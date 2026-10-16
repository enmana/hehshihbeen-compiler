using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 查找/替换的**选项**。默认都取"保守"那一档:**保护注释与字符串 = 开**(与 `重命名C` 同一个口径 ——
    /// 代码里的文字才改,注释与字符串里的原样留);区分大小写默认**关**(与编辑器自带的搜索面板一致)。
    /// </summary>
    public sealed class 查找设置C
    {
        /// <summary> 区分大小写(默认不区分) </summary>
        public bool 区分大小写;

        /// <summary> 整词匹配(前后不能是字母/数字/下划线;与 `重命名C` 的整词口径一致) </summary>
        public bool 整词;

        /// <summary>
        /// **按正则找**(H5,默认关 = 按字面量)。开着时"要找"按**正则**解释 ——
        /// 与编辑器自带的 `Ctrl+F` 查找面板**同一套口径**(都是 .NET 正则:字符类 `[abc]` / `[^abc]` / `[a-z]`、
        /// 量词 `*` `+` `?` `{n,m}`、分组 `( )` 与非捕获 `(?: )`、或 `|`、锚点 `^` `$`、
        /// 简写 `\d` `\w` `\s` 与转义 `\.` `\*` / `\\` 等);
        /// 替换内容里的 `$1` / `$2` / `${名}` 会**按这一处命中的分组展开**,`$$` 写一个 `$`;
        /// 关着时一律字面量(`.*` 就是两个普通字符)、替换内容原样写入。
        /// **一条边界**(如实记,不硬改):**命中跨行的替换这一档没做** ——
        /// 一条命中里带了换行就当场报一条问题、清单留空(只找那一半不受限,可以跨行找)。
        /// </summary>
        public bool 正则;

        /// <summary> 注释与字符串字面量里的命中**跳过**(默认开;关掉就是"连注释一起换",危险,界面上要问一次) </summary>
        public bool 保护注释与字符串 = true;

        public 查找设置C 副本() => new() { 区分大小写 = 区分大小写, 整词 = 整词, 正则 = 正则, 保护注释与字符串 = 保护注释与字符串 };

        public override string ToString() =>
            (正则 ? "按正则 · " : "")
            + (区分大小写 ? "区分大小写" : "不区分大小写") + (整词 ? " · 整词" : "")
            + (保护注释与字符串 ? " · 保护注释与字符串" : " · 连注释/字符串一起改");
    }

    /// <summary> 查找的**一处命中**(当前文件用:给"下一个 / 上一个"跳转与计数;带行号列号与那一行原文)</summary>
    public sealed class 查找命中C
    {
        /// <summary> 行号(1 基) </summary>
        public int 行号;

        /// <summary> 列(0 基,按 .NET 字符数算 —— 与编辑器列口径一致) </summary>
        public int 列;

        /// <summary> 命中那一段有多长(字符数) </summary>
        public int 长度;

        /// <summary> 命中的原文(不区分大小写时,它可能与"要找的词"大小写不同)</summary>
        public string 原文 = "";

        /// <summary> 这一行的原文(不含行尾换行) </summary>
        public string 行文本 = "";

        /// <summary> 给界面/自检用的一行文本 </summary>
        public override string ToString() => $"{行号}:{列 + 1} `{原文}`";
    }

    /// <summary>
    /// 一次"全工程替换"算出来的**编辑清单**(与 `重命名清单C` 同构:先看 `问题`,再看 `编辑`)。
    /// 只算清单:不写盘、不碰编辑器。套用走 `编辑清单C.套用`(核对每处原词,套两遍等于套一遍)。
    /// </summary>
    public sealed class 查找替换清单C
    {
        /// <summary> 要改的每一处(文件 → 行号 → 列,顺序确定) </summary>
        public List<编辑项C> 编辑 { get; } = [];

        /// <summary> 办不到 / 要提醒的事(非空则 `可改` 为假、`编辑` 为空) </summary>
        public List<编辑问题C> 问题 { get; } = [];

        /// <summary> 这次用的设置(预览里要写出来,免得看的人不知道是哪种口径) </summary>
        public 查找设置C 设置 { get; set; } = new();

        public bool 可改 => 问题.Count == 0;
        public int 处数 => 编辑.Count;

        /// <summary> 涉及哪几个文件(去重;清单已按 文件 → 行号 排好,所以顺序也确定)</summary>
        public List<string> 涉及文件
        {
            get
            {
                var 表 = new List<string>();
                foreach (var 项 in 编辑)
                    if (!表.Contains(项.文件, StringComparer.OrdinalIgnoreCase)) 表.Add(项.文件);
                return 表;
            }
        }

        /// <summary>
        /// 给人看的**预览文本**(界面写输出框 / 命令行打印共用一份,免得两处格式慢慢走偏)。
        /// `带行` = 每处多打一行"旧行 → 新行";`最多` = 最多打几处(超出只报个数)。
        /// </summary>
        public string 预览(bool 带行 = false, int 最多 = 200)
        {
            if (问题.Count > 0)
            {
                var 问 = new StringBuilder($"替换没有执行(一个字符都没改),先解决下面 {问题.Count} 个问题:\n");
                foreach (var p in 问题) 问.AppendLine("  · " + p);
                return 问.ToString().TrimEnd('\n');
            }
            var 文 = new StringBuilder();
            文.AppendLine($"共 {处数} 处,涉及 {涉及文件.Count} 个文件(设置:{设置})");
            int 打了 = 0;
            foreach (var 项 in 编辑)
            {
                if (打了 >= 最多) { 文.AppendLine($"  …(还有 {处数 - 打了} 处,只列前 {最多} 处)"); break; }
                文.AppendLine($"  {System.IO.Path.GetFileName(项.文件)}:{项.行号}:{项.列 + 1}  `{项.原词}` → `{项.新词}`");
                if (带行 && !string.Equals(项.旧行, 项.新行, StringComparison.Ordinal))
                    文.AppendLine($"      - {项.旧行}\n      + {项.新行}");
                打了++;
            }
            return 文.ToString().TrimEnd('\n');
        }

        /// <summary> 把清单整体套到文件表上(纯函数,见 `编辑清单C.套用`) </summary>
        public List<(string 名称, string 内容)> 应用(IReadOnlyList<(string 名称, string 内容)> 文件表)
            => 编辑清单C.套用(编辑, 文件表);

        public override string ToString() =>
            问题.Count > 0 ? $"不能替换:{string.Join(" | ", 问题)}" : $"替换 {编辑.Count} 处 / {涉及文件.Count} 个文件";
    }

    /// <summary>
    /// **查找 / 替换计算器**(P21):当前文件的"找哪几处"(给下一个/上一个跳转),以及**全工程**要改的编辑清单。
    ///
    /// 只算不写:不写盘、不碰编辑器、不动编译器状态;同样的输入给同样的输出(顺序:文件 → 行号 → 列)。
    ///
    /// 口径:
    ///   · **字面量查找**(默认),不是正则 —— 要找的内容里的 `.*` 之类就是普通字符;
    ///     要按正则找就把 `查找设置C.正则` 打开(H5:与编辑器自带的 `Ctrl+F` 查找面板同一套 .NET 正则口径,
    ///     替换内容里的 `$1` 展开成分组);
    ///   · `保护注释与字符串` 默认**开**:用 `代码解析器.取保护改写区` 跳过注释 ∪ 字符串/字符字面量,
    ///     与 `重命名C` 同一套判据;**命中跨进保护区的两头都查**(正则那档能一次命中一大段);
    ///   · 不区分大小写时按 `RegexOptions.IgnoreCase`(中文不受影响;`ASCII` 大小写互认);
    ///   · 命中的区间**不重叠**(`"aa"` 找 `"aa"` 只算一处),与编辑器搜索面板的手感一致;
    ///   · 要替换成的内容:字面量档**原样写入**;正则档按 `$1` / `${名}` **展开分组**;
    ///   · 正则那档带**超时护栏**(`正则超时`,默认 3 秒):一条正则算不完就报人话,不让灾难性回溯把界面卡死;
    ///   · **没做的那一档**:正则命中**跨行**时不替换(编辑清单是按行组织的),当场报一条问题、不硬改。
    /// </summary>
    public static class 查找替换C
    {
        /// <summary> 正则那一档的**超时护栏**(H5):一条正则最多算这么久;超了当场报人话(防 `(a+)+b` 那类灾难性回溯) </summary>
        public static readonly TimeSpan 正则超时 = TimeSpan.FromSeconds(3);

        /// <summary>
        /// 把"要找"按当前设置编一次(不匹配任何文本,只看编不编得出)。编不出(正则写错)⇒ `false` 且 `说明` 是人话 + 改法。
        /// 字面量档永远编得出(它走 `Regex.Escape`)。**命令行 / 界面动手之前先问这一句**,就能把"正则写错"当场报出来;
        /// `查找` / `替换清单` 自己也会查(`查找` 编不出就返回空表,`替换清单` 记一条问题)。
        /// </summary>
        public static bool 试编正则(string 要找, 查找设置C? 设置, out string 说明)
        {
            说明 = "";
            设置 ??= new 查找设置C();
            if (string.IsNullOrEmpty(要找))
            {
                说明 = "要找的内容是空的 —— 写点东西再找(空串会命中每一个位置)。";
                return false;
            }
            try { _ = 造正则(要找, 设置); return true; }
            catch (ArgumentException 异常)
            {
                说明 = 设置.正则
                    ? $"这条正则编不出来:`{要找}`(引擎原话:{异常.Message})。" +
                      "改法:检查圆括号与方括号是否配对、量词 `*` `+` `?` `{n,m}` 前面有没有东西可重复、" +
                      "`\\` 转义有没有写完整;拿不准就先关掉「按正则」按字面量找一次。"
                    : $"按字面量找时不该编不出来:`{要找}`(引擎原话:{异常.Message})—— 这是内部问题,请回报。";
                return false;
            }
        }

        /// <summary> 当前文件里所有命中(带行号列号与行文本,给"下一个 / 上一个"用) </summary>
        public static List<查找命中C> 查找(string? 文本, string 要找, 查找设置C? 设置 = null)
        {
            设置 ??= new 查找设置C();
            var 结果 = new List<查找命中C>();
            string 文 = 文本 ?? "";
            if (文.Length == 0 || string.IsNullOrEmpty(要找)) return 结果;
            // 正则写错 / 算超时:返回空表(要跟用户说话请先问 `试编正则`;界面那条"替换"走的是 `替换清单`,它会给问题)
            if (!试编正则(要找, 设置, out _)) return 结果;

            var 保护区 = 设置.保护注释与字符串 ? 代码解析器.取保护改写区(文) : new List<(int 开始, int 结束)>();
            var 行首表 = 编辑清单C.行首下标表(文);
            try
            {
                foreach (Match 匹配 in 造正则(要找, 设置).Matches(文))
                {
                    if (命中在保护区内(保护区, 匹配)) continue;
                    var (行号, 列) = 编辑清单C.定位(行首表, 匹配.Index);
                    int 行起 = 行首表[行号 - 1];
                    int 行止 = 行号 < 行首表.Count ? 行首表[行号] : 文.Length;
                    结果.Add(new 查找命中C
                    {
                        行号 = 行号,
                        列 = 列,
                        长度 = 匹配.Length,
                        原文 = 匹配.Value,
                        行文本 = 文[行起..行止].TrimEnd('\r', '\n'),
                    });
                }
            }
            catch (RegexMatchTimeoutException) { /* 超时:给到手的这些(要报给用户就自己先问 `试编正则` 之外的耗时口径) */ }
            return 结果;
        }

        /// <summary>
        /// 算出**替换清单**(当前文件给单个文件的表、全工程给工程文件表 —— 同一份代码,界面两处都用它)。
        /// `只改文件` 不给 = 文件表里全都算(与 `重命名C.计算` 同一个口径)。
        /// </summary>
        public static 查找替换清单C 替换清单(string 要找, string 换成,
            IReadOnlyList<(string 名称, string 内容)>? 文件表,
            查找设置C? 设置 = null, IReadOnlyList<string>? 只改文件 = null)
        {
            设置 ??= new 查找设置C();
            var 清单 = new 查找替换清单C { 设置 = 设置.副本() };
            string 找 = 要找 ?? "";
            string 换 = 换成 ?? "";

            if (找.Length == 0)
            {
                清单.问题.Add(new 编辑问题C { 说明 = "要找的内容是空的 —— 写点东西再找(空串会命中每一个位置)。" });
                return 清单;
            }
            // 正则档不拦"找 == 换":换成里写的是**替换文本**(可以是字面量 `\s+`、也可以带 `$1` 分组),与图形不是一回事
            if (!设置.正则 && string.Equals(找, 换, StringComparison.Ordinal))
            {
                清单.问题.Add(new 编辑问题C { 说明 = $"要找的和要换的是一样的(`{找}`),没有要改的地方。" });
                return 清单;
            }
            if (!试编正则(找, 设置, out string 正则说明))
            {
                清单.问题.Add(new 编辑问题C { 说明 = 正则说明 });
                return 清单;
            }
            if (文件表 == null || 文件表.Count == 0)
            {
                清单.问题.Add(new 编辑问题C { 说明 = "没有给任何源文件,算不出替换清单。" });
                return 清单;
            }

            var 匹配器 = 造正则(找, 设置);
            foreach (var (名称, 内容) in 文件表)
            {
                if (!该改这个文件(名称, 只改文件)) continue;
                string 文本 = 内容 ?? "";
                if (文本.Length == 0) continue;
                var 保护区 = 设置.保护注释与字符串 ? 代码解析器.取保护改写区(文本) : new List<(int 开始, int 结束)>();
                var 行首表 = 编辑清单C.行首下标表(文本);
                try
                {
                    foreach (Match 匹配 in 匹配器.Matches(文本))
                    {
                        if (命中在保护区内(保护区, 匹配)) continue;
                        // 没做的那一档(如实报,不硬改):编辑清单是按(行,列)组织的,跨行的命中摆不进这一格
                        if (匹配.Value.Contains('\n'))
                        {
                            清单.编辑.Clear();
                            清单.问题.Add(new 编辑问题C
                            {
                                说明 = "这条正则的命中**跨了行**(第 " + 编辑清单C.定位(行首表, 匹配.Index).行号 +
                                       " 行那一处里带换行),跨行替换这一档没做 —— " +
                                       "改法:把正则收紧到一行里(比如用 `[^\\n]*` 代替 `[\\s\\S]*`)," +
                                       "或者把那一段手工改;只想找不想换可以照旧用 `--查找`(只找不分行)。",
                                文件 = 名称,
                            });
                            return 清单;
                        }
                        string 新 = 换;
                        if (设置.正则)
                        {
                            string? 展 = 展开分组(匹配, 换, out string 展开说明);
                            if (展 == null)
                            {
                                清单.编辑.Clear();
                                清单.问题.Add(new 编辑问题C
                                {
                                    说明 = 展开说明,
                                    文件 = 名称,
                                    行号 = 编辑清单C.定位(行首表, 匹配.Index).行号,
                                });
                                return 清单;
                            }
                            新 = 展;
                        }
                        if (string.Equals(匹配.Value, 新, StringComparison.Ordinal)) continue;   // 换了等于没换
                        清单.编辑.Add(编辑清单C.造编辑项(名称, 文本, 行首表, 匹配.Index, 匹配.Length, 新));
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    清单.编辑.Clear();
                    清单.问题.Add(new 编辑问题C
                    {
                        说明 = $"这条正则在这份文本上算超过 {正则超时.TotalSeconds:0} 秒还没算完 —— 多半是量词嵌套成了灾难性回溯" +
                               "(形如 `(a+)+`)。改法:把嵌套量词拆开、用更具体的字符类收紧范围,或者分几步替换。",
                        文件 = 名称,
                    });
                    return 清单;
                }
            }
            编辑清单C.排好(清单.编辑);
            return 清单;
        }

        /// <summary> 命令行的 `--查找` 用:把"要找/换成/选项/只改哪些文件"一次算完并给一段预览 </summary>
        public static string 预览文本(string 要找, string 换成, IReadOnlyList<(string 名称, string 内容)>? 文件表,
            查找设置C? 设置 = null, IReadOnlyList<string>? 只改文件 = null, bool 带行 = false, int 最多 = 200)
            => 替换清单(要找, 换成, 文件表, 设置, 只改文件).预览(带行, 最多);

        /// <summary>
        /// 造匹配正则:字面量(**先转义**)或按正则**原样** + 可选整词 + 可选忽略大小写 + **超时护栏**。
        /// 正则写错时抛 `ArgumentException`(调用方先问 `试编正则` 或自己接住)。
        /// </summary>
        internal static Regex 造正则(string 要找, 查找设置C 设置)
        {
            string 图 = 设置.正则 ? 要找 : Regex.Escape(要找);
            // 整词那两条环视:正则档得把图形包一层非捕获组,不然 `a|b` 会跟环视接错边
            if (设置.整词) 图 = $@"(?<![\p{{L}}\p{{N}}_])(?:{图})(?![\p{{L}}\p{{N}}_])";
            var 选项 = RegexOptions.Compiled;
            if (!设置.区分大小写) 选项 |= RegexOptions.IgnoreCase;
            return new Regex(图, 选项, 正则超时);
        }

        /// <summary>
        /// 这一处命中是不是落在注释 / 字符串里(**两头都查**) —— 正则那档能一次命中一大段,
        /// 只查命中开头会放过"从代码里起、扎进注释里"那种半截命中。
        /// </summary>
        private static bool 命中在保护区内(List<(int 开始, int 结束)> 保护区, Match 匹配)
            => 编辑清单C.在保护区内(保护区, 匹配.Index)
               || (匹配.Length > 0 && 编辑清单C.在保护区内(保护区, 匹配.Index + 匹配.Length - 1));

        /// <summary>
        /// 正则档:把 `换` 里的 `$1` / `${名}` 按**这一处命中的分组**展开(`$$` = 一个 `$`)。
        /// 展开不出来(写法认不出来)⇒ 返回 null 且 `说明` 是人话 + 改法。
        /// </summary>
        private static string? 展开分组(Match 匹配, string 换, out string 说明)
        {
            说明 = "";
            if (换.IndexOf('$') < 0) return 换;      // 99% 的替换内容里没有 `$`,省一次展开
            try { return 匹配.Result(换); }
            catch (ArgumentException 异常)
            {
                说明 = $"换成里的分组写法认不出来:`{换}`(引擎原话:{异常.Message})。" +
                       "改法:按分组出现的次序写 `$1` / `$2`(`$0` = 整条命中),或者用 `${名字}`;" +
                       "要一个真正的 `$` 就写 `$$`。";
                return null;
            }
        }

        /// <summary> 这个文件在不在"只改哪些文件"里(不给名单 = 全都改;名字按"全名相同或文件名相同"认) </summary>
        private static bool 该改这个文件(string 名称, IReadOnlyList<string>? 只改文件)
        {
            if (只改文件 == null || 只改文件.Count == 0) return true;
            foreach (string 候选 in 只改文件)
            {
                if (string.Equals(候选, 名称, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(System.IO.Path.GetFileName(候选), System.IO.Path.GetFileName(名称), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
