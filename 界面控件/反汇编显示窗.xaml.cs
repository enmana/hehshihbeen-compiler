using System.Windows;

namespace hehshihbeen_compiler
{
    public partial class 反汇编显示窗 : Window
    {
        public 反汇编显示窗(string 文本)
        {
            InitializeComponent();
            反汇编框.Text = 文本 ?? string.Empty;
        }
    }
}
