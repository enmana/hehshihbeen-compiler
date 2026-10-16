using hehshihbeen_compiler;

namespace hehshihbeen_compiler.图形宿主
{
    /// <summary>
    /// 桌面壳自己的入口:**只用来人工观感 / 装机体检** —— 开一次真窗口、画一帧、到时自己关掉。
    ///
    /// 自检**不跑它**(硬规矩 6:不许拿开窗当验证;自检走的是无头呈现)。
    /// HB 程序那条路不经过这里:由 `HB宿主` 挂上 `桌面壳C`(见 `HB宿主\Program.cs`)。
    /// 用法:`图形宿主.exe --自测 [毫秒]`(默认 900 毫秒)。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] 参数)
        {
            // `图形宿主 --压帧 每帧条数 帧数`:只量"真窗口那条路的帧率"(即时模式:每帧整份重画)。
            // 拍板要的那个数(全量重绘够不够)就出在这儿;不进自检(硬规矩 6 只禁"拿开窗当验证")。
            if (参数.Length >= 3 && 参数[0] == "--压帧"
                && int.TryParse(参数[1], out int 每帧条数) && int.TryParse(参数[2], out int 帧数))
                return 量帧率(每帧条数, 帧数);

            int 毫秒 = 900;
            for (int i = 0; i + 1 < 参数.Length; i++)
                if (参数[i] == "--自测" && int.TryParse(参数[i + 1], out int n)) 毫秒 = n;

            var 壳 = new 桌面壳C();
            try
            {
                壳.打开窗口("HB 图形宿主 —— 空窗体检", 320, 200);
                壳.呈现(
                [
                    new 绘制命令C { 动作 = "清屏", 颜色 = 0xFF101018u },
                    new 绘制命令C { 动作 = "画矩形", x = 10, y = 10, 宽 = 120, 高 = 40, 颜色 = 0xFF2A6EF0u },
                    new 绘制命令C { 动作 = "画文字", x = 16, y = 24, 文本 = "你好,HB", 颜色 = 0xFFFFFFFFu },
                    new 绘制命令C { 动作 = "画线", x = 0, y = 60, x2 = 319, y2 = 60, 颜色 = 0xFF3C3C46u },
                ]);
                Console.WriteLine($"图形宿主 空窗体检:窗口开出(320x200)、一帧已提交;{毫秒} 毫秒后自动关闭");
                Console.Out.Flush();
                壳.等待(毫秒);
                壳.关闭窗口();
                Console.WriteLine("图形宿主 空窗体检:窗口已关闭");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("图形宿主 空窗体检:开不出来 —— " + ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// 真窗口那条路的帧率实测:每帧 `每帧条数` 条矩形,连着提交 `帧数` 帧,报"帧/秒 + 万条命令/秒"。
        /// **只量不判**:数字给"全量重绘还是脏矩形"当依据(批 1 的定案要的就是这个数)。
        /// </summary>
        private static int 量帧率(int 每帧条数, int 帧数)
        {
            var 壳 = new 桌面壳C();
            try
            {
                壳.打开窗口("HB 图形宿主 —— 帧率实测", 320, 200);
                var 命令 = new 绘制命令C[每帧条数];
                for (int i = 0; i < 每帧条数; i++)
                    命令[i] = new 绘制命令C
                    {
                        动作 = "画矩形",
                        x = i % 300, y = i % 180, 宽 = 6, 高 = 6,
                        颜色 = (uint)(0xFF000000u | (uint)((i * 2654435761u) & 0xFFFFFFu)),
                    };
                var 秒表 = System.Diagnostics.Stopwatch.StartNew();
                for (int f = 0; f < 帧数; f++)
                {
                    壳.呈现(命令);
                    壳.等待(1);      // 让界面线程喘一口(即时模式留一档给输入处理的真实节拍)
                }
                秒表.Stop();
                壳.关闭窗口();
                double 秒 = Math.Max(0.001, 秒表.Elapsed.TotalSeconds);
                Console.WriteLine($"图形宿主 帧率实测:{每帧条数} 条命令 × {帧数} 帧 = {每帧条数 * 帧数} 条," +
                    $"耗时 {秒表.Elapsed.TotalMilliseconds:F0} ms;折合 {帧数 / 秒:F0} 帧/秒、" +
                    $"{每帧条数 * 帧数 / 秒 / 10000.0:F1} 万条命令/秒(提交 = 一次 Dispatcher 往返,不含真正的 Skia 出图)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("图形宿主 帧率实测:开不出来 —— " + ex.Message);
                return 1;
            }
        }
    }
}
