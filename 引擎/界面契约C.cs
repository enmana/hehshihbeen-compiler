using System;
using System.Collections.Generic;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **前后端契约(H38)的唯一事实源。**
    ///
    /// 要治的病:前端(WPF 的 `MainWindow.*` / `界面控件` / `选卡控件` / 手机端)与引擎之间
    /// 一直没有一份契约 —— 界面那些 partial 直接 `new 汇编器C()`、直接读 `虚拟机C.内存`、直接调
    /// `宿主能力C` / `图形宿主C` / `文件判定C` 这些引擎静态方法。后果三条:
    /// ① 换前端(MAUI / 浏览器 / HB 写的前端)等于把胶水重写一遍;
    /// ② 没法写"假前端"做自动化(只能按源码钉字符串);
    /// ③ 引擎侧改一处接口,得先扫一遍界面代码。
    ///
    /// 口径(用户 2026-09-29 当班拍板四点,别改):
    /// · **a 粒度**:按能力分四份小接口(`编译服务C` / `调试服务C` / `宿主与文件服务C` / `图形服务C`),
    ///   再由 `界面服务C` 攒成一个门面交给前端 —— 界面只认门面,不认引擎。
    /// · **b 同步还是异步**:**契约是同步的**(值进值出)。界面那侧"继续 / 跑到断点"照旧用现成的
    ///   `在后台跑` 包一层 —— 线程模型一个字不动,界面线程照样不被占。
    /// · **c 图形**:放进来(`图形宿主C` 本来就是"命令进、事件出"的形状)。
    /// · **d hbcc**:同批收「会话状态」那一小块口径(`会话状态C` + `调试服务C.读状态`),
    ///   WPF 与 `hbcc` 两边问的是同一个东西 —— 但两边各自持一份引擎实现(见下面"两个实现")。
    ///
    /// **取用口只有两个**:
    /// · 前端(WPF / 手机端):`前端契约C.服务`(进程内那一份真实现;测试或另一个壳可以用
    ///   `前端契约C.换成(…)` 换掉它 —— 这就是"假前端"能挂上来的口子);
    /// · 另一个实现(命令行那种):自己 `引擎实现C.新一份()`,不走 `前端契约C` 那一格。
    ///
    /// **两个实现**:WPF 前端与 `hbcc`(它本来就在"无 UI"那条路上跑全套)。今天两份都用
    /// `引擎实现C`(同一个真引擎的两次实例化);将来要接浏览器 / HB 自举前端,就是在这里加第三个实现。
    ///
    /// **不许在这儿做别的事**:这一层只做"把界面要的东西列清楚 + 一行转发",
    /// 不带判断、不带状态、不碰 ISA / 语言面 / 机器码。
    /// </summary>
    public static class 前端契约C
    {
        private static 界面服务C _服务 = 引擎实现C.新一份();

        /// <summary> 前端那一份服务(整个前端进程共用这一份,与 `配置\上下文C` 记的"汇编器 / 虚拟机只有一份"同一口径) </summary>
        public static 界面服务C 服务 => _服务;

        /// <summary>
        /// 换一份实现(自检里的"假前端"、将来别的壳都走这儿)。
        /// 传 null 当场报错 —— 悄悄退回默认实现会让"换过了"这件事说不清。
        /// </summary>
        public static void 换成(界面服务C? 实现)
            => _服务 = 实现 ?? throw new ArgumentNullException(nameof(实现), "前后端契约:换实现不许传 null");

        /// <summary> 退回默认那一份(自检用例之间清场用;只为它留一个口,别在别处调) </summary>
        public static void 复位() => _服务 = 引擎实现C.新一份();
    }

    /// <summary> 前面那扇门:四份小接口攒在一起,前端拿一次就够(不再是"哪个 partial 直接 new 哪个引擎对象") </summary>
    public interface 界面服务C
    {
        编译服务C 编译 { get; }
        调试服务C 调试 { get; }
        宿主与文件服务C 宿主 { get; }
        图形服务C 图形 { get; }
    }

    /// <summary>
    /// 会话状态:界面(与命令行)判断"这一份机器码要不要重新装载 / 是接着跑还是从头来"用的那几个数。
    ///
    /// 为什么要有它:同一个口径以前在界面里手写了**两遍** —— 一处 `ReferenceEquals(编.机器码, 当前机器码)`,
    /// 一处逐字节比长度 + `SequenceEqual`。两份判断迟早分叉(WPF 与 hbcc 也各写各的)。
    /// </summary>
    /// <param name="已装载机器码">这台机器上装过东西没有(`调试会话C.有机器码` 那个口径)</param>
    /// <param name="同一份机器码">现在装着的就是问的那一份(逐字节比,不是比引用)</param>
    /// <param name="已停机">已经停机(跑完了 / 撞了错误槽)</param>
    /// <param name="正在等输入">程序正卡在"等你敲"上(这一档**不能**重新装载:会把 PC 与输入队列一起冲掉)</param>
    /// <param name="已执行步数">这台机器上一共执行了多少条指令(调试会话那条腿才有;别的腿给 0)</param>
    public readonly record struct 会话状态C(
        bool 已装载机器码, bool 同一份机器码, bool 已停机, bool 正在等输入, long 已执行步数);

    /// <summary>
    /// 编译那一族:把源码变成机器码 + 调试信息,以及编译结果之外那些**只读的编译期资料**
    /// (诊断 / 折叠区 / 语义列 / 补全 / 工程默认值)。
    ///
    /// 界面侧从此**不许再 `new 汇编器C()`** —— 汇编器只有一份,住在实现里。
    /// </summary>
    public interface 编译服务C
    {
        /// <summary> 编译整个工程(多文件 + 调试信息)。签名与 `汇编器C.汇编多文件带调试` **逐字一致** </summary>
        汇编调试结果C 汇编多文件带调试(
            IEnumerable<(string 名称, string 内容)> 源文件表, bool 优化 = true, int 堆大小 = 中间层C.默认堆大小,
            IReadOnlyList<string>? 工程归属 = null, int 池大小 = 中间层C.默认池大小,
            int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1);

        /// <summary> 单文件汇编(工具栏那个"反汇编"用:只要机器码) </summary>
        byte[] 汇编单文件(string 源代码, string? 基目录 = null);

        /// <summary> 最近一次编译的标签表(标签名 → 字节地址) </summary>
        Dictionary<string, int> 取标签表();

        /// <summary>
        /// H100 第二步:最近一次编译那几个**账本槽**的地址(角色 → 地址:`池指针` / `池末` / `堆首` / `堆末` /
        /// `空闲表头` / `软栈`)。界面把它收进会话(`会话内部槽地址`),运行监视拿它算"池 / 堆 / 软栈用了多少"。
        /// </summary>
        Dictionary<string, int> 取内部槽地址();

        // ---- 最近一次编译结果:界面侧**不再声明 `汇编调试结果C` 这个类型**(自己存 = 攥着引擎对象) ----

        /// <summary> 最近一次编译的完整结果(`汇编多文件带调试` 每次都自动记下;`清最近结果` 清掉) </summary>
        汇编调试结果C? 最近结果 { get; }

        /// <summary> 与 `最近结果` 成对的那一次编译用的源码表(未使用扫描要它看声明行上的可见性前缀) </summary>
        IReadOnlyList<(string 名称, string 内容)>? 最近源码表 { get; }

        /// <summary>
        /// 记下"这一份结果配的是哪张源码表"。**两样必须一起换** —— 结果那一半由 `汇编多文件带调试` 自己记,
        /// `清最近结果` 把两样一起清;这一格只管源码表。
        /// </summary>
        void 设最近源码表(IReadOnlyList<(string 名称, string 内容)>? 源码表);

        /// <summary> 清掉最近结果与它的源码表(编不过 / 没有可编的工程 / 内存装不下时调)</summary>
        void 清最近结果();

        /// <summary>
        /// **当前工程编出来的那一份**(界面上"执行 / 逐步"要装的那一份)。
        ///
        /// 为什么要与 `最近结果` 分两格:界面每 400ms 就**静默编译**一次(编的是编辑器里的文本,
        /// 只为标红线),那个结果会把 `最近结果` 换掉 —— 可机器码 `当前机器码` 指的仍是**正式编译**那一份。
        /// 装载要连**顶层常量初值**一起落进数据段(初值表是编译产物),所以必须认得出"当前机器码出自哪一份"。
        /// 判据就是**引用相等**(`ReferenceEquals(结果.机器码, 当前机器码)`),不许换成按内容比 ——
        /// 那个引用还兼着 `虚拟机C.需要重新装载` 的"是不是同一份"那一档,一比内容就会每按一次 F5 重装一遍
        /// (程序正卡在等你输入时重装 = 把 PC 与输入队列一起冲掉)。
        /// </summary>
        汇编调试结果C? 当前结果 { get; }

        /// <summary> 记下"这一份就是当前工程编出来的那一份"(正式编译成功之后调一次;失败时**不动**它)</summary>
        void 记当前结果(汇编调试结果C? 结果);

        /// <summary>
        /// 把这一份编译结果**寄存**在引擎侧,拿一个号回来(同一份结果重复寄存给同一个号)。
        ///
        /// 为什么要这一格:原生 C 那条腿在**后台线程**上打包,而界面这边每 400ms 就静默编译一次
        /// (`最近结果` 会被换掉)—— 后台那一段必须攥住"当时那一份",不能只认"最近那份";
        /// 界面又攥不住引擎对象,于是攥一个**号**。
        /// </summary>
        int 寄存结果(汇编调试结果C 结果);

        /// <summary> 把**最近这一份**结果转成 C 源码(原生 C / 只编译那两条腿;整份编译结果都不出引擎)</summary>
        C后端结果C 转译成C(int 内存大小);

        /// <summary> 同上,但按**寄存号**取那一份(后台线程那条腿用它攥住"当时那一份") </summary>
        C后端结果C 按号转译成C(int 结果号, int 内存大小);

        // ---- 独立 exe 要贴的那几张材料(界面不再自己翻编译结果的内脏) ----

        /// <summary> 最近这份结果里的顶层常量初值表(空表 = 没有要搬的初值) </summary>
        IReadOnlyList<初值项C> 初值表();

        /// <summary> 最近这份结果的"地址 → 源码位置"表(H7:贴进包,独立 exe 的运行期报错才带行号) </summary>
        IReadOnlyList<文件映射C> 调试文件表();

        /// <summary> 独立 exe 要贴的那两张材料:运行期报错的**消息表** + **错误槽地址**(没有错误槽给 0)</summary>
        (List<string> 消息表, int 错误槽地址) 打包材料();

        // ---- 工程文件里那几个"没写就用默认"的值(工程属性页与提示文案都读它) ----

        int 默认堆大小 { get; }
        int 默认池大小 { get; }
        int 默认帧大小 { get; }
        int 最小帧大小 { get; }
        int 最大帧大小 { get; }
        int 默认内存大小 { get; }
        /// <summary> 运行内存的上限(现在 = `虚拟机C.最大内存大小`)。
        /// 界面报"装不下"时要给用户一个**真的**上限 —— 原来那句写死"最大 1MB",而 `内存` 早就放开到 64MB 了 </summary>
        int 最大内存大小 { get; }

        /// <summary> 软栈默认多大是**按运行内存算**的(所以是个函数,不是一个常数)</summary>
        int 默认软栈大小(int 内存大小);

        /// <summary> 硬件栈在运行内存末尾占多少(内存布局那条说明用它)</summary>
        int 栈顶保留(int 内存大小);

        /// <summary> 中英切换:关键字 + 指令名 / 内建函数名 / 附加类型名一次换齐(顺序在引擎里定死)</summary>
        string 切换中英(bool 使用中文, string 代码);

        // ---- 只读诊断那一票(错误列表 / 折叠区 / 语义列 / 补全 / 检查器) ----

        编译错误C.单页诊断 取诊断(编译异常C? 错, string? 这一页全称, int 这一页行数, bool 是当前页);

        List<(string 全称, int 行, string 文本)> 取各条(编译异常C? 错);

        List<诊断项C> 检查(string 源码, bool 编译通过 = true);

        List<折叠区C.区段> 算折叠区(string 正文);

        string 对齐语义列(string 代码, out string 主换行);

        List<补全候选C> 取补全(补全来源C? 来源, 补全请求C 请求, IReadOnlyCollection<string>? 模糊规则 = null);
    }

    /// <summary>
    /// 调试那一族:两台机器 + 调试会话那一份状态 + 断点表。
    ///
    /// **H38-1** 收了"同一份机器码不重装 / 接着跑 / 正在等输入"那一小块口径(拍板 d);
    /// **H38-2** 把这一族收干净:界面侧
    /// ① **不再直接读** `.内存` / `.寄存器`(18 处 —— 改走下面那三格机器读数);
    /// ② **不再自己攥着引擎对象** —— `调试会话C` / `断点表C` / `虚拟机C?` 那些字段全撤掉,
    ///    一律问这一族要(界面手里只剩字节数组、bool、号这些**纯数据**)。
    ///
    /// 这么分是刻意的:一格 = 界面确实要用的一件小事,**没有一格把整个引擎对象递出去当"你自己看着办"** ——
    /// 于是"假前端"(自检里的 `假前端C`)可以**一份引擎都不 new** 就把这四份接口全替掉。
    /// </summary>
    public interface 调试服务C
    {
        // ---- 两台机器:"执行"那一台与调试会话那一台 ----

        /// <summary>
        /// "执行"用的那一台虚拟机(界面侧那一格从 `上下文.虚拟机` 挪到这儿)。
        /// 可写:H39-2 起"跑程序"那台住在子进程里,壳起来之后要就地换成跨进程那一台。
        /// </summary>
        虚拟机C 机器 { get; set; }

        /// <summary> 调试会话那一台(逐步 / 断点都走它;可写,理由同 `机器`)</summary>
        虚拟机C 会话机器 { get; set; }

        /// <summary> 按"哪一台"取回来(`跑一遍` / `在后台跑` 靠它选机器,不必自己存一个字段)</summary>
        虚拟机C 取机器(bool 会话机);

        /// <summary> 对着"哪一台"下中断请求(「停止」那一步)</summary>
        void 请求中断(bool 会话机 = false);

        /// <summary> 这台机器要不要重新装当前这份机器码(判据在 `虚拟机C.需要重新装载` 里,不许在界面重写一遍)</summary>
        bool 要重新装载(虚拟机C 机, byte[] 机器码);

        /// <summary> 同上,问的是"执行"那一台(省得每个调用点自己把机器递进来)</summary>
        bool 机器要重新装载(byte[] 机器码);

        // ---- 机器读数(界面**不再直接读** `.内存` / `.寄存器`) ----

        /// <summary> 寄存器有几格(界面上那两张表按它循环)</summary>
        int 寄存器个数(虚拟机C 机);

        /// <summary> 第 `号` 格寄存器的值(R0~R15)</summary>
        uint 寄存器值(虚拟机C 机, int 号);

        /// <summary> 内存有多大(界面那份 hex dump 的边界之一)</summary>
        int 内存长度(虚拟机C 机);

        /// <summary> 内存里第 `地址` 个字节(hex dump 用;越界给 0)</summary>
        byte 内存字节(虚拟机C 机, int 地址);

        /// <summary>
        /// 内存**本体**(只读用途:查运行期报错要把它整片递给 `汇编调试结果C.查运行期报错`)。
        /// 别看它是一格转发:界面拿它不是"自己翻内存",而是"递给引擎另一个入口"。
        /// </summary>
        byte[] 内存数组(虚拟机C 机);

        /// <summary> "这一趟是怎么停下来的"是不是「被中断」(「停止」那一步;停下原因是个引擎枚举)</summary>
        bool 是中断停下(虚拟机C.停下原因C 原因);

        /// <summary>
        /// **H100 第二步(运行监视):池 / 堆 / 软栈各用了多少**(算的是这台机器**此刻**那份内存:
        /// 进程内就是那台机的内存,跨进程是**镜像内存** —— 所以这条路不用动协议)。
        /// 返回 (池用, 池顶, 堆用, 堆共, 软栈用, 软栈共);**算不出来的项给 0**(监视条那边"全 0 就不显示",
        /// 不拿 0 冒充"用量是 0")。口径**一处**在 `引擎\内存用量C.cs`。
        /// 为什么返回元组而不是那个结构:界面侧**不许点名引擎类型**(「前后端边界」棘轮按源码文本数)。
        /// </summary>
        (int 池用, int 池顶, int 堆用, int 堆共, int 软栈用, int 软栈共) 内存用量(虚拟机C 机);

        // ---- 装载(“有编译结果、而且就是这一份”判在这儿,界面不再自己拼那两句) ----

        /// <summary> 把这份机器码装进这台机器(有编译结果、而且 `ReferenceEquals` 是这一份 ⇒ 连顶层常量初值一起装)</summary>
        void 装载(虚拟机C 机, byte[] 机器码);

        /// <summary> 按工程文件里的 `内存` 装载(执行那条腿)。**容量是随装载帧走的** ——
        /// `跨进程虚拟机C.装载` 里 `写.Write(内存大小)`,子进程那台照它开内存;调用方(界面)不设就是默认 1MB,
        /// 大内存档当场抛『机器码 N 字节装不进这台虚拟机的内存』(用户实测:`示例60_大内存档` 按 F5)。
        /// **为什么做成契约的一格**:界面侧不许直接摸引擎(前后端边界棘轮只许少不许多),
        /// 所以"比一比、不一样才设"这两句收在引擎这一侧(设内存大小会把栈指针重置到栈顶,一样时绝不能设)。 </summary>
        void 按内存装载(虚拟机C 机, byte[] 机器码, int 内存大小);

        /// <summary> 同上,装进调试会话那一台(逐步也要看得到常量初值)</summary>
        void 装载会话(byte[] 机器码);

        /// <summary>
        /// **H13:热重载** —— 把"刚编出来的这一份"换进**正跑着的那台**,能保的顶层变量接着用
        /// (判据在 `虚拟机C.重载`;保不住什么写在 `虚拟机C.上次重载说明` 里)。
        /// 返回 false = 没有可用的编译结果 / 不是这一份(调用方该先说一句"先编译")。
        /// </summary>
        bool 热重载(虚拟机C 机, byte[] 机器码);

        // ---- 调试会话那一份状态(界面不再自己 `new` / 存 `调试会话C`) ----

        bool 会话有机器码 { get; }
        byte[] 会话机器码 { get; }

        /// <summary> 会话里装的就是这一份机器码(逐字节比,不是比引用)</summary>
        bool 会话机器码就是这一份(byte[] 机器码);

        bool 会话已停机 { get; }
        long 会话已执行步数 { get; }

        /// <summary> 当前停在哪个文件的哪一行(逐步那条路靠它标黄条)</summary>
        string? 会话当前文件 { get; set; }

        /// <summary> 黄条 / 调用处按源码行还是按展开汇编行算(第158批起界面只剩 false 这一档)</summary>
        bool 会话汇编模式 { get; set; }

        /// <summary> 工程文件里的 `内存` 跟着工程走,装载前设一次</summary>
        int 会话内存大小 { get; set; }

        /// <summary> 这一次编译的"地址 → 源码位置"表(和机器码同一次展开出来的,不会对不上)</summary>
        IReadOnlyList<文件映射C> 会话调试文件表 { get; set; }

        /// <summary> 变量槽标签 → 内存地址(变量面板 / 寄存器说明靠它)</summary>
        Dictionary<string, int> 会话标签表 { get; set; }

        /// <summary>
        /// H100 第二步:运行期那几个**账本槽**的绝对地址(角色 → 地址:`池指针` / `池末` / `堆首` / `堆末` /
        /// `空闲表头` / `软栈`)。与 `会话标签表` **同一次编译**出来的(见 `汇编调试结果C.内部槽地址`),
        /// 运行监视拿它 + 镜像内存算"池 / 堆 / 软栈各用了多少"(口径见 `引擎\内存用量C.cs`)。
        /// </summary>
        Dictionary<string, int> 会话内部槽地址 { get; set; }

        /// <summary> 断点落在哪些机器码地址上(按会话那张调试表算出来的)</summary>
        HashSet<int> 会话断点地址 { get; set; }

        /// <summary> 每走到一个断点地址问一次"这次算不算命中"(P24 条件断点)</summary>
        Func<int, bool>? 会话断点命中判断 { get; set; }

        /// <summary> 调用栈(C15 的"调用处"标灰条读它)</summary>
        调用栈标记C 会话调用栈 { get; }

        /// <summary> 把会话清干净(换了工程 / 内存装不下时调)</summary>
        void 会话清空();

        /// <summary> 记下"调试会话这一份机器码装在哪台机器上了"(换了机器就必须重装 —— 空机器上单步什么都走不动)</summary>
        void 记会话装载();

        /// <summary> 上一次记会话装载时的那台机器,和现在这一台还是不是同一台</summary>
        bool 会话装载机换过();

        // ---- 单步 / 继续 / 快照 / 位置 ----

        /// <summary> 走一条**源码行**(编译器生成的序言 / 收尾一口气跑过)</summary>
        调试步进结果C 逐步一次();

        /// <summary> 跑到断点或停机(留在界面上那条"继续"的路)</summary>
        调试步进结果C 运行到断点(long 上限 = 5_000_000);

        /// <summary> 同 `运行到断点`(名字在使用侧那一头)</summary>
        调试步进结果C 继续(long 上限 = 5_000_000);

        /// <summary> 按会话当前状态取一张快照(变量 / 指令 / 寄存器说明 / 调用帧)</summary>
        调试步进结果C 会话快照(bool 这一步跳出去 = false);

        /// <summary> 某个机器码地址对应哪个文件的哪一行(取不到给 null)</summary>
        (string 文件, int 行)? 会话取位置(int 地址);

        /// <summary> 调试会话的状态(同一份机器码 / 已停机 / 正在等输入 / 已执行步数)</summary>
        会话状态C 读状态(byte[]? 机器码 = null);

        /// <summary>
        /// 同上,但问的是**传进来的那台会话**(命令行自己造一份会话跑脚本化调试时用它 —— 拍板 d:
        /// `hbcc` 与 WPF 问的是**同一个出口**、同一份判据)。界面那侧不传会话,走上面那个重载。
        /// </summary>
        会话状态C 读状态(调试会话C 会话, byte[]? 机器码 = null);

        // ---- 断点表:跟着工程走(存在项目目录的 断点.hbd),界面不再自己 new / 存 断点表C ----

        /// <summary> 按当前工程把断点读进来(换工程 / 换启动项目时调;读不到 = 没有断点)</summary>
        void 加载断点(string? 工程目录);

        /// <summary> 某个文件上有哪些断点行(编辑器画红点用)</summary>
        int[] 断点行号(string 全称);

        /// <summary> P34:某个文件上"带附加条件"(条件 / 命中次数 / 日志)的断点行(编辑器画空心圈)</summary>
        int[] 断点附加行号(string 全称);

        /// <summary> 断点换算成机器码地址(编译之后才算得出来)</summary>
        HashSet<int> 断点地址(IReadOnlyList<文件映射C> 调试文件表);

        /// <summary> 地址 → 那个断点本身(P24 条件断点要它的 `条件`)</summary>
        Dictionary<int, 断点项C> 断点地址带条件(IReadOnlyList<文件映射C> 调试文件表);

        bool 有断点(string 全称, int 行);
        string 取断点条件(string 全称, int 行);

        /// <summary> P34:那个断点整项(界面的"断点设置"弹窗要 `条件 / 命中次数 / 日志` 三样)</summary>
        断点项C? 取断点(string 全称, int 行);

        /// <summary> P34:一次把 `条件 / 命中次数 / 日志` 三样都写好;返回 false = 那一行没有断点</summary>
        bool 设断点(string 全称, int 行, string 条件, string 命中次数, string 日志);

        void 设断点条件(string 全称, int 行, string 条件);
        void 保存断点(string? 工程目录);

        /// <summary> 切换一个断点:有就去掉,没有就加上;返回"现在有没有"</summary>
        bool 切换断点(string 全称, int 行);

        int 断点个数 { get; }
        void 清除断点();

        /// <summary> 清掉"已经不成立"的断点(文件不在工程里 / 那一行没代码了);返回清掉了几个</summary>
        int 清失效断点(IReadOnlyList<文件映射C> 调试文件表);

        /// <summary> 给人看的一行摘要(按文件分组):`主程序.hb 第 3、6 行`</summary>
        string 断点摘要();

        /// <summary> 条件断点那一行摘要(没有条件断点给空串)</summary>
        string 断点条件摘要();

        /// <summary> 这张调试表里有没有这个文件的段("这一行能不能下断点"必须先问它)</summary>
        bool 断点表有文件(IReadOnlyList<文件映射C> 调试文件表, string 全称);

        /// <summary> 某一行有没有"能停下来"的地址(null = 这一行没有可执行代码)</summary>
        int? 断点算某行地址(IReadOnlyList<文件映射C> 调试文件表, string 全称, int 行);
    }

    /// <summary>
    /// 宿主与文件那一族:文件系统 / 程序包 / 宿主能力白名单 / 路径判定。
    ///
    /// 为什么这些也算"契约":它们全是**宿主环境**的事(桌面是本地文件、安卓是 SAF 目录、
    /// 将来的浏览器是另一套)—— 界面自己判"两个路径是不是同一个文件"、自己拼程序包,
    /// 换个平台就要重写一遍。
    ///
    /// 注:这里那一格**故意不叫 `同文件`** —— 硬规矩(H48)要求"两个路径是不是同一个文件"的
    /// 判据全仓只剩 `引擎\文件判定C.cs` 一处,自检那条源码钉认的就是**那个名字**;
    /// 契约这一格只转发,换个读起来一样清楚的名字就不与那条规矩打架。
    /// </summary>
    public interface 宿主与文件服务C
    {
        /// <summary> 两个路径是不是同一个文件(大小写 / 分隔符 / 短长名那些差别都在引擎里判,只有一份判据)</summary>
        bool 是同一个文件(string? 左, string? 右);

        string 读源文件(string 路径);
        string 解源文件(byte[] 字节, string 路径 = "");
        void 写回(string 路径, string 文本);

        bool 文件存在(string 路径);
        string 读文本文件(string 路径);
        void 写文本文件(string 路径, string 文本);
        void 建目录(string 目录);

        /// <summary> 换掉文件接口实现(手机端把 SAF 那套挂上来;桌面用默认那一套)</summary>
        void 挂上文件实现(文件接口C? 实现);
        void 摘掉文件实现();
        文件接口C? 当前文件实现 { get; }

        /// <summary> 相对路径的基准目录(全仓唯一的解析口子)</summary>
        void 设基准目录(string? 目录);

        void 设可执行白名单(IReadOnlyDictionary<string, string>? 表);
        void 设网址白名单(网址白名单C? 表);

        /// <summary> 宿主调用那一格(读文件 / 写文件 / 当前时间…):"内存 + 参数区 + 功能号"进,状态码出 </summary>
        int 处理(byte[] 内存, uint 参数区, int 功能号);

        宿主能力种C 问能力(string 可执行路径, int 该到几号 = 0, int 该到包格式 = 0);
        string? 陈旧原因(string 可执行路径, int 该到几号 = 0, int 该到包格式 = 0);
        string 能力上限文本(宿主能力种C 能力);
        string 包格式上限文本(宿主能力种C 能力);
        int 这段机器码要的功能号上限(byte[] 机器码);

        /// <summary> 打独立 exe 的程序包(宿主字节 + 机器码 + 消息表 + 错误槽,可选初值表与源行表)</summary>
        byte[] 打包(byte[] 宿主字节, byte[] 机器码, IReadOnlyList<string> 消息表, int 错误槽地址,
            IReadOnlyList<初值项C>? 初值表 = null, 源行表C? 源行表 = null);

        int 段初值包格式 { get; }

        /// <summary> "地址 → 源码位置"那张表(独立 exe 的运行期报错靠它带行号)</summary>
        List<文件映射C> 建全工程表(
            IReadOnlyList<(string 名称, string 内容)> 文件表, bool 优化 = true, int 堆大小 = 中间层C.默认堆大小,
            IReadOnlyList<string>? 工程归属 = null, int 池大小 = 中间层C.默认池大小,
            int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1);

        源行表C 压源行表(IReadOnlyList<文件映射C>? 段表);
    }

    /// <summary>
    /// 图形那一族:窗口那一侧的"命令进、事件出"(拍板 c)。
    ///
    /// 呈现口住在**前端这一侧**(拍板 a:调试要插得进),所以前端挂口 / 投事件都走这一族;
    /// 子进程那条腿(H39)也认同一份形状。
    /// </summary>
    public interface 图形服务C
    {
        图形呈现口C? 呈现口 { get; }
        void 挂上(图形呈现口C? 口);
        void 摘掉();
        void 清空();
        Action<int, int, int, int>? 事件出口 { get; set; }
        void 投事件(int 类型, int 键码, int x, int y);
        bool 这段机器码用图形(byte[] 机器码);
    }

    /// <summary>
    /// 契约**唯一的一份真实现**:两件事 ——
    /// ① 每一格列一行转发(不带判断、不存第二份状态);
    /// ② 把"汇编器 / 虚拟机各只有一份"这件事落在这儿(以前散在 `配置\上下文C` 里)。
    ///
    /// 它自己就实现对四份接口:门面拿到的四格就是同一个对象,不必再套一层包装。
    /// </summary>
    public sealed class 引擎实现C : 界面服务C, 编译服务C, 调试服务C, 宿主与文件服务C, 图形服务C
    {
        private readonly 汇编器C _汇编器 = new();
        private 虚拟机C _机器 = new();

        // ---- 最近一次编译结果(界面攥不住它,只拿 `最近结果` / `当前结果` 或一个**寄存号**) ----
        private 汇编调试结果C? _最近结果;
        private 汇编调试结果C? _当前结果;
        private IReadOnlyList<(string 名称, string 内容)>? _最近源码表;
        /// <summary> 寄存表:号 → 那一份结果(只留最近 4 份 —— 静默编译每 400ms 换一份,不回收会一直涨)</summary>
        private readonly List<(汇编调试结果C 结果, int 号)> _寄存 = new();
        private int _下一个寄存号 = 1;

        // ---- 调试那一族的两台机器 / 会话 / 断点表(界面一个字段都不留,全在这儿) ----
        private readonly 调试会话C _会话 = new();
        private readonly 断点表C _断点表 = new();
        private 虚拟机C? _会话装载机;

        /// <summary> 新开一份引擎实现(前端启动 / 命令行 / 自检各持一份,互不打扰)</summary>
        public static 引擎实现C 新一份() => new();

        private 引擎实现C() { }

        // ---- 门面:四格就是它自己(不套第二层包装;命令行 / 自检拿具体类型也直接看得见这四格) ----

        public 编译服务C 编译 => this;
        public 调试服务C 调试 => this;
        public 宿主与文件服务C 宿主 => this;
        public 图形服务C 图形 => this;

        // ---- 编译 ----

        public 汇编调试结果C 汇编多文件带调试(
            IEnumerable<(string 名称, string 内容)> 源文件表, bool 优化 = true, int 堆大小 = 中间层C.默认堆大小,
            IReadOnlyList<string>? 工程归属 = null, int 池大小 = 中间层C.默认池大小,
            int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
        {
            var 结果 = _汇编器.汇编多文件带调试(源文件表, 优化, 堆大小, 工程归属, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小);
            _最近结果 = 结果;      // 每次编译都自动记下 —— 界面不必再自己存一份(存了就是"攥着引擎对象")
            return 结果;
        }

        public byte[] 汇编单文件(string 源代码, string? 基目录 = null) => _汇编器.汇编(源代码, 基目录);

        public Dictionary<string, int> 取标签表() => _汇编器.标签表;

        public Dictionary<string, int> 取内部槽地址() => _汇编器.内部槽地址;

        public 汇编调试结果C? 最近结果 => _最近结果;
        public 汇编调试结果C? 当前结果 => _当前结果;
        public void 记当前结果(汇编调试结果C? 结果) => _当前结果 = 结果;
        public IReadOnlyList<(string 名称, string 内容)>? 最近源码表 => _最近源码表;
        public void 设最近源码表(IReadOnlyList<(string 名称, string 内容)>? 源码表) => _最近源码表 = 源码表;
        public void 清最近结果() { _最近结果 = null; _最近源码表 = null; }

        public int 寄存结果(汇编调试结果C 结果)
        {
            foreach (var (已, 号) in _寄存) if (ReferenceEquals(已, 结果)) return 号;
            int 新号 = _下一个寄存号++;
            _寄存.Add((结果, 新号));
            if (_寄存.Count > 4) _寄存.RemoveAt(0);
            return 新号;
        }

        private 汇编调试结果C? 按号取结果(int 号)
        {
            foreach (var (结果, 编号) in _寄存) if (编号 == 号) return 结果;
            return null;
        }

        public C后端结果C 转译成C(int 内存大小)
            => C后端C.转译(_最近结果 ?? throw new InvalidOperationException("前后端契约:还没有编译结果,转不成 C"), 内存大小);

        public C后端结果C 按号转译成C(int 结果号, int 内存大小)
            => C后端C.转译(按号取结果(结果号)
                ?? throw new ArgumentException($"前后端契约:没有这个寄存号({结果号})", nameof(结果号)), 内存大小);

        public IReadOnlyList<初值项C> 初值表() => _最近结果?.初值表 ?? [];

        public IReadOnlyList<文件映射C> 调试文件表() => _最近结果?.调试文件表 ?? [];

        public (List<string> 消息表, int 错误槽地址) 打包材料()
        {
            var 消息表 = new List<string>();
            int 错误槽地址 = 0;
            if (_最近结果 is not { } 编) return (消息表, 错误槽地址);
            foreach (var (槽标签, 表) in 编.运行期报错)
            {
                if (错误槽地址 == 0 && 编.标签表.TryGetValue(槽标签, out int 地址)) 错误槽地址 = 地址;
                foreach (var (_, 说明) in 表.OrderBy(x => x.号)) 消息表.Add(说明);
            }
            return (消息表, 错误槽地址);
        }

        public int 默认堆大小 => 中间层C.默认堆大小;
        public int 默认池大小 => 中间层C.默认池大小;
        public int 默认帧大小 => 中间层C.默认帧大小;
        public int 最小帧大小 => 中间层C.最小帧大小;
        public int 最大帧大小 => 中间层C.最大帧大小;
        public int 默认内存大小 => 虚拟机C.默认内存大小;
        public int 最大内存大小 => 虚拟机C.最大内存大小;

        public int 默认软栈大小(int 内存大小) => 中间层C.默认软栈大小(内存大小);

        public int 栈顶保留(int 内存大小) => 虚拟机C.栈顶保留(内存大小);

        public string 切换中英(bool 使用中文, string 代码) => 中间层C.切换中英(使用中文, 代码);

        public 编译错误C.单页诊断 取诊断(编译异常C? 错, string? 这一页全称, int 这一页行数, bool 是当前页)
            => 编译错误C.取诊断(错, 这一页全称, 这一页行数, 是当前页);

        public List<(string 全称, int 行, string 文本)> 取各条(编译异常C? 错) => 编译错误C.取各条(错);

        public List<诊断项C> 检查(string 源码, bool 编译通过 = true) => 检查器C.检查(源码, 编译通过);

        public List<折叠区C.区段> 算折叠区(string 正文) => 折叠区C.算(正文);

        public string 对齐语义列(string 代码, out string 主换行) => 语义列C.对齐文本(代码, out 主换行);

        public List<补全候选C> 取补全(补全来源C? 来源, 补全请求C 请求, IReadOnlyCollection<string>? 模糊规则 = null)
            => 补全名字C.取(来源, 请求, 模糊规则);

        // ---- 调试 ----

        public 虚拟机C 机器
        {
            get => _机器;
            set => _机器 = value ?? throw new ArgumentNullException(nameof(value), "前后端契约:机器不许置 null");
        }

        public 虚拟机C 会话机器
        {
            get => _会话.虚拟机;
            set => _会话.虚拟机 = value ?? throw new ArgumentNullException(nameof(value), "前后端契约:会话机器不许置 null");
        }

        public 虚拟机C 取机器(bool 会话机) => 会话机 ? _会话.虚拟机 : _机器;

        public void 请求中断(bool 会话机 = false) => 取机器(会话机).请求中断();

        public bool 要重新装载(虚拟机C 机, byte[] 机器码) => 机.需要重新装载(机器码);

        public bool 机器要重新装载(byte[] 机器码) => _机器.需要重新装载(机器码);

        public int 寄存器个数(虚拟机C 机) => 机.寄存器.Length;

        public uint 寄存器值(虚拟机C 机, int 号)
            => 号 >= 0 && 号 < 机.寄存器.Length ? 机.寄存器[号] : 0;

        public int 内存长度(虚拟机C 机) => 机.内存.Length;

        public byte 内存字节(虚拟机C 机, int 地址)
            => 地址 >= 0 && 地址 < 机.内存.Length ? 机.内存[地址] : (byte)0;

        public byte[] 内存数组(虚拟机C 机) => 机.内存;

        public bool 是中断停下(虚拟机C.停下原因C 原因) => 原因 == 虚拟机C.停下原因C.被中断;

        /// <summary>
        /// H100 第二步:三个用量数(口径**一处** = `引擎\内存用量C.cs`)。
        /// 帧指针取 R15 —— 软栈从软栈底往上长,程序开头那条 `取地址 R15 软栈槽` 定的就是它
        /// (见 `中间层C.主流程.cs`)。
        /// </summary>
        public (int 池用, int 池顶, int 堆用, int 堆共, int 软栈用, int 软栈共) 内存用量(虚拟机C 机)
        {
            var 用量 = 内存用量C.算(机.内存, 机.内存分区, _会话.内部槽地址, 机.寄存器.Length > 15 ? 机.寄存器[15] : 0);
            return (用量.池用, 用量.池顶, 用量.堆用, 用量.堆共, 用量.软栈用, 用量.软栈共);
        }

        /// <summary> 按工程文件里的 `内存` 装载:只在**真的不一样**时才设 ——
        /// 设内存大小会把栈指针重置到栈顶,程序跑一半时重置就把栈毁了(`跑一遍` 那条老口径同源) </summary>
        public void 按内存装载(虚拟机C 机, byte[] 机器码, int 内存大小)
        {
            if (机.内存大小 != 内存大小) 机.设内存大小(内存大小);
            装载(机, 机器码);
        }

        public void 装载(虚拟机C 机, byte[] 机器码)
        {
            // "当前机器码出自哪一份编译结果" ⇒ 连顶层常量初值一起装(初值表是编译产物,丢了就落不进数据段)。
            // 判据是**引用相等**:那个引用还兼着 `需要重新装载` 的"是不是同一份"那一档(见 `当前结果` 的注释)。
            if (_当前结果 is { } 编 && ReferenceEquals(编.机器码, 机器码))
            {
                备好重载要的三张表(机, 编);          // H13:标签表 / 内存分区 / 名字表(重载判"旧那一份"靠它们)
                机.装载(编);
            }
            else 机.装载(机器码);
        }

        public void 装载会话(byte[] 机器码)
        {
            if (_当前结果 is { } 编 && ReferenceEquals(编.机器码, 机器码))
            {
                备好重载要的三张表(_会话.虚拟机, 编);
                _会话.装载(编);
            }
            else _会话.装载(机器码);
        }

        /// <summary>
        /// H13:装载之前把**这一份编译结果的三张表**挂到那台机器上 —— 热重载要靠它们认"旧那一份"
        /// (标签表给旧地址、内存分区给字符串池的位置与大小、名字表给全局槽的宽度)。
        /// 只在"有编译结果、而且就是这一份"那一档挂(与初值表同一个判据):没有结果时重载一律判"保不住"。
        /// </summary>
        private static void 备好重载要的三张表(虚拟机C 机, 汇编调试结果C 编)
        {
            机.标签表 = 编.标签表;
            机.内存分区 = 编.内存分区;
            机.名字表 = 编.名字表;
            机.数据字节数 = 编.数据字节数;
            机.代码字节数 = 编.代码字节数;
        }

        /// <summary>
        /// H13:**热重载** —— 界面按这一句把"刚编出来的这一份"换进**正跑着的那台**(执行机 / 会话机)。
        ///
        /// 判据全在 `虚拟机C.重载` 里(哪一台在跑就在哪一台身上判);这里只负责一件事:
        /// 把**新这一份**的那几张表交给它(不给的话它无从知道新布局),再调它。
        /// 返回 false = 没有可用的编译结果(调用方该说一句"先编译");true = 换过了,说明在 `机.上次重载说明`。
        /// </summary>
        public bool 热重载(虚拟机C 机, byte[] 机器码)
        {
            ArgumentNullException.ThrowIfNull(机);
            ArgumentNullException.ThrowIfNull(机器码);
            if (_当前结果 is not { } 编 || !ReferenceEquals(编.机器码, 机器码)) return false;
            机.初值表 = 编.初值表;              // 新那一份的常量初值:重载按它把常量拽回出厂值(与装载同一套口径)
            机.重载(机器码, 新名字表: 编.名字表, 新代码字节数: 编.代码字节数,
                新标签表: 编.标签表, 新分区表: 编.内存分区);
            return true;
        }

        public bool 会话有机器码 => _会话.有机器码;
        public byte[] 会话机器码 => _会话.机器码;
        public bool 会话机器码就是这一份(byte[] 机器码)
            => ReferenceEquals(_会话.机器码, 机器码);
        public bool 会话已停机 => _会话.已停机;
        public long 会话已执行步数 => _会话.已执行步数;
        public string? 会话当前文件 { get => _会话.当前文件; set => _会话.当前文件 = value; }
        public bool 会话汇编模式 { get => _会话.汇编模式; set => _会话.汇编模式 = value; }
        public int 会话内存大小 { get => _会话.内存大小; set => _会话.内存大小 = value; }
        public IReadOnlyList<文件映射C> 会话调试文件表 { get => _会话.调试文件表; set => _会话.调试文件表 = [.. value]; }
        public Dictionary<string, int> 会话标签表 { get => _会话.标签表; set => _会话.标签表 = value; }
        public Dictionary<string, int> 会话内部槽地址 { get => _会话.内部槽地址; set => _会话.内部槽地址 = value; }
        public HashSet<int> 会话断点地址 { get => _会话.断点地址; set => _会话.断点地址 = value; }
        public Func<int, bool>? 会话断点命中判断 { get => _会话.断点命中判断; set => _会话.断点命中判断 = value; }
        public 调用栈标记C 会话调用栈 => _会话.调用栈;
        public void 会话清空() => _会话.清空();
        public void 记会话装载() => _会话装载机 = _会话.虚拟机;
        public bool 会话装载机换过() => !ReferenceEquals(_会话装载机, _会话.虚拟机);

        public 调试步进结果C 逐步一次() => _会话.逐步一次();
        public 调试步进结果C 运行到断点(long 上限 = 5_000_000) => _会话.运行到断点(上限);
        public 调试步进结果C 继续(long 上限 = 5_000_000) => _会话.继续(上限);
        public 调试步进结果C 会话快照(bool 这一步跳出去 = false) => _会话.取快照(这一步跳出去);
        public (string 文件, int 行)? 会话取位置(int 地址) => _会话.取位置(地址);

        public 会话状态C 读状态(byte[]? 机器码 = null) => 读状态(_会话, 机器码);

        public 会话状态C 读状态(调试会话C 会话, byte[]? 机器码 = null)
        {
            bool 装过 = 会话.有机器码;
            bool 同一份 = 装过 && 机器码 != null && 会话.机器码.Length == 机器码.Length
                && 会话.机器码.AsSpan().SequenceEqual(机器码);
            return new 会话状态C(装过, 同一份, 会话.已停机, 会话.虚拟机.正在等输入, 会话.已执行步数);
        }

        // ---- 断点表 ----

        public void 加载断点(string? 工程目录) => 换断点表(断点表C.加载(工程目录));

        /// <summary> 断点表那一格**换一份新的**(加载 / 读盘都是一只新表;自检要能从外面换,所以留着)</summary>
        private void 换断点表(断点表C 新表)
        {
            _断点表.断点 = 新表.断点;
        }

        public int[] 断点行号(string 全称) => _断点表.取行号(全称);
        public int[] 断点附加行号(string 全称) => _断点表.取附加行号(全称);
        public HashSet<int> 断点地址(IReadOnlyList<文件映射C> 调试文件表) => _断点表.算地址(调试文件表);
        public Dictionary<int, 断点项C> 断点地址带条件(IReadOnlyList<文件映射C> 调试文件表)
            => _断点表.算地址带条件(调试文件表);
        public bool 有断点(string 全称, int 行) => _断点表.有(全称, 行);
        public string 取断点条件(string 全称, int 行) => _断点表.取条件(全称, 行);
        public 断点项C? 取断点(string 全称, int 行) => _断点表.取(全称, 行);
        public bool 设断点(string 全称, int 行, string 条件, string 命中次数, string 日志)
            => _断点表.设置(全称, 行, 条件, 命中次数, 日志);
        public void 设断点条件(string 全称, int 行, string 条件) => _断点表.设置条件(全称, 行, 条件);
        public void 保存断点(string? 工程目录) => _断点表.保存(工程目录);
        public bool 切换断点(string 全称, int 行) => _断点表.切换(全称, 行);
        public int 断点个数 => _断点表.个数;
        public void 清除断点() => _断点表.清除();
        public int 清失效断点(IReadOnlyList<文件映射C> 调试文件表) => _断点表.清失效(调试文件表);
        public string 断点摘要() => _断点表.摘要明细();
        public string 断点条件摘要() => _断点表.摘要条件();
        public bool 断点表有文件(IReadOnlyList<文件映射C> 调试文件表, string 全称)
            => 断点表C.有文件(调试文件表, 全称);
        public int? 断点算某行地址(IReadOnlyList<文件映射C> 调试文件表, string 全称, int 行)
            => 断点表C.算某行地址(调试文件表, 全称, 行);

        // ---- 宿主与文件 ----

        public bool 是同一个文件(string? 左, string? 右) => 文件判定C.同文件(左, 右);
        public string 读源文件(string 路径) => 文本落盘C.读源文件(路径);
        public string 解源文件(byte[] 字节, string 路径 = "") => 文本落盘C.解源文件(字节, 路径);
        public void 写回(string 路径, string 文本) => 文本落盘C.写回(路径, 文本);
        public bool 文件存在(string 路径) => 虚拟文件C.存在(路径);
        public string 读文本文件(string 路径) => 虚拟文件C.读文本(路径);
        public void 写文本文件(string 路径, string 文本) => 虚拟文件C.写文本(路径, 文本);
        public void 建目录(string 目录) => 虚拟文件C.建目录(目录);
        public void 挂上文件实现(文件接口C? 实现) => 虚拟文件C.挂上(实现);
        public void 摘掉文件实现() => 虚拟文件C.摘掉();
        public 文件接口C? 当前文件实现 => 虚拟文件C.挂着;
        public void 设基准目录(string? 目录) => 宿主能力C.设基准目录(目录);
        public void 设可执行白名单(IReadOnlyDictionary<string, string>? 表) => 宿主能力C.设可执行白名单(表);
        public void 设网址白名单(网址白名单C? 表) => 宿主能力C.设网址白名单(表);
        public int 处理(byte[] 内存, uint 参数区, int 功能号) => 宿主能力C.处理(内存, 参数区, 功能号);
        public 宿主能力种C 问能力(string 可执行路径, int 该到几号 = 0, int 该到包格式 = 0)
            => 宿主能力C.问能力(可执行路径, 该到几号, 该到包格式);
        public string? 陈旧原因(string 可执行路径, int 该到几号 = 0, int 该到包格式 = 0)
            => 宿主能力C.陈旧原因(可执行路径, 该到几号, 该到包格式);
        public string 能力上限文本(宿主能力种C 能力) => 宿主能力C.能力上限文本(能力);
        public string 包格式上限文本(宿主能力种C 能力) => 宿主能力C.包格式上限文本(能力);
        public int 这段机器码要的功能号上限(byte[] 机器码) => 宿主能力C.这段机器码要的功能号上限(机器码);
        public byte[] 打包(byte[] 宿主字节, byte[] 机器码, IReadOnlyList<string> 消息表, int 错误槽地址,
            IReadOnlyList<初值项C>? 初值表 = null, 源行表C? 源行表 = null)
            => 程序包C.打包(宿主字节, 机器码, 消息表, 错误槽地址, 初值表, 源行表);
        public int 段初值包格式 => 程序包C.包格式_段初值;
        public List<文件映射C> 建全工程表(
            IReadOnlyList<(string 名称, string 内容)> 文件表, bool 优化 = true, int 堆大小 = 中间层C.默认堆大小,
            IReadOnlyList<string>? 工程归属 = null, int 池大小 = 中间层C.默认池大小,
            int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
            => 执行行映射C.建全工程表(文件表, 优化, 堆大小, 工程归属, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小);
        public 源行表C 压源行表(IReadOnlyList<文件映射C>? 段表) => 执行行映射C.压源行表(段表);

        // ---- 图形 ----

        public 图形呈现口C? 呈现口 => 图形宿主C.呈现口;
        public void 挂上(图形呈现口C? 口) => 图形宿主C.挂上(口);
        public void 摘掉() => 图形宿主C.摘掉();
        public void 清空() => 图形宿主C.清空();
        public Action<int, int, int, int>? 事件出口
        {
            get => 图形宿主C.事件出口;
            set => 图形宿主C.事件出口 = value;
        }
        public void 投事件(int 类型, int 键码, int x, int y) => 图形宿主C.投事件(类型, 键码, x, y);
        public bool 这段机器码用图形(byte[] 机器码) => 图形宿主C.这段机器码用图形(机器码);
    }
}
