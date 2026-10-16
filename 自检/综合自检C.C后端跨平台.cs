using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R3-6-4 批 5:**跨平台验证** —— 同一份 C 源码在 Linux 的 `cc` 上编一遍、跑出同一结果。
    ///
    /// 为什么值得单列一组:批 1~4 只验过 **Windows / MSVC** 那条腿 —— 运行时里 15 处平台差异件
    /// 走的全是 `_WIN32` 那一半,POSIX 那一半(opendir / unlink / rmdir / stat …)**一次都没被编过**。
    /// 这一组就是那一次的"机器验"。
    ///
    /// 分两半,与 `C后端` 那几组同一个口径:
    ///   · **纯源码那半**(不要 Linux 运行器):运行时.c 的 POSIX 那一半得真是实现,不是空壳;
    ///   · **真编真跑那半**(要有 Linux 运行器):同一份 `.c` 交给容器里的 `cc`,跑出来的**程序输出**与
    ///     **整机状态**(寄存器 + 整块内存)跟 **C# 虚拟机**逐字节比 —— 与批 2 在 Windows 上的判据一字不差。
    /// 没有运行器(docker 没装 / 没起 / 镜像不在本地)**逐条如实记跳过**(R4-2 的口径:跳过不许冒充通过)。
    ///
    /// 默认只跑**挑出来的 6 个标准工程**(快,覆盖面够:类型 / 递归 / 字符串 / 堆 / 双精度 / 宿主能力);
    /// 想 26 个标准工程一个不落全扫一遍,设 `HB_C跨平台全量=1` 再跑自检。
    ///
    /// **macOS 这一批没有真机可验** —— 生成的 C 与 POSIX 那一半跟 Linux 是同一份,但没有 macOS 上的
    /// `cc` 实测过,如实记着(见 `未做任务清单.md` §21)。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary>本组**真编真跑**那几条的名字(没有 Linux 运行器就按这些名字逐条记跳过)</summary>
        private static readonly string[] C后端跨平台断言 =
        [
            "C后端 跨平台 Linux 上 cc 编得过、程序跑得起来(同一份 C 源码)",
            "C后端 跨平台 Linux 上的输出与 C# 虚拟机逐字节一致(函数 / 递归 / 字符串 / 数组)",
            "C后端 跨平台 Linux 上的整机状态(寄存器 + 整块内存)与虚拟机逐字节一致",
            "C后端 跨平台 Linux 上走的是标签表(computed goto)那条分派路",
            "C后端 跨平台 Linux 上运行期错误带源码行号、退出码非 0(除零)",
            "C后端 跨平台 Linux 上标准工程真编真跑(输出 + 整机状态一致)",
            "C后端 跨平台 Linux 上宿主能力(建目录 / 写文件 / 读文件 / 列目录 / 删文件)也一致",
            // 第333批补:第332批新加了"图形呈现层(X11)"这一条,却忘了把它的名字加进这张名单 ⇒
            // 没有 Linux 运行器时**它一次都不登记**,断言总数随环境少一条(2848/2849),「断言清单指纹」与
            // 「接手说明 §4 本次运行结果」两条当场红。这张名单是"环境缺件时逐条记跳过"的**唯一依据**,
            // 以后新加"要 Linux 才跑得动"的断言,**名字要同时加到这里**。
            "C后端 跨平台 Linux 上图形呈现层(X11)真编真跑:没有 X 时如实给状态 25(有 X 时从已提交那一帧读回像素)",
        ];

        /// <summary>默认挑这几个标准工程上 Linux 真跑(名字 = `Sample\中间层\` 下的目录名)</summary>
        private static readonly string[] 跨平台挑的工程 =
        [
            "示例01_类型与字面量",
            "示例04_函数与递归",
            "示例07_字符串",
            "示例09_堆与数据结构",
            "示例11_双精度",
            "示例12_输入输出与宿主能力",
        ];

        /// <summary>
        /// 这一轮**真跑了几条**"要 Linux 才跑得动"的断言(0 = 一条没跑 = 不带 Linux 档)。
        ///
        /// <para>
        /// **拿它做什么**:`接手说明.md` §4 那三个数(通过 / 失败 / 跳过)**一律按"不带 Linux 档"记** ——
        /// 带档那一轮,上面那张名字表里那几条从"跳过"换成"真跑" ⇒ 通过 +N / 跳过 -N(N = 这张表的长度,
        /// 实测 8);不折算的话 §4 与那一轮永远对不上(`文档数字 接手说明的当前数字是本次运行的结果` 当场红),
        /// 而"每夜自动真验 Linux"恰恰就得在**开了档**的进程里跑一遍全量自检 ⇒ 结构性做不到。
        /// </para>
        /// <para>
        /// **判据 = 那几条在报告里有没有"登记过结果"**(`PASS` 或 `FAIL`,都算真跑;只有 `SKIP` 才算没跑)。
        /// 为什么不看"跳过名单里有没有它":`HB_自检只跑=…` 那种**筛选跑法**下,没被筛上的组一条都不登记 ——
        /// 拿"没跳过"当"真跑了"会把筛选跑法误判成开了档(实测踩过:筛选那一趟详情里写着"带 Linux 档真跑了 8 条")。
        /// </para>
        /// <para>
        /// **折算只用在这儿**:判据本身一个字没放宽 —— 名字表与"哪几条真跑了"是同一个事实源
        /// (这张表就是"环境缺件时逐条记跳过"的唯一依据,见它上面的注释),手改 §4 一个数字照旧红灯。
        /// 两个折算点在 `自检\综合自检C.cs` 的 T21 那条判据与 `编译器CLI\Program.cs` 的 `--stats --更新报告`。
        /// </para>
        /// </summary>
        public static int Linux档真跑条数(IEnumerable<string> 报告行)
        {
            var 行 = 报告行 as string[] ?? 报告行.ToArray();
            static bool 登记过结果(string x) =>
                x.StartsWith("PASS", StringComparison.Ordinal) || x.StartsWith("FAIL", StringComparison.Ordinal);
            return C后端跨平台断言.Count(名 => 行.Any(x => 登记过结果(x) && x.Contains(名, StringComparison.Ordinal)));
        }

        private static void C后端跨平台自检()
        {
            const int 内存 = 虚拟机C.默认内存大小;

            // ── 一、纯源码那半:运行时.c 的 POSIX 那一半得真是实现 ──
            try
            {
                string 运行期 = C后端C.取运行时源码();
                string[] POSIX件 = ["<dirent.h>", "<unistd.h>", "opendir(", "readdir(", "unlink(", "rmdir(", "S_ISDIR", "stat("];
                string[] 缺 = POSIX件.Where(x => !运行期.Contains(x, StringComparison.Ordinal)).ToArray();
                int 平台件 = 数出现(运行期, "#ifdef _WIN32");
                int 对子 = 数出现(运行期, "\n#else");
                记录("C后端 跨平台 运行时.c 的 POSIX 那一半是真实现(不是空壳)",
                    缺.Length == 0 && 平台件 >= 10 && 对子 >= 10,
                    $"平台差异件 {平台件} 处(带 `#else` 对子 {对子} 处);POSIX 侧缺的件:" +
                    (缺.Length == 0 ? "一个不缺" : string.Join(" / ", 缺)));
            }
            catch (Exception ex) { 记录异常("C后端 跨平台 运行时.c 的 POSIX 那一半", ex); }

            // ── 一·五、R3-6-5 批 2:X11 呈现层那几件得真接上(源码钉;行为面由下面真编真跑那一条兜) ──
            // 为什么单列:这一档的"真开窗 + 从窗口读回像素"要一台有 X 的机器才跑得起来,而**接线**这一半
            // 在任何机器上都该是稳的 —— 少一个 `dlsym` 名字,那一档在真 X 上就静悄悄地退化成"读不回来"。
            try
            {
                string 运行期 = C后端C.取运行时源码();
                string[] 该有 = [
                    "#elif defined(__linux__)", "libX11.so.6", "dlopen(", "RTLD_LAZY",
                    "XCreateSimpleWindow", "XMapWindow", "XFillRectangle", "XDrawLine", "XDrawString",
                    "XCreateFontSet", "Xutf8DrawString",
                    "XGetWindowAttributes", "XGetImage", "XGetPixel", "XDestroyImage",
                    "hb_win_键码", "hb_win_鼠标码", "0xFFBEu", "hb_宽开(\"/dev/null\"",
                ];
                var 缺X = 该有.Where(x => !运行期.Contains(x, StringComparison.Ordinal)).ToList();
                // 这几件是"读回像素"那一档才用的:写成**可缺**(缺了整档照编,只是少一项便利)——
                // 钉住"它们在符号表里、而且是可缺那一档",别让谁顺手把可缺改成必需(老 Xlib 上就整档不成了)。
                bool 可缺 = 运行期.Contains("\"XGetImage\", (void **)&hb_x.XGetImage, 1", StringComparison.Ordinal)
                    && 运行期.Contains("\"XGetWindowAttributes\", (void **)&hb_x.XGetWindowAttributes, 1", StringComparison.Ordinal);
                记录("C后端 跨平台 X11 呈现层接线:窗口 / 画 / 字体 / 读回像素那几件都在(读回那三件标成可缺)",
                    缺X.Count == 0 && 可缺,
                    $"该有的件 {该有.Length} 件,缺 {缺X.Count} 件" +
                    (缺X.Count > 0 ? "(" + string.Join(" / ", 缺X) + ")" : "") +
                    $";读回像素那三件标了【可缺】= {可缺}(老 Xlib 上整档照样编得过)");
            }
            catch (Exception ex) { 记录异常("C后端 跨平台 X11 呈现层接线", ex); }

            // ── 一·六、H113:X11 那一条同一个位置上的"钉文件"那一类 —— **必须挂在没有运行器也照跑的那半** ──
            // 为什么挂这儿(第451批的教训,别再挪回去):
            //   · 这条钉的是"这一组有没有把 `当前毫秒()` 也冻上"(原来只冻秒、`--hb-now` 也只补秒 ——
            //     组里一旦有工程用 `当前毫秒()`,`HB_C跨平台全量=1` 全扫就会撞上 `示例28_定时器` 那一族,
            //     两边各看各的真钟 ⇒ 整机逐字节比早晚假红);
            //   · **不能挂到下面"真编真跑"那半**:那一半没有 Linux 运行器时整组逐条记**跳过** ⇒
            //     这条断言会**看环境决定在不在**,而 `工具\自检断言清单.txt` 是进 git 的基线:
            //     夜里巡检**开着** Linux 档那一趟会当场红(第451批实测撞过:同一份代码,带档那一趟
            //     清单指纹 `F1C18AF2F4B2`、不带档那一趟 `A7783766D4EC`,条数都是 3364)。
            //   · 三处一起钉(组里换口子 / 工具链认开关 / 调用点真递下去)—— 少一处就是"又只剩秒"。
            try
            {
                string? 根H113 = 找仓库根目录();
                var 跨平台缺 = new List<string>();
                string 这组 = 根H113 == null ? "" : Path.Combine(根H113, "自检", "综合自检C.C后端跨平台.cs");
                string 工具链 = 根H113 == null ? "" : Path.Combine(根H113, "C后端", "跨平台工具链C.cs");
                if (根H113 == null || !File.Exists(这组)) 跨平台缺.Add(@"`自检\综合自检C.C后端跨平台.cs` 不在");
                else if (!File.ReadAllText(这组).Contains("宿主能力C.取当前毫秒 = () => 冻结毫秒;", StringComparison.Ordinal))
                    跨平台缺.Add("这一组没把虚拟机侧的 `取当前毫秒` 冻上");
                if (根H113 == null || !File.Exists(工具链)) 跨平台缺.Add(@"`C后端\跨平台工具链C.cs` 不在");
                else
                {
                    if (!File.ReadAllText(工具链).Contains("--hb-now-ms", StringComparison.Ordinal))
                        跨平台缺.Add("工具链不认 `--hb-now-ms`");
                    // 认了还不够:**得真递过去**(参数挂上了、调用点没传,等于没冻)——
                    // 这一组只有 `对拍一个工程在Linux` 那一个调用点在比整机内存,补的就是它。
                    else if (!File.ReadAllText(这组).Contains("现在毫秒: 冻结毫秒", StringComparison.Ordinal))
                        跨平台缺.Add("标准工程那一趟没把 `现在毫秒: 冻结毫秒` 递下去");
                }
                记录("H113 跨平台那一组也冻 `当前毫秒()`:组里换掉宿主那个口子 + 原生侧递 `--hb-now-ms`",
                    跨平台缺.Count == 0,
                    跨平台缺.Count == 0
                        ? "这一组设 `宿主能力C.取当前毫秒 = () => 冻结毫秒`(与秒同一个刻度)," +
                          "工具链把 `--hb-now-ms` 补进容器里那条命令 ⇒ 两侧读的是同一个数"
                        : string.Join(" / ", 跨平台缺));
            }
            catch (Exception ex) { 记录异常("H113 跨平台那一组也冻 `当前毫秒()`", ex); }

            // ── 二、找 Linux 运行器(没有就逐条如实记跳过) ──
            string? 根 = 找仓库根目录();
            if (根 == null)
            {
                foreach (string 名 in C后端跨平台断言) 跳过(名, "找不到仓库根(不在工作副本里跑)");
                return;
            }
            if (!跨平台工具链C.探一下(out string 运行器))
            {
                foreach (string 名 in C后端跨平台断言) 跳过(名, "这台机器上没有能用的 Linux 运行器 —— " + 运行器);
                return;
            }

            string 暂存 = Path.Combine(根, "工具", "临时", "跨平台验证", Environment.ProcessId.ToString());
            Directory.CreateDirectory(暂存);
            string 原目录 = Directory.GetCurrentDirectory();
            try
            {
                // 容器里 `-w /hb` 就是仓库根:标准工程里的相对路径(示例12 那几条)两边才对得上同一批文件
                Directory.SetCurrentDirectory(根);

                // ── 三、最小程序:cc 编得过 + 程序跑得起来(这一条只判这两样) ──
                try
                {
                    var 最小编译 = 编译("整数 结果 = 42;\n输出(结果);\n输出换行;\n停止\n");
                    string 最小c = Path.Combine(暂存, "最小.c");
                    File.WriteAllText(最小c, C后端C.转译(最小编译, 内存).源码, new UTF8Encoding(false));
                    var 最小跑 = 跨平台工具链C.编并跑(根, 最小c, Path.Combine(暂存, "最小程序"), "-O2");
                    记录("C后端 跨平台 Linux 上 cc 编得过、程序跑得起来(同一份 C 源码)",
                        最小跑.编过 && 最小跑.退出码 == 0 && 最小跑.输出文字 == "42\n",
                        $"cc -O2 编过 = {最小跑.编过};退出码 {最小跑.退出码};输出「{截断(最小跑.输出文字, 60)}」" +
                        (最小跑.日志.Length > 0 ? ";cc / 程序的话:" + 截断(最小跑.日志, 140) : ""));
                }
                catch (Exception ex) { 记录异常("C后端 跨平台 Linux 上 cc 编得过、程序跑得起来", ex); }

                // ── 四、函数 / 递归 / 字符串 / 数组:-O2 编一遍,输出与整机状态跟虚拟机逐字节比 ──
                try
                {
                    var 递归编译 = 编译(是函数与递归源());
                    string 递归c = Path.Combine(暂存, "函数与递归.c");
                    File.WriteAllText(递归c, C后端C.转译(递归编译, 内存).源码, new UTF8Encoding(false));
                    string 递归状态 = Path.Combine(暂存, "函数与递归.状态");
                    var 递归跑 = 跨平台工具链C.编并跑(根, 递归c, Path.Combine(暂存, "函数与递归程序"), "-O2", 递归状态);
                    var (虚拟机出, 虚拟机误, 虚拟机状态) = 虚拟机跑一侧(递归编译, 内存, null);
                    虚拟机状态.退出码 = 0;

                    bool 输出同一 = 递归跑.编过 && 递归跑.输出.AsSpan().SequenceEqual(虚拟机出);
                    记录("C后端 跨平台 Linux 上的输出与 C# 虚拟机逐字节一致(函数 / 递归 / 字符串 / 数组)",
                        输出同一,
                        输出同一
                            ? $"Linux 侧 {递归跑.输出.Length} 字节与虚拟机逐字节一致;退出码 {递归跑.退出码}"
                            : $"编过 = {递归跑.编过};退出码 {递归跑.退出码};Linux {递归跑.输出.Length} 字节" +
                              $"「{截断(递归跑.输出文字, 70)}」/ 虚拟机 {虚拟机出.Length} 字节" +
                              $"「{截断(Encoding.UTF8.GetString(虚拟机出), 70)}」" +
                              (虚拟机误.Length > 0 ? ";虚拟机那侧还报了:" + 截断(虚拟机误, 80) : ""));

                    var 原生状态 = C状态C.读原生状态(递归状态);
                    var 状态比 = 原生状态 == null ? null : C状态C.比(虚拟机状态, 原生状态, 递归跑.输出, 递归跑.退出码);
                    记录("C后端 跨平台 Linux 上的整机状态(寄存器 + 整块内存)与虚拟机逐字节一致",
                        状态比 is { 一致: true },
                        (状态比 == null ? "Linux 那侧没落下状态转储(程序没跑起来?)" : 状态比.说明) +
                        $";{运行器}");

                    string 分派方式 = 取分派方式(递归跑.日志);
                    记录("C后端 跨平台 Linux 上走的是标签表(computed goto)那条分派路",
                        分派方式.Contains("标签表", StringComparison.Ordinal),
                        ($"Linux 侧自报「{(分派方式.Length == 0 ? "(没自报)" : 分派方式)}」" +
                         "—— 开发机上那套 MSVC 只走 switch(PC),标签表那条是批 2 如实记下的" +
                         "「要在有 clang / gcc 的机器上才验得到」,在这儿真验到了"));
                }
                catch (Exception ex) { 记录异常("C后端 跨平台 Linux 上的输出与整机状态", ex); }

                // ── 五、运行期错误:带源码行号 + 退出码非 0 ──
                try
                {
                    var 除零编译 = 编译("整数 除数 = 0;\n整数 商 = 100 / 除数;\n输出(商);\n输出换行;\n停止\n");
                    string 除零c = Path.Combine(暂存, "除零.c");
                    File.WriteAllText(除零c, C后端C.转译(除零编译, 内存).源码, new UTF8Encoding(false));
                    var 除零跑 = 跨平台工具链C.编并跑(根, 除零c, Path.Combine(暂存, "除零程序"), "-O2");
                    bool 对 = 除零跑.编过 && 除零跑.退出码 != 0
                        && 除零跑.日志.Contains("除零", StringComparison.Ordinal)
                        && 除零跑.日志.Contains("行", StringComparison.Ordinal);
                    记录("C后端 跨平台 Linux 上运行期错误带源码行号、退出码非 0(除零)", 对,
                        $"退出码 {除零跑.退出码}(期望非 0);stderr = {截断(除零跑.日志, 140)}");
                }
                catch (Exception ex) { 记录异常("C后端 跨平台 Linux 上运行期错误带源码行号", ex); }

                // ── 六、标准工程(默认挑 6 个;`HB_C跨平台全量=1` 时全扫) ──
                bool 全量 = Environment.GetEnvironmentVariable("HB_C跨平台全量") == "1";
                var 工程们 = 找全部工程文件().ToList();
                if (工程们.Count == 0)
                {
                    跳过("C后端 跨平台 Linux 上标准工程真编真跑(输出 + 整机状态一致)", "找不到 Sample 目录");
                    跳过("C后端 跨平台 Linux 上宿主能力(建目录 / 写文件 / 读文件 / 列目录 / 删文件)也一致", "找不到 Sample 目录");
                    return;
                }

                var 挑的 = 全量
                    ? 工程们
                    : 工程们.Where(p => 跨平台挑的工程.Contains(Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal)).ToList();
                var 各工程 = new Dictionary<string, string>(StringComparer.Ordinal);
                long 毫秒 = 0;
                宿主能力C.清运行痕迹();
                宿主能力C.取当前秒 = () => 冻结秒;      // 与批 2 同一个冻结秒:`当前时间()` 不冻两边必差几个字节
                // H113(2026-10-15,第451批):**毫秒也要冻** —— 这一组原来只冻了秒,组里只要有工程用到
                // `当前毫秒()`(`示例28_定时器` 那一族;`HB_C跨平台全量=1` 全扫时就会撞上),虚拟机这一侧读真钟、
                // Linux 那侧也读真钟,**两边各看各的、差多少全看运气** ⇒ 整机逐字节比早晚假红。
                // 刻度沿用全量对拍那一个常量(`冻结秒` / `冻结毫秒` 同一处口径),原生侧由
                // `编并跑(… 现在毫秒: 冻结毫秒)` 补 `--hb-now-ms`(见 `对拍一个工程在Linux`)。
                宿主能力C.取当前毫秒 = () => 冻结毫秒;
                try
                {
                    foreach (string 一个 in 挑的)
                    {
                        var 表 = System.Diagnostics.Stopwatch.StartNew();
                        string 结论;
                        try { 结论 = 对拍一个工程在Linux(根, 暂存, 一个); }
                        catch (Exception ex) { 结论 = "这一趟抛了:" + 截断(ex.Message, 120); }
                        表.Stop();
                        毫秒 += 表.ElapsedMilliseconds;
                        各工程[Path.GetFileNameWithoutExtension(一个)] = 结论;
                    }
                }
                finally { 宿主能力C.清运行痕迹(); }        // 时钟也一起还原成真时间

                int 不一致 = 各工程.Count(x => x.Value.Length > 0);
                var 坏 = 各工程.Where(x => x.Value.Length > 0).Take(4)
                    .Select(x => $"{x.Key}: {x.Value}").ToArray();
                记录("C后端 跨平台 Linux 上标准工程真编真跑(输出 + 整机状态一致)",
                    不一致 == 0 && 各工程.Count > 0,
                    $"标准工程 {(全量 ? "全量" : "挑的")} {各工程.Count} 个在 Linux 上真编真跑(cc -O0)、不一致 {不一致} 个" +
                    (坏.Length > 0 ? ";" + string.Join(" ; ", 坏) : "") +
                    $";合计 {毫秒} 毫秒(串行;想全扫 26 个:HB_C跨平台全量=1)");

                string 宿主 = 各工程.TryGetValue("示例12_输入输出与宿主能力", out string? 宿主结论) ? 宿主结论 : "";
                记录("C后端 跨平台 Linux 上宿主能力(建目录 / 写文件 / 读文件 / 列目录 / 删文件)也一致",
                    宿主.Length == 0,
                    宿主.Length == 0
                        ? "示例12_输入输出与宿主能力 在 Linux 上把建目录 / 写文件 / 追加写文件 / 读文件 / 列目录 / 删文件" +
                          "都走了一遍(走的正是 POSIX 那一半),输出与整机状态跟虚拟机一字不差"
                        : 宿主);

                // ── 七、R3-6-5 批 2:Linux 那一档的**图形呈现层(X11)**也在容器里真编真跑一遍 ──
                //     判据分两半,按这台机器 / 容器里有没有 X 服务器分档(两种都算过,理由写在那条断言的详情里):
                //       · **有 X**(给 `HB_跨平台X服务器=1` + 一个自带 Xvfb 的镜像):开窗给状态 0、
                //         画 / 提交都走通,而且**从窗口把像素读回来**对颜色(这一档就是"真开一屏"的机器判据);
                //       · **没有 X**(`gcc:14` 容器默认就是):`dlopen` 拿不到 DISPLAY ⇒ 开窗给状态 25
                //         (能力层"开不出来就如实说"),整条路照样跑完不崩。
                //     为什么值得单列:这一档是**新加的代码**(Xlib 结构 / dlopen 符号表 / 十路接线),
                //     而开发机上的 MSVC 永远编不到它 —— 不在这儿编一遍,它就一次都没被编译器看过。
                bool 要X = Environment.GetEnvironmentVariable("HB_跨平台X服务器") == "1";
                // 「现装」那一档:镜像里没有 Xvfb 就在**这一次容器运行里** apt 装一次(容器一销毁什么都没留下,
                // 不碰镜像标签、不 commit)。默认不开 —— 别让日常跑自检都去联网。
                bool 现装X = 要X && Environment.GetEnvironmentVariable("HB_跨平台X现装") == "1";
                string 说明X没起 = "";
                if (要X && !现装X && !跨平台工具链C.有X服务器()) { 要X = false; 说明X没起 = 跨平台工具链C.X服务器说明(); }
                try
                {
                    string 探针c = Path.Combine(暂存, "窗口探针.c");
                    File.WriteAllText(探针c, Linux窗口探针源(), new UTF8Encoding(false));
                    if (Environment.GetEnvironmentVariable("HB_留下探针源码") == "1")
                        try { File.WriteAllText(Path.Combine(根, "工具", "临时", "h12", "Linux窗口探针.c"), Linux窗口探针源(), new UTF8Encoding(false)); } catch { }
                    var 窗口跑 = 要X
                        ? 跨平台工具链C.编并跑带X(根, 探针c, Path.Combine(暂存, "窗口探针程序"), "-O0", 现装: 现装X)
                        : 跨平台工具链C.编并跑(根, 探针c, Path.Combine(暂存, "窗口探针程序"), "-O0");
                    string 文字 = 窗口跑.输出文字;
                    bool 编过 = 窗口跑.编过;
                    bool 有X = 要X && 文字.Contains("open=0", StringComparison.Ordinal);
                    // 有 X 那一档**必须真读回像素**才作数:第一版只钉能力层状态码(`readback-skip`),
                    // 结果"窗口全黑、状态码全 0"整整活过一版 —— 状态码是接线哨兵,不是"画上去了"的证据。
                    // 现在从**已提交那一帧**(离屏 Pixmap)把像素读回来对三处颜色 + 数白点(文字)。
                    int 白点 = 0;
                    int 位白 = 文字.IndexOf("xwhite=", StringComparison.Ordinal);
                    if (位白 >= 0)
                    {
                        int 末 = 位白 + "xwhite=".Length;
                        int 尾 = 末;
                        while (尾 < 文字.Length && 文字[尾] >= '0' && 文字[尾] <= '9') 尾++;
                        int.TryParse(文字[末..尾], out 白点);
                    }
                    const string 像素对 = "xrgb=AABBCC/102030/00FF00";   // 矩形里 / 底色 / 线上
                    // 没有 X(容器默认):开窗给 25(能力层"开不出来就如实说"),之后的画 / 刷新给 22
                    // ("没有窗口"那条老口径)—— 关键是**不崩、也不假装开了**。
                    bool 开窗那档对 = 有X
                        ? 文字.Contains("open=0", StringComparison.Ordinal)
                          && 文字.Contains("present=0", StringComparison.Ordinal)
                          && 文字.Contains("frames=1", StringComparison.Ordinal)
                          && 文字.Contains(像素对, StringComparison.Ordinal)
                          && 白点 > 0
                          && 窗口跑.退出码 == 0
                        : 文字.Contains("open=25", StringComparison.Ordinal)
                          && !文字.Contains("open=0", StringComparison.Ordinal);

                    string 这一档 = 有X
                        ? "**真 X 服务器(容器里的 Xvfb)**:开窗给 0、清屏 / 矩形 / 线 / 提交都走通," +
                          "而且每一路都给了成功状态码,并且**从已提交那一帧把像素读回来对上了颜色**" +
                          $"({像素对} / 文字白点 {白点} 个)"
                        : (要X
                            ? "要了真 X 服务器、但那一档没跑起来(容器里没有 Xvfb 那一族工具?)"
                            : "没给环境变量 `HB_跨平台X服务器=1`(或那台机器没有自带 X 的镜像)⇒ 走【没有 X】那一支:" +
                              "开窗给 25(能力层如实说开不出来,整条路不崩)");
                    记录("C后端 跨平台 Linux 上图形呈现层(X11)真编真跑:没有 X 时如实给状态 25(有 X 时从已提交那一帧读回像素)",
                        编过 && 开窗那档对,
                        $"cc 编过 = {编过};退出码 {窗口跑.退出码};{这一档}" +
                        (说明X没起.Length > 0 ? ";" + 说明X没起 : "") +
                        $";探针输出「{截断(文字.Replace("\n", " "), 140)}」" +
                        (窗口跑.日志.Length > 0 ? ";cc 的话:" + 截断(窗口跑.日志, 140) : "") +
                        " —— 有 X 那一档是真开窗那一份(硬规矩 6 的机器那一份);" +
                        "第一版这里只钉能力层状态码(探针打 `readback-skip`),而『窗口全黑、状态码全 0』" +
                        "整整活过一版 ⇒ 现在改钉**从已提交那一帧读回来的像素**(三处颜色 + 文字白点数)");
                }
                catch (Exception ex) { 记录异常("C后端 跨平台 Linux 上图形呈现层(X11)", ex); }
            }
            catch (Exception ex) { 记录异常("C后端 跨平台验证", ex); }
            finally
            {
                try { Directory.SetCurrentDirectory(原目录); } catch { }
                try { Directory.Delete(暂存, true); } catch { }
            }
        }

        /// <summary>
        /// 一个标准工程在 Linux 上真编真跑:同一份 `.c` 交给容器里的 `cc -O0`,跑、落状态,
        /// 与 C# 虚拟机比**程序输出 + 整机状态**。返回**空串 = 一致**;非空 = 差异说明。
        /// (与批 2 在 Windows 上那个 `对拍一个工程` 同一套口径,只是把"本机 MSVC"换成"容器里的 cc";
        /// 钟也是那一套 —— **秒与毫秒都冻**,见 H113。)
        /// </summary>
        private static string 对拍一个工程在Linux(string 根, string 暂存, string 工程路径)
        {
            string 名 = Path.GetFileNameWithoutExtension(工程路径);
            var 工程 = 工程文件C.加载(工程路径);
            if (工程.文件.Count == 0) return "空工程";
            int 内存; try { 内存 = 工程.内存字节数; } catch { 内存 = 虚拟机C.默认内存大小; }
            int 堆; try { 堆 = 工程.堆字节数; } catch { 堆 = 中间层C.默认堆大小; }
            int 池; try { 池 = 工程.池字节数; } catch { 池 = 中间层C.默认池大小; }
            int 软栈; try { 软栈 = 工程.软栈字节数; } catch { 软栈 = -1; }

            var 编译结果 = new 汇编器C().汇编多文件带调试(工程.读全部编译输入(), true, 堆, null, 池, 内存, 软栈);
            string c源 = Path.Combine(暂存, 名 + ".c");
            File.WriteAllText(c源, C后端C.转译(编译结果, 内存).源码, new UTF8Encoding(false));

            // H109(2026-10-14):**图形工程要挂无头呈现 + 喂同一份事件脚本** —— 与 Windows 那侧
            // (`自检\综合自检C.C后端全量对拍.cs` 里 `用图形` 那一段)同一把尺子,判据 = 源码里写没写 `打开窗口`。
            // 不挂的下场实测过:容器里没有用户去关窗,主循环一路刷帧(`示例01` 打了 5,263,278 字节),
            // 六个工程里有四个各挂到 600 秒超时 ⇒ 单组烧 1842 秒、把预算顶穿。
            bool 用图形 = 用图形标准工程(工程路径);
            string? 图形脚本 = 用图形 ? 读工程事件脚本(工程路径, 暂存) : null;
            string 图形落点 = Path.Combine(暂存, 名 + ".图形");
            if (用图形)
            {
                try
                {
                    图形宿主C.清空();
                    图形宿主C.挂上(new 无头呈现C(TextWriter.Null));   // 命令不用留:这一组比的是程序输出与整机状态
                    图形宿主C.挂事件脚本(事件脚本C.解析(File.ReadAllText(图形脚本!, Encoding.UTF8)));
                }
                catch (脚本错误C ex)
                {
                    return "图形宿主挂不起来(工程里的 `事件脚本.txt` 写错了?):" + 截断(ex.Message, 140);
                }
                catch (Exception ex) { return "图形宿主挂不起来:" + 截断(ex.Message, 140); }
            }

            try
            {
                // 关优化编(与批 2 一个理由:判据是语义,与优化档无关;`-O2` 编那两份 2.5MB 的浮点示例要几分钟)。
                // `限时秒: 180` 与 Windows 那侧 `WaitForExit(180_000)` 对齐 —— 挂死时是"3 分钟记一条 FAIL"。
                string 状态 = Path.Combine(暂存, 名 + ".状态");
                var 跑 = 跨平台工具链C.编并跑(根, c源, Path.Combine(暂存, 名 + ".程序"), "-O0", 状态, 冻结秒,
                    标准输入: null, 图形路径: 用图形 ? 图形落点 : null, 事件脚本路径: 图形脚本, 限时秒: 180,
                    现在毫秒: 冻结毫秒);   // H113:与虚拟机那一侧的 `取当前毫秒` 同一个刻度
                if (!跑.编过) return "Linux 上没编过:" + 截断(跑.日志, 200);

                // C# 这一侧**在同一个呈现口下跑**(与原生那侧同挂无头呈现、同喂脚本),两边才谈得上逐字节比
                var (机出, 机误, 机状态) = 虚拟机跑一侧(编译结果, 内存, null);
                if (机误.Length > 0) return "虚拟机那侧就报了错:" + 截断(机误, 120);
                机状态.退出码 = 0;

                if (!跑.输出.AsSpan().SequenceEqual(机出))
                    return $"输出不一致:Linux {跑.输出.Length} 字节「{截断(跑.输出文字, 60)}」/ " +
                           $"虚拟机 {机出.Length} 字节「{截断(Encoding.UTF8.GetString(机出), 60)}」";

                var 原生 = C状态C.读原生状态(状态);
                if (原生 == null) return "Linux 那侧没落下状态转储(程序没跑起来?)";
                var 比 = C状态C.比(机状态, 原生, 跑.输出, 跑.退出码);
                return 比.一致 ? "" : 比.说明;
            }
            finally
            {
                // 呈现口是**全局的**:这一趟用完就还,别渗到下一个工程或别的组
                if (用图形) { try { 图形宿主C.清空(); } catch { } }
            }
        }

        /// <summary>
        /// R3-6-5 批 2:Linux 那一档呈现层(X11)的探针 —— 拼一份**自足**的 C(运行时原文 + 一段 main)。
        ///
        /// 运行时原文只改**一处**:把窗口层那个 `#if HB_有窗口` 换成 `#if 1`(并把那把运行期闸置 1)——
        /// 这一档平时只在**转译产物与解释器核**里开(`#define HB_有窗口 1`),探针要单独编一次就得自己打开它。
        /// (锚点 = `static int hb_win_活 = 0;` 那一行,它在运行时里**只出现一次**;按 `IndexOf` 切,不靠 `Replace` 撞运气。)
        /// `带真X` 决定 main 里要不要多走"从窗口读回像素"那一段(`XGetImage` 那一族,老 Xlib 没有就跳过)。
        /// 判据全在标准输出那几行上(`mount=` / `open=` / `clear=` / `present=` / `ink=`),自检不依赖像素以外的任何东西。
        /// </summary>
        private static string Linux窗口探针源()
        {
            string 原文 = C后端C.取运行时源码().Replace("\r\n", "\n").Replace('\r', '\n');

            // ① 两份原文里都要求 `_POSIX_C_SOURCE` 在**第一个 include 之前** ⇒ 把它提到整份的最前面。
            //    运行时的开头有 `#ifndef _POSIX_C_SOURCE / #define … / #endif` 这一小段,抄过来当开头。
            const string 位标记 = "#ifndef _POSIX_C_SOURCE";
            string 前置 = "";
            int 位首 = 原文.IndexOf(位标记, StringComparison.Ordinal);
            if (位首 >= 0)
            {
                int 行尾 = 原文.IndexOf("\n#endif", 位首, StringComparison.Ordinal);
                if (行尾 > 0)
                {
                    前置 = 原文[位首..(行尾 + "\n#endif".Length)] + "\n";
                    原文 = 原文.Remove(位首, 行尾 + "\n#endif".Length - 位首);
                }
            }

            // ② 把"窗口层"那一档打开(`hb_win_活` 置 1 + 那个 `#if HB_有窗口` 改成 `#if 1`)——
            //    这一档平时只有**解释器核与转译产物**会开(`#define HB_有窗口 1`),探针要单独编一次。
            const string 锚 = "static int hb_win_活 = 0;";
            int 位 = 原文.IndexOf(锚, StringComparison.Ordinal);
            if (位 >= 0)
                原文 = 原文[..位] + "static int hb_win_活 = 1;" + 原文[(位 + 锚.Length)..].Replace("\n#if HB_有窗口", "\n#if 1");

            // ③ 探针那一份**排在运行时之后**(类型与那一族函数编译器才看得见),自己把图形档开上。
            //    给了 `带真X` 就多走一段:开窗 / 画 / 提交之后,用 `XGetImage` 把**窗口像素读回来**
            //    (自检按颜色判;人工留证也拿这一行 XIMG 去画 PNG)。
            string 探针 =
                "/* R3-6-5 批 2 探针:Linux / X11 那一档呈现层(同一份运行时原文 + 这一段 main) */\n" +
                "static int hb_gfx_装配(const hb_开关 *开关);   /* 它在运行时后段,先声明一句 */\n" +
                "int main(void)\n" +
                "{\n" +
                "    const hb_u32 区 = 4096u;      /* 参数区得在运行时内存里(能力层查的是那一块) */\n" +
                "    hb_开关 开关;\n" +
                "    hb_mem_size = 1u << 20;\n" +
                "    hb_mem = (hb_u8 *)calloc(hb_mem_size, 1u);\n" +
                "    if (hb_mem == NULL) { printf(\"nomem\\n\"); return 0; }\n" +
                "    memset(&开关, 0, sizeof(开关));\n" +
                "    开关.要窗口 = 1;                 /* 真开窗那一档 */\n" +
                "    printf(\"mount=%d\\n\", hb_gfx_装配(&开关));\n" +
                "    hb_host_wr(区 + 0u, 0u); hb_host_wr(区 + 4u, 0u);\n" +
                "    hb_host_wr(区 + 8u, 96u); hb_host_wr(区 + 12u, 48u);\n" +
                "    printf(\"open=%u\\n\", (unsigned)hb_gfx_调用(区, HB_F_OPENWIN));\n" +
                "    hb_host_wr(区, 0xFF102030u);\n" +
                "    printf(\"clear=%u\\n\", (unsigned)hb_gfx_调用(区, HB_F_CLEAR));\n" +
                "    hb_host_wr(区 + 0u, 4u); hb_host_wr(区 + 4u, 4u);\n" +
                "    hb_host_wr(区 + 8u, 40u); hb_host_wr(区 + 12u, 20u); hb_host_wr(区 + 16u, 0xFFAABBCCu);\n" +
                "    printf(\"rect=%u\\n\", (unsigned)hb_gfx_调用(区, HB_F_DRAWRECT));\n" +
                "    hb_host_wr(区 + 0u, 2u); hb_host_wr(区 + 4u, 44u);\n" +
                "    hb_host_wr(区 + 8u, 92u); hb_host_wr(区 + 12u, 44u); hb_host_wr(区 + 16u, 0xFF00FF00u);\n" +
                "    printf(\"line=%u\\n\", (unsigned)hb_gfx_调用(区, HB_F_DRAWLINE));\n" +
                "    {\n" +
                "        const char *文本 = \"HB 中文\";\n" +
                "        hb_u32 长 = (hb_u32)strlen(文本);\n" +
                "        memcpy(hb_mem + 2048u, 文本, 长 + 1u);\n" +
                "        hb_host_wr(区 + 0u, 4u); hb_host_wr(区 + 4u, 4u);\n" +
                "        hb_host_wr(区 + 8u, 2048u); hb_host_wr(区 + 12u, 长); hb_host_wr(区 + 16u, 0xFFFFFFFFu);\n" +
                "        printf(\"text=%u\\n\", (unsigned)hb_gfx_调用(区, HB_F_DRAWTEXT));\n" +
                "    }\n" +
                "    printf(\"present=%u\\n\", (unsigned)hb_gfx_调用(区, HB_F_PRESENT));\n" +
                "    hb_win_泵();\n" +
                "    printf(\"frames=%d\\n\", hb_gfx_已呈现帧数);\n" +
                "    /* 从**已提交那一帧**(离屏 Pixmap)把像素读回来:这一档的真判据。\n" +
                "       第一版读的是窗口,实测 X 给 BadValue,于是退成只钉状态码 —— 而窗口全黑正是那时候漏过去的。\n" +
                "       Pixmap 的内容是确定的,读它不会 BadValue。XGetImage 那三件符号标了【可缺】,没有就如实说跳过。 */\n" +
                "    if (hb_win_显 == NULL || hb_win_已提交 == 0 || hb_x.XGetImage == NULL || hb_x.XGetPixel == NULL || hb_x.XDestroyImage == NULL)\n" +
                "        printf(\"xrgb=skip\\n\");\n" +
                "    else\n" +
                "    {\n" +
                "        void *图 = hb_x.XGetImage(hb_win_显, hb_win_已提交, 0, 0,\n" +
                "                                 (unsigned)hb_win_宽, (unsigned)hb_win_高, ~0UL, 2 /* ZPixmap */);\n" +
                "        if (图 == NULL) printf(\"xrgb=none\\n\");\n" +
                "        else\n" +
                "        {\n" +
                "            int 横, 竖, 白 = 0;\n" +
                "            printf(\"xrgb=%06lX/%06lX/%06lX\\n\",\n" +
                "                hb_x.XGetPixel(图, 38, 20) & 0xFFFFFFUL,   /* 矩形里(0xAABBCC) */\n" +
                "                hb_x.XGetPixel(图, 50, 30) & 0xFFFFFFUL,   /* 底色(0x102030) */\n" +
                "                hb_x.XGetPixel(图, 50, 44) & 0xFFFFFFUL);  /* 线上(0x00FF00) */\n" +
                "            for (竖 = 0; 竖 < (int)hb_win_高; 竖++)\n" +
                "                for (横 = 0; 横 < (int)hb_win_宽; 横++)\n" +
                "                    if ((hb_x.XGetPixel(图, 横, 竖) & 0xFFFFFFUL) == 0xFFFFFFUL) 白++;\n" +
                "            printf(\"xwhite=%d\\n\", 白);   /* 文字那几笔;0 就是字没画上去 */\n" +
                "            hb_x.XDestroyImage(图);\n" +
                "        }\n" +
                "    }\n" +
                "    hb_win_拆窗口();   /* 开着真窗口直接 return,X 连接挂着不算退出 ⇒ 对拍会等到超时(实测踩过)——跑完拆掉 */\n" +

                "    free(hb_mem);\n" +
                "    return 0;\n" +
                "}\n";

            // ④ 三个 `#define` 必须排在运行时那一份**之前**(它自己的 `#ifndef` 缺省是 0 —— 这条踩过:
            //    写在后面 ⇒ 图形层整段被跳过 ⇒ `hb_gfx_装配` 根本没编出来)。
            return 前置 +
                "#define HB_有图形 1\n" +
                "#define HB_有窗口 1\n" +
                "/* 这一条只看状态码:参数区错用不着 dump 文件,绘制命令不走程序输出那一股 */\n" +
                原文 + "\n" + 探针;
        }

        /// <summary>把拼好的 Linux 窗口探针源码交出去(给"人工留证"那个临时工具用;自检自己走 `Linux窗口探针源`)</summary>
        public static string Linux窗口探针源码()
        {
            string 落 = Path.Combine("工具", "临时", "h12", "Linux窗口探针.c");
            File.WriteAllText(落, Linux窗口探针源(), new UTF8Encoding(false));
            return Path.GetFullPath(落);
        }

        /// <summary>临时:把拼好的探针落到仓库 `工具\临时\h12\` 下,好在真 X 上手工跑一遍(自检自己不用它)</summary>
        public static string 写Linux窗口探针到(string 目录, bool 带真X)
        {
            Directory.CreateDirectory(目录);
            string 路径 = Path.Combine(目录, "Linux窗口探针.c");
            File.WriteAllText(路径, Linux窗口探针源(), new UTF8Encoding(false));
            return 路径;
        }

        /// <summary>虚拟机那一侧:跑一份编译结果,收程序输出字节 + 整机状态(与批 2 的 Windows 侧同一套口径)</summary>
        private static (byte[] 出, string 报错, C状态快照C 状态) 虚拟机跑一侧(汇编调试结果C 编译结果, int 内存, string? 标准输入)
        {
            var 机 = new 虚拟机C();
            机.设内存大小(内存);
            机.初值表 = 编译结果.初值表;
            机.宿主调用 = 宿主能力C.处理;
            if (标准输入 != null)
            {
                var 队列 = new Queue<uint?>(Encoding.UTF8.GetBytes(标准输入).Select(b => (uint?)b));
                机.输入源 = () => 队列.Count > 0 ? 队列.Dequeue() : null;
            }
            var 出 = new MemoryStream();
            机.输出目标 = b => 出.WriteByte(b);
            string 报错 = "";
            机.装载(编译结果.机器码);
            try { while (机.单步()) { } }
            catch (Exception ex) { 报错 = ex.Message; }
            return (出.ToArray(), 报错, C状态C.导出虚拟机状态(机, 出, 编译结果.代码字节数, 编译结果.数据字节数));
        }
    }
}
