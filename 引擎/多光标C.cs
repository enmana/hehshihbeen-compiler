namespace hehshihbeen_compiler
{
    /// <summary>
    /// **多光标 / 列选择**的纯规则(P40,2026-10-07;用户排的七件里的第 7 件:"纯编辑器工程量,没设计难题")。
    ///
    /// 这里只有**算**:该在哪儿再放一个光标、同一次输入要往哪几处插什么、哪些光标该并掉。
    /// 真正动文档的是界面那一侧(它才知道主光标在哪、AvalonEdit 的插入发生在哪一刻)。
    /// 分开写的好处与别处一样:自检能直接喂参数把这几条规则真跑一遍。
    ///
    /// 三条口径:
    ///   · **同一列**:加光标时按"当前主光标的列"算,越界夹到行尾(短行不跳过 —— 与 VS Code 一个手感);
    ///   · **去重**:同一个位置上的两个光标并成一个(不然一次输入会在同一处插两遍);
    ///   · **批量替换从后往前**:先动后面的位置,前面的下标才不会被前一次插入的新长度带偏。
    /// </summary>
    public static class 多光标C
    {
        /// <summary> 一个光标:文档下标 + 它那一段选中的长度(0 = 只是插字符)</summary>
        public readonly record struct 光标位C(int 位置, int 选中长度 = 0)
        {
            public int 尾 => 位置 + 选中长度;
        }

        /// <summary>
        /// 从 `主位置` 往**上/下一行**各加一个光标(下 `向下 = true`)。
        /// 列按 `列` 算(0 基,行内的第几个字符);行不够了返回原样。
        /// </summary>
        public static string? 相邻行(int 行号, int 列, bool 向下, string 文档)
        {
            string[] 行 = 切行(文档);
            int 目标 = 行号 + (向下 ? 1 : -1);
            return 目标 >= 1 && 目标 <= 行.Length ? 行[目标 - 1] : null;
        }

        /// <summary> 把"第几行第几列"换算成文档下标(行越界给 null;列越界夹到行尾)</summary>
        public static int? 偏移(string 文档, int 行号, int 列)
        {
            string[] 行 = 切行(文档);
            if (行号 < 1 || 行号 > 行.Length) return null;
            int 偏移 = 0;
            for (int i = 0; i < 行号 - 1; i++) 偏移 += 行[i].Length + 换行长度(文档, 偏移 + 行[i].Length);
            int 列夹 = Math.Clamp(列, 0, 行[行号 - 1].Length);
            return 偏移 + 列夹;
        }

        /// <summary> 第 `偏移` 处那个换行符有几字节(`\r\n` = 2,`\n` = 1,没有 = 0)</summary>
        private static int 换行长度(string 文档, int 偏移)
        {
            if (偏移 >= 文档.Length) return 0;
            if (文档[偏移] == '\r' && 偏移 + 1 < 文档.Length && 文档[偏移 + 1] == '\n') return 2;
            return 文档[偏移] is '\r' or '\n' ? 1 : 0;
        }

        /// <summary> 按与 `检查器C` 同一套切法切行</summary>
        public static string[] 切行(string 文档) =>
            (文档 ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        /// <summary> 第 `偏移` 在第几行第几列(0 基列) </summary>
        public static (int 行, int 列) 位置(string 文档, int 偏移)
        {
            string 文 = 文档 ?? "";
            偏移 = Math.Clamp(偏移, 0, 文.Length);
            int 行 = 1, 行首 = 0;
            for (int i = 0; i < 偏移; i++)
            {
                if (文[i] == '\r' && i + 1 < 文.Length && 文[i + 1] == '\n') { i++; 行++; 行首 = i + 1; }
                else if (文[i] == '\n' || 文[i] == '\r') { 行++; 行首 = i + 1; }
            }
            return (行, 偏移 - 行首);
        }

        /// <summary>
        /// 把一批光标规整一下:**去掉重复位置**、**按位置从大到小排**(批量替换要按这个顺序用)。
        /// 与主光标重合的那些**不在这儿去** —— 那要界面拿主光标的位置来比(见 `去掉主光标`)。
        /// </summary>
        public static List<光标位C> 规整(IEnumerable<光标位C> 光标们)
        {
            var 结果 = new List<光标位C>();
            foreach (var 一 in 光标们)
            {
                if (一.位置 < 0) continue;
                if (结果.Any(x => x.位置 == 一.位置)) continue;
                结果.Add(一);
            }
            结果.Sort((a, b) => b.位置.CompareTo(a.位置));
            return 结果;
        }

        /// <summary> 去掉"跟主光标同一处"的那些(主光标自己那一下由编辑框负责,不然会插两遍)</summary>
        public static List<光标位C> 去掉主光标(IEnumerable<光标位C> 光标们, int 主位置) =>
            [.. 光标们.Where(x => x.位置 != 主位置)];

        /// <summary>
        /// 算一次"在所有光标处替换成 `新文本`"之后的**新文档**与**新光标位置**。
        /// 从后往前替换;返回 null 表示下标对不上(调用方就别动手)。
        /// </summary>
        public static (string 文档, List<int> 新位置)? 批量替换(string 文档, IEnumerable<光标位C> 光标们, string 新文本)
        {
            string 文 = 文档 ?? "";
            新文本 ??= "";
            var 排好 = 规整(光标们);
            var 新位置 = new List<int>();
            foreach (var 一 in 排好)
            {
                int 起 = 一.位置;
                int 长 = Math.Max(0, 一.选中长度);
                if (起 < 0 || 起 + 长 > 文.Length) return null;
                文 = 文[..起] + 新文本 + 文[(起 + 长)..];
                新位置.Add(起 + 新文本.Length);
            }
            // 从后往前改的,"新位置"也是从大到小;给调用方一个升序的,便于它照着摆光标
            新位置.Sort();
            return (文, 新位置);
        }

        /// <summary>
        /// **选中下一个相同的词**(Ctrl+D 那一下):从 `主位置` 往后找**下一个**整词相同的地方,
        /// 返回它的位置与长度(找不到给 null —— 那就什么都不做,不硬选一个不相干的地方)。
        /// `主词` = 现在选中的/光标下那个词。
        /// </summary>
        public static (int 位置, int 长度)? 下一个相同词(string 文档, string? 主词, int 主位置)
        {
            string 文 = 文档 ?? "";
            string 词 = 主词 ?? "";
            if (词.Length == 0) return null;
            int 起找 = Math.Clamp(主位置 + 词.Length, 0, 文.Length);
            for (int i = 起找; i + 词.Length <= 文.Length; i++)
            {
                if (string.CompareOrdinal(文, i, 词, 0, 词.Length) != 0) continue;
                if (i > 0 && 是词字符(文[i - 1])) continue;                       // 前半截也得是词边界
                if (i + 词.Length < 文.Length && 是词字符(文[i + 词.Length])) continue;
                return (i, 词.Length);
            }
            // 从头上再找一遍(光标在最后一个之后时,VS Code 也是回头找)
            for (int i = 0; i + 词.Length <= Math.Min(起找, 文.Length); i++)
            {
                if (string.CompareOrdinal(文, i, 词, 0, 词.Length) != 0) continue;
                if (i > 0 && 是词字符(文[i - 1])) continue;
                if (i + 词.Length < 文.Length && 是词字符(文[i + 词.Length])) continue;
                return (i, 词.Length);
            }
            return null;
        }

        /// <summary> 词字符:一律走 `高亮扫描C.是词字符`(字母/数字/下划线/汉字),全仓只留这一份口径 </summary>
        private static bool 是词字符(char c) => 高亮扫描C.是词字符(c);

        /// <summary>
        /// 输入一段文字之后,把**每个光标的新位置**算出来(界面拿它摆光标)。
        /// `插入位置们` 是"这一次输入落在哪几个位置",`文字长度` 是插进去那段的长度。
        /// </summary>
        public static List<int> 插过之后的位置(IEnumerable<int> 插入位置们, int 文字长度)
        {
            var 结果 = new List<int>();
            foreach (int 位 in 插入位置们) 结果.Add(位 + Math.Max(0, 文字长度));
            结果.Sort();
            return 结果;
        }
    }
}
