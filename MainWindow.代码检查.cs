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
    /// <summary> 主窗口的一部分:代码检查(定时/工程级检查 + 问题表刷新 + 控制台输入) </summary>
    public partial class MainWindow : Window
    {
        #region 代码检查
        /// <summary> 问题页上的一行(界面绑定用:级别/文件/行/说明)</summary>
        public sealed class 问题行C
        {
            public string 级别 { get; set; } = "";
            public string 文件 { get; set; } = "";
            public int 行 { get; set; }
            public string 文本 { get; set; } = "";
            /// <summary> 这条属于哪个文件(双击跳转用;不显示)</summary>
            public string 全称 { get; set; } = "";

            /// <summary>
            /// P38:这一条当时的**整份源码**(一键修复按它算"哪一行哪一列换成什么")。
            /// 为什么带在行上:问题页是异步刷新的("停手 400ms"那一拍),修复要拿的是**同一次检查**看的那份文本 ——
            /// 现取一遍(编辑器里此刻的文本)有可能已经不是同一条问题了。
            /// </summary>
            public string 源码 { get; set; } = "";
        }

        private readonly List<问题行C> _问题行集 = [];
        private DispatcherTimer? _检查计时器;
        /// <summary> 上一次编译是不是成功(编不过的时候,语义类检查不可靠,只报形式类)</summary>
        private bool _上次编译成功 = true;
        /// <summary>
        /// 最近一次编译失败的异常(P23 起:**多错误报告合成的那一条**,一条条子错挂在 `子错` 里)。
        /// 编译通过时为 null。界面靠 `编译服务C.取诊断 / 取各条` 一条条标红、一条条列进『问题』页。
        /// </summary>
        private 编译异常C? _编译异常;
        /// <summary> 这一轮检查里**已经标到编辑器上**的那些编译错(键 = 文件|行|文本);剩下的进『问题』页兜底,不重复</summary>
        private readonly HashSet<string> _已标的编译错 = new(StringComparer.Ordinal);

        /// <summary>
        /// P38:问题表里那一行 → 它的**诊断项**(一键修复按它算方案)。
        /// 键就是绑到表上的那个对象本身(引用相等);每跑一遍检查清一次。
        /// </summary>
        private readonly Dictionary<问题行C, 诊断项C> _问题诊断 = new();
        /// <summary> 没打开工程时,"当前文件所在目录"临时当项目用(缓一个,别每 400ms 扫一次目录)</summary>
        private 项目项C? _临时项目;
        private string _临时项目目录 = "";
        /// <summary> 最近一次静默编译的结果(说明),显示在"问题"页摘要里 —— 好让你一眼看出"检查到底跑没跑、编没编过"</summary>
        private string _检查说明 = "";

        /// <summary>
        /// **上一次算"未使用函数"用的那一份**(同一份结果只扫一次)。
        /// H38-2 起类型是 `object?` —— 界面**不再声明 `汇编调试结果C` 这个类型**(攥住引擎对象就是 H38 要治的病);
        /// 这里只要"是不是同一份"这一个判断,比引用就够。
        /// </summary>
        private object? _未使用来源;
        /// <summary> 缓存的未使用报告(给 『问题』页与波浪线用) </summary>
        private 未使用报告C? _未使用报告;
        /// <summary> 缓存好的诊断项(报告 → 诊断项是纯映射,算一次就够;每敲一个字不该重算) </summary>
        private List<诊断项C>? _未使用诊断;

        /// <summary> 新造出来的编辑器挂上"文本变了 → 安排一次检查";顺便先查一遍(刚打开的文件也能马上有下划线)</summary>
        private void 编辑器_新造出来(编辑器 编)
        {
            编.文本变化 += 安排检查;
            安排检查();
        }

        /// <summary> 源码变了:停手 400ms 再检查一次(边写边查,但不至于每敲一个字就查)</summary>
        private void 安排检查()
        {
            _检查计时器 ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _检查计时器.Stop();
            _检查计时器.Tick -= 检查计时器_Tick;
            _检查计时器.Tick += 检查计时器_Tick;
            _检查计时器.Start();
            安排会话自存();          // P36:顺手把"现在这一屏"记进会话(防崩;见 MainWindow.会话记忆.cs)
        }

        private void 检查计时器_Tick(object? sender, EventArgs e)
        {
            if (_已关窗) return;          // H23:关窗之后不碰界面(队列里排着的那一拍也不放进来)
            _检查计时器?.Stop();
            静默编译看错误();       // 先悄悄编一次:打错了马上有红线(不碰 exe、不写编译页)
            跑一遍检查();
        }

        /// <summary>
        /// 静默编译一次,只为"看有没有错误":用**编辑器里当前的内容**(不是磁盘上的,你改完没保存也算),
        /// 结果只用来标红线 —— 不生成 exe、不写编译页、不影响"接着跑"的状态。
        /// 成功时把这份结果留在 `前端契约C.服务.编译.最近结果` 里(未使用扫描 / 转到定义读它,免得再编一遍)。
        /// </summary>
        private void 静默编译看错误()
        {
            try
            {
                var 项目 = 拿检查用的项目();
                if (项目 == null) { _编译异常 = null; _上次编译成功 = true; 前端契约C.服务.编译.清最近结果(); _检查说明 = "没找到要编的工程"; return; }
                var 文件表 = 取当前源码(项目);
                if (文件表.Count == 0) { _编译异常 = null; _上次编译成功 = true; 前端契约C.服务.编译.清最近结果(); _检查说明 = "工程里没有源文件"; return; }
                // H38-2:结果由 `汇编多文件带调试` 自己记进契约(界面不再存一份),这里只补"配的是哪张源码表"
                前端契约C.服务.编译.汇编多文件带调试(文件表, 优化开, 项目.堆字节数, 工程归属表(项目, 文件表), 项目.池字节数, 项目.内存字节数, 项目.软栈字节数, 项目.边界检查);
                前端契约C.服务.编译.设最近源码表(文件表);
                _编译异常 = null;
                _上次编译成功 = true;
                _检查说明 = $"编译通过({文件表.Count} 个文件)";
            }
            catch (编译异常C 错)
            {
                _上次编译成功 = false;
                前端契约C.服务.编译.清最近结果();      // 编不过:别拿上一次的旧边表冒充"现在的"
                _编译异常 = 错;
                int 处数 = 错.逐条().Count;
                _检查说明 = 处数 > 1
                    ? $"编译不过:{处数} 处错误(见底部『问题』页,双击能一条条跳)"
                    : (错.行号 ?? 0) > 0 ? $"编译不过:第 {错.行号} 行" : "编译不过(不知道在第几行)";
            }
            catch (Exception 异常)
            {
                // **别的异常也要看得见**:此前此处静默忽略 → 用户那边就是"编不过却一条红线都没有"
                _上次编译成功 = false;
                前端契约C.服务.编译.清最近结果();
                _编译异常 = new 编译异常C(
                    $"编译器内部错误:{异常.Message}(这一条是提示,不是你代码的错,请把这句话反馈)",
                    null, 1, null, 当前选卡?.全称 ?? "");
                _检查说明 = "编译器内部出错(已在第 1 行标红提示)";
            }
        }

        /// <summary>
        /// "检查"用哪个项目编:
        /// ① 启动项目里**确实有这个文件** → 就用启动项目(多文件语义照旧);
        /// ② 否则(没打开工程,或者看的是工程外的散文件)→ 把它**所在的目录**临时当工程
        ///    (优先读同目录的 .hbp,没有就扫目录里的 .hb)。
        /// 为什么要②:不然"编的不是你在看的文件" → 你删一个字符它也不报错,一条红线都没有。
        /// </summary>
        private 项目项C? 拿检查用的项目()
        {
            string 全称 = 当前选卡?.全称 ?? "";
            var 项目 = 解决方案.启动项目;
            if (项目 != null && 全称.Length > 0 && 项目.源文件.Any(f => 前端契约C.服务.宿主.是同一个文件(f.全称, 全称))) return 项目;
            if (项目 != null && 全称.Length == 0) return 项目;
            if (全称.Length == 0) return 项目;
            string 目录;
            try { 目录 = Path.GetDirectoryName(全称) ?? ""; } catch { return 项目; }
            if (目录.Length == 0 || !Directory.Exists(目录)) return 项目;
            if (_临时项目 != null && string.Equals(_临时项目目录, 目录, StringComparison.OrdinalIgnoreCase)) return _临时项目;
            _临时项目目录 = 目录;
            try
            {
                var 临 = new 项目项C { 名称 = Path.GetFileName(目录), 目录 = 目录 };
                临.刷新文件();
                _临时项目 = 临.源文件.Any() ? 临 : null;
            }
            catch { _临时项目 = null; }
            return _临时项目 ?? 项目;
        }

        /// <summary> 工程里参与编译的文件内容:打开着的用**编辑器里的**(没保存也算),其余读磁盘 </summary>
        private List<(string 名称, string 内容)> 取当前源码(项目项C 项目)
        {
            var 打开着的 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (编, 页) in 全部编辑器带页()) 打开着的[页.全称 ?? ""] = 编.获取代码();
            // **当前正在编辑的这一页一定要用编辑器里的文本**(没保存也算)。
            // 为什么单独补这一句:`全部编辑器带页()` 靠"标签页容器有没有造出来"来找编辑器,
            // 容器还没造出来时就找不到 → 于是这里退回读**磁盘上的旧文件** → 你删掉的字符根本没参与编译
            // → "编得没错,所以没有红线";而 F5 编译会先自动存盘,所以一编译就报出来了(用户实测到的现象)。
            if (当前编辑器 is { } 当前编 && 当前选卡?.全称 is { } 当前全称 && 当前全称.Length > 0)
                打开着的[当前全称] = 当前编.获取代码();

            var 表 = new List<(string 名称, string 内容)>();
            foreach (var f in 项目.源文件)
            {
                if (打开着的.TryGetValue(f.全称, out string? 文本)) 表.Add((f.全称, 文本));
                else if (File.Exists(f.全称)) 表.Add((f.全称, 前端契约C.服务.宿主.读源文件(f.全称)));   // T16:不是 UTF-8 就报编码
            }
            return 表;
        }

        /// <summary>
        /// 这次编译的"文件 → 工程"归属(与 `文件表` 一一对应),给 `程序集` 的可见性判据用。
        /// 有工程文件就按**整棵工程树**算:借来一起编译的库文件仍归库工程(库里的 `程序集` 才挡得住);
        /// 没有工程文件(扫目录那种)或工程文件读不动,就都算这个项目自己的(此时 `程序集` 与 `公共` 等价)。
        /// </summary>
        private List<string> 工程归属表(项目项C 项目, IReadOnlyList<(string 名称, string 内容)> 文件表)
        {
            if (文件表.Count == 0) return [];
            if (项目.有工程文件 && 项目.工程路径 is { Length: > 0 } 路径)
            {
                try { return 工程文件C.加载(路径).源文件归属([.. 文件表.Select(f => f.名称)], 工程文件C.读取工程树(路径)); }
                catch { /* 工程文件读不动:退回"都算本工程",编译本身会另报它的错 */ }
            }
            return [.. 文件表.Select(_ => 项目.名称)];
        }

        /// <summary>
        /// P37:**签名提示表**跟着"最近一次编译结果"走 —— 编译结果换了(或换了工程)就重灌一次。
        /// 与智能补全**同一份事实源**:函数签名来自 `名字表C.函数签名`(工程表 + 每个文件一份),
        /// 内建函数与方法的说明来自 `中间层指令C.说明表`(见 `引擎\签名表C.cs`)。
        /// 界面侧只做"什么时候灌":进 `跑一遍检查` 之前调一次 —— 那里正好是"编译结果刚更新"的时候。
        /// </summary>
        private object? _签名表来源;

        private void 刷新签名表()
        {
            try
            {
                var 编译 = 前端契约C.服务.编译.最近结果;
                if (ReferenceEquals(_签名表来源, 编译)) return;      // 同一份结果:不重灌
                _签名表来源 = 编译;
                var 表们 = new List<名字表C?>();
                if (编译 != null)
                {
                    表们.Add(编译.名字表);
                    foreach (var 段 in 编译.调试文件表) 表们.Add(段.名字表);
                }
                编辑器.设签名表(签名表C.建(表们));
            }
            catch (Exception 异常) { 异常收集C.记($"刷新签名表失败: {异常.Message}"); }
        }

        /// <summary>
        /// 对**当前打开的所有文件**跑一次检查:红 = 最近一次编译错误(**P23 起:多错一条条画**),
        /// 黄/绿 = 检查器的规则。结果同时喂给编辑器(画波浪线)和底部"问题"页。
        /// </summary>
        private void 跑一遍检查()
        {
            try
            {
                刷新签名表();          // P37:签名提示跟着这一份编译结果走(同一份不重灌)
                _问题行集.Clear();
                _问题诊断.Clear();
                _已标的编译错.Clear();
                // 当前正在看的那个文件一定要查(用户就盯着它);控件还没造出来的标签页跳过
                if (当前编辑器 is { } 当前编 && 当前选卡 is { } 当前页)
                {
                    // 这个标签页没带路径(比如还没存过的新文件):唯一的线索就是它,红线也画在它身上,总比一条都不画强
                    string 这个全称 = 当前页.全称 ?? "";
                    if (这个全称.Length == 0 && _编译异常 is { } 无路径错) 这个全称 = 无路径错.文件 ?? "";
                    查一个文件(当前编, 这个全称, 是当前页: true);
                }
                foreach (var (编, 页) in 全部编辑器带页())
                {
                    if (ReferenceEquals(编, 当前编辑器)) continue;
                    查一个文件(编, 页.全称 ?? "", 是当前页: false);
                }
                // P23:错在没打开的文件里(下划线画不了)也要在"问题"页**一条条**列出来(双击能跳过去)。
                // 已经标到编辑器上的那些不再重复(键 = 文件|行|文本)。
                foreach (var (全称, 行, 文本) in 前端契约C.服务.编译.取各条(_编译异常))
                {
                    if (_已标的编译错.Contains(键(全称, 行, 文本))) continue;
                    _问题行集.Add(new 问题行C
                    {
                        级别 = "错误",
                        文件 = Path.GetFileName(全称),
                        全称 = 全称,
                        行 = 行,
                        文本 = 文本,
                        源码 = 取该文件源码兜底(全称),      // P38:这一条没法一键修复(编译错误没有通法),但字段照给
                    });
                }
                刷新问题页();
            }
            catch (Exception 异常)
            {
                记异常框($"代码检查出错: {异常.Message}");
            }
        }

        /// <summary> "已经标到编辑器上"的键(文件|行|文本)</summary>
        private static string 键(string 全称, int 行, string 文本) => $"{全称}\u0001{行}\u0001{文本}";

        /// <summary>
        /// P38:错在**没打开的文件**里时,那一行问题也要有源码(一键修复按它算)。
        /// 打开的页直接取编辑器里的文本(没存盘也算),否则读盘;读不到给空串(那种情况本来也没有一键修复)。
        /// </summary>
        private string 取该文件源码兜底(string 全称)
        {
            try
            {
                foreach (var (编, 页) in 全部编辑器带页())
                    if (前端契约C.服务.宿主.是同一个文件(页.全称, 全称)) return 编.获取代码();
                return File.Exists(全称) ? 前端契约C.服务.宿主.读源文件(全称) : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 查一个文件:红线(编译错误)+ 黄绿线(检查器规则),喂给编辑器与"问题"页。
        /// P23 起编译错误是**一条条**画的(多错误报告里的每一条都按自己的行号落位);
        /// 都不落在这一页、但这一页是当前页时,补一条"夹进范围"的兜底 —— **宁可多标一条,也不能什么都没有**。
        /// </summary>
        private void 查一个文件(编辑器 编, string 全称, bool 是当前页)
        {
            string 源码 = 编.获取代码();
            var 表 = new List<诊断项C>();
            int 这一页行数 = 源码.Count(c => c == '\n') + 1;
            var (编译诊断, _) = 前端契约C.服务.编译.取诊断(_编译异常, 全称, 这一页行数, 是当前页);
            表.AddRange(编译诊断);
            foreach (var 一条 in 编译诊断)
                _已标的编译错.Add(键(全称, 一条.行, 一条.文本));      // 问题页兜底那轮不重复列
            表.AddRange(前端契约C.服务.编译.检查(源码, _上次编译成功 && _编译异常 == null));
            // 未使用函数(引擎侧扫出来的,同一份编译结果只扫一次):只把**属于这一页文件**的那几条挂上去。
            // 走的是同一个 `表` —— 所以它同时进波浪线与底部『问题』页,和别的诊断完全一样地显示与双击跳转。
            foreach (var 未使用 in 取未使用诊断())
                if (前端契约C.服务.宿主.是同一个文件(未使用.文件, 全称)) 表.Add(未使用);
            编.设置诊断(表);
            foreach (var d in 表)
            {
                var 问题行 = new 问题行C
                {
                    级别 = d.级别 switch { 诊断级别.错误 => "错误", 诊断级别.警告 => "警告", _ => "提示" },
                    文件 = Path.GetFileName(全称),
                    全称 = 全称,
                    行 = d.行,
                    文本 = d.文本,
                    源码 = 源码,          // P38:一键修复按**这一次检查看的这份文本**算改动
                };
                _问题行集.Add(问题行);
                _问题诊断[问题行] = d;
            }
        }

        /// <summary>
        /// 取最近一次编译的"未使用函数"诊断项(纯映射,见 `引擎\未使用扫描C.cs` 的 `未使用诊断C`)。
        /// **同一份编译结果只扫一次**:每敲一个字都会走到这里,不能每次都重扫一遍;编译结果换了才重算。
        /// 文件表只为判"对外接口"里的可见性前缀 —— 本轮编译用的就是 `取当前源码` 那一份。
        /// 对外接口栏按 Q4c 定的口径**默认不报**(理由见 `未使用诊断C` 的类注释)。
        /// </summary>
        private List<诊断项C> 取未使用诊断()
        {
            if (前端契约C.服务.编译.最近结果 == null)
            {
                _未使用来源 = null;
                _未使用报告 = null;
                _未使用诊断 = null;
                return [];
            }
            if (!ReferenceEquals(_未使用来源, 前端契约C.服务.编译.最近结果))
            {
                // 用**那次编译同一份源码表**(`设最近源码表` 成对记下的);万一没有(极早的初始化路径)就现取一份
                var 文件表 = 前端契约C.服务.编译.最近源码表;
                if (文件表 == null)
                {
                    var 项目 = 拿检查用的项目();
                    文件表 = 项目 != null ? 取当前源码(项目) : null;
                }
                _未使用报告 = 未使用扫描C.扫描(前端契约C.服务.编译.最近结果, 文件表);
                _未使用诊断 = 未使用诊断C.映射(_未使用报告);
                _未使用来源 = 前端契约C.服务.编译.最近结果;
            }
            return _未使用诊断 ?? [];
        }

        /// <summary>
        /// C19:给编辑器算补全用的两片名字表 —— 当前文件那份(变量按作用域)+ 工程级那份(函数/结构/可见性/参数表)。
        /// 没编译过就返回 null,编辑器自己退回现场源码扫(见 `编译服务C.取补全`)。文件名用 `宿主与文件服务C.是同一个文件` 那套判法:
        /// 名字表里是源文件表给的写法(可能是相对路径),标签页给的是全称,两边都对得上。
        /// </summary>
        private 补全来源C? 取补全名字表(string? 文件全称)
        {
            try
            {
                var 编译 = 前端契约C.服务.编译.最近结果;
                if (编译 == null) return null;
                名字表C? 本文件 = null;
                foreach (var 段 in 编译.调试文件表)
                    if (前端契约C.服务.宿主.是同一个文件(段.文件, 文件全称)) { 本文件 = 段.名字表; break; }
                return new 补全来源C { 本文件表 = 本文件, 工程表 = 编译.名字表 };
            }
            catch { return null; }      // 算名字表绝不能把编辑搅停:拿不到就退回现场扫
        }

        private void 刷新问题页()
        {
            try
            {
                var 显示 = _问题行集
                    .Where(x => (x.级别 == "错误" && 看错误.IsChecked == true)
                             || (x.级别 == "警告" && 看警告.IsChecked == true)
                             || (x.级别 == "提示" && 看提示.IsChecked == true))
                    .OrderBy(x => x.全称).ThenBy(x => x.行)
                    .ToList();
                问题表.ItemsSource = 显示;
                int 错 = _问题行集.Count(x => x.级别 == "错误");
                int 警 = _问题行集.Count(x => x.级别 == "警告");
                int 提 = _问题行集.Count(x => x.级别 == "提示");
                问题摘要.Text = 错 + 警 + 提 == 0
                    ? "没有发现问题 ✔"
                    : $"错误 {错} · 警告 {警} · 提示 {提}";
                问题摘要.Text += $" (已检查 {全部编辑器带页().Count()} 个文件" +
                    (_检查说明.Length > 0 ? $";{_检查说明}" : "") + ")";
                问题页.Header = 错 + 警 + 提 == 0 ? "问题" : $"问题({错 + 警 + 提})";
                // 有错误就自动把"问题"页露出来(哪怕你从没打开过它)—— 下划线是提示,这里才是"看得见的那一条"
                if (错 > 0 && 问题页.Visibility != Visibility.Visible) 设置页签(问题页, true);
            }
            catch { }
        }

        private void 问题筛选_Changed(object sender, RoutedEventArgs e) => 刷新问题页();

        /// <summary> 双击问题页的一条:跳到那个文件那一行</summary>
        private void 问题表_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (问题表.SelectedItem is not 问题行C 行) return;
            打开文件并跳到(行.全称, 行.行);
        }

        /// <summary> 打开(或切到)某个文件,并把光标放到指定行</summary>
        private void 打开文件并跳到(string 全称, int 行)
        {
            try
            {
                var 页 = 全部编辑器带页().FirstOrDefault(x => string.Equals(x.页.全称, 全称, StringComparison.OrdinalIgnoreCase));
                if (页.编 == null)
                {
                    输出框.AppendText($"这个文件已经不在打开列表里了:{全称}\n");
                    return;
                }
                主视图模型.选中页 = 页.页;
                页.编.跳到行(行);
            }
            catch (Exception 异常) { 记异常框($"跳转失败: {异常.Message}"); }
        }

        #endregion
    }
}
