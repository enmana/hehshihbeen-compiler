using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>一次"整机状态对拍"的结论:过了没有 + 给人看的一句话(差异说在哪儿)</summary>
    public sealed class C状态对比C
    {
        public bool 一致;
        public string 说明 = "";
    }

    /// <summary>
    /// **整机状态的一份快照**(C# 虚拟机那一侧导出的、或从 C 侧转储文件里读回来的)。
    /// 字段与 `C后端\运行时.c` 的 `hb_dump_state` 里那些一一对应。
    /// </summary>
    public sealed class C状态快照C
    {
        /// <summary>程序计数器(跑完那一刻;两边约定不同,对拍比的是 <see cref="上一条指令地址"/>)</summary>
        public uint PC;
        /// <summary>
        /// **最后一条真执行过的指令地址**(对拍比的就是它):
        /// 虚拟机侧 = `虚拟机C.上一条指令地址`;C 侧 = `hb_pc`(它本来就停在那条指令上)。
        /// </summary>
        public uint 上一条指令地址;
        /// <summary>**执行过的指令条数**(两边必须一样 —— 条数不同就是走的不是同一条路)</summary>
        public uint 执行条数;
        /// <summary>最近执行过的指令地址(从旧到新,最多 64 条;C 侧的 `T` 行)</summary>
        public List<uint> 足迹 = [];
        /// <summary>运行内存字节数(两边必须一样)</summary>
        public uint 内存大小;
        /// <summary>静态数据段字节数(编译器报的那个数,两边同口径)</summary>
        public uint 数据字节数;
        /// <summary>代码段字节数</summary>
        public uint 代码字节数;
        /// <summary>R0~R15</summary>
        public List<uint> 寄存器 = [];
        /// <summary>整块运行内存</summary>
        public byte[] 内存 = [];
        /// <summary>程序输出的字节(虚拟机侧由调用方收;C 侧由调用方从 stdout 收)</summary>
        public byte[] 输出 = [];
        /// <summary>退出码(虚拟机侧正常停机 0;C 侧取进程退出码)</summary>
        public int 退出码;
        /// <summary>运行期错误说明(空 = 没出错)</summary>
        public string 报错 = "";
    }

    /// <summary>
    /// R3-6-4 批 2:**C# 虚拟机**与**C 后端编出的原生 exe** 的整机状态对拍。
    ///
    /// 为什么单做这一层:批 1 只比了**程序输出**——那只等于比了个尾巴。
    /// 两份实现真漂开的地方(某条指令的语义、某个地址算错)往往**不影响输出**,只在寄存器 / 内存里露出来。
    /// 所以两边跑完都落一份**同格式**的状态转储:
    ///   · C# 侧:`虚拟机C.导出状态`(自检直接把 `TextWriter` 接到 `StringBuilder` 上,不落盘);
    ///   · C 侧:`C后端\运行时.c` 的 `hb_dump_state`(由命令行参数 `--hb-dump <路径>` 指路径,不给就一个字节都不写)。
    ///
    /// 比三样(与"全量对拍"的口径对齐):
    ///   ① **寄存器 R0~R15** 逐项;
    ///   ② **整块内存**逐字节(代码段 + 静态数据段 + 隐藏区全在里面);
    ///   ③ **程序输出**逐字节 + 退出码。
    ///
    /// 两处**有意不比**(不比才诚实,比了只会制造假差异):
    ///   · **`PC` 那一行**:虚拟机跑到 `停止` 时 PC 已经 +4,C 侧停在 `停止` 那一条上 ——
    ///     两边口径本来就差一条指令。真正比的是 `LAST_ADDR`(最后一条**真执行过**的指令),
    ///     那个必须完全相等;
    ///   · **SP**:C 侧把 `压栈/弹栈` 编译成了 C 自己的调用栈,HB 内存末尾那 4 个字节一个字没动过,
    ///     `SP` 不是一个东西。内存里真被用过的那一段照旧逐字节比。
    /// </summary>
    public static class C状态C
    {
        /// <summary>
        /// 虚拟机那边跑完之后的整机状态(全在内存里,不落盘):四个头字段 + 16 个寄存器 + 整块内存。
        /// `机` 必须是**已经跑完**的那一台(`装载` 之后跑过)。
        /// </summary>
        public static C状态快照C 导出虚拟机状态(虚拟机C 机) =>
            导出虚拟机状态(机, new MemoryStream(), 0, 0);

        /// <summary>同上,但连程序输出的字节一起收(自检那条"输出也逐字节比"的路走它)</summary>
        public static C状态快照C 导出虚拟机状态(虚拟机C 机, Stream 输出流, int 代码字节数, int 数据字节数)
        {
            ArgumentNullException.ThrowIfNull(机);
            ArgumentNullException.ThrowIfNull(输出流);
            var 文 = new StringWriter(CultureInfo.InvariantCulture);
            机.导出状态(文, 代码字节数, 数据字节数);
            var 状态 = 解析(文.ToString());
            输出流.Position = 0;
            var 出 = new byte[输出流.Length];
            int 读 = 0;
            while (读 < 出.Length)
            {
                int 这 = 输出流.Read(出, 读, 出.Length - 读);
                if (这 <= 0) break;
                读 += 这;
            }
            状态.输出 = 出;
            return 状态;
        }

        /// <summary>把 C 侧落的那份转储文件读进来(不存在 / 读不动 → null,调用方如实记账)</summary>
        public static C状态快照C? 读原生状态(string 路径)
        {
            if (!File.Exists(路径)) return null;
            try { return 解析(File.ReadAllText(路径, Encoding.ASCII)); }
            catch { return null; }
        }

        /// <summary>
        /// 两份状态逐项比。`原生输出` / `原生退出码` 由调用方从原生 exe 那边收(C 侧转储里没有这两样)。
        ///
        /// `不比条数` = **"这一份程序走了多少条指令"本来就不是个确定数**,于是"最后执行的指令 / 执行条数 /
        /// 最近路径"这三项**不参与比对**(寄存器 / 内存 / 输出 / 退出码照旧逐字节比)。
        /// G3 域 5 的异步标准工程 `示例31_异步` 就走这一档:它靠
        /// `当 (任务就绪(…) == 0) { }` 等后台线程,**问了几轮由线程快慢决定** ——
        /// 拿它当判据是假判据(与 `读一块` 的短读那一格同一类)。**别的工程一律用缺省值 `false`。**
        /// </summary>
        public static C状态对比C 比(C状态快照C 虚拟机, C状态快照C 原生, byte[] 原生输出, int 原生退出码,
            bool 不比条数 = false)
        {
            ArgumentNullException.ThrowIfNull(虚拟机);
            ArgumentNullException.ThrowIfNull(原生);
            var 差 = new List<string>();

            if (虚拟机.内存大小 != 原生.内存大小)
                差.Add($"内存大小 虚拟机 {虚拟机.内存大小} / 原生 {原生.内存大小}");
            else
            {
                if (虚拟机.寄存器.Count != 原生.寄存器.Count)
                    差.Add($"寄存器个数 虚拟机 {虚拟机.寄存器.Count} / 原生 {原生.寄存器.Count}");
                else
                    for (int i = 0; i < 虚拟机.寄存器.Count && 差.Count < 3; i++)
                        if (虚拟机.寄存器[i] != 原生.寄存器[i])
                            差.Add($"R{i} 虚拟机 0x{虚拟机.寄存器[i]:X8} / 原生 0x{原生.寄存器[i]:X8}");

                if (!不比条数)
                {
                    // 最后停在**同一条指令**上:两边都报 `LAST_ADDR`(虚拟机 = 上一条真执行过的,
                    // C 侧 = `hb_pc`,它本来就停在那条指令上)⇒ 必须**完全相等**。
                    // 只比"PC 那一行"是不够的:虚拟机跑到 `停止` 时 PC 已经 +4,C 侧停在 `停止` 那一条上,
                    // 两边相差 4 是口径差异、不是分岔(所以这里不拿 PC 当判据)。
                    if (虚拟机.上一条指令地址 != 原生.上一条指令地址)
                        差.Add($"最后执行的指令 虚拟机 0x{虚拟机.上一条指令地址:X} / 原生 0x{原生.上一条指令地址:X}");
                    if (虚拟机.执行条数 != 原生.执行条数)
                        差.Add($"执行过的指令条数 虚拟机 {虚拟机.执行条数} / 原生 {原生.执行条数}" +
                            "(条数不同 = 两份实现走的不是同一条路)");
                    else if (虚拟机.足迹.Count > 0 && 原生.足迹.Count > 0)
                    {
                        // 条数一样也可能"换了路又走回同一处":比最近这几十条指令地址,尾巴一对就看得出来
                        int 同 = 0;
                        while (同 < 虚拟机.足迹.Count && 同 < 原生.足迹.Count
                            && 虚拟机.足迹[虚拟机.足迹.Count - 1 - 同] == 原生.足迹[原生.足迹.Count - 1 - 同]) 同++;
                        if (同 < Math.Min(虚拟机.足迹.Count, 原生.足迹.Count))
                            差.Add($"最近路径在倒数第 {同 + 1} 条上分岔:虚拟机 0x{虚拟机.足迹[虚拟机.足迹.Count - 1 - 同]:X} / " +
                                $"原生 0x{原生.足迹[原生.足迹.Count - 1 - 同]:X}");
                    }
                }

                if (虚拟机.内存.Length != 原生.内存.Length)
                    差.Add($"内存字节数 虚拟机 {虚拟机.内存.Length} / 原生 {原生.内存.Length}");                else
                {
                    for (int i = 0; i < 虚拟机.内存.Length; i++)
                        if (虚拟机.内存[i] != 原生.内存[i])
                        {
                            差.Add($"内存第一个不同在 0x{i:X}(虚拟机 0x{虚拟机.内存[i]:X2} / 原生 0x{原生.内存[i]:X2})");
                            break;
                        }
                }
            }

            if (!虚拟机.输出.AsSpan().SequenceEqual(原生输出))
            {
                int 头一个 = 0;
                while (头一个 < Math.Min(虚拟机.输出.Length, 原生输出.Length) && 虚拟机.输出[头一个] == 原生输出[头一个]) 头一个++;
                差.Add($"程序输出 虚拟机 {虚拟机.输出.Length} 字节 / 原生 {原生输出.Length} 字节," +
                    $"第一个不同在第 {头一个} 字节");
            }
            if (虚拟机.退出码 != 原生退出码)
                差.Add($"退出码 虚拟机 {虚拟机.退出码} / 原生 {原生退出码}");

            return new C状态对比C { 一致 = 差.Count == 0, 说明 = string.Join(";", 差) };
        }
        // ───────────────────────── 解析(两侧同一份格式) ─────────────────────────

        private static C状态快照C 解析(string 文本)
        {
            var 状态 = new C状态快照C();
            var 内存 = new List<byte>();
            bool 数字段 = false;
            foreach (string 原行 in 文本.Replace("\r\n", "\n").Split('\n'))
            {
                string 行 = 原行.Trim();
                if (行.Length == 0) continue;
                if (!数字段 && 行.StartsWith('R') && 行.Length > 2 && char.IsDigit(行[1]))
                {
                    状态.寄存器.Add(取值(行.AsSpan(行.IndexOf(' ') + 1)));
                    continue;
                }
                int 空 = 行.IndexOf(' ');
                string 标签 = 空 < 0 ? 行 : 行[..空];
                switch (标签)
                {
                    case "PC": 状态.PC = 取值(行.AsSpan(空 + 1)); continue;
                    case "LAST_ADDR": 状态.上一条指令地址 = 取值(行.AsSpan(空 + 1)); continue;
                    case "STEPS": 状态.执行条数 = 取值(行.AsSpan(空 + 1)); continue;
                    case "T": 状态.足迹.Add(取值(行.AsSpan(空 + 1))); continue;
                    case "MEM": 状态.内存大小 = 取值(行.AsSpan(空 + 1)); continue;
                    case "DATA_BYTES": 状态.数据字节数 = 取值(行.AsSpan(空 + 1)); continue;
                    case "CODE_BYTES": 状态.代码字节数 = 取值(行.AsSpan(空 + 1)); continue;
                }
                // 前面都不是 ⇒ 这就是内存体(每行 64 个十六进制字符)
                数字段 = true;
                for (int i = 0; i + 1 < 行.Length; i += 2) 内存.Add(字节(行, i));
            }
            状态.内存 = 内存.ToArray();
            return 状态;
        }

        private static uint 取值(ReadOnlySpan<char> 十六进制) =>
            uint.Parse(十六进制, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        private static byte 字节(string 行, int 起)
        {
            int 高 = 数(行[起]);
            int 低 = 数(行[起 + 1]);
            return (byte)((高 << 4) | 低);
        }

        private static int 数(char 字) => 字 switch
        {
            >= '0' and <= '9' => 字 - '0',
            >= 'A' and <= 'F' => 字 - 'A' + 10,
            >= 'a' and <= 'f' => 字 - 'a' + 10,
            _ => 0,
        };
    }
}
