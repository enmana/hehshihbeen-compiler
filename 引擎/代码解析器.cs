using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 代码解析器(引擎层):集中处理与文本结构相关的解析——
    /// 注释(// 与 /* */)、词元首词、控制关键字识别、顶层可展开构造定位、块(})配对。
    /// 语法高亮、展开层、编辑器折叠均基于它;业务上的“汇编指令编码/执行”不在这里。
    /// </summary>
    public static class 代码解析器
    {
        // ============ 词法:注释 ============

        /// <summary>
        /// 扫描文本中的注释区间(// 到行尾、/* */ 可跨行),返回 (开始, 结束)。
        /// **字符串/字符字面量整段跳过**:里面的 `//` 不是注释 ——
        /// D5 的网络能力就是踩在这个上:`取网址("http://主机:端口/", …)` 以前会被当成"从 `//` 起是注释",
        /// 于是网址根本写不出来(报"字符串字面量缺少结尾的引号")。
        /// 与 `取字面量区` 同一口径:认 `\` 转义;没闭合的字面量扫到行尾为止。
        /// </summary>
        public static List<(int 开始, int 结束)> 取注释区(string 文本)
        {
            var 结果 = new List<(int, int)>();
            if (string.IsNullOrEmpty(文本)) return 结果;
            int 位置 = 0;
            while (位置 < 文本.Length)
            {
                char 当前 = 文本[位置];
                if (当前 == '"' || 当前 == '\'') { 位置 = 跳过字面量(文本, 位置); continue; }
                if (位置 + 1 < 文本.Length && 当前 == '/' && 文本[位置 + 1] == '/')
                {
                    int 行尾 = 文本.IndexOf('\n', 位置);
                    int 结束 = 行尾 < 0 ? 文本.Length : 行尾;
                    结果.Add((位置, 结束));
                    位置 = 结束;
                }
                else if (位置 + 1 < 文本.Length && 当前 == '/' && 文本[位置 + 1] == '*')
                {
                    int 结尾 = 文本.IndexOf("*/", 位置 + 2, StringComparison.Ordinal);
                    int 结束 = 结尾 < 0 ? 文本.Length : 结尾 + 2;
                    结果.Add((位置, 结束));
                    位置 = 结束;
                }
                else 位置++;
            }
            return 结果;
        }

        /// <summary>
        /// **T15(2026-09-27 评测整改)**:扫"没闭合的块注释",返回那个 `/*` 的 **1 起行号**(都好就返回 0)。
        ///
        /// 为什么单列一个:`取注释区` 对没闭合的 `/*` 只能"把注释区拉到文本末尾"(它没有报错的出口),
        /// 于是**文件里剩下的内容全部无声消失**、编译退出码 0。实测:
        /// <code>
        /// 整数 初值 = 1;
        /// 测试输出(初值);
        /// /* 这里忘了关
        /// 测试输出(999);
        /// 停止
        /// </code>
        /// 只输出 `1`,后面两行不见了。对照:没闭合的 `"` 是会报错的(见 `表达式语法C`)——
        /// 同一个类别的两种写法,一个报一个不报。扫描口径与 `取注释区` 一字不差:
        /// 字符串 / 字符字面量整段跳过(网址里的 `//` 不是注释),认 `\` 转义。
        /// </summary>
        public static int 找没闭合的块注释(string 文本)
        {
            if (string.IsNullOrEmpty(文本)) return 0;
            int 位置 = 0;
            while (位置 < 文本.Length)
            {
                char 当前 = 文本[位置];
                if (当前 == '"' || 当前 == '\'') { 位置 = 跳过字面量(文本, 位置); continue; }
                if (位置 + 1 < 文本.Length && 当前 == '/' && 文本[位置 + 1] == '/')
                {
                    int 行尾 = 文本.IndexOf('\n', 位置);
                    位置 = 行尾 < 0 ? 文本.Length : 行尾;
                }
                else if (位置 + 1 < 文本.Length && 当前 == '/' && 文本[位置 + 1] == '*')
                {
                    int 结尾 = 文本.IndexOf("*/", 位置 + 2, StringComparison.Ordinal);
                    if (结尾 < 0) return 数行(文本, 位置);
                    位置 = 结尾 + 2;
                }
                else 位置++;
            }
            return 0;
        }

        /// <summary> 第 位置 那个字符在**第几行**(1 起;块注释的抬头按 `/*` 自己那一行报) </summary>
        private static int 数行(string 文本, int 位置)
        {
            int 行 = 1;
            for (int i = 0; i < 位置 && i < 文本.Length; i++)
                if (文本[i] == '\n') 行++;
            return 行;
        }

        /// <summary> 从 `起`(那个引号)扫到字面量结尾的下一个位置;认 `\` 转义,没闭合就到行尾(不吃掉换行) </summary>
        private static int 跳过字面量(string 文本, int 起)
        {
            char 引号 = 文本[起];
            int 尾 = 起 + 1;
            while (尾 < 文本.Length)
            {
                if (文本[尾] == '\\') { 尾 += 2; continue; }
                if (文本[尾] == 引号) { 尾++; break; }
                if (文本[尾] == '\n') break;
                尾++;
            }
            return Math.Min(尾, 文本.Length);
        }

        /// <summary>
        /// 扫描文本中的**字面量**区间:`"…"`(字符串)与 `'…'`(字符),认 `\` 转义。
        /// 返回 (开始, 结束, 是不是字符字面量)。编辑器拿它给两类字面量分别上色(引擎侧 `高亮扫描C`);
        /// 中英切换拿它绕开字面量(见 `取保护改写区`)。
        /// </summary>
        public static List<(int 开始, int 结束, bool 是字符)> 取字面量区(string 文本)
        {
            var 结果 = new List<(int, int, bool)>();
            if (string.IsNullOrEmpty(文本)) return 结果;
            int 位置 = 0;
            while (位置 < 文本.Length)
            {
                char c = 文本[位置];
                if (c != '"' && c != '\'') { 位置++; continue; }
                char 引号 = c;
                int 尾 = 位置 + 1;
                while (尾 < 文本.Length)
                {
                    if (文本[尾] == '\\') { 尾 += 2; continue; }     // 跳过被转义的字符(\" \\ \n …)
                    if (文本[尾] == 引号) { 尾++; break; }
                    if (文本[尾] == '\n') break;                     // 单行没闭合(语言里本来也不允许):到行尾为止
                    尾++;
                }
                if (尾 > 文本.Length) 尾 = 文本.Length;
                结果.Add((位置, 尾, 引号 == '\''));
                位置 = 尾;
            }
            return 结果;
        }

        /// <summary>
        /// 扫描文本中的**字符串/字符字面量**区间(两类合在一起,只给位置)。
        /// 用途:中英切换这类"按词改写源码"的操作必须**绕开字面量** ——
        /// 不然 `输出行("字符串")` 会被改成 `输出行("string")`,字面量的内容就被偷偷改了。
        /// (注释区是另一件事,看 `取注释区`;两者可能重叠,调用方取并集用。)
        /// </summary>
        public static List<(int 开始, int 结束)> 取字符串区(string 文本)
        {
            var 结果 = new List<(int, int)>();
            foreach (var (开始, 结束, _) in 取字面量区(文本)) 结果.Add((开始, 结束));
            return 结果;
        }

        /// <summary>
        /// "按词改写源码"(中英切换)时必须**原样跳过**的区间:注释 ∪ 字符串/字符字面量。
        /// 两段区间可能互相压着(比如字符串里写了个 `//`),这里排好序、合并成互不重叠的一串,
        /// 调用方按它切"能改的代码段 / 不能改的原样段"即可。
        /// </summary>
        public static List<(int 开始, int 结束)> 取保护改写区(string 文本)
        {
            var 区 = new List<(int 开始, int 结束)>();
            区.AddRange(取注释区(文本));
            区.AddRange(取字符串区(文本));
            if (区.Count == 0) return 区;
            区.Sort((a, b) => a.开始 != b.开始 ? a.开始.CompareTo(b.开始) : a.结束.CompareTo(b.结束));
            var 合并 = new List<(int 开始, int 结束)> { 区[0] };
            for (int i = 1; i < 区.Count; i++)
            {
                var (起, 止) = 合并[^1];
                if (区[i].开始 <= 止) 合并[^1] = (起, Math.Max(止, 区[i].结束));
                else 合并.Add(区[i]);
            }
            return 合并;
        }

        /// <summary> 去掉注释后返回纯代码文本 </summary>
        public static string 去注释(string 文本)
        {
            if (string.IsNullOrEmpty(文本)) return 文本;
            var 注释区表 = 取注释区(文本);
            if (注释区表.Count == 0) return 文本;
            var sb = new StringBuilder(文本.Length);
            int 位置 = 0;
            foreach (var (开始, 结束) in 注释区表)
            {
                if (开始 > 位置) sb.Append(文本, 位置, 开始 - 位置);
                位置 = 结束;
            }
            if (位置 < 文本.Length) sb.Append(文本, 位置, 文本.Length - 位置);
            return sb.ToString();
        }

        /// <summary> 把注释字符替换为空格(保留 \n \r 与行数),注释内的关键字/花括号不影响结构判断 </summary>
        public static string 遮蔽注释(string 文本)
        {
            if (string.IsNullOrEmpty(文本)) return 文本;
            var 区 = 取注释区(文本);
            if (区.Count == 0) return 文本;
            char[] 字符 = 文本.ToCharArray();
            foreach (var (开始, 结束) in 区)
                for (int i = 开始; i < 结束; i++)
                    if (字符[i] != '\n' && 字符[i] != '\r') 字符[i] = ' ';
            return new string(字符);
        }

        /// <summary>
        /// **T29(2026-09-27 评测整改)**:把"注释 ∪ 字符串/字符字面量"整段换成空格(换行原样保留)。
        ///
        /// 用途:`检查器C` 那几处"在代码里找这个词"必须**同一套口径** ——
        /// 以前 `检查器C` 自己写了一套只认 `"`、只认 `//` 的小扫描,于是
        /// `字符 引号 = '"'; // …` 这一行里真实存在的调用**漏检**(引号状态被那个 `"` 带反了)。
        /// 现在只留这一份:整段遮蔽之后,`IndexOf` 找得到的就是**代码里**的,找不到的就是注释/字面量里的。
        /// 长度与行数都一字不变(只换空格),所以下标可以直接拿去报列号。
        /// </summary>
        public static string 遮蔽区间(string 文本)
        {
            if (string.IsNullOrEmpty(文本)) return 文本;
            var 区 = 取保护改写区(文本);      // 注释 ∪ 字面量,已排序合并
            if (区.Count == 0) return 文本;
            char[] 字符 = 文本.ToCharArray();
            foreach (var (开始, 结束) in 区)
                for (int i = 开始; i < 结束 && i < 字符.Length; i++)
                    if (字符[i] != '\n' && 字符[i] != '\r') 字符[i] = ' ';
            return new string(字符);
        }

        // ============ 词法:首词与控制关键字 ============

        private static readonly Regex 首词正则 = new(@"^[\p{L}_][\p{L}\p{N}_]*", RegexOptions.Compiled);

        /// <summary> 取一行/一段文字开头的标识词(含中文) </summary>
        public static string 取首词(string 文本)
        {
            var 匹配 = 首词正则.Match(文本);
            return 匹配.Success ? 匹配.Value : "";
        }

        /// <summary> 首词是不是"能展开成汇编的块构造"(如果/选择/循环/遍历/当 及其英文) </summary>
        public static bool 是构造首词(string 首词) =>
            !string.IsNullOrEmpty(首词) &&
            中间层指令C.块关键字.Contains(首词, StringComparer.OrdinalIgnoreCase);

        /// <summary> 首词是不是"能展开成汇编的单行构造"(测试输出 这类,一行展开一小段、没有 {}) </summary>
        public static bool 是单行构造首词(string 首词) =>
            !string.IsNullOrEmpty(首词) &&
            中间层指令C.单行关键字.Contains(首词, StringComparer.OrdinalIgnoreCase);

        /// <summary> 这两种都算"可展开构造"(块 + 单行) </summary>
        public static bool 是可展开首词(string 首词) => 是构造首词(首词) || 是单行构造首词(首词);

        // ============ 结构:块定位 ============

        /// <summary> 返回顶层可展开构造的起始行号(1 基,注释已遮蔽) </summary>
        public static List<int> 取顶层构造起始行号(string 文本)
        {
            var 结果 = new List<int>();
            if (string.IsNullOrEmpty(文本)) return 结果;
            string[] 行表 = 遮蔽注释(文本).Split('\n');
            int 深度 = 0;
            for (int i = 0; i < 行表.Length; i++)
            {
                string 行 = 行表[i].TrimEnd('\r');
                string 修剪 = 行.Trim();
                if (修剪.Length == 0) continue;

                int 前深度 = 深度;
                if (前深度 == 0 && 是可展开首词(取首词(修剪)))
                    结果.Add(i + 1);

                foreach (char c in 行)
                {
                    if (c == '{') 深度++;
                    else if (c == '}') 深度 = Math.Max(0, 深度 - 1);
                }
            }
            return 结果;
        }

        /// <summary>
        /// 从起始行(1 基)找该构造的结束行(1 基),自动把后随 else/否则 并入同一构造;
        /// 找不到配对 '}' 返回 -1。
        /// </summary>
        public static int 找结束行(string 文本, int 起始行号)
        {
            if (string.IsNullOrEmpty(文本)) return -1;
            string[] 行表 = 遮蔽注释(文本).Split('\n');
            int 起始 = 起始行号 - 1;
            if (起始 < 0 || 起始 >= 行表.Length) return -1;

            int 深度 = 0;
            bool 见过开括号 = false;
            for (int i = 起始; i < 行表.Length; i++)
            {
                string 行 = 行表[i].TrimEnd('\r');
                int 加 = 0, 减 = 0;
                foreach (char c in 行)
                {
                    if (c == '{') 加++;
                    else if (c == '}') 减++;
                }
                深度 += 加 - 减;
                if (深度 > 0) 见过开括号 = true;

                if (见过开括号 && 深度 == 0)
                {
                    // 若后跟 else/否则(同一层),继续并入
                    int 下一行 = i + 1;
                    while (下一行 < 行表.Length)
                    {
                        string 下一 = 行表[下一行].Trim();
                        if (下一.Length == 0) { 下一行++; continue; }
                        if (下一 == "else" || 下一 == "否则" ||
                            下一.StartsWith("else", StringComparison.OrdinalIgnoreCase) ||
                            下一.StartsWith("否则"))
                        {
                            i = 下一行 - 1;
                            深度 = 0; 见过开括号 = false;
                        }
                        break;
                    }
                    if (深度 == 0 && 见过开括号 == false && i >= 起始) continue;
                    if (深度 == 0) return i + 1;
                }
            }
            return -1;
        }
    }
}
