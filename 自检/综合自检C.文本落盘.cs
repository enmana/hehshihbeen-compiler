using System.IO;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        /// <summary>
        /// C26(用户 2026-09-20 提;第161批查 F5 那条路时顺手发现的真缺陷):
        /// **IDE 保存给 `.hb` 加 BOM + 吃掉末尾空行**。
        ///
        /// 两个根因,这次都真量了(不凭印象):
        /// <list type="number">
        /// <item>`保存标签页` 原来写的是 `File.WriteAllText(路径, 文本, Encoding.UTF8)` ——
        ///   那个静态属性是**带 BOM** 的 `UTF8Encoding(true)`,所以"打开 → 什么不改按 Ctrl+S"
        ///   就把本来没 BOM 的 `.hb` 写成带 BOM 的;</item>
        /// <item>`分词器C.清洗文本` 原来在"删不可见字符"的同一支里还 `TrimEnd('\r','\n')`,
        ///   而读盘(`解码文本`)与写盘(`取磁盘文本`)共用它 ⇒ 末尾空行被吃掉
        ///   (`git diff` 上那句 `\ No newline at end of file`)。</item>
        /// </list>
        /// 修法:编码头收进引擎纯函数 `文本落盘C.写回`(IDE 保存 / `工程维护C.写回` / `hbcc --写回` 三处共用),
        /// `清洗文本` 去掉 `TrimEnd`(读进来也不动末尾空行)。
        ///
        /// 这里就是**验收**:四种组合(有 / 无 BOM × 有 / 无末尾空行,再加一份 CRLF)各走一趟
        /// "读进编辑器 → 原样写回",逐字节比;另有一段把**旧口径原样重放**一遍,把这些数字留档。
        /// 界面那两处按源码断言 —— 自检不许开窗口(硬规矩 6)。
        /// </summary>
        private static void 文本落盘自检()
        {
            string 根 = Path.Combine(临时区C.基目录(), "hb文本落盘自检_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                Directory.CreateDirectory(根);

                static byte[] 读(string 路径) => File.ReadAllBytes(路径);
                static bool 有头(byte[] 字节) =>
                    字节.Length >= 3 && 字节[0] == 0xEF && 字节[1] == 0xBB && 字节[2] == 0xBF;
                static bool 逐字节同(byte[] a, byte[] b) => a.Length == b.Length && a.AsSpan().SequenceEqual(b);

                // ---- ① 有 BOM 的判定:按文件头(不存在 → 假,按"新建文件"算) ----
                string 带 = Path.Combine(根, "带BOM.hb"), 不带 = Path.Combine(根, "不带BOM.hb"), 没有 = Path.Combine(根, "不存在.hb");
                File.WriteAllText(带, "内容\n", new UTF8Encoding(true));
                File.WriteAllText(不带, "内容\n", new UTF8Encoding(false));
                记录("文本落盘 有BOM 按文件头判(文件不存在 → 假 = 按新建算)",
                    文本落盘C.有BOM(带) && !文本落盘C.有BOM(不带) && !文本落盘C.有BOM(没有),
                    $"带 BOM {(文本落盘C.有BOM(带) ? "认" : "不认")}、不带 {(文本落盘C.有BOM(不带) ? "认" : "不认")}、" +
                    $"文件不存在 {(文本落盘C.有BOM(没有) ? "认" : "不认")}(应为 认/不认/不认)");

                // ---- ② 写回按原样:没有 BOM 的不加、有的不丢 ----
                string 加过 = Path.Combine(根, "加过.hb"), 丢过 = Path.Combine(根, "丢过.hb");
                File.Copy(不带, 加过, true);
                File.Copy(带, 丢过, true);
                文本落盘C.写回(加过, "改后\n");
                文本落盘C.写回(丢过, "改后\n");
                记录("文本落盘 写回按原样:没有 BOM 的不加、有的不丢",
                    !有头(读(加过)) && 有头(读(丢过))
                    && Encoding.UTF8.GetString(读(加过)) == "改后\n" && Encoding.UTF8.GetString(读(丢过)).TrimStart('\uFEFF') == "改后\n",
                    $"没 BOM 的那份写回后 {(有头(读(加过)) ? "长出了 BOM" : "还是没 BOM")};" +
                    $"带 BOM 的那份 {(有头(读(丢过)) ? "BOM 还在" : "BOM 丢了")}");

                // ---- ③ 目标文件不存在 → 不带 BOM 写(与 解决方案C.新建文件 同一档) ----
                string 新建 = Path.Combine(根, "新建.hb");
                文本落盘C.写回(新建, "新建内容\n");
                记录("文本落盘 目标文件不存在 → 不带 BOM 写(新建 / 另存为到新名字)",
                    !有头(读(新建)) && Encoding.UTF8.GetString(读(新建)) == "新建内容\n",
                    "与 解决方案C.新建文件 / 新建项目 那一档一致");

                // ---- ④ 验收:「读进编辑器 → 什么不改写回」逐字节不变(六种组合;最后一种是 R8-1 补的"带 `[]`") ----
                // 读那一趟模拟 IDE 的 `解码文本`(GetString 不摘 BOM,由 清洗文本 摘),写那一趟模拟 `保存标签页`。
                var 用例 = new (string 名, bool 有BOM, string 正文)[]
                {
                    ("无BOM+末尾空行", false, "// 说明\n输出行(1);\n停止\n\n"),
                    ("无BOM+无末尾空行", false, "// 说明\n输出行(1);\n停止"),
                    ("有BOM+末尾空行", true, "// 说明\n输出行(1);\n停止\n\n"),
                    ("有BOM+无末尾空行", true, "// 说明\n输出行(1);\n停止"),
                    ("无BOM+CRLF", false, "// 说明\r\n输出行(1);\r\n停止\r\n"),
                    // R8-1(用户 2026-09-21):数组参数的 `[]`、列表声明的 `[]`、`[]` 这种**空方括号对**
                    // 以前会被"删不可见字符"那条正则整对吃掉(`整数 nums[]` → `整数 nums`),
                    // 所以这一条专门拿"带 `[]` 的源码"走同一趟往返。`[3]` / `nums[0]` / `&]` 一起带上。
                    ("无BOM+数组与列表声明", false,
                        "整数 表[3] = [1, 2, 3];\n短整数 分数[];\n方法 求和(整数 nums[]) 返回 整数\n{\n    返回 nums[0];\n}\n// &] 与 ^] 也一起走一趟\n"),
                };
                var 不一致 = new List<string>();
                var 账 = new List<string>();
                for (int i = 0; i < 用例.Length; i++)
                {
                    string 路径 = Path.Combine(根, $"往返{i}.hb");
                    File.WriteAllText(路径, 用例[i].正文, new UTF8Encoding(用例[i].有BOM));
                    byte[] 原 = 读(路径);

                    string 编辑器里 = 分词器C.清洗文本(Encoding.UTF8.GetString(读(路径)));   // ← 打开文件那一趟
                    文本落盘C.写回(路径, 分词器C.清洗文本(编辑器里));                       // ← Ctrl+S 那一趟
                    byte[] 新 = 读(路径);

                    if (!逐字节同(原, 新)) 不一致.Add($"{用例[i].名}({原.Length}→{新.Length} 字节)");
                    账.Add($"{用例[i].名} {原.Length}");
                }
                记录("文本落盘 打开 → 什么不改按 Ctrl+S = 磁盘逐字节不变(六种组合)",
                    不一致.Count == 0,
                    不一致.Count == 0 ? $"全过: {string.Join(" / ", 账)} 字节" : string.Join(" / ", 不一致));

                // ---- ⑤ 旧口径原样重放(把这些数字留档,顺带钉住"这就是本批修的根因") ----
                string 旧源 = Path.Combine(根, "旧源.hb");
                File.WriteAllText(旧源, "// 说明\n输出行(1);\n停止\n\n", new UTF8Encoding(false));
                byte[] 旧原 = 读(旧源);
                string 旧文本 = 分词器C.清洗文本(Encoding.UTF8.GetString(旧原)).TrimEnd('\r', '\n');  // ← C26 之前的 清洗文本 逐字如此
                string 旧出 = Path.Combine(根, "旧口径.hb");
                File.WriteAllText(旧出, 旧文本, Encoding.UTF8);                                      // ← C26 之前 IDE 保存那一行
                byte[] 旧新 = 读(旧出);
                bool 多BOM = 有头(旧新) && !有头(旧原);
                bool 吃尾巴 = 旧文本.EndsWith("\n", StringComparison.Ordinal) == false && 旧文本.Length < 分词器C.清洗文本(Encoding.UTF8.GetString(旧原)).Length;
                记录("文本落盘 旧口径实测:给没 BOM 的文件加 BOM + 吃掉末尾空行(本批修掉的根因)",
                    多BOM && 吃尾巴,
                    $"同一份 {旧原.Length} 字节的文件,旧口径存出来 {旧新.Length} 字节" +
                    $"(BOM {(多BOM ? "长出来了" : "没长")}、末尾空行 {(吃尾巴 ? "被吃掉" : "还在")});" +
                    "新口径 0 字节差(见上一条)");

                // ---- ⑥ 清洗文本 纯函数:末尾空行不动、不可见字符照摘 ----
                记录("文本落盘 清洗文本 不动末尾空行(读进来也不动,写回才可能逐字节一致)",
                    分词器C.清洗文本("内容\n\n") == "内容\n\n"
                    && 分词器C.清洗文本("内容\r\n\r\n") == "内容\r\n\r\n"
                    && 分词器C.清洗文本("内容") == "内容"
                    && 分词器C.清洗文本(null) == "", "读盘 / 写盘共用它,所以两侧都不许在这儿修剪行尾");
                记录("文本落盘 清洗文本 还是摘掉 BOM 字符与零宽字符(编辑器里看不到它们)",
                    分词器C.清洗文本("\uFEFF左\u200B右") == "左右"
                    && 分词器C.清洗文本("\uFEFF") == "",
                    "BOM 那份字符由 文本落盘C.写回 按磁盘原样补回,所以不会写两遍");

                // ---- ⑥-b R8-1:清洗文本 **不许碰空方括号**(用户 2026-09-21 报"IDE 里能编过、命令行却报错") ----
                // 根因 = "删不可见字符"那条老正则 `(?:\p{Cf}|[\p{Cc}&&[^\r\n\t]])+`:那是**支持字符类减法**的
                // 正则方言的写法,而 **.NET 不认识 `&&`** —— 它把类里那个 `[` 当字面量、把第一个 `]` 当成类的结束,
                // 于是整个类变成「{控制字符, &, [, ^, \r, \n, \t} 里一个字符,**后面还必须跟一个字面 `]`**」,
                // `[]` / `&]` / `^]` / `\n]` 被整对吃掉、该摘的控制字符反倒摘不掉。
                // 读盘(`解码文本`)与写盘(`取磁盘文本`)共用 `清洗文本` ⇒ 打开就被吃、保存还写回磁盘;
                // 而"运行→编译"读的是磁盘那份(仍带 `[]`)⇒ 界面上少了一对方括号、编出来却是对的。
                {
                    string 老口径 = System.Text.RegularExpressions.Regex.Replace(
                        "整数 nums[];\n", @"(?:\p{Cf}|[\p{Cc}&&[^\r\n\t]])+", "");
                    string 老口径可见 = 老口径.Replace("\r", "\\r").Replace("\n", "\\n");
                    bool 新口径留住 = 分词器C.清洗文本("整数 nums[];\n") == "整数 nums[];\n"
                        && 分词器C.清洗文本("短整数 分数[];\n") == "短整数 分数[];\n"
                        && 分词器C.清洗文本("整数 表[3] = [1, 2, 3];\n") == "整数 表[3] = [1, 2, 3];\n"
                        && 分词器C.清洗文本("a&]b\n") == "a&]b\n"
                        && 分词器C.清洗文本("左\n]右\n") == "左\n]右\n";
                    记录("文本落盘 清洗文本 只摘不可见字符:空方括号 `[]` / `&]` / 行首 `]` 一个字不动(R8-1 的根)",
                        新口径留住,
                        $"老口径实测:11 字的「整数 nums[];」被吃成 {老口径.Length} 字(「{老口径可见}」);"
                        + "新口径逐字不动(.NET 的正则类里没有 `&&` 求交,老写法得换掉)");

                    记录("文本落盘 带 `[]` 的源码 打开 → 原样写回 = 逐字节不变(R8-1 现场)",
                        分词器C.清洗文本("方法 求和(整数 nums[]) 返回 整数\n{\n    返回 nums[0];\n}\n")
                            == "方法 求和(整数 nums[]) 返回 整数\n{\n    返回 nums[0];\n}\n",
                        "上一节那五种组合是 C26 的验收,这一条补的是 R8-1:数组参数与列表声明的方括号必须活着到编辑器、"
                        + "再原样回到磁盘");
                }

                // ---- ⑦ 界面与三条写回路:按源码断言(自检不开窗口) ----
                string? 示例 = 找示例目录();
                string? 仓根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
                if (仓根 == null)
                {
                    跳过("文本落盘 IDE 保存走唯一落盘点(不再自己拿带 BOM 的 Encoding.UTF8 写)", "工作副本之外跑自检时跳过");
                    跳过("文本落盘 三条写回路共用一个口径(IDE 保存 / 工程维护C.写回 / hbcc --写回)", "工作副本之外跑自检时跳过");
                    跳过("文本落盘 断点表落盘也走同一条", "工作副本之外跑自检时跳过");
                    return;
                }

                string 窗口 = File.ReadAllText(Path.Combine(仓根, "MainWindow.窗口与文件.cs"));
                string 维护 = File.ReadAllText(Path.Combine(仓根, "引擎", "工程维护C.cs"));
                string 命令行 = File.ReadAllText(Path.Combine(仓根, "编译器CLI", "Program.cs"));
                string 断点表 = File.ReadAllText(Path.Combine(仓根, "引擎", "断点表C.cs"));

                记录("文本落盘 IDE 保存走唯一落盘点(不再自己拿带 BOM 的 Encoding.UTF8 写)",
                    窗口.Contains("前端契约C.服务.宿主.写回(路径, 取磁盘文本(页.内容));", StringComparison.Ordinal)
                    && !窗口.Contains("File.WriteAllText(路径, 取磁盘文本(页.内容), Encoding.UTF8);", StringComparison.Ordinal),
                    "落点在 MainWindow.窗口与文件.cs 的 保存标签页(编码头由 文本落盘C 按文件头决定)");

                记录("文本落盘 三条写回路共用一个口径(IDE 保存 / 工程维护C.写回 / hbcc --写回)",
                    维护.Contains("文本落盘C.写回(路径, 文本);", StringComparison.Ordinal)
                    && 命令行.Contains("文本落盘C.写回(名称, 新文本, 有BOM.TryGetValue(名称, out bool b) && b);", StringComparison.Ordinal)
                    && 命令行.Contains("文本落盘C.写回(文件, 新文本, 有BOM.TryGetValue(文件, out bool b) && b);", StringComparison.Ordinal)
                    && !命令行.Contains("new UTF8Encoding(有BOM", StringComparison.Ordinal),
                    "以前三份各写各的 BOM 判定(工程维护C / hbcc 排版 / hbcc 查找替换),IDE 那条路干脆没判");

                记录("文本落盘 断点表落盘也走同一条(不再一律 UTF8Encoding(false))",
                    断点表.Contains("文本落盘C.写回(文件路径(项目目录), 到文本());", StringComparison.Ordinal),
                    "引擎\\断点表C.cs 的 保存:.hbd 在 git 里也不该被翻成另一个样子");

                // ---- ⑧ R8-1:`运行→编译` 这条界面入口也先存盘(与 F5 同一个口径) ----
                // 硬规矩 6:自检不许开窗口 ⇒ 这条只能**按源码钉住入口**(做法与上面三条一样)。
                string 编译界面 = File.ReadAllText(Path.Combine(仓根, "MainWindow.项目与编译.cs"));
                int 入口 = 编译界面.IndexOf("private void 编译启动项目_Click", StringComparison.Ordinal);
                int 存盘 = 入口 < 0 ? -1 : 编译界面.IndexOf("保存项目里改过的页(项目)", 入口, StringComparison.Ordinal);
                int 真编译 = 入口 < 0 ? -1 : 编译界面.IndexOf("汇编多文件带调试(文件表", 入口, StringComparison.Ordinal);
                记录("R8-1 「运行→编译」先存盘再编:屏幕上那份源码就是这次编的那一份(与 F5 同一个口径)",
                    入口 > 0 && 存盘 > 入口 && 真编译 > 存盘,
                    "编译读磁盘、检查读编辑器 —— 两边不一致就是「看着是 A、编出来是 B」;"
                    + "F5(`确保编译过`)本来就先存盘,这条把「运行→编译」这条菜单路补成同一条");

                // ---- ⑨ R8-1:IDE 那条形状(汇编多文件带调试 + 全路径 + 工程归属)对去 `[]` 的源码也报同一句话 ----
                {
                    string 去括号 = "方法 求和(整数 nums) 返回 整数\n{\n    整数 s = 0;\n    整数 i = 0;\n"
                        + "    当 (i < nums.长度())\n    {\n        s = s + nums[i];\n        i = i + 1;\n    }\n    返回 s;\n}\n"
                        + "整数 表[3] = [1, 2, 3];\n测试输出(求和(表));\n停止;\n";
                    int 错行 = 0;
                    string 错文案 = "(没抛)";
                    try
                    {
                        // 文件名给**全路径**、工程归属给一档 —— 与 MainWindow.项目与编译.cs 那次调用同形
                        new 汇编器C().汇编多文件带调试([(Path.Combine(根, "主程序.hb"), 去括号)],
                            优化: true, 堆大小: 中间层C.默认堆大小, 工程归属: ["测试"]);
                    }
                    catch (编译异常C 错) { 错行 = 错.行号 ?? 0; 错文案 = 错.中文; }
                    记录("R8-1 IDE 那条形状(汇编多文件带调试 + 全路径 + 工程归属)编去 `[]` 的源码 = 报同一句话、同一行",
                        错行 == 5 && 错文案.Contains("不是列表/数组,没有 .长度()", StringComparison.Ordinal),
                        $"实测:第 {错行} 行「{错文案}」;命令行 `hbcc` 对同一份文本报的是同一句(第 5 行)");
                }
            }
            catch (Exception ex) { 记录异常("文本落盘自检", ex); }
            finally { try { Directory.Delete(根, true); } catch { } }
        }
    }
}
