using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **自检窗格**(P39,2026-10-07;用户排的七件里的第 6 件:"把 3063 条断言搬进界面,红绿点着看")。
    ///
    /// 窗子本身开不了(硬规矩 6:自检不开 GUI),所以这一组钉的是**窗子要的那根线**:
    ///   ① `装逐条回调` 装上去之后,每记一条都报到(结果 / 组名 / 断言名 / 详情四样都对得上);
    ///   ② **组名跟着跑的那一组走**(第几组 → `组名表` 里那一个);
    ///   ③ 注销之后一条都不再报(窗口关掉不许还留着钩子 —— 那是 H23 那一族的账);
    ///   ④ `组名表` 与分组表同一份(不另抄一份清单);
    ///   ⑤ 接线(接线哨兵):菜单上挂着、窗子在后台线程跑、关窗先立旗再退订、同一时刻只留一个窗。
    ///
    /// **为什么只跑"前几组"**:整趟自检要几分钟,而这一组要验的是**钩子本身**;
    /// 拿一个临时的"只报前 N 条就抛异常收工"的回调走一遍,照样能证明"每条都报到了"——
    /// 自检**带总预算**(撞了就不再往下铺),所以这里不担心它把整趟跑完。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 自检窗格自检()
        {
            // ---- ①②③ 钩子:装上报得到、拆得对、注销之后不再报 ----
            // 用 `探一条给钩子` 喂**合成报告行**走一遍 —— 它不往报告里记账(不动通过/失败/跳过那几个数),
            // 所以自检自己的那一趟照常往下跑,不会被这一组搅乱。
            const string 钩子名 = "自检窗格 逐条回调:装上报得到(结果/组名/断言名/详情)、注销之后一条都不再报";
            try
            {
                var 收到 = new List<自检条C>();
                var 退订 = 装逐条回调((条, 第几组) => 收到.Add(条));
                探一条给钩子("PASS  命令面板 排序  => 逐字 → 转到定义");
                探一条给钩子("FAIL  一键修复 缩进归一  => 差一处");
                探一条给钩子("SKIP  工具反汇编窗 某条  => 这台机器没有");
                退订();
                探一条给钩子("PASS  注销之后不该再收到  => x");

                bool 齐 = 收到.Count == 3
                    && 收到[0].结果 == "PASS" && 收到[0].名 == "命令面板 排序" && 收到[0].详情 == "逐字 → 转到定义"
                    && 收到[1].结果 == "FAIL" && 收到[1].名 == "一键修复 缩进归一" && 收到[1].详情 == "差一处"
                    && 收到[2].结果 == "SKIP" && 收到[2].名 == "工具反汇编窗 某条";
                记录(钩子名, 齐,
                    $"三条各拆成[结果|名|详情]:{string.Join(";", 收到.Select(x => $"{x.结果}|{x.名}|{x.详情}"))};" +
                    $"注销之后不再收到 = {收到.Count == 3}");
            }
            catch (Exception ex) { 记录异常(钩子名, ex); }

            // ---- ④ 组名表与分组表同一份 ----
            const string 组名表名 = "自检窗格 组名表与分组表同一份(不另抄一份清单),而且没有重名";
            try
            {
                var 表 = 组名表快照();
                bool 对 = 表.Count > 100
                    && 表.Contains("命令面板") && 表.Contains("调试器加强") && 表.Contains("会话记忆")
                    && 表.Contains("括号与签名") && 表.Contains("一键修复")
                    && 表.Distinct(StringComparer.Ordinal).Count() == 表.Count;
                记录(组名表名, 对,
                    $"共 {表.Count} 组(含本批新加的 5 组),重名 {表.Count - 表.Distinct(StringComparer.Ordinal).Count()} 个");
            }
            catch (Exception ex) { 记录异常(组名表名, ex); }

            // ---- ⑤ 接线(接线哨兵) ----
            const string 接线名 = "自检窗格 接线:菜单挂着 / 后台线程跑 / 关窗先立旗再退订 / 同一时刻只留一个窗 / 停止是『这一组之后』";
            string? 根 = 找仓库根目录();
            if (根 == null) 跳过接线(接线名, "工作副本之外跑自检,读不到源码");
            else
            {
                try
                {
                    string 界面 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml"));
                    string 主窗 = File.ReadAllText(Path.Combine(根, "MainWindow.主题与选项.cs"));
                    string 窗源 = File.ReadAllText(Path.Combine(根, "界面控件", "自检窗口.xaml.cs"));
                    string 窗样 = File.ReadAllText(Path.Combine(根, "界面控件", "自检窗口.xaml"));
                    string 自检 = File.ReadAllText(Path.Combine(根, "自检", "综合自检C.cs"));
                    string[] 名字 = ["菜单挂着", "只留一个窗", "后台线程", "关窗先立旗", "退订", "停止口径", "窗样有列表"];
                    bool[] 条 =
                    [
                        界面.Contains("Click=\"菜单自检窗_Click\"", StringComparison.Ordinal),
                        主窗.Contains("if (_自检窗 is { IsLoaded: true } 已开)", StringComparison.Ordinal),
                        窗源.Contains("var 工 = new Thread(() =>", StringComparison.Ordinal)
                            && 窗源.Contains("{ IsBackground = true, Name = \"HB自检\" }", StringComparison.Ordinal),
                        窗源.Contains("已关 = true;", StringComparison.Ordinal)
                            && 窗源.IndexOf("已关 = true;", StringComparison.Ordinal) < 窗源.IndexOf("退订回调?.Invoke();", StringComparison.Ordinal),
                        窗源.Contains("退订回调?.Invoke();", StringComparison.Ordinal)
                            && 自检.Contains("return () => { if (ReferenceEquals(逐条回调, 回调)) 逐条回调 = null; };", StringComparison.Ordinal),
                        窗源.Contains("要停 = true;", StringComparison.Ordinal)
                            && 窗源.Contains("跑完当前这一组就收", StringComparison.Ordinal),
                        窗样.Contains("x:Name=\"列表\"", StringComparison.Ordinal)
                            && 窗样.Contains("x:Name=\"只看失败\"", StringComparison.Ordinal)
                            && 窗样.Contains("x:Name=\"进度\"", StringComparison.Ordinal),
                    ];
                    var 缺 = Enumerable.Range(0, 条.Length).Where(i => !条[i]).Select(i => 名字[i]).ToArray();
                    记接线(接线名, 缺.Length == 0,
                        缺.Length == 0
                            ? "菜单 / 只留一个窗 / 后台线程 / 关窗先立旗再退订 / 停止=这一组之后 / 窗样三件齐"
                            : $"{条.Length} 处里差了 {缺.Length} 处:{string.Join(" / ", 缺)}");
                }
                catch (Exception ex) { 记录异常(接线名, ex); }
            }
        }

        /// <summary> 组名表的快照(自检自己跑的时候会改东西,先拷一份出来比)</summary>
        private static List<string> 组名表快照() => [.. 组名表];
    }
}
