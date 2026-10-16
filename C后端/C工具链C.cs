using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>找到的一套 C 编译器(怎么调、要不要手工喂 INCLUDE/LIB,都在这里定)</summary>
    public sealed class C编译器C
    {
        /// <summary>报给用户的名字(clang / gcc / cl)</summary>
        public string 名称 = "";
        /// <summary>可执行文件全路径</summary>
        public string 可执行 = "";
        /// <summary>这套是从哪儿找到的(PATH / LLVM 安装目录 / VS 安装目录)—— 报错时要说清</summary>
        public string 来历 = "";
        /// <summary>支持 computed goto(labels-as-values)—— 分派表走 `goto *表[PC/4]` 而不是 `switch`</summary>
        public bool 支持计算跳转;
        /// <summary>MSVC 那种"必须手工给 INCLUDE / LIB"的额外环境</summary>
        public string? 头文件目录;
        public string? 库目录;

        /// <summary>给人看的一句"这套环境是怎么凑出来的"</summary>
        public string 环境说明() => 名称 == "cl" && 头文件目录 != null
            ? $"手工喂了 INCLUDE = {头文件目录};LIB = {库目录}"
            : "用它自带的工具链";
    }

    /// <summary>一次"把 C 源码编成原生 exe"的结果</summary>
    public sealed class C编译结果C
    {
        /// <summary>编出来了没有</summary>
        public bool 成功;
        /// <summary>用的是哪套编译器(成功时才有意义)</summary>
        public string 编译器 = "";
        /// <summary>给它喂的额外环境说明(MSVC 那条路上会写清 INCLUDE / LIB 是从哪儿找的)</summary>
        public string 环境说明 = "";
        /// <summary>编译日志**尾部**(工具输出只读尾:前面的编译进度对排查没用)</summary>
        public string 日志 = "";
        /// <summary>一个候选都没找到时的原因(给人看的)</summary>
        public string 找不到原因 = "";
    }

    /// <summary>
    /// R3-6-4 批 1:C 工具链 —— 找到本机的 C 编译器,把 C 源码编成原生 exe。
    ///
    /// 口径(用户 2026-09-22 拍板第 1 条 = **clang 优先**,别的当备用):
    ///   ① 环境变量 `HB_C编译器` 指了就用它(测试 / 用户手动指定,谁都不问);
    ///   ② PATH 上 / LLVM 常见安装目录里的 **clang**;
    ///   ③ PATH 上的 **gcc / cc / tcc**;
    ///   ④ **MSVC 的 `cl`** —— PATH 上有(说明在开发人员命令提示符里)就直接用,
    ///      不然从 VS 安装目录里找,并按已知布局凑出 INCLUDE / LIB。
    ///   **逐个候选真编一遍**:哪个编出 exe 就算哪个成功(所以"PATH 上有个跑不动的 clang"
    ///   不会把整条路堵死,换下一个接着试);全失败就把每套的日志尾部一起报出来。
    ///
    /// 为什么允许"试到能用为止":装机是**一次性**的开发机成本,而用户的机器拿到的是原生 exe、
    /// 什么都不用装(拍板第 1 条) —— 这条路上的容错比"死认一套"值钱。
    ///
    /// **R3-6-4 批 4 收尾(用户 2026-09-23 报"切到原生 C 就卡死")在这条路上的三刀**:
    ///   · **候选表是惰性的**(`试编` / `编成exe带选项` 都直接 `foreach (列候选())`):
    ///     以前先 `ToList()`,于是"列一遍候选"就等于先把兜底扫盘跑完 —— 哪怕 PATH 上第一套 clang 就能编;
    ///   · **扫盘是兜底**(排在 vswhere 与标准安装位置后面)、**带预算**(`扫盘预算毫秒` / `扫盘目录上限`)、
    ///     **结果缓存**(同一进程不重扫),撞预算就如实说"没扫完";
    ///   · **超时不再连试**(`没编完`):大源码等满预算还没编完时,换一套编译器一样慢 ——
    ///     以前两个候选各等 180 秒(实测 3.1 MB 的 C 源码 = 白等 6 分钟还是失败)。
    /// </summary>
    public static class C工具链C
    {
        private static C编译器C? _选定;          // 已经真编过一遍、确认能用这一套
        private static C编译结果C? _上次没找到;   // "一个候选都没有"那档结果(免得每次都重扫一遍盘)
        private static readonly object _锁 = new();

        /// <summary>扫盘找 VS 安装目录那一段的结果(扫过一次就记住;null = 还没扫过)</summary>
        private static List<string>? _扫盘找到;
        /// <summary>上次扫盘是不是撞到预算上限停的(报错时要如实说,不许把"没扫完"说成"没有")</summary>
        private static bool _扫盘截断;
        private static readonly object _扫盘锁 = new();

        /// <summary>
        /// 把 `c源文件` 编成 `输出exe`。成功 = 本机至少有一套能编的 C 编译器。
        /// 结果**缓存**一份(自检 / 一次会话里可能问很多次,别每次都重新找)。
        /// **缓存的是"哪一套能编",不是"这次编了没有"** —— 后者会把上一份产物当成本次的结果
        /// (自检里实测踩过:先探针编了个空程序,接着每个用例都直接"成功"、exe 其实根本没生成)。
        /// </summary>
        public static C编译结果C 编成exe(string c源文件, string 输出exe) => 编成exe(c源文件, 输出exe, 关优化: false);

        /// <summary>
        /// 同上,但可以**显式选优化档**(自检的全量对拍走 `关优化: true` —— 判据是语义,
        /// 与优化档无关,而 `-O2` 编 2.5MB 的那两份浮点示例要几分钟)。
        /// </summary>
        public static C编译结果C 编成exe(string c源文件, string 输出exe, bool 关优化)
        {
            // ⚠ **一次编译不许占着这把锁**(R4-2,2026-10)。
            // 以前整个方法体都在 `lock (_锁)` 里 —— 而 `跑一套` 是**起一个 `cl.exe` 真编**,
            // 于是"并行 N 路"实际是**串行 N 路**:实测 67 个标准工程、C 编译器那一段累计
            // **1,369,421 毫秒(约 23 分钟)**,而整组墙钟 258 秒 —— 这个比例就是"串行"的指纹
            // (自检那一组把并发从 4 提到 8,总时长只快 2%,因为压根没并行起来)。
            // 现在锁只圈**两件极短的事**:看有没有选定的编译器、选定/作废它。
            C编译器C? 编译器 = null;
            lock (_锁) { 编译器 = _选定; }
            if (编译器 != null)
            {
                var 日志 = new StringBuilder();
                bool 成 = 跑一套(编译器, c源文件, 输出exe, 日志, 额外选项(), 关优化, out bool 超时);
                if (成 && File.Exists(输出exe) && new FileInfo(输出exe).Length > 0)
                    return new C编译结果C
                    {
                        成功 = true,
                        编译器 = 编译器.名称 + "(" + 编译器.可执行 + ")",
                        环境说明 = 编译器.环境说明(),
                        日志 = 收尾(日志.ToString()),
                    };
                // 超时不是"这套编译器坏了",是**这份源码太大**:留着 `_选定`,也别再去逐套重试
                if (超时) return 没编完(c源文件, 日志.ToString());
                lock (_锁) { _选定 = null; }   // 上次能编、这次不行(机器状态变了)→ 重新找
            }

            var 结果 = 试编(c源文件, 输出exe, 关优化);
            if (!结果.成功 && 结果.日志.Length == 0)
                lock (_锁) { _上次没找到 = 结果; }   // "一个候选都没有"才缓存,别的下次再试
            return 结果;
        }

        /// <summary>
        /// 本机有没有能编的 C 编译器(编一个空程序试一次;选定之后就不再重复找)
        /// </summary>
        public static C编译结果C 探一下()
        {
            lock (_锁)
            {
                if (_选定 != null)
                    return new C编译结果C { 成功 = true, 编译器 = _选定.名称 + "(" + _选定.可执行 + ")", 环境说明 = _选定.环境说明() };
                if (_上次没找到 != null) return _上次没找到;
            }
            string 目录 = Path.Combine(临时区C.基目录(), "hbcc_C工具链");
            Directory.CreateDirectory(目录);
            string 源 = Path.Combine(目录, "探针.c");
            File.WriteAllText(源, "int main(void) { return 0; }\n", new UTF8Encoding(false));
            return 编成exe(源, Path.Combine(目录, "探针.exe"));
        }

        /// <summary>
        /// 编一份 C 源码,**显式带上这几个额外选项**(不读也不改 `HB_C编译器选项`)。
        ///
        /// 谁要它:R3-6-4 批 2 的**分派兜底**那条断言 —— 同一份 C 源码编两遍,
        /// 一遍按编译器默认、一遍带 `-DHB_COMPUTED_GOTO=0` 逼它走 `switch(PC)`,
        /// 看两条路的输出与整机状态是否一致。走**参数**而不是环境变量,是因为这台机器上
        /// 从 .NET 起子进程时自定义环境变量送不到(实测 `getenv` 一律 NULL)。
        /// </summary>
        public static C编译结果C 编成exe带选项(string c源文件, string 输出exe, string[] 额外选项)
        {
            // 与 `编成exe` 同一个口径:**锁只圈"看/改 _选定",编译那一趟在锁外**(R4-2)。
            C编译器C? 编译器 = null;
            lock (_锁) { 编译器 = _选定; }
            if (编译器 != null)
            {
                var 日志 = new StringBuilder();
                bool 成 = 跑一套(编译器, c源文件, 输出exe, 日志, 额外选项, 关优化: false, out bool 超时);
                if (成 && File.Exists(输出exe) && new FileInfo(输出exe).Length > 0)
                    return new C编译结果C
                    {
                        成功 = true,
                        编译器 = 编译器.名称 + "(" + 编译器.可执行 + ")",
                        环境说明 = 编译器.环境说明() + $";额外选项 = {string.Join(" ", 额外选项)}",
                        日志 = 收尾(日志.ToString()),
                    };
                if (超时) return 没编完(c源文件, 日志.ToString());   // 同上:是源码太大,不是这套不成
                lock (_锁) { _选定 = null; }
            }
            // 还没选定(或刚才那套不成):照默认那套的候选顺序逐个试,每个都带这些额外选项
            // **候选表要惰性地过**:那段迭代器的尾巴是"扫盘找 VS 安装目录" ——
            // 先把候选收成一张表,就等于"还没试呢先扫一次盘"(自检按源码钉着这一条)
            {
                var 全部 = new StringBuilder();
                bool 见了候选 = false;
                foreach (var 一个 in 列候选())
                {
                    见了候选 = true;
                    var 一套 = new StringBuilder();
                    bool 成 = 跑一套(一个, c源文件, 输出exe, 一套, 额外选项, 关优化: false, out bool 超时);
                    if (成 && File.Exists(输出exe) && new FileInfo(输出exe).Length > 0)
                    {
                        lock (_锁) { _选定 = 一个; }
                        return new C编译结果C
                        {
                            成功 = true,
                            编译器 = $"{一个.名称}({一个.可执行})",
                            环境说明 = 一个.环境说明() + $";额外选项 = {string.Join(" ", 额外选项)}",
                            日志 = 收尾(一套.ToString()),
                        };
                    }
                    全部.Append($"〔{一个.名称} · {一个.来历}〕").Append(收尾(一套.ToString())).Append('\n');
                    if (超时) return 没编完(c源文件, 全部.ToString());
                }
                if (!见了候选) return 探一下();       // 一个候选都没有:交回"探一下"那条口径(它自己会缓存)
                return new C编译结果C { 成功 = false, 日志 = 全部.ToString().TrimEnd() };
            }
        }

        private static C编译结果C 试编(string c源文件, string 输出exe, bool 关优化 = false)
        {
            var 日志 = new StringBuilder();
            bool 见了候选 = false;
            // 逐个**惰性**试(见 `列候选` 的说明):前面有一套能编的(环境变量点名 / LLVM 目录 / PATH 上的
            // clang / gcc / cl),后面那段兜底扫盘根本不会被枚举到 —— 这正是"切到原生 C 就卡死"要的那一刀。
            foreach (var 编译器 in 列候选())
            {
                见了候选 = true;
                var 一套 = new StringBuilder();
                bool 成 = 跑一套(编译器, c源文件, 输出exe, 一套, null, 关优化, out bool 超时);
                if (成 && File.Exists(输出exe) && new FileInfo(输出exe).Length > 0)
                {
                    _选定 = 编译器;
                    return new C编译结果C
                    {
                        成功 = true,
                        编译器 = $"{编译器.名称}({编译器.可执行})",
                        环境说明 = 编译器.环境说明(),
                        日志 = 收尾(一套.ToString()),
                    };
                }
                日志.Append($"〔{编译器.名称} · {编译器.来历}〕").Append(收尾(一套.ToString())).Append('\n');
                if (超时) return 没编完(c源文件, 日志.ToString());
            }
            if (!见了候选) return new C编译结果C { 成功 = false, 找不到原因 = 找不着说明() };
            return new C编译结果C { 成功 = false, 日志 = 日志.ToString().TrimEnd() };
        }

        /// <summary>
        /// 这一套 C 编译器**编一份源码最多等多久**(秒)。R3-6-4 批 4 收尾(用户 2026-09-23 报"切到 C 就卡死"):
        /// 以前一律 180 秒 —— 实测这份仓库里的 `示例10_浮点与64位` 转出来是 **3.1 MB C 源码**,
        /// `cl /O2` 编它 **22 分钟还没完**,于是"候选 A 等 180 秒 + 候选 B 又等 180 秒",
        /// 白白烧掉 6 分钟还是失败。现在按**源码大小**给预算:180 秒打底,每 256 KB 再加 30 秒,
        /// **封顶 10 分钟**;环境变量 `HB_C编译器超时`(单位秒)可以手工盖过它。
        /// 大工程想真编出来,比等更管用的是换个没那么慢的优化档(`HB_C编译器选项` 写 `-O0` / `/Od`)。
        /// </summary>
        public static int 编译预算秒(string c源文件)
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("HB_C编译器超时"), out int 手设) && 手设 > 0)
                return Math.Min(手设, 60 * 60);
            long 字节 = 0;
            try { 字节 = new FileInfo(c源文件).Length; } catch { }
            long 算 = 180 + 字节 / (256 * 1024) * 30;
            return (int)Math.Min(算, 10 * 60);
        }

        /// <summary>
        /// "预算到了还没编完"那一档的如实说明。
        /// **关键的一条**:这时候**不再去试别的候选** —— 同一份 C 源码换一套编译器一样慢,
        /// 换来的是又一个满预算的干等(实测就是两个 180 秒连在一起)。
        /// </summary>
        private static C编译结果C 没编完(string c源文件, string 日志)
        {
            int 秒 = 编译预算秒(c源文件);
            return new C编译结果C
            {
                成功 = false,
                找不到原因 =
                    $"这份 C 源码大,C 编译器等了 {秒} 秒还没编完(不再换别的编译器接着等 —— 换一套一样慢)。" +
                    $"要么把环境变量 HB_C编译器超时 调大(单位秒)让它慢慢编," +
                    $"要么换个没那么慢的优化档(环境变量 HB_C编译器选项 写 `-O0` / `/Od`)," +
                    $"要么换成「自带运行时·单文件」那一档,或者切「只编译」把 C 源码自己拿去编。",
                日志 = 收尾(日志),
            };
        }

        /// <summary>"这台机器上没找到 C 编译器"那一档的说明(带改法;兜底扫盘被预算截断时如实补一句)</summary>
        private static string 找不着说明()
        {
            string 尾巴 = _扫盘截断
                ? "另外:兜底扫盘找 Visual Studio 安装目录到预算上限就停了(目录树太大),装在非标准位置的话可以直接用 HB_C编译器 指路。"
                : "";
            return "这台机器上没找到 C 编译器(clang / gcc / cc / tcc / MSVC 的 cl 都没有)。" +
                "装一套就能用:`winget install LLVM.LLVM`(clang),或者给 Visual Studio 装上 C++ 工作负载;" +
                "也可以设环境变量 HB_C编译器 直接指到 clang.exe / cl.exe。" + 尾巴;
        }

        private static string 收尾(string 文本)
        {
            string 一行行 = 文本.Replace("\r\n", "\n").Trim();
            if (一行行.Length <= 1500) return 一行行;
            return "…(只留尾部)" + 一行行[^1500..];
        }

        /// <summary>
        /// 环境变量 `HB_C编译器选项` 里那串**额外编译选项**(空格 / 分号 / 逗号分隔;空 = 没有)。
        ///
        /// R3-6-4 批 2 要它来验**没有 computed goto 时那条 `switch(PC)` 兜底支路**:
        /// 给编译器加一句 `-DHB_COMPUTED_GOTO=0`(`运行时.c` 用 `#ifndef` 给命令行让路),
        /// 同一份源码编两遍 —— 一遍走标签表、一遍走 switch —— 输出与整机状态必须一致。
        /// 平时不设这个变量,用户那条路上一个额外选项都没有。
        ///
        /// **注意**:这台开发机上从 .NET 起子进程时**自定义环境变量送不过去**
        /// (`cl.exe` / 原生 exe 的 `getenv` 一律拿不到),所以自检里这条支路是
        /// `试分派兜底()` **自己起 `cl` 走 `HB_C编译器选项`**——真身不在时**如实报"没验到"**,
        /// 不许把"没验"说成"验过了"(见那条断言的详情)。
        /// </summary>
        public static string[] 额外选项()
        {
            string? 串 = Environment.GetEnvironmentVariable("HB_C编译器选项");
            if (string.IsNullOrWhiteSpace(串)) return [];
            return 串!.Split([' ', '\t', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        /// <summary>
        /// 这一次编译**用哪一档优化**。默认 `-O2`(拍板第 4 条:与现口径一致)。
        ///
        /// 自检那条**全量对拍**(26 个标准工程)显式传 `关优化: true` 换 `-Od`:
        /// 那一组的判据是**语义**(寄存器 / 内存 / 输出逐字节),与优化档无关,
        /// 而 `-O2` 编那两份 2.5MB 的浮点示例要几分钟(实测 16.5 秒 → 5 分钟以上),
        /// 拿它当"跑得完闸门"的瓶颈不值。要看优化档本身有没有问题,单独跑那次对拍即可。
        /// </summary>
        private static string 优化开关(C编译器C 编译器, bool 关优化)
        {
            if (编译器.名称 == "cl") return 关优化 ? "/Od" : "/O2";
            return 关优化 ? "-O0" : "-O2";
        }

        /// <summary>真跑一套编译器;返回"进程退出码 0 且 exe 出来了"。</summary>
        private static bool 跑一套(C编译器C 编译器, string c源文件, string 输出exe, StringBuilder 日志, bool 关优化 = false)
            => 跑一套(编译器, c源文件, 输出exe, 日志, 额外选项(), 关优化, out _);

        /// <summary>同上,但额外选项由调用方给(`编成exe带选项` 要拿它逼 `switch(PC)` 那条路)</summary>
        private static bool 跑一套(C编译器C 编译器, string c源文件, string 输出exe, StringBuilder 日志,
            string[]? 额外, bool 关优化 = false)
            => 跑一套(编译器, c源文件, 输出exe, 日志, 额外, 关优化, out _);

        /// <summary>
        /// 同上,但把"是不是**预算到了还没编完**"报给调用方 —— `试编` 据此**不再往下试别的候选**
        /// (R3-6-4 批 4 收尾:以前两个候选各等 180 秒 = 白等 6 分钟,还是失败)。
        /// </summary>
        private static bool 跑一套(C编译器C 编译器, string c源文件, string 输出exe, StringBuilder 日志,
            string[]? 额外, bool 关优化, out bool 超时)
        {
            超时 = false;
            string[] 加 = 额外 ?? 额外选项();
            string 输出目录 = Path.GetDirectoryName(Path.GetFullPath(输出exe)) ?? ".";
            Directory.CreateDirectory(输出目录);
            try { if (File.Exists(输出exe)) File.Delete(输出exe); } catch { }

            var 起 = new ProcessStartInfo(编译器.可执行)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(c源文件)) ?? ".",
            };
            if (编译器.名称 == "cl")
            {
                if (编译器.头文件目录 != null) 起.Environment["INCLUDE"] = 编译器.头文件目录;
                if (编译器.库目录 != null) 起.Environment["LIB"] = 编译器.库目录;
            }
            if (编译器.名称 == "cl")
            {
                起.ArgumentList.Add("/nologo");
                起.ArgumentList.Add("/utf-8");
                起.ArgumentList.Add(优化开关(编译器, 关优化));
                foreach (string 一 in 加) 起.ArgumentList.Add(一);
                起.ArgumentList.Add(Path.GetFullPath(c源文件));
                起.ArgumentList.Add("/Fe:" + Path.GetFullPath(输出exe));
            }
            else
            {
                起.ArgumentList.Add(优化开关(编译器, 关优化));
                起.ArgumentList.Add("-std=c11");
                foreach (string 一 in 加) 起.ArgumentList.Add(一);
                起.ArgumentList.Add(Path.GetFullPath(c源文件));
                起.ArgumentList.Add("-o");
                起.ArgumentList.Add(Path.GetFullPath(输出exe));
                // H97:Linux 上 `sqrtf` 在 libm 里,不写 `-lm` 就是 `undefined reference to 'sqrtf'`
                // (`运行时.c:43` 有 `#include <math.h>`、`:338` 用 `sqrtf`;`.NET` 那条腿由 `cl` 自己带 CRT)。
                // `cl` 那一支**不加**(MSVC 认不得 `-lm`);gcc / clang / MinGW 都认,排在源文件后面是**规矩**。
                起.ArgumentList.Add("-lm");
            }

            Process? 进程;
            try { 进程 = Process.Start(起); }
            catch (Exception ex) { 日志.Append($"起不来: {ex.Message}"); return false; }
            if (进程 == null) { 日志.Append("起不来: 进程是 null"); return false; }

            using (进程)
            {
                var 出任务 = 进程.StandardOutput.ReadToEndAsync();
                var 误任务 = 进程.StandardError.ReadToEndAsync();
                try { 进程.StandardInput.Close(); } catch { }
                int 预算秒 = 编译预算秒(c源文件);
                if (!进程.WaitForExit(预算秒 * 1000))
                {
                    try { 进程.Kill(entireProcessTree: true); } catch { }
                    日志.Append($"超时({预算秒} 秒没编完)");
                    超时 = true;
                    return false;
                }
                日志.Append(出任务.GetAwaiter().GetResult()).Append(误任务.GetAwaiter().GetResult());
                return 进程.ExitCode == 0;
            }
        }

        // ───────────────────────────── 找编译器 ─────────────────────────────

        private static IEnumerable<C编译器C> 列候选()
        {
            var 见过 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // ① 环境变量点名:HB_C编译器
            string? 指定 = Environment.GetEnvironmentVariable("HB_C编译器");
            if (!string.IsNullOrWhiteSpace(指定))
            {
                string 路径 = 指定!.Trim('"');
                if (File.Exists(路径))
                {
                    var 定制 = 按名字认(路径, "环境变量 HB_C编译器", out bool 是cl);
                    if (是cl) 凑MSVC环境(定制, Path.GetDirectoryName(路径)!);
                    if (见过.Add(定制.可执行)) yield return 定制;
                }
                else
                {
                    string? 找 = 找命令(路径);
                    if (找 != null)
                    {
                        var 定制 = 按名字认(找, "环境变量 HB_C编译器(PATH 上找到)", out bool 是cl);
                        if (是cl) 凑MSVC环境(定制, Path.GetDirectoryName(找)!);
                        if (见过.Add(定制.可执行)) yield return 定制;
                    }
                }
            }

            // ② clang(拍板里点名的首选):PATH 上的,以及 LLVM 常见安装目录
            foreach (string? 目录 in new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles") is { } pf ? Path.Combine(pf, "LLVM", "bin") : null,
                Environment.GetEnvironmentVariable("ProgramFiles(x86)") is { } pf86 ? Path.Combine(pf86, "LLVM", "bin") : null,
                Environment.GetEnvironmentVariable("LOCALAPPDATA") is { } la ? Path.Combine(la, "Programs", "LLVM", "bin") : null,
            })
            {
                if (目录 == null) continue;
                string 候选 = Path.Combine(目录, "clang.exe");
                if (File.Exists(候选) && 见过.Add(候选))
                    yield return new C编译器C { 名称 = "clang", 可执行 = 候选, 来历 = "LLVM 安装目录", 支持计算跳转 = true };
            }
            foreach (string 名字 in new[] { "clang", "clang.exe" })
            {
                string? 找 = 找命令(名字);
                if (找 != null && 见过.Add(找))
                    yield return new C编译器C { 名称 = "clang", 可执行 = 找, 来历 = "PATH", 支持计算跳转 = true };
            }

            // ③ gcc / cc / tcc(gcc 与 clang 一样支持 computed goto)
            foreach (string 名字 in new[] { "gcc", "gcc.exe", "cc", "cc.exe", "tcc", "tcc.exe" })
            {
                string? 找 = 找命令(名字);
                if (找 == null || !见过.Add(找)) continue;
                bool 是tcc = Path.GetFileName(找).StartsWith("tcc", StringComparison.OrdinalIgnoreCase);
                yield return new C编译器C { 名称 = 是tcc ? "tcc" : "gcc", 可执行 = 找, 来历 = "PATH", 支持计算跳转 = !是tcc };
            }

            // ④ MSVC 的 cl
            foreach (var msvc in 列MSVC())
                if (见过.Add(msvc.可执行)) yield return msvc;
        }

        private static C编译器C 按名字认(string 路径, string 来历, out bool 是cl)
        {
            string 名 = Path.GetFileName(路径);
            是cl = 名.Equals("cl.exe", StringComparison.OrdinalIgnoreCase) || 名.Equals("cl", StringComparison.OrdinalIgnoreCase);
            bool 是clang = 名.StartsWith("clang", StringComparison.OrdinalIgnoreCase);
            return new C编译器C
            {
                名称 = 是cl ? "cl" : (是clang ? "clang" : (名.StartsWith("tcc", StringComparison.OrdinalIgnoreCase) ? "tcc" : "gcc")),
                可执行 = 路径,
                来历 = 来历,
                支持计算跳转 = !是cl,
            };
        }

        /// <summary>PATH 上找可执行文件(带上 .exe 与常见后缀)</summary>
        private static string? 找命令(string 名字)
        {
            string 路径变量 = Environment.GetEnvironmentVariable("PATH") ?? "";
            string[] 后缀 = Path.HasExtension(名字) ? new[] { "" } : new[] { ".exe", "" };
            foreach (string 目录 in 路径变量.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string 干净 = 目录.Trim().Trim('"');
                if (干净.Length == 0) continue;
                foreach (string 后缀名 in 后缀)
                {
                    try
                    {
                        string 全 = Path.Combine(干净, 名字 + 后缀名);
                        if (File.Exists(全)) return 全;
                    }
                    catch { }
                }
            }
            return null;
        }

        /// <summary>
        /// 找 MSVC:先看 PATH 上那个(在"开发人员命令提示符"里 INCLUDE / LIB 已经齐了),
        /// 再去 VS 安装目录里找,并照已知布局凑出 INCLUDE / LIB。
        /// </summary>
        private static IEnumerable<C编译器C> 列MSVC()
        {
            string? 在path = 找命令("cl");
            if (在path != null)
            {
                var 直接用 = new C编译器C { 名称 = "cl", 可执行 = 在path, 来历 = "PATH(开发人员命令提示符)", 支持计算跳转 = false };
                凑MSVC环境(直接用, Path.GetDirectoryName(在path)!);
                yield return 直接用;
            }

            foreach (string vs根 in 找VS根())
                foreach (C编译器C 一套 in 从VS根认cl(vs根))
                    yield return 一套;

            // 兜底:扫盘找(带预算、结果缓存)。**只有**前面 vswhere / 标准位置一套都没给出能编的 cl
            // 才会走到这儿 —— 用户 2026-09-23 报的"切到 C 就卡死",病根之一就是这一段以前**无条件**跑。
            foreach (string vs根 in 扫盘找VS根())
                foreach (C编译器C 一套 in 从VS根认cl(vs根))
                    yield return 一套;
        }

        /// <summary>一个 VS 安装根下面能用的那几套 cl(INCLUDE / LIB 凑不齐的跳过,免得白报一堆 C1083)</summary>
        private static IEnumerable<C编译器C> 从VS根认cl(string vs根)
        {
            foreach (string cl in 找cl程序(vs根))
            {
                var 一套 = new C编译器C { 名称 = "cl", 可执行 = cl, 来历 = $"VS 安装目录 {vs根}", 支持计算跳转 = false };
                凑MSVC环境(一套, Path.GetDirectoryName(cl)!);
                if (一套.头文件目录 == null || 一套.库目录 == null) continue;
                yield return 一套;
            }
        }

        /// <summary>
        /// VS 安装根 —— **快的那两条路(不扫盘)**:先问 vswhere(毫秒级),再看标准安装位置。
        /// 扫盘那一段在 `扫盘找VS根`(兜底,带预算)。
        /// </summary>
        private static IEnumerable<string> 找VS根()
        {
            var 见过 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? vswhere in new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles(x86)") is { } a ? Path.Combine(a, "Microsoft Visual Studio", "Installer", "vswhere.exe") : null,
                Environment.GetEnvironmentVariable("ProgramFiles") is { } b ? Path.Combine(b, "Microsoft Visual Studio", "Installer", "vswhere.exe") : null,
            })
            {
                if (vswhere == null || !File.Exists(vswhere)) continue;
                foreach (string 一条 in 问vswhere(vswhere))
                    if (Directory.Exists(一条) && 见过.Add(一条)) yield return 一条;
            }
            foreach (string 一条 in 标准VS位置())
                if (Directory.Exists(一条) && 见过.Add(一条)) yield return 一条;
        }

        /// <summary>VS 的**标准安装位置**(`Program Files` 下那几代 × 那几个版;只碰这几个目录,不递归)</summary>
        private static IEnumerable<string> 标准VS位置()
        {
            foreach (string? 根 in new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
            })
            {
                if (根 == null) continue;
                foreach (string 代 in new[] { "18", "2022", "2019" })
                    foreach (string 版 in new[] { "Enterprise", "Professional", "Community", "BuildTools", "Preview" })
                        yield return Path.Combine(根, "Microsoft Visual Studio", 代, 版);
            }
        }

        /// <summary>
        /// 兜底:扫盘找 VS 安装目录 —— 从每个盘符 + 盘符下的 `software` / `tools` 往下三层找叫
        /// `Microsoft Visual Studio` 的目录。**两条上限**,都写死在 `扫盘预算毫秒` / `扫盘目录上限`:
        ///   · 用户 2026-09-23 报的"切到原生 C 就卡死"里有一段就是它 —— 目录树一大(实测这台机器
        ///     五万多个目录、十几秒;更大的树就是几分钟),而且以前**每次都重扫**;
        ///   · 现在:① 排在 vswhere / 标准位置**后面**(见 `列MSVC`),② 扫过就缓存(`_扫盘找到`),
        ///     ③ 到预算就停,并**如实记下"是撞预算停的"**(`_扫盘截断`,报错里要说清)。
        /// </summary>
        private static List<string> 扫盘找VS根()
        {
            lock (_扫盘锁)
            {
                if (_扫盘找到 != null) return _扫盘找到;
            }
            var 出 = new List<string>();
            var 预算 = new 扫盘预算C();
            foreach (string 目录 in 扫盘起点())
                foreach (string 找 in 往下找(目录, "Microsoft Visual Studio", 3, 预算))
                    出.Add(找);
            lock (_扫盘锁)
            {
                _扫盘截断 = 预算.截断;
                return _扫盘找到 = 出;
            }
        }

        /// <summary>扫盘起点:每个存在的盘符,加上盘符下的 `software` / `tools`(装机常去的那两层)</summary>
        private static IEnumerable<string> 扫盘起点()
        {
            foreach (char 盘 in "CDEFGHIJ")
            {
                string 根 = 盘 + ":\\";
                if (!Directory.Exists(根)) continue;
                yield return 根;
                yield return Path.Combine(根, "software");
                yield return Path.Combine(根, "tools");
            }
        }

        /// <summary>
        /// 兜底扫盘的预算:时间(毫秒)+ 目录数两个上限,谁先到算谁。
        /// **宁可如实说"没扫完",也不能让界面 / 命令行在这一段上无限等。**
        /// </summary>
        private sealed class 扫盘预算C
        {
            private readonly Stopwatch _秒表 = Stopwatch.StartNew();
            private int _扫过;
            private bool _截断;

            /// <summary>还能接着扫吗(每次都看钟:`GetDirectories` 单次再慢也顶不穿太多)</summary>
            public bool 还有()
            {
                if (_秒表.ElapsedMilliseconds >= 扫盘预算毫秒 || _扫过 >= 扫盘目录上限)
                {
                    _截断 = true;
                    return false;
                }
                return true;
            }

            public void 记(int 个数) => _扫过 += 个数;

            /// <summary>是不是"撞预算停的"(撞了要在"没找到 C 编译器"那条说明里补一句)</summary>
            public bool 截断 => _截断;
        }

        /// <summary>兜底扫盘的时间预算(毫秒):最多扫这么久(以前没有上限)</summary>
        public const int 扫盘预算毫秒 = 5000;

        /// <summary>兜底扫盘最多枚举多少个目录(与时间预算谁先到算谁)</summary>
        public const int 扫盘目录上限 = 50000;

        /// <summary>问一次 vswhere,拿它报的 VS 安装路径(每一行一个;问不动就什么都不给)</summary>
        private static List<string> 问vswhere(string vswhere)
        {
            var 结果 = new List<string>();
            try
            {
                var 起 = new ProcessStartInfo(vswhere)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                起.ArgumentList.Add("-latest");
                起.ArgumentList.Add("-products");
                起.ArgumentList.Add("*");
                起.ArgumentList.Add("-property");
                起.ArgumentList.Add("installationPath");
                using var 进程 = Process.Start(起);
                if (进程 == null) return 结果;
                string 出 = 进程.StandardOutput.ReadToEnd();
                进程.WaitForExit(10_000);
                foreach (string 行 in 出.Replace("\r\n", "\n").Split('\n'))
                {
                    string 候选 = 行.Trim();
                    if (候选.Length > 0) 结果.Add(候选);
                }
            }
            catch { }
            return 结果;
        }

        /// <summary>在 `根` 下面最多 `层` 层里找名字等于 `名字` 的目录(浅层优先,别扫全盘;`预算` 见底就停)</summary>
        private static IEnumerable<string> 往下找(string 根, string 名字, int 层, 扫盘预算C 预算)
        {
            if (层 < 0 || !预算.还有()) yield break;
            string[] 子;
            try { 子 = Directory.GetDirectories(根); } catch { yield break; }
            预算.记(子.Length);
            foreach (string 一 in 子)
            {
                if (string.Equals(Path.GetFileName(一), 名字, StringComparison.OrdinalIgnoreCase)) yield return 一;
            }
            if (层 == 0) yield break;
            foreach (string 一 in 子)
            {
                foreach (string 找 in 往下找(一, 名字, 层 - 1, 预算)) yield return 找;
                if (!预算.还有()) yield break;   // 预算见底:停在半路,别再往剩下的兄弟目录里扫
            }
        }

        /// <summary>VS 根下面所有 `VC\Tools\MSVC\*\bin\Hostx64\x64\cl.exe`</summary>
        private static IEnumerable<string> 找cl程序(string vs根)
        {
            string 工具 = Path.Combine(vs根, "VC", "Tools", "MSVC");
            if (!Directory.Exists(工具)) yield break;
            string[] 版本目录;
            try { 版本目录 = Directory.GetDirectories(工具); } catch { yield break; }
            foreach (string 版本 in 版本目录.OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal))
            {
                foreach (string 架构 in new[] { Path.Combine("bin", "Hostx64", "x64"), Path.Combine("bin", "Hostx64", "x86") })
                {
                    string 候选 = Path.Combine(版本, 架构, "cl.exe");
                    if (File.Exists(候选)) yield return 候选;
                }
            }
        }

        /// <summary>
        /// 按已知布局给 `cl` 凑 INCLUDE / LIB(环境里已经有、且目录还在的就不动)。
        /// 两套布局都认:①标准 MSVC + Windows SDK;②VS 自带的 `SDK\ScopeCppSDK\vc15` 那一份(实测这台机器就是它)。
        /// 凑不齐就把两个字段留空 —— 调用方据此**跳过**这套候选,别让它去编出一堆 C1083。
        /// </summary>
        private static void 凑MSVC环境(C编译器C 一套, string bin目录)
        {
            if (一套.头文件目录 != null && 一套.库目录 != null) return;

            // PATH 上那个 cl:开发人员命令提示符已经把 INCLUDE / LIB 设好了
            if (环境里有("INCLUDE", 套 => Directory.Exists(套)) && 环境里有("LIB", 套 => Directory.Exists(套)))
            {
                一套.头文件目录 = Environment.GetEnvironmentVariable("INCLUDE")!;
                一套.库目录 = Environment.GetEnvironmentVariable("LIB")!;
                return;
            }

            // 从 bin 往上找到"VS 根"(有 VC\Tools\MSVC 的那一层)与版本目录
            string? 版本目录 = null, vs根 = null;
            var 目录 = new DirectoryInfo(bin目录);
            for (int 上 = 0; 上 < 6 && 目录 != null; 上++, 目录 = 目录.Parent)
            {
                if (目录.Name.Equals("MSVC", StringComparison.OrdinalIgnoreCase) && 目录.Parent != null)
                {
                    版本目录 = 目录.Parent.FullName;
                    vs根 = 目录.Parent.Parent?.Parent?.FullName;    // VC\Tools\MSVC\版本 → VS 根
                    break;
                }
            }

            var 头 = new List<string>();
            var 库 = new List<string>();
            void 加(List<string> 表, params string?[] 候选)
            {
                foreach (string? c in 候选)
                    if (c != null && Directory.Exists(c) && !表.Contains(c, StringComparer.OrdinalIgnoreCase)) 表.Add(c);
            }

            if (版本目录 != null)
            {
                加(头, Path.Combine(版本目录, "include"));
                加(库, Path.Combine(版本目录, "lib", "x64"), Path.Combine(版本目录, "lib", "onecore", "x64"));
            }
            if (vs根 != null)
            {
                string scope = Path.Combine(vs根, "SDK", "ScopeCppSDK", "vc15");
                加(头, Path.Combine(scope, "VC", "include"));
                加(头, Path.Combine(scope, "SDK", "include", "ucrt"),
                       Path.Combine(scope, "SDK", "include", "um"),
                       Path.Combine(scope, "SDK", "include", "shared"));
                加(库, Path.Combine(scope, "VC", "lib"), Path.Combine(scope, "SDK", "lib"));
            }
            // Windows SDK 10(装了 C++ 工作负载的正常机器走这条)
            foreach (string? kits in new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles(x86)") is { } a ? Path.Combine(a, "Windows Kits", "10") : null,
                Environment.GetEnvironmentVariable("ProgramFiles") is { } b ? Path.Combine(b, "Windows Kits", "10") : null,
            })
            {
                if (kits == null) continue;
                foreach (string 版本 in 最高版本(Path.Combine(kits, "Include")))
                    加(头, Path.Combine(版本, "ucrt"), Path.Combine(版本, "um"), Path.Combine(版本, "shared"));
                foreach (string 版本 in 最高版本(Path.Combine(kits, "Lib")))
                    加(库, Path.Combine(版本, "ucrt", "x64"), Path.Combine(版本, "um", "x64"));
            }

            // 库必须能凑到 kernel32.lib 与 CRT 两份,头必须凑到 vcruntime.h —— 不然编也是白编
            string? 有kernel = 库.FirstOrDefault(d => File.Exists(Path.Combine(d, "kernel32.lib")));
            string? 有crt = 库.FirstOrDefault(d => File.Exists(Path.Combine(d, "libcmt.lib")) || File.Exists(Path.Combine(d, "libucrt.lib")));
            string? 有vcruntime = 头.FirstOrDefault(d => File.Exists(Path.Combine(d, "vcruntime.h")));
            bool 有ucrt头 = 头.Any(d => File.Exists(Path.Combine(d, "stdio.h")) || File.Exists(Path.Combine(d, "corecrt.h")));

            if (有kernel == null || 有crt == null || 有vcruntime == null || !有ucrt头) return;
            一套.头文件目录 = string.Join(Path.PathSeparator, 头);
            一套.库目录 = string.Join(Path.PathSeparator, 库);
        }

        private static bool 环境里有(string 名, Func<string, bool> 判)
        {
            string? 值 = Environment.GetEnvironmentVariable(名);
            return !string.IsNullOrWhiteSpace(值) && 值!.Split(Path.PathSeparator).Any(x => 判(x.Trim()));
        }

        private static IEnumerable<string> 最高版本(string 根)
        {
            if (!Directory.Exists(根)) yield break;
            string[] 们;
            try { 们 = Directory.GetDirectories(根); } catch { yield break; }
            foreach (string 一 in 们.OrderByDescending(x => Path.GetFileName(x), StringComparer.Ordinal).Take(3))
                yield return 一;
        }
    }
}
