// ============================================================
// 编码 / HTML转义 / 通配 三个库的自检(G4 批 12,第357批)
//   —— `Sample\库\编码.hb` · `HTML转义.hb` · `通配.hb`
//
//   判据照 `基础库施工计划.md` §2 批 12 那一行:**"编解码往返逐字节同;通配 `*` `?`"**。
//   这一组八块(行为断言为主 + 一条接线哨兵;硬规矩 5 / T22):
//     ① **块那两半与 .NET 逐条对拍**:`Convert.ToHexString` / `Convert.ToBase64String` 与
//        `编码.转十六进制 / 转Base64` 同一批字节逐字符比(几个长度都造,含 0 字节 / 补位那两档);
//     ② **串那两半的往返**:`转十六进制串 / 解析十六进制串`、`转Base64串 / 解析Base64串`
//        (原文 → 编码 → 解码 ⇒ 逐字节同;认不出的那几档给 `假`);
//     ③ **URL 百分号与 `Uri.EscapeDataString` 逐字符比** —— 安全字符集取 RFC 3986 的
//        unreserved(`A~Z a~z 0~9 - _ . ~`),与 .NET 那一条**同一个口径**;
//     ④ **HTML 转义 / 反转义与 `WebUtility.HtmlEncode / HtmlDecode` 逐条比**
//        (`&#39;` 那一档 .NET 也是这么编的;认得出来的命名实体与数字实体都过一遍);
//     ⑤ **BCD 与 C# 参考实现逐条对拍**(打包的十六进制 + 解回来的十进制文字;非数字 / 放不下给 -1);
//     ⑥ **通配与 C# 参考实现逐条对拍**(文本 × 模式的小矩阵:真 / 假 / 忽略大小写 / `找位置`);
//     ⑦ **端到端**:标准工程 **`示例44_编码与通配`** 真编真跑,与它那行 `// 预期输出:` 逐行对;
//     ⑧ **接线哨兵**:三个库都在盘上 + 头注释五段齐 + 公开方法个数对上(17 / 2 / 3)。
//
//   ⚠ 这一组**不引 `测试助手.hb`**:库与库之间互相引会把"谁坏了"搅浑(自检要能一眼定位)。
//   ⚠ 通配那一块的参考实现**照被验方的口径写**(只折 ASCII 的 `A~Z`),见 §3 那条教训。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        private static void 编码与通配库自检()
        {
            string? 示例 = 找示例目录();
            string? 仓库根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
            string 库目录 = 仓库根 == null ? "" : Path.Combine(仓库根, "Sample", "库");
            string 编路径 = Path.Combine(库目录, "编码.hb").Replace('\\', '/');
            string 转路径 = Path.Combine(库目录, "HTML转义.hb").Replace('\\', '/');
            string 通路径 = Path.Combine(库目录, "通配.hb").Replace('\\', '/');
            string 内路径 = Path.Combine(库目录, "内存.hb").Replace('\\', '/');
            if (!File.Exists(编路径) || !File.Exists(转路径) || !File.Exists(通路径) || !File.Exists(内路径))
            {
                跳过("编码与通配库 定位库文件", $"找不到 {编路径} / {转路径} / {通路径} / {内路径}(不在工作副本里跑就跳过)");
                return;
            }
            // 那两个"带前缀"的帮手是**现拼进 HB 夹具**的:`跑()` 收尾会 `Trim()` 再按 `|` 拼,
            //   空结果会被吃掉 ⇒ 每一条都留一个非空前缀;整数要拼成文字就用**夹具自带**的
            //   `数文`(`拼接到` 只收字符串 / 一个字节 / 一个文字;它是夹具,不是库)。
            //   ⚠ **不引 `string.hb`**:实测它与 `编码.hb` 一起编会把 base64 那一支算坏
            //   (H76,见 `未做任务清单.md` §0;最小复现留在 `工具\临时\批12探针\H76最小复现*.hb`)。
            string 头 = $"包含 \"{内路径}\"\n包含 \"{编路径}\"\n包含 \"{转路径}\"\n包含 \"{通路径}\"\n"
                + "方法 数文(整数 值) 返回 字符串\n"
                + "{\n"
                + "    字符串 果 = \"\";\n"
                + "    如果 (值 < 0)\n"
                + "    {\n"
                + "        拼接到(果, \"-\");\n"
                + "        值 = 0 - 值;\n"
                + "    }\n"
                + "    无符号整数 剩 = 值;\n"
                + "    字符 数字[10];\n"
                + "    整数 位 = 0;\n"
                + "    当 (剩 > 0)\n"
                + "    {\n"
                + "        数字[位] = 剩 % 10 + '0';\n"
                + "        位 = 位 + 1;\n"
                + "        剩 = 剩 / 10;\n"
                + "    }\n"
                + "    如果 (位 == 0)\n"
                + "    {\n"
                + "        拼接到(果, '0');\n"
                + "    }\n"
                + "    当 (位 > 0)\n"
                + "    {\n"
                + "        位 = 位 - 1;\n"
                + "        拼接到(果, 数字[位]);\n"
                + "    }\n"
                + "    返回 果;\n"
                + "}\n"
                + "方法 带前缀(字符串 标, 字符串 值) 返回 字符串\n"
                + "{\n"
                + "    字符串 果 = \"\";\n"
                + "    拼接到(果, 标, 值);\n"
                + "    返回 果;\n"
                + "}\n"
                + "方法 带前缀逻辑(字符串 标, 逻辑 值) 返回 字符串\n"
                + "{\n"
                + "    字符串 果 = \"\";\n"
                + "    拼接到(果, 标);\n"
                + "    如果 (值) { 拼接到(果, \"1\"); } 否则 { 拼接到(果, \"0\"); }\n"
                + "    返回 果;\n"
                + "}\n"
                + "方法 带前缀2(字符串 标, 整数 值, 字符串 文, 字符串 模) 返回 字符串\n"
                + "{\n"
                + "    字符串 果 = \"\";\n"
                + "    字符串 数值文 = \"\";\n"
                + "    拼接到(数值文, 数文(值));\n"
                + "    拼接到(果, 标, \"文[\", 文, \"]模[\", 模, \"]\", 数值文);\n"
                + "    返回 果;\n"
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

            static string 对拍说明(string 期望, string 实得)            {
                if (期望 == 实得) return $"一字不差({期望.Split('|').Length} 条)";
                string[] 期望段 = 期望.Split('|'), 实得段 = 实得.Split('|');
                int 少 = Math.Min(期望段.Length, 实得段.Length);
                for (int i = 0; i < 少; i++)
                    if (期望段[i] != 实得段[i])
                        return $"第 {i + 1} 个输出对不上:期望 [{期望段[i]}] / 实得 [{实得段[i]}](期望 {期望段.Length} 条 / 实得 {实得段.Length} 条)";
                return $"条数对不上:期望 {期望段.Length} 条 / 实得 {实得段.Length} 条";
            }

            // ============================================================
            // ① 块那两半:转十六进制 / 转Base64 与 .NET 逐条对拍
            // ============================================================
            try
            {
                var 随机 = new Random(20261020);
                // 九批字节:0 / 1 / 2 / 3 / 4(补位两档)/ 16 / 33 / 64 / 全 0
                var 批们 = new List<byte[]>
                {
                    Array.Empty<byte>(),
                    new byte[] { 0x00 },
                    new byte[] { 0xFF, 0x01 },
                    new byte[] { 0x00, 0x01, 0x02 },
                    new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
                    Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(),
                    Enumerable.Range(0, 33).Select(i => (byte)(i * 7)).ToArray(),
                    Enumerable.Range(0, 64).Select(i => (byte)(255 - i)).ToArray(),
                    new byte[8],
                };
                for (int i = 0; i < 6; i++)
                {
                    var 一 = new byte[1 + 随机.Next(0, 40)];
                    随机.NextBytes(一);
                    批们.Add(一);
                }

                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                for (int k = 0; k < 批们.Count; k++)
                {
                    源.Append($"地址 块{k} = 申请(256);\n");
                    源.Append($"内存.清零(块{k}, 64);\n");       // 同一块只在这一轮用;清零是为了挡"上一轮/相邻块"的尾巴
                    源.Append($"编码.解析十六进制(\"{Convert.ToHexString(批们[k])}\", 块{k}, 256);\n");
                    源.Append($"输出行(带前缀(\"H\", 编码.转十六进制(块{k}, 4)));\n");
                    源.Append($"输出行(带前缀(\"A\", 编码.转十六进制(块{k}, {批们[k].Length})));\n");
                    源.Append($"输出行(带前缀(\"B\", 编码.转Base64(块{k}, {批们[k].Length})));\n");
                    string 头八字 = 批们[k].Length >= 4
                        ? Convert.ToHexString(批们[k], 0, 4)
                        : Convert.ToHexString(批们[k]) + new string('0', (4 - 批们[k].Length) * 2);
                    期望.Append('H').Append(头八字).Append('|');
                    期望.Append('A').Append(Convert.ToHexString(批们[k])).Append('|');
                    期望.Append('B').Append(Convert.ToBase64String(批们[k])).Append('|');
                }
                for (int k = 0; k < 批们.Count; k++)
                    源.Append($"释放(块{k});\n");
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("编码与通配库 块那两半:转十六进制 / 转Base64 与 .NET 的 Convert 逐字符对拍(含 0 字节 / 1 / 2 / 3 字节补位档 / 随机 8 批)",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("编码与通配库 块那两半", ex); }

            // ============================================================
            // ② 串那两半的往返 + 认不出那几档
            // ============================================================
            try
            {
                var 样们 = new[] { "", "a", "ab", "abc", "Hello, World!", "中", "中文测试", "~!@#$%^&*()", "\x01\x02\x7F\x80\xFF" };
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                foreach (string 样 in 样们)
                {
                    string 转义样 = 样.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    源.Append($"字符串 原 = \"{转义样}\";\n");
                    源.Append("字符串 回 = \"\";\n");
                    源.Append("输出行(带前缀(\"A\", 编码.转十六进制串(原)));\n");
                    源.Append("输出行(带前缀(\"B\", 编码.转Base64串(原)));\n");
                    源.Append("输出行(带前缀逻辑(\"C\", 编码.解析十六进制串(编码.转十六进制串(原), 回)));\n");
                    源.Append("输出行(带前缀(\"D\", 回));\n");
                    源.Append("输出行(带前缀逻辑(\"E\", 编码.解析Base64串(编码.转Base64串(原), 回)));\n");
                    源.Append("输出行(带前缀(\"F\", 回));\n");
                    var 字节 = Encoding.UTF8.GetBytes(样);
                    期望.Append('A').Append(Convert.ToHexString(字节)).Append('|');
                    期望.Append('B').Append(Convert.ToBase64String(字节)).Append('|');
                    // 空串那一档:空十六进制 / 空 Base64 都"认不出"(没有字节可解)⇒ `假`(0);其余给真
                    string 成 = 样.Length == 0 ? "0" : "1";
                    期望.Append('C').Append(成).Append('|').Append('D').Append(样).Append('|');
                    期望.Append('E').Append(成).Append('|').Append('F').Append(样).Append('|');                }
                // 认不出的那几档
                foreach (string 坏 in new[] { "", "1", "12G4", "0x12", "1 2" })
                {
                    源.Append($"字符串 坏 = \"{坏}\";\n");
                    源.Append("字符串 出坏 = \"\";\n");
                    源.Append("输出行(带前缀逻辑(\"H\", 编码.解析十六进制串(坏, 出坏)));\n");
                    期望.Append("H0").Append('|');
                }                foreach (string 坏 in new[] { "", "a", "****", "YQ===", "SGVs*bG8=" })
                {
                    源.Append($"字符串 坏 = \"{坏}\";\n");
                    源.Append("字符串 出坏 = \"\";\n");
                    源.Append("输出行(带前缀逻辑(\"M\", 编码.解析Base64串(坏, 出坏)));\n");
                    期望.Append("M0").Append('|');
                }
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("编码与通配库 串那两半往返(九种字面量逐字节同)+ 认不出的那几档一律给假(奇数位 / 夹字母 / 空串 / 夹 `*` / 多补位)",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("编码与通配库 串那两半", ex); }

            // ============================================================
            // ③ URL 百分号:与 Uri.EscapeDataString 逐字符对拍 + 往返
            // ============================================================
            try
            {
                var 样们 = new[] { "", "a", "a b", "a/b?c=d&e", "中", "张 三&李四=1", "-_.~", "~!*'()", "https://例子.测试/路径?q=值" };
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                foreach (string 样 in 样们)
                {
                    string 转义样 = 样.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    源.Append($"字符串 原 = \"{转义样}\";\n");
                    源.Append("字符串 出 = \"\";\n");
                    源.Append("编码.转百分号编码(原, 出);\n");
                    源.Append("输出行(带前缀(\"A\", 出));\n");
                    源.Append("字符串 回 = \"\";\n");
                    源.Append("编码.解析百分号编码(出, 回);\n");
                    源.Append("输出行(带前缀(\"B\", 回));\n");
                    期望.Append('A').Append(Uri.EscapeDataString(样)).Append('|');
                    期望.Append('B').Append(样).Append('|');
                }
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("编码与通配库 URL 百分号:与 .NET 的 Uri.EscapeDataString 逐字符对拍 + 解回来一字不差(含中文 / 空格 / 保留字符)",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("编码与通配库 URL", ex); }

            // ============================================================
            // ④ HTML 转义 / 反转义:与 WebUtility 对拍
            // ============================================================
            try
            {
                var 样们 = new[] { "", "abc", "<b>张三</b>", "a & b", "引号 \" 与 ' 都要", "<a href=\"x\">O'Neil & Co</a>", "中文 & <标签> \"引\"" };
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                foreach (string 样 in 样们)
                {
                    string 转义样 = 样.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    源.Append($"输出行(带前缀(\"A\", HTML转义.转义(\"{转义样}\")));\n");
                    期望.Append('A').Append(WebUtility.HtmlEncode(样)).Append('|');
                }
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("编码与通配库 HTML 转义:与 .NET 的 WebUtility.HtmlEncode 逐字对拍(五个字符 / 顺序 / 单引号走 &#39;)",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("编码与通配库 HTML 转义", ex); }

            // ============================================================
            // ⑤ HTML 反转义:命名实体 / 数字实体 / 认不出的照原样
            // ============================================================
            try
            {
                var 样们 = new[]
                {
                    "" , "abc", "&lt;b&gt;", "&amp;amp;", "&quot;q&quot;", "&#39;", "&apos;", "&nbsp;x",
                    "&#20013;&#x4E2D;", "&AMP;", "a &amp; b", "&foo;", "&", "&#;", "&#xZZ;", "&ampx", "a &amp b",
                };                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                foreach (string 样 in 样们)
                {
                    string 转义样 = 样.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    源.Append($"输出行(带前缀(\"A\", HTML转义.反转义(\"{转义样}\")));\n");
                    期望.Append('A').Append(参考反转义(样)).Append('|');
                }
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("编码与通配库 HTML 反转义:逐条对参考口径(命名实体**不区分大小写** / 十进制与十六进制数字实体 / 少了分号 / 认不出的照原样)",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("编码与通配库 HTML 反转义", ex); }

            // ============================================================
            // ⑥ BCD:与 C# 参考实现逐条对拍 + 边界
            // ============================================================
            try
            {
                var 随机 = new Random(20261021);
                var 样们 = new List<string> { "0", "00", "5", "05", "123", "1234", "20261005", "00000001", "99999999" };
                for (int i = 0; i < 8; i++)
                {
                    int 位 = 1 + 随机.Next(0, 12);
                    var 串 = new StringBuilder();
                    for (int k = 0; k < 位; k++) 串.Append((char)('0' + 随机.Next(0, 10)));
                    样们.Add(串.ToString());
                }
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                源.Append("地址 块 = 申请(64);\n");
                源.Append("字符串 回 = \"\";\n");
                foreach (string 样 in 样们)
                {
                    源.Append("内存.清零(块, 16);\n");          // 同一块反复用:先清零,免得上一档的尾巴混进十六进制那一行
                    源.Append($"输出行(编码.转BCD(\"{样}\", 块, 64));\n");
                    源.Append($"输出行(编码.转十六进制(块, {BCD字节数(样)}));\n");
                    源.Append($"输出行(编码.解析BCD(块, {BCD字节数(样)}, 回));\n");
                    源.Append("输出行(回);\n");
                    期望.Append(BCD字节数(样)).Append('|').Append(BCD十六进制(样)).Append('|');
                    期望.Append('1').Append('|').Append(BCD解回来(样)).Append('|');
                }
                // 边界:非数字 / 空串 / 放不下 / 半字节 > 9
                源.Append("输出行(编码.转BCD(\"12x\", 块, 64));\n");
                源.Append("输出行(编码.转BCD(\"\", 块, 64));\n");
                源.Append("输出行(编码.转BCD(\"1234567890123\", 块, 4));\n");
                源.Append("输出行(编码.解析BCD(块, 1, 回));\n");
                源.Append("输出行(回);\n");
                源.Append("编码.解析十六进制(\"0F\", 块, 64);\n");
                源.Append("输出行(编码.解析BCD(块, 1, 回));\n");
                源.Append("输出行(编码.解析BCD(块, 0, 回));\n");
                源.Append("输出行(回.长度());\n");
                源.Append("输出行(编码.转BCD串(\"12x\").长度());\n");
                源.Append("停止\n");
                期望.Append("-1|-1|-1|1|44|0|1|0|0");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("编码与通配库 BCD:打包 / 解回与 C# 参考实现逐条对拍(奇数位补前导零、前导 0 只吃一个)+ 非数字与放不下给 -1 / 半字节 > 9 给假",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("编码与通配库 BCD", ex); }

            // ============================================================
            // ⑦ 通配:与 C# 参考实现逐条对拍(文本 × 模式)
            // ============================================================
            try
            {
                var 文本们 = new[] { "", "a", "ab", "abc", "abcabc", "名单.txt", "报告.TXT", "中文测试", "版本 v1.2.3", "a*b", "aaaaab", "xaybzc", "..", "/usr/local" };
                var 模式们 = new[] { "", "*", "?", "a", "a?", "?c", "a*", "*c", "*b*", "*.txt", "*.TXT", "a**b", "*a*b*c", "??", "版本*", "v?.?.*", ".", "../*", "*/local" };
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                foreach (string 文本 in 文本们)
                    foreach (string 模式 in 模式们)
                    {
                        string 文转 = 文本.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        string 模转 = 模式.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        源.Append($"字符串 文 = \"{文转}\";\n");
                        源.Append($"字符串 模 = \"{模转}\";\n");
                        源.Append("输出行(带前缀2(\"P\", 通配.匹配(文, 模), 文, 模));\n");
                        源.Append("输出行(带前缀2(\"Q\", 通配.匹配忽略大小写(文, 模), 文, 模));\n");
                        源.Append("输出行(带前缀2(\"R\", 通配.找位置(文, 模), 文, 模));\n");
                        期望.Append("P文[").Append(文本).Append("]模[").Append(模式).Append("]").Append(参考匹配(文本, 模式, false) ? '1' : '0').Append('|');
                        期望.Append("Q文[").Append(文本).Append("]模[").Append(模式).Append("]").Append(参考匹配(文本, 模式, true) ? '1' : '0').Append('|');
                        期望.Append("R文[").Append(文本).Append("]模[").Append(模式).Append("]").Append(参考找位置(文本, 模式)).Append('|');
                    }
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("编码与通配库 通配匹配:14 文本 × 19 模式的小矩阵与 C# 参考实现逐条对拍(`*` / `?` / 空模式 / 空文本 / 中文按文字算)",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("编码与通配库 通配匹配", ex); }

            // ============================================================
            // ⑧ 端到端:标准工程 示例44_编码与通配
            // ============================================================
            try
            {
                string? 工程目录 = 示例 == null ? null : Path.Combine(示例, "中间层", "示例44_编码与通配");
                string? 源码 = 工程目录 == null ? null : Path.Combine(工程目录, "主程序.hb");
                if (源码 == null || !File.Exists(源码))
                {
                    跳过("编码与通配库 标准工程 示例44 逐行对预期输出", $"找不到 {源码}(不在工作副本里跑就跳过)");
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
                    bool 好 = 期望行.Count > 0 && 实得行.Count == 期望行.Count
                        && 实得行.Zip(期望行, (a, b) => a == b).All(x => x);
                    int 错行 = 0;
                    if (!好 && 期望行.Count == 实得行.Count)
                    {
                        for (int k = 0; k < 期望行.Count; k++)
                            if (期望行[k] != 实得行[k]) { 错行 = k + 1; break; }
                    }
                    记录("编码与通配库 标准工程 示例44 逐条对预期输出(文件头往返 / Base64 / URL / BCD / HTML 转义 / 通配筛名字)",
                        好, 好 ? $"逐行一致({期望行.Count} 行)" : $"期望 {期望行.Count} 行 / 实得 {实得行.Count} 行;" +
                            (错行 > 0
                                ? $"第 {错行} 行 期望[{期望行[错行 - 1]}] 实得[{实得行[错行 - 1]}]"
                                : "条数先对不上"));
                }
            }
            catch (Exception ex) { 记录异常("编码与通配库 标准工程 示例44", ex); }

            // ============================================================
            // ⑨ 接线哨兵
            // ============================================================
            try
            {
                string 编文 = File.ReadAllText(编路径);
                string 转文 = File.ReadAllText(转路径);
                string 通文 = File.ReadAllText(通路径);
                bool 头齐(string 文) => 文.Contains("口径(2026-", StringComparison.Ordinal)
                    && 文.Contains("提供的函数(", StringComparison.Ordinal)
                    && 文.Contains("英文对照", StringComparison.Ordinal)
                    && 文.Contains("复杂度", StringComparison.Ordinal)
                    && 文.Contains("边界(如实登记", StringComparison.Ordinal);
                int 法(string 文)
                {
                    int 个 = 0;
                    foreach (string 行 in 文.Split('\n'))
                        if (行.StartsWith("方法 ", StringComparison.Ordinal)) 个++;
                    return 个;
                }
                记录("编码与通配库 接线哨兵:三个库都在盘上、头注释五段齐、公开方法数(17 / 2 / 3)",
                    头齐(编文) && 头齐(转文) && 头齐(通文)
                    && 编文.Contains("模块 编码", StringComparison.Ordinal)
                    && 转文.Contains("模块 HTML转义", StringComparison.Ordinal)
                    && 通文.Contains("模块 通配", StringComparison.Ordinal)
                    && 法(编文) == 17 && 法(转文) == 2 && 法(通文) == 3,
                    $"公开方法 编码 {法(编文)} / HTML转义 {法(转文)} / 通配 {法(通文)}");
            }
            catch (Exception ex) { 记录异常("编码与通配库 接线哨兵", ex); }
        }

        // ---------- BCD 参考实现(照 `编码.hb` 的口径写:高位在前、奇数位补前导零) ----------

        /// <summary>一条十进制文字打包成 BCD 要几个字节。</summary>
        private static int BCD字节数(string 文本) => (文本.Length + 1) / 2;

        /// <summary>一条十进制文字打包成 BCD 之后的十六进制写法。</summary>
        private static string BCD十六进制(string 文本)
        {
            string 补 = 文本.Length % 2 == 1 ? "0" + 文本 : 文本;
            var 果 = new StringBuilder();
            for (int i = 0; i < 补.Length; i += 2)
            {
                int 值 = (补[i] - '0') * 16 + (补[i + 1] - '0');
                果.Append(值.ToString("X2"));
            }
            return 果.ToString();
        }

        /// <summary>BCD 解回来的十进制文字(只吃最前面那一个 0)。</summary>
        private static string BCD解回来(string 文本)
        {
            string 补 = 文本.Length % 2 == 1 ? "0" + 文本 : 文本;
            return 补.Length > 1 && 补[0] == '0' ? 补.Substring(1) : 补;
        }

        /// <summary>
        /// HTML 反转义的参考口径(**照 `HTML转义.hb` 的说明写**,不是照 .NET 的 `WebUtility.HtmlDecode`):
        /// 命名实体**不区分大小写**(`&AMP;` 也认),`;` 可以省,认不出的整段照原样。
        /// 为什么不用 .NET 那一份:.NET 只认小写的命名实体(`&AMP;` 它不认),
        /// 拿它当对拍方会在那一格上把"库的宽松口径"误判成错 —— 对拍方要与被验方同口径(批 10 那条教训)。
        /// </summary>
        private static string 参考反转义(string 文本)
        {
            var 结果 = new StringBuilder();
            int i = 0;
            while (i < 文本.Length)
            {
                if (文本[i] != '&') { 结果.Append(文本[i]); i++; continue; }
                int 止 = 文本.IndexOf(';', i);
                // `HTML转义.hb` 只认**带 `;`** 的实体 ⇒ 没写 `;` 就整段照原样搬(不再往下试)
                if (止 < 0) { 结果.Append('&'); i++; continue; }
                int 名止 = 止;
                bool 认出来了 = false;
                string 换 = "";
                if (i + 1 < 名止 && 文本[i + 1] == '#')
                {
                    int 起 = i + 2, 基 = 10;
                    if (起 < 名止 && (文本[起] == 'x' || 文本[起] == 'X')) { 基 = 16; 起++; }
                    int 码 = 0, 位 = 0;
                    while (起 + 位 < 名止)
                    {
                        char 字 = 文本[起 + 位];
                        int 数 = -1;
                        if (字 >= '0' && 字 <= '9') 数 = 字 - '0';
                        if (基 == 16)
                        {
                            if (字 >= 'a' && 字 <= 'f') 数 = 字 - 'a' + 10;
                            else if (字 >= 'A' && 字 <= 'F') 数 = 字 - 'A' + 10;
                        }
                        if (数 < 0) break;
                        码 = 码 * 基 + 数;
                        位++;
                    }
                    if (位 > 0)
                    {
                        换 = char.ConvertFromUtf32(码);
                        认出来了 = true;
                        名止 = 起 + 位;
                    }
                }
                else
                {
                    // 最长前缀先试(`&ampx` 要认成 `&amp` + `x`)
                    for (int 长试 = 名止 - i - 1; 长试 > 0 && !认出来了; 长试--)
                    {
                        string 名字 = 文本.Substring(i + 1, 长试);
                        switch (名字.ToLowerInvariant())
                        {
                            case "amp": 换 = "&"; 认出来了 = true; break;
                            case "lt": 换 = "<"; 认出来了 = true; break;
                            case "gt": 换 = ">"; 认出来了 = true; break;
                            case "quot": 换 = "\""; 认出来了 = true; break;
                            case "apos": 换 = "'"; 认出来了 = true; break;
                            case "nbsp": 换 = "\u00A0"; 认出来了 = true; break;
                        }
                        if (认出来了) 名止 = i + 1 + 长试;
                    }
                }
                if (!认出来了) { 结果.Append('&'); i++; }
                else { 结果.Append(换); i = 止 + 1; }
            }
            return 结果.ToString();
        }

        // ---------- 通配参考实现(照 `通配.hb` 的口径写:只折 ASCII 的 A~Z) ----------

        /// <summary>
        /// 参考实现**刻意与库那边同形**(递归 + `*` 逐个起点试):
        /// 这一条对拍的是"库算得对不对",不是"库算得快不快" —— 换个写法(迭代 + 单星回溯)会在
        /// `*a*b*c` 这种多星模式上给出**不同的组合顺序**,那是参考实现自己的事,与库无关。
        /// </summary>
        private static bool 参考匹配(string 文本, string 模式, bool 忽略大小写)
        {
            int 起 = 参考跳星(模式);
            if (起 == 模式.Length) return true;           // 全是 `*`(含空模式):什么都配
            return 参考找配上点(文本, 模式, 起, 0, 忽略大小写) >= 0;
        }

        private static int 参考找位置(string 文本, string 模式)
        {
            int 起 = 参考跳星(模式);
            if (起 == 模式.Length) return 0;
            return 参考找配上点(文本, 模式, 起, 0, false);
        }

        private static int 参考跳星(string 模式)
        {
            int 起 = 0;
            while (起 < 模式.Length && 模式[起] == '*') 起++;
            return 起;
        }

        private static int 参考找配上点(string 文本, string 模式, int 模位, int 从头, bool 忽略大小写)
        {
            for (int 试 = 从头; 试 <= 文本.Length; 试++)
                if (参考试着配(文本, 试, 模式, 模位, 忽略大小写)) return 试;
            return -1;
        }

        /// <summary>返回真 = "有一处能配上"(不要求配到文本末尾),与库那边 `试着配` 同口径。</summary>
        private static bool 参考试着配(string 文本, int 文位, string 模式, int 模位, bool 忽略大小写)
        {
            while (true)
            {
                if (模位 >= 模式.Length) return true;
                char 模字 = 模式[模位];
                if (模字 == '*')
                {
                    模位++;
                    while (模位 < 模式.Length && 模式[模位] == '*') 模位++;
                    if (模位 >= 模式.Length) return true;
                    for (int 试 = 文位; 试 <= 文本.Length; 试++)
                        if (参考试着配(文本, 试, 模式, 模位, 忽略大小写)) return true;
                    return false;
                }
                if (文位 >= 文本.Length) return false;
                if (模字 == '?')
                {
                    文位++; 模位++;
                }
                else
                {
                    if (!参考同字(文本[文位], 模字, 忽略大小写)) return false;
                    文位++; 模位++;
                }
            }
        }

        private static bool 参考同字(char 文本字, char 模式字, bool 忽略大小写)
        {
            if (忽略大小写)
            {
                if (文本字 >= 'A' && 文本字 <= 'Z') 文本字 = (char)(文本字 + 32);
                if (模式字 >= 'A' && 模式字 <= 'Z') 模式字 = (char)(模式字 + 32);
            }
            return 文本字 == 模式字;
        }
    }
}
