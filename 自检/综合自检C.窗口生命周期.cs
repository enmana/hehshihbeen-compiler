using System;
using System.IO;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// H23(2026-09-24):**窗体检漏 —— 关了窗口还留着回调**。
    ///
    /// 复核稿(`全面测评报告_2026-09-13.md:133,346`,P1 第 7 条)原话:
    /// `Closing` 里只做保存提示与树状态,**没有 `Stop()` 两个 DispatcherTimer,也没有解绑事件** ——
    /// 四个**静态**事件(`异常收集C.追加` / `主题C.主题变化` / `编辑器.新造出来` / `选项页.选项已应用`)
    /// 只 `+=` 不 `-=`,回调活得比窗口久。
    ///
    /// 这一组为什么**按源码钉**:窗口开不了(硬规矩 6:不许开 GUI 窗口验证),而这一件要看的恰恰是
    /// "关窗那一刻到底收了什么口" —— 于是钉**接线本身**:
    /// ① 四个静态事件集中进 `订阅全局事件` / `退订全局事件` 这一对方法,各一处 `+=`、各一处 `-=`,
    ///    而且退订用的必须是**具名方法**(lambda 是退不掉的);
    /// ② `Closing` 里"立旗 → 停表 → 退订"三句齐、顺序对,并且排在"用户点取消就早退"那一条之后
    ///    (取消关窗还收口 = 窗口活着但已经不干活了,比泄漏更坏);
    /// ③ 主窗口自己起的三个计时器一个不落地停(复核稿说的"两个"是当时单文件里的两个,现在拆成四份文件);
    /// ④ 三个 Tick 入口第一句都是 `if (_已关窗) return;` —— 已经排进队列的那一拍也不许再动界面。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 窗口生命周期自检()
        {
            string? 根 = 找仓库根目录();
            if (根 == null)
            {
                跳过接线("H23 窗口生命周期(订阅 / 退订 + 关窗收口,按源码钉)", "找不到仓库根(不在工作副本里跑)");
                return;
            }
            try
            {
                string 窗口 = File.ReadAllText(Path.Combine(根, "MainWindow.窗口与文件.cs"));
                string 检查 = File.ReadAllText(Path.Combine(根, "MainWindow.代码检查.cs"));
                string 运行 = File.ReadAllText(Path.Combine(根, "MainWindow.运行与调试器.cs"));

                // ---------- ① 四个静态事件:一处 += 一处 -=,全在这一对方法里 ----------
                string[] 订阅 =
                [
                    "异常收集C.追加 += 记异常框;",
                    "主题C.主题变化 += 应用主题;",
                    "编辑器.新造出来 += 编辑器_新造出来;",
                    "选项页.选项已应用 += 全局选项已应用;",
                ];
                string[] 退订 =
                [
                    "异常收集C.追加 -= 记异常框;",
                    "主题C.主题变化 -= 应用主题;",
                    "编辑器.新造出来 -= 编辑器_新造出来;",
                    "选项页.选项已应用 -= 全局选项已应用;",
                ];
                var 不成对 = "";
                for (int i = 0; i < 订阅.Length; i++)
                {
                    if (出现次数(窗口, 订阅[i]) != 1) 不成对 += $"「{订阅[i]}」{出现次数(窗口, 订阅[i])} 处;";
                    if (出现次数(窗口, 退订[i]) != 1) 不成对 += $"「{退订[i]}」{出现次数(窗口, 退订[i])} 处;";
                }
                bool 一对都在 = 窗口.Contains("private void 订阅全局事件()", StringComparison.Ordinal)
                    && 窗口.Contains("private void 退订全局事件()", StringComparison.Ordinal)
                    && 出现次数(窗口, "订阅全局事件();") == 1;   // 构造函数里只调一次(定义那一行不带分号,不会误算)
                记接线("H23 四个静态事件各一处 += 一处 -=,全部集中进 `订阅全局事件` / `退订全局事件`",
                    不成对.Length == 0 && 一对都在,
                    不成对.Length == 0
                        ? $"四个事件各一处 += / 一处 -=;订阅在构造函数里调 1 次:{一对都在}"
                        : 不成对);

                // ---------- ② 退订用同一个具名方法(lambda 退不掉) ----------
                bool 老写法 = 窗口.Contains("异常收集C.追加 += 行 => 记异常框(行);", StringComparison.Ordinal)
                    || 窗口.Contains("选项页.选项已应用 += _ =>", StringComparison.Ordinal);
                记接线("H23 退订的是具名方法(拿去订阅的那个 lambda 退不掉,一处不剩)",
                    !老写法
                    && 窗口.Contains("private void 全局选项已应用(选项设置 _)", StringComparison.Ordinal),
                    老写法 ? "还有 lambda 订阅残留(`+= 行 =>` / `+= _ =>`)" : "四处订阅全是具名方法");

                // ---------- ③ Closing:立旗 → 停表 → 退订,且排在"取消关窗"那条早退之后 ----------
                int 早退 = 窗口.IndexOf("e.Cancel = true;", StringComparison.Ordinal);
                int 保存 = 窗口.IndexOf("保存树展开状态();", StringComparison.Ordinal);
                int 立旗 = 窗口.IndexOf("_已关窗 = true;", StringComparison.Ordinal);
                int 停表 = 窗口.IndexOf("停掉窗口计时器();", StringComparison.Ordinal);
                int 退订处 = 窗口.IndexOf("退订全局事件();", StringComparison.Ordinal);
                bool 顺序对 = 早退 >= 0 && 保存 >= 0 && 立旗 >= 0 && 停表 >= 0 && 退订处 >= 0
                    && 早退 < 立旗 && 保存 < 立旗 && 立旗 < 停表 && 停表 < 退订处;
                记接线("H23 关窗收口 = 立旗 → 停表 → 退订,而且排在「用户点取消就早退」之后",
                    顺序对,
                    顺序对
                        ? $"取消[{早退}] < 立旗[{立旗}] < 停表[{停表}] < 退订[{退订处}](保存树状态在 [{保存}])"
                        : $"早退={早退} 保存={保存} 立旗={立旗} 停表={停表} 退订={退订处}");

                // ---------- ④ 主窗口自己起的三个计时器一个不落 ----------
                string 停表体 = 取方法体(窗口, "private void 停掉窗口计时器()");
                string[] 三个 =
                [
                    "_检查计时器?.Stop();",
                    "_标签点击计时器?.Stop();",
                    "_输出节流?.Stop();",
                ];
                var 漏了 = "";
                foreach (string 一条 in 三个)
                {
                    if (!停表体.Contains(一条, StringComparison.Ordinal)) 漏了 += 一条 + " ";
                }
                记接线("H23 关窗把主窗口三个计时器全停(检查 / 标签点击 / 输出节流)",
                    停表体.Length > 0 && 漏了.Length == 0,
                    停表体.Length == 0 ? "找不到 `停掉窗口计时器` 的方法体" : (漏了.Length == 0 ? "三个都在" : "漏:" + 漏了));

                // ---------- ⑤ 三个 Tick 入口第一句都是"关窗了就走人" ----------
                string 检查拍 = 取方法体(检查, "private void 检查计时器_Tick(object? sender, EventArgs e)");
                string 标签拍 = 取之间(窗口, "_标签点击计时器.Tick += (_, _) =>", "};");
                string 输出拍 = 取方法体(运行, "private void 输出节流_Tick(object? sender, EventArgs e)");
                var 漏拍 = "";
                foreach ((string 名, string 片, string 动作) in new[]
                {
                    ("检查", 检查拍, "Stop();"),
                    ("标签点击", 标签拍, "Stop();"),
                    ("输出节流", 输出拍, "刷程序输出();"),
                })
                {
                    int 守卫 = 片.IndexOf("if (_已关窗) return;", StringComparison.Ordinal);
                    int 干活 = 片.IndexOf(动作, StringComparison.Ordinal);
                    if (片.Length == 0 || 守卫 < 0 || 干活 < 0 || 守卫 > 干活) 漏拍 += 名 + " ";
                }
                记接线("H23 三个 Tick 入口第一句都是 `if (_已关窗) return;`(排进队列的那一拍也不动界面)",
                    漏拍.Length == 0,
                    漏拍.Length == 0 ? "四处都有守卫,且在动界面之前" : "没有守卫 / 守卫在后:" + 漏拍);

                // ---------- ⑥ T28:编辑器那 8 个静态委托也成对(关窗回收不掉那一处的账) ----------
                // 它们以前在构造函数里直接赋值、关窗一个都不退 ⇒ 9 处 static 引用仍指着已关窗口。
                // 判据:① 这几个静态字段**不在构造函数里**被赋值(都在 `订阅全局事件` 里);
                //      ② `订阅全局事件` / `退订全局事件` 各自把它们点过名(订阅处 `=`,退订处 `= null`);
                //      ③ 换过 lambda 的两个(退订只能靠同一个实例)各有一个字段留着那份引用。
                // P35 起多一个 `编辑器.加监视被请求`(右键菜单那条),所以这一族是 **9 个委托 + 1 张名字表**。
                string 订阅体 = 取方法体(窗口, "private void 订阅全局事件()");
                string 退订体 = 取方法体(窗口, "private void 退订全局事件()");
                string 构造体 = 取之间(窗口, "public MainWindow()", "        }\n\n        #endregion");
                string[] 八个 =
                [
                    "编辑器.断点被点击",
                    "编辑器.重命名被请求",
                    "编辑器.转到定义被请求",
                    "编辑器.查找引用被请求",
                    "编辑器.思维导图被请求",
                    "编辑器.切换断点被请求",
                    "编辑器.断点条件被请求",
                    "编辑器.删除全部断点被请求",
                    "编辑器.加监视被请求",
                    "编辑器.取名表",
                ];
                var 没成对 = "";
                foreach (string 一个 in 八个)
                {
                    if (!订阅体.Contains(一个, StringComparison.Ordinal)) 没成对 += $"订阅缺「{一个}」;";
                    if (!退订体.Contains(一个, StringComparison.Ordinal)) 没成对 += $"退订缺「{一个}」;";
                    if (构造体.Contains(一个, StringComparison.Ordinal)) 没成对 += $"构造函数里还在赋值「{一个}」;";
                }
                bool 两个实例 = 窗口.Contains("private Action? _切换断点请求;", StringComparison.Ordinal)
                    && 窗口.Contains("private Action? _删除全部断点请求;", StringComparison.Ordinal)
                    && 订阅体.Contains("_切换断点请求 ??=", StringComparison.Ordinal)
                    && 订阅体.Contains("_删除全部断点请求 ??=", StringComparison.Ordinal);
                记接线("T28 编辑器 9 个静态委托 + 取名表都订阅 / 退订成对(关窗后 static 不许再指着已关窗口)",
                    没成对.Length == 0 && 两个实例,
                    没成对.Length > 0 ? 没成对
                        : $"10 个静态成员订阅处 1 处、退订处 1 处,构造函数里一处不留;两个 lambda 留着同一份实例:{两个实例}");
            }
            catch (Exception ex) { 记录异常("H23 窗口生命周期", ex); }
        }

        /// <summary>
        /// 取一个方法从签名到**它自己那一层的收尾 `}`**(8 空格缩进);找不到给空串。
        /// T28 起**先抹平 `\r`** —— 仓库里 `MainWindow.运行与调试器.cs` 是 CRLF,`"\n        }"` 这种
        /// 带 `\n` 的锚在 CRLF 文件上一个都匹配不上(实测踩到)。
        /// </summary>
        private static string 取方法体(string 文本, string 签名)
        {
            文本 = 文本.Replace("\r", "");
            int 起 = 文本.IndexOf(签名, StringComparison.Ordinal);
            if (起 < 0) return "";
            int 尾 = 文本.IndexOf("\n        }", 起, StringComparison.Ordinal);
            return 尾 < 0 ? "" : 文本[起..(尾 + "\n        }".Length)];
        }

        /// <summary> 取 `起点` 之后到 `终点`(不含)之间那一片;任一处找不到给空串 </summary>
        private static string 取之间(string 文本, string 起点, string 终点)
        {
            int 起 = 文本.IndexOf(起点, StringComparison.Ordinal);
            if (起 < 0) return "";
            int 尾 = 文本.IndexOf(终点, 起 + 起点.Length, StringComparison.Ordinal);
            return 尾 < 0 ? "" : 文本[起..尾];
        }
    }
}
