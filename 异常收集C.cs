namespace hehshihbeen_compiler
{
    /// <summary>
    /// 跨窗口/控件的异常收集通道:各界面(MainWindow/编辑器/调试器/向导)捕获的异常统一记到这里,
    /// 由主窗口订阅后写入专门的"异常"页签,不再用 MessageBox 弹窗打断操作。
    /// 无订阅者时静默丢弃(如引擎级命令行场景)。
    ///
    /// **T28(2026-09-27 评测整改)加的两个口子**:这份文件被 CLI / HB宿主 / 引擎库 / WPF **各端的工程**
    /// 一起编(net10.0,不带 WPF),所以这里**不许出现 `System.Windows.Dispatcher`** ——
    /// 界面那一侧在启动时装两个回调进来(`安装界面线程`),本类按它们分流。
    /// </summary>
    public static class 异常收集C
    {
        /// <summary> 追加一行异常文本(主窗口在启动时订阅,写入 异常框) </summary>
        public static event Action<string>? 追加;

        private static bool _已安装;

        /// <summary> 现在这个线程是不是界面线程(null = 这个宿主没有界面线程的概念:CLI / 自检 / HB宿主) </summary>
        private static Func<bool>? 在界面线程;

        /// <summary> 把一件事排到界面线程上做(null = 没有界面线程,只能当场做) </summary>
        private static Action<Action>? 排到界面线程;

        /// <summary>
        /// 装"界面线程"这一对口子(WPF 主程序启动时调一次;装过就不再换)。
        /// CLI / HB宿主 / 自检**不装**,`记` 于是直接调订阅者(它们本来就没有界面线程)。
        /// </summary>
        public static void 安装界面线程(Func<bool> 是不是界面线程, Action<Action> 排队)
        {
            在界面线程 = 是不是界面线程;
            排到界面线程 = 排队;
        }

        /// <summary>
        /// 装一次全局兜底(与界面无关的那两条):后台线程 / 非界面异常也进"异常"页。
        /// **界面那一条**(`Application.DispatcherUnhandledException`)不在这儿 ——
        /// T28 起它归 `App.xaml.cs`(这份文件各端一起编,不许出现 `System.Windows.*`)。
        /// </summary>
        public static void 安装全局捕获()
        {
            if (_已安装) return;
            _已安装 = true;

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                记($"未处理的异常: {e.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                记($"未观察的任务异常: {e.Exception}");
                e.SetObserved();
            };
        }

        /// <summary>
        /// 记一行异常。**T28(2026-09-27 评测整改)起走界面线程**:以前是 `追加?.Invoke(...)` 直接调,
        /// 而后台线程(`Task.Run` 里、`TaskScheduler.UnobservedTaskException` 里)来的异常一进订阅者
        /// 就跨线程碰 `TextBox`(抛 `InvalidOperationException`),又被本函数那个 `catch { }` 吞掉
        /// —— 结果是**记录永久丢失**:该看见的一行,一条也没进"异常"页。
        ///
        /// 现在按装进来的那一对口子分流:
        ///   · 有界面线程且**不在**上面 ⇒ `排到界面线程` 排过去写(异步,不把后台线程卡在界面上);
        ///   · 已经在界面线程上 ⇒ 直接写(省一次排队);
        ///   · **没装**(CLI / HB宿主 / 自检)⇒ 照旧直接调 —— 那种场景本来就没有界面线程可言。
        /// 三条路都不许把异常漏出去。
        /// </summary>
        public static void 记(string 文本)
        {
            string 行 = 文本.EndsWith('\n') ? 文本 : 文本 + "\n";
            try
            {
                if (在界面线程 != null && 排到界面线程 != null && !在界面线程())
                {
                    排到界面线程(() => 唤订阅者(行));
                    return;
                }
            }
            catch { /* 判错了就当"就在界面线程上":直接写,总比丢了强 */ }
            唤订阅者(行);
        }

        /// <summary> 真正喊一声订阅者(界面线程上;订阅者自己的异常不许影响调用方) </summary>
        private static void 唤订阅者(string 行)
        {
            try { 追加?.Invoke(行); }
            catch { /* 订阅者自身异常不影响调用方 */ }
        }
    }
}
