using hehshihbeen_compiler.界面控件;
using hehshihbeen_compiler.配置;
using hehshihbeen_compiler.选卡控件;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using static System.Net.Mime.MediaTypeNames;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class 编辑器 : UserControl
    {
        #region 编辑器核心字段与状态
        private 语法高亮C 高亮器 { get; set; }
        private TextEditor? 代码编辑器 { get; set; }

        /// <summary> C16:挂上去的那个行内高亮器(花括号层号表由它按文档版本缓存,不必再存一份) </summary>
        private AvalonInlineHighlighter? 行内高亮器 { get; set; }

        /// <summary>
        /// C16-3 / H8:挂上去的那个缩进导引线渲染器 —— 存一份是为了**选项页那个开关**
        /// (`应用选项()` 把 `配置.选项.当前.缩进导引线` 写进它的 `显示`)。
        /// </summary>
        private 缩进导引线渲染? 导引线渲染 { get; set; }

        private bool 正在更新高亮 { get; set; } = false;
        private bool 结构操作中 { get; set; } = false;
        private readonly DispatcherTimer 防抖定时器 = new();
        public 代码框设置 代码框通用设置 { get; set; }
        /// <summary> P21:编辑器自带的**查找面板**(Ctrl+F 找 + 高亮 + F3 下一个);替换走 `查找替换C`,见主窗口</summary>
        private ICSharpCode.AvalonEdit.Search.SearchPanel? 搜索面板;
        #endregion

        // ===== 编辑器辅助:断点边距 / 代码折叠 =====
        #region 辅助字段:断点边距 + 成对输入表
        /// <summary> 行号左边的断点边距(红点;点一下切换断点) </summary>
        private readonly 断点边距 断点栏 = new();

        // 成对输入:开括号/引号 → 自动补右符(注释内不打扰)
        private static readonly Dictionary<char, string> 成对表 = new()
        {
            ['('] = ")",
            ['['] = "]",
            ['{'] = "}",
            ['"'] = "\"",
            ['\''] = "'",
        };
        private static readonly HashSet<char> 关闭符 = [')', ']', '}', '"', '\''];
        #endregion

        #region 内容依赖属性与文本双向同步
        public static readonly DependencyProperty 内容Property = DependencyProperty.Register(
                        nameof(内容), typeof(string), typeof(编辑器), new FrameworkPropertyMetadata(
                            string.Empty,
                            // 关键：注册为双向绑定，让编辑器文本可以写回绑定源(VM)
                            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                            On内容Changed));
        public string 内容
        {
            get => (string)GetValue(内容Property);
            set => SetValue(内容Property, value);
        }

        private static void On内容Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is 编辑器 control && e.NewValue is string newText)
            {
                control.推送绑定内容到编辑器(newText); // 绑定源变化时同步编辑器
            }
        }

        /// <summary>
        /// 将绑定/外部设置的内容推送到 AvalonEdit。
        /// 仅在文本确实不同时才覆盖文档，避免打断用户正在进行的输入。
        ///
        /// **C10 改过(实测出来的坑)**:以前这里是 `代码编辑器.Text = 文本` —— AvalonEdit 那个属性设值会
        /// **换掉整个 TextDocument**,于是 ① 撤销栈跟着没了(探针实测 CanUndo=False:替换/格式化/改名
        /// 这些"整篇推文本"的操作**根本撤不回来**,而界面一直写着"Ctrl+Z 可撤销这一整次");
        /// ② 折叠区、滚动位置也一起丢。现在改成对**同一个文档**做一次 `Replace`
        /// (`TextDocument` 层面的一次替换 = 一个撤销单元,实测能一步退回原文),
        /// 既保住了撤销栈,也保住了折叠与光标周边状态。
        ///
        /// **C29 再分了一层(用户 2026-09-20 报的)**:同一条 `Replace` 也是**载入文件**走的路
        /// ("打开文件 → `Replace("" → 全文)`"),于是栈底多了一条"把整篇删掉"的撤销单元,
        /// 再按一次 `Ctrl+Z` 得到的就是**空文档**(用户原话"最后一个撤销会撤出来一个空白页")。
        /// 现在按 `撤销边界C.算载入` 分两条路:**载入**(换页 / 页上标着这一份载入)**不进撤销栈**
        /// (替换完 `UndoStack.ClearAll()`),**用户动作**照旧留一个撤销单元。
        /// </summary>
        private void 推送绑定内容到编辑器(string 文本)
        {
            if (代码编辑器 == null) return;
            文本 ??= string.Empty;

            // C29:这一趟算"载入"还是"用户编辑"(旗子读走即复位;判断收在引擎 `撤销边界C`)
            bool 是载入 = 取走载入标记(文本);

            if (代码编辑器.Text == 文本)
            {
                // 文本一样、绑定不推 —— 但"载入"这层意思还得落地:比如两个文件正文相同、
                // 或者空文件落在刚造出来的编辑器上。不断栈的话,上一次那几条撤销单元撤下去就是**别的文件**的正文。
                if (是载入) 清空撤销栈();
                return;
            }

            正在更新高亮 = true;
            try
            {
                var 文档 = 代码编辑器.Document;
                if (文档 == null)
                {
                    代码编辑器.Text = 文本;
                    if (是载入) 清空撤销栈();       // 兜底那支换的是新文档,栈本来就是空的;写上只是把口径说全
                }
                else
                {
                    using (文档.RunUpdate())
                        文档.Replace(0, 文档.TextLength, 文本);
                    if (是载入) 清空撤销栈();       // C29:载入不占一个撤销单元("载入完成后才开始算撤销")
                }
            }
            finally
            {
                正在更新高亮 = false;
            }
            // 整文替换后重算折叠区(行都变了)
            刷新折叠区();
            // 延后刷新高亮以避免 UI 卡顿
            Dispatcher.BeginInvoke(new Action(() => 刷新高亮()), DispatcherPriority.Background);
        }

        /// <summary> C29:这份文档现在属于哪一页(换页 = 载入:撤销栈要断开,不能跨文件撤) </summary>
        private 选卡页视图模型? _文档属于哪一页;

        /// <summary>
        /// C29:这一趟算不算"载入"。判据两条(合成一条规则的地方在引擎 `撤销边界C.算载入`,自检直接喂参数):
        /// ① **换了页** —— 绑定源变成了另一个标签页(编辑器控件是各页共用的一个 `TextDocument`);
        /// ② **页上标着这一份载入** —— 打开文件 / 新建页 / 装载工程在页模型上标的那一面旗子
        ///    (只有推的正文正是当初载入那一份才认,见 `选卡页视图模型.取走载入标记`)。
        /// </summary>
        private bool 取走载入标记(string 文本)
        {
            var 页 = DataContext as 选卡页视图模型;
            bool 换了页 = !ReferenceEquals(页, _文档属于哪一页);
            _文档属于哪一页 = 页;
            bool 页上标着载入 = 页?.取走载入标记(文本) == true;
            return 撤销边界C.算载入(换了页, 页上标着载入);
        }

        /// <summary> C29:清空撤销栈(AvalonEdit 现成 API)——“载入完成后才开始算撤销”就靠这一下 </summary>
        public void 清空撤销栈()
        {
            try { 代码编辑器?.Document?.UndoStack.ClearAll(); } catch { }
        }
        #endregion

        // ==================== 代码折叠 ====================

    }
}
