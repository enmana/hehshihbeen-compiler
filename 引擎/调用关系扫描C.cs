using System.IO;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 一条调用边的"被调方"是哪一类。**只是给人看的旁路分类**,不参与汇编、不影响机器码。
    /// </summary>
    public enum 调用种类C
    {
        /// <summary> 标签能对上"某个文件里定义过的函数"(用户自己写的函数 / 方法 / 模块函数) </summary>
        用户函数,

        /// <summary>
        /// 编译器自己发的**内建例程**(打印一串 / 读行 / 读数字 / 列表扩容 / 堆分配…)。
        /// 判据:这个标签在展开文本里被定义过、也在汇编器标签表里,但**对不上任何函数定义**
        /// —— 也就是说,它不是"用户写的函数",而是编译器为实现 `输出行`/`读取行`/`列表.添加` 这类内建能力发的子程序。
        /// </summary>
        内建函数,

        /// <summary> 宿主能力(`宿主调用 Rd, 功能号`:读文件 / 写文件 / 当前时间…;功能号含义见 `宿主能力C`) </summary>
        宿主能力,

        /// <summary> 对不上任何已知标签(例如 `调用 1234` 这种直接跳数字地址,或者别的编译器拼出来的地址) </summary>
        未知,
    }

    /// <summary>
    /// 一条**调用边**:谁(调用者定义名)在哪个文件的第几行调用了谁(被调标签 / 被调定义名),属于哪一类。
    /// 这是"谁调用了我 / 我调用了谁"列表的原料(见 `调用关系扫描C`)。
    /// </summary>
    public sealed class 调用边C
    {
        /// <summary> 调用者的**函数定义名**;空串 = 这一行不在任何函数里(顶层 / 程序段) </summary>
        public string 调用者 = "";

        /// <summary> 调用点所在文件(工程文件表里的那个名字;库行只知道是"包含进来的那一份",见 `是库行`) </summary>
        public string 调用点文件 = "";

        /// <summary> 调用点源码行号(1 基);**0 = 不知道**(`库` 行是另一个文件的行号、`编译器生成` 是生成代码 ——
        /// 这两种都不该算到本文件的某条源码语句头上,比没有更糟) </summary>
        public int 调用点行;

        /// <summary> 被调方在汇编里的标签(`调用`/`CALL` 后面那个词;宿主能力是 `宿主能力#功能号`) </summary>
        public string 被调标签 = "";

        /// <summary> 被调方的**函数定义名**(能对上用户函数时才有;内建 / 宿主 / 未知都是空串) </summary>
        public string 被调定义名 = "";

        /// <summary> 分类 </summary>
        public 调用种类C 种类;

        /// <summary> 补充说明:宿主能力是功能名(读文件/当前时间…),内建例程是"这是干什么用的" </summary>
        public string 备注 = "";

        /// <summary> 调用点来自 `包含` 进来的文件(标记带 `库` 字):**那种行号不可考**(见 `调用点行`) </summary>
        public bool 是库行;

        /// <summary> 在顶层(不在任何函数里) </summary>
        public bool 在顶层 => 调用者.Length == 0;

        /// <summary> 调用者给人看的样子 </summary>
        public string 调用者显示 => 在顶层 ? "顶层(程序段)" : 调用者;

        /// <summary> 被调方给人看的样子(有函数定义名就用定义名,没有就退回标签) </summary>
        public string 被调显示 => 被调定义名.Length > 0 ? 被调定义名 : 被调标签;

        /// <summary> 分类给人看的样子 </summary>
        public string 种类名 => 种类 switch
        {
            调用种类C.用户函数 => "用户函数",
            调用种类C.内建函数 => "内建函数",
            调用种类C.宿主能力 => "宿主能力",
            _ => "未知(标签跳转)",
        };

        /// <summary> 调用点给人看的样子:`文件:行`;行号不可考时说明原因(不编一个假行号) </summary>
        public string 位置显示
        {
            get
            {
                string 文件 = 调用点文件.Length > 0 ? Path.GetFileName(调用点文件) : "(未知文件)";
                if (调用点行 > 0) return $"{文件}:{调用点行}";
                return 是库行 ? $"{文件}(包含进来的文件,行号不可考)" : $"{文件}(无源行)";
            }
        }

        /// <summary> 给自检/排查用的一行文本 </summary>
        public override string ToString() =>
            $"{调用者显示} → {被调显示} [{种类名}{(备注.Length > 0 ? ":" + 备注 : "")}] @{位置显示}";
    }

    /// <summary>
    /// 一次扫描的全部结果:边表 + 两个索引 + 几个查询口子。
    /// **纯数据、纯函数**:扫描过程不改任何编译器状态,也不会被编译器读回去(纯粹给 IDE 侧栏与自检用)。
    /// </summary>
    public sealed class 调用关系表C
    {
        /// <summary> 全部边(按"文件顺序 + 行出现顺序";同一调用点重复出现的已经去重) </summary>
        public List<调用边C> 边表 { get; } = [];

        /// <summary> 出边:**函数定义名 → 它调用了谁**。顶层那些边挂在空串键上(`""` = 顶层/程序段) </summary>
        public Dictionary<string, List<调用边C>> 出边 { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 入边:**被调方(有函数定义名就用定义名,没有就用标签) → 谁调用了它** </summary>
        public Dictionary<string, List<调用边C>> 入边 { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 某个函数调用了谁(出边);没有就返回空表 </summary>
        public IReadOnlyList<调用边C> 调用了谁(string? 函数定义名)
            => 出边.TryGetValue(函数定义名 ?? "", out var 表) ? 表 : [];

        /// <summary> 谁调用了这个函数(入边;参数可以是函数定义名,也可以是内建/宿主那样的标签);没有就返回空表 </summary>
        public IReadOnlyList<调用边C> 谁调用了(string? 定义名或标签)
            => 定义名或标签 != null && 入边.TryGetValue(定义名或标签, out var 表) ? 表 : [];

        /// <summary> 收一条边,顺手挂进两个索引 </summary>
        internal void 收(调用边C 边)
        {
            边表.Add(边);
            if (!出边.TryGetValue(边.调用者, out var 出)) 出边[边.调用者] = 出 = [];
            出.Add(边);
            string 被调键 = 边.被调定义名.Length > 0 ? 边.被调定义名 : 边.被调标签;
            if (!入边.TryGetValue(被调键, out var 入)) 入边[被调键] = 入 = [];
            入.Add(边);
        }
    }

    /// <summary>
    /// **调用边扫描器**:从一次编译的调试信息里扫出"谁调用了谁"。
    ///
    /// 原料就是现成的 `汇编调试结果C`(`调试文件表[].展开文本` 带 `// 来自文件K第 N 行` 标记 +
    /// `调试文件表[].名字表` 的 `函数范围` + 汇编器标签表)—— **不为它另外编译一遍**,
    /// 也不重新展开一次;编译结果里本来就有这些旁路信息。
    ///
    /// 三条判据(全部只看文本,不碰机器码):
    ///   · **调用点**:展开文本里行首词是 `调用`/`CALL` 的那一行(工程级展开一律写英文助记符,
    ///     但中文写法也认;词表取自 `指令C`,不另抄一份)。
    ///   · **调用者**:那一行标记里的"第 N 行" + 这个文件的 `名字表.行所属函数(N)`;不在任何函数里 = 顶层/程序段。
    ///   · **被调者**:`调用` 后面那个标签。能按"定义名里 `.` 换 `_`"对上 `函数范围` 就是**用户函数**;
    ///     对不上但确实是代码里的标签(内建例程)就是**内建函数**;`宿主调用 Rd, 功能号` 那条算**宿主能力**;
    ///     都不是就记**未知(标签跳转)**。
    ///
    /// 去重:同一个 `(调用者, 调用点文件, 调用点行, 被调标签)` 只留一条 —— 一行里写两次 `函数(x)` 只出一条边。
    ///
    /// **边界(如实记下)**:
    ///   · 行号取标记里的第一个 N(标记写成范围 `第 7 到 12 行` 时也取 7);
    ///   · 标记带 `库` 字(语句来自被 `包含` 进来的文件)时**行号置 0** —— 那是另一个文件的行号,
    ///     算到主文件头上就是错行号,比没有更糟;这类边的调用者因此只能记成"顶层/程序段"(见 `调用边C.是库行`);
    ///   · 只有**真发 `CALL` 的**才算边:`测试输出(数字)`、`列表.添加` 的一部分是原地展开的,
    ///     源码里写了也不会出现在这张表里;反过来说,表里的边一定对应机器码里真实的一条 `CALL`;
    ///   · **编译器生成的代码一律没有源行**:展开文本里那句 `// 编译器生成`(见 `源行标记C`)之后、
    ///     下一个真标记之前的那一段(程序开头的初始化 / 列表扩容与堆这些内部例程)按"行号不知道"算 ——
    ///     与 `执行行映射C` 同一条口径(单步走进生成代码不换行),不是扫描器另算的。
    ///     不这么算的话,生成区里哪天真发一条 `调用`,它会沿用**上一条真语句**的行号,
    ///     面板上就冒出一条"某函数在第 N 行调用了 `_fheapalloc…`"的假边。
    ///     (H6;第244批实测:以前这条口径没读 `编译器生成`,一直靠"展开器碰巧不往例程里发 CALL"兜着 ——
    ///     全 32 个标准工程逐个真编过一遍,生成区里的 `调用` 是 **0 条**,所以那之前也没人看见过这条假边。)
    /// </summary>
    public static class 调用关系扫描C
    {
        /// <summary> 行首词是"调用"的写法(工程级展开写英文;手写汇编里的中文写法也认) </summary>
        private static readonly string[] 调用词表 = [指令C.到英文指令名("调用"), "调用"];

        /// <summary> 行首词是"宿主调用"的写法(见 `指令C` 的 宿主调用 / `宿主能力C`) </summary>
        private static readonly string[] 宿主调用词表 = [指令C.到英文指令名("宿主调用"), "宿主调用"];

        /// <summary>
        /// 展开文本里的源行标记 —— **解析只有一份**:`引擎\源行标记C.cs`(唯一事实源)。
        /// 写法:`// 来自文件2第 12 行`(工程级)、`// 来自第 12 行`(单文件)、
        /// 范围写法 `// 来自第 7 到 12 行`,以及被 `包含` 的行尾多一个 `库`(见 `中间层C.发出`)。
        /// 一条语句的指令算在它**开头**那一行;带 `库` 的算"不知道行号"(另一个文件的行号,不能拿来当本文件的)。
        /// </summary>
        private static 源行标记C.标记 读标记(string 行) => 源行标记C.读(行);

        /// <summary> 扫一次编译结果(推荐口径:标签表齐全,内建/未知分得准) </summary>
        public static 调用关系表C 扫描(汇编调试结果C? 编译结果)
            => 编译结果 == null ? new 调用关系表C() : 扫描(编译结果.调试文件表, 编译结果.标签表);

        /// <summary>
        /// 只有调试文件表也能扫(标签表可以不给:那就按展开文本里"出现过的标签定义"认内建例程)。
        /// 这个重载给"没有机器码、只有调试段"的场合用。
        /// </summary>
        public static 调用关系表C 扫描(IReadOnlyList<文件映射C>? 调试文件表, IReadOnlyDictionary<string, int>? 标签表 = null)
        {
            var 表 = new 调用关系表C();
            if (调试文件表 == null || 调试文件表.Count == 0) return 表;

            // ① 用户函数的"汇编标签 ↔ 定义名":定义名里的 `.` 换成 `_`
            //    (与 `中间层C.汇编标签` 同一套规则 —— 体方法 `棋盘.面积` 编译出来就是标签 `棋盘_面积`)
            var 标签到定义名 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var 段 in 调试文件表)
                foreach (var (函数, _, _, _) in (段.名字表 ?? new 名字表C()).函数范围)
                    标签到定义名[函数.Replace('.', '_')] = 函数;

            // ② "这是个代码标签"的集合:汇编器标签表优先;没有就先把各段展开文本里的标签定义扫一遍
            //    (必须**先扫全**再判边:不然"先调用后定义"的标签会被误判成未知)
            var 已知标签 = new HashSet<string>(StringComparer.Ordinal);
            if (标签表 != null) foreach (string 名 in 标签表.Keys) 已知标签.Add(名);
            foreach (var 段 in 调试文件表) 收集标签(段.展开文本, 已知标签);

            // ③ 逐段扫边
            var 去过重 = new HashSet<string>(StringComparer.Ordinal);
            foreach (var 段 in 调试文件表) 扫一段(段, 标签到定义名, 已知标签, 去过重, 表);
            return 表;
        }

        // ---------------- 内部实现 ----------------

        /// <summary> 把一段展开文本里定义过的标签收进集合(只认"行首到冒号"那一段) </summary>
        private static void 收集标签(string? 展开文本, HashSet<string> 已知标签)
        {
            if (string.IsNullOrEmpty(展开文本)) return;
            foreach (string 行 in 展开文本.Split('\n'))
            {
                int 注释 = 行.IndexOf("//", StringComparison.Ordinal);
                string 净 = (注释 >= 0 ? 行[..注释] : 行).Trim();
                int 冒号 = 净.IndexOf(':');
                if (冒号 <= 0) continue;
                string 名 = 净[..冒号].Trim();
                if (名.Length > 0 && !名.Contains(' ')) 已知标签.Add(名);
            }
        }

        private static void 扫一段(文件映射C 段, Dictionary<string, string> 标签到定义名,
            HashSet<string> 已知标签, HashSet<string> 去过重, 调用关系表C 表)
        {
            string 文件 = 段.文件 ?? "";
            var 名字表 = 段.名字表 ?? new 名字表C();
            int 源行 = 0;          // 最近一次标记里的"第 N 行";0 = 不知道(标记之前 / 库行 / 编译器生成的代码)
            bool 是库行 = false;   // 最近一次标记带不带 `库`(语句来自被 `包含` 进来的文件)
            int 库行号 = 0;        // H47:库行时那个行号(属于**库文件**的编号);老标记没文件名时为 0
            string 库文件名 = "";  // H47:库行的文件名(标记尾巴上那个)

            foreach (string 原行 in (段.展开文本 ?? "").Split('\n'))
            {
                // 先读标记(它挂在指令后面当注释),再剥注释、去标签
                var 标记 = 读标记(原行);
                if (标记.生成)
                {
                    // 编译器生成的代码(程序开头的初始化 / 列表扩容与堆这些内部例程)从这儿起**没有源行**,
                    // 直到下一个真标记 —— 与 `执行行映射C` 同一条口径(单步走进生成代码不换行)。
                    // 不这么清的话,生成区里真发一条 `调用` 时,它会沿用**上一条真语句**的行号,
                    // 面板上就冒出一条"某函数在第 N 行调用了 `_fheapalloc…`"的假边(见上面的边界说明,H6)。
                    源行 = 0;
                    是库行 = false;
                    库行号 = 0;
                    库文件名 = "";
                }
                else if (标记.有)
                {
                    是库行 = 标记.来自库;
                    if (是库行)
                    {
                        // H47:库行的行号**不是本文件的**,不能算到本文件头上;但标记尾巴上带了库文件名时,
                        // 就能拿"库文件 + 库行号"去认**库函数**——库函数体里的调用边从此有调用者、有行号。
                        源行 = 0;
                        库行号 = 标记.起始行;
                        库文件名 = 标记.库文件;
                    }
                    else
                    {
                        源行 = 标记.起始行;
                        库行号 = 0;
                        库文件名 = "";
                    }
                }

                int 注释 = 原行.IndexOf("//", StringComparison.Ordinal);
                string 净 = (注释 >= 0 ? 原行[..注释] : 原行).Trim();
                if (净.Length == 0) continue;
                int 冒号 = 净.IndexOf(':');
                if (冒号 >= 0)
                {
                    净 = 净[(冒号 + 1)..].Trim();       // 标签与指令写在同一行:`起点: 返回` —— 标签不算操作数
                    if (净.Length == 0) continue;
                }

                var 词 = 净.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
                if (词.Length < 2) continue;

                if (是这几种词(词[0], 调用词表))
                {
                    收一条(表, 名字表, 文件, 源行, 是库行, 词[1], 标签到定义名, 已知标签, 去过重,
                        库行号: 库行号, 库文件名: 库文件名);
                }
                else if (是这几种词(词[0], 宿主调用词表) && 词.Length >= 3 && int.TryParse(词[2], out int 功能号))
                {
                    收一条(表, 名字表, 文件, 源行, 是库行, $"宿主能力#{功能号}", 标签到定义名, 已知标签, 去过重,
                        宿主种类: 功能号, 库行号: 库行号, 库文件名: 库文件名);
                }
            }
        }

        private static bool 是这几种词(string 词, string[] 词表)
        {
            foreach (string 候选 in 词表)
                if (string.Equals(词, 候选, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary> 收一条边(去重在这里;调用者从"这一行落在哪个函数里"算出来) </summary>
        private static void 收一条(调用关系表C 表, 名字表C 名字表, string 文件, int 源行, bool 是库行, string 被调标签,
            Dictionary<string, string> 标签到定义名, HashSet<string> 已知标签, HashSet<string> 去过重,
            int? 宿主种类 = null, int 库行号 = 0, string 库文件名 = "")
        {
            // H47:库行"跟着**哪一份库文件**的哪一行"—— 有文件名就按 (行号, 库文件) 认调用者(库函数),
            // 没有(老标记 / 单文件展开)就退回原来那条"行号不可考、调用者算顶层"。
            bool 认得库行 = 是库行 && 库行号 > 0 && 库文件名.Length > 0;
            string 调用者 = 源行 > 0 ? (名字表.行所属函数(源行) ?? "")
                : 认得库行 ? (名字表.行所属函数(库行号, 库文件名) ?? "") : "";
            string 边文件 = 认得库行 ? 库文件名 : 文件;
            int 边行 = 认得库行 ? 库行号 : 源行;
            if (!去过重.Add($"{调用者}\u0001{边文件}\u0001{边行}\u0001{被调标签}")) return;

            var 边 = new 调用边C
            {
                调用者 = 调用者,
                调用点文件 = 边文件,
                调用点行 = 边行,
                被调标签 = 被调标签,
                是库行 = 是库行,
            };
            if (宿主种类 is { } 号)
            {
                边.种类 = 调用种类C.宿主能力;
                边.备注 = 宿主功能名(号);
            }
            else if (标签到定义名.TryGetValue(被调标签, out string? 定义名))
            {
                边.种类 = 调用种类C.用户函数;
                边.被调定义名 = 定义名;
            }
            else if (已知标签.Contains(被调标签))
            {
                边.种类 = 调用种类C.内建函数;
                边.备注 = 内建例程说明(被调标签);
            }
            else
            {
                边.种类 = 调用种类C.未知;
            }
            表.收(边);
        }

        /// <summary> 宿主功能号 → 给人看的名字(功能号的含义以 `宿主能力C` 为准,不另立一份口径) </summary>
        public static string 宿主功能名(int 功能号) => 功能号 switch
        {
            宿主能力C.功能_读文件 => "读文件",
            宿主能力C.功能_写文件 => "写文件",
            宿主能力C.功能_当前时间 => "当前时间",
            宿主能力C.功能_追加写文件 => "追加写文件",
            宿主能力C.功能_建目录 => "建目录",
            宿主能力C.功能_删文件 => "删文件",
            宿主能力C.功能_列目录 => "列目录",
            宿主能力C.功能_环境变量 => "环境变量",
            // R1-6 批 1:图形界面的 10 条原语(功能号 60~69)
            图形宿主C.功能_打开窗口 => "打开窗口",
            图形宿主C.功能_关闭窗口 => "关闭窗口",
            图形宿主C.功能_清屏 => "清屏",
            图形宿主C.功能_画文字 => "画文字",
            图形宿主C.功能_画矩形 => "画矩形",
            图形宿主C.功能_画线 => "画线",
            图形宿主C.功能_刷新 => "刷新",
            图形宿主C.功能_取事件 => "取事件",
            图形宿主C.功能_等待 => "等待",
            图形宿主C.功能_取文字宽度 => "取文字宽度",
            _ => $"功能号 {功能号}",
        };

        /// <summary> 编译器内建例程的标签 → "它是干什么用的"(标签名本身就是中文,这里只把 `_f…N` 说清楚) </summary>
        public static string 内建例程说明(string 标签)
        {
            if (标签.Contains("打印串", StringComparison.Ordinal) || 标签.Contains("打印到零", StringComparison.Ordinal))
                return "打印字符串的内建例程(输出 / 输出行 走的这条)";
            if (标签.Contains("读行", StringComparison.Ordinal)) return "读取行的内建例程";
            if (标签.Contains("读数字", StringComparison.Ordinal)) return "读取数字的内建例程";
            if (标签.Contains("列表扩容", StringComparison.Ordinal)) return "列表扩容的内建例程";
            if (标签.Contains("heapalloc", StringComparison.OrdinalIgnoreCase)) return "堆分配的内建例程(申请 走的这条)";
            if (标签.Contains("heapfree", StringComparison.OrdinalIgnoreCase)) return "堆释放的内建例程(释放 走的这条)";
            return "编译器内建例程";
        }
    }
}
