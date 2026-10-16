namespace hehshihbeen_compiler
{
    /// <summary>
    /// **虚拟机测速的那一轮**(T30②,2026-09-27 评测整改)。为什么单列一个类:
    ///   · `hbcc --bench` 要它(报"每秒多少条指令",确认手上这份是 Release 还是 Debug);
    ///   · 综合自检也要它("两档规模的读数比值 ≤1.5"这条判据必须**真跑**,不能按源码钉充数)——
    ///     而自检那几份文件在 **WPF 主程序里也一起编**(主程序里没有 `Program.cs`),
    ///     所以这一层不能留在 CLI 的 `Program` 里(实测:放那儿主程序编不过)。
    ///
    /// 口径:每轮**新编一次、装一台新虚拟机**(与老实现一致,量的是"整轮"),单步到底或到上限为止。
    /// </summary>
    public static class 虚拟机测速C
    {
        /// <summary> 单轮最多走这么多条指令(死循环护栏;正常自增循环远到不了) </summary>
        public const long 步数上限 = 2_000_000_000;

        /// <summary> 编一个 `var i = 0; 当 (i &lt; N) { i = i + 1; } 停止` 并跑到底,给出"走了几条 / 用了几秒" </summary>
        public static void 跑一轮(int 循环次数, out long 走了, out double 秒)
        {
            string 源 = $"var i = 0;\n当 (i < {循环次数}) {{ i = i + 1; }}\n停止";
            var 编译结果 = new 汇编器C().汇编多文件带调试([("主程序.hb", 源)]);
            var 机 = new 虚拟机C();
            机.装载(编译结果);
            var 表 = System.Diagnostics.Stopwatch.StartNew();
            走了 = 0;
            while (!机.已结束 && 走了 < 步数上限)
            {
                机.单步();
                走了++;
            }
            表.Stop();
            秒 = System.Math.Max(表.Elapsed.TotalSeconds, 0.000001);
        }
    }
}
