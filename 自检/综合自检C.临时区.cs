// ============================================================
// 临时区自检(H44;硬规矩 15 —— 临时文件 / 缓存不许落 C 盘)
//   用户 2026-09-25 当班点名:"不要把临时文件或者缓存弄到 C 盘,我的 C 盘都满了"
//   (现场:一个 `hbcc_C后端全量` 目录 6.8 GB、`hbcc_CLI端到端` 175 MB,跑一趟自检就多一份、跑完不删)。
//   这条钉四件事:
//     ① 全仓 `.cs` 里**一处**"系统临时目录那一路"都不许再有(有就点名是哪个文件;
//        断言自己那个搜索词是拼出来的,不然会把自己数进去)。
//        **G3 域 6 起有一处例外**:语言的 `47 临时目录` 那条宿主能力问的正是"本机临时目录",
//        它必须拿操作系统的答案(`引擎\宿主能力C.cs` 的 `临时目录文本`)—— 那是**给用户程序用的功能**,
//        不是"自检 / 工具把自己的临时文件写到系统临时目录"(H44 要治的是后者);
//     ② 落点阶梯按四种输入各量一遍(环境变量 > 仓库内 `工具\临时` > `E:\cache\temp` > 程序目录),
//        **任何一种都不许落到系统临时目录**;
//     ③ `工具\临时\` 在 `.gitignore` 里(临时区就在仓库里,不挡住就天天挂 `git status`);
//     ④ 收尾自清是接上的(按源码钉自检那条 `finally`)。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        private static void 临时区自检()
        {
            string? 示例 = 找示例目录();
            string? 仓根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName ?? 示例;
            if (仓根 == null || !Directory.Exists(仓根))
            {
                跳过("临时区 全仓没有系统临时目录那一路(例外只有语言 `临时目录` 那一处)", "找不到仓库根(不在工作副本里跑)");
                跳过("临时区 落点阶梯按四种输入各量一遍(都不落系统临时目录)", "找不到仓库根");
                跳过("临时区 工具\\临时 在 .gitignore 里", "找不到仓库根");
                跳过("临时区 收尾自清接上了(自检 finally 里那一句)", "找不到仓库根");
                return;
            }

            // ---- ① 全仓 .cs 里一处都不许再有(例外:语言 `47 临时目录` 那条宿主能力,见文件头) ----
            // 搜索词**拼出来**,不然这条断言会把本文件自己数进去(自己就算一处)。
            const string 那一路 = "Path.GetTemp" + "Path";
            string 能力那处 = "引擎" + Path.DirectorySeparatorChar + "宿主能力C.cs";
            try
            {
                var 命中 = new List<string>();
                foreach (string 文件 in Directory.EnumerateFiles(仓根, "*.cs", SearchOption.AllDirectories))
                {
                    string 相对 = Path.GetRelativePath(仓根, 文件);
                    if (相对.StartsWith("工具" + Path.DirectorySeparatorChar + "临时", StringComparison.Ordinal)) continue;
                    if (相对.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                        || 相对.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                    if (相对 == 能力那处) continue;                 // G3 域 6:语言 `临时目录` 那一处(唯一的例外)
                    if (File.ReadAllText(文件, Encoding.UTF8).Contains(那一路, StringComparison.Ordinal)) 命中.Add(相对);
                }
                记录("临时区 全仓没有系统临时目录那一路(例外只有语言 `临时目录` 那一处)", 命中.Count == 0,
                    命中.Count == 0
                        ? $"临时文件与缓存一律走 `临时区C`(落点 = HB_TEMP / 仓库 工具\\临时 / E:\\cache\\temp / 程序目录);" +
                          $"唯一例外 = {能力那处} 里语言 `47 临时目录` 那条宿主能力(它问的就是「本机临时目录」)"
                        : $"{命中.Count} 个文件里还有(临时区唯一入口见 引擎\\临时区C.cs):"
                          + string.Join(" / ", 命中.Take(5)));
            }
            catch (Exception ex) { 记录异常("临时区 全仓没有系统临时目录那一路(例外只有语言 `临时目录` 那一处)", ex); }

            // ---- ② 落点阶梯:四种输入各量一遍 ----
            try
            {
                const string 假根 = @"D:\假仓库";
                const string 假程序目录 = @"D:\假程序";
                string 环境变量落点 = 临时区C.选落点(@"E:\cache\给自检的", 假根, 有E盘: true, 假程序目录);
                string 仓库落点 = 临时区C.选落点(null, 假根, 有E盘: true, 假程序目录);
                string 机器级落点 = 临时区C.选落点("   ", null, 有E盘: true, 假程序目录);
                string 程序目录落点 = 临时区C.选落点(null, null, 有E盘: false, 假程序目录);
                bool 对 = 环境变量落点 == @"E:\cache\给自检的"
                    && 仓库落点 == Path.Combine(假根, "工具", "临时")
                    && 机器级落点 == 临时区C.机器级落点
                    && 程序目录落点 == Path.Combine(假程序目录, "临时区");
                记录("临时区 落点阶梯按四种输入各量一遍(都不落系统临时目录)", 对,
                    $"环境变量→{环境变量落点};仓库在→{仓库落点};只有 E 盘→{机器级落点};什么都没有→{程序目录落点}");
            }
            catch (Exception ex) { 记录异常("临时区 落点阶梯按四种输入各量一遍(都不落系统临时目录)", ex); }

            // ---- ③ `工具\临时\` 在 .gitignore 里 ----
            try
            {
                string 忽略 = Path.Combine(仓根, ".gitignore");
                bool 有 = File.Exists(忽略)
                    && File.ReadAllLines(忽略, Encoding.UTF8)
                        .Select(x => x.Trim()).Any(x => x == "工具/临时/");
                记录("临时区 工具\\临时 在 .gitignore 里", 有,
                    有 ? "临时区就在仓库里,忽略规则挡着它(`git status` 看不到)" : "`.gitignore` 里少了 `工具/临时/` 这一行");
            }
            catch (Exception ex) { 记录异常("临时区 工具\\临时 在 .gitignore 里", ex); }

            // ---- ④ 收尾自清是接上的(按源码钉) ----
            try
            {
                string 主文件 = Path.Combine(仓根, "自检", "综合自检C.cs");
                string 正文 = File.Exists(主文件) ? File.ReadAllText(主文件, Encoding.UTF8) : "";
                bool 接上 = 正文.Contains("临时区C.清理本次遗留()", StringComparison.Ordinal);
                记录("临时区 收尾自清接上了(自检 finally 里那一句)", 接上,
                    接上 ? "自检收尾的 finally 里调 `临时区C.清理本次遗留()` —— 几 GB 的对拍产物在这一句里收掉"
                         : "自检收尾的 finally 里找不到 `临时区C.清理本次遗留()`(临时区就成了只进不出的账)");
            }
            catch (Exception ex) { 记录异常("临时区 收尾自清接上了(自检 finally 里那一句)", ex); }
        }
    }
}
