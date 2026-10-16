using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 展开文本里"这条指令来自源码第几行"标记的**唯一一份解析**。
    ///
    /// 写法只有一处产生(`中间层C.发出` / `中间层C.主流程.cs` 拼生成代码时),挂在指令屁股后面当注释、不占字节:
    ///
    /// | 形状 | 意思 |
    /// |---|---|
    /// | `// 来自第 12 行` | 单行语句 |
    /// | `// 来自第 7 到 12 行` | 整块语句(if / 当 / 函数体…) |
    /// | `// 来自文件2第 12 行` | 工程级展开:文件号 1 基,对应文件表顺序 |
    /// | `// 来自第 12 行 #语句37` | 这条语句在伪行表里的下标(编辑器"就地展开"按它挑指令) |
    /// | `// 来自第 12 行 库` | 这条语句来自被 `包含` 进来的文件 —— **行号是那个文件的行号**,不是本文件的 |
    /// | `// 来自第 12 行 库 E:\…\库\目录.hb` | 这条语句来自被 `包含` 进来的**哪一份**文件(H47 起带上它的全路径) |
    /// | `// 编译器生成` | 从这儿起是编译器自己生成的代码(序言、字符串/容器初始化、池与列表扩容等内部例程) |
    ///
    /// **为什么要收成一份**:以前有三处各写一份正则,写法一改就只改一处,
    /// 结果第133批之前 `执行行映射C` 认的是"来自**库**第 12 行"(库写在前面),
    /// 而真正写出来的是尾巴上的 `库` —— 于是库里的行号被当成主文件的行号,
    /// 单步黄条在编辑器里乱跳(用户实测:示例 61 单步,一来就跳到 `缩写[1] = '中';`)。
    ///
    /// **`// 编译器生成` 是第136批加的**:生成代码(内部例程、程序开头的初始化)以前没有标记,
    /// 查表时"沿用上一条语句的行号",于是单步走进内部例程就停在**最后一条语句**上(用户报"跑到 停止 去了")。
    /// 有了这条标记,生成代码一律**没有源行**(走进去不换行,也就不会停在那上面),直到下一个真标记。
    /// </summary>
    public static class 源行标记C
    {
        /// <summary> 标记的写法(正则文本) </summary>
        public const string 正则文本 =
            @"(?<生成>编译器生成)|来自(?:文件(?<文件>\d+))?第\s*(?<起>\d+)\s*(?:到\s*(?<止>\d+)\s*)?行(?:\s*#语句(?<语句>\d+))?(?:\s*(?<库>库)(?:\s+(?<库文件>\S[^\r\n]*))?)?";

        /// <summary> 编译好的正则(容错用 `Match`,不抛) </summary>
        public static readonly Regex 正则 = new(正则文本, RegexOptions.Compiled);

        /// <summary> 生成代码的标记文本(拼展开文本时用;见 `中间层C.主流程.cs`) </summary>
        public const string 生成标记 = "// 编译器生成";

        /// <summary> 读出来的标记 </summary>
        /// <param name="文件号">工程级展开里的文件号(1 基);单文件展开 = 0</param>
        /// <param name="起始行">这条语句的源码首行(1 基)</param>
        /// <param name="止行">整块语句的末行(单行语句 = 起始行)</param>
        /// <param name="语句号">伪行表下标(没有 = -1)</param>
        /// <param name="来自库">是不是 `包含` 进来的文件里的语句</param>
        /// <param name="库文件">来自哪一份库文件(只要文件名;H47)。空 = 老标记 / 没有文件名(那时只能按"行号不可考"办)</param>
        /// <param name="生成">这一行是"编译器生成"的开关标记(从这儿起没源行,直到下一个真标记)</param>
        public readonly record struct 标记(int 文件号, int 起始行, int 止行, int 语句号, bool 来自库, bool 生成, string 库文件 = "")
        {
            /// <summary> 这一行真的带"来自第 N 行"标记吗(起始行 > 0 才算) </summary>
            public bool 有 => 起始行 > 0;
        }

        /// <summary> 没有标记时的空值(免得到处写 `out _`) </summary>
        public static readonly 标记 空 = new(0, 0, 0, -1, false, false, "");

        /// <summary> 读一行里的标记;没有标记返回 `空`(调用方看 <see cref="标记.有"/>) </summary>
        public static 标记 读(string? 行)
        {
            if (string.IsNullOrEmpty(行)) return 空;
            var m = 正则.Match(行);
            if (!m.Success) return 空;
            if (m.Groups["生成"].Success) return 空 with { 生成 = true };
            if (!int.TryParse(m.Groups["起"].Value, out int 起) || 起 <= 0) return 空;
            int 止 = int.TryParse(m.Groups["止"].Value, out int 到) && 到 >= 起 ? 到 : 起;
            int 文件号 = int.TryParse(m.Groups["文件"].Value, out int 号) ? 号 : 0;
            int 语句号 = m.Groups["语句"].Success && int.TryParse(m.Groups["语句"].Value, out int t) ? t : -1;
            string 库文件 = m.Groups["库文件"].Success ? m.Groups["库文件"].Value.Trim() : "";
            return new 标记(文件号, 起, 止, 语句号, m.Groups["库"].Success, false, 库文件);
        }

        /// <summary>
        /// 把一行尾巴上的源行标记剪掉(拼生成代码时用:生成的那几块不该挂着某条语句的行号)。
        /// 只剪标记本身,别的注释原样留着。
        /// </summary>
        public static string 去标记(string? 行)
        {
            if (string.IsNullOrEmpty(行)) return 行 ?? "";
            var m = 正则.Match(行);
            if (!m.Success) return 行;
            string 果 = (行[..m.Index] + 行[(m.Index + m.Length)..]).TrimEnd();
            // 标记原来挂在 `// 来自…` 后面:剪掉标记后别留下一个光秃秃的 `//`
            if (果.EndsWith("//", StringComparison.Ordinal)) 果 = 果[..^2].TrimEnd();
            return 果;
        }
    }
}
