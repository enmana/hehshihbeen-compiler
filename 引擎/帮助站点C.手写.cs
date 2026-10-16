using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// C25-5:帮助站点里**手写那一半**(《介绍》《上手》《语言细节》)。
    /// <para>
    /// 分工:C25-2 那六节(类型 / 指令 / 关键字 / 内建函数 / 方法 / 类库)**从表现场生成**,表里加一条就多一条;
    /// 这里三节是**人写的**,编译器加了新东西机器盯不住 —— 所以口径定成三条:
    /// </para>
    /// <list type="number">
    /// <item>《上手》与《语言细节》的正文**只有一份**,就在 `帮助文本C` 里(命令行 `hbcc --help-text` 与自检在用)。
    ///   这里在**生成站点那一刻**把它按节标题拆成子节点(拆法见 `拆上手` / `拆方括号节`),改一处两边都变 ——
    ///   别把正文抄第二份进来(那就会互相打脸);</item>
    /// <item>《介绍》是站点独有的两小节(语言特性速览、文档分工),正文写在这个文件里;</item>
    /// <item>手写这三节的**人工核对**写进仓库根 `发布检查单.md`(核对依据 = `帮助核对表.md`),
    ///   改完记得跑 `hbcc --生成帮助`(自检有一条"盘上那份没过期"盯着)。</item>
    /// </list>
    /// </summary>
    public static partial class 帮助站点C
    {
        /// <summary> 手写节之一:HB 介绍(语言特性速览 + 文档分工) </summary>
        public const string 介绍节名 = "介绍";
        /// <summary> 手写节之二:《快速上手》那几十节,按原标题拆成子节点 </summary>
        public const string 上手节名 = "上手";
        /// <summary> 手写节之三:《中间层速查》里那些手写说明(与生成的速查不重复的部分) </summary>
        public const string 语言细节节名 = "语言细节";

        /// <summary> 《上手》的节标题:`15,编成独立 exe`(行首阿拉伯数字 + 半角逗号,2026-10-07 用户点名从
        /// `十五、…` 改过来);缩进过的正文行不会误判。
        /// **只认 `数字,` 这一种**:改成中文数字 / 别的标点就整节漏拆(站点少几页却一声不响),
        /// 判据见自检「帮助站点 上手节的标题一律顶格」与「上手节 = 快速上手() 现场拆出来的每一节」 </summary>
        private static readonly Regex 上手标题正则 = new(@"^\d+,\S", RegexOptions.Compiled);
        /// <summary> 《语言细节》的节标题:`【字符串】…`(标题与正文可能在同一行) </summary>
        private static readonly Regex 方括号标题正则 = new(@"^【(.+?)】", RegexOptions.Compiled);

        // ---------------- 三节 ----------------

        /// <summary> 建《介绍》那一节(正文 + 两个子节点) </summary>
        public static 帮助节点C 建介绍节()
        {
            帮助节点C 节 = new()
            {
                标题 = 介绍节名,
                锚点 = 介绍节名,
                正文 = "HB 是一套中文优先的`高级语言 → 中间层 → 汇编`工具链:自研的 4 字节定长指令集、" +
                       "两层语言(中间层与汇编)、自带的虚拟机与调试器,外加一个中文 IDE。" +
                       "这份帮助站点分两半 —— **手写的教程**(介绍 / 上手 / 语言细节)与**照表现场生成的速查**" +
                       "(类型 / 指令 / 关键字 / 内建函数 / 方法 / 类库);后一半只要表动了就自动跟上。",
            };
            节.加(new 帮助节点C
            {
                标题 = "语言特性速览(想学什么找哪儿)",
                锚点 = 锚点(介绍节名, "语言特性速览"),
                正文 = "· **类型**(宽度 / 范围 / 取值)→『类型』一节每个类型一条;上手看《上手》第8节。\n" +
                       "· **控制语句**(如果 / 当 / 循环 / 选择 / 跳出 / 继续)→『关键字』一节逐条 + 《上手》第18节。\n" +
                       "· **函数与模块**→《上手》第10节(多文件怎么共享名字)、第11节(模块与可见性);参数细节看《语言细节》。\n" +
                       "· **结构**(含嵌套、结构数组、字段可见性)→《上手》第19节 + 《语言细节》的「结构参数」。\n" +
                       "· **字符串**(长度按文字算、定长缓冲、进结构、`出` 参数、返回串进表达式)→《上手》第19、20节 + " +
                       "《语言细节》的「字符串」。\n" +
                       "· **数组与列表、堆**(`申请` / `释放` / `取字` / `存字`)→《语言细节》的「数据与函数的样子」与「堆」。\n" +
                       "· **汇编指令**(中英名 / 操作码 / 格式 / 写法示例)→『指令』一节,按编码格式分组。\n" +
                       "· **类库**(用 HB 自己写的库,`包含` 内联)→『类库』一节,一个库一条,正文就是库文件的头注释。",
            });
            节.加(new 帮助节点C
            {
                标题 = "这些文档谁说了算",
                锚点 = 锚点(介绍节名, "文档分工"),
                正文 = "· **这份帮助站点**(`帮助\\index.html`)—— 看文档的唯一入口;" +
                       "IDE 的帮助菜单与 `hbcc --生成帮助` 都指它。\n" +
                       "· **`hbcc --help-text`** —— 纯文本那一份(命令行与自检在用);正文与《上手》《语言细节》同一个来源," +
                       "《介绍》这两小节只在站点里。\n" +
                       "· **`Sample\\说明.md`** —— **按示例讲语言的说明书**(类型 / 字符串 / 结构 / 模块 / 函数 / " +
                       "宿主能力…一节一个话题,都能对到 `Sample` 目录里那个标准工程);查语言怎么写就看它。" +
                       "**不是**流水账目录 —— 目录那部分只在开头一小节。\n" +
                       "· **`指令集说明.md`** —— 指令集与中间层的**规格**(命令怎么编码、关键字与类型有哪些);" +
                       "查指令 / 关键字请用左边那两节。",
            });
            return 节;
        }

        /// <summary> 建《上手》那一节:正文 = 引言,子节点 = 从 `帮助文本C.快速上手()` 现场拆出来的每一节 </summary>
        public static 帮助节点C 建上手节()
        {
            List<(string 标题, string 正文)> 节表 = 拆上手(帮助文本C.快速上手(), out string 引言);
            帮助节点C 节 = new()
            {
                标题 = 上手节名,
                锚点 = 上手节名,
                正文 = (引言.Length > 0 ? 引言 + "\n" : "") +
                       $"下面是 **{节表.Count} 节**手写教程,与命令行 `hbcc --help-text` 的《快速上手》**同一份正文**" +
                       "(站点在生成时现场拆开,改一处两边都变)。",
            };
            foreach ((string 标题, string 正文) in 节表)
                节.加(new 帮助节点C { 标题 = 标题, 锚点 = 锚点(上手节名, 标题), 正文 = 正文 });
            return 节;
        }

        /// <summary>
        /// 建《语言细节》那一节:`帮助文本C.中间层速查()` 里那些**手写说明**。
        /// 「关键字」与「内建函数」两段**不进来** —— 站点里已经有从表现场生成的两节,再来一份就是同一件事说两遍。
        /// </summary>
        public static 帮助节点C 建语言细节节()
        {
            List<(string 标题, string 正文)> 节表 = 拆方括号节(帮助文本C.中间层速查());
            帮助节点C 节 = new()
            {
                标题 = 语言细节节名,
                锚点 = 语言细节节名,
                正文 = "语言的**规矩与坑**都在这儿(取自 `中间层速查` 的手写说明,与命令行 `hbcc --help-text` 同一份正文);" +
                       "关键字与内建函数的逐条说明看站点里那两节(它们是从表现场生成的)。",
            };
            foreach ((string 标题, string 正文) in 节表)
                节.加(new 帮助节点C { 标题 = 标题, 锚点 = 锚点(语言细节节名, 标题), 正文 = 正文 });
            return 节;
        }

        // ---------------- 拆(纯函数,自检直接喂文本) ----------------

        /// <summary> 《快速上手》按 `15,…` 标题拆开;标题之前那一段(第一行 `【一句话】…`)丢掉 </summary>
        public static List<(string 标题, string 正文)> 拆上手(string 文本) => 拆上手(文本, out _);

        /// <summary> 同上,另给引言(标题之前那一段) </summary>
        public static List<(string 标题, string 正文)> 拆上手(string 文本, out string 引言) =>
            分节(文本, 行 => 上手标题正则.IsMatch(行) ? (行.Trim(), "") : null, out 引言);

        /// <summary> 《中间层速查》按 `【…】` 标题拆开(标题与正文可能在同一行) </summary>
        public static List<(string 标题, string 正文)> 拆方括号节(string 文本)
        {
            List<(string 标题, string 正文)> 全部 = 分节(文本, 行 =>
            {
                Match 匹 = 方括号标题正则.Match(行);
                return 匹.Success ? (匹.Groups[1].Value.Trim(), 行[匹.Length..]) : null;
            }, out _);
            // 站点里已经有从表现场生成的『关键字』『内建函数』两节 —— 那两段不再来一份
            List<(string 标题, string 正文)> 结果 = [];
            foreach ((string 标题, string 正文) in 全部)
            {
                if (标题.StartsWith("关键字", StringComparison.Ordinal)) continue;
                if (标题.StartsWith("内建函数", StringComparison.Ordinal)) continue;
                结果.Add((标题, 正文));
            }
            return 结果;
        }

        /// <summary>
        /// 分节的**真身**:`取标题` 给 null = 这是正文行,给 (标题, 余下) = 新的一节开始。
        /// 标题之前的正文行算引言。首尾空行与每行行尾空白丢掉,中间的空行留着(渲染成换行)。
        /// </summary>
        private static List<(string 标题, string 正文)> 分节(
            string 文本, Func<string, (string 标题, string 余下)?> 取标题, out string 引言)
        {
            List<(string 标题, string 正文)> 节表 = [];
            List<string> 引言行 = [], 缓冲 = [];
            string? 标题 = null;

            foreach (string 行 in (文本 ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if (取标题(行) is { } 命中)
                {
                    if (标题 != null) 节表.Add((标题, 成文(缓冲)));
                    缓冲.Clear();
                    标题 = 命中.标题;
                    if (命中.余下.Trim().Length > 0) 缓冲.Add(命中.余下);
                    continue;
                }
                (标题 == null ? 引言行 : 缓冲).Add(行);
            }
            if (标题 != null) 节表.Add((标题, 成文(缓冲)));
            引言 = 成文(引言行);
            return 节表;
        }

        /// <summary> 若干行 → 一段正文:去掉首尾空行、每行行尾空白;中间的空行留着 </summary>
        private static string 成文(List<string> 行)
        {
            List<string> 净 = [.. 行];
            while (净.Count > 0 && 净[0].Trim().Length == 0) 净.RemoveAt(0);
            while (净.Count > 0 && 净[^1].Trim().Length == 0) 净.RemoveAt(净.Count - 1);
            for (int i = 0; i < 净.Count; i++) 净[i] = 净[i].TrimEnd();
            return string.Join("\n", 净);
        }
    }
}
