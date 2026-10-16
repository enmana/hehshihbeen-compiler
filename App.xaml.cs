using System.Windows;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 程序入口。窗口流程很简单:
    /// 主窗口先启动(StartupUri),它在 Loaded 里把主窗口藏起来、显示启动页;
    /// 启动页一关,主窗口再显示出来。这里只装一次全局异常兜底。
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            程序目录C.搬一次隔壁设置();      // Release/Debug 两个构建共用一份设置(只搬一次,搬不动也不影响)
            base.OnStartup(e);
            异常收集C.安装全局捕获();        // 后台/非界面异常统一进"异常"页,不弹系统错误框
            // T28(2026-09-27 评测整改):界面线程这一条也在界面层装 —— `异常收集C` 各端一起编,
            // 不许出现 `System.Windows.*`(以前那句 `Application.Current.DispatcherUnhandledException`
            // 就在那份文件里,所以它编不进 hbcc / 引擎库 / HB宿主)。
            DispatcherUnhandledException += (_, e) =>
            {
                异常收集C.记($"未处理的界面异常: {e.Exception}");
                e.Handled = true;          // 不弹系统框,留在程序里继续用
            };
            // 再把"界面线程"这一对口子装给它:后台线程来的异常改走 Dispatcher(以前直接写 TextBox,
            // 跨线程抛了又被吞,记录永久丢失)。
            异常收集C.安装界面线程(
                () => Dispatcher.CheckAccess(),
                做 => Dispatcher.BeginInvoke(做));
        }
    }
}
