using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 第169批(C28):**用户的默认浏览器**是谁,以及"用它打开一个网址"这条路。
    /// <para>
    /// 为什么不直接把网址丢给 shell:shell 是按**文件关联**开 `.html` 的,而那条关联可能是坏的
    /// (用户这台机器实测:`AssocQueryString(".html")` 只回 `OpenWith.exe`,`file` 协议回 `0x80070483`
    /// = 没有关联 ⇒ `Process.Start` 抛异常 ⇒ 帮助菜单"点了没反应")。
    /// 这里改问注册表里"默认**浏览器**(`http` / `https` 的 UserChoice)是谁",再拿它的 `shell\open\command`
    /// 现场套出命令行(`引擎\浏览器命令C.套命令行`,纯函数)—— 这条路不经过 `.html` 的文件关联,
    /// 坏掉的关联也就不再挡路。
    /// </para>
    /// <para>
    /// **这一层是 Windows 专属**:引擎库是 `net10.0`(手机端也编那一份),注册表代码只能待在界面层;
    /// 于是分工是"界面问注册表 + 引擎拼命令行"(引擎那份能进自检,这份不能)。
    /// 问不到 / 起不来一律给 `false`,由调用方决定是退回系统 shell 还是弹一句人话 —— **绝不留静默失败**。
    /// </para>
    /// </summary>
    internal static class 默认浏览器C
    {
        /// <summary> 两份 UserChoice:`http` 是主、`https` 兜底(两个都缺说明这台机器没设过默认浏览器) </summary>
        private static readonly string[] 协议表 = ["http", "https"];

        /// <summary> 默认浏览器的 shell open 命令模板(例:`"…\msedge.exe" --single-argument %1`);问不到给 null </summary>
        public static string? 取打开命令()
        {
            foreach (string 协议 in 协议表)
            {
                try
                {
                    using RegistryKey? 用户选择 = Registry.CurrentUser.OpenSubKey(
                        $@"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\{协议}\UserChoice");
                    if (用户选择?.GetValue("ProgId") is not string 程序标识 || 程序标识.Length == 0) continue;

                    using RegistryKey? 命令键 = Registry.ClassesRoot.OpenSubKey($@"{程序标识}\shell\open\command");
                    if (命令键?.GetValue(null) is string 命令 && 命令.Trim().Length > 0) return 命令;
                }
                catch
                {
                    // 注册表读不动(权限 / 键被删)就换下一个协议 —— 这里不是出错点,调用方还有 shell 兜底
                }
            }
            return null;
        }

        /// <summary> 按默认浏览器打开一个网址;**真起了进程给 true**(调用方据此决定要不要再兜底 / 要不要弹提示) </summary>
        public static bool 打开(string 网址)
        {
            if (取打开命令() is not { } 命令模板) return false;

            var (可执行文件, 参数) = 浏览器命令C.套命令行(命令模板, 网址);
            可执行文件 = Environment.ExpandEnvironmentVariables(可执行文件);
            if (可执行文件.Length == 0 || !File.Exists(可执行文件)) return false;

            try
            {
                using Process? 进程 = Process.Start(new ProcessStartInfo(可执行文件, 参数) { UseShellExecute = false });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
