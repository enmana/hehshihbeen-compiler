using hehshihbeen_compiler.界面控件;
using hehshihbeen_compiler.配置;
using System.Windows;
using System.Windows.Controls;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 主窗口的一部分:**命令面板**(Ctrl+Shift+P / 视图 → 命令面板)。
    ///
    /// 半边在引擎(`引擎\命令面板C.cs`:候选模型 / 排序 / 最近用过),半边在这里:
    ///   · **走菜单树**把菜单上真挂着的项一条条收起来(见 `加上菜单项`)——
    ///     这就是"菜单加一项,面板里自动就有"的那条路,别处不许再抄一份命令表;
    ///   · 把**帮助站点的话题**也当成命令(`帮助站点C.建树` → 叶子,与 `hbcc --生成帮助` 同源);
    ///   · 选中之后**按那条路在菜单树上找回来**,`RaiseEvent(Click)` 走菜单项自己的处理器 ——
    ///     于是"面板里点一下"与"用户自己点菜单"走的是**同一条**代码路径(不会两处行为分叉)。
    ///
    /// 为什么菜单走树、不在这儿列一张表:抄一张表就多一个事实源 —— 菜单改名/加项之后两边必然打脸。
    /// 自检那条"菜单项一条不落地进得了面板"钉的就是这个(它真去走一遍这棵树)。
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 命令面板:走菜单树 + 帮助话题

        /// <summary> 菜单树里一项的说明文本(字符串 / 控件都可能有;取不到给空串)</summary>
        private static string 取说明(MenuItem 项) => 项.ToolTip switch
        {
            string 文 => 文,
            TextBlock 块 => 块.Text,
            _ => "",
        };

        /// <summary>
        /// 现场走一遍菜单树,把**每一片叶子**(最里面那一层菜单项)收成一条命令。
        /// 菜单项自己的标题原样进 `菜单之路`(查找时就按这串字对);命令名去掉助记符 `(_N)` 与结尾的 `…`。
        /// 分隔符、没标题的项、`IsCheckable` 那种开关**照收**(开关也是一条命令,点一下就是切它)。
        /// </summary>
        private static List<命令项C> 走菜单树(Menu 菜单)
        {
            var 结果 = new List<命令项C>();
            foreach (var 顶层 in 菜单.Items.OfType<MenuItem>())
                加项(顶层, new List<string>());
            return 结果;

            void 加项(MenuItem 项, List<string> 父之路)
            {
                var 这里 = new List<string>(父之路);
                if (项.Header is string 头 && 头.Length > 0) 这里.Add(头);
                var 子菜单项 = 项.Items.OfType<MenuItem>().ToList();
                if (子菜单项.Count > 0)
                {
                    foreach (var 子 in 子菜单项) 加项(子, 这里);
                    return;
                }
                if (这里.Count == 0) return;
                string 名 = 干净标题(这里[^1]);
                if (名.Length == 0) return;
                结果.Add(new 命令项C
                {
                    命令 = 名,
                    来源 = 命令来源C.菜单,
                    路径 = string.Join(" / ", 这里.Select(干净标题)),
                    标号 = 命令项C.菜单标号(这里),
                    菜单之路 = 这里,
                    键位 = 项.InputGestureText ?? "",
                    提示 = 取说明(项),
                });
            }
        }

        /// <summary> 菜单标题 → 命令名:去掉助记符 `(_N)` / 结尾的 `…`(省略号说明"会再问你一句") </summary>
        private static string 干净标题(string 头) =>
            System.Text.RegularExpressions.Regex.Replace(头 ?? "", @"\(_\w\)", "").Trim().TrimEnd('…', '.').Trim();

        /// <summary>
        /// 按 `菜单之路` 把那一项在树上找回来(逐层按标题对;找不到返回 null —— 菜单变了就别瞎执行)。
        /// 找到了再 `RaiseEvent(Click)`:走的是**菜单项自己的处理器**,与用户亲手点一下完全同路。
        /// </summary>
        private static MenuItem? 按路径找(Menu 根, IReadOnlyList<string> 之路)
        {
            ItemsControl? 现在 = 根;
            MenuItem? 找到 = null;
            foreach (string 段 in 之路)
            {
                var 下一个 = 现在?.Items.OfType<MenuItem>()
                    .FirstOrDefault(x => x.Header is string 头 && string.Equals(头, 段, StringComparison.Ordinal));
                if (下一个 == null) return null;
                找到 = 下一个;
                现在 = 下一个;
            }
            return 找到;
        }

        /// <summary> 面板自带的那几条(菜单上没有、但找起来顺手:撤销/重做/复制这一族) </summary>
        private static List<命令项C> 面板自带命令()
        {
            命令项C 条(string 名, string 键位, string 提示) => new()
            {
                命令 = 名,
                来源 = 命令来源C.面板,
                路径 = "面板 / " + 名,
                标号 = 命令项C.面板标号 + "\u0001" + 名,
                键位 = 键位,
                提示 = 提示,
            };
            return
            [
                条("撤销", "", "撤掉编辑器里最后一次改动(菜单『编辑 → 撤销』同一条路)"),
                条("重做", "", "把刚撤销的那一次改回来"),
                条("复制", "", "把选中的文字复制到剪贴板"),
                条("粘贴", "", "把剪贴板里的文字粘到光标处"),
                条("删除", "", "删掉选中的文字"),
                条("全选", "", "选中当前文件全部文字"),
            ];
        }

        /// <summary> 建一份候选表:菜单树(现场走)+ 面板自带的 + 帮助话题(与帮助站点同源) </summary>
        private 命令面板模型C 建命令面板模型()
        {
            var 候选 = new List<命令项C>();
            try { 候选.AddRange(走菜单树(主菜单)); } catch (Exception 异常) { 异常收集C.记($"命令面板:走菜单树失败: {异常.Message}"); }
            候选.AddRange(面板自带命令());
            try { 候选.AddRange(命令面板表C.列帮助(帮助站点C.建树())); }
            catch (Exception 异常) { 异常收集C.记($"命令面板:收帮助话题失败: {异常.Message}"); }
            return new 命令面板模型C(候选) { 模糊规则 = 选项.当前.模糊拼音规则 };
        }

        /// <summary> 视图 → 命令面板(默认键 Ctrl+Shift+P) </summary>
        private void 命令面板_Click(object sender, RoutedEventArgs e) => 打开命令面板();

        private void 打开命令面板()
        {
            try
            {
                var 模型 = 建命令面板模型();
                var 窗 = new 命令面板窗口(模型, 执行面板命令) { Owner = this };
                窗.ShowDialog();       // 模态:面板开着的时候别让底下的菜单被点到
                刷新命令可用性();      // 面板里执行过的动作可能改了界面状态(开关/断点/可用性),收尾对一次
            }
            catch (Exception 异常)
            {
                异常收集C.记($"打开命令面板失败: {异常.Message}");
                记异常框($"打开命令面板失败: {异常}");
            }
        }

        /// <summary>
        /// 面板选中一条之后干什么。三条路(与 `命令项C.来源` 一一对应):
        ///   ① **菜单**:按路径在树上找回来 → `RaiseEvent(Click)`(同一条代码路径);
        ///   ② **面板**:那几条只有编辑器有的动作(撤销/重做/复制/粘贴/删除/全选)直接转给当前编辑器;
        ///   ③ **帮助**:锚点交给 `打开帮助站点`(与帮助菜单同一条路)。
        /// 顺手把"最近用过"记一笔(由 `命令面板窗口` 在关窗之前调,见那边)。
        /// </summary>
        private void 执行面板命令(命令项C 条)
        {
            switch (条.来源)
            {
                case 命令来源C.帮助:
                    打开帮助站点(条.锚点);
                    return;

                case 命令来源C.面板:
                    执行面板自带(条.命令);
                    return;

                default:
                    var 项 = 按路径找(主菜单, 条.菜单之路);
                    if (项 == null)
                    {
                        输出框.AppendText($"这条命令在菜单里已经找不到了:`{条.路径}`(菜单改过了?)\n");
                        切到底部输出();
                        return;
                    }
                    项.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    return;
            }
        }

        /// <summary> 面板自带那几条的落点(全在当前编辑器上;没有打开的文件就什么也不做) </summary>
        private void 执行面板自带(string 名)
        {
            var 编 = 当前编辑器;
            if (编 == null) return;
            switch (名)
            {
                case "撤销": 撤销按钮_Click(this, new RoutedEventArgs()); return;
                case "重做": 重做按钮_Click(this, new RoutedEventArgs()); return;
                case "复制": 编.执行复制(); return;
                case "粘贴": 编.执行粘贴(); return;
                case "删除": 编.执行删除(); return;
                case "全选": 编.执行全选(); return;
            }
        }

        #endregion
    }
}
