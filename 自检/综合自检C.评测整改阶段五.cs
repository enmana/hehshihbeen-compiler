using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 综合自检的一部分:**第 5 阶段(约定与结构)** —— `评测整改计划.md` §6 那五条(一次性、无依赖、可插空):
    ///   T26 `global.json` 的静默换 SDK 与警告门槛 / T27 语义对齐不再改行尾
    ///   / T28 关窗与后台三处收口 / T29 三套注释口径合成一处 / T30 四处小修。
    ///
    /// 判据分两类,名字里也写清楚:
    ///   · **真跑**(T27/T29/T30 那些)—— 真编真跑、真比字节,不靠源码钉充数;
    ///   · **接线哨兵**(T26、T28 那些按源码钉的)—— 判"线还在不在",按 T22 的口径单独记账,
    ///     不许单独当功能正确性的证据。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary>
        /// T26(`global.json` 的静默换 SDK 与警告门槛)。
        ///
        /// **为什么盯这两处**:`global.json` 锁 `10.0.401` + `rollForward: latestFeature` 时,
        /// 把版本号改成 `10.0.100 / 200 / 400 / 500` **照样解析成盘上最新那个 SDK**(实测:
        /// `工具\临时\T26\sdk解析_口径.md`)—— 也就是说这份锁**根本没锁住**,装个新 feature band 就静默换编译器。
        /// 所以判据不是"解析出来是几"(那台机器装了哪些 SDK 一决定,数就钉不住),而是**政策本身**:
        /// `rollForward` 只许 `latestPatch` / `disable`(都停在同一 feature band 内)、版本号三段钉死、
        /// `LangVersion` 显式写着(不写就随 SDK 漂);外加"同名类型两份拷贝"的 CS0436 不许回来。
        /// </summary>
        private static void 评测整改阶段五自检()
        {
            string? 根 = 找仓库根目录();

            // ---- T26①:版本锁定的政策(接线哨兵:读的就是 global.json / Directory.Build.props 的原文) ----
            if (根 == null) 跳过接线("评测整改阶段五 T26 版本锁定不静默漂移", "找不到仓库根(不在工作副本里跑)");
            else try
            {
                string 全局 = File.ReadAllText(Path.Combine(根, "global.json"));
                var 政策格 = Regex.Match(全局, "\"rollForward\"\\s*:\\s*\"([^\"]+)\"");
                string 政策 = 政策格.Success ? 政策格.Groups[1].Value : "(没写)";
                bool 同带内 = 政策 is "latestPatch" or "disable";
                var 版本格 = Regex.Match(全局, "\"version\"\\s*:\\s*\"(\\d+)\\.(\\d+)\\.(\\d+)\"");
                bool 三段钉 = 版本格.Success && !全局.Contains('*', StringComparison.Ordinal);

                // `LangVersion` 一并钉在这条里:不写 = 随 SDK 漂,与 global.json 锁不住是同一类病。
                string 属性 = File.ReadAllText(Path.Combine(根, "Directory.Build.props"));
                var 语言格 = Regex.Match(属性, "<LangVersion>\\s*([^<\\s]+)\\s*</LangVersion>");
                记接线("评测整改阶段五 T26 版本锁定不静默漂移(rollForward 停在同 feature band + LangVersion 显式)",
                    同带内 && 三段钉 && 语言格.Success,
                    $"rollForward={政策}(只许 latestPatch / disable);三段版本钉死={三段钉};" +
                    $"LangVersion={(语言格.Success ? 语言格.Groups[1].Value : "(没写)")}");
            }
            catch (Exception ex) { 记录异常("评测整改阶段五 T26 版本锁定", ex); }

            // ---- T26②:CS0436(同名类型两份拷贝)不许回来 —— 判据 = 两个工程链进来的引擎源不许有交集 ----
            if (根 == null) 跳过接线("评测整改阶段五 T26 宿主与图形宿主不编同一份引擎源", "找不到仓库根(不在工作副本里跑)");
            else try
            {
                string[] 宿主 = 工程编的引擎源(Path.Combine(根, "HB宿主", "HB宿主.csproj"));
                string[] 图形 = 工程编的引擎源(Path.Combine(根, "图形宿主", "图形宿主.csproj"));
                string[] 重叠 = 宿主.Intersect(图形, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                记接线("评测整改阶段五 T26 宿主与图形宿主不编同一份引擎源(重叠 = 同名类型两份拷贝 = CS0436)",
                    重叠.Length == 0,
                    重叠.Length == 0
                        ? $"HB宿主 {宿主.Length} 份 / 图形宿主 {图形.Length} 份,零交集"
                        : "两边都编了:" + string.Join(" / ", 重叠));
            }
            catch (Exception ex) { 记录异常("评测整改阶段五 T26 宿主与图形宿主", ex); }

            // ---- T27:语义对齐不许改行尾(唯一会改坏用户文件字节的那一处;3 例:CRLF / LF / 混合) ----
            try
            {
                // 同一份内容三种行尾写法;对齐只该动空白,行尾一个字节都不许变。
                const string 一段 = "起:\n加载立即数 R0, 1 // 一\n停止\n";
                string crlf = 一段.Replace("\n", "\r\n");
                string lf = 一段;
                string 混 = "起:\r\n" + "加载立即数 R0, 1 // 一\n" + "停止\r\n";   // 2 段 CRLF + 1 段 LF

                var 例 = new (string 名, string 原, string 望主换行)[]
                {
                    ("CRLF", crlf, "\r\n"),
                    ("LF", lf, "\n"),
                    ("混合", 混, "\r\n"),
                };
                var 说明 = new List<string>();
                foreach (var (名, 原, 该主换行) in 例)
                {
                    string 对齐后 = 语义列C.对齐文本(原, out string 主);
                    // ① 换行骨架(只剩 `R`/`N` 两种字符)一个字节不变 —— 纯 CRLF / 纯 LF / 混合都当场看得见
                    bool 行尾不变 = 骨架(原) == 骨架(对齐后);
                    // ② 内容逐字符不变(去掉所有空白与换行再比)= "只动空白"
                    bool 内容不变 = 无空白(原) == 无空白(对齐后);
                    if (!行尾不变 || !内容不变) 说明.Add($"{名}:行尾不动={行尾不变} 内容不动={内容不变}");
                    if (主 != 该主换行)
                        说明.Add($"{名}:主换行={看白(主)} 该是 {看白(该主换行)}");
                }
                记录("T27 语义对齐不动行尾(CRLF 仍 CRLF / LF 仍 LF / 混合逐行原样;只动空白)",
                    说明.Count == 0, 说明.Count == 0 ? "3 例的换行骨架一个字节没变、内容逐字符相同" : string.Join(" ; ", 说明));
                // ③ 对齐真在动空白:同一份内容里几行标签宽度不同 ⇒ 对齐后与原文本**不一样**(否则上面三条是白绿)
                记录("T27 语义对齐确实在改空白(不是原样返回混过判据)",
                    语义列C.对齐文本("起:\n加载立即数 R0, 1\n长标签名:\n停止\n") != "起:\n加载立即数 R0, 1\n长标签名:\n停止\n",
                    "标签列宽不同 ⇒ 该被补齐");
            }
            catch (Exception ex) { 记录异常("评测整改阶段五 T27 语义对齐", ex); }

            // ---- T28①:异常收集C.记 从后台线程进来时排队(不直接碰界面;这条是**真行为探针**) ----
            try
            {
                var 收到 = new List<string>();
                void 订(string 行) => 收到.Add(行);
                异常收集C.追加 += 订;

                // 装了"界面线程"且当前**不在**界面上 ⇒ 只排队、不直接写
                var 排上的 = new List<Action>();
                异常收集C.安装界面线程(() => false, 做 => 排上的.Add(做));
                异常收集C.记("后台来的");
                bool 没直接写 = 收到.Count == 0 && 排上的.Count == 1;
                // 队列里那一件跑到"界面线程"上时才真写
                if (排上的.Count == 1) 排上的[0]();
                bool 排队后会写 = 收到.Count == 1 && 收到[0] == "后台来的\n";

                // 已经在界面上 ⇒ 当场写(不排队)
                收到.Clear(); 排上的.Clear();
                异常收集C.安装界面线程(() => true, 做 => 排上的.Add(做));
                异常收集C.记("界面来的");
                bool 当场写 = 收到.Count == 1 && 收到[0] == "界面来的\n" && 排上的.Count == 0;

                异常收集C.追加 -= 订;
                记录("T28 异常收集 后台线程来的记录排到界面线程(排在界面上才写;已在界面上就当场写)",
                    没直接写 && 排队后会写 && 当场写,
                    $"不在界面上:直接写={!没直接写}(该 False) 排了 {排上的.Count} 件;排队跑完写到={排队后会写};在界面上当场写={当场写}");
            }
            catch (Exception ex) { 记录异常("评测整改阶段五 T28 异常收集跨线程", ex); }

            // ---- T28②:后台发布宿主那处 `Task.Run` 有没有 try/catch/finally(接线哨兵) ----
            if (根 == null) 记接线("T28 后台发布宿主有 try/catch/finally(失败也要复位状态旗 + 给回执)", false, "找不到仓库根");
            else try
            {
                string 窗口 = File.ReadAllText(Path.Combine(根, "MainWindow.运行与调试器.cs"));
                string 体 = 取段(窗口, "private void 开始后台发布宿主()");
                // 判据锚在**代码形态**上,而且一律从"补"那一处往后找 —— 方法的文档注释里就写着
                // "补 try/catch/finally",从开头 `IndexOf("finally")` 会锚到注释上去(实测踩到)。
                int 旗 = 体.IndexOf("_宿主发布中 = true;", StringComparison.Ordinal);
                int 发布 = 体.IndexOf("发布宿主(out", StringComparison.Ordinal);
                int 试 = 体.IndexOf("                try\n", StringComparison.Ordinal);
                int 捕 = 体.IndexOf("catch (Exception", 发布, StringComparison.Ordinal);
                int 终 = 体.IndexOf("finally", 捕, StringComparison.Ordinal);
                int 复位 = 体.IndexOf("_宿主发布中 = false;", 终, StringComparison.Ordinal);
                记接线("T28 后台发布宿主有 try/catch/finally(失败也要复位状态旗 + 给回执)",
                    旗 >= 0 && 试 >= 0 && 发布 >= 0 && 捕 >= 0 && 终 >= 0 && 复位 >= 0
                    && 旗 < 试 && 试 < 发布 && 发布 < 捕 && 捕 < 终 && 终 < 复位,
                    $"立旗[{旗}] try[{试}] 发布[{发布}] 捕[{捕}] 终[{终}] 复位[{复位}](须依次递增)");
            }
            catch (Exception ex) { 记录异常("评测整改阶段五 T28 后台发布宿主", ex); }

            // ---- T29:注释/字面量口径只剩一处(四处对同一行源码逐字节比区间 + 检查器不再漏检) ----
            try
            {
                // 四点口径:① `代码解析器.取注释区`(唯一事实源) ② `高亮扫描C.行内注释区`
                // ③ `分词器C.取注释区`(转调) ④ `检查器C` 的口径 —— 它没有公开的"取注释区",
                //    用 `遮蔽注释` 的等价物验:`代码解析器.去注释` 与 `分词器C.去注释` 逐字节相同。
                var 刁钻 = new (string 名, string 行)[]
                {
                    ("网址里的 //", "字符串 u = \"http://主机:80/路径\";"),
                    ("字符字面量里的双引号", "字符 引号 = '\"'; // 尾巴注释"),
                    ("块注释一行内", "左 /* 块 */ 右 // 尾巴"),
                    ("没闭合的块注释", "左 /* 忘了关"),
                    ("转义引号", "字符串 s = \"a\\\"//b\"; // 真注释"),
                    ("普通行尾注释", "整数 初值 = 1; // 说明"),
                };
                var 说明 = new List<string>();
                foreach (var (名, 行) in 刁钻)
                {
                    string 解析器区 = 区间文本(代码解析器.取注释区(行));
                    string 高亮区 = 区间文本(高亮扫描C.行内注释区(行));
                    string 分词区 = 区间文本(分词器C.取注释区(行));
                    bool 同 = 解析器区 == 高亮区 && 高亮区 == 分词区;
                    bool 去注释同 = 代码解析器.去注释(行) == 分词器C.去注释(行);
                    if (!同 || !去注释同) 说明.Add($"{名}:取注释区[{解析器区}|{高亮区}|{分词区}] 去注释同={去注释同}");
                }
                记录("T29 注释口径只剩一处(代码解析器 / 高亮扫描C / 分词器C 三处取注释区逐字节相同;去注释也相同)",
                    说明.Count == 0, 说明.Count == 0 ? $"{刁钻.Length} 行刁钻源码三处区间一致" : string.Join(" ; ", 说明));

                // 网址那一行必须**没有**注释区(以前的 `行内注释区` 会给 `(14,26)`);真注释那一行必须有
                bool 网址不算注释 = 高亮扫描C.行内注释区("字符串 u = \"http://主机:80/路径\";").Count == 0
                    && 代码解析器.取注释区("字符串 u = \"http://主机:80/路径\";").Count == 0;
                // 字符字面量里那个双引号不许把"在串里"带反 ⇒ 后面那个 `// 尾巴注释` 要认出来
                var 尾注释 = 代码解析器.取注释区("字符 引号 = '\"'; // 尾巴注释");
                bool 尾巴认出来 = 尾注释.Count == 1 && 尾注释[0].结束 == "字符 引号 = '\"'; // 尾巴注释".Length;
                记录("T29 网址里的 // 不算注释、字符字面量里的 \" 不把状态带反(两个当年的实测)",
                    网址不算注释 && 尾巴认出来,
                    $"网址行注释区={代码解析器.取注释区("字符串 u = \"http://主机:80/路径\";").Count} 个;" +
                    $"字符字面量那行认出的注释={尾注释.Count} 个");

                // 检查器:以前 `字符 引号 = '"'; // …` 里真实的 `测试输出` 调用**漏检**(整行被当注释/串里)
                string 源 = "字符 引号 = '\"'; // 引号那一行\n测试输出(1);\n停止\n";
                var 诊断 = 检查器C.检查(源, true);
                bool 没漏检 = 诊断.Any(x => x.规则 == "旧写法" && x.行 == 2);
                记录("T29 检查器在「字符字面量里带双引号 + 行尾注释」之后还认得出真实调用(以前漏检)",
                    没漏检, 没漏检 ? "第 2 行的 `测试输出` 报出来了" : "没报出来:" + string.Join("/", 诊断.Select(x => $"{x.行}:{x.规则}")));
            }
            catch (Exception ex) { 记录异常("评测整改阶段五 T29 注释口径", ex); }

            // ---- T30:便宜但直接的四处小修(四条各自独立,合一组) ----
            评测整改阶段五自检_T30();
        }

        /// <summary> 把区间表写成一串文本(自检详情里看得见"四处区间是不是逐字节相同") </summary>
        private static string 区间文本(List<(int 开始, int 结束)> 区) =>
            string.Join(",", 区.Select(x => $"{x.开始}-{x.结束}"));

        /// <summary>
        /// T30(便宜但直接的四处小修)。四条各一组判据:
        ///   ① 名字扫描不再每次现造两个 `RegexOptions.Compiled` 正则(接线哨兵 + 行为探针);
        ///   ② `--bench` 有预热 + 多遍取最优,默认档规模有意义(接线哨兵 + **真跑比值**);
        ///   ③ `--en` 那句绝对话收口成"示例集内 + 撞类型名的边界"(接线哨兵,两处文档);
        ///   ④ 内联汇编负立即数给对症的改法(真跑)。
        /// </summary>
        private static void 评测整改阶段五自检_T30()
        {
            string? 根 = 找仓库根目录();

            // ---- T30①:名字扫描那两个正则不许"每次现造" ----
            if (根 == null) 记接线("T30 名字扫描的变量/循环正则按模式缓存(不再每次现造 Compiled)", false, "找不到仓库根");
            else
            {
                try
                {
                    string 源 = File.ReadAllText(Path.Combine(根, "引擎", "源码名字扫描C.cs"));
                    bool 有缓存 = 源.Contains("private static (Regex 变量, Regex 循环) 变量扫描正则(string 类型支全)", StringComparison.Ordinal)
                        && 源.Contains("_正则缓存.TryGetValue(类型支全, out var 已有)", StringComparison.Ordinal);
                    // 那一对正则只许**合起来 `new` 这两条**(变量 + 循环)、而且只许在那个缓存方法里。
                    // 数的时候先掐掉注释行 —— 老写法那两行收口后成了"以前这里是…"的注释,
                    // 不掐会把注释也数进去(实测踩到)。
                    int 现造处 = 源.Replace("\r", "").Split('\n')
                        .Where(行 => !行.TrimStart().StartsWith("//", StringComparison.Ordinal))
                        .Sum(行 => Regex.Matches(行, @"new Regex\(\$\@""[^""]*类型支全").Count);
                    记接线("T30 名字扫描的变量/循环正则按模式缓存(不再每次现造 Compiled)",
                        有缓存 && 现造处 == 2, $"有缓存方法={有缓存};`new Regex` 带类型支全的处数={现造处}(变量 + 循环,只许这 2 处)");

                    // 行为探针:同一份源码连扫两次,结果一致(缓存没把结果串味)
                    var 左次 = 源码名字扫描C.扫描("结构 点 {\n整数 x;\n}\n点 记录;\n整数 初值 = 1;\n");
                    var 右次 = 源码名字扫描C.扫描("结构 点 {\n整数 x;\n}\n点 记录;\n整数 初值 = 1;\n");
                    记录("T30 名字扫描连扫两次结果一致(缓存/结构名排序没有改变扫出来的名字)",
                        左次.变量.SetEquals(右次.变量) && 左次.结构.SetEquals(右次.结构) && 左次.结构.Contains("点") && 左次.变量.Contains("记录"),
                        $"结构[{string.Join(",", 左次.结构)}] 变量[{string.Join(",", 左次.变量)}]");
                }
                catch (Exception ex) { 记录异常("评测整改阶段五 T30 名字扫描", ex); }
            }

            // ---- T30②:--bench 有预热 + 多遍取最优;两档规模的读数比值落在 1.5 倍内(真跑) ----
            try
            {
                double 取最优(int 循环, int 遍数)
                {
                    double 最好 = 0;
                    for (int i = 0; i < 遍数; i++)
                    {
                        虚拟机测速C.跑一轮(循环, out long 走, out double 秒);
                        double 速率 = 走 / 秒;
                        if (速率 > 最好) 最好 = 速率;
                    }
                    return 最好;
                }
                // 预热(与 CLI 同一条路:先跑一轮小规模)
                虚拟机测速C.跑一轮(2_000, out _, out _);
                double 小 = 取最优(200_000, 3);
                double 大 = 取最优(2_000_000, 2);
                double 比值 = 大 > 小 ? 大 / 小 : 小 / 大;
                记录("T30 --bench 两档规模的读数比值 ≤1.5(预热 + 取最优之后;以前同一台机器能漂 15 倍)",
                    比值 <= 1.5,
                    $"--bench 200000 → {小 / 1e6:F1} M/s;--bench 2000000 → {大 / 1e6:F1} M/s;比值 {比值:F2}");
            }
            catch (Exception ex) { 记录异常("评测整改阶段五 T30 --bench", ex); }

            // ---- T30③:--en 那句绝对话收口(CLI 帮助 + 帮助站点正文两处) ----
            try
            {
                string 命令行源 = 根 == null ? "" : File.ReadAllText(Path.Combine(根, "编译器CLI", "Program.cs"));
                string 帮助源 = 根 == null ? "" : File.ReadAllText(Path.Combine(根, "引擎", "帮助文本C.cs"));
                bool 命令行收口 = 命令行源.Contains("示例集里两边机器码逐字节", StringComparison.Ordinal)
                    && 命令行源.Contains("撞了类型名", StringComparison.Ordinal)
                    && !命令行源.Contains("编译结果与中文版逐字节一致)", StringComparison.Ordinal);
                bool 帮助收口 = 帮助源.Contains("示例集里", StringComparison.Ordinal)
                    && 帮助源.Contains("不保证逐字节一致", StringComparison.Ordinal);
                记接线("T30 `--en` 不再承诺无条件逐字节一致(示例集内 + 撞类型名的边界写清)",
                    命令行收口 && 帮助收口, $"CLI 帮助收口={命令行收口};帮助站点正文收口={帮助收口}");
            }
            catch (Exception ex) { 记录异常("评测整改阶段五 T30 --en 承诺", ex); }

            // ---- T30④:内联汇编负立即数给对症的改法(真跑) ----
            try
            {
                string 文案 = "";
                try { new 汇编器C().汇编("加载立即数 R0, -1\n停止"); }
                catch (Exception 异常) { 文案 = 异常.Message; }
                bool 对症 = 文案.Contains("不支持负数", StringComparison.Ordinal)
                    && 文案.Contains("取反", StringComparison.Ordinal)
                    && !文案.Contains("多写了 1 个操作数", StringComparison.Ordinal);
                记录("T30 内联汇编 `加载立即数 R0, -1` 报的是负立即数 + 给的改法是 装 1 再取反(不再是「多写了操作数」)",
                    对症, 文案.Length == 0 ? "(没报错?)" : 文案);
            }
            catch (Exception ex) { 记录异常("评测整改阶段五 T30 负立即数", ex); }
        }

        /// <summary>
        /// 取一段源码:从 `签名` 起,到它自己那一层的收尾 `}`(8 空格缩进)为止。找不到给空串。
        /// **先把 `\r` 抹平**再找 —— 仓库里几份 `MainWindow.*.cs` 是 CRLF(实测 1093 个 `\r`),
        /// 锚 `"\n        }"` / `"\n                try\n"` 这类字面量在 CRLF 文件上一个都找不到。
        /// (与 `综合自检C.窗口生命周期` 那个 `取方法体` 同一个口径 —— 这里另起名,免得两处撞定义。)
        /// </summary>
        private static string 取段(string 文本, string 签名)
        {
            文本 = 文本.Replace("\r", "");
            int 起 = 文本.IndexOf(签名, StringComparison.Ordinal);
            if (起 < 0) return "";
            int 尾 = 文本.IndexOf("\n        }", 起, StringComparison.Ordinal);
            return 尾 < 0 ? "" : 文本[起..(尾 + "\n        }".Length)];
        }

        /// <summary> 只留换行骨架(`N` = \n、`R` = \r;其余字符一概不进来)—— 比"行尾序列一个字都没变"用 </summary>
        private static string 骨架(string 文本)
        {
            var 出 = new System.Text.StringBuilder();
            for (int i = 0; i < 文本.Length; i++)
                if (文本[i] == '\n') 出.Append('N');
                else if (文本[i] == '\r') 出.Append('R');
            return 出.ToString();
        }

        /// <summary> 去掉所有空白与换行(比"内容逐字符没变"用) </summary>
        private static string 无空白(string 文本) =>
            new string(文本.Where(c => !char.IsWhiteSpace(c)).ToArray());

        /// <summary>
        /// 一个 csproj 里 `Compile Include` 的**引擎源文件名**(只认 `..\引擎\xxx.cs` 这种链进来的)。
        /// `EnableDefaultCompileItems=false` 的工程全是显式列,拿它比"两个工程是不是编了同一个源文件"。
        /// </summary>
        private static string[] 工程编的引擎源(string 工程路径)
        {
            var 名 = new List<string>();
            if (!File.Exists(工程路径)) return 名.ToArray();
            foreach (Match m in Regex.Matches(File.ReadAllText(工程路径),
                @"Compile\s+Include\s*=\s*""[^""]*引擎\\([^""\\]+\.cs)"""))
                名.Add(m.Groups[1].Value);
            return 名.ToArray();
        }
    }
}
