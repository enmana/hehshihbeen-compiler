using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **会话记忆**(P36,2026-10-07;用户排的七件里的第 3 件:
    /// "标签/光标/断点/窗口位置,连带未保存恢复")。
    ///
    /// 这一组钉六件事(前五条是**真跑**的纯函数,第六条按源码钉界面接线):
    ///   ① **往返一致**:一屏(页 / 光标 / 未保存正文 / 当前页 / 窗口四格)写进文本再读回来,逐格相同;
    ///   ② **防呆读入**:坏 JSON 当"没有会话";没路径又没标题的页读进来就扔掉(不报"少了 1 页");
    ///   ③ **裁旧**:超过 20 个工程按"最后离开"留最近的 20 个;
    ///   ④ **窗口四格**:缺一格 / 太小(不到 400×300)一律算"没记过";齐全才算;
    ///   ⑤ **会话键**:同一个工程的两种写法(带尾分隔符 / 大小写不同)算**同一个键**;
    ///   ⑥ 接线:关窗那一刻记、打开工程那一刻恢复、切页时记光标、启动页关掉时按记忆定最大化,
    ///      而且**断点不重复记**(它跟着工程的 断点.hbd 走)。
    /// </summary>
    public static partial class 综合自检C
    {
        public static void 会话记忆自检()
        {
            // ---- ① 往返一致 ----
            const string 往返名 = "会话记忆 一屏(页/光标/未保存正文/当前页/窗口四格)写进文本再读回来逐格相同";
            try
            {
                var 表 = new 会话表C();
                var 会话 = new 会话工程C
                {
                    当前页 = 1,
                    左 = 120, 上 = 80, 宽 = 1280, 高 = 800, 最大化 = true,
                    页表 =
                    [
                        new 会话页C { 全称 = @"E:\示例\主程序.hb", 标题 = "主程序.hb", 行 = 42, 列 = 7 },
                        new 会话页C { 全称 = @"E:\示例\工具.hb", 标题 = "工具.hb", 行 = 3, 列 = 1, 未保存正文 = "整数 初值 = 1;\n" },
                    ],
                };
                表.存(@"E:\示例\工程.hbp", 会话);
                string 文本 = 表.到文本();
                var 读回 = 会话表C.从文本(文本);
                var 工程 = 读回.取(@"E:\示例\工程.hbp");
                bool 对 = 工程.页表.Count == 2 && 工程.当前页 == 1
                    && 工程.页表[0].行 == 42 && 工程.页表[0].列 == 7 && !工程.页表[0].有未保存
                    && 工程.页表[1].有未保存 && 工程.页表[1].未保存正文 == "整数 初值 = 1;\n"
                    && 工程.有窗口位置 && Math.Abs(工程.宽 - 1280) < 0.01 && 工程.最大化
                    && 文本.Contains("未保存正文", StringComparison.Ordinal);
                记录(往返名, 对,
                    $"页 {工程.页表.Count} 张 / 当前页 {工程.当前页} / 光标 {工程.页表[0].行}:{工程.页表[0].列} / " +
                    $"未保存 {工程.页表.Count(x => x.有未保存)} 张 / 窗口 {工程.宽}×{工程.高}{(工程.最大化 ? "(最大化)" : "")}");
            }
            catch (Exception ex) { 记录异常(往返名, ex); }

            // ---- ② 防呆读入 ----
            const string 防呆名 = "会话记忆 读入防呆:坏 JSON = 没有会话;没路径又没标题的页读进来就扔掉";
            try
            {
                var 坏 = 会话表C.从文本("{ 这不是 json");
                var 空 = 会话表C.从文本("{}");
                var 脏 = 会话表C.从文本("{\"工程表\":{\"k\":{\"页表\":[{\"全称\":\"\",\"标题\":\"\"},{\"全称\":\"E:\\\\a.hb\",\"标题\":\"a.hb\"}]}}}");
                bool 对 = 坏.工程表.Count == 0 && 空.工程表.Count == 0
                    && 脏.取("k").页表.Count == 1 && 脏.取("k").页表[0].标题 == "a.hb";
                记录(防呆名, 对,
                    $"坏 JSON → {坏.工程表.Count} 个工程;空 JSON → {空.工程表.Count} 个;脏数据 2 页 → 留 {脏.取("k").页表.Count} 页");
            }
            catch (Exception ex) { 记录异常(防呆名, ex); }

            // ---- ③ 裁旧:超过 20 个工程留最近的 ----
            const string 裁旧名 = "会话记忆 超过 20 个工程按『最后离开』裁掉最旧的(留最近 20 个)";
            try
            {
                var 表 = 会话表C.从文本("{}");
                var 现在 = new DateTime(2026, 10, 7, 12, 0, 0);
                for (int i = 0; i < 25; i++)
                {
                    var 会话 = new 会话工程C { 最后离开 = 现在.AddMinutes(-i) };
                    会话.页表.Add(new 会话页C { 全称 = $@"E:\项目{i}\主程序.hb", 标题 = $"主程序{i}.hb" });
                    表.存($@"E:\项目{i}\工程.hbp", 会话);
                }
                // 存档会把"最后离开"刷成现在:所以这里按**存进去的顺序**判 —— 最早的 5 个该被裁掉
                bool 对 = 表.工程表.Count == 会话表C.最多工程数
                    && !表.工程表.ContainsKey(@"E:\项目0\工程.hbp")
                    && 表.工程表.ContainsKey(@"E:\项目24\工程.hbp");
                记录(裁旧名, 对, $"存 25 个 → 留 {表.工程表.Count} 个(上限 {会话表C.最多工程数});最旧的还在吗 = {表.工程表.ContainsKey(@"E:\项目0\工程.hbp")}");
            }
            catch (Exception ex) { 记录异常(裁旧名, ex); }

            // ---- ④ 窗口四格:缺一格 / 太小都算"没记过" ----
            const string 窗口名 = "会话记忆 窗口四格:缺一格或太小(不到 400×300)一律算『没记过』,不让窗口开成一条线";
            try
            {
                var 齐 = new 会话工程C { 左 = 10, 上 = 10, 宽 = 800, 高 = 600 };
                var 缺 = new 会话工程C { 左 = 10, 上 = 10, 宽 = 800 };
                var 小 = new 会话工程C { 左 = 10, 上 = 10, 宽 = 300, 高 = 200 };
                var 没 = new 会话工程C();
                记录(窗口名, 齐.有窗口位置 && !缺.有窗口位置 && !小.有窗口位置 && !没.有窗口位置,
                    $"齐全 → {齐.有窗口位置};缺高 → {缺.有窗口位置};300×200 → {小.有窗口位置};全没记 → {没.有窗口位置}");
            }
            catch (Exception ex) { 记录异常(窗口名, ex); }

            // ---- ⑤ 会话键:两种写法算同一个 ----
            const string 键名 = "会话记忆 会话键:同一个工程的两种写法(尾分隔符 / 大小写)算同一个键";
            try
            {
                string 全路径键 = 会话表C.键(@"E:\示例\工程.hbp", null);
                string 小写盘符键 = 会话表C.键(@"e:\示例\工程.hbp", null);
                string 带尾分隔目录键 = 会话表C.键(null, @"E:\示例\");
                string 无尾分隔目录键 = 会话表C.键(null, @"E:\示例");
                var 表 = new 会话表C();
                表.存(全路径键, new 会话工程C { 页表 = [new 会话页C { 全称 = "x.hb", 标题 = "x.hb" }] });
                bool 对 = 全路径键 == 小写盘符键 && 带尾分隔目录键 == 无尾分隔目录键 && 带尾分隔目录键.Length > 0 && 表.取(小写盘符键).页表.Count == 1 && 表.取("").页表.Count == 0;
                记录(键名, 对, $"`{全路径键}` == `{小写盘符键}` = {全路径键 == 小写盘符键};`{带尾分隔目录键}` == `{无尾分隔目录键}` = {带尾分隔目录键 == 无尾分隔目录键};大小写不认得的那份也读得到 = {表.取(小写盘符键).页表.Count == 1}");
            }
            catch (Exception ex) { 记录异常(键名, ex); }

            // ---- ⑥ 界面接线(接线哨兵) ----
            const string 接线名 = "会话记忆 接线:关窗时记、打开工程时恢复、切页时记光标、启动页按记忆定最大化、断点不重复记";
            string? 根 = 找仓库根目录();
            if (根 == null) 跳过接线(接线名, "工作副本之外跑自检,读不到源码");
            else
            {
                try
                {
                    string 窗口 = File.ReadAllText(Path.Combine(根, "MainWindow.窗口与文件.cs"));
                    string 项目 = File.ReadAllText(Path.Combine(根, "MainWindow.项目与编译.cs"));
                    string 会话 = File.ReadAllText(Path.Combine(根, "MainWindow.会话记忆.cs"));
                    string 检查 = File.ReadAllText(Path.Combine(根, "MainWindow.代码检查.cs"));
                    string 主窗 = File.ReadAllText(Path.Combine(根, "MainWindow.xaml.cs"));
                    记接线(接线名,
                        窗口.Contains("记住离页光标();", StringComparison.Ordinal)
                        && 窗口.Contains("记会话();", StringComparison.Ordinal)
                        && 窗口.Contains("代码选卡.SelectionChanged += 选卡变了;", StringComparison.Ordinal)
                        && 项目.Contains("记会话();", StringComparison.Ordinal)
                        && 项目.Contains("if (会话.页表.Count > 0 && 恢复会话(会话) > 0)", StringComparison.Ordinal)
                        && 项目.Contains("WindowState = 还要最大化() ? WindowState.Maximized : WindowState.Normal;", StringComparison.Ordinal)
                        && 检查.Contains("安排会话自存();", StringComparison.Ordinal)
                        && 主窗.Contains("private void 选卡变了(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)", StringComparison.Ordinal)
                        && 会话.Contains("会话表C.当前.存(键, 会话);", StringComparison.Ordinal)
                        // 断点**不**在会话里记(它跟着工程的 断点.hbd 走)—— 这条钉住"别顺手抄一份":
                        // 会话记忆那两个类型里不许出现"断点"字段
                        && !File.ReadAllText(Path.Combine(根, "引擎", "会话记忆C.cs")).Contains("断点项C", StringComparison.Ordinal),
                        "关窗记 / 换工程先记 / 打开工程恢复 / 切页记光标 / 启动页按记忆定最大化 / 改完停手自存;断点不在会话里");
                }
                catch (Exception ex) { 记录异常(接线名, ex); }
            }
        }
    }
}
