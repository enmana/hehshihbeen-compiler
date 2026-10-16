using System.IO;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **一对标记之间的"自动生成块"**(R4-6,第197批)。
    ///
    /// 由来:P31 的规模表(`全面测评报告.md`)自己写了一套"只改标记之间"的替换;R4-6 又要把
    /// `接手说明.md` §4 那段"当前数字"也变成生成的 —— 同一件事,于是把那段逻辑收成一个纯函数
    /// (`规模统计C.更新报告` 与 `当前数字C.更新` 都改调它,行为一字未改)。
    ///
    /// **为什么是"标记之间"而不是"整篇重写"**:那几个文件里绝大部分是**人写的正文**,
    /// 只有一小段是机器生成的;认一对标记,就只动那一段。
    ///
    /// **踩过的坑(原样留着)**:替换曾经在循环里把"结束标记到文件尾"整段提前追加一次、收尾又追加一次,
    /// 于是每跑一遍,标记之后的正文就翻一倍(实测 `全面测评报告.md` 长到 43MB / 正文 2048 份)。
    /// 现在的写法:每轮只把"开始标记 + 生成块"追加进去,结束标记之后的内容一律交给下一轮或收尾那一次 ——
    /// **连跑两遍长度不变**(自检里有断言盯着)。
    /// </summary>
    public static class 标记块C
    {
        /// <summary>
        /// 把一对标记之间的内容换成 `块`;返回替换了几段(正常是 1)。
        /// 找不到标记对就抛异常说清怎么加 —— **不去猜"该插在哪儿"**。
        /// </summary>
        public static int 替换(string 路径, string 开始标记, string 结束标记, string 块)
        {
            string 文本 = File.ReadAllText(路径);
            int 计数 = 0, 位置 = 0;
            var 结果 = new StringBuilder();
            while (true)
            {
                int 起 = 文本.IndexOf(开始标记, 位置, StringComparison.Ordinal);
                if (起 < 0) break;
                int 止 = 文本.IndexOf(结束标记, 起 + 开始标记.Length, StringComparison.Ordinal);
                if (止 < 0)
                    throw new InvalidOperationException($"文件里有开始标记但没有结束标记({结束标记})");
                结果.Append(文本, 位置, 起 + 开始标记.Length - 位置);
                结果.Append('\n').Append(块).Append('\n');
                位置 = 止;   // 结束标记本身与它后面的内容都不在这里追加(见上面"踩过的坑")
                计数++;
            }
            if (计数 == 0)
                throw new InvalidOperationException(
                    $"找不到自动统计的标记(这份文件的自动生成段)。请在要自动生成的那一段前后各加一行:\n{开始标记}\n{结束标记}");
            结果.Append(文本, 位置, 文本.Length - 位置);
            File.WriteAllText(路径, 结果.ToString());
            return 计数;
        }

        /// <summary>
        /// 取一对标记之间的原文(**不含标记本身**);找不到那一对标记就返回 null。
        /// 给"盘上那份没过期"用:自检拿它跟现场生成的那一份逐字比。
        /// </summary>
        public static string? 取(string 文本, string 开始标记, string 结束标记)
        {
            int 起 = 文本.IndexOf(开始标记, StringComparison.Ordinal);
            if (起 < 0) return null;
            int 止 = 文本.IndexOf(结束标记, 起 + 开始标记.Length, StringComparison.Ordinal);
            if (止 < 0) return null;
            return 文本.Substring(起 + 开始标记.Length, 止 - 起 - 开始标记.Length);
        }
    }
}
