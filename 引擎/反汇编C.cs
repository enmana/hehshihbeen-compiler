namespace hehshihbeen_compiler
{
    /// <summary>
    /// 反汇编器:把一条 4 字节指令还原成文本(供单步调试显示)。输出中文助记符。
    /// </summary>
    public static class 反汇编C
    {
        /// <summary> 反汇编 pc 处的一条指令;pc 越界或未知操作码返回 null </summary>
        public static string? 反汇编一条(byte[] 码, uint pc, out int 长度)
        {
            长度 = 4;
            if (码 == null || pc + 4 > (uint)码.Length) { 长度 = 0; return null; }

            uint 指令 = (uint)(码[pc] | (码[pc + 1] << 8) | (码[pc + 2] << 16) | (码[pc + 3] << 24));
            byte 操作码 = (byte)(指令 >> 24);
            string? 名 = 指令C.取指令中文名(操作码);
            if (名 == null) return null;

            string R(byte n) => $"R{n}";
            byte 目标 = (byte)((指令 >> 20) & 0xF);
            byte 源 = (byte)((指令 >> 16) & 0xF);
            uint 立即数20 = 指令 & 0xFFFFF;
            uint 地址 = (指令 & 0xFFFFFF) << 2;

            switch (指令C.取格式(操作码))
            {
                case 指令格式.无操作数:
                    return 名;

                case 指令格式.单寄存器:
                    return $"{名} {R(目标)}";

                case 指令格式.双寄存器:
                    return $"{名} {R(目标)}, {R(源)}";

                case 指令格式.寄存器地址:   // 显示为字节地址(0x)
                    return $"{名} {R(目标)}, 0x{立即数20:X}";

                case 指令格式.寄存器立即数: // 显示为十进制立即数
                    return $"{名} {R(目标)}, {立即数20}";

                case 指令格式.跳转地址:     // 低24位 <<2
                    return $"{名} 0x{地址:X}";

                case 指令格式.双寄存器小立即数:   // 第 4 个操作数是 4 位小立即数
                    return $"{名} {R(目标)}, {R(源)}, {(指令 >> 12) & 0xF}";

                case 指令格式.三寄存器小立即数:   // 基址, 下标, 值, 缩放(A3 起:存字索引)
                    return $"{名} {R(目标)}, {R(源)}, {R((byte)((指令 >> 12) & 0xF))}, {(指令 >> 8) & 0xF}";

                default:
                    return null;
            }
        }

        /// <summary> 从 pc 开始反汇编若干条(用于调试窗口展示附近指令) </summary>
        public static List<string> 反汇编附近(byte[] 码, uint pc, int 前几条, int 后几条)
        {
            var 结果 = new List<string>();
            if (码 == null) return 结果;
            long 起点 = Math.Max(0, (long)pc - (long)(前几条 * 4));
            long 终点 = Math.Min(码.Length - 4L, (long)pc + (long)(后几条 * 4));
            for (long addr = 起点; addr <= 终点; addr += 4)
            {
                string? 行 = 反汇编一条(码, (uint)addr, out _);
                string 标记 = (uint)addr == pc ? "▶" : "  ";
                结果.Add($"{标记}0x{addr:X4}: {行 ?? "(未知指令)"}");
            }
            return 结果;
        }
    }
}
