using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **调试器那两件加强**(P34 / P35,2026-10-07;用户排的七件里的第 2 件:
    /// "监视表达式 + 条件断点加强(命中次数 / 日志断点)")。
    ///
    /// 这一组钉的都是**纯函数**(真喂参数真算,不是读源码文本):
    ///   ① **命中次数**:三种写法(第 N 次 / 每 N 次 / 第 N 次起)各自的边界逐个数一遍;
    ///      写坏(`0`、`abc`、`%-3`)不许当"合法"混过去;
    ///   ② **跟另一个变量比**:`左 &lt; 右` 这种两变量比(P34 起的写法)—— 看不清 / 比不了各给一句人话;
    ///   ③ **日志断点**:`{变量}` / `{行}` / `{文件}` 的展开;名字看不见时**原样留着 + 补一句为什么**(不许打假值);
    ///   ④ **三项合起来**:`断点判定C.从断点` 把 `条件 / 命中次数 / 日志` 一起读出来,日志断点自报身份;
    ///   ⑤ **监视表达式求值**:变量 / 比较 / 两变量比各算一遍,值变了才记历史;
    ///   ⑥ **接线**(接线哨兵):断点表多了三格并跟着工程存、编辑器画空心圈、
    ///      求值函数挂在调试会话上、监视那一页真在 XAML 里。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 调试器加强自检()
        {
            // ---- ① 命中次数:三种写法 + 边界 + 坏写法 ----
            const string 次数名 = "调试加强 命中次数三种写法(第 N 次 / 每 N 次 / 第 N 次起)逐次判对,坏写法不当合法";
            try
            {
                var 第5 = 命中次数条件C.解析("5");
                var 每5 = 命中次数条件C.解析("%5");
                var 起5 = 命中次数条件C.解析(">=5");
                var 空前 = 命中次数条件C.解析("");
                bool 第5对 = 第5.合法 && 第5.种类 == "等于" && !第5.满足(4) && 第5.满足(5) && !第5.满足(6);
                bool 每5对 = 每5.合法 && 第5.种类 == "等于" && !每5.满足(4) && 每5.满足(5) && !每5.满足(6) && 每5.满足(10);
                bool 起5对 = 起5.合法 && !起5.满足(4) && 起5.满足(5) && 起5.满足(6) && 起5.满足(100);
                bool 空对 = 空前.合法 && 空前.满足(1) && 空前.满足(999);
                var 坏 = new[] { "0", "abc", "%-3", "=-1", ">=x" }.Select(命中次数条件C.解析).ToArray();
                bool 坏对 = 坏.All(x => !x.合法);
                记录(次数名, 第5对 && 每5对 && 起5对 && 空对 && 坏对,
                    $"`5` → {第5.说明};`%5` → {每5.说明};`>=5` → {起5.说明};空 → {空前.说明};" +
                    $"坏写法 {坏.Count(x => !x.合法)}/{坏.Length} 条认出来了");
            }
            catch (Exception ex) { 记录异常(次数名, ex); }

            // ---- ② 两变量比:解析 / 合法 / 说明 ----
            const string 两变量名 = "调试加强 条件可以写『跟另一个变量比』(`左 < 右`),常量那一档照旧";
            try
            {
                var 两 = 断点条件求值C.解析("左 < 右");
                var 一 = 断点条件求值C.解析("i == 5");
                var 裸 = 断点条件求值C.解析("计数");
                var 坏 = 断点条件求值C.解析("i ==");
                记录(两变量名,
                    两.合法 && 两.跟变量比 && 两.变量 == "左" && 两.另一个变量 == "右" && 两.运算符 == "<"
                    && 一.合法 && !一.跟变量比 && 一.常量原文 == "5"
                    && 裸.合法 && 裸.运算符.Length == 0
                    && !坏.合法,
                    $"`左 < 右` → {两};`i == 5` → {一};`计数` → {裸};`i ==` → {坏.说明}");
            }
            catch (Exception ex) { 记录异常(两变量名, ex); }

            // ---- ③ 日志断点:占位展开(含"看不见时原样留着 + 说原因") ----
            const string 日志名 = "调试加强 日志断点:占位展开认变量 / {行} / {文件};看不见的原样留着并说明原因(不打假值)";
            try
            {
                var 读数 = new List<变量读数C>
                {
                    new() { 名字 = "i", 类型名 = "整数", 值 = 7, 值文本 = "7", 可读 = true, 位置文本 = "全局" },
                    new() { 名字 = "和", 类型名 = "整数", 值 = 12, 值文本 = "12", 可读 = true, 位置文本 = "全局" },
                    new() { 名字 = "看不见的", 类型名 = "整数", 值 = 0, 值文本 = "?", 可读 = false },
                };
                string 全 = 日志断点C.展开("到第 {i} 轮,和 = {和}(在第 {行} 行 / {文件})", 读数, 42, "主程序.hb");
                string 缺 = 日志断点C.展开("计数 = {计数}", 读数, 3, "主程序.hb");
                string 读不到 = 日志断点C.展开("备用计数 = {看不见的}", 读数, 3, "主程序.hb");
                var 用到 = 日志断点C.用到的名字("x {计数} y {记录.z} {行} {文件} {计数}");
                记录(日志名,
                    全 == "到第 7 轮,和 = 12(在第 42 行 / 主程序.hb)"
                    && 缺.StartsWith("计数 = {计数}", StringComparison.Ordinal) && 缺.Contains("看不见", StringComparison.Ordinal)
                    && 读不到.StartsWith("备用计数 = {看不见的}", StringComparison.Ordinal) && 读不到.Contains("读不到", StringComparison.Ordinal)
                    && 用到.Count == 2 && 用到[0] == "计数" && 用到[1] == "记录.z",
                    $"全占位 → {全};名字看不见 → {缺};值读不到 → {读不到};用到的名字 = {string.Join(",", 用到)}");
            }
            catch (Exception ex) { 记录异常(日志名, ex); }

            // ---- ④ 三项合起来:从断点项算判定 + 日志断点自报身份 + 摘要 ----
            const string 判定名 = "调试加强 一个断点的三样(条件 / 命中次数 / 日志)一起读出来;日志断点自报『只打印不停』";
            try
            {
                var 普通 = new 断点项C { 文件 = "主程序.hb", 行 = 3 };
                var 带 = new 断点项C { 文件 = "主程序.hb", 行 = 9, 条件 = "i == 5", 命中次数 = "%3" };
                var 日志 = new 断点项C { 文件 = "主程序.hb", 行 = 12, 日志 = "i = {i}" };
                var d普通 = 断点判定C.从断点(普通);
                var d带 = 断点判定C.从断点(带);
                var d日志 = 断点判定C.从断点(日志);
                记录(判定名,
                    !普通.带附加条件 && !d普通.是日志断点 && d普通.合法
                    && d带.合法 && d带.条件.合法 && d带.命中次数.种类 == "每" && !d带.是日志断点
                    && d日志.是日志断点 && d日志.日志 == "i = {i}"
                    && 带.附加摘要() == ":i == 5 每%3次" && 日志.附加摘要() == ":日志 i = {i}"
                    && !普通.带附加条件,
                    $"普通 → 附加 {普通.带附加条件};带条件 → {d带.说明};日志 → {d日志.说明};摘要 `{日志.附加摘要()}`");
            }
            catch (Exception ex) { 记录异常(判定名, ex); }

            // ---- ⑤ 监视表达式:变量 / 比较 / 两变量比 / 值变了才记历史 ----
            const string 监视名 = "调试加强 监视表达式求值:变量给值、比较给真假、两变量比给真假;值变了才记进变化历史";
            try
            {
                var 读数 = new List<变量读数C>
                {
                    new() { 名字 = "i", 类型名 = "整数", 值 = 7, 值文本 = "7", 可读 = true, 位置文本 = "全局" },
                    new() { 名字 = "上界", 类型名 = "整数", 值 = 10, 值文本 = "10", 可读 = true, 位置文本 = "全局" },
                };
                var 看变量 = 监视表达式C.解析("i");
                var 看比较 = 监视表达式C.解析("i < 上界");
                var 看常量 = 监视表达式C.解析("i == 7");
                var 看不存在 = 监视表达式C.解析("没有这个");
                var g变量 = 监视求值C.求值(看变量, 读数, 4);
                var g比较 = 监视求值C.求值(看比较, 读数, 4);
                var g常量 = 监视求值C.求值(看常量, 读数, 4);
                var g不存在 = 监视求值C.求值(看不存在, 读数, 4);
                看变量.记历史(g变量.值文本);
                看变量.记历史(g变量.值文本);      // 同一个值:不记第二条
                看变量.记历史("9");
                记录(监视名,
                    g变量.算出来了 && g变量.值文本 == "7" && g变量.类型 == "整数"
                    && g比较.算出来了 && g比较.值文本 == "真" && g比较.说明.Contains("i = 7", StringComparison.Ordinal)
                    && g常量.算出来了 && g常量.值文本 == "真"
                    && !g不存在.算出来了 && g不存在.值文本 == "(判不了)" && g不存在.说明.Contains("看不见", StringComparison.Ordinal)
                    && 看变量.历史.Count == 2 && 看变量.历史[0] == "9" && 看变量.历史文本() == "7 → 9",
                    $"`i` → {g变量.值文本};`i < 上界` → {g比较.值文本}({g比较.说明});`i == 7` → {g常量.值文本};" +
                    $"`没有这个` → {g不存在.说明};变化 = {看变量.历史文本()}");
            }
            catch (Exception ex) { 记录异常(监视名, ex); }

            // ---- ⑥ 接线(接线哨兵):三格跟着工程存 + 编辑器空心圈 + 挂上调试会话 + 监视页在 XAML 里 ----
            const string 接线名 = "调试加强 接线:断点三项跟着 断点.hbd 存、编辑器画空心圈、求值挂调试会话、监视页在 XAML 里";
            string? 根 = 找仓库根目录();
            if (根 == null) 跳过接线(接线名, "工作副本之外跑自检,读不到源码");
            else
            {
                try
                {
                    string 表源 = File.ReadAllText(Path.Combine(根, "引擎", "断点表C.cs"));
                    string 断点源 = File.ReadAllText(Path.Combine(根, "MainWindow.调试与断点.cs"));
                    string 边距源 = File.ReadAllText(Path.Combine(根, "界面控件", "断点边距.cs"));
                    string 界面 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml"));
                    string 选项源 = File.ReadAllText(Path.Combine(根, "配置", "选项.cs"));
                    记接线(接线名,
                        表源.Contains("public string 命中次数 { get; set; } = \"\";", StringComparison.Ordinal)
                        && 表源.Contains("public string 日志 { get; set; } = \"\";", StringComparison.Ordinal)
                        && 表源.Contains("public bool 设置(string 文件, int 行, string 条件, string 命中次数, string 日志)", StringComparison.Ordinal)
                        && 断点源.Contains("_断点判定表 = _断点条件表.ToDictionary", StringComparison.Ordinal)
                        && 断点源.Contains("前端契约C.服务.调试.会话断点命中判断 = 断点条件满足;", StringComparison.Ordinal)
                        && 断点源.Contains("lock (_日志锁) _待刷日志.Enqueue(行);", StringComparison.Ordinal)
                        && 断点源.Contains("设断点(全称, 行, 设置.条件, 设置.命中次数, 设置.日志)", StringComparison.Ordinal)
                        && 边距源.Contains("dc.DrawEllipse(null, new Pen(红刷, 1.6)", StringComparison.Ordinal)
                        && 界面.Contains("x:Name=\"监视页\"", StringComparison.Ordinal)
                        && 界面.Contains("KeyDown=\"监视输入框_KeyDown\"", StringComparison.Ordinal)
                        && 选项源.Contains("public List<string> 监视 { get; set; } = [];", StringComparison.Ordinal),
                        "断点.hbd 三格(条件/命中次数/日志)+ 编辑器空心圈 + 命中判断挂会话 + 日志走队列 + 监视页");
                }
                catch (Exception ex) { 记录异常(接线名, ex); }
            }
        }
    }
}
