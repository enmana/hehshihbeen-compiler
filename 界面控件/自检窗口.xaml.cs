using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// **自检窗格**(P39,2026-10-07;用户排的七件里的第 6 件:"把 3063 条断言搬进界面,红绿点着看")。
    ///
    /// 它做的事只有三件:
    ///   ① 在**后台线程**上跑一遍 `综合自检C.运行`(界面不冻 —— 一跑几分钟,冻住就没法看了);
    ///   ② 每出一条就画一行(`综合自检C.装逐条回调`,动作派回界面线程);
    ///   ③ 跑完给一行摘要(通过 / 失败 / 跳过 + 耗时),失败的那几条一眼看得见。
    ///
    /// 两条边界(照这个仓库的老规矩):
    ///   · **自检不被打断** —— "停止"是"跑完当前这一组就不再往下跑了",不是"立刻掐掉"(它改的是共享状态);
    ///   · 关窗**先停**(`_已关`)再退订回调 —— 不然那一条条会往一个已经关掉的窗口里塞。
    /// </summary>
    public partial class 自检窗口 : Window
    {
        /// <summary>
        /// 列表里的一行:自检那一条(结果 / 组 / 断言名 / 详情)。
        /// **它是个不变的容器** —— 一行造出来就定死了(不像变量窗格那样原地改值),
        /// 所以**不实现 `INotifyPropertyChanged`**(实现了也永远不触发,白留一个事件)。
        /// </summary>
        public sealed class 自检行C
        {
            public string 结果 { get; init; } = "";
            public string 组 { get; init; } = "";
            public string 名 { get; init; } = "";
            public string 详情 { get; init; } = "";

            /// <summary> 结果那一格前面加一个圆点(绿 / 红 / 灰),扫一眼就知道哪几条不对</summary>
            public string 显示结果 => 结果 switch
            {
                "PASS" => "● 通过",
                "FAIL" => "● 失败",
                "SKIP" => "○ 跳过",
                _ => 结果,
            };
        }

        private readonly ObservableCollection<自检行C> 全部行 = [];
        private readonly List<自检行C> 源行 = [];
        /// <summary> 报告里第 N 行属于哪一组(回调按"第几组"攒下来;收尾重画时照它填)</summary>
        private readonly List<string> 行组名 = [];
        private Action? 退订回调;
        private volatile bool 要停;
        private volatile bool 已关;
        private bool 在跑;
        private int 组数;
        private DateTime 开跑时刻;

        public 自检窗口()
        {
            InitializeComponent();
            列表.ItemsSource = 全部行;
            Closed += (_, _) =>
            {
                已关 = true;          // 先立旗:后台那条线再往这儿派就不再动界面了
                要停 = true;
                退订回调?.Invoke();
                退订回调 = null;
            };
        }

        /// <summary> 主窗口那边"跑自检"那个菜单项现在还开着的老窗口:有没有正在跑(没跑就不必再开一个) </summary>
        public bool 正在跑 => 在跑;

        private void 跑_Click(object sender, RoutedEventArgs e) => 开跑();

        private void 停_Click(object sender, RoutedEventArgs e)
        {
            要停 = true;
            摘要.Text = "已请求停止:跑完当前这一组就收(自检自己不会被打断)。";
        }

        private void 筛选_Changed(object sender, RoutedEventArgs e) => 重画();

        /// <summary>
        /// 开跑:装钩子 → 后台线程跑 → 每出一条派回界面线程画一行。
        /// **同一时刻只跑一趟**(再点一次不会起第二条)。
        /// </summary>
        public void 开跑()
        {
            if (在跑) { 摘要.Text = "这一趟还没跑完。"; return; }
            在跑 = true;
            要停 = false;
            源行.Clear();
            行组名.Clear();
            全部行.Clear();
            进度.Value = 0;
            跑按钮.IsEnabled = false;
            停按钮.IsEnabled = true;
            开跑时刻 = DateTime.Now;
            摘要.Text = "正在跑……(每条一出就画一行)";

            退订回调?.Invoke();
            退订回调 = 综合自检C.装逐条回调((条, 第几组) =>
            {
                if (已关) return;
                // 自检跑在别的线程上 ⇒ 一律派回界面线程再动控件
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (已关) return;
                    var 行 = new 自检行C { 结果 = 条.结果, 组 = 条.组, 名 = 条.名, 详情 = 条.详情 };
                    源行.Add(行);
                    行组名.Add(条.组);
                    if (该显示(行)) 全部行.Add(行);
                    if (源行.Count % 25 == 0) 更新进度(第几组);
                }), System.Windows.Threading.DispatcherPriority.Background);
            });

            var 工 = new Thread(() =>
            {
                自检报告? 报告 = null;
                string? 出错 = null;
                try { 报告 = 综合自检C.运行(); }
                catch (Exception ex) { 出错 = ex.Message; }
                if (已关) return;
                Dispatcher.BeginInvoke(new Action(() => 收尾(报告, 出错)));
            })
            { IsBackground = true, Name = "HB自检" };
            工.Start();
        }

        /// <summary> 跑完了:取消钩子 + 摘要 + 把没赶上的那些行补画一遍(派回界面的动作可能比收尾晚到) </summary>
        private void 收尾(自检报告? 报告, string? 出错)
        {
            退订回调?.Invoke();
            退订回调 = null;
            在跑 = false;
            跑按钮.IsEnabled = true;
            停按钮.IsEnabled = false;
            进度.Value = 100;

            // 钩子那条路是"派回界面线程"的,收尾这一刻可能还有几条排在路上 ⇒ 以**报告**为准重画一遍
            if (报告 != null && 报告.行.Count >= 源行.Count)
            {
                源行.Clear();
                for (int i = 0; i < 报告.行.Count; i++)
                {
                    var 条 = 拆报告行(报告.行[i]);
                    源行.Add(new 自检行C
                    {
                        结果 = 条.结果,
                        组 = i < 行组名.Count ? 行组名[i] : "",
                        名 = 条.名,
                        详情 = 条.详情,
                    });
                }
            }
            重画();

            var 用了 = DateTime.Now - 开跑时刻;
            if (出错 != null)
            {
                摘要.Text = $"自检自己抛异常了:{出错}(这一趟的结果不作数)";
                return;
            }
            if (报告 == null) { 摘要.Text = "自检没有返回结果。"; return; }
            string 结论 = 报告.失败 == 0 ? "全绿" : $"{报告.失败} 条失败";
            摘要.Text = $"通过 {报告.通过} · 失败 {报告.失败} · 跳过 {报告.跳过}(接线哨兵 {报告.接线哨兵} 条)" +
                       $" —— {结论};共 {组数} 组,耗时 {用了.TotalSeconds:F1} 秒" +
                       (要停 ? ";**中途停过**,这一趟只跑到一部分组" : "") + "。";
        }

        /// <summary> 报告里那一行拆成三段(与 `综合自检C.拆一条` 同一套;那边是私有的,这里只服务显示)</summary>
        private static (string 结果, string 名, string 详情) 拆报告行(string 行)
        {
            string 结果 = 行.StartsWith("PASS", StringComparison.Ordinal) ? "PASS"
                : 行.StartsWith("FAIL", StringComparison.Ordinal) ? "FAIL"
                : 行.StartsWith("SKIP", StringComparison.Ordinal) ? "SKIP" : "";
            string 余 = 结果.Length > 0 ? 行[结果.Length..].TrimStart() : 行;
            int 切 = 余.IndexOf("  => ", StringComparison.Ordinal);
            string 名 = 切 < 0 ? 余 : 余[..切];
            return (结果, 名.Trim(), 切 < 0 ? "" : 余[(切 + 5)..].Trim());
        }

        private bool 该显示(自检行C 行) =>
            只看失败.IsChecked != true || 行.结果 is "FAIL" or "SKIP";

        private void 更新进度(int 第几组)
        {
            // 总条数事先不知道(自检自己边跑边记)⇒ 用"跑到第几组 / 到了多少条"给个大概的进度感
            int 总组 = Math.Max(1, 综合自检C.组名表.Count);
            进度.Value = Math.Min(99, 第几组 * 100.0 / 总组);
            摘要.Text = $"正在跑……第 {第几组}/{总组} 组,已出 {源行.Count} 条(失败 {源行.Count(x => x.结果 == "FAIL")} 条)";
        }

        private void 重画()
        {
            全部行.Clear();
            foreach (var 行 in 源行)
                if (该显示(行)) 全部行.Add(行);
            组数 = 源行.Select(x => x.组).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).Count();
        }

        /// <summary> 双击一条:把它的详情(那一行 `=>` 后面那一段)弹出来看全 —— 列表那一列放不下长详情</summary>
        private void 列表_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (列表.SelectedItem is not 自检行C 行) return;
            string 文 = $"{行.显示结果}  {行.名}" + (行.组.Length > 0 ? $"\n组:{行.组}" : "")
                + (行.详情.Length > 0 ? $"\n\n{行.详情}" : "\n\n(这一条没有详情)");
            MessageBox.Show(this, 文, "自检那一条", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary> 复制现在显示着的这些行(Tab 分隔,粘出去能用)</summary>
        private void 复制_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (全部行.Count == 0) return;
                var 文 = string.Join("\n", 全部行.Select(x => $"{x.结果}\t{x.组}\t{x.名}\t{x.详情}"));
                Clipboard.SetText(文);
                摘要.Text = $"已复制 {全部行.Count} 行。";
            }
            catch (Exception 异常) { 摘要.Text = $"复制失败:{异常.Message}"; }
        }
    }
}
