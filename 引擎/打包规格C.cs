using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 独立 exe / 原生 exe 的**打包规格** —— 一个词在两个地方同时成立:
    ///   · **进机器人的口径**(`hbcc --打包`)与**界面上给用户看的名字**(IDE 那个下拉)是同一个词;
    ///   · **判具**(`是自包含`)是同一个纯函数 —— 谁也不能嘴上说"自带运行时",盘上那份却是框架依赖。
    ///
    /// 三条规格(2026-09-23 用户拍的 R3-6-4 批 4 原为四条;第248批 H41 撤掉「轻量·需装 .NET」):
    ///   · **原生 C** —— 机器码 → C 源码 → 本机 C 编译器 → 原生 exe。产物几十~几百 KB、**不带 .NET**;
    ///     本机要有 C 编译器(clang / gcc / cc / cl),没有就报一条带改法的错。
    ///   · **自带运行时·单文件** —— 老那条(拷一份 HB 宿主 + 贴机器码)。产物几十 MB、**内置 .NET 运行时**,
    ///     目标机器什么都不用装 —— 但宿主那份**必须是真自包含版**,不然"双击即跑"就是假话。
    ///   · **只编译** —— 既不编 exe 也不贴尾巴:走 C 那条路就交一份 C 源码,走宿主那条路就交一份 `.hb`
    ///     (4 字节头 + 机器码)。给"我要自己拿去 `cc` / 自己贴"的人留的口子,也让"没有 C 编译器的机器"
    ///     照样能用上 C 后端。
    ///
    /// **"假自包含宿主"那个缺陷**(R3-6-4 顺手项)就靠这一份治:`取宿主exe()` 以前只看"路径在不在",
    /// 于是盘上那份 0.18 MB 的框架依赖 exe 被当成"自包含"贴出去,文案还写"双击即跑";
    /// 现在贴之前一律过 `是自包含()`,不是就换候选 / 后台重发 / **说真话**。
    ///
    /// **第248批(H41)撤掉「轻量·需装 .NET」那一档**(四条规格变三条)。为什么撤:
    /// 那一档自称"产物一百多 KB、目标机器要先装 .NET",实测它贴出去的是**那份 101 MB 的自包含宿主**
    /// (框架依赖那份单文件宿主**从来没被发布过**,候选表也有意不收 158 KB 那个启动器)⇒ 三处文案全说反。
    /// 两条路量过之后判:**"一百多 KB"这个卖点在本机形态下永远做不到** —— HB宿主 引用了图形宿主(Avalonia),
    /// 框架依赖单文件发布把原生库裹进去实测 **29,612 KB(约 29 MB)**(而且不带
    /// `-p:ValidateExecutableReferencesMatchSelfContained=false` 就直接报 NETSDK1151 编不过);
    /// 而它想解决的问题("产物小")**已经由「原生 C」更彻底地解决了** —— 产物几百 KB、连 .NET 都不用装。
    /// 老偏好文件里存着 `host_fx` 的自动回落到缺省档(`取规格` 认不出 ⇒ null ⇒ `读取偏好` 给缺省档),不留坑。
    /// </summary>
    public sealed class 打包规格种C
    {
        /// <summary>界面上给用户看的正式名(IDE 下拉那一行、`hbcc --打包` 的取值)</summary>
        public string 名 = "";
        /// <summary>写进 `.hbp` / 命令行的存储名(全 ASCII,谁来存都不怕编码)</summary>
        public string 键 = "";
        /// <summary>命令行上再收的旧写法 / 简称(大小写与空格都按"去空白后忽略大小写"比)</summary>
        public string[] 别名 = [];
        /// <summary>一句话说明:产物是什么、目标机器要什么</summary>
        public string 说明 = "";
    }

    /// <summary>
    /// 三条规格的**唯一事实源**(顺序 = 界面上从上到下的顺序)。
    /// 第248批(H41)起是**三条**:「轻量·需装 .NET」撤掉了(理由见 `打包规格种C` 的类注释)。
    /// </summary>
    public static class 打包规格C
    {
        public const string 原生C = "原生 C";
        public const string 自带运行时 = "自带运行时·单文件";
        public const string 只编译 = "只编译";

        /// <summary>三条规格的**唯一事实源**(顺序 = 界面上从上到下的顺序)</summary>
        public static readonly 打包规格种C[] 全部 =
        [
            new 打包规格种C
            {
                名 = 原生C,
                键 = "native_c",
                别名 = ["原生c", "native", "native_c", "c", "原生"],
                说明 = "机器码转 C 源码再用本机 C 编译器编:产物几十~几百 KB,不带 .NET,双击即跑;本机要有 C 编译器",
            },
            new 打包规格种C
            {
                名 = 自带运行时,
                键 = "host_self",
                别名 = ["host_self", "self", "selfcontained", "自带运行时单文件", "自带运行时"],
                说明 = "复制一份自包含的 HB 宿主 + 贴机器码:产物几十 MB,内置 .NET 运行时,目标机器什么都不用装",
            },
            new 打包规格种C
            {
                名 = 只编译,
                键 = "source_only",
                别名 = ["source_only", "sourceonly", "只编", "源码"],
                说明 = "不编 exe、不贴尾巴:走 C 那条路就交一份 C 源码,走宿主那条路就交一份 .hb(头 + 机器码)",
            },
        ];

        public const int 规格条数 = 3;

        /// <summary>缺省规格 = 自带运行时·单文件(与批 1~3 老行为一致:老命令一个字不改也走原来那条路)</summary>
        public const string 缺省 = 自带运行时;

        /// <summary>
        /// 按 C 编译器的编译产物 16MB 原封不动搬进来的那个尺寸界线:
        /// 不到这个数的"自带运行时"产物一定不是真自包含(实测:框架依赖那份 0.18 MB,
        /// 真自包含单文件那份十几 MB —— 差一个量级,拿 16MB 当界线足够稳)。
        /// </summary>
        public const long 自包含最小字节 = 16L * 1024 * 1024;

        /// <summary>认一个名字(正式名 / 存储键 / 别名;忽略大小写、忽略空格与中点)</summary>
        public static 打包规格种C? 查找(string? 名字)
        {
            if (string.IsNullOrWhiteSpace(名字)) return null;
            string 归一 = 归一化(名字);
            foreach (var 种 in 全部)
            {
                if (归一化(种.名) == 归一 || 归一化(种.键) == 归一) return 种;
                if (种.别名.Any(别 => 归一化(别) == 归一)) return 种;
            }
            return null;
        }

        /// <summary>取规格;认不出来给 null(调用方负责报"带选项列表"的用法错误)</summary>
        public static 打包规格种C? 取规格(string? 名字) => 查找(名字);

        /// <summary>规格名(认不出来原样返回,报错时好把用户写的那个值打出来)</summary>
        public static string 规格名(string? 名字) => 查找(名字)?.名 ?? (名字 ?? "").Trim();

        /// <summary>去空白 + 忽略大小写 + 丢掉中点 `·` 与分隔符 `_` `-`(用户手写 `Native_C` / `host self` 都认)</summary>
        private static string 归一化(string 文本)
        {
            var 出 = new StringBuilder(文本.Length);
            foreach (char 字 in 文本)
            {
                if (char.IsWhiteSpace(字)) continue;
                if (字 == '·' || 字 == '・' || 字 == '．' || 字 == '.' || 字 == '_' || 字 == '-') continue;
                出.Append(char.ToLowerInvariant(字));
            }
            return 出.ToString();
        }

        /// <summary>三条规格的"键 = 名"对照(命令行帮助、IDE 下拉、报错里都吃这一份)</summary>
        public static string 表()
        {
            var 行 = new List<string>();
            foreach (var 种 in 全部)
                行.Add($"  {种.名,-16} --打包 {种.键,-12} {种.说明}");
            return string.Join(Environment.NewLine, 行);
        }

        /// <summary>一行"三档是什么"(帮助正文里那句话)</summary>
        public static string 一句话()
            => string.Join(";", 全部.Select(种 => $"{种.名} = {种.说明}"));

        /// <summary>
        /// 这条规格贴不贴 .NET 宿主(第248批之后**只剩一条**贴:自带运行时·单文件;
        /// 原生 C 交原生 exe、只编译只交源码,都不贴)。
        ///
        /// **括号必须写**:`名 == A || 名 == B` 里 `??` 的优先级比 `==` 还低,不套括号会被解析成
        /// `名 == (A || 名) == B`,于是**每个 `||` 链的第一项恒真、后面的项被短路吃掉** ——
        /// 实测踩过:这条与 `是C源码` 双双"对谁都判真",连"只编译"都被算成要 .NET。
        /// </summary>
        public static bool 要装net(string? 规格名)
        {
            string 名 = 查找(规格名)?.名 ?? "";
            return 名 == 自带运行时;
        }

        /// <summary>这条规格要不要本机 C 编译器(只有「原生 C」要 —— 它真叫编译器编)</summary>
        public static bool 要C编译器(string? 规格名)
        {
            string 名 = 查找(规格名)?.名 ?? "";
            return 名 == 原生C;
        }

        /// <summary>
        /// 这条规格交出去的是不是 **C 源码**(`原生 C` 编 exe、`只编译` 只交源码,两条都算)。
        /// 这里的 `||` 同样必须先取到 `名` 再比(见 `要装net` 那条注释里的优先级坑)。
        /// </summary>
        public static bool 是C源码(string? 规格名)
        {
            string 名 = 查找(规格名)?.名 ?? "";
            return 名 == 原生C || 名 == 只编译;
        }

        /// <summary>
        /// "只编译"这档交出来的**后缀**:走 C 那条腿交 `.c`,走宿主那条腿交 `.hb`。
        ///
        /// 为什么要有这一条:规格名只有一个(`只编译`),"走哪条腿"是**另一个维度** ——
        /// 命令行上由 `--方式 C` 说(没写就是宿主那条);界面上由用户先把下拉放到"原生 C"再选"只编译"。
        /// 没有它就会两边串味:实测踩过"只编译 + 宿主那条"却交出 `.c`(后缀只看了"是不是源码类型",
        /// 没看"哪条腿")。
        /// </summary>
        public static string 只编译后缀(bool 走C那条腿) => 走C那条腿 ? ".c" : ".hb";

        /// <summary>这条规格交出去的是源码而不是 exe(`只编译`)</summary>
        public static bool 是只编译(string? 规格名) => (查找(规格名)?.名 ?? "") == 只编译;

        /// <summary>
        /// 一份 exe 到底是不是**真自包含**。三条判据(够用且不会误伤):
        ///   ① 体积不到界线(`自包含最小字节`)—— 框架依赖那份只有一百多 KB,差一个量级;
        ///   ② 同目录里躺着运行期 dll(单文件发布**不会**在旁边留 dll,留了就说明它是框架依赖);
        ///   ③ 同目录里躺着 `HB宿主.dll` —— 那是"启动器 + 旁边少 dll 就跑不起来"那一份的铁证。
        /// 界线上那两个参数(字节 / 同目录文件表)都从外面进来,**纯函数**,自检不用碰盘就能钉它。
        /// </summary>
        public static bool 是自包含(long 字节数, IReadOnlyCollection<string>? 同目录文件名)
            => 自包含原因(字节数, 同目录文件名) == null;

        /// <summary>查盘上那一份:是自包含给 true(文件不在也算"不是")</summary>
        public static bool 是自包含(string exe路径) => 自包含原因(exe路径) == null;

        /// <summary>
        /// 同一个判据的"说人话"那半边:是自包含给 null,不是就给一句**为什么**。
        /// 文案口径:说清"哪一份、差在哪儿、怎么修",不写"校验失败"这种没头没尾的话。
        /// </summary>
        public static string? 自包含原因(long 字节数, IReadOnlyCollection<string>? 同目录文件名)
        {
            if (字节数 < 自包含最小字节)
                return $"它只有 {字节数 / 1024} KB,像是**框架依赖**那一份(自带运行时的单文件产物至少几十 MB)";
            if (同目录文件名 == null) return null;
            foreach (string 名 in 同目录文件名)
            {
                if (名.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;   // 符号文件不参与运行
                if (名.Equals("HB宿主.dll", StringComparison.OrdinalIgnoreCase)
                    || 名.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    return $"它旁边还躺着 {名}(自带运行时的单文件不会在旁边留 dll)";
            }
            return null;
        }

        /// <summary>查盘上这一份的真实判据(`文件不存在` 也算一种"不是")</summary>
        public static string? 自包含原因(string exe路径)
        {
            if (!File.Exists(exe路径)) return "文件不在";
            long 长;
            try { 长 = new FileInfo(exe路径).Length; }
            catch (Exception ex) { return $"读不到它的大小:{ex.Message}"; }
            string 目录 = Path.GetDirectoryName(Path.GetFullPath(exe路径)) ?? ".";
            string[] 同目录;
            try { 同目录 = Directory.GetFiles(目录).Select(Path.GetFileName).OfType<string>().ToArray(); }
            catch { 同目录 = []; }
            return 自包含原因(长, 同目录);
        }

        /// <summary>
        /// 怎么弄到一份真自包含的宿主(报错/提示里那句改法,三处共用这一句)。
        ///
        /// **`-p:IncludeNativeLibrariesForSelfExtract=true` 不能少**(H36,第225批实测):
        /// 单文件发布**默认不把原生库打进去** —— Avalonia 的 `libSkiaSharp.dll` / `libHarfBuzzSharp.dll` /
        /// `av_libglesv2.dll` 会**留在发布目录旁边**。两个后果:① `是自包含()` 判"旁边躺着 dll ⇒ 不是单文件";
        /// ② 真贴出去的那个 exe **旁边什么都没有** ⇒ 桌面壳起不来(界面线程 15 秒都进不了消息循环)
        /// ⇒ `打开窗口` 一律给 假 —— 独立 exe 里看不见任何窗口。加上这个开关,原生库进单文件、
        /// 运行时自己解到临时目录,exe 单独拷走也能开窗。
        /// </summary>
        public const string 发布宿主改法 =
            "dotnet publish HB宿主\\HB宿主.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true " +
            "-p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none";

        // ── 本机偏好:上一次选的那一档 ──
        // 为什么住在引擎里(而不是界面那边):**命令行也算"上次选的"**,而且自检要能钉住"写进去读得回、
        // 文件被改坏不抛异常"这两条。存在**数据目录**(桌面 IDE 与 hbcc 各自的 bin 目录)里一个小 json,
        // 与主题同一个路数;**不进 `.hbp`** —— 工程文件是给人共享 / 进版本库的,"我这次想打成什么"
        // 是本机偏好,换台机器各选各的。

        /// <summary>偏好文件名(全 ASCII,记事本也能看懂)</summary>
        public const string 偏好文件名 = "打包方式.json";

        /// <summary>缺省那一档:有 C 编译器就「原生 C」(编得最快、产物最小、不带 .NET),没有就「自带运行时·单文件」</summary>
        public static string 缺省档()
        {
            try { return C工具链C.探一下().成功 ? 原生C : 自带运行时; }
            catch { return 自带运行时; }
        }

        /// <summary>偏好文件落在哪个目录(缺省 = 当前程序所在目录,与主题那套同一个口径)</summary>
        public static string 偏好目录 => AppContext.BaseDirectory;

        public static string 偏好路径(string? 目录 = null) => Path.Combine(目录 ?? 偏好目录, 偏好文件名);

        /// <summary>读上次选的那一档(没存过 / 存坏了 → 缺省档;从不抛异常)</summary>
        public static string 读取偏好(string? 目录 = null)
        {
            string 文件 = 偏好路径(目录);
            try
            {
                if (File.Exists(文件))
                {
                    var 规格 = 取规格(File.ReadAllText(文件).Trim().Trim('"'));
                    if (规格 != null) return 规格.名;
                }
            }
            catch { }
            return 缺省档();
        }

        /// <summary>记下这一档(认不出来的名字不写,免得把坏值存进去)</summary>
        public static void 写偏好(string 规格名, string? 目录 = null)
        {
            var 规格 = 取规格(规格名);
            if (规格 == null) return;
            try
            {
                string 文件 = 偏好路径(目录);
                Directory.CreateDirectory(Path.GetDirectoryName(文件) ?? ".");
                File.WriteAllText(文件, 规格.键, new UTF8Encoding(false));
            }
            catch { }
        }
    }
}
