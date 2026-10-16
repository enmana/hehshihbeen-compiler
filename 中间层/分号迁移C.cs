using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 给老代码补 `;`(2026-09 起 HB 语言语句必须以 `;` 结尾)。
    ///
    /// 只做两件事:① 该收尾的语句**在代码区末尾插一个 `;`**(注释之前),别的字符一个不动 ——
    /// 所以格式、空格、注释都原样保留;② 已经有的不动。
    /// 块头(`如果 (…)` / `函数 …`,后面跟 `{`)、`{` `}` 行、内联汇编行、标签行、`包含` 行**不补** ——
    /// 判据和编译器的合句规则是同一套(见 `中间层展开器.规范化文本`),不会两边打架。
    ///
    /// 命令行:`hbcc --fix-semicolon 文件.hb`(老工程/老示例迁移用)。
    /// `.cs` 文件也认:只处理里面的**字符串字面量**(自检/帮助文本里内嵌的 HB 源码),源码本身不动。
    /// </summary>
    public static class 分号迁移C
    {
        /// <summary> 补一个 .hb 源文件(就地改写);返回补了几处 </summary>
        public static int 补文件(string 路径)
        {
            string 原文 = File.ReadAllText(路径);
            string 新文 = 补文本(原文);
            if (新文 == 原文) return 0;
            File.WriteAllText(路径, 新文);
            return Math.Max(0, 新文.Count(c => c == ';') - 原文.Count(c => c == ';'));
        }

        // ================= 纯文本:给 HB 源码补 `;` =================

        /// <summary> 给一段 HB 源码补 `;`(字符串字面量也一样处理,`.cs` 迁移走这里)</summary>
        public static string 补文本(string 源代码)
        {
            string 归一 = 源代码.Replace("\r\n", "\n");
            string[] 行表 = 归一.Split('\n');
            // 判据一律看"遮蔽注释"之后的文本(注释里的花括号/分号/文字都不算),
            // 插入位置在**原行**上算 —— 两边长度一一对应,所以注释、空格一个字符都不会动。
            var 遮蔽行表 = new string[行表.Length];
            for (int i = 0; i < 行表.Length; i++) 遮蔽行表[i] = 代码解析器.遮蔽注释(行表[i]);
            var 片段表 = 中间层展开器.切片段(遮蔽行表);

            // 每行要插入的位置(行下标 → 插入下标);同一行可能插多个(一行里 `{ 返回 x }` 这种)
            var 插入表 = new Dictionary<int, List<int>>();
            string 缓冲 = "";                             // 正在续行的语句(上一片以续行符收尾)

            void 记插(int 行号, int 位)
            {
                if (!插入表.TryGetValue(行号, out var 列)) 插入表[行号] = 列 = [];
                列.Add(位);
            }

            for (int k = 0; k < 片段表.Count; k++)
            {
                var (片段, 起始, 行, 花括号) = 片段表[k];
                if (花括号) { 缓冲 = ""; continue; }       // 撞上 { }:拼接出来的是块头 → 不补
                string 内容 = 片段.Trim();
                if (内容.Length == 0) continue;

                // 这一片在行里的"代码末尾"位置(片段末尾去掉空白)
                int 片尾 = 起始 + 片段.TrimEnd().Length;

                if (中间层展开器.以分号结尾(内容)) { 缓冲 = ""; continue; }        // 本来就有 `;`
                if (中间层展开器.下一个是花括号(片段表, k)) { 缓冲 = ""; continue; }  // 块头
                if (缓冲.Length == 0 && (中间层展开器.是内联汇编行(内容) || 中间层展开器.是标签行(内容)
                    || 中间层展开器.是包含行(内容))) { 缓冲 = ""; continue; }
                if (中间层展开器.以续行符结尾(内容)) { 缓冲 = 内容; continue; }     // 还没完,看下一片

                // 到这儿:这是一句的**结尾**,但没写 `;` → 补在它末尾(多行语句也一样:补在最后一行)
                记插(行, 片尾);
                缓冲 = "";
            }

            // 逐行插入(同一行多个位置从后往前插,免得前面的插入把后面的下标顶偏)
            var 输出 = new StringBuilder();
            for (int i = 0; i < 行表.Length; i++)
            {
                string 行 = 行表[i];
                if (插入表.TryGetValue(i + 1, out var 位置列))
                    foreach (int 位 in 位置列.OrderByDescending(x => x))
                        行 = 行[..位] + ";" + 行[位..];
                输出.Append(行);
                if (i + 1 < 行表.Length) 输出.Append('\n');
            }
            return 输出.ToString();
        }

        // ================= .cs:只动字符串字面量里内嵌的 HB 源码 =================

        /// <summary>
        /// 补一个 .cs 文件(就地改写):找出所有**普通字符串字面量**,把里面像 HB 源码的内容补上 `;`
        /// (自检/帮助文本/说明表里内嵌的示例源码都在字符串里)。源码本体、逐字字符串(@"…")不动。
        /// </summary>
        public static int 补CSharp文件(string 路径)
        {
            string 原文 = File.ReadAllText(路径);
            string 新文 = 补CSharp文本(原文);
            if (新文 == 原文) return 0;
            File.WriteAllText(路径, 新文);
            return 新文.Count(c => c == ';') - 原文.Count(c => c == ';');
        }

        private static readonly Regex 普通字符串正则 = new(@"(?<![@$])""(?<内>(?:\\.|[^""\\\r\n])*)""", RegexOptions.Compiled);

        /// <summary> 生字符串字面量 `"""…"""`(C# 11):里面**不用转义**,换行就是换行 </summary>
        private static readonly Regex 生字符串正则 = new("\"{3}(?<内>.*?)\"{3}", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary> 把 .cs 里普通字符串字面量中的 HB 源码补上 `;` </summary>
        public static string 补CSharp文本(string 文本)
        {
            文本 = 补插值字符串(文本);
            文本 = 生字符串正则.Replace(文本, m =>
            {
                string 内 = m.Groups["内"].Value;
                if (!看起来像源码(内)) return m.Value;
                string 补好 = 补文本(内);
                return 补好 == 内 ? m.Value : "\"\"\"" + 补好 + "\"\"\"";
            });
            return 普通字符串正则.Replace(文本, m =>
            {
                string 内 = m.Groups["内"].Value;
                string 解码 = 反转义(内);          // 判据看**解码后**的真文本(不然 `\n` 只是两个字符,看不出是源码)
                if (!看起来像源码(解码)) return m.Value;
                string 补好 = 补文本(解码);
                if (补好 == 解码) return m.Value;
                return "\"" + 转义(补好) + "\"";
            });
        }

        /// <summary>
        /// 插值字符串 `$"…{C#代码}…"`:插值段里的 `{…}` 是 **C# 代码**,不能当 HB 的花括号 ——
        /// 先把它换成**等长占位符**再补分号,补完按占位符换回来。
        /// 插值段里的引号(如 `{引号(Path.Combine(目录, "x.hb"))}`)靠**花括号深度**判,不当字符串结束。
        ///
        /// 为什么用占位符换回、而不是"按下标还原"(2026-10 修):补分号是**插入**字符,插在某段之前的 `;`
        /// 会把这段的下标顶偏 —— 实测 `测试输出({引号(…)})` 会被还原成 `测试输出{引号(…)}x);`
        /// (少了个括号、多了个 x)。占位符是**唯一**的,换回来跟下标无关,插多少个 `;` 都不怕。
        /// (这个 bug 是批 5 给这个工具补黄金文件时抓出来的 —— 它以前一条断言都没有。)
        /// </summary>
        private static string 补插值字符串(string 文本)
        {
            var sb = new StringBuilder(文本.Length);
            int i = 0;
            while (i < 文本.Length)
            {
                bool 是插值 = 文本[i] == '$' && i + 1 < 文本.Length && 文本[i + 1] == '"'
                    && (i == 0 || 文本[i - 1] != '@');
                if (!是插值) { sb.Append(文本[i]); i++; continue; }

                int 起 = i + 2, j = 起, 深 = 0;
                bool 收尾 = false;
                while (j < 文本.Length)
                {
                    char c = 文本[j];
                    if (c == '\\') { j += 2; continue; }
                    if (c == '\n') break;
                    if (c == '{') { 深++; j++; continue; }
                    if (c == '}') { 深--; j++; continue; }
                    if (c == '"' && 深 == 0) { 收尾 = true; break; }
                    j++;
                }
                if (!收尾) { sb.Append(文本[i]); i++; continue; }

                string 内 = 文本[起..j];
                string 解码 = 反转义(内);
                if (!看起来像源码(解码)) { sb.Append(文本[i..(j + 1)]); i = j + 1; continue; }

                // 每个插值段换成一个**唯一占位符**(用私用区字符包住序号,绝不会跟正文撞)
                var 原段 = new List<(string 占位, string 原)>();
                int 序号 = 0;
                string 遮蔽 = 插值段正则.Replace(解码, m =>
                {
                    string 占位 = $"\uE000{序号++}\uE001";
                    原段.Add((占位, m.Value));
                    return 占位;
                });
                string 补好 = 补文本(遮蔽);
                if (补好 == 遮蔽) { sb.Append(文本[i..(j + 1)]); i = j + 1; continue; }
                foreach (var (占位, 原) in 原段) 补好 = 补好.Replace(占位, 原);
                sb.Append("$\"").Append(转义(补好)).Append('"');
                i = j + 1;
            }
            return sb.ToString();
        }

        private static readonly Regex 插值段正则 = new(@"\{[^{}]*\}", RegexOptions.Compiled);

        /// <summary>
        /// 只在"像 HB 源码"的字符串上动手。判据刻意保守:
        ///   · 单个词(关键字名/类型名/表键)不算源码 —— 否则会把关键字表改成 `"如果;"`
        ///   · 至少要**多行**(含换行)或者**带块**(有 `{`);
        ///   · 而且得真有"像代码的一行"(首词是类型/关键字/指令)—— 期望输出 `"11\n44"` 这种不算
        /// </summary>
        private static bool 看起来像源码(string 内)
        {
            string t = 内.Trim();
            if (t.Length == 0) return false;
            if (!t.Contains('\n') && !t.Contains('{')) return false;
            foreach (string 行 in t.Split('\n'))
            {
                string 一 = 行.Trim();
                if (一.Length is 0 or 1) continue;
                if (一.StartsWith("//", StringComparison.Ordinal)) continue;
                string 首词 = 代码解析器.取首词(一);
                if (首词.Length == 0) continue;
                if (类型C.解析(首词) != null || 中间层指令C.到英文关键字(首词) != null
                    || 指令C.取指令码表(首词) != null) return true;
                // 一行"看着就是语句"的也算:赋值(`p.x = p.x + dx`)/ 调用(`移动(p, 3)`)
                if (一.Contains('=') && !一.StartsWith("//", StringComparison.Ordinal)) return true;
                if (一.EndsWith(')') || 一.EndsWith("++") || 一.EndsWith("--")) return true;
            }
            return false;
        }

        /// <summary> 把 C# 字符串字面量的转义还原成真字符(只认常见那几个)</summary>
        private static string 反转义(string 内)
        {
            var sb = new StringBuilder(内.Length);
            for (int i = 0; i < 内.Length; i++)
            {
                if (内[i] != '\\' || i + 1 >= 内.Length) { sb.Append(内[i]); continue; }
                char 下 = 内[++i];
                sb.Append(下 switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '0' => '\0',
                    '\\' => '\\',
                    '"' => '"',
                    '\'' => '\'',
                    _ => 下,       // \u0041 之类原样留着(内嵌源码里用不到)
                });
            }
            return sb.ToString();
        }

        /// <summary> 反过来:真字符变回 C# 字符串字面量里的写法</summary>
        private static string 转义(string 文本) => 文本
            .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }
}
