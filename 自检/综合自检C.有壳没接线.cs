using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// H30(2026-09-24):**清理"有壳没接线"的残留**(`未做任务清单.md` §9 H30)。
    ///
    /// 那一类东西不影响功能,可怕在"**壳在线没接**":起了名 / 建了接口,全仓却没有一处用它 ——
    /// 下一个接手的人照着名字找一圈,白找。本批清单四类,外加顺手扫出来的同一族:
    ///  ① 编辑器区那一格原先挂着一个"显示当前文件名"式的名字,可**没有任何代码更新它**(永远同一句固定文案);
    ///  ② 十个**起了名却全仓无引用**的控件名:5 个菜单项(新建项目 / 打开项目 / 打开文件 / 断点条件 / 全工程替换)
    ///     + 2 个工具栏按钮(新建项目 / 打开项目)+ 控制台「发送」+「内存」「寄存器」两个固定标题;
    ///  ③ `选卡控件` 的四个死接口:`AddTabCommand`(注释说"绑定到 + 按钮",可界面上没有那个按钮)、
    ///     `CloseCommand`(XAML 从不绑,关页走 `Click` 事件)、`被操作页`(只写不读)、
    ///     `修改 / 修改标题 / 修改内容`(没人调,报错串里还留着错别字"此处标题不能**位**null");
    ///  ④ 快捷键表里的「查找」在分发里**没有落点分支**(靠编辑器自己注册的 Ctrl+F 兜底)。
    ///
    /// 这一组为什么**按源码钉**:硬规矩 6 不许开 GUI 窗口验证(界面手感 ★ 留给用户),
    /// 而这一件要看的恰恰是"名字 / 接口跟代码接上没有",于是钉**接线本身**:
    ///  ① 撤掉的名字与接口**一处不剩**(别删一半);② **该留的没被误删**(要按状态开关的菜单还起着名、
    ///  关页那条 Click 路照旧、「查找」仍是"有意不写分支"而不是漏写);③ 顺手项也钉住
    ///  (没有空白页可复用了 ⇒ 载入只剩开新页一条路;`中继命令.cs` 已随命令链删掉);
    ///  ④ 再加一条**通用护栏**:`MainWindow.xaml` 里每个 `x:Name` / `Name`,要么被这份 XAML 自己另外引用
    ///  (`TargetName` / `ElementName` / `PART_` 那类模板部件),要么在别的源码里出现 ——
    ///  "只声明、没人用"当场红灯(H30 这一族就是这么长出来的)。
    ///
    /// **查的是"声明那一行"不是裸名字**:这几个文件的头注释里正解释着"撤了哪几样",
    /// 裸名字当然还看得见 —— 拿裸名字当判据会把自己判红(第一版就是这么栽的)。
    ///
    /// 通用护栏扫文本时跳过 `自检\`:自检自己提一句不算接线(本文件的断言里本来就写着那些名字),
    /// 也跳过 `bin` / `obj`(生成物里每个名字都在,查了等于没查)。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 有壳没接线自检()
        {
            string? 根 = 找仓库根目录();
            if (根 == null)
            {
                跳过接线("H30 MainWindow.xaml 里没有一个『只起名、没人用』的名字(工作副本之外跳过)", "工作副本之外跑自检时正常");
                return;
            }
            try
            {
                string 主窗 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml"));
                string 主窗码 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml.cs"));
                string 窗口 = File.ReadAllText(Path.Combine(根, "MainWindow.窗口与文件.cs"));
                string 主视图 = File.ReadAllText(Path.Combine(根, "选卡控件", "主视图模型.cs"));
                string 页视图 = File.ReadAllText(Path.Combine(根, "选卡控件", "选卡页视图模型.cs"));
                string 键表 = File.ReadAllText(Path.Combine(根, "引擎", "快捷键C.cs"));

                // ---------- ① 编辑器区那一格:名字撤了,文案留着 ----------
                bool 名字没了 = !主窗.Contains("文件名显示区", StringComparison.Ordinal);
                记接线("H30 编辑器区那一格不再挂『显示当前文件名』式的名字(它是固定分区标题,文件名在页头上)",
                    名字没了 && 主窗.Contains("Text=\"源代码 (支持中文指令)\"", StringComparison.Ordinal),
                    名字没了 ? "名字 0 处;那一格仍是写死的分区标题" : "MainWindow.xaml 里又出现了那个名字");

                // ---------- ② 十个"起了名却没人用"的名字一处不剩 ----------
                string[] 撤掉的名字 =
                [
                    "新建项目菜单", "打开项目菜单", "打开文件菜单", "全工程替换菜单", "断点条件菜单",
                    "新建项目按钮", "打开项目按钮", "控制台发送按钮", "内存标题", "寄存器标题",
                ];
                var 还在 = 撤掉的名字.Where(名 =>
                    主窗.Contains($"x:Name=\"{名}\"", StringComparison.Ordinal)
                    || 主窗.Contains($"Name=\"{名}\"", StringComparison.Ordinal)).ToList();
                记接线("H30 十个「起了名却全仓没人用」的控件名一处不剩(5 菜单项 + 2 工具栏按钮 + 发送按钮 + 内存/寄存器标题)",
                    还在.Count == 0,
                    还在.Count == 0 ? "十个名字都不在 MainWindow.xaml 里了(逐个查过 x:Name= / Name=)"
                                    : "又出现了:" + string.Join(" / ", 还在));

                // ---------- ② 的反面:该留的名字没被误删(按状态开关的那几条) ----------
                string[] 留着名字的 =
                [
                    "新建文件菜单", "添加项目菜单", "生成工程文件菜单", "汇编菜单", "执行菜单", "清除断点菜单",
                ];
                var 误删 = 留着名字的.Where(名 => !主窗.Contains($"x:Name=\"{名}\"", StringComparison.Ordinal)).ToList();
                记接线("H30 该留的名字没被误删(`刷新命令可用性()` 按名开 `IsEnabled` 的那几条照旧,起名规矩也写在 XAML 里)",
                    误删.Count == 0 && 主窗.Contains("只给**要按状态开关**的那些起", StringComparison.Ordinal),
                    误删.Count == 0
                        ? "六个按状态开关的名字都在;XAML 里写着『只给要按状态开关的那些起』"
                        : "少了" + string.Join(" / ", 误删));

                // ---------- ③ 选卡控件:四个死接口 + 只为它们存在的那份文件 ----------
                var 残留 = new List<string>();
                if (主视图.Contains("public ICommand", StringComparison.Ordinal)) 残留.Add("主视图模型里还有 ICommand 属性(两个死命令)");
                if (主视图.Contains("选卡页视图模型? 被操作页", StringComparison.Ordinal)) 残留.Add("被操作页 那一格");
                if (主视图.Contains(".CollectionChanged +=", StringComparison.Ordinal)) 残留.Add("集合变化订阅");
                if (主视图.Contains("public void 修改(", StringComparison.Ordinal)
                    || 主视图.Contains("public void 修改标题(", StringComparison.Ordinal)
                    || 主视图.Contains("public void 修改内容(", StringComparison.Ordinal)) 残留.Add("修改 三件");
                if (页视图.Contains("public ICommand CloseCommand", StringComparison.Ordinal)) 残留.Add("选卡页.CloseCommand");
                if (页视图.Contains("using System.Windows.Input;", StringComparison.Ordinal)) 残留.Add("选卡页的 ICommand using");
                if (页视图.Contains("ICommand 关闭命令", StringComparison.Ordinal)) 残留.Add("构造函数又收了 关闭命令");
                bool 中继命令还在 = File.Exists(Path.Combine(根, "选卡控件", "中继命令.cs"));
                记接线("H30 选卡控件:两个死命令 / `被操作页` / `修改` 三件 / `中继命令.cs` 一处不剩",
                    残留.Count == 0 && !中继命令还在,
                    残留.Count == 0 && !中继命令还在
                        ? "主视图模型 与 选卡页 里都不剩;`选卡控件\\中继命令.cs` 这个文件也没了"
                        : $"残留:{string.Join(" / ", 残留)};中继命令.cs {(中继命令还在 ? "还在" : "没了")}");

                // ---------- ④ 关页只有一条路:页头 ✕ 的 Click ----------
                var 关页问题 = new List<string>();
                if (!页视图.Contains("public 选卡页视图模型(string? 标题, string? 内容, string? 全称, bool 是否选项页 = false)", StringComparison.Ordinal))
                    关页问题.Add("页视图的构造函数签名对不上(那一格 关闭命令 没撤干净?)");
                if (主窗.Contains("CloseCommand", StringComparison.Ordinal)) 关页问题.Add("MainWindow.xaml 里出现了 CloseCommand");
                if (!主窗.Contains("Click=\"标签关闭按钮_Click\"", StringComparison.Ordinal)) 关页问题.Add("页头 ✕ 没接到 标签关闭按钮_Click");
                if (!窗口.Contains("private void 标签关闭按钮_Click", StringComparison.Ordinal)) 关页问题.Add("找不到 标签关闭按钮_Click");
                if (!窗口.Contains("尝试关闭标签页(页);", StringComparison.Ordinal)) 关页问题.Add("找不到 尝试关闭标签页(页)");
                记接线("H30 关页只有『页头 ✕ 的 Click』一条路(构造函数不再收 `关闭命令`)",
                    关页问题.Count == 0,
                    关页问题.Count == 0
                        ? "✕ 的 Click → 标签关闭按钮_Click → 尝试关闭标签页,构造函数那一格已撤"
                        : string.Join(" / ", 关页问题));

                // ---------- ⑤ 顺手项:空白页那条复用路随无参 添加() 一起撤了 ----------
                var 载入问题 = new List<string>();
                if (主视图.Contains("public void 添加()", StringComparison.Ordinal)) 载入问题.Add("无参 添加() 还在");
                if (窗口.Contains("t.标题 == null && t.内容 == null", StringComparison.Ordinal)) 载入问题.Add("还在找『没用过的空白页』");
                if (!窗口.Contains("主视图模型.添加(文件名, 内容 ?? \"\", 路径);", StringComparison.Ordinal)) 载入问题.Add("找不到开新页那一句");
                if (!窗口.Contains("页.标记载入(内容);", StringComparison.Ordinal)) 载入问题.Add("载入旗子没立");
                记接线("H30 空白页那条复用路随之撤掉(载入只剩『开新页』一条路,不再找『没用过的空白页』)",
                    载入问题.Count == 0,
                    载入问题.Count == 0
                        ? "无参 添加() 没了;更新标签页内容 里只剩 添加(文件名, 内容, 路径) → 立载入旗 → 记已保存"
                        : string.Join(" / ", 载入问题));

                // ---------- ⑥ 快捷键「查找」:有意不写分支,不是漏写 ----------
                string 分发 = 取方法体(主窗码, "private bool 执行快捷键(string 动作)");
                var 查找问题 = new List<string>();
                if (分发.Length == 0) 查找问题.Add("取不到 执行快捷键 的方法体");
                else
                {
                    if (!分发.Contains("故意不写分支", StringComparison.Ordinal)) 查找问题.Add("分发里没写明『故意不写分支』");
                    if (分发.Contains("case \"查找\"", StringComparison.Ordinal)) 查找问题.Add("分发里冒出了 case 查找");
                }
                if (!键表.Contains("动作 = \"查找\"", StringComparison.Ordinal)) 查找问题.Add("快捷键表里没有『查找』这一项");
                if (!键表.Contains("可改 = false", StringComparison.Ordinal)) 查找问题.Add("『查找』那一项不再是 可改 = false");
                if (!键表.Contains("if (!项.可改) continue;", StringComparison.Ordinal)) 查找问题.Add("动作Of 不再跳过不可改的项");
                记接线("H30 快捷键『查找』是有意不写分支(它 `可改 = false`,分发只派可改的项 ⇒ 根本到不了这里)",
                    查找问题.Count == 0,
                    查找问题.Count == 0
                        ? "分发里没有 `case \"查找\"` 但注释点明了;表里那一项 `可改 = false`,`动作Of` 只派可改的项"
                        : string.Join(" / ", 查找问题));

                // ---------- ⑦ 通用护栏:MainWindow.xaml 里的名字都在别处接上了 ----------
                var 名字表 = new List<string>();
                foreach (Match 一条 in Regex.Matches(主窗, "(?:x:Name|Name)=\"([^\"]+)\""))
                    名字表.Add(一条.Groups[1].Value);
                名字表 = 名字表.Distinct().ToList();
                var 别处 = new StringBuilder();
                收集源码文本(根, 别处);
                string 别处文本 = 别处.ToString();
                var 没人用 = 名字表
                    .Where(名 => 出现次数(主窗, 名) <= 1 && !别处文本.Contains(名, StringComparison.Ordinal))
                    .ToList();
                记接线("H30 MainWindow.xaml 里没有一个『只起名、没人用』的名字(有壳没接线的老毛病)",
                    没人用.Count == 0,
                    没人用.Count == 0
                        ? $"共 {名字表.Count} 个名字:要么在别的源码里接上了,要么是这份 XAML 自己另外引用的模板部件"
                        : "没接上的:" + string.Join(" / ", 没人用) + "(要么在代码里用上它,要么把名字撤掉)");
            }
            catch (Exception ex) { 记录异常("H30 有壳没接线", ex); }
        }

        /// <summary>
        /// 把仓库里**源码文本**攒成一份(`.cs` / `.xaml`),给"H30 通用护栏"查名字用。
        /// 排除:`bin` / `obj`(生成物里每个名字都在,查了等于没查)、`自检`(自检自己提一句不算接线)、
        /// `工具\临时`(探针窝)、`.git` 这类点开头的目录。
        /// </summary>
        private static void 收集源码文本(string 目录, StringBuilder 结果)
        {
            foreach (string 子目录 in Directory.EnumerateDirectories(目录))
            {
                string 名 = Path.GetFileName(子目录);
                if (名 is "bin" or "obj" or "自检" or "临时") continue;
                if (名.StartsWith('.')) continue;
                收集源码文本(子目录, 结果);
            }
            foreach (string 文件 in Directory.EnumerateFiles(目录))
            {
                string 后缀 = Path.GetExtension(文件);
                if (后缀 is ".cs" or ".xaml") 结果.Append(File.ReadAllText(文件));
            }
        }
    }
}
