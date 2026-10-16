using System.Windows;

namespace hehshihbeen_compiler
{
    /// <summary> 一个最小的"输入一行文字"对话框(新建文件、重命名之类都用它,免得再各写一套) </summary>
    public partial class 输入窗口 : Window
    {
        /// <summary> 用户确定后拿到的文本;取消时为 null </summary>
        public string? 结果 { get; private set; }

        public 输入窗口(string 标题, string 提示, string 默认值 = "", string 说明 = "", string 勾选文本 = "")
        {
            InitializeComponent();
            Title = 标题;
            提示文本.Text = 提示;
            输入框.Text = 默认值;
            说明文本.Text = 说明;
            if (说明.Length == 0) 说明文本.Visibility = Visibility.Collapsed;
            // H5:要一个"可选勾选框"的调用方(查找/替换的「按正则」)才把它显出来
            if (勾选文本.Length > 0)
            {
                勾选框.Content = 勾选文本;
                勾选框.Visibility = Visibility.Visible;
            }
            Loaded += (_, _) => { 输入框.Focus(); 输入框.SelectAll(); };
        }

        /// <summary> 弹一次输入框;取消返回 null </summary>
        public static string? 问(Window? owner, string 标题, string 提示, string 默认值 = "", string 说明 = "")
        {
            var 窗口 = new 输入窗口(标题, 提示, 默认值, 说明) { Owner = owner };
            return 窗口.ShowDialog() == true ? 窗口.结果 : null;
        }

        /// <summary>
        /// 弹一次输入框 **+ 一个可选勾选框**(H5:查找/替换的「按正则」用它)。
        /// 取消返回 null,且 `勾选值` **一个字都不动**;确定时把勾选框的状态写回 `勾选值`。
        /// </summary>
        public static string? 问带勾选(Window? owner, string 标题, string 提示, string 默认值, string 说明,
            string 勾选文本, ref bool 勾选值)
        {
            var 窗口 = new 输入窗口(标题, 提示, 默认值, 说明, 勾选文本) { Owner = owner };
            窗口.勾选框.IsChecked = 勾选值;
            if (窗口.ShowDialog() != true) return null;
            勾选值 = 窗口.勾选框.IsChecked == true;
            return 窗口.结果;
        }

        private void 确定_Click(object sender, RoutedEventArgs e)
        {
            结果 = 输入框.Text;
            DialogResult = true;
        }

        private void 取消_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
