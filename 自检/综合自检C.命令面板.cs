using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **命令面板**(Ctrl+Shift+P)的验收。用户 2026-10-07 排的七件 IDE 活里的第 1 件
    /// ("吃菜单项 + 帮助话题 + 最近的命令")。
    ///
    /// 这一组钉七件事:
    ///   ① **帮助话题一条不落**:`命令面板表C.列帮助` 收出来的条数 == 帮助站点那棵树里**有正文的叶子**数
    ///      (与 `hbcc --生成帮助` 同一个口径:站点里有多少页,面板里就有多少条);
    ///   ② **每条话题都带得走**:锚点非空、路径非空、标号唯一;点它跳的是 `打开帮助站点(锚点)` 那条现成的路;
    ///   ③ **菜单走的是树、不是抄的表**(接线哨兵):`MainWindow.xaml` 那棵树真起着名,
    ///      `MainWindow.命令面板.cs` 里逐层按标题找回来 + `RaiseEvent(ClickEvent)` —— 走菜单项自己的处理器;
    ///   ④ **排序**:中文前缀 / 中间命中 / 拼音首字母 / 跳字母,四条各真跑一次(与编辑器补全同一套字音缓存);
    ///   ⑤ **最近用过**:记一笔之后它排到最前、次数累加、最多留 12 条、落盘再读回来一致(写的是临时文件,不碰程序目录);
    ///   ⑥ **标号前缀三族互不撞车**:菜单 / 帮助 / 面板各一套前缀,"菜单改个标题"与"帮助换个锚点"互不牵连;
    ///   ⑦ **快捷键接线**:表里默认 `Ctrl+Shift+P`、分发里有落点、菜单项挂在视图菜单上、XAML 里起名 (`x:Name="主菜单"`)。
    ///
    /// 界面那一半(面板真弹出来、↑↓ 真换选中)自检**不开窗**(硬规矩 6),那些按源码钉。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 命令面板自检()
        {
            // ---- ① 帮助话题:与站点同一套叶子口径 ----
            const string 帮助条数名 = "命令面板 帮助话题一条不落(列帮助 == 帮助站点那棵树里有正文的叶子数)";
            List<命令项C> 帮助条 = [];
            帮助节点C? 树 = null;
            try
            {
                树 = 帮助站点C.建树();
                帮助条 = 命令面板表C.列帮助(树);
                var 叶子 = 帮助站点C.取叶子(树);
                记录(帮助条数名,
                    帮助条.Count > 0 && 帮助条.Count == 叶子.Count,
                    $"列帮助 {帮助条.Count} 条 / 站点叶子 {叶子.Count} 条 / 站点节点 {帮助站点C.节点数(树)} 个");
            }
            catch (Exception ex) { 记录异常(帮助条数名, ex); }

            // ---- ② 每条话题都带得走:锚点 / 路径 / 标号 ----
            const string 帮助齐名 = "命令面板 帮助话题每条都带锚点与路径,标号唯一(点它就能跳到站点那一页)";
            try
            {
                bool 齐 = 帮助条.All(x => x.来源 == 命令来源C.帮助 && x.锚点.Length > 0 && x.路径.Length > 0 && x.命令.Length > 0);
                bool 缺页 = 帮助条.Any(x => 帮助站点C.按锚点找(树!, x.锚点) == null);
                bool 标号唯一 = 帮助条.Select(x => x.标号).Distinct(StringComparer.Ordinal).Count() == 帮助条.Count;
                记录(帮助齐名, 齐 && !缺页 && 标号唯一,
                    $"锚点/路径/命令名都非空 = {齐};锚点在树上找得回 = {!缺页};标号唯一 = {标号唯一};例:{帮助条.FirstOrDefault()?.路径 ?? "(空)"}");
            }
            catch (Exception ex) { 记录异常(帮助齐名, ex); }

            // ---- ③ 菜单走的是树(接线哨兵) ----
            const string 菜单树名 = "命令面板 菜单候选是现场走树来的(不是另抄一张表),执行走菜单项自己的 Click";
            string? 根 = 找仓库根目录();
            if (根 == null) 跳过接线(菜单树名, "工作副本之外跑自检,读不到 MainWindow.xaml / MainWindow.命令面板.cs");
            else
            {
                try
                {
                    string 主窗 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml"));
                    string 面板源 = File.ReadAllText(Path.Combine(根, "MainWindow.命令面板.cs"));
                    记接线(菜单树名,
                        主窗.Contains("<Menu x:Name=\"主菜单\"", StringComparison.Ordinal)
                        && 主窗.Contains("Click=\"命令面板_Click\"", StringComparison.Ordinal)
                        && 主窗.Contains("x:Name=\"命令面板菜单\"", StringComparison.Ordinal)
                        && 面板源.Contains("走菜单树(主菜单)", StringComparison.Ordinal)
                        && 面板源.Contains("按路径找(主菜单, 条.菜单之路)", StringComparison.Ordinal)
                        && 面板源.Contains("MenuItem.ClickEvent", StringComparison.Ordinal)
                        && 面板源.Contains("命令面板表C.列帮助(帮助站点C.建树())", StringComparison.Ordinal),
                        "菜单树起了名(`主菜单`)→ 走树收叶子 → 按路径找回来 → RaiseEvent(Click);帮助走 `列帮助(建树())`");
                }
                catch (Exception ex) { 记录异常(菜单树名, ex); }
            }

            // ---- ④ 排序:逐字 > 前缀 > 位置靠前的中间命中 > 拼音首字母 > 跳字母;不沾的一条都不给 ----
            // 池子里故意放"切换断点"(命中在第 0 列)与"清除所有断点"(命中在后面)两条:
            // 同样是"中间命中",位置靠前的要排在前面(打分里的 `命中位置 / 16` 那一项就是干这个的)。
            //
            // **跳字母那一档的口径(实测过,别按直觉写断言)**:两条拼音路子覆盖的字不一样 ——
            //   ① **拼音首字母**是运行期用 `ToolGood.Words.FirstPinyin` 现算的 ⇒ **任何词都算得出来**
            //      (`qhd` → `切换断点`);
            //   ② **全拼变体**(跳字母靠它)读的是内置的 `字音表C`,那张表**只覆盖补全词表里出现过的 107 个字**
            //      ⇒ 表外那些字所在的词**一个变体都给不出来**(实测:`打开文件` / `命令面板` / `帮助关于` 都是空)。
            // 所以跳字母的样本要用**表里认得的词**(`打开文件` 走不了这一档,`长度` 可以)。
            const string 排序名 = "命令面板 排序:逐字 > 前缀 > 位置靠前的中间命中 > 拼音首字母 > 跳字母(不沾的一条都不给)";
            try
            {
                var 池 = new List<命令项C>
                {
                    条("转到定义"), 条("切换断点"), 条("长度"), 条("清除所有断点"), 条("命令面板"),
                    条("在控制台窗口里跑"), 条("帮助关于"),
                };
                var 模型 = new 命令面板模型C(池, new 命令面板表C());
                var 定 = 模型.匹配("转到定义");
                var 断 = 模型.匹配("断点");
                var 首 = 模型.匹配("qhd");           // 切换断点(拼音首字母 QHDD 的前三个)
                var 跳 = 模型.匹配("cdg");           // 长度(全拼 CHANGDU:跳字母档至少要 3 个字母)
                var 全 = 模型.匹配("cangdu");        // 长度(模糊拼音:CHANGDU → CANGDU)
                var 前 = 模型.匹配("转到");           // 转到定义(中文前缀)
                var 无 = 模型.匹配("zzzz");
                记录(排序名,
                    定.Count > 0 && 定[0].命令 == "转到定义"
                    && 断.Count == 2 && 断[0].命令 == "切换断点" && 断[1].命令 == "清除所有断点"
                    && 前.Count > 0 && 前[0].命令 == "转到定义"
                    && 首.Count > 0 && 首[0].命令 == "切换断点"
                    && 全.Count > 0 && 全[0].命令 == "长度"
                    && 跳.Count > 0 && 跳[0].命令 == "长度"
                    && 无.Count == 0,
                    $"逐字 → {定.FirstOrDefault()?.命令};『断点』(位置靠前的在前)→ {string.Join(",", 断.Select(x => x.命令))};" +
                    $"『转到』→ {前.FirstOrDefault()?.命令};qhd → {首.FirstOrDefault()?.命令};" +
                    $"cangdu(模糊全拼)→ {全.FirstOrDefault()?.命令};cdg(跳字母)→ {跳.FirstOrDefault()?.命令};zzzz → {无.Count} 条");

                static 命令项C 条(string 名) => new() { 命令 = 名, 路径 = "测试 / " + 名, 标号 = "菜单\u0001测试\u0001" + 名 };
            }
            catch (Exception ex) { 记录异常(排序名, ex); }

            // ---- ⑤ 最近用过:排到最前 / 次数累加 / 上限 / 落盘读回一致 ----
            // **次数那两条在"记满 22 笔"之前量**(记满会把早先那几条挤出历史 —— 上限之外的就该没了);
            // 报详情的时候也只用这两条,别拿被挤掉之后的次数去打印(那会把一条本来就对的断言印成 0)。
            const string 历史名 = "命令面板 最近用过:记一笔就排到最前、次数累加、最多留 12 条、落盘读回一致";
            try
            {
                命令面板表C 新表() => 命令面板表C.从文本("{}");
                var 表 = 新表();
                var 池 = Enumerable.Range(1, 20).Select(i => new 命令项C
                {
                    命令 = $"命令{i:00}",
                    路径 = $"测试 / 命令{i:00}",
                    标号 = $"菜单\u0001测试\u0001命令{i:00}",
                }).ToList();
                var 模型 = new 命令面板模型C(池, 表);
                表.记用过(池[7].标号);
                表.记用过(池[7].标号);
                表.记用过(池[2].标号);
                int 次七 = 表.次数(池[7].标号), 次二 = 表.次数(池[2].标号);
                bool 次数对 = 次七 == 2 && 次二 == 1 && 表.次数("没有这一条") == 0;
                var 空查 = 模型.匹配("");
                bool 最前 = 空查.Count > 0 && 空查[0].命令 == "命令03";     // 最后一次用过的排最前
                string 第一条 = 空查.FirstOrDefault()?.命令 ?? "(空)";
                for (int i = 0; i < 20; i++) 表.记用过($"菜单\u0001测试\u0001第{i}轮");
                bool 上限 = 表.历史.Count == 命令面板表C.最多历史;

                string 文件 = Path.Combine(临时区C.取目录("命令面板"), "命令历史.json");
                表.保存(文件);
                var 读回 = 命令面板表C.加载(文件);
                bool 往返 = 读回.历史.Count == 表.历史.Count
                    && 读回.最近标号().SequenceEqual(表.最近标号(), StringComparer.Ordinal)
                    && File.ReadAllText(文件).Contains("最后用过", StringComparison.Ordinal);

                记录(历史名, 次数对 && 最前 && 上限 && 往返,
                    $"同一标号记两次 → 次数 {次七};另一条 → {次二};空查询第一条 = {第一条};" +
                    $"记 22 笔之后历史 {表.历史.Count} 条(上限 {命令面板表C.最多历史});落盘读回一致 = {往返}");
            }
            catch (Exception ex) { 记录异常(历史名, ex); }

            // ---- ⑥ 三族标号前缀互不撞车 ----
            const string 标号名 = "命令面板 三族标号前缀分开(菜单 / 帮助 / 面板):同一个样子每次一样,三族之间不相等";
            try
            {
                string 菜单 = 命令项C.菜单标号(["文件", "新建项目…"]);
                string 帮助 = 命令项C.帮助标号(帮助站点C.类型节名);
                bool 分开 = 菜单.StartsWith("菜单\u0001", StringComparison.Ordinal)
                    && 帮助.StartsWith("帮助\u0001", StringComparison.Ordinal)
                    && 命令项C.面板标号.StartsWith("面板\u0001", StringComparison.Ordinal)
                    && 菜单 != 帮助 && 菜单 != 命令项C.面板标号 && 帮助 != 命令项C.面板标号
                    && 命令项C.菜单标号(["a", "b", "c"]) == 命令项C.菜单标号(["a", "b", "c"])
                    && 命令项C.菜单标号(["a", "b"]) != 命令项C.菜单标号(["a", "b", "c"]);
                记录(标号名, 分开,
                    $"菜单 `{菜单.Replace("\u0001", "|")}` / 帮助 `{帮助.Replace("\u0001", "|")}` / " +
                    $"面板 `{命令项C.面板标号.Replace("\u0001", "|")}`;同一个菜单之路两次算出来一样");
            }
            catch (Exception ex) { 记录异常(标号名, ex); }

            // ---- ⑦ 快捷键与菜单接线(接线哨兵) ----
            const string 键名 = "命令面板 快捷键接线:Ctrl+Shift+P = 表里那一条 + 分发有落点 + 视图菜单上挂着";
            if (根 == null) 跳过接线(键名, "工作副本之外跑自检,读不到源码");
            else
            {
                try
                {
                    string 键表 = File.ReadAllText(Path.Combine(根, "引擎", "快捷键C.cs"));
                    string 主窗码 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml.cs"));
                    string 主窗 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml"));
                    记接线(键名,
                        快捷键C.默认键("命令面板") == "Ctrl+Shift+P"
                        && 快捷键C.动作Of("P", true, true, false) == "命令面板"
                        && 键表.Contains("动作 = \"命令面板\", 默认键 = \"Ctrl+Shift+P\"", StringComparison.Ordinal)
                        && 主窗码.Contains("case \"命令面板\": 打开命令面板(); return true;", StringComparison.Ordinal)
                        && 主窗码.Contains("命令面板菜单.InputGestureText = 取(\"命令面板\");", StringComparison.Ordinal)
                        && 主窗.Contains("Header=\"命令面板(_P)…\"", StringComparison.Ordinal),
                        $"默认键 {快捷键C.默认键("命令面板")};分发 {(快捷键C.动作Of("P", true, true, false) ?? "(空)")};" +
                        "表 / 分发 / 菜单键位提示 / 视图菜单项四处都接上了");
                }
                catch (Exception ex) { 记录异常(键名, ex); }
            }
        }
    }
}
