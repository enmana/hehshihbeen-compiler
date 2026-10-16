using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary> 中间层展开器的一部分:规范化与错误报告(源码预处理 / 报错构造)。(第88批从 `中间层C.cs` 按 region 原样搬出,逻辑一字未改) </summary>
    internal partial class 中间层展开器
    {
        #region 规范化与错误报告(源码预处理/报错构造)
        // ---------------- 规范化 ----------------
        private void 规范化(string 源代码) => 规范化文本(源代码, _基目录, 0, "");

        /// <summary>
        /// 认文件**第一条**有效行是不是 `模块 名字`(英文 module)。认出来就登记模块、返回它的名字与行号;
        /// 不是模块文件返回 null(那就是老写法:名字全全局,语义一个字不变)。
        /// 同一份代码里模块名重复 → 当场报错(包含两次同一个文件不算:那条路上按路径去过重)。
        /// </summary>
        private (string 名, int 行号, int 行下标)? 认模块头(string[] 原行表, string 来源)
        {
            for (int i = 0; i < 原行表.Length; i++)
            {
                string 行 = 原行表[i].Trim();
                if (行.Length == 0) continue;
                string 首词 = 取首词(行);
                if (中间层C.到英文关键字(首词) != "module") return null;
                string 模块名 = 行[首词.Length..].Trim().TrimEnd(';').Trim();
                int 行号 = i + 1;
                if (!标识符全词正则.IsMatch(模块名))
                    throw 报错(行号, $"模块名非法: '{模块名}'(写法是:模块 数学 —— 只要一个名字)", $"invalid module name: '{模块名}'");
                if (中间层C.到英文关键字(模块名) != null || 类型C.是类型名(模块名) || 指令C.取指令码表(模块名) != null)
                    throw 报错(行号, $"模块名不能与关键字/类型/指令重名: '{模块名}'", $"module name conflicts with a keyword/type/instruction: '{模块名}'");
                if (_模块表.TryGetValue(模块名, out var 旧))
                {
                    string 在哪 = 旧.来源.Length > 0 ? $"{Path.GetFileName(旧.来源)} 第 {旧.行号} 行" : $"第 {旧.行号} 行";
                    throw 报错(行号, $"模块 '{模块名}' 重复定义:{在哪}已经有一个同名的模块。" +
                        $"同一份程序里模块名要唯一(两个库想都用,就把其中一个改个名)", $"duplicate module: '{模块名}'");
                }
                _模块表[模块名] = (行号, 来源);
                return (模块名, 行号, i);
            }
            return null;
        }

        /// <summary>
        /// 规范化一段文本;遇到 包含 "路径" 时按基目录读入并就地拼接(同一文件只包含一次)。
        ///
        /// **2026-09 起:HB 语言语句以 `;` 结尾**(用户拍板:行尾有结束符才看得出一行写没写完整)。
        /// 合句规则(伪行表里存的是**去掉 `;`** 之后的一条语句):
        ///   · 语句末尾的 `;` 去掉;想分多行写,就让上一行以"续行符"收尾
        ///     (`=` `+ - * / % &amp; | ^ &lt;&lt; &gt;&gt; &amp;&amp; || == != &lt; &gt; &lt;= &gt;=` `,` `(` `[` `?` `:` `.`)
        ///   · **块头**(`如果 (…)` / `当 (…)` / `函数 …` / `情况 1` …后面紧跟 `{`)不写 `;`
        ///   · 单独的 `{` `}` 行原样;`包含 "路径"` 不写 `;`
        ///   · **内联汇编行**(首词是指令表里的指令,原样透传给汇编器)不写 `;`(写了也认,帮你去掉)
        ///   · 标签行(`名字:`)不写 `;`
        /// 这样"上一句忘了 `;`"会当场报出来,而不会稀里糊涂跟下一句拼成一句。
        /// </summary>
        private void 规范化文本(string 源代码, string? 基目录, int 包含深度, string 来源)
        {
            // R3-2:嵌太深的检查**挪进了 `尝试包含`** —— 只有那里手上有"那一句 `包含`"的行号,
            // 报出来才带行号;从这里抛的裸 Exception 以前显示成"内部错误(请回报)"(详见 `尝试包含`)。
            // 注释统一为 // 与 /* */;先整体去掉注释(保留换行)
            // T15(2026-09-27 评测整改):**没闭合的 `/*` 当场拦住** —— 以前 `去注释` 会把注释区拉到文本末尾,
            // 文件里剩下的内容无声消失、退出码 0。判据不在 `去注释` 里做(它还要给高亮/折叠用,不能抛)。
            int 没闭合行 = 代码解析器.找没闭合的块注释(源代码);
            if (没闭合行 > 0)
                throw 报错(没闭合行, "块注释 `/*` 没有收尾:后面找不到配对的 `*/`,文件剩下的内容会被整段当成注释" +
                    " —— 补上 `*/`,或者这段本来就不该是注释就改用 `//`",
                    "unterminated block comment: add the matching */ (or use //)");
            源代码 = 分词器C.去注释(源代码);
            string[] 原行表 = 源代码.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // ⓪ 文件第一条可以是 `模块 名`:这个文件里的声明都带上它的前缀(包含进来的文件各算各的)
            string 上层模块 = _本文件模块;
            // 这一份文本**真正**是哪份文件(见 `_当前来源文件`):`包含` 进来时 `来源` 就是库文件的全路径,
            // 工程级展开时是文件表里那个名字,单文件编译没有名字(空串)= 跟 `_当前文件`(也是空)同值 ⇒ 一处都不变。
            string 上层来源文件 = _当前来源文件;
            var 模块头 = 认模块头(原行表, 来源);
            _本文件模块 = 模块头?.名 ?? "";
            _当前来源文件 = string.IsNullOrEmpty(来源) ? _当前文件 : 来源;
            if (模块头 is { } 头) 原行表[头.行下标] = "";     // 头行本身不参与后面的流水线
            try
            {

            // ① 切片段:`{` `}` 各自成一片,其余按物理行切(字符串/字符字面量里的花括号不算)
            var 片段表 = 切片段(原行表);

            // ② 合句:语句拼到 `;` 收尾为止;上一片以"续行符"结尾才往下续
            //    (所以 `如果 (条件 >` / `0)` / `{` 这种"条件分两行写"也能认:缓冲出来是个块头,不写 `;`)
            string 缓冲 = "";
            int 起行 = 0;
            for (int k = 0; k < 片段表.Count; k++)
            {
                var (片段, _, 行, 花括号) = 片段表[k];
                string 内容 = 片段.Trim();
                if (花括号)
                {
                    // `… = {` 是 2026-10 之前的老写法(花括号初值已撤):当场指到方括号,
                    // 不让它落到"少一个分号"上 —— 那句报错指的方向是错的
                    if (内容 == "{" && 缓冲.Length > 0 && 缓冲.TrimEnd().EndsWith('='))
                        throw 报错(行, "初值一律用**方括号**写成一串常量:`[1, 2, 3]`;" +
                            "**`{ }` 只用于语句块**(方法体 / 条件 / 循环 / 容器体),两处不混用;" +
                            $"把 `{{1, 2, 3}}` 改成 `[1, 2, 3]`",
                            "initializers must use [...]; braces are for statement blocks");
                    if (缓冲.Length > 0) throw 缺分号(起行, 缓冲, 单行: 起行 == 行);
                    收伪行(片段, 行);
                    continue;
                }
                if (内容.Length == 0) continue;
                if (缓冲.Length == 0)
                {
                    if (尝试包含(内容, 基目录, 行, 包含深度)) continue;
                    起行 = 行;
                    // H68-2:枚举声明(`枚举 颜色 { 赤, 绿, 蓝 }`)的 `{ }` 是**成员表**,不是语句块 ——
                    // 整条收成一个伪行。不收的话成员会被当成语句(`赤, 绿, 蓝` 甚至会被当内联汇编放过去,
                    // 带 `= 3` 的成员则报"少一个分号"),用户看到的错全指错方向。
                    if (是枚举起头(内容) && 下一个是花括号(片段表, k) && 收枚举整行(片段表, ref k, 内容, out string 枚举整行))
                    {
                        收伪行(枚举整行, 起行);
                        continue;
                    }
                }
                // 正在续行、下一片却是"一条汇编/标签"→ 说明上一句确实没写完(汇编行不可能是表达式的操作数)
                else if (是内联汇编行(内容) || 是标签行(内容)) throw 缺分号(起行, 缓冲, 单行: 起行 == 行);
                缓冲 = 缓冲.Length == 0 ? 内容 : 缓冲 + " " + 内容;

                if (以分号结尾(内容)) { 收伪行(去尾分号(缓冲), 起行); 缓冲 = ""; continue; }
                // 块头(条件分几行写也算)
                if (下一个是花括号(片段表, k) && !内容.TrimEnd().EndsWith('='))
                {
                    收伪行(缓冲, 起行);
                    缓冲 = "";
                    continue;
                }
                if (缓冲 == 内容 && (是内联汇编行(内容) || 是标签行(内容)))
                {
                    收伪行(内容, 行);
                    缓冲 = "";
                    continue;
                }
                if (!以续行符结尾(内容)) throw 缺分号(起行, 缓冲, 单行: 缓冲 == 内容);
            }
            if (缓冲.Length > 0) throw 缺分号(起行, 缓冲, 单行: false);
            }
            finally
            {
                _本文件模块 = 上层模块;      // 回到包含它的那个文件的模块(递归 包含 时才对得上)
                _当前来源文件 = 上层来源文件;  // 同上:回到包含它的那一份(见 `_当前来源文件`)
            }
        }

        /// <summary> 缺 `;` 的说明报错(单行 = 这一句本来就没打算分几行写)</summary>
        private 编译异常C 缺分号(int 行, string 内容, bool 单行) => 报错(行, 单行
            ? $"第 {行} 行这句没写完(结尾少一个 ';'):'{内容}'。语言语句都要以 ';' 结尾" +
              $"(块头、`{{` `}}`、内联汇编行不用);要分多行写,就让上一行以 = 或运算符/逗号收尾"
            : $"第 {行} 行开始的这句一直没等到 ';':'{内容}'。分多行写的时候,最后一行不要忘记用 ';' 收尾",
            单行 ? $"line {行} is missing the trailing ';': '{内容}'" : $"statement starting at line {行} never got its ';': '{内容}'");

        /// <summary> H68-2:这一句是不是**枚举声明头**(`枚举 名` / `enum 名`,后面还带着名字)</summary>
        internal static bool 是枚举起头(string 内容)
        {
            string t = 内容.Trim();
            string 首词 = 取首词(t);
            return 中间层指令C.到英文关键字(首词) == "enum" && t.Length > 首词.Length;
        }

        /// <summary>
        /// H68-2:把 `枚举 名 { 成员, 成员 }` 这一整条**收成一个伪行**(`k` 前进到那个 `}` 上)。
        /// 收不齐(没找到 `}`)返回 false —— 原样让它走老路,报错还是老那几句(不会把后面的源码整个吞掉)。
        /// </summary>
        private static bool 收枚举整行(List<(string 文本, int 起始, int 行, bool 花括号)> 片段表, ref int k, string 头, out string 整行)
        {
            string 拼 = 头;
            for (int j = k + 1; j < 片段表.Count; j++)
            {
                拼 += 片段表[j].文本;
                if (片段表[j].花括号 && 片段表[j].文本.Trim() == "}")
                {
                    整行 = 拼;
                    k = j;
                    return true;
                }
            }
            整行 = "";
            return false;
        }

        /// <summary> 把源码切成片段:`{` `}` 各自一片,其余按物理行切(带起始下标与源行号;字符串里的花括号不算)</summary>
        internal static List<(string 文本, int 起始, int 行, bool 花括号)> 切片段(string[] 原行表)
        {
            var 片段 = new List<(string, int, int, bool)>();
            for (int i = 0; i < 原行表.Length; i++)
            {
                string 行 = 原行表[i];
                int 起 = 0;
                while (起 < 行.Length)
                {
                    int 括号 = 找结构花括号(行, 起);
                    if (括号 < 0) { 片段.Add((行[起..], 起, i + 1, false)); break; }
                    if (括号 > 起) 片段.Add((行[起..括号], 起, i + 1, false));
                    片段.Add((行[括号].ToString(), 括号, i + 1, true));
                    起 = 括号 + 1;
                }
            }
            return 片段;
        }

        /// <summary> 从 起 开始找第一个"结构花括号"(跳过字符串/字符字面量里的);没有返回 -1 </summary>
        internal static int 找结构花括号(string 行, int 起)
        {
            bool 双 = false, 单 = false;
            for (int i = 起; i < 行.Length; i++)
            {
                char c = 行[i];
                if (c == '\\' && (双 || 单)) { i++; continue; }
                if (!单 && c == '"') { 双 = !双; continue; }
                if (!双 && c == '\'') { 单 = !单; continue; }
                if (双 || 单) continue;
                if (c == '{' || c == '}') return i;
            }
            return -1;
        }

        /// <summary> 这一片是不是以 `;` 结尾(字符串/字符里的 `;` 不算)</summary>
        internal static bool 以分号结尾(string 文本)
        {
            int 最后 = 最后一个有效字符位置(文本);
            return 最后 >= 0 && 文本[最后] == ';';
        }

        /// <summary> 这一片是不是以"续行符"结尾(= 运算符 逗号 开括号 ? : .)—— 是就说明要接着下一行写</summary>
        internal static bool 以续行符结尾(string 文本)
        {
            string t = 文本.TrimEnd();
            if (t.EndsWith("++") || t.EndsWith("--")) return false;   // `i++` / `i--` 是一句完整的话,不是"算到一半"
            int 最后 = 最后一个有效字符位置(文本);
            if (最后 < 0) return false;
            return "=+-*/%&|^<>,([?:.".Contains(文本[最后]);
        }

        /// <summary> 去掉末尾的 `;`(可以连着好几个)</summary>
        internal static string 去尾分号(string 文本)
        {
            string 结果 = 文本.TrimEnd();
            while (结果.EndsWith(';')) 结果 = 结果[..^1].TrimEnd();
            return 结果;
        }

        /// <summary>
        /// 最后一个非空白字符的下标(分号/续行符都看它)。
        /// 注意**不要**跳过字符串/字符字面量里的字符:`字符 字母 = 'A'` 的最后一个字符是收尾的 `'`,
        /// 不然会看成"以 = 结尾"→ 被当成算到一半(踩过:整批示例的常量初值都漏了 `;`)。
        /// </summary>
        private static int 最后一个有效字符位置(string 文本)
        {
            for (int i = 文本.Length - 1; i >= 0; i--)
                if (!char.IsWhiteSpace(文本[i])) return i;
            return -1;
        }

        /// <summary> 下一个非空片段是不是 `{`(块头的判据:块头后面必跟 `{`)</summary>
        internal static bool 下一个是花括号(List<(string 文本, int 起始, int 行, bool 花括号)> 片段表, int k)
        {
            // 必须**跳过纯空白片段**:缩进写的 `    {` 在切片段时会先切出一个空白片段,
            // 若直接看 片段表[k+1] 就会被它挡住 → 块头被误判成"没写 ';'"(踩过)
            for (int i = k + 1; i < 片段表.Count; i++)
            {
                if (片段表[i].文本.Trim().Length == 0) continue;
                return 片段表[i].花括号 && 片段表[i].文本.Trim() == "{";
            }
            return false;
        }

        /// <summary> 标签行:`名字:`(没有 `?` 的那种 —— `条件 ? 真支 : 假支` 里的 `:` 不算)</summary>
        internal static bool 是标签行(string 内容)
        {
            string t = 内容.TrimEnd();
            if (!t.EndsWith(':')) return false;
            if (t.Contains('?')) return false;
            string 首词 = 取首词(t);
            return 首词.Length > 0 && t.TrimEnd(':').Trim() == 首词;
        }

        /// <summary>
        /// 内联汇编行(原样透传给汇编器,不需要 `;`)。判据:
        ///   · 首词是指令表里的指令(但 `返回 值` 是语言语句、语言关键字如 停止/输出/输出行 也算语言语句);
        ///   · `R0 = 0x12345678` 这种**寄存器赋值**:汇编器本来就认(老示例全靠它),照旧当汇编行;
        ///   · 首词谁都不认识、又不带 `=`、也不是 `…)` 收尾的(比如拼错的 `飞天猫 R0, R1`)→ 也透传,
        ///     让汇编器去报它那句"未知指令"(比先说"缺 ';'"更贴近真正的问题)。
        /// </summary>
        internal static bool 是内联汇编行(string 内容)
        {
            string t = 内容.Trim();
            string 首词 = 取首词(t);
            if (首词.Length == 0) return false;
            if (指令C.取指令码表(首词) != null)
            {
                // `返回` 是"两张皮":**裸写**(整行只有这一个词)是汇编 RET(老示例的子程序全靠它),
                // 后面跟了东西(`返回 值`)才是语言语句。这条判定必须放在下面的"语言关键字优先"**前面**
                // —— `返回` 现在也在 关键字表 里(为了中英切换),先看关键字表会把它一律当成语言语句,
                // 子程序末尾那行裸 `返回` 就要报"缺 ';'"了(实测踩到)。
                if (首词 is "返回" || string.Equals(首词, "RET", StringComparison.OrdinalIgnoreCase))
                    return t == 首词;
                if (中间层指令C.到英文关键字(首词) != null) return false;
                return true;
            }
            // 首词不认识:寄存器赋值(`R2 = 0` / `R0 = 0x1234`)与"完全不像语言语句"的行都算汇编
            if (寄存器C.解析(首词, out _) && t.Contains('=')) return true;
            if (t.Contains('=') || t.EndsWith(')') || t.EndsWith(']') || t.EndsWith('}')) return false;
            return true;
        }

        /// <summary> `包含 "路径"` / `import "路径"` 行:编译器指令,不写 `;` </summary>
        internal static bool 是包含行(string 内容) => 中间层指令C.到英文关键字(取首词(内容)) == "import";

        /// <summary>
        /// 批 10(D3):**真·汇编指令行** —— 首词是指令表里的指令(`返回` 那两张皮:裸写才是汇编 RET)。
        /// 与 `是内联汇编行` 只差一处:后者把 `R0 = 42` 这类"寄存器赋值"也算汇编(那是老口子),
        /// 语言层退场之后要把这两类分开 —— 见 `检查没有寄存器`。
        /// </summary>
        internal static bool 是汇编指令行(string 内容)
        {
            string t = 内容.Trim();
            string 首词 = 取首词(t);
            if (首词.Length == 0 || 指令C.取指令码表(首词) == null) return false;
            if (首词 is "返回" || string.Equals(首词, "RET", StringComparison.OrdinalIgnoreCase)) return t == 首词;
            if (中间层指令C.到英文关键字(首词) != null) return false;   // 语言关键字优先(与 是内联汇编行 同一套)
            return true;
        }

        /// <summary>
        /// 批 11(D4):这一行是不是"**像调用**"的形状(`名字(…)` / `名.方法(…)`)?是就返回那个名字 ——
        /// 给"未定义的函数/方法"报错用:退场之后隐式调用是**唯一**的调用写法,名字拼错不能退化成汇编器的
        /// "未知指令"(那会把人往"指令拼错了"的方向带)。不像就返回 null(纯汇编行/赋值/声明/块头都从这里让开)。
        /// </summary>
        internal static string? 取调用形状名(string 内容)
        {
            string t = 内容.Trim().TrimEnd(';').Trim();
            int 左 = t.IndexOf('(');
            if (左 <= 0 || !t.EndsWith(')')) return null;
            string 名 = t[..左].Trim();
            return 标识符带点正则.IsMatch(名) ? 名 : null;
        }

        /// <summary>
        /// 批 10(D3):**单独成句**的寄存器赋值/自增(`R0 = …` / `R2++` / `R5--`)。
        /// 这类行照旧当**内联汇编**放行(与 `加法 R0, R2` / `加载立即数 R1, 5` 同一档):
        /// 语言里不许用寄存器,指的是"`如果 (R0 == 5)` / `循环 (R4 = 0; …)` / `测试输出(R0)` / `r = R0 * n`
        /// 这种**把寄存器当语言的值/变量**用",不是把内联汇编也一起取消。
        /// </summary>
        internal static bool 是寄存器赋值句(string 内容) => 寄存器赋值句正则.IsMatch(内容);

        private static readonly Regex 寄存器赋值句正则 = new(@"^\s*[rR](1[0-5]|[0-9])\s*(=|\+\+|--)", RegexOptions.Compiled);
        private static readonly Regex 寄存器词正则 =
            new(@"(?<![\p{L}\p{N}_.])[rR](1[0-5]|[0-9])(?![\p{L}\p{N}_])", RegexOptions.Compiled);

        /// <summary>
        /// 批 10(D3):**语言结构里出现 R0~R15 就报错**(写、读都算),并给一行改法。
        /// 内联汇编行不在此列(调用处已经把它们让开)。报错文案是用户可见文档的一部分:
        /// 三条改法分别对应"要返回值 / 要间接存取 / 真要手写指令"这三种真实来意。
        /// </summary>
        private void 检查没有寄存器(string 内容, int 行号)
        {
            var 串区 = 代码解析器.取字符串区(内容);
            foreach (Match 命中 in 寄存器词正则.Matches(内容))
            {
                if (串区.Any(r => 命中.Index >= r.开始 && 命中.Index < r.结束)) continue;   // 字符串字面量里的不算
                string 名 = 命中.Value.ToUpperInvariant();
                throw 报错(行号,
                    $"语言里不再用寄存器 {名} 了(寄存器只在汇编层出现)。改法:" +
                    $"①要函数/方法的返回值,直接写进表达式 —— `整数 果 = 名字(…);`(不用先 `调用` 再读 {名});" +
                    $"②要读写别的变量的存储,用 `取字(地址, 偏移)` / `存字(地址, 偏移, 值)`;" +
                    $"③真要手写指令,就把这一行写成汇编指令(例如 `加载立即数 {名}, 5`),单独成句的 `{名} = …` 也照样当汇编",
                    $"registers are assembly-level only: use a variable, `取字/存字`, or a real assembly line ({名})");
            }
        }


        /// <summary> 进伪行表(字面量替换 + 丢掉空行);顺手记住"这一行属于哪个模块"</summary>
        private void 收伪行(string 内容, int 行)
        {
            string 原文 = 内容.Trim();
            内容 = 替换字面量(原文, 行);
            if (内容.Length == 0) return;
            _伪行表.Add(内容);
            _伪行原文.Add(原文);          // 与伪行表一一对应:拼接到 要靠它认"字符字面量"(替换后 'A' 就成 65 了)
            _伪行号表.Add(行);
            _伪行模块表.Add(_本文件模块);
            _伪行来自包含.Add(_正在包含文件);
            _伪行文件表.Add(_当前文件号);      // 工程级展开:这条语句来自哪个文件(报错与调试段都要用)
            _伪行来源表.Add(_当前来源文件);    // 这条语句**真正**来自哪一份文件(被 包含 时是库文件;见 `_当前来源文件`)
        }

        /// <summary> 是否 包含/import 行;是则读入被包含文件并就地展开,返回 true </summary>
        private bool 尝试包含(string 内容, string? 基目录, int 行号, int 包含深度)
        {
            string 首词 = 取首词(内容);
            if (中间层指令C.到英文关键字(首词) != "import") return false;

            string 余下 = 内容[首词.Length..].Trim();
            if (余下.Length < 2 || 余下[0] != '"' || !余下.EndsWith('"'))
                throw 报错(行号, $"包含 需要写成:包含 \"路径\" (当前: {内容})", $"import expects a quoted path: '{内容}'");

            string 路径 = 余下[1..^1].Trim();
            // H64:被包含的文件可能在**系统目录那一档**(虚拟路径)里 —— 解析与读盘都走 `虚拟文件C`;
            // 真路径那一支与老代码逐字一样(先归绝对路径再展开)
            bool 虚拟 = 虚拟路径C.是虚拟(基目录) || 虚拟路径C.是虚拟(路径);
            string 全路径 = 虚拟
                ? 虚拟路径C.收正(虚拟路径C.拼接(基目录 ?? "", 路径))
                : Path.IsPathRooted(路径)
                    ? Path.GetFullPath(路径)
                    : Path.GetFullPath(Path.Combine(基目录 ?? Environment.CurrentDirectory, 路径));

            if (!_已包含文件.Add(全路径)) return true;   // 同一文件只展开一次
            if (!虚拟文件C.存在(全路径))
                throw 报错(行号, $"找不到包含文件: {路径}", $"included file not found: {路径}");

            // R3-2:嵌太深。**在这里查**(不在 `规范化文本` 的入口查):只有这里手上有"那一句 `包含`"的行号。
            // 以前从入口抛裸 `Exception`,用户看到的是一句"内部错误(请回报)",而且那句"可能是循环包含"**是错的** ——
            // 上面 `_已包含文件` 保证同一份文件只展开一次,循环包含根本递归不下去。
            if (包含深度 + 1 > 硬上限C.包含深度上限)
                throw 报错(行号, $"包含 嵌得太深了:这一句下去就是第 {包含深度 + 1} 层,上限 {硬上限C.包含深度上限} 层。" +
                    $"办法:把中间那些只转发一句 包含 的文件合并,或者让上层直接 包含 最里面那个文件(摊平)",
                    $"import nesting too deep: {包含深度 + 1} > {硬上限C.包含深度上限}");

            bool 原来是包含 = _正在包含文件;
            _正在包含文件 = true;             // 被包含文件的行号从 1 数起,要跟主文件区分开(见 发出)
            try { 规范化文本(文本落盘C.读源文件(全路径), 虚拟路径C.所在目录(全路径), 包含深度 + 1, 全路径); }
            finally { _正在包含文件 = 原来是包含; }
            return true;
        }

        /// <summary>
        /// 字面量预处理:字符字面量 'A'/'\n'/'\x41' → 十进制码;逻辑 真/假(中英) → 1/0。
        /// **字符串字面量 "…" 里的内容一个字都不动** —— 里面写"真"就该打出"真",不能变成 1。
        ///
        /// R2-5(2026-09-21):转义**不再在这里手抄一份**,统一吃 `转义表C`(唯一事实源)——
        /// 以前这张表少了 `b f v a`,于是字符串报错文案承诺能用的 `'\b'` 在这里编不过(实测)。
        /// </summary>
        private string 替换字面量(string 文本, int 行号)
        {
            var 出 = new StringBuilder(文本.Length);
            var 外 = new StringBuilder();       // 引号外的普通文本:缓冲一段替换一次

            void 冲外()
            {
                if (外.Length == 0) return;
                出.Append(替换真假(外.ToString()));
                外.Clear();
            }

            int i = 0;
            while (i < 文本.Length)
            {
                char c = 文本[i];

                if (c == '"')                                    // 字符串:整段原样搬过去
                {
                    冲外();
                    int 起 = i++;
                    while (i < 文本.Length && 文本[i] != '"')
                    {
                        if (文本[i] == '\\' && i + 1 < 文本.Length) i++;
                        i++;
                    }
                    if (i < 文本.Length) i++;                     // 收尾的那个 "
                    出.Append(文本, 起, i - 起);
                    continue;
                }

                if (c == '\'')                                   // 字符字面量 → 数值
                {
                    int 收尾 = 找单引号收尾(文本, i + 1);
                    if (收尾 > i)
                    {
                        string 内 = 文本[(i + 1)..收尾];
                        if (转义表C.解字符内文(内, out string 说明) is { } 码)
                        {
                            外.Append(码.ToString());
                            i = 收尾 + 1;
                            continue;
                        }
                        // 看着就是字符字面量(短,或者带反斜杠)却解不出 ⇒ 当场说清楚是哪个转义不认识。
                        // 不拦的话它会一路飘到"表达式里有无法识别的字符 '''",用户根本看不出是转义写错
                        // ——R2-5 开工实测:`'\b'` 报的就是这一句(而报错文案还承诺 `\b` 可用)。
                        if (内.Length <= 2 || 内[0] == '\\')
                            throw 报错(行号, $"字符字面量 {文本[i..(收尾 + 1)]} 不合法:{说明}",
                                $"bad character literal: {文本[i..(收尾 + 1)]}");
                    }
                }

                外.Append(c);
                i++;
            }
            冲外();
            return 出.ToString();
        }

        /// <summary> 从 `起` 找字符字面量收尾的那个 `'`(`\'` 要跳过);找不到给 -1。 </summary>
        private static int 找单引号收尾(string 文本, int 起)
        {
            for (int j = 起; j < 文本.Length; j++)
            {
                if (文本[j] == '\\' && j + 1 < 文本.Length) { j++; continue; }
                if (文本[j] == '\'') return j;
                // 字符字面量不会跨行:一行里碰不到收尾就当它不是一个字面量,别把后面的代码全吃进来
                if (文本[j] == '\n') return -1;
            }
            return -1;
        }

        /// <summary> 引号外的 真/假/true/false → 1/0(整词替换,不动变量名里的子串) </summary>
        private static string 替换真假(string 文本)
        {
            文本 = 替换整词(文本, "真", "1");
            文本 = 替换整词(文本, "假", "0");
            文本 = 替换整词(文本, "true", "1");
            文本 = 替换整词(文本, "false", "0");
            return 文本;
        }

        /// <summary> 只替换完整词(避免变量名里的子串被误换) </summary>
        private static string 替换整词(string 文本, string 旧词, string 新词) =>
            Regex.Replace(文本, $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(旧词)}(?![\p{{L}}\p{{N}}_])", 新词);

        private 编译异常C 报错(int 源行号, string 中文, string 英文)
        {
            var 错 = 报错静态(源行号, 中文, 英文);
            // 工程级展开:报错要带对文件 —— 带的是**真正出错那一份**(`包含` 进来的库文件算它自己那一份):
            // 行号本来就是库里那份文件的行号,写主文件的名字会让人找到主文件的同号行,指错方向(见 `_当前来源文件`)。
            return _当前来源文件.Length > 0
                ? new 编译异常C(错.中文, 错.英文, 错.行号, 错.源行, _当前来源文件)
                : 错;
        }

        /// <summary>
        /// 静态版 `报错`(格式**完全一致**):给"常量折叠"这类 static 方法用。
        /// 为什么要有它:`计算常量` 以前直接 new 一个不带前缀的编译异常 ⇒ 命令行只打出
        /// `编译错误: 常量除零: '7 / 0'`,**没有行号**;走这里就和别的报错一个面孔(`第N行`)。
        /// </summary>
        private static 编译异常C 报错静态(int 源行号, string 中文, string 英文) =>
            new($"中间层展开错误 第{源行号}行: {中文}", $"line {源行号}: {英文}", 源行号);
        #endregion
    }
}
