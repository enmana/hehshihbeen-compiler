using System.IO;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// C26:文本落盘的**唯一口径** —— 「原文件有没有 BOM,写回时就照旧」。
    ///
    /// 为什么单开一个文件:`File.WriteAllText(路径, 文本, Encoding.UTF8)` 里那个 `Encoding.UTF8`
    /// 是**带 BOM 的**(.NET 的静态属性就是 `UTF8Encoding(true)`),于是 IDE 存一次就把本来没有 BOM 的
    /// `.hb` / `.hbp` 写成带 BOM 的 —— `git diff` 上看着像整个文件都改了,BOM 还容易被当成乱码字符进第一行;
    /// 反过来"一律不带 BOM"也不行:本来就带 BOM 的那几份会被洗掉,`git diff` 同样要翻。
    /// 全仓此前有三份各写各的判定(`工程维护C.写回` / `hbcc` 的两处 `--写回`),IDE 那条路干脆没判 —— 收成这里一份。
    ///
    /// 口径:
    /// · 目标文件**存在** → 读它头 3 字节,原来有 BOM 就写 BOM、原来没有就不写;
    ///   `文本` 里那份 BOM 字符(`\uFEFF`)早已被 `分词器C.清洗文本` 摘掉,不会写两遍;
    /// · 目标文件**不存在**(新建 / 另存为到新名字)→ 不带 BOM 写(与 `解决方案C.新建文件` 那一档一致)。
    ///
    /// 这里**只管编码头,不修剪文本** —— 末尾空行与换行风格一个字都不动。C26 有一半的毛病就是"修剪"引起的
    /// (`分词器C.清洗文本` 里那个 `TrimEnd`),所以读盘 / 写盘两侧都别在这儿再做一遍清洗。
    /// </summary>
    public static class 文本落盘C
    {
        /// <summary> 文件带不带 UTF-8 BOM(读不到 / 文件不存在 → 假 = 按"新建文件"算)。
        /// H64:口径已经收在 `虚拟文件C` 一处(那边也认系统目录那一档),这里只做转调、不再自己实现 </summary>
        public static bool 有BOM(string 路径) => 虚拟文件C.有BOM(路径);

        /// <summary> 按**目标文件现在有没有 BOM** 写回(最常用的一档) </summary>
        public static void 写回(string 路径, string 文本) => 写回(路径, 文本, 有BOM(路径));

        /// <summary>
        /// 已经知道有没有 BOM 时走这一档(调用方一次读头、多处写回)。
        /// H64:落盘的字节与编码头都从 `虚拟文件C` 走 —— 系统目录(SAF)那份实现也吃同一口径。
        /// <para>
        /// **行尾照原文件**(2026-10 补):`任务清单.md` 这类**手工维护**的文本是 CRLF,而
        /// `--清单 --索引` 拼出来的表是 LF ⇒ 以前写一次就把整份文件的换行风格冲掉,
        /// `git diff` 上 11,000 多行全像改了(实测踩到)。现在:原文件以 CRLF 为主就整篇写 CRLF,
        /// 否则按调用方给的文本原样写 —— **只补这一件事,不动文本别的字符**。
        /// </para>
        /// </summary>
        public static void 写回(string 路径, string 文本, bool 有BOM)
        {
            string 要写 = 文本;
            try
            {
                if (File.Exists(路径) && 用CRLF(虚拟文件C.读字节(路径)))
                    要写 = 文本.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }
            catch { /* 读不到就按调用方给的写(退化成老口径) */ }
            虚拟文件C.写文本(路径, 要写, 有BOM);
        }

        /// <summary> 这份字节的换行风格是不是 CRLF(过半就算:允许文件里混着两种) </summary>
        private static bool 用CRLF(byte[] 字节)
        {
            int crlf = 0, lf = 0;
            for (int i = 0; i < 字节.Length; i++)
                if (字节[i] == (byte)'\n')
                {
                    if (i > 0 && 字节[i - 1] == (byte)'\r') crlf++; else lf++;
                }
            return crlf > 0 && crlf >= lf;
        }

        /// <summary> 文本 → 字节(带不带 BOM 由调用方说了算;实现在 `虚拟文件C.编码`,全仓只那一处编 UTF-8)</summary>
        public static byte[] 编码(string 文本, bool 有BOM) => 虚拟文件C.编码(文本, 有BOM);

        // ================= T16(2026-09-27 评测整改):读侧与写侧配对 =================

        /// <summary>
        /// **唯一读入口**:读一份源文件(`.hb` / `.hba` / 任何文本)。
        ///
        /// 口径与写侧配成一对:写侧按"原文件有没有 BOM"写回,读侧就把 UTF-8 的 BOM 摘掉交出去
        /// (BOM 是编码头,不是源码的一部分);**不是 UTF-8 就给一条看得懂的提示,不猜着编**。
        ///
        /// 为什么非要单开:`File.ReadAllText(路径)` 的兜底是"非法字节换 U+FFFD" —— 中文 Windows 上
        /// 把源码存成 GBK(很常见)时,报出来的是 `未知指令: �` 这种**读不懂的**错。
        /// 仓库在宿主能力那一侧早就有"先严 UTF-8、再系统代码页"的阶梯(`宿主能力C.解码输出`),
        /// 源码读取这一侧一直没走。
        /// **只提示、不转码、不动用户的文件**(用户自己另存为 UTF-8;要替他转是另一件事)。
        /// </summary>
        public static string 读源文件(string 路径) => 解源文件(虚拟文件C.读字节(路径), 路径);

        /// <summary> `读源文件` 的内核:已经拿到字节时走它(测试与 IDE 的"打开文件"路也用它)。
        /// **解码**那一手在 `虚拟文件C.解码`(与写侧同一处);这里只管"不是 UTF-8 时报一条带行号的错" </summary>
        public static string 解源文件(byte[] 字节, string 路径 = "")
        {
            int 起 = 字节.Length >= 3 && 字节[0] == 0xEF && 字节[1] == 0xBB && 字节[2] == 0xBF ? 3 : 0;
            try
            {
                // 严格 UTF-8:碰上一个非法字节就抛,不静默换成 U+FFFD(口径见 `虚拟文件C.解码`)
                return 虚拟文件C.解码(字节);
            }
            catch (DecoderFallbackException) { /* 不是 UTF-8:下面给提示 */ }

            int 行 = 第一个坏字节在第几行(字节, 起);
            string 像什么 = 看着像哪个代码页(字节, 起);
            string 在哪 = 路径.Length > 0 ? $"(文件:{路径})" : "";
            throw new 编译异常C(
                $"这个源文件不是 UTF-8 编码{像什么} —— 请用编辑器另存为 UTF-8 再编译{在哪};" +
                "HB 源码一律按 UTF-8 读(带不带 BOM 都认),别的编码解出来是乱码,后面那些报错都当不了真",
                "source file is not UTF-8; re-save it as UTF-8",
                行);
        }

        /// <summary> 第一个非法字节落在第几行(1 起;报错指到真正出问题的那一行,不是永远第 1 行) </summary>
        private static int 第一个坏字节在第几行(byte[] 字节, int 起)
        {
            int 坏 = 第一个坏字节(字节, 起);
            if (坏 < 0) return 1;
            int 行 = 1;
            for (int i = 起; i < 坏; i++)
                if (字节[i] == (byte)'\n') 行++;
            return 行;
        }

        /// <summary> 按 UTF-8 的编码规则找第一个非法字节的位置(没有就 -1)。
        /// 自己走一遍而不是拿严格解码器试前 n 个字节:那样"前缀正好断在一个合法多字节字中间"会被当成非法,
        /// 行号就指错了。这里查的是**真非法**:引导字节越界、后续字节不是 10xxxxxx、过长编码、代理区、超出 U+10FFFF。 </summary>
        private static int 第一个坏字节(byte[] b, int 起)
        {
            int i = 起;
            while (i < b.Length)
            {
                byte c = b[i];
                if (c < 0x80) { i++; continue; }
                int 后续 = c >= 0xC2 && c <= 0xDF ? 1 : c >= 0xE0 && c <= 0xEF ? 2 : c >= 0xF0 && c <= 0xF4 ? 3 : -1;
                if (后续 < 0) return i;
                if (i + 后续 >= b.Length) return i;
                for (int k = 1; k <= 后续; k++)
                    if ((b[i + k] & 0xC0) != 0x80) return i;
                if (后续 == 2)
                {
                    if (c == 0xE0 && b[i + 1] < 0xA0) return i;      // 过长编码
                    if (c == 0xED && b[i + 1] > 0x9F) return i;      // 代理区 U+D800~DFFF
                }
                if (后续 == 3)
                {
                    if (c == 0xF0 && b[i + 1] < 0x90) return i;
                    if (c == 0xF4 && b[i + 1] > 0x8F) return i;      // 超过 U+10FFFF
                }
                i += 后续 + 1;
            }
            return -1;
        }

        /// <summary> 这份字节"看着像"哪个代码页(中文 Windows 的 GBK 就是 936);认不出返回空串 </summary>
        private static string 看着像哪个代码页(byte[] 字节, int 起)
        {
            try { Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance); } catch { }
            var 文字信息 = System.Globalization.CultureInfo.CurrentCulture.TextInfo;
            foreach (int 码页 in new[] { 文字信息.ANSICodePage, 文字信息.OEMCodePage })
            {
                if (码页 <= 0 || 码页 == 65001) continue;
                try
                {
                    string 试 = Encoding.GetEncoding(码页).GetString(字节, 起, 字节.Length - 起);
                    if (!试.Contains('\uFFFD')) return $"(看着像 GBK / 代码页 {码页})";
                }
                catch { }
            }
            return "";
        }
    }
}
