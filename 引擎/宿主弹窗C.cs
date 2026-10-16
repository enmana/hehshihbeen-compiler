// ============================================================
// 宿主弹窗C —— **剪贴板 / 提示框 / 选文件**(H91,第406批 = G4 批 23)的**桌面那一份实现**。
//
// 为什么要单开一份:这三件事都得问**操作系统**要(剪贴板 / 一个真窗口 / 系统的文件对话框),
//   而它们**不是"文件能力"那一族** —— `虚拟文件C` 那套口子只管路径,挂不上"现在剪贴板里有什么"。
//   于是照 `虚拟文件C` 的格局单开一层:
//     · **引擎其余地方只认这一份的静态方法**;
//     · 平台实现(将来手机端 SAF / 别的壳)自己挂上来,或者干脆不挂 —— 不挂就是"这个平台没有这一手",
//       调用方拿到 `支持 = false`,**绝不假装"用户取消了"**(与 `选择器C` 那条口径同一条)。
//
// ⚠ **为什么不借 `System.Windows.Forms` / WPF 的 `Clipboard`**:
//   ① 那两套都要求线程是 **STA**,而 HB 程序跑在哪条线程上由宿主说了算(子进程那条腿是普通线程)
//      ⇒ 一调就是 `ThreadStateException`,而且**只在某些宿主上炸**(最难查的一类);
//   ② `HB宿主` 是个纯控制台程序(`net10.0`,不带 WindowsDesktop 框架),
//      链 WPF 只是为了一块剪贴板 —— 产物与启动都跟着涨。
//   ⇒ 直接 Win32:无线程要求、无框架依赖、零第三方。
//
// **平台**:Windows 这一份是完整的。非 Windows 上三样一律 `支持 = false`(状态码 25),
//   由调用方如实说"这一版没有" —— 不去猜、不去拼一个假对话框(与 C 后端"没有 X 就给状态 25"同一口径)。
// ============================================================

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary> 一次系统弹窗的结果(三样都用它:取消与"平台没有"是**两件事**,不许混) </summary>
    public readonly record struct 弹窗结果C(bool 支持, bool 好, string 文本, int 号)
    {
        public static 弹窗结果C 不支持() => new(false, false, "", 0);
        public static 弹窗结果C 糟了() => new(true, false, "", 0);
        public static 弹窗结果C 成了(string 文本, int 号 = 0) => new(true, true, 文本, 号);
    }

    /// <summary> 见文件头:剪贴板 / 提示框 / 选文件 —— **桌面这一份**(Win32;别处如实说不支持) </summary>
    public static class 宿主弹窗C
    {
        /// <summary>
        /// **准不准弹**(H91 的那道闸)。**默认关** —— 理由不是"省事",是**硬规矩 6**:
        ///   自检与命令行(`hbcc --run` / `--跨进程` / 独立 exe)**不许开窗** ——
        ///   一个真的模态框会把这一趟永远挂在那儿(没人点它),而 `--selftest` 是全量跑几十组的。
        ///   ⇒ **交互式宿主**(桌面 IDE 的「执行」)在跑用户程序之前 `允许弹窗(真)`;
        ///   命令行要弹就显式写 `hbcc … --run --弹窗`(看得到窗口的人在的时候才写)。
        /// 关着的时候这三条一律给 `状态_不支持`(25)—— 那是"这个宿主不弹",与"平台没有这一手"同一个出口,
        /// 调用方(库那一侧)如实说一句就完了,**不是**假装用户点了"取消"。
        ///
        /// ⚠ **H39-2 之后程序跑在子进程里** ⇒ 界面这一侧设了不算:真正弹窗的是子进程。
        ///   于是有一份**环境变量**口径:`跑程序通道C.起` 按这一格给子进程设 `HB_弹窗`,
        ///   子进程启动时读它(见下面静态构造)。环境变量是"每个子进程启动那一刻读一次"的,
        ///   所以**同一个界面进程里来回开关也照旧准**(下一次起子进程就是新值)。
        /// 另外留一个 `HB_弹窗=1` 给命令行自报:`hbcc` 之外自己起 `HB宿主` 的人也能开。
        /// </summary>
        public static bool 允许弹窗 { get; private set; }

        /// <summary> 那个环境变量的名字(子进程那一侧读;**值 `1` = 允许**)</summary>
        public const string 环境变量名 = "HB_弹窗";

        static 宿主弹窗C()
        {
            try { 允许弹窗 = Environment.GetEnvironmentVariable(环境变量名) == "1"; } catch { 允许弹窗 = false; }
        }

        /// <summary> 开 / 关那道闸(见 `允许弹窗` 的说明;宿主在跑程序之前设一次) </summary>
        public static void 设允许弹窗(bool 开) => 允许弹窗 = 开;

        /// <summary> 这个平台有没有这一手(Windows = 有;别处 = 没有,调用方给状态 25) </summary>
        public static bool 支持 => OperatingSystem.IsWindows();

        /// <summary> 现在**真能弹**吗(平台有这一手 **且** 那道闸开着)—— 提示框 / 选文件都看它 </summary>
        public static bool 能弹 => 支持 && 允许弹窗;

        // ================= 剪贴板 =================

        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;

        [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr 属主);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern bool EmptyClipboard();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetClipboardData(uint 格式, IntPtr 数据);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint 格式);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool IsClipboardFormatAvailable(uint 格式);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint 旗标, UIntPtr 字节数);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr 块);
        [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr 块);
        [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr 块);
        [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr 块);

        /// <summary>
        /// 开一次剪贴板。**别的进程正开着它**是常态(输入法、聊天软件都在抢),
        /// 所以照规矩**重试几次**再认输 —— 一次不成就报"打不开"是这一族最常见的假故障。
        /// </summary>
        private static bool 开剪贴板()
        {
            for (int 试 = 0; 试 < 10; 试++)
            {
                if (OpenClipboard(IntPtr.Zero)) return true;
                System.Threading.Thread.Sleep(10);
            }
            return false;
        }

        /// <summary>
        /// **诊断开关**(环境变量 `HB_弹窗诊断=1`):把"卡在哪一步"逐行打到标准错误。
        /// 为什么要有它:这一族的失败**全是同一种现象**(读到空串 / 状态 25),
        /// 而原因可能是"格式不在 / 打不开剪贴板 / 拿到空句柄 / 平台不支持"四种 ——
        /// 不开这个开关就只能靠猜(这一条实测查了三轮)。
        /// </summary>
        private static void 诊断(string 话)
        {
            if (Environment.GetEnvironmentVariable("HB_弹窗诊断") is { Length: > 0 })
                Console.Error.WriteLine("[弹窗] " + 话);
        }

        /// <summary> 主机的诊断口(给 `宿主能力C` 那几条搬运用;同一个环境变量) </summary>
        public static void 报(string 话) => 诊断(话);

        /// <summary> 读剪贴板里的**文本**;剪贴板里没有文本 ⇒ 好 = true 且文本为空串(不是错) </summary>
        public static 弹窗结果C 取剪贴板()
        {
            诊断($"支持={支持} 有文本格式={IsClipboardFormatAvailable(CF_UNICODETEXT)}");
            if (!支持) return 弹窗结果C.不支持();
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return 弹窗结果C.成了("");
            if (!开剪贴板()) { 诊断("OpenClipboard 重试 10 次都没开成"); return 弹窗结果C.糟了(); }
            try
            {
                IntPtr 块 = GetClipboardData(CF_UNICODETEXT);
                诊断($"GetClipboardData 句柄={块}");
                if (块 == IntPtr.Zero) return 弹窗结果C.成了("");
                IntPtr 指针 = GlobalLock(块);
                诊断($"GlobalLock 指针={指针} 块大小={GlobalSize(块)}");
                if (指针 == IntPtr.Zero) return 弹窗结果C.糟了();
                try
                {
                    string 文本 = Marshal.PtrToStringUni(指针) ?? "";
                    诊断($"读到 {文本.Length} 个字符");
                    return 弹窗结果C.成了(文本);
                }
                finally { GlobalUnlock(块); }
            }
            catch (Exception 错) { 诊断("抛:" + 错.Message); return 弹窗结果C.糟了(); }
            finally { CloseClipboard(); }
        }

        /// <summary> 把一段文本放进剪贴板(覆盖原来的内容) </summary>
        public static 弹窗结果C 放剪贴板(string 文本)
        {
            if (!支持) return 弹窗结果C.不支持();
            文本 ??= "";
            // GMEM_MOVEABLE 是剪贴板要的那一档(它自己接管这块内存的所有权,所以我们**不 free** 成功后那块)
            UIntPtr 字节数 = (UIntPtr)((文本.Length + 1) * 2);
            IntPtr 块 = GlobalAlloc(GMEM_MOVEABLE, 字节数);
            if (块 == IntPtr.Zero) return 弹窗结果C.糟了();
            bool 交出去了 = false;
            try
            {
                IntPtr 指针 = GlobalLock(块);
                if (指针 == IntPtr.Zero) return 弹窗结果C.糟了();
                try { Marshal.Copy(文本.ToCharArray(), 0, 指针, 文本.Length); Marshal.WriteInt16(指针, 文本.Length * 2, 0); }
                finally { GlobalUnlock(块); }

                if (!开剪贴板()) return 弹窗结果C.糟了();
                try
                {
                    if (!EmptyClipboard()) return 弹窗结果C.糟了();
                    if (SetClipboardData(CF_UNICODETEXT, 块) == IntPtr.Zero) return 弹窗结果C.糟了();
                    交出去了 = true;      // 所有权交给系统了,不能 free
                    return 弹窗结果C.成了("");
                }
                finally { CloseClipboard(); }
            }
            finally { if (!交出去了) GlobalFree(块); }
        }

        /// <summary> 把剪贴板**清空**(什么格式都不留)。清完 `取剪贴板` 给空串。 </summary>
        public static 弹窗结果C 清剪贴板()
        {
            if (!支持) return 弹窗结果C.不支持();
            if (!开剪贴板()) { 诊断("清剪贴板:OpenClipboard 重试 10 次都没开成"); return 弹窗结果C.糟了(); }
            try { return EmptyClipboard() ? 弹窗结果C.成了("") : 弹窗结果C.糟了(); }
            catch (Exception 错) { 诊断("清剪贴板抛:" + 错.Message); return 弹窗结果C.糟了(); }
            finally { CloseClipboard(); }
        }

        // ================= 提示框 =================

        private const uint MB_OK = 0x00000000;
        private const uint MB_OKCANCEL = 0x00000001;
        private const uint MB_YESNO = 0x00000004;
        private const uint MB_YESNOCANCEL = 0x00000003;
        private const uint MB_ICONINFORMATION = 0x00000040;
        private const uint MB_ICONWARNING = 0x00000030;
        private const uint MB_ICONQUESTION = 0x00000020;
        private const uint MB_TOPMOST = 0x00040000;
        private const int IDOK = 1, IDCANCEL = 2, IDYES = 6, IDNO = 7;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MessageBoxW(IntPtr 属主, string 正文, string 标题, uint 型);

        /// <summary>
        /// 弹一个提示框 / 问一句。`按钮` 见 `宿主能力C.按钮_*`(0 确定 / 1 确定取消 / 2 是否 / 3 是否取消);
        /// 答案是 `宿主能力C.答_*`(0 确定 / 1 取消 / 2 是 / 3 否)。
        /// **平台没有这一手 / 那道闸关着**(见 `允许弹窗`)**都不弹**,直接给 `弹窗结果C.不支持()`
        /// —— 免得自检与命令行挂在一个永远等不到人点的窗口上(硬规矩 6:自检不许开窗)。
        /// </summary>
        public static 弹窗结果C 提示框(string 标题, string 正文, int 按钮, bool 警告 = false)
        {
            if (!能弹) { 诊断($"提示框不弹(平台={支持} 允许={允许弹窗})"); return 弹窗结果C.不支持(); }
            uint 型 = 按钮 switch
            {
                1 => MB_OKCANCEL,
                2 => MB_YESNO,
                3 => MB_YESNOCANCEL,
                _ => MB_OK,
            };
            型 |= 警告 ? MB_ICONWARNING : (按钮 == 0 ? MB_ICONINFORMATION : MB_ICONQUESTION);
            型 |= MB_TOPMOST;      // 程序可能没有前台窗口(控制台宿主)—— 不加这一条框可能躲在别的窗口后面
            int 回 = MessageBoxW(IntPtr.Zero, 正文 ?? "", 标题 ?? "", 型);
            int 答 = 回 switch { IDOK => 0, IDCANCEL => 1, IDYES => 2, IDNO => 3, _ => 1 };
            return 弹窗结果C.成了("", 答);
        }

        // ================= 选文件(系统的"打开 / 另存为"对话框) =================

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OPENFILENAMEW
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public string? lpstrFilter;
            public string? lpstrCustomFilter;
            public int nMaxCustFilter;
            public int nFilterIndex;
            public IntPtr lpstrFile;
            public int nMaxFile;
            public string? lpstrFileTitle;
            public int nMaxFileTitle;
            public string? lpstrInitialDir;
            public string? lpstrTitle;
            public int Flags;
            public short nFileOffset;
            public short nFileExtension;
            public string? lpstrDefExt;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public string? lpTemplateName;
            public IntPtr pvReserved;
            public int dwReserved;
            public int FlagsEx;
        }

        private const int OFN_READONLY = 0x00000001;
        private const int OFN_OVERWRITEPROMPT = 0x00000002;
        private const int OFN_HIDEREADONLY = 0x00000004;
        private const int OFN_NOCHANGEDIR = 0x00000008;
        private const int OFN_PATHMUSTEXIST = 0x00000800;
        private const int OFN_FILEMUSTEXIST = 0x00001000;
        private const int OFN_EXPLORER = 0x00080000;

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool GetOpenFileNameW(ref OPENFILENAMEW 参数);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool GetSaveFileNameW(ref OPENFILENAMEW 参数);

        /// <summary>
        /// 把后缀表拼成 Win32 要的那一串:每段两个以 0 结尾的串(`说明\0*.hb\0`),整串以 0 结尾。
        /// 说明用后缀自己("*.hb")—— 这一档**不给每种后缀编中文名**(那要一张 后缀→名字 的表,
        /// 而全仓没有这样一张表,现编一张只会与 `命名规则.md` 打架)。
        /// </summary>
        private static string 拼过滤表(string[]? 后缀)
        {
            var 段 = new StringBuilder();
            foreach (string 一个 in 后缀 ?? [])
            {
                string 净 = (一个 ?? "").Trim();
                if (净.Length == 0) continue;
                if (净[0] != '.') 净 = "." + 净;
                段.Append('*').Append(净).Append('\0').Append('*').Append(净).Append('\0');
            }
            段.Append("所有文件\0*.*\0").Append('\0');
            return 段.ToString();
        }

        /// <summary>
        /// 系统的"打开 / 另存为"对话框。`要保存` = true 走另存为(默认后缀取 `后缀` 里第一个)。
        /// 回 `好 = true` 时 `文本` = 用户挑的**全路径**;用户按了取消 ⇒ `好 = false` 但 `支持 = true`
        /// (那是正常的"没挑",**不是**平台缺这一手)。
        /// </summary>
        public static 弹窗结果C 选文件(string 标题, string[]? 后缀, bool 要保存, string 预填名 = "", string 初始目录 = "")
        {
            if (!能弹) { 诊断($"选文件不弹(平台={支持} 允许={允许弹窗})"); return 弹窗结果C.不支持(); }
            int 缓冲字数 = 4096;
            IntPtr 缓冲 = Marshal.AllocHGlobal(缓冲字数 * 2);
            try
            {
                // 预填名先写进缓冲(另存为那条路:用户看到的是这个名字)
                string 初值 = 预填名 ?? "";
                var 起 = new char[缓冲字数];
                for (int i = 0; i < 缓冲字数; i++) 起[i] = '\0';
                for (int i = 0; i < 初值.Length && i < 缓冲字数 - 1; i++) 起[i] = 初值[i];
                Marshal.Copy(起, 0, 缓冲, 缓冲字数);

                string 第一后缀 = "";
                foreach (string 一个 in 后缀 ?? [])
                {
                    string 净 = (一个 ?? "").Trim();
                    if (净.Length == 0) continue;
                    第一后缀 = 净[0] == '.' ? 净[1..] : 净;
                    break;
                }

                var 参 = new OPENFILENAMEW
                {
                    lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                    hwndOwner = IntPtr.Zero,
                    lpstrFilter = 拼过滤表(后缀),
                    nFilterIndex = 1,
                    lpstrFile = 缓冲,
                    nMaxFile = 缓冲字数,
                    lpstrTitle = string.IsNullOrEmpty(标题) ? (要保存 ? "另存为" : "打开文件") : 标题,
                    lpstrInitialDir = string.IsNullOrEmpty(初始目录) ? null : 初始目录,
                    lpstrDefExt = 要保存 && 第一后缀.Length > 0 ? 第一后缀 : null,
                    Flags = OFN_EXPLORER | OFN_NOCHANGEDIR | OFN_HIDEREADONLY | OFN_PATHMUSTEXIST
                            | (要保存 ? OFN_OVERWRITEPROMPT : OFN_FILEMUSTEXIST),
                };
                bool 成了 = 要保存 ? GetSaveFileNameW(ref 参) : GetOpenFileNameW(ref 参);
                if (!成了) return 弹窗结果C.糟了();       // 取消 / 失败都算"没挑"(调用方给空路径)
                string 路径 = Marshal.PtrToStringUni(缓冲) ?? "";
                return 路径.Length == 0 ? 弹窗结果C.糟了() : 弹窗结果C.成了(路径);
            }
            catch { return 弹窗结果C.糟了(); }
            finally { Marshal.FreeHGlobal(缓冲); }
        }
    }
}
