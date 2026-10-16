using hehshihbeen_compiler.配置;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 启动页:程序启动时显示它、主窗口先藏着(见 MainWindow.显示启动页)。
    /// 它只做两件事:列出最近项目、把点击转给**上下文里挂好的那几个动作**;
    /// 一关掉,主窗口就在自己的 Closed 回调里 Show 出来。
    /// 这里不向主窗口要任何东西(上下文是构造时传进来的)。
    /// </summary>
    public partial class 启动页窗口 : Window
    {
        private readonly 上下文C 上下文;
        /// <summary> 这次关闭是不是我们自己调 Close() 来的(是 → 主窗口出来;不是 → 用户按了 ✕,直接退出程序) </summary>
        private bool _由代码关闭;

        public 启动页窗口(上下文C 上下文)
        {
            InitializeComponent();
            this.上下文 = 上下文;

            Loaded += (_, _) =>
            {
                刷新();
                Topmost = true;
            };
            Closed += (_, _) =>
            {
                if (_由代码关闭) 主窗口?.Show();
                else Environment.Exit(0);
            };
        }

        /// <summary> 主窗口(Owner 就是它);只当 Owner 用,不向它要数据 </summary>
        private MainWindow? 主窗口 => Owner as MainWindow;

        /// <summary> 刷新最近项目列表 </summary>
        public void 刷新()
        {
            var 项目 = 上下文.最近项目.取全部();
            最近列表.ItemsSource = 项目;
            空列表提示.Visibility = 项目.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            副标题.Text = 项目.Count == 0
                ? "新建一个项目,或者打开一个工程文件(.hbp)"
                : $"最近打开过 {项目.Count} 个项目";
        }

        // ---------------- 入口:调上下文里的动作,然后关掉自己(主窗口会自动出来) ----------------

        private void 最近项_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string 路径 || 路径.Length == 0) return;
            if (!File.Exists(路径))
            {
                // 文件不在了:去掉这条,留在启动页
                上下文.最近项目.移除(路径);
                上下文.最近项目.保存();
                刷新();
                return;
            }
            上下文.刚才取消了吗 = false;
            Topmost = false;
            上下文.请打开工程?.Invoke(路径);
            if (上下文.刚才取消了吗) return;
            关掉自己();
        }

        private void 链接新建项目_Click(object sender, MouseButtonEventArgs e)
        {
            上下文.刚才取消了吗 = false;
            Topmost = false;
            上下文.请新建项目?.Invoke();
            if (上下文.刚才取消了吗) return;      // 用户取消:留在启动页
            关掉自己();
        }

        private void 链接打开项目_Click(object sender, MouseButtonEventArgs e)
        {
            上下文.刚才取消了吗 = false;
            Topmost = false;
            上下文.请打开项目?.Invoke();
            if (上下文.刚才取消了吗) return;
            关掉自己();
        }

        private void 链接打开文件_Click(object sender, MouseButtonEventArgs e)
        {
            上下文.刚才取消了吗 = false;
            Topmost = false;
            上下文.请打开文件?.Invoke();
            if (上下文.刚才取消了吗) return;
            关掉自己();
        }

        /// <summary> 直接开始:关掉启动页 → 主窗口出来(无项目无文件) </summary>
        private void 链接空白_Click(object sender, MouseButtonEventArgs e) => 关掉自己();

        /// <summary> 清空最近列表(只清历史,不动磁盘上的工程);清完还留在启动页上 </summary>
        private void 链接清空_Click(object sender, MouseButtonEventArgs e)
        {
            上下文.最近项目.清空();
            上下文.最近项目.保存();
            刷新();
        }

        /// <summary> 关掉自己(主窗口会在 Closed 里出来) </summary>
        private void 关掉自己()
        {
            _由代码关闭 = true;
            Close();
        }
    }
}
