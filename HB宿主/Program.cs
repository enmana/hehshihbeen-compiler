using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// HB 程序宿主:自己读自己的尾巴,把贴着的机器码装进虚拟机跑起来。
    ///
    /// 尾巴格式(由 `hbcc --exe 程序.hb -o 程序.exe` 生成,全部小端):
    ///   [宿主 exe 本体][机器码 N 字节][消息区 M 字节][N:u32][M:u32][错误槽地址:u32][消息条数:u16]["HBEX"]
    /// 消息区每条 = u16 长度 + UTF-8 字节(运行期报错的说明;比如"下标越界…")。
    /// 于是"编出来的程序"就是**一个 exe**:双击就跑,不需要 IDE、不需要 hbcc;
    /// 用自包含发布时连 .NET 都不用装。没贴机器码(直接跑宿主本体)时打一行用法。
    /// </summary>
    internal static class Program
    {
        internal const string 尾巴标记 = "HBEX";
        internal const int 尾巴字节数 = 4 + 4 + 4 + 2 + 4;      // N + M + 错误槽 + 条数 + 标记

        private static int Main(string[] 参数)
        {
            // H39-1:**跑程序服务档** —— 前端(IDE / `hbcc --跨进程`)通过标准输入输出管道把命令送进来,
            // 这一档里**标准输出就是二进制协议通道**:一个字节的 BOM / 中文提示都不许写,
            // 所以它必须排在 `Console.OutputEncoding` 之前(设编码本身就可能在标准输出上留下东西)。
            if (参数.Contains(跑程序协议C.服务参数))
                return new 跑程序后端C(Console.OpenStandardInput(), Console.OpenStandardOutput()).跑();

            Console.OutputEncoding = Encoding.UTF8;               // 中文输出别乱码
            // 中文**输入**同样是编码问题:Windows 控制台默认用 GBK(936),手打的中文以 GBK 字节进来,
            // 程序按 UTF-8 读就成乱码(实测:输入"小明"变成"С��")。把控制台输入码页也设成 UTF-8 就对齐了。
            // 重定向(`程序.exe < 文件`)时这个设置不影响(读的是文件原字节),文件请存成 UTF-8。
            try { Console.InputEncoding = Encoding.UTF8; } catch { }
            // "双击跑"还是"在终端里跑/被重定向":双击时跑完要停一下,不然黑框一闪就没了,输出根本看不见
            bool 手打 = !Console.IsInputRedirected && !Console.IsOutputRedirected;

            // H35:**自报能力** —— 贴尾巴之前,IDE / `hbcc --exe` 会问一句"你是哪一版"。
            // 为什么要有这一句:老口径只判"宿主在不在、够不够大",于是一份**够大但过时**的自包含宿主
            // (图形能力落地之前发布的那份)照贴不误 ⇒ 独立 exe 里 `打开窗口` 给 假、双击就是黑框一闪。
            // 不认这个旗标的**老宿主**会把它当程序文件(报"里面没有贴程序"、退出码非 0)⇒ 正好被筛出来。
            if (参数.Contains(宿主能力C.能力请求参数))
            {
                Console.WriteLine(宿主能力C.能力清单());
                Console.WriteLine($"构建={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}" +
                    $" / {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
                return 停一下(手打, 0);
            }

            try
            {
                string 自己 = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
                try { Console.Title = Path.GetFileNameWithoutExtension(自己) + " —— HB 程序"; } catch { }
                var 程序 = 程序包C.解包(File.ReadAllBytes(自己));
                if (程序 == null)
                {
                    Console.Error.WriteLine("这是 HB 程序宿主,里面没有贴程序。");
                    Console.Error.WriteLine("用法:用 hbcc 生成 —— `hbcc --exe 你的程序.hb -o 你的程序.exe`");
                    return 停一下(手打, 2);
                }

                var 机 = new 虚拟机C();
                var 输出 = new 文字输出C(Console.Write);
                机.输出目标 = b => 输出.写(b);
                机.宿主调用 = 宿主能力C.处理;         // P28:读文件 / 写文件 / 当前时间
                // R1-6 批 1:机器码里**用到图形能力**才把桌面壳挂上 —— 不用的程序仍是纯控制台
                // (于是"贴尾巴"出来的独立 exe,不带 Avalonia 那堆 dll 也照跑)。
                if (图形宿主C.这段机器码用图形(程序.机器码)) 挂图形壳();
                // D1/D5 说明:独立 exe 里**没有工程文件**(.hbp),所以"起进程"与"连网址"这两个
                // 靠工程文件声明白名单的能力在这里一律是"没挂能力"(调用给 假);D6 在收这条尾。
                // 输入要**按字节**读(Console.In 是按字符读的,中文会变成一个 >255 的 char code,读进来就错了)
                using var 标准输入 = Console.OpenStandardInput();
                机.输入源 = () =>
                {
                    int c = 标准输入.ReadByte();                    // 真阻塞读(终端里手打也吃得到)
                    return c < 0 ? null : (uint)c;                 // 只有真的 EOF 才给 null
                };
                机.初值表 = 程序.初值表;   // 顶层常量初值随包带来,装载时落进数据段(老 HBEX 包这里就是空表)
                // H7:包尾巴上那张"地址 → 源码位置"表挂进虚拟机 —— 运行期报错从此带**文件 + 行号**
                // (老包 / 手写汇编没有这张表 ⇒ 查位置给空串,照旧只报说明)
                if (!程序.源行表.空) 机.取源位置 = 地址 => 程序.源行表.查位置(地址);
                机.运行(程序.机器码);
                输出.收尾();                                       // 最后半个 UTF-8 字符别丢弃
                图形宿主C.摘掉();                                  // R1-6:跑完把桌面壳摘掉(与"白名单跑完就清"一个口径)

                if (程序.错误槽地址 > 0 && 程序.错误槽地址 + 4 <= 机.内存.Length)
                {
                    int 号 = (int)(机.内存[程序.错误槽地址]
                        | (机.内存[程序.错误槽地址 + 1] << 8)
                        | (机.内存[程序.错误槽地址 + 2] << 16)
                        | (机.内存[程序.错误槽地址 + 3] << 24));
                    if (号 >= 1 && 号 <= 程序.消息表.Count)
                    {
                        Console.Error.WriteLine($"⚠ 运行停下:{程序.消息表[号 - 1]}");
                        return 停一下(手打, 1);
                    }
                }
                return 停一下(手打, 0);
            }
            catch (运行期错误C 运行错)
            {
                // 说明这里是**现成的**:程序带"地址 → 源码位置"表(HBE4)时,`用户文案` 里已经拼好了
                // `文件 第 N 行`(装载时挂了 `取源位置`);老包没这张表就只报说明 —— 但绝不再漏 DIV/PC 这类内部名
                Console.Error.WriteLine($"⚠ 运行停下:{运行错.用户文案}");
                return 停一下(手打, 1);
            }
            catch (Exception ex)
            {
                // 用户程序怎么乱写都不许让宿主闪退:漏出来的 .NET 异常一律按"内部错误(请回报)"报(批 7)
                Console.Error.WriteLine($"内部错误(请回报):{ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine("  这是宿主/运行时自己的问题 —— 请把这一行连同你的程序一起回报一下。");
                return 停一下(手打, 1);
            }
        }

        /// <summary>
        /// 挂桌面壳(R1-6 批 1)。**单独一个方法是有意的**:壳那些类型住在 `图形宿主.dll` 里,
        /// 方法一被 JIT 就要解析它们 —— 不分开写,纯控制台的独立 exe(旁边没拷那份 dll)一进门就炸。
        /// 挂不上(没有 dll / 开不出窗口)**不让程序崩**:说一句提醒,`打开窗口` 那头给 `假`,程序自己决定怎么办。
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void 挂图形壳()
        {
            try { 图形宿主C.挂上(new 图形宿主.桌面壳C()); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"提醒: 图形窗口没挂上({ex.Message})—— 程序里 `打开窗口` 会给 假,别的地方照常跑。");
            }
        }

        /// <summary>
        /// 双击跑的时候(输入输出都没被重定向)收尾停一下:不然黑框一闪就没了,用户根本看不见输出。
        /// 在终端里跑、或者被重定向(`程序.exe &lt; 输入.txt`)时不打扰,直接返回退出码。
        /// </summary>
        private static int 停一下(bool 手打, int 退出码)
        {
            if (!手打) return 退出码;
            try
            {
                Console.WriteLine();
                Console.Write("(程序结束,按任意键关闭这个窗口)");
                Console.ReadKey(true);
                Console.WriteLine();
            }
            catch { }
            return 退出码;
        }
    }
}
