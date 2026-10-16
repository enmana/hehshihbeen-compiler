namespace 手机IDE;

/// <summary> 没有系统目录选择器的那几端(iOS / MacCatalyst / Windows 上跑的手机 IDE):如实说"没有" </summary>
internal sealed class 无系统目录 : 系统目录选择器C
{
    public bool 可用 => false;

    public Task<系统目录C?> 选目录(string 标题) => Task.FromResult<系统目录C?>(null);
}
