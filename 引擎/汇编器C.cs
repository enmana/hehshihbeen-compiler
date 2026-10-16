using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary> 一次编译同时拿到的东西:机器码 + 每个文件的调试信息(就是给单步用的那张表) </summary>
    public sealed class 汇编调试结果C
    {
        public byte[] 机器码 = [];
        public List<文件映射C> 调试文件表 = [];
        /// <summary> 整个工程的调试名字表(各文件并起来;单步调试的"变量"面板看它) </summary>
        public 名字表C 名字表 = new();
        /// <summary> 标签名 → 字节地址(调试器把"变量槽标签"翻成内存地址就看它) </summary>
        public Dictionary<string, int> 标签表 = [];
        /// <summary> 代码段字节数 + 静态数据段字节数(报"内存够不够用"用) </summary>
        public int 代码字节数;
        public int 数据字节数;
        /// <summary> 编译页给人看的说明(列表算出来多大之类) </summary>
        public List<string> 编译提示 = [];
        /// <summary> 运行期报错:每个文件一个 (错误槽标签, 错误号 → 说明);程序停机后读那些槽,非 0 就给出明确错误 </summary>
        public List<(string 槽标签, List<(int 号, string 说明)> 表)> 运行期报错 = [];
        /// <summary>
        /// 顶层常量初值(地址 + 宽度 + 值):装载时直接写进内存(见 `程序包C.应用初值`、`虚拟机C.初值表`)。
        /// 空表 = 这份程序没有可搬的初值,装载方什么都不用做。
        /// </summary>
        public List<初值项C> 初值表 = [];
        /// <summary>
        /// R1-3:编译器自己开的**隐藏区**在运行内存里的实际位置(堆 / 池 / 软栈 / 垫子)。
        /// 中间层报的是"区名 + 槽名 + 多大"(见 <see cref="内存分区定义C"/>),这儿才把槽名翻成绝对地址 ——
        /// 因为只有汇编器知道代码段有多长。整张分区表由 `引擎\内存布局C.cs` 算给用户看。
        /// 空表 = 这份程序没用到隐藏区(既没 `申请`、也没有字符串/列表/递归)。
        /// </summary>
        public List<内存分区C> 内存分区 = [];

        /// <summary>
        /// H100 第二步(运行监视):**运行期那几个"账本槽"的绝对地址**(角色 → 地址)。
        /// 角色是中间层报的语义名(`池指针` / `池末` / `堆首` / `堆末` / `空闲表头` / `软栈`),
        /// 中间层报的是槽名、这里才翻成地址(与 `内存分区` 同一个道理:只有汇编器知道代码段有多长)。
        /// 前端拿它 + 镜像内存就能算出"池 / 堆 / 软栈各用了多少" —— 口径**一处**在 `引擎\内存用量C.cs`。
        /// 查不到的角色不写进去(宁可少一项,不许把编译搞崩)。
        /// </summary>
        public Dictionary<string, int> 内部槽地址 = new(StringComparer.Ordinal);

        /// <summary> 把初值写进内存(装载之后、运行之前;空表是空操作) </summary>
        public void 应用初值(byte[] 内存) => 程序包C.应用初值(内存, 初值表);

        /// <summary>
        /// 程序停机后查"有没有运行期报错":读每个错误槽,非 0 就返回那句说明(找到第一个就返回)。
        /// 没有错误槽 / 没报错 → null。
        /// </summary>
        public string? 查运行期报错(byte[] 内存)
        {
            foreach (var (槽标签, 表) in 运行期报错)
            {
                if (!标签表.TryGetValue(槽标签, out int 地址) || 地址 + 4 > 内存.Length) continue;
                uint 值 = (uint)(内存[地址] | (内存[地址 + 1] << 8) | (内存[地址 + 2] << 16) | (内存[地址 + 3] << 24));
                if (值 == 0) continue;
                string 说明 = 表.FirstOrDefault(x => x.号 == (int)值).说明 ?? $"运行期错误 #{值}";
                return 说明;
            }
            return null;
        }
    }

    /// <summary>
    /// "装载编译结果"的顺路重载(扩展方法):机器码进内存,**顶层常量初值跟着一起落进数据段**。
    /// 初值现在不编成"程序开头一串存储指令"了,所以凡是要跑程序的地方都得走这几个口子。
    /// 放在这里而不是 `虚拟机C` 里,是因为独立宿主编译时只带虚拟机与程序包、不带整个编译器
    /// (初值表本来就是编译产物,宿主那份由程序包提供)。
    /// </summary>
    public static class 装载扩展
    {
        /// <summary>
        /// H39-2:"跑在子进程里"那台要一张**"地址 → 源码位置"表** —— 子进程那侧靠它给运行期报错带文件 + 行号
        /// (`HB宿主` 那条老口径挂的是 `取源位置`;跨进程这条腿过不了 lambda,只能把表本身带过去)。
        /// 这一步放在扩展方法里:**谁调 `装载(编译结果)` 谁就自动带上**,界面 / 命令行都不用各写一遍。
        /// 表就是从这一份编译结果的调试文件表压出来的(与 CLI 那条路同一个入口 `执行行映射C.压源行表`)。
        /// </summary>
        private static void 备好源行表(虚拟机C 机, 汇编调试结果C 编译)
        {
            if (机 is 跨进程虚拟机C 跨机) 跨机.源行表 = 执行行映射C.压源行表(编译.调试文件表);
        }

        /// <summary>
        /// 装载一份编译结果(机器码 + 顶层常量初值)。
        /// H13:装载之后**顺手记下"这一份就是下一次重载眼里的旧那一份"** ——
        /// 机器码 / 标签表 / 名字表 / 内存分区 / 初值表这几样都是调用方在装载**之前**挂上来的
        /// (`界面契约C.装载`、`备好新表`),所以快照只能在装载之后记。
        /// </summary>
        public static void 装载(this 虚拟机C 机, 汇编调试结果C 编译)
        {
            机.初值表 = 编译.初值表;
            备好源行表(机, 编译);
            机.装载(编译.机器码);
            机.记下装载快照();
        }

        /// <summary> 跑一份编译结果(装载 + 连跑) </summary>
        public static void 运行(this 虚拟机C 机, 汇编调试结果C 编译, bool 跟踪 = false)
        {
            机.初值表 = 编译.初值表;
            备好源行表(机, 编译);
            机.运行(编译.机器码, 跟踪);
            机.记下装载快照();          // H13:同上(装载那条路走的是 `虚拟机C.运行`,不会自动记)
        }

        /// <summary> 装一份编译结果到调试会话(单步调试也看得到常量初值) </summary>
        public static void 装载(this 调试会话C 会话, 汇编调试结果C 编译)
        {
            会话.虚拟机.初值表 = 编译.初值表;
            备好源行表(会话.虚拟机, 编译);
            会话.装载(编译.机器码);
        }
    }

    public partial class 汇编器C
    {
        #region K6 增量编译:同一份源第二次编译直接复用上次的展开结果

        /// <summary> `包含 "路径"` / `include "路径"` —— 用来把被包含文件也算进指纹 </summary>
        private static readonly Regex 包含正则 = new("(?:包含|include)\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>
        /// 展开结果缓存:键 = 优化开关 + 基目录 + 源文本 + **堆大小** + **递归收集到的被包含文件内容**,一起算 SHA-256。
        /// 堆大小必须进键:堆的初始化(整块大小、块末地址)是编译期生成的机器码,同一个源配 64KB 与配 128KB
        /// 展出来的文本不一样 —— 不带它就会"换个 `堆` 却吃到上一个大小的旧展开"。
        /// 展开结果只读(标签唯一化/拼文本都是在外面另起字符串干的),复用不会互相污染。
        /// </summary>
        private static readonly Dictionary<string, 中间层C.中间层展开结果C> _展开缓存 = new(StringComparer.Ordinal);

        /// <summary> 命中次数(自检用:证明"第二次真的走了缓存") </summary>
        public static int 展开缓存命中数 { get; private set; }

        /// <summary>
        /// 取(必要时算)一份**工程级**展开结果。键 = 整份工程的内容指纹(每个文件各自含它 `包含` 的闭包),
        /// 所以改任何一个文件、或者改一个被 `包含` 的库,都会重展。
        ///
        /// 代价:多文件工程改一个文件要重展全工程。换来的是正确性 —— 第94批起展开结果依赖**别的文件**
        /// (工程级符号表),再按单文件缓存就会"改 B 吃到 A 的旧展开"、符号对不上。
        /// 全工程编译本来就快(自检实测 2753 行 / 250 个函数 104 ms)。
        /// </summary>
        private static 中间层C.中间层展开结果C 取工程展开(List<(string 名称, string 内容)> 文件表, bool 优化, int 堆大小, IReadOnlyList<string>? 工程归属,
            int 池大小 = 中间层C.默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
        {
            string 键 = 工程指纹(文件表, 优化, 堆大小, 工程归属, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小);
            if (_展开缓存.TryGetValue(键, out var 命中)) { 展开缓存命中数++; return 命中; }
            var 结果 = 中间层C.展开工程带目录(文件表, 带源行标记: true, 优化: 优化, 堆大小: 堆大小, 工程归属: 工程归属, 池大小: 池大小, 内存大小: 内存大小, 软栈大小: 软栈大小, 边界检查: 边界检查, 帧大小: 帧大小);
            if (_展开缓存.Count > 64) _展开缓存.Clear();     // 简单封顶:不为此写 LRU
            _展开缓存[键] = 结果;
            return 结果;
        }

        /// <summary>
        /// 整份工程的指纹:文件名 + 每个文件的内容指纹(含它 `包含` 进来的文件)+ 优化开关 + 堆大小
        /// + **池大小 / 运行内存大小**(R1-2:池的大小与硬顶都编进机器码,换了就吃到旧展开)
        /// + **软栈大小**(R1-1 同理:软栈槽宽度与递归深度上限都编进机器码)+ **工程归属**
        /// + **边界开关**(H14:关掉越界检查就是少发几条指令,不进键就会吃到另一档的旧展开)
        /// + **帧大小**(H34:帧大小编进 `取地址帧` / `减立即数`,换了也必须重展)。
        /// 归属决定 `程序集` 成员的可见性(它改变编译结果),不进键就会吃到旧展开。
        /// </summary>
        private static string 工程指纹(List<(string 名称, string 内容)> 文件表, bool 优化, int 堆大小, IReadOnlyList<string>? 工程归属,
            int 池大小 = 中间层C.默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
        {
            var 料 = new StringBuilder();
            料.Append(优化 ? "P1|" : "P0|").Append("堆").Append(堆大小).Append("|池").Append(池大小).Append("|内存").Append(内存大小).Append("|软栈").Append(软栈大小)
               .Append("|边界").Append(边界检查 ? "查" : "关").Append("|帧").Append(帧大小);
            if (工程归属 is { Count: > 0 })
            {
                料.Append("|归属");
                foreach (string 属 in 工程归属) 料.Append('|').Append(属);
            }
            foreach (var (名称, 内容) in 文件表)
            {
                string? 目录 = null;
                try { 目录 = 虚拟路径C.所在目录(名称); } catch { 目录 = null; }
                料.Append("||").Append(名称).Append('|')
                   .Append(内容指纹(内容 ?? "", string.IsNullOrEmpty(目录) ? null : 目录, 优化, 堆大小, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小));
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(料.ToString())));
        }

        /// <summary>
        /// 内容指纹:源文本 + **这次的堆大小 / 池大小 / 运行内存大小 / 软栈大小 / 边界开关 / 帧大小** + 它 `包含` 进来的文件(递归,最多 8 层)一起算。
        /// 被包含的文件改一个字,包含它的那个文件也必须重展 —— 这是增量编译最容易错的地方。
        /// </summary>
        private static string 内容指纹(string 内容, string? 基目录, bool 优化, int 堆大小, int 池大小 = 中间层C.默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1, int 深 = 0)
        {
            var 料 = new StringBuilder();
            料.Append(优化 ? "1|" : "0|").Append(基目录 ?? "").Append("|堆").Append(堆大小)
               .Append("|池").Append(池大小).Append("|内存").Append(内存大小).Append("|软栈").Append(软栈大小)
               .Append("|边界").Append(边界检查 ? "查" : "关").Append("|帧").Append(帧大小).Append('|').Append(内容);
            if (深 < 8 && 内容.Length > 0 && (内容.Contains("包含") || 内容.Contains("include")))
            {
                foreach (Match 匹配 in 包含正则.Matches(内容))
                {
                    string 相对 = 匹配.Groups[1].Value;
                    料.Append("||").Append(相对);
                    try
                    {
                        // H64:`包含` 的相对路径按**本文件所在目录**解析 —— 与编译器同一条口径,
                        // 而且是虚拟路径(系统目录那一档)也认的那一份:拼接不能用 `Path.Combine`
                        string 全 = 虚拟路径C.是虚拟(相对) || 基目录 == null ? 相对 : 虚拟路径C.拼接(基目录, 相对);
                        if (虚拟文件C.存在(全))
                            料.Append('|').Append(内容指纹(文本落盘C.读源文件(全), 虚拟路径C.所在目录(全), 优化, 堆大小, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小, 深 + 1));
                    }
                    catch { /* 读不到就只把路径算进去:那次展开本来就会报错,不会进缓存 */ }
                }
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(料.ToString())));
        }
        #endregion

        // 寄存器解析统一走 寄存器C(R0~R15,单一事实源),不再维护本地寄存器表

        // 标签字典（公开供界面显示）
        public Dictionary<string, int> 标签表 { get; private set; } = [];

        /// <summary>
        /// H100 第二步:**最近一次汇编的那几个账本槽地址**(角色 → 地址;与 <see cref="标签表"/> 同一次结果,
        /// 公开给界面走契约取 —— 运行监视算"池 / 堆 / 软栈用了多少"要用它)。
        /// </summary>
        public Dictionary<string, int> 内部槽地址 { get; private set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 通用化(2026-10):这一趟汇编里**要原地展开**的行下标 → 展开形态
        /// (20 位那一档的**统一出口**:目标/地址/常量越过 1MB 的行在这儿一次性摊开)。
        /// 空表 = 一个都没超 ⇒ 与老版本逐字节等价(这是验收要求)。
        /// </summary>
        private Dictionary<int, 展开形态C> _需展开行 = [];

        /// <summary>
        /// 最近一次汇编里**原地摊开过的行数**(通用化第2批:20 位那一档的统一出口)。
        /// **0 = 一个都没摊** ⇒ 这一趟与"没有展开机制"的老版本逐字节等价(判据 ① 就钉这一条:
        /// 1MB 档的标准工程必须是 0)。自检 / 调试拿它当指纹;产物本身不受它影响。
        /// </summary>
        public int 展开行数 { get; private set; }

        private int 当前地址; // 字节地址（每条指令4字节）

        /// <summary>
        /// 预处理后每一行对应的**源行号**(展开文本里带 `// 来自第 N 行` 标记时才有;0 = 不知道)。
        /// 用处:纯汇编这一层报错(比如"未知指令")也能告诉用户是源码第几行出的事 —— 不然只有一句干巴巴的消息,界面标不出红线。
        /// </summary>
        private List<int> _每行源行号 = [];

        /// <summary>
        /// 每一条指令来自**文件表里的第几个文件**(C22 起;展开文本里 `// 来自文件K第 N 行` 的那个 K,1 基)。
        /// 0 = 不知道(单文件展开 / 编译器生成 / `包含` 进来的库行)。用处:标签重复时报得出"是哪个文件"。
        /// </summary>
        private List<int> _每行文件号 = [];

        /// <summary> 正在汇编的那一条指令在 `行列表` 里的下标(报错时算文件名与行号;见 `标签重复错`) </summary>
        private int _报错行下标 = -1;

        public byte[] 汇编(string 源代码, string? 基目录 = null, int 堆大小 = 中间层C.默认堆大小,
            int 池大小 = 中间层C.默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
        {
            // 中间层展开：把 如果/选择/循环/遍历/当 等流程控制展开为纯汇编；无高层代码时原样返回
            // 基目录用于解析 包含 "路径" 里的相对路径(单文件编译时传源文件所在目录)
            // 堆大小:单文件编译没有工程文件,一律走默认;带工程文件的那条路走 汇编多文件带调试
            var 结果 = 中间层C.展开完整带目录(源代码, 基目录, 带源行标记: true, 堆大小: 堆大小, 池大小: 池大小, 内存大小: 内存大小, 软栈大小: 软栈大小, 边界检查: 边界检查, 帧大小: 帧大小);
            return 汇编已展开文本(结果.汇编文本, 结果.变量槽);
        }

        /// <summary>
        /// 为**纯汇编**文本生成"字节地址 → 行号"映射(行号从 1 开始),每行一条指令、占 4 字节。
        /// 只认纯汇编:语言(中间层)源码一行会展开成好几条指令,那种要用 执行行映射C。
        /// 注释全部跳过:`//…` 行注释、`/* … */` 块注释(可跨行),以及 `;` 之后的结束符部分。
        /// </summary>
        public Dictionary<int, int> 取地址到行号映射(string 源代码)
        {
            var 映射 = new Dictionary<int, int>();
            if (string.IsNullOrEmpty(源代码)) return 映射;
            int 地址 = 0;
            bool 块注释中 = false;
            var 行数组 = 源代码.Split('\n');
            for (int idx = 0; idx < 行数组.Length; idx++)
            {
                string 行 = 行数组[idx];
                // 块注释:整行在注释里就直接跳过,否则从 */ 之后继续
                if (块注释中)
                {
                    int 收 = 行.IndexOf("*/", StringComparison.Ordinal);
                    if (收 < 0) continue;
                    行 = 行[(收 + 2)..];
                    块注释中 = false;
                }
                while (true)
                {
                    int 开 = 行.IndexOf("/*", StringComparison.Ordinal);
                    if (开 < 0) break;
                    int 收 = 行.IndexOf("*/", 开 + 2, StringComparison.Ordinal);
                    if (收 < 0) { 行 = 行[..开]; 块注释中 = true; break; }
                    行 = 行[..开] + 行[(收 + 2)..];
                }
                int 行注释 = 行.IndexOf("//", StringComparison.Ordinal);
                if (行注释 >= 0) 行 = 行[..行注释];
                int 结束符 = 行.IndexOf(';');
                if (结束符 >= 0) 行 = 行[..结束符];
                行 = 行.Trim();
                if (string.IsNullOrEmpty(行)) continue;
                // 去掉标签部分
                if (行.Contains(':'))
                {
                    var 部分 = 行.Split(':', 2);
                    行 = 部分.Length > 1 ? 部分[1].Trim() : string.Empty;
                }
                if (!string.IsNullOrWhiteSpace(行))
                {
                    映射[地址] = idx + 1; // 行号从1开始
                    地址 += 4;
                }
            }
            return 映射;
        }

        /// <summary>
        /// 多文档编译(**连调试信息一起**):第94批起走**工程级展开** ——
        /// 一个展开器按文件表顺序连吃所有文件(一张工程级符号表:函数 / 结构 / 全局变量跨文件直接可用),
        /// 展开一次、汇编一次。调试段按标记里的 `文件K` 分回各文件(段与段**不必连续**:各文件的主程序行
        /// 在前面,所有函数体统一排在程序末尾,所以同一个文件的指令会分散在多处)。
        ///
        /// 为什么不再"每个文件各展一次再拼":那样每个文件各有一套 `_函数表`/根作用域/槽号/序言 ——
        /// 跨文件什么都不能用,而且序言(池/堆/软栈)会发好几份、把池切成好几块。
        /// </summary>
        /// <param name="堆大小">
        /// 这次编译给堆区留多少字节(工程文件里的 `堆`;不传就是 64KB)。它决定生成的机器码,也因此在展开缓存的键里。
        /// </param>
        /// <param name="软栈大小">
        /// **R1-1**:这次编译给软栈留多少字节(工程文件里的 `软栈`;**-1 = 自动**,按运行内存算 `min(64KB, 内存÷4)`)。
        /// 与堆同理:软栈槽宽度与递归深度上限都编进机器码,所以也在展开缓存的键里(换大小必须重展)。
        /// </param>
        /// <param name="工程归属">
        /// 每个源文件属于哪个 `.hbp` 工程(与 `源文件表` 一一对应;不传 = 都算同一个匿名工程)。
        /// `程序集` 成员的可见性按它判 —— 见 `工程文件C.源文件归属`。
        /// </param>
        /// <param name="边界检查">
        /// **H14**:数组 / 列表的运行期下标越界检查发不发(工程文件 `"边界": "关"` ⇒ 假)。
        /// 它改变生成的机器码 ⇒ 也在展开缓存的键里(见 `工程指纹`;不带它就会"换一档吃到另一档的旧展开")。
        /// </param>
        /// <param name="帧大小">
        /// **H34**:每个函数帧多大(工程文件里的 `帧`;**-1 = 默认** = `中间层C.默认帧大小`)。
        /// 与堆、软栈同理:帧大小编进 `取地址帧` / `减立即数` 的偏移 ⇒ 也在展开缓存的键里。
        /// </param>
        public 汇编调试结果C 汇编多文件带调试(IEnumerable<(string 名称, string 内容)> 源文件表, bool 优化 = true, int 堆大小 = 中间层C.默认堆大小, IReadOnlyList<string>? 工程归属 = null,
            int 池大小 = 中间层C.默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
        {
            var 文件表 = 源文件表.ToList();
            名称表 = 文件表.Select(x => x.名称).ToArray();
            中间层C.中间层展开结果C 结果;
            try
            {
                结果 = 取工程展开(文件表, 优化, 堆大小, 工程归属, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小);
            }
            catch (编译异常C 错) when (错.文件 == null && 文件表.Count == 1)
            {
                // 单文件那条路保持老口径:出错时补上"是哪个文件"(工程级展开的报错自己带文件名,见 中间层展开器.报错)
                throw new 编译异常C(错.中文, 错.英文, 错.行号, 错.源行, 文件表[0].名称 ?? "");
            }
            var 码 = 汇编已展开文本(结果.汇编文本, 结果.变量槽);
            var 调试表 = 执行行映射C.扫工程展开(结果.汇编文本, 名称表);
            var 全名字表 = 结果.名字表 ?? new 名字表C();
            for (int i = 0; i < 调试表.Count; i++)
                调试表[i].名字表 = 拆名字表(全名字表, i < 名称表.Length ? 名称表[i] : "", 首段: i == 0);
            // 顶层常量初值:槽标签 → 绝对地址(标签表这时才齐)。查不到标签的那条直接丢:宁可少写一个字,不许把编译搞崩
            var 初值表 = new List<初值项C>();
            foreach (var (槽名, 偏移, 宽度, 值) in 结果.初值表 ?? [])
                if (标签表.TryGetValue(槽名, out int 槽址))
                    初值表.Add(new 初值项C(槽址 + 偏移, 宽度, 值));
            var 运行期报错 = new List<(string 槽标签, List<(int 号, string 说明)> 表)>();
            if (结果.错误槽 is { } 错误槽 && (结果.错误表?.Count ?? 0) > 0)
                运行期报错.Add((错误槽.名称, [.. 结果.错误表!]));
            // R1-3:隐藏区的槽名 → 绝对地址(标签表这时才齐;查不到就丢一条,不许把编译搞崩 —— 与 初值表 同一个口径)
            var 分区表 = new List<内存分区C>();
            foreach (var 区 in 结果.内存分区 ?? [])
                if (标签表.TryGetValue(区.槽名, out int 区址))
                    分区表.Add(new 内存分区C(区.区名, 区址, 区.大小, 区.说明));
            // H100 第二步:运行监视要读的账本槽(角色 → 地址),同一个口径
            var 账本槽地址 = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (角色, 槽名) in 结果.内部槽 ?? [])
                if (标签表.TryGetValue(槽名, out int 槽址2))
                    账本槽地址[角色] = 槽址2;
            内部槽地址 = 账本槽地址;          // 公开给界面走契约取(与 `标签表` 同一个口径)
            return new 汇编调试结果C
            {
                机器码 = 码,
                调试文件表 = 调试表,
                名字表 = 全名字表,
                标签表 = 标签表,
                代码字节数 = 代码字节数,
                数据字节数 = 数据字节数,
                编译提示 = [.. 结果.编译提示 ?? []],
                运行期报错 = 运行期报错,
                初值表 = 初值表,
                内存分区 = 分区表,
                内部槽地址 = 账本槽地址,
            };
        }

        /// <summary>
        /// 从**工程级**名字表里挑出属于某个文件的那一份(调试面板按文件显示,才不会串文件)。
        /// 变量按 `声明文件` 分;第一个段额外收下"没有文件归属"的项(编译器在语句之外登记的)。
        /// 结构表是共享只读的,每个段给同一份;**函数范围 / 函数边界按"这段码编在哪个段"分**
        /// (行号是各文件自己的,两个文件都可能有一个"第 5 到 9 行"的函数)——
        /// 被 `包含` 进来的库函数码在本段、声明在库文件里,段归属看 `名字表C.函数段文件`
        /// (第 4 项照旧原样带出去:它 = **真正声明那一份文件**,见第254批的口径),
        /// 同时把 `本段文件` 摆上,免得"第 N 行属哪个函数"拿库里的行号去认本文件。
        /// </summary>
        private static 名字表C 拆名字表(名字表C 全, string 文件, bool 首段)
        {
            var 结果 = new 名字表C { 结构 = 全.结构, 函数签名 = 全.函数签名, 本段文件 = 文件 };
            foreach (var 项 in 全.变量)
                if (项.声明文件.Length == 0 ? 首段 : string.Equals(项.声明文件, 文件, StringComparison.OrdinalIgnoreCase))
                    结果.变量.Add(项);
            var 本段函数 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (函数, 起, 止, 定义文件) in 全.函数范围)
                if (在本段(全, 函数, 定义文件, 文件, 首段))
                {
                    结果.函数范围.Add((函数, 起, 止, 定义文件));
                    本段函数.Add(函数);
                }
            // 段归属那句"为什么它在本段里"也跟着带过来(库函数在这一段里,声明在库文件里)
            foreach (var (函数, 段文件) in 全.函数段文件)
                if (本段函数.Contains(函数)) 结果.函数段文件[函数] = 段文件;
            // C14 的花括号行同一套过滤口径(行号是各文件自己的,不按文件分就会串)
            foreach (var (函数, 左行, 右行, 定义文件) in 全.函数边界)
                if (在本段(全, 函数, 定义文件, 文件, 首段))
                    结果.函数边界.Add((函数, 左行, 右行, 定义文件));
            // C19 的两张旁路表(可见性按声明文件分;"文件 → 模块/工程"按文件名分)
            foreach (var (名, 项) in 全.可见性)
                if (项.声明文件.Length == 0 ? 首段 : string.Equals(项.声明文件, 文件, StringComparison.OrdinalIgnoreCase))
                    结果.可见性[名] = 项;
            foreach (var (路径, 模块) in 全.文件模块)
                if (路径.Length == 0 ? 首段 : string.Equals(路径, 文件, StringComparison.OrdinalIgnoreCase))
                    结果.文件模块[路径] = 模块;
            foreach (var (路径, 工程) in 全.文件工程)
                if (路径.Length == 0 ? 首段 : string.Equals(路径, 文件, StringComparison.OrdinalIgnoreCase))
                    结果.文件工程[路径] = 工程;
            结果.模块名.AddRange(全.模块名);
            return 结果;
        }

        /// <summary> 这个函数是不是编在 `文件` 这一段里(段归属看 `函数段文件`,没有就按声明文件;都空了归首段) </summary>
        private static bool 在本段(名字表C 全, string 函数, string 定义文件, string 文件, bool 首段)
        {
            string 段文件 = 全.函数段文件.TryGetValue(函数, out string? 段) ? 段 : 定义文件;
            return 段文件.Length == 0 ? 首段 : string.Equals(段文件, 文件, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary> 只要机器码的老入口:内部走同一条路(带标记展开),保证和带调试信息的那份逐字节一致 </summary>
        public byte[] 汇编多文件(IEnumerable<(string 名称, string 内容)> 源文件表, bool 优化 = true, int 堆大小 = 中间层C.默认堆大小, IReadOnlyList<string>? 工程归属 = null,
            int 池大小 = 中间层C.默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
            => 汇编多文件带调试(源文件表, 优化, 堆大小, 工程归属, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小).机器码;

        public string[] 名称表 { get; private set; } = [];

        /// <summary> 最近一次汇编出来的代码段字节数(调试/编译信息里报"内存用掉多少") </summary>
        public int 代码字节数 { get; private set; }

        /// <summary> 最近一次汇编出来的静态数据段字节数(变量槽紧致排布的总宽) </summary>
        public int 数据字节数 { get; private set; }

        // 第94批:三个"按文件序号唯一化"的助手(唯一化槽 / 去旧序号 / 唯一化展开标签)全部退场 ——
        // 工程级展开只有一个编译单元,内部标签本来就顺序唯一;跨文件重名由"重复声明"报错挡住(D2 决策),
        // 不再需要靠改名回避。留着它们反而会掩盖重名。

        /// <summary>
        /// 对"已展开纯汇编文本"做两遍汇编(标签收集→编码);单/多文件共用。
        /// 变量数据槽由调用方显式给出(名+宽度,按声明序紧致分配);文本里残留的 _vN 兜底按 32 位字补上。
        /// </summary>
        private byte[] 汇编已展开文本(string 展开文本, IReadOnlyList<变量槽定义>? 数据槽 = null)
        {
            标签表 = [];
            内部槽地址 = new(StringComparer.Ordinal);   // 与 标签表 同一条命:这一趟没登记就当作没有
            当前地址 = 0;
            List<string> 行列表 = 预处理(展开文本);

            // ---- 通用化:大地址 / 大常量的**原地展开**(2026-10,统一出口)----
            // 病:地址字段只有 20 位(1MB)。跳转目标一过 1MB 就报「立即数/地址超出20位范围」
            //     (实测 40,803 行、代码段 1.83MB 那次:19,990 处全是 `为零跳转`);
            //     **数据这一半**同理:`加载 / 存储 / 取字 / 存字 / 取地址` 的地址、以及
            //     跟内存大小挂钩的常量(池顶那一格最典型)一过 1MB 也全报同一句,连 4 行的程序都编不过。
            // 治:这一行**原地摊开**成几条 —— `载入高位(LUI) + 或立即数(OR_IMMEDIATE)` 把 32 位值拼进寄存器,
            //     再走**间接寻址**那一档(加载/存储/取字/存字/取地址都各有一条间接指令)。
            //     摊开的形态与条数见 `展开形态C`;每一条都是现成的 ISA,一条新指令都不加。
            // 暂存寄存器用 **R14**:跳转点上表达式的值早已求值完并写回,而"写地址 / 算常量"这两档
            //     用 `入栈 R14 … 出栈 R14` 把它护住(硬件栈;`入栈/出栈` 不动状态标志,见 `虚拟机C`),
            //     所以连"比较之后紧接着跳转"那种排布也不会被摊开打乱。
            // **20 位装得下时这一整段都不触发** ⇒ 老程序的机器码逐字节不变(这是硬要求,判据 ①)。
            const string 展开暂存寄存器 = "R14";
            var 需展开 = new Dictionary<int, 展开形态C>();

            // 第一趟:**重复跑**(算长度 → 建标签 → 再扫)直到"要展开的行"不再增长,再定下来。
            // 为什么要重复:展开会让代码段变长,变长又可能让**别的**跳转越过 1MB。
            int 代码长度 = 0;
            var 槽序表 = new List<变量槽定义>();
            int 数据长度 = 0;
            for (int 趟 = 0; ; 趟++)
            {
                标签表.Clear();      // 每趟重建(长度变了,标签地址跟着变;不清就报"标签重复定义")
                当前地址 = 0;
                for (int i = 0; i < 行列表.Count; i++)
                {
                    _报错行下标 = i;                     // 标签重复时靠它算出"哪个文件第几行"(C22)
                    string 剩余行 = 去掉标签(行列表[i], true); // 只收集标签，不修改地址
                    if (!string.IsNullOrWhiteSpace(剩余行))
                        当前地址 += 需展开.TryGetValue(i, out var 形态) ? 展开条数(形态) * 4 : 4;
                }
                代码长度 = 当前地址;

                // 变量数据槽：显式列表(名+宽度)决定尺寸与顺序;文本里残留的 _vN 兜底(按 32 位字)
                // **H28**:去重不再"每来一个槽就把表从头扫一遍"(`Any(x => x.名称 == …)`,槽多时是槽数²),
                // 改成一张 `HashSet`(`Add` 返回 false 就是已经有过)。顺序仍是"第一次出现的顺序"。
                槽序表 = new List<变量槽定义>();
                var 已见槽名 = new HashSet<string>(StringComparer.Ordinal);
                if (数据槽 != null)
                    foreach (var 槽 in 数据槽)
                        if (已见槽名.Add(槽.名称)) 槽序表.Add(槽);
                foreach (string 行 in 行列表)
                {
                    string 指令行 = 去掉标签(行, false);
                    if (string.IsNullOrWhiteSpace(指令行)) continue;
                    foreach (string 标记 in 标记化(指令行))
                    {
                        if (!标记.StartsWith("_v", StringComparison.Ordinal)) continue;
                        if (标签表.ContainsKey(标记))
                            throw new Exception($"保留前缀 _v 与代码标签冲突: {标记}");
                        if (已见槽名.Add(标记))
                            槽序表.Add(new 变量槽定义(标记, 类型C.字宽度));
                    }
                }
                int 数据偏移 = 0;
                foreach (var 槽 in 槽序表)
                {
                    标签表[槽.名称] = 代码长度 + 数据偏移;
                    数据偏移 += 槽.宽度;   // 紧致连续:1/2/4 字节按声明序排布
                }
                数据长度 = 数据偏移;

                // 扫一遍:还有哪些行的"第二操作数"(目标 / 地址 / 常量)越过 1MB(要展开的直接寻址上限 = 20 位)
                bool 长了 = false;
                for (int i = 0; i < 行列表.Count; i++)
                {
                    if (需展开.ContainsKey(i)) continue;
                    string 指令行 = 去掉标签(行列表[i], false);
                    if (string.IsNullOrWhiteSpace(指令行)) continue;
                    展开形态C 形态 = 定展开形态(指令行);
                    if (形态 == 展开形态C.不展开) continue;
                    需展开[i] = 形态;
                    长了 = true;
                }
                if (!长了 || 趟 >= 8) break;   // 收敛(或到趟数上限,照当前这版编下去)
            }

            _需展开行 = 需展开;
            展开行数 = 需展开.Count;
            代码字节数 = 代码长度;
            数据字节数 = 数据长度;

            // 第二遍：生成机器码（代码段 + 尾部零初始化数据段,数据段总长=各槽宽度之和）
            byte[] 输出代码 = new byte[代码长度 + 数据长度];
            当前地址 = 0;
            int 写入位置 = 0;
            // 多错误报告(K5 下半):一行错不打断整篇 —— 这一行留 4 字节零、地址不挪位,接着编下一行,
            // 收尾一次列全(零填的代码绝不会被用上:有错就在下面抛出去)
            var 汇编错误表 = new List<编译异常C>();
            for (int 行下标 = 0; 行下标 < 行列表.Count; 行下标++)
            {
                string 指令行 = 去掉标签(行列表[行下标], false);
                if (string.IsNullOrWhiteSpace(指令行))
                    continue;
                bool 这行有形态 = _需展开行.TryGetValue(行下标, out 展开形态C 形态);

                byte[] 编码;
                try
                {
                    编码 = 这行有形态 ? 编码展开行(指令行, 形态) : 编码指令(指令行);
                    // 通用化:展开行按形态占 2 / 3 / 5 条,其余固定 4 字节
                    int 该多长 = (这行有形态 ? 展开条数(形态) : 1) * 4;
                    if (编码.Length != 该多长) throw new Exception($"指令编码长度错误: {指令行}");
                }
                catch (编译异常C 错)
                {
                    汇编错误表.Add(错);
                    编码 = new byte[4];
                }
                catch (Exception 异常)
                {
                    // 纯汇编这一层的错(未知指令、操作数不对……)也带上源行号:界面上能标红,用户知道去哪一行改
                    int 源行 = 当前取源行号(行列表, 写入位置 / 4);
                    string 话 = 整理提示(异常.Message);
                    汇编错误表.Add(new 编译异常C(源行 > 0 ? $"第 {源行} 行: {话}" : 话, null, 源行 > 0 ? 源行 : null, 指令行));
                    编码 = new byte[4];
                }
                Buffer.BlockCopy(编码, 0, 输出代码, 写入位置, 编码.Length);
                写入位置 += 编码.Length;
                当前地址 += 编码.Length;
            }
            if (汇编错误表.Count > 0) throw 合成汇编多错(汇编错误表);

            return 输出代码;
        }

        /// <summary>
        /// 把攒下的汇编错误合成一条:按行号排;**只有一处错时原样抛原来那条**(文案与行号一个字不变)。
        /// </summary>
        private static 编译异常C 合成汇编多错(List<编译异常C> 错表)
        {
            var 排好 = 错表.OrderBy(x => x.行号 ?? int.MaxValue).ToList();
            if (排好.Count == 1) return 排好[0];
            var 说明 = new System.Text.StringBuilder($"共 {排好.Count} 处错误(一次列全,按行号排):");
            foreach (var 错 in 排好) 说明.Append($"\n  {错.中文}");
            return new 编译异常C(说明.ToString(), $"multiple errors ({排好.Count})", 排好[0].行号, 排好[0].源行, 排好[0].文件 ?? "")
            {
                子错 = 排好,      // P23:同上,一条条挂上
            };
        }

        /// <summary> 第几条非空指令对应的源行号(展开文本里没带标记就给 0);只在报错时调一次 </summary>
        private int 当前取源行号(List<string> 行列表, int 第几条)
        {
            try
            {
                if (_每行源行号.Count != 行列表.Count) return 0;
                int 数 = 0;
                for (int i = 0; i < 行列表.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(去掉标签(行列表[i], false))) continue;
                    if (数 == 第几条) return _每行源行号[i];
                    数++;
                }
            }
            catch { }
            return 0;
        }

        /// <summary> 给纯汇编层的报错补一句"说明":常见的是把关键字/指令名拼错了 </summary>
        private static string 整理提示(string 消息)
        {
            if (消息.StartsWith("未知指令", StringComparison.Ordinal))
                return 消息 + " —— 这行既不是认识的指令,也不是语言的关键字(通常是拼错了一个字)";
            return 消息;
        }

        private List<string> 预处理(string 源代码)
        {
            var 结果 = new List<string>();
            _每行源行号 = [];
            _每行文件号 = [];
            bool 块注释中 = false;
            foreach (string 原始行 in 源代码.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            {
                string 行 = 原始行;
                // 中间层展开时会在指令屁股后面缀一个源行标记(`// 来自[文件K]第 N 行`,被 `包含` 的行尾多一个 `库`),
                // 先把源行号捡出来再当注释扔掉。**解析只有一份** —— `引擎\源行标记C.cs`(唯一事实源):
                // 以前这里自己写了一份,既不认尾巴上的 `库`、也不认 `第 7 到 12 行` 的范围写法,
                // 于是库里的行号被当成主文件的行号(运行期报错会指错行)。
                // 带 `库` 的行、以及 `// 编译器生成` 之后的生成代码,都**不给行号**(0 = 不知道):
                // 它们的行号属于别的语句/别的文件,写上去只会指错(没标记的行本来就给 0,见下)。
                int 源行号 = 0;
                int 文件号 = 0;                       // C22:标签重复时报"是哪个文件"用(展开文本里的 `文件K`)
                var 源行标记 = 源行标记C.读(行);
                if (源行标记.有 && !源行标记.来自库) { 源行号 = 源行标记.起始行; 文件号 = 源行标记.文件号; }
                // 注释:`//…` 行注释、`/* … */` 块注释(可跨行)。以前只认 `;`,
                // 而"带 `// 来自第 N 行` 标记的展开文本"要直接进这里汇编,所以注释必须在这儿也认。
                if (块注释中)
                {
                    int 收 = 行.IndexOf("*/", StringComparison.Ordinal);
                    if (收 < 0) continue;
                    行 = 行[(收 + 2)..];
                    块注释中 = false;
                }
                while (true)
                {
                    int 开 = 行.IndexOf("/*", StringComparison.Ordinal);
                    if (开 < 0) break;
                    int 收 = 行.IndexOf("*/", 开 + 2, StringComparison.Ordinal);
                    if (收 < 0) { 行 = 行[..开]; 块注释中 = true; break; }
                    行 = 行[..开] + 行[(收 + 2)..];
                }
                int 行注释 = 行.IndexOf("//", StringComparison.Ordinal);
                if (行注释 >= 0)
                    行 = 行[..行注释];
                int 注释位置 = 行.IndexOf(';');
                if (注释位置 >= 0)
                    行 = 行[..注释位置];

                行 = 行.Trim();
                if (string.IsNullOrEmpty(行))
                    continue;

                行 = 替换空白符正则().Replace(行, " ");
                结果.Add(行);
                _每行源行号.Add(源行号);
                _每行文件号.Add(文件号);
            }
            return 结果;
        }

        private string 去掉标签(string 行, bool 收集标签)
        {
            if (行.Contains(':'))
            {
                string[] 部分 = 行.Split(':', StringSplitOptions.RemoveEmptyEntries);
                string 标签名 = 部分[0].Trim();
                if (!string.IsNullOrEmpty(标签名))
                {
                    if (收集标签)
                    {
                        if (标签表.ContainsKey(标签名)) throw 标签重复错(标签名);
                        标签表[标签名] = 当前地址; // 当前地址指向该行指令起始
                    }
                }
                return 部分.Length > 1 ? 部分[1].Trim() : "";
            }
            return 行;
        }

        /// <summary>
        /// 标签重复(C22 顺带把口径改对):以前这里抛的是**裸 Exception**,到界面上显示成"内部错误(请回报),
         /// 不是你写错了" —— 可它偏偏是用户最容易撞上的一种错:把一份**整篇展开**的 `.hba` 和它的 `.hb` 源码
         /// 一起列进编译,两边标签必然撞。现在报成**正式编译错**(带文件 + 行号 + 怎么改),与别的编译错一个待遇。
        /// </summary>
        private 编译异常C 标签重复错(string 标签名)
        {
            int 源行 = _报错行下标 >= 0 && _报错行下标 < _每行源行号.Count ? _每行源行号[_报错行下标] : 0;
            string 文件名 = "";
            if (_报错行下标 >= 0 && _报错行下标 < _每行文件号.Count)
            {
                int 号 = _每行文件号[_报错行下标];
                if (号 >= 1 && 号 <= 名称表.Length) 文件名 = 名称表[号 - 1];
            }
            string 话 = $"标签重复定义:`{标签名}` —— 这个名字在前面已经定义过一次(同一份机器码里标签必须唯一)。";
            // 这次编译里有没有汇编源码?有就把 C22 那条最容易踩的说清楚。
            // (不硬说"就是它":整篇展开的 `.hba` 里自带的 `// 来自…` 标记指的文件号是**上一次编译**的,
            //  所以行号有时落在 `.hb` 上 —— 按"这次编译里有 `.hba`"这个事实给提示最稳。)
            string? 汇编文件 = 文件名.Length > 0 && 汇编源码C.是汇编源码(文件名) ? 文件名 : 名称表.FirstOrDefault(汇编源码C.是汇编源码);
            if (汇编文件 != null)
                话 += $"\n这次编译里有汇编源码 `{System.IO.Path.GetFileName(汇编文件)}`,最常见的原因就是它:" +
                    "它要是**编译出来的整篇展开**(里面已经有整份程序),再和源码一起编译必然撞标签。" +
                    "改法二选一:① 只留你手写的那几个例程(把源码里对应的函数删掉或改名);" +
                    "② 或者把它从工程文件的 `汇编` 表里拿掉(编辑器里取消勾选『参与编译』)。";
            return new 编译异常C(话, $"duplicate label: {标签名}", 源行 > 0 ? 源行 : null, null, 文件名);
        }

        /// <summary>
        /// **20 位那一档的原地展开:形态表(2026-10,通用化第2批 —— 数据那一半)**。
        /// <para>
        /// 老的那一版只认"`为零跳转` 的目标越过 1MB"(代码段能超过 1MB 那一件事)。可 20 位这道墙
        /// 对**数据**一样有效:`加载 / 存储 / 取字 / 存字 / 取地址` 的地址、以及跟内存大小挂钩的常量
        /// (池顶那一格最典型)一过 1MB 就报同一句「立即数/地址超出20位范围」—— 连 4 行的程序都编不过。
        /// </para>
        /// <para>
        /// 每一档的摊开形状(都是现成的 ISA,一条新指令都不加):
        /// <list type="bullet">
        /// <item>`存取地址`(取地址 Rd, 大地址)→ `LUI Rd` + `ORI Rd`(2 条;目标寄存器本身就是暂存)</item>
        /// <item>`装常量`(加载立即数 Rd, 大常量)→ `LUI Rd` + `ORI Rd`(2 条)</item>
        /// <item>`读地址`(加载 / 取字 Rd, 大地址)→ `LUI Rd` + `ORI Rd` + `××间接 Rd, Rd`(3 条)</item>
        /// <item>`跳转`(为零跳转 Rd, 大目标)→ `LUI R14` + `ORI R14` + `为零跳转寄存器 Rd, R14`(3 条)</item>
        /// <item>`写地址`(存储 / 存字 Rs, 大地址)→ `入栈 R14` + `LUI R14` + `ORI R14` + `××间接 Rs, R14` + `出栈 R14`(5 条)</item>
        /// <item>`算常量`(比较 / 加 / 减 / 或立即数 Rd, 大常量)→ `入栈 R14` + `LUI R14` + `ORI R14` + `原算术 Rd, R14` + `出栈 R14`(5 条)</item>
        /// </list>
        /// </para>
        /// <para>
        /// 为什么读那一档能拿目标寄存器当暂存:值是**算出来再放进去**的,放进去之前它没有活值;
        /// 写与算那两档不能毁掉任何一个操作数,所以用 `入栈/出栈 R14` 护住 —— `入栈/出栈`
        /// **不动状态标志**(见 `虚拟机C` 的 0x0B/0x0C),所以"比较之后紧接着跳转"也不会被这段摊开打乱。
        /// </para>
        /// </summary>
        private enum 展开形态C
        {
            不展开 = 0,
            存取地址,
            装常量,
            读地址,
            跳转,
            写地址,
            算常量,
        }

        /// <summary> 这一形态摊开成几条指令(估长与编码两处共用同一份事实源) </summary>
        private static int 展开条数(展开形态C 形态) => 形态 switch
        {
            展开形态C.存取地址 or 展开形态C.装常量 => 2,
            展开形态C.读地址 or 展开形态C.跳转 => 3,
            展开形态C.写地址 or 展开形态C.算常量 => 5,
            _ => 1,
        };

        /// <summary>
        /// 展开用的**暂存寄存器**:从 R14 往下找第一个**不在这一行操作数里**的临时寄存器。
        /// <para>
        /// 为什么不能写死 R14(第一版就栽在这儿):中间层也会拿 R14 装值 —— 碰上
        /// `存字 R14, 大地址` 这种行,写死 R14 就等于"把要存的那个值当成地址用"(实测:六个大数组
        /// 那份程序算出来的和是**地址**、循环只跑了 353 条指令)。挑一个不参与本行的寄存器最省事,
        /// 再加上 `入栈 / 出栈` 把它护住,连"调用方正拿它装着活值"这一层也不用管。
        /// </para>
        /// <para>全被占满(理论上不会有:一行最多两个寄存器)⇒ 退回 R14,交给 `入栈/出栈` 兜底。</para>
        /// </summary>
        private static string 选展开暂存(params string[] 操作数寄存器)
        {
            for (int i = 14; i >= 2; i--)
            {
                string 候选 = $"R{i}";
                if (!操作数寄存器.Any(x => string.Equals(x, 候选, StringComparison.OrdinalIgnoreCase)))
                    return 候选;
            }
            return "R14";
        }

        /// <summary>
        /// 这一行要不要展开、摊成哪一形态。判据 = **第二操作数解出来的值装不进 20 位**;
        /// 解不出来(形状不对 / 负立即数 / 认不得的写法)⇒ 不展开,**交给正常编码路去报它该报的错**
        /// (那样报错文案最准,也不会把"写错了"悄悄摊成一段能编过的代码)。
        /// </summary>
        private 展开形态C 定展开形态(string 指令行)
        {
            string[] 标记 = 标记化(指令行);
            if (标记.Length < 3) return 展开形态C.不展开;
            byte? 码 = 指令C.取指令码表(标记[0]);
            if (码 is null) return 展开形态C.不展开;
            var 格式 = 指令C.取格式(码.Value);
            if (格式 != 指令格式.寄存器地址 && 格式 != 指令格式.寄存器立即数) return 展开形态C.不展开;

            // 那几条"本身就是低位那一半"的指令不参与展开(否则会自己套自己):
            // 载入高位 / 或立即数 / 符号扩展 / 零扩展 的第二操作数是低位或位宽,天生 ≤ 20 位。
            if (标记[0] is "载入高位" or "LUI" or "或立即数" or "OR_IMMEDIATE" or "符号扩展" or "零扩展")
                return 展开形态C.不展开;

            long 值;
            try
            {
                值 = 解析数值(取第二操作数文本(标记[0], 标记), 格式 == 指令格式.寄存器地址);
            }
            catch { return 展开形态C.不展开; }        // `标签+偏移` / 多写操作数 那些形状交给老路报错
            if (值 < 0 || 值 < (1 << 20)) return 展开形态C.不展开;

            if (格式 == 指令格式.寄存器立即数)
            {
                // ⚠ `加载立即数` **不摊**:它是 ISA 里那条 20 位立即数的指令(助记符 `LOAD_IMMEDIATE`),
                // 操作数装不下就**该当场报错**,不许悄悄摊成两条(`编码指令` 那道 20 位闸正要报它,
                // 见自检「立即数超 20 位报错」)。生成器 `生成浮点打印例程.py` 的同一条自查也这么写:
                // "`寄存器立即数` 档的立即数只有 20 位 ⇒ 写 `加载立即数 R10 10000000` 会被汇编器当场拒"。
                // 语言面那条路本来就不经过这儿:中间层发常量走 `输出装载常量`(≤20 位一条、否则 `载入高位 + 或立即数`)。
                if (标记[0] is "加载立即数" or "LOAD_IMMEDIATE") return 展开形态C.不展开;
                // 剩下的"参与运算"那一档(比较 / 加 / 减 / 或立即数)照 H71 的表摊成大常量算术
                return 展开形态C.算常量;
            }

            if (指令C.带跳转目标(码.Value)) return 展开形态C.跳转;                 // 为零跳转 Rd, 大目标
            return 标记[0] switch
            {
                "取地址" or "LOAD_ADDRESS" => 展开形态C.存取地址,
                "加载" or "LOAD" => 展开形态C.读地址,
                "取字" or "LOAD_WORD" => 展开形态C.读地址,
                "存储" or "STORE" => 展开形态C.写地址,
                "存字" or "STORE_WORD" => 展开形态C.写地址,
                _ => 展开形态C.不展开,
            };
        }

        /// <summary>
        /// 把一行按形态摊开并编码(拼成连续的几段 4 字节)。
        /// 暂存寄存器固定 **R14**,写 / 算两档用 `入栈 / 出栈` 护住。
        /// </summary>
        private byte[] 编码展开行(string 指令行, 展开形态C 形态)
        {
            string[] 标记 = 标记化(指令行);
            var 格式 = 指令C.取格式(指令C.取指令码表(标记[0])!.Value);
            long 值 = 解析数值(取第二操作数文本(标记[0], 标记), 格式 == 指令格式.寄存器地址);
            uint 高位 = (uint)(值 >> 12) & 0xFFFFF;    // 载入高位 Rd, N ⇒ Rd = N << 12
            uint 低位 = (uint)值 & 0xFFF;              // 低 12 位
            string Rd = 标记[1];
            // 写 / 算 / 跳 三档要借一个**不参与本行**的暂存寄存器(读那一档拿目标寄存器自己当暂存)
            string 暂 = 选展开暂存(标记[1]);
            string 拼地址 = $"LUI {暂} {高位}";
            string 拼低位 = $"OR_IMMEDIATE {暂} {低位}";

            string[] 片段 = 形态 switch
            {
                展开形态C.存取地址 => [$"LUI {Rd} {高位}", $"OR_IMMEDIATE {Rd} {低位}"],
                展开形态C.装常量 => [$"LUI {Rd} {高位}", $"OR_IMMEDIATE {Rd} {低位}"],
                展开形态C.读地址 => [$"LUI {Rd} {高位}", $"OR_IMMEDIATE {Rd} {低位}", 间接读指令(标记[0], Rd, Rd)],
                展开形态C.跳转 => [$"LUI {暂} {高位}", $"OR_IMMEDIATE {暂} {低位}", $"JZ_REG {Rd} {暂}"],
                展开形态C.写地址 => [$"PUSH {暂}", 拼地址, 拼低位, 间接写指令(标记[0], Rd, 暂), $"POP {暂}"],
                展开形态C.算常量 => [$"PUSH {暂}", 拼地址, 拼低位, 原算术指令(标记[0], Rd, 暂), $"POP {暂}"],
                _ => throw new Exception($"未知的展开形态: {形态}"),
            };

            var 出 = new byte[片段.Length * 4];
            for (int i = 0; i < 片段.Length; i++)
                Buffer.BlockCopy(编码指令(片段[i]), 0, 出, i * 4, 4);
            return 出;
        }

        /// <summary> 读那一档的间接形式(`加载`→`加载间接寻址` / `取字`→`取字间接`) </summary>
        private static string 间接读指令(string 助记符, string 目标寄存器, string 地址寄存器) => 助记符 switch
        {
            "加载" or "LOAD" => $"LOAD_INDIRECT {目标寄存器} {地址寄存器}",
            "取字" or "LOAD_WORD" => $"LOAD_WORD_INDIRECT {目标寄存器} {地址寄存器}",
            _ => throw new Exception($"{助记符} 没有「读」这一档的间接形式"),
        };

        /// <summary> 写那一档的间接形式(`存储`→`存储间接寻址` / `存字`→`存字间接`) </summary>
        private static string 间接写指令(string 助记符, string 源寄存器, string 地址寄存器) => 助记符 switch
        {
            "存储" or "STORE" => $"STORE_INDIRECT {源寄存器} {地址寄存器}",
            "存字" or "STORE_WORD" => $"STORE_WORD_INDIRECT {源寄存器} {地址寄存器}",
            _ => throw new Exception($"{助记符} 没有「写」这一档的间接形式"),
        };

        /// <summary> 算那一档:把"××立即数"换成"×× 寄存器"(常量已在暂存里) </summary>
        private static string 原算术指令(string 助记符, string 目标寄存器, string 暂存寄存器) => 助记符 switch
        {
            "比较立即数" or "CMP_IMMEDIATE" => $"CMP {目标寄存器} {暂存寄存器}",
            "加立即数" or "ADD_IMMEDIATE" => $"ADD {目标寄存器} {暂存寄存器}",
            "减立即数" or "SUB_IMMEDIATE" => $"SUB {目标寄存器} {暂存寄存器}",
            "或立即数" or "OR_IMMEDIATE" => $"OR {目标寄存器} {暂存寄存器}",
            _ => throw new Exception($"{助记符} 没有「算」这一档的展开形式"),
        };

        private byte[] 编码指令(string 指令行)
        {
            string[] 标记 = 标记化(指令行);
            if (标记.Length == 0) throw new Exception("空指令");

            string 操作码字符串 = 标记[0];
            byte? 操作码 = 指令C.取指令码表(操作码字符串) ?? throw new Exception($"未知指令: {操作码字符串}");

            // R1-4:老助记符的**寄存器写法** —— `调用 R0` / `跳转 R0`(第二操作数真是个寄存器名)改走间接那条
            // (`调用寄存器` 0x35 / `跳转寄存器` 0x36)。老写法(标签 / 数值)一个字不破,照下面那张格式表走;
            // 判据只认"整行就两个记号",所以 `跳转 R0+4` 这种没听说过的写法仍然如实报错,不会被悄悄当成寄存器。
            // 另留一道 `!标签表.ContainsKey`:万一手写汇编里真有个叫 `R0` 的标签,标签优先
            // (语言层本来就不许把标签起成寄存器名 —— 那一步会先报"语言里不再用寄存器 R0 了")。
            if (标记.Length == 2 && 指令C.取寄存器间接码(操作码字符串) is { } 间接码
                && !标签表.ContainsKey(标记[1]) && 寄存器C.解析(标记[1], out _))
                操作码 = 间接码;

            uint 编码 = 0;
            // 操作码放入高8位 [31:24]
            编码 |= (uint)(操作码 << 24);

            // 操作数布局由 指令表 里登记的编码格式决定(单一事实源),不再按指令名各写一份 case
            switch (指令C.取格式(操作码.Value))
            {
                case 指令格式.无操作数:
                    // P32 复审顺手补的一个口子:以前 `停止 R5` 这种多写的操作数被**悄悄忽略**;
                    // 指令不该收的东西就当场说清,别让写错的人以为生效了。
                    if (标记.Length != 1)
                        throw new Exception($"{操作码字符串} 不需要操作数(多写了 {标记.Length - 1} 个)");
                    break;

                case 指令格式.单寄存器:
                    if (标记.Length != 2) throw new Exception($"{操作码字符串} 需要一个寄存器参数");
                    int reg = 解析寄存器(标记[1]);
                    编码 |= (uint)(reg << 20); // [23:20]
                    break;

                case 指令格式.双寄存器:
                    if (标记.Length != 3) throw new Exception($"{操作码字符串} 需要两个寄存器参数");
                    int dst = 解析寄存器(标记[1]);
                    int src = 解析寄存器(标记[2]);
                    编码 |= (uint)(dst << 20);
                    编码 |= (uint)(src << 16); // [19:16]
                    break;

                case 指令格式.寄存器立即数:
                case 指令格式.寄存器地址:
                    {
                        // R2-4:以前这两支**共用一份**且都 `允许标签`,于是 `加载立即数 R0, 某代码标签`
                        // 也合法 —— 机器码里装的是地址,源码看着却像立即数。现在只有"地址"那一支才收标签。
                        bool 允许标签 = 指令C.取格式(操作码.Value) == 指令格式.寄存器地址;
                        if (标记.Length < 3) throw new Exception($"{操作码字符串} 需要两个参数（寄存器和立即数/地址）");
                        int reg2 = 解析寄存器(标记[1]);
                        string 值文本 = 取第二操作数文本(操作码字符串, 标记);
                        if (!允许标签 && 标签表.ContainsKey(值文本))
                            throw new Exception($"{操作码字符串} 的第二个参数是**立即数**,不能写标签 `{值文本}`" +
                                " —— 要一个标签的地址,请换成按地址的指令(`取地址` / `加载` / `取字`)");
                        long 值 = 解析数值(值文本, 允许标签);
                        if (值 < 0 || 值 >= (1 << 20))
                        {
                            // 立即数那一档给**装得下这条指令**的改法:拼进寄存器的是它自己。
                            // (以前一律给"先拼地址再走间接寻址" —— 那是地址那一档的话,对
                            //  `加载立即数 R0, 0x100000` 这种问法等于没回答。)
                            string 大值改法 = 允许标签
                                ? "超过它要先把地址拼进寄存器(载入高位 + 或立即数)再走间接寻址"
                                : $"超过它要先把常量拼进寄存器(`载入高位 {标记[1]}, {(uint)值 >> 12}` + " +
                                  $"`或立即数 {标记[1]}, {(uint)值 & 0xFFF}`)";
                            throw new Exception($"立即数/地址超出20位范围: {值}" +
                                $"(`{指令行.Trim()}` —— 直接寻址的这一档只有 20 位;" + 大值改法 + ")");
                        }
                        // 通用化第2批(2026-10):`符号扩展` / `零扩展` 的第二操作数是**位宽**。
                        // 认 1~31,以及 **0 与 32**(两者都表示"整 32 位 = 什么都不做"——
                        // 写 32 在直觉上就是"32 位",拒掉它没有道理);
                        // 越界(比如 40)当场报错 —— 以前那种"静默收下一个说不通的位数"正是要防的事。
                        if (操作码.Value is 0x46 or 0x47 && 值 > 32)
                            throw new Exception($"{(操作码.Value == 0x46 ? "符号扩展" : "零扩展")} 的位宽只能是 1~32" +
                                $"(0 也视作整 32 位 = 什么都不做): {值}");
                        // R2-4:为零跳转的第二操作数是**跳转目标**(它借的就是这一档格式)——
                        // 以前只有 `跳转`(跳转地址格式)查对齐,`为零跳转 R0, 6` 编得过、而且真的跳,
                        // 然后从错位字节流接着解释,最后表现成"程序正常跑完"。
                        if (指令C.带跳转目标(操作码.Value) && (值 & 0x3) != 0)
                            throw new Exception($"跳转目标必须4字节对齐: {值}" +
                                "(指令是 4 字节定长,从两条指令中间跳过去会解释出乱七八糟的字节;把标签对到指令行上)");
                        编码 |= (uint)(reg2 << 20);
                        编码 |= (uint)(值 & 0xFFFFF); // 低20位
                        break;
                    }

                case 指令格式.双寄存器小立即数:
                    {
                        if (标记.Length != 4) throw new Exception($"{操作码字符串} 需要三个参数（两个寄存器和一个 4 位数值）");
                        int d = 解析寄存器(标记[1]);
                        int s = 解析寄存器(标记[2]);
                        long 小 = 解析数值(标记[3], 允许标签: false);
                        if (小 < 0 || 小 > 0xF) throw new Exception($"4 位数值超出范围(0~15): {小}");
                        // R2-4:同一个 4 位字段含义按指令分,越界以前静默收下(VM 里落到 `_ => false` / 按 2^15 缩放访存)
                        if (操作码.Value == 0x2F && !指令C.是有效关系码(小))
                            throw new Exception($"比较取值 的关系码只能是 0~5 或 8~13(第 3 位 1 = 按有符号比): {小}");
                        if (操作码.Value == 0x32 && 小 > 指令C.最大缩放)
                            throw new Exception($"取字索引 的缩放只能是 0~{指令C.最大缩放}(元素宽 = 2^缩放,即 1/2/4/8 字节): {小}");
                        // ISA 扩容 批 2:`取字节` 的第 4 个操作数是"第几个字节"(0~3)——
                        // 与上面两条同一个规矩:**每条带 4 位字段的指令都要自己带闸**,不写就是静默未定义行为。
                        // ISA 扩容 批 3:条件移动的关系码与 `比较取值` 同一张表(0~5 / 8~13)
                        if (操作码.Value is 0x59 or 0x5A && !指令C.是有效关系码(小))
                            throw new Exception($"条件移动 / 比较并条件移动 的关系码只能是 0~5 或 8~13(第 3 位 1 = 按有符号比): {小}");                        if (操作码.Value == 0x51 && 小 > 3)
                            throw new Exception($"取字节 只认 0~3(第 0~3 个字节,低位在前): {小}");
                        编码 |= (uint)(d << 20);
                        编码 |= (uint)(s << 16);
                        编码 |= (uint)((小 & 0xF) << 12);   // [15:12]
                        break;
                    }

                case 指令格式.三寄存器小立即数:
                    {
                        if (标记.Length != 5) throw new Exception($"{操作码字符串} 需要四个参数（三个寄存器和 4 位数值）");
                        int 基 = 解析寄存器(标记[1]);
                        int 下标 = 解析寄存器(标记[2]);
                        int 值寄存器 = 解析寄存器(标记[3]);
                        long 小2 = 解析数值(标记[4], 允许标签: false);
                        if (小2 < 0 || 小2 > 0xF) throw new Exception($"4 位数值超出范围(0~15): {小2}");
                        if (操作码.Value == 0x33 && 小2 > 指令C.最大缩放)
                            throw new Exception($"存字索引 的缩放只能是 0~{指令C.最大缩放}(元素宽 = 2^缩放,即 1/2/4/8 字节): {小2}");
                        // ISA 扩容 批 2:`取地址索引` 的缩放与上面那档同口径(只算地址,缩放含义一样)
                        // ISA 扩容 批 3 / 批 4:三寄存器档每一条都要自己带闸(不写 = 静默未定义行为)
                        if (操作码.Value is 0x54 or 0x55 or 0x56 && 小2 > 指令C.最大缩放)
                            throw new Exception($"半字 / 字节索引 的缩放只能是 0~{指令C.最大缩放}(元素宽 = 2^缩放): {小2}");
                        if (操作码.Value == 0x57 && 小2 != 0)
                            throw new Exception($"内存拷贝 的第四个操作数是**保留位**,只许写 0: {小2}");                        if (操作码.Value == 0x53 && 小2 > 指令C.最大缩放)
                            throw new Exception($"取地址索引 的缩放只能是 0~{指令C.最大缩放}(元素宽 = 2^缩放,即 1/2/4/8 字节): {小2}");
                        编码 |= (uint)(基 << 20);
                        编码 |= (uint)(下标 << 16);
                        编码 |= (uint)(值寄存器 << 12);
                        编码 |= (uint)((小2 & 0xF) << 8);   // [11:8]
                        break;
                    }

                case 指令格式.跳转地址:
                    if (标记.Length != 2) throw new Exception($"{操作码字符串} 需要一个地址参数");
                    long addr = 解析数值(标记[1], 允许标签: true);
                    if (addr < 0 || addr >= (1 << 26)) throw new Exception($"地址超出24位（实际26位）范围: {addr}");
                    if (指令C.带跳转目标(操作码.Value) && (addr & 0x3) != 0)
                        throw new Exception($"跳转目标必须4字节对齐: {addr}" +
                            "(指令是 4 字节定长,从两条指令中间跳过去会解释出乱七八糟的字节;把标签对到指令行上)");
                    uint target = (uint)(addr >> 2); // 右移2位存入24位字段
                    编码 |= (target & 0xFFFFFF);     // 低24位
                    break;

                default:
                    throw new Exception($"未处理的指令格式: {指令C.取格式(操作码.Value)} ({操作码字符串})");
            }

            // 转为小端字节序（低字节在前）
            byte[] bytes =
            [
                (byte)(编码 & 0xFF),
                (byte)((编码 >> 8) & 0xFF),
                (byte)((编码 >> 16) & 0xFF),
                (byte)((编码 >> 24) & 0xFF),
            ];
            return bytes;
        }

        private static string[] 标记化(string 行) => 分词器C.分词(行);

        /// <summary>
        /// 取"逗号之后那一段"的原文。分词会把 `标签+偏移`(如 `_v1+1`)拆成多个记号,这里拼回整体;
        /// 但**只允许"接着一个 + 号"的续接** —— 别的多写记号是写错了:`加载 R0, 0x10 99` 以前被
        /// `string.Concat(标记.Skip(2))` 静默拼成 `0x1099`(编出来的机器码与源码字面不符,R2-4)。
        ///
        /// **T30④(2026-09-27 评测整改)**:拼之前先认一眼"这其实是**负立即数**"——
        /// `加载立即数 R0, -1` 的词元是 `加载立即数 | R0 | , | - | 1`,以前走到这儿报的是
        /// "**多写了 1 个操作数** …… 请写成 `标签+1`",与真实病因(不支持负立即数)风马牛不相及。
        /// </summary>
        private static string 取第二操作数文本(string 操作码字符串, string[] 标记)
        {
            if (标记.Length == 3) return 标记[2];
            负立即数提示(操作码字符串, 标记);
            for (int i = 3; i < 标记.Length; i++)
            {
                bool 该是加号 = (i % 2) == 1;          // `标签 + 1` / `标签+1` 这两种写法下,奇数位才是那个 `+`
                if ((标记[i] == "+") != 该是加号)
                    throw new Exception($"{操作码字符串} 多写了 {标记.Length - 3} 个操作数" +
                        "(寄存器和地址之间只能写一个地址或标签;地址里要加偏移请写成 `标签+1`)");
            }
            return string.Concat(标记.Skip(2));
        }

        /// <summary> `-` 后面直接跟数字/十六进制数 = 想写负立即数(内联汇编这一层不收) </summary>
        private static readonly Regex 负数字面量 = new(@"^-\s*(?:\d|0[xX][0-9A-Fa-f])", RegexOptions.Compiled);

        /// <summary>
        /// T30④:负立即数给**对症**的报错。`加载立即数 R0, -1` 以前那句话把人往 `标签+1` 上引
        /// (那是另一个场景的改法),真正的病因是"这一层不收负数" —— 现在直接说清 + 给能用的写法。
        /// </summary>
        private static void 负立即数提示(string 操作码字符串, string[] 标记)
        {
            string 拼 = string.Concat(标记.Skip(2));
            if (!负数字面量.IsMatch(拼)) return;
            throw new Exception($"{操作码字符串} 的立即数不支持负数(读到的第二操作数是 `{拼}`),写 `-1` 时" +
                "分词已把它拆成 `-` 和 `1` 两个记号 —— 负数在内联汇编这一层没有编码位," +
                "请改用「先装绝对值再取反」:`" + 操作码字符串 + " 目标寄存器, 1` + `取反 目标寄存器`");
        }

        private int 解析寄存器(string 标记)
        {
            if (!寄存器C.解析(标记, out int 编号))
                throw new Exception($"无效寄存器: {标记} (仅支持 {寄存器C.名称(0)}-{寄存器C.名称(寄存器C.数量 - 1)})");
            return 编号;
        }

        private long 解析数值(string 标记, bool 允许标签)
        {
            标记 = 标记.Trim();
            if (允许标签 && 标签表.TryGetValue(标记, out int value))
                return value;

            // 标签+偏移:如 变量槽+1(短整数的次字节存储用)
            if (允许标签)
            {
                int 加号 = 标记.IndexOf('+');
                if (加号 > 0)
                {
                    string 左 = 标记[..加号].Trim();
                    string 右 = 标记[(加号 + 1)..].Trim();
                    if (标签表.TryGetValue(左, out int 基址) && 解析纯数(右, out long 偏移))
                        return 基址 + 偏移;
                }
            }

            if (标记.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                string hex = 标记[2..];
                if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out long hv))
                    return hv;
                throw new Exception($"无效十六进制数: {标记}");
            }

            if (long.TryParse(标记, out long 值))
                return 值;

            if (允许标签)
                throw new Exception($"未定义标签或无效数值: {标记}");
            else
                throw new Exception($"无效数值: {标记}");
        }

        /// <summary> 纯数值(十进制/0x 十六进制),用于 标签+偏移 的偏移部分 </summary>
        private static bool 解析纯数(string 文本, out long 值)
        {
            值 = 0;
            if (文本.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(文本[2..], System.Globalization.NumberStyles.HexNumber, null, out 值);
            return long.TryParse(文本, out 值);
        }

        [GeneratedRegex(@"\s+")]
        private static partial Regex 替换空白符正则();
    }
}
