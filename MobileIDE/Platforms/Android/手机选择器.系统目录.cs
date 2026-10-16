using hehshihbeen_compiler;
using 手机IDE.Platforms.Android;

namespace 手机IDE;

/// <summary>
/// H64(Android 那一半):把一棵 SAF 系统目录挂到引擎上(`手机选择器` 的平台分片)。
/// 引擎那一侧只认"虚拟路径",本文件负责"挑回来的这一棵"→"挂在哪一段前缀下"。
/// 别的平台(iOS / MacCatalyst / Windows 那一端)另有一份同名的空实现,界面不必到处判平台。
/// </summary>
internal static partial class 手机选择器
{
    /// <summary>
    /// 把一棵**系统目录**(SAF 树)挂到引擎上:先摘掉上一次挂的那棵,再按前缀把这一棵挂进
    /// `虚拟文件C` —— 于是 `内容://…` 这一族路径(工程文件、源码、程序里的 `读文件`/`列目录` …)
    /// 全走这一份实现,沙箱里那些真路径照旧走真实文件那一份(两边并存)。
    ///
    /// 返回 null = 这一棵树挂不上(没有权限 / 系统那一层出错)—— 界面如实说一句,别默默当"用户取消了"。
    /// </summary>
    public static string? 挂系统目录(系统目录C 目录)
    {
        摘系统目录();
        var 实现 = 内容文件实现C.建(Android.Net.Uri.Parse(目录.文档地址), 目录.名字);
        if (实现 == null) return null;
        前端契约C.服务.宿主.挂上文件实现(new 路径分派表C().挂(实现.根路径, 实现));
        return 实现.根路径;
    }

    /// <summary> 摘掉系统目录这一档(回到沙箱工程目录时调一次:留着会让 `内容://…` 还指向上一棵树) </summary>
    public static void 摘系统目录() => 前端契约C.服务.宿主.摘掉文件实现();

    /// <summary> 这条路径是不是"系统目录那一档"(引擎的 `虚拟路径C` 说了算,界面不自己认前缀) </summary>
    public static bool 是系统目录路径(string? 路径) => 虚拟路径C.是虚拟(路径);
}
