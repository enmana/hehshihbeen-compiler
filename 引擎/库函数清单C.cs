// ============================================================
// 库函数清单C —— `关键字与方法清单.md` 里**由库源码现场生成**的那四个节
//   (§6 库函数 / §7 结构 / §8 命名常量 / §9 库一览)
//
// 为什么要有这一处:
//   · 2026-10-05 用户问"帮助里不把库的方法一个个列出来,我怎么知道每个库有哪些方法" ——
//     追下去发现两处都欠:① 帮助站点《类库》每页只渲染头注释的**前 512 字**(方法单子被切掉);
//     ② 这份清单的 §6/§9 是 `工具\临时\dbg1\探针` 导出的,而 `工具\临时\` **不进版本库** ⇒
//     那个探针一删,清单就永远停在最后跑过的那一天(实测停在 5 个库 229 个方法,批 5~15 全没进去);
//   · 所以:**数据一律取自 `Sample\库\*.hb` 的源码** —— 库里加一个 `方法` / `结构` / 命名常量,
//     这几节跟帮助站点那一页同时多一行(自检拿假的钉住这条)。单一事实源只有一处,就是库源码。
//
// H81(2026-10-11)把 §7 / §8 也收了进来 —— 那两节以前同样是手抄的,实测 §7 写 16(现场 19),
// §8 那 23 个名字更是 2026-09-19 改名**之前**的(`双精度常量_零` → 现在的 `双精度常量零`)。
//
// 生成的是整节(从节标题到下一个 `## ` 之前),原节里手写的东西会被**整段换掉**。
// ⚠ 写盘不在这儿 —— 一个文件只有一个写口,见 `引擎\关键字与方法清单C.cs`。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary> `关键字与方法清单.md` 的 §6~§9:从库源码现场生成(写盘见 `关键字与方法清单C`) </summary>
    public static class 库函数清单C
    {
        /// <summary> 库函数那一节的标题前缀(按前缀找节;标题里的"共 N 个"是生成的,不在前缀里) </summary>
        public const string 库函数节 = "## 6、库函数";
        /// <summary> 库里声明的结构那一节的标题前缀 </summary>
        public const string 结构节 = "## 7、库里声明的结构";
        /// <summary> 库里的命名常量那一节的标题前缀 </summary>
        public const string 常量节 = "## 8、库里的命名常量";
        /// <summary> 库一览那一节的标题(到文件末尾或下一个 `## ` 为止) </summary>
        public const string 库一览节 = "## 9、库一览";

        /// <summary> §6:一个库一个 `###` 小节,小节里一张"方法 / 签名 / 作用"表 </summary>
        public static string 建库函数节(IReadOnlyList<(string 文件名, string 内容)> 库文件)
        {
            int 总数 = 库文件.Sum(x => 帮助站点C.取公开方法(x.内容).Count);
            StringBuilder 缓 = new();
            缓.Append(库函数节).Append("(`Sample\\库\\*.hb`,共 ").Append(总数).Append(" 个)\n\n");
            缓.Append("> 来源 = 每个库文件里的 `方法 …` 声明(`私有 方法` 不算),作用取紧上面那行注释;\n");
            缓.Append("> 一个库一张表,按文件名排。**为什么有这一节**:帮助站点《类库》一节一个库一页,\n");
            缓.Append("> 页面上那张**公开方法表**与这里同源;要查" + "\u201c" + "某库有些什么方法" + "\u201d" +
                       "看这节或那一页都行。\n\n");
            foreach ((string 文件名, string 内容) in 库文件)
            {
                var 方法们 = 帮助站点C.取公开方法(内容);
                缓.Append("### ").Append(文件名).Append('(').Append(方法们.Count).Append(" 个)\n\n");
                if (方法们.Count == 0)
                {
                    缓.Append("(这个库没有公开方法)\n\n");
                    continue;
                }
                缓.Append("| 方法 | 签名 | 作用 |\n|---|---|---|\n");
                foreach ((string 名, string 签名, string 作用) in 方法们)
                    缓.Append("| `").Append(名).Append("` | `").Append(表格格(签名)).Append("` | ")
                       .Append(表格格(作用.Length > 0 ? 作用 : "—")).Append(" |\n");
                缓.Append('\n');
            }
            return 缓.ToString();
        }

        /// <summary>
        /// §7:一行一个 `结构` 声明 —— 结构名 / 文件 / 字段(`、` 接起来)/ 作用。
        /// **作用两处找**:① 紧贴在 `结构` 上面那一段 `//`(收尾若是 `====` 分隔线就算"没写注释",
        /// 那行属于它上面那个小节标题);② 都没有就退回该库**头注释第一行** —— 至少让人知道是哪个库的结构。
        /// </summary>
        public static string 建结构节(IReadOnlyList<(string 文件名, string 内容)> 库文件)
        {
            List<(string 结构, string 文件, string 字段, string 作用)> 行 = [];
            foreach ((string 文件名, string 内容) in 库文件)
            {
                string[] 文 = 规范行(内容);
                string 兜底 = 头一句(内容);
                for (int i = 0; i < 文.Length; i++)
                {
                    if (!是声明行(文[i], "结构", out string 余)) continue;
                    string 名 = 余.Split(' ', '\t')[0].Trim();
                    if (名.Length == 0) continue;
                    string 注 = 紧上面注释(文, i);
                    行.Add((名, 文件名, 取字段(文, i), 注.Length > 0 ? 注 : 兜底));
                }
            }
            StringBuilder 缓 = new();
            缓.Append(结构节).Append("(类型名,共 ").Append(行.Count).Append(" 个)\n\n");
            缓.Append("> 来源 = 每个库文件里的 `结构 名 { … }` 声明,字段按声明顺序 `、` 接起来;\n");
            缓.Append("> 「作用」取紧贴声明上面那段注释,没写注释的退回该库头注释第一行。\n\n");
            缓.Append("| 结构 | 文件 | 字段 | 作用 |\n|---|---|---|---|\n");
            foreach ((string 结构, string 文件, string 字段, string 作用) in 行)
            {
                缓.Append("| `").Append(结构).Append("` | ").Append(文件).Append(" | ")
                   .Append(表格格(字段.Length > 0 ? 字段 : "—")).Append(" | ")
                   .Append(表格格(作用.Length > 0 ? 作用 : "—")).Append(" |\n");
            }
            return 缓.ToString();
        }

        /// <summary>
        /// §8:库里的**命名常量** —— 零参数、名字里带 `常量` 的 `方法`(库把"取常量"做成函数:
        /// 语言层的容器字段只让放基础类型,结构放不进去,所以 `float64.hb` 才有一族 `双精度常量Xxx()`)。
        /// 「作用」不退回头注释 —— 这一节九成来自同一个库,把同一句头注释重复 N 遍只是噪音。
        /// </summary>
        public static string 建常量节(IReadOnlyList<(string 文件名, string 内容)> 库文件)
        {
            List<(string 常量, string 文件, string 作用)> 行 = [];
            foreach ((string 文件名, string 内容) in 库文件)
            {
                string[] 文 = 规范行(内容);
                for (int i = 0; i < 文.Length; i++)
                {
                    if (!是声明行(文[i], "方法", out string 余)) continue;
                    int 左 = 余.IndexOf('(');
                    int 右 = 余.IndexOf(')');
                    if (左 <= 0 || 右 < 左) continue;             // 不是"名(…)"这种形态,跳过
                    string 名 = 余[..左].Trim();
                    if (余[(左 + 1)..右].Trim().Length != 0) continue;   // 有参数 ⇒ 不是取常量
                    if (!名.Contains("常量", StringComparison.Ordinal)) continue;
                    行.Add((名, 文件名, 紧上面注释(文, i)));
                }
            }
            StringBuilder 缓 = new();
            缓.Append(常量节).Append("(返回常量的函数,共 ").Append(行.Count)
               .Append(" 个 —— 零运行期开销,直接当值用)\n\n");
            缓.Append("> 来源 = 库里**零参数**、名字里带 `常量` 的 `方法`(库把" + "\u201c" + "取常量" + "\u201d" +
                       "做成一组函数:\n");
            缓.Append("> 语言层的容器字段只让放基础类型,结构放不进去)。「作用」取紧贴声明上面那段注释。\n\n");
            缓.Append("| 常量 | 文件 | 作用 |\n|---|---|---|\n");
            foreach ((string 常量, string 文件, string 作用) in 行)
            {
                缓.Append("| `").Append(常量).Append("()` | ").Append(文件).Append(" | ")
                   .Append(表格格(作用.Length > 0 ? 作用 : "—")).Append(" |\n");
            }
            return 缓.ToString();
        }

        /// <summary> §9:一行一个库 —— 库文件 / 头注释第一行(是什么)/ 公开方法数 / 结构数 </summary>
        public static string 建库一览节(IReadOnlyList<(string 文件名, string 内容)> 库文件)
        {
            StringBuilder 缓 = new();
            缓.Append(库一览节).Append("\n\n");
            缓.Append("| 库文件 | 是什么 | 函数 | 结构 |\n|---|---|---|---|\n");
            foreach ((string 文件名, string 内容) in 库文件)
            {
                string 一句 = 头一句(内容);
                if (一句.Length == 0) 一句 = 文件名;
                if (一句.Length > 96) 一句 = 一句[..96] + "…";
                int 法 = 帮助站点C.取公开方法(内容).Count;
                int 构 = 数结构(内容);
                缓.Append("| `").Append(文件名).Append("` | ").Append(表格格(一句)).Append(" | ")
                   .Append(法).Append(" | ").Append(构).Append(" |\n");
            }
            return 缓.ToString();
        }

        // ---------------- 工具 ----------------

        /// <summary> markdown 表格里那一格:竖线转义、换行压成空格(免得把表切坏) </summary>
        public static string 表格格(string 文) =>
            文.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "").Trim();

        /// <summary> 库头注释的第一行(拿它当"这个库是什么");没有头注释给空串 </summary>
        private static string 头一句(string 内容)
        {
            string 头 = 帮助站点C.取头注释(内容);
            return 头.Length == 0 ? "" : 头.Split('\n')[0].Trim();
        }

        /// <summary> 库里有几个 `结构` 声明(`结构节` 与 `库一览节` 必须同一个口径) </summary>
        private static int 数结构(string 内容) =>
            规范行(内容).Count(x => 是声明行(x, "结构", out _));

        /// <summary> 统一换行、切成行(所有扫描都走它,免得一处 `\r\n` 一处 `\n`) </summary>
        private static string[] 规范行(string 内容) =>
            内容.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        /// <summary> 这一行是不是 `关键字` 开头的声明;是就把关键字后面那截(去掉首尾空白)给出去 </summary>
        private static bool 是声明行(string 行, string 关键字, out string 余)
        {
            余 = "";
            string 净 = 行.TrimStart();
            if (!净.StartsWith(关键字, StringComparison.Ordinal)) return false;
            string 尾 = 净[关键字.Length..];
            if (尾.Length == 0 || !char.IsWhiteSpace(尾[0])) return false;   // `结构体` 这种不能算
            余 = 尾.Trim();
            return true;
        }

        /// <summary> `结构` 那一行的字段:`{` 到 `}` 之间的行,去注释 / 去 `;` / 空白压成一个空格 </summary>
        private static string 取字段(string[] 文, int 声明行)
        {
            List<string> 段 = [];
            int i = 声明行 + 1;
            while (i < 文.Length && !文[i].Contains('{')) i++;      // 找 `{`(允许它换行写)
            for (i++; i < 文.Length; i++)
            {
                string 行 = 文[i];
                if (行.TrimStart().StartsWith("}", StringComparison.Ordinal)) break;
                int 注 = 行.IndexOf("//", StringComparison.Ordinal);
                if (注 >= 0) 行 = 行[..注];
                行 = 行.Trim().TrimEnd(';').Trim();
                if (行.Length == 0) continue;
                段.Add(string.Join(" ", 行.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
            }
            return string.Join("、", 段);
        }

        /// <summary>
        /// 紧贴在 `声明行` 上面那一段连续 `//` 拼成的一句(分隔线丢掉)。
        /// **收尾那行是纯分隔线(`====`)时当"没写注释"** —— 那行属于它上面那个小节标题,
        /// 把标题当结构的作用会误导(实测:float32.hb 的 `宽整数` 就该退回库头注释)。
        /// </summary>
        private static string 紧上面注释(string[] 文, int 声明行)
        {
            List<string> 段 = [];
            int i = 声明行 - 1;
            while (i >= 0 && 文[i].TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                段.Insert(0, 文[i].TrimStart()[2..].Trim());
                i--;
            }
            if (段.Count == 0) return "";
            if (段[^1].Length == 0 || 段[^1].All(是分隔线字符)) return "";
            var 真 = 段.Where(x => x.Length > 0 && !x.All(是分隔线字符));
            return string.Join(" ", 真).Trim();

            static bool 是分隔线字符(char 字) => 字 is '=' or '-' or '_' or '*' or ' ';
        }
    }
}
