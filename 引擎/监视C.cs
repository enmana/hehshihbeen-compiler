using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **一条监视表达式**(P35,2026-10-07):调试时"我要一直盯着这个"。
    ///
    /// 写法与条件断点**同一套**(`断点条件C`):
    ///   · 光写一个变量名(`计数`、`计数.字段`)—— 看它现在是多少;
    ///   · `变量 运算符 常量`(`i == 5`、`总数 >= 100`、`c == 'A'`)—— 看这个判断现在是真还是假;
    ///   · `变量 运算符 变量`(`左 &lt; 右`)—— 两个变量比。
    /// 也就是说:监视窗里能写的东西,**下断点条件时也能原样贴进去**(一套语法,不另学一套)。
    /// </summary>
    public sealed class 监视表达式C
    {
        public string 原文 { get; set; } = "";

        /// <summary> 解析结果(合法性 / 说明都跟着它走)</summary>
        public 断点条件C 条件 { get; set; } = new() { 合法 = true, 说明 = "" };

        /// <summary> 最近几次算出来的值(新的在前;只在值**变了**的时候记一条 —— 与 VS 的监视窗一个意思)</summary>
        public List<string> 历史 { get; set; } = [];

        /// <summary> 变化历史最多留几条 </summary>
        public const int 历史条数 = 6;

        /// <summary> 建一条(原文空白 ⇒ `合法 = false`,由界面给一句"写点什么")</summary>
        public static 监视表达式C 解析(string? 文本)
        {
            string 文 = (文本 ?? "").Trim();
            return new 监视表达式C { 原文 = 文, 条件 = 断点条件求值C.解析(文) };
        }

        /// <summary> 记一条"这一次算出来的值"(与上一条相同就不记,省得列表里全是同一个数)</summary>
        public void 记历史(string 值文本)
        {
            if (历史.Count > 0 && string.Equals(历史[0], 值文本, StringComparison.Ordinal)) return;
            历史.Insert(0, 值文本);
            if (历史.Count > 历史条数) 历史.RemoveRange(历史条数, 历史.Count - 历史条数);
        }

        /// <summary> "变过的样子":最早 → 最新(`1 → 2 → 3`);只记了一条就是它自己 </summary>
        public string 历史文本()
        {
            if (历史.Count == 0) return "";
            var 顺 = new List<string>(历史);
            顺.Reverse();
            return string.Join(" → ", 顺);
        }

        public override string ToString() => 原文;
    }

    /// <summary>
    /// 监视表达式的**求值**(纯函数):把一行"变量读数"喂进来,算出现在是多少 / 真还是假。
    /// 结果里 `值文本` 是给人看的那一格,`说明` 是"为什么是这个值"(比不了 / 看不见时写清原因)。
    /// </summary>
    public static class 监视求值C
    {
        /// <summary> 一次求值的结果(界面直接贴到表上)</summary>
        public sealed class 结果C
        {
            public string 值文本 = "";
            public string 类型 = "";
            public string 说明 = "";
            /// <summary> 算出来了吗(false = 看不见 / 比不了,值文本给的是"判不了"那一档)</summary>
            public bool 算出来了;
        }

        /// <summary> 算一条监视表达式;`行` 用来在说明里说清"第几行看不见" </summary>
        public static 结果C 求值(监视表达式C 监视, IReadOnlyList<变量读数C> 读数们, int 行)
        {
            var 果 = new 结果C { 值文本 = "(判不了)" };
            if (监视 == null) { 果.说明 = "空的监视"; return 果; }
            var 条件 = 监视.条件;
            if (条件 == null || 条件.原文.Length == 0) { 果.说明 = "写一个变量名或 `变量 == 值`"; return 果; }
            if (!条件.合法) { 果.说明 = 条件.说明; return 果; }

            if (!断点条件求值C.按名字取(读数们, 条件.变量, 行, out var 左读数, out string 找说明))
            {
                果.说明 = 找说明;
                return 果;
            }
            if (!左读数!.可读)
            {
                果.说明 = $"`{条件.变量}` 现在的值读不到(它还没进作用域,或者内存位置对不上)";
                return 果;
            }
            果.类型 = 左读数.类型名;

            // ① 光写一个变量:显示它自己的值(与变量窗格同一份口径)
            if (条件.运算符.Length == 0)
            {
                果.值文本 = 左读数.值文本;
                果.说明 = 左读数.位置文本;
                果.算出来了 = true;
                return 果;
            }

            // ② `变量 运算符 常量/变量`:显示 真 / 假,说明里把两边的值都写出来(不然"为什么是假"看不出来)
            if (!断点条件求值C.解一个(左读数, out long 左, out string 左说明)) { 果.说明 = 左说明; return 果; }
            long 右;
            string 右话;
            if (条件.跟变量比)
            {
                if (!断点条件求值C.按名字取(读数们, 条件.另一个变量, 行, out var 右读数, out string 找说明2))
                {
                    果.说明 = 找说明2;
                    return 果;
                }
                if (!右读数!.可读) { 果.说明 = $"`{条件.另一个变量}` 现在的值读不到"; return 果; }
                if (!断点条件求值C.解一个(右读数, out 右, out string 右说明)) { 果.说明 = 右说明; return 果; }
                右话 = $"{条件.另一个变量} = {右}";
            }
            else if (!断点条件求值C.试解常量(条件.常量原文, out 右, out string 常量说明))
            {
                果.说明 = 常量说明;
                return 果;
            }
            else 右话 = $"{条件.常量原文} = {右}";

            bool 真 = 断点条件求值C.比(条件.运算符, 左, 右);
            果.值文本 = 真 ? "真" : "假";
            果.说明 = $"{条件.变量} = {左}, {右话} —— 所以 `{条件.变量} {条件.运算符} "
                + (条件.跟变量比 ? 条件.另一个变量 : 条件.常量原文) + "` 是 " + (真 ? "真" : "假");
            果.算出来了 = true;
            return 果;
        }
    }
}
