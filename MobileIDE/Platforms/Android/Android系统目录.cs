using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;
using hehshihbeen_compiler;
using URI = Android.Net.Uri;

namespace 手机IDE.Platforms.Android;

/// <summary>
/// H64:Android 上的**系统目录**这一格 = SAF(`ACTION_OPEN_DOCUMENT_TREE`)。
///
/// 拿回来的是 `content://` 树(不是文件系统路径),所以:
///   · 挑目录这一手在本文件里(平台特有);
///   · 挑回来的树交给 <see cref="内容文件实现C"/> —— 引擎的 `文件接口C` 的一份实现,
///     把"路径"当"这棵树里的相对路径"去走 `DocumentFile`;
///   · 挂 / 摘由 `手机选择器` 管(见那一份的字注释)。
///
/// 权限:`TakePersistableUriPermission` 拿的是**跨重启**的读**写**授权(只给读的话,
/// 用户在里面改完代码按「保存」会失败 —— 那就成了半成品,见硬规则 4)。
/// </summary>
public sealed class Android系统目录 : 系统目录选择器C
{
    private const int 选目录请求码 = 7301;
    private TaskCompletionSource<系统目录C?>? _待办;

    public bool 可用 => true;

    public Task<系统目录C?> 选目录(string 标题)
    {
        var 活动 = Platform.CurrentActivity;
        if (活动 == null) return Task.FromResult<系统目录C?>(null);
        // 只留一个待办:上一次没回来又点一次的话,把上一次如实收成"取消"(不留悬挂的 Task)
        _待办?.TrySetResult(null);
        var 待办 = new TaskCompletionSource<系统目录C?>();
        _待办 = 待办;
        try
        {
            var 意图 = new Intent(Intent.ActionOpenDocumentTree);
            意图.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission
                | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
            活动.StartActivityForResult(意图, 选目录请求码);
        }
        catch (Exception 错)
        {
            _待办 = null;
            待办.TrySetResult(null);
            System.Diagnostics.Debug.WriteLine($"打开系统目录选择器失败:{错.Message}");
        }
        return 待办.Task;
    }

    /// <summary> MAUI 会把每个 Activity 的结果广播到这里(接线见 `MainActivity.OnActivityResult`) </summary>
    public static void 收结果(int 请求码, Result 结果码, Intent? 数据)
    {
        if (请求码 != 选目录请求码) return;
        Android系统目录? 这一格 = 口;
        var 待办 = 这一格?._待办;
        if (这一格 != null) 这一格._待办 = null;
        if (待办 == null) return;

        var 地址 = 数据?.Data;
        if (结果码 != Result.Ok || 地址 == null)
        {
            待办.TrySetResult(null);                       // 用户按了返回 / 取消了
            return;
        }
        try
        {
            var 活动 = Platform.CurrentActivity;
            // 跨重启的读写授权(不拿的话:重启一次这个目录就"打不开"了)
            活动?.ContentResolver?.TakePersistableUriPermission(地址,
                ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            // 虚拟路径的根由平台这一层算出来(`内容文件实现C` 的规则),引擎那边只看到它
            待办.TrySetResult(new 系统目录C(
                内容文件实现C.根路径算(地址),
                显示名(活动, 地址),
                地址.ToString()!));
        }
        catch (Exception 错)
        {
            System.Diagnostics.Debug.WriteLine($"取系统目录授权失败:{错.Message}");
            待办.TrySetResult(null);
        }
    }

    /// <summary> 平台这一端挂着的实现(整个 App 一个) </summary>
    private static Android系统目录? 口 => 系统目录口C.口 as Android系统目录;

    /// <summary> 这一棵树的显示名(系统给的;取不到就落一句通用的) </summary>
    private static string 显示名(Activity? 活动, URI 地址)
    {
        try
        {
            if (活动?.ContentResolver == null) return "系统目录";
            using var 游标 = 活动.ContentResolver.Query(地址, null, null, null, null);
            if (游标 != null && 游标.MoveToFirst())
            {
                int 列 = 游标.GetColumnIndex(DocumentsContract.Document.ColumnDisplayName);
                if (列 >= 0)
                {
                    string? 名 = 游标.GetString(列);
                    if (!string.IsNullOrWhiteSpace(名)) return 名!;
                }
            }
        }
        catch { /* 取不到名字不是错:下面给通用名 */ }
        return "系统目录";
    }
}
