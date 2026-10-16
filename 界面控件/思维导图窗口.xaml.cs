using hehshihbeen_compiler.配置;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 思维导图**浮动窗**(H33 立、**H60-14 换过一茬**)。
    ///
    /// 长什么样:
    ///   · 布局 = 左 → 右一层一列(排版是纯函数 `导图布局C.排`,这里只管照搬坐标画出来);
    ///   · 一个框 = 一个节点;点一下 → 往右长出**它的下一级**;
    ///     **H63-2 起画面只画"相关的那些框"**(当前那一格的父链 + 自己 + 直接子;兄弟枝与走过的旁支
    ///     收起来不画 —— 数据仍在树上,勾上「全集图」就看得见);
    ///   · 一层超过 20 个子项时,**先长 20 个 + 一条「…」**,点「…」才把剩下的全长出来;
    ///   · 顶上一排「全集图 / ← 上一个 / 下一个 → / 回到根 / 全部收起」(`全集图` = H63-2 的视野开关,
    ///     其余四个是**前后追溯**,只挪视口与高亮,不动树);
    ///   · 框里三行:粗体标题 / 灰字说明 / 更淡的位置;框右键有「展开全部 / 收起这一枝 / 复制标题」。
    ///
    /// **H60-14 改的三件**(施工图 = `未做任务清单.md` §9 H53 的"施工图 · 步 7"):
    ///   ① **弧线**:连线从折线 `Polyline`(四个点)换成一条三次贝塞尔 `Path` ——
    ///      控制点就取原来那两个折点,起止点不动,观感从"方折"变"鼓起来"(用户口径 ⑦);线宽 1.4 → 1.6;
    ///   ② **双击跳转**:单击照旧展开下一级,**双击**则打开那一份文件并把光标落到那一行
    ///      (节点自己带着 `行` / `文件`,H60-12 排好、这一步直接用;跳不动就写进 `状态行` 说人话);
    ///   ③ **框放大**:三行字号 14 / 12.5 / 11.5、内边距 (16,10,16,10)、圆角 8(用户口径 ⑧;
    ///      框的尺寸仍由 `导图布局C` 按 `量文字宽` 算,这一层只把字号调大)。
    ///
    /// **H61-1(导图形状)在这一层改的两件**(施工图 = `未做任务清单.md` §9 H61 的"施工图 · H61-1"):
    ///   ④ **左侧竖排种类标签** —— 声明项与 结构 / 方法 / 流程块 框左边竖着写一个种类名
    ///      (`TextBlock` + `RotateTransform(-90)`,颜色取 `类刷(种类)`;框宽那一层已经给竖排条留了位置);
    ///      文件框与「声明区 / 调用区」两个区框**不竖排**(口径 ④)。
    ///   ⑤ **单击已展开的框 = 收起**(口径 ②)—— 与"双击跳代码"共用左键,所以单击走一个**短延迟**
    ///      (`单击延迟毫秒`):双击来时把待办的那次单击撤掉,不许"先收起再跳行"。
    ///
    /// **H61-2(导图关系)在这一层改的两件**(施工图 = `未做任务清单.md` §9 H61 的"施工图 · H61-2"):
    ///   ⑥ **红色回连线** —— 一个链节框展开时,主窗告诉它"这条声明就在本文件的哪一格"
    ///      (`回连委托` 给键路),窗口拿 `导图模型C.铺到` 把那一枝**自动展开**、再 `加回连` 记一条线;
    ///      **重画先画树边、再画回连线**(弧线的那条重载 `画一条线` 复用,颜色 = `配色词类C.取导图回连色`);
    ///   ⑦ **收枝不悬空**:`收起` 由模型那一层把落在被收掉节点上的回连线一起清掉;
    ///      窗口每回重画再 `补齐回连线`(声明区收过又展开,只要目标还在树上,红线自己接回来)。
    ///
    /// **第278批(用户复查 H61-3 时给的三条)在这一层改的三件**:
    ///   ⑧ **选中路径绿色**:`_选中` 顺着 `父` 爬到根,那一路的树边**最后画、绿色 + 线宽 2.8**
    ///      (`在选中路上` / `画选中线` / `路径刷`;色 = `配色词类C.取导图路径色`)—— 压在普通树边上面;
    ///   ⑨ **斜体小字那一排**:`节.小字` 非空就在**标题下面**补一行小号斜体淡字
    ///      (H63-7 起**只有文件框的「声明区 / 调用区」**用它写「此为思维导图虚拟概念」——
    ///      那句话对整个导图说一遍就够了,方法 / 结构 / 流程块框的区框不再写);
    ///   ⑩ **框宽收进区间**:那一条改在 `引擎\思维导图C.cs` 的 `导图布局参数C`(最大宽 340 / 最小宽 140),
    ///      这一层只是跟着排版走。
    ///
    /// **H63-1(跟着调用走)在这一层改的一件**:
    ///   ⑪ **补红线改走 `沿路找`** —— `补齐回连线` 原来用 `找节点`(全树按键扫),H63-1 之后
    ///      **同一个键可以在树上出现两次**(本文件的方法既在声明区里有一个框,又在调用链上"跟着调用走"
    ///      挂了一个框)⇒ 全树扫会拿到先建出来的那一个、红线指到另一个枝上;改成
    ///      `导图模型C.沿路找(键路)`(**从根一格一格走**),指的就是那条路的目的地。
    ///      "跟着调用走"那几个框本身由主窗挂(`MainWindow.思维导图.cs` 的 `长链节`),这一层照旧画。
    ///
    /// **H63-2(视野模型 + 全集图开关)在这一层改的三件**:
    ///   ⑫ **视野** —— `重画` 开头按"当前站在哪一格"现算 `导图模型C.算可见(_选中, _全集图)`,
    ///      布局(`导图布局C.排(…, _可见)`)与画框 / 画线 / 画回连线**只认这个集合** ⇒
    ///      **画布尺寸跟着视野走,不再随点越点越大**(用户口径:只展示相关的、无关的不展示;
    ///      父链全留、只收掉兄弟枝。H33 那条"只追加不清除"作废 —— 数据不清除,画面只画相关的);
    ///   ⑬ **顶上「全集图」开关**(`勾全集图` + `全集图_勾了`)—— 勾上 = 旧那一套"长过的全都画"
    ///      (`算可见` 给全集),取消勾选回到视野;**只重画,不动树**;
    ///   ⑭ **状态行报"当前选中那一格"**(H63-7 起;原来报的是追溯步数与一串操作说明)——
    ///      选中哪一格,底下那条就说哪一格(标题 · 种类 · 行号 · 文件),后面挂这一屏的个数;
    ///      那一长串操作说明挪进 `状态行` 的**鼠标提示**(见 `思维导图窗口.xaml`)。
    ///
    /// **H63-3(缩放 / 适应窗口)在这一层改的两件**:
    ///   ⑮ **缩放** —— 画布套一层 `ScaleTransform`,走**布局**那一层(`LayoutTransform`:滚动的范围跟着
    ///      一起缩,滚动条才对得上);三个入口 **`缩放条` 滑条 / Ctrl + 滚轮(`滚动_滚轮了`)/ 「适应窗口」**
    ///      都汇到 `应用缩放()`;比例夹在 `导图缩放C.最小` ~ `最大`(5% ~ 300%)。滚轮缩放时
    ///      **光标底下那一格不跑**(先记画布坐标,换完比例再倒推滚动偏移);裸滚轮照旧只滚动,不抢。
    ///   ⑯ **滚到按比例换算** —— `滚到(节)` 原来是裸坐标,缩过之后会跳偏;现在走
    ///      `导图缩放C.起点偏移` / `居中偏移`(画布坐标 → 滚动偏移) —— 两个纯函数在自检里钉着。
    ///
    /// 上色两族(用户口径 ①;唯一映射表 = `配色词类C.导图映射`):
    ///   · **框**按 `框来源C.种类`(文件 / 结构 / 方法 / 流程块);
    ///   · **子项**按 `分区项C.种类`(变量 / 常量 / 结构 / 方法 / 语句 / 包含…)与调用链节的种类。
    ///   两条线也在这里分开:**声明线画虚线**(`导图节点C.声明边`),**调用线画实线**。
    ///
    /// 数据从哪来:窗口**不认识工程与编译结果** —— 根节点、"取子项"与"跳转"都由主窗口给
    /// (见 `MainWindow.思维导图.cs`),所以引擎那一半是纯的、自检钉得住。
    /// </summary>
    public partial class 思维导图窗口 : Window
    {
        /// <summary> 框里标题那三行的字号(H60-14 放大过;`量文字宽` 也按最大的这一档量)</summary>
        private const double 标题字号 = 14;

        /// <summary> 框左边那条竖排种类标签占多宽(与 `导图布局参数C.竖排宽` 同一个数 —— 两边不一致标题就会被挤)</summary>
        private const double 竖排条宽 = 26;

        /// <summary>
        /// 单击等多久再动手(H61-1 口径 ②):等这么久没等到第二次点击,才按"展开 / 收起"办。
        /// 比系统的双击间隔略短一点 —— 手感上"点一下就展开",双击也来得及把它撤掉。
        /// </summary>
        private const int 单击延迟毫秒 = 260;

        /// <summary> 待办的那一次单击(双击来时撤掉;`收起` 之后不许再动树)</summary>
        private readonly DispatcherTimer _单击定时 = new() { Interval = TimeSpan.FromMilliseconds(单击延迟毫秒) };
        private 导图节点C? _待单击;

        private readonly 导图模型C _模型;
        private readonly 跳转委托 _跳转;
        private readonly 回连委托? _回连;
        private readonly 搜索委托? _搜索;
        private readonly 调用者委托? _调用者;
        private 导图节点C? _选中;

        /// <summary>
        /// **H63-2:顶上那个「全集图」开关**。假(缺省)= **视野**:只画当前那一格的父链 + 自己 + 直接子
        /// (红线那头只到声明处 + 它的父级);真 = 旧那一套"长过的全都画"。
        /// </summary>
        private bool _全集图;

        /// <summary> 这一屏要画哪些框(`重画` 开头按视野现算;`看得见` 就看它) </summary>
        private HashSet<导图节点C>? _可见;

        /// <summary>
        /// **H63-3:当前缩放比例**(1 = 原样)。三个入口都写它:`缩放条`(滑条)、`滚动_滚轮了`(Ctrl + 滚轮)、
        /// `适应窗口_Click`(按视口算)。重画**不重置**它 —— 点开新枝之后还停在你看的那个比例上。
        /// </summary>
        private double _缩放 = 1.0;

        /// <summary>
        /// **H63-3:套在画布上的那个缩放变换**(`LayoutTransform` —— 走布局那一层,滚动的范围跟着一起缩,
        /// 不然滚动条还是按原尺寸算,滚起来对不上)。`滚到` 与"适应窗口"都按这个比例换算。
        /// </summary>
        private readonly ScaleTransform 缩放变换 = new(1, 1);

        private 主题设置? _主题;
        private Brush 面板刷 = Brushes.Transparent;
        private Brush 框面刷 = Brushes.Transparent;      // 面板刷的半透明版(框的底色;第280批)
        private Brush 内容刷 = Brushes.Transparent;
        private Brush 前景刷 = Brushes.WhiteSmoke;
        private Brush 强调刷 = Brushes.SteelBlue;
        private Brush 淡字刷 = Brushes.Gray;

        public 思维导图窗口(Window? 主窗, 导图子项C 根项, 取子项委托 取子项, 跳转委托 跳转, 回连委托? 回连 = null,
            IReadOnlyList<string>? 起路 = null, 搜索委托? 搜索 = null, 调用者委托? 调用者 = null)
        {
            InitializeComponent();
            画布.LayoutTransform = 缩放变换;      // H63-3:缩放走布局这一层(滚动的范围跟着一起缩)
            _模型 = new 导图模型C(根项, 取子项);
            _跳转 = 跳转 ?? ((_, _) => "");
            _回连 = 回连;
            _搜索 = 搜索;
            _调用者 = 调用者;

            取主窗的刷子(主窗);
            if (主窗 != null) Owner = 主窗;
            主题C.主题变化 += 主题变了;
            Closed += (_, _) => { 主题C.主题变化 -= 主题变了; _单击定时.Stop(); };
            _单击定时.Tick += (_, _) => { _单击定时.Stop(); if (_待单击 is { } 节) 展开或收起(节); };

            导图标题.Text = "思维导图 · " + 根项.标题;
            重画();
            // 第281批:**从编辑器按光标行开导图**时给的那条路 —— 等窗口显示出来(有视口了)再铺:
            // 一路展开到光标那一行、选中它、滚过去(口径「光标在哪就从哪起」)。
            if (起路 is { Count: > 0 })
                Loaded += (_, _) =>
                {
                    try
                    {
                        if (_模型.铺到(起路) is not { } 到) return;
                        _选中 = 到;
                        接回连线(到);
                        重画();
                        滚到(到);
                    }
                    catch (Exception 异常) { 异常收集C.记($"思维导图铺到起点失败: {异常.Message}"); }
                };
        }

        /// <summary> 主题刷子从主窗那四个 `主题*刷` 拿(这个窗口自己 Resources 里没有)—— 拿不到就用兜底色 </summary>
        private void 取主窗的刷子(Window? 主窗)
        {
            面板刷 = 取刷(主窗, "主题面板刷", Color.FromRgb(0x2A, 0x2A, 0x2E));
            内容刷 = 取刷(主窗, "主题内容刷", Color.FromRgb(0x1E, 0x1E, 0x22));
            前景刷 = 取刷(主窗, "主题前景刷", Color.FromRgb(0xE6, 0xE6, 0xE6));
            强调刷 = 取刷(主窗, "主题强调刷", Color.FromRgb(0x4E, 0x9A, 0xE8));
            _主题 = 主题C.当前主题;
            var 前景色 = (前景刷 as SolidColorBrush)?.Color ?? Colors.WhiteSmoke;
            淡字刷 = 冻(new SolidColorBrush(Color.FromArgb(0x8C, 前景色.R, 前景色.G, 前景色.B)));
            Resources["主题面板刷"] = 面板刷;
            Resources["主题内容刷"] = 内容刷;
            Resources["主题前景刷"] = 前景刷;
            Resources["主题强调刷"] = 强调刷;
            Background = 内容刷;
            Foreground = 前景刷;
            框面刷 = 半透(面板刷, 框面透明度);
        }

        /// <summary>
        /// 框面**底色的透明度**(第280批:用户「以前的项方框的那种半透明挺不错的,有线穿过去也能看见,
        /// 这个改回去」)—— 0xB4 ≈ 70%:线从框底下穿过去看得见,框里的字照旧实心(不是整框 `Opacity`,
        /// 那样连字一起发灰)。
        /// </summary>
        private const byte 框面透明度 = 0xB4;

        /// <summary> 把一个刷子做成半透明版(不是纯色刷就原样返回)</summary>
        private static Brush 半透(Brush 底, byte 透明度)
            => 底 is SolidColorBrush 实
                ? 冻(new SolidColorBrush(Color.FromArgb(透明度, 实.Color.R, 实.Color.G, 实.Color.B)))
                : 底;

        private static Brush 取刷(Window? 主窗, string 键, Color 兜底色)
        {
            if (主窗?.TryFindResource(键) is Brush 刷) return 刷;
            return 冻(new SolidColorBrush(兜底色));
        }

        private static Brush 冻(SolidColorBrush 刷)
        {
            刷.Freeze();
            return 刷;
        }

        /// <summary> 主题换了:刷子重取一遍 + 重画(窗口自己不留死色值;词类色也跟着主题走)</summary>
        private void 主题变了(主题设置 主题)
        {
            try
            {
                _主题 = 主题;
                var 前景色 = (主题设置.转刷(主题.前景) as SolidColorBrush)?.Color ?? Colors.WhiteSmoke;
                前景刷 = 主题设置.转刷(主题.前景);
                面板刷 = 主题设置.转刷(主题.面板背景);
                内容刷 = 主题设置.转刷(主题.背景);
                强调刷 = 主题设置.转刷(主题.强调色);
                淡字刷 = 冻(new SolidColorBrush(Color.FromArgb(0x8C, 前景色.R, 前景色.G, 前景色.B)));
                Resources["主题面板刷"] = 面板刷;
                Resources["主题内容刷"] = 内容刷;
                Resources["主题前景刷"] = 前景刷;
                Resources["主题强调刷"] = 强调刷;
                Background = 内容刷;
                Foreground = 前景刷;
                框面刷 = 半透(面板刷, 框面透明度);
                重画();
            }
            catch (Exception 异常) { 异常收集C.记($"思维导图换主题失败: {异常.Message}"); }
        }

        /// <summary> **靠主窗右侧生成**(用户要的落点);越界就往里夹 </summary>
        public void 靠右摆(Window? 主窗)
        {
            try
            {
                var 区 = SystemParameters.WorkArea;
                double 宽 = double.IsNaN(Width) || Width < 200 ? 1000 : Width;
                double 左 = 主窗 == null ? 区.Right - 宽 - 24 : 主窗.Left + 主窗.Width - 宽 - 24;
                double 上 = 主窗 == null ? 区.Top + 60 : 主窗.Top + 60;
                Left = Math.Clamp(左, 区.Left, Math.Max(区.Left, 区.Right - 宽));
                Top = Math.Clamp(上, 区.Top, Math.Max(区.Top, 区.Bottom - 200));
            }
            catch { /* 摆不动就用系统的默认落点 */ }
        }

        // ---------------- 画 ----------------

        private double 量文字宽(string 文本)
        {
            if (string.IsNullOrEmpty(文本)) return 0;
            try
            {
                var 文 = new FormattedText(文本, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    标题字号, 前景刷, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                return 文.WidthIncludingTrailingWhitespace;
            }
            catch { return 导图布局C.假量宽(文本); }
        }

        private void 重画()
        {
            try
            {
                画布.Children.Clear();
                补齐回连线();
                // H63-2:先按"当前站在哪一格"算这一屏的**视野**(全集图开关一勾就是全部)——
                // 布局与画都只认它,所以**画布尺寸跟着视野走,不会随点越点越大**
                // (示例06 那种全量视图 1566×4488 只在勾了「全集图」时才出现)。
                _可见 = _模型.算可见(_选中, _全集图);
                var (总宽, 总高) = 导图布局C.排(_模型.根, 量文字宽, null, _可见);
                画布.Width = Math.Max(总宽, 200);
                画布.Height = Math.Max(总高, 120);

                // H61-2 施工图 ②:**先画树边,再画回连线(红色)**,最后才是框
                // 第278批:**选中项 → 根**那一串树边**最后画**(压在普通树边上面)且画成绿色
                // H63-2:看不见的框 / 两头有一条看不见的回连线一律不画
                foreach (var 节 in _模型.节点表)
                    if (看得见(节) && 节.父 != null && !在选中路上(节)) 画连线(节.父, 节);
                foreach (var 节 in _模型.节点表)
                    if (看得见(节) && 节.父 != null && 在选中路上(节)) 画选中线(节.父, 节);
                foreach (var 边 in _模型.回连线)
                    if (看得见(边.从) && 看得见(边.到)) 画回连线(边);
                foreach (var 节 in _模型.节点表)
                    if (看得见(节)) 画框(节);

                // H63-7:**状态行报"当前选中那一格"**(标题 · 种类 · 行号 · 文件),
                // 后面挂这一屏的个数;那一长串操作说明挪进 `状态行` 的鼠标提示(见 思维导图窗口.xaml)。
                状态行.Text = 选中说明() + $" · {(_全集图 ? "全集图" : "视野")} " +
                    $"{_模型.节点表.Count(看得见)} / 已有 {_模型.节点表.Count} 条" +
                    (_模型.回连线.Count > 0 ? $" · 回连线 {_模型.回连线.Count} 条" : "");
            }
            catch (Exception 异常)
            {
                状态行.Text = "画不出来:" + 异常.Message;
                异常收集C.记($"思维导图画不出来: {异常}");
            }
        }

        /// <summary>
        /// **这一格现在画不画**(H63-2 视野):`_可见` 是 `重画` 开头按"当前那一格"现算的集合,
        /// 还没算出来(理论上不会)就按"都画"办 —— 宁可多画一个,不许整张图空白。
        /// </summary>
        private bool 看得见(导图节点C 节) => _可见 == null || _可见.Contains(节);

        /// <summary>
        /// **H63-7:状态行那一句 —— 报的是"当前选中那一格"**(用户口径:状态行改报当前选中那一格,
        /// 操作说明挪进窗口提示)。写法与 `算可见` 同一口径:还没点过任何框时 `_选中` 为空,
        /// **按根报**(第一屏画的就是根那一格),不另编一句"没选中"。
        /// 一行里给:标题 · 种类 · 行号 · 文件名 —— 能省的就省(文件框的标题本来就是文件名,
        /// 说明项本来就没有源码位置),种类那一格优先用竖排条上那个词(与框上写的一致)。
        /// </summary>
        private string 选中说明()
        {
            var 节 = _选中 ?? _模型.根;
            var 行 = new List<string> { $"当前选中:{节.标题}" };
            string 种类 = 节.竖排字.Length > 0 ? 节.竖排字 : 节.种类;
            if (种类.Length > 0 && 种类 != 导图种类C.说明) 行.Add(种类);
            if (节.行 > 0) 行.Add($"第 {节.行} 行");
            else if (节.说明.Length > 0 && 节.说明 != 种类) 行.Add(节.说明);
            string 文件名 = 节.文件.Length > 0 ? System.IO.Path.GetFileName(节.文件) : "";
            if (文件名.Length > 0 && 文件名 != 节.标题) 行.Add(文件名);
            return string.Join(" · ", 行);
        }

        /// <summary>
        /// **弧线**(H60-14 施工图 ③;H61-2 拆成"给定两个框画一条线"的重载,回连线复用它):
        /// 起止点还是"父框右边中点 → 子框左边中点",中间那一段是一条三次贝塞尔 —— 控制点就取原来那两个折点
        /// (`((起X+到X)/2, 起Y)` 与 `((起X+到X)/2, 到Y)`),所以"方折"变"鼓起来"。
        /// **声明线画虚线、调用线画实线**(口径 ②):`节.声明边` 为真就挂 `StrokeDashArray`。
        /// </summary>
        private void 画连线(导图节点C 父, 导图节点C 子)
            => 画一条线(父.X + 父.宽, 父.Y + 父.高 / 2, 子.X, 子.Y + 子.高 / 2,
                类刷(子.种类, 连线: true), 虚线: 子.声明边);

        /// <summary>
        /// 这一格**是不是"选中项 → 根"那一串上的**(第278批;用户复查时的第 3 条:
        /// 「被选中项到根节点的所有连接线均加绿色高亮显示」)。
        /// 判法 = 从 `_选中` 顺着 `父` 一路往上爬,爬到的每一格都算;没选中 / 不在这一枝 ⇒ 假。
        /// 按**节点引用**比(不按键):树上同一个键只会有一个节点实例。
        /// </summary>
        private bool 在选中路上(导图节点C 节)
        {
            for (var 走 = _选中; 走 != null; 走 = 走.父)
                if (ReferenceEquals(走, 节)) return true;
            return false;
        }

        /// <summary>
        /// 选中路径那一条树边:**绿色 + 加粗**(2.8)。除了颜色与粗细,几何与 `画连线` 逐字相同
        /// (同一段弧、声明边照旧虚线)—— 换的是"这一枝是我现在站的地方",不是形状。
        /// </summary>
        private void 画选中线(导图节点C 父, 导图节点C 子)
            => 画一条线(父.X + 父.宽, 父.Y + 父.高 / 2, 子.X, 子.Y + 子.高 / 2,
                路径刷(), 虚线: 子.声明边, 线宽: 2.8);

        /// <summary> 选中路径那条绿色(亮 / 暗两套;与 15 个词类、与回连线那条红都分开算) </summary>
        private Brush 路径刷()
        {
            bool 暗 = _主题?.是暗主题 ?? false;
            var 色 = (主题设置.转刷(配色词类C.取导图路径色(暗)) as SolidColorBrush)?.Color ?? Colors.Green;
            return 冻(new SolidColorBrush(色));
        }

        /// <summary>
        /// **一条三次贝塞尔**(树边与回连线共用这一个口子):给两个端点、一支笔、要不要虚线。
        /// 控制点取两端的中点(老折线那两个折点),所以怎么看都是"鼓起来"的弧。
        /// </summary>
        private void 画一条线(double 起X, double 起Y, double 到X, double 到Y, Brush 刷, bool 虚线, double 线宽 = 1.6)
        {
            double 中X = (起X + 到X) / 2;

            var 图 = new PathFigure { StartPoint = new Point(起X, 起Y), IsFilled = false, IsClosed = false };
            图.Segments.Add(new BezierSegment(new Point(中X, 起Y), new Point(中X, 到Y), new Point(到X, 到Y), true));
            var 几何 = new PathGeometry();
            几何.Figures.Add(图);

            var 线 = new Path
            {
                Stroke = 刷,
                StrokeThickness = 线宽,
                Data = 几何,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            if (虚线) 线.StrokeDashArray = [4, 3];
            画布.Children.Add(线);
        }

        /// <summary>
        /// **红色回连线**(H61-2 口径 ⑤):从一个框**指回**声明区那一项,两头取**相对的那两条边**
        /// (目标在左边就都从左边出、进目标的右边),所以线是往回兜的一个弧,不会与树边挤在一块。
        /// 颜色 = `配色词类C.取导图回连色`(亮/暗两套),线型**红实线**(施工图:先按红实线做)。
        /// </summary>
        private void 画回连线(回连线C 边)
            => 画一条线(回连端起(边), 回连端Y(边.从), 回连端到(边), 回连端Y(边.到), 回连刷(), 虚线: false, 线宽: 2.0);

        private static double 回连端起(回连线C 边) => 到左边(边) ? 边.从.X : 边.从.X + 边.从.宽;
        private static double 回连端到(回连线C 边) => 到左边(边) ? 边.到.X + 边.到.宽 : 边.到.X;
        private static double 回连端Y(导图节点C 节) => 节.Y + 节.高 / 2;

        /// <summary> 目标是不是在出发框的**左边**(按框的水平中点比;声明区一般排在左边的列上) </summary>
        private static bool 到左边(回连线C 边)
            => 边.到.X + 边.到.宽 / 2 < 边.从.X + 边.从.宽 / 2;

        /// <summary> 回连线那条红色(亮 / 暗两套;与 15 个词类分开算 —— 它画的不是代码里的字) </summary>
        private Brush 回连刷()
        {
            bool 暗 = _主题?.是暗主题 ?? false;
            var 色 = (主题设置.转刷(配色词类C.取导图回连色(暗)) as SolidColorBrush)?.Color ?? Colors.IndianRed;
            return 冻(new SolidColorBrush(色));
        }

        /// <summary>
        /// **这个框展开之后要不要拉一条回连线**(H61-2):问主窗要一条**从文件框到声明项**的键路,
        /// 拿 `导图模型C.铺到` 把目标那一枝**自动展开**(口径 ⑤),再记一条红线。
        /// 没有(跨文件 / 内建 / 不是链节框)就什么都不做 —— 那种照旧走"声明处"小节点。
        /// </summary>
        private void 接回连线(导图节点C 节)
        {
            if (_回连 == null) return;
            try
            {
                // 第280批(用户报「很多变量调用没有回调线」):这一格**自己 + 刚长出来的每一格**都算。
                // 根因:红线原来只在"那一格自己被展开"时才拉 —— 方法框一点开,`第一个(data)` 里的 `data`
                // 这类变量子项冒出来了,可谁也不会再逐个去点它们 ⇒ 它们一条红线都没有。
                // 现在父节点一长,子项里凡是**本文件声明的**当场各拉一条(口径 ⑤"目标没展开就先自动展开"照旧)。
                var 两批 = new List<导图节点C> { 节 };
                两批.AddRange(节.子);
                foreach (var 一格 in 两批)
                {
                    var 路 = _回连(一格);
                    if (路 != null && 路.Count > 0)
                    {
                        var 到 = _模型.铺到(路);
                        if (到 != null) _模型.加回连(一格, 到);
                        continue;
                    }
                    // 第281批:声明在**别的文件**里(多文件工程)/ 导图这一层算不出键路的,
                    // 它下面那个「→ 声明处」小节点就是它的声明 —— 也拉一条红线(用户「变量要有回调线」)。
                    // 只认**有源码行**的:内建 / 关键字 / 指令那档(行 0)照旧不画假线。
                    foreach (var 子 in 一格.子)
                        if (子.附加 is 声明处C { 是源码声明: true, 行: > 0 })
                            _模型.加回连(一格, 子);
                }
            }
            catch (Exception 异常) { 异常收集C.记($"思维导图接回连线失败: {异常.Message}"); }
        }

        /// <summary>
        /// 重画之前把**已经在树上的**声明项接回来(H61-2 ③):声明区收过又展开、或者目标是被别的枝
        /// 展开出来的 —— 只要那条键路的目标还在树上,红线自己就接回去,不会"掉了就再也不回来"。
        /// 这一趟**不展开**(展开那件事归 `接回连线`),所以重画永远是廉价的。
        ///
        /// **H63-1**:找目标改成 `导图模型C.沿路找`(**从根按路走**),不再用 `找节点`(全树按键扫)——
        /// 跟着调用走之后**同一个键可以在树上出现两次**(本文件的方法既在声明区里有一个框,
        /// 又在调用链上挂了一个框),全树扫会拿到先建出来的那一个,红线就指到另一个枝上去了。
        /// </summary>
        private void 补齐回连线()
        {
            if (_回连 == null) return;
            foreach (var 节 in _模型.节点表.ToList())
            {
                if (节.父 == null) continue;
                try
                {
                    var 路 = _回连(节);
                    if (路 == null || 路.Count == 0) continue;
                    if (_模型.沿路找(路) is { } 到) _模型.加回连(节, 到);
                }
                catch { /* 一条算不出来不影响别的 */ }
            }
        }

        /// <summary>
        /// 一个种类一种颜色 —— **唯一映射表是 `配色词类C.导图映射`**(H60-13 定,施工图 · 步 6 ①):
        /// 有对应词类的走主题里那一档色(选项页里改过就跟着变),没有的(文件 / 流程块 / 语句 / 包含)
        /// 走 `配色词类C.取导图中性色`。界面自己那几样(工程 / 说明 / 更多 / 区)另有兜底。
        /// </summary>
        private Brush 类刷(string 种类, bool 连线 = false)
        {
            var 前景色 = (前景刷 as SolidColorBrush)?.Color ?? Colors.WhiteSmoke;
            var 强调色 = (强调刷 as SolidColorBrush)?.Color ?? Colors.SteelBlue;
            string 词类 = 配色词类C.导图词类(种类);
            Color 色;
            if (词类.Length > 0) 色 = 取词类色(词类);
            else if (种类 == 导图种类C.工程) 色 = 强调色;
            else if (种类 == 导图种类C.说明) 色 = Color.FromArgb(0x70, 前景色.R, 前景色.G, 前景色.B);
            else if (种类 == 导图种类C.更多) 色 = Color.FromArgb(0x88, 前景色.R, 前景色.G, 前景色.B);
            else 色 = 取中性色();
            if (连线) 色 = Color.FromArgb(0x60, 色.R, 色.G, 色.B);
            return 冻(new SolidColorBrush(色));
        }

        /// <summary> 一个词类在当前主题下的颜色(主题那一层已经把"显式 → 老属性 → 亮/暗默认"三级兜底做完)</summary>
        private Color 取词类色(string 词类键)
        {
            try
            {
                if (_主题 != null) return (主题设置.转刷(_主题.取词类色(词类键)) as SolidColorBrush)?.Color
                    ?? Colors.WhiteSmoke;
            }
            catch { /* 主题坏了就走默认色 */ }
            return (主题设置.转刷(配色词类C.取默认色(词类键, 用暗主题: false)) as SolidColorBrush)?.Color
                ?? Colors.WhiteSmoke;
        }

        /// <summary> 导图自己的中性色(没有对应词类的那几样;与 15 个词类分开算)</summary>
        private Color 取中性色()
        {
            bool 暗 = _主题?.是暗主题 ?? false;
            return (主题设置.转刷(配色词类C.取导图中性色(暗)) as SolidColorBrush)?.Color ?? Colors.Gray;
        }

        private void 画框(导图节点C 节)
        {
            bool 是区 = 节.种类 == 导图种类C.区;
            bool 是说明 = 节.种类 == 导图种类C.说明;
            var 内 = new StackPanel();
            内.Children.Add(new TextBlock
            {
                Text = 节.标题.Length == 0 ? "(无名)" : 节.标题,
                FontWeight = FontWeights.SemiBold,
                FontSize = 标题字号,
                Foreground = 是说明 ? 淡字刷 : 前景刷,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (节.小字.Length > 0)
                内.Children.Add(new TextBlock
                {
                    // 第278批:**斜体小字**紧贴标题下面(声明区 / 调用区那排"此为思维导图虚拟概念")
                    Text = 节.小字,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Foreground = 淡字刷,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            if (节.说明.Length > 0)
                内.Children.Add(new TextBlock
                {
                    Text = 节.说明,
                    FontSize = 12.5,
                    Foreground = 淡字刷,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            if (节.提示.Length > 0)
                内.Children.Add(new TextBlock
                {
                    Text = 节.提示,
                    FontSize = 11.5,
                    Foreground = 淡字刷,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

            bool 选中 = ReferenceEquals(节, _选中);
            var 框 = new Border
            {
                Width = 节.宽,
                Height = 节.高,
                Background = 框面刷,       // 半透(第280批):线从框底下穿过去看得见
                BorderBrush = 选中 ? 强调刷 : 类刷(节.种类),
                BorderThickness = new Thickness(选中 ? 2.4 : 是区 ? 1.6 : 1),
                CornerRadius = new CornerRadius(是区 ? 10 : 8),
                Padding = new Thickness(16, 10, 16, 10),
                Child = 加竖排条(节, 内),
                Tag = 节,
                Cursor = 能点的(节) ? Cursors.Hand : Cursors.Arrow,
                Opacity = 能点的(节) ? 1.0 : 0.8,
                ToolTip = 提示语(节),
            };
            Canvas.SetLeft(框, 节.X);
            Canvas.SetTop(框, 节.Y);
            框.PreviewMouseLeftButtonDown += 框被按下了;
            框.MouseRightButtonUp += 框被右键了;
            画布.Children.Add(框);
        }

        /// <summary>
        /// **左侧竖排种类标签**(H61-1 口径 ④):声明项与 结构 / 方法 / 流程块 框在框左边竖着写一个种类名
        /// (`RotateTransform(-90)` 是**布局变换**,所以那一列的高度按文字长度算,不会溢到框外);
        /// 竖排条右边一条细线分栏,颜色取 `类刷(种类)`。文件框与「区」框不竖排,原样返回内容。
        /// 宽度两边同一个数:`这里是 竖排条宽`,`框宽` 那边是 `导图布局参数C.竖排宽`。
        /// </summary>
        private FrameworkElement 加竖排条(导图节点C 节, StackPanel 内)
        {
            if (!节.竖排 || 节.竖排字.Length == 0) return 内;
            var 刷 = 类刷(节.种类);
            var 字 = new TextBlock
            {
                Text = 节.竖排字,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = 刷,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                LayoutTransform = new RotateTransform(-90),
            };
            var 条 = new Border
            {
                Width = 竖排条宽,
                BorderBrush = 刷,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = 字,
            };
            var 格 = new Grid();
            格.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(竖排条宽) });
            格.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(条, 0);
            Grid.SetColumn(内, 1);
            格.Children.Add(条);
            格.Children.Add(内);
            return 格;
        }

        /// <summary> 点得动吗(能往下长 / 能跳行都算;"…"是"全部生成"那一条;说明项不算) </summary>
        private static bool 能点的(导图节点C 节) =>
            节.种类 == 导图种类C.更多
            || (节.种类 != 导图种类C.说明 && (节.能往下长 || 节.行 > 0));

        private static string 提示语(导图节点C 节)
        {
            var 行 = new List<string> { 节.标题 };
            if (节.说明.Length > 0) 行.Add(节.说明);
            if (节.提示.Length > 0) 行.Add(节.提示);
            // 第279批:只进提示、不画在框上的那一行(文件框 / 工程根的全路径就在这儿)
            if (节.悬停.Length > 0) 行.Add(节.悬停);
            if (节.行 > 0)
                行.Add($"双击:跳到 {(节.文件.Length > 0 ? System.IO.Path.GetFileName(节.文件) : "代码")} 第 {节.行} 行");
            else if (节.种类 != 导图种类C.区 && 节.种类 != 导图种类C.说明 && 节.种类 != 导图种类C.更多)
                行.Add("这一项没有源码位置(没有源码定义),双击没有可跳的地方");
            行.Add(节.种类 == 导图种类C.更多 ? "点它把剩下的全部生成"
                : 节.种类 == 导图种类C.区 ? (节.已展开 ? "点一下收起这一边" : "点一下展开这一边")
                : 节.还能长 ? $"还有 {节.总数 - 节.已生成} 条没生成(右键「展开全部子项」全要)"
                : 节.已展开 ? "这一层已经生成完了(点一下收起这一枝)"
                : 节.能往下长 ? "点一下,往右生成它的下一级"
                : "到底了,没有下一级");
            return string.Join("\n", 行);
        }

        // ---------------- 交互 ----------------

        /// <summary>
        /// **左键按下**(H60-14 的双击跳代码 + H61-1 的"单击已展开 = 收起"共用这一个口子):
        ///   · `ClickCount == 2` ⇒ 把待办的那次单击**撤掉**,直接跳代码(不许先收起再跳行);
        ///   · `ClickCount == 1` ⇒ 起一个短延迟(`单击延迟毫秒`),等不到第二次点击才真展开 / 收起。
        /// 跳转本身交给主窗那条现成的"打开文件并跳到行"(`跳转委托`),跳不动就把人话写进 `状态行`。
        /// </summary>
        private void 框被按下了(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not 导图节点C 节) return;
            if (e.ClickCount == 2)
            {
                _单击定时.Stop();
                _待单击 = null;
                e.Handled = true;
                try
                {
                    string 说话 = _跳转(节.文件, 节.行);
                    状态行.Text = 说话.Length > 0
                        ? 说话
                        : $"已跳到 {(节.文件.Length > 0 ? System.IO.Path.GetFileName(节.文件) : "代码")} 第 {节.行} 行:{节.标题}";
                }
                catch (Exception 异常) { 状态行.Text = "跳转失败:" + 异常.Message; }
                return;
            }
            if (e.ClickCount != 1 || !能点的(节)) return;
            _待单击 = 节;
            _单击定时.Stop();
            _单击定时.Start();
        }

        /// <summary>
        /// 那一次单击真到了(H61-1 口径 ②③,施工图 ③):
        /// **已经展开的框点一下 = 收起**(右键「展开全部子项」/「收起这一枝」照旧);
        /// 没展开的照旧 `展开一层`;"…"那一条走"全部生成"(`展开全部` 自己幂等,收完还能再点)。
        /// 收起 / 展开之后**必须重画**(布局与连线都跟着变),再把视口滚到那个框上。
        /// </summary>
        private void 展开或收起(导图节点C 节)
        {
            try
            {
                if (节.种类 == 导图种类C.更多)
                {
                    var 父 = 节.父;
                    if (父 != null) { _模型.展开全部(父); _选中 = 父; 接回连线(父); 重画(); 滚到(父); }
                    return;
                }
                _选中 = 节;
                if (节.已展开 && 节.子.Count > 0) _模型.收起(节);
                else if (_模型.展开一层(节)) 接回连线(节);   // H61-2:展开成功才看要不要拉红线
                重画();
                滚到(节);
            }
            catch (Exception 异常) { 状态行.Text = "展开失败:" + 异常.Message; }
        }

        private void 框被右键了(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not 导图节点C 节) return;
            e.Handled = true;
            _选中 = 节;
            重画();
            var 菜单 = new ContextMenu();

            var 全要 = new MenuItem { Header = "展开全部子项", IsEnabled = 节.还能长 && 节.能往下长 };
            全要.Click += (_, _) => { _模型.展开全部(节); 接回连线(节); 重画(); 滚到(节); };
            菜单.Items.Add(全要);

            var 收 = new MenuItem { Header = "收起这一枝", IsEnabled = 节.子.Count > 0 };
            收.Click += (_, _) => { _模型.收起(节); 重画(); 滚到(节); };
            菜单.Items.Add(收);

            菜单.Items.Add(new Separator());

            // H63-5:方法框右键多这一格 —— 在**这一格下面**列出"谁调用了它"(整个工程的调用边;
            // 每一条双击就跳到那一行)。数据是当场问 IDE 要的,挂上去排在这一层最前面(见 `导图模型C.挂子项`)。
            var 看谁 = new MenuItem
            {
                Header = "看谁调用它",
                IsEnabled = 节.附加 is 框来源C { 种类: 导图分区C.种类方法 },
                ToolTip = "在下面列出调用这个方法的地方(文件 + 行;整个工程,含 `包含` 进来的库),双击一条跳到那一行",
            };
            看谁.Click += (_, _) => 看谁调用它(节);
            菜单.Items.Add(看谁);

            菜单.Items.Add(new Separator());
            var 跳 = new MenuItem { Header = "跳到代码", IsEnabled = 节.行 > 0 && 节.文件.Length > 0 };
            跳.Click += (_, _) =>
            {
                try
                {
                    string 说话 = _跳转(节.文件, 节.行);
                    状态行.Text = 说话.Length > 0 ? 说话 : $"已跳到 {System.IO.Path.GetFileName(节.文件)} 第 {节.行} 行";
                }
                catch (Exception 异常) { 状态行.Text = "跳转失败:" + 异常.Message; }
            };
            菜单.Items.Add(跳);

            var 拷 = new MenuItem { Header = "复制标题" };
            拷.Click += (_, _) => { try { Clipboard.SetText(节.标题); } catch { /* 剪贴板被别人占着就算了 */ } };
            菜单.Items.Add(拷);

            菜单.PlacementTarget = sender as UIElement;
            菜单.IsOpen = true;
        }

        private void 后退_Click(object sender, RoutedEventArgs e) => 走一步(_模型.后退());
        private void 前进_Click(object sender, RoutedEventArgs e) => 走一步(_模型.前进());
        private void 回到根_Click(object sender, RoutedEventArgs e) => 走一步(_模型.根);

        private void 走一步(导图节点C? 节)
        {
            _选中 = 节;
            重画();
            if (节 != null) 滚到(节);
        }

        private void 收起_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _模型.收起(_模型.根);
                _选中 = _模型.根;
                重画();
                滚到(_模型.根);
            }
            catch (Exception 异常) { 状态行.Text = "收起失败:" + 异常.Message; }
        }

        // ---------------- 搜索定位(H63-4) ----------------

        private void 搜索框_按键(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;                       // 别让回车冒到别处去
            执行搜索();
        }

        private void 搜索_Click(object sender, RoutedEventArgs e) => 执行搜索();

        /// <summary>
        /// **打一个名字 → 铺到它声明的那个框并选中**(H63-4;用户 2026-09-26 点名的"搜索定位")。
        ///
        /// 窗口自己不查任何东西:把名字交给 `搜索委托`(主窗拿**现成的 `声明索引C`** 查,
        /// 再用**同一个键路拼法** `声明键路`),拿回 `搜索结果C` 照办 ——
        /// `键路` 有值就 `铺到` + 选中 + 滚过去(与 H61-2 拉红线走的是同一条路);
        /// 声明在别的文件时主窗已经**换了一扇窗**;都没有就把那句人话写进状态行,**不硬铺**。
        /// </summary>
        private void 执行搜索()
        {
            try
            {
                string 名字 = 搜索框.Text.Trim();
                if (名字.Length == 0) { 状态行.Text = "先在那一格打个名字,再按回车。"; return; }
                var 果 = _搜索?.Invoke(名字);
                if (果 == null) { 状态行.Text = "这一扇窗没有接上搜索。"; return; }
                if (果.键路 is { Count: > 0 } && 果.另开文件.Length == 0 && _模型.铺到(果.键路) is { } 到)
                {
                    _选中 = 到;
                    接回连线(到);
                    重画();
                    滚到(到);
                }
                状态行.Text = 果.说明.Length > 0 ? 果.说明 : $"没找到「{名字}」。";
            }
            catch (Exception 异常) { 状态行.Text = "搜索失败:" + 异常.Message; }
        }

        // ---------------- 反向查(谁调用它,H63-5) ----------------

        /// <summary>
        /// **方法框右键「看谁调用它」**(H63-5):问 IDE 要一组"调用点"(整个工程的调用边,含 `包含` 进来的库),
        /// 挂到**这一格下面**(`导图模型C.挂子项`)再重画 —— 每一条自己带 `文件` / `行`,
        /// **双击照旧跳那一行**(与其它框一个手势)。一条都没查到 / 这一格不是方法框 / 还没编过,
        /// 都如实写进状态行(`调用者结果C.说明`)。
        /// </summary>
        private void 看谁调用它(导图节点C 节)
        {
            try
            {
                if (_调用者 == null) { 状态行.Text = "这一扇窗没有接上调用边表。"; return; }
                var 果 = _调用者(节);
                if (果 == null) { 状态行.Text = "这一扇窗没有接上调用边表。"; return; }
                int 挂上 = _模型.挂子项(节, 果.项们);
                if (挂上 > 0)
                {
                    _选中 = 节;
                    重画();
                    滚到(节);
                }
                状态行.Text = 果.说明.Length > 0
                    ? 果.说明
                    : (挂上 > 0 ? $"挂上 {挂上} 条调用点。" : "没有查到调用它的地方。");
            }
            catch (Exception 异常) { 状态行.Text = "查调用者失败:" + 异常.Message; }
        }

        /// <summary>
        /// **H63-2 顶上那个「全集图」开关**:切"视野 / 全集图"两档,只重画一遍(**不动树**)——
        /// 取消勾选就回到视野:当前那一格还是原来那一格,长出来的枝叶照旧在树上,只是不画了
        /// (想留着它就得留着树;要真丢掉走「全部收起」)。
        /// </summary>
        private void 全集图_勾了(object sender, RoutedEventArgs e)
        {
            try
            {
                _全集图 = 勾全集图.IsChecked == true;
                重画();
            }
            catch (Exception 异常) { 状态行.Text = "切换全集图失败:" + 异常.Message; }
        }

        private void 滚到(导图节点C 节)
        {
            try
            {
                滚动.UpdateLayout();
                // H63-3:滚动偏移按**缩放比例**换算(画布坐标 → 屏幕坐标),不然缩过之后"滚到那一格"会跳偏
                滚动.ScrollToHorizontalOffset(导图缩放C.起点偏移(节.X - 40, _缩放));
                滚动.ScrollToVerticalOffset(导图缩放C.居中偏移(节.Y, 节.高, 滚动.ViewportHeight, _缩放));
            }
            catch { /* 还没布局出来就算了 */ }
        }

        // ---------------- 缩放(H63-3) ----------------

        /// <summary>
        /// 把 `_缩放` 真应用下去:变换 + 百分比 + 滑条跟着走。三个入口(滑条 / Ctrl 滚轮 / 适应窗口)都汇到这一处,
        /// 免得三处各写一遍、改一处忘一处。`导图缩放C.夹比例` 兜住上下限(5% ~ 300%)。
        /// </summary>
        private void 应用缩放()
        {
            _缩放 = 导图缩放C.夹比例(_缩放);
            缩放变换.ScaleX = _缩放;
            缩放变换.ScaleY = _缩放;
            if (缩放标签 != null) 缩放标签.Text = $"{_缩放 * 100:F0}%";
            // 滚轮 / 适应窗口改的比例,滑条跟着走(设成同一个数不会再触发一次 —— 下面那一处有护栏)
            if (缩放条 != null && Math.Abs(缩放条.Value - _缩放) > 1e-4) 缩放条.Value = _缩放;
        }

        /// <summary> 滑条动了:改比例(自己给自己设值那一趟被上面那句护栏挡掉,不会打转)</summary>
        private void 缩放条_变了(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            try
            {
                if (Math.Abs(_缩放 - 缩放条.Value) < 1e-4) return;
                _缩放 = 缩放条.Value;
                应用缩放();
            }
            catch (Exception 异常) { 异常收集C.记($"思维导图缩放失败: {异常.Message}"); }
        }

        /// <summary>
        /// **Ctrl + 滚轮缩放**(H63-3):裸滚轮照旧交给 `ScrollViewer` 滚动 —— 图上滚动比缩放常用得多,
        /// 不能抢。缩放时**光标底下那一格不许跑**:先记住它在画布坐标里的位置,换完比例再把滚动偏移倒推回去。
        /// </summary>
        private void 滚动_滚轮了(object sender, MouseWheelEventArgs e)
        {
            try
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;   // 没按 Ctrl:交给滚动
                double 旧 = _缩放;
                double 新比 = 导图缩放C.夹比例(旧 * (e.Delta > 0 ? 1.1 : 1 / 1.1));
                if (Math.Abs(新比 - 旧) < 1e-6) return;                        // 已经顶到上 / 下限了
                var 光标在画布 = e.GetPosition(画布);
                var 光标在视口 = e.GetPosition(滚动);
                _缩放 = 新比;
                应用缩放();
                滚动.UpdateLayout();
                滚动.ScrollToHorizontalOffset(Math.Max(0, 光标在画布.X * _缩放 - 光标在视口.X));
                滚动.ScrollToVerticalOffset(Math.Max(0, 光标在画布.Y * _缩放 - 光标在视口.Y));
                e.Handled = true;
            }
            catch (Exception 异常) { 异常收集C.记($"思维导图滚轮缩放失败: {异常.Message}"); }
        }

        /// <summary>
        /// **「适应窗口」**(H63-3):按当前视口算一个比例,让**整张图都进窗口**(四周留一点边),
        /// 再回到左上角。图很大时字会很小 —— 这一档就是"看全貌",要看清再用滑条 / Ctrl 滚轮放大。
        /// 比例算在 `导图缩放C.适应比例`(纯函数,自检里钉着)。
        /// </summary>
        private void 适应窗口_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                滚动.UpdateLayout();
                _缩放 = 导图缩放C.适应比例(画布.Width, 画布.Height, 滚动.ViewportWidth, 滚动.ViewportHeight);
                应用缩放();
                滚动.UpdateLayout();
                滚动.ScrollToHorizontalOffset(0);
                滚动.ScrollToVerticalOffset(0);
                状态行.Text = $"已按视口缩放到 {_缩放 * 100:F0}%(整张图都在窗口里了;Ctrl + 滚轮 / 拖滑条能再放大)";
            }
            catch (Exception 异常) { 状态行.Text = "适应窗口失败:" + 异常.Message; }
        }
    }
}
