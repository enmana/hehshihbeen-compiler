using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 工程文件(.hbp,JSON):显式记录"这个工程由哪些文件组成、入口是哪个、带哪些资源"。
    ///
    /// 为什么要有它:目录扫描只能表达"这个目录里所有 .hb 都算",说不了顺序、入口和资源;
    /// 工程文件把这些写死成数据,于是 —— 桌面 IDE / 命令行 hbcc / 手机版用的是同一份声明,
    /// 而且"复用"也不必再靠 包含 把源码糊在一起(要共享函数/结构仍然得用 包含,见说明文档)。
    ///
    /// 路径规则:文件里写的都是**相对工程文件的路径**(也可以用绝对路径),加载时统一解析成绝对路径。
    /// 放在 引擎\ 里:纯数据 + JSON,零 UI 依赖,自检/CLI/WPF/MAUI 都能用。
    /// </summary>
    public sealed class 工程文件C
    {
        /// <summary> 约定扩展名 </summary>
        public const string 扩展名 = ".hbp";

        /// <summary> 没写入口时自动找的入口文件名 </summary>
        public const string 默认入口名 = "主程序.hb";

        public string 名称 { get; set; } = "";

        /// <summary> 入口文件(相对路径);它是机器码的第一段,程序从这里开始跑 </summary>
        public string 启动文件 { get; set; } = "";

        /// <summary> 参与联合编译的源文件(相对路径,按顺序;启动文件会被排到第一个) </summary>
        public List<string> 文件 { get; set; } = [];

        /// <summary> 随工程一起带的资源文件(相对路径):目前用于"执行时作为虚拟机输入流",也可以只是记录 </summary>
        public List<string> 资源 { get; set; } = [];

        /// <summary>
        /// **汇编源码表**(C22,用户 2026-09-19:"要参与编译需要手动设置"):列出**参与编译**的 `.hba`
        /// (手写的纯汇编例程)。**默认空表 = 一个都不参与编译** —— C20 存出来的 `.hba` 因此照旧只是"看的东西"。
        ///
        /// 口径(与 `文件` 表分开写,是为了不动启动文件/顺序/资源那几套老规矩):
        /// <list type="bullet">
        /// <item>编译时**接在 `文件` 表全部展开结果之后**(顺序 = 这里的声明顺序)—— 入口仍是启动文件的第一条指令,
        ///       汇编段里的标签与 `.hb` 展开出来的标签**全局共用**(两遍汇编,前向引用天然支持);</item>
        /// <item>它们是**真的源码**:能单步、能下断点、能改完重编(单步黄条上的行号就是 `.hba` 自己的行号);</item>
        /// <item>写的是**相对 `.hbp` 的路径**(与 `文件` / `资源` 同一套);</item>
        /// <item>同一个文件既在 `文件` 表又在 `汇编` 表 → 按 `文件` 表算(规范化时从本表里去掉,不会编两遍)。</item>
        /// </list>
        /// 为什么不让用户直接写进 `文件` 表:那张表是"按顺序跑的源码",汇编段挤在中间会悄悄改掉入口语义;
        /// 单列一档,顺序也就定死成"源码在前、汇编在后"。
        /// </summary>
        public List<string> 汇编 { get; set; } = [];

        /// <summary> 本工程还包含的其它工程(相对 .hbp 路径):打开项目时一起读进来(自动防循环) </summary>
        public List<string> 子工程 { get; set; } = [];

        /// <summary>
        /// **可执行文件白名单**(D1 进程启动):名字 → 可执行文件路径(相对 `.hbp` 的路径,或绝对路径)。
        /// HB 侧只能 `运行进程("名字", …)` —— **给不了路径**,只能从这个表里按名字取。
        /// 为什么这么设计:HB 程序(尤其示例)谁都能改,不能让它随手起任意命令;工程作者**明确列出来**的那几个才许起。
        /// 空表 = 这个工程没有"起进程"这个能力(调用一律返回 `假`,状态码 = 没挂能力)。
        /// </summary>
        public Dictionary<string, string> 可执行文件 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// **网址白名单**(D5 网络能力):HB 程序 `取网址("http://主机:端口/路径", …)` 只能连这里声明过的
        /// **主机 + 端口**(路径随便写)。一行一条,认 `主机`、`主机:端口`、`http://主机:端口` 三种写法;
        /// 端口留空 = 按协议默认端口(http 80 / https 443)。空表 = 这个工程没有网络能力(调用一律返回 `假`)。
        /// 为什么这么设计:与 `可执行文件` 同一个道理 —— HB 程序谁都能改,不能让它随手往外发数据;
        /// 工程作者明确列出来的那几个才许连。**只做同步一次性 GET**,不做长连接。
        /// </summary>
        public List<string> 网址 { get; set; } = [];

        /// <summary> 说明(随便写) </summary>
        public string 说明 { get; set; } = "";

        /// <summary>
        /// 运行内存大小(字节)。**留空 = 默认 1 MB**;可以写 `1MB` / `512KB` / 直接写字节数 `1048576`。
        /// 为什么要可调:小程序想在"小内存"上跑(逼自己省着用、或者早点发现越界),或者反过来想确认 1 MB 够不够。
        /// 上限 64 MB(通用化之后不再受"地址字段只有 20 位"卡着 —— 超过 20 位的地址由编译器改走间接寻址)。
        /// </summary>
        public string 内存 { get; set; } = "";

        /// <summary>
        /// 堆区大小(字节)。**留空 = 默认 64 KB**;写法与 `内存` 一样(`128KB` / `1MB` / 纯字节数 `131072`)。
        /// 为什么要可调:要处理的数据比 64 KB 大就配大一点;小程序想省内存、想早点撞上"申请不到"就配小一点。
        /// 堆是从运行内存里切出来的一块,所以不能超过 `内存`;上限跟着内存上限走。
        /// </summary>
        public string 堆 { get; set; } = "";

        /// <summary>
        /// 字符串池**一开始**的大小(字节)。**留空 = 默认 32 KB**;写法与 `内存` 一样(`32KB` / `256KB` / 纯字节数)。
        /// 为什么要可调:池装的是运行期的字符串与动态列表,要一次读进一个大文件就配大一点(省得它一层层长大);
        /// 小程序想省内存、想**早点撞上池上限**就配小一点。
        /// 注意:池不是死的 —— 不够会自己按倍增长大,一直长到"运行内存末尾减去给硬件栈留的余量"为止
        /// (`虚拟机C.栈顶保留`);`池` 只是它起步要多大。所以它也不能超过 `内存`。
        /// </summary>
        public string 池 { get; set; } = "";

        /// <summary>
        /// 软栈大小(字节)。**留空 = 按运行内存自动算**(`min(64KB, 内存 ÷ 4)`);写法与 `内存` 一样
        /// (`64KB` / `128KB` / 纯字节数 `65536`)。
        /// 为什么要可调:软栈是**递归/嵌套调用**能有多深的天花板 —— 默认一帧 96 字节,所以 4 KB 只够约 34 层,
        /// 用 HB 写递归下降解析器根本不够(R1-1)。上限 256 KB(约 2184 层)是拍板定的;
        /// 它也是从运行内存里切出来的一块,所以不能超过 `内存`。
        /// </summary>
        public string 软栈 { get; set; } = "";

        /// <summary>
        /// **函数帧大小(字节,H34)**。**留空 = 默认 96**;写法与 `内存` 一样
        /// (`128` / `256` / 纯字节数)。
        /// 为什么要可调:帧是"参数 + 局部变量 + 表达式临时槽"**共用**的一块,装不下就报「帧超出」;
        /// 参数多 / 局部多的函数以前只能拆函数绕,现在可以把帧调大(代价:同样的软栈能装的层数变少)。
        /// 三条硬规矩:范围 `中间层C.最小帧大小` ~ `最大帧大小`(96 ~ 4096)、必须是 **4 的倍数**、
        /// 必须 **≤ 软栈**(帧就住在软栈里)。写错了一律报错,报错里带改法。
        /// </summary>
        public string 帧 { get; set; } = "";

        /// <summary>
        /// **数组 / 列表的运行期下标越界检查**(H14)。**留空 = 查**(默认,现在的行为);
        /// 写 `"关"` = 一条都不发(每次下标访问省 2~3 条指令,极限速度那一档)。
        /// 写 `"开"` 与留空等价(显式写明"要查")。别的值一律报错(说人话 + 给改法)。
        ///
        /// 为什么要有:越界检查**默认就查**(定长数组 + 列表都查;没有它越界会静悄悄写坏隔壁变量),
        /// 但热路径上它是要花指令的。谁自己保证不越界、又在意那几条,就把这一档关掉。
        /// **关掉之后越界的后果如实存在**(写到隔壁去)—— 这是工程作者自己的选择,编译器不再拦。
        /// 只管数组 / 列表:编译期算得出的常量下标越界**照旧报错**,字符串取字的下标检查也不归它管。
        /// </summary>
        public string 边界 { get; set; } = "";

        // ---------------- 加载/保存 ----------------
        [JsonIgnore] public string 工程路径 { get; private set; } = "";

        [JsonIgnore] public string 目录 => 工程路径.Length == 0 ? "" : 虚拟路径C.所在目录(工程路径);

        private static readonly JsonSerializerOptions 选项 = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 中文别被转成 \uXXXX
            // 工程文件是给人手改的:允许写 // 注释与末尾多余逗号(用户想在自己工程里留一句备注,不该因此打不开)
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>
        /// 读工程文件;读不了直接抛(消息里带原因)。
        /// H64:路径**可能来自系统目录**(手机端 SAF 的虚拟路径)—— 那一档不做 `Path.GetFullPath`
        /// (它会把 `内容://…` 当成相对路径去解析、算出一个谁都不认识的名字),原样留着。
        /// </summary>
        public static 工程文件C 加载(string 路径)
        {
            string 全 = 虚拟路径C.是虚拟(路径) ? 虚拟路径C.收正(路径) : Path.GetFullPath(路径);
            if (!虚拟文件C.存在(全)) throw new FileNotFoundException($"找不到工程文件: {路径}", 全);
            工程文件C? 工程;
            try
            {
                工程 = JsonSerializer.Deserialize<工程文件C>(虚拟文件C.读文本(全), 选项);
            }
            catch (JsonException ex)
            {
                throw new Exception($"工程文件不是合法 JSON: {Path.GetFileName(全)}({ex.Message});");
            }
            if (工程 == null) throw new Exception($"工程文件是空的: {Path.GetFileName(全)}");
            工程.工程路径 = 全;
            工程.文件 ??= [];
            工程.资源 ??= [];
            工程.汇编 ??= [];
            工程.子工程 ??= [];
            工程.可执行文件 ??= new(StringComparer.OrdinalIgnoreCase);
            工程.网址 ??= [];
            if (工程.名称.Length == 0) 工程.名称 = Path.GetFileNameWithoutExtension(全);
            工程.规范化();
            return 工程;
        }

        /// <summary>
        /// 读一整棵工程树:自己 + 子工程(深度优先)。同一个 .hbp 只读一次(按全路径防循环)。
        /// "打开项目"就走这里:工程文件里写了什么,就按路径把文件和子工程都读出来。
        /// </summary>
        public static List<工程文件C> 读取工程树(string 路径, HashSet<string>? 已访问 = null)
        {
            已访问 ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var 结果 = new List<工程文件C>();
            var 工程 = 加载(路径);
            if (!已访问.Add(工程.工程路径)) return 结果;
            结果.Add(工程);
            foreach (string 子 in 工程.子工程)
            {
                string 子全 = 工程.绝对路径(子);
                if (!虚拟文件C.存在(子全)) throw new FileNotFoundException($"子工程不存在: {子}", 子全);
                foreach (var 孙子 in 读取工程树(子全, 已访问)) 结果.Add(孙子);
            }
            return 结果;
        }

        /// <summary> 在某个目录里找工程文件(找得到就返回路径,找不到返回 null)。
        /// H64:目录可能是系统目录那一档(虚拟路径)—— 问在不在 / 列名字都走 `虚拟文件C` </summary>
        public static string? 找工程文件(string 目录)
        {
            if (!虚拟文件C.是目录(目录)) return null;
            string 同目录名 = 虚拟路径C.文件名(目录.TrimEnd('/', '\\'));
            if (同目录名.Length > 0)
            {
                string 同名 = 虚拟路径C.拼接(目录, 同目录名 + 扩展名);
                if (虚拟文件C.存在(同名)) return 同名;
            }
            var 全部 = 虚拟文件C.列文件(目录, 扩展名).OrderBy(f => f, StringComparer.Ordinal).ToArray();
            return 全部.Length > 0 ? 全部[0] : null;
        }

        /// <summary>
        /// 按目录内容生成一份工程文件(新建工程用):所有 .hb 进 文件 表(默认入口排第一),其余给 资源。
        /// </summary>
        /// <summary>
        /// 新建项目时写进 主程序.hb 的起步内容:一按"执行"就有输出断言,不用先学语法。
        /// </summary>
        public const string 新建主程序文本 = """
            // 这是新建项目的入口文件(启动文件):机器码从第一行开始执行
            // 想拆文件:左侧项目右键 / 文件菜单 → 新建文件;想在文件之间共享函数和结构,用 包含 "路径"
            // 写完点"汇编"再点"执行",IDE 会按最后一行的预期输出自动断言

            变量 起点 = 1;

            方法 加(短整数 a, 短整数 b)  返回 整数 {
                返回 a + b;
            }

            测试输出(加(起点, 41));
            停止

            // 预期输出: 42
            """;

        public static 工程文件C 从目录生成(string 目录, string? 名称 = null)
        {
            string 全 = 虚拟路径C.是虚拟(目录) ? 虚拟路径C.收正(目录) : Path.GetFullPath(目录);
            var 工程 = new 工程文件C
            {
                名称 = 名称 ?? (虚拟路径C.文件名(全.TrimEnd('/', '\\')) is { Length: > 0 } n ? n : "工程"),
                说明 = "由 IDE 生成的工程文件:文件按顺序联合编译,启动文件排第一;资源可用于执行时的输入流。",
                内存 = "1MB",     // 显式写上,让人直接看到"运行内存是可以调的"(默认就是 1MB)
                堆 = "64KB",      // 同上:堆大小也能在工程文件里改(不写就是 64KB)
            };
            if (虚拟文件C.是目录(全))
            {
                foreach (string f in 虚拟文件C.列文件(全, ".hb").OrderBy(f => f, StringComparer.Ordinal))
                    工程.文件.Add(虚拟路径C.文件名(f));
                foreach (string f in 虚拟文件C.列全部(全).OrderBy(f => f, StringComparer.Ordinal))
                {
                    string 名 = 虚拟路径C.文件名(f);
                    if (名.EndsWith(".hb", StringComparison.OrdinalIgnoreCase)) continue;
                    if (名.EndsWith(扩展名, StringComparison.OrdinalIgnoreCase)) continue;
                    // C20:汇编源码(`.hba`)是"编译出来的那一份"的落点 —— 它既不是源码也不是资源,
                    // 默认不参与工程(真要拿它优化热路径是 C22 那件手动设置的事)。
                    // 不收这一条的话,它会被当成"资源",执行时还会被当成虚拟机输入流喂进去。
                    if (名.EndsWith(汇编源码C.扩展名, StringComparison.OrdinalIgnoreCase)) continue;
                    if (名.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (名.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;
                    工程.资源.Add(名);
                }
            }
            工程.启动文件 = 工程.文件.FirstOrDefault(f => string.Equals(虚拟路径C.文件名(f), 默认入口名, StringComparison.OrdinalIgnoreCase))
                ?? 工程.文件.FirstOrDefault() ?? "";
            return 工程;
        }

        /// <summary>
        /// 保存到那个路径。H64:系统目录那一档(虚拟路径)**不做 `Path.GetFullPath`** ——
        /// 它只是"哪一棵授权的树"的记号,绝对化没有意义(而且会算出个不认识的名字);原样收正即可。
        /// </summary>
        public void 保存(string 路径)
        {
            工程路径 = 虚拟路径C.是虚拟(路径) ? 虚拟路径C.收正(路径) : Path.GetFullPath(路径);
            规范化();
            文本落盘C.写回(工程路径, JsonSerializer.Serialize(this, 选项));
        }

        /// <summary> 顺序规整:启动文件必须进 文件 表且排第一;重复项去掉 </summary>
        public void 规范化()
        {
            var 去重 = new List<string>();
            foreach (string f in 文件)
            {
                string t = f.Trim();
                if (t.Length == 0) continue;
                if (去重.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) continue;
                去重.Add(t);
            }
            文件 = 去重;

            if (启动文件.Trim().Length == 0)
                启动文件 = 文件.FirstOrDefault(f => string.Equals(Path.GetFileName(f), 默认入口名, StringComparison.OrdinalIgnoreCase))
                    ?? 文件.FirstOrDefault() ?? "";
            if (启动文件.Length > 0 && !文件.Any(f => string.Equals(f, 启动文件, StringComparison.OrdinalIgnoreCase)))
                文件.Insert(0, 启动文件);
            // 启动文件排第一,其余保持声明顺序
            文件 = [.. 文件.OrderBy(f => string.Equals(f, 启动文件, StringComparison.OrdinalIgnoreCase) ? 0 : 1)];

            // C22:`汇编` 表同样规整 —— 空项去掉、重复项去掉;已经在 `文件` 表里列过的从本表里拿掉
            // (同一个文件编两遍会"标签重复定义";按 `文件` 表算 = 它本来就是按源码跑的)
            var 汇编去重 = new List<string>();
            foreach (string f in 汇编)
            {
                string t = f.Trim();
                if (t.Length == 0) continue;
                if (文件.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) continue;
                if (汇编去重.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) continue;
                汇编去重.Add(t);
            }
            汇编 = 汇编去重;
        }

        /// <summary>
        /// 文件 → 绝对路径。H64:工程**本身**在系统目录里(虚拟路径)时,文件表里的相对项跟着它走
        /// (两边都收成同一种斜杠写法,后面既能问盘、也能按名比对);工程在真盘上时还是老口径
        /// (`Path.GetFullPath` 那一套一个字没改)。
        /// </summary>
        public string 绝对路径(string 相对)
        {
            string 目录 = this.目录;
            if (虚拟路径C.是虚拟(相对)) return 虚拟路径C.收正(相对);
            if (虚拟路径C.是虚拟(目录) || 虚拟路径C.是虚拟(工程路径))
                return 虚拟路径C.收正(虚拟路径C.拼接(目录, 相对));
            return Path.IsPathRooted(相对) ? Path.GetFullPath(相对) : Path.GetFullPath(Path.Combine(目录, 相对));
        }

        /// <summary> 名称 / 路径在**给人看**时的那一份(`Path.GetFileName` 的虚拟路径版) </summary>
        public static string 光名(string? 路径) => 虚拟路径C.文件名(路径);

        // ---------------- 运行内存大小 / 堆区大小(工程文件里的 `内存` 与 `堆`) ----------------

        /// <summary>
        /// 把工程文件里写的 `内存` 解析成字节数(留空 = 默认 1 MB)。
        /// 认三种写法:`1MB`/`1M`、`512KB`/`512K`、以及直接的字节数 `1048576`;大小写和空格都不计较。
        /// 写错了就抛出明确错误(带上"该怎么写"),调用方直接显示给用户。
        /// </summary>
        public static int 解析内存(string? 文本) => 解析大小(文本, "内存",
            虚拟机C.默认内存大小, 虚拟机C.最小内存大小, 虚拟机C.最大内存大小,
            $"默认 {大小文本(虚拟机C.默认内存大小)};上限 {大小文本(虚拟机C.最大内存大小)}" +
            "(20 位以内容不下时,编译器自动改走 `载入高位` + 间接寻址)");

        /// <summary>
        /// 把工程文件里写的 `堆` 解析成字节数(留空 = 默认 64 KB)。写法与 `内存` 完全一样。
        /// 上限跟着内存上限走:堆是从运行内存里切出来的一块,不能比能配的运行内存还大。
        /// </summary>
        public static int 解析堆(string? 文本) => 解析大小(文本, "堆",
            中间层C.默认堆大小, 中间层C.最小堆大小, 中间层C.最大堆大小, "堆是从运行内存里切出来的,不能超过 `内存`");

        /// <summary>
        /// 把工程文件里写的 `池` 解析成字节数(留空 = 默认 32 KB)。写法与 `内存` 完全一样。
        /// 这是池**一开始**要多大(它会自己长大,见 `中间层C.默认池大小`);上限同理由内存上限卡着。
        /// </summary>
        public static int 解析池(string? 文本) => 解析大小(文本, "池",
            中间层C.默认池大小, 中间层C.最小池大小, 中间层C.最大池大小, "池是从运行内存里切出来的一块,不能超过 `内存`");

        /// <summary>
        /// 把工程文件里写的 `软栈` 解析成字节数(**留空 = 按运行内存自动算** `min(64KB, 内存÷4)`)。写法与 `内存` 完全一样。
        /// 为什么默认值要现算而不是一个常量:软栈是从运行内存里切出来的一块,写死 64 KB 会让
        /// `"内存": "32KB"` 这种小内存工程当场报"软栈比内存还大"(见 `中间层C.默认软栈大小`)。
        /// </summary>
        public static int 解析软栈(string? 文本, int 内存大小) => 解析大小(文本, "软栈",
            中间层C.默认软栈大小(内存大小), 中间层C.最小软栈大小, 中间层C.最大软栈大小,
            "软栈是从运行内存里切出来的一块,不能超过 `内存`", "64KB、128KB,或者直接写字节数 65536");

        /// <summary>
        /// 把工程文件里写的 `帧` 解析成字节数(H34)。**留空 = 默认 96**;写法与 `内存` 一样(`128` / `256`)。
        /// 比别的尺寸多一条规矩:**必须是 4 的倍数**(帧按 4 字节一个槽排,不是 4 的倍数会错位)——
        /// 这一条在 `解析大小` 之后单独查,报错里写清该写什么样的数。
        /// (与 `软栈` 的搭配关系在 `检查()` 里查:`帧` 不许比 `软栈` 大。)
        /// </summary>
        public static int 解析帧(string? 文本)
        {
            int 帧 = 解析大小(文本, "帧", 中间层C.默认帧大小, 中间层C.最小帧大小, 中间层C.最大帧大小,
                "帧住在软栈里,不能超过 `软栈`", "128、256,或者直接写字节数 128");
            if (帧 % 4 != 0)
                throw new Exception($"工程文件里的 `帧` 必须是 4 的倍数:{文本} = {帧} 字节" +
                    "(帧按 4 字节一个槽排,不是 4 的倍数会错位;可以写 96、128、256);");
            return 帧;
        }

        /// <summary>
        /// 把工程文件里写的 `边界` 解析成"要不要查下标越界"(H14)。**留空 = 查**(现在的行为);
        /// `开` = 查(显式写明),`关` = 一条都不发。别的值抛出"说人话 + 给改法"的错误。
        /// </summary>
        public static bool 解析边界(string? 文本)
        {
            string s = (文本 ?? "").Trim();
            if (s.Length == 0 || s == "开" || s.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (s == "关" || s.Equals("off", StringComparison.OrdinalIgnoreCase)) return false;
            throw new Exception($"工程文件里的 `边界` 写的不对:'{文本}'(可以写 \"开\"(查)或者 \"关\"(不查)," +
                "删掉那一行就是默认的 \"开\");");
        }

        /// <summary>
        /// `1MB` / `512KB` / `1048576` 这种写法的共同解析(内存 / 堆 / 池 / 软栈共用一份,免得几处规矩长歪)。
        /// 留空 = 默认值;写错(不认识的后缀、零/负数、超范围)一律抛出"说人话 + 给改法"的错误。
        /// `写法示例` 是报错里给用户看的可取值例子 —— 各档的上限不一样(软栈只到 256KB),不能用同一句。
        /// </summary>
        private static int 解析大小(string? 文本, string 字段名, int 默认值, int 最小, int 最大, string 范围说明,
            string 写法示例 = "1MB、512KB,或者直接写字节数 1048576")
        {
            string s = (文本 ?? "").Trim().Replace(" ", "").Replace("　", "");
            if (s.Length == 0) return 默认值;
            long 倍 = 1;
            if (s.EndsWith("KB", StringComparison.OrdinalIgnoreCase) || s.EndsWith("K", StringComparison.OrdinalIgnoreCase))
            {
                倍 = 1024;
                s = s[..(s.EndsWith("KB", StringComparison.OrdinalIgnoreCase) ? ^2 : ^1)];
            }
            else if (s.EndsWith("MB", StringComparison.OrdinalIgnoreCase) || s.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                倍 = 1024 * 1024;
                s = s[..(s.EndsWith("MB", StringComparison.OrdinalIgnoreCase) ? ^2 : ^1)];
            }
            if (!long.TryParse(s, out long 数值) || 数值 <= 0)
                throw new Exception($"工程文件里的 `{字段名}` 写的不对:'{文本}'(可以写 {写法示例});");
            long 字节数 = 数值 * 倍;
            if (字节数 < 最小 || 字节数 > 最大)
                throw new Exception($"工程文件里的 `{字段名}` 超范围:{文本} = {字节数} 字节," +
                    $"只能是 {最小} ~ {最大} 字节({范围说明});");
            return (int)字节数;
        }

        /// <summary> 本工程的运行内存大小(字节);没写就是默认 1 MB。写错了会抛(消息可直接给用户看)</summary>
        [JsonIgnore] public int 内存字节数 => 解析内存(内存);

        /// <summary> 本工程的堆区大小(字节);没写就是默认 64 KB。写错了会抛(消息可直接给用户看)</summary>
        [JsonIgnore] public int 堆字节数 => 解析堆(堆);

        /// <summary> 本工程的池**一开始**的大小(字节);没写就是默认 32 KB。写错了会抛(消息可直接给用户看)</summary>
        [JsonIgnore] public int 池字节数 => 解析池(池);

        /// <summary>
        /// 本工程的软栈大小(字节);没写就按运行内存自动算(`min(64KB, 内存÷4)`)。写错了会抛(消息可直接给用户看)。
        /// 注意它要先解析 `内存` —— 自动算的那个默认值就是按内存来的。
        /// </summary>
        [JsonIgnore] public int 软栈字节数 => 解析软栈(软栈, 内存字节数);

        /// <summary> 本工程的函数帧大小(字节,H34);没写就是默认 96。写错了会抛(消息可直接给用户看)</summary>
        [JsonIgnore] public int 帧字节数 => 解析帧(帧);

        /// <summary> 本工程要不要查数组 / 列表的下标越界(H14);没写就是查。写错了会抛(消息可直接给用户看)</summary>
        [JsonIgnore] public bool 边界开关 => 解析边界(边界);

        /// <summary> 字节数给人看的样子:1048576 → "1MB",65536 → "64KB",40000 → "40000 字节" </summary>
        public static string 大小文本(int 字节数) =>
            字节数 % (1024 * 1024) == 0 ? $"{字节数 / (1024 * 1024)}MB"
            : 字节数 % 1024 == 0 ? $"{字节数 / 1024}KB"
            : $"{字节数} 字节";

        /// <summary> 内存大小给人看的样子(与 大小文本 同一个实现,名字留着给用户看得懂)</summary>
        public static string 内存文本(int 字节数) => 大小文本(字节数);

        /// <summary> 联合编译用:启动文件在第一个的 (绝对路径, 内容) 表 </summary>
        public List<(string 名称, string 内容)> 读全部源文件()
        {
            var 结果 = new List<(string, string)>();
            foreach (string f in 文件)
            {
                string 全 = 绝对路径(f);
                if (!虚拟文件C.存在(全)) throw new FileNotFoundException($"工程里列的文件不存在: {f}", 全);
                结果.Add((全, 文本落盘C.读源文件(全)));   // T16:统一走"严格 UTF-8,不是就给编码提示"
            }
            return 结果;
        }

        /// <summary>
        /// C22:`汇编` 表里那些 `.hba` 的 (绝对路径, 内容),按声明顺序。
        /// 它们**已经是展开好的纯汇编**,所以进汇编器时不能当源码再展开一遍 —— 现有管线本来就把
        /// "首词是指令"的行原样透传,所以与 `读全部源文件` 拼在一起直接编就是对的(实测坐实过)。
        /// </summary>
        public List<(string 名称, string 内容)> 读汇编源码()
        {
            var 结果 = new List<(string, string)>();
            foreach (string f in 汇编)
            {
                string 全 = 绝对路径(f);
                if (!虚拟文件C.存在(全)) throw new FileNotFoundException($"工程里列的汇编源码不存在: {f}", 全);
                结果.Add((全, 文本落盘C.读源文件(全)));   // T16:同一读入口
            }
            return 结果;
        }

        /// <summary>
        /// C22:**这次编译的全部输入** = `文件` 表(启动文件排第一)+ `汇编` 表(接在后面)。
        /// 顺序是定死的,且只有这一处定 —— 命令行按它编;界面那条路按工程树里的同一顺序拼
        /// (`项目项C.源文件` 先列 `文件` 再列 `汇编`)。
        /// </summary>
        public List<(string 名称, string 内容)> 读全部编译输入()
        {
            var 表 = 读全部源文件();
            表.AddRange(读汇编源码());
            return 表;
        }

        /// <summary> 联合编译用:启动文件在第一个的 (绝对路径, 内容, 所属工程) 表 —— 文件集合与 `读全部源文件` 一字不差,
        /// 只多一列"这个文件属于哪个 `.hbp` 工程"(`程序集` 的可见性判据) </summary>
        /// <param name="树">
        /// 整棵工程树(见 `读取工程树`);不传 = 每个文件都算本工程(`程序集` 与 `公共` 等价)。
        /// </param>
        public List<(string 名称, string 内容, string 工程)> 读全部源文件带归属(IReadOnlyList<工程文件C>? 树 = null)
        {
            var 表 = 读全部源文件();
            var 归属 = 源文件归属([.. 表.Select(x => x.名称)], 树);
            var 结果 = new List<(string, string, string)>(表.Count);
            for (int i = 0; i < 表.Count; i++) 结果.Add((表[i].名称, 表[i].内容, 归属[i]));
            return 结果;
        }

        /// <summary>
        /// 这次联合编译里"每个源文件属于哪个工程"(与 `文件全路径表` 一一对应),给 `汇编器C` 的 `工程归属` 用。
        /// 给了 `树`(整棵工程树)时按 `归属表` 算:**借来一起编译的库文件仍归库工程** ——
        /// 库里的 `程序集` 成员因此挡得住程序工程。不给 `树` = 都算本工程(`程序集` 与 `公共` 等价)。
        /// </summary>
        public List<string> 源文件归属(IReadOnlyList<string> 文件全路径表, IReadOnlyList<工程文件C>? 树 = null)
        {
            var 归属 = 树 is { Count: > 0 } ? 归属表(树) : null;
            var 结果 = new List<string>(文件全路径表.Count);
            foreach (string f in 文件全路径表)
            {
                string 名 = 名称;
                if (归属 != null && f.Length > 0)
                {
                    string 全;
                    try { 全 = Path.GetFullPath(f); } catch { 全 = f; }
                    if (归属.TryGetValue(全, out string? 属) && !string.IsNullOrEmpty(属)) 名 = 属;
                }
                结果.Add(名);
            }
            return 结果;
        }

        /// <summary>
        /// 整棵工程树里"每个源文件属于哪个工程"(键 = 文件全路径,值 = 工程名)。
        /// 同一个文件被多个工程列出来时,归**目录离它最近**的那个工程 —— 库文件归库工程:
        /// 程序工程把库的 `.hb` 列进自己的文件表一起编译,库里的 `程序集` 成员也仍然只算库工程的。
        /// </summary>
        public static Dictionary<string, string> 归属表(IReadOnlyList<工程文件C> 树)
        {
            var 结果 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var 目录长 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var 工程 in 树)
                foreach (string f in 工程.文件.Concat(工程.汇编))
                {
                    string 全;
                    try { 全 = Path.GetFullPath(工程.绝对路径(f)); } catch { continue; }
                    int 匹配 = 目录匹配长度(工程.目录, 全);
                    if (结果.ContainsKey(全) && 目录长[全] >= 匹配) continue;   // 已经有更"亲"的工程认领了
                    结果[全] = 工程.名称;
                    目录长[全] = 匹配;
                }
            return 结果;
        }

        /// <summary> 工程目录是文件全路径的前缀有多长(不是前缀返回 -1);用来判"哪个工程离这个文件最近"。
        /// H64:系统目录那一档两边都是虚拟路径,只做字符串层面的规整(不去 `Path.GetFullPath`) </summary>
        private static int 目录匹配长度(string 目录, string 文件全路径)
        {
            if (目录.Length == 0) return -1;
            string 规整;
            if (虚拟路径C.是虚拟(目录) || 虚拟路径C.是虚拟(文件全路径))
            {
                规整 = 虚拟路径C.规整(目录).TrimEnd('/');
                string 文件 = 虚拟路径C.规整(文件全路径);
                return 文件.StartsWith(规整 + "/", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(文件, 规整, StringComparison.OrdinalIgnoreCase) ? 规整.Length : -1;
            }
            try { 规整 = Path.GetFullPath(目录).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return -1; }
            return 文件全路径.StartsWith(规整 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(文件全路径, 规整, StringComparison.OrdinalIgnoreCase) ? 规整.Length : -1;
        }

        /// <summary> 资源 → 绝对路径(不存在返回 null) </summary>
        public string? 取资源路径(string 相对)
        {
            string 全 = 绝对路径(相对);
            return 虚拟文件C.存在(全) ? 全 : null;
        }

        /// <summary> 校验:文件表空 / 启动文件不在表里 / 几个尺寸(`内存`/`堆`/`池`/`软栈`/`帧`)写错或配不上 都算问题(返回给用户看的问题清单) </summary>
        public List<string> 检查()
        {
            var 问题 = new List<string>();
            if (文件.Count == 0) 问题.Add("文件表是空的:至少写一个 .hb");
            if (启动文件.Length == 0) 问题.Add("没写启动文件(启动文件是机器码的第一段)");
            foreach (string f in 文件)
                if (!虚拟文件C.存在(绝对路径(f))) 问题.Add($"文件不存在: {f}");
            // C22:勾了"参与编译"却没存盘(或者文件被删了)—— 说清是哪一个,别等汇编器报"找不到"
            foreach (string f in 汇编)
                if (!虚拟文件C.存在(绝对路径(f))) 问题.Add($"汇编源码不存在: {f}(把它取消勾选,或者先存盘)");
            foreach (string r in 资源)
                if (!虚拟文件C.存在(绝对路径(r))) 问题.Add($"资源不存在: {r}");
            foreach (string 子 in 子工程)
                if (!虚拟文件C.存在(绝对路径(子))) 问题.Add($"子工程不存在: {子}");
            // 可执行文件白名单(D1):只查"形状"(名字/路径写没写、名字里有没有空格或换行);
            // **文件在不在**留给运行时判(给出"打不开"状态)—— 换台机器/换个安装路径不该让工程打不开
            foreach (var (名字, 路径) in 可执行文件)
            {
                if (名字.Trim().Length == 0) 问题.Add("可执行文件表里有个**空名字**:名字是 HB 侧 `运行进程(\"名字\", …)` 里写的那个");
                else if (名字.Any(char.IsWhiteSpace)) 问题.Add($"可执行文件的名字 `{名字}` 里有空格/换行:名字只是 HB 侧用的键,别带空白");
                if (路径.Trim().Length == 0) 问题.Add($"可执行文件表的 `{名字}` 没写路径");
            }
            // 网址白名单(D5):解析交给 `网址白名单C`(与运行时**同一份**解析器 —— 检查说行、运行时就得行;
            // 文件在不在/连不连得上留给运行时判,换网络环境不该让工程打不开)
            foreach (string 错 in 网址白名单C.由文本(网址).问题) 问题.Add(错);
            try { _ = 内存字节数; }
            catch (Exception ex) { 问题.Add(ex.Message); }
            try { _ = 堆字节数; }
            catch (Exception ex) { 问题.Add(ex.Message); }
            try { _ = 池字节数; }
            catch (Exception ex) { 问题.Add(ex.Message); }
            try { _ = 软栈字节数; }
            catch (Exception ex) { 问题.Add(ex.Message); }
            try { _ = 帧字节数; }                       // H34:`帧` 写乱了(含"不是 4 的倍数")也要在这儿说清
            catch (Exception ex) { 问题.Add(ex.Message); }
            try { _ = 边界开关; }                      // H14:`边界` 写乱了也要在这儿说清(不然只有编译时才报)
            catch (Exception ex) { 问题.Add(ex.Message); }
            // 堆/池/软栈都是从运行内存里切出来的一块:都写对了也要看它们配不配得上,不然要到"装不进内存"才看出来
            try
            {
                if (堆字节数 > 内存字节数)
                    问题.Add($"`堆` 比 `内存` 还大:堆 {大小文本(堆字节数)} / 内存 {大小文本(内存字节数)}" +
                        "(堆是从运行内存里切出来的一块,请把 `内存` 调大、或者把 `堆` 改小)");
            }
            catch { /* 上面两条已经把"写错了"报出来了,这里不重复 */ }
            try
            {
                if (池字节数 > 内存字节数)
                    问题.Add($"`池` 比 `内存` 还大:池 {大小文本(池字节数)} / 内存 {大小文本(内存字节数)}" +
                        "(池是从运行内存里切出来的一块,请把 `内存` 调大、或者把 `池` 改小)");
            }
            catch { /* 同上 */ }
            try
            {
                if (软栈字节数 > 内存字节数)
                    问题.Add($"`软栈` 比 `内存` 还大:软栈 {大小文本(软栈字节数)} / 内存 {大小文本(内存字节数)}" +
                        "(软栈是从运行内存里切出来的一块,请把 `内存` 调大、或者把 `软栈` 改小)");
            }
            catch { /* 同上 */ }
            // H34:`帧` 与 `软栈` 配不配(帧就住在软栈里 ⇒ 一帧比整块软栈还大,一层调用都放不下)
            try
            {
                if (帧字节数 > 软栈字节数)
                    问题.Add($"`帧` 比 `软栈` 还大:帧 {帧字节数} 字节 / 软栈 {大小文本(软栈字节数)}" +
                        "(帧就住在软栈里,一帧比整块软栈还大就没法装下,请把 `软栈` 调大、或者把 `帧` 改小)");
            }
            catch { /* 同上 */ }
            return 问题;
        }

        /// <summary> 给界面/命令行看的一行摘要 </summary>
        public string 摘要() =>
            $"{名称}:{文件.Count} 个文件 + {资源.Count} 个资源" +
            (汇编.Count > 0 ? $" + {汇编.Count} 个汇编源码" : "") +
            (子工程.Count > 0 ? $" + {子工程.Count} 个子工程" : "") +
            $",启动文件 {(启动文件.Length == 0 ? "(未指定)" : 启动文件)}" +
            $",内存 {(内存.Trim().Length == 0 ? 内存文本(虚拟机C.默认内存大小) + "(默认)" : 内存)}" +
            $",堆 {(堆.Trim().Length == 0 ? 大小文本(中间层C.默认堆大小) + "(默认)" : 堆)}" +
            $",池 {(池.Trim().Length == 0 ? 大小文本(中间层C.默认池大小) + "(默认,会自己长大)" : 池)}" +
            $",软栈 {(软栈.Trim().Length == 0 ? "默认(按内存算)" : 软栈)}" +
            $",帧 {(帧.Trim().Length == 0 ? 中间层C.默认帧大小 + " 字节(默认)" : 帧)}" +
            $",下标越界检查 {((边界 ?? "").Trim().Length == 0 ? "开(默认)" : (边界 ?? "").Trim())}";

        /// <summary> 新建工程时的默认 JSON 文本(给用户看格式用) </summary>
        public static string 模板文本(string 名称 = "我的工程") => $$"""
            {
              // 这份文件就是工程:哪些 .hb 一起编译、谁是启动文件、带哪些资源、跑多大内存、堆留多大、池起步多大
              // 支持 // 注释(改坏了别怕,看不懂的行删掉就行)
              "名称": "{{名称}}",
              "启动文件": "主程序.hb",
              "文件": [
                "主程序.hb",
                "数学库.hb"
              ],
              "资源": [
                "输入.txt"
              ],
              // 汇编源码(C22,可选):手写的纯汇编例程,勾上"参与编译"才列进来 —— 默认一个都不列。
              // 编译时接在上面 文件 表全部展开结果之后(入口仍是启动文件的第一条),标签与源码全局共用。
              // 想看编出来的汇编:编译成功后 IDE 会自动在 项目目录\<项目名>.hba 里开一页(默认不参与编译)。
              // "汇编": [ "热路径.hba" ],
              "子工程": [
                "..\\公共库\\公共库.hbp"
              ],
              // 网络能力(D5,可选):HB 程序 `取网址("http://主机:端口/路径", …)` 只能连这里声明过的主机和端口
              // (只做同步一次性 GET;空表 = 没有网络能力)。删除这几行就不带网络能力。
              "网址": [
                "127.0.0.1:8080",
                "https://api.example.com"
              ],
              // 起进程能力(D1,可选):HB 程序 `运行进程("名字", …)` 只能起这里声明过的可执行文件
              // "可执行文件": { "ffmpeg": "C:/tools/ffmpeg/bin/ffmpeg.exe" },
              "内存": "1MB",
              "堆": "64KB",
              // 字符串池一开始多大(可选):池装运行期的字符串与动态列表,不够会**自己长大**
              // (一直长到"运行内存末尾减去给硬件栈留的余量"),这一行只是它起步要多大 ——
              // 想早点撞上池上限、或者想让展开结果稳定,就写死一个数。
              "池": "32KB",
              // 软栈多大(可选,R1-1):递归/嵌套调用能有多深就看它 —— 默认一帧 96 字节,所以 4KB 只够约 34 层。
              // 不写就是**按内存自动算** min(64KB, 内存/4);要写就写 64KB / 128KB / 65536(上限 256KB)。
              "软栈": "64KB",
              // 函数帧多大(可选,H34):参数 + 局部变量 + 表达式临时槽**共用**这一块(不写就是 96 字节)。
              // 参数多 / 局部多的函数装不下时可以把帧调大 —— 可写 128 / 256 / 4096(范围 96 ~ 4096,
              // 必须是 4 的倍数,而且不能比 `软栈` 大);帧调大 = 同样的软栈能装的层数变少。
              // "帧": "128",
              // 数组 / 列表的**运行期下标越界检查**(可选,H14):写 "关" = 一条都不发(每次下标访问省 2~3 条指令);
              // 不写 / 写 "开" 就是查(默认)。关掉之后越界会**写到隔壁去**,自己保证不越界再用它。
              // "边界": "关",
              "说明": "文件按顺序联合编译(启动文件排第一);资源可用于执行时的输入流;子工程会被一起打开;内存可写 1MB / 512KB / 1048576,堆可写 64KB / 128KB / 131072,池可写 32KB / 256KB / 131072(不写就是默认;池不够会自己长大),软栈可写 64KB / 128KB / 65536(不写就是按内存自动算),帧可写 128 / 256 / 4096(不写就是 96;必须是 4 的倍数,且不能比 `软栈` 大),边界可写 \"关\"(不查下标越界)/ \"开\"(查,默认);网址表里的主机+端口才允许 HB 程序去连"
            }
            """;
    }
}
