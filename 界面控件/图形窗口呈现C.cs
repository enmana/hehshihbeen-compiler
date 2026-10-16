using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// **IDE 里的图形呈现口**(R1-6 批 1 尾巴 ③ 接线;用户 2026-09-23 口径:"就像 VS 的 F5 ——
    /// 按了 F5 就能看见窗口,还能单步调试")。
    ///
    /// **为什么 IDE 自己挂一个口,而不是起 `HB宿主` 子进程**:用户要的是"看得见窗口 **并且** 断点 / 单步照旧" ——
    /// 子进程那条路(桌面壳 Avalonia)是**另一个进程**,IDE 的调试器插不进去(断点 / 单步 / 变量面板全用不上)。
    /// 引擎那层早就把"攒命令 + 收事件"与"画在哪儿"分开了(`图形呈现口C`),这里只补最后一步:
    /// 把一帧命令画进 **IDE 自己的 WPF 窗口** —— 于是程序还在**进程内**那条虚拟机上跑,调试照旧。
    ///
    /// 线程口径(与桌面壳同一套):
    /// · HB 程序在**后台线程**上跑(H22),本口在**界面线程**上开窗 / 画面;
    /// · `呈现` 先把命令**拷一份**再跨界(`Dispatcher.Invoke`)—— 能力层那张表是真源,界面侧不碰它;
    /// · `等待(毫秒)` 真睡(睡的正是跑程序那条后台线程,界面不冻;无头那条不睡,所以自检才确定)。
    ///
    /// 平台口径:WPF 只有 Windows —— 这里是**呈现口的第二个实现**(第一份是独立 exe / 跨平台那个 Avalonia 桌面壳)。
    ///
    /// **看得见 + 有反应(用户 2026-09-24 实测报的两条)**:① 示例14/16 的窗口"一闪就没" ——
    /// 开窗**等首帧真上屏**(`ContentRendered`)、每次 `刷新` / `关闭窗口` **等那一帧真画出去**;
    /// ② 示例15"按任意键 / 点鼠标没反应" —— 画布**可聚焦** + 开窗后 `Focus()` + 窗口每次激活都把焦点收回来
    /// (WPF 的键盘事件只送给有焦点的元素);屏上的可见反馈由程序自己画(示例15 现在把事件数画在窗口上)。
    /// </summary>
    public sealed class 图形窗口呈现C : 图形呈现口C
    {
        private readonly Dispatcher _界面线程;
        private Window? _窗口;
        private 图形画布C? _画布;

        /// <summary> 首帧最多等多久(毫秒)。等到 = 窗口真上屏;等不到也照旧往下走(别把用户的程序卡在这儿) </summary>
        private const int 首帧预算毫秒 = 3000;

        /// <summary> 文字档:等宽 + 单行(与画布那份、以及 `画文字` 的字号口径同一处常量)</summary>
        private static readonly Typeface 等宽 = new("Consolas");

        public 图形窗口呈现C(Dispatcher 界面线程) => _界面线程 = 界面线程;

        /// <summary> 窗口现在开着没有(诊断用;关窗之后窗口对象就丢掉,下一次 `打开窗口` 重新建) </summary>
        public bool 窗口开着 => _窗口 != null;

        /// <summary> 界面上的活一律回界面线程做(程序在后台线程上跑;万一将来在界面线程上单步,就地做掉不再派发) </summary>
        private void 在界面上(Action 做)
        {
            if (_界面线程.CheckAccess()) 做();
            else _界面线程.Invoke(做);
        }

        /// <summary>
        /// **等这一帧真的画出去**(用户 2026-09-24 实测:示例14/16 的窗口"一闪就没" —— 根因是
        /// 程序画完一帧马上 `关闭窗口()`,而界面线程里那一帧还排在队列上没画)。
        ///
        /// 判据:`InvalidateVisual()` 会往界面线程排一个 **`DispatcherPriority.Render`** 的重绘回调,
        /// 所以再排一个**优先级更低**的空活(`Background` < `Render`)—— 它跑到了,那一帧就已经画过。
        /// 只在**程序线程**上排(在界面线程上排会自己等自己);`关闭窗口` / `呈现` 都过这一道。
        /// </summary>
        private void 等这一帧画完()
        {
            if (_界面线程.CheckAccess()) return;
            _界面线程.Invoke(() => { }, DispatcherPriority.Background);
        }

        public void 打开窗口(string 标题, int 宽, int 高)
        {
            int 画宽 = 宽 <= 0 ? 320 : 宽;
            int 画高 = 高 <= 0 ? 200 : 高;
            var 首帧 = new ManualResetEventSlim(false);
            在界面上(() =>
            {
                if (_窗口 != null) { 首帧.Set(); return; }
                // 画布**可聚焦**:WPF 的键盘事件只送给"有键盘焦点的元素",而一块普通画布默认拿不到焦点 ——
                // 不设这一格、不开窗后 `Focus()`,用户按任意键一条 `按键` 事件都收不到(示例15"按键没反应"的那一半)。
                _画布 = new 图形画布C { Focusable = true };
                _窗口 = new Window
                {
                    Title = 标题,
                    Width = 画宽,
                    Height = 画高,
                    Background = Brushes.Black,
                    Content = _画布,
                };
                // 首帧画完 = 窗口真在屏幕上(用户实测:画一帧就关的程序,以前窗口根本没露面)
                _窗口.ContentRendered += (_, _) => 首帧.Set();
                // 输入源:键盘 / 鼠标 / 关窗请求都投进能力层的事件队列。
                // **先译成 HB 键码再投**(`图形键C`;WPF 的 Key / MouseButton 枚举是框架实现细节,
                // 随版本动,不能当对外接口 —— 与桌面壳 `译键码` 同一条纪律)。
                // `Key.System` 是 WPF 把 Alt 组合键藏起来的那一档,要取 `SystemKey` 才是真键。
                _窗口.KeyDown += (_, e) =>
                    前端契约C.服务.图形.投事件(图形事件C.按键, 译键码(e.Key == Key.System ? e.SystemKey : e.Key), 0, 0);
                _窗口.MouseMove += (_, e) =>
                {
                    var 位 = e.GetPosition(_窗口);
                    前端契约C.服务.图形.投事件(图形事件C.移动, 0, (int)位.X, (int)位.Y);
                };
                _窗口.MouseDown += (_, e) =>
                {
                    var 位 = e.GetPosition(_窗口);
                    前端契约C.服务.图形.投事件(图形事件C.按下, 译鼠标键(e.ChangedButton), (int)位.X, (int)位.Y);
                };
                // 按下 ↔ 抬起成对(点击 = 同一键码的两条事件:即时模式的按钮靠这一对判"松开才算点中")
                _窗口.MouseUp += (_, e) =>
                {
                    var 位 = e.GetPosition(_窗口);
                    前端契约C.服务.图形.投事件(图形事件C.抬起, 译鼠标键(e.ChangedButton), (int)位.X, (int)位.Y);
                };
                // 关窗:投一条关窗事件给程序(程序自己决定收手);窗口对象当场丢掉,下一次 `打开窗口` 重新建
                _窗口.Closing += (_, _) =>
                {
                    前端契约C.服务.图形.投事件(图形事件C.关窗, 0, 0, 0);
                    _窗口 = null;
                    _画布 = null;
                };
                // 窗口每次被激活(用户从别的窗口切回来)都把键盘焦点收回画布 —— 焦点飘走之后按键就不是这个窗口的事了
                _窗口.Activated += (_, _) => _画布?.Focus();
                _窗口.Show();
                _窗口.Activate();
                _画布.Focus();          // 焦点落在画布上 ⇒ 按键冒泡到窗口那一档就有了
            });
            // **等首帧上屏**再让程序往下画:画一帧就 `关闭窗口()` 的程序(示例14/16)以前窗口根本露不了面
            首帧.Wait(首帧预算毫秒);
        }

        /// <summary>
        /// **WPF 的键 → HB 键码**。三族连续码按算术翻(字母 / 数字 / F1~F12 —— WPF 的 `Key` 枚举
        /// 这三段本来就是连着的),其余按名字一个个对上;认不出给 `图形键C.无`
        /// (不是错:键盘上总有怪键)。这张表就是"WPF 不认识 HB"的那道隔离层。
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
                Key.Return => 图形键C.回车,
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

        /// <summary> WPF 的鼠标键 → HB 键码(`图形键C.鼠标左键/中键/右键`);认不出给 0 </summary>
        internal static int 译鼠标键(MouseButton 键) => 键 switch
        {
            MouseButton.Left => 图形键C.鼠标左键,
            MouseButton.Middle => 图形键C.鼠标中键,
            MouseButton.Right => 图形键C.鼠标右键,
            _ => 0,
        };

        public void 关闭窗口()
        {
            等这一帧画完();          // 还没画出去的那一帧先画掉,再关窗 —— 不然"最后一帧"永远只是队列里的数据
            在界面上(() =>
            {
                var 窗 = _窗口;
                _窗口 = null;
                _画布 = null;
                窗?.Close();
            });
        }

        public void 呈现(IReadOnlyList<绘制命令C> 命令)
        {
            // **空帧不许擦屏**(第236批 H40;用户实测:"示例16,编译后窗体的内容区里一片漆黑,
            // 鼠标滑过某些控件时显一下内容又黑")。干净的一帧照样 `刷新()`(帧节拍照走),但**一条命令都没有** ——
            // 这时动一下画布,`OnRender` 就把上一帧擦成空屏(即时模式每帧整份重画,空的那一份 = 黑底)。
            // 示例16 每帧都刷、只有 `要重画` 那几帧才发命令 ⇒ 少了这一格,窗口每 250 毫秒黑一次,看着就是"一片漆黑"。
            if (命令.Count == 0) return;
            var 快照 = new 绘制命令C[命令.Count];          // 能力层的表是真源:界面侧只拿一份自己的拷贝
            for (int i = 0; i < 命令.Count; i++) 快照[i] = 命令[i].复制();
            在界面上(() => _画布?.设一帧(快照));
            等这一帧画完();          // `刷新()` 回来就真在屏幕上(与桌面壳同一套口径)
        }

        /// <summary> 让出一帧:真睡一会儿(界面线程自己会重绘)。睡的是跑程序那条后台线程,界面不冻 </summary>
        public void 等待(int 毫秒)
        {
            if (毫秒 > 0) Thread.Sleep(毫秒);
        }

        /// <summary>
        /// **把一段文字画进一块像素**(控 1-①,号 78 `贴文字到位图`):WPF 这一口用 `RenderTargetBitmap`
        /// 把一段 `FormattedText` 光栅化,再把 **`Pbgra32` 的字节**拆回 `0xAARRGGBB` 写进 `像素`。
        ///
        /// <para>为什么必须由呈现口干:引擎库是 `net10.0`,**没有 WPF** —— 字体光栅化只有这一侧有。</para>
        ///
        /// <para>⚠ **线程**:`RenderTargetBitmap` 要一个 `Dispatcher`(它内部走 WPF 的渲染栈)。
        /// 本口开的那个窗口是**界面线程**上的,所以这里派到界面线程去做 —— 与 `呈现` / `打开窗口` 同一条规矩。
        /// 界面线程还没起来(自检那条腿没开窗)时**如实返回假**:宁可"没有字",也不要
        /// 在这条线程上硬起一个 WPF 渲染栈(那会让自检多出一整套它不该有的东西)。</para>
        ///
        /// <para>⚠ **只写 `[0, 宽×高)` 这一段**(接口上那条契约):用一个中间位图当画布,
        /// 拷回来时按 `Min(块宽, 图宽)` / `Min(块高, 图高)` 逐行裁 —— 超出这块图的字自然丢掉。</para>
        /// </summary>
        public bool 把文字画进像素(int x, int y, string 文本, uint 字色, int 字号, uint[] 像素, int 宽, int 高)
        {
            if (文本.Length == 0 || 宽 <= 0 || 高 <= 0 || 像素.Length < 宽 * 高) return false;
            if (_界面线程.Thread == null || !_界面线程.Thread.IsAlive) return false;   // 界面线程没起来 ⇒ 没有光栅化器
            bool 成了 = false;
            try
            {
                在界面上(() =>
                {
                    var 画布 = new DrawingVisual();
                    using (var 笔 = 画布.RenderOpen())
                    {
                        var 刷 = new SolidColorBrush(颜色(字色));
                        var 文字 = new FormattedText(文本, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                            等宽, 字号, 刷, 1.0);
                        笔.DrawText(文字, new Point(x, y));
                    }
                    var 光栅 = new RenderTargetBitmap(宽, 高, 96, 96, PixelFormats.Pbgra32);
                    光栅.Render(画布);
                    var 字节 = new byte[宽 * 高 * 4];
                    光栅.CopyPixels(字节, 宽 * 4, 0);
                    for (int i = 0; i < 宽 * 高; i++)
                    {
                        byte b = 字节[i * 4], g = 字节[i * 4 + 1], r = 字节[i * 4 + 2], a = 字节[i * 4 + 3];
                        if (a == 0) continue;                       // 这个像素没被字碰到 ⇒ 原样留着
                        像素[i] = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
                    }
                    成了 = true;
                });
            }
            catch { return false; }          // 这一侧画不了字(没图形栈 / 别的怪事)⇒ 如实返回假,像素不动
            return 成了;
        }

        /// <summary> 颜色是 HB 那边的 0xAARRGGBB(与画布那一份同一个口径)</summary>
        private static Color 颜色(uint 值) =>
            Color.FromArgb((byte)(值 >> 24), (byte)(值 >> 16), (byte)(值 >> 8), (byte)值);
    }

    /// <summary> 画布:把这一帧命令按顺序画上去(即时模式 —— 每帧整份重画,没有保留状态)</summary>
    internal sealed class 图形画布C : FrameworkElement
    {
        private IReadOnlyList<绘制命令C> _一帧 = [];
        private static readonly Typeface 等宽 = new("Consolas");     // 批 1 的文本档:等宽 + 单行

        /// <summary>
        /// **`贴位图` 那一块的家**(控 1-⑥):按帧复用同一张 WPF 位图,尺寸变了才重建。
        ///
        /// ⚠ 不许每帧 `new`:一张 480×520 的图 = 249,600 个像素,60 帧/秒就是每秒 1,500 万次装箱,
        /// 而这一族要的恰恰是"帧数不随画了多少东西涨" —— 每帧新建会把省下来的时间又花回去
        /// (GC 一忙,画面就抖)。
        /// </summary>
        private WriteableBitmap? _贴块;
        /// <summary> 已铺进 `_贴块` 的是哪一块(`_一帧` 里那一条的引用;同一块不重铺)</summary>
        private uint[]? _贴块源;

        public void 设一帧(IReadOnlyList<绘制命令C> 一帧)
        {
            _一帧 = 一帧;
            InvalidateVisual();
        }

        /// <summary> 颜色是 HB 那边的 0xAARRGGBB(与桌面壳 `Color.FromUInt32` 同一份口径)</summary>
        private static Color 颜色(uint 值) =>
            Color.FromArgb((byte)(值 >> 24), (byte)(值 >> 16), (byte)(值 >> 8), (byte)值);

        /// <summary>
        /// **把一块 HB 位图铺进 WPF 位图**(控 1-⑥):字节摆法走引擎那一份口径(`位图字节C`),
        /// 这里只负责"字节 ⇒ `Pbgra32`"。`Pbgra32` 的字节序也是 `B,G,R,A`,与
        /// `C后端\运行时.c` 喂给 `SetDIBitsToDevice` 的那一份**逐字节一样**
        /// (⇒ 同一张图在 IDE 这条腿、独立 exe 那条腿、C 后端那条腿上像素一致)。
        /// </summary>
        private void 铺贴块(绘制命令C 条)
        {
            var 像素 = 条.像素!;
            int 宽 = Math.Max(1, 条.宽), 高 = Math.Max(1, 条.高);
            if (_贴块 == null || _贴块.PixelWidth != 宽 || _贴块.PixelHeight != 高)
            {
                _贴块 = new WriteableBitmap(宽, 高, 96, 96, PixelFormats.Pbgra32, null);
                _贴块源 = null;                     // 换了尺寸 = 上一块的内容作废
            }
            if (ReferenceEquals(_贴块源, 像素)) return;      // 同一块(这一帧别重铺)
            int 格数 = Math.Min(像素.Length, 宽 * 高);
            var 字节 = new byte[位图字节C.偏移(格数)];
            位图字节C.铺进(字节, 0, 像素, 格数);
            _贴块.WritePixels(new Int32Rect(0, 0, 宽, 高), 字节, 位图字节C.偏移(宽), 0);
            _贴块源 = 像素;
        }

        /// <summary>
        /// **把一整块像素一次贴到窗口上**(控 1-⑥)。用户报的原话是"位图命令攒进了窗口队列、
        /// `一句话()` 也打得出那一行,**但真窗口上一个像素都不出**" —— 根因就是这个 `switch` 里
        /// 以前只有 `清屏 / 画矩形 / 画线 / 画文字` 四档,`贴位图` 那条命令走到这儿**没人接**。
        ///
        /// 贴上去**不透明**(施工图 §2.4 拍板 a):图的像素是什么就画什么 ——
        /// 半透明像素按它自己那个 alpha 与前缀颜色混(那是 WPF 的 `Pbgra32` 自带的口径,
        /// 不是我们额外加的合成规则)。
        /// </summary>
        private void 贴一块(绘制命令C 条, DrawingContext 画)
        {
            铺贴块(条);
            if (_贴块 != null) 画.DrawImage(_贴块, new Rect(条.x, 条.y, Math.Max(0, 条.宽), Math.Max(0, 条.高)));
        }

        protected override void OnRender(DrawingContext 画)
        {
            base.OnRender(画);
            double 宽 = Math.Max(0, ActualWidth), 高 = Math.Max(0, ActualHeight);
            foreach (var 条 in _一帧)
            {
                var 笔 = new SolidColorBrush(颜色(条.颜色));
                switch (条.动作)
                {
                    case "清屏":
                        画.DrawRectangle(笔, null, new Rect(0, 0, 宽, 高));
                        break;
                    case "画矩形":
                        画.DrawRectangle(笔, null, new Rect(条.x, 条.y, Math.Max(0, 条.宽), Math.Max(0, 条.高)));
                        break;
                    case "画线":
                        画.DrawLine(new Pen(笔, 1), new Point(条.x, 条.y), new Point(条.x2, 条.y2));
                        break;
                    case "画文字":
                        if (条.文本.Length > 0)
                        {
                            // 字号走共用常量(`图形规格C.默认字号`)—— 与 `贴文字到位图` 那两个口同一处,
                            // 不这么钉就会"窗口上的字一个大小、图里的字另一个大小"(实测过)
                            var 文字 = new FormattedText(条.文本, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                                等宽, 图形规格C.默认字号, 笔, 1.0);
                            画.DrawText(文字, new Point(条.x, 条.y));
                        }
                        break;
                    // R4-12 的位图那一条(控 1-⑥ 补):一整块像素**一次画上去** ——
                    // 这一族要的就是"几百次往返压成一次",所以这里**不许**退化成逐像素 `DrawRectangle`
                    case "贴位图":
                        if (条.像素 != null) 贴一块(条, 画);
                        break;
                }
            }
        }
    }
}
