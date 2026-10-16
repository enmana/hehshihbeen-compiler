using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        /// <summary>
        /// C25-2(第165批):帮助**章节树生成器**的验收(`引擎\帮助站点C.cs`)。
        ///
        /// 这一件的价值全在一条不变式上:**生成物条数 = 表里条目数** —— 以后往
        /// `类型C.表` / `指令C.全部指令` / `中间层指令C` 的词表里加一条,帮助站点的对应节就多一条,
        /// 不需要谁记得去改文档(C25-1 的核对表说明手写那 23 节正是栽在这儿)。
        /// 所以这里的断言分两半:
        /// <list type="number">
        /// <item>**对着真表数**:类型 / 指令 / 关键字 / 内建函数 / 方法五节的条数与表逐一对上,
        ///   每条子项都有标题、锚点、正文(说明取自 `中间层指令C.说明表`);类库节对着
        ///   `Sample\库\*.hb` 数,一个库一条;</item>
        /// <item>**喂假表验"加一条多一条"**:五张表各追加一条假的,条数必须各多 1
        ///   (这条才是防"以后落后"的钉子,不依赖当时恰好有几条)。</item>
        /// </list>
        /// 另外把"写 HTML 之前必须成立"的两条也钉住:**锚点全树唯一**、**大纲文本里每个锚点都出现**
        /// (C25-3 靠锚点跳转)。
        /// </summary>
        private static void 帮助站点自检()
        {
            // ---- ① 真表:七节齐全、顺序固定、每条都有标题 / 锚点 / 正文 ----
            帮助节点C 树 = 帮助站点C.建树();

            记录("帮助站点 顶层十节齐全且顺序固定(手写三节在前 + 生成七节)",
                一级节标题(树).SequenceEqual(帮助站点C.一级节),
                string.Join(" / ", 一级节标题(树)));

            记录("帮助站点 锚点全树唯一且非空,标题也不空",
                帮助站点C.重复锚点(树) == null
                && 帮助站点C.全部节点(树).All(节 => 节.锚点.Length > 0
                    && 节.标题.Length > 0 && string.Equals(节.锚点, 节.锚点.Trim(), StringComparison.Ordinal)),
                帮助站点C.重复锚点(树) is { } 重 ? $"重复锚点 = {重}" : $"共 {帮助站点C.节点数(树)} 个节点");

            // 多页站点按标题找"分类索引挂哪个分组"(见 `帮助站点C.群表`),所以**一级节 + 直接子项**这一层
            // 的标题不能重(类型名同时又是关键字,那是两节里各有一条,不算这里说的重复)
            var 一级子 = 树.子节点.SelectMany(节 => 节.子节点).ToArray();
            var 重标题 = 一级子.GroupBy(节 => 节.标题, StringComparer.Ordinal)
                            .Where(组 => 组.Count() > 1).Select(组 => $"{组.Key}×{组.Count()}").ToArray();
            记录("帮助站点 一级节的直接子项标题不重复(分类索引按这一层挂,标题重了会挂错两份)",
                 重标题.Length == 0,
                 重标题.Length > 0 ? "重复标题: " + string.Join(" ", 重标题)
                                   : $"一级子项 {一级子.Length} 条标题都不重");

            // 类型节:基本类型逐个对上(宽度 / 范围都进了正文),外加 `字符串` 那一条附加类型
            帮助节点C? 类型节 = 帮助站点C.找节(树, 帮助站点C.类型节名);
            var 类型子 = 类型节?.子节点 ?? [];
            int 期望类型数 = 类型C.表.Length + 中间层指令C.附加类型名表.Length;
            bool 类型正文齐 = 类型C.表.All(类 =>
                类型子.Any(子 => 子.标题 == $"{类.中文}({类.英文})"
                    && 子.正文.Contains($"{类.宽度} 字节", StringComparison.Ordinal)
                    && 子.正文.Contains(类.范围文本, StringComparison.Ordinal)));
            记录("帮助站点 类型节 = 类型C.表 + 附加类型(每个类型一条:中英名 / 宽度 / 范围)",
                类型节 != null && 类型子.Count == 期望类型数 && 类型正文齐,
                $"条数 {类型子.Count} / 期望 {期望类型数}(表 {类型C.表.Length} + 附加 {中间层指令C.附加类型名表.Length})");

            记录("帮助站点 类型节里 `字符串` 查得到(它不是基本类型,靠附加类型名表补)",
                帮助站点C.按锚点找(树, 帮助站点C.锚点(帮助站点C.类型节名, "字符串")) is { 正文.Length: > 0 },
                帮助站点C.锚点(帮助站点C.类型节名, "字符串"));

            // 指令节:分组之下的**叶子**条数 = 指令表条数;每条正文带操作码 / 格式 / 写法
            帮助节点C? 指令节 = 帮助站点C.找节(树, 帮助站点C.指令节名);
            var 指令叶子 = 指令节?.子节点.SelectMany(组 => 组.子节点).ToArray() ?? [];
            bool 指令正文齐 = 指令C.全部指令.All(条 =>
                指令叶子.Any(叶 => 叶.标题 == $"{条.中文} {条.英文}"
                    && 叶.正文.Contains($"0x{条.操作码:X2}", StringComparison.Ordinal)
                    && 叶.正文.Contains(条.格式.ToString(), StringComparison.Ordinal)));
            记录("帮助站点 指令节 = 指令表(每条:中英名 / 操作码 / 格式 / 写法示例)",
                指令节 != null && 指令叶子.Length == 指令C.全部指令.Count && 指令正文齐,
                $"条数 {指令叶子.Length} / 表 {指令C.全部指令.Count};分 {指令节?.子节点.Count ?? 0} 组");

            // 关键字 / 内建函数 / 方法三节:条数对表,正文来自说明表(非空)
            记录("帮助站点 关键字节 = 关键字表去掉基本类型名后的那些,每条正文非空(说明表来)",
                数节(树, 帮助站点C.关键字节名) == 关键字条数()
                && 正文全非空(树, 帮助站点C.关键字节名),
                $"条数 {数节(树, 帮助站点C.关键字节名)} / 期望 {关键字条数()}(表 {中间层指令C.关键字表.Length} 去掉类型名)");

            记录("帮助站点 内建函数节 = 内建函数名表,且每条正文非空",
                数节(树, 帮助站点C.内建节名) == 中间层指令C.内建函数名表.Length
                && 正文全非空(树, 帮助站点C.内建节名),
                $"条数 {数节(树, 帮助站点C.内建节名)} / 表 {中间层指令C.内建函数名表.Length}");

            记录("帮助站点 方法节 = 方法名表(带点写法 .名字()),且每条正文非空",
                数节(树, 帮助站点C.方法节名) == 中间层指令C.方法名表.Length
                && 正文全非空(树, 帮助站点C.方法节名),
                $"条数 {数节(树, 帮助站点C.方法节名)} / 表 {中间层指令C.方法名表.Length}");

            // ---- ② 类库节:对着真正的 Sample\库 数(每个 .hb 一条,正文 = 头注释) ----
            跑且断言("帮助站点 类库节 = Sample\\库 下的每个 .hb(一个库一条)", () =>
            {
                string? 示例 = 找示例目录();
                string 库目录 = 示例 == null ? "" : Path.Combine(示例, "库");
                var 库文件 = 帮助站点C.读库目录(库目录);
                帮助节点C 库节 = 帮助站点C.建树(库文件);
                帮助节点C? 节 = 帮助站点C.找节(库节, 帮助站点C.类库节名);
                记录("帮助站点 类库节 = Sample\\库 下的每个 .hb(一个库一条)",
                    节 != null && 库文件.Count > 0 && 节.子节点.Count == 库文件.Count
                    && 节.子节点.All(子 => 子.正文.Length > 0 && 子.标题.EndsWith(".hb", StringComparison.Ordinal)),
                    节 == null ? "找不到类库节" : $"{库文件.Count} 个库文件:{string.Join(" ", 节.子节点.Select(x => x.标题))}");
            });

            // 头注释的取法:分隔线丢掉、第一条真代码之前才收、**行与行之间保留换行**(页面按块渲染)、没注释不炸
            记录("帮助站点 库头注释:丢掉分隔线,第一条真代码之前才收,行与行之间保留换行",
                帮助站点C.取头注释("// ======\n// 假库 —— 说明\n//   · 用法\n\n整数 x = 1;\n// 后面这句不算\n")
                    == "假库 —— 说明\n  · 用法",
                帮助站点C.取头注释("// ======\n// 假库 —— 说明\n//   · 用法\n\n整数 x = 1;\n// 后面这句不算\n").Replace("\n", "\\n"));
            记录("帮助站点 库头注释:没有注释给空串(类库节会写一句人话,不是空白)",
                帮助站点C.取头注释("") == "" && 帮助站点C.取头注释("整数 x = 1;\n") == ""
                && 假库正文("整数 x = 1;\n").Contains("没有开头说明注释", StringComparison.Ordinal));

            // 公开方法的取法:**库源码里的 `方法 …` 声明**才算(带 `私有` 的不算),作用取紧上面那行注释
            记录("帮助站点 库公开方法:只认 `方法 …` 声明(私有不算),作用取紧上面那行注释",
                帮助站点C.取公开方法("// 取数:干这个\n方法 取数(整数 x) 返回 整数\n{\n    返回 x;\n}\n\n// 助手:干那个\n私有 方法 助手() 返回 整数\n{\n    返回 1;\n}\n")
                    is var 取数 && 取数.Count == 1 && 取数[0].名 == "取数"
                    && 取数[0].签名 == "取数(整数 x) 返回 整数" && 取数[0].作用 == "取数:干这个",
                string.Join(" ; ", 帮助站点C.取公开方法("// 取数:干这个\n方法 取数(整数 x) 返回 整数\n{\n    返回 x;\n}\n\n// 助手:干那个\n私有 方法 助手() 返回 整数\n{\n    返回 1;\n}\n")
                    .Select(x => $"{x.名}/{x.签名}/{x.作用}")));

            // ---- ③ 每个库那一页都要有一张"公开方法表",条数 = 源码里的公开方法数(用户 2026-10-05 提的缺口) ----
            跑且断言("帮助站点 每个库一页都有**公开方法表**,条数 = 库源码里的公开 `方法` 声明数(一个不落)", () =>
            {
                string? 示例 = 找示例目录();
                string 库目录 = 示例 == null ? "" : Path.Combine(示例, "库");
                var 库文件 = 帮助站点C.读库目录(库目录);
                var 详情 = new List<string>();
                bool 全好 = 库文件.Count > 0;
                foreach ((string 文件名, string 内容) in 库文件)
                {
                    var 方法们 = 帮助站点C.取公开方法(内容);
                    string 页 = 假库正文(内容);
                    int 表行 = 页.Split('\n').Count(行 => 行.StartsWith("| ", StringComparison.Ordinal)) - 1;   // 减表头
                    bool 头全 = 页.StartsWith(帮助站点C.取头注释(内容), StringComparison.Ordinal);
                    bool 好 = 表行 == 方法们.Count && 头全 && !页.Contains("(库文件没有开头说明注释)", StringComparison.Ordinal);
                    全好 &= 好;
                    详情.Add($"{文件名} 表 {表行}/{方法们.Count} 头全 {头全}");
                }
                记录("帮助站点 每个库一页都有**公开方法表**,条数 = 库源码里的公开 `方法` 声明数(一个不落)",
                    全好, $"{库文件.Count} 个库:{string.Join(" ; ", 详情.Take(6))}{(详情.Count > 6 ? " …" : "")}");
            });

            // ---- ③ 加一条就多一条(七张表各追加一条假的) ----
            跑且断言("帮助站点 表里加一条,生成的节就多一条(七类都验)", () =>
            {
                var 假类型 = 类型C.表.Append(new 类型C.类型定义(基本类型.整数, "假类型", "faketype", 4, 有符号: true)).ToArray();
                var 假指令 = 指令C.全部指令.Append(("假指令", "FAKE", (byte)0xF0, 指令格式.无操作数)).ToArray();
                var 假关键字 = 中间层指令C.关键字表.Append(("假关键字", "fakekeyword")).ToArray();
                var 假内建 = 中间层指令C.内建函数名表.Append(("假内建", "fakebuiltin")).ToArray();
                var 假方法 = 中间层指令C.方法名表.Append(("假方法", "fakemethod")).ToArray();
                var 假上限 = 硬上限C.表.Append(new 硬上限项("假上限", "99 个", "假写法", "假改法")).ToArray();
                帮助节点C 假树 = 帮助站点C.建树(null, 假类型, 假指令, 假关键字, 假内建, 假方法, 假上限);

                bool 上限对 = 数节(假树, 帮助站点C.硬上限节名) == 硬上限C.表.Length + 1;
                bool 类型对 = 数节(假树, 帮助站点C.类型节名) == 期望类型数 + 1;
                bool 指令对 = 指令叶子数(假树) == 指令C.全部指令.Count + 1;
                bool 关键字对 = 数节(假树, 帮助站点C.关键字节名) == 关键字条数() + 1;
                bool 内建对 = 数节(假树, 帮助站点C.内建节名) == 中间层指令C.内建函数名表.Length + 1;
                bool 方法对 = 数节(假树, 帮助站点C.方法节名) == 中间层指令C.方法名表.Length + 1;
                记录("帮助站点 表里加一条,生成的节就多一条(七类都验)",
                    上限对 && 类型对 && 指令对 && 关键字对 && 内建对 && 方法对,
                    $"硬上限 {上限对} / 类型 {类型对} / 指令 {指令对} / 关键字 {关键字对} / 内建 {内建对} / 方法 {方法对}");
            });

            // ---- ⑤ C25-5:手写那三节(介绍 / 上手 / 语言细节)—— 正文与 帮助文本C 是同一份 ----
            var 上手节表 = 帮助站点C.拆上手(帮助文本C.快速上手(), out string 上手引言);
            帮助节点C? 上手节 = 帮助站点C.找节(树, 帮助站点C.上手节名);
            bool 上手对得上 = 上手节 != null && 上手节表.Count > 0 && 上手引言.Length > 0
                && 上手节.子节点.Count == 上手节表.Count
                && 上手节.子节点.Select(x => x.标题).SequenceEqual(上手节表.Select(x => x.标题))
                && 上手节.子节点.Select(x => x.正文).SequenceEqual(上手节表.Select(x => x.正文))
                && 上手节.正文.Contains(上手引言, StringComparison.Ordinal);
            记录("帮助站点 上手节 = 快速上手() 现场拆出来的每一节(手写正文只有一份,标题逐条对上)",
                上手对得上,
                $"站点 {上手节?.子节点.Count ?? -1} 节 / 拆分 {上手节表.Count} 节;引言 {上手引言.Length} 字符");

            // G3 域 6 抓到的坑:**标题必须顶格**。拆分是按"行首 `15,…`"认标题的 ——
            // 标题多缩一格(前几批真发生过:《第29节·回调》/《第30节·流式读写》缩了一格,
            // 《第31节·网络》《第32节·运行时》缩了两格)它就不算标题,整段并进**上一节**,
            // 站点上"少几页"却一声不响(站点就是在这份正文上现场拆的,两边一起错就永远对得上)。
            // 判据:把每一行 `TrimStart` 之后**再拆一遍**,两边的节数必须一样 ——
            // 只用一个事实源(`帮助站点C.拆上手` 自己那套正则),不在这里手抄第二条正则。
            string 上手原文 = 帮助文本C.快速上手();
            string 上手去缩进 = string.Join("\n",
                上手原文.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(行 => 行.TrimStart()));
            int 按原文拆 = 帮助站点C.拆上手(上手原文).Count;
            int 去缩进后拆 = 帮助站点C.拆上手(上手去缩进).Count;
            记录("帮助站点 上手节的标题一律顶格(缩了一格就会并进上一节、站点少一页而不报错)",
                按原文拆 > 0 && 按原文拆 == 去缩进后拆 && 上手节?.子节点.Count == 按原文拆,
                $"行首标题 {按原文拆} 个 / 去掉缩进后 {去缩进后拆} 个(多出来的就是缩进过的标题);" +
                $"站点里 {上手节?.子节点.Count ?? -1} 节");

            var 细节节表 = 帮助站点C.拆方括号节(帮助文本C.中间层速查());
            帮助节点C? 细节节 = 帮助站点C.找节(树, 帮助站点C.语言细节节名);
            bool 细节对得上 = 细节节 != null && 细节节表.Count > 0
                && 细节节.子节点.Count == 细节节表.Count
                && 细节节.子节点.Select(x => x.标题).SequenceEqual(细节节表.Select(x => x.标题));
            bool 细节不重复生成节 = 细节节 != null
                && !细节节.子节点.Any(子 => 子.标题.StartsWith("关键字", StringComparison.Ordinal)
                    || 子.标题.StartsWith("内建函数", StringComparison.Ordinal));
            记录("帮助站点 语言细节节 = 中间层速查() 的手写说明(关键字 / 内建两段不重复进)",
                细节对得上 && 细节不重复生成节,
                $"站点 {细节节?.子节点.Count ?? -1} 节 / 拆分 {细节节表.Count} 节");

            帮助节点C? 介绍节 = 帮助站点C.找节(树, 帮助站点C.介绍节名);
            记录("帮助站点 介绍节有两条子项(语言特性速览 / 文档分工),正文都非空",
                介绍节 is { 正文.Length: > 0 } && 介绍节.子节点.Count == 2
                && 介绍节.子节点.All(子 => 子.正文.Length > 0)
                && 介绍节.子节点.Any(子 => 子.标题.Contains("语言特性速览", StringComparison.Ordinal))
                && 介绍节.子节点.Any(子 => 子.标题.Contains("这些文档", StringComparison.Ordinal)),
                介绍节 == null ? "找不到介绍节" : string.Join(" / ", 介绍节.子节点.Select(x => x.标题)));

            // ---- ⑥ C25-5 的防过期钉子:C25-1 销账过的"过期说法"一处都不许回来 ----
            string[] 过期说法 =
            [
                "<项目名>.hba", "十二个内建函数", "48 个标准工程", "帧只有 64 字节", "软栈 2112",
                "测试输出(值) 是中间层糖", "不能放进结构/数组", "不能直接写在表达式里", "功能到 M8",
                "一共九个",   // 第170批:主题与选项收进工具菜单,顶级菜单 9 → 7
                "名字[下标]` 读和**等长**改写",          // 第173批(H1):不等长写回早就实现了,帮助里不许再写"只支持等长"
                "还没做(排队):`名字[下标] = 文字`",     // 同上:那一整句"排队"是过期说法
                "字符串字段还没做",                      // H1:`记录.名字[下标]` 已经做了
                "字符串列表暂时不支持",                  // H1:`字符串 名单[]` 已经做了
                "个数不定的列表(`字符串 名单[]`)还不支持", // 同上
                "字段/数组里先不放列表",                 // H1 第四件(第174批):结构字段里能放动态列表了
                "结构里先不放列表",                      // 同上:那句"要可变个数就用顶层列表"是过期说法
                "手机端没有输入界面",                    // H29(第201批):手机端有输入框 + 「发送」了
                "优化开 4096 字节 / 关 3072 字节",       // R1-1(第204批):软栈改成可配 + 默认按内存算,不再按优化分两档
                "优化开 4096 字节(约 42 层调用深度)",     // 同上
                "约 42 层调用深度",                      // 同上:老口径的层数说法
                "约 32 层",                              // 同上
            ];
            string 四页 = 帮助文本C.快速上手() + 帮助文本C.指令速查() + 帮助文本C.中间层速查() + 帮助文本C.关于();
            string[] 还在 = 过期说法.Where(坏 => 四页.Contains(坏, StringComparison.Ordinal)).ToArray();
            记录("帮助文本 过期说法一处不留(C25-1 销账过的说法不许回来)",
                还在.Length == 0,
                还在.Length == 0 ? $"{过期说法.Length} 条逐条查过" : "还在: " + string.Join(" / ", 还在));

            // ---- ④ 大纲文本:每个锚点都出现(C25-3 靠锚点跳转) ----
            记录("帮助站点 大纲文本含全部节点(每行 标题 [锚点])",
                大纲对得上(树, 帮助站点C.大纲文本(树)),
                $"大纲 {帮助站点C.大纲文本(树).Length} 字符 / {帮助站点C.节点数(树)} 个节点");
        }

        /// <summary>
        /// C25-3(第166批;第241批改**多页站点**):帮助**静态网页**的验收(`引擎\帮助站点C.HTML.cs`)。
        ///
        /// 形态 = `帮助\index.html`(总目录)+ `帮助\目录\&lt;锚点&gt;.html`(**一个话题一页**)+ `帮助\样式.css`
        /// (全站一份样式)+ `帮助\索引-&lt;分类&gt;.html`(按用途分类的索引页),零依赖不用 JavaScript。
        /// 这里钉七件事:
        /// <list type="number">
        /// <item>**一点不漏**:每个叶子节点恰好一页(`页面 → 锚点 → 页面` 逐一对照);</item>
        /// <item>**一页清一味**:页面里不混进别的节点的标题(改了"一节一页"之后最容易犯的错);</item>
        /// <item>**链接不断**:每页里的**站内链接**都指得到真文件(改了节名 / 加了节点,自检当场红灯);</item>
        /// <item>**零依赖**:没有 `&lt;script&gt;` / `onclick` / `&lt;iframe&gt;`,只有一份相对路径的样式 link;</item>
        /// <item>**标签配平**(自己拼的 HTML,开合数必须对上);</item>
        /// <item>**重新生成逐字节相同** + **盘上那份没过期**(改了表忘了跑 `hbcc --生成帮助` 就红灯);</item>
        /// <item>**写盘只多不留孤儿**:`目录\` 里这一轮没生成的旧页会被清掉(改名之后不留断链的死页)。</item>
        /// </list>
        /// </summary>
        private static void 帮助站点HTML自检()
        {
            string? 示例根 = 找示例目录();
            string? 仓库根 = 示例根 == null ? null : Directory.GetParent(示例根)?.FullName;
            帮助节点C 树 = 仓库根 != null ? 帮助站点C.建整树(仓库根) : 帮助站点C.建树();
            var 产物 = 帮助站点C.全部页面(树);
            帮助节点C[] 全节 = [.. 帮助站点C.全部节点(树)];
            List<帮助节点C> 叶子 = 帮助站点C.取叶子(树);
            string 首页文 = 产物[帮助站点C.站点文件名];

            // ---- ① 骨架:首页是总目录(左 nav 树 + 右 main 内容区) ----
            bool 骨架齐 = 首页文.StartsWith("<!DOCTYPE html>\n", StringComparison.Ordinal)
                && 首页文.Contains("<html lang=\"zh-CN\">", StringComparison.Ordinal)
                && 首页文.Contains("<meta charset=\"utf-8\">", StringComparison.Ordinal)
                && 首页文.Contains("<title>", StringComparison.Ordinal)
                && 首页文.Contains("<nav", StringComparison.Ordinal) && 首页文.Contains("</nav>", StringComparison.Ordinal)
                && 首页文.IndexOf("<nav", StringComparison.Ordinal) < 首页文.IndexOf("<main", StringComparison.Ordinal)
                && 首页文.Contains("</main>", StringComparison.Ordinal)
                && 首页文.EndsWith("</html>\n", StringComparison.Ordinal);
            记录("帮助站点 首页骨架:nav 树在前 / main 内容区在后,收尾是 </html>",
                 骨架齐, $"首页 {首页文.Length} 字符 / 全站 {产物.Count} 个文件 / {帮助站点C.节点数(树)} 个节点");

            // ---- ② 一个叶子一页(不漏 / 不重);首页 / 分类索引 / 样式都在产物里 ----
            var 页缺 = 叶子.Where(叶 => !产物.ContainsKey(帮助站点C.页相对路径(叶.锚点)))
                            .Select(叶 => 叶.锚点).Take(5).ToArray();
            int 页产物数 = 产物.Keys.Count(x => x.StartsWith(帮助站点C.页目录名 + "/", StringComparison.Ordinal));
            记录("帮助站点 每个叶子一页(话题页数 = 叶子数,不多不少)",
                 页缺.Length == 0 && 页产物数 == 叶子.Count
                 && 产物.ContainsKey(帮助站点C.站点文件名) && 产物.ContainsKey(帮助站点C.样式文件名)
                 && 帮助站点C.群表.All(群 => 产物.ContainsKey($"索引-{群}.html")),
                 页缺.Length > 0 ? "缺: " + string.Join(" ", 页缺)
                                 : $"{页产物数} 页 / 叶子 {叶子.Count};加首页 / 分类索引 {帮助站点C.群表.Length} 份 / 一份样式");

            // ---- ③b 分类索引:每一份都挂得住(至少一行)—— `类库` 那节的子项要仓库根才有,
            //          所以只按产物查(工作副本之外跑自检时它照样成立)
            int 索引行数(string 群) => System.Text.RegularExpressions.Regex.Matches(
                产物[$"索引-{群}.html"], "<tr><td>").Count;
            var 索引缺 = 帮助站点C.群表.Where(群 => !产物.ContainsKey($"索引-{群}.html") || 索引行数(群) == 0).ToArray();
            记录("帮助站点 分类索引每份都挂得住(每份至少一行)",
                 索引缺.Length == 0,
                 索引缺.Length > 0 ? "挂空: " + string.Join(" ", 索引缺)
                                   : string.Join(" ", 帮助站点C.群表.Select(群 => $"{群}{索引行数(群)}")));

            // ---- ④ 链接不断:每页里的站内链接都指得到真产物 ----
            int 查过链接 = 0;
            var 断链 = new List<string>();
            foreach ((string 相对, string 文本) in 产物)
            {
                if (!相对.EndsWith(".html", StringComparison.Ordinal)) continue;
                string 前缀 = 相对.Contains('/', StringComparison.Ordinal) ? "../" : "";
                int 位 = 0;
                while ((位 = 文本.IndexOf("href=\"", 位, StringComparison.Ordinal)) >= 0)
                {
                    int 起 = 位 + 6, 止 = 文本.IndexOf('"', 起);
                    if (止 < 0) break;
                    string 目标 = 文本[起..止];
                    位 = 止;
                    if (目标.StartsWith('#')) continue;                          // 页内锚点:这一类不生成 id
                    string 净 = 目标.StartsWith(前缀, StringComparison.Ordinal) ? 目标[前缀.Length..] : 目标;
                    int 井 = 净.IndexOf('#');
                    if (井 >= 0) 净 = 净[..井];
                    if (净.Length == 0) continue;
                    查过链接++;
                    if (!产物.ContainsKey(净)) 断链.Add($"{相对}→{目标}");
                }
            }
            记录("帮助站点 每页的站内链接都指得到真文件(改了节名 / 加了节点就断)",
                 断链.Count == 0 && 查过链接 > 0,
                 断链.Count > 0 ? "断链: " + string.Join(" ", 断链.Take(5)) : $"查过 {查过链接} 条链接");

            // 导航两条路:首页点得到十大节 + 分类索引点得到
            var 首页缺 = 树.子节点.Where(节 =>
            {
                List<帮助节点C> 这一支 = 帮助站点C.取叶子(节);
                return 这一支.Count == 0
                    || !首页文.Contains($"href=\"{帮助站点C.页相对路径(这一支[0].锚点)}\"", StringComparison.Ordinal);
            }).Select(节 => 节.标题).ToArray();
            记录("帮助站点 首页点得到十大节 + 分类索引(导航两条路都通)",
                 首页缺.Length == 0
                 && 帮助站点C.群表.All(群 => 首页文.Contains($"href=\"索引-{群}.html\"", StringComparison.Ordinal)),
                 首页缺.Length > 0 ? "缺: " + string.Join(" ", 首页缺) : $"{树.子节点.Count} 节 + {帮助站点C.群表.Length} 份索引");

            // ---- ⑤ 排得开:几条"很密"的老病(一条长龙)在版式里治住了 ----
            string 样式 = 产物[帮助站点C.样式文件名];
            bool 行高 = 样式.Contains("font: 15px/1.9", StringComparison.Ordinal);
            bool 条目表 = 样式.Contains("table.索引表", StringComparison.Ordinal);
            bool 限宽 = 样式.Contains("max-width: 54rem", StringComparison.Ordinal);
            bool 面包屑 = 样式.Contains(".面包屑", StringComparison.Ordinal);
            bool 正文 = 样式.Contains("body { margin: 0; color: #24292f", StringComparison.Ordinal);
            bool 宽屏 = 样式.Contains("@media", StringComparison.Ordinal);
            记录("帮助站点 排版放宽(行高 / 段距 / 条目表 / 正文限宽都进了样式表)",
                 行高 && 条目表 && 限宽 && 面包屑 && 正文 && 宽屏,
                 $"样式 {样式.Length} 字符;行高 {行高} / 条目表 {条目表} / 限宽 {限宽} / 面包屑 {面包屑} / 正文 {正文} / 宽屏 {宽屏}");

            // ---- ⑥ 零依赖:不用 JavaScript、不链外部资源(样式只有一份相对路径的 link) ----
            string[] 外链 = ["<script", "onclick=", "<iframe"];
            记录("帮助站点 零依赖不用 JavaScript(无 script / onclick / iframe;样式只有一份相对路径的 link)",
                 产物.Values.All(文 => 外链.All(坏 => !文.Contains(坏, StringComparison.OrdinalIgnoreCase)))
                 && 产物.Where(个 => 个.Key.EndsWith(".html", StringComparison.Ordinal))
                         .All(个 => 出现次数(个.Value, "<link rel=\"stylesheet\"") == 1),
                 string.Join(" ", 外链.Select(坏 => $"{坏}:{(产物.Values.Any(文 => 文.Contains(坏, StringComparison.OrdinalIgnoreCase)) ? "有" : "无")}")));

            // ---- ⑦ 标签配平(首页 + 一份索引 + 抽 30 页) ----
            // 开标签按"标签名 + 词边界"数(别漏掉带属性的:`<li class="当前">` / `<table class="索引表">` /
            // `<details open>` / `<main class="内容区">`,都是同一个坑:`<li>` 这种写法数不到它们)
            string[] 要配平 = ["html", "nav", "main", "details", "summary", "ul", "li", "article",
                                "code", "strong", "h1", "h2", "table", "thead", "tbody", "tr", "td"];
            List<(string 名, string 文)> 查样本 =
            [
                ("首页", 首页文),
                ("分类索引", 产物[$"索引-{帮助站点C.类型节名}.html"]),
            ];
            查样本.AddRange(产物.Where(个 => 个.Key.StartsWith(帮助站点C.页目录名 + "/", StringComparison.Ordinal))
                                .Take(30).Select(个 => (个.Key, 个.Value)));
            var 不平 = new List<string>();
            foreach ((string 名, string 文) in 查样本)
                foreach (string 标签 in 要配平)
                {
                    int 开 = System.Text.RegularExpressions.Regex.Matches(文, $"<{标签}(\\s|>)").Count;
                    int 闭 = 出现次数(文, $"</{标签}>");
                    if (开 != 闭) 不平.Add($"{名}:{标签} {开}/{闭}");
                }
            记录("帮助站点 开合标签数配平(首页 + 一份索引 + 抽 30 页,17 种标签逐种数过)",
                 不平.Count == 0, 不平.Count == 0 ? $"查了 {查样本.Count} 页" : string.Join(";", 不平.Take(5)));

            // ---- ⑧ 转义:标题 / 正文里的 < > & " 一律变实体,不破结构 ----
            跑且断言("帮助站点 转义:标题与正文里的 < > & \" 变成实体", () =>
            {
                // 锚点照真口径起(`锚点()` 会把符号换成 `-`)—— 别拿带 `<` `"` 的字符串直接当锚点,那种节点真站点里不存在
                帮助节点C 假根 = new() { 标题 = "假根", 锚点 = 帮助站点C.锚点("假", "根"), 正文 = "根" };
                // 叶子的锚点不跟标题走(标题里带 `<` `"` 时 `锚点()` 会把它们换成 `-`,那种名字真站点里没有)
                帮助节点C 假叶 = new()
                {
                    标题 = "<b>标题 & 引号\"</b>",
                    锚点 = 帮助站点C.锚点("假", "节"),
                    正文 = "a < b & c > d `代码<1>` **粗 < 2**",
                };
                帮助节点C 假叶二 = new()
                {
                    标题 = "<script>alert(1)</script>",
                    锚点 = 帮助站点C.锚点("假", "孙"),
                    正文 = "<img src=x>",
                };
                假根.加(假叶);      // 一个分组两个叶子 ⇒ 两个叶子各成一页,分组自己不出页
                假根.加(假叶二);
                var 假产物 = 帮助站点C.全部页面(假根);
                string 假页 = 假产物[帮助站点C.页相对路径(假叶.锚点)];
                string 假孙页 = 假产物[帮助站点C.页相对路径(假叶二.锚点)];
                // 分组页(有子节点的那些)在首页上:组正文 + 直接子项各一句摘要,两处都得转义
                string 假首页 = 假产物[帮助站点C.站点文件名];
                记录("帮助站点 转义:标题与正文里的 < > & \" 变成实体",
                    !假页.Contains("<b>标题", StringComparison.Ordinal)
                    && !假页.Contains("<script", StringComparison.Ordinal)
                    && 假页.Contains("&lt;b&gt;标题 &amp; 引号&quot;&lt;/b&gt;", StringComparison.Ordinal)
                    && 假页.Contains("<code>代码&lt;1&gt;</code>", StringComparison.Ordinal)
                    && 假页.Contains("<strong>粗 &lt; 2</strong>", StringComparison.Ordinal)
                    && !假孙页.Contains("<img", StringComparison.Ordinal)
                    && 假孙页.Contains("&lt;img src=x&gt;", StringComparison.Ordinal)
                    && !假首页.Contains("<script", StringComparison.Ordinal),
                    $"标题 / 正文 / 代码 / 粗体 / 第二个叶子 / 首页摘要六处都过(假树 {帮助站点C.节点数(假根)} 节点)");
            });

            // ---- ⑨ 重新生成一次逐字节相同 + 写盘只多不留孤儿 ----
            var 再生成 = 帮助站点C.全部页面(仓库根 != null ? 帮助站点C.建整树(仓库根) : 帮助站点C.建树());
            bool 逐字节同 = 再生成.Count == 产物.Count
                && 产物.All(个 => 再生成.TryGetValue(个.Key, out string? 文) && string.Equals(个.Value, 文, StringComparison.Ordinal))
                && 产物.Values.All(文 => !文.Contains('\r') && !文.Contains('\uFEFF'));
            记录("帮助站点 重新生成一次逐字节相同(不信时间戳 / 字典序 / 平台换行)",
                 逐字节同,
                 $"{产物.Count} 个文件,{产物.Values.Sum(文 => Encoding.UTF8.GetByteCount(文))} 字节");

            跑且断言("帮助站点 写盘 = UTF-8 无 BOM + 清掉改名留下的孤儿页", () =>
            {
                string 临时根 = Path.Combine(临时区C.基目录(), "hb帮助站点_" + Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    string 页目录 = Path.Combine(临时根, 帮助站点C.站点目录, 帮助站点C.页目录名);
                    Directory.CreateDirectory(页目录);
                    string 孤儿 = Path.Combine(页目录, "上上轮改过名的旧页.html");
                    File.WriteAllText(孤儿, "旧产物");
                    帮助站点C.写盘(临时根, 产物);
                    byte[] 盘上 = File.ReadAllBytes(帮助站点C.站点路径(临时根));
                    byte[] 该是 = new UTF8Encoding(false).GetBytes(首页文);
                    bool 首页对 = 盘上.SequenceEqual(该是)
                        && (盘上.Length < 3 || !(盘上[0] == 0xEF && 盘上[1] == 0xBB && 盘上[2] == 0xBF));
                    bool 一个不少 = 叶子.All(叶 => File.Exists(Path.Combine(页目录, 叶.锚点 + ".html")));
                    记录("帮助站点 写盘 = UTF-8 无 BOM + 清掉改名留下的孤儿页",
                         首页对 && 一个不少 && !File.Exists(孤儿),
                         $"首页 {盘上.Length} 字节 / 期望 {该是.Length};话题页齐 {一个不少};孤儿清掉 {!File.Exists(孤儿)}");
                }
                finally { try { Directory.Delete(临时根, true); } catch { /* 临时目录清不掉不影响判定 */ } }
            });

            // ---- ⑩ 盘上那份没过期(与规模表同一套办法:改了表忘了跑 --生成帮助 就红灯) ----
            // 第304批(H66):**每一页都逐字节比** —— 原来只比首页 + 页数,话题页正文漂了照样绿灯
            // (H64 那批就是这么把《第24节·手机端》漂在上一批的正文上的:标题没动 ⇒ 页数不变 ⇒ 首页也不变)。
            // 站点是用户可见文档(硬规矩 18):正文与源码两份口径不一致时,用户看到的是旧的那一份。
            try
            {
                if (仓库根 == null || !File.Exists(帮助站点C.站点路径(仓库根)))
                {
                    记录("帮助站点 盘上那份没过期(工作副本之外跑自检时跳过)",
                        !File.Exists(帮助站点C.站点路径(仓库根)), "找不到 帮助\\index.html");
                }
                else
                {
                    var 盘上产物 = 帮助站点C.全部页面(帮助站点C.建整树(仓库根));
                    var 缺页 = new List<string>();
                    var 漂页 = new List<string>();
                    foreach (var (相对, 该是) in 盘上产物)
                    {
                        string 路径 = Path.Combine(仓库根, 帮助站点C.站点目录,
                            相对.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(路径)) { 缺页.Add(相对); continue; }
                        if (!File.ReadAllBytes(路径).SequenceEqual(new UTF8Encoding(false).GetBytes(该是)))
                            漂页.Add(相对);
                    }
                    bool 样式在 = File.Exists(帮助站点C.样式路径(仓库根));
                    bool 过期 = 缺页.Count > 0 || 漂页.Count > 0 || !样式在;
                    记录("帮助站点 盘上那份没过期(每一页逐字节一致 + 样式在)",
                        !过期,
                        过期
                            ? $"缺页 {缺页.Count} 个{(缺页.Count > 0 ? "(" + string.Join(" / ", 缺页.Take(3)) + ")" : "")};" +
                              $"正文漂了 {漂页.Count} 页{(漂页.Count > 0 ? "(" + string.Join(" / ", 漂页.Take(3)) + ")" : "")};" +
                              $"样式在={样式在} —— 跑 `hbcc --生成帮助`"
                            : $"{盘上产物.Count} 页逐字节一致(含 {叶子.Count} 个话题页)+ 样式在");
                }
            }
            catch (Exception ex) { 记录异常("帮助站点 盘上那份没过期", ex); }
        }

        /// <summary>
        /// C25-4(第167批):帮助站点的**接线**验收(`引擎\帮助站点C.HTML.cs` 的路径 / 网址 / 提示 +
        /// IDE 侧"帮助菜单改成浏览器打开")。
        ///
        /// 钉四件事:
        /// <list type="number">
        /// <item>**找得到**:`找已生成站点()` 从程序目录往上找到的就是仓库根那份 `帮助\index.html`;</item>
        /// <item>**网址组得对**:`file:///` 开头、非 ASCII 与空格都转义、锚点走 `#…`(路径**不能**直接喂
        ///   `Process.Start` —— 里面的 `#` 会被 shell 当成文件名的一部分);</item>
        /// <item>**没生成过会说人话**:提示里带"跑哪条命令"(`hbcc --生成帮助`),且合文案规范;</item>
        /// <item>**只有一套帮助**:按源码钉住"菜单四条都走 `打开帮助站点`"、`MainWindow.xaml` 还挂着这四条,
        ///   而且旧的 WPF `帮助窗口` 确实不在仓里了(免得两份帮助文案又互相打脸)。</item>
        /// </list>
        /// </summary>
        private static void 帮助站点接线自检()
        {
            string? 示例根 = 找示例目录();
            string? 仓库根 = 示例根 == null ? null : Directory.GetParent(示例根)?.FullName;

            // ---- ① 找已生成站点:程序在 bin 里,往上几层就是仓库根 ----
            跑且断言("帮助站点 找得到已生成的那一份(从程序目录往上找)", () =>
            {
                string? 找到 = 帮助站点C.找已生成站点();
                string 该是 = 仓库根 == null ? "" : 帮助站点C.站点路径(仓库根);
                if (该是.Length == 0 || !File.Exists(该是))
                {
                    跳过("帮助站点 找得到已生成的那一份(工作副本之外跑自检时跳过)", "工作副本之外跑自检,没找到 帮助\\index.html");
                    return;
                }
                记录("帮助站点 找得到已生成的那一份(从程序目录往上找,给的是仓库根那份)",
                     string.Equals(找到, 该是, StringComparison.OrdinalIgnoreCase),
                     找到 ?? "(null)");
            });

            // ---- ② 网址:file:/// + 转义;跳某一节先翻成"那一页" ----
            跑且断言("帮助站点 网址组得对(file:/// + 转义;跳节先翻成那一页)", () =>
            {
                string 假路径 = @"C:\临时 目录\a b\index.html";
                string 网址 = 帮助站点C.网址(假路径);
                // 菜单跳节那条路:锚点 → 哪一页(拿真仓库根试;工作副本之外给不了就只查"首页"这一半)
                bool 翻页对了 = 仓库根 == null
                    || (帮助站点C.文件相对路径(仓库根, 帮助站点C.类型节名) == 帮助站点C.页相对路径(帮助站点C.找节(帮助站点C.建整树(仓库根), 帮助站点C.类型节名)!.子节点[0].锚点)
                        && 帮助站点C.文件相对路径(仓库根) == 帮助站点C.站点文件名
                        && 帮助站点C.文件相对路径(仓库根, "根本没这一节") == 帮助站点C.站点文件名);
                // 汉语路径要百分号编码(`类型` = %E7%B1%BB%E5%9E%8B),空格要 %20,原文一个汉字都不许留在网址里
                bool 转义对 = 网址.StartsWith("file:///", StringComparison.Ordinal)
                    && 网址.Contains("%20", StringComparison.Ordinal)
                    && !网址.Contains("临时", StringComparison.Ordinal)
                    && !网址.Contains("目录", StringComparison.Ordinal)
                    && Uri.UnescapeDataString(网址).EndsWith("index.html", StringComparison.Ordinal);
                记录("帮助站点 网址组得对(file:/// + 转义;跳节先翻成那一页)",
                    转义对 && 翻页对了, $"网址:{网址} / 翻页:{翻页对了}");
            });

            // ---- ③ 没生成过时的提示:说清怎么办,且合文案规范 ----
            string 提示 = 帮助站点C.没生成过的提示;
            string[] 禁词 = ["！", "吧", "啦", "哦", "呀", "嘛", "呢", "啊", "哈"];
            记录("帮助站点 没生成过时说清怎么办(带 hbcc --生成帮助,不含口语 / 感叹号)",
                 提示.Contains("--生成帮助", StringComparison.Ordinal)
                 && 提示.Contains("帮助\\index.html", StringComparison.Ordinal)
                 && 禁词.All(坏 => !提示.Contains(坏, StringComparison.Ordinal)),
                 $"提示 {提示.Length} 字符");

            // 菜单四条跳的锚点必须在树上查得到(改了节名不至于跳空)
            帮助节点C 树 = 仓库根 != null ? 帮助站点C.建整树(仓库根) : 帮助站点C.建树();
            var 跳空 = new[] { 帮助站点C.指令节名, 帮助站点C.关键字节名 }
                .Where(名 => 帮助站点C.按锚点找(树, 名) == null).ToArray();
            记录("帮助站点 菜单跳的锚点在树上查得到(指令 / 关键字)",
                 跳空.Length == 0, 跳空.Length == 0 ? "两条都查得到" : "缺: " + string.Join(" ", 跳空));

            // ---- ④ 只有一套帮助:菜单走浏览器 + 旧窗口确实下线 ----
            if (仓库根 == null)
            {
                记接线("帮助站点 IDE 接线:四条菜单都走浏览器打开,旧 WPF 帮助窗口已下线(工作副本之外跳过)",
                     true, "找不到仓库根");
                return;
            }
            // ---- ⑤ C25-5:发布检查单在仓库根(手写那几节的人工核对入口) ----
            string 检查单路径 = Path.Combine(仓库根, "发布检查单.md");
            bool 检查单在 = File.Exists(检查单路径);
            string 检查单文 = 检查单在 ? File.ReadAllText(检查单路径) : "";
            记接线("发布检查单 在仓库根,写着手写帮助的人工核对与机械命令",
                检查单在 && 检查单文.Contains("帮助核对表.md", StringComparison.Ordinal)
                && 检查单文.Contains("hbcc --生成帮助", StringComparison.Ordinal)
                && 检查单文.Contains("hbcc --selftest", StringComparison.Ordinal)
                && 检查单文.Contains("自检断言清单.txt", StringComparison.Ordinal),
                检查单在 ? $"{检查单文.Length} 字符" : "找不到 发布检查单.md");

            跑且断言("帮助站点 IDE 接线:四条菜单都走浏览器打开,旧 WPF 帮助窗口已下线", () =>
            {
                string 主窗 = File.ReadAllText(Path.Combine(仓库根, "MainWindow.主题与选项.cs"));
                string 界面 = File.ReadAllText(Path.Combine(仓库根, "MainWindow.xaml"));
                bool 四条走站点 = 主窗.Contains("帮助快速上手_Click(object sender, RoutedEventArgs e) => 打开帮助站点(", StringComparison.Ordinal)
                    && 主窗.Contains("帮助指令速查_Click(object sender, RoutedEventArgs e) => 打开帮助站点(帮助站点C.指令节名)", StringComparison.Ordinal)
                    && 主窗.Contains("帮助中间层速查_Click(object sender, RoutedEventArgs e) => 打开帮助站点(帮助站点C.关键字节名)", StringComparison.Ordinal)
                    && 主窗.Contains("帮助关于_Click(object sender, RoutedEventArgs e) => 打开帮助站点(\"\")", StringComparison.Ordinal);
                bool 菜单还挂着 = 界面.Contains("Click=\"帮助快速上手_Click\"", StringComparison.Ordinal)
                    && 界面.Contains("Click=\"帮助指令速查_Click\"", StringComparison.Ordinal)
                    && 界面.Contains("Click=\"帮助中间层速查_Click\"", StringComparison.Ordinal)
                    && 界面.Contains("Click=\"帮助关于_Click\"", StringComparison.Ordinal);
                bool 旧窗口下线 = !File.Exists(Path.Combine(仓库根, "界面控件", "帮助窗口.xaml"))
                    && !File.Exists(Path.Combine(仓库根, "界面控件", "帮助窗口.xaml.cs"))
                    && !主窗.Contains("帮助窗口.显示(", StringComparison.Ordinal);   // 注释里提一句不算,代码里还有人调它才算
                bool 走浏览器 = 主窗.Contains("UseShellExecute = true", StringComparison.Ordinal)
                    && 主窗.Contains("帮助站点C.找已生成站点()", StringComparison.Ordinal)
                    && 主窗.Contains("帮助站点C.没生成过的提示", StringComparison.Ordinal);
                记接线("帮助站点 IDE 接线:四条菜单都走浏览器打开,旧 WPF 帮助窗口已下线",
                     四条走站点 && 菜单还挂着 && 旧窗口下线 && 走浏览器,
                     $"菜单四条 {四条走站点} / MainWindow.xaml 还挂着 {菜单还挂着} / 旧窗口已下线 {旧窗口下线} / 走系统浏览器 {走浏览器}");
            });

            // ---- ⑥ 第169批(C28):"打开"那条命令怎么套(纯函数,不碰注册表也不起进程) ----
            // 用户实测的"帮助菜单点了没反应"根因之一:shell 按 `.html` 的文件关联开不起来
            // (`AssocQueryString` 只回 `OpenWith.exe`、`file` 协议回 `0x80070483`)⇒ 改走"按注册表的默认浏览器开"。
            // 这里钉住"命令模板 → 命令行"那一步(注册表那一步在界面层,自检够不着)。
            // **第170批改口径**:`--single-argument` 下面网址**裸放**(Edge 把引号当成网址原文,
            // 用户实测报 `DNS_PROBE_FINISHED_NXDOMAIN`,Edge 会话文件里那条是 `http://"file///…index.html%22`)。
            var (可执行文件, 参数) = 浏览器命令C.套命令行(
                "\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" --single-argument %1",
                "file:///E:/x/index.html");
            记录("浏览器命令 带引号的可执行文件 + `%1` 套对(默认浏览器注册表里就是这种写法)",
                可执行文件 == "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe"
                && 参数 == "--single-argument file:///E:/x/index.html",
                $"{可执行文件} | {参数}");

            var (_, 参数单参数带引号) = 浏览器命令C.套命令行(
                "\"C:\\浏览器.exe\" --single-argument \"%1\"", "file:///w");
            记录("浏览器命令 `--single-argument` 下面网址不加引号(模板自带引号也去掉)",
                参数单参数带引号 == "--single-argument file:///w", 参数单参数带引号);

            var (_, 参数带引号) = 浏览器命令C.套命令行("\"C:\\浏览器.exe\" \"%1\"", "file:///y");
            记录("浏览器命令 占位符自带引号时不套出两个引号", 参数带引号 == "\"file:///y\"", 参数带引号);

            var (_, 参数无占位) = 浏览器命令C.套命令行("\"C:\\浏览器.exe\"", "file:///z");
            记录("浏览器命令 模板里没有占位符时把网址接在参数尾巴上", 参数无占位 == "\"file:///z\"", 参数无占位);

            var (空路径, _) = 浏览器命令C.套命令行("   ", "file:///z");
            var (半引号, _) = 浏览器命令C.套命令行("\"C:\\没闭合.exe --x %1", "file:///z");
            记录("浏览器命令 空模板 / 引号没闭合 ⇒ 两个空串(调用方据此换路,别硬套)",
                空路径.Length == 0 && 半引号.Length == 0, $"空模板 {空路径.Length} / 半引号 {半引号.Length}");

            // ---- ⑦ 第169批:用户实测的三个界面 bug,口径按源码钉住 ----
            跑且断言("第169批 三个界面 bug 的口径按源码钉住(帮助开路两条 / 关选项不补空白页 / 失败不静默)", () =>
            {
                string 主窗 = File.ReadAllText(Path.Combine(仓库根, "MainWindow.主题与选项.cs"));
                string 界面 = File.ReadAllText(Path.Combine(仓库根, "MainWindow.xaml"));

                bool 默认浏览器优先 = 主窗.Contains("默认浏览器C.打开(网址)", StringComparison.Ordinal)
                    && 主窗.Contains("UseShellExecute = true", StringComparison.Ordinal);          // shell 仍留作兜底
                bool 失败给人的是路径 = 主窗.Contains("打不开帮助站点", StringComparison.Ordinal)
                    && 主窗.Contains("可以手工用浏览器打开这个文件", StringComparison.Ordinal);
                bool 关选项不补页 = !主窗.Contains("// 只剩选项页", StringComparison.Ordinal)
                    && 主窗.Contains("if (目标 == null) 模型.选中页 = null;", StringComparison.Ordinal);
                bool 自检后台跑 = 主窗.Contains("await Task.Run(() => 综合自检C.运行())", StringComparison.Ordinal);
                bool 子菜单统一暗色 = 界面.Contains(
                        "<Style TargetType=\"MenuItem\" BasedOn=\"{StaticResource AllMenuItemStyle}\"/>",
                        StringComparison.Ordinal)
                    && 界面.Contains("<Trigger Property=\"IsChecked\" Value=\"True\">", StringComparison.Ordinal);   // 勾选别丢

                记接线("第169批 三个界面 bug 的口径按源码钉住(帮助开路两条 / 关选项不补空白页 / 失败不静默)",
                    默认浏览器优先 && 失败给人的是路径 && 关选项不补页 && 自检后台跑 && 子菜单统一暗色,
                    $"默认浏览器优先 {默认浏览器优先} / 失败给路径 {失败给人的是路径} / 关选项不补页 {关选项不补页} / 自检后台 {自检后台跑} / 子菜单暗色 {子菜单统一暗色}");
            });

            // ---- ⑧ 第170批:主题与选项收进「工具」菜单(顶级菜单 9 → 7) ----
            // 顶级菜单按**缩进 16 格**认(`MainWindow.xaml` 里 `Menu` 的直接子项都是这个缩进),
            // 别按"出现顺序"数 —— 子菜单项也在同一个文件里。
            跑且断言("第170批 顶级菜单七个:主题与选项都收在「工具」里", () =>
            {
                string 界面 = File.ReadAllText(Path.Combine(仓库根, "MainWindow.xaml"));
                string[] 顶级 = [.. 界面.Split('\n')
                    .Where(行 => 行.StartsWith("                <MenuItem Header=\"", StringComparison.Ordinal))
                    .Select(行 => { int 起 = 行.IndexOf('"') + 1; return 行[起..行.IndexOf('"', 起)]; })];
                string[] 期望 = ["文件(_F)", "编辑(_E)", "运行(_R)", "调试(_D)", "视图(_V)", "工具(_U)", "帮助(_H)"];
                bool 顶级对 = 顶级.SequenceEqual(期望, StringComparer.Ordinal);

                int 工具起 = 界面.IndexOf("                <MenuItem Header=\"工具(_U)\"", StringComparison.Ordinal);
                int 帮助起 = 界面.IndexOf("                <MenuItem Header=\"帮助(_H)\"", StringComparison.Ordinal);
                string 工具块 = 工具起 >= 0 && 帮助起 > 工具起 ? 界面[工具起..帮助起] : "";
                bool 工具里有 = 工具块.Contains("反汇编当前文件", StringComparison.Ordinal)
                    && 工具块.Contains("主题(_T)", StringComparison.Ordinal)
                    && 工具块.Contains("Click=\"菜单主题暗_Click\"", StringComparison.Ordinal)
                    && 工具块.Contains("Click=\"菜单主题亮_Click\"", StringComparison.Ordinal)
                    && 工具块.Contains("Click=\"菜单选项_Click\"", StringComparison.Ordinal);

                记接线("第170批 顶级菜单七个:主题与选项都收在「工具」里",
                    顶级对 && 工具里有,
                    $"顶级 {顶级.Length} 个[{string.Join(" ", 顶级)}] / 工具里有 {工具里有}");
            });
        }

        // ---------------- 小工具(只给这个自检用) ----------------

        private static string[] 一级节标题(帮助节点C 根) => [.. 根.子节点.Select(x => x.标题)];

        /// <summary> 一片文本在整份网页里出现几次(锚点 / `<details>` 条数都靠它数) </summary>
        private static int 出现次数(string 文本, string 片)
        {
            if (片.Length == 0) return 0;
            int 数 = 0, 位 = 0;
            while ((位 = 文本.IndexOf(片, 位, StringComparison.Ordinal)) >= 0) { 数++; 位 += 片.Length; }
            return 数;
        }

        /// <summary> 关键字那一节去掉基本类型名之后的条数(`建词节(去掉类型名: true)` 的口径) </summary>
        private static int 关键字条数()
        {
            HashSet<string> 类型名 = new(StringComparer.OrdinalIgnoreCase);
            foreach (类型C.类型定义 类 in 类型C.表) 类型名.Add(类.英文);
            foreach ((string _, string 英) in 中间层指令C.附加类型名表) 类型名.Add(英);
            return 中间层指令C.关键字表.Count(x => !类型名.Contains(x.英文));
        }

        /// <summary> 一段正文里 12 字以上的整句(串味比对用:短词不算,免得"清屏…"撞上"清空…") </summary>
        private static IEnumerable<string> 整句(string 正文)
        {
            foreach (string 句 in (正文 ?? "").Split(['。', ';', '\n', '(', ')', '；']))
            {
                string 净 = 句.Trim();
                if (净.Length >= 12) yield return 净;
            }
        }

        private static int 数节(帮助节点C 根, string 节名) =>
            帮助站点C.找节(根, 节名)?.子节点.Count ?? -1;

        private static int 指令叶子数(帮助节点C 根) =>
            帮助站点C.找节(根, 帮助站点C.指令节名)?.子节点.Sum(组 => 组.子节点.Count) ?? -1;

        private static bool 正文全非空(帮助节点C 根, string 节名) =>
            帮助站点C.找节(根, 节名) is { } 节 && 节.子节点.Count > 0
            && 节.子节点.All(子 => 子.正文.Length > 0);

        /// <summary> 建一棵只有一个假库文件的树,只为拿类库节的正文(看"没有注释"那句兜底话说没说) </summary>
        private static string 假库正文(string 库内容) =>
            帮助站点C.找节(帮助站点C.建树([("假.hb", 库内容)]), 帮助站点C.类库节名)?.子节点
                .FirstOrDefault()?.正文 ?? "";

        /// <summary> 大纲文本里每个节点都得有一行(标题 + 锚点),行数也要对得上 </summary>
        private static bool 大纲对得上(帮助节点C 根, string 大纲)
        {
            var 行 = 大纲.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (行.Length != 帮助站点C.节点数(根)) return false;
            foreach (帮助节点C 节 in 帮助站点C.全部节点(根))
                if (!大纲.Contains($"[{节.锚点}]", StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
