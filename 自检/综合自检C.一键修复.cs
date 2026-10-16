using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **问题表 → 一键修复**(P38,2026-10-07;用户排的七件里的第 5 件:
    /// "从『报错』到『改好』的那一步";这一批先做**十条最常见的**)。
    ///
    /// 判据是**真把那条诊断修没**:每一类都拿一段真源码跑 `检查器C.检查` 拿到那条诊断 →
    /// `一键修复C.算` 拿方案 → `一键修复C.应用` → **再检查一遍** → 那条诊断必须不见了,
    /// 而且**同一类别的条数只许变少**(修一条不许捅出别的来)。
    /// 另外钉两条纪律:
    ///   · **拿不准的不许动代码**(常量条件 / 空循环只插一行 TODO,原文一个字不改);
    ///   · **编译错误没有通法** ⇒ 方案为空(界面据此把"修这一条"变灰,不假装有);
    ///   · 末尾一条接线哨兵:右键菜单 / 预览 / `页.内容` 那条落点都在。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 一键修复自检()
        {
            // ---- ① 行尾空格 / 缩进混用 / 旧写法:三条文本级修复各修没一条 ----
            const string 文本级名 = "一键修复 行尾空格 / 缩进混用 / 旧写法:应用之后那一条诊断真的没了(别处不多出一条)";
            try
            {
                var 样例 = new (string 规则, string 源码)[]
                {
                    ("行尾空格", "整数 初值 = 1;   \n停止\n"),
                    ("缩进混用", "方法 函数()\n{\n \t 整数 初值 = 1;\n    停止\n}\n"),
                    ("旧写法", "整数 初值 = 2;\n测试输出(初值);\n停止\n"),
                };
                var 结果 = new List<string>();
                bool 全对 = true;
                foreach (var (规则, 源码) in 样例)
                {
                    var 修前 = 检查器C.检查(源码);
                    var 那一条 = 修前.FirstOrDefault(x => x.规则 == 规则);
                    if (那一条 == null) { 全对 = false; 结果.Add($"{规则}:没检查出这一条"); continue; }
                    var 方案 = 一键修复C.算(那一条, 源码).FirstOrDefault();
                    if (方案 == null) { 全对 = false; 结果.Add($"{规则}:没有方案"); continue; }
                    string 修后 = 一键修复C.应用(源码, 方案.改动) ?? "";
                    var 新诊断 = 检查器C.检查(修后);
                    bool 这条没了 = !新诊断.Any(x => x.规则 == 规则);
                    bool 没多出来 = 新诊断.Count(x => x.规则 == 规则) == 0;
                    bool 别的没多 = 新诊断.Count(x => x.规则 != 规则) <= 修前.Count(x => x.规则 != 规则);
                    全对 &= 这条没了 && 没多出来 && 别的没多;
                    结果.Add($"{规则}→「{方案.名称}」这条没了={这条没了} 别的没多={别的没多}");
                }
                记录(文本级名, 全对, string.Join(";", 结果));
            }
            catch (Exception ex) { 记录异常(文本级名, ex); }

            // ---- ② 计数器会绕回 / 未使用:两条"改一处就好"的 ----
            const string 改一处名 = "一键修复 计数器加宽 / 未使用加下划线:应用之后那一条诊断真的没了";
            try
            {
                const string 绕回源 = "循环 (短整数 i = 0; i < 200000; i++)\n{\n    输出行(i);\n}\n停止\n";
                var 绕回条 = 检查器C.检查(绕回源).FirstOrDefault(x => x.规则 == "计数器会绕回");
                var 绕回方案 = 绕回条 == null ? null : 一键修复C.算(绕回条, 绕回源).FirstOrDefault();
                string 绕回后 = 绕回方案 == null ? "" : (一键修复C.应用(绕回源, 绕回方案.改动) ?? "");
                bool 绕回对 = 绕回后.Contains("整数 i = 0", StringComparison.Ordinal)
                    && !检查器C.检查(绕回后).Any(x => x.规则 == "计数器会绕回");

                const string 没用源 = "短整数 计数 = 1;\n停止\n";
                var 没用条 = 检查器C.检查(没用源).FirstOrDefault(x => x.规则 == "未使用");
                var 没用方案 = 没用条 == null ? null : 一键修复C.算(没用条, 没用源).FirstOrDefault();
                string 没用后 = 没用方案 == null ? "" : (一键修复C.应用(没用源, 没用方案.改动) ?? "");
                bool 没用对 = 没用后.Contains("短整数 _计数 = 1;", StringComparison.Ordinal)
                    && !检查器C.检查(没用后).Any(x => x.规则 == "未使用");

                记录(改一处名, 绕回对 && 没用对,
                    $"绕回:`短整数 i` → `{(绕回后.Contains("整数 i = 0") ? "整数 i" : "(没改)")}`、诊断没了 = {绕回对};" +
                    $"未使用:`计数` → `{(没用后.Contains("_计数") ? "_计数" : "(没改)")}`、诊断没了 = {没用对}");            }
            catch (Exception ex) { 记录异常(改一处名, ex); }

            // ---- ③ 拿不准的两条:只插注释,原文一个字不动 ----
            const string 保守名 = "一键修复 常量条件 / 空循环:只插一行 TODO,原来的代码一个字不改(不许替用户拿主意)";
            try
            {
                const string 常量源 = "如果 (1)\n{\n    输出行(1);\n}\n停止\n";
                var 常量条 = 检查器C.检查(常量源).FirstOrDefault(x => x.规则 == "常量条件");
                var 常量方案 = 常量条 == null ? null : 一键修复C.算(常量条, 常量源).FirstOrDefault();
                string 常量后 = 常量方案 == null ? "" : (一键修复C.应用(常量源, 常量方案.改动) ?? "");
                bool 常量对 = 常量方案 != null
                    && 一键修复C.切行(常量后).Contains("如果 (1)")
                    && 常量后.Contains("// TODO 这里的条件是常量", StringComparison.Ordinal)
                    && 去掉插进去的注释(常量后, "// TODO 这里的条件是常量") == 常量源;

                // 形状照 `检查器C` 那条规则来:**循环头那一行以 `{` 结尾、下一行就是 `}`**
                // (`当 (1 > 0) {\n}\n` 这种;写成"花括号另起一行"的样子它不认 —— 那条规则就是这么写的)。
                const string 空循环源 = "当 (1 > 0) {\n}\n停止\n";
                var 空条 = 检查器C.检查(空循环源).FirstOrDefault(x => x.规则 == "空循环");
                var 空方案 = 空条 == null ? null : 一键修复C.算(空条, 空循环源).FirstOrDefault();
                string 空后 = 空方案 == null ? "" : (一键修复C.应用(空循环源, 空方案.改动) ?? "");
                bool 空对 = 空方案 != null && 空后.Contains("// TODO 还没写", StringComparison.Ordinal)
                    && 空后.Contains("当 (1 > 0) {", StringComparison.Ordinal)
                    && 空后.Contains("停止", StringComparison.Ordinal)
                    && !检查器C.检查(空后).Any(x => x.规则 == "空循环");

                记录(保守名, 常量对 && 空对,
                    $"常量条件:原文原样留着 = {去掉插进去的注释(常量后, "// TODO 这里的条件是常量") == 常量源};" +
                    $"空循环:插了 TODO 且循环还在 = {空后.Contains("// TODO 还没写") && 空后.Contains("当 (1 > 0) {")}");
            }
            catch (Exception ex) { 记录异常(保守名, ex); }

            // ---- ④ 死代码:整行注释掉,而且诊断没了 ----
            const string 死代码名 = "一键修复 死代码:整行注释掉(原文留着),诊断随之消失";
            try
            {
                const string 源 = "输出行(1);\n停止\n输出行(2);\n";
                var 条 = 检查器C.检查(源).FirstOrDefault(x => x.规则 == "死代码");
                var 方案 = 条 == null ? null : 一键修复C.算(条, 源).FirstOrDefault();
                string 后 = 方案 == null ? "" : (一键修复C.应用(源, 方案.改动) ?? "");
                bool 对 = 后.Contains("// 输出行(2);", StringComparison.Ordinal)
                    && !检查器C.检查(后).Any(x => x.规则 == "死代码");
                记录(死代码名, 对, $"第 3 行 → `{一键修复C.切行(后).ElementAtOrDefault(2) ?? "(空)"}`;诊断没了 = {!检查器C.检查(后).Any(x => x.规则 == "死代码")}");
            }
            catch (Exception ex) { 记录异常(死代码名, ex); }

            // ---- ⑤ 编译错误没有通法 + 支持规则表齐全 ----
            const string 通法名 = "一键修复 编译错误**没有**一键方案(界面据此变灰,不假装有);支持规则表与检查器对得上";
            try
            {
                var 编译错 = new 诊断项C { 行 = 1, 级别 = 诊断级别.错误, 规则 = "编译错误", 文本 = "少一个分号" };
                bool 空 = 一键修复C.算(编译错, "整数 初值 = 1\n").Count == 0;
                bool 表齐 = 一键修复C.支持规则.Length >= 8
                    && 一键修复C.支持规则.Contains("行尾空格") && 一键修复C.支持规则.Contains("死代码");
                记录(通法名, 空 && 表齐, $"编译错误 → {一键修复C.算(编译错, "整数 初值 = 1\n").Count} 个方案;支持规则 {一键修复C.支持规则.Length} 条");
            }
            catch (Exception ex) { 记录异常(通法名, ex); }

            // ---- ⑥ 缩进归一与多行插入的小口径 ----
            const string 口径名 = "一键修复 缩进归一按 4 格就近(`\\t` 算 4);同一行多处改动按列从大到小应用(不会互相带偏)";
            try
            {
                // 归一的形状(逐条钉死,别用一长串 && —— 上一版就是那么写,红了看不出是哪一条)
                var 缩进样 = new (string 入, string 期望)[]
                {
                    ("\t", "    "),                 // 一个制表位 = 4 格
                    ("\t\t", "        "),           // 两个 = 8 格
                    ("\t ", "    "),                // 混着写也归一
                    ("    ", "    "),               // 4 格原样
                    ("        ", "        "),       // 8 格原样
                    ("      ", "    "),             // 6 格 → 就近到 4
                    ("       ", "        "),        // 7 格 → 就近到 8
                };
                var 缩进错 = 缩进样.Where(x => 一键修复C.归一缩进(x.入) != x.期望)
                    .Select(x => $"`{x.入.Replace("\t", "\\t")}` → {一键修复C.归一缩进(x.入).Length} 格(期望 {x.期望.Length})").ToArray();
                bool 缩进对 = 缩进错.Length == 0;
                string 两处 = 一键修复C.应用("左右中下;\n", [new 修复处C(1, 1, 1, "X"), new 修复处C(1, 3, 1, "Y")]) ?? "";
                bool 两处对 = 两处 == "左X中Y;\n";
                记录(口径名, 缩进对 && 两处对,
                    (缩进对 ? $"缩进归一 {缩进样.Length} 种写法全对" : "缩进归一不对:" + string.Join(" / ", 缩进错))
                    + $";两处替换 → `{两处.Replace("\n", "\\n")}`(期望 `左X中Y;\\n`)= {两处对}");
            }
            catch (Exception ex) { 记录异常(口径名, ex); }

            // ---- ⑦ 接线(接线哨兵) ----
            const string 接线名 = "一键修复 接线:问题行带源码 + 右键菜单按有没有方案开关 + 先预览再落 `页.内容` + 不自动存盘";            string? 根 = 找仓库根目录();
            if (根 == null) 跳过接线(接线名, "工作副本之外跑自检,读不到源码");
            else
            {
                try
                {
                    string 问题源 = File.ReadAllText(Path.Combine(根, "MainWindow.问题表与重命名.cs"));
                    string 检查源 = File.ReadAllText(Path.Combine(根, "MainWindow.代码检查.cs"));
                    string 界面 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml"));
                    string[] 名字 = ["问题行带源码", "右键开关", "预览后落内容", "不自动存盘", "重查一遍", "菜单挂着"];
                    bool[] 条 =
                    [
                        检查源.Contains("public string 源码 { get; set; } = \"\";", StringComparison.Ordinal)
                            && 检查源.Contains("_问题诊断[问题行] = d;", StringComparison.Ordinal),
                        问题源.Contains("菜单修这一条.IsEnabled = 可修;", StringComparison.Ordinal),
                        问题源.Contains("页.内容 = 新文本;", StringComparison.Ordinal)
                            && 问题源.Contains("一键修复C.应用(行.源码, 方案.改动)", StringComparison.Ordinal),
                        !问题源.Contains("保存标签页(页", StringComparison.Ordinal),
                        问题源.Contains("安排检查();", StringComparison.Ordinal),
                        界面.Contains("x:Name=\"菜单修这一条\"", StringComparison.Ordinal)
                            && 界面.Contains("ContextMenuOpening=\"问题表_右键\"", StringComparison.Ordinal),
                    ];
                    var 缺 = Enumerable.Range(0, 条.Length).Where(i => !条[i]).Select(i => 名字[i]).ToArray();
                    记接线(接线名, 缺.Length == 0,
                        缺.Length == 0
                            ? "问题行带着那一次的源码;右键按有没有方案开关;确认后走 `页.内容`(可撤销、不自动存盘);改完重查一遍"
                            : $"{条.Length} 处里差了 {缺.Length} 处:{string.Join(" / ", 缺)}");
                }
                catch (Exception ex) { 记录异常(接线名, ex); }
            }
        }

        /// <summary>
        /// 把"插进去那一行注释"从文本里去掉,剩下的应当与原文**逐字相同** ——
        /// 用来钉住"只插一行、别的什么都不动"(行尾按 `\r\n` / `\n` 两种都试)。
        /// </summary>
        private static string 去掉插进去的注释(string 文本, string 注释)
        {
            string 文 = (文本 ?? "").Replace(注释 + "\r\n", "", StringComparison.Ordinal)
                                     .Replace(注释 + "\n", "", StringComparison.Ordinal);
            return 文;
        }
    }
}
