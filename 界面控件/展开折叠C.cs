using ICSharpCode.AvalonEdit.Editing;
using System.Windows;
using System.Windows.Media;

namespace hehshihbeen_compiler.界面控件
{
    // 说明(第83批):
    //   这里原来放两个东西 —— `展开折叠`(转发给 代码解析器的解析定位)与 `展开折叠边距`(行号右侧画 ＋/−、
    //   点一下把这一段源码**就地换成汇编**)。用户拍板取消了"就地展开"这个功能:
    //   汇编是展开的产物,改一两个值还对得上源码、改一大段就回不去了 —— 那个 +/− 看着像"查看",实际在改源码。
    //   现在那条边距的位置换成**代码折叠**(AvalonEdit 的 FoldingManager + FoldingMargin,见 编辑器.刷新折叠区)。
    //   `代码解析器` 里那套块配对/注释遮蔽还在(折叠与其它检查都在用),只是不再有"就地展开"的入口。
    #region 空隙边距:把文本区整体右推的空白占位
    /// <summary> 仅用于把文本区整体往右推(空边距,不画内容不拦点击) </summary>
    internal class 空隙边距(double 宽) : AbstractMargin
    {
        private readonly double _宽 = 宽;

        protected override Size MeasureOverride(Size availableSize) => new(_宽, 0);
        protected override void OnRender(DrawingContext dc) { }
    }
    #endregion
}
