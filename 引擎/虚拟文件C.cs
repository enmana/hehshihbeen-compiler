using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// H64:**文件接口** —— 引擎那一侧"读写盘上那些字节"的**唯一一道口子**。
    ///
    /// 平时挂的是 <see cref="真实文件实现C"/>(就是 `File` / `Directory` 那几个静态方法,
    /// 桌面端 / 命令行一个字的行为都不变);手机端从 **系统目录(SAF)** 打开工程时,
    /// 换成 `content://` 那一套实现 —— 于是"引擎要不要认识 SAF"这件事就收在这一层,
    /// 编译链与运行期那几处只管调 `虚拟文件C`,不各写一套。
    ///
    /// **只放"两边都做得到"的几件事**:读 / 写 / 追加字节、问在不在、列名字、建目录、删、移动、复制。
    /// 不往里塞真路径特有的东西(卷标、盘符、符号链接、权限位)—— SAF 那边给不出来。
    /// 目录操作只到"这一层"(SAF 的 `DocumentFile` 本来也是按文档树一层层走的),不提供递归。
    /// </summary>
    public interface 文件接口C
    {
        /// <summary> 这个文件在不在(目录也算"在") </summary>
        bool 存在(string 路径);

        /// <summary> 这个路径是不是目录 </summary>
        bool 是目录(string 路径);

        /// <summary> 读成字节;读不到抛出(调用方自己决定吞不吞) </summary>
        byte[] 读字节(string 路径);

        /// <summary>
        /// **只读这一块**(从 `偏移` 起最多 `上限` 字节)—— D3「分块读文件」要的就是它:
        /// 大文件按块读时**不许把整文件先吃进内存**(那等于把"分块"变成假的)。
        /// 真实现走 `FileStream.Seek`,SAF 那份走流上的跳过再读。**到末尾返回空数组**。
        /// </summary>
        byte[] 读字节块(string 路径, long 偏移, int 上限);

        /// <summary> 整份覆盖写(文件不在就新建) </summary>
        void 写字节(string 路径, byte[] 内容);

        /// <summary> 接在文件末尾(文件不在就新建) </summary>
        void 追加字节(string 路径, byte[] 内容);

        /// <summary> 列这一层里的**名字**(不带路径),按 `Ordinal` 排好序 —— 结果稳定是硬要求
        /// (自检的"双跑一致"、界面上"同一个目录两次列出来一样"都靠它) </summary>
        IReadOnlyList<string> 列名字(string 目录);

        /// <summary> 建目录(已存在也算成功:同一段程序跑第二遍不该失败) </summary>
        void 建目录(string 目录);

        /// <summary> 删文件(不在就抛出) </summary>
        void 删文件(string 路径);

        /// <summary> 删**空**目录(非空时抛 `IOException`;不在就抛出)。
        /// SAF 那边删目录与删文档是同一手,真路径这边是 `Directory.Delete(recursive: false)` </summary>
        void 删目录(string 路径);

        /// <summary> 移动(目标已存在时抛 `IOException`;目标目录会先建出来) </summary>
        void 移动(string 旧路径, string 新路径);

        /// <summary> 改名 / **覆盖式替换**(G3 运行时 `46 改名` 走它):目标是文件且已存在就整体替换
        /// (同一卷上是一个原子替换 —— `安全写全部` 靠这一条);目录只接受"目标不存在"。
        /// 与 <see cref="移动"/> 的"目标已存在就报错"是**两回事**,别混用 </summary>
        void 改名(string 旧路径, string 新路径);

        /// <summary> 复制(目标已存在时抛 `IOException`;目标目录会先建出来) </summary>
        void 复制(string 源路径, string 目标路径);
    }

    /// <summary>
    /// **真实文件**那一份实现(= 老行为,一个字没改口径):桌面端 / 命令行 / 独立宿主都用它。
    /// 桌面端不会有人 `设根`,所以 `虚拟文件C` 永远走这一份;手机端只在"系统目录"那一档换实现。
    /// </summary>
    public sealed class 真实文件实现C : 文件接口C
    {
        /// <summary> 共用一份就行(无状态) </summary>
        public static readonly 真实文件实现C 单例 = new();

        public bool 存在(string 路径) => File.Exists(路径) || Directory.Exists(路径);

        public bool 是目录(string 路径) => Directory.Exists(路径);

        public byte[] 读字节(string 路径) => File.ReadAllBytes(路径);

        /// <summary> 只读这一块(`FileStream` + `Seek`;偏移到末尾 / 超过末尾 ⇒ 空数组)</summary>
        public byte[] 读字节块(string 路径, long 偏移, int 上限)
        {
            if (偏移 < 0 || 上限 <= 0) return [];
            using var 流 = new FileStream(路径, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (偏移 >= 流.Length) return [];
            long 要读 = Math.Min((long)上限, 流.Length - 偏移);
            var 缓冲 = new byte[要读];
            流.Seek(偏移, SeekOrigin.Begin);
            int 读到 = 0;
            while (读到 < 缓冲.Length)
            {
                int n = 流.Read(缓冲, 读到, 缓冲.Length - 读到);
                if (n <= 0) break;
                读到 += n;
            }
            if (读到 != 缓冲.Length) Array.Resize(ref 缓冲, 读到);
            return 缓冲;
        }

        public void 写字节(string 路径, byte[] 内容)
        {
            string? 目录 = Path.GetDirectoryName(路径);
            if (!string.IsNullOrEmpty(目录) && !Directory.Exists(目录)) Directory.CreateDirectory(目录);
            File.WriteAllBytes(路径, 内容);
        }

        public void 追加字节(string 路径, byte[] 内容)
        {
            string? 目录 = Path.GetDirectoryName(路径);
            if (!string.IsNullOrEmpty(目录) && !Directory.Exists(目录)) Directory.CreateDirectory(目录);
            using var 流 = new FileStream(路径, FileMode.Append, FileAccess.Write);
            流.Write(内容, 0, 内容.Length);
        }

        public IReadOnlyList<string> 列名字(string 目录) =>
            [.. Directory.GetFileSystemEntries(目录)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(名 => 名.Length > 0)
                .OrderBy(名 => 名, StringComparer.Ordinal)];

        public void 建目录(string 目录) => Directory.CreateDirectory(目录);

        public void 删文件(string 路径)
        {
            if (!File.Exists(路径)) throw new FileNotFoundException($"找不到文件: {路径}", 路径);
            File.Delete(路径);
        }

        public void 删目录(string 路径)
        {
            if (!Directory.Exists(路径)) throw new DirectoryNotFoundException($"找不到目录: {路径}");
            Directory.Delete(路径, recursive: false);     // 非空会抛 IOException,这正是"只删空目录"的口径
        }

        public void 移动(string 旧路径, string 新路径)
        {
            string? 目录 = Path.GetDirectoryName(新路径);
            if (!string.IsNullOrEmpty(目录) && !Directory.Exists(目录)) Directory.CreateDirectory(目录);
            if (Directory.Exists(旧路径)) Directory.Move(旧路径, 新路径);
            else File.Move(旧路径, 新路径);
        }

        /// <summary> 改名 / 覆盖式替换(G3 `46 改名`):文件走 `File.Move(覆盖:true)`(Windows 上是
        /// `MoveFileEx(REPLACE_EXISTING)`,同一卷即原子替换);**目录只接受"目标不存在"** ——
        /// 目录的"整体替换"说不清是什么(要先递归删光才算数),那一档留给调用方自己决定 </summary>
        public void 改名(string 旧路径, string 新路径)
        {
            string? 目录 = Path.GetDirectoryName(新路径);
            if (!string.IsNullOrEmpty(目录) && !Directory.Exists(目录)) Directory.CreateDirectory(目录);
            if (Directory.Exists(旧路径))
            {
                if (Directory.Exists(新路径) || File.Exists(新路径)) throw new IOException($"目标已存在: {新路径}");
                Directory.Move(旧路径, 新路径);
                return;
            }
            File.Move(旧路径, 新路径, overwrite: true);
        }

        public void 复制(string 源路径, string 目标路径)
        {
            string? 目录 = Path.GetDirectoryName(目标路径);
            if (!string.IsNullOrEmpty(目录) && !Directory.Exists(目录)) Directory.CreateDirectory(目录);
            if (Directory.Exists(源路径))
            {
                // 目录**整棵**复制(C22 起的老口径:目标已存在就报 14,不合并不覆盖)
                Directory.CreateDirectory(目标路径);
                foreach (string 子文件 in Directory.EnumerateFiles(源路径, "*", SearchOption.AllDirectories))
                {
                    string 相对 = Path.GetRelativePath(源路径, 子文件);
                    string 目标文件 = Path.Combine(目标路径, 相对);
                    string? 子目录 = Path.GetDirectoryName(目标文件);
                    if (!string.IsNullOrEmpty(子目录) && !Directory.Exists(子目录)) Directory.CreateDirectory(子目录);
                    if (File.Exists(目标文件)) throw new IOException($"目标已存在: {目标文件}");
                    File.Copy(子文件, 目标文件, overwrite: false);
                }
                return;
            }
            File.Copy(源路径, 目标路径, overwrite: false);
        }
    }

    /// <summary>
    /// H64:**虚拟文件**的两件事**:① 谁来实现(挂哪一份 <see cref="文件接口C"/>);
    /// ② 引擎各处拿它读写的**统一入口**。
    ///
    /// 分工(别弄混):
    ///   · `真实文件实现C` = 桌面 / 命令行那份**缺省**实现 —— **不挂任何东西时就是它**,老路径一个字不改;
    ///   · 平台实现(手机端 SAF)= 只认自己那一档虚拟路径,别的一律抛(这样"写错了实现"会当场炸,
    ///     而不是静悄悄把文件建到手机沙箱里、用户还以为存进系统目录了);
    ///   · 引擎其余地方**不要直接 `File.ReadAllBytes`** 去读"可能来自系统目录"的路径 —— 走这里。
    ///
    /// 挂 / 摘的口径与 `宿主能力C.设可执行白名单` 那一族一样:进程级静态,界面在"打开系统目录"时挂、
    /// 回到沙箱工程时摘(见 `手机选择器.挂上 (摘掉)`)。**桌面端从头到尾没人挂**,所以那份行为不变。
    /// </summary>
    public static class 虚拟文件C
    {
        private static 文件接口C? _根实现;

        /// <summary> 现在挂着哪一份(没挂 = null;引擎各处不用直接问它,统一调下面的静态方法) </summary>
        public static 文件接口C? 挂着 => _根实现;

        /// <summary> 挂上一份实现;传 null = 摘掉(回到真实文件) </summary>
        public static void 挂上(文件接口C? 实现) => _根实现 = 实现;

        /// <summary> 摘掉(回到真实文件实现) </summary>
        public static void 摘掉() => _根实现 = null;

        /// <summary> 这条路径该谁管:是虚拟路径**且挂了实现** ⇒ 平台那份;否则真实文件那份。
        /// 挂了实现却没挂号那条路径(比如沙箱里的真路径)照样走真实文件 —— 两边可以并存。 </summary>
        public static 文件接口C 取(string 路径) =>
            是虚拟(路径) && _根实现 != null ? _根实现 : 真实文件实现C.单例;

        /// <summary> 平台那一份**只管虚拟路径**:给的就是虚拟路径时问它(自检 / 平台自己对账用) </summary>
        public static 文件接口C? 虚拟实现(string 路径) =>
            是虚拟(路径) && _根实现 != null ? _根实现 : null;

        private static bool 是虚拟(string 路径) => 虚拟路径C.是虚拟(路径);

        // ---------------- 字节 ----------------

        /// <summary> 读成字节(路径是哪一边由它自己判) </summary>
        public static byte[] 读字节(string 路径) => 取(路径).读字节(路径);

        /// <summary> 只读这一块(D3 分块读用;到末尾给空数组) </summary>
        public static byte[] 读字节块(string 路径, long 偏移, int 上限) => 取(路径).读字节块(路径, 偏移, 上限);

        /// <summary> 覆盖写(缺的目录先建出来 —— 与老 `写字到文件` 同口径) </summary>
        public static void 写字节(string 路径, byte[] 内容) => 取(路径).写字节(路径, 内容);

        /// <summary> 追加写 </summary>
        public static void 追加字节(string 路径, byte[] 内容) => 取(路径).追加字节(路径, 内容);

        // ---------------- 文本(编码口径只有一处:就在下面这几个方法里)----------------

        /// <summary> 读成文本(严格 UTF-8 + 摘 BOM;不是 UTF-8 由 `文本落盘C.解源文件` 给一句人话)。 </summary>
        public static string 读文本(string 路径) => 解码(读字节(路径));

        /// <summary>
        /// 字节 → 文本的**唯一口径**:摘 UTF-8 BOM、**严格 UTF-8**(碰上一个非法字节就抛
        /// `DecoderFallbackException`)。
        ///
        /// 为什么严格:以前用 `File.ReadAllText` 那一档,非法字节会被静默换成 `U+FFFD`,
        /// 于是中文 Windows 上把源码存成 GBK 时报出来的是 `未知指令: �` 这种**读不懂的**错
        /// (T16 治过一回)。这里只做**纯解码**,不负责编文案 —— 要带行号 / 编码提示的报错
        /// 由编译器那侧的 `文本落盘C.解源文件` 给(它调这里)。
        /// </summary>
        public static string 解码(byte[] 字节)
        {
            int 起 = 字节.Length >= 3 && 字节[0] == 0xEF && 字节[1] == 0xBB && 字节[2] == 0xBF ? 3 : 0;
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(字节, 起, 字节.Length - 起);
        }

        /// <summary> 文本 → 字节(带不带 BOM 由调用方说了算;**全仓只有这一处编 UTF-8**)。
        ///
        /// **注意这一条**(H64 实测抓到的):`UTF8Encoding(true)` 的**`GetBytes` 不加 BOM** ——
        /// 那个 `true` 只管 `GetPreamble()` 给不给那三个字节(`File.WriteAllText` 是自己先写前导
        /// 再 `GetBytes`)。所以带 BOM 这一档要**自己拼上前导**;少了这一句,凡是走"写文本"落盘的
        /// 带 BOM 文件都会被悄悄洗掉 BOM。 </summary>
        public static byte[] 编码(string 文本, bool 有BOM)
        {
            byte[] 正文 = new UTF8Encoding(false).GetBytes(文本);
            if (!有BOM) return 正文;
            byte[] 前导 = new UTF8Encoding(true).GetPreamble();
            if (前导.Length == 0) return 正文;
            var 结果 = new byte[前导.Length + 正文.Length];
            Array.Copy(前导, 0, 结果, 0, 前导.Length);
            Array.Copy(正文, 0, 结果, 前导.Length, 正文.Length);
            return 结果;
        }

        /// <summary> 这个文件带不带 UTF-8 BOM(读不到 / 文件不存在 → 假 = 按"新建文件"算) </summary>
        public static bool 有BOM(string 路径)
        {
            try
            {
                byte[] 字节 = 读字节(路径);
                return 字节.Length >= 3 && 字节[0] == 0xEF && 字节[1] == 0xBB && 字节[2] == 0xBF;
            }
            catch { return false; }
        }

        /// <summary> 按原文件有没有 BOM 写回(写侧唯一口径) </summary>
        public static void 写文本(string 路径, string 文本) => 写文本(路径, 文本, 有BOM(路径));

        /// <summary> 已经知道编码头那一档(调用方一次读头、多处写回) </summary>
        public static void 写文本(string 路径, string 文本, bool 有BOM) => 写字节(路径, 编码(文本, 有BOM));

        // ---------------- 在不在 / 列目录 / 建删移复 ----------------

        public static bool 存在(string 路径) => 取(路径).存在(路径);

        public static bool 是目录(string 路径) => 取(路径).是目录(路径);

        /// <summary> 列这一层的名字(排序稳定) </summary>
        public static IReadOnlyList<string> 列名字(string 目录) => 取(目录).列名字(目录.TrimEnd('/', '\\'));

        /// <summary> 列这一层里**符合后缀**的那些**(只列本层、不递归;按名字排序)。
        /// 虚拟那边也只列名字、过滤在引擎这一侧做 —— 免得两边的通配符规则长歪 </summary>
        public static List<string> 列文件(string 目录, string 后缀 = "")
        {
            var 结果 = new List<string>();
            foreach (string 名 in 列名字(目录))
                if (后缀.Length == 0 || 名.EndsWith(后缀, StringComparison.OrdinalIgnoreCase))
                    结果.Add(虚拟路径C.拼接(目录, 名));
            return 结果;
        }

        /// <summary> 列这一层里**所有**项(文件与子目录都给,拼成完整路径) </summary>
        public static List<string> 列全部(string 目录) =>
            [.. 列名字(目录).Select(名 => 虚拟路径C.拼接(目录, 名))];

        public static void 建目录(string 目录) => 取(目录).建目录(目录);

        public static void 删文件(string 路径) => 取(路径).删文件(路径);

        public static void 删目录(string 路径) => 取(路径).删目录(路径);

        public static void 移动(string 旧路径, string 新路径) => 取(旧路径).移动(旧路径, 新路径);

        /// <summary> 改名 / 覆盖式替换(G3 `46 改名`;与 `移动到` 的"不覆盖"分工写清) </summary>
        public static void 改名(string 旧路径, string 新路径) => 取(旧路径).改名(旧路径, 新路径);

        public static void 复制(string 源路径, string 目标路径) => 取(源路径).复制(源路径, 目标路径);
    }

    /// <summary>
    /// H64:某一条挂上的实现**只管哪些路径**(手机端要按"用户授权过哪几棵树"分派,
    /// 以后 iOS 的书签、桌面端的压缩包虚拟目录也照这一套接)+ 一张静态分派表。
    /// 引擎各处不直接用这张表(走 `虚拟文件C`),它是**平台那一侧**的插口。
    /// </summary>
    public interface 路径归属C
    {
        /// <summary> 这条虚拟路径归不归我管 </summary>
        bool 认领(string 路径);
    }

    /// <summary>
    /// 按前缀分派的一张表(平台侧用:一棵 SAF 树挂一条)。**先挂的先生效**(后面的前缀更短就不抢)。
    /// </summary>
    public sealed class 路径分派表C : 文件接口C
    {
        private readonly List<(string 前缀, 文件接口C 实现)> _条 = [];

        /// <summary> 挂一段前缀(前缀本身也归它管) </summary>
        public 路径分派表C 挂(string 前缀, 文件接口C 实现)
        {
            _条.Add((虚拟路径C.规整(前缀).TrimEnd('/'), 实现));
            return this;
        }

        /// <summary> 摘掉一段前缀(没挂过就什么也不做) </summary>
        public void 摘(string 前缀)
        {
            string 净 = 虚拟路径C.规整(前缀).TrimEnd('/');
            _条.RemoveAll(条 => string.Equals(条.前缀, 净, StringComparison.Ordinal));
        }

        public bool 已挂(string 前缀) =>
            _条.Any(条 => string.Equals(条.前缀, 虚拟路径C.规整(前缀).TrimEnd('/'), StringComparison.Ordinal));

        /// <summary> 现在挂着哪些前缀(界面 / 自检看它) </summary>
        public IReadOnlyList<string> 前缀表 => [.. _条.Select(条 => 条.前缀)];

        private 文件接口C 找(string 路径)
        {
            // 两头都收成"不带尾斜杠"再比:挂的时候是 `内容://树X/工程/` 这种写法(界面拼出来的),
            // 查的时候可能是 `内容://树X/工程`,不规整就会**同一个目录认不出来** —— 那是很难查的一类 bug
            string 净 = 虚拟路径C.规整(路径).TrimEnd('/');
            foreach (var (前缀, 实现) in _条)
                if (净.StartsWith(前缀 + "/", StringComparison.Ordinal) || string.Equals(净, 前缀, StringComparison.Ordinal))
                    return 实现;
            throw new NotSupportedException($"这条路径还没有对应的文件实现(没挂上授权的树):{路径}");
        }

        public bool 存在(string 路径) => 找(路径).存在(路径);
        public bool 是目录(string 路径) => 找(路径).是目录(路径);
        public byte[] 读字节(string 路径) => 找(路径).读字节(路径);
        public byte[] 读字节块(string 路径, long 偏移, int 上限) => 找(路径).读字节块(路径, 偏移, 上限);
        public void 写字节(string 路径, byte[] 内容) => 找(路径).写字节(路径, 内容);
        public void 追加字节(string 路径, byte[] 内容) => 找(路径).追加字节(路径, 内容);
        public IReadOnlyList<string> 列名字(string 目录) => 找(目录).列名字(目录);
        public void 建目录(string 目录) => 找(目录).建目录(目录);
        public void 删文件(string 路径) => 找(路径).删文件(路径);
        public void 删目录(string 路径) => 找(路径).删目录(路径);
        public void 移动(string 旧路径, string 新路径) => 找(旧路径).移动(旧路径, 新路径);
        public void 改名(string 旧路径, string 新路径) => 找(旧路径).改名(旧路径, 新路径);
        public void 复制(string 源路径, string 目标路径) => 找(源路径).复制(源路径, 目标路径);
    }
}
