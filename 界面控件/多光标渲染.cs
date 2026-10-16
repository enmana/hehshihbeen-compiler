using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// **多光标**的那几个"副光标"(P40,2026-10-07):主光标由 AvalonEdit 自己画,
    /// 这里只把另外几个画出来 —— 细竖线 + 半透明底,和主光标一眼分得开(主光标是实心的)。
    ///
    /// 画法同 `括号方框`(按字符取视觉矩形),所以自动换行之后落在两行上的光标也画得对。
    /// </summary>
    internal class 多光标渲染 : IBackgroundRenderer
    {
        private readonly Func<IReadOnlyList<int>> _取位置;

        private static readonly Pen 线笔 = new(new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x88, 0xE5)), 1.4);
        private static readonly SolidColorBrush 底刷 = new(Color.FromArgb(0x22, 0x1E, 0x88, 0xE5));

        static 多光标渲染()
        {
            线笔.Freeze();
            底刷.Freeze();
        }

        public 多光标渲染(Func<IReadOnlyList<int>> 取位置) =>
            _取位置 = 取位置 ?? throw new ArgumentNullException(nameof(取位置));

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (textView?.Document == null || dc == null) return;
            var 位置们 = _取位置();
            if (位置们 == null || 位置们.Count == 0) return;
            try
            {
                var doc = textView.Document;
                foreach (int 位 in 位置们)
                {
                    if (位 < 0 || 位 > doc.TextLength) continue;
                    // 一个位置画成"一个宽 1.4 的竖条":用一个字符宽度的空段取矩形,
                    // 文档尾(没有下一个字符)时退化成取它前一个字符的右边缘
                    int 起 = 位;
                    int 止 = Math.Min(位 + 1, doc.TextLength);
                    if (止 <= 起)
                    {
                        if (doc.TextLength == 0) continue;
                        起 = doc.TextLength - 1;
                        止 = doc.TextLength;
                    }
                    var 段 = new TextSegment { StartOffset = 起, EndOffset = 止 };
                    var 建框 = new BackgroundGeometryBuilder();
                    建框.AddSegment(textView, 段);
                    var 几何 = 建框.CreateGeometry();
                    if (几何 == null || 几何.IsEmpty()) continue;
                    dc.DrawGeometry(底刷, 线笔, 几何);
                }
            }
            catch { }
        }
    }
}
