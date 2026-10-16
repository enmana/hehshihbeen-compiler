namespace hehshihbeen_compiler
{
    /// <summary>
    /// **转义序列的唯一事实源**(R2-5,2026-09-21)。
    ///
    /// 以前这一处是**三份手抄的表、两种能力**:字符字面量在 `替换字面量` 里认 `n r t 0 \ ' "`,
    /// 在 `解字符字面量` 里多认 `b f v a`,字符串在 `解码字符串` 里又抄一份;
    /// 而字符串那条报错文案**向用户承诺 `\b \f \v \a` 都能用** —— 实测 `字符 c = '\b';` 编不过
    /// (报"表达式里有无法识别的字符 '''",用户根本看不出是转义不认识)。
    ///
    /// 现在四处全吃这一张表:字符字面量两处(`中间层C.错误报告` 的 `替换字面量`、
    /// `中间层C.拼接到与格式化` 的 `解字符字面量`)、字符串一处(`中间层C.输入输出` 的 `解码字符串`)、
    /// 断点条件一处(`断点条件C.试解常量`);**报错文案里那份"能用的有…"也从表生成**,不许再手写第二份。
    /// </summary>
    public static class 转义表C
    {
        // 顺序就是报错文案里露出来的顺序(表 → 文案,单向生成,所以永远不会漂移)
        private static readonly (char 记号, int 码值)[] 表 =
        [
            ('n', 10), ('r', 13), ('t', 9), ('b', 8), ('f', 12), ('v', 11), ('a', 7),   // 换行/回车/制表/退格/换页/竖制表/响铃
            ('0', 0), ('\\', 92), ('"', 34), ('\'', 39),
        ];

        /// <summary> 全部单字符转义(记号 → 码值)。`\xHH` / `\uXXXX` 不带码值,单独走 `解字符内文`。 </summary>
        public static IReadOnlyList<(char 记号, int 码值)> 全部 => 表;

        /// <summary> 单个转义记号 → 码值;不认识返回 false </summary>
        public static bool 试解(char 记号, out int 码值)
        {
            foreach (var (表记号, 表码值) in 表)
                if (表记号 == 记号) { 码值 = 表码值; return true; }
            码值 = 0;
            return false;
        }

        /// <summary> 报错文案里那句"能用的有…"(**从表生成**;`\xHH`/`\uXXXX` 是另一套写法,缀在后面) </summary>
        public static string 可用转义说明 { get; } =
            string.Join(" ", 表.Select(x => "\\" + x.记号)) + " \\xHH \\uXXXX";

        /// <summary> 认不出的转义:一句人话 + 把**表里真实支持的**全列出来(不再手抄) </summary>
        public static string 未知转义提示(char 记号) => $"不认识的转义 \\{记号}:能用的有 {可用转义说明}";

        /// <summary>
        /// 字符字面量的**内文**(已去掉两头那个 `'`)→ 码值;解不出给 null 并在 `说明` 里说清原因。
        /// 星平面字符(`'😀'`,UTF-16 里是代理对)按码点收。
        /// </summary>
        public static int? 解字符内文(string? 内, out string 说明)
        {
            说明 = "";
            内 ??= "";
            if (内.Length == 0) { 说明 = "`''` 里要写一个字符"; return null; }

            if (内[0] != '\\')
            {
                if (内.Length == 2 && char.IsSurrogatePair(内[0], 内[1])) return char.ConvertToUtf32(内[0], 内[1]);
                if (内.Length != 1) { 说明 = $"这个字符字面量里有 {内.Length} 个字符 —— 只能写一个"; return null; }
                return 内[0];
            }

            if (内.Length < 2) { 说明 = "转义写了一半(`\\` 后面要跟一个字符)"; return null; }
            char e = 内[1];
            if (内.Length == 2 && 试解(e, out int 码值)) return 码值;

            if (e is 'x' or 'X')
            {
                int 长 = 0;
                while (长 < 2 && 2 + 长 < 内.Length && Uri.IsHexDigit(内[2 + 长])) 长++;
                if (长 > 0 && 2 + 长 == 内.Length) return Convert.ToInt32(内.Substring(2, 长), 16);
                说明 = "`\\x` 后面要跟 1~2 位十六进制(例如 `'\\x41'` 就是 `'A'`)";
                return null;
            }

            if (e is 'u' or 'U')
            {
                int? 码 = 解u转义(内, 1, out int 尾, out string u说明);
                if (码 is not { } 值) { 说明 = u说明; return null; }
                if (尾 != 内.Length) { 说明 = $"`\\u` 转义后面多写了 {内.Length - 尾} 个字符 —— 字符字面量只能写一个字符"; return null; }
                return 值;
            }

            if (内.Length > 2) { 说明 = $"`\\{e}` 后面多写了 {内.Length - 2} 个字符 —— 字符字面量只能写一个字符"; return null; }
            说明 = 未知转义提示(e);
            return null;
        }

        /// <summary>
        /// `\uXXXX`(以及紧跟其后的 `\uYYYY` 代理对)→ 一个**文字**(Unicode 码点)。
        ///
        /// `内文` 是整段(已去掉两头的引号),`起` 指向 `u` / `U` 那一位。
        /// 成功:`尾` = 这个转义之后的下标(代理对吃两个转义),返回码点。
        /// 失败:返回 null,`说明` 里说清为什么。
        ///
        /// **T17(2026-09-27 评测整改):两套解码器收成这一处。** 以前字符字面量(`解字符内文`)与
        /// 字符串(`解码字符串`)各写一份 `\u` 解析,结论对不上:实测 `'\uD83D'` 收下 55357(孤立代理码),
        /// 而它落盘/输出会产出**不合法的 UTF-8**;`"\uD83D\uDE00"` 又只在字符串那一侧才拼成一个字。
        /// 现在两边同吃这一份:`\uD83D\uDE00` 是一个文字(😀),**单独一个代理码报错并给改法**。
        /// </summary>
        public static int? 解u转义(string 内文, int 起, out int 尾, out string 说明)
        {
            尾 = 起;
            说明 = "";
            if (起 < 0 || 内文.Length < 起 + 5 || !内文.Substring(起 + 1, 4).All(Uri.IsHexDigit))
            {
                说明 = "`\\u` 后面要跟 4 位十六进制(例如 `\\u4e2d` 就是 `'中'`)";
                return null;
            }
            int 码 = Convert.ToInt32(内文.Substring(起 + 1, 4), 16);
            string 这一个 = $"\\u{内文.Substring(起 + 1, 4)}";
            尾 = 起 + 5;
            if (char.IsHighSurrogate((char)码))
            {
                if (内文.Length >= 起 + 11 && 内文[起 + 5] == '\\' && 内文[起 + 6] is 'u' or 'U'
                    && 内文.Substring(起 + 7, 4).All(Uri.IsHexDigit))
                {
                    int 低 = Convert.ToInt32(内文.Substring(起 + 7, 4), 16);
                    if (char.IsLowSurrogate((char)低))
                    {
                        尾 = 起 + 11;
                        return (码 - 0xD800) * 0x400 + (低 - 0xDC00) + 0x10000;
                    }
                }
                说明 = $"孤立代理码:{这一个} 只是**一个文字的一半**(高代理),单写它不是一个文字 —— " +
                    $"要么跟它的另一半写在一起(例如 `\\uD83D\\uDE00` 才是 😀),要么直接写那个字本身";
                return null;
            }
            if (char.IsLowSurrogate((char)码))
            {
                说明 = $"孤立代理码:{这一个} 只是**一个文字的一半**(低代理),单写它不是一个文字 —— " +
                    $"要么跟它的另一半写在一起(例如 `\\uD83D\\uDE00` 才是 😀),要么直接写那个字本身";
                return null;
            }
            return 码;
        }
    }
}
