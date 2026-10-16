// ============================================================
// 综合自检C.仓库根与工作目录.cs —— H90(用户 2026-10-11 当班实测报的缺陷)
//
//   **症状**:在 IDE 里跑标准工程「示例58_真人头」,程序报
//     `0 0 0` + `打不开这个文件(路径对不对?盘上有没有?)`
//   —— 而那份模型(`Sample\模型\人头肩\头肩.obj`)明明在盘上,命令行 `hbcc -p … --run` 也读得进去。
//
//   **成因**:标准示例的数据路径一律**照仓库根写**(`Sample/模型/…`、`工具/临时/…`),
//   而 IDE 把程序放在 `HB宿主.exe` 子进程里跑 —— 子进程不指定当前目录就**继承界面进程那一份**
//   (界面住在 `bin\Debug\net10.0-windows\`),于是相对路径解到别处去,模型就"不见了"。
//   命令行那条腿不踩它:`hbcc` 本来就是在仓库根敲的。
//
//   **修法**:子进程的当前目录钉在**仓库根** —— 唯一口径在 `引擎\仓库根C.cs`;
//   IDE 那两处起通道的地方(`确保跑程序机` / `确保调试机`)都传 `跑程序工作目录()`。
//
//   这一组钉四件事:
//     ① `仓库根C.找` 的判据本身(**同时有 `引擎` 与 `Sample` 才算**,拿个假树验正反两档);
//     ② 起点必须是**程序自己住哪**(`AppContext.BaseDirectory`)—— 拿本程序验它真找到仓库根;
//     ③ ⭐ **端到端**:同一个 `HB宿主` 子进程,当前目录给对 ⇒ 相对路径读得到;给错 ⇒ 读不到
//        (给错那一趟就是用户看到的那条报错 —— 它把"修的是什么"钉死了,不是空转);
//     ④ ⭐ **真实用户路径**:按仓库根读 `Sample\模型\人头肩\头肩.obj`,顶点/面数要与盘上那份文件
//        **现数出来的**一致(不手抄数字 —— 素材换一份,这条断言跟着走)。
// ============================================================
using System;
using System.IO;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        /// <summary>
        /// H90:找跑程序用的子进程壳(`HB宿主.exe`),照 `MainWindow.跑程序通道.cs` 的 `找跑程序壳` 那条口径
        /// (优先 Release)。这里**故意自己拼一份**:自检是控制台那条腿,而 IDE 那份是界面私有方法 ——
        /// 拼法只有一句"仓库根下 HB宿主\bin\{Release,Debug}\net10.0",两边一眼能对上。
        /// </summary>
        private static (string 可执行, string[] 参数)? 仓库根找宿主壳(string 根)
        {
            foreach (string 配置 in new[] { "Release", "Debug" })
            {
                string 基 = Path.Combine(根, "HB宿主", "bin", 配置, "net10.0");
                string exe = Path.Combine(基, "HB宿主.exe");
                if (File.Exists(exe)) return (exe, [跑程序协议C.服务参数]);
                string dll = Path.Combine(基, "HB宿主.dll");
                if (File.Exists(dll)) return ("dotnet", [dll, 跑程序协议C.服务参数]);
            }
            return null;
        }

        /// <summary> 数一份 `.obj` 里有几行 `v `/`f `(与宿主 `读取模型` 同一套认法:行首那个词)。</summary>
        private static (int 顶点, int 面) 仓库根数OBJ(string 路径)
        {
            int 顶点 = 0, 面 = 0;
            foreach (string 原行 in File.ReadAllText(路径).Replace("\r\n", "\n").Split('\n'))
            {
                string 行 = 原行.Trim();
                if (行.StartsWith("v ", StringComparison.Ordinal)) 顶点++;
                else if (行.StartsWith("f ", StringComparison.Ordinal)) 面++;
            }
            return (顶点, 面);
        }

        /// <summary> 把一段 HB 源码丢进**真的子进程**里跑,收回程序自己的输出(当前目录按参数给)。</summary>
        private static string 仓库根跑子进程(string 可执行, string[] 壳参数, string? 工作目录,
            byte[] 机器码, IReadOnlyList<初值项C> 初值表, out string 跑错)
        {
            跑错 = "";
            var 缓冲 = new StringBuilder();
            var 解码 = new 文字输出C(文本 => 缓冲.Append(文本));
            try
            {
                using var 通道 = new 跑程序通道C(可执行, 壳参数, 工作目录);
                if (!通道.活着) { 跑错 = "子进程起不来:" + (通道.死因 ?? "?"); return ""; }
                var 机 = new 跨进程虚拟机C(通道)
                {
                    输入源 = () => null,
                    输出目标 = 解码.写,
                    宿主调用 = 宿主能力C.处理,
                };
                机.设内存大小(虚拟机C.默认内存大小);
                机.初值表 = 初值表;
                机.装载(机器码);
                long 步 = 0;
                while (机.单步() && 步 < 20_000_000) 步++;
                解码.收尾();
            }
            catch (Exception 错) { 跑错 = 错.Message; }
            return 缓冲.ToString().Replace("\r\n", "\n").TrimEnd('\n');
        }

        private static void 仓库根与工作目录自检()
        {
            // ---------- ① 判据本身:同时有 `引擎` 与 `Sample` 才算仓库根;两个空档都不认 ----------
            try
            {
                string 临时 = 临时区C.取目录("仓库根自检");
                Directory.CreateDirectory(临时);
                string 真根 = Path.Combine(临时, "假仓库");
                string 深 = Path.Combine(真根, "bin", "Debug", "net10.0");
                Directory.CreateDirectory(深);
                Directory.CreateDirectory(Path.Combine(真根, "引擎"));
                Directory.CreateDirectory(Path.Combine(真根, "Sample"));

                string 只有Sample = Path.Combine(临时, "只有Sample");
                Directory.CreateDirectory(Path.Combine(只有Sample, "Sample"));
                string 只有引擎 = Path.Combine(临时, "只有引擎");
                Directory.CreateDirectory(Path.Combine(只有引擎, "引擎"));

                string? 找到了 = 仓库根C.找(深);
                bool 正 = 找到了 != null
                    && string.Equals(Path.GetFullPath(找到了), Path.GetFullPath(真根), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetFullPath(仓库根C.找(真根) ?? ""), Path.GetFullPath(真根), StringComparison.OrdinalIgnoreCase);
                // ⚠ 这两个"缺一半"的目录**建在真仓库里**,所以往上走**必然**撞上真仓库根 ——
                //   判据只能是"**不许把这一层自己当成根**"(否则单看 `Sample` 就认根,是假阳),
                //   判 null 会假红(实测踩过)。
                static bool 没误认(string 假, string? 得到) =>
                    得到 == null || !string.Equals(Path.GetFullPath(得到), Path.GetFullPath(假), StringComparison.OrdinalIgnoreCase);
                bool 反 = 没误认(只有Sample, 仓库根C.找(只有Sample)) && 没误认(只有引擎, 仓库根C.找(只有引擎))
                    && 仓库根C.找(null) == null && 仓库根C.找("") == null
                    // ⚠ **"盘上不存在的路径"不是 null 那一档**:这一族的判据是"**最近的**那一层祖先仓库根",
                    //   而自检进程本来就在仓库里 ⇒ 从 `工具\临时\没有这一层` 往上照样找到真仓库根(实测)。
                    //   真·null 那一档是"往上走完都撞不上" —— 用**盘根**当起点才量得出来(仓库不在盘根上)。
                    && 仓库根C.找(Path.GetPathRoot(临时)) == null;
                记录("仓库根 H90:判据是『同时有 引擎 与 Sample 的那一层』(往上 3 层找得到;缺一半的那一层自己不许算根;空 / 走到盘根都没有给 null)",
                    正 && 反,
                    $"从 {深} 往上找到 <{找到了 ?? "(null)"}>(要的是 {真根});" +
                    $"只有 Sample ⇒ <{仓库根C.找(只有Sample) ?? "(null)"}>;只有 引擎 ⇒ <{仓库根C.找(只有引擎) ?? "(null)"}>(都不许等于它自己);" +
                    $"空 ⇒ <{仓库根C.找("") ?? "(null)"}>;盘根 {Path.GetPathRoot(临时)} ⇒ <{仓库根C.找(Path.GetPathRoot(临时)) ?? "(null)"}>");
            }
            catch (Exception ex) { 记录异常("仓库根 H90 判据本身", ex); }

            // ---------- ② 起点是"程序自己住哪":本程序应当找得到真仓库根 ----------
            string? 根 = null;
            try
            {
                根 = 仓库根C.程序运行目录();
                记录("仓库根 H90:起点按 `AppContext.BaseDirectory`(程序自己住哪)⇒ 自检进程找得到真仓库根",
                    根 != null && Directory.Exists(Path.Combine(根, "引擎")) && Directory.Exists(Path.Combine(根, "Sample")),
                    $"程序在 <{AppContext.BaseDirectory}> ⇒ 仓库根 <{根 ?? "(null)"}>;" +
                    "⚠ 这里**不许**改看当前目录 —— 那正是这条缺陷的成因");
            }
            catch (Exception ex) { 记录异常("仓库根 H90 起点", ex); }
            if (根 == null) { 跳过("仓库根 H90", "本程序不在仓库里(找不到仓库根),子进程那两条跳过"); return; }

            // ---------- ③ ⭐ 端到端:同一个子进程壳,当前目录给对能读、给错读不到 ----------
            //   给错的那一趟 = **用户看到的那条报错**(实锤"修的是这个");夹具现造,不碰仓库里的数据
            try
            {
                var 壳 = 仓库根找宿主壳(根);
                if (壳 == null) { 跳过("仓库根 H90 子进程相对路径", "找不到 HB宿主 构建(先构建 HB宿主\\HB宿主.csproj)"); }
                else
                {
                    string 夹具 = 临时区C.取目录("仓库根工作目录");
                    Directory.CreateDirectory(夹具);
                    File.WriteAllText(Path.Combine(夹具, "数据.txt"), "自检夹具42", new UTF8Encoding(false));

                    const string 源 =
                        "字符串 内容 = \"\";\n" +
                        "逻辑 成 = 读文件(\"数据.txt\", 内容, 64);\n" +
                        "如果 (成) { 输出行(内容); } 否则 { 输出行(\"打不开这个文件(路径对不对?盘上有没有?)\"); }\n" +
                        "停止\n";
                    var 编译 = new 汇编器C().汇编多文件带调试([("主程序.hb", 源)], 优化: true);
                    宿主能力C.清运行痕迹();

                    string 对 = 仓库根跑子进程(壳.Value.可执行, 壳.Value.参数, 夹具, 编译.机器码, 编译.初值表, out string 错1);
                    // 给错的那一趟:拿本程序自己的目录(`bin\…`)当当前目录 —— 那里没有 `数据.txt`
                    string 错目录 = AppContext.BaseDirectory;
                    string 不对 = 仓库根跑子进程(壳.Value.可执行, 壳.Value.参数, 错目录, 编译.机器码, 编译.初值表, out string 错2);

                    // 阴性对照**另有一条**(不指定当前目录 ⇒ 继承自检进程的):它现在是"对"还是"错"取决于
                    // 自检进程站在哪 —— 所以只记录、不判。判据只认上面那两条(一条必须读到、一条必须读不到)。
                    记录("仓库根 H90 ⭐子进程当前目录给对就读得到(相对路径的数据文件)",
                        对 == "自检夹具42" && 错1.Length == 0,
                        $"当前目录 = 夹具 <{夹具}> ⇒ 程序输出 <{对}>(要的是 自检夹具42);跑错=<{错1}>");
                    记录("仓库根 H90 ⭐子进程当前目录给错就读不到 —— 与用户看到的那条报错逐字一致",
                        不对.Contains("打不开这个文件", StringComparison.Ordinal) && 错2.Length == 0,
                        $"当前目录 = 程序自己的目录 <{错目录}> ⇒ 程序输出 <{不对}>");
                }
            }
            catch (Exception ex) { 记录异常("仓库根 H90 子进程相对路径", ex); }

            // ---------- ④ ⭐ 真实那条路:照仓库根读 `Sample\模型\人头肩\头肩.obj` ----------
            try
            {
                string 模型 = Path.Combine(根, "Sample", "模型", "人头肩", "头肩.obj");
                if (!File.Exists(模型))
                {
                    跳过("仓库根 H90 真实模型路径", "盘上没有 Sample\\模型\\人头肩\\头肩.obj");
                }
                else
                {
                    var (顶点, 面) = 仓库根数OBJ(模型);       // 现数,不手抄
                    var 壳 = 仓库根找宿主壳(根);
                    if (壳 == null) { 跳过("仓库根 H90 真实模型路径", "找不到 HB宿主 构建"); }
                    else
                    {
                        // 用的就是用户那条路:同一份相对路径、同一个宿主;
                        // `读文件` 与 `读取模型` 走的是**同一条路径解析**(`虚拟文件C.存在` / `读文本`),
                        // 所以"读得到这份文件"就把"模型找得到"钉住了 —— 而且不必为它申请三块缓冲。
                        // ⚠ 模型本身那条全链路(装进网格 / 画几百面)在 `模型库` 那一组里。
                        const string 判据源 =
                            "字符串 头 = \"\";\n" +
                            "逻辑 成 = 读文件(\"Sample/模型/人头肩/头肩.obj\", 头, 64);\n" +
                            "如果 (成) { 输出行(1); } 否则 { 输出行(0); }\n" +
                            "停止\n";
                        var 编译 = new 汇编器C().汇编多文件带调试([("主程序.hb", 判据源)], 优化: true);
                        宿主能力C.清运行痕迹();
                        string 出 = 仓库根跑子进程(壳.Value.可执行, 壳.Value.参数, 根, 编译.机器码, 编译.初值表, out string 错);
                        记录("仓库根 H90 ⭐真实那条路:子进程照仓库根读得到 `Sample/模型/人头肩/头肩.obj`",
                            出.Trim() == "1" && 错.Length == 0,
                            $"程序输出 <{出.Trim()}>(1 = 读到);盘上那份现数 = {顶点} 顶点 / {面} 面");
                    }
                }
            }
            catch (Exception ex) { 记录异常("仓库根 H90 真实模型路径", ex); }

            // ---------- ⑤ 接线:界面那两处起通道的地方**必须**把工作目录传下去 ----------
            // 为什么要单钉这一条(本组前面那几条行为断言**管不到它**):H90 的修法有两半 ——
            //   一半是"仓库根怎么找"(上面 ①②),另一半是"找到之后**有没有人用它**"。
            //   把那一句删掉,前面四条行为断言**照样全绿**(自检自己传的是仓库根),
            //   而 IDE 里又会变回"打不开这个文件"。这正是本缺陷第一次出现的方式。
            const string 接线断言 = "仓库根 H90 接线:界面两处起通道的地方都把工作目录传下去了";
            try
            {
                string? 根3 = 仓库根C.程序运行目录();
                string 界面文件 = 根3 == null ? "" : Path.Combine(根3, "MainWindow.跑程序通道.cs");
                if (!File.Exists(界面文件))
                {
                    跳过(接线断言, "找不到 MainWindow.跑程序通道.cs(不在工作副本里跑就跳过)");
                }
                else
                {
                    string 源 = File.ReadAllText(界面文件, Encoding.UTF8);
                    int 传了几处 = 源.Split(", 跑程序工作目录())").Length - 1;
                    bool 有兜底 = 源.Contains("跑程序工作目录() => 仓库根C.程序运行目录()", StringComparison.Ordinal);
                    记接线(接线断言, 传了几处 == 2 && 有兜底,
                        $"界面里传工作目录的地方 = {传了几处} 处(该 2:`确保跑程序机` / `确保调试机`);" +
                        $"那一句是不是转发给 `仓库根C.程序运行目录()` = {有兜底}" +
                        " —— 接线哨兵(读界面源码文本判的),H90 的行为面看上面四条");
                }
            }
            catch (Exception ex) { 记录异常(接线断言, ex); }
        }
    }
}
