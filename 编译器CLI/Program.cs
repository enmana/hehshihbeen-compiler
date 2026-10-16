using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// hbcc —— 中文汇编/中间层语言的命令行编译器(引擎与 UI 解耦的产物)。
    /// 用法:
    ///   hbcc [选项] 文件1.hb 文件2.hb ...
    ///   -o/--out 路径    写出二进制(前 4 字节 HB 头 + 机器码);缺省则把机器码 hex 打到 stdout
    ///   --hex            强制把机器码 hex 打到 stdout
    ///   --labels         打印标签表(标签名 -> 字节地址)
    ///   --asm            打印各文件中间层展开后的纯汇编文本
    ///   --run            编译后立即运行(输入 EOF),执行完打印寄存器
    ///   --跑法 原生      与 --run 一起:**跑原生码** —— 机器码转成 C、本机 C 编译器编成原生 exe 再跑
    ///                    (R4-9:比虚拟机快约 9~30 倍;缺省仍是虚拟机,`--跑法 虚拟机` 可以明写回来。
    ///                    要本机有 C 编译器,只认 C 后端那条路 —— 见 `--方式 C` / `C工具链C`)
    ///   --冻结秒 N       与 --run 一起:把 `当前时间()` 冻在 N 这个 Unix 秒上(两条跑法都认;
    ///                    两条跑法要逐字节对拍、或者想让输出可复现时用)
    ///   --跨进程         与 --run 一起:把"跑程序"那一步放到**子进程**里跑(引擎 / 宿主崩了不会带走这条命令)
    ///   --无头           与 --run 一起:图形原语的绘制命令逐条打在标准输出上(R1-6 批 1;hbcc 本来就是无头)
    ///   --事件脚本 路径   与 --run 一起:按这份脚本按帧喂键盘 / 鼠标 / 关窗事件(R1-6 批 2;不开窗的确定性测试;
    ///                    与 --跨进程 一起用也认:脚本随装载帧交给子进程,由那一侧按帧喂)
    ///   --in 路径         仅与 --run 搭配:把文件字节作为虚拟机 IN 输入流(EOF 结束)
    ///   --内存 [短]       与 --run 搭配:跑完之后打一张**内存缩略图**(每段的占用 / 峰值 / 热点 / 头一页原始字;
    ///                    写 `短` 就只出段表与热点。口径见 引擎\内存缩略C.cs,与 `内存布局C` 的段表同一份)
    ///   -p/--project 路径 按工程文件(.hbp)编译:文件表/顺序/启动文件都从工程文件读;没给 --in 时用第一个资源当输入
    ///   --help-text [页]  打印速查手册(与界面"帮助"菜单同一份文本);页 = 上手/指令/中间层/关于,省略=全部
    ///   --selftest       运行引擎综合自检;`--更新断言清单` 再把断言清单写进 工具\自检断言清单.txt
    ///   --生成帮助       把帮助站点重新生成到 帮助\(首页 + 一个话题一页,零依赖静态网页)
    ///   -h/--help        用法说明
    /// 多文件编译:各文件用户标签可互相引用(跨文件 调用/跳转 标签);内部流程展开标签自动隔离。
    /// 退出码:0 成功;1 编译错误 / 运行期错误 / **跑完但没通过**(断言失败、或被运行期守卫拦下 —— 错误槽里有号);2 用法错误。
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// <summary>
        /// **跑法的缺省那一档:进程内 C# 虚拟机**(`--run` 不带 `--跑法 原生` 走这儿)。
        ///
        /// <para>R4-9 之前这一段就写在 `Main` 的 `if (要运行)` 里(**一行口径没改**地搬出来)——
        /// 因为从这一批起 `--run` 有两条跑法(`跑原生程序` 是另一条),两条都得站在同一个位置上,
        /// 所以把"怎么跑"与"跑完怎么报"分开;报的那一半两条路各印一份,口径逐字对齐。</para>
        ///
        /// <para>返回 = **跑完但没通过**(错误槽里有号:断言失败 / 被运行期守卫拦下)。</para>
        ///
        /// <para>`冻结秒` 非空时把 `当前时间()` 冻在那一秒(见 `宿主能力C.取当前秒`):
        /// 两条跑法要逐字节对拍、或者想让输出可复现,都得先把钟停住 ——
        /// 原生那一侧的同一条开关是 C 的 `--hb-now`。</para>
        ///
        /// <para>⚠ **`当前毫秒()` 也一起冻**(第452批补的):它原来是"运行时长"那一类单调钟,
        /// `--冻结秒` 管不着 ⇒ **手跑对拍根本不可复现** —— 只要程序里有个 `文件.临时文件(…)`
        /// (名字掺了 `当前毫秒()`),同一份程序跑两遍,池里那几个字节就不一样;两条腿各跑一次更不一样。
        /// 实测就是这么撞上的:两腿的寄存器 / 输出 / 执行条数**全同**,只有池区差十几个字节
        /// (差的正是那个名字),看着就像"内存差一个字"那种缺陷。自检那条对拍早就把毫秒冻住了
        /// (`综合自检C.C后端全量对拍.cs` 的 `冻结毫秒` + C 侧 `--hb-now-ms`),缺的只是**命令行这一手**。
        /// 冻法:`秒 × 1000`(取低 32 位;同一个 `--冻结秒` 永远给同一个毫秒 ⇒ 可复现,且两条腿一致)。</para>
        /// </summary>
        private static uint 冻结毫秒(long 秒) => (uint)((秒 * 1000L) & 0xFFFFFFFFL);

        /// <summary> 见 <see cref="冻结毫秒(long)"/>:两侧同一处口径,虚拟机这一侧换的是宿主那两个口子 </summary>
        private static void 冻住钟(long 秒)
        {
            宿主能力C.取当前秒 = () => 秒;
            宿主能力C.取当前毫秒 = () => 冻结毫秒(秒);
        }

        private static bool 跑虚拟机程序(汇编调试结果C 编译结果, byte[] 码, int 内存大小, bool 跨进程,
            bool 要无头, string? 事件脚本路径, (int 步数, int 起, int 止)? 要跟踪,
            (int 起始, int 长度)? 要转储, bool 要内存图, bool 内存图原始, string? 输入路径,
            long? 冻结秒, string? 工程路径, string? 导出模型路径 = null,
            IReadOnlyDictionary<string, int>? 标签表 = null)
        {
            if (冻结秒 is { } 冻) 冻住钟(冻);
            跨进程虚拟机C? 跨进程机 = null;         // H39-3:事件脚本也要交给它(随装载帧过线)
            跑程序通道C? 跨进程通道 = null;
            bool 跑完没通过 = false;      // 跑完但错误槽里有号(断言失败 / 被运行期守卫拦下)→ 退出码非 0(D2 拍板)
            虚拟机C vm;
            if (跨进程)
            {
                // H39-1:跑程序那一段整个搬进子进程 —— 虚拟机 / 宿主能力 / 图形宿主都在那边,
                // 这一侧只留镜像 + 通道;图形命令回放到这一侧的**无头呈现口**(命令行的窗口就是这几行文字)。
                跨进程通道 = 跑程序通道C.从当前进程();
                跨进程通道.呈现口 = new 无头呈现C(Console.Out);
                跨进程机 = new 跨进程虚拟机C(跨进程通道);
                // 运行期报错要带行号的那张"地址 → 源码位置"表**不在这儿设**:装载扩展
                // (`汇编器C.cs` 的 `装载扩展.备好源行表`)在装载那一刻按调试文件表自动压一张带过去 ——
                // 界面与命令行两条路都吃同一个口径,谁也不用记得补这一句。
                vm = 跨进程机;
                Console.Error.WriteLine("  (跨进程:跑程序那一段在子进程里(`hbcc " + 跑程序协议C.服务参数 +
                    "`),引擎 / 宿主崩了不会带走这个进程)");
            }
            else
            {
                // R1-6 批 1:hbcc 没有窗口 ⇒ 图形原语走**无头呈现**(命令逐条打在标准输出上)。
                // `--无头` 是把这个口径显式写出来;没写也照走(不然"打开了窗口"这件事在命令行里一个字都看不到)。
                图形宿主C.挂上(new 无头呈现C(Console.Out));
                if (要无头) Console.Error.WriteLine("  (无头呈现:hbcc 没有窗口,图形命令逐条打在标准输出上)");
                vm = new 虚拟机C();
            }
            // R1-6 批 2:按帧喂固定输入 —— 脚本写错(帧数没写 / 键名认不出 / 帧号超了)一律带行号报出来,
            // 别让它变成"程序静悄悄地没有事件"。脚本里的帧号口径与自检那条路**同一个解析器**。
            // H39-3:跨进程那条腿把**脚本原文**随装载帧带过去,由子进程那一侧的图形宿主挂上
            // (帧 0 投放 / 跑满帧数投关窗请求 / 帧数宽限,三处口径就只有一份实现)。
            if (事件脚本路径 != null)
            {
                var 脚本 = 事件脚本C.读文件(事件脚本路径);
                if (跨进程机 != null) 跨进程机.事件脚本文本 = 脚本.原文;
                else 图形宿主C.挂事件脚本(脚本);
                Console.Error.WriteLine($"  (事件脚本:{脚本.一句话()};跑满 {脚本.帧数} 帧后投关窗请求" +
                    (跨进程机 != null ? ";脚本在子进程那一侧按帧喂" : "") + ")");
            }
            vm.设内存大小(内存大小);
            vm.宿主调用 = 宿主能力C.处理;         // P28:读文件 / 写文件 / 当前时间
            // D1:可执行文件白名单来自工程文件(-p);没给工程 = 没有"起进程"这个能力
            宿主能力C.设可执行白名单(取工程可执行白名单(工程路径));
            // D5:网址白名单同样来自工程文件(-p);没给工程 / 没声明 = 没有"连网址"这个能力
            宿主能力C.设网址白名单(取工程网址白名单(工程路径));
            // 运行期错误(除零/栈溢出)带行号:拿这次编译顺便建好的调试表(和单步调试同一份数据)
            // H65:同一个地址也挂一份"给人看的位置串" —— 多文件程序报 `工具.hb 第 3 行`
            // (单文件程序照旧只报 `第 3 行`;跨进程那条腿由子进程按同一套判据给,两边逐字一致)
            vm.取源位置 = 地址 => 执行行映射C.查位置(编译结果.调试文件表, 地址);
            vm.取源行号 = 地址 => 执行行映射C.查源行(编译结果.调试文件表, 地址);
            byte[]? 输入字节 = null;
            if (输入路径 != null)
            {
                if (!File.Exists(输入路径))
                {
                    Console.Error.WriteLine($"错误: 找不到输入文件 {输入路径}");
                    return true;
                }
                输入字节 = File.ReadAllBytes(输入路径);
            }
            else
            {
                // 没给 --in 就用**标准输入**:可以直接 `hbcc 程序.hb --run < 输入.txt`,也能在终端里手打
                输入字节 = Console.IsInputRedirected ? 读全部标准输入() : null;
            }
            if (输入字节 != null)
            {
                var 队列 = new Queue<uint?>(输入字节.Select(b => (uint?)b));
                vm.输入源 = () => 队列.Count > 0 ? 队列.Dequeue() : null;
            }
            else
            {
                vm.输入源 = () => Console.In.Peek() < 0 ? null : (uint)Console.In.Read();   // 终端里真读(读到 EOF 给 null)
            }
            var 解码 = new 文字输出C(Console.Write);
            vm.输出目标 = b => 解码.写(b);
            if (要跟踪 is { } 跟踪)
            {
                // 逐条单步:打印 PC、反汇编、本步改动过的寄存器(排查自举程序最直接)
                vm.装载(编译结果);
                var 旧 = new uint[vm.寄存器.Length];
                int 步 = 0;
                while (!vm.停机 && 步 < 跟踪.步数)
                {
                    Array.Copy(vm.寄存器, 旧, 旧.Length);
                    uint pc前 = vm.程序计数器;
                    string 指令 = 反汇编C.反汇编一条(码, pc前, out _) ?? "?";
                    vm.单步();
                    步++;
                    bool 在范围 = (跟踪.起 < 0 || (pc前 >= 跟踪.起 && pc前 <= 跟踪.止));
                    if (!在范围) continue;
                    var 变化 = new StringBuilder();
                    for (int r = 0; r < vm.寄存器.Length; r++)
                        if (旧[r] != vm.寄存器[r]) 变化.Append($" R{r}:{旧[r]:X}→{vm.寄存器[r]:X}");
                    Console.WriteLine($"  #{步,-6} 0x{pc前:X4}  {指令,-28}{变化}");
                }
                Console.WriteLine($"  (单步 {步} 次,停机={vm.停机} PC=0x{vm.程序计数器:X})");
            }
            else
            {
                // 顺手数一下"跑了多少条指令"(性能对比要靠它:优化到底省了几条,看这个数最实在)
                long 条数 = 0;
                var 秒表 = System.Diagnostics.Stopwatch.StartNew();
                if (vm is 跨进程虚拟机C 跨机)
                {
                    // 跨进程这条腿**一趟往返跑完整条程序**:不是每条指令过一趟管道
                    // (那样 100 万条指令要几十秒;子进程里那台机器自己会跑、会自己查中断闸门)
                    跨机.运行(编译结果);
                    // 口径与进程内那圈对齐:进程内数的是"`单步()` 返回 true 的次数",
                    // 最后那条 `停止`(它让 `单步()` 返回 false)不算 —— 子进程报的是**已执行条数**(算它)
                    条数 = 跨机.已执行条数 > 0 ? 跨机.已执行条数 - 1 : 0;
                }
                else
                {
                    vm.装载(编译结果);
                    while (vm.单步()) 条数++;
                }
                秒表.Stop();
                Console.WriteLine($"  (共执行 {条数} 条指令,{秒表.Elapsed.TotalMilliseconds:F1} ms)");
            }
            解码.收尾();                       // 最后半个 UTF-8 字符也要收尾,别丢弃
            Console.WriteLine();
            // 跨进程这条腿:前端手里是**镜像内存** —— 要读它(查错误槽 / --dump)之前先同步一次
            // (`运行` 那条路已经顺手同步过;`--debug-steps` 那种一条一条单步的路就得在这儿补一次)
            if (vm is 跨进程虚拟机C) vm.同步镜像();
            // 运行期报错(列表满了 / 池用完 / 断言失败…):程序停下来了(或者跑完了),但错误槽里有号 —— 给出明确错误。
            // **同时把这次运行算作"没通过"**:退出码非 0。这就是中间层那句"让命令行看得出这次测试没过"的落地,
            // 也和独立 exe(HB宿主)一个答案 —— 以前 CLI 在这里给 0、exe 给 1,同一份程序两个结论(D2 拍板改掉)。
            // ⚠ **这一段的打印位置在 R4-9 挪过**:以前它排在 `PC` 那一行**前面**,而现在原生那条路是
            // "跑完 → 读回状态 → 查错误槽"⇒ 它只能在寄存器之后报。两条跑法要能逐行对拍,就得取**同一个位置**
            // (取原生那一边能到的位置)。判定一个字没动,只有"印在哪一行"变了。
            string? 报错说明 = 编译结果.查运行期报错(vm.内存);
            Console.WriteLine($"  PC = 0x{vm.程序计数器:X}  停机={vm.停机}  SP = 0x{vm.栈指针:X}");
            for (int r = 0; r < vm.寄存器.Length; r++)
                Console.WriteLine($"  R{r} = 0x{vm.寄存器[r]:X8} ({vm.寄存器[r]})");
            if (报错说明 != null)
            {
                Console.WriteLine($"⚠ 运行停下: {报错说明}");
                跑完没通过 = true;
            }

            // --内存:跑完之后那张"内存缩略图"(每段填了多少 / 峰值到哪 / 哪几格最密 / 头一页原样)
            if (要内存图)
            {
                Console.WriteLine();
                Console.WriteLine("── 内存缩略图 ──");
                Console.Write(内存缩略C.生成(编译结果, vm.内存, 内存图原始));
            }

            // --dump 起始[,长度]:把某段内存打出来(排查自举/自修改代码时最有用)
            if (要转储 != null)
            {
                int 起始 = 要转储.Value.起始, 长度 = 要转储.Value.长度;
                Console.WriteLine($"── 内存 0x{起始:X}..0x{起始 + 长度 - 1:X} ──");
                for (int 行 = 0; 行 < 长度; 行 += 16)
                {
                    int 本行 = Math.Min(16, 长度 - 行);
                    var sb = new StringBuilder($"  0x{起始 + 行:X4}: ");
                    for (int i = 0; i < 本行; i++) sb.Append($"{vm.内存[起始 + 行 + i]:X2} ");
                    sb.Append("  ");
                    for (int i = 0; i < 本行; i++)
                    {
                        byte b = vm.内存[起始 + 行 + i];
                        sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                    }
                    Console.WriteLine(sb.ToString());
                }
            }
            跨进程通道?.Dispose();           // H39-1:体面收掉子进程(发"结束" → 等它自己退 → 超时才杀)

            // ── R4-10:`--导出模型 路径` —— 把 `三维网格.hb` 那五个数组导成纯 ASCII 文本 ──
            // 在这儿(跑完、内存还是热的)做:导出的是**跑完之后**的网格。
            // 数组地址现查:名字表里那五个全局变量的 `槽` → 标签表。查不齐就**如实说不支持**,
            // 不静默写一个空文件(那比不导更坏:外面会以为"模型是空的")。
            if (导出模型路径 is { Length: > 0 })
            {
                if (标签表 == null)
                {
                    Console.Error.WriteLine("错误: `--导出模型` 需要标签表(内部问题,请回报)。");
                }
                else
                {
                    int 顶点x址 = 三维导出C.找数组槽(编译结果.名字表, 标签表, "顶点x");
                    int 顶点y址 = 三维导出C.找数组槽(编译结果.名字表, 标签表, "顶点y");
                    int 顶点z址 = 三维导出C.找数组槽(编译结果.名字表, 标签表, "顶点z");
                    int 边起点址 = 三维导出C.找数组槽(编译结果.名字表, 标签表, "边起点");
                    int 边终点址 = 三维导出C.找数组槽(编译结果.名字表, 标签表, "边终点");
                    int 顶点数址 = 三维导出C.找数组槽(编译结果.名字表, 标签表, "顶点数");
                    int 边数址 = 三维导出C.找数组槽(编译结果.名字表, 标签表, "边数");
                    var 导出 = 三维导出C.写文件(vm.内存, 导出模型路径,
                        顶点x址, 顶点y址, 顶点z址, 边起点址, 边终点址, 顶点数址, 边数址);
                    if (导出.成功) Console.Error.WriteLine($"  (导出模型:{导出.说明})");
                    else Console.Error.WriteLine($"  (导出模型没做成:{导出.说明})");
                }
            }
            return 跑完没通过;
        }
        /// R4-9:`--run --跑法 原生` —— **跑原生码**(不走虚拟机)。
        ///
        /// <para>**这条路要解决什么**:以前 `--run` 恒等于 `while (vm.单步())`(逐条解释),
        /// 实测稳态 **184 ns/条指令**;同一份机器码编成原生 exe 是 **1.6 ns/条**(贴着硬件下限),
        /// 差两个数量级。所以"5 万顶点一帧 6.4 ms ≈ 150 fps"那个结论**只有在这条路上才成立** ——
        /// 在那之前它是拿虚拟机量出来的 0.7 秒。</para>
        ///
        /// <para>**这一条不是"再实现一遍执行器"**:走的是已经在对拍里钉住的那条 C 后端 ——
        /// `C后端C.转译`(机器码逐条静态展开成 C)+ `C工具链C.编成exe`(本机 clang/gcc/cl),
        /// 与 `--方式 C` / `--exe` **同一个后端、同一份语义**(那份语义由 `C后端全量对拍` 逐工程钉着:
        /// 寄存器 / 整块内存 / 输出逐字节一致)。这一层只做**接线与呈现**:编 → 跑 → 把
        /// `--hb-dump` 那份整机状态读回来 → 照虚拟机那条路**逐字**打印同一份报告。</para>
        ///
        /// <para>**为什么状态转储走文件**:原生 exe 是个真进程,跑完之后它的寄存器 / 内存不在我们手里。
        /// 唯一的出口是 R3-6-4 批 2 就有的 `--hb-dump &lt;路径&gt;`(`运行时.c` 的 `hb_dump_state`,
        /// 格式与 `虚拟机C.导出状态` 一字不差、由 `C状态C` 解析)。多花一次落盘的代价,
        /// 换"PC / 寄存器 / --dump / --内存 全照旧"。</para>
        ///
        /// <para>**几条如实说清的差别**(都是"两套栈模型 / 两个进程"的必然结果,不是缺陷):
        /// ① `SP` 不适用 —— C 后端把 `压栈/弹栈` 编成了 C 自己的调用栈,HB 内存末尾那 4 个字节没动过
        /// (与 `C状态C` 里"两处有意不比"的 SP 同一条理由);② `PC` 按 **C 侧口径**印(它停在
        /// 最后那条指令上,虚拟机是执行完 +4),同一份程序两者差一条指令、正好 4 字节;
        /// ③ 耗时是**进程墙钟**(含启动与初始化)⇒ 短程序偏低不准,长程序才是那条 1.6 ns/条。
        /// 所以报告里把"编译一次"的耗时单独印一行:那一笔是一次性的,别算进程序本身。</para>
        ///
        /// <para>**不认识的那几档**:`--跨进程`(本身就是个子进程了)与 `--debug-asm` / `--debug-steps`
        /// 都**到不了这儿** —— 前者由 `--跑法` 那一支上头的判断挡住,后者走的是 `Main` 里更早的那条
        /// 单步分支(所以"要单步"永远落在虚拟机上,这条路是性能档)。</para>
        /// </summary>
        private static bool 跑原生程序(汇编调试结果C 编译结果, byte[] 码, int 内存大小, long? 冻结秒,
            bool 要无头, string? 事件脚本路径,
            (int 起始, int 长度)? 要转储, bool 要内存图, bool 内存图原始, string? 输入路径)
        {
            Console.Error.WriteLine("  (跑法:原生 —— 机器码转 C 源码,交本机 C 编译器编成原生 exe 再跑;不是虚拟机)");

            // ---- 一、转译 + 编成原生 exe(与 `--exe --方式 C` 同一条路,同一条口径) ----
            C后端结果C 转译;
            try
            {
                转译 = C后端C.转译(编译结果, 内存大小);
            }
            catch (C后端错误C ex)
            {
                Console.Error.WriteLine($"错误: 原生这条跑法要 C 后端,而它没能转译:{ex.Message}");
                Console.Error.WriteLine("  要按虚拟机跑就把 `--跑法 原生` 去掉(那条路不需要 C 编译器)。");
                return true;
            }

            string 暂存 = Path.Combine(临时区C.基目录(), "hbcc_原生跑", Environment.ProcessId.ToString());
            Directory.CreateDirectory(暂存);
            string c源 = Path.Combine(暂存, "跑.c");
            string 程序exe = Path.Combine(暂存, "跑.exe");
            string 状态文件 = Path.Combine(暂存, "跑状态.txt");
            // 上一次留下的状态转储先清掉:这一趟要读的是**这一趟**的产物
            // (读不到就拿不到寄存器 / 内存 —— 留着旧的那份比读不到更坏:会印上一次的机器状态)。
            try { if (File.Exists(状态文件)) File.Delete(状态文件); } catch { }

            // ── **同一份程序不重编**(R4-9) ──
            // 为什么非要这一条:`-O2` 编一份 C 是**秒级**的(实测这一档约 1.1 秒),而"改一次跑一次"
            // 正是这条命令的常规用法 —— 不缓存就等于每跑一次白等一秒多,比程序本身还长。
            // 判据 = **C 源码的 SHA-256**:机器码 / 内存大小 / 转译器版本只要变了,源码文本必然变
            // ⇒ 指纹必然变。所以命中时的语义与重编**逐字节等价**,不是"猜"出来的。
            string 指纹 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                Encoding.UTF8.GetBytes(转译.源码)))[..16];
            string 缓存目录 = Path.Combine(临时区C.基目录(), "hbcc_原生跑_缓存");
            string 缓存exe = Path.Combine(缓存目录, 指纹 + ".exe");
            bool 命中 = false;
            try { 命中 = File.Exists(缓存exe) && new FileInfo(缓存exe).Length > 0; } catch { 命中 = false; }

            var 编 = System.Diagnostics.Stopwatch.StartNew();
            C编译结果C 编译;
            if (命中)
            {
                try
                {
                    File.Copy(缓存exe, 程序exe, overwrite: true);
                    编译 = new C编译结果C { 成功 = true, 编译器 = "缓存(这一份刚编过,不重编)" };
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"  (缓存那份拷不过来:{ex.Message};改成重编一遍)");
                    编译 = 重编并缓存(c源, 程序exe, 缓存目录, 缓存exe, 转译.源码);
                }
            }
            else 编译 = 重编并缓存(c源, 程序exe, 缓存目录, 缓存exe, 转译.源码);
            编.Stop();
            if (!编译.成功)
            {
                Console.Error.WriteLine("错误: 原生这条跑法要本机的 C 编译器,而这次没能编出 exe。");
                if (编译.找不到原因.Length > 0) Console.Error.WriteLine($"  {编译.找不到原因}");
                if (编译.日志.Length > 0) Console.Error.WriteLine($"  C 编译器说:{编译.日志}");
                Console.Error.WriteLine($"  生成的 C 源码留在:{c源}");
                Console.Error.WriteLine("  要按虚拟机跑就把 `--跑法 原生` 去掉(那条路什么都不用装)。");
                return true;
            }

            // ---- 二、跑那个 exe(输入、事件脚本、冻结秒都从这儿过线) ----
            // 事件脚本:*原文*交给 exe 自己按同一套解析器喂(`运行时.c` 的 `--hb-事件脚本`),
            // 与 `--跨进程` 把脚本原文交给子进程是同一条理由 —— 解析器只有一份实现。
            string? 脚本文件 = null;
            if (事件脚本路径 != null)
            {
                var 脚本 = 事件脚本C.读文件(事件脚本路径);      // 先在**这一侧**解析一遍:脚本写错要当场报出来
                脚本文件 = Path.Combine(暂存, "事件脚本.txt");
                File.WriteAllText(脚本文件, 脚本.原文, new UTF8Encoding(false));
                Console.Error.WriteLine($"  (事件脚本:{脚本.一句话()};跑满 {脚本.帧数} 帧后投关窗请求;按帧喂的事在原生 exe 那一侧)");
            }

            var 起 = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Path.GetFullPath(程序exe),
                WorkingDirectory = Environment.CurrentDirectory,   // 相对路径按"用户站的那儿"算,与虚拟机那条路一致
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                Arguments =
                    $"--hb-dump \"{状态文件}\""
                    + (冻结秒 is { } 秒 ? $" --hb-now {秒} --hb-now-ms {冻结毫秒(秒)}" : "")
                    + (脚本文件 != null ? $" --hb-事件脚本 \"{脚本文件}\"" : ""),
            };

            var 秒表 = System.Diagnostics.Stopwatch.StartNew();
            var 输出 = new MemoryStream();
            string 标准错;
            int 退出码;
            try
            {
                using var 进程 = System.Diagnostics.Process.Start(起);
                if (进程 == null)
                {
                    Console.Error.WriteLine($"错误: 起不了原生程序 {起.FileName}");
                    return true;
                }
                // 输入:照命令行那两条口径(`--in` 优先,不然标准输入重定向过来什么就喂什么)。
                // 不给就是空输入 → 程序读到 EOF —— 与虚拟机那条路的"输入 EOF"同一个意思。
                byte[] 喂 = 输入路径 != null ? File.ReadAllBytes(输入路径) : [];
                try
                {
                    if (喂.Length > 0) 进程.StandardInput.BaseStream.Write(喂, 0, 喂.Length);
                    进程.StandardInput.BaseStream.Flush();
                }
                catch { /* 程序不看输入就关掉了那一头 —— 不算错 */ }
                进程.StandardInput.Close();

                // 输出边读边写、与错误流**同时**读:两头都重定向时先等一头会把另一头的管道填满卡死
                var 读错 = Task.Run(() => 进程.StandardError.ReadToEnd());
                var 拷出 = Task.Run(() =>
                {
                    var 源 = 进程.StandardOutput.BaseStream;
                    var 块 = new byte[8192];
                    int 读;
                    while ((读 = 源.Read(块, 0, 块.Length)) > 0) 输出.Write(块, 0, 读);
                });
                if (!进程.WaitForExit(600_000))
                {
                    try { 进程.Kill(entireProcessTree: true); } catch { }
                    Console.Error.WriteLine("错误: 原生程序跑了 10 分钟还没停,已经掐掉(死循环?)。");
                    return true;
                }
                拷出.Wait();
                标准错 = 读错.Result;
                退出码 = 进程.ExitCode;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"错误: 原生程序没跑起来({起.FileName}):{ex.Message}");
                return true;
            }
            秒表.Stop();

            // 程序输出:与虚拟机那条路**同一个解码器**(`文字输出C`)—— 汉字跨块也不会被切坏,
            // 而且最后那半个 UTF-8 字符要收尾(不收就等于丢掉一个字的尾巴)。
            var 解码 = new 文字输出C(Console.Write);
            var 输出字节 = 输出.ToArray();
            foreach (byte b in 输出字节) 解码.写(b);
            解码.收尾();
            // ⚠ 这儿**不补空行**:虚拟机那条路是"程序输出 → (共执行…) → 空行 → PC…",
            // 那个空行在下面读回状态之后再补(两条跑法要逐行对得上,分隔行的位置也得一样)。

            // ---- 三、把整机状态读回来(这一份就是"跑完之后机器里到底是什么") ----
            var 快照 = C状态C.读原生状态(状态文件);
            if (快照 == null)
            {
                // 状态落不下来 = 程序没能跑到停机(或者半路被系统掐了)。如实说,别猜。
                Console.Error.WriteLine($"⚠ 原生程序没落下状态转储({状态文件})—— 它没跑到停机那一步。");
                if (标准错.Trim().Length > 0) Console.Error.WriteLine($"  原生程序的标准错误:{标准错.Trim()}");
                Console.Error.WriteLine($"  退出码 {退出码};耗时 {秒表.Elapsed.TotalMilliseconds:F1} ms。");
                return true;
            }

            // 口径与虚拟机那条路对齐:虚拟机数的是"`单步()` 返回 true 的次数",最后那条 `停止`
            // (它让 `单步()` 返回 false)不算;而 C 侧 `hb_steps` 是**已执行条数**(算它)⇒ 减一。
            long 条数 = 快照.执行条数 > 0 ? 快照.执行条数 - 1 : 0;
            Console.WriteLine($"  (共执行 {条数} 条指令,{秒表.Elapsed.TotalMilliseconds:F1} ms —— 含进程启动与初始化)");
            Console.WriteLine();                       // 分隔行:与虚拟机那条路取同一个位置

            bool 跑完没通过 = false;
            string? 报错说明 = 编译结果.查运行期报错(快照.内存);
            Console.WriteLine($"  PC = 0x{快照.PC:X}  停机=True  SP = 不适用(原生这条腿的栈在 C 自己的调用栈上," +
                "`压栈`/`弹栈` 没动 HB 内存末尾那 4 个字节)");
            for (int r = 0; r < 快照.寄存器.Count && r < 16; r++)
                Console.WriteLine($"  R{r} = 0x{快照.寄存器[r]:X8} ({快照.寄存器[r]})");
            if (报错说明 != null)
            {
                Console.WriteLine($"⚠ 运行停下: {报错说明}");    // 位置与虚拟机那条路**逐行对齐**(见那边的说明)
                跑完没通过 = true;
            }
            // 跑完之后还打一句"这次编原生花掉多少":那一笔是**一次性**的,
            // 别把它算进程序耗时里(短程序上它比程序本身还长,不写清就会被读数骗)。
            Console.WriteLine($"  (原生:转译 {转译.指令条数} 条指令 / 跳转 {转译.跳转条数} 条其中间接 {转译.间接条数} 条;" +
                $"这次编译 C + 链接花 {编.Elapsed.TotalMilliseconds:F0} ms —— 一次性的,不计入上面的执行耗时。" +
                $"原生那条腿的稳态成本约 1.6 ns/指令,虚拟机约 184 ns/指令)");
            if (要无头) Console.Error.WriteLine("  (无头呈现:图形命令逐条打在标准输出上,与虚拟机那条路同一份格式)");
            if (标准错.Trim().Length > 0) Console.Error.Write(标准错);   // C 侧那句"C后端分派"之类的自报
            // --内存:与虚拟机那条路**同一个口径**(同一份段表、同一个缩略图算法),
            // 只是内存来自状态转储、编译元数据仍然来自这一侧的编译结果。
            if (要内存图)
            {
                Console.WriteLine();
                Console.WriteLine("── 内存缩略图 ──");
                Console.Write(内存缩略C.生成(编译结果, 快照.内存, 内存图原始));
            }

            // --dump 起始[,长度]:同一套十六进制 + 字符并排的排法
            if (要转储 != null)
            {
                int 起始 = 要转储.Value.起始, 长度 = 要转储.Value.长度;
                if (起始 < 0 || 起始 >= 快照.内存.Length)
                {
                    Console.Error.WriteLine($"  (--dump:0x{起始:X} 落在这次运行内存({快照.内存.Length} 字节)之外,没东西可打)");
                }
                else
                {
                    长度 = Math.Min(长度, 快照.内存.Length - 起始);
                    Console.WriteLine($"── 内存 0x{起始:X}..{起始 + 长度 - 1:X} ──");
                    for (int 行 = 0; 行 < 长度; 行 += 16)
                    {
                        int 本行 = Math.Min(16, 长度 - 行);
                        var sb = new StringBuilder($"  0x{起始 + 行:X4}: ");
                        for (int i = 0; i < 本行; i++) sb.Append($"{快照.内存[起始 + 行 + i]:X2} ");
                        sb.Append("  ");
                        for (int i = 0; i < 本行; i++)
                        {
                            byte b = 快照.内存[起始 + 行 + i];
                            sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                        }
                        Console.WriteLine(sb.ToString());
                    }
                }
            }

            return 跑完没通过;
        }

        /// <summary>
        /// `--跑法 原生` 里"真去编一份 + 顺手放进缓存"那一段。
        ///
        /// <para>单独抽出来的理由只有一个:**失败也要如实留下 C 源码**(调用方要把它印给用户看),
        /// 所以"写源码 → 叫 C 编译器 → 成功了才拷进缓存"这三步必须挨在一起、只写一遍。</para>
        ///
        /// <para>缓存拷不进去**不算失败**:下一趟重编一遍就是了(开发机上常见:临时区被清过)。</para>
        /// </summary>
        private static C编译结果C 重编并缓存(string c源, string 程序exe, string 缓存目录, string 缓存exe, string 源码)
        {
            try
            {
                File.WriteAllText(c源, 源码, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                // 写不出源码 = 这条跑法没法走。用一条"失败"的结果如实往上报(报错文本走 `日志`)。
                return new C编译结果C { 成功 = false, 日志 = $"写 C 源码失败({c源}):{ex.Message}" };
            }
            var 果 = C工具链C.编成exe(c源, 程序exe);
            if (果.成功)
            {
                try
                {
                    Directory.CreateDirectory(缓存目录);
                    File.Copy(程序exe, 缓存exe, overwrite: true);
                }
                catch { /* 缓存是快路,不是判据 */ }
            }
            return 果;
        }

        /// <summary> 十进制 / 0x 十六进制 都认(给 --dump 用) </summary>
        private static bool TryParseNumber(string 文本, out int 值)
        {
            文本 = 文本.Trim();
            if (文本.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(文本[2..], System.Globalization.NumberStyles.HexNumber, null, out 值);
            return int.TryParse(文本, out 值);
        }

        public static int Main(string[] args)
        {
            // H39-1:**跑程序服务档** —— 前端(IDE / `hbcc --跨进程`)用标准输入输出管道把命令送进来,
            // 这一档里**标准输出就是二进制协议通道**(一个字节的提示都不许写),所以它排在设编码之前;
            // 也排在用法解析之前(服务模式不认别的开关,更不打印用法)。
            // 为什么 hbcc 也要有这一档:自检要能"就地再起一份自己"当子进程,免得自检依赖 `HB宿主` 先编出来。
            if (args.Contains(跑程序协议C.服务参数))
                return new 跑程序后端C(Console.OpenStandardInput(), Console.OpenStandardOutput()).跑();

            Console.OutputEncoding = Encoding.UTF8;
            var 文件表 = new List<string>();
            string? 输出路径 = null;
            string? 输入路径 = null;
            bool 要hex = false, 要标签 = false, 要asm = false, 要运行 = false, 要自检 = false, 要帮助文本 = false;
            string? 导出模型路径 = null;   // --导出模型:R4-10,跑完把网格导成纯 ASCII 文本
            bool 要无头 = false;   // --无头:R1-6 批 1,把图形原语的绘制命令逐条打出来(与 --run 的默认口径相同,见帮助《上手》第25节)
            bool 要内存图 = false;   // --内存:跑完之后打一张内存缩略图(每段填了多少 / 峰值 / 热点 / 头一页)
            bool 内存图原始 = true;  // --内存 短:只出段表 + 热点,不打每段头一页的原始字
            bool 跨进程 = false;   // --跨进程(H39-1):把"跑程序"那一步放到子进程里跑(见 H39 / `跨进程虚拟机C`)
            bool 跑原生 = false;   // --跑法 原生(R4-9):--run 那一步走 C 后端编出的原生 exe,而不是 C# 虚拟机
            long? 冻结秒 = null;   // --冻结秒:把 `当前时间()` 冻在某一秒(见 `宿主能力C.取当前秒` / C 侧 `--hb-now`)
            string? 事件脚本路径 = null;   // --事件脚本:R1-6 批 2,按帧喂固定输入(见 引擎\图形事件C.cs 的格式说明)
    bool 要标记 = false;   // --asm-marked:展开文本里带上 `// 来自第 N 行` 标记(对展开查错/编辑器展开用)
    int 片段起 = 0, 片段止 = 0;   // --片段 起-止:只展开这一段(编辑器"就地展开"的引擎入口)
    bool 切到英文 = false, 切到中文 = false;   // --中英/--en、--中文:整篇关键字与指令名换语言
            bool 要统计 = false, 要更新报告 = false;
            bool 要生成帮助 = false;          // --生成帮助:把帮助章节树重新生成到 帮助\index.html(C25-3)
            bool 要更新指令集 = false;        // --更新指令集:R3-6-1,把 ISA 真值源重新写进 工具\指令集真值源.txt
            bool 要清单 = false, 清单索引 = false;
            string? 清单词 = null;            // --清单 --查 <词>:查一条记录的支脉/种类/状态/递进与行区间
            string? 要找 = null;              // --查找 <内容>:P21 查找/替换(字面量)
            string? 换成 = null;              // --换成 <内容>:给了就是"替换清单"(默认只预览;--写回 才落盘)
            bool 查找区分大小写 = false, 查找整词 = false, 查找连注释一起 = false, 查找带行 = false, 查找写回 = false;
            bool 查找正则 = false;            // --正则(H5):"要找"按正则解释,换成里的 $1 展开分组
            var 查找只看 = new List<string>();
            bool 要更新断言清单 = false;
            int 乱源次数 = 0;                 // --乱源 N:随机乱源轰炸 N 例(批 7;夜巡用大批量)
            string? 导图文件 = null;          // --导图 <文件>:T20 起,命令行也能跑一遍思维导图那条链(判据 = 退出码)
            bool 优化 = true;
            bool 已在运行阶段 = false;      // 报错前缀用:编译错误 vs 运行错误
            string? 要exe路径 = null;       // --exe:生成独立 exe
            string 方式 = "宿主";           // --方式:独立 exe 走哪条路(宿主 = 贴机器码到 HB宿主;C = 原生 C 后端)
            string? 打包给的 = null;         // --打包:产物打成什么(R3-6-4 批 4;见 引擎\打包规格C.cs 的四条规格)。没给 = 跟 --方式 走
            string? 帮助页 = null;
            string? 工程路径 = null;
            (int 起始, int 长度)? 要转储 = null;
            (int 步数, int 起, int 止)? 要跟踪 = null;
            (bool 汇编, int 步数)? 要调试 = null;
            int? 要测速 = null;
            string? 要建工程 = null;
            var 要补分号 = new List<string>();     // --fix-semicolon:给老代码补 ';'(迁移用)
            bool 要排版 = false, 排版写回 = false, 排版制表符 = false;   // --排版 / --写回 / --制表符
            int? 排版宽度 = null;                                        // --缩进 N
            bool 要片段库 = false;                                       // --片段库:列出代码片段骨架
            bool 要快捷键表 = false;                                     // --快捷键:列出快捷键表
            (string 旧, string 新)? 要改名 = null;                        // --改名文件 旧 新:改名 + 更新 .hbp 与 包含

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "-h" or "--help":
                        打印用法();
                        return 0;
                    case "--selftest": 要自检 = true; break;
                    case "--更新断言清单": 要自检 = true; 要更新断言清单 = true; break;
                    case "--乱源":
                        if (++i >= args.Length || !int.TryParse(args[i], out 乱源次数) || 乱源次数 <= 0)
                            return 用法错误("--乱源 后面要跟一个正整数(随机乱源轰炸的例数,例如 --乱源 5000)");
                        break;
                    case "--导图":
                        if (++i >= args.Length || args[i].StartsWith('-'))
                            return 用法错误("--导图 后面要跟一份 .hb 文件的路径(跑一遍思维导图那条链:扫结构 → 声明线 → 分区)");
                        导图文件 = args[i];
                        break;
                    case "--stats": 要统计 = true; break;
                    case "--更新报告": 要统计 = true; 要更新报告 = true; break;
                    case "--生成帮助": 要生成帮助 = true; break;
                    case "--更新指令集": 要更新指令集 = true; break;
                    case "--清单": 要清单 = true; break;
                    case "--索引": 清单索引 = true; break;
                    case "--查":
                        if (++i >= args.Length) return 用法错误("--查 后面要跟一个词(节编号 / 支脉 A / 支脉号 A9 / 批号 / 种类 / 状态 / 关键字)");
                        要清单 = true; 清单词 = args[i]; break;
                    case "-p" or "--project":
                        if (++i >= args.Length) return 用法错误("-p/--project 后需要工程文件路径(.hbp)或工程目录");
                        工程路径 = args[i];
                        break;
                    // ---- P21 查找 / 替换(第118批):只算清单 + 打印预览;加 --写回 才落盘 ----
                    case "--查找":
                        if (++i >= args.Length) return 用法错误("--查找 后面要跟要找的内容(默认按字面量找;要按正则加 --正则)");
                        要找 = args[i]; break;
                    case "--换成":
                        if (++i >= args.Length) return 用法错误("--换成 后面要跟替换成的内容(可以是空串,写成 --换成 \"\";--正则 时里面可以写 $1)");
                        换成 = args[i]; break;
                    case "--区分大小写": 查找区分大小写 = true; break;
                    case "--整词": 查找整词 = true; break;
                    case "--正则": 查找正则 = true; break;
                    case "--连注释一起": 查找连注释一起 = true; break;
                    case "--带行": 查找带行 = true; break;
                    case "--写回": 查找写回 = true; 排版写回 = true; break;   // 查找替换与代码排版共用这一个旗标
                    case "--只看":
                        if (++i >= args.Length) return 用法错误("--只看 后面要跟一个文件(可写多次)");
                        查找只看.Add(args[i]); break;
                    // ---- C5 代码排版(第122批):只动前导/行尾空白,不改语义 ----
                    case "--排版":
                        要排版 = true; break;
                    case "--片段库":
                        要片段库 = true; break;
                    case "--快捷键":
                        要快捷键表 = true; break;
                    case "--缩进":
                        if (++i >= args.Length || !int.TryParse(args[i], out int 宽) || 宽 < 1 || 宽 > 16)
                            return 用法错误("--缩进 后面要跟 1~16 的整数(一档几个空格)");
                        排版宽度 = 宽; break;
                    case "--制表符":
                        排版制表符 = true; break;
                    // ---- C6 文件改名:改磁盘上的名 + 更新 .hbp 条目与别的文件里的 包含 "…" ----
                    case "--改名文件":
                        if (i + 2 >= args.Length || args[i + 1].StartsWith('-') || args[i + 2].StartsWith('-'))
                            return 用法错误("--改名文件 后要跟 旧文件名 新文件名(例如 --改名文件 工具.hb 数学工具.hb)");
                        要改名 = (args[i + 1], args[i + 2]);
                        i += 2;
                        break;
                    case "--help-text":
                        // 与界面里"帮助"菜单同一份文本(引擎生成,不会和代码脱节)
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-')) 帮助页 = args[++i];
                        要帮助文本 = true;
                        break;
                    case "-o" or "--out":
                        if (++i >= args.Length) return 用法错误("-o/--out 后需要路径");
                        输出路径 = args[i];
                        break;
                    case "--in":
                        if (++i >= args.Length) return 用法错误("--in 后需要路径");
                        输入路径 = args[i];
                        break;
                    case "--hex": 要hex = true; break;
                    case "--fix-semicolon":
                        // 老代码迁移:语言语句缺结尾的 ';' 就补上(块头/{} /内联汇编/标签/包含 不动)
                        if (i + 1 >= args.Length) return 用法错误("--fix-semicolon 后需要文件路径(.hb 或 .cs)");
                        要补分号.Add(args[++i]);
                        break;
                    case "--labels": 要标签 = true; break;
                    // R4-10:`--导出模型 路径` —— 跑完把 `三维网格.hb` 那五个数组导成纯 ASCII 文本
                    // (顶点 / 边)。为什么单开一个开关:看模型这件事以前只能靠人眼盯着窗口,
                    // 而"把绘制命令打成文字"那条路**过控制台管道会把编码转坏**(实测 1.1 MB 的文件里
                    // 一个中文字都解不出)⇒ 要一份**不经过编码转换**的纯数字,才好在外面画出来看。
                    case "--导出模型":
                        if (++i >= args.Length || args[i].StartsWith('-'))
                            return 用法错误("--导出模型 后面要跟一个输出路径(例如 --导出模型 模型.txt)");
                        导出模型路径 = args[i];
                        要运行 = true;          // 导出的是"跑完之后"的网格 ⇒ 得先真跑一遍
                        break;
                    case "--asm": 要asm = true; break;
                    case "--asm-marked": 要asm = true; 要标记 = true; break;   // 展开文本带源行标记
                    case "--片段":                                            // --片段 起-止:只展开这一段
                        if (++i >= args.Length) return 用法错误("--片段 后需要 起行-止行");
                        {
                            var 段 = args[i].Split('-');
                            if (段.Length != 2 || !int.TryParse(段[0], out 片段起) || !int.TryParse(段[1], out 片段止))
                                return 用法错误($"--片段 要写成 起行-止行(当前: {args[i]})");
                        }
                        break;
                    case "--run": 要运行 = true; break;
                    // R4-9:`--跑法` 决定 **`--run` 那一步谁来跑** —— 虚拟机(C# 那份引擎实现,缺省)
                    // 还是**原生码**(机器码 → C 源码 → 本机 C 编译器 → 原生 exe,与 `--方式 C` 同一个后端)。
                    // 为什么单开一个开关、不动 `--方式`:那个词管的是**产物**(`--exe` 那条路),
                    // 而"跑"与"产"是两件事,合在一个词上会让 `--exe … --run` 这种写法没法定。
                    case "--跑法":
                        {
                            if (++i >= args.Length)
                                return 用法错误("--跑法 后面要跟 `原生` 或 `虚拟机`(例如 hbcc 程序.hb --run --跑法 原生)");
                            string 值 = args[i].Trim();
                            if (值 is "原生" or "原生C" or "原生c" or "C" or "c" or "native") 跑原生 = true;
                            else if (值 is "虚拟机" or "虚机" or "vm" or "VM") 跑原生 = false;
                            else return 用法错误($"--跑法 只认 `原生` 或 `虚拟机`,收到: {值}");
                        }
                        break;
                    // R4-9:把 `当前时间()` 冻在某一秒 —— 两条跑法都认(虚拟机侧换 `宿主能力C.取当前秒`,
                    // 原生侧补一条 `--hb-now`)。不冻的话同一份程序两趟的秒不同,输出与内存都可能差几个字节。
                    case "--冻结秒":
                        {
                            if (++i >= args.Length || !long.TryParse(args[i], out long 秒))
                                return 用法错误("--冻结秒 后面要跟一个整数(Unix 秒,例如 --冻结秒 1790000000)");
                            冻结秒 = 秒;
                        }
                        break;
                // R1-3 的"看得见"那一半:跑完之后把整块运行内存缩成一张图(`引擎\内存缩略C.cs` 出这份文本)。
                // 参数可选:写 `短` 就只出段表 + 热点,不打每段头一页的原始字。
                case "--内存" or "--memmap":
                    要内存图 = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    {
                        内存图原始 = args[++i] != "短";
                    }
                    break;
                // H39-1:把"跑程序"那一步挪进子进程(虚拟机遇上未处理异常 / 宿主崩了就崩它,不带走这一条命令)。
                // 与 --run 搭配;子进程就是**再起一份自己**(hbcc --跑程序服务),图形仍按无头口径打出来。
                case "--跨进程": 跨进程 = true; 要运行 = true; break;
                // R1-6 批 1:`hbcc --run` 本来就是无头(没有窗口)—— 图形原语按同一份格式逐条打在标准输出上。
                // 这个旗标是**把这个口径显式写出来**(给脚本看,效果与不写完全一样),见帮助《上手》第25节。
                case "--无头": 要无头 = true; 要运行 = true; break;
                // H91(G4 批 23):**准不准弹提示框 / 选文件**。
                // 缺省关着(硬规矩 6:一个没人点的模态框会把整趟挂住 —— 自检与脚本都会踩);
                // 只有"人在终端前看着"的时候才写这一条(与桌面 IDE 的「执行」开同一道闸)。
                case "--弹窗": 宿主弹窗C.设允许弹窗(true); break;
                // R1-6 批 2:事件脚本 —— 一份写死的输入(第几帧投哪个事件)。**只跟 --run 一起用**:
                // 开了 `--run` 才会有窗口去接这些事件(见 引擎\图形事件C.cs 的格式与帧号口径)。
                case "--事件脚本":
                    if (++i >= args.Length) return 用法错误("--事件脚本 后需要脚本文件路径(例如 --事件脚本 事件脚本.txt)");
                    事件脚本路径 = args[i];
                    要运行 = true;
                    break;
                    case "--中英" or "--en": 切到英文 = true; break;             // 把文件里的关键字/指令名切到英文
                    case "--中文": 切到中文 = true; break;                        // 反向:切回中文
                    case "--debug-steps" or "--debug-asm":
                    {
                        // 脚本化调试:走 N 步,每步打印 PC / 文件:行 / 调用处(把"单步映射"变成能被脚本断言的东西)
                        int n = 20;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        {
                            if (!TryParseNumber(args[i + 1], out n)) return 用法错误($"{a} 后需要步数: {args[i + 1]}");
                            i++;
                        }
                        要调试 = (a == "--debug-asm", n);
                        break;
                    }
                    case "--make-project":
                        // 给一个目录生成工程文件(.hbp):目录里已有的 .hb 全进文件表,主程序.hb 当启动文件
                        if (++i >= args.Length) return 用法错误("--make-project 后需要目录");
                        要建工程 = args[i];
                        break;
                    case "--bench":
                    {
                        // 虚拟机测速:确认手上这份是 Release 还是 Debug(实测差约 13 倍)。
                        // T30②(2026-09-27 评测整改):默认档从 3 提到 500_000 —— 以前 `--bench 3` 只执行 **38 条**
                        // 指令(全是冷启动开销,数字毫无意义);现在默认一跑就是"零头都看不见"的规模。
                        int n = 500_000;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        {
                            if (!TryParseNumber(args[i + 1], out n)) return 用法错误($"--bench 后需要循环次数: {args[i + 1]}");
                            i++;
                        }
                        要测速 = n;
                        break;
                    }
                    case "--exe":
                        // 生成独立 exe:`hbcc --exe 程序.hb -o 程序.exe`(宿主 + 贴机器码)
                        if (++i >= args.Length) return 用法错误("--exe 后需要输出路径(例如 --exe 程序.hb -o 程序.exe)");
                        要exe路径 = args[i];
                        要运行 = false;
                        break;
                    case "--方式":
                    {
                        // R3-6-4 批 1:`--方式 C` = 走 C 后端(机器码 → C 源码 → 本机 C 编译器 → 原生 exe,不带 .NET);
                        // 缺省 `宿主` = 老路(复制 HB 宿主 + 贴机器码尾巴)。
                        if (++i >= args.Length) return 用法错误("--方式 后要跟 宿主 或 C(例如 hbcc 程序.hb --exe 程序.exe --方式 C)");
                        string 值 = args[i];
                        if (值 is "C" or "c" or "原生" or "原生C") 方式 = "C";
                        else if (值 is "宿主" or "host") 方式 = "宿主";
                        else return 用法错误($"--方式 只认 宿主 或 C,收到: {值}");
                        break;
                    }
                    case "--打包":
                    {
                        // R3-6-4 批 4:产物打成什么(原生 C / 自带运行时·单文件 / 只编译;
                        // 第248批 H41 撤掉了「轻量·需装 .NET」那一档)。
                        // 认三个**存储键**(全 ASCII)与三个正式名 —— 两边都是 引擎\打包规格C.cs 那一份事实源。
                        if (++i >= args.Length) return 用法错误("--打包 后要跟一档规格" + Environment.NewLine + 打包规格C.表());
                        string 值 = args[i];
                        var 规格 = 打包规格C.取规格(值);
                        if (规格 == null) return 用法错误($"--打包 不认这一档: {值}" + Environment.NewLine + 打包规格C.表());
                        打包给的 = 规格.名;
                        break;
                    }
                    case "--no-opt":
                        // 编译期优化(消冗余 MOV + 循环不变量提升)默认开;要对着"原样展开"看就关掉它
                        优化 = false;
                        break;
                    case "--trace":
                    {
                        // --trace [N] 或 --trace 起始,结束:单步跑并逐条打印(调试自举/自修改代码用)
                        int n = 200; int 起 = -1, 止 = -1;
                        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                        {
                            string 参 = args[++i];
                            string[] 段 = 参.Split(',');
                            if (段.Length == 2 && TryParseNumber(段[0], out int 起2) && TryParseNumber(段[1], out int 止2)) { 起 = 起2; 止 = 止2; n = 5000; }
                            else if (!TryParseNumber(参, out n)) return 用法错误($"--trace 参数看不懂: {参}");
                        }
                        要跟踪 = (n, 起, 止);
                        break;
                    }
                    case "--dump":
                        if (++i >= args.Length) return 用法错误("--dump 后需要 起始地址[,长度]");
                        {
                            string[] 段 = args[i].Split(',');
                            if (!TryParseNumber(段[0], out int 起始)) return 用法错误($"--dump 起始地址看不懂: {段[0]}");
                            int 长度 = 64;
                            if (段.Length > 1 && !TryParseNumber(段[1], out 长度)) return 用法错误($"--dump 长度看不懂: {段[1]}");
                            要转储 = (起始, 长度);
                        }
                        break;
                    default:
                        if (a.StartsWith('-') && a != "-") return 用法错误($"未知选项: {a}");
                        文件表.Add(a);
                        break;
                }
            }

            if (要帮助文本)
            {
                // 页名可省(默认全部);中文页名:快速上手/指令速查/中间层速查/关于
                string 页 = 帮助页 ?? "";
                bool 全部 = 页.Length == 0 || 页 is "全部" or "all";
                if (全部 || 页.Contains("上手") || 页.Equals("quick", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine(帮助文本C.快速上手());
                if (全部 || 页.Contains("指令") || 页.Equals("isa", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine(帮助文本C.指令速查());
                if (全部 || 页.Contains("中间层") || 页.Equals("lang", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine(帮助文本C.中间层速查());
                if (全部 || 页.Contains("关于") || 页.Equals("about", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine(帮助文本C.关于());
                return 0;
            }

            if (要统计)
                return 跑规模统计(要更新报告);

            if (要生成帮助)
                return 跑生成帮助();

            if (要更新指令集)
                return 跑更新指令集();

            if (要清单)
                return 跑清单(清单索引, 清单词);

            if (要快捷键表)
            {
                Console.WriteLine(快捷键C.清单());
                return 0;
            }

            if (要改名 is { } 改名)
                return 跑改名文件(改名.旧, 改名.新, 排版写回, 文件表, 工程路径);

            if (要片段库)            {
                Console.WriteLine(片段表C.清单());
                Console.WriteLine();
                foreach (var 项 in 片段表C.全部)
                {
                    Console.WriteLine($"---- {项.触发}({项.标题})----");
                    Console.WriteLine(片段表C.展开(项));
                    Console.WriteLine();
                }
                return 0;
            }

            if (要排版)
                return 跑排版(文件表, 工程路径, 排版写回,
                    new 排版设置C { 缩进宽度 = 排版宽度 ?? 4, 用制表符 = 排版制表符 });

            if (要找 != null)
                return 跑查找(要找, 换成,
                    new 查找设置C { 区分大小写 = 查找区分大小写, 整词 = 查找整词, 正则 = 查找正则, 保护注释与字符串 = !查找连注释一起 },
                    查找带行, 查找写回, 查找只看, 文件表, 工程路径);

            if (导图文件 != null)
                return 跑导图探针(导图文件);

            if (乱源次数 > 0)
            {
                var (试了, 崩了, 首例, 说明) = 综合自检C.乱源轰炸(乱源次数);
                Console.WriteLine($"乱源轰炸:{试了} 例,崩 {崩了} 例(固定种子 ⇒ 语料可复现)");
                if (崩了 > 0)
                {
                    Console.Error.WriteLine($"内部错误(请回报):{说明}\n首例源码:\n{首例}");
                    return 1;
                }
                // R4-3:**深度维**(长链 / 深括号 / 块嵌套 / 否则如果长链 / 思维导图链,T20 起五族)——
                // 一格一个子进程,判据 = 退出码(栈溢出进不了 catch)
                var (深试了, 深崩了, 深首例, 深说明) = 综合自检C.乱源深度轰炸();
                Console.WriteLine(深试了 == 0
                    ? $"乱源深度维:跳过({深说明})"
                    : $"乱源深度维:{深试了} 格,不合预期 {深崩了} 格(长链 / 深括号 / 块嵌套 如果·当·选择 / " +
                      "否则如果长链 / 思维导图链,一格一个子进程)");
                if (深崩了 > 0)
                {
                    Console.Error.WriteLine($"内部错误(请回报):{深说明}\n首例源码前 300 字:\n{深首例[..Math.Min(深首例.Length, 300)]}");
                    return 1;
                }
                return 0;
            }

            if (要自检)
            {
                var 报告 = 综合自检C.运行();
                foreach (var 行 in 报告.行) Console.WriteLine(行);
                Console.WriteLine($"\n=== 综合自检:通过 {报告.通过} / 失败 {报告.失败} / 跳过 {报告.跳过} ===");
                var 清单 = 综合自检C.取断言清单(报告);
                // R4-4:条数与实例数一起打 —— 名字唯一时两个数相等,两边都被自检里那条"断言名唯一"钉着。
                Console.WriteLine($"断言清单:{清单.Length} 条 / {综合自检C.断言实例数(报告)} 次,指纹 {综合自检C.清单指纹(清单)}");

                // --更新断言清单:把清单写进 `工具\自检断言清单.txt`(进 git;谁增删断言,git diff 一眼看得出)。
                // 自检收尾会拿当前清单跟这份文件比,不一致就红灯 —— 这就是"条数漂移必须一眼可见"的落地。
                // ⚠ 第451批:**写之前要把"清单比对那一条自己"滤掉** —— 它是判据、不是被记的断言,
                // 而读过这份清单的三处(比对 / `当前数字C.取断言清单`)都按 `清单比对断言名` 滤它。
                // 以前这儿没滤 ⇒ 文件里多一行、读出来少一行,§4 那个条数与文件行数每次差 1(实测 3365 / 3364)。
                if (要更新断言清单)
                {
                    string? 根 = 找仓库根();
                    if (根 == null)
                    {
                        Console.Error.WriteLine("错误:找不到仓库根(要有 Sample 目录的那一层),没法写清单文件");
                        return 1;
                    }
                    string 清单路径 = Path.Combine(根, "工具", "自检断言清单.txt");
                    var 写出去 = 清单.Where(x => x != 综合自检C.清单比对断言名).ToArray();
                    File.WriteAllLines(清单路径, 写出去, new UTF8Encoding(false));
                    Console.WriteLine($"已更新 {清单路径}({写出去.Length} 条)");
                }
                return 报告.失败 == 0 ? 0 : 1;
            }

            if (要测速 is { } 次数)
            {
                跑测速(次数);
                return 0;
            }

            if (要补分号.Count > 0)
            {
                int 共补 = 0;
                foreach (string 路径 in 要补分号)
                {
                    if (!File.Exists(路径)) { Console.Error.WriteLine($"错误: 文件不存在 {路径}"); return 1; }
                    try
                    {
                        int 补了 = 路径.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                            ? 分号迁移C.补CSharp文件(路径)
                            : 分号迁移C.补文件(路径);
                        共补 += Math.Max(0, 补了);
                        Console.WriteLine($"{(补了 > 0 ? "补了" : "不用补")} {路径}" + (补了 > 0 ? $"({补了} 处)" : ""));
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"错误: {路径} —— {ex.Message}");
                        return 1;
                    }
                }
                Console.WriteLine($"共补 {共补} 处 ';'(语言语句必须以 ';' 结尾;块头、花括号、内联汇编、标签、包含 不动)");
                return 0;
            }

            if (要建工程 is { } 建工程目录)
            {
                if (!Directory.Exists(建工程目录))
                {
                    Console.Error.WriteLine($"错误: 目录不存在 {建工程目录}");
                    return 1;
                }
                string 目录全 = Path.GetFullPath(建工程目录);
                string 名 = Path.GetFileName(目录全.TrimEnd(Path.DirectorySeparatorChar));
                if (名.Length == 0) 名 = "工程";
                string 目标 = Path.Combine(目录全, 名 + 工程文件C.扩展名);
                try
                {
                    var 新工程 = 工程文件C.从目录生成(目录全, 名);
                    新工程.保存(目标);
                    Console.WriteLine($"已生成工程文件: {目标}({新工程.文件.Count} 个文件" +
                        (新工程.资源.Count > 0 ? $",{新工程.资源.Count} 个资源" : "") + $",启动文件 {新工程.启动文件})");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"错误: {ex.Message}");
                    return 1;
                }
            }

            // 工程文件(.hbp):文件表/顺序/启动文件/资源都从工程文件来;资源可当 --run 的输入
            工程文件C? 工程 = null;
            if (工程路径 != null)
            {
                try
                {
                    string 文件路径 = Directory.Exists(工程路径)
                        ? 工程文件C.找工程文件(工程路径) ?? throw new FileNotFoundException($"这个目录里没有 {工程文件C.扩展名} 工程文件: {工程路径}")
                        : 工程路径;
                    工程 = 工程文件C.加载(文件路径);
                    var 问题 = 工程.检查();
                    if (问题.Count > 0)
                    {
                        Console.Error.WriteLine($"工程文件有问题({Path.GetFileName(文件路径)}):");
                        foreach (string p in 问题) Console.Error.WriteLine($"  · {p}");
                        return 1;
                    }
                    Console.WriteLine($"工程 {工程.摘要()}");
                    if (输入路径 == null && 工程.资源.Count > 0)
                    {
                        输入路径 = 工程.取资源路径(工程.资源[0]);
                        if (输入路径 != null) Console.WriteLine($"输入流: {工程.资源[0]}");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"错误: {ex.Message}");
                    return 1;
                }
            }

            if (工程 == null && 文件表.Count == 0) return 用法错误("至少需要一个输入文件(或用 -p/--project 指向工程文件)");

            try
            {
                // 运行内存与堆区:工程文件里写了 `内存`/`堆` 就按它来(写错的情况上面 工程.检查() 已经挡住并退出了);单文件走默认
                int 内存大小 = 工程?.内存字节数 ?? 虚拟机C.默认内存大小;
                int 堆大小 = 工程?.堆字节数 ?? 中间层C.默认堆大小;
                int 池大小 = 工程?.池字节数 ?? 中间层C.默认池大小;   // R1-2:池一开始多大(不够会自己长大)
                int 软栈大小 = 工程?.软栈字节数 ?? 中间层C.默认软栈大小(内存大小);   // R1-1:软栈多大(不写就按内存自动算)
                bool 边界检查 = 工程?.边界开关 ?? true;    // H14:工程文件里 `"边界": "关"` = 不查下标越界;不写就是查
                int 帧大小 = 工程?.帧字节数 ?? 中间层C.默认帧大小;   // H34:每个函数帧多大(不写就是 96)

                var 源文件表 = new List<(string 名称, string 内容)>();
                List<string>? 工程归属 = null;
                if (工程 != null)
                {
                    源文件表 = 工程.读全部编译输入();   // C22:`.hb` 源文件(启动文件排第一)+ `汇编` 表里的汇编源码
                    // `程序集` 的边界 = 单个 `.hbp` 工程:按**整棵工程树**算"每个文件属于哪个工程" ——
                    // 程序工程把库的 .hb 列进自己的文件表一起编译时,库文件仍归库工程(库里的 `程序集` 才挡得住)。
                    // 子工程读不动就先都算本工程:编译本身会另报它的错,不在这里抢着报。
                    IReadOnlyList<工程文件C>? 树 = null;
                    try { 树 = 工程文件C.读取工程树(工程.工程路径); } catch { 树 = null; }
                    工程归属 = 工程.源文件归属([.. 源文件表.Select(f => f.名称)], 树);
                    if (要asm)
                    {
                        // H43:工程这条腿以前是**一个文件展开一份**(每个文件各 new 一个展开器)—— 别的文件里的函数 /
                        // 全局变量 / 结构全看不见,于是 `hbcc -p 跨文件工程 --asm` 报一堆"未定义的函数 / 未知名字"
                        // (第 245 批量过:全 32 个标准工程 31 过 1 败)。**真正编译**走的是 `中间层C.展开工程带目录`
                        // (一个展开器连吃整张文件表,见 `引擎\汇编器C.cs`),这里改用同一个入口 ——
                        // 输出是**一份整篇**、与真正编译同源,源行标记里带 `文件K`(文件号 = 文件表次序,1 基)。
                        Console.WriteLine($"──── 工程展开后(文件表 {源文件表.Count} 个文件) ────");
                        Console.WriteLine(中间层C.展开工程带目录(源文件表, 带源行标记: 要标记, 优化: 优化,
                            堆大小: 堆大小, 工程归属: 工程归属, 池大小: 池大小, 内存大小: 内存大小,
                            软栈大小: 软栈大小, 边界检查: 边界检查, 帧大小: 帧大小).汇编文本);
                    }
                }
                else
                {
                    foreach (string 路径 in 文件表)
                    {
                        if (!File.Exists(路径))
                        {
                            Console.Error.WriteLine($"错误: 找不到文件 {路径}");
                            return 1;
                        }
                        string 内容 = 文本落盘C.读源文件(路径);   // T16:严格 UTF-8;不是就给"另存为 UTF-8"的提示
                        // --中英/--en(切英文)、--中文(切回来):跟编辑器那个"中英切换"按钮走同一套
                        // (关键字 + 指令名/内建函数名/附加类型名各一份,**注释与字符串字面量一个字不动**),
                        // 切完直接当输入往下编译
                        if (切到英文 || 切到中文)
                            内容 = 中间层C.切换中英(切到中文, 内容);
                        源文件表.Add((Path.GetFullPath(路径), 内容));
                        if (要asm && 片段起 == 0)
                        {
                            Console.WriteLine($"──── {Path.GetFileName(路径)} 展开后 ────");
                            Console.WriteLine(中间层C.展开完整带目录(内容, Path.GetDirectoryName(Path.GetFullPath(路径)), 带源行标记: 要标记, 优化: 优化, 堆大小: 堆大小, 池大小: 池大小, 内存大小: 内存大小, 软栈大小: 软栈大小, 边界检查: 边界检查, 帧大小: 帧大小).汇编文本);
                        }
                        if (片段起 > 0)   // --片段 起-止:这一段就地展开成汇编(编辑器那条路)
                        {
                            string 段文本 = 中间层C.展开片段(内容, 片段起, 片段止, Path.GetDirectoryName(Path.GetFullPath(路径)), 堆大小, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小) ?? "";
                            Console.WriteLine($"──── 片段 {片段起}-{片段止} 展开后(共 {段文本.Length} 字 / {(段文本.Length == 0 ? 0 : 段文本.Split('\n').Length)} 行) ────");
                            // 片段文本本身是不是**自洽的纯汇编**:把整篇展开里被引用到的标签补上再汇编一次。
                            // 判据:片段引用到的每一个 `_f…` 目标,要么在片段里定义了,要么在整篇展开里定义了。
                            var 整篇行 = 中间层C.展开完整带目录(内容, Path.GetDirectoryName(Path.GetFullPath(路径)), 带源行标记: true, 优化: false, 堆大小: 堆大小, 池大小: 池大小, 内存大小: 内存大小, 软栈大小: 软栈大小, 边界检查: 边界检查, 帧大小: 帧大小).汇编文本
                                .Replace("\r\n", "\n").Split('\n')
                                .Select(x => { int c = x.IndexOf("//", StringComparison.Ordinal); return c >= 0 ? x[..c] : x; });
                            string 整篇纯 = string.Join('\n', 整篇行);
                            var 缺 = new List<string>();
                            foreach (var 行 in 段文本.Split('\n'))
                            {
                                int c = 行.IndexOf("//", StringComparison.Ordinal);
                                string s = (c >= 0 ? 行[..c] : 行).Trim();
                                if (s.Length == 0 || s.EndsWith(':')) continue;
                                int m = s.IndexOf(':');
                                if (m >= 0) { s = s[(m + 1)..].Trim(); if (s.Length == 0) continue; }
                                int sp = s.IndexOfAny([' ', '\t']);
                                if (sp <= 0) continue;
                                string 首操作 = s[(sp + 1)..].Trim();
                                int e = 首操作.IndexOfAny([' ', '\t', ',']);
                                if (e < 0) e = 首操作.Length;
                                string 候选 = 首操作[..e];
                                if (!候选.StartsWith("_f", StringComparison.Ordinal)) continue;
                                if (!段文本.Contains("\n" + 候选 + ":") && !段文本.StartsWith(候选 + ":") && !整篇纯.Contains("\n" + 候选 + ":") && !整篇纯.StartsWith(候选 + ":"))
                                    缺.Add(候选);
                            }
                            Console.WriteLine($"──── 片段引用的编译器标签都有定义: {缺.Count == 0}(缺 {string.Join(",", 缺.Distinct().Take(6))}) ────");
                        }
                    }
                }

                // H38-1:`hbcc` 是前后端契约的**第二个实现** —— 它自己持一份引擎实现(`引擎实现C.新一份()`),
                // 与 WPF 前端走的是同一份 `界面服务C`(不是"再写一套界面才知道的调用")
                var 服务 = 引擎实现C.新一份();
                var 编译结果 = 服务.编译.汇编多文件带调试(源文件表, 优化, 堆大小, 工程归属, 池大小, 内存大小, 软栈大小, 边界检查, 帧大小);
                byte[] 码 = 编译结果.机器码;
                if (编译结果.代码字节数 + 编译结果.数据字节数 > 内存大小)
                {
                    Console.Error.WriteLine($"错误: 代码 {编译结果.代码字节数} + 静态数据 {编译结果.数据字节数} " +
                        $"装不进 {内存大小} 字节的运行内存 —— 把工程文件里的 `内存` 调大" +
                        $"(最大 {工程文件C.内存文本(虚拟机C.最大内存大小)})," +
                        $"或者把 `堆` / 大数组改小");
                    return 1;
                }
                Console.WriteLine($"运行内存: {工程文件C.内存文本(内存大小)}" +
                    $"({内存大小} 字节;代码 {编译结果.代码字节数} + 静态数据 {编译结果.数据字节数} = " +
                    $"{编译结果.代码字节数 + 编译结果.数据字节数} 字节),堆 {工程文件C.大小文本(堆大小)}({堆大小} 字节)" +
                    $",池 {工程文件C.大小文本(池大小)}(一开始 {池大小} 字节,不够会自己长大)" +
                    $",软栈 {工程文件C.大小文本(软栈大小)}({软栈大小} 字节,约 {软栈大小 * 8 / 10 / 帧大小} 层调用;一帧 {帧大小} 字节、用掉八成算太深)" +
                    $",帧 {帧大小} 字节{(帧大小 == 中间层C.默认帧大小 ? "(默认)" : "(工程文件里配的)")}" +
                    $",下标越界检查 {(边界检查 ? "开(默认)" : "关(工程文件里配的)")}");
                // R1-3:分区明细 —— "这 1MB 里哪一段是什么、池还能长多少"一次摆清楚(数字都是现场算的,见 引擎\内存布局C.cs)
                foreach (string 行 in 内存布局C.文本行(编译结果, 内存大小))
                    Console.WriteLine(行);
                foreach (string 提示 in 编译结果.编译提示)
                    Console.WriteLine($"  · {提示}");

                // 脚本化调试:编译 → 装载 → 走 N 步 → 每步打印(和界面上两个单步按钮同一套 调试会话C)
                if (要调试 is { } 调)
                {
                    var 会话 = new 调试会话C
                    {
                        汇编模式 = 调.汇编,
                        调试文件表 = 编译结果.调试文件表,
                        标签表 = 编译结果.标签表,
                        内存大小 = 内存大小,
                        当前文件 = 源文件表.Count > 0 ? 源文件表[0].名称 : null,
                    };
                    // H38-1:"同一份机器码不重装"这一条口径**只有一个出口**:契约里的会话状态(IDE 那边问的是同一个口)。
                    // 这台会话这一趟是新造的 ⇒ 今天这一问必然是要装;判据收在一处,以后加"接着跑 / 重连"两边不用各写一遍。
                    if (!服务.调试.读状态(会话, 码).已装载机器码) 会话.装载(编译结果);
                    Console.WriteLine($"── 调试:{(调.汇编 ? "汇编单步(按指令)" : "语言单步(按行)")},{调.步数} 步 ──");
                    脚本化调试(会话, 调.步数);
                    return 0;
                }

                if (要标签)
                {
                    Console.WriteLine("── 标签表 ──");
                    foreach (var kv in 服务.编译.取标签表().OrderBy(x => x.Value))
                        Console.WriteLine($"  {kv.Key} = 0x{kv.Value:X}");
                }

                if (要exe路径 != null)
                {
                    // R3-6-4 批 1 的 `--方式` 与批 4 的 `--打包` 是同一条路的两头,这里合成一个词再派发:
                    // 明确给了 `--打包` 就听它的;没给就跟老那条 `--方式`(脚本一个字不改,口径与批 1~3 一致)。
                    // 走哪条腿:**自带运行时·单文件**这一条才贴宿主尾巴;
                    // `原生 C` 与 `只编译` 都归 C 后端那条路(只编译 = 只交源码,不叫 C 编译器)。
                    // `只编译` 那一档交的是源码:后缀看**哪条腿** —— 走 C 给 `.c`、走宿主给 `.hb`
                    // (`--打包 只编译` 没配 `--方式 C` 时走宿主那条,与批 4 的文档口径一致)。
                    bool 只编译 = 打包规格C.是只编译(打包给的);
                    bool 走C腿 = !只编译 || 方式 == "C";
                    string 规格 = 打包给的 ?? (方式 == "C" ? 打包规格C.原生C : 打包规格C.缺省);
                    bool 点名 = 打包给的 != null;
                    string 出 = 只编译
                        ? Path.ChangeExtension(要exe路径, 打包规格C.只编译后缀(走C腿))
                        : 要exe路径;
                    int 结果码 = (!只编译 && 规格 == 打包规格C.自带运行时)
                        ? 生成独立exe(出, 码, 编译结果, 规格, 点名)
                        : 生成原生exe(出, 编译结果, 内存大小, 只编译 ? 打包规格C.只编译 : 规格);
                    if (结果码 != 0) return 结果码;
                    return 0;
                }

                if (输出路径 != null && !要hex)
                {
                    byte[] 完整 = [0x48, 0x42, 0x01, 0x00, .. 码];
                    File.WriteAllBytes(输出路径, 完整);
                    Console.WriteLine($"已写出 {码.Length} 字节机器码 + HB 头 → {Path.GetFullPath(输出路径)}");
                }
                else
                {
                    Console.WriteLine("── 机器码 (hex) ──");
                    Console.WriteLine(BitConverter.ToString(码).Replace("-", " "));
                }

                bool 跑完没通过 = false;      // 跑完但错误槽里有号(断言失败 / 被运行期守卫拦下)→ 退出码非 0(D2 拍板)
                if (要运行)
                {
                    已在运行阶段 = true;              // 之后抛的异常是"运行期"的,别再报成"编译错误"(不然往源码里找半天)

                    // ── R4-9:`--跑法` 决定这一步**谁来执行这份机器码** ──
                    // `原生` = C 后端编出的原生 exe(实测稳态 1.75 ns/条);缺省 = 进程内 C# 虚拟机(184 ns/条)。
                    // 编译 / 装载 / 指令集一个字没动 —— 换的只是执行器,两条路的报告逐字对齐。
                    // 这一行抬头**印在分派之前**:两条跑法都得从同一个位置、用同一个字面量开始。
                    Console.WriteLine("── 运行结果 ──");
                    // ⚠ `--debug-steps` / `--debug-asm` 走的是**上面那条更早的分支**
                    // (`if (要调试 is { } 调)`:它自己装载、自己单步)⇒ 走到这儿说明没给那两条。
                    // 原生那条路上**没有逐条单步**(HB 的栈与 C 的调用栈不在一处,要单步得另立一层
                    // IPC 调试协议)⇒ "要单步"就永远落在虚拟机上:这条路是**性能档**,不是调试档。
                    if (跑原生 && 跨进程)
                    {
                        // 两条都是"把跑程序挪出本进程",叠在一起没有意义(原生本来就是另一个进程);
                        // 如实报出来,别让用户以为两个开关都生效了。
                        Console.Error.WriteLine("错误: `--跑法 原生` 与 `--跨进程` 不能一起用 —— " +
                            "原生那条路本来就是**另一个进程**(原生 exe),`--跨进程` 说的是『把虚拟机挪进子进程』。");
                        Console.Error.WriteLine("  要原生速度:去掉 `--跨进程`;要进程隔离的虚拟机:去掉 `--跑法 原生`。");
                        return 1;
                    }
                    if (!跑原生)
                        跑完没通过 = 跑虚拟机程序(编译结果, 码, 内存大小, 跨进程, 要无头, 事件脚本路径,
                            要跟踪, 要转储, 要内存图, 内存图原始, 输入路径, 冻结秒, 工程路径,
                            导出模型路径, 服务.编译.取标签表());
                    else
                        跑完没通过 = 跑原生程序(编译结果, 码, 内存大小, 冻结秒, 要无头, 事件脚本路径,
                            要转储, 要内存图, 内存图原始, 输入路径);
                    图形宿主C.摘掉();                  // R1-6:跑完把呈现口摘掉(与"宿主能力白名单跑完就清"一个口径)
                    return 跑完没通过 ? 1 : 0;
                }
                图形宿主C.摘掉();                  // R1-6:跑完把呈现口摘掉(与"宿主能力白名单跑完就清"一个口径)
                return 0;
            }
            catch (运行期错误C 运行错)
            {
                // 运行期错误(除零/栈溢出):文案和"错误槽那套"一个格式(第 N 行:说明),
                // **不许**把内部指令名(DIV/MOD)或裸 PC 漏给用户 —— 见 运行期错误C
                Console.Error.WriteLine($"运行错误: {运行错.用户文案}");
                return 1;
            }
            catch (Exception ex)
            {
                // 编译/运行期**说人话的**错误照原样报(别改文案,用户习惯与既有断言都靠它);
                // 其余漏出来的 .NET 异常(空引用/越界…) = **内部错误**:说清"请回报",绝不闪退、不甩堆栈(批 7)
                if (ex is 编译异常C or 运行期错误C)
                    Console.Error.WriteLine($"{(已在运行阶段 ? "运行错误" : "编译错误")}: {ex.Message}");
                else
                    Console.Error.WriteLine($"内部错误(请回报):{ex.GetType().Name}: {ex.Message}\n" +
                        "  这是编译器自己的问题,不是你写错了 —— 请把这行连同触发它的源码片段回报一下;偶尔重跑一次也能绕过。");
                return 1;
            }
        }

        /// <summary>
        /// 脚本化调试:走 N 步,每步打印 步号 / PC / 文件:行 / 调用处 / 是否停机(汇编单步再带一条当前指令)。
        /// 和界面上两个单步按钮共用同一个 调试会话C —— 所以"单步映射对不对"脚本就能断言,不用靠人眼。
        /// </summary>
        private static void 脚本化调试(调试会话C 会话, int 步数)
        {
            for (int i = 1; i <= 步数; i++)
            {
                var 步 = 会话.汇编模式 ? 会话.汇编单步一次() : 会话.语言走一行();
                string 位置 = 步.文件 == null
                    ? "生成代码"
                    : 步.源码行 > 0
                        ? $"{Path.GetFileName(步.文件)}:{步.源码行}行"
                        : $"{Path.GetFileName(步.文件)}:(编译器生成)";
                string 调用处 = 步.调用处行.Length == 0 ? "-" : string.Join(",", 步.调用处行);
                Console.WriteLine($"  {i,3}. PC=0x{步.程序计数器:X4}{(步.已停机 ? " 停机" : "")}  {位置}  调用处={调用处}" +
                    (会话.汇编模式 && 步.指令文本.Length > 0 ? $"  | {步.指令文本}" : ""));
                // 变量面板:停在这一行能看见哪些变量、各自是多少(和界面上的"变量"面板同一份数据;结构字段缩进显示)
                if (步.变量.Count > 0)
                    Console.WriteLine("       变量: " + string.Join("  ", 步.变量
                        .Select(v => $"{new string(' ', v.层级 * 2)}{v.名字}({v.类型名})={(v.可读 ? v.值文本 : "?")}")));
                // 有名字可说的寄存器(说明来自 寄存器信息管理器C;只报"认得出是谁的值"的那些,换过帧的旧账不算)
                var 有名 = new List<string>();
                uint 当前帧 = 会话.虚拟机.寄存器[15];
                for (int 号 = 0; 号 < 步.寄存器说明.Length; 号++)
                {
                    if (步.寄存器说明[号] is not { 编号: not 15, 源码行: > 0 } 笔) continue;
                    if (笔.登记时帧指针 != 当前帧) continue;
                    if (笔.变量名 is { Length: > 0 } 名) 有名.Add($"R{号}={名}({笔.类型名})");
                    else if (笔.说明.Contains("帧内偏移")) 有名.Add($"R{号}={笔.说明}");
                }
                if (有名.Count > 0) Console.WriteLine("       寄存器: " + string.Join("  ", 有名));
                if (步.已停机) break;
            }
        }

        /// <summary>
        /// 虚拟机测速:跑一个固定的自增循环,报"每秒执行多少条指令"。
        /// 用途很实在:确认手上这份是 Release(实测约 149M 条/秒)还是 Debug(约 11M 条/秒,慢 13 倍)。
        ///
        /// **T30②(2026-09-27 评测整改)**:
        ///   · **加预热**:以前第一遍就把结果当答案,而这台机器上冷启动(JIT + 首次走那些分支)能吃掉一大截,
        ///     实测读数随规模从 4.4 M 漂到 65 M 条/秒(**15 倍**);现在先跑一轮小规模把路走热,再正式测;
        ///   · **多次取最优**:正式跑三遍取**最快**的那遍(系统噪声只会让某一遍变慢,不会让它变快)——
        ///     这样 `--bench 200000` 与 `--bench 2000000` 的读数才落在 1.5 倍内(判据见自检)。
        /// </summary>
        private static void 跑测速(int 循环次数)
        {
            // ① 预热(规模取 1/10,夹在 1000~20000 之间:小到不费时,大到能把解释器主循环走热)
            跑一轮(循环次数 == 0 ? 1 : Math.Clamp(循环次数 / 10, 1_000, 20_000), out _, out _);

            // ② 正式:三遍取最优
            double 最快 = 0;
            long 条数 = 0;
            for (int 遍 = 0; 遍 < 3; 遍++)
            {
                跑一轮(循环次数, out long 走了, out double 秒);
                条数 = 走了;
                double 本遍 = 走了 / 秒;
                if (本遍 > 最快) 最快 = 本遍;
            }

            Console.WriteLine($"构建配置: {帮助文本C.构建配置}" +
                (帮助文本C.是调试构建() ? "  ← 换成 Release 构建大约能快 7~13 倍(看程序)" : "  (全速)"));
            Console.WriteLine($"循环 {循环次数} 次,执行 {条数} 条指令,最好一轮 {最快 / 1_000_000:F1} M 条指令/秒");
            Console.WriteLine($"  (已预热一轮 + 正式跑 3 遍取最优;一共 4 轮,数字随规模漂动的幅度因此小得多)");
        }

        /// <summary> 跑一轮:交给 `虚拟机测速C.跑一轮`(引擎层的那一份;自检也要用同一条路,理由见那个类) </summary>
        public static void 跑一轮(int 循环次数, out long 走了, out double 秒) =>
            虚拟机测速C.跑一轮(循环次数, out 走了, out 秒);

        private static int 用法错误(string 消息)
        {
            Console.Error.WriteLine($"用法错误: {消息}\n");
            打印用法();
            return 2;
        }

        /// <summary>
        /// R3-6-4 批 1:走 **C 后端**生成原生 exe —— 机器码逐条静态展开成 C 源码,
        /// 再交本机的 C 编译器(clang / gcc / MSVC 的 cl)编成**不带 .NET** 的原生可执行文件。
        ///
        /// 与老那条"宿主 + 贴机器码"的区别:老的产物要 .NET;这条产物几十~几百 KB、双击即跑,
        /// 而且编译期就能搬到 Linux / macOS(有 cc 就行)。
        ///
        /// R3-6-4 批 4:`--打包 只编译` 时**不叫 C 编译器**,直接把 C 源码交到 `输出路径` 上 ——
        /// 给"我要自己拿去 cc"的人用,也让**没有 C 编译器的机器**照样能用上 C 后端。
        /// </summary>
        private static int 生成原生exe(string 输出路径, 汇编调试结果C 编译结果, int 内存大小, string 打包)
        {
            string 输出全 = Path.GetFullPath(输出路径);
            Directory.CreateDirectory(Path.GetDirectoryName(输出全) ?? ".");

            C后端结果C 转译;
            try
            {
                转译 = C后端C.转译(编译结果, 内存大小);
            }
            catch (C后端错误C ex)
            {
                Console.Error.WriteLine($"错误: {ex.Message}");
                return 1;
            }

            // `只编译 + 宿主那条腿` = 连 C 源码都不交,只交机器码(与 R3-6-4 批 4 的文档口径一致:
            //   走 C 那条交 `.c`、走宿主那条交 `.hb`)。判据是**后缀**:调用方已经按"哪条腿"定过了。
            if (打包规格C.是只编译(打包) && !输出全.EndsWith(".c", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.WriteAllBytes(输出全, [0x48, 0x42, 0x01, 0x00, .. 编译结果.机器码]);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"错误: 写不出来({输出全}):{ex.Message}");
                    return 1;
                }
                Console.WriteLine($"已生成机器码文件: {输出全}(4 字节 HB 头 + {编译结果.机器码.Length} 字节机器码 = " +
                    $"{new FileInfo(输出全).Length} 字节)");
                Console.WriteLine("  只编译(宿主那条腿):没贴宿主、没编 exe。它能直接喂虚拟机跑,也留着给自己贴尾巴。");
                return 0;
            }

            if (打包规格C.是只编译(打包))
            {
                try
                {
                    File.WriteAllText(输出全, 转译.源码, new UTF8Encoding(false));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"错误: 写 C 源码失败({输出全}):{ex.Message}");
                    return 1;
                }
                Console.WriteLine($"已生成 C 源码: {输出全}({new FileInfo(输出全).Length} 字节," +
                    $"指令 {转译.指令条数} 条,跳转 {转译.跳转条数} 条 —— 其中间接转移 {转译.间接条数} 条走分派表)");
                Console.WriteLine("  只编译:这里**只交 C 源码**,没叫 C 编译器。自己编就是:");
                Console.WriteLine($"  clang -O2 \"{Path.GetFileName(输出全)}\" -o 程序.exe     (或 gcc / cl,哪个在 PATH 里用哪个)");
                Console.WriteLine("  想一步到位:把 `--打包` 换成 原生 C(要本机有 C 编译器)。");
                return 0;
            }

            // 生成的 C 源码放在临时目录里(留一份方便看/手工编;仓库里不留产物)
            string 暂存 = Path.Combine(临时区C.基目录(), "hbcc_C后端", Environment.ProcessId.ToString());
            Directory.CreateDirectory(暂存);
            string c源 = Path.Combine(暂存, Path.GetFileNameWithoutExtension(输出全) + ".c");
            try
            {
                File.WriteAllText(c源, 转译.源码, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"错误: 写 C 源码失败({c源}):{ex.Message}");
                return 1;
            }

            var 结果 = C工具链C.编成exe(c源, 输出全);
            if (!结果.成功)
            {
                Console.Error.WriteLine("错误: C 后端没能编出原生 exe。");
                if (结果.找不到原因.Length > 0) Console.Error.WriteLine($"  {结果.找不到原因}");
                if (结果.日志.Length > 0) Console.Error.WriteLine($"  C 编译器说:{结果.日志}");
                Console.Error.WriteLine($"  生成的 C 源码留在:{c源}");
                Console.Error.WriteLine("  要马上能跑,换成老那条路:`--方式 宿主`(产物要 .NET,但立即可用);");
                Console.Error.WriteLine("  或者加 `--打包 只编译`:只交 C 源码,到有 C 编译器的机器上再编。");
                return 1;
            }

            Console.WriteLine($"已生成原生程序: {输出全}({new FileInfo(输出全).Length / 1024} KB,不带 .NET,双击即跑)");
            Console.WriteLine($"  C 源码: {c源}(指令 {转译.指令条数} 条,跳转 {转译.跳转条数} 条 —— 其中间接转移 {转译.间接条数} 条走分派表)");
            Console.WriteLine($"  C 编译器: {结果.编译器}({结果.环境说明})");
            Console.WriteLine("  输入重定向:`程序.exe < 输入.txt`;运行期错误带源码行号、退出码非 0");
            Console.WriteLine("  宿主能力:文件 / 目录 / 环境变量 / 时间 / 分块读照做;" +
                "`运行进程` / `取网址` 要工程文件里的白名单,独立 exe 带不了 —— 一律按\"这个宿主没挂\"返回");
            return 0;
        }

        /// <summary>
        /// 生成"独立 exe":复制一份 HB 宿主,把机器码 + 运行期报错的说明 + 错误槽地址贴到它尾巴上。
        /// 尾巴格式见 HB宿主\Program.cs(最后 18 字节是固定字段 + "HBEX" 标记)。
        ///
        /// R3-6-4 批 4:**先校验宿主是不是真自包含**。
        /// 顺手项那条缺陷就出在这儿 —— `找宿主exe()` 以前只看"路径在不在",盘上那份 **0.18 MB** 的
        /// 框架依赖 exe 被当成自包含贴出去,文案还写"双击即跑",拷到没装 .NET 的机器上就跑不起来。
        /// 现在:
        ///   · `自带运行时·单文件` —— 候选里**必须**挑得出一份真自包含的;一份都没有就报带改法的错**(如实说清)**
        ///     (IDE 那头同一条路会**后台重新发布**);
        ///   · `只编译` —— 连宿主都不找:交一份 `.hb`(4 字节头 + 机器码)。
        ///
        /// 第248批(H41)撤掉了「轻量·需装 .NET」那一档 —— 它自称"一百多 KB 的框架依赖版",
        /// 实测贴的却是那份 101 MB 的自包含宿主(三处文案全说反),而"产物小"这件事
        /// **已经由「原生 C」更彻底地解决**(几百 KB、连 .NET 都不用装)。详见 `引擎\打包规格C.cs`。
        /// </summary>
        private static int 生成独立exe(string 输出路径, byte[] 机器码, 汇编调试结果C 编译结果, string 打包 = 打包规格C.缺省,
            bool 点名要这一档 = false)
        {
            var 候选 = 找宿主候选();

            if (打包规格C.是只编译(打包))
            {
                string 全 = Path.GetFullPath(输出路径);
                Directory.CreateDirectory(Path.GetDirectoryName(全) ?? ".");
                try { File.WriteAllBytes(全, [0x48, 0x42, 0x01, 0x00, .. 机器码]); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"错误: 写不出来({全}):{ex.Message}");
                    return 1;
                }
                Console.WriteLine($"已生成机器码文件: {全}(4 字节 HB 头 + {机器码.Length} 字节机器码 = {new FileInfo(全).Length} 字节)");
                Console.WriteLine("  只编译:没贴宿主、没编 exe。它能直接喂虚拟机跑,也留着给自己贴尾巴或做别的处理。");
                Console.WriteLine("  想一步到位:把 `--打包` 换成 原生 C(本机要有 C 编译器)或 自带运行时·单文件(目标机器什么都不用装)。");
                return 0;
            }

            string? 宿主;
            string 校验说明 = "";
            {
                // 第248批之后这里只剩**一条贴宿主的路**(自带运行时·单文件):
                // 先看手上第一份是不是真自包含,不是就往后找一份真的;一份都没有就按下面那两种情形**说真话**。
                宿主 = 候选.FirstOrDefault(File.Exists);
                if (宿主 != null && 打包规格C.自包含原因(宿主) is { } 原因)
                {
                    // 手上这份不是真自包含 —— 先往后找一份真的
                    string? 真自包含 = 候选.FirstOrDefault(项 => File.Exists(项) && 打包规格C.是自包含(项));
                    if (真自包含 != null)
                    {
                        宿主 = 真自包含;
                        校验说明 = "这一份是真自包含单文件版,目标机器**什么都不用装**";
                    }
                    else if (点名要这一档)
                    {
                        // **点名**要自带运行时:一份真的都没有 ⇒ 不冒充,当场说清"差在哪儿 + 怎么修"
                        Console.Error.WriteLine("错误: 要『自带运行时·单文件』,可找到的宿主**没有一份是真自包含的**。");
                        Console.Error.WriteLine($"  最好的那份:{宿主}");
                        Console.Error.WriteLine($"  差在哪儿:{原因}");
                        Console.Error.WriteLine($"  正式的修法:{打包规格C.发布宿主改法}");
                        Console.Error.WriteLine($"  临时换法:`--打包 {打包规格C.原生C}`(产物不带 .NET,要本机有 C 编译器);");
                        Console.Error.WriteLine($"            或 `--打包 {打包规格C.只编译}`(只交机器码,自己贴)。");
                        foreach (string 项 in 候选) Console.Error.WriteLine($"  找过: {项}");
                        return 1;
                    }
                    else
                    {
                        // 缺省那一支(老命令一个字不改):留个能用的产物,但**嘴里说实话** ——
                        // 以前就是这儿不校验,才让"0.18 MB 的框架依赖 exe + 文案写『双击即跑』"蒙混过去。
                        校验说明 = "这一份是**框架依赖**版(不是真自包含):本机能跑,拷到没装 .NET 10 的机器上跑不起来";
                        Console.Error.WriteLine("提醒: 缺省那档想要『自带运行时·单文件』,可盘上这份宿主不是真自包含的。");
                        Console.Error.WriteLine($"  差在哪儿:{原因}");
                        Console.Error.WriteLine($"  想要真自包含(目标机器什么都不用装):{打包规格C.发布宿主改法}");
                        Console.Error.WriteLine($"  不想装 C 工具链、也不想等发布:`--打包 {打包规格C.原生C}`(产物几十~几百 KB、不带 .NET)。");
                    }
                }
                else if (宿主 != null)
                {
                    校验说明 = "这一份是真自包含单文件版,目标机器**什么都不用装**";
                }
            }

            if (宿主 == null)
            {
                Console.Error.WriteLine("错误: 找不到 HB 宿主 exe(HB宿主.exe)。");
                Console.Error.WriteLine("  开发时:先 `dotnet build HB宿主\\HB宿主.csproj`(会生成 HB宿主\\bin\\<配置>\\net10.0\\HB宿主.exe)");
                Console.Error.WriteLine($"  要『不装 .NET 也能跑』的独立 exe:{打包规格C.发布宿主改法}");
                Console.Error.WriteLine("  找不到时 hbcc 会在这些地方找:自己旁边、自己旁边的 宿主\\ 子目录、仓库里的 HB宿主\\bin\\…");
                foreach (string 项 in 候选) Console.Error.WriteLine($"  找过: {项}");
                return 1;
            }

            // H35:手上这份宿主**是哪一版**?光"够大"不算数 —— 图形能力落地之前发布的那份也够大,
            // 贴出来的独立 exe 是"老宿主 + 新机器码"(`打开窗口` 一律给 假,双击就是黑框一闪)。
            // 判据在 `宿主能力C`(带 `--能力` 问它一句;老宿主不认这句问话,正好被筛出来):
            // 这个程序用得到它没有的能力就**不产这一份**(如实说清 + 给发布命令),用不到就照产但嘴里说实话。
            // H26 再补一条轴:有顶层常量初值 = 要写段式初值包(`HBE3`),宿主也得**读得动这一档**才产。
            // H7 补第三条:程序有源码位置(编译出来的都有)= 要写带源行表的包(`HBE4`)⇒ 独立 exe 的运行期报错才带行号。
            var 源行表 = 执行行映射C.压源行表(编译结果.调试文件表);
            int 需要包格式 = 源行表.空 ? (编译结果.初值表.Count > 0 ? 程序包C.包格式_段初值 : 0) : 程序包C.包格式_源行表;
            var 宿主版本 = 宿主能力C.问能力(宿主, 0, 需要包格式);
            if (宿主版本.陈旧 is { } 旧因)
            {
                int 需要 = 宿主能力C.这段机器码要的功能号上限(机器码);
                bool 能力不够 = 需要 > 宿主版本.最大功能号;
                bool 包格式不够 = 需要包格式 > 宿主版本.包格式;
                if (能力不够 || 包格式不够)
                {
                    Console.Error.WriteLine("错误: 盘上那份宿主比当前代码旧,而这个程序要用到它没有的能力 —— 这一份不产。");
                    Console.Error.WriteLine($"  宿主的毛病:{旧因}");
                    Console.Error.WriteLine(能力不够
                        ? $"  程序要用到功能号 {需要},{宿主能力C.能力上限文本(宿主版本)}。"
                        : $"  程序要用到包格式 {需要包格式},{宿主能力C.包格式上限文本(宿主版本)}。");
                    Console.Error.WriteLine($"  正式的修法(重新发布宿主):{打包规格C.发布宿主改法}");
                    Console.Error.WriteLine($"  或者换 `--打包 {打包规格C.原生C}`(产物不带 .NET,也不看宿主新旧)。");
                    return 1;
                }
                Console.Error.WriteLine("提醒: 盘上那份宿主比当前代码旧 —— 这个程序用不到它没有的能力,这一份照跑。");
                Console.Error.WriteLine($"  宿主的毛病:{旧因}");
                Console.Error.WriteLine($"  换一份新的:{打包规格C.发布宿主改法}");
                校验说明 = 校验说明.Length > 0 ? 校验说明 + ";这一份宿主比当前代码旧(见上面的提醒)" : "这一份宿主比当前代码旧(见上面的提醒)";
            }

            // 运行期报错的说明:错误槽标签 → 地址(标签表),消息按错误号顺序排
            var 消息表 = new List<string>();
            int 错误槽地址 = 0;
            foreach (var (槽标签, 表) in 编译结果.运行期报错)
            {
                if (错误槽地址 == 0 && 编译结果.标签表.TryGetValue(槽标签, out int 地址)) 错误槽地址 = 地址;
                foreach (var (_, 说明) in 表.OrderBy(x => x.号)) 消息表.Add(说明);
            }

            var 宿主字节 = File.ReadAllBytes(宿主);
            var 打包字节 = 程序包C.打包(宿主字节, 机器码, 消息表, 错误槽地址, 编译结果.初值表, 源行表);
            string 目录 = Path.GetDirectoryName(Path.GetFullPath(输出路径)) ?? ".";
            Directory.CreateDirectory(目录);
            File.WriteAllBytes(输出路径, 打包字节);
            Console.WriteLine($"已生成独立程序: {Path.GetFullPath(输出路径)}({new FileInfo(输出路径).Length / 1024} KB)");
            Console.WriteLine($"  打包:{打包规格C.规格名(打包)}");
            Console.WriteLine($"  宿主: {宿主}({工程文件C.大小文本((int)new FileInfo(宿主).Length)}{校验说明 switch { "" => "", _ => "," + 校验说明 }})");
            Console.WriteLine($"  机器码 {机器码.Length} 字节" + (消息表.Count > 0 ? $",运行期报错说明 {消息表.Count} 条" : "") +
                (编译结果.初值表.Count > 0 ? $",顶层常量初值 {编译结果.初值表.Count} 处(装载时写进内存)" : ""));
            Console.WriteLine("  双击就能跑(控制台输入输出直连终端);输入重定向:`程序.exe < 输入.txt`");
            return 0;
        }

        /// <summary>
        /// 找**能独立拷走**的 HB 宿主 exe,按优先级排成一列(候选表本身也是"报错时把找过哪些地方列出来"的凭据)。
        /// 注意:`HB宿主\bin\&lt;配置&gt;\net10.0\HB宿主.exe` 那个是"启动器",旁边少了 dll 就跑不起来 —— 不能用,
        /// 所以这里**不认它**(只认发布出来的那份)。
        /// </summary>
        private static List<string> 找宿主候选()
        {
            string 自己目录 = AppContext.BaseDirectory;
            var 候选 = new List<string>
            {
                Path.Combine(自己目录, "HB宿主.exe"),                 // 你把它拷到 hbcc 旁边(发布时就是这么放的)
                Path.Combine(自己目录, "宿主", "HB宿主.exe"),
            };
            var 目录 = new DirectoryInfo(自己目录);
            for (int 上 = 0; 上 < 5 && 目录?.Parent != null; 上++)
            {
                目录 = 目录.Parent;
                foreach (string 配置 in new[] { "Release", "Debug" })
                {
                    string 发布 = Path.Combine(目录.FullName, "HB宿主", "bin", 配置, "net10.0", "win-x64", "publish", "HB宿主.exe");
                    if (File.Exists(发布)) 候选.Add(发布);
                    string 发布arm = Path.Combine(目录.FullName, "HB宿主", "bin", 配置, "net10.0", "win-arm64", "publish", "HB宿主.exe");
                    if (File.Exists(发布arm)) 候选.Add(发布arm);
                }
            }
            return 候选;
        }

        /// <summary> 把标准输入整段读成字节(重定向文件/管道时用)</summary>
        private static byte[] 读全部标准输入()
        {
            using var 流 = Console.OpenStandardInput();
            using var 记 = new MemoryStream();
            流.CopyTo(记);
            return 记.ToArray();
        }

        /// <summary>
        /// 仓库根(当前目录往上找"同时有 引擎 与 Sample 的那一层");找不到返回 null。
        /// `--更新断言清单` 与 `--stats` 都要靠它定位 `工具\` / `全面测评报告.md`。
        /// </summary>
        private static string? 找仓库根()
        {
            var 目录 = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (目录 != null)
            {
                if (Directory.Exists(Path.Combine(目录.FullName, "引擎"))
                    && Directory.Exists(Path.Combine(目录.FullName, "Sample")))
                    return 目录.FullName;
                目录 = 目录.Parent;
            }
            return null;
        }

        /// <summary>
        /// C25-3:把帮助**章节树**重新生成到 `帮助\index.html`(左 `<nav>` 折叠树 / 右 `<main>` 内容区,
        /// 零依赖不用 JavaScript,单文件可拷走)。内容全部现场读表 ⇒ 改完类型 / 指令 / 关键字 / 内建 / 方法
        /// 或某个库的头注释,跑这一条就同步;哪次忘了跑,自检里"帮助站点 HTML 盘上那份没过期"会红灯。
        /// <para>顺带把 `关键字与方法清单.md` 的**九节**全部重生成(H81,2026-10-11 起 §1~§5、§7、§8 也收回生成)
        /// —— 那份文档现在一个字都不手抄,同一条命令一起刷。</para>
        /// </summary>
        private static int 跑生成帮助()
        {
            string? 根 = 找仓库根();
            if (根 == null)
            {
                Console.Error.WriteLine("错误:--生成帮助 要在仓库根目录跑(当前目录往上找不到同时有 引擎 与 Sample 的那一层)");
                return 2;
            }
            try
            {
                帮助节点C 树 = 帮助站点C.建整树(根);
                var 产物 = 帮助站点C.生成(根);
                帮助站点C.写盘(根, 产物);
                int 页数 = 产物.Keys.Count(x => x.EndsWith(".html", StringComparison.Ordinal));
                int 叶子数 = 帮助站点C.取叶子(树).Count;
                Console.WriteLine($"已生成 {帮助站点C.站点路径(根)}({帮助站点C.节点数(树)} 个节点," +
                                  $"{页数} 个页面,{叶子数} 个话题;样式与页面都在 {帮助站点C.站点目录}\\ 下)");
                // 2026-10-05:库函数那两节也归这一条 —— 以前靠 `工具\临时\dbg1\探针` 导出,
                // 而 `工具\临时\` 不进版本库 ⇒ 探针一删,清单就停在最后那次(实测停在 5 个库 229 个方法)
                // H81(2026-10-11):§1~§5、§7、§8 也收回现场生成,这份清单**九节全是生成物**了
                string 清单路径 = Path.Combine(根, "关键字与方法清单.md");
                var 库文件表 = 帮助站点C.读库目录(Path.Combine(根, "Sample", "库"));
                int 换掉 = 关键字与方法清单C.更新(清单路径, 库文件表);
                if (换掉 > 0)
                    Console.WriteLine($"已更新 {Path.GetFileName(清单路径)}({换掉} 节:" +
                                      $"关键字 {中间层指令C.关键字表.Length} / 内建 {中间层指令C.内建函数名表.Length} / " +
                                      $"指令 {指令C.全部指令.Count} / " +
                                      $"{库文件表.Count} 个库、" +
                                      $"{库文件表.Sum(x => 帮助站点C.取公开方法(x.内容).Count)} 个公开方法)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"生成帮助站点失败:{ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// R3-6-1:把 **ISA 真值源**重新写进 `工具\指令集真值源.txt`(进 git)。
        ///
        /// <para>内容全部现场读表:`指令C`(名字 / 操作码 / 格式 / 示例 / 关系码)+ `指令集说明.md §3 操作码表`
        /// (语义说明)+ 手写的语义落点表。改了指令表或那段文档就跑这一条同步;
        /// 哪次忘了跑,自检里「指令集真值源 生成的是它自己(逐字节)」会红灯。</para>
        ///
        /// <para>**为什么落在 `工具\`**:与 `自检断言清单.txt` 同一个口径 —— 机器生成、进版本库、
        /// `git diff` 一眼看得出 ISA 改了什么。换核(R3-6-2)拿它当施工与对账基准。</para>
        /// </summary>
        private static int 跑更新指令集()
        {
            string? 根 = 找仓库根();
            if (根 == null)
            {
                Console.Error.WriteLine("错误:--更新指令集 要在仓库根目录跑(当前目录往上找不到同时有 引擎 与 Sample 的那一层)");
                return 2;
            }
            try
            {
                string 文档路径 = Path.Combine(根, 指令集真值源C.语义文档相对路径);
                string? 文档全文 = File.Exists(文档路径) ? File.ReadAllText(文档路径) : null;
                if (文档全文 == null)
                    Console.Error.WriteLine($"提示:读不到 {指令集真值源C.语义文档相对路径},这次生成的表里语义那一段是空的");
                string 文本 = 指令集真值源C.生成(文档全文,
                    f => File.Exists(Path.Combine(根, f.Replace('/', Path.DirectorySeparatorChar))));
                string 路径 = Path.Combine(根, 指令集真值源C.相对落点);
                File.WriteAllText(路径, 文本, new UTF8Encoding(false));
                int 行数 = 文本.Replace("\r\n", "\n").Split('\n').Length;
                Console.WriteLine($"已更新 {路径}({指令C.全部指令.Count} 条指令 / {行数} 行 / " +
                                  $"{new System.Text.UTF8Encoding(false).GetByteCount(文本)} 字节,逐字节可复现)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"生成指令集真值源失败:{ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// P31:`--stats` 打印规模统计;`--更新报告` 再跑一遍自检(自检条数只有跑过才知道),
        /// 然后把那张表写进 `全面测评报告.md` 的一对标记之间。找不到报告文件就当场说清在哪儿跑。
        /// </summary>
        /// <summary>
        /// T20(2026-09-27 评测整改):**把思维导图那条链搬到命令行** —— 扫结构 → 声明线 → 分区。
        /// 跑通报一行统计(声明多少处 / 框多少个);链上抛编译错误(嵌套太深 / 源文件编码不对…)就按普通
        /// 编译错误打出来、退出码 1。
        ///
        /// 为什么要这个开关:那条链上的几处递归(`源码声明扫描C` → `声明线C.收块们` → `导图分区C`)长在界面的路上,
        /// 而**栈溢出不是可捕获异常**(`catch` 接不住)⇒ 判据只能是"另起一个子进程看退出码"。
        /// `--乱源` 深度维里那三格走的就是它(见 `自检\综合自检C.编译诊断.cs` 的 `乱源深度轰炸`)。
        /// </summary>
        private static int 跑导图探针(string 路径)
        {
            string 文本;
            try { 文本 = 文本落盘C.读源文件(路径); }
            catch (编译异常C 错) { Console.Error.WriteLine($"编译错误: {错.中文}"); return 1; }
            try
            {
                var 树 = 源码声明扫描C.扫结构(文本, 路径);
                var 索引 = 声明线C.建(树);
                var 根 = 导图分区C.建(树, 索引);
                记录导图统计(索引, 根);
                return 0;
            }
            catch (编译异常C 错)
            {
                Console.Error.WriteLine($"编译错误: 第 {错.行号?.ToString() ?? "?"} 行: {错.中文}");
                return 1;
            }
        }

        private static void 记录导图统计(声明索引C 索引, 框来源C 根)
            => Console.WriteLine($"思维导图链:声明 {索引.计数} 处 / 框 {导图分区C.全部框(根).Count()} 个" +
                                 "(扫结构 → 声明线 → 分区,一路没崩)");

        private static int 跑规模统计(bool 更新)
        {
            string 根 = Directory.GetCurrentDirectory();
            string 报告路径 = Path.Combine(根, "全面测评报告.md");
            if (!Directory.Exists(Path.Combine(根, "引擎")) || !Directory.Exists(Path.Combine(根, "中间层")))
            {
                Console.Error.WriteLine("错误:--stats 要在仓库根目录跑(有 引擎\\ 与 中间层\\ 的那一层)");
                return 2;
            }
            int 通过 = -1, 失败 = -1, 跳过 = -1, 条数 = -1;
            if (更新)
            {
                if (!File.Exists(报告路径))
                {
                    Console.Error.WriteLine($"错误:这个目录里没有 全面测评报告.md({报告路径})");
                    return 2;
                }
                var 报告 = 综合自检C.运行(正在更新报告: true);
                // §4 与规模表里那三个数**一律按"不带 Linux 档"记**:带档那一轮,`C后端跨平台断言`
                // 那几条从"跳过"换成"真跑" ⇒ 通过 +N / 跳过 -N。折算口径与 T21 那条判据同一处
                // (见 `综合自检C.Linux档真跑条数`),判据本身一个字没放宽。
                int 档差 = 综合自检C.Linux档真跑条数(报告.行);
                通过 = 报告.通过 - 档差; 失败 = 报告.失败; 跳过 = 报告.跳过 + 档差;
                条数 = 综合自检C.取断言清单(报告).Length;   // R4-4:头条数字用"条数"(清单里那些断言名)
            }
            string 表 = 规模统计C.生成表(根, 通过, 失败, 跳过, 条数);
            Console.WriteLine(表);
            if (更新)
            {
                try
                {
                    int 段数 = 规模统计C.更新报告(报告路径, 表);
                    Console.WriteLine($"\n已更新 全面测评报告.md({段数} 段)");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"\n更新报告失败:{ex.Message}");
                    return 1;
                }

                // R4-6(第197批):"当前数字"也一起写 —— 摘要数字只有这一处维护点,
                // 别处(任务清单 / 全面测评 / 三份 ISA 文档)一律只许指路。没这个文件就当没这回事。
                string 接手路径 = Path.Combine(根, 当前数字C.接手说明文件名);
                if (File.Exists(接手路径))
                {
                    try
                    {
                        int 段数 = 当前数字C.更新(接手路径, 当前数字C.生成(根, 通过, 失败, 跳过));
                        Console.WriteLine($"已更新 {当前数字C.接手说明文件名}({段数} 段)");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"\n更新 {当前数字C.接手说明文件名} 失败:{ex.Message}");
                        return 1;
                    }
                }
            }
            return 0;
        }

        /// <summary>
        /// 第113批:任务清单的结构管理。`--清单`(默认)= 校验;"`--清单 --索引`" 重新生成那一节;
        /// "`--清单 --查 <词>`" 按节编号 / 支脉 / 支脉号 / 批号 / 种类 / 状态 / 关键字查条目(带行区间)。
        /// </summary>
        /// <summary>
        /// P21(第118批):命令行版的**查找 / 替换**。文件来自 `-p 工程`(.hbp 的文件表)或命令行上给的 .hb;
        /// 不给 `--换成` = 只找(打印每处 文件:行:列);给了 = 算替换清单并**打印预览清单**
        /// (默认**不写盘**,加 `--写回` 才真改;写回保留原来的 BOM 与换行)。
        /// 加 `--正则`(H5)= 要找的内容按正则解释(与 IDE 的 `Ctrl+F` 面板同一套口径),换成里可写 `$1` 展开分组;
        /// 命中跨行的替换不做(当场报一条)、正则超时 3 秒。与界面共用同一套引擎(`查找替换C`)。
        /// </summary>
        /// <summary>
        /// 取"要处理的源码表":`-p/--project` 给工程就读工程的文件表,否则读命令行给的那几个 .hb。
        /// 干活的只有这一份(查找替换 / 代码排版共用),免得两处口径不一致。
        /// 返回 0 = 成功;-1 = 已经报过错(调用方直接原样返回);其它非 0 = 该返回的退出码。
        /// </summary>
        private static int 取源码表(List<string> 文件表, string? 工程路径,
            out List<(string 名称, string 内容)> 源, out Dictionary<string, bool> 有BOM)
        {
            源 = [];
            有BOM = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            if (工程路径 != null)
            {
                try
                {
                    string 文件路径 = Directory.Exists(工程路径)
                        ? 工程文件C.找工程文件(工程路径) ?? throw new FileNotFoundException($"这个目录里没有 {工程文件C.扩展名} 工程文件: {工程路径}")
                        : 工程路径;
                    var 工程 = 工程文件C.加载(文件路径);
                    var 工程问题 = 工程.检查();
                    if (工程问题.Count > 0)
                    {
                        Console.Error.WriteLine($"工程文件有问题({Path.GetFileName(文件路径)}):");
                        foreach (string p in 工程问题) Console.Error.WriteLine($"  · {p}");
                        return 1;
                    }
                    foreach (var (名称, 内容) in 工程.读全部源文件())
                    {
                        源.Add((名称, 内容));
                        有BOM[名称] = 有BOM文件(名称);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"错误: {ex.Message}");
                    return 1;
                }
            }
            else
            {
                foreach (string 路径 in 文件表)
                {
                    if (!File.Exists(路径)) { Console.Error.WriteLine($"错误: 文件不存在 {路径}"); return 2; }
                    string 全 = Path.GetFullPath(路径);
                    源.Add((全, 文本落盘C.读源文件(全)));   // T16:严格 UTF-8;不是就给"另存为 UTF-8"的提示
                    有BOM[全] = 有BOM文件(全);
                }
            }
            return 0;
        }

        /// <summary>
        /// D1:从工程文件(-p)收集"可执行文件白名单"(名字 → 绝对路径);整棵工程树并起来(子工程声明的也算),
        /// 重名以**先出现的**为准。没有工程文件 / 没声明 → 空(null)= 这个宿主不给"起进程"能力。
        /// </summary>
        private static Dictionary<string, string>? 取工程可执行白名单(string? 工程路径)
        {
            if (工程路径 == null) return null;
            try
            {
                string 文件路径 = Directory.Exists(工程路径)
                    ? 工程文件C.找工程文件(工程路径) ?? 工程路径
                    : 工程路径;
                if (!File.Exists(文件路径)) return null;
                var 表 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var 工程 in 工程文件C.读取工程树(文件路径))
                    foreach (var (名字, 路径) in 工程.可执行文件)
                    {
                        string 键 = 名字.Trim();
                        if (键.Length == 0 || 路径.Trim().Length == 0 || 表.ContainsKey(键)) continue;
                        表[键] = 工程.绝对路径(路径.Trim());
                    }
                return 表.Count > 0 ? 表 : null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"警告: 读工程文件里的 `可执行文件` 表失败({ex.Message})→ 这一趟没有『起进程』能力");
                return null;
            }
        }

        /// <summary>
        /// D5:从工程文件(-p)收集"网址白名单"(整棵工程树并起来,解析器与运行时共用 `网址白名单C.由文本`)。
        /// 没有工程文件 / 没声明 → null = 这个宿主不给"连网址"能力。
        /// </summary>
        private static 网址白名单C? 取工程网址白名单(string? 工程路径)
        {
            if (工程路径 == null) return null;
            try
            {
                string 文件路径 = Directory.Exists(工程路径)
                    ? 工程文件C.找工程文件(工程路径) ?? 工程路径
                    : 工程路径;
                if (!File.Exists(文件路径)) return null;
                var 行 = new List<string>();
                foreach (var 工程 in 工程文件C.读取工程树(文件路径)) 行.AddRange(工程.网址);
                var 表 = 网址白名单C.由文本(行);
                return 表.是空的 ? null : 表;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"警告: 读工程文件里的 `网址` 表失败({ex.Message})→ 这一趟没有『连网址』能力");
                return null;
            }
        }

        /// <summary>
        /// C6 文件改名:`--改名文件 旧 新`(配合 `-p 工程`,或者命令行给 .hb 文件表)。
        /// 一条命令干三件事,顺序固定:① 磁盘上 `File.Move`;② 改 `.hbp` 里指向它的条目
        /// (文件 / 资源 / 子工程 / 启动文件);③ 改别的文件里 `包含 "…"` 的路径(按引用方重算相对路径)。
        /// 默认**只预览不写盘**(加 `--写回` 才真改);命令行没有撤销,改之前请自己留一份。
        /// </summary>
        private static int 跑改名文件(string 旧参, string 新参, bool 写回, List<string> 文件表, string? 工程路径)
        {
            string 旧全;
            try { 旧全 = Path.GetFullPath(旧参); } catch { return 用法错误($"旧文件名看不懂: {旧参}"); }
            if (!File.Exists(旧全)) { Console.Error.WriteLine($"错误: 文件不存在 {旧参}"); return 2; }

            // 新名怎么解析:**只给文件名**(`助手.hb`)= 改在旧文件同一个目录里;
            // **带目录**(`库\算术.hb`)= 相对 `-p` 那个工程目录(命令行给文件表时相对当前目录);绝对路径照用。
            string 新名 = 新参.Trim();
            bool 带目录 = 新名.Contains('/') || 新名.Contains('\\');
            string 新基准;
            if (工程路径 != null)
                新基准 = Directory.Exists(工程路径) ? Path.GetFullPath(工程路径) : Path.GetDirectoryName(Path.GetFullPath(工程路径)) ?? "";
            else
                新基准 = Environment.CurrentDirectory;
            string 新全;
            try
            {
                新全 = Path.IsPathRooted(新名)
                    ? Path.GetFullPath(新名)
                    : Path.GetFullPath(带目录 ? Path.Combine(新基准, 新名) : Path.Combine(Path.GetDirectoryName(旧全) ?? "", 新名));
            }
            catch { return 用法错误($"新文件名看不懂: {新参}"); }
            if (!Directory.Exists(Path.GetDirectoryName(新全) ?? ""))
            {
                Console.Error.WriteLine($"错误: 目标目录不存在:{Path.GetDirectoryName(新全)}(新名的算法:不带目录 = 改在旧文件旁边;" +
                    "带目录 = 相对 -p 工程目录,没有 -p 就相对当前目录)");
                return 2;
            }
            if (File.Exists(新全) && !string.Equals(新全, 旧全, StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"错误: 新名字已经存在:{新全}(先想好要不要覆盖 —— 这里不替你覆盖)");
                return 2;
            }

            int 取码 = 取源码表(文件表, 工程路径, out var 源, out _);
            if (取码 != 0) return 取码;
            if (源.Count == 0 && 工程路径 == null)
                源.Add((旧全, File.ReadAllText(旧全)));

            // 工程文件:给了 -p 就用工程树里的 .hbp;没给就在旧文件所在目录找一个
            var 工程表 = new List<(string 文件, string 内容)>();
            var 工程路径们 = new List<string>();
            if (工程路径 != null)
            {
                try
                {
                    string 文件路径 = Directory.Exists(工程路径)
                        ? 工程文件C.找工程文件(工程路径) ?? throw new FileNotFoundException($"这个目录里没有 {工程文件C.扩展名} 工程文件: {工程路径}")
                        : 工程路径;
                    foreach (var 工程 in 工程文件C.读取工程树(文件路径)) 工程路径们.Add(工程.工程路径);
                }
                catch (Exception ex) { Console.Error.WriteLine($"错误: {ex.Message}"); return 1; }
            }
            else if (工程文件C.找工程文件(Path.GetDirectoryName(旧全) ?? "") is { } 就近)
            {
                工程路径们.Add(就近);
            }
            foreach (string p in 工程路径们)
            {
                try { 工程表.Add((p, File.ReadAllText(p))); }
                catch (Exception ex) { Console.Error.WriteLine($"读不了工程文件 {p}: {ex.Message}"); return 1; }
            }

            var 计划 = 工程维护C.计划(旧全, 新全, 源, 工程表);
            Console.WriteLine(计划.预览());
            if (!计划.可改) return 1;
            if (!写回)
            {
                Console.WriteLine("只预览,没有写盘 —— 确认无误后加 --写回 真改(命令行没有撤销,改之前请自己留一份)。");
                return 0;
            }

            try { File.Move(旧全, 新全); }
            catch (Exception ex) { Console.Error.WriteLine($"磁盘改名失败: {ex.Message}"); return 1; }

            var 全部表 = new List<(string 名称, string 内容)>(源);
            foreach (var 项 in 工程表)
                if (!全部表.Any(x => string.Equals(x.名称, 项.文件, StringComparison.OrdinalIgnoreCase))) 全部表.Add((项.文件, 项.内容));
            int 写了 = 0;
            foreach (var (名称, 内容) in 计划.套用(全部表))
            {
                string 原 = 全部表.First(x => string.Equals(x.名称, 名称, StringComparison.OrdinalIgnoreCase)).内容;
                if (string.Equals(原, 内容, StringComparison.Ordinal)) continue;
                工程维护C.写回(名称, 内容);
                写了++;
            }
            Console.WriteLine($"已改名:{Path.GetFileName(旧全)} → {Path.GetFileName(新全)};顺手改了 {写了} 个文件" +
                $"(工程文件条目 {计划.工程处数} 处 / 包含 {计划.源码处数} 处)。");
            return 0;
        }

        /// <summary>
        /// C5 代码排版(第122批):对每个文件跑 `代码排版C.排版`,打印改了几行;
        /// 默认**不写盘**(加 `--写回` 才真改,命令行没有撤销,改之前自己留一份)。
        /// 口径:只动前导空白与行尾空白(行尾在注释/字符串里的不动),注释与字符串里的字符一个不碰 ——
        /// 所以"排版前后编译出来的机器码逐字节一致"。
        /// </summary>
        private static int 跑排版(List<string> 文件表, string? 工程路径, bool 写回, 排版设置C 设置)
        {
            int 码 = 取源码表(文件表, 工程路径, out var 源, out var 有BOM);
            if (码 != 0) return 码;
            if (源.Count == 0)
                return 用法错误("--排版 需要 -p/--project 指向工程,或者给几个 .hb 文件");

            int 共改 = 0;
            var 改后 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (名称, 内容) in 源)
            {
                var 结果 = 代码排版C.排版(内容, 设置);
                共改 += 结果.改了几行;
                改后[名称] = 结果.文本;
                Console.WriteLine($"{Path.GetFileName(名称)}:{结果.改了几行} 行{(结果.变了 ? "" : "(已经是排好的)")}");
                foreach (var 条 in 结果.改动.Take(8))
                    Console.WriteLine($"    第 {条.行号} 行:『{摆出来(条.旧行)}』→『{摆出来(条.新行)}』");
                if (结果.改动.Count > 8) Console.WriteLine($"    …还有 {结果.改了几行 - 8} 行");
            }
            Console.WriteLine($"共 {共改} 行(设置:{设置})");

            if (共改 == 0) return 0;
            if (!写回)
            {
                Console.WriteLine("只预览,没有写盘 —— 确认无误后加 --写回 真改(命令行没有撤销,改之前请自己留一份)。");
                return 0;
            }
            int 写了 = 0;
            foreach (var (名称, _) in 源)
            {
                if (!改后.TryGetValue(名称, out string? 新文本)) continue;
                文本落盘C.写回(名称, 新文本, 有BOM.TryGetValue(名称, out bool b) && b);
                写了++;
            }
            Console.WriteLine($"已写回 {写了} 个文件(只动了缩进与行尾空白)。");
            return 0;
        }

        /// <summary> 预览里把空白摆出来看(空格 → ·,制表符 → →) </summary>
        private static string 摆出来(string 行)
        {
            string 一 = 行.Replace("\t", "→").Replace(" ", "·");
            return 一.Length <= 60 ? 一 : 一[..30] + " … " + 一[^24..];
        }

        private static int 跑查找(string 要找, string? 换成, 查找设置C 设置, bool 带行, bool 写回,
            List<string> 只看, List<string> 文件表, string? 工程路径)
        {
            int 取码 = 取源码表(文件表, 工程路径, out var 源, out var 有BOM);
            if (取码 != 0) return 取码;
            if (源.Count == 0)
                return 用法错误("--查找 需要 -p/--project 指向工程,或者给几个 .hb 文件");
            // H5:正则写错当场说人话(只找那一半也照问 —— `查找` 自己只会给空表)
            if (!查找替换C.试编正则(要找, 设置, out string 正则说明)) return 用法错误(正则说明);

            if (换成 == null)
            {
                int 共 = 0;
                foreach (var (名称, 内容) in 源)
                {
                    if (只看.Count > 0 && !只看.Any(候 => string.Equals(Path.GetFileName(候), Path.GetFileName(名称), StringComparison.OrdinalIgnoreCase))) continue;
                    var 命中 = 查找替换C.查找(内容, 要找, 设置);
                    foreach (var 处 in 命中)
                        Console.WriteLine($"{Path.GetFileName(名称)}:{处.行号}:{处.列 + 1}  `{处.原文}`");
                    共 += 命中.Count;
                }
                Console.WriteLine($"共 {共} 处(设置:{设置};只是找,什么都没改 —— 要替换加 --换成)");
                return 0;
            }

            var 清单 = 查找替换C.替换清单(要找, 换成, 源, 设置, 只看.Count > 0 ? 只看 : null);
            Console.WriteLine(清单.预览(带行));
            if (清单.问题.Count > 0) return 1;
            if (清单.处数 == 0) return 0;

            if (!写回)
            {
                Console.WriteLine("只预览,没有写盘 —— 确认无误后加 --写回 真改(命令行没有撤销,改之前请自己留一份)。");
                return 0;
            }

            var 改后 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (名称, 文本) in 清单.应用(源)) 改后[名称] = 文本;
            int 写了 = 0;
            foreach (string 文件 in 清单.涉及文件)
            {
                if (!改后.TryGetValue(文件, out string? 新文本)) continue;
                文本落盘C.写回(文件, 新文本, 有BOM.TryGetValue(文件, out bool b) && b);
                写了++;
            }
            Console.WriteLine($"已写回 {写了} 个文件,共 {清单.处数} 处(命令行没有撤销 —— 要回退请用 git)。");
            return 0;
        }

        /// <summary> 文件是不是带 UTF-8 BOM(写回时照着原来那份写,别把 BOM 弄丢/弄多;C26 起判定收在 `文本落盘C`) </summary>
        private static bool 有BOM文件(string 路径) => 文本落盘C.有BOM(路径);

        private static int 跑清单(bool 要索引, string? 词)
        {
            string? 根 = 找仓库根();
            if (根 == null)
            {
                Console.Error.WriteLine("错误:--清单 要在仓库根目录跑(有 任务清单.md 的那一层)");
                return 2;
            }
            string 路径 = Path.Combine(根, 任务清单C.文件名);
            if (!File.Exists(路径))
            {
                Console.Error.WriteLine($"错误:这个目录里没有 {任务清单C.文件名}({路径})");
                return 2;
            }

            if (词 != null)
            {
                string 文本 = File.ReadAllText(路径);
                var 命中 = 任务清单C.查(文本, 词);
                if (命中.Count == 0)
                {
                    Console.WriteLine($"清单里没有匹配 `{词}` 的记录");
                    return 1;
                }
                foreach (var x in 命中)
                    Console.WriteLine($"{x.编号}({x.结构头})  行 {x.标题行}  {x.标题}");
                Console.WriteLine($"共 {命中.Count} 条");
                return 0;
            }

            if (要索引)
            {
                int 段 = 任务清单C.更新索引(路径);
                Console.WriteLine($"已更新清单结构索引:{路径}({段} 段)");
                return 0;
            }

            var 问题 = 任务清单C.校验(File.ReadAllText(路径));
            if (问题.Count == 0)
            {
                var 条们 = 任务清单C.解析(File.ReadAllText(路径));
                Console.WriteLine($"清单结构:全过({条们.Count} 条记录;支脉 "
                    + string.Join(" ", 任务清单C.支脉表.Select(支 => $"{支.字母}{条们.Count(x => x.支脉 == 支.字母)}"))
                    + ";种类 " + string.Join(" ", 任务清单C.种类表.Select(种 => $"{种}{条们.Count(x => x.种类 == 种)}")) + ")");
                return 0;
            }
            foreach (string p in 问题.Take(40)) Console.Error.WriteLine("  · " + p);
            Console.Error.WriteLine(问题.Count > 40 ? $"清单结构:{问题.Count} 处问题(只列了前 40 条)" : $"清单结构:{问题.Count} 处问题");
            return 1;
        }

        private static void 打印用法()        {            Console.WriteLine("hbcc —— 中文汇编/中间层命令行编译器");
            Console.WriteLine("用法: hbcc [选项] 文件1.hb 文件2.hb ...");
            Console.WriteLine("选项:");
            Console.WriteLine("  -o, --out 路径   写出二进制(HB 头 + 机器码);缺省打 hex 到 stdout");
            Console.WriteLine("  --hex            强制打机器码 hex");
            Console.WriteLine("  --labels         打印标签表");
            Console.WriteLine("  --asm            打印各文件中间层展开后的汇编(和真正编译的一致;要看未优化原样加 --no-opt)");
            Console.WriteLine("  --asm-marked     配合 --asm:展开文本里带上源行标记(// 来自第 N 到 M 行),查`哪句生成了哪条指令`用");
            Console.WriteLine("  --片段 起-止      只打印这一段(源码行号,含两端)就地展开成的汇编,并检查它引用到的编译器标签都有定义");
            Console.WriteLine("  --no-opt         关掉编译期优化(消冗余指令 + 循环不变量提升),对着原样展开查错用");
            Console.WriteLine("  --中英, --en      把源文件里的关键字与指令名/内建函数名整体切到英文再编译(注释一个字不动;");
            Console.WriteLine("                   跟编辑器那个`中英切换`按钮同一套。**边界**(T30 收口):示例集里两边机器码逐字节");
            Console.WriteLine("                   一致;但如果用户自己起的标识符**撞了类型名**(如 `整数 测试 字符串`),切过去之后");
            Console.WriteLine("                   那两个词会被当成 `string` 类型名,产物**不再保证**逐字节一致)");
            Console.WriteLine("  --中文           反过来切回中文(配合 --en 可以把英文源切回中文编译)");
            Console.WriteLine("  --run            编译后立即运行并打印寄存器(缺省在**本机虚拟机**里跑,见下面 --跑法)");
            Console.WriteLine("  --跑法 原生      与 --run 同时:**跑原生码** —— 机器码转 C 源码,交本机 C 编译器编成原生 exe 再跑,");
            Console.WriteLine("                   绕开虚拟机。实测稳态 **1.75 ns/指令**(虚拟机约 184 ns/指令 ⇒ 差约 100 倍):");
            Console.WriteLine("                   5 万顶点那份 3D 负载 2144.9 ms → 70.5 ms;16 万条指令的小程序看不出差别");
            Console.WriteLine("                   (进程启动要 ~30 ms,短程序被它盖住)。报告与虚拟机那条路**逐字对齐**:");
            Console.WriteLine("                   输出 / 指令条数 / PC / 寄存器 / --dump / --内存 全照旧(整机状态走 `--hb-dump` 读回)。");
            Console.WriteLine("                   不用 C 编译器的机器照旧用缺省那条;`--跑法 虚拟机` 可以明写回来。");
            Console.WriteLine("                   ⚠ `--debug-asm` / `--debug-steps` 是**另一条更早的分支**,本来就只在虚拟机里单步 ⇒");
            Console.WriteLine("                     那两条与 `--跑法 原生` 一起写时,单步照旧走虚拟机(这条路是性能档,不带断点/单步)");
            Console.WriteLine("  --冻结秒 N       与 --run 同时:把 `当前时间()` 与 `当前毫秒()` 都冻住(N = Unix 秒;两条跑法都认)。");
            Console.WriteLine("                   想让输出可复现、或者要拿两条跑法逐字节对拍时用(不冻就是本机真时间)");
            Console.WriteLine("  --跨进程         与 --run 同时:把『跑程序』那一步放到**子进程**里跑 —— 虚拟机 / 宿主能力 /");
            Console.WriteLine("                   图形宿主都在那一边,引擎或宿主崩了不会带走这条命令(子进程 = 再起一份自己);");
            Console.WriteLine("                   图形命令打在**这一侧**的标准输出上(与不带 --跨进程 逐行一致,见帮助《上手》第25节)");
            Console.WriteLine("  --无头           与 --run 同时:图形原语(打开窗口/画文字/刷新…)按同一份格式**逐条打在标准输出上**");
            Console.WriteLine("  --弹窗           与 --run 同时:允许 `提示框` / `选文件` / `另存文件` **真的弹窗**(缺省**不弹**,给『不支持』)。");
            Console.WriteLine("                   ⚠ 缺省关着是有意的:没人点的模态框会把这一趟永远挂住(自检与脚本都会踩)");
            Console.WriteLine("                   ⇒ 只有『人在终端前看着』时才写这一条;剪贴板那几条**不受它影响**(本来就不用点)");
            Console.WriteLine("  --事件脚本 路径   与 --run 同时:按这份脚本**按帧喂**键盘 / 鼠标 / 关窗事件(R1-6 批 2;不开窗也能跑事件循环;");
            Console.WriteLine("                   与 --跨进程 一起用也认:脚本随装载帧交给子进程,由那一侧按帧喂)");
            Console.WriteLine("                   脚本一行一条:`帧数 4` + `帧 0 按键 回车` / `帧 1 移动 100 60` / `帧 2 按下 鼠标左键 10 40`");
            Console.WriteLine("                   帧号口径:打开窗口 = 第 0 帧,每 `刷新()` 一次进下一帧;跑满 `帧数` 帧投关窗请求(详见帮助《上手》第25节)");
            Console.WriteLine("                   (R1-6 批 1;hbcc 本来就没有窗口,这个写法是把这个口径显式点出来,见帮助《上手》第25节)");
            Console.WriteLine("  --debug-asm [N]   脚本化调试:按指令单步 N 步,每步打印 PC/文件:行/调用处");
            Console.WriteLine("  --debug-steps [N] 脚本化调试:按源码行走 N 步(同一行的多条指令一次走完)");
            Console.WriteLine("  --in 路径         仅配合 --run:把文件字节作为 IN 输入流");
            Console.WriteLine("  --内存 [短]       配合 --run:跑完之后打一张【内存缩略图】—— 每段的占用条 / 非零字占比 / 峰值地址 /");
            Console.WriteLine("                   热点格 / 每段头一页的原始字;写 `短` 就只出段表与热点。段表与 `--stats` 那份同一个来源");
            Console.WriteLine("  --selftest       运行引擎综合自检(不需要输入文件);结尾会打印断言条数与清单指纹");
            Console.WriteLine("  --更新断言清单   跑一遍自检并把断言清单写进 工具\\自检断言清单.txt(自检会拿它比对,有增删就红灯)");
            Console.WriteLine("  --乱源 N         随机乱源轰炸 N 例(固定种子):编译器只许报「说人话的编译错误」,不许自己炸;");
            Console.WriteLine("                   随后跑一遍**深度维**(长链 / 深括号 / 块嵌套 / 否则如果长链 / 思维导图链,一格一个子进程比退出码)");
            Console.WriteLine("  --导图 文件      跑一遍思维导图那条链(扫结构 → 声明线 → 分区),把统计打出来;嵌套太深等编译错误给退出码 1");
            Console.WriteLine("  --stats          打印规模统计(指令/关键字/示例/各目录行数;不含自检条数)");
            Console.WriteLine("  --更新报告       配合 --stats:跑一遍自检,并把统计表写进 全面测评报告.md(只改标记之间那段)");
            Console.WriteLine("  --生成帮助       把帮助站点重新生成到 帮助\\(首页 + 一个话题一页,零依赖静态网页)");
            Console.WriteLine("  --更新指令集     R3-6-1:把 ISA 真值源(55 条的名字 / 操作码 / 格式 / 示例 / 文档语义 / 语义落点)");
            Console.WriteLine("                   重新写进 工具\\指令集真值源.txt(自检会拿它逐字节比对,改了指令表忘了跑就红灯)");
            Console.WriteLine("  --清单           校验 任务清单.md 的结构(每条记录的支脉/种类/状态/递进;有问题给行号,退出码 1)");
            Console.WriteLine("  --清单 --索引    重新生成任务清单里那一节四张表(支脉 / 种类 / 批序 / 显式递进链;只改标记之间)");
            Console.WriteLine("  --清单 --查 词   查条目:词可以是 节编号 / 支脉 A / 支脉号 A9 / 批号 108 / 种类 / 状态 / 关键字");
            Console.WriteLine("  --查找 内容      查找/替换(P21):文件来自 -p 工程 或命令行给的 .hb;不给 --换成 就只找,打印每处 文件:行:列");
            Console.WriteLine("  --换成 内容      配合 --查找:算出替换清单并打印预览(默认**不写盘** —— 加 --写回 才真改)");
            Console.WriteLine("  --正则           配合 --查找:要找的内容按正则解释(H5;与 IDE 的 Ctrl+F 面板同一套口径),换成里可写 $1 展开分组");
            Console.WriteLine("                   边界:命中跨行的替换不做(当场报一条);超时护栏 3 秒(防灾难性回溯)");
            Console.WriteLine("  --区分大小写 / --整词 / --连注释一起 / --带行 / --只看 文件 / --写回   上一条的选项(默认保护注释与字符串)");
            Console.WriteLine("  --bench [N]      虚拟机测速:跑 N 次自增循环,报每秒多少条指令(看 Release/Debug 快 7~13 倍)");
            Console.WriteLine("  --make-project 目录  给这个目录生成工程文件(.hbp):目录里的 .hb 进文件表,主程序.hb 当启动文件");
            Console.WriteLine("  --排版           代码排版(C5):按块深度重排缩进 + 修行尾空白;文件来自 -p 工程 或命令行给的 .hb");
            Console.WriteLine("                   默认**只预览不写盘**(加 --写回);只动空白,注释/字符串一个字符不碰,编译结果逐字节不变");
            Console.WriteLine("  --缩进 N / --制表符   配合 --排版:一档几个空格(默认 4)/ 用制表符缩进");
            Console.WriteLine("  --片段库         列出内置代码片段骨架(函数/如果/当/循环/遍历/选择/结构/测试…),`◆` = 插完光标停这儿");
            Console.WriteLine("  --改名文件 旧 新  整份文件改名(C6):磁盘改名 + 工程文件(.hbp)条目 + 别的文件里 包含 \"…\" 的路径");
            Console.WriteLine("  --快捷键         列出 IDE 的快捷键表(动作 / 默认键 / 现在生效的键);改键位在 IDE 的选项页里改");
            Console.WriteLine("                   默认**只预览不写盘**(加 --写回);不带目录的新名 = 改在旧文件旁边,带目录 = 相对 -p 工程目录");
            Console.WriteLine("  --fix-semicolon 文件  老代码迁移:语言语句缺结尾的 ';' 就补上(.hb;.cs 只改里面的源码字符串)");
            Console.WriteLine("  --exe 路径       生成独立可执行程序(缺省 = 复制 HB 宿主 + 贴机器码;打包方式见 --打包)");
            Console.WriteLine("  --方式 C         配合 --exe:走 C 后端 —— 机器码转成 C 源码再交本机 C 编译器(clang/gcc/cl)编,产物**不带 .NET**");
            Console.WriteLine("  --打包 规格      R3-6-4 批 4:产物打成什么(与 IDE 工具栏那个下拉同一份事实源;缺省 " + 打包规格C.缺省 + ")");
            foreach (string 行 in 打包规格C.表().Split(Environment.NewLine)) Console.WriteLine("                  " + 行);
            Console.WriteLine("  -h, --help       用法说明");
            Console.WriteLine("退出码: 0=成功 1=编译错误/运行期错误/跑完但没通过(断言失败、被运行期守卫拦下) 2=用法错误");
        }
    }
}
