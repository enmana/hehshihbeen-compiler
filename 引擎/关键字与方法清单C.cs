// ============================================================
// 关键字与方法清单C —— `关键字与方法清单.md` 这份**生成物**的唯一写盘口
//
// 为什么要有这一处(H81,2026-10-11):
//   · §6 库函数 / §9 库一览 早在 2026-10-05 就收回了 `hbcc --生成帮助` 现场生成,
//     可**其余各节还是手抄** —— 当初是"拿探针跑一遍、把结果贴进 md",而探针在 `工具\临时\`
//     (**不进版本库**)⇒ 探针一没,谁也不知道它还准不准。
//   · 实测漂到哪:§1 关键字写 40(现场 41)、§3 内建函数写 45(现场 57);
//     §7 结构写 16(现场 19);§8 命名常量的 23 个名字甚至是 2026-09-19 改名**之前**的
//     (`双精度常量_零` 早就不存在了,现在叫 `双精度常量零`)—— 照它找名字会找到**不存在**的函数。
//   ⇒ 修法:§1~§5 也收回生成(数据一律取自 `中间层指令C` 的四张表 + 说明表、`指令C` 的指令表,
//     与帮助站点《关键字》《内建函数》《方法》《指令》**同一份数据**),§7/§8 跟着库源码一起生成,
//     并把"哪些节是生成的"写在文件头。
//
// 分工:**本类建语言面五个节(§1~§5)并统一写盘**;`库函数清单C` 建库那一半的四个节(§6~§9)。
// 一个文件只有一个写口 —— 免得两处各写各的,把头句冲掉。
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary> `关键字与方法清单.md`:语言面 §1~§5 从表生成,库那一半 §6~§9 交 `库函数清单C` </summary>
    public static class 关键字与方法清单C
    {
        // 节标题前缀(按前缀找节;标题里那个"共 N 个"是生成的,不在前缀里)
        public const string 关键字节 = "## 1、关键字";
        public const string 附加类型名节 = "## 2、附加类型名";
        public const string 内建节 = "## 3、内建函数";
        public const string 方法节 = "## 4、字符串/列表方法";
        public const string 指令节 = "## 5、ISA 指令";

        /// <summary>
        /// 文件头那段(标题下面、第一个 `## ` 之前):**把"哪些节是生成的、从哪生成"一次说清**。
        /// 以前这里只写了库那两节,于是没人知道 §1~§5 是手抄的、也没人知道它们已经过期了(H81)。
        /// </summary>
        public const string 文件头 =
            "> **本文档每一节都是生成的 —— 别手改,改了下次生成会被冲掉**(2026-10-11 起 §1~§5 也收回现场生成):\n" +
            "> · **语言面 §1~§5** ← `中间层\\中间层指令C.cs`(关键字表 / 附加类型名表 / 内建函数名表 / 方法名表 + 说明表)\n" +
            ">   与 `引擎\\指令C.cs`(指令表)—— 与帮助站点《关键字》《内建函数》《方法》《指令》**同一份数据**;\n" +
            "> · **库那一半 §6~§9** ← 扫 `Sample\\库\\*.hb` 的声明(函数 / 结构 / 命名常量),把声明**紧上面**那行注释当『作用』。\n" +
            ">\n" +
            "> 跑 `hbcc --生成帮助` 一次同步全部九节;都是**单一事实源**,不手抄。用途是『审查命名合不合理』(B7 / B8)。\n" +
            "> (库那一半已于 2026-09-19 改名:双_/长_/超_/双精_/积_ 这些前缀取消,见 `任务清单.md`。)";

        // ---------------- §1~§5:语言面(表 → 节) ----------------

        /// <summary> §1 关键字:整张 `关键字表`(含基本类型名 —— 它们要跟着中英切换 / 高亮 / 补全) </summary>
        public static string 建关键字节(IReadOnlyList<(string 中文, string 英文)> 表) =>
            建词节(关键字节, " 个", 表, 前缀: "", 后缀: "",
                "> 来源 = `中间层指令C.关键字表`;基本类型名也在这张表里(它们要跟着中英切换 / 高亮 / 补全,\n" +
                "> 在帮助站点里另占《类型》一节)。『作用』一列取自 `中间层指令C.说明表`。\n" +
                "> 表里加一条,这一节与帮助站点《关键字》同时多一条。");

        /// <summary> §2 附加类型名:`字符串` / `string` 这种"不是基本类型但要进词表"的名字 </summary>
        public static string 建附加类型名节(IReadOnlyList<(string 中文, string 英文)> 表) =>
            建词节(附加类型名节, " 个,与关键字一起进词表", 表, 前缀: "", 后缀: "",
                "> 来源 = `中间层指令C.附加类型名表`;`字符串` **不是基本类型**(头 8 字节 + 堆里的字),\n" +
                "> 所以不在《类型》那张表里,但必须查得到 —— 和关键字一起进词表。");

        /// <summary> §3 内建函数:语言级内建(不是语句开头那个词,而是表达式里的函数名) </summary>
        public static string 建内建节(IReadOnlyList<(string 中文, string 英文)> 表) =>
            建词节(内建节, " 个,中英可切换", 表, 前缀: "", 后缀: "(…)",
                "> 来源 = `中间层指令C.内建函数名表`;中英两侧都认,整篇可切换,加一个新内建只登记这一处。\n" +
                "> `存字` / `取字` 与 ISA 指令同名:带括号的是**堆读写**,不带括号的才是汇编(见各自那一条)。");

        /// <summary> §4 字符串/列表方法:只能写在变量后面的带点方法 </summary>
        public static string 建方法节(IReadOnlyList<(string 中文, string 英文)> 表) =>
            建词节(方法节, " 个,中英都认", 表, 前缀: ".", 后缀: "(…)",
                "> 来源 = `中间层指令C.方法名表`;中文是规范写法,英文是别名,两种都能编译。\n" +
                "> 中英切换**不动它们**(反正两边都认,少一层" + "\u201c" + "切一下就废" + "\u201d" + "的风险)。");

        /// <summary> §5 ISA 指令:按操作码排(与 `指令集说明.md` 的语义、编码同一批数据) </summary>
        public static string 建指令节(IReadOnlyList<(string 中文, string 英文, byte 操作码, 指令格式 格式)> 表)
        {
            StringBuilder 缓 = new();
            缓.Append(指令节).Append('(').Append(表.Count).Append(" 条,中英对照;语义与编码见 `指令集说明.md`)\n\n");
            缓.Append("> 来源 = `引擎\\指令C.cs` 的指令表(操作码 / 编码格式);全部指令固定 4 字节(小端),\n");
            缓.Append("> 操作码在最高字节。加一条指令,这一节与帮助站点《指令》同时多一条。\n\n");
            缓.Append("| 中文名 | 英文名 | 操作码 | 编码格式 |\n|---|---|---|---|\n");
            foreach ((string 中, string 英, byte 码, 指令格式 格) in 表.OrderBy(x => x.操作码))
            {
                缓.Append("| `").Append(中).Append("` | `").Append(英).Append("` | `0x")
                   .Append(码.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))
                   .Append("` | ").Append(格).Append(" |\n");
            }
            return 缓.ToString();
        }

        /// <summary> §1~§4 共用:一张 `中文名 / 英文名 / 作用` 表(作用取 `说明表`,没有就给 `—`) </summary>
        private static string 建词节(string 节标题, string 个数后缀,
            IReadOnlyList<(string 中文, string 英文)> 表, string 前缀, string 后缀, string 来源句)
        {
            StringBuilder 缓 = new();
            缓.Append(节标题).Append('(').Append(表.Count).Append(个数后缀).Append(")\n\n");
            缓.Append(来源句).Append("\n\n");
            缓.Append("| 中文名 | 英文名 | 作用 |\n|---|---|---|\n");
            foreach ((string 中, string 英) in 表)
            {
                string 说明 = 中间层指令C.取说明(英) ?? "—";
                缓.Append("| `").Append(前缀).Append(中).Append(后缀)
                   .Append("` | `").Append(前缀).Append(英).Append(后缀)
                   .Append("` | ").Append(库函数清单C.表格格(说明)).Append(" |\n");
            }
            return 缓.ToString();
        }

        // ---------------- 写盘 ----------------

        /// <summary>
        /// 把九节全部重生成进 `关键字与方法清单.md`,并重写文件头;返回换掉的**节数**(文件头不计数)。
        /// **返回 0 = 一个节标题都没找到 ⇒ 一个字节都不动盘** —— 连文件头都不换:
        /// 免得把一份"不是本文档"的同名文件开头顶掉(宁可不动,也不要把文件写坏)。
        /// </summary>
        public static int 更新(string 路径, IReadOnlyList<(string 文件名, string 内容)> 库文件)
        {
            if (!File.Exists(路径)) return 0;
            string 文 = File.ReadAllText(路径).Replace("\r\n", "\n");
            string 新 = 文;
            int 节数 = 0;

            void 换(string 节标题, string 新节)
            {
                string? 果 = 换节(新, 节标题, 新节);
                if (果 != null) { 新 = 果; 节数++; }
            }

            // §1~§5:语言面 —— 四张表 + 说明表 + 指令表
            换(关键字节, 建关键字节(中间层指令C.关键字表));
            换(附加类型名节, 建附加类型名节(中间层指令C.附加类型名表));
            换(内建节, 建内建节(中间层指令C.内建函数名表));
            换(方法节, 建方法节(中间层指令C.方法名表));
            换(指令节, 建指令节(指令C.全部指令));
            // §6~§9:库那一半 —— 扫 `Sample\库\*.hb`
            换(库函数清单C.库函数节, 库函数清单C.建库函数节(库文件));
            换(库函数清单C.结构节, 库函数清单C.建结构节(库文件));
            换(库函数清单C.常量节, 库函数清单C.建常量节(库文件));
            换(库函数清单C.库一览节, 库函数清单C.建库一览节(库文件));
            // 一个节标题都没找到 ⇒ 这不是本文档,一个字节都别动(连文件头也不换)
            if (节数 == 0) return 0;

            // 文件头:把"哪些节是生成的"写在最显眼的地方(这一段换不上不算错,节已经换好了)
            string? 带头 = 换头(新, 文件头);
            if (带头 != null) 新 = 带头;

            if (!新.EndsWith('\n')) 新 += "\n";
            if (!string.Equals(新, 文, StringComparison.Ordinal)) File.WriteAllText(路径, 新);
            return 节数;
        }

        /// <summary> 把 `节标题` 那一整节(到下一个 `## ` 或文件末尾)换成 `新节`;找不到给 null </summary>
        public static string? 换节(string 文, string 节标题, string 新节)
        {
            string[] 行 = 文.Split('\n');
            int 起 = Array.FindIndex(行, x => x.StartsWith(节标题, StringComparison.Ordinal));
            if (起 < 0) return null;
            int 止 = 起 + 1;
            while (止 < 行.Length && !行[止].StartsWith("## ", StringComparison.Ordinal)) 止++;
            var 果 = new List<string>();
            果.AddRange(行.Take(起));
            果.AddRange(新节.TrimEnd('\n').Split('\n'));
            果.Add("");
            果.AddRange(行.Skip(止));
            return string.Join("\n", 果);
        }

        /// <summary> 换掉标题下面那一段(第一个 `## ` 之前的一切);找不到 `# ` 标题或 `## ` 节给 null </summary>
        public static string? 换头(string 文, string 新头)
        {
            string[] 行 = 文.Split('\n');
            int 起 = Array.FindIndex(行, x => x.StartsWith("# ", StringComparison.Ordinal));
            if (起 < 0) return null;
            int 止 = 起 + 1;
            while (止 < 行.Length && !行[止].StartsWith("## ", StringComparison.Ordinal)) 止++;
            if (止 >= 行.Length) return null;   // 一个节都没有 ⇒ 这不是本文档,别动
            var 果 = new List<string> { 行[起], "" };
            果.AddRange(新头.TrimEnd('\n').Split('\n'));
            果.Add("");
            果.AddRange(行.Skip(止));
            return string.Join("\n", 果);
        }
    }
}
