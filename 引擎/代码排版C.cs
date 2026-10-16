using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary> 排版设置(第122批:C5 自动缩进 / 格式化 / 代码片段) </summary>
    public sealed class 排版设置C
    {
        /// <summary> 一档缩进几个空格(用制表符时忽略它) </summary>
        public int 缩进宽度 { get; set; } = 4;

        /// <summary> 用制表符缩进(默认用空格 —— 和标准示例一致) </summary>
        public bool 用制表符 { get; set; }

        /// <summary>
        /// 去行尾空格。**只去"这一行的行尾不在注释/字符串里"的那些**:
        /// `// 说明   ` 后面那几个空格属于注释文本,一个字都不动(验收要求"不改注释与字符串")。
        /// </summary>
        public bool 去行尾空格 { get; set; } = true;

        /// <summary> 上一行以续行符收尾 `= + - * / % &amp; | ^ &lt; &gt; , ( [ ? : .` 时,这一行再缩一档(多行语句看得清) </summary>
        public bool 续行再缩进 { get; set; } = true;

        /// <summary> 一档缩进的实际字符 </summary>
        public string 单位 => 用制表符 ? "\t" : new string(' ', Math.Clamp(缩进宽度, 1, 16));

        public override string ToString() =>
            (用制表符 ? "制表符缩进" : $"{Math.Clamp(缩进宽度, 1, 16)} 空格缩进")
            + (去行尾空格 ? " + 去行尾空格" : "") + (续行再缩进 ? " + 续行缩进" : "");
    }

    /// <summary> 一行改了什么(给"改之前看一眼"的预览用)</summary>
    public readonly record struct 排版改动C(int 行号, string 旧行, string 新行);

    /// <summary> 排版结果 </summary>
    public sealed class 排版结果C
    {
        public string 文本 { get; set; } = "";
        /// <summary> 改了几行(总账;**明细只留前几条**,大文件不至于为预览吃内存) </summary>
        public int 改了几行 { get; set; }
        /// <summary> 用的换行(原文是 CRLF 就还是 CRLF,是 LF 就还是 LF —— 不把整个文件的行尾换掉) </summary>
        public string 换行 { get; set; } = "\n";
        public List<排版改动C> 改动 { get; } = [];

        public bool 变了 => 改了几行 > 0;

        /// <summary> 给人看的预览(最多 最多条 条明细):`第 12 行:『    x』→『        x』` </summary>
        public string 预览(int 最多 = 10)
        {
            if (改了几行 == 0) return "排版:没有一行需要改(已经是排好的)。";
            var sb = new StringBuilder($"排版:共 {改了几行} 行要改;明细(最多 {最多} 条):");
            foreach (var 条 in 改动.Take(最多))
                sb.Append($"\n  第 {条.行号} 行:『{可看(条.旧行)}』→『{可看(条.新行)}』");
            if (改了几行 > 改动.Count) sb.Append($"\n  …还有 {改了几行 - 改动.Count} 行(不一条条列了)");
            return sb.ToString();
        }

        private static string 可看(string 行)
        {
            string 一 = 行.Replace("\t", "→").Replace(" ", "·").Replace("\r\n", "⏎").Replace("\n", "⏎");
            return 一.Length <= 40 ? 一 : 一[..20] + " … " + 一[^16..];
        }
    }

    /// <summary>
    /// 代码排版(第122批:C5)—— **只动空白,再就是把块括号挪到自己一行**;别的字符一个都不碰。
    ///
    /// 为什么这么定:语言里语句以 `;` 收尾、块用 `{}`,一行的前导空白对编译**没有任何影响**;
    /// 而"改格式但不改语义"最硬的验收就是"排版前后编译出来的机器码逐字节一致"。
    /// 所以这里不做"合并/补齐 `;`"这类会动语义的事(那是 `分号迁移C` 的活),
    /// 也不碰注释与字符串里的字符(注释与字面量区间按 `代码解析器.取保护改写区` 算,
    /// 落在里面的行**整行原样**、落在里面的行尾空格**不动**)。
    ///
    /// **块括号另起一行**(用户 2026-09-21 定版):`结构 点` 与 `{` 分两行、`{` 与配对的 `}`
    /// 同列、所有块头末尾的 `{` 一律挪到下一行,一行块 `如果 (真) { 输出("你好"); }` 也拆开。
    /// 认出"这是块括号"只看**它前面那个非空白字符**:紧跟在 `=` 后面的是**老写法**的花括号初值
    /// `{…}`(2026-10 已撤,`[ … ]` 才是初值)—— 排版**照旧不动它**,好让中间层报那句
    /// "初值一律用方括号";其余都算块括号(老初值里再嵌也按小状态机跳过)。拆行只**插换行**,
    /// 空白与缩进照旧由下面那套算,所以"拆行"与"重排缩进"互不干扰、连排两遍一模一样。
    /// 老写法(`{` 与块头同行)照样能编过 —— 语言面**只加不改**,排版只是把它摆成标准样式。
    ///
    /// 缩进规则(与标准示例的写法一致):
    ///   · 层级 = 这一行之前没配对的 `{` 个数(字符串/注释里的花括号不算);
    ///   · 行首是 `}` 的那一行先退一档(它关的是上一层);
    ///   · 上一行以续行符收尾 = 这一行还是同一句 → 再进一档;
    ///   · 空行不留空白;取行尾空格时"行尾在注释/字符串里"的不动。
    /// </summary>
    public static class 代码排版C
    {
        // ================= 对外:排版 =================

        /// <summary> 全篇排版(纯函数;幂等 —— 结果再排一遍一定不再变) </summary>
        public static 排版结果C 排版(string 文本, 排版设置C? 设置 = null)
        {
            设置 ??= new 排版设置C();
            var 结果 = new 排版结果C { 换行 = 检测换行(文本 ?? "") };
            if (string.IsNullOrEmpty(文本)) { 结果.文本 = 文本 ?? ""; return 结果; }

            string 头 = 文本[0] == '\uFEFF' ? "\uFEFF" : "";       // BOM 不属于第一行的缩进,别让它被顶走
            string 正文 = 头.Length > 0 ? 文本[1..] : 文本;
            string 归一 = 归一换行(正文);
            var 保护 = 取保护(归一);
            var 原行表 = 归一.Split('\n');
            // 块括号另起一行(用户 2026-09-21 定的写法):一个原行可能拆成好几个片段,
            // 每个片段都记着**它原来是第几行** —— 改动明细照旧按用户看得见的行号报。
            var 片段表 = 拆块括号(归一, 保护);
            var 行表 = 片段表.Select(x => x.片段).ToArray();
            保护 = 取保护(string.Join("\n", 行表));                  // 拆点是插换行,绝对下标全变了 ⇒ 重算一遍
            var 遮蔽 = 遮蔽行表(行表, 保护);
            var 行起 = 算行首偏移(行表);

            var 新行表 = new string[行表.Length];
            var 态 = new 排版状态(0, false, false, false);
            for (int i = 0; i < 行表.Length; i++)
            {
                string 原 = 行表[i];
                string? 目标 = 定前导(态, 原, 遮蔽[i], 保护, 行起[i], 设置);
                string 新 = 原;
                if (目标 != null)
                {
                    string 内容 = 原.TrimStart(' ', '\t');
                    新 = 内容.Length == 0 ? "" : 目标 + 内容;
                    if (内容.Length > 0 && 设置.去行尾空格 && !行尾在保护内(保护, 行起[i] + 原.Length))
                        新 = 新.TrimEnd(' ', '\t');
                }
                新行表[i] = 新;
                态 = 推进一步(态, 原, 遮蔽[i]);
            }

            // 归并回"原文件的一行算一条":拆出来的几行在预览里合成一条(`第 53 行:『结构 宽整数 {』→『结构 宽整数⏎{』`)
            var 新整表 = new List<string>(行表.Length);
            int 片段号 = 0;
            for (int 行 = 1; 行 <= 原行表.Length && 片段号 < 片段表.Count; 行++)
            {
                if (片段表[片段号].原行号 != 行) continue;
                var 块 = new List<string>();
                while (片段号 < 片段表.Count && 片段表[片段号].原行号 == 行) { 块.Add(新行表[片段号]); 片段号++; }
                string 新行 = string.Join(结果.换行, 块);
                if (!string.Equals(新行, 原行表[行 - 1], StringComparison.Ordinal))
                {
                    结果.改了几行++;
                    if (结果.改动.Count < 200) 结果.改动.Add(new 排版改动C(行, 原行表[行 - 1], 新行));
                }
                新整表.AddRange(块);
            }
            结果.文本 = 头 + string.Join(结果.换行, 新整表);
            return 结果;
        }

        // ================= 对外:一行该缩几档(编辑器回车/缩进用) =================

        /// <summary>
        /// 第 行号 行(1 基,已存在)**该有的前导空白**。
        /// 返回 null = 这一行整行在注释里 / 是顶格的整行注释(这类行原样不动)。
        /// </summary>
        public static string? 行目标缩进(string 文本, int 行号, 排版设置C? 设置 = null)
        {
            设置 ??= new 排版设置C();
            if (string.IsNullOrEmpty(文本) || 行号 <= 0) return "";
            string 归一 = 归一换行(文本);
            var 行表 = 归一.Split('\n');
            if (行号 > 行表.Length) return "";
            var 保护 = 取保护(归一);
            var 遮蔽 = 遮蔽行表(行表, 保护);
            var 行起 = 算行首偏移(行表);

            var 态 = new 排版状态(0, false, false, false);
            for (int i = 0; i < 行号 - 1; i++) 态 = 推进一步(态, 行表[i], 遮蔽[i]);
            return 定前导(态, 行表[行号 - 1], 遮蔽[行号 - 1], 保护, 行起[行号 - 1], 设置);
        }

        /// <summary>
        /// 在 光标行 末尾按回车之后,新开的那一行该缩几档。
        /// 新行**还不存在**(所以不能拿它当"空行"判),规则:块里进一档、标签体进一档、话没说完进一档。
        /// 光标行本身是空行 → 新行不缩进(免得连按回车越缩越深)。
        /// </summary>
        public static string 回车后的缩进(string 文本, int 光标行, 排版设置C? 设置 = null)
        {
            设置 ??= new 排版设置C();
            if (string.IsNullOrEmpty(文本) || 光标行 <= 0) return "";
            string 归一 = 归一换行(文本);
            var 行表 = 归一.Split('\n');
            if (光标行 <= 行表.Length && 行表[光标行 - 1].Trim().Length == 0) return "";

            var 保护 = 取保护(归一);
            var 遮蔽 = 遮蔽行表(行表, 保护);
            var 态 = new 排版状态(0, false, false, false);
            for (int i = 0; i < Math.Min(光标行, 行表.Length); i++) 态 = 推进一步(态, 行表[i], 遮蔽[i]);
            return 新行前导(态, 设置);
        }

        /// <summary>
        /// **新行(刚按回车建出来的那一行)该用的前导空白**(C16-4)。回车有两种情形,判据得分开:
        /// <list type="number">
        /// <item>新行是**空的**(在行尾按回车):没有内容可看,按"上一个块里进一档"算(= `回车后的缩进`);</item>
        /// <item>新行**带着原文的尾巴**(在行中间按回车 —— 用户实测的那一幕:光标停在 `{/*…*/}` 的 `}` 前面):
        ///       那就按**这一行自己的写法**算(= `行目标缩进`):`}` 退一档、整行注释跟着层级、顶格分节线不动。</item>
        /// </list>
        /// 老写法只有第 ① 种(`回车后的缩进` 按"新行还不存在"设计),界面那边还要"行首不是空白就 return",
        /// 于是第 ② 种里拆出来的 `}` 一直停在 0 列、与它的 `{` 对不上(用户 2026-09-19 实测报的)。
        /// 返回 null = 这一行**原样不动**(整行在注释/字符串里、顶格分节线、断开的标签体)。
        /// </summary>
        public static string? 回车后前导(string 文本, int 新行号, 排版设置C? 设置 = null)
        {
            设置 ??= new 排版设置C();
            if (string.IsNullOrEmpty(文本) || 新行号 <= 0) return "";
            string 归一 = 归一换行(文本);
            var 行表 = 归一.Split('\n');
            if (新行号 > 行表.Length) return "";
            if (行表[新行号 - 1].Trim().Length == 0) return 回车后的缩进(文本, 新行号 - 1, 设置);
            return 行目标缩进(文本, 新行号, 设置);
        }

        // ================= 内部:算一行该缩几档 =================

        /// <summary>
        /// 排版要看的状态(**只看这一行之前的东西**,所以"排版整篇"与"只问某一行"算出来一模一样):
        /// `深度` = 这一行之前没配对的 `{` 个数;`上一行续行` = 上一行以 = 运算符/逗号收尾(话没说完);
        /// `标签体` = 现在还在某个 `标签:` 的体里(汇编层子程序:标签下一档,直到下一个标签);
        /// `标签体断了` = 这个标签体中间出现过空行 —— 空行之后作者可能是"另起一节"(示例里两种写法都有),
        /// 这时候**不替作者拿主意**:那几行原样不动(见 `定前导`)。
        /// </summary>
        private readonly record struct 排版状态(int 深度, bool 上一行续行, bool 标签体, bool 标签体断了);

        /// <summary> 走完一行:数括号、判"这一行是不是标签"、判"话说完没有" </summary>
        private static 排版状态 推进一步(排版状态 态, string 原行, string 遮蔽行)
        {
            int 深度 = Math.Max(0, 态.深度 + 数括号(遮蔽行));
            string 遮修 = 遮蔽行.Trim();
            bool 空行 = 遮修.Length == 0;
            // 标签行:`名字:`(判据与编译器"标签行"一致,不会把 `情况 1` 认成标签)
            bool 是标签 = !空行 && 中间层展开器.是标签行(遮修);
            bool 续 = !空行 && 以续行符结尾(遮蔽行);       // 纯注释行(遮蔽后是空的)不算"话没说完"
            if (是标签) return new 排版状态(深度, 续, 标签体: true, 标签体断了: false);
            return new 排版状态(深度, 续, 态.标签体, 态.标签体 && (态.标签体断了 || 空行));
        }

        /// <summary>
        /// 这一行该用哪个前导空白。null = **原样不动**:
        /// ① 整行在注释里(多行 `/* */` 的里面那几行);
        /// ② 顶格的整行注释(示例把这种当"分节线",一个空格都不改);
        /// ③ 汇编层标签体被空行断开之后的那些行(作者可能是另起一节,不替他拿主意)。
        /// </summary>
        private static string? 定前导(排版状态 态, string 原行, string 遮蔽行,
            List<(int 开始, int 结束)> 保护, int 本行首, 排版设置C 设置)
        {
            if (保护.Any(区 => 区.开始 < 本行首 && 本行首 < 区.结束)) return null;

            string 缩进(int 档) => string.Concat(Enumerable.Repeat(设置.单位, Math.Max(0, 档)));
            string 内容 = 原行.TrimStart(' ', '\t');
            string 遮修 = 遮蔽行.Trim();
            int 基础 = Math.Max(0, 态.深度);
            int 体档 = 态.标签体 && !态.标签体断了 ? 基础 + 1 : 基础;

            if (遮修.Length == 0)
            {
                if (内容.Length == 0) return "";                                  // 真空行:不留空白
                if (内容.Length == 原行.Length) return null;                       // 顶格整行注释:分节线,不动
                return 态.标签体 && 态.标签体断了 ? null : 缩进(体档);              // 缩进过的整行注释:跟着层级
            }
            if (态.标签体 && 态.标签体断了) return null;                            // 断开的标签体:原样
            if (内容[0] == '}') return 缩进(体档 - 1);                              // 收尾括号:退一档(标签体里退的是它自己那一档)
            if (中间层展开器.是标签行(遮修)) return 缩进(基础);                     // 标签本身跟块深度平齐
            if (态.标签体) return 缩进(基础 + 1);                                  // 标签体:下一档
            if (设置.续行再缩进 && 态.上一行续行) return 缩进(基础 + 1);             // 话没说完:下一档
            return 缩进(基础);
        }

        /// <summary> 一个**新行**(还没写进文本,所以不用管"它是不是空行")该用的前导空白 </summary>
        private static string 新行前导(排版状态 态, 排版设置C 设置)
        {
            string 缩进(int 档) => string.Concat(Enumerable.Repeat(设置.单位, Math.Max(0, 档)));
            if (态.标签体 && 态.标签体断了) return "";          // 断开的标签体:不替作者拿主意
            int 基础 = Math.Max(0, 态.深度);
            if (态.标签体) return 缩进(基础 + 1);
            if (设置.续行再缩进 && 态.上一行续行) return 缩进(基础 + 1);
            return 缩进(基础);
        }

        /// <summary> 这个位置是不是落在某个"注释/字符串"区间里面 </summary>
        private static bool 行尾在保护内(List<(int 开始, int 结束)> 保护, int 位置) =>
            保护.Any(区 => 区.开始 < 位置 && 位置 <= 区.结束);

        /// <summary> 每行行首在归一文本里的下标(行表是把 '\n' 切掉拼出来的,所以 +1 补回换行) </summary>
        private static int[] 算行首偏移(string[] 行表)
        {
            var 行起 = new int[行表.Length];
            for (int i = 0, p = 0; i < 行表.Length; i++) { 行起[i] = p; p += 行表[i].Length + 1; }
            return 行起;
        }

        // ================= 内部:块括号另起一行 =================

        /// <summary>
        /// 把**块括号**拆到自己一行(用户 2026-09-21 定版:所有 `{` 另起一行、与配对的 `}` 对称)。
        ///
        /// 判"是不是块括号"只看**它前面那个非空白字符**:紧跟在 `=` 后面的是**老写法**的花括号初值
        /// `{…}`(2026-10 已撤)—— 排版照旧不动它(留给中间层报"初值要用方括号"),其余都算块括号;
        /// 老初值里再嵌初值按 `初值深` 那个小状态机跳过。
        /// 只在**非空白的另一侧**断开(不会拆出空行);注释与字符串里的 `{}` 由保护区间挡掉。
        /// 只插换行符,**空白与缩进全交给后面那道老规矩**。
        /// 返回的每个片段都带着**它原来是第几行**(1 基)—— 一个原行可能拆成好几个片段,
        /// 改动明细要按用户看得见的行号报,不能报"拆完之后的第几行"。
        /// </summary>
        private static List<(int 原行号, string 片段)> 拆块括号(string 归一文本, List<(int 开始, int 结束)> 保护)
        {
            var 行表 = 归一文本.Split('\n');
            var 结果 = new List<(int, string)>(行表.Length);
            if (归一文本.Length == 0) { 结果.Add((1, "")); return 结果; }

            // 每个字符在不在保护区里(注释 / 字符串 / 字符字面量):一次摊平,行内查得快
            var 受保护 = new bool[归一文本.Length];
            foreach (var (起, 止) in 保护)
                for (int p = Math.Max(0, 起); p < Math.Min(止, 受保护.Length); p++) 受保护[p] = true;

            int 行起 = 0;
            for (int 行号 = 1; 行号 <= 行表.Length; 行号++)
            {
                string 行 = 行表[行号 - 1];
                // H68-2:枚举声明(`枚举 颜色 { 赤, 绿, 蓝 }`)的 `{ }` 是**成员表**,不是语句块 ——
                // 整行原样留着(拆了会把"一行写完"这种写法改成好几行,行数一变就不是"只动空白"了)
                if (是枚举行(行)) { 结果.Add((行号, 行)); 行起 += 行.Length + 1; continue; }
                var 断 = new SortedSet<int>();
                int 初值深 = 0;
                for (int k = 0; k < 行.Length; k++)
                {
                    if (受保护[行起 + k]) continue;
                    char c = 行[k];
                    if (c == '}')
                    {
                        if (初值深 > 0) { 初值深--; continue; }              // 初值收尾:不算块括号
                        if (有非空白(行, 0, k)) 断.Add(k);
                        if (有非空白(行, k + 1, 行.Length)) 断.Add(k + 1);
                    }
                    else if (c == '{')
                    {
                        if (初值深 > 0) { 初值深++; continue; }              // 初值里嵌初值
                        if (上一非空白(行, k) == '=') { 初值深 = 1; continue; }   // `= {…}` 是老写法(已撤):排版不动它
                        if (有非空白(行, 0, k)) 断.Add(k);
                        if (有非空白(行, k + 1, 行.Length)) 断.Add(k + 1);
                    }
                }

                if (断.Count == 0) { 结果.Add((行号, 行)); }
                else
                {
                    int 上 = 0;
                    foreach (int 位 in 断)
                        if (位 > 上) { 结果.Add((行号, 行[上..位])); 上 = 位; }
                    if (上 < 行.Length) 结果.Add((行号, 行[上..]));
                }
                行起 += 行.Length + 1;
            }
            return 结果;
        }

        /// <summary> `行[起, 止)` 里有没有非空白字符(用来判"这一侧还站着东西,得断在这儿") </summary>
        private static bool 有非空白(string 行, int 起, int 止)
        {
            for (int i = Math.Max(0, 起); i < Math.Min(止, 行.Length); i++)
                if (!char.IsWhiteSpace(行[i])) return true;
            return false;
        }

        /// <summary>
        /// H68-2:这一行是**枚举声明**(`枚举 颜色 { 赤, 绿, 蓝 }` / `enum 颜色 { … }`)吗。
        /// 它的 `{ }` 是成员表 —— 排版不许把它当语句块拆开(拆了行数就变,而"排版只动空白"是硬口径)。
        /// </summary>
        private static bool 是枚举行(string 行)
        {
            string t = 行.TrimStart(' ', '\t');
            if (t.Length == 0) return false;
            int 止 = 0;
            while (止 < t.Length && !char.IsWhiteSpace(t[止]) && t[止] != '{') 止++;
            string 首词 = t[..止];
            bool 是枚举起头 = string.Equals(首词, "枚举", StringComparison.Ordinal)
                || string.Equals(首词, "enum", StringComparison.OrdinalIgnoreCase);
            return 是枚举起头 && 止 < t.Length;
        }

        /// <summary> `行[位]` 前面第一个非空白字符(没有就给 '\0' —— 判"`{` 是不是紧跟 `=`") </summary>
        private static char 上一非空白(string 行, int 位)
        {
            for (int i = 位 - 1; i >= 0; i--)
                if (!char.IsWhiteSpace(行[i])) return 行[i];
            return '\0';
        }

        // ================= 内部:换行与保护区间 =================

        /// <summary> 原文用哪种换行(有 CRLF 就按 CRLF 输出;都没有就是 LF) </summary>
        public static string 检测换行(string 文本) =>
            !string.IsNullOrEmpty(文本) && 文本.Contains("\r\n") ? "\r\n" : "\n";

        /// <summary> 把 CRLF / 老 Mac 的 CR 都归成 LF(算下标用) </summary>
        public static string 归一换行(string 文本) =>
            (文本 ?? "").Replace("\r\n", "\n").Replace('\r', '\n');

        /// <summary> 括号 / 分号 / 续行符都不看字符串与注释里的 —— 先把保护区换成空格 </summary>
        private static string[] 遮蔽行表(string[] 行表, List<(int 开始, int 结束)> 保护)
        {
            var 遮蔽 = (string[])行表.Clone();
            if (保护.Count == 0) return 遮蔽;
            var 行起 = 算行首偏移(行表);

            foreach (var (起, 止) in 保护)
            {
                for (int i = 0; i < 行表.Length; i++)
                {
                    int 首 = 行起[i], 尾 = 首 + 行表[i].Length;
                    if (尾 <= 起) continue;
                    if (首 >= 止) break;
                    int a = Math.Max(起, 首) - 首, b = Math.Min(止, 尾) - 首;
                    if (b <= a) continue;
                    var 字 = 遮蔽[i].ToCharArray();
                    for (int k = a; k < b; k++) 字[k] = ' ';
                    遮蔽[i] = new string(字);
                }
            }
            return 遮蔽;
        }

        private static List<(int 开始, int 结束)> 取保护(string 归一文本)
        {
            try { return 代码解析器.取保护改写区(归一文本); }
            catch { return []; }
        }

        /// <summary> 花括号净数(传入的已经是"保护区内换成空格"的行)</summary>
        private static int 数括号(string 遮蔽行)
        {
            int 净 = 0;
            foreach (char c in 遮蔽行)
            {
                if (c == '{') 净++;
                else if (c == '}') 净--;
            }
            return 净;
        }

        /// <summary> 这一行是不是"话没说完"(= 运算符 / 逗号 / 开括号收尾)—— 与编译器合句同一套判据 </summary>
        private static bool 以续行符结尾(string 遮蔽行)
        {
            string t = 遮蔽行.TrimEnd();
            if (t.Length == 0) return false;
            if (t.EndsWith("++", StringComparison.Ordinal) || t.EndsWith("--", StringComparison.Ordinal)) return false;
            return "=+-*/%&|^<>,([?:.".Contains(t[^1]);
        }
    }

    /// <summary> 一个代码片段(骨架):`触发` 是它的名字;`正文` 里 `◆` 标"插完光标停这儿" </summary>
    public sealed class 片段项C
    {
        public string 触发 { get; set; } = "";
        public string 标题 { get; set; } = "";
        public string 说明 { get; set; } = "";
        public string 正文 { get; set; } = "";

        /// <summary> 光标记号(`正文` 里标"插完停在哪儿";展开后会去掉) </summary>
        public const char 光标记号 = '◆';

        public override string ToString() => $"{触发} —— {说明}";
    }

    /// <summary>
    /// 常用代码片段(骨架)—— 引擎侧数据,界面只负责"插到光标处"。
    /// 写法一律照**标准示例**(中文关键字、`函数 … 返回 类型`、块用 `{}`),块括号**另起一行**
    /// (用户 2026-09-21 定版:所有 `{` 与配对的 `}` 同列、上下对称),
    /// 这样插出来的骨架不用改就能编过、也不用再排版(`代码排版自检` 会拿编译器真编一遍钉住)。
    /// </summary>
    public static class 片段表C
    {
        public static readonly IReadOnlyList<片段项C> 全部 =
        [
            new 片段项C
            {
                触发 = "方法", 标题 = "方法(带返回值)", 说明 = "方法骨架:参数 + 返回值 + 一行 返回",
                正文 = "方法 名字(整数 参数) 返回 整数\n{\n    ◆返回 0;\n}",
            },
            new 片段项C
            {
                触发 = "过程", 标题 = "方法(无返回值)", 说明 = "只干活不返回值的方法",
                正文 = "方法 名字(整数 参数)\n{\n    ◆\n}",
            },
            new 片段项C
            {
                触发 = "如果", 标题 = "如果", 说明 = "单分支",
                正文 = "如果 (条件)\n{\n    ◆\n}",
            },
            new 片段项C
            {
                触发 = "如果否则", 标题 = "如果 / 否则", 说明 = "双分支",
                正文 = "如果 (条件)\n{\n    ◆\n}\n否则\n{\n}",
            },
            new 片段项C
            {
                触发 = "当", 标题 = "当(while)", 说明 = "条件循环",
                正文 = "当 (条件)\n{\n    ◆\n}",
            },
            new 片段项C
            {
                触发 = "循环", 标题 = "循环(for)", 说明 = "三段式循环(写法照示例:var i = 0; i < N; i = i + 1)",
                正文 = "循环 (var i = 0; i < 10; i = i + 1)\n{\n    ◆\n}",
            },
            new 片段项C
            {
                触发 = "遍历", 标题 = "遍历", 说明 = "按『当前字节, 起始地址, 数量』遍历一段内存",
                正文 = "遍历 (当前, 起始 游标, 数量 剩余)\n{\n    ◆\n}",
            },
            new 片段项C
            {
                触发 = "选择", 标题 = "选择(switch)", 说明 = "多分支:情况 + 默认",
                正文 = "选择 (值)\n{\n    情况 1\n    {\n        ◆\n    }\n    默认\n    {\n    }\n}",
            },
            new 片段项C
            {
                触发 = "结构", 标题 = "结构(struct)", 说明 = "纯数据类型:字段按声明顺序紧排",
                正文 = "结构 名字\n{\n    整数 字段;\n    ◆\n}",
            },
            new 片段项C
            {
                触发 = "测试", 标题 = "测试", 说明 = "测试块(里面写 断言)",
                正文 = "测试 名字\n{\n    断言 ◆;\n}",
            },
            new 片段项C
            {
                触发 = "循环列表", 标题 = "循环遍历列表", 说明 = "按下标走列表一圈(上界用 .长度())",
                正文 = "循环 (var i = 0; i < 列表.长度(); i = i + 1)\n{\n    ◆\n}",
            },
        ];

        /// <summary> 按触发词找(不区分大小写);找不到返回 null </summary>
        public static 片段项C? 按触发(string 词) =>
            string.IsNullOrEmpty(词) ? null
            : 全部.FirstOrDefault(x => string.Equals(x.触发, 词, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// 展开片段:整体先按 `缩进` 起头(插在哪儿就跟着哪儿的层级),
        /// 返回文本并把 `◆` 去掉;`光标偏移` = 展开后光标该停的下标(相对返回的文本)。
        /// </summary>
        public static string 展开(片段项C 项, string 缩进, out int 光标偏移)
        {
            缩进 ??= "";
            string[] 行表 = 代码排版C.归一换行(项?.正文 ?? "").Split('\n');
            var sb = new StringBuilder();
            int 光 = -1;
            for (int i = 0; i < 行表.Length; i++)
            {
                string 行 = 行表[i];
                if (i > 0) sb.Append('\n');
                int 记号 = 行.IndexOf(片段项C.光标记号);
                if (记号 >= 0) { 光 = sb.Length + 缩进.Length + 记号; 行 = 行.Remove(记号, 1); }
                if (行.Trim().Length == 0) continue;              // 空行不留缩进
                sb.Append(缩进).Append(行);
            }
            if (光 < 0) 光 = sb.Length;                            // 没标记号就停在末尾
            光标偏移 = Math.Clamp(光, 0, sb.Length);
            return sb.ToString();
        }

        /// <summary> 展开(不关心光标) </summary>
        public static string 展开(片段项C 项, string 缩进 = "") => 展开(项, 缩进, out _);

        /// <summary> 给人看的一份清单(命令行 `--片段库` 与帮助文本用) </summary>
        public static string 清单()
        {
            var sb = new StringBuilder($"代码片段 {全部.Count} 条(写法与标准示例一致;`{片段项C.光标记号}` = 插完光标停这儿):");
            foreach (var 项 in 全部)
                sb.Append($"\n  {项.触发,-10} {项.标题,-16} {项.说明}");
            return sb.ToString();
        }
    }
}
