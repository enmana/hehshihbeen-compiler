namespace hehshihbeen_compiler
{
    /// <summary>
    /// 语义列模型:把一行汇编切成 [标签][指令][操作数][注释],并可按“显示宽度”(CJK=2)
    /// 对齐 —— 让 标签后的冒号、指令列、注释列逐行对齐。
    /// 只调整空白,不改变任何词元 → 汇编结果不变,可在编辑器上安全套用。
    /// </summary>
    public static class 语义列C
    {
        /// <summary> 显示宽度:ASCII 为 1,东亚字符(含中文)按 2 计 </summary>
        public static int 显示宽度(string 文本)
        {
            int 宽 = 0;
            if (文本 == null) return 宽;
            foreach (char c in 文本)
                宽 += (c > 0x2E7F) ? 2 : 1;   // 粗略:>U+2E7F 的 CJK/全角按 2
            return 宽;
        }

        private static string 空格(int n) => n > 0 ? new string(' ', n) : "";

        private static (string 标签, string 指令, string 操作数, string 注释) 拆列(string 行)
        {
            string 注释 = "";
            int 注释位置 = -1;
            for (int i = 0; i + 1 < 行.Length; i++)
            {
                if (行[i] == '/' && 行[i + 1] == '/') { 注释位置 = i; break; }
            }
            if (注释位置 >= 0)
            {
                注释 = 行[注释位置..].TrimEnd();
                行 = 行[..注释位置];
            }
            行 = 行.Trim();

            string 标签 = "", 指令 = "", 操作数 = "";
            string[] 词 = 行.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            int 起点 = 0;
            if (词.Length > 0 && 词[0].EndsWith(':'))
            {
                标签 = 词[0];
                起点 = 1;
            }
            if (词.Length > 起点)
            {
                指令 = 词[起点];
                操作数 = string.Join(" ", 词, 起点 + 1, Math.Max(0, 词.Length - 起点 - 1));
            }
            return (标签, 指令, 操作数, 注释);
        }

        private static string 重组(string 标签, string 指令, string 操作数, string 注释,
            int 标签列宽, int 注释列宽)
        {
            var 段 = new System.Text.StringBuilder();

            if (标签.Length > 0)
            {
                段.Append(标签);
                // 标签(含冒号)补齐到 标签列宽,使后续内容起点一致
                int 差 = 标签列宽 - 显示宽度(标签);
                if (差 > 0) 段.Append(' ', 差);
            }
            else if (标签列宽 > 0)
            {
                段.Append(' ', 标签列宽);   // 无标签行也空出同样宽度
            }

            段.Append(指令);
            if (操作数.Length > 0) 段.Append(' ').Append(操作数);

            if (注释.Length > 0)
            {
                int 已用 = 显示宽度(段.ToString());
                int 差 = 注释列宽 - 已用;
                if (差 < 1) 差 = 1;
                段.Append(' ', 差).Append(注释);
            }
            return 段.ToString().TrimEnd();
        }

        /// <summary>
        /// 对齐整段源码:第一遍统计列宽,第二遍重组各行。
        ///
        /// **T27(2026-09-27 评测整改):换行原样还回去。** 以前这里是 `Replace("\r\n","\n")` 进、
        /// 一路 `Append('\n')` 出,**全文没有任何还原** ⇒ 对一份 CRLF 文件做一次「语义对齐」再保存,
        /// 整个文件的行尾变成 LF、`git diff` 整篇翻(这是唯一会改坏用户文件字节的缺陷)。
        /// 现在:**每行带没带 `\r` 逐行记着**(归一化只用来算,不丢信息),重组时**一行一个地还回去**
        /// ⇒ 纯 CRLF 仍纯 CRLF、纯 LF 仍纯 LF、混合换行也**混得和原来一样**。
        /// `主换行` 是这份文稿整体按哪种算 CRLF(多数决),给"整篇重排"的调用方用。
        /// </summary>
        public static string 对齐文本(string 代码, out string 主换行)
        {
            if (string.IsNullOrEmpty(代码)) { 主换行 = "\n"; return 代码; }
            // 按行处理;行内保留 // 注释;多行 /* */ 暂不在对齐中跨行处理。
            // T27:只按 **\n** 切,行尾那个 `\r` **先别扔** —— 它就是"这一行原来是不是 CRLF"的凭据。
            var 行表 = 代码.Split('\n');
            // 第 i 行原来是不是 CRLF(下标 0..行表.Length-2;最后一段后面没有换行符了)
            bool[] 原为CRLF = new bool[Math.Max(0, 行表.Length - 1)];
            for (int i = 0; i < 行表.Length; i++)
            {
                string 行 = 行表[i];
                if (行.Length > 0 && 行[行.Length - 1] == '\r')
                {
                    行表[i] = 行.Substring(0, 行.Length - 1);   // 归一化:只用来算,下面一行一个还回去
                    if (i < 原为CRLF.Length) 原为CRLF[i] = true;
                }
            }
            int 数CRLF = 0;
            foreach (bool 是 in 原为CRLF) if (是) 数CRLF++;
            主换行 = (原为CRLF.Length > 0 && 数CRLF * 2 > 原为CRLF.Length) ? "\r\n" : "\n";

            var 行记录 = new (string 标签, string 指令, string 操作数, string 注释)[行表.Length];

            int 标签列宽 = 0;
            int 内容最大宽 = 0;
            for (int i = 0; i < 行表.Length; i++)
            {
                var 列 = 拆列(行表[i]);
                行记录[i] = 列;
                if (列.标签.Length > 0)
                    标签列宽 = Math.Max(标签列宽, 显示宽度(列.标签));
                int 内容宽 = 显示宽度(列.标签) + (列.标签.Length > 0 ? 1 : 0) +
                             显示宽度(列.指令) + (列.操作数.Length > 0 ? 1 + 显示宽度(列.操作数) : 0);
                内容最大宽 = Math.Max(内容最大宽, 内容宽);
            }

            var 输出 = new System.Text.StringBuilder();
            for (int i = 0; i < 行表.Length; i++)
            {
                var (标签, 指令, 操作数, 注释) = 行记录[i];
                if (指令.Length == 0 && 标签.Length == 0)
                {
                    // 空行/纯注释:原样
                    输出.Append(行表[i]);
                }
                else
                {
                    输出.Append(重组(标签, 指令, 操作数, 注释, 标签列宽, 内容最大宽 + 2));
                }
                // T27:换行按**这一行原来那种**还回去(最后一段没有换行符)
                if (i < 行表.Length - 1) 输出.Append(原为CRLF[i] ? "\r\n" : "\n");
            }
            return 输出.ToString();
        }

        /// <summary> 对齐整段源码(不关心"主换行"的调用方用这个重载) </summary>
        public static string 对齐文本(string 代码) => 对齐文本(代码, out _);
    }
}
