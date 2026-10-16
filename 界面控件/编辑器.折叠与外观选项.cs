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
    /// <summary> 编辑器的一部分:代码折叠 / 语义对齐 / 编辑命令 / 应用外观选项 </summary>
    public partial class 编辑器 : UserControl
    {
        #region 代码折叠:块 {} + 连续注释块(通用规则,和 VS 那种一样)
        /// <summary> AvalonEdit 的折叠管理器(整篇文档的折叠区都登记在它这里) </summary>
        private ICSharpCode.AvalonEdit.Folding.FoldingManager? 折叠器;

        /// <summary>
        /// 在**当前文档**上装一个折叠管理器(每个编辑器实例在构造时装一次)。
        ///
        /// **为什么要有这个函数**(第141批实测定位,C12"点汇编视图整个窗口卡死"的根因):
        /// `FoldingManager` 是**绑死在某一个 `TextDocument` 对象**上的(它内部的 `TextSegmentCollection`
        /// 记的文档就是装它那一刻的那一个)。换了文档对象还留着它,排版时 `FoldingElementGenerator.
        /// StartGeneration` 会拿旧文档去查新文档的偏移,抛 `ArgumentException: Invalid document`
        /// ⇒ **一行可视行都建不出来**(实测 `TextView.VisualLines` 直接抛异常),界面就成了"点了没反应"。
        /// 所以规矩是:**换文档对象之前先 `卸掉折叠器()`,换完再 `装上折叠器()`**。
        /// (第158批起编辑器里没有"换文档"那条路了 —— 汇编只读视图已撤 ——
        ///  这一对函数留着,是因为它们就是 AvalonEdit 的通用护栏,以后真换文档别再踩这个坑。)
        /// </summary>
        private void 装上折叠器()
        {
            if (代码编辑器 == null) return;
            折叠器 = ICSharpCode.AvalonEdit.Folding.FoldingManager.Install(代码编辑器.TextArea);
            换成无连线边距();          // 第152批:折叠符那一栏那条"折叠范围线"不要(用户截图点名)
        }

        /// <summary>
        /// 把 AvalonEdit 自带的折叠边距换成我们的替身(`无连线折叠边距`):**只留 `+` / `-` 标记**。
        /// 位置 / 宽度 / 交互都不动 —— 同一个位置换一个"不画那两条范围线"的同类实例;
        /// 四个画刷属性照抄过去(主题或样式万一设过,不能让标记跟着变色)。
        /// </summary>
        private void 换成无连线边距()
        {
            try
            {
                var 边距们 = 代码编辑器?.TextArea.LeftMargins;
                if (边距们 == null) return;
                for (int i = 0; i < 边距们.Count; i++)
                {
                    if (边距们[i] is not ICSharpCode.AvalonEdit.Folding.FoldingMargin 旧) continue;
                    if (旧 is 无连线折叠边距) continue;                 // 已经是替身了
                    var 新 = new 无连线折叠边距
                    {
                        FoldingManager = 折叠器!,
                        FoldingMarkerBrush = 旧.FoldingMarkerBrush,
                        FoldingMarkerBackgroundBrush = 旧.FoldingMarkerBackgroundBrush,
                        SelectedFoldingMarkerBrush = 旧.SelectedFoldingMarkerBrush,
                        SelectedFoldingMarkerBackgroundBrush = 旧.SelectedFoldingMarkerBackgroundBrush,
                    };
                    边距们[i] = 新;
                }
            }
            catch { /* 换不上就用自带那个(多一条灰线而已):绝不影响编辑/编译 */ }
        }

        /// <summary>
        /// 卸掉折叠管理器(折叠边距靠 `TextView` 的服务自己认领/放手,不用手动摘)。
        /// **替身边距要手动摘**:`FoldingManager.Uninstall` 只摘它自己装的那一个,
        /// 不认得我们换上去的 `无连线折叠边距` —— 不摘的话换文档来回几次,左边会越攒越多条边距。
        /// (第158批起没有换文档那条路了,见 `装上折叠器` 的注释。)
        /// </summary>
        private void 卸掉折叠器()
        {
            if (折叠器 == null) return;
            try { ICSharpCode.AvalonEdit.Folding.FoldingManager.Uninstall(折叠器); } catch { }
            折叠器 = null;
            try
            {
                var 边距们 = 代码编辑器?.TextArea.LeftMargins;
                if (边距们 != null)
                    foreach (var 边距 in 边距们.OfType<无连线折叠边距>().ToList()) 边距们.Remove(边距);
            }
            catch { }
        }

        /// <summary>
        /// 按"通用折叠规则"重算全文的折叠区 —— 规则本身在引擎层 `前端契约C.服务.编译.算折叠区()`
        /// (这样自检能直接喂源码比结果;这里只负责**贴到 AvalonEdit 上**)。
        ///
        /// **C13 修的那个坑**:AvalonEdit 的 `NewFolding(起, 止)` 认的是**字符偏移**,
        /// 以前这里把 `开.行` / `行` 两个**行号**当偏移传了进去 ⇒ 每条折叠都缩成"第 1 行里的一小段"
        /// (探针实测 `1-1 / 1-1`),三角要么看不见、点出来也没东西 —— 用户说"看不见折叠"就是这个。
        /// 现在传的是 `折叠区C` 算好的**字符偏移**(行首/行尾口径)。
        ///
        /// 纯折叠:**不换内容、不动源码**,只把行收起来(字符串/注释里的括号不参与)。
        /// </summary>
        public void 刷新折叠区()
        {
            try
            {
                if (折叠器 == null || 代码编辑器 == null) return;
                var doc = 代码编辑器.Document;
                if (doc == null) return;
                var 区 = 前端契约C.服务.编译.算折叠区(代码编辑器.Text);
                if (区.Count == 0) { 折叠器.UpdateFoldings([], -1); return; }
                var 排好 = new ICSharpCode.AvalonEdit.Folding.NewFolding[区.Count];
                for (int i = 0; i < 区.Count; i++)
                    排好[i] = new ICSharpCode.AvalonEdit.Folding.NewFolding(区[i].起始, 区[i].结束);
                折叠器.UpdateFoldings(排好, -1);
            }
            catch { /* 折叠只是观感:算不出来就当没折叠,绝不影响编辑/编译 */ }
        }
        #endregion

        /// <summary> 光标所在行(切换断点用);没有编辑器时为 0 </summary>
        public int 当前行号 => 代码编辑器?.TextArea.Caret.Line ?? 0;

        /// <summary>
        /// 点断点边距时的处理:**静态挂一次**,所有编辑器实例都用它。
        /// (以前是每个实例一个 Action,由主窗口"扫一遍界面上的编辑器"去挂 —— 可编辑器是切标签时才生成的,
        ///  挂载时机一错,鼠标点上去就没人接,只能先点一下菜单才能用。静态挂一次就没这个问题。)
        /// </summary>
        public static Action<int>? 断点被点击;

        private void 断点边距点击(int 行号) => 断点被点击?.Invoke(行号);

        /// <summary>
        /// 右键菜单里"重命名"被点了。**静态挂一次**,所有编辑器实例都管用 ——
        /// 与 `断点被点击` 同一套写法(编辑器是切标签时才生成的,挂到实例上容易挂漏)。
        /// 真正的实现在主窗口:它要拿整个工程的文件表算编辑清单、再逐个标签页落盘。
        /// </summary>
        public static Action? 重命名被请求;

        private void 右键重命名_Click(object sender, RoutedEventArgs e) => 重命名被请求?.Invoke();

        /// <summary> 右键"转到定义 / 查找引用"被点了(P22;与 `重命名被请求` 同一套静态挂法,实现在主窗口) </summary>
        public static Action? 转到定义被请求;
        public static Action? 查找引用被请求;

        private void 右键转到定义_Click(object sender, RoutedEventArgs e) => 转到定义被请求?.Invoke();
        private void 右键查找引用_Click(object sender, RoutedEventArgs e) => 查找引用被请求?.Invoke();

        /// <summary> 右键"打开思维导图"被点了(H33;同一套静态挂法,实现在主窗口) </summary>
        public static Action? 思维导图被请求;

        private void 右键思维导图_Click(object sender, RoutedEventArgs e) => 思维导图被请求?.Invoke();

        /// <summary> 右键"切换断点 / 断点条件… / 删除全部断点"被点了(P24;同一套静态挂法,实现在主窗口) </summary>
        public static Action? 切换断点被请求;
        public static Action? 断点条件被请求;
        public static Action? 删除全部断点被请求;
        /// <summary> P35:"加监视表达式…"(右键菜单那一条,与调试菜单里那条同一件事)</summary>
        public static Action? 加监视被请求;

        private void 右键切换断点_Click(object sender, RoutedEventArgs e) => 切换断点被请求?.Invoke();
        private void 右键断点条件_Click(object sender, RoutedEventArgs e) => 断点条件被请求?.Invoke();
        private void 右键删除全部断点_Click(object sender, RoutedEventArgs e) => 删除全部断点被请求?.Invoke();
        private void 右键加监视_Click(object sender, RoutedEventArgs e) => 加监视被请求?.Invoke();

        /// <summary> 本文件里哪些行有断点(主窗口算好传进来,边距画红点) </summary>
        public void 设断点行(IEnumerable<int> 行号们)
        {
            try { 断点栏.更新标记([.. 行号们]); }
            catch { }
        }

        /// <summary>
        /// P34:这一份里"带附加条件"(条件 / 命中次数 / 日志)的断点行 —— 边距把它们画成**空心红圈**,
        /// 与"普通断点"(实心红点)一眼分得开。主窗口在 `刷新断点显示` 里两个一起传进来。
        /// </summary>
        public void 设断点附加行(IEnumerable<int> 行号们)
        {
            try { 断点栏.更新附加标记([.. 行号们]); }
            catch { }
        }

        /// <summary> P35:编辑器里现在选中的那一段文字(没有编辑器 / 没选中就给空串) </summary>
        public string 取选中的词()
        {
            try { return 代码编辑器?.SelectedText ?? ""; }
            catch { return ""; }
        }

        /// <summary> 当前光标行号(主窗口"切换断点"命令要问) </summary>
        public int 取当前行号() => 当前行号;

        /// <summary>
        /// P36:**光标现在在哪**(行列都按 1 基报;没有编辑器给 `(0, 0)`)—— 会话记忆存的就是它。
        /// 列取的是**文档列**(不是屏幕列):一行里的第几个字符,恢复时光标落在同一个字符前面。
        /// </summary>
        public (int 行, int 列) 取光标位置()
        {
            try
            {
                var 编 = 代码编辑器;
                var 文档 = 编?.Document;
                if (编 == null || 文档 == null) return (0, 0);
                int 偏移 = Math.Clamp(编.CaretOffset, 0, 文档.TextLength);
                var 行段 = 文档.GetLineByOffset(偏移);
                return (行段.LineNumber, 偏移 - 行段.Offset + 1);
            }
            catch { return (0, 0); }
        }

        /// <summary>
        /// P36:把光标放回"第几行第几列"。行号越界 / 列越界一律**夹到最近的位置**,不抛也不跳空。
        /// 顺带把它滚进视野(`ScrollToLine`)—— 恢复完看不见光标等于没恢复。
        /// </summary>
        public void 设光标位置(int 行, int 列)
        {
            try
            {
                var 编 = 代码编辑器;
                var 文档 = 编?.Document;
                if (编 == null || 文档 == null || 文档.LineCount == 0) return;
                int 目标行 = Math.Clamp(行, 1, 文档.LineCount);
                var 行段 = 文档.GetLineByNumber(目标行);
                int 目标列 = Math.Clamp(列 < 1 ? 1 : 列, 1, 行段.Length + 1);
                编.CaretOffset = 行段.Offset + 目标列 - 1;
                编.ScrollToLine(目标行);
            }
            catch { }
        }

        /// <summary> 取某一行的原文(判断"这行是不是注释/空行"用);没有编辑器或行号越界时返回空串 </summary>
        public string 取行文本(int 行号)
        {
            try
            {
                var doc = 代码编辑器?.Document;
                if (doc == null || 行号 <= 0 || 行号 > doc.LineCount) return "";
                return doc.GetText(doc.GetLineByNumber(行号));
            }
            catch { return ""; }
        }

        /// <summary>
        /// **光标底下那个名字**(字母/数字/下划线/汉字连续的一段,`.` 是分界)。
        /// 口径与 `高亮扫描C.是词字符` 一致,也就是与"鼠标底下的词"那套判定同源;
        /// 光标落在空白/标点/空文件上时返回 null(调用方据此提示"把光标放进名字里")。
        /// </summary>
        public string? 取光标下的词() => 取光标下的词与后继()?.词;

        /// <summary>
        /// 光标底下那个名字 + 它**后面紧挨着的那个字符**(词尾就是文件尾时返回 '\0')。
        /// "重命名"靠这个后继字符分清"这是在**调用函数**(后面跟 `(`)"还是"只是一个同名的变量/字段",
        /// 免得把变量的改名套到同名函数上去。
        /// </summary>
        public (string 词, char 后一个)? 取光标下的词与后继()
        {
            try
            {
                var doc = 代码编辑器?.Document;
                if (doc == null) return null;
                string 文本 = doc.Text;
                if (文本.Length == 0) return null;
                int 偏移 = Math.Clamp(代码编辑器!.CaretOffset, 0, 文本.Length);
                var 行段 = doc.GetLineByOffset(偏移);
                int 起 = 偏移, 止 = 偏移;
                while (起 > 行段.Offset && 是词字符(文本[起 - 1])) 起--;
                while (止 < 行段.EndOffset && 止 < 文本.Length && 是词字符(文本[止])) 止++;
                if (止 <= 起) return null;
                char 后一个 = 止 < 文本.Length ? 文本[止] : '\0';
                return (文本[起..止], 后一个);
            }
            catch { return null; }
        }

        // ---------------- 汇编只读视图已撤(第158批) ----------------
        // 旧 C12 那条"把编辑器换成整篇展开的汇编(只读)"的路整段删掉了:
        // 用户 2026-09-20 定下"逐步不再展开编辑器",而工具栏那个按钮改成**开新页**
        // (`<被展开文件>_ASM.hba`,可编辑可保存)⇒ 这个只读视图再没有入口。
        // 现在整篇展开一律在**真页**上看:C20 编译后自动开的那页 / C23 工具栏那页。

        #region 语义对齐(按语义列整文对齐)
        /// <summary>
        /// 按“语义列”对齐整篇文本(只改空白,汇编结果不变)。
        /// **T27**:换行口径由 `编译服务C.对齐语义列` 按输入逐行还原(CRLF / LF / 混合都不变)——
        /// 以前这里整篇 `Replace` 会把 CRLF 文件的行尾全换成 LF,保存一下 `git diff` 整篇翻。
        /// </summary>
        public void 语义对齐()
        {
            if (代码编辑器 == null) return;
            string 现文本 = 代码编辑器.Text;
            string 新文本 = 前端契约C.服务.编译.对齐语义列(现文本, out _);
            if (新文本 == 现文本) return;

            var doc = 代码编辑器.Document;
            结构操作中 = true;
            try
            {
                doc.BeginUpdate();
                doc.Replace(0, doc.TextLength, 新文本);
            }
            finally
            {
                doc.EndUpdate();
                结构操作中 = false;
            }
            刷新折叠区();
            Dispatcher.BeginInvoke(new Action(() => 刷新高亮()), DispatcherPriority.Background);
        }
        #endregion

        // ===== 菜单编辑命令(复制/粘贴/删除/全选),转发给 AvalonEdit =====
        #region 编辑命令(复制/粘贴/删除/全选)
        private void 执行编辑命令(RoutedCommand 命令)
        {
            if (代码编辑器 == null) return;
            try { 命令.Execute(null, 代码编辑器); } catch { }
        }
        public void 执行复制() => 执行编辑命令(ApplicationCommands.Copy);
        public void 执行粘贴() => 执行编辑命令(ApplicationCommands.Paste);
        public void 执行删除() => 执行编辑命令(ApplicationCommands.Delete);
        public void 执行全选() => 执行编辑命令(ApplicationCommands.SelectAll);
        #endregion

        #region 应用外观选项(字体/字号/行高等)
        /// <summary>
        /// 把“选项”里的编辑器外观套到本编辑器。
        /// H8:顺手落两件观感开关 —— **缩进导引线**那个布尔位(`导引线渲染.显示`)与
        /// **7 档彩虹色**(`高亮器.刷新花括号色()`;缓存清掉之后必须重绘,不然屏上还是老色)。
        /// </summary>
        public void 应用选项()
        {
            try
            {
                var 选项 = 配置.选项.当前;
                代码框通用设置.字体 = new System.Windows.Media.FontFamily(选项.字体族);
                代码框通用设置.字号 = 选项.字号;
                代码框通用设置.字粗 = 选项.粗体 ? FontWeights.Bold : FontWeights.Normal;
                代码框通用设置.字体样式 = 选项.斜体 ? FontStyles.Italic : FontStyles.Normal;
                代码框通用设置.行高 = 选项.行高;
                代码框通用设置.显示行号 = 选项.显示行号;
                代码框通用设置.自动换行 = 选项.自动换行;
                装上缩进策略();          // C5:自动缩进开关/缩进宽度改了就重挂(策略每次现取设置,这里只是保险)

                // H8:缩进导引线开关(渲染器常挂在背景层上,关掉只是不画)
                if (导引线渲染 != null) 导引线渲染.显示 = 选项.缩进导引线;
                // H8:花括号 7 档彩虹色(选项里那份 → 规范 → 清画刷缓存)
                高亮器.刷新花括号色();
                try { 代码编辑器?.TextArea.TextView.Redraw(); } catch { }
                Dispatcher.BeginInvoke(new Action(() => 刷新高亮()), DispatcherPriority.Background);
            }
            catch { }
        }
        #endregion

    }
}
