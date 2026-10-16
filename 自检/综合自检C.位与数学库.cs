// ============================================================
// 位与数学两个库的自检(G4 批 1,第344批)—— `Sample\库\位.hb` / `Sample\库\数学.hb`
//
//   判据照 `基础库施工计划.md` §2 批 1 那一行:"位运算 / 整数开方幂 / 最大公约数逐条真跑;**与 .NET 对拍**"。
//   这一组就是那条对拍 —— 一张输入表喂给两个库**真编真跑**,期望值由 **.NET 现场算**
//   (`System.Numerics.BitOperations` / `BinaryPrimitives.ReverseEndianness` / 整数牛顿迭代 / 欧几里得),
//   逐行比。**不手抄一串数当基准**:手抄的数只证明"抄的时候是对的",证明不了"库是对的"。
//
//   两个库都是**纯 HB、零依赖**(不动 ISA / 不动关键字 / 不动语言面);这一组只真编真跑它们的公开函数,
//   不碰宿主能力、不碰文件。
// ============================================================

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        private static void 位与数学库自检()
        {
            string? 示例 = 找示例目录();
            string? 仓库根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
            string 库目录 = 仓库根 == null ? "" : Path.Combine(仓库根, "Sample", "库");
            string 位路径 = Path.Combine(库目录, "位.hb").Replace('\\', '/');
            string 数学路径 = Path.Combine(库目录, "数学.hb").Replace('\\', '/');
            if (!File.Exists(位路径) || !File.Exists(数学路径))
            {
                跳过("位与数学库 定位 Sample\\库\\位.hb 与 数学.hb", $"找不到 {位路径} / {数学路径}(不在工作副本里跑就跳过)");
                return;
            }
            string 头 = $"包含 \"{位路径}\"\n包含 \"{数学路径}\"\n";

            // 真编真跑一趟(输出按 UTF-8 收、行尾换成 `|`,与 数据表库 / 目录库 那两组同一套跑法)
            static string 跑(string 源)
            {
                var 编译 = new 汇编器C().汇编多文件带调试(
                    new List<(string, string)> { ("主程序.hb", 源) }, true,
                    中间层C.默认堆大小, null, 中间层C.默认池大小,
                    虚拟机C.默认内存大小, 32 * 1024);
                var vm = new 虚拟机C();
                vm.设内存大小(虚拟机C.默认内存大小);
                var 缓冲 = new StringBuilder();
                var 解码 = new 文字输出C(文本 => 缓冲.Append(文本));
                vm.输出目标 = 解码.写;
                vm.运行(编译);
                解码.收尾();
                string 报错 = 编译.查运行期报错(vm.内存) is { } 说明 ? $"⚠{说明}" : "";
                return 缓冲.ToString().Trim().Replace("\n", "|") + 报错;
            }

            // 对拍说明:哪一条对不上,一眼看得见(红了要能定位,不靠"再跑一遍猜")
            static string 对拍说明(string 期望, string 实得)
            {
                if (期望 == 实得) return "一字不差";
                string[] 期望段 = 期望.Split('|'), 实得段 = 实得.Split('|');
                int 少 = Math.Min(期望段.Length, 实得段.Length);
                for (int i = 0; i < 少; i++)
                    if (期望段[i] != 实得段[i])
                        return $"第 {i + 1} 个输出对不上:期望 [{期望段[i]}] / 实得 [{实得段[i]}](期望 {期望段.Length} 条 / 实得 {实得段.Length} 条)";
                return $"条数对不上:期望 {期望段.Length} 条 / 实得 {实得段.Length} 条;实得尾巴 [{实得[Math.Max(0, 实得.Length - 60)..]}]";
            }

            // ---- 参考实现:期望值一律用 .NET 算(这就是"与 .NET 对拍"那一半) ----
            static bool 位越界(int 位) => 位 is < 0 or > 31;
            static uint 参考取位(uint 值, int 位) => 位越界(位) ? 0u : (值 >> 位) & 1u;
            static uint 参考置位(uint 值, int 位) => 位越界(位) ? 值 : 值 | (1u << 位);
            static uint 参考清位(uint 值, int 位) => 位越界(位) ? 值 : 值 & ~(1u << 位);
            static uint 参考翻位(uint 值, int 位) => 位越界(位) ? 值 : 值 ^ (1u << 位);
            static uint 参考循环左移(uint 值, int 位)
            {
                int 挪 = ((位 % 32) + 32) % 32;
                return 挪 == 0 ? 值 : (值 << 挪) | (值 >> (32 - 挪));
            }
            static uint 参考掩码(int 位) => 位 <= 0 ? 0u : 位 >= 32 ? 0xFFFFFFFFu : (uint)((1ul << 位) - 1);
            // `-2147483648` 不在源里直接写成字面量(它超出 `整数` 的正数范围,写法上不干净)—— 用一条减法拼出来
            static string 字面(int 数) => 数 == int.MinValue ? "(0 - 2147483647 - 1)" : 数.ToString();

            // ---- ① 一位操作:6 个值 × 8 个位数 = 48 个点,取/置/清/翻各一行 ----
            uint[] 值们 = [0u, 1u, 0x80000000u, 0xFFFFFFFFu, 0x12345678u, 0xF0F0F0F0u];
            int[] 位们 = [-1, 0, 3, 7, 8, 15, 31, 32];
            var 源一 = new StringBuilder(头);
            var 期望一 = new StringBuilder();
            源一.Append("无符号整数 v = 0;\n无符号整数 r = 0;\n");     // 只声明一次:同一个名字不许重复声明
            foreach (uint 值 in 值们)
                foreach (int 位 in 位们)
                {
                    源一.Append($"v = {值};\n");
                    源一.Append($"输出行(位.取位(v, {位}));\n");
                    源一.Append($"r = 位.置位(v, {位});\n输出行(r);\n");
                    源一.Append($"r = 位.清位(v, {位});\n输出行(r);\n");
                    源一.Append($"r = 位.翻位(v, {位});\n输出行(r);\n");
                    foreach (uint 果 in new[] { 参考取位(值, 位), 参考置位(值, 位), 参考清位(值, 位), 参考翻位(值, 位) })
                        期望一.Append(果).Append('|');
                }
            源一.Append("停止\n");
            string 期一 = 期望一.ToString().TrimEnd('|'), 实一 = 跑(源一.ToString());
            记录("位与数学库 位:取位 / 置位 / 清位 / 翻位(6 个值 × 8 个位数 = 48 个点,与 .NET 逐点对拍;越界那一档也含在内)",
                实一 == 期一, 对拍说明(期一, 实一));

            // ---- ② 数一遍 / 字节交换 / 半字拆拼 ----
            var 源二 = new StringBuilder(头);
            var 期望二 = new StringBuilder();
            源二.Append("无符号整数 v = 0;\n无符号整数 r = 0;\n");
            foreach (uint 值 in 值们.Append(0x00010000u))
            {
                源二.Append($"v = {值};\n");
                源二.Append("输出行(位.数一位(v));\n输出行(位.前导零(v));\n输出行(位.尾随零(v));\n");
                源二.Append("r = 位.字节交换(v);\n输出行(r);\n");
                源二.Append("r = 位.取高半字(v);\n输出行(r);\n");
                源二.Append("r = 位.取低半字(v);\n输出行(r);\n");
                源二.Append("r = 位.拼半字(位.取高半字(v), 位.取低半字(v));\n输出行(r);\n");
                foreach (uint 果 in new[]
                {
                    (uint)BitOperations.PopCount(值), (uint)BitOperations.LeadingZeroCount(值),
                    (uint)BitOperations.TrailingZeroCount(值), BinaryPrimitives.ReverseEndianness(值),
                    值 >> 16, 值 & 0xFFFFu,
                })
                    期望二.Append(果).Append('|');
                期望二.Append(值).Append('|');       // 拼半字(取高, 取低) 原样拼回
            }
            源二.Append("停止\n");
            string 期二 = 期望二.ToString().TrimEnd('|'), 实二 = 跑(源二.ToString());
            记录("位与数学库 位:数一位 / 前导零 / 尾随零 / 字节交换 / 半字拆拼(与 .NET 的 BitOperations 逐点对拍)",
                实二 == 期二, 对拍说明(期二, 实二));

            // ---- ③ 循环移位 / 掩码 ----
            var 源三 = new StringBuilder(头);
            var 期望三 = new StringBuilder();
            源三.Append("无符号整数 v = 0;\n无符号整数 r = 0;\n无符号整数 m = 0;\n");
            foreach (uint 值 in 值们)
                for (int 位 = -32; 位 <= 32; 位 += 4)
                {
                    源三.Append($"v = {值};\n");
                    源三.Append($"r = 位.循环左移(v, {位});\n输出行(r);\n");
                    源三.Append($"r = 位.循环右移(v, {位});\n输出行(r);\n");
                    int 挪 = ((位 % 32) + 32) % 32;
                    期望三.Append(参考循环左移(值, 位)).Append('|');
                    期望三.Append(挪 == 0 ? 值 : (值 >> 挪) | (值 << (32 - 挪))).Append('|');
                }
            foreach (int 位 in new[] { -1, 0, 1, 8, 16, 31, 32, 40 })
            {
                源三.Append($"m = 位.掩码({位});\n输出行(m);\n");
                期望三.Append(参考掩码(位)).Append('|');
            }
            源三.Append("停止\n");
            string 期三 = 期望三.ToString().TrimEnd('|'), 实三 = 跑(源三.ToString());
            记录("位与数学库 位:循环左移 / 循环右移(位数 -32~32 都按 % 32 归一)+ 掩码(越界那一档也给结果,与 .NET 对拍)",
                实三 == 期三, 对拍说明(期三, 实三));

            // ---- ④ 数学:比较与取整四件 ----
            int[] 整们 = [0, 1, -1, 7, -7, 2147483647, -2147483647, -2147483648];
            var 源四 = new StringBuilder(头);
            var 期望四 = new StringBuilder();
            foreach (int 左 in 整们)
                foreach (int 右 in new[] { 0, 3, -3, 2147483647 })
                {
                    源四.Append($"输出行(数学.绝对值({字面(左)}));\n");
                    期望四.Append(左 < 0 ? 0 - 左 : 左).Append('|');
                    源四.Append($"输出行(数学.最大({字面(左)}, {字面(右)}));\n");
                    期望四.Append(Math.Max(左, 右)).Append('|');
                    源四.Append($"输出行(数学.最小({字面(左)}, {字面(右)}));\n");
                    期望四.Append(Math.Min(左, 右)).Append('|');
                    源四.Append($"输出行(数学.夹取({字面(左)}, {字面(右)}, 100));\n");
                    期望四.Append(右 > 100 ? 右 : Math.Clamp(左, Math.Min(右, 100), Math.Max(右, 100))).Append('|');
                    源四.Append($"输出行(数学.符号({字面(左)}));\n");
                    期望四.Append(Math.Sign(左)).Append('|');
                }
            源四.Append("停止\n");
            string 期四 = 期望四.ToString().TrimEnd('|'), 实四 = 跑(源四.ToString());
            记录("位与数学库 数学:绝对值 / 最大 / 最小 / 符号(与 .NET 的 Math 对拍)+ 夹取(含 `下 > 上` 那一档的口径)",
                实四 == 期四, 对拍说明(期四, 实四));

            // ---- ⑤ 整数幂 / 整数开方 ----
            var 源五 = new StringBuilder(头);
            var 期望五 = new StringBuilder();
            foreach (int 底 in new[] { 0, 1, 2, 3, -2, 7, -7, 46340 })
                foreach (int 指 in new[] { -1, 0, 1, 2, 5, 10, 31 })
                {
                    源五.Append($"输出行(数学.整数幂({字面(底)}, {字面(指)}));\n");
                    int 幂 = 1;
                    for (int k = 0; k < 指; k++) 幂 = unchecked(幂 * 底);
                    期望五.Append(指 < 0 ? 0 : 幂).Append('|');
                }
            foreach (int 值 in new[] { -4, -1, 0, 1, 2, 3, 4, 8, 9, 10, 15, 16, 17, 1000000, 2147483646, 2147483647 })
            {
                源五.Append($"输出行(数学.整数开方({字面(值)}));\n");
                期望五.Append(参考开方(值)).Append('|');
            }
            源五.Append("停止\n");
            string 期五 = 期望五.ToString().TrimEnd('|'), 实五 = 跑(源五.ToString());
            记录("位与数学库 数学:整数幂(含指数为负那一档)+ 整数开方(牛顿迭代,与 .NET 的整数平方根逐点对拍)",
                实五 == 期五, 对拍说明(期五, 实五));

            // ---- ⑥ 最大公约数 / 最小公倍数 / 向上整除 / 正模 / 符号 ----
            int[] 对左 = [0, 1, 12, -12, 17, 100, 2147483647, -2147483648];
            int[] 对右 = [0, 1, 18, -18, 5, 7, 2, -2];
            var 源六 = new StringBuilder(头);
            var 期望六 = new StringBuilder();
            foreach (int 左 in 对左)
                foreach (int 右 in 对右)
                {
                    源六.Append($"输出行(数学.最大公约数({字面(左)}, {字面(右)}));\n");
                    期望六.Append(参考最大公约数(左, 右)).Append('|');
                    源六.Append($"输出行(数学.最小公倍数({字面(左)}, {字面(右)}));\n");
                    期望六.Append(参考最小公倍数(左, 右)).Append('|');
                    源六.Append($"输出行(数学.向上整除({字面(左)}, {字面(右)}));\n");
                    期望六.Append(参考向上整除(左, 右)).Append('|');
                    源六.Append($"输出行(数学.正模({字面(左)}, {字面(右)}));\n");
                    期望六.Append(参考正模(左, 右)).Append('|');
                }
            源六.Append("停止\n");
            string 期六 = 期望六.ToString().TrimEnd('|'), 实六 = 跑(源六.ToString());
            记录("位与数学库 数学:最大公约数 / 最小公倍数 / 向上整除 / 正模(8 × 8 个点,含除数为 0 与负数那几档,与 .NET 对拍)",
                实六 == 期六, 对拍说明(期六, 实六));

            // ---- ⑦ 接线哨兵:两个库在位、五段头注释齐、函数条数与头注释对得上 ----
            string 位源 = File.ReadAllText(位路径), 数学源 = File.ReadAllText(数学路径);
            记接线("位与数学库 接线哨兵:两个库在 Sample\\库 下、五段头注释(口径 / 函数表 / 英文对照 / 复杂度 / 边界)齐、函数条数对得上",
                位源.Contains("模块 位", StringComparison.Ordinal) && 数学源.Contains("模块 数学", StringComparison.Ordinal)
                && 位源.Contains("提供的函数(14 个)", StringComparison.Ordinal)
                && 数学源.Contains("提供的函数(11 个)", StringComparison.Ordinal)
                && 头注释五段齐(位源) && 头注释五段齐(数学源)
                && 头数(位源) == 14 && 头数(数学源) == 11,
                $"位.hb 公开 {头数(位源)} 个 / 数学.hb 公开 {头数(数学源)} 个;五段头注释 = " +
                $"{头注释五段齐(位源)} / {头注释五段齐(数学源)}");

            // 头注释里"提供的函数(N 个)"那句话里的 N(照着数,与 `方法` 的公开条数比)
            static int 头数(string 源)
            {
                var m = System.Text.RegularExpressions.Regex.Match(源, @"提供的函数\((\d+) 个\)");
                return m.Success ? int.Parse(m.Groups[1].Value) : -1;
            }
            static bool 头注释五段齐(string 源) =>
                源.Contains("口径(2026-", StringComparison.Ordinal)
                && 源.Contains("提供的函数", StringComparison.Ordinal)
                && 源.Contains("英文对照", StringComparison.Ordinal)
                && 源.Contains("复杂度:", StringComparison.Ordinal)
                && 源.Contains("边界(如实登记", StringComparison.Ordinal);
        }

        // ---- 参考实现(.NET 这一侧;写成静态方法,免得挤在上面那一段里) ----
        private static int 参考开方(int 值)
        {
            if (值 < 0) return -1;
            int 果 = (int)Math.Sqrt((double)值);
            while ((long)(果 + 1) * (果 + 1) <= 值) 果++;
            while ((long)果 * 果 > 值) 果--;
            return 果;
        }

        private static long 参考向上整除(int 左, int 右)
        {
            if (右 == 0) return 0;
            long 商 = (long)左 / 右, 余 = (long)左 % 右;
            if (余 == 0) return 商;
            return (左 > 0) == (右 > 0) ? 商 + 1 : 商;
        }

        private static long 参考正模(int 左, int 右)
        {
            if (右 == 0) return 0;
            long 余 = (long)左 % 右;
            return 余 < 0 ? 余 + Math.Abs((long)右) : 余;
        }

        private static long 参考最大公约数(int 左, int 右)
        {
            // 口径与库里那一份逐字对齐(先取绝对值,而 `-2147483648` 的相反数在 32 位里装不下、回绕还是它自己):
            // 其余 63 个点都由这段 .NET 算术独立算出来,只有这一个别扭数是"照口径复现"
            int 左值 = 左 < 0 ? unchecked(0 - 左) : 左, 右值 = 右 < 0 ? unchecked(0 - 右) : 右;
            while (右值 != 0)
            {
                int 余 = 左值 % 右值;
                if (余 < 0) 余 += 右值;
                (左值, 右值) = (右值, 余);
            }
            return 左值;
        }

        private static int 参考最小公倍数(int 左, int 右)
        {
            if (左 == 0 || 右 == 0) return 0;
            long 约 = 参考最大公约数(左, 右);
            return 约 == 0 ? 0 : unchecked((int)((long)左 / 约 * 右));
        }
    }
}
