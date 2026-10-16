using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// **命令面板**(Ctrl+Shift+P):一个输入框 + 一张表,把**菜单项**与**帮助话题**放在一起找。
    ///
    /// 它自己不认识任何一条命令 —— 候选是外面算好递进来的(`命令面板模型C`),
    /// 执行也是外面那一句(`执行一条`)干的。这么切有两个好处:
    ///   ① 菜单加一项、帮助加一页,面板**自动就有**(候选是现场算的,不是这里抄的);
    ///   ② 自检能在命令行里把"排序 / 最近用过 / 标号"这一整套真跑一遍(那些都在引擎侧),
    ///      这里只剩"按键怎么走、列表怎么选"这点界面活。
    /// </summary>
    public partial class 命令面板窗口 : Window
    {
        private readonly 命令面板模型C 模型;
        private readonly Action<命令项C> 执行一条;

        /// <summary> 刚才执行掉的那一条(执行完窗口已经关了,调用方还记得它干了什么好写状态栏)</summary>
        public 命令项C? 执行过的 { get; private set; }

        public 命令面板窗口(命令面板模型C 模型, Action<命令项C> 执行一条)
        {
            InitializeComponent();
            this.模型 = 模型;
            this.执行一条 = 执行一条;
            刷新(模型.匹配(""));
            Loaded += (_, _) => 搜索框.Focus();
        }

        /// <summary> 查询串变了:重排候选(空查询 = 最近用过的那几条排最前) </summary>
        private void 搜索框_TextChanged(object sender, TextChangedEventArgs e) => 刷新(模型.匹配(搜索框.Text));

        private void 刷新(List<命令项C> 结果)
        {
            列表.ItemsSource = 结果;
            列表.SelectedIndex = 结果.Count > 0 ? 0 : -1;
            空提示.Visibility = 结果.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            细节文本.Text = 列表.SelectedItem is 命令项C 选中 ? 选中.细节 : "";
        }

        private void 列表_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            细节文本.Text = 列表.SelectedItem is 命令项C 选中 ? 选中.细节 : "";
        }

        private void 列表_MouseDoubleClick(object sender, MouseButtonEventArgs e) => 执行选中的();

        /// <summary> 输入框上的键:↑↓ 换选中、回车执行、Esc 关掉 —— 手不用离开键盘 </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            switch (e.Key)
            {
                case Key.Escape:
                    e.Handled = true;
                    Close();
                    return;
                case Key.Enter:
                    e.Handled = true;
                    执行选中的();
                    return;
                case Key.Down:
                    e.Handled = true;
                    挪一位(1);
                    return;
                case Key.Up:
                    e.Handled = true;
                    挪一位(-1);
                    return;
            }
        }

        private void 挪一位(int 步)
        {
            int 数 = 列表.Items.Count;
            if (数 == 0) return;
            int 现在 = 列表.SelectedIndex < 0 ? 0 : 列表.SelectedIndex;
            int 下一个 = Math.Clamp(现在 + 步, 0, 数 - 1);
            列表.SelectedIndex = 下一个;
            列表.ScrollIntoView(列表.SelectedItem);
        }

        /// <summary> 执行当前选中那一条:先记"用过"(下次打开它就在最前面),再交给外面执行,最后关窗 </summary>
        private void 执行选中的()
        {
            if (列表.SelectedItem is not 命令项C 选中) return;
            执行过的 = 选中;
            try { 模型.记用过(选中); } catch { /* 记不住历史不影响这一下能不能执行 */ }
            Close();
            try { 执行一条(选中); }
            catch (Exception 异常) { 异常收集C.记($"命令面板执行 `{选中.路径}` 失败: {异常.Message}"); }
        }
    }
}
