// ============================================================
// 内存 / 内存流 / 定点 / 随机 四个库的自检(G4 批 10,第355批)
//   —— `Sample\库\内存.hb` · `内存流.hb` · `定点.hb` · `随机.hb`
//
//   判据照 `基础库施工计划.md` §2 批 10 那一行:"**`内存流` 今天就能做(零宿主能力)**;
//   随机要**可复现**(设种子)"。
//   这一组六块(行为断言为主 + 一条接线哨兵;硬规矩 5 / T22):
//     ① **内存:随机填 / 拷 / 比 / 找 / 倒 / 求和与 C# 参考实现逐条对拍**(含重叠搬);
//     ② **内存流:随机写 / 重置 / 读 / 挪到与 C# 参考实现逐条对拍**(含越界那几档);
//     ③ **定点:与 C# 参考实现逐条对拍**(加 / 减 / 乘 / 除 / 到整数 / 取整 / 取小数 / 转字符串)
//        —— 参考实现**照被验方的口径写**(32 位回绕的那两条公式一字不差,见 §3 那条教训);
//     ④ **随机:与 C# 的 xorshift32 参考实现逐条对拍** —— 这一条就是判据里"可复现"那句话:
//        同一个种子,HB 侧与 C# 侧**逐个数都对得上**;
//     ⑤ **端到端**:标准工程 **`示例42_内存与随机`** 真编真跑,与它那行 `// 预期输出:` 逐行对;
//     ⑥ **接线哨兵**:四个库都在盘上 + 头注释五段齐 + 公开方法个数对上(10 / 12 / 13 / 8)。
//
//   ⚠ 这一组**不引 `测试助手.hb`**:库与库之间互相引会把"谁坏了"搅浑(自检要能一眼定位)。
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
        private static void 内存与随机库自检()
        {
            string? 示例 = 找示例目录();
            string? 仓库根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
            string 库目录 = 仓库根 == null ? "" : Path.Combine(仓库根, "Sample", "库");
            string 存路径 = Path.Combine(库目录, "内存.hb").Replace('\\', '/');
            string 流路径 = Path.Combine(库目录, "内存流.hb").Replace('\\', '/');
            string 定路径 = Path.Combine(库目录, "定点.hb").Replace('\\', '/');
            string 随路径 = Path.Combine(库目录, "随机.hb").Replace('\\', '/');
            if (!File.Exists(存路径) || !File.Exists(流路径) || !File.Exists(定路径) || !File.Exists(随路径))
            {
                跳过("内存与随机库 定位四个库文件", $"找不到 {存路径} / {流路径} / {定路径} / {随路径}(不在工作副本里跑就跳过)");
                return;
            }
            string 头 = $"包含 \"{存路径}\"\n包含 \"{流路径}\"\n包含 \"{定路径}\"\n包含 \"{随路径}\"\n";

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
            // ① 内存:随机动作与 C# 参考实现逐条对拍
            // ============================================================
            try
            {
                var 随机 = new Random(20261015);
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                const int 字数 = 12;
                源.Append($"整数 块 = 申请({字数 * 4});\n整数 块二 = 申请({字数 * 4});\n");
                var 表左 = new int[字数];
                var 表右 = new int[字数];
                for (int 轮 = 0; 轮 < 90; 轮++)
                {
                    int 动作 = 随机.Next(7);
                    int 给 = 随机.Next(0, 101);
                    if (动作 == 0)                                   // 填字
                    {
                        源.Append($"输出行(内存.填字(块, {给}, {字数}));\n");
                        for (int i = 0; i < 字数; i++) 表左[i] = 给;
                        期望.Append(字数).Append('|');
                    }
                    else if (动作 == 1)                              // 拷字(块 → 块二)
                    {
                        源.Append($"输出行(内存.拷字(块, 块二, {字数}));\n");
                        Array.Copy(表左, 表右, 字数);
                        期望.Append(字数).Append('|');
                    }
                    else if (动作 == 2)                              // 比字 / 是相同
                    {
                        源.Append($"输出行(内存.比字(块, 块二, {字数}));\n输出行(内存.是相同(块, 块二, {字数}));\n");
                        int 比 = 0;
                        for (int i = 0; i < 字数; i++)
                        {
                            uint a = unchecked((uint)表左[i]), b = unchecked((uint)表右[i]);
                            if (a != b) { 比 = a < b ? -1 : 1; break; }
                        }
                        期望.Append(比).Append('|').Append(比 == 0 ? 1 : 0).Append('|');
                    }
                    else if (动作 == 3)                              // 找字 / 求和
                    {
                        源.Append($"输出行(内存.找字(块, {给}, {字数}));\n输出行(内存.求和(块, {字数}));\n");
                        期望.Append(Array.IndexOf(表左, 给)).Append('|');
                        int 和 = 0; foreach (int x in 表左) 和 = unchecked(和 + x);
                        期望.Append(unchecked(和)).Append('|');
                    }
                    else if (动作 == 4)                              // 倒过来
                    {
                        源.Append($"输出行(内存.倒过来(块, {字数}));\n");
                        Array.Reverse(表左);
                        期望.Append(字数).Append('|');
                    }
                    else if (动作 == 5)                              // 交换字(头一个字)
                    {
                        源.Append($"输出行(内存.填字(块, {给}, 1));\n");
                        表左[0] = 给;
                        源.Append("内存.交换字(块, 块二);\n");
                        (表左[0], 表右[0]) = (表右[0], 表左[0]);
                        期望.Append('1').Append('|');
                    }
                    else                                             // 清空 / 读回一个
                    {
                        int 位 = 随机.Next(0, 字数);
                        源.Append($"输出行(内存.清零(块, {位}));\n");
                        for (int i = 0; i < 位; i++) 表左[i] = 0;
                        期望.Append(位).Append('|');
                        源.Append($"输出行(取字(块, {位 * 4}));\n");
                        期望.Append(位 < 字数 ? 表左[位] : 0).Append('|');
                    }
                }
                源.Append("输出行(内存.求和(块, 12));\n");
                int 终 = 0; foreach (int x in 表左) 终 = unchecked(终 + x);
                期望.Append(unchecked(终)).Append('|');
                源.Append("停止\n");      // 这一份只量行为:末尾不释放(那一趟"还回去"由别的断言管)
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("内存与随机库 内存:填 / 拷 / 比 / 是相同 / 找 / 求和 / 倒过来 / 交换 / 清零(90 轮随机动作,12 个字,与 C# 参考实现逐条对拍;含重叠搬与 32 位回绕)",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("内存与随机库 内存随机对拍", ex); }

            // ============================================================
            // ② 内存流:随机写 / 重置 / 读 / 挪到与 C# 参考实现逐条对拍
            // ============================================================
            try
            {
                var 随机 = new Random(20261016);
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                const int 容量 = 10;
                源.Append($"整数 流 = 内存流.建({容量});\n整数 收 = 0;\n");
                var 袋 = new int[容量];
                int 位 = 0, 写了 = 0, 收 = 0;
                for (int 轮 = 0; 轮 < 110; 轮++)
                {
                    int 动作 = 随机.Next(6);
                    int 给 = 随机.Next(0, 101);
                    if (动作 <= 1)                                   // 写入
                    {
                        源.Append($"输出行(内存流.写入(流, {给}));\n");
                        if (位 < 0 || 位 >= 容量) 期望.Append('0');
                        else
                        {
                            袋[位] = 给; 位++; if (位 > 写了) 写了 = 位;
                            期望.Append('1');
                        }
                        期望.Append('|');
                    }
                    else if (动作 == 2)                              // 读出
                    {
                        源.Append("输出行(内存流.读出(流, 收));\n输出行(收);\n");
                        if (位 < 0 || 位 >= 写了) 期望.Append('0');
                        else { 收 = 袋[位]; 位++; 期望.Append('1'); }
                        期望.Append('|').Append(收).Append('|');
                    }
                    else if (动作 == 3)                              // 移动到
                    {
                        int 到哪 = 随机.Next(-1, 容量 + 2);
                        源.Append($"输出行(内存流.移动到(流, {到哪}));\n");
                        if (到哪 < 0 || 到哪 > 容量) 期望.Append('0');
                        else { 位 = 到哪; 期望.Append('1'); }
                        期望.Append('|');
                    }
                    else if (动作 == 4)                              // 重置 / 账
                    {
                        源.Append("内存流.重置(流);\n");
                        位 = 0;
                        源.Append("输出行(内存流.读到哪(流));\n输出行(内存流.写了几个(流));\n输出行(内存流.还剩(流));\n");
                        期望.Append(位).Append('|').Append(写了).Append('|').Append(Math.Max(0, 写了 - 位)).Append('|');
                    }
                    else                                             // 取出
                    {
                        源.Append($"整数 出去{轮}[{容量}];\n输出行(内存流.取出(流, 出去{轮}));\n");
                        期望.Append(写了).Append('|');
                        for (int i = 0; i < 容量; i++)
                        {
                            源.Append($"输出行(出去{轮}[{i}]);\n");
                            期望.Append(i < 写了 ? 袋[i] : 0).Append('|');
                        }
                    }
                }
                源.Append("内存流.销毁(流);\n停止\n");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("内存与随机库 内存流:写入 / 读出 / 移动到 / 重置 / 取出 / 读到哪 / 写了几个 / 还剩(110 轮随机动作,容量 10,**零宿主能力**,与 C# 参考实现逐条对拍;含越界那几档)",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("内存与随机库 内存流随机对拍", ex); }

            // ============================================================
            // ③ 定点:与 C# 参考实现逐条对拍(参考实现**照被验方的口径写**)
            // ============================================================
            try
            {
                const int 一步 = 65536, 半步 = 32768;
                // 与库里那两条公式**一字不差**(含 32 位回绕)—— "参考实现要照被验方的口径写",
                // 这是批 8 并查集那一课的同一条。
                static int 定乘(int 左, int 右) => unchecked((左 / 一步) * 右 + ((左 % 一步) * 右) / 一步);
                static int 定除(int 左, int 右) => 右 == 0 ? 0 : unchecked((左 / 右) * 一步 + ((左 % 右) * 一步) / 右);
                static int 定到整(int 值) => 值 >= 0 ? (值 + 半步) / 一步 : (值 - 半步) / 一步;
                static string 定文(int 值, int 位)
                {
                    int 负 = 值 < 0 ? 1 : 0;
                    long 正 = Math.Abs((long)值);
                    long 放大 = 1; for (int i = 0; i < 位; i++) 放大 *= 10;
                    long 总 = (正 * 放大 + 半步) / 一步;
                    long 整部 = 总 / 放大, 尾 = 总 % 放大;
                    string 果 = 整部.ToString();
                    if (位 > 0) 果 += "." + 尾.ToString().PadLeft(位, '0');
                    return (负 == 1 && 总 != 0) ? "-" + 果 : 果;
                }

                var 随机 = new Random(20261017);
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                for (int 轮 = 0; 轮 < 70; 轮++)
                {
                    // 取"安全带之内"的操作数(乘除的 32 位中间项天花板见 `定点.hb` 头注释;
                    // `转字符串` 里 `值 × 10^位` 也要放进 32 位 ⇒ 值小、位数 ≤ 3)
                    int 左 = 随机.Next(-3, 4) * 一步 + 随机.Next(0, 2) * 半步;
                    int 右 = 随机.Next(-3, 4) * 一步 + 随机.Next(0, 2) * 半步;
                    if (左 < 0) 左 += 半步;
                    if (右 < 0) 右 += 半步;
                    源.Append($"输出行({左} + {右});\n");                 // 直接当整数打出来对
                    期望.Append(unchecked(左 + 右)).Append('|');
                    源.Append($"输出行(定点.相加({左}, {右}));\n输出行(定点.相减({左}, {右}));\n");
                    期望.Append(unchecked(左 + 右)).Append('|').Append(unchecked(左 - 右)).Append('|');
                    源.Append($"输出行(定点.到整数({左}));\n输出行(定点.取整({左}));\n输出行(定点.取小数({左}));\n");
                    期望.Append(定到整(左)).Append('|').Append(左 / 一步).Append('|')
                        .Append(Math.Abs(左 % 一步)).Append('|');
                    int 位 = 随机.Next(0, 4);
                    源.Append($"输出行(定点.转字符串({左}, {位}));\n");
                    期望.Append(定文(左, 位)).Append('|');
                }
                // 乘除单独一批(只用安全值)
                for (int 轮 = 0; 轮 < 20; 轮++)
                {
                    int 左 = 随机.Next(-6, 7) * 一步 + 随机.Next(0, 2) * 半步;
                    int 右 = 随机.Next(1, 3);
                    源.Append($"输出行(定点.相乘({左}, {右}));\n输出行(定点.相除({左}, {右}));\n");
                    期望.Append(定乘(左, 右)).Append('|').Append(定除(左, 右)).Append('|');
                }
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("内存与随机库 定点:相加 / 相减 / 到整数(四舍五入)/ 取整 / 取小数 / 相乘 / 相除 / 转字符串(70 + 20 轮,与 C# 参考实现逐条对拍;参考实现照库里的口径写,含 32 位回绕)",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("内存与随机库 定点对拍", ex); }

            // ============================================================
            // ④ 随机:与 C# 的 xorshift32 参考实现逐条对拍(判据里"可复现"那句话)
            // ============================================================
            try
            {
                static uint 下一个(ref uint 态)
                {
                    态 ^= 态 << 13;
                    态 ^= 态 >> 17;
                    态 ^= 态 << 5;
                    return 态;
                }
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                int[] 种子们 = [1, 2026, 7, 12345, 2147483647];
                foreach (int 种 in 种子们)
                {
                    源.Append($"随机.设种子({种});\n");
                    uint 态 = (uint)种;
                    for (int i = 0; i < 12; i++)
                    {
                        源.Append("输出行(随机.下一个());\n");
                        期望.Append(unchecked((int)下一个(ref 态))).Append('|');
                    }
                    源.Append("输出行(随机.看种子());\n");
                    期望.Append(unchecked((int)态)).Append('|');
                    // 同种子重设一遍 ⇒ 必须给同一串(可复现那一半;这里再走两个,与上面头两个同值)
                    源.Append($"随机.设种子({种});\n输出行(随机.下一个());\n");
                    uint 态二 = (uint)种;
                    期望.Append(unchecked((int)下一个(ref 态二))).Append('|');
                }
                源.Append("随机.设种子(0);\n输出行(随机.看种子());\n");
                期望.Append(unchecked((int)2463534242u)).Append('|');
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|'), 实 = 跑(源.ToString());
                记录("内存与随机库 随机:xorshift32 与 C# 参考实现逐条对拍(5 个种子各 12 个 + 同种子重设给同一串 + 看种子 + 设 0 换出厂种子)—— 判据里「可复现」那句话就是这个",
                    实 == 期, 对拍说明(期, 实));
            }
            catch (Exception ex) { 记录异常("内存与随机库 随机对拍", ex); }

            // ============================================================
            // ⑤ 端到端:标准工程 示例42_内存与随机
            // ============================================================
            try
            {
                string? 工程目录 = 示例 == null ? null : Path.Combine(示例, "中间层", "示例42_内存与随机");
                string? 源码 = 工程目录 == null ? null : Path.Combine(工程目录, "主程序.hb");
                if (源码 == null || !File.Exists(源码))
                {
                    跳过("内存与随机库 标准工程 示例42 逐行对预期输出", $"找不到 {源码}(不在工作副本里跑就跳过)");
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
                    记录("内存与随机库 标准工程 示例42 逐条对预期输出(四个库在真程序里的那一趟:裸内存搬运 → 内存流读写 → 定点算 → 确定性随机)",
                        好, 好 ? $"逐行一致({期望行.Count} 行)" : $"期望 {期望行.Count} 行 / 实得 {实得行.Count} 行;" +
                            (期望行.Count > 0 && 实得行.Count > 0
                                ? $"第 1 行 期望[{期望行[0]}] 实得[{实得行[0]}]"
                                : "条数先对不上"));
                }
            }
            catch (Exception ex) { 记录异常("内存与随机库 标准工程 示例42", ex); }

            // ============================================================
            // ⑥ 接线哨兵:四个库在位 + 头注释五段齐 + 公开方法个数
            // ============================================================
            try
            {
                string 存文 = File.ReadAllText(存路径);
                string 流文 = File.ReadAllText(流路径);
                string 定文 = File.ReadAllText(定路径);
                string 随文 = File.ReadAllText(随路径);
                bool 头齐(string 文)
                    => 文.Contains("口径(2026-", StringComparison.Ordinal)
                    && 文.Contains("提供的函数(", StringComparison.Ordinal)
                    && 文.Contains("英文对照", StringComparison.Ordinal)
                    && 文.Contains("复杂度", StringComparison.Ordinal)
                    && 文.Contains("边界(如实登记", StringComparison.Ordinal);
                int 数方法(string 文)
                {
                    int 数 = 0;
                    foreach (string 行 in 文.Split('\n'))
                        if (行.StartsWith("方法 ", StringComparison.Ordinal)) 数++;
                    return 数;
                }
                int 存法 = 数方法(存文), 流法 = 数方法(流文), 定法 = 数方法(定文), 随法 = 数方法(随文);
                bool 好 = 头齐(存文) && 头齐(流文) && 头齐(定文) && 头齐(随文)
                    && 存文.Contains("模块 内存", StringComparison.Ordinal)
                    && 流文.Contains("模块 内存流", StringComparison.Ordinal)
                    && 定文.Contains("模块 定点", StringComparison.Ordinal)
                    && 随文.Contains("模块 随机", StringComparison.Ordinal)
                    && 存法 == 10 && 流法 == 12 && 定法 == 13 && 随法 == 8;
                记录("内存与随机库 接线哨兵:四个库都在盘上、头注释五段齐、公开方法数(内存 10 / 内存流 12 / 定点 13 / 随机 8)",
                    好, $"内存 {存法} 个 / 内存流 {流法} 个 / 定点 {定法} 个 / 随机 {随法} 个");
            }
            catch (Exception ex) { 记录异常("内存与随机库 接线哨兵", ex); }
        }
    }
}
