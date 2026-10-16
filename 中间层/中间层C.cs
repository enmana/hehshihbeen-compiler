using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 汇编器中间层：把流程控制(如果/选择/循环/遍历, if/switch/for/foreach)展开成
    /// 现有汇编器能直接识别的纯汇编文本。与原生汇编可混写。
    /// 生成的是英文助记符；源码层关键字支持中英双语。
    /// </summary>
    public static class 中间层C
    {
        #region 关键字表及查询委托(转发到 中间层指令C)
        /// <summary> 关键字表委托给专用类 中间层指令C </summary>
        public static (string 中文, string 英文)[] 关键字表 => 中间层指令C.关键字表;
        public static string? 到英文关键字(string? 词) => 中间层指令C.到英文关键字(词);
        public static string[] 全部关键字 => 中间层指令C.全部关键字;
        #endregion

        #region 堆区大小的默认值与上下限(工程文件里的 `堆` 改的就是它)
        /// <summary>
        /// 堆区大小(字节):**默认 64KB**。堆是数据段里独立的一块(与 32KB 池分开),`申请` 从这儿要地方。
        /// 工程文件里写一行 `"堆": "128KB"` 就能改(认 `64KB`/`1MB`/纯字节数,和 `内存` 一个写法);
        /// 不写就是这里的默认值。
        /// </summary>
        public const int 默认堆大小 = 64 * 1024;

        /// <summary>
        /// 字符串池的默认大小(字节):**池一开始要多大**。工程文件里写一行 `"池": "128KB"` 就能改
        /// (认 `32KB`/`1MB`/纯字节数,和 `内存`/`堆` 一个写法);不写就是这里的默认值。
        /// 注意:池**不是死的** —— 一开始这么大,不够会自己按倍增往上长(见 `中间层C.列表.cs` 的 `确保池`),
        /// 一直长到"运行内存末尾减去给硬件栈留的余量"(`栈顶保留`)为止;真长到头了才报错停机。
        /// </summary>
        public const int 默认池大小 = 32 * 1024;

        /// <summary> 池下限:再小连一个 4 字节的字符串头都放不下,没有意义 </summary>
        public const int 最小池大小 = 1024;

        /// <summary> 池上限:池是从**运行内存**里切出来的一块,所以不可能比内存上限(1MB)还大 </summary>
        public const int 最大池大小 = 虚拟机C.最大内存大小;

        /// <summary>
        /// 软栈(R1-1)默认大小的**上限**:运行内存够大时给 64 KB —— 默认一帧 96 字节,约 546 层调用。
        /// 实际默认值 = `min(这个上限, 运行内存 ÷ 4)`(见 `默认软栈大小(int)`)。
        /// 工程文件里写一行 `"软栈": "128KB"` 就能改(认 `64KB`/`128KB`/纯字节数,与 `内存`/`堆`/`池` 同一套写法)。
        /// </summary>
        public const int 默认软栈上限 = 64 * 1024;

        /// <summary> 软栈下限:默认一帧就是 96 字节,再小连几层调用都放不下,没有意义(与池一样给 1 KB) </summary>
        public const int 最小软栈大小 = 1024;

        /// <summary>
        /// 软栈上限(R1-1 拍板 = 256 KB,默认一帧 96 字节,约 2184 层):用 HB 写递归下降解析器够用了;
        /// 再大也没人用得上,不如把运行内存留给堆与池。
        /// </summary>
        public const int 最大软栈大小 = 256 * 1024;

        /// <summary>
        /// 这次编译**没配** `软栈` 时的默认值(R1-1 拍板):`min(64KB, 运行内存 ÷ 4)`。
        /// 为什么跟运行内存联动:软栈是从运行内存里切出来的一块 —— 写死 64 KB 会让 `"内存": "32KB"` 这种
        /// 小内存工程当场报"软栈比内存还大";按四分之一缩,老口径(4 KB)只在内存在 16 KB 以下才会遇到。
        /// </summary>
        public static int 默认软栈大小(int 内存大小) => Math.Max(最小软栈大小, Math.Min(默认软栈上限, 内存大小 / 4));

        /// <summary>
        /// **函数帧的默认大小(字节,H34)**:参数 + 局部变量 + 表达式临时槽**共用**的一块。
        /// 工程文件里写一行 `"帧": "256"` 就能改(认 `256`/`1KB`/纯字节数,与 `内存`/`堆`/`池`/`软栈` 同一套写法);
        /// 不写就是这里的默认值。第97批起两档优化本来就都是 96;**H34 把写死的 96 改成"默认值"** ——
        /// 与 R1-1 的软栈同款口径:优化开关只影响"槽怎么放",不再影响"帧多大"。
        /// 为什么要可调:参数多 / 局部多的函数在 96 字节里装不下,以前只能靠拆函数绕;
        /// 现在工程作者可以按自己的需要把帧放大(代价是同样的软栈能装的层数变少)。
        /// </summary>
        public const int 默认帧大小 = 96;

        /// <summary>
        /// 帧下限 = 默认值 96(H34 拍板):96 是"标准库那几个函数刚好装得下"的那个数,
        /// 再小连 `库\**` 都编不过 —— 所以可配范围只能往上,不能往下。
        /// </summary>
        public const int 最小帧大小 = 默认帧大小;

        /// <summary>
        /// 帧上限(H34 拍板 = 4096 字节):一帧 4KB,默认软栈 64KB 用掉八成也够约 16 层。
        /// 帧就住在软栈里,所以它还必须 ≤ 软栈(见 `工程文件C.检查` 与 `中间层展开器` 的检查)。
        /// </summary>
        public const int 最大帧大小 = 4096;

        /// <summary> 堆区下限:再小连一个块头(12 字节)+ 对齐都放不下,没有意义 </summary>
        public const int 最小堆大小 = 1024;

        /// <summary>
        /// 堆区上限:堆是从**运行内存**里切出来的一块,所以不可能比内存上限(1MB)还大;
        /// 至于"这块程序配这么大的堆到底装不装得下",由编译时的"代码 + 静态数据 装不进内存"判定来管。
        /// </summary>
        public const int 最大堆大小 = 虚拟机C.最大内存大小;
        #endregion

        #region 展开入口与语言切换委托
        /// <summary>
        /// 展开结果:纯汇编文本 + 变量数据槽表(显式契约,汇编器不再从文本嗅探 _vN)
        /// + 调试名字表(旁路信息,只在单步调试时用来看"这个变量叫什么、什么类型、在哪",不参与汇编)
        /// + 编译提示(编译页给人看的说明,例如"列表 分数 编译器算出来最多 3 个")
        /// + 运行期报错表(错误号 → 说明;程序停机后 IDE/命令行读"错误槽"来报)
        /// </summary>
        public sealed record 中间层展开结果C(string 汇编文本, IReadOnlyList<变量槽定义> 变量槽, 名字表C? 名字表 = null,
            IReadOnlyList<string>? 编译提示 = null, 变量槽定义? 错误槽 = null, IReadOnlyList<(int 号, string 说明)>? 错误表 = null,
            IReadOnlyList<(string 槽名, int 偏移, int 宽度, uint 值)>? 初值表 = null,
            IReadOnlyList<内存分区定义C>? 内存分区 = null,
            IReadOnlyList<(string 角色, string 槽名)>? 内部槽 = null);

        /// <summary>
        /// 展开入口：把高层源码展开成纯汇编文本(丢弃变量槽信息,仅供 展示/对比/展开折叠 使用)。
        /// </summary>
        public static string 展开(string 源代码) => 展开完整(源代码).汇编文本;

        /// <summary> 展开入口:返回 汇编文本 + 变量槽表(按声明顺序)。编译器→汇编器的正式通道。 </summary>
        public static 中间层展开结果C 展开完整(string 源代码)
        {
            var (标签号, 选择号) = 展开编号起点(源代码);
            return 展开完整(源代码, null, null, 0, 标签号, 选择号);
        }

        /// <summary>
        /// 展开完整(带外部变量/外部结构上下文):供编辑器对"块片段"展开使用——
        /// 外部变量作为根作用域预置(带结构信息以便字段访问),槽号从 起始槽号 之后接续。
        /// </summary>
        public static 中间层展开结果C 展开完整(string 源代码, IReadOnlyDictionary<string, 变量绑定C>? 外部变量, int 起始槽号)
            => 展开完整(源代码, 外部变量, null, 起始槽号, 0, 0);

        /// <summary>
        /// 展开完整(片段+编号续接):已展开文本里的内部标签(_f…)在整文件里必须唯一——
        /// 新展开从 起始标签号/起始选择号 之后编号,避免"部分展开后再汇编"与残留构造重名(标签重复定义)。
        ///
        /// **T9 起这一条不带"补停机"**(`补停机: false`):它就是**片段 / 部分展开 / "展开给我看"**那条路
        /// (编辑器把块展开成汇编塞回源码,自检也拿它拼"展开块 + 残留块")—— 里面多一条 `停止`
        /// 会把拼在后面的残留代码挡在门外。正式编译走下面的长重载(默认 `补停机: true`)。
        /// </summary>
        public static 中间层展开结果C 展开完整(string 源代码, IReadOnlyDictionary<string, 变量绑定C>? 外部变量, IReadOnlyDictionary<string, 结构类型C>? 外部结构, int 起始槽号, int 起始标签号, int 起始选择号)
            => new 中间层展开器(源代码, 外部变量, 外部结构, 起始槽号, 起始标签号, 起始选择号, 补停机: false).运行();

        /// <summary>
        /// 展开完整(片段+编号续接)<b>正式编译那一半</b>:带基目录 / 源行标记 / 优化 / 各区大小 / 帧 / 补停机。
        /// 与上面那条同名:参数多的这一条是"正式编译"入口(补停机默认开),参数少的那条是片段/展示入口。
        /// </summary>
        /// <param name="带源行标记">
        /// 展开时在每条源码行生成的第一条指令前插一行 `//@源行号`(汇编器把 `//` 当注释,不影响编译)。
        /// 单步调试要靠它算"机器码地址 → 源码第几行";平时编译不插,免得展开文本变啰嗦。
        /// </param>
        /// <param name="优化">
        /// 编译期优化(消冗余 MOV + 循环不变量提升)。**默认关**:编辑器"展开给我看"那条路必须看得见原样,
        /// 而且展开块要放回源码里,常驻寄存器会和外面的代码打架 —— 只有正式编译(汇编器那条路)才默认开。
        /// </param>
        /// <param name="堆大小">
        /// 这次编译给堆区留多少字节(工程文件里的 `堆`;不传就是 64KB)。
        /// **它必须参与编译**:堆的初始化(整块大小、块末地址)是编译期生成的机器码,换大小机器码就变
        /// —— 所以它也要进"展开结果缓存"的键(见 汇编器C.内容指纹),不然会吃到上一个大小的旧展开。
        /// </param>
        /// <param name="软栈大小">
        /// **R1-1**:这次编译给软栈留多少字节(工程文件里的 `软栈`)。**-1 = 自动**:按运行内存算
        /// `min(64KB, 内存 ÷ 4)`(见 `默认软栈大小(int)`);默认一帧 96 字节,所以 64KB ≈ 546 层调用。
        /// 它与堆一样必须参与编译(软栈槽的宽度、递归深度上限的立即数都编进机器码)⇒ 也要进展开缓存的键。
        /// </param>
        /// <param name="帧大小">
        /// **H34**:这次编译每个函数帧多大(工程文件里的 `帧`)。**-1 = 默认** = `中间层C.默认帧大小`(96 字节)。
        /// 可配范围 `最小帧大小` ~ `最大帧大小`(96 ~ 4096),而且必须是 **4 的倍数**、必须 **≤ 软栈**。
        /// 帧大小编进机器码(`取地址帧` / `减立即数` 的偏移都是它)⇒ 与堆、软栈一样要进展开缓存的键。
        /// </param>
        public static 中间层展开结果C 展开完整(string 源代码, IReadOnlyDictionary<string, 变量绑定C>? 外部变量, IReadOnlyDictionary<string, 结构类型C>? 外部结构, int 起始槽号, int 起始标签号, int 起始选择号, string? 基目录 = null, bool 带源行标记 = false, bool 优化 = false, int 堆大小 = 默认堆大小, int 池大小 = 默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1, bool 补停机 = true)
            => new 中间层展开器(源代码, 外部变量, 外部结构, 起始槽号, 起始标签号, 起始选择号, 基目录, 带源行标记, 优化, 堆大小, null, null, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小, 补停机).运行();

        /// <summary> 展开(带"包含"搜索目录):编译器按文件所在目录解析 `包含 "库/xx.hb"` </summary>
        public static 中间层展开结果C 展开完整带目录(string 源代码, string? 基目录, bool 带源行标记 = false, bool 优化 = false, int 堆大小 = 默认堆大小, int 池大小 = 默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
        {
            var (标签号, 选择号) = 展开编号起点(源代码);
            return 展开完整(源代码, null, null, 0, 标签号, 选择号, 基目录, 带源行标记, 优化, 堆大小, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小);
        }

        /// <summary>
        /// **工程级展开**(第94批):把 `.hbp` 文件表里的所有源文件交给**一个**展开器按顺序展开。
        ///
        /// 与"每个文件各 new 一个展开器"的区别(这就是"工程级符号表"):
        ///   · `_函数表` / 结构表 / 根作用域 / 容器表 **整份工程共享** → 跨文件直接用函数、结构、全局变量;
        ///   · 槽号与内部标签号连续编号(一个编译单元里天然唯一,不再需要"按文件序号唯一化");
        ///   · 序言(FP / 软栈 / 池 / 堆 / 列表例程)只发一份 —— 每个文件各发一份会白占数据段,还会把池切成好几块;
        ///   · 跨文件"先调用后定义"成立(预扫覆盖全部文件的函数头);
        ///   · 跨文件"先用后声明"也成立(第94批 Q1b:结构与顶层全局变量另外预扫一遍,见 `中间层C.声明预扫.cs`)。
        ///     注意:**初始化代码不提前** —— 预扫只登记名字 + 开数据槽,`整数 a = 1;` 那句码仍发在原语句上。
        ///
        /// 每个文件仍然:用自己的目录解析 `包含`、行号从各自第 1 行数起(标记里带 `文件K`)、`包含` 各自只展一次。
        /// </summary>
        /// <param name="文件表">(文件名, 源码)按编译顺序(启动文件排第一)</param>
        /// <param name="工程归属">
        /// 每个文件属于哪个 `.hbp` 工程(与 `文件表` 一一对应;单工程编译可以不传)。
        /// 它是 `程序集` 可见性的判据:引用方与声明方不是同一个工程就报错(见 `校验可取用`)。
        /// </param>
        public static 中间层展开结果C 展开工程带目录(IReadOnlyList<(string 名称, string 内容)> 文件表,
            bool 带源行标记 = false, bool 优化 = false, int 堆大小 = 默认堆大小, IReadOnlyList<string>? 工程归属 = null,
            int 池大小 = 默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
        {
            var 合并 = new StringBuilder();
            foreach (var (_, 内容) in 文件表) 合并.Append(内容 ?? "").Append('\n');
            var (标签号, 选择号) = 展开编号起点(合并.ToString());
            return new 中间层展开器("", null, null, 0, 标签号, 选择号, null, 带源行标记, 优化, 堆大小, 文件表, 工程归属, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小).运行();
        }

        private static readonly Regex 内部记号正则 = new(@"(?<![A-Za-z0-9_])(_f[A-Za-z0-9_]+|_v\d+)", RegexOptions.Compiled);
        private static readonly Regex 选择标签正则 = new(@"^_fCase(\d+)_(\d+)$", RegexOptions.Compiled);

        /// <summary>
        /// 扫描文本里已存在的内部标签,给出"下一次展开的起始编号":
        /// 通用序号(各类 _f…N 的尾号)与 switch 分支选择号(_fCase{选择号}_{项})都取已见最大 +1,
        /// 保证部分展开过的文件再次 汇编/展开 不会撞标签。
        /// </summary>
        public static (int 标签号, int 选择号) 展开编号起点(string 源代码)
        {
            int 标签 = 0, 选择 = 0;
            foreach (Match m in 内部记号正则.Matches(源代码))
            {
                string t = m.Value;
                var 选择匹配 = 选择标签正则.Match(t);
                if (选择匹配.Success)
                    选择 = Math.Max(选择, int.Parse(选择匹配.Groups[1].Value));
                var 尾 = Regex.Match(t, @"(\d+)$");
                if (尾.Success)
                    标签 = Math.Max(标签, int.Parse(尾.Groups[1].Value));
            }
            return (标签, 选择);
        }

        /// <summary> 关键字语言切换委托给 中间层指令C </summary>
        public static string 切换关键字语言(bool 使用中文, string 代码) =>
            中间层指令C.切换关键字语言(使用中文, 代码);

        /// <summary>
        /// **整体中英切换**(关键字 + 指令名/内建函数名/附加类型名),编辑器按钮、CLI `--中英/--en/--中文`、
        /// 综合自检都调这一个入口,实现在 中间层指令C.切换中英。
        /// </summary>
        public static string 切换中英(bool 使用中文, string 代码) =>
            中间层指令C.切换中英(使用中文, 代码);
        #endregion

        #region 片段展开(编辑器"就地展开成汇编"用)

        /// <summary>
        /// 一行展开后的汇编里贴的源行标记(见 `中间层展开器.发出`):
        /// `// 来自第 12 行`(单行语句)、`// 来自第 7 到 12 行`(整块语句);
        /// 工程级展开会多一个文件号 —— `// 来自文件2第 12 行`;
        /// 尾巴上的 `#语句21` 是这条语句在伪行表里的下标(**唯一**,跨文件也不会撞),
        /// 尾巴上的 `库` 表示这条语句来自 `包含` 进来的文件(它的行号从 1 数起)。
        /// **解析只有一份**:`引擎\源行标记C.cs`(唯一事实源;这里只是转一手)。
        /// </summary>
        public static bool 解析源行标记(string 行, out int 起行, out int 止行, out int 语句号, out bool 来自包含)
        {
            var 标记 = 源行标记C.读(行);
            起行 = 标记.起始行;
            止行 = 标记.止行;
            语句号 = 标记.语句号;
            来自包含 = 标记.来自库;
            return 标记.有;
        }

        /// <summary> 带文件号的版本(工程级展开:文件号 1 基;单文件展开 = 0) </summary>
        public static bool 解析源行标记(string 行, out int 文件号, out int 起行, out int 止行, out int 语句号, out bool 来自包含)
        {
            var 标记 = 源行标记C.读(行);
            文件号 = 标记.文件号;
            起行 = 标记.起始行;
            止行 = 标记.止行;
            语句号 = 标记.语句号;
            来自包含 = 标记.来自库;
            return 标记.有;
        }

        /// <summary> 纯标签行:`名字:`(编译器的例程/内部跳转标签就长这样;缩进的指令行不会误判) </summary>
        private static readonly Regex 标签行正则 = new(@"^[ \t]*([A-Za-z_\u0080-\uFFFF][A-Za-z0-9_.\u0080-\uFFFF]*):[ \t]*$", RegexOptions.Compiled);

        /// <summary>
        /// **把一段构造展开成汇编**(编辑器"就地展开成汇编"的引擎侧入口)。
        ///
        /// 做法一句话:**整篇展开一遍,按每行后面的源行标记把这一段(这几行源码)生成的指令挑出来,
        /// 再补上这些指令引用到的编译器标签块**。
        /// <list type="number">
        /// <item>整篇展开(**优化关**、带源行标记)—— 外层变量(字符串/列表/结构/数组)、函数参数、
        ///       `包含` 进来的库函数全都由"整篇展开"天然解决,界面端不必再靠"文本扫前缀"猜作用域;</item>
        /// <item>按标记挑"这一段"的指令:标记里带**语句号**(伪行表下标),正是为了不受两件事干扰 ——
        ///       ① 循环体里编译器自己开的临时槽会一直挂着循环头那一行的行号;
        ///       ② 被 `包含` 进来的文件行号从 1 数起,会跟主文件撞号(那样的语句带 `库` 字,直接不认);</item>
        /// <item>补上这些指令**引用到的 `_f…` 标签块**(标签行自己没有标记,例如 `JEQ _fForEnd3` 要的 `_fForEnd3:`)
        ///       以及那些块引用的(传递闭包)。**只搬 `_f…` 开头的编译器标签块**:
        ///       用户函数(如 `转字符串`)本来就定义在整篇里,搬进来会"标签重复定义";</item>
        /// <item>按展开文本里的原顺序拼回去(指令后面的源行标记原样保留,方便对着看"这条是第几行来的")。</item>
        /// </list>
        /// 编号:整篇展开的标签号/槽号就是**真编号**,所以这段文本拼回整篇之后与"整篇重新编译"完全一致
        /// (不会与别处残留的 `_f…` 撞名,也不会与外面的变量槽错位)。
        /// </summary>
        /// <param name="整篇源码">整篇源码(编辑器里全文;片段展开必须从这里出发 —— 作用域/编号都靠它)</param>
        /// <param name="起行">构造第一行(1 基,含)</param>
        /// <param name="止行">构造最后一行(1 基,含)</param>
        /// <param name="基目录">哪个目录下的源码(`包含 "库/xx.hb"` 按它找);界面端传当前文件所在目录</param>
        /// <param name="堆大小">堆区大小(与那次编译一致;不传就是默认 64KB)</param>
        /// <returns>这段构造展开出来的汇编文本;这段范围里没有语句时返回空串</returns>
        public static string 展开片段(string 整篇源码, int 起行, int 止行, string? 基目录 = null, int 堆大小 = 默认堆大小,
            int 池大小 = 默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
        {
            if (整篇源码 == null) throw new ArgumentNullException(nameof(整篇源码));
            if (起行 < 1) 起行 = 1;
            if (止行 < 起行) return "";

            // 整篇展开(**优化关**:这段文本要放回源码里,循环不变量提升会常驻寄存器,跟外面的代码打架)
            string 原文本 = 展开完整带目录(整篇源码, 基目录, 带源行标记: true, 优化: false, 堆大小: 堆大小, 池大小: 池大小, 内存大小: 内存大小, 软栈大小: 软栈大小, 边界检查: 边界检查, 帧大小: 帧大小).汇编文本;
            var 原 = 指令行表(原文本);
            var 原文行表 = 原文本.Split('\n');

            // ② 段内判定:标记的**范围**跟 [起行, 止行] 有重叠就算这一段的(带 `库` 的不算主文件)。
            //    为什么按范围而不是按"当前处理到第几行":循环头那一行的标记会一直挂到循环体里
            //    编译器自己开的临时槽上,按"当前行"挑会把这些尾巴算进来;带范围之后 `}` 那一行也在范围内。
            bool 在段内(int i)
                => 解析源行标记(原[i].文本, out int 起, out int 止, out _, out bool 来自包含)
                   && !来自包含 && 起 <= 止行 && 止 >= 起行;

            // ③ 取"这一段自己的指令":标记范围跟 [起行, 止行] 有重叠的那些(带 `库` 的不算主文件)。
            //    中间夹着的"没有标记"的衔接指令(池检查、跳转、往暂存槽里写常量)一起带上 ——
            //    它们跟这一段在同一次展开里,少带了反而会缺指令。
            // 落段指令可能被"字面量初始化/池检查"这类没有自己标记的指令隔开(实测隔几十条),
            // 所以用"头尾 + 桥接"的办法:首 = 第一条落段指令;尾 = 最后一条落段指令;
            // 两者之间的全都要(同一份展开里,这一段自己的指令全在这条线里),真正不属于这一段的引用靠下面的标签块闭包补齐。
            int 首 = -1, 段后 = -1;
            for (int i = 0; i < 原.Count; i++)
                if (在段内(i)) { if (首 < 0) 首 = i; 段后 = i + 1; }
            if (首 < 0) return "";
            // 尾巴再往外扫一点:最后一条落段指令之后若还有"没有标记"的收尾指令,一起带上
            while (段后 < 原.Count && !解析源行标记(原[段后].文本, out _, out _, out _, out _)) 段后++;
            // ③ 标签块索引:整篇展开里每个 `_f…` 标签块的范围(标签行自己没有标记,要按块一起搬)
            var 块起 = new Dictionary<string, int>(StringComparer.Ordinal);
            var 块止 = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int k = 0; k < 原.Count; k++)
            {
                if (标签行正则.Match(原[k].文本) is not { Success: true } m) continue;
                string 名 = m.Groups[1].Value;
                if (块起.ContainsKey(名)) continue;          // 重复标签交给汇编器报错
                int 止 = 原.Count;
                for (int t = k + 1; t < 原.Count; t++)
                    if (标签行正则.IsMatch(原[t].文本)) { 止 = t; break; }
                块起[名] = k;
                块止[名] = 止;
            }

            // ④ 补:这一段引用的 `_f…` 标签块,以及那些块引用的(传递闭包)
            var 要的 = new SortedSet<int>();
            for (int k = 首; k < 段后; k++) 要的.Add(原[k].行下标);
            var 待办 = new Queue<int>();
            foreach (int k in 要的) 待办.Enqueue(k);
            var 走过 = new HashSet<int>(要的);
            while (待办.Count > 0)
            {
                int k = 待办.Dequeue();
                foreach (string 名 in 引用的标签(展开行(原文行表, k)))
                {
                    if (!名.StartsWith("_f", StringComparison.Ordinal)) continue;   // 用户函数已定义在整篇别处,不搬
                    if (!块起.TryGetValue(名, out int 起)) continue;                // 本段里没有的目标就不搬(交给汇编器照旧)
                    for (int t = 起; t < 块止[名]; t++)
                        if (走过.Add(t)) 待办.Enqueue(t);
                }
            }

            // ⑤ 按展开文本里的原顺序拼回(指令后面的源行标记原样留着)
            var 出 = new List<string>(走过.Count);
            foreach (int k in 走过.OrderBy(x => x)) 出.Add(展开行(原文行表, k).TrimEnd());
            return string.Join('\n', 出);
        }

        /// <summary> 展开文本的第 k 行(展开文本一次切开,行下标是 0 基) </summary>
        private static string 展开行(string[] 展开行表, int k) => 展开行表[k];
/// <summary> 展开文本 → 指令行(序号、它在那份文本里的行下标、原文);与 执行行映射C.扫 同一套判据 </summary>
        private static List<(int 行下标, string 文本)> 指令行表(string 展开文本)
        {
            var 出 = new List<(int, string)>();
            var 行表 = 展开文本.Split('\n');
            for (int i = 0; i < 行表.Length; i++)
                if (是指令行(行表[i])) 出.Add((i, 行表[i]));
            return 出;
        }

        /// <summary> 这行算"指令行"吗(与 执行行映射C.扫 同一套判据):去掉注释/空白后非空、且不是纯标签行 </summary>
        private static bool 是指令行(string 行)
        {
            int 注释 = 行.IndexOf("//", StringComparison.Ordinal);
            if (注释 >= 0) 行 = 行[..注释];
            行 = 行.Trim();
            if (行.Length == 0) return false;
            if (行.EndsWith(':')) return false;                       // 纯标签行
            int 冒号 = 行.IndexOf(':');
            if (冒号 >= 0) 行 = 行[(冒号 + 1)..].Trim();               // "标签: 指令"写在同一行
            return 行.Length > 0;
        }

        /// <summary>
        /// 这行引用到的"标签名"(只看第一个操作数 —— 中间层/汇编器里跳转与调用的目标都在第一个操作数上)。
        /// 用户函数名/中英混排的标签名也在这里被认出来:按空白切出来的第一个词就是。
        /// </summary>
        private static IEnumerable<string> 引用的标签(string 行)
        {
            int 注释 = 行.IndexOf("//", StringComparison.Ordinal);
            if (注释 >= 0) 行 = 行[..注释];
            行 = 行.Trim();
            if (行.Length == 0) yield break;
            int 冒号 = 行.IndexOf(':');
            if (冒号 >= 0) 行 = 行[(冒号 + 1)..].Trim();               // 同一行前面挂着标签:看后面的指令
            if (行.Length == 0 || 行.EndsWith(':')) yield break;
            int 空 = 行.IndexOfAny([' ', '\t']);
            if (空 <= 0) yield break;
            string 首操作数 = 行[(空 + 1)..].Trim();
            if (首操作数.Length == 0) yield break;
            int 止 = 首操作数.IndexOfAny([' ', '\t', ',']);
            if (止 < 0) 止 = 首操作数.Length;
            string 名 = 首操作数[..止];
            if (名.Length > 0) yield return 名;
        }
        #endregion
    }

    /// <summary> 一次展开的内部状态与递归下降实现(表达式引擎见 中间层C.表达式.cs) </summary>
    internal partial class 中间层展开器
    {
        #region 内部状态与基础工具
        private readonly List<string> _伪行表 = [];       // 规范后的“代码块”行(含单独成行的 { } )
        private readonly List<string> _伪行原文 = [];     // 与上一行一一对应:字面量替换**之前**的原文
        private readonly List<int> _伪行号表 = [];        // 每个伪行对应的源行号(1基)
        private readonly List<(string 跳出, string? 继续)> _上下文栈 = []; // 嵌套 循环/选择 栈
        private int _块深度;   // T2:当前进了几层语句块(处理块 进出时增减;撞 `硬上限C.语句块嵌套上限` 就报错)
        private bool _块深度超限;   // T2:本句刚撞过嵌套上限(恢复时整块跳过,不再往里收错 —— 见 跳过坏语句)
        private int _恢复层数;   // T20:当前嵌了几层**错误恢复**(收块内错误 进出时增减;见 中间层C.多错误报告.cs 的 `恢复嵌套上限`)
        private readonly Stack<List<string>> _缓冲栈 = new();
        private int _标签序号;
        private int _选择计数;
        private int _槽序号;   // 变量数据槽编号(标签 _v1..)
        private readonly List<变量槽定义> _槽序表 = [];   // 槽(名+宽度)按声明顺序记录,随展开结果交给汇编器
        // 变量作用域栈(编译期名字栈):栈顶=当前代码块;每层 变量名 → 变量绑定(槽+可选结构类型)
        private readonly List<Dictionary<string, 变量绑定C>> _作用域栈 = [new(StringComparer.OrdinalIgnoreCase)];
        // ---- 调试名字表(纯旁路信息):变量名/类型/在哪/在哪些行可见,汇编器与机器码完全不看它 ----
        private readonly 名字表C _名字表 = new();
        private readonly List<名字作用域区间C> _作用域区间表 = [];   // 每层块作用域占的行范围(出块时补"止")
        private readonly Stack<int> _作用域区间栈 = new();           // 当前作用域对应的区间下标(与 压作用域/弹作用域 同步)
        // 结构类型表(编译期,纯数据):结构名 → 结构类型
        private readonly Dictionary<string, 结构类型C> _结构表 = new(StringComparer.OrdinalIgnoreCase);
        // 当前语句文本里出现过的寄存器 + 已分配的临时寄存器;选临时寄存器时一律避开
        private HashSet<string>? _语句占用;
        /// <summary> 本语句已经装载过的常量 → 寄存器(同一条语句里同一个常量只装一次,省指令也省寄存器)</summary>
        private Dictionary<uint, string>? _语句常量;

        // ---- 包含(M7e):`包含 "库/xx.hb"` 在规范化阶段内联库文件 ----
        private readonly string? _基目录;
        /// <summary> T9:主程序末尾没写 `停止` 时补不补一条(正式编译补;片段展开 / "展开给我看"不补)</summary>
        private readonly bool _补停机 = true;
        private readonly HashSet<string> _已包含文件 = new(StringComparer.OrdinalIgnoreCase);

        // ---- 模块(K7):文件第一条写 `模块 名` → 这个文件里的函数/结构/全局变量都带 `名.` 前缀 ----
        // 包含是**文本展开**,所以"这一行属于哪个模块"必须逐行记下来(展开后各文件的行走在一起了)。
        private readonly List<string> _伪行模块表 = [];                       // 与 _伪行表 一一对应("" = 没有模块)
        private readonly Dictionary<string, (int 行号, string 来源)> _模块表 = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _私有名 = new(StringComparer.OrdinalIgnoreCase);   // 标了 `私有` 的全名(可能是容器前缀)
        /// <summary> 标了 `程序集` 的全名(可能是容器前缀:前缀标了,它里面的成员跟着算)。
        /// 边界 = **单个 `.hbp` 工程**,见 `校验可取用` 与 `_声明工程` </summary>
        private readonly HashSet<string> _程序集名 = new(StringComparer.OrdinalIgnoreCase);
        /// <summary> 标了 `程序集` 的名字 → 它在**哪个工程**里声明的(和 `_当前工程` 一比就知道能不能用) </summary>
        private readonly Dictionary<string, string> _声明工程 = new(StringComparer.OrdinalIgnoreCase);
        private string _本文件模块 = "";      // 正在规范化的这个文件声明的模块
        private string _当前模块 = "";        // 正在处理的那条语句属于哪个模块(处理语句 每条按 _伪行模块表 设)
        private bool _本句私有;               // 这一句的声明前面写了 `私有`
        private bool _本句程序集;             // 这一句的声明前面写了 `程序集`(英文 internal)

        // ---- 执行行映射(单步调试):展开时插 "//@源行号" 标记,供"地址→源码行"换算 ----
        private readonly bool _带源行标记;
        private int _当前源行号;              // 正在处理的源码行号(发出时打标记用)
        private int _上次标记语句号 = -1;      // 上一次插标记时的语句号(见 发出)
        private List<string>? _上次标记缓冲;   // 那个标记打在哪个缓冲上(换缓冲要重打:临时缓冲里的标记会插到别处)
        private int _语句首行;                // 正在处理的这条语句的**首行**(块语句 = 块头那一行)
        private int _语句止行;                // 正在处理的这条语句的**止行**(整块;单行语句 = 首行)
        private int _当前伪行号;              // 正在处理的这条语句在伪行表里的下标(打标记用;0 = 不在语句里)
        private readonly List<bool> _伪行来自包含 = [];   // 这个伪行是不是 `包含` 进来的文件里的
        private bool _正在包含文件;            // 规范化时:现在这段文本属于被包含的文件
        // ---- 工程级展开(第94批):一个展开器按文件表顺序连吃多个文件 ----
        /// <summary> 这个伪行属于工程文件表里的第几个文件(与 `_伪行号表` 一一对应;单文件编译全程 0) </summary>
        private readonly List<int> _伪行文件表 = [];
        /// <summary> 这个伪行**真正**来自哪一份文件(与 `_伪行文件表` 一一对应;见 `_当前来源文件`) </summary>
        private readonly List<string> _伪行来源表 = [];
        /// <summary> 工程文件表的文件名(下标就是 `_伪行文件表` 里的号);单文件编译为空 </summary>
        private readonly List<string> _工程文件表 = [];

        /// <summary> 现在在处理哪个文件(报错带文件名、标记带文件号都用它;预扫/收尾时为空) </summary>
        private string _当前文件 = "";
        private int _当前文件号 = -1;
        /// <summary>
        /// 这条语句**真正**来自哪一份文件(被 `包含` 进来时 = 那一份的全路径;其余与 `_当前文件` 同值)。
        ///
        /// **为什么要与 `_当前文件` 分开**:`_当前文件` 是"这条语句编进**哪个调试段**"(被包含进来的内容是
        /// **就地展开**的,段还是包含它的那一份),而"这个函数 / 这条报错**属于哪份文件**"要的是这一份 ——
        /// 两者的行号口径不同(库里的第 57 行不是主文件的第 57 行),混着用就会出
        /// "按文件名筛出 42 条函数、38 条行号越界"这种对不上的事(H33 第252批实测)。
        /// 怎么摆的值见 `中间层C.错误报告.cs` 的 `规范化文本`(`来源` 参数)与 `收伪行`(逐行记一份)。
        /// </summary>
        private string _当前来源文件 = "";
        /// <summary>
        /// 每个源文件属于哪个 `.hbp` 工程(下标与 `_工程文件表` 一一对应;没传 = null)。
        /// `程序集` 的边界就落在这上面:归属表空/越界 → 空串 = 都算**同一个匿名工程**,此时 `程序集` 与 `公共` 等价。
        /// </summary>
        private readonly IReadOnlyList<string>? _工程归属;
        /// <summary> 现在处理的这条语句属于哪个工程(处理语句 按 `_当前文件号` 从 `_工程归属` 取) </summary>
        private string _当前工程 = "";
        private bool _本句来自包含;            // 正在处理的这条语句是不是来自被包含的文件(发出时打标记用)

        // ---- 列表(M8a):`类型 名字[]` = 个数不写,编译器先算、算不出就自己长。
        // 列表 = 一段数据区 + 一个"长度"槽;长大时才多一个"指针槽 + 容量槽"(数据搬到池里的一块新地方) ----
        private readonly Dictionary<string, 列表绑定C> _列表表 = new(StringComparer.OrdinalIgnoreCase);
        private 变量槽定义? _池槽;          // 编译器自己开的隐藏区:动态列表从这儿要地方
        private 变量槽定义? _字符串需要槽;  // 拼接/赋值要跨"扩容子程序"的 CALL,暂存"这次要多少字节"

        /// <summary>
        /// "这次要多少字节"的暂存槽:放**数据段的隐藏槽**,全程序共用一个。
        /// 为什么不用帧里的临时槽:帧只有 64/96 字节,库函数(几个参数 + 几个局部 + 局部字符串头)很容易被它顶爆
        /// (实测:`替换` 这种"3 个字符串参数 + 结果串 + 几个局部"就正好差这 4 字节)。
        /// 这个值只在"写进去 → CALL 扩容子程序 → 读回来"这一小段里活着,而扩容子程序不调用户代码,
        /// 所以一个全局槽不会串(它不需要可重入 —— 真正的嵌套拼接在时间上是错开的)。
        /// </summary>
        private 地址位置 需要字节暂存槽() => new((_字符串需要槽 ??= 新槽(4)).名称, 0, 4);

        /// <summary> 定长缓冲/堆串写入时"起始长度"的暂存槽(见 发出串目标写入);同样是数据段的隐藏槽 </summary>
        private 地址位置 串起点暂存槽() => new((_串起点槽 ??= 新槽(4)).名称, 0, 4);
        private 变量槽定义? _串起点槽;      // 定长缓冲/堆串 写入时暂存"从第几个字节开始接"(换块的 CALL 会冲掉寄存器)
        private 变量槽定义? _池指针槽;      // 池里下一个空位(4 字节地址)
        private 变量槽定义? _池末槽;        // 池**现在**能用到哪儿(不够就按倍增往上抬,见 池要一块)
        private string? _软栈隙槽;          // 软栈与池之间的那块空闲(见 确保池;只占槽号,登记时排在软栈后面)
        private readonly int _池大小 = 中间层C.默认池大小;   // 池**一开始**多大(工程文件 `池`;不写就是 32KB)
        private readonly int _内存大小 = 虚拟机C.默认内存大小;  // 这块程序的运行内存(池硬顶按它算)
        private 变量槽定义? _错误槽;        // 运行期报错:把"错误号"写这儿再停机(IDE/命令行读它给出明确错误)
        private readonly List<(int 号, string 说明)> _错误表 = [];
        private readonly List<string> _编译提示 = [];   // 编译页给用户看的说明(列表算出来多大之类)
        private readonly List<string> _列表例程 = [];   // 列表的"扩容"子程序(像函数正文一样追加在程序最后)

        // ---- K8 堆(动态内存):独立一块(默认 64KB,可由工程文件 `堆` 配),与 32KB 池分开;块头 12 字节 ----
        // 块头:[+0 下一空闲块的块首][+4 本块总大小(含块头)][+8 空闲标志(1=空闲,0=已分配)][+12 数据]
        // 分配器:首次适配 + 切分 + **释放时合并相邻空闲块**(P33 已做;早先这里写的是"先不做合并")
        private readonly int _堆大小 = 中间层C.默认堆大小;   // 这次编译的堆区字节数(工程文件 `堆`;不写就是 64KB)
        /// <summary>
        /// **H14**:数组 / 列表的运行期下标越界检查发不发(工程文件 `"边界": "关"` ⇒ 假)。
        /// 只管 `输出下标检查` 那一档(定长数组 / 列表 / 结构字段里的数组);**字符串取字**那条
        /// (`发出文字下标检查`,按 Unicode 文字数查)不归它管 —— 那是另一件事,开关不碰它。
        /// 编译期就能算出来的常量下标越界**照旧当场报错**(它不花运行期的码,没有"省"可言)。
        /// </summary>
        private readonly bool _边界检查 = true;
        private const int 块头字节 = 12;         // 块头:下一空闲块(4) + 本块大小(4) + 空闲标志(4)
        private 变量槽定义? _堆槽;               // 堆区(这次编译配的那么大)
        private 变量槽定义? _堆首槽;             // 堆区起始地址
        private 变量槽定义? _堆末槽;             // 堆区结束地址(堆首 + 堆大小)
        private 变量槽定义? _空闲表头槽;         // 第一个空闲块的块首(0 = 链表结束)
        private string _堆分配标签 = "";         // 分配子程序的标签(编译期生成一次)
        private string _堆释放标签 = "";         // 释放子程序的标签(编译期生成一次)

        /// <summary> 一个列表变量在编译期知道的东西(数据槽/指针槽/长度槽/容量) </summary>
        private sealed class 列表绑定C
        {
            public string 名称 = "";
            public 列表类型C 类型 = null!;
            public 变量槽定义 数据槽;        // 定容:数据就在这个数据段槽里(标签直接可寻址)
            public 变量槽定义? 指针槽;       // 动态:数据在池里,这个槽装着当前首地址
            public 变量槽定义 长度槽;        // 现在有几个(运行期值)
            public 变量槽定义? 容量槽;       // 动态:现在的容量(运行期值)
            public string 扩容标签 = "";     // 动态:共用的"扩容"子程序标签(生成一次,每次添加不够就 CALL 它)
            public bool 动态 => 指针槽 != null;
            /// <summary>
            /// 字段里的列表(`筐.数`,H1 第四件):头 12 字节(数据地址 @+0 / 长度 @+4 / 容量 @+8)就在
            /// **结构内存**里,地址按这条引用链运行期算 —— 指针槽/长度槽/容量槽 三个都是 null,
            /// 一切读写都走 `列表头地址` / `读列表头字` / `写列表头字`(见 中间层C.列表.cs)。
            /// 这是一条**编译期快照**:链走到 `筐.数` 那一刻的样子(偏移已经含字段偏移),以后再往下取下标也不影响它。
            /// </summary>
            public 引用链C? 字段链;
            public bool 是字段列表 => 字段链 != null;
            /// <summary> 这个列表的数据在**堆**里、装不下就翻倍(池里的动态列表与字段列表都是) </summary>
            public bool 会长大 => 指针槽 != null || 是字段列表;
            /// <summary> 这个"列表"其实是一个**字符串**(语法糖):元素宽固定 1,长度语义是"字节数",
            /// 下标上界看长度(不是容量)、不许 .添加()、不许取地址(数据在池里会迁移) </summary>
            public bool 是字符串;
        }

        // ---- 函数(M7d):固定帧 + 软栈 + R15 作帧指针 FP ----
        // 帧大小(H34)是**一个可配的值**(工程文件 `帧`),两档优化共用 —— 与 R1-1 的软栈同款口径:
        // 优化开关只管"槽怎么放"(短整数按 2 字节还是 4 字节),不再管"帧多大、能递归多少层"。
        // 第97批之前老档帧更小(C 档把短整数也按 4 字节放 ⇒ 帧要更大),两档统一成 96 之后,
        // H34 又把 96 从写死的常量改成"默认值"(`中间层C.默认帧大小`)。
        private readonly int _软栈大小 = 中间层C.默认软栈大小(虚拟机C.默认内存大小);   // R1-1:工程文件 `软栈` 可配;不写按运行内存算
        private readonly int _帧大小 = 中间层C.默认帧大小;                                // H34:工程文件 `帧` 可配;不写就是 96
        private int 函数帧大小 => _帧大小;
        private int 软栈大小 => _软栈大小;

        /// <summary>
        /// **R3-1:帧不够时的统一改法**。帧是"参数 + 局部变量 + 表达式临时槽"**共用**的一块,
        /// 所以"少用一个局部""把长表达式拆成两句""大结构按引用传""把函数拆小"都是有效出路 ——
        /// 报错只说一句"太复杂"是没法指导优化的,得把这几条路写出来(硬规矩 2:报错要给改法)。
        /// </summary>
        private string 帧超限改法() =>
            $"这块帧(上限 {函数帧大小} 字节)是参数、局部变量、表达式临时槽共用的一块。办法:" +
            "把大结构改成按引用传的参数(参数写成 `引用 结构名 参数名`)、把不常用的局部提成全局变量或结构字段、" +
            "把一个长表达式拆成两三句用中间变量接,或者把这个函数拆成两个。";

        /// <summary>
        /// **R3-1**:每个函数编译完那一刻的帧用量(函数名 → 用到几字节)。
        /// 帧是**复用**的、`局部偏移` 只增不减 ⇒ 那一趟跑完的值就是峰值;收尾时汇总成一条编译提示给用户看。
        /// </summary>
        private readonly List<(string 函数, int 用量)> _帧用量 = [];
        private const string 帧指针寄存器 = "R15";   // 编译器保留:帧指针
        private const string 帧槽前缀 = "@FP";       // 帧相对地址的编码前缀(局部变量/参数)
        private readonly Dictionary<string, List<(string 名, int 宽度, string 类型名, 引用类型C? 引用)>> _函数表 = new(StringComparer.OrdinalIgnoreCase);
        /// <summary> 函数 → 返回类型词(基础类型名或结构名)。**不在表里 = 无返回值(void)**。
        /// 调用点靠它决定两件事:① 结果能不能当值用 ② 返回结构时要多递一个"存放地址"的隐藏参数。</summary>
        private readonly Dictionary<string, string> _函数返回类型 = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _已定义函数 = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// 顶层名字(函数 / 结构 / 全局变量)→ 它在**哪个文件第几行**声明的。
        /// 工程级编译里名字必须唯一(D2 决策):撞名时要把**两处位置**都报出来,不然用户只知道"重复了",
        /// 却不知道另一处在哪(几十个文件时这是最要紧的一句话)。
        /// </summary>
        private readonly Dictionary<string, (string 文件, int 行)> _声明出处 = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _函数正文 = [];
        /// <summary>
        /// 顶层常量的初值(槽名 + 偏移 + 宽度 + 值):**不编成"程序开头一串存储指令"**,随编译结果交给装载方,
        /// 由装载时直接写进内存(见 `程序包C.应用初值` / `虚拟机C.初值表`)。函数帧里的初值照旧发现场指令。
        /// </summary>
        private readonly List<(string 槽名, int 偏移, int 宽度, uint 值)> _顶层初值 = [];
        private string? _软栈槽;
        private sealed class 函数上下文
        {
            public string 名称 = "";
            public int 局部偏移;                       // 下一个可用帧偏移(参数之后)
            public int 参数总宽;
            /// <summary> 返回类型词(空 = 无返回值 void)与解析出来的结构(返回结构时非 null)</summary>
            public string 返回类型词 = "";
            public 结构类型C? 返回结构;
            /// <summary> 返回字符串时 true:调用方递进来的是**它自己那个字符串头的地址**(数据地址/长度/容量 三个字);
            /// 被调方把内容整份拷进去(容量不够就换块),值语义与赋值一致。</summary>
            public bool 返回字符串;
            /// <summary> 返回结构/字符串时:调用方递进来的"存放地址"在帧里的偏移(-1 = 都不是)</summary>
            public int 返回地址偏移 = -1;
            /// <summary> 入口需要清零的帧区间(起, 宽)。只有"声明时不会立刻写满自己"的局部才登记进来 </summary>
            public readonly List<(int 起, int 宽)> 需清零 = [];
            /// <summary> 这个函数体里从池里要过地方(字符串/列表):收尾时据此决定要不要退池水位(见 收尾池水位)</summary>
            public bool 用到池;
            /// <summary> 体里出现过 `引用`(参数或局部):池对象可能顺着引用流到调用方,那种不退水位 </summary>
            public bool 有引用;
            /// <summary>
            /// H60 步②:这是**容器的成员方法**时,收方(那个隐藏的第一个参数)在帧里叫什么名字。
            /// 空 = 不是成员方法(普通函数 / 顶层过程)。方法体里的裸字段名要改写成 `<它>.字段`;
            /// 容器体里裸叫别的成员时,也要把这个收方**原样传下去**(见 `解析函数链路`)。
            /// </summary>
            public string 收方名 = "";
        }
        private 函数上下文? _当前函数;

        // ---- 方法容器(M7f):`方法 名()` = 代码的名字空间;`静态 方法 名(字段...)` = 带常驻状态。
        // 静态容器 = "一个内部结构类型 + 一个常驻结构变量",方法里直接写字段名(编译期补 容器名.) ----
        private sealed class 方法容器C
        {
            public string 名称 = "";
            public bool 静态;
            public readonly Dictionary<string, string> 字段全名 = new(StringComparer.OrdinalIgnoreCase);   // 宽 → 棋盘.宽
            public readonly Dictionary<string, string> 方法全名 = new(StringComparer.OrdinalIgnoreCase);   // 面积 → 棋盘.面积
            public readonly Dictionary<string, string> 方法标签 = new(StringComparer.OrdinalIgnoreCase);   // 面积 → 棋盘_面积
        }
        private readonly Dictionary<string, 方法容器C> _容器表 = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// H60 步④:**结构自己的方法** —— 结构名 → (方法短名 → 定义名 `点.到原点`)。
        /// 结构里的方法只有一条路能进来:声明时带 `方法` 关键字(数据与方法在同一层,得一眼分得开);
        /// 成员方法的定义名与容器成员同一套写法(带点名),汇编标签把点换成下划线。
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, string>> _结构方法表 = new(StringComparer.OrdinalIgnoreCase);
        /// <summary> 结构方法体里:裸字段名 → `_收方.字段`(与容器那张 `方法容器C.字段全名` 同一套用法)</summary>
        private Dictionary<string, string>? _当前结构字段全名;
        /// <summary> 结构方法体里:方法短名 → 定义名(裸叫兄弟成员用;容器那边用的是 `方法容器C.方法全名`)</summary>
        private Dictionary<string, string>? _当前结构方法表;
        /// <summary> 结构方法体里:正在处理哪个结构(定义名要带它的名;调试/报错也用得上)</summary>
        private string _当前结构名 = "";
        /// <summary>
        /// H60 步②:成员方法的**定义名 → 容器全名**(`计数器.累加` → `计数器`)。
        /// 调用点靠它认出"这是个成员方法、要额外递一个收方";普通函数不在这张表里。
        /// </summary>
        private readonly Dictionary<string, string> _成员收方 = new(StringComparer.OrdinalIgnoreCase);
        /// <summary> 成员方法体里那个隐藏收方参数的**固定名字**(用户写不出同名的参数会被 `校验变量名` 挡住)</summary>
        private const string 收方参数名 = "_收方";
        private readonly Dictionary<string, string> _函数标签 = new(StringComparer.OrdinalIgnoreCase);   // 定义名 → 汇编标签
        /// <summary> G2:`回调 名字 = 地址(函数名);` 里"名字 → 函数名"的登记(调用点照它摆参数槽;大小写不敏感)</summary>
        private readonly Dictionary<string, string> _回调表 = new(StringComparer.OrdinalIgnoreCase);
        /// <summary> G2:`回调 比较(整数, 整数)` 这种**形参**的形参类型表(键 = `函数名.参数名`)</summary>
        private readonly Dictionary<string, List<string>> _函数回调形参 = new(StringComparer.OrdinalIgnoreCase);
        /// <summary> H86:一次 `解析函数头` 里攒下来的"回调形参 → 它的形参表"。
        /// **为什么要攒一道**:定义名(`模块.名` / `容器.名`)要等 `解析函数头` 返回**之后**才算得出来,
        /// 而这张表按 `函数名.参数名` 登记 —— 所以先存这儿,由声明那一段(定义名定了以后)一次登记完。 </summary>
        private List<(string 参数名, List<string> 形参表)>? _本次解析回调形参;
        private 方法容器C? _当前容器;
        private int _主缓冲序言行数;      // 序言(软栈初始化等)的行数:"程序开头跑一次"的代码插在这之后
        private readonly List<string> _开头初始化 = [];   // 拼接的"程序开头跑一次"代码(见 插入主程序开头)

        // ---- 编译期优化(2026-09):消冗余指令 + 循环不变量/常量提升。纯编译期的事:ISA 一个字不改、语义不变 ----
        private readonly bool _优化开;
        private int _优化省下;                    // A 档:少发的冗余指令条数(消掉的 MOV 之类)
        private int _提升个数;                    // B 档:提到循环体外的"不变量/常量"个数
        private int _地址增量个数;                // C 档(P1):改成"常驻指针每轮步进"的数组个数
        private int _长度缓存个数;                   // C 档(S12):循环里改成"常驻字数"的字符串个数
        private sealed class 循环保留C
        {
            public readonly Dictionary<string, string> 变量 = new(StringComparer.OrdinalIgnoreCase);   // 变量名 → 常驻寄存器
            public readonly Dictionary<uint, string> 常量 = [];                                        // 常量值 → 常驻寄存器
            public readonly HashSet<string> 寄存器 = new(StringComparer.OrdinalIgnoreCase);
            /// <summary>
            /// P1 数组地址增量:这一层的"步进指针" —— 数组名(用户在源码里写的那个)→
            /// (元素宽, 常驻寄存器, 槽名)。循环体里 `数组[归纳变量]` 直接拿这个寄存器当地址,
            /// 每轮在步进点 `ADD_IMMEDIATE 元素宽`;不再每轮算 基址 + 下标×元素宽。
            /// </summary>
            public readonly List<步进指针项C> 步进指针 = [];
            /// <summary>
            /// S12:`字符串.长度()` 要按 UTF-8 **数一遍文字**(一次约 40 条指令)—— 循环体/条件里反复读同一个字符串
            /// 的字数、体里又没写过它时,循环外先数一次放进常驻寄存器,整段循环直接用这个寄存器。
            /// </summary>
            public readonly Dictionary<string, string> 长度 = new(StringComparer.OrdinalIgnoreCase);   // 字符串名 → 常驻寄存器(字数)
        }

        /// <summary>
        /// 一个"步进指针":数组名 + 它现在指向第几个(归纳变量名)+ 元素宽 + 常驻寄存器 + 槽名 +
        /// **下标偏移**(H4:`表[i + 1]` 那个 1,单位是元素个数;0 = 最干净的 `表[i]`)。
        /// 指针 = 基址 + (初值 + 下标偏移) × 元素宽,每轮照旧只走元素宽×步长 ——
        /// 所以**偏移是折进初值的,热路径一条都不多花**;偏移不同就是不同一个指针(各占一个常驻寄存器)。
        /// </summary>
        private sealed record 步进指针项C(string 数组名, string 索引名, int 元素宽, string 寄存器, string 槽名, int 下标偏移 = 0);

        /// <summary>
        /// 循环头的"归纳变量"信息(K3 的 P1 余量 —— 数组地址增量用):
        /// 变量名 + 每轮步长 + 初值(常量初值才能把"起点偏移"并进指针初始化里)。
        /// </summary>
        private readonly record struct 归纳信息C(string 变量, int 步长, uint? 初值);
        private readonly List<循环保留C> _循环保留栈 = [];   // 嵌套循环:每层一组常驻寄存器(语句分配临时寄存器时一律避开)

        private static string 帧槽(int 偏移) => 帧槽前缀 + 偏移;
        private static bool 解析帧槽(string 槽, out int 偏移)
        {
            偏移 = 0;
            return 槽.StartsWith(帧槽前缀, StringComparison.Ordinal)
                && int.TryParse(槽[帧槽前缀.Length..], out 偏移);
        }

        /// <summary>
        /// 变量槽宽度(C 档优化):`短整数`(2 字节)的**独立变量**按 4 字节放 ——
        /// 这样读是一条 `取字`、写是一条 `存字`(ISA 没有 2 字节读写,紧排的话读要掩码、写要拆两次存)。
        /// 结构字段 / 数组元素 / 列表元素**仍然紧排**(它们的偏移和元素宽度是用户看得见的布局);
        /// 优化关掉时一律按声明宽紧排(老的布局)。
        /// </summary>
        private int 槽宽度(int 声明宽) => _优化开 && 声明宽 == 2 ? 4 : 声明宽;

        /// <summary> 按声明宽把值截断(掩码);值的高位抹掉后才能存进宽槽</summary>
        private uint 截断掩码(int 声明宽) => 声明宽 >= 4 ? 0xFFFFFFFFu : (1u << (声明宽 * 8)) - 1;

        /// <param name="工程文件表">
        /// **工程级展开**(第94批):非空时按顺序连吃这些文件(一个展开器 = 一张工程级符号表)。
        /// 决定跨文件能不能用函数/结构/全局变量,也决定序言(池/堆/软栈)只发一份。
        /// 传了它就不看 `源代码`(那是单文件那条路)。
        /// </param>
        /// <param name="工程归属">
        /// 每个源文件属于哪个 `.hbp` 工程(与 `工程文件表` 一一对应;不传 = 都算同一个匿名工程,
        /// 此时 `程序集` 与 `公共` 等价)。
        /// </param>
        /// <param name="边界检查">
        /// **H14**:数组 / 列表的运行期下标越界检查发不发。默认 `true`(照旧查);
        /// 工程文件里写 `"边界": "关"` ⇒ 假 ⇒ 那 +2~3 条/次访问一条都不发(极限速度那一档,
        /// 越界的后果由工程作者自己负责 —— 详见 `输出下标检查` 与帮助《第9节·工程文件》)。
        /// 与 `堆大小` 一样,它改变生成的机器码 ⇒ 必须在展开缓存的键里(见 `汇编器C.工程指纹`)。
        /// </param>
        /// <param name="帧大小">
        /// **H34**:每个函数帧多大(工程文件里的 `帧`)。**-1 = 默认** = `中间层C.默认帧大小`(96)。
        /// 范围 `最小帧大小` ~ `最大帧大小`,必须 4 的倍数、必须 ≤ 软栈;错了一律在这儿抛(消息可直接给用户看)。
        /// </param>
        /// <param name="补停机">
        /// **T9**(2026-09-27 评测整改):主程序末尾没写 `停止` 时补一条带生成标记的 `停止`(见 `运行()`)。
        /// **正式编译一律 true**(命令行 / IDE / 工程编译 / `--asm` 都走这里);**片段展开 / "展开给我看" 用 false**
        /// —— 那些文本会被拼回源码或只用来显示,里面多一条 `停止` 会把后面的残留代码挡在门外
        /// (实测:`展开块 + 残留块` 拼起来跑,补出来的 `停止` 一停机,第二个循环就不跑了)。
        /// </param>
        public 中间层展开器(string 源代码, IReadOnlyDictionary<string, 变量绑定C>? 外部变量 = null, IReadOnlyDictionary<string, 结构类型C>? 外部结构 = null, int 起始槽号 = 0, int 起始标签号 = 0, int 起始选择号 = 0, string? 基目录 = null, bool 带源行标记 = false, bool 优化 = false, int 堆大小 = 中间层C.默认堆大小, IReadOnlyList<(string 名称, string 内容)>? 工程文件表 = null, IReadOnlyList<string>? 工程归属 = null, int 池大小 = 中间层C.默认池大小, int 内存大小 = 虚拟机C.默认内存大小, int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1, bool 补停机 = true)
        {
            _基目录 = 基目录;
            _带源行标记 = 带源行标记;
            _优化开 = 优化;
            _工程归属 = 工程归属;
            _边界检查 = 边界检查;
            _补停机 = 补停机;
            // 堆大小从工程文件来,这是最后一道闸:数不对就别往下走(不然会"按 0 字节堆"编出一份看着没事的机器码)
            if (堆大小 < 中间层C.最小堆大小 || 堆大小 > 中间层C.最大堆大小)
                throw new Exception($"堆大小超范围:{堆大小} 字节 —— 只能是 {中间层C.最小堆大小} ~ {中间层C.最大堆大小} 字节" +
                    "(工程文件里的 `堆` 可以写 64KB、128KB,或者直接写字节数 131072);");
            _堆大小 = 堆大小;
            // R1-2:池大小与运行内存大小同理(池一开始多大 / 池往上长到哪儿)
            if (池大小 < 中间层C.最小池大小 || 池大小 > 中间层C.最大池大小)
                throw new Exception($"池大小超范围:{池大小} 字节 —— 只能是 {中间层C.最小池大小} ~ {中间层C.最大池大小} 字节" +
                    "(工程文件里的 `池` 可以写 32KB、256KB,或者直接写字节数 131072);");
            if (内存大小 < 虚拟机C.最小内存大小 || 内存大小 > 虚拟机C.最大内存大小)
                throw new Exception($"内存大小超范围:{内存大小} 字节 —— 只能是 {虚拟机C.最小内存大小} ~ {虚拟机C.最大内存大小} 字节" +
                    "(工程文件里的 `内存` 可以写 1MB、512KB,或者直接写字节数 1048576);");
            // R1-1:软栈大小同理(不写 = 按运行内存自动算 min(64KB, 内存/4));它是机器码的一部分(软栈槽宽度 + 深度上限立即数)
            int 软栈实际 = 软栈大小 < 0 ? 中间层C.默认软栈大小(内存大小) : 软栈大小;
            if (软栈实际 < 中间层C.最小软栈大小 || 软栈实际 > 中间层C.最大软栈大小)
                throw new Exception($"软栈大小超范围:{软栈实际} 字节 —— 只能是 {中间层C.最小软栈大小} ~ {中间层C.最大软栈大小} 字节" +
                    "(工程文件里的 `软栈` 可以写 64KB、128KB,或者直接写字节数 65536);");
            if (软栈实际 > 内存大小)
                throw new Exception($"软栈比运行内存还大:软栈 {软栈实际} 字节 / 内存 {内存大小} 字节" +
                    "(软栈是从运行内存里切出来的一块,请把工程文件里的 `内存` 调大、或者把 `软栈` 改小);");
            _软栈大小 = 软栈实际;
            // H34:帧大小(参数 + 局部变量 + 表达式临时槽共用一块)—— 与软栈同一个口径的可配值。
            // 三条闸:① 在可配范围里 ② 4 的倍数(帧按 4 字节一个槽排,不是 4 的倍数会错位)
            // ③ 不许比软栈还大(帧住在软栈里,一帧比整块软栈还大就一层都放不下)
            int 帧实际 = 帧大小 < 0 ? 中间层C.默认帧大小 : 帧大小;
            if (帧实际 < 中间层C.最小帧大小 || 帧实际 > 中间层C.最大帧大小)
                throw new Exception($"帧大小超范围:{帧实际} 字节 —— 只能是 {中间层C.最小帧大小} ~ {中间层C.最大帧大小} 字节" +
                    "(工程文件里的 `帧` 可以写 128、256,或者直接写字节数 128;不写就是 96);");
            if (帧实际 % 4 != 0)
                throw new Exception($"帧大小必须是 4 的倍数:{帧实际} 字节 —— 帧按 4 字节一个槽排,不是 4 的倍数会错位" +
                    "(工程文件里的 `帧` 可以写 96、128、256);");
            if (帧实际 > 软栈实际)
                throw new Exception($"帧比软栈还大:帧 {帧实际} 字节 / 软栈 {软栈实际} 字节" +
                    "(帧就住在软栈里,一帧比整块软栈还大就没法装下;请把工程文件里的 `软栈` 调大、或者把 `帧` 改小);");
            _帧大小 = 帧实际;
            _池大小 = 池大小;
            _内存大小 = 内存大小;
            if (工程文件表 is { Count: > 0 })
            {
                // 工程级(第94批):一个展开器连吃多文件 —— 符号表/全局作用域/槽号/序言天然共享,
                // 跨文件函数、结构、全局变量因此直接可用。每个文件仍然:①用自己的目录解析 `包含`
                // ②`包含` 只展开一次(所以每个顶层文件开始时清一次,见 D3 决策)
                // ③行号从各自文件的第 1 行数起(标记里带 `文件K` 区分,见 发出)
                for (int i = 0; i < 工程文件表.Count; i++)
                {
                    string 名称 = 工程文件表[i].名称 ?? "";
                    _当前文件号 = i;
                    _当前文件 = 名称;
                    _工程文件表.Add(名称);
                    _已包含文件.Clear();
                    string? 目录 = null;
                    // H64:`名称` 可能是**系统目录那一档**的虚拟路径(`内容://…`)—— 那种路径不许走
                    // `Path.GetDirectoryName`(它会把 `内容://` 当成"相对路径里的一个怪文件名",
                    // 算出 `内容:\树…` 这种谁都不认识的名字,于是文件里那句 `包含` 一路找不到文件)
                    try { 目录 = 虚拟路径C.所在目录(名称); } catch { 目录 = null; }
                    规范化文本(工程文件表[i].内容 ?? "", string.IsNullOrEmpty(目录) ? null : 目录, 0, 名称);
                }
                _当前文件 = "";
                _当前文件号 = -1;
            }
            else 规范化(源代码);
            _缓冲栈.Push([]);
            _槽序号 = 起始槽号;      // 片段展开:槽号接着前缀(构造前已声明变量)继续编号
            _标签序号 = 起始标签号;  // 部分展开过的文件:标签号接着已存在内部标签继续,避免重名
            _选择计数 = 起始选择号;  // switch 分支选择号同样续接(_fCase{选择号}_{项})
            if (外部结构 != null)
                foreach (var (名, 结构) in 外部结构)
                    _结构表[名] = 结构;
            if (外部变量 != null)
                foreach (var (名, 绑定) in 外部变量)
                    当前作用域[名] = 绑定;   // 外层可见变量预置进根作用域

            // 预扫函数头:先收集签名(参数名/宽度),这样"先调用、后定义"也能编译。
            // 容器(方法 名 / 静态 方法 名)里的成员按 容器名.成员名 登记:
            // 容器栈靠 { } 深度跟踪,只有"正好在容器第一层"的 名(...) { 才算成员
            var 容器栈 = new List<(string 名, int 深度)>();
            var 结构栈 = new List<(string 名, int 深度)>();   // H60 步④:结构体里的方法也要预扫签名
            int 深度 = 0;
            string? 待开容器 = null;
            string? 待开结构 = null;
            for (int i = 0; i < _伪行表.Count; i++)
            {
                string 行 = _伪行表[i];
                if (行 == "{")
                {
                    深度++;
                    if (待开容器 != null) { 容器栈.Add((待开容器, 深度)); 待开容器 = null; }
                    if (待开结构 != null) { 结构栈.Add((待开结构, 深度)); 待开结构 = null; }
                    continue;
                }
                if (行 == "}")
                {
                    深度--;
                    while (容器栈.Count > 0 && 容器栈[^1].深度 > 深度) 容器栈.RemoveAt(容器栈.Count - 1);
                    while (结构栈.Count > 0 && 结构栈[^1].深度 > 深度) 结构栈.RemoveAt(结构栈.Count - 1);
                    continue;
                }
                _当前模块 = _伪行模块表[i];          // 预扫也要按行看模块:`私有 函数 f` 与模块前缀都靠它
                _当前工程 = 文件工程(i);              // 预扫收 `程序集` 档位时要记"是哪个工程的"
                _当前源行号 = _伪行号表[i];           // 私有成员的报错也要带对行号
                string 原始行 = 行;                    // 可见性前缀剥掉之前的样子(判 `程序集` 用)
                行 = 去可见性前缀(行);                // `私有` / `程序集` 都要剥掉,不然函数头认不出来
                string 首词 = 取首词(行);
                string? 词 = 中间层C.到英文关键字(首词);
                if (词 == "struct")
                {
                    // H60 步④:结构体里的 `方法 …` 也要预扫签名(递归 / 先调用后定义)
                    待开结构 = 限定名(行[首词.Length..].Trim());
                    continue;
                }
                bool 是结构第一层 = 结构栈.Count > 0 && 深度 == 结构栈[^1].深度;
                if (是结构第一层 && 词 == "method")
                {
                    if (解析函数头(行, 0, 抛错: false) is not { } 结构头) continue;
                    string 结构定义名 = 结构栈[^1].名 + "." + 结构头.名;
                    if (!结构头.参数.Any(p => p.名 == 收方参数名))
                        结构头.参数.Insert(0, (收方参数名, 4, 结构栈[^1].名,
                            new 引用类型C(结构栈[^1].名, null, 0)));
                    _函数表[结构定义名] = 结构头.参数;
                    if (结构头.返回类型词.Length > 0) _函数返回类型[结构定义名] = 结构头.返回类型词;
                    continue;
                }
                if (词 is "method" or "static")
                {
                    待开容器 = 试读容器名(行);      // 不是容器头(带参数的非静态 方法 = 普通函数)则为 null
                    if (待开容器 != null) { 待开容器 = 限定名(待开容器); continue; }
                }
                bool 是容器第一层 = 容器栈.Count > 0 && 深度 == 容器栈[^1].深度;
                bool 是裸成员 = 是容器第一层 && 词 == null && 下一行是块(i) && 是裸函数头(行);
                if (词 is not "method" && !是裸成员) continue;
                if (解析函数头(是裸成员 ? "方法 " + 行 : 行, 0, 抛错: false) is not { } 头) continue;
                string 全名 = 头.名;
                if (是容器第一层 && !全名.Contains('.')) 全名 = 容器栈[^1].名 + "." + 全名;
                else if (!是容器第一层) 全名 = 限定名(全名);      // 模块里的普通函数:也带前缀
                // H60 步②:成员方法多一个**隐藏的第一个参数(收方)** —— 预扫这一趟也补上,
                // 不然递归 / 先调用后定义那条路会按"用户写了几个实参"去数,当场报"参数个数不对"。
                // 这里只求"调用点算得对":收方是个 4 字节 `引用`,结构类型那一格留空(这一趟结构还没登记),
                // 真正的绑定由声明那一句按真结构类型建(见 `处理函数声明`)。
                if (是容器第一层 && !头.参数.Any(p => p.名 == 收方参数名))
                {
                    string 内部结构名 = "_方法_" + 容器栈[^1].名;
                    头.参数.Insert(0, (收方参数名, 4, 内部结构名, new 引用类型C(内部结构名, null, 0)));
                }
                _函数表[全名] = 头.参数;
                if (头.返回类型词.Length > 0) _函数返回类型[全名] = 头.返回类型词;
                // `程序集` 的函数档位要在预扫里就收一份:函数靠预扫才做到"先调用、后定义"(跨文件任意顺序),
                // 档位要是等到声明那一句才记,排在前面那个文件调它就查不出"不是同一个工程"(见 校验可取用)。
                // `私有` 不收:它的档位按"模块"判,老口径原样不动。
                if (中间层C.到英文关键字(取首词(原始行)) == "internal") 记程序集档(全名, _当前工程);
            }
            _当前模块 = "";
            _当前工程 = "";
            if (_函数表.Count > 0)
            {
                _软栈槽 = 预留槽号();                        // 槽号照旧优先于变量前面(名字不变),但**登记放到最后**(见 运行)
                发出($"取地址 {帧指针寄存器} {_软栈槽}");   // 程序开头:FP = 软栈底
            }
            _主缓冲序言行数 = 当前缓冲.Count;   // 容器初始化脚本插在这之后(得先有 FP 才能调函数)

            // 工程级声明预扫(Q1b):结构与顶层全局变量先登记一遍 —— 这样"先用、后声明"和
            // "库文件排后面"都能编译(与上面的"预扫函数头"同一个道理,只是收的是名字+槽)。
            // 摆在这儿(软栈槽的槽号已经占下之后)是为了让数据槽的编号顺序与以前一致。
            // 只登记不发码;初始化代码仍然留在原语句那一句上(见 中间层C.声明预扫.cs)。
            预扫顶层声明();
        }

        private List<string> 当前缓冲 => _缓冲栈.Peek();
        private Dictionary<string, 变量绑定C> 当前作用域 => _作用域栈[^1];
        private void 发出(string 行)
        {
            // 单步调试要的标记:挂在"每条源码行生成的第一条指令"后面当注释(不占字节、不影响编译)。
            // 以前它单独占一行,一堆 `// 来自第 N 行` 夹在指令中间,看着又乱、标黄在哪条也对不上(用户反馈);
            // 现在跟在指令屁股后面 —— 黄条压在哪条指令上,它来自第几行,一眼就对得上。
            //
            // 标记里带**范围**(`// 来自第 7 到 12 行`):光写"当前处理到第几行"没法还原"一条指令属于哪条语句" ——
            // 循环体里编译器自己开的临时槽会一直挂着循环头那一行;**被 `包含` 进来的文件行号还跟主文件撞号**
            // (那样的语句带 `库` 字)。编辑器"就地展开"就按这个范围挑指令(见 中间层C.展开片段)。
            //
            // 第136批:去重还要看**打在哪个缓冲上**。字符串字面量的初始化是"先发进临时缓冲、最后插到程序开头"
            // (见 输入输出.字符串槽),那几条指令会把本语句的标记用掉 —— 回到主缓冲后本语句自己的代码就没有标记了,
            // 查表只能沿用上一条(于是单步黄条乱跳)。所以"换缓冲"也算没打过标记,重新打一次。
            if (_带源行标记 && _语句首行 > 0
                && (_当前伪行号 != _上次标记语句号 || !ReferenceEquals(_上次标记缓冲, 当前缓冲)))
            {
                int 止 = _语句止行 >= _语句首行 ? _语句止行 : _语句首行;
                // 工程级展开(第94批):标记里还要带**哪个文件** —— 一个展开器连吃多文件之后,
                // 行号是各文件自己的行号,光凭 `第 N 行` 分不出是哪个文件(两个文件都有第 5 行)。
                string 文件前缀 = _工程文件表.Count > 0 && _当前文件号 >= 0 ? $"文件{_当前文件号 + 1}" : "";
                // H47:`库` 后面再带上**是哪一份**库文件(只要文件名)—— 两份库的行号本来就撞(都从 1 数起),
                // 光凭行号分不出来,调用关系那份边表于是只能把库函数体里的调用记成「顶层(程序段)」。
                string 库后缀 = _本句来自包含 && _当前来源文件.Length > 0 ? " 库 " + _当前来源文件 : (_本句来自包含 ? " 库" : "");
                行 = $"{行}   // 来自{文件前缀}{(止 > _语句首行 ? $"第 {_语句首行} 到 {止} 行" : $"第 {_语句首行} 行")}"
                    + 库后缀;
                _上次标记语句号 = _当前伪行号;
                _上次标记缓冲 = 当前缓冲;
            }
            当前缓冲.Add(行);
        }

        private string 新标签(string 前缀) => $"_f{前缀}{++_标签序号}";

        /// <summary> 新开一个数据槽(带宽度),按声明顺序登记,随展开结果交给汇编器紧致分配地址 </summary>
        private 变量槽定义 新槽(int 宽度)
        {
            var 定义 = new 变量槽定义($"_v{++_槽序号}", 宽度);
            _槽序表.Add(定义);
            return 定义;
        }

        /// <summary>
        /// 只占一个槽号、**先不登记**:软栈槽要排在数据段的**最后**。
        /// 为什么:软件栈是"函数帧"的活动区,它后面如果还跟着用户变量,递归一深就会写到变量上
        /// (测评实测过:60 层递归把 `var 表[64]` 悄悄改坏)。放到最后,溢出只会写数据区之后的空闲内存。
        /// </summary>
        private string 预留槽号() => $"_v{++_槽序号}";

        // ---- 调试名字表:登记与查询(见 引擎\名字表C.cs) ----
        /// <summary> 当前所在块作用域的行区间(在根作用域里返回 null = 全文件可见) </summary>
        private 名字作用域区间C? 当前区间 =>
            _作用域区间栈.Count > 0 ? _作用域区间表[_作用域区间栈.Peek()] : null;

        /// <summary> 压一层变量作用域,顺手记下它从第几行开始(调试器靠它判断"这一行能看见哪些变量") </summary>
        private void 压作用域(int 起行)
        {
            _作用域栈.Add(new Dictionary<string, 变量绑定C>(StringComparer.OrdinalIgnoreCase));
            _类型回滚栈.Push([]);                 // 这一层里登记过的"变量类型"出层时要撤掉
            _作用域区间表.Add(new 名字作用域区间C { 起 = 起行 });
            _作用域区间栈.Push(_作用域区间表.Count - 1);
        }

        /// <summary> 弹一层变量作用域,补上它到第几行结束 </summary>
        private void 弹作用域(int 止行)
        {
            if (_作用域区间栈.Count > 0)
            {
                int 下标 = _作用域区间栈.Pop();
                if (下标 >= 0 && 下标 < _作用域区间表.Count) _作用域区间表[下标].止 = 止行;
            }
            if (_作用域栈.Count > 1) _作用域栈.RemoveAt(_作用域栈.Count - 1);
            if (_类型回滚栈.Count > 0)
                foreach (var (名, 旧值) in _类型回滚栈.Pop())
                {
                    if (旧值 == null) _变量类型.Remove(名);
                    else _变量类型[名] = 旧值;
                }
        }

        /// <summary> 最后一个伪行对应的源行号(出块补"止"用) </summary>
        private int 上一伪行源行号(int 当前位置) =>
            _伪行号表.Count == 0 ? 1 : _伪行号表[Math.Clamp(当前位置 - 1, 0, _伪行号表.Count - 1)];

        /// <summary>
        /// 把一个变量登记进调试名字表:全局变量记槽标签,局部/参数记"所属函数 + 帧偏移"。
        /// 只是给调试用的旁路信息,汇编器根本不看,机器码一个字节都不会变。
        /// </summary>
        private void 登记名字(string 名称, 变量槽定义 定义, 类型C.类型定义? 类型, 结构类型C? 结构, 数组类型C? 数组, int 行号,
            bool 是参数 = false, 列表绑定C? 列表 = null, 引用类型C? 引用 = null, string? 显示类型名 = null)
        {
            bool 是帧 = 解析帧槽(定义.名称, out int 偏移);
            _名字表.变量.Add(new 名字项C
            {
                名字 = 名称,
                // H68-2:枚举变量登记的是**枚举名**(底层类型是 整数,可调试窗口该说的是"颜色"不是"整数")
                类型名 = 引用 != null ? 引用.指向类型名
                    : 列表 != null ? 列表.类型.元素类型名 : 数组 != null ? 数组.元素类型名
                    : 结构?.名称 ?? 显示类型名 ?? 类型?.中文 ?? "32位字",
                宽度 = 列表 != null ? 0 : 定义.宽度,
                是全局 = !是帧,
                槽 = 是帧 ? "" : 定义.名称,
                所属函数 = 是帧 ? _当前函数?.名称 ?? "" : "",
                帧偏移 = 是帧 ? 偏移 : 0,
                声明行 = 行号,
                声明文件 = _当前文件,
                元素宽 = 列表?.类型.元素宽 ?? 数组?.元素宽 ?? 0,
                元素个数 = 列表 != null ? 列表.类型.容量 : 数组?.长度 ?? 0,
                是列表 = 列表 != null,
                是引用 = 引用 != null,
                指向类型名 = 引用?.指向类型名 ?? "",
                指向宽 = 引用?.整块宽 ?? 0,
                是字符串参数 = 引用 is { 是字符串: true },
                出参数 = 引用 is { 是字符串: true, 出参数: true },
                区间 = 是参数 ? null : _当前函数 != null || _作用域区间栈.Count > 0 ? 当前区间 : null,
            });
            // C19:全局变量的可见性也记一份(`私有` / `程序集` 的全局在别的模块/工程里不补)
            if (!是帧 && _当前函数 == null) 记可见性(名称);
        }

        private static readonly Regex 首词正则 = new(@"^[\p{L}_][\p{L}\p{N}_]*", RegexOptions.Compiled);
        private static readonly Regex 标识符全词正则 = new(@"^[\p{L}_][\p{L}\p{N}_]*$", RegexOptions.Compiled);
        private static readonly Regex 标识符带点正则 = new(@"^[\p{L}_][\p{L}\p{N}_]*(\.[\p{L}_][\p{L}\p{N}_]*)*$", RegexOptions.Compiled);
        /// <summary>
        /// 调用点那一段"函数名"的形状(H60 步②):比 `标识符带点正则` 宽一档 —— 收方还能是**数组/列表元素**
        /// (`表[1].累加`、`名单[下标].清空`),所以每一段后面都允许跟一个 `[…]`。只用在"认这是不是调用"这一处。
        /// </summary>
        private static readonly Regex 调用名正则 = new(
            @"^[\p{L}_][\p{L}\p{N}_]*(?:\[[^\[\]]*\])?(?:\.[\p{L}_][\p{L}\p{N}_]*(?:\[[^\[\]]*\])?)*$", RegexOptions.Compiled);
        private static readonly Regex 寄存器形正则 = new(@"^[rR]\d+$", RegexOptions.Compiled); // R16 之类“看着像寄存器”的名字也禁用
        // 提及正则按 寄存器C 生成(R0..R15),范围不再写死
        private static readonly Regex 寄存器提及正则 = new(
            "(?<![A-Za-z0-9_])(" + string.Join("|", Enumerable.Range(0, 寄存器C.数量).Select(寄存器C.名称)) + ")(?![0-9])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        #endregion
    }
}
