using System.IO;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary> 一份源码文本里自己声明的一样东西(行号是**这份文本自己的行号**)</summary>
    public sealed class 源码声明C
    {
        public string 名 = "";
        public int 行;
    }

    /// <summary>
    /// 一份源码文本"自己有什么"的清单。
    /// **行号一律是这份文本自己的行号** —— 这是这一层存在的全部理由:见下面 `源码声明扫描C` 的注释。
    /// </summary>
    public sealed class 源码清单C
    {
        /// <summary> 自己声明的结构(按出现顺序) </summary>
        public List<源码声明C> 结构 { get; } = [];

        /// <summary> 自己声明的函数 / 方法(按出现顺序) </summary>
        public List<源码声明C> 函数 { get; } = [];

        /// <summary> 自己写的 `包含 "…"`(写法原样 + 行号) </summary>
        public List<(string 路径, int 行)> 包含 { get; } = [];

        /// <summary> 这份文本有几行(含最后那个换行之后的那一行,与编辑器行号口径一致) </summary>
        public int 行数;
    }

    // ================= H60-8(思维导图 2.0 的数据源):"文件 = 类"的结构树 =================
    //
    // 与上面 `源码清单C` **并排**的一条新路(老口径一个字不动,见 施工图 ①):
    //   · `源码清单C` 答的是"这份文件自己声明了哪些名字"(H45 那批为"文件归属"立的);
    //   · 这一组答的是"这份文件的**自然结构**"—— 用户 2026-09-26 的口径:
    //     「以我们的设定,一个文件就相当于一个大的方法(类)」。
    //     文件 = 类;类里有变量 / 常量 / 结构 / 方法 / 包含;方法体里有参数 / 局部 / 流程块 / 语句;
    //     流程块(用户点名 `循环 (var k = 1; …)`)当一个**作用域**,`k` 是它自己的局部。
    // 全部是**纯数据 + 纯函数**(喂文本即量,自检直接钉);H60-9 ~ H60-15 都从这棵树出发。

    /// <summary> 一条普通语句(只留行号与**去掉行注释后**的原文;H60-10 的"调用线"从它出发)</summary>
    public sealed class 语句C
    {
        public int 行;
        public string 原文 = "";
    }

    /// <summary>
    /// 一份声明的登记项 —— **参数 / 局部变量 / 顶层变量 / 结构字段都用它**(施工图 ②)。
    /// `是常量` 只在"顶层 + 初值是字面量"时为真(见 `源码声明扫描C.扫结构` 的注释)。
    /// </summary>
    public sealed class 声明项C
    {
        public string 名 = "";
        public string 类型词 = "";
        public int 行;
        public bool 是常量;
    }

    /// <summary>
    /// 一个**流程块**(`循环` / `如果` / `当` / `遍历` / `选择` / `否则` / `情况` / `默认` / `测试`)。
    /// 用户口径:流程块也当一个作用域 —— 头里 `var k = …` 与体里声明的名字都是**它自己的局部**。
    /// </summary>
    public sealed class 流程块节点C
    {
        /// <summary> 块头那个词(中文原样,如 `循环` / `否则`) </summary>
        public string 词 = "";
        public int 行;

        /// <summary> 块头括号里声明的(`循环 (var k = …)`) </summary>
        public List<声明项C> 头声明们 { get; } = [];

        /// <summary> 体里声明的局部 </summary>
        public List<声明项C> 局部们 { get; } = [];

        /// <summary> 体里**直接**挂着的语句(不含子块里的) </summary>
        public List<语句C> 里层语句们 { get; } = [];

        /// <summary> 里层的流程块(嵌套的 `如果` / `循环` …) </summary>
        public List<流程块节点C> 子块们 { get; } = [];
    }

    /// <summary> 一个方法(过程)。容器(`静态 方法 名(字段…)`)也走它:成员方法进 `方法们` </summary>
    public sealed class 方法节点C
    {
        public string 名 = "";
        public int 头行;

        /// <summary> 尾缀 `返回 类型` 的那个类型词(没写就是空串) </summary>
        public string 返回类型词 = "";

        public List<声明项C> 参数们 { get; } = [];

        /// <summary> 体(`{`)那一行 / 收尾 `}` 那一行 </summary>
        public int 体起行;
        public int 止行;

        public List<声明项C> 局部们 { get; } = [];
        public List<流程块节点C> 流程块们 { get; } = [];

        /// <summary> 体里**直接**挂着的语句(不含流程块里的) </summary>
        public List<语句C> 语句们 { get; } = [];

        /// <summary> 容器里的**成员方法**(裸成员头 `累加(整数 n) { … }`;见 扫结构 的注释) </summary>
        public List<方法节点C> 方法们 { get; } = [];
    }

    /// <summary> 一个结构体:字段 + (H60-4 之后的)结构方法 </summary>
    public sealed class 结构节点C
    {
        public string 名 = "";
        public int 行;
        public List<声明项C> 字段们 { get; } = [];
        public List<方法节点C> 方法们 { get; } = [];
    }

    /// <summary>
    /// **一份源码文本的自然结构**(根 = "文件 = 类")。行号一律是**这份文本自己的行号**。
    /// </summary>
    public sealed class 源码结构C
    {
        /// <summary> 这份文本的路径(可空 —— 纯函数喂文本时就没有) </summary>
        public string? 文件;

        /// <summary> 几行(与 `源码清单C.行数` 同一个口径) </summary>
        public int 行数;

        public List<(string 路径, int 行)> 包含们 { get; } = [];

        /// <summary> 顶层变量(**含常量**:`是常量` 为真的那些) </summary>
        public List<声明项C> 变量们 { get; } = [];

        public List<结构节点C> 结构们 { get; } = [];
        public List<方法节点C> 方法们 { get; } = [];

        /// <summary> 直接写在顶层的流程块(示例06 的 `循环 (var k = 1; …)` 就在这儿) </summary>
        public List<流程块节点C> 顶层流程块们 { get; } = [];

        /// <summary> 顶层直接挂着的语句 </summary>
        public List<语句C> 顶层语句们 { get; } = [];
    }

    /// <summary>
    /// **一份源码文本自己的声明**(结构 / 函数 / 包含)—— 纯函数,不吃编译结果、不读盘。
    ///
    /// 为什么要有这一层(H33 第252批的实测教训;口径见第254批):
    /// 那时 `名字表C.函数范围` 的第 4 项记的是**编译那一段的文件**(`包含` 进来的文件是**原地内联**进去的),
    /// 而库函数的行号又是**库文件自己的** —— 于是 `Sample\中间层\示例19_目录遍历\主程序.hb`
    /// (80 行、自己一条 `函数` 都没有)按文件名一筛,筛出 **42 条**函数,其中 **38 条的行号超出本文行数**
    /// (它们是 `库\string.hb` / `库\目录.hb` 的行号)。照着那个画思维导图 ⇒ 子项与编辑器里的代码**一条都对不上**。
    /// **那个根因已在第254批治掉**(第 4 项现在 = 真正声明那一份文件),但这一层照旧要留:
    /// 它答的是"这份文件**自己**有什么",不只是函数 —— **结构**、**`包含` 那一族**都只有源码文本里才有,
    /// 而且"这份文件没参与编译"时它也照样答得出来(编译结果那时候什么都没有)。
    /// 编译结果只用来把"声明行"翻译成带模块前缀的**全名**(调用关系表要的是全名)。
    ///
    /// 扫的规矩:
    ///   · **去行注释**(`//` 起头,不在字符串里);
    ///   · 前缀词(`公共` / `私有` / `程序集` / `静态` / `模块` 与英文)可以叠着写;
    ///   · 中英两种写法都认(`函数`/`func`、`结构`/`struct`、`包含`/`import`,与 `补全词表C` 同一套词);
    ///   · 不认"函数体里面的嵌套声明"这类花样(这门语言也没有)。
    /// </summary>
    public static class 源码声明扫描C
    {
        /// <summary>
        /// 声明一个过程的写法(H46:`函数` 撤了,只剩 `方法`/`method`)—— 有参数/没参数都算,
        /// 容器头 `静态 方法 名(…)` 也会被它认出来(那是名字空间,列出来也不算错:它确实是个"名字")。
        /// </summary>
        private static readonly Regex 函数行 = new(
            @"^[ \t]*(?:(?:公共|私有|程序集|静态|模块|public|private|static|module)[ \t]+)*(?:方法|method)[ \t]+([^\s(（:：]+)",
            RegexOptions.Compiled);

        private static readonly Regex 结构行 = new(
            @"^[ \t]*(?:(?:公共|私有|程序集|静态|模块|public|private|static|module)[ \t]+)*(?:结构|struct)[ \t]+([^\s{（(:：]+)",
            RegexOptions.Compiled);

        /// <summary> 扫一份文本(传空给空清单,不炸)</summary>
        public static 源码清单C 扫(string? 文本)
        {
            var 清单 = new 源码清单C();
            if (string.IsNullOrEmpty(文本)) return 清单;
            string[] 行集 = 文本.Split('\n');
            清单.行数 = 行集.Length;
            for (int i = 0; i < 行集.Length; i++)
            {
                string 净 = 去行注释(行集[i]);
                int 行号 = i + 1;
                if (补全词表C.取包含路径(净) is { } 相对) 清单.包含.Add((相对, 行号));
                if (结构行.Match(净) is { Success: true } 结) 清单.结构.Add(new 源码声明C { 名 = 结.Groups[1].Value, 行 = 行号 });
                if (函数行.Match(净) is { Success: true } 函) 清单.函数.Add(new 源码声明C { 名 = 函.Groups[1].Value, 行 = 行号 });
            }
            return 清单;
        }

        /// <summary> 把这个文件里的 `包含 "…"` 解析成绝对路径(**与编译器同一条口径**:相对本文件所在目录);解不出来给 null。
        /// H64:系统目录那一档的虚拟路径也认(拼接走 `虚拟路径C`,不碰 `Path.Combine`) </summary>
        public static string? 解析包含路径(string? 本文件全称, string? 包含里的写法)
        {
            if (string.IsNullOrWhiteSpace(本文件全称) || string.IsNullOrWhiteSpace(包含里的写法)) return null;
            try
            {
                string 目录 = 虚拟路径C.所在目录(本文件全称);
                if (目录.Length == 0) return null;
                // H64:文件在系统目录里(虚拟路径)时只做字符串层面的拼接(不去 `Path.GetFullPath`)
                if (虚拟路径C.是虚拟(本文件全称) || 虚拟路径C.是虚拟(包含里的写法))
                    return 虚拟路径C.收正(虚拟路径C.拼接(目录, 包含里的写法));
                return Path.GetFullPath(Path.Combine(目录, 包含里的写法));
            }
            catch { return null; }
        }

        /// <summary> 顶层名字的尾名(`数学.求和` → `求和`);没有点就是它自己 </summary>
        public static string 尾名(string? 全名)
        {
            if (string.IsNullOrEmpty(全名)) return "";
            int 点 = 全名.LastIndexOf('.');
            return 点 < 0 ? 全名 : 全名[(点 + 1)..];
        }

        // ================= H60-8:"文件 = 类"的结构扫描(施工图在 未做任务清单.md §9 H53 步 1) =================

        /// <summary> 扫一份文本,给出"文件 = 类"的自然结构(路径不带,`文件` 为空)</summary>
        public static 源码结构C 扫结构(string? 文本) => 扫结构(文本, null);

        /// <summary>
        /// 扫一份文本,给出"文件 = 类"的自然结构(纯函数:不读盘、不吃编译结果、不碰界面)。
        ///
        /// 扫法(**按花括号深度分块**;这门语言一行一条语句、块头与 `{` 分行,行级就够):
        ///   · 深度 0 = 顶层:`结构 名` 进结构体;`方法 名(…)`(含 `静态 方法`)进方法体;
        ///     其余 `类型 名…;` = 顶层变量;别的行 = 顶层语句;
        ///   · 体里 `类型 名…;` = 局部;`循环/如果/当/遍历/选择(…)+ {` = **流程块**
        ///     (头里 `var k = …` 算**它自己的局部**),流程块里还能再套流程块;
        ///   · 块、体收在**行级**上认:行首的 `}` 收一层,行尾(或单独一行)的 `{` 开一层。
        ///
        /// **"常量"判据(施工图 ④,这一步就定死;H60-13 那一档配色靠它)**:
        /// **顶层声明 + 初值是字面量**(数字 / 字符 / 字符串 / `真` / `假`)⇒ `是常量 = true`。
        /// 为什么这么定:这门语言**没有"声明一个具名常量"的写法**(H3 那一族还没放开),
        /// 不这么定"常量"这一档就是空的。
        ///
        /// **两处超出施工图、但必须收**(否则内容会丢,已记进本批的记录):
        /// ① 顶层也能写流程块(示例06 的 `循环 (var k = …)` 就在顶层)⇒ 根上多一个 `顶层流程块们`;
        /// ② 容器(`静态 方法 名(字段…)`)的**裸成员头**(`累加(整数 n)` + `{`)按成员方法收进
        ///    `方法节点C.方法们`(光按施工图那两个字段,成员体里的语句会挂错地方)。
        /// </summary>
        public static 源码结构C 扫结构(string? 文本, string? 文件)
        {
            var 树 = new 源码结构C { 文件 = 文件 };
            if (string.IsNullOrEmpty(文本)) return 树;
            string[] 行集 = 文本.Split('\n');
            树.行数 = 行集.Length;

            var 栈 = new Stack<帧>();
            栈.Push(new 帧 { 种类 = 帧顶层 });
            帧? 待入 = null;

            for (int i = 0; i < 行集.Length; i++)
            {
                int 行号 = i + 1;
                string 净 = 去行注释(行集[i]).Trim();
                if (净.Length == 0) continue;

                // 行首的 `}`:收一层(这一语言的 `}` 一律自己起行,或在 `} 否则 {` 这种行首)
                while (净.StartsWith('}'))
                {
                    收帧(栈, 行号);
                    净 = 净[1..].Trim();
                    if (净.Length == 0) break;
                }
                if (净.Length == 0) continue;

                bool 开括号 = 净.EndsWith('{');
                if (开括号) 净 = 净[..^1].TrimEnd();

                if (净.Length > 0) 分类一行(树, 栈, 净, 行号, i, 行集, ref 待入);

                if (开括号)
                {
                    帧 入 = 待入 ?? new 帧 { 种类 = 帧透传, 父帧 = 内容宿主(栈.Peek()) };
                    if (入.方法 != null && 入.方法.体起行 == 0) 入.方法.体起行 = 行号;
                    待入 = null;
                    栈.Push(入);
                }
            }

            while (栈.Count > 1) 收帧(栈, 树.行数);   // 文本没闭合也把能收的收掉(不抛)
            return 树;
        }

        private const int 帧顶层 = 0, 帧结构 = 1, 帧方法 = 2, 帧流程块 = 3, 帧透传 = 4;

        /// <summary> 一个还没收尾的块(顶层 / 结构体 / 方法体 / 流程块;"透传"用来兜认不出的 `{`) </summary>
        private sealed class 帧
        {
            public int 种类;
            public 结构节点C? 结构;
            public 方法节点C? 方法;
            public 流程块节点C? 块;
            public 帧? 父帧;
        }

        /// <summary> 认不出的 `{` 用"透传"兜住:里面收的东西照旧归外面那一层(内容不丢) </summary>
        private static 帧 内容宿主(帧 帧)
        {
            var 现在 = 帧;
            while (现在.种类 == 帧透传 && 现在.父帧 != null) 现在 = 现在.父帧;
            return 现在;
        }

        private static void 收帧(Stack<帧> 栈, int 行号)
        {
            if (栈.Count <= 1) return;
            var 帧 = 栈.Pop();
            if (帧.种类 == 帧方法 && 帧.方法 != null) 帧.方法.止行 = 行号;
        }

        private static readonly Regex 结构头 = new(
            @"^[ \t]*(?:(?:公共|私有|程序集|静态|模块|public|private|static|module)[ \t]+)*(?:结构|struct)[ \t]+([^ \t{（(]+)[ \t]*$",
            RegexOptions.Compiled);

        private static readonly Regex 方法头 = new(
            @"^[ \t]*(?:(?:公共|私有|程序集|静态|模块|public|private|static|module)[ \t]+)*(?:方法|method)[ \t]+([^ \t(（]+)[ \t]*[（(]([^)）]*)[)）](?:[ \t]*(?:返回|return)[ \t]+([^ \t{;]+))?[ \t]*$",
            RegexOptions.Compiled);

        /// <summary> 流程块头。后面那个负向环视必须留着 —— 不然 `测试输出(…)` 会被 `测试` 认走 </summary>
        private static readonly Regex 流程块头 = new(
            @"^[ \t]*(如果|否则|选择|情况|默认|循环|当|遍历|测试|if|else|switch|case|default|for|while|foreach|test)(?![_\p{L}\p{N}])(.*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary> 容器里的**裸成员头**:`累加(整数 n)` / `当前值() 返回 整数`(没有 `方法` 关键字,下一行紧跟 `{`) </summary>
        private static readonly Regex 裸成员头 = new(
            @"^([\p{L}_][\p{L}\p{N}_]*)[ \t]*[（(]([^)）]*)[)）][ \t]*(?:(?:返回|return)[ \t]+([^ \t{;]+))?[ \t]*$",
            RegexOptions.Compiled);

        /// <summary> 一条声明:`类型 名[个数][个数]… [= 初值];`(类型可以带点 / 两个词:`引用 整数`、`数学.rec`;
        /// 方括号那一段**可以重复**(H68-1 多维数组 `整数 袋[3][4];`))</summary>
        private static readonly Regex 声明头 = new(
            @"^((?:[\p{L}_][\p{L}\p{N}_.]*[ \t]+)+)([\p{L}_][\p{L}\p{N}_]*)([ \t]*\[[^\]]*\])*[ \t]*(?:=[ \t]*(?<初值>.*?))?[ \t]*;[ \t]*$",
            RegexOptions.Compiled);

        /// <summary>
        /// H68-3 **元组声明**:`[整数, 整数] 结果 = [7, 3];` / `[整数 商, 整数 余] 一份;` /
        /// `[整数, 整数] 表[3];` —— 类型那一截是方括号(元素里还能嵌一层元组),类型词就是那一段。
        /// </summary>
        private static readonly Regex 元组声明头 = new(
            @"^(\[(?:[^\[\]]|\[[^\]]*\])*\][ \t]+)([\p{L}_][\p{L}\p{N}_]*)([ \t]*\[[^\]]*\])*[ \t]*(?:=[ \t]*(?<初值>.*?))?[ \t]*;[ \t]*$",
            RegexOptions.Compiled);

        /// <summary> 参数:`类型 名[个数]`(类型词允许两个词,如 `出 字符串` / `引用 整数`)</summary>
        private static readonly Regex 参数项 = new(
            @"^(.+?)[ \t]+([\p{L}_][\p{L}\p{N}_]*)[ \t]*(?:\[[^\]]*\])?$",
            RegexOptions.Compiled);

        /// <summary> 流程块头里的一条声明(**必须带初始值**:`var k = 1` ✓,`i = 0` ✗)</summary>
        private static readonly Regex 头声明 = new(
            @"^((?:[\p{L}_][\p{L}\p{N}_]*[ \t]+)+)([\p{L}_][\p{L}\p{N}_]*)[ \t]*=[ \t]*(.+)$",
            RegexOptions.Compiled);

        /// <summary> 初值是不是**字面量**(数字 / 字符 / 字符串 / 真 / 假)—— "常量"那一档的判据 </summary>
        private static readonly Regex 整字面量 = new(
            @"^(?:[-+]?(?:0[xX][0-9A-Fa-f]+|\d+(?:\.\d+)?)|""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|真|假|true|false)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 顶在行首就**不是声明**的词(语句关键字 + 流程块词 + 声明前缀词)。
        /// 为什么需要:`返回 s;` 的形状和 `整数 s;` 一模一样,不排掉就会被当成一条声明。
        /// **有意不在表里的**:类型名(`整数`/`字符串`/`地址` …)与 `变量`/`var` —— 它们本来就该当类型词用。
        /// </summary>
        private static readonly HashSet<string> 语句起头词 = new(StringComparer.OrdinalIgnoreCase)
        {
            "返回", "return", "跳出", "break", "继续", "continue", "停止", "stop",
            "断言", "assert", "输出", "print", "输出行", "println", "测试输出", "debug",
            "拼接到", "concat", "包含", "import", "模块", "module",
            "公共", "public", "私有", "private", "程序集", "internal", "静态", "static",
            "方法", "method", "结构", "struct",
            "如果", "if", "否则", "else", "选择", "switch", "情况", "case", "默认", "default",
            "循环", "for", "当", "while", "遍历", "foreach", "测试", "test",
        };

        private static void 分类一行(源码结构C 树, Stack<帧> 栈, string 码, int 行号, int 序号, string[] 行集, ref 帧? 待入)
        {
            var 当前 = 内容宿主(栈.Peek());

            if (补全词表C.取包含路径(码) is { } 相对) { 树.包含们.Add((相对, 行号)); return; }

            if (结构头.Match(码) is { Success: true } 结)
            {
                var 节点 = new 结构节点C { 名 = 结.Groups[1].Value, 行 = 行号 };
                if (当前.种类 == 帧顶层) 树.结构们.Add(节点);
                待入 = new 帧 { 种类 = 帧结构, 结构 = 节点 };
                return;
            }

            if (方法头.Match(码) is { Success: true } 方)
            {
                var 节点 = new 方法节点C
                {
                    名 = 方.Groups[1].Value,
                    头行 = 行号,
                    返回类型词 = 方.Groups[3].Success ? 方.Groups[3].Value : "",
                };
                解参数(方.Groups[2].Value, 行号, 节点.参数们);
                switch (当前.种类)
                {
                    case 帧顶层: 树.方法们.Add(节点); break;
                    case 帧结构: 当前.结构!.方法们.Add(节点); break;
                    case 帧方法: 当前.方法!.方法们.Add(节点); break;
                }
                待入 = new 帧 { 种类 = 帧方法, 方法 = 节点 };
                return;
            }

            if (流程块头.Match(码) is { Success: true } 块)
            {
                var 节点 = new 流程块节点C { 词 = 块.Groups[1].Value, 行 = 行号 };
                解头声明(码[块.Groups[1].Length..], 行号, 节点.头声明们);
                switch (当前.种类)
                {
                    case 帧顶层: 树.顶层流程块们.Add(节点); break;
                    case 帧方法: 当前.方法!.流程块们.Add(节点); break;
                    case 帧流程块: 当前.块!.子块们.Add(节点); break;
                }
                待入 = new 帧 { 种类 = 帧流程块, 块 = 节点 };
                return;
            }

            // 容器里的裸成员头:`累加(整数 n)`,**下一行紧跟 `{`** 才算(不然只是个残缺的调用行)
            if (当前.种类 == 帧方法 && 裸成员头.Match(码) is { Success: true } 裸 && 下一非空行是开括号(行集, 序号))
            {
                var 节点 = new 方法节点C
                {
                    名 = 裸.Groups[1].Value,
                    头行 = 行号,
                    返回类型词 = 裸.Groups[3].Success ? 裸.Groups[3].Value : "",
                };
                解参数(裸.Groups[2].Value, 行号, 节点.参数们);
                当前.方法!.方法们.Add(节点);
                待入 = new 帧 { 种类 = 帧方法, 方法 = 节点 };
                return;
            }

            // H68-3 元组声明(类型那一截是方括号,`声明头` 那把尺子够不着)
            if (元组声明头.Match(码) is { Success: true } 元声)
            {
                var 项 = new 声明项C
                {
                    名 = 元声.Groups[2].Value,
                    类型词 = 元声.Groups[1].Value.Trim(),
                    行 = 行号,
                    是常量 = 当前.种类 == 帧顶层
                             && 元声.Groups["初值"].Success
                             && 整字面量.IsMatch(元声.Groups["初值"].Value.Trim()),
                };
                switch (当前.种类)
                {
                    case 帧顶层: 树.变量们.Add(项); break;
                    case 帧结构: 当前.结构!.字段们.Add(项); break;
                    case 帧方法: 当前.方法!.局部们.Add(项); break;
                    case 帧流程块: 当前.块!.局部们.Add(项); break;
                }
                待入 = null;
                return;
            }

            if (声明头.Match(码) is { Success: true } 声 && !语句起头词.Contains(声.Groups[1].Value.Trim()))
            {
                var 项 = new 声明项C
                {
                    名 = 声.Groups[2].Value,
                    类型词 = 去前缀词(声.Groups[1].Value),
                    行 = 行号,
                    是常量 = 当前.种类 == 帧顶层
                             && 声.Groups["初值"].Success
                             && 整字面量.IsMatch(声.Groups["初值"].Value.Trim()),
                };
                switch (当前.种类)
                {
                    case 帧顶层: 树.变量们.Add(项); break;
                    case 帧结构: 当前.结构!.字段们.Add(项); break;
                    case 帧方法: 当前.方法!.局部们.Add(项); break;
                    case 帧流程块: 当前.块!.局部们.Add(项); break;
                }
                待入 = null;
                return;
            }

            var 句 = new 语句C { 行 = 行号, 原文 = 码 };
            switch (当前.种类)
            {
                case 帧顶层: 树.顶层语句们.Add(句); break;
                case 帧方法: 当前.方法!.语句们.Add(句); break;
                case 帧流程块: 当前.块!.里层语句们.Add(句); break;
            }
            待入 = null;
        }

        /// <summary> 声明前面的可见性 / 静态那类前缀词(登记一份,免得两处各写一遍) </summary>
        private static readonly string[] 声明前缀词 = ["公共", "私有", "程序集", "静态", "public", "private", "internal", "static"];

        /// <summary> 去掉类型词前面叠着的前缀词(`私有 整数` → `整数`) </summary>
        private static string 去前缀词(string 类型词)
        {
            string 现在 = 类型词.Trim();
            bool 又 = true;
            while (又)
            {
                又 = false;
                foreach (string 词 in 声明前缀词)
                {
                    if (现在.Length <= 词.Length || (现在[词.Length] != ' ' && 现在[词.Length] != '\t')) continue;
                    if (!现在.StartsWith(词, StringComparison.OrdinalIgnoreCase)) continue;
                    现在 = 现在[(词.Length + 1)..].TrimStart();
                    又 = true;
                    break;
                }
            }
            return 现在;
        }

        /// <summary> 把 `(…)` 里面的那一段取出来(没有括号就原样还回去) </summary>
        private static string 取括号里(string 头)        {
            int 起 = 头.IndexOfAny(['(', '（']);
            if (起 < 0) return 头.Trim();
            int 止 = 头.LastIndexOfAny([')', '）']);
            return 止 > 起 ? 头[(起 + 1)..止] : 头[(起 + 1)..];
        }

        private static void 解参数(string 参数串, int 行号, List<声明项C> 收)
        {
            foreach (string 段 in 参数串.Split([',', '，']))
            {
                string 项 = 段.Trim();
                if (项.Length == 0 || 参数项.Match(项) is not { Success: true } 匹) continue;
                收.Add(new 声明项C { 名 = 匹.Groups[2].Value, 类型词 = 匹.Groups[1].Value.Trim(), 行 = 行号 });
            }
        }

        private static void 解头声明(string 头, int 行号, List<声明项C> 收)
        {
            foreach (string 段 in 取括号里(头).Split([';', '；', ',', '，']))
            {
                string 项 = 段.Trim();
                if (项.Length == 0 || 头声明.Match(项) is not { Success: true } 匹) continue;
                收.Add(new 声明项C { 名 = 匹.Groups[2].Value, 类型词 = 匹.Groups[1].Value.Trim(), 行 = 行号 });
            }
        }

        private static bool 下一非空行是开括号(string[] 行集, int 序号)
        {
            for (int i = 序号 + 1; i < 行集.Length; i++)
            {
                string 净 = 去行注释(行集[i]).Trim();
                if (净.Length == 0) continue;
                return 净 == "{";
            }
            return false;
        }

        /// <summary>
        /// 砍掉一行里 `//` 之后的部分(引号里的 `//` 不算;认不出来就原样返回)。
        /// H60-10 起 `调用线C` 也调它 —— **同一套手势只写一份**(施工图 · 步 3 那句"能抽共用就抽");
        /// 本文件里的用法与行为**一个字没动**,这里只是把可见性从 `private` 放到 `public`。
        /// </summary>
        public static string 去行注释(string 行)
        {
            if (行.Length == 0) return 行;
            int 引号 = 0;
            for (int i = 0; i < 行.Length - 1; i++)
            {
                if (行[i] == '"') { 引号++; continue; }
                if (行[i] == '/' && 行[i + 1] == '/' && 引号 % 2 == 0) return 行[..i];
            }
            return 行;
        }
    }
}
