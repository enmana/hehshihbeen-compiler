using hehshihbeen_compiler.选卡控件;
using hehshihbeen_compiler.配置;
using hehshihbeen_compiler.界面控件;
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
    /// <summary> 主窗口的一部分:调试会话装载 / 单步 / 断点 / 调试面板 / 集成调试视图 </summary>
    public partial class MainWindow : Window
    {

        // ---- 控制台页:程序的输出往"输出页 + 控制台页"都写一份;输入靠一个字节队列(程序等输入时你敲一行送进去) ----
        private readonly Queue<byte> _控制台输入 = new();
        private 文字输出C? _控制台解码;
        /// <summary> 工程里配了资源文件当输入流时的读取器(优先于控制台输入)</summary>
        private Func<uint?>? _资源输入流;

        /// <summary> 本次执行捕获到的程序输出(文本,已经 UTF-8 解好)—— 输出断言用它,别再用"没被写过的空串"去比 </summary>
        private readonly StringBuilder 本次程序输出 = new();

        /// <summary> 程序吐一个字节:按 UTF-8 解好,**先攒进输出缓冲**(H22:程序跑在后台线程上,
        /// 这里绝不许碰 输出框),由界面线程的节流器刷到"输出页 + 控制台页",并记进 本次程序输出 ——
        /// 跑完的"输出断言"就是拿它跟 `// 预期输出:` 比
        /// (以前断言读的是一个**从没被写过的**局部 StringBuilder,所以凡是打了字的示例都报"实际 &lt;&gt;") </summary>
        private void 控制台吐字节(byte b)
        {
            try
            {
                _控制台解码 ??= new 文字输出C(收程序输出);
                _控制台解码.写(b);
            }
            catch { }
        }

        /// <summary> 程序要一个输入字节:队列里有就给;没有 → 返回 null(等输入模式下 VM 会停下来等你敲) </summary>
        private uint? 取一个输入字节()
        {
            if (_资源输入流 != null) return _资源输入流();      // 工程里配了资源文件当输入:优先吃它(自检/批处理用)
            return _控制台输入.Count > 0 ? _控制台输入.Dequeue() : null;
        }

        /// <summary> 把一行文字(UTF-8 + 换行)送进输入队列;程序正在等输入就顺势接着跑(整趟都在后台线程上,H22) </summary>
        private async Task 送一行输入(string 行)
        {
            foreach (byte b in Encoding.UTF8.GetBytes(行)) _控制台输入.Enqueue(b);
            _控制台输入.Enqueue((byte)'\n');
            控制台输出框.AppendText($"> {行}\n");               // 回显,让用户看得见自己送了什么
            控制台输出框.ScrollToEnd();
            // R2-6:接着跑 = `接上一次的输出` —— 输出连着攒(断言比整个程序),**输入队列也不许清**
            // (刚送进来的那一行就在里面)。以前这条路上两点都不成立:队列被清 + 循环条件一进来就假,
            // 送进输入程序毫无反应(探针实测)。
            if (前端契约C.服务.调试.机器.正在等输入 && 当前机器码.Length > 0)
            {
                await 跑一遍(跑到断点: false, 接上一次的输出: true);
            }
            else if (前端契约C.服务.调试.会话机器.正在等输入)
            {
                await 跑一遍(跑到断点: true, 接上一次的输出: true);
            }
        }

        private async void 控制台输入框_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            string 行 = 控制台输入框.Text;
            控制台输入框.Clear();
            await 送一行输入(行);
        }

        private async void 控制台发送_Click(object sender, RoutedEventArgs e)
        {
            string 行 = 控制台输入框.Text;
            控制台输入框.Clear();
            await 送一行输入(行);
        }

        /// <summary> 视图菜单:项目树 / 输出 / 编译 / 异常 / 控制台 / 变量 / 调试 —— 每个框一个开关 </summary>
        private void 视图开关_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem 项) return;
            bool 要显示 = 项.IsChecked;
            switch (项.Name)
            {
                case "视图项目树":
                    项目树列.Width = 要显示 ? new GridLength(240) : new GridLength(0);
                    项目树列.MinWidth = 要显示 ? 160 : 0;
                    break;
                case "视图输出": 设置页签(输出页, 要显示); break;
                case "视图编译": 设置页签(编译页, 要显示); break;
                case "视图异常": 设置页签(异常页, 要显示); break;
                case "视图控制台": 设置页签(控制台页, 要显示); break;
                case "视图问题": 设置页签(问题页, 要显示); break;
                case "视图变量": 设置页签(变量页, 要显示); break;
                case "视图调试": 设置页签(调试页, 要显示); break;
            }
        }

        /// <summary> 显示/收起底部某个页签(收起的页签不显示页头,和"变量/调试"两页同一套做法)</summary>
        private void 设置页签(TabItem 页, bool 要显示)
        {
            if (要显示)
            {
                页.Visibility = Visibility.Visible;
                if (底部行.ActualHeight < 120) 底部行.Height = new GridLength(200);   // 太矮了看不见,给它点高度
                底部选卡.SelectedItem = 页;
            }
            else
            {
                if (ReferenceEquals(底部选卡.SelectedItem, 页))
                {
                    // 先挪到还有的页上,再收起来
                    var 还能选 = 底部选卡.Items.OfType<TabItem>().FirstOrDefault(x => x.Visibility == Visibility.Visible && !ReferenceEquals(x, 页));
                    if (还能选 != null) 底部选卡.SelectedItem = 还能选;
                }
                页.Visibility = Visibility.Collapsed;
            }
            同步视图菜单勾选();
        }

        /// <summary> 视图菜单的勾选状态跟实际显示同步(变量/调试页会在单步时自动出现,勾选也跟着变) </summary>
        private void 同步视图菜单勾选()
        {
            视图项目树.IsChecked = 项目树列.Width.Value > 0;
            视图输出.IsChecked = 输出页.Visibility == Visibility.Visible;
            视图编译.IsChecked = 编译页.Visibility == Visibility.Visible;
            视图异常.IsChecked = 异常页.Visibility == Visibility.Visible;
            视图控制台.IsChecked = 控制台页.Visibility == Visibility.Visible;
            视图问题.IsChecked = 问题页.Visibility == Visibility.Visible;
            视图变量.IsChecked = 变量页.Visibility == Visibility.Visible;
            视图调试.IsChecked = 调试页.Visibility == Visibility.Visible;
        }

        /// <summary>
        /// 编译期优化(消冗余指令 + 循环不变量提升)。默认开;输出框写 `优化="关"` 可关掉(对着原样展开查错用)。
        /// 关掉以后编译指纹要跟着变,不然切来切去 F5 会以为"没改过,不用重编译"。
        /// </summary>
        private static bool 优化开
        {
            get => 选项.当前.优化开;
            set
            {
                if (选项.当前.优化开 == value) return;
                选项.当前.优化开 = value;
                选项.保存(选项.当前);
            }
        }

        /// <summary> 工程里参与编译的源文件内容,跟上次编译时一样吗? </summary>
        private bool 源码变了吗()
        {
            if (当前机器码.Length == 0) return true;
            if (解决方案.启动项目 is not { } 项目) return false;
            try
            {
                var 文件表 = 项目.源文件.Select(f => (名称: f.全称, 内容: 前端契约C.服务.宿主.读源文件(f.全称))).ToList();   // T16:同一读入口
                return 算源码指纹(文件表) + 优化标记() != 编译指纹;
            }
            catch { return true; }     // 读不了就当变了:交给编译去给出明确错误
        }

        /// <summary>
        /// 源文本指纹:文件名 + 内容拼起来做一个 FNV-1a 散列。只是为了"变了没有",不做安全用途,
        /// 所以不上密码学散列(几千字节的源码,这点开销可以忽略)。
        /// </summary>
        private static string 算源码指纹(IEnumerable<(string 名称, string 内容)> 文件表)
        {
            uint 哈希 = 2166136261;
            int 长度 = 0;
            foreach (var (名称, 内容) in 文件表)
            {
                foreach (char c in 名称) { 哈希 = (哈希 ^ c) * 16777619; 长度++; }
                哈希 = (哈希 ^ '\u0001') * 16777619;
                foreach (char c in 内容 ?? "") { 哈希 = (哈希 ^ c) * 16777619; 长度++; }
                哈希 = (哈希 ^ '\u0002') * 16777619;
            }
            return $"{长度:x8}-{哈希:x8}";
        }

        /// <summary>
        /// 把这个项目里"改过还没保存"的标签页先存盘,返回存了几个(没路径的未命名页跳过,不弹框)。
        /// </summary>
        private int 保存项目里改过的页(项目项C 项目)
        {
            int 存了 = 0;
            var 项目文件 = 项目.源文件.Select(f => f.全称).ToList();
            foreach (var 页 in 主视图模型.选卡页视图模型表.ToList())
            {
                if (!页.有未保存更改 || string.IsNullOrWhiteSpace(页.全称)) continue;
                if (!项目文件.Any(全 => string.Equals(全, 页.全称, StringComparison.OrdinalIgnoreCase))) continue;
                if (保存标签页(页, 强制另存为: false)) 存了++;
            }
            return 存了;
        }

        /// <summary>
        /// 点"逐步"就该直接能走:先自动编译、再自动装载(不用先想着"我得先点编译、再点复位")。
        /// **上一轮已经跑完(停机)的也要重新装载** —— 否则点单步什么都不会发生,
        /// 只有调试页闪一下(用户实测:"逐语句直接点击无法进入单步")。
        /// </summary>
        private bool 自动编译好再调试()
        {
            if (!确保编译过()) return false;
            // H39-2:调试那一台也在子进程里(与"执行"那台各是各的通道);换了机器 = 得重新装载一遍
            if (!确保调试机()) { 报跑程序不可用(); return false; }
            // H38-2:"装的还是不是这一份 / 有没有停机 / 换过机器没有"三样都问契约(界面不再存那台机器)
            if (!前端契约C.服务.调试.会话机器码就是这一份(当前机器码) || 前端契约C.服务.调试.会话已停机
                || 前端契约C.服务.调试.会话装载机换过()) 装载调试();
            return true;
        }

        /// <summary> 把刚编译的机器码装进调试会话,顺手把断点换算成地址 </summary>
        private void 装载调试()
        {
            前端契约C.服务.调试.会话当前文件 = 当前选卡?.全称;
            前端契约C.服务.调试.会话内存大小 = 当前内存大小;                                // 工程文件里的 `内存` 跟着工程走
            同步宿主白名单();                                              // D1/D5:起进程与连网址的白名单也跟着工程走
            装载调试会话();
            前端契约C.服务.调试.记会话装载();
            前端契约C.服务.调试.会话标签表 = new Dictionary<string, int>(前端契约C.服务.编译.取标签表());   // 变量面板/寄存器说明靠它把槽标签翻成地址
            前端契约C.服务.调试.会话内部槽地址 = new Dictionary<string, int>(前端契约C.服务.编译.取内部槽地址());   // H100 第二步:运行监视算池/堆/软栈用量
            // 单步时程序自己的 OUT 也要进"输出"页(和"执行"一个去处);输入走**同一份**输入源 ——
            // 桌面端底部「控制台」页有输入框 + 发送键(`送一行输入`),所以在等输入的程序单步时也能喂进去,
            // 输入源没接上时才退回 EOF。
            // H39-2:输出 / 输入 / 宿主调用这三个回调**装载时不用再抄一遍** —— 它们在"建这台机器"的
            // 那一刻就接上了(见 `接上跑程序回调`),而且是挂在机器自己身上的。
            刷新断点地址();
        }

        /// <summary> 按会话返回的快照刷新界面:底部调试面板、黄条、调用处灰条 </summary>
        private void 应用调试快照(调试步进结果C 结果)
        {
            刷新集成调试视图();
            标出当前执行行(结果);
            标出调用处(结果);
        }

        /// <summary>
        /// 进入单步模式(第158批起只剩**一档"逐步"**,不再有"汇编视图"要换):
        /// 底部打开调试页(变量 / 指令 / 内存 / 寄存器),黄条按**源码行**画。
        /// 已经在单步里就只是把调试页刷出来(别收起来再开 —— 看着会闪一下,用户反馈过)。
        /// </summary>
        private void 进入单步()
        {
            if (正在单步)
            {
                打开调试页();          // 只是刷新一下(面板就在底部,不重建、不遮挡)
                return;
            }
            正在单步 = true;
            // 调试面板:单步期间显示在底部(变量 / 指令 / 内存 / 寄存器),不挡编辑器
            打开调试页();
            刷新断点显示();
        }

        /// <summary> 退出单步模式:关观察窗、清黄条与灰条、清调用栈 </summary>
        private void 退出单步()
        {
            if (!正在单步) return;
            正在单步 = false;
            前端契约C.服务.调试.会话当前文件 = null;
            前端契约C.服务.调试.会话调用栈.清空();
            if (当前编辑器 is { } 编辑)
            {
                编辑.清执行行();
                编辑.设调用处([], null, null);
            }
            收起调试页();                                 // 单步结束就把底部那两页一起收掉(用户要求)
            刷新断点显示();
        }

        /// <summary>
        /// 继续:往下跑 —— 有断点就停在断点上,没有就跑到停机(和"执行"同一件事,只是从当前位置接着走)。
        /// H22:和"执行"一样挪到**后台线程**(跑大循环 / 死循环时界面不冻),「停止」也能把它叫停;
        /// 后台那一段只碰虚拟机,界面动作一律等 `await` 回来再做。
        /// 第423批:整趟走 `记一趟` —— 与「执行」同一笔账(F5 在它跑着的时候按下去要能等它收工)。
        /// </summary>
        private async void 调试_继续按钮_Click(object sender, RoutedEventArgs e) => await 记一趟(继续这一趟());

        private async Task 继续这一趟()
        {
            try
            {
                bool 准备好了 = 准备好调试();       // 自动编译那一段由 确保编译过() 自己打标记(不翻汇编源码页)
                if (!准备好了) return;
                if (!正在单步) 进入单步();
                前端契约C.服务.调试.会话当前文件 = 当前选卡?.全称;
                前端契约C.服务.调试.会话汇编模式 = false;          // 黄条 / 调用处一律按**源码行**算(编辑器里就是源码)
                调试步进结果C? 结果 = null;
                await 在后台跑(会话机: true, () => { 结果 = 前端契约C.服务.调试.继续(); });
                if (结果 == null) return;
                bool 被停 = 结果.已中断;
                在界面上(() =>
                {
                    if (被停)
                    {
                        停止后收尾(结果);              // H22:被「停止」叫停,不当成"命中断点"
                        return;
                    }
                    if (结果.命中断点) 输出框.AppendText($"程序暂停(命中断点):{Path.GetFileName(结果.文件 ?? "")} 第{结果.源码行}行\n");
                    if (结果.命中断点) 切到命中文件(结果);      // P24:命中的是别的文件就自动切过去(跨文件单步不迷路)
                    应用调试快照(结果);
                });
            }
            catch (Exception ex)
            {
                // 第423批:与「执行」那条路同一个口径 —— 出错也是"这一趟算完了",图形窗口跟着收掉
                // (收不掉的话,下一次 `打开窗口` 会撞上还挂着的呈现口 / 还留在屏幕上的那个窗口)
                在界面上(收掉图形窗口);
                // H39-2:子进程 / 通道出的事如实说清(不当成"继续失败")
                if (!报跑程序故障(ex))
                {
                    编译框.AppendText($"\n继续执行失败: {ex.Message}");
                    记异常框($"继续执行失败: {ex}");
                }
            }
        }

        // ---------------- 断点:跟着工程走(项目目录里的 断点.hbd) ----------------

        /// <summary> 当前工程(断点、保存目录都跟着它) </summary>
        private 项目项C? 当前工程 => 解决方案.启动项目 ?? 解决方案.项目表.FirstOrDefault();

        /// <summary> 按当前工程把断点读进来(换工程/换启动项目时调) </summary>
        private void 按当前工程加载断点()
        {
            前端契约C.服务.调试.加载断点(当前工程?.目录);
            刷新断点显示();
            刷新断点地址();
        }

        /// <summary>
        /// 编辑器的红点。行号口径 = **源码行**(第158批起单步也不再换文档,编辑器里永远是源码)。
        /// </summary>
        private void 刷新断点显示()
        {
            try
            {
                if (当前编辑器 is not { } 当前) return;
                string? 全称 = 当前选卡?.全称;
                if (string.IsNullOrWhiteSpace(全称)) { 当前.设断点行([]); 当前.设断点附加行([]); return; }
                当前.设断点行(前端契约C.服务.调试.断点行号(全称));
                // P34:带条件 / 命中次数 / 日志的那几行画成空心圈(它们不一定会停下来)
                当前.设断点附加行(前端契约C.服务.调试.断点附加行号(全称));
            }
            catch { }
        }

        /// <summary>
        /// 等界面布局完再画一遍红点。打开项目那一刻编辑器控件还没有生成(是切标签时才生成的),
        /// 直接调 刷新断点显示() 会"扫不到编辑器" → 断点明明加载了却看不见。
        /// </summary>
        private void 稍后刷新断点显示() =>
            Dispatcher.BeginInvoke(new Action(刷新断点显示), DispatcherPriority.Loaded);

        /// <summary> 断点地址要重算:断点变了、或者刚编译完(地址全变了)都要调 </summary>
        /// <summary> 地址 → 那个断点(P24 条件断点要用它的 `条件`;P34 起还要 `命中次数` / `日志`);断点变了、刚编译完都要重算 </summary>
        private Dictionary<int, 断点项C> _断点条件表 = new();

        /// <summary> P34:地址 → 这个断点的三项判定(条件 / 命中次数 / 日志)—— 与 `_断点条件表` 一起重算 </summary>
        private Dictionary<int, 断点判定C> _断点判定表 = new();

        /// <summary>
        /// P34:**每个断点到目前为止经过了几次**(键 = 机器码地址)。
        /// 只在求值那一侧(后台线程)改、只在"重新装载程序"(界面线程、程序停着)清 —— 两者不重叠,所以不上锁。
        /// "哪一趟算新的一趟":**重新装载过**(`装载执行机` / `装载调试会话`)就清 —— 那时程序本来就从头跑。
        /// </summary>
        private readonly Dictionary<int, long> _断点命中次数 = new();

        /// <summary> 已经提示过的"条件判不了"(按文案去重):循环里每绕一圈都提示一次会把输出页刷爆 </summary>
        private readonly HashSet<string> _条件提示过 = new(StringComparer.Ordinal);

        /// <summary>
        /// P34:**日志断点攒下来的那几行**(等界面线程的节流器收走)。
        /// `锁` 是必须的:写它的是求值那一侧(可能跑在后台线程上),读它的是界面线程 ——
        /// 这是本批**唯一**一处跨线程共享的可变状态,其余(命中次数)靠"只在停着的时候清"划开。
        /// </summary>
        private readonly Queue<string> _待刷日志 = new();
        private readonly object _日志锁 = new();

        /// <summary> 断点红了/变了:重算地址表,并把判断函数交给调试会话 </summary>
        private void 刷新断点地址()
        {
            前端契约C.服务.调试.会话断点地址 = 前端契约C.服务.调试.断点地址(前端契约C.服务.调试.会话调试文件表);
            // P24/P34:顺手把"哪条地址上挂着什么样的断点"也算好,再把判断函数交给调试会话 ——
            // 它每走到一个断点地址都会问一次"这次算不算命中":不满足就当没命中(虚拟机一行都没改;
            // 日志断点就是靠"永远返回假 + 顺手记一行"实现的:程序不停,但那一行会出现在「输出」页)。
            _断点条件表 = 前端契约C.服务.调试.断点地址带条件(前端契约C.服务.调试.会话调试文件表);
            _断点判定表 = _断点条件表.ToDictionary(对 => 对.Key, 对 => 断点判定C.从断点(对.Value));
            _条件提示过.Clear();
            前端契约C.服务.调试.会话断点命中判断 = 断点条件满足;
        }

        /// <summary> P34:程序重新装载(从头跑)时把命中次数与攒下的日志清掉 </summary>
        private void 重置断点次数()
        {
            _断点命中次数.Clear();
            lock (_日志锁) _待刷日志.Clear();
        }

        /// <summary>
        /// P24/P34:走到了断点地址,**这一趟算不算命中**?
        ///
        /// 顺序是"先记一次经过,再看三关":① **命中次数**(`%5` / `==5` / `&gt;=5`,不填就是每次都算过)
        /// ② **条件**(不填就是不拦)③ 填了**日志**的 → 打一行、返回**假**(不停下)。
        /// 任何一关判不了(变量看不见 / 类型比不了 / 条件写错)→ **按『停』处理 + 提示一次** ——
        /// 宁可多停一次让人看见问题,也绝不"悄悄猜一个值"把断点吞掉(那才是最难查的毛病)。
        /// </summary>
        private bool 断点条件满足(int 地址)
        {
            try
            {
                if (!_断点条件表.TryGetValue(地址, out var 断点)) return true;
                var 判定 = _断点判定表.TryGetValue(地址, out var 有) ? 有 : 断点判定C.从断点(断点);

                // ① 走过这一趟了(次数只记账,不参与"停不停"以外的判断)
                long 次数 = (_断点命中次数.TryGetValue(地址, out long 旧) ? 旧 : 0) + 1;
                _断点命中次数[地址] = 次数;

                // ②③ 条件与日志:交给引擎那一侧的求值器(纯逻辑,自检直接喂参数真跑)
                var 求值器 = new 断点求值器C(前端契约C.服务.调试.会话调试文件表, 前端契约C.服务.调试.会话标签表,
                    前端契约C.服务.调试.内存数组(前端契约C.服务.调试.会话机器),
                    前端契约C.服务.调试.寄存器值(前端契约C.服务.调试.会话机器, 寄存器C.帧指针号),
                    行 => { lock (_日志锁) _待刷日志.Enqueue(行); });
                bool? 判 = 求值器.这一趟(断点, 判定, 次数, out string 说明);
                if (判 == null)
                {
                    条件提示一次($"{Path.GetFileName(断点.文件)} 第 {断点.行} 行:{说明}(这一处先按『停』处理)");
                    return true;
                }
                if (!判.Value) return false;
                return true;
            }
            catch (Exception 异常)
            {
                异常收集C.记($"条件断点判断失败: {异常.Message}");
                return true;              // 判断本身出问题就按"停":别让断点条件把整趟调试带歪
            }
        }

        /// <summary> P34:把攒下的日志刷到「输出」页(界面线程;没攒到就什么都不做 —— 别每走一步都动一次控件) </summary>
        private void 刷日志到输出()
        {
            string[] 行们;
            lock (_日志锁)
            {
                if (_待刷日志.Count == 0) return;
                行们 = [.. _待刷日志];
                _待刷日志.Clear();
            }
            foreach (string 行 in 行们) 输出框.AppendText(行 + "\n");
            if (!正在单步) 切到底部输出();          // 单步期间别把底部的"变量/监视"页抢走
        }

        /// <summary> 条件相关的提示只说一遍(同一句),不然循环里条件判不了会刷屏 </summary>
        private void 条件提示一次(string 文本)
        {
            if (!_条件提示过.Add(文本)) return;
            输出框.AppendText("⚠ " + 文本 + "\n");
            切到底部输出();
        }

        /// <summary>
        /// P24:停在断点上时**自动切到那个文件**。跨文件调试时,人还盯着旧标签页、
        /// 黄条却画在刚切过来的新页上不一致 —— 干脆把页也跟着切过去(命中别的文件还赖着不动最误导人)。
        /// </summary>
        private void 切到命中文件(调试步进结果C 结果)
        {
            try
            {
                if (结果.文件 is not { Length: > 0 } 文件) return;
                if (当前选卡?.全称 is { Length: > 0 } 正看着 && 前端契约C.服务.宿主.是同一个文件(正看着, 文件)) return;   // 就在眼前,不用切
                if (找标签页(文件) == null) 打开文件到标签页(文件);
                if (找标签页(文件) is not { } 页) return;
                主视图模型.选中页 = 页;
                代码选卡.UpdateLayout();      // 让容器先生成出来:紧接着画黄条/红点才画得到这一页上
            }
            catch (Exception 异常) { 异常收集C.记($"切到断点文件失败: {异常.Message}"); }
        }

        /// <summary> 运行 → 断点条件…:给**光标那一行**的断点写条件(留空 = 取消条件) </summary>
        private void 断点条件_Click(object sender, RoutedEventArgs e) => 执行断点条件();

        /// <summary> 调试 → 加监视表达式…(P35;快捷键 Ctrl+Alt+W) </summary>
        private void 加监视_Click(object sender, RoutedEventArgs e) => 执行加监视();

        private void 执行断点条件()
        {
            try
            {
                var 编 = 当前编辑器;
                if (编 == null || 当前选卡?.全称 is not { Length: > 0 } 全称)
                {
                    调试提示("先把光标放到要加条件的那一行(文件要有路径)");
                    return;
                }
                int 行 = 编.取当前行号();
                if (!前端契约C.服务.调试.有断点(全称, 行))
                {
                    // 键位那一句跟着**当前生效的键位**走(用户可以在选项页改,也可能整个取消);不写死一个 F9
                    string 断点键 = 快捷键C.生效键("切换断点", 选项.当前.快捷键);
                    string 怎么下 = 断点键.Length > 0 ? $"先按 {断点键},或者点行号左边" : "点行号左边";
                    调试提示($"第 {行} 行还没有断点 —— {怎么下}下一个断点,再写条件。");
                    return;
                }
                // P34:一个对话框把三样写完(命中条件 / 命中次数 / 日志断点);取消 = 一个字都不改
                var 设置 = 断点设置窗口.问(this, $"第 {行} 行的断点({Path.GetFileName(全称)})", 前端契约C.服务.调试.取断点(全称, 行));
                if (设置 == null) return;

                前端契约C.服务.调试.设断点(全称, 行, 设置.条件, 设置.命中次数, 设置.日志);
                前端契约C.服务.调试.保存断点(当前工程?.目录);
                刷新断点显示();
                刷新断点地址();

                string 文;
                if (设置.条件.Length == 0 && 设置.命中次数.Length == 0 && 设置.日志.Length == 0)
                    文 = $"第 {行} 行的断点已改回普通断点(命中就停)。";
                else
                {
                    var 条 = 断点条件求值C.解析(设置.条件);
                    var 次 = 命中次数条件C.解析(设置.命中次数);
                    文 = $"第 {行} 行的断点:{条.说明};{次.说明}"
                        + (设置.日志.Length > 0 ? $";日志断点 —— 命中时打印 `{设置.日志}` 但不停下" : "");
                    if (!条.合法) 文 += $"\n注意:{条.说明}";
                    if (!次.合法) 文 += $"\n注意:{次.说明}";
                }
                调试提示(文);
                说一句(文);                  // 状态栏那句只在调试输出里看得见 → 输出页也留一份(可回看)
                切到底部输出();
                if (正在单步) 刷新监视();     // 断点条件变了,监视那一列也跟着重算一次
            }
            catch (Exception 异常)
            {
                异常收集C.记($"设置断点条件失败: {异常.Message}");
                记异常框($"设置断点条件失败: {异常}");
            }
        }

        /// <summary>
        /// 切换某一行的断点(点行号左边 / 菜单"切换断点"都走这里)。
        ///
        /// 第158批起**只剩源码行一种口径**:编辑器永远显示源码(单步也不再换成展开汇编),
        /// 汇编源码页(`.hba`)那一路由下面那道门槛挡住(见注释)。
        /// </summary>
        private void 断点行被点击(int 行号)
        {
            if (当前选卡?.全称 is not { Length: > 0 } 全称 || 行号 <= 0) return;

            // C20:汇编源码页(`.hba`)里那串汇编是**整篇展开拼起来的**,不属于任何文件的源码行 ——
            // 下在这里的断点画不准、下次编译还会被 `清失效` 清掉(留个永远不响的红点最伤人)。直接说清楚。
            // C22:但**参与编译**的那份 `.hba` 不一样 —— 它自己是源码(行号就是它自己的),单步、断点都成立。
            if (当前选卡 is { 是汇编源码页: true }
                && (解决方案.找所属项目(全称) ?? 解决方案.启动项目)?.参与编译(全称) != true)
            {
                说一句("这是汇编源码页(`.hba`):里面是展开出来的汇编,没有源码行对应,不能下断点;断点请下在源码页上。" +
                    "想在这一份里单步/下断点,先把它勾成『参与编译』(运行菜单)。");
                return;
            }

            // 空行/注释行不能下断点(那一行压根没有代码可停)
            string 行文本 = (当前编辑器?.取行文本(行号) ?? "").Trim();
            if (行文本.Length == 0 || 行文本.StartsWith("//") || 行文本.StartsWith("/*") || 行文本.StartsWith('*'))
            {
                说一句($"第 {行号} 行是空行或注释,不能下断点");
                return;
            }

            int 存行 = 行号;

            // 编译过、而且这张调试表里真有这个文件,才能拿它判"这一行有没有代码";
            // 没编译过(或者表是别的工程的)时**先记下断点**,等编译后再校验/清理 ——
            // 不然会冤枉所有行都说"没有可执行的代码"(用户实测:没编译过时一个断点都下不上)
            bool 已有断点 = 前端契约C.服务.调试.有断点(全称, 存行);
            bool 有本文件调试信息 = 前端契约C.服务.调试.断点表有文件(前端契约C.服务.调试.会话调试文件表, 全称);
            if (!已有断点 && 有本文件调试信息 && 前端契约C.服务.调试.断点算某行地址(前端契约C.服务.调试.会话调试文件表, 全称, 存行) == null)
            {
                说一句($"第 {行号} 行没有可执行的代码,不能下断点");
                return;
            }

            bool 加了 = 前端契约C.服务.调试.切换断点(全称, 存行);
            前端契约C.服务.调试.保存断点(当前工程?.目录);
            刷新断点显示();
            刷新断点地址();
            刷新命令可用性();                  // "清除所有断点"要跟着能亮/能灰
            输出框.AppendText($"{(加了 ? "已加断点" : "已去断点")}:{Path.GetFileName(全称)} 第{存行}行\n");
            if (!正在单步) 切到底部输出();       // 单步期间别把底部的"变量/调试"页抢走
        }

        /// <summary> 调试 → 切换断点:在光标那一行加/去一个断点 </summary>
        private void 菜单切换断点_Click(object sender, RoutedEventArgs e)
        {
            if (当前编辑器 is not { } 编辑) return;
            断点行被点击(编辑.取当前行号());
        }

        /// <summary> 调试 → 清除所有断点(编辑器右键「删除全部断点」也走这里) </summary>
        private void 菜单清除所有断点_Click(object sender, RoutedEventArgs e)
        {
            int 清了几个 = 前端契约C.服务.调试.断点个数;
            前端契约C.服务.调试.清除断点();
            前端契约C.服务.调试.保存断点(当前工程?.目录);
            刷新断点显示();
            刷新断点地址();
            输出框.AppendText(清了几个 == 0 ? "当前没有断点。\n" : $"已清除所有断点({清了几个} 个)。\n");
            切到底部输出();
            刷新命令可用性();
        }

        /// <summary>
        /// 把"调用处"标出来:整行淡灰 + 同一行里按调用层叠色的跨度(C15)。
        /// 行号与帧都由会话按当前模式算好;跨度要用编辑器里那份正文,所以交给编辑器去算。
        /// </summary>
        private void 标出调用处(调试步进结果C 结果)
        {
            try
            {
                if (当前编辑器 is { } 编辑) 编辑.设调用处(结果.调用处行, 结果.调用帧, 当前选卡?.全称);
            }
            catch { }
        }

        /// <summary>
        /// 把"当前执行到哪一行"标到编辑器上(单步/继续/停在断点之后调)。
        /// 第158批起**只有一种口径**:源码行(编辑器永远是源码,不再换成展开汇编)。
        ///
        /// 第159批补一条(用户实测"逐步看着没往前走"的根因):**黄条得画在正在执行的那个文件那一页上** ——
        /// 编译之后当前页可能已经不是它了(C20 会把 `<文件>_ASM.hba` 翻到前面;用户也可能自己翻去别的文件),
        /// 那时旧代码只会 `清执行行()`,于是"走了也看不见"。所以先按 P24 那套**切页**(见 `切到命中文件`),再画黄条。
        /// 快照里查不到源码行(0)就清掉黄条 —— 不硬猜,免得标错行。
        /// </summary>
        private void 标出当前执行行(调试步进结果C 结果)
        {
            try
            {
                if (结果.文件 is { Length: > 0 }) 切到命中文件(结果);      // 别把黄条画在别的页上
                if (当前编辑器 is not { } 编辑) return;
                if (结果.源码行 > 0 && string.Equals(当前选卡?.全称, 结果.文件, StringComparison.OrdinalIgnoreCase))
                    编辑.设执行行(结果.源码行, 深黄: false);
                else 编辑.清执行行();
            }
            catch { }
        }

        // ---------------- 调试面板(变量 / 调试 两页,就在主窗口底部;只在单步/停在断点时出现) ----------------

        private readonly System.Collections.ObjectModel.ObservableCollection<变量行C> 变量行集 = [];
        private readonly List<寄存器行C> 寄存器行集 = [];
        private bool 调试面板已建;

        /// <summary> 第一次用到时把表格的数据源挂上(寄存器固定 16 行,建一次之后只改值) </summary>
        private void 建调试面板()
        {
            if (调试面板已建) return;
            调试面板已建 = true;
            for (int 号 = 0; 号 < 寄存器C.数量; 号++) 寄存器行集.Add(new 寄存器行C { 寄存器 = 寄存器C.名称(号) });
            变量表.ItemsSource = 变量行集;
            寄存器表.ItemsSource = 寄存器行集;
        }

        /// <summary>
        /// 显示/收起底部的"变量"和"调试"两页。用户要求:只有**单步**和**停在断点上**的时候出现,
        /// 平时收起来(以前那个浮窗太挡视线,而且拖走了单步一开又跳回来)。
        /// </summary>
        private void 显示调试页(bool 显示, bool 切到变量页 = false)
        {
            try
            {
                建调试面板();
                if (显示)
                {
                    变量页.Visibility = Visibility.Visible;
                    监视页.Visibility = Visibility.Visible;
                    调试页.Visibility = Visibility.Visible;
                    // 底部太矮时(默认只有 1/4 高)三栏挤得没法看,给它撑到能用的高度(之后用户想拖还是能拖)
                    if (底部行.ActualHeight < 200) 底部行.Height = new GridLength(240);
                    if (切到变量页) 底部选卡.SelectedItem = 变量页;
                }
                else
                {
                    // 先把选中页挪走,再收起(不然收起的正好是选中页,不好看也不好点)
                    if (ReferenceEquals(底部选卡.SelectedItem, 变量页) || ReferenceEquals(底部选卡.SelectedItem, 调试页)
                        || ReferenceEquals(底部选卡.SelectedItem, 监视页))
                        底部选卡.SelectedItem = 输出页;
                    变量页.Visibility = Visibility.Collapsed;
                    监视页.Visibility = Visibility.Collapsed;
                    调试页.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex) { 记异常框($"切换调试页失败: {ex.Message}"); }
        }

        /// <summary> 打开底部调试面板:进入单步、停在断点上时用(默认切到"变量"页 —— 这是最常看的)</summary>
        private void 打开调试页(bool 切到变量页 = true)
        {
            显示调试页(true, 切到变量页);
            刷新集成调试视图();
        }

        /// <summary> 收起底部调试面板:复位 / 程序跑完 / 退出单步时用 </summary>
        private void 收起调试页() => 显示调试页(false);

        /// <summary> 变量/寄存器表格里的"谁的"那一列(复用调试会话里那套说明)</summary>
        private static string 谁的(寄存器记录C? 记录, int 号, uint 当前帧指针)
        {
            if (记录 is { 变量名.Length: > 0 } && 记录.登记时帧指针 == 当前帧指针)
            {
                string 类型 = 记录.类型名.Length > 0 ? $"({记录.类型名})" : "";
                return 记录.是地址 ? $"{记录.变量名}{类型} 的地址" : $"{记录.变量名}{类型}";
            }
            if (号 == 15) return "帧指针(局部变量都靠它 + 偏移定位)";
            if (号 == 0) return "返回值 / 表达式结果";
            return "";
        }

        /// <summary> 变量名字 → 寄存器列文本(结构字段按"结构名.字段"取它的结构名;取不到就空) </summary>
        private static string 找寄存器(Dictionary<string, List<int>> 表, string 名字)
        {
            if (表.Count == 0) return "";
            if (表.TryGetValue(名字, out var 直接)) return string.Join(" ", 直接.Select(寄存器C.名称));
            int 点 = 名字.IndexOf('.');
            if (点 > 0 && 表.TryGetValue(名字[..点], out var 基础)) return string.Join(" ", 基础.Select(寄存器C.名称));
            return "";
        }

        /// <summary> 按会话当前状态刷新"变量 / 监视 / 调试"三页(主窗口每走一步调一次;没显示也刷,切过去就是新的)</summary>
        private void 刷新集成调试视图()
        {
            try
            {
                建调试面板();
                var 机 = 前端契约C.服务.调试.会话机器;
                byte[] 码 = 前端契约C.服务.调试.会话机器码;
                var 快照 = 前端契约C.服务.调试.会话快照();

                // ---- 变量表 ----
                int 源码行 = 快照.源码行;
                var 变量到寄存器 = 寄存器信息管理器C.变量到寄存器(快照.寄存器说明, 前端契约C.服务.调试.寄存器值(机, 寄存器C.帧指针号));
                var 新行 = new List<变量行C>();
                foreach (var 项 in 快照.变量)
                    新行.Add(new 变量行C
                    {
                        名字 = new string(' ', 项.层级 * 2) + 项.名字,
                        值 = 项.可读 ? 项.值文本 : "?(现在读不到)",
                        类型 = 项.类型名,
                        在哪 = 项.位置文本,
                        寄存器槽 = 找寄存器(变量到寄存器, 项.名字),
                    });
                // 同一批变量(名字一样)就原地改值:不重建列表 → 不闪、滚动位置不跳
                if (变量行集.Count == 新行.Count && !变量行集.Where((旧, i) => 旧.名字 != 新行[i].名字).Any())
                    for (int i = 0; i < 新行.Count; i++) 变量行集[i].设为(新行[i]);
                else
                {
                    变量行集.Clear();
                    foreach (var 行 in 新行) 变量行集.Add(行);
                }
                变量页.Header = 源码行 > 0 ? $"变量(第 {源码行} 行)" : "变量";

                // ---- P34:日志断点攒下的行,先刷到「输出」页(界面线程,一步一次) ----
                刷日志到输出();

                // ---- P35:监视那一页跟着重算(同一份快照,不再问一次机器) ----
                刷新监视(快照.变量, 源码行);

                // ---- 指令(标题里带上"在哪/PC/走了几步",原来浮窗顶部那行状态搬这儿) ----
                string 在哪 = 前端契约C.服务.调试.会话取位置((int)机.程序计数器) is { } 位置
                    ? $"{System.IO.Path.GetFileName(位置.文件)} 第 {位置.行} 行"
                    : "编译器生成的那一段";
                指令标题.Text = 机.停机
                    ? $"指令 · 已停机(共 {前端契约C.服务.调试.会话已执行步数} 步)"
                    : $"指令 · {在哪} · PC = 0x{机.程序计数器:X} · 已执行 {前端契约C.服务.调试.会话已执行步数} 步";
                var 指令文本 = new StringBuilder();
                指令文本.AppendLine("── 当前指令 ──");
                指令文本.AppendLine(反汇编C.反汇编一条(码, 机.程序计数器, out _) ?? "(已结束)");
                指令文本.AppendLine();
                指令文本.AppendLine("── PC 附近 ──");
                if (码.Length > 0)
                    foreach (string 行 in 反汇编C.反汇编附近(码, 机.程序计数器, 6, 10))
                        指令文本.AppendLine(行);
                指令框.Text = 指令文本.ToString();

                // ---- 寄存器(16 行建一次,之后只改值) ----
                for (int 号 = 0; 号 < 寄存器行集.Count; 号++)
                {
                    var 记录 = 快照.寄存器说明.Length > 号 ? 快照.寄存器说明[号] : null;
                    uint 值 = 前端契约C.服务.调试.寄存器值(机, 号);
                    寄存器行集[号].值 = $"{值} (0x{值:X})";
                    寄存器行集[号].谁的 = 谁的(记录, 号, 前端契约C.服务.调试.寄存器值(机, 寄存器C.帧指针号));
                }

                // ---- 内存(代码段 + 数据区开头) ----
                var 内存文本 = new StringBuilder();
                int 显示长度 = Math.Min(码.Length + 0x60, 0x400);
                for (int 行首 = 0; 行首 < 显示长度; 行首 += 16)
                {
                    内存文本.Append($"0x{行首:X4}: ");
                    for (int 列 = 0; 列 < 16 && 行首 + 列 < 显示长度; 列++)
                        内存文本.Append($"{前端契约C.服务.调试.内存字节(机, 行首 + 列):X2} ");
                    内存文本.AppendLine();
                }
                内存框.Text = 内存文本.ToString();
            }
            catch (Exception ex) { 异常收集C.记($"刷新调试面板失败: {ex.Message}"); }
        }

        /// <summary>
        /// 当前打开着的所有编辑器控件(代码检查要一份份看过去)+ 它属于哪个标签页。
        /// 两步走:① 每个标签页的容器里找(最准);② 容器还没造出来时,直接在可视树里把 编辑器 捞出来、
        /// 用它的 DataContext 认页 —— 少了这一步,某些标签页会被漏掉(用户反馈过"编的不是我在看的文件")。
        /// </summary>
        private IEnumerable<(编辑器 编, 选卡页视图模型 页)> 全部编辑器带页()
        {
            var 给过了 = new HashSet<编辑器>();
            foreach (var 页 in 主视图模型.选卡页视图模型表)
            {
                if (页.是特殊页) continue;
                if (代码选卡.ItemContainerGenerator.ContainerFromItem(页) is not TabItem 项) continue;
                if (查找视觉子级<编辑器>(项) is { } 编 && 给过了.Add(编)) yield return (编, 页);
            }
            foreach (var 编 in 查找所有视觉子级<编辑器>(代码选卡))
            {
                if (给过了.Contains(编)) continue;
                if (编.DataContext is 选卡页视图模型 页2 && !页2.是特殊页 && 给过了.Add(编)) yield return (编, 页2);
            }
        }

        /// <summary> 汇编/保存使用的源代码：始终取当前标签页绑定内容，编辑后会经双向绑定实时更新</summary>
        public string 源代码 => 当前选卡?.内容 ?? string.Empty;
        /// <summary> 当前标签页内容区域中的 编辑器 控件实例（用于撤销/重做等编辑器内部操作）</summary>
        private 编辑器? 当前编辑器
        {
            get
            {
                var 卡 = 当前选卡;
                if (卡 != null && 代码选卡.ItemContainerGenerator.ContainerFromItem(卡) is TabItem tabItem)
                {
                    var 编辑器实例 = 查找视觉子级<编辑器>(tabItem);
                    if (编辑器实例 != null) return 编辑器实例;
                }
                return 查找视觉子级<编辑器>(代码选卡);
            }
        }

        // ==================== P35:监视表达式 ====================
        #region 监视表达式

        /// <summary> "监视"页上的一行(界面绑定用)+ 它背后那条表达式(历史只有引擎那一份)</summary>
        private readonly System.Collections.ObjectModel.ObservableCollection<监视行C> 监视行集 = [];
        private readonly List<监视表达式C> 监视表 = [];
        private bool 监视页已建;

        /// <summary> 第一次用到时挂数据源 + 把选项里存着的监视读进来(选项里存的是**文本**,引擎对象在这一侧建)</summary>
        private void 建监视页()
        {
            if (监视页已建) return;
            监视页已建 = true;
            监视列表.ItemsSource = 监视行集;
            foreach (string 文 in 选项.当前.监视)
                if (文.Trim().Length > 0) 监视表.Add(监视表达式C.解析(文));
            重建监视行();
            更新监视说明();
        }

        /// <summary> 按 `监视表` 重建界面那一列(增删时用;平时刷新是**原地改值**,不重建)</summary>
        private void 重建监视行()
        {
            监视行集.Clear();
            foreach (var 一条 in 监视表) 监视行集.Add(new 监视行C { 表达式 = 一条.原文 });
        }

        /// <summary> 把监视表写回选项(与界面动作同步落盘;写不出也不影响调试)</summary>
        private void 保存监视列表()
        {
            try
            {
                选项.当前.监视 = [.. 监视表.Select(x => x.原文)];
                选项.保存(选项.当前);
            }
            catch (Exception 异常) { 异常收集C.记($"保存监视表达式失败: {异常.Message}"); }
        }

        private void 更新监视说明()
        {
            监视说明.Text = 监视表.Count == 0
                ? "写一个变量名(可带 .字段),或者 `变量 == 值`(也认 `左 < 右` 这种两变量比);回车就加上去"
                : $"{监视表.Count} 条;每走一步自动重算(值变了会记进『变化』那一列)";
        }

        /// <summary>
        /// 重算所有监视表达式。快照已经由 `刷新集成调试视图` 取好了就传进来(省一次问机器);
        /// 单独调(加了/去了一条)时现取一份。
        /// </summary>
        private void 刷新监视(IReadOnlyList<变量读数C>? 读数们 = null, int 源码行 = 0)
        {
            try
            {
                建监视页();
                if (监视表.Count == 0) return;
                if (读数们 == null)
                {
                    var 快照 = 前端契约C.服务.调试.会话快照();
                    读数们 = 快照.变量;
                    源码行 = 快照.源码行;
                }
                // 行号缺省(单加一条时)就问一次"现在停在哪一行" —— 监视的"看得见"是按行算的
                if (源码行 <= 0) 源码行 = 前端契约C.服务.调试.会话快照().源码行;
                for (int i = 0; i < 监视表.Count && i < 监视行集.Count; i++)
                {
                    var 果 = 监视求值C.求值(监视表[i], 读数们, 源码行);
                    if (果.算出来了) 监视表[i].记历史(果.值文本);
                    监视行集[i].设为(new 监视行C
                    {
                        值 = 果.值文本,
                        类型 = 果.类型,
                        历史 = 监视表[i].历史文本(),
                        说明 = 果.说明,
                    });
                }
                监视页.Header = 源码行 > 0 ? $"监视({监视表.Count})· 第 {源码行} 行" : $"监视({监视表.Count})";
            }
            catch (Exception ex) { 异常收集C.记($"刷新监视失败: {ex.Message}"); }
        }

        /// <summary> 加一条监视(输入框里那段文本);空文本 / 写错都当场说清,不闷着 </summary>
        private void 加监视(string? 文本)
        {
            string 文 = (文本 ?? "").Trim();
            if (文.Length == 0) { 更新监视说明(); return; }
            if (监视表.Any(x => string.Equals(x.原文, 文, StringComparison.Ordinal)))
            {
                监视说明.Text = $"`{文}` 已经在监视里了";
                return;
            }
            var 一条 = 监视表达式C.解析(文);
            监视表.Add(一条);
            监视行集.Add(new 监视行C { 表达式 = 一条.原文 });
            监视输入框.Clear();
            保存监视列表();
            更新监视说明();
            if (!一条.条件.合法) 监视说明.Text = $"加上了,但这条写不通:{一条.条件.说明}";
            打开调试页(切到变量页: false);
            底部选卡.SelectedItem = 监视页;
            刷新监视();
        }

        private void 监视添加_Click(object sender, RoutedEventArgs e) => 加监视(监视输入框.Text);

        private void 监视输入框_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            加监视(监视输入框.Text);
        }

        /// <summary> 去掉选中的那一条</summary>
        private void 监视删除_Click(object sender, RoutedEventArgs e)
        {
            int 位 = 监视列表.SelectedIndex;
            if (位 < 0 || 位 >= 监视表.Count)
            {
                监视说明.Text = "先在列表里点一条,再点『去这一条』";
                return;
            }
            监视表.RemoveAt(位);
            监视行集.RemoveAt(位);
            保存监视列表();
            更新监视说明();
            刷新监视();
        }

        private void 监视清空_Click(object sender, RoutedEventArgs e)
        {
            监视表.Clear();
            监视行集.Clear();
            保存监视列表();
            更新监视说明();
        }

        /// <summary> 双击监视里的一条:把它填回输入框(想改一改再加回来,不必重新敲一遍)</summary>
        private void 监视表_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (监视列表.SelectedItem is not 监视行C 选中) return;
            监视输入框.Text = 选中.表达式;
            监视输入框.Focus();
            监视输入框.SelectAll();
        }

        /// <summary> "加监视表达式…"那条命令(编辑器右键 / 命令面板 / 快捷键都走它):
        /// 有选中的文字就拿它当默认值,否则拿光标下的词。 </summary>
        private void 执行加监视()
        {
            try
            {
                建监视页();
                var 编 = 当前编辑器;
                string 词 = 编?.取选中的词() ?? "";
                if (词.Trim().Length == 0) 词 = 编?.取光标下的词与后继() is { } 位 ? 位.词 : "";
                string? 文 = 输入窗口.问(this, "加监视表达式",
                    "要一直盯着的那一条(变量名,或者 `变量 运算符 变量或常量`):", 词,
                    "例:`i`、`总数 >= 100`、`左 < 右`、`记录.字段`。\n" +
                    "加好之后每走一步自动重算;停在断点上、逐步走的时候都能看。\n" +
                    "写下来会记住(存在 选项.json 里),下次打开还在。");
                if (文 == null) return;
                加监视(文);
            }
            catch (Exception 异常)
            {
                异常收集C.记($"加监视表达式失败: {异常.Message}");
                记异常框($"加监视表达式失败: {异常}");
            }
        }

        #endregion

    }
}
