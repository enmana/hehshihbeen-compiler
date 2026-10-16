using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary> 中间层展开器的一部分:编译期优化:循环不变量与常量提升(纯编译期,ISA 一个字不改)。(第88批从 `中间层C.cs` 按 region 原样搬出,逻辑一字未改) </summary>
    internal partial class 中间层展开器
    {
        #region 编译期优化:循环不变量/常量提升(纯编译期;ISA 一个字不改)
        private static readonly Regex 数字字面量正则 = new(@"(?<![0-9A-Za-z_\u4E00-\u9FFF\u3400-\u4DBF])(?:0[xX][0-9A-Fa-f]+|\d+)", RegexOptions.Compiled);
        private static readonly Regex 标识符提及正则 = new(@"(?<![0-9A-Za-z_\u4E00-\u9FFF\u3400-\u4DBF])([\p{L}_][\p{L}\p{N}_]*)", RegexOptions.Compiled);

        /// <summary> **模块限定调用**的样子:`模块.函数(` / `方法容器.名(`(两个标识符夹一个点,点后面紧跟左括号) </summary>
        private static readonly Regex 模块限定调用正则 = new(
            @"(?<![A-Za-z0-9_.])([\p{L}_][\p{L}\p{N}_]*)\s*\.\s*([\p{L}_][\p{L}\p{N}_]*)\s*\(",
            RegexOptions.Compiled);

        /// <summary> 位置指向 '{' 时返回配对 '}' 的下标(只读扫描,不改展开位置)</summary>
        private int 找块结束(int 位置)
        {
            int 深度 = 0;
            for (int i = 位置; i < _伪行表.Count; i++)
            {
                if (_伪行表[i] == "{") 深度++;
                else if (_伪行表[i] == "}" && --深度 == 0) return i;
            }
            return _伪行表.Count - 1;
        }

        /// <summary> 这一行是赋值/自增/自减的话,返回被写的基名(字段/下标都归到最外层变量);不是赋值返回 null </summary>
        private string? 被写名字(string 行)
        {
            string 内容 = 行.Trim();
            if (内容.Length == 0) return null;
            string 前段;
            if (内容.EndsWith("++") || 内容.EndsWith("--")) 前段 = 内容[..^2].Trim();
            else
            {
                int 等号 = 内容.IndexOf('=');
                if (等号 <= 0) return null;
                if (内容[等号 - 1] is '<' or '>' or '!' or '=') return null;      // <= >= != == 不是赋值
                if (等号 + 1 < 内容.Length && 内容[等号 + 1] == '=') return null;
                前段 = 内容[..等号].Trim();
            }
            if (前段.Length == 0 || 解析寄存器(前段, out _)) return null;
            string 名 = 取首词(前段);
            return 名.Length > 0 ? 名 : null;
        }

        /// <summary>
        /// **会往调用方变量里写东西的内建函数**(宿主能力 / 图形原语那一档)。
        /// 循环不变量提升碰上它们要**整段不提**:它们的写是"通过这个名字的地址"发生的,
        /// 静态的"体里谁被写了"看不出来 —— 例子:`取事件(类型, 键码, x, y)` 每一轮都把四个变量重写一遍。
        ///
        /// 实测(R1-6 批 1 抓到的):`当 (取事件(类型, 键码, mx, my)) { 测试输出(类型); … }` 里
        /// `类型 / 键码 / mx` 被当成循环不变量提到循环外只读一次 ⇒ 三轮全打 0 ——
        /// 优化开/关跑出来两个结果(自检那条"优化 全示例双跑一致"没覆盖到,这就是补的那道闸)。
        /// 加新的"带 `出` 参数"的宿主能力时,**这个名字要一起加进来**。
        /// </summary>
        private static readonly string[] 会写变量的内建名 =
        [
            "取事件", "pollevent",                                                   // 四个出参
            "读文件", "readfile", "列目录", "listdir", "环境变量", "getenv",           // 第二个实参是目标串
            "读文件块", "readchunk", "取网址", "fetch", "运行进程", "runprocess",      // 同上
            "读一块", "readblock",                                                      // G3 流:第二个实参是目标串
            "接收", "recv", "主机解析", "resolve",                                      // G3 网络:第二个实参是目标串
            "接收块", "recvblock",                                                       // G3 网络:往"块地址"那块内存里写(静态看不出别名)⇒ 与其他间接写同档
            "临时目录", "tempdir",                                                       // G3 运行时:第一个实参是目标串
            "发起异步", "startasync", "任务就绪", "taskready",                            // G3 域 5:任务号 / 状态 / 字节数都是"通过名字的地址"写的
        ];

        /// <summary>
        /// **会展开成"用户函数调用"的内建名**(第426批)。
        ///
        /// 为什么单列一张:循环不变量提升把不变量放进**常驻寄存器**常驻一整个循环体,它的前提是
        /// "这一轮里没人动这些寄存器" —— 所以体里只要有**真发 CALL 的写法**,这一段就整段不提。
        /// 而原来那一关只认**源码里看得见的**调用(`名字(` 在函数表里,或 `模块.名字(`;见
        /// `循环体里拦住优化`)。`格式化("{0}", 5)` 这种写法在源码里**看不出是调用**:它是内建,
        /// 要到拆占位符时才现造一句 `转字符串(5)`(见 `中间层C.拼接到与格式化.cs` 的 `发出格式化`),
        /// 而 `转字符串` 是 `库\string.hb` 里的**用户函数**。
        /// ⇒ 于是循环体被判成"这一轮没有调用",不变量提进常驻寄存器,`CALL 转字符串` 一回来就把它冲掉:
        /// **同一份源码优化开 / 关跑出两个结果**。实测最小复现(3 行):
        /// `整数 复 = 0; 当 (复 < 6) { 暂 = 格式化("{0}", 5); 复 = 复 + 1; }`
        /// ⇒ 优化开 `复 = 1`、优化关 `复 = 6`;把占位符换成**字符串**(`格式化("{0}", "文字")`)就正常
        /// —— 因为那一路不需要 `转字符串`、不发 CALL。这正是"危害来自隐式调用"的实证。
        ///
        /// 代价与取舍:占位符是字符串时其实不发 CALL,但"这次要不要 `转字符串`"得把格式串拆开才看得出,
        /// **静态判不准** ⇒ 一律按"有调用"办(与这个文件通篇的口径一致:宁可少提,不许提错)。
        /// **以后再有"内建展开成用户函数调用"的写法,名字要加进这一张表**(同 `会写变量的内建名`)。
        /// </summary>
        private static readonly string[] 会展开成调用的内建名 = ["格式化", "format"];

        /// <summary> 这段循环体里有没有"拦住优化"的东西:调用 / 别名 / 用户寄存器 / 列表动作 </summary>
        private bool 循环体里拦住优化(string 文本)
        {
            if (文本.Contains("地址(") || 文本.Contains("address(")) return true;      // 取地址 → 别名可能改到别的名字
            if (文本.Contains("引用") || Regex.IsMatch(文本, @"(?<![A-Za-z0-9_])ref\s")) return true;
            if (文本.Contains("存字间接") || 文本.Contains("STORE_WORD_INDIRECT")) return true;
            if (中间层指令C.含方法动作除长度(文本)) return true;   // 列表/字符串动作(`.添加`/`.add`);`.长度()` 只读,放行(S12)
            // 会写调用方变量的内建(宿主能力 / 图形原语):写是"通过名字的地址"发生的,静态看不出来 ⇒ 整段不提
            foreach (string 名 in 会写变量的内建名)
                if (Regex.IsMatch(文本, $@"(?<![A-Za-z0-9_]){Regex.Escape(名)}\s*\(", RegexOptions.IgnoreCase))
                    return true;
            // 会展开成"用户函数调用"的内建(`格式化` 的数字占位符要过 `转字符串`):源码里看不出是调用 ⇒ 整段不提
            foreach (string 名 in 会展开成调用的内建名)
                if (Regex.IsMatch(文本, $@"(?<![A-Za-z0-9_]){Regex.Escape(名)}\s*\(", RegexOptions.IgnoreCase))
                    return true;
            // 堆:申请/释放 会 CALL 分配/释放例程(常驻寄存器会被冲掉),取字/存字 是间接读写(别名)——
            // 有堆动作就整段不提(宁可少提,不许提错)
            if (堆调用正则.IsMatch(文本)
                || 文本.Contains("释放(") || 文本.Contains("free(")
                || 文本.Contains("存字(") || 文本.Contains("storeword(")) return true;
            if (文本.Contains("调用") || Regex.IsMatch(文本, @"(?<![A-Za-z0-9_])(call)\s")) return true;
            if (寄存器提及正则.IsMatch(文本)) return true;                              // 用户自己写寄存器:不跟他抢
            // 用户函数调用:被调方可能改全局、也会用掉这些常驻寄存器(测试输出/停止 这类内置不算,它们不 CALL)
            foreach (Match m in 标识符提及正则.Matches(文本))
            {
                string 名 = m.Groups[1].Value;
                int 后 = m.Index + m.Length;
                if (后 < 文本.Length && 文本[后] == '(' && 是用户函数名(名)) return true;
            }
            // **模块限定调用**(`模块.函数(…)` / `方法容器.名(…)`):上面那一步只认**裸名字**,
            // 而括号前面那一段是带着点的 ⇒ 它一个都认不出来。
            // 漏掉它的后果实测过(第344批,G4 批 1 写 `位.hb` 时撞上的):循环体里
            // `三 = 位.置位(三, t);` 被当成"这一轮没有调用",比较用的两个常量被提到常驻寄存器
            // (汇编里是 `LUI R1 … / OR_IMMEDIATE R1 0` 提到 `_fWhileStart` 前面),而 `CALL 位_置位`
            // 回来会把 R1 / R2 冲掉 ⇒ 第二轮判条件拿垃圾比,循环提前退出:
            // 优化开 `3 / 2`,优化关 `127 / 7` —— **同一份源码两个结果**(那正是"优化开/关双跑一致"要抓的)。
            // 认法:限定名 `前.后` 在函数表里,或某个方法容器的定义名就是它 —— 认不出就照"有调用"办。
            foreach (Match m in 模块限定调用正则.Matches(文本))
            {
                string 全名 = m.Groups[1].Value + "." + m.Groups[2].Value;
                if (_函数表.ContainsKey(全名)) return true;
                foreach (var 容器 in _容器表.Values)
                    if (容器.方法全名.TryGetValue(m.Groups[2].Value, out string? 定义名) && 定义名 == 全名)
                        return true;
            }
            return false;
        }

        private bool 是用户函数名(string 名)
        {
            if (_函数表.ContainsKey(名)) return true;
            foreach (var 容器 in _容器表.Values)
                if (容器.方法全名.ContainsKey(名)) return true;
            // 模块里的裸名字(拦截优化 / 认堆动作 都靠它:认不出就会把用户函数当内置处理)
            return _当前模块.Length > 0 && !名.Contains('.') && _函数表.ContainsKey(_当前模块 + "." + 名);
        }

        // ---------------- P1 数组地址增量(归纳变量步进) ----------------

        /// <summary> 源码里 `名字[下标]` 的样子(下标里不带方括号;嵌套下标由更里面那一层自己匹配)。
        /// 名字前面不能是 `.`:`记录.单位[i]` 那种字段数组不归这条管(只认顶层数组变量)。 </summary>
        private static readonly Regex 数组下标访问正则 = new(@"(?<![A-Za-z0-9_.])([\p{L}_][\p{L}\p{N}_]*)\s*\[([^\[\]]+)\]", RegexOptions.Compiled);

        /// <summary> S12:`名字.长度()`(中英方法名都认)—— 循环里反复读同一个字符串的字数,值得在循环外数一次 </summary>
        private static readonly Regex 长度调用正则 = new(@"(?<![A-Za-z0-9_.])([\p{L}_][\p{L}\p{N}_]*)\s*\.\s*(?:长度|length)\s*\(\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ---- A7(第106批):把"改字符串内容"的靶子从写法里取出来,好只废掉被写的那个串 ----
        /// <summary>`拼接到(目标, …)` 的目标(第一个实参必须是字符串变量)</summary>
        private static readonly Regex 拼接到目标正则 = new(@"(?<![0-9A-Za-z_\u4E00-\u9FFF\u3400-\u4DBF])(?:拼接到|concat)\s*\(\s*([\p{L}_][\p{L}\p{N}_]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        /// <summary>`读取行(目标)` 的目标(读一整行写进它)</summary>
        private static readonly Regex 读取行目标正则 = new(@"(?<![0-9A-Za-z_\u4E00-\u9FFF\u3400-\u4DBF])(?:读取行|readline)\s*\(\s*([\p{L}_][\p{L}\p{N}_]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        /// <summary>`读文件(路径, 目标, 上限)` 的目标(第二个实参)</summary>
        private static readonly Regex 读文件目标正则 = new(@"(?<![0-9A-Za-z_\u4E00-\u9FFF\u3400-\u4DBF])(?:读文件|readfile)\s*\(\s*[^,()]*,\s*([\p{L}_][\p{L}\p{N}_]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        /// <summary> 这三种写法的**总出现次数**:认出的目标数比它少 → 说明有认不出的,退回"整段不缓存" </summary>
        private static readonly Regex 改串出现正则 = new(@"(?<![0-9A-Za-z_\u4E00-\u9FFF\u3400-\u4DBF])(?:拼接到|concat|读取行|readline|读文件|readfile)(?![A-Za-z0-9_])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// 认循环头的步进是不是"归纳变量每轮 +常量":`i = i + 1` / `i = i + 2` / `i++`。
        /// 认不出(或步长为 0/负数/大得离谱)就返回 null —— 认不出就什么都不做,照原样展开。
        /// </summary>
        private static (string 变量, int 步长)? 解析归纳步进(string 步进)
        {
            string s = 步进.Trim();
            if (s.Length == 0) return null;
            if (s.EndsWith("++", StringComparison.Ordinal))
            {
                string 名 = s[..^2].Trim();
                return 标识符全词正则.IsMatch(名) ? (名, 1) : null;
            }
            int 等号 = s.IndexOf('=');
            if (等号 <= 0) return null;
            if (s[等号 - 1] is '<' or '>' or '!' or '=') return null;              // <= >= != ==
            if (等号 + 1 < s.Length && s[等号 + 1] == '=') return null;            // += 之类:本语言不认,别乱认
            string 左 = s[..等号].Trim();
            string 右 = s[(等号 + 1)..].Trim();
            if (!标识符全词正则.IsMatch(左)) return null;
            int 加 = 右.IndexOf('+');
            if (加 <= 0) return null;
            if (!string.Equals(右[..加].Trim(), 左, StringComparison.OrdinalIgnoreCase)) return null;
            if (!uint.TryParse(右[(加 + 1)..].Trim(), out uint 步) || 步 == 0 || 步 > 64) return null;   // 步子太大就不提(不值得)
            return (左, (int)步);
        }

        /// <summary>
        /// 循环初始化里那个归纳变量的**常量初值**(`整数 i = 0` / `var i = 1` / `i = 0`);
        /// 不是常量(或者名字对不上)返回 null —— 那就按运行期算一次指针起点(循环外只做一次,也划算)。
        /// </summary>
        private static uint? 解析初始化常量(string 初始化, string 变量名)
        {
            string s = 初始化.Trim();
            int 等号 = s.IndexOf('=');
            if (等号 <= 0) return null;
            if (s[等号 - 1] is '<' or '>' or '!' or '=') return null;
            string 左 = s[..等号].Trim();
            var 名们 = 标识符提及正则.Matches(左);
            if (名们.Count == 0 || !string.Equals(名们[^1].Value, 变量名, StringComparison.OrdinalIgnoreCase)) return null;
            return 解析无符号32(s[(等号 + 1)..].Trim(), out uint 值) ? 值 : null;
        }

        /// <summary> 循环体里有没有嵌套的循环 —— 有就整段不提地址增量(锁定决策:嵌套一律不提)</summary>
        private static bool 体里有嵌套循环(IReadOnlyList<string> 文本行)
        {
            foreach (string 行 in 文本行)
            {
                string 首 = 取首词(行.Trim());
                if (首 is "循环" or "当" or "遍历") return true;
                if (string.Equals(首, "for", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(首, "while", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(首, "foreach", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary> 条件就是"变量 &lt; 常量"这一种(窄计数器的回绕判断只认这种最干净的写法)</summary>
        private static readonly Regex 简单上界正则 = new(@"^\s*([\p{L}_][\p{L}\p{N}_]*)\s*<\s*(0[xX][0-9A-Fa-f]+|\d+)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// 这个归纳变量能不能给指针:指针每轮走 元素宽×步长,而**下标是按计数器算的** ——
        /// 只要计数器回绕(窄类型:255+1 → 0),两边就对不上,地址会指错。
        /// 判据:① 4 字节计数器(现实中回绕不了)随便;② 窄计数器(字节/短整数)只在
        /// "初值与上界都是常量、上界 + 步长 - 1 不超过这个类型能表示的最大值"时才安全(这样一轮都不会走到回绕点)。
        /// </summary>
        private bool 归纳指针安全(string 变量名, int 步长, uint? 初值, string 条件)
        {
            if (查变量(变量名) is not { } 绑定) return false;
            int 宽 = 绑定.实际声明宽;
            if (宽 >= 4) return true;                                  // 32 位:要到 40 亿次才回绕,不操心
            if (初值 is not { } 初) return false;                      // 窄计数器:初值必须是常量,好跟界比
            var m = 简单上界正则.Match(条件);
            if (!m.Success || !string.Equals(m.Groups[1].Value, 变量名, StringComparison.OrdinalIgnoreCase)) return false;
            if (!解析无符号32(m.Groups[2].Value, out uint 界)) return false;
            if (初 >= 界) return false;                                // 一次都不跑:不提(省几条初始化)
            uint 上限 = 绑定.有符号
                ? (1u << (宽 * 8 - 1)) - 1
                : (1u << (宽 * 8)) - 1;
            return 界 + (uint)步长 - 1 <= 上限;
        }

        /// <summary> 体里(不含条件与步进)有没有再改这个归纳变量 —— 改了指针就与下标脱钩,一律不提</summary>
        private bool 体里改过归纳变量(IReadOnlyList<string> 体行, string 变量名)
        {
            foreach (string 行 in 体行)
                if (string.Equals(被写名字(行), 变量名, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// H4:下标文本 = 归纳变量本身(`表[i]`,偏移 0)还是 `归纳变量 ± 十进制常量`(`表[i + 1]` / `表[i-1]`)。
        /// 返回的偏移单位是**元素个数**;认不出(乘、括号、另一个变量、十六进制、`i1` 这种同前缀的别的名字)给 null
        /// —— **认不出就不提**(宁可少提,不许提错)。
        /// </summary>
        private static int? 解析下标偏移(string 索引文本, string 归纳变量)
        {
            string s = 索引文本.Trim();
            if (string.Equals(s, 归纳变量, StringComparison.OrdinalIgnoreCase)) return 0;
            if (s.Length <= 归纳变量.Length) return null;
            if (!s.StartsWith(归纳变量, StringComparison.OrdinalIgnoreCase)) return null;
            string 余 = s[归纳变量.Length..].TrimStart();
            if (余.Length == 0) return null;
            if (char.IsLetterOrDigit(余[0]) || 余[0] == '_') return null;         // `i1` / `i表` 不是同一个变量
            if (余[0] is not ('+' or '-')) return null;
            if (!int.TryParse(余[1..].Trim(), out int 常)) return null;           // 只认十进制常量(认不出就不提)
            return 余[0] == '-' ? -常 : 常;
        }

        /// <summary>
        /// 带偏移的指针初值 = 基址 + (初值 + 偏移) × 元素宽。落不到实地的一律不提:
        /// ① 算出来指到数组前面去(初值 + 负偏移 &lt; 0)② 偏移那一截装不进立即数(`偏移 × 元素宽 &gt; 0xFFFF`)
        /// ③ 初值不是常量时只认非负偏移(负的要一条减法,不值当)。偏移 0 照旧全放行。
        /// </summary>
        private static bool 偏移初值合法(uint? 初值, int 下标偏移, int 元素宽)
        {
            if (下标偏移 == 0) return true;
            if ((long)Math.Abs((long)下标偏移) * 元素宽 > 0xFFFF) return false;
            if (初值 is { } 初) return (long)初 + 下标偏移 >= 0;
            return 下标偏移 > 0;
        }

        /// <summary>
        /// 挑出"这个循环里值得用步进指针访问"的数组:体里有 `数组[归纳变量]`(H4 起也认 `数组[归纳变量 ± 常量]`),
        /// 数组是**定长数组**、元素宽 1/2/4、不是结构元素、**体里没写过它**(锁定决策:写数组/别名一律不提)。
        /// </summary>
        private List<步进指针项C> 找步进数组候选(IReadOnlyList<string> 文本行, string 全部, 归纳信息C 归)
        {
            var 结果 = new List<步进指针项C>();
            if (体里有嵌套循环(文本行)) return 结果;
            var 写过 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string 行 in 文本行)
                if (被写名字(行) is { } 名) 写过.Add(名);
            var 见过 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in 数组下标访问正则.Matches(全部))
            {
                string 数组名 = m.Groups[1].Value;
                // H4:`表[i + 2]` 也算候选(常量折进指针初值,热路径不多花一条);偏移不同算两个候选
                if (解析下标偏移(m.Groups[2].Value, 归.变量) is not { } 下标偏移) continue;
                if (!见过.Add($"{数组名}[{下标偏移}]")) continue;
                if (写过.Contains(数组名)) continue;                       // 体里写过这个数组 → 不提
                if (查变量(数组名) is not { } 绑定) continue;
                if (!绑定.是数组 || 绑定.是列表 || 绑定.是引用 || 绑定.是数组切片 || 绑定.是结构) continue;
                if (绑定.数组类型 is not { 元素结构: null, 长度: > 0 } 数组) continue;
                if (数组.是多维) continue;                                 // H68-1:多维数组不提步进指针(地址是多维累加)
                if (数组.元素宽 is not (1 or 2 or 4)) continue;
                // P12 起:4 字节元素的**读**由一条 `取字索引` 搞定(比"步进指针 + 每轮加一下"还省),
                // 所以这一类不再提步进指针 —— 免得白花一个常驻寄存器与一段循环外初始化(实测 200 轮下来
                // 反而是不提更省:3610 条 vs 4009 条)。1/2 字节元素照旧提(它们没有"索引读"这条指令)。
                if (数组.元素宽 == 4) continue;
                if (绑定.槽.宽度 != 数组.总宽度) continue;                 // 槽和数组类型对不上(不该发生):不碰
                if (!偏移初值合法(归.初值, 下标偏移, 数组.元素宽)) continue;
                结果.Add(new 步进指针项C(数组名, 归.变量, 数组.元素宽, "", 绑定.槽.名称, 下标偏移));
            }
            return 结果;
        }

        /// <summary>
        /// 这是一个**已经在跑的步进指针**吗?对得上就把寄存器给调用方当"元素地址"用。
        /// H4 起也认带常量偏移的写法:`表[i + 1]` 找的是"偏移 1 那一个指针"(`表[i]` 与它各一个,
        /// 常量折在各自指针初值里,热路径一条都不多花)。常量偏移(`链.偏移`,即数组外面那层字段偏移)
        /// 照旧一律不提 —— 那一位要另加一条,不值当。
        /// </summary>
        private string? 步进指针(string 数组名, string 索引文本, int 元素宽, int 常量偏移)
        {
            if (!_优化开 || 常量偏移 != 0) return null;
            for (int i = _循环保留栈.Count - 1; i >= 0; i--)
                foreach (var 项 in _循环保留栈[i].步进指针)
                {
                    if (项.元素宽 != 元素宽) continue;
                    if (解析下标偏移(索引文本, 项.索引名) is not { } 下标偏移 || 下标偏移 != 项.下标偏移) continue;
                    if (string.Equals(项.数组名, 数组名, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(项.槽名, 数组名, StringComparison.OrdinalIgnoreCase))
                        return 项.寄存器;
                }
            return null;
        }

        /// <summary> 基址到**指定寄存器**:帧内变量/数组 → FP+偏移;全局 → 取地址 标签 </summary>
        private void 输出基址到(string 寄存器, string 槽名)
        {
            if (解析帧槽(槽名, out int 帧偏移)) 发出($"取地址帧 {寄存器} {帧偏移}");
            else 发出($"取地址 {寄存器} {槽名}");
        }

        /// <summary>
        /// 步进指针的初值(循环外面只做一次):指针 = 数组基址 + (归纳变量当前值 + 下标偏移) × 元素宽。
        /// 归纳变量是常量初值(常见:`i = 0`)就直接把偏移并进立即数,省掉一次取值与缩放;
        /// H4 的下标偏移(`表[i + 2]`)也并在这一步里 —— 所以带偏移的访问每轮**一条都不多花**。
        /// </summary>
        private void 输出步进指针初值(步进指针项C 项, 归纳信息C 归, int 行号)
        {
            if (归.初值 is { } 初)
            {
                long 元素位 = 初 + 项.下标偏移;
                ulong 偏移 = (ulong)元素位 * (ulong)项.元素宽;
                if (偏移 <= 0xFFFF)
                {
                    输出基址到(项.寄存器, 项.槽名);
                    if (偏移 > 0) 发出($"ADD_IMMEDIATE {项.寄存器} {偏移}");
                    return;
                }
            }
            // 初值不是常量(`循环 (; i < 8; i = i + 1)` 这种):按运行期算一次 —— 循环外做一次,不影响热路径
            string 下标 = 索引到寄存器(归.变量, 行号, 归.变量);
            缩放下标(下标, 项.元素宽);
            输出基址到(项.寄存器, 项.槽名);
            发出($"ADD {项.寄存器} {下标}");
            // 初值非常量时偏移只能是正的(挑候选时保证过):补一条立即数,热路径照旧不受影响
            if (项.下标偏移 > 0) 发出($"ADD_IMMEDIATE {项.寄存器} {(uint)(项.下标偏移 * 项.元素宽)}");
        }

        /// <summary>
        /// 循环不变量提升:把"每一轮都重新读、但这一轮里不会变"的东西提到循环外面只做一次 ——
        /// 变量占一个常驻寄存器、常量也占一个,循环体里直接用寄存器(不再访存、不再重复装载)。
        /// 只在有把握时提:体里出现 调用/取地址/引用/间接写/列表动作/用户寄存器/变量声明 → 整段不提(宁可少提,不许提错)。
        /// 另外(K3 余量 P1):认出归纳变量时,把"每轮 `数组[归纳变量]`"也提成**步进指针**(比不变量值钱,先分寄存器)。
        /// </summary>
        private 循环保留C? 提升循环不变量(IReadOnlyList<string> 文本行, int 行号, 归纳信息C? 归纳 = null, int 条件行数 = 0)
        {
            if (!_优化开) return null;
            var 全部 = string.Join('\n', 文本行);
            if (全部.Trim().Length == 0) return null;
            if (循环体里拦住优化(全部)) return null;

            var 被写 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string 行 in 文本行)
            {
                if (是声明行(行)) return null;                 // 体里声明变量:作用域一变容易提错,直接不提
                if (被写名字(行) is { } 名) 被写.Add(名);
            }

            // S12:字符串 `.长度()` 不是读一个长度字 —— 它要按 UTF-8 **数一遍文字**(一次约 40 条指令)。
            //   循环体/条件里反复读同一个字符串的字数、体里又没写过它时,先在循环外数一次,整段循环用常驻寄存器。
            //   (能走到这儿已经过了两关:体里没有调用/别名/列表动作、也没在体里声明变量)
            // 这一段循环里"会改字符串内容"的写法:`拼接到` / `读取行` / `读文件` 都会写进串里。
            // A7(第106批):以前只要出现就**整段**不缓存字数;现在从写法里取出**靶子**,
            //   只废掉被写的那个串(拼接到(串,…) → 串;读取行(串) → 串;读文件(路径,串,上限) → 串),
            //   别的串照提。认不全(出现次数 > 认出次数,例如换行/写法怪)就退回"整段不缓存" —— 宁可少提,不许提错。
            var 被改串 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int 改串出现 = 改串出现正则.Matches(全部).Count;
            int 认出目标 = 0;
            foreach (Match m in 拼接到目标正则.Matches(全部)) { 被改串.Add(m.Groups[1].Value); 认出目标++; }
            foreach (Match m in 读取行目标正则.Matches(全部)) { 被改串.Add(m.Groups[1].Value); 认出目标++; }
            foreach (Match m in 读文件目标正则.Matches(全部)) { 被改串.Add(m.Groups[1].Value); 认出目标++; }
            bool 改串认不全 = 改串出现 > 认出目标;
            var 候字数 = new List<(string 名, bool 在条件里, int 次数)>();
            {
            if (!改串认不全)
            {
                var 字数次数 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in 长度调用正则.Matches(全部))
                {
                    string 名 = m.Groups[1].Value;
                    if (被写.Contains(名)) continue;
                    if (被改串.Contains(名)) continue;      // 这个串在体里被 拼接到/读取行/读文件 写过 → 它的字数会变
                    字数次数[名] = 字数次数.TryGetValue(名, out int n) ? n + 1 : 1;
                }
                foreach (var (名, 次数) in 字数次数.OrderByDescending(x => x.Value))
                {
                    if (!(是字符串参数(名) || 查列表(名) is { 是字符串: true }
                        || 取堆串链(名) is not null || 试取定长缓冲链(名) is not null)) continue;
                    bool 在条件里 = false;
                    for (int i = 0; i < Math.Min(条件行数, 文本行.Count); i++)
                        if (长度调用正则.Matches(文本行[i]).Any(m => string.Equals(m.Groups[1].Value, 名, StringComparison.OrdinalIgnoreCase)))
                        {
                            在条件里 = true;
                            break;
                        }
                    // 一次也算值:数一遍文字要 ~40 条指令,放进常驻寄存器稳赚(条件里每轮都读的排前面)
                    候字数.Add((名, 在条件里, 次数));
                }
            候字数 = [.. 候字数.OrderByDescending(x => x.在条件里).ThenByDescending(x => x.次数)];
            }
            }

            var 次表 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in 标识符提及正则.Matches(全部))
            {
                string 名 = m.Groups[1].Value;
                if (被写.Contains(名)) continue;
                if (中间层C.到英文关键字(名) != null || 类型C.是类型名(名) || 指令C.取指令码表(名) != null) continue;
                次表[名] = 次表.TryGetValue(名, out int n) ? n + 1 : 1;
            }

            var 候变量 = new List<(string 名, 地址位置 位置, int 宽)>();
            foreach (var (名, 次数) in 次表.OrderByDescending(x => x.Value))
            {
                if (查变量(名) is not { } 绑定) continue;
                if (绑定.是引用 || 绑定.数组类型 != null || 绑定.列表类型 != null || 绑定.结构类型 != null) continue;
                if (绑定.槽.宽度 <= 0 || 绑定.槽.宽度 > 4) continue;
                if (次数 < 2 && 绑定.槽.宽度 >= 4) continue;   // 4 字节变量只读一次:提不提都是一条,不提
                if (查位置(名, 行号, 全部) is not { } 位置) continue;
                候变量.Add((名, 位置, 绑定.槽.宽度));
            }

            var 候常量 = new List<uint>();
            var 常量次数 = new Dictionary<uint, int>();
            foreach (Match m in 数字字面量正则.Matches(全部))
            {
                if (!解析无符号32(m.Value, out uint 值) || 值 <= 1) continue;
                常量次数[值] = 常量次数.TryGetValue(值, out int n) ? n + 1 : 1;
            }
            foreach (var (值, 次数) in 常量次数.OrderByDescending(x => x.Value))
                if (次数 >= 2) 候常量.Add(值);

            // **有符号比较的"偏置版"常量也要提**(K3 强度削减):
            // `i < 200` 每轮都要 `异或 0x80000000` 再跟 `200 ^ 0x80000000`(0x800000C8)比 ——
            // 光提字面量 200 没用(比较用的是偏置版,0x800000C8 装不进 20 位立即数,每轮两条 LUI/ORI)。
            // 有比较就把"掩码"和"偏置版的界"也当候选;它们排在字面量后面,寄存器不够时先让给不变量。
            bool 有比较 = 全部.IndexOfAny(['<', '>', '=']) >= 0;
            if (有比较)
            {
                var 偏置候选 = new List<uint> { 0x80000000u };
                foreach (Match m in 数字字面量正则.Matches(全部))
                    if (解析无符号32(m.Value, out uint 字面) && 字面 != 0x80000000u && !偏置候选.Contains(字面 ^ 0x80000000u))
                        偏置候选.Add(字面 ^ 0x80000000u);
                foreach (uint 值 in 偏置候选)
                    if (!候常量.Contains(值)) 候常量.Add(值);
            }

            // P1:归纳变量在体里访问的定长数组 → 步进指针(比不变量值钱,寄存器先给它)
            var 候指针 = 归纳 is { } 归 ? 找步进数组候选(文本行, 全部, 归) : [];

            // 一共最多 3 个常驻寄存器(R0 留给返回值、R15 是帧指针),从 R1 往上拿;外层的先占
            int 已占 = 0;
            foreach (var 层 in _循环保留栈) 已占 += 层.寄存器.Count;
            int 还能给 = Math.Min(3, 6 - 已占);
            if (还能给 <= 0 || (候变量.Count == 0 && 候常量.Count == 0 && 候指针.Count == 0 && 候字数.Count == 0)) return null;

            var 保留 = new 循环保留C();
            var 想提变量 = new List<(string 名, 地址位置 位置, string 寄存器)>();
            var 想提常量 = new List<(uint 值, string 寄存器)>();
            var 想提字数 = new List<(string 名, string 寄存器)>();
            var 想提指针 = new List<步进指针项C>();
            int 号 = 1;
            // ⓪ S12:字符串字数最值钱(一次省 ~40 条),寄存器先给它
            foreach (var (名, _, _) in 候字数)
            {
                while (号 <= 14 && 寄存器被外层占着($"R{号}")) 号++;
                if (号 > 14 || 想提指针.Count + 想提变量.Count + 想提常量.Count + 想提字数.Count >= 还能给) break;
                想提字数.Add((名, $"R{号}"));
                保留.寄存器.Add($"R{号}");
                保留.长度[名] = $"R{号}";
                号++;
            }

            // ① 步进指针:一次省 3~4 条/访问,值钱;先分
            foreach (var 候 in 候指针)
            {
                while (号 <= 14 && 寄存器被外层占着($"R{号}")) 号++;
                if (号 > 14 || 想提指针.Count >= 还能给) break;
                想提指针.Add(候 with { 寄存器 = $"R{号}" });
                保留.寄存器.Add($"R{号}");
                保留.步进指针.Add(候 with { 寄存器 = $"R{号}" });
                号++;
            }
            foreach (var (名, 位置, _) in 候变量)
            {
                while (号 <= 14 && 寄存器被外层占着($"R{号}")) 号++;
                if (号 > 14 || 想提指针.Count + 想提变量.Count + 想提常量.Count + 想提字数.Count >= 还能给) break;
                想提变量.Add((名, 位置, $"R{号}"));
                保留.寄存器.Add($"R{号}");
                号++;
            }
            foreach (uint 值 in 候常量)
            {
                while (号 <= 14 && 寄存器被外层占着($"R{号}")) 号++;
                if (号 > 14 || 想提指针.Count + 想提变量.Count + 想提常量.Count + 想提字数.Count >= 还能给) break;
                想提常量.Add((值, $"R{号}"));
                保留.寄存器.Add($"R{号}");
                号++;
            }
            if (想提变量.Count == 0 && 想提常量.Count == 0 && 想提指针.Count == 0 && 想提字数.Count == 0) return null;

            // 先登记"名字 → 常驻寄存器"(整段循环体都不许再分配它们),再发装载
            // (装载自己要用临时寄存器,而 准备语句占用 会把这一层的常驻寄存器全避开)
            foreach (var (名, _, 寄存器) in 想提变量) 保留.变量[名] = 寄存器;
            foreach (var (值, 寄存器) in 想提常量) 保留.常量[值] = 寄存器;
            _循环保留栈.Add(保留);
            准备语句占用("");
            if (归纳 is { } 归纳值)
                foreach (var 项 in 想提指针) 输出步进指针初值(项, 归纳值, 行号);
            foreach (var (_, 位置, 寄存器) in 想提变量) 发出读变量(寄存器, 位置);
            foreach (var (名, 寄存器) in 想提字数)
            {
                var (数据址, 字节长) = 串数据与字节长(名, 行号, 全部);
                string 数 = 发出数文字(数据址, 字节长);
                发出($"MOV {寄存器} {数}");             // 数在临时寄存器里,搬进常驻寄存器
            }
            foreach (var (值, 寄存器) in 想提常量) 输出装载常量(寄存器, 值);
            _提升个数 += 想提变量.Count + 想提常量.Count;
            _地址增量个数 += 想提指针.Count;
            _长度缓存个数 += 想提字数.Count;
            return 保留;
        }

        private bool 寄存器被外层占着(string 寄存器)
        {
            foreach (var 层 in _循环保留栈)
                if (层.寄存器.Contains(寄存器)) return true;
            return false;
        }

        /// <summary> 退出循环作用域:把这一层的常驻寄存器还回去(不变量出了循环就不再保证)</summary>
        private void 收起循环保留(循环保留C? 保留)
        {
            if (保留 == null) return;
            int 末 = _循环保留栈.Count - 1;
            if (末 >= 0 && ReferenceEquals(_循环保留栈[末], 保留)) _循环保留栈.RemoveAt(末);
        }
        #endregion
    }
}
