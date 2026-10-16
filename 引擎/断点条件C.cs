using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 一个**条件断点**的条件:`变量 [运算符 常量]`,右边也可以是**另一个变量**(如 `左 == 右`)。
    /// 例:`i == 5`、`计数 != 真`、`c == 'A'`、`总数 >= 100`、`左 < 右`;只写变量(`计数`)就是"非 0 / 真 就算命中"。
    /// 解析失败时 `合法 = false`,`说明` 里写清怎么改(界面把它原样给人看,不静默)。
    /// </summary>
    public sealed class 断点条件C
    {
        /// <summary> 原样文本(空 = 无条件) </summary>
        public string 原文 = "";

        /// <summary> 左边的变量名(可以带 `.字段`,例如 `记录.x`) </summary>
        public string 变量 = "";

        /// <summary> `==` / `!=` / `<` / `<=` / `>` / `>=`;空 = 只写变量本身(非 0 为真) </summary>
        public string 运算符 = "";

        /// <summary> 右边的常量原文(十进制 / `0x…` / `'c'` / 真 / 假);跟变量比时为空 </summary>
        public string 常量原文 = "";

        /// <summary> 右边那个变量名(`左 == 右` 里的 `右`);跟常量比时为空 </summary>
        public string 另一个变量 = "";

        /// <summary> 给人看的说明(合法时是写法摘要;不合法时是改法)</summary>
        public string 说明 = "";

        public bool 合法;

        /// <summary> 条件是"跟另一个变量比"吗 </summary>
        public bool 跟变量比 => 另一个变量.Length > 0;

        public override string ToString() => !合法
            ? $"不合法:{说明}"
            : 运算符.Length == 0 ? $"{变量}(非 0 就算真)"
            : 跟变量比 ? $"{变量} {运算符} {另一个变量}"
            : $"{变量} {运算符} {常量原文}";
    }

    /// <summary>
    /// **命中次数条件**(P34,2026-10-07 起):断点可以只在"第 N 次经过"时停下。
    /// 写法三种(与 VS 的 hit count 同一个意思):
    ///   · `5` 或 `=5` —— 第 5 次经过时停(就这一次);
    ///   · `%5`       —— 每 5 次停一次;
    ///   · `&gt;=5` / `&gt;5` / `&lt;=5` / `&lt;5` —— 次数到这个界就停(以后每次都停)。
    /// 空 = 不限次数(路过就停,与从前一样)。
    /// </summary>
    public sealed class 命中次数条件C
    {
        public string 原文 = "";

        /// <summary> `等于` / `每` / `大于等于` / `大于` / `小于等于` / `小于` </summary>
        public string 种类 = "";

        public long 界;

        public bool 合法;

        public string 说明 = "";

        /// <summary> 空文本 = 不设条件(合法;说明写清"每次经过都算命中")</summary>
        public static 命中次数条件C 解析(string? 文本)
        {
            var 条件 = new 命中次数条件C { 原文 = (文本 ?? "").Trim() };
            if (条件.原文.Length == 0)
            {
                条件.合法 = true;
                条件.说明 = "不限次数(每次经过都算命中)";
                return 条件;
            }
            string 文 = 条件.原文;
            string 号 = "";
            foreach (string 候选 in new[] { ">=", "<=", "%", "=", ">", "<" })
                if (文.StartsWith(候选, StringComparison.Ordinal)) { 号 = 候选; 文 = 文[候选.Length..].Trim(); break; }
            if (号.Length == 0) 号 = "=";                    // 光写一个数字 = `=数字`
            if (!long.TryParse(文, out long 值) || 值 <= 0)
            {
                条件.说明 = $"认不出这个命中次数:`{条件.原文}`。写法三种:`5`(第 5 次停一次)、`%5`(每 5 次停一次)、" +
                    "`>=5`(从第 5 次起每次都停);数字要大于 0";
                return 条件;
            }
            条件.界 = 值;
            条件.种类 = 号 switch
            {
                "=" => "等于",
                "%" => "每",
                ">=" => "大于等于",
                ">" => "大于",
                "<=" => "小于等于",
                _ => "小于",
            };
            条件.合法 = true;
            条件.说明 = 号 switch
            {
                "=" => $"第 {值} 次经过时停下(只有这一次)",
                "%" => $"每 {值} 次经过停一次",
                ">=" => $"第 {值} 次起,每次经过都停",
                ">" => $"超过 {值} 次之后,每次经过都停",
                "<=" => $"前 {值} 次经过都停",
                _ => $"不到 {值} 次时每次经过都停",
            };
            return 条件;
        }

        /// <summary> 这一次(第 <paramref name="次数"/> 次经过)算不算命中;不设条件 ⇒ 每次都算 </summary>
        public bool 满足(long 次数) => 种类 switch
        {
            "等于" => 次数 == 界,
            "每" => 界 > 0 && 次数 % 界 == 0,
            "大于等于" => 次数 >= 界,
            "大于" => 次数 > 界,
            "小于等于" => 次数 <= 界,
            "小于" => 次数 < 界,
            _ => true,
        };

        public override string ToString() => 合法 ? 说明 : $"不合法:{说明}";
    }

    /// <summary>
    /// 一个断点上"停下来之前要过的那几关"(条件 / 命中次数 / 日志)合起来的一份判定数据。
    /// 三种**同时成立**才算命中;填了日志的断点命中时**只打印不停**(logpoint,= "日志断点")。
    /// 纯数据 + 纯函数,自检直接 new 出来喂。
    /// </summary>
    public sealed class 断点判定C
    {
        public 断点条件C 条件 { get; set; } = new() { 合法 = true, 说明 = "没有条件(空 = 每次都停)" };
        public 命中次数条件C 命中次数 { get; set; } = new() { 合法 = true, 说明 = "不限次数(每次经过都算命中)" };

        /// <summary> 日志模板(空 = 不是日志断点);`{变量}` 会被换成那个变量现在的值 </summary>
        public string 日志 { get; set; } = "";

        /// <summary> 这是个**日志断点**吗(命中时只打印、不停下) </summary>
        public bool 是日志断点 => 日志.Trim().Length > 0;

        public bool 合法 => 条件.合法 && 命中次数.合法;

        public string 说明 => 条件.说明 + ";" + 命中次数.说明 + (是日志断点 ? ";命中时只打印不停下" : "");

        /// <summary> 从一个断点项算出来(弹窗里改什么,这里就跟着变) </summary>
        public static 断点判定C 从断点(断点项C 断点) => new()
        {
            条件 = 断点条件求值C.解析(断点.条件),
            命中次数 = 命中次数条件C.解析(断点.命中次数),
            日志 = 断点.日志 ?? "",
        };
    }

    /// <summary>
    /// **条件断点**的求值。纯函数,零界面依赖 —— 界面把它挂在
    /// `调试会话C.断点命中判断` 上(见 `引擎\调试会话C.cs`:判断为假就当没命中,继续跑)。
    ///
    /// 口径:
    ///   · 条件形如 `变量 运算符 常量`,右边也可以是**另一个变量**(P34 起;`变量读取C` 会把结构字段也摊出来,名字是 `记录.x`);
    ///   · 运算符 `== != < <= > >=`;两边都按**各自声明类型的宽度与符号**解释(与调试面板显示一致:
    ///     `短整数` 槽里的 0xFFFF 显示成 -1,这里也比 -1);
    ///   · 常量:十进制、`0x` 十六进制、`'字符'`(认 `\n \t \0 \\ \'` 这几个转义)、`真`/`假`;
    ///   · 只写变量(`计数`)= 非 0 就算真(逻辑变量天然适用);
    ///   · **字符串 / 结构 / 数组 / 列表不能这样比**(读不出一个能比的值)—— 返回 null,说明里讲清楚;
    ///   · 变量不在**这一行**可见、或者值读不到 → 返回 null(调用方按"停 + 说一句"处理:宁可多停一次,
    ///     也别让用户以为条件断点没生效 —— 但绝不猜一个值去比)。
    /// </summary>
    public static class 断点条件求值C
    {
        /// <summary> 变量名(可带 `.字段`):字母或下划线开头,后面字母/数字/下划线,点分段。
        /// 注意:点那一段用**非捕获组** —— 不然组号会错位(运算符会变成第 3 组)。 </summary>
        private static readonly Regex 变量式 = new(
            @"^([\p{L}_][\p{L}\p{N}_]*(?:\.[\p{L}_][\p{L}\p{N}_]*)*)\s*(==|!=|<=|>=|<|>)?\s*(.*)$", RegexOptions.Compiled);

        /// <summary> 只看"右边是不是一个裸名字"(跟另一个变量比;P34 起支持) </summary>
        private static readonly Regex 裸名字式 = new(@"^[\p{L}_][\p{L}\p{N}_]*(\.[\p{L}_][\p{L}\p{N}_]*)*$", RegexOptions.Compiled);

        /// <summary> 解析一个条件文本(不抛异常;坏写法靠 `合法=false` + `说明` 报出来) </summary>
        public static 断点条件C 解析(string? 文本)
        {
            var 条件 = new 断点条件C { 原文 = (文本 ?? "").Trim() };
            if (条件.原文.Length == 0)
            {
                条件.合法 = true;
                条件.说明 = "没有条件(空 = 每次都停)";
                return 条件;
            }

            var 匹配 = 变量式.Match(条件.原文);
            if (!匹配.Success)
            {
                条件.说明 = $"认不出这个条件:`{条件.原文}`。写法是 `变量 运算符 常量`,例如 `i == 5`、`计数 != 真`、`c == 'A'`";
                return 条件;
            }
            条件.变量 = 匹配.Groups[1].Value;

            if (!匹配.Groups[2].Success)
            {
                条件.常量原文 = "";
                条件.说明 = $"只看 `{条件.变量}` 是不是非 0(要比较就写 `{条件.变量} == 5` 这样)";
                条件.合法 = true;
                return 条件;
            }

            条件.运算符 = 匹配.Groups[2].Value;
            条件.常量原文 = 匹配.Groups[3].Value.Trim();
            if (条件.常量原文.Length == 0)
            {
                条件.说明 = $"`{条件.运算符}` 后面要跟一个常量或另一个变量(数字 / `0x` 十六进制 / `'字符'` / `真` / `假`)";
                return 条件;
            }
            if (条件.常量原文.StartsWith('"'))
            {
                条件.说明 = "这一批不支持**字符串**条件(比不了)—— 想按内容判断,可以先用 `.长度()` 之类换个整数来判断";
                return 条件;
            }
            // P34:**先认常量、再认变量名**。顺序不能反 —— `真` / `假` / 十进制数都是**常量的写法**,
            // 拿"裸名字"那把尺子先量,`好 == 真` 会被当成"跟另一个叫 `真的` 变量比"。
            if (试解常量(条件.常量原文, out _, out _))
            {
                条件.合法 = true;
                条件.说明 = $"命中条件:{条件}";
                return 条件;
            }
            if (裸名字式.IsMatch(条件.常量原文))
            {
                条件.另一个变量 = 条件.常量原文;
                条件.常量原文 = "";
                条件.合法 = true;
                条件.说明 = $"命中条件:{条件.变量} {条件.运算符} {条件.另一个变量}";
                return 条件;
            }
            // 两个都不是:把"常量解不出"的那句人话原样给人看(它写清了支持哪几种写法)
            试解常量(条件.常量原文, out _, out string 常量说明);
            条件.说明 = 常量说明;
            return 条件;
        }

        /// <summary>
        /// 判断"现在算不算命中"。`说明` 解释 null 的原因(界面拿它提示一次)。
        /// 返回 true = 停;false = 这次不算命中、继续跑;null = 判不了(读不到变量 / 类型比不了)。
        /// </summary>
        public static bool? 判断(断点条件C 条件, 名字表C? 名字表, IReadOnlyDictionary<string, int>? 标签表,
            byte[]? 内存, uint 帧指针, int 行, out string 说明)
        {
            说明 = "";
            if (条件 == null) { 说明 = "没有条件"; return true; }
            if (!条件.合法) { 说明 = 条件.说明; return null; }
            if (条件.原文.Length == 0) return true;

            var 读数们 = 变量读取C.取可见变量(名字表, 标签表, 内存, 帧指针, 行);
            if (!按名字取(读数们, 条件.变量, 行, out var 读数, out 说明)) return null;
            if (!读数!.可读) { 说明 = $"`{条件.变量}` 现在的值读不到(它还没进作用域,或者内存位置对不上)"; return null; }

            if (条件.运算符.Length == 0) return 读数.值 != 0;

            // 右边:常量(P34 前的写法)还是另一个变量(P34 起)
            long 右值;
            if (条件.跟变量比)
            {
                if (!按名字取(读数们, 条件.另一个变量, 行, out var 右读数, out 说明)) return null;
                if (!右读数!.可读) { 说明 = $"`{条件.另一个变量}` 现在的值读不到(它还没进作用域,或者内存位置对不上)"; return null; }
                if (!解一个(右读数, out 右值, out string 右说明)) { 说明 = 右说明; return null; }
            }
            else if (!试解常量(条件.常量原文, out 右值, out string 常量说明))
            {
                说明 = 常量说明;
                return null;
            }

            if (!解一个(读数, out long 左, out string 类型说明)) { 说明 = 类型说明; return null; }
            return 比(条件.运算符, 左, 右值);
        }

        /// <summary> 六个运算符的比较(条件与监视表达式共用一处,别两边各写一份)</summary>
        public static bool 比(string 运算符, long 左, long 右) => 运算符 switch
        {
            "==" => 左 == 右,
            "!=" => 左 != 右,
            "<" => 左 < 右,
            "<=" => 左 <= 右,
            ">" => 左 > 右,
            ">=" => 左 >= 右,
            _ => false,
        };

        /// <summary> 在"这一行看得见的变量"里找一个(找不到给 null + 说明);监视窗格也走这一处 </summary>
        public static bool 按名字取(IReadOnlyList<变量读数C> 读数们, string 名字, int 行,
            out 变量读数C? 读数, out string 说明)
        {
            读数 = null;
            说明 = "";
            foreach (var 一个 in 读数们)
                if (string.Equals(一个.名字, 名字, StringComparison.OrdinalIgnoreCase)) { 读数 = 一个; break; }
            if (读数 != null) return true;
            说明 = $"`{名字}` 在第 {行} 行看不见(变量名写错?或者它在这个函数之外)";
            return false;
        }

        /// <summary>
        /// 把一个读数按"它声明时的类型"解释成一个能比的整数(条件与监视窗格共用)。
        /// 特例:不写类型的变量(`var i = 0;`)在名字表里的类型名是内部名 **`32位字`** ——
        /// 那不是一个语言类型名(`类型C.解析` 认不出),但它就是 4 字节的槽,按**有符号 32 位**看
        /// (与 `整数` 同一口径 —— `var`/`整数` 在语言里都是 4 字节有符号槽)。
        /// </summary>
        public static bool 解一个(变量读数C 读数, out long 值, out string 说明)
        {
            值 = 0;
            说明 = "";
            if (string.Equals(读数.类型名, "32位字", StringComparison.Ordinal))
            {
                值 = 类型C.看成有符号(读数.值, 类型C.字宽度);
                return true;
            }
            if (类型C.解析(读数.类型名) is not { } 类型)
            {
                说明 = $"`{读数.名字}` 是 `{读数.类型名}`,这里只能拿**基本类型**比" +
                    "(字符串 / 结构 / 数组 / 列表比不了)";
                return false;
            }
            值 = 类型.有符号 ? 类型C.看成有符号(读数.值, 类型.宽度) : 读数.值;
            return true;
        }

        /// <summary> 把常量文本解成一个整数(十进制 / `0x` / `'字符'` / 真 / 假);解不出给一句人话。监视窗格也用它 </summary>
        public static bool 试解常量(string 文本, out long 值, out string 说明)
        {
            值 = 0;
            说明 = "";
            string 文 = (文本 ?? "").Trim();
            if (文.Length == 0) { 说明 = "常量是空的"; return false; }

            switch (文)
            {
                case "真": case "true": case "TRUE": 值 = 1; return true;
                case "假": case "false": case "FALSE": 值 = 0; return true;
            }

            if (文.Length >= 2 && 文[0] == '\'' && 文[^1] == '\'')
            {
                // R2-5:字符常量走 `转义表C`(唯一事实源)—— 以前这里又手抄了一份更小的表
                // (只认 `n t r 0 \ '`),与字符串、字符字面量三处口径对不上。
                if (转义表C.解字符内文(文[1..^1], out string 说) is { } 码) { 值 = 码; return true; }
                说明 = $"`{文}` 不合法:{说}";
                return false;
            }

            if (文.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(文[2..], System.Globalization.NumberStyles.HexNumber, null, out long 十六)) { 值 = 十六; return true; }
                说明 = $"`{文}` 不是合法的十六进制数";
                return false;
            }

            if (long.TryParse(文, out long 十进制)) { 值 = 十进制; return true; }
            说明 = $"`{文}` 认不出:常量支持十进制 / `0x…` / `'字符'` / `真` / `假`,也可以写另一个变量名(如 `左 < 右`)";
            return false;
        }
    }

    /// <summary>
    /// **日志断点**(logpoint,P34)的模板展开:`{变量}` 换成那个变量现在的值。
    /// 除了变量,还认 `{行}`(当前源码行号)与 `{文件}`(当前文件名)。
    /// 换不出来(名字看不见 / 值读不到 / 类型比不了)时**原样留着那个占位**并在末尾补一句为什么 ——
    /// 宁可打一行看得出问题的日志,也不悄悄打一个假值。
    /// 纯函数:读数表由调用方(界面)拿 `变量读取C.取可见变量` 取好递进来。
    /// </summary>
    public static class 日志断点C
    {
        /// <summary> 模板里的占位:`{名字}`(名字可带 `.字段`,里面不许有花括号) </summary>
        private static readonly Regex 占位式 = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

        /// <summary> 展开一行日志;`行` / `文件名` 用来填 `{行}` / `{文件}` </summary>
        public static string 展开(string 模板, IReadOnlyList<变量读数C> 读数们, int 行, string 文件名)
        {
            string 文 = 模板 ?? "";
            if (文.Length == 0) return "";
            var 说明 = new List<string>();
            string 结果 = 占位式.Replace(文, 匹配 =>
            {
                string 名 = 匹配.Groups[1].Value.Trim();
                if (名 is "行" or "line") return 行.ToString();
                if (名 is "文件" or "file") return 文件名;
                变量读数C? 读数 = null;
                foreach (var 一个 in 读数们)
                    if (string.Equals(一个.名字, 名, StringComparison.OrdinalIgnoreCase)) { 读数 = 一个; break; }
                if (读数 == null) { 说明.Add($"`{名}` 在第 {行} 行看不见"); return 匹配.Value; }
                if (!读数.可读) { 说明.Add($"`{名}` 现在的值读不到"); return 匹配.Value; }
                return 读数.值文本;
            });
            return 说明.Count == 0 ? 结果 : 结果 + "(说明:" + string.Join(";", 说明) + ")";
        }

        /// <summary> 模板里用到了哪些变量名(界面拿它提示"这一行看不看得见")</summary>
        public static List<string> 用到的名字(string 模板)
        {
            var 结果 = new List<string>();
            foreach (Match 一条 in 占位式.Matches(模板 ?? ""))
            {
                string 名 = 一条.Groups[1].Value.Trim();
                if (名.Length == 0 || 名 is "行" or "line" or "文件" or "file") continue;
                if (!结果.Contains(名, StringComparer.Ordinal)) 结果.Add(名);
            }
            return 结果;
        }
    }
}
