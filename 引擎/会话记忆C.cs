using System.IO;
using System.Text.Json;

namespace hehshihbeen_compiler
{
    /// <summary> 会话记忆里的一页:文件路径 + 光标在哪 + **没存下来的正文** </summary>
    public sealed class 会话页C
    {
        /// <summary> 文件全称(没路径的新页给空串 —— 那种页恢复不了,只在标题里留个念想) </summary>
        public string 全称 { get; set; } = "";

        /// <summary> 页头显示的标题(没路径的新页就靠它认得出来)</summary>
        public string 标题 { get; set; } = "";

        /// <summary> 光标在第几行 / 第几列(1 基;0 或负数 = 没记)</summary>
        public int 行 { get; set; }
        public int 列 { get; set; }

        /// <summary>
        /// **改过还没保存的正文**(与磁盘上那份不同时才记;一样就是空串)。
        /// 为什么连正文一起记:用户当班点名的"未保存恢复" —— 崩了 / 手滑关了,那一份还在。
        /// 恢复时**不覆盖磁盘**、也不自动存盘:正文回到编辑器里,页头照样挂着 `*`,存不存由用户按 Ctrl+S。
        /// </summary>
        public string 未保存正文 { get; set; } = "";

        /// <summary> 有没存下来的东西吗 </summary>
        public bool 有未保存 => 未保存正文.Length > 0;

        public string 显示名 => 标题.Length > 0 ? 标题 : Path.GetFileName(全称);
    }

    /// <summary> 一个工程的会话(开过哪些页 / 光标在哪 / 窗口长什么样 / 谁是当前页)</summary>
    public sealed class 会话工程C
    {
        public List<会话页C> 页表 { get; set; } = [];

        /// <summary> 当前页在 `页表` 里的下标(-1 = 没记)</summary>
        public int 当前页 { get; set; } = -1;

        // 窗口位置与大小(最小化 / 最大化时不更新这几格 —— 那样记下来下次窗口会"开成一条线")
        public double 左 { get; set; } = double.NaN;
        public double 上 { get; set; } = double.NaN;
        public double 宽 { get; set; } = double.NaN;
        public double 高 { get; set; } = double.NaN;
        public bool 最大化 { get; set; }

        /// <summary> 上次离开的时刻(会话文件按它裁旧)</summary>
        public DateTime 最后离开 { get; set; } = DateTime.Now;

        /// <summary> 窗口那四格齐全、而且看着像个真窗口(宽高至少 400×300)吗 </summary>
        public bool 有窗口位置 =>
            !double.IsNaN(左) && !double.IsNaN(上) && !double.IsNaN(宽) && !double.IsNaN(高)
            && 宽 >= 400 && 高 >= 300;
    }

    /// <summary>
    /// **会话记忆**(P36,2026-10-07;用户排的七件里的第 3 件)。
    ///
    /// 记什么(全在**一次**落盘里):
    ///   · 开过哪些页(文件全称)+ **每页光标在第几行第几列**;
    ///   · **改过还没保存的正文**(崩溃 / 手滑关窗也不丢);
    ///   · 当前是哪一页;
    ///   · **窗口位置与大小**(最大化也记)。
    /// 不记什么:**断点**——它本来就跟着工程走(项目目录里的 `断点.hbd`,见 `断点表C`),
    /// 这里再抄一份只会变成两个事实源。这一条写在类注释里,免得后来者"顺手补上"。
    ///
    /// 落点:`<程序目录>\会话.json`,**一份文件装所有工程**(键 = 工程目录或工程文件全称)。
    /// 为什么不是"每个工程一个文件":那会在用户的项目目录里拉一坨小文件(项目目录是用户的;
    /// 程序自己的记忆放程序自己旁边)。
    /// </summary>
    public sealed class 会话表C
    {
        /// <summary> 最多记几个工程的会话(按最后离开时间裁;够用了 —— 桌面启动页也只记 15 个最近项目)</summary>
        public const int 最多工程数 = 20;

        private static readonly JsonSerializerOptions 选项 = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary> 工程键 → 那个工程的会话 </summary>
        public Dictionary<string, 会话工程C> 工程表 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 进程内共用的一份(与 `选项` / `最近项目` 一个路子)</summary>
        public static 会话表C 当前 { get; } = 加载();

        public static string 文件路径默认 => Path.Combine(程序目录C.数据目录, "会话.json");

        // ---------------- 读一个工程 / 写一个工程 ----------------

        /// <summary> 取某个工程的会话(没有就给一份空的;**不**顺手建进去)</summary>
        public 会话工程C 取(string? 工程键) =>
            工程键 != null && 工程表.TryGetValue(工程键, out var 有) ? 有 : new 会话工程C();

        /// <summary> 写某个工程的会话(空键忽略),顺手裁旧 </summary>
        public void 存(string? 工程键, 会话工程C 会话)
        {
            if (string.IsNullOrWhiteSpace(工程键)) return;
            会话.最后离开 = DateTime.Now;
            工程表[工程键] = 会话;
            裁剪();
            保存();
        }

        /// <summary> 清掉某个工程的会话(用户点了"不要恢复"之类)</summary>
        public void 清(string? 工程键)
        {
            if (string.IsNullOrWhiteSpace(工程键)) return;
            工程表.Remove(工程键);
            保存();
        }

        /// <summary> 按最后离开时间裁到 `最多工程数` 个 </summary>
        private void 裁剪()
        {
            if (工程表.Count <= 最多工程数) return;
            var 留 = 工程表.OrderByDescending(x => x.Value.最后离开).Take(最多工程数)
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            工程表 = 留;
        }

        // ---------------- 读写(坏了当"没有会话",不能让程序打不开) ----------------

        public string 到文本() => JsonSerializer.Serialize(this, 选项);

        public static 会话表C 从文本(string json)
        {
            try
            {
                var 表 = JsonSerializer.Deserialize<会话表C>(json) ?? new 会话表C();
                表.工程表 ??= new Dictionary<string, 会话工程C>(StringComparer.OrdinalIgnoreCase);
                foreach (var 一个 in 表.工程表.Values)
                {
                    一个.页表 ??= [];
                    // 没路径、又没标题的页恢复不了:读进来就扔掉(免得每次打开都报"少了 1 页")
                    一个.页表.RemoveAll(页 => 页.全称.Length == 0 && 页.标题.Length == 0);
                }
                return 表;
            }
            catch (JsonException) { return new 会话表C(); }
        }

        public static 会话表C 加载(string? 路径 = null)
        {
            string 文件 = 路径 ?? 文件路径默认;
            if (!File.Exists(文件)) return new 会话表C();
            try { return 从文本(File.ReadAllText(文件)); }
            catch { return new 会话表C(); }
        }

        public void 保存(string? 路径 = null)
        {
            string 文件 = 路径 ?? 文件路径默认;
            try
            {
                裁剪();
                Directory.CreateDirectory(Path.GetDirectoryName(文件) ?? ".");
                File.WriteAllText(文件, 到文本());
            }
            catch { }
        }

        // ---------------- 工程键 ----------------

        /// <summary>
        /// 一个工程的**会话键**:有工程文件(.hbp)就用它的全称;没有(临时按目录编的那种)就用目录。
        /// 归一三件事:全路径 + 去尾分隔符 + **大小写折成大写**(Windows 上 `E:\示例` 与 `e:\示例` 是同一个地方;
        /// 字典那边的 `StringComparer.OrdinalIgnoreCase` 在"查"的时候管用,可 `ContainsKey` 之外的
        /// 相等判断(自检、去重)还得靠键本身长得一样)。
        /// </summary>
        public static string 键(string? 工程文件全称, string? 目录)
        {
            string 用 = !string.IsNullOrWhiteSpace(工程文件全称) ? 工程文件全称! : (目录 ?? "");
            if (string.IsNullOrWhiteSpace(用)) return "";
            try { return Path.GetFullPath(用).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant(); }
            catch { return 用.Trim().ToUpperInvariant(); }
        }
    }
}
