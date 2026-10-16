namespace hehshihbeen_compiler
{
    /// <summary>
    /// **花括号的嵌套层号**(C16,用户 2026-09-19 报的第三条:"增加 `{}` 嵌套颜色,也用三分之二饱和度的彩虹色
    /// (一层不用高亮色)")。这一层只算**规则**:每个 `{` / `}` 在第几层、那一层该用哪个色档;
    /// 界面(`AvalonInlineHighlighter`)拿它把这两个字符涂上色。
    ///
    /// **为什么放引擎**:层号是规则,不是观感 —— 规则要能被自检直接喂源码钉住(与 `折叠区C` 同一个理由)。
    ///
    /// **扫描口径与 `折叠区C` 完全一致(不写第二套)**:注释区 ∪ 字符串/字符字面量走
    /// `代码解析器.取保护改写区`,里面的 `{` / `}` 一律不算 —— 不认这一条,`输出行("{")` 与
    /// `// 注释里的 }` 就会把后面所有行的层号带偏。
    ///
    /// **层号规则**:
    /// <list type="number">
    /// <item>`{` **自己**占的层 = 遇到它**之前**已经开了几层 + 1(第一个 `{` 是第 1 层);</item>
    /// <item>`}` **与它配对的 `{`** 同层(先减再记,所以一对花括号画出来是同一个颜色);</item>
    /// <item>多出来的 `}`(没有 `{` 可配)按第 1 层算 —— 半路写的代码不该把整篇颜色带乱;</item>
    /// <item>**第 1 层就上色**(C16-2,用户 2026-09-19 改口径:原来拍板"一层不用高亮色",现在改成
    ///       "彩虹色从第一层开始"),7 档彩虹循环(与 C15 调用叠色同一套色相):第 1 层赤、
    ///       第 8 层又回到赤(7 档一轮)。</item>
    /// </list>
    ///
    /// 纯函数 + 一次扫完:一次 O(全篇长度) 扫描顺手记下"每一行开头在第几层"与"这一行里每个括号的列号 + 层号",
    /// 界面那边只查看得见的那几十行(见 `AvalonInlineHighlighter`),不整篇重扫。
    /// </summary>
    public static class 花括号层号C
    {
        /// <summary> 一个花括号字符:**行内列号**(0 基,不含换行符)+ 它在第几层(1 基) </summary>
        public readonly record struct 括号位C(int 列, int 层号);

        /// <summary>
        /// 一次扫完的结果:`每行开头在第几层`(下标 0 = 第 1 行)+ 行号(1 基)→ 这一行里的花括号。
        /// **只给查询用**,别自己改这两个表(见 `取层号` / `取行括号`)。
        /// </summary>
        public sealed class 层号表C
        {
            /// <summary> 第 n 行(1 基)**开头**时已经开了几层(1 基;空文本是 1) </summary>
            public int[] 每行开头层号 { get; }

            /// <summary> 行号(1 基)→ 这一行里 `{` / `}` 的(列号, 层号);没有括号的行不占键 </summary>
            public IReadOnlyDictionary<int, List<括号位C>> 行括号 { get; }

            internal 层号表C(int[] 每行开头层号, IReadOnlyDictionary<int, List<括号位C>> 行括号)
            {
                this.每行开头层号 = 每行开头层号;
                this.行括号 = 行括号;
            }
        }

        /// <summary>
        /// 扫一遍正文,算出每个 `{` / `}` 的层号 + 每一行开头在第几层。
        /// 认 `\r\n`(与 AvalonEdit 的 `DocumentLine` 同口径);算不出来绝不抛异常(观感功能,出错就当没有)。
        /// </summary>
        public static 层号表C 扫(string? 正文)
        {
            string 文本 = 正文 ?? "";
            var 行括号 = new Dictionary<int, List<括号位C>>();
            var 每行开头 = new List<int> { 1 };
            int 层 = 1;                     // 当前开着几层(1 基;第一个 `{` 自己就是第 1 层)
            int 行号 = 1;
            int 列 = 0;                     // 行内列号(换行符不算进去)
            if (文本.Length == 0) return new 层号表C([.. 每行开头], 行括号);

            List<(int 开始, int 结束)> 保护;
            try { 保护 = 代码解析器.取保护改写区(文本); }
            catch { 保护 = []; }
            int 保护号 = 0;

            for (int 位 = 0; 位 < 文本.Length; 位++)
            {
                char c = 文本[位];
                if (c == '\n') { 行号++; 列 = 0; 每行开头.Add(层); continue; }
                if (c == '\r') continue;                        // `\r\n` 与单个 `\r` 都不算列

                while (保护号 < 保护.Count && 位 >= 保护[保护号].结束) 保护号++;
                if (保护号 < 保护.Count && 位 >= 保护[保护号].开始)
                {
                    列++;                                       // 保护区里的字符照数(只不上色)
                    continue;
                }

                if (c == '{')
                {
                    记(行括号, 行号, 列, 层);
                    层++;
                }
                else if (c == '}')
                {
                    层 = Math.Max(1, 层 - 1);
                    记(行括号, 行号, 列, 层);
                }
                列++;
            }
            return new 层号表C([.. 每行开头], 行括号);
        }

        /// <summary> 第 `行号`(1 基)开头在第几层;行号越界就按第 1 层算(不抛) </summary>
        public static int 取层号(层号表C? 表, int 行号)
        {
            if (表 == null || 行号 < 1 || 行号 > 表.每行开头层号.Length) return 1;
            return 表.每行开头层号[行号 - 1];
        }

        /// <summary> 第 `行号`(1 基)里的花括号(没有就返回空表,不返回 null) </summary>
        public static List<括号位C> 取行括号(层号表C? 表, int 行号)
        {
            if (表 != null && 表.行括号.TryGetValue(行号, out var 一位)) return 一位;
            return [];
        }

        /// <summary>
        /// 层号(1 基)→ 色档(0 基),7 档循环。**第 1 层就是色档 0(赤)** —— C16-2 用户 2026-09-19 改口径:
        /// 原来拍板"一层不用高亮色",现在改成"彩虹色从第一层开始",所以**不再有 `-1 = 不上色` 这一档**
        /// (接口跟着简单了:返回值恒在 `0~6`,界面那边不必再判空)。越界(0 层 / 负数)也按第 1 层算。
        /// </summary>
        public static int 取色档(int 层号)
        {
            return (Math.Max(1, 层号) - 1) % 7;
        }

        /// <summary>
        /// 色档(0 基,自动按 7 循环)→ `#RRGGBB`。色相沿用 C15 那一套(赤橙黄绿青蓝紫),饱和度压到 2/3。
        /// 7 档都是**同一个亮度 0.5** —— 亮/暗两套主题下都看得清(而不是"照某一张底调出来的色")。
        /// **这是"引擎默认色"**:H8 起选项页里能逐档改,实际生效的那份走 `规范色表`(没改过就逐档等于这里)。
        /// </summary>
        public static string 取色(int 档)
        {
            int 归一 = ((档 % 7) + 7) % 7;
            double 色相 = 归一 switch
            {
                0 => 0.0,      // 赤
                1 => 30.0,     // 橙
                2 => 58.0,     // 黄
                3 => 130.0,    // 绿
                4 => 186.0,    // 青
                5 => 224.0,    // 蓝
                _ => 278.0,    // 紫
            };
            return 三分之二饱和度(色相);
        }

        /// <summary> 7 档配色的亮色(第 1 层赤,7 档一轮 —— C16-2 起第 1 层也上色) </summary>
        public static readonly string[] 色表 =
            [取色(0), 取色(1), 取色(2), 取色(3), 取色(4), 取色(5), 取色(6)];

        /// <summary> 7 档的名字(赤橙黄绿青蓝紫,与 `取色` 的色相一一对应;选项页按它给 7 行起名) </summary>
        public static readonly string[] 色档名 = ["赤", "橙", "黄", "绿", "青", "蓝", "紫"];

        /// <summary> 一句话说明"这些色是怎么来的"(选项页/文档要能对上,自检也按它验) </summary>
        public const string 配色说明 = "赤橙黄绿青蓝紫 7 档,每档按 HLS 把饱和度压到原来的 2/3(亮度保持 0.5)";

        /// <summary>
        /// H8(用户 2026-09-24 点名"外观项进选项页"):把选项里那份**用户调过的 7 档色**规范成
        /// "7 档都认得出颜色、而且一律是 `#RRGGBB`"的一张表 —— **哪一档空着 / 写坏了 / 表不够长,
        /// 就用 `色表` 里那一档顶上**,绝不返回 null,也不让一个错字把整篇彩虹色弄没。
        ///
        /// **为什么必须归一成 `#RRGGBB`**:缩进导引线要把透明度拼在色值前面
        /// (`#AARRGGBB`,见 `导引线透明度`),写的是"取 `色[1..]`" —— 用户要是填 `#RGB` 或
        /// `#AARRGGBB`,不归一那一刀就切错位。规则留在这里(纯函数),命令行自检也验得了;
        /// 界面只负责把这张表拿去转画刷。
        /// </summary>
        public static string[] 规范色表(IReadOnlyList<string>? 用户表)
        {
            var 结果 = new string[色表.Length];
            for (int i = 0; i < 结果.Length; i++)
            {
                string? 值 = 用户表 != null && i < 用户表.Count ? 用户表[i] : null;
                var (r, g, b) = 配色词类C.取RGB(值);
                结果[i] = r < 0 ? 色表[i] : $"#{r:X2}{g:X2}{b:X2}";
            }
            return 结果;
        }

        /// <summary>
        /// C16-3:缩进导引线用**同一套 7 档色**画,只是压一点透明度(`#AARRGGBB` 的 A)——
        /// 线跟着括号那一层的颜色走(用户 2026-09-19 原话:"不是跟随括号颜色么"),
        /// 又不能把代码盖住。第一版是一条半透明灰(`0x3C`),用户看过说"太淡",所以提到这里。
        /// </summary>
        public const byte 导引线透明度 = 0xB4;

        /// <summary> 把 HLS 的饱和度压到 2/3、亮度 = 0.5,返回 `#RRGGBB` </summary>
        private static string 三分之二饱和度(double 色相)
        {
            const double 饱和度 = 2.0 / 3.0;
            const double 亮度 = 0.5;
            double c = (1 - Math.Abs(2 * 亮度 - 1)) * 饱和度;
            double h = ((色相 % 360) + 360) % 360 / 60.0;
            double x = c * (1 - Math.Abs(h % 2 - 1));
            double m = 亮度 - c / 2;
            var (r, g, b) = ((int)h, h) switch
            {
                (0, _) => (c, x, 0.0),
                (1, _) => (x, c, 0.0),
                (2, _) => (0.0, c, x),
                (3, _) => (0.0, x, c),
                (4, _) => (x, 0.0, c),
                _ => (c, 0.0, x),
            };
            return $"#{通道(r + m):X2}{通道(g + m):X2}{通道(b + m):X2}";

            static int 通道(double 值) => Math.Clamp((int)Math.Round(值 * 255, MidpointRounding.AwayFromZero), 0, 255);
        }

        private static void 记(Dictionary<int, List<括号位C>> 行括号, int 行号, int 列, int 层)
        {
            if (!行括号.TryGetValue(行号, out var 一位)) { 一位 = []; 行括号[行号] = 一位; }
            一位.Add(new 括号位C(列, 层));
        }
    }
}
