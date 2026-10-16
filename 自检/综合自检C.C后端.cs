using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R3-6-4 批 1:C 后端(机器码 → C 源码 → **原生 exe**,不带 .NET)。
    ///
    /// 这一组分两半:
    ///   · **纯转译那半**(不需要任何 C 编译器):逐条静态展开的数对不对、同一个机器码转两次
    ///     是不是逐字节一样、批 1 不做的宿主能力有没有在**转译期**就拦下并给改法;
    ///   · **真编真跑那半**(要本机有 C 编译器):最小程序编出 exe 跑出 42、跟 **C# 虚拟机逐字节对拍**、
    ///     产物确实是原生可执行文件(PE 头、不带 .NET)、运行期错误带源码行号且退出码非 0。
    ///
    /// **没有 C 编译器就逐条如实记跳过**(写出理由与怎么装)—— 按 R4-2 的口径,跳过不许冒充通过。
    /// 这也是拍板里那句"自检加一组(有 cc 就跑对拍、没有就跳过并写明理由)"的落点。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary>C 后端那几条断言的**真跑部分**(要 C 编译器才能跑;没有就逐条记跳过)</summary>
        private static readonly string[] C后端真跑断言 =
        [
            "C后端 最小程序(输出 42)编出原生 exe 并跑对",
            "C后端 原生 exe 的输出与 C# 虚拟机逐字节一致(函数 / 递归 / 字符串 / 数组)",
            "C后端 产物是原生可执行文件、不带 .NET(PE 头 + 没有 mscoree)",
            "C后端 运行期错误带源码行号、退出码非 0(除零)",
            "C后端 输入(IN)与虚拟机一致(标准输入重定向)",
            "C后端 位图 独立 exe 腿:离屏那一族(建 / 画点 / 读点 / 线 / 矩形 / 尺寸 / 释放)与虚拟机逐字节一致",
        ];

        private static void C后端自检()
        {
            const int 内存 = 虚拟机C.默认内存大小;
            const string 最小源 = "整数 结果 = 42;\n输出(结果);\n输出换行;\n停止\n";

            汇编调试结果C 最小编译;
            try { 最小编译 = 编译(最小源); }
            catch (Exception ex) { 记录异常("C后端 示范程序编译(输出 42)", ex); return; }

            // ── 一、纯转译(不需要 C 编译器) ──
            C后端结果C 转译结果;
            C后端结果C 再转译结果;
            try
            {
                转译结果 = C后端C.转译(最小编译, 内存);
                再转译结果 = C后端C.转译(最小编译, 内存);

                记录("C后端 转译 逐条静态展开(指令条数 == 代码段 ÷ 4,一个不落)",
                    转译结果.指令条数 == 最小编译.代码字节数 / 4 && 转译结果.指令条数 > 0,
                    $"指令 {转译结果.指令条数} 条 / 代码段 {最小编译.代码字节数} 字节");

                记录("C后端 转译 同一份机器码转两次逐字节相同(没有时间戳,可复现)",
                    转译结果.源码 == 再转译结果.源码,
                    $"{转译结果.源码.Length} 字;{(转译结果.源码 == 再转译结果.源码 ? "两次一致" : "两次不一样")}");

                bool 有运行期 = 转译结果.源码.Contains("hb_boot(", StringComparison.Ordinal)
                    && 转译结果.源码.Contains("hb_stop(", StringComparison.Ordinal)
                    && 转译结果.源码.Contains("hb_cmp_set(", StringComparison.Ordinal)
                    && 转译结果.源码.Contains("hb_dispatch:", StringComparison.Ordinal);
                记录("C后端 转译 源码里拼进了运行期与分派表(hb_boot / hb_stop / hb_cmp_set / hb_dispatch)",
                    有运行期, 有运行期 ? "四样都在" : "少跑期那几样 —— 生成的 C 不自洽");

                bool 带行号 = 转译结果.源码.Contains("hb_line_spans[]", StringComparison.Ordinal)
                    && 转译结果.源码.Contains("运行期错误(第 %u 行)", StringComparison.Ordinal);
                记录("C后端 转译 拼进了源码行表(运行期错误能报行号)",
                    带行号, 带行号 ? "行表 + 行号文案都在" : "行表或行号文案缺了");
            }
            catch (Exception ex) { 记录异常("C后端 转译", ex); }

            // 批 3 起:宿主调用不再拦在转译期 —— 0x31 编成 `hb_host_call(参数区, 功能号)`
            string 宿主源码;
            try
            {
                var 码 = new 汇编器C().汇编("宿主调用 R0, 3\n停止");
                宿主源码 = C后端C.转译(new 汇编调试结果C { 机器码 = 码 }, 内存).源码;
            }
            catch (Exception ex) { 宿主源码 = "转译抛了异常:" + ex.Message; }
            记录("C后端 转译 宿主调用编成 hb_host_call(批 3 起不再转译期拦下)",
                宿主源码.Contains("hb_host_call(hb_r[0], 0x3u)", StringComparison.Ordinal)
                    && 宿主源码.Contains("static hb_u32 hb_host_call(", StringComparison.Ordinal),
                宿主源码.Contains("hb_host_call(", StringComparison.Ordinal)
                    ? "编出来的 C 里:宿主调用 → hb_host_call(hb_r[0], 0x3u),运行期那 16 条也在"
                    : "没编出 hb_host_call:" + 截断(宿主源码, 160));

            string 野跳文案 = 异常文本(() =>
            {
                var 码 = new 汇编器C().汇编("跳转 0x1000\n停止");
                C后端C.转译(new 汇编调试结果C { 机器码 = 码 }, 内存);
            });
            记录("C后端 转译 跳转目标不在代码段里 → 转译期报错带改法",
                野跳文案.Contains("不是代码段里的一条指令", StringComparison.Ordinal)
                    && 野跳文案.Contains("4 字节对齐", StringComparison.Ordinal),
                野跳文案);

            // ── 二、真编真跑(要 C 编译器) ──
            var 工具 = C工具链C.探一下();
            if (!工具.成功)
            {
                string 理由 = "这台机器上没有能用的 C 编译器 —— " +
                    (工具.找不到原因.Length > 0 ? 工具.找不到原因 : 工具.日志);
                foreach (string 名 in C后端真跑断言) 跳过(名, 理由);
                return;
            }

            try
            {
                跑原生对拍("C后端 最小程序(输出 42)编出原生 exe 并跑对", 最小源, 内存, "42\n");
                跑原生对拍("C后端 原生 exe 的输出与 C# 虚拟机逐字节一致(函数 / 递归 / 字符串 / 数组)",
                    是函数与递归源(), 内存, "1 1 2 6 24 120 \n5050\n142 余 6\n中文串abc\n10,20,30,40,\n");
                跑原生对拍("C后端 运行期错误带源码行号、退出码非 0(除零)",
                    "整数 除数 = 0;\n整数 商 = 100 / 除数;\n输出(商);\n输出换行;\n停止\n", 内存, null, "除零");
                跑原生对拍("C后端 输入(IN)与虚拟机一致(标准输入重定向)",
                    "整数 头 = 读取字符();\n整数 二 = 读取字符();\n输出(头);\n输出(二);\n输出换行;\n停止\n",
                    内存, "6566\n", null, "AB");
                // R4-12:位图那一族的**独立 exe 腿**(转译产物 —— 与解释器核那条腿共用同一份
                // `运行时.c`,但这一条走的是"机器码 → C 源码 → 原生 exe"那条路,得各验一份)。
                // ⚠ **这段不开窗**:转译产物这边没有呈现口,开窗两边会走岔(实测那正是无头 dump
                //   落点不同);离屏那七条与窗口无关,正好是这条腿能干净验的那一半,"贴位图"由
                //   解释器核那条腿(挂了无头呈现口)与 Windows 真画布那条(GetPixel 读回)分别钉。
                跑原生对拍("C后端 位图 独立 exe 腿:离屏那一族(建 / 画点 / 读点 / 线 / 矩形 / 尺寸 / 释放)与虚拟机逐字节一致",
                    是位图离屏源(), 内存, "1\n4294901760\n0\n4278255360\n4278255360\n"
                        + "4278190335\n4278190335\n0\n16\n8\n1\n");
            }
            catch (Exception ex) { 记录异常("C后端 真编真跑", ex); }

            // 产物得真是"原生可执行文件、不带 .NET"——这是这一整条路要换来的东西
            try
            {
                string 暂存 = Path.Combine(临时区C.基目录(), "hbcc_C后端自检", Environment.ProcessId.ToString());
                Directory.CreateDirectory(暂存);
                string c源 = Path.Combine(暂存, "最小_原生检查.c");
                string exe = Path.Combine(暂存, "最小_原生检查.exe");
                File.WriteAllText(c源, C后端C.转译(最小编译, 内存).源码, new UTF8Encoding(false));
                var 编 = C工具链C.编成exe(c源, exe);
                if (!编.成功)
                {
                    记录("C后端 产物是原生可执行文件、不带 .NET(PE 头 + 没有 mscoree)", false, 截断(编.日志, 200));
                }
                else
                {
                    byte[] 字节 = File.ReadAllBytes(exe);
                    string 全文 = Encoding.ASCII.GetString(字节);
                    bool 是pe = 字节.Length > 2 && 字节[0] == (byte)'M' && 字节[1] == (byte)'Z';
                    bool 无net = !全文.Contains("mscoree", StringComparison.OrdinalIgnoreCase)
                        && !全文.Contains(".NET Framework", StringComparison.OrdinalIgnoreCase);
                    bool 够小 = 字节.Length < 4 * 1024 * 1024;
                    记录("C后端 产物是原生可执行文件、不带 .NET(PE 头 + 没有 mscoree)",
                        是pe && 无net && 够小,
                        $"PE 头 = {是pe};带 .NET 字样 = {!无net};{字节.Length / 1024} KB(期望几百 KB 以内)");
                }
            }
            catch (Exception ex) { 记录异常("C后端 产物是原生可执行文件、不带 .NET", ex); }
        }

        /// <summary>
        /// R4-12:**位图那一族的独立 exe 腿**那一份程序(离屏七条:建 / 画点 / 读点 / 线 / 矩形 / 尺寸 / 释放)。
        /// 十一个读数与 `C后端宿主能力与无头呈现` 那一组给解释器核跑的那段**同一套值**
        /// (画进去的颜色 / 空白格 / 越界给 0 / 宽高 16×8 / 释放后旧号作废)。
        /// **不开窗** —— 理由见调用处那一段注释。
        /// </summary>
        private static string 是位图离屏源() =>
            "整数 图 = 0;\n" +
            "逻辑 建成 = 建位图(16, 8, 图);\n" +
            "如果 (建成) { 输出行(1); } 否则 { 输出行(0); }\n" +
            "画点到图(图, 3, 4, 4294901760);\n" +
            "输出行(取图像素(图, 3, 4));\n" +
            "输出行(取图像素(图, 0, 0));\n" +
            "画线到图(图, 0, 2, 15, 2, 4278255360);\n" +
            "输出行(取图像素(图, 0, 2));\n" +
            "输出行(取图像素(图, 8, 2));\n" +
            "画矩形到图(图, 10, 5, 3, 2, 4278190335);\n" +
            "输出行(取图像素(图, 10, 5));\n" +
            "输出行(取图像素(图, 12, 6));\n" +
            "画点到图(图, 999, 999, 255);\n" +
            "画矩形到图(图, 100, 100, 3, 2, 255);\n" +
            "输出行(取图像素(图, 999, 999));\n" +
            "整数 宽 = 0;\n" +
            "整数 高 = 0;\n" +
            "逻辑 问到了 = 位图尺寸(图, 宽, 高);\n" +
            "输出行(宽);\n" +
            "输出行(高);\n" +
            "逻辑 放了 = 释放位图(图);\n" +
            "逻辑 再用 = 画点到图(图, 1, 1, 255);\n" +
            "如果 (放了 && !再用) { 输出行(1); } 否则 { 输出行(0); }\n" +
            "停止\n";

        /// <summary>函数 / 递归 / 循环 / 乘除余 / 字符串 / 数组都沾一点:对拍用的第二份程序</summary>
        private static string 是函数与递归源() =>
            "方法 fact(整数 n) 返回 整数\n{\n    如果 (n <= 1)\n    {\n        返回 1;\n    }\n    返回 n * fact(n - 1);\n}\n\n" +
            "方法 求和(整数 到) 返回 整数\n{\n    整数 累计 = 0;\n    整数 i = 1;\n    当 (i <= 到)\n    {\n        累计 = 累计 + i;\n        i = i + 1;\n    }\n    返回 累计;\n}\n\n" +
            "整数 i = 0;\n当 (i < 6)\n{\n    输出(fact(i));\n    输出(\" \");\n    i = i + 1;\n}\n输出换行;\n" +
            "输出(求和(100));\n输出换行;\n输出(1000 / 7);\n输出(\" 余 \");\n输出(1000 % 7);\n输出换行;\n" +
            "字符串 名字 = \"中文串abc\";\n输出(名字);\n输出换行;\n" +
            "整数 名单[4] = [10, 20, 30, 40];\n整数 k = 0;\n当 (k < 4)\n{\n    输出(名单[k]);\n    输出(\",\");\n    k = k + 1;\n}\n输出换行;\n停止\n";

        /// <summary>
        /// 同一份 HB 源码跑两遍:**C# 虚拟机收程序输出字节** vs **C 后端编出的原生 exe 收 stdout 字节**,
        /// 两串逐字节比。`期望报错` 非 null 时改判"运行期错误带行号、退出码非 0"(那种程序不该跑出正常输出)。
        /// </summary>
        private static void 跑原生对拍(string 名, string 源码, int 内存, string? 期望输出,
            string? 期望报错 = null, string? 标准输入 = null)
        {
            var 编译结果 = 编译(源码);

            // ① 虚拟机侧:原样收字节(不经过文字解码,才能逐字节比)
            var 机 = new 虚拟机C();
            机.设内存大小(内存);
            if (标准输入 != null)
            {
                var 队列 = new Queue<uint?>(Encoding.UTF8.GetBytes(标准输入).Select(b => (uint?)b));
                机.输入源 = () => 队列.Count > 0 ? 队列.Dequeue() : null;
            }
            var 收 = new MemoryStream();
            机.输出目标 = b => 收.WriteByte(b);
            // ⚠ **这一行不能少**(R4-12 位图那条腿量出来的):原生那一侧宿主能力是**编进去的**,
            //   C# 这一侧不挂 `宿主调用` 就"能力一律给假" —— 症状是位图那一段两边读数
            //   `0 0 0 …` 对 `1 4294901760 …`(本批第一遍实测就是这么红的)。
            //   以前那四条判据不吃宿主能力,所以这一行一直没露头。
            机.宿主调用 = 宿主能力C.处理;
            string 机报错 = "";
            try
            {
                机.装载(编译结果);
                while (机.单步()) { }
            }
            catch (Exception ex) { 机报错 = ex.Message; }
            byte[] 机出 = 收.ToArray();

            // ② C 后端侧:转译 → 编 → 跑
            var 转译 = C后端C.转译(编译结果, 内存);
            string 暂存 = Path.Combine(临时区C.基目录(), "hbcc_C后端自检", Environment.ProcessId.ToString());
            Directory.CreateDirectory(暂存);
            string c源 = Path.Combine(暂存, 干净名(名) + ".c");
            string exe = Path.Combine(暂存, 干净名(名) + ".exe");
            File.WriteAllText(c源, 转译.源码, new UTF8Encoding(false));

            var 编 = C工具链C.编成exe(c源, exe);
            if (!编.成功)
            {
                记录(名, false, "C 编译器没编过:" + 截断(编.日志, 240));
                return;
            }

            var (退出码, 原生出, 原生误) = 跑原生(exe, 标准输入);

            if (期望报错 != null)
            {
                bool 对 = 退出码 != 0
                    && 原生误.Contains(期望报错, StringComparison.Ordinal)
                    && 原生误.Contains("行", StringComparison.Ordinal)
                    && 机报错.Contains(期望报错, StringComparison.Ordinal);
                记录(名, 对, 对
                    ? $"两边都报「{期望报错}」;原生退出码 {退出码},stderr = {截断(原生误, 90)}"
                    : $"原生退出码 {退出码}(期望非 0);原生 stderr = {截断(原生误, 100)};" +
                      $"虚拟机那边 = {截断(机报错, 80)}");
                return;
            }

            bool 逐字节同 = 机出.SequenceEqual(原生出);
            bool 看着对 = 期望输出 == null || Encoding.UTF8.GetString(原生出) == 期望输出;
            记录(名, 逐字节同 && 看着对 && 退出码 == 0, 逐字节同 && 看着对 && 退出码 == 0
                ? $"退出码 0;{原生出.Length} 字节与虚拟机逐字节一致;C 源码 {转译.源码.Length} 字"
                : $"退出码 {退出码};虚拟机 {机出.Length} 字节 / 原生 {原生出.Length} 字节;" +
                  $"虚拟机「{截断(Encoding.UTF8.GetString(机出), 80)}」原生「{截断(Encoding.UTF8.GetString(原生出), 80)}」");
        }

        /// <summary>起原生 exe,拿(退出码, stdout **字节**, stderr 文本);超时/起不来给 (-1 / -2)</summary>
        private static (int 退出码, byte[] 出, string 误) 跑原生(string exe, string? 标准输入)
        {
            var 起 = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = new UTF8Encoding(false),
            };
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
                    if (标准输入 != null)
                        进程.StandardInput.BaseStream.Write(Encoding.UTF8.GetBytes(标准输入));
                    进程.StandardInput.Close();
                }
                catch { }
                if (!进程.WaitForExit(120_000))
                {
                    try { 进程.Kill(entireProcessTree: true); } catch { }
                    return (-1, [], "原生 exe 120 秒没结束(超时)");
                }
                出任务.GetAwaiter().GetResult();
                string 误 = 误任务.GetAwaiter().GetResult();
                return (进程.ExitCode, 出流.ToArray(), 误);
            }
        }

        /// <summary>断言名当文件名使:只留中文/字母/数字,别的换掉(免得路径里有奇怪字符)</summary>
        private static string 干净名(string 名)
        {
            var 出 = new StringBuilder();
            foreach (char 字 in 名)
                出.Append(char.IsLetterOrDigit(字) ? 字 : '_');
            return 出.ToString();
        }
    }
}
