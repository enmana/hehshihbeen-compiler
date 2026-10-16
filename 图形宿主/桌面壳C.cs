using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace hehshihbeen_compiler.图形宿主
{
    /// <summary>
    /// **桌面壳**(R1-6 批 1):把能力层攒下的绘制命令画到一个**真窗口**上。
    ///
    /// 三条设计口径:
    /// ① **界面线程是分开的** —— HB 程序在调用线程上同步跑(虚拟机是同步的),壳自己在一条
    ///    后台线程上起 Avalonia 消息循环;两边靠 `Dispatcher` 交接。这样宿主一行都不用改结构。
    /// ② **懒启动**:第一次 `打开窗口` 才把界面线程起来(`确保起界面`)。没用图形的程序
    ///    连 Avalonia 都不会碰 —— 独立 exe 还是纯控制台。
    /// ③ **开不出来如实说** = 抛异常,能力层把它转成状态码(`打开窗口` 给 `假`),**不让程序崩**。
    ///
    /// 平台口径:Windows / Linux 上消息循环跑在副线程没问题;macOS 要求主线程(AppKit),
    /// 批 1 只保证"能编过 + Windows/Linux 能开窗",跑到 macOS 上要改成"程序下副线程"(记在 `未做任务清单.md` §19)。
    /// </summary>
    public sealed class 桌面壳C : 图形呈现口C
    {
        private readonly object _锁 = new();
        private Thread? _界面线程;
        private Exception? _起不来;
        private Window? _窗口;
        private 画布C? _画布;

        /// <summary> 界面线程起好了没有(诊断看它;没起来时 `打开窗口` 抛) </summary>
        public bool 界面起来了 => 界面就绪.IsSet;

        /// <summary> 界面线程进消息循环了(App 的 `OnFrameworkInitializationCompleted` 里点亮)</summary>
        internal static readonly ManualResetEventSlim 界面就绪 = new(false);
        /// <summary> 界面线程退出了(消息循环结束 / 起不来)</summary>
        internal static readonly ManualResetEventSlim 界面结束 = new(false);

        /// <summary> 起界面线程(幂等)。起不来就把异常记下,等 `打开窗口` 抛给能力层 </summary>
        private void 确保起界面()
        {
            lock (_锁)
            {
                if (_界面线程 != null) return;
                界面就绪.Reset();
                界面结束.Reset();
                _界面线程 = new Thread(() =>
                {
                    try
                    {
                        AppBuilder.Configure<壳应用C>()
                            .UsePlatformDetect()
                            .WithInterFont()
                            .LogToTrace()
                            .StartWithClassicDesktopLifetime([]);
                    }
                    catch (Exception ex) { _起不来 = ex; }
                    finally { 界面结束.Set(); }
                })
                {
                    IsBackground = true,
                    Name = "HB图形宿主界面",
                };
                // Windows 上要 STA(界面线程的老规矩);别的平台没这个 API —— 用平台判断包住,别在 Linux 上被拦
                if (OperatingSystem.IsWindows())
                {
                    try { _界面线程.SetApartmentState(ApartmentState.STA); } catch { }
                }
                _界面线程.Start();
                // 等到"消息循环真的起来了"再往下走:没起来就别假装窗口开了(15 秒够任何一台机器起 Avalonia)
                if (!界面就绪.Wait(TimeSpan.FromSeconds(15)) && _起不来 == null)
                    _起不来 = new TimeoutException("桌面壳 15 秒还没进入消息循环(界面线程没起来)");
            }
        }

        private void 在界面上(Action 做)
        {
            if (_起不来 != null) throw new InvalidOperationException("窗口打不开:" + _起不来.Message, _起不来);
            Dispatcher.UIThread.Invoke(做);
        }

        /// <summary>
        /// **等这一帧真的画出去**(与 WPF 呈现口同一套口径,用户 2026-09-24 实测:画一帧就关窗的程序
        /// 窗口"一闪就没")。"改视觉 / 关窗"排在 `Render` 那一档,再排一个**更低**的空活
        /// (`Background` < `Render`)—— 它跑到了,布局与场景更新就已经做完。
        /// 只在**程序线程**上排(界面线程上排会自己等自己)。
        /// </summary>
        private void 等这一帧画完()
        {
            try { Dispatcher.UIThread.Invoke(() => { }, DispatcherPriority.Background); }
            catch { /* 消息循环已经收了(窗口关掉之后):没什么可等的 */ }
        }

        public void 打开窗口(string 标题, int 宽, int 高)
        {
            确保起界面();
            if (_起不来 != null) throw new InvalidOperationException("窗口打不开:" + _起不来.Message, _起不来);
            int 画宽 = 宽 <= 0 ? 320 : 宽;
            int 画高 = 高 <= 0 ? 200 : 高;
            在界面上(() =>
            {
                if (_窗口 != null) return;
                _画布 = new 画布C();
                _窗口 = new Window
                {
                    Title = 标题,
                    Width = 画宽,
                    Height = 画高,
                    Background = Brushes.Black,
                    Content = _画布,
                };
                // 输入源:键盘 / 鼠标 / 关窗请求都投进能力层的事件队列。
                // R1-6 批 2:键码 / 鼠标键**不直接把 Avalonia 的枚举值丢过去** —— 先译成 HB 口径
                // (`图形键C`;Avalonia 的 Key 枚举编号是框架实现细节,随版本动,不能当对外接口)。
                _窗口.KeyDown += (_, e) => 图形宿主C.投事件(图形事件C.按键, 译键码(e.Key), 0, 0);
                _窗口.PointerMoved += (_, e) =>
                {
                    var 位 = e.GetPosition(_窗口);
                    图形宿主C.投事件(图形事件C.移动, 0, (int)位.X, (int)位.Y);
                };
                _窗口.PointerPressed += (_, e) =>
                {
                    var 位 = e.GetPosition(_窗口);
                    图形宿主C.投事件(图形事件C.按下, 译鼠标键(e.GetCurrentPoint(_窗口).Properties.PointerUpdateKind), (int)位.X, (int)位.Y);
                };
                // 按下 ↔ 抬起成对:`点击` = 同一个键码的两条事件(即时模式的按钮就靠这一对判"松开才算点中")
                _窗口.PointerReleased += (_, e) =>
                {
                    var 位 = e.GetPosition(_窗口);
                    图形宿主C.投事件(图形事件C.抬起, 译鼠标键(e.GetCurrentPoint(_窗口).Properties.PointerUpdateKind), (int)位.X, (int)位.Y);
                };
                _窗口.Closing += (_, _) => 图形宿主C.投事件(图形事件C.关窗, 0, 0, 0);
                _窗口.Show();
                _窗口.Activate();
            });
            等这一帧画完();          // 首帧:开窗之后先把这一趟布局做掉,再让程序往下画
        }

        /// <summary>
        /// **Avalonia 的键 → HB 键码**(R1-6 批 2)。认不出的键给 `图形键C.无`(不是错:键盘上总有怪键)。
        ///
        /// 三族连续码按算术翻(字母 65~90 / 数字 48~57 / F1~F12 = 112~123),
        /// 其余按名字一个个对上 —— 这张表就是"Avalonia 不认识 HB"的那道隔离层。
        /// </summary>
        internal static int 译键码(Key 键)
        {
            if (键 is >= Key.A and <= Key.Z) return 图形键C.字母最小 + (键 - Key.A);
            if (键 is >= Key.D0 and <= Key.D9) return 图形键C.数字最小 + (键 - Key.D0);
            if (键 is >= Key.F1 and <= Key.F12) return 图形键C.功能键(键 - Key.F1 + 1);
            return 键 switch
            {
                Key.Back => 图形键C.退格,
                Key.Tab => 图形键C.制表,
                Key.Enter => 图形键C.回车,
                Key.LeftShift or Key.RightShift => 图形键C.上档,
                Key.LeftCtrl or Key.RightCtrl => 图形键C.控制,
                Key.LeftAlt or Key.RightAlt => 图形键C.替换,
                Key.Escape => 图形键C.退出,
                Key.Space => 图形键C.空格,
                Key.PageUp => 图形键C.上一页,
                Key.PageDown => 图形键C.下一页,
                Key.Home => 图形键C.首页,
                Key.End => 图形键C.末页,
                Key.Left => 图形键C.左,
                Key.Up => 图形键C.上,
                Key.Right => 图形键C.右,
                Key.Down => 图形键C.下,
                Key.Insert => 图形键C.插入,
                Key.Delete => 图形键C.删除,
                _ => 图形键C.无,
            };
        }

        /// <summary> Avalonia 的鼠标键 → HB 键码(`图形键C.鼠标左键/中键/右键`);认不出给 0 </summary>
        internal static int 译鼠标键(PointerUpdateKind 种) => 种 switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => 图形键C.鼠标左键,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => 图形键C.鼠标中键,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => 图形键C.鼠标右键,
            _ => 0,
        };

        public void 关闭窗口()
        {
            等这一帧画完();          // 还没画出去的那一帧先画掉,再关窗
            在界面上(() =>
            {
                _窗口?.Close();
                _窗口 = null;
                _画布 = null;
            });
        }

        public void 呈现(IReadOnlyList<绘制命令C> 命令)
        {
            // **空帧不许擦屏**(第236批 H40,与 IDE 那个呈现口同一套口径):干净的一帧照样 `刷新()`
            // (帧节拍照走),但一条命令都没有 —— 动一下画布就把上一帧擦成黑底(空的那一份 = 空屏)。
            if (命令.Count == 0) return;
            var 快照 = 命令.Select(x => x.复制()).ToArray();     // 能力层的表是真源,界面侧拿一份自己的
            在界面上(() => _画布?.设一帧(快照));
            等这一帧画完();          // `刷新()` 回来就真在屏幕上(与 IDE 那个呈现口同一套口径)
        }

        /// <summary> 让出一帧。壳这边就是真睡一会儿(界面线程自己会重绘);睡完顺手收一下已投的事件 </summary>
        public void 等待(int 毫秒)
        {
            if (毫秒 > 0) Thread.Sleep(毫秒);
        }

        /// <summary>
        /// **把一段文字画进一块像素**(控 1-①,号 78 `贴文字到位图`):这一口用 Avalonia 的
        /// `RenderTargetBitmap` 把 `FormattedText` 光栅化,再把 `Bgra8888` 的字节拆回 `0xAARRGGBB`。
        ///
        /// <para>⚠ **只画"字的形状部分"**:`DrawText` 只写它覆盖到的像素 ⇒ 界面线程上先把这块像素
        /// 铺成"全透明"再画,于是"字没碰到的地方"读回来 alpha = 0 —— 与 WPF 那一口**同一个判据**
        /// (两边都靠 `a == 0` 判"这个像素不是字"),两边行为对齐就不用各记一套。</para>
        ///
        /// <para>⚠ 界面线程没起来(自检那条腿没开窗)时**如实返回假**:宁可"没有字",
        /// 也不在这条线程上硬起一套 Avalonia 渲染栈。</para>
        /// </summary>
        public bool 把文字画进像素(int x, int y, string 文本, uint 字色, int 字号, uint[] 像素, int 宽, int 高)
        {
            if (文本.Length == 0 || 宽 <= 0 || 高 <= 0 || 像素.Length < 宽 * 高) return false;
            if (!界面起来了) return false;
            bool 成了 = false;
            try
            {
                在界面上(() =>
                {
                    var 光栅 = new RenderTargetBitmap(new PixelSize(宽, 高), new Vector(96, 96));
                    using (var 画 = 光栅.CreateDrawingContext())
                    {
                        var 刷 = new SolidColorBrush(Color.FromUInt32(字色));
                        var 文字 = new FormattedText(文本, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            Typeface.Default, 字号, 刷);
                        画.DrawText(文字, new Point(x, y));
                    }
                    // ⚠ `RenderTargetBitmap` **没有 `Lock()`**(那个口在 `WriteableBitmap` 上)——
                    // 读回来的正路是 `Bitmap.CopyPixels(目标帧缓冲)`:官方 XML 里写着"need 时连像素格式
                    // 与 alpha 格式一起转",所以先建一张同尺寸的 `WriteableBitmap` 当目标、再锁它读。
                    using var 读回 = new WriteableBitmap(new PixelSize(宽, 高), new Vector(96, 96),
                        PixelFormat.Bgra8888, AlphaFormat.Premul);
                    using (var 帧 = 读回.Lock()) 光栅.CopyPixels(帧);
                    using var 帧读 = 读回.Lock();
                    var 行 = new byte[帧读.RowBytes];
                    for (int 行号 = 0; 行号 < 高; 行号++)
                    {
                        Marshal.Copy(帧读.Address + 行号 * 帧读.RowBytes, 行, 0, 行.Length);
                        for (int 列 = 0; 列 < 宽; 列++)
                        {
                            byte b = 行[列 * 4], g = 行[列 * 4 + 1], r = 行[列 * 4 + 2], a = 行[列 * 4 + 3];
                            if (a == 0) continue;          // 不是字碰到的像素 ⇒ 原样留着
                            像素[行号 * 宽 + 列] = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                        }
                    }
                    成了 = true;
                });
            }
            catch { return false; }
            return 成了;
        }
    }

    /// <summary> 空应用:只要一个 Fluent 主题 + "消息循环起来了"的信号(窗口由 `桌面壳C` 按需创建)</summary>
    internal sealed class 壳应用C : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());

        public override void OnFrameworkInitializationCompleted()
        {
            base.OnFrameworkInitializationCompleted();
            桌面壳C.界面就绪.Set();     // 到这儿平台已经起好、消息循环马上就要转:可以安全地往界面线程投活了
        }
    }

    /// <summary> 画布:把这一帧的命令按顺序画上去(即时模式 —— 每帧整份重画,没有保留状态)</summary>
    internal sealed class 画布C : Control
    {
        private IReadOnlyList<绘制命令C> _一帧 = [];

        /// <summary>
        /// **`贴位图` 那一块的家**(控 1-⑥):按帧复用同一张 Avalonia 位图,尺寸变了才重建
        /// (与 IDE 那个 WPF 画布同一套口径 —— 每帧新建一张 480×520 的图,省下来的时间又花回 GC 上)。
        /// </summary>
        private WriteableBitmap? _贴块;
        /// <summary> 已铺进 `_贴块` 的是哪一块(`_一帧` 里那一条的引用;同一块不重铺)</summary>
        private uint[]? _贴块源;

        public void 设一帧(IReadOnlyList<绘制命令C> 一帧)
        {
            _一帧 = 一帧;
            InvalidateVisual();
        }

        private static Color 颜色(uint 值) => Color.FromUInt32(值);

        /// <summary>
        /// **把一块 HB 位图铺进 Avalonia 位图**(控 1-⑥):字节摆法走引擎那一份口径(`位图字节C.铺进`),
        /// 这里只负责"字节 ⇒ `Bgra8888`"。`Bgra8888 + Premul` 的字节序也是 `B,G,R,A`,
        /// 与 `C后端\运行时.c` 喂给 `SetDIBitsToDevice` 的那一份、以及 WPF 那个 `Pbgra32` **逐字节一样**。
        ///
        /// ⚠ 行距用 `Marshal.Copy` 原样搬:`帧缓冲.RowBytes` 是平台给的(可能带补白),
        /// **不许假设它等于 `宽 × 4`** —— 假设了以后在别的平台上就是斜的。
        /// </summary>
        private void 铺贴块(绘制命令C 条)
        {
            var 像素 = 条.像素!;
            int 宽 = Math.Max(1, 条.宽), 高 = Math.Max(1, 条.高);
            if (_贴块 == null || _贴块.PixelSize.Width != 宽 || _贴块.PixelSize.Height != 高)
            {
                _贴块?.Dispose();
                _贴块 = new WriteableBitmap(new PixelSize(宽, 高), new Vector(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Premul);
                _贴块源 = null;                     // 换了尺寸 = 上一块的内容作废
            }
            if (ReferenceEquals(_贴块源, 像素)) return;      // 同一块(这一帧别重铺)
            int 格数 = Math.Min(像素.Length, 宽 * 高);
            using (var 帧缓冲 = _贴块.Lock())
            {
                int 行字节 = 帧缓冲.RowBytes;
                var 行 = new byte[位图字节C.偏移(宽)];
                for (int y = 0; y < 高; y++)
                {
                    int 本行 = Math.Min(宽, Math.Max(0, 格数 - y * 宽));
                    if (本行 > 0)
                    {
                        位图字节C.铺进(行, y * 宽, 像素, 本行);      // 从源里那一行起,铺进这一行
                        Marshal.Copy(行, 0, 帧缓冲.Address + y * 行字节, 位图字节C.偏移(本行));
                    }
                }
            }
            _贴块源 = 像素;
        }

        /// <summary>
        /// **把一整块像素一次贴到窗口上**(控 1-⑥;用户报的"独立宿主那条腿真窗口上一个像素都不出"
        /// 就是这里以前没有 `贴位图` 这一档)。贴上去**不透明**(施工图 §2.4 拍板 a),
        /// 半透明像素按自己的 alpha 混 —— 那是 `Premul` 自带的口径,不是额外加的合成规则。
        /// </summary>
        private void 贴一块(绘制命令C 条, DrawingContext 画)
        {
            铺贴块(条);
            if (_贴块 == null) return;
            // 长宽给足贴到哪一块**不改命令**(那一帧的表是两个呈现口共用的只读快照);
            // `Stretch.None` ⇒ 一个源像素对屏幕上一个像素,不做缩放(缩放会把小图糊掉)
            画.DrawImage(_贴块, new Rect(0, 0, 条.宽, 条.高), new Rect(条.x, 条.y, 条.宽, 条.高));
        }

        public override void Render(DrawingContext 画)
        {
            foreach (var 条 in _一帧)
            {
                var 笔 = new SolidColorBrush(颜色(条.颜色));
                switch (条.动作)
                {
                    case "清屏":
                        画.FillRectangle(笔, new Rect(Bounds.Size));
                        break;
                    case "画矩形":
                        画.FillRectangle(笔, new Rect(条.x, 条.y, Math.Max(0, 条.宽), Math.Max(0, 条.高)));
                        break;
                    case "画线":
                        画.DrawLine(new Pen(笔), new Point(条.x, 条.y), new Point(条.x2, 条.y2));
                        break;
                    case "画文字":
                        if (条.文本.Length > 0)
                        {
                            var 文字 = new FormattedText(条.文本, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                Typeface.Default, 14, 笔);
                            画.DrawText(文字, new Point(条.x, 条.y));
                        }
                        break;
                    // R4-12 的位图那一条(控 1-⑥ 补):一整块像素**一次画上去** ——
                    // 这一族要的就是"几百次往返压成一次",所以这里**不许**退化成逐像素 `FillRectangle`
                    case "贴位图":
                        if (条.像素 != null) 贴一块(条, 画);
                        break;
                }
            }
        }
    }
}
