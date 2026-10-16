using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **"这个 `.hb` 是源码还是机器码"的唯一判据**(2026-10-13 第434批)。
    ///
    /// <para>
    /// 为什么要有它:IDE 的「打包方式 → 只编译」(宿主那条腿)把产物落在
    /// `&lt;项目目录&gt;\&lt;工程名&gt;.hb`,而 `.hb` 正是源码的后缀 —— 于是**产物躺在源码树里**,
    /// 谁按 `*.hb` 枚举"源码",谁就会把它算进去、甚至拿它当源码去编译。
    /// </para>
    /// <para>
    /// **实测(第434批)**:把一个带 HB 头的二进制放进 `Sample\中间层\示例63_新指令演示\`,
    /// 全量自检当场红**四条**,没有一条与"编译器算错了"有关 ——
    /// `文档数字`(标准工程 .hb 数 161 → 162,与盘上那段对不上)、
    /// `示例回归`(它多出一次"整文件不可运行"的跳过)、`自检 跳过数护栏`(那条跳过不在允许的三族里)、
    /// `中英切换`(拿二进制当源码切英文再编译 ⇒ 编译不过)。
    /// </para>
    /// <para>
    /// **判据**:HB 机器码的头两字节是 `48 42`(`hbcc -o` / `hbcc --打包 只编译` / IDE `只编译` /
    /// 宿主贴尾巴之前的那一段,一律是 `48 42 01 00` + 机器码)。**源码是文本**,开头不可能是这两个字节
    /// (`.hb` 源码以注释、关键字或标识符开头)。这一条与 `MainWindow.窗口与文件.cs` 打开文件时判
    /// "这是不是二进制程序"**同一份口径**,别各写各的。
    /// </para>
    /// </summary>
    public static class 源码文件C
    {
        /// <summary> HB 机器码的头两字节:'H' 'B'(小端,见 `编译器CLI` 的 `-o` 与 `--打包 只编译` 那两段) </summary>
        public const byte 机器码头0 = 0x48, 机器码头1 = 0x42;

        /// <summary> 一段文件头(至少 2 字节)是不是 HB 机器码 </summary>
        public static bool 是机器码头(ReadOnlySpan<byte> 头)
            => 头.Length >= 2 && 头[0] == 机器码头0 && 头[1] == 机器码头1;

        /// <summary>
        /// 这个文件是不是 HB 机器码。**读不到就当"不是"** —— 统计与扫描不因为一个文件整个失败
        /// (口径与 `规模统计C` 那一族一致)。
        /// </summary>
        public static bool 是机器码(string 路径)
        {
            try
            {
                using FileStream 流 = File.OpenRead(路径);
                Span<byte> 头 = stackalloc byte[2];
                return 流.Read(头) == 2 && 是机器码头(头);
            }
            catch { return false; }
        }

        /// <summary> 列一个目录下的 HB 源码(递归;**机器码产物不算** —— 它不是源码) </summary>
        public static string[] 列源码(string 目录)
            => Directory.Exists(目录)
                ? 去掉机器码(Directory.GetFiles(目录, "*.hb", SearchOption.AllDirectories))
                : [];

        /// <summary> 从一份文件清单里剔掉机器码产物(顺序原样保留,调用方原来怎么排的还是怎么排) </summary>
        public static string[] 去掉机器码(IEnumerable<string> 文件们)
            => [.. 文件们.Where(文件 => !是机器码(文件))];
    }
}
