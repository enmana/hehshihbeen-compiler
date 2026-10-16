using hehshihbeen_compiler.配置;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace hehshihbeen_compiler
{
    public partial class 选项页 : System.Windows.Controls.UserControl
    {
        public static event Action<选项设置>? 选项已应用;

        /// <summary> 快捷键表里的一行(动作 → 键位;`默认值` 只用来显示)</summary>
        public class 快捷键行
        {
            public string 动作 { get; set; } = "";
            public string 标题 { get; set; } = "";
            public string 键位 { get; set; } = "";
            public string 默认值 { get; set; } = "";
            public bool 可改 { get; set; } = true;
        }

        public class 颜色行
        {            public string 标签 { get; set; } = "";
            /// <summary> 主题里的属性名(非词类项用它);词类项为空,看 `词类键` </summary>
            public string 属性 { get; set; } = "";
            /// <summary> `配色词类C` 里的词类键(非空 = 这一行是"按词类上色"的项) </summary>
            public string 词类键 { get; set; } = "";
            public string 色值 { get; set; } = "";
        }

        /// <summary>
        /// **不是"代码词类"的那些界面色**(编辑器背景/行号/面板…) —— 这些仍然是主题里的一个属性。
        /// 代码词类(指令/关键字/类型/结构/内建函数/方法/变量/字符串/字符/数字/寄存器/注释/普通文本)
        /// **不在这儿**:它们由 `配色词类C.表` 生成(见 `刷新调色`),谁也不许再手抄一份清单。
        /// </summary>
        private static readonly string[] 界面色属性 =
        [
            "编辑器背景","编辑器前景","行号颜色","面板背景","背景","前景","强调色",
            "输出框背景","输出框前景"
        ];

        private readonly ObservableCollection<颜色行> _调色 = [];
        private readonly ObservableCollection<快捷键行> _快捷键 = [];

        /// <summary> H8:花括号 7 档彩虹色那 7 行(与主题调色表分开 —— 这 7 档存在 `选项.json`,不随主题走) </summary>
        private readonly ObservableCollection<颜色行> _彩虹色 = [];
        private readonly string[] 常用字体 = [..Fonts.SystemFontFamilies.Select(x=>x.Source)];
            //["Consolas", "Microsoft YaHei", "Microsoft YaHei UI", "SimSun", "NSimSun", "Courier New"];

        public 选项页()
        {
            InitializeComponent();
            调色表.ItemsSource = _调色;
            彩虹色表.ItemsSource = _彩虹色;
            快捷键列表.ItemsSource = _快捷键;
            字体下拉.ItemsSource = 常用字体;
            foreach (var 主题 in 主题C.取全部主题()) 主题下拉.Items.Add(主题.名称);
            主题下拉.SelectionChanged -= 主题下拉_SelectionChanged; // 避免构造触发
            载入当前值();
            主题下拉.SelectionChanged += 主题下拉_SelectionChanged;
            模糊拼音勾.Checked += (_, _) => 刷新模糊规则可用性();
            模糊拼音勾.Unchecked += (_, _) => 刷新模糊规则可用性();
        }

        /// <summary> 按 `补全词表C.模糊规则表` 生成"模糊拼音规则"勾选项(规则表是唯一来源,不另抄一份) </summary>
        private void 建模糊规则勾选()
        {
            if (模糊规则面板.Children.Count == 0)
                foreach (var (名, 定义) in 补全词表C.模糊规则表)
                    模糊规则面板.Children.Add(new System.Windows.Controls.CheckBox
                    {
                        Content = $"{名}({定义.说明})",
                        Tag = 名,
                        Margin = new Thickness(0, 0, 16, 2),
                    });
            刷新模糊规则勾选();
        }

        /// <summary> 勾选状态跟着选项走("恢复默认"之后也要跟着变) </summary>
        private void 刷新模糊规则勾选()
        {
            var 已勾 = 配置.选项.当前.模糊拼音规则;
            foreach (var 勾 in 模糊规则面板.Children.OfType<System.Windows.Controls.CheckBox>())
                勾.IsChecked = 已勾.Contains((string)勾.Tag);
        }

        /// <summary> 总开关关掉时规则勾选变灰(还看得见,知道自己勾过什么)</summary>
        private void 刷新模糊规则可用性()
        {
            bool 开 = 模糊拼音勾.IsChecked == true;
            模糊规则面板.IsEnabled = 开;
            模糊拼音说明.Opacity = 开 ? 1.0 : 0.5;
        }

        private void 载入当前值()
        {
            var 选项 = 配置.选项.当前;
            字体下拉.Text = 选项.字体族;
            字号框.Text = 选项.字号.ToString("0.##");
            行高框.Text = 选项.行高.ToString("0.##");
            粗体勾.IsChecked = 选项.粗体;
            斜体勾.IsChecked = 选项.斜体;
            行号勾.IsChecked = 选项.显示行号;
            换行勾.IsChecked = 选项.自动换行;
            成对勾.IsChecked = 选项.自动成对;
            Tab确认勾.IsChecked = 选项.Tab确认;
            回车缩进勾.IsChecked = 选项.回车自动缩进;
            缩进宽度框.Text = 选项.缩进宽度.ToString();
            // H8:缩进导引线开关 + 花括号 7 档彩虹色(默认 = 引擎那一套)
            导引线勾.IsChecked = 选项.缩进导引线;
            刷新彩虹色(选项);

            // 快捷键:表从 `快捷键C.全部` 生成(单一事实源),值取"现在生效的键位"
            建快捷键行();
            文件夹框.Text = 选项.默认文件夹;

            // 模糊拼音:总开关 + 规则勾选(规则表是唯一来源,这里按它生成勾选项)
            建模糊规则勾选();
            模糊拼音勾.IsChecked = 选项.模糊拼音;
            刷新模糊规则可用性();

            var 当前 = 主题C.当前主题 ?? 主题C.按名称取("亮");
            主题下拉.SelectedItem = 当前?.名称;
            刷新调色(当前);
        }

        /// <summary> 按 `快捷键C.全部` 生成"快捷键"那一组(不另抄一份清单;值取现在生效的键位)</summary>
        private void 建快捷键行()
        {
            _快捷键.Clear();
            foreach (var (动作, 键位, 说明, 可改) in 快捷键C.生效表(配置.选项.当前.快捷键))
                _快捷键.Add(new 快捷键行
                {
                    动作 = 动作,
                    标题 = $"{动作} —— {说明}",
                    键位 = 键位,
                    默认值 = $"默认 {快捷键C.默认键(动作)}" + (可改 ? "" : "(内置)"),
                    可改 = 可改,
                });
        }

        /// <summary> 把界面上这张快捷键表读成"覆盖表"(只留改过的;空串 = 用户取消了它)</summary>
        private Dictionary<string, string> 取快捷键覆盖(out List<string> 问题)
        {
            问题 = [];
            var 覆盖 = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var 行 in _快捷键)
            {
                string 原文 = (行.键位 ?? "").Trim();
                if (原文.Length == 0)
                {
                    if (快捷键C.默认键(行.动作).Length > 0) 覆盖[行.动作] = "";      // 明确取消
                    continue;
                }
                string 规范 = 快捷键C.规范(原文);
                if (规范.Length == 0) { 问题.Add($"『{行.动作}』的键位 `{原文}` 看不懂(写成 F5 / Ctrl+H / Ctrl+Shift+F 这样)"); continue; }
                if (!行.可改) { 问题.Add($"『{行.动作}』是编辑器内置的,改不了(已按默认 {快捷键C.默认键(行.动作)} 用)"); continue; }
                if (!string.Equals(规范, 快捷键C.默认键(行.动作), StringComparison.OrdinalIgnoreCase)) 覆盖[行.动作] = 规范;
            }
            问题.AddRange(快捷键C.冲突(覆盖));
            return 覆盖;
        }

        private void 刷新调色(主题设置? 主题)
        {
            主题 ??= 主题C.当前主题;
            if (主题 == null) return;
            _调色.Clear();
            // ① 代码词类:一项一项全从 配色词类C.表 来(单一事实源,加词类这里自动多一项)
            foreach (var 类 in 配色词类C.全部项)
                _调色.Add(new 颜色行 { 标签 = 类.显示名, 词类键 = 类.键, 色值 = 主题.取词类色(类.键) });
            // ② 界面色(背景/行号/面板…):仍然是主题里的一个属性
            foreach (string 属性 in 界面色属性)
            {
                string? 值 = 主题.GetType().GetProperty(属性)?.GetValue(主题) as string;
                _调色.Add(new 颜色行 { 标签 = 属性, 属性 = 属性, 色值 = 值 ?? "" });
            }
            主题名框.Text = 主题.名称;
        }

        /// <summary>
        /// H8:花括号 7 档彩虹色那 7 行 —— **按 `花括号层号C.色表` / `色档名` 生成**(单一事实源,
        /// 引擎那边改色相这里自动跟上,不另抄一份色值);显示的是**规范过的那份**,
        /// 所以盘上存坏的档当场就看见"落回默认了",而不是"看着是我填的、编出来不是"。
        /// </summary>
        private void 刷新彩虹色(选项设置 选项)
        {
            var 归一 = 花括号层号C.规范色表(选项.花括号色);
            _彩虹色.Clear();
            for (int 档 = 0; 档 < 归一.Length; 档++)
                _彩虹色.Add(new 颜色行
                {
                    标签 = $"第 {档 + 1} 档 {花括号层号C.色档名[档]}",
                    色值 = 归一[档],
                });
        }

        private 主题设置 取草稿()
        {
            var 基础 = 主题C.当前主题 ?? 主题C.按名称取("亮")!;
            var 草稿 = JsonSerializer.Deserialize<主题设置>(JsonSerializer.Serialize(基础))!;
            草稿.词类颜色 ??= new(StringComparer.Ordinal);
            foreach (var 行 in _调色)
            {
                if (行.词类键.Length > 0) 草稿.词类颜色[行.词类键] = 行.色值;
                else 草稿.GetType().GetProperty(行.属性)?.SetValue(草稿, 行.色值);
            }
            if (!string.IsNullOrWhiteSpace(主题名框.Text)) 草稿.名称 = 主题名框.Text.Trim();
            return 草稿;
        }

        private void 主题下拉_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (主题下拉.SelectedItem is not string 名) return;
            if (主题C.应用(名)) 刷新调色(主题C.当前主题);
        }

        private void 预览按钮_Click(object sender, RoutedEventArgs e)
        {
            var 草稿 = 取草稿();
            主题C.应用对象(草稿, 持久化: false);
            提示文字.Text = "已临时套用配色(未写入文件)。";
        }

        private void 另存按钮_Click(object sender, RoutedEventArgs e)
        {
            string 名 = 主题名框.Text.Trim();
            if (名.Length == 0) { 提示文字.Text = "请先填主题名。"; return; }
            if (名 == "暗" || 名 == "亮") { 提示文字.Text = "请换一个名字(暗/亮是内置名,启动会重置)。"; return; }
            var 草稿 = 取草稿();
            草稿.名称 = 名;
            string 文件 = Path.Combine(主题C.主题目录, $"{名}.json");
            File.WriteAllText(文件, JsonSerializer.Serialize(草稿, new JsonSerializerOptions { WriteIndented = true }));
            主题C.应用对象(草稿, 持久化: true);
            if (!主题下拉.Items.Contains(名)) 主题下拉.Items.Add(名);
            主题下拉.SelectedItem = 名;
            提示文字.Text = $"已保存新主题“{名}”: {文件}";
        }

        private void 保存当前按钮_Click(object sender, RoutedEventArgs e)
        {
            var 草稿 = 取草稿();
            bool 内置 = 草稿.名称 == "暗" || 草稿.名称 == "亮";
            string 文件 = Path.Combine(主题C.主题目录, $"{草稿.名称}.json");
            File.WriteAllText(文件, JsonSerializer.Serialize(草稿, new JsonSerializerOptions { WriteIndented = true }));
            主题C.应用对象(草稿, 持久化: true);
            提示文字.Text = 内置
                ? $"已保存到内置“{草稿.名称}”(注意:下次启动内置会按代码刷新)。建议“另存为新主题”。"
                : $"已保存到“{草稿.名称}”。";
        }

        private void 浏览按钮_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择默认文件夹" };
            if (dlg.ShowDialog() == true) 文件夹框.Text = dlg.FolderName;
        }

        private void 应用按钮_Click(object sender, RoutedEventArgs e)
        {
            var 选项 = 配置.选项.当前;
            选项.字体族 = string.IsNullOrWhiteSpace(字体下拉.Text) ? "Consolas" : 字体下拉.Text.Trim();
            double.TryParse(字号框.Text, out double 字号);
            选项.字号 = 字号 > 0 ? 字号 : 14;
            double.TryParse(行高框.Text, out double 行高);
            选项.行高 = 行高 > 0 ? 行高 : new 选项设置().行高;
            选项.粗体 = 粗体勾.IsChecked == true;
            选项.斜体 = 斜体勾.IsChecked == true;
            选项.显示行号 = 行号勾.IsChecked == true;
            选项.自动换行 = 换行勾.IsChecked == true;
            选项.自动成对 = 成对勾.IsChecked == true;
            选项.Tab确认 = Tab确认勾.IsChecked == true;
            选项.回车自动缩进 = 回车缩进勾.IsChecked == true;
            if (int.TryParse(缩进宽度框.Text.Trim(), out int 缩进宽)) 选项.缩进宽度 = Math.Clamp(缩进宽, 1, 16);
            // H8:缩进导引线开关 + 花括号 7 档彩虹色(写盘前先规范一遍,存下去的就是真生效的那 7 档)
            选项.缩进导引线 = 导引线勾.IsChecked == true;
            选项.花括号色 = [.. 花括号层号C.规范色表([.. _彩虹色.Select(行 => 行.色值)])];
            选项.模糊拼音 = 模糊拼音勾.IsChecked == true;
            选项.模糊拼音规则 = [.. 模糊规则面板.Children.OfType<System.Windows.Controls.CheckBox>()
                .Where(勾 => 勾.IsChecked == true).Select(勾 => (string)勾.Tag)];
            选项.默认文件夹 = 文件夹框.Text.Trim();
            选项.快捷键 = 取快捷键覆盖(out var 快捷键问题);
            配置.选项.保存(选项);
            选项已应用?.Invoke(选项);
            状态文字.Text = 快捷键问题.Count == 0
                ? "已应用并保存。"
                : "已应用并保存,但这几点要提醒你:\n· " + string.Join("\n· ", 快捷键问题);
            if (快捷键问题.Count > 0) 快捷键说明.Text = "提示:" + string.Join(";", 快捷键问题);
        }

        private void 恢复默认_Click(object sender, RoutedEventArgs e)
        {
            选项.保存(new 选项设置());
            载入当前值();
            选项已应用?.Invoke(选项.当前);
            状态文字.Text = "已恢复编辑器默认选项(主题未动)。";
        }
    }
}
