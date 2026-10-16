using System.IO;
using System.Text.Json;

namespace hehshihbeen_compiler.配置
{
    /// <summary> 编辑器/运行选项 + 界面记忆(统一存于程序目录 选项.json) </summary>
    public class 选项设置
    {
        // ---- 编辑器 ----
        public string 字体族 { get; set; } = "Consolas";
        public double 字号 { get; set; } = 14;
        public bool 粗体 { get; set; }
        public bool 斜体 { get; set; }
        /// <summary>
        /// 行高(像素)。2026-09 用户反馈"行与行挨得太紧",默认从 20 提到 26;
        /// 老配置里还留着旧默认值 20 的,载入时按新默认 26 处理(自己改过别的值不受影响)。
        /// </summary>
        public double 行高 { get; set; } = 26;

        /// <summary> 旧的默认行高(载入时用它判断"用户是不是没动过" )</summary>
        public const double 旧默认行高 = 20;
        public bool 显示行号 { get; set; } = true;
        public bool 自动换行 { get; set; }
        public bool 自动成对 { get; set; } = true;
        /// <summary> Tab 确认:候选弹着的时候按 Tab = 插入选中的候选(**空格不再参与补全**,照常输入空格)</summary>
        public bool Tab确认 { get; set; } = true;

        /// <summary> 回车自动缩进(C5):按**块深度**给新行缩进(块里进一档、`}` 行退回来、续行进一档)</summary>
        public bool 回车自动缩进 { get; set; } = true;

        /// <summary> 一档缩进几个空格(只影响"回车自动缩进"与"格式化";1~16)</summary>
        public int 缩进宽度 { get; set; } = 4;

        /// <summary>
        /// 按层的**缩进导引线**(C16-3):每一对跨行 `{}` 在它那个 `}` 的列上画一条竖线。
        /// H8 起进选项页(以前"想关掉只能改代码"),默认开 —— 关掉只是不画,折叠本身一个字节不动。
        /// </summary>
        public bool 缩进导引线 { get; set; } = true;

        /// <summary>
        /// **花括号 7 档彩虹色**(H8):按嵌套层循环上色,与缩进导引线同一套色。
        /// 默认 = 引擎那一套(`花括号层号C.色表`,赤橙黄绿青蓝紫);选项页里逐档改,存的就是这 7 行。
        /// 值写坏 / 表短了都由 `花括号层号C.规范色表` 落回默认,不会让整篇彩虹色消失。
        /// </summary>
        public List<string> 花括号色 { get; set; } = [.. 花括号层号C.色表];

        /// <summary>
        /// 快捷键覆盖表(C7):动作名 → 键位文本(如 `Ctrl+E`);**没有的动作用默认键**,
        /// 空串 = 用户把这个快捷键取消了。默认键与动作清单的唯一事实源是 `快捷键C.全部`。
        /// </summary>
        public Dictionary<string, string> 快捷键 { get; set; } = [];

        /// <summary>
        /// **监视表达式**(P35,2026-10-07):调试时一直盯着的那几条(`i`、`总数 &gt;= 100`、`左 &lt; 右`)。
        /// 与工程无关(换个工程也还盯着),所以放进选项跟着 `选项.json` 走;
        /// 写法与条件断点**同一套**(见 `引擎\监视C.cs`)。
        /// </summary>
        public List<string> 监视 { get; set; } = [];

        /// <summary> 模糊拼音:按拼音打字时,勾中的模糊规则也算命中(默认开)</summary>
        public bool 模糊拼音 { get; set; } = true;

        /// <summary> 勾中的模糊拼音规则名(与 `补全词表C.模糊规则表` 的键一致;默认 翘舌→平舌 与 后鼻音→前鼻音)</summary>
        public List<string> 模糊拼音规则 { get; set; } = [.. 补全词表C.默认模糊规则];
        public string 默认文件夹 { get; set; } = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// 编译期优化(消冗余指令 + 循环不变量提升)默认开 —— 关掉只是为了"对着原样展开查错",
        /// 所以在这儿记一下,免得重启后又忘了自己是关着的(编译页会一直显示当前状态)。
        /// </summary>
        public bool 优化开 { get; set; } = true;

        /// <summary>
        /// **跑程序时允不允许弹系统弹窗**(H92-1,2026-10-14)。**默认关**。
        /// <para>
        /// 管的是 `Sample\库\弹窗.hb` 那一族(提示框 / 选文件 / 另存为):关着时它们一律给**状态 25**
        /// ("这个宿主不弹窗"),程序照常跑完 —— 与命令行 / 自检 / 独立 exe **同一个口径**;
        /// 勾上「运行 → 允许弹窗」才真弹,那时人得守着点它。
        /// </para>
        /// <para>
        /// **为什么做成开关而不是像以前那样写死"开"**:界面这条腿原来**无条件**设真(H91 当时的想法是
        /// "桌面 IDE 是有人在看的那一条"),于是 `Sample\中间层\示例59_剪贴板与弹窗` 一跑就真弹模态框,
        /// 而它那行 `// 预期输出:` 是按"不弹"写的 ⇒ **输出断言永远红**(H92-1);
        /// 更要紧的是弹窗会**把人挡在程序跑完之前**。默认关 = 两条腿同答案,想看真弹窗的人自己勾一次。
        /// </para>
        /// <para>
        /// 与 `宿主弹窗C.允许弹窗` 是同一件事的两头:界面这一格 → 起子进程时经环境变量 `HB_弹窗` 传下去。
        /// </para>
        /// </summary>
        public bool 允许弹窗 { get; set; }

        // ---- 界面记忆:对话框各自记住"上次用的目录"(变了才写盘) ----
        /// <summary> 老字段:以前只记"上次新建项目用的父目录";现在改用 新建项目目录表,留着只为把老配置带过来 </summary>
        public string 上次新建项目目录 { get; set; } = "";
        /// <summary> 新建项目用过哪些父目录(最近用的排最前,自动去重,最多 20 条) </summary>
        public List<string> 新建项目目录表 { get; set; } = [];
        /// <summary> 上次打开项目(.hbp)所在目录 </summary>
        public string 上次打开项目目录 { get; set; } = "";
        /// <summary> 上次打开文件所在目录 </summary>
        public string 上次打开文件目录 { get; set; } = "";

        /// <summary> 上次展开着哪些项目(存工程文件路径;没有项目时不记录) </summary>
        public List<string> 展开的项目表 { get; set; } = [];

        /// <summary> 把老配置里那一个目录补进新表(只在表还空着的时候做一次),搬完就把老字段清掉 </summary>
        public void 带过老目录()
        {
            if (新建项目目录表.Count > 0 || 上次新建项目目录.Length == 0) return;
            if (Directory.Exists(上次新建项目目录)) 新建项目目录表.Add(上次新建项目目录);
            上次新建项目目录 = "";
        }
    }

    public static class 选项
    {
        private static string 文件路径 => Path.Combine(程序目录C.数据目录, "选项.json");

        public static 选项设置 当前 { get; private set; } = new();

        static 选项()
        {
            try
            {
                if (File.Exists(文件路径))
                {
                    var 读 = JsonSerializer.Deserialize<选项设置>(File.ReadAllText(文件路径));
                    if (读 != null && 读.默认文件夹 == "")
                    {
                        读.默认文件夹 = AppDomain.CurrentDomain.BaseDirectory;
                    }
                    当前 = 读 ?? throw new NullReferenceException("读取的配置文件不能为null");
                    当前.带过老目录();
                    // 行高:老配置里还是旧默认值 20 的,按新默认 26 走(用户反馈行距太紧)
                    if (Math.Abs(当前.行高 - 选项设置.旧默认行高) < 0.01) 当前.行高 = new 选项设置().行高;
                    // 模糊拼音规则:老配置可能是 null(没有这个字段)
                    当前.模糊拼音规则 ??= [.. 补全词表C.默认模糊规则];
                    // 快捷键覆盖表:老配置没有这个字段 → 空表(等于全用默认)
                    当前.快捷键 ??= [];
                    // P35:监视表达式(老配置没有这个字段 → 空表)
                    当前.监视 ??= [];
                    // H8:花括号 7 档彩虹色 —— 老配置没有这个字段 → 引擎默认那 7 档
                    当前.花括号色 ??= [.. 花括号层号C.色表];
                }
            }
            catch { }
        }

        public static void 保存(选项设置 值)
        {
            当前 = 值;
            try
            {
                File.WriteAllText(文件路径, JsonSerializer.Serialize(当前, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        /// <summary> 新建项目窗口要显示的候选目录:还在磁盘上的,最近用的在最前 </summary>
        public static List<string> 新建项目目录候选() =>
            [.. 当前.新建项目目录表.Where(Directory.Exists)];

        /// <summary> 最多记几个"新建项目用过的父目录" </summary>
        public const int 最多目录数 = 20;

        /// <summary> 记一个"新建项目用过的父目录":去重、提到最前、最多留 20 条,然后写盘 </summary>
        public static void 记住新建目录(string 目录)
        {
            if (string.IsNullOrWhiteSpace(目录) || !Directory.Exists(目录)) return;
            string 全 = Path.GetFullPath(目录);
            当前.新建项目目录表.RemoveAll(项 => string.Equals(项, 全, StringComparison.OrdinalIgnoreCase));
            当前.新建项目目录表.Insert(0, 全);
            if (当前.新建项目目录表.Count > 最多目录数)
                当前.新建项目目录表.RemoveRange(最多目录数, 当前.新建项目目录表.Count - 最多目录数);
            保存(当前);
        }

        /// <summary>
        /// 记下"打开项目"用过的目录:存的是**工程目录的上一层**(把最后一个目录名去掉 —— 那一般就是工程名),
        /// 下次打开对话框就停在"工程们的父目录",方便挑旁边的工程。
        /// </summary>
        public static void 记住打开项目目录(string 工程文件全称)
        {
            string? 工程目录 = 所在目录(工程文件全称);
            if (工程目录 == null) return;
            string 记 = Path.GetDirectoryName(工程目录.TrimEnd(Path.DirectorySeparatorChar)) ?? 工程目录;
            if (!Directory.Exists(记)) 记 = 工程目录;
            if (同(当前.上次打开项目目录, 记)) return;
            当前.上次打开项目目录 = 记;
            保存(当前);
        }

        /// <summary> 记下"打开文件"用过的目录(就是文件自己所在的那一层,不去掉) </summary>
        public static void 记住打开文件目录(string 文件全称)
        {
            string? 目录 = 所在目录(文件全称);
            if (目录 == null || 同(当前.上次打开文件目录, 目录)) return;
            当前.上次打开文件目录 = 目录;
            保存(当前);
        }

        private static string? 所在目录(string 全称)
        {
            try
            {
                string 全 = Path.GetFullPath(全称);
                if (!File.Exists(全)) return null;
                string? 目录 = Path.GetDirectoryName(全);
                return 目录 != null && Directory.Exists(目录) ? 目录 : null;
            }
            catch { return null; }
        }

        private static bool 同(string 左, string 右) =>
            string.Equals(左, 右, StringComparison.OrdinalIgnoreCase);
    }
}