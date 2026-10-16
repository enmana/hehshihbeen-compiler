using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Intrinsics;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **H39-1:跑程序那条本机通道的错误** —— 通道本身出了事(子进程起不来 / 半路退出 / 帧格式不像话 /
    /// 两边协议版本对不上),**不是** HB 程序写错了。
    ///
    /// 为什么要单独一个类型:这类故障在前端必须"如实说得清"(见 H39 验收①),不能混进
    /// `运行期错误C`(那是用户程序自己的运行期错误,文案格式与处理方式都不一样);
    /// 也绝不能静默当成"程序跑完了"——那是最难查的毛病。
    /// </summary>
    public sealed class 跑程序通道错误C : Exception
    {
        public 跑程序通道错误C(string 说明, Exception? 内层 = null) : base(说明, 内层) { }
    }

    /// <summary>
    /// **H39-1:前端(IDE / hbcc)与"跑程序子进程"之间那条标准输入输出管道的协议** ——
    /// 帧格式 / 全部消息号 / 结果码 / 载荷读写,**只在这一份里定义**(别处再抄一套就是两个事实源)。
    ///
    /// <para><b>帧格式</b>:`u32 小端 长度 + u8 消息号 + 载荷`,`长度 = 1 + 载荷字节数`。
    /// 载荷一律用 `BinaryWriter` / `BinaryReader`(小端;字符串自带 7 位长度前缀,UTF-8),大块字节用
    /// "块"(`u32 长度 + 这么多个字节`)。写一帧要**加锁**(前端那条线程写命令、泵线程写"输入应答",
    /// 后面还会有别的线程发"中断"),写完 `Flush()`。长度放在最前面 ⇒ 收帧那一边永远知道自己该读多少。</para>
    ///
    /// <para><b>通道形状</b>:一条命令 → 一条"状态"帧(命令结束帧)。`命令_问候 / 中断请求 / 输入应答 /
    /// 图形事件 / 清除…` 是**只发不收**的(同步等会自己把自己锁死:中断请求正是"另一条线程在长跑中间插进来"
    /// 用的)。</para>
    ///
    /// <para><b>载荷布局(照着接就行)</b>:
    /// <list type="table">
    /// <item><term>命令_问候</term><description>i32 本端协议版本</description></item>
    /// <item><term>命令_装载</term><description>i32 内存大小 · 块 机器码 · 初值表 · 源行表 ·
    /// 可执行白名单 · 网址白名单 · bool 有图形呈现口 · bool 等输入 · bool 要记账 · 字符串 事件脚本文本</description></item>
    /// <item><term>命令_单步</term><description>bool 等输入 · bool 要记账</description></item>
    /// <item><term>命令_跑到</term><description>i32 断点数 + 每个 i32 断点地址 · i64 步数上限 · bool 等输入 · bool 要记账</description></item>
    /// <item><term>命令_读内存</term><description>(无)</description></item>
    /// <item><term>命令_中断请求 / 命令_取中断请求 / 命令_清除中断请求 / 命令_清除等待输入 / 命令_结束</term>
    /// <description>(无)</description></item>
    /// <item><term>命令_输入应答</term><description>u8 种类(0=有字节,后面跟 u8 字节;1=没有输入 / 前端走了)</description></item>
    /// <item><term>命令_图形事件</term><description>i32 类型 · i32 键码 · i32 x · i32 y</description></item>
    /// <item><term>命令_图形回执</term><description>u32 序号 · u8 结果(0=好 / 1=前端画不了) · 字符串 说明</description></item>
    /// <item><term>事件_问候</term><description>i32 本端协议版本 · 字符串 本端说明(`构建=…` 那一行,出问题时给人看)</description></item>
    /// <item><term>事件_输出</term><description>块 输出字节(UTF-8,一个程序输出的原始字节)</description></item>
    /// <item><term>事件_请求输入</term><description>(无;前端收到就调自己的 `输入源`,再把结果当 `命令_输入应答` 发回来)</description></item>
    /// <item><term>事件_内存增量</term><description>i32 段数 + 每段(i32 起址 · 块 字节)</description></item>
    /// <item><term>事件_图形命令</term><description>u32 序号 · u8 种类(1=打开窗口:字符串标题 + i32 宽 + i32 高;2=关闭窗口;3=呈现:i32 条数 + 每条的 `绘制命令C` 字段(动作 / 八个整数 / 文本 / 颜色 / 像素格数 + 每格 u32,没有像素写 −1);4=等待:i32 毫秒)</description></item>
    /// <item><term>事件_状态</term><description>见 `跑程序状态C`(结果码 · 停下原因 · PC · 16 个寄存器 · 栈指针 · 状态标志 ·
    /// 四个旗标 · 上一条指令地址 · 已执行条数 · 说明 · 源码行 · 位置 · 有中断请求 · 记账)</description></item>
    /// <item><term>事件_致命错误</term><description>字符串 说明(子进程要收摊了;前端据此记 `死因`)</description></item>
    /// </list></para>
    /// </summary>
    public static class 跑程序协议C
    {
        /// <summary>
        /// 协议版本。**两边必须用同一份这个文件**(它跟着同一个仓库走)——不一样就当场说"版本对不上",
        /// 不许硬接着跑(载荷布局一改,硬跑只会读出乱七八糟的东西)。
        /// 改了任何一条载荷布局/消息号,把这个数 +1。
        /// 2 = H39-2:状态帧带上"记账"(调用栈两张表 + 尾部轨迹),装载 / 单步 / 跑到 多带一个"要不要记账"。
        /// 3 = H39-3:图形命令带**序号** + 新一条"图形回执"帧(前端那一侧画成了没有,逐条回话);
        ///     装载多带一份"事件脚本文本"(脚本在子进程那一侧按帧喂,见 `事件脚本C`)。
        /// 4 = H13:新一条"重载"命令 + 新一条"重载回执"事件(改源码不重启程序,把新机器码换进去)。
        /// 5 = 控 1-⑥(2026-10-11 第414批):绘制命令那一格**多带一份像素**
        ///     (`贴位图` 那条命令的整块 `0xAARRGGBB`;别的条写 `-1`)—— 老载荷没有这一段,
        ///     两边不同版硬读会把后面的字节读偏,所以版本号必须跟着走。
        /// </summary>
        public const int 协议版本 = 5;

        /// <summary> 服务模式的命令行开关(前缀里那个 `-` 是给用户看的;`hbcc` / `HB宿主` 认它就读协议、跑服务)</summary>
        public const string 服务参数 = "--跑程序服务";

        /// <summary> 一帧的载荷上限(64 MB):防止一条坏帧(比如把机器码当长度读)把内存吃光</summary>
        public const int 最大载荷字节数 = 64 * 1024 * 1024;

        // ---------------- 前端 → 后端 ----------------
        public const byte 命令_问候 = 1;
        public const byte 命令_装载 = 2;
        public const byte 命令_单步 = 3;
        public const byte 命令_跑到 = 4;
        public const byte 命令_读内存 = 5;
        public const byte 命令_中断请求 = 6;
        public const byte 命令_取中断请求 = 7;
        public const byte 命令_清除中断请求 = 8;
        public const byte 命令_清除等待输入 = 9;
        public const byte 命令_输入应答 = 10;
        public const byte 命令_图形事件 = 11;
        public const byte 命令_结束 = 12;

        /// <summary>
        /// H39-3:**图形回执**(前端 → 后端)—— 每一条图形命令的"画成了没有"。
        ///
        /// 为什么要有它:H39-2 那条路只发不收,前端画不出来(呈现口抛了 / 根本没挂)时子进程还以为画成了;
        /// 有了回执,`图形宿主C.处理` 那条兜底 catch 才真的能把 `状态_开不了窗口` 交回给程序
        /// —— 与进程内"呈现口抛异常"**同一个出口**。
        ///
        /// **由读帧线程收**(不是命令队列):发图形命令的那条线程正卡在"等这一条回执"里,塞队列会自己锁死自己。
        /// </summary>
        public const byte 命令_图形回执 = 13;

        /// <summary>
        /// H13:**热重载**(前端 → 后端)—— 把刚编出来的新机器码换进**正在跑的那一台**虚拟机。
        ///
        /// 载荷 = 新机器码 + 新初值表;后端判"哪些格能保"(判据只有一处:`虚拟机C.重载`),
        /// 判完回一条 `事件_重载`(载荷 = 迁移表 + 说明)。前端照那份迁移表把**自己这一份镜像**重摆一遍
        /// —— 于是两边一模一样,不是各判一遍。
        /// </summary>
        public const byte 命令_重载 = 14;

        // ---------------- 后端 → 前端 ----------------
        public const byte 事件_问候 = 101;
        public const byte 事件_输出 = 102;
        public const byte 事件_请求输入 = 103;
        public const byte 事件_内存增量 = 104;
        public const byte 事件_图形命令 = 105;
        public const byte 事件_状态 = 106;
        public const byte 事件_致命错误 = 107;

        /// <summary>
        /// H13:**重载回执**(后端 → 前端)—— "新机器码换好了,这是保住的东西"。
        ///
        /// 载荷 = 迁移表(`写迁移`)+ 说明文本。前端拿到它就把镜像按同一份迁移重摆
        /// (与 `命令_图形回执` 同一个理由:**不由读帧线程等** —— 发重载的那条线程正卡在等它)。
        /// </summary>
        public const byte 事件_重载 = 108;

        // ---------------- 状态帧的结果码 ----------------
        /// <summary> 命令做完了(程序跑没跑完看旗标与停下原因)</summary>
        public const byte 结果_正常 = 0;
        /// <summary> HB 程序的运行期错误(说明 / 源码行 / 位置都带回来了,前端照原样抛 `运行期错误C`)</summary>
        public const byte 结果_运行期错误 = 1;
        /// <summary> 子进程自己出的事(不是用户程序写错):说明里是异常原文</summary>
        public const byte 结果_内部错误 = 2;

        // ---------------- 输入应答的种类 ----------------
        public const byte 输入_有字节 = 0;
        /// <summary> 前端这会儿没有输入(IDE 里"程序停下来等你敲")/ 前端已经走了 ⇒ 子进程按"没有输入了"处理</summary>
        public const byte 输入_没有 = 1;

        // ---------------- 图形回执的结果 ----------------
        /// <summary> 前端那一侧把这一条图形命令做成了</summary>
        public const byte 图形回执_好 = 0;
        /// <summary> 前端那一侧的呈现口抛了 / 根本没挂呈现口 / 等超时 ⇒ 子进程让程序拿到 `状态_开不了窗口`</summary>
        public const byte 图形回执_失败 = 1;

        // ---------------- 图形命令的种类 ----------------
        public const byte 图形_打开窗口 = 1;
        public const byte 图形_关闭窗口 = 2;
        public const byte 图形_呈现 = 3;
        public const byte 图形_等待 = 4;

        /// <summary> 状态帧里"这条命令不带停下原因"(装载 / 单步 / 读内存 / 取中断请求这些命令)</summary>
        public const byte 停_没有 = 255;

        // ===================================================================
        // 一、帧(写一帧 / 读一帧)
        // ===================================================================

        /// <summary>
        /// 写一帧:先算长度,再 `u32 长度 + u8 消息号 + 载荷`,写完 `Flush()`。
        /// **整个"写"在 `写锁` 里**(多线程写同一条管道会把帧搅在一起)。
        /// `写载荷` 可以传 null(= 这条消息没有载荷)。
        /// </summary>
        public static void 写帧(Stream 出, object 写锁, byte 消息号, Action<BinaryWriter>? 写载荷 = null)
        {
            ArgumentNullException.ThrowIfNull(出);
            ArgumentNullException.ThrowIfNull(写锁);

            byte[] 载荷;
            using (var 缓 = new MemoryStream())
            {
                using (var 写 = new BinaryWriter(缓, new UTF8Encoding(false), leaveOpen: true))
                    写载荷?.Invoke(写);
                载荷 = 缓.ToArray();
            }
            if (载荷.Length > 最大载荷字节数)
                throw new 跑程序通道错误C($"一帧的载荷 {载荷.Length} 字节超过了上限 {最大载荷字节数}(装载的机器码太大?这条通道是给『跑程序』用的,别把整份工程塞进来)");

            uint 长 = (uint)(1 + 载荷.Length);
            lock (写锁)
            {
                Span<byte> 头 = stackalloc byte[5];
                头[0] = (byte)(长 & 0xFF);
                头[1] = (byte)((长 >> 8) & 0xFF);
                头[2] = (byte)((长 >> 16) & 0xFF);
                头[3] = (byte)((长 >> 24) & 0xFF);
                头[4] = 消息号;
                出.Write(头);
                if (载荷.Length > 0) 出.Write(载荷, 0, 载荷.Length);
                出.Flush();
            }
        }

        /// <summary>
        /// 读一帧。返回 null = **对面走了**(管道读到末尾)—— 调用方要如实当成"前端/后端没了",
        /// 不许当成"命令做完了"。半路断掉(读了一半 EOF)与长度不像话都当通道错误抛出。
        /// </summary>
        public static (byte 消息号, byte[] 载荷)? 读帧(Stream 入)
        {
            ArgumentNullException.ThrowIfNull(入);
            Span<byte> 头 = stackalloc byte[4];
            if (!读满(入, 头)) return null;
            uint 长 = (uint)(头[0] | (头[1] << 8) | (头[2] << 16) | (头[3] << 24));
            if (长 == 0 || 长 > 最大载荷字节数 + 1)
                throw new 跑程序通道错误C($"帧长度 {长} 不像话(两边用的必须是同一份 `跑程序协议C`;协议版本对不上的典型症状)");
            byte[] 整帧 = new byte[长];
            if (!读满(入, 整帧))
                throw new 跑程序通道错误C($"读到一半对面就没了(这一帧 {长} 字节只到一半)—— 子进程多半是崩了");
            byte[] 载荷 = 长 == 1 ? [] : 整帧[1..];
            return (整帧[0], 载荷);
        }

        /// <summary> 读满这么多字节;第一字节就 EOF → false(对面体面地走了),读到一半 EOF → false 也由调用方判 </summary>
        private static bool 读满(Stream 入, Span<byte> 缓冲)
        {
            int 已读 = 0;
            while (已读 < 缓冲.Length)
            {
                int 这次 = 入.Read(缓冲[已读..]);
                if (这次 <= 0) return false;
                已读 += 这次;
            }
            return true;
        }

        /// <summary> 把一帧的载荷包成一个 `BinaryReader`(用 `using` 收掉) </summary>
        public static BinaryReader 开读(byte[] 载荷) =>
            new(new MemoryStream(载荷, writable: false), new UTF8Encoding(false), leaveOpen: false);

        /// <summary> 块 = `u32 长度 + 字节`(大块字节一律这么传:机器码 / 输出 / 内存增量的一段) </summary>
        public static void 写块(BinaryWriter 写, byte[] 字节)
        {
            写.Write(字节.Length);
            写.Write(字节);
        }

        /// <summary> 读一块(长度不像话就当场报通道错误,别把它当字节数去开数组) </summary>
        public static byte[] 读块(BinaryReader 读)
        {
            int 长 = 读.ReadInt32();
            if (长 < 0 || 长 > 最大载荷字节数)
                throw new 跑程序通道错误C($"块长度 {长} 不像话(协议版本对不上 / 帧被读偏了)");
            byte[] 字节 = 读.ReadBytes(长);
            if (字节.Length != 长) throw new 跑程序通道错误C($"块说好 {长} 字节,只读到 {字节.Length} 字节");
            return 字节;
        }

        // ===================================================================
        // 二、各类载荷(装载用的那几张表 / 状态 / 图形 / 内存增量)
        // ===================================================================

        /// <summary> 顶层常量初值表(地址 + 宽度 + 值;装载时落进数据段,见 `程序包C.应用初值`) </summary>
        public static void 写初值表(BinaryWriter 写, IReadOnlyList<初值项C> 表)
        {
            写.Write(表.Count);
            foreach (var 项 in 表)
            {
                写.Write(项.地址);
                写.Write(项.宽度);
                写.Write(项.值);
            }
        }

        /// <inheritdoc cref="写初值表"/>
        public static List<初值项C> 读初值表(BinaryReader 读)
        {
            int 条数 = 读.ReadInt32();
            if (条数 < 0 || 条数 > 最大载荷字节数) throw new 跑程序通道错误C($"初值表条数 {条数} 不像话");
            var 表 = new List<初值项C>(条数);
            for (int i = 0; i < 条数; i++) 表.Add(new 初值项C(读.ReadInt32(), 读.ReadInt32(), 读.ReadUInt32()));
            return 表;
        }

        /// <summary>
        /// H13:热重载的**迁移表**(后端判好、前端照贴)。写的是"逐段搬哪些格" + "旧那一份的初值表" + 说明。
        /// 前端拿到就把自己那份镜像按同一份迁移重摆 —— 判据只有后端一处,不是两边各判一遍。
        /// </summary>
        public static void 写迁移(BinaryWriter 写, 虚拟机C.重载迁移C 迁移)
        {
            ArgumentNullException.ThrowIfNull(迁移);
            写.Write(迁移.段.Count);
            foreach (var (旧起, 新起, 大小) in 迁移.段)
            {
                写.Write(旧起);
                写.Write(新起);
                写.Write(大小);
            }
            写初值表(写, 迁移.旧初值表);
            写.Write(迁移.说明.Count);
            foreach (string 一条 in 迁移.说明) 写.Write(一条 ?? "");
        }

        /// <inheritdoc cref="写迁移"/>
        public static 虚拟机C.重载迁移C 读迁移(BinaryReader 读)
        {
            var 迁移 = new 虚拟机C.重载迁移C();
            int 段数 = 读.ReadInt32();
            if (段数 < 0 || 段数 > 1_000_000) throw new 跑程序通道错误C($"重载迁移段数 {段数} 不像话");
            for (int i = 0; i < 段数; i++) 迁移.段.Add((读.ReadInt32(), 读.ReadInt32(), 读.ReadInt32()));
            foreach (var 项 in 读初值表(读)) 迁移.旧初值表.Add(项);
            int 说明数 = 读.ReadInt32();
            if (说明数 < 0 || 说明数 > 100_000) throw new 跑程序通道错误C($"重载说明条数 {说明数} 不像话");
            for (int i = 0; i < 说明数; i++) 迁移.说明.Add(读.ReadString());
            return 迁移;
        }

        /// <summary>
        /// H13:热重载要判"哪些全局 / 顶层变量槽能保" —— 名字表整份太大(函数范围 / 可见性那一堆过线没用),
        /// 只带**用得着的那几格**:槽标签 / 宽度 / 名字(全是 `是全局` 的那些)。
        /// 口径与 `虚拟机C.重载` 的 `全局槽(旧名字表)` 一致:这一份就是它过滤之后的结果。
        /// </summary>
        public static void 写全局槽(BinaryWriter 写, 名字表C? 表)
        {
            var 条们 = new List<名字项C>();
            foreach (var 项 in 表?.变量 ?? [])
                if (项.是全局 && 项.槽.Length > 0) 条们.Add(项);
            写.Write(条们.Count);
            foreach (var 项 in 条们)
            {
                写.Write(项.槽);
                写.Write(项.宽度);
                写.Write(项.名字 ?? "");
            }
        }

        /// <inheritdoc cref="写全局槽"/>
        public static 名字表C 读全局槽(BinaryReader 读)
        {
            var 表 = new 名字表C();
            if (读.BaseStream.Position >= 读.BaseStream.Length) return 表;   // 老载荷到此为止(协议版本对不上时不当崩)
            int 条数 = 读.ReadInt32();
            if (条数 < 0 || 条数 > 1_000_000) throw new 跑程序通道错误C($"全局槽条数 {条数} 不像话");
            for (int i = 0; i < 条数; i++)
            {
                string 槽 = 读.ReadString();
                int 宽 = 读.ReadInt32();
                string 名 = 读.ReadString();
                表.变量.Add(new 名字项C { 名字 = 名, 槽 = 槽, 宽度 = 宽, 是全局 = true });
            }
            return 表;
        }

        /// <summary> H13:隐藏区表(堆 / 池 / 软栈 / 垫子的**绝对地址 + 大小 + 说明**;重载判"字符串池能不能原地保") </summary>
        public static void 写分区表(BinaryWriter 写, IReadOnlyList<内存分区C>? 表)
        {
            写.Write(表?.Count ?? 0);
            foreach (var 区 in 表 ?? [])
            {
                写.Write(区.区名 ?? "");
                写.Write(区.起始);
                写.Write(区.大小);
                写.Write(区.说明 ?? "");
            }
        }

        /// <inheritdoc cref="写分区表"/>
        public static List<内存分区C> 读分区表(BinaryReader 读)
        {
            var 表 = new List<内存分区C>();
            if (读.BaseStream.Position >= 读.BaseStream.Length) return 表;   // 老载荷到此为止(同上:不当崩)
            int 条数 = 读.ReadInt32();
            if (条数 < 0 || 条数 > 100_000) throw new 跑程序通道错误C($"分区表条数 {条数} 不像话");
            表.Capacity = 条数;
            for (int i = 0; i < 条数; i++)
            {
                string 区名 = 读.ReadString();
                int 起始 = 读.ReadInt32();
                int 大小 = 读.ReadInt32();
                string 说明 = 读.ReadString();
                表.Add(new 内存分区C(区名, 起始, 大小, 说明));
            }
            return 表;
        }

        /// <summary> H13:"槽标签 → 字节地址"表(热重载判"旧那一格排在哪";整份标签表只有这一个去向) </summary>
        public static void 写标签表(BinaryWriter 写, IReadOnlyDictionary<string, int>? 表)
        {
            写.Write(表?.Count ?? 0);
            foreach (var 条 in 表 ?? new Dictionary<string, int>())
            {
                写.Write(条.Key);
                写.Write(条.Value);
            }
        }

        /// <inheritdoc cref="写标签表"/>
        public static Dictionary<string, int> 读标签表(BinaryReader 读)
        {
            var 表 = new Dictionary<string, int>(StringComparer.Ordinal);
            if (读.BaseStream.Position >= 读.BaseStream.Length) return 表;   // 老载荷到此为止
            int 条数 = 读.ReadInt32();
            if (条数 < 0 || 条数 > 1_000_000) throw new 跑程序通道错误C($"标签表条数 {条数} 不像话");
            for (int i = 0; i < 条数; i++) 表[读.ReadString()] = 读.ReadInt32();
            return 表;
        }

        /// <summary> H13:代码段字节数(`汇编调试结果C.代码字节数`)—— 判"变量槽有没有压在代码段里"只有它算得准 </summary>
        public static void 写代码字节数(BinaryWriter 写, int 代码字节数) => 写.Write(代码字节数);

        /// <inheritdoc cref="写代码字节数"/>
        public static int 读代码字节数(BinaryReader 读) =>
            读.BaseStream.Position >= 读.BaseStream.Length ? -1 : 读.ReadInt32();   // 老载荷到此为止

        /// <summary> "地址 → 源码位置"那张表(文件们 + 段们;子进程靠它给运行期报错带文件 + 行号) </summary>
        public static void 写源行表(BinaryWriter 写, 源行表C? 表)
        {
            表 ??= new 源行表C();
            写.Write(表.文件们.Count);
            foreach (string 名 in 表.文件们) 写.Write(名 ?? "");
            写.Write(表.段们.Count);
            foreach (var 段 in 表.段们)
            {
                写.Write(段.起址);
                写.Write(段.条数);
                写.Write(段.行号);
                写.Write(段.文件号);
            }
        }

        /// <inheritdoc cref="写源行表"/>
        public static 源行表C 读源行表(BinaryReader 读)
        {
            var 表 = new 源行表C();
            int 文件数 = 读.ReadInt32();
            if (文件数 < 0 || 文件数 > 最大载荷字节数) throw new 跑程序通道错误C($"源行表文件数 {文件数} 不像话");
            for (int i = 0; i < 文件数; i++) 表.文件们.Add(读.ReadString());
            int 段数 = 读.ReadInt32();
            if (段数 < 0 || 段数 > 最大载荷字节数) throw new 跑程序通道错误C($"源行表段数 {段数} 不像话");
            for (int i = 0; i < 段数; i++)
                表.段们.Add(new 源行表C.段C(读.ReadInt32(), 读.ReadInt32(), 读.ReadInt32(), 读.ReadInt32()));
            return 表;
        }

        /// <summary> 可执行文件白名单(名字 → 绝对路径;空表 = 这一侧的宿主没挂"起进程"能力) </summary>
        public static void 写可执行白名单(BinaryWriter 写, IReadOnlyDictionary<string, string>? 表)
        {
            写.Write(表?.Count ?? 0);
            if (表 == null) return;
            foreach (var 条 in 表)
            {
                写.Write(条.Key);
                写.Write(条.Value);
            }
        }

        /// <inheritdoc cref="写可执行白名单"/>
        public static Dictionary<string, string>? 读可执行白名单(BinaryReader 读)
        {
            int 条数 = 读.ReadInt32();
            if (条数 <= 0) return null;
            if (条数 > 100_000) throw new 跑程序通道错误C($"可执行白名单 {条数} 条不像话");
            var 表 = new Dictionary<string, string>(条数, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < 条数; i++) 表[读.ReadString()] = 读.ReadString();
            return 表;
        }

        /// <summary> 网址白名单(主机 + 端口;空表 = 这一侧的宿主没挂"连网址"能力) </summary>
        public static void 写网址白名单(BinaryWriter 写, 网址白名单C? 表)
        {
            var 项们 = 表?.项表 ?? [];
            写.Write(项们.Count);
            foreach (var 项 in 项们)
            {
                写.Write(项.主机);
                写.Write(项.端口);
            }
        }

        /// <inheritdoc cref="写网址白名单"/>
        public static 网址白名单C? 读网址白名单(BinaryReader 读)
        {
            int 条数 = 读.ReadInt32();
            if (条数 <= 0) return null;
            if (条数 > 100_000) throw new 跑程序通道错误C($"网址白名单 {条数} 条不像话");
            var 行 = new List<string>(条数);
            for (int i = 0; i < 条数; i++)
            {
                string 主机 = 读.ReadString();
                int 端口 = 读.ReadInt32();
                行.Add(端口 == 0 ? 主机 : $"{主机}:{端口}");
            }
            var 表 = 网址白名单C.由文本(行);          // 复用同一个解析器:两边认的写法不会分叉
            return 表.是空的 ? null : 表;
        }

        /// <summary>
        /// 一条绘制命令(按 `绘制命令C` 的**字段序列**,不另立口径):动作 / 八个整数 / 文本 / 颜色 / 像素。
        /// 顺序动一下就是两个格式 —— 要与下面的 `读绘制命令` 一起改。
        ///
        /// ⚠ **像素那一格是控 1-⑥ 补的**(2026-10-11 第414批):`贴位图` 那条命令**带一整块像素**
        /// (在子进程里画的那台虚拟机上,窗口队列里的命令要过这条线回来重放到 IDE 那个窗口)。
        /// 漏掉它 = "命令过线之后变成一张空图",而且**只在子进程那条腿上看得出** ——
        /// 进程内那条腿(自检 / 命令行)一份都不少,查起来极容易怀疑错地方。
        /// 没有像素的命令(清屏 / 画矩形 / 画线 / 画文字)照旧一个字节不写(写 `-1`)。
        /// </summary>
        public static void 写绘制命令(BinaryWriter 写, 绘制命令C 条)
        {
            写.Write(条.动作 ?? "");
            写.Write(条.x);
            写.Write(条.y);
            写.Write(条.宽);
            写.Write(条.高);
            写.Write(条.x2);
            写.Write(条.y2);
            写.Write(条.文本 ?? "");
            写.Write(条.颜色);
            if (条.像素 == null)
            {
                写.Write(-1);
            }
            else
            {
                写.Write(条.像素.Length);
                foreach (var 值 in 条.像素) 写.Write(值);
            }
        }

        /// <inheritdoc cref="写绘制命令"/>
        public static 绘制命令C 读绘制命令(BinaryReader 读)
        {
            var 条 = new 绘制命令C
            {
                动作 = 读.ReadString(),
                x = 读.ReadInt32(),
                y = 读.ReadInt32(),
                宽 = 读.ReadInt32(),
                高 = 读.ReadInt32(),
                x2 = 读.ReadInt32(),
                y2 = 读.ReadInt32(),
                文本 = 读.ReadString(),
                颜色 = 读.ReadUInt32(),
            };
            int 格数 = 读.ReadInt32();
            // 一格 = 4 字节,所以"格数不许超过 载荷上限 ÷ 4"那道闸与别处同口径(读一个不像话的数就当场说)
            if (格数 < -1 || (long)格数 * 4 > 最大载荷字节数)
                throw new 跑程序通道错误C($"一条绘制命令的像素格数 {格数} 不像话");
            if (格数 >= 0)
            {
                var 像素 = new uint[格数];
                for (int i = 0; i < 格数; i++) 像素[i] = 读.ReadUInt32();
                条.像素 = 像素;
            }
            return 条;
        }

        /// <summary> 内存增量:若干段"从哪个地址起、这段字节是什么"(只传变化过的那几段) </summary>
        public static void 写内存增量(BinaryWriter 写, IReadOnlyList<(int 起址, byte[] 字节)> 段们)
        {
            写.Write(段们.Count);
            foreach (var (起址, 字节) in 段们)
            {
                写.Write(起址);
                写块(写, 字节);
            }
        }

        /// <inheritdoc cref="写内存增量"/>
        public static List<(int 起址, byte[] 字节)> 读内存增量(BinaryReader 读)
        {
            int 段数 = 读.ReadInt32();
            if (段数 < 0 || 段数 > 最大载荷字节数) throw new 跑程序通道错误C($"内存增量段数 {段数} 不像话");
            var 段们 = new List<(int, byte[])>(段数);
            for (int i = 0; i < 段数; i++) 段们.Add((读.ReadInt32(), 读块(读)));
            return 段们;
        }

        /// <summary>
        /// **比一次内存,只挑出变过的连续段**(SIMD 分块比 —— 1 MB 逐字节比要好几毫秒,这条路是每一步都可能走的)。
        ///
        /// 为什么按"上一次发出去的镜像"比:前端手里那份就是从那儿来的,只发差量 ⇒ 每步几十字节到几 KB。
        /// 形状对不上(刚设过内存 / 第一趟)时整块都给出去 —— "没发过"就得当"全变了"。
        /// </summary>
        public static List<(int 起址, byte[] 字节)> 比较内存(byte[] 现在, byte[]? 上次)
        {
            ArgumentNullException.ThrowIfNull(现在);
            if (上次 == null || 上次.Length != 现在.Length)
                return 现在.Length == 0 ? [] : [(0, (byte[])现在.Clone())];

            var 段们 = new List<(int, byte[])>();
            int 段起 = -1, 段止 = -1;
            for (int i = 0; i < 现在.Length; )
            {
                int 块 = Math.Min(32, 现在.Length - i);
                int 变起 = -1, 变止 = -1;
                if (块 == 32)
                {
                    // 32 字节一次比:全等就一跳而过(这是"1MB 每步比一遍"还够快的关键)
                    var 左 = Vector256.LoadUnsafe(ref 现在[i], 0);
                    var 右 = Vector256.LoadUnsafe(ref 上次[i], 0);
                    if (Vector256.ExtractMostSignificantBits(Vector256.Equals(左, 右)) != uint.MaxValue)
                    {
                        变起 = i;
                        变止 = i + 32;
                        while (变起 < 变止 && 现在[变起] == 上次[变起]) 变起++;          // 块内再收窄到真变过的那几个字节
                        while (变止 > 变起 && 现在[变止 - 1] == 上次[变止 - 1]) 变止--;
                    }
                }
                else
                {
                    for (int k = i; k < i + 块; k++)
                        if (现在[k] != 上次[k]) { if (变起 < 0) 变起 = k; 变止 = k + 1; }
                }

                if (变起 >= 0)
                {
                    if (段起 < 0) { 段起 = 变起; 段止 = 变止; }
                    else if (变起 <= 段止) 段止 = Math.Max(段止, 变止);                  // 与上一段挨着 ⇒ 合成一段
                    else { 段们.Add((段起, 现在[段起..段止])); 段起 = 变起; 段止 = 变止; }
                }
                i += 块;
            }
            if (段起 >= 0) 段们.Add((段起, 现在[段起..段止]));
            return 段们;
        }

        /// <summary> 把"内存增量"贴到这份内存上(地址越界 = 通道错误,不许悄悄越界写) </summary>
        public static void 贴内存增量(byte[] 内存, IReadOnlyList<(int 起址, byte[] 字节)> 段们)
        {
            ArgumentNullException.ThrowIfNull(内存);
            foreach (var (起址, 字节) in 段们)
            {
                if (起址 < 0 || 起址 + 字节.Length > 内存.Length)
                    throw new 跑程序通道错误C($"内存增量落在 0x{起址:X}..0x{起址 + 字节.Length:X},超出这份内存({内存.Length} 字节)—— 两边装的不是同一台机器");
                Array.Copy(字节, 0, 内存, 起址, 字节.Length);
            }
        }

        // ===================================================================
        // 三、状态帧(每条命令的"结束帧")
        // ===================================================================

        /// <summary>
        /// 一条命令跑完之后,子进程报回来的那一份"整机小快照"(前端拿它更新镜像)。
        /// 字段就是 <see cref="虚拟机C"/> 上那些状态 + 一句说明;`内容` 里没有内存(内存在增量帧里)。
        /// </summary>
        public sealed class 跑程序状态C
        {
            /// <summary> 见 `结果_正常 / 结果_运行期错误 / 结果_内部错误` </summary>
            public byte 结果码;

            /// <summary> 只有"跑到"那条命令会给(`停_没有` = 这条命令不带停下原因)</summary>
            public byte 停下 = 停_没有;

            public uint 程序计数器;
            public uint[] 寄存器 = new uint[寄存器C.数量];
            public uint 栈指针;
            public byte 状态标志;
            public bool 停机;
            public bool 命中断点;
            public bool 已中断;
            public bool 正在等输入;
            public uint 上一条指令地址;
            public uint 已执行条数;

            /// <summary> 运行期错误的说明(或内部错误的异常原文);空串 = 没出错 </summary>
            public string 说明 = "";
            /// <summary> 出错那条指令的源码行(0 = 查不到)</summary>
            public int 源码行;
            /// <summary> 出错处的位置串(`目录.hb 第 12 行`);空 = 查不到 </summary>
            public string 位置 = "";

            /// <summary> 只给"取中断请求"那条命令用:这一趟有没有待消费的停止请求 </summary>
            public bool 有中断请求;

            /// <summary>
            /// H39-2:这一趟的**记账**(调用栈两张表 + 尾部一小段轨迹);null = 这一趟没要记账。
            /// 前端拿它整份复原自己的 `调用栈标记C`,并把轨迹逐条重放进 `寄存器信息管理器C`
            /// (名字表 / 机器码都是前端那份 ⇒ 与进程内那条路同源同口径)。
            /// </summary>
            public 跑程序记账C? 记账;

            /// <summary> 四个旗标压成一个字节(bit0 停机 / bit1 命中断点 / bit2 已中断 / bit3 正在等输入) </summary>
            public byte 旗标
            {
                get => (byte)((停机 ? 1 : 0) | (命中断点 ? 2 : 0) | (已中断 ? 4 : 0) | (正在等输入 ? 8 : 0));
                set
                {
                    停机 = (value & 1) != 0;
                    命中断点 = (value & 2) != 0;
                    已中断 = (value & 4) != 0;
                    正在等输入 = (value & 8) != 0;
                }
            }
        }

        /// <summary>
        /// H39-2:**一趟命令的记账** —— 调试那条腿在"真正执行的那一侧"记下来的两样东西:
        /// ① **调用栈两张表**(调用处地址 / 被调方入口地址,一一对应;前端整份复原进 `调用栈标记C`);
        /// ② **尾部轨迹**:最近若干条(最多 <see cref="轨迹上限"/> 条)执行过的 `(指令地址, 当时的 R15)`
        ///    —— 前端照它逐条 `寄存器信息管理器C.登记(...)` 重放,把"这一格现在装的是谁的值"补回来
        ///    (跑到断点可能一口气跑掉几十万条,不重放的话寄存器说明就停在跑之前那一刻了)。
        ///
        /// **只装"这一条命令里新产生的"**(后端每条命令开头把轨迹清掉):单步那条路只带 1 条,
        /// 跑到那条路最多带满 512 条 —— 于是"每步要小"这条口径不会被记账拖坏。
        /// </summary>
        public sealed class 跑程序记账C
        {
            /// <summary> 还欠着的调用处地址(与 `被调` 一一对应,顺序 = 调用先后)</summary>
            public int[] 调用处 = [];
            /// <summary> 与 `调用处` 一一对应的被调方入口地址 </summary>
            public int[] 被调 = [];
            /// <summary> 尾部轨迹(从旧到新):指令地址 + 执行它那一条时的帧指针(R15) </summary>
            public (int 地址, uint 帧指针)[] 轨迹 = [];
        }

        /// <summary> 一趟命令最多带多少条尾部轨迹(够"跑到断点"之后把最近这些格子的说明补回来) </summary>
        public const int 轨迹上限 = 512;

        /// <summary> 写一份记账 </summary>
        public static void 写记账(BinaryWriter 写, 跑程序记账C 账)
        {
            写.Write(账.调用处.Length);
            foreach (int 一个 in 账.调用处) 写.Write(一个);
            写.Write(账.被调.Length);
            foreach (int 一个 in 账.被调) 写.Write(一个);
            写.Write(账.轨迹.Length);
            foreach (var (地址, 帧指针) in 账.轨迹)
            {
                写.Write(地址);
                写.Write(帧指针);
            }
        }

        /// <inheritdoc cref="写记账"/>
        public static 跑程序记账C 读记账(BinaryReader 读)
        {
            var 账 = new 跑程序记账C();
            int 调用处条数 = 读.ReadInt32();
            if (调用处条数 < 0 || 调用处条数 > 100_000) throw new 跑程序通道错误C($"调用栈 {调用处条数} 层不像话");
            账.调用处 = new int[调用处条数];
            for (int i = 0; i < 调用处条数; i++) 账.调用处[i] = 读.ReadInt32();
            int 被调条数 = 读.ReadInt32();
            if (被调条数 < 0 || 被调条数 > 100_000) throw new 跑程序通道错误C($"调用栈 {被调条数} 层不像话");
            账.被调 = new int[被调条数];
            for (int i = 0; i < 被调条数; i++) 账.被调[i] = 读.ReadInt32();
            int 轨迹条数 = 读.ReadInt32();
            if (轨迹条数 < 0 || 轨迹条数 > 1_000_000) throw new 跑程序通道错误C($"尾部轨迹 {轨迹条数} 条不像话");
            账.轨迹 = new (int, uint)[轨迹条数];
            for (int i = 0; i < 轨迹条数; i++) 账.轨迹[i] = (读.ReadInt32(), 读.ReadUInt32());
            return 账;
        }

        /// <summary> 写一条命令的状态帧 </summary>
        public static void 写状态(BinaryWriter 写, 跑程序状态C 状态)
        {
            写.Write(状态.结果码);
            写.Write(状态.停下);
            写.Write(状态.程序计数器);
            for (int i = 0; i < 寄存器C.数量; i++) 写.Write(i < 状态.寄存器.Length ? 状态.寄存器[i] : 0u);
            写.Write(状态.栈指针);
            写.Write(状态.状态标志);
            写.Write(状态.旗标);
            写.Write(状态.上一条指令地址);
            写.Write(状态.已执行条数);
            写.Write(状态.说明 ?? "");
            写.Write(状态.源码行);
            写.Write(状态.位置 ?? "");
            写.Write(状态.有中断请求);
            写.Write(状态.记账 != null);
            if (状态.记账 != null) 写记账(写, 状态.记账);
        }

        /// <inheritdoc cref="写状态"/>
        public static 跑程序状态C 读状态(BinaryReader 读)
        {
            var 状态 = new 跑程序状态C { 结果码 = 读.ReadByte(), 停下 = 读.ReadByte() };
            状态.程序计数器 = 读.ReadUInt32();
            for (int i = 0; i < 寄存器C.数量; i++) 状态.寄存器[i] = 读.ReadUInt32();
            状态.栈指针 = 读.ReadUInt32();
            状态.状态标志 = 读.ReadByte();
            状态.旗标 = 读.ReadByte();
            状态.上一条指令地址 = 读.ReadUInt32();
            状态.已执行条数 = 读.ReadUInt32();
            状态.说明 = 读.ReadString();
            状态.源码行 = 读.ReadInt32();
            状态.位置 = 读.ReadString();
            状态.有中断请求 = 读.ReadBoolean();
            状态.记账 = 读.ReadBoolean() ? 读记账(读) : null;
            return 状态;
        }
    }
}
