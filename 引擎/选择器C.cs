using System.IO;

namespace hehshihbeen_compiler
{
    /// <summary> 一次"让用户挑点什么"的请求(标题 / 要目录还是要文件 / 后缀 / 能不能多选) </summary>
    public sealed class 选择请求C
    {
        /// <summary> 对话框标题(手机端会显示在系统选择器上) </summary>
        public string 标题 { get; set; } = "选一个文件";

        /// <summary> true = 挑目录(工程目录);false = 挑文件 </summary>
        public bool 要目录 { get; set; }

        /// <summary> 要平台层允许多选(只对"挑文件"有意义:挑目录一家一门,平台给了多选也只当一条)。
        /// 拿回来的条数看 `选择结果C.路径表`(H9:不再是一条) </summary>
        public bool 可多选 { get; set; }

        /// <summary> 文件后缀过滤(例:`.hb`),空数组 = 不过滤;只对"挑文件"有意义 </summary>
        public string[] 后缀 { get; set; } = [];

        /// <summary> 给人看的一句(自检/界面提示都用它) </summary>
        public string 说明 =>
            (要目录 ? "选一个目录" : "选一个文件")
            + (后缀.Length > 0 ? $"({string.Join(" / ", 后缀)})" : "")
            + (可多选 ? " · 可多选" : "");
    }

    /// <summary> 选择结果:拿到路径 / 用户取消了 / 平台没有选择器 —— 三种情况都**明说**,不返回空路径糊弄人。 </summary>
    public sealed class 选择结果C
    {
        /// <summary> 拿到的路径(**按用户挑的顺序**;多选时是全部,单条时只有一条) </summary>
        public List<string> 路径表 { get; } = [];

        /// <summary> 拿到的那**一条**路径(单条时就是它,多选时是第一条;取消或没选器时是空串)。
        /// 只看一条的老写法照旧;要看全的读 `路径表` </summary>
        public string 路径
        {
            get => 路径表.Count > 0 ? 路径表[0] : "";
            set
            {
                路径表.Clear();
                string 一条 = (value ?? "").Trim();
                if (一条.Length > 0) 路径表.Add(一条);
            }
        }

        /// <summary> 用户主动取消了 </summary>
        public bool 取消 { get; set; }

        /// <summary> 给人看的一句(界面直接显示) </summary>
        public string 说明 { get; set; } = "";

        /// <summary> 拿到了(取消 / 一条没选到都算没拿到) </summary>
        public bool 有路径 => !取消 && 路径表.Count > 0;

        /// <summary> 拿到了几条(多选时 > 1) </summary>
        public int 条数 => 路径表.Count;

        public static 选择结果C 由取消(string 说明 = "没有选择文件") =>
            new() { 取消 = true, 说明 = 说明 };

        /// <summary>
        /// H9:把平台那一层拿回来的一串路径收成一个结果 —— 去掉两头空白、丢掉空串、
        /// **去重**(同一个文件挑两遍只算一条;按序数、忽略大小写 —— Windows 与安卓的路径都不分大小写),
        /// 顺序照用户挑的;一条都不剩 ⇒ 当成"没选到"(与 `由取消` 同一档,不返回半截结果)。
        /// </summary>
        public static 选择结果C 由路径表(IEnumerable<string>? 路径们)
        {
            var 干净 = new List<string>();
            foreach (string? 一条 in 路径们 ?? [])
            {
                string 正 = (一条 ?? "").Trim();
                if (正.Length == 0) continue;
                if (干净.Any(已有 => string.Equals(已有, 正, StringComparison.OrdinalIgnoreCase))) continue;
                干净.Add(正);
            }
            if (干净.Count == 0) return 由取消();
            var 结果 = new 选择结果C { 说明 = 说明一句(干净) };
            结果.路径表.AddRange(干净);
            return 结果;
        }

        /// <summary> 说明那一句:一条写名字;多条写"几个 + 各自的名字",名字凑起来太长就只报个数 </summary>
        private static string 说明一句(List<string> 路径表)
        {
            if (路径表.Count == 1) return $"选了:{Path.GetFileName(路径表[0])}";
            string 名字 = string.Join("、", 路径表.Select(Path.GetFileName));
            return 名字.Length <= 60 ? $"选了 {路径表.Count} 个:{名字}" : $"选了 {路径表.Count} 个文件";
        }
    }

    /// <summary>
    /// **系统文件/目录选择器**(D4)。引擎里只放"口子 + 判定",具体平台(MAUI Android / 以后的别的)自己去挂:
    ///   · `选文件` / `选目录` = 两个异步委托(界面线程上 `await`,不阻塞);**没挂 = 这个平台没有选择器**,
    ///     调用方拿到的就是 null / 一句说明(绝不假装"用户取消了");
    ///   · **多选**(H9):请求里 `可多选 = true` 时平台层该去要多个,拿回来的是 `选择结果C.路径表`(第一条 = `路径`);
    ///     平台那一边不支持多选也不报错 —— 照旧回来一条,调用方按"一条也算数"处理;
    ///   · 判定与文案都是纯函数,所以 `选择器替身C` 能在命令行自检里把整条路走通(真机验证留给用户)。
    /// </summary>
    /// <para>⚠ **这个类是 partial**:`看目录`(与它要用的 `目录检查C`)单放在
    /// `引擎\选择器C.看目录.cs` —— 那一段要 `工程文件C` ⇒ `中间层C`,`HB宿主` 链不动(见那个文件的头注释)。
    /// 往这儿加新方法时:要 `工程文件C` / `中间层C` 的放那个文件,不要的放这里。</para>
    /// </summary>
    public static partial class 选择器C
    {
        /// <summary> 挑文件(由平台挂上;null = 没挂) </summary>
        public static Func<选择请求C, Task<选择结果C?>>? 选文件 { get; set; }

        /// <summary> 挑目录(由平台挂上;null = 没挂) </summary>
        public static Func<选择请求C, Task<选择结果C?>>? 选目录 { get; set; }

        public static bool 有文件选择器 => 选文件 != null;
        public static bool 有目录选择器 => 选目录 != null;
        public static bool 有系统选择器 => 有文件选择器 || 有目录选择器;

        /// <summary> 摘掉(= 没挂);退出工程/换宿主时调一次,免得拿着上一个平台的委托 </summary>
        public static void 清空()
        {
            选文件 = null;
            选目录 = null;
        }

        /// <summary> 挑文件(**同步**版:命令行 / 非界面线程用;没挂选择器返回 null) </summary>
        public static 选择结果C? 选文件同步(选择请求C 请求) => 同步(选文件, 请求);

        /// <summary> 挑目录(**同步**版) </summary>
        public static 选择结果C? 选目录同步(选择请求C 请求) => 同步(选目录, 请求);

        private static 选择结果C? 同步(Func<选择请求C, Task<选择结果C?>>? 口子, 选择请求C 请求)
        {
            if (口子 == null) return null;
            try { return 口子(请求).GetAwaiter().GetResult(); }
            catch (Exception ex) { return new 选择结果C { 说明 = $"选择器出错了:{ex.Message}" }; }
        }

        // `看目录`(与它要用的 `目录检查C`)**不在这儿** —— 见 `引擎\选择器C.看目录.cs`:
        // 那一段要 `工程文件C` ⇒ `中间层C`,而 `HB宿主` 只链这一个文件、链不动中间层。

        /// <summary> 请求的一句话(界面提示/自检详情共用) </summary>
        public static string 摘要(选择请求C 请求) => 请求 == null ? "(空请求)" : 请求.说明;
    }

    /// <summary>
    /// **选择器替身**(D4 验收要求"接口层有测试替身"):把平台那一层换成"排队好的答案 + 请求记录",
    /// 于是自检能在命令行里把"请求 → 拿到路径 / 用户取消 / 没挂选择器"整条路都走一遍。
    /// </summary>
    public sealed class 选择器替身C
    {
        private readonly Queue<选择结果C?> _答案 = new();

        /// <summary> 收过的请求(按顺序;自检拿它核对标题/要目录/后缀有没有传对) </summary>
        public List<选择请求C> 请求记录 { get; } = [];

        /// <summary> 排一个答案(null = 这个平台没这一手,调用方该看到"没挂")</summary>
        public 选择器替身C 排队(选择结果C? 结果)
        {
            _答案.Enqueue(结果);
            return this;
        }

        public 选择器替身C 排队路径(string 路径) => 排队路径表(路径);

        /// <summary> 排一个"多选"的答案(平台一次拿回来好几条;H9) </summary>
        public 选择器替身C 排队路径表(params string[] 路径表) => 排队(选择结果C.由路径表(路径表));

        public 选择器替身C 排队取消(string 说明 = "用户取消了选择") => 排队(选择结果C.由取消(说明));

        /// <summary> 替身自己的处理(挂到 `选择器C` 上) </summary>
        public Task<选择结果C?> 处理(选择请求C 请求)
        {
            请求记录.Add(请求);
            return Task.FromResult(_答案.Count > 0 ? _答案.Dequeue() : null);
        }

        /// <summary> 挂上:文件与目录两个口子都指向自己 </summary>
        public void 挂上()
        {
            选择器C.选文件 = 处理;
            选择器C.选目录 = 处理;
        }

        /// <summary> 摘掉(恢复"这个平台没有选择器") </summary>
        public void 卸载() => 选择器C.清空();
    }
}
