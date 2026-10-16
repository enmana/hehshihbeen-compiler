using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// **断点设置**(P34,2026-10-07):一个对话框里把这三样写完 ——
    ///   · **命中条件**:`i == 5` / `计数 != 真` / `左 &lt; 右`(与从前那条一样,只是现在右边也能写变量);
    ///   · **命中次数**:`5`(第 5 次停一次)/ `%5`(每 5 次停一次)/ `&gt;=5`(第 5 次起每次都停);
    ///   · **日志**:填了它就成**日志断点** —— 命中时把这行文本打进「输出」页、**不停下来**
    ///     (`{变量}` 会换成那个变量现在的值,还认 `{行}` / `{文件}`)。
    /// 三样都留空 = 普通断点(命中就停)。
    ///
    /// 三格都是**边打边判**:底下那一行实时显示"这样写是什么意思 / 哪里不对"(与真正求值走同一个解析器,
    /// 不另写一套判据 —— 判得对不对,自检盯的是 `断点条件求值C` / `命中次数条件C` 那两个纯函数)。
    /// </summary>
    public partial class 断点设置窗口 : Window
    {
        /// <summary> 确定之后的三个值(取消时调用方不读它们)</summary>
        public string 条件 { get; private set; } = "";
        public string 命中次数 { get; private set; } = "";
        public string 日志 { get; private set; } = "";

        public 断点设置窗口(string 题目, 断点项C? 断点)
        {
            InitializeComponent();
            标题文本.Text = 题目;
            if (断点 != null)
            {
                条件框.Text = 断点.条件;
                次数框.Text = 断点.命中次数;
                日志框.Text = 断点.日志;
            }
            条件框.TextChanged += (_, _) => 刷新预览();
            次数框.TextChanged += (_, _) => 刷新预览();
            日志框.TextChanged += (_, _) => 刷新预览();
            刷新预览();
            Loaded += (_, _) => { 条件框.Focus(); 条件框.SelectAll(); };
        }

        /// <summary> 弹一次断点设置;取消返回 null(调用方一个字都不改) </summary>
        public static 断点设置窗口? 问(Window? owner, string 题目, 断点项C? 断点)
        {
            var 窗 = new 断点设置窗口(题目, 断点) { Owner = owner };
            return 窗.ShowDialog() == true ? 窗 : null;
        }

        private void 刷新预览()
        {
            var 条件 = 断点条件求值C.解析(条件框.Text);
            var 次数 = 命中次数条件C.解析(次数框.Text);
            条件预览.Text = (条件.合法 ? "✔ " : "✘ ") + 条件.说明;
            条件预览.Foreground = 色(条件.合法);
            次数预览.Text = (次数.合法 ? "✔ " : "✘ ") + 次数.说明;
            次数预览.Foreground = 色(次数.合法);

            string 日志 = 日志框.Text.Trim();
            if (日志.Length == 0)
            {
                日志预览.Text = "✔ 留空 = 命中就停下来(普通断点)";
                日志预览.Foreground = 色(true);
            }
            else
            {
                var 名字 = 日志断点C.用到的名字(日志);
                日志预览.Text = "✔ 这是**日志断点**:命中时把这行打进「输出」页,程序**不停**"
                    + (名字.Count > 0 ? $";用到的变量:{string.Join("、", 名字)}(这一行看不见的会原样留着并说明原因)" : "");
                日志预览.Foreground = 色(true);
            }
            确定按钮.IsEnabled = 条件.合法 && 次数.合法;
        }

        private static Brush 色(bool 好) => 好
            ? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32))
            : new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

        private void 确定_Click(object sender, RoutedEventArgs e)
        {
            条件 = 条件框.Text.Trim();
            命中次数 = 次数框.Text.Trim();
            日志 = 日志框.Text.Trim();
            DialogResult = true;
        }

        private void 清空_Click(object sender, RoutedEventArgs e)
        {
            条件框.Text = "";
            次数框.Text = "";
            日志框.Text = "";
            刷新预览();
        }

        private void 取消_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
