using hehshihbeen_compiler.选卡控件;
using hehshihbeen_compiler.配置;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace hehshihbeen_compiler
{
    /// <summary> 主窗口的一部分:问题表复制 / 编辑:重命名(跨文件改名) </summary>
    public partial class MainWindow : Window
    {
        #region 问题表复制
        // 把一行拼成 Tab 分隔的字符串
        private static string 行转文本(object item)
        {
            // 用 dynamic 或反射都行,这里用 dynamic 最省事
            dynamic d = item;
            return $"{d.级别}\t{d.文件}\t{d.行}\t{d.文本}";
        }

        // Ctrl+C 复制选中行
        private void 问题表_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                复制选中行();
                e.Handled = true;
            }
        }

        private void 问题表_复制_Click(object sender, RoutedEventArgs e)
        {
            复制选中行();
        }

        private void 问题表_复制全部_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            foreach (var item in 问题表.Items)
                sb.AppendLine(行转文本(item));

            if (sb.Length > 0)
                Clipboard.SetText(sb.ToString());
        }

        private void 复制选中行()
        {
            var sb = new StringBuilder();
            foreach (var item in 问题表.SelectedItems)
                sb.AppendLine(行转文本(item));

            if (sb.Length > 0)
                Clipboard.SetText(sb.ToString());
        }
        #endregion

        #region P38:问题表 → 一键修复

        // ---- 问题表的一键修复(P38) ----
        //
        // 口径(用户排的第 5 件:"从『报错』到『改好』的那一步"):
        //   · 方案由引擎算(`引擎\一键修复C.cs`,纯函数;**十条最常见的**里,
        //     "改法唯一且稳"的才给方案,拿不准的只插一行 TODO,**绝不替用户删代码**);
        //   · 应用之前**先给预览**(改前 / 改后那一行),确认了才落到编辑器里;
        //   · 落下去走 `页.内容` 那条路(编辑器同步 + 页面标脏 + 一个撤销单元),**不自动存盘**;
        //   · 修完立刻重查一遍(问题页跟着更新,"这条没了"看得见)。

        /// <summary> 问题表里选中的那一条 → 它的修复方案(没有就给空列表)</summary>
        private List<修复方案C> 取这一条的修复(问题行C 行)
        {
            try
            {
                if (_问题诊断.TryGetValue(行, out var 诊断)) return 一键修复C.算(诊断, 行.源码);
            }
            catch (Exception 异常) { 异常收集C.记($"算一键修复失败: {异常.Message}"); }
            return [];
        }

        /// <summary> 问题表选中行的"修这一条":算方案 → 预览 → 应用</summary>
        private void 问题表_一键修复_Click(object sender, RoutedEventArgs e) => 修选中的那一条();

        private void 修选中的那一条()
        {
            try
            {
                if (问题表.SelectedItem is not 问题行C 行)
                {
                    输出框.AppendText("先在问题表里点一条(或者右键点一条),再点『修这一条』。\n");
                    切到底部输出();
                    return;
                }
                var 方案们 = 取这一条的修复(行);
                if (方案们.Count == 0)
                {
                    输出框.AppendText($"`{行.文本}` 这一条没有一键修复 —— 它要你自己拿主意(改法不唯一)。\n");
                    切到底部输出();
                    return;
                }
                var 方案 = 方案们[0];

                // 预览:把要动的那几行"改前 / 改后"并排打出来(与替换/重命名同一套"先看后改"的口径)
                string 新文本 = 一键修复C.应用(行.源码, 方案.改动) ?? "";
                if (新文本.Length == 0 && 行.源码.Length > 0)
                {
                    输出框.AppendText("这条修复算不出改后的文本,没有动手。\n");
                    切到底部输出();
                    return;
                }
                var 预览 = new StringBuilder();
                预览.AppendLine($"一键修复:`{方案.名称}` —— {方案.说明}");
                预览.AppendLine($"文件:{Path.GetFileName(行.全称)} 第 {行.行} 行");
                var 旧行 = 一键修复C.切行(行.源码);
                var 新行 = 一键修复C.切行(新文本);
                int 从 = Math.Max(0, 行.行 - 2), 到 = Math.Min(旧行.Length, 行.行 + 1);
                for (int i = 从; i < 到; i++)
                {
                    string 旧行文本 = i < 旧行.Length ? 旧行[i] : "";
                    string 新行文本 = i < 新行.Length ? 新行[i] : "";
                    if (旧行文本 == 新行文本) { 预览.AppendLine($"    {i + 1}| {旧行文本}"); continue; }
                    预览.AppendLine($"  - {i + 1}| {旧行文本}");
                    预览.AppendLine($"  + {i + 1}| {新行文本}");
                }
                预览.AppendLine("(改的是编辑器里的文本,**没有存盘**;存不存按 Ctrl+S 自己定)");
                输出框.AppendText(预览.ToString());
                切到底部输出();

                if (MessageBox.Show($"要应用这个修复吗?\n\n{方案.名称}\n{方案.说明}\n\n" +
                        "改的是编辑器里的文本(不自动存盘);改完可以按 Ctrl+Z 撤回。",
                        "一键修复", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                {
                    输出框.AppendText("已取消(一个字符都没改)。\n");
                    return;
                }

                var 页 = 找标签页(行.全称);
                if (页 == null)
                {
                    输出框.AppendText($"`{Path.GetFileName(行.全称)}` 不在打开的标签页里,改不了 —— 先双击这条问题把它打开。\n");
                    切到底部输出();
                    return;
                }
                页.内容 = 新文本;          // 走依赖属性:编辑器同步 + 页面标脏 + 一个撤销单元
                输出框.AppendText($"已应用:`{方案.名称}`。改的是 {页.显示标题};按 Ctrl+S 存盘。\n");
                安排检查();                // 400ms 后重查一遍:这条问题该没了(改坏了也马上看得出来)
            }
            catch (Exception 异常)
            {
                异常收集C.记($"一键修复失败: {异常.Message}");
                记异常框($"一键修复失败: {异常}");
            }
        }

        /// <summary> 问题表右键菜单弹出来之前:把"修这一条"按有没有方案开关掉(没有就变灰,不假装有)</summary>
        private void 问题表_右键(object sender, ContextMenuEventArgs e)
        {
            try
            {
                bool 可修 = 问题表.SelectedItem is 问题行C 行 && 取这一条的修复(行).Count > 0;
                菜单修这一条.IsEnabled = 可修;
                菜单修这一条.Header = 可修 && 问题表.SelectedItem is 问题行C 行2
                    ? $"修这一条({取这一条的修复(行2)[0].名称})"
                    : "修这一条(这一条没有一键修复)";
            }
            catch { }
        }

        #endregion

        #region 编辑:重命名(跨文件改名;引擎侧只算"编辑清单",见 `引擎\重命名C.cs`)

        private void 重命名_Click(object sender, RoutedEventArgs e) => 执行重命名();

        /// <summary>
        /// **重命名七步**(主菜单 编辑→重命名 与 编辑器右键 → 都走这里):
        ///   ① 取光标下的词(`编辑器.取光标下的词`;取不到、或取到的是关键字/内建函数,就提示一句、不动);
        ///   ② 补成**带模块前缀**的旧名 —— `重命名C` 的类注释 ③ 说了:旧名给裸名时会把**别的模块**里的同名函数一起改到;
        ///   ③ 用项目里现成的 `输入窗口` 问新名(取消就一个字符都不动);
        ///   ④ 拿 `取当前源码(项目)` 算编辑清单;`问题` 非空(撞车 / 非法名 / 新旧同名)就报清楚并**停止**;
        ///   ⑤ **先预览再确认**:清单摘要写进输出框,再问一次"要应用吗";
        ///   ⑥ 应用:已打开的标签页走 `内容` 依赖属性推文本(编辑器经双向绑定同步、页面自动标脏);
        ///      未打开的先用 `打开文件到标签页` 开出来(这样它进了标签页、用户看得见改了什么),
        ///      然后逐个走 `保存标签页` 落盘(沿用既有存盘路径,不自己写 File.WriteAllText);
        ///   ⑦ 撤销口径如实:**每个文件各自一个 AvalonEdit 撤销栈,没有跨文件事务** —— 想整体回退要逐个文件撤销。
        /// 全程 try/catch:哪一步出错都 `异常收集C.记(...)`(进『异常』页),不让界面崩。
        /// </summary>
        private void 执行重命名()
        {
            try
            {
                var 编 = 当前编辑器;
                var 页 = 当前选卡;
                if (编 == null || 页 == null)
                {
                    重命名提示("现在没有打开的文件。先打开一个 .hb 文件,把光标放到要改名的名字上,再执行重命名。");
                    return;
                }

                // ① 光标下的词(顺带看它后面是不是 `(`:这决定"按函数名算"还是"按普通名字算")
                var 词信息 = 编.取光标下的词与后继();
                if (词信息 is not { } 词位 || string.IsNullOrWhiteSpace(词位.词))
                {
                    重命名提示("光标下面没有名字。把光标放进要改名的函数名/变量名里(别停在空白或标点上),再试一次。");
                    return;
                }
                string 词 = 词位.词;
                bool 像调用 = 词位.后一个 == '(';
                // 关键字 / 类型名 / 内建函数 / 汇编指令不是用户起的名字:改了只会把源码改坏,先拦住
                if (补全词表C.查种类(词) is { } 种类)
                {
                    重命名提示($"`{词}` 是{种类},不是能改名的函数或变量。把光标放到自己起的名字上再试。");
                    return;
                }

                // ② 优先用带模块前缀的旧名
                string 旧名 = 取重命名旧名(词, 页.全称, 像调用);

                // ③ 问新名(项目里现成的输入窗口:新建文件也用它,不另造一套)
                string? 新名 = 输入窗口.问(this, "重命名", $"把 `{旧名}` 改成:", 旧名,
                    "会先给一份改动预览,确认之后才动手;注释与字符串里的同名文字不会被改。");
                if (新名 == null) return;                       // 用户取消:不动
                新名 = 新名.Trim();
                // 旧名带模块前缀、新名只写了尾巴(`数学.求和` → `加和`):补上前缀。
                // 不补的话会把 `数学.求和` 整个换成裸名,调用点当场编不过。
                if (旧名.Contains('.') && 新名.Length > 0 && !新名.Contains('.'))
                    新名 = 旧名[..(旧名.LastIndexOf('.') + 1)] + 新名;

                // ④ 算编辑清单
                var 项目 = 拿检查用的项目();
                if (项目 == null)
                {
                    重命名提示("没有可用的工程或文件,算不出要改哪些地方。先打开一个文件或工程再试。");
                    return;
                }
                var 文件表 = 取当前源码(项目);
                var 清单 = 重命名C.计算(旧名, 新名, 文件表);
                if (清单.问题.Count > 0)
                {
                    var 文 = new StringBuilder($"重命名没有执行(一个字符都没改),先解决下面 {清单.问题.Count} 个问题:\n");
                    foreach (var 问 in 清单.问题) 文.AppendLine("  · " + 问);
                    输出框.AppendText(文.ToString());
                    切到底部输出();
                    MessageBox.Show(文.ToString(), "重命名", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (清单.处数 == 0)
                {
                    重命名提示($"工程里没有找到 `{旧名}` 可以改的地方(注释与字符串里的不算)。" +
                        "换个名字,或者确认它在源码里真的出现过。");
                    return;
                }

                // 涉及哪些文件(去重;清单已按 文件 → 行号 排好)
                var 涉及文件 = new List<string>();
                foreach (var 项 in 清单.编辑)
                    if (!涉及文件.Contains(项.文件, StringComparer.OrdinalIgnoreCase)) 涉及文件.Add(项.文件);

                // 未打开的文件必须磁盘上真存在,否则改完写不回去 —— 先查齐再动手,免得改一半
                foreach (string 文件 in 涉及文件)
                    if (找标签页(文件) == null && !File.Exists(文件))
                    {
                        重命名提示($"`{Path.GetFileName(文件)}` 既没打开、磁盘上也不在了,改完没法保存。" +
                            "先把它打开(或另存为)再重命名。");
                        return;
                    }

                // ⑤ 预览
                var 预览 = new StringBuilder();
                预览.AppendLine($"共 {清单.处数} 处,涉及 {涉及文件.Count} 个文件:");
                foreach (var 项 in 清单.编辑)
                    预览.AppendLine($"  {Path.GetFileName(项.文件)}:{项.行号} {项.原词}→{项.新词}");
                预览.AppendLine("撤销是逐个文件独立的(每个文件一个撤销栈),没有「一次全撤销」。");
                输出框.AppendText(预览.ToString());
                切到底部输出();
                if (MessageBox.Show($"要把 `{旧名}` 改成 `{新名}` 吗?共 {清单.处数} 处、{涉及文件.Count} 个文件。\n" +
                        "确认后会立刻保存这些文件;注释与字符串里的同名文字不会被改。", "重命名",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                {
                    输出框.AppendText("已取消重命名(一个字符都没改)。\n");
                    return;
                }

                // ⑥ 应用:先算好每个文件改完的文本(纯函数),再逐个标签页推文本并落盘
                var 改后 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (名称, 文本) in 清单.应用(文件表)) 改后[名称] = 文本;

                var 要存 = new List<选卡页视图模型>();
                var 收过 = new HashSet<选卡页视图模型>();
                foreach (string 文件 in 涉及文件)
                {
                    var 目标页 = 找标签页(文件);
                    if (目标页 == null)
                    {
                        打开文件到标签页(文件);                   // 没打开的:先开成标签页(用户看得见改了什么)
                        目标页 = 找标签页(文件);
                    }
                    if (目标页 == null)
                    {
                        异常收集C.记($"重命名:没能把 {文件} 打开成标签页,这个文件没有改。");
                        continue;
                    }
                    if (!改后.TryGetValue(文件, out string? 新文本))
                    {
                        异常收集C.记($"重命名:算不出 {文件} 改完的文本,这个文件没有改。");
                        continue;
                    }
                    目标页.内容 = 新文本;                          // 走 内容 依赖属性:编辑器同步 + 页面标脏
                    if (收过.Add(目标页)) 要存.Add(目标页);
                }
                int 存好 = 0;
                foreach (var 目标页 in 要存)
                    if (保存标签页(目标页, 强制另存为: false)) 存好++;

                输出框.AppendText($"已改 `{旧名}` → `{新名}`:{清单.处数} 处 / {涉及文件.Count} 个文件,落盘 {存好} 个。\n");
                输出框.AppendText("撤销请逐个文件来(每个文件一个撤销栈,没有跨文件的一次性撤销)。\n");
            安排检查();          // 名字都变了:400ms 后重编一遍,问题页跟着更新
            }
            catch (Exception 异常)
            {
                异常收集C.记($"重命名失败: {异常.Message}");
                记异常框($"重命名失败: {异常}");
            }
        }

        /// <summary> 按路径找一个已经打开的标签页(认法与 `宿主与文件服务C.是同一个文件` 一致:全称 / 绝对路径 / 光名对全称)</summary>
        private 选卡页视图模型? 找标签页(string 全称)
        {
            if (string.IsNullOrWhiteSpace(全称)) return null;
            foreach (var 页 in 主视图模型.选卡页视图模型表)
                if (!页.是特殊页 && 前端契约C.服务.宿主.是同一个文件(页.全称, 全称)) return 页;
            return null;
        }

        /// <summary> 重命名的一句提示:输出框留一份(可回看)+ 弹一次对话框(保证人看得见)</summary>
        private void 重命名提示(string 文本)
        {
            输出框.AppendText(文本 + "\n");
            切到底部输出();
            MessageBox.Show(文本, "重命名", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 光标下这个词该拿哪个"旧名"去算清单:能认出模块就用**带模块前缀**的 `模块.名字`
        /// (`重命名C` 的类注释 ③ 说了:旧名给裸名时,别的模块里的同名函数会被一起改到)。
        /// 认法只看最近一次编译的 `名字表C.函数范围`,三步(都认不出就照裸名算):
        ///   ① 光标所在的那个函数(同一个文件里的行范围)**尾名**就是这个词 → 用它(在声明行上改名最准);
        ///   ② 光标所在函数带模块前缀,而且工程里真有 `那个前缀.词` → 用它(模块内部裸写的调用);
        ///   ③ 全工程里"尾名等于它、而且带前缀"的函数**只有一个** → 用它(顶层程序段里的调用也能补上前缀)。
        /// **前提:这个词后面紧跟 `(`**(`像调用`)—— 否则它多半是个同名的变量/字段,套上函数名就改错东西了。
        /// 名字表里的行号是**各文件自己的**,所以每一步都先按文件认(`宿主与文件服务C.是同一个文件`),不能只看行号。
        /// </summary>
        private string 取重命名旧名(string 词, string? 全称, bool 像调用)
        {
            try
            {
                if (!像调用) return 词;      // 后面不是 `(`:按普通名字(变量/字段)算,别往函数上套
                var 范围 = 前端契约C.服务.编译.最近结果?.名字表?.函数范围;
                if (范围 == null || 范围.Count == 0) return 词;
                int 行 = 当前编辑器?.取当前行号() ?? 0;

                string? 所在函数 = null;
                foreach (var (函数, 起, 止, 文件) in 范围)
                    if (前端契约C.服务.宿主.是同一个文件(文件, 全称) && 行 >= 起 && 行 <= 止) { 所在函数 = 函数; break; }

                // ① 就在这个函数的声明/函数体里,而且这个词是它的尾名
                if (所在函数 is { } 函数全名 && 函数全名.Contains('.')
                    && string.Equals(尾段(函数全名), 词, StringComparison.Ordinal)) return 函数全名;

                // ② 所在函数带前缀 → 试 `前缀.词`(模块内部的裸写)
                if (所在函数 is { } 带点全名 && 带点全名.Contains('.'))
                {
                    string 前缀 = 带点全名[..(带点全名.LastIndexOf('.') + 1)];
                    foreach (var (函数, _, _, 文件) in 范围)
                        if (前端契约C.服务.宿主.是同一个文件(文件, 全称) && string.Equals(函数, 前缀 + 词, StringComparison.Ordinal))
                            return 函数;
                }

                // ③ 全工程里唯一的"带前缀同名函数";不止一个就分不清是哪个模块的,照裸名算
                string? 唯一 = null;
                foreach (var (函数, _, _, _) in 范围)
                {
                    if (!函数.Contains('.') || !string.Equals(尾段(函数), 词, StringComparison.Ordinal)) continue;
                    if (唯一 != null) return 词;
                    唯一 = 函数;
                }
                return 唯一 ?? 词;
            }
            catch { return 词; }
        }

        /// <summary> 取点分名字的最后一段(`数学.求和` → `求和`;不带点就是它自己)</summary>
        private static string 尾段(string 名)
        {
            int 点 = 名.LastIndexOf('.');
            return 点 < 0 ? 名 : 名[(点 + 1)..];
        }

        #endregion

        #region 编辑:转到定义 / 查找引用(P22;引擎侧只算导航结果,见 `引擎\转到定义C.cs`)

        private void 转到定义_Click(object sender, RoutedEventArgs e) => 执行转到定义();
        private void 查找引用_Click(object sender, RoutedEventArgs e) => 执行查找引用();

        /// <summary>
        /// **转到定义**(F12;编辑器右键也有):光标下那个词 → `转到定义C.计算`(带最近一次编译的名字表 +
        /// 调用关系表 + 当前工程源码)→ 有定义就 `打开文件并跳到`(跨文件);没有定义就把**说明**给人看
        /// (内建函数 / 宿主能力 / 汇编指令 / 语句 / 结构 / 还没编译 / 未定义,各有各的话,不是没反应)。
        /// </summary>
        private void 执行转到定义()
        {
            try
            {
                var 词位 = 取光标下的词或提示();
                if (词位 is not { } 位) return;
                var 结果 = 算导航结果(位.词);
                if (结果.定义 is { } 定义 && 定义.行 > 0)
                {
                    打开文件并跳到(定义.文件.Length > 0 ? 定义.文件 : (当前选卡?.全称 ?? ""), 定义.行);
                    输出框.AppendText($"转到定义:{定义}\n" + (结果.说明.Length > 0 ? "  " + 结果.说明 + "\n" : ""));
                    切到底部输出();
                    return;
                }
                导航提示(结果.说明 + (结果.引用数 > 0
                    ? $"\n\n(顺带按文本找到 {结果.引用数} 处同名 —— 要清单就用『查找引用』)"
                    : ""));
            }
            catch (Exception 异常)
            {
                异常收集C.记($"转到定义失败: {异常.Message}");
                记异常框($"转到定义失败: {异常}");
            }
        }

        /// <summary>
        /// **查找引用**(Shift+F12):同一份导航结果 —— 引用清单写进输出框(**函数走调用边表 = 语义;
        /// 变量是文本整词**,说明里写明口径),有引用就跳到第一处(看得见)。
        /// </summary>
        private void 执行查找引用()
        {
            try
            {
                var 词位 = 取光标下的词或提示();
                if (词位 is not { } 位) return;
                var 结果 = 算导航结果(位.词);

                var 文 = new StringBuilder();
                文.AppendLine($"查找引用:`{位.词}` —— {结果.类别}"
                    + (结果.定义 != null ? $";定义 {结果.定义}" : ";没有源码定义可跳"));
                if (结果.说明.Length > 0) 文.AppendLine("  " + 结果.说明);
                if (结果.引用数 == 0) 文.AppendLine("  没有找到引用处。");
                else foreach (var 处 in 结果.引用) 文.AppendLine("  · " + 处);
                输出框.AppendText(文.ToString());
                切到底部输出();

                if (结果.引用.Count > 0 && 结果.引用[0].行 > 0)
                    打开文件并跳到(结果.引用[0].文件, 结果.引用[0].行);
            }
            catch (Exception 异常)
            {
                异常收集C.记($"查找引用失败: {异常.Message}");
                记异常框($"查找引用失败: {异常}");
            }
        }

        /// <summary> 取光标下的词(取不到就提示一句并返回 null)</summary>
        private (string 词, char 后一个)? 取光标下的词或提示()
        {
            var 编 = 当前编辑器;
            var 页 = 当前选卡;
            if (编 == null || 页 == null) { 导航提示("现在没有打开的文件。先打开一个 .hb 文件再试。"); return null; }
            var 词位 = 编.取光标下的词与后继();
            if (词位 is not { } 位 || string.IsNullOrWhiteSpace(位.词)) { 导航提示("光标下面没有名字。把光标放进要看的函数名 / 变量名里再试。"); return null; }
            return 位;
        }

        /// <summary> 算一次导航结果:最近一次编译的 `名字表C` / 调用关系表 + 当前工程源码(没有源码也能算,说明里会讲)</summary>
        private 导航结果C 算导航结果(string 词)
        {
            List<(string 名称, string 内容)>? 文件表 = null;
            try { var 项目 = 拿检查用的项目(); if (项目 != null) 文件表 = 取当前源码(项目); } catch { 文件表 = null; }
            return 转到定义C.计算(词, 当前选卡?.全称, 前端契约C.服务.编译.最近结果, 文件表);
        }

        /// <summary> 导航的一句提示:输出框留一份 + 弹一次对话框(与重命名/替换同一套)</summary>
        private void 导航提示(string 文本)
        {
            输出框.AppendText(文本 + "\n");
            切到底部输出();
            MessageBox.Show(文本, "转到定义 / 查找引用", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion
    }
}
