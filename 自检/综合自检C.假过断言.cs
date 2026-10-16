using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R4-2:清假过断言 —— 这一部分**扫自检自己的源码**,防"假过断言"回来。
    ///
    /// 为什么值得单开一组:假过断言比不写断言更坏 —— 它把"没测"记成"通过",还占着一个看着很硬的名字。
    /// 独立复核(`独立复核测评报告.md` §4、`独立复核待办任务清单_自举路线.md` R4-2)点名的几类形态,
    /// 每一类都对应一个**真发生过**的教训:
    ///   · R2-6 那条"PC 不前进"的断言拿同一个 PC 跟自己比 ⇒ 恒真,而它前面正藏着"IN 之前那条语句多跑一次"的真缺陷;
    ///   · `catch { 不是分号错 = true; }` 把"编译器自己崩"记成"没报分号错" ⇒ PASS;
    ///   · 否定式断言("文案里没有 X")在"根本没抛异常"时拿空串比 ⇒ 错没报出来反而 PASS(这类改成 `异常与文案`)。
    /// 这一组只管**机器能认的形态**(见下面三条正则);语义那几类靠写法本身(改完的用例在各自的组里)。
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary>
        /// 自检源码里的三类**假过形态**,逐种钉住(命中即红):
        ///   ① `记录(&lt;字面名字&gt;, true, …)` —— 谓词写死 `true`,唯一失败途径是自己抛异常(那不是断言);
        ///   ② 同一行 `记录(…)` 里的 `x == x` —— 同一个标识符跟自己比,恒真;
        ///   ③ 裸 `catch { 变量 = true; }` —— 连异常类型都不看就当成"预期行为"
        ///      (`catch (编译异常C …)` 那种**指定了异常类型**的不算,它本身就是一个真判据)。
        ///
        /// **豁免**:同一行写 `// R4-2 豁免: 理由`(自检自己扫自己,真有正当例外就必须把理由写在明面上)。
        /// **找不到仓库根** ⇒ **记跳过**(既不当 PASS,也不当 FAIL —— 这条根本没执行)。
        /// </summary>
        private static void 假过断言自扫自检()
        {
            const string 名 = "R4-2 自检自扫:没有恒真谓词 / 自比较 / 裸 catch 里写 true 的假过断言";
            try
            {
                string? 示例 = 找示例目录();
                string? 自检目录 = 示例 == null ? null : Path.Combine(Directory.GetParent(示例)?.FullName ?? 示例, "自检");
                if (自检目录 == null || !Directory.Exists(自检目录))
                {
                    跳过(名, "工作副本之外跑自检,找不到 自检 目录");
                    return;
                }

                // 名字只认**字符串字面量**(`记录(名, …)` 这种变量名不算),免得把
                // `记录("优化 …", 循环体(写源, true).Contains(…))` 这类正常调用误伤。
                var 恒真 = new Regex("记录\\(\\s*\\$?\"(?:[^\"\\\\]|\\\\.)*\"\\s*,\\s*true\\s*[,)]");
                var 自比 = new Regex("(?<![\\w\\u4e00-\\u9fa5.])(?<x>[A-Za-z_\\u4e00-\\u9fa5][\\w\\u4e00-\\u9fa5]*)\\s*==\\s*\\k<x>(?![\\w\\u4e00-\\u9fa5])");
                var 裸抓 = new Regex("catch\\s*\\{[^}]*=\\s*true\\s*;");

                var 抓到 = new List<string>();
                foreach (string 文件 in Directory.GetFiles(自检目录, "*.cs").OrderBy(x => x, StringComparer.Ordinal))
                {
                    string[] 行们 = File.ReadAllLines(文件, Encoding.UTF8);
                    for (int i = 0; i < 行们.Length; i++)
                    {
                        string 行 = 行们[i];
                        if (行.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;    // 注释行不算(说明文字里当然会提到这些形态)
                        if (行.Contains("R4-2 豁免", StringComparison.Ordinal)) continue;          // 显式豁免
                        string 哪一种 =
                            恒真.IsMatch(行) ? "恒真谓词" :
                            行.Contains("记录(", StringComparison.Ordinal) && 自比.IsMatch(行) ? "自比较" :
                            裸抓.IsMatch(行) ? "裸 catch 里写 true" : "";
                        if (哪一种.Length > 0)
                            抓到.Add($"{Path.GetFileName(文件)}:{i + 1}({哪一种}) {行.Trim()}");
                    }
                }

                记录(名, 抓到.Count == 0, 抓到.Count == 0
                    ? $"自检源码 {Directory.GetFiles(自检目录, "*.cs").Length} 个文件里 0 处(三类形态各一条正则盯着)"
                    : $"{抓到.Count} 处:" + string.Join(" | ", 抓到.Take(3)));
            }
            catch (Exception ex) { 记录异常(名, ex); }
        }
    }
}
