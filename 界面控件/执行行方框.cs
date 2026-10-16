using System;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// 在当前执行行画一条半透明底色(单步调试用)。两种颜色:
    /// 语言单步 = 黄(淡),汇编单步 = 深黄(浓),一眼能看出现在是在哪个模式里单步。
    /// </summary>
    internal class 执行行方框 : IBackgroundRenderer
    {
        private readonly Func<int?> _取行号;
        private readonly Func<bool> _取深色;

        /// <summary> 语言单步:黄 </summary>
        private static readonly SolidColorBrush 黄刷 = new(Color.FromArgb(0x55, 0xFF, 0xD4, 0x00));
        /// <summary> 汇编单步:深黄 </summary>
        private static readonly SolidColorBrush 深黄刷 = new(Color.FromArgb(0xAA, 0xFF, 0xB3, 0x00));

        static 执行行方框()
        {
            黄刷.Freeze();
            深黄刷.Freeze();
        }

        public 执行行方框(Func<int?> 取行号, Func<bool> 取深色)
        {
            _取行号 = 取行号 ?? throw new ArgumentNullException(nameof(取行号));
            _取深色 = 取深色 ?? throw new ArgumentNullException(nameof(取深色));
        }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (textView == null || dc == null || textView.Document == null) return;
            int? 行号 = _取行号();
            if (行号 == null) return;
            try
            {
                var doc = textView.Document;
                if (行号 <= 0 || 行号 > doc.LineCount) return;
                var line = doc.GetLineByNumber(行号.Value);
                var seg = new TextSegment { StartOffset = line.Offset, EndOffset = line.EndOffset };
                var 建框 = new BackgroundGeometryBuilder();
                建框.AddSegment(textView, seg);
                var 几何 = 建框.CreateGeometry();
                if (几何 == null || 几何.IsEmpty()) return;
                dc.DrawGeometry(_取深色() ? 深黄刷 : 黄刷, null, 几何);
            }
            catch { }
        }
    }
}
