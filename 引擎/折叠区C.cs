namespace hehshihbeen_compiler
{
    /// <summary>
    /// 代码折叠区(C13):把一份源码文本算成"哪些区段可以折起来"。
    ///
    /// **为什么放引擎层**:折叠只是观感,但"什么算一个块"是**规则** —— 规则要能被自检直接钉住。
    /// 界面里的 `刷新折叠区()` 只能按源码文本对着断言(第141批就是这么办的),费劲还容易漏;
    /// 规则挪到这里以后,自检可以喂一段源码、逐条比算出来的区段(见 `综合自检C.折叠区自检()`)。
    ///
    /// 三条规则(与 VS 那种通用折叠一致):
    /// <list type="number">
    /// <item>**花括号块**:`{` 与配对的 `}` 之间**跨行**才算(单行 `{ … }` 不折);</item>
    /// <item>**连续注释块**:连着两行以上只由 `//` 组成的注释(文件头、分隔条那种);</item>
    /// <item>**跨行块注释**:`/* … */` 跨行的才折(写在一行里的不折)。</item>
    /// </list>
    ///
    /// **注释与字符串/字符字面量里的括号一律不算**:`输出行("{");` 里那个 `{` 不是块的开始
    /// (不认这一条就会拿字符串里的 `{` 去配后面的 `}`,折出一段跟代码块毫无关系的假块)。
    ///
    /// 界面那边(AvalonEdit 的 `NewFolding`)认的是**字符偏移**,所以这里给的就是偏移 ——
    /// 而且是**行首/行尾**的偏移:AvalonEdit 收起一段折叠区时是"起点那一行留着、往后到末行整行藏起来",
    /// 所以 `{` **独占一行**时(本项目常见的"花括号另起一行"写法)起点取**上一行的行尾**:
    /// 折叠标记落在 `当 (…)` / `函数 f()` 那一行 —— **用户 2026-09-19 复核:要对齐的是这一行,不是 `{` 那一行**
    /// (第150批曾按当时的口径改成"`{` 那一行",第151批照实测复核改回来),折起来以后这一行还看得见;
    /// `{` 跟在同一行尾巴上时(`如果 (…) {`)起点就是**那个 `{` 自己**(C24 实测改过来的:
    /// 取行首会把整行签名一起折掉,折叠后只剩一个 `…`)。
    /// </summary>
    public static class 折叠区C
    {
        /// <summary> 一段折叠区:字符偏移 `起始`(含)到 `结束`(不含)。与 AvalonEdit 的 `NewFolding(起始, 结束)` 同口径 </summary>
        public readonly record struct 区段(int 起始, int 结束);

        /// <summary> 一行:行首偏移 / 行尾偏移(不含换行符)/ 这一行的文本 </summary>
        private readonly record struct 行信息(int 首, int 尾, string 文本);

        /// <summary>
        /// 算出整篇文本的折叠区(按起始偏移排好序)。算不出来就返回空表,绝不抛异常 ——
        /// 调用方(编辑器)拿它当"折叠只是观感"用。
        /// </summary>
        public static List<区段> 算(string 正文)
        {
            var 结果 = new List<区段>();
            if (string.IsNullOrEmpty(正文)) return 结果;
            try
            {
                var 行表 = 切行(正文);
                // 注释 ∪ 字符串/字符字面量:这些地方出现的 `{` `}` `/*` 都不是代码
                var 保护 = 代码解析器.取保护改写区(正文);
                算花括号块(正文, 行表, 保护, 结果);
                算连续注释块(行表, 结果);
                算块注释(正文, 行表, 结果);
                结果.Sort((a, b) => a.起始 != b.起始 ? a.起始.CompareTo(b.起始) : a.结束.CompareTo(b.结束));
            }
            catch { 结果.Clear(); }
            return 结果;
        }

        /// <summary> ① 花括号块:一个栈按 (行, 偏移) 配对,只收**跨行**的 </summary>
        private static void 算花括号块(string 正文, List<行信息> 行表, List<(int 开始, int 结束)> 保护, List<区段> 结果)
        {
            var 栈 = new Stack<(int 行, int 偏移)>();
            int 保护号 = 0;         // 保护区按起点升序,跟着扫描往前走(不每个字符去二分)
            int 行号 = 1;
            for (int 位 = 0; 位 < 正文.Length; 位++)
            {
                while (行号 < 行表.Count && 位 >= 行表[行号].首) 行号++;
                char c = 正文[位];
                if (c != '{' && c != '}') continue;
                while (保护号 < 保护.Count && 位 >= 保护[保护号].结束) 保护号++;
                if (保护号 < 保护.Count && 位 >= 保护[保护号].开始) continue;   // 在注释或字面量里,不算
                if (c == '{') 栈.Push((行号, 位));
                else if (栈.Count > 0)
                {
                    var 开 = 栈.Pop();
                    if (行号 <= 开.行) continue;                                 // 单行 `{ … }` 不折
                    var 开行 = 行表[开.行 - 1];
                    // `{` 独占一行(前面只有空白)⇒ 起点取**上一行行尾**:折叠标记落在 `当 (…)` / 签名那一行
                    // (用户复核:要对齐的是语句那一行,不是 `{` 那一行),折起来以后那一行还看得见。
                    // `{` 跟在同一行尾巴上(`当 (轮 < 100) {`)⇒ 起点取**那个 `{` 自己**,不是行首 ——
                    // **这一行有此 bug 的实锤**:起点取行首时 AvalonEdit 把这一行从行首到折点整段折掉,
                    // 折叠后屏幕上只剩一个 `…`(用户 2026-09-20 实测:示例 58/59 的 `当 (轮 < 100) {` 折完变 `+ ...`)。
                    // 取 `{` 自己 ⇒ 折点在签名文字之后,折叠后是 `当 (轮 < 100) …`。
                    bool 从上一行起 = 开.行 > 1 && 只空白(正文, 开行.首, 开.偏移)
                        && 有非空白(正文, 行表[开.行 - 2].首, 行表[开.行 - 2].尾);
                    int 起 = 从上一行起 ? 行表[开.行 - 2].尾 : 开.偏移;
                    结果.Add(new 区段(起, 行表[行号 - 1].尾));
                }
            }
        }

        /// <summary> ② 连续 `//` 注释块:连着 2 行以上才算(单行注释不折) </summary>
        private static void 算连续注释块(List<行信息> 行表, List<区段> 结果)
        {
            int 起行 = 0;
            for (int 行 = 1; 行 <= 行表.Count + 1; 行++)
            {
                bool 是注释行 = 行 <= 行表.Count && 行表[行 - 1].文本.Trim().StartsWith("//", StringComparison.Ordinal);
                if (是注释行) { if (起行 == 0) 起行 = 行; continue; }
                if (起行 > 0 && 行 - 起行 >= 2) 结果.Add(new 区段(行表[起行 - 1].首, 行表[行 - 2].尾));
                起行 = 0;
            }
        }

        /// <summary> ③ 跨行块注释 `/* … */`:直接用注释区,不再"按行找 `/*` / `*/`"(那样会把字符串里的 `/*` 也算进来) </summary>
        private static void 算块注释(string 正文, List<行信息> 行表, List<区段> 结果)
        {
            foreach (var (开始, 结束) in 代码解析器.取注释区(正文))
            {
                if (开始 + 1 >= 正文.Length || 正文[开始] != '/' || 正文[开始 + 1] != '*') continue;   // `//` 是上一条规则的事
                if (结束 <= 开始) continue;
                int 起行 = 行号(行表, 开始);
                int 止行 = 行号(行表, 结束 - 1);
                if (止行 > 起行) 结果.Add(new 区段(行表[起行 - 1].首, 行表[止行 - 1].尾));
            }
        }

        /// <summary> 按 `\n` 切行(认 `\r\n`:行尾不含换行符,与 AvalonEdit 的 `DocumentLine.EndOffset` 同口径) </summary>
        private static List<行信息> 切行(string 正文)
        {
            var 表 = new List<行信息>();
            int 首 = 0;
            for (int 位 = 0; 位 <= 正文.Length; 位++)
            {
                if (位 < 正文.Length && 正文[位] != '\n') continue;
                int 尾 = 位;
                if (尾 > 首 && 正文[尾 - 1] == '\r') 尾--;
                表.Add(new 行信息(首, 尾, 正文[首..尾]));
                首 = 位 + 1;
            }
            return 表;
        }

        /// <summary> 偏移落在第几行(1 起;二分) </summary>
        private static int 行号(List<行信息> 行表, int 偏移)
        {
            int 低 = 0, 高 = 行表.Count - 1, 结果 = 1;
            while (低 <= 高)
            {
                int 中 = (低 + 高) / 2;
                if (行表[中].首 <= 偏移) { 结果 = 中 + 1; 低 = 中 + 1; }
                else 高 = 中 - 1;
            }
            return 结果;
        }

        /// <summary> [起, 止) 里全是空白吗 </summary>
        private static bool 只空白(string 正文, int 起, int 止)
        {
            for (int i = 起; i < 止 && i < 正文.Length; i++) if (!char.IsWhiteSpace(正文[i])) return false;
            return true;
        }

        /// <summary> [起, 止) 里有非空白字符吗 </summary>
        private static bool 有非空白(string 正文, int 起, int 止)
        {
            for (int i = 起; i < 止 && i < 正文.Length; i++) if (!char.IsWhiteSpace(正文[i])) return true;
            return false;
        }
    }
}
