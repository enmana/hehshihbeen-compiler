using System.IO;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 一条"顶层常量初值"(槽地址 + 宽度 + 值)。
    /// 用途:顶层 `整数 初值 = 5;` / `短整数 表[4] = [1,2,3]` 这类初值,以前是编译成"程序开头一串存储指令",
    /// 现在随程序包携带,**装载时直接写进内存** —— 少一段开机就为几条常量跑的代码。
    /// 只搬顶层常量:字符串与池的数据仍旧运行期生成(它们不是常量,写到哪儿要运行才知道)。
    /// </summary>
    public readonly record struct 初值项C(int 地址, int 宽度, uint 值);

    /// <summary>
    /// 程序包里"**地址 → 源码位置**"那张表(H7)。独立 exe 的运行期报错靠它带行号 ——
    /// 修前那种包不带源码位置,报错只有说明(`⚠ 运行停下:除零错误`),用户看不出错在哪一行。
    ///
    /// 打包时从 `执行行映射C` 那份**调试文件表**压出来:相邻指令同属一条语句是常态
    /// (一条语句展开成好几条指令)⇒ 按"连续同位置"合成**一段**再编码,几百条语句只要几百段。
    /// 段长按**指令条数**记(指令定长 4 字节,端点 = 起址 + 条数 × 4)。
    /// </summary>
    public sealed class 源行表C
    {
        /// <summary> 段:一段连续的指令(起址 + 条数),都来自 文件号 那一份的 行号 那一行 </summary>
        public readonly record struct 段C(int 起址, int 条数, int 行号, int 文件号)
        {
            public int 止址 => 起址 + 条数 * 4;   // 不含
        }

        /// <summary> 包里的文件表(下标 = 段里的文件号;单文件程序就一条) </summary>
        public List<string> 文件们 = [];

        /// <summary> 按地址升序排好的段(查的时候二分) </summary>
        public List<段C> 段们 = [];

        public bool 空 => 段们.Count == 0;

        // 注:"从调试文件表压出这张表"那一步在 `中间层\执行行映射C.压源行表` ——
        // 那一层才有 `文件映射C`;这一份要能被 `HB宿主`(只链引擎几个文件)单独编,不能反向依赖它。

        /// <summary> 这个地址落在哪一行(查不到给 0) </summary>
        public int 查行号(uint 地址)
        {
            int 低 = 0, 高 = 段们.Count - 1;
            while (低 <= 高)
            {
                int 中 = (低 + 高) / 2;
                var 段 = 段们[中];
                if ((long)地址 < 段.起址) { 高 = 中 - 1; continue; }
                if ((long)地址 >= 段.止址) { 低 = 中 + 1; continue; }
                return 段.行号;
            }
            return 0;
        }

        /// <summary>
        /// 给人看的**位置**:`目录.hb 第 12 行` / `第 12 行`(单文件时文件名就不啰嗦了)/ 空串(查不到)。
        /// 独立 exe 的运行期报错就用它当抬头(H7)。
        /// </summary>
        public string 查位置(uint 地址)
        {
            int 低 = 0, 高 = 段们.Count - 1;
            while (低 <= 高)
            {
                int 中 = (低 + 高) / 2;
                var 段 = 段们[中];
                if ((long)地址 < 段.起址) { 高 = 中 - 1; continue; }
                if ((long)地址 >= 段.止址) { 低 = 中 + 1; continue; }
                string 名 = 文件们.Count > 1 && 段.文件号 >= 0 && 段.文件号 < 文件们.Count
                    ? Path.GetFileName(文件们[段.文件号]) : "";
                return 名.Length > 0 ? $"{名} 第 {段.行号} 行" : $"第 {段.行号} 行";
            }
            return "";
        }
    }

    /// <summary>
    /// HB 独立程序的"尾巴"格式(编译器贴、宿主读,两边共用这一份,免得格式对不上)。
    ///
    /// 老布局(全部小端,尾巴 18 字节):
    ///   [宿主 exe 本体][机器码 N 字节][消息区 M 字节][N:u32][M:u32][错误槽地址:u32][消息条数:u16]["HBEX"]
    /// 逐条初值布局(尾巴 22 字节,只在读老 exe 时用):
    ///   [宿主 exe][机器码 N][消息区 M][初值区 V][N:u32][M:u32][V:u32][错误槽:u32][条数:u16]["HBE2"]
    /// 段式初值布局(当前写的这一种,尾巴 22 字节):
    ///   [宿主 exe][机器码 N][消息区 M][初值区 V][N:u32][M:u32][V:u32][错误槽:u32][条数:u16]["HBE3"]
    /// 带源行表布局(H7 起,尾巴 26 字节;程序有源码位置时就写这一种):
    ///   [宿主 exe][机器码 N][消息区 M][初值区 V][源行区 S][N:u32][M:u32][V:u32][S:u32][错误槽:u32][条数:u16]["HBE4"]
    /// 消息区每条 = u16 长度 + UTF-8 字节(运行期报错的说明,例如"下标越界…")。
    /// 初值区段式 = u16 段数 + 每段(u32 起始地址、u16 字节数、这么多个字节);
    /// 初值区逐条 = u16 条数 + 每条 12 字节(地址:u32、宽度:u8 + 3 字节填充、值:u32)——
    /// 一个地址就要 4 字节,密集表里纯属重复,段式把它压掉了(见 `编初值区`)。
    /// 源行区 = u16 文件数 + 每个(u16 名字长 + UTF-8 名字)+ u16 段数 + 每段(u32 起址、u16 条数、u16 文件号、u32 行号)。
    /// 最后那截是固定字段 + 标记,宿主从尾巴往前切,不用解析 PE 结构;**四种包照样都能读**。
    /// </summary>
    public static class 程序包C
    {
        public const string 尾巴标记 = "HBEX";
        public const int 尾巴字节数 = 4 + 4 + 4 + 2 + 4;
        /// <summary> 逐条初值区的标记(老的写法,现在只读不写) </summary>
        public const string 初值尾巴标记 = "HBE2";
        public const int 初值尾巴字节数 = 4 + 4 + 4 + 4 + 2 + 4;
        /// <summary> 段式初值区的标记(当前写的这一种;两种初值布局的尾巴形状一样,只差标记与初值区内容) </summary>
        public const string 段尾巴标记 = "HBE3";
        public const int 段尾巴字节数 = 4 + 4 + 4 + 4 + 2 + 4;
        /// <summary> 带**源行表**的标记(H7;比段式多一个"源行区长"字段) </summary>
        public const string 源行尾巴标记 = "HBE4";
        public const int 源行尾巴字节数 = 4 + 4 + 4 + 4 + 4 + 2 + 4;

        /// <summary> 包格式版本号(宿主自报能力时也报它,见 `宿主能力C.能力清单`):
        /// 1 = 只会 `HBEX`;2 = 还会逐条初值 `HBE2`;3 = 还会段式初值 `HBE3`;4 = 还会读源行表 `HBE4`(H7) </summary>
        public const int 包格式_老包 = 1;
        public const int 包格式_逐条初值 = 2;
        public const int 包格式_段初值 = 3;
        public const int 包格式_源行表 = 4;

        /// <summary> 这一版会读、也会写的最高包格式 </summary>
        public static int 当前包格式 => 包格式_源行表;

        /// <summary> 一份贴好的程序:机器码 + 运行期报错说明 + 错误槽地址 + 顶层常量初值 + 源行表 </summary>
        public sealed class 文件包
        {
            public byte[] 机器码 = [];
            public List<string> 消息表 = [];
            public int 错误槽地址;
            public List<初值项C> 初值表 = [];
            /// <summary> 地址 → 源码位置(H7;老包里是空表 ⇒ 运行期报错只给说明) </summary>
            public 源行表C 源行表 = new();
        }

        /// <summary> 把宿主字节 + 机器码 + 报错说明(+ 顶层常量初值 + 源行表)拼成一个独立 exe 的全部字节 </summary>
        public static byte[] 打包(byte[] 宿主字节, byte[] 机器码, IReadOnlyList<string> 消息表, int 错误槽地址,
            IReadOnlyList<初值项C>? 初值表 = null, 源行表C? 源行表 = null)
        {
            var 消息区 = new List<byte>();
            int 条数 = 0;
            foreach (string 消息 in 消息表)
            {
                byte[] 字节 = Encoding.UTF8.GetBytes(消息 ?? "");
                if (字节.Length == 0 || 字节.Length > ushort.MaxValue) continue;
                消息区.AddRange(BitConverter.GetBytes((ushort)字节.Length));
                消息区.AddRange(字节);
                条数++;
            }
            byte[] 初值区 = 编初值区(初值表);
            byte[] 源行区 = 编源行区(源行表);
            bool 有初值 = 初值区.Length > 0;   // 没有初值就照老格式写,老宿主也还能读
            bool 有源行 = 源行区.Length > 0;   // 没有源码位置(手写汇编那种)照旧写低一档的包
            int 尾长 = 有源行 ? 源行尾巴字节数 : (有初值 ? 段尾巴字节数 : 尾巴字节数);
            var 出 = new List<byte>(宿主字节.Length + 机器码.Length + 消息区.Count + 初值区.Length + 源行区.Length + 尾长);
            出.AddRange(宿主字节);
            出.AddRange(机器码);
            出.AddRange(消息区);
            出.AddRange(初值区);
            出.AddRange(源行区);
            出.AddRange(BitConverter.GetBytes(机器码.Length));
            出.AddRange(BitConverter.GetBytes(消息区.Count));
            if (有初值 || 有源行) 出.AddRange(BitConverter.GetBytes(初值区.Length));
            if (有源行) 出.AddRange(BitConverter.GetBytes(源行区.Length));
            出.AddRange(BitConverter.GetBytes(错误槽地址));
            出.AddRange(BitConverter.GetBytes((ushort)条数));
            出.AddRange(Encoding.ASCII.GetBytes(有源行 ? 源行尾巴标记 : (有初值 ? 段尾巴标记 : 尾巴标记)));
            return [.. 出];
        }

        /// <summary> 段式初值区里"接哪条缝"的上限:缝 ≤ 4 字节(典型是跳掉的那一个 0)就补零接上 ——
        /// 补的零比新起一段的 6 字节段头少,而且装载时数据段本来就整块清过零(见 `虚拟机C.装载`),
        /// 补进去的 0 写不写一个样。缝再大就断段:那说明中间隔着别人的数据,不值得为空着的地方掏包体积。 </summary>
        public const int 补零上限 = 4;

        /// <summary>
        /// 初值区编码(段式):u16 段数 + 每段(u32 起始地址 + u16 字节数 + 字节)。
        /// 地址挨着的初值并进同一段 —— 逐条写时每条都要 4 字节地址 + 3 字节填充,进段以后一个初值只剩自己的宽度,
        /// 密集表因此能小到原来的三分之一(实测 `整数 表[100]`:1202 → 408 字节)。
        /// 表先按地址排序,再一条条往当前段里接;接不上(缝太大 / 会超出 u16 段长)就收段开新段。
        /// 宽度不合法(不是 1/2/4)的按 4 看 —— 与 `应用初值` 同一个口径,免得两处对同一张表理解不一样。
        /// </summary>
        public static byte[] 编初值区(IReadOnlyList<初值项C>? 表)
        {
            if (表 == null || 表.Count == 0) return [];
            var 段表 = new List<(int 起, List<byte> 数据)>();
            foreach (var 项 in 表.OrderBy(x => x.地址))
            {
                int 宽 = 项.宽度 is 1 or 2 or 4 ? 项.宽度 : 4;
                if (段表.Count > 0)
                {
                    var 末段 = 段表[^1];
                    int 缝 = 项.地址 - (末段.起 + 末段.数据.Count);
                    if (缝 >= 0 && 缝 <= 补零上限 && 末段.数据.Count + 缝 + 宽 <= ushort.MaxValue)
                    {
                        for (int i = 0; i < 缝; i++) 末段.数据.Add(0);
                        for (int i = 0; i < 宽; i++) 末段.数据.Add((byte)(项.值 >> (i * 8)));
                        continue;
                    }
                }
                var 新段 = new List<byte>(宽);
                for (int i = 0; i < 宽; i++) 新段.Add((byte)(项.值 >> (i * 8)));
                段表.Add((项.地址, 新段));
            }
            var 出 = new List<byte>(2 + 段表.Count * 8);
            出.Add((byte)(段表.Count & 0xFF));
            出.Add((byte)((段表.Count >> 8) & 0xFF));
            foreach (var (起, 数据) in 段表)
            {
                出.AddRange(BitConverter.GetBytes(起));
                出.AddRange(BitConverter.GetBytes((ushort)数据.Count));
                出.AddRange(数据);
            }
            return [.. 出];
        }

        /// <summary>
        /// 初值区解码(段式):把每段摊成"宽度 4 / 2 / 1"的条目(装不下 4 就 2、再不行 1)。
        /// 段里存的是**内存镜像**,不是逐条的表 —— 摊出来的宽度可能跟编译时那条不一样(1+4 会摊成 4+1),
        /// 但 `应用初值` 写出来的字节逐字节相同,所以能这么摊。长度不对就返回空表,不瞎认。
        /// </summary>
        public static List<初值项C> 解初值区(byte[] 全文, int 起, int 长)
        {
            var 表 = new List<初值项C>();
            if (长 < 2 || 起 < 0 || 起 + 长 > 全文.Length) return 表;
            int 段数 = BitConverter.ToUInt16(全文, 起);
            int 位置 = 起 + 2;
            int 止 = 起 + 长;
            for (int i = 0; i < 段数 && 位置 + 6 <= 止; i++)
            {
                int 段起 = BitConverter.ToInt32(全文, 位置);
                int 段长 = BitConverter.ToUInt16(全文, 位置 + 4);
                位置 += 6;
                if (段长 == 0 || 位置 + 段长 > 止) return 表;   // 段自己写坏了:前面的算数,后面的不认
                int 段止 = 位置 + 段长;
                while (位置 < 段止)
                {
                    int 宽 = 段止 - 位置 >= 4 ? 4 : 段止 - 位置 >= 2 ? 2 : 1;
                    uint 值 = 0;
                    for (int b = 0; b < 宽; b++) 值 |= (uint)全文[位置 + b] << (b * 8);
                    表.Add(new 初值项C(段起 + (位置 - (段止 - 段长)), 宽, 值));
                    位置 += 宽;
                }
            }
            return 表;
        }

        /// <summary> 逐条初值区解码(老 `HBE2` 用的写法:u16 条数 + 每条 12 字节);长度不对就返回空表,不瞎认 </summary>
        public static List<初值项C> 解逐条初值区(byte[] 全文, int 起, int 长)
        {
            var 表 = new List<初值项C>();
            if (长 < 2 || 起 < 0 || 起 + 长 > 全文.Length) return 表;
            int 条数 = BitConverter.ToUInt16(全文, 起);
            int 位置 = 起 + 2;
            for (int i = 0; i < 条数 && 位置 + 12 <= 起 + 长; i++, 位置 += 12)
            {
                int 地址 = BitConverter.ToInt32(全文, 位置);
                int 宽度 = 全文[位置 + 4];
                表.Add(new 初值项C(地址, 宽度, BitConverter.ToUInt32(全文, 位置 + 8)));
            }
            return 表;
        }

        /// <summary>
        /// 源行区编码(H7):u16 文件数 + 每个(u16 名字长 + UTF-8 名字)+ u16 段数 +
        /// 每段(u32 起址 + u16 条数 + u16 文件号 + u32 行号)。空表 / 空段 ⇒ 零字节(照旧写低一档的包)。
        /// </summary>
        public static byte[] 编源行区(源行表C? 表)
        {
            if (表 == null || 表.空) return [];
            var 出 = new List<byte>();
            void 写u16(int 值) => 出.AddRange(BitConverter.GetBytes((ushort)值));
            void 写u32(int 值) => 出.AddRange(BitConverter.GetBytes(值));
            var 文件们 = 表.文件们;
            写u16(Math.Min(文件们.Count, ushort.MaxValue));
            foreach (string 名 in 文件们.Take(ushort.MaxValue))
            {
                byte[] 字节 = Encoding.UTF8.GetBytes(名 ?? "");
                if (字节.Length > ushort.MaxValue) 字节 = 字节[..ushort.MaxValue];
                写u16(字节.Length);
                出.AddRange(字节);
            }
            var 段们 = 表.段们.Take(ushort.MaxValue).ToList();
            写u16(段们.Count);
            foreach (var 段 in 段们)
            {
                写u32(段.起址);
                写u16(Math.Clamp(段.条数, 0, ushort.MaxValue));
                写u16(Math.Clamp(段.文件号, 0, ushort.MaxValue));
                写u32(段.行号);
            }
            return [.. 出];
        }

        /// <summary> 源行区解码(长度不对就返回空表,不瞎认 —— 与初值区同一条口径) </summary>
        public static 源行表C 解源行区(byte[] 全文, int 起, int 长)
        {
            var 表 = new 源行表C();
            if (长 <= 0 || 起 < 0 || 起 + 长 > 全文.Length) return 表;
            int 位置 = 起, 止 = 起 + 长;
            try
            {
                if (位置 + 2 > 止) return 表;
                int 文件数 = BitConverter.ToUInt16(全文, 位置); 位置 += 2;
                for (int i = 0; i < 文件数; i++)
                {
                    if (位置 + 2 > 止) return new 源行表C();
                    int 名长 = BitConverter.ToUInt16(全文, 位置); 位置 += 2;
                    if (位置 + 名长 > 止) return new 源行表C();
                    表.文件们.Add(Encoding.UTF8.GetString(全文, 位置, 名长));
                    位置 += 名长;
                }
                if (位置 + 2 > 止) return new 源行表C();
                int 段数 = BitConverter.ToUInt16(全文, 位置); 位置 += 2;
                for (int i = 0; i < 段数; i++)
                {
                    if (位置 + 12 > 止) return new 源行表C();
                    int 起址 = BitConverter.ToInt32(全文, 位置);
                    int 条数 = BitConverter.ToUInt16(全文, 位置 + 4);
                    int 文件号 = BitConverter.ToUInt16(全文, 位置 + 6);
                    int 行号 = BitConverter.ToInt32(全文, 位置 + 8);
                    位置 += 12;
                    表.段们.Add(new 源行表C.段C(起址, 条数, 行号, 文件号));
                }
            }
            catch { return new 源行表C(); }
            return 表;
        }

        /// <summary> 从一个 exe 的全文里取出贴着的那份程序;没贴(或格式不对)返回 null。四种标记都认 </summary>
        public static 文件包? 解包(byte[] 全文)
        {
            if (全文.Length < 尾巴字节数) return null;
            // 先按 26 字节尾巴试(带源行表,H7),再按两种 22 字节尾巴试(段式与逐条形状一样,只差标记),
            // 标记不在就退回老格式(尾巴 18 字节);都不认 = 没贴程序
            if (全文.Length >= 源行尾巴字节数)
            {
                int 尾 = 全文.Length - 源行尾巴字节数;
                if (Encoding.ASCII.GetString(全文, 尾 + 22, 4) == 源行尾巴标记) return 按源行格式取(全文, 尾);
            }
            if (全文.Length >= 段尾巴字节数)
            {
                int 尾 = 全文.Length - 段尾巴字节数;
                string 标记 = Encoding.ASCII.GetString(全文, 尾 + 18, 4);
                if (标记 == 段尾巴标记) return 按段格式取(全文, 尾);
                if (标记 == 初值尾巴标记) return 按逐条格式取(全文, 尾);
            }
            int 老尾 = 全文.Length - 尾巴字节数;
            if (Encoding.ASCII.GetString(全文, 老尾 + 14, 4) != 尾巴标记) return null;
            return 按老格式取(全文, 老尾);
        }

        /// <summary> 26 字节尾巴(带源行表)那一档:比段式多一个"源行区长",其余字段往后挪 4 字节 </summary>
        private static 文件包? 按源行格式取(byte[] 全文, int 尾)
        {
            int 机器码长 = BitConverter.ToInt32(全文, 尾);
            int 消息区长 = BitConverter.ToInt32(全文, 尾 + 4);
            int 初值区长 = BitConverter.ToInt32(全文, 尾 + 8);
            int 源行区长 = BitConverter.ToInt32(全文, 尾 + 12);
            int 错误槽 = BitConverter.ToInt32(全文, 尾 + 16);
            int 条数 = BitConverter.ToUInt16(全文, 尾 + 20);
            if (机器码长 <= 0 || 消息区长 < 0 || 初值区长 < 0 || 源行区长 < 0) return null;
            int 机器码起 = 尾 - 源行区长 - 初值区长 - 消息区长 - 机器码长;
            if (机器码起 < 0) return null;
            var 结果 = new 文件包 { 错误槽地址 = 错误槽 };
            结果.机器码 = new byte[机器码长];
            Array.Copy(全文, 机器码起, 结果.机器码, 0, 机器码长);
            读消息(全文, 尾 - 源行区长 - 初值区长 - 消息区长, 尾 - 源行区长 - 初值区长, 条数, 结果.消息表);
            结果.初值表 = 解初值区(全文, 尾 - 源行区长 - 初值区长, 初值区长);
            结果.源行表 = 解源行区(全文, 尾 - 源行区长, 源行区长);
            return 结果;
        }

        private static 文件包? 按老格式取(byte[] 全文, int 尾)
        {
            int 机器码长 = BitConverter.ToInt32(全文, 尾);
            int 消息区长 = BitConverter.ToInt32(全文, 尾 + 4);
            int 错误槽 = BitConverter.ToInt32(全文, 尾 + 8);
            int 条数 = BitConverter.ToUInt16(全文, 尾 + 12);
            if (机器码长 <= 0 || 消息区长 < 0) return null;
            int 机器码起 = 尾 - 消息区长 - 机器码长;
            if (机器码起 < 0) return null;
            var 结果 = new 文件包 { 错误槽地址 = 错误槽 };
            结果.机器码 = new byte[机器码长];
            Array.Copy(全文, 机器码起, 结果.机器码, 0, 机器码长);
            读消息(全文, 尾 - 消息区长, 尾, 条数, 结果.消息表);
            return 结果;
        }

        private static 文件包? 按段格式取(byte[] 全文, int 尾) => 按初值格式取(全文, 尾, 解初值区);

        private static 文件包? 按逐条格式取(byte[] 全文, int 尾) => 按初值格式取(全文, 尾, 解逐条初值区);

        /// <summary> 两种 22 字节尾巴(段式 / 逐条)只差初值区怎么解,其余字段一模一样 —— 这一处收口 </summary>
        private static 文件包? 按初值格式取(byte[] 全文, int 尾, Func<byte[], int, int, List<初值项C>> 解初值)
        {
            int 机器码长 = BitConverter.ToInt32(全文, 尾);
            int 消息区长 = BitConverter.ToInt32(全文, 尾 + 4);
            int 初值区长 = BitConverter.ToInt32(全文, 尾 + 8);
            int 错误槽 = BitConverter.ToInt32(全文, 尾 + 12);
            int 条数 = BitConverter.ToUInt16(全文, 尾 + 16);
            if (机器码长 <= 0 || 消息区长 < 0 || 初值区长 < 0) return null;
            int 机器码起 = 尾 - 初值区长 - 消息区长 - 机器码长;
            if (机器码起 < 0) return null;
            var 结果 = new 文件包 { 错误槽地址 = 错误槽 };
            结果.机器码 = new byte[机器码长];
            Array.Copy(全文, 机器码起, 结果.机器码, 0, 机器码长);
            读消息(全文, 尾 - 初值区长 - 消息区长, 尾 - 初值区长, 条数, 结果.消息表);
            结果.初值表 = 解初值(全文, 尾 - 初值区长, 初值区长);
            return 结果;
        }

        private static void 读消息(byte[] 全文, int 起, int 止, int 条数, List<string> 出)
        {
            int 位置 = 起;
            for (int i = 0; i < 条数 && 位置 + 2 <= 止; i++)
            {
                int 长 = BitConverter.ToUInt16(全文, 位置);
                位置 += 2;
                if (位置 + 长 > 止) break;
                出.Add(Encoding.UTF8.GetString(全文, 位置, 长));
                位置 += 长;
            }
        }

        /// <summary>
        /// 把顶层常量初值写进内存(装载之后、运行之前调一次)。
        /// 越界 / 地址不合法的一律跳过:这是装载期的旁路数据,宁可少写一个字,也不许把装载搞崩。
        /// </summary>
        public static void 应用初值(byte[] 内存, IReadOnlyList<初值项C>? 表)
        {
            if (表 == null) return;
            foreach (var (地址, 宽度, 值) in 表)
            {
                int 宽 = 宽度 is 1 or 2 or 4 ? 宽度 : 4;
                if (地址 < 0 || 地址 + 宽 > 内存.Length) continue;
                for (int i = 0; i < 宽; i++) 内存[地址 + i] = (byte)(值 >> (i * 8));
            }
        }
    }
}
