using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 源码里"**声明过的名字**"的集合(编辑器给它上变量色 / 常量色 / 结构色 / 用户方法色用)。
    /// 只有从**声明**里扫出来的名字才进集合 —— 认不准的宁可不进去(不上色),
    /// 绝不拿"看着像名字"的词乱涂(用户要求:分不清的宁可不上色)。
    /// </summary>
    public sealed class 配色名字集C
    {
        /// <summary> 结构名(含 `方法`/`method` 容器名:引用时也当"类型名"用) </summary>
        public HashSet<string> 结构 { get; } = new(StringComparer.Ordinal);

        /// <summary> 变量名:局部 / 参数 / 全局 / 结构字段(都靠"声明行"扫出来) </summary>
        public HashSet<string> 变量 { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// **用户声明的方法名**(H51):`方法 名(…)` 那种声明,以及容器里的裸成员头(`累加(整数 n)` + 下一行 `{`)。
        /// 单列一档是为了给它上自己的颜色(以前这些名字一个都不上色 —— 用户点名的毛病)。
        /// </summary>
        public HashSet<string> 方法名 { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// **顶层常量名**(H60-13):顶层声明、且初值是字面量那种(`var i = 0` / `整数 表宽 = 4;`)——
        /// 判据只有一句,直接问 `源码声明扫描C.扫结构` 的 `声明项C.是常量` 要,**不在这里重判一遍**
        /// (H60-13 施工图 ③)。实测代价:最大的标准工程 `float64.hb`(2379 行)扫一次约 **2 ms**,
        /// 与这一层原有的十几个正则同一量级。
        /// </summary>
        public HashSet<string> 常量名 { get; } = new(StringComparer.Ordinal);

        public bool 空 => 结构.Count == 0 && 变量.Count == 0 && 方法名.Count == 0 && 常量名.Count == 0;
    }

    /// <summary>
    /// **按源码扫"声明过的名字"**(结构名 + 变量名),给编辑器上色用。
    ///
    /// 为什么不用编译器那份 `名字表C`:`名字表C` 是**整篇编译**才有的旁路信息(还在机器码之后),
    /// 编辑器每敲一个字都编译一遍是不现实的。这里只做**逐行正则扫一遍**(和
    /// `补全词表C.从源码收集名字` 同一套正则,共用同一份"什么算声明"的口径),开销小、够用。
    ///
    /// 覆盖范围(用户点名要的):
    ///   · 变量:带类型的声明(`整数 初值 = 1;`)、函数参数(含 `出`/`result`)、`循环 (整数 i = 0; …)`
    ///     的小声明、全局变量、结构字段(`结构 点 { 整数 x; }` 里的 `x`)。
    ///   · 结构:`结构 点 {` / `struct 点 {`,以及 `方法 棋盘()` / `静态 方法 棋盘(...)` 这类容器名。
    ///
    /// 性能:整篇扫一次。**同一份文本复用上次结果**;两次扫描挨得太近(默认 250ms 内)就用上次那份
    /// (`节流`)—— 用户连着敲字时不会每敲一下就把整篇重扫一遍。文本特别大(> `最大长度`)直接放弃名字上色,
    /// 宁可不给变量上色,也不让编辑卡住。
    /// </summary>
    public static class 源码名字扫描C
    {
        /// <summary> 超过这个长度就不扫了(和 补全词表C 的大文件护栏同一个思路) </summary>
        public const int 最大长度 = 300_000;

        /// <summary> 两次扫描的最小间隔(毫秒):连着敲字时用上一次的结果顶着 </summary>
        public static int 节流毫秒 { get; set; } = 250;

        // ---- "什么算声明"的口径(与 补全词表C 一一对应) ----

        /// <summary> 类型名那一支(基本类型 + `字符串`/`string` + `变量`/`var`),后接名字 </summary>
        private const string 类型支 = @"字节|逻辑|字符|短整数|整数|无符号短整数|无符号整数|文字|变量|var|byte|logic|char|short|int|ushort|uint|rune|字符串|string|addr|地址";

        /// <summary> `循环 (类型 名 = …; …)` 里那个小声明(整行以 `循环`/`for` 开头,变量正则够不着) </summary>
        private static readonly Regex 循环声明正则 = new(
            $@"(?:循环|for)\s*[(（]\s*(?:{类型支})\s+([^\s=;,\[（(]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary> 结构声明(`结构 点 {`) */
        private static readonly Regex 结构声明正则 = new(
            @"^\s*(?:结构|struct)\s+([^\s{（(]+)", RegexOptions.Compiled);

        /// <summary> H68-2 枚举声明(`枚举 颜色 { 赤, 绿, 蓝 }` —— 一行写完)。枚举名与结构名同一档
        /// (它们都是"能当类型名写"的词),成员进**常量那一档**(成员本来就是编译期常量)。 </summary>
        private static readonly Regex 枚举声明正则 = new(
            @"^\s*(?:枚举|enum)\s+([^\s{（(]+)\s*[{(]\s*(.*?)\s*[})]\s*;?\s*$", RegexOptions.Compiled);

        /// <summary> `静态 方法 名(…)` —— 容器名(它同时是**类型名**,归"结构"那一档) </summary>
        private static readonly Regex 容器声明正则 = new(
            @"^\s*(?:静态|static)\s+(?:方法|method)\s+([^\s(（{]+)", RegexOptions.Compiled);

        /// <summary>
        /// H68-3 元组声明:`[整数, 整数] 名字 …` / `[整数 商, 整数 余] 名字[3];` ——
        /// 类型那一截是**方括号**(元素里还能嵌一层元组),取出来的是**变量名**。
        /// 元组类型自己不当"能写的类型名"(它没有标识符形状),所以只收变量。
        /// </summary>
        private static readonly Regex 元组声明正则 = new(
            @"^\s*\[(?:[^\[\]]|\[[^\]]*\])*\](?:[ \t]*\[[^\]]*\])*[ \t]+([^\s=;,\[（(]+)", RegexOptions.Compiled);

        /// <summary> `方法 名(…)` / `method name(…)` —— 用户声明的方法(H51;**不含** `静态` 那种容器) </summary>
        private static readonly Regex 方法声明正则 = new(
            @"^\s*(?:方法|method)\s+([^\s(（{]+)", RegexOptions.Compiled);

        /// <summary>
        /// 容器里的**裸成员头**:`累加(整数 n)` / `当前值() 返回 整数`(不写 `方法` 关键字那种)。
        /// 只在一行长得像这个形状、**下一行正好是 `{`**、而且首词不是关键字时才认(见 `扫描`)。
        /// </summary>
        private static readonly Regex 裸成员头正则 = new(
            @"^\s*(?:(?:私有|private|程序集|internal)\s+)?([^\s(（{]+)\s*\([^()（）]*\)\s*(?:(?:返回|return)\s+[^\s{（(]+)?$",
            RegexOptions.Compiled);

        /// <summary> `函数 名(类型 参数, 字符串 出 结果, 整数 表[]) 返回 类型 {` —— 只取参数表那一段 */
        private static readonly Regex 函数头正则 = new(
            @"^\s*(?:函数|func)\s+[^\s(（]+\s*[(（]([^)）]*)", RegexOptions.Compiled);

        /// <summary> 参数表里的一节:`[出|result] 类型 名字` */
        private static readonly Regex 参数正则 = new(
            $@"(?:^|[,，、\s])(?:出\s+|result\s+)?(?:{类型支})\s+([^\s=;,\[（()）]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary> 扫一篇源码,得到"声明过的名字"集合(超长文本返回空集 —— 放弃名字上色) </summary>
        public static 配色名字集C 扫描(string? 源码)
        {
            var 集 = new 配色名字集C();
            if (string.IsNullOrEmpty(源码) || 源码.Length > 最大长度) return 集;

            // 先取"有内容的行"(去掉行尾注释、跳过整行注释)
            var 行表 = new List<string>();
            foreach (string 原行 in 源码.Split('\n'))
            {
                string 行 = 去行尾注释(原行);
                if (行.Length == 0) continue;
                string 修剪 = 行.TrimStart();
                if (修剪.StartsWith("//") || 修剪.StartsWith("/*") || 修剪.StartsWith("*")) continue;
                行表.Add(行);
            }

            // 第一遍:结构名(含 `静态 方法` 容器)+ **用户声明的方法名**(H51)。后面认"结构类型的变量"要用到结构名
            // (只认基本类型是不够的:`点 位置 = …;` 里 `位置` 也是变量,用户要看它有颜色)
            for (int i = 0; i < 行表.Count; i++)
            {
                string 行 = 行表[i];
                if (结构声明正则.Match(行) is { Success: true } 结构匹配)
                {
                    收名(集.结构, 结构匹配.Groups[1].Value);
                    continue;
                }
                // H68-2 枚举:名字归"结构"那一档(类型色),成员归"常量"那一档(`绿` 就是个常量)
                if (枚举声明正则.Match(行) is { Success: true } 枚举匹配)
                {
                    收名(集.结构, 枚举匹配.Groups[1].Value);
                    foreach (string 段 in 枚举匹配.Groups[2].Value.Split(','))
                    {
                        int 等号 = 段.IndexOf('=');
                        收名(集.常量名, 等号 >= 0 ? 段[..等号] : 段);
                    }
                    continue;
                }
                // `静态 方法 名(…)` = **容器名**(它同时是类型名)⇒ 归"结构"那一档
                if (容器声明正则.Match(行) is { Success: true } 容器匹配)
                {
                    收名(集.结构, 容器匹配.Groups[1].Value);
                    continue;
                }
                // `方法 名(…)` = 用户声明的方法(顶层过程 / 容器成员 / 结构方法)⇒ 归新的"用户方法名"那一档
                if (方法声明正则.Match(行) is { Success: true } 方法匹配)
                {
                    收名(集.方法名, 方法匹配.Groups[1].Value);
                    continue;
                }
                // 容器里的**裸成员头**(`累加(整数 n)` / `当前值() 返回 整数`,不写 `方法` 关键字):
                // 判据 = 这一行长得像 `名(参数) [返回 类型]`、下一行正好是 `{`,而且首词不是关键字
                // (不然 `循环 (…)`、`如果 (…)` 这种也会被当成方法名)
                if (i + 1 < 行表.Count && 行表[i + 1].Trim() == "{" && 裸成员头正则.Match(行) is { Success: true } 裸匹配
                    && 中间层指令C.到英文关键字(裸匹配.Groups[1].Value) == null)
                    收名(集.方法名, 裸匹配.Groups[1].Value);
            }

            // 第二遍:变量。类型那一支 = 基本类型 + `字符串`/`string` + **本文件声明的结构名**
            // (结构名**排过序**再拼:同一份集合永远拼出同一个模式文本 ⇒ 正则缓存稳定命中)
            string 类型支全 = 集.结构.Count == 0
                ? 类型支
                : $"{类型支}|{string.Join("|", 集.结构.OrderBy(x => x, StringComparer.Ordinal).Select(Regex.Escape))}";
            var (变量正则, 循环正则) = 变量扫描正则(类型支全);

            foreach (string 行 in 行表)
            {
                if (函数头正则.Match(行) is { Success: true } 函数匹配)
                    foreach (Match 参 in 参数正则.Matches(函数匹配.Groups[1].Value))
                        收名(集.变量, 参.Groups[1].Value);

                if (变量正则.Match(行) is { Success: true } 变量匹配) 收名(集.变量, 变量匹配.Groups[1].Value);
                if (循环正则.Match(行) is { Success: true } 循环匹配) 收名(集.变量, 循环匹配.Groups[1].Value);
                // H68-3 元组声明的变量(`[整数, 整数] 结果 = …;` / `[整数, 整数] 表[3];`)
                if (元组声明正则.Match(行) is { Success: true } 元组匹配) 收名(集.变量, 元组匹配.Groups[1].Value);
            }

            集.变量.RemoveWhere(x => x.Length == 0);
            集.结构.RemoveWhere(x => x.Length == 0);
            集.常量名.RemoveWhere(x => x.Length == 0);

            // H60-13:常量那一档。判据只有一句(顶层声明 + 初值是字面量)⇒ 复用 `源码声明扫描C.扫结构`,
            // 不在扫描器里重判一遍(施工图 ③)。
            foreach (var 常 in 源码声明扫描C.扫结构(源码).变量们)
                if (常.是常量) 收名(集.常量名, 常.名);
            集.常量名.RemoveWhere(x => x.Length == 0);
            // 同一个名字两种身份时(示例06 的 `i`:顶层 `var i = 0` 是常量,方法里又有同名局部)——
            // **常量那一档优先**(用户要的就是"常量看得见";这一层是**按行扫**的,分不出作用域)。
            // 全 `Sample\**` 46 份 .hb 里只有这 1 个名字会这样(实测,见第268批的记录)。
            集.变量.ExceptWith(集.常量名);

            // 同一个名字既是方法名又是变量名时,变量那一档优先(以前的观感就是这个,别抖)
            集.方法名.ExceptWith(集.变量);
            return 集;
        }

        /// <summary> 收一个名字(去掉数组后缀 `[]`、去掉泛型括号;太短太怪的不收) </summary>
        private static void 收名(HashSet<string> 集, string 原名)
        {
            string 名 = 原名.Trim().TrimEnd('[', ']', '(', ')', '{', '}');
            if (名.Length == 0) return;
            if (!名.All(c => char.IsLetterOrDigit(c) || c == '_')) return;   // 带点/带符号的不是简单名字
            if (char.IsDigit(名[0])) return;
            集.Add(名);
        }

        /// <summary> 去掉行尾的 `// 注释` 再匹配(注释里写的声明不算声明) </summary>
        private static string 去行尾注释(string 行)
        {
            int i = 行.IndexOf("//", StringComparison.Ordinal);
            return i < 0 ? 行 : 行[..i];
        }

        // ---------------- T30①:变量扫描那两个正则**只造一次** ----------------

        /// <summary> 已造好的 (变量正则, 循环正则),按"类型支全"这份模式文本缓存(小的 LRU) </summary>
        private static readonly object _正则锁 = new();
        private static readonly Dictionary<string, (Regex 变量, Regex 循环)> _正则缓存 = new(StringComparer.Ordinal);
        private static readonly Queue<string> _正则顺序 = new();

        /// <summary>
        /// 取"变量 / 循环声明"那一对正则。**T30(2026-09-27 评测整改)**:
        /// 以前这两条是**每次扫描现造**(`new Regex(…, RegexOptions.Compiled)`)—— `RegexOptions.Compiled`
        /// 的编译代价是实打实的,**1 行输入也要付 ~8~10 ms 的地板价**(实测,见
        /// `工具\临时\T30\`);而类型支全只由"这篇源码里声明过的结构名"决定,大多数文稿彼此相同
        /// —— 缓存 8 份就够,命中即零成本(与 `补全词表C` / 检查器 H28 那两处的思路一致)。
        /// 模式文本里结构名是 `Regex.Escape` 过的,排序后再拼 ⇒ 同一份结构集合永远命中同一条。
        /// </summary>
        private static (Regex 变量, Regex 循环) 变量扫描正则(string 类型支全)
        {
            lock (_正则锁)
            {
                if (_正则缓存.TryGetValue(类型支全, out var 已有)) return 已有;
                var 一对 = (
                    new Regex($@"^\s*(?:{类型支全})\s+([^\s=;,\[（(]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
                    new Regex($@"(?:循环|for)\s*[(（]\s*(?:{类型支全})\s+([^\s=;,\[（(]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase));
                if (_正则缓存.Count >= 8)
                {
                    string 最老 = _正则顺序.Dequeue();
                    _正则缓存.Remove(最老);
                }
                _正则缓存[类型支全] = 一对;
                _正则顺序.Enqueue(类型支全);
                return 一对;
            }
        }

        // ---------------- 带节流的缓存 ----------------

        private static readonly object _锁 = new();
        private static string? _上次文本;
        private static 配色名字集C? _上次结果;
        private static DateTime _上次时刻 = DateTime.MinValue;

        /// <summary>
        /// 取这篇源码的名字集(带缓存 + 节流)。同一份文本直接复用;
        /// `节流毫秒` 内换了文本就先给上一次那份(下次再来就重扫)。
        /// </summary>
        public static 配色名字集C 取(string? 源码, DateTime 现在)
        {
            string 文本 = 源码 ?? "";
            lock (_锁)
            {
                if (文本.Length == 0) return _上次结果 ??= new 配色名字集C();
                if (string.Equals(文本, _上次文本, StringComparison.Ordinal) && _上次结果 != null) return _上次结果;
                if (_上次结果 != null && (现在 - _上次时刻).TotalMilliseconds < 节流毫秒) return _上次结果;
                _上次文本 = 文本;
                _上次时刻 = 现在;
                return _上次结果 = 扫描(文本);
            }
        }

        /// <summary> 清掉缓存(自检用:免得上一段的缓存串到下一段) </summary>
        public static void 清缓存()
        {
            lock (_锁) { _上次文本 = null; _上次结果 = null; _上次时刻 = DateTime.MinValue; }
        }
    }
}
