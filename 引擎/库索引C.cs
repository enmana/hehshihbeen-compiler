// ============================================================
// 库索引C —— "编辑器认库"的**唯一事实源**(补全与高亮共用这一份)
//
// 要解决的是两处"没跟上"(用户 2026-10-07 当班点名):
//   · **补全**:`模块.` 的成员原来只认"最近一次编译结果"的名字表(`MainWindow.代码检查.cs` 的 `取补全名字表`)
//     ⇒ 没编译过 / 工程里没那一份的时候,`控制台.` 后面一个成员都不给;
//   · **高亮**:名字上色只扫**本文件里声明过的名字**(`源码名字扫描C.取(全文)` 那一份)
//     ⇒ 库里声明的函数名(示例里写的 `拼表格行`)永远不上色。
// 两处的共同缺口 = **没人读 `Sample\库\*.hb` 这份事实源**:帮助站点《类库》与 `关键字与方法清单.md` §6
// 早就读它(`库函数清单C`),所以那两处"库里加一个方法就自动多一行"。这一份把同一份数据接到编辑器上。
//
// **口径(用户 2026-10-07 拍板:窄)**:只认**本文件 + 它的 `包含` 链(最多两层)**上那几个库 ——
// 没 `包含` 进来的库一个词都不给(与 `补全名字C` 那句"宁可不给,也不给错的"同一把尺子:
// 补出编不过的调用比不补更糟)。
//
// **数据来源**:库源码本身 —— 模块名读 `模块 X` 那一行,公开方法走 `帮助站点C.取公开方法`
// (与类库页、`关键字与方法清单.md` §6 同一份解析,口径只有一处)。
// **缓存**:按 (路径, 修改时间, 长度),与 `补全词表C` 原来那套包含缓存同一个口径。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;

namespace hehshihbeen_compiler
{
    /// <summary> 一组库文件里能用的名字:模块名 + 公开方法(名 / 签名 / 作用) </summary>
    public sealed class 库名字集C
    {
        /// <summary> 库的模块名(`模块 控制台` 那一行;**没有 `模块` 头的老库不进这一档** —— 它们的函数是全局名) </summary>
        public HashSet<string> 模块名 { get; } = new(StringComparer.Ordinal);

        /// <summary> 库里声明的**公开方法名**(给上色用;没有模块头的老库也算) </summary>
        public HashSet<string> 方法名 { get; } = new(StringComparer.Ordinal);

        /// <summary> 模块名 → 这个库的公开方法(名 / 签名 / 作用);`模块.` 的成员补全查它 </summary>
        public Dictionary<string, List<(string 名, string 签名, string 作用)>> 模块方法 { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 一个词都没有(没 `包含` 任何库时就是它 —— 调用方照旧走路,不必特判) </summary>
        public bool 空 => 模块名.Count == 0 && 方法名.Count == 0;
    }

    /// <summary> 见文件头那段说明:含进来的库 = 模块名 + 公开方法,数据取自库源码 </summary>
    public static class 库索引C
    {
        /// <summary> `包含` 往下最多几层(本文件算第 0 层;与老那套包含缓存同一口径) </summary>
        public const int 最深 = 2;

        private static readonly Dictionary<string, (DateTime 时间, long 长度, string 内容)> _内容缓存 =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, (string 模块, List<(string 名, string 签名, string 作用)> 方法们)> _解析缓存 =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _锁 = new();

        /// <summary> 清缓存(自检用:免得上一段的缓存串到下一段) </summary>
        public static void 清缓存()
        {
            lock (_锁) { _内容缓存.Clear(); _解析缓存.Clear(); }
        }

        /// <summary>
        /// 本文件 `包含` 进来的文件(路径 + 内容,最多两层,同一个文件只算一次)。
        /// 读不了 / 路径坏的**当场跳过** —— 补全与上色都只是帮手,不能因为一个坏路径把编辑搅停。
        /// </summary>
        public static List<(string 路径, string 内容)> 包含链(string? 源码, string? 本文件路径)
        {
            var 结果 = new List<(string, string)>();
            if (string.IsNullOrEmpty(本文件路径)) return 结果;
            string 目录 = Path.GetDirectoryName(本文件路径) ?? "";
            if (目录.Length == 0) return 结果;

            var 见过 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void 往下(string 文本, string 当前目录, int 层)
            {
                if (层 > 最深) return;
                foreach (string 行 in 文本.Split('\n'))
                {
                    if (补全词表C.取包含路径(行) is not { } 相对) continue;
                    string 目标;
                    try { 目标 = Path.GetFullPath(Path.Combine(当前目录, 相对)); }
                    catch { continue; }
                    if (!见过.Add(目标)) continue;
                    string? 内容 = 读内容(目标);
                    if (内容 == null) continue;
                    结果.Add((目标, 内容));
                    if (层 < 最深) 往下(内容, Path.GetDirectoryName(目标) ?? 当前目录, 层 + 1);
                }
            }

            往下(源码 ?? "", 目录, 0);
            return 结果;
        }

        /// <summary>
        /// 本文件 + 它 `包含` 进来的库,合成一份库名字集。
        /// `现场正文` 优先(编辑器里还没存盘的改动也算),没给就从 `本文件路径` 读盘。
        /// </summary>
        public static 库名字集C 取(string? 本文件路径, string? 现场正文 = null)
        {
            string? 根 = 现场正文;
            if (根 == null && !string.IsNullOrEmpty(本文件路径)) 根 = 读内容(本文件路径);

            var 集 = new 库名字集C();
            foreach (var (路径, 内容) in 包含链(根, 本文件路径))
            {
                var (模块, 方法们) = 解析(路径, 内容);
                foreach (var (名, _, _) in 方法们) 集.方法名.Add(名);
                if (模块.Length == 0) continue;
                集.模块名.Add(模块);
                集.模块方法[模块] = 方法们;
            }
            return 集;
        }

        /// <summary>
        /// 一个库文件:`模块 X` 那一行给的名字 + 公开方法(名 / 签名 / 作用)。
        /// 方法那一段走 `帮助站点C.取公开方法`;**解析结果按 路径+修改时间+长度 缓存**。
        /// </summary>
        public static (string 模块, List<(string 名, string 签名, string 作用)> 方法们) 解析(string 路径, string 内容)
        {
            string 键 = "";
            try
            {
                var 信息 = new FileInfo(路径);
                if (信息.Exists) 键 = $"{路径}|{信息.LastWriteTimeUtc.Ticks}|{信息.Length}";
            }
            catch { /* 拿不到就用现场内容算,不缓存 */ }

            if (键.Length > 0) lock (_锁) if (_解析缓存.TryGetValue(键, out var 旧)) return 旧;

            string 模块 = 取模块名(内容);
            var 方法们 = 帮助站点C.取公开方法(内容);

            if (键.Length > 0) lock (_锁) _解析缓存[键] = (模块, 方法们);
            return (模块, 方法们);
        }

        /// <summary>
        /// `模块 X` 那一行给的名字(没有给空串)。只认**第一条真语句**(语言规定 `模块` 必须是文件第一条),
        /// 注释行跳过;`模块` / `module` 两种写法都认。
        /// </summary>
        public static string 取模块名(string 内容)
        {
            if (string.IsNullOrEmpty(内容)) return "";
            foreach (string 原行 in 内容.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string 行 = 原行.Trim();
                if (行.Length == 0) continue;
                if (行.StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (string 头 in new[] { "模块 ", "module " })
                    if (行.StartsWith(头, StringComparison.Ordinal))
                    {
                        string 名 = 行[头.Length..].Trim().TrimEnd('{').Trim();
                        int 空 = 名.IndexOfAny([' ', '\t']);
                        return 空 > 0 ? 名[..空] : 名;
                    }
                return "";      // 第一条真语句不是模块头 ⇒ 这个文件没有模块(老库那样)
            }
            return "";
        }

        private static string? 读内容(string 路径)
        {
            try
            {
                var 信息 = new FileInfo(路径);
                if (!信息.Exists) return null;
                lock (_锁)
                    if (_内容缓存.TryGetValue(路径, out var 旧) && 旧.时间 == 信息.LastWriteTimeUtc && 旧.长度 == 信息.Length)
                        return 旧.内容;
                string 内容 = File.ReadAllText(路径);
                lock (_锁) _内容缓存[路径] = (信息.LastWriteTimeUtc, 信息.Length, 内容);
                return 内容;
            }
            catch { return null; }
        }
    }
}
