namespace hehshihbeen_compiler
{
    /// <summary> 一处改动:第几行、行内第几列(0 基)、把多长的一段换成什么 </summary>
    public readonly record struct 修复处C(int 行, int 列, int 长度, string 换成);

    /// <summary>
    /// 问题表里那一条的**一键修复**方案(P38,2026-10-07;用户排的七件里的第 5 件:
    /// "从『报错』到『改好』的那一步")。
    ///
    /// 一条诊断可能有好几种改法(比如"计数器会绕回"既可以加宽类型、也可以改循环上界),
    /// 所以是一个**列表**;界面上"修这一条"取第一条(最稳的那条),以后要列表挑也现成。
    /// 方案里只有**纯数据**(哪一行哪一列换成什么),动手改文本的是 `修复C.应用` ——
    /// 自检能真跑:喂一段源码 + 一条诊断 → 拿到方案 → 应用 → 再跑一遍检查 → 那条诊断必须没了。
    /// </summary>
    public sealed class 修复方案C
    {
        /// <summary> 方案的短名(按钮/菜单上显示,例如"删掉行尾空白")</summary>
        public string 名称 { get; set; } = "";

        /// <summary> 一句说明(`把什么改成什么`;给人看的)</summary>
        public string 说明 { get; set; } = "";

        /// <summary> 要改的地方(**同一行内按列从大到小排**,应用时从后往前替换才不会被前一处的长度带偏)</summary>
        public List<修复处C> 改动 { get; set; } = [];

        public override string ToString() => $"{名称}:{说明}";
    }

    /// <summary>
    /// **一键修复**的规则表(P38)。这一批先做**十条最常见的** —— 判据两条:
    ///   ① 改法**唯一且稳**(不猜用户想干什么;拿不准的只给"插入一行提示",绝不替他删代码);
    ///   ② 改完那条诊断**必须真的消失**(自检拿真源码跑一遍:应用 → 再检查 → 那条不见了)。
    ///
    /// 十条(按 `诊断项C.规则` 分):
    ///   · **行尾空格** → 删掉行尾那些空白;
    ///   · **缩进混用** → 整行前导空白归一成 4 的倍数个空格(按制表位展开);
    ///   · **旧写法** → `测试输出(x)` → `输出行(x)`;
    ///   · **计数器会绕回** → 把计数器类型加宽成 `整数`;
    ///   · **未使用** → 名字前面加一个 `_`(那是这门语言"故意不用"的写法,加了这条诊断就没了);
    ///   · **常量条件** → 拿不准就**只插入一行注释**提示(不动用户的代码);
    ///   · **空循环** → 循环体里插入一行 `// TODO 还没写`;
    ///   · **死代码** → 那一行整行注释掉;
    ///   · **编译错误** → 没有通法,方案是空的(界面按"没有一键修复"处理,不假装有);
    ///   · 其余规则 → 同上(空方案)。
    /// 纯函数:**不碰界面、不碰盘**,自检直接喂文本。
    /// </summary>
    public static class 一键修复C
    {
        /// <summary> 这一批支持的那几条规则名(自检拿它钉"表里那十条都在")</summary>
        public static readonly string[] 支持规则 =
        [
            "行尾空格", "缩进混用", "旧写法", "计数器会绕回", "未使用",
            "常量条件", "空循环", "死代码",
        ];

        /// <summary> 把源码切成行(与 `检查器C` 同一套切法:CRLF / CR / LF 都认)</summary>
        public static string[] 切行(string 源码) =>
            (源码 ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        /// <summary>
        /// 给一条诊断算**能修的方案**(没有就给空列表——界面据此把"修这一条"变灰,
        /// 绝不放一个点了没反应的按钮)。
        /// </summary>
        public static List<修复方案C> 算(诊断项C 诊断, string 源码)
        {
            var 结果 = new List<修复方案C>();
            if (诊断 == null) return 结果;
            string[] 行表 = 切行(源码);
            int 行号 = 诊断.行;
            if (行号 <= 0 || 行号 > 行表.Length) return 结果;
            string 行 = 行表[行号 - 1];

            switch (诊断.规则)
            {
                case "行尾空格":
                    {
                        int 尾 = 行.Length;
                        while (尾 > 0 && (行[尾 - 1] == ' ' || 行[尾 - 1] == '\t')) 尾--;
                        if (尾 >= 行.Length) return 结果;
                        结果.Add(new 修复方案C
                        {
                            名称 = "删掉行尾空白",
                            说明 = $"第 {行号} 行末尾那 {行.Length - 尾} 个空白字符删掉",
                            改动 = [new 修复处C(行号, 尾, 行.Length - 尾, "")],
                        });
                        return 结果;
                    }

                case "缩进混用":
                    {
                        int 前导 = 0;
                        while (前导 < 行.Length && (行[前导] == ' ' || 行[前导] == '\t')) 前导++;
                        if (前导 == 0) return 结果;
                        string 新缩进 = 归一缩进(行[..前导]);
                        if (新缩进 == 行[..前导]) return 结果;
                        结果.Add(new 修复方案C
                        {
                            名称 = "缩进归一成空格",
                            说明 = $"第 {行号} 行前导空白换成 {新缩进.Length} 个空格(按 4 格一档)",
                            改动 = [new 修复处C(行号, 0, 前导, 新缩进)],
                        });
                        return 结果;
                    }

                case "旧写法":
                    {
                        int 位 = 代码解析器.遮蔽区间(行).IndexOf("测试输出", StringComparison.Ordinal);
                        if (位 < 0) return 结果;
                        结果.Add(new 修复方案C
                        {
                            名称 = "改成 输出行",
                            说明 = $"第 {行号} 行的 `测试输出` 换成 `输出行`(现在有 `输出行` 了:输出并换行)",
                            改动 = [new 修复处C(行号, 位, "测试输出".Length, "输出行")],
                        });
                        return 结果;
                    }

                case "计数器会绕回":
                    {
                        // 诊断的列就是类型词那一段(检查器C 里 `起列 = 类型词.Index`)
                        int 起 = Math.Max(0, 诊断.起列);
                        int 长 = Math.Max(0, 诊断.止列 - 诊断.起列);
                        if (长 <= 0 || 起 + 长 > 行.Length) return 结果;
                        string 类型词 = 行.Substring(起, 长);
                        if (类型词.Length == 0) return 结果;
                        结果.Add(new 修复方案C
                        {
                            名称 = $"计数器加宽成 整数",
                            说明 = $"第 {行号} 行的计数器类型 `{类型词}` 换成 `整数`(32 位,数得下这一趟)",
                            改动 = [new 修复处C(行号, 起, 长, "整数")],
                        });
                        return 结果;
                    }

                case "未使用":
                    {
                        int 起 = Math.Max(0, 诊断.起列);
                        int 长 = Math.Max(0, 诊断.止列 - 诊断.起列);
                        if (长 <= 0 || 起 + 长 > 行.Length) return 结果;
                        string 名 = 行.Substring(起, 长);
                        if (名.Length == 0 || 名.StartsWith('_')) return 结果;
                        结果.Add(new 修复方案C
                        {
                            名称 = "加 `_` 前缀(说明这是故意不用的)",
                            说明 = $"第 {行号} 行的 `{名}` 改成 `_{名}`(这门语言里下划线开头 = 故意不用,不再提示)",
                            改动 = [new 修复处C(行号, 起, 长, "_" + 名)],
                        });
                        return 结果;
                    }

                case "常量条件":
                    {
                        // 拿不准用户是想改条件还是想删这一段 ⇒ 只**插入一行注释**,一个字都不动
                        结果.Add(new 修复方案C
                        {
                            名称 = "插一行注释提醒(不动代码)",
                            说明 = $"第 {行号} 行上面插一行 `// TODO 这里的条件是常量`,改法由你定",
                            改动 = [new 修复处C(行号, 0, 0, "// TODO 这里的条件是常量\n")],
                        });
                        return 结果;
                    }

                case "空循环":
                    {
                        // 循环体那一行(诊断落在循环头那一行)是 `}` —— 在它前面插一行 TODO
                        if (行号 + 1 > 行表.Length) return 结果;
                        string 体行 = 行表[行号];              // 下标 行号 = 下一行(0 基)
                        string 缩进 = 前导空白(行) + "    ";
                        结果.Add(new 修复方案C
                        {
                            名称 = "循环体里插一行 TODO",
                            说明 = $"第 {行号 + 1} 行(`}}` 那一行)上面插一行 `{缩进}// TODO 还没写`,循环体不再是空的",
                            改动 = [new 修复处C(行号 + 1, 0, 0, 缩进 + "// TODO 还没写\n")],
                        });
                        return 结果;
                    }

                case "死代码":
                    {
                        string 干 = 行.TrimStart();
                        if (干.Length == 0 || 干.StartsWith("//")) return 结果;
                        结果.Add(new 修复方案C
                        {
                            名称 = "整行注释掉",
                            说明 = $"第 {行号} 行整行注释掉(保留原文,随时能改回来)",
                            改动 = [new 修复处C(行号, 0, 0, "// ")],
                        });
                        return 结果;
                    }
            }
            return 结果;      // 其余规则(包括"编译错误"):没有通法 —— 老老实实返回空
        }

        /// <summary>
        /// 把一套改动应用到源码上,返回改完的文本(改不动返回 null)。
        /// **同一行里按列从大到小替换**:先动后面的,前面的列号才不会被前一处的新长度带偏。
        /// 插行(长度 0 的改动)也一样按"本行末尾的相对位置"排 —— 它在 `行号` 那一行的**行首**插入。
        /// </summary>
        public static string? 应用(string 源码, IEnumerable<修复处C> 改动)
        {
            try
            {
                string[] 行表 = 切行(源码);
                string 换行符 = (源码 ?? "").Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var 新行表 = new List<string>(行表);
                foreach (var 组 in 改动.GroupBy(x => x.行).OrderByDescending(g => g.Key))
                {
                    int 行号 = 组.Key;
                    if (行号 <= 0 || 行号 > 行表.Length) return null;
                    string 行 = 新行表[行号 - 1];
                    // 同一行里**按列从大到小**替换:先动后面的,前面的列号才不会被前一处的新长度带偏
                    foreach (var 处 in 组.OrderByDescending(x => x.列))
                    {
                        int 起 = Math.Clamp(处.列, 0, 行.Length);
                        int 长 = Math.Clamp(处.长度, 0, 行.Length - 起);
                        string 新 = 处.换成 ?? "";
                        if (新.Contains('\n'))
                        {
                            // "插一行"那一路:插进去的内容自己占一行(前面那半行原样留着,后面那半行跟到新的一行去)
                            string 正文 = 新.TrimEnd('\r', '\n');
                            行 = 行[..起] + 正文 + 换行符 + 行[起..];
                        }
                        else 行 = 行[..起] + 新 + 行[(起 + 长)..];
                    }
                    新行表[行号 - 1] = 行;
                }
                return string.Join(换行符, 新行表);
            }
            catch { return null; }
        }

        /// <summary>
        /// 缩进归一:把一段前导空白按制表位展开成空格,再夹到 4 的倍数
        /// (`\t` 算 4 格;`4 空格 + 1 tab` 这种混用也归一)。
        /// </summary>
        public static string 归一缩进(string 前导)
        {
            int 列 = 0;
            foreach (char c in 前导 ?? "")
            {
                if (c == '\t') 列 += 4 - (列 % 4);
                else if (c == ' ') 列++;
            }
            int 档 = (列 + 1) / 4;                 // 就近归到 4 的倍数(1 格余数往回、2 格及以上往上,与编辑器缩进的手感一致)
            return new string(' ', 档 * 4);
        }

        /// <summary> 这一行的前导空白原文(插行时照着它缩进)</summary>
        public static string 前导空白(string 行)
        {
            int i = 0;
            while (i < (行?.Length ?? 0) && (行![i] == ' ' || 行[i] == '\t')) i++;
            return 行?[..i] ?? "";
        }
    }
}
