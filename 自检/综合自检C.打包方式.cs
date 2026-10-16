using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R3-6-4 批 4:**接线** —— 三档打包方式(原生 C / 自带运行时·单文件 / 只编译)
    /// 在 IDE 工具栏下拉、命令行 `--打包`、帮助正文三处是不是**同一份事实源**,以及
    /// "顺手项"那条**假自包含宿主**缺陷的判据(体积 + 无伴随 dll)是不是真的立得住。
    /// (第248批 H41 撤掉了原先的第四档「轻量·需装 .NET」—— 它宣称"一百多 KB 的框架依赖版",
    /// 实测贴的是那份 101 MB 自包含宿主;两条路量过之后判:这个卖点在本机形态下做不到,而"产物小"
    /// 已由「原生 C」更彻底地解决。详见 `任务清单.md` 第248节。)
    ///
    /// **这一组不开窗**(硬规矩 6):界面那半边一律**按源码钉** —— 下拉的三项与
    /// `引擎\打包规格C.cs` 那三条一一对上、接线确实挂在 `SelectionChanged` 上、换档后走的是
    /// "按新档重做一次打包"而不是重编机器码。真编真跑那半边(C 编译器 / 宿主)已经在
    /// `C后端` 与 `C后端全量对拍` 两组里了,这里不重复烧时间。
    ///
    /// 顺手把"假自包含"这个缺陷**钉成一条能红的断言**:以前 `取宿主exe()` 只看路径在不在,
    /// 盘上那份 **0.18 MB** 的框架依赖 exe 就被当自包含贴出去(文案还写"双击即跑")。
    /// 现在判据在 `打包规格C.自包含原因`,这里拿**现场造的两份**真文件两头都钉一遍。
    ///
    /// R3-6-4 批 4 收尾(用户 2026-09-23 报"选 C 编译 / 一切回 C 就卡死")在这份文件里补的是
    /// **"卡死那一刀"**:原生 C 那条腿**下后台**(不占界面线程)、**同一份程序同一档不重做**、
    /// 后台**一次只跑一条**;C 工具链那边**候选表惰性**、**兜底扫盘带预算 + 结果缓存**、
    /// **大源码超时不再连试下一套**(以前两个候选各等 180 秒 = 白等 6 分钟还是失败)。
    /// 这几条都按源码钉 —— 界面那半边依旧不开窗(硬规矩 6)。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary> 源码按行取一段(界面接线那几条断言用;"源码里找得到"比"跑一次窗口"实在得多) </summary>
        private static string 取源码段(string 路径, int 从行, int 到行)
        {
            if (!File.Exists(路径)) return "";
            var 行 = File.ReadAllLines(路径);
            if (从行 < 1) 从行 = 1;
            if (到行 > 行.Length) 到行 = 行.Length;
            return string.Join("\n", 行.Skip(从行 - 1).Take(到行 - 从行 + 1));
        }

        private static void 打包方式自检()
        {
            string 暂存 = Path.Combine(临时区C.基目录(), "hbcc_打包方式", Environment.ProcessId.ToString());

            // ── 一、纯函数:三档规格本身 ──
            try
            {
                记录("打包方式 三档有序、互不重名",
                    打包规格C.全部.Length == 打包规格C.规格条数
                    && 打包规格C.全部.Select(种 => 种.名).Distinct().Count() == 打包规格C.规格条数
                    && 打包规格C.规格条数 == 3,
                    string.Join(" / ", 打包规格C.全部.Select(种 => 种.名)));

                记录("打包方式 三档的存储键都是 ASCII(命令行 / .json / 记事本都不怕编码)",
                    打包规格C.全部.All(种 => 种.键.Length > 0 && 种.键.All(字 => 字 < 128)),
                    string.Join(" ", 打包规格C.全部.Select(种 => 种.键)));

                记录("打包方式 三档都有说明文字(下拉 tip / 帮助 / 报错共吃这一份)",
                    打包规格C.全部.All(种 => 种.说明.Trim().Length > 0),
                    string.Join(" / ", 打包规格C.全部.Select(种 => 种.名)));
            }
            catch (Exception ex) { 记录异常("打包方式 三档规格本身", ex); }

            // ── 二、纯函数:名字认不认 ──
            try
            {
                记录("打包方式 正式名 / 存储键 / 别名三种写法都认",
                    打包规格C.取规格("原生 C")?.键 == "native_c"
                    && 打包规格C.取规格("native_c")?.名 == 打包规格C.原生C
                    && 打包规格C.取规格("host_self")?.名 == 打包规格C.自带运行时
                    && 打包规格C.取规格("source_only")?.名 == 打包规格C.只编译,
                    "三个存储键 + 别名都通");

                记录("打包方式 撤掉的那一档不再认(第248批 H41:host_fx 与它的几个别名都收回)",
                    打包规格C.取规格("host_fx") == null
                    && 打包规格C.取规格("轻量") == null
                    && 打包规格C.取规格("轻量·需装 .NET") == null
                    && 打包规格C.取规格("framework") == null
                    && !打包规格C.表().Contains("host_fx", StringComparison.Ordinal),
                    "认不出给 null(调用方按用法错误报)");

                记录("打包方式 忽略大小写与空格(手打 `Native_C` / `HOST SELF` 也认)",
                    打包规格C.取规格("Native_C")?.名 == 打包规格C.原生C
                    && 打包规格C.取规格("host self")?.名 == 打包规格C.自带运行时,
                    "大小写与空格都归一了");

                记录("打包方式 认不出给 null,不瞎猜(调用方负责报用法错误)",
                    打包规格C.取规格("乱写") == null && 打包规格C.取规格("") == null && 打包规格C.取规格(null) == null,
                    "三种都给 null");

                记录("打包方式 缺省那一档 = 自带运行时·单文件(老命令一个字不改照走老路)",
                    打包规格C.缺省 == 打包规格C.自带运行时);

                记录("打包方式 两条腿的分档:谁贴 .NET 宿主 / 谁交 C 源码",
                    !打包规格C.要装net(打包规格C.原生C) && 打包规格C.要C编译器(打包规格C.原生C)
                    && 打包规格C.要装net(打包规格C.自带运行时) && !打包规格C.要C编译器(打包规格C.自带运行时)
                    && !打包规格C.要装net(打包规格C.只编译) && !打包规格C.要C编译器(打包规格C.只编译)
                    && 打包规格C.全部.Count(种 => 打包规格C.要装net(种.名)) == 1
                    && 打包规格C.全部.Count(种 => 打包规格C.要C编译器(种.名)) == 1
                    && 打包规格C.全部.Count(种 => 打包规格C.是C源码(种.名)) == 2,
                    "贴 .NET 宿主:只剩自带运行时(=1);要 C 编译器:只有原生 C(=1);" +
                    "交 C 源码:原生 C + 只编译(=2);只编译两条都不要(它只交源码)");

                记录("打包方式 只编译认得出(它是「不产 exe」那一档)",
                    打包规格C.是只编译(打包规格C.只编译) && !打包规格C.是只编译(打包规格C.原生C)
                    && !打包规格C.是只编译(打包规格C.自带运行时));

                记录("打包方式 缺省那份「两行字」由三档现场拼(不手抄)",
                    打包规格C.表().Contains("native_c") && 打包规格C.表().Contains("host_self")
                    && 打包规格C.表().Contains("source_only")
                    && 打包规格C.一句话().Contains(打包规格C.只编译),
                    "表 / 一句话都从 全部 来");
            }
            catch (Exception ex) { 记录异常("打包方式 名字解析", ex); }

            // ── 三、"假自包含宿主"那条缺陷的判据(纯函数 + 现场造两份真文件) ──
            try
            {
                记录("假自包含 体积那一刀:0.18 MB 那份判假、70 MB 那份判真(纯函数)",
                    !打包规格C.是自包含(184417L, [])
                    && 打包规格C.是自包含(打包规格C.自包含最小字节, [])
                    && 打包规格C.自包含原因(184417L, []) is { } 因1 && 因1.Contains("框架依赖"),
                    $"界线 {打包规格C.自包含最小字节 / 1024 / 1024} MB");

                记录("假自包含 伴随 dll 那一刀:旁边有 dll 就判假(哪怕体积够)",
                    !打包规格C.是自包含(打包规格C.自包含最小字节 * 2, ["HB宿主.dll"])
                    && !打包规格C.是自包含(打包规格C.自包含最小字节 * 2, ["核心.dll"])
                    && 打包规格C.是自包含(打包规格C.自包含最小字节 * 2, ["HB宿主.exe"]),
                    "单文件发布不会在旁边留 dll");

                记录("假自包含 pdb 不参与:单文件发布带符号文件也照样判真",
                    打包规格C.是自包含(打包规格C.自包含最小字节 * 2, ["HB宿主.pdb", "HB宿主.exe"]));

                记录("假自包含 文件不在也如实说(不当成「是」)",
                    "打包规格C.自包含原因(不存在路径)" == "文件不在"
                    || 打包规格C.自包含原因(Path.Combine(临时区C.基目录(), "hb_绝不存在_打包方式.exe")) == "文件不在");
            }
            catch (Exception ex) { 记录异常("假自包含 判据(纯函数)", ex); }

            try
            {
                // 现场造两份真文件:一份 1 MB(框架依赖那个量级)、一份 20 MB(自包含那个量级)
                Directory.CreateDirectory(暂存);
                string 小 = Path.Combine(暂存, "小宿主.exe");
                string 大 = Path.Combine(暂存, "大宿主.exe");
                File.WriteAllBytes(小, new byte[1024 * 1024]);
                using (var 流 = new FileStream(大, FileMode.Create, FileAccess.Write))
                    流.SetLength(打包规格C.自包含最小字节 + 1024 * 1024);

                string? 小因 = 打包规格C.自包含原因(小);
                string? 大因 = 打包规格C.自包含原因(大);
                记录("假自包含 真查盘:1 MB 那份报假(带上体积)、20 MB 那份判真",
                    小因 != null && 小因.Contains("框架依赖") && 大因 == null,
                    $"小的:{截断(小因 ?? "判真", 60)};大的:{(大因 == null ? "判真" : 大因)}");
            }
            catch (Exception ex) { 记录异常("假自包含 查盘(现场造两份真文件)", ex); }

            // ── 四、命令行端:接线按源码钉(不开子进程,真跑那半边在 CLI端到端 组) ──
            string? 根 = 找仓库根目录();
            try
            {
                if (根 == null)
                {
                    跳过("打包方式 CLI:认 --打包 并报带选项表的用法错误", "找不到仓库根");
                    跳过("打包方式 IDE:下拉三项与规格源一一对上", "找不到仓库根");
                    跳过("打包方式 IDE:下拉接线 SelectionChanged + 走同一份分派", "找不到仓库根");
                    跳过("打包方式 CLI/IDE:原生 C 那条腿真叫 C 编译器(不是只打印)", "找不到仓库根");
                    跳过("打包方式 帮助正文写出三档与「不带 .NET」的口径", "找不到仓库根");
                }
                else
                {
                    string cli = File.ReadAllText(Path.Combine(根, "编译器CLI", "Program.cs"));
                    记录("打包方式 CLI:认 --打包 并报带选项表的用法错误",
                        cli.Contains("case \"--打包\":", StringComparison.Ordinal)
                        && cli.Contains("打包规格C.取规格(值)", StringComparison.Ordinal)
                        && cli.Contains("--打包 不认这一档", StringComparison.Ordinal)
                        && cli.Contains("打包规格C.表()", StringComparison.Ordinal),
                        "--打包 分支 + 认不出来时报三档表");

                    记录("打包方式 CLI:缺省不写死老行为(没给 --打包 就跟 --方式 走)",
                        cli.Contains("打包给的 ?? (方式 == \"C\" ? 打包规格C.原生C : 打包规格C.缺省)", StringComparison.Ordinal),
                        "老脚本一个字不改");

                    string 界面 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml"));
                    记录("打包方式 IDE:下拉三项与规格源一一对上(不是又抄一份中文)",
                        界面.Contains("Name=\"打包方式下拉\"", StringComparison.Ordinal)
                        && 界面.Contains("SelectionChanged=\"打包方式下拉_SelectionChanged\"", StringComparison.Ordinal)
                        && 打包规格C.全部.All(种 => 界面.Contains($"<ComboBoxItem Content=\"{种.名}\"/>", StringComparison.Ordinal)),
                        string.Join(" / ", 打包规格C.全部.Select(种 => 种.名)));

                    string 调试器 = File.ReadAllText(Path.Combine(根, "MainWindow.运行与调试器.cs"));
                    记录("打包方式 IDE:下拉接线 SelectionChanged + 走同一份分派",
                        调试器.Contains("打包方式下拉_SelectionChanged", StringComparison.Ordinal)
                        && 调试器.Contains("按打包方式生成(输出, 规格, 上一档)", StringComparison.Ordinal)
                        && 调试器.Contains("case 打包规格C.原生C: return 原生C排后台(输出, 当前内存大小)", StringComparison.Ordinal)
                        && 调试器.Contains("case 打包规格C.只编译: return 只编译产物", StringComparison.Ordinal),
                        "换档 → 按新档重做一次打包(机器码没变);H38-2 起『编译结果』那一格由契约自己攥(参数只传路径 / 规格 / 上一档)");

                    记录("打包方式 CLI/IDE:原生 C 那条腿真叫 C 编译器(不是只打印)",
                        cli.Contains("C工具链C.编成exe(c源, 输出全)", StringComparison.Ordinal)
                        && 调试器.Contains("C工具链C.编成exe(c源, 输出)", StringComparison.Ordinal)
                        && cli.Contains("打包规格C.是只编译(打包)", StringComparison.Ordinal)
                        && 调试器.Contains("打包规格C.要C编译器(上一档)", StringComparison.Ordinal),
                        "两边都真调 C 工具链;只编译那一档只写源码(走哪条腿看上一档)");

                    // H103(2026-10-15 第443批):**IDE「只编译」的产物落 `<工程目录>\bin\`**。
                    // 为什么钉:它交的 `.hb` 与真源码**同后缀**,落工程目录会把工作树弄脏(第434批只兜住了
                    // 自检那一侧);`bin/` 在 `.gitignore` 里(那一行的口径见仓库根那份文件)。
                    // 这里钉**源码文本**(界面层不进口译库,自检调不到那个私有方法)——
                    // 落点一改,这条就该红:`.gitignore` 与"产物会不会被当成源码数"两件事都得跟着想。
                    记录("打包方式 IDE:「只编译」的产物落 `<工程目录>\\bin\\`(H103;命令行 `-o` 不受影响)",
                        调试器.Contains("private const string 只编译落点子目录 = \"bin\";", StringComparison.Ordinal)
                        && 调试器.Contains("输出 = Path.Combine(目录, 只编译落点子目录, 名称 + 打包规格C.只编译后缀", StringComparison.Ordinal)
                        && 调试器.Contains("Directory.CreateDirectory(落点目录)", StringComparison.Ordinal)
                        && cli.Contains("Path.ChangeExtension(要exe路径, 打包规格C.只编译后缀(走C腿))", StringComparison.Ordinal),
                        "IDE 落 bin\\ 且先建目录;命令行那条腿照旧按 `-o` 指哪写哪(它不经过 `产物路径`)");

                    记录("打包方式 CLI/IDE:宿主那条腿统一过自包含校验(顺手项那条缺陷)",
                        cli.Contains("打包规格C.自包含原因(宿主)", StringComparison.Ordinal)
                        && 调试器.Contains("打包规格C.自包含原因(宿主)", StringComparison.Ordinal)
                        && 调试器.Contains("取宿主exe(要自包含: true)", StringComparison.Ordinal)
                        && 调试器.Contains("开始后台发布宿主()", StringComparison.Ordinal)
                        // 老口径那句"只看路径在不在"必须真没了:取宿主exe 里不许再出现裸的 File.Exists 返回值
                        && !调试器.Contains("if (File.Exists(p)) return p;", StringComparison.Ordinal),
                        "两边都先判自包含;IDE 那份不是就后台重发");

                    // H36(第225批实测):单文件发布**默认不把原生库打进去** —— Avalonia 那三个
                    // 原生 dll 会留在发布目录旁边 ⇒ ① 判「不是单文件」② 贴出去的 exe 旁边什么都没有、
                    // 桌面壳起不来(界面线程 15 秒进不了消息循环)⇒ `打开窗口` 一律给 假、看不见窗口。
                    记录("打包方式 宿主:H36 —— 自包含单文件把 Avalonia 的原生库也打进去(不然贴出去的 exe 起不了桌面壳)",
                        调试器.Contains("-p:IncludeNativeLibrariesForSelfExtract=true", StringComparison.Ordinal)
                        && 打包规格C.发布宿主改法.Contains("IncludeNativeLibrariesForSelfExtract=true", StringComparison.Ordinal),
                        "发布命令与那句改法都带上这个开关(发布目录不留原生 dll,单文件自己解到临时目录)");

                    string 帮助 = 帮助文本C.快速上手() + 帮助文本C.指令速查() + 帮助文本C.中间层速查() + 帮助文本C.关于();
                    记录("打包方式 帮助正文写出三档与「不带 .NET」的口径(撤掉那一档的正文与键都清干净)",
                        帮助.Contains("三档打包方式", StringComparison.Ordinal)
                        && 打包规格C.全部.All(种 => 帮助.Contains(种.名, StringComparison.Ordinal))
                        && 帮助.Contains("不带 .NET", StringComparison.Ordinal)
                        && 帮助.Contains("目标机器什么都不用装", StringComparison.Ordinal)
                        && !帮助.Contains("轻量", StringComparison.Ordinal)
                        && !帮助.Contains("host_fx", StringComparison.Ordinal)
                        && 帮助.Contains("打包方式.json", StringComparison.Ordinal)
                        && 帮助.Contains("一个 exe 都不产", StringComparison.Ordinal),
                        "三档 + 「不带 .NET」+ 只编译不产 exe 都说清了;撤掉那一档在帮助里一点痕迹不留");
                }
            }
            catch (Exception ex) { 记录异常("打包方式 按源码钉(CLI / IDE / 帮助)", ex); }

            // ── 四之二、R3-6-4 批 4 收尾:用户 2026-09-23 报的「选 C 编译 / 一切回 C 就卡死」那一刀 ──
            // 三条"界面不再冻"的接线 + 两条引擎侧(候选惰性、兜底扫盘带预算)+ 一条纯函数(预算按大小给)。
            try
            {
                if (根 == null)
                {
                    跳过("打包方式 后台:原生 C 那条腿下后台(界面不冻在 cl 上)", "找不到仓库根");
                    跳过("打包方式 后台:同一份程序同一档不重做 + 后台一次只跑一条", "找不到仓库根");
                    跳过("打包方式 工具链:候选表是惰性的(不为列候选先扫一遍盘)", "找不到仓库根");
                    跳过("打包方式 工具链:兜底扫盘带预算 + 结果缓存(目录树再大也有上限)", "找不到仓库根");
                    跳过("打包方式 工具链:大源码等满预算就不再连试下一套(白等两轮 180 秒那件事)", "找不到仓库根");
                    跳过("打包方式 工具链:编译预算按源码大小给(小源码打底,大源码更长且封顶)", "找不到仓库根");
                    跳过("打包方式 后台:大工程先打个招呼(不让用户对着不动的界面猜)", "找不到仓库根");
                }
                else
                {
                    string 调试器 = File.ReadAllText(Path.Combine(根, "MainWindow.运行与调试器.cs"));
                    记录("打包方式 后台:原生 C 那条腿下后台(界面不冻在 cl 上)",
                        调试器.Contains("原生C排后台(输出, 当前内存大小)", StringComparison.Ordinal)
                        && 调试器.Contains("Task.Run(后台编原生C)", StringComparison.Ordinal)
                        && 调试器.Contains("private void 后台编原生C()", StringComparison.Ordinal)
                        && 调试器.Contains("编译框.AppendText($\"\\n{果.说明}\\n\")", StringComparison.Ordinal)
                        // 失败那次不许记进"已做"(不然切回来会说"已是最新" —— 那是假话)
                        && 调试器.Contains("if (果.成) _原生C已做 = 这;", StringComparison.Ordinal)
                        // H38-2:后台那一段攥的是**寄存号**(界面攥不住引擎对象)
                        && 调试器.Contains("前端契约C.服务.编译.寄存结果(前端契约C.服务.编译.最近结果!)", StringComparison.Ordinal)
                        && 调试器.Contains("前端契约C.服务.编译.按号转译成C(结果号, 内存大小)", StringComparison.Ordinal),
                        "cl 是分钟级的活:交给后台线程,编好了把结果贴回编译页");

                    记录("打包方式 后台:同一份程序同一档不重做 + 后台一次只跑一条",
                        调试器.Contains("同一条(_原生C已做, 输出, 结果号)", StringComparison.Ordinal)
                        && 调试器.Contains("_原生C待做 = 这一条;", StringComparison.Ordinal)
                        && 调试器.Contains("if (_原生C任务 is { IsCompleted: false })", StringComparison.Ordinal)
                        && 调试器.Contains("**已是最新**", StringComparison.Ordinal),
                        "切走再切回来:产物还在就说已是最新;后台忙的时候只留最新那条");

                    记录("打包方式 后台:大工程先打个招呼(不让用户对着不动的界面猜)",
                        调试器.Contains("估算C源码字节(机器码字节数) >= 1024 * 1024", StringComparison.Ordinal)
                        && 调试器.Contains("这一档本来就是分钟级的活", StringComparison.Ordinal),
                        "3 MB C 源码那种工程,先明说\"要几分钟、不用等着看\"");

                    string 工具链 = File.ReadAllText(Path.Combine(根, "C后端", "C工具链C.cs"));
                    记录("打包方式 工具链:候选表是惰性的(不为列候选先扫一遍盘)",
                        !工具链.Contains("列候选().ToList()", StringComparison.Ordinal)
                        && 工具链.Contains("foreach (var 编译器 in 列候选())", StringComparison.Ordinal),
                        "以前 ToList() 一下就等于先把兜底扫盘跑完(哪怕第一套 clang 就能编)");

                    记录("打包方式 工具链:兜底扫盘带预算 + 结果缓存(目录树再大也有上限)",
                        工具链.Contains("public const int 扫盘预算毫秒", StringComparison.Ordinal)
                        && 工具链.Contains("public const int 扫盘目录上限", StringComparison.Ordinal)
                        && 工具链.Contains("if (!预算.还有()) yield break;", StringComparison.Ordinal)
                        && 工具链.Contains("_扫盘找到", StringComparison.Ordinal)
                        && 工具链.Contains("扫盘找VS根()", StringComparison.Ordinal),
                        "扫盘只当兜底(排在 vswhere / 标准安装位置后面),扫过就记住,到预算就停");

                    记录("打包方式 工具链:大源码等满预算就不再连试下一套(白等两轮 180 秒那件事)",
                        工具链.Contains("public static int 编译预算秒(string c源文件)", StringComparison.Ordinal)
                        && 工具链.Contains("if (超时) return 没编完(c源文件, 日志.ToString());", StringComparison.Ordinal)
                        && 工具链.Contains("if (超时) return 没编完(c源文件, 全部.ToString());", StringComparison.Ordinal)
                        && 工具链.Contains("不再换别的编译器接着等", StringComparison.Ordinal),
                        "实测 3.1 MB 的 C 源码:两个候选各等 180 秒 = 白等 6 分钟还是失败");

                    // 纯函数:预算按源码大小给(小源码 180 秒打底,大源码更长、且封顶 —— 不许变成"无上限")
                    // 先把这个环境变量按住(用户机器上可能手设过),比完再放回去
                    string? 老超时 = Environment.GetEnvironmentVariable("HB_C编译器超时");
                    try
                    {
                        Environment.SetEnvironmentVariable("HB_C编译器超时", null);
                        Directory.CreateDirectory(暂存);
                        string 小源 = Path.Combine(暂存, "预算小.c");
                        string 大源 = Path.Combine(暂存, "预算大.c");
                        File.WriteAllText(小源, "int main(void) { return 0; }\n", new UTF8Encoding(false));
                        using (var 流 = new FileStream(大源, FileMode.Create, FileAccess.Write)) 流.SetLength(3 * 1024 * 1024);
                        int 小预算 = C工具链C.编译预算秒(小源);
                        int 大预算 = C工具链C.编译预算秒(大源);
                        记录("打包方式 工具链:编译预算按源码大小给(小源码打底,大源码更长且封顶)",
                            小预算 == 180 && 大预算 > 小预算 && 大预算 <= 10 * 60,
                            $"小源码 {小预算} 秒 / 3 MB 源码 {大预算} 秒");
                    }
                    finally { Environment.SetEnvironmentVariable("HB_C编译器超时", 老超时); }
                }
            }
            catch (Exception ex) { 记录异常("打包方式 后台(批 4 收尾 卡死那一刀)", ex); }

            // ── 五、本机偏好:换档记得住、认不出来回缺省(读写都在 打包规格C 里,引擎侧) ──
            try
            {
                // 一律在**临时目录**里试:绝不去碰开发机上真正那份偏好文件(用户 GUI 里选的那一档)
                string 偏好目录 = Path.Combine(暂存, "偏好");
                Directory.CreateDirectory(偏好目录);
                string 真 = 打包规格C.偏好路径(偏好目录);

                打包规格C.写偏好(打包规格C.只编译, 偏好目录);
                bool 记得住 = 打包规格C.取规格(打包规格C.读取偏好(偏好目录))?.名 == 打包规格C.只编译;
                string 原文 = File.ReadAllText(真);
                打包规格C.写偏好("乱写的那一档", 偏好目录);        // 不该被写进去
                bool 没被带坏 = File.ReadAllText(真) == 原文;
                记录("打包方式 本机偏好:写进去读得回、乱名字写不进(不把坏值存下来)",
                    记得住 && 没被带坏 && 原文 == 打包规格C.全部.First(种 => 种.名 == 打包规格C.只编译).键,
                    $"文件里存的是 {原文}");

                File.WriteAllText(真, "{ 这不是 json");
                记录("打包方式 本机偏好:文件被改坏 → 回缺省档,不抛异常、不挡启动",
                    打包规格C.取规格(打包规格C.读取偏好(偏好目录)) != null
                    && 打包规格C.取规格(打包规格C.缺省档()) != null,
                    $"读回 {打包规格C.读取偏好(偏好目录)}(缺省档现在是 {打包规格C.缺省档()})");
            }
            catch (Exception ex) { 记录异常("打包方式 本机偏好", ex); }

            // ── 六、H35:宿主**是不是这一版**(自报能力 + 陈旧判据 + 两条腿都不贴旧尾巴) ──
            // 病因见 `未做任务清单.md` §22:老口径只判"路径在不在 + 够不够大",于是"够大但过时"的
            // 那份自包含宿主照贴不误(图形能力落地之前发布的),贴出来的 exe 里 `打开窗口` 一律给 假。
            // 这里钉三样:① 纯函数(假的能力清单就能判)② 真起一次进程 ③ 两条腿的源码接线 + 帮助口径。
            try
            {
                // F-4(2026-10-16):判"够不够新"是**四条**轴 —— 功能号 / 包格式 / **指令集** / 会不会自报。
                // 为什么补指令集那一轴:示例63 那次实测,一份 ISA 扩容**前一天**编的宿主在功能号与包格式上
                // **报得跟当前代码一模一样**(85 / 4),于是被判"够新"照用,而它把 `0x4E` 当未知操作码 ⇒
                // 用户看到的是"跑程序通道错误:子进程报『未知操作码』"。所以这一轴的"够新"要**单独钉**。
                // ⚠ 假清单按**真实的能力清单格式**拼(一行一条判据;`能力行 = 0` 的那一行**不写**):
                //   把两行挤成一行会被解析器当成"后面那一条没报"(F-4 实测踩到,红的是测试数据不是判据)。
                string 假清单(int 号 = 0, int 包 = 0, int 集 = -1)
                {
                    var 行 = new List<string>
                    {
                        宿主能力C.能力标记,
                        $"{宿主能力C.能力行最大功能号}={(号 > 0 ? 号 : 宿主能力C.当前最大功能号)}",
                    };
                    if (包 > 0) 行.Add($"{宿主能力C.能力行包格式}={包}");
                    if (集 >= 0) 行.Add($"{宿主能力C.能力行指令集}=0x{集:X2}");
                    return string.Join("\n", 行);
                }
                int 当前集 = 宿主能力C.当前指令集上界;
                string 全套 = 假清单(包: 程序包C.当前包格式, 集: 当前集);

                var 够新 = 宿主能力C.解析能力清单(宿主能力C.能力清单(), 0);
                var 老宿主 = 宿主能力C.解析能力清单("这是 HB 程序宿主,里面没有贴程序。\n用法:用 hbcc 生成", 2);
                var 只到16号 = 宿主能力C.解析能力清单(假清单(号: 16), 0);
                // G3 流加入后补的这一档:流那一档到齐(54)、图形那一档(60~69)与 `当前毫秒`(55)没有
                // ⇒ 原因该**一条一条点明**(流后面那两档都得说出来,不能只挑一段)
                var 只到54号 = 宿主能力C.解析能力清单(假清单(号: 宿主能力C.流功能号最大), 0);
                var 答不上 = 宿主能力C.解析能力清单("", 0);
                var 比当前还高 = 宿主能力C.解析能力清单(假清单(号: 宿主能力C.当前最大功能号 + 4, 集: 当前集), 0);
                // H26 那条轴:会自报能力 **不等于** 读得动新包 —— 各拿一份假清单钉住
                var 不报包格式 = 宿主能力C.解析能力清单(假清单(集: 当前集), 0, 0, 程序包C.包格式_段初值);
                var 只读逐条包 = 宿主能力C.解析能力清单(
                    假清单(包: 程序包C.包格式_逐条初值, 集: 当前集), 0, 0, 程序包C.包格式_段初值);
                var 看包格式但没要求 = 宿主能力C.解析能力清单(假清单(包: 程序包C.包格式_逐条初值, 集: 当前集), 0);
                // F-4 那条轴:功能号与包格式**都够新**,只有指令集那一轴不行 —— 这三份就是"示例63 那次的壳"
                var 不报指令集 = 宿主能力C.解析能力清单(假清单(包: 程序包C.当前包格式, 集: -1), 0);
                var 指令集旧 = 宿主能力C.解析能力清单(假清单(包: 程序包C.当前包格式, 集: 当前集 - 1), 0);
                var 指令集补上就够新 = 宿主能力C.解析能力清单(全套, 0);
                // 反向:ISA 报得比当前**新**(理论上不该出现)不许被误判成陈旧 —— 同 `比当前还高` 那一档的口径
                var 指令集比当前还新 = 宿主能力C.解析能力清单(
                    假清单(包: 程序包C.当前包格式, 集: Math.Min(0xFF, 当前集 + 1)), 0);

                记录("陈旧宿主 H35:会自报且够得上的那份判「够新」(能力清单由 能力清单() 生成、原样认得出)",
                    够新.陈旧 == null && 够新.会自报 && 够新.最大功能号 == 宿主能力C.当前最大功能号,
                    $"自报最大功能号 {够新.最大功能号}(该到 {宿主能力C.当前最大功能号})");

                记录("陈旧宿主 H26:能力清单里报了「读包能力」这一档(当前最高 = 带源行表的包,包格式 4)",
                    够新.包格式 == 程序包C.包格式_源行表
                    && 宿主能力C.能力清单().Contains($"{宿主能力C.能力行包格式}={程序包C.包格式_源行表}", StringComparison.Ordinal),
                    $"自报包格式 {够新.包格式}");

                记录("陈旧宿主 F-4:能力清单里报了**指令集上界**(= 指令表的最大操作码),那份判「够新」",
                    够新.指令集上界 == 宿主能力C.当前指令集上界
                    && 宿主能力C.能力清单().Contains(
                        $"{宿主能力C.能力行指令集}=0x{宿主能力C.当前指令集上界:X2}", StringComparison.Ordinal)
                    // ⭐ F-4 的**防漂移机检**:自报的那个常量必须跟真表**逐位对** ——
                    //   对不上 = ISA 扩了容、忘了回来改 `宿主能力C.当前指令集上界`;
                    //   那正是 H92 那条老毛病(加了两族能力、忘了补一个数,静默把老宿主任判成够新)。
                    && 宿主能力C.当前指令集上界 == 指令C.全部指令.Max(x => x.操作码),
                    $"自报指令集上界 0x{够新.指令集上界:X2}(该到 0x{宿主能力C.当前指令集上界:X2}," +
                    $"= 指令表最大操作码 0x{指令C.全部指令.Max(x => x.操作码):X2})");

                记录("陈旧宿主 F-4:功能号与包格式**都够新、只有指令集旧**的那份判陈旧 ⭐(示例63 那次的壳)",
                    指令集旧.陈旧 != null && 指令集旧.最大功能号 == 宿主能力C.当前最大功能号
                    && 指令集旧.包格式 == 程序包C.包格式_源行表
                    && 指令集旧.指令集上界 == 宿主能力C.当前指令集上界 - 1
                    && 指令集旧.陈旧.Contains("指令集", StringComparison.Ordinal)
                    && 指令集旧.陈旧.Contains("未知操作码", StringComparison.Ordinal),
                    $"原因:{截断(指令集旧.陈旧 ?? "判新", 80)}");

                记录("陈旧宿主 F-4:**不报**指令集那一行的宿主判陈旧(那正是 ISA 扩容前编出来的那一版)",
                    不报指令集.陈旧 != null && 不报指令集.指令集上界 == 0
                    && 不报指令集.陈旧.Contains(宿主能力C.能力行指令集, StringComparison.Ordinal),
                    $"原因:{截断(不报指令集.陈旧 ?? "判新", 80)}");

                记录("陈旧宿主 F-4:指令集上界补到当前那一份判「够新」(这一条判的是指令集那一轴,不是包格式)",
                    指令集补上就够新.陈旧 == null && 指令集补上就够新.指令集上界 == 宿主能力C.当前指令集上界
                    && 指令集比当前还新.陈旧 == null,
                    $"补上之后判「{指令集补上就够新.陈旧 ?? "够新"}」;报 0x{Math.Min(0xFF, 宿主能力C.当前指令集上界 + 1):X2}(比当前还新)也判「{指令集比当前还新.陈旧 ?? "够新"}」");

                记录("陈旧宿主 F-4:指令集那一轴的「说人话」两种说法(不报那一行 / 报了但旧)",
                    宿主能力C.指令集上限文本(不报指令集).Contains(宿主能力C.能力行指令集, StringComparison.Ordinal)
                    && 宿主能力C.指令集上限文本(指令集旧).Contains("0x", StringComparison.Ordinal)
                    && 指令集旧.陈旧!.Contains("0x", StringComparison.Ordinal),
                    $"不报:{截断(宿主能力C.指令集上限文本(不报指令集), 60)} / 旧:{截断(宿主能力C.指令集上限文本(指令集旧), 60)}");

                记录("陈旧宿主 H26:会自报功能号、却没有「包格式」那一行的宿主判陈旧(正是本批格式一变就贴不出来的那份)",
                    不报包格式.陈旧 != null && 不报包格式.包格式 == 0
                    && 不报包格式.陈旧.Contains(宿主能力C.能力行包格式, StringComparison.Ordinal),
                    $"少包格式那份的原因:{截断(不报包格式.陈旧 ?? "判新", 60)}");

                记录("陈旧宿主 H26:只认逐条初值包(包格式 2)的宿主判陈旧,原因点明是段式包读不了",
                    只读逐条包.陈旧 != null && 只读逐条包.包格式 == 程序包C.包格式_逐条初值
                    && 只读逐条包.陈旧.Contains("段式", StringComparison.Ordinal),
                    $"原因:{截断(只读逐条包.陈旧 ?? "判新", 60)}");

                记录("陈旧宿主 F-4:指令集旧的那一份,原因里点名「未知操作码」并给出新旧上界(用户看到的就是这句)",
                    指令集旧.陈旧!.Contains($"0x{宿主能力C.当前指令集上界 - 1:X2}", StringComparison.Ordinal)
                    && 指令集旧.陈旧.Contains($"0x{宿主能力C.当前指令集上界:X2}", StringComparison.Ordinal)
                    && 指令集旧.陈旧.Contains("指令集", StringComparison.Ordinal),
                    $"原因:{截断(指令集旧.陈旧, 100)}");

                记录("陈旧宿主 F-4:指令集那一行的写法认得出带 `0x` 的十六进制(报的是 0x62 这种,不是十进制)",
                    宿主能力C.能力清单().Contains($"{宿主能力C.能力行指令集}=0x", StringComparison.Ordinal)
                    && 够新.指令集上界 == 宿主能力C.当前指令集上界,
                    $"那一行原文:`{宿主能力C.能力行指令集}=0x{宿主能力C.当前指令集上界:X2}`,解析回来 {够新.指令集上界}");

                记录("陈旧宿主 H26:这次不看包格式(要求 0)时,读包能力旧不算陈旧(没有初值的程序照产)",
                    看包格式但没要求.陈旧 == null && 看包格式但没要求.包格式 == 程序包C.包格式_逐条初值);

                记录("陈旧宿主 H35:老宿主(不认 `--能力`,退出码非 0)判陈旧,原因写明是「不认这句问话」",
                    老宿主.陈旧 != null && !老宿主.会自报 && 老宿主.最大功能号 == 0
                    && 老宿主.陈旧.Contains("不认", StringComparison.Ordinal)
                    && 老宿主.陈旧.Contains(宿主能力C.能力请求参数, StringComparison.Ordinal),
                    $"原因:{截断(老宿主.陈旧 ?? "判新", 60)}");

                记录("陈旧宿主 H35:功能号只到 16 的那份判陈旧(缺 17 号起那一大段)",
                    只到16号.陈旧 != null && 只到16号.会自报 && 只到16号.最大功能号 == 16
                    && 只到16号.陈旧.Contains("缺", StringComparison.Ordinal),
                    $"原因:{截断(只到16号.陈旧 ?? "判新", 60)}");

                记录("陈旧宿主 H35:只到 54(流到齐、`当前毫秒` 与图形那一档没有)的那份,原因把缺的两档都点名",
                    只到54号.陈旧 != null && 只到54号.最大功能号 == 宿主能力C.流功能号最大
                    && 只到54号.陈旧.Contains("当前毫秒", StringComparison.Ordinal)
                    && 只到54号.陈旧.Contains("图形界面", StringComparison.Ordinal),
                    $"原因:{截断(只到54号.陈旧 ?? "判新", 80)}");

                记录("陈旧宿主 H35:答不上来(空输出)判陈旧;自报得比当前还高也算够新(两头都不误判)",
                    答不上.陈旧 != null && 比当前还高.陈旧 == null
                    && 比当前还高.最大功能号 == 宿主能力C.当前最大功能号 + 4);

                记录("陈旧宿主 H35:文件不在也算「答不上来」(不当成够新;这条路不真起进程)",
                    宿主能力C.陈旧原因(Path.Combine(暂存, "绝不存在_宿主.exe")) == "文件不在");

                // 真起一次进程问 `--能力` 这半边也得钉(纯函数钉不住"参数没传对 / 起不来"这类毛病)。
                // 替身挑系统盘上的 where.exe:它肯定不认 `--能力`,又是一闪就结束的控制台程序(不开窗)。
                string 替身 = Path.Combine(Environment.SystemDirectory, "where.exe");
                if (File.Exists(替身))
                {
                    var 真问 = 宿主能力C.问能力(替身);
                    记录("陈旧宿主 H35:真起一次进程问 `--能力` —— 不认这句问话的程序判陈旧(不是「文件不在」)",
                        真问.会自报 == false && 真问.陈旧 != null && 真问.陈旧.Contains("不认", StringComparison.Ordinal),
                        $"替身 {Path.GetFileName(替身)}:{截断(真问.陈旧 ?? "判新", 60)}");
                }
                else
                    跳过("陈旧宿主 H35:真起一次进程问 `--能力` —— 不认这句问话的程序判陈旧(不是「文件不在」)",
                        "这台机器上找不到 where.exe");
            }
            catch (Exception ex) { 记录异常("陈旧宿主 H35 判据(纯函数 + 真起进程)", ex); }

            try
            {
                // 判"这个程序用不用得到宿主没有的能力":扫机器码里 `宿主调用`(0x31)的 20 位立即数。
                // 指令字**小端**、操作码在最后一个字节(与 `图形宿主C.这段机器码用图形` 同一套扫法):
                // 一条"宿主调用 功能号 F"的 4 字节 = [F&0xFF, (F>>8)&0xFF, (F>>16)&0x0F, 0x31]。
                byte[] 没用到 = [0x00, 0x00, 0x00, 0x31];
                byte[] 用到3号 = [0x03, 0x00, 0x00, 0x31];
                byte[] 用到图形 = [0x45, 0x00, 0x00, 0x31];                       // 69(0x45)= 取文字宽度(G3 域 2 起号段 60~69)
                byte[] 两条 = [0x3C, 0x00, 0x00, 0x31, 0x03, 0x00, 0x00, 0x31];   // 60(= 图形功能号最小)然后 3
                记录("陈旧宿主 H35:扫得出「这个程序最多用到几号能力」(多条取最大、没用到给 0)",
                    宿主能力C.这段机器码要的功能号上限(没用到) == 0
                    && 宿主能力C.这段机器码要的功能号上限(用到3号) == 3
                    && 宿主能力C.这段机器码要的功能号上限(用到图形) == 图形宿主C.图形功能号最大
                    && 宿主能力C.这段机器码要的功能号上限(两条) == 图形宿主C.图形功能号最小
                    && 宿主能力C.这段机器码要的功能号上限([]) == 0
                    && 图形宿主C.这段机器码用图形(用到图形)
                    && !图形宿主C.这段机器码用图形(用到3号),
                    $"用到图形那条 = 功能号 {图形宿主C.图形功能号最大}(与 `这段机器码用图形` 同判)");
            }
            catch (Exception ex) { 记录异常("陈旧宿主 H35 机器码扫描", ex); }

            try
            {
                if (根 == null)
                {
                    跳过("陈旧宿主 H35:宿主自己认 `--能力`(问一句就报功能号)", "找不到仓库根");
                    跳过("陈旧宿主 H35:IDE 贴尾巴前判陈旧 —— 够新的优先、用得到它没有的能力就不产", "找不到仓库根");
                    跳过("陈旧宿主 H35:命令行那条腿同一条判据(用得到就不产,用不到就提醒)", "找不到仓库根");
                    跳过("陈旧宿主 H35:帮助《上手》第15节写出「问宿主一句你是哪一版」的口径", "找不到仓库根");
                }
                else
                {
                    string 宿主程序 = File.ReadAllText(Path.Combine(根, "HB宿主", "Program.cs"));
                    记录("陈旧宿主 H35:宿主自己认 `--能力`(问一句就报功能号;老宿主不认 ⇒ 正好被筛出来)",
                        宿主程序.Contains("宿主能力C.能力请求参数", StringComparison.Ordinal)
                        && 宿主程序.Contains("宿主能力C.能力清单()", StringComparison.Ordinal),
                        "HB宿主\\Program.cs");

                    string 调试器 = File.ReadAllText(Path.Combine(根, "MainWindow.运行与调试器.cs"));
                    记录("陈旧宿主 H35:IDE 贴尾巴前判陈旧 —— 够新的优先、用得到它没有的能力就不产",
                        调试器.Contains("前端契约C.服务.宿主.陈旧原因(p, 0, 该到包格式)", StringComparison.Ordinal)
                        && 调试器.Contains("前端契约C.服务.宿主.问能力(宿主, 0, 需要包格式)", StringComparison.Ordinal)
                        && 调试器.Contains("前端契约C.服务.宿主.这段机器码要的功能号上限(当前机器码)", StringComparison.Ordinal)
                        && 调试器.Contains("前端契约C.服务.宿主.段初值包格式", StringComparison.Ordinal)
                        && 调试器.Contains("前端契约C.服务.宿主.包格式上限文本(宿主版本)", StringComparison.Ordinal)
                        && 调试器.Contains("**这一轮没有生成**", StringComparison.Ordinal)
                        && 调试器.Contains("开始后台发布宿主();", StringComparison.Ordinal),
                        "取宿主exe 先挑够新的;贴尾巴生成 里用得到就拒产 + 后台重发(H26 连读包能力一起判)");

                    string cli = File.ReadAllText(Path.Combine(根, "编译器CLI", "Program.cs"));
                    记录("陈旧宿主 H35:命令行那条腿同一条判据(用得到就不产,用不到就提醒 + 给发布命令)",
                        cli.Contains("宿主能力C.问能力(宿主, 0, 需要包格式)", StringComparison.Ordinal)
                        && cli.Contains("宿主能力C.这段机器码要的功能号上限(机器码)", StringComparison.Ordinal)
                        && cli.Contains("程序包C.包格式_段初值", StringComparison.Ordinal)
                        && cli.Contains("宿主能力C.包格式上限文本(宿主版本)", StringComparison.Ordinal)
                        && cli.Contains("这一份不产", StringComparison.Ordinal)
                        && cli.Contains("打包规格C.发布宿主改法", StringComparison.Ordinal),
                        "编译器CLI\\Program.cs");

                    string 帮助 = 帮助文本C.快速上手() + 帮助文本C.指令速查() + 帮助文本C.中间层速查() + 帮助文本C.关于();
                    记录("陈旧宿主 H35:帮助《上手》第15节写出「问宿主一句你是哪一版」的口径",
                        帮助.Contains("问宿主一句", StringComparison.Ordinal)
                        && 帮助.Contains("这一轮不产 exe", StringComparison.Ordinal)
                        && 帮助.Contains("这份宿主比代码旧", StringComparison.Ordinal),
                        "帮助《上手》第15节打包那三档");
                }
            }
            catch (Exception ex) { 记录异常("陈旧宿主 H35 按源码钉(宿主 / IDE / CLI / 帮助)", ex); }

            // ---------- H92-2 接线条①:界面起子进程那两处必须**问过宿主版本** ----------
            // 为什么单钉这一条:H92 的 ③ 是"`找跑程序壳` 只判文件在不在、从不问版本" ⇒ 拿老壳照跑。
            // 修完之后**没有任何断言盯着它** —— 把 `陈旧原因` 那一句删掉,别的断言照样全绿,
            // 而 IDE 里又会退回"新能力静默给错答案"(那正是本缺陷第一次出现的方式)。
            const string H92接线 = "陈旧宿主 H92 接线:界面起子进程那两处都经 `找跑程序壳` 问过宿主版本";
            try
            {
                if (根 == null) 跳过接线(H92接线, "找不到仓库根");
                else
                {
                    string 通道源 = File.ReadAllText(Path.Combine(根, "MainWindow.跑程序通道.cs"), Encoding.UTF8);
                    bool 壳里问了版本 = 通道源.Contains("前端契约C.服务.宿主.陈旧原因(可执行)", StringComparison.Ordinal);
                    int 几处找壳 = 通道源.Split("找跑程序壳(out string 壳旧)").Length - 1;
                    记接线(H92接线, 壳里问了版本 && 几处找壳 == 2,
                        $"`找跑程序壳` 里问了宿主版本 = {壳里问了版本};走 `找跑程序壳` 的地方 = {几处找壳} 处" +
                        "(该 2:`确保跑程序机` / `确保调试机`)—— 接线哨兵(读界面源码文本判的),行为面看别处的真跑");
                }
            }
            catch (Exception ex) { 记录异常(H92接线, ex); }

            // ---------- H92-2 接线条②:逐个列文件的宿主工程必须把引擎文件链齐 ----------
            const string H92工程 = "陈旧宿主 H92 接线:逐个列文件的宿主工程把这一族的引擎文件链齐了";
            try
            {
                if (根 == null) 跳过接线(H92工程, "找不到仓库根");
                else
                {
                    // H92 ① 漏的正是前两个(`宿主弹窗C` / `ObjLoader`);同一个坑 `图形宿主` 漏 `位图仓C` 时栽过一次。
                    // 这里钉的是**最小集**;完整判据仍是 `工具\五端构建.ps1`(六端闸门)——
                    // 新加能力层文件之后一定要跑一次闸门,这条哨兵只是让它在自检里就先响一声。
                    (string 工程, string[] 该有)[] 清单 =
                    [
                        (Path.Combine("HB宿主", "HB宿主.csproj"),
                         ["宿主能力C.cs", "宿主弹窗C.cs", "ObjLoader.cs", "选择器C.cs", "网络能力C.cs"]),
                        (Path.Combine("图形宿主", "图形宿主.csproj"),
                         ["图形宿主C.cs", "图形事件C.cs", "位图仓C.cs", "运行期错误C.cs"]),
                    ];
                    var 缺 = new List<string>();
                    foreach ((string 工程, string[] 该有) in 清单)
                    {
                        string 路径 = Path.Combine(根, 工程);
                        if (!File.Exists(路径)) { 缺.Add($"{工程}(文件不在)"); continue; }
                        string 文 = File.ReadAllText(路径, Encoding.UTF8);
                        foreach (string 一 in 该有)
                            if (!文.Contains(一, StringComparison.Ordinal)) 缺.Add($"{工程}:{一}");
                    }
                    记接线(H92工程, 缺.Count == 0,
                        缺.Count == 0
                            ? $"两个工程共 {清单.Sum(x => x.该有.Length)} 份引擎文件都在" +
                              "(H92 ① 漏的 `宿主弹窗C` / `ObjLoader` 就在这份名单里)"
                            : $"缺 {缺.Count} 处:{string.Join(" ", 缺)}(逐个列文件的工程:新加能力层文件必须回这儿加一行)");
                }
            }
            catch (Exception ex) { 记录异常(H92工程, ex); }

            // ---------- H92-3:反射机检 —— 每一个 `功能_*` 常量都必须落在某个能力族段里 ----------
            // 为什么要有这一条:`能力族` 原来只登记了"影响 max 的那几族",`1~16` 与 `56` 不在里面 ⇒
            // "新加一个能力只登记一处"这件事还是靠人记得(H92 ② 就是这么来的)。H92-3 把族表补成全量,
            // 这一条反射机检就负责**以后别再漏**:盘上有一个 `功能_*` 落在所有段之外就红灯。
            const string H92族表 = "陈旧宿主 H92-3:每个 `功能_*` 常量都落在某个 `能力族` 段的号段里(反射机检)";
            try
            {
                var 段 = 宿主能力C.能力族表;
                var 漏 = new List<string>();
                int 数了 = 0;
                foreach (Type 类 in new[] { typeof(宿主能力C), typeof(图形宿主C) })
                    foreach (var 字段 in 类.GetFields(System.Reflection.BindingFlags.Public
                                                     | System.Reflection.BindingFlags.Static))
                    {
                        if (字段.FieldType != typeof(int)) continue;
                        if (!字段.Name.StartsWith("功能_", StringComparison.Ordinal)) continue;
                        if (字段.GetRawConstantValue() is not int 号) continue;
                        数了++;
                        if (!段.Any(x => 号 >= x.最小 && 号 <= x.最大)) 漏.Add($"{类.Name}.{字段.Name}={号}");
                    }
                记录(H92族表, 漏.Count == 0 && 数了 > 0,
                    漏.Count == 0
                        ? (数了 > 0
                            ? $"{数了} 个 `功能_*` 常量全部落在 {段.Count} 段里" +
                              "(1~16 / 20~28 / 40~42 / 43~47 / 48~54 / 55 / 56 / 60~69 / 70~77 / 80~85)"
                            : "一个 `功能_*` 常量都没反射到 —— 反射这条路本身断了")
                        : $"{漏.Count} 个落在段外:{string.Join(" ", 漏.Take(8))}" +
                          "(回 `宿主能力C.能力族` 加一行,或把新号补进对应那一段)");
            }
            catch (Exception ex) { 记录异常(H92族表, ex); }

            try { if (Directory.Exists(暂存)) Directory.Delete(暂存, true); } catch { }
        }
    }
}
