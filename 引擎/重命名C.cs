using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 一次重命名算出来的**编辑清单**:`编辑` = 要改的每一处(顺序确定:文件 → 行号 → 列),
    /// `问题` = 名字撞车之类"不硬改"的提醒。`可改` 为真时,调用方可以直接照着 `编辑` 改文本。
    ///
    /// 注意:它**只算清单** —— 不写盘、不碰编辑器、不动任何编译器状态(界面接线早已接上:
    /// `MainWindow.问题表与重命名.cs` 的七步 `执行重命名`)。
    /// 编辑项本身与"怎么套用"都在 `引擎\编辑清单C.cs`(与文本替换共用一套)。
    /// </summary>
    public sealed class 重命名清单C
    {
        /// <summary> 要改的每一处(文件 → 行号 → 列,顺序确定) </summary>
        public List<编辑项C> 编辑 { get; } = [];

        /// <summary> 办不到 / 要提醒的事(非空则 `可改` 为假、`编辑` 为空) </summary>
        public List<编辑问题C> 问题 { get; } = [];

        /// <summary> 能不能照这份清单硬改(有问题就不硬改) </summary>
        public bool 可改 => 问题.Count == 0;

        /// <summary> 一共要改几处 </summary>
        public int 处数 => 编辑.Count;

        /// <summary> 给自检/排查用的一行文本 </summary>
        public override string ToString() =>
            问题.Count > 0 ? $"不能改:{string.Join(" | ", 问题)}" : $"改 {编辑.Count} 处";

        /// <summary> 把这份清单整体套到文件表上(纯函数;实现见 `编辑清单C.套用`:核对每处原词,套两遍等于套一遍) </summary>
        public List<(string 名称, string 内容)> 应用(IReadOnlyList<(string 名称, string 内容)> 文件表)
            => 编辑清单C.套用(编辑, 文件表);
    }

    /// <summary>
    /// **重命名计算器**:给"旧名 / 新名 / 工程文件表",算出**到底要改哪几处地方**。
    ///
    /// 这个类**只算编辑清单**,一个字节都不写:不写盘、不碰编辑器、不改编译器状态。
    /// 给同样的输入必给同样的输出(顺序定死:文件 → 行号 → 列);界面接线(跨文件撤销事务等)见 Q4c。
    ///
    /// 三条硬要求:
    ///   ① **绝不改注释与字符串字面量** —— 用 `代码解析器.取保护改写区`(注释 ∪ 字符串/字符字面量)
    ///      圈出"原样保留"的区间,落在这个区间里的命中一律跳过。`输出行("求和")` 里的 `求和`
    ///      与 `// 求和 的说明` 里的 `求和` 一个字符都不许动。
    ///   ② **整词匹配** —— 口径与 `检查器C.cs` 的 `出现次数` 一致:`(?<![\p{L}\p{N}_])词(?![\p{L}\p{N}_])`,
    ///      前后都不能是字母/数字/下划线。所以 `x求和`、`求和x`、`求和_1` 都**不算**同名,不会被改。
    ///      (`中间层指令C.cs` 那份整词正则少了一个下划线,这里取更严的那份。)
    ///   ③ **带模块前缀的名字与裸名都要能命中**(可用 `含模块前缀名` 开关关掉):
    ///      · 旧名给的是 `数学.求和` → `数学.求和` 整个换掉;模块内那种裸写的 `求和` 也换(只换尾巴那一段);
    ///        裸写形态**前面紧挨着点就不算**(`物理.求和` 不动 —— 那是别的模块的同名函数)。
    ///      · 旧名给的是裸名 `求和` → 裸写的换掉,`模块.求和` 这种也换(只换尾巴那一段)。
    ///        ⚠ 这意味着**所有** `模块.求和` 都会被改到,包括别的模块的同名函数 —— 这是"两种写法都要命中"
    ///        这个要求的直接后果。调用方(界面/命令行)有符号表时应当传**带模块前缀的旧名**,或者把开关关掉。
    ///
    /// 幂等与可逆:
    ///   · 幂等 —— 清单是在**原文**上算的;套用后旧名已经不在了,再算一次得到空清单
    ///     (`编辑清单C.套用` 还额外核对每处的原词,套两遍等于套一遍)。
    ///   · 可逆 —— 把新名再当旧名、旧名当新名算一遍套回去,文本与原文**逐字节一致**
    ///     (判据是 `编辑项C.原词` + 同口径的整词匹配,不靠"记住位置")。
    ///
    /// 名字撞车(新名已经存在):**只看文本**分不清作用域(同一个名字可能是另一个模块的函数、
    /// 或者是某个变量),所以**报一条问题就不硬改**(`重命名清单C.编辑` 保持为空)。
    /// </summary>
    public static class 重命名C
    {
        /// <summary> 什么算"一个名字":字母/下划线开头,后面字母/数字/下划线,可以用点分段(与 `中间层C` 的标识符口径一致) </summary>
        private static readonly Regex 名字正则 = new(
            @"^[\p{L}_][\p{L}\p{N}_]*(\.[\p{L}_][\p{L}\p{N}_]*)*$", RegexOptions.Compiled);

        /// <summary>
        /// 算一份编辑清单。
        /// `只改文件` 不给(或给空表)= 文件表里全都算;**给了就只在这些文件里出编辑项**
        /// (文件表里其余文件仍然参与"新名是不是已经存在"的检查,免得改出一个跨文件重名)。
        /// `含模块前缀名` 默认开:带前缀的写法与裸名两种形态都命中(见类注释)。
        /// </summary>
        public static 重命名清单C 计算(string 旧名, string 新名,
            IReadOnlyList<(string 名称, string 内容)>? 文件表,
            IReadOnlyList<string>? 只改文件 = null, bool 含模块前缀名 = true)
        {
            var 清单 = new 重命名清单C();
            string 旧 = (旧名 ?? "").Trim();
            string 新 = (新名 ?? "").Trim();

            if (!名字正则.IsMatch(旧))
            {
                清单.问题.Add(new 编辑问题C
                {
                    说明 = $"旧名 `{旧}` 不像一个函数/变量名:只允许『字母或下划线开头,后面字母/数字/下划线,并且可以用点分段』" +
                           "(例如 `求和`、`数学.求和`)。请从编辑器里选中一个真名字再重命名。",
                });
                return 清单;
            }
            if (!名字正则.IsMatch(新))
            {
                清单.问题.Add(new 编辑问题C
                {
                    说明 = $"新名 `{新}` 不像一个函数/变量名:只允许『字母或下划线开头,后面字母/数字/下划线,并且可以用点分段』" +
                           "(例如 `加和`、`数学.加和`)。请换一个合法的名字。",
                });
                return 清单;
            }
            if (旧 == 新)
            {
                清单.问题.Add(new 编辑问题C
                {
                    说明 = $"新名与旧名一样(都是 `{旧}`),没有要改的地方 —— 想改名就把新名写成一个不一样的名字。",
                });
                return 清单;
            }
            if (文件表 == null || 文件表.Count == 0)
            {
                清单.问题.Add(new 编辑问题C { 说明 = "没有给任何源文件,算不出编辑清单。" });
                return 清单;
            }

            // ① 名字撞车:新名(或它的裸名形态)在**代码区**里已经出现过 → 不硬改
            foreach (var (名称, 内容) in 文件表)
                foreach (var (行号, 列, 词) in 找撞车(内容 ?? "", 新, 含模块前缀名))
                {
                    清单.问题.Add(new 编辑问题C
                    {
                        文件 = 名称,
                        行号 = 行号,
                        说明 = $"新名 `{新}` 在这儿已经出现(`{词}`),同一个名字会撞上。" +
                               "本工具只看文本、分不清作用域(那一处可能是别的模块的同名函数、或者一个变量)," +
                               "所以**不硬改** —— 换个新名,或者先确认那一处是什么再手工处理。",
                    });
                    break;      // 一个文件报一条就够(位置给人看,不用刷屏)
                }
            if (清单.问题.Count > 0) return 清单;

            // ② 逐文件找要改的地方(注释/字符串一律跳过)
            var 形态 = 取形态(旧, 新, 含模块前缀名);
            var 去过重 = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (名称, 内容) in 文件表)
            {
                if (!该改这个文件(名称, 只改文件)) continue;
                string 文本 = 内容 ?? "";
                if (文本.Length == 0) continue;
                var 保护区 = 代码解析器.取保护改写区(文本);
                var 行首表 = 编辑清单C.行首下标表(文本);

                foreach (var (正则, 新词) in 形态)
                    foreach (Match 匹配 in 正则.Matches(文本))
                    {
                        if (编辑清单C.在保护区内(保护区, 匹配.Index)) continue;                 // 注释 / 字符串:一个字符都不动
                        if (string.Equals(匹配.Value, 新词, StringComparison.Ordinal)) continue;   // 换了等于没换
                        if (!去过重.Add($"{名称}\u0001{匹配.Index}\u0001{匹配.Length}\u0001{新词}")) continue;
                        清单.编辑.Add(编辑清单C.造编辑项(名称, 文本, 行首表, 匹配.Index, 匹配.Length, 新词));
                    }
            }

            编辑清单C.排好(清单.编辑);
            return 清单;
        }

        // ---------------- 内部实现 ----------------

        /// <summary>
        /// 要匹配的形态表:(正则, 替换成什么)。口径与判据见类注释 ③。
        /// 带点旧名会额外多一条"裸尾"形态(只换尾巴,且**前面紧挨着点就不算** —— 那是别的模块的同名函数)。
        /// </summary>
        private static List<(Regex 正则, string 新词)> 取形态(string 旧名, string 新名, bool 含模块前缀名)
        {
            var 形态 = new List<(Regex 正则, string 新词)>();
            string 新尾 = 末段(新名);
            if (旧名.Contains('.'))
            {
                形态.Add((整词正则(旧名), 新名));
                if (含模块前缀名) 形态.Add((裸词正则(末段(旧名)), 新尾));
            }
            else if (含模块前缀名)
            {
                形态.Add((整词正则(旧名), 新尾));
            }
            else
            {
                形态.Add((裸词正则(旧名), 新尾));
            }
            return 形态;
        }

        /// <summary> 新名的哪些形态算"已经存在"(与改写形态同一套口径,见类注释 ③) </summary>
        private static List<(Regex 正则, string 用于显示)> 取新名形态(string 新名, bool 含模块前缀名)
        {
            var 形态 = new List<(Regex 正则, string 用于显示)>();
            if (新名.Contains('.'))
            {
                形态.Add((整词正则(新名), 新名));
                if (含模块前缀名) 形态.Add((裸词正则(末段(新名)), 末段(新名)));
            }
            else if (含模块前缀名)
            {
                形态.Add((整词正则(新名), 新名));
            }
            else
            {
                形态.Add((裸词正则(新名), 新名));
            }
            return 形态;
        }

        /// <summary> 新名在代码区里哪儿已经出现了(注释/字符串不算);返回 (行号, 列, 命中的词) </summary>
        private static List<(int 行号, int 列, string 词)> 找撞车(string 文本, string 新名, bool 含模块前缀名)
        {
            var 结果 = new List<(int, int, string)>();
            if (文本.Length == 0) return 结果;
            var 保护区 = 代码解析器.取保护改写区(文本);
            var 行首表 = 编辑清单C.行首下标表(文本);
            foreach (var (正则, 显示) in 取新名形态(新名, 含模块前缀名))
                foreach (Match 匹配 in 正则.Matches(文本))
                {
                    if (编辑清单C.在保护区内(保护区, 匹配.Index)) continue;
                    var (行号, 列) = 编辑清单C.定位(行首表, 匹配.Index);
                    结果.Add((行号, 列, 显示));
                }
            结果.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.CompareTo(b.Item2));
            return 结果;
        }

        /// <summary> 整词口径(与 `检查器C` 的出现次数一致):前后都不能是字母/数字/下划线 </summary>
        private static Regex 整词正则(string 词) => new(
            $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(词)}(?![\p{{L}}\p{{N}}_])", RegexOptions.Compiled);

        /// <summary> 更严的一份:前面**连点也不能有** —— 用来只命中"模块内部那种裸写"(不动 `别的模块.同名`) </summary>
        private static Regex 裸词正则(string 词) => new(
            $@"(?<![\p{{L}}\p{{N}}_.]){Regex.Escape(词)}(?![\p{{L}}\p{{N}}_])", RegexOptions.Compiled);

        /// <summary> 取点分名字的最后一段(`数学.求和` → `求和`;不带点就是它自己) </summary>
        private static string 末段(string 名)
        {
            int 点 = 名.LastIndexOf('.');
            return 点 < 0 ? 名 : 名[(点 + 1)..];
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
