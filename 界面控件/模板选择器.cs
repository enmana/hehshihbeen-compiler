using System.Windows;
using System.Windows.Controls;
using hehshihbeen_compiler.选卡控件;

// ⚠ 命名空间**必须**与 MainWindow.xaml 的 `xmlns:local="clr-namespace:hehshihbeen_compiler"` 对齐:
// 这两个选择器是 XAML 里用 `<local:标签头模板选择器/>` / `<local:内容模板选择器/>` 直接实例化的,
// 挪进 `hehshihbeen_compiler.界面控件` 会当场 MC3074(命名空间里不存在该标记)。
namespace hehshihbeen_compiler
{
    /// <summary> 按"普通页 / 选项页"分流标签页头模板 </summary>
    public class 标签头模板选择器 : DataTemplateSelector
    {
        public DataTemplate? 普通头 { get; set; }
        public DataTemplate? 选项头 { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is 选卡页视图模型 页 && 页.是否选项页 && 选项头 != null) return 选项头;
            return 普通头 ?? base.SelectTemplate(item, container);
        }
    }

    /// <summary> 按"普通页 / 选项页"分流标签页内容模板 </summary>
    public class 内容模板选择器 : DataTemplateSelector
    {
        public DataTemplate? 普通内容 { get; set; }
        public DataTemplate? 选项内容 { get; set; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            if (item is 选卡页视图模型 页 && 页.是否选项页 && 选项内容 != null) return 选项内容;
            return 普通内容 ?? base.SelectTemplate(item, container);
        }
    }
}
