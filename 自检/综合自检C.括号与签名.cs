using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **括号配对高亮 + 签名提示**(P37,2026-10-07;用户排的七件里的第 4 件:
    /// "编辑器手感这两条最显眼")。
    ///
    /// 这一组钉七件事(前六条真跑纯函数,第七条按源码钉接线):
    ///   ① **配对**:`()[]{}` 三对各自找得到另一半;光标在左括号前 / 右括号后都算"贴着";
    ///   ② **不串门**:`f([1, 2])` 里数 `(` 不会被 `[` 的层数带偏;
    ///   ③ **注释与字符串里的括号不算**(`输出行("(")` 不该配出一个框);
    ///   ④ **找不到就不画**(没写完的括号返回 null,不硬指一个地方);
    ///   ⑤ **第几个参数**:顶层逗号才计数,嵌套里的逗号不算;注释/字符串里的逗号也不算;
    ///   ⑥ **参数切段与显示名**:`整数 个数, 字符串 名[]` 切成两段、显示名取 `个数` / `名`;
    ///   ⑦ 接线:括号方框挂在背景层、光标一动就重算、签名表跟着编译结果灌、`(` 才弹。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 括号与签名自检()
        {
            // ---- ① 配对:三种括号 + 光标在左 / 右都算 ----
            const string 配对名 = "括号配对 三种括号各自找到另一半;光标停在括号前 / 括号后都算『贴着』";
            try
            {
                const string 文 = "方法 求和(整数 步数)\n{\n    如果 (步数 > 0) { 返回 [步数]; }\n}\n";
                int 左 = 文.IndexOf('(');
                var 左括号 = 括号配对C.找(文, 左 + 1);              // 光标在 `(` 后面
                var 右括号 = 括号配对C.找(文, 左);                  // 光标在 `(` 前面
                int 方左 = 文.IndexOf('[');
                var 方括号 = 括号配对C.找(文, 方左 + 1);
                int 花左 = 文.IndexOf('{');
                var 花括号 = 括号配对C.找(文, 花左 + 1);
                bool 对 = 左括号 is { } a1 && a1.左起 == 左 && 文[a1.右起] == ')'
                    && 右括号 is { } a2 && a2.左起 == 左
                    && 方括号 is { } a3 && 文[a3.左起] == '[' && 文[a3.右起] == ']'
                    && 花括号 is { } a4 && 文[a4.左起] == '{' && 文[a4.右起] == '}';
                记录(配对名, 对,
                    $"`(`@{左} → 另一半 @{左括号?.右起};光标在括号前也算 = {右括号 != null};" +
                    $"`[`@{方左} → @{方括号?.右起};`{{`@{花左} → @{花括号?.右起}");
            }
            catch (Exception ex) { 记录异常(配对名, ex); }

            // ---- ② 不串门 + ③ 注释与字符串里的不算 + ④ 找不到就不画 ----
            const string 边界名 = "括号配对 不串门(`f([1,2])` 数对)、注释与字符串里的括号不算、没写完就不画";
            try
            {
                const string 文 = "整数 总数 = 计算(取(1, 2), [3, 4]);\n输出行(\"(\"); // 这里有个 (\n整数 合计 = (1 + 2;\n";
                int 外左 = 文.IndexOf('(');                       // `计算(` 那个开括号(最外那一对)
                var 外 = 括号配对C.找(文, 外左 + 1);
                int 串里 = 文.IndexOf("\"(\"", StringComparison.Ordinal) + 1;   // 字符串里那个 `(`
                var 串 = 括号配对C.找(文, 串里 + 1);
                int 注释里 = 文.IndexOf("// 这里", StringComparison.Ordinal) + 6;  // 注释里那个 `(`
                var 注 = 括号配对C.找(文, 注释里 + 1);
                int 没写完 = 文.IndexOf("(1 + 2", StringComparison.Ordinal);
                var 缺 = 括号配对C.找(文, 没写完 + 1);
                // 最外那一对:左括号就是 `计算(` 那个、右括号在本行末尾的 `);` 上(不是被 `[3, 4]` 的 `]` 顶掉)
                bool 对 = 外 is { } a && a.左起 == 外左 && 文[a.右起] == ')' && 文[a.右起 + 1] == ';'
                    && 串 == null && 注 == null && 缺 == null;
                记录(边界名, 对,
                    $"最外那一对 → @{外?.左起}…@{外?.右起}(后面紧跟 `{文[Math.Min((外?.右起 ?? 0) + 1, 文.Length - 1)]}`);" +
                    $"字符串里的 `(` → {(串 == null ? "不画" : "画了")};注释里的 `(` → {(注 == null ? "不画" : "画了")};" +
                    $"没写完整的 `(` → {(缺 == null ? "不画" : "画了")}");
            }
            catch (Exception ex) { 记录异常(边界名, ex); }

            // ---- ⑤ 第几个参数:顶层逗号才计数 ----
            // 样本 `显示(取(1, 2), "左,右", 3);`:顶层只有**两个**逗号(`取(1, 2)` 后面那个、`"左,右"` 后面那个),
            // 段里那两个逗号(嵌套的、字符串里的)一个都不许算进去。
            const string 参数序名 = "签名提示 第几个参数:只有顶层逗号计数(嵌套 / 注释 / 字符串里的逗号不算)";
            try
            {
                const string 文 = "显示(取(1, 2), \"左,右\", 3);";
                int 左 = 文.IndexOf('(');
                int 一 = 文.IndexOf('取');
                int 串后 = 文.IndexOf("\"左,右\"", StringComparison.Ordinal) + 5;
                int 三 = 文.IndexOf(", 3");
                int 尾 = 文.LastIndexOf(')');
                bool 对 = 签名提示规则C.第几个参数(文, 左, 一 + 1) == 0
                    && 签名提示规则C.第几个参数(文, 左, 串后) == 1     // `"左,右"` 走完,还在第 2 个参数上
                    && 签名提示规则C.第几个参数(文, 左, 三 + 1) == 2
                    && 签名提示规则C.第几个参数(文, 左, 尾) == 2
                    && 签名提示规则C.第几个参数(文, 左, 左 + 1) == 0;
                记录(参数序名, 对,
                    $"第一个参数上 → {签名提示规则C.第几个参数(文, 左, 一 + 1)};" +
                    $"字符串走完 → {签名提示规则C.第几个参数(文, 左, 串后)};" +
                    $"`3` 前面 → {签名提示规则C.第几个参数(文, 左, 三 + 1)};" +
                    $"末尾 → {签名提示规则C.第几个参数(文, 左, 尾)}");
            }
            catch (Exception ex) { 记录异常(参数序名, ex); }

            // ---- ⑥ 正在调用 + 参数切段 + 显示名 ----
            const string 调用名 = "签名提示 认出『正在调用的名字』(带模块前缀也认),参数切段与显示名都对";
            try
            {
                const string 文 = "整数 和 = 数学.求和(1 + 2, 名);\n";
                int 位 = 文.IndexOf('名');
                var 呼 = 签名提示规则C.正在调用(文, 位);
                var 段 = 签名提示规则C.切参数("整数 个数, 字符串 名[], 出 整数 和");
                var 显示 = 段.Select(签名提示规则C.参数显示名).ToArray();
                bool 对 = 呼 is { } h && h.名字 == "数学.求和"
                    && 段.Count == 3 && 显示[0] == "个数" && 显示[1] == "名" && 显示[2] == "和"
                    && 签名提示规则C.切参数("").Count == 0
                    && 签名提示规则C.切参数("整数 个数").Count == 1;
                记录(调用名, 对,
                    $"正在调用 `{呼?.名字 ?? "(没认出来)"}`;切段 {段.Count} 段 → 显示名 {string.Join("/", 显示)}");
            }
            catch (Exception ex) { 记录异常(调用名, ex); }

            // ---- ⑦ 签名表:与补全同一份事实源(内建函数取的是说明表里那句) ----
            const string 签名表名 = "签名提示 表从『名字表 + 说明表』建出来:内建函数有说明、说明过长会截断";
            try
            {
                var 名字表 = new 名字表C();
                名字表.函数签名["数学.求和"] = new 函数签名C
                {
                    返回类型 = "整数",
                    参数 = [new 参数项C { 名字 = "个数", 类型名 = "整数" }, new 参数项C { 名字 = "步数", 类型名 = "整数", 是出 = true }],
                };
                var 表 = 签名表C.建([名字表]);
                var 求和 = 表.FirstOrDefault(x => x.名字 == "数学.求和");
                var 读取行 = 表.FirstOrDefault(x => x.名字 == "读取行");
                var 长度 = 表.FirstOrDefault(x => x.名字 == "长度");
                bool 对 = 求和 != null && 求和.参数段.Count == 2 && 求和.参数段[1] == "整数 步数 出"
                    && 求和.返回类型 == "整数" && 求和.显示括号
                    && 读取行 != null && 读取行.说明.Length > 0
                    && 长度 != null && 长度.是方法 && !长度.显示括号
                    && 签名表C.截断(new string('字', 500)).Length <= 签名表C.说明上限 + 1
                    && 表.Select(x => x.名字).Distinct(StringComparer.Ordinal).Count() == 表.Count;
                记录(签名表名, 对,
                    $"`数学.求和` → {求和?.一行}(参数 {string.Join(" | ", 求和?.参数段 ?? [])});" +
                    $"`读取行` 说明 {读取行?.说明.Length ?? 0} 字;`长度` 是方法 = {长度?.是方法};共 {表.Count} 条不重名");
            }
            catch (Exception ex) { 记录异常(签名表名, ex); }

            // ---- ⑧ 界面接线(接线哨兵) ----
            const string 接线名 = "括号与签名 接线:方框挂背景层 + 光标一动就重算 + 签名表跟着编译结果灌 + 只在 `(` 后弹";
            string? 根 = 找仓库根目录();
            if (根 == null) 跳过接线(接线名, "工作副本之外跑自检,读不到源码");
            else
            {
                try
                {
                    string 括号源 = File.ReadAllText(Path.Combine(根, "界面控件", "编辑器.括号与签名.cs"));
                    string 方框源 = File.ReadAllText(Path.Combine(根, "界面控件", "括号方框.cs"));
                    string 初始化 = File.ReadAllText(Path.Combine(根, "界面控件", "编辑器.初始化与交互.cs"));
                    string 界面 = File.ReadAllText(Path.Combine(根, "界面控件", "编辑器.xaml"));
                    string 检查 = File.ReadAllText(Path.Combine(根, "MainWindow.代码检查.cs"));
                    string 补全 = File.ReadAllText(Path.Combine(根, "界面控件", "编辑器.补全与外观.cs"));
                    bool[] 条 =
                    [
                        初始化.Contains("装上括号与签名();", StringComparison.Ordinal),
                        括号源.Contains("代码编辑器.TextArea.TextView.BackgroundRenderers.Add(new 括号方框(() => _括号对));", StringComparison.Ordinal),
                        方框源.Contains("KnownLayer.Selection", StringComparison.Ordinal),
                        方框源.Contains("AddSegment(textView, 段)", StringComparison.Ordinal),
                        括号源.Contains("代码编辑器.TextArea.Caret.PositionChanged += (_, _) => 刷新括号与签名();", StringComparison.Ordinal),
                        括号源.Contains("签名提示规则C.正在调用(文, 位)", StringComparison.Ordinal),
                        括号源.Contains("签名提示规则C.第几个参数(文, 呼.左括号位置, 位)", StringComparison.Ordinal),
                        界面.Contains("x:Name=\"签名Popup\"", StringComparison.Ordinal),
                        界面.Contains("x:Name=\"签名文本\"", StringComparison.Ordinal),
                        检查.Contains("编辑器.设签名表(签名表C.建(表们));", StringComparison.Ordinal),
                        补全.Contains("刷新括号与签名();", StringComparison.Ordinal),
                    ];
                    string[] 名字 = ["初始化调装上", "初始化挂方框", "方框层", "方框画段", "光标重算", "正在调用", "第几个参数",
                                      "签名Popup", "签名文本", "灌签名表", "文本变重算"];
                    var 缺的 = Enumerable.Range(0, 条.Length).Where(i => !条[i]).Select(i => 名字[i]).ToArray();
                    记接线(接线名, 缺的.Length == 0,
                        缺的.Length == 0
                            ? "方框挂 BackgroundRenderers(KnownLayer.Selection)+ 光标 PositionChanged 重算 + " +
                              "签名走 `正在调用` / `第几个参数` + 编译结果一换就重灌表"
                            : $"{条.Length} 处接线里差了 {缺的.Length} 处:{string.Join(" / ", 缺的)}");
                }
                catch (Exception ex) { 记录异常(接线名, ex); }
            }
        }
    }
}
