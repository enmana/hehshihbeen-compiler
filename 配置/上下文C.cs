using System.Windows.Media;

namespace hehshihbeen_compiler.配置
{
    /// <summary>
    /// 窗口之间共用的那一份东西:主窗口建一份,启动页、新建向导这类窗口拿着它干活,
    /// 不用再去摸别的窗口。规矩只有一条 —— **只放"两个以上窗口都要用"的**,
    /// 只主窗口自己用的(标签页、当前机器码、调试那套)不放这儿。
    ///
    /// **H38 起这里一个字都不放引擎了**:汇编器 / 虚拟机 / 宿主能力 / 图形都在
    /// `前端契约C.服务` 那一份契约后面(见 `引擎\界面契约C.cs`)——
    /// 界面侧只认契约,不再直接 new 引擎对象、也不直接读那台机器的内存与寄存器。
    /// 以前这里那两个字段(`汇编器C` / `虚拟机C`)已经撤掉。
    /// </summary>
    public class 上下文C
    {
        /// <summary> 最近打开过的项目(启动页列表读它) </summary>
        public 最近项目表C 最近项目 { get; } = 最近项目表C.当前;

        /// <summary>
        /// 上一次"新建项目/打开项目/打开文件"是不是被用户取消了。
        /// 主窗口每次开工前先置回 false,用户取消才置 true;启动页据此决定要不要关掉自己。
        /// </summary>
        public bool 刚才取消了吗 { get; set; }

        // ---- 启动页要触发的几个动作:主窗口构造时挂上,启动页只管调,不碰主窗口 ----

        /// <summary> 弹"新建项目"向导 </summary>
        public Action? 请新建项目 { get; set; }
        /// <summary> 弹"打开项目"(选工程文件 .hbp) </summary>
        public Action? 请打开项目 { get; set; }
        /// <summary> 弹"打开文件" </summary>
        public Action? 请打开文件 { get; set; }
        /// <summary> 打开指定的工程文件(.hbp) </summary>
        public Action<string>? 请打开工程 { get; set; }
        /// <summary> 真去建一个项目(建目录 + 工程文件 + 主程序,并换掉当前项目);由主窗口挂上 </summary>
        public Func<string, string, 项目项C>? 建项目 { get; set; }
        /// <summary> 把一个文件打开成标签页(启动页、向导等都用它) </summary>
        public Action<string>? 打开文件到标签页 { get; set; }
    }

    public class 词表C
    {
        public string 内容 = "";
        public int 偏移;
        public int 宽度;
        public Brush 刷子 = Brushes.Black;
    }
}
