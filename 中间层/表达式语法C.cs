using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary> 表达式词元的种类 </summary>
    public enum 词元种类C
    {
        数值,
        名字,
        字符串,
        运算符,
        左括号,
        右括号,
        逗号,
        /// <summary> 通用化(2026-10):**浮点字面量**(`1.5` / `1.5f`)。
        /// 它仍是一个"常量",但值是 IEEE754 单精度位模式,所以单独一档 ——
        /// 免得被当成整数走进整数那套范围检查与常量折半。 </summary>
        浮点,
    }

    /// <summary>
    /// 表达式词元(切词产物):数值词元带 数值,其余用 文本。
    /// 通用化(2026-10)起浮点字面量走 <see cref="词元种类C.浮点"/> 那一档,
    /// 它的 IEEE754 单精度位模式放在 <paramref name="浮点位"/> 里(带默认值 ⇒ 老调用点一处都不用改)。
    /// </summary>
    public readonly record struct 词元C(词元种类C 种类, string 文本, uint 数值, int 位置, uint 浮点位 = 0)
    {
        public bool 是运算符(string 记号) => 种类 == 词元种类C.运算符 && 文本 == 记号;
        /// <summary> 通用化:这个词元是不是浮点字面量 </summary>
        public bool 是浮点 => 种类 == 词元种类C.浮点;
        public override string ToString() => 种类 switch
        {
            词元种类C.数值 => 数值.ToString(),
            词元种类C.浮点 => $"{文本}(0x{浮点位:X8})",
            _ => 文本,
        };
    }

    /// <summary>
    /// 表达式 AST(小型语法树)。
    /// 四类节点够表达现在这门语言的全部表达式:常量 / 名字(变量·字段·数组元素·寄存器) / 一元 / 二元;
    /// 第五类"调用"已经留好位置 —— 等 2 号改进(表达式里调函数)接上即可。
    /// </summary>
    public abstract class 表达式节点C
    {
        /// <summary> 调试/测试用:括号化中缀写法(能一眼看出优先级与结合顺序) </summary>
        public abstract string 显示();

        /// <summary> 子树里有没有函数调用 —— 求值时要跨 CALL,中间结果必须落到帧槽里暂存 </summary>
        public virtual bool 含调用 => false;
    }

    /// <summary>
    /// 常量节点。通用化(2026-10)起它还能表达**浮点常量**:<paramref name="浮点"/> 为真时,
    /// <see cref="值"/> 装的是那个浮点数的 **IEEE754 单精度位模式**(不是整数值)。
    /// 参数带默认值 ⇒ 老的 `new 常量节点C(5)` 一处都不用改,语义也一个字不变。
    /// </summary>
    public sealed class 常量节点C(uint 值, bool 浮点 = false) : 表达式节点C
    {
        public uint 值 { get; } = 值;
        /// <summary> 这个常量是不是浮点(位模式在 <see cref="值"/> 里) </summary>
        public bool 是浮点 { get; } = 浮点;
        public override string 显示() => 是浮点 ? $"{BitConverter.UInt32BitsToSingle(值):R}f" : 值.ToString();
    }

    public sealed class 名字节点C(string 名) : 表达式节点C
    {
        public string 名 { get; } = 名;
        public override string 显示() => 名;
    }

    /// <summary>
    /// 字符串字面量节点(`"…"`,2026-09 起):**只用来和另一个字符串比大小**(按字节)。
    /// 带引号的原文照存 —— 建数据槽、解码转义都交给字符串那一套(它已经有 `字符串槽`),
    /// 这里只负责"让语法树认得出这是个字符串"。单独当数字用会给出明确报错。
    /// </summary>
    public sealed class 字符串节点C(string 原文) : 表达式节点C
    {
        public string 原文 { get; } = 原文;
        public override string 显示() => 原文;
    }

    public sealed class 一元节点C(string 记号, 表达式节点C 子) : 表达式节点C
    {
        public string 记号 { get; } = 记号;
        public 表达式节点C 子 { get; } = 子;
        public override string 显示() => $"({记号}{子.显示()})";
        public override bool 含调用 => 子.含调用;
    }

    public sealed class 二元节点C(string 记号, 表达式节点C 左, 表达式节点C 右) : 表达式节点C
    {
        public string 记号 { get; } = 记号;
        public 表达式节点C 左 { get; } = 左;
        public 表达式节点C 右 { get; } = 右;
        public override string 显示() => $"({左.显示()} {记号} {右.显示()})";
        public override bool 含调用 => 左.含调用 || 右.含调用;
    }

    /// <summary> 函数/方法调用节点(2 号改进用):名字 + 实参表达式 </summary>
    public sealed class 调用节点C(string 名, IReadOnlyList<表达式节点C> 实参) : 表达式节点C
    {
        public string 名 { get; } = 名;
        public IReadOnlyList<表达式节点C> 实参 { get; } = 实参;
        public override string 显示() => $"{名}({string.Join(", ", 实参.Select(a => a.显示()))})";
        public override bool 含调用 => true;
    }

    /// <summary>
    /// 三目节点(`条件 ? 真支 : 假支`,R2-2,2026-09-21 收进 AST):**优先级最低、右结合**
    /// (`条件一 ? 值一 : 条件二 ? 值二 : 值三` 读作 `条件一 ? 值一 : (条件二 ? 值二 : 值三)`)。
    /// 求值时展开成跳转,**只算被选中的那一支**(没选中的那支一条指令都不发)。
    /// 收进 AST 的原因:以前三目只在赋值那条路上按**文本**找顶层 `?`,条件那条路根本不认 ——
    /// 于是 `如果 (条件 ? 1 : 0)` 与 `如果 (x == (条件 ? 1 : 2))` 走的是两套切法(后者实测算错)。
    /// </summary>
    public sealed class 三目节点C(表达式节点C 条件, 表达式节点C 真, 表达式节点C 假) : 表达式节点C
    {
        public 表达式节点C 条件 { get; } = 条件;
        public 表达式节点C 真 { get; } = 真;
        public 表达式节点C 假 { get; } = 假;
        public override string 显示() => $"({条件.显示()} ? {真.显示()} : {假.显示()})";
        public override bool 含调用 => 条件.含调用 || 真.含调用 || 假.含调用;
    }

    /// <summary> 表达式语法错误(带字符位置,调用方再加上源行号就是完整报错) </summary>
    public sealed class 表达式语法异常C(string 消息, int 位置) : Exception(消息)
    {
        public int 位置 { get; } = 位置;
    }

    /// <summary>
    /// 表达式前端:文本 → 词元 → AST。纯语法,不认识变量/函数/寄存器,也不生成任何汇编,
    /// 所以可以单独跑单元测试(见综合自检的"表达式AST"组)。
    /// 优先级(低 → 高):| &gt; ^ &gt; &amp; &gt; &lt;&lt; &gt;&gt; &gt; + - &gt; * / % &gt; 一元 - &gt; 原子
    /// </summary>
    public static class 表达式语法C
    {
        /// <summary>
        /// 支持的二元运算符(按优先级从低到高分组,单测/帮助都用它)。
        /// 2026-09 起最底下多了三层:**比较**和 **&amp;&amp; / ||** 都能写进表达式,结果是一个 `逻辑` 值(0/1)。
        /// 比较比 `| ^ &amp;` 还低 —— 这样 `a | b == c` 读作 `(a | b) == c`,和条件里一直以来的切法一致。
        /// </summary>
        public static readonly string[][] 优先级表 =
        [
            ["||"],
            ["&&"],
            ["==", "!=", "<", ">", "<=", ">="],
            ["|"],
            ["^"],
            ["&"],
            ["<<", ">>"],
            ["+", "-"],
            ["*", "/", "%"],
        ];

        /// <summary> 全部二元运算符 </summary>
        public static IEnumerable<string> 全部二元运算符 => 优先级表.SelectMany(x => x);

        /// <summary>
        /// 表达式**最多嵌套多少层**(R2-1,2026-09-21):平铺长运算链、括号、一元链**都按层数算**。
        ///
        /// 为什么必须有一道上限:语法树是**照着树递归**求值 / 折常量的,而 `.NET` 的栈溢出(`0xC00000FD`)
        /// **接不住**(进程直接没,连一句诊断都没有)—— 实测 500 项平铺 `+` 还能编、1000 项就死;
        /// 括号 300 层还能编、600 层就死。128 是照"编译器能安全递归的深度"取的(离实测极限一倍以上余量),
        /// 不是"刚好够现有示例"。超限在 `解析` 里当场抛 `表达式语法异常C` ⇒ 用户看到的是
        /// 一条**带行号的编译错误**(见 `中间层C.表达式.求值表达式` 那层包装),而不是编译器自己崩掉。
        /// </summary>
        public const int 最大深度 = 128;

        // ---------------- 词法 ----------------
        /// <summary>
        /// 切词:数值(十/十六进制)、名字(可带 .字段 与 [下标])、运算符(&lt;&lt; &gt;&gt; 等)、括号、逗号。
        /// 空白跳过;不认识的字符直接报位置。
        /// </summary>
        public static List<词元C> 切词(string 文本)
        {
            var 结果 = new List<词元C>();
            int i = 0;
            while (i < 文本.Length)
            {
                char c = 文本[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsDigit(c))
                {
                    int 起 = i;
                    bool 十六 = c == '0' && i + 1 < 文本.Length && (文本[i + 1] == 'x' || 文本[i + 1] == 'X');
                    if (十六)
                    {
                        i += 2;
                        while (i < 文本.Length && Uri.IsHexDigit(文本[i])) i++;
                    }
                    else
                    {
                        // 通用化(2026-10):`1.5` / `1.5f` 这类**浮点字面量**。
                        // 判据:小数点**后面必须还跟着数字**(所以 `记录.字段` 这种成员访问不受影响 ——
                        // 它前面是名字不是数字,走不到这儿;而 `1.` 这种半截写法也不认,免得把 `1..2` 之类切歪)。
                        // 十六进制整篇不参与(它在上面那一支就走完了)。
                        while (i < 文本.Length && char.IsDigit(文本[i])) i++;
                        if (i < 文本.Length && 文本[i] == '.' && i + 1 < 文本.Length && char.IsDigit(文本[i + 1]))
                        {
                            i++;                                        // 吃掉小数点
                            while (i < 文本.Length && char.IsDigit(文本[i])) i++;
                            if (i < 文本.Length && (文本[i] == 'f' || 文本[i] == 'F')) i++;   // 可选后缀
                            string 浮点原文 = 文本[起..i];
                            string 数字部分 = 浮点原文.TrimEnd('f', 'F');
                            if (!float.TryParse(数字部分, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out float 浮值))
                                throw new 表达式语法异常C($"浮点数写得不对: '{浮点原文}'", 起);
                            结果.Add(new 词元C(词元种类C.浮点, 浮点原文, 0, 起,
                                BitConverter.SingleToUInt32Bits(浮值)));
                            continue;
                        }
                    }
                    string 原文 = 文本[起..i];
                    uint 值;
                    bool 成功 = 十六
                        ? uint.TryParse(原文[2..], System.Globalization.NumberStyles.HexNumber, null, out 值)
                        : uint.TryParse(原文, out 值);
                    if (!成功) throw new 表达式语法异常C($"数值超出范围: '{原文}'", 起);
                    结果.Add(new 词元C(词元种类C.数值, 原文, 值, 起));
                    continue;
                }

                if (c == '_' || char.IsLetter(c))
                {
                    int 起 = i;
                    while (i < 文本.Length && (文本[i] == '_' || char.IsLetterOrDigit(文本[i]))) i++;
                    // 字段与下标可以任意交替:名.字段[下标].字段[下标]…(整个算一个名字,由求值那一侧解析)
                    while (i < 文本.Length)
                    {
                        if (文本[i] == '.' && i + 1 < 文本.Length && (文本[i + 1] == '_' || char.IsLetter(文本[i + 1])))
                        {
                            i++;
                            while (i < 文本.Length && (文本[i] == '_' || char.IsLetterOrDigit(文本[i]))) i++;
                            continue;
                        }
                        if (文本[i] == '[')
                        {
                            int 深 = 0;
                            while (i < 文本.Length)
                            {
                                if (文本[i] == '[') 深++;
                                else if (文本[i] == ']') { 深--; i++; if (深 == 0) break; continue; }
                                i++;
                            }
                            if (深 != 0) throw new 表达式语法异常C("数组下标缺少 ']'", 起);
                            continue;
                        }
                        break;
                    }
                    结果.Add(new 词元C(词元种类C.名字, 文本[起..i], 0, 起));
                    continue;
                }

                if (c == '"')
                {
                    int 起 = i;
                    i++;
                    while (i < 文本.Length && 文本[i] != '"')
                    {
                        if (文本[i] == '\\' && i + 1 < 文本.Length) i++;    // 转义:连着的下一个字符不当收尾引号
                        i++;
                    }
                    if (i >= 文本.Length) throw new 表达式语法异常C("字符串字面量缺少结尾的 '\"'", 起);
                    i++;                                                    // 收尾引号
                    结果.Add(new 词元C(词元种类C.字符串, 文本[起..i], 0, 起));
                    continue;
                }

                switch (c)
                {
                    case '(': 结果.Add(new 词元C(词元种类C.左括号, "(", 0, i)); i++; continue;
                    case ')': 结果.Add(new 词元C(词元种类C.右括号, ")", 0, i)); i++; continue;
                    case ',': 结果.Add(new 词元C(词元种类C.逗号, ",", 0, i)); i++; continue;
                }

                // 三目(`?` `:`,R2-2,2026-09-21):让"条件"和"赋值右侧"共用同一套优先级 ——
                // 以前三目只在赋值那条路上按文本切,条件那条路不认(AST 里也没有),两条路各算各的。
                if (c == '?') { 结果.Add(new 词元C(词元种类C.运算符, "?", 0, i)); i++; continue; }
                if (c == ':') { 结果.Add(new 词元C(词元种类C.运算符, ":", 0, i)); i++; continue; }

                if (c == '<' || c == '>' || c == '=' || c == '!')
                {
                    // 两字符的先认:<< >> <= >= == !=
                    if (i + 1 < 文本.Length)
                    {
                        string 双 = 文本.Substring(i, 2);
                        if (双 is "<<" or ">>" or "<=" or ">=" or "==" or "!=")
                        {
                            结果.Add(new 词元C(词元种类C.运算符, 双, 0, i));
                            i += 2;
                            continue;
                        }
                    }
                    if (c == '=') throw new 表达式语法异常C("比较相等要写 '=='(一个 '=' 是赋值)", i);
                    结果.Add(new 词元C(词元种类C.运算符, c.ToString(), 0, i));
                    i++;
                    continue;
                }

                if (c == '&' || c == '|')
                {
                    // `&&` `||`(逻辑)先认;单个 `&` `|` 是按位运算
                    if (i + 1 < 文本.Length && 文本[i + 1] == c)
                    {
                        结果.Add(new 词元C(词元种类C.运算符, new string(c, 2), 0, i));
                        i += 2;
                        continue;
                    }
                    结果.Add(new 词元C(词元种类C.运算符, c.ToString(), 0, i));
                    i++;
                    continue;
                }

                if ("+-*/%^~".Contains(c))
                {
                    结果.Add(new 词元C(词元种类C.运算符, c.ToString(), 0, i));
                    i++;
                    continue;
                }

                throw new 表达式语法异常C($"表达式里有无法识别的字符 '{c}'", i);
            }
            return 结果;
        }

        // ---------------- 语法(递归下降,一层一个优先级) ----------------
        /// <summary> 文本 → AST </summary>
        public static 表达式节点C 解析(string 文本)
        {
            var 词元表 = 切词(文本);
            if (词元表.Count == 0) throw new 表达式语法异常C("表达式为空", 0);
            return 解析(词元表);
        }

        /// <summary> 词元表 → AST(会检查末尾没有多余记号) </summary>
        public static 表达式节点C 解析(List<词元C> 词元表)
        {
            var 读 = new 词元游标(词元表);
            表达式节点C 根 = 解析三目(读, 1);
            if (!读.到头)
            {
                if (读.当前.种类 == 词元种类C.右括号)
                    throw new 表达式语法异常C("多余的 ')'", 读.当前.位置);
                throw new 表达式语法异常C($"表达式末尾有多余内容: '{读.当前.文本}'", 读.当前.位置);
            }
            // R2-1:长运算链在**语法上**不加深递归(就是同一个 while 转圈),但**树是左倾的** ——
            // 1000 项 `+` 出来就是 1000 层深的树,后面照树递归的求值 / 折常量一样会崩。
            // 所以这里再按**树深**把一道关。词元比上限还少时深度不可能超,省掉这趟遍历(绝大多数表达式走这条)。
            if (词元表.Count > 最大深度)
            {
                int 树深 = 深度(根);
                if (树深 > 最大深度) throw new 表达式语法异常C(嵌套太深(树深), 0);
            }
            return 根;
        }

        private sealed class 词元游标(List<词元C> 词元表)
        {
            private readonly List<词元C> _词元表 = 词元表;
            private int _位置;
            public bool 到头 => _位置 >= _词元表.Count;
            public 词元C 当前 => 到头 ? new 词元C(词元种类C.数值, "", 0, _词元表.Count == 0 ? 0 : _词元表[^1].位置) : _词元表[_位置];
            public 词元C 下一个() => 到头 ? 当前 : _词元表[_位置++];
            public int 位置 => 到头 ? 当前.位置 + 当前.文本.Length : _词元表[_位置].位置;
        }

        /// <summary> 嵌套太深那一句(R2-1):实测层数 + 上限 + **改法**,照项目报错口径写 </summary>
        private static string 嵌套太深(int 层数) =>
            $"表达式嵌套太深({层数} 层,上限 {最大深度} 层):把它拆成几步 —— 先算出中间结果放进变量,再接着算";

        /// <summary> 按优先级表逐层解析(层号越大优先级越高);`递归` = 括号 / 一元链套到第几层,超限当场报错(R2-1) </summary>
        /// <summary>
        /// 三目层(R2-2):**比 `||` 还低、右结合** —— `条件 ? 真支 : 假支`。
        /// 三边都不许空(空的那一支当场报错,文案与老的文本切分路**一个字不差**,免得既有断言位移)。
        /// `递归` 照旧按层数记:三目套三目也吃 `最大深度` 那道护栏。
        /// </summary>
        private static 表达式节点C 解析三目(词元游标 读, int 递归)
        {
            if (递归 > 最大深度) throw new 表达式语法异常C(嵌套太深(递归), 读.位置);
            表达式节点C 条件 = 解析层(读, 0, 递归);
            if (读.到头 || !读.当前.是运算符("?")) return 条件;
            读.下一个();
            if (读.到头 || 读.当前.种类 is 词元种类C.右括号 or 词元种类C.逗号)
                throw new 表达式语法异常C("三目 '条件 ? 真支 : 假支' 三边都不能空", 读.位置);
            表达式节点C 真 = 解析三目(读, 递归 + 1);
            if (读.到头 || !读.当前.是运算符(":"))
                throw new 表达式语法异常C("三目少了配对的 ':'", 读.位置);
            读.下一个();
            if (读.到头 || 读.当前.种类 is 词元种类C.右括号 or 词元种类C.逗号)
                throw new 表达式语法异常C("三目 '条件 ? 真支 : 假支' 三边都不能空", 读.位置);
            表达式节点C 假 = 解析三目(读, 递归 + 1);
            return new 三目节点C(条件, 真, 假);
        }

        private static 表达式节点C 解析层(词元游标 读, int 层, int 递归)
        {
            if (递归 > 最大深度) throw new 表达式语法异常C(嵌套太深(递归), 读.位置);
            if (层 >= 优先级表.Length) return 解析一元(读, 递归);
            表达式节点C 左 = 解析层(读, 层 + 1, 递归);
            while (!读.到头 && 读.当前.种类 == 词元种类C.运算符 && 优先级表[层].Contains(读.当前.文本))
            {
                string 记号 = 读.下一个().文本;
                表达式节点C 右 = 解析层(读, 层 + 1, 递归);
                左 = new 二元节点C(记号, 左, 右);
            }
            return 左;
        }

        private static 表达式节点C 解析一元(词元游标 读, int 递归)
        {
            if (递归 > 最大深度) throw new 表达式语法异常C(嵌套太深(递归), 读.位置);
            if (!读.到头 && 读.当前.是运算符("-")) { 读.下一个(); return new 一元节点C("-", 解析一元(读, 递归 + 1)); }
            if (!读.到头 && 读.当前.是运算符("+")) { 读.下一个(); return 解析一元(读, 递归 + 1); }   // 一元正号:吃掉
            if (!读.到头 && 读.当前.是运算符("!")) { 读.下一个(); return new 一元节点C("!", 解析一元(读, 递归 + 1)); }  // 逻辑取反(结果 0/1)
            if (!读.到头 && 读.当前.是运算符("~")) { 读.下一个(); return new 一元节点C("~", 解析一元(读, 递归 + 1)); }  // 按位取反(32 位)
            return 解析原子(读, 递归);
        }

        private static 表达式节点C 解析原子(词元游标 读, int 递归)
        {
            if (读.到头) throw new 表达式语法异常C("表达式不完整", 读.位置);
            词元C 记号 = 读.下一个();
            switch (记号.种类)
            {
                case 词元种类C.数值:
                    return new 常量节点C(记号.数值);

                // 通用化(2026-10):浮点字面量 —— 位模式原样带过去(它是常量,编译期就定死了)
                case 词元种类C.浮点:
                    return new 常量节点C(记号.浮点位, 浮点: true);

                case 词元种类C.字符串:
                    return new 字符串节点C(记号.文本);

                case 词元种类C.左括号:
                {
                    表达式节点C 内 = 解析三目(读, 递归 + 1);
                    if (读.到头 || 读.当前.种类 != 词元种类C.右括号)
                        throw new 表达式语法异常C("括号不配对,缺少 ')'", 记号.位置);
                    读.下一个();
                    return 内;
                }

                case 词元种类C.名字:
                    // 名字后面跟 '(' → 函数/方法调用节点
                    if (!读.到头 && 读.当前.种类 == 词元种类C.左括号)
                    {
                        读.下一个();
                        var 实参 = new List<表达式节点C>();
                        if (!读.到头 && 读.当前.种类 == 词元种类C.右括号) 读.下一个();
                        else
                        {
                            while (true)
                            {
                                实参.Add(解析三目(读, 递归 + 1));
                                if (读.到头) throw new 表达式语法异常C($"调用 '{记号.文本}' 缺少 ')'", 记号.位置);
                                if (读.当前.种类 == 词元种类C.逗号) { 读.下一个(); continue; }
                                if (读.当前.种类 == 词元种类C.右括号) { 读.下一个(); break; }
                                throw new 表达式语法异常C($"调用 '{记号.文本}' 的实参之间缺少 ',' 或 ')'", 读.当前.位置);
                            }
                        }
                        return new 调用节点C(记号.文本, 实参);
                    }
                    return new 名字节点C(记号.文本);

                case 词元种类C.右括号:
                    throw new 表达式语法异常C("多余的 ')'", 记号.位置);

                default:
                    throw new 表达式语法异常C($"这里不该出现运算符 '{记号.文本}'", 记号.位置);
            }
        }

        // ---------------- 小工具(调试/测试/后续优化都能用) ----------------
        /// <summary> 全常量子树 → 值;不是常量返回 null </summary>
        public static uint? 试求常量(表达式节点C 节点) => 节点 switch
        {
            常量节点C 常量 => 常量.值,
            一元节点C { 记号: "-" } 一元 when 试求常量(一元.子) is { } 子 => 0u - 子,
            // `!值` 只看"是不是 0",没有符号歧义 → 可以放心折叠
            一元节点C { 记号: "!" } 一元 when 试求常量(一元.子) is { } 子 => 子 == 0 ? 1u : 0u,
            一元节点C { 记号: "~" } 一元 when 试求常量(一元.子) is { } 子 => ~子,
            二元节点C 二元 when 试求常量(二元.左) is { } 左 && 试求常量(二元.右) is { } 右 => 试算(二元.记号, 左, 右),
            // 三目:条件折得动就只折**被选中那一支**(没选中的那支不必是常量,也就不会误判"不折")
            三目节点C 三目 when 试求常量(三目.条件) is { } 条 => 条 != 0 ? 试求常量(三目.真) : 试求常量(三目.假),
            _ => null,
        };

        private static uint? 试算(string 记号, uint 左, uint 右) => 记号 switch
        {
            "+" => 左 + 右,
            "-" => 左 - 右,
            "*" => 左 * 右,
            "/" => 右 == 0 ? null : 左 / 右,
            "%" => 右 == 0 ? null : 左 % 右,
            "&" => 左 & 右,
            "|" => 左 | 右,
            "^" => 左 ^ 右,
            "<<" => 左 << (int)(右 & 31),
            ">>" => 左 >> (int)(右 & 31),
            // 逻辑只关心"是不是 0" → 折叠也没有符号歧义(比较符有符号歧义,这里故意不折叠,交给求值那一侧按类型定)
            "&&" => 左 != 0 && 右 != 0 ? 1u : 0u,
            "||" => 左 != 0 || 右 != 0 ? 1u : 0u,
            _ => null,
        };

        /// <summary> 树上所有名字节点(编辑器补全/依赖分析可用) </summary>
        public static IEnumerable<string> 取名字(表达式节点C 节点)
        {
            switch (节点)
            {
                case 名字节点C 名字: yield return 名字.名; break;
                case 一元节点C 一元: foreach (string n in 取名字(一元.子)) yield return n; break;
                case 二元节点C 二元:
                    foreach (string n in 取名字(二元.左)) yield return n;
                    foreach (string n in 取名字(二元.右)) yield return n;
                    break;
                case 调用节点C 调用:
                    foreach (var a in 调用.实参) foreach (string n in 取名字(a)) yield return n;
                    break;
                case 三目节点C 三目:
                    foreach (string n in 取名字(三目.条件)) yield return n;
                    foreach (string n in 取名字(三目.真)) yield return n;
                    foreach (string n in 取名字(三目.假)) yield return n;
                    break;
            }
        }

        /// <summary>
        /// 树深度(生成代码时估临时寄存器压力用;**R2-1 的护栏也用它**)。
        /// **刻意写成迭代的**:它要能对付 5000 层深的树还不自己崩 —— 递归版在这儿就是同一个栈溢出。
        /// 口径与逐层递归一致:叶子 = 1,一元 / 二元 = 1 + 子,调用 = 1 + 最深的实参(没有实参 = 1)。
        /// </summary>
        public static int 深度(表达式节点C 节点)
        {
            int 最深处 = 0;
            var 待看 = new Stack<(表达式节点C 节点, int 深)>();
            待看.Push((节点, 1));
            while (待看.Count > 0)
            {
                var (当前, 深) = 待看.Pop();
                if (深 > 最深处) 最深处 = 深;
                switch (当前)
                {
                    case 一元节点C 一元: 待看.Push((一元.子, 深 + 1)); break;
                    case 二元节点C 二元: 待看.Push((二元.左, 深 + 1)); 待看.Push((二元.右, 深 + 1)); break;
                    case 调用节点C 调用: foreach (var 实参 in 调用.实参) 待看.Push((实参, 深 + 1)); break;
                    case 三目节点C 三目:
                        待看.Push((三目.条件, 深 + 1)); 待看.Push((三目.真, 深 + 1)); 待看.Push((三目.假, 深 + 1)); break;
                }
            }
            return 最深处;
        }

        /// <summary> 把树打印成缩进形式(排查优先级问题时比 显示() 更直观) </summary>
        public static string 打印树(表达式节点C 节点, int 缩进 = 0)
        {
            string 头 = new string(' ', 缩进 * 2);
            var sb = new StringBuilder();
            switch (节点)
            {
                case 常量节点C 常量: sb.AppendLine($"{头}常量 {常量.值}"); break;
                case 名字节点C 名字: sb.AppendLine($"{头}名字 {名字.名}"); break;
                case 字符串节点C 串: sb.AppendLine($"{头}字符串 {串.原文}"); break;
                case 一元节点C 一元:
                    sb.AppendLine($"{头}一元 {一元.记号}");
                    sb.Append(打印树(一元.子, 缩进 + 1));
                    break;
                case 二元节点C 二元:
                    sb.AppendLine($"{头}二元 {二元.记号}");
                    sb.Append(打印树(二元.左, 缩进 + 1));
                    sb.Append(打印树(二元.右, 缩进 + 1));
                    break;
                case 调用节点C 调用:
                    sb.AppendLine($"{头}调用 {调用.名}({调用.实参.Count} 个实参)");
                    foreach (var a in 调用.实参) sb.Append(打印树(a, 缩进 + 1));
                    break;
                case 三目节点C 三目:
                    sb.AppendLine($"{头}三目");
                    sb.Append(打印树(三目.条件, 缩进 + 1));
                    sb.Append(打印树(三目.真, 缩进 + 1));
                    sb.Append(打印树(三目.假, 缩进 + 1));
                    break;
            }
            return sb.ToString();
        }
    }
}
