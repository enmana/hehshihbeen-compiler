using hehshihbeen_compiler.界面控件;
using hehshihbeen_compiler.配置;
using hehshihbeen_compiler.选卡控件;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static System.Net.Mime.MediaTypeNames;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    /// <summary> 编辑器的一部分:行高设置工具 / 文本变更回调 / 拼音缩写自动补全 / 高亮刷新与滚动联动 / 视觉树查找 / 外观属性 </summary>
    public partial class 编辑器 : UserControl
    {
        #region 文本变更回调(回写/防抖/关候选)
        private void 源代码编辑器_TextChanged(object? sender, EventArgs e)
        {
            // **文本一变就把悬浮提示收掉**(不管是谁改的:手敲、撤销、绑定推文本、切文件)。
            // 以前只关候选框,提示靠 400ms 防抖后的"设置诊断"顺带收 —— 那 400ms 里提示还挂着,
            // 而且它是"过期信息"(指着已经变了的代码)。
            关掉没固定的提示();
            // 文本都改了,"关过哪一处"这条记忆(键里带着行号)已经指不准了 —— 一并清掉,
            // 免得它活得比所指的那段代码还久,变成"这个提示再也弹不出来"。
            提示开关.清空();

            // 正在整体推送外部文本(数据绑定/设置代码)时，不需要再回写
            if (正在更新高亮) return;

            // 用户输入/撤销/自动补全等导致的文档变化 → 写回 内容 依赖属性，
            // 借助双向绑定让 VM(绑定源) 与编辑器文本保持一致。
            if (代码编辑器 != null)
            {
                string 编辑器文本 = 代码编辑器.Text;
                string 当前绑定值 = 内容 ?? string.Empty;
                if (编辑器文本 != 当前绑定值)
                    SetValue(内容Property, 编辑器文本);
            }

            防抖定时器.Stop();
            防抖定时器.Start();
            try { 文本变化?.Invoke(); } catch { }        // 主窗口订了它做"停手 400ms 检查一遍"

            // 文本改变时关闭候选框（避免过期候选显示）
            if (FindName("候选Popup") is Popup popup && popup.IsOpen) popup.IsOpen = false;

            // P37:文本变了,配对括号与签名提示都重算一遍(括号可能刚被删掉、参数可能刚敲进去)
            刷新括号与签名();

            // 文本变了就重算折叠区(块/连续注释块)
            if (!结构操作中) 刷新折叠区();

            // P40:整篇"推文本"(格式化 / 替换 / 改主题 / 载入别的文件)之后副光标的位置就不准了 ⇒ 全收掉。
            // 判据是**变化范围**:`正在更新高亮` 立着 = 绑定推文本,`结构操作中` 立着 = 整篇换;
            // 多光标自己插那几下(逐处 `Replace`)不在其中 —— 它自己会把位置算好。
            if (!多光标正在写 && 正在更新高亮) 清掉多光标();
        }
        #endregion

        #region 拼音缩写自动补全与候选弹窗
        /// <summary>
        /// **预览层**(隧道:比 AvalonEdit 自己的按键处理先到)—— 候选框开着的时候,↑↓ 选、回车插入、Esc 关掉、Tab 插入;
        /// 必须在这儿拦:AvalonEdit 的 TextArea 会先把方向键(Tab 也会)吃掉,挂在 KeyDown(冒泡)上收不到
        /// (用户实测:`s.` 之后按 Tab 出来的是制表符,↑↓ 也不动)。
        /// 候选框没开就一律不拦(按键照旧)。
        /// </summary>
        private void 源代码编辑器_预览键(object? sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (FindName("候选Popup") is Popup popup && popup.IsOpen && FindName("候选List") is ListBox list)
                {
                    switch (e.Key)
                    {
                        case System.Windows.Input.Key.Down:
                            选中候选(list, +1);
                            e.Handled = true;
                            return;
                        case System.Windows.Input.Key.Up:
                            选中候选(list, -1);
                            e.Handled = true;
                            return;
                        // C16-1(用户 2026-09-19 拍板):**Tab / 回车 = 只要这个名字**(后面不加空格);
                        // **空格 = 名字 + 一个空格**。尾空格不再写在候选的插入文本里(`补全词表C.确认文本` 定)。
                        case System.Windows.Input.Key.Tab:
                        case System.Windows.Input.Key.Enter:
                            if (尝试确认候选(补空格: false)) e.Handled = true;
                            return;
                        case System.Windows.Input.Key.Space:
                            // 空格是**字符**输入:在预览(隧道)层吃掉它,后面的 TextInput 就不会再插一个空格
                            if (尝试确认候选(补空格: true)) e.Handled = true;
                            return;
                        case System.Windows.Input.Key.Escape:
                            popup.IsOpen = false;
                            e.Handled = true;
                            return;
                    }
                }
                // 候选框没开:Tab 不拦(还给它原来的用处:缩进/切焦点)
                // **Esc 关悬浮提示**(用户要的:失焦/Esc 都要能关掉)
                if (e.Key == System.Windows.Input.Key.Escape && _开着的提示.Count > 0)
                {
                    收起诊断提示(用户关的: true);
                    e.Handled = true;
                    return;
                }
                // C7:编辑器里不再自己认 Ctrl+Shift+F —— 它进了窗口那一层的**快捷键表**
                // (`MainWindow.快捷键_预览` + `快捷键C.全部`),这样"格式化"的键位能跟别的动作一起改。
            }
            catch { }
        }

        /// <summary> 把候选框里的选中项挪一格(到头就停住)</summary>
        private void 选中候选(ListBox list, int 步)
        {
            if (list.Items.Count == 0) return;
            int 下标 = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + 步));
            list.SelectedIndex = 下标;
            try { list.ScrollIntoView(list.Items[下标]); } catch { }
        }

        // 处理拼音首字母自动补全逻辑
        private void 源代码编辑器_KeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
        {
            if (代码编辑器 == null) return;

            // 候选框开着时的 ↑↓/回车/Esc/Tab 都在**预览层**处理(见 源代码编辑器_预览键):
            // AvalonEdit 会先吃掉方向键,挂在冒泡这一层收不到(用户实测 ↑↓ 不动、Tab 出来制表符)。

            // 退格/回车不参与缩写补全
            if (e.Key == System.Windows.Input.Key.Back || e.Key == System.Windows.Input.Key.Enter) return;

            // 使用 Dispatcher 延后处理，以便 内容 已更新
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (代码编辑器 == null) return;
                    int caret = 代码编辑器.CaretOffset;
                    string text = 代码编辑器.Text;
                    // **字符串/字符字面量/行注释里面不弹候选**(用户实测:写在 "…" 里的点也会弹方法)
                    if (在字面量或注释里(text, caret))
                    {
                        if (FindName("候选Popup") is Popup p字) p字.IsOpen = false;
                        return;
                    }
                    // 取光标前的"词"(字母/数字/下划线/汉字)
                    int cur = caret - 1;
                    while (cur >= 0 && 是词字符(text[cur])) cur--;
                    int tokenStart = cur + 1;
                    string token = text.Substring(tokenStart, caret - tokenStart);
                    // 刚敲了一个 `.`,而且点**前面是一个名字**(`分数.` 才弹方法;句子末尾的 `.` 不算)
                    bool 在点后 = tokenStart >= 2 && text[tokenStart - 1] == '.' && 是词字符(text[tokenStart - 2]);
                    string 点名 = 在点后 ? 取点名(text, tokenStart) : "";
                    if (在点后 && token.Length == 0)
                    {
                        显示候选弹窗(收集候选("", 在点后: true, 点名));
                        return;
                    }
                    if (token.Length == 0)
                    {
                        if (FindName("候选Popup") is Popup p0) p0.IsOpen = false;
                        return;
                    }

                    // 通用候选:指令(中/英) + 语言关键字 + 类型 + 内建函数 + 方法 + 工程里的名字
                    // (C19:变量按作用域、函数/结构来自整个工程、按公私有过滤;`名字.` 后面给那个类型的成员),
                    // 都吃**拼音首字母**(hs → 函数、cd → 长度);只弹候选、不自动替换 —— 由用户按 Tab/回车确认
                    var 候选 = 收集候选(token, 在点后, 点名);
                    if (候选.Count == 0)
                    {
                        if (FindName("候选Popup") is Popup p1) p1.IsOpen = false;
                        return;
                    }
                    显示候选弹窗(候选);
                }
                catch { }
            }), DispatcherPriority.Background);
        }

        /// <summary> 词字符:一律走 `高亮扫描C.是词字符`(字母/数字/下划线/汉字),全工程只留这一份口径 </summary>
        private static bool 是词字符(char c) => 高亮扫描C.是词字符(c);

        /// <summary> `名字.` 里点号**左边**那个名字(`分数.` → `分数`;没有返回空串) —— C19 按它算"." 后面的成员 </summary>
        private static string 取点名(string 文本, int 词起)
        {
            if (词起 < 2 || 文本[词起 - 1] != '.' || !是词字符(文本[词起 - 2])) return "";
            int 止 = 词起 - 1;
            int 起 = 止 - 1;
            while (起 >= 0 && 是词字符(文本[起])) 起--;
            return 文本.Substring(起 + 1, 止 - 起 - 1);
        }

        /// <summary> 这个位置在不在字符串/字符字面量/行注释里(规则在 引擎\文本上下文C,两端共用、可自检)</summary>
        private static bool 在字面量或注释里(string 文本, int 位置) => 文本上下文C.在字面量或注释里(文本, 位置);

        /// <summary> 本文件路径(标签页模型里带着);拿不到就没有"含进来的库"那一份候选 </summary>
        private string? 本文件路径 => (DataContext as 选卡页视图模型)?.全称;

        /// <summary>
        /// C19 智能补全:主窗口把"最近一次编译结果"递进来的口子(`MainWindow` 构造里挂一次)。
        /// 入参 = 正在编辑的文件全称,返回 = 那个文件的名字表 + 工程级名字表;没编译过返回 null。
        /// 编辑器只在"算候选"那一刻调它,拿不到就退回现场源码扫(见 `补全名字C`)。
        /// </summary>
        public static Func<string?, 补全来源C?>? 取名表;

        /// <summary>
        /// 取候选(C19 起只调一个引擎入口):
        ///   ① 普通位置 —— 固定词表 + 工程里的名字(**变量按作用域**、函数/结构来自整个工程、按公私有过滤);
        ///   ② `名字.` 后面 —— 那个类型的成员(结构字段 / 方法容器成员 / `字符串`·列表的内建方法);
        ///   ③ 没有编译结果(没编译过 / 新文件)—— `补全名字C` 自己退回现场扫兜底。
        /// 匹配与排序全在 `补全词表C.排序匹配`(与手机 IDE 共用一份),这里只负责把上下文递进去。
        /// </summary>
        private List<补全候选C> 收集候选(string token, bool 在点后, string 点名 = "")
        {
            var 选项 = 配置.选项.当前;
            // 模糊拼音:总开关关掉就传空集合(标准拼音仍然能打,只是不认近似音)
            var 规则 = 选项.模糊拼音 ? 选项.模糊拼音规则 : [];
            var 请求 = new 补全请求C
            {
                当前文件 = 本文件路径 ?? "",
                当前行 = 取当前行号(),
                词 = token,
                在点后 = 在点后,
                点名 = 点名,
                现场正文 = 代码编辑器?.Text ?? "",
                上限 = 30,
            };
            var 候选 = 前端契约C.服务.编译.取补全(取名表?.Invoke(本文件路径), 请求, 规则);
            if (!在点后)
                foreach (var 寄存器 in 寄存器候选(token))
                    if (!候选.Any(x => x.词 == 寄存器.词)) 候选.Add(寄存器);
            return [.. 候选.Take(30)];
        }

        /// <summary> 寄存器候选:`r` 开头时给 R0~R15(数量以 寄存器C 为准) </summary>
        private static List<补全候选C> 寄存器候选(string token)
        {
            var 结果 = new List<补全候选C>();
            string 上 = token.ToUpperInvariant();
            if (token.Length == 0 || token.Length > 3 || 上[0] != 'R') return 结果;
            for (int i = 0; i < 寄存器C.数量; i++)
            {
                string 名 = 寄存器C.名称(i);
                if (名.StartsWith(上, StringComparison.Ordinal))
                    结果.Add(new 补全候选C(名, 候选来历.名字, 名));
            }
            return 结果;
        }

        /// <summary>
        /// 从候选列表里点/敲一条(双击 = 显式点选,与"按空格"同档;列表里按回车 = 与编辑器里按回车同档)。
        /// </summary>
        private void 替换当前词(补全候选C 项, bool 补空格 = false)
        {
            if (代码编辑器 == null) return;
            int caret = 代码编辑器.CaretOffset;
            string text = 代码编辑器.Text;
            int cur = caret - 1;
            while (cur >= 0 && 是词字符(text[cur])) cur--;
            int start = cur + 1;
            替换词(start, caret - start, 项, 补空格);
        }

        /// <summary>
        /// 用候选项替换一段缩写。写进去的文本由 `补全词表C.确认文本` 算(C16-1:**Tab / 回车 = 只要这个名字**、
        /// **空格 = 名字 + 一个空格**);带括号的(方法/内建函数)插 `xxx()` 并把光标停在括号里。
        ///
        /// **代码片段候选**(C28,`项.片段 != null`)是另一条:先把打进去的缩写删掉,
        /// 再交给 `插入片段` 插整段骨架(**按当前层级缩进**、光标停在要你填的地方)——
        /// 与菜单『编辑 → 插入代码片段』走的是同一个方法、同一份 `片段插入C.算插入`,
        /// 所以两边的插入结果逐字节相同(Tab / 空格 / 回车在这里没有区别:骨架不是"名字 + 空格")。
        /// </summary>
        private void 替换词(int 起点, int 长度, 补全候选C 项, bool 补空格)
        {
            if (代码编辑器 == null) return;
            if (项.片段 != null)
            {
                var 文 = 代码编辑器.Document;
                if (长度 > 0) using (文.RunUpdate()) 文.Remove(起点, 长度);
                代码编辑器.CaretOffset = 起点;
                插入片段(项.片段);
                return;
            }
            string 插入 = 补全词表C.确认文本(项, 补空格);
            代码编辑器.Document.Replace(起点, 长度, 插入);
            代码编辑器.CaretOffset = 起点 + 插入.Length - (项.括号内落光标 ? 1 : 0);
        }

        /// <summary>
        /// Tab / 回车 / 空格确认候选:候选框开着就用**选中的那条**(↑↓ 选过就是它,没选过就是第一条);
        /// 候选框没开才按"光标前那个词"重算一遍。
        /// 注意:`名字.` 之后光标前**没有词**(点后面是空的),这时候也要能插进去(用户实测:以前按 Tab 插了个制表符)。
        /// `补空格`(C16-1)决定尾空格:Tab / 回车 = 假(只要名字)、空格 = 真(名字 + 一个空格)。
        /// </summary>
        private bool 尝试确认候选(bool 补空格)
        {
            if (!选项.当前.Tab确认) return false;
            if (代码编辑器 == null) return false;
            int caret = 代码编辑器.CaretOffset;
            string text = 代码编辑器.Text;
            int cur = caret - 1;
            while (cur >= 0 && 是词字符(text[cur])) cur--;
            int start = cur + 1;
            string token = text.Substring(start, caret - start);
            bool 在点后 = start >= 2 && text[start - 1] == '.' && 是词字符(text[start - 2]);
            string 点名 = 在点后 ? 取点名(text, start) : "";
            if (在字面量或注释里(text, caret) && token.Length == 0) return false;

            var popup = FindName("候选Popup") as Popup;
            补全候选C? 项 = null;
            if (popup is { IsOpen: true } && FindName("候选List") is ListBox list && list.SelectedItem is 补全候选C sel)
                项 = sel;
            if (项 == null)
            {
                if (token.Length == 0 && !在点后) return false;      // 光标前没东西,也没弹着候选:不拦
                项 = 收集候选(token, 在点后, 点名).FirstOrDefault();
            }
            if (项 == null) return false;

            替换词(start, caret - start, 项, 补空格);
            if (popup != null) popup.IsOpen = false;
            return true;
        }

        /// <summary>
        /// 候选框该落在**编辑器坐标**里的哪个位置(第144批抽出来的:界面与离线探针共用同一份口径)。
        ///
        /// 口径分两步:
        /// ① 光标在编辑器里的位置 —— **`Caret.CalculateCaretRectangle()` 给的是文档坐标,不随滚动走**
        ///    (c16 探针实测:文档滚到 200 像素时它给 `Y=492.9`,而那一行的可视行顶是 `292.9`)。
        ///    所以先减掉 `TextView.ScrollOffset` 换成视口坐标,再用 `TranslatePoint` 换到编辑器坐标
        ///    (旧代码直接把它当视口坐标喂给 `PointToScreen` ⇒ 越往下滚,算出来的落点越往下掉,
        ///    一路掉到编辑器外面,用户看到的就是"补全框跑到任务栏上");
        /// ② 落点一律过 `弹窗定位C.算`:下面放不下就翻到光标上面,左右也夹在编辑区里 ——
        ///    保证整个框留在编辑区,绝不越出屏幕底边。
        /// </summary>
        public Point 算候选框落点()
        {
            if (代码编辑器 == null) return new Point(0, 0);
            try
            {
                var textView = 代码编辑器.TextArea.TextView;
                textView.EnsureVisualLines();
                var 光标矩形 = 代码编辑器.TextArea.Caret.CalculateCaretRectangle();
                var 视口点 = new Point(光标矩形.X - textView.ScrollOffset.X, 光标矩形.Y - textView.VerticalOffset);
                var 视点 = textView.TranslatePoint(视口点, 代码编辑器);
                double 行高 = 光标矩形.Height > 0 ? 光标矩形.Height : 代码编辑器.FontSize * 1.4;
                var (弹窗宽, 弹窗高) = 量候选框大小();
                var (x, y) = 弹窗定位C.算(视点.X, 视点.Y, 行高, 弹窗宽, 弹窗高,
                    代码编辑器.ActualWidth, 代码编辑器.ActualHeight);
                return new Point(x, y);
            }
            catch { return new Point(0, 0); }     // 算不出来就退到编辑区左上角,绝不乱放
        }

        /// <summary>
        /// 量候选框的真实大小。**`ActualWidth` 在弹窗没开过的时候是 0**(旧代码拿它当宽度用,夹取一直是摆设),
        /// 所以这里先对弹窗内容做一次无限尺寸的 Measure,读 `DesiredSize`。
        /// </summary>
        private (double 宽, double 高) 量候选框大小()
        {
            double 宽 = 150, 高 = 120;            // 兜底:量不出来时的保守值
            try
            {
                if (FindName("候选List") is ListBox list)
                {
                    list.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    宽 = list.DesiredSize.Width;
                    高 = list.DesiredSize.Height;
                }
                if (FindName("候选Popup") is Popup popup && popup.Child is FrameworkElement 孩子)
                {
                    孩子.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (孩子.DesiredSize.Width > 0) 宽 = 孩子.DesiredSize.Width;   // 边框比列表宽一点,按框算
                    if (孩子.DesiredSize.Height > 0) 高 = 孩子.DesiredSize.Height;
                }
            }
            catch { }
            return (宽, 高);
        }

        /// <summary> 在光标处弹出候选列表 </summary>
        private void 显示候选弹窗(List<补全候选C> 候选)
        {
            if (FindName("候选List") is not ListBox list || FindName("候选Popup") is not Popup popup || 代码编辑器 == null) return;
            list.ItemsSource = 候选;
            list.SelectedIndex = 0;
            var 落点 = 算候选框落点();     // 先量大小再算落点(量的是刚设进去的这一份候选)
            popup.HorizontalOffset = 落点.X;
            popup.VerticalOffset = 落点.Y;
            popup.IsOpen = true;
        }

        #endregion

        #region 高亮刷新与滚动联动
        /// <summary>
        /// 高亮刷新(C7 大文件流畅度改过)。**以前**这里把整篇文本交给一个 overlay `TextBlock` 重新着色 ——
        /// 每次改字(150ms 防抖后)和每次换主题都要**整篇扫描 + 造几千个 Run**,上万行的文件就卡在这儿;
        /// 而那个 overlay 一直在编辑器**底下**(编辑器背景是不透明的主题色),根本看不见。
        ///
        /// 现在着色只走 AvalonEdit 的**逐行**通路(`AvalonInlineHighlighter`:只给看得见的那几十行上色,
        /// 逐行结果还有 `增量高亮C` 缓存兜着),这里就只剩一句"重画一遍"。
        /// </summary>
        private void 刷新高亮()
        {
            try { 代码编辑器?.TextArea.TextView.Redraw(); }
            catch { /* 重画失败只影响观感:绝不影响编辑/编译 */ }
        }

        // (滚轮转发 / 两个 ScrollViewer 互相同步的三个处理函数已删:编辑器不再套外层 ScrollViewer,
        //  滚动交给 AvalonEdit 自己 —— 见 编辑器.xaml 顶上那段,C12 卡死的根因就在那儿)
        #endregion

        #region 视觉树查找工具
        // 查找子孙控件的通用方法
        private static T? FindVisualChild<T>(DependencyObject dep) where T : DependencyObject
        {
            if (dep == null) return null;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
            {
                var child = VisualTreeHelper.GetChild(dep, i);
                if (child is T t) return t;
                var ret = FindVisualChild<T>(child);
                if (ret != null) return ret;
            }
            return null;
        }
        #endregion
    }
    public class 代码框设置
    {
        #region 外观属性(写时同步到代码框)
        private 编辑器 CEB { get; set; }
        private FontFamily 字体_ = new("Consolas");
        public FontFamily 字体
        {
            get => 字体_;
            set
            {
                字体_ = value;
                CEB.代码框.FontFamily = 字体_;
            }
        }
        private double 字号_ = 14;
        public double 字号
        {
            get => 字号_;
            set
            {
                字号_ = value;
                CEB.代码框.FontSize = 字号_;
            }
        }
        private FontWeight 字粗_ = FontWeights.Normal;
        public FontWeight 字粗
        {
            get => 字粗_;
            set
            {
                字粗_ = value;
                CEB.代码框.FontWeight = 字粗_;
            }
        }
        private FontStyle 字体样式_ = FontStyles.Normal;
        public FontStyle 字体样式
        {
            get => 字体样式_;
            set
            {
                字体样式_ = value;
                CEB.代码框.FontStyle = 字体样式_;
            }
        }
        private double 行高_ = 26;    // 默认行高与 选项设置.行高 一致(用户反馈 20 太紧)
        public double 行高
        {
            get => 行高_;
            set
            {
                行高_ = value;
                设置TextEdit行高(CEB.代码框, 行高_);
            }
        }
        private Thickness 外边距_ = new(0);
        public Thickness 外边距 // Margin
        {
            get => 外边距_;
            set
            {
                外边距_ = value;
                CEB.代码框.Margin = 外边距_;
            }
        }
        private Thickness 内边距_ = new(0); // Padding
        public Thickness 内边距
        {
            get => 内边距_;
            set
            {
                内边距_ = value;
                CEB.代码框.Padding = 内边距_;
            }
        }
        private bool 显示行号_ = true;
        public bool 显示行号
        {
            get => 显示行号_;
            set
            {
                显示行号_ = value;
                CEB.代码框.ShowLineNumbers = 显示行号_;
            }
        }
        private bool 自动换行_ = false;
        public bool 自动换行
        {
            get => 自动换行_;
            set
            {
                自动换行_ = value;
                CEB.代码框.WordWrap = 自动换行_;
            }
        }
        #endregion


        #region 构造函数与行高换算辅助
        public 代码框设置(编辑器 CodeEditBox)
        {
            CEB = CodeEditBox;
            字体 = 字体_;
            字号 = 字号_;
            字粗 = 字粗_;
            字体样式 = 字体样式_;
            行高 = 行高_;
            外边距 = 外边距_;
            内边距 = 内边距_;
            显示行号 = 显示行号_;
            自动换行 = 自动换行_;
        }
        private static void 设置TextEdit行高(TextEditor 编辑器, double 目标行高)
        {
            if (编辑器 == null) return;
            var textView = 编辑器.TextArea.TextView;
            if (textView == null) return;

            // 1. 保存当前的 LineSpacing，然后临时设为 1.0 以测量默认行高
            double 原系数 = TextView.GetLineSpacing(textView);
            if (原系数 <= 0) 原系数 = 1.0;

            TextView.SetLineSpacing(textView, 1.0);
            编辑器.UpdateLayout(); // 强制刷新布局，确保 DefaultLineHeight 更新

            // 2. 获取默认行高（即字号 × 字体比例，在 LineSpacing=1.0 时的值）
            double 默认行高 = textView.DefaultLineHeight * 1.078;
            if (默认行高 <= 0)
            {
                // 如果仍为 0，用字体大小估算（比例 1.2 是一个常见近似值）
                默认行高 = 编辑器.FontSize;
            }

            // 3. 恢复原来的 LineSpacing
            TextView.SetLineSpacing(textView, 原系数);

            // 4. 计算所需系数
            double 所需系数 = 目标行高 / 默认行高;

            // 5. 限制系数范围，避免极端值（可选）
            所需系数 = Math.Max(0.5, Math.Min(5, 所需系数));

            // 6. 应用新系数
            TextView.SetLineSpacing(textView, 所需系数);
        }
        #endregion
    }
}
