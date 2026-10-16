namespace hehshihbeen_compiler
{
    /// <summary>
    /// 寄存器文件的唯一事实源:R0~R15(数量、名称、解析)。
    /// 虚拟机/汇编器/中间层/补全 UI 一律经这里,不再各自写死 8/16 或维护一份寄存器表。
    /// </summary>
    public static class 寄存器C
    {
        /// <summary> 寄存器数量(编码字段 4 位,最多 16) </summary>
        public const int 数量 = 16;

        /// <summary>
        /// 编译器保留的帧指针寄存器号(FP = R15):`取字帧/存字帧/取地址帧` 三条指令的基准。
        /// 放在这里(而不是指令表里)是因为**虚拟机也要用它**,而 HB 宿主只链接虚拟机、不链接汇编器。
        /// </summary>
        public const int 帧指针号 = 15;

        /// <summary> 取第 编号 个寄存器的规范名 R0..R15(0 基) </summary>
        public static string 名称(int 编号) =>
            编号 is >= 0 and < 数量 ? "R" + 编号 : throw new ArgumentOutOfRangeException(nameof(编号), $"寄存器编号须在 0..{数量 - 1}");

        /// <summary> 大小写不敏感解析 R0~R15(接受 R8、R15);成功返回 true 并给出编号 </summary>
        public static bool 解析(string 文本, out int 编号)
        {
            编号 = 0;
            string 上 = 文本.Trim().ToUpperInvariant();
            if (上.Length < 2 || 上[0] != 'R') return false;
            if (!int.TryParse(上.AsSpan(1), out int n)) return false;
            if (n is < 0 or >= 数量) return false;
            编号 = n;
            return true;
        }
    }
}
