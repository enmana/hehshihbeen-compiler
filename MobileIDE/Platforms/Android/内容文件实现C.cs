using Android.Provider;
using hehshihbeen_compiler;
using URI = Android.Net.Uri;
// Android 那一套里 `Java.IO` 也有同名异常 ⇒ 引擎这层要的始终是 .NET 这一支(别删这两行别名)
using FileNotFoundException = System.IO.FileNotFoundException;
using IOException = System.IO.IOException;
using DirectoryNotFoundException = System.IO.DirectoryNotFoundException;

namespace 手机IDE.Platforms.Android;

/// <summary>
/// H64:**一棵 SAF 授权树**当"一门文件系统"使 —— 引擎 `文件接口C` 的那份 Android 实现。
///
/// 引擎那一侧看到的是**虚拟路径**(引擎 `虚拟路径C` 定的那套:记号 + 树标识 + 树里的相对路径)。
/// 本类负责把"树标识 + 树里的相对路径"翻译成 `DocumentFile` 一层层走。
///
/// 三处口径(与真路径那份实现**对齐**,不然同一段 HB 程序在两端的表现会分叉):
///   · **列名字排序**照旧 `Ordinal` —— 结果稳定是硬要求(程序里 `列目录` 的顺序、自检的"双跑一致");
///   · **写**文件不在就新建、缺的目录先建出来(真路径那份就是这么做的);
///   · **移动 / 复制**的目标已存在 ⇒ 抛 `IOException`(上层翻成状态 14"已经存在",不覆盖)。
///
/// SAF 给不了的两件事如实认下来,改走等效做法:
///   ① `DocumentFile` 没有"追加" —— `追加字节` 读旧的 + 拼新的 + 覆盖写;
///   ② 它没有"随机 seek" —— 分块读在引擎那一侧已经改成"整读再切一块"(`宿主能力那一侧的分块读`)。
/// </summary>
public sealed class 内容文件实现C : 文件接口C
{
    /// <summary> 我们写进虚拟路径的那个"树标识"前缀(只用它拼路径,不解释别的意思) </summary>
    public const string 树前缀 = "树";

    private readonly string 标识;
    private readonly URI 树地址;
    private readonly AndroidX.DocumentFile.Provider.DocumentFile 根;

    private 内容文件实现C(string 标识, URI 树地址, AndroidX.DocumentFile.Provider.DocumentFile 根)
    {
        this.标识 = 标识;
        this.树地址 = 树地址;
        this.根 = 根;
    }

    /// <summary> 虚拟路径的根(整棵树):记号 + 树标识 </summary>
    public string 根路径 => 虚拟路径C.记号 + 标识;

    /// <summary> 这一棵树给用户看的名字(系统给的显示名) </summary>
    public string 显示名 { get; private set; } = "系统目录";

    /// <summary> 从系统给的 `content://` 树地址建一个实现(null = 建不出来:没有权限 / 系统那一层出错) </summary>
    public static 内容文件实现C? 建(URI 树地址, string 显示名)
    {
        try
        {
            var 根 = AndroidX.DocumentFile.Provider.DocumentFile.FromTreeUri(Platform.AppContext, 树地址);
            if (根 == null) return null;
            return new 内容文件实现C(标识算(树地址), 树地址, 根) { 显示名 = string.IsNullOrWhiteSpace(显示名) ? "系统目录" : 显示名 };
        }
        catch (Exception 错)
        {
            System.Diagnostics.Debug.WriteLine($"挂系统目录失败:{错.Message}");
            return null;
        }
    }

    /// <summary> 这一棵树该用哪个虚拟路径根(`内容://…`)。**纯字符串**:挑目录那一层要用它,
    /// 挂上之后还要用同一个(不然路径对不上),所以收在一处 </summary>
    public static string 根路径算(URI 树地址) => 虚拟路径C.记号 + 标识算(树地址);

    /// <summary> 树标识只从树地址尾部取一段 —— 它只需要"同一棵树每次都算出同一个",不必让人看懂 </summary>
    private static string 标识算(URI 树地址)
    {
        string 尾 = 树地址.LastPathSegment ?? "";
        int 冒 = 尾.IndexOf(':');
        string 标识 = 树前缀 + (冒 >= 0 ? 尾[(冒 + 1)..].Replace('/', '_') : 尾.Replace('/', '_'));
        return 标识.Length <= 树前缀.Length ? 树前缀 + Math.Abs(尾.GetHashCode()).ToString("x8") : 标识;
    }

    /// <summary> 这条虚拟路径归不归这一棵树管(前缀比;两边都收成同一种斜杠) </summary>
    public bool 认领(string 路径)
    {
        string 净 = 虚拟路径C.规整(路径);
        return string.Equals(净, 根路径, StringComparison.Ordinal)
            || 净.StartsWith(根路径 + "/", StringComparison.Ordinal);
    }

    // ================= 路径 → 文档 =================

    /// <summary> 树里的相对路径(`${内容://树}` 那一段摘掉) </summary>
    private string[] 段(string 路径)
    {
        string 净 = 虚拟路径C.规整(路径);
        if (净.Length <= 根路径.Length) return [];
        string 尾 = 净[(根路径.Length + 1)..];
        return 尾.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// 走到那一层文档上:一段一段 `FindFile`;`建 = true` 时缺的目录**当场建出来**(写文件那条路要)。
    /// 找不到 / 中间那段不是目录 ⇒ null(调用方按自己的口径翻成"打不开"或抛出)。
    /// </summary>
    private AndroidX.DocumentFile.Provider.DocumentFile? 走到(string 路径, bool 建)
    {
        AndroidX.DocumentFile.Provider.DocumentFile 当 = 根;
        foreach (string 一 in 段(路径))
        {
            var 子 = 当.FindFile(一);
            if (子 == null)
            {
                if (!建) return null;
                子 = 当.CreateDirectory(一);
                if (子 == null) return null;
            }
            else if (!子.IsDirectory) return null;
            当 = 子;
        }
        return 当;
    }

    /// <summary> 文件那一层(最后一段按文件找;`建 = true` 时不在就建出来) </summary>
    private AndroidX.DocumentFile.Provider.DocumentFile? 找文件(string 路径, bool 建)
    {
        string[] 段表 = 段(路径);
        if (段表.Length == 0) return null;                     // 根是目录,不是文件
        string 名 = 段表[^1];
        string 父路径 = 段表.Length == 1 ? 根路径 : 根路径 + "/" + string.Join("/", 段表[..^1]);
        var 父 = 走到(父路径, 建);
        if (父 == null) return null;
        var 文件 = 父.FindFile(名);
        if (文件 != null) return 文件;
        if (!建) return null;
        return 父.CreateFile("application/octet-stream", 名);   // 后缀归名字(SAF 允许)
    }

    // ================= 文件接口C =================

    public bool 存在(string 路径)
    {
        try
        {
            if (段(路径).Length == 0) return 根.IsDirectory;
            return 走到(路径, false) != null || 找文件(路径, false) != null;
        }
        catch { return false; }
    }

    public bool 是目录(string 路径)
    {
        try
        {
            if (段(路径).Length == 0) return 根.IsDirectory;
            return 走到(路径, false) != null;
        }
        catch { return false; }
    }

    public byte[] 读字节(string 路径)
    {
        var 文件 = 找文件(路径, false);
        if (文件 == null) throw new FileNotFoundException($"系统目录里找不到:{虚拟路径C.显示(路径)}", 路径);
        var 流 = Platform.AppContext.ContentResolver?.OpenInputStream(文件.Uri);
        if (流 == null) throw new IOException($"打不开这个文件:{虚拟路径C.显示(路径)}");
        try
        {
            using var 缓冲 = new MemoryStream();
            var 块 = new byte[8192];
            int 读到;
            while ((读到 = 流.Read(块, 0, 块.Length)) > 0) 缓冲.Write(块, 0, 读到);
            return 缓冲.ToArray();
        }
        finally { 关掉(流); }
    }

    /// <summary>
    /// 只读这一块(D3「分块读文件」那条路):SAF 的流**不便随机 seek**(拿到的可能是管道那一类),
    /// 所以照**顺序跳过偏移**再读,而且**只把这 `上限` 个字节读进来** —— 大文件照样是一块一块读,
    /// 不会因为"分块读"反过来把整文件吃进内存。
    /// </summary>
    public byte[] 读字节块(string 路径, long 偏移, int 上限)
    {
        if (偏移 < 0 || 上限 <= 0) return [];
        var 文件 = 找文件(路径, false);
        if (文件 == null) throw new FileNotFoundException($"系统目录里找不到:{虚拟路径C.显示(路径)}", 路径);
        var 流 = Platform.AppContext.ContentResolver?.OpenInputStream(文件.Uri);
        if (流 == null) throw new IOException($"打不开这个文件:{虚拟路径C.显示(路径)}");
        try
        {
            var 丢 = new byte[8192];
            long 跳过 = 0;
            while (跳过 < 偏移)
            {
                int n = 流.Read(丢, 0, (int)Math.Min(丢.Length, 偏移 - 跳过));
                if (n <= 0) return [];                     // 偏移已经过了文件末尾
                跳过 += n;
            }
            using var 缓冲 = new MemoryStream();
            var 块 = new byte[Math.Min(上限, 8192)];
            int 已读 = 0;
            while (已读 < 上限)
            {
                int n = 流.Read(块, 0, Math.Min(块.Length, 上限 - 已读));
                if (n <= 0) break;
                缓冲.Write(块, 0, n);
                已读 += n;
            }
            return 缓冲.ToArray();
        }
        finally { 关掉(流); }
    }

    public void 写字节(string 路径, byte[] 内容)    {
        var 文件 = 找文件(路径, true);
        if (文件 == null) throw new IOException($"写不进去(建不出这个文件):{虚拟路径C.显示(路径)}");
        var 流 = Platform.AppContext.ContentResolver?.OpenOutputStream(文件.Uri, "wt");
        if (流 == null) throw new IOException($"打不开这个文件:{虚拟路径C.显示(路径)}");
        try
        {
            流.Write(内容);
            流.Flush();
        }
        finally { 关掉(流); }
    }

    /// <summary> `DocumentFile` 没有"追加"这一手:读旧的 + 拼新的 + 覆盖写(内容口径与真路径那份一致)</summary>
    public void 追加字节(string 路径, byte[] 内容)
    {
        byte[] 旧 = 存在(路径) ? 读字节(路径) : [];
        var 合 = new byte[旧.Length + 内容.Length];
        Array.Copy(旧, 0, 合, 0, 旧.Length);
        Array.Copy(内容, 0, 合, 旧.Length, 内容.Length);
        写字节(路径, 合);
    }

    public IReadOnlyList<string> 列名字(string 目录)
    {
        var 当 = 段(目录).Length == 0 ? 根 : 走到(目录, false);
        if (当 == null) throw new DirectoryNotFoundException($"系统目录里找不到:{虚拟路径C.显示(目录)}");
        var 名字 = new List<string>();
        foreach (var 子 in 当.ListFiles())
        {
            string? 名 = 子.Name;
            if (!string.IsNullOrEmpty(名)) 名字.Add(名!);
        }
        名字.Sort(StringComparer.Ordinal);                    // 与真路径那份同一个排序口径
        return 名字;
    }

    public void 建目录(string 目录)
    {
        if (段(目录).Length == 0) return;                     // 根已经在了(幂等)
        var 建好 = 走到(目录, true);
        if (建好 == null) throw new IOException($"建不出这个目录:{虚拟路径C.显示(目录)}");
    }

    public void 删文件(string 路径)
    {
        var 文件 = 找文件(路径, false);
        if (文件 == null) throw new FileNotFoundException($"系统目录里找不到:{虚拟路径C.显示(路径)}", 路径);
        if (!文件.Delete()) throw new IOException($"删不掉这个文件:{虚拟路径C.显示(路径)}");
    }

    /// <summary> 删**空**目录(非空 ⇒ 抛 `IOException`,与真路径那份 `recursive: false` 同口径)</summary>
    public void 删目录(string 路径)
    {
        var 当 = 段(路径).Length == 0 ? 根 : 走到(路径, false);
        if (当 == null) throw new DirectoryNotFoundException($"系统目录里找不到:{虚拟路径C.显示(路径)}");
        foreach (var _ in 当.ListFiles()) throw new IOException($"目录不是空的(不递归删):{虚拟路径C.显示(路径)}");
        if (!当.Delete()) throw new IOException($"删不掉这个目录:{虚拟路径C.显示(路径)}");
    }

    public void 移动(string 旧路径, string 新路径)
    {
        if (存在(新路径)) throw new IOException($"目标已存在:{虚拟路径C.显示(新路径)}");
        var 源 = 找文件(旧路径, false);
        if (源 == null) throw new FileNotFoundException($"系统目录里找不到:{虚拟路径C.显示(旧路径)}", 旧路径);
        复制到(源, 新路径);
        if (!源.Delete()) throw new IOException($"搬过去之后删不掉原件:{虚拟路径C.显示(旧路径)}");
    }

    public void 复制(string 源路径, string 目标路径)
    {
        if (存在(目标路径)) throw new IOException($"目标已存在:{虚拟路径C.显示(目标路径)}");
        var 源 = 找文件(源路径, false);
        if (源 == null) throw new FileNotFoundException($"系统目录里找不到:{虚拟路径C.显示(源路径)}", 源路径);
        复制到(源, 目标路径);
    }

    /// <summary> 改名 / 覆盖式替换(G3 `46 改名`)。**SAF 没有原子替换这一手**:目标是文件就先删掉再搬
    /// (这一档因此**不是原子的** —— 真路径那份走 `MoveFileEx(REPLACE_EXISTING)`,同一卷上才是原子替换);
    /// 目录只接受"目标不存在"(与真路径那份同一个口径)。 </summary>
    public void 改名(string 旧路径, string 新路径)
    {
        if (存在(新路径))
        {
            if (是目录(旧路径) || 是目录(新路径)) throw new IOException($"目标已存在:{虚拟路径C.显示(新路径)}");
            if (找文件(旧路径, false) == null)
                throw new FileNotFoundException($"系统目录里找不到:{虚拟路径C.显示(旧路径)}", 旧路径);
            删文件(新路径);
        }
        移动(旧路径, 新路径);
    }

    /// <summary> 把一份文档的内容照抄到目标路径上(目标必须还不存在) —— 移动 / 复制共用 </summary>
    private void 复制到(AndroidX.DocumentFile.Provider.DocumentFile 源, string 目标路径)
    {
        var 目标 = 找文件(目标路径, true);
        if (目标 == null) throw new IOException($"写不进去(建不出目标):{虚拟路径C.显示(目标路径)}");
        var 入 = Platform.AppContext.ContentResolver?.OpenInputStream(源.Uri);
        if (入 == null) throw new IOException($"打不开源文件:{虚拟路径C.显示(目标路径)}");
        var 出 = Platform.AppContext.ContentResolver?.OpenOutputStream(目标.Uri, "wt");
        if (出 == null) { 关掉(入); throw new IOException($"打不开目标文件:{虚拟路径C.显示(目标路径)}"); }
        try
        {
            var 块 = new byte[8192];
            int 读到;
            while ((读到 = 入.Read(块, 0, 块.Length)) > 0) 出.Write(块, 0, 读到);
            出.Flush();
        }
        finally { 关掉(入); 关掉(出); }
    }

    /// <summary> 流用完一定关掉(SAF 的流拿着文件描述符,不关会影响后续同文件的操作)</summary>
    private static void 关掉(IDisposable 流)
    {
        try { 流.Dispose(); } catch { }
    }
}
