using System.IO;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        /// <summary>
        /// C20(用户 2026-09-19:"编译成汇编后在编辑器的新页打开,关闭或者退出时提示是否保存")。
        ///
        /// 规则本体是引擎纯函数 `汇编源码C`(后缀认定 / 落点路径 / 复用哪一页 / 段拼回全文),这里直接喂数比;
        /// 界面那几处(编译成功后开页、"页种类只影响后缀与筛选器"、"关页与退出都问一句"、汇编源码页里不能下断点)
        /// **只能按源码断言** —— 自检不许开窗口(硬规矩 6),与 C12~C19 那几处界面断言同一个办法。
        /// </summary>
        private static void 汇编源码自检()
        {
            try
            {
                // ---- ① 后缀认定 / 页种类 / 落点路径 ----
                string 目录 = Path.Combine(临时区C.基目录(), "hb汇编源码自检");
                记录("汇编源码 后缀认定 .hba 才算(大小写不敏感;空 / 别的后缀都不算)",
                    汇编源码C.是汇编源码(Path.Combine(目录, "主程序.hba"))
                    && 汇编源码C.是汇编源码("主程序.HBA")
                    && !汇编源码C.是汇编源码("主程序.hb")
                    && !汇编源码C.是汇编源码("主程序.hba.bak")
                    && !汇编源码C.是汇编源码(null) && !汇编源码C.是汇编源码("   "),
                    $".hba {(汇编源码C.是汇编源码("主程序.hba") ? "认" : "不认")}、" +
                    $".HBA {(汇编源码C.是汇编源码("主程序.HBA") ? "认" : "不认")}、" +
                    $".hb {(汇编源码C.是汇编源码("主程序.hb") ? "认" : "不认")}(应为 认/认/不认)");

                记录("汇编源码 落点 = 项目目录里的 <名字>.hba(用户拍板 a)",
                    汇编源码C.落点路径(目录, "主程序") == Path.Combine(目录, "主程序.hba")
                    && 汇编源码C.落点路径(目录, "主程序.hb") == Path.Combine(目录, "主程序.hba")
                    && 汇编源码C.落点路径(目录, "主程序.hba") == Path.Combine(目录, "主程序.hba")
                    && 汇编源码C.落点路径("", "主程序") == "主程序.hba",
                    $"目录 + 主程序 → {Path.GetFileName(汇编源码C.落点路径(目录, "主程序"))};" +
                    $".hb 换后缀 → {Path.GetFileName(汇编源码C.落点路径(目录, "主程序.hb"))};" +
                    $"没目录 → {(汇编源码C.落点路径("", "主程序") == "主程序.hba" ? "只给文件名" : 汇编源码C.落点路径("", "主程序"))}");

                记录("汇编源码 落点 名字空给兜底名(别让保存路径变成目录本身)",
                    汇编源码C.落点路径(目录, null) == Path.Combine(目录, "汇编" + 汇编源码C.扩展名)
                    && 汇编源码C.落点路径(目录, "  ") == Path.Combine(目录, "汇编" + 汇编源码C.扩展名)
                    && 汇编源码C.页标题("主程序") == "主程序.hba",
                    $"空名字 → {汇编源码C.页标题(null)};页标题按文件名给");

                // ---- ①b C23:落点按**被展开的那个文件**取 = `<文件名>_ASM.hba` ----
                记录("汇编源码 C23 落点按**被展开文件**取 = <文件名>_ASM.hba(不是项目名)",
                    汇编源码C.落点路径按文件(目录, "主程序.hb") == Path.Combine(目录, "主程序_ASM.hba")
                    && 汇编源码C.落点路径按文件(目录, "主程序") == Path.Combine(目录, "主程序_ASM.hba")
                    && 汇编源码C.落点路径按文件(目录, @"库\字符串.hb") == Path.Combine(目录, "字符串_ASM.hba")
                    && 汇编源码C.落点路径按文件("", "主程序.hb") == "主程序_ASM.hba",
                    $"主程序.hb → {Path.GetFileName(汇编源码C.落点路径按文件(目录, "主程序.hb"))};" +
                    $"子目录里的库 → {Path.GetFileName(汇编源码C.落点路径按文件(目录, @"库\字符串.hb"))}(目录一律用工程目录)");

                记录("汇编源码 C23 落点不叠第二次 _ASM(连点两次按钮不会变成 _ASM_ASM)",
                    汇编源码C.落点路径按文件(目录, "主程序_ASM.hb") == Path.Combine(目录, "主程序_ASM.hba")
                    && 汇编源码C.落点路径按文件(目录, "主程序_ASM.hba") == Path.Combine(目录, "主程序_ASM.hba")
                    && 汇编源码C.落点路径按文件(目录, "主程序_ASM") == Path.Combine(目录, "主程序_ASM.hba")
                    && 汇编源码C.落点路径按文件(目录, null) == Path.Combine(目录, "汇编" + 汇编源码C.扩展名),
                    "同一个文件再展开一次还是**同一页**(查复用页那条路才成立)");

                记录("汇编源码 C23 页头写明是谁生成的(项目级那页 / 工具栏那页分得清)",
                    汇编源码C.页头("主程序.hb", "展开到汇编").Contains("展开到汇编", StringComparison.Ordinal)
                    && 汇编源码C.页头("主程序.hb").Contains("编译(启动项目)", StringComparison.Ordinal)
                    && 汇编源码C.页头("主程序.hb", "展开到汇编").Split('\n').All(行 => 行.StartsWith("//", StringComparison.Ordinal)),
                    "页头第一行点出来源;两条路都还是 `//` 注释(不占字节)");

                记录("汇编源码 页种类按后缀判(打开 .hba 就是汇编源码页)",
                    汇编源码C.判种类(Path.Combine(目录, "主程序.hba")) == 页面种类C.汇编源码
                    && 汇编源码C.判种类(Path.Combine(目录, "主程序.hb")) == 页面种类C.普通
                    && 汇编源码C.判种类(null) == 页面种类C.普通,
                    "普通页与汇编源码页只差这一格:后缀与筛选器;保存/标脏/关页提示都走同一套");

                // ---- ② 复用的是哪一页(同一个项目再编译一次不新开页) ----
                string 目标 = Path.Combine(目录, "主程序.hba");
                记录("汇编源码 同一个项目复用同一页(按全称比,大小写不敏感)",
                    汇编源码C.找复用页(["别的.hb", 目标, "又一个.hb"], 目标.ToUpperInvariant()) == 1
                    && 汇编源码C.找复用页([目标, 目标], 目标) == 0,
                    $"三次查表命中下标 {汇编源码C.找复用页(["别的.hb", 目标, "又一个.hb"], 目标)}");

                记录("汇编源码 没开过就新开(查不到给 -1;空页/空全称不误认)",
                    汇编源码C.找复用页([], 目标) == -1
                    && 汇编源码C.找复用页(["别的.hb", null, "  "], 目标) == -1
                    && 汇编源码C.找复用页([Path.Combine(目录, "工具.hba")], 目标) == -1,
                    "同一个目录里别的 `.hba` 不算复用(按整条路径比,不是按后缀比)");

                // 第193批(用户 2026-09-21 报"同一个文件只能打开一份")——这一格就是那条路的判据。
                // 要点是**同名不同目录算两份**:多文件工程里两个目录各有一个 `主程序.hb`,
                // 要是按文件名认,双击后一个会把前一个的页（还可能带着没保存的改动）顶上来。
                string 别的目录 = Path.Combine(临时区C.基目录(), "hb汇编源码自检_另一个工程");
                记录("标签页 同一个文件只开一份:认的是**整条绝对路径**,同名不同目录算两份",
                    汇编源码C.找复用页([Path.Combine(别的目录, "主程序.hb")], Path.Combine(目录, "主程序.hb")) == -1
                    && 汇编源码C.找复用页([Path.Combine(目录, @"子目录\主程序.hb")], Path.Combine(目录, "主程序.hb")) == -1
                    && 汇编源码C.找复用页([Path.Combine(目录, "主程序.hb")], Path.Combine(目录, "主程序.hb")) == 0,
                    $"同一个文件 → 下标 {汇编源码C.找复用页([Path.Combine(目录, "主程序.hb")], Path.Combine(目录, "主程序.hb"))};" +
                    $"同名不同目录 → {汇编源码C.找复用页([Path.Combine(别的目录, "主程序.hb")], Path.Combine(目录, "主程序.hb"))}(应为 -1)");

                // ---- ③ 各文件段拼回来 = 展开原文(存出来的 .hba 与这一次展开对得上) ----
                var 文件表 = new List<(string 名称, string 内容)>
                {
                    ("主程序.hb", "整数 初值 = 1;\n测试输出(初值);\n停止"),
                    ("工具.hb", "方法 打招呼()\n{\n测试输出(2);\n}\n"),
                };
                string 展开 = 中间层C.展开工程带目录(文件表, 带源行标记: true, 优化: true,
                    堆大小: 中间层C.默认堆大小, 工程归属: null).汇编文本;
                var 分段 = 执行行映射C.扫工程展开(展开, [.. 文件表.Select(x => x.名称)]);
                记录("汇编源码 各文件段拼回来 = 展开原文(逐字节;拼的顺序不许乱)",
                    分段.Count == 文件表.Count && 汇编源码C.拼全工程汇编(分段.Select(段 => 段.展开文本)) == 展开,
                    $"展开 {展开.Length} 字符 / {分段.Count} 段;" +
                    $"拼回 {(汇编源码C.拼全工程汇编(分段.Select(段 => 段.展开文本)) == 展开 ? "一致" : "不一致")}");

                记录("汇编源码 页头是注释(汇编器当注释,不占字节)",
                    汇编源码C.页头("主程序").Split('\n').All(行 => 行.StartsWith("//", StringComparison.Ordinal)),
                    $"页头 {汇编源码C.页头("主程序").Split('\n').Length} 行,行首都是 `//`");

                // ---- ④ `.hba` 默认不进编译(后缀不同 ⇒ 工程文件与目录扫描都收不到它) ----
                string 临时 = Path.Combine(临时区C.基目录(), "hb汇编源码自检_" + Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    Directory.CreateDirectory(临时);
                    File.WriteAllText(Path.Combine(临时, "主程序.hb"), "停止\n");
                    File.WriteAllText(Path.Combine(临时, "工具.hb"), "方法 打招呼()\n{\n返回;\n}\n");
                    File.WriteAllText(Path.Combine(临时, "主程序.hba"), "// 假装这里有一份汇编源码\nHALT\n");

                    var 项目 = new 项目项C { 名称 = "汇编源码测试", 目录 = 临时 };
                    项目.刷新文件();
                    var 收进来的 = 项目.源文件.Select(f => f.名称).ToList();
                    记录("汇编源码 .hba 不进工程:扫目录只收 .hb(默认不参与编译的落地)",
                        收进来的.Contains("主程序.hb") && 收进来的.Contains("工具.hb")
                        && !收进来的.Any(汇编源码C.是汇编源码),
                        $"扫到:{string.Join(", ", 收进来的)}(.hba 一个都不该在里面)");

                    var 生成的 = 工程文件C.从目录生成(临时, "汇编源码测试");
                    记录("汇编源码 .hba 不进工程:从目录生成工程文件也只列 .hb",
                        !生成的.文件.Any(汇编源码C.是汇编源码) && !生成的.资源.Any(汇编源码C.是汇编源码),
                        $"文件表:{string.Join(", ", 生成的.文件)}");

                    // 反过来:同一份汇编源码存成 `.hb` 就会被收进去(证明"挡住的正是后缀",不是别的什么)
                    File.WriteAllText(Path.Combine(临时, "会进工程的.hb"), "停止\n");
                    var 再扫 = new 项目项C { 名称 = "汇编源码测试", 目录 = 临时 };
                    再扫.刷新文件();
                    记录("汇编源码 后缀就是那道闸:同名内容存成 .hb 才会被收进工程",
                        再扫.源文件.Any(f => f.名称 == "会进工程的.hb"),
                        "所以「想手动参与编译」要靠 C22(改工程文件),不是改个后缀就完事");
                }
                finally { try { Directory.Delete(临时, true); } catch { } }

                // ---- ⑤ 界面接线:按源码断言(自检不开窗口) ----
                string? 示例 = 找示例目录();
                string? 根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
                if (根 == null)
                {
                    跳过接线("汇编源码 编译成功后自动开页", "工作副本之外跑自检时跳过");
                    跳过接线("汇编源码 自动编译(执行 / 逐步 / 继续)不翻汇编页(不再抢源码页)", "工作副本之外跑自检时跳过");
                    跳过接线("编辑器 黄条先切回正在执行的那个文件再画(不画在别的页上)", "工作副本之外跑自检时跳过");
                    跳过接线("汇编源码 C23 工具栏「展开到汇编」开新页(与 C20 共用开页那条路)", "工作副本之外跑自检时跳过");
                    跳过接线("汇编源码 页种类只挑后缀与筛选器(保存/关页提示沿用现成那套)", "工作副本之外跑自检时跳过");
                    跳过接线("汇编源码 关页与退出都问一句", "工作副本之外跑自检时跳过");
                    跳过接线("汇编源码 汇编源码页里不能下断点", "工作副本之外跑自检时跳过");
                    return;
                }

                string 编译 = File.ReadAllText(Path.Combine(根, "MainWindow.项目与编译.cs"));
                string 窗口 = File.ReadAllText(Path.Combine(根, "MainWindow.窗口与文件.cs"));
                string 主窗 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml.cs"));
                string 断点 = File.ReadAllText(Path.Combine(根, "MainWindow.调试与断点.cs"));
                string 页模型 = File.ReadAllText(Path.Combine(根, "选卡控件", "选卡页视图模型.cs"));

                记接线("汇编源码 编译成功后自动开页(调用点在「编译成功」那条路上)",
                    编译.Contains("打开汇编源码页(项目);", StringComparison.Ordinal)
                    && 编译.Contains("_上次编译成功 = true;", StringComparison.Ordinal)
                    && 编译.Contains("汇编源码C.找复用页(", StringComparison.Ordinal)
                    && 编译.Contains("MessageBoxButton.YesNo", StringComparison.Ordinal),
                    "编译→开页/复用那一跳在 MainWindow.项目与编译.cs;有未保存改动先问一句");

                // 第160批(用户实测"逐步看着没往前走"查出来的两条)先治了「逐步」那一档;
                // 第161批用户又报"F5 也会多开一个 tabitem" ⇒ 口径统一成:**只有用户自己点
                // 「运行 → 编译」才自动开 `_ASM.hba`**,那四种自动编译一律不开;
                // ② 黄条得画在**正在执行的那个文件**那一页上,所以 `标出当前执行行` 先切页再画(P24 那套)。
                记接线("汇编源码 自动编译(执行 / 逐步 / 继续)与资源管理器右键的「编译」都不翻汇编页(不再抢源码页)",
                    编译.Contains("if (!_自动编译中 && !_不开汇编源码页) 打开汇编源码页(项目);", StringComparison.Ordinal)
                    && 主窗.Contains("private bool _自动编译中;", StringComparison.Ordinal)
                    && 主窗.Contains("_自动编译中 = true;", StringComparison.Ordinal)
                    && 编译.Contains("private bool _不开汇编源码页;", StringComparison.Ordinal)          // 第193批:资源管理器右键那条
                    && !主窗.Contains("_单步里自动编译", StringComparison.Ordinal)
                    && !断点.Contains("_单步里自动编译", StringComparison.Ordinal),
                    "标记只在 确保编译过() 里置(执行 F5 / 逐步 / 继续 都走它);想看展开的汇编:点「运行 → 编译」或「展开到汇编」");
                记接线("编辑器 黄条先切回正在执行的那个文件再画(不画在别的页上)",
                    断点.Contains("if (结果.文件 is { Length: > 0 }) 切到命中文件(结果);", StringComparison.Ordinal),
                    "标出当前执行行 第一句就切页;不然单步看着像没走");

                记接线("汇编源码 C23 工具栏「展开到汇编」开新页(与 C20 共用开页那条路)",
                    编译.Contains("private void 展开到汇编_Click(", StringComparison.Ordinal)
                    && 编译.Contains("汇编源码C.落点路径按文件(项目.目录, 页.全称)", StringComparison.Ordinal)
                    && 编译.Contains("private void 打开汇编源码页(string 目标, string 新文本)", StringComparison.Ordinal)
                    && 编译.Contains("汇编源码C.页头(Path.GetFileName(页.全称), \"展开到汇编\")", StringComparison.Ordinal),
                    "按钮那条路:算落点 → 共用 打开汇编源码页(目标, 新文本)(与 C20 一个字都不重复)");

                记接线("汇编源码 页种类只挑后缀与筛选器(保存 / 标脏 / 关页提示沿用现成那套)",
                    页模型.Contains("public 页面种类C 页种类", StringComparison.Ordinal)
                    && 页模型.Contains("public bool 是汇编源码页", StringComparison.Ordinal)
                    && 窗口.Contains("汇编页 ? 汇编文件筛选 : 文件筛选", StringComparison.Ordinal)
                    && 窗口.Contains("DefaultExt = 汇编页 ? \"hba\" : \"hb\"", StringComparison.Ordinal)
                    && 窗口.Contains("页.页种类 = 汇编源码C.判种类(路径);", StringComparison.Ordinal),
                    "页种类没有引入第二套保存逻辑:只决定后缀/筛选器与打开时的那一格");

                记接线("汇编源码 关页与退出都问一句(汇编源码页算常规页,不落进特殊页)",
                    窗口.Contains("if (!提示保存未保存更改(页)) return false;", StringComparison.Ordinal)
                    && 窗口.Contains("if (!提示保存未保存更改(页)) { e.Cancel = true; return; }", StringComparison.Ordinal)
                    && !页模型.Contains("是否汇编源码页 =>", StringComparison.Ordinal),
                    "关标签页 + 关窗口两条路都在问;`是特殊页` 只认选项页,所以汇编源码页照样被问到");

                记接线("汇编源码 汇编源码页里不能下断点(整篇展开拼起来的,没有源码行对应)",
                    断点.Contains("是汇编源码页: true", StringComparison.Ordinal)
                    && 断点.Contains("不能下断点", StringComparison.Ordinal),
                    "以前那种「红点打上了却永远不响」的观感不留在新页上");
            }
            catch (Exception ex) { 记录异常("汇编源码自检", ex); }
        }

        /// <summary>
        /// C22(用户 2026-09-19:"要参与编译需要手动设置",给"拿汇编优化热路径"用):汇编源码(`.hba`)**手动**参与编译。
        ///
        /// 规则面在引擎(`工程文件C.汇编` 表 / `读全部编译输入` 的顺序 / `工程维护C` 的文本增删),这里**真编真跑**:
        /// <list type="number">
        /// <item>默认(空表)与"根本没有 `汇编` 这一档的老工程文件"编出来**逐字节一致**;</item>
        /// <item>勾上以后那段汇编真的进机器码(产物变长)、程序行为不变(它接在展开结果之后,入口没动);</item>
        /// <item>写进源码里 `调用` 它就真的跑到(输出里看得到);</item>
        /// <item>取消勾(文本去掉那一项)以后产物又变回去(逐字节等于默认那份);</item>
        /// <item>"整篇展开的 `.hba` 和源码一起编"报的是**正式编译错**(以前是"内部错误(请回报)"),文案里有改法。</item>
        /// </list>
        /// 界面那几处(菜单勾 / 写回 `.hbp` / 参与编译的那份不自动覆盖 / 能下断点)按源码断言 —— 自检不许开窗口(硬规矩 6)。
        /// </summary>
        private static void 汇编参与编译自检()
        {
            string 根 = Path.Combine(临时区C.基目录(), "hb汇编参与自检_" + Guid.NewGuid().ToString("N")[..8]);
            try
            {
                // 三条工程放三个目录:纯(汇编表空)/ 参与(列出 .hba)/ 调用(源码里真的调它)
                string 纯目录 = Path.Combine(根, "纯"), 参与目录 = Path.Combine(根, "参与"), 调用目录 = Path.Combine(根, "调用");
                foreach (string d in new[] { 纯目录, 参与目录, 调用目录 }) Directory.CreateDirectory(d);

                const string 普通主程序 = "输出行(\"开始\");\n测试输出(41);\n停止\n";
                const string 打标记 = "热路径_打标记:\n    加载立即数 R5, 60\n    输出字符 R5\n    返回\n";
                const string 调用主程序 = "输出行(\"开始\");\n调用 热路径_打标记\n测试输出(41);\n停止\n";
                foreach (string d in new[] { 纯目录, 参与目录 })
                {
                    File.WriteAllText(Path.Combine(d, "主程序.hb"), 普通主程序);
                    File.WriteAllText(Path.Combine(d, "热路径.hba"), 打标记);
                }
                File.WriteAllText(Path.Combine(调用目录, "主程序.hb"), 调用主程序);
                File.WriteAllText(Path.Combine(调用目录, "热路径.hba"), 打标记);

                // ---- ① 默认(空表)vs 老工程文件(连 `汇编` 这一档都没有):产物逐字节一致 ----
                var 纯工程 = new 工程文件C { 名称 = "纯", 启动文件 = "主程序.hb", 文件 = ["主程序.hb"], 汇编 = [] };
                纯工程.保存(Path.Combine(纯目录, "纯.hbp"));
                // 老格式:连 `汇编` 这一档都没有的 .hbp(C20 那时候的工程文件就长这样)
                File.WriteAllText(Path.Combine(纯目录, "老格式.hbp"),
                    "{\n  \"名称\": \"老\",\n  \"启动文件\": \"主程序.hb\",\n  \"文件\": [\"主程序.hb\"]\n}\n");
                var 老工程 = 工程文件C.加载(Path.Combine(纯目录, "老格式.hbp"));
                记录("汇编参与 老工程文件(没有 `汇编` 那一档)照常打开:表是空的",
                    老工程.汇编.Count == 0 && 老工程.文件.Count == 1,
                    $"汇编表 {老工程.汇编.Count} 项;文件表 {string.Join(",", 老工程.文件)}");

                var 基准 = new 汇编器C().汇编多文件带调试(纯工程.读全部编译输入(), true, 纯工程.堆字节数);
                var 老基准 = new 汇编器C().汇编多文件带调试(老工程.读全部编译输入(), true, 老工程.堆字节数);
                string 纯输出 = 跑一遍取输出(纯工程.读全部编译输入(), 纯工程.堆字节数);
                记录("汇编参与 默认不进编译:产物与老工程文件逐字节一致(空表 = 什么都没变)",
                    基准.机器码.SequenceEqual(老基准.机器码) && 纯工程.汇编.Count == 0,
                    $"机器码 {基准.机器码.Length} 字节,两份一致={基准.机器码.SequenceEqual(老基准.机器码)}");

                // ---- ② 勾上以后:那段汇编真的进机器码,但入口没动(程序行为一字不变) ----
                var 参与工程 = new 工程文件C { 名称 = "参与", 启动文件 = "主程序.hb", 文件 = ["主程序.hb"], 汇编 = ["热路径.hba"] };
                参与工程.保存(Path.Combine(参与目录, "参与.hbp"));
                var 参与 = new 汇编器C().汇编多文件带调试(参与工程.读全部编译输入(), true, 参与工程.堆字节数);
                string 参与输出 = 跑一遍取输出(参与工程.读全部编译输入(), 参与工程.堆字节数);
                记录("汇编参与 勾上以后那段汇编真的进了机器码(接在源码展开之后,入口与行为不变)",
                    参与.机器码.Length > 基准.机器码.Length && 参与输出 == 纯输出,
                    $"默认 {基准.机器码.Length} → 参与 {参与.机器码.Length} 字节;输出两边都是 <{纯输出.Replace("\n", "|")}>");

                // ---- ③ 源码里 `调用` 它:真的跑到(输出里看得到那个字符) ----
                var 调用工程 = new 工程文件C { 名称 = "调用", 启动文件 = "主程序.hb", 文件 = ["主程序.hb"], 汇编 = ["热路径.hba"] };
                调用工程.保存(Path.Combine(调用目录, "调用.hbp"));
                string 调用输出 = 跑一遍取输出(调用工程.读全部编译输入(), 调用工程.堆字节数);
                记录("汇编参与 源码里 `调用 手写例程` 真的跑到那个例程(输出里有它打的字符)",
                    调用输出.Contains('<', StringComparison.Ordinal),
                    $"输出=<{调用输出.Replace("\n", "|")}>");

                // 反过来:把 `汇编` 表那一条拿掉,同一个源码就编不过了 —— 这正是"要手动设置"的意思
                bool 拿掉就编不过;
                string 拿掉后消息;
                try
                {
                    调用工程.汇编.Clear();                        // 同一个工程、同一个源码,只是不再把那份 `.hba` 列进来
                    new 汇编器C().汇编多文件带调试(调用工程.读全部编译输入());
                    拿掉就编不过 = false; 拿掉后消息 = "(居然编过了)";
                }
                catch (Exception 异常) { 拿掉就编不过 = true; 拿掉后消息 = 异常.Message; }
                记录("汇编参与 把 `.hba` 从编译里拿掉,源码里那句 `调用` 就编不过(说得出是哪一行哪个名字)",
                    拿掉就编不过 && 拿掉后消息.Contains("未定义标签", StringComparison.Ordinal),
                    $"拿掉后:{拿掉后消息.Replace("\n", " ")}");

                // ---- ④ 取消勾(文本里去掉那一项)以后产物又变回去 ----
                string 参与文本 = File.ReadAllText(Path.Combine(参与目录, "参与.hbp"));
                string? 去掉后文本 = 工程维护C.工程文本去项(参与文本, 工程维护C.汇编字段, "热路径.hba", out string 去说明);
                工程维护C.写回(Path.Combine(参与目录, "参与.hbp"), 去掉后文本 ?? 参与文本);
                var 去掉后工程 = 工程文件C.加载(Path.Combine(参与目录, "参与.hbp"));
                var 又变回 = new 汇编器C().汇编多文件带调试(去掉后工程.读全部编译输入(), true, 去掉后工程.堆字节数);
                记录("汇编参与 取消勾以后产物又变回去(逐字节等于默认那份)",
                    去掉后工程.汇编.Count == 0 && 又变回.机器码.SequenceEqual(基准.机器码),
                    $"{去说明};汇编表 {去掉后工程.汇编.Count} 项;机器码 {又变回.机器码.Length} 字节");

                // ---- ⑤ `.hbp` 文本增删:只动那一处(注释与手工排版都留着)、再加一次幂等 ----
                string 带注释 = "{\n  // 这是手工写的注释,别洗掉\n  \"名称\": \"注\",\n  \"启动文件\": \"主程序.hb\",\n  \"文件\": [\n    \"主程序.hb\"\n  ]\n}\n";
                string? 加过 = 工程维护C.工程文本加项(带注释, 工程维护C.汇编字段, "热路径.hba", out string 加说明);
                bool 文本可解析 = false;
                string 解析详情 = "";
                try
                {
                    File.WriteAllText(Path.Combine(根, "注.hbp"), 加过 ?? "");
                    var 注工程 = 工程文件C.加载(Path.Combine(根, "注.hbp"));
                    文本可解析 = 注工程.汇编.Count == 1 && 注工程.汇编[0] == "热路径.hba" && 注工程.文件.Count == 1;
                    解析详情 = $"汇编表 [{string.Join(",", 注工程.汇编)}] / 文件表 [{string.Join(",", 注工程.文件)}]";
                }
                catch (Exception 异常) { 解析详情 = 异常.Message; }
                记录("汇编参与 工程文件没有 `汇编` 这一档时,自动现开一个表(只动那一处,注释照留)",
                    (加过 ?? "").Contains("// 这是手工写的注释", StringComparison.Ordinal) && 文本可解析,
                    $"{加说明};注释还在={(加过 ?? "").Contains("// 这是手工写的注释", StringComparison.Ordinal)};{解析详情}");

                string? 再加 = 工程维护C.工程文本加项(加过 ?? "", 工程维护C.汇编字段, "热路径.hba", out string 再说明);
                记录("汇编参与 同一项加两次是幂等的(工程文件一个字不变)",
                    string.Equals(再加, 加过, StringComparison.Ordinal), 再说明);

                // ---- ⑥ "整篇展开的 .hba + 它的源码"报正式编译错(不是"内部错误(请回报)") ----
                string 整篇目录 = Path.Combine(根, "整篇");
                Directory.CreateDirectory(整篇目录);
                var 整篇文件 = new List<(string 名称, string 内容)> { ("主程序.hb", "方法 双倍(整数 值) 返回 整数\n{\n返回 值 * 2;\n}\n测试输出(双倍(3));\n停止\n") };
                string 展开 = 中间层C.展开工程带目录(整篇文件, 带源行标记: true, 优化: true, 堆大小: 中间层C.默认堆大小, 工程归属: null).汇编文本;
                File.WriteAllText(Path.Combine(整篇目录, "主程序.hb"), 整篇文件[0].内容);
                File.WriteAllText(Path.Combine(整篇目录, "主程序.hba"), 展开);
                var 整篇工程 = new 工程文件C { 名称 = "整篇", 启动文件 = "主程序.hb", 文件 = ["主程序.hb"], 汇编 = ["主程序.hba"] };
                整篇工程.保存(Path.Combine(整篇目录, "整篇.hbp"));
                string? 重复消息 = null;
                bool 是正式编译错 = false;
                try { new 汇编器C().汇编多文件带调试(整篇工程.读全部编译输入()); }
                catch (编译异常C 编译错) { 是正式编译错 = true; 重复消息 = 编译错.中文; }
                catch (Exception 异常) { 重复消息 = "不是编译异常:" + 异常.GetType().Name + " " + 异常.Message; }
                记录("汇编参与 整篇展开的 `.hba` 与源码一起编:报正式编译错 + 说清怎么改(以前是「内部错误(请回报)」)",
                    是正式编译错 && (重复消息 ?? "").Contains("标签重复定义", StringComparison.Ordinal)
                    && (重复消息 ?? "").Contains("参与编译", StringComparison.Ordinal),
                    (重复消息 ?? "(没抛)").Replace("\n", " "));

                // ---- ⑦ 界面接线:按源码断言(自检不开窗口) ----
                string? 示例 = 找示例目录();
                string? 仓库根 = 示例 == null ? null : Directory.GetParent(示例)?.FullName;
                if (仓库根 == null)
                {
                    跳过("汇编参与 菜单勾写回工程文件", "工作副本之外跑自检时跳过");
                    跳过("汇编参与 参与编译的那份不自动覆盖", "工作副本之外跑自检时跳过");
                    跳过("汇编参与 参与编译的 `.hba` 能下断点", "工作副本之外跑自检时跳过");
                    return;
                }
                string 项目与编译 = File.ReadAllText(Path.Combine(仓库根, "MainWindow.项目与编译.cs"));
                string 窗口 = File.ReadAllText(Path.Combine(仓库根, "MainWindow.xaml"));
                string 断点 = File.ReadAllText(Path.Combine(仓库根, "MainWindow.调试与断点.cs"));
                string 方案 = File.ReadAllText(Path.Combine(仓库根, "引擎", "解决方案C.cs"));
                记录("汇编参与 菜单一勾就把工程文件里那一项加上/拿掉(文本增删,不洗注释)",
                    窗口.Contains("参与编译菜单", StringComparison.Ordinal)
                    && 项目与编译.Contains("汇编源码参与编译_Click", StringComparison.Ordinal)
                    && 项目与编译.Contains("工程维护C.工程文本加项(工程文本, 工程维护C.汇编字段", StringComparison.Ordinal)
                    && 项目与编译.Contains("工程维护C.工程文本去项(工程文本, 工程维护C.汇编字段", StringComparison.Ordinal)
                    && 方案.Contains("public bool 是汇编", StringComparison.Ordinal)
                    && 方案.Contains("public bool 参与编译(string? 全称)", StringComparison.Ordinal),
                    "菜单项 + 处理函数 + 文本增删 + 工程树那两格,四处在的都在");
                记录("汇编参与 正在参与编译的 `.hba` 编译成功后不自动覆盖(免得冲掉手写内容)",
                    项目与编译.Contains("if (项目.参与编译(目标))", StringComparison.Ordinal)
                    && 项目与编译.Contains("已跳过自动刷新", StringComparison.Ordinal),
                    "C20 那条自动开页路上加的一道闸");
                记录("汇编参与 参与编译的 `.hba` 能下断点(它是真源码),没参与的那份仍挡住",
                    断点.Contains("参与编译(全称) != true", StringComparison.Ordinal)
                    && 断点.Contains("先把它勾成『参与编译』", StringComparison.Ordinal),
                    "同一条路上按「是不是这次编译的输入」分岔");
            }
            catch (Exception ex) { 记录异常("汇编参与编译自检", ex); }
            finally { try { Directory.Delete(根, true); } catch { } }
        }

        /// <summary> 按这份文件表编译并跑完,回程序输出(自检内部用;顺带复用 虚拟机的输出缓冲) </summary>
        private static string 跑一遍取输出(IEnumerable<(string 名称, string 内容)> 文件表, int 堆大小)
        {
            var 编译 = new 汇编器C().汇编多文件带调试(文件表, true, 堆大小);
            var 缓冲 = new System.Text.StringBuilder();
            var vm = new 虚拟机C { 输出目标 = b => 缓冲.Append((char)b) };
            vm.运行(编译);
            return 缓冲.ToString().Replace("\r\n", "\n").TrimEnd('\n');
        }
    }
}
