using System;
using System.IO;
using System.Linq;
using System.Text;

namespace hehshihbeen_compiler
{
    public static partial class 综合自检C
    {
        /// <summary>
        /// H29(第201批):**手机端的能力缺口 —— 输入那一半**。
        ///
        /// 原报:`MobileIDE\MainPage.xaml.cs` 把虚拟机的 输入源 写成 `() => null`,于是 `IN` 一律 EOF
        /// (**任何要输入的 HB 程序在手机上都跑不了**);同时手机端从来没设过网址白名单。
        /// 用户 2026-09-22 拍板两件:**输入给**(照桌面「控制台」页口径做输入框 + 「发送」)、
        /// **网络不给**(帮助文本写死"手机端没有网络";独立 exe 那条归 D6,本批不动)。
        ///
        /// 手机端 UI 开不了窗、自检也不链 MAUI —— 这份验收走**按源码钉**(与 H22 / C28 那几条同一个办法):
        /// 一条一条查"那两格控件在不在、输入源是不是真走队列、等输入有没有开、送一行是不是 UTF-8 + 换行 + 回显、
        /// 从头跑是不是清队列、拍板结果(手机端不设白名单)有没有被写反"。
        /// </summary>
        private static void 手机端自检()
        {
            string[] 名表 =
            [
                "手机端 H29:输入框 + 「发送」两格控件在(XAML 里有名有事件)",
                "手机端 H29:输入源改走字节队列(不再是 `() => null` 那条 EOF 路)",
                "手机端 H29:开了 `等输入`(读输入没数据时停下来等,不当作没有输入了)",
                "手机端 H29:送一行 = UTF-8 字节 + 一个换行,并回显 `> 行`(与桌面同一个口径)",
                "手机端 H29:程序卡在等输入时送一行就接着跑(同一条判据 `正在等输入`)",
                "手机端 H29:从头跑清掉输入队列 / 回显 / 上一趟输出(与桌面「执行」同一个口径)",
                "手机端 H29:拍板结果 —— 手机端不挂白名单(`取网址` 一律假),帮助文本写死这一条",
                "手机端 H10:「保存」把编辑框写回磁盘(没打开过文件就落沙箱工程目录的 `主程序.hb`)",
                "手机端 H10:跑程序之前把『相对路径基准目录』定成源文件所在目录(没打开过文件 = 沙箱工程目录)",
                "手机端 H10:命令行 / HB宿主 / 桌面 IDE 一处都不设基准目录(相对路径照旧按进程工作目录)",
                "手机端 H10:帮助《第24节·手机端》写明「保存」与相对路径口径,老那句『那条还没做』退场",
                "手机端 执行自己先汇编:『执行』不再等『汇编』成功才可点(源码 / 附加文件动过就现编,编不过就不跑)",
            ];

            string? 示例目录 = 找示例目录();
            string? 根 = 示例目录 == null ? null : Directory.GetParent(示例目录)?.FullName;
            if (根 == null)
            {
                foreach (string 名 in 名表) 跳过接线(名, "工作副本之外跑自检时跳过");
                return;
            }

            try
            {
                string 手机 = File.ReadAllText(Path.Combine(根, "MobileIDE", "MainPage.xaml.cs"));
                string 手机界面 = File.ReadAllText(Path.Combine(根, "MobileIDE", "MainPage.xaml"));
                string 手机选择器源 = File.ReadAllText(Path.Combine(根, "MobileIDE", "手机选择器.cs"));
                string 桌面 = File.ReadAllText(Path.Combine(根, "MainWindow.调试与断点.cs"));
                string 桌面宿主接线 = File.ReadAllText(Path.Combine(根, "MainWindow.窗口与文件.cs"));
                string 命令行 = File.ReadAllText(Path.Combine(根, "编译器CLI", "Program.cs"));
                string 独立宿主 = File.ReadAllText(Path.Combine(根, "HB宿主", "Program.cs"));
                string 帮助 = 帮助文本C.快速上手();

                记接线(名表[0],
                    手机界面.Contains("x:Name=\"控制台输入框\"", StringComparison.Ordinal)
                    && 手机界面.Contains("Text=\"发送\"", StringComparison.Ordinal)
                    && 手机界面.Contains("Completed=\"控制台输入框_Completed\"", StringComparison.Ordinal)
                    && 手机界面.Contains("Clicked=\"发送按钮_Click\"", StringComparison.Ordinal),
                    "MainPage.xaml:输入框(回车)+「发送」按钮两条路都在");

                记接线(名表[1],
                    !手机.Contains("() => null", StringComparison.Ordinal)
                    && 手机.Contains("输入源 = 取一个输入字节", StringComparison.Ordinal)
                    && 手机.Contains("_输入队列.Count > 0 ? _输入队列.Dequeue() : null", StringComparison.Ordinal),
                    "MainPage.xaml.cs:输入源 = 取一个输入字节(队列空才给 null)");

                记接线(名表[2],
                    手机.Contains("等输入 = true", StringComparison.Ordinal)
                    && 手机.Contains("前端契约C.服务.调试.机器.清除等待输入()", StringComparison.Ordinal)
                    && 手机.Contains("前端契约C.服务.调试.机器要重新装载(_机器码!)", StringComparison.Ordinal),
                    "MainPage.xaml.cs:等输入 / 清除等待输入 / 需要重新装载 三处都在(接着跑不清状态;H38-2 起机器与判据都问契约)");

                记接线(名表[3],
                    手机.Contains("foreach (byte b in Encoding.UTF8.GetBytes(行)) _输入队列.Enqueue(b)", StringComparison.Ordinal)
                    && 手机.Contains("_输入队列.Enqueue((byte)'\\n')", StringComparison.Ordinal)
                    && 手机.Contains("_输入回显.Append(\"> \")", StringComparison.Ordinal)
                    && 桌面.Contains("_控制台输入.Enqueue((byte)'\\n')", StringComparison.Ordinal),
                    "手机 `送一行输入` 与桌面 `送一行输入` 逐句同款");

                记接线(名表[4],
                    手机.Contains("if (_有机器 && 前端契约C.服务.调试.机器.正在等输入) 接着跑()", StringComparison.Ordinal)
                    && 手机.Contains("while (机.单步() && !机.正在等输入)", StringComparison.Ordinal)
                    && 桌面.Contains("正在等输入 && 当前机器码.Length > 0", StringComparison.Ordinal),
                    "MainPage.xaml.cs 跑到停 / 接着跑 + 桌面那条循环拿同一个记号当退出条件(H38-2 起机器问契约要)");

                记接线(名表[5],
                    手机.Contains("_输入队列.Clear()", StringComparison.Ordinal)
                    && 手机.Contains("_输入回显.Clear()", StringComparison.Ordinal)
                    && 手机.Contains("_程序输出.Clear()", StringComparison.Ordinal)
                    && 手机.Contains("初值表 = 前端契约C.服务.编译.初值表()", StringComparison.Ordinal),
                    "MainPage.xaml.cs 执行按钮_Click:三样一起清 + 初值表跟着机器码落进数据段(H38-2 起编译结果不存字段)");

                记接线(名表[6],
                    !手机.Contains("设网址白名单", StringComparison.Ordinal)
                    && 帮助.Contains("手机端没有网络", StringComparison.Ordinal)
                    && !帮助.Contains("手机端没有输入界面", StringComparison.Ordinal)
                    && 帮助.Contains("输入框 + 「发送」", StringComparison.Ordinal),
                    "拍板两件:输入给(帮助写明)、网络不给(白名单一处不挂)");

                // ---- H10/H64:手机端目录能力(沙箱那一半 + 系统目录那一半)—— 保存 / 相对路径基准目录 ----
                记接线(名表[7],
                    手机界面.Contains("x:Name=\"保存按钮\"", StringComparison.Ordinal)
                    && 手机界面.Contains("Clicked=\"保存按钮_Click\"", StringComparison.Ordinal)
                    && 手机.Contains("private void 保存按钮_Click", StringComparison.Ordinal)
                    && 手机.Contains("前端契约C.服务.宿主.写文本文件(目标, 源码编辑框.Text ?? \"\")", StringComparison.Ordinal)
                    && 手机.Contains("?? Path.Combine(手机选择器.工程根目录, \"主程序.hb\")", StringComparison.Ordinal),
                    "MainPage:「保存」写回当前文件(经 `虚拟文件C` ⇒ 系统目录里那份也写得回去);没打开过文件就落沙箱工程目录的 主程序.hb");

                记接线(名表[8],
                    手机.Contains("手机选择器.定基准目录(_工程目录)", StringComparison.Ordinal)
                    && 手机选择器源.Contains("前端契约C.服务.宿主.设基准目录(string.IsNullOrWhiteSpace(当前目录) ? 工程根目录 : 当前目录)", StringComparison.Ordinal),
                    "跑之前调 `定基准目录(_工程目录)`;选择器那头落到 `宿主能力C.设基准目录`(空则沙箱工程目录)");

                记接线(名表[9],
                    !命令行.Contains("设基准目录", StringComparison.Ordinal)
                    && !独立宿主.Contains("设基准目录", StringComparison.Ordinal)
                    && !桌面宿主接线.Contains("设基准目录", StringComparison.Ordinal),
                    "命令行 / HB宿主 / 桌面 IDE 一处都不设基准目录(相对路径照旧按进程工作目录)");

                记接线(名表[10],
                    帮助.Contains("「保存」", StringComparison.Ordinal)
                    && 帮助.Contains("相对路径以\"程序源文件所在的那个目录\"为准", StringComparison.Ordinal)
                    && !帮助.Contains("SAF、拿回来的是", StringComparison.Ordinal)
                    && 帮助.Contains("「打开工程目录…」先给你挑系统目录", StringComparison.Ordinal),
                    "帮助《第24节·手机端》:保存 + 相对路径口径写清(H64 起还写明「打开工程目录…」先挑系统目录)");
                // ---- T22 行为探针:H29 那一套的**引擎侧一半**真跑一遍(这才是"不是只读文本"的那一条) ----
                // 手机端界面开不了(MAUI 不链进自检),但"字节队列当输入源 + `等输入`"这条契约长在引擎上:
                // 队列里有数据 → 读取行(缓冲) 拿得到整行;队列空 → 程序**停在原地等**,而不是被当成"没有输入了"。
                bool 输入探针好 = false;
                string 输入探针说 = "";
                try
                {
                    // `读取行(缓冲)` 读一整行(不含换行)写进那个字符串、返回读到的字节数 —— 与手机端
                    // `送一行输入`(UTF-8 字节 + 一个换行)配对的那条路。
                    const string 读一行源 = "字符串 行 = \"\";\n整数 n = 读取行(行);\n输出行(行);\n停止";
                    var 队列 = new System.Collections.Generic.Queue<uint>(
                        Encoding.UTF8.GetBytes("abc\n").Select(b => (uint)b));
                    var 缓冲 = new StringBuilder();
                    var 有数据机 = new 虚拟机C
                    {
                        输入源 = () => 队列.Count > 0 ? 队列.Dequeue() : null,
                        等输入 = true,
                        输出目标 = b => 缓冲.Append((char)b),
                    };
                    有数据机.运行(编译(读一行源));
                    string 出 = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(缓冲.ToString())).TrimEnd('\n');

                    var 空机 = new 虚拟机C { 输入源 = () => null, 等输入 = true, 输出目标 = null };
                    空机.装载(编译(读一行源));
                    long 步 = 0;
                    while (空机.单步() && !空机.正在等输入 && 步 < 100_000) 步++;
                    输入探针好 = 出 == "abc" && 空机.正在等输入 && !空机.已结束;
                    输入探针说 = $"有数据时读到[{出}];空队列时 正在等输入={空机.正在等输入} 已结束={空机.已结束}";
                }
                catch (Exception ex) { 输入探针说 = ex.GetType().Name + ": " + ex.Message; }
                记录("手机端 H29 行为探针:字节队列当输入源 + `等输入`,引擎那半真跑一遍(读到整行 / 队列空就停下来等)",
                    输入探针好, 输入探针说);

                // ---- 第329批(用户真机核对时点名):「执行」不再要"先点汇编"才能点 ----
                // 原来 `执行按钮` 初始 `IsEnabled="False"`,只有汇编成功才置真 ⇒ 两步仪式,
                // 而且"汇编之后又改源码再点执行"会**静默跑上一版机器码**。现在执行自己先编。
                记接线(名表[11],
                    手机界面.Contains("x:Name=\"执行按钮\" Text=\"执行\" Clicked=\"执行按钮_Click\"", StringComparison.Ordinal)
                    && !手机界面.Contains("x:Name=\"执行按钮\" Text=\"执行\" IsEnabled=\"False\"", StringComparison.Ordinal)
                    && 手机.Contains("private bool 确保汇编过()", StringComparison.Ordinal)
                    && 手机.Contains("if (!确保汇编过()) return;", StringComparison.Ordinal)
                    && 手机.Contains("_汇编签名 == 当前汇编签名()", StringComparison.Ordinal)
                    && 手机.Contains("_汇编签名 = 当前汇编签名();", StringComparison.Ordinal),
                    "MainPage:「执行」初始不置灰,点下去先 `确保汇编过()`(源码 / 附加文件动过就现编,编不过不跑)");
            }
            catch (Exception ex) { 记录异常("手机端(H29)", ex); }
        }
    }
}
