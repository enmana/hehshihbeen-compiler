using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 编辑器里的**轻量检查**(不编译、只看文本 —— 编译错误由编译器另外报,标红)。
    ///
    /// 为什么另做一个:编译器是"碰到第一个错就抛异常",天生只能报一条、而且报完就不走了;
    /// 编辑器要的是"一次标出很多处"。所以这里只放**不需要完整编译**就能判的规则:
    ///   黄(警告):声明了没用的变量 / `停止` 之后的死代码 / 恒真恒假的 如果·当 / 空循环体
    ///   绿(提示):还在用 测试输出()(现在有 输出行() 了)/ Tab 与空格混用缩进 / 行尾多余空格
    /// 规则都写成"只看这一行"或"看相邻几行",不会因为半个文件没写完就乱报(写一半的代码也不该满屏红线)。
    /// </summary>
    public static class 检查器C
    {
        /// <summary> 一行里"声明了一个基本类型变量"的样子:`短整数 计数 = 0;` / `var 数;` </summary>
        private static readonly Regex 声明正则 = new(
            @"^\s*(?:字节|逻辑|字符|短整数|变量|var|byte|logic|char|short)\s+([\p{L}_][\p{L}\p{N}_]*)\s*(?:=|;|$|\s*//)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary> 恒真的写法:条件和常量比(`如果 (1)` / `当 (0)`) </summary>
        private static readonly Regex 条件正则 = new(@"^\s*(?:如果|当|if|while)\s*\(\s*(?<值>\d+|真|假|true|false)\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex 循环头正则 = new(@"^\s*(?:循环|当|遍历|for|while|foreach)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary> 循环头里"窄类型计数器 + 常量大上界"的样子:`循环 (短整数 i = 0; i < 100000; i++)` </summary>
        private static readonly Regex 窄计数器正则 = new(
            @"(?:循环|for)\s*\(\s*(?<类型>字节|逻辑|字符|短整数|byte|logic|char|short)\s+(?<名>[\p{L}_][\p{L}\p{N}_]*)\s*=[^;]*;\s*\k<名>\s*<\s*(?<上界>\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// **H28**:整词匹配的**字符集**,与老的正则 `[\p{L}\p{N}_]` 逐字相同
        /// (`char.IsNumber` 覆盖 Nd / Nl / No 三类,和 `\p{N}` 一个口径)。
        /// </summary>
        private static bool 是词字符(char c) => char.IsLetter(c) || char.IsNumber(c) || c == '_';

        /// <summary>
        /// **H28**:全文每个"整词"各出现几次(逐行**去掉注释**之后算,与老口径一样)。
        ///
        /// 为什么要它:老写法是"**每个声明**现编一个逐名字的正则、再把整份文件扫一遍"
        /// (`出现次数` 每调一次 `new Regex`),代价 ≈ 声明数 × 行数;现在是**全文走一遍**。
        /// 口径不变:老正则 `(?&lt;![\p{L}\p{N}_])名(?![\p{L}\p{N}_])` 数的是"名字作为一个**整词**出现",
        /// 而声明正则抓出来的名字本身就只含词字符 ⇒ 与"数最大词串里有几个等于它"逐字等价。
        /// </summary>
        private static Dictionary<string, int> 统计整词(string[] 行表)
        {
            var 表 = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string 行 in 行表)
            {
                string 干 = 代码解析器.去注释(行);
                int i = 0;
                while (i < 干.Length)
                {
                    if (!是词字符(干[i])) { i++; continue; }
                    int 起 = i;
                    while (i < 干.Length && 是词字符(干[i])) i++;
                    string 词 = 干[起..i];
                    表[词] = 表.TryGetValue(词, out int 次) ? 次 + 1 : 1;
                }
            }
            return 表;
        }

        /// <summary>
        /// 检查一份源码,返回按行排好的诊断。
        /// 参数 编译通过:false 时**只报形式类**(缩进/空格/旧写法),不报"没用到/死代码"这种语义类的 ——
        /// 文件都编不过的时候,语义类的判断本来就不可靠。
        /// </summary>
        public static List<诊断项C> 检查(string 源码, bool 编译通过 = true)
        {
            var 结果 = new List<诊断项C>();
            if (string.IsNullOrEmpty(源码)) return 结果;
            string[] 行表 = 源码.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var 是代码行 = new bool[行表.Length];
            for (int i = 0; i < 行表.Length; i++) 是代码行[i] = 有代码(行表[i]);

            // ---- 绿:行尾多余空格 / Tab 与空格混用 ----
            for (int i = 0; i < 行表.Length; i++)
            {
                string 行 = 行表[i];
                int 尾 = 行.Length;
                while (尾 > 0 && (行[尾 - 1] == ' ' || 行[尾 - 1] == '\t')) 尾--;
                if (尾 < 行.Length && 尾 > 0)
                    结果.Add(new 诊断项C { 行 = i + 1, 起列 = 尾, 止列 = 行.Length, 级别 = 诊断级别.提示, 规则 = "行尾空格", 文本 = "行尾有多余的空格/制表符(不可见,但不规范)" });
                if (尾 > 0)
                {
                    string 头 = 行[..尾];
                    int j = 0; bool 有空格 = false, 有Tab = false;
                    while (j < 头.Length && (头[j] == ' ' || 头[j] == '\t')) { if (头[j] == ' ') 有空格 = true; else 有Tab = true; j++; }
                    if (有空格 && 有Tab)
                        结果.Add(new 诊断项C { 行 = i + 1, 起列 = 0, 止列 = j, 级别 = 诊断级别.提示, 规则 = "缩进混用", 文本 = "缩进里 Tab 和空格混合使用了,建议只用一种(推荐 4 个空格)" });
                }
            }

            // ---- 绿:还在用 测试输出() ----
            for (int i = 0; i < 行表.Length; i++)
            {
                int 位置 = 找代码里(行表[i], "测试输出");
                if (位置 < 0) continue;
                结果.Add(new 诊断项C
                {
                    行 = i + 1, 起列 = 位置, 止列 = 位置 + 4, 级别 = 诊断级别.提示, 规则 = "旧写法",
                    文本 = "建议改用 输出行(值) / 输出(值):输出行 输出数字并换行,输出 不换行,也可直接输出字符串(输出行(\"文字\"))",
                });
            }

            // ---- 黄:窄类型的循环计数器会绕回(短整数到 65535 再加就回 0,循环转不停) ----
            for (int i = 0; i < 行表.Length; i++)
            {
                var m = 窄计数器正则.Match(行表[i]);
                if (!m.Success) continue;
                string 类型词 = m.Groups["类型"].Value;
                if (!uint.TryParse(m.Groups["上界"].Value, out uint 上界)) continue;
                var 类型定义 = 类型C.解析(类型词);
                int 宽 = 类型定义?.宽度 ?? 0;
                if (宽 <= 0 || 宽 >= 类型C.字宽度) continue;
                long 上限值 = 类型定义?.最大值 ?? 类型C.最大值(宽);
                if (上界 <= (uint)Math.Min(上限值, uint.MaxValue)) continue;
                var 名组 = m.Groups["名"];
                结果.Add(new 诊断项C
                {
                    行 = i + 1, 起列 = m.Groups["类型"].Index, 止列 = m.Groups["类型"].Index + 类型词.Length,
                    级别 = 诊断级别.警告, 规则 = "计数器会绕回",
                    文本 = $"{类型词} 的取值范围上限为 {上限值};{名组.Value} 数到 {(上限值 >= 0 ? 上限值 : 上限值)} 后将发生回绕,该循环无法结束;" +
                           $"要数这么多,计数器改成 `var {名组.Value} = 0`(32 位有符号),或者用范围够大的无符号类型",
                });
            }

            if (!编译通过) { 排序(结果); return 结果; }

            // ---- 黄:恒真恒假的 如果/当 ----
            for (int i = 0; i < 行表.Length; i++)
            {
                var m = 条件正则.Match(行表[i]);
                if (!m.Success) continue;
                string 值 = m.Groups["值"].Value;
                bool 恒真 = 值 is "真" or "true" || (uint.TryParse(值, out uint n) && n != 0);
                结果.Add(new 诊断项C
                {
                    行 = i + 1, 起列 = m.Groups["值"].Index, 止列 = m.Groups["值"].Index + 值.Length,
                    级别 = 诊断级别.警告, 规则 = "常量条件",
                    文本 = 恒真 ? "条件为常量,恒为真:该判断无实际作用,可能写错了变量,或该段代码可以删除"
                                 : "条件是个常量,永远为假 —— 这一段永远不会执行",
                });
            }

            // ---- 黄:空循环体(循环头那一行以 { 结尾,下一行就是 }) ----
            for (int i = 0; i + 1 < 行表.Length; i++)
            {
                if (!循环头正则.IsMatch(行表[i]) || !行表[i].TrimEnd().EndsWith('{')) continue;
                if (行表[i + 1].Trim() != "}") continue;
                结果.Add(new 诊断项C
                {
                    行 = i + 1, 起列 = 0, 止列 = 行表[i].TrimEnd().Length, 级别 = 诊断级别.警告, 规则 = "空循环",
                    文本 = "循环体为空:尚未编写,或编写完成后未删除",
                });
            }

            // ---- 黄:停止 之后的死代码 ----
            int 深度 = 0;
            for (int i = 0; i < 行表.Length; i++)
            {
                if (!是代码行[i]) continue;
                string 干 = 代码解析器.去注释(行表[i]).Trim();
                if (干.StartsWith('}')) 深度--;
                if (干 is "停止" or "HALT" or "halt" or "停止;" or "HALT;" or "halt;")
                {
                    for (int j = i + 1; j < 行表.Length; j++)
                    {
                        if (!是代码行[j]) continue;
                        string 后 = 代码解析器.去注释(行表[j]).Trim();
                        if (后.StartsWith('}')) break;                       // 出了这一层块:可能还有别的入口,不报
                        if (后.EndsWith(':') || 后.StartsWith(后.TrimEnd(':') + ":")) continue;   // 标签:是个入口,不报
                        结果.Add(new 诊断项C
                        {
                            行 = j + 1, 起列 = 0, 止列 = 行表[j].TrimEnd().Length, 级别 = 诊断级别.警告, 规则 = "死代码",
                            文本 = "此前已执行 停止,该语句不会被执行",
                        });
                        break;
                    }
                }
                int 开 = 0, 闭 = 0;
                foreach (char c in 代码解析器.去注释(行表[i])) { if (c == '{') 开++; else if (c == '}') 闭++; }
                深度 += 开 - 闭;
            }

            // ---- 黄:声明了但没用到(同文件里再没出现过这个名字) ----
            // **H28**:去注释后的整词计数**全文只算一遍**(惰性:真出现声明才算)
            // —— 以前是"每个声明都把整份文件用逐名字的正则扫一遍",代价 ≈ 声明数 × 行数。
            Dictionary<string, int>? 词次数 = null;
            for (int i = 0; i < 行表.Length; i++)
            {
                var m = 声明正则.Match(行表[i]);
                if (!m.Success) continue;
                string 名 = m.Groups[1].Value;
                if (名.Length == 0) continue;
                if (在注释里(行表[i], m.Groups[1].Index)) continue;
                词次数 ??= 统计整词(行表);
                if (词次数.TryGetValue(名, out int 次) && 次 > 1) continue;   // 别处出现过:用到了
                // 名字本身就带下划线开头这种"故意不用"的写法就不报(留个后门)
                if (名.StartsWith('_')) continue;
                结果.Add(new 诊断项C
                {
                    行 = i + 1, 起列 = m.Groups[1].Index, 止列 = m.Groups[1].Index + 名.Length,
                    级别 = 诊断级别.警告, 规则 = "未使用",
                    文本 = $"\'{名}\' 已声明,但整个文件中未再使用;如无用途请删除,以免误认为仍需保留",
                });
            }

            排序(结果);
            return 结果;
        }

        /// <summary> 这一行算不算"代码行"(非空、非纯注释) </summary>
        private static bool 有代码(string 行) => 代码解析器.去注释(行).Trim().Length > 0;

        /// <summary>
        /// **T29(2026-09-27 评测整改)**:在"代码段"(不在注释、不在字符串/字符字面量里)找这个词;找不到给 -1。
        ///
        /// 以前这里是**自己写的一小套扫描**(只认 `"`、只认 `//`)—— 于是:
        ///   · `字符 引号 = '"'; // …` 里那个 `"` 把"在串里"带反了,后面真实的 `测试输出(…)` **漏检**;
        ///   · `/* */` 块注释完全不认。
        /// 现在把整份文本交给 `代码解析器.遮蔽区间`(注释 ∪ 字面量 → 空格,下标与行数不变),
        /// 再在遮蔽后的文本里 `IndexOf` —— 那就是"唯一事实源"。
        /// </summary>
        private static int 找代码里(string 行, string 词)
        {
            string 干 = 代码解析器.遮蔽区间(行);
            return 干.IndexOf(词, StringComparison.Ordinal);
        }

        /// <summary> 这一列是不是落在注释里(口径同 `代码解析器.取注释区`) </summary>
        private static bool 在注释里(string 行, int 列)
        {
            foreach (var (开始, 结束) in 代码解析器.取注释区(行))
                if (列 >= 开始 && 列 < 结束) return true;
            return false;
        }

        private static void 排序(List<诊断项C> 表) =>
            表.Sort((a, b) => a.行 != b.行 ? a.行.CompareTo(b.行) : a.起列.CompareTo(b.起列));
    }
}
