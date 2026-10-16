namespace hehshihbeen_compiler
{
    /// <summary> 光标旁边那一对括号在哪(文档里的起止下标)—— 编辑器拿它画方框</summary>
    public readonly record struct 括号对C(int 左起, int 右起);

    /// <summary>
    /// **括号配对**(P37,2026-10-07;用户排的七件里的第 4 件的一半)。
    ///
    /// 口径(纯函数,自检直接喂文本):
    ///   · 光标**贴着**某个括号的时候才找:光标左边那个字符,或者光标右边那个字符
    ///     (`如果 (条件) |` 光标在 `)` 后面也算 —— 与常见编辑器一个手感);
    ///   · `() [] {}` 三对互不串门:扫的时候只看**同一种**括号,遇到另一种就跳过
    ///     (不然 `f([1, 2])` 里数错层);
    ///   · **注释与字符串里的括号不算**(`输出行("(")` 不该被当成一个开括号)——
    ///     遮蔽口径复用全仓唯一那份 `代码解析器.遮蔽区间`(注释 ∪ 字面量 → 空格,下标不变);
    ///   · 找不到配对(括号没写完 / 写错了)返回 null,**不猜、不硬指一个地方**。
    /// </summary>
    public static class 括号配对C
    {
        private static readonly (char 开, char 闭)[] 三对 =
        [
            ('(', ')'), ('[', ']'), ('{', '}'),
        ];

        /// <summary>
        /// 光标在 `位置`(文档下标,0 ~ 长度)时,该高亮哪一对括号;没有给 null。
        /// 先看光标**左边**那个字符(刚敲完右括号时最常见),再看**右边**那个(光标停在开括号前)。
        /// </summary>
        public static 括号对C? 找(string? 文本, int 位置)
        {
            string 文 = 文本 ?? "";
            if (文.Length == 0) return null;
            位置 = Math.Clamp(位置, 0, 文.Length);
            string 干净;
            try { 干净 = 代码解析器.遮蔽区间(文); }
            catch { 干净 = 文; }                       // 遮蔽失败(理论上不会)就按原文本找,别整个功能不干活

            // ① 光标左边那个字符(刚刚敲下去的那一个)
            if (位置 - 1 >= 0 && 位置 - 1 < 干净.Length && 找一边(干净, 位置 - 1) is { } 左括号) return 左括号;
            // ② 光标右边那个字符(光标停在括号前面)
            if (位置 < 干净.Length && 找一边(干净, 位置) is { } 右括号) return 右括号;
            return null;
        }

        /// <summary> 把 `下标` 那个字符当成括号找它的另一半;它不是括号 / 找不到给 null </summary>
        public static 括号对C? 找一边(string 干净文本, int 下标)
        {
            if (下标 < 0 || 下标 >= 干净文本.Length) return null;
            char 这里 = 干净文本[下标];
            foreach (var (开, 闭) in 三对)
            {
                if (这里 == 开)
                {
                    int 层 = 0;
                    for (int i = 下标; i < 干净文本.Length; i++)
                    {
                        if (干净文本[i] == 开) 层++;
                        else if (干净文本[i] == 闭) { 层--; if (层 == 0) return new 括号对C(下标, i); }
                    }
                    return null;                   // 开括号没闭上
                }
                if (这里 == 闭)
                {
                    int 层 = 0;
                    for (int i = 下标; i >= 0; i--)
                    {
                        if (干净文本[i] == 闭) 层++;
                        else if (干净文本[i] == 开) { 层--; if (层 == 0) return new 括号对C(i, 下标); }
                    }
                    return null;                   // 闭括号前面没有开
                }
            }
            return null;
        }
    }

    /// <summary> 签名提示要显示的那几样(引擎侧纯数据;界面只负责画)</summary>
    public sealed class 签名提示C
    {
        /// <summary> 函数名(带不带模块前缀看解析出什么)</summary>
        public string 名字 { get; set; } = "";

        /// <summary> 参数表原文(括号里那一段;没有参数给空串)</summary>
        public string 参数原文 { get; set; } = "";

        /// <summary> 返回类型词(界面拼成 `方法 名(参数) 返回 类型`;空 = 不显示)</summary>
        public string 返回类型 { get; set; } = "";

        /// <summary> 这个函数是干什么的(候选表里那句说明;空 = 不显示)</summary>
        public string 说明 { get; set; } = "";

        /// <summary> 光标现在在第几个参数上(0 基;参数表为空时是 0)</summary>
        public int 当前参数 { get; set; }

        /// <summary> 参数切成一段一段(`整数 x` / `字符串 名`),给"当前那个加粗"用</summary>
        public List<string> 参数段 { get; set; } = [];

        /// <summary>
        /// 这是**点方法**(`字符串.长度()` 那一族)吗?
        /// 方法写成 `变量.名字()` —— 光敲名字加 `(`(`长度(`)不是它的写法,所以提示里不画那对括号。
        /// </summary>
        public bool 是方法 { get; set; }

        /// <summary> 提示里要不要画那一对括号(方法不画)</summary>
        public bool 显示括号 => !是方法;

        /// <summary> 一行签名(给自检与"没渲染出来时"用)</summary>
        public string 一行 =>
            $"方法 {名字}({参数原文})" + (返回类型.Length > 0 ? $" 返回 {返回类型}" : "");

        public override string ToString() => 一行;
    }

    /// <summary>
    /// **签名提示**(P37;第 4 件的另一半):写着写着敲出 `(` 时,把那个函数的参数表显示在光标下面,
    /// 并标出"你现在在第几个参数上"。
    ///
    /// 这里只有**纯规则**(哪几个字算函数名 / 现在第几个参数 / 参数怎么切);
    /// "这个函数是谁"由界面那一侧查(补全词表 + 最近一次编译的名字表),查到了就填进 `签名提示C`。
    /// </summary>
    public static class 签名提示规则C
    {
        /// <summary>
        /// 光标在 `位置`(文档下标)时,它**正在写的那个调用**是什么:
        /// 往回找最近的 `(`(注释/字符串里的不算、括号要配对),返回那个 `(` 的下标 + 它前面那个名字。
        /// 没有(没在调用里 / 括号没配对)给 null。
        /// </summary>
        public static (string 名字, int 左括号位置)? 正在调用(string? 文本, int 位置)
        {
            string 文 = 文本 ?? "";
            if (文.Length == 0) return null;
            位置 = Math.Clamp(位置, 0, 文.Length);
            string 干净;
            try { 干净 = 代码解析器.遮蔽区间(文); }
            catch { 干净 = 文; }

            var 对 = 括号配对C.找(干净, 位置);
            int 左括号;
            if (对 is { } 有 && 好括号(干净, 有.左起) && 位置 > 有.左起 && 位置 <= 有.右起)
                左括号 = 有.左起;                       // 光标就在这一对圆括号里面
            else
            {
                // 往回找:深度归零之前遇到的那个 `(`(括号还没闭上时 `找` 会返回 null —— 那正是打字打到一半的样子)
                左括号 = -1;
                int 深 = 0;
                for (int i = 位置 - 1; i >= 0; i--)
                {
                    char c = 干净[i];
                    if (c == ')') 深++;
                    else if (c == '(')
                    {
                        if (深 == 0) { 左括号 = i; break; }
                        深--;
                    }
                    else if (c == ';' || c == '{' || c == '}') break;   // 出了这一句:不是调用
                }
                if (左括号 < 0) return null;
            }

            string? 名 = 前面的名字(干净, 左括号);
            return 名 == null ? null : (名, 左括号);
        }

        /// <summary> 那个下标上是不是圆括号(方括号 / 花括号不算"调用") </summary>
        private static bool 好括号(string 干净, int 下标) => 下标 >= 0 && 下标 < 干净.Length && 干净[下标] == '(';

        /// <summary> `(` 前面紧挨着的那个名字(`数学.求和` 整串都取;没有给 null) </summary>
        public static string? 前面的名字(string 干净文本, int 左括号)
        {
            int 止 = 左括号 - 1;
            while (止 >= 0 && (干净文本[止] == ' ' || 干净文本[止] == '\t')) 止--;
            if (止 < 0) return null;
            int 起 = 止;
            while (起 >= 0 && (高亮扫描C.是词字符(干净文本[起]) || 干净文本[起] == '.')) 起--;
            if (起 == 止) return null;
            string 名 = 干净文本[(起 + 1)..(止 + 1)];
            return 名.Length == 0 ? null : 名;
        }

        /// <summary>
        /// 光标现在在第几个参数上(0 基):从 `左括号` 往光标数**同一层**的逗号。
        /// `(` `[` `{` 三种都算一层(嵌套里的逗号不算 —— `f(取(a, b), 2)` 的光标在 `2` 上是第 2 个参数)。
        /// 注释/字符串里的逗号已经被遮蔽掉了,不会数错。
        /// </summary>
        public static int 第几个参数(string? 文本, int 左括号, int 光标)
        {
            string 文 = 文本 ?? "";
            左括号 = Math.Clamp(左括号, 0, 文.Length);
            光标 = Math.Clamp(光标, 左括号, 文.Length);
            string 干净;
            try { 干净 = 代码解析器.遮蔽区间(文); }
            catch { 干净 = 文; }

            int 序 = 0, 深 = 0;
            for (int i = 左括号 + 1; i < 光标 && i < 干净.Length; i++)
            {
                char c = 干净[i];
                if (c is '(' or '[' or '{') 深++;
                else if (c is ')' or ']' or '}') { if (深 == 0) break; 深--; }
                else if (c == ',' && 深 == 0) 序++;
            }
            return 序;
        }

        /// <summary>
        /// 参数表切成一段一段(顶层逗号切;带默认值 / 数组括号的原样留着)。
        /// 空表(没参数)返回空列表。
        /// </summary>
        public static List<string> 切参数(string? 参数原文)
        {
            var 结果 = new List<string>();
            string 文 = (参数原文 ?? "").Trim();
            if (文.Length == 0) return 结果;
            int 起 = 0, 深 = 0;
            for (int i = 0; i < 文.Length; i++)
            {
                char c = 文[i];
                if (c is '(' or '[' or '{') 深++;
                else if (c is ')' or ']' or '}') 深--;
                else if (c == ',' && 深 == 0)
                {
                    结果.Add(文[起..i].Trim());
                    起 = i + 1;
                }
            }
            string 尾 = 文[起..].Trim();
            if (尾.Length > 0) 结果.Add(尾);
            // 全空的段(连续逗号那种)不留
            结果.RemoveAll(x => x.Length == 0);
            return 结果;
        }

        /// <summary>
        /// 从**补全候选/名字表**那种"参数表字符串"里取参数名给提示用:
        /// 有名字就用名字(`整数 x` → `x`),只有类型就退回整段(`整数` → `整数`)。
        /// </summary>
        public static string 参数显示名(string 参数段)
        {
            string 段 = (参数段 ?? "").Trim();
            if (段.Length == 0) return "";
            // 去掉数组/列表那几笔尾巴,只留最后那个词
            int 止 = 段.Length;
            while (止 > 0 && (段[止 - 1] == ']' || 段[止 - 1] == '[')) 止--;
            string 头 = 段[..止].Trim();
            int 空 = 头.LastIndexOfAny([' ', '\t']);
            if (空 < 0) return 头;
            string 尾 = 头[(空 + 1)..].Trim();
            return 尾.Length == 0 ? 头 : 尾;
        }
    }
}
