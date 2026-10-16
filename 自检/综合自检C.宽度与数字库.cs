// ============================================================
// 宽度 / 数字 两个库的自检(G4 批 3,第346批)
//   —— `Sample\库\宽度.hb` · `Sample\库\数字.hb`
//
//   判据照 `基础库施工计划.md` §2 批 3 那一行:
//     "中文占 2 格 / ASCII 1 格的宽度表 + 对齐 / 补零 / 千分位"。
//   这一组这么验:
//     · **宽度表那一条是"两边对拍"**:库里那一份私有的字宽表,与语言内建 `取文字宽度` 用的
//       `图形宿主C.量文字宽度` —— 拿一串覆盖各档边界的样本字(半角 / 汉字 / 假名 / 韩文 /
//       全角 / 兼容区 / 扩展 A ……),逐个比"对齐出来的结果占几格",对不上当场红灯;
//     · 对齐 / 居中 / 截断的**规则**用手写期望钉(格数 0 / 负数 / 相等 / 更小 / 省略放不下 ……);
//     · 数字那一族的期望值**由 .NET 现场算**(`Convert.ToString(…, 进制)` /
//       `ToString("N0")` / `PadLeft`),真编真跑逐行比 —— 不手抄一串数当基准。
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        private static void 宽度与数字库自检()
        {
            string? 示例 = 找示例目录();
            string? 仓库根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
            string 库目录 = 仓库根 == null ? "" : Path.Combine(仓库根, "Sample", "库");
            string 宽路径 = Path.Combine(库目录, "宽度.hb").Replace('\\', '/');
            string 数路径 = Path.Combine(库目录, "数字.hb").Replace('\\', '/');
            if (!File.Exists(宽路径) || !File.Exists(数路径))
            {
                跳过("宽度与数字库 定位 Sample\\库 下两个库", $"找不到 {宽路径} / {数路径}(不在工作副本里跑就跳过)");
                return;
            }
            string 头 = $"包含 \"{宽路径}\"\n包含 \"{数路径}\"\n";

            static string 跑(string 源)
            {
                var 编译 = new 汇编器C().汇编多文件带调试(
                    new List<(string, string)> { ("主程序.hb", 源) }, true,
                    中间层C.默认堆大小, null, 中间层C.默认池大小,
                    虚拟机C.默认内存大小, 32 * 1024);
                // ⚠ `取文字宽度` 是**图形原语(功能号 69)** —— 不挂宿主调用它就一路返回 0
                //   (实测:这一组原来没挂,宽表那条断言拿到 63 个 0);图形那一族与文件那一族同一个入口。
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
                if (期望 == 实得) return "一字不差";
                string[] 期望段 = 期望.Split('|'), 实得段 = 实得.Split('|');
                int 少 = Math.Min(期望段.Length, 实得段.Length);
                for (int i = 0; i < 少; i++)
                    if (期望段[i] != 实得段[i])
                        return $"第 {i + 1} 个输出对不上:期望 [{期望段[i]}] / 实得 [{实得段[i]}];实得头 12 个 = " +
                            string.Join(",", 实得段.Take(12)) + $";实得共 {实得段.Length} 条 / 期望共 {期望段.Length} 条";
                return $"条数对不上:期望 {期望段.Length} 条 / 实得 {实得段.Length} 条";
            }

            // ---- ① 宽表对拍:样本字逐个比"对齐出来占几格"(库里的表 ↔ 图形宿主C.量文字宽度) ----
            try
            {
                string[] 样本 =
                [
                    "a", "0", " ", "-", "汉", "字", "，", "。", "、", "…", "·",
                    "\u2E80", "\u3041", "\u3400", "\u4E00", "\uA000", "\uAC00", "\uF900", "\uFE30", "\uFF01", "\uFFE0",
                ];
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                源.Append("字符串 样 = \"\";\n字符串 对齐后 = \"\";\n");
                foreach (string 字 in 样本)
                {
                    源.Append($"样 = \"{字}\";\n");
                    源.Append("对齐后 = 宽度.左对齐(样, 5);\n输出行(取文字宽度(对齐后));\n");
                    源.Append("对齐后 = 宽度.右对齐(样, 5);\n输出行(取文字宽度(对齐后));\n");
                    源.Append("对齐后 = 宽度.居中(样, 6);\n输出行(取文字宽度(对齐后));\n");
                    int 宽 = 图形宿主C.量文字宽度(字);
                    期望.Append(Math.Max(5, 宽)).Append('|').Append(Math.Max(5, 宽)).Append('|').Append(Math.Max(6, 宽)).Append('|');
                }
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|');
                记录("宽度与数字库 宽度:库里那份字宽表与内建 `取文字宽度` 逐字对拍(21 个样本字 × 左 / 右 / 居中,含各档边界)",
                    跑(源.ToString()) == 期,
                    对拍说明(期, 跑(源.ToString())));
            }
            catch (Exception ex) { 记录异常("宽度与数字库 宽表对拍", ex); }

            // ---- ② 对齐 / 居中 / 截断的规则(手写期望:格数 0 / 负数 / 相等 / 更小 / 省略放不下) ----
            try
            {
                string 源 = 头 +
                    "输出行(格式化(\"[{0}]\", 宽度.左对齐(\"ab\", 0)));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.左对齐(\"ab\", 0 - 3)));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.右对齐(\"\", 3)));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.居中(\"\", 4)));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.居中(\"ab\", 6)));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.居中(\"汉字\", 5)));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.截断(\"\", 3, \"…\")));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.截断(\"abcdef\", 2, \"…\")));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.截断(\"abcdef\", 1, \"…\")));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.截断(\"汉字\", 4, \"…\")));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.截断(\"汉字字\", 4, \"…\")));\n" +
                    "输出行(格式化(\"[{0}]\", 宽度.截断(\"汉字字\", 0, \"\")));\n" +
                    "停止\n";
                string 期 = "[ab]|[ab]|[   ]|[    ]|[  ab  ]|[汉字 ]|[]|[a…]|[…]|[汉字]|[汉…]|[]";
                记录("宽度与数字库 宽度:左 / 右 / 居中 / 截断的规则(格数 0 / 负数 / 相等 / 更小 / 省略放不下 / 不切半个汉字)",
                    跑(源) == 期, 对拍说明(期, 跑(源)));
            }
            catch (Exception ex) { 记录异常("宽度与数字库 宽度 规则表", ex); }

            // ---- ③ 数字:进制 / 补零 / 千分位,期望值由 .NET 现场算 ----
            try
            {
                int[] 值们 = [0, 1, -1, 7, -7, 255, -255, 1024, 1234567, -1234567, 2147483647, int.MinValue];
                int[] 进制们 = [2, 8, 10, 16, 36];
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                foreach (int 值 in 值们)
                    foreach (int 进制 in 进制们)
                    {
                        源.Append($"输出行(数字.按进制转字符串({字面(值)}, {进制}));\n");
                        long 大 = Math.Abs((long)值);
                        string 位数 = 参考按进制(大, 进制);
                        期望.Append((值 < 0 ? "-" : "") + 位数).Append('|');
                    }
                foreach (int 值 in 值们)
                {
                    源.Append($"输出行(数字.千分位({字面(值)}));\n");
                    期望.Append(值.ToString("N0", CultureInfo.InvariantCulture)).Append('|');
                }
                foreach (int 值 in 值们)
                    foreach (int 位 in new[] { 1, 2, 5, 11 })
                    {
                        源.Append($"输出行(数字.补零({字面(值)}, {位}));\n");
                        string 数 = Math.Abs((long)值).ToString(CultureInfo.InvariantCulture);
                        string 果 = 值 < 0
                            ? "-" + 数.PadLeft(Math.Max(位 - 1, 数.Length), '0')
                            : 数.PadLeft(Math.Max(位, 数.Length), '0');
                        期望.Append(果).Append('|');
                    }
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|');
                记录("宽度与数字库 数字:按进制转字符串(2 / 8 / 10 / 16 / 36 + 32 位边界)+ 补零 + 千分位,与 .NET 逐点对拍",
                    跑(源.ToString()) == 期, 对拍说明(期, 跑(源.ToString())));
            }
            catch (Exception ex) { 记录异常("宽度与数字库 数字 格式化对拍", ex); }

            // ---- ④ 数字:按进制解析(认大小写 / 负号 / 认不出给假并清零 / 溢出按 32 位回绕) ----
            try
            {
                (string 文本, int 进制, bool 成, int 值)[] 表 =
                [
                    ("FF", 16, true, 255), ("ff", 16, true, 255), ("0", 10, true, 0),
                    ("-101", 2, true, -5), ("1010", 2, true, 10), ("ZIK0ZJ", 36, true, 2147483647),
                    ("777", 8, true, 511), ("12Z", 36, true, 1403),
                    ("9", 8, false, 0), ("12X", 10, false, 0), ("", 10, false, 0), ("-", 10, false, 0),
                    ("+5", 10, false, 0), (" 5", 10, false, 0), ("123", 1, false, 0), ("123", 37, false, 0),
                ];
                var 源 = new StringBuilder(头);
                var 期望 = new StringBuilder();
                源.Append("整数 收 = 12345;\n");
                foreach (var (文本, 进制, 成, 值) in 表)
                {
                    源.Append($"输出行(数字.按进制解析(\"{文本}\", {进制}, 收));\n输出行(收);\n");
                    期望.Append(成 ? 1 : 0).Append('|').Append(值).Append('|');
                }
                源.Append("停止\n");
                string 期 = 期望.ToString().TrimEnd('|');
                记录("宽度与数字库 数字:按进制解析(大小写 / 负号 / 认不出给假并清零 / 进制越界),与 .NET 逐点对拍",
                    跑(源.ToString()) == 期, 对拍说明(期, 跑(源.ToString())));
            }
            catch (Exception ex) { 记录异常("宽度与数字库 数字 解析对拍", ex); }

            // ---- ⑤ 接线哨兵:两个库在位、五段头注释齐、头注释说的函数条数对得上 ----
            try
            {
                string 宽源 = File.ReadAllText(宽路径), 数源 = File.ReadAllText(数路径);
                记录("宽度与数字库 接线哨兵:两个库在 Sample\\库 下、五段头注释齐、头注释的函数条数与正文对得上",
                    宽源.Contains("模块 宽度", StringComparison.Ordinal) && 数源.Contains("模块 数字", StringComparison.Ordinal)
                    && 宽源.Contains("提供的函数(4 个)", StringComparison.Ordinal)
                    && 数源.Contains("提供的函数(4 个)", StringComparison.Ordinal)
                    && 五段齐(宽源) && 五段齐(数源) && 方法数(宽源) == 4 && 方法数(数源) == 4,
                    $"公开方法:宽度 {方法数(宽源)} / 数字 {方法数(数源)}");

                static bool 五段齐(string 源) =>
                    源.Contains("口径(2026-", StringComparison.Ordinal)
                    && 源.Contains("英文对照", StringComparison.Ordinal)
                    && 源.Contains("复杂度:", StringComparison.Ordinal)
                    && 源.Contains("边界(如实登记", StringComparison.Ordinal);
                static int 方法数(string 源) =>
                    System.Text.RegularExpressions.Regex.Matches(源, @"(?m)^方法 ").Count;
            }
            catch (Exception ex) { 记录异常("宽度与数字库 接线哨兵", ex); }
        }

        /// <summary>`按进制转字符串` 的参考实现(纯 C# 算术;.NET 的 `Convert` 只认 2 / 8 / 10 / 16,36 进制得自己写)</summary>
        private static string 参考按进制(long 大, int 进制)
        {
            const string 符 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            if (大 == 0) return "0";
            var 果 = new StringBuilder();
            while (大 > 0)
            {
                果.Insert(0, 符[(int)(大 % 进制)]);
                大 /= 进制;
            }
            return 果.ToString();
        }
    }
}
