// ============================================================
// 表达式库自检(G4 批 5,第348批)
//   —— `Sample\库\表达式.hb`
//
//   判据照 `基础库施工计划.md` §2 批 5 那一行:"带括号与优先级;错表达式给带位置的说明"。
//   这一组这么验(三条行为 + 一条接线哨兵,硬规矩 5 / T22):
//     · **与 C# 参考实现对拍**:七十多条算式(优先级 / 括号 / 一元 / 十六进制 / 空白与换行 /
//       比较 / 逻辑 / 真短路 / 变量表 / 各种错算式)在 HB 里真编真跑,每条打印
//       "成功否 / 结果 / 出错格 / 错因"四行;C# 这一边照**同一份语法与错因表**另写一份
//       递归下降现场算 —— 两边逐行比。**不手抄一串数当基准**(改一处语法,两边必须一起改);
//     · **"说明"整句逐字对拍**:位置(1 起、按字符)、原因文、"那一格是哪个字符"、算式共几格 ——
//       这一条钉的就是"错表达式给带位置的说明"那句话本身(用户看到的就是它);
//     · **短路口径三条**:`0 && 1 / 0` / `1 || 1 / 0` / `0 && 名字没设过` 必须**成功且值为 0/1**、
//       `取出错格` 是 0 —— 短路那一支"只解析不算数"是这一库最容易被后来者改坏的一处;
//     · **接线哨兵**只钉"库在位 + 五段头注释齐 + 7 个公开方法 + **零依赖**(源码里没有一条 `包含`)",
//       不许单独当正确性证据(硬规矩 5 / T22)。
//
//   ⚠ 这一组比输出用的是**逐行**比(不是批 4 那种拿 `|` 拼起来比):错因 8 那句话里
//     本来就带 `|`(它就是列位运算符号的),拿 `|` 当分隔符会把自己劈开。
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
        private static void 表达式库自检()
        {
            string? 示例 = 找示例目录();
            string? 仓库根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
            string 库路径 = 仓库根 == null ? "" : Path.Combine(仓库根, "Sample", "库", "表达式.hb").Replace('\\', '/');
            if (库路径.Length == 0 || !File.Exists(库路径))
            {
                跳过("表达式库 定位 Sample\\库\\表达式.hb", $"找不到 {库路径}(不在工作副本里跑就跳过)");
                return;
            }
            string 头 = $"包含 \"{库路径}\"\n";
            string 帮手 =
                "方法 试算(字符串 算式)\n{\n    整数 果 = 0;\n" +
                "    输出行(表达式.求值(算式, 果));\n    输出行(果);\n" +
                "    输出行(表达式.取出错格());\n    输出行(表达式.取错因());\n}\n\n" +
                "方法 说话(字符串 算式)\n{\n    整数 果 = 0;\n" +
                "    表达式.求值(算式, 果);\n    输出行(表达式.说明(算式));\n}\n\n";

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
                return 缓冲.ToString().Replace("\r\n", "\n").TrimEnd('\n') + 报错;
            }
            static string 表达式对拍说明(string 期望, string 实得)
            {
                string[] 期望行 = 期望.Split('\n'), 实得行 = 实得.Split('\n');
                if (期望 == 实得) return $"逐行一致({期望行.Length} 行)";
                int 少 = Math.Min(期望行.Length, 实得行.Length);
                for (int i = 0; i < 少; i++)
                    if (期望行[i] != 实得行[i])
                        return $"第 {i + 1} 行对不上:期望 [{期望行[i]}] / 实得 [{实得行[i]}];" +
                            $"期望共 {期望行.Length} 行 / 实得共 {实得行.Length} 行";
                return $"行数对不上:期望 {期望行.Length} 行 / 实得 {实得行.Length} 行";
            }

            // ---- ① 求值对拍:七十多条算式 + 一段变量表脚本,逐行比 ----
            try
            {
                string[] 算式表 =
                [
                    "1 + 2 * 3", "(1 + 2) * 3", "2 * (3 + 4) - 5", "100 / 7", "100 % 7",
                    "100 / 7 * 7", "7 * 7 / 7", "-3 + 5", "2 * -3", "-(2 + 3)",
                    "- - 4", "!!!5", "!!0", "+5", "-0",
                    "0x1F + 1", "0x10 * 0x2", "0xff", "0Xa + 1",
                    "1 + 2 * 3 - 4 / 2", "((1 + 2) * (3 + 4))", "((((1))))",
                    "10 - 2 - 3", "2 * 3 % 4", "1 + 2 == 3", "2 < 3 < 1",
                    "5 >= 5", "5 <= 4", "3 != 3", "4 > 3", "4 < 3",
                    "1 || 0 && 0", "0 || 0 || 7", "1 && 2 && 3", "!0 + 1", "!5",
                    "  1 +\t2  ", "1 +\\n2", "0 && 1 / 0", "1 || 1 / 0",
                    "1 && 1 / 0", "0 || 1 / 0", "0 && 名字没设过", "1 || 名字没设过", "1 && 名字没设过",
                    "2147483647 + 1", "-2147483647 - 1", "1000000 * 1000000",
                    "7 / 0", "7 % 0", "1 + * 2", "(1 + 2", "1 2", "1 | 2",
                    "1 ^ 2", "1 & 2", "1 << 2", "1 >> 2", "", "   ",
                    "0x", "0xZZ", "1 +", "(1 + 2))", "1 + = 2", "-", "()", "(  )",
                    "1234567890 + * 2", "2 * (3 + 4", "((1)", "1 == = 2", "名字",
                    "aa", "1 ! 2", "5 > 4 > 3", "超时 + 1",
                ];
                var 源 = new StringBuilder(头);
                源.Append(帮手);
                var 期 = new List<string>();
                var 变量 = new Dictionary<string, int>(StringComparer.Ordinal);
                void 记试算(string 算式)
                {
                    源.Append($"试算(\"{算式}\");\n");
                    var 结果 = 参考表达式求值(解转义(算式), 变量);
                    期.Add(结果.成功 ? "1" : "0");
                    期.Add(结果.成功 ? 结果.值.ToString() : "0");
                    期.Add(结果.成功 ? "0" : 结果.出错格.ToString());
                    期.Add(结果.成功 ? "" : 参考表达式错因文(结果.错因));
                }
                void 记设变量(string 名, int 值)
                {
                    源.Append($"输出行(表达式.设变量(\"{名}\", {值}));\n");
                    bool 好 = 变量.ContainsKey(名) || 变量.Count < 16;
                    if (好) 变量[名] = 值;
                    期.Add(好 ? "1" : "0");
                }
                void 记有变量(string 名)
                {
                    源.Append($"输出行(表达式.有变量(\"{名}\"));\n");
                    期.Add(变量.ContainsKey(名) ? "1" : "0");
                }
                void 记清变量()
                {
                    源.Append("表达式.清变量();\n");
                    变量.Clear();
                }

                foreach (string 算式 in 算式表) 记试算(算式);

                // 变量表:设 / 改 / 用 / 清,外加"满了之后还能改、加不进去"那一档
                记清变量();
                记有变量("计数");
                记试算("计数 + 1");                       // 没设过 ⇒ 错因 6
                记设变量("计数", 8);
                记试算("计数 * 2");
                记设变量("计数", 9);                      // 有就改
                记试算("计数 * 2");
                记设变量("加数", -3);
                记试算("计数 + 加数 * 2");
                记试算("加数 % 2");
                记试算("计数 != 0 && 100 / 计数 > 5");
                记试算("加数 != 0 || 1 / 0");
                记试算("计数 > 100 || 加数 < 0");
                记设变量("超时", 30);
                记试算("超时 / 4");
                记试算("超时2");                        // 汉字名字也认,但这个名字没设过
                记有变量("超时");
                记清变量();
                记有变量("超时");
                for (int i = 0; i < 16; i++) 记设变量($"v{i}", i);
                记有变量("v15");
                记设变量("v16", 16);                    // 满 16 个 ⇒ 加不进去(不悄悄丢掉已有)
                记试算("v15 + 1");
                记设变量("v0", 99);                     // 满了也能改已有那个
                记试算("v0");
                记试算("v16");                          // 上一句没加进去 ⇒ 这个名字还是没设过
                记清变量();
                记有变量("v0");
                源.Append("停止\n");

                string 期望 = string.Join("\n", 期);
                string 实得 = 跑(源.ToString());
                记录($"表达式库 求值:优先级 / 括号 / 一元 / 十六进制 / 空白 / 比较 / 逻辑 / 短路 / " +
                    $"{算式表.Length} 条算式 + 变量表 24 步(设 / 改 / 清 / 满),与 C# 参考实现逐行比",
                    期望 == 实得, 表达式对拍说明(期望, 实得));
            }
            catch (Exception ex) { 记录异常("表达式库 求值", ex); }

            // ---- ② "说明"整句逐字对拍(位置 + 原因 + 那一格 + 共几格) ----
            try
            {
                string[] 错算式 =
                [
                    "1 + * 2", "(1 + 2", "10 / 0", "1 2", "1 | 2", "0x", "1 + ",
                    "", "   ", "超时2", "1234567890 + * 2", "2 * (3 + 4", "((1)",
                    "()", "-", "1 + = 2", "1 << 2", "名字", "  1 +  ",
                ];
                var 源 = new StringBuilder(头);
                源.Append(帮手);
                源.Append("表达式.设变量(\"超时\", 30);\n");     // 下面"超时 / 4"那一条要用(不打印,免得算进对拍的行)
                var 期 = new List<string>();
                var 变量 = new Dictionary<string, int>(StringComparer.Ordinal) { ["超时"] = 30 };
                foreach (string 算式 in 错算式)
                {
                    源.Append($"说话(\"{算式}\");\n");
                    期.Add(参考表达式说明(解转义(算式), 变量));
                }
                源.Append("说话(\"1 + 2 * 3\");\n");        // 没错的那一条:说明要说"算式没问题"
                期.Add(参考表达式说明("1 + 2 * 3", 变量));
                源.Append("说话(\"超时 / 4\");\n");
                期.Add(参考表达式说明("超时 / 4", 变量));
                源.Append("停止\n");
                string 期望 = string.Join("\n", 期);
                string 实得 = 跑(源.ToString());
                记录($"表达式库 说明:19 条错算式 + 2 条对的,整句逐字比(位置是 1 起、按字符;" +
                    $"汉字算式才算得对 \"那一格\" 与 \"共几格\")",
                    期望 == 实得, 表达式对拍说明(期望, 实得));
            }
            catch (Exception ex) { 记录异常("表达式库 说明", ex); }

            // ---- ③ 短路口径三条:右边"只解析不算数" ⇒ 成功、值对、出错格是 0 ----
            try
            {
                var 源 = new StringBuilder(头);
                源.Append("整数 果 = 0;\n");
                源.Append("输出行(表达式.求值(\"0 && 1 / 0\", 果));\n输出行(果);\n输出行(表达式.取出错格());\n");
                源.Append("输出行(表达式.求值(\"1 || 1 / 0\", 果));\n输出行(果);\n输出行(表达式.取出错格());\n");
                源.Append("输出行(表达式.求值(\"0 && 名字没设过\", 果));\n输出行(果);\n输出行(表达式.取出错格());\n");
                源.Append("输出行(表达式.求值(\"1 || 名字没设过\", 果));\n输出行(果);\n输出行(表达式.取出错格());\n");
                源.Append("输出行(表达式.求值(\"1 && 1 / 0\", 果));\n输出行(表达式.取出错格());\n");
                源.Append("输出行(表达式.求值(\"0 || 名字没设过\", 果));\n输出行(表达式.取出错格());\n");
                源.Append("停止\n");
                string 期望 = string.Join("\n",
                    "1", "0", "0",
                    "1", "1", "0",
                    "1", "0", "0",
                    "1", "1", "0",
                    // 没短路成的那两条:必须真报错 —— `1 && 1 / 0` 指除号(第 8 格),`0 || …` 指名字(第 6 格)
                    "0", "8",
                    "0", "6");
                string 实得 = 跑(源.ToString());
                记录("表达式库 短路:0 && 1/0 / 1 || 1/0 / 0 && 名字没设过 / 1 || 名字没设过 四条都成功且值为 0 或 1、" +
                    "取出错格 是 0;没短路的 `1 && 1/0`(第 8 格)与 `0 || 名字没设过`(第 6 格)照旧报错",
                    期望 == 实得, 表达式对拍说明(期望, 实得));
            }
            catch (Exception ex) { 记录异常("表达式库 短路", ex); }

            // ---- ④ 接线哨兵:库在位、五段头注释齐、7 个公开方法、零依赖 ----
            try
            {
                string 源文 = File.ReadAllText(库路径);
                记录("表达式库 接线哨兵:库在 Sample\\库 下、五段头注释齐、7 个公开方法、" +
                    "零依赖(源码里没有一条 `包含`)",
                    源文.Contains("模块 表达式", StringComparison.Ordinal)
                    && 源文.Contains("提供的函数(7 个)", StringComparison.Ordinal)
                    && 五段齐(源文)
                    && 公开方法数(源文) == 7
                    && !System.Text.RegularExpressions.Regex.IsMatch(源文, @"(?m)^包含\s")
                    // 命名规则:库文件里不许出现禁用名(`转文字` 该写 `转字符串`;本库零依赖 ⇒ 自己写了 `数文`)
                    && !源文.Contains("转文字", StringComparison.Ordinal)
                    && !源文.Contains("文字转", StringComparison.Ordinal),
                    $"公开方法 {公开方法数(源文)} / 包含行 {System.Text.RegularExpressions.Regex.Matches(源文, @"(?m)^包含\s").Count}");

                static bool 五段齐(string 源) =>
                    源.Contains("口径(2026-", StringComparison.Ordinal)
                    && 源.Contains("英文对照", StringComparison.Ordinal)
                    && 源.Contains("复杂度:", StringComparison.Ordinal)
                    && 源.Contains("边界(如实登记", StringComparison.Ordinal);
                static int 公开方法数(string 源) =>
                    System.Text.RegularExpressions.Regex.Matches(源, @"(?m)^方法 ").Count;
            }
            catch (Exception ex) { 记录异常("表达式库 接线哨兵", ex); }
        }

        // ============================================================
        // C# 参考实现(与 `Sample\库\表达式.hb` 同一份语法与错因表,另写一遍)
        //   —— 只当"另一条独立算出来的基准",不进产品代码
        // ============================================================

        /// <summary>对拍用:算式里的 `\n` / `\t` 转义(HB 的字符串字面量在编译期就把它们变成真字符了)</summary>
        private static string 解转义(string 串) => 串.Replace("\\n", "\n").Replace("\\t", "\t");

        /// <summary>一条算式的求值结果(对拍用)</summary>
        private sealed class 表达式结果
        {
            public bool 成功;
            public int 值;
            public int 错因;
            public int 出错格;
        }

        /// <summary>递归下降的参考解析态(`表达式.hb` 那一份的逐条对照)</summary>
        private sealed class 表达式参考态
        {
            public string 文本 = "";
            public Dictionary<string, int> 变量 = new(StringComparer.Ordinal);
            public int 位置, 错因, 出错格, 静默, 算符格;

            public void 置错(int 因, int 格)
            {
                if (错因 == 0) { 错因 = 因; 出错格 = 格; }
            }
            public int 看码() => 位置 >= 文本.Length ? -1 : 文本[位置];
            public int 看二() => 位置 + 1 >= 文本.Length ? -1 : 文本[位置 + 1];
            public bool 吃码(int 码)
            {
                if (看码() != 码) return false;
                位置++;
                return true;
            }
            public bool 吃两码(int 左, int 右)
            {
                if (看码() != 左 || 看二() != 右) return false;
                位置 += 2;
                return true;
            }
            public void 跳空白()
            {
                while (位置 < 文本.Length)
                {
                    int 码 = 文本[位置];
                    if (码 != 32 && 码 != 9 && 码 != 13 && 码 != 10) break;
                    位置++;
                }
            }
            public static bool 是名首(int 码) =>
                (码 >= 65 && 码 <= 90) || (码 >= 97 && 码 <= 122) || 码 == 95 || 码 >= 128;
            public static bool 是名码(int 码) => (码 >= 48 && 码 <= 57) || 是名首(码);

            public int 化真假(bool 条件) => 条件 ? 1 : 0;

            public int 十六值(int 码)
            {
                if (码 >= 48 && 码 <= 57) return 码 - 48;
                if (码 >= 65 && 码 <= 70) return 码 - 55;
                if (码 >= 97 && 码 <= 102) return 码 - 87;
                return -1;
            }
            public bool 是十六开头()
            {
                if (看码() != 48) return false;
                int 二 = 看二();
                return 二 == 120 || 二 == 88;
            }
            public int 读十进()
            {
                long 值 = 0;
                while (位置 < 文本.Length)
                {
                    int 码 = 文本[位置];
                    if (码 < 48 || 码 > 57) break;
                    值 = 值 * 10 + (码 - 48);
                    位置++;
                }
                return unchecked((int)值);
            }
            public int 读十六()
            {
                int 起 = 位置;
                位置 += 2;
                long 值 = 0;
                int 位 = 0;
                while (位置 < 文本.Length)
                {
                    int 数 = 十六值(文本[位置]);
                    if (数 < 0) break;
                    值 = 值 * 16 + 数;
                    位置++;
                    位++;
                }
                if (位 == 0) 置错(7, 起 + 1);
                return unchecked((int)值);
            }
            public int 读数字() => 是十六开头() ? 读十六() : 读十进();

            public int 读名字()
            {
                int 起 = 位置;
                while (位置 < 文本.Length && 是名码(文本[位置])) 位置++;
                string 名 = 文本[起..位置];
                if (变量.TryGetValue(名, out int 值)) return 值;
                if (静默 > 0) return 0;
                置错(6, 起 + 1);
                return 0;
            }

            public int 算基本()
            {
                if (错因 != 0) return 0;
                跳空白();
                int 码 = 看码();
                if (码 < 0) { 置错(2, 位置 + 1); return 0; }
                if (码 == 40) return 算括号();
                if (码 >= 48 && 码 <= 57) return 读数字();
                if (是名首(码)) return 读名字();
                置错(2, 位置 + 1);
                return 0;
            }
            public int 算括号()
            {
                int 开 = 位置 + 1;
                吃码(40);
                int 值 = 算或();
                if (错因 != 0) return 0;
                跳空白();
                if (!吃码(41)) 置错(3, 开);
                return 值;
            }
            public int 算一元()
            {
                if (错因 != 0) return 0;
                跳空白();
                if (吃码(45))
                {
                    int 值 = 算一元();
                    if (错因 != 0) return 0;
                    return unchecked(0 - 值);
                }
                if (吃码(43)) return 算一元();
                if (吃码(33))
                {
                    int 反 = 算一元();
                    if (错因 != 0) return 0;
                    return 反 == 0 ? 1 : 0;
                }
                return 算基本();
            }
            public int 合积(int 码, int 左, int 右)
            {
                if (码 == 42) return unchecked(左 * 右);
                if (右 == 0)
                {
                    if (静默 == 0) 置错(4, 算符格);
                    return 0;
                }
                if (码 == 47) return unchecked(左 / 右);
                return unchecked(左 % 右);
            }
            public int 算积()
            {
                int 左 = 算一元();
                while (错因 == 0)
                {
                    跳空白();
                    int 码 = 看码();
                    if (码 != 42 && 码 != 47 && 码 != 37) break;
                    算符格 = 位置 + 1;
                    位置++;
                    int 右 = 算一元();
                    if (错因 != 0) break;
                    左 = 合积(码, 左, 右);
                }
                return 左;
            }
            public int 算和()
            {
                int 左 = 算积();
                while (错因 == 0)
                {
                    跳空白();
                    int 码 = 看码();
                    if (码 != 43 && 码 != 45) break;
                    位置++;
                    int 右 = 算积();
                    if (错因 != 0) break;
                    左 = 码 == 43 ? unchecked(左 + 右) : unchecked(左 - 右);
                }
                return 左;
            }
            public int 取记号()
            {
                if (吃两码(61, 61)) return 1;
                if (吃两码(33, 61)) return 2;
                if (吃两码(60, 61)) return 3;
                if (吃两码(62, 61)) return 4;
                if (看二() == 60) return 0;
                if (看二() == 62) return 0;
                if (吃码(60)) return 5;
                if (吃码(62)) return 6;
                return 0;
            }
            public int 比较值(int 记号, int 左, int 右) => 记号 switch
            {
                1 => 化真假(左 == 右),
                2 => 化真假(左 != 右),
                3 => 化真假(左 <= 右),
                4 => 化真假(左 >= 右),
                5 => 化真假(左 < 右),
                _ => 化真假(左 > 右),
            };
            public int 算比()
            {
                int 左 = 算和();
                while (错因 == 0)
                {
                    跳空白();
                    int 记号 = 取记号();
                    if (记号 == 0) break;
                    int 右 = 算和();
                    if (错因 != 0) break;
                    左 = 比较值(记号, 左, 右);
                }
                return 左;
            }
            public int 算与()
            {
                int 左 = 算比();
                while (错因 == 0)
                {
                    跳空白();
                    if (!吃两码(38, 38)) break;
                    if (左 == 0) 静默++;
                    int 右 = 算比();
                    if (左 == 0) 静默--;
                    if (错因 != 0) break;
                    if (左 != 0) 左 = 化真假(右 != 0);
                }
                return 左;
            }
            public int 算或()
            {
                int 左 = 算与();
                while (错因 == 0)
                {
                    跳空白();
                    if (!吃两码(124, 124)) break;
                    if (左 != 0) 静默++;
                    int 右 = 算与();
                    if (左 != 0) 静默--;
                    if (错因 != 0) break;
                    if (左 == 0) 左 = 化真假(右 != 0);
                }
                return 左;
            }
        }

        /// <summary>`表达式.求值` 的参考实现</summary>
        private static 表达式结果 参考表达式求值(string 串, Dictionary<string, int> 变量)
        {
            var 态 = new 表达式参考态 { 文本 = 串, 变量 = 变量 };
            态.跳空白();
            if (态.位置 >= 串.Length) return new 表达式结果 { 错因 = 1, 出错格 = 1 };
            int 值 = 态.算或();
            if (态.错因 != 0) return new 表达式结果 { 错因 = 态.错因, 出错格 = 态.出错格 };
            态.跳空白();
            if (态.位置 < 串.Length)
            {
                int 余 = 串[态.位置];
                int 因 = (余 == 124 || 余 == 94 || 余 == 38 || 余 == 60 || 余 == 62) ? 8 : 5;
                态.置错(因, 态.位置 + 1);
                return new 表达式结果 { 错因 = 态.错因, 出错格 = 态.出错格 };
            }
            return new 表达式结果 { 成功 = true, 值 = 值 };
        }

        /// <summary>`表达式.取错因` / `说明` 里那句原因文的参考实现(错因表两边必须一起改)</summary>
        private static string 参考表达式错因文(int 因) => 因 switch
        {
            2 => "这一格要一个数、一个名字或者 `(`",
            3 => "这一格开的 `(` 没有关上",
            4 => "这一格的除数是 0",
            5 => "算式到这里就完了,后面还有多余的字符",
            6 => "这一格的名字没有设过值(先用 设变量 给它一个值)",
            7 => "这一格的 `0x` 后面没有十六进制数字",
            8 => "本库不做位运算(`| ^ & << >>` 请自己先算好再写进来)",
            _ => "认不出的算式",
        };

        /// <summary>`表达式.说明` 的参考实现(位置 1 起、按字符)</summary>
        private static string 参考表达式说明(string 串, Dictionary<string, int> 变量)
        {
            var 结果 = 参考表达式求值(串, 变量);
            if (结果.成功) return "算式没问题";
            if (结果.错因 == 1) return "算式是空的(一个字符都没有)";
            int 总 = 串.Length;
            var 果 = new StringBuilder();
            果.Append("第 ").Append(结果.出错格).Append(" 格:").Append(参考表达式错因文(结果.错因));
            if (结果.出错格 <= 总) 果.Append(";那一格是 `").Append(串[结果.出错格 - 1]).Append('`');
            果.Append(";算式共 ").Append(总).Append(" 格");
            return 果.ToString();
        }
    }
}
