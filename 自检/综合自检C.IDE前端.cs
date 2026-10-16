using System.Diagnostics;
using System.IO;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **R1-6 批 4 前端线(4-1 ~ 4-4)那一族的库契约自检** —— 与 `示例64~67` 那一组**分工不同**:
    /// · 标准工程那几组量的是"整件拼起来逐行对"(端到端,在 `示例` 那一组里);
    /// · 这一组量的是**库自己的契约**:同一条路上的数对不对、焦点与脏标记给不给确定值。
    ///
    /// 为什么要单独一组:那几件的端到端判据走的是"示例用到的那几条路";
    /// 库这一层要是被人改坏了(改个常量、漏个入口),端到端**未必当场红** ⇒ 这一组把每一件的核心契约各钉一条。
    ///
    /// **跑法(这一段是踩出来的,见 `任务清单.md` 第461 / 464节)**:
    /// ① **不在自检进程里编** —— 那条路上 `包含` 解不了(内存文件表不参与解析;盘上那条路的基准是
    ///    进程当前目录,不是源文件目录。四条路都试过、都不成立);
    /// ② 改成**起 `hbcc.exe` 跑一份临时探针**(与命令行 `--run` 同一个口径:探针里 `包含` 写**绝对路径**,
    ///    盘上那几份库自己按盘上相对路径解 —— 这条链在命令行下是通的,`工具\临时\` 里的探针实测过);
    /// ③ 探针落在 `工具\临时\IDE前端契约\`,判据 = **进程退出码 0 + stdout 逐行等于期望**。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary> 起 hbcc 跑一份源码,回 (退出码, 输出) </summary>
        private static (int 码, string 出) IDE前端起探针(string 源码, string 目录, string 名)
        {
            Directory.CreateDirectory(目录);
            string 源文件 = Path.Combine(目录, 名 + ".hb");
            File.WriteAllText(源文件, 源码, new UTF8Encoding(false));
            string hbcc = Path.Combine(AppContext.BaseDirectory, "hbcc.exe");
            if (!File.Exists(hbcc)) hbcc = "hbcc";
            var 起 = new ProcessStartInfo(hbcc)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                WorkingDirectory = Directory.GetCurrentDirectory(),
            };
            起.ArgumentList.Add(源文件);
            起.ArgumentList.Add("--run");
            using var 进程 = Process.Start(起)!;
            string 出 = 进程.StandardOutput.ReadToEnd() + 进程.StandardError.ReadToEnd();
            进程.WaitForExit(120_000);
            return (进程.ExitCode, 出.Replace("\r\n", "\n").TrimEnd('\n'));
        }

        /// <summary> 一条契约:跑探针、逐行比期望 </summary>
        private static void IDE前端契约(string 题目, string 源码, string 应有)
        {
            try
            {
                string 目录 = Path.Combine(临时区C.基目录(), "IDE前端契约");
                var (码, 出) = IDE前端起探针(源码, 目录, "探针");
                // 只留"答案行":工程横幅 / 内存布局 / 带 `·` 的那些都不是契约的一部分
                var 行 = 出.Split('\n').Where(x => x.Length > 0 && !x.Contains(' ') && !x.Contains('·')
                    && !x.Contains(':') && !x.Contains('=') && !x.Contains('(') && !x.Contains(')')).ToArray();
                string 实 = string.Join("\n", 行);
                记录(题目, 码 == 0 && 实 == 应有,
                    码 != 0 ? $"退出码 {码};出=<{出.Replace("\n", "|")}>"
                     : 实 == 应有 ? $"{应有.Split('\n').Length} 个数逐行对上"
                     : $"实际=<{实.Replace("\n", "|")}> 期望=<{应有.Replace("\n", "|")}>");
            }
            catch (Exception ex) { 记录异常(题目, ex); }
        }

        /// <summary> R1-6 批 4 前端线(4-1 ~ 4-4)的库契约 </summary>
        public static void IDE前端自检()
        {
            string? 示例 = 找示例目录();
            if (示例 == null) { 跳过("IDE前端 4-1~4-4 库契约", "找不到 Sample 目录(工作副本之外跑自检)"); return; }
            string 根 = Directory.GetParent(示例)!.FullName;
            string 库 = Path.Combine(根, "Sample", "库").Replace('\\', '/');
            string 头 = "包含 \"" + 库 + "/编辑器.hb\"\n包含 \"" + 库 + "/编辑器视图.hb\"\n包含 \"" + 库 + "/IDE布局.hb\"\n";

            // ① 4-1 编辑器内核:光标按字走(中文一列两格)/ 回车拆行 / 行首退格并回接缝
            IDE前端契约("IDE前端 4-1 编辑器内核:光标按字走(中文一列两格)/ 回车拆行 / 行首退格夹在两行之间时行数不变(光标留接缝列 0)",
                头 + "编辑器.编辑缓冲 编;\n编辑器.建编辑器(编, 5);\n" +
                "编辑器.插入文本(编, \"abc\");\n编辑器.插入文本(编, \"中文\");\n" +
                "测试输出(编辑器.光标列(编));\n测试输出(编辑器.光标格(编));\n" +
                "编辑器.编辑键(编, 编辑器.键回车());\n测试输出(编辑器.行数(编));\n" +
                "编辑器.移动键(编, 编辑器.键首页());\n编辑器.编辑键(编, 编辑器.键退格());\n" +
                "测试输出(编辑器.行数(编));\n测试输出(编辑器.光标列(编));\n停止\n",
                "5\n7\n2\n2\n0");

            // ② 4-2 编辑器视图:正文宽让开滚动条 / 一屏 5 行 / 三条文字那一屏 8 条命令
            IDE前端契约("IDE前端 4-2 编辑器视图:正文宽让开滚动条(200-32-6-8)/ 一屏 5 行 / 三条文字那一屏 9 条命令(实测)",
                头 + "编辑器.编辑缓冲 编;\n编辑器.建编辑器(编, 5);\n编辑器.装入文本(编, \"甲\\n乙\\n丙\");\n" +
                "编辑器视图.面板 屏;\n编辑器视图.建面板(屏, 10, 20, 200, 100, 32, 20);\n" +
                "测试输出(编辑器视图.正文宽(屏));\n测试输出(编辑器视图.一屏行数(屏));\n" +
                "测试输出(编辑器视图.画一屏(屏, 编));\n停止\n",
                "154\n5\n9");

            // ③ 4-3 IDE 布局:三格命中各归各位 / 横隔条认得出 / 拖动把左栏 140 拖到 182
            IDE前端契约("IDE前端 4-3 IDE 布局:三格命中各归各位 / 横隔条认得出 / 拖动把左栏 140 拖到 182",
                头 + "IDE布局.布局 台;\nIDE布局.建布局(台, 0, 0, 640, 400, 140, 120);\n" +
                "测试输出(IDE布局.命中格(台, 300, 200));\n测试输出(IDE布局.命中格(台, 60, 380));\n" +
                "测试输出(IDE布局.命中格(台, 400, 380));\n测试输出(IDE布局.命中隔条(台, 142, 200));\n" +
                "IDE布局.送鼠标(台, 1, 142, 200);\nIDE布局.送鼠标(台, 0, 182, 200);\n" +
                "测试输出(台.左栏宽);\nIDE布局.送鼠标(台, 2, 182, 200);\n停止\n",
                "1\n2\n2\n1\n182");

            // ④ 4-4 主循环那块调度:焦点跟着鼠标走 / 脏了就重画、重画过就干净
            IDE前端契约("IDE前端 4-4 主循环:焦点跟着鼠标走 / 脏了就重画、重画过就干净(没变不发命令)",
                头 + "IDE布局.布局 台;\nIDE布局.建布局(台, 0, 0, 640, 400, 140, 120);\n" +
                "测试输出(IDE布局.焦点格(台));\n测试输出(IDE布局.要重画(台));\n" +
                "测试输出(IDE布局.重画(台) > 0);\n测试输出(IDE布局.要重画(台));\n" +
                "IDE布局.送鼠标(台, 1, 400, 380);\n测试输出(IDE布局.焦点格(台));\n停止\n",
                "1\n1\n1\n0\n2");
        }
    }
}
