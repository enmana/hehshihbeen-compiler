namespace hehshihbeen_compiler
{
    /// <summary>
    /// 判断"某个位置在不在字符串/字符字面量/行注释里面"。
    /// 用途:编辑器补全与悬停提示 —— 里面的内容不是代码,**不该弹候选、也不该弹用法**
    /// (用户实测:在 `"..."` 里打一个点,方法候选照样弹出来)。
    /// 中文汇编里字符串不跨行(`\n` 走转义),所以只看光标所在那一行就够。
    /// 与 分词器C 同一套规矩:`//` 到行尾是注释,`"`/`'` 里认 `\` 转义。
    /// </summary>
    public static class 文本上下文C
    {
        /// <summary> 位置(0 起的偏移,通常就是光标)是不是落在字面量或注释里 </summary>
        public static bool 在字面量或注释里(string? 文本, int 位置)
        {
            if (string.IsNullOrEmpty(文本)) return false;
            int 位置夹 = Math.Max(0, Math.Min(位置, 文本.Length));
            int 行首 = 文本.LastIndexOf('\n', Math.Max(0, 位置夹 - 1));
            行首 = 行首 < 0 ? 0 : 行首 + 1;
            bool 在串 = false, 在字符 = false;
            for (int i = 行首; i < 位置夹 && i < 文本.Length; i++)
            {
                char c = 文本[i];
                if (在串)
                {
                    if (c == '\\') i++;                     // 转义:下一个字符不算引号
                    else if (c == '"') 在串 = false;
                }
                else if (在字符)
                {
                    if (c == '\\') i++;
                    else if (c == '\'') 在字符 = false;
                }
                else if (c == '"') 在串 = true;
                else if (c == '\'') 在字符 = true;
                else if (c == '/' && i + 1 < 文本.Length && 文本[i + 1] == '/') return true;   // 后面全是注释
            }
            return 在串 || 在字符;
        }
    }
}
