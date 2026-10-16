using System.IO;
using System.Text.RegularExpressions;
using ToolGood.Words.FirstPinyin;

namespace hehshihbeen_compiler
{
    /// <summary> 候选的来历(只在排序时用:同分时"语句"排在"名字"前面) </summary>
    public enum 候选来历 { 方法 = 0, 内建函数 = 1, 关键字 = 2, 片段 = 3, 类型 = 4, 指令 = 5, 名字 = 6, 字段 = 7 }

    /// <summary>
    /// 一条候选:`词` 是显示与匹配用的名字;`插入` 是真正写进代码的**干净文本**;
    /// `英文` 是同一个概念的另一语言名(英文前缀匹配用);`括号内落光标` = 插入后光标停在括号里;
    /// `说明` = 候选框右侧那行小字(C19:变量的类型、函数的参数表、结构字段的类型 —— 也就是"参数提示")。
    /// **`插入` 里不带尾空格**(C16-1):同一个候选按 Tab 与按空格要两种结果,
    /// 所以尾空格由"确认时按了哪个键"决定 —— 见 `补全词表C.确认文本`。
    /// `片段`(C28)= 这条候选是**代码片段骨架**时,指向 `片段表C` 里那一项
    /// (显示成 `如果{…}`、插进去的是按当前层级缩进好的整段骨架 —— 走 `片段插入C.算插入`)。
    /// </summary>
    public sealed record 补全候选C(string 词, 候选来历 来历, string 插入, string? 英文 = null, bool 括号内落光标 = false,
        string? 说明 = null, 片段项C? 片段 = null)
    {
        /// <summary>
        /// 匹配时用哪个名字(C28):片段候选显示的是 `如果{…}`、匹配要按**触发词** `如果` 算
        /// (不然拼音首字母会把 `{…}` 一起算进去);别的候选就是 `词` 自己。
        /// </summary>
        public string 匹配名 => 片段?.触发 ?? 词;
    }

    /// <summary>
    /// 智能补全的**唯一事实源**(编辑器与手机 IDE 共用):
    ///   ① 固定词表 —— 汇编指令(中英)、语言关键字(中英)、类型名、内建函数、方法;
    ///   ② 拼音首字母 —— 用 ToolGood.Words.FirstPinyin 现算(带缓存),所以 `hs` 能出 `函数`、`cd` 能出 `长度`;
    ///   ③ 工程里的名字 —— 从源码里扫 `函数/结构/方法/静态 方法/变量声明`,含进来的库文件也扫。
    /// 匹配顺序(分数小的优先):完全相等 → 中文前缀 → 英文前缀 → 拼音首字母前缀。
    /// **一个需求一个入口**:界面端只管"取候选 → 弹窗",匹配规则全在这里(两端行为一致)。
    /// </summary>
    public static class 补全词表C
    {
        // ---------------- 固定词表 ----------------

        private static readonly 补全候选C[] _固定候选 = 造固定词表();

        /// <summary> 固定候选:指令(中英)、关键字(中英)、类型名、内建函数、方法 </summary>
        public static IReadOnlyList<补全候选C> 固定候选 => _固定候选;

        /// <summary> 固定候选里"方法"那一档(长度/清空/添加,中英各 3):刚敲一个 `.` 时至少给这些。</summary>
        public static IReadOnlyList<补全候选C> 固定方法候选 { get; } =
            [.. _固定候选.Where(x => x.来历 == 候选来历.方法)];

        private static 补全候选C[] 造固定词表()
        {
            var 表 = new List<补全候选C>();
            var 见过 = new HashSet<string>(StringComparer.Ordinal);

            void 加(string 词, 候选来历 来历, string 插入, string? 英文 = null, bool 括号内落光标 = false)
            {
                if (词.Length == 0) return;
                if (!见过.Add(词 + "|" + 来历)) return;
                表.Add(new 补全候选C(词, 来历, 插入, 英文, 括号内落光标));
            }

            // ① 方法:字符串/列表都只有这三个(与 中间层C 里那张"列表动作"表一一对应)。
            //    中英两条都登记(第96批加英文别名):写 `.长度()` 与写 `.length()` 都能编译。
            foreach (var (中文, 英文) in 中间层指令C.方法名表)
            {
                加(中文, 候选来历.方法, 中文 + "()", 英文, 括号内落光标: true);
                加(英文, 候选来历.方法, 英文 + "()", 中文, 括号内落光标: true);
            }

            // ② 内建函数:读输入三兄弟 + 堆的 申请/释放 + 宿主能力(P28)。
            //    词表只有一份 —— 中间层指令C.内建函数名表(中英两侧都登记,中文词反查英文名,反之亦然);
            //    `取字`/`存字` 与 ISA 指令同名,已经在指令那一档里了,这里不再重复登记。
            var 指令词表 = new HashSet<string>(指令C.取全部指令名(), StringComparer.Ordinal);
            foreach (var (中文, 英文) in 中间层指令C.内建函数名表)
            {
                if (!指令词表.Contains(中文)) 加(中文, 候选来历.内建函数, 中文 + "()", 英文, 括号内落光标: true);
                if (!指令词表.Contains(英文)) 加(英文, 候选来历.内建函数, 英文 + "()", 中文, 括号内落光标: true);
            }

            // ③ 语言关键字(中英各一条:用户切到哪种语言就选哪条)
            foreach (var (中文, 英文) in 中间层指令C.关键字表)
            {
                加(中文, 候选来历.关键字, 中文, 英文);
                string 上英 = 英文;
                加(上英, 候选来历.关键字, 上英, 中文);
            }

            // ④ 指令(中英各一条):指令名一律大写,所以英文侧插入大写
            foreach (var 中 in 指令C.取全部中文指令名()) 加(中, 候选来历.指令, 中);
            foreach (var 英 in 指令C.取全部英文指令名()) 加(英, 候选来历.指令, 英, null);

            // ⑤ 类型名(类型C 是唯一事实源;字符串不是基础类型,手补一条)
            加("字符串", 候选来历.类型, "字符串", "string");
            加("string", 候选来历.类型, "string", "字符串");

            return [.. 表];
        }

        // ---------------- 拼音首字母与全拼(带缓存) ----------------

        private static readonly Dictionary<string, string> _首字母缓存 = new(StringComparer.Ordinal);
        private static readonly Dictionary<char, string> _字音缓存 = new();
        private static readonly object _首字母锁 = new();

        /// <summary> 一个词的大写拼音首字母(`函数` → HS)。算不出来就返回空串(不抛) </summary>
        public static string 拼音首字母(string 词)
        {
            if (词.Length == 0) return "";
            lock (_首字母锁)
            {
                if (_首字母缓存.TryGetValue(词, out string? 旧)) return 旧;
            }
            string 新;
            try { 新 = WordsHelper.GetFirstPinyin(词).ToUpperInvariant(); }
            catch { 新 = ""; }
            lock (_首字母锁) _首字母缓存[词] = 新;
            return 新;
        }

        /// <summary>
        /// 一个字最常见的读音(大写;英文/数字原样)。查 `字音表C`(由 NPinyin 生成好的死数据,
        /// 只覆盖补全词表里出现过的字 —— 这样不用为补全引一个只有 .NET Framework 资产的包)。
        /// 表里没有的字返回空串:那这个词就只按首字母匹配,别的候选不受影响。
        /// </summary>
        private static string 字音(char 字)
        {
            if (字 < 128) return char.ToUpperInvariant(字).ToString();
            return 字音表C.取(字);
        }

        // ---------------- 模糊拼音(用户可勾:默认只开"翘舌→平舌"与"后鼻音→前鼻音") ----------------

        /// <summary>
        /// 模糊拼音规则表:**键 = 规则名**(选项里存的就是这个名字),值 = 音节内的替换(长的在前)。
        /// 规则名同时也是选项界面上勾选项的文案来源(一个来源,不另抄一份)。
        /// </summary>
        public static readonly IReadOnlyDictionary<string, (string 说明, (string 原, string 新)[] 替换)> 模糊规则表 =
            new Dictionary<string, (string, (string, string)[])>(StringComparer.Ordinal)
            {
                ["翘舌→平舌"] = ("zh / ch / sh 当作 z / c / s", [("ZH", "Z"), ("CH", "C"), ("SH", "S")]),
                ["后鼻音→前鼻音"] = ("ang / eng / ing 当作 an / en / in", [("ANG", "AN"), ("ENG", "EN"), ("ING", "IN")]),
                ["鼻音→边音"] = ("n 当作 l(只在音节开头)", [("^N", "L")]),
                ["唇齿→舌根"] = ("f 当作 h(只在音节开头)", [("^F", "H")]),
                ["r→l"] = ("r 当作 l(只在音节开头)", [("^R", "L")]),
            };

        /// <summary> 默认开着的模糊规则(用户没动设置时就是这样) </summary>
        public static readonly string[] 默认模糊规则 = ["翘舌→平舌", "后鼻音→前鼻音"];

        /// <summary> 把一个音节的某条规则应用上去(替换按"长的在前",`^` 表示只认音节开头) </summary>
        private static string 应用换(string 音节, (string 原, string 新) 替换)
        {
            if (替换.原.StartsWith('^'))
            {
                string 头 = 替换.原[1..];
                return 音节.StartsWith(头, StringComparison.Ordinal) ? 替换.新 + 音节[头.Length..] : 音节;
            }
            return 音节.Replace(替换.原, 替换.新, StringComparison.Ordinal);
        }

        /// <summary>
        /// 一个词可接受的几种拼法:全拼 + 每条勾中的规则各自变一次 + 全部规则一起变一次。
        /// 这样"手比脑子快"也能补上:`长` 的全拼是 CHANG,勾了翘舌+后鼻之后 CANGD / CAND / CANG / CAN / CA 都算命中。
        /// 纯英文/数字的词返回空(它们走中文名与英文名前缀匹配)。
        /// </summary>
        public static List<string> 全拼变体(string 词, IReadOnlyCollection<string>? 规则 = null)
        {
            if (string.IsNullOrEmpty(词)) return [];
            var 音节 = new List<string>();
            foreach (char c in 词)
            {
                if (!char.IsLetterOrDigit(c)) continue;
                string 音 = 字音(c);
                if (音.Length == 0) return [];      // 有算不出来的字(纯符号等)就当没有拼音
                音节.Add(音);
            }
            if (音节.Count == 0) return [];
            // 每个音节都是一个字母的(英文缩写那种)不算拼音词
            if (音节.All(x => x.Length == 1) && 词.All(c => c < 128)) return [];

            var 变体 = new HashSet<string>(StringComparer.Ordinal) { string.Concat(音节) };
            var 勾中 = (规则 ?? 默认模糊规则).Where(模糊规则表.ContainsKey).ToList();
            foreach (string r in 勾中)
                变体.Add(string.Concat(音节.Select(x => 模糊规则表[r].替换.Aggregate(x, 应用换))));
            if (勾中.Count > 1)
            {
                var 全部 = 勾中.SelectMany(r => 模糊规则表[r].替换).ToArray();
                变体.Add(string.Concat(音节.Select(x => 全部.Aggregate(x, 应用换))));
            }
            return [.. 变体];
        }

        // ---------------- 匹配 ----------------

        /// <summary>
        /// 按输入给候选(最多 <paramref name="上限"/> 条)。分数小的在前:完全相等 0 / 中文前缀 1 / 英文前缀 2 / 拼音首字母 3。
        /// <paramref name="只要方法"/> = true 时只给方法(用在 `名字.` 后面)。
        /// <paramref name="附加名字"/> 是工程里的名字(函数/结构/方法/变量),按"名字"这一档参与匹配。
        ///
        /// C19 起真正的匹配在 `排序匹配` —— 这个方法只是"固定词表 + 一串裸名字"的便捷入口
        /// (带类型/参数说明的候选由 `补全名字C` 组好池子再交给 `排序匹配`)。
        /// </summary>
        public static List<补全候选C> 匹配(string 输入, bool 只要方法 = false, IReadOnlyList<string>? 附加名字 = null,
            int 上限 = 30, IReadOnlyCollection<string>? 模糊规则 = null)
        {
            // 输入为空:只有"刚敲了一个 `.`"这种情形会走到(那时把方法全列出来给用户挑)
            if ((输入 ?? "").Trim().Length == 0)
                return 只要方法 ? [.. 固定方法候选] : [];
            var 池 = new List<补全候选C>(只要方法 ? 固定方法候选 : _固定候选);
            if (!只要方法 && 附加名字 != null)
                foreach (string 名 in 附加名字)
                    if (名.Length > 0) 池.Add(new 补全候选C(名, 候选来历.名字, 名));
            return 排序匹配(输入 ?? "", 池, 上限, 模糊规则);
        }

        /// <summary>
        /// 给一个**候选池**打分并排好序(C19 起是全项目唯一的匹配口径;界面两端与自检都走它)。
        /// 池子由调用方组好:固定词表、工程里的函数/结构/变量、`.` 后面的结构字段与方法、
        /// **代码片段骨架**(C28)都可以混在一起。
        /// 规则:完全相等 0 / 中文前缀 1 / 英文前缀 2 / 拼音首字母 3 / 全拼 4 / 多打了 5 / **跳字母** 6;
        /// 同一分的按 来历(方法在前)→ 词长 → 词序;**同一个词只留分最高的那条**。
        /// 输入为空 = 把池子原样给出(`名字.` 后面还没打字时用的就是这一档)。
        /// </summary>
        public static List<补全候选C> 排序匹配(string 输入, IReadOnlyList<补全候选C> 候选池,
            int 上限 = 30, IReadOnlyCollection<string>? 模糊规则 = null)
        {
            string 词 = (输入 ?? "").Trim();
            var 去重 = new Dictionary<string, (int 分, 补全候选C 项)>(StringComparer.Ordinal);
            if (词.Length == 0)
            {
                // 池子里没打分这回事:按调用方给的顺序原样给(同一个词只留第一条),顺序才稳定
                var 见过 = new HashSet<string>(StringComparer.Ordinal);
                return [.. 候选池.Where(x => x.词.Length > 0 && 见过.Add(x.词)).Take(上限)];
            }
            bool 全字母 = 词.All(char.IsAsciiLetter);
            string 词上 = 词.ToUpperInvariant();
            var 规则 = 模糊规则 ?? 默认模糊规则;

            foreach (var 项 in 候选池)
            {
                if (项.词.Length == 0) continue;
                int 分 = 打分(项, 词, 词上, 全字母, 规则);
                if (分 < 0) continue;
                // 同名的只留分最高的那条(例如中文名既是关键字又是类型);同分时先来的赢(编译结果排在现场兜底前面)
                if (!去重.TryGetValue(项.词, out var 旧) || 分 < 旧.分) 去重[项.词] = (分, 项);
            }

            return [.. 去重.Values
                .OrderBy(x => x.分)
                .ThenBy(x => (int)x.项.来历)
                .ThenBy(x => x.项.词.Length)
                .ThenBy(x => x.项.词, StringComparer.Ordinal)
                .Select(x => x.项)
                .Take(上限)];
        }

        /// <summary>
        /// 确认一条候选时**真正写进代码的文本**(C16-1,用户 2026-09-19 拍板):
        /// 按 Tab / 回车 = **只要这个名字**(`补空格: false`);按空格 = **名字 + 一个空格**(`补空格: true`)。
        /// 带括号的候选(`长度()` / `内建函数(…)`)光标要落在括号里,这两种键都**不补空格**
        /// —— 不然会写出 `函数( )` 这种别扭写法。手机端点一条 = 按空格那一档(见 MobileIDE)。
        /// </summary>
        public static string 确认文本(补全候选C? 项, bool 补空格)
        {
            if (项 == null) return "";
            if (!补空格 || 项.括号内落光标) return 项.插入;
            return 项.插入.EndsWith(' ') ? 项.插入 : 项.插入 + " ";
        }

        /// <summary>
        /// 打分(小的优先):完全相等 0 / 中文前缀 1 / 英文前缀 2 / 拼音首字母 3 / 全拼(含模糊) 4 /
        /// 多打了一两个字母 5 / **跳字母** 6;都不沾返回 -1。
        /// 匹配一律按 `项.匹配名` 算(C28:片段候选显示 `如果{…}`、按触发词 `如果` 参与匹配)。
        /// "多打了"与"跳字母"这两档是给"手比脑子快"留的:打多了不至于让候选框消失、逼人按退格。
        /// **多个全拼变体里取最好的那一档**(模糊拼音开着时 `长度` 有 CHANGDU / CANGDU 两条:
        /// 先碰上哪条就返回,会把 `cang` 这样本该 4 分的算成 6 分,排序跟着变形)。
        /// </summary>
        private static int 打分(补全候选C 项, string 词, string 词上, bool 全字母, IReadOnlyCollection<string> 规则)
        {
            string 名 = 项.匹配名;
            if (名 == 词) return 0;
            if (名.StartsWith(词, StringComparison.OrdinalIgnoreCase)) return 1;
            if (!string.IsNullOrEmpty(项.英文) && 项.英文.StartsWith(词, StringComparison.OrdinalIgnoreCase)) return 2;
            if (!全字母) return -1;

            string 首 = 拼音首字母(名);
            if (首.Length > 0 && 首.StartsWith(词上, StringComparison.Ordinal)) return 3;

            int 最好 = -1;
            // 首字母也多打了一两个(`cdx` 这种):它不是全拼那一档的事,先记上,后面取最好的那一档
            if (首.Length > 0 && 词上.Length > 首.Length && 词上.StartsWith(首, StringComparison.Ordinal)) 最好 = 5;
            foreach (string 拼 in 全拼变体(名, 规则))
            {
                if (拼.StartsWith(词上, StringComparison.Ordinal)) return 4;
                if (词上.Length > 拼.Length && 词上.StartsWith(拼, StringComparison.Ordinal))
                {
                    最好 = Math.Max(最好, 5);                    // 多打了
                    continue;
                }
                if (是跳字母(词上, 拼)) 最好 = Math.Max(最好, 6);
            }
            return 最好;
        }

        /// <summary>
        /// "跳字母"口径(C28,用户 2026-09-20 定):打进去的那串字母**第一个必须是这个词拼音的第一个字母**,
        /// 其余字母在拼音里**按顺序**出现就行(`rugo` 出 `如果` —— R,U,G,U,O 里跳过第二个 U)。
        /// 老口径是"输入出现在拼音当中"(第 6 档 `拼.Contains(词上)`),那会把 `ug` / `uo` 这种**不含首字母**的
        /// 也捞出来(`ug` 恰好是 RUGUO 里的一段)⇒ 用户原话"会匹配太多的词出来,ug 等不提示"。
        /// **至少 3 个字母才给这个特权**:两个字母太短(`ca` 对 CHANGDU 一跳就中),
        /// 会把"关掉模糊拼音就不出"这条分界抹平(自检里有老断言盯着它)。
        /// </summary>
        private static bool 是跳字母(string 词上, string 拼) =>
            词上.Length >= 3 && 词上.Length <= 拼.Length && 词上[0] == 拼[0] && 是子序列(词上, 拼);

        /// <summary> 短串的字母是不是按顺序都出现在长串里(可以跳) </summary>
        private static bool 是子序列(string 短, string 长)
        {
            int 位 = 0;
            foreach (char c in 长)
            {
                if (位 < 短.Length && 短[位] == c) 位++;
                if (位 == 短.Length) return true;
            }
            return 位 == 短.Length;
        }

        // ---------------- 用法说明(悬停提示用:一个词的"声明格式 + 怎么用") ----------------

        /// <summary> 方法(与 中间层C 里那张"列表动作"表一一对应)。说明只写一份中文的,
        /// 英文别名(第96批)在 `查说明` 里退回这一条 —— 两份迟早写走样。 </summary>
        private static readonly Dictionary<string, string> _方法说明 = new(StringComparer.Ordinal)
        {
            ["长度"] = "名字.长度()(英文别名 .length()) —— 返回 `整数`:列表为元素个数,字符串为文字数(一个汉字计作一个文字)。可用作循环上界。",
            ["清空"] = "名字.清空()(英文别名 .clear()) —— 将长度归零;内存不释放,执行很快。列表与字符串均可使用。",
            ["添加"] = "名字.添加(值)(英文别名 .add(值)) —— 在列表末尾追加一个元素。写定容量的列表(如 `短整数 表[3]`)写满后继续追加会给出明确错误;未写个数者会自动增长。",
        };

        /// <summary> 内建函数(不在关键字表里,只有编译器认) </summary>
        private static readonly Dictionary<string, string> _内建说明 = new(StringComparer.Ordinal)
        {
            ["读取字符"] = "变量 = 读取字符() —— 读取一个字节(0~255);输入结束时为 -1。接收值的变量应声明为 `整数` 或 `var`:`-1` 存入 `字符` 会被截断为 255。",
            ["读取行"] = "变量 = 读取行(缓冲) —— 读取一整行(不含换行)到 `字符`/`字节` 的数组或列表,返回读取到的字节数。",
            ["读取数字"] = "变量 = 读取数字() —— 读取一个十进制整数(读到空白或换行为止)。",
            ["申请"] = "地址 p = 申请(字节数) —— 从堆里要一块(堆是独立的一块,默认 64KB,工程文件里 `\"堆\"` 可配);p 是这块数据区的起点," +
                "配 `释放(p)` 与 `取字(p, 偏移)` / `存字(p, 偏移, 值)` 用;申请不到会**当场停下**并给出改法。英文写法 alloc。",
            ["alloc"] = "地址 p = alloc(字节数) —— 从堆里要一块(堆是独立的一块,默认 64KB,工程文件里 `\"堆\"` 可配);p 是这块数据区的起点," +
                "配 `free(p)` 与 `取字(p, 偏移)` / `存字(p, 偏移, 值)` 用;申请不到会**当场停下**并给出改法。中文写法 申请。",
            ["释放"] = "释放(p); —— 把 `申请` 返回的那一块还回堆里(还了以后同一块还能再被申请到,并自动与相邻空闲块合并);" +
                "释放不是申请来的地址、重复释放、释放之后又 取字/存字,三种都会被拦住并当场停下。英文写法 free。",
            ["free"] = "free(p); —— 把 `alloc` 返回的那一块还回堆里(还了以后同一块还能再被申请到,并自动与相邻空闲块合并);" +
                "释放不是申请来的地址、重复释放、释放之后又 取字/存字,三种都会被拦住并当场停下。中文写法 释放。",
            ["字符串"] = "声明 `字符串 名字 = \"内容\";` —— 一个名字装载一段文字。长度与下标均按文字计(`\"世界\".长度()` 为 2);" +
                "赋值变长时自动扩容;作参数时传递数据地址与长度;不可取地址。" +
                "**也能放进结构/数组**:字段写 `字符串 名字;`(字段里是「数据地址 + 容量」8 字节,字在堆里)," +
                "数组写 `字符串 名单[10]`;整体赋值/返回/进列表时那些字段深拷贝(各有一份)。" +
                "参数默认只读,要能写回就写 `字符串 出 名字`(英文 result);`返回 字符串` 的调用能直接用在表达式里。",
            ["string"] = "声明 `字符串 名字 = \"内容\";` —— 一个名字装载一段文字。长度与下标均按文字计(`\"世界\".长度()` 为 2);" +
                "赋值变长时自动扩容;作参数时传递数据地址与长度;不可取地址。" +
                "**也能放进结构/数组**:字段写 `字符串 名字;`(字段里是「数据地址 + 容量」8 字节,字在堆里)," +
                "数组写 `字符串 名单[10]`;整体赋值/返回/进列表时那些字段深拷贝(各有一份)。" +
                "参数默认只读,要能写回就写 `字符串 出 名字`(英文 result);`返回 字符串` 的调用能直接用在表达式里。",
        };

        /// <summary>
        /// 一个词的"声明格式 + 用法"(悬停提示):方法 → 内建函数 → 关键字/类型/真假(查 中间层指令C.说明表)→ 汇编指令。
        /// 都不认识返回 null(调用方再去看"是不是本文件声明的名字")。
        /// </summary>
        public static string? 查说明(string 词)
        {
            string 名 = (词 ?? "").Trim();
            if (名.Length == 0) return null;
            if (_方法说明.TryGetValue(名, out string? 方法)) return 方法;
            // 方法英文别名(`length`/`clear`/`add`):退回中文那条说明(只维护一份)
            if (中间层指令C.到方法中文名(名) is { } 方法中文 && _方法说明.TryGetValue(方法中文, out string? 别名说明))
                return 别名说明;
            if (_内建说明.TryGetValue(名, out string? 内建)) return 内建;
            // 关键字 / 类型 / 真假如:说明表按"规范英文关键字"登记,中英都能查到
            if (中间层指令C.到英文关键字(名) is { } 英文 && 中间层指令C.取说明(英文) is { } 说明) return 说明;
            // 内建函数:上面那张手写表没收的(读文件/写文件/当前时间/存字/取字…),退回 中间层指令C.说明表
            if (中间层指令C.到内建函数英文名(名) is { } 内建英文 && 中间层指令C.取说明(内建英文) is { } 内建通用) return 内建通用;
            // 汇编指令:中文名或英文名都能查
            foreach (var (中文, 英, 操作码, 格式) in 指令C.全部指令)
                if (名 == 中文 || string.Equals(名, 英, StringComparison.OrdinalIgnoreCase))
                    return $"{中文} {英} · 操作码 0x{操作码:X2} · 操作数:{指令C.操作数写法(格式)} · 示例:{指令C.取示例(英) ?? 中文}";
            return null;
        }

        /// <summary> 这个词属于哪一类(提示框的页脚要写"词条:长度(方法)"这种);不认识返回 null </summary>
        public static string? 查种类(string 词)
        {
            string 名 = (词 ?? "").Trim();
            if (名.Length == 0) return null;
            if (_方法说明.ContainsKey(名) || 中间层指令C.是方法名(名)) return "方法";
            if (_内建说明.ContainsKey(名) || 中间层指令C.到内建函数英文名(名) != null) return "内建函数";
            if (中间层指令C.到英文关键字(名) is { } 英文 && 中间层指令C.取说明(英文) != null)
                return 关键字种类(英文);
            foreach (var (中文, 英, _, _) in 指令C.全部指令)
                if (名 == 中文 || string.Equals(名, 英, StringComparison.OrdinalIgnoreCase)) return "指令";
            return null;
        }

        /// <summary>
        /// "这一类名字**没有源码定义**可跳"那一句人话 —— **一份口径,两处共用**
        /// (`转到定义C` 与 H60-9 的 `声明线C`;编出来一个字不差,免得两处各说各的)。
        /// 内建函数那一档顺带点一句"宿主能力也归在这里"(它们由宿主实现,同样没有源码定义)。
        /// </summary>
        public static string 没有源码定义说明(string 词, string 种类) =>
            $"`{词}` 是{种类} —— 由引擎或编辑器实现,**没有源码定义**可跳。"
            + (种类 == "内建函数"
                ? "(宿主能力 `读文件` / `写文件` / `建目录` / `当前时间` 这一类也归在这里:它们由宿主实现)"
                : "");

        /// <summary> 关键字细分成"类型/字面量/语句"(提示框上分得清"这是类型"还是"这是语句") </summary>
        private static string 关键字种类(string 英文关键字)
        {
            if (类型C.关键字对.Any(x => string.Equals(x.英文, 英文关键字, StringComparison.OrdinalIgnoreCase))) return "类型";
            if (英文关键字 is "true" or "false") return "字面量";
            return "语句";
        }

        // ---------------- 从源码里收集名字(函数 / 结构 / 方法 / 变量) ----------------

        private static readonly Regex 函数正则 = new(@"^\s*(?:函数|func)\s+([^\s(（]+)", RegexOptions.Compiled);
        private static readonly Regex 结构正则 = new(@"^\s*(?:结构|struct)\s+([^\s{（(]+)", RegexOptions.Compiled);

        /// <summary> H68-2 枚举:`枚举 颜色 { 赤, 绿, 蓝 }`(**一行写完**)—— 名字那一格进候选,
        /// 成员另用 `枚举成员名们` 摘(成员是编译期常量,写表达式时会用到) </summary>
        private static readonly Regex 枚举正则 = new(@"^\s*(?:枚举|enum)\s+([^\s{（(]+)", RegexOptions.Compiled);

        /// <summary> 枚举那一行花括号里的成员串(`赤, 绿, 蓝` / `赤 = 3, 绿`) </summary>
        private static readonly Regex 枚举成员正则 = new(
            @"^\s*(?:枚举|enum)\s+[^\s{（(]+\s*[{(]\s*(.*?)\s*[})]\s*;?\s*$", RegexOptions.Compiled);

        private static readonly Regex 方法正则 = new(@"^\s*(?:静态\s+)?(?:方法|method)\s+([^\s(（{]+)", RegexOptions.Compiled);

        /// <summary> H68-3 元组声明:`[整数, 整数] 结果 = …;` / `[整数 商, 整数 余] 一份;` ——
        /// 声明出来的名字是**变量**(元组类型自己不是能写的标识符,不进候选) </summary>
        private static readonly Regex 元组正则 = new(
            @"^\s*\[(?:[^\[\]]|\[[^\]]*\])*\](?:[ \t]*\[[^\]]*\])*[ \t]+([^\s=;,\[（(]+)", RegexOptions.Compiled);

        private static readonly Regex 包含正则 = new("^\\s*(?:包含|import)\\s+\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary> 变量声明:`类型名 名字`(数组 `名字[]`、初始化 `名字 =`、只声明 `名字;` 都算)。
        /// 类型那一支必须是**非捕获组** —— 否则第 1 组是类型名,扫出来的"名字"全变成类型(自检抓过)。 </summary>
        private static readonly Regex 变量正则 = new(
            @"^\s*(?:字节|逻辑|字符|短整数|整数|无符号短整数|无符号整数|文字|变量|var|byte|logic|char|short|int|ushort|uint|rune|字符串|string)\s+([^\s=;,\[（(]+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary> 这一行声明出来的名字(函数/结构/枚举/元组/方法/变量);不是声明行返回 null </summary>
        private static string? 该行声明的名字(string 行)
        {
            foreach (var 正则 in new[] { 函数正则, 结构正则, 枚举正则, 元组正则, 方法正则, 变量正则 })
                if (正则.Match(行) is { Success: true } 匹配)
                {
                    string 名 = 匹配.Groups[1].Value.Trim();
                    if (名.Length > 0) return 名;
                }
            return null;
        }

        /// <summary> H68-2:这一行是枚举声明就把成员名逐个摘出来(去掉 `= 值` 那一截);不是枚举行给空表 </summary>
        private static List<string> 枚举成员名们(string 行)
        {
            var 结果 = new List<string>();
            if (枚举成员正则.Match(行) is not { Success: true } 匹配) return 结果;
            foreach (string 段 in 匹配.Groups[1].Value.Split(','))
            {
                int 等号 = 段.IndexOf('=');
                string 名 = (等号 >= 0 ? 段[..等号] : 段).Trim();
                if (名.Length > 0) 结果.Add(名);
            }
            return 结果;
        }

        /// <summary> 从一段源码里扫出"能补全的名字":函数 / 结构 / 枚举(含成员)/ 方法 / 变量(顺序即出现顺序,已去重) </summary>
        public static List<string> 从源码收集名字(string 源码)
        {
            var 结果 = new List<string>();
            var 见过 = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(源码)) return 结果;
            // 每敲一个字都要扫一遍:文件特别大就只给固定词表那些候选(不能让补全把编辑拖卡)
            if (源码.Length > 400_000) return 结果;
            foreach (string 行 in 源码.Split('\n'))
            {
                if (行.TrimStart().StartsWith("//")) continue;      // 注释行不算
                if (该行声明的名字(行) is { } 名 && 见过.Add(名)) 结果.Add(名);
                foreach (string 成员 in 枚举成员名们(行))
                    if (见过.Add(成员)) 结果.Add(成员);
            }
            return 结果;
        }

        /// <summary>
        /// 在源码里找这个名字的**声明行原文**(悬停提示用:`函数 打招呼(字符串 谁)` 这样的格式);
        /// 找不到(不是本文件声明的)返回 null。
        /// </summary>
        public static string? 查声明行(string 名字, string 源码)
        {
            string 名 = (名字 ?? "").Trim();
            if (名.Length == 0 || string.IsNullOrEmpty(源码)) return null;
            if (源码.Length > 400_000) return null;      // 与 从源码收集名字 同一条护栏:大文件不扫
            foreach (string 行 in 源码.Split('\n'))
            {
                if (行.TrimStart().StartsWith("//")) continue;
                if (该行声明的名字(行) == 名) return 行.Trim().TrimEnd('{').Trim();
            }
            return null;
        }

        /// <summary> 取一行里的 `包含 "相对路径"`(没有返回 null) </summary>
        public static string? 取包含路径(string 行) =>
            包含正则.Match(行) is { Success: true } 匹配 ? 匹配.Groups[1].Value : null;

        /// <summary>
        /// 扫"本文件 + 它 `包含` 进来的文件"(相对本文件所在目录解析,最多往下两层):
        /// 这样写 `拼接到` 时能补出 `库\string.hb` 里的 `取子串`、`转字符串` 这些函数名。
        /// **包含链那一段走 `库索引C.包含链`** —— 同一个"哪些算含进来的库"只留一处口径
        /// (编辑器认库那一批起;缓存按 路径 + 修改时间 + 长度,也在那边)。
        /// 读文件失败就当没有(补全只是帮手,不能因为一个坏路径把编辑搅停)。
        /// </summary>
        public static List<string> 从文件收集名字(string 源码, string? 本文件路径)
        {
            var 结果 = 从源码收集名字(源码);
            if (string.IsNullOrEmpty(本文件路径)) return 结果;

            var 见过 = new HashSet<string>(结果, StringComparer.Ordinal);
            foreach (var (_, 内容) in 库索引C.包含链(源码, 本文件路径))
                foreach (string n in 从源码收集名字(内容))
                    if (见过.Add(n)) 结果.Add(n);
            return 结果;
        }
    }
}
