using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **一条调用链上的一个链节**(H60-10;施工图 = `未做任务清单.md` §9 H53 的"施工图 · 步 3")。
    ///
    /// 用户 2026-09-26 点名的形状:「调用部分,`测试输出(第一个(data));` → `测试输出()` → `第一个()` → `data`」
    /// —— 调用**按表达式嵌套长**,不是只给"方法 A 调用了 B"。于是这一层的每个链节 = 表达式里的一个名字:
    /// 带括号的是**调用**(`是调用 = true`,它的实参继续往下长成 `子们`),最里层那个光名字是**叶**
    /// (`是调用 = false`,比如 `data` / `k` / `总数`)。
    ///
    /// **点号写法取末段当被调**(施工图 ②的那一句 `分数.添加(k)` ⇒ `添加` → `k`):
    /// `名` = 末段(`添加`),`收方` = 点号前面那一截(`分数`),`全名` = 原文里那一段(`分数.添加`)
    /// —— 于是 `声明线C.查(名, 行, 文件)` 查的是**方法自己**(内建方法 / 结构方法 / 成员方法都能对上),
    /// 而不是那个收方变量。
    ///
    /// **与 H60-9 拼起来才是用户要的两条线**:声明线(`声明` 那一格,虚线)是 H60-9 的 `声明索引C` 给的,
    /// 调用线(`子们`)是这一层长的 —— 画到界面上是 H60-11 / H60-14。
    /// </summary>
    public sealed class 调用链节C
    {
        /// <summary> 链节认到的名字(点号写法取**末段**:`分数.添加` ⇒ `添加`)</summary>
        public string 名 = "";

        /// <summary> 原文里那一段(带点:`分数.添加`;没有点就是它自己)</summary>
        public string 全名 = "";

        /// <summary> 点号前面那一截(`分数` / `数学`);没有点就是空串</summary>
        public string 收方 = "";

        /// <summary> 这一节出现在哪一行(1 基;一条语句里所有链节同号 —— 一行一条语句)</summary>
        public int 行;

        /// <summary> 带括号的 = 调用;最里层那个光名字 = 不是(H60-10 里 `data` 就是这一种)</summary>
        public bool 是调用;

        /// <summary>
        /// 声明线查出来的种类(用户方法 / 内建函数 / 语句 / 变量 / 参数 / 局部 / 常量…);
        /// 没拼声明索引时是空串,拼了但**认不出这个词**时也是空串(`声明 == null` = 不硬猜)。
        /// </summary>
        public string 种类 = "";

        /// <summary> 这个名字的声明处(与 H60-9 同一个 `声明索引C`;`null` = 他没来问 / 认不出这个词)</summary>
        public 声明处C? 声明;

        /// <summary> 实参里继续长出来的链节(按出现顺序;这才是"调用线") </summary>
        public List<调用链节C> 子们 { get; } = [];

        /// <summary>
        /// **H63-6**:同一个**框**里**别的行**也用到这个名字 —— 《同一框里同名只列一次》之后,
        /// 后面那些行号记在**第一次出现**的那一节这儿(界面把它写进提示:"同框里第 12/15 行也用到")。
        /// 注意去重粒度是 **"框 + 名字"**(不是全局):两个方法里各有一个 `i` 是**两份**声明,各列各的。
        /// </summary>
        public List<int> 也在行 { get; } = [];

        public override string ToString()
            => $"{(全名.Length > 0 ? 全名 : 名)}{(是调用 ? "()" : "")}{(种类.Length > 0 ? "[" + 种类 + "]" : "")}@{行}";
    }

    /// <summary>
    /// **调用线**(H60-10):从**一行语句的原文**里,按表达式嵌套拆出调用链
    /// (`测试输出(第一个(data));` ⇒ `测试输出` → `第一个` → `data`)。
    ///
    /// **纯函数**:喂文本即量 —— 不读盘、不吃编译结果、不碰界面、不动机器码(与 H60-8 / H60-9 同一个节奏)。
    ///
    /// 四条扫的规矩:
    ///   ① **括号配对**:只有名字后面紧跟 `(` / `（` 才算调用;实参按**顶层**逗号切开,再逐个往里钻
    ///      (`函数一(1) + 函数二(2)` 在同一档实参里 → 两个调用是**兄弟**,各长各的枝);
    ///   ② **避开字符串区**:引号(`"…"` 与 `'…'`、认 `\` 转义)里的 `(` `)` `,` 都不算,
    ///      `"函数一(函数二,函数三)"` 只能当一个实参;行尾 `//` 之后不算(与 `源码声明扫描C` 共用
    ///      `去行注释` 那一份实现 —— 同一套手势,只写一遍);
    ///   ③ **点号取末段**:`分数.添加(k)` 的被调是 `添加`,`收方` 记 `分数`(见 `调用链节C` 的注释);
    ///   ④ **数字字面量的尾巴不算名字**:`0x7FFFFFFF` 里紧跟在数字后面的那一段不认
    ///      (实测抓的:不排掉它,`float32` / `float64` / `界面` 那几个库里的十六进制常量会长出一堆假链节)。
    ///
    /// **输入契约**:`拆(原文, …)` 收的是**一条语句**的原文 —— `源码声明扫描C` 那棵树里
    /// `语句C.原文` 那一格(块头 / 声明行**不算语句**,树里已经把它们分到别处了,
    /// 所以 `循环 (…)` 这种块头别往这儿喂)。
    ///
    /// 用法(H60-11 / H60-13 / H60-14 都从这儿拿"这条语句调用了谁"):
    /// <code>
    /// var 索引 = 声明线C.建(源码声明扫描C.扫结构(文本, 文件));
    /// foreach (var 根 in 调用线C.拆("测试输出(第一个(data));", 124, 文件, 索引))
    ///     Console.WriteLine(调用线C.链文本(根));      // ⇒ 测试输出 → 第一个 → data
    /// var 全树 = 调用线C.拆全树(源码声明扫描C.扫结构(文本, 文件), 索引);
    /// </code>
    /// </summary>
    public static class 调用线C
    {
        /// <summary> 只拆形状(不拼声明线;`行` 不传就是 0)</summary>
        public static List<调用链节C> 拆(string? 原文, int 行 = 0) => 拆(原文, 行, "", null);

        /// <summary>
        /// 拆一条语句的调用链(**根 = 这条语句里直接调用的那些**,按出现顺序)。
        /// 传了 `索引` 就顺手把每个链节的 `声明` / `种类` 拼上(H60-9 那条**声明线**;虚线画它)。
        /// `文件` = 这条语句在哪一份文件里(声明索引挑"同文件那一个"要用)。
        ///
        /// **第281批**:新加 `只要调用`(缺省 真 = 老口径)。给 假 时**语句里的变量名也收**
        /// (`i = i + 1;` 会长出 `i` 两个链节)—— 导图那边要"变量也要有回连线"就靠它
        /// (`导图分区C` 建语句项时传 假)。实参那一层一直是 假(见 `扫` 的递归)。
        /// </summary>
        public static List<调用链节C> 拆(string? 原文, int 行, string 文件, 声明索引C? 索引, bool 只要调用 = true)
        {
            var 根 = new List<调用链节C>();
            string 净 = 源码声明扫描C.去行注释(原文 ?? "").Trim();
            if (净.Length == 0) return 根;
            扫(净, 行, 文件 ?? "", 索引, 只要调用, 收: 根);
            return 根;
        }

        /// <summary> 一串语句(一棵树里某一层现成的 `语句们`)→ 它们的调用链根(按顺序摊平)</summary>
        public static List<调用链节C> 拆语句们(IEnumerable<语句C>? 语句们, string 文件, 声明索引C? 索引 = null)
        {
            var 出 = new List<调用链节C>();
            if (语句们 == null) return 出;
            foreach (var 句 in 语句们)
                出.AddRange(拆(句.原文, 句.行, 文件 ?? "", 索引));
            return 出;
        }

        /// <summary>
        /// 一棵结构树里**所有语句**(顶层 / 顶层流程块里 / 方法体与流程块里 / 结构方法与容器成员方法里),
        /// 按行号排(同号按在树里出现的顺序 —— `OrderBy` 是稳定排序)。
        /// </summary>
        public static List<语句C> 全部语句(源码结构C 树)
        {
            var 出 = new List<语句C>();
            if (树 == null) return 出;
            收语句(树.顶层语句们, 出);
            收块语句(树.顶层流程块们, 出);
            收方法语句(树.方法们, 出);
            foreach (var 结 in 树.结构们) 收方法语句(结.方法们, 出);
            return 出.OrderBy(句 => 句.行).ToList();
        }

        /// <summary> 一棵树整棵拆一遍(方便全扫 / 计数;界面按"哪个框点开"取的话,直接 `拆语句们` 那个框自己的语句) </summary>
        public static List<调用链节C> 拆全树(源码结构C 树, 声明索引C? 索引 = null)
            => 拆语句们(全部语句(树), 树?.文件 ?? "", 索引);

        /// <summary> 递归走一遍(先自己、后子节,按顺序)—— 计数 / 全扫查行号用 </summary>
        public static IEnumerable<调用链节C> 展开(IEnumerable<调用链节C>? 链们)
        {
            if (链们 == null) yield break;
            foreach (var 节 in 链们)
            {
                yield return 节;
                foreach (var 子 in 展开(节.子们)) yield return 子;
            }
        }

        /// <summary> 这一串链节(含子节)一共几个 </summary>
        public static int 链节总数(IEnumerable<调用链节C>? 链们) => 展开(链们).Count();

        /// <summary>
        /// 一条链节给人看的形状:`测试输出 → 第一个 → data`。
        /// 一个节有多个子节(一个实参里的多个调用、或好几个实参)时用 `/` 隔开各枝、括起来。
        /// </summary>
        public static string 链文本(调用链节C? 节)
        {
            if (节 == null) return "";
            var sb = new StringBuilder(节.全名.Length > 0 ? 节.全名 : 节.名);
            if (节.子们.Count == 1) sb.Append(" → ").Append(链文本(节.子们[0]));
            else if (节.子们.Count > 1)
            {
                sb.Append(" → (");
                for (int i = 0; i < 节.子们.Count; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    sb.Append(链文本(节.子们[i]));
                }
                sb.Append(')');
            }
            return sb.ToString();
        }

        // ---------------- 往下都是"扫"这一层自己 ----------------


        /// <summary> 一个字面量(不是"名字"):`真` / `假` / 英文那对 —— 当实参时不长成链节 </summary>
        private static readonly HashSet<string> 字面量词 = new(StringComparer.OrdinalIgnoreCase) { "真", "假", "true", "false" };

        /// <summary>
        /// 扫一段表达式:`只要调用` 为真时**只把带括号的调用**记成链节(语句这一层要的就是"它调用了谁"),
        /// 为假时**光名字也记**(实参里那个 `data` / `k` 就是这么来的)。
        /// </summary>
        private static void 扫(string 段, int 行, string 文件, 声明索引C? 索引, bool 只要调用, List<调用链节C> 收)
        {
            int i = 0;
            while (i < 段.Length)
            {
                char c = 段[i];

                // 引号里整段跳过去:里面的 `(` `)` `,` 都不算
                if (c == '"' || c == '\'') { i = 跳过字面量(段, i); continue; }
                if (!是名字首(c)) { i++; continue; }

                int 起 = i;
                i = 读名字(段, i);

                // **数字字面量的尾巴不算名字**:`0x7FFFFFFF` 里那个 `x7FFFFFFF` 紧跟在数字后面 ——
                // 不排掉它,float32 / float64 / 界面 那几个库里的十六进制常量就会长出一堆假链节
                // (实测抓的:`x7FFFFFFF` / `x7FF00000` / `xFFB0B0B8` 一类)。
                if (起 > 0 && char.IsDigit(段[起 - 1])) continue;

                while (i < 段.Length - 1 && 段[i] == '.' && 是名字首(段[i + 1])) i = 读名字(段, i + 1);
                string 全名 = 段[起..i];

                int 探 = i;
                while (探 < 段.Length && (段[探] == ' ' || 段[探] == '\t')) 探++;

                if (探 < 段.Length && (段[探] == '(' || 段[探] == '（'))
                {
                    var 节 = 建节(全名, 行, true, 文件, 索引);
                    int 闭 = 配对的右括号(段, 探);
                    if (闭 > 探)
                    {
                        foreach (string 实参 in 拆顶层实参(段[(探 + 1)..闭]))
                            扫(实参, 行, 文件, 索引, 只要调用: false, 收: 节.子们);
                        i = 闭 + 1;
                    }
                    else i = 探 + 1;            // 括号没闭合:这一节照收,后面不再往里钻(不炸)
                    收.Add(节);
                }
                else if (!只要调用 && !字面量词.Contains(全名))
                {
                    收.Add(建节(全名, 行, false, 文件, 索引));
                }
            }
        }

        /// <summary> 按配对括号把 `"(" … ")"` 里那一段切成实参(顶层逗号,认中英两种逗号;引号里的不算) </summary>
        private static List<string> 拆顶层实参(string 里)
        {
            var 出 = new List<string>();
            int 深 = 0, 起 = 0;
            for (int i = 0; i < 里.Length; i++)
            {
                char c = 里[i];
                if (c == '"' || c == '\'') { i = 跳过字面量(里, i) - 1; continue; }
                if (c == '(' || c == '（') 深++;
                else if (c == ')' || c == '）') { if (深 > 0) 深--; }
                else if ((c == ',' || c == '，') && 深 == 0)
                {
                    加实参(出, 里[起..i]);
                    起 = i + 1;
                }
            }
            加实参(出, 里[起..]);
            return 出;
        }

        private static void 加实参(List<string> 出, string 段)
        {
            string 净 = 段.Trim();
            if (净.Length > 0) 出.Add(净);
        }

        /// <summary> 从 `开` 那个括号起找配对的右括号(引号里跳过);找不到给 -1(不抛) </summary>
        private static int 配对的右括号(string 段, int 开)
        {
            int 深 = 0;
            for (int i = 开; i < 段.Length; i++)
            {
                char c = 段[i];
                if (c == '"' || c == '\'') { i = 跳过字面量(段, i) - 1; continue; }
                if (c == '(' || c == '（') 深++;
                else if (c == ')' || c == '）')
                {
                    深--;
                    if (深 == 0) return i;
                }
            }
            return -1;
        }

        /// <summary> 从引号那一位起跳到右引号之后(认 `\` 转义;没闭合就吃到行尾) </summary>
        private static int 跳过字面量(string 段, int 起)
        {
            char 引 = 段[起];
            for (int i = 起 + 1; i < 段.Length; i++)
            {
                if (段[i] == '\\') { i++; continue; }
                if (段[i] == 引) return i + 1;
            }
            return 段.Length;
        }

        private static bool 是名字首(char c) => c == '_' || char.IsLetter(c);

        private static bool 是名字身(char c) => c == '_' || char.IsLetterOrDigit(c);

        private static int 读名字(string 段, int 起)
        {
            int i = 起;
            while (i < 段.Length && 是名字身(段[i])) i++;
            return i;
        }

        /// <summary> 起一个链节(点号取末段当 `名`、前面那一截当 `收方`),顺手拼上声明线(给了索引的话) </summary>
        private static 调用链节C 建节(string 全名段, int 行, bool 是调用, string 文件, 声明索引C? 索引)
        {
            string 名 = 源码声明扫描C.尾名(全名段);
            string 收方 = 名.Length < 全名段.Length ? 全名段[..(全名段.Length - 名.Length - 1)] : "";
            var 节 = new 调用链节C { 名 = 名, 全名 = 全名段, 收方 = 收方, 行 = 行, 是调用 = 是调用 };
            if (索引 != null)
            {
                节.声明 = 索引.查(名, 行, 文件);
                节.种类 = 节.声明?.种类 ?? "";
            }
            return 节;
        }

        // ---------------- 把树里的语句收出来(供 `全部语句`) ----------------

        private static void 收语句(List<语句C> 语句们, List<语句C> 出) => 出.AddRange(语句们);

        private static void 收块语句(List<流程块节点C> 块们, List<语句C> 出)
        {
            foreach (var 块 in 块们)
            {
                出.AddRange(块.里层语句们);
                收块语句(块.子块们, 出);
            }
        }

        private static void 收方法语句(List<方法节点C> 方法们, List<语句C> 出)
        {
            foreach (var 方 in 方法们)
            {
                出.AddRange(方.语句们);
                收块语句(方.流程块们, 出);
                收方法语句(方.方法们, 出);
            }
        }
    }
}
