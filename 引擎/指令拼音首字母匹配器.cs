using ToolGood.Words.FirstPinyin;
using ToolGood.Words.FirstPinyin.internals;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 提供中文指令的拼音首字母匹配功能，便于快速输入
    /// </summary>
    public static class 指令拼音首字母匹配器
    {
        // 缓存指令名 -> 大写首字母序列
        private static readonly Dictionary<string, string> 指令名首字母映射;
        static 指令拼音首字母匹配器()
        {
            // 获取所有中文指令名（指令码C需提供该方法）
            var 中文指令名列表 = 指令C.取全部中文指令名();
            指令名首字母映射 = 中文指令名列表.ToDictionary(
                name => name,
                name => WordsHelper.GetFirstPinyin(name).ToUpper()  // 生成大写首字母
            );
        }

        /// <summary>
        /// 根据输入的首字母序列，返回所有匹配的中文指令名列表（前缀匹配）
        /// </summary>
        public static List<string> 匹配指令(string 输入)
        {
            if (string.IsNullOrWhiteSpace(输入)) return [];

            string 输入大写 = 输入.ToUpper().Trim();
            return [.. 指令名首字母映射.Where(kvp => kvp.Value.StartsWith(输入大写)).Select(kvp => kvp.Key)];
        }

        /// <summary>
        /// 精确匹配（输入必须与完整首字母序列一致）
        /// </summary>
        public static string? 精确匹配(string 输入)
        {
            if (string.IsNullOrWhiteSpace(输入))
                return null;

            string 输入大写 = 输入.ToUpper().Trim();
            var 匹配项 = 指令名首字母映射.FirstOrDefault(kvp => kvp.Value == 输入大写);
            return 匹配项.Key;
        }

        /// <summary>
        /// 获取所有指令名及其首字母序列（用于调试或展示）
        /// </summary>
        public static IReadOnlyDictionary<string, string> 获取所有映射() => 指令名首字母映射;
    }
}