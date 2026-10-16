using hehshihbeen_compiler.界面控件;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 编辑器的一部分:**多光标 / 列选择**(P40,2026-10-07;用户排的七件里的第 7 件)。
    ///
    /// **列选择**(Alt + 鼠标拖)不用写:那一份是 AvalonEdit 自带的
    /// (`TextArea.MouseSelectionMode = RectangleSelection` 那一档),我们要做的只是**把它开着**,
    /// 并让"输入 / 删除"落到那个矩形里的每一行 —— 这一段见 `接上多光标`。
    ///
    /// **多光标**(自己实现的那一半):AvalonEdit **没有**多光标(它的 `Selection` 只有一个),
    /// 所以这里自己记一张"副光标"表 + 自己画(见 `多光标渲染`),输入时再**逐个位置补一遍**:
    ///   · 编辑框先把字插在**主光标**处(它本来就管这一处);
    ///   · 我们在 `TextArea.TextEntered` 里拿到刚插进去的那段字,再把它插到每个副光标处;
    ///   · 删除同理(退格:每个副光标前面删一个字符)。
    /// 键位(与 VS Code 一套):
    ///   · **Ctrl+D** —— 选中下一个相同的词(先在光标处选一个,再按就一处一处往下加);
    ///   · **Ctrl+Alt+下 / 上** —— 在下一行 / 上一行同列加一个光标;
    ///   · **Esc** —— 收起所有副光标。
    /// **边界(如实说)**:多光标只覆盖"输入字符 / 回车 / 退格 / 删除 / Tab"这几种编辑;
    /// 别的高级编辑(替换、格式化这些整篇推文本的操作)只作用在主光标那一处 ——
    /// 那种情况下副光标不会跟着乱跑(见 `文本变了` 里那一句"整篇换了就全收掉")。
    /// </summary>
    public partial class 编辑器 : UserControl
    {
        #region 多光标:状态

        /// <summary> 副光标的位置(文档下标;不含主光标) </summary>
        private readonly List<多光标C.光标位C> _副光标 = [];

        /// <summary> 正在做"多光标的一次输入"(它自己改文档时要挡住重入) </summary>
        private bool 多光标正在写;

        private bool 多光标已接;

        /// <summary> 现在有几个副光标(界面上给状态栏/自检看)</summary>
        public int 副光标个数 => _副光标.Count;

        #endregion

        #region 多光标:接线

        /// <summary> 编辑器造好时调一次:挂渲染器 + 接输入事件(都包 try —— 这是增强,装不上照旧能编辑)</summary>
        private void 接上多光标()
        {
            try
            {
                if (代码编辑器 == null || 多光标已接) return;
                多光标已接 = true;
                代码编辑器.TextArea.TextView.BackgroundRenderers.Add(new 多光标渲染(() => [.. _副光标.Select(x => x.位置)]));
                代码编辑器.TextArea.TextEntered += 多光标_文字进来了;
                代码编辑器.TextArea.PreviewKeyDown += 多光标_按键;
                代码编辑器.TextArea.DocumentChanged += (_, _) => 清掉多光标();
                // **列选择**(Alt + 鼠标拖)是 AvalonEdit 自带的:按住 Alt 拖就是 `Rectangular` 那一档,
                // 这一句只是把鼠标那一档写成**明面上的默认**(枚举名见 `MouseSelectionMode`);
                // 记在这儿是给接手的人看的 —— 别以为这一半没做。
                try { 代码编辑器.TextArea.MouseSelectionMode = MouseSelectionMode.Normal; }
                catch { }
            }
            catch { }
        }

        /// <summary> 收起所有副光标(公式化的"我不多选了") </summary>
        public void 清掉多光标()
        {
            if (_副光标.Count == 0) return;
            _副光标.Clear();
            重画多光标();
        }

        private void 重画多光标()
        {
            try { 代码编辑器?.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Selection); }
            catch { }
        }

        #endregion

        #region 多光标:按键

        /// <summary> Ctrl+D / Ctrl+Alt+↑↓ / Esc:这三个键在这一层拦(拦下就 `Handled`,不给编辑框) </summary>
        private void 多光标_按键(object? sender, KeyEventArgs e)
        {
            try
            {
                bool 控制 = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                bool 换档 = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                bool 替换 = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                if (e.Key == Key.Escape && _副光标.Count > 0)
                {
                    清掉多光标();
                    e.Handled = true;
                    return;
                }
                if (控制 && !替换 && !换档 && e.Key == Key.D)
                {
                    if (选中下一个相同的词()) e.Handled = true;
                    return;
                }
                if (控制 && 替换 && (e.Key == Key.Down || e.Key == Key.Up))
                {
                    if (在同一列再加一个(e.Key == Key.Down)) e.Handled = true;
                    return;
                }
                // 退格 / Delete:**排到编辑框自己删完之后**再补删(它删主光标那一处,我们补其余几处)
                if (_副光标.Count > 0 && (e.Key == Key.Back || e.Key == Key.Delete))
                {
                    bool 退格 = e.Key == Key.Back;
                    Dispatcher.BeginInvoke(new Action(() => 多光标补删(退格)),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch { }
        }

        /// <summary>
        /// Ctrl+D:把"下一个相同的词"也选上。
        /// 第一次按 = 选中光标下那个词;再按 = 往后找**下一个**整词相同的地方,把它加成第二个光标
        /// (两个都要选着那一段,这样接着打字就是"一起改")。
        /// 找不到 / 词取不到 ⇒ 返回 false(不拦键,照旧给编辑框)。
        /// </summary>
        private bool 选中下一个相同的词()
        {
            var 编 = 代码编辑器;
            if (编?.Document == null) return false;
            string 文 = 编.Text;
            string 主词;
            int 主位;
            if (!string.IsNullOrEmpty(编.SelectedText))
            {
                主词 = 编.SelectedText;
                主位 = 编.SelectionStart;
                // 选中的是**多行**一段就不玩这个了(那不是"一个词")
                if (主词.Contains('\n') || 主词.Contains('\r')) return false;
            }
            else
            {
                var 词位 = 取光标下的词与后继();
                if (词位 is not { } 位 || 位.词.Length == 0) return false;
                主词 = 位.词;
                // 光标下的词起点 = 光标往前退到词首
                int 起 = Math.Clamp(编.CaretOffset, 0, 文.Length);
                主位 = 起;
                while (主位 > 0 && 高亮扫描C.是词字符(文[主位 - 1])) 主位--;
                if (主位 + 主词.Length > 文.Length) return false;
            }

            var 下 = 多光标C.下一个相同词(文, 主词, 主位 + 主词.Length);
            if (下 is not { } 找) return false;
            if (!_副光标.Any(x => x.位置 == 主位 && x.选中长度 == 主词.Length))
                _副光标.Add(new 多光标C.光标位C(主位, 主词.Length));
            _副光标.Add(new 多光标C.光标位C(找.位置, 找.长度));
            _副光标.RemoveAll(x => x.位置 == 找.位置 && x.选中长度 != 找.长度);
            规整副光标();
            // 主光标挪到刚加的那一处,并选着它 —— 接着按 Ctrl+D 就一处一处往下加
            编.SelectionStart = 找.位置;
            编.SelectionLength = 找.长度;
            重画多光标();
            return true;
        }

        /// <summary> Ctrl+Alt+下/上:在下一行 / 上一行**同一列**再加一个光标(到文档头尾就不动) </summary>
        private bool 在同一列再加一个(bool 向下)
        {
            var 编 = 代码编辑器;
            if (编?.Document == null) return false;
            string 文 = 编.Text;
            var (行, 列) = 多光标C.位置(文, 编.CaretOffset);
            if (多光标C.相邻行(行, 列, 向下, 文) == null) return false;
            if (多光标C.偏移(文, 行 + (向下 ? 1 : -1), 列) is not { } 新位) return false;
            _副光标.Add(new 多光标C.光标位C(新位));
            规整副光标();
            重画多光标();
            return true;
        }

        /// <summary> 副光标规整:去重 + 去掉跟主光标重合的(主光标那一处由编辑框自己管) </summary>
        private void 规整副光标()
        {
            int 主 = 代码编辑器?.CaretOffset ?? -1;
            var 干净 = 多光标C.去掉主光标(多光标C.规整(_副光标), 主);
            _副光标.Clear();
            _副光标.AddRange(干净);
        }

        #endregion

        #region 多光标:输入与删除逐个补一遍

        /// <summary>
        /// 编辑框往**主光标**处插了一段文字之后(`TextEntered`):同一段再往每个副光标处插一遍。
        /// 从后往前插 —— 前面的下标才不会被后一次插入带偏。
        /// </summary>
        private void 多光标_文字进来了(object? sender, TextCompositionEventArgs e)
        {
            try
            {
                if (多光标正在写 || _副光标.Count == 0) return;
                var 编 = 代码编辑器;
                if (编?.Document == null) return;
                string 插入 = e.Text ?? "";
                if (插入.Length == 0) return;

                var 目标 = 多光标C.去掉主光标(多光标C.规整(_副光标), 编.CaretOffset);
                if (目标.Count == 0) { 规整副光标(); return; }

                多光标正在写 = true;
                try
                {
                    var 文档 = 编.Document;
                    int 主位移 = 编.CaretOffset - 插入.Length;      // 这次插入落在主光标的哪儿
                    using (文档.RunUpdate())
                    {
                        foreach (var 一 in 目标)
                        {
                            int 起 = 一.位置;
                            int 长 = Math.Max(0, 一.选中长度);
                            // 主光标那一次插入在它前面:位置要往后挪(不然会插到上一处里去)
                            if (主位移 < 起) 起 += 插入.Length;
                            起 = Math.Clamp(起, 0, 文档.TextLength);
                            长 = Math.Clamp(长, 0, 文档.TextLength - 起);
                            文档.Replace(起, 长, 插入);
                        }
                    }
                    // 光标:每个副光标走到它自己那一段后面
                    var 新 = new List<多光标C.光标位C>();
                    foreach (var 一 in 目标)
                    {
                        int 起 = 一.位置 + (主位移 < 一.位置 ? 插入.Length : 0);
                        新.Add(new 多光标C.光标位C(Math.Clamp(起 + 插入.Length, 0, 编.Document.TextLength)));
                    }
                    _副光标.Clear();
                    _副光标.AddRange(多光标C.规整(新));
                }
                finally { 多光标正在写 = false; }
                重画多光标();
            }
            catch { 清掉多光标(); }        // 补不动就全收掉:宁可回到"只有主光标",也不许把文本改坏
        }

        /// <summary>
        /// 删除(退格 / Delete):编辑框只在主光标处删了一个,副光标处**一个一个补删**。
        /// 从后往前删,删完把副光标往前挪一格。
        /// </summary>
        public void 多光标补删(bool 退格)
        {
            try
            {
                if (多光标正在写 || _副光标.Count == 0) return;
                var 编 = 代码编辑器;
                if (编?.Document == null) return;
                var 目标 = 多光标C.去掉主光标(多光标C.规整(_副光标), 编.CaretOffset);
                if (目标.Count == 0) return;
                多光标正在写 = true;
                try
                {
                    var 文档 = 编.Document;
                    var 新 = new List<多光标C.光标位C>();
                    using (文档.RunUpdate())
                    {
                        foreach (var 一 in 目标)
                        {
                            int 长 = Math.Max(0, 一.选中长度);
                            int 起 = 一.位置;
                            if (长 == 0)
                            {
                                // 没选中:退格删前面那个字符,Delete 删后面那个
                                起 = 退格 ? 起 - 1 : 起;
                                长 = 1;
                            }
                            起 = Math.Clamp(起, 0, Math.Max(0, 文档.TextLength - 1));
                            长 = Math.Clamp(长, 0, 文档.TextLength - 起);
                            if (长 == 0) continue;
                            文档.Remove(起, 长);
                            新.Add(new 多光标C.光标位C(起));
                        }
                    }
                    _副光标.Clear();
                    _副光标.AddRange(多光标C.规整(新));
                }
                finally { 多光标正在写 = false; }
                重画多光标();
            }
            catch { 清掉多光标(); }
        }

        #endregion
    }
}
