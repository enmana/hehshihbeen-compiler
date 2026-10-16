using System.IO;
using System.Text.Json;

namespace hehshihbeen_compiler
{
    /// <summary> 命令面板里一条命令从哪儿来 </summary>
    public enum 命令来源C
    {
        /// <summary> 菜单项(界面上真挂着的那些:`MainWindow` 那一棵树现场走出来的) </summary>
        菜单 = 0,

        /// <summary> 帮助站点里的一个话题(按锚点打开那一页) </summary>
        帮助 = 1,

        /// <summary> 面板自己那一条(`打开命令面板`)—— 它不在菜单上,所以单独一档 </summary>
        面板 = 2,
    }

    /// <summary>
    /// 命令面板里的一条候选。**纯数据**:窗口那一层只负责画与执行,不自己判"叫什么、从哪来"。
    /// `标号` 是它的唯一标识,也是"最近用过"记在盘上的东西 —— 下面三条各有一套前缀:
    ///   · 菜单:`菜单\u0001文件\u0001新建项目…`(路径用 `\u0001` 拼,免得中文名里恰好有点号时切错);
    ///   · 帮助:`帮助\u0001指令\u0001ADD`;
    ///   · 面板:`面板\u0001打开命令面板`(干脆就用命令名,只此一条)。
    /// 前缀分开是为了"菜单改了个标题"与"帮助换了个锚点"互不牵连(两种标号撞不到一起)。
    /// </summary>
    public sealed class 命令项C
    {
        /// <summary> 命令名(菜单项标题去了助记符与省略号;帮助话题就是话题标题) </summary>
        public string 命令 { get; set; } = "";

        /// <summary> 从哪儿来(窗口按它分组显示,也决定"执行"那一下走哪条路) </summary>
        public 命令来源C 来源 { get; set; } = 命令来源C.菜单;

        /// <summary> 完整路径(给人看的那一串,例如 `编辑 / 转到定义`、`帮助 / 指令 / ADD`) </summary>
        public string 路径 { get; set; } = "";

        /// <summary> 唯一标识(记"最近用过"用;见类注释里那三条前缀) </summary>
        public string 标号 { get; set; } = "";

        /// <summary> 帮助话题的锚点(`来源 = 帮助` 时才有;打开站点就按它跳) </summary>
        public string 锚点 { get; set; } = "";

        /// <summary> 菜单之路(从顶层菜单项到它自己,含自己);`来源 = 菜单` 时才有 —— 执行时按这条路在树上找回来 </summary>
        public List<string> 菜单之路 { get; set; } = [];

        /// <summary> 现在生效的键位文本(默认值;窗口会用快捷键表里"用户改过的"顶掉它) </summary>
        public string 键位 { get; set; } = "";

        /// <summary> 一句说明(菜单项没有 ToolTip 就给空串) </summary>
        public string 提示 { get; set; } = "";

        /// <summary> 画在右边那串:`菜单` / `帮助` / `命令面板` </summary>
        public string 来源文本 => 来源 switch
        {
            命令来源C.菜单 => "菜单",
            命令来源C.帮助 => "帮助",
            _ => "命令面板",
        };

        /// <summary> 面板下面那一行细节:先给菜单项自己的说明(ToolTip),没有就退回路径 </summary>
        public string 细节 => 提示.Length > 0 ? 提示 : 路径;

        /// <summary> 一行摘要(`显示给用户的那一串`) </summary>
        public override string ToString() =>
            键位.Length > 0 ? $"{路径}  [{键位}]" : 路径;

        public static string 菜单标号(IEnumerable<string> 路径段) =>
            "菜单\u0001" + string.Join('\u0001', 路径段);

        public static string 帮助标号(string 锚点) => "帮助\u0001" + 锚点;

        public const string 面板标号 = "面板\u0001打开命令面板";
    }

    /// <summary> "最近用过的一条命令":标号 + 次数 + 最后一次的时刻 </summary>
    public sealed class 命令历史项C
    {
        public string 标号 { get; set; } = "";
        public int 次数 { get; set; }
        public DateTime 最后用过 { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// **命令面板**(Ctrl+Shift+P)的数据与排序 —— 纯函数、零界面依赖,自检直接喂参数。
    ///
    /// 三件事:
    ///   ① **候选怎么来的**:菜单项由窗口那一层现场走 WPF 那棵树(`列菜单`),帮助话题由这里从帮助站点
    ///      那棵树上摘叶子(`列帮助` —— 与 `hbcc --生成帮助` 同源,所以站点里有多少话题,面板里就有多少条);
    ///   ② **怎么排**:`匹配` 给一个查询串排出前 N 条。打分口径与编辑器补全同源(都用
    ///      `补全词表C` 的拼音首字母/全拼那两套缓存),再加上"最近用过"的加权 ——
    ///      常用的那几条会自己浮上来,不必每次打全名;
    ///   ③ **"最近用过"存在哪**:程序目录下的 `命令历史.json`(与 `选项.json` / `最近项目.json` 同一处)。
    ///
    /// 为什么要"菜单现场走树"而不是在这里抄一份菜单表:抄一份就多一个事实源 ——
    /// 菜单加一项、面板里看不到(或者反过来,面板里点了一条早已不存在的菜单项)。
    /// 走树之后**菜单就是唯一事实源**:菜单上有什么,面板里就有什么;名字改了两边一起改。
    /// 自检那条"菜单项一条不落地进得了面板"就是钉这个的。
    /// </summary>
    public sealed class 命令面板表C
    {
        /// <summary> "最近用过"最多记几条(面板空查询时按它排在最前面) </summary>
        public const int 最多历史 = 12;

        /// <summary> 面板一次最多给多少条候选(打一个字不该把几百条全铺出来) </summary>
        public const int 默认上限 = 60;

        private static readonly JsonSerializerOptions 选项 = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public List<命令历史项C> 历史 { get; set; } = [];

        /// <summary> 进程内共用的一份(面板与自检读的是同一份) </summary>
        public static 命令面板表C 当前 { get; } = 加载();

        /// <summary> 默认落盘位置(程序目录下的 命令历史.json) </summary>
        public static string 文件路径默认 => Path.Combine(程序目录C.数据目录, "命令历史.json");

        // ---------------- 最近用过 ----------------

        /// <summary>
        /// 记一条"用过":用过的次数 **+1**、时刻刷新成现在;记完**立刻落盘**(不攒着 —— 攒着就会在崩溃时丢)。
        /// 已有那一条**原地加**、不是"先删了再加一条新的"(那种写法次数永远停在 1 —— 实测踩过)。
        /// </summary>
        public void 记用过(string 标号, DateTime? 时间 = null)
        {
            if (string.IsNullOrWhiteSpace(标号)) return;
            var 已有 = 历史.FirstOrDefault(x => string.Equals(x.标号, 标号, StringComparison.Ordinal));
            if (已有 == null) 历史.Add(new 命令历史项C { 标号 = 标号, 次数 = 1, 最后用过 = 时间 ?? DateTime.Now });
            else
            {
                已有.次数 += 1;
                已有.最后用过 = 时间 ?? DateTime.Now;
            }
            裁剪();
            保存();
        }

        public void 清空() { 历史.Clear(); 保存(); }

        /// <summary> 标号 → 用过几次(没用过 0) </summary>
        public int 次数(string 标号) =>
            历史.FirstOrDefault(x => string.Equals(x.标号, 标号, StringComparison.Ordinal))?.次数 ?? 0;

        /// <summary> 标号 → 最后一次用的时刻(没用过给 null) </summary>
        public DateTime? 最后用过(string 标号) =>
            历史.FirstOrDefault(x => string.Equals(x.标号, 标号, StringComparison.Ordinal))?.最后用过;

        /// <summary> 按"最后用过"倒序取前几条(标号表;面板空查询就按它排) </summary>
        public List<string> 最近标号(int 上限 = 最多历史) =>
            [.. 历史.OrderByDescending(x => x.最后用过).Take(上限).Select(x => x.标号)];

        private void 裁剪() =>
            历史 = [.. 历史.OrderByDescending(x => x.最后用过).Take(最多历史)];

        // ---------------- 读写(坏了当"没有历史",不影响面板能用) ----------------

        public string 到文本() => JsonSerializer.Serialize(this, 选项);

        public static 命令面板表C 从文本(string json)
        {
            try
            {
                var 表 = JsonSerializer.Deserialize<命令面板表C>(json) ?? new 命令面板表C();
                表.历史 ??= [];
                表.历史.RemoveAll(x => string.IsNullOrWhiteSpace(x.标号));
                return 表;
            }
            catch (JsonException) { return new 命令面板表C(); }
        }

        public static 命令面板表C 加载(string? 路径 = null)
        {
            string 文件 = 路径 ?? 文件路径默认;
            if (!File.Exists(文件)) return new 命令面板表C();
            try { return 从文本(File.ReadAllText(文件)); }
            catch { return new 命令面板表C(); }
        }

        public void 保存(string? 路径 = null)
        {
            string 文件 = 路径 ?? 文件路径默认;
            try
            {
                裁剪();
                Directory.CreateDirectory(Path.GetDirectoryName(文件) ?? ".");
                File.WriteAllText(文件, 到文本());
            }
            catch { }
        }

        // ---------------- 候选:帮助话题(菜单那一半由界面走树给) ----------------

        /// <summary>
        /// 帮助站点里的**全部话题** → 面板候选。
        /// 口径与站点一致:树里每个**有正文的叶子**是一个话题(一级节自己也是一页 —— 所以它也算);
        /// `根` 给 null 时现建一棵(建树要读 `Sample\库\*.hb`,调用方最好自己建一次复用)。
        /// 标题重复的(不同分类下同名)只在**路径**上区分,锚点各自独立 —— 面板里两条都在,都有锚点可跳。
        /// </summary>
        public static List<命令项C> 列帮助(帮助节点C? 根 = null)
        {
            var 结果 = new List<命令项C>();
            if (根 == null)
            {
                try { 根 = 帮助站点C.建树(); } catch { return 结果; }
            }
            // 一趟深搜把路径攒出来(别对每个叶子各来一次 `找链` —— 那是"每个节点都把整棵树走一遍",
            // 287 个话题就是 287 遍;一趟下去 O(节点数))。
            走(根, []);
            return 结果;

            void 走(帮助节点C 节, List<string> 父路径)
            {
                var 这里 = new List<string>(父路径);
                if (节.标题.Length > 0 && (这里.Count == 0 || !string.Equals(这里[^1], 节.标题, StringComparison.Ordinal)))
                    这里.Add(节.标题);
                bool 是叶子 = 节.子节点.Count == 0;
                if (是叶子 && 节.锚点.Length > 0 && 这里.Count > 0)
                    结果.Add(new 命令项C
                    {
                        命令 = 节.标题,
                        来源 = 命令来源C.帮助,
                        路径 = string.Join(" / ", 这里),
                        标号 = 命令项C.帮助标号(节.锚点),
                        锚点 = 节.锚点,
                    });
                foreach (var 子 in 节.子节点) 走(子, 这里);
            }
        }

        // ---------------- 排序与匹配 ----------------

        /// <summary>
        /// 给一个查询串排出候选(前 `上限` 条)。**空查询** = 最近用过的排最前,其余按原顺序跟在后面
        /// (面板一打开就是"我常用的那几条",不必先打字)。
        ///
        /// 打分口径(小的在前;数字与编辑器补全那套同一套字):
        ///   ① `0` 命令名**逐字相同**;② `1` 中文/原样**前缀**;
        ///   ③ `2 + 命中位置 / 16` **中间命中**(越靠前越优先;位置项只当次序用,不参与"是不是命中"的判断);
        ///   ④ `40` 起:**拼音首字母前缀**;⑤ `50` 起:**全拼前缀**(带模糊规则);
        ///   ⑥ `70` 起"打多了"、`80` 起"跳字母"(手比脑子快也不至于一条都不给);
        ///   ⑦ **最近用过 −3**(只影响次序,不会把不匹配的东西顶上来 —— 不匹配的连分都没有)。
        /// 路径里的父级也参与匹配(打"调试"能出 `调试 / 切换断点` 这种),命中父级的排在命中自己的后面(`+8`)。
        /// </summary>
        public List<命令项C> 匹配(string? 查询, IReadOnlyList<命令项C> 全部候选,
            int 上限 = 默认上限, IReadOnlyCollection<string>? 模糊规则 = null)
        {
            string 词 = (查询 ?? "").Trim();
            var 最近 = new Dictionary<string, int>(StringComparer.Ordinal);
            var 序 = 最近标号(int.MaxValue);
            for (int i = 0; i < 序.Count; i++) 最近[序[i]] = i;

            if (词.Length == 0)
            {
                // 空查询:最近用过的(按最后一次倒序)在前,其余按调用方给的顺序
                var 用过 = new List<命令项C>();
                var 见过 = new HashSet<string>(StringComparer.Ordinal);
                foreach (string 标 in 序)
                    foreach (var 条 in 全部候选)
                        if (string.Equals(条.标号, 标, StringComparison.Ordinal) && 见过.Add(条.标号)) 用过.Add(条);
                foreach (var 条 in 全部候选)
                    if (见过.Add(条.标号)) 用过.Add(条);
                return [.. 用过.Take(上限)];
            }

            var 打分表 = new List<(int 分, int 序, 命令项C 条)>();
            int 排 = 0;
            foreach (var 条 in 全部候选)
            {
                int 分 = 一条的分(条, 词, 模糊规则, out bool 命中自己);
                if (分 < 0) continue;
                if (!命中自己) 分 += 8;                       // 命中父级路径的靠后
                if (最近.TryGetValue(条.标号, out int 位)) 分 -= Math.Max(1, 3 - 位 / 4);   // 最近用过的稍微前移
                打分表.Add((分, 排++, 条));
            }
            return [.. 打分表.OrderBy(x => x.分).ThenBy(x => x.序).Select(x => x.条).Take(上限)];
        }

        /// <summary> 单条打分(不沾返回 -1);`命中自己` 说的是"命令名本身命中"还是"只有父级路径命中" </summary>
        private static int 一条的分(命令项C 条, string 词, IReadOnlyCollection<string>? 规则, out bool 命中自己)
        {
            命中自己 = false;
            int 自己 = 串的分(条.命令, 词, 规则);
            if (自己 >= 0) { 命中自己 = true; return 自己; }
            string 词上 = 词.ToUpperInvariant();
            bool 全字母 = 词.Length > 0 && 词.All(char.IsAsciiLetter);
            string? 尾 = 父级(条.路径);
            return 尾 == null ? -1 : 串的分(尾, 词, 规则, 全字母, 词上);
        }

        /// <summary> 路径里"命令名"前面那一段(`调试 / 切换断点` → `调试`;没有父级给 null) </summary>
        private static string? 父级(string 路径)
        {
            int 切 = 路径.LastIndexOf(" / ", StringComparison.Ordinal);
            return 切 <= 0 ? null : 路径[..切];
        }

        private static int 串的分(string 名, string 词, IReadOnlyCollection<string>? 规则) =>
            串的分(名, 词, 规则, 词.All(char.IsAsciiLetter), 词.ToUpperInvariant());

        private static int 串的分(string 名, string 词, IReadOnlyCollection<string>? 规则, bool 全字母, string 词上)
        {
            if (名.Length == 0) return -1;
            if (string.Equals(名, 词, StringComparison.OrdinalIgnoreCase)) return 0;
            if (名.StartsWith(词, StringComparison.OrdinalIgnoreCase)) return 1;
            int 位 = 名.IndexOf(词, StringComparison.OrdinalIgnoreCase);
            if (位 > 0) return 2 + Math.Min(位, 63) / 16;
            if (!全字母) return -1;

            string 首 = 补全词表C.拼音首字母(名);
            if (首.Length > 0)
            {
                if (首.StartsWith(词上, StringComparison.Ordinal)) return 40;
                if (词上.Length > 首.Length && 词上.StartsWith(首, StringComparison.Ordinal)) return 70;
            }
            int 最好 = -1;
            foreach (string 拼 in 补全词表C.全拼变体(名, 规则))
            {
                if (拼.StartsWith(词上, StringComparison.Ordinal)) return 50;
                if (词上.Length > 拼.Length && 词上.StartsWith(拼, StringComparison.Ordinal)) 最好 = Math.Max(最好, 70);
                else if (是跳字母(词上, 拼)) 最好 = Math.Max(最好, 80);
            }
            return 最好;
        }

        /// <summary>
        /// "跳字母"(口径与编辑器补全一致,见 `补全词表C` 里那一段):打进去那串**第一个必须是拼音的第一个字母**,
        /// 其余按顺序出现即可(`wtbj` → `问题标记`);**至少 3 个字母**才给这个特权 ——
        /// 两个字母太短,一跳就中一大片,面板会变成"打什么都是这几条"。
        /// </summary>
        private static bool 是跳字母(string 词上, string 拼) =>
            词上.Length >= 3 && 词上.Length <= 拼.Length && 词上[0] == 拼[0] && 是子序列(词上, 拼);

        private static bool 是子序列(string 短, string 长)
        {
            int 位 = 0;
            foreach (char c in 长)
            {
                if (位 < 短.Length && 短[位] == c) 位++;
                if (位 == 短.Length) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// 面板要用的一整套(全部候选 + 排序 + 最近用过),界面那一层只跟它打交道。
    /// 候选在**建的时候算一次**(菜单那棵树走一遍 + 帮助树摘一遍)——
    /// 面板每次打开都重建一份 `全部候选` 是件轻活(实测几百条),但**排序**每敲一个字都要跑,所以分开。
    /// </summary>
    public sealed class 命令面板模型C
    {
        private readonly 命令面板表C 表;

        public 命令面板模型C(IReadOnlyList<命令项C> 候选表, 命令面板表C? 历史表 = null)
        {
            全部候选 = 候选表 ?? [];
            表 = 历史表 ?? 命令面板表C.当前;
        }

        /// <summary> 一条不落的候选(菜单 + 面板自带的 + 帮助话题) </summary>
        public IReadOnlyList<命令项C> 全部候选 { get; }

        /// <summary> 现在生效的模糊拼音规则(默认那两条;选项页改过就传进来) </summary>
        public IReadOnlyCollection<string> 模糊规则 { get; set; } = 补全词表C.默认模糊规则;

        public List<命令项C> 匹配(string? 查询, int 上限 = 命令面板表C.默认上限) =>
            表.匹配(查询, 全部候选, 上限, 模糊规则);

        public void 记用过(命令项C 条) => 表.记用过(条.标号);
    }
}
