using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// P31:`全面测评报告.md` 里那张"规模"表**自动生成**。
    ///
    /// 为什么要它:那张表以前靠人工数,加了几个文件、几条指令,数字就悄悄过期了 —— 报告里写"45 条"、
    /// 代码里已经是 49 条,读者没法判断哪句是新的。现在数出来的东西全部**从代码/文件本身取**:
    /// 各目录 `*.cs` 行数(不数 bin/obj)、`Sample` 里的 .hbp/.hb 个数、关键字个数、指令条数与操作码上限、
    /// 默认内存/池/堆大小(取常量,不抄数字);只有"自检多少条"必须**跑一遍自检**才知道,由调用方传进来。
    ///
    /// 报告里那张表夹在一对标记之间,`hbcc --stats --更新报告` 只改标记之间那一段,别处的文字一个字不动。
    /// </summary>
    public static class 规模统计C
    {
        public const string 开始标记 = "<!-- 规模统计:下面是自动生成的,别手改(在仓库根目录跑 `hbcc --stats --更新报告`) -->";
        public const string 结束标记 = "<!-- 规模统计:自动生成结束 -->";

        /// <summary> 数一个目录里的 `*.cs` 行数;bin / obj 不算。`递归` = false 时只数这一层(仓库根目录那几个 .cs 用) </summary>
        private static int 数行数(string 目录, string 扩展名, bool 递归 = true)
        {
            if (!Directory.Exists(目录)) return 0;
            int 行数 = 0;
            foreach (string 文件 in Directory.EnumerateFiles(目录, "*" + 扩展名,
                         递归 ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                string 全 = 文件.Replace('\\', '/');
                if (全.Contains("/bin/") || 全.Contains("/obj/")) continue;
                try { 行数 += File.ReadAllLines(文件).Length; } catch { /* 读不到就当 0,不因为一个文件让统计整个失败 */ }
            }
            return 行数;
        }

        /// <summary>
        /// 数一个目录(含子目录)里某种扩展名的文件个数(bin / obj 不算,**HB 机器码产物也不算**)。
        /// **public 是给 `当前数字C` 用的** —— "标准工程几个"这条数字只许有一处数法(第197批)。
        ///
        /// **机器码那一条是第434批补的**:IDE 的「打包方式 → 只编译」(宿主那条腿)把产物落成
        /// `&lt;项目目录&gt;\&lt;工程名&gt;.hb`,后缀与源码一样 ⇒ 以前它会被当成源码数进去,
        /// `文档数字 接手说明的当前数字没过期` 当场红灯(实测 161 → 162)。
        /// 判据与口径见 `源码文件C` —— **二进制不是源码**,这一条对 `.hbp` 那些计数同样成立(它是文本,永不为真)。
        /// </summary>
        public static int 数个数(string 目录, string 扩展名)
        {
            if (!Directory.Exists(目录)) return 0;
            int 个数 = 0;
            foreach (string 文件 in Directory.EnumerateFiles(目录, "*" + 扩展名, SearchOption.AllDirectories))
            {
                string 全 = 文件.Replace('\\', '/');
                if (全.Contains("/bin/") || 全.Contains("/obj/")) continue;
                if (源码文件C.是机器码(文件)) continue;   // 二进制不是源码(只编译的 .hb 产物会落进工程目录)
                个数++;
            }
            return 个数;
        }

        /// <summary> 内存/堆/池那几个数按"人看的写法"来(KB / MB) </summary>
        private static string 大小文本(int 字节) => 字节 >= 1024 * 1024 && 字节 % (1024 * 1024) == 0
            ? $"{字节 / (1024 * 1024)} MB"
            : $"{字节 / 1024}KB";

        /// <summary>
        /// 生成那张"规模"表(markdown)。`通过/失败/跳过` 传负数 = 这次没跑自检(那行会写"见 --selftest")。
        /// `条数` = 断言清单的条数(去重后的断言名个数)。**R4-4 之前那一列填的是"通过数"(实例数)**,
        /// 而名字不唯一时两个数能差出二十多条 —— 现在头条数字用**条数**,它被清单指纹与"断言名唯一"两条钉着。
        /// 不传(负数)就退回"通过 + 失败 + 跳过"(名字唯一时与条数相等)。
        /// </summary>
        public static string 生成表(string 仓库根, int 通过 = -1, int 失败 = -1, int 跳过 = -1, int 条数 = -1)
        {
            string 路(params string[] 段) => Path.Combine([仓库根, .. 段]);

            // ---- 各目录行数(口径与报告一致;仓库根只数这一层的那几个 .cs) ----
            var 组 = new (string 名, string 目录, bool 递归)[]
            {
                ("引擎", 路("引擎"), true),
                ("C后端", 路("C后端"), true),            // R3-6-4:机器码 → C 源码的转译器与 C 工具链(运行时.c 不算行数)
                ("中间层", 路("中间层"), true),
                ("自检", 路("自检"), true),
                ("界面与控件", 仓库根, false),          // 根目录的 MainWindow / App 等(下面再把 界面控件 加进来)
                ("CLI", 路("编译器CLI"), true),
                ("配置", 路("配置"), true),
                ("宿主", 路("HB宿主"), true),
                ("手机 IDE", 路("MobileIDE"), true),
            };
            var 行数 = new List<(string 名, int 行)>();
            foreach (var (名, 目录, 递归) in 组)
            {
                int n = 数行数(目录, ".cs", 递归);
                // 界面与控件 = 仓库根那几个 .cs(MainWindow / App …)+ 界面控件\ + 选卡控件\ + 工具\ 顶层的 反汇编器C.cs
                // —— 这几处都在同一个 csproj 里(主工程只排除 `工具\临时\**`),漏一个"按目录统计"就不成立(第195批补)。
                if (名 == "界面与控件")
                {
                    n += 数行数(路("界面控件"), ".cs");
                    n += 数行数(路("选卡控件"), ".cs");
                    n += 数行数(路("工具"), ".cs", false);   // 递归会把 `工具\临时\**` 的探针算进来,所以只数这一层
                }
                行数.Add((名, n));
            }
            int 总计 = 行数.Sum(x => x.行);
            int xaml = 数Xaml(仓库根);

            int 示例数 = 数个数(路("Sample"), ".hbp");
            int 源码数 = 数个数(路("Sample"), ".hb");
            int 关键字数 = 中间层指令C.关键字表.Length;
            int 指令数 = 指令C.全部指令.Count;
            byte 最大操作码 = 指令C.全部指令.Max(x => x.操作码);

            var sb = new StringBuilder();
            sb.AppendLine("| 项 | 数量 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| 指令集 | **{指令数} 条**(0x00~0x{最大操作码:X2}),固定 4 字节;寄存器 R0~R15(R15 = 帧指针) |");
            sb.AppendLine($"| 中间层关键字 | **{关键字数} 个**(中英双语,可整体切换;含类型名与 `真/假`) |");
            sb.AppendLine($"| 标准工程示例 | **{示例数} 个 .hbp**(含 {源码数} 个 .hb) |");
            sb.AppendLine("| 代码量 | " +
                string.Join(" / ", 行数.Select(x => $"{x.名} {x.行} 行")) +
                $" / XAML {xaml} 行 ≈ **{总计 / 10000.0:0.#} 万行 C#**" +
                "(按目录统计 `*.cs` 行数,bin/obj 不算;`工具\\临时\\**` 的探针不计;本行由 `hbcc --stats --更新报告` 自动生成) |");
            sb.AppendLine(通过 >= 0
                ? $"| 自检 | **{(条数 >= 0 ? 条数 : 通过 + 失败 + 跳过)} 条**" +
                  $"(Debug、Release 各跑一遍都是 {通过} 通过 / {失败} 失败 / {跳过} 跳过) |"
                : "| 自检 | 见 `hbcc --selftest` 的收尾那一行(要写进报告就跑 `hbcc --stats --更新报告`) |");
            sb.AppendLine($"| 内存模型 | 默认 {大小文本(虚拟机C.默认内存大小)}、上限 {大小文本(虚拟机C.最大内存大小)}" +
                "(直接寻址仍是 20 位,超过它的地址由编译器改走 `载入高位` + 间接寻址);代码段 + 静态数据段" +
                $"(含**字符串池**:一开始 {大小文本(中间层C.默认池大小)}、不够会自己长大,`.hbp` 里配 `池`;与独立堆" +
                $"**默认 {大小文本(中间层C.默认堆大小)}、可在 `.hbp` 里配 `堆`**)+ 软件栈" +
                $"(默认按运行内存算 {大小文本(中间层C.默认软栈上限)}、`.hbp` 里配 `软栈`)" +
                ";**分区明细**(代码段 / 变量槽 / 堆 / 池 / 软栈 / 空闲 / 硬件栈各占哪一段、池还能长多少)由 " +
                "`hbcc --run` 现场打印,口径见 `引擎\\内存布局C.cs`(R1-3) |");
            return sb.ToString().TrimEnd('\n');
        }

        /// <summary> XAML 行数(界面/手机 IDE 都算;bin/obj 不算) </summary>
        private static int 数Xaml(string 仓库根)
        {
            int 行数 = 0;
            foreach (string 文件 in Directory.EnumerateFiles(仓库根, "*.xaml", SearchOption.AllDirectories))
            {
                string 全 = 文件.Replace('\\', '/');
                if (全.Contains("/bin/") || 全.Contains("/obj/")) continue;
                try { 行数 += File.ReadAllLines(文件).Length; } catch { }
            }
            return 行数;
        }

        /// <summary>
        /// 把表写进报告(只改一对标记之间的内容)。找不到标记就报错说清怎么办 —— 不去猜"该插在哪儿"。
        /// 返回替换了几段(正常是 1)。
        ///
        /// **第197批(R4-6)起**:这段逻辑搬进 `引擎\标记块C.cs`(它同时服务"当前数字"),
        /// 这里只剩一层转发 —— 行为一字未改。
        /// </summary>
        public static int 更新报告(string 报告路径, string 表)
            => 标记块C.替换(报告路径, 开始标记, 结束标记, 表);
    }
}
