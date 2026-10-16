using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>一次"在 Linux 那一侧编 / 跑"的结果</summary>
    public sealed class 跨平台结果C
    {
        /// <summary>cc 那一步过没过(看容器回报的 `HB_CC=0`,不看日志里像不像错)</summary>
        public bool 编过;

        /// <summary>程序自己的退出码;编都没编过时给 cc 的退出码</summary>
        public int 退出码 = -1;

        /// <summary>程序 stdout 的**原始字节**(要逐字节比,不能先解码)</summary>
        public byte[] 输出 = [];

        /// <summary>cc 的话 + 程序 stderr(那行 `HB_CC=` 标记已经摘掉)</summary>
        public string 日志 = "";

        /// <summary>这一趟为什么没成(成的时候是空串)</summary>
        public string 说明 = "";

        /// <summary>stdout 当文字看(报错说明用;判据一律走字节)</summary>
        public string 输出文字 => Encoding.UTF8.GetString(输出);
    }

    /// <summary>
    /// R3-6-4 批 5:**跨平台验证**用的"Linux 那一侧的 C 工具链"。
    ///
    /// 批 1~4 把"HB 机器码 → C 源码 → 原生 exe"这条路在 **Windows / MSVC** 上走通了,
    /// 但生成的 C 源码里那整套 **POSIX 分支**(`opendir` / `unlink` / `rmdir` / `stat` …)从来没被编过一次 ——
    /// 这一批要的就是那句:**同一份 C 源码在 Linux 上 `cc` 一遍、跑出同一结果**。
    ///
    /// 这台开发机上没有 Linux,借 **docker 容器**当那台 Linux:
    ///   ① 只挂**仓库这一层**(容器里是 `/hb`)、`-w /hb` —— 标准工程里的相对路径(示例12 那种)两边才对得上;
    ///   ② 一趟容器里先 `cc` 再跑;编译结果用 stderr 上的一行 `HB_CC=<码>` 报回来 ——
    ///      "cc 编过没有"与"程序退出码是多少"是两件事,不能挤进同一个退出码里;
    ///   ③ **绝不偷偷拉镜像**:镜像得先在本地(`docker pull gcc:14` 一次),没有就如实说明白。
    ///
    /// 镜像可以用环境变量 `HB_C跨平台镜像` 换(想拿 clang 那类的镜像再验一遍另一套编译器就换它)。
    /// **macOS 这一批没有真机可验**(见 `未做任务清单.md` §21),如实写明、不冒充。
    /// </summary>
    public static class 跨平台工具链C
    {
        /// <summary>缺省镜像(本地没有就 `docker pull` 一次;自检里没有就逐条记跳过)</summary>
        public const string 缺省镜像 = "gcc:14";

        /// <summary>容器里挂仓库根的那个点</summary>
        public const string 容器挂载点 = "/hb";

        private const string 编译标记 = "HB_CC=";
        private const int 超时秒 = 600;

        /// <summary>这一次用哪个镜像(`HB_C跨平台镜像` 可盖过)</summary>
        public static string 镜像
        {
            get
            {
                string? 自己 = Environment.GetEnvironmentVariable("HB_C跨平台镜像");
                return string.IsNullOrWhiteSpace(自己) ? 缺省镜像 : 自己.Trim();
            }
        }

        /// <summary>这台机器上有没有能用的 Linux 运行器(docker 起着 + 镜像已经在本地);没有就把理由说清</summary>
        public static bool 探一下(out string 说明)
        {
            var 版本 = 跑("docker", ["version", "--format", "{{.Server.Version}}"], null, 60);
            if (!版本.起得来)
            {
                说明 = "没找到 docker(不在 PATH 里、或者起不来)—— 装 Docker Desktop 并让它跑起来;" +
                    "或者在任意一台有 `cc` 的 Linux 上直接 `cc 生成的.c -o 程序`";
                return false;
            }
            if (版本.退出码 != 0)
            {
                说明 = "本机有 docker、但引擎没起(把 Docker Desktop 起来再跑):" + 一行化(版本.误, 120);
                return false;
            }

            var 查镜像 = 跑("docker", ["image", "inspect", "--format", "{{.Id}}", 镜像], null, 120);
            if (查镜像.退出码 != 0)
            {
                说明 = $"docker 起着,但镜像 `{镜像}` 不在本地 —— 先拉一次:`docker pull {镜像}`" +
                    "(想换镜像就设环境变量 `HB_C跨平台镜像`)";
                return false;
            }

            说明 = $"docker {版本.出文本.Trim()} / 镜像 {镜像}";
            return true;
        }

        /// <summary>
        /// 把一份 `.c` 送进 Linux 容器:`cc` 编成原生程序,再就地跑起来。
        /// `状态路径` 非空就补 `--hb-dump <路径>`(整机状态转储落进挂载目录,宿主读得到);
        /// `冻结秒` 非空就补 `--hb-now <秒>`(把 `当前时间()` 冻住,不然两边的整块内存必然差几个字节);
        /// `现在毫秒` 非空就补 `--hb-now-ms <毫秒>`(把 `当前毫秒()` 也冻住 —— **H113**:
        /// 这一组原来只冻了秒,那一组里只要有工程用到 `当前毫秒()`,两侧就各看各的真钟,早晚假红;
        /// 参数名与刻度口径跟 Windows 那侧 `跑原生带状态` 的那一个一字不差)。
        /// `优化档` 就是给 cc 的那个开关(`-O2` / `-O0`)。
        /// <para>
        /// **`图形路径` / `事件脚本路径`(2026-10-14,H109 的修法)**:非空就补
        /// `--hb-图形 <落点>` 与 `--hb-事件脚本 <脚本>` —— 与 Windows 那侧
        /// (`自检\综合自检C.C后端全量对拍.cs` 的 `跑原生带状态`)同一套口径。**不给会出事**:
        /// 带 UI 的标准工程主循环是"收不到事件就接着刷",而容器里没有用户去关窗
        /// ⇒ 一路刷到超时(实测 `示例01` 打了 5,263,278 字节、单组烧 1842 秒)。
        /// </para>
        /// <para>
        /// **`限时秒`**:单次跑的上限,不给就用类里那个 <see cref="超时秒"/>(600)。
        /// 标准工程那一档给 180 秒(与 Windows 那侧 `WaitForExit(180_000)` 对齐)——
        /// 挂死时是"3 分钟记一条 FAIL",不是"10 分钟烧掉整条预算"。
        /// </para>
        /// </summary>
        public static 跨平台结果C 编并跑(string 仓库根, string c源文件, string 程序路径, string 优化档,
            string? 状态路径 = null, long? 冻结秒 = null, string? 标准输入 = null,
            string? 图形路径 = null, string? 事件脚本路径 = null, int? 限时秒 = null, uint? 现在毫秒 = null)
        {
            string 源 = 容器路径(仓库根, c源文件);
            string 程序 = 容器路径(仓库根, 程序路径);
            if (源.Length == 0 || 程序.Length == 0)
                return new 跨平台结果C { 说明 = "源文件或产物不在仓库这一层里(容器只挂仓库)" };

            // 注意:**sh 的变量名只认 ASCII** —— 写中文名当场变成 "码=0: not found"(实测踩过)
            var 脚本 = new StringBuilder();
            脚本.Append("cc ").Append(优化档).Append(" -std=c11 -o '").Append(程序).Append("' '").Append(源).Append("' -lm; ");
            脚本.Append("hb_rc=$?; echo ").Append(编译标记).Append("$hb_rc >&2; ");
            脚本.Append("if [ $hb_rc -ne 0 ]; then exit $hb_rc; fi; ");
            脚本.Append("exec '").Append(程序).Append("'");
            if (状态路径 is { Length: > 0 })
                脚本.Append(" --hb-dump '").Append(容器路径(仓库根, 状态路径)).Append("'");
            // H109:图形工程必须挂无头呈现 + 喂事件脚本 —— 不给的话两边一起转到天荒地老(见上面的说明)
            if (图形路径 is { Length: > 0 })
                脚本.Append(" --hb-图形 '").Append(容器路径(仓库根, 图形路径)).Append("'");
            if (事件脚本路径 is { Length: > 0 })
                脚本.Append(" --hb-事件脚本 '").Append(容器路径(仓库根, 事件脚本路径)).Append("'");
            if (冻结秒 is { } 秒)
                脚本.Append(" --hb-now ").Append(秒.ToString(CultureInfo.InvariantCulture));
            if (现在毫秒 is { } 毫秒)
                脚本.Append(" --hb-now-ms ").Append(毫秒.ToString(CultureInfo.InvariantCulture));

            int 用超时 = 限时秒 ?? 超时秒;
            var 参数 = new List<string> { "run", "--rm" };
            if (标准输入 != null) 参数.Add("-i");
            参数.AddRange(["-v", 仓库根 + ":" + 容器挂载点, "-w", 容器挂载点, 镜像, "sh", "-c", 脚本.ToString()]);

            var 跑过 = 跑("docker", 参数.ToArray(),
                标准输入 == null ? null : Encoding.UTF8.GetBytes(标准输入), 用超时);
            if (!跑过.起得来) return new 跨平台结果C { 说明 = "起不了 docker:" + 一行化(跑过.误, 160) };
            if (跑过.超时) return new 跨平台结果C { 说明 = $"Linux 那侧 {用超时} 秒没跑完(超时)" };

            bool 编过 = false;
            var 净误 = new List<string>();
            foreach (string 行 in 跑过.误.Replace("\r\n", "\n").Split('\n'))
            {
                if (行.StartsWith(编译标记, StringComparison.Ordinal))
                {
                    编过 = 行[编译标记.Length..].Trim() == "0";
                    continue;
                }
                净误.Add(行);
            }

            return new 跨平台结果C
            {
                编过 = 编过,
                退出码 = 跑过.退出码,
                输出 = 跑过.出字节,
                日志 = string.Join("\n", 净误).Trim(),
                说明 = 编过 ? "" : "cc 没编过(日志里是 gcc 的话)",
            };
        }

        /// <summary>
        /// R3-6-5 批 2:在**真 X 服务器**上跑同一份探针 —— 容器里先起 `Xvfb`,再 `cc` 编、跑、收输出。
        ///
        /// 为什么要单开一条:这一档验的是"**真开一屏**"(硬规矩 6 的机器那一份)——
        /// `gcc:14` 那种镜像里没有 X,只能验到"开窗给状态 25"那一支;要有 X 才验得到
        /// "开窗给 0 + 从窗口把像素读回来对颜色"。口径与"零依赖、不动镜像"不冲突:
        /// **不装、不拉、不改任何镜像** —— 由调用方给一个**本来就带 Xvfb** 的镜像(见 `有X服务器`)。
        /// 脚本里 `Xvfb` 起在后台、跑完 `kill` 掉(不收尾的话容器不退出 —— 实测踩过)。
        /// 镜像用环境变量 `HB_跨平台X镜像` 换;没给就走 `镜像` 那一份(那时 `有X服务器` 会先拦住)。
        /// 参数口径与 <see cref="编并跑"/> 那条一模一样(`现在毫秒` 那一格也是 —— 见那边的说明)。
        /// </summary>
        public static 跨平台结果C 编并跑带X(string 仓库根, string c源文件, string 程序路径, string 优化档,
            string? 状态路径 = null, long? 冻结秒 = null, string? 标准输入 = null, bool 现装 = false,
            uint? 现在毫秒 = null)
        {
            string 源 = 容器路径(仓库根, c源文件);
            string 程序 = 容器路径(仓库根, 程序路径);
            if (源.Length == 0 || 程序.Length == 0)
                return new 跨平台结果C { 说明 = "源文件或产物不在仓库这一层里(容器只挂仓库)" };

            var 脚本 = new StringBuilder();
            if (现装)
            {
                // 「现装」那一档:`apt-get` 装在这**一次容器运行**里(容器一销毁什么都没留下);
                // 不碰任何镜像标签、不 commit —— "不动镜像"这条口径照旧成立。
                脚本.Append("apt-get -qq update >/dev/null 2>&1; ");
                脚本.Append("apt-get -qq install -y --no-install-recommends xvfb >/dev/null 2>&1; ");
            }
            脚本.Append("Xvfb :99 -screen 0 320x240x24 >/dev/null 2>&1 & ");
            脚本.Append("hb_xpid=$!; sleep 2; export DISPLAY=:99; export LANG=C.UTF-8; ");
            脚本.Append("cc ").Append(优化档).Append(" -std=c11 -o '").Append(程序).Append("' '").Append(源).Append("' -lm; ");
            脚本.Append("hb_rc=$?; echo ").Append(编译标记).Append("$hb_rc >&2; ");
            脚本.Append("if [ $hb_rc -ne 0 ]; then kill $hb_xpid 2>/dev/null; exit $hb_rc; fi; ");
            脚本.Append("'").Append(程序).Append("'");
            if (状态路径 is { Length: > 0 })
                脚本.Append(" --hb-dump '").Append(容器路径(仓库根, 状态路径)).Append("'");
            if (冻结秒 is { } 秒)
                脚本.Append(" --hb-now ").Append(秒.ToString(CultureInfo.InvariantCulture));
            if (现在毫秒 is { } 毫秒)
                脚本.Append(" --hb-now-ms ").Append(毫秒.ToString(CultureInfo.InvariantCulture));
            脚本.Append("; hb_rc=$?; kill $hb_xpid 2>/dev/null; exit $hb_rc");

            var 参数 = new List<string> { "run", "--rm" };
            if (标准输入 != null) 参数.Add("-i");
            参数.AddRange(["-v", 仓库根 + ":" + 容器挂载点, "-w", 容器挂载点, X镜像, "sh", "-c", 脚本.ToString()]);

            var 跑过 = 跑("docker", 参数.ToArray(),
                标准输入 == null ? null : Encoding.UTF8.GetBytes(标准输入), 超时秒);
            if (!跑过.起得来) return new 跨平台结果C { 说明 = "起不了 docker:" + 一行化(跑过.误, 160) };
            if (跑过.超时) return new 跨平台结果C { 说明 = $"Linux 那侧 {超时秒} 秒没跑完(超时)" };

            bool 编过 = false;
            var 净误 = new List<string>();
            foreach (string 行 in 跑过.误.Replace("\r\n", "\n").Split('\n'))
            {
                if (行.StartsWith(编译标记, StringComparison.Ordinal))
                {
                    编过 = 行[编译标记.Length..].Trim() == "0";
                    continue;
                }
                净误.Add(行);
            }

            return new 跨平台结果C
            {
                编过 = 编过,
                退出码 = 跑过.退出码,
                输出 = 跑过.出字节,
                日志 = string.Join("\n", 净误).Trim(),
                说明 = 编过 ? "" : "cc 没编过(日志里是 gcc 的话)",
            };
        }

        /// <summary>带 X 的那一档用哪个镜像(`HB_跨平台X镜像` 可盖过;缺省就借 `镜像` 那一份,那时 `有X服务器` 会先拦住)</summary>
        public static string X镜像
        {
            get
            {
                string? 自己 = Environment.GetEnvironmentVariable("HB_跨平台X镜像");
                return string.IsNullOrWhiteSpace(自己) ? 镜像 : 自己.Trim();
            }
        }

        /// <summary>
        /// 这台机器上有没有"本来就带 Xvfb"的镜像可用(`X镜像` 里 `command -v Xvfb` 问一句)。
        /// **不装、不拉、不改镜像** —— 没有就如实说没有,调用方记一条"没给/没有"的说明。
        /// </summary>
        public static bool 有X服务器()
        {
            var 跑过 = 跑("docker", ["run", "--rm", "--entrypoint", "sh", X镜像, "-c", "command -v Xvfb"], null, 120);
            if (!跑过.起得来 || 跑过.退出码 != 0) { X服务器说明_存 = X服务器说明算(跑过.出文本, 跑过.误); return false; }
            X服务器说明_存 = $"镜像 {X镜像} 里有 {跑过.出文本.Trim()}(Xvfb)";
            return true;
        }

        private static string X服务器说明_存 = "";
        private static string X服务器说明算(string 出, string 误) =>
            $"镜像 {X镜像} 里没有 Xvfb(不装 / 不拉 / 不改镜像;要这一档就给 `HB_跨平台X镜像` 指一个自带 Xvfb 的)" +
            (出.Length + 误.Length > 0 ? ":" + 一行化(出 + " " + 误, 120) : "");

        /// <summary>上一次 `有X服务器()` 的结论(给人看的说明,含"没给镜像 / 镜像里没有")</summary>
        public static string X服务器说明() => X服务器说明_存.Length > 0
            ? X服务器说明_存
            : $"没给 `HB_跨平台X镜像`(缺省借 `{镜像}`,它没有 Xvfb)";

        /// <summary>仓库里的绝对路径 → 容器里的路径(不在仓库这一层里给空串)</summary>
        private static string 容器路径(string 仓库根, string 路径)
        {
            string 全 = Path.GetFullPath(路径);
            string 根 = Path.GetFullPath(仓库根).TrimEnd('\\', '/');
            if (!全.StartsWith(根 + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "";
            return 容器挂载点 + "/" + 全[(根.Length + 1)..].Replace('\\', '/');
        }

        private static string 一行化(string 文本, int 上限)
        {
            string 一行 = 文本.Replace("\r", " ").Replace("\n", " ").Trim();
            return 一行.Length <= 上限 ? 一行 : 一行[..上限] + "…";
        }

        /// <summary>起一个子进程、收 stdout **字节** + stderr 文本 + 退出码(起不来 / 超时也如实报)</summary>
        private static (bool 起得来, bool 超时, int 退出码, byte[] 出字节, string 出文本, string 误) 跑(
            string 程序, string[] 参数, byte[]? 标准输入, int 超时)
        {
            var 起 = new ProcessStartInfo(程序)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = 标准输入 != null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            foreach (string 一 in 参数) 起.ArgumentList.Add(一);

            Process? 进程;
            try { 进程 = Process.Start(起); }
            catch (Exception ex) { return (false, false, -1, [], "", ex.Message); }
            if (进程 == null) return (false, false, -1, [], "", "进程是 null");

            using (进程)
            {
                var 出流 = new MemoryStream();
                var 出任务 = 进程.StandardOutput.BaseStream.CopyToAsync(出流);
                var 误任务 = 进程.StandardError.ReadToEndAsync();
                try
                {
                    if (标准输入 != null) 进程.StandardInput.BaseStream.Write(标准输入);
                    if (起.RedirectStandardInput) 进程.StandardInput.Close();
                }
                catch { }

                if (!进程.WaitForExit(超时 * 1000))
                {
                    try { 进程.Kill(entireProcessTree: true); } catch { }
                    return (true, true, -1, [], "", "超时");
                }

                出任务.GetAwaiter().GetResult();
                byte[] 字节 = 出流.ToArray();
                return (true, false, 进程.ExitCode, 字节, Encoding.UTF8.GetString(字节), 误任务.GetAwaiter().GetResult());
            }
        }
    }
}
