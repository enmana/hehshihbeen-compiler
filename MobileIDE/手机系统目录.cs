namespace 手机IDE;

/// <summary> 用户挑中的**系统目录**(H64):它是 `content://` 树,不是文件系统路径 </summary>
/// <param name="虚拟路径">给引擎看的抽象路径(引擎 `虚拟路径C` 定的那套:`内容://` 加树标识再加相对路径)</param>
/// <param name="名字">给用户看的名字(系统给的显示名,例如"下载")</param>
/// <param name="文档地址">`content://` 树地址(平台适配层用它认树;界面不解释它)</param>
public sealed record 系统目录C(string 虚拟路径, string 名字, string 文档地址);

/// <summary>
/// H64:**"挑一个系统目录"这一格的插口**。手机端真正去弹系统的目录选择器
/// (Android = SAF 的 `ACTION_OPEN_DOCUMENT_TREE`)这件事是**平台特有**的,
/// 所以它落在平台那一侧(`Platforms\Android\Android系统目录.cs`),这里只留接口 + 注册口子;
/// 别的平台(Windows 那一端)不注册 ⇒ 按钮照旧走沙箱目录,与以前一模一样。
/// </summary>
public interface 系统目录选择器C
{
    /// <summary> 这个平台能不能挑系统目录(Android 真机/模拟器 = 能) </summary>
    bool 可用 { get; }

    /// <summary> 弹系统目录选择器;用户取消 / 平台不支持 ⇒ null </summary>
    Task<系统目录C?> 选目录(string 标题);
}

/// <summary> 平台侧的注册口子(单例静态:整个 App 只有一个) </summary>
public static class 系统目录口C
{
    private static 系统目录选择器C? _口;

    /// <summary> 这一端挂着的实现(null = 没有:界面该如实说一句,别假装用户取消了)</summary>
    public static 系统目录选择器C? 口 => _口;

    /// <summary> 挂上 / 摘掉(以后别的平台要接自己的选择器,就在它自己的平台文件里挂) </summary>
    public static void 挂上(系统目录选择器C? 口) => _口 = 口;
}
