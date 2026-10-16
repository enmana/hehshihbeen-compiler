// ============================================================
// 综合自检C.剪贴板与弹窗库.cs —— G4 批 23(2026-10-11,第406批)的机械判据
//
//   这一批是**加能力**(宿主功能号 80~85 + 两个库 `Sample\库\剪贴板.hb` / `弹窗.hb`),
//   所以判据钉四件事:
//     ① **剪贴板往返**:写进去 → 读回来**逐字节同**;清掉之后读回空串;
//        ⚠ 跨进程那条腿(clipboard 是**进程级**的)在自检里**没法验** —— 那要真起子进程去抢剪贴板,
//        两条腿会互相踩(本进程刚写、子进程又清)。这里只钉**同进程**这一条,如实记着;
//     ② ⭐ **"这个宿主不弹窗"要给状态 25,不许当"用户取消了"**:
//        这一条是本批最要紧的设计口径 —— 自检与命令行**默认关着那道闸**
//        (`宿主弹窗C.允许弹窗`),一个真模态框会把整趟自检永远挂住(硬规矩 6);
//     ③ **取消与"没有这一手"分得开**:`弹窗.选一个` 返回假时,`用户取消了()` 与 `能弹窗()`
//        给的是**两个不同的问题**的答案(库头注释里写着这条口径);
//     ④ **端到端**:标准工程 `示例59_剪贴板与弹窗` 真编真跑,与它那行 `// 预期输出:` 逐段对。
//
//   ⚠ 夹具:**不依赖剪贴板里原来的内容** —— 每一条都是自己先写再读(别的东西也会动剪贴板)。
// ============================================================
using System;
using System.IO;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        /// <summary> 两个长串"哪一段开始不一样"(报错文案用;与 `消息与状态机库` 那一组同一把尺子)</summary>
        private static string 剪贴板弹窗说明(string 期望, string 实得)
        {
            string[] 期望段 = 期望.Split('|'), 实得段 = 实得.Split('|');
            for (int i = 0; i < Math.Max(期望段.Length, 实得段.Length); i++)
            {
                string a = i < 期望段.Length ? 期望段[i] : "(缺)";
                string b = i < 实得段.Length ? 实得段[i] : "(缺)";
                if (a != b) return $"第 {i + 1} 段不同:期望 `{a}` / 实得 `{b}`";
            }
            return "两串逐段相同(不该走到这儿)";
        }

        private static void 剪贴板与弹窗库自检()
        {
            string? 根 = 找示例目录();
            string 库目录 = 根 == null ? "" : Path.Combine(根, "库");
            string 剪贴板路径 = Path.Combine(库目录, "剪贴板.hb");
            string 弹窗路径 = Path.Combine(库目录, "弹窗.hb");
            if (根 == null || !File.Exists(剪贴板路径) || !File.Exists(弹窗路径))
            {
                跳过("剪贴板与弹窗库", $"找不到库文件(根={根 ?? "(null)"})");
                return;
            }
            // 库是从工程目录 `包含 "../../库/…"` 进来的 ⇒ 这一组也要在**工程目录**那一层跑
            string 工作目录 = Path.Combine(根, "中间层", "示例59_剪贴板与弹窗");
            if (!Directory.Exists(工作目录)) 工作目录 = 根;

            // 本组的每一条都自己先写剪贴板:不依赖"跑之前剪贴板里有什么"
            宿主弹窗C.设允许弹窗(false);        // ⚠ 本组**一条都不弹**(硬规矩 6);测完恢复
            bool 原先允许 = 宿主弹窗C.允许弹窗;

            // ---------- ① 剪贴板往返(写 → 读 → 清 → 读) ----------
            try
            {
                const string 源 = """
                    包含 "../../库/剪贴板.hb"
                    输出行(剪贴板.复制("自检 左"));
                    输出行(剪贴板.取());
                    输出行(剪贴板.有东西());
                    输出行(剪贴板.复制("自检 右"));
                    输出行(剪贴板.取());
                    输出行(剪贴板.取().长度());
                    输出行(剪贴板.清掉());
                    输出行(剪贴板.有东西());
                    输出行(剪贴板.取().长度());
                    输出行(剪贴板.上次好());
                    停止
                    """;
                var (_, 输出) = 运行带输出(源, 工作目录);
                string 实得 = 输出.Replace("\r\n", "\n").TrimEnd('\n').Replace("\n", "|");
                // 第一次 `取` 之后 `有东西` 是 1;第二次 `取().长度()` = **第二段的字符个数**:
                //   「自检 右」四个字符(自 / 检 / 空格 / 右)⇒ **4**。
                //   ⚠ 这一格**照实得记,不照直觉写** —— 我第一版按"UTF-8 字节数"猜了 6,当场红。
                //   (长度口径本身是另一件事:`字符串.长度()` 数的是**字符**;要看字节数得走 `编码.hb`。)
                string 期望 = "1|自检 左|1|1|自检 右|4|1|0|0|1";
                bool 好 = 实得 == 期望;
                记录("剪贴板库 写进去再读回来逐字节同 + 清掉之后读回空串(clipboard 往返;同进程这一条)",
                    好, 好 ? "逐段一致(10 段)" : $"期望 <{期望}> / 实得 <{实得}>");
            }
            catch (Exception ex) { 记录异常("剪贴板库 往返", ex); }

            // ---------- ② ⭐ 那道闸关着时:三条弹窗 / 选文件都给"不支持"(状态 25) ----------
            try
            {
                宿主弹窗C.设允许弹窗(false);
                const string 源 = """
                    包含 "../../库/弹窗.hb"
                    输出行(弹窗.提醒("不该弹出来"));
                    输出行(弹窗.确认("不该弹出来"));
                    输出行(弹窗.问三样("不该弹出来"));
                    字符串 路 = "";
                    输出行(弹窗.选一个(".txt", 路));
                    输出行(弹窗.用户取消了());
                    输出行(弹窗.能弹窗());
                    停止
                    """;
                var (_, 输出) = 运行带输出(源, 工作目录);
                string 实得 = 输出.Replace("\r\n", "\n").TrimEnd('\n').Replace("\n", "|");
                // 提醒=假(0)/确认=假(0)/问三样=-1(不是 0,那个是"用户点了确定")/选一个=假(0)
                // 用户取消了=0(**没有这一手≠用户取消** —— 这正是本批要钉的那条口径)/能弹窗=0
                string 期望 = "0|0|-1|0|0|0";
                bool 好 = 实得 == 期望;
                记录("弹窗库 ⭐闸关着(自检 / 命令行)时:提醒 / 确认 / 问三样 / 选一个一律不弹,报『没有这一手』",
                    好, 好 ? "逐段一致(6 段;`问三样` 给 -1 而不是 0 —— 0 是『用户点了确定』)"
                            : $"期望 <{期望}> / 实得 <{实得}>");
            }
            catch (Exception ex) { 记录异常("弹窗库 闸关着", ex); }

            // ---------- ③ ⭐ "没有这一手" 与 "用户取消了" 分得开(接上替身走一趟真的取消) ----------
            try
            {
                var 替身 = new 选择器替身C();
                替身.排队取消();                      // 排一个"用户按了取消"的答案
                替身.挂上();
                try
                {
                    const string 源 = """
                        包含 "../../库/弹窗.hb"
                        字符串 路 = "";
                        输出行(弹窗.选一个(".txt", 路));
                        输出行(弹窗.用户取消了());
                        输出行(弹窗.能弹窗());
                        输出行(弹窗.上次错().长度());
                        停止
                        """;
                    var (_, 输出) = 运行带输出(源, 工作目录);
                    // 四行末尾那个空行(末段 = `上次错` 的空串)**在这一串里体现不出来**:
                    //   `TrimEnd('\n')` 把它连同最后那个换行一起去掉了 ⇒ 实得只有三段。
                    //   ⚠ 所以末段那个"空"**要靠另一条路钉** —— 用 `上次错().长度()` 直接印出来,
                    //   别指望"串尾的空段"(踩过一次:判据写成 `0|1|1|` 当场假红)。
                    string 实得 = 输出.Replace("\r\n", "\n").TrimEnd('\n').Replace("\n", "|");
                    // 取消:返回假 / `用户取消了` = 1 / **`能弹窗` = 1**(这个宿主有得弹,只是人没挑)/
                    //        `上次错` 是**空串**(取消不是错)⇒ 用它的长度 0 来钉那一格
                    string 期望 = "0|1|1|0";
                    bool 好 = 实得 == 期望;
                    记录("弹窗库 ⭐『用户取消了』与『没有这一手』分得开(替身排一个取消 ⇒ 取消了=1 而 能弹窗=1、上次错=空)",
                        好, 好 ? "逐段一致(4 段)" : $"期望 <{期望}> / 实得 <{实得}>");
                }
                finally { 替身.卸载(); }
            }
            catch (Exception ex) { 记录异常("弹窗库 取消与不支持分得开", ex); }

            // ---------- ④ 端到端:标准工程 `示例59_剪贴板与弹窗` ----------
            try
            {
                string? 示例目录 = 找示例目录();
                string? 工程 = 示例目录 == null ? null : Path.Combine(示例目录, "中间层", "示例59_剪贴板与弹窗", "主程序.hb");
                if (工程 == null || !File.Exists(工程))
                    跳过("剪贴板与弹窗库 端到端:标准工程 `示例59_剪贴板与弹窗` 与它那行 `// 预期输出:` 逐段对",
                        $"找不到 {工程}(不在工作副本里跑就跳过)");
                else
                {
                    string 工程目录 = Path.GetDirectoryName(工程)!;
                    var 期望 = 解析预期输出(File.ReadAllLines(工程, Encoding.UTF8));
                    var (_, 输出) = 运行带输出(File.ReadAllText(工程, Encoding.UTF8), 工程目录);
                    string 实得串 = 输出.Replace("\r\n", "\n").TrimEnd('\n').Replace("\n", "|");
                    string 期望串 = string.Join("|", 期望);
                    bool 好 = 期望.Count > 0 && 实得串 == 期望串;
                    记录("剪贴板与弹窗库 端到端:标准工程 `示例59_剪贴板与弹窗` 与它那行 `// 预期输出:` 逐段对",
                        好, 好 ? $"逐段一致({期望.Count} 段)"
                              : $"期望 {期望.Count} 段 / 实得 {实得串.Split('|').Length} 段;{剪贴板弹窗说明(期望串, 实得串)}");
                }
            }
            catch (Exception ex) { 记录异常("剪贴板与弹窗库 端到端", ex); }

            // ---------- ⑤ 接线哨兵:两个库都在盘上 + 模块名 + 头注释五段齐 ----------
            const string 接线断言 = "剪贴板与弹窗库 接线:两个库文件 + 模块名 + 公开函数数(6 / 11)";
            try
            {
                string 剪贴板源 = File.ReadAllText(剪贴板路径, Encoding.UTF8);
                string 弹窗源 = File.ReadAllText(弹窗路径, Encoding.UTF8);
                int 剪贴板公开 = 剪贴板源.Split('\n').Count(x => x.StartsWith("方法 ", StringComparison.Ordinal));
                int 弹窗公开 = 弹窗源.Split('\n').Count(x => x.StartsWith("方法 ", StringComparison.Ordinal));
                bool 判据 = 剪贴板源.StartsWith("// ============================================================", StringComparison.Ordinal)
                    && 剪贴板源.Contains("模块 剪贴板", StringComparison.Ordinal)
                    && 弹窗源.Contains("模块 弹窗", StringComparison.Ordinal)
                    && 剪贴板公开 == 6 && 弹窗公开 == 11;
                记接线(接线断言, 判据,
                    $"剪贴板 模块名 = {剪贴板源.Contains("模块 剪贴板", StringComparison.Ordinal)} / 公开方法 {剪贴板公开}(该 6);" +
                    $"弹窗 模块名 = {弹窗源.Contains("模块 弹窗", StringComparison.Ordinal)} / 公开方法 {弹窗公开}(该 11)" +
                    " —— 接线哨兵(读库源码文本判的),行为面看上面那四条");
            }
            catch (Exception ex) { 记录异常(接线断言, ex); }

            // ---------- ⑥ 接线哨兵(H92-1):「运行 → 允许弹窗」是界面那一侧**唯一**的口径 ----------
            // 为什么单钉这一条:界面原来**无条件** `宿主弹窗C.设允许弹窗(true)` ⇒ 这份示例在 IDE 腿
            // 真弹模态框、而它的 `// 预期输出:` 是按"不弹"写的 ⇒ 输出断言**永远红**(H92-1)。
            // 判据四条:菜单那一格在、**默认不勾**、设闸那行读的是这一格(而且没有别的"无条件设真")、
            // 勾选状态真的存进 `选项.json`(选项设置里有这个字段)。
            const string H92断言 = "H92-1 「运行 → 允许弹窗」是界面侧唯一口径(默认不勾 / 存 选项.json / 不再无条件设真)";
            try
            {
                string? 仓库根 = Directory.GetParent(根)?.FullName;
                if (仓库根 == null) 跳过(H92断言, "找不到仓库根");
                else
                {
                    string xaml = File.ReadAllText(Path.Combine(仓库根, "MainWindow.xaml"), Encoding.UTF8);
                    string 运行调试器 = File.ReadAllText(Path.Combine(仓库根, "MainWindow.运行与调试器.cs"), Encoding.UTF8);
                    string 选项源 = File.ReadAllText(Path.Combine(仓库根, "配置", "选项.cs"), Encoding.UTF8);
                    int 起 = xaml.IndexOf("x:Name=\"允许弹窗菜单\"", StringComparison.Ordinal);
                    string 那一格 = 起 < 0 ? "" : xaml.Substring(起, Math.Min(420, xaml.Length - 起));
                    bool 判据 = 那一格.Contains("IsCheckable=\"True\"", StringComparison.Ordinal)
                        && 那一格.Contains("IsChecked=\"False\"", StringComparison.Ordinal)          // 默认不勾
                        && 运行调试器.Contains("宿主弹窗C.设允许弹窗(允许弹窗菜单.IsChecked == true)", StringComparison.Ordinal)
                        && !运行调试器.Contains("宿主弹窗C.设允许弹窗(true)", StringComparison.Ordinal)   // 老那条"无条件设真"不许回来
                        && 选项源.Contains("public bool 允许弹窗", StringComparison.Ordinal);
                    记接线(H92断言, 判据,
                        $"菜单那一格 = {(起 >= 0 ? "在" : "找不到")} / 默认不勾 = {那一格.Contains("IsChecked=\"False\"", StringComparison.Ordinal)};" +
                        $"设闸读这一格 = {运行调试器.Contains("宿主弹窗C.设允许弹窗(允许弹窗菜单.IsChecked == true)", StringComparison.Ordinal)}" +
                        $"(无条件设真 {(运行调试器.Contains("宿主弹窗C.设允许弹窗(true)", StringComparison.Ordinal) ? "还在" : "已不在")});" +
                        $"选项.json 有这一格 = {选项源.Contains("public bool 允许弹窗", StringComparison.Ordinal)}" +
                        " —— 接线哨兵(读 XAML / IDE / 选项源码文本判的),行为面看上面那几条");
                }
            }
            catch (Exception ex) { 记录异常(H92断言, ex); }

            宿主弹窗C.设允许弹窗(原先允许);
        }
    }
}
