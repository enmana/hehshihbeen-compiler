namespace hehshihbeen_compiler
{
    /// <summary>
    /// 带结构信息的编译/展开异常:行号 + 中英双语消息,便于界面展示与定位。
    /// </summary>
    public class 编译异常C : Exception
    {
        /// <summary> 出错源行号(1 基),未知为 null </summary>
        public int? 行号 { get; }

        /// <summary> 中文消息 </summary>
        public string 中文 { get; }

        /// <summary> 英文消息(可为空) </summary>
        public string? 英文 { get; }

        /// <summary> 出错源行内容(便于展示,可为空) </summary>
        public string? 源行 { get; }

        /// <summary>
        /// 出错的文件全称(多文件编译时由汇编器补上;单个文件编译或未知时为 null)。
        /// 界面靠它把红线画到**真正出错的那个文件**上。
        /// </summary>
        public string? 文件 { get; }

        /// <summary>
        /// P23(第120批):**多错误报告**合成出来的那一条,把一条条子错挂在这儿(每条自带 行号/中文/文件)。
        /// 只有一处错时是 null(`合成多错` 原样抛那一条,文案与行号一个字不变);界面靠 `逐条()` 一条条列、一条条跳。
        /// </summary>
        public IReadOnlyList<编译异常C>? 子错 { get; set; }

        /// <summary> 把这条异常摊成"要一条条处理"的错误列表:多错给子错(已按行号排好),单错就是它自己 </summary>
        public IReadOnlyList<编译异常C> 逐条() => 子错 is { Count: > 0 } ? 子错 : [this];

        public 编译异常C(string 中文, string? 英文 = null, int? 行号 = null, string? 源行 = null, string? 文件 = null)
            : base(string.IsNullOrWhiteSpace(英文) ? 中文 : $"{中文} ({英文})")
        {
            this.中文 = 中文;
            this.英文 = 英文;
            this.行号 = 行号;
            this.源行 = 源行;
            this.文件 = 文件;
        }

        public 编译异常C(string 中文, Exception 内部, string? 英文 = null, int? 行号 = null)
            : base(string.IsNullOrWhiteSpace(英文) ? 中文 : $"{中文} ({英文})", 内部)
        {
            this.中文 = 中文;
            this.英文 = 英文;
            this.行号 = 行号;
        }
    }
}
