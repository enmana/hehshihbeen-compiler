using hehshihbeen_compiler.选卡控件;
using hehshihbeen_compiler.配置;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace hehshihbeen_compiler
{
    /// <summary> 主窗口的一部分:视觉树辅助 / 窗口生命周期 / 标题栏 / 文件读存 / 标签页 / 编辑命令 </summary>
    public partial class MainWindow : Window
    {

        #region 视觉树辅助

        private static T? 查找视觉子级<T>(DependencyObject? 根) where T : DependencyObject
        {
            if (根 == null) return null;
            for (int 下标 = 0; 下标 < VisualTreeHelper.GetChildrenCount(根); 下标++)
            {
                var 子 = VisualTreeHelper.GetChild(根, 下标);
                if (子 is T 目标) return 目标;
                var 更深 = 查找视觉子级<T>(子);
                if (更深 != null) return 更深;
            }
            return null;
        }

        /// <summary> 可视树里**所有** T(不只第一个);找"每个标签页里的编辑器"用它 </summary>
        private static IEnumerable<T> 查找所有视觉子级<T>(DependencyObject? 根) where T : DependencyObject
        {
            if (根 == null) yield break;
            for (int 下标 = 0; 下标 < VisualTreeHelper.GetChildrenCount(根); 下标++)
            {
                var 子 = VisualTreeHelper.GetChild(根, 下标);
                if (子 is T 目标) yield return 目标;
                foreach (var 更深 in 查找所有视觉子级<T>(子)) yield return 更深;
            }
        }

        private static void 收集视觉子级<T>(DependencyObject? 根, List<T> 收集表) where T : DependencyObject
        {
            if (根 == null) return;
            for (int 下标 = 0; 下标 < VisualTreeHelper.GetChildrenCount(根); 下标++)
            {
                var 子 = VisualTreeHelper.GetChild(根, 下标);
                if (子 is T 匹配) 收集表.Add(匹配);
                收集视觉子级(子, 收集表);
            }
        }

        #endregion

        #region 构造函数与窗口生命周期

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                初始化解决方案();
                刷新快捷键显示();                      // C7:菜单右边那串键位提示按(可能改过的)快捷键表来
                // (C7 起 Ctrl+Shift+F 由窗口那一层的快捷键表统一分发,见 MainWindow.xaml.cs 的 快捷键_预览)
                建片段菜单();                          // C5:编辑 → 插入代码片段(条目按 片段表C 生成)
                上下文 = new()
                {
                    打开文件到标签页 = 打开文件到标签页,
                    请新建项目 = () => 新建项目_Click(this, new RoutedEventArgs()),
                    请打开项目 = () => 打开项目_Click(this, new RoutedEventArgs()),
                    请打开文件 = () => 打开文件_Click(this, new RoutedEventArgs()),
                    请打开工程 = 路径 => 打开项目文件(路径),
                    建项目 = 新建项目,
                };
                // H23:全局(静态)事件一律从"订阅 / 退订"这一对方法进出 —— 关窗时退得掉
                // T28:编辑器那 8 个静态委托(断点点击 / 右键各条)也从这儿进 —— 以前它们是在构造函数里
                // 直接赋值的,关窗(甚至用户点"取消")都不会退订 ⇒ 9 处 static 引用仍指着已关窗口。
                订阅全局事件();
                // R3-6-4 批 4:打包方式下拉恢复上次那一档(存本机,不进 .hbp)。
                // 认不出来 / 没存过 → 缺省档(有 C 编译器就"原生 C",没有就"自带运行时·单文件")。
                恢复打包方式下拉();
                代码选卡.SelectionChanged += (_, _) => 安排检查();   // 换个文件看:也重查一遍
                代码选卡.SelectionChanged += 选卡变了;               // P36:换页之前先把光标位置记下来
                _上次选卡 = 当前选卡;
                // 阶段 D:程序 OUT/NEWLINE 宿主回调 → 输出框 + 控制台页;IN 从控制台页/资源文件来(见 跑一遍)。
                // **H39-2 起这几个回调不再挂在这儿**:跑程序那台虚拟机住在子进程里,由
                // `MainWindow.跑程序通道.cs` 的 `接上跑程序回调(会话机: …)` 在**建机器那一刻**挂上
                // (口径与原来那四行一字不差 —— 输出目标 / 输入源 / 等输入 / 宿主调用;
                //  H38-2 起参数只说"哪一台",机器问契约要)。
                // 立即恢复上次主题(不能放 Loaded,可能被后续流程覆盖)
                主题C.应用(主题C.加载当前主题名());
                Loaded += (sender, e) =>
                {
                    订阅命令联动();          // 状态变化 → 菜单/按钮联动
                    跑一遍检查();            // 开着的文件先查一遍(不用等你敲字才看到下划线)
                    起运行监视();            // H100:最底下那条运行监视条(执行中实时看 PC / 条数 / CPU / 子进程内存)
                    // H92-1:弹窗那道闸的勾选状态跟着 `选项.json` 走(默认不勾),顺带把本进程那一份摆齐 ——
                    // 单步那条腿在**本进程**里跑,它读的就是这一份(`宿主弹窗C.能弹`)。
                    允许弹窗菜单.IsChecked = 允许弹窗;
                    宿主弹窗C.设允许弹窗(允许弹窗);
                };
                // 藏主窗口要等"内容渲染完"再藏:在 Loaded 里 Hide() 太早,屏幕后面会留一块黑框。
                // 先最小化再隐藏,出来的时侯再还原成 Normal(见 显示启动页 的 Closed)。
                ContentRendered += (sender, e) =>
                {
                    if (_已藏过主窗口) return;      // 启动页关掉后主窗口会重新渲染,这里只做一次
                    _已藏过主窗口 = true;
                    WindowState = WindowState.Minimized;
                    Hide();
                    显示启动页();
                };
                Closing += (sender, e) =>
                {
                    退出单步();      // 单步中的调试视图先还原,别把"展开后的汇编"当成未保存改动来问
                    // P36:关窗之前先把"现在这一屏"记进会话(标签 / 光标 / 未保存正文 / 窗口四格)。
                    // 放在这一行(提问之前):用户就算在下面的提问里点"取消",也不过是**多记了一次**;
                    // 反过来放最后的话,"取消"那一路会白记一遍、而后面的收口又不该做。
                    记住离页光标();
                    记会话();
                    // 逐个检查未保存的标签页；用户选择"取消"则中止关闭
                    var 全部页 = 主视图模型.选卡页视图模型表.Where(页 => !页.是特殊页).ToList();
                    foreach (var 页 in 全部页)
                    {
                        if (!提示保存未保存更改(页)) { e.Cancel = true; return; }
                    }
                    保存树展开状态();        // 有项目才记录(没项目就不动 JSON)
                    // H23:关窗收口 —— 先立旗(队列里已经排上的回调进来也一律走人),再停表,最后退订全局事件。
                    // 这一段必须留在"上面那些提问都过了"之后:用户点"取消",窗口还开着,收口一步都不能做。
                    _已关窗 = true;
                    停掉窗口计时器();
                    退订全局事件();
                };
            }
            catch (Exception e)
            {
                throw new Exception(e.Message);
            }

        }

        #endregion

        #region 全局订阅与关窗收口(H23)

        /// <summary> 主窗口是不是已经关掉:关窗之后计时器回调 / 全局事件一律不许再动界面 </summary>
        private bool _已关窗;

        /// <summary>
        /// T28:`编辑器.切换断点被请求` / `编辑器.删除全部断点被请求` 这两条**必须用 lambda** 才能拿到
        /// `this`(它们要问"当前编辑器"'、"要清的是这个窗口的断点"),而 lambda **退不掉** ——
        /// 所以在字段里各留一份**同一个实例**的引用,退订时拿它去 `-=`。
        /// </summary>
        private Action? _切换断点请求;
        private Action? _删除全部断点请求;

        /// <summary>
        /// 订阅那些**静态**事件与静态委托(它们活得比窗口久,不集中成对管就没法退订)。
        /// 每一处都用**具名方法**(lambda 退不掉;非用不可的两个见
        /// `_切换断点请求` / `_删除全部断点请求` 两个字段)。
        ///
        /// T28(2026-09-27 评测整改):以前这儿只有 4 个事件,**编辑器那 8 个静态委托是在构造函数里
        /// 直接赋值的** —— 关窗退不掉 ⇒ 9 处 static 引用仍指着已关窗口,整个窗口对象图 + 编辑器控件树
        /// 回收不掉。现在两块一起进退(共 12 处,逐一成对)。
        /// </summary>
        private void 订阅全局事件()
        {
            异常收集C.追加 += 记异常框;             // 全应用内异常统一进"异常"页签,不再弹 MessageBox
            主题C.主题变化 += 应用主题;
            编辑器.新造出来 += 编辑器_新造出来;      // 新开的编辑器:文本一变就安排检查
            选项页.选项已应用 += 全局选项已应用;

            // ---- T28:编辑器那 8 个静态委托(鼠标点行号 / 右键菜单各条) ----
            编辑器.断点被点击 = 断点行被点击;      // 鼠标点行号左边就能切;静态挂一次,所有编辑器实例都管用
            编辑器.重命名被请求 = 执行重命名;       // 编辑器右键菜单里的"重命名"(同一套静态挂法)
            编辑器.转到定义被请求 = 执行转到定义;   // P22:右键 → 转到定义(F12)
            编辑器.查找引用被请求 = 执行查找引用;   // P22:右键 → 查找引用(Shift+F12)
            编辑器.思维导图被请求 = 从编辑器打开思维导图;   // H33:右键 → 打开思维导图
            编辑器.切换断点被请求 = _切换断点请求 ??= () => 断点行被点击(当前编辑器?.取当前行号() ?? 0);
            编辑器.断点条件被请求 = 执行断点条件;   // P24:右键 → 断点条件…(和调试菜单那条同一份实现)
            编辑器.删除全部断点被请求 = _删除全部断点请求 ??= () => 菜单清除所有断点_Click(this, new RoutedEventArgs());   // C23
            编辑器.加监视被请求 = 执行加监视;        // P35:右键 → 加监视表达式…(和调试菜单那条同一份实现)
            编辑器.取名表 = 取补全名字表;           // C19:补全按"最近一次编译结果"算作用域、公私有与成员
        }

        /// <summary> 与 `订阅全局事件` 一一对应(同一个具名方法才退得掉)</summary>
        private void 退订全局事件()
        {
            异常收集C.追加 -= 记异常框;
            主题C.主题变化 -= 应用主题;
            编辑器.新造出来 -= 编辑器_新造出来;
            选项页.选项已应用 -= 全局选项已应用;

            // ---- T28:8 个静态委托一起退(换过 lambda 的两个用字段里那份同一个实例) ----
            编辑器.断点被点击 = null;
            编辑器.重命名被请求 = null;
            编辑器.转到定义被请求 = null;
            编辑器.查找引用被请求 = null;
            编辑器.思维导图被请求 = null;
            编辑器.切换断点被请求 = null;
            编辑器.断点条件被请求 = null;
            编辑器.删除全部断点被请求 = null;
            编辑器.加监视被请求 = null;
            编辑器.取名表 = null;
        }

        /// <summary> 选项页按了"应用":开着的编辑器跟着换一套选项</summary>
        private void 全局选项已应用(选项设置 _)
        {
            应用全部编辑器选项();
            刷新快捷键显示();
        }

        /// <summary>
        /// H23:关窗时把主窗口自己起的计时器全停掉 —— 不停的话它们还挂在调度队列上,
        /// 窗口都没了还可能进来动界面。
        /// (复核稿说的"两个"是当时单文件里的两个;现在拆成四份文件,主窗口起的一共三个。)
        /// </summary>
        private void 停掉窗口计时器()
        {
            _检查计时器?.Stop();
            _标签点击计时器?.Stop();
            _输出节流?.Stop();
        }

        #endregion

        #region 窗口:标题栏按钮与尺寸

        /// <summary> 最小化 </summary>
        private void 最小化按钮_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        /// <summary> 最大化 / 还原(顺带换图标与提示) </summary>
        private void 最大化还原按钮_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                最大化还原按钮.Content = "\uE922";   // 最大化图标
                最大化还原按钮.ToolTip = "最大化";
            }
            else
            {
                WindowState = WindowState.Maximized;
                最大化还原按钮.Content = "\uE923";   // 还原图标
                最大化还原按钮.ToolTip = "还原";
            }
        }

        private void 关闭按钮_Click(object sender, RoutedEventArgs e) => Close();

        /// <summary> 关闭按钮悬停变红(提示"这是关窗") </summary>
        private void 关闭按钮_鼠标进入(object sender, MouseEventArgs 事件)
            => (sender as Button)!.Background = Brushes.Red;

        private void 关闭按钮_鼠标离开(object sender, MouseEventArgs 事件)
            => (sender as Button)!.Background = Brushes.Transparent;

        #endregion

        #region 文件:读入 / 保存(新建与打开见"项目与文件"区域,只有那一套)

        /// <summary> 普通源码页的筛选器(`.hb` 排第一 ⇒ 保存对话框默认还是它) </summary>
        private const string 文件筛选 = "hb文件|*.hb|汇编源码|*.hba|所有文件|*.*";

        /// <summary> C20:汇编源码页(`.hba`)的筛选器 —— 后缀排第一,免得另存为时手滑存成 `.hb` </summary>
        private const string 汇编文件筛选 = "汇编源码|*.hba|hb文件|*.hb|所有文件|*.*";

        /// <summary>
        /// 把文件读成标签页。HB 二进制(0x48 0x42 开头)直接交给虚拟机跑;其余按 UTF-8 文本打开。
        /// `临时` = H61-3 的预览页那一档(只有导图双击跳转传真;二进制那一支不看它 —— 那是"跑一遍",不开页)。
        /// </summary>
        private bool 读入文件内容(string 全称, out string 文件名, bool 临时 = false)
        {
            文件名 = Path.GetFileName(全称);

            try
            {
                using FileStream 文件流 = File.OpenRead(全称);
                long 文件长度 = 文件流.Length;

                if (文件长度 == 0)
                {
                    更新标签页内容(文件名, "", 全称, 临时);
                    return true;
                }

                if (文件长度 < 4)   // 太短,不可能是 HB 头,直接当文本
                {
                    byte[] 短字节 = new byte[文件长度];
                    文件流.ReadExactly(短字节, 0, (int)文件长度);
                    更新标签页内容(文件名, 解码文本(短字节), 全称, 临时);
                    return true;
                }

                byte[] 文件头 = new byte[4];
                文件流.ReadExactly(文件头, 0, 4);

                if (源码文件C.是机器码头(文件头))   // 前两字节 "HB" → 二进制程序(判据只有一处:`源码文件C`)
                {
                    long 负载长度 = 文件长度 - 4;
                    if (负载长度 > int.MaxValue)
                        throw new InvalidOperationException("文件过大，无法一次加载。");
                    byte[] 负载 = new byte[负载长度];
                    文件流.ReadExactly(负载, 0, (int)负载长度);
                    前端契约C.服务.调试.机器.运行(负载);
                    return false;   // 已由虚拟机执行,不再开标签页
                }

                // 普通文本:回到开头整份读进来
                文件流.Seek(0, SeekOrigin.Begin);
                if (文件长度 > int.MaxValue)
                    throw new InvalidOperationException("文本文件过大，无法一次加载。");
                byte[] 全部字节 = new byte[文件长度];
                文件流.ReadExactly(全部字节, 0, (int)文件长度);
                更新标签页内容(文件名, 解码文本(全部字节), 全称, 临时);
                return true;
            }
            catch (Exception 异常) when (异常 is IOException || 异常 is UnauthorizedAccessException)
            {
                string 原因 = string.IsNullOrEmpty(Path.GetFileName(全称)) ? "路径不完整，缺少文件名" : 异常.Message;
                异常收集C.记($"读取文件时发生错误: {原因}");
                文件名 = "";
                return false;
            }
            catch (编译异常C 错)
            {
                // T16(2026-09-27 评测整改):不是 UTF-8 的文件(中文 Windows 上存成 GBK 很常见)以前按
                // `Encoding.UTF8` 松解——非法字节全变成 U+FFFD,编辑器里看着是一屏问号,还照样能按保存写回去。
                // 现在给一条明确的编码提示,并把"打开"这个动作判为没成功(不开一页乱码)。
                异常收集C.记($"这个文件打不开:{错.中文}");
                文件名 = "";
                return false;
            }
        }

        /// <summary>
        /// 把文件内容载入标签页:开一页新的,记下路径与"已保存"快照。
        ///
        /// H30:这里原先还有一条"当前有一个没用过的空白页就复用它"的分支 —— 那条路**已经走不到**了:
        /// 会造空白页的只有 `主视图模型.添加()` 的无参重载(= 那个从没绑过的 `AddTabCommand`),
        /// 而"没打开文件时编辑器区就是空的、不摆空标签页"是定下来的设计。本批连那个无参重载一起撤了,
        /// 于是这里只剩一条路(开新页)。
        ///
        /// H61-3:`临时` 为真时走 `主视图模型.换临时页`(预览页 —— 整页顶掉上一个预览页,位置不变);
        /// 两条路下面的收尾一模一样(立载入旗 / 记已保存快照 / 按后缀定页种类)。
        /// </summary>
        private 选卡页视图模型 更新标签页内容(string 文件名, string 内容, string 路径, bool 临时 = false)
        {
            选卡页视图模型 页;
            if (临时) 页 = 主视图模型.换临时页(文件名, 内容 ?? "", 路径);
            else
            {
                主视图模型.添加(文件名, 内容 ?? "", 路径);
                页 = 主视图模型.选中页 ?? throw new NullReferenceException("刚添加的标签页没有成为选中页");
            }
            页.标记载入(内容);
            // C29:这一趟**先有内容、后立旗**是对新页而言的 —— 新页的编辑器这会儿还没造出来,
            // 绑定要等编辑器造出来才把它推过去,所以旗子留得住,不会被当成"用户把整篇编辑过"。
            页.标记已保存(内容 ?? "");
            // C20:按落点后缀定页种类(打开 `.hba` 走汇编源码页那一档:后缀/筛选器不同,也不参与编译)
            页.页种类 = 汇编源码C.判种类(路径);
            return 页;
        }

        /// <summary>
        /// 文本解码统一走这里(与写出时的 清洗文本 对称)。
        /// `Encoding.UTF8.GetString` **不会**摘 BOM(那份 `\uFEFF` 会原样进字符串),所以由 `清洗文本` 摘掉 ——
        /// 编辑器里看不到那个字符;写回时 `宿主与文件服务C.写回` 按磁盘上原来有没有 BOM 再补回去(C26)。
        /// </summary>
        /// <summary> 文本文件解码:**严格 UTF-8**(带 BOM 就摘掉),不是 UTF-8 就抛带提示的编译异常(T16);
        /// 解出来的正文再走与写盘同一套清洗(只摘不可见字符,末尾空行不动)。 </summary>
        private static string 解码文本(byte[] 字节) => 分词器C.清洗文本(前端契约C.服务.宿主.解源文件(字节));

        private void 保存_Click(object sender, RoutedEventArgs e)
        {
            if (当前选卡 is not { } 页) return;
            保存标签页(页, 强制另存为: false);
        }

        private void 另存为_Click(object sender, RoutedEventArgs e)
        {
            if (当前选卡 is not { } 页) return;
            保存标签页(页, 强制另存为: true);
        }

        /// <summary> 保存指定标签页;无路径或强制另存为时弹保存对话框。返回是否写入成功。 </summary>
        private bool 保存标签页(选卡页视图模型 页, bool 强制另存为)
        {
            string? 路径 = string.IsNullOrWhiteSpace(页.全称) ? null : 页.全称;
            if (强制另存为 || 路径 == null)
            {
                // C20:汇编源码页另存为时后缀排第一(`.hba`),普通页还是 `.hb` 排第一
                bool 汇编页 = 页.是汇编源码页;
                SaveFileDialog 保存对话框 = new()
                {
                    Filter = 汇编页 ? 汇编文件筛选 : 文件筛选,
                    InitialDirectory = 取初始目录().Length > 0 ? 取初始目录() : AppDomain.CurrentDomain.BaseDirectory,
                    Title = 汇编页 ? "保存汇编源码" : "保存文件",
                    FileName = 建议文件名(页),
                    DefaultExt = 汇编页 ? "hba" : "hb",
                    AddExtension = true,
                };
                if (保存对话框.ShowDialog() != true) return false;
                路径 = 保存对话框.FileName;
            }

            try
            {
                // C26:落盘走全仓唯一口径(保留原文件有没有 BOM)—— 以前这里的 `Encoding.UTF8` **带 BOM**,
                // "打开 → 什么不改按 Ctrl+S"就把没 BOM 的 .hb 写成带 BOM 的(git diff 上整个文件都"变了")。
                前端契约C.服务.宿主.写回(路径, 取磁盘文本(页.内容));
            }
            catch (Exception 异常) when (异常 is IOException || 异常 is UnauthorizedAccessException)
            {
                异常收集C.记($"保存文件失败: {异常.Message}");
                return false;
            }

            页.全称 = 路径;
            页.标题 = Path.GetFileName(路径);
            页.标记已保存(页.内容 ?? "");
            刷新命令可用性();
            return true;
        }

        private static string 建议文件名(选卡页视图模型 页)
        {
            if (!string.IsNullOrWhiteSpace(页.全称)) return Path.GetFileName(页.全称)!;
            if (!string.IsNullOrWhiteSpace(页.标题)) return 页.标题!;
            return 页.是汇编源码页 ? "未命名" + 汇编源码C.扩展名 : "未命名.hb";
        }

        /// <summary>
        /// 写出前清理文本(与读入用同一套清洗:只摘不可见字符,**末尾空行不动**)。
        /// C26 起编码头由 `宿主与文件服务C.写回` 按"原文件有没有 BOM"决定,这里不再碰 BOM 那件事。
        /// </summary>
        private static string 取磁盘文本(string? 文本) => 分词器C.清洗文本(文本);

        private static string 取初始目录()
        {
            string 目录 = 选项.当前.默认文件夹;
            return string.IsNullOrWhiteSpace(目录) ? "" : 目录;
        }

        #endregion

        #region 标签页:关闭 / 未保存提示 / 标题重命名

        /// <summary> 标签头上的 ✕ </summary>
        private void 标签关闭按钮_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: 选卡页视图模型 页 })
                尝试关闭标签页(页);
        }

        /// <summary>
        /// H61-3:**双击标签头(标题文字以外那一圈)= 把预览页固定成普通页**。
        /// 双击**标题文字**那条路是"改文件名",它自己会 `e.Handled = true`,冒泡到不了这里
        /// (`MainWindow.xaml` 的 `标签头样式` 上挂的 EventSetter)。
        /// 固定之后这一页就与普通打开的一模一样(页头不再斜体,关页照旧问未保存)。
        /// </summary>
        private void 标签头_双击(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: 选卡页视图模型 页 } || !页.是临时页) return;
            e.Handled = true;
            页.是临时页 = false;
        }

        /// <summary> 有未保存修改就问"保存/不保存/取消";返回 false = 用户取消,流程中止 </summary>
        private bool 提示保存未保存更改(选卡页视图模型 页)
        {
            if (!页.有未保存更改) return true;
            var 结果 = MessageBox.Show(
                $"“{显示名称(页)}”有未保存的修改，是否保存？",
                "未保存的更改",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (结果 == MessageBoxResult.Yes) return 保存标签页(页, 强制另存为: false);
            if (结果 == MessageBoxResult.No) return true;      // 放弃修改
            return false;                                      // 取消
        }

        /// <summary> 关一个标签页:未保存先问。单步中先退出单步(把调试视图还原回源码,免得把展开文本存出去) </summary>
        private bool 尝试关闭标签页(选卡页视图模型 页)
        {
            if (正在单步) 退出单步();
            if (!提示保存未保存更改(页)) return false;
            主视图模型.选卡页视图模型表.Remove(页);
            刷新命令可用性();
            return true;
        }

        private static string 显示名称(选卡页视图模型 页)
        {
            if (!string.IsNullOrWhiteSpace(页.标题)) return 页.标题!;
            if (!string.IsNullOrWhiteSpace(页.全称)) return Path.GetFileName(页.全称)!;
            return "未命名";
        }

        // ---- 标题就地编辑(双击标签标题 → 改文件名) ----
        private bool _正在提交标题;

        private void 标题框_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            _标签点击计时器?.Stop();
            if (sender is not TextBox 标题框 || !标题框.IsReadOnly) return;
            e.Handled = true;
            标题框.Cursor = Cursors.IBeam;
            标题框.Tag = 标题框.Text;          // 备份,取消时还原
            标题框.IsReadOnly = false;
            标题框.CaretIndex = 标题框.Text?.Length ?? 0;
            标题框.Focus();
            标题框.SelectAll();
        }

        /// <summary> 单击 = 切到该页(用 100ms 计时器把单击与双击区分开) </summary>
        private void 标题框_模拟选卡页标题单击(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 1) return;
            _标签点击计时器?.Stop();
            _标签点击计时器 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _标签点击计时器.Tick += (_, _) =>
            {
                if (_已关窗) return;          // H23:关窗之后不碰界面(队列里排着的那一拍也不放进来)
                _标签点击计时器.Stop();
                if (sender is TextBox { DataContext: 选卡页视图模型 所在页 }) 主视图模型.选中页 = 所在页;
            };
            _标签点击计时器.Start();
        }

        private void 标题框_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox 标题框 || 标题框.IsReadOnly) return;
            if (e.Key == Key.Enter) { e.Handled = true; 提交标题修改(标题框); }
            else if (e.Key == Key.Escape) { e.Handled = true; 取消标题修改(标题框); }
        }

        private void 标题框_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox 标题框 || 标题框.IsReadOnly) return;
            if (_正在提交标题) return;          // 提交过程中弹框导致失焦,避免重入
            提交标题修改(标题框);
            标题框.Cursor = Cursors.Arrow;
        }

        private void 提交标题修改(TextBox 标题框)
        {
            if (_正在提交标题) return;
            if (标题框.DataContext is not 选卡页视图模型 页) { 恢复标题为只读(标题框); return; }

            _正在提交标题 = true;
            try
            {
                // C21:页头那一格显示的是 `显示标题`(有未保存改动时带 ` *`),所以"没改"要按**显示的那串字**比;
                // 真改名只把 `标题` 传下去(星号不是文件名的一部分)。
                string 原显示 = 页.显示标题;
                string 新显示 = 标题框.Text?.Trim() ?? "";
                if (新显示.Length == 0)
                {
                    MessageBox.Show("文件名不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    标题框.Text = 原显示;
                }
                else if (新显示 != 原显示 && !重命名标签(页, 新显示))
                {
                    标题框.Text = 原显示;            // 没接受 → 还原显示
                }
            }
            finally
            {
                _正在提交标题 = false;
                恢复标题为只读(标题框);
            }
        }

        private static void 取消标题修改(TextBox 标题框)
        {
            if (标题框.DataContext is 选卡页视图模型 页) 标题框.Text = 页.显示标题;
            恢复标题为只读(标题框);
        }

        private static void 恢复标题为只读(TextBox 标题框)
        {
            标题框.IsReadOnly = true;
            if (标题框.IsKeyboardFocusWithin) 标题框.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        }

        /// <summary>
        /// 改标题并同步磁盘文件名(没有关联文件时只改标题)。
        /// C6:真改名时**顺手把工程文件条目与别的文件里的 `包含 "…"` 一起改对** ——
        /// 不然改完名工程文件里还挂着旧名字(下一次编译就"找不到文件"),别的文件里的 包含 也断了。
        /// 先把连带的改动**预览一遍 + 问一次**,确认了才动磁盘。
        /// </summary>
        private bool 重命名标签(选卡页视图模型 页, string 新标题)
        {
            string? 旧路径 = string.IsNullOrWhiteSpace(页.全称) ? null : 页.全称;

            if (旧路径 != null && File.Exists(旧路径))
            {
                if (Path.GetFileName(新标题) != 新标题)
                {
                    MessageBox.Show("文件名不能包含路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                if (新标题.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    MessageBox.Show("文件名包含非法字符。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                string 扩展名 = Path.GetExtension(旧路径);
                string 新文件名 = 新标题;
                if (Path.GetExtension(新文件名).Length == 0 && 扩展名.Length > 0) 新文件名 += 扩展名;
                string 新路径 = Path.Combine(Path.GetDirectoryName(旧路径) ?? "", 新文件名);

                if (!string.Equals(新路径, 旧路径, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(新路径))
                    {
                        MessageBox.Show($"已存在同名文件：“{新文件名}”。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    if (!执行文件改名(旧路径, 新路径)) return false;
                    页.全称 = 新路径;
                }
                页.标题 = Path.GetFileName(新路径);      // 有关联文件:显示最终文件名(含扩展名)
                return true;
            }

            页.标题 = 新标题;                              // 没有关联文件:只改标题
            return true;
        }

        /// <summary>
        /// C6:磁盘上改名 + 把 `.hbp` 条目与别的文件里的 `包含 "…"` 一起改对(引擎 `工程维护C.计划`)。
        /// 顺序:**先算计划 → 预览 + 问一次 → 磁盘改名 → 再落盘那些连带改动**;
        /// 新名已存在、计划有问题、或者用户点了取消,都一个字符都不动。
        /// 落盘走既有路子:打开着的文件推给标签页 `内容` 再 `保存标签页`(编码/BOM 口径统一),没打开的与 `.hbp` 直接写。
        /// </summary>
        private bool 执行文件改名(string 旧全路径, string 新全路径)
        {
            try
            {
                var 项目 = 解决方案.找所属项目(旧全路径) ?? 解决方案.启动项目;
                var 源码表 = 项目 != null ? 取当前源码(项目) : [];
                var 工程表 = new List<(string 文件, string 内容)>();
                foreach (var 一个 in 解决方案.项目表)
                    if (一个.工程路径 is { } 工程路径 && File.Exists(工程路径))
                        工程表.Add((工程路径, File.ReadAllText(工程路径)));

                var 计划 = 工程维护C.计划(旧全路径, 新全路径, 源码表, 工程表);
                if (!计划.可改)
                {
                    替换提示("这个文件不能这样改名:" + string.Join(" / ", 计划.问题.Select(x => x.说明)));
                    return false;
                }
                if (计划.处数 > 0)
                {
                    输出框.AppendText(计划.预览() + "\n");
                    切到底部输出();
                    if (MessageBox.Show(
                            $"改名的时候要不要把这些地方也一起改对?\n\n{计划.摘要()}\n\n" +
                            "工程文件里的条目、别的文件里的 `包含 \"…\"` 都会跟着改成新名字;" +
                            "确认后**当场落盘**(这些连带改动不留在编辑器里等 Ctrl+S)。",
                            "改名(连带改动)", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                    {
                        输出框.AppendText("已取消改名(一个字符都没改)。\n");
                        return false;
                    }
                }

                try { File.Move(旧全路径, 新全路径); }
                catch (Exception 移动异常)
                {
                    替换提示($"磁盘改名失败:{移动异常.Message}");
                    return false;
                }

                int 存了 = 0;
                if (计划.处数 > 0)
                {
                    var 表 = new List<(string 名称, string 内容)>(源码表);
                    foreach (var 项 in 工程表)
                        if (!表.Any(x => string.Equals(x.名称, 项.文件, StringComparison.OrdinalIgnoreCase))) 表.Add((项.文件, 项.内容));
                    foreach (var (名称, 内容) in 计划.套用(表))
                    {
                        string 原 = 表.First(x => string.Equals(x.名称, 名称, StringComparison.OrdinalIgnoreCase)).内容;
                        if (string.Equals(原, 内容, StringComparison.Ordinal)) continue;
                        // 被改名那个文件自己的内容(它 包含 别人):写到**新路径**上
                        if (string.Equals(名称, 旧全路径, StringComparison.OrdinalIgnoreCase)) { 工程维护C.写回(新全路径, 内容); 存了++; continue; }
                        if (名称.EndsWith(工程文件C.扩展名, StringComparison.OrdinalIgnoreCase)) { 工程维护C.写回(名称, 内容); 存了++; continue; }
                        var 页 = 找标签页(名称);
                        if (页 != null) { 页.内容 = 内容; if (保存标签页(页, 强制另存为: false)) 存了++; }
                        else { 工程维护C.写回(名称, 内容); 存了++; }
                    }
                    输出框.AppendText($"改名的同时改对了 {存了} 个文件(工程条目 {计划.工程处数} 处 / 包含 {计划.源码处数} 处)。\n");
                }
                else if (工程表.Count > 0)
                {
                    输出框.AppendText(计划.提示.Count > 0 ? "(" + string.Join(";", 计划.提示) + ")\n" : "");
                }

                foreach (var 一个 in 解决方案.项目表) 一个.刷新文件();     // 工程文件条目变了:项目树跟着刷
                项目树.UpdateLayout();
                刷新命令可用性();
                安排检查();                                            // 400ms 后重编一遍,问题页跟着更新
                return true;
            }
            catch (Exception 异常)
            {
                异常收集C.记($"文件改名失败: {异常.Message}");
                记异常框($"文件改名失败: {异常}");
                替换提示($"文件改名失败:{异常.Message}");
                return false;
            }
        }

        #endregion

        #region 编辑命令(可用性由 刷新命令可用性() 统一开关)

        private void 菜单关闭标签_Click(object sender, RoutedEventArgs e)
        {
            if (当前选卡 is { } 页) 尝试关闭标签页(页);
        }

        private void 菜单复制_Click(object sender, RoutedEventArgs e)
        {
            if (当前编辑器 is { } 编) 编.执行复制();
        }

        private void 菜单粘贴_Click(object sender, RoutedEventArgs e)
        {
            if (当前编辑器 is { } 编) 编.执行粘贴();
        }

        private void 菜单删除_Click(object sender, RoutedEventArgs e)
        {
            if (当前编辑器 is { } 编) 编.执行删除();
        }

        private void 菜单全选_Click(object sender, RoutedEventArgs e)
        {
            if (当前编辑器 is { } 编) 编.执行全选();
        }

        private void 菜单语义对齐_Click(object sender, RoutedEventArgs e)
        {
            if (当前编辑器 is { } 编)
            {
                编.语义对齐();
                编译框.Text = "已按语义列对齐(只调整空白)。\n";
            }
        }

        #endregion

        #region 编辑:查找 / 替换(P21;引擎侧只算"编辑清单",见 `引擎\查找替换C.cs`)

        /// <summary> H5:上次「替换」勾没勾「按正则」(同一个会话里连做几次替换不用每次重勾) </summary>
        private bool _替换按正则;

        /// <summary> 打开编辑器自带的查找面板(Ctrl+F 也是它:找 + 高亮 + F3 下一个)</summary>
        private void 查找_Click(object sender, RoutedEventArgs e)
        {
            if (当前编辑器 is { } 编) 编.打开查找面板();
            else 替换提示("现在没有打开的文件。先打开一个 .hb 文件再查找。");
        }

        /// <summary>
        /// **当前文件**的查找/替换:① 问要找什么、换成什么(空 = 删掉)② `查找替换C` 算清单
        /// (默认不区分大小写 + **保护注释与字符串**)③ 预览写进输出框、弹一次确认 ④ 把新文本推给标签页的 `内容`
        /// (走依赖属性:编辑器同步、页面标脏;**不自动存盘**,要存按 Ctrl+S)。
        /// 撤销:整篇替换在编辑器里算**一次**撤销单元(Ctrl+Z 一按回退整次替换 —— 与重命名同一个口径)。
        /// </summary>
        private void 替换当前文件_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var 编 = 当前编辑器;
                var 页 = 当前选卡;
                if (编 == null || 页 == null) { 替换提示("现在没有打开的文件。先打开一个 .hb 文件再替换。"); return; }

                bool 按正则 = _替换按正则;
                string? 找 = 输入窗口.问带勾选(this, "替换(当前文件)", "要查找的内容:", "",
                    "只在**当前文件**里替换;注释与字符串里的同样文字不会被改(要连注释一起改,用命令行 hbcc --查找 … --连注释一起)。\n" +
                    "勾上「按正则」= 要找的内容**按正则解释**(H5;与编辑器 Ctrl+F 面板同一套口径),换成里可以写 $1 表示分组。",
                    "按正则(与 Ctrl+F 面板同一套)", ref 按正则);
                if (找 == null) return;
                _替换按正则 = 按正则;          // 下一次打开还记着这一档
                if (找.Length == 0) { 替换提示("要找的内容是空的 —— 写点东西再替换。"); return; }
                string? 换 = 输入窗口.问(this, "替换(当前文件)", $"把 `{找}` 换成:", "",
                    按正则
                        ? "留空 = 删掉这些文字;按正则时这里可以写 $1 / $2(= 这一处命中的第 1 / 第 2 个分组)、$$(= 一个 $)。按确定后先给改动预览。"
                        : "留空 = 删掉这些文字;按确定后先给改动预览,再问一次才动手。");
                if (换 == null) return;

                string 找说法 = 按正则 ? $"正则 `{找}`" : $"`{找}`";
                var 设置 = new 查找设置C { 正则 = 按正则 };
                var 表 = new List<(string 名称, string 内容)> { (页.全称 ?? "", 页.内容 ?? "") };
                var 清单 = 查找替换C.替换清单(找, 换, 表, 设置);
                if (清单.问题.Count > 0) { 替换提示(清单.预览()); return; }
                if (清单.处数 == 0)
                {
                    替换提示($"这个文件里没有 {找说法} 可以替换的地方(注释与字符串里的不算)。");
                    return;
                }

                输出框.AppendText(清单.预览(带行: true) + "\n");
                切到底部输出();
                if (MessageBox.Show(
                        $"要在**当前文件**里把 {找说法} 换成 `{换}`(留空 = 删掉)吗?共 {清单.处数} 处。\n" +
                        $"口径:{设置}。\n确认后只改编辑器里的文本(不自动存盘,Ctrl+S 才落盘)。",
                        "替换(当前文件)", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                {
                    输出框.AppendText("已取消替换(一个字符都没改)。\n");
                    return;
                }

                页.内容 = 清单.应用(表)[0].内容;       // 依赖属性:编辑器同步 + 页面标脏
                输出框.AppendText($"已在当前文件替换 {清单.处数} 处(没自动存盘;Ctrl+Z 可撤销这一整次替换)。\n");
            }
            catch (Exception 异常)
            {
                异常收集C.记($"替换(当前文件)失败: {异常.Message}");
                记异常框($"替换(当前文件)失败: {异常}");
            }
        }

        /// <summary>
        /// C5:编辑 → 格式化当前文件(Ctrl+Shift+F)。引擎 `代码排版C` 按**块深度**重排缩进 + 修行尾空白,
        /// **只动空白** —— 注释与字符串里的字符一个不碰(所以编译结果逐字节不变,自检里钉着)。
        /// 与命令行 `hbcc --排版` 同一套引擎:先预览(输出页)+ 问一次,确认后推给标签页的 `内容`
        /// (走依赖属性:编辑器同步、页面标脏;**不自动存盘**,Ctrl+S 才落盘)。
        /// </summary>
        private void 格式化_Click(object sender, RoutedEventArgs e) => 执行格式化();

        private void 执行格式化()
        {
            try
            {
                var 编 = 当前编辑器;
                var 页 = 当前选卡;
                if (编 == null || 页 == null) { 替换提示("现在没有打开的文件。先打开一个 .hb 文件再格式化。"); return; }

                var 设置 = 编辑器.当前排版设置();
                var 结果 = 代码排版C.排版(页.内容 ?? "", 设置);
                if (结果.改了几行 == 0)
                {
                    替换提示("这个文件已经排好了(一行都不用改)。");
                    return;
                }

                输出框.AppendText(结果.预览(12) + $"\n口径:{设置}\n");
                切到底部输出();
                if (MessageBox.Show(
                        $"要按块深度重排这个文件的缩进吗?共 {结果.改了几行} 行。\n" +
                        $"只动前导空白与行尾空白:注释与字符串里的字符一个不碰,编译结果逐字节不变。\n" +
                        $"确认后只改编辑器里的文本(不自动存盘,Ctrl+S 才落盘;Ctrl+Z 可撤销这一整次)。",
                        "格式化当前文件", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                {
                    输出框.AppendText("已取消格式化(一个字符都没改)。\n");
                    return;
                }

                页.内容 = 结果.文本;
                输出框.AppendText($"已格式化:{结果.改了几行} 行(没自动存盘;Ctrl+Z 可撤销这一整次)。\n");
            }
            catch (Exception 异常)
            {
                异常收集C.记($"格式化失败: {异常.Message}");
                记异常框($"格式化失败: {异常}");
            }
        }

        /// <summary> 编辑 → 插入代码片段:条目**按 `片段表C.全部` 生成**(单一事实源,加一条片段这里自动多一项)</summary>
        private void 建片段菜单()
        {
            try
            {
                片段菜单.Items.Clear();
                foreach (var 项 in 片段表C.全部)
                {
                    var 条 = new MenuItem { Header = $"{项.标题}({项.触发})", ToolTip = 项.说明 };
                    // 第169批(bug 3):现场生成的这几条**明写一次**子菜单样式 —— 暗色那一套是
                    // `MainWindow.xaml` 里的**隐式样式**(按 WPF 的规矩,加进树里的现场生成项照理也吃得到),
                    // 可用户报的正是这一个菜单"灰底白字",索性两条腿都站住:显式的这份在,样式一定上身。
                    if (TryFindResource(typeof(MenuItem)) is Style 菜单项样式) 条.Style = 菜单项样式;
                    var 定项 = 项;
                    条.Click += (_, _) => 当前编辑器?.插入片段(定项);
                    片段菜单.Items.Add(条);
                }
            }
            catch (Exception 异常) { 异常收集C.记($"建『插入代码片段』菜单失败: {异常.Message}"); }
        }

        /// <summary>
        /// **全工程**替换:与『编辑→重命名』同一个七步套路(算清单 → 预览 → 确认 → 逐文件推文本 → 落盘),
        /// 只是"改什么"从名字换成了一段文字。未打开的文件必须先能打开(磁盘上真存在),否则改完写不回去。
        /// 口径:默认不区分大小写 + 保护注释与字符串;想用整词 / 区分大小写 / 连注释一起,走命令行
        /// `hbcc --查找 … --换成 … [--整词|--区分大小写|--连注释一起] [--写回]`(同一套引擎)。
        /// </summary>
        private void 全工程替换_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var 页 = 当前选卡;
                bool 按正则 = _替换按正则;
                string? 找 = 输入窗口.问带勾选(this, "全工程替换", "要查找的内容:", "",
                    "在**整个工程**里替换一段文字;注释与字符串里的同样文字不会被改。按确定后先给改动预览。\n" +
                    "勾上「按正则」= 要找的内容**按正则解释**(H5;与编辑器 Ctrl+F 面板同一套口径),换成里可以写 $1 表示分组。",
                    "按正则(与 Ctrl+F 面板同一套)", ref 按正则);
                if (找 == null) return;
                _替换按正则 = 按正则;
                if (找.Length == 0) { 替换提示("要找的内容是空的 —— 写点东西再替换。"); return; }
                string? 换 = 输入窗口.问(this, "全工程替换", $"把 `{找}` 换成:", "",
                    按正则
                        ? "留空 = 删掉这些文字;按正则时这里可以写 $1 / $2(= 这一处命中的第 1 / 第 2 个分组)、$$(= 一个 $)。确认后会逐文件保存,撤销请逐个文件来。"
                        : "留空 = 删掉这些文字;确认后会逐文件保存,撤销请逐个文件来(每个文件一个撤销栈)。");
                if (换 == null) return;

                string 找说法 = 按正则 ? $"正则 `{找}`" : $"`{找}`";
                var 项目 = 拿检查用的项目();
                if (项目 == null) { 替换提示("没有可用的工程或文件,算不出要改哪些地方。先打开一个文件或工程再试。"); return; }
                var 文件表 = 取当前源码(项目);
                var 设置 = new 查找设置C { 正则 = 按正则 };
                var 清单 = 查找替换C.替换清单(找, 换, 文件表, 设置);
                if (清单.问题.Count > 0) { 替换提示(清单.预览()); return; }
                if (清单.处数 == 0)
                {
                    替换提示($"工程里没有找到 {找说法} 可以替换的地方(注释与字符串里的不算)。");
                    return;
                }

                var 涉及文件 = 清单.涉及文件;
                foreach (string 文件 in 涉及文件)
                    if (找标签页(文件) == null && !File.Exists(文件))
                    {
                        替换提示($"`{Path.GetFileName(文件)}` 既没打开、磁盘上也不在了,改完没法保存。先把它打开再试。");
                        return;
                    }

                输出框.AppendText(清单.预览(带行: true) + "\n");
                输出框.AppendText("撤销是逐个文件独立的(每个文件一个撤销栈),没有「一次全撤销」。\n");
                切到底部输出();
                if (MessageBox.Show(
                        $"要在整个工程里把 {找说法} 换成 `{换}`(留空 = 删掉)吗?共 {清单.处数} 处、{涉及文件.Count} 个文件。\n" +
                        $"口径:{设置}。\n确认后会立刻保存这些文件。",
                        "全工程替换", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                {
                    输出框.AppendText("已取消全工程替换(一个字符都没改)。\n");
                    return;
                }

                var 改后 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (名称, 文本) in 清单.应用(文件表)) 改后[名称] = 文本;

                var 要存 = new List<选卡页视图模型>();
                var 收过 = new HashSet<选卡页视图模型>();
                foreach (string 文件 in 涉及文件)
                {
                    var 目标页 = 找标签页(文件);
                    if (目标页 == null) { 打开文件到标签页(文件); 目标页 = 找标签页(文件); }
                    if (目标页 == null) { 异常收集C.记($"全工程替换:没能把 {文件} 打开成标签页,这个文件没有改。"); continue; }
                    if (!改后.TryGetValue(文件, out string? 新文本)) { 异常收集C.记($"全工程替换:算不出 {文件} 改完的文本,这个文件没有改。"); continue; }
                    目标页.内容 = 新文本;
                    if (收过.Add(目标页)) 要存.Add(目标页);
                }
                int 存好 = 0;
                foreach (var 目标页 in 要存)
                    if (保存标签页(目标页, 强制另存为: false)) 存好++;

                输出框.AppendText($"已替换 `{找}` → `{换}`:{清单.处数} 处 / {涉及文件.Count} 个文件,落盘 {存好} 个。\n");
                安排检查();          // 文本变了:400ms 后重编一遍,问题页跟着更新
            }
            catch (Exception 异常)
            {
                异常收集C.记($"全工程替换失败: {异常.Message}");
                记异常框($"全工程替换失败: {异常}");
            }
        }

        /// <summary> 查找/替换的一句提示:输出框留一份 + 弹一次对话框(与重命名同一套)</summary>
        private void 替换提示(string 文本)
        {
            输出框.AppendText(文本 + "\n");
            切到底部输出();
            MessageBox.Show(文本, "查找 / 替换", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

    }
}
