using System.IO;
using System.Text.Json;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 一个断点。记的是**源码位置**(文件全称 + 行号),不是机器码地址 ——
    /// 一编译地址就变,源码位置不变,所以重编/热重载之后断点还在原地。
    /// </summary>
    public sealed class 断点项C
    {
        public string 文件 { get; set; } = "";
        public int 行 { get; set; }
        /// <summary>
        /// 条件断点:P24(第121批)**已实现** —— 写法 `变量 运算符 常量`(如 `i == 5`、`计数 != 真`、`c == 'A'`),
        /// 右边也可以是**另一个变量**(P34 起:`左 &lt; 右`),或者只写变量(非 0 就算真);空 = 无条件命中。
        /// 求值见 `引擎\断点条件C.cs`。
        /// </summary>
        public string 条件 { get; set; } = "";

        /// <summary>
        /// 命中次数条件(P34,2026-10-07):`5`(第 5 次停一次)/ `%5`(每 5 次停一次)/ `&gt;=5`(第 5 次起每次都停);
        /// 空 = 不限次数。解析与判断见 `命中次数条件C`。
        /// </summary>
        public string 命中次数 { get; set; } = "";

        /// <summary>
        /// 日志断点(logpoint,P34):这一段文本在命中时**打印到「输出」页**、**不停下来**
        /// (`{变量}` 会被换成那个变量现在的值,还认 `{行}` / `{文件}`);空 = 普通断点(命中就停)。
        /// 展开见 `日志断点C.展开`。
        /// </summary>
        public string 日志 { get; set; } = "";

        /// <summary> 这个断点带"条件 / 命中次数 / 日志"吗(编辑器上画成空心点)</summary>
        public bool 带附加条件 =>
            条件.Trim().Length > 0 || 命中次数.Trim().Length > 0 || 日志.Trim().Length > 0;

        /// <summary> 这是个日志断点吗(命中时只打印不停)</summary>
        public bool 是日志断点 => 日志.Trim().Length > 0;

        /// <summary> 一行摘要里跟在行号后面的那一小段(`: i == 5` / `: 每 5 次` / `: 日志 …`);没有附加条件给空串 </summary>
        public string 附加摘要()
        {
            var 段 = new List<string>();
            if (条件.Trim().Length > 0) 段.Add(条件.Trim());
            if (命中次数.Trim().Length > 0) 段.Add($"每{命中次数.Trim()}次");
            if (日志.Trim().Length > 0) 段.Add($"日志 {日志.Trim()}");
            return 段.Count == 0 ? "" : ":" + string.Join(" ", 段);
        }
    }

    /// <summary>
    /// 断点表:**跟着工程走** —— 存在项目目录里的 `断点.hbd`(JSON),工程一挪断点跟着挪。
    /// 纯数据 + 读写,零界面依赖,自检和命令行都能用。
    /// </summary>
    public sealed class 断点表C
    {
        /// <summary> 断点文件名(放在项目目录里,和 工程.hbp 一起) </summary>
        public const string 文件名 = "断点.hbd";

        public List<断点项C> 断点 { get; set; } = [];

        public int 个数 => 断点.Count;

        public bool 有(string 文件, int 行) => 断点.Any(x => 同行(x, 文件, 行));

        /// <summary> 切换一个断点:有就去掉,没有就加上。返回"现在有没有" </summary>
        public bool 切换(string 文件, int 行)
        {
            var 已有 = 断点.FirstOrDefault(x => 同行(x, 文件, 行));
            if (已有 != null)
            {
                断点.Remove(已有);
                return false;
            }
            断点.Add(new 断点项C { 文件 = 文件, 行 = 行 });
            return true;
        }

        /// <summary> 清除所有断点(菜单里那条"清除所有断点") </summary>
        public void 清除() => 断点.Clear();

        /// <summary> 某个文件上有哪些断点行(编辑器画红点用) </summary>
        public int[] 取行号(string 文件) =>
            [.. 断点.Where(x => 文件判定C.同文件(x.文件, 文件)).Select(x => x.行).OrderBy(x => x)];

        /// <summary> P34:某个文件上"带附加条件"(条件 / 命中次数 / 日志)的那些断点行(编辑器画空心圈) </summary>
        public int[] 取附加行号(string 文件) =>
            [.. 断点.Where(x => 文件判定C.同文件(x.文件, 文件) && x.带附加条件).Select(x => x.行).OrderBy(x => x)];

        public string 摘要() => 断点.Count == 0 ? "没有断点" : $"{断点.Count} 个断点";

        /// <summary>
        /// 给人看的一行摘要(按文件分组):例如 `主程序.hb 第 3、6 行`。没断点返回空串。
        /// 执行前打这一行,是为了让"为什么会停下来"一眼可见 —— 不然一堆断点是哪来的都不知道。
        /// </summary>
        public string 摘要明细()
        {
            if (断点.Count == 0) return "";
            var 组 = 断点.GroupBy(x => Path.GetFileName(x.文件), StringComparer.OrdinalIgnoreCase);
            return string.Join(";", 组.Select(g => $"{g.Key} 第 {string.Join("、", g.Select(x => x.行).OrderBy(x => x))} 行"));
        }

        /// <summary>
        /// 清掉"已经不成立"的断点:文件不在这次编译的工程里、或者那一行已经没有可执行代码了
        /// (改了代码、删了行之后都会这样)。返回清掉了几个。
        /// </summary>
        public int 清失效(IReadOnlyList<文件映射C> 调试文件表)
        {
            int 清掉 = 断点.RemoveAll(x => 算某行地址(调试文件表, x.文件, x.行) == null);
            return 清掉;
        }

        private static bool 同行(断点项C 项, string 文件, int 行) => 文件判定C.同文件(项.文件, 文件) && 项.行 == 行;

        // ---------------- 跟着工程走的读写(读不到/坏了都当"没有断点",不炸) ----------------

        public static string 文件路径(string 项目目录) => Path.Combine(项目目录, 文件名);

        public static 断点表C 加载(string? 项目目录)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(项目目录)) return new 断点表C();
                string 路径 = 文件路径(项目目录);
                if (!File.Exists(路径)) return new 断点表C();
                return 从文本(File.ReadAllText(路径));
            }
            catch { return new 断点表C(); }
        }

        public void 保存(string? 项目目录)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(项目目录) || !Directory.Exists(项目目录)) return;
                // C26:走全仓唯一的文本落盘点(保留这个文件原来有没有 BOM;新文件不带 BOM)
                文本落盘C.写回(文件路径(项目目录), 到文本());
            }
            catch { }
        }

        public string 到文本() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        public static 断点表C 从文本(string 文本)
        {
            if (string.IsNullOrWhiteSpace(文本)) return new 断点表C();
            try
            {
                var 读 = JsonSerializer.Deserialize<断点表C>(文本);
                if (读 == null) return new 断点表C();
                读.断点 ??= [];
                读.断点.RemoveAll(x => string.IsNullOrWhiteSpace(x.文件) || x.行 <= 0);
                return 读;
            }
            catch { return new 断点表C(); }     // 文件坏了就当没有断点,别让打不开工程
        }

        // ---------------- 断点 → 机器码地址(编译之后才算得出来) ----------------

        /// <summary>
        /// 把"文件 + 行"的断点换算成机器码地址:取**那一行的第一条指令**的地址
        /// (一行会展开成好几条指令,停在第一条才对;循环里每次经过这一行都会命中)。
        /// 文件/行对不上的断点(被删了、改过了)直接忽略。
        /// </summary>
        public HashSet<int> 算地址(IReadOnlyList<文件映射C> 调试文件表)
        {
            var 结果 = new HashSet<int>();
            foreach (var 一个 in 断点)
                if (算某行地址(调试文件表, 一个.文件, 一个.行) is int 地址) 结果.Add(地址);
            return 结果;
        }

        /// <summary>
        /// P24(第121批):地址 → **那个断点本身**(要它的 `条件`)。
        /// 同一地址上有多个断点时(同一行被下了两次是不可能的,但两个不同行可能落在同一条指令上),
        /// **带条件的优先** —— 那是用户更明确的意思。
        /// </summary>
        public Dictionary<int, 断点项C> 算地址带条件(IReadOnlyList<文件映射C> 调试文件表)
        {
            var 结果 = new Dictionary<int, 断点项C>();
            foreach (var 一个 in 断点)
            {
                if (算某行地址(调试文件表, 一个.文件, 一个.行) is not int 地址) continue;
                if (!结果.TryGetValue(地址, out var 已有)) { 结果[地址] = 一个; continue; }
                if (已有.条件.Trim().Length == 0 && 一个.条件.Trim().Length > 0) 结果[地址] = 一个;
            }
            return 结果;
        }

        /// <summary> 给某个断点设置(或清空)条件;返回 false = 那一行本来就没有断点(界面提示一句) </summary>
        public bool 设置条件(string 文件, int 行, string 条件)
        {
            var 已有 = 断点.FirstOrDefault(x => 同行(x, 文件, 行));
            if (已有 == null) return false;
            已有.条件 = (条件 ?? "").Trim();
            return true;
        }

        /// <summary> P34:一次把三样都写好(条件 / 命中次数 / 日志)—— 弹窗那一边只调这一个入口,免得三处分头写漏 </summary>
        public bool 设置(string 文件, int 行, string 条件, string 命中次数, string 日志)
        {
            var 已有 = 断点.FirstOrDefault(x => 同行(x, 文件, 行));
            if (已有 == null) return false;
            已有.条件 = (条件 ?? "").Trim();
            已有.命中次数 = (命中次数 ?? "").Trim();
            已有.日志 = (日志 ?? "").Trim();
            return true;
        }

        /// <summary> 某个断点整项(没有给 null)</summary>
        public 断点项C? 取(string 文件, int 行) =>
            断点.FirstOrDefault(x => 同行(x, 文件, 行));

        /// <summary> 某个断点的条件(没有断点或没有条件都给空串)</summary>
        public string 取条件(string 文件, int 行) =>
            断点.FirstOrDefault(x => 同行(x, 文件, 行))?.条件 ?? "";

        /// <summary> 一条给人看的摘要(按文件分组,带条件的标出来):例如 `主程序.hb 第 3、6 行(6: i == 5)` </summary>
        public string 摘要条件()
        {
            if (断点.Count == 0) return "";
            var 带条件 = 断点.Where(x => x.带附加条件).ToList();
            return 带条件.Count == 0 ? "" : "条件断点:" + string.Join(";", 带条件.Select(x => $"{Path.GetFileName(x.文件)} 第 {x.行} 行({x.附加摘要().TrimStart(':')})"));
        }

        /// <summary>
        /// 这张调试表里有没有这个文件的段 —— "这一行能不能下断点"必须先问它:
        /// 没编译过(或者表是上一个工程的)时不能拿旧表去判,否则会冤枉所有行都说"没有可执行的代码"。
        /// </summary>
        public static bool 有文件(IReadOnlyList<文件映射C> 调试文件表, string 文件) =>
            调试文件表.Any(段 => 文件判定C.同文件(段.文件, 文件));

        /// <summary>
        /// 某一行有没有"能停下来"的地址(那一行的第一条指令)。返回 null = 这一行没有可执行代码
        /// (空行、注释行、`}`、只有声明的行都可能没有)—— 下断点前用它挡一下,免得下出个永远不响的断点。
        /// </summary>
        public static int? 算某行地址(IReadOnlyList<文件映射C> 调试文件表, string 文件, int 行)
        {
            var 段 = 调试文件表.FirstOrDefault(s => 文件判定C.同文件(s.文件, 文件));
            if (段 == null) return null;
            int? 最小 = null;
            foreach (var (地址, 源行) in 段.地址到源行)
                if (源行 == 行 && (最小 == null || 地址 < 最小)) 最小 = 地址;
            return 最小;      // 表里的键已经是绝对地址,不用再加基址(第93批)
        }
    }
}
