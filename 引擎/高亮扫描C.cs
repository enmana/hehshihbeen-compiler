using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary> 一行的"上色位置":从哪开始、多长、属于哪个词类(键见 `配色词类C`) </summary>
    public readonly record struct 高亮位C(int 起始, int 长度, string 词类);

    /// <summary>
    /// **语法高亮的"扫一行"逻辑(引擎侧,不引 WPF)** —— 编辑器那边只负责把词类键换成画刷。
    /// 这么分有两个好处:①配色规则能上自检(命令行只链接 引擎/中间层/自检,看不到 WPF);
    /// ②"谁盖谁"的规则只有一份,不会编辑器一份、自检一份。
    ///
    /// 扫的档位(优先级从低到高,见 `配色词类C.表` 的顺序):
    ///   变量名 / 常量名 / 结构名(靠"声明过的名字"猜,最容易认错 → 最低)
    ///   → 汇编指令 → 语言关键字 → 内建函数 → 方法名 → 类型名
    ///   → 寄存器 → 数字字面量 → 字符串字面量 → 字符字面量 → 注释(最高)
    ///
    /// **优先级不再是"谁后加进列表谁赢"**:同一位置两档撞上时按 `配色词类C.档位` 定输赢
    /// (以前那套依赖 `List.Sort` 的稳定性,同位置撞上时谁赢看运气 —— 实测 `整数` 这种
    /// "既在关键字表、又在类型表"的词就是这种撞法)。
    /// </summary>
    public static partial class 高亮扫描C
    {
        /// <summary> 一行里的"词"(连续字母/数字/下划线/汉字;`.` 是分界)。编辑器与自检共用 </summary>
        public static IEnumerable<(int 位置, string 词)> 取词(string 行)
        {
            if (string.IsNullOrEmpty(行)) yield break;
            int i = 0;
            while (i < 行.Length)
            {
                if (!是词字符(行[i])) { i++; continue; }
                int 起 = i;
                while (i < 行.Length && 是词字符(行[i])) i++;
                yield return (起, 行[起..i]);
            }
        }

        /// <summary> 词字符:字母 / 数字 / 下划线 / 汉字(与编辑器那边"找鼠标下的词"同一套口径) </summary>
        public static bool 是词字符(char c) =>
            char.IsLetterOrDigit(c) || c == '_' || (c >= 0x4E00 && c <= 0x9FFF);

        /// <summary>
        /// 扫一行,返回按起始排好、互不重叠的高亮位。
        /// </summary>
        /// <param name="行">这一行的文本(不含换行)</param>
        /// <param name="名字集">"声明过的名字"(结构/变量);null = 这一行不参与名字上色</param>
        /// <param name="注释区">这一行与注释相交的区间(已换算成行内下标);null = 本行内自己认 `//` 与 `/* */`</param>
        public static List<高亮位C> 扫一行(string 行, 配色名字集C? 名字集, IReadOnlyList<(int 开始, int 结束)>? 注释区 = null)
        {
            var 片段 = new List<高亮位C>();
            if (string.IsNullOrEmpty(行)) return 片段;

            var 注释 = 注释区 != null ? [.. 注释区] : 行内注释区(行);
            var 字面量 = 代码解析器.取字面量区(行);
            var 保护 = 合并区间([.. 注释, .. 字面量.Select(x => (x.开始, x.结束))]);

            bool 在保护内(int 位置) => 保护.Any(x => 位置 >= x.开始 && 位置 < x.结束);

            // ① 名字(变量/常量/结构/用户方法名):最低档,先摆上去,后面的档位会盖掉它
            if (名字集 is { 空: false })
            {
                foreach (var (位置, 词) in 取词(行))
                {
                    if (在保护内(位置)) continue;
                    if (名字集.结构.Contains(词)) 片段.Add(new 高亮位C(位置, 词.Length, 配色词类C.结构));
                    else if (名字集.常量名.Contains(词)) 片段.Add(new 高亮位C(位置, 词.Length, 配色词类C.常量));
                    else if (名字集.变量.Contains(词)) 片段.Add(new 高亮位C(位置, 词.Length, 配色词类C.变量));
                    else if (名字集.方法名.Contains(词)) 片段.Add(new 高亮位C(位置, 词.Length, 配色词类C.用户方法名));
                }
            }

            // ② 固定词表:指令 / 关键字 / 内建函数 / 方法 / 类型
            摆词表(片段, 行, 保护, 指令C.取全部指令名(), 配色词类C.指令, 必须带点: false);
            摆词表(片段, 行, 保护, 中间层指令C.全部关键字, 配色词类C.关键字, 必须带点: false);
            摆词表(片段, 行, 保护, 中间层指令C.全部内建函数词, 配色词类C.内建函数, 必须带点: false);
            摆词表(片段, 行, 保护, 中间层指令C.全部方法词, 配色词类C.方法, 必须带点: true);
            摆词表(片段, 行, 保护, 中间层指令C.全部类型词, 配色词类C.类型, 必须带点: false);

            // ③ 寄存器与数字
            foreach (Match m in 寄存器正则().Matches(行))
                if (!在保护内(m.Index)) 片段.Add(new 高亮位C(m.Index, m.Length, 配色词类C.寄存器));
            foreach (Match m in 数字正则().Matches(行))
                if (!在保护内(m.Index)) 片段.Add(new 高亮位C(m.Index, m.Length, 配色词类C.数字));

            // ④ 字面量(字符串/字符)与注释:最高两档,盖住里面所有东西
            foreach (var (开始, 结束, 是字符) in 字面量)
                片段.Add(new 高亮位C(开始, 结束 - 开始, 是字符 ? 配色词类C.字符 : 配色词类C.字符串));
            foreach (var (开始, 结束) in 注释)
                片段.Add(new 高亮位C(开始, 结束 - 开始, 配色词类C.注释));

            return 合并(片段);
        }

        /// <summary> 按固定词表摆一遍(`必须带点` = 只认 `.名字` 那种写法,给"方法"用) </summary>
        private static void 摆词表(List<高亮位C> 片段, string 行, List<(int 开始, int 结束)> 保护, IEnumerable<string> 词表, string 词类, bool 必须带点)
        {
            foreach (string 词 in 词表)
            {
                int 起始 = 0;
                while (true)
                {
                    int 找到 = 行.IndexOf(词, 起始, StringComparison.Ordinal);
                    if (找到 == -1) break;
                    if (!在保护内(保护, 找到) && 边界合法(行, 找到, 词, 必须带点))
                        片段.Add(new 高亮位C(找到, 词.Length, 词类));
                    起始 = 找到 + 1;
                }
            }
        }

        private static bool 在保护内(List<(int 开始, int 结束)> 保护, int 位置) =>
            保护.Any(x => 位置 >= x.开始 && 位置 < x.结束);

        /// <summary>
        /// 左右边界必须"不粘着别的字母/数字";带点方法还要**前面紧跟 `.`**
        /// (不然用户起个叫 `添加` 的变量也会被涂成方法色 —— 用户点过这一条)。
        /// 与 中间层指令C.是内建词高亮位 同一套口径。
        /// </summary>
        private static bool 边界合法(string 行, int 位置, string 词, bool 必须带点)
        {
            if (必须带点)
            {
                if (位置 == 0 || 行[位置 - 1] != '.') return false;
            }
            else if (位置 > 0 && char.IsLetterOrDigit(行[位置 - 1])) return false;
            int 止 = 位置 + 词.Length;
            return 止 >= 行.Length || !char.IsLetterOrDigit(行[止]);
        }

        /// <summary>
        /// 本行内的注释区间:`//` 到行尾、`/* */` 成对(**跨行的块注释由调用方传整篇的区间进来**)。
        ///
        /// **T29(2026-09-27 评测整改)**:以前这里是**自己写的一小套扫描** —— 它不跳字符串/字符字面量,
        /// 于是 `字符串 u = "http://主机:80/路径";` 这一行被认成"从 `//` 起是注释"(区间 `(14,26)`,
        /// 把网址后半截连收尾引号一起涂成注释色)。现在一律转调 **`代码解析器.取注释区`**:
        /// 注释区 / 高亮 / 检查器 / 折叠区 全仓只剩那一份口径。
        /// </summary>
        public static List<(int 开始, int 结束)> 行内注释区(string 行) =>
            代码解析器.取注释区(行);

        /// <summary> 把区间排好序、合并成互不重叠的一串 </summary>
        public static List<(int 开始, int 结束)> 合并区间(List<(int 开始, int 结束)> 区)
        {
            var 结果 = new List<(int, int)>();
            if (区.Count == 0) return 结果;
            区.Sort((a, b) => a.开始 != b.开始 ? a.开始.CompareTo(b.开始) : a.结束.CompareTo(b.结束));
            结果.Add(区[0]);
            for (int i = 1; i < 区.Count; i++)
            {
                var (起, 止) = 结果[^1];
                if (区[i].开始 <= 止) 结果[^1] = (起, Math.Max(止, 区[i].结束));
                else 结果.Add(区[i]);
            }
            return 结果;
        }

        /// <summary>
        /// 合并重叠片段:**档位高的赢**;档位一样时长的赢(都长就取先出现的)。
        /// 返回按起始排好、互不重叠的一串 —— 编辑器照着一段一段画就行。
        /// </summary>
        public static List<高亮位C> 合并(List<高亮位C> 片段)
        {
            var 结果 = new List<高亮位C>();
            if (片段.Count == 0) return 结果;

            // 每个位置只留"赢的那个"再切分:先把片段按 起始、档位降、长度降 排序,再逐段裁
            var 排好 = 片段
                .Where(x => x.长度 > 0)
                .OrderBy(x => x.起始)
                .ThenByDescending(x => 配色词类C.档位(x.词类))
                .ThenByDescending(x => x.长度)
                .ToList();

            foreach (var 段 in 排好)
            {
                int 起 = 段.起始, 止 = 段.起始 + 段.长度;
                if (结果.Count > 0)
                {
                    var 上 = 结果[^1];
                    int 上止 = 上.起始 + 上.长度;
                    if (起 < 上止)
                    {
                        // 与上一段重叠:谁档位高听谁的
                        int 上档 = 配色词类C.档位(上.词类), 本档 = 配色词类C.档位(段.词类);
                        if (本档 > 上档)
                        {
                            if (起 > 上.起始) 结果[^1] = 上 with { 长度 = 起 - 上.起始 };   // 上一段被截短
                            else 结果.RemoveAt(结果.Count - 1);                              // 上一段整段被吃掉
                        }
                        else
                        {
                            if (止 <= 上止) continue;                                        // 本段被上一段盖住
                            起 = 上止;                                                       // 本段只留右边露出来那一截
                        }
                    }
                }
                if (止 > 起) 结果.Add(new 高亮位C(起, 止 - 起, 段.词类));
            }
            return 结果;
        }

        [GeneratedRegex(@"\bR[0-9]+\b", RegexOptions.Compiled)]
        private static partial Regex 寄存器正则();

        [GeneratedRegex(@"\b0x[0-9A-Fa-f]+\b|\b\d+\b", RegexOptions.Compiled)]
        private static partial Regex 数字正则();
    }
}
