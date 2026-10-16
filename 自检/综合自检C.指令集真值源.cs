using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R3-6-1:ISA 真值源(`指令集真值源C`)的自检组。
    ///
    /// <para>它盯五件事(都是**行为**断言:真生成、真读盘、真逐条复算,不是读源码文本):</para>
    /// <list type="number">
    /// <item>**生成的是它自己**:拿 `指令集真值源C.生成()` 现场重算一遍,与盘上那份**逐字节**比
    ///   —— 改了指令表忘了跑 `hbcc --更新指令集` 当场红灯;</item>
    /// <item>**盘上那份按段读得出来**:只看盘上文件,把各段的记录行数与"该有多少条"逐一对一遍
    ///   (手改盘上那份 / 某一段整段丢了,这一条抓得到);</item>
    /// <item>**盘上那份能当数据源**:按格式**独立拆列**(不是拿生成器再跑一遍),逐条复算
    ///   操作码 / 中英名 / 格式 / 操作数写法 / 位布局 / 示例 / 带跳转目标 / 双语可互转;</item>
    /// <item>**语义那一半来自文档**:`指令集说明.md §3 操作码表` 里**每条指令都有一行**、
    ///   操作码与名字跟指令表对得上、说明不许是空的,而且反过来的表行也必须在指令表里(没有多余行);</item>
    /// <item>**语义落点指得到实处**:每条指令都登记了落点、落点表里没有指令表之外的操作码、
    ///   `@` 前面那份文件真的在仓库里、文件末尾那份"落点文件清单"与它逐条一致。</item>
    /// </list>
    /// <para>**校验和那一格有意不做**:一是自指(先算表体再把校验和那一行拼上去,"被哈希的那一份"
    /// 与"落盘那一份"就差那个换行,校验和跟自己对不上);二是**判据已经更强** —— "重新生成逐字节比"
    /// 抓得到的,校验和抓不到的都少。文件本身**逐字节可复现**(不带时间戳),要复现看修改时间。</para>
    /// <para>为什么值得单开一组:`指令集真值源.txt` 是 R3-6-2(C 解释器核)的施工基准 ——
    /// 它一旦过期,后来的实现就会照着一份旧 ISA 写,漂了还没人知道。</para>
    /// </summary>
    public static partial class 综合自检C
    {
        /// <summary> 真值源自检:逐字节 / 按段读得出 / 独立复算 / 语义与文档对齐 / 语义落点 (五条断言) </summary>
        private static void 指令集真值源自检()
        {
            string? 示例根 = 找示例目录();
            string? 仓库根 = 示例根 == null ? null : Directory.GetParent(示例根)?.FullName;
            if (仓库根 == null)
            {
                跳过("指令集真值源 生成的是它自己(逐字节)", "工作副本之外跑自检,找不到仓库根");
                跳过("指令集真值源 盘上那份逐条复算对得上(独立拆列)", "工作副本之外跑自检,找不到仓库根");
                跳过("指令集真值源 盘上那份能按段段读出来", "工作副本之外跑自检,找不到仓库根");
                跳过("指令集真值源 语义说明与文档逐条对齐", "工作副本之外跑自检,找不到仓库根");
                跳过("指令集真值源 语义落点全覆盖且指得到实处", "工作副本之外跑自检,找不到仓库根");
                return;
            }

            string 路径 = Path.Combine(仓库根, 指令集真值源C.相对落点);
            string 文档路径 = Path.Combine(仓库根, 指令集真值源C.语义文档相对路径);

            // 生成一遍(纯函数;文件存在探针真查盘)
            string? 文档全文 = File.Exists(文档路径) ? File.ReadAllText(文档路径) : null;
            string 该是 = 指令集真值源C.生成(文档全文,
                f => File.Exists(Path.Combine(仓库根, f.Replace('/', Path.DirectorySeparatorChar))));

            // ---- ① 盘上那份 == 现场生成的(逐字节) ----
            byte[]? 盘上字节 = File.Exists(路径) ? File.ReadAllBytes(路径) : null;
            byte[] 该是字节 = new UTF8Encoding(false).GetBytes(该是);
            bool 一致 = 盘上字节 != null && 盘上字节.SequenceEqual(该是字节);
            记录("指令集真值源 生成的是它自己(逐字节)",
                一致,
                一致
                    ? $"{该是字节.Length} 字节 / {该是.Replace("\r\n", "\n").Split('\n').Length} 行"
                    : 盘上字节 == null
                        ? $"盘上没有 {指令集真值源C.相对落点} —— 跑 `hbcc --更新指令集` 生成一份"
                        : $"盘上 {盘上字节.Length} 字节 / 现在生成的是 {该是字节.Length} 字节" +
                          $" —— 改了指令表 / 文档语义就重跑 `hbcc --更新指令集`");

            // ---- ② 盘上那份按段读得出来(段数的账对得上) ----
            // 为什么单列一条:`生成的是它自己(逐字节)` 只在**生成器与它一致**时能发现问题 ——
            // 手改盘上那份会红灯不假,但"某一段整段丢了 / 生成器与自检共用同一个错"这类事,
            // 得有一条**只看盘上文件**的判据。它读的就是各段的记录行,数与"段头声明的条数"逐一对。
            try
            {
                if (盘上字节 == null)
                {
                    跳过("指令集真值源 盘上那份能按段段读出来", "盘上还没有这份文件");
                }
                else
                {
                    string 盘上全文 = new UTF8Encoding(false).GetString(盘上字节).Replace("\r\n", "\n");
                    var 行们 = 盘上全文.Split('\n');
                    int 格式数 = 行们.Count(x => x.StartsWith("格式\t"));
                    int 指令数 = 行们.Count(x => x.StartsWith("指令\t"));
                    int 关系数 = 行们.Count(x => x.StartsWith("关系码\t"));
                    int 语义数 = 行们.Count(x => x.StartsWith("语义\t"));
                    int 落点数 = 行们.Count(x => x.StartsWith("落点文件\t"));
                    int 声明落点数 = 指令集真值源C.语义落点操作码().Distinct()
                        .Select(码 => 指令集真值源C.取语义落点(码)!.Split('@')[0]).Distinct(StringComparer.Ordinal).Count();
                    bool 好 = 格式数 == Enum.GetValues<指令格式>().Length
                              && 指令数 == 指令C.全部指令.Count
                              && 语义数 == 指令C.全部指令.Count
                              && 关系数 == 指令C.关系码表.Count
                              && 落点数 == 声明落点数;
                    记录("指令集真值源 盘上那份能按段段读出来", 好,
                        好
                            ? $"格式 {格式数} / 指令 {指令数} / 关系码 {关系数} / 语义 {语义数} / 落点文件 {落点数} 段都对得上"
                            : $"格式 {格式数}(该 {Enum.GetValues<指令格式>().Length}) / 指令 {指令数}(该 {指令C.全部指令.Count})" +
                              $" / 关系码 {关系数}(该 {指令C.关系码表.Count}) / 语义 {语义数} / 落点文件 {落点数}(该 {声明落点数})");
                }
            }
            catch (Exception ex) { 记录异常("指令集真值源 盘上那份能按段段读出来", ex); }

            // ---- ③ 盘上那份逐条复算(独立拆列,不借生成器) ----
            跑且断言("指令集真值源 盘上那份逐条复算对得上(独立拆列)", () =>
            {
                if (盘上字节 == null) { 记录("指令集真值源 盘上那份逐条复算对得上(独立拆列)", false, "盘上没有这份文件"); return; }
                string 全文 = new UTF8Encoding(false).GetString(盘上字节).Replace("\r\n", "\n");
                var 行们 = 全文.Split('\n');
                var 问题 = new List<string>();

                // 非注释行不许有尾随空白(格式纪律;生成的文本自己先过这一关)
                for (int i = 0; i < 行们.Length; i++)
                    if (行们[i].Length > 0 && !行们[i].StartsWith('#') && 行们[i] != 行们[i].TrimEnd())
                    {
                        问题.Add($"第 {i + 1} 行有尾随空白");
                        break;
                    }

                // 段一:格式
                // 段一的每条是 **4 格**:`格式` 记号 + 格式名 + 操作数写法 + 位布局(别按 3 格判 —— 踩过)
                var 格式行 = 行们.Where(x => x.StartsWith("格式\t")).Select(x => x.Split('\t')).ToArray();
                var 应有格式 = Enum.GetValues<指令格式>();
                if (格式行.Length != 应有格式.Length || 格式行.Any(x => x.Length != 4))
                    问题.Add($"格式段 {格式行.Length} 行(该是 {应有格式.Length} 行 / 每行 4 格;" +
                             $"列数分布 {string.Join(",", 格式行.GroupBy(x => x.Length).Select(g => $"{g.Key}列×{g.Count()}"))})");
                else
                    for (int i = 0; i < 应有格式.Length; i++)
                    {
                        if (格式行[i][1] != 应有格式[i].ToString())
                            问题.Add($"格式第 {i + 1} 行名字是 {格式行[i][1]},该是 {应有格式[i]}");
                        if (格式行[i][2] != 指令C.操作数写法(应有格式[i]))
                            问题.Add($"格式 {格式行[i][1]} 的操作数写法对不上(表里 {格式行[i][2]})");
                        if (格式行[i][3] != 指令C.格式位布局(应有格式[i]))
                            问题.Add($"格式 {格式行[i][1]} 的位布局对不上(表里 {格式行[i][3]})");
                    }

                // 段二:操作码表 —— **按操作码排**逐个复算
                var 表 = 指令C.全部指令.OrderBy(x => x.操作码).ToArray();
                var 指令行 = 行们.Where(x => x.StartsWith("指令\t")).Select(x => x.Split('\t')).ToArray();
                if (指令行.Length != 表.Length)
                    问题.Add($"操作码段 {指令行.Length} 行,指令表 {表.Length} 条");
                else
                    for (int i = 0; i < 表.Length; i++)
                    {
                        var 格 = 指令行[i];
                        if (格.Length != 8) { 问题.Add($"操作码段第 {i + 1} 行不是 8 列"); continue; }
                        string 码该是 = $"0x{表[i].操作码:X2}";
                        if (格[1] != 码该是) 问题.Add($"{格[1]} 该是 {码该是}");
                        if (格[2] != 表[i].中文) 问题.Add($"{码该是} 中文是 {格[2]},该是 {表[i].中文}");
                        if (格[3] != 表[i].英文) 问题.Add($"{码该是} 英文是 {格[3]},该是 {表[i].英文}");
                        if (格[4] != 表[i].格式.ToString()) 问题.Add($"{码该是} 格式是 {格[4]},该是 {表[i].格式}");
                        if (格[5] != 指令C.操作数写法(表[i].格式)) 问题.Add($"{码该是} 操作数写法对不上");
                        if (格[6] != (指令C.带跳转目标(表[i].操作码) ? "是" : "否")) 问题.Add($"{码该是} 带跳转目标那一列对不上");
                        if (格[7] != (指令C.取示例(表[i].英文) ?? "—")) 问题.Add($"{码该是} 写法示例对不上");
                        // 双语可互转(拿表里的英文名倒着查回中文名)
                        if (!指令C.是中文指令名(指令C.到中文指令名(表[i].英文)))
                            问题.Add($"{码该是} 英文名倒查不回中文名");
                        if (指令C.取指令码表(表[i].中文) != 表[i].操作码 || 指令C.取指令码表(表[i].英文) != 表[i].操作码)
                            问题.Add($"{码该是} 按中/英名查操作码对不上");
                        if (指令C.取格式(表[i].操作码) != 表[i].格式) 问题.Add($"{码该是} 按操作码查格式对不上");
                    }

                // 段三:关系码
                var 关系行 = 行们.Where(x => x.StartsWith("关系码\t")).Select(x => x.Split('\t')).ToArray();
                if (关系行.Length != 指令C.关系码表.Count)
                    问题.Add($"关系码段 {关系行.Length} 行,关系码表 {指令C.关系码表.Count} 条");
                else
                    foreach (var 格 in 关系行)
                    {
                        if (格.Length != 5) { 问题.Add($"关系码段有行不是 5 列"); continue; }
                        if (!指令C.关系码表.TryGetValue(格[1], out uint 值) || 值.ToString() != 格[2])
                            问题.Add($"关系码 {格[1]} 的数值对不上(表里 {格[2]})");
                        if (格[4] != 指令C.关系码说明(值)) 问题.Add($"关系码 {格[1]} 的说明对不上(表里 {格[4]})");
                    }

                // 段四:语义段的行数与操作码一一对应(内容那半由 ④ 管)
                var 语义行 = 行们.Where(x => x.StartsWith("语义\t")).Select(x => x.Split('\t')).ToArray();
                if (语义行.Length != 表.Length) 问题.Add($"语义段 {语义行.Length} 行,指令表 {表.Length} 条");
                else
                    for (int i = 0; i < 表.Length; i++)
                    {
                        if (语义行[i].Length != 5) { 问题.Add($"语义段第 {i + 1} 行不是 5 列"); continue; }
                        if (语义行[i][1] != $"0x{表[i].操作码:X2}") 问题.Add($"语义段第 {i + 1} 行操作码与指令表错位");
                        if (语义行[i][2] != 表[i].中文) 问题.Add($"语义段第 {i + 1} 行中文名与指令表错位");
                    }

                记录("指令集真值源 盘上那份逐条复算对得上(独立拆列)",
                    问题.Count == 0,
                    问题.Count == 0
                        ? $"格式 {格式行.Length} 段 / 指令 {指令行.Length} 条 / 关系码 {关系行.Length} 条 / 语义 {语义行.Length} 条,逐格复算一致"
                        : $"{问题.Count} 处对不上,如 " + string.Join(" / ", 问题.Take(3)));
            });

            // ---- ④ 语义说明与文档逐条对齐 ----
            跑且断言("指令集真值源 语义说明与文档逐条对齐", () =>
            {
                if (文档全文 == null)
                {
                    记录("指令集真值源 语义说明与文档逐条对齐", false,
                        $"找不到 {指令集真值源C.语义文档相对路径}(ISA 语义的唯一事实源不能缺)");
                    return;
                }
                var 文档行 = 指令集真值源C.读文档语义行(文档全文);
                if (文档行.Length == 0)
                {
                    记录("指令集真值源 语义说明与文档逐条对齐", false,
                        $"在 {指令集真值源C.语义文档相对路径} 里找不到「{指令集真值源C.语义文档节标题}」那段表");
                    return;
                }

                var 表 = 指令C.全部指令.OrderBy(x => x.操作码).ToArray();
                var 文档里 = new Dictionary<byte, (string 中文, string 说明)>();
                var 坏行 = new List<string>();
                foreach (string 行 in 文档行)
                {
                    var 格 = 指令集真值源C.拆文档表行(行);
                    if (格 == null) { 坏行.Add(行); continue; }
                    if (!格[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        || !byte.TryParse(格[0][2..], System.Globalization.NumberStyles.HexNumber, null, out byte 码))
                    { 坏行.Add(行); continue; }
                    if (格[4].Length == 0) 坏行.Add(行);
                    文档里[码] = (格[1], 格[4]);
                }
                var 缺 = 表.Where(x => !文档里.ContainsKey(x.操作码)).Select(x => $"0x{x.操作码:X2} {x.中文}").Take(3).ToArray();
                var 错名 = 表.Where(x => 文档里.TryGetValue(x.操作码, out var v) && v.中文 != x.中文)
                              .Select(x => $"0x{x.操作码:X2} 文档写 {文档里[x.操作码].中文} / 指令表是 {x.中文}").Take(3).ToArray();
                var 多 = 文档里.Keys.Where(k => 表.All(x => x.操作码 != k)).Select(k => $"0x{k:X2}").Take(3).ToArray();
                var 空 = 表.Where(x => 文档里.TryGetValue(x.操作码, out var v) && v.说明.Trim().Length == 0)
                           .Select(x => x.中文).Take(3).ToArray();
                bool 好 = 坏行.Count == 0 && 缺.Length == 0 && 错名.Length == 0 && 多.Length == 0 && 空.Length == 0;
                记录("指令集真值源 语义说明与文档逐条对齐", 好,
                    好
                        ? $"{文档行.Length} 行逐条对上({表.Length} 条指令,各有一行且名字 / 操作码一致)"
                        : $"文档表里 {文档行.Length} 行 / 指令表 {表.Length} 条;" +
                          $"{(坏行.Count > 0 ? $"格式坏 {坏行.Count} 行" : "")}" +
                          $"{(缺.Length > 0 ? $";缺 " + string.Join(" / ", 缺) : "")}" +
                          $"{(错名.Length > 0 ? $";名字不符 " + string.Join(" / ", 错名) : "")}" +
                          $"{(多.Length > 0 ? $";文档里多了 " + string.Join(" / ", 多) : "")}" +
                          $"{(空.Length > 0 ? $";说明是空的 " + string.Join(" / ", 空) : "")}");
            });

            // ---- ⑤ 语义落点:全覆盖 + 指得到实处 ----
            跑且断言("指令集真值源 语义落点全覆盖且指得到实处", () =>
            {
                var 表 = 指令C.全部指令.OrderBy(x => x.操作码).ToArray();
                var 问题 = new List<string>();
                var 漏 = 表.Where(x => string.IsNullOrWhiteSpace(指令集真值源C.取语义落点(x.操作码)))
                           .Select(x => x.中文).Take(3).ToArray();
                if (漏.Length > 0) 问题.Add("漏登记落点:" + string.Join(" / ", 漏));
                var 多 = 指令集真值源C.语义落点操作码().Where(码 => 表.All(x => x.操作码 != 码))
                            .Select(码 => $"0x{码:X2}").Take(3).ToArray();
                if (多.Length > 0) 问题.Add("登记了指令表里没有的操作码:" + string.Join(" / ", 多));
                var 指向空 = 指令集真值源C.语义落点操作码().Distinct()
                            .Select(码 => (码, 落点: 指令集真值源C.取语义落点(码)!))
                            .Where(x => x.落点.Split('@')[0].Length == 0).Select(x => $"0x{x.码:X2}").Take(3).ToArray();
                if (指向空.Length > 0) 问题.Add("落点里没有文件名:" + string.Join(" / ", 指向空));
                var 文件缺 = 指令集真值源C.语义落点操作码().Distinct()
                            .Select(码 => 指令集真值源C.取语义落点(码)!.Split('@')[0]).Distinct(StringComparer.Ordinal)
                            .Where(f => !File.Exists(Path.Combine(仓库根, f.Replace('/', Path.DirectorySeparatorChar))))
                            .Take(3).ToArray();
                if (文件缺.Length > 0) 问题.Add("落点文件不在仓库里:" + string.Join(" / ", 文件缺));
                记录("指令集真值源 语义落点全覆盖且指得到实处", 问题.Count == 0,
                    问题.Count == 0
                        ? $"{表.Length} 条各有一处落点,涉及 {指令集真值源C.语义落点操作码().Select(码 => 指令集真值源C.取语义落点(码)!.Split('@')[0]).Distinct().Count()} 份文件,都在仓库里"
                        : string.Join(";", 问题));
            });

            // ---- ⑥ 落点文件清单里的路径与落点表一致(逐条比,不只比条数) ----
            跑且断言("指令集真值源 落点文件清单与落点表逐条一致", () =>
            {
                if (盘上字节 == null) { 记录("指令集真值源 落点文件清单与落点表逐条一致", false, "盘上没有这份文件"); return; }
                string 全文 = new UTF8Encoding(false).GetString(盘上字节).Replace("\r\n", "\n");
                var 清单行 = 全文.Split('\n').Where(x => x.StartsWith("落点文件\t")).Select(x => x.Split('\t')).ToArray();
                var 该有 = 指令集真值源C.语义落点操作码().Distinct()
                          .Select(码 => 指令集真值源C.取语义落点(码)!.Split('@')[0])
                          .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                var 盘上 = 清单行.Select(x => x.Length >= 2 ? x[1] : "").OrderBy(x => x, StringComparer.Ordinal).ToArray();
                bool 好 = 清单行.All(x => x.Length == 3) && 盘上.SequenceEqual(该有, StringComparer.Ordinal)
                          && 清单行.All(x => x[2] == "在");
                记录("指令集真值源 落点文件清单与落点表逐条一致", 好,
                    好 ? $"{该有.Length} 份文件逐条对上、都在"
                       : $"盘上 {盘上.Length} 份 / 该有 {该有.Length} 份" +
                         (盘上.SequenceEqual(该有, StringComparer.Ordinal) ? "" : "(清单不一致)") +
                         (清单行.Any(x => x.Length == 3 && x[2] != "在") ? ";有文件标着「不在」" : ""));
            });
        }
    }
}
