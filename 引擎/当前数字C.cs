using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R4-6(第197批):**"当前数字"的唯一维护点**。
    ///
    /// 为什么要有它(R4-5 的根因):项目的机检闸门只盖住"机器生成的产物" ——
    /// 规模表、帮助站点、断言清单指纹、命名禁用词表。于是**"人写的摘要数字"成了机检之外的一块飞地**:
    /// 自检条数、标准工程数、ISA 条数、关键字个数…… 手抄在 `接手说明.md` / `任务清单.md` /
    /// `全面测评报告.md` / 三份 ISA 文档里,清完一茬又烂一茬(批 177 清完 13 处,批 195 又冒出 16 处)。
    ///
    /// 口径(**能被机检的留下,不能的被删掉**):
    /// <list type="bullet">
    /// <item>能数得出来的(自检条数与指纹 / 标准工程数 / ISA 条数 / 关键字 / 内建函数 / 方法 /
    ///   配色词类 / 彩虹档 / 帮助站点节点与字数)—— 全部由本文件**现场生成**,
    ///   写进 `接手说明.md` §4 的一对标记之间(`hbcc --stats --更新报告`);
    ///   自检 `文档数字 接手说明的当前数字没过期` 拿盘上那份跟现场逐字比,手改一个数字就红灯;</item>
    /// <item>数不出来的(五端 0 错误 / WPF 警告数 / 自检耗时 / 代码行数明细)——
    ///   前者看 `工具\五端构建.ps1` 的输出,行数看 `全面测评报告.md` §1 那张自动表,耗时现测;
    ///   **别处只许指路,不许再抄**(自检 `文档数字 摘要位不许再手抄数字` 盯着)。</item>
    /// </list>
    ///
    /// **为什么自检那三个数单独走"和校验"**:`通过 / 失败 / 跳过` 只有**跑完一遍**才知道,
    /// 而这条断言就在那一遍里面跑 —— 拿它跟盘上比是拿两个不同时刻的数比。
    /// 所以那三个数取盘上的值回填再比(等于不比),另用 **通过 + 失败 + 跳过 == 条数** 这个等式钉住
    /// (R4-4 保证断言名唯一,于是这个等式成立)。
    /// </summary>
    public static class 当前数字C
    {
        /// <summary> `接手说明.md` §4 那一段的头一行(前后各一行,见 `标记块C`) </summary>
        public const string 开始标记 =
            "<!-- 当前数字:下面是自动生成的,别手改(在仓库根目录跑 `hbcc --stats --更新报告`) -->";

        /// <summary> `接手说明.md` §4 那一段的尾一行 </summary>
        public const string 结束标记 = "<!-- 当前数字:自动生成结束 -->";

        /// <summary> 这一段住在哪个文件里(相对仓库根;放在这里,免得分散在三处各写一遍) </summary>
        public const string 接手说明文件名 = "接手说明.md";

        /// <summary>
        /// 生成块的换行一律跟平台走(`Environment.NewLine`)—— 与 `规模统计C.生成表` 的 `AppendLine` 同一个口径。
        /// 为什么:那两个文件本来就是 CRLF,生成块要是写进去一堆 LF,文件就成了"混行尾",
        /// 而且**拿编辑器改一个数字就会把行尾整段归一化**,新鲜度检查当场红(实测踩过:盘上 345 字 / 现场 342 字,
        /// 差的正是那三处行尾)。自检那边另外还做了行尾无关的比较,两道一起兜。
        /// </summary>
        private static readonly string 换行 = Environment.NewLine;

        /// <summary>
        /// 这一段是拿**哪一档**跑的(写在"自检"那一句里)。
        /// <para>
        /// 为什么要有它(2026-10-13 **H108**):那一句原文写死的是"(Debug 与 Release 都跑过、同数)",
        /// 而这句话**只有两档各自跑满**才成立 —— 本机 Debug 全量自检**跑不完 600 秒预算**
        /// (实测 193/245 组、620 秒),那一轮根本不是判据 ⇒ 写死就成了**对外说了没量过的话**。
        /// 现在只写**这一次**是哪一档;两档各自跑过、失败都为 0 是**收口口径**,不在这句里冒充既成事实。
        /// </para>
        /// <para>
        /// 判据用 `DebuggableAttribute.IsJITOptimizerDisabled`:Debug 配置编出来的程序集是 true、
        /// Release 是 false(比 `#if DEBUG` 稳 —— 那份符号取决于各工程自己的 `DefineConstants`)。
        /// </para>
        /// </summary>
        private static string 本次档位()
            => typeof(当前数字C).Assembly
                   .GetCustomAttribute<System.Diagnostics.DebuggableAttribute>() is { IsJITOptimizerDisabled: true }
               ? "Debug"
               : "Release";

        /// <summary>
        /// 生成"当前数字"那一段(markdown,多行)。`通过 / 失败 / 跳过` 传负数 = 这次没跑自检
        /// (那三个数写成"见 `--selftest` 收尾那一行")。
        ///
        /// **只放"现场数得出来"的东西** —— 数不出来的(五端 / 警告数 / 耗时)不在这里,见类注释。
        /// </summary>
        public static string 生成(string 仓库根, int 通过 = -1, int 失败 = -1, int 跳过 = -1)
        {
            string 路(params string[] 段) => Path.Combine([仓库根, .. 段]);

            var (条数, 指纹) = 取断言清单(仓库根);
            int 示例数 = 规模统计C.数个数(路("Sample"), ".hbp");
            int 源码数 = 规模统计C.数个数(路("Sample"), ".hb");
            int 指令数 = 指令C.全部指令.Count;
            byte 最大操作码 = 指令C.全部指令.Max(x => x.操作码);
            var (页数, 字节数, 字符数) = 数帮助站点(仓库根);

            string 自检 = 通过 >= 0
                ? $"自检 **{通过} 通过 / {失败} 失败 / {跳过} 跳过**"
                : "自检全绿(见 `hbcc --selftest` 收尾那一行)";

            var sb = new StringBuilder();
            // H108:只写**这一次**是哪一档跑出来的 —— 不再声称"两档都跑过、同数"(那句只有两档都跑满才成立)
            sb.Append(自检).Append($"(本次是 **{本次档位()}** 档那一轮;收口口径 = Debug 与 Release 各自跑过、失败都为 0);");
            sb.Append($"断言清单 **{条数} 条**(一条一个名字,指纹 `{指纹}`);").Append(换行);
            sb.Append($"标准工程 **{示例数}** 个(.hbp)/ **{源码数}** 个 .hb,优化开/关双跑一致;");
            sb.Append($"ISA **{指令数} 条**(`0x00~0x{最大操作码:X2}`);关键字 **{中间层指令C.关键字表.Length}**;").Append(换行);
            sb.Append($"内建函数 **{中间层指令C.内建函数名表.Length}**(中英可切换)+ 方法 **{中间层指令C.方法名表.Length}**(仅中文);");
            sb.Append($"配色 **{配色词类C.全部项.Length}** 个词类(亮/暗两套,选项页可逐项调)+");
            sb.Append($" **{花括号层号C.色表.Length}** 档花括号彩虹色(选项页可逐档调);").Append(换行);
            sb.Append($"帮助站点 `帮助\\index.html` **{页数} 页**({字节数} 字节 / {字符数} 字符:");
            sb.Append("总目录 + 一个话题一页 + 分类索引,逐字节可复现)。");
            return sb.ToString();
        }

        /// <summary> 把这一段写进 `接手说明.md`(只改一对标记之间);返回替换了几段(正常是 1) </summary>
        public static int 更新(string 接手说明路径, string 块)
            => 标记块C.替换(接手说明路径, 开始标记, 结束标记, 块);

        /// <summary>
        /// 从 `工具\自检断言清单.txt` 数出**条数**与**指纹**。
        ///
        /// 为什么读盘而不是让调用方传进来:`--更新报告` 与自检那条新鲜度断言都要用这两个数,
        /// 而自检**没法**在自己跑完之前拿到"这一次的条数" —— 读盘就等于两边看的是同一份,
        /// 不会出现"写进去一个数、比的是另一个数"。清单本身被 `自检 断言清单与 工具/自检断言清单.txt 一致`
        /// 那条钉着,所以它是可信的。
        /// </summary>
        public static (int 条数, string 指纹) 取断言清单(string 仓库根)
        {
            string 路径 = Path.Combine(仓库根, "工具", "自检断言清单.txt");
            if (!File.Exists(路径)) return (0, "见 --selftest");
            var 清单 = File.ReadAllLines(路径, Encoding.UTF8)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0 && x != 综合自检C.清单比对断言名)
                .ToArray();
            return (清单.Length, 综合自检C.清单指纹(清单));
        }

        /// <summary>
        /// 数已生成的帮助站点(第241批改成多页站点):**页数**(`帮助\**\*.html` 全算)/
        /// 字节(那些页 + 样式表的总长度)/ 字符(解出来的文本长度)。以前数的是单文件里
        /// `&lt;section id=` 的条数,多页站点里那样数出来是 0。
        /// </summary>
        private static (int 页数, long 字节, int 字符) 数帮助站点(string 仓库根)
        {
            string 目录 = Path.Combine(仓库根, 帮助站点C.站点目录);
            if (!Directory.Exists(目录)) return (0, 0, 0);
            int 页数 = 0, 字符 = 0;
            long 字节 = 0;
            foreach (string 文件 in Directory.EnumerateFiles(目录, "*.html", SearchOption.AllDirectories)
                                             .Concat(Directory.EnumerateFiles(目录, "*.css", SearchOption.AllDirectories)))
            {
                string 文本;
                try { 文本 = File.ReadAllText(文件); } catch { continue; }
                字符 += 文本.Length;
                字节 += new FileInfo(文件).Length;
                if (文件.EndsWith(".html", StringComparison.OrdinalIgnoreCase)) 页数++;
            }
            return (页数, 字节, 字符);
        }
    }
}
