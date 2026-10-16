using hehshihbeen_compiler.选卡控件;
using System.IO;
using System.Windows;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 主窗口的一部分:**会话记忆**(P36,2026-10-07;用户排的七件里的第 3 件:
    /// "标签 / 光标 / 断点 / 窗口位置,连带未保存恢复")。
    ///
    /// 记与恢复各一处进出:
    ///   · **记** = 关窗那一刻(`MainWindow.窗口与文件.cs` 的 `Closing`,排在"保存提问都过了"之后)
    ///     + 换工程 / 关项目之前(`只留这些项目` 那一路);
    ///   · **恢复** = 打开工程文件成功之后(`打开项目文件`:**有会话就按会话开页**,没有才退回"打开启动文件");
    ///   · **窗口位置**在"恢复会话"那一刻一起摆好,启动页关掉时按同一份记忆决定要不要还原成最大化
    ///     (见 `MainWindow.项目与编译.cs` 的 `显示启动页` —— 不然那次 `WindowState = Normal` 会把它抹掉)。
    ///
    /// **断点不在这里记**:它本来就跟着工程走(项目目录里的 `断点.hbd`,见 `断点表C`)——
    /// 再抄一份就是两个事实源,改一处漏一处。
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 会话记忆:记与恢复

        /// <summary> 当前该用哪个键记会话(工程文件全称优先,没有就用目录);没有工程给空串 </summary>
        private string 会话键()
        {
            var 项目 = 解决方案.启动项目 ?? 解决方案.项目表.FirstOrDefault();
            return 项目 == null ? "" : 会话表C.键(项目.工程路径, 项目.目录);
        }

        /// <summary> 把"现在这一屏"收成一份会话(页表 + 光标 + 未保存正文 + 当前页 + 窗口四格)</summary>
        private 会话工程C 收会话()
        {
            var 会话 = new 会话工程C();
            int 当前位 = -1;
            var 当前页 = 当前选卡;
            foreach (var 页 in 主视图模型.选卡页视图模型表)
            {
                if (页.是特殊页) continue;
                var 记 = new 会话页C
                {
                    全称 = 页.全称 ?? "",
                    标题 = 页.标题 ?? "",
                    未保存正文 = 页.有未保存更改 ? (页.内容 ?? "") : "",
                };
                // 光标只对"现在正看着的那一页"是准的(编辑器控件是各页共用的一个)——
                // 别的页记下上一次存过的位置(没有就 0),恢复时不至于跳到开头
                if (ReferenceEquals(页, 当前页) && 当前编辑器 is { } 编)
                {
                    var (行, 列) = 编.取光标位置();
                    记.行 = 行;
                    记.列 = 列;
                }
                else if (_光标备忘.TryGetValue(页, out var 老))
                {
                    记.行 = 老.行;
                    记.列 = 老.列;
                }
                if (ReferenceEquals(页, 当前页)) 当前位 = 会话.页表.Count;
                会话.页表.Add(记);
            }
            会话.当前页 = 当前位;

            // 窗口四格:最小化时**不记**(记下来下次开成一条线);最大化记 RestoreBounds(那是"还原后"的大小)
            try
            {
                if (WindowState != WindowState.Minimized)
                {
                    会话.最大化 = WindowState == WindowState.Maximized;
                    var 框 = 会话.最大化 ? RestoreBounds : new Rect(Left, Top, Width, Height);
                    if (!框.IsEmpty && 框.Width >= 400 && 框.Height >= 300)
                    {
                        会话.左 = 框.Left;
                        会话.上 = 框.Top;
                        会话.宽 = 框.Width;
                        会话.高 = 框.Height;
                    }
                }
                else if (_上次窗口框 is { } 老框)
                {
                    // 现在是"最小化"那一档:沿用上一次记下的框(不然一最小化关窗,下次窗口位置就没了)
                    会话.左 = 老框.Left; 会话.上 = 老框.Top; 会话.宽 = 老框.Width; 会话.高 = 老框.Height;
                    会话.最大化 = _上次最大化;
                }
            }
            catch { }
            return 会话;
        }

        /// <summary> 上一次记下的窗口框(最小化时兜底用)</summary>
        private Rect? _上次窗口框;
        private bool _上次最大化;

        /// <summary>
        /// 恢复出来的会话里"上次是不是最大化"(启动页关掉那一刻要用)。
        /// 为什么不每次去问会话文件:启动页关掉之后用户可能换了工程 —— 那一格跟着**最近一次恢复 / 记录**走才对。
        /// </summary>
        private bool _要最大化;

        /// <summary> 把"现在这一屏"记进会话文件(关窗 / 换工程时调;没工程就什么都不做)</summary>
        private void 记会话()
        {
            try
            {
                string 键 = 会话键();
                if (键.Length == 0) return;
                var 会话 = 收会话();
                if (会话.有窗口位置)
                {
                    _上次窗口框 = new Rect(会话.左, 会话.上, 会话.宽, 会话.高);
                    _上次最大化 = 会话.最大化;
                    _要最大化 = 会话.最大化;
                }
                会话表C.当前.存(键, 会话);
            }
            catch (Exception 异常) { 异常收集C.记($"记会话失败: {异常.Message}"); }
        }

        /// <summary> 这一页上次光标在哪(切页之前先把它记下来;编辑器控件是共用的,不记就丢了)</summary>
        private readonly Dictionary<选卡页视图模型, (int 行, int 列)> _光标备忘 = [];

        /// <summary>
        /// 把**这一页**的光标位置记进备忘(切页之前调)。传 null / 特殊页就什么都不做。
        /// 注意:切页事件里"当前选卡"已经是新那一页了 ⇒ 调用方要把**旧那一页**显式传进来。
        /// </summary>
        private void 记住离页光标(选卡页视图模型? 页 = null)
        {
            try
            {
                页 ??= 当前选卡;
                if (页 == null || 页.是特殊页) return;
                if (当前编辑器 is { } 编)
                {
                    var 位 = 编.取光标位置();
                    if (位.行 > 0) _光标备忘[页] = 位;
                }
            }
            catch { }
        }

        /// <summary>
        /// 按会话把上一次那一屏摆回来。返回恢复了几页(0 = 这个工程没有会话,调用方照旧开启动文件)。
        /// 规矩三条:
        ///   ① **只补不删** —— 会话里那几页打不开(文件删了 / 挪走了)就跳过并说一句,绝不弹框拦住打开工程;
        ///   ② **未保存正文只推进编辑器,不落盘** —— 页头照样挂 `*`,存不存由用户按 Ctrl+S
        ///      (自动存盘会悄悄改掉用户的源码,那是另一件事);
        ///   ③ 窗口四格在这一刻就摆好,启动页关掉时按同一份记忆定"要不要最大化"。
        /// </summary>
        private int 恢复会话(会话工程C 会话)
        {
            if (会话 == null || 会话.页表.Count == 0) return 0;
            int 开成 = 0, 少页 = 0, 带未保存 = 0;
            foreach (var 记 in 会话.页表)
            {
                if (记.全称.Length == 0) { 少页++; continue; }        // 没路径的新页恢复不了
                if (!File.Exists(记.全称)) { 少页++; continue; }
                try
                {
                    打开文件到标签页(记.全称);
                    开成++;
                    var 页 = 找标签页(记.全称);
                    if (页 == null) continue;
                    if (记.行 > 0) _光标备忘[页] = (记.行, 记.列);
                    if (记.有未保存)
                    {
                        页.内容 = 记.未保存正文;
                        带未保存++;
                    }
                }
                catch (Exception 异常) { 异常收集C.记($"恢复会话那一页失败({记.显示名}): {异常.Message}"); 少页++; }
            }

            // 当前页 + 光标:选中之后再摆光标(选中会触发载入,光标会被重置)
            if (会话.当前页 >= 0 && 会话.当前页 < 会话.页表.Count)
            {
                var 想开 = 找标签页(会话.页表[会话.当前页].全称);
                if (想开 != null) 主视图模型.选中页 = 想开;
            }
            代码选卡.UpdateLayout();
            if (当前选卡 is { } 现页 && _光标备忘.TryGetValue(现页, out var 位)) 当前编辑器?.设光标位置(位.行, 位.列);

            // 窗口四格
            try
            {
                if (会话.有窗口位置)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = 会话.左; Top = 会话.上; Width = 会话.宽; Height = 会话.高;
                    _上次窗口框 = new Rect(会话.左, 会话.上, 会话.宽, 会话.高);
                    _上次最大化 = 会话.最大化;
                    _要最大化 = 会话.最大化;
                    // 最大化**现在不设**:启动页关掉那一刻有一句 `WindowState = Normal` 会把它抹掉 ——
                    // 那一句改成问 `还要最大化吗()`(见 `显示启动页`)
                }
            }
            catch { }

            说一句($"已恢复上次离开时的 {开成} 个标签页" +
                (带未保存 > 0 ? $"(其中 {带未保存} 个有**没保存**的改动,页头带 `*`,按 Ctrl+S 存)" : "") +
                (少页 > 0 ? $";{少页} 个打不开了(文件不在)" : "") + "。断点跟着工程,照旧在。");
            切到底部输出();
            return 开成;
        }

        /// <summary>
        /// 启动页关掉那一刻:窗口要不要**最大化**开?
        /// 判据就是最近一次"恢复会话 / 记会话"留下的那一格(用户上次离开时是不是最大化)。
        /// </summary>
        private bool 还要最大化() => _要最大化;

        // ---------------- 防崩自存 ----------------

        /// <summary> 上一次把会话写盘是什么时候(节流用)</summary>
        private DateTime _上次会话时刻 = DateTime.MinValue;

        /// <summary> 两次自存之间至少隔多久(用户停手 400ms 才触发一次,再加上这道闸 —— 磁盘不至于天天转)</summary>
        private static readonly TimeSpan 会话自存间隔 = TimeSpan.FromSeconds(10);

        /// <summary>
        /// P36:**防崩**的一道口子 —— 用户改完东西停手 400ms 那一下(与代码检查同一个节拍)顺手记一次会话。
        /// 为什么要它:只在关窗那一刻记的话,**崩了 / 断电**那一份就没了(而"未保存恢复"要的正是那种时候)。
        /// 闸门两道:① 与代码检查同一个 400ms 防抖(不是每敲一个字);② 两次之间至少隔 `会话自存间隔`。
        /// 关窗之后不再写(`_已关窗`)。
        /// </summary>
        private void 安排会话自存()
        {
            try
            {
                if (_已关窗) return;
                if (DateTime.UtcNow - _上次会话时刻 < 会话自存间隔) return;
                _上次会话时刻 = DateTime.UtcNow;
                记住离页光标();
                记会话();
            }
            catch { }
        }

        #endregion
    }
}
