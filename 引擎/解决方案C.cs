using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using Path = System.IO.Path;

namespace hehshihbeen_compiler
{
    /// <summary> 项目里的一个文件(源文件 / 资源 / 汇编源码) </summary>
    public class 文件项C
    {
        public string 全称 { get; set; } = "";
        public string 名称 { get; set; } = "";
        /// <summary> 是否随工程带的资源(资源会在"执行"时作为虚拟机输入流) </summary>
        public bool 是资源 { get; set; }
        /// <summary>
        /// C22:这一份是不是**参与编译的汇编源码**(`.hba`,工程文件 `汇编` 表里列着的那几份)。
        /// 它**和源码同等待遇**(顺序排在 `文件` 表之后、算在 `源文件` 里 —— 编译读它、单步能停它、
        /// 改过没保存也会跟着一起存盘),只在工程树显示时多一个后缀、编译时排在后面。
        /// </summary>
        public bool 是汇编 { get; set; }
        public string 显示名 => 是资源 ? 名称 + "  (资源)" : 是汇编 ? 名称 + "  (汇编源码)" : 名称;
    }

    /// <summary>
    /// 项目 = 一个目录(+ 可选的工程文件 .hbp)。
    /// 有工程文件时:文件表/顺序/启动文件/资源都听工程文件的;
    /// 没工程文件时退回老约定:目录里所有 .hb,入口叫 主程序.hb。
    /// </summary>
    public class 项目项C : INotifyPropertyChanged
    {
        public const string 入口文件名 = 工程文件C.默认入口名;

        public string 名称 { get; set; } = "";
        public string 目录 { get; set; } = "";
        public ObservableCollection<文件项C> 文件 { get; } = [];
        public ObservableCollection<文件项C> 资源 { get; } = [];
         public string 图标 => (文件 != null && 文件.Count > 0) ? "\uE8B7" : "\uE8A5";

        /// <summary> 工程文件(.hbp);没找到时为空,退回"扫目录"模式 </summary>
        public string? 工程路径 { get; private set; }

        /// <summary> 参与联合编译的源文件(文件 表里去掉资源) —— **含 C22 的汇编源码**(顺序:先 `.hb`、后 `汇编` 表) </summary>
        public IEnumerable<文件项C> 源文件 => 文件.Where(f => !f.是资源);

        /// <summary> C22:参与编译的汇编源码(`.hba`;工程文件 `汇编` 表里列着的那几份) </summary>
        public IEnumerable<文件项C> 汇编源 => 文件.Where(f => f.是汇编);

        /// <summary> C22:这一份 `.hba` 参不参与编译(按全路径比,大小写不敏感) </summary>
        public bool 参与编译(string? 全称) =>
            !string.IsNullOrWhiteSpace(全称)
            && 汇编源.Any(f => string.Equals(f.全称, 全称, StringComparison.OrdinalIgnoreCase));

        public bool 有工程文件 => 工程路径 != null;

        public string 工程文件名 => 工程路径 == null ? "" : Path.GetFileName(工程路径);

        /// <summary> 启动文件(绝对路径);没有则 null </summary>
        public string? 启动文件 { get; private set; }

        /// <summary> 工程文件里写的运行内存大小(字节);没写/没工程文件 = 默认 1MB </summary>
        public int 内存字节数 { get; private set; } = 虚拟机C.默认内存大小;

        /// <summary> 工程文件里 `内存` 的原文(给界面显示用;空 = 没写 = 默认)</summary>
        public string 内存原文 { get; private set; } = "";

        /// <summary> 工程文件里 `内存` 写错时的说明(空 = 没问题) —— 编译时先报这个,别让人对着"为什么装不下"猜</summary>
        public string 内存错 { get; private set; } = "";

        /// <summary> 工程文件里写的堆区大小(字节);没写/没工程文件 = 默认 64KB </summary>
        public int 堆字节数 { get; private set; } = 中间层C.默认堆大小;
        /// <summary> 工程文件里 `堆` 的原文(给界面显示用;空 = 没写 = 默认)</summary>
        public string 堆原文 { get; private set; } = "";

        /// <summary> 工程文件里 `堆` 写错时的说明(空 = 没问题)</summary>
        public string 堆错 { get; private set; } = "";

        /// <summary> 工程文件里写的池**一开始**的大小(字节);没写/没工程文件 = 默认 32KB(不够会自己长大)</summary>
        public int 池字节数 { get; private set; } = 中间层C.默认池大小;
        /// <summary> 工程文件里 `池` 的原文(给界面显示用;空 = 没写 = 默认)</summary>
        public string 池原文 { get; private set; } = "";

        /// <summary> 工程文件里 `池` 写错时的说明(空 = 没问题)</summary>
        public string 池错 { get; private set; } = "";

        /// <summary>
        /// 工程文件里写的软栈大小(字节,R1-1);没写/没工程文件 = 按运行内存自动算 `min(64KB, 内存÷4)`。
        /// 它在**内存解析完之后**才定(自动算的那个默认值就是按内存来的)。
        /// </summary>
        public int 软栈字节数 { get; private set; } = 中间层C.默认软栈大小(虚拟机C.默认内存大小);
        /// <summary> 工程文件里 `软栈` 的原文(给界面显示用;空 = 没写 = 按内存自动算)</summary>
        public string 软栈原文 { get; private set; } = "";

        /// <summary> 工程文件里 `软栈` 写错时的说明(空 = 没问题)</summary>
        public string 软栈错 { get; private set; } = "";

        /// <summary>
        /// 工程文件里写的函数帧大小(字节,H34);没写/没工程文件 = 默认 96。
        /// 与 `软栈字节数` 一样,它改变生成的机器码 ⇒ 一路传到 `汇编器C.汇编多文件带调试`。
        /// </summary>
        public int 帧字节数 { get; private set; } = 中间层C.默认帧大小;
        /// <summary> 工程文件里 `帧` 的原文(给界面显示用;空 = 没写 = 默认 96)</summary>
        public string 帧原文 { get; private set; } = "";

        /// <summary> 工程文件里 `帧` 写错时的说明(空 = 没问题;含"不是 4 的倍数""比软栈还大")</summary>
        public string 帧错 { get; private set; } = "";

        /// <summary>
        /// **H14**:本工程要不要查数组 / 列表的运行期下标越界(工程文件 `边界`;没写/没工程文件 = 查)。
        /// 与 `堆字节数` 一样,它改变生成的机器码 ⇒ 一路传到 `汇编器C.汇编多文件带调试`。
        /// </summary>
        public bool 边界检查 { get; private set; } = true;

        /// <summary> 工程文件里 `边界` 的原文(给界面显示用;空 = 没写 = 查)</summary>
        public string 边界原文 { get; private set; } = "";

        /// <summary> 工程文件里 `边界` 写错时的说明(空 = 没问题)</summary>
        public string 边界错 { get; private set; } = "";

        private bool _启动;
        /// <summary>
        /// **可执行文件白名单**(D1):工程文件 `.hbp` 的 `可执行文件` 表,路径已经解析成绝对路径。
        /// 空 = 这个工程不许 HB 程序起子进程(调 `运行进程` 一律给 假)。宿主在"跑程序之前"把它交给
        /// `宿主能力C.设可执行白名单`。
        /// </summary>
        public IReadOnlyDictionary<string, string> 可执行白名单 { get; private set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// **网址白名单**(D5):工程文件 `.hbp` 的 `网址` 表解析成的主机+端口表(与运行时**同一份**解析器)。
        /// 空 = 这个工程不许 HB 程序连任何网址(调 `取网址` 一律给 假)。宿主在"跑程序之前"把它交给
        /// `宿主能力C.设网址白名单`。
        /// </summary>
        public 网址白名单C 网址白名单 { get; private set; } = 网址白名单C.空;
        /// <summary> 是否启动项目(资源管理器里用粗体显示) </summary>
        public bool 是否启动项目
        {
            get => _启动;
            set { if (_启动 != value) { _启动 = value; 通知(nameof(是否启动项目)); } }
        }

        public bool 有入口文件 => 启动文件 != null && File.Exists(启动文件);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void 通知(string 名) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(名));

        /// <summary> 重新扫描:优先按工程文件,其次扫目录(主程序.hb 排第一) </summary>
        public void 刷新文件()
        {
            文件.Clear();
            资源.Clear();
            内存字节数 = 虚拟机C.默认内存大小;      // 退回默认,以免换工程后留着上一个工程的内存设置
            内存原文 = "";
            内存错 = "";
            堆字节数 = 中间层C.默认堆大小;           // 堆同理:换工程就退回默认
            堆原文 = "";
            堆错 = "";
            池字节数 = 中间层C.默认池大小;           // 池同理(池是"一开始多大",不够会自己长大)
            池原文 = "";
            池错 = "";
            软栈字节数 = 中间层C.默认软栈大小(虚拟机C.默认内存大小);   // 软栈(R1-1)同理:没写就按内存自动算
            软栈原文 = "";
            软栈错 = "";
            帧字节数 = 中间层C.默认帧大小;             // 帧(H34)同理:没写就是 96
            帧原文 = "";
            帧错 = "";
            边界检查 = true;                          // H14:换工程就退回默认(查)
            边界原文 = "";
            边界错 = "";
            工程路径 = Directory.Exists(目录) ? 工程文件C.找工程文件(目录) : null;
            // D1:换工程就把上一份白名单清掉(不然上个工程声明的可执行文件会"漏"到这一个)
            可执行白名单 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            网址白名单 = 网址白名单C.空;              // D5:网址白名单同理
            if (!Directory.Exists(目录)) { 通知(nameof(文件)); return; }

            string? 启动 = null;
            if (工程路径 != null)
            {
                try
                {
                    var 工程 = 工程文件C.加载(工程路径);
                    foreach (string f in 工程.文件)
                    {
                        string 全 = 工程.绝对路径(f);
                        文件.Add(new 文件项C { 全称 = 全, 名称 = Path.GetFileName(全) });
                    }
                    // C22:汇编源码(`汇编` 表)排在源码**之后**(编译顺序 = 先 `.hb`、后汇编段;见 工程文件C.汇编)
                    foreach (string f in 工程.汇编)
                    {
                        string 全 = 工程.绝对路径(f);
                        文件.Add(new 文件项C { 全称 = 全, 名称 = Path.GetFileName(全), 是汇编 = true });
                    }
                    foreach (string r in 工程.资源)
                    {
                        string 全 = 工程.绝对路径(r);
                        var 项 = new 文件项C { 全称 = 全, 名称 = Path.GetFileName(全), 是资源 = true };
                        资源.Add(项);
                        文件.Add(项);          // 资源也进树里显示(带"(资源)"后缀),编译时会跳过
                    }
                    启动 = 工程.启动文件.Length == 0 ? null : 工程.绝对路径(工程.启动文件);
                    // D1:可执行文件白名单(路径解析成绝对路径;名字是 HB 侧 `运行进程("名字", …)` 用的键)
                    if (工程.可执行文件.Count > 0)
                    {
                        var 表 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var (名字, 路径) in 工程.可执行文件)
                            if (名字.Trim().Length > 0 && 路径.Trim().Length > 0) 表[名字.Trim()] = 工程.绝对路径(路径.Trim());
                        可执行白名单 = 表;
                    }
                    // D5:网址白名单(同一个工程文件里的 `网址` 表;解析器与运行时共用一份)
                    if (工程.网址.Count > 0) 网址白名单 = 网址白名单C.由文本(工程.网址);
                    if (名称.Length == 0 || 名称 == Path.GetFileName(目录.TrimEnd(Path.DirectorySeparatorChar))) 名称 = 工程.名称;
                    // 运行内存:能解析就用它;写错了也不让工程打不开 —— 记下错在哪,编译时再报给用户
                    内存原文 = 工程.内存;
                    内存错 = "";
                    try { 内存字节数 = 工程.内存字节数; }
                    catch (Exception 内存异常) { 内存字节数 = 虚拟机C.默认内存大小; 内存错 = 内存异常.Message; }
                    // 堆区大小:同内存一条路(写错了也照样打开工程,编译时再报)
                    堆原文 = 工程.堆;
                    堆错 = "";
                    try { 堆字节数 = 工程.堆字节数; }
                    catch (Exception 堆异常) { 堆字节数 = 中间层C.默认堆大小; 堆错 = 堆异常.Message; }
                    // 两个都写对、但配不上(堆比内存还大):也当成"堆写错了",编译时挡住
                    if (堆错.Length == 0 && 内存错.Length == 0 && 堆字节数 > 内存字节数)
                        堆错 = $"堆 {工程文件C.大小文本(堆字节数)} 比运行内存 {工程文件C.大小文本(内存字节数)} 还大:" +
                            "堆是从运行内存里切出来的一块,请把 `内存` 调大、或者把 `堆` 改小。";
                    // 池(一开始多大):同堆一条路;配不上内存也当成"池写错了"
                    池原文 = 工程.池;
                    池错 = "";
                    try { 池字节数 = 工程.池字节数; }
                    catch (Exception 池异常) { 池字节数 = 中间层C.默认池大小; 池错 = 池异常.Message; }
                    if (池错.Length == 0 && 内存错.Length == 0 && 池字节数 > 内存字节数)
                        池错 = $"池 {工程文件C.大小文本(池字节数)} 比运行内存 {工程文件C.大小文本(内存字节数)} 还大:" +
                            "池是从运行内存里切出来的一块,请把 `内存` 调大、或者把 `池` 改小。";
                    // 软栈(R1-1):同池一条路;它的默认值本来就按内存算,所以只有"写大了"才会配不上
                    软栈原文 = 工程.软栈;
                    软栈错 = "";
                    try { 软栈字节数 = 工程.软栈字节数; }
                    catch (Exception 软栈异常) { 软栈字节数 = 中间层C.默认软栈大小(内存字节数); 软栈错 = 软栈异常.Message; }
                    if (软栈错.Length == 0 && 内存错.Length == 0 && 软栈字节数 > 内存字节数)
                        软栈错 = $"软栈 {工程文件C.大小文本(软栈字节数)} 比运行内存 {工程文件C.大小文本(内存字节数)} 还大:" +
                            "软栈是从运行内存里切出来的一块,请把 `内存` 调大、或者把 `软栈` 改小。";
                    // 帧(H34):同软栈一条路;它比软栈还大就是一层调用都放不下
                    帧原文 = 工程.帧;
                    帧错 = "";
                    try { 帧字节数 = 工程.帧字节数; }
                    catch (Exception 帧异常) { 帧字节数 = 中间层C.默认帧大小; 帧错 = 帧异常.Message; }
                    if (帧错.Length == 0 && 软栈错.Length == 0 && 帧字节数 > 软栈字节数)
                        帧错 = $"帧 {帧字节数} 字节比软栈 {工程文件C.大小文本(软栈字节数)} 还大:" +
                            "帧就住在软栈里,一帧比整块软栈还大就没法装下,请把 `软栈` 调大、或者把 `帧` 改小。";
                    // 下标越界检查(H14):写乱了也照样打开工程 —— 记下错在哪,编译时再报给用户
                    边界原文 = 工程.边界;
                    边界错 = "";
                    try { 边界检查 = 工程.边界开关; }
                    catch (Exception 边界异常) { 边界检查 = true; 边界错 = 边界异常.Message; }
                }
                catch
                {
                    工程路径 = null;   // 工程文件坏了就退回扫目录,别让资源管理器空着
                }
            }

            if (工程路径 == null)
            {
                // 第434批:扫目录时也剔掉 HB 机器码产物 —— 没有 `.hbp` 的目录里点一次「只编译」
                // 就会落下 `<目录名>.hb`,以前它会被当源码列进项目树、下一次编译当场报错。
                var 列表 = 源码文件C.去掉机器码(Directory.GetFiles(目录, "*.hb"))
                    .OrderBy(f => Path.GetFileName(f) == 入口文件名 ? 0 : 1)
                    .ThenBy(f => f, StringComparer.Ordinal);
                foreach (string f in 列表)
                    文件.Add(new 文件项C { 全称 = f, 名称 = Path.GetFileName(f) });
                启动 = 文件.FirstOrDefault(f => Path.GetFileName(f.全称) == 入口文件名)?.全称;
            }

            启动文件 = 启动;
            通知(nameof(文件));
            通知(nameof(资源));
            通知(nameof(有工程文件));
            通知(nameof(工程文件名));
        }
    }

    /// <summary> 解决方案:一组项目,其中最多一个是"启动项目"(联合编译/执行以它为准) </summary>
    public class 解决方案C
    {
        public ObservableCollection<项目项C> 项目表 { get; } = [];
        public 项目项C? 启动项目 => 项目表.FirstOrDefault(p => p.是否启动项目);
        public 项目项C? 当前项目 { get; set; }

        /// <summary> 添加项目:目录里已经有 .hbp 就用它,否则按老约定扫目录 </summary>
        public 项目项C 添加项目(string 目录)
        {
            string 全 = Path.GetFullPath(目录);
            var 已有 = 项目表.FirstOrDefault(p => string.Equals(p.目录, 全, StringComparison.OrdinalIgnoreCase));
            if (已有 != null) { 已有.刷新文件(); return 已有; }

            var 项目 = new 项目项C { 目录 = 全, 名称 = Path.GetFileName(全.TrimEnd(Path.DirectorySeparatorChar))};
            项目.刷新文件();
            项目表.Add(项目);
            if (项目表.Count == 1) 项目.是否启动项目 = true;   // 第一个项目默认启动
            return 项目;
        }

        /// <summary> 直接打开工程文件(.hbp):把它和它的子工程都加进解决方案 </summary>
        public List<项目项C> 添加工程(string 工程路径)
        {
            var 结果 = new List<项目项C>();
            foreach (var 工程 in 工程文件C.读取工程树(工程路径))
            {
                var 项目 = 添加项目(工程.目录);
                if (项目.名称.Length == 0 || !项目.有工程文件) 项目.名称 = 工程.名称;
                项目.刷新文件();
                结果.Add(项目);
            }
            return 结果;
        }

        /// <summary>
        /// 新建项目:建项目目录 → 写工程文件(.hbp) → 写起步用的 主程序.hb → 加进解决方案。
        /// 返回新建的项目(调用方再打开它的启动文件)。
        /// </summary>
        public 项目项C 新建项目(string 位置, string 名称)
        {
            string 目录 = Path.GetFullPath(Path.Combine(位置, 名称));
            Directory.CreateDirectory(目录);

            var 工程 = new 工程文件C
            {
                名称 = 名称,
                启动文件 = 工程文件C.默认入口名,
                文件 = [工程文件C.默认入口名],
                说明 = "工程文件:文件按这里的顺序联合编译,启动文件排第一;资源可用于执行时的输入流;子工程会被一起打开。",
            };
            工程.保存(Path.Combine(目录, 名称 + 工程文件C.扩展名));

            string 主程序 = Path.Combine(目录, 工程文件C.默认入口名);
            if (!File.Exists(主程序))
                File.WriteAllText(主程序, 工程文件C.新建主程序文本, new System.Text.UTF8Encoding(false));

            var 项目 = 添加项目(目录);
            项目.刷新文件();
            return 项目;
        }

        /// <summary>
        /// 在项目目录里新建一个 .hb:写文件 →(有工程文件就)登记进文件表 → 刷新项目文件列表。
        /// 返回新文件的绝对路径。没有项目时不该走到这里(界面会把按钮禁掉)。
        /// </summary>
        public static string 新建文件(项目项C 项目, string 文件名, string? 内容 = null)
        {
            string 名 = 文件名.Trim();
            if (名.Length == 0) throw new Exception("文件名不能为空");
            if (!名.EndsWith(".hb", StringComparison.OrdinalIgnoreCase)) 名 += ".hb";
            if (名.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new Exception($"文件名里有非法字符: {名}");

            string 全 = Path.Combine(项目.目录, 名);
            if (File.Exists(全)) throw new Exception($"文件已存在: {名}");
            File.WriteAllText(全, 内容 ?? "", new System.Text.UTF8Encoding(false));

            if (项目.工程路径 is { } 工程路径)
            {
                // C6:优先**只动那一处**地往 `文件` 数组里加一条 —— 用户写在 .hbp 里的注释与手工排版都留着;
                // 找不到 `文件` 数组(手工改过 / 老格式)再退回"读成对象再保存"这条老路。
                string 工程文本 = File.ReadAllText(工程路径);
                if (工程维护C.工程文本加文件(工程文本, 名, out _) is { } 新文本)
                {
                    if (!string.Equals(新文本, 工程文本, StringComparison.Ordinal))
                        工程维护C.写回(工程路径, 新文本);
                }
                else
                {
                    var 工程 = 工程文件C.加载(工程路径);
                    工程.文件.Add(名);
                    工程.保存(工程路径);
                }
            }
            项目.刷新文件();
            return 全;
        }

        /// <summary> 这个文件属于哪个项目(按所在目录判断;不属于任何项目返回 null) </summary>
        public 项目项C? 找所属项目(string 文件全称)
        {
            string 全 = Path.GetFullPath(文件全称);
            string? 目录 = Path.GetDirectoryName(全);
            if (目录 == null) return null;
            return 项目表.FirstOrDefault(p => string.Equals(Path.GetFullPath(目录), p.目录, StringComparison.OrdinalIgnoreCase));
        }

        public void 移除(项目项C 项目)
        {
            bool 曾是启动 = 项目.是否启动项目;
            项目表.Remove(项目);
            if (曾是启动 && 项目表.Count > 0) 项目表[0].是否启动项目 = true;
        }

        public void 设为启动(项目项C 目标)
        {
            foreach (var p in 项目表) p.是否启动项目 = ReferenceEquals(p, 目标);
        }
    }
}
