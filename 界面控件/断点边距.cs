using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// 行号左边的断点边距:有断点的行画一个红点,点一下切换断点(和 VS 一样的手感)。
    /// 断点数据本身在主窗口那儿(跟着工程走),这里只负责"画 + 报点一下的行号"。
    /// </summary>
    internal class 断点边距 : AbstractMargin
    {
        private readonly List<int> _行表 = [];
        private readonly List<int> _附加行表 = [];
        private Action<int>? _点击回调;
        private static readonly double 宽 = 18;
        private static readonly Brush 红刷 = Brushes.IndianRed;
        private static double _像素比 = 1.0;

        public void 绑定(Action<int> 点击回调) => _点击回调 = 点击回调;

        public void 更新标记(List<int> 行表)
        {
            _行表.Clear();
            _行表.AddRange(行表);
            InvalidateVisual();
        }

        /// <summary>
        /// P34:"带附加条件"(命中条件 / 命中次数 / 日志断点)的那几行 —— 画成**空心红圈**。
        /// 为什么要有这个区分:日志断点、`%5` 这种断点**不一定停**,画得跟普通断点一模一样,
        /// 用户会以为"断点坏了"(实测口径:一眼看得出"这个断点不是每次都停")。
        /// </summary>
        public void 更新附加标记(List<int> 附加行表)
        {
            _附加行表.Clear();
            _附加行表.AddRange(附加行表);
            InvalidateVisual();
        }

        /// <summary>
        /// **滚动/换行以后必须重画**(第144批,用户实测报的 bug:红点跟着滚动条跑)。
        ///
        /// `OnRender` 里的纵坐标是 `VisualTop − TextView.VerticalOffset` —— 也就是说**画出来的位置是死的**,
        /// 滚动之后必须再画一遍才对得上代码行。AvalonEdit 自带的 `LineNumberMargin` 就是这么活的
        /// (它订了 `VisualLinesChanged` 并 `InvalidateVisual`);我们这个边距原来只在"断点表变了"时才重画,
        /// 于是滚动时红点**留在原处不动**、代码却滚走了 —— 看过去就是红点在整篇里漂。
        /// 不弹窗探针(c16)实测:滚动 0 → 60 → 120 像素,红点始终停在 y=116(应该在 116 → 57 → 看不见)。
        /// 两个事件都订:换行集(`VisualLinesChanged`)与滚动(`ScrollOffsetChanged`),
        /// 免得"只滚了半行、可视行没变"那种情况下少画一次。
        /// </summary>
        protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
        {
            if (oldTextView != null)
            {
                oldTextView.VisualLinesChanged -= 视图变了;
                oldTextView.ScrollOffsetChanged -= 视图变了;
            }
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView != null)
            {
                newTextView.VisualLinesChanged += 视图变了;
                newTextView.ScrollOffsetChanged += 视图变了;
            }
            InvalidateVisual();
        }

        private void 视图变了(object? 谁, EventArgs 什么) => InvalidateVisual();

        public 断点边距()
        {
            // 注意:AbstractMargin 没有 Background 属性 —— 整条都能点到靠在 OnRender 里铺一层透明矩形
            Loaded += (_, _) => _像素比 = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            _ = CultureInfo.CurrentUICulture;      // 保持与其它边距一致(没事干,纯占位)
            return new(宽, 0);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (TextView == null) return;
            // 先铺一层透明底:保证整条边距(含没有断点的行)都能接收鼠标,点一下就能加断点
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            foreach (var 视觉行 in TextView.VisualLines)
            {
                int 行号 = 视觉行.FirstDocumentLine.LineNumber;
                if (!_行表.Contains(行号)) continue;
                double y = 视觉行.VisualTop + 视觉行.Height / 2 - TextView.VerticalOffset;
                // P34:带附加条件的画空心圈(日志断点 / `%5` 这种不一定停的,一眼看得出来)
                if (_附加行表.Contains(行号)) dc.DrawEllipse(null, new Pen(红刷, 1.6), new Point(宽 / 2, y), 5, 5);
                else dc.DrawEllipse(红刷, null, new Point(宽 / 2, y), 5, 5);
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (TextView == null || _点击回调 == null) return;
            double y = e.GetPosition(this).Y;
            foreach (var 视觉行 in TextView.VisualLines)
            {
                double 顶 = 视觉行.VisualTop - TextView.VerticalOffset;
                if (y >= 顶 && y < 顶 + 视觉行.Height)
                {
                    e.Handled = true;
                    _点击回调(视觉行.FirstDocumentLine.LineNumber);
                    return;
                }
            }
        }
    }
}
