namespace hehshihbeen_compiler
{
    /// <summary>
    /// 编辑器配色的**一个词类**:稳定键(存进主题文件)、给人看的名字、亮/暗两套默认色、一句说明。
    /// 键用中文字面量是故意的 —— 主题文件是人手能读的,`"变量": "#FF001080"` 一眼就懂。
    /// </summary>
    public sealed record 配色词类项C(string 键, string 显示名, string 亮色, string 暗色, string 说明);

    /// <summary>
    /// **编辑器配色的单一事实源**:有哪些词类、每类亮/暗默认色、谁盖谁(优先级)。
    /// 三处都读它,谁也不许再手抄一份:
    ///   · 语法高亮(`高亮扫描C` 按类挑颜色,`界面控件\语法高亮C` 只负责把色值转成画刷)
    ///   · 选项页(`选项 → 调色表` 按这张表生成可调项,见 `选项页.xaml.cs`)
    ///   · 综合自检(校验"每类都有亮/暗默认色、键不重名、选项页确实读的是这张表")
    ///
    /// 这里**只放颜色字符串**(`#AARRGGBB`),不引任何 WPF 类型 —— 所以命令行自检也能读它。
    ///
    /// **默认色的取法**:照 VS Code 那套通用观感(亮主题关键字 `#0000FF`、类型 `#2B91AF`、
    /// 函数 `#795E26`、变量 `#001080`、字符串 `#A31515`、注释 `#008000`、数字 `#098658`;
    /// 暗主题对应 `#569CD6` / `#4EC9B0` / `#DCDCAA` / `#9CDCFE` / `#CE9178` / `#6A9955` / `#B5CEA8`)。
    /// 用户点过"四档蓝色撞在一起"这个毛病 —— 所以**指令/关键字/类型/变量**四类在两套主题里
    /// 都必须一眼分得开(自检有一条专门盯它)。剩下的类:
    ///   · `结构` 与 `类型` 同色系(都是类型名),`结构` 取更深的青(亮 `#1F6F8B` / 暗 `#2E9E92`);
    ///   · `方法` 与 `内建函数` 同色系(都是"函数"),`方法` 取更深的金(亮 `#9A6A00` / 暗 `#DCB66A`);
    ///   · `寄存器` 用洋红系(亮 `#B00040` / 暗 `#D169A0`),跟 变量/关键字 都拉得开;
    ///   · `字符` 比 `字符串` 偏橙一点(亮 `#C05A00` / 暗 `#D7A06A`),两类字面量分得开;
    ///   · `用户方法名` 用紫系(亮 `#7A3E9D` / 暗 `#C49CE0`),`常量` 用黄绿系
    ///     (亮 `#5A8A00` / 暗 `#C8E06A`)—— 这两档都是 H60 那几步新加的,各自跟变量 / 数字 / 金褐那几档拉得开。
    /// </summary>
    public static class 配色词类C
    {
        // 词类键(稳定,别改:主题文件里存的就是它们)
        public const string 默认 = "默认";
        public const string 指令 = "指令";
        public const string 关键字 = "关键字";
        public const string 类型 = "类型";
        public const string 结构 = "结构";
        public const string 用户方法名 = "用户方法名";
        public const string 内建函数 = "内建函数";
        public const string 方法 = "方法";
        public const string 变量 = "变量";
        public const string 常量 = "常量";
        public const string 寄存器 = "寄存器";
        public const string 数字 = "数字";
        public const string 字符串 = "字符串";
        public const string 字符 = "字符";
        public const string 注释 = "注释";

        /// <summary>
        /// **词类表(唯一事实源)** —— 顺序 = 从低到高的优先级(见 `档位`)。
        /// 优先级只决定"同一个字被两档同时认出来时听谁的":
        /// 变量/结构最低(它们靠"声明过的名字"猜,猜错的概率最大),
        /// 字面量与注释最高(它们按扫描出来的区间盖,谁也别想在里面冒头)。
        /// </summary>
        public static readonly 配色词类项C[] 表 =
        [
            new(变量,     "变量名",         "#FF001080", "#FF9CDCFE", "声明过的变量/参数/全局/结构字段的引用"),
            new(常量,     "常量名",         "#FF5A8A00", "#FFC8E06A", "顶层声明且初值是字面量那种(`var i = 0`;判据见 `源码声明扫描C.扫结构`)"),
            new(结构,     "结构名",         "#FF1F6F8B", "#FF2E9E92", "本文件声明的结构(含 `静态 方法` 容器)的名字"),
            new(用户方法名, "用户方法名",   "#FF7A3E9D", "#FFC49CE0", "自己声明的方法名(顶层过程 / 容器成员 / 结构方法)"),
            new(指令,     "汇编指令",       "#FF0000FF", "#FF569CD6", "ISA 助记符,中英两侧都算"),
            new(关键字,   "语言关键字",     "#FFAF00DB", "#FFC586C0", "控制流关键字(如果/循环/返回…),中英两侧都算"),
            new(内建函数, "内建函数",       "#FF795E26", "#FFDCDCAA", "申请/释放/读取行/读文件/当前时间/存字/取字…"),
            new(方法,     "方法名",         "#FF9A6A00", "#FFDCB66A", "带点调用的 `.长度()` / `.清空()` / `.添加()`"),
            new(类型,     "类型名",         "#FF2B91AF", "#FF4EC9B0", "基本类型(整数/字节/文字/地址…)与 `字符串`/`string`"),
            new(寄存器,   "寄存器",         "#FFB00040", "#FFD169A0", "R0 ~ R15"),
            new(数字,     "数字字面量",     "#FF098658", "#FFB5CEA8", "十进制与 0x 十六进制"),
            new(字符串,   "字符串字面量",   "#FFA31515", "#FFCE9178", "\"…\""),
            new(字符,     "字符字面量",     "#FFC05A00", "#FFD7A06A", "'A' / '\\n' / '\\x41'"),
            new(注释,     "注释",           "#FF008000", "#FF6A9955", "// 行注释 与 /* */ 块注释"),
        ];

        /// <summary> 普通文本(不属于任何词类的字)—— 也进表,好让"选项页每一项都能调"这句话成立 </summary>
        public static readonly 配色词类项C 普通文本 = new(默认, "普通文本", "#FF000000", "#FFDCDCDC", "不属于任何词类的代码字");

        /// <summary> 选项页要显示的**全部**项:普通文本 + 词类表 —— 顺序就是这个顺序 </summary>
        public static 配色词类项C[] 全部项 => [普通文本, .. 表];

        /// <summary> 全部词类键(含"默认") </summary>
        public static string[] 全部键 => [.. 全部项.Select(x => x.键)];

        public static bool 是词类(string? 键) =>
            !string.IsNullOrEmpty(键) && 全部项.Any(x => x.键 == 键);

        public static 配色词类项C? 取(string? 键) =>
            string.IsNullOrEmpty(键) ? null : 全部项.FirstOrDefault(x => x.键 == 键);

        /// <summary> 这一类在亮/暗主题下的默认色(`用暗主题` 为 true 就走暗主题那一套) </summary>
        public static string 取默认色(string 键, bool 用暗主题) =>
            取(键) is { } 类 ? (用暗主题 ? 类.暗色 : 类.亮色) : "#FF000000";

        // ================= 思维导图 2.0 那一侧:H60-13("种类 → 配色档") =================
        //
        // 思维导图里要上色的东西有**两族**(见 `引擎\导图分区C.cs`):
        //   · **框**(`框来源C.种类`:文件 / 结构 / 方法 / 流程块);
        //   · **子项**(`分区项C.种类`:声明线C 查出来的 用户方法 / 结构 / 结构方法 / 成员方法 / 变量 /
        //     常量 / 参数 / 局部 / 字段,外加本层自己那两档 语句 / 包含)。
        // **映射表只放这一处**(施工图 · 步 6 ①):`导图分区C` 那一层不许再列一遍"哪个种类是什么颜色"
        // (两套口径迟早打架,与 H60-11 ③ 同一条铁律)。

        /// <summary> 导图自己的**中性色**(亮):没有对应词类的那几样(框的 `文件` / `流程块`,子项的 `语句` / `包含`) </summary>
        public const string 导图中性亮 = "#FF6E6E6E";

        /// <summary> 导图自己的**中性色**(暗)—— 与 15 个词类**分开算**(施工图 ④:别硬套词类) </summary>
        public const string 导图中性暗 = "#FFA8A8A8";

        /// <summary>
        /// **回连线那条红色**(H61-2 口径 ⑤「本文件的方法就用**红色线回连**到声明区的相关处,变量的调用亦同」)。
        /// 亮/暗两套,与词类那 15 档分开算 —— 它画的是"关系",不是代码里哪一个字。
        /// </summary>
        public const string 导图回连亮 = "#FFD13438";

        /// <summary> 回连线那条红色(暗主题)—— 暗底上再亮一点,与中性灰拉得开 </summary>
        public const string 导图回连暗 = "#FFFF6B6B";

        /// <summary>
        /// **选中项到根那一串连接线的绿色**(第278批,用户复查 H61-3 时点名要的:
        /// 「被选中项到根节点的所有连接线均加绿色高亮显示」)。亮/暗两套,与词类那 15 档、
        /// 与回连线那条红**都分开算** —— 它标的是"我现在站在哪一枝上",不是代码里的字、也不是声明关系。
        /// </summary>
        public const string 导图路径亮 = "#FF107C10";

        /// <summary> 选中路径那条绿色(暗主题)—— 暗底上提亮一档,与红色回连线不撞 </summary>
        public const string 导图路径暗 = "#FF6CCB5F";

        /// <summary>
        /// **导图的"种类" → 词类键**(唯一映射表)。词类给**空串** = 这一档没有对应词类,走 `导图中性亮 / 暗`。
        /// 自检盯着两条:① 表里每个词类键都是真词类(或者是有意留空的中性那几档);
        /// ② `Sample\**` 全扫出来的每一个 `种类`(框 + 子项)都在这里登记过 —— 不许有"认不出"的落下来。
        /// </summary>
        public static readonly (string 种类, string 词类)[] 导图映射 =
        [
            // 声明线C 查出来的那一档(用户方法 / 结构 / 结构方法 / 成员方法 / 变量 / 常量 / 参数 / 局部 / 字段)
            // (`方法` 这一档在 `补全词表C.查种类` 里也能查出来 —— 那是带点调用的内建方法 `.长度()`;
            //  它会出现在**调用链**上,不当成"分区项",所以这里按"用户方法"那一档上色)
            ("变量", 变量), ("常量", 常量), ("参数", 变量), ("局部", 变量), ("字段", 变量),
            ("结构", 结构), ("方法容器", 结构),
            ("方法", 用户方法名), ("结构方法", 用户方法名), ("成员方法", 用户方法名),
            // `补全词表C.查种类` 那一族(导图里出现时也各有各的色)
            ("内建函数", 内建函数), ("类型", 类型), ("指令", 指令), ("关键字", 关键字), ("字面量", 关键字),
            // 框的种类 + 本层自己那两档:没有对应词类,走中性色
            ("文件", ""), ("流程块", ""), ("语句", ""), ("包含", ""),
        ];

        /// <summary> 导图一个"种类"对应的词类键(**没有对应词类 / 没登记过都给空串** = 走中性色) </summary>
        public static string 导图词类(string? 种类)
        {
            if (string.IsNullOrEmpty(种类)) return "";
            foreach (var (名, 词类) in 导图映射)
                if (string.Equals(名, 种类, StringComparison.Ordinal)) return 词类;
            return "";
        }

        /// <summary> 这个"种类"在映射表里登记过没有(自检拿它盯"Sample 全扫出来的种类一个都不落") </summary>
        public static bool 导图有映射(string? 种类)
        {
            if (string.IsNullOrEmpty(种类)) return false;
            foreach (var (名, _) in 导图映射)
                if (string.Equals(名, 种类, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary> 导图的中性色(亮 / 暗两套) </summary>
        public static string 取导图中性色(bool 用暗主题) => 用暗主题 ? 导图中性暗 : 导图中性亮;

        /// <summary> **回连线**那条红色(亮 / 暗两套;H61-2 那一族专用色,唯一事实源就在这儿) </summary>
        public static string 取导图回连色(bool 用暗主题) => 用暗主题 ? 导图回连暗 : 导图回连亮;

        /// <summary> **选中项 → 根**那一串连接线的绿色(亮 / 暗两套;第278批那一族专用色) </summary>
        public static string 取导图路径色(bool 用暗主题) => 用暗主题 ? 导图路径暗 : 导图路径亮;

        /// <summary>
        /// 导图里一个"种类"该用的颜色 —— **有词类的走那一档的默认色,没有的走中性色**。
        /// 界面侧(H60-14 画框)请把 `导图词类` 拿到的键递给主题那一层(`主题设置.取词类色`),
        /// 这样选项页里改过的色在导图上也生效;这个函数是给自检与"没有主题"的场合用的。
        /// </summary>
        public static string 取导图色(string? 种类, bool 用暗主题)
        {
            string 词类 = 导图词类(种类);
            return 词类.Length > 0 ? 取默认色(词类, 用暗主题) : 取导图中性色(用暗主题);
        }

        /// <summary>
        /// 优先级(数组下标 + 1;`普通文本` = 0)。**同一个字被两档同时认出来时,档位高的赢**
        /// (见 `高亮扫描C.合并`) —— 顺序写死在 表 里,不再靠"谁后加进列表"这种隐式规则
        /// (以前那套 List.Sort 是不稳定排序,同一位置两档撞上时谁赢看运气)。
        /// </summary>
        public static int 档位(string? 键)
        {
            if (string.IsNullOrEmpty(键) || 键 == 默认) return 0;
            for (int i = 0; i < 表.Length; i++) if (表[i].键 == 键) return i + 1;
            return 0;
        }

        /// <summary>
        /// **老主题文件里的属性名**(第86批以前只有这几个色)。读主题时按它把老配色**搬进**词类,
        /// 别人的 `选项.json` / 自定义主题一点不浪费(见 `主题设置.取词类色`)。
        /// 没登记的老属性(背景/前景/行号…)不参与配色搬迁。
        /// </summary>
        public static readonly (string 老属性, string 词类键)[] 老属性映射 =
        [
            ("关键字颜色",   指令),
            ("寄存器颜色",   寄存器),
            ("数字颜色",     数字),
            ("注释颜色",     注释),
            ("默认文本颜色", 默认),
        ];

        /// <summary> 这个词类在老主题里对应哪个属性(没有返回 null) </summary>
        public static string? 老属性名(string 键)
        {
            foreach (var (老, 类) in 老属性映射) if (类 == 键) return 老;
            return null;
        }

        /// <summary>
        /// **一个词类的实际颜色:显式 → 老属性 → 亮/暗默认**(三级兜底)。
        /// 主题对象(`主题设置`)只负责把两个来源递进来,规则留在这里 —— 这样"老主题文件还认不认"
        /// 命令行自检也验得了(界面那层 `主题设置.取词类色` 就是转发到这个函数)。
        /// </summary>
        /// <param name="词类键">`配色词类C` 里的键</param>
        /// <param name="显式">本主题 `词类颜色` 里的显式值(选项页改的就是它);可以是 null</param>
        /// <param name="取老属性">老主题属性查值(`关键字颜色` 等);可以是 null</param>
        /// <param name="用暗主题">挑亮/暗哪一套默认色</param>
        public static string 解析色(string 词类键, IReadOnlyDictionary<string, string>? 显式,
            Func<string, string?>? 取老属性, bool 用暗主题)
        {
            if (显式 != null && 显式.TryGetValue(词类键, out string? 值) && !string.IsNullOrWhiteSpace(值)) return 值!;
            string? 老属性 = 老属性名(词类键);
            if (取老属性 != null && 老属性 != null && 取老属性(老属性) is { } 旧 && !string.IsNullOrWhiteSpace(旧)) return 旧!;
            return 取默认色(词类键, 用暗主题);
        }

        /// <summary>
        /// 这个底色算不算"暗色主题"(按 `编辑器背景` 的亮度判断,用来挑亮/暗两套默认色)。
        /// 认 `#RGB` / `#RRGGBB` / `#AARRGGBB`;认不出来按亮主题算。
        /// </summary>
        public static bool 是暗背景(string? 背景十六进制)
        {
            var (r, g, b) = 取RGB(背景十六进制);
            if (r < 0) return false;
            // 0.299R + 0.587G + 0.114B(人眼亮度),小于中点算暗
            return 0.299 * r + 0.587 * g + 0.114 * b < 128;
        }

        /// <summary> `#AARRGGBB` / `#RRGGBB` / `#RGB` → (r, g, b);认不出来返回 (-1, -1, -1) </summary>
        public static (int r, int g, int b) 取RGB(string? 颜色)
        {
            if (string.IsNullOrWhiteSpace(颜色)) return (-1, -1, -1);
            string 十六进制 = 颜色.Trim().TrimStart('#');
            try
            {
                if (十六进制.Length == 8) 十六进制 = 十六进制[2..];          // 丢掉 AA
                else if (十六进制.Length == 3)                                // #RGB → #RRGGBB
                    十六进制 = $"{十六进制[0]}{十六进制[0]}{十六进制[1]}{十六进制[1]}{十六进制[2]}{十六进制[2]}";
                if (十六进制.Length != 6) return (-1, -1, -1);
                int 值 = Convert.ToInt32(十六进制, 16);
                return ((值 >> 16) & 0xFF, (值 >> 8) & 0xFF, 值 & 0xFF);
            }
            catch { return (-1, -1, -1); }
        }
    }
}
