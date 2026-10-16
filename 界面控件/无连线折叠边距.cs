using System.Windows.Media;
using ICSharpCode.AvalonEdit.Folding;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// 折叠边距的**替身**:**只留 `+` / `-` 标记,不画 AvalonEdit 那两条"折叠范围线"**(第152批)。
    ///
    /// **为什么要有它**(用户 2026-09-20 截图点名的"取消折叠符(+/-)那一栏的对齐线"):
    /// AvalonEdit 的 `FoldingMargin.OnRender` 除了画标记,还会画两条线 ——
    /// ① **竖线**:从折叠起点那一行拉到结束那一行,画在**边距中线**上(`DrawFoldLines` 里 `markerXPos = RenderSize.Width / 2`);
    /// ② **横线**:结束那一行从边距中线拉到边距右缘(结束提示)。
    /// 两条都取 `FoldingMarkerBrush`(默认 `Brushes.Gray`),而且**只由边距自己画** ——
    /// 于是最左边就多出一条灰竖线,紧挨着 `+` / `-` 那一列(用户把它当成"对齐线")。
    ///
    /// **怎么去掉**:那两条线只在 `OnRender` 里画;**`+` / `-` 标记不是** ——
    /// 它们是 `FoldingMarginMarker`(**视觉子元素**,由基类 `TextViewVisualLinesChanged` 建/删/摆位),
    /// WPF 自己会把子元素画出来。所以这里只要把 `OnRender` 空掉:线没了,标记、展开/收起、
    /// 嵌套、鼠标悬停高亮**全都照旧**(那些逻辑都在基类里,一个字没改)。
    ///
    /// **换上去/摘下来**都归 `编辑器.装上折叠器()` / `卸掉折叠器()` 管:
    /// `FoldingManager.Uninstall` 只摘**它自己装的那个**边距,不认得我们这个替身,所以要手动摘。
    /// </summary>
    internal sealed class 无连线折叠边距 : FoldingMargin
    {
        /// <summary>
        /// 有意**不调** `base.OnRender`:`FoldingMargin` 在那里画折叠范围线(竖线 + 结束横线)。
        /// 标记是视觉子元素,不走这里,所以标记照常显示。
        /// </summary>
        protected override void OnRender(DrawingContext drawingContext)
        {
            // 什么都不画 —— 这正是这个替身存在的理由。
        }
    }
}
