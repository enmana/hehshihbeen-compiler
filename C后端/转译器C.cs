using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>C 后端转译失败(给用户看的中文原因;CLI 拿它当一条编译错误报)</summary>
    public sealed class C后端错误C : Exception
    {
        public C后端错误C(string 消息) : base(消息) { }
    }

    /// <summary>C 后端一次转译的产物:C 源码 + 几个能拿去钉自检的数</summary>
    public sealed class C后端结果C
    {
        /// <summary>整份 C 源码 = 运行期(运行时.c)+ 程序本体(逐条静态展开)</summary>
        public string 源码 = "";
        /// <summary>展开出来的指令条数(自检拿它跟机器码长度对账)</summary>
        public int 指令条数;
        /// <summary>代码段里出现的跳转 / 调用条数(直接跳转 + 间接转移)</summary>
        public int 跳转条数;
        /// <summary>间接转移(返回 / 调用寄存器 / 跳转寄存器)条数 —— 它们走分派表</summary>
        public int 间接条数;
    }

    /// <summary>
    /// R3-6-4 批 1:C 后端 —— 把 HB **机器码**转成 **C 源码**。
    ///
    /// 形态(用户 2026-09-22 拍板第 2 条):**逐条静态展开** —— 机器码里每一条指令都摊成一句 C,
    /// `跳转` / 条件跳转直接编成 `goto L_地址`;目标在寄存器里的那三条(`返回` / `调用寄存器` /
    /// `跳转寄存器`)落到一张**分派表**上:有 computed goto 的编译器(GCC / Clang)走 `goto *表[PC/4]`,
    /// 别的(MSVC)退回 `switch (PC/4)`。
    ///
    /// 为什么这么做:纯 `switch(PC)` 等于把解释器编译进 exe(用户拍板里明确不取),
    /// 而"每条指令一个标签"能让 `-O2` 把跳转折成真跳转 —— 这是"编出来的是原生代码"这句话的落脚点。
    ///
    /// **宿主能力**(`宿主调用` 0x31)从**批 3** 起走 `hb_host_call(参数区, 功能号)`;
    /// 编出来的是一份**独立 exe**:本地能力(文件 / 目录 / 环境变量 / 时间 / 分块读)真做,
    /// 要白名单的两条(`运行进程` / `取网址`)与老的独立 exe 一样返回状态 1「没挂这项能力」——
    /// 详见 `C后端\运行时.c` 里批 3 那一段的说明。
    ///
    /// **一份 C 源码里不许有时间戳**:同一份机器码转出来的 C 必须逐字节可复现
    /// (自检拿两次转译比字符串)。
    /// </summary>
    public static class C后端C
    {
        /// <summary>运行期源码在程序集里的资源名(见三个 csproj 的 `EmbeddedResource LogicalName`)</summary>
        public const string 运行时资源名 = "hb运行时.c";

        /// <summary>
        /// 取 `C后端\运行时.c` 的文本:优先读**嵌进程序集**的那份(hbcc / IDE 从哪儿跑都找得到),
        /// 找不到就按仓库根找盘上那份(自检 / 探针在源码树里跑时走这条兜底)。
        /// </summary>
        public static string 取运行时源码()
        {
            using (var 流 = typeof(C后端C).Assembly.GetManifestResourceStream(运行时资源名))
            {
                if (流 != null)
                {
                    using var 读 = new StreamReader(流, Encoding.UTF8);
                    return 读.ReadToEnd().Replace("\r\n", "\n");
                }
            }
            foreach (string 起点 in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var 目录 = new DirectoryInfo(起点);
                for (int 上 = 0; 上 < 8 && 目录 != null; 上++, 目录 = 目录.Parent)
                {
                    string 候选 = Path.Combine(目录.FullName, "C后端", "运行时.c");
                    if (File.Exists(候选)) return File.ReadAllText(候选, Encoding.UTF8).Replace("\r\n", "\n");
                }
            }
            throw new C后端错误C(
                $"找不到 C 后端运行期源码(`C后端\\运行时.c`)。程序和它必须在同一棵树里;" +
                $"从别处跑 hbcc 时请重新构建一次(运行期会嵌进程序集)。");
        }

        /// <summary>
        /// 把一份编译结果转成 C 源码。`内存大小` 用编译期那个值(默认 1MB),
        /// C 程序按它 `calloc` 同一块内存 —— 与虚拟机的内存映像逐字节同口径。
        /// </summary>
        public static C后端结果C 转译(汇编调试结果C 编译, int 内存大小)
        {
            ArgumentNullException.ThrowIfNull(编译);
            byte[] 码 = 编译.机器码;
            if (码.Length == 0)
                throw new C后端错误C("这份程序没有代码段(机器码是空的),没法走 C 后端 —— 检查工程里是不是没有主程序。");
            // `机器码` 是**整块内存映像**(代码段 + 静态数据段:池 / 软栈 / 堆那些隐藏区都在里面,且整块为零)。
            // 指令只在前 `代码字节数` 字节里;后面那一段交给 calloc(`hb_boot` 里有说明),免得
            // 生成一份几十万字节的 C 数组(实测:小程序的映像就有 16 万字节,九成九是零)。
            int 代码长 = 编译.代码字节数 > 0 && 编译.代码字节数 <= 码.Length ? 编译.代码字节数 : 码.Length;
            if (代码长 % 4 != 0)
                throw new C后端错误C($"代码段长度 {代码长} 不是 4 的倍数(指令定长 4 字节)—— 这份编译结果不完整,请重新编译。");

            int 条数 = 代码长 / 4;
            var 跳转目标 = new HashSet<int>();
            int 跳转条数 = 0, 间接条数 = 0;

            // ── 第一遍:解一遍码,收跳转目标 ──
            for (int i = 0; i < 条数; i++)
            {
                uint 指令 = 取字(码, i * 4);
                byte 操作码 = (byte)((指令 >> 24) & 0xFF);
                if (操作码 == 0x07 || 指令C.取格式(操作码) == 指令格式.跳转地址)
                {
                    跳转目标.Add(取跳转目标(操作码, 指令));
                    跳转条数++;
                }
                else if (操作码 is 0x0E or 0x35 or 0x36)
                {
                    跳转条数++;
                    间接条数++;
                }
            }
            foreach (int 目标 in 跳转目标)
            {
                if (目标 < 0 || 目标 > 代码长 || 目标 % 4 != 0 || (目标 != 代码长 && 目标 + 4 > 代码长))
                    throw new C后端错误C(
                        $"C 后端:跳转 / 调用的目标 0x{目标:X} 不是代码段里的一条指令" +
                        $"(代码段 0 到 0x{代码长:X},指令定长 4 字节,目标必须 4 字节对齐)。" +
                        "批 1 只做代码段内的静态展开 —— 检查地址是不是算错了。");
            }

            var 出 = new StringBuilder();
            出.Append("/* ═══════════════════════════════════════════════════════════════════\n");
            出.Append("   本文件由 hbcc 的 C 后端自动生成,**用手改没有意义** ——\n");
            出.Append("   语义在 `C后端\\运行时.c`,展开规则在 `C后端\\转译器C.cs`。\n");
            出.Append($"   指令 {条数} 条 / 代码段 {代码长} 字节 / 静态数据段 {编译.数据字节数} 字节" +
                      $"(整块内存映像 {码.Length} 字节)/ 内存 {内存大小} 字节。\n");
            出.Append("   代码段之后**只展开非零字节**(零初始化的静态数据段由 calloc 兜住;实测一份\n");
            出.Append("   262984 字节的映像里,代码后非零 0 字节 —— 整块展开会白白多出两百多万字符的 C)。\n");
            出.Append("   ═══════════════════════════════════════════════════════════════════ */\n\n");
            // ── R3-6-5 批 2:转译产物也**带图形**(图形能力 60~69 + 无头呈现 + 事件脚本) ──
            // 口径(用户 2026-10-01 当班拍板取 a):**缺省仍不带** —— 不给那两个开关时,
            // 生成物跑起来与老版**逐字节**一样(图形那一档编译进来但一次都不执行,见运行时.c 那两道闸);
            // 给了 `--hb-图形` / `--hb-事件脚本` 就是"与解释器核同一个无头宿主",自检拿它做对拍。
            // **两个 `#define` 必须在 `#include "运行时.c"` 之前**(运行时那一份自己的 `#ifndef` 缺省是 0):
            // 第一版把它们排在 include 后面,于是图形层那一整段被跳过、`hb_gfx_装配` 根本没编出来
            // (实测:LNK2019 无法解析的外部符号 `hb_gfx_装配`)。
            出.Append("/* R3-6-5 批 2:这一份带图形(与解释器核同一档:图形能力 + 无头呈现 + 事件脚本)。\n");
            出.Append("   开关(`--hb-图形` / `--hb-事件脚本` / `--hb-窗口`)与解释器核**共用** `hb_取开关`;\n");
            出.Append("   一个都不给时 `hb_gfx_装配` 只是把状态复位,行为与不带图形的老版一字不差。\n");
            出.Append("   窗口那一档(Win32 / GDI,Linux / X11)也编进来:只有运行期 `--hb-窗口` 才真开,不给就空转。 */\n");
            出.Append("#define HB_有图形 1\n");
            出.Append("#define HB_有窗口 1\n\n");
            出.Append(取运行时源码());
            出.Append("\n/* ────────────────────────── 程序本体 ────────────────────────── */\n\n");

            // 机器码:留在内存映像里(与虚拟机同一个"代码段就在地址 0"口径,自修改代码 / 取地址都还对得上)
            出.Append("static const hb_u8 hb_code[] =\n{\n");
            for (int i = 0; i < 代码长; i += 16)
            {
                出.Append("    ");
                for (int k = i; k < Math.Min(i + 16, 代码长); k++)
                    出.Append("0x").Append(码[k].ToString("X2", CultureInfo.InvariantCulture)).Append(',');
                出.Append('\n');
            }
            出.Append("};\n\n");

            // 代码段之后那些**非零**字节(零件一组:地址 / 值)。实际几乎总是空 ——
            // 数据段整块是零初始化的,`calloc` 兜住;真有非零就一小撮,别为它生成整块映像。
            var 尾 = new List<(int 地址, byte 值)>();
            for (int i = 代码长; i < 码.Length; i++)
                if (码[i] != 0) 尾.Add((i, 码[i]));
            出.Append("static const hb_u32 hb_img[] =\n{\n");
            foreach (var (地址, 值) in 尾) 出.Append($"    0x{地址:X}u, 0x{值:X2}u,\n");
            if (尾.Count == 0) 出.Append("    0u, 0u,\n");
            出.Append("};\n");
            出.Append($"static const hb_u32 hb_img_n = {(uint)尾.Count}u;\n\n");

            // 顶层常量初值(三件一组:地址 / 宽度 / 值)
            出.Append("static const hb_u32 hb_init[] =\n{\n");
            foreach (var (地址, 宽度, 值) in 编译.初值表)
                出.Append($"    0x{地址:X}u, {(uint)宽度}u, 0x{值:X}u,\n");
            if (编译.初值表.Count == 0) 出.Append("    0u, 0u, 0u,\n");       // C 里空初始化列表不合法,垫一条(条数给 0)
            出.Append("};\n");
            出.Append($"static const hb_u32 hb_init_n = {(uint)编译.初值表.Count}u;\n\n");

            // 错误槽:槽地址 + 「错误号 → 说明」
            uint 槽地址 = 0;
            var 消息 = new List<(int 号, string 说明)>();
            foreach (var (槽标签, 表) in 编译.运行期报错)
            {
                if (槽地址 == 0 && 编译.标签表.TryGetValue(槽标签, out int 地址0)) 槽地址 = (uint)地址0;
                foreach (var (号, 说明) in 表.OrderBy(x => x.号)) 消息.Add((号, 说明));
            }
            出.Append($"static const hb_u32 hb_slot_addr = 0x{槽地址:X}u;   /* 0 = 这份程序没有错误槽 */\n");
            if (消息.Count == 0)
            {
                出.Append("static const hb_err_msg hb_err_table[] = { { 0u, \"\" } };\n");
            }
            else
            {
                出.Append("static const hb_err_msg hb_err_table[] =\n{\n");
                foreach (var (号, 说明) in 消息)
                    出.Append($"    {{ {(uint)号}u, {C字符串(说明)} }},\n");
                出.Append("};\n");
            }
            出.Append($"static const hb_u32 hb_err_n = {(uint)消息.Count}u;\n\n");

            // 源码行表(地址升序:地址 >= addr 的都算这一行;段数远小于指令数)
            var 行段 = new List<(int 地址, int 行)>();
            for (int i = 0; i < 条数; i++)
            {
                int 行 = 执行行映射C.查源行(编译.调试文件表, (uint)(i * 4));
                if (行段.Count == 0 || 行段[^1].行 != 行) 行段.Add((i * 4, 行));
            }
            if (行段.Count == 0) 行段.Add((0, 0));
            出.Append("static const hb_span hb_line_spans[] =\n{\n");
            foreach (var (地址, 行) in 行段) 出.Append($"    {{ 0x{地址:X}u, {(uint)行}u }},\n");
            出.Append("};\n");
            出.Append($"static const hb_u32 hb_line_n = {(uint)(条数 == 0 ? 0 : 行段.Count)}u;\n\n");

            出.Append("#define HB_CODE_LEN ((hb_u32)sizeof hb_code)\n\n");
            出.Append("int main(int argc, char **argv)\n{\n");
            出.Append("    /* R3-6-4 批 2:对拍要看\"跑完之后机器里到底是什么\"。只有显式给了 `--hb-dump <路径>`\n");
            出.Append("       才落那份转储(自检用);不出现这条参数时一个字节都不写。\n");
            出.Append("       **参数名取 ASCII 也照样要转一遍**:Windows 上 CRT 给的 `argv` 是**按本机代码页**\n");
            出.Append("       转出来的字节,非 ASCII 的**路径**(自检的暂存目录叫 `…\\工具\\临时\\…`)进去就是乱码,\n");
            出.Append("       `_wfopen` 拿着乱码开不出文件 —— 转储悄悄落不下来。\n");
            出.Append("       R3-6-3 起这一段与解释器核共用 `hb_取参数`(宽字符拿、自己转 UTF-8);\n");
            出.Append("       R3-6-5 批 2 起开关解析也共用 `hb_取开关`。 */\n");
            出.Append("    hb_开关 hb_sw;\n");
            出.Append("    {\n");
            出.Append("        int hb_args_n = 0;\n");
            出.Append("        char **hb_args = hb_取参数(argc, argv, &hb_args_n);\n");
            出.Append("        if (hb_args != NULL) { argc = hb_args_n; argv = hb_args; }\n");
            出.Append("    }\n");
            出.Append("    /* R3-6-4 批 3:`--hb-now <秒>` 把 `当前时间()` 的钟**停住**(自检对拍用)。\n");
            出.Append("       为什么非要这个开关:`当前时间()` 的结果会写进运行内存,而全量对拍连内存一起比 ——\n");
            出.Append("       两次跑落在同一秒里靠不住,于是两边冻结成同一个秒(C# 那边见 `宿主能力C.取当前秒`)。\n");
            出.Append("       不给这条参数就是本机真时间,用户那条路上没有它。*/\n");
            出.Append("    if (hb_取开关(argc, argv, HB_有图形, &hb_sw) != 0) { hb_用法(argv[0], HB_有图形); return 2; }\n");
            出.Append("    /* 图形那一档:先挂呈现口(事件脚本要在第 0 帧就投得出来),再装载。\n");
            出.Append("       这一段收在 `运行时.c` 的 `hb_gfx_装配`(解释器核那条 `main` 共用同一句);\n");
            出.Append("       给了 `--hb-图形` 却开不了那个文件,当场说清楚 —— 别让绘制命令静悄悄写进程序输出。 */\n");
            出.Append("    if (hb_gfx_装配(&hb_sw) != 0) return 2;\n");
            出.Append("    hb_boot(hb_code, HB_CODE_LEN,\n");
            出.Append("            hb_img, hb_img_n,\n");
            出.Append("            hb_init, hb_init_n,\n");
            出.Append("            hb_slot_addr, hb_err_table, hb_err_n,\n");
            出.Append("            hb_line_spans, hb_line_n,\n");
            出.Append($"            {(uint)Math.Max(0, 编译.数据字节数)}u,   /* 静态数据段(状态导出里报出去,与虚拟机同口径) */\n");
            出.Append($"            {(uint)内存大小}u,\n");
            出.Append("            hb_sw.状态文件);\n");
            出.Append("#if HB_COMPUTED_GOTO\n");
            出.Append("    const void *hb_labels[] =\n    {\n");
            for (int i = 0; i < 条数; i += 8)
            {
                出.Append("        ");
                for (int k = i; k < Math.Min(i + 8, 条数); k++) 出.Append($"&&L_{k * 4}, ");
                出.Append('\n');
            }
            出.Append("    };\n");
            出.Append("#endif\n");
            出.Append("    goto L_0;\n\n");

            // ── 第二遍:逐条静态展开 ──
            for (int i = 0; i < 条数; i++)
            {
                int 地址 = i * 4;
                uint 指令 = 取字(码, 地址);
                // 标签后面紧跟一句"走到这条指令上了":对拍要看**跑过的条数**与**最近路径**
                // (输出一样但条数不同 = 走的不是同一条路,只比输出根本看不出来)。
                出.Append($"L_{地址}:\n");
                出.Append($"    hb_到达(0x{地址:X}u);\n");
                出.Append(发一条(指令, 地址, 代码长));
            }

            出.Append("\n    /* 代码段末尾:再往下就是零初始化的数据段(头一个字 0x00000000 = 停止),与虚拟机同口径 */\n");
            出.Append("hb_end:\n");
            出.Append("    hb_stop();\n\n");
            出.Append("hb_dispatch:\n");
            出.Append("#if HB_COMPUTED_GOTO\n");
            出.Append("    if ((hb_pc & 3u) != 0 || (hb_pc >> 2) >= (hb_u32)(sizeof hb_labels / sizeof hb_labels[0]))\n");
            出.Append("        hb_bad_pc(hb_pc, HB_CODE_LEN);\n");
            出.Append("    goto *hb_labels[hb_pc >> 2];\n");
            出.Append("#else\n");
            出.Append("    switch (hb_pc >> 2)\n    {\n");
            for (int i = 0; i < 条数; i++) 出.Append($"    case {i}u: goto L_{i * 4};\n");
            出.Append("    default: hb_bad_pc(hb_pc, HB_CODE_LEN); break;\n");
            出.Append("    }\n");
            出.Append("#endif\n");
            出.Append("}\n");

            return new C后端结果C
            {
                源码 = 出.ToString(),
                指令条数 = 条数,
                跳转条数 = 跳转条数,
                间接条数 = 间接条数,
            };
        }

        /// <summary>机器码里第 `偏移` 字节起的 4 字节小端指令(与虚拟机取指同一口径)</summary>
        private static uint 取字(byte[] 码, int 偏移) =>
            (uint)(码[偏移] | (码[偏移 + 1] << 8) | (码[偏移 + 2] << 16) | (码[偏移 + 3] << 24));

        /// <summary>
        /// 这条指令要跳到哪个**字节地址**:`跳转地址`格式是低 24 位左移 2,
        /// `为零跳转`(0x07)借的是"寄存器地址"格式、低 20 位**就是字节地址**(不左移)。
        /// </summary>
        private static int 取跳转目标(byte 操作码, uint 指令)
        {
            if (操作码 == 0x07) return (int)(指令 & 0xFFFFF);
            return (int)((指令 & 0xFFFFFF) << 2);
        }

        /// <summary>
        /// 把一条指令摊成 C 语句(逐条静态展开的核心)。
        /// 每条都先落 `hb_pc`:运行期错误要拿它查源码行(与 `虚拟机C` 的行号口径一致);
        /// 只有**真会出错**的那几条才写这行,免得给热循环白加一次全局写。
        /// </summary>
        private static string 发一条(uint 指令, int 地址, int 码长)
        {
            byte 操作码 = (byte)((指令 >> 24) & 0xFF);
            int 寄存器1 = (int)((指令 >> 20) & 0xF);          // [23:20] 寄存器(各指令叫法不同)
            int 寄存器2 = (int)((指令 >> 16) & 0xF);          // [19:16] 源 / 下标寄存器
            int 寄存器3 = (int)((指令 >> 12) & 0xF);          // [15:12] 值寄存器(三寄存器格式)
            uint 立即数 = 指令 & 0xFFFFF;
            uint 小立即数 = (指令 >> 12) & 0xF;
            uint 小立即数2 = (指令 >> 8) & 0xF;
            var 出 = new StringBuilder("    ");
            // 每条指令开头都把"当前指令地址"落到 `hb_pc`:
            // ① 转译期是常量、C 编译器看得见,`-O2` 会把它消掉;
            // ② 三处**运行期要看** `hb_pc` 的地方(运行期错误查行号、`返回` 的对齐报错、统一分派
            //    的越界报错)都靠它;
            // ③ 跳转那条路会把它改成**目标地址**(与虚拟机 `程序计数器` 同一个口径)。
            // **间接转移(`返回` / `调用寄存器` / `跳转寄存器`)要显式把它设成"这一条"**:
            // 分派回来的那一条也许走的是**另一条指令**,不能吃前一家的常量。
            string 落址() => $"hb_pc = 0x{地址:X}u; ";

            if (操作码 == 0x0E)
            {
                // `返回`:目标在硬件栈上。**发码之前**先把"当前指令地址"记下来 ——
                // 分派回来的那一条也许是另一条指令,不能吃上一条留下的常量。
                出.Append($"hb_pc = 0x{地址:X}u; ");
                出.Append("hb_pc = hb_pop(); goto hb_dispatch;");
                return 出.Append('\n').ToString();
            }
            switch (操作码)
            {
                case 0x00: 出.Append(落址()).Append("hb_stop();"); break;                    // 停止 HALT
                case 0x01: 出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_ld8(0x{立即数:X}u);"); break;      // 加载
                case 0x02: 出.Append(落址()).Append($"hb_st8(0x{立即数:X}u, hb_r[{寄存器1}]);"); break;       // 存储
                case 0x03: 出.Append($"hb_r[{寄存器1}] = 0x{立即数:X}u;"); break;                              // 加载立即数
                case 0x04: 出.Append($"hb_r[{寄存器1}] += hb_r[{寄存器2}];"); break;                                // 加法
                case 0x05: 出.Append($"hb_r[{寄存器1}] -= hb_r[{寄存器2}];"); break;                                // 减法
                case 0x06: 出.Append($"hb_pc = 0x{取跳转目标(操作码, 指令):X}u; goto {跳转标签(取跳转目标(操作码, 指令), 码长)};"); break;          // 跳转
                case 0x07: 出.Append($"if (hb_r[{寄存器1}] == 0) {{ hb_pc = 0x{取跳转目标(操作码, 指令):X}u; goto {跳转标签(取跳转目标(操作码, 指令), 码长)}; }}"); break;   // 为零跳转
                case 0x08: 出.Append($"hb_cmp(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;                          // 比较
                case 0x09: 出.Append($"if (hb_psw == 1) {{ hb_pc = 0x{取跳转目标(操作码, 指令):X}u; goto {跳转标签(取跳转目标(操作码, 指令), 码长)}; }}"); break;   // 大于跳转
                case 0x0A: 出.Append($"if (hb_psw == 2) {{ hb_pc = 0x{取跳转目标(操作码, 指令):X}u; goto {跳转标签(取跳转目标(操作码, 指令), 码长)}; }}"); break;   // 小于跳转
                case 0x1B: 出.Append($"if (hb_psw == 0) {{ hb_pc = 0x{取跳转目标(操作码, 指令):X}u; goto {跳转标签(取跳转目标(操作码, 指令), 码长)}; }}"); break;   // 相等跳转
                case 0x1C: 出.Append($"if (hb_psw != 0) {{ hb_pc = 0x{取跳转目标(操作码, 指令):X}u; goto {跳转标签(取跳转目标(操作码, 指令), 码长)}; }}"); break;   // 不相等跳转
                case 0x0B: 出.Append(落址()).Append($"hb_push(hb_r[{寄存器1}]);"); break;                      // 入栈
                case 0x0C: 出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_pop();"); break;                    // 出栈
                case 0x0D: 出.Append(落址()).Append($"hb_push(0x{地址 + 4:X}u); goto {跳转标签(取跳转目标(操作码, 指令), 码长)};"); break;  // 调用
                case 0x0E: 出.Append(落址()).Append("hb_pc = hb_pop(); goto hb_dispatch;"); break;        // 返回
                case 0x0F: 出.Append($"hb_r[{寄存器1}] ^= hb_r[{寄存器2}];"); break;                                // 按位异或
                case 0x10: 出.Append($"hb_r[{寄存器1}] &= hb_r[{寄存器2}];"); break;                                // 与
                case 0x11: 出.Append($"hb_r[{寄存器1}] |= hb_r[{寄存器2}];"); break;                                // 或
                case 0x12: 出.Append($"hb_r[{寄存器1}] = hb_r[{寄存器1}] << (hb_r[{寄存器2}] & 31u);"); break;            // 左移
                case 0x13: 出.Append($"hb_r[{寄存器1}] = hb_r[{寄存器1}] >> (hb_r[{寄存器2}] & 31u);"); break;            // 右移
                case 0x14: 出.Append($"hb_r[{寄存器1}] += 0x{立即数:X}u;"); break;                              // 加立即数
                case 0x15: 出.Append($"hb_r[{寄存器1}] -= 0x{立即数:X}u;"); break;                              // 减立即数
                case 0x16: 出.Append($"hb_r[{寄存器1}] = 0x{立即数:X}u;"); break;                               // 取地址(常量地址)
                case 0x17: 出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_ld8(hb_r[{寄存器2}]);"); break;           // 加载间接寻址
                case 0x18: 出.Append(落址()).Append($"hb_st8(hb_r[{寄存器2}], hb_r[{寄存器1}]);"); break;            // 存储间接寻址
                case 0x19: 出.Append($"hb_r[{寄存器1}] ^= 0xFFFFFFFFu;"); break;                                // 取反
                case 0x1A: 出.Append($"hb_cmp(hb_r[{寄存器1}], 0x{立即数:X}u);"); break;                        // 比较立即数
                case 0x1D: 出.Append($"hb_r[{寄存器1}] = hb_r[{寄存器2}];"); break;                                  // 复制
                case 0x1E: 出.Append($"hb_r[{寄存器1}] = (0x{立即数:X}u << 12);"); break;                       // 载入高位
                case 0x1F: 出.Append($"hb_r[{寄存器1}] |= 0x{立即数:X}u;"); break;                              // 或立即数
                case 0x20: 出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_ld32(0x{立即数:X}u);"); break;       // 取字
                case 0x21: 出.Append(落址()).Append($"hb_st32(0x{立即数:X}u, hb_r[{寄存器1}]);"); break;        // 存字
                case 0x22: 出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_ld32(hb_r[{寄存器2}]);"); break;          // 取字间接
                case 0x23: 出.Append(落址()).Append($"hb_st32(hb_r[{寄存器2}], hb_r[{寄存器1}]);"); break;           // 存字间接
                case 0x24: 出.Append($"hb_r[{寄存器1}] = hb_r[{寄存器1}] * hb_r[{寄存器2}];"); break;                     // 乘法
                case 0x25:                                                                                 // 除法(除零 = 运行期错误)
                    出.Append("{ hb_u32 dvs = hb_r[").Append(寄存器2).Append("]; ")
                       .Append(落址())
                       .Append("if (dvs == 0) hb_fail(")
                       .Append(C字符串("除零(除数是 0 —— 先判断除数再除)")).Append("); ")
                       .Append("hb_r[").Append(寄存器1).Append("] = hb_r[").Append(寄存器1).Append("] / dvs; }");
                    break;
                case 0x26:                                                                                 // 取余(除零 = 运行期错误)
                    出.Append("{ hb_u32 dvs = hb_r[").Append(寄存器2).Append("]; ")
                       .Append(落址())
                       .Append("if (dvs == 0) hb_fail(")
                       .Append(C字符串("除零(取余的除数是 0 —— 先判断除数再取余)")).Append("); ")
                       .Append("hb_r[").Append(寄存器1).Append("] = hb_r[").Append(寄存器1).Append("] % dvs; }");
                    break;
                case 0x27: 出.Append($"hb_r[{寄存器1}] = hb_in();"); break;                                     // 读取字符
                case 0x28: 出.Append($"hb_out((hb_u8)hb_r[{寄存器1}]);"); break;                                // 输出字符
                case 0x29: 出.Append("hb_newline();"); break;                                              // 输出换行
                case 0x2A: 出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_ld32(hb_r[15] + 0x{立即数:X}u);"); break;     // 取字帧
                case 0x2B: 出.Append(落址()).Append($"hb_st32(hb_r[15] + 0x{立即数:X}u, hb_r[{寄存器1}]);"); break;      // 存字帧
                case 0x2C: 出.Append($"hb_r[{寄存器1}] = hb_r[15] + 0x{立即数:X}u;"); break;                    // 取地址帧
                case 0x2D: 出.Append($"hb_r[{寄存器1}] = hb_r[{寄存器1}] << (0x{立即数:X}u & 31u);"); break;         // 左移立即数
                case 0x2E: 出.Append($"hb_r[{寄存器1}] &= 0x{立即数:X}u;"); break;                              // 与立即数
                case 0x2F: 出.Append($"hb_r[{寄存器1}] = hb_cmp_set(hb_r[{寄存器1}], hb_r[{寄存器2}], 0x{小立即数:X}u);"); break;   // 比较取值
                case 0x30: 出.Append(落址()).Append("hb_stop();"); break;                                   // 断点(没有调试器,按停机走)
                case 0x31: 出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_host_call(hb_r[{寄存器1}], 0x{立即数:X}u);"); break;   // 宿主调用
                case 0x32: 出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_ld32(hb_r[{寄存器1}] + (hb_r[{寄存器2}] << {小立即数}u));"); break;   // 取字索引
                case 0x33: 出.Append(落址()).Append($"hb_st32(hb_r[{寄存器1}] + (hb_r[{寄存器2}] << {小立即数2}u), hb_r[{寄存器3}]);"); break;   // 存字索引
                case 0x34: 出.Append($"hb_r[{寄存器1}] = hb_r[{寄存器1}] >> (0x{立即数:X}u & 31u);"); break;         // 右移立即数
                case 0x35: 出.Append(落址()).Append($"hb_call_reg(hb_r[{寄存器1}], 0x{地址 + 4:X}u); goto hb_dispatch;"); break;  // 调用寄存器
                case 0x36: 出.Append(落址()).Append($"hb_jmp_reg(hb_r[{寄存器1}]); goto hb_dispatch;"); break;   // 跳转寄存器
                case 0x37: 出.Append(落址()).Append($"if (hb_r[{寄存器1}] == 0u) {{ hb_jmp_reg(hb_r[{寄存器2}]); goto hb_dispatch; }}"); break;   // 为零跳转寄存器
                // ---- 通用化(2026-10):硬件浮点(单精度)。这几条**不会**报运行期错误 ⇒ 不落址
                // (与 `虚拟机C` 同一口径:报错才落址;多写一次会让热循环白加一次全局写)
                case 0x38: 出.Append($"hb_r[{寄存器1}] = hb_fadd(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;   // 浮点加法
                case 0x39: 出.Append($"hb_r[{寄存器1}] = hb_fsub(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;   // 浮点减法
                case 0x3A: 出.Append($"hb_r[{寄存器1}] = hb_fmul(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;   // 浮点乘法
                case 0x3B: 出.Append($"hb_r[{寄存器1}] = hb_fdiv(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;   // 浮点除法
                case 0x3C: 出.Append($"hb_r[{寄存器1}] = hb_fsqrt(hb_r[{寄存器1}]);"); break;                  // 浮点开方
                case 0x3D: 出.Append($"hb_r[{寄存器1}] = hb_fcmp(hb_r[{寄存器1}], hb_r[{寄存器2}], 0x{小立即数:X}u);"); break;   // 浮点比较
                case 0x3E: 出.Append($"hb_r[{寄存器1}] = hb_f2i(hb_r[{寄存器1}]);"); break;                    // 浮点转整数
                case 0x3F: 出.Append($"hb_r[{寄存器1}] = hb_i2f(hb_r[{寄存器1}]);"); break;                    // 整数转浮点
                // ---- 通用化第2批(2026-10):整数扩展 9 条 ----
                // 除零那两条**要落址**(它们会报运行期错误,报错要带源码行);其余七条不落址
                // (与 `虚拟机C` 同一口径:不报错的指令不写那次全局写)。
                case 0x40:                                                                                 // 有符号除法
                    出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_sdiv(hb_r[{寄存器1}], hb_r[{寄存器2}]);");
                    break;
                case 0x41:                                                                                 // 有符号取余
                    出.Append(落址()).Append($"hb_r[{寄存器1}] = hb_smod(hb_r[{寄存器1}], hb_r[{寄存器2}]);");
                    break;
                case 0x42: 出.Append($"hb_r[{寄存器1}] = hb_mulh(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;   // 高位乘
                case 0x43: 出.Append($"hb_r[{寄存器1}] = hb_clz(hb_r[{寄存器1}]);"); break;                     // 前导零
                case 0x44: 出.Append($"hb_r[{寄存器1}] = hb_ctz(hb_r[{寄存器1}]);"); break;                     // 尾随零
                case 0x45: 出.Append($"hb_r[{寄存器1}] = hb_popcnt(hb_r[{寄存器1}]);"); break;                  // 数一位
                case 0x46: 出.Append($"hb_r[{寄存器1}] = hb_sign_ext(hb_r[{寄存器1}], 0x{立即数:X}u);"); break;   // 符号扩展
                case 0x47: 出.Append($"hb_r[{寄存器1}] = hb_zero_ext(hb_r[{寄存器1}], 0x{立即数:X}u);"); break;   // 零扩展
                // ---- ISA 扩容 批 1:浮点补齐(0x48~0x4B)----
                // 四条**都不报运行期错误**(除零 / 非数都按 IEEE 走)⇒ 与上面那八条浮点同一个口径:**不落址**。
                case 0x48: 出.Append($"hb_r[{寄存器1}] = hb_fmax(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;      // 浮点最大值
                case 0x49: 出.Append($"hb_r[{寄存器1}] = hb_fmin(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;      // 浮点最小值
                case 0x4A: 出.Append($"hb_r[{寄存器1}] = hb_floor(hb_r[{寄存器1}]);"); break;                      // 浮点向下取整
                case 0x4B: 出.Append($"hb_r[{寄存器1}] = hb_copysign(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;  // 浮点符号复制
                // ---- ISA 扩容 批 2:位与字节(0x4C~0x53);八条都不报运行期错误 ⇒ **不落址** ----
                case 0x4C: 出.Append($"hb_r[{寄存器1}] = hb_umulh(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;   // 双字乘高
                case 0x4D: 出.Append($"hb_r[{寄存器1}] = hb_umadd(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;   // 双字乘加
                case 0x4E: 出.Append($"hb_r[{寄存器1}] = hb_rol(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;     // 循环左移
                case 0x4F: 出.Append($"hb_r[{寄存器1}] = hb_ror(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;     // 循环右移
                case 0x50: 出.Append($"hb_r[{寄存器1}] = hb_bswap(hb_r[{寄存器1}]);"); break;                   // 字节交换
                case 0x51: 出.Append($"hb_r[{寄存器1}] = hb_getbyte(hb_r[{寄存器2}], 0x{小立即数}u);"); break;  // 取字节
                case 0x52: 出.Append($"hb_r[{寄存器1}] = hb_iabs(hb_r[{寄存器1}]);"); break;                   // 取整数绝对值
                case 0x53: 出.Append($"hb_r[{寄存器3}] = hb_r[{寄存器1}] + (hb_r[{寄存器2}] << 0x{小立即数2}u);"); break;   // 取地址索引                // ---- ISA 扩容 批 3 + 批 4(0x54~0x62)----
                // 报错的那四条(取半字索引 / 存半字索引 / 存字节索引 / 内存拷贝)落址;其余不落址。
                case 0x54: 出.Append(落址()).Append($"hb_r[{(指令 >> 12) & 0xFu}] = hb_ld32(hb_r[{(指令 >> 20) & 0xFu}] + (hb_r[{(指令 >> 16) & 0xFu}] << {小立即数2}u)) & 0xFFFFu;"); break;   // 取半字索引
                        // ⚠ 存半字索引**只写 2 字节**(读改写):第一版写的是 `hb_st32(址, 值 & 0xFFFF)` ——
                        //    那会把紧跟其后的 2 字节一起写成 0(第433批实测:`表[i] = 42` 把下标变量抹成 0)。
                        case 0x55: 出.Append(落址()).Append($"{{ uint32_t 址55 = hb_r[{(指令 >> 20) & 0xFu}] + (hb_r[{(指令 >> 16) & 0xFu}] << {小立即数2}u); hb_st32(址55, (hb_ld32(址55) & 0xFFFF0000u) | (hb_r[{(指令 >> 12) & 0xFu}] & 0xFFFFu)); }}"); break;   // 存半字索引
                case 0x56: 出.Append(落址()).Append($"hb_st32(hb_r[{(指令 >> 20) & 0xFu}] + (hb_r[{(指令 >> 16) & 0xFu}] << {小立即数2}u), (hb_ld32(hb_r[{(指令 >> 20) & 0xFu}] + (hb_r[{(指令 >> 16) & 0xFu}] << {小立即数2}u)) & 0xFFFFFF00u) | (hb_r[{(指令 >> 12) & 0xFu}] & 0xFFu));"); break;   // 存字节索引
                case 0x57: 出.Append(落址()).Append($"{{ uint32_t k, 长 = hb_r[{(指令 >> 16) & 0xFu}], 起 = hb_r[{(指令 >> 20) & 0xFu}], 到 = hb_r[{(指令 >> 12) & 0xFu}]; for (k = 0; k < 长; k++) hb_mem[到 + k] = hb_mem[起 + k]; }}"); break;   // 内存拷贝
                case 0x58: 出.Append($"hb_psw = (hb_r[{(指令 >> 12) & 0xFu}] >= hb_r[{(指令 >> 20) & 0xFu}] && hb_r[{(指令 >> 12) & 0xFu}] <= hb_r[{(指令 >> 16) & 0xFu}]) ? 0u : 2u;"); break;   // 立即数范围比较
                // ⚠ 关系码在 **[11:8]**(三寄存器档的 4 位字段 = `小立即数2`),不是 [15:12] ——
                //    第一版写成 `小立即数`(= [15:12],那是第三个寄存器),于是"关系"永远按乙寄存器的号去判:
                //    实测 `数学.最大(3, 9)` 原生腿给 3、虚拟机给 9(2026-10-13 把条件移动真用上时才露出来)。
                case 0x59: 出.Append($"hb_r[{寄存器1}] = hb_rel({小立即数2}u, hb_r[{寄存器2}], hb_r[{寄存器3}]) ? hb_r[{寄存器2}] : hb_r[{寄存器3}];"); break;   // 条件移动
                case 0x5A: 出.Append($"if (hb_rel({小立即数2}u, hb_r[{寄存器2}], hb_r[{寄存器3}])) hb_r[{寄存器1}] = hb_r[{寄存器2}];"); break;   // 比较并条件移动
                case 0x5B: 出.Append($"hb_r[{寄存器1}] = (hb_psw == 2u) ? 0u : 1u;"); break;   // 条件置位
                case 0x5C: 出.Append($"if (hb_psw != 2u) {{ hb_pc = 0x{(指令 & 0xFFFFFFu) << 2:X}u; goto hb_dispatch; }}"); break;   // 大于等于跳转
                case 0x5D: 出.Append($"if (hb_psw == 0u || hb_psw == 2u) {{ hb_pc = 0x{(指令 & 0xFFFFFFu) << 2:X}u; goto hb_dispatch; }}"); break;   // 小于等于跳转
                case 0x5E: 出.Append($"hb_pc = 0x{(指令 & 0xFFFFFFu) << 2:X}u; goto hb_dispatch;"); break;   // 尾调用跳转
                case 0x5F: 出.Append($"hb_r[{寄存器1}] = hb_sadd(hb_r[{寄存器1}], hb_r[{寄存器2}]);"); break;   // 饱和加
                case 0x60: break;   // 无操作
                case 0x61: 出.Append(落址()).Append($"{{ uint32_t 丢; for (丢 = 0; 丢 < 0x{立即数:X}u; 丢 += 4u) (void)hb_pop(); hb_pc = hb_pop(); goto hb_dispatch; }}"); break;   // 带弹栈返回
                case 0x62: 出.Append($"{{ uint32_t 顶 = hb_pop(); hb_r[{寄存器1}] = 顶; hb_push(顶); }}"); break;   // 取返回地址                default:
                    throw new C后端错误C(
                        $"C 后端不认识这条指令:操作码 0x{操作码:X2}(机器码地址 0x{地址:X})。" +
                        "这是编译器内部对不上(指令表里没有它)—— 请把这一段回报。");
            }
            return 出.Append('\n').ToString();
        }

        /// <summary>跳转目标 → C 标签名(代码段末尾那一下落在"停机"标签上,与虚拟机"落进零数据段"同口径)</summary>
        private static string 跳转标签(int 目标, int 码长) => 目标 >= 码长 ? "hb_end" : $"L_{目标}";

        /// <summary>
        /// 中文说明 → C 字符串字面量:非 ASCII 按 UTF-8 逐字节写成 `\xNN`
        /// (这样生成出来的 C 不挑源码字符集,三套编译器编出来都是同一串字节),
        /// 并且在紧跟十六进制数字时**断开字面量**(不然 `\xADB` 会被当成一个转义)。
        /// </summary>
        private static string C字符串(string 文本)
        {
            var 出 = new StringBuilder("\"");
            bool 刚转义 = false;
            foreach (char 字 in 文本)
            {
                if (字 is >= ' ' and < '\u007F' && 字 != '"' && 字 != '\\')
                {
                    if (刚转义 && 是十六进制数字(字)) 出.Append("\" \"");
                    出.Append(字);
                    刚转义 = false;
                }
                else if (字 == '"') { 出.Append("\\\""); 刚转义 = false; }
                else if (字 == '\\') { 出.Append("\\\\"); 刚转义 = false; }
                else if (字 == '\n') { 出.Append("\\n"); 刚转义 = false; }
                else if (字 == '\r') { 出.Append("\\r"); 刚转义 = false; }
                else if (字 == '\t') { 出.Append("\\t"); 刚转义 = false; }
                else
                {
                    foreach (byte b in Encoding.UTF8.GetBytes(字.ToString()))
                        出.Append("\\x").Append(b.ToString("X2", CultureInfo.InvariantCulture));
                    刚转义 = true;
                }
            }
            return 出.Append('"').ToString();
        }

        /// <summary>ASCII 的 0-9 / a-f / A-F(C 的 `\x` 转义会一直吃十六进制数字,要在这儿断开)</summary>
        private static bool 是十六进制数字(char 字) =>
            字 is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
    }
}
