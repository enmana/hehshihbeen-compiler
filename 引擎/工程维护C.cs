using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 一次**文件改名**算出来的计划(C6)。与 `重命名清单C`(改代码里的名字)同一个套路:
    /// **只算清单,不碰磁盘、不碰编辑器**;调用方拿 `套用` 得到新文本,自己决定写不写。
    ///
    /// 一处改名会牵动三样东西:
    ///   ① 磁盘上的文件名(调用方用 `File.Move` 做 —— 引擎不碰磁盘);
    ///   ② 工程文件 `.hbp` 里指向它的条目(`文件` / `资源` / `子工程` / `启动文件` 都算);
    ///   ③ 别的文件里 `包含 "路径"` 那一行 —— 路径是**相对各自文件所在目录**写的,
    ///      所以这里要按引用方重新算相对路径(不是简单地把旧名替换成新名)。
    /// </summary>
    public sealed class 改名计划C
    {
        public string 旧全路径 { get; set; } = "";
        public string 新全路径 { get; set; } = "";

        /// <summary> 要改的每一处(源码里的 `包含 "…"` + 工程文件里的条目;顺序:文件 → 行号 → 列) </summary>
        public List<编辑项C> 编辑 { get; } = [];

        /// <summary> 办不到的事(非空就不硬改:调用方报清楚然后停手) </summary>
        public List<编辑问题C> 问题 { get; } = [];

        /// <summary> 提醒(不挡路:例如"这个文件不在任何工程里,只改磁盘上的名字") </summary>
        public List<string> 提示 { get; } = [];

        public bool 可改 => 问题.Count == 0;
        public int 处数 => 编辑.Count;

        /// <summary> 涉及的**别的**文件(不含被改名那个) </summary>
        public List<string> 涉及文件 =>
            [.. 编辑.Select(x => x.文件).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)];

        /// <summary> 工程文件里改了几处(按扩展名认:`.hbp`) </summary>
        public int 工程处数 => 编辑.Count(x => x.文件.EndsWith(工程文件C.扩展名, StringComparison.OrdinalIgnoreCase));

        /// <summary> 源码里改了几处(`包含 "…"`) </summary>
        public int 源码处数 => 处数 - 工程处数;

        /// <summary> 把这份计划套到文件表上(纯函数;实现见 `编辑清单C.套用`:核对原词,套两遍等于套一遍) </summary>
        public List<(string 名称, string 内容)> 套用(IReadOnlyList<(string 名称, string 内容)> 文件表)
            => 编辑清单C.套用(编辑, 文件表);

        /// <summary> 一行摘要(界面提示 / 命令行结尾都用它) </summary>
        public string 摘要() =>
            $"把 {Path.GetFileName(旧全路径)} 改名成 {Path.GetFileName(新全路径)}:"
            + $"磁盘 1 处 / 工程文件 {工程处数} 处 / 包含 {源码处数} 处(涉及 {涉及文件.Count} 个文件)";

        /// <summary> 给人看的预览(最多 最多 条明细) </summary>
        public string 预览(int 最多 = 20)
        {
            var sb = new StringBuilder(摘要());
            foreach (var 条 in 编辑.Take(最多))
            {
                string 类 = 条.文件.EndsWith(工程文件C.扩展名, StringComparison.OrdinalIgnoreCase) ? "[工程]" : "[包含]";
                sb.Append($"\n  {类} {Path.GetFileName(条.文件)}:{条.行号}  `{条.原词}` → `{条.新词}`");
            }
            if (编辑.Count > 最多) sb.Append($"\n  …还有 {编辑.Count - 最多} 处");
            foreach (string 一句 in 提示) sb.Append($"\n  提醒:{一句}");
            foreach (var 问 in 问题) sb.Append($"\n  问题:{问}");
            return sb.ToString();
        }

        public override string ToString() => 可改 ? 摘要() : $"不能改:{string.Join(" | ", 问题)}";
    }

    /// <summary>
    /// **工程与包含的维护**(C6 = P26):新建文件进 `.hbp` 的文件表、文件改名时把 `.hbp` 条目与
    /// 别处的 `包含 "路径"` 一起改对。纯文本 + 纯函数(零磁盘、零界面),命令行与 IDE 共用同一份。
    ///
    /// 为什么不用"读成对象再整体写回":`工程文件C.保存` 会把 `//` 注释与手工排版一起洗掉;
    /// 这里改的是**文本上的那一处**,用户写在 `.hbp` 里的注释、缩进都原样留着。
    /// </summary>
    public static class 工程维护C
    {
        // ================= 一、文件改名:算计划 =================

        /// <summary>
        /// 算一份改名计划。`源码表` = (文件全路径, 内容),要**含全部可能引用它的文件**(工程里的全部源码);
        /// `工程表` = (.hbp 全路径, 内容),没有工程文件就传空。
        /// 纯函数:不读盘、不写盘(磁盘上"新名已存在"这类检查由调用方做,它才知道该不该拦)。
        /// </summary>
        public static 改名计划C 计划(string 旧全路径, string 新全路径,
            IReadOnlyList<(string 文件, string 内容)> 源码表,
            IReadOnlyList<(string 文件, string 内容)>? 工程表 = null)
        {
            var 计划 = new 改名计划C
            {
                // H64:系统目录那一档(虚拟路径)不做 `Path.GetFullPath` —— 那会把 `内容://…` 算成一个
                // 谁都不认识的名字(新旧两边都比不出"是同一个文件");原样收正即可
                旧全路径 = 虚拟路径C.是虚拟(旧全路径) ? 虚拟路径C.收正(旧全路径) : Path.GetFullPath(旧全路径),
                新全路径 = 虚拟路径C.是虚拟(新全路径) ? 虚拟路径C.收正(新全路径) : Path.GetFullPath(新全路径),
            };
            if (string.Equals(计划.旧全路径, 计划.新全路径, StringComparison.OrdinalIgnoreCase))
            {
                计划.问题.Add(new 编辑问题C { 说明 = "新旧路径是同一个文件(什么都不用做)" });
                return 计划;
            }
            if (!计划.新全路径.EndsWith(".hb", StringComparison.OrdinalIgnoreCase))
                计划.问题.Add(new 编辑问题C { 说明 = $"新名字不是 .hb 文件:{虚拟路径C.文件名(计划.新全路径)}(请用 .hb 结尾)" });

            string 旧目录 = 虚拟路径C.所在目录(计划.旧全路径);
            string 新目录 = 虚拟路径C.所在目录(计划.新全路径);
            bool 在工程里 = false;

            // ① 别的文件里的 `包含 "…"`(含"被改名文件自己 包含 别人"——它换了目录也得跟着重算)
            foreach (var (文件, 内容) in 源码表)
            {
                if (内容 == null) continue;
                string 全 = 全路径(文件);
                var 行首表 = 编辑清单C.行首下标表(内容);
                var 注释区 = 代码解析器.取注释区(内容);
                foreach (Match 匹配 in 包含正则().Matches(内容))
                {
                    if (编辑清单C.在保护区内(注释区, 匹配.Index)) continue;         // 注释里的 `包含` 不算
                    var 组 = 匹配.Groups[1];
                    string 路径 = 组.Value;
                    if (路径.Trim().Length == 0) continue;

                    if (string.Equals(全, 计划.旧全路径, StringComparison.OrdinalIgnoreCase))
                    {
                        // 被改名那个文件自己:包含的相对路径要按**新目录**重算(换目录时否则就断了)
                        string 目标全 = 解析(旧目录, 路径);
                        if (目标全.Length == 0) continue;
                        string 新路径 = 字面量(新目录, 路径, 目标全);
                        if (!string.Equals(新路径, 路径, StringComparison.Ordinal))
                            计划.编辑.Add(编辑清单C.造编辑项(全, 内容, 行首表, 组.Index, 组.Length, 新路径));
                        continue;
                    }

                    string 引用目标 = 解析(全路径(Path.GetDirectoryName(全) ?? ""), 路径);
                    if (引用目标.Length == 0) continue;
                    if (!string.Equals(引用目标, 计划.旧全路径, StringComparison.OrdinalIgnoreCase)) continue;
                    string 换 = 字面量(Path.GetDirectoryName(全) ?? "", 路径, 计划.新全路径);
                    if (!string.Equals(换, 路径, StringComparison.Ordinal))
                        计划.编辑.Add(编辑清单C.造编辑项(全, 内容, 行首表, 组.Index, 组.Length, 换));
                }
            }
            bool 有人包含 = 计划.编辑.Any(x => !x.文件.EndsWith(工程文件C.扩展名, StringComparison.OrdinalIgnoreCase));

            // ② 工程文件 `.hbp` 里的条目(文件 / 资源 / 子工程 / 启动文件 —— 都是"相对 .hbp 的路径")
            foreach (var (工程路径, 工程文本) in 工程表 ?? [])
            {
                if (工程文本 == null) continue;
                string 目录 = Path.GetDirectoryName(全路径(工程路径)) ?? "";
                var (条目, 命中过) = 改工程文本里的条目(工程路径, 工程文本, 目录, 计划.旧全路径, 计划.新全路径);
                计划.编辑.AddRange(条目);
                if (命中过) 在工程里 = true;
            }
            if (!在工程里 && 有人包含)
                计划.提示.Add("这个文件不在工程文件的文件表里(只改了别的文件里 包含 它的地方)");
            else if (!在工程里 && !有人包含)
                计划.提示.Add("工程文件里没有它、也没有别的文件 包含 它:这次只改磁盘上的文件名");

            编辑清单C.排好(计划.编辑);
            return 计划;
        }

        /// <summary> `.hbp` 文本里所有"指向 旧全路径"的字符串字面量(只认注释外的那种);
        /// `命中过` = 这份工程文件里**确实提到了**这个文件(哪怕一个字都不用改) </summary>
        private static (List<编辑项C> 编辑, bool 命中过) 改工程文本里的条目(string 工程路径, string 工程文本,
            string 基准目录, string 旧全路径, string 新全路径)
        {
            var 编辑 = new List<编辑项C>();
            bool 命中 = false;
            var 注释区 = 代码解析器.取注释区(工程文本);       // 字符串**故意**不保护:要改的就是里面的路径
            var 行首表 = 编辑清单C.行首下标表(工程文本);
            foreach (Match 匹配 in 字符串字面量正则().Matches(工程文本))
            {
                if (编辑清单C.在保护区内(注释区, 匹配.Index)) continue;
                string? 值 = 反转义(匹配.Value[1..^1]);
                if (值 == null || 值.Trim().Length == 0) continue;
                if (!string.Equals(解析(基准目录, 值), 旧全路径, StringComparison.OrdinalIgnoreCase)) continue;
                命中 = true;
                string 新值 = 字面量(基准目录, 值, 新全路径);
                string 新字面量 = "\"" + 转义(新值) + "\"";
                if (string.Equals(新字面量, 匹配.Value, StringComparison.Ordinal)) continue;
                编辑.Add(编辑清单C.造编辑项(工程路径, 工程文本, 行首表, 匹配.Index, 匹配.Length, 新字面量));
            }
            return (编辑, 命中);
        }

        // ================= 二、新建文件:往工程文件里加一条 =================

        /// <summary>
        /// 往 `.hbp` **文本**的 `文件` 数组里加一项(只动那一处:注释、缩进、字段顺序都原样留着)。
        /// 返回 null = 这份文本里找不到 `文件` 数组(调用方退回"读成对象再保存");
        /// `说明` 里写明都干了什么(重复项 / 加在哪儿),给界面打一行用。
        /// </summary>
        public static string? 工程文本加文件(string 工程文本, string 相对路径, out string 说明)
            => 工程文本加项(工程文本, "文件", 相对路径, out 说明);

        /// <summary>
        /// 第193批:「添加文件」把磁盘上已有的文件加进工程时,它该进 `.hbp` 的**哪张表**。
        /// 工程树本来就分三种节点(`文件` / `汇编源码` / `资源`),所以按后缀定:
        /// `.hb` → `文件`;`.hba` → `汇编`(C22 那张表:接在源码展开结果之后一起汇编,不是当源码编);
        /// 别的(图片、数据这类)→ `资源`(执行时当虚拟机的输入流;编译时会跳过)。
        /// **纯函数**,自检直接喂数。
        /// </summary>
        public static string 该进哪个表(string? 路径)
        {
            if (汇编源码C.是汇编源码(路径)) return 汇编字段;
            if ((路径 ?? "").Trim().EndsWith(".hb", StringComparison.OrdinalIgnoreCase)) return "文件";
            return "资源";
        }

        /// <summary>
        /// 第193批(用户 2026-09-21 要求资源管理器右键能「添加文件」):把一个**磁盘上已有的文件**
        /// 登记进工程时,`.hbp` 的 `文件` 表里该写成什么。**纯函数**(不碰磁盘、不读工程文件),自检直接喂数。
        ///
        /// 口径:
        /// <list type="bullet">
        /// <item>文件在工程目录里(含子目录)→ **相对工程目录**的路径,分隔符统一成 `/`(工程文件里一直这么写);</item>
        /// <item>文件在工程目录外 → **绝对路径**(`工程文件C.绝对路径` 本来就认绝对路径);
        ///       这么选的代价写在调用方的提示里:工程整体搬到别处时,这一条要重新登记;</item>
        /// <item>路径写不动(带非法字符)或工程目录为空 → 原样给,只把分隔符统一 —— 宁可比不出来,别在这里抛。</item>
        /// </list>
        /// </summary>
        public static string 工程里的路径写法(string 项目目录, string 文件全路径)
        {
            string 全 = (文件全路径 ?? "").Trim();
            if (全.Length == 0) return "";
            string 目录 = (项目目录 ?? "").Trim();
            if (目录.Length == 0) return 全.Replace('\\', '/');
            // H64:工程在系统目录里(虚拟路径)⇒ 只做前缀比、算字符串层面的相对路径
            if (虚拟路径C.是虚拟(全) || 虚拟路径C.是虚拟(目录))
            {
                string 相对虚拟 = 虚拟相对(目录, 虚拟路径C.收正(全));
                return 相对虚拟;
            }
            try
            {
                string 目标 = Path.GetFullPath(全);
                string 相对 = Path.GetRelativePath(Path.GetFullPath(目录), 目标);
                // 相对路径能表达(不含 `..` 往上跑)就写相对的;否则按绝对路径登记
                if (!相对.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(相对))
                    return 相对.Replace('\\', '/');
                return 目标.Replace('\\', '/');
            }
            catch { return 全.Replace('\\', '/'); }
        }

        /// <summary>
        /// 往 `.hbp` **文本**里某个**数组字段**加一项(只动那一处,C22 的"参与编译"勾选就走它)。
        /// `字段名` 是 JSON 里的键(`文件` / `汇编`)。找不到那个字段时:
        /// <list type="bullet">
        /// <item>`汇编`(C22)→ 在 `文件` 数组**屁股后面现开一个**表(用户不必先手工写一个空表);</item>
        /// <item>别的字段 → 返回 null(调用方退回"读成对象再保存")。</item>
        /// </list>
        /// </summary>
        public static string? 工程文本加项(string 工程文本, string 字段名, string 相对路径, out string 说明)
        {
            说明 = "";
            if (string.IsNullOrEmpty(工程文本) || string.IsNullOrEmpty(字段名)) return null;
            string 项 = 相对路径.Replace('\\', '/').Trim();
            if (项.Length == 0) return null;

            Match 键 = 数组正则(字段名).Match(工程文本);
            if (!键.Success)
            {
                if (字段名 != 汇编字段) return null;
                Match 文件键 = 数组正则("文件").Match(工程文本);
                if (!文件键.Success) return null;                 // 连 `文件` 表都没有:不像我们的工程文件,别乱插
                int 文件开 = 文件键.Index + 文件键.Length - 1;
                int 文件闭 = 找数组结尾(工程文本, 文件开);
                if (文件闭 < 0) return null;
                string 换行 = 代码排版C.检测换行(工程文本);
                int 行起 = 工程文本.LastIndexOf('\n', Math.Max(0, 文件键.Index - 1)) + 1;
                string 缩进 = 前导空白(工程文本, 行起);
                说明 = $"在 `{字段名}` 表(新建)里加了 `{项}`";
                return 工程文本[..(文件闭 + 1)]
                    + "," + 换行 + 缩进 + "\"" + 字段名 + "\": [" + 换行
                    + 缩进 + "    \"" + 转义(项) + "\"" + 换行 + 缩进 + "]"
                    + 工程文本[(文件闭 + 1)..];
            }

            int 开 = 键.Index + 键.Length - 1;                      // `[` 的位置
            int 闭 = 找数组结尾(工程文本, 开);
            if (闭 < 0) return null;
            return 在数组里加项(工程文本, 字段名, 开, 闭, 项, out 说明);
        }

        /// <summary> 往 [开, 闭] 这个数组里插一项(空数组直接插;非空插在最后一个字面量那一行后面,缩进照它) </summary>
        private static string? 在数组里加项(string 工程文本, string 字段名, int 开, int 闭, string 项, out string 说明)
        {
            说明 = "";
            // 已经在表里了?(按字面量值比,不区分大小写)
            var 注释区 = 代码解析器.取注释区(工程文本);
            var 已有 = new List<string>();
            foreach (Match 匹配 in 字符串字面量正则().Matches(工程文本))
            {
                if (匹配.Index < 开 || 匹配.Index >= 闭) continue;      // 只看这个数组里面的
                if (编辑清单C.在保护区内(注释区, 匹配.Index)) continue;
                if (反转义(匹配.Value[1..^1]) is { } 值) 已有.Add(值);
            }
            if (已有.Any(x => string.Equals(x.Replace('\\', '/'), 项, StringComparison.OrdinalIgnoreCase)))
            {
                说明 = $"`{项}` 已经在 {字段名} 表里(工程文件没动)";
                return 工程文本;
            }

            string 换行 = 代码排版C.检测换行(工程文本);
            int 数组行起 = 工程文本.LastIndexOf('\n', Math.Max(0, 开 - 1)) + 1;
            string 原缩进 = 前导空白(工程文本, 数组行起);
            // 空数组:`[` 后面直接插;非空:插在最后一个字面量所在的**行**后面,缩进照它
            if (已有.Count == 0)
            {
                string 插入 = 换行 + 原缩进 + "    " + "\"" + 转义(项) + "\"" + 换行 + 原缩进;
                说明 = $"在空 {字段名} 表里加了 `{项}`";
                return 工程文本[..(开 + 1)] + 插入 + 工程文本[(开 + 1)..];
            }

            int 末字面量 = -1;
            foreach (Match 匹配 in 字符串字面量正则().Matches(工程文本))
            {
                if (匹配.Index < 开 || 匹配.Index >= 闭) continue;
                if (编辑清单C.在保护区内(注释区, 匹配.Index)) continue;
                末字面量 = 匹配.Index + 匹配.Length - 1;
            }
            if (末字面量 < 0) return null;
            int 末行起 = 工程文本.LastIndexOf('\n', 末字面量) + 1;
            string 末缩进 = 前导空白(工程文本, 末行起);
            // 逗号已经有了就不再加一个(用户手工排版可能带尾逗号 —— 工程文件本来也允许)
            string 之间 = 工程文本[(末字面量 + 1)..闭];
            bool 有逗号 = 之间.Contains(',');
            string 插 = (有逗号 ? "" : ",") + 换行 + 末缩进 + "\"" + 转义(项) + "\"";
            说明 = $"在 {字段名} 表末尾加了 `{项}`";
            return 工程文本[..(末字面量 + 1)] + 插 + 工程文本[(末字面量 + 1)..];
        }

        /// <summary>
        /// 从 `.hbp` **文本**里某个数组字段拿掉一项(C22 取消勾选走它)。**只动那一处**:
        /// 整行只有这一个字面量(最常见)就整行删掉;一行里还有别的项就只删字面量 + 邻接的一个逗号。
        /// 返回 null = 这份文本读不动(调用方退回"读成对象再保存")。`说明` 里写清动了没有。
        /// </summary>
        public static string? 工程文本去项(string 工程文本, string 字段名, string 相对路径, out string 说明)
        {
            说明 = "";
            if (string.IsNullOrEmpty(工程文本) || string.IsNullOrEmpty(字段名)) return null;
            string 项 = 相对路径.Replace('\\', '/').Trim();
            if (项.Length == 0) return null;

            Match 键 = 数组正则(字段名).Match(工程文本);
            if (!键.Success)
            {
                说明 = $"工程文件里没有 `{字段名}` 表(没动)";
                return 工程文本;
            }
            int 开 = 键.Index + 键.Length - 1;
            int 闭 = 找数组结尾(工程文本, 开);
            if (闭 < 0) return null;
            var 注释区 = 代码解析器.取注释区(工程文本);
            foreach (Match 匹配 in 字符串字面量正则().Matches(工程文本))
            {
                if (匹配.Index < 开 || 匹配.Index >= 闭) continue;
                if (编辑清单C.在保护区内(注释区, 匹配.Index)) continue;
                if (反转义(匹配.Value[1..^1]) is not { } 值) continue;
                if (!string.Equals(值.Replace('\\', '/'), 项, StringComparison.OrdinalIgnoreCase)) continue;

                说明 = $"从 `{字段名}` 表里拿掉了 `{项}`";
                // ① 整行只有它(可能有逗号):连行一起删 —— 排版最干净
                int 行起 = 工程文本.LastIndexOf('\n', Math.Max(0, 匹配.Index - 1)) + 1;
                int 行止 = 工程文本.IndexOf('\n', 匹配.Index + 匹配.Length);
                行止 = 行止 < 0 ? 工程文本.Length : 行止 + 1;
                if (工程文本[行起..行止].Trim().TrimEnd(',') == 匹配.Value)
                    return 工程文本[..行起] + 工程文本[行止..];
                // ② 同一行还有别的项:删字面量 + 紧跟其后的逗号(没有就删它前面的)
                int 起 = 匹配.Index, 止 = 匹配.Index + 匹配.Length;
                int 后逗 = 工程文本.IndexOf(',', 止);
                if (后逗 >= 0 && 后逗 < 闭) 止 = 后逗 + 1;
                else
                {
                    int 前逗 = 工程文本.LastIndexOf(',', Math.Max(0, 匹配.Index - 1));
                    if (前逗 > 开) 起 = 前逗;
                }
                return 工程文本[..起] + 工程文本[止..];
            }
            说明 = $"`{项}` 本来就不在 `{字段名}` 表里(没动)";
            return 工程文本;
        }

        /// <summary> C22:`.hbp` 里"参与编译的汇编源码"那一档的键名(唯一一处字面量) </summary>
        public const string 汇编字段 = "汇编";

        /// <summary> 从 开(`[` 的位置)往后找配对的 `]`;跳过字符串字面量里的括号。找不到返回 -1 </summary>
        private static int 找数组结尾(string 文本, int 开)
        {
            bool 在串里 = false;
            for (int i = 开 + 1; i < 文本.Length; i++)
            {
                char c = 文本[i];
                if (在串里)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') 在串里 = false;
                    continue;
                }
                if (c == '"') { 在串里 = true; continue; }
                if (c == ']') return i;
            }
            return -1;
        }

        private static string 前导空白(string 文本, int 行起)
        {
            int i = 行起;
            while (i < 文本.Length && (文本[i] == ' ' || 文本[i] == '\t')) i++;
            return 文本[行起..i];
        }

        // ================= 三、路径小工具(相对路径一律写 '/' —— 与示例的写法一致) =================

        /// <summary> 字符串字面量 → 真实值(只认常见转义;有别的转义就当认不出来,返回 null) </summary>
        public static string? 反转义(string 字面量)
        {
            var sb = new StringBuilder(字面量.Length);
            for (int i = 0; i < 字面量.Length; i++)
            {
                char c = 字面量[i];
                if (c != '\\') { sb.Append(c); continue; }
                if (i + 1 >= 字面量.Length) return null;
                char 下 = 字面量[++i];
                switch (下)
                {
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 >= 字面量.Length) return null;
                        if (!ushort.TryParse(字面量.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                null, out ushort 码)) return null;
                        sb.Append((char)码);
                        i += 4;
                        break;
                    default: return null;
                }
            }
            return sb.ToString();
        }

        /// <summary> 真实值 → JSON 字符串字面量里该写的样子(路径里的 `\` 要转义) </summary>
        public static string 转义(string 值) =>
            (值 ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        /// <summary> 把 `路径` 按 `基准目录` 解析成全路径(错了/空了返回空串)。
        /// H64:两边任一是虚拟路径(系统目录那一档)就只做字符串拼接 + 收正 </summary>
        public static string 解析(string 基准目录, string 路径)
        {
            try
            {
                if (虚拟路径C.是虚拟(路径) || 虚拟路径C.是虚拟(基准目录))
                    return 虚拟路径C.收正(虚拟路径C.拼接(基准目录, 路径));
                string p = (路径 ?? "").Trim().Replace('/', Path.DirectorySeparatorChar);
                if (p.Length == 0) return "";
                return Path.IsPathRooted(p) ? Path.GetFullPath(p) : Path.GetFullPath(Path.Combine(基准目录 ?? "", p));
            }
            catch { return ""; }
        }

        /// <summary>
        /// 指向 `目标全路径` 的新字面量:**跟着旧写法** —— 旧的是绝对路径就还给绝对路径,
        /// 旧的是相对路径就按 `基准目录` 重新算相对路径(换目录/换层级都能算对),分隔符统一写 `/`。
        /// H64:系统目录那一档(两边都是虚拟路径)直接按前缀算相对路径,不碰 `Path` 那一套。
        /// </summary>
        public static string 字面量(string 基准目录, string 旧字面量, string 目标全路径)
        {
            string 基准 = 基准目录 ?? "", 目标 = 目标全路径 ?? "";
            if (虚拟路径C.是虚拟(目标) || 虚拟路径C.是虚拟(基准))
            {
                string 相对 = 虚拟相对(基准, 目标);
                return 虚拟路径C.是虚拟(旧字面量) ? 虚拟路径C.规整(目标) : 相对;
            }
            if (Path.IsPathRooted((旧字面量 ?? "").Replace('/', Path.DirectorySeparatorChar)))
                return 目标.Replace('\\', '/');
            try
            {
                string 相对 = Path.GetRelativePath(基准, 目标);
                return 相对.Replace('\\', '/');
            }
            catch { return 目标.Replace('\\', '/'); }
        }

        /// <summary> 虚拟路径版的"相对路径"(`内容://树/库/a.hb` 相对 `内容://树` ⇒ `库/a.hb`;
        /// 跑不到基准下面就给绝对那一份)</summary>
        private static string 虚拟相对(string 基准, string 目标)
        {
            string 基准串 = 虚拟路径C.规整(基准).TrimEnd('/');
            string 目标串 = 虚拟路径C.规整(目标);
            if (基准串.Length == 0) return 目标串;
            if (!目标串.StartsWith(基准串 + "/", StringComparison.OrdinalIgnoreCase)) return 目标串;
            return 目标串[(基准串.Length + 1)..];
        }

        private static string 全路径(string 路径)
        {
            if (虚拟路径C.是虚拟(路径)) return 虚拟路径C.收正(路径);   // H64:虚拟路径不做绝对化
            try { return Path.GetFullPath(路径); } catch { return 路径; }
        }

        /// <summary>
        /// 写回一个文本文件,**保留它原来有没有 BOM**。
        /// C26 起口径收在 `文本落盘C.写回` 一处(IDE 保存 / 这里 / `hbcc --写回` 三条路共用)——
        /// 以前三份各写各的,IDE 那条路用的是带 BOM 的 `Encoding.UTF8`,存一次就把 `.hb` 改出 BOM。
        /// </summary>
        public static void 写回(string 路径, string 文本) => 文本落盘C.写回(路径, 文本);

        /// <summary> `包含 "路径"` / `import "路径"`(行首那个词;第 1 组 = 引号里的路径)</summary>
        private static Regex 包含正则() => _包含正则;
        private static readonly Regex _包含正则 = new(
            "^[ \t]*(?:包含|import)[ \t]+\"([^\"]*)\"", RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary> JSON 字符串字面量(带转义)</summary>
        private static Regex 字符串字面量正则() => _字符串正则;
        private static readonly Regex _字符串正则 = new("\"(?:\\\\.|[^\"\\\\])*\"", RegexOptions.Compiled);

        /// <summary> `.hbp` 里某个数组字段的开头(第 1 组含 `[`;字段名进正则前转义)</summary>
        private static Regex 数组正则(string 字段名)
        {
            lock (_数组正则表)
            {
                if (_数组正则表.TryGetValue(字段名, out var 有)) return 有;
                var 建 = new Regex("\"" + Regex.Escape(字段名) + "\"[ \t]*:[ \t]*(\\[)", RegexOptions.Compiled);
                _数组正则表[字段名] = 建;
                return 建;
            }
        }

        private static readonly Dictionary<string, Regex> _数组正则表 = new(StringComparer.Ordinal);
    }
}
