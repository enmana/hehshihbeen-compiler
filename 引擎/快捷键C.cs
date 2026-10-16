using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary> 一个可绑定的动作:动作名(唯一)+ 说明 + 默认键位(规范文本) </summary>
    public sealed class 快捷键项C
    {
        public string 动作 { get; set; } = "";
        public string 说明 { get; set; } = "";
        public string 默认键 { get; set; } = "";
        /// <summary> 能不能改(false = 由编辑器/系统自己注册,这里只列出来让人知道)</summary>
        public bool 可改 { get; set; } = true;
        public override string ToString() => $"{动作} = {默认键}";
    }

    /// <summary> 一个解析出来的键位:键名 + 三个修饰键 </summary>
    public readonly record struct 键位值C(string 键, bool 控制, bool 换档, bool 替换)
    {
        /// <summary> 规范文本(样式与表里写的一致:`Ctrl+Shift+F` / `F5`)</summary>
        public string 文本 => 快捷键C.格式化(this);
    }

    /// <summary>
    /// **IDE 快捷键表**(C7 = P27:快捷键可配)。纯数据 + 纯函数,不碰 WPF:
    /// `System.Windows.Input.Key` 那套只活在界面层(界面把 `Key.ToString()` 交给这里,按键名匹配),
    /// 所以这张表在命令行自检里也测得到。
    ///
    /// 用户的改动单独存一份"覆盖表"(动作 → 键位文本,存在 选项.json 里):
    /// 表里没有的动作名忽略并报出来;两个动作绑同一个键 = **冲突**,界面会提示(不静默失效)。
    /// </summary>
    public static class 快捷键C
    {
        /// <summary> 全部可配动作 + 默认键位(**唯一事实源**:菜单显示、按键分发、帮助文本都读它)</summary>
        public static readonly IReadOnlyList<快捷键项C> 全部 =
        [
            new 快捷键项C { 动作 = "执行", 默认键 = "F5", 说明 = "编译并执行(有断点就停在断点上)" },
            new 快捷键项C { 动作 = "逐步", 默认键 = "F10", 说明 = "走一条源码行(编译器生成的指令一口气跑过;编辑器不换文档)" },
            new 快捷键项C { 动作 = "继续", 默认键 = "F7", 说明 = "往下跑:有断点停在断点,没有就跑到停机" },
            new 快捷键项C { 动作 = "停止", 默认键 = "Shift+F5", 说明 = "把正在跑的程序叫停(死循环也不用杀进程);程序停在原地,不做断言" },
            new 快捷键项C { 动作 = "复位", 默认键 = "F8", 说明 = "回到起点并退出单步" },
            new 快捷键项C { 动作 = "热重载", 默认键 = "F6", 说明 = "H13:改源码不重启就换机器码(程序还在跑时先停下,再按一次换)" },
            new 快捷键项C { 动作 = "切换断点", 默认键 = "F9", 说明 = "在光标那一行加/去一个断点" },
            new 快捷键项C { 动作 = "格式化当前文件", 默认键 = "Ctrl+Shift+F", 说明 = "按块深度重排缩进(先预览再确认)" },
            new 快捷键项C { 动作 = "转到定义", 默认键 = "F12", 说明 = "跳到光标下那个名字的定义处" },
            new 快捷键项C { 动作 = "查找引用", 默认键 = "Shift+F12", 说明 = "列出谁用了这个名字" },
            new 快捷键项C { 动作 = "替换当前文件", 默认键 = "Ctrl+H", 说明 = "当前文件里替换一段文字(先给改动预览)" },
            new 快捷键项C { 动作 = "在控制台窗口里跑", 默认键 = "Ctrl+F5", 说明 = "编译(没编就编)后把独立程序在**它自己的控制台窗口**里跑起来(输入输出都在那个窗口里,退出码回写「输出」页)" },
            new 快捷键项C { 动作 = "命令面板", 默认键 = "Ctrl+Shift+P", 说明 = "一个框里找全部命令:菜单项 + 帮助站点的话题 + 最近用过的(回车执行,↑↓换)" },
            new 快捷键项C { 动作 = "加监视表达式", 默认键 = "Ctrl+Alt+W", 说明 = "把光标下的名字(或选中的文字)加进底部『监视』页(P35),每走一步自动重算" },
            new 快捷键项C { 动作 = "查找", 默认键 = "Ctrl+F", 可改 = false, 说明 = "编辑器自带的查找面板(它在编辑器上注册,这里只列出来)" },
        ];

        /// <summary> 动作名 → 默认键位 </summary>
        public static string 默认键(string 动作) =>
            全部.FirstOrDefault(x => string.Equals(x.动作, 动作, StringComparison.Ordinal))?.默认键 ?? "";

        /// <summary> 这一档动作能不能改(不认识的动作用默认值 true)</summary>
        public static bool 可改(string 动作) =>
            全部.FirstOrDefault(x => string.Equals(x.动作, 动作, StringComparison.Ordinal))?.可改 ?? false;

        /// <summary> 动作现在生效的键位:覆盖表里有就用它(空串 = 用户想**取消**这个快捷键),否则用默认 </summary>
        public static string 生效键(string 动作, IReadOnlyDictionary<string, string>? 覆盖 = null)
        {
            if (覆盖 != null && 覆盖.TryGetValue(动作, out string? 用户) && 用户 != null)
                return 规范(用户);
            return 默认键(动作);
        }

        /// <summary>
        /// 规范化键位文本:大小写、空格、顺序都不计较(`ctrl + shift+f` / `Shift+Ctrl+F` / `F5` 都认)。
        /// 认不出来返回空串(调用方报"写错了"并保留原样)。
        /// </summary>
        public static string 规范(string 文本)
        {
            if (string.IsNullOrWhiteSpace(文本)) return "";
            string[] 段 = 文本.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (段.Length == 0) return "";
            bool 控制 = false, 换档 = false, 替换 = false;
            string 键 = "";
            foreach (string 一 in 段)
            {
                string t = 一.Trim();
                if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || t.Equals("Control", StringComparison.OrdinalIgnoreCase))
                { if (控制) return ""; 控制 = true; continue; }
                if (t.Equals("Shift", StringComparison.OrdinalIgnoreCase)) { if (换档) return ""; 换档 = true; continue; }
                if (t.Equals("Alt", StringComparison.OrdinalIgnoreCase)) { if (替换) return ""; 替换 = true; continue; }
                if (键.Length > 0) return "";                 // 两个主键:写错了
                键 = 规范键名(t);
                if (键.Length == 0) return "";
            }
            if (键.Length == 0) return "";                    // 只有修饰键:不算
            return 格式化(new 键位值C(键, 控制, 换档, 替换));
        }

        /// <summary> 键名归一:`f5`→`F5`、`esc`→`Esc`、`回车`→`Enter`…(不认识返回空串)</summary>
        public static string 规范键名(string 名)
        {
            string t = (名 ?? "").Trim();
            if (t.Length == 0) return "";
            if (t.Length == 1 && char.IsLetter(t[0])) return char.ToUpperInvariant(t[0]).ToString();
            if (t.Length == 1 && char.IsDigit(t[0])) return t;
            if (t.Length is 2 or 3 && (t[0] == 'F' || t[0] == 'f') && int.TryParse(t[1..], out int 号) && 号 is >= 1 and <= 24)
                return "F" + 号;
            string 小 = t.ToLowerInvariant();
            return 小 switch
            {
                "enter" or "return" or "回车" => "Enter",
                "esc" or "escape" or "退出" => "Esc",
                "tab" or "制表" => "Tab",
                "space" or "空格" => "Space",
                "back" or "backspace" or "退格" => "Backspace",
                "delete" or "del" or "删除" => "Delete",
                "insert" or "ins" => "Insert",
                "home" or "首" => "Home",
                "end" or "尾" => "End",
                "pageup" or "pgup" or "上页" => "PageUp",
                "pagedown" or "pgdn" or "下页" => "PageDown",
                "up" or "上" => "Up",
                "down" or "下" => "Down",
                "left" or "左" => "Left",
                "right" or "右" => "Right",
                _ => "",
            };
        }

        /// <summary> 键位值 → 规范文本(`Ctrl+Shift+F` / `F5`)</summary>
        public static string 格式化(键位值C 值)
        {
            var sb = new StringBuilder();
            if (值.控制) sb.Append("Ctrl+");
            if (值.换档) sb.Append("Shift+");
            if (值.替换) sb.Append("Alt+");
            sb.Append(值.键);
            return sb.ToString();
        }

        /// <summary> 解析键位文本;认不出来返回 false(键位值为默认)</summary>
        public static bool 解析(string 文本, out 键位值C 值)
        {
            值 = default;
            string 规范文本 = 规范(文本);
            if (规范文本.Length == 0) return false;
            string[] 段 = 规范文本.Split('+');
            bool 控制 = 段.Contains("Ctrl"), 换档 = 段.Contains("Shift"), 替换 = 段.Contains("Alt");
            string 键 = 段[^1];
            值 = new 键位值C(键, 控制, 换档, 替换);
            return true;
        }

        /// <summary> 这个键位现在绑给哪个动作(没绑返回 null)</summary>
        public static string? 动作Of(string 键, bool 控制, bool 换档, bool 替换,
            IReadOnlyDictionary<string, string>? 覆盖 = null)
        {
            string 要 = 格式化(new 键位值C(键, 控制, 换档, 替换));
            foreach (var 项 in 全部)
            {
                if (!项.可改) continue;                     // 不可改的不参与分发(它由编辑器自己注册)
                string 现在 = 生效键(项.动作, 覆盖);
                if (现在.Length > 0 && string.Equals(现在, 要, StringComparison.OrdinalIgnoreCase)) return 项.动作;
            }
            return null;
        }

        /// <summary> 两个动作撞同一个键(返回给人看的一句句说明;空 = 没冲突)</summary>
        public static List<string> 冲突(IReadOnlyDictionary<string, string>? 覆盖 = null)
        {
            var 问题 = new List<string>();
            var 用过 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var 项 in 全部)
            {
                if (!项.可改) continue;
                string 现在 = 生效键(项.动作, 覆盖);
                if (现在.Length == 0) continue;
                if (用过.TryGetValue(现在, out string? 谁))
                    问题.Add($"`{现在}` 同时绑给了『{谁}』和『{项.动作}』:按下去只会触发先出现的那个,请改一个");
                else 用过[现在] = 项.动作;
            }
            return 问题;
        }

        /// <summary> 覆盖表里有、但表里没有的动作名(改了名字 / 手改坏了配置):报出来别静默吃掉</summary>
        public static List<string> 未知动作(IReadOnlyDictionary<string, string>? 覆盖)
        {
            var 结果 = new List<string>();
            if (覆盖 == null) return 结果;
            foreach (var 键 in 覆盖.Keys)
                if (!全部.Any(x => string.Equals(x.动作, 键, StringComparison.Ordinal)))
                    结果.Add($"快捷键设置里的 `{键}` 不是本程序的动作名(已忽略)");
            return 结果;
        }

        /// <summary> 现在生效的整张表(动作 → 键位文本;空串 = 用户取消了)</summary>
        public static List<(string 动作, string 键位, string 说明, bool 可改)> 生效表(IReadOnlyDictionary<string, string>? 覆盖 = null) =>
            [.. 全部.Select(x => (x.动作, 生效键(x.动作, 覆盖), x.说明, x.可改))];

        /// <summary> 给人看的一份清单(帮助文本 / 选项页 / 命令行都用它)</summary>
        public static string 清单(IReadOnlyDictionary<string, string>? 覆盖 = null)
        {
            var sb = new StringBuilder($"快捷键(共 {全部.Count} 条):");
            foreach (var (动作, 键位, 说明, 可改) in 生效表(覆盖))
                sb.Append($"\n  {键位,-16}{(可改 ? "可改" : "内置")}  {动作,-14} {说明}");
            return sb.ToString();
        }
    }
}
