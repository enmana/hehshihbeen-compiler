using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary> 中间层展开器的一部分:控制台输入输出(`输出`/`输出行` + 读字符/行/数字 + 字符串字面量)。(第88批从 `中间层C.cs` 按 region 原样搬出,逻辑一字未改) </summary>
    internal partial class 中间层展开器
    {
        #region 控制台输入输出(`输出`/`输出行` + `读取字符`/`读取行`/`读取数字` + 字符串字面量)
        // 这一层是"给人用"的控制台;`测试输出()` 那个是给自检/调试用的(打数字 + 换行)。
        // 底下还是 ISA 的 IN(0x27)/OUT(0x28)/NEWLINE(0x29) —— **ISA 一个字都没改**,全是编译期展开。
        private readonly Dictionary<string, (变量槽定义 槽, int 字节数)> _字符串池 = new(StringComparer.Ordinal);
        private string _打印串例程 = "", _打印串到零例程 = "", _读行例程 = "", _读数字例程 = "";

        /// <summary> 从文本里找出第一个字符串字面量(返回引号内的原文;没有就 null)</summary>
        private static string? 找字符串字面量(string 文本)
        {
            int 起 = 文本.IndexOf('"');
            if (起 < 0) return null;
            for (int i = 起 + 1; i < 文本.Length; i++)
            {
                if (文本[i] == '\\') { i++; continue; }
                if (文本[i] == '"') return 文本[(起 + 1)..i];
            }
            return null;
        }

        /// <summary>
        /// `"文字"` → 真实字节(UTF-8)。转义按 C 的习惯:\\ \" \' \n \r \t \b \f \v \a \0 \xHH \uXXXX;
        /// 认不出来的转义直接给出明确错误(不悄悄放过去 —— 不然"看着像换行,打出来是个 n"最难查)。
        /// </summary>
        private byte[] 解码字符串(string 内文, int 行号)
        {
            // T7(2026-09-27 评测整改):**没有转义就整段一次编码**。
            // 以前逐 UTF-16 码元 `Encoding.UTF8.GetBytes([c])` —— 增补平面字符(emoji 等)在 .NET 里是
            // **代理对**,拆成两个孤立代理码各编成 EF BF BD(替换字符)⇒ `字符串 s = "😀";` 的
            // `s.长度()` 得 2、打印出两个"问号"、与 `"\xF0\x9F\x98\x80"` 比为假;而同一个字写成
            // **字符字面量** `'😀'` 却是对的(128512)—— 两套解码器口径不一致。
            // 文档承诺(`Sample\说明.md` / `指令集说明.md`)"长度与下标按文字(Unicode 码点)算",字面量这步得跟上。
            if (!内文.Contains('\\')) return Encoding.UTF8.GetBytes(内文);

            var 字节 = new List<byte>();
            for (int i = 0; i < 内文.Length; i++)
            {
                char c = 内文[i];
                if (c != '\\')
                {
                    // 代理对:高代理 + 低代理 = **一个文字**(一个码点)→ 一起编码,不许拆
                    if (char.IsHighSurrogate(c) && i + 1 < 内文.Length && char.IsLowSurrogate(内文[i + 1]))
                    {
                        字节.AddRange(Encoding.UTF8.GetBytes(内文.Substring(i, 2)));
                        i++;
                        continue;
                    }
                    字节.AddRange(Encoding.UTF8.GetBytes([c]));
                    continue;
                }
                if (++i >= 内文.Length)
                    throw 报错(行号, "字符串结尾是一个孤零零的 \\", "string ends with a lone backslash");
                char e = 内文[i];
                if (转义表C.试解(e, out int 码)) { 字节.Add((byte)码); continue; }
                switch (e)
                {
                    case 'x':
                        {
                            int 起 = i + 1, 长 = 0;
                            while (长 < 2 && 起 + 长 < 内文.Length && Uri.IsHexDigit(内文[起 + 长])) 长++;
                            if (长 == 0) throw 报错(行号, "\\x 后面要跟 1~2 位十六进制(例如 \\x41 = 'A')", "\\x needs 1~2 hex digits");
                            字节.Add(Convert.ToByte(内文.Substring(起, 长), 16));
                            i += 长;
                            break;
                        }
                    case 'u':
                    case 'U':
                        {
                            // T17(2026-09-27 评测整改):`\u` 的解析与"孤立代理码"的判据收到
                            // `转义表C.解u转义` 一处 —— 与字符字面量(`'😀'` / `'\uD83D\uDE00'`)同一个口径。
                            // 以前这一支自己拼代理对、字符字面量那一支不管代理码,两边结论对不上。
                            int? u码 = 转义表C.解u转义(内文, i, out int 尾, out string u说明);
                            if (u码 is not { } 值) throw 报错(行号, u说明, "invalid \\u escape");
                            i = 尾 - 1;   // 后面 for 还会 ++;代理对时 尾 已经跨过第二个转义
                            字节.AddRange(Encoding.UTF8.GetBytes(char.ConvertFromUtf32(值)));
                            break;
                        }
                    default:
                        throw 报错(行号, 转义表C.未知转义提示(e), $"unknown escape \\{e}");
                }
            }
            return 字节.ToArray();
        }

        /// <summary>
        /// 字符串字面量 → 数据段里的一个槽(同样的字节只存一份)。
        /// 字节是**程序开头一次性写进去的**(发几条 `存字`),所以不需要汇编器/虚拟机支持"带初值的数据段"。
        /// </summary>
        private (变量槽定义 槽, int 字节数) 字符串槽(string 内文, int 行号)
        {
            var 字节 = 解码字符串(内文, 行号);
            string 键 = Encoding.Latin1.GetString(字节);
            if (_字符串池.TryGetValue(键, out var 已有)) return 已有;
            int 补齐 = Math.Max(4, (字节.Length + 3) / 4 * 4);      // 补齐到 4 的倍数:初始化整字写,不越界
            var 槽 = 新槽(补齐);
            // 生成"程序开头写这些字节"的初始化代码:先写进一个临时缓冲(和列表扩容例程一个套路),
            // 再整段插到主程序最前面 —— 千万别直接 发出,那会把指令插进当前语句中间(踩过)
            var 存档 = _语句占用;
            var 初始化 = new List<string>();
            _缓冲栈.Push(初始化);
            try
            {
                自成一段(() =>
                {
                    string t = 新临时();                     // 一个临时寄存器反复用(每个字都新要一个会把 15 个用光)
                    for (int i = 0; i < 补齐; i += 4)
                    {
                        uint 字 = 0;
                        for (int j = 0; j < 4 && i + j < 字节.Length; j++) 字 |= (uint)字节[i + j] << (8 * j);
                        输出装载常量(t, 字);
                        发出($"存字 {t} {槽.名称}{(i == 0 ? "" : "+" + i)}");
                    }
                });
            }
            finally
            {
                _缓冲栈.Pop();
                _语句占用 = 存档;
            _语句常量 = null;
            }
            插入主程序开头(初始化);                                 // 程序一开始就写好,后面随便用
            var 结果 = (槽, 字节.Length);
            _字符串池[键] = 结果;
            return 结果;
        }

        /// <summary> 打印字节串的公共子程序:地址在 R12、长度在 R11(调用方负责把这两个寄存器摆好)</summary>
        private void 确保打印串例程()
        {
            if (_打印串例程.Length > 0) return;
            _打印串例程 = 新标签("打印串");
            string 环 = 新标签("串环"), 完 = 新标签("串完");
            _列表例程.Add($"{_打印串例程}:");
            _列表例程.Add($"JZ R11 {完}");                  // 空串
            _列表例程.Add($"{环}:");
            _列表例程.Add("LOAD_INDIRECT R13 R12");          // 读一个字节
            _列表例程.Add("OUT R13");
            _列表例程.Add("ADD_IMMEDIATE R12 1");
            _列表例程.Add("SUB_IMMEDIATE R11 1");
            _列表例程.Add($"JZ R11 {完}");
            _列表例程.Add($"JMP {环}");
            _列表例程.Add($"{完}:");
            _列表例程.Add("返回");
        }

        /// <summary> 打印"以 0 结尾的字节串"的公共子程序:地址在 R12。
        /// 给 `输出(字符数组)` 用 —— 数组多半比文字长,后面全是 0,打到第一个 0 就停。</summary>
        private void 确保打印到零例程()
        {
            if (_打印串到零例程.Length > 0) return;
            _打印串到零例程 = 新标签("打印到零");
            string 环 = 新标签("零环"), 完 = 新标签("零完");
            _列表例程.Add($"{_打印串到零例程}:");
            _列表例程.Add($"{环}:");
            _列表例程.Add("LOAD_INDIRECT R13 R12");
            _列表例程.Add($"JZ R13 {完}");                  // 遇到 0:停
            _列表例程.Add("OUT R13");
            _列表例程.Add("ADD_IMMEDIATE R12 1");
            _列表例程.Add($"JMP {环}");
            _列表例程.Add($"{完}:");
            _列表例程.Add("返回");
        }
        /// <summary> 读一整行的公共子程序:R12 = 存放地址,R11 = 最多收几个;返回 R0 = 实际收了几个,
        /// **R1 = 1 表示"是缓冲满了停的"**(B1:调用方据此决定"扩容接着读"还是收工;0 = 遇到行尾或没输入了)。
        /// 约定:遇到 换行(10) 或 没有输入了(>255) 就停;回车(13) 丢掉不入账。 </summary>
        private void 确保读行例程()
        {
            if (_读行例程.Length > 0) return;
            _读行例程 = 新标签("读行");
            string 环 = 新标签("行环"), 完 = 新标签("行完"), 满 = 新标签("行满");
            _列表例程.Add($"{_读行例程}:");
            _列表例程.Add("LOAD_IMMEDIATE R0 0");            // 收了几个
            _列表例程.Add("LOAD_IMMEDIATE R1 0");            // 0 = 遇到行尾/没输入停的(不是缓冲满)
            _列表例程.Add($"JZ R11 {满}");                   // 缓冲上限 0:一个都没收,算"缓冲满了"
            _列表例程.Add($"{环}:");
            _列表例程.Add("IN R13");
            _列表例程.Add("CMP_IMMEDIATE R13 255");          // >255 = 没有输入了(IN 给 0xFFFFFFFF)
            _列表例程.Add($"JG {完}");
            _列表例程.Add("CMP_IMMEDIATE R13 10");           // 换行:这一行完了
            _列表例程.Add($"JEQ {完}");
            _列表例程.Add("CMP_IMMEDIATE R13 13");           // 回车:丢掉
            _列表例程.Add($"JEQ {环}");
            _列表例程.Add("MOV R14 R12");
            _列表例程.Add("ADD R14 R0");
            _列表例程.Add("STORE_INDIRECT R13 R14");
            _列表例程.Add("ADD_IMMEDIATE R0 1");
            _列表例程.Add("SUB_IMMEDIATE R11 1");
            _列表例程.Add($"JZ R11 {满}");
            _列表例程.Add($"JMP {环}");
            _列表例程.Add($"{满}:");
            _列表例程.Add("LOAD_IMMEDIATE R1 1");
            _列表例程.Add($"{完}:");
            _列表例程.Add("返回");
        }

        /// <summary> 读一整行里的十进制整数的公共子程序;返回 R0 = 数字(一个都没读到给 -1)</summary>
        private void 确保读数字例程()
        {
            if (_读数字例程.Length > 0) return;
            _读数字例程 = 新标签("读数字");
            string 环 = 新标签("数环"), 跳 = 新标签("数跳"), 吃 = 新标签("数吃"), 完 = 新标签("数完"), 负 = 新标签("数负"), 错 = 新标签("数错");
            _列表例程.Add($"{_读数字例程}:");
            _列表例程.Add("LOAD_IMMEDIATE R0 0");            // 值
            _列表例程.Add("LOAD_IMMEDIATE R11 0");           // 0 = 还没开始数字
            _列表例程.Add($"{环}:");
            _列表例程.Add("IN R12");
            _列表例程.Add("CMP_IMMEDIATE R12 255");
            _列表例程.Add($"JG {完}");                       // 没有输入了
            _列表例程.Add("CMP_IMMEDIATE R12 10");
            _列表例程.Add($"JEQ {完}");                      // 一行读完
            _列表例程.Add("CMP_IMMEDIATE R12 13");
            _列表例程.Add($"JEQ {环}");                      // 回车丢掉
            _列表例程.Add("CMP_IMMEDIATE R12 32");           // 空格
            _列表例程.Add($"JEQ {跳}");
            _列表例程.Add("CMP_IMMEDIATE R12 9");            // 制表符
            _列表例程.Add($"JEQ {跳}");
            _列表例程.Add("CMP_IMMEDIATE R12 48");           // '0'
            _列表例程.Add($"JL {错}");
            _列表例程.Add("CMP_IMMEDIATE R12 57");           // '9'
            _列表例程.Add($"JG {错}");
            _列表例程.Add("LOAD_IMMEDIATE R13 10");          // 值 = 值*10 + (c-'0')
            _列表例程.Add("MUL R0 R13");
            _列表例程.Add("SUB_IMMEDIATE R12 48");
            _列表例程.Add("ADD R0 R12");
            _列表例程.Add("LOAD_IMMEDIATE R11 1");
            _列表例程.Add($"JMP {环}");
            _列表例程.Add($"{跳}:");                          // 空格/制表
            _列表例程.Add($"JZ R11 {环}");                    // 还没开始数字:继续找
            _列表例程.Add($"{吃}:");                          // 已经在数字里:把这一行剩下的吃掉,以免留给下一次读
            _列表例程.Add("IN R12");
            _列表例程.Add("CMP_IMMEDIATE R12 255");
            _列表例程.Add($"JG {完}");
            _列表例程.Add("CMP_IMMEDIATE R12 10");
            _列表例程.Add($"JEQ {完}");
            _列表例程.Add($"JMP {吃}");
            _列表例程.Add($"{完}:");
            _列表例程.Add($"JZ R11 {负}");                    // 一个数字都没读到 → -1
            _列表例程.Add("返回");
            _列表例程.Add($"{负}:");
            _列表例程.Add("LUI R0 1048575");                 // 1048575<<12 | 4095 = 0xFFFFFFFF = -1
            _列表例程.Add("OR_IMMEDIATE R0 4095");
            _列表例程.Add("返回");
            _列表例程.Add($"{错}:");                          // 不是数字:停下给出明确错误
            {
                var 例程 = _列表例程;                        // 让 报错停机 生成的指令落进这段例程里
                _缓冲栈.Push(例程);
                try { 报错停机(0, "读取数字:这里要写数字(0~9),读到别的字符了"); }
                finally { _缓冲栈.Pop(); }
            }
        }

        // ---------------- 测试框架(P10):`测试 名字 { 断言 条件; }` ----------------
        //
        // 与 `// 预期输出:` 的分工:`// 预期输出:` 是"整个程序输出对不对"的**回归断言**(自检在跑,一个程序一条);
        // `测试`/`断言` 是**程序里自带的单元测试** —— 一个程序里可以有很多条,失败时直接说出"哪一行、哪个条件",
        // 失败**不打断**(一次看全),块尾汇总;任何一个块失败过,跑完把失败数写进"错误槽"(命令行/IDE 看得出没过)。

        /// <summary> 一个 `测试 名字 { … }` 块:自己两个计数器(块内断言数 / 失败数)</summary>
        private sealed class 测试块C
        {
            public string 名字 = "";
            public int 行号;
            public 变量槽定义 总数槽;
            public 变量槽定义 失败槽;
        }

        private 测试块C? _当前测试块;         // `断言` 必须写在测试块里;测试块不许嵌套
        private readonly List<string> _测试块名字 = [];   // 编译提示用

        /// <summary>`测试 名字 { … }` —— 块尾打印汇总;这一块有失败就把失败数写进错误槽(不停止)</summary>
        private void 处理测试块(ref int 位置)
        {
            int 行号 = _伪行号表[位置];
            string 内容 = _伪行表[位置];
            string 首词 = 取首词(内容);
            string 余下 = 内容[首词.Length..].Trim();
            if (_当前函数 is not null)
                throw 报错(行号, "`测试` 块要写在**函数外面**(它是程序级别的:块里的断言按先后顺序跑一遍)",
                    "`test` blocks must be written outside functions (top level)");
            if (_当前测试块 is not null)
                throw 报错(行号, "`测试` 块不能嵌套:一个块写完(补上 `}`)再写下一个", "`test` blocks cannot be nested");
            string 名字 = 余下.Trim();
            if (名字.Length == 0)
                throw 报错(行号, "`测试` 后面要写一个名字(汇总那行会用它):测试 加法 { … }", "`test` needs a name");
            if (!标识符带点正则.IsMatch(名字))
                throw 报错(行号, $"测试名非法: '{名字}'(只要一个名字,不带括号/空格)", $"bad test name: '{名字}'");
            位置++;
            if (位置 >= _伪行表.Count || _伪行表[位置] != "{")
                throw 报错(行号, "`测试 名字` 后面缺少 '{'", "expected '{' after the test header");

            var 块 = new 测试块C { 名字 = 名字, 行号 = 行号, 总数槽 = 新槽(4), 失败槽 = 新槽(4) };
            _测试块名字.Add(名字);
            // 两个计数器从 0 开始(数据段本来也是 0,但"重复编译/单元测试里连着跑"不能靠天生是 0)
            输出装载常量到槽(块.总数槽, 0);
            输出装载常量到槽(块.失败槽, 0);
            var 上一个块 = _当前测试块;
            _当前测试块 = 块;
            try { 处理块(ref 位置); }
            finally { _当前测试块 = 上一个块; }
            // 块尾汇总:`测试 名字:共 N 条断言,失败 M 条`(整段自成一体:外面那些寄存器一个都不用管)
            自成一段(() =>
            {
                发出打印字面量($"测试 {名字}:共 ", 行号);
                发出打印十进制寄存器(读槽到临时(块.总数槽));
                发出打印字面量(" 条断言,失败 ", 行号);
                发出打印十进制寄存器(读槽到临时(块.失败槽));
                发出打印字面量(" 条", 行号);
                发出("NEWLINE");
            });
            // 这一块有失败 → 把失败数写进错误槽(不停止):命令行/IDE 跑完能看出"这次测试没过"
            自成一段(() =>
            {
                string 好 = 新标签("测试过");
                string 失败 = 读槽到临时(块.失败槽);
                发出($"CMP_IMMEDIATE {失败} 0");
                发出($"JEQ {好}");
                记运行期错误(行号, $"测试 {名字} 里有断言不成立:共几条、失败几条见上一行汇总,不成立的行号与条件都打印在上面了。" +
                    $"这个失败**没有**让程序停下(免得一条失败就看不到后面的)," +
                     "但要按\"测试没过\"算:照上面打印的行号去改那条断言,或者改被测的代码。", 停机: false);
                发出($"{好}:");
            });
        }

        /// <summary>`断言 条件;` / `断言 条件, "说明";` —— 不成立就打印那一行(不打断),两个计数器各 +1</summary>
        private void 处理断言(int 位置)
        {
            int 行号 = _伪行号表[位置];
            string 内容 = _伪行表[位置];
            string 原文行 = 位置 < _伪行原文.Count ? _伪行原文[位置] : 内容;
            if (_当前测试块 is not { } 块)
                throw 报错(行号, "`断言` 要写在 `测试 名字 { … }` 里(这样失败时能按块汇总)",
                    "`assert` must be inside a `测试` block");
            string 首词 = 取首词(内容);
            string 余下 = 内容[首词.Length..].Trim().TrimEnd(';').Trim();
            string 原文余下 = 原文行.Trim();
            int 空格 = 原文余下.IndexOf(' ');
            原文余下 = (空格 > 0 ? 原文余下[(空格 + 1)..] : "").Trim().TrimEnd(';').Trim();
            if (余下.Length == 0)
                throw 报错(行号, "`断言` 后面要写一个条件:断言 1 + 1 == 2;", "`assert` needs a condition");
            var 段们 = 切实参(余下);
            var 原文段们 = 切实参(原文余下);
            if (段们.Count == 0 || 段们[0].Trim().Length == 0)
                throw 报错(行号, "`断言` 后面要写一个条件:断言 1 + 1 == 2;", "`assert` needs a condition");
            if (段们.Count > 2)
                throw 报错(行号, "`断言` 最多写两段:条件 + 说明(`断言 左 == 右, \"说明\";`)",
                    "`assert` takes a condition and an optional message");
            string 条件 = 段们[0].Trim();
            string 条件原文 = 原文段们.Count > 0 ? 原文段们[0].Trim() : 条件;
            string 说明 = 段们.Count == 2 ? 段们[1].Trim() : "";
            string 说明文本 = "";
            if (说明.Length > 0)
            {
                if (!是字符串字面量(说明))
                    throw 报错(行号, $"`断言` 的说明要写成字符串字面量:断言 {条件}, \"说明\"; —— 现在给的是 '{说明}'",
                        "the assert message must be a string literal");
                说明文本 = $"({说明[1..^1]})";
            }

            string 通过 = 新标签("断言过");
            // 条件怎么算交给条件分发那一份:常量折叠 / 字符串比较 / 函数调用 / `&&` `||` `!` 都在里面
            // (与 `如果` 同一个入口 —— 断言里的条件写法与 `如果` 完全一样)
            输出条件为真(条件, 行号, 通过);
            自成一段(() =>
            {
                发出打印字面量($"第 {行号} 行:断言失败:{条件原文}{说明文本}", 行号);
                发出("NEWLINE");
                // 打印那一步会 CALL,所以先打完再动计数器
                发出加一(块.失败槽);
            });
            发出($"{通过}:");
            自成一段(() => 发出加一(块.总数槽));
        }

        /// <summary>把一个常量写进槽(计数器清零用)</summary>
        private void 输出装载常量到槽(变量槽定义 槽, uint 值)
        {
            自成一段(() =>
            {
                string t = 新临时();
                输出装载常量(t, 值);
                发出写变量(t, new 地址位置(槽.名称, 0, 4));
            });
        }

        private string 读槽到临时(变量槽定义 槽)
        {
            string t = 新临时();
            发出读变量(t, 槽);
            return t;
        }

        /// <summary>计数器 +1(数据段隐藏槽)</summary>
        private void 发出加一(变量槽定义 槽)
        {
            var 快照 = 记占用();
            try
            {
                string t = 读槽到临时(槽);
                发出($"ADD_IMMEDIATE {t} 1");
                发出写变量(t, new 地址位置(槽.名称, 0, 4));
            }
            finally { 还占用(快照); }
        }

        /// <summary>打印一段**编译期就知道**的文字(不换行):测试的失败行与汇总行用它。
        /// 用完就把自己那几个临时寄存器还掉 —— 汇总行连着打三四段,不还就把 15 个临时寄存器用光了(实测踩到)。</summary>
        private void 发出打印字面量(string 文本, int 行号)
        {
            var 快照 = 记占用();
            try
            {
                确保打印串例程();
                占住子程序约定寄存器();
                var (槽, 字节数) = 字符串槽(文本, 行号);
                string 址 = 新临时();
                发出($"取地址 {址} {槽.名称}");
                发出($"MOV R12 {址}");
                string 长 = 新临时();
                输出装载常量(长, (uint)字节数);
                发出($"MOV R11 {长}");
               发出($"CALL {_打印串例程}");
            }
            finally { 还占用(快照); }
        }

        /// <summary>打印一个寄存器里的十进制(负数按 `测试输出` 那套加负号)</summary>
        private void 发出打印十进制寄存器(string 寄存器)
        {
            var 快照 = 记占用();
            try
            {
                string r = 寄存器;
                发出处理负号(ref r, 可改写: true);
                发出打印十进制(r);
            }
            finally { 还占用(快照, 寄存器); }
        }

        /// <summary>
        /// `输出(值)` / `输出行(值)`:给人用的控制台输出(不换行 / 末尾换行)。
        /// 值可以是:字符串字面量 `"文字"`、字符/字节的数组或列表(按字节打)、字符/字节变量(按字节打)、
        /// 其余(短整数/var/表达式)按十进制打。
        /// </summary>
        private void 处理输出(ref int 位置, bool 换行)
        {
            int 行号 = _伪行号表[位置];
            string 内容 = _伪行表[位置];
            string 首词 = 取首词(内容);
            string 余下 = 内容[首词.Length..].Trim();
            string 词名 = 换行 ? "输出行" : "输出";
            if (余下.Length == 0)
            {
                if (!换行) throw 报错(行号, "输出() 要写清楚输出什么;只想要一个空行就写 输出行()", "print needs a value; use println() for a blank line");
                发出("NEWLINE");
                位置++;
                return;
            }
            if (!余下.StartsWith('(') || !余下.EndsWith(')'))
                throw 报错(行号, $"{词名}(...) 需要括号:{内容}", $"{词名}(...) needs parentheses: {内容}");
            string 参数 = 余下[1..^1].Trim();
            if (参数.Length == 0)
            {
                if (!换行) throw 报错(行号, "输出() 要写清楚输出什么(要空行写 输出行())", "print needs a value");
                发出("NEWLINE");
                位置++;
                return;
            }
            准备语句占用(内容);
            // 通用化(2026-10):**浮点值不能走整数打印那条路** —— 它就是一个寄存器里的 32 位 IEEE754 位模式,
            // 按整数打会打出 `1069547520` 这种"看着像算错了"的数(静默错误)。
            // 浮点值交给**编译期生成的那条例程**(`中间层C.浮点打印.cs` 的 `输出浮点值`):
            // 它把位模式转成十进制文字再打到标准输出上,口径与 `Sample\库\float32.hb` 的
            // `浮点32转字符串` 一致(7 位有效数字、小数末尾的 0 去掉、`0`/`-0`/`Inf`/`-Inf`/`NaN`)。
            // 判据用 `是浮点表达式`(**不是** `是浮点字面量 || 是浮点变量`):
            // 后两者只认"整段一个字面量/一个变量名",于是 `输出行(左 + 右)`、`输出行(左 * 2.0)`
            // 这种**表达式**会被当成整数去打,打出 `1082130432`(4.0 的位模式)—— 静默错误。
            if (是浮点表达式(参数))
            {
                输出浮点值(参数, 行号, 内容, 换行);
                位置++;
                return;
            }
            输出一个值(参数, 行号, 内容);
            if (换行) 发出("NEWLINE");
            位置++;
        }

        /// <summary>
        /// 拼接的一段:**字符串**(整段拷)、**一个字节**(单字节追加),或**一个文字**(Unicode 码点,按 UTF-8 追加 1~4 字节)。
        /// 注意用词:HB 的 `字符` 类型就是 **1 个字节**(和 C 的 char 一样);汉字是 1 个**文字**、3 个字节。
        /// </summary>
        /// <summary>拼接的一段:整段字符串 / 一个字节(字符字面量或宽度 1 的左值)/ 一个文字(码点)</summary>
        private sealed class 拼接段
        {
            public bool 是单字节;
            public string 文本;
            public uint 字节值;
            public bool 是文字;
            /// <summary>
            /// 这一段是"返回字符串的函数调用"时:先把它**物化**到这个隐藏临时串上
            /// (只调一次 —— 算长度与拷内容都用它,不然函数会被调两遍,有副作用的就对不上了)
            /// </summary>
            public 列表绑定C? 物化串;

            public 拼接段(bool 是单字节, string 文本, uint 字节值 = 0, bool 是文字 = false)
            {
                this.是单字节 = 是单字节;
                this.文本 = 文本;
                this.字节值 = 字节值;
                this.是文字 = 是文字;
            }
        }

        /// <summary>
        /// "返回 字符串 的函数调用"直接当字符串用时的隐藏临时串。
        /// 以前要求先 `字符串 临时 = 取名字();` 再用;现在编译器在每个调用点自动开一个:
        /// 函数里开在**帧**里(每层调用各一份,递归也安全),顶层开在数据段。
        /// 临时串和"声明一个字符串变量"完全一样(池里要一块、变长自动换块),
        /// 所以调用前先把它初始化成空串,再把它的头地址递给被调方。
        /// </summary>
        #endregion
    }
}
