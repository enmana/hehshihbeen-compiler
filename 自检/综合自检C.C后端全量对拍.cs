using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R3-6-4 批 2:**全量对拍** —— 把 `Sample\**` 下的**每个标准工程**两头都真跑一遍:
    /// 一头是 **C# 虚拟机**,另一头是 **C 后端编出的原生 exe**,比
    /// ① **寄存器 R0~R15** ② **整块运行内存逐字节** ③ **程序输出逐字节 + 退出码**。
    ///
    /// 为什么值得单开一组(批 1 不是已经比对了吗):批 1 只比了**程序输出**——
    /// 那只等于比了个尾巴。两份实现真漂开的地方(某条指令语义写错、某个地址算错)
    /// **往往不影响输出**,只在寄存器 / 内存里露出来(本组开工第一遍就抓出一条:
    /// `取字帧` / `存字帧` 的帧偏移、`比较取值` 的有符号位)。所以"全量"= 连机器里的状态一起比。
    ///
    /// 几条口径(不比才诚实):
    ///   · **PC 只要求"指向同一条指令"**(差值 0 或 4):虚拟机跑到 `停止` 时 PC 已经 +4,
    ///     C 侧停在 `停止` 那一条上;
    ///   · **SP 不比**:C 侧把 `压栈/弹栈` 编译成了 C 自己的调用栈,HB 内存末尾那 4 个字节一个字没动过;
    ///   · **报了运行期错误的工程**只比"两边报的是同一类错 + 退出码",不比出错那一刻的寄存器
    ///     (报错点本来就允许差一条指令,拿它当差异是假差异);
    ///   · **用了宿主能力的工程**(0x31)批 2 那会儿的 C 后端还不支持、如实记跳过;**批 3 起真编真跑了** ——
    ///     C# 虚拟机这一侧要挂上 `宿主能力C.处理`(命令行 / IDE 也是这么挂的),
    ///     并且**两边把 `当前时间()` 的钟冻结成同一个秒**(`宿主能力C.取当前秒` / exe 的 `--hb-now`):
    ///     那个秒会写进运行内存,不冻结就等于白白差 4 个字节。
    ///
    /// 另一条腿是**分派兜底**:`调用寄存器` / `跳转寄存器` / `返回` 的目标在寄存器里,
    /// 编译器支持 computed goto 就走标签表,不支持就走 `switch(PC)`。
    /// 自检拿 `-DHB_COMPUTED_GOTO=0`(`编成exe带选项`,参数而不是环境变量 —— 这台机器上
    /// 自定义环境变量送不到子进程)逼着同一份 C 源码走一遍兜底支路,
    /// 再拿编出来的 exe 自己报的"我走的是哪条"(stderr 第一行)钉住 ——
    /// 光看源码里有没有 `switch` 是看不出"这条支路到底编得动、跑得对"的。
    /// 这台机器只有 MSVC(自己就走 switch),所以"两条路都换过"不强求、只如实报(`试分派兜底`)。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary>
        /// 每个工程要真编真跑的断言名(`C后端真跑断言` 那几条之外,本组自己这几条也按它记跳过)。
        /// **"两条路都在"那一条不在表里** —— 它只读 C 源码,不吃 C 编译器,本组开头就记了它一次;
        /// 放进这张表会让它在没有 C 编译器的机器上**记两遍**(一次通过 + 一次跳过),
        /// 撞上 `自检 断言名唯一` 那条钉子(本批实测:2448 条 / 2449 次)。
        /// </summary>
        private static readonly string[] C后端全量断言 =
        [
            "C后端 全量对拍 标准工程全过(寄存器 / 内存 / 输出逐字节一致)",
            "C后端 全量对拍 逐工程:寄存器与内存逐字节一致",
            "C后端 全量对拍 分派兜底:switch(PC) 支路真编真跑,输出与标签表那一条一致",
            "C后端 全量对拍 每个真跑过的 exe 都自报了分派方式(runtime 里那句只在导出状态时才说)",
            "C后端 全量对拍 图形工程:转译产物认 `--hb-图形` / `--hb-事件脚本`(两边同挂无头呈现,逐字节一致)",
        ];

        /// <summary>
        /// 对拍时把 `当前时间()` 冻结在这个秒(2026-09-22 前后,`Sample` 里那两条 `> 1700000000` 的
        /// 判断照样成立)。**两边必须吃同一个值**,不然"整块内存逐字节"这条判据必然误报。
        /// </summary>
        private const long 冻结秒 = 1_790_000_000;

        /// <summary>
        /// 对拍时把 `当前毫秒()`(宿主功能号 55,单调钟)冻结在这个刻度。
        ///
        /// <para>**为什么非要冻**:`当前毫秒()` 是"开机以来的毫秒",C# 侧与原生侧各读各的机器钟 ——
        /// 而它一旦被某个库落进运行内存(局部变量槽),整机逐字节比就必然差那几个字节。
        /// 实测(`示例51_异步与同步`:`异步.等待任务` / `同步.等到` 里那一句 `整数 起 = 当前毫秒()`):
        /// 两侧内存第一个不同的地址就是那个槽。两侧本来就有 `--hb-now-ms` 这个开关
        /// (`C后端\解释器核.c`),这一组照 `取当前秒` 那一套把它接上。</para>
        /// </summary>
        private const uint 冻结毫秒 = 1_000_000;

        private static void C后端全量对拍自检()
        {
            const int 内存 = 虚拟机C.默认内存大小;

            // ── 一、纯转译那半:两条分派路都在源码里(不需要 C 编译器) ──
            try
            {
                汇编调试结果C 最小编译 = 编译("整数 结果 = 42;\n输出(结果);\n输出换行;\n停止\n");
                string c源 = C后端C.转译(最小编译, 内存).源码;
                bool 标签表 = c源.Contains("goto *hb_labels[hb_pc >> 2]", StringComparison.Ordinal)
                    && c源.Contains("&&L_0", StringComparison.Ordinal);
                bool 兜底 = c源.Contains("switch (hb_pc >> 2)", StringComparison.Ordinal)
                    && c源.Contains("case 0u: goto L_0;", StringComparison.Ordinal)
                    && c源.Contains("default: hb_bad_pc(hb_pc, HB_CODE_LEN); break;", StringComparison.Ordinal);
                string 运行期 = C后端C.取运行时源码();
                bool 运行期认命令行 = 运行期.Contains("#ifndef HB_COMPUTED_GOTO", StringComparison.Ordinal);
                记录("C后端 全量对拍 分派兜底:C 源码里两条路都在(有 computed goto 走标签表,没有走 switch(PC))",
                    标签表 && 兜底 && 运行期认命令行,
                    $"标签表 = {标签表};switch(PC) 兜底 = {兜底};运行期让命令行覆盖 = {运行期认命令行}");
            }
            catch (Exception ex) { 记录异常("C后端 全量对拍 分派兜底:C 源码里两条路都在", ex); }

            // ── 二、真编真跑那半 ──
            var 工具 = C工具链C.探一下();
            if (!工具.成功)
            {
                string 理由 = "这台机器上没有能用的 C 编译器 —— " +
                    (工具.找不到原因.Length > 0 ? 工具.找不到原因 : 工具.日志);
                foreach (string 名 in C后端全量断言) 跳过(名, 理由);
                return;
            }

            // 兜底支路:逼着同一份 C 源码走 `-DHB_COMPUTED_GOTO=0`,拿编出来的 exe 自报走哪条路
            try
            {
                var (成, 说明) = 试分派兜底();
                记录("C后端 全量对拍 分派兜底:switch(PC) 支路真编真跑,输出与标签表那一条一致", 成, 说明);
            }
            catch (Exception ex) { 记录异常("C后端 全量对拍 分派兜底", ex); }

            // ── 三、标准工程全量对拍 ──
            var 全部工程 = 找全部工程文件().ToList();
            // 调一条工程时不必等全量(整组在本机实测十几分钟):`HB_对拍只跑=示例59` 就只跑名字里含它的那几个。
            // 与 `HB_自检只跑` 同一个思路(全量一轮太贵,定位一条差异时只想跑那一条)。
            if (Environment.GetEnvironmentVariable("HB_对拍只跑") is { Length: > 0 } 只跑 && 只跑.Trim().Length > 0)
            {
                string 要 = 只跑.Trim();
                var 挑出来 = 全部工程.Where(p => Path.GetFileName(p).Contains(要, StringComparison.Ordinal)).ToList();
                if (挑出来.Count > 0) 全部工程 = 挑出来;
            }
            if (全部工程.Count == 0)
            {
                跳过("C后端 全量对拍 逐工程:寄存器与内存逐字节一致", "找不到 Sample 目录(不在工作副本里跑)");
                跳过("C后端 全量对拍 标准工程全过(寄存器 / 内存 / 输出逐字节一致)", "找不到 Sample 目录");
                return;
            }

            var 各工程 = new ConcurrentDictionary<string, string>();
            int 编过 = 0, 分派说过话 = 0, 图形工程跑过 = 0;
            long 毫秒 = 0;
            // R4-2(2026-10):**分段记账**。这一组是头号耗时(实测 258 秒 / 全程 476 秒),
            // 但"哪一段吃掉的"以前只能猜。三段各记各的(跨线程用 Interlocked 累加):
            //   ① 编译 + 转译(C# 侧,吃 CPU)  ② `cl /Od` 编 C 源码(外部进程)  ③ 跑原生 exe
            long 毫秒编 = 0, 毫秒C = 0, 毫秒跑 = 0;
            // 门开多大:按 `ProcessorCount` 来,夹在 [4, 8]。
            // 下限 4 = 老口径,不倒退;上限 8 = **实测选出来的**(这台机器 12 逻辑核 / 32GB):
            //   8 路 → 该组 101 秒;12 路 → 117 秒(反而更慢:`cl.exe` 之间抢 CPU,连"跑 exe"那一段
            //   都被拖长 1.8 倍)⇒ 上限就压在 8,别再往上加。
            // ⚠ 这把门**不是**并行的决定因素:真正的并行度在 `C工具链C.编成exe` 那把锁上
            //   (R4-2 之前它把整趟编译都锁着,门开到 8 也只有 1 路 —— 实测总时长只快 2%)。
            int 并发 = Math.Clamp(Environment.ProcessorCount - 1, 4, 8);
            // H69:先认一遍"哪几个是图形标准工程"—— 它们要**一个一个跑**(见 `用图形标准工程`);
            // R3-6-5 批 2 起这一组也给它们挂无头呈现,于是同样要串起来(理由见 `图形对拍门`)。
            string[] 图形工程们 = 全部工程.Where(用图形标准工程).ToArray();
            // 批 3:两边把钟冻在同一个秒(见 `冻结秒` 的说明);跑完恢复成真时间
            宿主能力C.清运行痕迹();
            宿主能力C.取当前秒 = () => 冻结秒;
            宿主能力C.取当前毫秒 = () => 冻结毫秒;
            try
            {
                using (var 闸 = new SemaphoreSlim(并发))
                {
                    var 任务 = 全部工程.Select(路径 => Task.Run(() =>
                    {
                        闸.Wait();
                        bool 图形工程 = 用图形标准工程(路径);
                        bool 句柄工程 = 用句柄标准工程(路径);      // G3 域 6:流 / 网络那两张句柄表是进程级的
                        if (图形工程) Monitor.Enter(图形对拍门);
                        if (句柄工程) Monitor.Enter(句柄对拍门);
                        try
                        {
                            // G3 域 6:句柄表先压回"刚启动"(原生侧每个工程都是新进程,这边一个进程连跑)
                            if (句柄工程) 宿主能力C.清句柄表();
                            var 表 = Stopwatch.StartNew();
                            string 结论 = 对拍一个工程(路径, out bool 真编过, out bool 分派已跑, out bool 图形跑过,
                                out long 段编, out long 段C, out long 段跑);
                            表.Stop();
                            Interlocked.Add(ref 毫秒编, 段编);
                            Interlocked.Add(ref 毫秒C, 段C);
                            Interlocked.Add(ref 毫秒跑, 段跑);
                            lock (各工程)
                            {
                                各工程[Path.GetFileName(路径)] = 结论;
                                if (真编过) 编过++;
                                if (分派已跑) 分派说过话++;
                                if (图形跑过) 图形工程跑过++;
                                毫秒 += 表.ElapsedMilliseconds;
                            }
                        }
                        finally
                        {
                            闸.Release();
                            if (句柄工程) Monitor.Exit(句柄对拍门);
                            if (图形工程) Monitor.Exit(图形对拍门);
                        }
                    })).ToArray();
                    Task.WaitAll(任务);
                }
            }
            finally { 宿主能力C.清运行痕迹(); }        // 时钟也一起还原成真时间

            int 不一致 = 各工程.Count(x => x.Value.Length > 0);
            var 坏 = 各工程.Where(x => x.Value.Length > 0).Take(4)
                .Select(x => $"{x.Key}: {x.Value}").ToArray();
            记录("C后端 全量对拍 逐工程:寄存器与内存逐字节一致",
                不一致 == 0 && 编过 == 全部工程.Count && 编过 > 0,
                $"真编真跑 {编过} 个 / 共 {全部工程.Count} 个;" +
                $"不一致 {不一致} 个" + (坏.Length > 0 ? ";" + string.Join(" ; ", 坏) : "") +
                $";合计 {毫秒} 毫秒(并行 {并发} 路,关优化编);" +
                $"分段:编译+转译 {毫秒编} / 编C {毫秒C} / 跑exe {毫秒跑} 毫秒(各段之和 = 各工程串行总账)");
            记录("C后端 全量对拍 标准工程全过(寄存器 / 内存 / 输出逐字节一致)",
                不一致 == 0 && 编过 == 全部工程.Count && 编过 > 0,
                $"标准工程 {全部工程.Count} 个**一个不落**真编真跑、一字不差" +
                $"(批 2 里那两条用宿主能力的工程 —— 示例12_输入输出与宿主能力 / 综合_核心与边界 —— " +
                $"批 3 起也真跑了,不再记跳过)");

            // 真跑过的那些 exe 都该自报"我走的是哪条分派路"(分派那条每次 `返回` 都要过,没有例外)
            记录("C后端 全量对拍 每个真跑过的 exe 都自报了分派方式(runtime 里那句只在导出状态时才说)",
                分派说过话 == 编过 && 编过 > 0,
                $"真编真跑 {编过} 个里 {分派说过话} 个自报了分派方式;没自报的那个要么没跑起来、要么分派路没接上");

            // R3-6-5 批 2:图形标准工程这一档走的是**新口径** —— 转译产物也认 `--hb-图形` / `--hb-事件脚本`,
            // 两边同挂无头呈现、同喂工程目录那份事件脚本;一个都没跑成这一条就是红的(别让它"悄悄跳过")。
            记录("C后端 全量对拍 图形工程:转译产物认 `--hb-图形` / `--hb-事件脚本`(两边同挂无头呈现,逐字节一致)",
                图形工程跑过 == 图形工程们.Length && 图形工程们.Length > 0,
                $"图形标准工程 {图形工程们.Length} 个(" +
                (图形工程们.Length > 0 ? string.Join("/", 图形工程们.Select(Path.GetFileNameWithoutExtension)) : "一个也没有") +
                $"),其中**真编真跑且结论一致**的 {图形工程跑过} 个" +
                (图形工程们.Length == 0 ? " —— 一个图形标准工程都没认出来,判据没落地" : ""));
        }

        /// <summary>
        /// 分派兜底那一条的真跑:**同一份 C 源码编两遍** —— 一遍按编译器默认(这台机器上是哪条就是哪条),
        /// 一遍加 `-DHB_COMPUTED_GOTO=0` 逼它走 `switch(PC)`。
        /// 判据:① 强制那份**真的报了**"走 switch(PC)"(光看源码里有没有 `switch` 不算数);
        /// ② 退出码与 stdout 逐字节相同;③ 两次落下的**整机状态转储逐字节相同**。
        ///
        /// **"两条路都真换过"这一条不强求**:这台开发机上只有 MSVC(自己就走 `switch(PC)`),
        /// 那两份 clang 是 Android NDK 的、编不出 Windows exe ⇒ 两条路是同一个样子。
        /// 真换了就报"两条路都跑过";没换就**如实说清"只有兜底这一条真跑过、标签表那条要有 clang / gcc 的机器"**,
        /// 不许把"只跑了一条"讲成"两条都验过"。
        /// </summary>
        private static (bool 成, string 说明) 试分派兜底()
        {
            const string 短源 = "方法 步(整数 n) 返回 整数\n{\n    如果 (n <= 0)\n    {\n        返回 0;\n    }\n    返回 n + 步(n - 1);\n}\n输出(步(10));\n输出换行;\n停止\n";
            var 编译结果 = 编译(短源);
            string 暂存 = 目录("分派兜底");
            string c源 = Path.Combine(暂存, "兜底.c");
            string exe = Path.Combine(暂存, "兜底.exe");
            File.WriteAllText(c源, C后端C.转译(编译结果, 虚拟机C.默认内存大小).源码, new UTF8Encoding(false));

            var 默认 = C工具链C.编成exe(c源, exe);
            if (!默认.成功) return (false, "默认那条就没编过:" + 截断(默认.日志, 200));

            string 状态一 = Path.Combine(暂存, "默认.状态");
            var (码一, 出一, 误一) = 跑原生带状态(exe, null, 状态一);
            string 方式一 = 取分派方式(误一);

            string c源二 = Path.Combine(暂存, "兜底_switch.c");
            string exe二 = Path.Combine(暂存, "兜底_switch.exe");
            File.Copy(c源, c源二, overwrite: true);
            string 状态二 = Path.Combine(暂存, "兜底_switch.状态");
            int 码二; byte[] 出二; string 误二;
            var 编二 = C工具链C.编成exe带选项(c源二, exe二, ["-DHB_COMPUTED_GOTO=0"]);
            if (!编二.成功) return (false, "强制 switch 那条没编过:" + 截断(编二.日志, 220));
            (码二, 出二, 误二) = 跑原生带状态(exe二, null, 状态二);

            string 方式二 = 取分派方式(误二);
            bool 真走了兜底 = 方式二.Contains("switch(PC)", StringComparison.Ordinal);
            bool 换路了 = 方式一 != 方式二;
            bool 输出同 = 码一 == 码二 && 出二.AsSpan().SequenceEqual(出一);
            bool 状态同 = 状态一 != null && 状态二 != null
                && File.Exists(状态一) && File.Exists(状态二)
                && File.ReadAllBytes(状态一).AsSpan().SequenceEqual(File.ReadAllBytes(状态二));
            string 路径话 = 换路了
                ? $"两条路都跑过(默认 [{方式一}] / 强制 [{方式二}])"
                : $"这台机器的编译器自己就走 switch(PC)(默认 [{方式一}])," +
                  "所以只有兜底这一条真跑过 —— 标签表那条要在有 clang / gcc 的机器上才验得到" +
                  "(本机那两份 clang 是 Android NDK 的,编不出 Windows exe;见 `未做任务清单.md` §21)";
            return (真走了兜底 && 输出同 && 状态同,
                $"{路径话};退出码 {码一}/{码二};输出 {出一.Length}/{出二.Length} 字节一致 = {输出同};" +
                $"整机状态逐字节一致 = {状态同}");
        }

        /// <summary>
        /// 一个标准工程的对拍:编译 → 虚拟机跑一遍收状态 → 转译 C → 编 → 跑 → 逐字节比。
        /// 返回**空串 = 一致**;非空 = 差异说明(给记录用)。
        /// </summary>
        private static string 对拍一个工程(string 工程路径, out bool 真编过, out bool 分派已跑, out bool 图形跑过)
            => 对拍一个工程(工程路径, out 真编过, out 分派已跑, out 图形跑过, out _, out _, out _);

        /// <summary> 同上,外加**三段耗时**(编译+转译 / 编 C / 跑 exe)—— R4-2 用来定位这一组的时间去哪了 </summary>
        private static string 对拍一个工程(string 工程路径, out bool 真编过, out bool 分派已跑, out bool 图形跑过,
            out long 毫秒编, out long 毫秒C, out long 毫秒跑)
        {
            真编过 = false; 分派已跑 = false; 图形跑过 = false;
            毫秒编 = 0; 毫秒C = 0; 毫秒跑 = 0;
            string 名 = Path.GetFileNameWithoutExtension(工程路径);
            var 工程 = 工程文件C.加载(工程路径);
            if (工程.文件.Count == 0) return "空工程";
            int 内存; try { 内存 = 工程.内存字节数; } catch { 内存 = 虚拟机C.默认内存大小; }
            int 堆; try { 堆 = 工程.堆字节数; } catch { 堆 = 中间层C.默认堆大小; }
            int 池; try { 池 = 工程.池字节数; } catch { 池 = 中间层C.默认池大小; }
            int 软栈; try { 软栈 = 工程.软栈字节数; } catch { 软栈 = -1; }

            var 编表 = Stopwatch.StartNew();
            var 编译结果 = new 汇编器C().汇编多文件带调试(工程.读全部编译输入(), true, 堆, null, 池, 内存, 软栈);

            // 批 3 起宿主能力也能编了(转译期不再拦);真出转译错误就是真差异,别咽下去
            try { C后端C.转译(编译结果, 内存); }
            catch (C后端错误C ex) { return "转译期就编不动:" + 截断(ex.Message, 200); }
            真编过 = true;
            毫秒编 = 编表.ElapsedMilliseconds;

            string 暂存 = 目录(名);

            // R3-6-5 批 2:**图形标准工程这一档改口径**(用户 2026-10-01 拍板取 a)——
            // 转译产物也带图形了,于是 C# 这一侧**也要挂无头呈现 + 喂同一份事件脚本**,
            // 两边走同一条无头路。为什么非挂不可:图形工程的主循环是"收不到事件就接着刷",
            // C 侧真开窗(状态 0)、C# 侧"没挂呈现口"(状态 1)⇒ 寄存器 / 执行条数 / 内存整块分岔,
            // 不给脚本还会两边一起转到天荒地老(R3-6-3 坑三、H69 同一个病)。
            // 判据用 `用图形标准工程`(源码里写没写 `打开窗口`):与解释器核那一组**同一把尺子**。
            bool 用图形 = 用图形标准工程(工程路径);
            string? 图形脚本路径 = 用图形 ? 读工程事件脚本(工程路径, 暂存) : null;
            string 图形落点 = Path.Combine(暂存, 名 + ".图形");
            if (用图形)
            {
                string 脚本文本 = File.ReadAllText(图形脚本路径!, Encoding.UTF8);
                try
                {
                    图形宿主C.清空();
                    图形宿主C.挂上(new 无头呈现C(TextWriter.Null));   // 命令不用留:这一组比的是状态与程序输出
                    图形宿主C.挂事件脚本(事件脚本C.解析(脚本文本));
                }
                catch (脚本错误C ex)
                {
                    return "图形宿主挂不起来(工程里的 `事件脚本.txt` 写错了?):" + 截断(ex.Message, 140);
                }
            }

            // ① 虚拟机侧:原样收程序输出字节,跑完导出整机状态。
            //    宿主能力照命令行 / IDE 那一套挂上(不然那两条用宿主能力的工程两边根本不是一回事)
            var 机 = new 虚拟机C();
            机.设内存大小(内存);
            机.初值表 = 编译结果.初值表;
            机.宿主调用 = 宿主能力C.处理;
            var 出 = new MemoryStream();
            机.输出目标 = b => 出.WriteByte(b);
            string 机报错 = "";
            机.装载(编译结果.机器码);
            try { while (机.单步()) { } }
            catch (Exception ex) { 机报错 = ex.Message; }
            var 机状态 = C状态C.导出虚拟机状态(机, 出, 编译结果.代码字节数, 编译结果.数据字节数);

            // ② C 侧:转译 → 编 → 跑(顺手把"这一次走哪条分派路"从 stderr 收下来)
            string c源 = Path.Combine(暂存, 名 + ".c");
            string exe = Path.Combine(暂存, 名 + ".exe");
            string 状态路径 = Path.Combine(暂存, 名 + ".状态");
            File.WriteAllText(c源, C后端C.转译(编译结果, 内存).源码, new UTF8Encoding(false));

            // 关优化编:`-O2` 编那两份 2.5MB 的浮点示例要几分钟(实测 16.5 秒 → 5 分钟以上),
            // 而这一组的判据是**语义**(寄存器 / 内存 / 输出逐字节),与优化档无关。
            var C表 = Stopwatch.StartNew();
            var 编 = C工具链C.编成exe(c源, exe, 关优化: true);
            毫秒C = C表.ElapsedMilliseconds;
            if (!编.成功) return "C 编译器没编过:" + 截断(编.日志, 200);

            var 跑表 = Stopwatch.StartNew();
            var (退出码, 原生出, 原生误) = 跑原生带状态(exe, null, 状态路径, 冻结秒,
                图形: 用图形 ? 图形落点 : null, 事件脚本: 图形脚本路径, 现在毫秒: 冻结毫秒);
            毫秒跑 = 跑表.ElapsedMilliseconds;
            图形跑过 = 用图形;
            string 方式 = 取分派方式(原生误);
            分派已跑 = 方式.Length > 0;

            var 原生状态 = C状态C.读原生状态(状态路径);
            if (原生状态 == null) return "原生 exe 没落下状态转储(它自己没跑起来?)";

            // ③ 比:报错了就只比"同一类错 + 退出码";正常跑完才比寄存器 / 内存 / 输出
            if (机报错.Length > 0)
            {
                string 关键词 = 错关键词(机报错);
                if (退出码 == 0) return $"虚拟机报「{截断(机报错, 60)}」,原生却退出码 0";
                if (关键词.Length == 0 || !原生误.Contains(关键词, StringComparison.Ordinal))
                    return $"两边报的错对不上:虚拟机「{截断(机报错, 60)}」/ 原生「{截断(原生误, 60)}」";
                if (!机状态.输出.AsSpan().SequenceEqual(原生出)) return "报错前的程序输出不一致";
                return "";
            }

            机状态.退出码 = 0;
            // G3 域 5:`示例31_异步` 那一档**不比"执行条数 / 最近路径"** —— 它靠
            // `当 (任务就绪(…) == 0) { }` 等后台线程,问了几轮由线程快慢决定(口径与理由见
            // `C状态C.比` 的 `不比条数`);寄存器 / 内存 / 输出照旧逐字节比。
            bool 异步工程 = 名.StartsWith("示例31_异步", StringComparison.Ordinal);
            var 比 = C状态C.比(机状态, 原生状态, 原生出, 退出码, 不比条数: 异步工程);
            // G3 流的**那一格例外**(与 `综合自检C.C后端宿主能力.比内存只放行短读一格` 同一把尺子,
            // 口径与理由写在那个方法上):标准工程 `示例27_流` 用了 `读一块`,那一格记的是
            // "上一次 `Read` 的短读字节数" —— `FileStream.Read` 与 `fread` 的切法天生不同。
            bool 过 = 比.一致;
            if (!过 && 名.StartsWith("示例27_流", StringComparison.Ordinal))
            {
                // G3 流的**那一格例外**(口径与理由见 `综合自检C.比内存只放行短读一格` 的注释):
                // `读一块` 那一格记的是"上一次 `Read` 的短读字节数",`FileStream.Read` 与 `fread` 的切法
                // 天生不同 ⇒ 池水位与缓冲指针跟着错开。**行为那一半照旧比**。
                过 = 机报错.Length == 0
                    && 综合自检C.比内存只放行短读一格(机状态, 原生状态, 允许级联: true).过;
            }
            // 图形那一档跑完把进程级那几格状态**收干净**:别的组(或下一个工程)进来时看到的是"没挂呈现口",
            // 与"这一组没跑过"同形(H69 那一课的正面口径:谁挂的谁收)。
            if (用图形)
            {
                try { 图形宿主C.清空(); 图形宿主C.挂上(null); } catch { }
            }
            return 过 ? "" : 比.说明;
        }

        /// <summary>
        /// 运行期错误里的"是什么错":取说明那一段的头一小截。
        /// 两边的**文案不一样**(C 侧是 `hb_fail` 那句、虚拟机那侧是异常消息),
        /// 所以只比"病因"——`除零` / `栈为空` / `内存越界` 这种关键词必须两边都对得上。
        /// </summary>
        private static string 错关键词(string 报错)
        {
            string 文本 = 报错.Replace("运行期错误", "", StringComparison.Ordinal);
            int 分隔 = 文本.IndexOf("): ", StringComparison.Ordinal);
            if (分隔 >= 0) 文本 = 文本[(分隔 + 3)..];
            else
            {
                int 冒号 = 文本.IndexOf(':');
                if (冒号 < 0) 冒号 = 文本.IndexOf(':');      // 两边冒号都认
                if (冒号 >= 0) 文本 = 文本[(冒号 + 1)..];
            }
            int 括号 = 文本.IndexOf('(');
            if (括号 < 0) 括号 = 文本.IndexOf('(');
            if (括号 > 0) 文本 = 文本[..括号];
            文本 = 文本.Trim();
            return 文本.Length > 6 ? 文本[..6] : 文本;
        }

        /// <summary>原生 exe 报的"这一次走哪条分派路"(它只在开了状态导出时才说,见 `hb_say_dispatch`)</summary>
        private static string 取分派方式(string 标准错误)
        {
            foreach (string 行 in 标准错误.Replace("\r\n", "\n").Split('\n'))
            {
                string 净 = 行.Trim();
                if (净.StartsWith("C后端分派:", StringComparison.Ordinal)) return 净;
            }
            return "";
        }

        /// <summary>自检用的临时目录(一个工程一个子目录,免得并发时互相盖)</summary>
        private static string 目录(string 名)
        {
            string 暂存 = Path.Combine(临时区C.基目录(), "hbcc_C后端全量", Environment.ProcessId.ToString(), 干净名(名));
            Directory.CreateDirectory(暂存);
            return 暂存;
        }

        /// <summary>
        /// 起原生 exe、把整机状态导到 `状态路径`(命令行参数 `--hb-dump`),拿(退出码, stdout **字节**, stderr 文本)。
        /// `现在` 非 null 时再补一条 `--hb-now <秒>` 把 `当前时间()` 冻住(批 3;见 `冻结秒` 的说明);
        /// `现在毫秒` 非 null 时补一条 `--hb-now-ms <毫秒>` 把 `当前毫秒()` 冻住(见 `冻结毫秒` 的说明)。
        /// `镜像` 非 null 时把它作为**第一个位置参数**递过去(R3-6-2:纯 C 解释器核要吃一份镜像文件,
        /// 参数名与转译产物故意一致,所以两头共用这一个起进程的助手)。
        /// `图形` / `事件脚本` 非 null 时递 R3-6-3 那两个开关(挂无头呈现的口 / 按帧喂的脚本)——
        /// 只有解释器核认它们,转译产物那边永远不传。
        /// </summary>
        private static (int 退出码, byte[] 出, string 误) 跑原生带状态(string exe, string? 标准输入, string? 状态路径,
            long? 现在 = null, string? 镜像 = null, string? 图形 = null, string? 事件脚本 = null,
            uint? 现在毫秒 = null)
        {
            var 起 = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            if (镜像 != null) 起.ArgumentList.Add(镜像);
            if (状态路径 != null)
            {
                try { if (File.Exists(状态路径)) File.Delete(状态路径); } catch { }
                起.ArgumentList.Add("--hb-dump");
                起.ArgumentList.Add(状态路径);
            }
            if (图形 != null)
            {
                try { if (File.Exists(图形)) File.Delete(图形); } catch { }
                起.ArgumentList.Add("--hb-图形");
                起.ArgumentList.Add(图形);
            }
            if (事件脚本 != null)
            {
                起.ArgumentList.Add("--hb-事件脚本");
                起.ArgumentList.Add(事件脚本);
            }
            if (现在 != null)
            {
                起.ArgumentList.Add("--hb-now");
                起.ArgumentList.Add(现在.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (现在毫秒 != null)
            {
                起.ArgumentList.Add("--hb-now-ms");
                起.ArgumentList.Add(现在毫秒.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            Process? 进程;
            try { 进程 = Process.Start(起); }
            catch (Exception ex) { return (-2, [], "起不了原生 exe: " + ex.Message); }
            if (进程 == null) return (-2, [], "起不了原生 exe");

            using (进程)
            {
                var 出流 = new MemoryStream();
                var 出任务 = 进程.StandardOutput.BaseStream.CopyToAsync(出流);
                var 误任务 = 进程.StandardError.ReadToEndAsync();
                try
                {
                    if (标准输入 != null) 进程.StandardInput.BaseStream.Write(Encoding.UTF8.GetBytes(标准输入));
                    进程.StandardInput.Close();
                }
                catch { }
                if (!进程.WaitForExit(180_000))
                {
                    try { 进程.Kill(entireProcessTree: true); } catch { }
                    return (-1, [], "原生 exe 180 秒没结束(超时)");
                }
                出任务.GetAwaiter().GetResult();
                return (进程.ExitCode, 出流.ToArray(), 误任务.GetAwaiter().GetResult());
            }
        }
    }
}
