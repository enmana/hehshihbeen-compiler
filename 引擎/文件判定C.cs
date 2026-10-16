using System;
using System.IO;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// H48(第276批):「两个路径是不是同一个文件」这句判据的**唯一一处实现**。
    ///
    /// 以前全仓有 **5 份私有副本**,各认各的(界面 1 + 引擎 4):
    ///   · `引擎\断点表C.cs` 那份**只比原字符串**(相对写法与绝对写法碰头就认不出来);
    ///   · `引擎\编译错误C.cs` 那份把"空 vs 空"判成**不是**同一个;
    ///   · `引擎\转到定义C.cs` 那份**不归绝对路径**(少一条认法);
    ///   · `MainWindow.代码检查.cs` / `引擎\补全名字C.cs` 两份一样,都是"原样 → 绝对路径 → 文件名"。
    /// 于是"同一份文件"在不同功能里认法不一样,查到的东西跟着不一样 —— 最典型:编译结果里的文件名
    /// 可能只有**光名** `主程序.hb`,而标签页给的是**全称**。这里收成一处,**四条按顺序判**:
    ///
    ///   ① **两边都是空串**(单文件编译 / 匿名来源)⇒ 算同一个;一边空一边不空 ⇒ 不算。
    ///      空串的意思是"没有文件名",不是一个真实的文件,只跟另一个"没有文件名"相等。
    ///   ② 原样比,不分大小写(Windows 与安卓的路径都不分大小写)。
    ///   ③ 两边都归成**绝对路径**再比 —— "一边写相对、一边写绝对"靠这一步。
    ///   ④ **只有一边是"光名"(不带目录)时**才退回比文件名:编译结果里的文件名可能只有光名,
    ///      而标签页给的是全称。**两边都带目录却归不到一起的,是两份不同的文件** ——
    ///      不许放宽成"文件名一样就算同一份"(32 个标准工程每份都叫 `主程序.hb`,
    ///      放宽了就会「打开示例02 却切回示例01 那一页」/「断点、未使用诊断张冠李戴」)。
    ///
    /// 纯函数:不读盘、不看当前目录以外的东西,失败(路径不合法)如实退到第 ④ 条,不硬猜。
    /// </summary>
    public static class 文件判定C
    {
        /// <summary> 两个路径是不是同一个文件(四条口径见类注释;空串只跟空串相等) </summary>
        public static bool 同文件(string? 左, string? 右)
        {
            bool 左空 = string.IsNullOrEmpty(左), 右空 = string.IsNullOrEmpty(右);
            if (左空 || 右空) return 左空 && 右空;                                       // ①
            if (string.Equals(左, 右, StringComparison.OrdinalIgnoreCase)) return true;   // ②
            try
            {
                // ③
                if (string.Equals(Path.GetFullPath(左!), Path.GetFullPath(右!), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { /* 路径里有非法字符之类:归不到绝对路径就退到第 ④ 条 */ }
            // ④ 只有一边是光名才比文件名
            if (是光名(左!) == 是光名(右!)) return false;
            return string.Equals(Path.GetFileName(左), Path.GetFileName(右), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary> 光名 = 不带目录的那一种写法(`主程序.hb`);带了目录(`库\目录.hb` / `E:\…`)就不是 </summary>
        private static bool 是光名(string 路径) =>
            路径.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
    }
}
