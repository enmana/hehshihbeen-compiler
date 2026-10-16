using hehshihbeen_compiler.配置;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 新建项目向导:一次建齐"项目目录 + 工程文件(.hbp) + 起步用 主程序.hb"。
    /// 建好后由主窗口打开这个 主程序.hb(用户马上就能按 汇编/执行)。
    /// </summary>
    public partial class 新建项目窗口 : Window
    {
        /// <summary> 建好的项目(取消则为 null) </summary>
        public 项目项C? 新项目 { get; private set; }

        /// <summary> 这次实际用的父目录(主窗口据此记进 选项.json) </summary>
        public string 使用位置 { get; private set; } = "";

        private readonly 上下文C 上下文;

        public 新建项目窗口(上下文C 上下文)
        {
            InitializeComponent();
            this.上下文 = 上下文;
            填候选目录();

            Loaded += (_, _) =>
            {
                项目名称输入框.Focus();
            };
            刷新预览();
        }

        /// <summary> 位置框里列"用过的那几个目录"(最近用的在最前);一个都没用过就用默认文件夹 </summary>
        private void 填候选目录()
        {
            var 候选 = 选项.新建项目目录候选();
            if (候选.Count > 0)
            {
                项目位置输入框.ItemsSource = 候选;
                项目位置输入框.Text = 候选[0];
                return;
            }
            string 默认 = 选项.当前.默认文件夹;
            项目位置输入框.Text = Directory.Exists(默认) ? 默认 : AppDomain.CurrentDomain.BaseDirectory;
        }

        private string 位置 => (项目位置输入框.Text ?? "").Trim();
        private string 名称 => (项目名称输入框.Text ?? "").Trim();

        private void 刷新预览()
        {
            string 名 = 名称.Length == 0 ? "我的项目" : 名称;
            string 位置文本 = 位置.Length == 0 ? "(还没选目录)" : 位置;
            string 目录 = 位置.Length == 0 ? Path.Combine("…", 名) : Path.Combine(位置, 名);
            目录预览.Text =
                $"{目录}{Environment.NewLine}" +
                $"├─ {名}{工程文件C.扩展名}        工程文件(文件表/顺序/启动文件/资源){Environment.NewLine}" +
                $"└─ {工程文件C.默认入口名}         启动文件(机器码从这里开始跑)";

            警告文本.Text = 位置.Length > 0 && 名称.Length > 0 && Directory.Exists(Path.Combine(位置, 名))
                ? "⚠ 这个目录已经存在:会直接用它,并把工程文件/主程序补进去(已存在的文件不会覆盖)"
                : "";
        }

        private void 项目名称输入框_TextChanged(object sender, TextChangedEventArgs e) => 刷新预览();


        private void 选择文件夹_Click(object sender, RoutedEventArgs e)
        {
            var 对话框 = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择项目放在哪个目录",
                InitialDirectory = Directory.Exists(位置) ? 位置 : AppDomain.CurrentDomain.BaseDirectory,
            };
            if (对话框.ShowDialog() == true) 项目位置输入框.Text = 对话框.FolderName;
        }

        private void 新建_Click(object sender, RoutedEventArgs e)
        {
            if (名称.Length == 0 || 位置.Length == 0)
            {
                MessageBox.Show(this, "项目名称和位置都要填。", "新建项目", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                string 目录 = Path.GetFullPath(Path.Combine(位置, 名称));
                if (Directory.Exists(目录) && File.Exists(Path.Combine(目录, 名称 + 工程文件C.扩展名)) &&
                    MessageBox.Show(this, $"目录里已经有 {名称}{工程文件C.扩展名},打开它?", "新建项目",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                    return;

                var 建 = 上下文.建项目 ?? throw new InvalidOperationException("主窗口还没把\"建项目\"挂到上下文上");
                新项目 = 建(位置, 名称);      // 建目录 + 工程文件 + 主程序.hb,并换掉当前项目
                使用位置 = Path.GetFullPath(位置);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                异常收集C.记($"新建项目失败: {ex.Message}");
                警告文本.Text = $"创建失败:{ex.Message}";
            }
        }

        private void 取消_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
