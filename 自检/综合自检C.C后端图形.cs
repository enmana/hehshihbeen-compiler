using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R3-6-3:**宿主能力 C 版 + 无头呈现**(图形能力 60~69)的那一组对拍。
    ///
    /// 与 `C后端解释器核` 那一组的分工:那一组钉的是"55 条指令与整机状态两边逐字节一致";
    /// 这一组钉的是**图形那一族能力**:命令队列 / 打开窗口 / 取事件 / 等待 / 取文字宽度,
    /// 判据 = **与 C 版无头呈现的输出逐行一致**(C# 版打的那几行就是"无头宿主"的规格)。
    ///
    /// 两边怎么摆(**与命令行 `hbcc --run --无头` 同一套**):
    ///   · C# 那一侧:虚拟机 + `图形宿主C.挂上(new 无头呈现C(缓冲))`,程序输出收进内存流;
    ///   · C 那一侧:解释器核 + `--hb-图形 <文件>`(绘制命令写进文件)+ `--hb-事件脚本 <文件>`;
    ///   · 于是"程序输出"与"图形 dump"**各走各的**,两样都逐字节 / 逐行比:
    ///     程序输出与 `C状态C.导出虚拟机状态` 收的那一份比(与 `C后端解释器核` 同一套),
    ///     图形 dump 与 C# 版无头呈现打的那些行**逐行**比。
    ///
    /// 为什么值得单独一组(而不是并进解释器核那一组):这一批新增的是**能力层与呈现层**,
    /// 它的错法与指令语义的错法不一样 —— 指令写错整块内存就对不上,而这里错在
    /// "少打一行 `刷新 命令=N`""事件投早了一帧""帧数护栏没拦",只有**真跑一段图形程序**才看得见。
    /// 所以本组真跑两段程序(一段吃掉 4 帧事件后收手、一段故意不理关窗请求撞护栏)+ 一段手写机器码。
    ///
    /// **不做**(本批的边界,免得有人顺手开工):真窗口 / 图形呈现层(Skia / SDL3)= **R3-6-5**;
    /// 转译产物那一侧**不带图形**(`HB_有图形` 缺省 = 0,还是老口径"独立 exe 没有窗口"给状态 1)。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary>本组要真编真跑的断言名(没有 C 编译器时逐条如实记跳过;名字前缀 `C后端` 在允许跳过的三族里)</summary>
        private static readonly string[] 图形断言 =
        [
            "C后端 图形 与 C# 版无头呈现逐行一致(示例15 事件循环)",
            "C后端 图形 与 C# 版无头呈现逐行一致(示例16 控件库)",
            "C后端 图形 打开窗口 / 画文字 / 刷新 / 关闭窗口与 C# 版同格式(手写小程序)",
            "C后端 图形 取事件按帧投(事件脚本 4 帧 4 条 + 关窗请求,两边条数一致)",
            "C后端 图形 帧数宽限那道护栏两边都拦得住(不理关窗请求 → 同一类错 + 退出码非 0)",
            "C后端 位图 与 C# 版逐字节一致(建 / 画点 / 读点 / 线 / 矩形 / 尺寸 / 贴 / 释放)",
        ];

        /// <summary>不吃 C 编译器的那几条(接线哨兵:只读仓库源码文本)</summary>
        private const string 能力哨兵名 = "C后端 图形 能力层在 C 侧(功能号 60~69 + 参数区字节数表 + 无头呈现那几行)";
        private const string 开关哨兵名 = "C后端 图形 解释器核认 `--hb-图形` / `--hb-事件脚本`(挂无头 / 按帧喂输入)";
        private const string 键名哨兵名 = "C后端 图形 键名表认得与 `图形键C` 同一套";
        private const string 位图哨兵名 = "C后端 位图 能力层在 C 侧(功能号 70~78 + 参数区字节数表 + 九路分派 + 两套贴图)";
        private const string 位图对拍断言名 = "C后端 位图 与 C# 版逐字节一致(建 / 画点 / 读点 / 线 / 矩形 / 尺寸 / 贴 / 释放)";

        /// <summary>
        /// 一段图形程序(与 `图形输入与时钟` 那一组的夹具不同:**它不吃 `包含` / 库,自检里现编就能跑**):
        /// 打开窗口 → 把**第 0 帧**投进来的事件一次收干 → 输出收到条数 → 关窗。
        ///
        /// 为什么只吃第 0 帧:**帧边界只有 `刷新()` 一处**(`等待` 不推进帧号,与 `图形宿主C` 同口径),
        /// 所以这一段不刷帧、事件条数就是脚本里 `帧 0` 那几条 —— 判据确定,不靠"跑几轮"的算术。
        /// 要吃"跨帧按帧投"那件事,标准工程 示例15 / 16 那两条**逐行比**才是硬证据。
        /// </summary>
        private const string C侧事件循环源 =
            "整数 事件类 = 0;\n" +
            "整数 键码 = 0;\n" +
            "整数 横 = 0;\n" +
            "整数 纵 = 0;\n" +
            "整数 收手 = 0;\n" +
            "整数 收到 = 0;\n" +
            "如果 (打开窗口(\"HB 图形演示\", 320, 200))\n" +
            "{\n" +
            "    当 (收手 == 0)\n" +
            "    {\n" +
            "        如果 (取事件(事件类, 键码, 横, 纵))\n" +
            "        {\n" +
            "            如果 (事件类 == 1) { 收手 = 1; }\n" +
            "            否则 { 收到 = 收到 + 1; }\n" +
            "        }\n" +
            "        否则 { 收手 = 1; }\n" +
            "    }\n" +
            "    测试输出(收到);\n" +
            "    关闭窗口();\n" +
            "}\n" +
            "停止\n";

        /// <summary>
        /// 帧数护栏那一段:**故意不理**关窗请求 —— 脚本说跑满 4 帧,程序到第 9 帧还在刷新,
        /// 两边都该当场报"还在刷新"并给非 0 退出码(与 `图形宿主C.处理刷新` 那道护栏同一句口径)。
        /// </summary>
        private const string 帧数护栏源 =
            "如果 (打开窗口(\"护栏\", 64, 64))\n" +
            "{\n" +
            "    当 (真)\n" +
            "    {\n" +
            "        刷新();\n" +
            "    }\n" +
            "}\n" +
            "停止\n";

        private static void C后端图形自检()
        {
            // H69 / R3-6-5 批 2:`图形宿主C` 那几格状态是**进程级静态**的,而本组要挂自己的无头呈现口;
            // 另外两组(`C后端解释器核` / `C后端全量对拍`)也会挂 —— 三组共用**同一把门**(见 `图形对拍门`)。
            Monitor.Enter(图形对拍门);
            try
            {
            // ── ① 不吃 C 编译器那几条:能力层、那两个开关、键名表、位图接线 ──
            try { 图形能力层哨兵(); } catch (Exception ex) { 记录异常(能力哨兵名, ex); }
            try { 图形开关哨兵(); } catch (Exception ex) { 记录异常(开关哨兵名, ex); }
            try { 图形键名哨兵(); } catch (Exception ex) { 记录异常(键名哨兵名, ex); }
            try { 位图能力层哨兵(); } catch (Exception ex) { 记录异常(位图哨兵名, ex); }

            // ── ② 真编真跑 ──
            // 解释器核这一份**两组共用**(编一次;见 `编好解释器核` 的说明)。
            // 跳过名单只传自己这五条:上一组那几条断言名不该由这一组代记(同名会撞 `自检 断言名唯一`)。
            string? 解释器exe = 编好解释器核(图形断言);
            if (解释器exe == null) return;

            // ③ 手写小程序:同格式 + 取事件按帧投(这一条把"事件条数"先量出来)
            var 手写 = (机: -1, c: -1);
            try { 手写 = 图形与虚拟机逐行一致(解释器exe, "手写小程序", C侧事件循环源, 假事件脚本(), "手写小程序"); }
            catch (Exception ex) { 记录异常("C后端 图形 打开窗口 / 画文字 / 刷新 / 关闭窗口与 C# 版同格式(手写小程序)", ex); }

            // ④ 帧数护栏(两边都要拦得住)
            try { 图形帧数护栏(解释器exe); }
            catch (Exception ex) { 记录异常("C后端 图形 帧数宽限那道护栏两边都拦得住(不理关窗请求 → 同一类错 + 退出码非 0)", ex); }

            // ④② R4-12:位图那一族(70~77)—— 同一段位图程序两条腿真跑,程序输出逐字节 + 图形 dump 逐行
            try { 位图两腿对拍(解释器exe); }
            catch (Exception ex) { 记录异常(位图对拍断言名, ex); }

            // ⑤ 两个标准工程:自带的图形程序 + 自带的事件脚本,两边逐行对
            string? 例子 = 找示例目录();
            if (例子 == null)
            {
                跳过("C后端 图形 与 C# 版无头呈现逐行一致(示例15 事件循环)", "找不到 Sample 目录(不在工作副本里跑)");
                跳过("C后端 图形 与 C# 版无头呈现逐行一致(示例16 控件库)", "找不到 Sample 目录");
                跳过("C后端 图形 取事件按帧投(示例脚本 4 条 + 关窗请求,两边数出来一样)", "找不到 Sample 目录");
                return;
            }
            var 各例 = new List<(string 名, int 机, int c)>();
            foreach (var (工程名, 断言名) in new[]
            {
                ("示例15_事件循环", "C后端 图形 与 C# 版无头呈现逐行一致(示例15 事件循环)"),
                ("示例16_控件库", "C后端 图形 与 C# 版无头呈现逐行一致(示例16 控件库)"),
            })
            {
                try
                {
                    var (行数, 机数, c数) = 图形标准工程(解释器exe, Path.Combine(例子, "中间层", 工程名), 断言名);
                    各例.Add((工程名, 机数, c数));
                }
                catch (Exception ex) { 记录异常(断言名, ex); }
            }

            // ⑥ 事件那一条:手写夹具**只吃第 0 帧的 4 条**(确定值),两个标准工程**两边数出来一样**。
            //
            //    这里**不把"标准工程该报几"写死**:两个标准工程末尾那个数不是同一回事
            //    (示例15 末尾是收到的事件条数、示例16 末尾是跑过的帧数)——
            //    写死一个数就得跟着示例正文走,示例一改这里就假红。判据落两条硬事实:
            //    ① 手写那一段等于 **4**(帧 0 投了 4 条,眼睛看得见的确定值);
            //    ② **两个标准工程两边数出来一样**(同一份脚本、同一条无头路);
            //    真正"事件按帧投"的硬证据是上面那两条**逐字节比**(末尾每一个数都在里面)。
            {
                const string 断言名 = "C后端 图形 取事件按帧投(示例脚本 4 条 + 关窗请求,两边数出来一样)";
                bool 对 = 手写.机 == 4 && 手写.c == 4
                    && 各例.Count == 2 && 各例.All(x => x.机 > 0 && x.机 == x.c);
                记录(断言名, 对,
                    $"手写小程序(只吃第 0 帧):C# {手写.机} 条 / C {手写.c} 条(该是 4);" +
                    (各例.Count > 0
                        ? "标准工程(末尾那个数):" + string.Join(" / ", 各例.Select(x => $"{x.名} C# {x.机}, C {x.c}"))
                        : "标准工程:一条都没量到") +
                    " —— 判据 = 两边一样 + 手写那一段是 4");
            }
            }
            finally { Monitor.Exit(图形对拍门); }
        }

        /// <summary>
        /// 能力层那一条**接线哨兵**:10 个功能号 / 参数区字节数表 / 无头呈现那几行 / 十路分派,
        /// 逐条在 `C后端\运行时.c` 里点得到名(它钉的是"这批东西在 C 侧落了地",
        /// 语义对不对由下面真跑那几条钉)。
        /// </summary>
        private static void 图形能力层哨兵()
        {
            string? 根 = 找仓库根目录();
            if (根 == null) { 跳过(能力哨兵名, "找不到仓库根(读不到 `C后端\\运行时.c`)"); return; }
            string 路径 = Path.Combine(根, "C后端", "运行时.c");
            if (!File.Exists(路径)) { 跳过(能力哨兵名, $"找不到 {路径}"); return; }
            string 源 = File.ReadAllText(路径, Encoding.UTF8);
            string[] 功能号 = ["HB_F_OPENWIN", "HB_F_CLOSEWIN", "HB_F_CLEAR", "HB_F_DRAWTEXT", "HB_F_DRAWRECT",
                              "HB_F_DRAWLINE", "HB_F_PRESENT", "HB_F_POLLEVENT", "HB_F_SLEEP", "HB_F_TEXTWIDTH"];
            var 缺号 = 功能号.Where(名 => !源.Contains($"#define {名}", StringComparison.Ordinal)).ToArray();
            // 参数区字节数表:八个要参数的号 + 两个不要参数的号(与 `图形宿主C.参数区字节数` 同表)
            string[] 要表 = ["HB_F_OPENWIN:  return 16u", "HB_F_CLEAR:    return 4u", "HB_F_DRAWTEXT: return 20u",
                            "HB_F_DRAWRECT: return 20u", "HB_F_DRAWLINE: return 20u", "HB_F_POLLEVENT:return 16u",
                            "HB_F_SLEEP:    return 4u", "HB_F_TEXTWIDTH:return 12u"];
            var 缺表 = 要表.Where(句 => !源.Contains(句, StringComparison.Ordinal)).ToArray();
            // 无头呈现那几行(格式的事实源:`图形宿主C` 的 `一句话()` / `无头呈现C`)
            string[] 要行 = ["打开窗口 标题=", "关闭窗口\\n", "清屏 颜色=0x%08X", "画文字 x=%d y=%d 文本=",
                            "画矩形 x=%d y=%d 宽=%d 高=%d 颜色=0x%08X", "画线 x1=%d y1=%d x2=%d y2=%d 颜色=0x%08X",
                            "刷新 命令=%d"];
            var 缺行 = 要行.Where(句 => !源.Contains(句, StringComparison.Ordinal)).ToArray();
            bool 开关在 = 源.Contains("#ifndef HB_有图形", StringComparison.Ordinal)
                && 源.Contains("static int hb_gfx_是图形功能号(hb_u32 功能号)", StringComparison.Ordinal)
                && 源.Contains("static hb_u32 hb_gfx_调用(hb_u32 参数区, hb_u32 功能号)", StringComparison.Ordinal);
            bool 护栏在 = 源.Contains("还在刷新", StringComparison.Ordinal)
                && 源.Contains("hb_gfx_脚本.帧数 + 4", StringComparison.Ordinal);
            记录(能力哨兵名, 缺号.Length == 0 && 缺表.Length == 0 && 缺行.Length == 0 && 开关在 && 护栏在,
                $"功能号缺 {缺号.Length} 个{(缺号.Length > 0 ? "(" + string.Join(",", 缺号) + ")" : "")};" +
                $"参数区字节数表缺 {缺表.Length} 格{(缺表.Length > 0 ? "(" + string.Join(" | ", 缺表) + ")" : "")};" +
                $"无头呈现那几行缺 {缺行.Length} 行{(缺行.Length > 0 ? "(" + string.Join(" | ", 缺行) + ")" : "")};" +
                $"图形总开关与十路分派 = {开关在};帧数宽限护栏 = {护栏在}(4 = `事件脚本C.帧数宽限`)");
        }

        /// <summary>解释器核那两个开关(接线哨兵):`--hb-图形` / `--hb-事件脚本` 真的接在 main 里</summary>
        private static void 图形开关哨兵()
        {
            string? 根 = 找仓库根目录();
            if (根 == null) { 跳过(开关哨兵名, "找不到仓库根(读不到 `C后端\\解释器核.c`)"); return; }
            string 路径 = Path.Combine(根, "C后端", "解释器核.c");
            if (!File.Exists(路径)) { 跳过(开关哨兵名, $"找不到 {路径}"); return; }
            string 源 = File.ReadAllText(路径, Encoding.UTF8);
            // R3-6-5 批 2:开关解析收进了 `运行时.c` 的 `hb_取开关`(**两条 main 共用一份**),
            // 所以这一条改钉**接线**而不是那几个字面量:解释器核得真调共用那一份、得在装载前挂好呈现口。
            bool 图形 = 源.Contains("hb_取开关(argc, argv, HB_有图形, &开关)", StringComparison.Ordinal)
                && 源.Contains("hb_gfx_装配(&开关)", StringComparison.Ordinal);
            bool 脚本 = 源.Contains("hb_取参数(argc, argv, &个数)", StringComparison.Ordinal)
                && 源.Contains("HB_有窗口", StringComparison.Ordinal);
            bool 带图形 = 源.Contains("#define HB_有图形 1", StringComparison.Ordinal);
            记录(开关哨兵名, 图形 && 脚本 && 带图形,
                $"共用开关解析 = {图形};参数过宽字符 = {脚本};这一份带图形(`#define HB_有图形 1`)= {带图形}" +
                " —— 三个开关(`--hb-图形` / `--hb-事件脚本` / `--hb-窗口`)的名字落在 `运行时.c` 的 `hb_取开关` 里," +
                "那一份由两条 main 共用(转译产物那一侧见「C后端全量对拍」新那条)");
        }

        /// <summary>
        /// 手写那份**图形事件脚本**(与 `事件脚本C` 的格式一字不差:帧数 + 4 条事件,全排在第 0 帧)。
        /// 为什么不用示例15 那份:那一条要有 `包含` 与 HB 库才编得出,自检里现编不方便;
        /// 这里要的是**同一份脚本两条路各喂一遍**,内容越简单越好(示例15 / 16 那两条走标准工程)。
        /// </summary>
        private static string 假事件脚本() =>
            "帧数 4\n" +
            "# 注释行不算(`//` 也行)\n" +
            "帧 0 按键 回车\n" +
            "帧 0 移动 120 55\n" +
            "帧 0 按下 鼠标左键 40 52\n" +
            "帧 0 抬起 鼠标左键 40 52   // 行尾注释\n";

        /// <summary>
        /// 跑一段图形程序,两头都对一遍:
        /// ① C# 虚拟机 + 无头呈现口(程序输出收进内存流)= 那几行的**规格**;
        /// ② C 解释器核 + `--hb-图形`(绘制命令落文件)+ `--hb-事件脚本`;
        /// 判据:程序输出逐字节一致 + 图形 dump 逐行一致。
        /// 返回**两边各自报出来的收到事件条数**(程序最后一行 `测试输出(收到)` 就是它;量不出来给 -1)。
        /// </summary>
        private static (int 机, int c) 图形与虚拟机逐行一致(string 解释器exe, string 标签, string 源, string 脚本文本, string 断言名)
        {
            var 编译结果 = 编译(源);
            const int 内存 = 虚拟机C.默认内存大小;

            // ---- C# 那一侧:虚拟机 + 无头呈现(程序输出单独收,图形命令收进另一个缓冲) ----
            var 程序出 = new MemoryStream();
            var 图形缓冲 = new MemoryStream();
            var 机 = new 虚拟机C();
            机.设内存大小(内存);
            机.初值表 = 编译结果.初值表;
            机.宿主调用 = 宿主能力C.处理;
            机.输出目标 = b => 程序出.WriteByte(b);
            图形宿主C.清空();
            图形宿主C.挂上(new 无头呈现C(new StreamWriter(图形缓冲, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" }));
            string 机报错 = "";
            try
            {
                图形宿主C.挂事件脚本(事件脚本C.解析(脚本文本));
                机.装载(编译结果.机器码);
                while (机.单步()) { }
            }
            catch (Exception ex) { 机报错 = ex.Message; }
            finally { 图形宿主C.挂上(null); 图形宿主C.清空(); }
            byte[] 图 = 图形缓冲.ToArray();

            // ---- C 那一侧:解释器核 + 无头呈现 + 事件脚本 ----
            string 暂存 = 解释器目录(标签);
            string 镜像 = Path.Combine(暂存, 标签 + ".hbimg");
            string 状态 = Path.Combine(暂存, 标签 + ".状态");
            string 图形文件 = Path.Combine(暂存, 标签 + ".图形");
            string 脚本文件 = Path.Combine(暂存, "事件脚本.txt");
            File.WriteAllText(脚本文件, 脚本文本, new UTF8Encoding(false));
            try { 镜像C.写(编译结果, 内存, 镜像); }
            catch (C后端错误C ex) { 记录(断言名, false, "写镜像就写不动:" + 截断(ex.Message, 200)); return (-1, -1); }

            var (退出码, 原生出, 原生误) = 跑原生带状态(解释器exe, null, 状态, 冻结秒, 镜像, 图形文件, 脚本文件);
            var 原生状态 = C状态C.读原生状态(状态);
            string c图 = File.Exists(图形文件) ? File.ReadAllText(图形文件, Encoding.UTF8) : "";
            int c图行 = 数行(c图);
            int 机收到 = 收到事件数(程序出.ToArray());
            int c收到 = 收到事件数(原生出);

            if (机报错.Length > 0)
            {
                string 关键词 = 错关键词(机报错);
                bool 也报 = 退出码 != 0 && 关键词.Length > 0 && 原生误.Contains(关键词, StringComparison.Ordinal);
                记录(断言名, 也报,
                    $"虚拟机报「{截断(机报错, 60)}」,解释器退出码 {退出码}、标准错误「{截断(原生误.Trim(), 60)}」" +
                    $" —— 两边都该拦得住({关键词})");
                return (-1, -1);
            }
            if (原生状态 == null)
            {
                记录(断言名, false, "解释器没落下状态转储(它自己没跑起来?)stderr:" + 截断(原生误, 160));
                return (-1, -1);
            }

            string csharp图 = Encoding.UTF8.GetString(图);
            int 图行 = 数行(csharp图);
            var 差异 = 逐行比(csharp图, c图);
            bool 输出一致 = 原生出.AsSpan().SequenceEqual(程序出.ToArray());
            bool 一致 = 差异 == null && 输出一致 && 图行 > 0;
            记录(断言名, 一致,
                差异 != null
                    ? $"图形 dump 第 {差异.Value.行号} 行对不上:C#「{截断(差异.Value.左, 90)}」/ C「{截断(差异.Value.右, 90)}」" +
                      $"(C# {图行} 行 / C {c图行} 行)"
                    : (输出一致
                        ? $"图形 dump 逐行一致({图行} 行:{标签};程序输出 {程序出.Length} 字节与 C# 版逐字节一致;" +
                          $"两边各自报收到的事件条数 = C# {机收到} / C {c收到})"
                        : $"程序输出对不上:C# {程序出.Length} 字节 / C {原生出.Length} 字节"));
            return (机收到, c收到);
        }

        /// <summary>帧数护栏:脚本说 4 帧,程序一轮不理关窗请求 —— 两边该报同一类错、退出码都非 0</summary>
        private static void 图形帧数护栏(string 解释器exe)
        {
            const string 断言名 = "C后端 图形 帧数宽限那道护栏两边都拦得住(不理关窗请求 → 同一类错 + 退出码非 0)";
            var 编译结果 = 编译(帧数护栏源);
            const int 内存 = 虚拟机C.默认内存大小;

            var 机 = new 虚拟机C();
            机.设内存大小(内存);
            机.初值表 = 编译结果.初值表;
            机.宿主调用 = 宿主能力C.处理;
            机.输出目标 = _ => { };
            图形宿主C.清空();
            图形宿主C.挂上(new 无头呈现C(TextWriter.Null));
            string 机报错 = "";
            try
            {
                图形宿主C.挂事件脚本(事件脚本C.解析(假事件脚本()));
                机.装载(编译结果.机器码);
                while (机.单步()) { }
            }
            catch (Exception ex) { 机报错 = ex.Message; }
            finally { 图形宿主C.挂上(null); 图形宿主C.清空(); }

            string 暂存 = 解释器目录("护栏");
            string 镜像 = Path.Combine(暂存, "护栏.hbimg");
            string 状态 = Path.Combine(暂存, "护栏.状态");
            string 图形文件 = Path.Combine(暂存, "护栏.图形");
            string 脚本文件 = Path.Combine(暂存, "事件脚本.txt");
            File.WriteAllText(脚本文件, 假事件脚本(), new UTF8Encoding(false));
            镜像C.写(编译结果, 内存, 镜像);
            var (退出码, _, 误) = 跑原生带状态(解释器exe, null, 状态, 冻结秒, 镜像, 图形文件, 脚本文件);
            string c图 = File.Exists(图形文件) ? File.ReadAllText(图形文件, Encoding.UTF8) : "";
            int c图行 = 数行(c图);

            bool 机拦 = 机报错.Contains("还在刷新", StringComparison.Ordinal);
            bool c拦 = 误.Contains("还在刷新", StringComparison.Ordinal) && 退出码 != 0;
            // 护栏的"位置"也要对得上:第 4 帧起投关窗请求,宽限 4 帧 ⇒ 两边都在**第 9 帧**拦下
            bool 帧号一致 = 机报错.Contains("第 9 帧", StringComparison.Ordinal)
                && 误.Contains("第 9 帧", StringComparison.Ordinal);
            记录(断言名, 机拦 && c拦,
                $"C# 版「{截断(机报错, 70)}」;C 版退出码 {退出码}「{截断(误.Trim(), 70)}」;" +
                $"两边都在第 9 帧拦下 = {帧号一致}(4 帧 + 宽限 4 帧 + 这一帧 = 第 9 帧);" +
                $"拦下那一刻图形 dump 已经有 {c图行} 行");
        }

        /// <summary>一个标准工程的两条路对拍(工程文件 / 资源 / 事件脚本都按工程目录里的那份走);
        /// 返回(图形 dump 行数, C# 侧报的收到事件条数, C 侧报的收到事件条数)</summary>
        private static (int 图行, int 机收到, int c收到) 图形标准工程(string 解释器exe, string 工程目录, string 断言名)
        {
            if (!Directory.Exists(工程目录)) { 跳过(断言名, $"找不到 {工程目录}"); return (0, -1, -1); }
            string? 工程路径 = 工程文件C.找工程文件(工程目录);
            if (工程路径 == null) { 跳过(断言名, $"工程目录里没有 .hbp:{工程目录}"); return (0, -1, -1); }
            var 工程 = 工程文件C.加载(工程路径);
            if (工程.文件.Count == 0) { 跳过(断言名, "空工程"); return (0, -1, -1); }
            int 内存; try { 内存 = 工程.内存字节数; } catch { 内存 = 虚拟机C.默认内存大小; }
            int 堆; try { 堆 = 工程.堆字节数; } catch { 堆 = 中间层C.默认堆大小; }
            int 池; try { 池 = 工程.池字节数; } catch { 池 = 中间层C.默认池大小; }
            int 软栈; try { 软栈 = 工程.软栈字节数; } catch { 软栈 = -1; }
            var 编译结果 = new 汇编器C().汇编多文件带调试(工程.读全部编译输入(), true, 堆, null, 池, 内存, 软栈);

            string? 脚本路径 = 事件脚本C.找脚本(工程目录);
            if (脚本路径 == null) { 跳过(断言名, "工程目录里没有 `事件脚本.txt`(没有输入就不该拿它当对拍夹具)"); return (0, -1, -1); }
            string 脚本文本 = File.ReadAllText(脚本路径, Encoding.UTF8);

            // ---- C# 那一侧:虚拟机 + 无头呈现 ----
            var 程序出 = new MemoryStream();
            var 图形缓冲 = new MemoryStream();
            var 机 = new 虚拟机C();
            机.设内存大小(内存);
            机.初值表 = 编译结果.初值表;
            机.宿主调用 = 宿主能力C.处理;
            机.输出目标 = b => 程序出.WriteByte(b);
            图形宿主C.清空();
            图形宿主C.挂上(new 无头呈现C(new StreamWriter(图形缓冲, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" }));
            string 机报错 = "";
            try
            {
                图形宿主C.挂事件脚本(事件脚本C.解析(脚本文本));
                机.装载(编译结果.机器码);
                while (机.单步()) { }
            }
            catch (Exception ex) { 机报错 = ex.Message; }
            finally { 图形宿主C.挂上(null); 图形宿主C.清空(); }

            // ---- C 那一侧 ----
            string 名 = Path.GetFileName(工程目录);
            string 暂存 = 解释器目录(名);
            string 镜像 = Path.Combine(暂存, 名 + ".hbimg");
            string 状态 = Path.Combine(暂存, 名 + ".状态");
            string 图形文件 = Path.Combine(暂存, 名 + ".图形");
            镜像C.写(编译结果, 内存, 镜像);
            var (退出码, 原生出, 原生误) = 跑原生带状态(解释器exe, null, 状态, 冻结秒, 镜像, 图形文件, 脚本路径);
            var 原生状态 = C状态C.读原生状态(状态);
            string c图 = File.Exists(图形文件) ? File.ReadAllText(图形文件, Encoding.UTF8) : "";
            string csharp图 = Encoding.UTF8.GetString(图形缓冲.ToArray());
            int 图行 = 数行(csharp图);
            int c图行 = 数行(c图);

            if (机报错.Length > 0)
            {
                string 关键词 = 错关键词(机报错);
                bool 也报 = 退出码 != 0 && 关键词.Length > 0 && 原生误.Contains(关键词, StringComparison.Ordinal);
                记录(断言名, 也报, $"两边都报错:C#「{截断(机报错, 60)}」/ C「{截断(原生误.Trim(), 60)}」");
                return (0, -1, -1);
            }
            if (原生状态 == null)
            {
                记录(断言名, false, "解释器没落下状态转储 stderr:" + 截断(原生误, 160));
                return (0, -1, -1);
            }
            var 差异 = 逐行比(csharp图, c图);
            bool 输出一致 = 原生出.AsSpan().SequenceEqual(程序出.ToArray());
            记录(断言名, 差异 == null && 输出一致 && 图行 > 0,
                差异 != null
                    ? $"图形 dump 第 {差异.Value.行号} 行对不上:C#「{截断(差异.Value.左, 90)}」/ C「{截断(差异.Value.右, 90)}」" +
                      $"(C# {图行} 行 / C {c图行} 行)"
                    : (输出一致
                        ? $"图形 dump 逐行一致({图行} 行;程序输出 {程序出.Length} 字节与 C# 版逐字节一致)"
                        : $"程序输出对不上:C# {程序出.Length} 字节 / C {原生出.Length} 字节"));

            return (图行, 收到事件数(程序出.ToArray()), 收到事件数(原生出));
        }

        /// <summary>
        /// 从程序输出里量"末尾那个有意义的数"(手写夹具 = 它报的收到事件条数;
        /// 两个标准工程 = 它们各自末尾那两行里的**前一个**数:示例15 末尾是 `帧` 与 `事件数`,
        /// 示例16 末尾是 `选中` 与 `还要不要重画`)——
        /// 算法 = **从后往前找第二个整行都是数字的行**(最后那一个是别的诊断数)。量不出来给 -1。
        ///
        /// 这一条**不是**"标准工程该报 5"那种判据:两个标准工程末尾打的本来就不是同一个量,
        /// 写死一个数就得跟着示例正文走、示例一改这里就假红。它钉的是"**两边数出来一样**"
        /// (真正"事件按帧投"的硬证据是上面那条**逐字节比** + 手写夹具那个确定的 4)。
        /// **边界**(实测踩到):图形 dump 要是混进标准输出,夹在中间的数字行会把"第二个"顶掉 ——
        /// 所以解释器那一侧**必须**把绘制命令收进 `--hb-图形` 那个文件(见 `对拍解释器一个工程`)。
        /// </summary>
        private static int 收到事件数(byte[] 程序输出)
        {
            string 净 = Encoding.UTF8.GetString(程序输出).Replace("\r\n", "\n").TrimEnd('\n');
            if (净.Length == 0) return -1;
            string[] 行 = 净.Split('\n');
            int 见到 = 0;
            for (int i = 行.Length - 1; i >= 0; i--)
                if (int.TryParse(行[i].Trim(), out int 条))
                {
                    见到++;
                    if (见到 == 2) return 条;
                }
            // 只有一行数字的夹具(手写小程序只 `测试输出(收到)`)—— 那一个就是它
            return 见到 == 1 && int.TryParse(找最后一个数字行(行), out int 只有一条) ? 只有一条 : -1;
        }

        private static string 找最后一个数字行(string[] 行)
        {
            for (int i = 行.Length - 1; i >= 0; i--)
                if (int.TryParse(行[i].Trim(), out _)) return 行[i].Trim();
            return "";
        }

        /// <summary>数一段文本有几行(空的算 0 行;末尾那个换行不算多一行)</summary>
        private static int 数行(string 文本)
        {
            if (文本.Length == 0) return 0;
            int n = 1;
            foreach (char c in 文本) if (c == '\n') n++;
            if (文本[^1] == '\n') n--;
            return n;
        }

        /// <summary>
        /// **逐行比**(R3-6-3 的判据就是这一句):两份文本按 `\n` 切开逐行对 ——
        /// 行数不一样或某一行的字节不一样都给"第几行 + 两边各是什么"。一致给 null。
        /// </summary>
        private static (int 行号, string 左, string 右)? 逐行比(string 左, string 右)
        {
            string[] 左行 = 左.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            string[] 右行 = 右.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (左.Length == 0) 左行 = [];
            if (右.Length == 0) 右行 = [];
            int n = Math.Max(左行.Length, 右行.Length);
            for (int i = 0; i < n; i++)
            {
                string a = i < 左行.Length ? 左行[i] : "(没有这一行)";
                string b = i < 右行.Length ? 右行[i] : "(没有这一行)";
                if (!string.Equals(a, b, StringComparison.Ordinal)) return (i + 1, a, b);
            }
            return null;
        }

        /// <summary>
        /// 认键名那一条(接线哨兵 + 真算):脚本里写得出的键名,两边认出来的是**同一个键码**。
        /// 口径按 `图形键C.认键名`:中文名 / 英文别名 / 单个字母 / `字母X` / `数字X` / `F1~F12` / 裸键码。
        /// </summary>
        private static void 图形键名哨兵()
        {
            string? 根 = 找仓库根目录();
            if (根 == null) { 跳过("C后端 图形 键名表认得与 `图形键C` 同一套", "找不到仓库根"); return; }
            string 路径 = Path.Combine(根, "C后端", "运行时.c");
            if (!File.Exists(路径)) { 跳过("C后端 图形 键名表认得与 `图形键C` 同一套", $"找不到 {路径}"); return; }
            string 源 = File.ReadAllText(路径, Encoding.UTF8);
            // 键名表那几条(与 `图形键C` 的常量逐条同值)+ 三处前缀认法(单个字母 / `字母X` / `数字X` / `F1~F12`)
            string[] 词 = ["\"回车\", 13", "\"退格\", 8", "\"空格\", 32", "\"左\", 37", "\"下\", 40",
                          "\"鼠标左键\", 1", "\"pageup\", 33", "\"字母\"", "\"数字\"",
                          "词[0] >= 'a' && 词[0] <= 'z'", "111 + 号", "词[6] >= '0' && 词[6] <= '9'"];
            var 缺 = 词.Where(x => !源.Contains(x, StringComparison.Ordinal)).ToArray();
            记录(键名哨兵名, 缺.Length == 0,
                $"键名 / 前缀认法缺 {缺.Length} 个{(缺.Length > 0 ? "(" + string.Join(" | ", 缺) + ")" : "")}" +
                " —— 这一条是接线哨兵(表还在、认法还在);认出来的码对不对由事件脚本那几条真跑钉");
        }

        /// <summary>
        /// **位图那一族的接线哨兵**(R4-12/控 1-②):八个功能号 + 参数区字节数表 + 八路分派 +
        /// 两套"贴位图"实现(Win32 / X11)+ 装载时清表,逐条在 `C后端\运行时.c` 里点得到名。
        ///
        /// ⚠ 这是**接线哨兵**:它只证明"这批东西在 C 侧落了地",**语义对不对由下面真跑那条钉**
        ///   (硬规矩:接线哨兵不许单独当功能正确性的证据)。
        /// </summary>
        private static void 位图能力层哨兵()
        {
            string? 根 = 找仓库根目录();
            if (根 == null) { 跳过(位图哨兵名, "找不到仓库根(读不到 `C后端\\运行时.c`)"); return; }
            string 路径 = Path.Combine(根, "C后端", "运行时.c");
            if (!File.Exists(路径)) { 跳过(位图哨兵名, $"找不到 {路径}"); return; }
            string 源 = File.ReadAllText(路径, Encoding.UTF8);

            // ① 常量段:**九个**号(号段 70~78,与 `图形宿主C` 逐号同值;78 = 控 1-① 的 `贴文字到位图`)
            string[] 号表 = ["#define HB_F_MAKEBMP    70u", "#define HB_F_FREEBMP    71u", "#define HB_F_BMPSIZE    72u",
                            "#define HB_F_PUTPIX     73u", "#define HB_F_GETPIX     74u", "#define HB_F_BLITBMP    75u",
                            "#define HB_F_LINEBMP    76u", "#define HB_F_RECTBMP    77u", "#define HB_F_TEXTBMP    78u"];
            var 缺号 = 号表.Where(x => !源.Contains(x, StringComparison.Ordinal)).ToArray();

            // ② 参数区字节数表(与 `图形宿主C.参数区字节数` 逐格同值)
            string[] 区表 = ["case HB_F_MAKEBMP:  return 12u;", "case HB_F_FREEBMP:  return 4u;",
                            "case HB_F_BMPSIZE:  return 12u;", "case HB_F_PUTPIX:   return 16u;",
                            "case HB_F_GETPIX:   return 16u;", "case HB_F_BLITBMP:  return 16u;",
                            "case HB_F_LINEBMP:  return 24u;", "case HB_F_RECTBMP:  return 24u;",
                            "case HB_F_TEXTBMP:  return 24u;"];
            var 缺区 = 区表.Where(x => !源.Contains(x, StringComparison.Ordinal)).ToArray();

            // ③ **九路**分派(78 那一档本侧如实"没这一手":给 `HB_ST_NOCAP`,不假装能画)
            string[] 分派 = ["case HB_F_MAKEBMP:  return hb_gfx_建位图(参数区);",
                            "case HB_F_FREEBMP:  return hb_bmp_放(hb_host_rd(参数区)) ? HB_ST_OK : HB_ST_BADHANDLE;",
                            "case HB_F_BMPSIZE:  return hb_gfx_位图尺寸(参数区);",
                            "case HB_F_PUTPIX:   return hb_gfx_画点到图(参数区);",
                            "case HB_F_GETPIX:   return hb_gfx_取图像素(参数区);",
                            "case HB_F_BLITBMP:  return hb_gfx_贴位图(参数区);",
                            "case HB_F_LINEBMP:  return hb_gfx_画线到图(参数区);",
                            "case HB_F_RECTBMP:  return hb_gfx_画矩形到图(参数区);",
                            "case HB_F_TEXTBMP:  return HB_ST_NOCAP;"];
            var 缺派 = 分派.Where(x => !源.Contains(x, StringComparison.Ordinal)).ToArray();

            // ④ 号段判定上界挪到 **78**(不改这一行,70~78 会被当成"不是图形号"落到 `default` 给状态 9)
            bool 上界 = 源.Contains("功能号 >= HB_F_OPENWIN && 功能号 <= HB_F_TEXTBMP", StringComparison.Ordinal);
            // ⑤ 位图仓那一套(句柄表 + 旧号永不复用 + 装载时清干净)
            bool 仓在 = 源.Contains("static hb_bmp_格 *hb_bmp_找(hb_u32 号)", StringComparison.Ordinal)
                && 源.Contains("hb_bmp_清光();", StringComparison.Ordinal);
            // ⑥ 两套"贴位图"实现:Win32 走 DIB 直贴、X11 走 XImage + XPutImage
            bool 两套 = 源.Contains("SetDIBitsToDevice(hb_win_画dc", StringComparison.Ordinal)
                && 源.Contains("hb_x.XPutImage(hb_win_显, hb_win_画布", StringComparison.Ordinal)
                && 源.Contains("{ \"XPutImage\", (void **)&hb_x.XPutImage, 0 }", StringComparison.Ordinal);
            // ⑦ 无头 dump 那一行(格式的事实源在 `绘制命令C.一句话()`)
            bool 行在 = 源.Contains("\"贴位图 x=%d y=%d 宽=%d 高=%d\\n\"", StringComparison.Ordinal);

            记录(位图哨兵名,
                缺号.Length == 0 && 缺区.Length == 0 && 缺派.Length == 0 && 上界 && 仓在 && 两套 && 行在,
                $"常量缺 {缺号.Length} 个{(缺号.Length > 0 ? "(" + string.Join(",", 缺号) + ")" : "")};" +
                $"参数区表缺 {缺区.Length} 格{(缺区.Length > 0 ? "(" + string.Join(" | ", 缺区) + ")" : "")};" +
                $"分派缺 {缺派.Length} 路{(缺派.Length > 0 ? "(" + string.Join(" | ", 缺派) + ")" : "")};" +
                $"号段上界 78 = {上界};位图仓 + 装载清表 = {仓在};Win32/X11 两套贴图 = {两套};无头 dump 那一行 = {行在}");
        }

        /// <summary>
        /// 一段**位图程序**(R4-12 那一族 70~77 全用一遍):建图 → 画点 / 线 / 矩形 → 逐点读回 →
        /// 问尺寸 → 开窗贴上去刷一帧 → 释放 → 旧号作废。
        ///
        /// ⚠ **一个句柄都不打印**:句柄是宿主侧的号、只增不减,而 C# 那边是**一个进程连跑几十段程序**、
        ///   C 那边每段都是新进程(从 1 起)⇒ 号**不可比**,打出来这条判据必然假红
        ///   (F-8 那一组当时就只判"不是 0")。要比的是**像素值 / 尺寸 / 返回值**。
        /// </summary>
        private const string C侧位图源 =
            "整数 图 = 0;\n" +
            "逻辑 建成 = 建位图(16, 8, 图);\n" +
            "如果 (建成) { 输出行(1); } 否则 { 输出行(0); }\n" +
            "画点到图(图, 3, 4, 4294901760);\n" +
            "输出行(取图像素(图, 3, 4));\n" +                  // 画进去的红
            "输出行(取图像素(图, 0, 0));\n" +                  // 没画过的格子
            "画线到图(图, 0, 2, 15, 2, 4278255360);\n" +       // 一整条线 = 一次调用
            "输出行(取图像素(图, 0, 2));\n" +
            "输出行(取图像素(图, 8, 2));\n" +
            "画矩形到图(图, 10, 5, 3, 2, 4278190335);\n" +     // 实心矩形
            "输出行(取图像素(图, 10, 5));\n" +
            "输出行(取图像素(图, 12, 6));\n" +
            "画点到图(图, 999, 999, 255);\n" +                 // 越界:不许报错
            "画矩形到图(图, 100, 100, 3, 2, 255);\n" +         // 整块在外面:一步都不走
            "输出行(取图像素(图, 999, 999));\n" +              // 越界读给 0
            "整数 宽 = 0;\n" +
            "整数 高 = 0;\n" +
            "逻辑 问到了 = 位图尺寸(图, 宽, 高);\n" +
            "输出行(宽);\n" +
            "输出行(高);\n" +
            "如果 (打开窗口(\"位图\", 80, 60))\n" +
            "{\n" +
            "    清屏(4278190080);\n" +
            "    贴位图(图, 8, 6);\n" +                          // 一整块像素 = 一条命令
            "    刷新();\n" +
            "    关闭窗口();\n" +
            "}\n" +
            "逻辑 放了 = 释放位图(图);\n" +
            "逻辑 再用 = 画点到图(图, 1, 1, 255);\n" +         // 旧号作废
            "如果 (放了 && !再用) { 输出行(1); } 否则 { 输出行(0); }\n" +
            "停止\n";

        /// <summary>
        /// **位图那一族的两条腿对拍**(判据 = 施工图 §3 控 1 那句话):
        /// 同一段位图程序,C# 虚拟机 + 无头呈现 与 C 解释器核 + `--hb-图形` 各跑一遍 ——
        /// **程序输出逐字节一致 + 图形 dump 逐行一致**,而且 C# 那一侧的十一个读数**本身就等于
        /// 画进去的那些值**(两边都读到 0 也算"一致",所以这一条不能省)。
        /// </summary>
        private static void 位图两腿对拍(string 解释器exe)
        {
            var 编译结果 = 编译(C侧位图源);
            const int 内存 = 虚拟机C.默认内存大小;

            // ---- C# 那一侧:虚拟机 + 无头呈现(与 `图形与虚拟机逐行一致` 同一套摆法)----
            var 程序出 = new MemoryStream();
            var 图形缓冲 = new MemoryStream();
            var 机 = new 虚拟机C();
            机.设内存大小(内存);
            机.初值表 = 编译结果.初值表;
            机.宿主调用 = 宿主能力C.处理;
            机.输出目标 = b => 程序出.WriteByte(b);
            图形宿主C.清空();
            图形宿主C.挂上(new 无头呈现C(new StreamWriter(图形缓冲, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" }));
            string 机报错 = "";
            try
            {
                图形宿主C.挂事件脚本(事件脚本C.解析(假事件脚本()));
                机.装载(编译结果.机器码);
                while (机.单步()) { }
            }
            catch (Exception ex) { 机报错 = ex.Message; }
            finally { 图形宿主C.挂上(null); 图形宿主C.清空(); }
            byte[] 机出 = 程序出.ToArray();
            string 机图 = Encoding.UTF8.GetString(图形缓冲.ToArray());

            // ---- C 那一侧:解释器核 + `--hb-图形` ----
            string 暂存 = 解释器目录("位图");
            string 镜像 = Path.Combine(暂存, "位图.hbimg");
            string 状态 = Path.Combine(暂存, "位图.状态");
            string 图形文件 = Path.Combine(暂存, "位图.图形");
            string 脚本文件 = Path.Combine(暂存, "事件脚本.txt");
            File.WriteAllText(脚本文件, 假事件脚本(), new UTF8Encoding(false));
            try { 镜像C.写(编译结果, 内存, 镜像); }
            catch (C后端错误C ex) { 记录(位图对拍断言名, false, "写镜像就写不动:" + 截断(ex.Message, 200)); return; }
            var (退出码, 原生出, 原生误) = 跑原生带状态(解释器exe, null, 状态, 冻结秒, 镜像, 图形文件, 脚本文件);
            string c图 = File.Exists(图形文件) ? File.ReadAllText(图形文件, Encoding.UTF8) : "";

            if (机报错.Length > 0)
            {
                记录(位图对拍断言名, false, $"C# 那一侧就抛了:{截断(机报错, 140)}(C 退出码 {退出码})");
                return;
            }

            // 期望值:11 个读数(画进去的颜色 / 空白格 / 尺寸 / 返回值)—— 与 C# 侧 `位图` 那一组同一口径
            string[] 该 = ["1", "4294901760", "0", "4278255360", "4278255360", "4278190335",
                          "4278190335", "0", "16", "8", "1"];
            string[] 行 = Encoding.UTF8.GetString(机出).Replace("\r\n", "\n").Trim().Split('\n');
            string 取(int i) => i < 行.Length ? 行[i].Trim() : "(缺)";
            string 值差 = "";
            for (int i = 0; i < 该.Length && 值差.Length == 0; i++)
                if (取(i) != 该[i]) 值差 = $"第 {i + 1} 个读数 C# 给 {取(i)}(该 {该[i]})";

            bool 输出一致 = 机出.AsSpan().SequenceEqual(原生出);
            var 图差 = 逐行比(机图, c图);
            bool 对 = 输出一致 && 图差 == null && 值差.Length == 0 && 数行(机图) > 0;
            记录(位图对拍断言名, 对,
                $"程序输出 C# {机出.Length} 字节 / C {原生出.Length} 字节(逐字节一致={输出一致});" +
                $"图形 dump C# {数行(机图)} 行 / C {数行(c图)} 行" +
                (图差 != null
                    ? $";第 {图差.Value.行号} 行对不上:C#「{截断(图差.Value.左, 80)}」/ C「{截断(图差.Value.右, 80)}」"
                    : ",逐行一致") +
                (值差.Length > 0
                    ? $";{值差}"
                    : $"({string.Join(" / ", Enumerable.Range(0, 该.Length).Select(i => 取(i)))})") +
                $";C 退出码 {退出码}" +
                (原生误.Trim().Length > 0 ? $",stderr:{截断(原生误.Trim(), 80)}" : ""));
        }
    }
}
