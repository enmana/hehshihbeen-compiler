using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace 手机IDE.Platforms.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// H64:系统目录选择器(SAF)的结果从这里回给 `Android系统目录`。
    /// MAUI 自己也要看这个回调(它靠 `Platform.OnActivityResult` 分发),所以**先转给平台那一层**,
    /// 再问我们自己那一格 —— 顺序反了会把 MAUI 那边的回调吞掉。
    /// </summary>
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        Android系统目录.收结果(requestCode, resultCode, data);
    }
}
