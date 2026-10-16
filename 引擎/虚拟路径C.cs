using System;
using System.Collections.Generic;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// H64:**虚拟路径**的规则(唯一一处)。手机端走 SAF 挑"系统目录"时,拿回来的是
    /// `content://` 树、**不是文件系统路径**;引擎的文件能力吃的是真路径,两头对不上。
    ///
    /// 这里的办法:给这样的树起一个**不合法、因此永远不会撞上真文件**的抽象路径 ——
    ///
    ///     内容://<树标识>/<树里的相对路径>
    ///
    ///   · `内容:/` 那一段带一个冒号,Windows / Linux / Android 的真路径里都出现不了 ⇒
    ///     `是虚拟()` 一判就知道该走哪一边,不必先问盘;
    ///   · 分隔符统一是 `/`(与真路径的 `\` / `/` 都能对上,见 `规整`);
    ///   · **引擎不认识、也不需要认识** `<树标识>` 是什么 —— 它只是"哪一棵被授权的树"的一个记号,
    ///     由平台那一边(手机端 SAF 适配层)自己解释;
    ///   · 这层规则是**纯字符串函数**:不读盘、不看平台,命令行自检直接喂字符串就能钉住。
    ///
    /// 为什么要单开一份:全仓到处用 `Path.GetDirectoryName` / `Path.Combine` / `Path.GetFullPath`
    /// 拼路径 —— 它们**会把 `内容://` 当成怪文件名的相对路径**去解析(`Path.Combine("内容://树",
    /// "主程序.hb")` 在 Windows 上得到的是 `内容://树\主程序.hb`,在手机上是 `/内容:/树/主程序.hb`),
    /// 于是同一份文件在两条路上算出两个不同的名字、标签页与编译器互相认不出来。
    /// ⇒ **凡是要作用在可能来自系统目录的路径上的那几处**,一律改调这里的方法。
    /// </summary>
    public static class 虚拟路径C
    {
        /// <summary> 虚拟路径的记号(前缀)。见类注释:带冒号所以不可能撞上真路径 </summary>
        public const string 记号 = "内容://";

        /// <summary> 这条路径是不是"系统目录里的一份"(见类注释;空 / null 一律不算)。
        /// **也认"被 `Path.GetDirectoryName` 洗过一遍"的写法**:那种写法把 `内容://树X/工程` 变成
        /// `内容:\树X\工程`(`//` 收成一个 `\`),光比前缀就漏认了 —— 这一类漏认很难查,
        /// 所以判据放宽成"记号里的斜杠与冒号之间允许夹反斜杠"(见 `规整`) </summary>
        public static bool 是虚拟(string? 路径)
        {
            if (string.IsNullOrEmpty(路径)) return false;
            string 净 = 路径!;
            if (净.StartsWith(记号, StringComparison.Ordinal)) return true;
            // `内容:\树…` / `内容:\\树…` 都收成 `内容://树…` 再比(前两个字符固定是"内容:")
            if (净.Length > 3 && 净.StartsWith("内容:", StringComparison.Ordinal))
                return 规整(净).StartsWith(记号, StringComparison.Ordinal);
            return false;
        }

        /// <summary> 任何路径的**统一写法**:反斜杠一律换成 `/`(虚拟路径本来就只认 `/`)。
        /// 只规整分隔符,不动大小写、不做绝对化 —— 绝对化那一手(真路径)仍在调用方。
        /// **记号的修复也在这儿**:`内容:\树X`(被 `Path.GetDirectoryName` 洗过)收回来就是 `内容://树X` </summary>
        public static string 规整(string? 路径)
        {
            string 净 = (路径 ?? "").Replace('\\', '/');
            if (净.StartsWith("内容:/", StringComparison.Ordinal) && !净.StartsWith(记号, StringComparison.Ordinal))
                净 = 记号 + 净.TrimStart('内', '容', ':', '/');
            return 净;
        }

        /// <summary>
        /// 拼一段子路径(虚拟与真路径都能拼):虚拟走 `/` 手工拼(不许走 `Path.Combine`,
        /// 那会把 `内容://` 当相对路径去解析),真路径仍走 `Path.Combine`。
        /// 前一段为空 ⇒ 返回后一段;后一段是绝对真路径 ⇒ 返回后一段(与 `Path.Combine` 同口径)。
        /// </summary>
        public static string 拼接(string? 前, string? 后)
        {
            string 前段 = 前 ?? "", 后段 = 后 ?? "";
            if (前段.Length == 0) return 后段;
            if (后段.Length == 0) return 前段;
            if (是虚拟(前段))
            {
                if (是虚拟(后段)) return 后段;                        // 绝对虚拟路径直接赢
                return 前段.TrimEnd('/') + "/" + 后段.TrimStart('/');
            }
            if (是虚拟(后段)) return 后段;
            return System.IO.Path.Combine(前段, 后段);
        }

        /// <summary> 光名(`主程序.hb` / 虚拟路径的最后一段);空 ⇒ 空串 </summary>
        public static string 文件名(string? 路径)
        {
            string 净 = 规整(路径).TrimEnd('/');
            if (净.Length == 0) return "";
            int 斜 = 净.LastIndexOf('/');
            return 斜 < 0 ? 净 : 净[(斜 + 1)..];
        }

        /// <summary> 去扩展名的光名(`库\排序.hb` → `排序`) </summary>
        public static string 文件名无扩展(string? 路径)
        {
            string 名 = 文件名(路径);
            int 点 = 名.LastIndexOf('.');
            return 点 <= 0 ? 名 : 名[..点];
        }

        /// <summary>
        /// 所在目录(与 `Path.GetDirectoryName` 同形,但认虚拟路径):`内容://树/库/排序.hb` →
        /// `内容://树/库`;真路径仍走 `Path.GetDirectoryName`(盘根、盘符那一套照旧)。
        /// 取不出目录 ⇒ 空串。
        /// </summary>
        public static string 所在目录(string? 路径)
        {
            string 净 = 规整(路径);
            if (净.Length == 0) return "";
            if (!是虚拟(净))
            {
                try { return System.IO.Path.GetDirectoryName(净) ?? ""; } catch { return ""; }
            }
            string 去尾 = 净.TrimEnd('/');
            int 斜 = 去尾.LastIndexOf('/');
            // `内容://树` 再往上就没有目录了(`内容:/` 不是目录:`/` 那一段是记号本身)
            return 斜 < 记号.Length ? "" : 去尾[..斜];
        }

        /// <summary> 光名 = 不带目录的那一种写法(`主程序.hb`);带了目录就不是(与 `文件判定C` 同一口径) </summary>
        public static bool 是光名(string? 路径) => 规整(路径).IndexOf('/') < 0;

        /// <summary> 把"可能带 `.` / `..` / 重复斜杠"的虚拟路径收成正形(真路径返回原样字符串,不动盘)。
        /// 相对部分要先跟基准目录拼好再调它 —— 这里只做字符串层面的收拢 </summary>
        public static string 收正(string? 路径)
        {
            string 净 = 规整(路径);
            if (!是虚拟(净)) return 净;
            string 尾 = 净[记号.Length..];
            var 段 = new List<string>();
            foreach (string 一 in 尾.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (一 == ".") continue;
                if (一 == "..")
                {
                    if (段.Count > 0) 段.RemoveAt(段.Count - 1);
                    continue;
                }
                段.Add(一);
            }
            var 料 = new StringBuilder(记号);
            料.Append(string.Join("/", 段));
            return 料.ToString();
        }

        /// <summary> 给用户看的一句(虚拟路径把记号摘掉,免得界面上出现一串 `内容://…`) </summary>
        public static string 显示(string? 路径)
        {
            string 净 = 规整(路径);
            return 是虚拟(净) ? 净[记号.Length..] : 净;
        }
    }
}
