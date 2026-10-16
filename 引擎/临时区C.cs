using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler;

/// <summary>
/// 临时文件与缓存的**唯一入口**(H44;硬规矩 15:2026-09-25 用户当班定 ——
/// "不要把临时文件或者缓存弄到 C 盘,我的 C 盘都满了")。
///
/// **落点阶梯**(先认环境变量、再认仓库,最后才认盘 —— **绝不回退系统那个临时目录**(`GetTempPath` 那一路),
/// 换台机器 / 环境变量没被继承时也不会跑回 C 盘):
/// <list type="number">
/// <item>`HB_TEMP` 环境变量(非空就用它;换机器 / CI 上指哪儿去哪儿);</item>
/// <item>**仓库内** `工具\临时\`(已 gitignore —— 开发机上自检 / CLI / IDE 的默认落点);</item>
/// <item>硬规矩 15 那条机器级落点 `E:\cache\temp`(仓库根找不到、但 E 盘在);</item>
/// <item>最后才是**程序自己目录**下的 `临时区\`(至少不是 C 盘那个系统临时目录)。</item>
/// </list>
///
/// **两条配套纪律**:
/// <list type="number">
/// <item>**跑完自己清** —— 一个目录用完就 `清(路径)`;整个进程一次性的(自检那几份几 GB 的对拍产物)
/// 在自检收尾的 `finally` 里调一次 `清理本次遗留()`;</item>
/// <item>**开工扫旧** —— 第一次 `基目录()` 时把**陈旧**的进程目录扫掉(超过 <see cref="陈旧小时"/> 小时没动过的才算旧)
/// —— 上一次被 Ctrl-C / 超时打断留下的那几 GB 靠这一句收干净;两个自检同时在跑时谁也删不了谁
/// (这条真踩过:一次收尾把同时在跑的那一次刚交出来的产物删成 0 字节)。</item>
/// </list>
///
/// **为什么是这个阶梯**:环境变量是**进程启动时读一次**的(用户级 `TEMP` / `TMP` 已按硬规矩 15 指到 E 盘,
/// 但老会话 / 已经在跑的进程还认旧的)⇒ 光靠环境变量不算数,得在代码里把落点**自己定下来**;
/// 而"仓库内 `工具\临时\`"这一档的好处是**看得见、好清、跟仓库一起被 gitignore 挡住**。
/// </summary>
public static class 临时区C
{
    /// <summary>临时区落点的环境变量名(设了就听它的)。</summary>
    public const string 环境变量名 = "HB_TEMP";

    /// <summary>仓库内那一档(相对仓库根,与 `.gitignore` 里那条同字)。</summary>
    public const string 仓库内目录 = @"工具\临时";

    /// <summary>兜底那一档:硬规矩 15 点名的机器级落点,只有仓库根找不到时才用。</summary>
    public const string 机器级落点 = @"E:\cache\temp";

    /// <summary>同名进程目录多久没动过才算"陈旧"(小时)。并发跑的自检各有各的进程号,不会被误删。</summary>
    public const int 陈旧小时 = 6;

    private static string? 缓存根;

    /// <summary>
    /// 临时区的**根**(不含子名)。第一次调用时定下来、顺手建出来;以后同一次进程里都返回它。
    /// </summary>
    public static string 基目录()
    {
        if (缓存根 != null) return 缓存根;
        string 路径 = 定基目录();
        尽力建目录(路径);
        缓存根 = 路径;
        清陈旧(路径);                      // 开工扫旧:上一次被打断留下的那几 GB 在这儿收掉
        return 缓存根;
    }

    /// <summary>临时区里的一个子目录(按段拼;建好再返回)。`取目录("hbcc_C后端")` ⇒ `<基>\hbcc_C后端`。</summary>
    public static string 取目录(params string[] 段)
    {
        string 路径 = 基目录();
        foreach (string 一层 in 段)
            if (!string.IsNullOrEmpty(一层)) 路径 = Path.Combine(路径, 一层);
        尽力建目录(路径);
        return 路径;
    }

    /// <summary>
    /// **收工自清**:把临时区里"以本次进程号命名"的目录一份份删掉(尽力而为,删不掉不抛)。
    /// 自检收尾那条 `finally` 里调它 —— C 后端全量对拍那几份几 GB 的产物就是靠这一句收干净的。
    /// </summary>
    public static void 清理本次遗留()
    {
        string 根 = 基目录();
        if (!Directory.Exists(根)) return;
        string 我 = Environment.ProcessId.ToString();
        List<string> 档们;
        try { 档们 = Directory.EnumerateDirectories(根).ToList(); }
        catch { return; }
        foreach (string 档 in 档们)
        {
            // 一档一档各自 try:某一档删不动(文件被别的进程占着之类)**不许**连累后面的档
            // (第一版整段包在一个 try 里,实测真踩到:一档抛了就再没删过东西)。
            try
            {
                string 本 = Path.Combine(档, 我);
                if (!Directory.Exists(本)) continue;
                清(本);
                // 档里空了就把档也收了 —— 省得临时区里堆一层层空壳
                if (Directory.Exists(档) && !Directory.EnumerateFileSystemEntries(档).Any()) Directory.Delete(档);
            }
            catch { }
        }
    }

    /// <summary>尽力而为地删一个目录树(删不掉就留着,不影响结论)。基目录本身**不动**。</summary>
    public static void 清(string 路径)
    {
        if (string.IsNullOrWhiteSpace(路径)) return;
        try
        {
            string 全 = Path.GetFullPath(路径);
            if (string.Equals(全.TrimEnd(Path.DirectorySeparatorChar),
                    基目录().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                return;                        // 护栏:基目录自己不许删(路径拼错了也别把整个临时区清了)
            if (Directory.Exists(全)) Directory.Delete(全, recursive: true);
            else if (File.Exists(全)) File.Delete(全);
        }
        catch { }
    }

    // ---------- 下面都是内部实现 ----------

    private static string 定基目录()
    {
        string? 设 = Environment.GetEnvironmentVariable(环境变量名);
        string? 根 = 找仓库根();
        bool 有E盘 = false;
        try { 有E盘 = Directory.Exists(@"E:\"); } catch { }
        return 选落点(设, 根, 有E盘, AppContext.BaseDirectory);
    }

    /// <summary>
    /// 落点阶梯的**纯函数形状**(给自检断言按四种输入各量一遍;真跑时那几个输入由 `定基目录` 现取)。
    /// 阶梯见类头:`HB_TEMP` → 仓库内 `工具\临时` → `E:\cache\temp` → 程序目录下的 `临时区`。
    /// </summary>
    public static string 选落点(string? 环境变量, string? 仓库根, bool 有E盘, string 程序目录)
    {
        // ① 环境变量(设了就听它的)
        if (!string.IsNullOrWhiteSpace(环境变量))
        {
            try { return Path.GetFullPath(环境变量.Trim()); } catch { }
        }

        // ② 仓库内 `工具\临时\`
        if (!string.IsNullOrWhiteSpace(仓库根)) return Path.Combine(仓库根!, "工具", "临时");

        // ③ 硬规矩 15 那条机器级落点(E 盘在才用)
        if (有E盘) return 机器级落点;

        // ④ 最后才是程序自己目录下(至少不是 C 盘那个系统临时目录)
        return Path.Combine(程序目录, "临时区");
    }

    /// <summary>
    /// 从程序目录往上认**仓库根**(同时有 `引擎` / `自检` / `Sample` 三个目录的那一层)。
    /// 为什么要三个都认:光认 `Sample` 会撞上"程序运行时在工作目录下冒出来的空壳 Sample"(真踩过 ——
    /// 宿主能力示例会在工作目录里把路径建出来,于是自检往上找先撞上那个空壳)。
    /// </summary>
    private static string? 找仓库根()
    {
        try
        {
            var 目录 = new DirectoryInfo(AppContext.BaseDirectory);
            while (目录 != null)
            {
                if (Directory.Exists(Path.Combine(目录.FullName, "引擎"))
                    && Directory.Exists(Path.Combine(目录.FullName, "自检"))
                    && Directory.Exists(Path.Combine(目录.FullName, "Sample")))
                    return 目录.FullName;
                目录 = 目录.Parent;
            }
        }
        catch { }
        return null;
    }

    private static void 尽力建目录(string 路径)
    {
        try { Directory.CreateDirectory(路径); } catch { }
    }

    /// <summary>
    /// 扫掉**陈旧的进程目录**:目录名是纯数字(进程号)、不是本次进程号、而且 <see cref="陈旧小时"/> 小时没动过。
    /// 看两层 —— 临时区下一层是"档名"(`hbcc_C后端全量`),再下一层才是进程号;但也有把进程号直接挂在根下的写法。
    /// </summary>
    private static void 清陈旧(string 根)
    {
        try
        {
            if (!Directory.Exists(根)) return;
            foreach (string 子 in Directory.EnumerateDirectories(根))
            {
                if (是陈旧进程目录(子)) { 清(子); continue; }
                try
                {
                    foreach (string 孙 in Directory.EnumerateDirectories(子))
                        if (是陈旧进程目录(孙)) 清(孙);
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool 是陈旧进程目录(string 路径)
    {
        try
        {
            string 名 = Path.GetFileName(路径);
            if (名.Length == 0 || 名 == Environment.ProcessId.ToString()) return false;
            foreach (char c in 名) if (!char.IsAsciiDigit(c)) return false;
            return Directory.GetLastWriteTimeUtc(路径) < DateTime.UtcNow.AddHours(-陈旧小时);
        }
        catch { return false; }
    }
}
