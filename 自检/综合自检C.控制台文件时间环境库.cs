// ============================================================
// 控制台 / 文件 / 时间 / 环境 四个库的自检(G4 批 14,第359批)
//   —— `Sample\库\控制台.hb` · `文件.hb` · `时间.hb` · `环境.hb`
//
//   判据照 `基础库施工计划.md` §2 批 14 那一行:
//   **"读写 / 按行迭代 / 时间拆分拼回 / 原子写(写临时 + 改名)"**。
//   这一组八块(行为断言为主 + 一条接线哨兵;硬规矩 5 / T22):
//     ① **文件那一族在真盘上真跑**:建目录 / 覆盖写 / 存在 / 大小 / 整份读 / 行数 /
//        逐行迭代(跨块拼行 + 行尾 `\r` 去掉 + 末行没有换行也算一行)/ 句柄作废 /
//        追加 / 读一块 / **安全写(写临时 + 改名)**—— 一整段程序的输出与 C# 侧按同一套口径算的期望逐条比;
//     ② **写数字**:`写整数` / `写整数补零` 与 C# 的 `PadLeft` 逐条比(宽度 ≤ 0 不补那一档也在);
//     ③ **写双精度**:与 C# 参考实现逐条比 —— 参考实现照 `控制台.hb` 头注释写的那套
//        (拿最短十进制表示在小数点后截 + 四舍五入,**不按二进制精确值**);
//     ④ **表格与进度条**:列宽 / 中文按两格算 / 超宽截断 / 居中与左对齐 / `+---+` 线,
//        与 C# 参考实现逐条比;
//     ⑤ **时间**:拆分 / 拼回 / 周几 / 闰年 / 某月天数 / 当日零点 / 加秒,
//        与 .NET 的 `DateTimeOffset`(固定偏移)逐条比;`按模板写` / `按模板读` 互逆且与参考实现一致;
//     ⑥ **环境**:有 / 取 / 给默认 / 展开 `%名字%` / 当整数读(本进程里现设一个变量再问);
//     ⑦ **端到端**:标准工程 **`示例46_文件与终端`** 真编真跑,与它那行 `// 预期输出:` 逐行对;
//     ⑧ **接线哨兵**:四个库都在盘上 + 头注释五段齐 + 模块名 + 公开函数数(13 / 13 / 18 / 6)。
//
//   ⚠ 这一组**只在"给输出加非空前缀"时用一次 `拼接到`**(与 `中文库` 那一组同一套手续):
//     `跑()` 收尾那一下 `Trim()` 会把空结果吃掉,加个标记才看得见"这一条跑到没有"。
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
        private static void 控制台文件时间环境库自检()
        {
            string? 示例 = 找示例目录();
            string? 仓库根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
            string 库目录 = 仓库根 == null ? "" : Path.Combine(仓库根, "Sample", "库");
            string 控 = Path.Combine(库目录, "控制台.hb").Replace('\\', '/');
            string 文 = Path.Combine(库目录, "文件.hb").Replace('\\', '/');
            string 时 = Path.Combine(库目录, "时间.hb").Replace('\\', '/');
            string 环 = Path.Combine(库目录, "环境.hb").Replace('\\', '/');
            if (!File.Exists(控) || !File.Exists(文) || !File.Exists(时) || !File.Exists(环))
            {
                跳过("控制台文件时间环境库 定位库文件", $"找不到 {控} 那四个(不在工作副本里跑就跳过)");
                return;
            }

            string 头 = $"包含 \"{控}\"\n包含 \"{文}\"\n包含 \"{时}\"\n包含 \"{环}\"\n"
                + "方法 带标(字符串 标, 字符串 值) 返回 字符串\n"
                + "{\n"
                + "    字符串 结果 = \"\";\n"
                + "    拼接到(结果, 标, 值);\n"
                + "    返回 结果;\n"
                + "}\n"
                + "方法 带标数(字符串 标, 整数 值) 返回 字符串\n"
                + "{\n"
                + "    字符串 结果 = \"\";\n"
                + "    拼接到(结果, 标);\n"
                + "    拼接到(结果, 转字符串(值));\n"
                + "    返回 结果;\n"
                + "}\n"
                + "方法 带标逻辑(字符串 标, 逻辑 值) 返回 字符串\n"
                + "{\n"
                + "    字符串 结果 = \"\";\n"
                + "    拼接到(结果, 标);\n"
                + "    如果 (值)\n"
                + "    {\n"
                + "        拼接到(结果, \"1\");\n"
                + "    }\n"
                + "    否则\n"
                + "    {\n"
                + "        拼接到(结果, \"0\");\n"
                + "    }\n"
                + "    返回 结果;\n"
                + "}\n";


            static string 跑(string 源)
            {
                var 编译 = new 汇编器C().汇编多文件带调试(
                    new List<(string, string)> { ("主程序.hb", 源) }, true,
                    中间层C.默认堆大小, null, 中间层C.默认池大小,
                    虚拟机C.默认内存大小, 32 * 1024);
                var vm = new 虚拟机C { 宿主调用 = 宿主能力C.处理 };
                vm.设内存大小(虚拟机C.默认内存大小);
                var 缓冲 = new StringBuilder();
                var 解码 = new 文字输出C(文本 => 缓冲.Append(文本));
                vm.输出目标 = 解码.写;
                vm.运行(编译);
                解码.收尾();
                string 报错 = 编译.查运行期报错(vm.内存) is { } 说明 ? $"⚠{说明}" : "";
                return 缓冲.ToString().Trim().Replace("\n", "|") + 报错;
            }

            static string 对拍说明(string 期望, string 实得)
            {
                if (期望 == 实得) return $"一字不差({期望.Split('|').Length} 条)";
                string[] 期望段 = 期望.Split('|'), 实得段 = 实得.Split('|');
                int 少 = Math.Min(期望段.Length, 实得段.Length);
                for (int i = 0; i < 少; i++)
                    if (期望段[i] != 实得段[i])
                        return $"第 {i + 1} 个输出对不上:期望 [{期望段[i]}] / 实得 [{实得段[i]}](期望 {期望段.Length} 条 / 实得 {实得段.Length} 条)";
                return $"条数对不上:期望 {期望段.Length} 条 / 实得 {实得段.Length} 条";
            }

            // ============================================================
            // ① 文件那一族:真盘上真建真写真读
            // ============================================================
            string 临时根 = 仓库根 == null
                ? ""
                : Path.Combine(仓库根, "工具", "临时", $"终端文件库自检_{Guid.NewGuid().ToString("N")[..8]}").Replace('\\', '/');
            try
            {
                string 名单 = 临时根 + "/名单.txt";
                string 配置 = 临时根 + "/配置.txt";
                string 源 = 头
                    + $"字符串 根 = \"{临时根}\";\n"
                    + $"字符串 名单 = \"{名单}\";\n"
                    + $"字符串 配置 = \"{配置}\";\n"
                    + "输出行(带标逻辑(\"A\", 建目录(根)));\n"
                    + "输出行(带标逻辑(\"B\", 文件.存在(名单)));\n"
                    + "输出行(带标逻辑(\"C\", 文件.写全部(名单, \"张三\\n李四\\r\\n王五\\n末行\")));\n"
                    + "输出行(带标逻辑(\"D\", 文件.存在(名单)));\n"
                    + "输出行(带标数(\"E\", 文件.大小(名单)));\n"
                    + "输出行(带标(\"F\", 文件.读全部(名单)));\n"
                    + "输出行(带标数(\"G\", 文件.行数(名单)));\n"
                    + "整数 句 = 文件.开读(名单);\n"
                    + "整数 条 = 0;\n"
                    + "当 (文件.下一行(句))\n"
                    + "{\n"
                    + "    输出行(带标(\"H\", 文件.当前行(句)));\n"
                    + "    条 = 条 + 1;\n"
                    + "}\n"
                    + "输出行(带标数(\"I\", 条));\n"
                    + "输出行(带标逻辑(\"J\", 文件.关读(句)));\n"
                    + "输出行(带标逻辑(\"K\", 文件.下一行(句)));\n"
                    + "输出行(带标(\"L\", 文件.当前行(句)));\n"
                    + "输出行(带标逻辑(\"M\", 文件.追加(名单, \"\\n赵六\")));\n"
                    + "输出行(带标数(\"N\", 文件.行数(名单)));\n"
                    + "字符串 块 = \"\";\n"
                    + "输出行(带标逻辑(\"O\", 文件.读块(名单, 0, 块)));\n"
                    + "输出行(带标(\"P\", 块));\n"
                    + "输出行(带标逻辑(\"Q\", 文件.安全写全部(配置, \"层数=3\")));\n"
                    + "输出行(带标(\"R\", 文件.读全部(配置)));\n"
                    + "输出行(带标逻辑(\"S\", 文件.安全写全部(配置, \"层数=9\")));\n"
                    + "输出行(带标(\"T\", 文件.读全部(配置)));\n"
                    + "字符串 没有 = \"\";\n"
                    + "拼接到(没有, 根);\n"
                    + "拼接到(没有, \"/没有这个.txt\");\n"
                    + "输出行(带标数(\"U\", 文件.大小(没有)));\n"
                    + "输出行(带标逻辑(\"V\", 文件.存在(根)));\n"
                    + "输出行(带标数(\"W\", 文件.行数(没有)));\n"
                    + "输出行(带标数(\"X\", 文件.开读(没有)));\n"
                    + "字符串 备 = 文件.临时文件(\".tmp\");\n"
                    + "输出行(带标逻辑(\"Y\", 备.长度() > 0));\n"
                    + "停止\n";
                string 正文 = "张三\n李四\r\n王五\n末行";
                var 期望段 = new List<string>
                {
                    "A1", "B0", "C1", "D1", "E" + Encoding.UTF8.GetByteCount(正文),
                    "F" + 正文.Replace("\n", "|").Replace("\r", ""),
                    "G4",
                    "H张三", "H李四", "H王五", "H末行",
                    "I4", "J1", "K0", "L", "M1", "N5",
                    "O1", "P" + (正文 + "\n赵六").Replace("\n", "|").Replace("\r", ""),
                    "Q1", "R层数=3", "S1", "T层数=9", "U-1", "V0", "W-1", "X-1", "Y1",
                };
                string 期望 = string.Join("|", 期望段);
                string 实得 = 跑(源);
                记录("控制台文件时间环境库 文件那一族:建目录 / 存在 / 写全部 / 大小 / 读全部 / 行数 / " +
                    "逐行迭代(跨块拼行 + 行尾 \\r 去掉 + 末行无换行也算一行)/ 关读与句柄作废 / 追加 / 读一块 / " +
                    "安全写(写临时 + 改名)在真盘上真跑,逐条对 C# 侧按同一套口径算的期望",
                    期望 == 实得, 对拍说明(期望, 实得));
            }
            catch (Exception ex) { 记录异常("控制台文件时间环境库 文件那一族", ex); }
            finally
            {
                try { if (临时根.Length > 0 && Directory.Exists(临时根)) Directory.Delete(临时根, true); }
                catch { /* 收尾删不掉就留给下一次扫残留 */ }
            }

            // ============================================================
            // ② 写数字:写整数 / 写整数补零
            // ============================================================
            try
            {
                var 值们 = new[] { 0, 7, -7, 42, -42, 2147483647, -2147483648 };
                var 宽们 = new[] { 0, 1, 3, 6, -1 };
                var 语句 = new StringBuilder(头);
                var 期望段 = new List<string>();
                foreach (int v in 值们)
                    foreach (int w in 宽们)
                    {
                        语句.Append($"输出行(带标(\"I{w}_{v}\", 控制台.拼整数({v}, {w})));\n");
                        期望段.Add($"I{w}_{v}" + (w > 0 && v.ToString().Length < w
                            ? v.ToString().PadLeft(w)
                            : v.ToString()));
                        语句.Append($"输出行(带标(\"Z{w}_{v}\", 控制台.拼整数补零({v}, {w})));\n");
                        string 数文 = v < 0 ? "-" + (-(long)v).ToString() : v.ToString();
                        期望段.Add($"Z{w}_{v}" + (w > 0 && 数文.Length < w
                            ? 数文.PadLeft(w, '0')
                            : 数文));
                    }
                语句.Append("停止\n");
                string 期望 = string.Join("|", 期望段), 实得 = 跑(语句.ToString());
                记录("控制台文件时间环境库 写数字:写整数(空格右对齐)/ 写整数补零 与 C# 的 PadLeft 逐条比(含宽度 ≤ 0)",
                    期望 == 实得, 对拍说明(期望, 实得));
            }
            catch (Exception ex) { 记录异常("控制台文件时间环境库 写数字", ex); }

            // ============================================================
            // ③ 写双精度:与 C# 参考实现逐条比
            // ============================================================
            try
            {
                var 值们 = new[]
                {
                    "0", "3.14159", "0.145", "2", "-1.005", "9.999", "0.0001", "-0.0001",
                    "123456.789", "0.5", "1.5", "2.5", "-2.5", "99.999", "100.001", "0.05",
                };
                var 小数位们 = new[] { 0, 1, 2, 3 };
                var 语句 = new StringBuilder(头);
                var 期望段 = new List<string>();
                int k = 0;
                foreach (string 数值文 in 值们)
                    foreach (int d in 小数位们)
                    {
                        语句.Append($"双精度 值{k} = 字符串转双精度(\"{数值文}\");\n");
                        语句.Append($"输出行(带标(\"D{d}_{k}\", 控制台.拼双精度(值{k}, {d})));\n");
                        期望段.Add($"D{d}_{k}" + 参考定点(数值文, d));
                        k++;
                    }
                语句.Append("停止\n");
                string 期望 = string.Join("|", 期望段), 实得 = 跑(语句.ToString());
                记录($"控制台文件时间环境库 写双精度:{值们.Length} 个值 × {小数位们.Length} 档小数位与 C# 参考实现逐条比" +
                    "(参考实现照头注释那套:最短十进制表示在小数点后截 + 四舍五入)",
                    期望 == 实得, 对拍说明(期望, 实得));
            }
            catch (Exception ex) { 记录异常("控制台文件时间环境库 写双精度", ex); }

            // ============================================================
            // ④ 表格与进度条:与 C# 参考实现逐条比
            // ============================================================
            try
            {
                var 行们 = new[] { "张三,28,杭州", "李四,7,乌鲁木齐", "王五,35,上海", "亚历山大,7,北京", "a,bb,ccc" };
                var 宽表们 = new[] { new[] { 8, 6, 12 }, new[] { 4, 3, 5 }, new[] { 10, 8, 2 } };
                var 语句 = new StringBuilder(头);
                var 期望段 = new List<string>();
                int k = 0;
                foreach (var 宽 in 宽表们)
                {
                    语句.Append($"整数 宽{k}[3] = [{宽[0]}, {宽[1]}, {宽[2]}];\n");
                    语句.Append($"输出行(带标(\"L{k}A\", 控制台.拼表格线(宽{k}, 3)));\n");
                    期望段.Add($"L{k}A" + 参考表线(宽));
                    语句.Append($"输出行(带标(\"L{k}B\", 控制台.拼表格头(\"姓名,年龄,城市\", \",\", 宽{k}, 3)));\n");
                    期望段.Add($"L{k}B" + 参考表格(宽, "姓名,年龄,城市", 居中: true));
                    foreach (string 行 in 行们)
                    {
                        语句.Append($"输出行(带标(\"L{k}C\", 控制台.拼表格行(\"{行}\", \",\", 宽{k}, 3)));\n");
                        期望段.Add($"L{k}C" + 参考表格(宽, 行, 居中: false));
                    }
                    k++;
                }
                foreach (var (当, 总, 格) in new[] { (3, 4, 8), (0, 0, 5), (37, 100, 20), (5, 4, 6), (-1, 10, 4) })
                {
                    语句.Append($"控制台.进度条({当}, {总}, {格});\n");
                    期望段.Add(参考进度条(当, 总, 格));
                }
                语句.Append("停止\n");
                string 期望 = string.Join("|", 期望段), 实得 = 跑(语句.ToString());
                记录("控制台文件时间环境库 表格与进度条:列宽 / 中文按两格算 / 超宽截断 / 表头居中与表身左对齐 / " +
                    "`+---+` 线与进度条,与 C# 参考实现逐条比",
                    期望 == 实得, 对拍说明(期望, 实得));
            }
            catch (Exception ex) { 记录异常("控制台文件时间环境库 表格与进度条", ex); }

            // ============================================================
            // ⑤ 时间:与 .NET 的 DateTimeOffset(固定偏移)逐条比
            // ============================================================
            try
            {
                var 偏移们 = new[] { 0, 8 * 3600, -5 * 3600 };
                var 戳们 = new[]
                {
                    0, 1, 86399, 86400, 1767225600, 1767225600 + 43200, 951782400,
                    1583020800, 2147483647, 2000000000, 1234567890,
                };
                var 模板们 = new[] { "YYYY-MM-DD hh:mm:ss", "YYYY年MM月DD日 WW hh:mm", "YY/M/D W", "ss:mm:hh DD-MM-YYYY" };
                var 语句 = new StringBuilder(头);
                var 期望段 = new List<string>();
                int k = 0;
                foreach (int 偏 in 偏移们)
                {
                    语句.Append($"时间.设时区偏移({偏});\n");
                    foreach (int 戳 in 戳们)
                    {
                        string 名 = $"T{k}";
                        语句.Append($"整数 年{k} = 0;\n整数 月{k} = 0;\n整数 日{k} = 0;\n");
                        语句.Append($"整数 时{k} = 0;\n整数 分{k} = 0;\n整数 秒{k} = 0;\n");
                        语句.Append($"时间.拆分({戳}, 年{k}, 月{k}, 日{k}, 时{k}, 分{k}, 秒{k});\n");
                        语句.Append($"输出行(带标(\"{名}S\", 格式化(\"{{0}}/{{1}}/{{2}} {{3}}:{{4}}:{{5}}\", 年{k}, 月{k}, 日{k}, 时{k}, 分{k}, 秒{k})));\n");
                        // ⚠ 时间戳是 32 位:`戳 + 时区偏移` 会回绕(`2038-01-19` 那个极限值 + 正偏移就是这一档),
                        //   参考实现照同一条回绕算,免得把"库照 32 位走"误判成错
                        //   `拆分` 读的是 `时间戳 + 时区偏移` 那一串年月日时分秒(把它当"本地墙上时间"看),
                        //   所以参考实现也照"把 偏戳 当 UTC 读"来,别再叠一次偏移
                        int 偏戳 = unchecked((int)(戳 + (long)偏));
                        DateTime 本地 = DateTimeOffset.FromUnixTimeSeconds(偏戳).UtcDateTime;
                        期望段.Add($"{名}S{本地.Year}/{本地.Month}/{本地.Day} {本地.Hour}:{本地.Minute}:{本地.Second}");
                        语句.Append($"输出行(带标数(\"{名}W\", 时间.周几({戳})));\n");
                        期望段.Add($"{名}W" + (int)本地.DayOfWeek);
                        语句.Append($"输出行(带标数(\"{名}J\", 时间.拼回(年{k}, 月{k}, 日{k}, 时{k}, 分{k}, 秒{k})));\n");
                        期望段.Add($"{名}J{戳}");
                        语句.Append($"输出行(带标数(\"{名}D\", 时间.当日零点({戳})));\n");
                        期望段.Add($"{名}D" + unchecked((int)(new DateTimeOffset(本地.Year, 本地.Month, 本地.Day, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() - 偏)));
                        语句.Append($"输出行(带标数(\"{名}A\", 时间.加天({戳}, 2)));\n");
                        期望段.Add($"{名}A" + unchecked(戳 + 2 * 86400));
                        语句.Append($"输出行(带标数(\"{名}F\", 时间.是闰年(年{k})));\n");
                        期望段.Add($"{名}F" + (DateTime.IsLeapYear(本地.Year) ? 1 : 0));
                        语句.Append($"输出行(带标数(\"{名}M\", 时间.某月天数(年{k}, 月{k})));\n");
                        期望段.Add($"{名}M" + DateTime.DaysInMonth(本地.Year, 本地.Month));
                        foreach (string 模 in 模板们)
                        {
                            语句.Append($"输出行(带标(\"{名}P\", 时间.按模板写({戳}, \"{模}\")));\n");
                            期望段.Add($"{名}P" + 参考按模板写(本地, 模));
                        }
                        k++;
                    }
                }
                语句.Append("停止\n");
                string 期望 = string.Join("|", 期望段), 实得 = 跑(语句.ToString());
                记录($"控制台文件时间环境库 时间:{偏移们.Length} 档时区偏移 × {戳们.Length} 个时间戳的 " +
                    "拆分 / 周几 / 拼回 / 当日零点 / 加天 / 是闰年 / 某月天数 / 按模板写,与 .NET 的 " +
                    "`DateTimeOffset`(固定偏移)逐条比",
                    期望 == 实得, 对拍说明(期望, 实得));
            }
            catch (Exception ex) { 记录异常("控制台文件时间环境库 时间", ex); }

            // ============================================================
            // ⑤之二 时间的解析:与参考实现比,且与格式化互逆
            // ============================================================
            try
            {
                var 文们 = new[]
                {
                    "2026-01-01 08:00:00", "2026-01-01", "2026/1/1 0:0:0", "1970-01-01 00:00:00",
                    "2038-01-19 03:14:07", "2024-02-29 12:34:56", "2026-12-31 23:59:59",
                    "2026-13-01", "2025-02-29", "2026-1-1 24:00:00", "不是时间", "2026-01", "",
                };
                var 语句 = new StringBuilder(头);
                var 期望段 = new List<string>();
                int k = 0;
                foreach (string 时间文 in 文们)
                {
                    语句.Append($"整数 戳{k} = 123;\n");
                    语句.Append($"输出行(带标数(\"R{k}\", 时间.按模板读(\"{时间文}\", 戳{k})));\n");
                    语句.Append($"输出行(带标数(\"V{k}\", 戳{k}));\n");
                    bool 好 = 参考按模板读(时间文, out long 秒);
                    期望段.Add($"R{k}" + (好 ? 1 : 0));
                    期望段.Add($"V{k}" + (好 ? 秒 : 0));
                    k++;
                }
                语句.Append("停止\n");
                string 期望 = string.Join("|", 期望段), 实得 = 跑(语句.ToString());
                记录($"控制台文件时间环境库 时间解析:{文们.Length} 条文本(含月日越界 / 只有年月 / 空串)" +
                    "与参考实现逐条比「认不认得出 + 那个时间戳」(认不出一律清零)",
                    期望 == 实得, 对拍说明(期望, 实得));
            }
            catch (Exception ex) { 记录异常("控制台文件时间环境库 时间解析", ex); }

            // ============================================================
            // ⑥ 环境:本进程里现设一个变量再问
            // ============================================================
            try
            {
                string 名字 = "HB_自检_批14";
                Environment.SetEnvironmentVariable(名字, "四十二");
                Environment.SetEnvironmentVariable(名字 + "_数", "-17");
                Environment.SetEnvironmentVariable(名字 + "_长", "abc");
                var 源 = 头
                    + $"输出行(带标数(\"A\", 环境.有环境变量(\"{名字}\")));\n"
                    + $"输出行(带标(\"B\", 环境.取环境变量(\"{名字}\")));\n"
                    + $"输出行(带标数(\"C\", 环境.有环境变量(\"HB_绝对没有这个\")));\n"
                    + "输出行(带标(\"D\", 环境.取环境变量(\"HB_绝对没有这个\")));\n"
                    + "输出行(带标(\"E\", 环境.取环境变量或(\"HB_绝对没有这个\", \"缺省\")));\n"
                    + "字符串 待展 = \"左=%HB_绝对没有这个% 右=%\";\n"
                    + $"拼接到(待展, \"{名字}\");\n"
                    + "拼接到(待展, \"%\");\n"
                    + "输出行(带标(\"F\", 环境.展开(待展)));\n"
                    + "整数 数 = 0;\n"
                    + $"输出行(带标数(\"G\", 环境.取环境整数(\"{名字}_数\", 数)));\n"
                    + "输出行(带标数(\"H\", 数));\n"
                    + $"输出行(带标数(\"I\", 环境.取环境整数(\"{名字}_长\", 数)));\n"
                    + "输出行(带标数(\"J\", 数));\n"
                    + "字符串 临 = 环境.系统临时目录();\n"
                    + "输出行(带标数(\"K\", 临.长度() > 0));\n"
                    + "停止\n";
                string 期望 = string.Join("|", new[]
                {
                    "A1", "B四十二", "C0", "D", "E缺省",
                    "F左=%HB_绝对没有这个% 右=四十二|G1", "H-17", "I0", "J0", "K1",
                });
                string 实得 = 跑(源);
                记录("控制台文件时间环境库 环境:有 / 取 / 取默认 / 展开 %名字% / 当整数读(读不出给假并清零)/ 本机临时目录",
                    期望 == 实得, 对拍说明(期望, 实得));
                Environment.SetEnvironmentVariable(名字, null);
                Environment.SetEnvironmentVariable(名字 + "_数", null);
                Environment.SetEnvironmentVariable(名字 + "_长", null);
            }
            catch (Exception ex) { 记录异常("控制台文件时间环境库 环境", ex); }

            // ============================================================
            // ⑦ 端到端:标准工程 示例46_文件与终端
            // ============================================================
            try
            {
                string? 工程目录 = 示例 == null ? null : Path.Combine(示例, "中间层", "示例46_文件与终端");
                string? 源码 = 工程目录 == null ? null : Path.Combine(工程目录, "主程序.hb");
                if (源码 == null || !File.Exists(源码))
                {
                    跳过("控制台文件时间环境库 标准工程 示例46 逐行对预期输出", $"找不到 {源码}(不在工作副本里跑就跳过)");
                }
                else
                {
                    var 行表 = File.ReadAllLines(源码);
                    var 期望行 = 解析预期输出(行表);
                    var 装载 = new List<(string, string)> { (源码, string.Join("\n", 行表)) };
                    var 编译 = new 汇编器C().汇编多文件带调试(
                        装载, true, 中间层C.默认堆大小, null, 中间层C.默认池大小,
                        虚拟机C.默认内存大小, 32 * 1024);
                    var vm = new 虚拟机C { 宿主调用 = 宿主能力C.处理 };
                    vm.设内存大小(虚拟机C.默认内存大小);
                    var 缓冲 = new StringBuilder();
                    var 解码 = new 文字输出C(文本 => 缓冲.Append(文本));
                    vm.输出目标 = 解码.写;
                    vm.运行(编译);
                    解码.收尾();
                    var 实得行 = 缓冲.ToString().Trim().Split('\n').Select(x => x.TrimEnd('\r')).ToList();
                    // 示例把落点建在语言那条 `临时目录` 里(不动工作目录),末尾自己删干净 —— 这一侧不去碰它
                    bool 好 = 期望行.Count > 0 && 实得行.Count == 期望行.Count
                        && 实得行.Zip(期望行, (a, b) => a == b).All(x => x);
                    int 错行 = 0;
                    if (!好 && 期望行.Count == 实得行.Count)
                    {
                        for (int j = 0; j < 期望行.Count; j++)
                            if (期望行[j] != 实得行[j]) { 错行 = j + 1; break; }
                    }
                    记录("控制台文件时间环境库 标准工程 示例46 逐条对预期输出" +
                        "(写与查 / 逐行读 + 表格 / 追加 / 安全写 / 读一块 / 时间 / 环境 / 进度条)",
                        好, 好 ? $"逐行一致({期望行.Count} 行)" : $"期望 {期望行.Count} 行 / 实得 {实得行.Count} 行;" +
                            (错行 > 0
                                ? $"第 {错行} 行 期望[{期望行[错行 - 1]}] 实得[{实得行[错行 - 1]}]"
                                : "条数先对不上"));
                }
            }
            catch (Exception ex) { 记录异常("控制台文件时间环境库 标准工程 示例46", ex); }

            // ============================================================
            // ⑧ 接线哨兵
            // ============================================================
            try
            {
                var 表 = new (string 路径, string 模块, int 公开数)[]
                {
                    (控, "模块 控制台", 16),
                    (文, "模块 文件", 13),
                    (时, "模块 时间", 18),
                    (环, "模块 环境", 6),
                };
                var 详情 = new List<string>();
                bool 全好 = true;
                foreach (var (路径, 模块, 公开数) in 表)
                {
                    string 文本 = File.ReadAllText(路径);
                    bool 头齐 = 文本.Contains("口径(2026-", StringComparison.Ordinal)
                        && 文本.Contains("提供的", StringComparison.Ordinal)
                        && 文本.Contains("英文对照", StringComparison.Ordinal)
                        && 文本.Contains("复杂度", StringComparison.Ordinal)
                        && 文本.Contains("边界(如实登记", StringComparison.Ordinal);
                    int 法 = 0;
                    foreach (string 行 in 文本.Split('\n'))
                        if (行.StartsWith("方法 ", StringComparison.Ordinal)) 法++;
                    bool 好 = 头齐 && 文本.Contains(模块, StringComparison.Ordinal) && 法 == 公开数;
                    全好 &= 好;
                    详情.Add($"{Path.GetFileName(路径)} 头齐 {头齐} 公开 {法}/{公开数}");
                }
                记录("控制台文件时间环境库 接线哨兵:四个库都在盘上、头注释五段齐、模块名在、公开函数数对得上",
                    全好, string.Join(" ; ", 详情));
            }
            catch (Exception ex) { 记录异常("控制台文件时间环境库 接线哨兵", ex); }
        }

        // ============================================================
        // 参考实现(照那四个库的头注释写的那套口径,一份独立的 C# 写法)
        // ============================================================

        /// <summary>一个码点占几格(`宽度.hb` 那十一档,与 `取文字宽度` 逐条对齐)。</summary>
        private static int 参考字宽(int 码)
        {
            if (码 >= 0x1100 && 码 <= 0x115F) return 2;
            if (码 >= 0x2E80 && 码 <= 0x303E) return 2;
            if (码 >= 0x3041 && 码 <= 0x33FF) return 2;
            if (码 >= 0x3400 && 码 <= 0x4DBF) return 2;
            if (码 >= 0x4E00 && 码 <= 0x9FFF) return 2;
            if (码 >= 0xA000 && 码 <= 0xA4CF) return 2;
            if (码 >= 0xAC00 && 码 <= 0xD7A3) return 2;
            if (码 >= 0xF900 && 码 <= 0xFAFF) return 2;
            if (码 >= 0xFE30 && 码 <= 0xFE4F) return 2;
            if (码 >= 0xFF00 && 码 <= 0xFF60) return 2;
            if (码 >= 0xFFE0 && 码 <= 0xFFE6) return 2;
            return 1;
        }

        /// <summary>整串占几格。</summary>
        private static int 参考格数(string 文本)
        {
            int 合 = 0;
            foreach (char c in 文本) 合 += 参考字宽(c);
            return 合;
        }

        /// <summary>固定小数位(最短十进制表示在小数点后截 + 四舍五入)。</summary>
        private static string 参考定点(string 原, int 小数位)
        {
            if (小数位 < 0) 小数位 = 0;
            if (原.Length == 0) return "";
            bool 负 = 原.StartsWith('-');
            string 数 = 负 ? 原[1..] : 原;
            int 点 = 数.IndexOf('.');
            string 整 = 点 >= 0 ? 数[..点] : 数;
            string 小 = 点 >= 0 ? 数[(点 + 1)..] : "";
            if (整.Length == 0) 整 = "0";
            bool 进位 = 小.Length > 小数位 && 小[小数位] >= '5';
            if (小.Length > 小数位) 小 = 小[..小数位];
            while (小.Length < 小数位) 小 += "0";
            if (进位)
            {
                var 尾 = new StringBuilder(小);
                bool 带 = true;
                for (int i = 尾.Length - 1; i >= 0 && 带; i--)
                {
                    if (尾[i] == '9') 尾[i] = '0';
                    else { 尾[i] = (char)(尾[i] + 1); 带 = false; }
                }
                小 = 尾.ToString();
                if (带)
                {
                    var 头 = new StringBuilder(整);
                    bool 带2 = true;
                    for (int i = 头.Length - 1; i >= 0 && 带2; i--)
                    {
                        if (头[i] == '9') 头[i] = '0';
                        else { 头[i] = (char)(头[i] + 1); 带2 = false; }
                    }
                    整 = 带2 ? "1" + 头 : 头.ToString();
                }
            }
            string 果 = 小数位 > 0 ? 整 + "." + 小 : 整;
            bool 零 = 整 == "0" && 小.All(c => c == '0');
            return 负 && !零 ? "-" + 果 : 果;
        }

        /// <summary>`+------+----+` 那种线。</summary>
        private static string 参考表线(int[] 宽)
        {
            var 果 = new StringBuilder("+");
            foreach (int 格 in 宽)
            {
                int g = Math.Max(格, 0);
                果.Append(new string('-', g + 2)).Append('+');
            }
            return 果.ToString();
        }

        /// <summary>一格:超宽就先按 `宽度.hb.截断` 截(末尾 `…`),再左对齐 / 居中补到 `格` 格。</summary>
        private static string 参考格(string 文本, int 格, bool 居中)
        {
            if (格 < 0) 格 = 0;
            string 本 = 参考截断(文本, 格, "…");
            int 差 = 格 - 参考格数(本);
            if (差 <= 0) return 本;
            if (!居中) return 本 + new string(' ', 差);
            int 左 = 差 / 2;
            return new string(' ', 左) + 本 + new string(' ', 差 - 左);
        }

        /// <summary>`宽度.hb.截断`:本来就够给原样;连省略都放不下只给省略;否则逐字收到放不下为止。</summary>
        private static string 参考截断(string 文本, int 格数, string 省略)
        {
            if (参考格数(文本) <= 格数) return 文本;
            int 省宽 = 参考格数(省略);
            if (格数 <= 省宽) return 省略;
            int 余地 = 格数 - 省宽;
            var 果 = new StringBuilder();
            int 宽 = 0;
            foreach (char c in 文本)
            {
                int 本字宽 = 参考字宽(c);
                if (宽 + 本字宽 > 余地) break;
                果.Append(c);
                宽 += 本字宽;
            }
            return 果.Append(省略).ToString();
        }

        /// <summary>一整行表格(与 `控制台.拼表格行` 同一套:段不够空着、多了丢掉)。</summary>
        private static string 参考表格(int[] 宽, string 行, bool 居中)
        {
            string[] 段 = 行.Split(',');
            var 果 = new StringBuilder("|");
            for (int i = 0; i < 宽.Length; i++)
            {
                string 本 = i < 段.Length ? 段[i] : "";
                果.Append(' ').Append(参考格(本, 宽[i], 居中)).Append(" |");
            }
            return 果.ToString();
        }

        /// <summary>`[####....] 37%`。</summary>
        private static string 参考进度条(int 当, int 总, int 格)
        {
            int 百分 = 0, 满 = 0;
            if (总 > 0)
            {
                百分 = 当 * 100 / 总;
                if (格 > 0) 满 = 当 * 格 / 总;
            }
            百分 = Math.Clamp(百分, 0, 100);
            满 = Math.Clamp(满, 0, Math.Max(格, 0));
            return "[" + new string('#', 满) + new string('.', Math.Max(0, 格 - 满)) + "] " + 百分 + "%";
        }

        /// <summary>按模板写(照 `时间.hb` 头注释那张记号表)。</summary>
        private static string 参考按模板写(DateTime 本, string 模板)
        {
            var 果 = new StringBuilder();
            int i = 0;
            while (i < 模板.Length)
            {
                int 剩 = 模板.Length - i;
                string 四 = 剩 >= 4 ? 模板.Substring(i, 4) : "";
                string 两 = 剩 >= 2 ? 模板.Substring(i, 2) : "";
                if (四 == "YYYY") { 果.Append(本.Year.ToString("D4")); i += 4; continue; }
                if (两 == "YY") { 果.Append((本.Year % 100).ToString("D2")); i += 2; continue; }
                if (两 == "MM") { 果.Append(本.Month.ToString("D2")); i += 2; continue; }
                if (两 == "DD") { 果.Append(本.Day.ToString("D2")); i += 2; continue; }
                if (两 == "hh") { 果.Append(本.Hour.ToString("D2")); i += 2; continue; }
                if (两 == "mm") { 果.Append(本.Minute.ToString("D2")); i += 2; continue; }
                if (两 == "ss") { 果.Append(本.Second.ToString("D2")); i += 2; continue; }
                if (两 == "WW") { 果.Append("星期" + "日一二三四五六"[(int)本.DayOfWeek]); i += 2; continue; }
                char c = 模板[i];
                switch (c)
                {
                    case 'M': 果.Append(本.Month); break;
                    case 'D': 果.Append(本.Day); break;
                    case 'h': 果.Append(本.Hour); break;
                    case 'm': 果.Append(本.Minute); break;
                    case 's': 果.Append(本.Second); break;
                    case 'W': 果.Append((int)本.DayOfWeek); break;
                    default: 果.Append(c); break;
                }
                i++;
            }
            return 果.ToString();
        }

        /// <summary>认 `YYYY-MM-DD[ hh:mm[:ss]]`(UTC 秒;认不出给假)。</summary>
        private static bool 参考按模板读(string 文本, out long 秒)
        {
            秒 = 0;
            var 段 = new List<long>();
            long 值 = 0;
            bool 有 = false, 坏 = false;
            foreach (char c in 文本)
            {
                if (c >= '0' && c <= '9') { 值 = 值 * 10 + (c - '0'); 有 = true; }
                else if (有)
                {
                    段.Add(值); 值 = 0; 有 = false;
                    if (段.Count > 6) 坏 = true;
                }
            }
            if (有) { 段.Add(值); if (段.Count > 6) 坏 = true; }
            if (坏 || 段.Count < 3) return false;
            long 年 = 段[0];
            int 月 = (int)段[1], 日 = (int)段[2];
            int 时 = 段.Count > 3 ? (int)段[3] : 0;
            int 分 = 段.Count > 4 ? (int)段[4] : 0;
            int 秒s = 段.Count > 5 ? (int)段[5] : 0;
            if (月 < 1 || 月 > 12) return false;
            if (日 < 1 || 日 > DateTime.DaysInMonth((int)年, 月)) return false;
            if (时 < 0 || 时 > 23 || 分 < 0 || 分 > 59 || 秒s < 0 || 秒s > 59) return false;
            秒 = new DateTimeOffset((int)年, 月, 日, 时, 分, 秒s, TimeSpan.Zero).ToUnixTimeSeconds();
            return true;
        }
    }
}
