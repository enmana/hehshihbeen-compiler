using hehshihbeen_compiler.界面控件;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 编辑器的一部分:**配对括号高亮 + 签名提示**(P37,2026-10-07;用户排的七件里的第 4 件)。
    ///
    /// 两条都是"手感"件,规则全在引擎侧(`引擎\括号配对C.cs`,自检直接喂文本真跑):
    ///   · **括号配对**:光标贴着某个括号时,把**它和它的另一半**各画一个小方框
    ///     —— 注释与字符串里的括号不算,找不到就不画;
    ///   · **签名提示**:敲出 `(` 之后,光标下面显示那个函数的参数表,并且**当前那个参数加粗**
    ///     (参数表与说明来自补全词表 / 最近一次编译的名字表 —— 与智能补全同一份事实源)。
    /// 这里只做三件事:算出来、画上去、在光标动/文本变的时候重算一次。
    /// </summary>
    public partial class 编辑器 : UserControl
    {
        #region 配对括号 + 签名提示的状态

        /// <summary> 现在要画框的那一对括号(没有就是 null)</summary>
        private 括号对C? _括号对;

        /// <summary> 现在显示着的签名(没有就是 null)</summary>
        private 签名提示C? _签名;

        /// <summary>
        /// 名字 → 签名。主窗口在"打开工程 / 编译过一遍"之后灌一次(`MainWindow.代码检查.cs` 的 `刷新签名表`),
        /// 编辑器只在敲出 `(` 那一刻查表 —— 与智能补全共用同一份事实源,不另起一份。
        /// </summary>
        public static Dictionary<string, 签名提示C> 签名表 { get; } = new(StringComparer.Ordinal);

        /// <summary> 把主窗口算好的签名灌进来(整份替换:编译一次就是一份新的;带模块前缀的再记一个裸名)</summary>
        public static void 设签名表(IEnumerable<签名提示C> 签名们)
        {
            try
            {
                签名表.Clear();
                foreach (var 签 in 签名们)
                {
                    if (签.名字.Length == 0) continue;
                    签名表[签.名字] = 签;
                    string 裸 = 尾段(签.名字);
                    if (裸.Length > 0 && !签名表.ContainsKey(裸)) 签名表[裸] = 签;
                }
            }
            catch { }
        }

        private static string 尾段(string 名)
        {
            int 点 = 名.LastIndexOf('.');
            return 点 < 0 ? 名 : 名[(点 + 1)..];
        }

        #endregion

        #region 重算:光标一动 / 文本一变都走这里

        /// <summary>
        /// 重算配对括号与签名提示(光标位置变了 / 文本变了都调一次)。
        /// 三件事各自 try:一件出问题不许连累另外两件(观感件坏了也不能影响编辑)。
        /// </summary>
        private void 刷新括号与签名()
        {
            string 文;
            int 位;
            try
            {
                var 编 = 代码编辑器;
                if (编?.Document == null) { 收起括号与签名(); return; }
                文 = 编.Text;
                位 = 编.CaretOffset;
            }
            catch { 收起括号与签名(); return; }

            // ---- ① 配对括号 ----
            try
            {
                var 新对 = 括号配对C.找(文, 位);
                if (新对 != _括号对)
                {
                    _括号对 = 新对;
                    重画括号();
                }
            }
            catch { _括号对 = null; }

            // ---- ② 签名提示 ----
            try { 更新签名提示(文, 位); }
            catch { 收起签名(); }
        }

        private void 重画括号()
        {
            try { 代码编辑器?.TextArea.TextView.InvalidateLayer(KnownLayer.Selection); } catch { }
        }

        private void 收起括号与签名()
        {
            if (_括号对 != null)
            {
                _括号对 = null;
                重画括号();
            }
            收起签名();
        }

        #endregion

        #region 签名提示:算与画

        /// <summary>
        /// 光标在 `位` 时,`(` 里面那个调用该显示什么签名。
        /// 查不到(不是调用 / 表里没有这个名字)就**收起** —— 宁可不显示,也不显示一个猜的签名。
        /// </summary>
        private void 更新签名提示(string 文, int 位)
        {
            if (签名表.Count == 0
                || FindName("签名Popup") is not Popup 弹
                || FindName("签名文本") is not TextBlock 文本块)
            {
                收起签名();
                return;
            }
            if (签名提示规则C.正在调用(文, 位) is not { } 呼 || !签名表.TryGetValue(呼.名字, out var 签))
            {
                收起签名();
                return;
            }

            int 第几个 = 签名提示规则C.第几个参数(文, 呼.左括号位置, 位);
            var 段们 = 签名规则段(签);
            _签名 = 签;

            文本块.Inlines.Clear();
            文本块.Inlines.Add(new Run(签.名字) { Foreground = Brushes.DimGray });
            if (签.显示括号) 文本块.Inlines.Add(new Run("("));
            int 亮 = 段们.Count == 0 ? -1 : Math.Min(第几个, 段们.Count - 1);
            for (int i = 0; i < 段们.Count; i++)
            {
                if (i > 0) 文本块.Inlines.Add(new Run(", "));
                var 一 = new Run(段们[i]);
                if (i == 亮)
                {
                    一.FontWeight = FontWeights.Bold;
                    一.Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
                }
                文本块.Inlines.Add(一);
            }
            if (签.显示括号) 文本块.Inlines.Add(new Run(")"));
            if (签.返回类型.Length > 0)
                文本块.Inlines.Add(new Run(" 返回 " + 签.返回类型) { Foreground = Brushes.DimGray });

            if (FindName("签名说明") is TextBlock 说明块)
            {
                说明块.Text = 签.说明;
                说明块.Visibility = 签.说明.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            }

            var 落点 = 算提示落点();
            弹.HorizontalOffset = 落点.X;
            弹.VerticalOffset = 落点.Y;
            if (!弹.IsOpen) 弹.IsOpen = true;
        }

        /// <summary> 参数段(缓存进签名对象里;光标在括号里挪来挪去时不必重切)</summary>
        private static List<string> 签名规则段(签名提示C 签)
        {
            if (签.参数段.Count == 0 && 签.参数原文.Trim().Length > 0)
                签.参数段 = 签名提示规则C.切参数(签.参数原文);
            return 签.参数段;
        }

        /// <summary> 收起签名提示(光标离开调用 / 滚走了 / 换了文本都走它)</summary>
        private void 收起签名()
        {
            _签名 = null;
            try
            {
                if (FindName("签名Popup") is Popup 弹 && 弹.IsOpen) 弹.IsOpen = false;
            }
            catch { }
        }

        /// <summary>
        /// 提示框该落在哪(与候选框同一份口径:`弹窗定位C.算` —— 下面放不下就翻到光标上面、
        /// 左右夹在编辑区里,整个框绝不出编辑区)。
        /// </summary>
        private Point 算提示落点()
        {
            if (代码编辑器 == null) return new Point(0, 0);
            try
            {
                var textView = 代码编辑器.TextArea.TextView;
                textView.EnsureVisualLines();
                var 光标矩形 = 代码编辑器.TextArea.Caret.CalculateCaretRectangle();
                var 视口点 = new Point(光标矩形.X - textView.ScrollOffset.X,
                                        光标矩形.Y - textView.VerticalOffset + 光标矩形.Height);
                var 视点 = textView.TranslatePoint(视口点, 代码编辑器);
                double 行高 = 光标矩形.Height > 0 ? 光标矩形.Height : 代码编辑器.FontSize * 1.4;
                var (宽, 高) = 量签名框大小();
                var (x, y) = 弹窗定位C.算(视点.X, 视点.Y, 行高, 宽, 高, 代码编辑器.ActualWidth, 代码编辑器.ActualHeight);
                return new Point(x, y);
            }
            catch { return new Point(0, 0); }
        }

        private (double 宽, double 高) 量签名框大小()
        {
            double 宽 = 240, 高 = 26;
            try
            {
                if (FindName("签名Popup") is Popup 弹 && 弹.Child is FrameworkElement 孩子)
                {
                    孩子.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (孩子.DesiredSize.Width > 0) 宽 = 孩子.DesiredSize.Width;
                    if (孩子.DesiredSize.Height > 0) 高 = 孩子.DesiredSize.Height;
                }
            }
            catch { }
            return (宽, 高);
        }

        #endregion

        #region 接线:挂渲染器 + 订光标与文本变化

        /// <summary>
        /// 编辑器造好时调一次:把括号方框挂到背景层,把"光标动了 / 滚走了"两个口子接上。
        /// 都包在 try 里 —— 这是观感件,装不上照旧能编辑。
        /// </summary>
        private void 装上括号与签名()
        {
            try
            {
                if (代码编辑器 == null) return;
                代码编辑器.TextArea.TextView.BackgroundRenderers.Add(new 括号方框(() => _括号对));
                // 光标一动就重算(方向键 / 鼠标点 / 敲字符都会走这条路)
                代码编辑器.TextArea.Caret.PositionChanged += (_, _) => 刷新括号与签名();
                // 滚走了就别让签名框留在原处(方框由 AvalonEdit 自己按新位置重画,Popup 得自己收)
                代码编辑器.TextArea.TextView.ScrollOffsetChanged += (_, _) => { if (_签名 != null) 收起签名(); };
                刷新括号与签名();
            }
            catch { }
        }

        #endregion
    }
}
