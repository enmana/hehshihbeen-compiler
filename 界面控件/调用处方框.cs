using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// 把"调用处"画出来:执行被 调用 跳走以后,还看得出是从哪一行、哪一段跳过来的(找得回来)。
    /// 调用返回时由主窗口把对应的一段撤掉。画在 Background 层,所以当前执行行那条黄条会盖在它上面。
    ///
    /// **两层(C15,2026-09-19 用户拍板)**:
    ///   ① 底下一层还是整行淡灰(行号由会话给)—— "我是从这一行进来的";
    ///   ② 上面再按**调用跨度**叠色:同一行里层层往外调(`测试输出(打招呼(叫号(2)));`)时,
    ///      每一层只涂自己那一段,颜色按层号取 **赤橙黄绿青蓝紫** 七档、第 8 层起循环
    ///      —— 越内层越靠后画,同一段上叠着半透明色,自然更深(一眼看出"我在第几层")。
    /// </summary>
    internal class 调用处方框 : IBackgroundRenderer
    {
        private readonly Func<IReadOnlyList<int>> _取行号表;
        private readonly Func<IReadOnlyList<调用段C>> _取跨度表;
        private static readonly SolidColorBrush 灰刷 = new(Color.FromArgb(0x44, 0x80, 0x80, 0x80));

        /// <summary> 层号 → 颜色:赤 / 橙 / 黄 / 绿 / 青 / 蓝 / 紫(半透明,叠着画;第 8 层回到赤色) </summary>
        private static readonly SolidColorBrush[] 层刷 =
        [
            new(Color.FromArgb(0x55, 0xE0, 0x38, 0x38)),   // 赤
            new(Color.FromArgb(0x55, 0xF0, 0x8C, 0x20)),   // 橙
            new(Color.FromArgb(0x55, 0xE6, 0xC8, 0x18)),   // 黄
            new(Color.FromArgb(0x55, 0x38, 0xB0, 0x58)),   // 绿
            new(Color.FromArgb(0x55, 0x28, 0xB0, 0xC0)),   // 青
            new(Color.FromArgb(0x55, 0x3C, 0x68, 0xE0)),   // 蓝
            new(Color.FromArgb(0x55, 0x98, 0x48, 0xD0)),   // 紫
        ];

        /// <summary> 档数 = 颜色数;超过就循环(拍板:7 层,第 8 层回到赤色) </summary>
        public static int 档数 => 层刷.Length;

        static 调用处方框()
        {
            灰刷.Freeze();
            foreach (var 刷 in 层刷) 刷.Freeze();
        }

        public 调用处方框(Func<IReadOnlyList<int>> 取行号表, Func<IReadOnlyList<调用段C>> 取跨度表)
        {
            _取行号表 = 取行号表 ?? throw new ArgumentNullException(nameof(取行号表));
            _取跨度表 = 取跨度表 ?? throw new ArgumentNullException(nameof(取跨度表));
        }

        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (textView == null || dc == null || textView.Document == null) return;
            var doc = textView.Document;
            画整行灰(textView, dc, doc);
            画跨度(textView, dc, doc);
        }

        /// <summary> 最外那一层:整行淡灰(哪一行进来的) </summary>
        private void 画整行灰(TextView textView, DrawingContext dc, TextDocument doc)
        {
            var 行号表 = _取行号表();
            if (行号表 == null || 行号表.Count == 0) return;
            try
            {
                foreach (int 行号 in 行号表)
                {
                    if (行号 <= 0 || 行号 > doc.LineCount) continue;
                    var line = doc.GetLineByNumber(行号);
                    var seg = new TextSegment { StartOffset = line.Offset, EndOffset = line.EndOffset };
                    var 建框 = new BackgroundGeometryBuilder();
                    建框.AddSegment(textView, seg);
                    var 几何 = 建框.CreateGeometry();
                    if (几何 == null || 几何.IsEmpty()) continue;
                    dc.DrawGeometry(灰刷, null, 几何);
                }
            }
            catch { }
        }

        /// <summary> 里面那几层:按跨度 + 层号上色(层号越大画得越晚,叠在下面那些上面 → 越内层越深) </summary>
        private void 画跨度(TextView textView, DrawingContext dc, TextDocument doc)
        {
            var 跨度表 = _取跨度表();
            if (跨度表 == null || 跨度表.Count == 0) return;
            try
            {
                foreach (var 段 in 跨度表)
                {
                    if (段.行 <= 0 || 段.行 > doc.LineCount) continue;
                    var line = doc.GetLineByNumber(段.行);
                    int 起 = line.Offset + Math.Max(0, 段.起);
                    int 止 = line.Offset + Math.Max(0, 段.止);
                    if (止 <= 起) continue;
                    if (起 >= line.EndOffset) continue;
                    if (止 > line.EndOffset) 止 = line.EndOffset;
                    var seg = new TextSegment { StartOffset = 起, EndOffset = 止 };
                    var 建框 = new BackgroundGeometryBuilder();
                    建框.AddSegment(textView, seg);
                    var 几何 = 建框.CreateGeometry();
                    if (几何 == null || 几何.IsEmpty()) continue;
                    dc.DrawGeometry(层刷[调用跨度C.档位(段.层号, 档数)], null, 几何);
                }
            }
            catch { }
        }
    }
}
