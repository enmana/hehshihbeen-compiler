using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary> 帮助站点的一个节点:标题 + 锚点 + 正文片段 + 子节点。**纯数据**(没有 UI、没有 HTML)。 </summary>
    public sealed class 帮助节点C
    {
        public string 标题 { get; set; } = "";
        /// <summary> 站内唯一锚点(`<类>-<名>`),写 HTML 的 `id` / 左边树的跳转链接都用它 </summary>
        public string 锚点 { get; set; } = "";
        public string 正文 { get; set; } = "";
        public List<帮助节点C> 子节点 { get; set; } = [];

        public 帮助节点C 加(帮助节点C 子) { 子节点.Add(子); return this; }
        public 帮助节点C 加(string 标题, string 锚点, string 正文 = "")
        {
            子节点.Add(new 帮助节点C { 标题 = 标题, 锚点 = 锚点, 正文 = 正文 });
            return this;
        }

        /// <summary> 深度优先展开(**含自己**)—— 自检数条数、界面 / 写盘遍历都用它 </summary>
        public IEnumerable<帮助节点C> 全部()
        {
            yield return this;
            foreach (帮助节点C 子 in 子节点)
                foreach (帮助节点C 孙 in 子.全部())
                    yield return 孙;
        }

        public override string ToString() => 标题;
    }

    /// <summary>
    /// C25-2:帮助文档的**章节树生成器** —— `表 → 章节树`(节点 = 标题 + 锚点 + 正文片段)。
    /// <para>
    /// 存在的理由:`引擎\帮助文本C.快速上手()` 那 23 节是**手写**的,编译器加了新东西它不会自己跟上
    /// (C25-1 的核对表 `帮助核对表.md` 把这件事逐条列了出来)。这里换一条路:**能生成的一律从表生成** ——
    /// 硬上限 / 类型 / 指令 / 关键字 / 内建函数 / 方法 六类都现场读单一事实源(硬上限 `硬上限C.表`、
    /// 类型 `类型C.表`、指令 `指令C.全部指令`、词表与说明 `中间层指令C`),类库一节读 `Sample\库\*.hb`
    /// 的头注释。**表里加一条,生成物就多一条**(自检拿假表钉住这条)。
    /// </para>
    /// <para>
    /// 口径(别按直觉改):
    /// ① **纯读表 + 产文本**:ISA / 语言面 / 机器码一个字不碰,也不认识界面;
    /// ② 树**只有骨架**,不管排版 —— 渲染成网页是 C25-3 的事(见 `引擎\帮助站点C.HTML.cs`:
    ///    左 `<nav>` 折叠树 / 右 `<main>` 内容区,零依赖不用 JavaScript),这一步只保证"节数 = 表里条目数";
    /// ③ 类库一节只取**文件头注释**(第一条真代码之前的 `//` 行,分隔线那种纯符号行丢掉)——库的对外函数有
    ///    `关键字与方法清单.md`,别在这儿再抄一份(别做成三份互相打脸,见 `帮助核对表.md` C25-5 那条);
    /// ④ `字符串` 不在 `类型C.表` 里(它是"头 + 堆"),但**必须查得到**,所以跟着
    ///    `中间层指令C.附加类型名表` 一起进类型节(说明直接复用 `说明表` 里 `string` 那一条)。
    /// </para>
    /// </summary>
    public static partial class 帮助站点C
    {
        /// <summary> 根节点标题(写盘时的站点名;第241批从 `HB 帮助` 改成它 ——
        /// 多页站点里这一层就是总目录,面包屑第一格写成"HB 帮助"会让人以为点回站点首页而不是目录) </summary>
        public const string 根标题 = "帮助总目录";

        // 一级节的**名字与锚点**(写 HTML 的树、自检找节点都按这串字认,别各处另写一份)
        public const string 硬上限节名 = "硬上限";
        public const string 类型节名 = "类型";
        public const string 指令节名 = "指令";
        public const string 关键字节名 = "关键字";
        public const string 内建节名 = "内建函数";
        public const string 方法节名 = "方法";
        public const string 类库节名 = "类库";

        /// <summary> 一级节的固定顺序:**先 C25-5 手写的三节**(介绍 / 上手 / 语言细节),
        /// 再是 R3-2 的硬上限一节,最后是从表生成的六节 </summary>
        public static readonly string[] 一级节 =
            [介绍节名, 上手节名, 语言细节节名, 硬上限节名, 类型节名, 指令节名, 关键字节名, 内建节名, 方法节名, 类库节名];

        /// <summary> 从表现场生成的节(自检钉住"生成几类齐全 + 顺序不变");硬上限一节也在这条线上 ——
        /// 它读 `硬上限C.表`,表里加一条站点就多一条 </summary>
        public static readonly string[] 生成节 = [硬上限节名, 类型节名, 指令节名, 关键字节名, 内建节名, 方法节名, 类库节名];

        // ---------------- 入口 ----------------

        /// <summary> 按当前版本的全部表建树(库文件由调用方给:`帮助站点C.读库目录(Sample\库)`) </summary>
        public static 帮助节点C 建树(IReadOnlyList<(string 文件名, string 内容)>? 库文件 = null) =>
            建树(库文件, 类型C.表, 指令C.全部指令, 中间层指令C.关键字表,
                 中间层指令C.内建函数名表, 中间层指令C.方法名表, 硬上限C.表);

        /// <summary>
        /// 建树的**真身**:七张表都从参数来(自检就靠这条喂假表验"加一条多一节");传真实表那一份见上面那个重载。
        /// </summary>
        public static 帮助节点C 建树(
            IReadOnlyList<(string 文件名, string 内容)>? 库文件,
            IReadOnlyList<类型C.类型定义> 类型表,
            IReadOnlyList<(string 中文, string 英文, byte 操作码, 指令格式 格式)> 指令表,
            IReadOnlyList<(string 中文, string 英文)> 关键字表,
            IReadOnlyList<(string 中文, string 英文)> 内建表,
            IReadOnlyList<(string 中文, string 英文)> 方法表,
            IReadOnlyList<硬上限项>? 硬上限表 = null)
        {
            帮助节点C 根 = new()
            {
                标题 = 根标题,
                锚点 = "根",
                正文 = "HB:中文优先的“高级语言 → 中间层 → 汇编”工具链。下面先是**手写**的三节(介绍 / 上手 / 语言细节)," +
                       "再是**从表现场生成**的七节:硬上限 / 类型 / 指令 / 关键字 / 内建函数 / 方法 / 类库 —— " +
                       "后七节表里加一条这里就多一条。",
            };
            根.加(建介绍节());
            根.加(建上手节());
            根.加(建语言细节节());
            根.加(建硬上限节(硬上限表 ?? 硬上限C.表));
            根.加(建类型节(类型表));
            根.加(建指令节(指令表));
            根.加(建词节(关键字节名, "语言关键字(块头 / 流程控制 / 声明 / 可见性…);中英两种写法都认",
                           关键字表, 带点: false, 去掉类型名: true));
            根.加(建词节(内建节名, "语言级内建函数(不是关键字,和 `申请` / `读取行` 同一档);中英可整体切换", 内建表));
            根.加(建词节(方法节名, "字符串 / 列表的带点方法(只能写在变量后面,如 `名字.长度()`);中英两侧都认", 方法表, 带点: true));
            根.加(建类库节(库文件));
            return 根;
        }

        // ---------------- 七节 ----------------

        /// <summary>
        /// R3-2:硬上限一节 —— 每条读 `硬上限C.表`(名 / 值 / 在哪 / 怎么绕)。
        /// 为什么让它进帮助:这些数字以前**只写在代码里**,用户撞上才知道(`独立复核` 二档 R3-2 的原话);
        /// 现在改成"表是唯一事实源,站点从表生成" ⇒ 以后调上限只改一处,文档跟着走。
        /// </summary>
        private static 帮助节点C 建硬上限节(IReadOnlyList<硬上限项> 表)
        {
            帮助节点C 节 = new()
            {
                标题 = 硬上限节名,
                锚点 = 硬上限节名,
                正文 = "这些是**撞上就当场报错**的硬上限(不是“慢慢变慢”),共 " + 表.Count + " 条;" +
                       "每条写清三件事:上限是多少、什么写法会撞上、撞上了怎么绕。" +
                       "R3-2 逐条评估过一次,**一条都没有放大** —— 每条的硬理由写在它自己的条目里。",
            };
            foreach (硬上限项 条 in 表)
            {
                节.加(new 帮助节点C
                {
                    标题 = 条.名,
                    锚点 = 锚点(硬上限节名, 条.名),
                    正文 = $"**上限 {条.值}**。哪里会撞上:{条.在哪};怎么绕:{条.怎么绕}",
                });
            }
            return 节;
        }

        /// <summary> 类型节:每个类型一条子项(中英名 / 宽度 / 范围 / 示例)+ `字符串` 那一条附加类型 </summary>
        private static 帮助节点C 建类型节(IReadOnlyList<类型C.类型定义> 表)
        {
            帮助节点C 节 = new()
            {
                标题 = 类型节名,
                锚点 = 类型节名,
                正文 = "基本类型:**不写类型就是 `整数`(32 位有符号)**;下面的宽度与范围就是编译器报错 / 截断 / 回绕的依据。",
            };
            foreach (类型C.类型定义 类 in 表)
            {
                节.加(new 帮助节点C
                {
                    标题 = $"{类.中文}({类.英文})",
                    锚点 = 锚点(类型节名, 类.中文),
                    正文 = $"{类.中文} / {类.英文} —— {类.宽度} 字节,{(类.有符号 ? "有符号" : "无符号")},范围 {类.范围文本};" +
                           $"写法 `{类.中文} 值 = 0;`",
                });
            }
            // `字符串` 不是基本类型(头 8 字节 + 堆里的字),类型C.表 里没有它 —— 补一条,别让它"查不到"
            foreach ((string 中, string 英) in 中间层指令C.附加类型名表)
            {
                节.加(new 帮助节点C
                {
                    标题 = $"{中}({英})",
                    锚点 = 锚点(类型节名, 中),
                    正文 = 中间层指令C.取说明(英)
                           ?? $"{中}({英}) —— 头 8 字节(数据地址 + 容量),真正的字在堆里(不是基本类型)",
                });
            }
            return 节;
        }

        /// <summary> 指令节:按编码格式分组,每条给中英名 / 操作码 / 格式 / 写法示例 </summary>
        private static 帮助节点C 建指令节(IReadOnlyList<(string 中文, string 英文, byte 操作码, 指令格式 格式)> 表)
        {
            帮助节点C 节 = new()
            {
                标题 = 指令节名,
                锚点 = 指令节名,
                正文 = $"全部指令固定 4 字节(小端),操作码在最高字节;共 {表.Count} 条,下面按**编码格式**分组。",
            };
            foreach (指令格式 格式 in Enum.GetValues<指令格式>())
            {
                var 本组 = 表.Where(x => x.格式 == 格式).OrderBy(x => x.操作码).ToArray();
                if (本组.Length == 0) continue;
                帮助节点C 组 = new()
                {
                    标题 = $"{格式}({本组.Length} 条)",
                    锚点 = 锚点("指令格式", 格式.ToString()),
                    正文 = $"操作数写法:{指令C.操作数写法(格式)};位布局:{指令C.格式位布局(格式)}",
                };
                foreach ((string 中, string 英, byte 码, 指令格式 格) in 本组)
                {
                    string 示例 = 指令C.取示例(英) ?? "(待补示例)";
                    组.加(new 帮助节点C
                    {
                        标题 = $"{中} {英}",
                        锚点 = 锚点(指令节名, 英),
                        正文 = $"{中} / {英} —— 操作码 0x{码:X2};格式 {格};写法 `{示例}`",
                    });
                }
                节.加(组);
            }
            return 节;
        }

        /// <summary> 关键字 / 内建函数 / 方法三节共用一条(说明都取自 `中间层指令C.说明表`) </summary>
        private static 帮助节点C 建词节(string 节名, string 节说明,
            IReadOnlyList<(string 中文, string 英文)> 表, bool 带点 = false, bool 去掉类型名 = false)
        {
            // `中间层指令C.关键字表` 里**同时**放着基本类型名(那是有意的:类型名要跟着中英切换)。
            // 站点里它们已经在《类型》一节各占一条了,再来一份就是同一件事说两遍(而且面包屑会撞成
            // `关键字 › 整数(int)` 与 `类型 › 整数(int)` 两条长得一样的路)—— 关键字那一节按英文名剔掉它们。
            HashSet<string> 类型名 = new(StringComparer.OrdinalIgnoreCase);
            if (去掉类型名)
            {
                foreach (类型C.类型定义 类 in 类型C.表) 类型名.Add(类.英文);
                foreach ((string _, string 英) in 中间层指令C.附加类型名表) 类型名.Add(英);
            }
            帮助节点C 节 = new()
            {
                标题 = 节名,
                锚点 = 节名,
                正文 = 节说明 + (去掉类型名 ? "(基本类型名在《类型》一节,这里不重复列)" : "") + ";共 " +
                       (去掉类型名 ? 表.Count(x => !类型名.Contains(x.英文)) : 表.Count) + " 条。",
            };
            foreach ((string 中, string 英) in 表)
            {
                if (去掉类型名 && 类型名.Contains(英)) continue;
                string 说明 = 中间层指令C.取说明(英) ?? "";
                节.加(new 帮助节点C
                {
                    标题 = $"{(带点 ? "." + 中 + "()" : 中)}({英})",
                    锚点 = 锚点(节名, 中),
                    正文 = 说明.Length > 0 ? 说明 : $"{中}(英文别名 {英})",
                });
            }
            return 节;
        }

        /// <summary> 类库节:一个库一条子项,正文 = 这个文件的**头注释**(没有头注释就写文件名)+ **公开方法表** </summary>
        private static 帮助节点C 建类库节(IReadOnlyList<(string 文件名, string 内容)>? 库文件)
        {
            帮助节点C 节 = new()
            {
                标题 = 类库节名,
                锚点 = 类库节名,
                正文 = "用 HB 自己写的库(仓库 `Sample\\库\\*.hb`),通过 `包含 \"../库/xxx.hb\"` 编译期内联;" +
                       "一个库一页:正文是库文件开头的说明注释(全文,不截),末尾那张**公开方法表**是" +
                       "从库源码的 `方法 …` 声明现场数出来的 —— 库里加一个方法,这一页就多一行。",
            };
            if (库文件 == null) return 节;
            foreach ((string 文件名, string 内容) in 库文件)
            {
                string 摘要 = 取头注释(内容);
                List<(string 名, string 签名, string 作用)> 方法们 = 取公开方法(内容);
                StringBuilder 文 = new();
                文.Append(摘要.Length > 0 ? 摘要 : $"{文件名}(库文件没有开头说明注释)");
                if (方法们.Count > 0)
                {
                    文.Append("\n\n公开方法(共 ").Append(方法们.Count).Append(" 个):\n");
                    文.Append("| 方法 | 签名 | 作用 |\n");
                    foreach ((string 名, string 签名, string 作用) in 方法们)
                        文.Append("| ").Append(名).Append(" | ").Append(签名).Append(" | ")
                           .Append(作用.Length > 0 ? 作用 : "—").Append(" |\n");
                }
                节.加(new 帮助节点C
                {
                    标题 = 文件名,
                    锚点 = 锚点(类库节名, 文件名),
                    正文 = 文.ToString(),
                });
            }
            return 节;
        }

        // ---------------- 工具(纯) ----------------

        /// <summary> 锚点:`<类>-<名>`,非字母数字(中文算字母)一律换成 `-`,收尾的 `-` 去掉 </summary>
        public static string 锚点(string 类名, string 名)
        {
            StringBuilder 缓 = new();
            foreach (char 字 in 名)
                缓.Append(char.IsLetterOrDigit(字) ? 字 : '-');
            string 净 = 缓.ToString().Trim('-');
            return $"{类名}-{(净.Length == 0 ? "无名" : 净)}";
        }

        /// <summary>
        /// 取库文件的**头注释**:第一条真代码之前的 `//` 行(去掉 `//` 与它后面那一个空格);
        /// 纯符号的分隔线(整行只有 `=` / `-` / `_` / `*` / 空格)丢掉;**行与行之间保留换行**
        /// (页面按段落 / 列表 / 表格三种块渲染,见 `帮助站点C.HTML.正文块`)。没注释返回空串。
        /// **空注释行(`//` 后面什么都没有)= 段落分隔,保留成一个空行**(2026-10 改:以前直接丢掉 ⇒
        /// 一整篇头注释被挤进同一个 `&lt;p&gt;`,口径 / 函数单子 / 复杂度 / 边界全糊在一起)。
        /// `上限` 默认 0 = **不截**(2026-10-05 起:以前截到 512 字,库的方法单子正好被切掉)。
        /// </summary>
        public static string 取头注释(string 内容, int 上限 = 0)
        {
            if (string.IsNullOrEmpty(内容)) return "";
            List<string> 段 = [];
            foreach (string 原行 in 内容.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string 行 = 原行.TrimEnd();
                string 净 = 行.TrimStart();
                if (净.StartsWith("//", StringComparison.Ordinal))
                {
                    string 文 = 净[2..];
                    if (文.StartsWith(" ", StringComparison.Ordinal)) 文 = 文[1..];   // 只让掉 `//` 后面那一个空格
                    文 = 文.TrimEnd();
                    if (文.Trim().Length == 0) { if (段.Count > 0) 段.Add(""); continue; }  // 空注释行 = 段落分隔
                    if (文.Trim().All(字 => 字 is '=' or '-' or '_' or '*' or ' ')) continue; // 分隔线
                    段.Add(文);
                    continue;
                }
                if (净.Length == 0 && 段.Count == 0) continue;                       // 文件开头的空行
                if (净.Length == 0) continue;                                        // 注释块里的空行
                break;                                                               // 第一条真代码 → 收工
            }
            if (段.Count == 0) return "";
            string 摘要 = string.Join("\n", 段);
            return 上限 > 0 && 摘要.Length > 上限 ? 摘要[..上限] + "…" : 摘要;
        }

        /// <summary>
        /// 取库文件里的**公开方法**(`方法 名(参数) 返回 类型`;带 `私有` 的不算):
        /// 名 / 签名(去掉收尾的 `{`)/ 作用。
        /// **作用两处找**:① 紧上面那行 `//` 注释;② 头注释里"提供的(N 个函数)"那张单子
        /// (按 `名(` 认行、把第一处两格以上的空白后面那截当作用)—— 两处都没有给空串。
        /// **数据以库源码为准** —— 库里加一个方法,类库页与 `关键字与方法清单.md` 同时多一行。
        /// </summary>
        public static List<(string 名, string 签名, string 作用)> 取公开方法(string 内容)
        {
            List<(string, string, string)> 果 = [];
            if (string.IsNullOrEmpty(内容)) return 果;
            string 上一句 = "";
            foreach (string 原行 in 内容.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                string 行 = 原行.Trim();
                if (行.Length == 0) { 上一句 = ""; continue; }
                if (行.StartsWith("//", StringComparison.Ordinal))
                {
                    string 文 = 行[2..].Trim();
                    if (文.Length > 0 && !文.All(字 => 字 is '=' or '-' or '_' or '*' or ' ')) 上一句 = 文;
                    continue;
                }
                if (行.StartsWith("方法 ", StringComparison.Ordinal))
                {
                    string 签 = 行[3..].Trim();
                    int 头 = 签.IndexOf('{');
                    if (头 >= 0) 签 = 签[..头].Trim();
                    签 = 签.TrimEnd('{').Trim();
                    int 左 = 签.IndexOf('(');
                    string 名 = 左 > 0 ? 签[..左].Trim() : 签;
                    果.Add((名, 签, 上一句));
                }
                上一句 = "";
            }
            // 没写"紧上面那行注释"的,回头到头注释那张单子里找(写库时的惯例:单子在前、函数在后)
            if (果.Any(x => x.Item3.Length == 0))
            {
                string 头注 = 取头注释(内容);
                for (int i = 0; i < 果.Count; i++)
                    if (果[i].Item3.Length == 0)
                        果[i] = (果[i].Item1, 果[i].Item2, 单子里那一句(头注, 果[i].Item1));
            }
            return 果;
        }

        /// <summary> 头注释那张"提供的…"单子里,`名(` 开头那一行后面那句(找不着给空串) </summary>
        private static string 单子里那一句(string 头注释, string 名)
        {
            if (头注释.Length == 0 || 名.Length == 0) return "";
            foreach (string 原行 in 头注释.Split('\n'))
            {
                string 行 = 原行.Trim();
                if (!行.StartsWith(名 + "(", StringComparison.Ordinal)) continue;
                string 余 = 行[(名.Length + 1)..];
                int 闭 = 余.IndexOf(')');
                if (闭 < 0) continue;
                余 = 余[(闭 + 1)..].Trim();
                // 单子那一行的形态:`名(参数) 返回 类型    作用` —— 作用在第一处"两格以上空白"后面
                var 碰 = System.Text.RegularExpressions.Regex.Match(余, @"\s{2,}");
                if (!碰.Success) return "";
                return 余[(碰.Index + 碰.Length)..].Trim();
            }
            return "";
        }

        /// <summary> 读一个库目录里的 `*.hb`(按文件名排序);目录不存在给空表(不炸) </summary>
        public static List<(string 文件名, string 内容)> 读库目录(string 目录)
        {
            List<(string, string)> 结果 = [];
            if (!Directory.Exists(目录)) return 结果;
            foreach (string 文件 in Directory.GetFiles(目录, "*.hb").OrderBy(x => x, StringComparer.Ordinal))
            {
                try { 结果.Add((Path.GetFileName(文件), File.ReadAllText(文件))); }
                catch { /* 读不了就跳过这一个,别让整棵树建不出来 */ }
            }
            return 结果;
        }

        // ---------------- 查树(纯,自检与以后写盘都用) ----------------

        /// <summary> 全树节点(深度优先,含根) </summary>
        public static IEnumerable<帮助节点C> 全部节点(帮助节点C 根) => 根.全部();

        /// <summary> 全树节点数 </summary>
        public static int 节点数(帮助节点C 根) => 根.全部().Count();

        /// <summary> 某个一级节(找不到给 null) </summary>
        public static 帮助节点C? 找节(帮助节点C 根, string 节名) =>
            根.子节点.FirstOrDefault(x => string.Equals(x.标题, 节名, StringComparison.Ordinal));

        /// <summary> 按锚点找(找不到给 null) </summary>
        public static 帮助节点C? 按锚点找(帮助节点C 根, string 锚点) =>
            根.全部().FirstOrDefault(x => string.Equals(x.锚点, 锚点, StringComparison.Ordinal));

        /// <summary> 第一个重复锚点(全不重复给 null)—— 写 HTML 前必须为 null </summary>
        public static string? 重复锚点(帮助节点C 根) =>
            根.全部().GroupBy(x => x.锚点, StringComparer.Ordinal)
                      .FirstOrDefault(组 => 组.Count() > 1)?.Key;

        /// <summary> 大纲文本:每行 `缩进 + 标题 [锚点]` —— 不开界面 / 不开浏览器也能看这棵树长什么样 </summary>
        public static string 大纲文本(帮助节点C 根)
        {
            StringBuilder 缓 = new();
            遍历(根, 0);
            return 缓.ToString();

            void 遍历(帮助节点C 节, int 层)
            {
                缓.Append(new string(' ', 层 * 2)).Append("· ").Append(节.标题)
                   .Append(" [").Append(节.锚点).Append(']').Append('\n');
                foreach (帮助节点C 子 in 节.子节点) 遍历(子, 层 + 1);
            }
        }
    }
}
