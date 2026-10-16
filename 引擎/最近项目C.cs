using System.IO;
using System.Text.Json;

namespace hehshihbeen_compiler
{
    /// <summary> 启动页里的一条"最近打开的项目" </summary>
    public sealed class 最近项目记录C
    {
        /// <summary> 工程文件(.hbp)的完整路径 —— 这是项目的唯一标识 </summary>
        public string 工程路径 { get; set; } = "";

        /// <summary> 显示用的项目名(取自工程文件) </summary>
        public string 名称 { get; set; } = "";

        /// <summary> 最后一次打开的时间 </summary>
        public DateTime 最后打开 { get; set; } = DateTime.Now;

        /// <summary> 时间戳文本(启动页显示) </summary>
        public string 时间文本 => 最后打开.ToString("yyyy-MM-dd HH:mm");

        /// <summary> 相对时间("3 分钟前" / "昨天 20:14" / 具体日期) </summary>
        public string 相对时间文本 => 最近项目表C.相对时间(最后打开);

        public string 显示名 => 名称.Length > 0 ? 名称 : Path.GetFileNameWithoutExtension(工程路径);

        public bool 文件还在 => File.Exists(工程路径);

        /// <summary> 文件不在了给个提示文本(引擎层不能用 WPF 的 Visibility,所以用文本) </summary>
        public string 缺失提示 => 文件还在 ? "" : "  (文件已不在)";
    }

    /// <summary>
    /// 最近打开的项目表(启动页的数据来源)。
    /// 它**只是历史记录**,不是"会话状态":启动时不会按它自动打开任何项目 ——
    /// 想接着上次干活,就在启动页上点一下那条记录。
    /// </summary>
    public sealed class 最近项目表C
    {
        public const int 最多条数 = 15;

        private static readonly JsonSerializerOptions 选项 = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public List<最近项目记录C> 项目表 { get; set; } = [];

        /// <summary> 进程内共用的一份(启动页与主窗口读的是同一个;避免各自加载一遍)</summary>
        public static 最近项目表C 当前 { get; } = 加载();

        /// <summary> 默认落盘位置(程序目录下的 最近项目.json) </summary>
        public static string 文件路径默认 => Path.Combine(程序目录C.数据目录, "最近项目.json");

        /// <summary> 记一条:同一工程只留一条并刷新时间;超过上限砍掉最旧的 </summary>
        public void 记住(string 工程路径, string? 名称 = null, DateTime? 时间 = null)
        {
            if (string.IsNullOrWhiteSpace(工程路径)) return;
            string 全 = Path.GetFullPath(工程路径);
            if (名称 == null && File.Exists(全))
            {
                try { 名称 = 工程文件C.加载(全).名称; } catch { 名称 = null; }
            }
            项目表.RemoveAll(x => string.Equals(x.工程路径, 全, StringComparison.OrdinalIgnoreCase));
            项目表.Add(new 最近项目记录C
            {
                工程路径 = 全,
                名称 = 名称 ?? Path.GetFileNameWithoutExtension(全),
                最后打开 = 时间 ?? DateTime.Now,
            });
            裁剪();
        }

        public void 移除(string 工程路径)
        {
            string 全 = Path.GetFullPath(工程路径);
            项目表.RemoveAll(x => string.Equals(x.工程路径, 全, StringComparison.OrdinalIgnoreCase));
        }

        public void 清空() => 项目表.Clear();

        /// <summary> 按最后打开时间倒序 </summary>
        public List<最近项目记录C> 取全部() => [.. 项目表.OrderByDescending(x => x.最后打开)];

        private void 裁剪()
        {
            项目表 = [.. 项目表.OrderByDescending(x => x.最后打开).Take(最多条数)];
        }

        // ---------------- 读写 ----------------
        public string 到文本() => JsonSerializer.Serialize(this, 选项);

        public static 最近项目表C 从文本(string json)
        {
            try
            {
                var 表 = JsonSerializer.Deserialize<最近项目表C>(json) ?? new 最近项目表C();
                表.项目表 ??= [];
                return 表;
            }
            catch (JsonException)
            {
                return new 最近项目表C();      // 坏了就当没有,不要让启动页打不开
            }
        }

        public static 最近项目表C 加载(string? 路径 = null)
        {
            string 文件 = 路径 ?? 文件路径默认;
            if (!File.Exists(文件)) return new 最近项目表C();
            try { return 从文本(File.ReadAllText(文件)); }
            catch { return new 最近项目表C(); }
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

        /// <summary> 时间戳 → 说明("刚刚" / "5 分钟前" / "昨天 20:14" / "2026-09-11 20:14") </summary>
        public static string 相对时间(DateTime 时间, DateTime? 现在 = null)
        {
            DateTime 当下 = 现在 ?? DateTime.Now;
            TimeSpan 差 = 当下 - 时间;
            if (差.TotalSeconds < 0) return 时间.ToString("yyyy-MM-dd HH:mm");
            if (差.TotalMinutes < 1) return "刚刚";
            if (差.TotalMinutes < 60) return $"{(int)差.TotalMinutes} 分钟前";
            if (差.TotalHours < 24 && 时间.Date == 当下.Date) return $"今天 {时间:HH:mm}";
            if (时间.Date == 当下.Date.AddDays(-1)) return $"昨天 {时间:HH:mm}";
            if (差.TotalDays < 7) return $"{(int)差.TotalDays} 天前 {时间:HH:mm}";
            return 时间.ToString("yyyy-MM-dd HH:mm");
        }
    }
}
