using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// C25-3 / 第241批:帮助章节树 → **多页静态站点**(左 `<nav>` 折叠树 / 右 `<main>` 内容区)。
    /// <para>
    /// 形态(用户 2026-09-25 定版,照 runoob 那种语言教程):
    /// ① **每个叶子一页**,一页一个话题 —— 类型 / 指令 / 关键字 / 方法 / 每个库各自成页,
    ///    放在 `帮助\目录\<锚点>.html`(文件名就是锚点,一条节点对应一个稳定文件名);
    /// ② `帮助\index.html` 是**总目录首页**(站点唯一入口:IDE 帮助菜单 / `hbcc --生成帮助` 都指它),
    ///    另加 `帮助\索引-<分类>.html` 那几份**按用途分**的索引(分类表见 `帮助站点C.群表`);
    /// ③ 每页左边是**整棵折叠树**(当前那一支与短分组默认展开,长的分组点开才出 —— 打开不再是一屏几百条),
    ///    上边一条**分类横条**,正文上面一条**面包屑**、下面一条"上一页 / 下一页";
    /// ④ 排版照 runoob:正文行高 1.9、段距放宽、条目排成表、宽屏下正文限宽 54rem(不再是一条长龙)。
    /// </para>
    /// <para>
    /// 口径(别按直觉改):
    /// ① **首屏零依赖**:不用 JavaScript、不链外部资源,折叠靠原生 `<details>/<summary>`,
    ///    样式只有一份 `帮助\样式.css`(相对路径,页面与样式一起拷走就能看),所以没有 `<script>` / `<link>` 之外的外链;
    /// ② **站内链接全是相对路径**(`目录/<锚点>.html`):拷到别的盘 / 换浏览器都不断;
    /// ③ **逐字节可复现**:换行一律 `\n`、不带时间戳、字典按名字排序后再写 ——
    ///    "同一棵树生成两次,每一页逐字节相同"是验收(自检钉着);
    /// ④ 正文里的 `` `代码` `` 与 `**粗体**` 转成 `<code>` / `<strong>`,其余一律**先转义再排版**。
    /// </para>
    /// </summary>
    public static partial class 帮助站点C
    {
        /// <summary> 站点目录(仓库根下) </summary>
        public const string 站点目录 = "帮助";
        /// <summary> 站点首页文件名(**唯一入口**,总目录) </summary>
        public const string 站点文件名 = "index.html";
        /// <summary> 叶子页放哪个子目录(与首页分开,一眼看得出"哪些是生成的") </summary>
        public const string 页目录名 = "目录";
        /// <summary> 站点样式表文件名(一份管全站;页面里用相对路径链它) </summary>
        public const string 样式文件名 = "样式.css";

        /// <summary>
        /// 分类索引表(第一列 = 直接挂在一级节下的哪个"分组"标题;第二列 = 这一组是干什么的)。
        /// **按用途分**,与左边那棵"按章节分"的树是两条路:树上按书名找,索引里按用途找。
        /// 组名用树里现成的那些分组标题(硬上限 / 类型 / 指令 / 关键字 / 内建函数 / 方法 / 类库),
        /// 所以"表里加一条"也照旧自动进索引(自检钉着组名都在树上查得到)。
        /// </summary>
        public static readonly string[] 群表 =
            [介绍节名, 上手节名, 语言细节节名, 硬上限节名, 类型节名, 指令节名, 关键字节名, 内建节名, 方法节名, 类库节名];

        /// <summary> 一个分类的一句话说明(索引页与首页用) </summary>
        public static string 群说明(string 群) => 群 switch
        {
            介绍节名 => "HB 是什么、这门语言有什么、这几份文档谁说了算。",
            上手节名 => "从打开界面到编译、调试、打包、命令行 —— 一条一条按顺序写的教程。",
            语言细节节名 => "语言的规矩与坑(类型怎么放、字符串怎么算、结构参数怎么传)。",
            硬上限节名 => "撞上就当场报错的那些上限:是多少、哪儿会撞、怎么绕。",
            类型节名 => "每个类型一页:宽度、范围、写法。",
            指令节名 => "汇编指令逐条:中英名、操作码、编码格式、写法示例。",
            关键字节名 => "语言关键字逐条:块头、流程控制、声明、可见性。",
            内建节名 => "语言级内建函数逐条(和 `申请` / `读取行` 同一档)。",
            方法节名 => "字符串 / 列表的带点方法逐条(如 `名字.长度()`)。",
            类库节名 => "用 HB 自己写的库(仓库 `Sample\\库\\*.hb`),一个库一页,正文是库文件开头的说明。",
            _ => 群,
        };

        /// <summary> `帮助\index.html` 的完整路径 </summary>
        public static string 站点路径(string 仓库根) => Path.Combine(仓库根, 站点目录, 站点文件名);

        /// <summary> `帮助\目录\<锚点>.html` 的完整路径 </summary>
        public static string 页面路径(string 仓库根, string 锚点) =>
            Path.Combine(仓库根, 站点目录, 页目录名, 锚点 + ".html");

        /// <summary> `帮助\样式.css` 的完整路径 </summary>
        public static string 样式路径(string 仓库根) => Path.Combine(仓库根, 站点目录, 样式文件名);

        /// <summary> 按仓库根建**整棵树**(库文件读 `Sample\库\*.hb`) </summary>
        public static 帮助节点C 建整树(string 仓库根) =>
            建树(读库目录(Path.Combine(仓库根, "Sample", "库")));

        // ---------------- 生成(树 → 全部文件) ----------------

        /// <summary>
        /// **整站生成**(纯):给一棵树,给回 <c>相对路径 → 文本</c> 的全部产物,顺序稳定(按路径排序)。
        /// 一个叶子一条 `目录\&lt;锚点&gt;.html`,另加首页 `index.html`、样式 `样式.css`、分节索引 `索引.html`。
        /// 自检拿它验"所有链接都指得到 + 每一页标签配平",写盘那条路也走它。
        /// </summary>
        public static SortedDictionary<string, string> 全部页面(帮助节点C 根)
        {
            List<帮助节点C> 叶子 = 取叶子(根);
            SortedDictionary<string, string> 产物 = new(StringComparer.Ordinal);
            产物[样式文件名] = 样式文本();
            foreach (string 群 in 群表) 产物[$"索引-{群}.html"] = 群索引页(根, 群);
            for (int i = 0; i < 叶子.Count; i++) 产物[页相对路径(叶子[i].锚点)] = 叶子页(根, 叶子, i);
            产物[站点文件名] = 首页(根);
            return 产物;
        }

        /// <summary> 按仓库根生成整份站点(不写盘;自检拿它比"重新生成一次逐字节相同") </summary>
        public static SortedDictionary<string, string> 生成(string 仓库根) => 全部页面(建整树(仓库根));

        /// <summary>
        /// 把整份站点写盘到 `帮助\`。**一律 UTF-8 无 BOM + `\n` 换行**;写之前把 `帮助\目录\*.html`
        /// 里**这一轮没生成的**清掉(改了节名之后不留孤儿页:旧页里的链接会指丢,那正是"以后删增加不方便"的病)。
        /// </summary>
        public static void 写盘(string 仓库根, SortedDictionary<string, string> 产物)
        {
            foreach ((string 相对, string 文本) in 产物)
            {
                string 路径 = Path.Combine(仓库根, 站点目录, 相对.Replace('/', Path.DirectorySeparatorChar));
                string? 目录 = Path.GetDirectoryName(路径);
                if (!string.IsNullOrEmpty(目录)) Directory.CreateDirectory(目录);
                File.WriteAllText(路径, 文本, new UTF8Encoding(false));
            }
            清孤儿(Path.Combine(仓库根, 站点目录, 页目录名), 产物);
        }

        /// <summary> 删掉 `目录\` 下这一轮没生成过的 `*.html`(只动 `.html`,别的东西不碰) </summary>
        private static void 清孤儿(string 页目录, SortedDictionary<string, string> 产物)
        {
            if (!Directory.Exists(页目录)) return;
            HashSet<string> 该有 = new(产物.Keys
                .Where(x => x.StartsWith(页目录名 + "/", StringComparison.Ordinal))
                .Select(x => Path.GetFileName(x.Replace('/', Path.DirectorySeparatorChar))), StringComparer.OrdinalIgnoreCase);
            foreach (string 盘上 in Directory.GetFiles(页目录, "*.html"))
                if (!该有.Contains(Path.GetFileName(盘上)))
                    try { File.Delete(盘上); } catch { /* 删不掉不致命:下一页生成时还会再试 */ }
        }

        // ---------------- 树的查(取叶子 / 挂到哪一页) ----------------

        /// <summary> 文档序的**叶子**节点(没有子节点就是叶子;根若是叶子也算) </summary>
        public static List<帮助节点C> 取叶子(帮助节点C 根)
        {
            List<帮助节点C> 结果 = [];
            收集(根);
            return 结果;
            void 收集(帮助节点C 节)
            {
                if (节.子节点.Count == 0) { 结果.Add(节); return; }
                foreach (帮助节点C 子 in 节.子节点) 收集(子);
            }
        }

        /// <summary> 某个锚点落在哪一页相对路径上;找不到给 "" </summary>
        public static string 找页(帮助节点C 根, string 锚点)
        {
            帮助节点C? 节 = 按锚点找(根, 锚点);
            if (节 == null) return "";
            帮助节点C? 叶 = 叶子(节) ?? 节;
            return 页相对路径(叶.锚点);
        }

        /// <summary>
        /// 名字 → 页(给"菜单跳某一节"用):只认**一级节及其后代**的名字,别拿根节点的锚点去撞
        /// (`建树` 把根节点的锚点设成"类型"那种节名,直接 `按锚点找` 会先撞上根)。
        /// </summary>
        public static string 找节页(帮助节点C 根, string 名字)
        {
            帮助节点C? 节 = 根.子节点.FirstOrDefault(节 =>
                string.Equals(节.标题, 名字, StringComparison.Ordinal)
                || string.Equals(节.锚点, 名字, StringComparison.Ordinal));
            if (节 == null) return "";
            帮助节点C 叶 = 叶子(节) ?? 节;
            return 页相对路径(叶.锚点);
        }

        /// <summary> 这一节点往下第一个叶子(null = 它本身就是叶子) </summary>
        private static 帮助节点C? 叶子(帮助节点C 节)
        {
            foreach (帮助节点C 子 in 节.子节点)
                return 叶子(子) ?? 子;
            return null;
        }

        /// <summary> 页面相对路径(全站链接都用它,`/` 分隔,跨平台一致) </summary>
        public static string 页相对路径(string 锚点) => $"{页目录名}/{锚点}.html";

        /// <summary>
        /// 站点里的**文件相对路径**(`目录/类型-整数.html` / 首页 `index.html`)—— 给 IDE「跳某一节」用:
        /// 传锚点时先翻成"它在哪一页"(多页站点里 `#锚点` 只在页内有效,首页上根本没有那些 id)。
        /// </summary>
        public static string 文件相对路径(string 仓库根, string 锚点 = "")
        {
            if (锚点.Length == 0) return 站点文件名;
            string 页 = 找节页(建整树(仓库根), 锚点);
            return 页.Length == 0 ? 站点文件名 : 页;
        }

        /// <summary> `文件相对路径` 的绝对路径版(IDE 拿它算网址、报错时给人看的那条路径) </summary>
        public static string 页面文件(string 仓库根, string 锚点 = "") =>
            Path.GetFullPath(Path.Combine(仓库根, 站点目录,
                文件相对路径(仓库根, 锚点).Replace('/', Path.DirectorySeparatorChar)));

        /// <summary> 这一页在站内的相对前缀:`""`(根下的首页 / 索引)或 `"../"`(目录下的页)—— 拼样式 / 链接用 </summary>
        private static string 相对前缀(string 相对路径) => 相对路径.Contains('/', StringComparison.Ordinal) ? "../" : "";

        /// <summary> 站内链接(相对路径):`找页` 给的是**从站点根算**的路径,目录下的页要补 `../` 才点得动 </summary>
        private static string 连(帮助节点C 根, string 当前页, string 锚点) =>
            $"{相对前缀(当前页)}{找页(根, 锚点)}";

        // ---------------- 页面模板 ----------------

        /// <summary> 首页 `index.html`:站点名 + 根正文 + 十大节入口 + 分类索引(全站唯一入口) </summary>
        private static string 首页(帮助节点C 根)
        {
            StringBuilder 缓 = new();
            写页头(缓, 根, 站点文件名, 根.标题 + " —— 总目录", 页面描述(根));
            缓.Append("<article>\n");
            缓.Append("<h1 class=\"页标题\">").Append(网页转义(根.标题)).Append("</h1>\n");
            if (根.正文.Length > 0) 缓.Append("<p class=\"导语\">").Append(行内(根.正文)).Append("</p>\n");
            缓.Append("<h2>从哪儿开始</h2>\n");
            缓.Append("<ul class=\"入口表\">\n");
            foreach (帮助节点C 节 in 根.子节点)
                缓.Append("<li><a href=\"").Append(找页(根, 节.锚点)).Append("\"><strong>")
                   .Append(网页转义(节.标题)).Append("</strong></a> —— ")
                   .Append(行内(一句话(节))).Append("</li>\n");
            缓.Append("</ul>\n");
            缓.Append("<h2>按分类找</h2>\n");
            缓.Append("<p>").Append(行内("上面那份是**章节目录**;这一份按用途分组(入门 / 语言 / 类库 / 指令),按用途找东西时走这一份。")).Append("</p>\n");
            缓.Append("<ul class=\"入口表\">\n");
            foreach (string 群 in 群表)
                缓.Append("<li><a href=\"索引-").Append(群).Append(".html\"><strong>")
                   .Append(网页转义(群)).Append("</strong></a> —— ")
                   .Append(网页转义(群说明(群))).Append("</li>\n");
            缓.Append("</ul>\n");
            缓.Append("</article>\n</main>\n</body>\n</html>\n");
            return 缓.ToString();
        }

        /// <summary> 分类索引页(像 runoob 的横条那种"一屏看全一组"):分组标题 + 组下每一页一条链接 </summary>
        private static string 群索引页(帮助节点C 根, string 群)
        {
            StringBuilder 缓 = new();
            string 路径 = $"索引-{群}.html";
            写页头(缓, 根, 路径, $"{群} —— 分类索引", 群说明(群));
            缓.Append("<article>\n");
            缓.Append("<nav class=\"面包屑\"><a href=\"").Append(站点文件名).Append("\">").Append(网页转义(根.标题))
               .Append("</a> <span>›</span> <span>").Append(网页转义(群)).Append("</span></nav>\n");
            缓.Append("<h1 class=\"页标题\">").Append(网页转义(群)).Append("</h1>\n");
            缓.Append("<p class=\"导语\">").Append(网页转义(群说明(群))).Append("</p>\n");
            int 条 = 0;
            foreach (帮助节点C 节 in 根.子节点)          // 只看**一级节的直接子项** —— 别按标题全树扫
            {
                // 索引文件名用的是**锚点**(节名),组标题可能是"类库"而锚点是"类库",两边都认一下
                if (节.锚点 != 群 && 节.标题 != 群 || 节.子节点.Count == 0) continue;
                if (节.正文.Length > 0)
                    缓.Append("<p>").Append(行内(节.正文)).Append("</p>\n");
                缓.Append("<table class=\"索引表\">\n<thead><tr><th>条目</th><th>说明</th></tr></thead>\n<tbody>\n");
                foreach (帮助节点C 子 in 节.子节点)
                {
                    缓.Append("<tr><td><a href=\"").Append(找页(根, 子.锚点)).Append("\">")
                       .Append(网页转义(子.标题)).Append("</a></td><td>")
                       .Append(行内(一句话(子))).Append("</td></tr>\n");
                    条++;
                }
                缓.Append("</tbody>\n</table>\n");
            }
            缓.Append("<p class=\"页脚注\">共 ").Append(条).Append(" 条。</p>\n");
            缓.Append("</article>\n</main>\n</body>\n</html>\n");
            return 缓.ToString();
        }

        /// <summary> 一个叶子一页:面包屑 + 标题 + 正文 + (有子节点就补一张"这一节有哪几条") + 上一页 / 下一页 </summary>
        private static string 叶子页(帮助节点C 根, List<帮助节点C> 叶子, int 序)
        {
            帮助节点C 节 = 叶子[序];
            string 路径 = 页相对路径(节.锚点);
            StringBuilder 缓 = new();
            写页头(缓, 根, 路径, 节.标题 + " —— " + 根.标题, 一句话(节));
            缓.Append("<article>\n");
            缓.Append("<nav class=\"面包屑\">").Append(面包屑(根, 节, 路径)).Append("</nav>\n");
            缓.Append("<h1 class=\"页标题\">").Append(网页转义(节.标题)).Append("</h1>\n");
            // 分组节点的正文当"这一节的导语"(叶子页上它排正文之前,起一句上下文的作用)
            帮助节点C? 父 = 找父(根, 节);
            if (父 != null && 父.正文.Length > 0 && 父 != 根 && 父.标题 != 节.标题)
                缓.Append("<p class=\"节导语\">").Append(行内(父.正文)).Append("</p>\n");
            if (节.正文.Length > 0) 缓.Append(正文块(节.正文));
            if (节.子节点.Count > 0) 缓.Append(子目录(根, 节, 路径));
            缓.Append(翻页(叶子, 序));
            缓.Append("</article>\n</main>\n</body>\n</html>\n");
            return 缓.ToString();
        }

        /// <summary>
        /// **正文块**:一条正文按三种块渲染(2026-10-05 起;以前整条塞进一个 `&lt;p&gt;`,库的方法单子看不见):
        ///   ① **空行分段**;段内保留换行(`&lt;br&gt;`),一行一行照原样读得下去;
        ///   ② 第一行(**去掉行首缩进后**)是 `·` / `- ` → 列表:一条一项,**紧跟着的续行算这一项的内容**
        ///      (2026-10 改:以前要求段里**每一行**都是 `·` 开头才算列表 ⇒ 库头注释那种"缩进两格的 `·` +
        ///      缩进更深的续行"永远落回 `&lt;p&gt;`,一整段挤成一条长龙);
        ///   ③ `|` 开头的连续行 → 表格(第一行是表头)—— 类库页那张"公开方法表"走这条。
        /// 行内的 `` `代码` `` / `**粗体**` 依旧由 `行内()` 处理;其余一律先转义。
        /// **列表项里的「`**标题**`:说明」在冒号后另起一行**(见 `拆标题冒号`)—— 说明与标题在 `&lt;li&gt;` 里左对齐。
        /// </summary>
        private static string 正文块(string 文本)
        {
            StringBuilder 缓 = new();
            List<string> 行们 = 文本.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
            List<string> 段 = [];
            void 落段()
            {
                if (段.Count == 0) return;
                void 落正文段(List<string> 文)
                {
                    缓.Append("<p class=\"正文\">")
                       .Append(string.Join("<br>\n", 文.Select(x => 行内(x))))
                       .Append("</p>\n");
                }
                int 起 = 段.FindIndex(x => 是项头(x));           // 列表项不一定从第一行开始:前面常有一句引子(`口径(…):`)
                if (起 < 0) { 落正文段(段); 段.Clear(); return; }
                if (起 > 0) 落正文段(段.Take(起).ToList());
                List<string> 列表 = 段.Skip(起).ToList();
                缓.Append("<ul class=\"正文列表\">\n");
                List<string> 项 = [];
                void 落项()
                {
                    if (项.Count == 0) return;
                    string 首 = 项[0].TrimStart();
                    string 头文 = 首.StartsWith("·", StringComparison.Ordinal) ? 首[1..].Trim() : 首[2..].Trim();
                    (string 头, string 余) = 拆标题冒号(头文);
                    缓.Append("<li>").Append(行内(头));
                    if (余.Length > 0) 缓.Append("<br>\n").Append(行内(余));
                    for (int k = 1; k < 项.Count; k++) 缓.Append("<br>\n").Append(行内(项[k].Trim()));
                    缓.Append("</li>\n");
                    项.Clear();
                }
                foreach (string x in 列表)
                {
                    if (是项头(x)) { 落项(); 项.Add(x); }
                    else 项.Add(x);                       // 续行:归上一条
                }
                落项();
                缓.Append("</ul>\n");
                段.Clear();
            }
            int i = 0;
            while (i < 行们.Count)
            {
                string 行 = 行们[i];
                string 净 = 行.Trim();
                if (净.Length == 0) { 落段(); i++; continue; }
                if (净.StartsWith("|", StringComparison.Ordinal))
                {
                    落段();
                    List<string> 表 = [];
                    while (i < 行们.Count && 行们[i].Trim().StartsWith("|", StringComparison.Ordinal))
                    {
                        表.Add(行们[i].Trim());
                        i++;
                    }
                    缓.Append("<table class=\"方法表\">\n");
                    for (int r = 0; r < 表.Count; r++)
                    {
                        List<string> 格 = 表[r].Trim('|').Split('|').Select(x => x.Trim()).ToList();
                        if (r == 0)
                        {
                            缓.Append("<thead><tr>");
                            foreach (string 格文 in 格) 缓.Append("<th>").Append(行内(格文)).Append("</th>");
                            缓.Append("</tr></thead>\n<tbody>\n");
                        }
                        else
                        {
                            缓.Append("<tr>");
                            foreach (string 格文 in 格) 缓.Append("<td>").Append(行内(格文)).Append("</td>");
                            缓.Append("</tr>\n");
                        }
                    }
                    缓.Append("</tbody>\n</table>\n");
                    continue;
                }
                if (净.StartsWith("```", StringComparison.Ordinal))
                {
                    // 围栏代码块(头注释里写 `// ```` ` 起、`// ```` ` 收):**方框里等宽排版、原样保留空格**
                    落段();
                    i++;
                    List<string> 码行 = [];
                    while (i < 行们.Count && !行们[i].Trim().StartsWith("```", StringComparison.Ordinal))
                    {
                        码行.Add(行们[i].TrimEnd());
                        i++;
                    }
                    if (i < 行们.Count) i++;                                  // 收尾围栏那一行
                    int 缩 = 公共缩进(码行);
                    if (缩 > 0)
                        码行 = 码行.Select(x => x.Length >= 缩 ? x[缩..] : x.TrimStart()).ToList();
                    缓.Append("<pre class=\"代码框\"><code>")
                       .Append(网页转义(string.Join("\n", 码行)))
                       .Append("</code></pre>\n");
                    continue;
                }
                段.Add(行.TrimEnd());
                i++;
            }
            落段();
            return 缓.ToString();
        }

        /// <summary> 这一行是不是列表项的开头(`·` 或 `- `;**先去掉行首缩进**再判 —— 库头注释里的项目符号是缩进两格写的) </summary>
        private static bool 是项头(string 行)
        {
            string 净 = 行.TrimStart();
            return 净.StartsWith("·", StringComparison.Ordinal)
                || 净.StartsWith("- ", StringComparison.Ordinal);
        }

        /// <summary>
        /// 列表项里的「`**标题**`…:说明」→ 在冒号后面断开(说明另起一行,在 `&lt;li&gt;` 里与标题左对齐)。
        /// 判据:整项以 `**…**` 开头,往后扫到**第一个不在 `代码` 片段里的冒号**(标题后面可能先跟一句括号补充,
        /// 例如 `**不做"宽松模式"**(比如容忍没配对的引号):…`);扫到句号(说明正文开始了)就放弃,一个都不动。
        /// </summary>
        private static (string 头, string 余) 拆标题冒号(string 文)
        {
            if (!文.StartsWith("**", StringComparison.Ordinal)) return (文, "");
            int 闭 = 文.IndexOf("**", 2, StringComparison.Ordinal);
            if (闭 < 0) return (文, "");
            bool 码里 = false;
            for (int i = 闭 + 2; i < 文.Length; i++)
            {
                if (文[i] == '`') { 码里 = !码里; continue; }
                if (码里) continue;
                if (文[i] == '。') return (文, "");
                if (文[i] != ':' && 文[i] != '：') continue;
                int 起 = i + 1;
                while (起 < 文.Length && 文[起] == ' ') 起++;
                return (文[..(i + 1)], 起 < 文.Length ? 文[起..] : "");
            }
            return (文, "");
        }

        /// <summary> 若干行里**最短那个行首缩进**(空行不算)——代码框剥掉它,免得整块被作者的缩进推到右边 </summary>
        private static int 公共缩进(List<string> 行们)
        {
            int 缩 = int.MaxValue;
            foreach (string 行 in 行们)
            {
                if (行.Trim().Length == 0) continue;
                int n = 0;
                while (n < 行.Length && (行[n] == ' ' || 行[n] == '\t')) n++;
                缩 = Math.Min(缩, n);
            }
            return 缩 == int.MaxValue ? 0 : 缩;
        }

        /// <summary> 分组节点自己的正文 + 它下面那几条的目录(runoob 的"列表页"那种样子) </summary>
        private static string 子目录(帮助节点C 根, 帮助节点C 节, string 当前页)
        {
            StringBuilder 缓 = new();
            缓.Append("<h2 class=\"组标题\">这一节有哪几条</h2>\n");
            缓.Append("<table class=\"索引表\">\n<thead><tr><th>条目</th><th>说明</th></tr></thead>\n<tbody>\n");
            foreach (帮助节点C 子 in 节.子节点)
                缓.Append("<tr><td><a href=\"").Append(连(根, 当前页, 子.锚点)).Append("\">")
                   .Append(网页转义(子.标题)).Append("</a></td><td>").Append(行内(一句话(子))).Append("</td></tr>\n");
            缓.Append("</tbody>\n</table>\n");
            return 缓.ToString();
        }

        /// <summary> 页头:声明 / head / 左树 / 分类横条(每一页都带,换了页也不迷路) </summary>
        private static void 写页头(StringBuilder 缓, 帮助节点C 根, string 相对路径, string 页标题, string 描述)
        {
            string 前缀 = 相对前缀(相对路径);
            缓.Append("<!DOCTYPE html>\n<html lang=\"zh-CN\">\n<head>\n");
            缓.Append("<meta charset=\"utf-8\">\n");
            缓.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            缓.Append("<title>").Append(网页转义(页标题)).Append("</title>\n");
            缓.Append("<meta name=\"description\" content=\"").Append(网页转义(描述)).Append("\">\n");
            缓.Append("<link rel=\"stylesheet\" href=\"").Append(前缀).Append(样式文件名).Append("\">\n");
            缓.Append("</head>\n<body>\n");
            // 左边:整棵树(一级节默认展开;当前页那一支也展开,免得点了下一页树又收回去)
            缓.Append("<nav class=\"侧栏\">\n<div class=\"站点名\"><a href=\"").Append(前缀).Append(站点文件名).Append("\">")
               .Append(网页转义(根.标题)).Append("</a></div>\n<ul class=\"树\">\n");
            foreach (帮助节点C 节 in 根.子节点) 写导航(缓, 根, 节, 相对路径, 0);
            缓.Append("</ul>\n</nav>\n");
            // 上边:分类横条(照 runoob 那两排,一眼看得见"还有哪几大块")
            缓.Append("<header class=\"横条\">\n");
            缓.Append("<a class=\"横条首页\" href=\"").Append(前缀).Append(站点文件名).Append("\">总目录</a>\n");
            foreach (string 群 in 群表)
                缓.Append("<a href=\"").Append(前缀).Append("索引-").Append(群).Append(".html\">").Append(网页转义(群)).Append("</a>\n");
            缓.Append("</header>\n<main class=\"内容区\">\n");
        }

        /// <summary> 左树的一条:有子节点的给 `<details>`(当前页所在那一支默认展开),叶子给链接 </summary>
        private static void 写导航(StringBuilder 缓, 帮助节点C 根, 帮助节点C 节, string 当前页, int 层)
        {
            bool 当前 = 在枝上(节, 当前页, 根);
            string 缩 = new(' ', 2 + 层 * 4);
            string 链接 = $"<a href=\"{相对前缀(当前页)}{找页(根, 节.锚点)}\">{网页转义(节.标题)}</a>";
            if (节.子节点.Count == 0)
            {
                缓.Append(缩).Append("<li").Append(当前 ? " class=\"当前\"" : "").Append('>').Append(链接).Append("</li>\n");
                return;
            }
            缓.Append(缩).Append("<li>\n");
            // 默认展开只有两种:① 当前页所在那一支(换了页树不跟着收回去)② 一级节下的**短**分组(≤ 8 条)
            // —— **一级节一律折叠**(用户 2026-10-05:"帮助文档根目录全部折叠起来"):打开帮助先看见十个大类,
            //    点哪一类展哪一类;长的分组(比如《上手》那 25 节)也默认收起,不铺两百条链接
            bool 短组 = 层 == 1 && 节.子节点.Count <= 8;
            缓.Append(缩).Append("  <details").Append((当前 || 短组) ? " open" : "").Append(">\n");
            缓.Append(缩).Append("    <summary>").Append(链接).Append("</summary>\n");
            缓.Append(缩).Append("    <ul>\n");
            foreach (帮助节点C 子 in 节.子节点) 写导航(缓, 根, 子, 当前页, 层 + 1);
            缓.Append(缩).Append("    </ul>\n");
            缓.Append(缩).Append("  </details>\n");
            缓.Append(缩).Append("</li>\n");
        }

        /// <summary> 当前页是不是落在这一支下面(是的话 `<details>` 默认展开) </summary>
        private static bool 在枝上(帮助节点C 节, string 当前页, 帮助节点C 根) =>
            叶链(根, 节).Any(叶 => string.Equals(页相对路径(叶.锚点), 当前页, StringComparison.Ordinal));

        /// <summary> 这一支下面的全部叶子 </summary>
        private static List<帮助节点C> 叶链(帮助节点C 根, 帮助节点C 节)
        {
            List<帮助节点C> 结果 = [];
            收集(节);
            return 结果;
            void 收集(帮助节点C 当)
            {
                if (当.子节点.Count == 0) { 结果.Add(当); return; }
                foreach (帮助节点C 子 in 当.子节点) 收集(子);
            }
        }

        /// <summary> 面包屑:`根 › 一级节 › … › 这一页`(每一层都点得动) </summary>
        private static string 面包屑(帮助节点C 根, 帮助节点C 节, string 当前页)
        {
            List<帮助节点C> 链 = 找链(根, 节);
            StringBuilder 缓 = new();
            for (int i = 0; i < 链.Count; i++)
            {
                if (i > 0) 缓.Append(" <span>›</span> ");
                帮助节点C 当 = 链[i];
                if (i == 链.Count - 1) 缓.Append("<span>").Append(网页转义(当.标题)).Append("</span>");
                else 缓.Append("<a href=\"").Append(连(根, 当前页, 当.锚点)).Append("\">").Append(网页转义(当.标题)).Append("</a>");
            }
            return 缓.ToString();
        }

        /// <summary> 从根到目标节点的链(含两端);找不到给空表 —— 面包屑、左树"当前这一支"、自检都用它 </summary>
        public static List<帮助节点C> 找链(帮助节点C 根, 帮助节点C 目标)
        {
            List<帮助节点C> 链 = [];
            return 找链(根, 目标, 链) ? 链 : [];
        }

        /// <summary> 找链的真身(自己拼链时用;找到给 true,路上不匹配的会自己弹掉) </summary>
        private static bool 找链(帮助节点C 当, 帮助节点C 目标, List<帮助节点C> 链)
        {
            链.Add(当);
            if (当 == 目标) return true;
            foreach (帮助节点C 子 in 当.子节点)
                if (找链(子, 目标, 链)) return true;
            链.RemoveAt(链.Count - 1);
            return false;
        }

        /// <summary> 父节点(null = 根或没找到) </summary>
        private static 帮助节点C? 找父(帮助节点C 当, 帮助节点C 目标)
        {
            foreach (帮助节点C 子 in 当.子节点)
            {
                if (子 == 目标) return 当;
                if (找父(子, 目标) is { } 父) return 父;
            }
            return null;
        }

        /// <summary> 页脚:上一页 / 下一页(照教程的读法,顺着读不用回树上找) </summary>
        private static string 翻页(List<帮助节点C> 叶子, int 序)
        {
            StringBuilder 缓 = new();
            缓.Append("<nav class=\"翻页\">\n");
            if (序 > 0)
                缓.Append("<a class=\"上一页\" href=\"").Append(页相对路径(叶子[序 - 1].锚点)).Append("\">← ")
                   .Append(网页转义(叶子[序 - 1].标题)).Append("</a>\n");
            else 缓.Append("<span></span>\n");
            if (序 + 1 < 叶子.Count)
                缓.Append("<a class=\"下一页\" href=\"").Append(页相对路径(叶子[序 + 1].锚点)).Append("\">")
                   .Append(网页转义(叶子[序 + 1].标题)).Append(" →</a>\n");
            else 缓.Append("<span></span>\n");
            缓.Append("</nav>\n");
            return 缓.ToString();
        }

        /// <summary> 一句话摘要(首页 / 索引 / 子目录那一列"说明"用):正文第一行 / 第一段,太长截断 </summary>
        public static string 一句话(帮助节点C 节, int 上限 = 96)
        {
            string 文 = (节.正文 ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();
            if (文.Length == 0) 文 = 节.子节点.Count > 0 ? $"共 {节.子节点.Count} 条" : "";
            if (文.Length == 0) return "";
            int 断 = 文.IndexOf('\n');
            if (断 >= 0) 文 = 文[..断];
            文 = 文.Replace("——", "-");
            if (文.Length > 上限) 文 = 文[..上限] + "…";
            return 文;
        }

        /// <summary> 页面描述(`<meta name="description">` 用;把标记符去掉,别把反引号 / 星号带进去) </summary>
        private static string 页面描述(帮助节点C 根)
        {
            string 文 = 一句话(根, 120).Replace("`", "").Replace("*", "");
            return 文.Length == 0 ? 根.标题 : 文;
        }

        // ---------------- 兼容老调用(单文件那版的几个口) ----------------

        /// <summary>
        /// C25-4:从**程序目录**往上找第一个已生成的站点(`帮助\index.html`);找不到给 null。
        /// 开发机上程序在 `bin\…` 里,往上几层就是仓库根;拷出去的单机版找不到属于正常情况 ——
        /// 界面那边据此给一句"怎么生成"的人话提示(见 `没生成过的提示`),不弹异常。
        /// </summary>
        public static string? 找已生成站点()
        {
            var 目录 = new DirectoryInfo(AppContext.BaseDirectory);
            while (目录 != null)
            {
                string 候选 = 站点路径(目录.FullName);
                if (File.Exists(候选)) return 候选;
                目录 = 目录.Parent;
            }
            return null;
        }

        /// <summary>
        /// 给系统默认浏览器用的网址:`file:///…`(非 ASCII 路径会被转义);给的那个**文件**要存在
        /// (多页站点里跳某一节得先把它翻成"那一页" —— 见 `文件相对路径` / `页面文件`)。
        /// **别直接把路径喂给 `Process.Start`** —— 路径里的 `#` 会被 shell 当成文件名的一部分,打不开。
        /// </summary>
        public static string 网址(string 路径) => new Uri(路径).AbsoluteUri;

        /// <summary> 还没生成过站点时给用户看的那句话:**说清怎么办**(不带感叹号、不报异常) </summary>
        public const string 没生成过的提示 =
            "还没生成过帮助站点:仓库里找不到 帮助\\index.html。\n\n" +
            "在仓库根目录跑一次 hbcc --生成帮助,再点这里就会用系统默认浏览器打开。";

        // ---------------- 排版 ----------------

        /// <summary> HTML 转义(`&` 第一个换,不然会把自己的实体再转一遍) </summary>
        public static string 网页转义(string 文本)
        {
            StringBuilder 缓 = new(文本.Length + 16);
            foreach (char 字 in 文本)
                switch (字)
                {
                    case '&': 缓.Append("&amp;"); break;
                    case '<': 缓.Append("&lt;"); break;
                    case '>': 缓.Append("&gt;"); break;
                    case '"': 缓.Append("&quot;"); break;
                    default: 缓.Append(字); break;
                }
            return 缓.ToString();
        }

        /// <summary> 正文片段 → 段落内 HTML(先整体转义,再把 `` `代码` `` / `**粗体**` / 换行排成标记) </summary>
        public static string 行内(string 正文) =>
            行内已转义(网页转义(正文.Replace("\r\n", "\n").Replace('\r', '\n')));

        /// <summary> 行内排版(**假定传进来的已经是转义过的文本**;粗体里面再走一遍自己,所以嵌套的反引号也认) </summary>
        private static string 行内已转义(string 文)
        {
            StringBuilder 缓 = new(文.Length + 16);
            int 位 = 0;
            while (位 < 文.Length)
            {
                char 字 = 文[位];
                if (字 == '`')
                {
                    int 止 = 文.IndexOf('`', 位 + 1);
                    if (止 > 位 + 1)
                    {
                        缓.Append("<code>").Append(文[(位 + 1)..止]).Append("</code>");
                        位 = 止 + 1;
                        continue;
                    }
                }
                else if (字 == '*' && 位 + 1 < 文.Length && 文[位 + 1] == '*')
                {
                    int 止 = 文.IndexOf("**", 位 + 2, StringComparison.Ordinal);
                    if (止 >= 位 + 2)
                    {
                        缓.Append("<strong>").Append(行内已转义(文[(位 + 2)..止])).Append("</strong>");
                        位 = 止 + 2;
                        continue;
                    }
                }
                else if (字 == '\n')
                {
                    缓.Append("<br>\n");
                    位++;
                    continue;
                }
                缓.Append(字);
                位++;
            }
            return 缓.ToString();
        }

        /// <summary>
        /// 全站样式(一份 `帮助\样式.css`,每一页相对路径链它)。排版照 runoob:
        /// 正文行高 1.9、段距放开、条目之间留分隔、宽屏正文限宽。
        /// </summary>
        public static string 样式文本() => string.Join("\n", 样式) + "\n";

        private static readonly string[] 样式 =
        [
            "/* HB 帮助站点样式(由 hbcc --生成帮助 生成,别手改;要改排改 引擎\\帮助站点C.HTML.cs 的样式表) */",
            "* { box-sizing: border-box; }",
            "html { scroll-behavior: smooth; }",
            "body { margin: 0; color: #24292f; background: #fff;",
            "       font: 15px/1.9 \"Microsoft YaHei\", \"PingFang SC\", \"Segoe UI\", sans-serif; }",
            "a { color: #1a5fb4; text-decoration: none; }",
            "a:hover { text-decoration: underline; }",
            "",
            "/* 顶部分类横条(照 runoob 那两排:一眼看见还有哪几大块) */",
            ".横条 { position: fixed; top: 0; left: 0; right: 0; z-index: 9;",
            "        display: flex; flex-wrap: wrap; gap: 1.1rem; align-items: center;",
            "        padding: 0.6rem 1.25rem; background: #f6f7f9; border-bottom: 1px solid #d8dce2; }",
            ".横条 a { font-size: 0.95rem; }",
            ".横条 .横条首页 { font-weight: 700; }",
            "",
            "/* 左边折叠树 */",
            ".侧栏 { position: fixed; top: 3rem; bottom: 0; left: 0; width: 22rem; overflow: auto;",
            "        padding: 0.9rem 0.75rem 3rem 1rem; background: #fbfcfd; border-right: 1px solid #d8dce2; }",
            ".侧栏 .站点名 { margin: 0 0 0.7rem; font-size: 1.05rem; font-weight: 700; }",
            ".侧栏 ul { list-style: none; margin: 0; padding-left: 0.9rem; }",
            ".侧栏 > ul { padding-left: 0; }",
            ".侧栏 li { margin: 0.15rem 0; }",
            ".侧栏 summary { cursor: pointer; }",
            ".侧栏 summary > a { color: #24292f; }",
            ".侧栏 li > a { color: #1a5fb4; }",              // 只认真正的条目链接:`<link` 也以 `<li` 开头,别被它撞上
            ".侧栏 li.当前 > a { font-weight: 700; color: #0b5cad; }",
            "",
            "/* 正文区 */",
            ".内容区 { margin-left: 22rem; padding-top: 3rem; }",
            "article { max-width: 54rem; padding: 1.2rem 2.6rem 5rem 2.6rem; }",
            ".面包屑 { font-size: 0.88rem; color: #57606a; margin: 0.2rem 0 1.1rem; }",
            "h1.页标题 { font-size: 1.75rem; line-height: 1.35; margin: 0 0 0.4rem; padding-bottom: 0.6rem;",
            "           border-bottom: 2px solid #eaeef2; }",
            "h2 { font-size: 1.25rem; margin: 2.2rem 0 0.7rem; }",
            "h2.组标题 { padding-top: 0.4rem; border-top: 1px solid #eaeef2; }",
            "p { margin: 0.75rem 0; }",
            "p.导语 { color: #3d444d; }",
            "p.节导语 { color: #3d444d; background: #f6f8fa; border-left: 3px solid #cfd8e3;",
            "            padding: 0.55rem 0.9rem; margin: 0.9rem 0 1.1rem; border-radius: 0 4px 4px 0; }",
            "p.正文 { font-size: 1.02rem; }",
            "p.页脚注 { color: #57606a; font-size: 0.9rem; margin-top: 1.6rem; }",
            "code { padding: 0.12em 0.38em; border-radius: 4px; background: #eff1f4;",
            "       font-family: Consolas, \"Courier New\", monospace; font-size: 0.94em; }",
            "",
            "/* 入口表 / 索引表:一条一行,行间留白(不再是一条长龙) */",
            "ul.入口表 { margin: 0.6rem 0 0; padding-left: 1.2rem; }",
            "ul.入口表 li { margin: 0.55rem 0; }",
            "table.索引表 { border-collapse: collapse; width: 100%; margin: 0.8rem 0 1.4rem; }",
            "table.索引表 th, table.索引表 td { text-align: left; vertical-align: top;",
            "        padding: 0.5rem 0.8rem; border-bottom: 1px solid #eaeef2; }",
            "table.索引表 th { background: #f6f8fa; font-size: 0.92rem; color: #3d444d; }",
            "table.索引表 tr:hover td { background: #f9fbfd; }",
            "",
            "/* 类库页那张**公开方法表**(从库源码现场数出来;签名一列用等宽字) */",
            "table.方法表 { border-collapse: collapse; width: 100%; margin: 0.8rem 0 1.4rem; }",
            "table.方法表 th, table.方法表 td { text-align: left; vertical-align: top;",
            "        padding: 0.45rem 0.8rem; border-bottom: 1px solid #eaeef2; }",
            "table.方法表 th { background: #f6f8fa; font-size: 0.92rem; color: #3d444d; }",
            "table.方法表 tr:hover td { background: #f9fbfd; }",
            "table.方法表 td:nth-child(2) { font-family: Consolas, \"Courier New\", monospace; font-size: 0.92em; }",
            "/* **方法名那一列不折行**:名称不长就该在一行里放下(汉字之间处处可断行,",
            "   会把「是需转义」竖着劈成两行);真碰上特别长的名字也只是把这一列撑宽,不竖排 */",
            "table.方法表 td:first-child, table.方法表 th:first-child { white-space: nowrap; }",
            "ul.正文列表 { margin: 0.5rem 0 1rem; padding-left: 1.3rem; }",
            "ul.正文列表 li { margin: 0.5rem 0; }",
            "",
            "/* **代码框**:头注释里用 ``` 围起来的那段 = 一整个方框(等宽、浅底、细边、超宽可横向滚)",
            "   说明文字里的 `代码` 还是那种小胶囊(code),只有成段落的走这个框 */",
            "pre.代码框 { margin: 0.7rem 0 1.1rem; padding: 0.7rem 0.95rem; overflow-x: auto;",
            "       background: #f6f8fa; border: 1px solid #d8dee4; border-radius: 6px; }",
            "pre.代码框 code { padding: 0; background: none; font-size: 0.92rem; line-height: 1.7;",
            "       white-space: pre; }",
            "",
            "/* 页脚翻页 */",
            ".翻页 { display: flex; justify-content: space-between; gap: 1rem; margin-top: 2.6rem;",
            "        padding-top: 1rem; border-top: 1px solid #eaeef2; }",
            "",
            "@media (max-width: 62rem) {",
            "  .侧栏 { position: static; width: auto; border-right: 0; border-bottom: 1px solid #d8dce2;",
            "          max-height: 22rem; }",
            "  .内容区 { margin-left: 0; padding-top: 0; }",
            "  article { padding: 1rem 1.25rem 3rem; }",
            "}",
        ];
    }
}
