using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R4-8:`编译器CLI`(hbcc)的**主要参数分支**端到端进自检。
    ///
    /// 为什么单开一组:自检一直是**进程内**叫引擎的(`汇编()` / `编译()` 那几条),而 `编译器CLI\Program.cs`
    /// 那 1200 多行参数分支 —— 解析参数、找仓库根、`--run` 的退出码口径 —— **一行都不执行**;
    /// 真正的端到端在 `工具\四组合冒烟.ps1`(套件之外,得有人记得跑)。于是"命令行这一端"只要有人改坏,
    /// 自检照样全绿。这一组把主要参数各起一个**真子进程**跑一遍,判据 = **退出码 + 关键输出**。
    ///
    /// 口径三条:
    ///   · **只跑只读或写了也白写的分支** —— `--stats` **不带** `--更新报告`(`--更新报告` 会重写
    ///     `全面测评报告.md` 与 `接手说明.md`,自检里绝不能碰)、`--清单` **不带** `--索引`(那条会写
    ///     `任务清单.md`)、`--生成帮助` 换到**假仓库根**(临时目录里放两个空目录 `引擎\` 与 `Sample\`)里跑,
    ///     另有一条断言盯着"真站点逐字节没动";
    ///   · **`--selftest` 自己不跑** —— 那是自检自己,起子进程跑它就是套娃;
    ///   · **找不到编译器就逐条记跳过** —— 名字跟真跑那一支**一模一样**(清单按名字比对,换个名字
    ///     在 IDE 里跑一次就红灯)。
    ///
    /// 退出码三档一条不落:0 = 成功、1 = 编译错误 / 跑完没通过、2 = 用法错误。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary>
        /// 起一个**子进程**跑命令行编译器,拿退出码 / 标准输出 / 标准错误。R4-3 与 R4-8 共用这一份。
        ///
        /// 为什么提成公用:R4-3(`乱源深度轰炸`)先要了这套机制(栈溢出进不了 `catch`,判据只能是退出码),
        /// R4-8 的 CLI 端到端要的是同一套东西 —— 超时 / 两个管道都要读干净 / 输出按 UTF-8 解,
        /// 这几处都踩过坑,合成一份只需要修一次。
        ///
        /// 三个约定:
        ///   · **两个管道都要同时读走** —— 只读一个,另一个被写满(管道缓冲区有限)时子进程会卡住;
        ///   · **标准输入一律重定向后立刻关掉** —— 子进程看到的是"空输入 + EOF"。不这么做的话,父进程的
        ///     标准输入被重定向时会被它继承,`--run` 那条路会 `读全部标准输入()` 一直等,自检就成了新的挂死点;
        ///   · **输出按 UTF-8 收** —— hbcc 的 `Main` 一进来就把 `Console.OutputEncoding` 设成 UTF-8(中文报错
        ///     靠它),这边不照 UTF-8 解会按控制台代码页解成乱码。
        ///
        /// 退出码 **-1** = 超时(进程树已杀)、**-2** = 起不来 —— 都不是真退出码,调用方按"不合预期"处理。
        /// </summary>
        private static (int 退出码, string 输出, string 出错) 跑命令行(
            string 编译器, string? 工作目录, int 超时毫秒, params string[] 参数)
            => 跑进程("dotnet", 工作目录, 超时毫秒, new[] { 编译器 }.Concat(参数));

        /// <summary>
        /// 起一个**子进程**跑一个**真产物**(独立 exe)或任意程序;约定与 `跑命令行` 一模一样
        /// (两个管道同时读走 / 标准输入立刻关掉 / 输出按 UTF-8 收 / 超时给 -1 / 起不来给 -2)。
        ///
        /// 为什么单开这一支:独立 exe 那条腿(贴了尾巴的 `HB宿主`)得**真起一次那份 exe**才量得出结果 ——
        /// 白名单是宿主在"跑程序之前"注入的,进程内叫引擎量不到"独立 exe 里到底给什么"(D6)。
        /// </summary>
        private static (int 退出码, string 输出, string 出错) 跑进程(
            string 程序, string? 工作目录, int 超时毫秒, IEnumerable<string> 参数)
        {
            var 起 = new System.Diagnostics.ProcessStartInfo(程序)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            if (工作目录 != null) 起.WorkingDirectory = 工作目录;
            foreach (string 参 in 参数) 起.ArgumentList.Add(参);

            System.Diagnostics.Process? 进程;
            try { 进程 = System.Diagnostics.Process.Start(起); }
            catch (Exception 起不来) { return (-2, "", $"起不了子进程: {起不来.Message}"); }
            if (进程 == null) return (-2, "", "子进程起不来");

            using (进程)
            {
                var 出错任务 = 进程.StandardError.ReadToEndAsync();
                var 输出任务 = 进程.StandardOutput.ReadToEndAsync();
                try { 进程.StandardInput.Close(); } catch { }        // 空输入 + EOF(见类注释)
                if (!进程.WaitForExit(超时毫秒))
                {
                    try { 进程.Kill(entireProcessTree: true); } catch { }
                    return (-1, "", $"子进程 {超时毫秒 / 1000} 秒没结束(超时)");
                }
                string 输出 = 输出任务.GetAwaiter().GetResult();
                string 出错 = 出错任务.GetAwaiter().GetResult();
                return (进程.ExitCode, 输出, 出错);
            }
        }

        /// <summary> 仓库根 = 有 `引擎\` 与 `中间层\` 的那一层(与 `Program.cs` 的 `找仓库根` 同一口径);找不到给 null </summary>
        private static string? 找仓库根目录()
        {
            string? 示例 = 找示例目录();
            if (示例 == null) return null;
            string? 根 = Directory.GetParent(示例)?.FullName;
            return 根 != null && Directory.Exists(Path.Combine(根, "引擎")) ? 根 : null;
        }

        /// <summary> 整行相等(去掉行尾 `\r`,再掐掉两头空白)—— 用来认"程序自己打出来的那一行" </summary>
        private static bool 有这一行(string 文本, string 行) =>
            文本.Replace("\r\n", "\n").Split('\n').Any(x => x.Trim() == 行);

        /// <summary>
        /// H65:从子进程输出里挑出**那一行运行期报错**(`运行错误: …`);没有就给空串。
        /// 命令行把运行期报错写进**标准错误**,而调用方一般把两股并起来看 —— 所以这里不区分来源。
        /// </summary>
        private static string 取运行错误行(string 文本) =>
            文本.Replace("\r\n", "\n").Split('\n')
                .Select(x => x.Trim())
                .FirstOrDefault(x => x.StartsWith("运行错误:", StringComparison.Ordinal)) ?? "";

        /// <summary> 详情里截一段(整篇子进程输出只留前 N 字,免得把一份展开文本灌进报告) </summary>
        private static string 截断(string 文本, int 上限)
        {
            string 一行 = 文本.Replace("\r", " ").Replace("\n", " ").Trim();
            return 一行.Length <= 上限 ? 一行 : 一行[..上限] + $"…(共 {一行.Length} 字)";
        }

        /// <summary>只挑几行关键输出(报告里够看,不用把整篇子进程输出灌进去) </summary>
        private static string 关键行(string 文本, params string[] 要找)
        {
            var 行 = 文本.Replace("\r", "").Split('\n')
                .Where(x => 要找.Any(词 => x.Contains(词, StringComparison.Ordinal)))
                .Select(x => x.Trim()).ToArray();
            return 行.Length == 0 ? "(关键行一行都没有)" : 截断(string.Join(" | ", 行), 220);
        }

        /// <summary>
        /// 把每一行尾巴上的**源行标记**(`// 来自[文件K]第 N 行`)剪掉,别的注释原样留着(H42 / H43)。
        ///
        /// 口径直接复用唯一那份解析 `源行标记C.去标记`(它连标记前头那个光秃秃的 `//` 一起收干净),
        /// 只对**真带 `来自…` 标记**的行调它 —— `// 编译器生成` 那几行不动:
        /// 不带 `--asm-marked` 时也有那句,动了就比不上了。
        /// </summary>
        private static string 剪掉源行标记(string 文本) =>
            string.Join("\n", 文本.Replace("\r\n", "\n").Split('\n')
                .Select(行 => 源行标记C.读(行).有 ? 源行标记C.去标记(行) : 行));

        private static void CLI端到端自检()
        {
            const int 超时 = 120_000;
            string? 编译器 = 找命令行编译器();
            string? 根 = 找仓库根目录();
            // 临时目录带上**进程号**:两台 / 两次自检同时在跑时各占一份,谁也不删谁的
            // (实测踩过:一次自检收尾把临时目录整个删掉,正好把同时在跑的那一次刚交出来的产物删成 0 字节)
            string 暂存 = Path.Combine(临时区C.基目录(), "hbcc_CLI端到端", Environment.ProcessId.ToString());
            string 过文件 = Path.Combine(暂存, "过.hb");
            string 不过文件 = Path.Combine(暂存, "不过.hb");
            string 坏文件 = Path.Combine(暂存, "坏.hb");
            string 假根 = Path.Combine(暂存, "假仓库");

            // 探针:① 编过 + 断言全过(还打一行 `7` 当"程序输出真的到标准输出了"的凭据);
            //       ② 断言不成立(跑完会写错误槽 ⇒ 退出码必须非 0);③ 编译错误(退出码 1)。
            const string 过源 = "测试 边界\n{\n    断言(1 + 1 == 2);\n}\n测试输出(7);\n停止\n";
            const string 不过源 = "测试 边界\n{\n    断言(1 == 2);\n}\n停止\n";
            const string 坏源 = "整数 初值 = ;\n停止\n";
            // D6 探针:同一条程序,工程那条路(白名单)在 `进程启动` 一组里已经钉着"给真";
            // 这里量的是**独立 exe**(没有工程文件)那个口径 —— 两个能力一律 假、状态码 0xFFFFFFFF。
            const string 白名单源 =
                "字符串 输出;\n" +
                "逻辑 起 = 运行进程(\"cmd\", \"/c\\necho 你好\", 输出, 5000);\n" +
                "如果 (起) {\n输出行(\"运行进程=真\");\n输出行(输出);\n} 否则 {\n输出行(\"运行进程=假\");\n}\n" +
                "测试输出(取退出码());\n" +
                "字符串 回应;\n" +
                "逻辑 联 = 取网址(\"http://127.0.0.1:8080/\", 回应, 4096, 1500);\n" +
                "如果 (联) {\n输出行(\"取网址=真\");\n} 否则 {\n输出行(\"取网址=假\");\n}\n" +
                "测试输出(取网址状态码());\n停止\n";

            // H43 探针:一个**跨文件引用**的两文件工程(主文件调工具文件里的函数、写工具文件里的全局变量)——
            // 工程级 `--asm` 现在走**工程级展开**(一份整篇,与真正编译同源),跨文件的名字看得见;
            // 第 245 批那版逐文件独立展开在这儿会报"未定义的函数 / 未知名字"。
            const string 标记主源 = "整数 数 = 1 + 2;\n整数 和 = 平方和(数, 数);\n总数 = 和;\n输出行(\"数加和\");\n停止\n";
            const string 标记工具源 = "整数 总数 = 0;\n方法 平方和(整数 左, 整数 右) 返回 整数\n{\n    返回 左 * 左 + 右 * 右;\n}\n";
            const string 标记工程源 =
                "{\n  \"名称\": \"标记探针\",\n  \"启动文件\": \"标记_主.hb\",\n  \"文件\": [\n    \"标记_主.hb\",\n    \"标记_工具.hb\"\n  ],\n" +
                "  \"资源\": [],\n  \"子工程\": [],\n  \"说明\": \"H43 探针:工程级展开。\"\n}\n";
            string 标记工程路径 = Path.Combine(暂存, "标记.hbp");

            // H65 探针:① 两文件工程 —— 除零在**第二份** `h65_工具.hb` 的第 3 行;
            //             ② 单文件版 —— 同样的除零,但只有一份文件 ⇒ 照旧只报 `第 3 行`(不硬加文件名)。
            const string h65主源 = "输出行(\"开始\");\n工具_除(0);\n停止\n";
            const string h65工具源 = "方法 工具_除(整数 a)\n{\n    整数 值 = 10 / a;\n    输出行(\"到这儿了\");\n}\n";
            const string h65工程源 =
                "{\n  \"名称\": \"h65探针\",\n  \"启动文件\": \"h65_主.hb\",\n  \"文件\": [\n    \"h65_主.hb\",\n    \"h65_工具.hb\"\n  ],\n" +
                "  \"资源\": [],\n  \"子工程\": [],\n  \"说明\": \"H65 探针:除零在第二份文件的第 3 行。\"\n}\n";
            const string h65单文件源 = "输出行(\"开始\");\n整数 除数 = 0;\n整数 值 = 10 / 除数;\n停止\n";
            string h65单文件 = Path.Combine(暂存, "h65_单.hb");

            bool 备好了 = false;
            try
            {
                Directory.CreateDirectory(暂存);
                File.WriteAllText(过文件, 过源, new UTF8Encoding(false));
                File.WriteAllText(不过文件, 不过源, new UTF8Encoding(false));
                File.WriteAllText(坏文件, 坏源, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(暂存, "标记_主.hb"), 标记主源, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(暂存, "标记_工具.hb"), 标记工具源, new UTF8Encoding(false));
                File.WriteAllText(标记工程路径, 标记工程源, new UTF8Encoding(false));
                File.WriteAllText(h65单文件, h65单文件源, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(暂存, "h65_主.hb"), h65主源, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(暂存, "h65_工具.hb"), h65工具源, new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(暂存, "h65.hbp"), h65工程源, new UTF8Encoding(false));
                Directory.CreateDirectory(Path.Combine(假根, "引擎"));     // 找仓库根 认这两个目录
                Directory.CreateDirectory(Path.Combine(假根, "Sample"));
                备好了 = true;
            }
            catch (Exception ex) { 记录异常("CLI 端到端 探针与假仓库根", ex); }

            var 用例 = new List<(string 名, Func<(bool? 通过, string 详情)> 跑)>
            {
                ("CLI 端到端 --help:退出码 0,用法里写着三档退出码口径", () =>
                {
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "--help");
                    bool 过 = 码 == 0
                        && 出.Contains("退出码: 0=成功 1=", StringComparison.Ordinal)
                        && 出.Contains("2=用法错误", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);{截断(出, 100)}");
                }),

                ("CLI 端到端 未知选项 → 退出码 2(用法错误那条路)", () =>
                {
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "--这项不存在");
                    bool 过 = 码 == 2 && (误 + 出).Contains("用法错误", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 2);{截断(误 + 出, 100)}");
                }),

                ("CLI 端到端 --run:编过跑完 → 退出码 0,程序输出与断言汇总都对", () =>
                {
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "--run", 过文件);
                    bool 过 = 码 == 0
                        && 有这一行(出, "7")
                        && 出.Contains("测试 边界:共 1 条断言,失败 0 条", StringComparison.Ordinal)
                        && !出.Contains("运行停下", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);输出 {截断(出, 120)}" +
                        (误.Length > 0 ? $";出错 {截断(误, 60)}" : ""));
                }),

                ("CLI 端到端 --run:断言不成立 → 退出码 1,且说清是哪一块没过", () =>
                {
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "--run", 不过文件);
                    bool 过 = 码 == 1
                        && 出.Contains("测试 边界:共 1 条断言,失败 1 条", StringComparison.Ordinal)
                        && 出.Contains("运行停下", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 1);输出 {截断(出, 120)}");
                }),

                ("CLI 端到端 --asm:编过 → 退出码 0,展开文本与机器码都出来了", () =>
                {
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "--asm", 过文件);
                    bool 过 = 码 == 0
                        && 出.Contains("展开后", StringComparison.Ordinal)
                        && 出.Contains("机器码 (hex)", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);输出 {出.Length} 字,含展开文本与机器码");
                }),

                ("CLI 端到端 --asm:编不过 → 退出码 1,报错带行号且不是内部错误", () =>
                {
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "--asm", 坏文件);
                    string 全部 = (误 + 出).Replace(" ", "").Replace("\t", "");
                    bool 过 = 码 == 1
                        && 全部.Contains("编译错误", StringComparison.Ordinal)
                        && 全部.Contains("第1行", StringComparison.Ordinal)
                        && !全部.Contains("内部错误", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 1);{截断(误, 120)}");
                }),

                ("CLI 端到端 --stats:退出码 0,规模表打出来了且没写报告(只读那条路)", () =>
                {
                    if (根 == null) return (null, "找不到仓库根(有 引擎 与 中间层 的那一层)");
                    var (码, 出, 误) = 跑命令行(编译器!, 根, 超时, "--stats");
                    bool 过 = 码 == 0
                        && 出.Contains("| 指令集 |", StringComparison.Ordinal)
                        && 出.Contains("| 标准工程示例 |", StringComparison.Ordinal)
                        && !出.Contains("已更新", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);表格 {出.Length} 字" +
                        (误.Length > 0 ? $";出错 {截断(误, 60)}" : ""));
                }),

                ("CLI 端到端 --清单:退出码 0(任务清单结构全过)", () =>
                {
                    if (根 == null) return (null, "找不到仓库根(有 引擎 与 中间层 的那一层)");
                    var (码, 出, 误) = 跑命令行(编译器!, 根, 超时, "--清单");
                    bool 过 = 码 == 0 && 出.Contains("清单结构:全过", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);{截断(出, 100)}" +
                        (误.Length > 0 ? $";出错 {截断(误, 60)}" : ""));
                }),

                ("CLI 端到端 --清单 --查:退出码 0,查得到条目", () =>
                {
                    if (根 == null) return (null, "找不到仓库根(有 引擎 与 中间层 的那一层)");
                    var (码, 出, 误) = 跑命令行(编译器!, 根, 超时, "--清单", "--查", "自检");
                    bool 过 = 码 == 0
                        && 出.Contains("共 ", StringComparison.Ordinal)
                        && !出.Contains("清单里没有匹配", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);{截断(出, 100)}");
                }),

                ("CLI 端到端 --清单 --查:批号带 `批` 前缀与裸数字给同一批", () =>
                {
                    if (根 == null) return (null, "找不到仓库根(有 引擎 与 中间层 的那一层)");
                    var (码1, 出1, 误1) = 跑命令行(编译器!, 根, 超时, "--清单", "--查", "批428");
                    var (码2, 出2, _) = 跑命令行(编译器!, 根, 超时, "--清单", "--查", "428");
                    bool 过 = 码1 == 0 && 码2 == 0 && 出1 == 出2 && 出1.Contains("第428节", StringComparison.Ordinal);
                    return (过, $"`批428` 退出码 {码1} / `428` 退出码 {码2};两次输出" +
                        $"{(出1 == 出2 ? "逐字节相同" : "不同")};{截断(出1, 80)}" +
                        (误1.Length > 0 ? $";出错 {截断(误1, 60)}" : ""));
                }),

                ("CLI 端到端 --生成帮助:在假仓库根里写出站点,退出码 0", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    var (码, 出, 误) = 跑命令行(编译器!, 假根, 超时, "--生成帮助");
                    string 站点 = Path.Combine(假根, "帮助", "index.html");
                    string 网页 = File.Exists(站点) ? File.ReadAllText(站点) : "";
                    bool 过 = 码 == 0
                        && 出.Contains("已生成", StringComparison.Ordinal)
                        && 网页.Contains("<nav", StringComparison.Ordinal)
                        && 网页.Contains("<main", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);假站点 {网页.Length} 字;{截断(误, 80)}");
                }),

                ("CLI 端到端 --生成帮助 只写工作目录:真站点逐字节没动", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    if (根 == null) return (null, "找不到仓库根(有 引擎 与 中间层 的那一层)");
                    string 站点 = Path.Combine(根, "帮助", "index.html");
                    if (!File.Exists(站点)) return (null, $"盘上还没有 {站点}(还没生成过站点)");
                    byte[] 前 = File.ReadAllBytes(站点);
                    var (码, _, 误) = 跑命令行(编译器!, 假根, 超时, "--生成帮助");
                    byte[] 后 = File.ReadAllBytes(站点);
                    bool 一样 = 前.Length == 后.Length && 前.SequenceEqual(后);
                    return (码 == 0 && 一样, $"退出码 {码}(期望 0);真站点 {前.Length} 字节 → " +
                        (一样 ? "逐字节没动" : "被改了") + $";{截断(误, 60)}");
                }),

                ("CLI 端到端 --乱源 20:退出码 0(乱源与深度维 8 格都合预期)", () =>
                {
                    var (码, 出, 误) = 跑命令行(编译器!, 根, 超时, "--乱源", "20");
                    bool 过 = 码 == 0
                        && 出.Contains("乱源轰炸:20 例", StringComparison.Ordinal)
                        && 出.Contains("不合预期 0 格", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);{截断(出 + 误, 120)}");
                }),

                ("CLI 端到端 --乱源 缺例数 → 退出码 2(用法错误)", () =>
                {
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "--乱源");
                    bool 过 = 码 == 2
                        && (误 + 出).Contains("--乱源 后面要跟一个正整数", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 2);{截断(误 + 出, 100)}");
                }),

                // ── R3-6-4 批 4:三档打包方式的命令行端到端(第248批 H41 起是三条) ──
                // `只编译` 这一档**故意挑它**:它一个字节都不碰 C 编译器与宿主,
                // 于是"没有 C 工具链的机器"也能真跑通(其余两档的真编真跑在 C后端 各组里)。
                ("CLI 端到端 --打包 乱写 → 退出码 2,用法里把三档列出来", () =>
                {
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "过.hb", "--exe", "x.exe", "--打包", "乱写");
                    string 全部 = 误 + 出;
                    bool 过 = 码 == 2
                        && 全部.Contains("--打包 不认这一档", StringComparison.Ordinal)
                        && 打包规格C.全部.All(种 => 全部.Contains(种.键, StringComparison.Ordinal));
                    return (过, $"退出码 {码}(期望 2);{截断(全部, 140)}");
                }),

                ("CLI 端到端 --打包 只编译 + --方式 C:交出 C 源码、退出码 0", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    // "只编译"这档交的是源码,**走哪条腿看 `--方式 C`**(没写就是宿主那条 ⇒ 交 `.hb`)。
                    // 这条专钉"走 C 那条腿":交 C 源码、且**不叫 C 编译器**(一个 exe 都不产)。
                    string 目标 = Path.Combine(暂存, "只编译_原生.c");
                    if (File.Exists(目标)) File.Delete(目标);
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时,
                        "过.hb", "--exe", 目标, "--方式", "C", "--打包", "source_only");
                    string 源码 = File.Exists(目标) ? File.ReadAllText(目标) : "";
                    bool 过 = 码 == 0
                        && 源码.Contains("hb_boot", StringComparison.Ordinal)
                        && 源码.Contains("hb_stop", StringComparison.Ordinal)
                        && !源码.StartsWith("HB", StringComparison.Ordinal)
                        && 出.Contains("已生成 C 源码", StringComparison.Ordinal)
                        && !出.Contains("已生成原生程序", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);C 源码 {源码.Length} 字(要找得到 hb_boot / hb_stop);" +
                        (File.Exists(Path.Combine(暂存, "只编译_原生.exe")) ? "**还多出个 exe**" : "没多出 exe") +
                        $";目录里:{string.Join(",", Directory.GetFiles(暂存).Select(Path.GetFileName))}" +
                        $";关键:{关键行(出 + 误, "只编译:", "已生成")}");
                }),

                ("CLI 端到端 --打包 只编译(宿主那条):交出 .hb(4 字节头 + 机器码)、退出码 0", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    // 用户写的名字跟"真正交出来的东西"不一致时:后缀听**规格**的(这条走宿主那条腿 ⇒ `.hb`)
                    string 目标 = Path.Combine(暂存, "只编译_宿主.exe");
                    if (File.Exists(目标)) File.Delete(目标);
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时,
                        "过.hb", "--exe", 目标, "--打包", "source_only");
                    // 交出来的是 `.hb`(后缀听规格的),不是用户写的那串 `.exe`
                    string 真目标 = Path.ChangeExtension(目标, ".hb");
                    byte[] 字节 = File.Exists(真目标) ? File.ReadAllBytes(真目标) : [];
                    bool 过 = 码 == 0
                        && 字节.Length > 8
                        && 字节[0] == 0x48 && 字节[1] == 0x42 && 字节[2] == 0x01 && 字节[3] == 0x00
                        && 出.Contains("已生成机器码文件", StringComparison.Ordinal);
                    return (过, $"退出码 {码}(期望 0);{Path.GetFileName(真目标)} 存在={File.Exists(真目标)} " +
                        $"{字节.Length} 字节,头 {string.Join(" ", 字节.Take(4).Select(b => b.ToString("X2")))};" +
                        $"目录里:{string.Join(",", Directory.GetFiles(暂存).Select(Path.GetFileName))};" +
                        $"关键:{关键行(出 + 误, "只编译:", "已生成")}");
                }),

                // ── D6:独立 exe 带不了 `可执行文件` / `网址` 白名单(真编真跑一份 exe,不只看源码) ──
                // 量过的那份口径(2026-09-25,`--打包 host_self`):同一条程序在独立 exe 里
                // `运行进程` / `取网址` 一律 假、状态码 `0xFFFFFFFF`(打印成 4294967295),
                // **退出码 0、不崩、不写错误槽**。工程那条路(有白名单)给真由 `进程启动` 一组钉着。
                // (第248批 H41 之前这里用的是 `host_fx`「轻量·需装 .NET」那一档,它整档撤了。)
                ("CLI 端到端 独立 exe 带不了白名单:运行进程 / 取网址 给假 + 状态码 0xFFFFFFFF + 退出码 0", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    string 源文件 = Path.Combine(暂存, "白名单.hb");
                    string 目标 = Path.Combine(暂存, "白名单.exe");
                    File.WriteAllText(源文件, 白名单源, new UTF8Encoding(false));
                    if (File.Exists(目标)) File.Delete(目标);
                    var (编码, 编出, 编误) = 跑命令行(编译器!, 暂存, 超时,
                        源文件, "--exe", 目标, "--打包", "host_self");
                    if (编码 != 0 || !File.Exists(目标))
                    {
                        // 编不出 exe **分两种**:这一台没有宿主(正常,记跳过)与真编坏了(红灯)
                        bool 缺宿主 = (编误 + 编出).Contains("找不到 HB 宿主 exe", StringComparison.Ordinal);
                        return 缺宿主
                            ? (null, "这一台没有 HB 宿主 exe(先 dotnet build HB宿主\\HB宿主.csproj)")
                            : (false, $"编独立 exe 失败:退出码 {编码};{截断(编误 + 编出, 200)}");
                    }
                    var (跑码, 跑出, 跑误) = 跑进程(目标, 暂存, 60_000, Array.Empty<string>());
                    string 净 = 跑出.Replace("\r\n", "\n").Trim();
                    bool 过 = 跑码 == 0
                        && 净 == "运行进程=假\n4294967295\n取网址=假\n4294967295"
                        && !跑误.Contains("运行停下", StringComparison.Ordinal);
                    return (过, $"退出码 {跑码}(期望 0);输出 {截断(净.Replace("\n", "|"), 130)}" +
                        (跑误.Length > 0 ? $";出错 {截断(跑误, 60)}" : ""));
                }),

                // ── H7:独立 exe 的运行期报错**也带行号**(包尾巴挂了一张"地址 → 源码位置"表) ──
                // 真编真跑一份 exe:第 3 行是 `返回 10 / a;`,a = 0 ⇒ 除零;老包(没有源行表)只报说明。
                ("CLI 端到端 独立 exe 的运行期报错带行号(H7:包尾巴那张「地址 → 源码位置」表)", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    string 源文件 = Path.Combine(暂存, "除零.hb");
                    string 目标 = Path.Combine(暂存, "除零.exe");
                    File.WriteAllText(源文件,
                        "方法 算(整数 a) 返回 整数\n{\n    返回 10 / a;\n}\n测试输出(算(0));\n停止\n", new UTF8Encoding(false));
                    if (File.Exists(目标)) File.Delete(目标);
                    var (编码, 编出, 编误) = 跑命令行(编译器!, 暂存, 超时, 源文件, "--exe", 目标, "--打包", "host_self");
                    if (编码 != 0 || !File.Exists(目标))
                    {
                        bool 缺宿主 = (编误 + 编出).Contains("找不到 HB 宿主 exe", StringComparison.Ordinal);
                        return 缺宿主
                            ? (null, "这一台没有 HB 宿主 exe(先发布 HB宿主)")
                            : (false, $"编独立 exe 失败:退出码 {编码};{截断(编误 + 编出, 200)}");
                    }
                    var (跑码, 跑出, 跑误) = 跑进程(目标, 暂存, 60_000, Array.Empty<string>());
                    string 全部 = (跑出 + 跑误).Replace("\r\n", "\n");
                    // 期望:`⚠ 运行停下:第 3 行:除零…`(位置串由包里的源行表给)
                    bool 过 = 跑码 != 0 && 全部.Contains("运行停下", StringComparison.Ordinal)
                        && 全部.Contains("第 3 行", StringComparison.Ordinal)
                        && 全部.Contains("除零", StringComparison.Ordinal);
                    return (过, $"退出码 {跑码};含『第 3 行』={全部.Contains("第 3 行", StringComparison.Ordinal)};" +
                        $"含『除零』={全部.Contains("除零", StringComparison.Ordinal)};{截断(全部.Trim().Replace("\n", "|"), 130)}");
                }),

                // ── H7 补钉(第303批):**手写汇编(`.hba`)那一份也带它自己的文件 + 行号** ──
                // 帮助《上手》第15节原来写"手写汇编天生没有源码行号可报" —— 实测相反:混编时 `.hba` 本来就是一份真源码,
                // 那张"地址 → 源码位置"表里同样有它的段(展开材料里它带着 `文件K` 标记,见 `--asm-marked`)。
                // 这一条按**真编真跑**钉,不按源码文本钉。
                ("CLI 端到端 独立 exe 里手写汇编(.hba)报的是它自己的文件 + 行号(H7 补钉)", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    string 目录 = Path.Combine(暂存, "手写汇编");
                    string 手写主程序 = Path.Combine(目录, "主程序.hb");
                    string 手写汇编源 = Path.Combine(目录, "热路径.hba");
                    string 手写工程 = Path.Combine(目录, "手写汇编.hbp");
                    string 手写目标 = Path.Combine(目录, "手写汇编.exe");
                    Directory.CreateDirectory(目录);
                    File.WriteAllText(手写主程序, "输出行(\"开始\");\n调用 热路径_打标记\n停止\n", new UTF8Encoding(false));
                    // 第 8 行 = `除法 R7, R6`(除数 0)⇒ 期望报 `热路径.hba 第 8 行`
                    File.WriteAllText(手写汇编源,
                        "// 探针:第 8 行故意除零。\n" +
                        "热路径_打标记:\n" +
                        "    入栈 R5\n" +
                        "    加载立即数 R5, 60\n" +
                        "    输出字符 R5\n" +
                        "    加载立即数 R6, 0\n" +
                        "    加载立即数 R7, 7\n" +
                        "    除法 R7, R6\n" +
                        "    出栈 R5\n" +
                        "    返回\n", new UTF8Encoding(false));
                    File.WriteAllText(手写工程,
                        "{\n  \"名称\": \"手写汇编探针\",\n  \"启动文件\": \"主程序.hb\",\n  \"文件\": [\"主程序.hb\"],\n" +
                        "  \"汇编\": [\"热路径.hba\"],\n  \"资源\": []\n}\n", new UTF8Encoding(false));
                    if (File.Exists(手写目标)) File.Delete(手写目标);
                    var (编手码, 编手出, 编手误) = 跑命令行(编译器!, 目录, 超时,
                        "-p", "手写汇编.hbp", "--exe", "手写汇编.exe", "--打包", "host_self");
                    if (编手码 != 0 || !File.Exists(手写目标))
                    {
                        bool 缺宿主 = (编手误 + 编手出).Contains("找不到 HB 宿主 exe", StringComparison.Ordinal);
                        return 缺宿主
                            ? (null, "这一台没有 HB 宿主 exe(先发布 HB宿主)")
                            : (false, $"编独立 exe 失败:退出码 {编手码};{截断(编手误 + 编手出, 200)}");
                    }
                    var (跑手码, 跑手出, 跑手误) = 跑进程(手写目标, 目录, 60_000, Array.Empty<string>());
                    string 手全部 = (跑手出 + 跑手误).Replace("\r\n", "\n");
                    bool 带文件名 = 手全部.Contains("热路径.hba 第 8 行", StringComparison.Ordinal);
                    bool 手过 = 跑手码 != 0 && 手全部.Contains("运行停下", StringComparison.Ordinal)
                        && 带文件名 && 手全部.Contains("除零", StringComparison.Ordinal);
                    return (手过, $"退出码 {跑手码};含『热路径.hba 第 8 行』={带文件名};" +
                        $"含『除零』={手全部.Contains("除零", StringComparison.Ordinal)};{截断(手全部.Trim().Replace("\n", "|"), 130)}");
                }),

                ("CLI 端到端 帮助《上手》第15节写明手写汇编(.hba)那一份也带自己的文件 + 行号(H7 补钉)", () =>
                {
                    string 帮助 = 帮助文本C.快速上手();
                    bool 写了新口径 = 帮助.Contains("手写汇编(`.hba`)那一份同样带它自己的文件 + 行号", StringComparison.Ordinal);
                    bool 老错话没了 = !帮助.Contains("天生没有源码行号", StringComparison.Ordinal);
                    return (写了新口径 && 老错话没了, $"新口径写了={写了新口径};那句错话不在了={老错话没了}");
                }),

                // ── H7:包格式这一档也钉住(源行表编得进、解得出来、查得对) ──
                ("CLI 端到端 源行表:编进包 → 解出来 → 按地址查得到行号与位置(H7)", () =>
                {
                    var 表 = new 源行表C();
                    表.文件们.Add("主.hb");
                    表.段们.Add(new 源行表C.段C(0, 3, 7, 0));      // 地址 0/4/8 都是第 7 行
                    表.段们.Add(new 源行表C.段C(12, 1, 9, 0));     // 地址 12 是第 9 行
                    byte[] 区 = 程序包C.编源行区(表);
                    var 回 = 程序包C.解源行区(区, 0, 区.Length);
                    bool 过 = 区.Length > 0 && 回.段们.Count == 2
                        && 回.查行号(0) == 7 && 回.查行号(8) == 7 && 回.查行号(12) == 9 && 回.查行号(16) == 0
                        && 回.查位置(0) == "第 7 行"
                        && 程序包C.编源行区(new 源行表C()).Length == 0
                        && 程序包C.解源行区(new byte[4], 0, 4).空;
                    return (过, $"区 {区.Length} 字节;段 {回.段们.Count} 条;查 0→{回.查行号(0)} / 12→{回.查行号(12)} / 16→{回.查行号(16)}"); 
                }),

                // ── H65:多文件程序的运行期报错,**进程内那条腿**(命令行 `--run`)也带文件名 ──
                // 除零故意放在**第二份** `h65_工具.hb` 的第 3 行:只报「第 3 行」时分不出是哪一份。
                ("CLI 端到端 --run 两文件程序报「h65_工具.hb 第 3 行」(H65:进程内那条腿也带文件名)", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "-p", "h65.hbp", "--run");
                    string 全部 = (出 + 误).Replace("\r\n", "\n");
                    bool 带名 = 取运行错误行(全部) == "运行错误: h65_工具.hb 第 3 行:除零(除数是 0 —— 先判断除数再除)";
                    return (码 == 1 && 带名,
                        $"退出码 {码}(期望 1);那一行 = 『{取运行错误行(全部)}』;{关键行(全部, "运行错误", "运行停下")}");
                }),

                ("CLI 端到端 --run 单文件程序照旧只报「第 3 行」、不硬加文件名(H65)", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "--run", h65单文件);
                    string 全部 = (出 + 误).Replace("\r\n", "\n");
                    string 那一行 = 取运行错误行(全部);
                    bool 只行号 = 那一行 == "运行错误: 第 3 行:除零(除数是 0 —— 先判断除数再除)";
                    bool 没硬加 = !全部.Contains("h65_单.hb 第", StringComparison.Ordinal);
                    return (码 == 1 && 只行号 && 没硬加,
                        $"退出码 {码}(期望 1);那一行 = 『{那一行}』;硬加了文件名={!没硬加}");
                }),

                // 跨进程那条腿早就带文件名(子进程按包里的源行表查)—— 这一条钉的是**两条腿逐字一致**,
                // 以后谁改了一边、另一边没跟,当场红灯。
                ("CLI 端到端 --run 与 --跨进程 报的运行期错误逐字一致(H65:三条腿一副面孔)", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    var (内码, 内出, 内误) = 跑命令行(编译器!, 暂存, 超时, "-p", "h65.hbp", "--run");
                    var (跨码, 跨出, 跨误) = 跑命令行(编译器!, 暂存, 超时, "-p", "h65.hbp", "--run", "--跨进程");
                    string 内行 = 取运行错误行((内出 + 内误).Replace("\r\n", "\n"));
                    string 跨行 = 取运行错误行((跨出 + 跨误).Replace("\r\n", "\n"));
                    return (内码 == 1 && 跨码 == 1 && 内行.Length > 0 && 内行 == 跨行,
                        $"进程内退出码 {内码} / 跨进程 {跨码};进程内『{内行}』;跨进程『{跨行}』");
                }),

                // `查位置` 是那两条腿共用的那个口子 —— 单文件不啰嗦文件名这条判据只写在一处,这儿直接喂表量。
                ("CLI 端到端 执行行映射C.查位置:多文件带文件名 / 单文件只报行号 / 查不到给空串(H65)", () =>
                {
                    var 单 = new List<文件映射C> { new() { 文件 = @"C:\x\只一个.hb" } };
                    单[0].地址到源行[4] = 7;
                    var 双 = new List<文件映射C> { new() { 文件 = @"C:\x\主.hb" }, new() { 文件 = @"C:\x\工具.hb" } };
                    双[0].地址到源行[0] = 2;
                    双[1].地址到源行[4] = 3;
                    string 单文 = 执行行映射C.查位置(单, 4);
                    string 双文 = 执行行映射C.查位置(双, 4);
                    string 查不到 = 执行行映射C.查位置(双, 8);
                    bool 过 = 单文 == "第 7 行" && 双文 == "工具.hb 第 3 行" && 查不到.Length == 0
                        && 执行行映射C.查位置(null, 4).Length == 0;
                    return (过, $"单文件 = 『{单文}』(期望 第 7 行);多文件 = 『{双文}』(期望 工具.hb 第 3 行);查不到 = 『{查不到}』(期望空)");
                }),

                ("CLI 端到端 帮助写明多文件程序的运行期报错带文件名(H65)", () =>
                {
                    string 速查 = 帮助文本C.中间层速查();
                    bool 写了 = 速查.Contains("**多文件程序**那句前面多一个文件名", StringComparison.Ordinal)
                        && 速查.Contains("三条腿同一副面孔", StringComparison.Ordinal);
                    return (写了, $"帮助【中间层速查】写了多文件带文件名={写了}");
                }),

                ("CLI 端到端 独立 exe 的白名单口径写进帮助第14节与第15节(不只是 CLI 报错里写着)", () =>
                {
                    string 帮助 = 帮助文本C.快速上手();
                    bool 十四 = 帮助.Contains("`hbcc --exe` 生成的**独立 exe** 也是这种", StringComparison.Ordinal);
                    bool 十五 = 帮助.Contains("**独立 exe 里没有 `运行进程` / `取网址`**", StringComparison.Ordinal);
                    return (十四 && 十五, $"《十四》点名独立 exe={十四};第15节三档共有口径写了这一条={十五}");
                }),

                // ── H43:工程那条路以前是**逐文件独立展开**(每个文件各 new 一个展开器)⇒ 跨文件引用的工程
                // 在命令行看展开会报"未定义的函数 / 未知名字";现在改用**工程级展开**(与真正编译同源) ──
                ("CLI 端到端 -p 工程 --asm:一份整篇工程级展开、跨文件引用编得过(H43)", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "-p", "标记.hbp", "--asm");
                    var 行 = 出.Replace("\r\n", "\n").Split('\n');
                    int 段数 = 行.Count(x => x.Contains("展开后", StringComparison.Ordinal));
                    bool 跨文件名在 = 出.Contains("平方和", StringComparison.Ordinal);
                    bool 过 = 码 == 0 && 段数 == 1 && 跨文件名在;
                    return (过, $"退出码 {码}(期望 0);展开段数 {段数}(期望 1);跨文件函数名 `平方和` 在展开里={跨文件名在};{截断(误, 60)}");
                }),

                ("CLI 端到端 -p 工程 --asm --asm-marked:标记带 `文件K`、两个文件都点到(H43)", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    var (码, 出, 误) = 跑命令行(编译器!, 暂存, 超时, "-p", "标记.hbp", "--asm", "--asm-marked");
                    var 行 = 出.Replace("\r\n", "\n").Split('\n');
                    int 文件1 = 行.Count(x => x.Contains("// 来自文件1第", StringComparison.Ordinal));
                    int 文件2 = 行.Count(x => x.Contains("// 来自文件2第", StringComparison.Ordinal));
                    bool 过 = 码 == 0 && 文件1 > 0 && 文件2 > 0;
                    return (过, $"退出码 {码}(期望 0);文件1 标记 {文件1} 处 / 文件2 标记 {文件2} 处(都要 > 0);{截断(误, 60)}");
                }),

                ("CLI 端到端 -p 工程 --asm:不带 --asm-marked 时与带标记那份剪掉标记后逐字节相同(H43 默认没变)", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    var (带码, 带出, 带误) = 跑命令行(编译器!, 暂存, 超时, "-p", "标记.hbp", "--asm", "--asm-marked");
                    var (平码, 平出, 平误) = 跑命令行(编译器!, 暂存, 超时, "-p", "标记.hbp", "--asm");
                    string 剪 = 剪掉源行标记(带出);
                    string 平 = 平出.Replace("\r\n", "\n");
                    int 平标记数 = 平.Split('\n').Count(x => x.Contains("// 来自", StringComparison.Ordinal));
                    bool 过 = 带码 == 0 && 平码 == 0 && 平标记数 == 0 && 剪 == 平;
                    return (过, $"带标记退出码 {带码} / 不带 {平码};不带那份里 `// 来自` {平标记数} 处(期望 0);" +
                        $"剪掉标记后逐字节相同={剪 == 平};{截断(带误 + 平误, 60)}");
                }),

                ("CLI 端到端 -p 标准工程 `多文件_跨文件共享` --asm 退出码 0、展开里找得到跨文件的名字(H43)", () =>
                {
                    if (!备好了) return (null, "临时目录没建起来");
                    if (根 == null) return (null, "工作副本之外跑自检,找不到仓库根");
                    string 工程目录 = Path.Combine(根, "Sample", "中间层", "多文件_跨文件共享");
                    if (!Directory.Exists(工程目录)) return (null, "标准工程不在(示例集换过主题了)");
                    var (码, 出, 误) = 跑命令行(编译器!, 根, 超时, "-p", 工程目录, "--asm");
                    bool 在 = 出.Contains("平方和", StringComparison.Ordinal);
                    bool 过 = 码 == 0 && 在;
                    return (过, $"退出码 {码}(期望 0);展开里找得到 `平方和`={在}(第 245 批量到的那一条:31 过 1 败);{截断(误, 60)}");
                }),

                ("CLI 端到端 帮助《上手》第16节写明工程级 --asm 是一份整篇(与真正编译同源)(H43)", () =>
                {
                    string 帮助 = 帮助文本C.快速上手();
                    bool 过 = 帮助.Contains("hbcc -p 工程.hbp --asm --asm-marked", StringComparison.Ordinal)
                        && 帮助.Contains("整张文件表交给一个展开器", StringComparison.Ordinal)
                        && 帮助.Contains("一份整篇", StringComparison.Ordinal);
                    return (过, $"帮助里写了工程级 --asm 的口径={过}");
                }),
            };

            // 找不到命令行编译器(在 IDE 里跑自检、或这一端还没建)⇒ **逐条记跳过**。
            // 名字必须跟真跑那一支一模一样:断言清单是按**名字**比对的,换名字等于给清单加一条/减一条。
            if (编译器 == null || !备好了)
            {
                string 因 = 编译器 == null
                    ? "找不到 hbcc.dll(在 IDE 里跑自检、或命令行这一端还没建时正常)"
                    : "临时目录没建起来(上面的异常那一条就是原因)";
                foreach (var (名, _) in 用例) 跳过(名, 因);
                return;
            }

            // 一条用例一个 try:一条自己抛异常,只算它自己一处,后面的照跑(与 R4-1 的"每组各自 try"同一个口径)。
            foreach (var (名, 跑) in 用例)
            {
                bool? 过; string 详情;
                try { (过, 详情) = 跑(); }
                catch (Exception ex) { (过, 详情) = (false, "用例自己抛了异常:" + ex.Message); }
                if (过 == null) 跳过(名, 详情);
                else 记录(名, 过.Value, 详情);
            }
        }
    }
}
