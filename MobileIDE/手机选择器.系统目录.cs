using hehshihbeen_compiler;

#if !ANDROID
namespace 手机IDE;

/// <summary>
/// H64:没有 SAF 的那几端(iOS / MacCatalyst / Windows 上跑的手机 IDE)的**空实现** ——
/// 系统目录那一格在这一端就是"没有"(`系统目录口C.口` 那一份 `可用` 为假),界面接着走沙箱工程目录,
/// 与 H64 之前一模一样。**接口在、行为空**:加了 H64 之后这一端也不该多出"挑系统目录"的假按钮。
/// Android 那一份在 `Platforms\Android\手机选择器.系统目录.cs`(平台文件夹只在该平台编)。
/// </summary>
internal static partial class 手机选择器
{
    /// <summary> 这一端没有系统目录:如实返回 null(调用方接着走沙箱那一档)</summary>
    public static string? 挂系统目录(系统目录C 目录) => null;

    /// <summary> 这一端本来就没挂过,摘也只是个空动作 </summary>
    public static void 摘系统目录() { }

    /// <summary> 这一端不会有系统目录路径(虚拟路径是 SAF 那一档才有的) </summary>
    public static bool 是系统目录路径(string? 路径) => 虚拟路径C.是虚拟(路径);
}
#endif
