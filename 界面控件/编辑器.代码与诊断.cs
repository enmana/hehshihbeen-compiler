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
    /// <summary> 编辑器的一部分:代码读写接口 / 代码检查诊断 </summary>
    public partial class 编辑器 : UserControl
    {
        #region 代码读写接口(获取/设置代码)
        /// <summary> 获取当前编辑器中的纯文本内容 </summary>
        public string 获取代码() => 代码编辑器?.Text ?? string.Empty;

        /// <summary>
        /// 设置编辑器内容（自动刷新高亮）。统一走 内容 依赖属性，
        /// 这样外部赋值与数据绑定行为一致，双向绑定时会同步到数据源。
        /// </summary>
        public void 设置代码(string 文本)        {
            内容 = 文本 ?? string.Empty;
        }

        // ================= 代码检查:波浪下划线 + 悬停提示(带"固定"按钮) =================
        #region 代码检查诊断
        /// <summary> 新造出来一个编辑器控件(主窗口订这个,好挂"文本变了就重查一遍")</summary>
        public static event Action<编辑器>? 新造出来;
        /// <summary> 内容变了(主窗口订它做 400ms 防抖检查)</summary>
        public event Action? 文本变化;

        private readonly List<诊断项C> _诊断表 = [];
        private Popup? 诊断提示;
        /// <summary>
        /// **所有还开着的提示弹窗**。为什么留一张表而不是只留一个字段:
        /// 以前 ✕ 关的是"字段当前指的那个弹窗" —— 字段被新弹窗顶掉之后,旧弹窗就没人引用了,
        /// 它的 ✕ 再也关不掉自己(用户报的"点关闭也没用"就是这么来的)。现在每个弹窗的 ✕
        /// **关的是它自己**,而且这里留着全部引用,兜底时能一次全关掉。
        /// </summary>
        private readonly List<Popup> _开着的提示 = [];
        private bool 诊断提示已固定;
        /// <summary> 现在弹的是哪一条诊断(鼠标停在同一条上就别重弹,否则"固定"会被自己顶掉)</summary>
        private 诊断项C? 当前提示诊断;
        /// <summary> 提示弹出的时刻(刚弹出来的那几百毫秒里,鼠标事件会抖一下,别急着收)</summary>
        private DateTime 提示弹出时刻;
        /// <summary> 鼠标是不是正停在提示框里(停在里面别收,不然点不着"固定")</summary>
        private bool 鼠标在提示里;
        /// <summary>
        /// "该不该弹"的判定(引擎侧 `提示开关C`,可自检):明确关掉过的那一处,**鼠标不挪开就不再弹**
        /// —— 这条是"点 ✕ 之后过一秒又自己弹回来"那个 bug 的正解。
        /// </summary>
        private readonly 提示开关C 提示开关 = new();
        /// <summary> 当前悬停位置的键(词 + 行号):"同一处"的判据,也是"关过"记忆的键 </summary>
        private string? 当前悬停键;
        /// <summary> "鼠标走了以后过一会儿再收"用的计时器:给用户留出走两步去点"📌 固定"的时间</summary>
        private System.Windows.Threading.DispatcherTimer? 收提示计时器;
        /// <summary>
        /// "鼠标停住 0.45 秒 → 看这一处有没有诊断"的计时器。
        /// **不依赖 AvalonEdit 的 MouseHover**(那套事件在不同版本/不同宿主里行为不一样,用户反馈"鼠标放上去没提示"),
        /// 自己按鼠标位置定时查一次,最稳。
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? 悬停计时器;
        /// <summary>
        /// "鼠标在某个词上停住 → 弹这个词的用法提示"用的计时器(方法/关键字/类型/指令/本文件的函数名)。
        /// 比诊断提示(0.45 秒)再慢一点点:诊断是"有问题要提醒",用法是"想看才看"(用户实测 2 秒太长,改成约 0.8 秒)。
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? 词提示计时器;
        /// <summary> 现在等着弹/正在弹的是哪个词(鼠标挪到别的词上就换掉)</summary>
        private string? 当前提示词;
        /// <summary> 想让界面重画下划线(诊断变了就调一下)</summary>
        private void 刷新下划线() => 代码编辑器?.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);

        /// <summary> 把光标挪到某一行并滚动过去(问题页双击跳转用)</summary>
        public void 跳到行(int 行号)
        {
            try
            {
                if (代码编辑器?.Document == null) return;
                int 行 = Math.Max(1, Math.Min(行号, 代码编辑器.Document.LineCount));
                var 目标行 = 代码编辑器.Document.GetLineByNumber(行);
                代码编辑器.TextArea.Caret.Line = 行;
                代码编辑器.TextArea.Caret.Column = 1;
                代码编辑器.ScrollToLine(行);
                代码编辑器.Focus();
                _ = 目标行;
            }
            catch { }
        }

        /// <summary> 换成新的诊断列表(界面会自动重画下划线;提示框若开着先收起来 —— 固定了的除外)</summary>
        public void 设置诊断(IReadOnlyList<诊断项C> 诊断)
        {
            _诊断表.Clear();
            if (诊断 != null) _诊断表.AddRange(诊断);
            if (!诊断提示已固定) 收起诊断提示();   // 用户点了"固定":边改代码边留着
            刷新下划线();
        }

        /// <summary> 当前这份诊断(主窗口的"问题"页拿它显示)</summary>
        public IReadOnlyList<诊断项C> 取诊断() => _诊断表;

        /// <summary> 鼠标停在某处:安排 0.45 秒后查一次(停下不动才会走到真正弹提示那一步)</summary>
        private void 代码视图_鼠标动(object sender, MouseEventArgs e)
        {
            词提示计时器?.Stop();          // 鼠标一动,"停 2 秒才提示"就重新数
            // 鼠标挪到**别处**了:清掉"这一处关过"的记忆(所以"关掉→挪开→再挪回来"还能弹;
            // 一直赖在同一处不动则不再弹 —— 见 提示开关C)
            提示开关.记鼠标停在(取悬停键());
            安排悬停检查();
        }

        /// <summary>
        /// 现在这个悬停位置的**键**:`词@行`(鼠标那一点按"词"算,不做矩形核对 —— 这个函数会被
        /// 鼠标每动一下都调,必须便宜;矩形核对只在真正决定"弹哪个词"时才做)。
        /// 同一个词同一行算"同一处":关掉之后一直停在这儿就不再弹,挪走再回来又能弹。
        ///
        /// **护栏(第137批,用户实测)**:进/出汇编视图时 `代码编辑器.Document` 换成了**新文档对象**,
        /// 而 `TextArea.TextView` 那边要等一次重新排版才跟上 —— 这一小段时间里 AvalonEdit 的
        /// `GetPositionFromPoint` 会抛 `System.ArgumentException: Invalid document`
        /// (示例 59 转汇编时鼠标一动就炸在这条路上)。所以:
        /// ① 先比一下 `TextView.Document` 与 `Document` 是不是同一个,不是就当"没有悬停位置";
        /// ② 整段包 try/catch —— 鼠标事件里抛异常会直接进 WPF 的输入泵(界面就完了)。
        /// </summary>
        private string? 取悬停键()
        {
            try
            {
                var doc = 代码编辑器?.Document;
                if (doc == null || 代码编辑器 == null) return null;
                if (!ReferenceEquals(代码编辑器.TextArea.TextView.Document, doc)) return null;   // 换文档期间不查位置
                if (取鼠标处词(doc, 0) is not { } 候选) return null;
                var 位置 = 代码编辑器.GetPositionFromPoint(Mouse.GetPosition(代码编辑器));
                int 行 = 位置 == null ? 0 : Math.Max(1, Math.Min(位置.Value.Line, doc.LineCount));
                return $"{候选.词}@{行}";
            }
            catch { return null; }
        }

        private void 安排悬停检查()
        {
            悬停计时器 ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            悬停计时器.Stop();
            悬停计时器.Tick -= 悬停计时器_Tick;
            悬停计时器.Tick += 悬停计时器_Tick;
            悬停计时器.Start();
        }

        private void 悬停计时器_Tick(object? sender, EventArgs e)
        {
            悬停计时器?.Stop();
            if (代码编辑器 == null) return;
            try
            {
                var 视图 = 代码编辑器.TextArea.TextView;
                if (!视图.IsMouseOver) { if (诊断提示 != null && !诊断提示已固定) 收起诊断提示(); return; }
                var 命中 = 找光标下的诊断(视图, Mouse.GetPosition(视图));
                if (命中 != null)
                {
                    // 诊断优先(这是"有问题要提醒");有诊断就不再弹用法,免得两层提示打架
                    词提示计时器?.Stop();
                    当前提示词 = null;
                    当前悬停键 = 诊断提示位置键(命中);
                    提示开关.记鼠标停在(当前悬停键);
                    if (诊断提示 != null && 是同一条(当前提示诊断, 命中)) return;   // 同一条就不要重弹(会把"固定"顶掉)
                    if (!提示开关.可以弹(当前悬停键, DateTime.UtcNow)) return;      // 用户刚关掉这一条:别再弹回来
                    弹出诊断提示(命中);
                    return;
                }
                // 没有诊断 → 看鼠标底下的词:停住 2 秒再弹"这个词怎么用"
                string? 词 = 找鼠标下的词(视图, Mouse.GetPosition(视图));
                if (词 == null || 补全词表C.查说明(词) == null && 补全词表C.查声明行(词, 代码编辑器.Text) == null)
                {
                    词提示计时器?.Stop();
                    当前提示词 = null;
                    if (诊断提示 != null && !诊断提示已固定) 收起诊断提示(用户关的: false);
                    return;
                }
                当前提示词 = 词;
                当前悬停键 = 取悬停键();
                安排词提示检查();
            }
            catch { }
        }

        /// <summary> 鼠标在词上停住约 0.4 秒后弹用法提示(加上前面那次 0.45 秒的悬停检查,总共约 0.8 秒;鼠标一动就重新数) </summary>
        private void 安排词提示检查()
        {
            词提示计时器 ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            词提示计时器.Stop();
            词提示计时器.Tick -= 词提示计时器_Tick;
            词提示计时器.Tick += 词提示计时器_Tick;
            词提示计时器.Start();
        }

        private void 词提示计时器_Tick(object? sender, EventArgs e)
        {
            词提示计时器?.Stop();
            if (代码编辑器 == null || 当前提示词 == null) return;
            try
            {
                var 视图 = 代码编辑器.TextArea.TextView;
                if (!视图.IsMouseOver) return;
                // 鼠标可能已经挪到别的词上:对不上就不弹(等下一次悬停检查)
                if (找鼠标下的词(视图, Mouse.GetPosition(视图)) != 当前提示词) return;
                // **用户刚把这一处关掉**:鼠标没挪开就不再弹(不然就是"点了关闭关不掉")
                if (!提示开关.可以弹(当前悬停键, DateTime.UtcNow)) return;
                弹出词提示(当前提示词);
            }
            catch { }
        }

        /// <summary> 一条诊断的"位置键"(关过就不许再弹的那个键):行号 + 规则 </summary>
        private static string 诊断键(诊断项C 诊断) => $"#{诊断.行}:{诊断.规则}";

        /// <summary>
        /// **诊断提示的位置键**:鼠标底下有词就用 `词@行`(跟用法提示同一个键),没有词才退回 `#行:规则`。
        /// 为什么非要统一:一个弹窗只认一个键,而"关掉过"的记忆是按**键**存的。诊断用 `#行:规则`、
        /// 鼠标一动却按 `词@行` 记账,两种键互相把对方那条记忆清掉 —— 于是 ✕ 关掉之后指针一回到编辑器
        /// (弹窗消失时系统就会补一次鼠标移动),记忆已经没了,提示又被弹回来,看着就是"关闭没用"。
        /// </summary>
        private string? 诊断提示位置键(诊断项C 诊断) => 取悬停键() ?? 诊断键(诊断);

        /// <summary>
        /// 鼠标底下的那个"词"(字母/数字/下划线/汉字连续的一段,`.` 是分界)。
        /// 两步:① 用 AvalonEdit 的"点 → 文本位置"算出一个词;② **拿下划线同款矩形核对一遍**(所见即所点)——
        /// 坐标换算在"行号边距 / 滚动 / 换行"下偶尔会偏一两个字符(用户实测:停在 `长度` 上提示的是 `整数`),
        /// 所以对不上就左右挪一两个字符再试;全都核对不上就不提示(宁可不提示,也别给错词)。
        /// </summary>
        private string? 找鼠标下的词(TextView 视图, Point 视图点)
        {
            var doc = 视图?.Document;
            if (doc == null || 视图 == null || 代码编辑器 == null) return null;
            if (代码编辑器.Text.Length == 0) return null;
            string? 退路 = null;
            foreach (int 微调 in new[] { 0, -1, 1, -2, 2, -3, 3, -4, 4 })
            {
                if (取鼠标处词(doc, 微调) is not { } 候选) continue;
                if (鼠标落在词上(视图, 候选.起, 候选.止, 视图点)) return 候选.词;
                退路 ??= 候选.词;
            }
            return 退路;      // 一处都没核对上:留第一个猜测(总比不提示强,但不会乱跳)
        }

        /// <summary> 按"鼠标 → 文本位置 → 词"算一次(微调 = 往左/右挪几个字符再算) </summary>
        private (int 起, int 止, string 词)? 取鼠标处词(TextDocument doc, int 微调)
        {
            if (代码编辑器 == null) return null;
            // 换文档的一瞬间 TextView 还没跟上(见 取悬停键 的护栏):查了会抛 `Invalid document`,先躲开
            if (!ReferenceEquals(代码编辑器.TextArea.TextView.Document, doc)) return null;
            // 注意:GetPositionFromPoint 要的是**编辑器控件**里的点(不是文本框里的点,差着一个行号边距)
            ICSharpCode.AvalonEdit.TextViewPosition? 位置;
            try { 位置 = 代码编辑器.GetPositionFromPoint(Mouse.GetPosition(代码编辑器)); }
            catch { return null; }        // 鼠标事件里绝不外抛(抛出去就进 WPF 输入泵)
            if (位置 == null) return null;
            string 文本 = doc.Text;
            int 行 = Math.Max(1, Math.Min(位置.Value.Line, doc.LineCount));
            var 行段 = doc.GetLineByNumber(行);
            int 基准 = doc.GetOffset(行, Math.Max(1, Math.Min(位置.Value.Column, 行段.Length + 1)));
            int 偏移 = Math.Clamp(基准 + 微调, 行段.Offset, 行段.EndOffset);
            if (偏移 > 文本.Length) 偏移 = 文本.Length;
            int 起 = 偏移, 止 = 偏移;
            while (起 > 行段.Offset && 起 - 1 < 文本.Length && 是词字符(文本[起 - 1])) 起--;
            while (止 < 行段.EndOffset && 止 < 文本.Length && 是词字符(文本[止])) 止++;
            if (止 <= 起) return null;
            return (起, 止, 文本[起..止]);
        }

        /// <summary> 鼠标是不是真落在这段文字上(矩形算法与诊断提示的"找光标下的诊断"同一套)</summary>
        private static bool 鼠标落在词上(TextView 视图, int 起, int 止, Point 点)
        {
            try
            {
                if (止 <= 起) return false;
                var 段 = new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = 起, EndOffset = 止 };
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(视图, 段))
                {
                    if (点.Y < r.Top - 6 || 点.Y > r.Bottom + 6) continue;
                    if (点.X >= r.Left - 8 && 点.X <= r.Right + 8) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary> 弹"这个词怎么用"的提示(壳子与诊断提示同一套:能固定、能关)</summary>
        private void 弹出词提示(string 词)
        {
            if (诊断提示已固定) return;                 // 用户把某条诊断钉住了:别抢它的位置
            string 说明 = 补全词表C.查说明(词) ?? "";
            string? 声明 = 补全词表C.查声明行(词, 代码编辑器?.Text ?? "");
            if (说明.Length == 0 && 声明 == null) return;
            string 正文 = 说明.Length > 0 ? 说明
                : "本文件里声明的名字(函数/结构/变量)";
            string? 种类 = 补全词表C.查种类(词);
            string 脚注 = 声明 != null ? $"声明:{声明}"
                : 种类 != null ? $"词条:{词}({种类})"
                : $"名字:{词}";
            if (当前悬停键 == null) 当前悬停键 = 取悬停键();
            收起诊断提示(用户关的: false);
            诊断提示已固定 = false;
            诊断提示 = 造提示弹窗("用法", Color.FromRgb(0x00, 0x78, 0xD7), 正文, 脚注);
            提示弹出时刻 = DateTime.UtcNow;
            提示开关.记弹出(当前悬停键, 提示弹出时刻);
        }

        /// <summary> 鼠标停在一段有诊断的代码上 → 弹提示(AvalonEdit 的 MouseHover 也接上,双保险)</summary>
        private void 代码视图_悬停(object sender, MouseEventArgs e)
        {
            if (代码编辑器 == null) return;
            try
            {
                var 视图 = 代码编辑器.TextArea.TextView;
                var 命中 = 找光标下的诊断(视图, Mouse.GetPosition(视图));
                if (命中 == null) { if (!诊断提示已固定) 收起诊断提示(用户关的: false); return; }
                if (诊断提示 != null && 是同一条(当前提示诊断, 命中)) return;   // 别把已固定的提示重弹掉
                if (!提示开关.可以弹(诊断提示位置键(命中), DateTime.UtcNow)) return;     // 刚关掉这一条:别弹回来
                弹出诊断提示(命中);
            }
            catch { }
        }

        /// <summary>
        /// 在鼠标那一点找诊断:**直接用画下划线用的那套矩形**判定(所见即所点),
        /// 不再自己按行列换算坐标(滚动以后行列换算容易偏)。
        /// 落在下划线上/贴着下划线 6px 内算命中;同一行上离得近(60px 内)也认。
        /// </summary>
        private 诊断项C? 找光标下的诊断(TextView 视图, Point 点)
        {
            if (视图?.Document == null || _诊断表.Count == 0) return null;
            var doc = 视图.Document;
            诊断项C? 最近 = null;
            double 最近距离 = double.MaxValue;
            foreach (var d in _诊断表)
            {
                if (d.行 <= 0 || d.行 > doc.LineCount) continue;
                var line = doc.GetLineByNumber(d.行);
                int 起 = line.Offset + Math.Max(0, Math.Min(d.起列, line.Length));
                int 止 = d.止列 > d.起列 ? line.Offset + Math.Min(d.止列, line.Length) : line.EndOffset;
                if (止 <= 起) { 起 = line.Offset; 止 = line.EndOffset; }
                var 段 = new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = 起, EndOffset = 止 };
                foreach (var r in BackgroundGeometryBuilder.GetRectsForSegment(视图, 段))
                {
                    if (点.Y < r.Top - 4 || 点.Y > r.Bottom + 4) continue;      // 不在这一行上
                    if (点.X >= r.Left - 6 && 点.X <= r.Right + 6) return d;    // 就在这段文字上/贴着它
                    double 距 = 点.X < r.Left ? r.Left - 点.X : 点.X - r.Right;
                    if (距 < 最近距离 && 距 <= 60) { 最近距离 = 距; 最近 = d; }
                }
            }
            return 最近;
        }

        private static bool 是同一条(诊断项C? 候选, 诊断项C 诊断)
            => 候选 != null && 候选.行 == 诊断.行 && 候选.级别 == 诊断.级别 && string.Equals(候选.文本, 诊断.文本, StringComparison.Ordinal);

        private void 弹出诊断提示(诊断项C 诊断)
        {
            收起诊断提示(用户关的: false);
            诊断提示已固定 = false;
            当前悬停键 = 诊断提示位置键(诊断);
            var 色 = 诊断.级别 switch
            {
                诊断级别.错误 => Color.FromRgb(0xE0, 0x3A, 0x3A),
                诊断级别.警告 => Color.FromRgb(0xCC, 0x9A, 0x00),
                _ => Color.FromRgb(0x2E, 0x9E, 0x4F),
            };
            诊断提示 = 造提示弹窗(
                诊断.级别 switch { 诊断级别.错误 => "错误", 诊断级别.警告 => "警告", _ => "提示" },
                色, 诊断.文本, $"{诊断.规则} · 第 {诊断.行} 行");
            提示弹出时刻 = DateTime.UtcNow;
            当前提示诊断 = 诊断;
            提示开关.记弹出(当前悬停键, 提示弹出时刻);
        }

        /// <summary>
        /// 造一个提示弹窗(诊断提示与"用法"提示共用这套壳子:右上角有 📌 固定 和 ✕,位置跟着鼠标不算远)。
        /// 造完就弹;弹窗自己也进 `_开着的提示`(兜底时能一次全关)。
        ///
        /// **✕ / 📌 绑的是"这个弹窗自己"**(不是"当前那个字段"):以前 ✕ 走 `收起诊断提示()` 去关字段指着的弹窗,
        /// 字段一旦被新弹窗顶掉,这个旧弹窗就再也关不掉了(用户报"点关闭也没用")。
        /// </summary>
        private Popup 造提示弹窗(string 标签, Color 色, string 正文, string 脚注)
        {
            var 头 = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
            var 内容 = new StackPanel { MaxWidth = 460 };
            // 先建弹窗(还不开),再挂事件 —— 事件里要用到 弹窗 自己
            var 弹窗 = new Popup
            {
                Child = new Border
                {
                    Child = 内容,
                    Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xFD, 0xFD)),
                    BorderBrush = new SolidColorBrush(色),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 8, 10, 8),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.25 },
                },
                Placement = PlacementMode.MousePoint,
                PlacementTarget = 代码编辑器,   // 挂到编辑器上:弹窗和主窗口同一条生命线
                // **别让弹窗盖住鼠标**:盖住了鼠标就"离开"了编辑器,我们自己那套"鼠标一走就收"会当场把它收掉 —— 看起来就像"没有提示"
                HorizontalOffset = 14,
                VerticalOffset = 20,
                StaysOpen = true,               // 自己管开合(鼠标走了要能自动收;固定了就留着)
                AllowsTransparency = true,
                IsOpen = false,
            };

            var 固定 = new ToggleButton
            {
                Content = "📌 固定", FontSize = 11, Padding = new Thickness(6, 0, 6, 0),
                ToolTip = "钉住:改代码的时候也留着(方便照着改);再点一下取消",
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(色),
            };
            固定.Checked += (_, _) => 诊断提示已固定 = true;
            固定.Unchecked += (_, _) => { 诊断提示已固定 = false; 关某一个提示(弹窗, 用户关的: false); };
            var 关 = new Button
            {
                Content = "✕", FontSize = 11, Padding = new Thickness(6, 0, 6, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                ToolTip = "关掉",
            };
            // **关的是它自己**:这样哪怕字段已经被新弹窗顶掉,这个 ✕ 也一定管用
            关.Click += (_, _) => 关某一个提示(弹窗, 用户关的: true);
            // 窗口没激活时,第一次点击可能被系统"吃掉"(只用来激活窗口)→ 按下就收,保证按钮一定管用
            关.PreviewMouseLeftButtonDown += (_, _) => 关某一个提示(弹窗, 用户关的: true);
            DockPanel.SetDock(关, Dock.Right);
            DockPanel.SetDock(固定, Dock.Right);
            头.Children.Add(关);
            头.Children.Add(固定);
            头.Children.Add(new TextBlock
            {
                Text = 标签,
                FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(色), VerticalAlignment = VerticalAlignment.Center,
            });

            内容.Children.Add(头);
            // 正文这一格**字色要自己钉**:弹窗那个 Border 是写死的浅底(`FDFDFD`),
            // 而 `Popup` 里这棵树的字色是从**编辑器**继承的 —— 暗 / 黑客帝国 / 赛博朋克下编辑器前景是浅色,
            // 不钉就是"浅字压浅底",提示等于没弹。头那一行的标签 / ✕ / 📌 各自都带着自己的前景色,不受影响。
            内容.Children.Add(new TextBlock
            {
                Text = 正文, TextWrapping = TextWrapping.Wrap, FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
            });
            内容.Children.Add(new TextBlock
            {
                Text = 脚注, FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90)),
            });

            弹窗.Closed += (_, _) => { _开着的提示.Remove(弹窗); if (ReferenceEquals(诊断提示, 弹窗)) 诊断提示 = null; 当前提示诊断 = null; };
            // 鼠标在弹窗里(点"固定"/"✕")就一律不收;离开弹窗才收
            弹窗.Child.MouseEnter += (_, _) => 鼠标在提示里 = true;
            弹窗.Child.MouseLeave += (_, _) => { 鼠标在提示里 = false; 也许收起诊断提示(); };

            _开着的提示.Add(弹窗);
            弹窗.IsOpen = true;
            return 弹窗;
        }

        /// <summary>
        /// 只关**指定那一个**提示(✕ 与"取消固定"用它)。
        /// 它是当前那个就把状态一起清掉;不是(已经被顶掉的旧弹窗)就只关它自己,别影响现在开着的那个。
        /// </summary>
        private void 关某一个提示(Popup 弹窗, bool 用户关的)
        {
            if (ReferenceEquals(诊断提示, 弹窗))
            {
                收起诊断提示(用户关的);
                return;
            }
            try { 弹窗.IsOpen = false; } catch { }
            _开着的提示.Remove(弹窗);
            if (用户关的) 提示开关.记关掉(当前悬停键, DateTime.UtcNow);
        }

        /// <summary>
        /// 鼠标动了/离开了:收提示 —— 但要留点余地:
        /// ① 刚弹出来的 300ms 内不收(弹窗一出现,鼠标事件会抖一下);
        /// ② 鼠标已经挪到提示框上了就不收(人家正要去点"固定");
        /// ③ 其余情况**晚 500ms 再收** —— 提示框在鼠标的右下方,鼠标挪过去要点一下的时候会先"离开编辑器",立即收就永远点不着。
        /// </summary>
        private void 也许收起诊断提示()
        {
            if (诊断提示已固定 || 鼠标在提示里 || 诊断提示?.Child?.IsMouseOver == true) return;
            if ((DateTime.UtcNow - 提示弹出时刻).TotalMilliseconds < 300) return;
            收提示计时器 ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            收提示计时器.Stop();
            收提示计时器.Tick -= 收提示计时器_Tick;
            收提示计时器.Tick += 收提示计时器_Tick;
            收提示计时器.Start();
        }

        private void 收提示计时器_Tick(object? sender, EventArgs e)
        {
            收提示计时器?.Stop();
            if (诊断提示 == null || 诊断提示已固定 || 鼠标在提示里) return;
            if (诊断提示.Child?.IsMouseOver == true) return;
            收起诊断提示();
        }

        /// <summary> 收起提示(没固定的时候由鼠标离开/移动/换诊断触发)</summary>
        public void 收起诊断提示() => 收起诊断提示(用户关的: false);

        /// <summary>
        /// 收起提示。`用户关的` = true 表示这是**用户明确要关**(点 ✕ / 按 Esc / 点气泡外 / 点编辑区),
        /// 这时要记一笔"这一处关过"(`提示开关C`),鼠标不挪开就不再弹 ——
        /// 不然关掉之后鼠标还停在这个词上,悬停计时器会立刻把它重新弹出来(用户报的"关不掉")。
        /// </summary>
        public void 收起诊断提示(bool 用户关的)
        {
            var 键 = 当前悬停键;
            // 先把**所有**开着的弹窗关掉(不依赖"字段还指着它"),再清状态
            var 快照 = _开着的提示.ToList();
            _开着的提示.Clear();
            诊断提示 = null;
            当前提示诊断 = null;
            当前提示词 = null;
            诊断提示已固定 = false;
            鼠标在提示里 = false;
            收提示计时器?.Stop();
            词提示计时器?.Stop();
            foreach (var p in 快照)
            {
                try { p.IsOpen = false; } catch { }
            }
            if (用户关的 && !string.IsNullOrEmpty(键)) 提示开关.记关掉(键, DateTime.UtcNow);
        }

        /// <summary> 把还开着的提示都关掉(切文件/改文本/滚动/失焦/卸载的兜底路径) </summary>
        private void 关掉没固定的提示()
        {
            if (诊断提示已固定) return;
            收起诊断提示(用户关的: false);
        }
        #endregion
        #endregion

    }
}
