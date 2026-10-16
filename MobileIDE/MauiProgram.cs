using Microsoft.Extensions.Logging;
#if ANDROID
using 手机IDE.Platforms.Android;   // H92-4:`Android系统目录` 在这个子名字空间里 —— C# 不会往子名字空间里找,不 using 就 CS0246
#endif

namespace 手机IDE;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// H64:把"挑系统目录"这一格接上 —— Android 走 SAF(`ACTION_OPEN_DOCUMENT_TREE`),
		// 别的端如实挂一份"没有"。界面(「打开工程目录…」)只问 `系统目录口C.口`,不判平台。
#if ANDROID
		系统目录口C.挂上(new Android系统目录());
#else
		系统目录口C.挂上(new 无系统目录());
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
