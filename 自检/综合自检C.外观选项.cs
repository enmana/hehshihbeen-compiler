using System;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// H8(用户 2026-09-24 点名"外观项进选项页"):**缩进导引线**开关 + **花括号 7 档彩虹色**进选项页。
    ///
    /// 要治的是两处"想调只能改代码":`引擎\帮助文本C.cs` 当年写死一句"想关掉只能改代码,暂不进选项页"
    /// (缩进导引线),C16(第146批)对 7 档彩虹色也记着"暂时不进选项页,想调以后接选项页也是现成的"。
    /// 两样都只差"接到选项页 + `选项.json` 存一份 + 自检钉住默认值"。
    ///
    /// 这一组钉的五件事:
    /// ① **默认值一个字没变** —— 7 档没调过时逐档等于引擎 `花括号层号C.色表`,开关默认开;
    /// ② **写坏了不许把整篇彩虹色弄没** —— `规范色表` 对空档 / 认不出的色 / 长度不对的表一律落回默认,
    ///    认得出的一律归一成 `#RRGGBB`(缩进导引线要在色值前面拼透明度,不归一那一刀就切错位);
    /// ③ 选项页那 7 行**从引擎生成**(色值不另抄一份)、勾选与"应用"都写回选项;
    /// ④ 编辑器应用选项时把开关落进渲染器、把 7 档刷子重取 + 重绘(关掉只是不画,不动挂在背景层上的那张表);
    /// ⑤ 帮助正文跟着改口径(不再说"暂不进选项页")。
    ///
    /// **按源码钉**的地方都是界面(硬规矩 6:不许开 GUI 窗口验证;真人手感 ★ 留给用户)——
    /// 而 `配置\选项.cs` / `界面控件\**` 不在命令行自检编译的范围内(CLI 只链 引擎 / 中间层 / 自检 / C后端),
    /// 所以这几条**只能读源码文本**,不能直接 new 出 `选项设置` 来验。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 外观选项自检()
        {
            string? 根 = 找仓库根目录();
            if (根 == null)
            {
                跳过接线("H8 外观选项(导引线开关 + 7 档彩虹色进选项页,按源码钉)", "找不到仓库根(不在工作副本里跑)");
                return;
            }

            // ---- ① 默认值:引擎那 7 档就是默认,且"档名"与色值一一对得上 ----
            const string 默认名 = "外观选项 7 档彩虹色的默认值就是引擎那一套(赤橙黄绿青蓝紫,没调过时逐档相等)";
            try
            {
                var 规范 = 花括号层号C.规范色表(null);
                记录(默认名,
                    花括号层号C.色表.Length == 7 && 花括号层号C.色档名.Length == 7
                    && 花括号层号C.色档名.Length == 花括号层号C.色表.Length
                    && 逐档相同(规范, 花括号层号C.色表)
                    && 花括号层号C.色表.All(色 => 色.Length == 7 && 色[0] == '#'),
                    $"色表 {花括号层号C.色表.Length} 档 / 档名 {花括号层号C.色档名.Length} 个;" +
                    $"空表规范后 = {string.Join(",", 规范)}");
            }
            catch (Exception ex) { 记录异常(默认名, ex); }

            // ---- ② 规范色表:空 / 写坏 / 长度不对落回默认,认得出的一律 #RRGGBB ----
            const string 规范名 = "外观选项 用户色表规范:空 / 写坏 / 表短了都落回默认那一档,认得出的一律归一成 #RRGGBB";
            try
            {
                var 混 = 花括号层号C.规范色表(["#123456", "", "zzz", "#0F0", "#80FF0000"]);
                bool 混对 = 混[0] == "#123456" && 混[1] == 花括号层号C.色表[1] && 混[2] == 花括号层号C.色表[2]
                    && 混[3] == "#00FF00" && 混[4] == "#FF0000" && 混[5] == 花括号层号C.色表[5]
                    && 混[6] == 花括号层号C.色表[6];
                var 短 = 花括号层号C.规范色表(["#010203"]);
                bool 短对 = 短.Length == 7 && 短[0] == "#010203" && 逐档相同([.. 短.Skip(1)], [.. 花括号层号C.色表.Skip(1)]);
                var 全给 = 花括号层号C.规范色表(["#111111", "#222222", "#333333", "#444444", "#555555", "#666666", "#777777"]);
                bool 全给对 = 全给[0] == "#111111" && 全给[6] == "#777777" && 全给.Length == 7;
                记录(规范名, 混对 && 短对 && 全给对,
                    $"混表 → {string.Join(",", 混)};只给一档 → 后 6 档全默认:{短对};7 档全给 → 原样:{全给对}");
            }
            catch (Exception ex) { 记录异常(规范名, ex); }

            // ---- ③ 选项里那两项的默认值 + 老配置兜底(按源码钉:`配置\选项.cs` 不在命令行自检的编译范围里) ----
            const string 默认源名 = "外观选项 选项里的默认值:导引线开关默认开、7 档默认 = 引擎那一套(老配置没这个字段时也补上)";
            try
            {
                string 选项源 = File.ReadAllText(Path.Combine(根, "配置", "选项.cs"));
                记接线(默认源名,
                    选项源.Contains("public bool 缩进导引线 { get; set; } = true;", StringComparison.Ordinal)
                    && 选项源.Contains("public List<string> 花括号色 { get; set; } = [.. 花括号层号C.色表];", StringComparison.Ordinal)
                    && 选项源.Contains("当前.花括号色 ??= [.. 花括号层号C.色表];", StringComparison.Ordinal),
                    "选项.cs:缩进导引线 = true / 花括号色 = [.. 花括号层号C.色表] / 老配置 ??= 同上");
            }
            catch (Exception ex) { 记录异常(默认源名, ex); }

            // ---- ④ 选项页:7 行从引擎生成 + 勾选在那儿(色值不另抄一份) ----
            const string 选项页名 = "外观选项 选项页:7 行按引擎 色表 / 色档名 生成 + 导引线勾选(不手抄一份色值)";
            try
            {
                string 页源 = File.ReadAllText(Path.Combine(根, "界面控件", "选项页.xaml.cs"));
                string 页样 = File.ReadAllText(Path.Combine(根, "界面控件", "选项页.xaml"));
                记接线(选项页名,
                    页源.Contains("花括号层号C.规范色表(选项.花括号色)", StringComparison.Ordinal)
                    && 页源.Contains("花括号层号C.色档名[档]", StringComparison.Ordinal)
                    && 页源.Contains("彩虹色表.ItemsSource = _彩虹色;", StringComparison.Ordinal)
                    && 页样.Contains("x:Name=\"导引线勾\"", StringComparison.Ordinal)
                    && 页样.Contains("x:Name=\"彩虹色表\"", StringComparison.Ordinal),
                    "选项页.xaml.cs 生成 7 行(规范色表 + 色档名);选项页.xaml 有 导引线勾 / 彩虹色表");
            }
            catch (Exception ex) { 记录异常(选项页名, ex); }

            // ---- ⑤ 选项页"应用":两项都写回选项(颜色写盘前再规范一遍) ----
            const string 应用名 = "外观选项 选项页『应用并保存』把开关与 7 档色写回选项(颜色先规范再写盘)";
            try
            {
                string 页源 = File.ReadAllText(Path.Combine(根, "界面控件", "选项页.xaml.cs"));
                记接线(应用名,
                    页源.Contains("导引线勾.IsChecked = 选项.缩进导引线;", StringComparison.Ordinal)
                    && 页源.Contains("选项.缩进导引线 = 导引线勾.IsChecked == true;", StringComparison.Ordinal)
                    && 页源.Contains("选项.花括号色 = [.. 花括号层号C.规范色表([.. _彩虹色.Select(行 => 行.色值)])];", StringComparison.Ordinal),
                    "载入时勾选跟着选项走;应用时开关与 7 档色都写回(规范一遍,存下去的就是真生效的那份)");
            }
            catch (Exception ex) { 记录异常(应用名, ex); }

            // ---- ⑥ 编辑器侧接线:开关落进渲染器 + 7 档刷子重取 + 重绘 ----
            const string 接线名 = "外观选项 编辑器应用选项:开关落进渲染器(关掉只是不画)+ 7 档刷子重取 + 重绘";
            try
            {
                string 外观源 = File.ReadAllText(Path.Combine(根, "界面控件", "编辑器.折叠与外观选项.cs"));
                string 渲染源 = File.ReadAllText(Path.Combine(根, "界面控件", "缩进导引线渲染.cs"));
                string 高亮源 = File.ReadAllText(Path.Combine(根, "界面控件", "语法高亮C.cs"));
                string 初始源 = File.ReadAllText(Path.Combine(根, "界面控件", "编辑器.初始化与交互.cs"));
                记接线(接线名,
                    外观源.Contains("导引线渲染.显示 = 选项.缩进导引线;", StringComparison.Ordinal)
                    && 外观源.Contains("高亮器.刷新花括号色();", StringComparison.Ordinal)
                    && 外观源.Contains("TextArea.TextView.Redraw()", StringComparison.Ordinal)
                    && 渲染源.Contains("if (!显示) return;", StringComparison.Ordinal)
                    && 渲染源.Contains("public bool 显示 { get; set; } = true;", StringComparison.Ordinal)
                    && 高亮源.Contains("花括号层号C.规范色表(配置.选项.当前.花括号色)", StringComparison.Ordinal)
                    && 高亮源.Contains("_花括号色[归一]", StringComparison.Ordinal)
                    && 高亮源.Contains("花括号层号C.导引线透明度", StringComparison.Ordinal)
                    && 初始源.Contains("导引线渲染 = new 缩进导引线渲染(高亮器);", StringComparison.Ordinal)
                    && 初始源.Contains("BackgroundRenderers.Add(导引线渲染)", StringComparison.Ordinal),
                    "渲染器常挂背景层、开关只落一个布尔位;刷子缓存清掉 + Redraw,不然屏上还是老色");
            }
            catch (Exception ex) { 记录异常(接线名, ex); }

            // ---- ⑦ 帮助正文跟着改口径(不再说"暂不进选项页") ----
            const string 帮助名 = "外观选项 帮助正文改口径:导引线能在选项页里关、7 档彩虹色能逐档改";
            try
            {
                string 帮助源 = File.ReadAllText(Path.Combine(根, "引擎", "帮助文本C.cs"));
                记接线(帮助名,
                    帮助源.Contains("选项页《外观与主题》里能关(`缩进导引线`)", StringComparison.Ordinal)
                    && 帮助源.Contains("这 7 档色也在选项页《外观与主题》里逐档改", StringComparison.Ordinal)
                    && !帮助源.Contains("暂不进选项页", StringComparison.Ordinal)
                    && !帮助源.Contains("引擎侧固定", StringComparison.Ordinal),
                    "《配色》与《代码折叠》两处都改成「选项页里能调」;老口径两句话一处不剩");
            }
            catch (Exception ex) { 记录异常(帮助名, ex); }
        }

        /// <summary> 两张色表逐档相等(都按 `#RRGGBB` 比,与 `规范色表` 的输出口径一致) </summary>
        private static bool 逐档相同(string[] 左, string[] 右) =>
            左.Length == 右.Length && 左.Zip(右).All(对 => string.Equals(对.First, 对.Second, StringComparison.OrdinalIgnoreCase));
    }
}
