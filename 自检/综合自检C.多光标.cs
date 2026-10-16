using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **多光标 / 列选择**(P40,2026-10-07;用户排的七件里的第 7 件:
    /// "纯编辑器工程量,没设计难题")。
    ///
    /// 两半分开钉:
    ///   · **列选择**(Alt + 鼠标拖)—— 那是 AvalonEdit 自带的(`MouseSelectionMode.Rectangular`),
    ///     我们只钉"它在、而且我们没把它关掉";
    ///   · **多光标** —— 自己实现的那一半,规则在 `引擎\多光标C.cs`(纯函数,真跑):
    ///     ① 行 / 列 ↔ 文档下标的换算(`\r\n` / `\n` / `\r` 三种换行都算得对);
    ///     ② 相邻行取同列(到文档头尾返回 null,列越界夹到行尾);
    ///     ③ 光标去重 + 去掉跟主光标重合的那些;
    ///     ④ **批量替换从后往前**(下标不会被前一次插入带偏);
    ///     ⑤ 下一个相同词(整词边界;找不到给 null,不硬选一个不相干的地方)。
    /// 末尾一条接线哨兵:渲染器挂上了、键位接上了、整篇推文本会把副光标收掉。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 多光标自检()
        {
            // ---- ① 行列 ↔ 下标(三种换行都算得对) ----
            const string 换算名 = "多光标 行列与下标的换算:`\\r\\n` / `\\n` / `\\r` 三种换行都算得对";
            try
            {
                const string 文 = "左右\r\n中下末\n上\r尾\n";
                //            行1: 0..1  \r\n 2..3   行2: 4..6  \n 7   行3: 8  \r 9   行4: 10
                bool 对 = 多光标C.偏移(文, 1, 0) == 0
                    && 多光标C.偏移(文, 1, 2) == 2                       // 夹到行尾(行只有 2 个字)
                    && 多光标C.偏移(文, 2, 0) == 4
                    && 多光标C.偏移(文, 3, 0) == 8
                    && 多光标C.偏移(文, 4, 0) == 10
                    && 多光标C.偏移(文, 9, 0) == null                    // 行不够
                    && 多光标C.位置(文, 4) == (2, 0)
                    && 多光标C.位置(文, 8) == (3, 0)
                    && 多光标C.位置(文, 10) == (4, 0);
                记录(换算名, 对,
                    $"第2行第0列 → @{多光标C.偏移(文, 2, 0)};第3行 → @{多光标C.偏移(文, 3, 0)};" +
                    $"第4行 → @{多光标C.偏移(文, 4, 0)};@4 在第 {多光标C.位置(文, 4).行} 行第 {多光标C.位置(文, 4).列} 列");
            }
            catch (Exception ex) { 记录异常(换算名, ex); }

            // ---- ② 相邻行取同列 ----
            const string 相邻名 = "多光标 相邻行取同列(短行夹到行尾、到文档头尾给 null)";
            try
            {
                const string 文 = "左\n中右\n下";          // 三行,末尾**不带**换行(带了就多出一个空行)
                // `相邻行` 只回答"上一行 / 下一行那一行是什么"(界面拿它判"还能不能再加一个光标");
                // 真正算下标的是 `偏移`(列越界夹到行尾)
                string 二 = 多光标C.相邻行(1, 0, true, 文) ?? "";
                string 三 = 多光标C.相邻行(2, 0, true, 文) ?? "";
                string 上 = 多光标C.相邻行(2, 0, false, 文) ?? "";
                bool 对 = 二 == "中右" && 三 == "下" && 上 == "左"
                    && 多光标C.相邻行(1, 0, false, 文) == null     // 第一行再往上:没有
                    && 多光标C.相邻行(3, 0, true, 文) == null      // 最后一行再往下:没有
                    && 多光标C.偏移(文, 1, 1) == 1                 // 单字行第 1 列 = 行尾
                    && 多光标C.偏移(文, 2, 1) == 3                 // 第二个字(`中` 与 `右` 之间)
                    && 多光标C.偏移(文, 2, 9) == 4                 // 列越界 ⇒ 夹到行尾
                    && 多光标C.偏移(文, 3, 0) == 5;
                记录(相邻名, 对, $"第1行往下 → `{二}`;第2行往下 → `{三}`;第2行往上 → `{上}`;头尾给 null;短行列越界夹到行尾");
            }
            catch (Exception ex) { 记录异常(相邻名, ex); }

            // ---- ③ 去重 + 去掉主光标 ----
            const string 去重名 = "多光标 副光标去重、并去掉跟主光标重合的那些(不许在同一处插两遍)";
            try
            {
                var 规 = 多光标C.规整([new(5), new(3), new(5), new(9)]);
                var 去主 = 多光标C.去掉主光标(规, 5);
                bool 对 = 规.Count == 3 && 规[0].位置 == 9 && 规[1].位置 == 5 && 规[2].位置 == 3
                    && 去主.Count == 2 && 去主.All(x => x.位置 != 5);
                记录(去重名, 对,
                    $"4 个输入(含一个重的)→ 规整成 {规.Count} 个、按位置从大到小 {string.Join(",", 规.Select(x => x.位置))};" +
                    $"去掉主光标 5 → {去主.Count} 个");
            }
            catch (Exception ex) { 记录异常(去重名, ex); }

            // ---- ④ 批量替换:从后往前,下标不被带偏 ----
            const string 批量名 = "多光标 批量替换从后往前改(前面的下标不被前一次插入带偏),新位置一并算出来";
            try
            {
                const string 文 = "左\n中\n右\n";
                // @0 `左`、@2 `中`、@4 `右`:三处都换成 `X`
                var 果 = 多光标C.批量替换(文, [new(0, 1), new(2, 1), new(4, 1)], "X");
                // 新位置 = 每一处**替换之后**那个光标该落的地方(替换成同样长度时保持在原处;这里 `X` 与 `左` 同长 ⇒ 0/2/4)
                bool 对 = 果 is { } g && g.文档 == "X\nX\nX\n" && g.新位置.SequenceEqual([1, 3, 5]);
                // 插入型(选中长度 0):三处各插一个 `#`
                var 插 = 多光标C.批量替换(文, [new(0), new(2), new(4)], "#");
                bool 插对 = 插 is { } h && h.文档 == "#左\n#中\n#右\n";
                // 下标越界 ⇒ 不动手
                bool 越界 = 多光标C.批量替换(文, [new(99, 1)], "X") == null;
                记录(批量名, 对 && 插对 && 越界,
                    $"三处换成 X → `{(果?.文档 ?? "").Replace("\n", "\\n")}`(新位置 {string.Join(",", 果?.新位置 ?? [])});" +
                    $"三处插入 # → `{(插?.文档 ?? "").Replace("\n", "\\n")}`;越界 → {(越界 ? "不动手" : "动了")}");
            }
            catch (Exception ex) { 记录异常(批量名, ex); }

            // ---- ⑤ 下一个相同词:整词边界 ----
            const string 找词名 = "多光标 下一个相同词只认整词(`词` 不会命中 `词词` / `前缀词`),找不到给 null";
            try
            {
                const string 文 = "词 = 1;\n词词 = 2;\n名 = 词;\n";
                var 一 = 多光标C.下一个相同词(文, "词", 1);
                var 没有 = 多光标C.下一个相同词(文, "没有这个词", 0);
                var 词词 = 多光标C.下一个相同词(文, "词词", 0);
                // 找到的那一处必须是一个**整词** `词`(第 3 行那个),不能是 `词词` 里那一个;
                // 并且 `词词` 自己也能整词找到
                bool 对 = 一 is { } c && c.长度 == 1 && c.位置 >= 7 && 文[c.位置] == '词'
                    && 词词 is { } d && 文.Substring(d.位置, d.长度) == "词词"
                    && 没有 == null;
                记录(找词名, 对,
                    $"`词` 的下一个 → @{一?.位置}(第 {多光标C.位置(文, 一?.位置 ?? 0).行} 行,长度 {一?.长度});" +
                    $"`词词` 的下一个 → @{词词?.位置}(长度 {词词?.长度});不存在的词 → {(没有 == null ? "null" : "有")}");
            }
            catch (Exception ex) { 记录异常(找词名, ex); }

            // ---- ⑥ 接线(接线哨兵) ----
            const string 接线名 = "多光标 接线:渲染器挂背景层 / 输入与删除挨个补一遍 / Ctrl+D 与 Ctrl+Alt+↑↓ / 整篇推文本收掉副光标 / 列选择那一档开着";
            string? 根 = 找仓库根目录();
            if (根 == null) 跳过接线(接线名, "工作副本之外跑自检,读不到源码");
            else
            {
                try
                {
                    string 源 = File.ReadAllText(Path.Combine(根, "界面控件", "编辑器.多光标.cs"));
                    string 渲染 = File.ReadAllText(Path.Combine(根, "界面控件", "多光标渲染.cs"));
                    string 初始化 = File.ReadAllText(Path.Combine(根, "界面控件", "编辑器.初始化与交互.cs"));
                    string 补全 = File.ReadAllText(Path.Combine(根, "界面控件", "编辑器.补全与外观.cs"));
                    string[] 名字 = ["初始化调接上", "渲染器挂上", "文字进来补插", "删除补删", "Ctrl+D", "Ctrl+Alt 上下",
                                      "推文本收掉", "Esc 收掉", "列选择那一档"];
                    bool[] 条 =
                    [
                        初始化.Contains("接上多光标();", StringComparison.Ordinal),
                        源.Contains("BackgroundRenderers.Add(new 多光标渲染(() => [.. _副光标.Select(x => x.位置)]));", StringComparison.Ordinal)
                            && 渲染.Contains("KnownLayer.Selection", StringComparison.Ordinal),
                        源.Contains("代码编辑器.TextArea.TextEntered += 多光标_文字进来了;", StringComparison.Ordinal)
                            && 源.Contains("文档.Replace(起, 长, 插入);", StringComparison.Ordinal),
                        源.Contains("多光标补删(bool 退格)", StringComparison.Ordinal)
                            && 源.Contains("Dispatcher.BeginInvoke(new Action(() => 多光标补删(退格))", StringComparison.Ordinal),
                        源.Contains("if (控制 && !替换 && !换档 && e.Key == Key.D)", StringComparison.Ordinal),
                        源.Contains("if (控制 && 替换 && (e.Key == Key.Down || e.Key == Key.Up))", StringComparison.Ordinal),
                        补全.Contains("if (!多光标正在写 && 正在更新高亮) 清掉多光标();", StringComparison.Ordinal),
                        源.Contains("if (e.Key == Key.Escape && _副光标.Count > 0)", StringComparison.Ordinal),
                        源.Contains("MouseSelectionMode.Normal", StringComparison.Ordinal),
                    ];
                    var 缺 = Enumerable.Range(0, 条.Length).Where(i => !条[i]).Select(i => 名字[i]).ToArray();
                    记接线(接线名, 缺.Length == 0,
                        缺.Length == 0
                            ? "渲染器 / 补插 / 补删 / Ctrl+D / Ctrl+Alt↑↓ / Esc / 推文本收掉 / 列选择那一档都在"
                            : $"{条.Length} 处里差了 {缺.Length} 处:{string.Join(" / ", 缺)}");
                }
                catch (Exception ex) { 记录异常(接线名, ex); }
            }
        }
    }
}
