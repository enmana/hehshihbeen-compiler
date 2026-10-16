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
    /// <summary> 编辑器的一部分:构造函数与初始化 / 应用主题配色 / 控件生命周期 / 成对输入 / 撤销与重做 </summary>
    public partial class 编辑器 : UserControl
    {
        #region 构造函数与初始化
        public 编辑器()
        {
            InitializeComponent();
            高亮器 = new 语法高亮C();
            try { 新造出来?.Invoke(this); } catch { }     // 主窗口订了它:好挂上"文本变了就重查一遍"

            // AvalonEdit TextEditor
            代码编辑器 = FindName("代码框") as TextEditor;
            if (代码编辑器 != null)
            {
                代码编辑器.TextChanged += 源代码编辑器_TextChanged;
                代码编辑器.KeyDown += 源代码编辑器_KeyDown;
                // Tab 会被 AvalonEdit 自己先吃掉(缩进/切焦点),所以补全确认要在**预览层**拦
                代码编辑器.PreviewKeyDown += 源代码编辑器_预览键;
                // 提示框要能自己收干净:切走窗口(Alt+Tab / 点别的程序)时收掉,免得切回来还挂在那儿
                代码编辑器.LostKeyboardFocus += (_, _) => 关掉没固定的提示();
                // 点回编辑区(不是点提示框本身)也收掉没固定的提示 —— 而且算"用户明确关掉":
                // 点哪儿就先别在哪儿再弹(不然鼠标一停同一处又冒出来)
                代码编辑器.PreviewMouseDown += (_, _) => 收起诊断提示(用户关的: true);
                Loaded += (_, _) =>
                {
                    if (Window.GetWindow(this) is { } 窗口)
                        窗口.Deactivated += (_, _) => 关掉没固定的提示();
                };
                代码编辑器.TextArea.TextEntering += 自动成对输入;
                // C5:回车自动缩进(按块深度算;规则在引擎 `代码排版C`,与命令行/格式化共用一份)
                装上缩进策略();
                // 基本设置
                代码编辑器.ShowLineNumbers = true;
                代码编辑器.FontFamily = new FontFamily("Consolas");
                代码编辑器.TextArea.Padding = new Thickness(5, 0, 0, 0);
                // **不再找/同步"外层滚动"**:编辑器现在就是自己那一个滚动条(见 编辑器.xaml 顶上那段说明)——
                // 悬停提示靠 TextView 自己的滚动事件收掉(以前是内外两个 ScrollViewer 互相同步时顺带收的)。
                try { 代码编辑器.TextArea.TextView.ScrollOffsetChanged += (_, _) => 关掉没固定的提示(); }
                catch { }

                // 最左边:断点边距(红点,点一下切换断点)
                断点栏.绑定(断点边距点击);
                代码编辑器.TextArea.LeftMargins.Add(断点栏);
                // 代码折叠:AvalonEdit 自带的折叠管理器 + 折叠边距(+/- 三角)
                // (以前这里是"就地展开成汇编"的 +/−,那个功能会改源码,已按用户要求取消)
                try
                {
                    // **折叠边距不要自己再加一个**:`FoldingManager.Install()` 内部就会往 LeftMargins 挂一个
                    // (C13 实测:再手加一个 ⇒ 折叠边距 2 个)。装/卸折叠器统一走 装上折叠器()/卸掉折叠器() 一对函数。
                    装上折叠器();
                    刷新折叠区();
                }
                catch { /* 折叠只是观感:装不上就照旧用,绝不影响编辑/编译 */ }
                // 代码整体右移 5px(空白边距,不拦点击)
                代码编辑器.TextArea.LeftMargins.Add(new 空隙边距(5));
                // P21:装 AvalonEdit 自带的**查找面板**(Ctrl+F 直接打开;菜单『编辑→查找/替换』也调 打开查找面板)
                // 它只管"找 + 高亮 + F3 下一个";**替换**由 `查找替换C` 算清单后统一走(注释/字符串跳过那条口径)
                try { 搜索面板 = ICSharpCode.AvalonEdit.Search.SearchPanel.Install(代码编辑器); }
                catch { /* 面板只是顺手:装不上不影响编辑/编译/替换 */ }

                // 语法高亮:无条件挂载行内高亮(此前只在双击候选时才挂上,导致平时无高亮)
                try
                {
                    var 行内高亮 = new AvalonInlineHighlighter(高亮器, () => 本文件路径);
                    行内高亮器 = 行内高亮;                  // C16:`{}` 嵌套彩虹色也走这条逐行通路
                    代码编辑器.TextArea.TextView.LineTransformers.Add(行内高亮);
                    // 执行行高亮(单行半透明底:语言单步=黄,汇编单步=深黄)
                    var 执行高亮 = new 执行行方框(() => _当前执行行号, () => _执行行深黄);
                    代码编辑器.TextArea.TextView.BackgroundRenderers.Add(执行高亮);
                    // 调用处(跳走以后知道从哪跳过来的;画在底下一层,黄条会盖住它):
                    // 整行淡灰 + 同一行里按调用层叠色的跨度(C15)
                    代码编辑器.TextArea.TextView.BackgroundRenderers.Add(new 调用处方框(() => _调用处行, () => _调用处跨度));
                    // C16-3:按层的缩进导引线(竖线画在 `{` 那一列、颜色跟着那一层的彩虹色;
                    // 规则在引擎 `缩进导引线C`,这里只换算像素 —— 所以要拿高亮器取线刷)
                    // H8:存一份给选项页那个开关用(开 / 关由 `应用选项()` 落)
                    导引线渲染 = new 缩进导引线渲染(高亮器);
                    代码编辑器.TextArea.TextView.BackgroundRenderers.Add(导引线渲染);
                    // (代码检查的波浪下划线 / 悬停提示在下面单独注册:不受这一段的成败影响)
                }
                catch { }

                // 代码检查的波浪下划线(红/黄/绿)+ 悬停提示:单独注册,上面的高亮/方框万一出问题也不影响它
                try
                {
                    代码编辑器.TextArea.TextView.BackgroundRenderers.Add(new 下划线渲染(() => _诊断表));
                    代码编辑器.TextArea.TextView.MouseHover += 代码视图_悬停;
                    代码编辑器.TextArea.TextView.MouseMove += 代码视图_鼠标动;
                    代码编辑器.TextArea.TextView.MouseHoverStopped += (_, _) => 也许收起诊断提示();
                    代码编辑器.TextArea.TextView.MouseLeave += (_, _) => 也许收起诊断提示();
                }
                catch { }

                // P37:配对括号方框 + 签名提示(两个都是"手感"件,单独一段 try)
                装上括号与签名();

                // P40:多光标(自己实现的那一半)+ 列选择(AvalonEdit 自带那一档钥匙)
                接上多光标();

                // 滚轮/滚动条都是 AvalonEdit 自己的(以前这里把滚轮转发给外层 ScrollViewer、还要两个滚动条互相同步;
                // 那层外层 ScrollViewer 已经拆掉,理由见 编辑器.xaml 顶上那段:它会把视口给成无限高,整篇排版)

                // 候选 List 初始化事件
                var popupInit = FindName("候选Popup") as Popup;
                // 放置目标**代码里钉死一遍**(第144批):XAML 那边原来绑的名字(`源代码框`)根本不存在,
                // 探针实测 PlacementTarget = null —— 偏移量的坐标系就成了"兜底目标"的,不可控。
                // 这里定死:目标 = 代码框,模式 = 相对(偏移量相对代码框左上角),与 `算候选框落点()` 的坐标系一致。
                if (popupInit != null && 代码编辑器 != null)
                {
                    popupInit.Placement = PlacementMode.Relative;
                    popupInit.PlacementTarget = 代码编辑器;
                }
                if (FindName("候选List") is ListBox listInit)
                {
                    listInit.MouseDoubleClick += (ss, ee) =>
                    {
                        if (代码编辑器 == null) return;
                        if (listInit.SelectedItem is 补全候选C sel)
                        {
                            替换当前词(sel, 补空格: true);      // 双击 = 显式点选,与"按空格"同档(C16-1)
                            popupInit?.SetCurrentValue(Popup.IsOpenProperty, false);
                        }
                    };

                    listInit.PreviewKeyDown += (ss, ee) =>
                    {
                        if (ee.Key == System.Windows.Input.Key.Enter)
                        {
                            if (listInit.SelectedItem is 补全候选C sel)
                            {
                                替换当前词(sel);                  // 列表里按回车 = 与编辑器里按回车同档:不加空格(C16-1)
                                popupInit?.SetCurrentValue(Popup.IsOpenProperty, false);
                                ee.Handled = true;
                            }
                        }
                        else if (ee.Key == System.Windows.Input.Key.Escape)
                        {
                            popupInit?.SetCurrentValue(Popup.IsOpenProperty, false);
                            ee.Handled = true;
                        }
                    };
                }

            }

            Loaded += CodeEditBox_Loaded;
            this.Unloaded += CodeEditBox_Unloaded;
            // DispatcherTimer 初始化
            防抖定时器.Interval = TimeSpan.FromMilliseconds(150);
            防抖定时器.Tick += (ts, te) =>
            {
                防抖定时器.Stop();
                刷新高亮();
            };
            代码框通用设置 = new(this);
            // 新控件应用当前主题与选项
            if (主题C.当前主题 is { } 主题) 应用主题(主题);
            应用选项();
        }
        private int? _当前执行行号 = null;
        /// <summary> 当前执行行是不是"深黄"(汇编单步)。语言单步用浅黄。 </summary>
        private bool _执行行深黄;
        /// <summary> "调用处"那几行(淡灰),由主窗口按调用栈算好传进来 </summary>
        private readonly List<int> _调用处行 = [];
        /// <summary> 同一行里按调用层叠色的跨度(C15),也是主窗口算好传进来 </summary>
        private readonly List<调用段C> _调用处跨度 = [];

        /// <summary>
        /// P21:打开编辑器自带的**查找面板**(Ctrl+F 也是它;菜单『编辑→查找/替换』调这里)。
        /// 面板负责"找 + 高亮 + 下一个";**替换**在 `查找替换C` 那边算清单后统一做(默认跳过注释与字符串)。
        /// </summary>
        public void 打开查找面板()
        {
            try
            {
                if (搜索面板 == null && 代码编辑器 != null)
                    搜索面板 = ICSharpCode.AvalonEdit.Search.SearchPanel.Install(代码编辑器);
                搜索面板?.Open();
            }
            catch (Exception ex) { 异常收集C.记($"打开查找面板失败: {ex.Message}"); }
        }

        /// <summary>
        /// 标出"当前执行到哪一行"(单步调试用;行号从 1 开始)。
        /// 行号由主窗口换算好再传进来(编辑器不认识机器码);
        /// 深黄 = 汇编单步,浅黄 = 语言单步。
        /// </summary>
        public void 设执行行(int 行号, bool 深黄 = false)
        {
            try
            {
                if (代码编辑器 == null) return;
                if (行号 <= 0 || 行号 > 代码编辑器.Document.LineCount) return;
                _当前执行行号 = 行号;
                _执行行深黄 = 深黄;
                滚到这一行(行号);          // 跳到别处(比如进函数)时自动跟过去
                try { 代码编辑器.TextArea.TextView.Redraw(); } catch { }
            }
            catch (Exception ex) { 异常收集C.记($"标执行行失败: {ex.Message}"); }
        }

        /// <summary>
        /// 标出"调用处":整行淡灰(行号)+ 同一行里的**调用跨度**(C15,按层号赤橙黄绿青蓝紫叠色)。
        /// 跨度要用编辑器里这份正文算(编译器只给行、不给列),所以计算放在编辑器这一侧 ——
        /// 规则本身在引擎 `调用跨度C.算`(纯函数,自检直接喂正文钉着)。
        /// </summary>
        public void 设调用处(IEnumerable<int> 行号们, IReadOnlyList<调用帧C>? 帧表, string? 只看文件)
        {
            try
            {
                _调用处行.Clear();
                foreach (int 行号 in 行号们)
                    if (行号 > 0 && !_调用处行.Contains(行号)) _调用处行.Add(行号);
                _调用处跨度.Clear();
                if (帧表 is { Count: > 0 })
                    _调用处跨度.AddRange(调用跨度C.算(帧表, 取自源码行, 只看文件));
                代码编辑器?.TextArea.TextView.Redraw();
            }
            catch { }
        }

        /// <summary> 行号(1 基)→ 这一行的正文(给"调用跨度"配对括号用;越界返回 null,绝不抛) </summary>
        private string? 取自源码行(int 行号)
        {
            try
            {
                var doc = 代码编辑器?.Document;
                if (doc == null || 行号 <= 0 || 行号 > doc.LineCount) return null;
                return doc.GetText(doc.GetLineByNumber(行号));
            }
            catch { return null; }
        }

        /// <summary>
        /// 滚到指定行。注意:编辑器外面还套了一层 ScrollViewer(**真正滚动的是它**),
        /// 所以先问"这一行在整篇里的纵坐标",再让外层滚过去;AvalonEdit 自带的 ScrollToLine 在这套结构里不管用。
        ///
        /// **什么时候滚**(判定在引擎侧 `执行行滚动C.定`,自检逐条钉着;用户 2026-09-19 实测反馈
        /// "黄条始终保持在编辑器中央…观感太跳"):
        ///   · 黄条在视口里、离底边还有 3 行以上 → **一动不动**(单步往下走时屏幕别跟着挪);
        ///   · 离底边只剩一两行 → 放到视口**中央**(把下面腾出来);
        ///   · 跑到视口上面去了 → 放回上三分之一(留出上下文,也免得黄条看不见)。
        /// </summary>
        private void 滚到这一行(int 行号)
        {
            try
            {
                var 视图 = 代码编辑器.TextArea.TextView;
                视图.EnsureVisualLines();
                var 可视 = 视图.VisualLines;
                int 首可见 = 可视.Count > 0 ? 可视[0].FirstDocumentLine.LineNumber : 行号;
                int 末可见 = 可视.Count > 0 ? 可视[^1].LastDocumentLine.LineNumber : 行号;
                var 方式 = 执行行滚动C.定(行号, 首可见, 末可见);
                if (方式 == 滚动方式C.不动) return;          // 黄条还在视口里:屏幕别动(观感就靠这条)
                double 顶 = 视图.GetVisualTopByDocumentLine(行号);
                // 视口就是编辑器自己的(外层那层 ScrollViewer 已拆,见 编辑器.xaml):用 TextEditor 自带的滚动口
                double 视口高 = 代码编辑器.ViewportHeight;
                代码编辑器.ScrollToVerticalOffset(Math.Max(0,
                    方式 == 滚动方式C.居中 ? 顶 - 视口高 / 2 : 顶 - 视口高 / 3));
            }
            catch
            {
                try { 代码编辑器.ScrollToLine(行号); } catch { }
            }
        }

        // ---------------- 汇编单步的"原语言源码"浮窗已撤(第158批) ----------------
        // 那个浮窗是配"编辑器换成展开汇编"的:现在单步不再换文档(见调试会话那套),
        // 编辑器里本来就是语言源码,浮窗没有意义,连同 XAML 里那一块一起删了。

        /// <summary> 清掉执行行高亮(这一条地址没有对应的源码行、或者当前标签页不是那个文件时用) </summary>
        public void 清执行行()
        {
            if (_当前执行行号 == null) return;
            _当前执行行号 = null;
            try { 代码编辑器?.TextArea.TextView.Redraw(); } catch { }
        }
        #endregion

        #region 应用主题配色
        /// <summary> 应用主题配色到本编辑器(高亮色/编辑器背景/行号) </summary>
        public void 应用主题(主题设置 主题)
        {
            try
            {
                // 配色全在 `配色词类C` 那张表里(词类 + 亮/暗默认色),这里只把主题的色值交给高亮器
                // —— 高亮器按词类键取刷子,引擎侧 `高亮扫描C` 决定每一段属于哪一类
                高亮器.应用主题(主题);
                if (代码编辑器 != null)
                {
                    代码编辑器.Background = 主题设置.转刷(主题.编辑器背景);
                    代码编辑器.Foreground = 主题设置.转刷(主题.编辑器前景);
                    try { 代码编辑器.LineNumbersForeground = 主题设置.转刷(主题.行号颜色); } catch { }
                    // 高亮刷已更新,强制重绘使行内高亮/前景立刻换色
                    try { 代码编辑器.TextArea.TextView.Redraw(); } catch { }
                }
                Dispatcher.BeginInvoke(new Action(() => 刷新高亮()), DispatcherPriority.Background);
            }
            catch { }
        }
        #endregion
        #region 控件生命周期(加载/卸载)
        private void CodeEditBox_Loaded(object? sender, RoutedEventArgs e)
        {
            // 初次加载后刷新高亮
            Dispatcher.BeginInvoke(new Action(() => 刷新高亮()), DispatcherPriority.Background);
        }
        private void CodeEditBox_Unloaded(object? sender, RoutedEventArgs e)
        {
            防抖定时器?.Stop();
            代码编辑器?.TextArea.TextEntering -= 自动成对输入;
            // 编辑器要卸载了:把还挂着的悬浮提示一起收掉(不然它跟着窗口活到最后)
            收起诊断提示(用户关的: false);
        }
        #endregion

        // ===== 成对输入(())[]{} 与引号、块注释 /**/ =====

        #region 成对输入:自动补右符(含注释内跳过)
        private bool 在注释或字符串内(int offset)
        {
            if (代码编辑器 == null || offset < 0 || offset > 代码编辑器.Document.TextLength) return false;
            if (代码编辑器.Document.TextLength > 30000) return false; // 大文档跳过,省性能
            var 区 = 分词器C.取注释区(代码编辑器.Text);
            return 区.Any(r => offset >= r.开始 && offset < r.结束);
        }

        private void 自动成对输入(object? sender, TextCompositionEventArgs e)
        {
            if (!选项.当前.自动成对) return;
            if (代码编辑器 == null || 结构操作中 || e.Text.Length != 1) return;
            char c = e.Text[0];
            int caret = 代码编辑器.CaretOffset;
            int selStart = 代码编辑器.SelectionStart;
            int selLen = 代码编辑器.SelectionLength;

            // 注释内不自动成对(注释/引号内容里按普通字符输入)
            if (在注释或字符串内(caret))
            {
                // 但允许“右符跳过”:已在注释里写成的 /**(/)… 手动跳过
                if (selLen == 0 && 关闭符.Contains(c) &&
                    caret < 代码编辑器.Document.TextLength && 代码编辑器.Document.GetCharAt(caret) == c)
                {
                    e.Handled = true;
                    代码编辑器.CaretOffset = caret + 1;
                }
                return;
            }

            // 块注释:输入 '*' 且前一字符是 '/' → 自动补 "**/" 光标停在中间
            if (c == '*' && caret >= 1 && 代码编辑器.Document.GetCharAt(caret - 1) == '/')
            {
                e.Handled = true;
                代码编辑器.Document.Insert(caret, "**/");
                代码编辑器.CaretOffset = caret + 1;
                return;
            }

            // 关闭符:若下一字符恰为关闭符 → 跳过不重复
            if (selLen == 0 && 关闭符.Contains(c) &&
                caret < 代码编辑器.Document.TextLength && 代码编辑器.Document.GetCharAt(caret) == c)
            {
                e.Handled = true;
                代码编辑器.CaretOffset = caret + 1;
                return;
            }

            // 开符:自动补对
            if (成对表.TryGetValue(c, out string? 右))
            {
                e.Handled = true;
                if (selLen > 0)
                {
                    string 选中 = 代码编辑器.SelectedText;
                    代码编辑器.SelectedText = c + 选中 + 右;
                    代码编辑器.Select(selStart + 1, 选中.Length);
                }
                else
                {
                    代码编辑器.Document.Insert(caret, c + 右);
                    代码编辑器.CaretOffset = caret + 1;
                }
            }
        }
        #endregion

        #region 撤销与重做
        /// <summary> 当前选中的字符数(菜单联动:没选中就没法复制/删除) </summary>
        public int 选中长度
        {
            get
            {
                try { return 代码编辑器?.SelectionLength ?? 0; }
                catch { return 0; }
            }
        }

        public bool CanUndo()
        {
            try
            {
                return 代码编辑器?.Document?.UndoStack?.CanUndo ?? false;
            }
            catch { return false; }
        }
        public void Undo()
        {
            try
            {
                if (代码编辑器?.Document?.UndoStack?.CanUndo == true) 代码编辑器.Document.UndoStack.Undo();
            }
            catch { }
        }
        public bool CanRedo()
        {
            try
            {
                return 代码编辑器?.Document?.UndoStack?.CanRedo ?? false;
            }
            catch { return false; }
        }
        public void Redo()
        {
            try
            {
                if (代码编辑器?.Document?.UndoStack?.CanRedo == true) 代码编辑器.Document.UndoStack.Redo();
            }
            catch { }
        }
        #endregion

    }
}
