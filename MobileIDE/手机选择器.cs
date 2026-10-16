using hehshihbeen_compiler;

namespace 手机IDE;

/// <summary>
/// D4:把**手机系统能提供的东西**接到引擎的 `选择器C` 上。
///
/// · **选文件**:MAUI Essentials 的 `FilePicker`(Android/iOS/Windows 都有)—— 拿回来的是系统给的
///   **临时副本路径**(Android 上是 content:// 拷进应用缓存的那一份),能读、能编译、能按目录解析 `包含`;
///   要长期保存请自己再复制一份到应用目录(这里不替用户搬文件)。手机 IDE 的「保存」写回的就是这个路径
///   (`MainPage.xaml.cs` 的 `保存按钮_Click`),所以"临时副本"那一档改完要留住的,记得先「打开工程目录…」。
///   **请求里 `可多选` 时**走 `PickMultipleAsync`(H9):系统那边一次挑几个,回来一串路径
///   (引擎 `选择结果C.由路径表` 去空白 / 去重后收成 `路径表`)。
/// · **选目录**:两档(H10 / H64)。
///   ① **系统目录**(H64,Android):走 SAF(`ACTION_OPEN_DOCUMENT_TREE`)—— 拿回来的是 `content://` 树,
///      配一份 `内容文件实现C`(引擎 `文件接口C` 的 Android 实现)挂到引擎的 `虚拟文件C` 上,
///      好让工程文件 / 源码 / 程序里的文件能力都认得出这一棵树(见 `挂系统目录`);
///   ② **应用自己的工程目录**:应用私有存储里的一块,真路径、可读可写、不用问权限。
///   界面先试 ①(那一格可用就弹系统选择器),用户取消 / 平台没有这一格再走 ②。
///
/// · **H10:相对路径的基准目录** —— 跑程序之前调 `定基准目录`:`宿主与文件服务C.设基准目录`(全仓唯一的解析处)。
///   手机上没有"敲命令时所在的目录"这一说(安卓的进程工作目录是 `/`,写不进去),于是把基准定成
///   **程序源文件所在的那个目录**;没打开过文件(手写 / 装入示例)就落在应用自己的沙箱工程目录里。
///
/// 判定/文案都在引擎侧(`选择器C.看目录` 之类),所以命令行自检能用**替身**把整条路走通;
/// 这一层只干"调平台 API、把结果翻译成 `选择结果C`"。
/// </summary>
internal static partial class 手机选择器
{
    private static bool _已挂;

    /// <summary> 应用自己的工程目录(第一次用时建出来)</summary>
    public static string 工程根目录
    {
        get
        {
            string 根 = Path.Combine(FileSystem.AppDataDirectory, "工程");
            try { Directory.CreateDirectory(根); } catch { }
            return 根;
        }
    }

    /// <summary>
    /// H10:定下**相对路径的基准目录**(跑程序之前调一次)—— `当前目录` 给空就落回沙箱工程目录。
    /// 与桌面端一个直觉:相对路径是相对**程序源文件所在的那个目录**。
    /// </summary>
    public static void 定基准目录(string? 当前目录) =>
        前端契约C.服务.宿主.设基准目录(string.IsNullOrWhiteSpace(当前目录) ? 工程根目录 : 当前目录);

    /// <summary> 现在挂着系统目录吗(界面提示 / 状态行用它) </summary>
    public static bool 有系统目录 => 前端契约C.服务.宿主.当前文件实现 is 路径分派表C;

    /// <summary> 挂上(重复调用只有第一次有效);挂不上就保持"没挂"(界面会拿到 null 并提示) </summary>
    public static void 挂上()
    {
        if (_已挂) return;
        try
        {
            选择器C.选文件 = async 请求 =>
            {
                string 标题 = string.IsNullOrWhiteSpace(请求?.标题) ? "选一个文件" : 请求.标题;
                // H9:请求里说"可多选"才走多选那一手(系统里多选是另一个 API);没说要就还是一次一个
                if (请求?.可多选 == true)
                {
                    var 选们 = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = 标题 });
                    var 路径表 = (选们 ?? []).Select(选 => 选?.FullPath ?? "").Where(p => p.Length > 0).ToArray();
                    return 路径表.Length > 0
                        ? 选择结果C.由路径表(路径表)
                        : 选择结果C.由取消("系统只给了个内容流、没给可读路径(这个来源挑不了)");
                }
                var 选 = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = 标题,
                });
                if (选 == null) return 选择结果C.由取消();
                string 路径 = 选.FullPath ?? "";
                return 路径.Length == 0
                    ? 选择结果C.由取消("系统只给了个内容流、没给可读路径(这个来源挑不了)")
                    : 选择结果C.由路径表([路径]);
            };
            选择器C.选目录 = async 请求 =>
            {
                // H64:先试**系统目录**(Android 上就是 SAF 那把手);用户取消 / 这一端没这一格 ⇒ 返回 null,
                // 界面接着走"应用自己的工程目录"那一档(与 H64 之前的默认行为一致)
                string 标题 = string.IsNullOrWhiteSpace(请求?.标题) ? "选一个工程目录" : 请求.标题;
                var 口 = 系统目录口C.口;
                if (口 != null && 口.可用)
                {
                    var 挑中 = await 口.选目录(标题);
                    if (挑中 == null) return null;
                    string? 根 = 挂系统目录(挑中);
                    if (根 == null)
                        return 选择结果C.由取消($"这一棵树挂不上(授权被系统收回了?):{挑中.名字}");
                    return new 选择结果C { 路径 = 根, 说明 = $"选了系统目录:{挑中.名字}" };
                }
                return new 选择结果C
                {
                    路径 = 工程根目录,
                    说明 = "用的是应用自己的工程目录(这一端没有系统目录那一档)",
                };
            };
            _已挂 = 选择器C.有系统选择器;
        }
        catch (Exception ex)
        {
            选择器C.清空();
            _已挂 = false;
            System.Diagnostics.Debug.WriteLine($"挂系统选择器失败(照旧能用『装入示例』+ 手写代码):{ex.Message}");
        }
    }
}
