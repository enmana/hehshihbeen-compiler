// ============================================================
// 内存缩略C —— 运行完之后,把**整块运行内存**缩成一张看得懂的图(R1-3 的"看得见"那一半)
//
//   和 `内存布局C` 的分工:`内存布局C` 只算**段的边界**(静态账,编译期就定);
//   这里读的是**跑完之后内存里真的有东西的地方**(运行期账):每段填了多少、峰值到哪、
//   非零最密的地方在哪、每一段头一页的原样字节。
//
//   三步:
//     ① **段表**照 `内存布局C.算表` 拿(不另立一份口径);
//     ② 每段扫一遍(段超过 64KB 时按每 4 个字节抽 1 个,省时间且不影响"有没有东西"的判断)——
//        得"非零字 / 扫描数 / 峰值地址";
//     ③ 每段再按 256 字节一格数一遍,取出最密的几格当热点;头一页按 32 字一行打出来。
//
//   ⚠ **故意不 dump 变量槽**:那是调用方的顶层变量,名字在调试文件表里、值在槽里,
//   真要一个个对名字请走 IDE 的"变量"窗格(它读同一张表)。这里只按段给账。
// ============================================================
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary> 运行内存的缩略图(文本;命令行 `--内存` 与 IDE 想看同一份口径时都读它) </summary>
    public static class 内存缩略C
    {
        private const int 头页字 = 32;            // 每行几个 32 位字
        private const int 头页行 = 4;             // 每段打几行
        private const int 每格字节 = 256;         // 热点按 256 字节一格数

        /// <summary>
        /// 出一份缩略图。<paramref name="要原始头页"/> = 每段头一页的原始字要不要打出来。
        /// </summary>
        public static string 生成(汇编调试结果C 编译, byte[] 内存, bool 要原始头页 = true)
        {
            var 表 = 内存布局C.算表(编译, 内存.Length);
            var 文 = new StringBuilder();

            文.AppendLine($"运行内存 {内存.Length:N0} 字节 —— 每段一行:大小 / 占用条 / 非零字占比 / 峰值");
            文.AppendLine("  段名                     起        止        大小      占用(整段)   非零字     占比   峰值");
            文.AppendLine("  ───────────────────────  ────────  ────────  ──────────  ───────────  ─────────  ─────  ────────");

            foreach (var 区 in 表)
            {
                long 扫过的 = 0, 非零 = 0, 峰值 = 0;
                int 步 = 区.大小 > 65536 ? 4 : 1;                    // 大段抽着扫
                for (long 位 = 区.起始; 位 + 4 <= 区.起始 + 区.大小; 位 += 4L * 步)
                {
                    扫过的++;
                    if (读字(内存, 位) != 0) { 非零++; 峰值 = 位; }
                }
                double 比 = 扫过的 == 0 ? 0 : (double)非零 / 扫过的;
                int 格 = (int)Math.Round(比 * 11);
                string 条 = new string('█', 格) + new string('·', 11 - 格);
                文.AppendLine($"  {区.区名,-23}  0x{区.起始:X6}  0x{区.末尾:X6}  {区.大小,10:N0}  {条}  {非零,9:N0}  {比,5:P0}  " +
                              (峰值 > 0 ? $"0x{峰值:X6}" : "—"));
            }

            // ---- 热点:哪些 256 字节格里东西最多(代码段跳过 —— 它本来满;每段最多留 3 格) ----
            文.AppendLine();
            文.AppendLine("热点(每 256 字节一格;代码段天生是满的所以跳过,每段最多列 3 格):");
            var 热点 = new List<(long 位, int 数, string 段)>();
            foreach (var 区 in 表)
            {
                if (区.区名 is "空闲" or "代码段" or "软栈垫子") continue;
                var 本段 = new List<(long 位, int 数)>();
                for (long 位 = 区.起始; 位 + 每格字节 <= 区.起始 + 区.大小; 位 += 每格字节)
                {
                    int 数 = 0;
                    for (long k = 位; k + 4 <= 位 + 每格字节; k += 4)
                        if (读字(内存, k) != 0) 数++;
                    if (数 > 0) 本段.Add((位, 数));
                }
                foreach (var x in 本段.OrderByDescending(y => y.数).ThenBy(y => y.位).Take(3))
                    热点.Add((x.位, x.数, 区.区名));
            }
            if (热点.Count == 0) 文.AppendLine("  (除代码段以外一处非零都没有)");
            foreach (var (位, 数, 段名) in 热点.OrderBy(x => x.位))
                文.AppendLine($"  0x{位:X6}  {数,3}/{每格字节 / 4} 字非零  {段名}");

            // ---- 每段头一页 ----
            if (!要原始头页) return 文.ToString();
            文.AppendLine();
            文.AppendLine($"每段头一页(每行 {头页字} 个 32 位字,大端读法;`·` = 全零的一行跳过):");
            foreach (var 区 in 表)
            {
                if (区.区名 == "空闲" || 区.区名 == "硬件栈") continue;   // 空闲没东西、硬件栈在跑的时候才动
                文.AppendLine($"  ── {区.区名} (0x{区.起始:X6} 起) ──");
                int 打的 = 0;
                for (int 行号 = 0; 行号 < 头页行; 行号++)
                {
                    long 行起 = 区.起始 + (long)行号 * 头页字 * 4;
                    if (行起 + 4 > 区.起始 + 区.大小) break;
                    bool 全零 = true;
                    var 一 = new StringBuilder();
                    for (int k = 0; k < 头页字; k++)
                    {
                        long 位 = 行起 + (long)k * 4;
                        if (位 + 4 > 区.起始 + 区.大小) break;
                        uint 字 = 读字(内存, 位);
                        if (字 != 0) 全零 = false;
                        一.Append($"{字:X8} ");
                    }
                    if (全零) continue;
                    文.AppendLine($"  0x{行起:X6}  {一.ToString().TrimEnd()}");
                    打的++;
                }
                if (打的 == 0) 文.AppendLine("  (头一页全是零)");
            }
            return 文.ToString();
        }

        private static uint 读字(byte[] 内存, long 位)
        {
            if (位 < 0 || 位 + 4 > 内存.Length) return 0;
            return (uint)((内存[位] << 24) | (内存[位 + 1] << 16) | (内存[位 + 2] << 8) | 内存[位 + 3]);
        }
    }
}
