using hehshihbeen_compiler.选卡控件;
using hehshihbeen_compiler.配置;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace hehshihbeen_compiler
{
    public partial class MainWindow : Window
    {
        #region 字段与属性

        /// <summary>
        /// 汇编器 / 虚拟机 / 调试会话 / 断点表**都只有一份,而且都在前后端契约那一侧**
        /// (`前端契约C.服务`;H38-1 收了编译与"执行"那台机器,H38-2 把调试会话与断点表也收了)。
        /// 窗口这边**一个引擎对象都不留** —— 换前端(MAUI / 将来别的壳)只要换一份 `界面服务C`,
        /// 不必再扫一遍界面代码(见 `引擎\界面契约C.cs`)。
        /// </summary>
        private 上下文C 上下文 { get; set; }
        private byte[] 当前机器码 { get; set; } = [];
        /// <summary> 本次编译的**启动文件**内容(执行时的"预期结果/预期输出"断言从这里读,不从当前标签页读) </summary>
        private string 断言源文本 = "";
        public 主视图模型 主视图模型 => (DataContext as 主视图模型) ?? throw new NullReferenceException("object类型的DataContext在此地开箱后为null");
        private DispatcherTimer? _标签点击计时器;
        /// <summary> 启动时"最小化+隐藏主窗口"只做一次(启动页关掉后主窗口会重新渲染) </summary>
        private bool _已藏过主窗口;
        /// <summary> 当前标签页（数据绑定/汇编/保存都以它为准;启动页/选项页不算) </summary>
        private 选卡页视图模型? 当前选卡
        {
            get
            {
                if (主视图模型 == null) return null;
                if (代码选卡.SelectedItem is 选卡页视图模型 选中 && !选中.是特殊页) return 选中;
                return 主视图模型.常规页.FirstOrDefault();
            }
        }

        /// <summary>
        /// P36:切页的那一刻,把**离开的那一页**的光标位置记下来(编辑器控件是各页共用的一个,
        /// 不记的话"回到刚才那个文件"就跳到开头了)。会话记忆与 `_光标备忘` 用的都是这一份。
        /// </summary>
        private 选卡页视图模型? _上次选卡;

        private void 选卡变了(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (!ReferenceEquals(_上次选卡, 当前选卡))
                {
                    记住离页光标(_上次选卡);       // 事件里"当前选卡"已经是新那一页了,所以要显式传旧那一页
                    _上次选卡 = 当前选卡;
                }
            }
            catch { }
        }
        //private 启动页窗口? 启动页 = null;
        // ===== 集成调试标签页操作 =====

        /// <summary> 是不是正在单步(单步期间编辑器的展开状态被冻结) </summary>
        private bool 正在单步;

        /// <summary>
        /// 这一趟编译是不是**自动顺手带起来的**(执行 F5 / 逐步 / 继续 / 展开到汇编 里那次;
        /// 用户自己点「运行 → 编译」或工具栏「汇编」不算)。
        /// 是的话编译成功后**不自动翻汇编源码页** —— 用户实测:"按 F5,编辑器里多出一个
        /// `&lt;文件&gt;_ASM.hba` 页,连源码页一起被抢走"(第160批先治了「逐步」那一档,
        /// 第161批把 F5 也一起治了:只有用户自己点「编译」才自动开页)。
        /// 想看展开的汇编:点「运行 → 编译」,或工具栏「展开到汇编」。
        /// **只有 `确保编译过()` 会置这一格** —— 别在调用方各置一遍(两处就会分叉)。
        /// </summary>
        private bool _自动编译中;

        private void 调试_复位按钮_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                退出单步();                       // 复位 = 不单步了:解冻、黄条清掉、汇编单步的展开收回去
                if (当前机器码 == null || 当前机器码.Length == 0)
                {
                    编译框.AppendText("\n请先汇编程序(汇编按钮)。");
                    return;
                }
                前端契约C.服务.调试.会话当前文件 = 当前选卡?.全称;
                // H39-2:复位也要有一台"住在子进程里"的调试机(通道死过就换一条新的)
                if (!确保调试机()) { 报跑程序不可用(); return; }
                装载调试会话();
                前端契约C.服务.调试.记会话装载();
                刷新集成调试视图();
            }
            catch (Exception ex)
            {
                编译框.AppendText($"\n调试复位失败: {ex.Message}");
                记异常框($"调试复位失败: {ex}");
            }
        }

        /// <summary>
        /// 程序执行完了(跑到停机),把编辑器上的调试痕迹全清掉:黄条、灰条、单步模式。
        /// 用户要求:没设断点就直接清干净,以表示"程序已经执行完毕";单步走到头也一样(两边同一套逻辑)。
        /// </summary>
        private void 程序结束清调试()
        {
            try
            {
                收掉图形窗口();            // R1-6 批 1 尾巴 ③:程序跑完了,图形窗口跟着收掉(与"进程退出"同口径)
                退出单步();
                if (当前编辑器 is { } 编辑)
                {
                    编辑.清执行行();
                    编辑.设调用处([], null, null);
                }
                前端契约C.服务.调试.会话当前文件 = 当前选卡?.全称;
                收起调试页();                 // 程序跑完了,底部那两页一起收掉
            }
            catch { }
        }

        /// <summary>
        /// 全局快捷键(C7 起**键位可配**,表在 `快捷键C.全部`;默认按 VS 的习惯:
        /// F5 执行 / F10 逐步 / F8 复位 / F7 继续 / F9 切换断点 / F12 转到定义 /
        /// Shift+F12 查找引用 / Ctrl+H 替换 / Ctrl+Shift+F 格式化)。
        /// 在窗口这一层拦(隧道),编辑器里有焦点也照样管用;**表里没有的组合一律不拦**(留给编辑器/AvalonEdit)。
        ///
        /// 第159批踩的坑:**F10 是系统键**。Windows 把 F10 当"激活菜单栏"那条路走(WPF 也是),
        /// 事件里 `e.Key` 报的是 `Key.System`,真正按下的键在 `e.SystemKey` ——
        /// 拿 `e.Key.ToString()` 去查表只会查到 `System`,于是谁都拦不住,系统顺手把菜单栏激活了。
        /// 用户实测现象:"按 F10 逐步没反应"。所以**一律先取系统键**,再拿它去查表;
        /// 查到就 `e.Handled = true`(把 F10 从系统手里接下来,不然焦点会跑进菜单栏)。
        /// Alt 组合键同时也是系统键:那时键盘修饰键里本来就带 Alt,查表走的就是 `Alt+…` 那一档,不受影响。
        /// </summary>
        private void 快捷键_预览(object sender, KeyEventArgs e)
        {
            try
            {
                if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) return;      // Win 组合键不参与
                Key 主键 = e.Key == Key.System ? e.SystemKey : e.Key;              // F10(还有 Alt+…)是系统键
                string 键 = 主键.ToString();
                bool 控制 = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                bool 换档 = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                bool 替换 = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                string? 动作 = 快捷键C.动作Of(键, 控制, 换档, 替换, 配置.选项.当前.快捷键);
                if (动作 == null) return;
                if (执行快捷键(动作)) e.Handled = true;
            }
            catch (Exception 异常) { 异常收集C.记($"快捷键处理失败: {异常.Message}"); }
        }

        /// <summary>
        /// 一个动作落到具体实现上(没实现的返回 false:不拦这个键)。
        ///
        /// H30:表(`快捷键C.全部`)里那条 **`查找`(Ctrl+F)** 这里**故意不写分支** —— 它 `可改 = false`,
        /// 而 `快捷键C.动作Of` 只把**可改**的项派下来(那一处写着"不可改的不参与分发"),
        /// 所以这个动作根本到不了这里;Ctrl+F 由编辑器自己注册的查找面板接(`SearchPanel.Install`)。
        /// 菜单那条「查找…」走的是 `查找_Click` → `编.打开查找面板()`,与快捷键分发是两条路。
        /// </summary>
        private bool 执行快捷键(string 动作)
        {
            switch (动作)
            {
                case "执行": 执行按钮_Click(this, new RoutedEventArgs()); return true;
                case "逐步": 调试_逐步菜单_Click(this, new RoutedEventArgs()); return true;
                case "继续": 调试_继续按钮_Click(this, new RoutedEventArgs()); return true;
                case "停止": 调试_停止按钮_Click(this, new RoutedEventArgs()); return true;
                case "复位": 调试_复位按钮_Click(this, new RoutedEventArgs()); return true;
                case "热重载": 热重载菜单_Click(this, new RoutedEventArgs()); return true;
                case "切换断点": 菜单切换断点_Click(this, new RoutedEventArgs()); return true;
                case "格式化当前文件": 执行格式化(); return true;
                case "转到定义": 执行转到定义(); return true;
                case "查找引用": 执行查找引用(); return true;
                case "替换当前文件": 替换当前文件_Click(this, new RoutedEventArgs()); return true;
                case "在控制台窗口里跑": 控制台窗口跑_Click(this, new RoutedEventArgs()); return true;
                case "命令面板": 打开命令面板(); return true;
                case "加监视表达式": 执行加监视(); return true;
                // H30:表里的『查找』(Ctrl+F)**故意不写分支** —— 它 可改 = false,而 `快捷键C.动作Of`
                // 只派可改的项,这个动作根本到不了这里;Ctrl+F 由编辑器自己注册的查找面板接
                // (菜单那条「查找…」走 查找_Click → 编.打开查找面板(),与这里无关)。
                default: return false;
            }
        }

        /// <summary> 菜单右边那串键位提示跟着设置走(选项页改完快捷键要调一次)</summary>
        private void 刷新快捷键显示()
        {
            try
            {
                var 覆盖 = 配置.选项.当前.快捷键;
                string 取(string 动作) => 快捷键C.生效键(动作, 覆盖);
                执行菜单.InputGestureText = 取("执行");
                调试逐步菜单.InputGestureText = 取("逐步");
                调试继续菜单.InputGestureText = 取("继续");
                调试停止菜单.InputGestureText = 取("停止");
                调试复位菜单.InputGestureText = 取("复位");
                热重载菜单.InputGestureText = 取("热重载");
                切换断点菜单.InputGestureText = 取("切换断点");
                格式化菜单.InputGestureText = 取("格式化当前文件");
                转到定义菜单.InputGestureText = 取("转到定义");
                查找引用菜单.InputGestureText = 取("查找引用");
                替换当前菜单.InputGestureText = 取("替换当前文件");
                控制台窗口跑菜单.InputGestureText = 取("在控制台窗口里跑");
                命令面板菜单.InputGestureText = 取("命令面板");
                加监视菜单.InputGestureText = 取("加监视表达式");
                查找菜单.InputGestureText = 取("查找");
            }
            catch { /* 菜单提示只是观感 */ }
        }

        private void 调试_逐步菜单_Click(object sender, RoutedEventArgs e) => 走一步();

        // ---------------- 「展开到汇编」按钮 ----------------
        // 第158批(C23):按钮不再是"语言源码 ↔ 整篇只读展开"的开关,改成**正常按钮** ——
        // 点一下把当前文件的那一段展开在新页里打开(可编辑、可保存),落点 `<文件>_ASM.hba`。
        // 实现在 `MainWindow.项目与编译.cs` 的 `#region C23`(与 C20 的自动开页共用一条路)。

        /// <summary>
        /// 逐步(第158批:用户把原「逐汇编 / 逐语句」两档**合并成一档**;
        /// 第160批按用户实测把粒度定成**一条源码行**)。
        /// 第一次按会自动"进入单步模式":底部显示指令/寄存器/内存、黄条落在**当前指令对应的源码行**上。
        /// **编辑器不再换成整篇展开的汇编**(用户:"当前代码是什么不管,也不展开,只需要逐条执行就行")——
        /// 想看展开的汇编请用工具栏「展开到汇编」在新页里看(可编辑可保存)。
        /// 一次走**一条源码行**(调试会话的 `逐步一次`):编译器生成的序言 / 收尾一口气跑过 ——
        /// 不然"走了几条指令"在编辑器里看不见(用户实测:"按了没反应")。走到头(停机)就按"执行完"清干净。
        /// 具体怎么走是调试会话的事,这里只负责"按一下 → 调一次 → 按快照画"。
        /// </summary>
        private void 走一步()
        {
            try
            {
                bool 准备好了 = 准备好调试();     // 自动编译那一段由 确保编译过() 自己打标记(不翻汇编源码页)
                if (!准备好了) return;
                if (!正在单步) 进入单步();
                前端契约C.服务.调试.会话当前文件 = 当前选卡?.全称;

                var 结果 = 前端契约C.服务.调试.逐步一次();
                // 走到头了(已经停机):清掉黄条和单步模式,和"执行完"一个样子 —— 表示程序已经跑完了
                if (结果.已停机) 程序结束清调试();
                else 应用调试快照(结果);
            }
            catch (Exception ex)
            {
                // H39-2:子进程 / 通道出的事如实说清(不当成"这一步走不动")
                if (!报跑程序故障(ex))
                {
                    编译框.AppendText($"\n单步失败: {ex.Message}");
                    记异常框($"单步失败: {ex}");
                }
            }
        }

        /// <summary> 准备调试:没编译过就先自动编译;装载过就直接用;没装载过就拿刚编译的机器码开跑 </summary>
        private bool 准备好调试() => 自动编译好再调试();

        /// <summary>
        /// 要机器码的动作(执行 / 生成二进制 / 调试)都先过这里:
        /// ① 先把**改过没保存**的标签页存盘(编译读的是磁盘上的文件)——
        ///    用户口径:F5 就是"编译执行",改了源码再按 F5,跑的必须是改过的程序;
        /// ② 源码跟上次编译的不一样就**自动重新编译**,一样就留着机器码(所以"停在断点上再按 F5"才是接着跑)。
        /// </summary>
        private bool 确保编译过()
        {
            if (解决方案.启动项目 == null) { 提示需要项目(); return false; }
            本次重新编译过 = false;
            int 存了 = 保存项目里改过的页(解决方案.启动项目);
            if (存了 > 0) 编译框.AppendText($"已自动保存 {存了} 个改过的文件(编译读的是磁盘上的文件)。\n");
            bool 要重编译 = 当前机器码.Length == 0 || 源码变了吗();
            if (要重编译)
            {
                // 自动编译 = 执行 F5 / 逐步 / 继续 / 展开到汇编 顺手带起来的那一次:
                // **这次编译成功后不自动翻汇编源码页**(用户实测:F5 会在编辑器里多开一个
                // `<文件>_ASM.hba` 页,还把源码页抢走 ⇒ 黄条/输出都对不上)。
                // 想看这一次编出来的展开:点「运行 → 编译」(那条路不置这一格),或工具栏「展开到汇编」。
                _自动编译中 = true;
                try { 编译启动项目_Click(this, new RoutedEventArgs()); }
                finally { _自动编译中 = false; }
                本次重新编译过 = 当前机器码.Length > 0;
            }
            if (当前机器码.Length == 0) return false;
            // H39-2:"执行"那台虚拟机住在**子进程**里(`MainWindow.跑程序通道.cs`):壳找不到 / 起不来就如实
            // 说一句 + 这一趟不跑(绝不静默退回进程内 —— 那会变成两条口径并存的半成品);通道死过就换一台新的。
            if (!确保跑程序机()) { 报跑程序不可用(); return false; }
            // **新编译出来的机器码要装进虚拟机** —— 不装的话"执行"跑的是空内存(全是 0 = HALT),
            // 现象就是"执行完成、寄存器全 0、程序输出是空的"(踩过:输出断言永远 实际 <>)。
            // 判据收在引擎侧(`调试服务C.要重新装载`):刚重新编译过 / 这台虚拟机上装的不是这一份 /
            // **上一轮已经停机**(跑完再按 F5 得从头来)都算要装。
            // 只有"程序正卡在等你输入"那一档不装 —— 不能把 PC 和输入队列一起冲掉。
            if (要重编译 || 前端契约C.服务.调试.机器要重新装载(当前机器码))
                装载执行机();
            return true;
        }

        /// <summary>
        /// 装载当前机器码:**顶层常量初值跟着一起落进数据段**。
        /// 初值现在不编成"程序开头一串存储指令"了,所以装载这一关必须把编译结果里的初值表也递进去
        /// (编译结果丢了就只能装机器码 —— 那种情况机器码本身也是空的)。
        /// "有编译结果、而且就是这一份"那两句判据收在契约里(`调试服务C.装载`),界面不再自己拼。
        /// </summary>
        private void 装载执行机()
        {
            同步图形呈现();          // R1-6 批 1 尾巴 ③:这份机器码用图形就把 IDE 自己的图形窗口挂上当呈现口
            重置断点次数();          // P34:装载 = 程序从头跑 ⇒ 命中次数从头数(日志队列也清)
            // **工程文件里的 `内存` 必须在装载这一刻交给契约**:容量随装载帧带给子进程那一台,
            // 不交就一直是默认 1MB ⇒ 大内存档当场抛「机器码 1083320 字节装不进这台虚拟机的内存
            // (1048576 字节)」(用户实测:F5 跑 `示例60_大内存档` 就是这个)。
            // "比一比、不一样才设"那两句**收在契约里** —— 界面侧不许直接摸引擎(前后端边界棘轮
            // 只许少不许多),所以这里只递一个数过去。
            前端契约C.服务.调试.按内存装载(前端契约C.服务.调试.机器, 当前机器码, 当前内存大小);
        }

        /// <summary> 同上,装载调试会话(单步调试也要看得到常量初值;图形窗口也在这条路上挂上)</summary>
        private void 装载调试会话()
        {
            同步图形呈现();
            重置断点次数();          // P34:同上(单步那一台也从第一句开始)
            // 与 `装载执行机` 同一条口径:内存档跟着工程走(调试会话那一台也住在子进程里,
            // 它自己的装载那一步会拿这一格去开内存,所以这里只要把工程里那个数传过去)
            前端契约C.服务.调试.会话内存大小 = 当前内存大小;
            前端契约C.服务.调试.装载会话(当前机器码);
        }

        /// <summary> 上一次 确保编译过() 是不是真的重新编译了(执行时好跟用户说清楚为什么从头跑) </summary>
        private bool 本次重新编译过;

        /// <summary> 这次编译用的源文本指纹(F5 靠它判断"该重新编译"还是"接着跑") </summary>
        private string 编译指纹 = "";

        /// <summary> 编译指纹要连"优化开/关"一起算:切了开关必须重新编译,不能拿旧机器码接着跑 </summary>
        private static string 优化标记() => 优化开 ? "|优化开" : "|优化关";

        /// <summary> Debug 构建提示只说一次(用户不爱看重复的话) </summary>
        private bool _提示过构建;

        // ================= 编辑器代码检查(波浪下划线 + 悬停提示 + 问题页) =================
        #endregion
    }
}
