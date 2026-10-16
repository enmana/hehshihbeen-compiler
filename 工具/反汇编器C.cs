using System;
using System.Text;

namespace hehshihbeen_compiler.工具
{
    /// <summary>
    /// 反汇编窗用的"整段机器码 → 文本"包装:只补 `地址 + 4 字节机器码` 这两列,**解码本身一律走引擎**
    /// (`引擎\反汇编C.cs` → `指令C.取指令中文名` / `取格式` / `取指令英文名`)。
    ///
    /// **R4-7(2026-09-22)**:这里以前挂着**第二份解码表** —— 只登记 0x00~0x1A 共 27 条,`0x1B` 往后一律打成
    /// `OP_0x24 raw: …`(MUL/DIV/MOD/IN/OUT/FP 相对寻址/立即数移位/比较取值/断点/宿主调用/索引寻址全在里面),
    /// 而且同一个操作码两份表还**已经漂移**(一个打十六进制、一个打十进制)。那份表整份删掉:
    /// 指令名、编码格式、中英名只从 `指令C.全部指令` 来,新增一条 ISA 指令这里**自动跟上**,不用再改第二处。
    /// 助记符仍按**英文名**显示(`ADD` / `LOAD_WORD_INDEX` 等),这是改动以前反汇编窗的既有观感,本批**不动**。
    /// </summary>
    public static class 反汇编器C
    {
        /// <summary> 把整段机器码逐条反汇编成文本(每行:`0x地址: 4 字节机器码    助记符 操作数`) </summary>
        public static string 反汇编(byte[] 机器码)
        {
            if (机器码 == null) throw new ArgumentNullException(nameof(机器码));
            var sb = new StringBuilder();
            for (int pc = 0; pc + 3 < 机器码.Length; pc += 4)
            {
                string bytes = BitConverter.ToString(机器码, pc, 4).Replace('-', ' ');
                string 指令 = 引擎那一行(机器码, (uint)pc);
                sb.AppendLine($"0x{pc:X4}: {bytes}    {指令}".TrimEnd());
            }
            return sb.ToString();
        }

        /// <summary>
        /// 一条指令的"助记符 + 操作数"文本(机器码那一列不在这层):先拿引擎的**中文助记符 + 操作数**
        /// (`反汇编C.反汇编一条`,未知操作码得到 null),再换成这条指令的**英文名**。换名只查 `指令C.取指令英文名`,
        /// 不在这儿另抄一份名字表。`0x37` 这种没登记的操作码照旧如实打出来,不装成一条指令。
        /// </summary>
        private static string 引擎那一行(byte[] 机器码, uint 地址)
        {
            // 地址不合法 ⇒ 挂在这里的那串字节连"一条指令"都算不上,如实打出来(和引擎那边"越界返回 null"一个口径)
            if (机器码 == null || 地址 + 4 > (uint)机器码.Length) return "(机器码越界)";

            string? 行 = 反汇编C.反汇编一条(机器码, 地址, out _);
            byte 操作码 = 机器码[地址 + 3];
            if (行 == null) return $"未知操作码 {操作码:X2}(这条不在指令表里)";

            string? 英文 = 指令C.取指令英文名(操作码);
            if (英文 == null) return 行;

            string? 中文 = 指令C.取指令中文名(操作码);
            // `反汇编一条` 保证行首就是中文名;去掉它,再把英文名接上(两者都是同一条指令的名字)
            return 中文 != null && 行.StartsWith(中文, StringComparison.Ordinal)
                ? 英文 + 行[中文.Length..]
                : 行;
        }
    }
}
