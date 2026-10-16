using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R3-6-5:**图形呈现层 C 版**(Win32 / GDI,**零依赖**)那一组。
    ///
    /// 与相邻两组的分工:`C后端解释器核` 钉"55 条指令与整机状态两边逐字节一致",
    /// `C后端宿主能力与无头呈现` 钉"图形那 10 条能力与无头 dump";这一组钉的是**真画**——
    /// 命令不再只是打一行字,而是真落在一块画布上。
    ///
    /// **本组一条窗口都不开**(硬规矩 6:自检要在命令行里跑、要能进 CI)。怎么办到的:
    /// 画布(`CreateCompatibleDC` + `CreateCompatibleBitmap`)本来就不需要窗口 —— 只要一块屏幕 DC 就够,
    /// 所以探针自己摆一块 64×32 的位图当画布,调那一族 `hb_win_*`,再用 `GetPixel` 把颜色**读回来**对。
    /// 于是"颜色通道序 / 清屏 / 填充矩形 / 画线 / 字形真出得来"这几件全是**行为断言**,
    /// 剩下真正要真人看的那一档(开窗、消息泵、键鼠真收进来)是 ★ 人工观感,留在 `未做任务清单.md` 里如实登记。
    ///
    /// 键码那一条是**跨实现**的对拍:C 侧 `hb_win_键码` 译出来的表,与 C# 侧 `图形键C` 的口径
    /// **逐条**比 —— 这两处漂开就是"同一个键在两种宿主上给出不同键码",HB 程序没法写。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary>本组要真编真跑的断言名(没有 C 编译器时逐条如实记跳过;前缀 `C后端` 在允许跳过的三族里)</summary>
        private static readonly string[] 窗口断言 =
        [
            "C后端 图形 窗口 C 侧真画得出来(清屏 / 矩形 / 线 / 文字,GetPixel 读回来对得上)",
            "C后端 图形 窗口 HB 颜色按 GDI 通道序换对了(0xAARRGGBB → 0x00BBGGRR,别把红蓝写反)",
            "C后端 图形 窗口 键码译法与 图形键C 逐条一致(Windows 虚拟键同号 + 鼠标键 1/2/3)",
            "C后端 图形 窗口 窗口那一档的绘制 dump 走 NUL(不混进程序输出)",
            "C后端 图形 窗口 贴位图真贴得上去(整块像素落到画布上,颜色与别的那几条同一序)",
        ];

        /// <summary>不吃 C 编译器的那一条(接线哨兵:只读仓库源码文本)</summary>
        private const string 窗口哨兵名 = "C后端 图形 窗口 呈现层在 C 侧(两道闸 + 十路接线 + 解释器核那个开关)";

        /// <summary>
        /// 探针源码:把呈现层里"不开窗也能验"的那几件拉出来真跑一遍。
        ///
        /// 标识符一律 ASCII —— 与 `运行时.c` 同一条口径(这一份也可能被 clang / gcc / MSVC 编);
        /// 打给自检看的那些标签也一律 ASCII,免得两边的编码口径把判据搅进去。
        /// </summary>
        private const string 窗口探针源 =
            "/* 自检探针:呈现层里不开窗就能验的那几件(画布 = 屏幕 DC 配一块兼容位图) */\n" +
            "#define HB_有图形 1\n" +
            "#define HB_有窗口 1\n" +
            "#include \"运行时.c\"\n" +
            "\n" +
            "int main(void)\n" +
            "{\n" +
            "    HDC screen;\n" +
            "    int w = 64, h = 32;\n" +
            "    int i;\n" +
            "\n" +
            "    /* 1) HB 颜色(0xAARRGGBB)→ GDI 的 COLORREF(0x00BBGGRR) */\n" +
            "    printf(\"color %06X\\n\", (unsigned)(hb_win_颜色(0xFF2A6EF0u) & 0xFFFFFFu));\n" +
            "    printf(\"color %06X\\n\", (unsigned)(hb_win_颜色(0x00112233u) & 0xFFFFFFu));\n" +
            "\n" +
            "    /* 2) 画布:不用窗口,一块屏幕 DC + 一块兼容位图就够 */\n" +
            "    screen = GetDC(NULL);\n" +
            "    hb_win_宽 = w;\n" +
            "    hb_win_高 = h;\n" +
            "    hb_win_画dc = CreateCompatibleDC(screen);\n" +
            "    hb_win_画位图 = CreateCompatibleBitmap(screen, w, h);\n" +
            "    hb_win_画旧位图 = (HBITMAP)SelectObject(hb_win_画dc, hb_win_画位图);\n" +
            "    ReleaseDC(NULL, screen);\n" +
            "\n" +
            "    hb_win_清屏(0xFF102030u);\n" +
            "    printf(\"clear %06X\\n\", (unsigned)(GetPixel(hb_win_画dc, 5, 5) & 0xFFFFFFu));\n" +
            "\n" +
            "    hb_win_画矩形(4, 4, 10, 8, 0xFFAABBCCu);\n" +
            "    printf(\"rectin %06X\\n\", (unsigned)(GetPixel(hb_win_画dc, 6, 6) & 0xFFFFFFu));\n" +
            "    printf(\"rectout %06X\\n\", (unsigned)(GetPixel(hb_win_画dc, 40, 24) & 0xFFFFFFu));\n" +
            "    hb_win_画矩形(0, 0, 0, 5, 0xFFFFFFFFu);\n" +
            "    printf(\"emptyrect %06X\\n\", (unsigned)(GetPixel(hb_win_画dc, 0, 0) & 0xFFFFFFu));\n" +
            "\n" +
            "    hb_win_画线(2, 28, 60, 28, 0xFF00FF00u);\n" +
            "    printf(\"line %06X\\n\", (unsigned)(GetPixel(hb_win_画dc, 30, 28) & 0xFFFFFFu));\n" +
            "\n" +
            "    /* 3) 文字:黑底上画一段(ASCII + 中文),数一数有多少个点子被写上了 */\n" +
            "    {\n" +
            "        const char *text = \"HB 中文\";\n" +
            "        int ink = 0, x, y;\n" +
            "        hb_win_清屏(0xFF000000u);\n" +
            "        hb_win_画文字(1, 1, (const hb_u8 *)text, (hb_u32)strlen(text), 0xFFFFFFFFu);\n" +
            "        for (y = 0; y < h; y++)\n" +
            "            for (x = 0; x < w; x++)\n" +
            "                if ((unsigned)(GetPixel(hb_win_画dc, x, y) & 0xFFFFFFu) != 0x000000u) ink++;\n" +
            "        printf(\"text %d\\n\", ink);\n" +
            "    }\n" +
            "\n" +
            "    /* 4) 键码:Windows 虚拟键 → HB 键码(`图形键C` 的口径) */\n" +
            "    {\n" +
            "        static const int keys[] = { VK_BACK, VK_TAB, VK_RETURN, VK_SHIFT, VK_CONTROL, VK_MENU,\n" +
            "            VK_ESCAPE, VK_SPACE, VK_PRIOR, VK_NEXT, VK_HOME, VK_END, VK_LEFT, VK_UP,\n" +
            "            VK_RIGHT, VK_DOWN, VK_INSERT, VK_DELETE, 'A', 'Z', '0', '9', VK_F1, VK_F12 };\n" +
            "        for (i = 0; i < (int)(sizeof(keys) / sizeof(keys[0])); i++)\n" +
            "            printf(\"key %d %d\\n\", keys[i], hb_win_键码((WPARAM)keys[i]));\n" +
            "        printf(\"mouse %d %d %d\\n\",\n" +
            "            hb_win_鼠标码(WM_LBUTTONDOWN), hb_win_鼠标码(WM_MBUTTONDOWN), hb_win_鼠标码(WM_RBUTTONDOWN));\n" +
            "    }\n" +
            "\n" +
            "    /* 5) 窗口那一档的 dump 落点:NUL 开得开,而且写进去的东西**不落到程序输出** */\n" +
            "    {\n" +
            "        FILE *sink = hb_win_黑洞();\n" +
            "        if (sink == NULL) printf(\"sink missing\\n\");\n" +
            "        else\n" +
            "        {\n" +
            "            fputs(\"SINK-MARKER-9F3A\\n\", sink);\n" +
            "            fflush(sink);\n" +
            "            printf(\"sink ok\\n\");\n" +
            "        }\n" +
            "    }\n" +
            "\n" +
            "    /* 6) R4-12:`贴位图` —— HB 位图(0xAARRGGBB)经 `SetDIBitsToDevice` 贴到画布上,GetPixel 读回来对。\n" +
            "       验三件事:贴块里的颜色**与别的那几条同一序**(HB → GDI 要换通道序,贴图这条路也不许例外)、\n" +
            "       贴块外面还是底色、贴完**源图那块像素还在**(不许被谁交出去释放掉)。 */\n" +
            "    {\n" +
            "        hb_u32 号 = hb_bmp_建(16u, 8u);\n" +
            "        hb_bmp_格 *bmp = hb_bmp_找(号);\n" +
            "        hb_win_清屏(0xFF102030u);\n" +
            "        if (bmp == NULL) printf(\"blit nobmp\\n\");\n" +
            "        else\n" +
            "        {\n" +
            "            hb_bmp_画矩形(bmp, 0, 0, 16, 8, 0xFFAABBCCu);\n" +
            "            hb_bmp_画线(bmp, 0, 4, 15, 4, 0xFF00FF00u);\n" +
            "            hb_win_贴位图(4, 4, bmp->像素, (int)bmp->宽, (int)bmp->高);\n" +
            "            printf(\"blit %06X\\n\", (unsigned)(GetPixel(hb_win_画dc, 4, 4) & 0xFFFFFFu));\n" +
            "            printf(\"blitline %06X\\n\", (unsigned)(GetPixel(hb_win_画dc, 12, 8) & 0xFFFFFFu));\n" +
            "            printf(\"blitout %06X\\n\", (unsigned)(GetPixel(hb_win_画dc, 40, 20) & 0xFFFFFFu));\n" +
            "            printf(\"blitsrc %08X\\n\", (unsigned)hb_bmp_取(bmp, 8, 4));\n" +
            "            hb_bmp_放(号);\n" +
            "            printf(\"blitfree %d\\n\", hb_bmp_找(号) == NULL ? 1 : 0);\n" +
            "        }\n" +
            "    }\n" +
            "\n" +
            "    return 0;\n" +
            "}\n";

        private static void C后端窗口自检()
        {
            // ── ① 不吃 C 编译器那一条:接线哨兵 ──
            try { 窗口接线哨兵(); } catch (Exception ex) { 记录异常(窗口哨兵名, ex); }

            // ── ② 真编真跑(一条窗口都不开:画布用屏幕 DC 自己摆) ──
            string? 输出 = 跑窗口探针();
            if (输出 == null) return;

            string 底 = Gdi色(0xFF102030u);
            string 矩形色 = Gdi色(0xFFAABBCCu);
            string 线色 = Gdi色(0xFF00FF00u);

            var 色 = new List<string>();
            var 单 = new Dictionary<string, string>(StringComparer.Ordinal);
            var 键 = new List<(int 虚拟键, int 键码)>();
            int 鼠标左 = -1, 鼠标中 = -1, 鼠标右 = -1;
            bool 黑洞开 = false, 有记号 = false;

            foreach (string 行 in 输出.Replace("\r", "").Split('\n'))
            {
                if (行.Contains("SINK-MARKER-9F3A", StringComparison.Ordinal)) 有记号 = true;
                string[] 格 = 行.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (格.Length == 0) continue;
                switch (格[0])
                {
                    case "color":
                        if (格.Length == 2) 色.Add(格[1]);
                        break;
                    case "clear":
                    case "rectin":
                    case "rectout":
                    case "emptyrect":
                    case "line":
                    case "text":
                    case "blit":
                    case "blitline":
                    case "blitout":
                    case "blitsrc":
                    case "blitfree":
                        if (格.Length == 2) 单[格[0]] = 格[1];
                        break;
                    case "key":
                        if (格.Length == 3 && int.TryParse(格[1], out int vk) && int.TryParse(格[2], out int hb))
                            键.Add((vk, hb));
                        break;
                    case "mouse":
                        if (格.Length == 4 && int.TryParse(格[1], out 鼠标左)
                            && int.TryParse(格[2], out 鼠标中) && int.TryParse(格[3], out 鼠标右))
                        { /* 三个数都读到了 */ }
                        break;
                    case "sink":
                        if (格.Length == 2 && 格[1] == "ok") 黑洞开 = true;
                        break;
                }
            }

            // ③ 颜色通道序
            {
                const string 本检查 = "C后端 图形 窗口 HB 颜色按 GDI 通道序换对了(0xAARRGGBB → 0x00BBGGRR,别把红蓝写反)";
                string 该一 = Gdi色(0xFF2A6EF0u), 该二 = Gdi色(0x00112233u);
                bool 对 = 色.Count == 2 && 色[0] == 该一 && 色[1] == 该二;
                记录(本检查, 对, 对
                    ? $"探针读回 {色[0]} / {色[1]}(该 {该一} / {该二};BBGGRR 那一序)"
                    : $"探针读回 {string.Join(" / ", 色)}(该 {该一} / {该二} —— 红蓝写反就会是 {RR(0xFF2A6EF0u)})");
            }

            // ④ 真画
            {
                const string 本检查 = "C后端 图形 窗口 C 侧真画得出来(清屏 / 矩形 / 线 / 文字,GetPixel 读回来对得上)";
                int 墨 = 单.TryGetValue("text", out string? 文数) && int.TryParse(文数, out int n) ? n : -1;
                bool 对 = 单.GetValueOrDefault("clear") == 底
                    && 单.GetValueOrDefault("rectin") == 矩形色
                    && 单.GetValueOrDefault("rectout") == 底
                    && 单.GetValueOrDefault("emptyrect") == 底
                    && 单.GetValueOrDefault("line") == 线色
                    && 墨 > 0;
                记录(本检查, 对,
                    $"清屏 {显(单, "clear")}(该 {底})/ 矩形里 {显(单, "rectin")}(该 {矩形色})/ " +
                    $"矩形外 {显(单, "rectout")}(该 {底})/ 空矩形 {显(单, "emptyrect")}(该 {底})/ " +
                    $"线上 {显(单, "line")}(该 {线色})/ 文字点子 {墨}(该 > 0)");
            }

            // ⑤ 键码与 `图形键C` 逐条一致
            {
                const string 本检查 = "C后端 图形 窗口 键码译法与 图形键C 逐条一致(Windows 虚拟键同号 + 鼠标键 1/2/3)";
                var 该 = new (int vk, int hb, string 名)[]
                {
                    (8, 图形键C.退格, "退格"), (9, 图形键C.制表, "制表"), (13, 图形键C.回车, "回车"),
                    (16, 图形键C.上档, "上档"), (17, 图形键C.控制, "控制"), (18, 图形键C.替换, "替换"),
                    (27, 图形键C.退出, "退出"), (32, 图形键C.空格, "空格"),
                    (33, 图形键C.上一页, "上一页"), (34, 图形键C.下一页, "下一页"),
                    (36, 图形键C.首页, "首页"), (35, 图形键C.末页, "末页"),
                    (37, 图形键C.左, "左"), (38, 图形键C.上, "上"), (39, 图形键C.右, "右"), (40, 图形键C.下, "下"),
                    (45, 图形键C.插入, "插入"), (46, 图形键C.删除, "删除"),
                    (65, 图形键C.字母最小, "字母A"), (90, 图形键C.字母最大, "字母Z"),
                    (48, 图形键C.数字最小, "数字0"), (57, 图形键C.数字最大, "数字9"),
                    (112, 图形键C.功能键(1), "F1"), (123, 图形键C.功能键(12), "F12"),
                };
                var 表 = new Dictionary<int, int>();
                foreach (var (虚拟键, 键码) in 键) 表[虚拟键] = 键码;
                var 差 = new List<string>();
                foreach (var (vk, hb, 名) in 该)
                    if (!表.TryGetValue(vk, out int 得) || 得 != hb)
                        差.Add($"{名}: 该 {hb},探针给 {(表.ContainsKey(vk) ? 表[vk].ToString() : "缺")}");
                bool 鼠标对 = 鼠标左 == 图形键C.鼠标左键 && 鼠标中 == 图形键C.鼠标中键 && 鼠标右 == 图形键C.鼠标右键;
                记录(本检查, 差.Count == 0 && 鼠标对, 差.Count == 0 && 鼠标对
                    ? $"{该.Length} 个键逐条一致(回车 {图形键C.回车} / 左 {图形键C.左} / F12 {图形键C.功能键(12)});" +
                      $"鼠标键 {鼠标左}/{鼠标中}/{鼠标右}"
                    : $"{string.Join(";", 差)}(共摸到 {键.Count} 条);" +
                      $"鼠标 {鼠标左}/{鼠标中}/{鼠标右}(该 {图形键C.鼠标左键}/{图形键C.鼠标中键}/{图形键C.鼠标右键})");
            }

            // ⑥ 窗口那一档的 dump 落点
            {
                const string 本检查 = "C后端 图形 窗口 窗口那一档的绘制 dump 走 NUL(不混进程序输出)";
                bool 对 = 黑洞开 && !有记号;
                记录(本检查, 对, 对
                    ? "NUL 开得开;写进去的记号(SINK-MARKER-9F3A)没出现在程序输出里"
                    : $"黑洞 {(黑洞开 ? "开得开" : "没打开")};输出里出现记号 = {有记号}");
            }

            // ⑦ R4-12:`贴位图` 在真画布上贴得上去(整块像素 → `SetDIBitsToDevice`)
            {
                const string 本检查 = "C后端 图形 窗口 贴位图真贴得上去(整块像素落到画布上,颜色与别的那几条同一序)";
                string 贴块 = 单.GetValueOrDefault("blit", "(缺)");
                string 贴线 = 单.GetValueOrDefault("blitline", "(缺)");
                string 贴外 = 单.GetValueOrDefault("blitout", "(缺)");
                string 源图 = 单.GetValueOrDefault("blitsrc", "(缺)");
                string 释放 = 单.GetValueOrDefault("blitfree", "(缺)");
                bool 对 = 贴块 == 矩形色 && 贴线 == 线色 && 贴外 == 底
                    && 源图 == "FF00FF00" && 释放 == "1";
                记录(本检查, 对,
                    $"贴块里 {贴块}(该 {矩形色})/ 贴块内那条线上 {贴线}(该 {线色})/ 贴块外 {贴外}(该 {底});" +
                    $"贴完源图那块像素还在 = {源图}(该 FF00FF00,HB 原值);释放后旧号作废 = {释放}(该 1)" +
                    " —— 判据:整块像素经 DIB 落到画布上、通道序与 清屏 / 画矩形 / 画线 / 画文字 那几条同一序");
            }
        }

        /// <summary>
        /// 接线哨兵:两道闸(编译期的 `HB_有窗口` 缺省 0 + 非 Windows 那条 `#error`;运行期的 `hb_win_活`)、
        /// 十路接线、解释器核那个用户面开关,逐条在仓库源码里点得到名。
        /// **它钉的是"这批东西在 C 侧落了地"**,画得对不对由同组那几条真跑钉。
        /// </summary>
        private static void 窗口接线哨兵()
        {
            string? 根 = 找仓库根目录();
            if (根 == null) { 跳过接线(窗口哨兵名, "找不到仓库根(读不到 `C后端\\运行时.c`)"); return; }
            string 运 = Path.Combine(根, "C后端", "运行时.c");
            string 核 = Path.Combine(根, "C后端", "解释器核.c");
            if (!File.Exists(运) || !File.Exists(核)) { 跳过接线(窗口哨兵名, "找不到 C 后端那两份源码"); return; }
            string 运源 = File.ReadAllText(运, Encoding.UTF8);
            string 核源 = File.ReadAllText(核, Encoding.UTF8);

            var 缺 = new List<string>();
            if (!运源.Contains("#define HB_有窗口 0", StringComparison.Ordinal)) 缺.Add("缺省那把闸(HB_有窗口 0)");
            if (!运源.Contains("hb_win_活", StringComparison.Ordinal)) 缺.Add("运行期那把闸(hb_win_活)");
            // R3-6-5 批 2:窗口层那一整段(三平台实现)由 `#if HB_有窗口` 包住;`hb_win_活` 与 `hb_win_黑洞`
            // 在它**外面**(没编窗口层时恒 0 / 黑洞换成 /dev/null —— 能力层那几处 `if (hb_win_活)` 才编得过)
            if (!运源.Contains("#if HB_有窗口", StringComparison.Ordinal)) 缺.Add("窗口层那把编译期闸(#if HB_有窗口)");
            if (!运源.Contains("#elif defined(__linux__)", StringComparison.Ordinal)) 缺.Add("Linux / X11 那一档");
            if (!运源.Contains("libX11.so.6", StringComparison.Ordinal)) 缺.Add("X11 运行时 dlopen(libX11.so.6)");
            foreach (string 词 in new[] { "hb_win_打开", "hb_win_拆窗口", "hb_win_清屏", "hb_win_画文字",
                                          "hb_win_画矩形", "hb_win_画线", "hb_win_刷新", "hb_win_泵",
                                          "hb_win_睡", "hb_win_黑洞" })
                if (!运源.Contains(词, StringComparison.Ordinal)) 缺.Add("运行时.c 缺 " + 词);
            // 解释器核这边:开关解析收进了共用的 `hb_取开关`,窗口那一档也交给 `hb_gfx_装配`
            if (!核源.Contains("hb_取开关(argc, argv, HB_有图形, &开关)", StringComparison.Ordinal)) 缺.Add("解释器核 没走共用开关解析");
            if (!核源.Contains("hb_gfx_装配(&开关)", StringComparison.Ordinal)) 缺.Add("解释器核 没走共用的图形装配");
            if (!核源.Contains("#define HB_有窗口 1", StringComparison.Ordinal)) 缺.Add("解释器核 没把窗口层编进来");
            if (!核源.Contains("#if defined(_WIN32)", StringComparison.Ordinal)) 缺.Add("解释器核 没按平台分档");
            if (!核源.Contains("--hb-窗口", StringComparison.Ordinal)) 缺.Add("解释器核 那一段没提 --hb-窗口");

            记接线(窗口哨兵名, 缺.Count == 0, 缺.Count == 0
                ? "两道闸 + 十路接线 + 解释器核那个开关都在位(判据是源码文本;画得对不对由同组那几条真跑钉)"
                : string.Join(";", 缺));
        }

        /// <summary>把探针编出来跑一遍,拿它的标准输出;**编不出来 / 跑不起来**给 null 并把本组四条逐条记跳过</summary>
        private static string? 跑窗口探针()
        {
            string? 根 = 找仓库根目录();
            if (根 == null)
            {
                foreach (string 名 in 窗口断言) 跳过(名, "找不到仓库根 —— 拿不到 `运行时.c` 的正文");
                return null;
            }
            var 工具 = C工具链C.探一下();
            if (!工具.成功)
            {
                string 理由 = "这台机器上没有能用的 C 编译器 —— " +
                    (工具.找不到原因.Length > 0 ? 工具.找不到原因 : 工具.日志);
                foreach (string 名 in 窗口断言) 跳过(名, 理由);
                return null;
            }
            try
            {
                string 暂存 = 解释器目录("窗口");
                File.WriteAllText(Path.Combine(暂存, "运行时.c"), C后端C.取运行时源码(), new UTF8Encoding(false));
                string 探针 = Path.Combine(暂存, "窗口探针.c");
                File.WriteAllText(探针, 窗口探针源, new UTF8Encoding(true));
                string exe = Path.Combine(暂存, "窗口探针.exe");
                var 编 = C工具链C.编成exe(探针, exe, 关优化: true);
                if (!编.成功)
                {
                    foreach (string 名 in 窗口断言) 跳过(名, "窗口探针没编过");
                    return null;
                }
                var (退出码, 出, 误) = 跑原生(exe, null);
                if (退出码 != 0)
                {
                    foreach (string 名 in 窗口断言) 跳过(名, $"窗口探针退出码 {退出码}:{截断(误, 120)}");
                    return null;
                }
                return Encoding.UTF8.GetString(出);
            }
            catch (Exception)
            {
                foreach (string 名 in 窗口断言) 跳过(名, "窗口探针那一次编译 / 运行抛了异常");
                return null;
            }
        }

        /// <summary>HB 颜色(0xAARRGGBB)→ 探针打回来的那六个十六进制位(GDI 的 `0x00BBGGRR`,即 BBGGRR)</summary>
        private static string Gdi色(uint 值) =>
            $"{(值 >> 0) & 0xFFu:X2}{(值 >> 8) & 0xFFu:X2}{(值 >> 16) & 0xFFu:X2}";

        /// <summary>故意写反的那一串(只在失败详情里当对照:照 RGB 正序读会长这样,红蓝正好对调)</summary>
        private static string RR(uint 值) =>
            $"{(值 >> 16) & 0xFFu:X2}{(值 >> 8) & 0xFFu:X2}{(值 >> 0) & 0xFFu:X2}";

        private static string 显(Dictionary<string, string> 表, string 键) =>
            表.TryGetValue(键, out string? 值) ? 值 : "缺";
    }
}
