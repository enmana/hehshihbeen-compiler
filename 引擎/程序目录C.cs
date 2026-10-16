using System;
using System.Collections.Generic;
using System.IO;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 程序自己的数据目录(选项.json / 主题\ / 最近项目.json 都放这儿),以及"两个构建共用一份设置"的搬迁。
    ///
    /// 为什么需要它:Release 和 Debug 是**两个不同的输出目录**(bin\Release\net10.0-windows 与
    /// bin\Debug\net10.0-windows),而设置是放在 exe 旁边的 —— 不搬的话,用户从 Debug 换到 Release
    /// 会发现"主题、最近项目、上次打开的目录全没了",那体验很糟(用户实测反馈过"少让人记仪式")。
    /// 规则很简单:**自己目录里已经有 选项.json 就不动;没有就去隔壁构建目录搬一次**(只搬一次,之后各写各的)。
    /// </summary>
    public static class 程序目录C
    {
        /// <summary> 数据目录 = exe 所在目录(和以前完全一样)</summary>
        public static string 数据目录 => AppContext.BaseDirectory;

        /// <summary> 从"隔壁那个构建目录"(Debug ↔ Release)把设置搬过来;搬不搬都不影响能用 </summary>
        /// <param name="目标目录">要搬进哪个目录(null = 程序自己的数据目录;自检会传临时目录来验)</param>
        public static void 搬一次隔壁设置(string? 目标目录 = null)
        {
            try
            {
                string 本目录 = (目标目录 ?? 数据目录).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(本目录)) return;
                if (File.Exists(Path.Combine(本目录, "选项.json"))) return;   // 自己已经有了:不动
                // 本目录形如 …\bin\<配置>\<目标框架>(例如 …\bin\Release\net10.0-windows):
                // 先退到 …\bin,再去它的各个子目录里找"另一个构建"的设置
                string? 配置层 = Path.GetDirectoryName(本目录);                             // …\bin\Release
                string? bin层 = 配置层 == null ? null : Path.GetDirectoryName(配置层);        // …\bin
                if (bin层 == null || !Directory.Exists(bin层)) return;

                foreach (string 候选 in 候选目录(bin层))
                {
                    string 候选目录 = 候选.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (string.Equals(候选目录, 本目录, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(Path.Combine(候选目录, "选项.json"))) continue;

                    搬文件(候选目录, 本目录, "选项.json");
                    搬文件(候选目录, 本目录, "最近项目.json");
                    string 源主题 = Path.Combine(候选目录, "主题");
                    if (Directory.Exists(源主题))
                    {
                        string 目标主题 = Path.Combine(本目录, "主题");
                        Directory.CreateDirectory(目标主题);
                        foreach (string 文件 in Directory.GetFiles(源主题, "*.json"))
                        {
                            string 目标 = Path.Combine(目标主题, Path.GetFileName(文件));
                            if (!File.Exists(目标)) File.Copy(文件, 目标);
                        }
                    }
                    return;      // 只认第一个有 选项.json 的隔壁目录
                }
            }
            catch
            {
                // 搬不过来就算了:新目录照样能用,只是设置从默认值开始
            }
        }

        /// <summary> bin 下面所有"可能放着设置"的目录:每个配置目录本身,以及它下面一层目标框架目录 </summary>
        private static List<string> 候选目录(string bin层)
        {
            var 表 = new List<string>();
            foreach (string 配置 in Directory.GetDirectories(bin层))
            {
                表.Add(配置);
                foreach (string 框架 in Directory.GetDirectories(配置)) 表.Add(框架);
            }
            return 表;
        }

        private static void 搬文件(string 源目录, string 目标目录, string 文件名)
        {
            string 源 = Path.Combine(源目录, 文件名);
            string 目标 = Path.Combine(目标目录, 文件名);
            if (File.Exists(源) && !File.Exists(目标)) File.Copy(源, 目标);
        }
    }
}
