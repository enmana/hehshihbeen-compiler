// ============================================================
// 仓库根C —— **"这份程序住在仓库里吗?住的话根在哪一层"** 的唯一口径(纯函数 + 纯判据,不读别的)。
//
// 为什么要单开一份(H90,2026-10-11 用户当班实测报的缺陷):
//   标准示例里的数据路径**一律照仓库根写** ——
//     `Sample/模型/人头肩/头肩.obj`(示例58)· `Sample/库/排序.hb`(示例27)·
//     `工具/临时/示例30/主文件.txt`(示例30)· `Sample/中间层/示例19_目录遍历`(示例19)……
//   这些路径是**相对当前目录**解出来的,于是"这个进程的当前目录在哪"直接决定示例跑不跑得起来。
//
//   命令行那条腿不踩:`hbcc` 是在仓库根敲的,当前目录本来就是对的。
//   **IDE 那条腿会踩**:程序跑在 `HB宿主.exe` 子进程里,子进程不指定当前目录就**继承界面进程那一份** ——
//   界面是从 `bin\Debug\net10.0-windows\` 起的,于是"盘上明明有这份模型"却报
//   `打不开这个文件(路径对不对?盘上有没有?)`(示例58 就是这么翻的车:模型 424 顶点 / 831 面都在盘上)。
//
// 判据只有一条:**从某一起点往上找"同时有 `引擎` 与 `Sample` 的那一层"**。
//   · 两个目录都在 ⇒ 才认(单看 `Sample` 会认错 —— 别人的仓库里也可能有叫 Sample 的目录);
//   · 找不到 ⇒ `null`,调用方**保持老行为**,不许瞎猜一个目录(发布出去单摆的 IDE 就属于这一档)。
//
// ⚠ 起点要按 `AppContext.BaseDirectory`(**程序自己住哪**),不是 `Environment.CurrentDirectory`
//   (**谁把我起来的**)—— 后者正是这一条缺陷的成因,拿它当起点等于什么也没修。
//   这一条与 R4-11 那笔旧账(自检找仓库文件**不能看当前目录**)同一条道理。
// ============================================================

using System;
using System.IO;

namespace hehshihbeen_compiler
{
    /// <summary> 见文件头:从某个起点目录往上找仓库根(同时有 `引擎` 与 `Sample` 的那一层)。 </summary>
    public static class 仓库根C
    {
        /// <summary> 往上找的最多层数(布局再深一点也够:`仓库\app\bin\Debug\net10.0\` 只用了 4 层) </summary>
        public const int 默认最多往上 = 8;

        /// <summary>
        /// 从 <paramref name="起点"/> 这一层开始**往上**找仓库根;找不到返回 null。
        /// 起点为 null / 空 / 盘上不存在 ⇒ 也返回 null(不抛)。
        /// </summary>
        public static string? 找(string? 起点, int 最多往上 = 默认最多往上)
        {
            if (string.IsNullOrWhiteSpace(起点)) return null;
            DirectoryInfo? 目录;
            try { 目录 = new DirectoryInfo(起点!); } catch { return null; }
            for (int 上 = 0; 上 <= 最多往上 && 目录 != null; 上++)
            {
                try
                {
                    if (Directory.Exists(Path.Combine(目录.FullName, "引擎"))
                        && Directory.Exists(Path.Combine(目录.FullName, "Sample")))
                        return 目录.FullName;
                }
                catch { return null; }
                目录 = 目录.Parent;
            }
            return null;
        }

        /// <summary>
        /// **跑用户程序的那个子进程该站在哪一层** —— 就是仓库根(见文件头:`HB宿主` 也照仓库根解相对路径)。
        /// 找不到返回 null ⇒ 调用方不指定当前目录(继承界面进程的),保持老行为。
        /// </summary>
        public static string? 程序运行目录() => 找(AppContext.BaseDirectory);
    }
}
