using hehshihbeen_compiler.选卡控件;
using hehshihbeen_compiler.配置;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace hehshihbeen_compiler
{
    /// <summary> 主窗口的一部分:帮助菜单与综合自检 / 主题菜单 / 选项页 / 中英切换 / 主题与输出框命令 </summary>
    public partial class MainWindow : Window
    {
        #region 帮助菜单与综合自检

        // C25-4:帮助 = **用系统默认浏览器打开静态站点**(C25-3 生成到 `帮助\index.html`)。
        // 四条菜单项各自跳到对应的那一节(锚点就是 C25-2 生成的锚点,全树唯一);
        // 原来那个"四个只读文本框 + 搜索框"的 WPF 帮助窗口随之下线 —— 帮助只留**一处事实源**,
        // 免得网页与窗口两份文案互相打脸(C25-1 的核对表 `帮助核对表.md` 记的就是这套账)。
        private void 帮助快速上手_Click(object sender, RoutedEventArgs e) => 打开帮助站点("");
        private void 帮助指令速查_Click(object sender, RoutedEventArgs e) => 打开帮助站点(帮助站点C.指令节名);
        private void 帮助中间层速查_Click(object sender, RoutedEventArgs e) => 打开帮助站点(帮助站点C.关键字节名);
        private void 帮助关于_Click(object sender, RoutedEventArgs e) => 打开帮助站点("");

        /// <summary>
        /// 用系统默认浏览器打开帮助站点(带锚点跳转),**没生成过时给一句人话提示**(说清跑哪条命令)。
        /// <para>
        /// 第169批(C28)按用户实测的"点了没反应"改了两条口径:
        /// ① **先按注册表里的默认浏览器开**(`默认浏览器C.打开`),再退回系统 shell —— 原先只有 shell 那一条路,
        ///    而它是按 `.html` **文件关联**开的,那条关联一旦坏了(`AssocQueryString` 只回 `OpenWith.exe`,
        ///    `file` 协议回 `0x80070483`),`Process.Start` 就抛异常;
        /// ② **失败一律弹一句人话**(把文件路径一起给出来,让人能手工拷进浏览器)—— 原先只往底部『异常』页写一行,
        ///    那一页平时收着,写进去等于没说,这正是"点了没反应"的另一半。
        /// </para>
        /// 只有三种结局:没生成过(提示怎么生成)/ 起了浏览器 / 起不来(给出文件路径);异常一律不外逃。
        /// </summary>
        private void 打开帮助站点(string 锚点)
        {
            string 路径;
            string 网址;
            try
            {
                if (帮助站点C.找已生成站点() is not { } 找到)
                {
                    MessageBox.Show(this, 帮助站点C.没生成过的提示, "帮助",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                路径 = 找到;
                // 多页站点(第241批起):锚点先翻成"它在哪一页" —— `#锚点` 只在页内有效,
                // 首页上根本没有那些 id,直接喂 `index.html#类型` 会停在总目录不动。
                string? 站点目录 = Path.GetDirectoryName(路径);
                string 仓库根 = (站点目录 == null ? null : Directory.GetParent(站点目录))?.FullName ?? 站点目录 ?? "";
                路径 = 帮助站点C.页面文件(仓库根, 锚点);
                网址 = 帮助站点C.网址(路径);
            }
            catch (Exception 异常)
            {
                记异常框($"打开帮助站点失败: {异常.Message}");
                MessageBox.Show(this, $"算不出帮助站点在哪: {异常.Message}", "帮助",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (默认浏览器C.打开(网址)) return;

            try
            {
                Process.Start(new ProcessStartInfo(网址) { UseShellExecute = true });
            }
            catch (Exception 异常)
            {
                记异常框($"打开帮助站点失败: {异常.Message}");
                MessageBox.Show(this,
                    $"打不开帮助站点: {异常.Message}\n\n可以手工用浏览器打开这个文件:\n{路径}",
                    "帮助", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary> 工具 → 反汇编当前文件:把当前标签页的代码编译一遍,再显示机器码反汇编 </summary>
        private void 菜单反汇编_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (当前选卡 is not { } 页) { 输出框.Text = "先打开一个文件,再反汇编。\n"; 切到底部输出(); return; }
                byte[] 码 = 前端契约C.服务.编译.汇编单文件(页.内容 ?? "", 当前基目录());
                new 反汇编显示窗(工具.反汇编器C.反汇编(码)) { Owner = this }.Show();
            }
            catch (Exception 异常)
            {
                输出框.Text = $"反汇编失败: {异常.Message}\n";
                切到底部输出();
                记异常框($"反汇编失败: {异常}");
            }
        }

        /// <summary> 追加一行到"异常"页(不弹 MessageBox) </summary>
        private void 记异常框(string 文本)
        {
            try
            {
                if (异常框 == null) return;
                异常框.AppendText(文本.EndsWith('\n') ? 文本 : 文本 + "\n");
                if (异常框.Text.Length > 200_000) 异常框.Text = 异常框.Text[^100_000..];
            }
            catch { }
        }

        /// <summary> 底部页签切到"输出"(执行完看程序输出与断言) </summary>
        private void 切到底部输出() => 切到底部页(输出页);

        /// <summary> 底部页签切到"编译"(看编译信息) </summary>
        private void 切到底部编译() => 切到底部页(编译页);

        private void 切到底部页(TabItem 页)
        {
            try { 底部选卡.SelectedItem = 页; }
            catch { }
        }

        /// <summary>
        /// 综合自检(全指令 + 中间层 + 边界 + 示例回归)。
        /// <para>
        /// 第169批:整份自检**挪到后台线程**跑(`Task.Run`)—— 引擎侧那 20 多秒原来压在界面线程上,
        /// 点下去界面就冻住(连"正在运行…"那行都刷不出来),用户看到的就是"点了卡死"。
        /// 自检本身与界面无关:命令行 `hbcc --selftest` 跑的就是同一个 `综合自检C.运行()`,
        /// 所以它不碰任何界面对象,放后台是安全的;跑完再回到界面线程把结果写进两个框。
        /// 跑的时候菜单项**先禁用**,免得连点两次把两份自检叠在一起(两遍都在写同一批静态名字表)。
        /// </para>
        /// </summary>
        /// <summary>
        /// **自检窗格**(P39,2026-10-07):用户排的第 6 件 —— "把 3063 条断言搬进界面,红绿点着看"。
        ///
        /// 与老那条「综合自检」(把结果写进编译 / 异常两个框)的分工:
        ///   · **这一条 = 给人看的**:开着窗、一条条往外冒、红的绿的灰的分开,失败的一眼看得见,能只筛失败、能复制;
        ///   · 老那条留着(它一行行写进文本框,"看得全、能整段拷/比") —— 两条都调同一个 `综合自检C.运行()`,
        ///     所以在自检**跑到一半**的时候不许两条一起跑(它们写同一批静态名字表)。
        /// 窗子只留一个:已经开着就把它拎到前面(而不是再开一个)。
        /// </summary>
        private 界面控件.自检窗口? _自检窗;

        private void 菜单自检窗_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_自检窗 is { IsLoaded: true } 已开)
                {
                    已开.Activate();
                    if (已开.正在跑) 编译框.AppendText("自检窗已经在跑了(等它跑完,或者点那一窗的『停止』)。\n");
                    else 已开.开跑();
                    return;
                }
                _自检窗 = new 界面控件.自检窗口 { Owner = this };
                _自检窗.Closed += (_, _) => _自检窗 = null;
                _自检窗.Show();
                _自检窗.开跑();
            }
            catch (Exception 异常)
            {
                记异常框($"打开自检窗失败: {异常.Message}");
            }
        }

        private async void 综合自检_Click(object sender, RoutedEventArgs e)
        {
            var 菜单项 = sender as MenuItem;
            if (菜单项 != null) 菜单项.IsEnabled = false;
            try
            {
                编译框.Clear();
                异常框.Clear();
                编译框.AppendText("正在运行综合自检(引擎级,约半分钟;这一步在后台跑,界面不卡)…\n");

                var 报告 = await Task.Run(() => 综合自检C.运行());   // T21:`运行` 多了可选参数,方法组转不成 Action

                var 报告文本 = new StringBuilder();
                foreach (var 行 in 报告.行) 报告文本.AppendLine(行);
                报告文本.AppendLine($"\n=== 综合自检完成:通过 {报告.通过} / 失败 {报告.失败} / 跳过 {报告.跳过} ===");
                编译框.Text = 报告文本.ToString();

                异常框.Text = 报告.异常.Count > 0 ? string.Join("\n", 报告.异常) : "（无异常）\n";
            }
            catch (Exception 异常)
            {
                编译框.Text = $"综合自检启动失败: {异常.Message}\n";
                记异常框($"综合自检启动失败: {异常}");
            }
            finally
            {
                if (菜单项 != null) 菜单项.IsEnabled = true;
            }
        }

        #endregion

        #region 主题菜单

        private void 菜单主题暗_Click(object sender, RoutedEventArgs e) => 执行菜单主题("暗");
        private void 菜单主题亮_Click(object sender, RoutedEventArgs e) => 执行菜单主题("亮");
        private void 菜单主题黑客帝国_Click(object sender, RoutedEventArgs e) => 执行菜单主题("黑客帝国");
        private void 菜单主题赛博朋克_Click(object sender, RoutedEventArgs e) => 执行菜单主题("赛博朋克");

        private void 执行菜单主题(string 名称)
        {
            if (主题C.应用(名称))
                编译框.Text = $"已切换主题: {主题C.当前主题?.名称}\n";
            else
                编译框.Text = $"未知主题: {名称}\n";
        }

        #endregion

        #region 选项页

        private 选卡页视图模型? _选项前的常规页;

        private void 菜单选项_Click(object sender, RoutedEventArgs e)
        {
            var 模型 = 主视图模型;
            if (模型.选项页 is not { } 选项) { 模型.确保选项页(); 选项 = 模型.选项页!; }
            if (模型.选中页 == 选项)
            {
                返回选项前页();
                return;
            }
            _选项前的常规页 = 当前选卡 ?? 模型.常规页.LastOrDefault();
            模型.选中页 = 选项;
        }

        /// <summary>
        /// 选项页的 ✕:切回点开选项之前那一页 → 移除选项页。
        /// <para>
        /// 第169批(bug 2):原先**一个常规页都没有时**会先 `模型.添加()` 补一张空白页再移除选项页 ——
        /// 于是"空窗口 → 选项 → 关掉选项"会在编辑器区留下一个没人要的空白页(用户实测报的)。
        /// 空窗口本来就是这个设计的一部分(`主视图模型` 的构造注释写着"没打开项目/文件时,编辑器区就是空的,
        /// 不摆一个空标签页"),所以这里**不补页**:移除选项页、选中页置空,回到启动时那个样子。
        /// </para>
        /// </summary>
        private void 关闭选项页_Click(object sender, RoutedEventArgs e)
        {
            var 模型 = 主视图模型;
            var 选项 = 模型.选项页;
            if (选项 == null) return;
            var 目标 = _选项前的常规页;
            if (目标 == null || !模型.选卡页视图模型表.Contains(目标)) 目标 = 模型.常规页.LastOrDefault();
            _选项前的常规页 = null;

            if (目标 != null) 模型.选中页 = 目标;      // 先切走再移除,免得选中的正是被移除的那一页
            模型.选卡页视图模型表.Remove(选项);
            if (目标 == null) 模型.选中页 = null;      // 一个常规页都没有 ⇒ 编辑器区就是空的(启动时那个样子)
        }

        private void 返回选项前页()
        {
            var 模型 = 主视图模型;
            var 目标 = _选项前的常规页 ?? 模型.常规页.LastOrDefault();
            if (目标 != null) 模型.选中页 = 目标;
        }

        private void 应用全部编辑器选项()
        {
            var 编辑器们 = new List<编辑器>();
            收集视觉子级(this, 编辑器们);
            foreach (var 编 in 编辑器们) 编.应用选项();
        }

        #endregion

        #region 中英切换

        private void 中英切换_Click(object sender, RoutedEventArgs e)
        {
            bool 切到中文;
            if (中英切换.Content.ToString() == "\ue982")
            {
                中英切换.Content = "\ue97f";
                中英切换.ToolTip = "Switch to Chinese";
                中英切换.FontSize = 20;
                中英切换.FontWeight = FontWeights.Bold;
                中英切换.Foreground = 中英切换图标色();
                切到中文 = false;
            }
            else
            {
                中英切换.Content = "\ue982";
                中英切换.ToolTip = "切换到英文";
                中英切换.FontSize = 20;
                中英切换.Foreground = 中英切换图标色();
                切到中文 = true;
            }

            // 以当前标签页内容为准(双向绑定已让 视图模型 与编辑器同步)。
            // 走 `编译服务C.切换中英` 这一个入口(关键字 + 指令名/内建函数名/附加类型名,顺序在里面定死)
            if (当前选卡 is { } 页)
                页.内容 = 前端契约C.服务.编译.切换中英(切到中文, 页.内容 ?? string.Empty);
        }

        /// <summary>
        /// 「中英切换」那个图标按钮的两档字色 —— **从当前主题的词类配色里取**,不是写死的颜色。
        ///
        /// 原来这两档是 `DarkSlateGray` / `Brown` 两个写死的**暗色**:亮色主题下还看得见,
        /// 换成暗 / 黑客帝国 / 赛博朋克这三套暗底主题,就等于在 `#2B2B2B` / 几乎全黑 / 深紫的工具栏上
        /// 压一撮深灰、深棕 —— 图标基本消失。
        ///
        /// 取词类配色是**因为它天生就是"要在本主题底色上看得见"的那一套**:
        /// `关键字` / `寄存器` 两档在四套主题下各自有值,而且两色一眼分得开(口径照 `配色词类C`)。
        /// 主题没给值时退回原来那两支写死色,不至于变成透明。
        ///
        /// 两档的判据照 `中英切换_Click` 既有的那一格(`ToolTip` 还是"切换到英文" ⇒ 现在停在中文那一档),
        /// **不动它**;这里只把颜色换成本主题的。
        /// </summary>
        private Brush 中英切换图标色() =>
            中英切换.ToolTip as string == "切换到英文" ? 词类色刷("寄存器", Brushes.Brown)
                                                      : 词类色刷("关键字", Brushes.DarkSlateGray);

        private static Brush 词类色刷(string 词类键, Brush 兜底)
        {
            try
            {
                if (主题C.当前主题 is { } 主题 &&
                    主题设置.转刷(主题.取词类色(词类键)) is SolidColorBrush { Color.A: > 0 } 实)
                    return 实;
            }
            catch { }
            return 兜底;
        }

        #endregion

        #region 主题与输出框命令

        /// <summary> 把主题套用到窗口 / 输出框 / 所有编辑器 </summary>
        private void 应用主题(主题设置 主题)
        {
            try
            {
                Background = 主题设置.转刷(主题.背景);
                // 窗口自己那一层前景:**不给它,`TextBlock` 就一直是系统默认的黑** ——
                // 暗 / 黑客帝国 / 赛博朋克这三套暗底主题下,凡是没有显式写字色、也没挂
                // `{DynamicResource 主题前景刷}` 的纯文字(左栏那一行"解决方案资源管理器"、
                // 工具栏上的"打包:"、调试页格子上的"指令 / 内存 / 寄存器" …)全是黑字压黑底。
                // 给在 Window 上 ⇒ 整个窗口的继承链一次到位(`TextBlock` 吃继承;
                // `TabItem` 表头那种 `ContentPresenter` 现生成的文字也吃 —— 探针实测)。
                // 探针还量清了两条边界,所以这一句是安全的:
                //   ① 子窗口(`Owner` = 本窗)**不继承**这一格 ⇒ 选项页 / 自检窗 / 思维导图那几处的黑白不会被带跑;
                //   ② `ContextMenu` 的项与 `ToolTip` 的内容是"继承自己的逻辑爹"(MenuItem 拿菜单那条样式、
                //      ToolTip 拿自己那格 `Foreground="Black"`),不受影响。
                // 底部那几格钉死在浅色上的文字反而要**显式钉色**(见 `主题浅底字刷`),不然会被这一句带成浅色。
                Foreground = 主题设置.转刷(主题.前景);
                Resources["主题面板刷"] = 主题设置.转刷(主题.面板背景);
                Resources["主题内容刷"] = 主题设置.转刷(主题.背景);
                Resources["主题前景刷"] = 主题设置.转刷(主题.前景);
                Resources["主题强调刷"] = 主题设置.转刷(主题.强调色);
                // 顶部菜单那一条:暗/亮两套下等于原来的深灰(与老样子逐像素一致),
                // 风格主题(黑客帝国 / 赛博朋克)下跟着主题走 —— 不然紫底上压一条灰黑菜单很出戏。
                Resources["主题菜单刷"] = 主题设置.转刷(主题.面板背景);
                Resources["主题悬停刷"] = 主题设置.转刷(主题.强调色);
                Resources["主题菜单字刷"] = 主题设置.转刷(主题.前景);

                输出框.Background = 主题设置.转刷(主题.输出框背景);
                输出框.Foreground = 主题设置.转刷(主题.输出框前景);
                输出框.CaretBrush = 主题设置.转刷(主题.输出框前景);
                if (编译框 != null)
                {
                    编译框.Background = 主题设置.转刷(主题.输出框背景);
                    编译框.Foreground = 主题设置.转刷(主题.输出框前景);
                    编译框.CaretBrush = 主题设置.转刷(主题.输出框前景);
                }
                if (异常框 != null)
                {
                    异常框.Background = 主题设置.转刷(主题.输出框背景);
                    异常框.Foreground = 主题设置.转刷(主题.输出框前景);
                    异常框.CaretBrush = 主题设置.转刷(主题.输出框前景);
                }

                // 中英切换那个图标按钮:它的两档字色是从**本主题的词类配色**里取的 ⇒ 换主题要跟着重取一遍,
                // 不然从暗主题切到亮主题(或反过来)时,它还挂着上一套主题里取的颜色。
                try { 中英切换.Foreground = 中英切换图标色(); } catch { }

                var 编辑器们 = new List<编辑器>();
                收集视觉子级(this, 编辑器们);
                foreach (var 编 in 编辑器们) 编.应用主题(主题);
            }
            catch { }
        }

        /// <summary> 输出框命令: 格式="对齐" 对齐当前编辑器语义列 </summary>
        private static readonly Regex 格式命令正则 =
            new(@"^\s*(?:格式|format)\s*=\s*""(?<名>[^""]+)""\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary> 输出框命令: 优化="关" / 优化="开" 开关编译期优化(消冗余指令 + 循环不变量提升) </summary>
        private static readonly Regex 优化命令正则 =
            new(@"^\s*(?:优化|optimize|opt)\s*=\s*""(?<名>[^""]+)""\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private void 输出框_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || sender is not TextBox 框) return;
            输出框.AcceptsReturn = true;
            int 行号 = 框.GetLineIndexFromCharacterIndex(框.CaretIndex);
            string 当前行 = 框.GetLineText(行号).Trim();

            var 格式匹配 = 格式命令正则.Match(当前行);
            if (格式匹配.Success)
            {
                e.Handled = true;
                string 命令 = 格式匹配.Groups["名"].Value.ToLowerInvariant();
                if (当前编辑器 is { } 编 && (命令 == "对齐" || 命令 == "align"))
                {
                    编.语义对齐();
                    输出框.Text = "已按语义列对齐(冒号/指令/注释列对齐,只调整空白)。\n";
                }
                else
                {
                    输出框.Text = $"未知格式命令: \"{格式匹配.Groups["名"].Value}\"(可用: 对齐)\n";
                }
                return;
            }

            var 优化匹配 = 优化命令正则.Match(当前行);
            if (优化匹配.Success)
            {
                e.Handled = true;
                string 命令 = 优化匹配.Groups["名"].Value.Trim().ToLowerInvariant();
                bool 开 = 命令 is "开" or "on" or "true" or "1";
                bool 关 = 命令 is "关" or "off" or "false" or "0";
                if (!开 && !关)
                {
                    输出框.Text = $"优化命令只认 开/关(也认 on/off):优化=\"关\"\n";
                    return;
                }
                优化开 = 开;
                当前机器码 = [];                 // 逼着下次编译重新来(优化开关一变,机器码必须重发)
                编译指纹 = "";
                输出框.Text = 开
                    ? "编译期优化已打开(消冗余指令 + 循环不变量提到循环外面)。下次执行会重新编译。\n"
                    : "编译期优化已关掉(对照模式:展开就是你写的原样)。下次执行会重新编译。\n";
                return;
            }
            // 非命令:保持默认回车行为
        }

        /// <summary> 输入 `格式="` / `优化="` 时自动补右引号(省得记格式;主题命令已去掉) </summary>
        private void 输出框_TextChanged(object sender, EventArgs e)
        {
            int 引号位置 = 输出框.Text.IndexOf('"');
            if (输出框.Text.IndexOf('=') + 1 == 引号位置 && 引号位置 == 输出框.Text.Length - 1)
            {
                输出框.Text += "\"";
                输出框.SelectionStart = 输出框.Text.Length - 1;
            }
        }

        #endregion

    }
}
