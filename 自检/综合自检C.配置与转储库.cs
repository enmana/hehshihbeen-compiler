// ============================================================
// 配置 / 十六进制转储 / 版本号 三个库的自检(G4 批 4,第347批)
//   —— `Sample\库\配置.hb` · `Sample\库\十六进制转储.hb` · `Sample\库\版本号.hb`
//
//   判据照 `基础库施工计划.md` §2 批 4 那一行:"转储三栏对表;键=值 / INI 读改写回"。
//   这一组这么验(三条行为 + 一条接线哨兵,硬规矩 5 / T22):
//     · **转储三栏**是"两边对拍":HB 里往堆块塞已知字节,`转储(块, N)` 的输出与 C# 现场按
//       同一份格式说明拼出来的整段文本**逐字比**;N 取 32 / 5 / 176 / 0 —— 176 那一档是为了
//       让偏移栏走到 `0xA0`(查十六进制大写与行循环),0 那一档查"给 0 给空串";
//     · **版本号**的期望值由 C# 参考实现现场算(三段 / 缺段当 0 / `v` 前缀 / 第四段不参与),
//       真编真跑逐点比 —— 不手抄一串数当基准;
//     · **配置**是"真写一份 INI → 读 → 取四档 → 改一条加一条 → 存 → 把回写的文件读出来逐字比",
//       顺带钉两条口径:① 同一份**重读一次**老句柄当场作废(查它给 -1);
//       ② **没有段的键写回最前面** —— 不这么写它就落进上一个段里,读回来会变成那个段的键;
//     · **接线哨兵**只钉"三个库在位 + 五段头注释齐 + 头注释的函数条数与正文对得上",
//       不许单独当正确性证据(硬规矩 5 / T22)。
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
        private static void 配置与转储库自检()
        {
            string? 示例 = 找示例目录();
            string? 仓库根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
            string 库目录 = 仓库根 == null ? "" : Path.Combine(仓库根, "Sample", "库");
            string 配路径 = Path.Combine(库目录, "配置.hb").Replace('\\', '/');
            string 转路径 = Path.Combine(库目录, "十六进制转储.hb").Replace('\\', '/');
            string 版路径 = Path.Combine(库目录, "版本号.hb").Replace('\\', '/');
            if (!File.Exists(配路径) || !File.Exists(转路径) || !File.Exists(版路径))
            {
                跳过("配置与转储库 定位 Sample\\库 下三个库",
                    $"找不到 {配路径} / {转路径} / {版路径}(不在工作副本里跑就跳过)");
                return;
            }
            string 头 = $"包含 \"{配路径}\"\n包含 \"{转路径}\"\n包含 \"{版路径}\"\n";
            string 临时目录 = Path.Combine(仓库根!, "工具", "临时", "hbcc_配置与转储").Replace('\\', '/');

            static string 跑(string 源)
            {
                var 编译 = new 汇编器C().汇编多文件带调试(
                    new List<(string, string)> { ("主程序.hb", 源) }, true,
                    中间层C.默认堆大小, null, 中间层C.默认池大小,
                    虚拟机C.默认内存大小, 32 * 1024);
                var vm = new 虚拟机C { 宿主调用 = 宿主能力C.处理 };    // 读 / 写 / 建目录都是宿主能力
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
                if (期望 == 实得) return "一字不差";
                string[] 期望段 = 期望.Split('|'), 实得段 = 实得.Split('|');
                int 少 = Math.Min(期望段.Length, 实得段.Length);
                for (int i = 0; i < 少; i++)
                    if (期望段[i] != 实得段[i])
                        return $"第 {i + 1} 个输出对不上:期望 [{期望段[i]}] / 实得 [{实得段[i]}];实得头 12 个 = " +
                            string.Join(",", 实得段.Take(12)) + $";实得共 {实得段.Length} 条 / 期望共 {期望段.Length} 条";
                return $"条数对不上:期望 {期望段.Length} 条 / 实得 {实得段.Length} 条";
            }

            // ---- ① 转储三栏:与 C# 现场按同一份格式说明拼的表逐字比(32 / 5 / 176 / 0 字节) ----
            try
            {
                byte[] 主块 = new byte[32];
                Array.Copy(Encoding.ASCII.GetBytes("Hello World!"), 主块, 12);   // 0~11:可打印串
                主块[12] = 0x00; 主块[13] = 0x7F; 主块[14] = 0x0A; 主块[15] = 0x20;   // 不可打印 / 空格
                主块[16] = 0x00; 主块[17] = 0x01; 主块[18] = 0x02; 主块[19] = 0x03;
                主块[20] = 0x7E; 主块[21] = 0x7F; 主块[22] = 0x80; 主块[23] = 0x00;   // 可打印边界 7E / 7F / 80
                主块[24] = 0x0A; 主块[25] = 0x0D; 主块[26] = 0x09; 主块[27] = 0x00;   // 换行 / 回车 / 制表
                主块[28] = 0xFF; 主块[29] = 0xFE; 主块[30] = 0x00; 主块[31] = 0x00;
                byte[] 对照块 = new byte[176];                                     // 偏移栏走到 0xA0
                for (int i = 0; i < 对照块.Length; i++) 对照块[i] = (byte)(i % 4 == 3 ? 0 : (i * 7 + 3) % 128);

                var 源 = new StringBuilder(头);
                源.Append("地址 主块 = 申请(64);\n地址 对照块 = 申请(192);\n");
                源.Append(转储块源码("主块", 主块));
                源.Append(转储块源码("对照块", 对照块));
                源.Append("输出(十六进制转储.转储(主块, 32));\n");
                源.Append("输出(十六进制转储.转储(主块, 5));\n");
                源.Append("输出(十六进制转储.转储(对照块, 176));\n");
                源.Append("输出行(十六进制转储.转储(主块, 0).长度());\n");
                源.Append("停止\n");
                // 跑() 收尾会 Trim() 再把换行换成 `|`,期望这一边照同一套手续办(不然永远对不上)
                string 期 = (参考转储(主块, 32) + 参考转储(主块, 5) + 参考转储(对照块, 176) + "0")
                    .Replace("\r\n", "\n").Trim().Replace("\n", "|");
                记录("配置与转储库 转储:偏移 + 十六进制 + ASCII 三栏(32 / 5 / 176 / 0 字节,与 .NET 现场按同一口径拼的表逐字比)",
                    跑(源.ToString()) == 期, 对拍说明(期, 跑(源.ToString())));
            }
            catch (Exception ex) { 记录异常("配置与转储库 转储三栏", ex); }

            // ---- ② 版本号:9 组谁更新 + 5 组解析,期望值由 C# 参考实现现场算 ----
            try
            {
                (string 左, string 右)[] 对表 =
                [
                    ("1.2.10", "1.2.9"), ("1.2.9", "1.2.10"), ("1.2", "1.2.0"), ("1", "0.9.9"),
                    ("v2.0", "1.9.9"), ("V1.0", "1.0.0"), ("1.2.3.4", "1.2.3.9"), ("1.2a", "1.2"),
                    ("", "0.0.0"),
                ];
                string[] 文本表 = ["3.14.159", "v1", "", "1.2.3.4", "7.8"];
                var 源 = new StringBuilder(头);
                var 期 = new StringBuilder();
                foreach (var (左, 右) in 对表)
                {
                    源.Append($"输出行(版本号.谁更新(\"{左}\", \"{右}\"));\n");
                    期.Append(参考谁更新(左, 右)).Append('|');
                }
                源.Append("整数 主 = 0;\n整数 次 = 0;\n整数 修 = 0;\n");
                foreach (string 文本 in 文本表)
                {
                    源.Append($"输出行(版本号.解析(\"{文本}\", 主, 次, 修));\n");
                    源.Append("输出行(主);\n输出行(次);\n输出行(修);\n");
                    期.Append(文本.Length > 0 ? 1 : 0).Append('|')
                        .Append(参考版本段(文本, 0)).Append('|')
                        .Append(参考版本段(文本, 1)).Append('|')
                        .Append(参考版本段(文本, 2)).Append('|');
                }
                源.Append("停止\n");
                string 期望 = 期.ToString().TrimEnd('|');
                记录("配置与转储库 版本号:9 组谁更新 + 5 组解析(含 v 前缀 / 缺段 / 第四段 / 混字母 / 空串),与 .NET 参考逐点比",
                    跑(源.ToString()) == 期望, 对拍说明(期望, 跑(源.ToString())));
            }
            catch (Exception ex) { 记录异常("配置与转储库 版本号", ex); }

            // ---- ③ 配置:真写一份 INI → 读 → 取四档 → 改一条加一条 → 存 → 回写文件逐字比 ----
            try
            {
                string 原路径 = 临时目录 + "/原.ini";
                string 回路径 = 临时目录 + "/回.ini";
                Directory.CreateDirectory(临时目录);
                // `#` / `;` 注释、没有 `=` 的行、两边空白、没有段的键、段里的键 —— 一份把口径都排上的 INI
                File.WriteAllText(原路径,
                    "# 注释行\n; 也是注释\n名字 = 局域网\n[server]\nhost = 127.0.0.1\n  port=8080\n没有等号的行\n[ui]\ntheme = 暗\n",
                    new UTF8Encoding(false));

                var 源 = new StringBuilder(头);
                源.Append("整数 老 = 0;\n");
                源.Append($"整数 本 = 配置.读(\"{原路径}\");\n");          // 哪一格没用过就用哪一格
                源.Append("输出行(本 > 0);\n");
                源.Append("输出行(配置.条数(本));\n");
                源.Append($"整数 本2 = 配置.读(\"{原路径}\");\n");        // 第二格还空着 ⇒ 用第二格(第一格照旧有效)
                源.Append("输出行(配置.条数(本2));\n");
                源.Append("老 = 本2;\n");
                源.Append($"本 = 配置.读(\"{原路径}\");\n");              // 两格都用过 ⇒ 复用"上一次那一格" + 版本 +1
                源.Append("输出行(配置.条数(老));\n");                    // ⇒ 那一格的老句柄当场作废 ⇒ -1
                源.Append("输出行(配置.条数(本));\n");
                源.Append("输出行(配置.取(本, \"名字\", \"无\"));\n");
                源.Append("输出行(配置.取(本, \"server.host\", \"无\"));\n");
                源.Append("输出行(配置.取整数(本, \"server.port\", 0));\n");
                源.Append("输出行(配置.取(本, \"ui.theme\", \"无\"));\n");
                源.Append("输出行(配置.取(本, \"server.nope\", \"缺省\"));\n");
                源.Append("输出行(配置.取逻辑(本, \"ui.theme\", 假));\n");   // 值不是逻辑档 ⇒ 给默认
                源.Append("输出行(配置.设(本, \"server.port\", \"9090\"));\n");
                源.Append("输出行(配置.设(本, \"散键\", \"到处跑\"));\n");     // 没有段的键
                源.Append("输出行(配置.条数(本));\n");
                源.Append($"输出行(配置.存(本, \"{回路径}\"));\n");
                源.Append("字符串 回文本 = \"\";\n");
                源.Append($"输出行(读文件(\"{回路径}\", 回文本, 4096));\n");
                源.Append("输出(回文本);\n");
                源.Append($"本2 = 配置.读(\"{回路径}\");\n");
                源.Append("输出行(配置.条数(本2));\n");
                源.Append("输出行(配置.取(本2, \"散键\", \"没找到\"));\n");
                源.Append("输出行(配置.取(本2, \"server.散键\", \"没找到\"));\n");
                源.Append("输出行(配置.取整数(本2, \"server.port\", 0));\n");
                源.Append("停止\n");

                // 期望 = 逐条现场算:没有段的键写最前面,再按条数顺序写、段一变补一行 `[段]`
                var 期 = new StringBuilder();
                期.Append("1|4|4|-1|4|局域网|127.0.0.1|8080|暗|缺省|0|1|1|5|1|1|");
                期.Append("名字=局域网|散键=到处跑|[server]|host=127.0.0.1|port=9090|[ui]|theme=暗|");
                期.Append("5|到处跑|没找到|9090");
                string 期望 = 期.ToString();
                记录("配置与转储库 配置:一份带段 / 注释 / 无等号行的 INI,读 → 取四档 → 改一条加一条 → 存 → 把回写的文件读出来逐字比(含老句柄作废与没有段的键)",
                    跑(源.ToString()) == 期望, 对拍说明(期望, 跑(源.ToString())));
            }
            catch (Exception ex) { 记录异常("配置与转储库 配置读写", ex); }
            finally
            {
                try { if (Directory.Exists(临时目录)) Directory.Delete(临时目录, true); }
                catch { /* 自检自己收尾:删不掉不算这一组的失败 */ }
            }

            // ---- ④ 接线哨兵:三个库在位、五段头注释齐、头注释的函数条数与正文对得上 ----
            try
            {
                string 配源 = File.ReadAllText(配路径), 转源 = File.ReadAllText(转路径), 版源 = File.ReadAllText(版路径);
                记录("配置与转储库 接线哨兵:三个库在 Sample\\库 下、五段头注释齐、头注释的函数条数与正文对得上",
                    配源.Contains("模块 配置", StringComparison.Ordinal)
                    && 转源.Contains("模块 十六进制转储", StringComparison.Ordinal)
                    && 版源.Contains("模块 版本号", StringComparison.Ordinal)
                    && 配源.Contains("提供的函数(7 个)", StringComparison.Ordinal)
                    && 转源.Contains("提供的函数(2 个)", StringComparison.Ordinal)
                    && 版源.Contains("提供的函数(2 个)", StringComparison.Ordinal)
                    && 五段齐(配源) && 五段齐(转源) && 五段齐(版源)
                    && 公开方法数(配源) == 7 && 公开方法数(转源) == 2 && 公开方法数(版源) == 2
                    // 库函数名不许撞 ISA 助记符:`比较` 是 CMP,中英切换会把它翻成 `CMP`(硬规矩 8 同一把尺子)
                    && !版源.Contains("方法 比较(", StringComparison.Ordinal),
                    $"公开方法:配置 {公开方法数(配源)} / 转储 {公开方法数(转源)} / 版本号 {公开方法数(版源)}");

                static bool 五段齐(string 源) =>
                    源.Contains("口径(2026-", StringComparison.Ordinal)
                    && 源.Contains("英文对照", StringComparison.Ordinal)
                    && 源.Contains("复杂度:", StringComparison.Ordinal)
                    && 源.Contains("边界(如实登记", StringComparison.Ordinal);
                static int 公开方法数(string 源) =>
                    System.Text.RegularExpressions.Regex.Matches(源, @"(?m)^方法 ").Count;
            }
            catch (Exception ex) { 记录异常("配置与转储库 接线哨兵", ex); }
        }

        /// <summary>`转储` 的参考实现(照 `十六进制转储.hb` 头注释那份格式说明逐字拼:每行 16 字节,第 8 个字节后多一个空格)</summary>
        private static string 参考转储(byte[] 数据, int 字节数)
        {
            if (字节数 <= 0) return "";
            var 果 = new StringBuilder();
            int 行起 = 0;
            while (行起 < 字节数)
            {
                int 本行 = Math.Min(16, 字节数 - 行起);
                果.Append(行起.ToString("X8")).Append("  ");
                for (int k = 0; k < 16; k++)
                {
                    if (k == 8) 果.Append(' ');
                    果.Append(k < 本行 ? 数据[行起 + k].ToString("X2") : "  ").Append(' ');
                }
                果.Append(' ').Append('|');
                for (int k = 0; k < 本行; k++)
                {
                    byte 值 = 数据[行起 + k];
                    果.Append(值 >= 32 && 值 <= 126 ? (char)值 : '.');
                }
                果.Append('|').Append('\n');
                行起 += 16;
            }
            return 果.ToString();
        }

        /// <summary>`版本号.段值` 的参考实现(0 起;缺的段给 0;开头 `v` / `V` 跳过;段里只收数字,别的字符这一段就算完)</summary>
        private static int 参考版本段(string 文本, int 第几)
        {
            int 段 = 0, 值 = 0, i = 0;
            if (文本.Length > 0 && (文本[0] == 'v' || 文本[0] == 'V')) i = 1;
            while (i < 文本.Length)
            {
                char 码 = 文本[i];
                if (码 == '.')
                {
                    if (段 == 第几) return 值;
                    段++;
                }
                else if (码 >= '0' && 码 <= '9')
                {
                    if (段 == 第几) 值 = 值 * 10 + (码 - '0');
                }
                i++;
            }
            return 值;
        }

        /// <summary>`版本号.谁更新` 的参考实现(只比 主 / 次 / 修订 三段)</summary>
        private static int 参考谁更新(string 左, string 右)
        {
            for (int i = 0; i < 3; i++)
            {
                int 左段 = 参考版本段(左, i), 右段 = 参考版本段(右, i);
                if (左段 > 右段) return 1;
                if (左段 < 右段) return -1;
            }
            return 0;
        }

        /// <summary>把一个字节数组编成"往堆块里按 4 字节字写进去"的 HB 源码(堆按字读写,小端)</summary>
        private static string 转储块源码(string 块名, byte[] 数据)
        {
            if (数据.Length % 4 != 0) throw new InvalidOperationException("夹具只按 4 字节字准备数据");
            var 果 = new StringBuilder();
            for (int 起 = 0; 起 < 数据.Length; 起 += 4)
            {
                int 字 = 数据[起] | (数据[起 + 1] << 8) | (数据[起 + 2] << 16) | (数据[起 + 3] << 24);
                if (字 < 0) throw new InvalidOperationException($"第 {起} 个字的最高字节 ≥ 0x80,HB 字面量写不出来");
                果.Append($"存字({块名}, {起}, 0x{字:X8});\n");
            }
            return 果.ToString();
        }
    }
}
