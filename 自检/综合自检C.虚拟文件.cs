using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        /// <summary>
        /// H64:**文件适配口子**(唯一一份文件读写入口 + `内容://` 那套虚拟路径 + 手机端 SAF 接线)。
        ///
        /// 这一组里三类东西分得很清(它们的证据强度不一样,别混着看):
        ///   · **行为**(在下面那个内存替身上真跑):虚拟路径的几条规则、`虚拟文件C` 的路由、
        ///     文本落盘(BOM / 严格 UTF-8 的文案)走的是不是同一条口径、**工程在虚拟树里的整套编译链**
        ///     (`.hbp` → 启动文件 → 源码 → `包含` 进来的库 → 展开出机器码);
        ///   · **接线哨兵**(读仓库源码文本判):手机端那几处真的接上了没有
        ///     (挑目录走 SAF / 挂到 `虚拟文件C` / 结果回调 / 界面把虚拟路径当路径用);
        ///   · **桌面不变**:不挂任何实现时,`虚拟文件C` 必须落到 `真实文件实现C`
        ///     —— 这是"加了 H64 之后桌面 / 命令行行为一字不改"的那条硬边界。
        ///
        /// 为什么行为那半能离线验:**引擎那一侧只认 `文件接口C`**,SAF 只是它的一份实现
        /// (手机端那个 `内容文件实现C`)。这里喂一份**内存替身**,于是"虚拟路径 → 引擎各处"这一整条
        /// 在命令行里就能真跑;真机那一步(真的去读相册 / 文档)按 H29 / H10 的老口径**留给人看一份**。
        /// </summary>
        private static void 虚拟文件自检()
        {
            string[] 名表 =
            [
                "虚拟文件 H64:虚拟路径的认定与拼接(记号带冒号撞不上真路径;拼接不落反斜杠)",
                "虚拟文件 H64:虚拟路径的目录 / 光名(虚拟走字符串,真路径照旧走 Path)",
                "虚拟文件 H64:没挂实现时一切照旧走 `真实文件实现C`(桌面 / 命令行行为不变)",
                "虚拟文件 H64:挂上实现之后只有虚拟路径走替身,沙箱里的真路径照旧走真盘(两边并存)",
                "虚拟文件 H64:文本落盘的 BOM 口径在虚拟路径上也成立(原文件有 BOM 写回就带,没有就不带)",
                "虚拟文件 H64:挂上系统目录之后,工程文件 / 启动文件 / `包含` 的库都在虚拟树里读出来并编成机器码",
                "虚拟文件 H64:问在不在 / 拼路径 这两步在虚拟树上给的是真答案(中间层 `包含` 靠它们)",
                "虚拟文件 H64:虚拟树里的一句 `包含` 也拉得动库文件(按文件表里那份的目录解析,走虚拟文件接口)",
                "虚拟文件 H64:程序里的文件能力(读文件 / 列目录 / 分块读 / 写文件 / 追加)作用在系统目录那一棵树上",
                "虚拟文件 H64:分块读(读字节块)在虚拟树上也是逐字节对得上的(SAF 那份实现少一个方法就编不过)",
                "虚拟文件 H64:接线 —— 手机端「打开工程目录…」先弹 SAF 的系统目录选择器,拿到的是虚拟路径",
                "虚拟文件 H64:接线 —— 挑回来的那一棵树挂进 `虚拟文件C`,摘的时候也摘干净",
                "虚拟文件 H64:接线 —— SAF 的结果回调接在 Activity 上,拿的是**跨重启**的读写授权",
                "虚拟文件 H64:接线 —— 界面与保存走虚拟路径(`虚拟文件C` / `虚拟路径C` 那一套,不再直接 File)",
                "虚拟文件 H64:接线 —— 帮助《第24节·手机端》写明系统目录这一档(不再写「不在这一档里」)",
            ];

            int 序号 = 0;
            try
            {
                // ============ 一、虚拟路径的规则(纯字符串:不读盘、不看平台) ============

                string 真路径样例 = Path.Combine("E:", "Codes", "工程", "主程序.hb");
                string 记号线 = 虚拟路径C.记号;
                bool 认定好 =
                    !虚拟路径C.是虚拟(真路径样例)
                    && !虚拟路径C.是虚拟("")
                    && !虚拟路径C.是虚拟(null)
                    && !虚拟路径C.是虚拟("content://com.android.externalstorage.documents/tree/primary")
                    && 虚拟路径C.是虚拟(记号线 + "树primary/工程")
                    && 记号线.Contains(':')
                    && !真路径样例.Contains(记号线);
                string 拼虚拟 = 虚拟路径C.拼接(记号线 + "树primary", "库/排序.hb");
                string 拼真 = 虚拟路径C.拼接(Path.Combine("E:", "Codes"), "工程");
                bool 拼接好 = 拼虚拟 == 记号线 + "树primary/库/排序.hb" && !拼虚拟.Contains('\\')
                    && 拼真 == Path.Combine("E:", "Codes", "工程");
                记录(名表[序号++], 认定好 && 拼接好,
                    $"认定:真路径 {虚拟路径C.是虚拟(真路径样例)} / 记号 {虚拟路径C.是虚拟(记号线 + "树x")};" +
                    $"拼接:虚拟[{拼虚拟}] 真[{拼真}]");

                string 虚拟文件 = 记号线 + "树primary/工程/主程序.hb";
                string 真文件 = Path.Combine("E:", "Codes", "工程", "主程序.hb");
                bool 目录光名好 =
                    虚拟路径C.所在目录(虚拟文件) == 记号线 + "树primary/工程"
                    && 虚拟路径C.文件名(虚拟文件) == "主程序.hb"
                    && 虚拟路径C.文件名无扩展(虚拟文件) == "主程序"
                    && 虚拟路径C.所在目录(真文件) == Path.Combine("E:", "Codes", "工程")
                    && 虚拟路径C.文件名(真文件) == "主程序.hb"
                    && 虚拟路径C.所在目录(记号线 + "树primary") == ""
                    && 虚拟路径C.显示(虚拟文件) == "树primary/工程/主程序.hb";
                记录(名表[序号++], 目录光名好,
                    $"虚拟:{虚拟路径C.所在目录(虚拟文件)} / {虚拟路径C.文件名(虚拟文件)};" +
                    $"真:{虚拟路径C.所在目录(真文件)} / {虚拟路径C.文件名(真文件)}");

                // ============ 二、路由:没挂 = 真文件;挂了也只认虚拟路径(桌面那条边界) ============

                string 临时 = Path.Combine(临时区C.基目录(), "hb虚拟文件_" + Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    Directory.CreateDirectory(临时);
                    string 盘上 = Path.Combine(临时, "盘上.hb");
                    File.WriteAllText(盘上, "// 盘上这一份\n停止", new UTF8Encoding(false));
                    Directory.CreateDirectory(Path.Combine(临时, "子"));

                    bool 没挂好 = 虚拟文件C.挂着 == null
                        && 虚拟文件C.存在(盘上)
                        && !虚拟文件C.存在(Path.Combine(临时, "没有这一份.hb"))
                        && 虚拟文件C.读文本(盘上).Contains("盘上这一份", StringComparison.Ordinal)
                        && 虚拟文件C.列文件(临时, ".hb").Count == 1;
                    记录(名表[序号++], 没挂好, $"没挂实现时:存在={虚拟文件C.存在(盘上)} 读回={虚拟文件C.读文本(盘上).Trim()}");

                    // ============ 三、挂上替身:虚拟走替身、真路径照旧走真盘 ============
                    var 替身 = new 内存文件替身C();
                    替身.写("内容://树primary/工程/数据.txt", Encoding.UTF8.GetBytes("替身里的数据"));
                    虚拟文件C.挂上(new 路径分派表C().挂("内容://树primary", 替身));
                    try
                    {
                        string 虚拟数据 = "内容://树primary/工程/数据.txt";
                        bool 两边并存好 =
                            虚拟文件C.挂着 != null
                            && 虚拟文件C.虚拟实现(虚拟数据) != null
                            && 虚拟文件C.取(盘上) == 真实文件实现C.单例
                            && 虚拟文件C.读文本(虚拟数据) == "替身里的数据"
                            && 虚拟文件C.读文本(盘上).Contains("盘上这一份", StringComparison.Ordinal)
                            && 虚拟文件C.存在(虚拟数据) && !虚拟文件C.存在(盘上 + ".没有");
                        记录(名表[序号++], 两边并存好,
                            $"虚拟路径交给平台实现={虚拟文件C.虚拟实现(虚拟数据) != null};" +
                            $"真路径仍走真盘={虚拟文件C.取(盘上) == 真实文件实现C.单例}");

                        // BOM:替身里先放一份带 BOM 的、一份不带 BOM 的,写回之后照旧
                        string 带BOM = "内容://树primary/工程/有BOM.hb";
                        string 不带BOM = "内容://树primary/工程/无BOM.hb";
                        替身.写(带BOM, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("变量 初值 = 1;")).ToArray());
                        替身.写(不带BOM, Encoding.UTF8.GetBytes("变量 初值 = 2;"));
                        虚拟文件C.写文本(带BOM, "改过了;");
                        虚拟文件C.写文本(不带BOM, "也改过了;");
                        bool BOM好 =
                            替身.字节(带BOM).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble())
                            && !替身.字节(不带BOM).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble())
                            && 虚拟文件C.读文本(带BOM) == "改过了;"
                            && 虚拟文件C.读文本(不带BOM) == "也改过了;";
                        记录(名表[序号++], BOM好,
                            $"带 BOM 的那份头三字节={BitConverter.ToString(替身.字节(带BOM).Take(3).ToArray())}(应为 EF-BB-BF);" +
                            $"不带的那份={BitConverter.ToString(替身.字节(不带BOM).Take(3).ToArray())}");

                        // ============ 四、整条编译链在虚拟树里跑一遍 ============
                        string 工程根 = 虚拟路径C.收正(虚拟路径C.记号 + "树primary/工程");
                        替身.写(工程根 + "/库/工具.hb", Encoding.UTF8.GetBytes(
                            "方法 取数() 返回 整数 {\n    返回 7;\n}\n"));
                        替身.写(工程根 + "/主程序.hb", Encoding.UTF8.GetBytes(
                            "测试输出(取数());\n停止\n// 预期输出: 7\n"));                        替身.写(工程根 + "/演示.hbp", Encoding.UTF8.GetBytes(
                            "{\n  \"名称\": \"虚拟树工程\",\n  \"文件\": [\"主程序.hb\", \"库/工具.hb\"],\n  \"启动文件\": \"主程序.hb\"\n}\n"));

                        var 找到的工程 = 工程文件C.找工程文件(工程根);
                        var 工程 = 工程文件C.加载(找到的工程!);
                        var 输入 = 工程.读全部编译输入();
                        string 启动全称 = 工程.绝对路径(工程.启动文件);
                        bool 读齐了 = 输入.Count == 2
                            && 虚拟路径C.是虚拟(启动全称)
                            && 输入[0].名称 == 启动全称
                            && 输入[1].名称 == 工程根 + "/库/工具.hb"
                            && 输入[1].内容.Contains("方法 取数()", StringComparison.Ordinal);

                        var 汇编器 = new 汇编器C();
                        string 展开说 = "";
                        try
                        {
                            var 结果 = 汇编器.汇编多文件带调试(输入);
                            var 缓冲 = new StringBuilder();
                            var 机 = new 虚拟机C { 输出目标 = b => 缓冲.Append((char)b) };
                            机.运行(结果.机器码);
                            string 出 = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(缓冲.ToString()));
                            展开说 = $"输入 {输入.Count} 份(启动={虚拟路径C.显示(输入[0].名称)}) 机器码 {结果.机器码.Length} 字节 输出[{出.Trim()}]";
                            记录(名表[序号++], 读齐了 && 结果.机器码.Length > 0 && 出.Contains("7", StringComparison.Ordinal), 展开说);
                        }
                        catch (Exception 展开错)
                        {
                            记录(名表[序号++], false,
                                $"{展开错.Message} —— 输入 {输入.Count} 份:" +
                                string.Join(" | ", 输入.Select(x => 虚拟路径C.显示(x.名称))) +
                                " · 替身里的键:" + 替身.键清单());
                        }

                        // 中间层 `尝试包含` 走的那两步(拿真路径字符串直接量,免得再被别的层绕过去)
                        string 库全称 = 工程根 + "/库/工具.hb";
                        string 拼出来的 = 虚拟路径C.收正(虚拟路径C.拼接(虚拟路径C.所在目录(启动全称), "库/工具.hb"));
                        bool 存在探针好 = 虚拟文件C.存在(库全称) && 虚拟文件C.存在(虚拟路径C.拼接(工程根, "库/工具.hb"));
                        记录(名表[序号++], 存在探针好,
                            $"存在({虚拟路径C.显示(库全称)})={虚拟文件C.存在(库全称)};" +
                            $"拼出来[{虚拟路径C.显示(拼出来的)}] 它存在={虚拟文件C.存在(拼出来的)};" +
                            $"启动文件所在目录[{虚拟路径C.显示(虚拟路径C.所在目录(启动全称))}]");

                        // 单文件那一路(手机端「打开文件…」那条):只给启动文件,库靠文件里的 `包含` 自己拉进来
                        bool 包含好 = false;
                        string 包含说 = "";
                        try
                        {
                            var 器2 = new 汇编器C();
                            var 果2 = 器2.汇编多文件带调试([(启动全称, "包含 \"库/工具.hb\"\n测试输出(取数());\n停止\n")]);
                            var 缓2 = new StringBuilder();
                            var 机2 = new 虚拟机C { 输出目标 = b => 缓2.Append((char)b) };
                            机2.运行(果2.机器码);
                            string 出2 = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(缓2.ToString()));
                            包含好 = 果2.机器码.Length > 0 && 出2.Contains("7", StringComparison.Ordinal);
                            包含说 = $"机器码 {果2.机器码.Length} 字节 输出[{出2.Trim()}]";
                        }
                        catch (Exception 包含错) { 包含说 = 包含错.Message; }
                        记录(名表[序号++], 包含好, 包含说);

                        // ============ 五、程序里的文件能力作用在这一棵树上 ============
                        // 判据:这一层该看见的那几样都在(不写死"一共几项" —— 夹具多塞一份就假红,那是坏断言)
                        var 根下名单 = 虚拟文件C.列名字(工程根);
                        bool 文件能力好 =
                            根下名单.Contains("库") && 根下名单.Contains("主程序.hb") && 根下名单.Contains("数据.txt")
                            && 虚拟文件C.列文件(工程根, ".hb").Any(f => 虚拟路径C.文件名(f) == "主程序.hb")
                            && 虚拟文件C.是目录(工程根 + "/库")
                            && !虚拟文件C.是目录(工程根 + "/主程序.hb");
                        // 字符串变量那一套的头部约定:读文件要一个"头地址"与池指针槽,这里直接量"替身收到了什么"
                        替身.写(工程根 + "/盘外.txt", Encoding.UTF8.GetBytes("替身里的数据"));
                        虚拟文件C.追加字节(工程根 + "/盘外.txt", Encoding.UTF8.GetBytes("+追加的"));
                        bool 追加好 = 虚拟文件C.读文本(工程根 + "/盘外.txt") == "替身里的数据+追加的";
                        记录(名表[序号++], 文件能力好 && 追加好,
                            $"这一层看得见 {根下名单.Count} 项[{string.Join("、", 根下名单)}]" +
                            $" / 是目录(库)={虚拟文件C.是目录(工程根 + "/库")}" +
                            $" / 追加后[{虚拟文件C.读文本(工程根 + "/盘外.txt")}]");

                        // 分块读(`读文件块` 那条路 = `读字节块`):不整读、逐块拼起来与一次读逐字节一致
                        string 大文件 = 工程根 + "/大件.bin";
                        var 原样 = new byte[100_000];
                        for (int i = 0; i < 原样.Length; i++) 原样[i] = (byte)(i % 251);
                        替身.写(大文件, 原样);
                        var 拼 = new MemoryStream();
                        long 走 = 0;
                        int 块数 = 0;
                        while (true)
                        {
                            byte[] 一块 = 虚拟文件C.读字节块(大文件, 走, 4096);
                            if (一块.Length == 0) break;
                            拼.Write(一块, 0, 一块.Length);
                            走 += 一块.Length;
                            块数++;
                        }
                        byte[] 拼好 = 拼.ToArray();
                        bool 分块好 = 拼好.Length == 原样.Length && 拼好.AsSpan().SequenceEqual(原样)
                            && 虚拟文件C.读字节块(大文件, 原样.Length, 4096).Length == 0
                            && 虚拟文件C.读字节块(大文件, 10, 5).Length == 5;
                        记录(名表[序号++], 分块好,
                            $"{块数} 块拼起来 {拼好.Length} 字节 / 原样 {原样.Length} 字节;" +
                            $"到末尾={虚拟文件C.读字节块(大文件, 原样.Length, 4096).Length} 字节;少一个实现方法这一条就编不过");
                    }
                    finally { 虚拟文件C.摘掉(); }
                }
                finally { try { 临时区C.清(临时); } catch { } }

                // ============ 六、接线:手机端那几处(按源码钉,"只有真机能验"那一半) ============
                string? 示例目录 = 找示例目录();
                string? 根 = 示例目录 == null ? null : Directory.GetParent(示例目录)?.FullName;
                if (根 == null)
                {
                    for (; 序号 < 名表.Length; 序号++) 跳过接线(名表[序号], "工作副本之外跑自检时跳过");
                    return;
                }

                string 选目录源 = File.ReadAllText(Path.Combine(根, "MobileIDE", "Platforms", "Android", "Android系统目录.cs"));
                string 实现源 = File.ReadAllText(Path.Combine(根, "MobileIDE", "Platforms", "Android", "内容文件实现C.cs"));
                string 分片源 = File.ReadAllText(Path.Combine(根, "MobileIDE", "Platforms", "Android", "手机选择器.系统目录.cs"));
                string 选择器源 = File.ReadAllText(Path.Combine(根, "MobileIDE", "手机选择器.cs"));
                string 活动源 = File.ReadAllText(Path.Combine(根, "MobileIDE", "Platforms", "Android", "MainActivity.cs"));
                string 手机 = File.ReadAllText(Path.Combine(根, "MobileIDE", "MainPage.xaml.cs"));
                string 帮助 = 帮助文本C.快速上手();

                记接线(名表[序号++],
                    选目录源.Contains("Intent.ActionOpenDocumentTree", StringComparison.Ordinal)
                    && 选目录源.Contains("StartActivityForResult", StringComparison.Ordinal)
                    && 选择器源.Contains("if (口 != null && 口.可用)", StringComparison.Ordinal)
                    && 分片源.Contains("前端契约C.服务.宿主.挂上文件实现", StringComparison.Ordinal),
                    "Android系统目录:SAF 的 `ACTION_OPEN_DOCUMENT_TREE` + 结果;选择器先试系统目录、被取消才落沙箱");

                记接线(名表[序号++],
                    分片源.Contains("内容文件实现C.建(", StringComparison.Ordinal)
                    && 分片源.Contains("摘系统目录()", StringComparison.Ordinal)
                    && 分片源.Contains("路径分派表C", StringComparison.Ordinal)
                    && 实现源.Contains("public sealed class 内容文件实现C : 文件接口C", StringComparison.Ordinal)
                    && 实现源.Contains("DocumentFile.FromTreeUri", StringComparison.Ordinal),
                    "挑回来的那一棵:`内容文件实现C`(引擎 `文件接口C` 的实现)+ `路径分派表C` 挂上 / 摘掉");

                记接线(名表[序号++],
                    活动源.Contains("protected override void OnActivityResult", StringComparison.Ordinal)
                    && 活动源.Contains("Android系统目录.收结果(requestCode, resultCode, data)", StringComparison.Ordinal)
                    && 选目录源.Contains("TakePersistableUriPermission", StringComparison.Ordinal)
                    && 选目录源.Contains("GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission", StringComparison.Ordinal),
                    "MainActivity 把 Activity 结果转给选择器;授权拿的是**跨重启的读写**(只给读的话「保存」会失败)");

                记接线(名表[序号++],
                    手机.Contains("前端契约C.服务.宿主.读文本文件(启动)", StringComparison.Ordinal)
                    && 手机.Contains("前端契约C.服务.宿主.写文本文件(目标, 源码编辑框.Text ?? \"\")", StringComparison.Ordinal)
                    && 手机.Contains("前端契约C.服务.宿主.文件存在(启动)", StringComparison.Ordinal)
                    && 手机.Contains("虚拟路径C.所在目录(目标)", StringComparison.Ordinal)
                    && !手机.Contains("File.ReadAllTextAsync", StringComparison.Ordinal)
                    && !手机.Contains("File.WriteAllTextAsync", StringComparison.Ordinal),
                    "MainPage:打开 / 保存 / 编译读文件全走 `虚拟文件C`,目录与光名走 `虚拟路径C`(不再直接 File)");

                记接线(名表[序号++],
                    帮助.Contains("「打开工程目录…」先给你挑系统目录", StringComparison.Ordinal)
                    && 帮助.Contains("长期读写授权", StringComparison.Ordinal)
                    && !帮助.Contains("不在这一档里", StringComparison.Ordinal),
                    "帮助《第24节·手机端》:系统目录那一档写清(授权 / 落回沙箱),老那句『不在这一档里』退场");
            }
            catch (Exception ex) { 记录异常("虚拟文件(H64)", ex); }
        }

        /// <summary>
        /// **内存文件系统**(H64 自检专用):`文件接口C` 的一份实现 —— 整棵树住在字典里,
        /// 不碰盘、不看平台。有了它,"虚拟路径 → 引擎各处"这一整条在命令行里就能**真跑**
        /// (SAF 那一份在 Android 上做的是同一件事,只是换成 `DocumentFile`)。
        ///
        /// 与真实现**同口径**的几条:列名字按 `Ordinal` 排序(结果稳定)、写文件缺的目录先建、
        /// 移动 / 复制的目标已存在 ⇒ 抛 `IOException`、删目录只删空的。
        /// </summary>
        private sealed class 内存文件替身C : 文件接口C
        {
            private readonly Dictionary<string, byte[]> _文件 = new(StringComparer.Ordinal);
            private readonly HashSet<string> _目录 = new(StringComparer.Ordinal);

            public 内存文件替身C()
            {
                foreach (string 路径 in new[] { "内容://树primary", "内容://树primary/工程" }) _目录.Add(路径);
            }

            /// <summary> 测试夹具用:直接塞一份进去(父目录按需补上)</summary>
            public void 写(string 路径, byte[] 内容)
            {
                for (string 父 = 虚拟路径C.所在目录(路径); 父.Length > 0; 父 = 虚拟路径C.所在目录(父)) _目录.Add(父);
                _文件[路径] = 内容;
            }

            public byte[] 字节(string 路径) => _文件.TryGetValue(路径, out var b) ? b : [];

            /// <summary> 诊断用:替身里现在有哪些键(自检红了要一眼看明白)</summary>
            public string 键清单() => string.Join(" | ", _文件.Keys.Concat(_目录).OrderBy(x => x, StringComparer.Ordinal).Select(虚拟路径C.显示));

            public bool 存在(string 路径) => _文件.ContainsKey(路径) || _目录.Contains(路径);

            public bool 是目录(string 路径) => _目录.Contains(路径);

            public byte[] 读字节(string 路径) =>
                _文件.TryGetValue(路径, out var b) ? b : throw new FileNotFoundException($"替身里没有:{路径}", 路径);

            public void 写字节(string 路径, byte[] 内容) => 写(路径, 内容);

            /// <summary> 只读这一块(与真实现同口径:偏移到末尾 / 超过末尾 ⇒ 空数组)</summary>
            public byte[] 读字节块(string 路径, long 偏移, int 上限)
            {
                byte[] 全部 = _文件.TryGetValue(路径, out var b) ? b : throw new FileNotFoundException($"替身里没有:{路径}", 路径);
                if (偏移 < 0 || 上限 <= 0 || 偏移 >= 全部.Length) return [];
                int 要读 = (int)Math.Min((long)上限, 全部.Length - 偏移);
                var 块 = new byte[要读];
                Array.Copy(全部, (int)偏移, 块, 0, 要读);
                return 块;
            }

            public void 追加字节(string 路径, byte[] 内容)
            {
                byte[] 旧 = 存在(路径) ? 读字节(路径) : [];
                var 合 = new byte[旧.Length + 内容.Length];
                Array.Copy(旧, 0, 合, 0, 旧.Length);
                Array.Copy(内容, 0, 合, 旧.Length, 内容.Length);
                写(路径, 合);
            }

            public IReadOnlyList<string> 列名字(string 目录)
            {
                string 前缀 = 目录.TrimEnd('/') + "/";
                var 名字 = new List<string>();
                foreach (string 一条 in _文件.Keys.Concat(_目录))
                {
                    if (!一条.StartsWith(前缀, StringComparison.Ordinal)) continue;
                    string 尾 = 一条[前缀.Length..];
                    if (尾.Length == 0 || 尾.Contains('/')) continue;
                    if (!名字.Contains(尾)) 名字.Add(尾);
                }
                名字.Sort(StringComparer.Ordinal);
                return 名字;
            }

            /// <summary> 建目录:虚拟目录树里记一条(父级按需补上)—— 与真实现"已存在也不报错"同口径</summary>
            public void 建目录(string 目录)
            {
                string 净 = 目录.TrimEnd('/');
                for (string 父 = 净; 父.Length > 0; 父 = 虚拟路径C.所在目录(父)) _目录.Add(父);
            }

            public void 删文件(string 路径)
            {
                if (!_文件.Remove(路径)) throw new FileNotFoundException($"替身里没有:{路径}", 路径);
            }

            public void 删目录(string 路径)
            {
                if (!_目录.Contains(路径)) throw new DirectoryNotFoundException($"替身里没有这个目录:{路径}");
                if (列名字(路径).Count > 0) throw new IOException($"目录不是空的:{路径}");
                _目录.Remove(路径);
            }

            public void 移动(string 旧路径, string 新路径)
            {
                if (存在(新路径)) throw new IOException($"目标已存在:{新路径}");
                if (!_文件.TryGetValue(旧路径, out var b)) throw new FileNotFoundException($"替身里没有:{旧路径}", 旧路径);
                _文件.Remove(旧路径);
                写(新路径, b);
            }

            public void 复制(string 源路径, string 目标路径)
            {
                if (存在(目标路径)) throw new IOException($"目标已存在:{目标路径}");
                if (!_文件.TryGetValue(源路径, out var b)) throw new FileNotFoundException($"替身里没有:{源路径}", 源路径);
                写(目标路径, b);
            }

            /// <summary> 改名 / 覆盖式替换(G3 `46 改名`):替身里"整体替换"就是直接盖掉那一格 </summary>
            public void 改名(string 旧路径, string 新路径)
            {
                if (!_文件.TryGetValue(旧路径, out var b)) throw new FileNotFoundException($"替身里没有:{旧路径}", 旧路径);
                _文件.Remove(旧路径);
                写(新路径, b);
            }
        }
    }
}
