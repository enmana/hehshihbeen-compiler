using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 任务清单.md 的**结构管理**(第113批:用户点"把任务清单切成任务种类、支脉、递进关系")。
    ///
    /// 三条轴:
    ///   ① **支脉**(线)= A 内核与编译器 / B 语言面与字符串库 / C IDE / D 宿主与运行期 / E 工具与质量 / F 收尾与文档
    ///      —— 与 `未做任务清单.md` 的编号字母**同一套**(两份文件同一个命名空间,好对账);
    ///   ② **种类**(型)= 修缺陷 / 加指令 / 加能力 / 语言面与库 / 重构搬运 / 优化 / 实测与判据 / 文档与收尾;
    ///   ③ **递进**(序)= `上游 → 下游`(记号:节编号 `第110节` / 支脉号 `A9` / 批号 `批108`),外加状态一档
    ///      (完成 / 半成品 / 搁置 / 判定不做 / 计划)。
    ///
    /// 落地:每条**含"第 N 批"的节**,标题下第一处非空内容必须是结构头,一行写完:
    ///   `> 支脉 A · 种类 加指令 · 状态 完成 · 递进 A2 → —`
    /// 顶部四张表(支脉 / 种类 / 批序 / 显式递进链)夹在一对标记之间,由 `hbcc --清单 --索引` 生成 —— 只改标记之间那一段。
    ///
    /// 为什么这么管:这份文件上万行、一百多条记录(不写死数 —— 每批都在长),以前找一条只能全文搜;现在 `--清单 --查 A9` 直接给出条目与行区间,
    /// `--清单 --校验` 把"结构头缺了 / 支脉写错 / 编号重了 / 递进链指向不存在的条目 / 索引与正文不一致"一次报全,
    /// 自检里再钉一条 —— **文档从此也是可断言的东西**。
    /// </summary>
    public static class 任务清单C
    {
        public const string 开始标记 = "<!-- 清单结构:下面是自动生成的,别手改(在仓库根目录跑 `hbcc --清单 --索引`) -->";
        public const string 结束标记 = "<!-- 清单结构:自动生成结束 -->";
        public const string 文件名 = "任务清单.md";

        /// <summary>
        /// R5-5(第237批):主文件瘦身之后,**较早批次的正文**整段搬到了这个文件里。
        /// 主文件里那一条只剩下"标题 + 结构头 + 一行指路"(`> 正文在 \`任务清单.归档.md\`(搜 \`第195节、\`)。`),
        /// 两边的节名一一对应 —— 由自检那条 `清单结构 归档指路条条对得上` 双向钉住。
        /// </summary>
        public const string 归档文件名 = "任务清单.归档.md";

        /// <summary> 支脉字母 → 名字(与 `未做任务清单.md` 的区域字母同一套) </summary>
        public static readonly (string 字母, string 名)[] 支脉表 = {
            ("A", "内核与编译器"), ("B", "语言面与字符串库"), ("C", "IDE"), ("D", "宿主与运行期"),
            ("E", "工具与质量"), ("F", "收尾与文档") };

        public static readonly string[] 种类表 =
            { "修缺陷", "加指令", "加能力", "语言面与库", "重构搬运", "优化", "实测与判据", "文档与收尾" };

        public static readonly string[] 状态表 = { "完成", "半成品", "搁置", "判定不做", "计划" };

        /// <summary> 一条记录(一个含"第 N 批"的 `##` 节) </summary>
        public sealed class 条
        {
            public int 标题行;                 // 1 基
            public string 编号 = "";           // 第113节
            public string 标题 = "";           // 完整的标题(去掉 `## `)
            public int 批号;                   // 第 N 批(N 解不出来就是 0)
            public string 支脉 = "", 种类 = "", 状态 = "", 递进 = "";
            public int 正文起, 正文止;         // 这一节的正文区间(1 基,不含标题行)
            public string 结构头 = "";         // 结构头那一行的原文
        }

        // 每条记录的**编号 / 批号 / 递进记号一律阿拉伯数字**(用户 2026-10-13 拍板;格式 `第398节` / `第398批`)——
        // 全仓文档编号同此一刀,规矩写在 `命名规则.md` §15。
        // ⚠ 为什么这里特意留一段旧账:这三样以前写的是中文数字(`三点三百三十` / `第三百三十批` / `§九`),
        //   于是正则得**逐字枚举**一整套字符集 —— R3-6-5 批 2 撞到的就是"集里少一个 `零`":`三点三百三十`
        //   一个都认不出来 ⇒ 那个节没有编号 ⇒ 后面指着它的 `递进` 全报"指向的节不存在"。
        //   换成 `\d+` 之后,"少写一个字就整段失灵"这一类坑没有了。
        private static readonly Regex 批号式 = new(@"第(\d+)(?:[~～](\d+))?批", RegexOptions.Compiled);
        private static readonly Regex 编号式 = new(@"^第(\d+)节、", RegexOptions.Compiled);
        private static readonly Regex 记号式 = new(@"^(第\d+节|[A-F]\d+|批\d+)$", RegexOptions.Compiled);
        private static readonly Regex 支脉号式 = new(@"([A-F]\d+)", RegexOptions.Compiled);

        /// <summary>
        /// **最近这一段里"确实没有号"的那几个号** —— 空档检查拿它当白名单,免得把两种**不是漏号**的情况报成撞车:
        ///   · `390` —— `第390节、HB 与 AI 结合方案` 那一节自己写着"(**规格批,不是施工批**)",所以它故意不占号;
        ///     那一节在文件里是真的,只是没号 —— 光看号段会以为漏了一条施工批。
        ///   · `402` —— 实际发号时被跳过、且全文件找不到任何记录解释它(挨着 401 与 403)。**留在这里当疤**:
        ///     它多半就是 2026-10-09 那次撞车让出来的号,写清楚比抹平强(抹平了下次还会有人问"402 呢")。
        /// 往这里加号**必须连缘由一起写**(照这两条的写法);反过来,号后来被人占了而表里没清掉,`校验` 会报
        /// "预留表里有它,但没有任何节占着它" —— 这张表不会悄悄过期。
        /// ⚠ 这一档**只对"最近这一段"生效**(见 `空档窗口`):中段那些历史空档(`2~21` `66` `68` `94` `130` `217` …
        /// 那是另一套计划编号留下的)不在这里登记 —— 它们离当前发号位置太远,报出来只是刷屏。
        /// </summary>
        private static readonly Dictionary<int, string> 已知缺号 = new() {
            [390] = "规格批(不是施工批):`第390节、HB 与 AI 结合方案` 那一条故意不发号",
            [402] = "撞车让号留下的疤:全文件没有任何记录解释它(挨着 401 与 403),按'保留、不复用'处理",
        };

        /// <summary>
        /// 空档检查**只量最近这一段**。为什么不到处量:这份清单的**批号账本在中段断过一次** ——
        /// 早期那些"内核加固计划批"用的是另一套计划编号,主文件里根本没有 `第 2 ~ 21 批` 这些记录;
        /// 后来又有若干号是被跳过 / 合并掉的(`66` `68` `94` `130` `217` `237` `253` `281` `295` `296` `318` …)。
        /// 那些陈年老账翻出来只会刷屏,而**能防住撞车的是最近这一段**:新号是从"当前最大号 +1"发的,
        /// 两个会话同时发号,撞出来的空档必然落在**尾部**。所以只量 `最大号 - 窗口 + 1 .. 最大号 + 1`。
        /// </summary>
        private const int 空档窗口 = 20;

        /// <summary>
        /// 从标题里认**声明位**上的批号(不是全篇搜"第N批"):
        ///   · `(第393批)` / `(第403批;通用化第五步)` / `(第400批、**浮点…**)` —— 括号里那一句的开头;
        ///   · `第324节、第324批:…` —— 编号之后那一段的开头。
        /// 为什么不能全篇搜:旧写法里 `(第78批,47 → 48 条)` 是**三条 P13/P14/P12 记录共用一个号**,
        /// `(第96~97批 = 内核加固计划批 13 前半)` 是**一个号横跨前后两半** —— 它们都不违背"一条批号一个号",
        /// 却会被全篇搜认成"同一个号出现了 N 次"(实测假警报 5 组)。声明位只认一次,这两个形状自然落回一条。
        /// </summary>
        private static readonly Regex 声明批号式 = new(
            @"(?:\(|、)第(\d+)(?:[~～](\d+))?批(?![,，=＝\s])", RegexOptions.Compiled);

        /// <summary> 声明位上的批号:区间取上界(`第96~97批` 算 97);认不出来给 0 </summary>
        public static int 声明批号(string 标题)
        {
            var m = 声明批号式.Match(标题);
            if (!m.Success) return 0;
            if (int.TryParse(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value, out int 上) && 上 > 0)
                return 上;
            return int.TryParse(m.Groups[1].Value, out int 左) ? 左 : 0;   // 上界认不出来就退回左边那一头
        }

        /// <summary> 从标题里取批号:取标题里**最后**一个"第 X 批";`第96~97批` 取 97 </summary>
        public static int 取批号(string 标题)
        {
            int 值 = 0;
            foreach (Match m in 批号式.Matches(标题))
                if (int.TryParse(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value, out int n) && n > 0)
                    值 = n;
            return 值;
        }

        /// <summary> 解析:返回所有含"第 N 批"的节;问题都收进 `问题`(不抛异常,一次报全) </summary>
        public static List<条> 解析(string 文本, List<string>? 问题 = null)
        {
            问题 ??= new List<string>();
            string[] 行 = 文本.Replace("\r\n", "\n").Split('\n');
            var 节头 = new List<int>();
            for (int i = 0; i < 行.Length; i++) if (行[i].StartsWith("## ", StringComparison.Ordinal)) 节头.Add(i);

            var 出 = new List<条>();
            for (int k = 0; k < 节头.Count; k++)
            {
                int i = 节头[k];
                string 标题 = 行[i][3..].Trim();
                if (!批号式.IsMatch(标题)) continue;      // 计划 / 速查 / 索引那些节不要求结构头
                var 条 = new 条
                {
                    标题行 = i + 1,
                    标题 = 标题,
                    正文起 = i + 2,
                    正文止 = k + 1 < 节头.Count ? 节头[k + 1] : 行.Length,   // 1 基;下一个标题行的前一行
                };
                var m = 编号式.Match(标题);
                条.编号 = m.Success ? "第" + m.Groups[1].Value + "节" : "";
                条.批号 = 取批号(标题);

                int j = i + 1;                                  // 标题下第一处非空内容必须是结构头
                while (j < 行.Length && 行[j].Trim().Length == 0) j++;
                if (j >= 行.Length || !行[j].StartsWith("> 支脉 ", StringComparison.Ordinal))
                {
                    问题.Add($"第 {i + 1} 行 `{标题}`:标题下没有结构头(应为 `> 支脉 A · 种类 … · 状态 … · 递进 … → …`)");
                    出.Add(条);
                    continue;
                }
                条.结构头 = 行[j].Trim();
                foreach (string 段 in 条.结构头[2..].Split('·'))
                {
                    string 文 = 段.Trim();
                    if (文.StartsWith("支脉 ")) 条.支脉 = 文[3..].Trim();
                    else if (文.StartsWith("种类 ")) 条.种类 = 文[3..].Trim();
                    else if (文.StartsWith("状态 ")) 条.状态 = 文[3..].Trim();
                    else if (文.StartsWith("递进 ")) 条.递进 = 文[3..].Trim();
                    else 问题.Add($"第 {i + 1} 行 `{标题}`:结构头里有认不出的字段 `{文}`");
                }
                if (!支脉表.Any(x => x.字母 == 条.支脉)) 问题.Add($"第 {i + 1} 行 `{标题}`:支脉 `{条.支脉}` 不在 A~F 里");
                if (!种类表.Contains(条.种类)) 问题.Add($"第 {i + 1} 行 `{标题}`:种类 `{条.种类}` 不在枚举里");
                if (!状态表.Contains(条.状态)) 问题.Add($"第 {i + 1} 行 `{标题}`:状态 `{条.状态}` 不在枚举里");
                if (条.递进.Length == 0) 问题.Add($"第 {i + 1} 行 `{标题}`:结构头缺 `递进` 字段");
                出.Add(条);
            }

            // 编号不许重复(同一节改过名、复制粘贴最容易撞)
            foreach (var g in 出.Where(x => x.编号.Length > 0).GroupBy(x => x.编号).Where(g => g.Count() > 1))
                问题.Add($"编号 `{g.Key}` 出现了 {g.Count()} 次(第 {string.Join(" / ", g.Select(x => x.标题行))} 行)");

            // 批号也要查:查重 + 尾部空档。**这是 2026-10-09 补的一条灯** ——
            // 那天**两个会话在同一棵树上各写各的文档**,批号是两边各自"顺手 +1"发出来的,谁也不知道对面发了多少
            // ⇒ 连撞三次,号从第三百九十五一路让到第四百零三。撞车的表现**不是报错**,而是两条记录各自声明同一个号,
            // 静静躺在文件里;空档(402 就是)是让号留下的疤。以前 `--清单` 只查 `编号`(那时候是 `三点X`),批号一个字节都不查。
            // 两条口径:① 只在**声明位**上认号(见 `声明批号式`)—— 旧写法"一个号横跨几节"不算撞;
            //           ② 空档只量最后 `空档窗口` 个号 —— 中段的历史空档是另一本账,翻出来只会刷屏。
            var 声明账 = new List<(int 号, int 行, string 标题)>();
            foreach (var x in 出)
                if (声明批号(x.标题) is int 号 && 号 > 0) 声明账.Add((号, x.标题行, x.标题));
            foreach (var g in 声明账.GroupBy(x => x.号).OrderBy(g => g.Key).Select(g => (号: g.Key, 们: g.ToList())).Where(g => g.们.Count > 1))
                问题.Add($"批号 `第 {g.号} 批` 被 {g.们.Count} 条记录各自声明了一次(第 {string.Join(" / ", g.们.Select(x => x.行))} 行)"
                       + " —— 一个批号只能顶一条施工批;后面进来的那条必须换号(撞车就是这么来的:两个会话各自从'当前最大号 +1'发号)");
            int 最大号 = 声明账.Count > 0 ? 声明账.Max(x => x.号) : 0;
            for (int n = Math.Max(1, 最大号 - 空档窗口 + 1); n <= 最大号 + 1; n++)
            {
                bool 有记录 = 声明账.Any(x => x.号 == n);
                bool 已登记 = 已知缺号.ContainsKey(n);
                if (已登记) continue;                                   // 缺号表里的号**本来就该没有节占着** —— 这就是它登记的意思
                if (有记录) continue;                                   // 绝大多数:号有记录顶着
                if (声明账.Any(x => x.号 > n))                          // 只在"下一个更大的号确实存在"时才算空档(尾部那个 +1 不算)
                    问题.Add($"批号 `第 {n} 批` 是个空档:它后面还有 `第 {声明账.Where(x => x.号 > n).Min(x => x.号)} 批` 顶着,"
                           + "可全文件没有任何节声明它 —— 多半是撞车让号留下的疤;要么补一条占它的记录,要么写进 `已知缺号` 表并写明缘由");
            }

            // 递进记号必须指得到东西
            var 有编号 = 出.Select(x => x.编号).ToHashSet();
            var 有支脉号 = 出.SelectMany(x => 支脉号式.Matches(x.标题).Select(m => m.Groups[1].Value)).ToHashSet();
            var 有批号 = 出.Where(x => x.批号 > 0).Select(x => "批" + x.批号).ToHashSet();
            foreach (var x in 出)
            {
                foreach (string 记号 in x.递进.Split('→')
                                 .SelectMany(s => s.Split([',', '、', ' '], StringSplitOptions.RemoveEmptyEntries)))
                {
                    string t = 记号.Trim();
                    if (t.Length == 0 || t is "—" or "-" or "无") continue;
                    if (!记号式.IsMatch(t))
                    {
                        问题.Add($"第 {x.标题行} 行 `{x.标题}`:递进记号 `{t}` 写法不对(要 `第110节` / `A9` / `批108` / `—`)");
                        continue;
                    }
                    if (t.StartsWith("第") && !有编号.Contains(t)) 问题.Add($"第 {x.标题行} 行 `{x.标题}`:递进指向的节 `{t}` 不存在");
                    else if (t.StartsWith("批") && !有批号.Contains(t)) 问题.Add($"第 {x.标题行} 行 `{x.标题}`:递进指向的批 `{t}` 不存在");
                    else if (记号式.IsMatch(t) && t[0] is >= 'A' and <= 'F' && !有支脉号.Contains(t)) 问题.Add($"第 {x.标题行} 行 `{x.标题}`:递进指向的支脉号 `{t}` 在任何标题里都没出现");
                }
            }
            return 出;
        }

        /// <summary> 校验:返回问题清单(空 = 全过);顺带查索引段与正文一致 </summary>
        public static List<string> 校验(string 文本)
        {
            var 问题 = new List<string>();
            解析(文本, 问题);
            try
            {
                string 现在 = 规整(取索引段(文本));
                string 该是 = 规整(生成索引(文本));
                if (现在 != 该是) 问题.Add("清单结构索引与正文不一致(跑 `hbcc --清单 --索引` 重新生成)");
            }
            catch (Exception ex) { 问题.Add(ex.Message); }
            return 问题;
        }

        /// <summary> 比文本前先去杂:统一换行,并把两头的空行与 `\r` 去掉(尾部 `\r` 是踩过的坑) </summary>
        private static string 规整(string 文) => 文.Replace("\r\n", "\n").Trim('\r', '\n');

        private static string 取索引段(string 文本)
        {
            int 起 = 文本.IndexOf(开始标记, StringComparison.Ordinal);
            if (起 < 0) throw new InvalidOperationException($"任务清单.md 里找不到清单结构标记。请在要自动生成的那一节前后各加一行:\n{开始标记}\n{结束标记}");
            int 止 = 文本.IndexOf(结束标记, 起, StringComparison.Ordinal);
            if (止 < 0) throw new InvalidOperationException($"任务清单.md 里有开始标记但没有结束标记({结束标记})");
            return 文本[(起 + 开始标记.Length)..止];
        }

        /// <summary> 生成四张表(支脉 / 种类 / 批序 / 显式递进链) </summary>
        public static string 生成索引(string 文本)
        {
            var 条们 = 解析(文本);
            var sb = new StringBuilder();
            sb.AppendLine("> 这一节由 `hbcc --清单 --索引` 生成(只改标记之间那一段),**别手改**;结构头写在每条记录的标题下。");
            sb.AppendLine($"> 口径:共 **{条们.Count}** 条记录;支脉 {支脉表.Length} 条(与 `未做任务清单.md` 同一套字母)、种类 {种类表.Length} 种、状态 {状态表.Length} 档。");
            sb.AppendLine();
            sb.AppendLine("| 支脉 | 条数 | 记录(按文件里的顺序) |");
            sb.AppendLine("|---|---|---|");
            foreach (var (字母, 名) in 支脉表)
            {
                var 本 = 条们.Where(x => x.支脉 == 字母).ToList();
                sb.AppendLine($"| **{字母}** {名} | {本.Count} | {拼(本)} |");
            }
            sb.AppendLine();
            sb.AppendLine("| 种类 | 条数 | 记录 |");
            sb.AppendLine("|---|---|---|");
            foreach (string 种 in 种类表)
                sb.AppendLine($"| {种} | {条们.Count(x => x.种类 == 种)} | {拼(条们.Where(x => x.种类 == 种).ToList())} |");
            sb.AppendLine();
            sb.AppendLine("**批序**(按批号排的脊梁;批号解不出来的(区间/多批合写)在后半张表):");
            sb.AppendLine();
            sb.AppendLine("| 批号 | 节 | 支脉 | 种类 | 状态 | 行 |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var x in 条们.Where(x => x.批号 > 0).OrderBy(x => x.批号))
                sb.AppendLine($"| 第 {x.批号} 批 | {x.编号} | {x.支脉} | {x.种类} | {x.状态} | {x.标题行} |");
            foreach (var x in 条们.Where(x => x.批号 == 0))
                sb.AppendLine($"| (区间) | {x.编号} | {x.支脉} | {x.种类} | {x.状态} | {x.标题行} |");
            sb.AppendLine();
            sb.AppendLine("**显式递进链**(结构头里写了上下游的;节编号能在上面批序表里对到批号):");
            sb.AppendLine();
            sb.AppendLine("| 上游 | 本条 | 下游 | 状态 |");
            sb.AppendLine("|---|---|---|---|");
            var 链 = 条们.Where(x => x.递进.Split('→').Any(s => s.Trim() is not ("—" or "-" or ""))).ToList();
            if (链.Count == 0) sb.AppendLine("| — | — | — | — |");
            foreach (var x in 链)
            {
                string[] 两 = x.递进.Split('→', StringSplitOptions.None).Select(s => s.Trim()).ToArray();
                string 本条 = x.编号.Length > 0 ? x.编号 : x.标题;
                sb.AppendLine($"| {两[0]} | {本条}({x.标题}) | {(两.Length > 1 && 两[1].Length > 0 ? 两[1] : "—")} | {x.状态} |");
            }
            return sb.ToString().TrimEnd('\r', '\n');   // 注意:`TrimEnd('\n')` 会把最后一行的 `\r` 留下 —— 一个隐形字符就够让"索引与正文一致"永远不成立

            static string 拼(List<条> 本) => 本.Count == 0 ? "—"
                : string.Join(" · ", 本.Select(x => x.编号.Length > 0 ? x.编号 : x.标题));
        }

        /// <summary>
        /// 把索引写进文件(只改标记之间那一段);返回替换段数(正常 1)。
        /// **迭代到不动点**:表里带"行"号,而表本身插在文件头部 ⇒ 第一次写完,后面每条记录的行号整体下移,
        /// 表就得按新行号再算一遍(实测要两轮;这里最多 5 轮,收不住就报错,不静默留个不一致的文件)。
        /// </summary>
        public static int 更新索引(string 路径)
        {
            int 段数 = 0;
            string 最后差异 = "";
            for (int 轮 = 0; 轮 < 5; 轮++)
            {
                string 文本 = File.ReadAllText(路径);
                段数 = 写段(路径, 文本, 生成索引(文本));
                string 新文 = File.ReadAllText(路径);
                string 现在 = 规整(取索引段(新文));
                string 该是 = 规整(生成索引(新文));   // 注意:必须拿**写完之后**的文件再算一遍,不能跟写进去的那份比(那是同义反复)
                if (现在 == 该是) return 段数;
                var a = 现在.Split('\n'); var b = 该是.Split('\n');
                for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
                    if (a[i] != b[i])
                    {
                        int k = 0;
                        while (k < Math.Min(a[i].Length, b[i].Length) && a[i][k] == b[i][k]) k++;
                        最后差异 = $"第 {i + 1} 行不一致(第 {k + 1} 个字符起):\n  文件里:{剪(a[i])}\n  算出来:{剪(b[i])}";
                        break;
                    }
                if (最后差异.Length == 0) 最后差异 = $"行数不一致:文件里 {a.Length} 行,算出来 {b.Length} 行";
            }
            throw new InvalidOperationException("清单结构索引迭代 5 轮还没稳定。\n" + 最后差异);

            static string 剪(string 行) => 行.Length <= 90 ? 行 : 行[..90] + "…";
        }

        private static int 写段(string 路径, string 文本, string 表)
        {
            int 计数 = 0, 位置 = 0;
            var 结果 = new StringBuilder();
            while (true)
            {
                int 起 = 文本.IndexOf(开始标记, 位置, StringComparison.Ordinal);
                if (起 < 0) break;
                int 止 = 文本.IndexOf(结束标记, 起, StringComparison.Ordinal);
                if (止 < 0) throw new InvalidOperationException($"任务清单.md 里有开始标记但没有结束标记({结束标记})");
                结果.Append(文本, 位置, 起 + 开始标记.Length - 位置);
                结果.Append('\n').Append(表).Append('\n');
                位置 = 止;
                计数++;
            }
            if (计数 == 0)
                throw new InvalidOperationException(
                    $"任务清单.md 里找不到清单结构标记。请在要自动生成的那一节前后各加一行:\n{开始标记}\n{结束标记}");
            结果.Append(文本, 位置, 文本.Length - 位置);
            // 落盘走全仓唯一口径(`文本落盘C`):照原文件有没有 BOM 写,**行尾由调用方文本自己决定** ——
            // 以前这里是 `File.WriteAllText(路径, 结果.ToString())`,而 .NET 的 `Encoding.UTF8` 是**带 BOM** 的,
            // 于是跑一次 `--清单 --索引` 就给 `任务清单.md` 加一个 BOM,`git diff` 上整份文件都像改了(C26 那个坑)。
            文本落盘C.写回(路径, 结果.ToString());
            return 计数;
        }

        /// <summary>
        /// 查:词可以是**节编号**(`第428节`)、**支脉字母**(`A`)、**支脉号**(`A9`)、**批号**(裸数字 `428`
        /// 或带前缀的 `批428` / `批 428` —— 两种写法给同一批)、种类、状态,或标题里的关键字。
        ///
        /// ⚠ **批号那两种写法是 H104 补的**:第 348 批起这份注释一直写着"批号(108 / 批108)",而实现只认裸数字,
        /// `--清单 --查 批426` 实测报"没有匹配"(2026-10-13 量到)⇒ 本批把实现补齐:剥掉开头的 `批`,
        /// **只在那后面全是数字时剥**(免得"批"字开头的关键字被误剥)。别的几种写法一个字没动。
        /// </summary>
        public static List<条> 查(string 文本, string 词)
        {
            string t = 词.Trim();
            if (t.Length > 1 && t[0] == '批')
            {
                string 余 = t[1..].Trim();
                if (余.Length > 0 && 余.All(char.IsAsciiDigit)) t = 余;
            }
            return 解析(文本).Where(x =>
                批号式.IsMatch(t) || Regex.IsMatch(t, @"^\d+$") ? x.批号 == 取批号(t) || x.批号.ToString() == t
                : Regex.IsMatch(t, "^[A-F]$") ? x.支脉 == t
                : Regex.IsMatch(t, @"^[A-F]\d+$") ? x.标题.Contains(t, StringComparison.Ordinal)
                : 种类表.Contains(t) ? x.种类 == t
                : 状态表.Contains(t) ? x.状态 == t
                : x.编号 == t || x.标题.Contains(t, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }
}
