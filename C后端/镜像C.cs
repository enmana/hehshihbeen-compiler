using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// R3-6-2:把一份编译结果写成 **C 解释器核**(`C后端\解释器核.c`)吃的**镜像文件**(HBIMG2)。
    ///
    /// 为什么单开一份镜像,而不是让解释器自己去调编译器:C 解释器是**纯 C** 的 ——
    /// 它不认识 `汇编调试结果C`,也不该认识(换核那件事的前提就是"机器码之外没有 .NET 概念")。
    /// 所以 C# 这一侧负责把"跑一次程序需要的全部东西"摊成一份**自足的二进制文件**:
    /// 机器码 + 代码段之后的非零字节 + 顶层常量初值 + 错误号表 + 源码行表 + 内存大小。
    ///
    /// **格式的唯一事实源是两处对齐的注释**:本类这一段字段表,与 `C后端\解释器核.c` 顶上
    /// 那一段**逐条相同**。改一处必须改另一处 —— 对拍第一遍就会把错抓出来
    /// (魔数 / 版本号 / 条数只要对不上,解释器当场报"镜像读不动")。
    ///
    /// 字段表(小端;`偏移`/`字节` 与 C 侧逐条对齐):
    /// <code>
    ///   偏移   字节  内容
    ///   0      8     魔数 `HBIMG2\0\0`
    ///   8      4     版本号(本版 = 2)
    ///   12     4     内存大小(字节)
    ///   16     4     代码字节数
    ///   20     4     数据字节数(代码段之后那块静态数据段)
    ///   24     4     镜像非零项数 n_img
    ///   28     4     初值表项数 n_init
    ///   32     4     错误表项数 n_err
    ///   36     4     源码行段项数 n_line
    ///   40     4     错误槽地址(0 = 这份程序没有错误槽)
    ///   44     …     代码段字节(代码字节数)
    ///   随后依次(都按上面的条数):
    ///     n_img  组:{ u32 地址; u8 值 }                       —— 代码段之后那些非零字节
    ///     n_init 组:{ u32 地址; u32 宽度; u32 值 }             —— 顶层常量初值
    ///     n_err  组:{ u32 错误号; u32 文本字节数; UTF-8 文本 } —— 错误号 → 说明(不带结尾 0)
    ///     n_line 组:{ u32 地址; u32 源码行 }                   —— 地址 ≥ 它就算这一行
    /// </code>
    ///
    /// 几处与 `转译` **同口径**(两边必须一致,不然同一个工程两条路跑出来不一样):
    ///   · 只有前 `代码字节数` 字节是指令,后面是零初始化的静态数据段 ⇒ 镜像里只带**非零**那几个字节;
    ///   · 初值表照原样带(装载时写进内存,在机器码之后);
    ///   · 源码行表按 `执行行映射C.查源行` 逐条指令查、**连续同号合成一段**;
    ///   · 错误槽地址 = 第一份运行期报错表里那个槽标签的地址(取不到就是 0 = 没有错误槽)。
    ///
    /// 产出**逐字节可复现**(不带时间戳、不带路径)。
    /// </summary>
    public static class 镜像C
    {
        /// <summary>镜像格式版本(与 `C后端\解释器核.c` 的 `HB_IMG_VERSION` 同一个数)</summary>
        public const uint 版本 = 2;

        /// <summary>魔数(8 字节;C 侧逐字节比)</summary>
        public static readonly byte[] 魔数 = [(byte)'H', (byte)'B', (byte)'I', (byte)'M', (byte)'G', (byte)'2', 0, 0];

        /// <summary>写出镜像文件(目录要已经存在;覆盖写)</summary>
        public static void 写(汇编调试结果C 编译, int 内存大小, string 路径)
        {
            byte[] 文 = 造(编译, 内存大小);
            File.WriteAllBytes(路径, 文);
        }

        /// <summary>造出镜像的字节(与 <see cref="写"/> 同一份内容;自检拿它比"两次造出来一样")</summary>
        public static byte[] 造(汇编调试结果C 编译, int 内存大小)
        {
            ArgumentNullException.ThrowIfNull(编译);
            byte[] 码 = 编译.机器码;
            if (码.Length == 0)
                throw new C后端错误C("这份程序没有代码段(机器码是空的),没法写镜像 —— 检查工程里是不是没有主程序。");
            // `机器码` 是**整块内存映像**:前 `代码字节数` 字节是指令,后面是零初始化的静态数据段
            // (池 / 软栈 / 堆那些隐藏区都在里面)。解释器那边用 calloc 兜住零,所以只带非零字节。
            int 代码长 = 编译.代码字节数 > 0 && 编译.代码字节数 <= 码.Length ? 编译.代码字节数 : 码.Length;
            if (代码长 % 4 != 0)
                throw new C后端错误C($"代码段长度 {代码长} 不是 4 的倍数(指令定长 4 字节)—— 这份编译结果不完整,请重新编译。");
            if (内存大小 < 4)
                throw new C后端错误C($"内存大小 {内存大小} 字节连一条指令都装不下 —— 检查工程文件里 `内存` 那一格。");
            if (代码长 > 内存大小)
                throw new C后端错误C($"机器码 {代码长} 字节装不进 {内存大小} 字节的运行内存 —— 检查工程文件里 `内存` 那一格。");

            var 镜像非零 = new List<(int 地址, byte 值)>();
            for (int i = 代码长; i < 码.Length; i++)
                if (码[i] != 0) 镜像非零.Add((i, 码[i]));

            uint 槽地址 = 0;
            var 错误 = new List<(int 号, string 说明)>();
            foreach (var (槽标签, 表) in 编译.运行期报错)
            {
                if (槽地址 == 0 && 编译.标签表.TryGetValue(槽标签, out int 地址)) 槽地址 = (uint)地址;
                foreach (var (号, 说明) in 表.OrderBy(x => x.号)) 错误.Add((号, 说明));
            }

            var 行段 = new List<(int 地址, int 行)>();
            for (int i = 0; i < 代码长 / 4; i++)
            {
                int 行 = 执行行映射C.查源行(编译.调试文件表, (uint)(i * 4));
                if (行段.Count == 0 || 行段[^1].行 != 行) 行段.Add((i * 4, 行));
            }
            if (行段.Count == 0) 行段.Add((0, 0));      // 理论上到不了(上面已经拦了空代码段),兜一条免得写出 0 段

            using var 流 = new MemoryStream();
            using (var 写 = new BinaryWriter(流, new UTF8Encoding(false), leaveOpen: true))
            {
                写.Write(魔数);
                写.Write(版本);
                写.Write((uint)内存大小);
                写.Write((uint)代码长);
                写.Write((uint)Math.Max(0, 编译.数据字节数));
                写.Write((uint)镜像非零.Count);
                写.Write((uint)编译.初值表.Count);
                写.Write((uint)错误.Count);
                写.Write((uint)行段.Count);
                写.Write(槽地址);
                写.Write(码, 0, 代码长);
                foreach (var (地址, 值) in 镜像非零) { 写.Write((uint)地址); 写.Write(值); }
                foreach (var 项 in 编译.初值表) { 写.Write((uint)项.地址); 写.Write((uint)项.宽度); 写.Write(项.值); }
                foreach (var (号, 说明) in 错误)
                {
                    byte[] 文 = Encoding.UTF8.GetBytes(说明);
                    写.Write((uint)号);
                    写.Write((uint)文.Length);
                    写.Write(文);
                }
                foreach (var (地址, 行) in 行段) { 写.Write((uint)地址); 写.Write((uint)行); }
            }
            return 流.ToArray();
        }
    }
}
