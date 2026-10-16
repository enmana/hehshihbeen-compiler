using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    #region 词元定义:词元类型枚举与词元数据类
    // 词元类型枚举
    internal enum 词元类型
    {
        未知,
        关键字,
        标识符,
        数字,
        十六进制数,
        寄存器,
        字符串,
        注释,
        空白,
        冒号,
        左方括号,
        右方括号,
        逗号,
        运算符,
        其他符号
    }

    // 词元类
    internal class 词元(词元类型 类型, string 值, int 起始位置, int 行号, int 列号)
    {
        public 词元类型 类型 { get; } = 类型;
        public string 值 { get; } = 值;
        public int 起始位置 { get; } = 起始位置;
        public int 长度 { get; } = 值?.Length ?? 0;
        public int 行号 { get; } = 行号;
        public int 列号 { get; } = 列号;

        public override string ToString() => $"{类型}({行号},{列号}): '{值}'";
    }
    #endregion

    #region 分词规则模型与基于正则的实现
    // 规则匹配结果
    internal class 匹配结果(bool 成功, bool 跳过, string? 值, 词元类型 类型)
    {
        public bool 成功 { get; } = 成功;
        public bool 跳过 { get; } = 跳过;
        public string? 值 { get; } = 值;
        public 词元类型 类型 { get; } = 类型;

        public static 匹配结果 失败 => new(false, false, null, 词元类型.未知);
        public static 匹配结果 成功匹配(string 值, 词元类型 类型, bool 跳过 = false) => new(true, 跳过, 值, 类型);
    }

    // 分词规则接口
    internal interface I分词规则
    {
        匹配结果 尝试匹配(string 输入, int 位置);
    }

    // 基于正则的规则
    internal class 正则规则(string 模式, 词元类型 类型, bool 是否跳过 = false) : I分词规则
    {
        private readonly Regex _正则 = new(模式, RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly 词元类型 _类型 = 类型;
        private readonly bool _是否跳过 = 是否跳过;

        public 匹配结果 尝试匹配(string 输入, int 位置)
        {
            var m = _正则.Match(输入, 位置);
            if (m.Success && m.Index == 位置)
            {
                return 匹配结果.成功匹配(m.Value, _类型, _是否跳过);
            }
            return 匹配结果.失败;
        }
    }
    #endregion

    #region 通用分词器:规则驱动的扫描引擎
    // 主分词器
    internal class 分词器
    {
        private readonly List<I分词规则> _规则 = [];

        public 分词器 添加规则(I分词规则 规则)
        {
            _规则.Add(规则);
            return this;
        }

        public 分词器 添加规则(string 模式, 词元类型 类型, bool 跳过 = false)
        {
            return 添加规则(new 正则规则(模式, 类型, 跳过));
        }

        public 分词器 添加跳过规则(string 模式, 词元类型 类型 = 词元类型.空白)
        {
            return 添加规则(模式, 类型, true);
        }

        public 分词器 添加关键字(IEnumerable<string> 列表)
        {
            var 排序 = 列表.OrderByDescending(s => s.Length).Select(Regex.Escape);
            var 模式 = $"(?<![_\\p{{L}}\\p{{N}}])({string.Join("|", 排序)})(?![_\\p{{L}}\\p{{N}}])";
            return 添加规则(模式, 词元类型.关键字);
        }

        public IEnumerable<词元> 分词(string 输入)
        {
            int pos = 0; int 行 = 1; int 列 = 1;
            while (pos < 输入.Length)
            {
                bool matched = false;
                int startPos = pos; int startLine = 行; int startCol = 列;
                foreach (var r in _规则)
                {
                    var res = r.尝试匹配(输入, pos);
                    if (res.成功)
                    {
                        matched = true;
                        string text = res.值 ?? string.Empty;
                        // update line/col
                        foreach (char c in text)
                        {
                            if (c == '\n') { 行++; 列 = 1; }
                            else if (c == '\r') { }
                            else 列++;
                        }

                        if (!res.跳过)
                        {
                            yield return new 词元(res.类型, text, startPos, startLine, startCol);
                        }
                        pos += text.Length;
                        break;
                    }
                }
                if (!matched)
                {
                    // treat single char as unknown and advance
                    char c = 输入[pos];
                    if (c == '\n') { 行++; 列 = 1; } else if (c == '\r') { } else 列++;
                    yield return new 词元(词元类型.未知, c.ToString(), pos, 行, 列 - 1);
                    pos++;
                }
            }
        }
    }
    #endregion

    // 包装类：提供兼容的 分词(string) 接口供汇编器使用
    public static class 分词器C
    {
        #region 分词器C:公开入口与静态初始化
        private static readonly 分词器 _tokenizer;

        static 分词器C()
        {
            var t = new 分词器();
            // 跳过:注释(// 到行尾)和空白
            t.添加跳过规则(@"(?://[^\r\n]*)|(?s:/\*.*?\*/)", 词元类型.注释);
            t.添加跳过规则(@"\s+", 词元类型.空白);

            t.添加关键字(指令C.取全部指令名());
            // 数字与寄存器等
            t.添加规则(@"0x[0-9A-Fa-f]+", 词元类型.十六进制数);
            t.添加规则(@"\d+", 词元类型.数字);
            t.添加规则(@"\bR\d+\b", 词元类型.寄存器);

            // 标点符号
            t.添加规则(@":", 词元类型.冒号);
            t.添加规则(@",", 词元类型.逗号);
            t.添加规则(@"\[", 词元类型.左方括号);
            t.添加规则(@"\]", 词元类型.右方括号);
            t.添加规则(@"[+\-*/%]", 词元类型.运算符);

            // 标识符（中文/字母开头）
            t.添加规则(@"[_\p{L}][_\p{L}\p{N}]*", 词元类型.标识符);

            // 其他单字符
            t.添加规则(@".", 词元类型.其他符号);

            _tokenizer = t;
        }

        // 兼容旧接口：返回字符串数组（过滤掉方括号、逗号、空白、注释）
        public static string[] 分词(string 行)
        {
            if (行 == null) return [];
            var tokens = _tokenizer.分词(行).ToArray();
            var list = new List<string>();
            foreach (var tk in tokens)
            {
                if (tk.类型 == 词元类型.空白 || tk.类型 == 词元类型.注释) continue;
                if (tk.类型 == 词元类型.左方括号 || tk.类型 == 词元类型.右方括号 || tk.类型 == 词元类型.逗号) continue;
                list.Add(tk.值);
            }
            
            return [.. list];
        }
        #endregion

        #region 分词器C:注释扫描与去掉工具
        /// <summary> 扫描文本中的注释区间;实现见 代码解析器 </summary>
        public static List<(int 开始, int 结束)> 取注释区(string 文本) =>
            代码解析器.取注释区(文本);

        /// <summary> 去掉注释后返回纯代码文本;实现见 代码解析器 </summary>
        public static string 去注释(string 文本) =>
            代码解析器.去注释(文本);
        #endregion
        #region 分词器C:不可见字符清洗与调试辅助工具
        /// <summary>
        /// 将字符串中的不可见字符转换为可见的转义序列（用于调试）
        /// </summary>
        public static string 到可见字符(this string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var sb = new StringBuilder();
            foreach (char c in input)
            {
                // 1. 普通空格（U+0020）直接保留，方便阅读
                if (c == ' ')
                {
                    sb.Append(' ');
                    continue;
                }

                // 2. 常见的转义字符用标准简写
                switch (c)
                {
                    case '\r': sb.Append("\\r"); continue;
                    case '\n': sb.Append("\\n"); continue;
                    case '\t': sb.Append("\\t"); continue;
                    case '\v': sb.Append("\\v"); continue;
                    case '\f': sb.Append("\\f"); continue;
                }

                // 3. 其他不可见字符：控制字符、格式字符、分隔符等
                //    以及所有其他空白字符（但不包括普通空格）
                if (char.IsControl(c) ||
                    char.GetUnicodeCategory(c) == UnicodeCategory.Format ||
                    char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator ||
                    char.GetUnicodeCategory(c) == UnicodeCategory.LineSeparator ||
                    char.GetUnicodeCategory(c) == UnicodeCategory.ParagraphSeparator)
                {
                    // 用 Unicode 码点表示，如 \u200B、\uFEFF
                    sb.Append($"\\u{(int)c:X4}");
                    continue;
                }

                // 4. 对于高代理项或普通可打印字符，直接追加
                sb.Append(c);
            }

            return sb.ToString();
        }
        /// <summary>
        /// 摘掉"不可见且非空白"的字符:格式字符(`\p{Cf}`,BOM / 零宽那一类)与控制字符(`\p{Cc}`),
        /// 但 **`\r` `\n` `\t` 必须原样留着**(制表符缩进与换行都是源码的一部分)。
        ///
        /// R8-1(用户 2026-09-21 报"示例06 去掉数组参数的 `[]` 之后 IDE 里能编过、能跑完,命令行却报错"):
        /// 老写法是 `[\p{Cc}&&[^\r\n\t]]` —— 那是**某个支持"字符类减法"的正则方言**的写法,
        /// 而 **.NET 不认识 `&&`**:它把类里那个 `[` 当普通字面量、把第一个 `]` 当成类的结束,
        /// 于是整个类变成「{控制字符, `&`, `[`, `^`, `\r`, `\n`, `\t`} 里一个字符,**后面还必须跟一个字面 `]`**」——
        /// 结果 `[]` / `&]` / `^]` / `\n]` 这种"某字符 + 右方括号"被**整对吃掉**,该摘的控制字符反倒摘不掉。
        /// 落点就是**空方括号**:`整数 nums[]` 载入编辑器后变成 `整数 nums`、`短整数 分数[]` 变成 `短整数 分数`
        /// (读盘 `解码文本` 与写盘 `取磁盘文本` 共用 `清洗文本`,所以**打开就被吃掉、保存还会写回磁盘**),
        /// 而"运行→编译"读的是**磁盘那份**(仍带 `[]`)⇒ 界面上看着少了 `[]`、编出来却是对的。
        /// 旧写法实测:`清洗文本("整数 nums[];\n")` 长度 11 → 9(少的就是那对 `[]`)。
        /// </summary>
        public static string 删除非空白类字符(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            return Regex.Replace(input, @"[\p{Cf}\p{Cc}]",
                m => m.Value is "\r" or "\n" or "\t" ? m.Value : "");
        }

        /// <summary>
        /// 清洗读入/写出的文本:**只摘掉不可见非空白类字符**(BOM、零宽字符等),**末尾空行一个字不动**。
        ///
        /// C26:这里原来还挂着 `TrimEnd('\r', '\n')`,而读盘(`解码文本`)与写盘(`取磁盘文本`)共用它 ——
        /// 于是"打开文件 → 什么不改按一次 Ctrl+S"就把末尾空行吃掉了(`git diff` 上那句
        /// `\ No newline at end of file`),注释里写的"往返一致"其实不成立。
        /// 修法是**读进来也不动末尾空行**(存在编辑器里的就是磁盘上那份),
        /// 这样"读进编辑器 → 原样写回"才可能逐字节一致;编码头那件事归 `文本落盘C.写回` 管。
        /// </summary>
        public static string 清洗文本(string? 文本)
        {
            文本 ??= "";
            return 到可见字符(文本).Length > 文本.Length ? 删除非空白类字符(文本) : 文本;
        }
        #endregion
    }
}
