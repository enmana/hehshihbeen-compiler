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
    /// R3-6-2:**纯 C 解释器核**(`C后端\解释器核.c`)的全量对拍。
    ///
    /// 判据就是 R3-6-2 表里那一句:**拿标准工程的机器码,与 C# 版对拍 ——
    /// 寄存器 / 内存 / 输出逐字节一致**。这里比 R3-6-4 那一组更“纯”:
    /// 那边每个工程都要**现转译 + 现编一份 C**(慢),这边解释器**只编一次**,
    /// 每个工程只是“写一份镜像 + 跑一遍”(快),所以这一组可以放心按全量跑。
    ///
    /// 一件事分四段记:
    ///   ① **纯源码那半**(不吃 C 编译器):`工具\指令集真值源.txt` 的操作码表 55 条,
    ///      逐条在解释器的 `switch` 里找得到 `case 0xNN:` —— 这是**接线哨兵**
    ///      (它钉的是“55 条都落了地”,不是“语义对不对”;语义由下面真跑那半钉);
    ///   ② **编译那一次**:解释器核编出来的得是原生 exe(PE 头 + 不带 .NET);
    ///   ③ **手写小机器码三条**:除零报错 / 输入(含 EOF)/ 镜像可复现 —— 判据与全量同口径;
    ///   ④ **全部标准工程对拍**:与 `C后端全量对拍` 同一套比法(`C状态C.比`)。
    ///
    /// 两处口径**故意与 `C后端全量对拍` 一模一样**(两边比的是同一件事,不能各写一套):
    /// `当前时间()` 的钟两边冻在同一个秒(`冻结秒`)、虚拟机那一侧挂 `宿主能力C.处理`。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary>要真编真跑的断言名(没有 C 编译器时逐条如实记跳过 —— 名字前缀 `C后端` 在允许跳过的三族里)</summary>
        private static readonly string[] C解释器核断言 =
        [
            "C后端 解释器核 编译成原生 exe(PE 头 + 没有 mscoree)",
            "C后端 解释器核 与 C# 版对拍:标准工程全过(寄存器 / 内存 / 输出逐字节一致)",
            "C后端 解释器核 逐工程:寄存器与内存逐字节一致",
            "C后端 解释器核 运行期错误与虚拟机同类(除零)+ 退出码非 0",
            "C后端 解释器核 输入(IN)与虚拟机一致(标准输入 + EOF 那条路)",
            "C后端 解释器核 镜像逐字节可复现,同一份镜像跑两遍状态一致",
        ];

        private const string 源码哨兵名 = "C后端 解释器核 源里有真值源每一条操作码(一条不落)";

        private static void C后端解释器核自检()
        {
            string? 根 = 找仓库根目录();
            if (根 == null)
            {
                string 理由 = "找不到仓库根(有 `引擎` 与 `中间层` 的那一层)—— 不在工作副本里跑,读不到真值源与解释器源码";
                跳过(源码哨兵名, 理由);
                foreach (string 名 in C解释器核断言) 跳过(名, 理由);
                return;
            }

            // ── ① 纯源码那半:真值源 55 条操作码,逐条在解释器里找得到 ──
            try
            {
                string 真值源路径 = Path.Combine(根, "工具", "指令集真值源.txt");
                string 解释器路径 = Path.Combine(根, "C后端", "解释器核.c");
                if (!File.Exists(真值源路径) || !File.Exists(解释器路径))
                {
                    跳过(源码哨兵名, $"缺文件:真值源 {File.Exists(真值源路径)} / 解释器核 {File.Exists(解释器路径)}");
                }
                else
                {
                    var 表里的码 = new List<string>();
                    foreach (string 行 in File.ReadAllLines(真值源路径, Encoding.UTF8))
                    {
                        if (!行.StartsWith("指令\t", StringComparison.Ordinal)) continue;
                        string[] 格 = 行.Split('\t');
                        if (格.Length >= 2 && 格[1].StartsWith("0x", StringComparison.Ordinal)) 表里的码.Add(格[1]);
                    }
                    string 源码 = File.ReadAllText(解释器路径, Encoding.UTF8);
                    var 缺 = 表里的码.Where(码 => !源码.Contains($"case {码}:", StringComparison.Ordinal)).ToArray();
                    var 多 = new List<string>();
                    for (int i = 0; i <= 0xFF; i++)
                    {
                        string 码 = "0x" + i.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
                        if (源码.Contains($"case {码}:", StringComparison.Ordinal) && !表里的码.Contains(码)) 多.Add(码);
                    }
                    记录(源码哨兵名, 表里的码.Count == 指令C.全部指令.Count && 缺.Length == 0 && 多.Count == 0,
                        $"真值源操作码 {表里的码.Count} 条(指令表 {指令C.全部指令.Count} 条);解释器里缺 {缺.Length} 条" +
                        (缺.Length > 0 ? "(" + string.Join(",", 缺.Take(6)) + ")" : "") +
                        $";多出来的 {多.Count} 条" + (多.Count > 0 ? "(" + string.Join(",", 多.Take(6)) + ")" : "") +
                        " —— 这一条是**接线哨兵**(钉“真值源里每条指令都落了地”),语义对不对由下面真跑那半钉");
                }
            }
            catch (Exception ex) { 记录异常(源码哨兵名, ex); }

            // ── ② 真编真跑那半 ──
            // `报告跳过: false` —— 上面已经把"没有 C 编译器"这一档逐条记过跳过了(再记一遍会撞
            // `自检 断言名唯一` 那道钉子);编译断言名给**空串** —— "编成原生 exe"那一条由下面
            // `解释器核_编译握手` 记,这里只借那份 exe(记两遍同样撞钉子,两处都实测撞过)。
            string? 解释器exe = 编好解释器核(C解释器核断言, 报告跳过: false);
            if (解释器exe == null) return;
            解释器核_编译握手(解释器exe);

            // ── ③ 手写小机器码:除零 / 输入(含 EOF)/ 镜像可复现 ──
            try { 解释器核_除零(解释器exe); } catch (Exception ex) { 记录异常("C后端 解释器核 运行期错误与虚拟机同类(除零)+ 退出码非 0", ex); }
            try { 解释器核_输入(解释器exe); } catch (Exception ex) { 记录异常("C后端 解释器核 输入(IN)与虚拟机一致(标准输入 + EOF 那条路)", ex); }
            try { 解释器核_可复现(解释器exe); } catch (Exception ex) { 记录异常("C后端 解释器核 镜像逐字节可复现,同一份镜像跑两遍状态一致", ex); }

            // ── ④ 全部标准工程对拍 ──
            var 全部工程 = 找全部工程文件().ToList();
            if (全部工程.Count == 0)
            {
                跳过("C后端 解释器核 逐工程:寄存器与内存逐字节一致", "找不到 Sample 目录(不在工作副本里跑)");
                跳过("C后端 解释器核 与 C# 版对拍:标准工程全过(寄存器 / 内存 / 输出逐字节一致)", "找不到 Sample 目录");
                return;
            }

            var 各工程 = new ConcurrentDictionary<string, string>();
            // H69:先认一遍"哪几个是图形标准工程"—— 它们要**一个一个跑**(理由见 `用图形标准工程`);
            // 这儿顺手把名单留在手上,那条断言的详情里才看得见"串了几个"。
            string[] 图形工程们 = 全部工程.Where(用图形标准工程).ToArray();
            int 跑过 = 0;
            long 毫秒 = 0;
            宿主能力C.清运行痕迹();
            宿主能力C.取当前秒 = () => 冻结秒;          // 与全量对拍同一个秒(见那边的说明)
            宿主能力C.取当前毫秒 = () => 冻结毫秒;      // 同一个刻度(见 `冻结毫秒` 的说明)
            try
            {
                // H107(2026-10-15 第449批):并行度**做成旋钮**了 —— 台账里那条复现口径写的是
                // "把 4 路并行收成 1 路跑 8 遍看还红不红",以前这一格写死 4,要按口径复现得先改代码。
                // 现在:环境变量 `HB_解释器核并行`(1~16),不给就是原来的 4(一个字没变)。
                // 判据一那一趟(`工具\四组合冒烟.ps1`)按量出来的结论决定要不要设它 —— 见本组那条断言的详情。
                int 并发 = 取并行度();
                using (var 闸 = new SemaphoreSlim(并发))
                {
                    var 任务 = 全部工程.Select(路径 => Task.Run(() =>
                    {
                        闸.Wait();
                        // H69(第330批):**图形标准工程一个一个跑**(见 `图形对拍门`)——
                        // 它们是这一组里唯一会动 `图形宿主C` 那几格**进程级静态状态**的工程
                        // (`挂上` / `挂事件脚本` / `清空` 不是线程局部的),4 路并行时
                        // 谁收了尾就把别人正开着的呈现口 `挂上(null)` 摘掉,于是"这一趟开没开成窗口"
                        // 两边不是同一条路,寄存器 / 执行条数 / 内存当场分岔(实测复现率见本组那条断言的详情)。
                        bool 图形工程 = 用图形标准工程(路径);
                        bool 句柄工程 = 用句柄标准工程(路径);
                        if (图形工程) Monitor.Enter(图形对拍门);
                        if (句柄工程) Monitor.Enter(句柄对拍门);
                        try
                        {
                            // G3 域 6:句柄表先压回"刚启动"(原生侧每个工程都是新进程;C# 侧一个进程连跑)
                            if (句柄工程) 宿主能力C.清句柄表();
                            var 表 = Stopwatch.StartNew();
                            string 结论 = 对拍解释器一个工程(路径, 解释器exe, out bool 真跑过);
                            表.Stop();
                            lock (各工程)
                            {
                                各工程[Path.GetFileName(路径)] = 结论;
                                if (真跑过) 跑过++;
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
            finally { 宿主能力C.清运行痕迹(); }

            int 不一致 = 各工程.Count(x => x.Value.Length > 0);
            var 坏 = 各工程.Where(x => x.Value.Length > 0).Take(4)
                .Select(x => $"{x.Key}: {x.Value}").ToArray();
            记录("C后端 解释器核 逐工程:寄存器与内存逐字节一致",
                不一致 == 0 && 跑过 == 全部工程.Count && 跑过 > 0,
                $"真跑 {跑过} 个 / 共 {全部工程.Count} 个;不一致 {不一致} 个" +
                (坏.Length > 0 ? ";" + string.Join(" ; ", 坏) : "") +
                $";图形标准工程 {图形工程们.Length} 个(" + string.Join("/", 图形工程们.Select(Path.GetFileNameWithoutExtension)) +
                (图形工程们.Length > 0 ? ")**一个一个跑**" : ")") +
                $";合计 {毫秒} 毫秒(别的工程并行 {取并行度()} 路,解释器只编了一次)");
            记录("C后端 解释器核 与 C# 版对拍:标准工程全过(寄存器 / 内存 / 输出逐字节一致)",
                不一致 == 0 && 跑过 == 全部工程.Count && 跑过 > 0,
                $"标准工程 {全部工程.Count} 个**一个不落**真跑、与 C# 版虚拟机逐字节一致");
        }

        /// <summary>
        /// 一个标准工程的对拍:编译 → 两头跑(虚拟机 / 解释器) → 逐字节比。
        /// 返回**空串 = 一致**;非空 = 差异说明。与 `C后端全量对拍` 的 `对拍一个工程` 同一套比法,
        /// 差别只在“C 那一侧”是**写一份镜像 + 跑一次解释器**,不是现编一份 C。
        /// </summary>
        private static string 对拍解释器一个工程(string 工程路径, string 解释器exe, out bool 真跑过)
        {
            真跑过 = false;
            string 名 = Path.GetFileNameWithoutExtension(工程路径);
            var 工程 = 工程文件C.加载(工程路径);
            if (工程.文件.Count == 0) return "空工程";
            int 内存; try { 内存 = 工程.内存字节数; } catch { 内存 = 虚拟机C.默认内存大小; }
            int 堆; try { 堆 = 工程.堆字节数; } catch { 堆 = 中间层C.默认堆大小; }
            int 池; try { 池 = 工程.池字节数; } catch { 池 = 中间层C.默认池大小; }
            int 软栈; try { 软栈 = 工程.软栈字节数; } catch { 软栈 = -1; }

            var 编译结果 = new 汇编器C().汇编多文件带调试(工程.读全部编译输入(), true, 堆, null, 池, 内存, 软栈);
            string 暂存 = 解释器目录(名);

            // ① 虚拟机侧:原样收输出字节,跑完导出整机状态(宿主能力照命令行 / IDE 那一套挂上)
            var 机 = new 虚拟机C();
            机.设内存大小(内存);
            机.初值表 = 编译结果.初值表;
            机.宿主调用 = 宿主能力C.处理;
            var 出 = new MemoryStream();
            机.输出目标 = b => 出.WriteByte(b);
            string 机报错 = "";
            // R3-6-3:解释器核那一份**带无头呈现**(图形能力 60~69 真落地了)⇒ 这一侧也要挂一只**不开窗的**
            // 呈现口,不然同一个图形程序两边走的不是同一条路(C# 侧"没挂呈现口"给状态 1、C 侧真开窗给状态 0,
            // 实测:示例14 的整机状态当场就分叉)。
            //
            // **图形程序还得配一份事件脚本**:无头呈现里帧边界只有 `刷新()` 一处,而两个图形标准工程的主循环
            // 是"收不到事件就接着刷" ⇒ 没有脚本**两边都会转到天荒地老**(这一批实测踩到:自检在这一组挂住)。
            // 脚本从工程目录那份 `事件脚本.txt` 拿(与 `图形宿主C.挂事件脚本` 同一份解析器);
            // 示例14 那种不自带脚本的给一份"只写 `帧数`"的兜底 —— 它自己不轮询事件,只借它把那一行解出来。
            bool 用图形 = 图形宿主C.这段机器码用图形(编译结果.机器码);
            string? 图形脚本路径 = 用图形 ? 读工程事件脚本(工程路径, 暂存) : null;
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
            机.装载(编译结果.机器码);
            try { while (机.单步()) { } }
            catch (Exception ex) { 机报错 = ex.Message; }
            finally { if (用图形) { 图形宿主C.挂上(null); 图形宿主C.清空(); } }
            var 机状态 = C状态C.导出虚拟机状态(机, 出, 编译结果.代码字节数, 编译结果.数据字节数);

            // ② 解释器侧:写镜像 → 跑 → 读状态
            string 镜像 = Path.Combine(暂存, 名 + ".hbimg");
            string 状态路径 = Path.Combine(暂存, 名 + ".状态");
            try { 镜像C.写(编译结果, 内存, 镜像); }
            catch (C后端错误C ex) { return "写镜像就写不动:" + 截断(ex.Message, 200); }

            // C 那一侧:图形程序按同一份脚本喂事件,绘制命令**收进暂存文件**(不给它文件它就打到标准输出上,
            // 与"程序输出"混成一股 —— 实测:示例14 的原生输出因此多出 267 字节,逐字节比当场就红);
            // 两边因此走的是**同一条无头路**:同一份事件脚本、都没有真窗口。
            string 图形文件 = Path.Combine(暂存, 名 + ".图形");
            var (退出码, 原生出, 原生误) = 跑原生带状态(解释器exe, null, 状态路径, 冻结秒, 镜像, 图形文件, 图形脚本路径,
                现在毫秒: 冻结毫秒);
            真跑过 = 退出码 != -2;
            var 原生状态 = C状态C.读原生状态(状态路径);
            if (原生状态 == null)
                return "解释器没落下状态转储(它自己没跑起来?)stderr: " + 截断(原生误, 160);

            // ③ 比:报错了就只比“同一类错 + 退出码”;正常跑完才比寄存器 / 内存 / 输出
            if (机报错.Length > 0)
            {
                string 关键词 = 错关键词(机报错);
                if (退出码 == 0) return $"虚拟机报「{截断(机报错, 60)}」,解释器却退出码 0";
                if (关键词.Length == 0 || !原生误.Contains(关键词, StringComparison.Ordinal))
                    return $"两边报的错对不上:虚拟机「{截断(机报错, 60)}」/ 解释器「{截断(原生误, 60)}」";
                if (!机状态.输出.AsSpan().SequenceEqual(原生出)) return "报错前的程序输出不一致";
                return "";
            }

            机状态.退出码 = 0;
            // G3 域 5:`示例31_异步` 那一档**不比"执行条数 / 最近路径"** —— 它靠
            // `当 (任务就绪(…) == 0) { }` 等后台线程,问了几轮由线程快慢决定(口径与理由见
            // `C状态C.比` 的 `不比条数`);寄存器 / 内存 / 输出照旧逐字节比。
            bool 异步工程 = 名.StartsWith("示例31_异步", StringComparison.Ordinal);
            var 比 = C状态C.比(机状态, 原生状态, 原生出, 退出码, 不比条数: 异步工程);
            // G3 流的**那一格例外**(口径与理由见 `综合自检C.比内存只放行短读一格`):
            // 标准工程 `示例27_流` 用了 `读一块`,那一格记的是"上一次 `Read` 的短读字节数"。
            if (!比.一致 && 名.StartsWith("示例27_流", StringComparison.Ordinal)
                && 综合自检C.比内存只放行短读一格(机状态, 原生状态, 允许级联: true).过)
                return "";
            return 比.一致 ? "" : 比.说明;
        }

        /// <summary>
        /// 手写一段机器码(4 字节一条,小端),给“不走编译器”的那几条断言用。
        /// 这份机器码**不进任何标准工程**:它钉的是解释器与虚拟机在错误 / 输入这两条冷路上的口径。
        /// </summary>
        private static byte[] 指令流(params uint[] 条)
        {
            var 码 = new byte[条.Length * 4];
            for (int i = 0; i < 条.Length; i++)
            {
                码[i * 4 + 0] = (byte)(条[i] & 0xFF);
                码[i * 4 + 1] = (byte)((条[i] >> 8) & 0xFF);
                码[i * 4 + 2] = (byte)((条[i] >> 16) & 0xFF);
                码[i * 4 + 3] = (byte)((条[i] >> 24) & 0xFF);
            }
            return 码;
        }

        /// <summary>把一段手写机器码交给虚拟机跑,收整机状态(输入源用给定字节表;用光就是 EOF)</summary>
        private static C状态快照C 虚拟机跑手写码(byte[] 码, byte[]? 标准输入, out string 报错, int 内存 = 4096)
        {
            var 机 = new 虚拟机C();
            机.设内存大小(内存);
            int 游 = 0;
            byte[] 输入 = 标准输入 ?? [];
            机.输入源 = () => 游 < 输入.Length ? 输入[游++] : (uint?)null;
            var 出 = new MemoryStream();
            机.输出目标 = b => 出.WriteByte(b);
            报错 = "";
            机.装载(码);
            try { while (机.单步()) { } }
            catch (Exception ex) { 报错 = ex.Message; }
            return C状态C.导出虚拟机状态(机, 出, 码.Length, 0);
        }

        /// <summary>除零:两边都该报“除零”、退出码都非 0(机器码:`加载立即数 R0,10` / `加载立即数 R1,0` / `除法 R0,R1` / `停止`)</summary>
        private static void 解释器核_除零(string 解释器exe)
        {
            byte[] 码 = 指令流(0x0300000Au, 0x03100000u, 0x25010000u, 0x00000000u);
            _ = 虚拟机跑手写码(码, null, out string 机报错);
            string 暂存 = 解释器目录("除零");
            string 镜像 = Path.Combine(暂存, "除零.hbimg");
            string 状态路径 = Path.Combine(暂存, "除零.状态");
            镜像C.写(new 汇编调试结果C { 机器码 = 码, 代码字节数 = 码.Length, 数据字节数 = 0 }, 4096, 镜像);
            var (退出码, _, 误) = 跑原生带状态(解释器exe, null, 状态路径, null, 镜像);
            bool 机报除零 = 机报错.Contains("除零", StringComparison.Ordinal);
            bool 核报除零 = 误.Contains("除零", StringComparison.Ordinal);
            记录("C后端 解释器核 运行期错误与虚拟机同类(除零)+ 退出码非 0",
                机报除零 && 核报除零 && 退出码 != 0,
                $"虚拟机「{截断(机报错, 60)}」/ 解释器「{截断(误.Trim(), 60)}」;解释器退出码 {退出码}");
        }

        /// <summary>输入:标准输入只给一个字节 —— 第二个 `读取字符` 就该拿到 EOF(0xFFFFFFFF),两边输出逐字节比</summary>
        private static void 解释器核_输入(string 解释器exe)
        {
            // 读取字符 R0 / 输出字符 R0 / 读取字符 R1 / 输出字符 R1 / 停止
            byte[] 码 = 指令流(0x27000000u, 0x28000000u, 0x27100000u, 0x28100000u, 0x00000000u);
            byte[] 输入 = [(byte)'A'];
            var 机状态 = 虚拟机跑手写码(码, 输入, out string 机报错);
            string 暂存 = 解释器目录("输入");
            string 镜像 = Path.Combine(暂存, "输入.hbimg");
            string 状态路径 = Path.Combine(暂存, "输入.状态");
            镜像C.写(new 汇编调试结果C { 机器码 = 码, 代码字节数 = 码.Length, 数据字节数 = 0 }, 4096, 镜像);
            var (退出码, 出, 误) = 跑原生带状态(解释器exe, Encoding.ASCII.GetString(输入), 状态路径, null, 镜像);
            var 原生状态 = C状态C.读原生状态(状态路径);
            string 说明;
            if (原生状态 == null) 说明 = "解释器没落下状态转储: " + 截断(误, 160);
            else if (机报错.Length > 0) 说明 = "虚拟机报了错: " + 截断(机报错, 60);
            else
            {
                var 比 = C状态C.比(机状态, 原生状态, 出, 退出码);
                说明 = 比.一致 ? "" : 比.说明;
            }
            byte[] 期望 = [(byte)'A', 0xFF];
            记录("C后端 解释器核 输入(IN)与虚拟机一致(标准输入 + EOF 那条路)",
                说明.Length == 0 && 出.AsSpan().SequenceEqual(期望),
                说明.Length > 0 ? 说明 : $"输出 {出.Length} 字节 = A + 0xFF(第二个读取字符拿到 EOF),与虚拟机逐字节一致");
        }

        /// <summary>镜像可复现:同一份编译结果写两遍逐字节相同;同一份镜像跑两遍,落下的状态也逐字节相同</summary>
        private static void 解释器核_可复现(string 解释器exe)
        {
            byte[] 码 = 指令流(0x0300002Au, 0x28000000u, 0x29000000u, 0x00000000u);   // 加载立即数 R0,42 / 输出字符 / 输出换行 / 停止
            var 编译 = new 汇编调试结果C { 机器码 = 码, 代码字节数 = 码.Length, 数据字节数 = 0 };
            byte[] 一 = 镜像C.造(编译, 4096);
            byte[] 二 = 镜像C.造(编译, 4096);
            bool 镜像同 = 一.AsSpan().SequenceEqual(二);
            string 暂存 = 解释器目录("可复现");
            string 镜像 = Path.Combine(暂存, "可复现.hbimg");
            File.WriteAllBytes(镜像, 一);
            string 状态一 = Path.Combine(暂存, "一.状态");
            string 状态二 = Path.Combine(暂存, "二.状态");
            var (码一, 出一, _) = 跑原生带状态(解释器exe, null, 状态一, null, 镜像);
            var (码二, 出二, _) = 跑原生带状态(解释器exe, null, 状态二, null, 镜像);
            bool 状态同 = File.Exists(状态一) && File.Exists(状态二)
                && File.ReadAllBytes(状态一).AsSpan().SequenceEqual(File.ReadAllBytes(状态二));
            bool 输出同 = 出一.AsSpan().SequenceEqual(出二) && 出一.AsSpan().SequenceEqual(new byte[] { (byte)'*', 0x0A });
            记录("C后端 解释器核 镜像逐字节可复现,同一份镜像跑两遍状态一致",
                镜像同 && 码一 == 0 && 码二 == 0 && 状态同 && 输出同,
                $"镜像两次写出来逐字节相同 = {镜像同}({一.Length} 字节);两遍退出码 {码一}/{码二};" +
                $"整机状态逐字节一致 = {状态同};输出 = {出一.Length} 字节(期望 `*` + 换行)");
        }

        /// <summary>
        /// 编出来的那一份**得是原生 exe**(PE 头 + 不带 .NET 字样)—— 这一条断言由本组自己记
        /// (所以 `编好解释器核` 两次调用都传空串编译断言名:同名记两遍会撞 `自检 断言名唯一`)。
        /// </summary>
        private static void 解释器核_编译握手(string 解释器exe)
        {
            const string 断言名 = "C后端 解释器核 编译成原生 exe(PE 头 + 没有 mscoree)";
            try
            {
                byte[] 字节 = File.ReadAllBytes(解释器exe);
                string 全文 = Encoding.ASCII.GetString(字节);
                bool 是pe = 字节.Length > 2 && 字节[0] == (byte)'M' && 字节[1] == (byte)'Z';
                bool 无net = !全文.Contains("mscoree", StringComparison.OrdinalIgnoreCase)
                    && !全文.Contains(".NET Framework", StringComparison.OrdinalIgnoreCase);
                记录(断言名, 是pe && 无net,
                    $"{字节.Length / 1024} KB;PE 头 = {是pe};带 .NET 字样 = {!无net}");
            }
            catch (Exception ex) { 记录异常(断言名, ex); }
        }

        /// <summary>自检用的临时目录(一个工程一个子目录,免得并发时互相盖)</summary>
        private static string 解释器目录(string 名)
        {
            string 暂存 = Path.Combine(临时区C.基目录(), "hbcc_C解释器核", Environment.ProcessId.ToString(), 干净名(名));
            Directory.CreateDirectory(暂存);
            return 暂存;
        }

        /// <summary>
        /// H107:这一组跑标准工程时的**并行度**。默认 **4**(老口径一个字没变);
        /// 环境变量 `HB_解释器核并行` 给 1~16 就用它 —— 台账那条复现口径("收成 1 路跑 8 遍")靠它,
        /// 判据一那一趟也可以按量出来的结论设成 1(见本文件头那条断言的详情)。
        /// 解析失败 / 超范围一律回默认 4(旋钮不该把自检跑挂)。
        /// </summary>
        private static int 取并行度()
        {
            string? 文 = Environment.GetEnvironmentVariable("HB_解释器核并行");
            if (int.TryParse(文, out int 值) && 值 >= 1 && 值 <= 16) return 值;
            return 4;
        }

        /// <summary>
        /// H69(第330批):**图形标准工程那把门** —— 它们**一个一个跑**,别的工程照旧并行 4 路。
        ///
        /// 为什么非串不可:`图形宿主C` 那几格状态(呈现口 / 事件脚本 / 已呈现帧数 / 三个队列)是
        /// **进程级静态**的,C# 侧那一台虚拟机就在**同一个进程里**跑 ⇒ 4 路并行时,A 工程跑完那句
        /// `挂上(null)` / `清空()` 会把 B 工程正开着的窗口摘掉,B 的 `打开窗口` 当场拿到
        /// `状态_没挂能力`(=1)⇒ 两个标准工程(`示例14` / `示例15` / `示例16`)都写了
        /// `如果 (打开窗口(…))` 这道分支 ⇒ **C# 侧走"开不了窗"那一支、C 侧照样开窗**
        /// (它是另一个进程,状态互不干扰),于是寄存器 / 执行条数 / 内存整块分岔。
        ///
        /// 这条不是"看着像",是**真抓到过**:`工具\临时\h69\selftest8.txt` 里 8 遍自检红了 3 遍,
        /// 两次都是图形工程,而且两边差的是**整条执行路径**(示例16:C# 9633 条 / 原生 44904 条,
        /// 输出 40 / 94 字节)—— 修之前是"偶发",修之后见那条断言的详情。
        ///
        /// **R3-6-5 批 2 起多一个用它的人**:`C后端全量对拍` 那一组也给图形工程挂无头呈现了
        /// (转译产物这一批也带图形)⇒ 两组**共用这一把门**(各自组内串行还不够 —— 两组是并行跑的)。
        /// </summary>
        private static readonly object 图形对拍门 = new();

        /// <summary>
        /// **会用句柄的标准工程**(流 / 网络那一族)一个一个跑的那把门 —— 与 <see cref="图形对拍门"/>
        /// 同一条道理,只是对象换成 `宿主能力C` 里那两张**进程级静态**的句柄表(流 64 格 + 套接字 32 格)。
        ///
        /// G3 域 6 量出来的(第340批):句柄 = `(表下标 &lt;&lt; 16) | 版本`,版本**每复用一格 +1**,
        /// 而 C# 侧是一个进程连跑几十个标准工程、原生侧每个工程都是新进程 ⇒
        /// 谁先跑谁把版本抬上去,后跑那个工程的句柄就和原生侧对不上
        /// (实测:示例30 的写句柄 **4 / 1** —— 示例27 先开过三条流,把第 0 格抬到版本 4)。
        /// 修法两条一起用:① 这类工程**串起来**(两张表不能同时被两个工程用);
        /// ② 开跑前 `宿主能力C.清句柄表()`(把句柄表压回"刚启动",见那个方法的说明)。
        ///
        /// **两把门的顺序固定**:先图形门、再这一把(两组都这么写)—— 固定顺序才不会有环路。
        /// </summary>
        private static readonly object 句柄对拍门 = new();

        /// <summary>
        /// 这个标准工程**用没用宿主句柄表**(决定要不要拿 <see cref="句柄对拍门"/> 串起来 + 清一次表)。
        ///
        /// 判据 = 工程目录下任何一份 `.hb` 源码里写没写那张表的**入口名**:
        /// `打开读流` / `打开写流`(流那张表)、`连到` / `监听`(套接字那张表,`接受` 是从监听句柄上来的,
        /// 没有监听句柄就到不了它)、`发起异步`(G3 域 5 的任务表 —— 那张表也是**进程级静态**的,
        /// 任务号同样按 `(下标 &lt;&lt; 16) | 版本` 走)。
        /// **与 `用图形标准工程` 同一档的源码文本判据** —— 不拿编译结果问,免得为了问一句话把每个工程白编一遍。
        /// </summary>
        private static bool 用句柄标准工程(string 工程路径)
        {
            try
            {
                string 目录 = Path.GetDirectoryName(工程路径) ?? "";
                foreach (string 源 in 源码文件C.列源码(目录))
                {
                    string 正文 = File.ReadAllText(源, Encoding.UTF8);
                    if (正文.Contains("打开读流", StringComparison.Ordinal)
                        || 正文.Contains("打开写流", StringComparison.Ordinal)
                        || 正文.Contains("连到", StringComparison.Ordinal)
                        || 正文.Contains("监听", StringComparison.Ordinal)
                        || 正文.Contains("发起异步", StringComparison.Ordinal)) return true;
                }
            }
            catch { return false; }        // 读不动就按"不碰句柄表"办:宁可少串一个,也不冤枉别的工程
            return false;
        }

        /// <summary>
        /// 这个标准工程**用没用图形**(决定要不要拿 <see cref="图形对拍门"/> 串起来)。
        ///
        /// 判据 = 工程目录下任何一份 `.hb` 源码里写没写 `打开窗口` —— 这是**源码文本级**的可靠判据:
        /// 图形能力 60~69 那一族里,`打开窗口` 是**唯一**的入口(`关闭窗口` / `清屏` / 画 / `刷新` /
        /// `取事件` / `等待` 都发生在开窗之后,而 "先画再开窗" 那种写法跑起来直接是状态 22),
        /// 所以"没写打开窗口"就等于"这个工程一次都不会碰 `图形宿主C`"。
        ///
        /// **为什么不拿 `图形宿主C.这段机器码用图形(编译结果.机器码)` 当判据**:那个得先把每个工程
        /// 都编一遍才问得出一句话(38 个工程白编一遍),而这一组每跑一遍本来就要编一次,
        /// 白白多烧一倍编译时间 —— 值不当。
        /// </summary>
        private static bool 用图形标准工程(string 工程路径)
        {
            try
            {
                string 目录 = Path.GetDirectoryName(工程路径) ?? "";
                foreach (string 源 in 源码文件C.列源码(目录))
                {
                    if (File.ReadAllText(源, Encoding.UTF8).Contains("打开窗口", StringComparison.Ordinal)) return true;
                }
            }
            catch { return false; }        // 读不动就按"不碰图形"办:宁可少串一个,也不冤枉别的工程
            return false;
        }

        /// <summary>
        /// 图形工程喂给无头宿主的那份**事件脚本**:优先用工程目录里自带的 `事件脚本.txt`;
        /// 没带(示例14 就是)就在暂存目录里写一份**只写 `帧数`** 的兜底 —— 它自己不轮询事件,
        /// 这份脚本只是让"帧数"那一行有出处(把 `事件脚本C` 那个解析器伺候好,别让它空手)。
        /// **返回路径**(调用方拿去喂 C 解释器核的 `--hb-事件脚本`)。
        /// </summary>
        private static string 读工程事件脚本(string 工程路径, string 暂存)
        {
            string? 自带 = 事件脚本C.找脚本(Path.GetDirectoryName(工程路径));
            if (自带 != null) return 自带;
            string 兜底 = Path.Combine(暂存, "事件脚本.txt");
            File.WriteAllText(兜底, "帧数 4\n", new UTF8Encoding(false));
            return 兜底;
        }

        /// <summary>
        /// 把 `C后端\解释器核.c`(+ `运行时.c`)编成一份原生 exe,返回它的路径;**编不出来给 null**。
        ///
        /// **抄到临时目录再编**(不能就地编)有两条理由:① `cl` 会把 `.obj` 落在源码旁边 ——
        /// 直接编仓库里那一份,仓库根就多出一个 `解释器核.obj`(实测踩过:自检跑完 `git status`
        /// 里冒出个未跟踪的产物);② 两份挨在一起,`#include "运行时.c"` 照样找得到
        /// (`运行时.c` 走 `C后端C.取运行时源码()`,与 R3-6-4 的转译产物同一个来源)。
        ///
        /// **本函数一条断言都不记**:它只负责"把 exe 弄出来"(编不出来就按 `本组断言` 逐条记跳过)。
        /// 这一点是有意为之 —— `C后端解释器核` 与 `C后端宿主能力与无头呈现` **两组共用这一份 exe**,
        /// 谁记"编成原生 exe"那条都会让**同名断言记两遍**,撞 `自检 断言名唯一` 那道钉子(这一批实测撞了两次)。
        /// 那一条由 `解释器核_编译握手` 一个人记。
        /// </summary>
        private static string? 编好解释器核(string[] 本组断言, bool 报告跳过 = true)
        {
            string? 根 = 找仓库根目录();
            if (根 == null)
            {
                if (报告跳过)
                    foreach (string 名 in 本组断言)
                        跳过(名, "找不到仓库根(有 `引擎` 与 `中间层` 的那一层)—— 读不到解释器源码");
                return null;
            }
            var 工具 = C工具链C.探一下();
            if (!工具.成功)
            {
                if (报告跳过)
                {
                    string 理由 = "这台机器上没有能用的 C 编译器 —— " +
                        (工具.找不到原因.Length > 0 ? 工具.找不到原因 : 工具.日志);
                    foreach (string 名 in 本组断言) 跳过(名, 理由);
                }
                return null;
            }
            string 仓库里的核 = Path.Combine(根, "C后端", "解释器核.c");
            try
            {
                if (!File.Exists(仓库里的核))
                {
                    if (报告跳过)
                        foreach (string 名 in 本组断言) 跳过(名, $"找不到 {仓库里的核}(不在工作副本里跑?)");
                    return null;
                }
                string 暂存 = 解释器目录("核");
                string c源 = Path.Combine(暂存, "解释器核.c");
                string 解释器exe = Path.Combine(暂存, "解释器核.exe");
                File.WriteAllText(Path.Combine(暂存, "运行时.c"), C后端C.取运行时源码(), new UTF8Encoding(false));
                File.WriteAllText(c源, File.ReadAllText(仓库里的核, Encoding.UTF8), new UTF8Encoding(true));
                // 关优化编:两组的判据都是**语义**,与优化档无关(与全量对拍同一档)
                var 编 = C工具链C.编成exe(c源, 解释器exe, 关优化: true);
                if (!编.成功)
                {
                    if (报告跳过)
                        foreach (string 名 in 本组断言)
                            跳过(名, "解释器核没编过,这一条没法真跑");
                    return null;
                }
                return 解释器exe;
            }
            catch (Exception ex)
            {
                if (报告跳过)
                    foreach (string 名 in 本组断言)
                        跳过(名, "解释器核那一次编译抛了异常");
                return null;
            }
        }
    }
}
