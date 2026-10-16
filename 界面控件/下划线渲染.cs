using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// 把 诊断项C 画成**波浪下划线**:错误(红)/ 警告(黄)/ 提示(绿)。
    /// 画法和"展开区方框"同一套(IBackgroundRenderer),只是画的是一条锯齿线而不是框。
    /// </summary>
    internal class 下划线渲染 : IBackgroundRenderer
    {
        private readonly Func<IReadOnlyList<诊断项C>> _取诊断;

        private static readonly Pen 红笔 = 造笔(Color.FromRgb(0xE0, 0x3A, 0x3A));
        private static readonly Pen 黄笔 = 造笔(Color.FromRgb(0xCC, 0x9A, 0x00));
        private static readonly Pen 绿笔 = 造笔(Color.FromRgb(0x2E, 0x9E, 0x4F));

        private static Pen 造笔(Color 色)
        {
            var p = new Pen(new SolidColorBrush(色), 1.6);
            p.Freeze();
            return p;
        }

        public 下划线渲染(Func<IReadOnlyList<诊断项C>> 取诊断) =>
            _取诊断 = 取诊断 ?? throw new ArgumentNullException(nameof(取诊断));

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (textView?.Document == null || dc == null) return;
            var 诊断 = _取诊断();
            if (诊断 == null || 诊断.Count == 0) return;
            try
            {
                var doc = textView.Document;
                foreach (var d in 诊断)
                {
                    if (d.行 <= 0 || d.行 > doc.LineCount) continue;
                    var line = doc.GetLineByNumber(d.行);
                    int 起 = line.Offset + Math.Max(0, Math.Min(d.起列, line.Length));
                    int 止 = d.止列 > d.起列 ? line.Offset + Math.Min(d.止列, line.Length) : line.EndOffset;
                    if (止 <= 起) { 起 = line.Offset; 止 = line.EndOffset; }
                    var 笔 = d.级别 switch
                    {
                        诊断级别.错误 => 红笔,
                        诊断级别.警告 => 黄笔,
                        _ => 绿笔,
                    };
                    画波浪(textView, dc, 起, 止, 笔);
                }
            }
            catch { }
        }

        /// <summary> 在一段文本下面画波浪线:拿这段文本的字符矩形(可能有多个,比如跨了折行),在每个矩形底边画</summary>
        private static void 画波浪(TextView textView, DrawingContext dc, int 起偏移, int 止偏移, Pen 笔)
        {
            const double 波高 = 3.0, 步长 = 3.2;
            var 段 = new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = 起偏移, EndOffset = 止偏移 };
            foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(textView, 段))
            {
                double x0 = r.Left, x1 = r.Right, y = r.Bottom - 1.0;
                if (x1 - x0 < 2) x1 = x0 + 2;
                var 几何 = new StreamGeometry();
                using (var g = 几何.Open())
                {
                    g.BeginFigure(new Point(x0, y), false, false);
                    bool 上 = true;
                    for (double x = x0; x < x1; x += 步长)
                    {
                        g.LineTo(new Point(Math.Min(x + 步长, x1), y + (上 ? -波高 : 0)), true, false);
                        上 = !上;
                    }
                }
                几何.Freeze();
                dc.DrawGeometry(null, 笔, 几何);
            }
        }
    }
}
