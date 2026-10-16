using System.IO;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 标签页的种类(C20):普通源码页 / **汇编源码页**(`.hba`)。
    /// 页种类只影响三件事:保存对话框的后缀与筛选器、"这串文字算不算汇编"、以及界面上的提示口径;
    /// "**默认**不参与编译"由"`.hba` 不进 `.hbp` 的文件表、目录扫描也只认 `.hb`"保证 ——
    /// **但可以手动让它参与**(C22,第155批:编辑器勾「运行 → 汇编源码参与编译」),见 `汇编源码C.是汇编源码`。
    /// </summary>
    public enum 页面种类C
    {
        /// <summary> 普通源码页(`.hb`;工程文件里列着的那一类) </summary>
        普通 = 0,
        /// <summary> 汇编源码页(C20:编译出来的汇编,存成 `.hba`;**默认不进编译**) </summary>
        汇编源码 = 1,
    }

    /// <summary>
    /// C20(用户 2026-09-19 提:"编译成汇编后在编辑器的新页打开,关闭或者退出时提示是否保存")的**纯规则**。
    ///
    /// 这里只算四件事,一行界面代码都不碰(自检直接喂数;界面按算出来的结果开页 / 存盘 / 提示):
    /// <list type="number">
    /// <item>**后缀认定**:`.hba` 才算汇编源码(大小写不敏感);</item>
    /// <item>**落点路径**:`<项目目录>\<名字>.hba` —— 落点是**项目目录里的真文件**,不是"没有落点的临时页",
    ///       所以保存、标脏、关页提示全沿用页上现成那一套(`选卡页视图模型`);</item>
    /// <item>**复用的是哪一页**:同一个项目再编译一次,复用同一页(按**全称**比,大小写不敏感);</item>
    /// <item>**段拼回全文**:工程级展开按 `文件K` 分成每文件一段,按顺序拼回来就是**展开原文**
    ///       (给"存出来的 `.hba` 与这一次展开逐字节一致"留一条可验的等式)。</item>
    /// </list>
    ///
    /// **为什么 `.hba` 而不是 `.hb`**(用户拍板 a):一眼能认出"这不是普通源码";也正因为后缀不同,
    /// 工程文件与目录扫描(只认 `*.hb`)**天生不会**把它当源码收进去 ⇒ "默认不进编译"不用另写判断。
    /// 想存成 `.hb` 也行(另存为时自己改后缀)。
    /// </summary>
    public static class 汇编源码C
    {
        /// <summary> 汇编源码的后缀(用户拍板:"汇编源码"那个后缀,一眼能认出不是普通 `.hb`) </summary>
        public const string 扩展名 = ".hba";

        /// <summary> 按"被展开文件"取落点时叠的那个标记(C23:用户要求文件名 = 被展开文件名 + `_ASM.hba`) </summary>
        public const string 展开标记 = "_ASM";

        /// <summary> 这个名字是不是汇编源码(`.hba`,大小写不敏感;空/相对路径都按后缀认) </summary>
        public static bool 是汇编源码(string? 路径)
        {
            if (string.IsNullOrWhiteSpace(路径)) return false;
            string 后缀;
            try { 后缀 = Path.GetExtension(路径.Trim()); }
            catch { return false; }                       // 路径里带非法字符:按"不是"处理,别在这里抛
            return string.Equals(后缀, 扩展名, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary> 按路径判页种类(打开 `.hba` 文件时走它:汇编源码页与普通源码页各处待遇不同) </summary>
        public static 页面种类C 判种类(string? 路径)
            => 是汇编源码(路径) ? 页面种类C.汇编源码 : 页面种类C.普通;

        /// <summary>
        /// 汇编源码页的落点:`<项目目录>\<名字>.hba`。
        /// 名字为空就给个兜底名 `汇编`(不该发生:项目名总有一个,但别让保存路径变成目录本身)。
        /// 名字已经带 `.hba` 就不再叠;带 `.hb` 就换成 `.hba`(主程序.hb → 主程序.hba)。
        /// 目录为空(没打开项目)就只给文件名 —— 这种页保存时会走"另存为"对话框,由用户挑地方。
        /// </summary>
        public static string 落点路径(string 项目目录, string? 名字)
        {
            string 净 = (名字 ?? "").Trim();
            if (净.Length == 0) 净 = "汇编";
            if (是汇编源码(净)) { /* 已经带 .hba:不叠后缀 */ }
            else if (净.EndsWith(".hb", StringComparison.OrdinalIgnoreCase)) 净 = 净[..^3] + 扩展名;
            else 净 += 扩展名;

            string 目录 = (项目目录 ?? "").Trim();
            return 目录.Length == 0 ? 净 : Path.Combine(目录, 净);
        }

        /// <summary> 页标题(页头那一格显示的字 = 文件名) </summary>
        public static string 页标题(string? 名字) => Path.GetFileName(落点路径("", 名字));

        /// <summary>
        /// C23(用户 2026-09-20:"展开后的文件名称应该是**被展开文件的名称** + `_ASM.hba`,而不是项目的名称"):
        /// 按被展开的那**一个文件**算落点 —— `<项目目录>\<被展开文件名>_ASM.hba`。
        ///
        /// 口径:
        /// <list type="bullet">
        /// <item>传全称(`…\库\字符串.hb`)也行:只取**文件名**,目录一律用**工程目录** ——
        ///       落点只有一条规则,不跟着被展开文件所在的子目录跑(与 C20 同一条路,保存时也好找);</item>
        /// <item>去掉 `.hb` 再叠 `_ASM`(`主程序.hb` → `主程序_ASM.hba`);已经带着 `_ASM` 的**不再叠**
        ///       (连点两次按钮不会变成 `主程序_ASM_ASM.hba`);</item>
        /// <item>本来就是一份汇编源码(`.hba`)就原样用它的名字(调用方那条路不会展开 `.hba`,这只是不硬猜)。</item>
        /// </list>
        /// </summary>
        public static string 落点路径按文件(string 项目目录, string? 被展开文件)
        {
            string 净 = (被展开文件 ?? "").Trim();
            if (净.Length == 0) return 落点路径(项目目录, null);
            string 名;
            try { 名 = Path.GetFileName(净); } catch { 名 = 净; }
            if (是汇编源码(名)) return 落点路径(项目目录, 名);      // 已经是 .hba:不要再叠一层
            if (名.EndsWith(".hb", StringComparison.OrdinalIgnoreCase)) 名 = 名[..^3];
            if (!名.EndsWith(展开标记, StringComparison.OrdinalIgnoreCase)) 名 += 展开标记;
            return 落点路径(项目目录, 名);
        }

        /// <summary>
        /// 这一页该复用哪一页:按**全称**在现有页里找,命中的下标(找不到给 -1)。
        /// 比之前先归一(能取全路径就取全路径),免得"同一个文件两种写法"被当成两页;
        /// 取不到全路径(路径非法)就按原串比 —— 宁可比不出来,别在开页这条路上抛异常。
        /// </summary>
        public static int 找复用页(IReadOnlyList<string?> 全称表, string 目标路径)
        {
            if (全称表 == null || 全称表.Count == 0) return -1;
            string 目标 = 归一(目标路径);
            for (int i = 0; i < 全称表.Count; i++)
            {
                string? 一个 = 全称表[i];
                if (string.IsNullOrWhiteSpace(一个)) continue;
                if (string.Equals(归一(一个!), 目标, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        /// <summary>
        /// 工程级展开按 `// 来自文件K第 N 行` 分给各文件之后,按顺序拼回来 —— 结果应当与原展开文本**逐字节相同**
        /// (每行只归一个段、顺序不变;`执行行映射C.扫工程展开` 就是这么切的)。
        /// 存 `.hba` 前用它;自检拿真展开文本往返比一次,防"以后改分段把顺序弄乱"。
        /// </summary>
        public static string 拼全工程汇编(IEnumerable<string?> 段表)
        {
            if (段表 == null) return "";
            return string.Join("\n", 段表.Where(x => x != null));
        }

        /// <summary>
        /// 存出来的 `.hba` 开头那段说明(用户可见文案,书面语)。
        /// 写清楚"这是编出来的汇编、默认不参与编译、`// 来自…` 标记怎么读" ——
        /// 免得以后有人把它当成源码改、或者以为它已经在参与编译。行首是 `//`,汇编器当注释,不占字节。
        /// 标记写法**没有方括号**(照 `源行标记C` 的唯一一份解析):`// 来自文件2第 3 行`。
        ///
        /// `来源` 说明这一页是谁生成的:C20 自动开的那页 = `编译(启动项目)`;
        /// C23 工具栏按钮开的那页 = `展开到汇编`。名字按**被展开的那个文件**给(不是项目名)。
        /// </summary>
        public static string 页头(string 名字, string 来源 = "编译(启动项目)") =>
            "// HB 汇编源码(.hba)—— 由「" + (来源 ?? "编译(启动项目)") + "」在【" + (名字 ?? "") + "】上生成。\n" +
            "// 它是**展开出来的汇编**,默认**不参与编译**;想拿它优化热路径,就在编辑器里勾「运行 → 汇编源码参与编译」" +
            "(C22:勾上会写进工程文件的 `汇编` 表,那一份接在源码展开结果之后一起汇编,标签与源码全局共用)。\n" +
            "// `// 来自文件K第 N 行` 是源行标记:`文件K` 对应编译时文件表里的第 K 个文件;`// 编译器生成` 那段没有源码行。";

        private static string 归一(string 路径)
        {
            string 净 = (路径 ?? "").Trim();
            try { return Path.GetFullPath(净); }
            catch { return 净; }
        }
    }
}
