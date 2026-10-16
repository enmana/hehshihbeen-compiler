using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// 把 `缩进导引线C.算` 出来的竖线画到编辑器**背景层**(C16-3)。
    ///
    /// 两条纪律(与逐行高亮同一套):
    ///   · **规则在引擎** —— 这里只把"第几列"换算成像素:横坐标走 `TextView.GetVisualPosition`,
    ///     它给的是**文档坐标、不随滚动走**(第144批实测:滚动 200 像素时 `CalculateCaretRectangle`
    ///     还给 `Y=492.9`,而那一行的可视行顶只有 `292.9`),所以 X 要减 `ScrollOffset.X`;
    ///     纵坐标用可视行的 `VisualTop − textView.VerticalOffset`;
    ///   · **只画看得见的那几十行** —— 遍历 `TextView.VisualLines`,整篇的竖线表按**文档版本**缓存。
    ///
    /// **颜色跟着括号那一层的彩虹色**(第151批改的口径):每段自带层号,
    /// 走 `语法高亮C.取花括号线刷`(同一套 7 档色相 + `花括号层号C.导引线透明度`)。
    /// 第一版是一条半透明灰,用户看过说"太淡、也不跟括号同色";现在第 2 层起每条线就是它那一层的颜色。
    /// </summary>
    internal class 缩进导引线渲染 : IBackgroundRenderer
    {
        private readonly 语法高亮C 高亮器;

        /// <summary>
        /// H8:选项页里那个 `缩进导引线` 开关。**关掉只是不画** —— 折叠、高亮、编译一个字节都不动,
        /// 也不去动 `BackgroundRenderers` 那张表(挂 / 摘容易漏,一个布尔位最稳)。
        /// 由 `编辑器.应用选项()` 从 `配置.选项.当前.缩进导引线` 设进来。
        /// </summary>
        public bool 显示 { get; set; } = true;

        /// <summary> 色档(0~6)→ 刷子 + 笔(同一档反复用,别每个可视行都造一根) </summary>
        private readonly (Brush 刷, Pen 笔)?[] _笔 = new (Brush, Pen)?[7];

        private ITextSourceVersion? _上次版本;
        private IReadOnlyDictionary<int, List<缩进导引线C.导引段C>>? _表;

        public 缩进导引线渲染(语法高亮C 高亮器)
        {
            this.高亮器 = 高亮器 ?? throw new ArgumentNullException(nameof(高亮器));
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (!显示) return;                               // H8:选项页关掉了(连算表都不必)
            if (textView == null || dc == null || textView.Document == null) return;
            if (!textView.VisualLinesValid) return;          // 还没排版出来就别画(C12 那次卡死之后立下的规矩)
            取这一版(textView.Document);
            if (_表 == null || _表.Count == 0) return;
            try
            {
                foreach (var 可视行 in textView.VisualLines)
                {
                    int 行号 = 可视行.FirstDocumentLine.LineNumber;
                    var 段表 = 缩进导引线C.取行段(_表, 行号);
                    if (段表.Count == 0) continue;
                    double 顶 = 可视行.VisualTop - textView.VerticalOffset;
                    double 高 = 可视行.Height;
                    if (高 <= 0) continue;
                    foreach (var 段 in 段表)
                    {
                        double x = 取列X(textView, 可视行, 段.列);
                        if (double.IsNaN(x)) continue;
                        var 一根 = 取笔(花括号层号C.取色档(段.层号));
                        dc.DrawLine(一根, new Point(x, 顶 + 高 * 段.起比例), new Point(x, 顶 + 高 * 段.止比例));
                    }
                }
            }
            catch { /* 导引线只是观感:画不出来绝不影响编辑/编译 */ }
        }

        /// <summary> 这一版文档的竖线表:同一个版本(AvalonEdit 每改一次文本换一个版本对象)只算一次 </summary>
        private void 取这一版(TextDocument doc)
        {
            var 版本 = doc.Version;
            if (_表 != null && ReferenceEquals(版本, _上次版本)) return;
            _上次版本 = 版本;
            try { _表 = 缩进导引线C.算(doc.Text); }
            catch { _表 = null; }
        }

        /// <summary> 第 `列` 列在这一行里的横坐标(视口坐标;算不出来给 NaN,调用方跳过) </summary>
        private static double 取列X(TextView textView, VisualLine 行, int 列)
        {
            try
            {
                var 位 = new TextViewPosition(行.FirstDocumentLine.LineNumber, 列 + 1);
                var 点 = textView.GetVisualPosition(位, VisualYPosition.TextTop);
                return 点.X - textView.ScrollOffset.X;
            }
            catch { return double.NaN; }
        }

        /// <summary>
        /// 色档(0~6)→ 笔(1 像素,颜色 = 那一层的彩虹色 + 导引线透明度;第一次用到才造)。
        /// **H8:笔跟着刷子走** —— 选项页改过颜色之后 `语法高亮C` 会造一批新刷子,
        /// 这里比一下刷子是不是同一个,不是就重造笔(不然屏上还是老颜色)。
        /// </summary>
        private Pen 取笔(int 色档)
        {
            int 归一 = ((色档 % 7) + 7) % 7;
            var 刷 = 高亮器.取花括号线刷(归一);
            if (_笔[归一] is { } 已有 && ReferenceEquals(已有.刷, 刷)) return 已有.笔;
            var 新 = new Pen(刷, 1.0);
            新.Freeze();
            _笔[归一] = (刷, 新);
            return 新;
        }
    }
}
