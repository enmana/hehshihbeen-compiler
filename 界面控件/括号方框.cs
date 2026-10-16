using System;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// **配对括号高亮**(P37,2026-10-07):光标贴着某个括号时,把**它和它的另一半**各画一个小方框。
    ///
    /// 画法与 `执行行方框` 同源(AvalonEdit 的 `BackgroundGeometryBuilder`),但用的是
    /// **按字符**的段(不是整行)—— `AddSegment` 会把那一个字符的每一个视觉矩形都加进去,
    /// 所以自动换行之后落在两行上的括号也画得对。
    ///
    /// 哪一对由引擎那边的纯函数算(`括号配对C.找`:注释与字符串里的括号不算、找不到就不画),
    /// 这里只负责"把两个下标画成两个框"。
    /// </summary>
    internal class 括号方框 : IBackgroundRenderer
    {
        private readonly Func<括号对C?> _取一对;
        private bool _开着 = true;

        /// <summary> 方框颜色(与主题无关:它是"当前这一对"的记号,太艳会吵、太淡看不见)</summary>
        private static readonly Pen 框笔 = new(new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x88, 0xE5)), 1.2);
        private static readonly SolidColorBrush 底刷 = new(Color.FromArgb(0x33, 0x1E, 0x88, 0xE5));

        static 括号方框()
        {
            框笔.Freeze();
            底刷.Freeze();
        }

        public 括号方框(Func<括号对C?> 取一对) =>
            _取一对 = 取一对 ?? throw new ArgumentNullException(nameof(取一对));

        /// <summary> 开关(这一批没进选项页;留着给以后"想关掉它"用)</summary>
        public bool 显示 { get => _开着; set => _开着 = value; }

        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (!_开着 || textView == null || dc == null || textView.Document == null) return;
            if (_取一对() is not { } 对) return;
            try
            {
                var doc = textView.Document;
                画一个(textView, dc, doc, 对.左起);
                画一个(textView, dc, doc, 对.右起);
            }
            catch { }
        }

        private static void 画一个(TextView textView, DrawingContext dc, TextDocument doc, int 下标)
        {
            if (下标 < 0 || 下标 >= doc.TextLength) return;
            var 段 = new TextSegment { StartOffset = 下标, EndOffset = 下标 + 1 };
            var 建框 = new BackgroundGeometryBuilder { CornerRadius = 2 };
            建框.AddSegment(textView, 段);
            var 几何 = 建框.CreateGeometry();
            if (几何 == null || 几何.IsEmpty()) return;
            dc.DrawGeometry(底刷, 框笔, 几何);
        }
    }
}
