using System.Globalization;
using System.IO;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **输入事件的类型**(`取事件(类型, 键码, x, y)` 的第一个出参)—— R1-6 批 2。
    ///
    /// 这里**只是编号表**:真投递在 `图形宿主C.投事件`(桌面壳把键盘 / 鼠标 / 关窗请求塞进队列;
    /// 自检与 `hbcc --事件脚本` 拿脚本塞)。编号是**对外接口**的一部分(写进帮助站点),
    /// 以后只加不改 —— 与"宿主能力号 / 图形功能号"同一档。
    /// </summary>
    public static class 图形事件C
    {
        /// <summary> 用户点了关窗(标题栏的叉 / 脚本跑满帧数时宿主自己投的那一个"关窗请求") </summary>
        public const int 关窗 = 1;
        /// <summary> 键盘按下一次(键码见 `图形键C`;鼠标坐标两个 0) </summary>
        public const int 按键 = 2;
        /// <summary> 鼠标移动(x, y 是窗口内坐标;键码 0) </summary>
        public const int 移动 = 3;
        /// <summary> 鼠标键按下(键码 = `图形键C.鼠标左键` 那三个;x, y 是按下那一刻的位置) </summary>
        public const int 按下 = 4;
        /// <summary> 鼠标键抬起(与 `按下` 成对;**"点一下" = 同一个键码的 `按下` + `抬起` 两条事件**) </summary>
        public const int 抬起 = 5;

        /// <summary> 事件类型的脚本写法(中文规范名;英文别名见 `认事件名`) </summary>
        private static readonly (string 词, int 类型)[] _别名 =
        [
            ("关窗", 关窗), ("关闭窗口", 关窗), ("close", 关窗), ("closewindow", 关窗),
            ("按键", 按键), ("键盘", 按键), ("key", 按键), ("keydown", 按键),
            ("移动", 移动), ("move", 移动), ("mousemove", 移动),
            ("按下", 按下), ("down", 按下), ("mousedown", 按下),
            ("抬起", 抬起), ("松开", 抬起), ("up", 抬起), ("mouseup", 抬起),
        ];

        /// <summary> 事件类型的中文名(报错文案 / 事件脚本 dump 用);认不出给空串 </summary>
        public static string 名字(int 类型) => 类型 switch
        {
            关窗 => "关窗",
            按键 => "按键",
            移动 => "移动",
            按下 => "按下",
            抬起 => "抬起",
            _ => "",
        };

        /// <summary> 事件脚本里那个词是哪个类型(中英两侧都认) </summary>
        public static bool 认事件名(string 词, out int 类型)
        {
            类型 = 0;
            foreach (var (别名, 号) in _别名)
                if (别名 == 词 || string.Equals(别名, 词, StringComparison.OrdinalIgnoreCase)) { 类型 = 号; return true; }
            return false;
        }
    }

    /// <summary>
    /// **键码表**(R1-6 批 2):`取事件` 的第二个出参是什么,由这儿说了算。
    ///
    /// 为什么要一张自己的表:桌面壳底座是 Avalonia,它那个 `Key` 枚举是**框架的实现细节**
    /// (编号会随框架版本动)。HB 程序要认的是**这门语言的口径** —— 表格在这儿,桌面壳负责把
    /// Avalonia 的键译过来(`图形宿主\桌面壳C.译键码`),两边从此解耦。
    ///
    /// 取值口径:
    /// · **字母 / 数字直接取 ASCII 码**(`A`=65、`0`=48;字母一律**大写**)—— 好记、与 C 一串同口径;
    /// · 编辑键 / 方向键 / 修饰键取**Windows 虚拟键**那套老编号(`回车`=13、`退出`=27、`左`=37;
    ///   不是抄某个框架的类型,是这套编号本来就跨框架通用);
    /// · `F1`~`F12` = 112~123;鼠标键 `鼠标左键`=1 / `鼠标中键`=2 / `鼠标右键`=3(**只用在 `按下`/`抬起`**)。
    /// </summary>
    public static class 图形键C
    {
        // ---- 键盘 ----
        public const int 无 = 0;
        public const int 退格 = 8;
        public const int 制表 = 9;
        public const int 回车 = 13;
        public const int 上档 = 16;      // Shift
        public const int 控制 = 17;      // Ctrl
        public const int 替换 = 18;      // Alt
        public const int 退出 = 27;      // Esc
        public const int 空格 = 32;
        public const int 上一页 = 33;
        public const int 下一页 = 34;
        public const int 末页 = 35;
        public const int 首页 = 36;
        public const int 左 = 37;
        public const int 上 = 38;
        public const int 右 = 39;
        public const int 下 = 40;
        public const int 插入 = 45;
        public const int 删除 = 46;

        // ---- 鼠标键(只在 `按下` / `抬起` 两个事件里当键码) ----
        public const int 鼠标左键 = 1;
        public const int 鼠标中键 = 2;
        public const int 鼠标右键 = 3;

        // ---- 三族连续码 ----
        /// <summary> 字母键起点 `A` = 65(到 `Z` = 90)</summary>
        public const int 字母最小 = 65;
        /// <summary> 字母键终点 `Z` = 90 </summary>
        public const int 字母最大 = 90;
        /// <summary> 数字键起点 `0` = 48(到 `9` = 57)</summary>
        public const int 数字最小 = 48;
        /// <summary> 数字键终点 `9` = 57 </summary>
        public const int 数字最大 = 57;
        /// <summary> 功能键起点 `F1` = 112 </summary>
        public const int 功能键最小 = 112;
        /// <summary> 功能键一共几个(F1~F12)</summary>
        public const int 功能键个数 = 12;

        /// <summary> 第 n 个功能键的键码(1 → F1 = 112)</summary>
        public static int 功能键(int 第几个) => 111 + 第几个;

        /// <summary> 有名字的那些键(键码 / 中文名 / 英文别名) —— 名字与脚本解析共用这一处 </summary>
        private static readonly (int 码, string 中文, string 英文)[] _表 =
        [
            (无, "无", "none"),
            (鼠标左键, "鼠标左键", "mouseleft"),
            (鼠标中键, "鼠标中键", "mousemiddle"),
            (鼠标右键, "鼠标右键", "mouseright"),
            (退格, "退格", "backspace"),
            (制表, "制表", "tab"),
            (回车, "回车", "enter"),
            (上档, "上档", "shift"),
            (控制, "控制", "ctrl"),
            (替换, "替换", "alt"),
            (退出, "退出", "esc"),
            (空格, "空格", "space"),
            (上一页, "上一页", "pageup"),
            (下一页, "下一页", "pagedown"),
            (首页, "首页", "home"),
            (末页, "末页", "end"),
            (插入, "插入", "insert"),
            (删除, "删除", "delete"),
            (左, "左", "left"),
            (上, "上", "up"),
            (右, "右", "right"),
            (下, "下", "down"),
        ];

        /// <summary> 键码的中文名(`回车` / `字母A` / `数字5` / `F3` / `鼠标左键`);认不出给空串 </summary>
        public static string 名字(int 码)
        {
            foreach (var (号, 中文, _) in _表) if (号 == 码) return 中文;
            if (码 is >= 字母最小 and <= 字母最大) return "字母" + (char)码;
            if (码 is >= 数字最小 and <= 数字最大) return "数字" + (char)码;
            if (码 is >= 功能键最小 and < 功能键最小 + 功能键个数) return "F" + (码 - 111);
            return "";
        }

        /// <summary>
        /// 认一个键名(事件脚本里写的那个词):中文名 / 英文别名 / `字母A` / `数字5` / `F3` /
        /// **裸十进制或 `0x` 十六进制数字**(直接写键码)。
        ///
        /// 注意一个有意为之的口径:**裸数字一律当"键码",不当"数字键"** ——
        /// `键码 5` 与"按下 5 这个键"(=53)会打架,所以数字键要写 `数字5`。
        /// </summary>
        public static bool 认键名(string 词, out int 码)
        {
            码 = 无;
            if (string.IsNullOrEmpty(词)) return false;
            foreach (var (代号, 中文, 英文) in _表)
                if (词 == 中文 || string.Equals(词, 英文, StringComparison.OrdinalIgnoreCase)) { 码 = 代号; return true; }

            // 单个字母:`a` / `A` 都算 A 键(ASCII 大写口径)
            if (词.Length == 1 && char.IsAsciiLetter(词[0])) { 码 = char.ToUpperInvariant(词[0]); return true; }
            if (去前缀(词, "字母", out string 字) || 去前缀(词, "letter", out 字))
            {
                if (字.Length == 1 && char.IsAsciiLetter(字[0])) { 码 = char.ToUpperInvariant(字[0]); return true; }
                return false;
            }
            if (去前缀(词, "数字", out string 数) || 去前缀(词, "digit", out 数))
            {
                if (数.Length == 1 && char.IsAsciiDigit(数[0])) { 码 = 数[0]; return true; }
                return false;
            }
            if (去前缀(词, "F", out string 号))
            {
                if (int.TryParse(号, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                    && n >= 1 && n <= 功能键个数) { 码 = 功能键(n); return true; }
                return false;
            }
            // 裸数字:十进制,或 `0x` 起头的十六进制(键码本身就是对外接口,允许直接写它的数)
            if (词.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(词[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int h) && h >= 0)
                { 码 = h; return true; }
                return false;
            }
            if (int.TryParse(词, NumberStyles.None, CultureInfo.InvariantCulture, out int 值) && 值 >= 0)
            { 码 = 值; return true; }
            return false;
        }

        /// <summary> 键码的脚本写法(诊断 / 报错文案用):有名字给名字,没名字给十进制数 </summary>
        public static string 一句话(int 码)
        {
            string 名 = 名字(码);
            return 名.Length > 0 ? 名 : 码.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary> 去掉前缀(`字母A` → `A`);前缀不匹配或后面没东西都给 false </summary>
        private static bool 去前缀(string 词, string 前缀, out string 余)
        {
            余 = "";
            if (词.Length <= 前缀.Length) return false;
            if (!词.StartsWith(前缀, StringComparison.OrdinalIgnoreCase)) return false;
            余 = 词[前缀.Length..];
            return true;
        }
    }

    /// <summary> **事件脚本写错了**:带行号、说人话、给改法(与编译错误同一套规矩) </summary>
    public sealed class 脚本错误C : Exception
    {
        /// <summary> 出错的行号(1 起;整份脚本的问题 —— 比如少 `帧数` —— 给 0)</summary>
        public int 行号 { get; }

        public 脚本错误C(int 行号, string 说明)
            : base(行号 > 0 ? $"事件脚本 第 {行号} 行:{说明}" : $"事件脚本:{说明}")
            => this.行号 = 行号;
    }

    /// <summary>
    /// **事件脚本**(R1-6 批 2):一份**写死的输入** —— 第几帧投哪个事件。给确定性测试用(硬规矩 6 不许开窗)。
    ///
    /// 格式(一行一条,`#` 或 `//` 起头是注释,空行不算):
    ///
    /// <code>
    /// 帧数 4                  # 这次要跑满多少帧(必写;到第 4 帧开始时宿主投一个「关窗请求」)
    /// 帧 0 按键 回车           # 第 0 帧开始(= 打开窗口之后)投一个键盘按下
    /// 帧 1 移动 100 60        # 鼠标移动(x y)
    /// 帧 2 按下 鼠标左键 10 40 # 鼠标键按下(键码 x y)
    /// 帧 2 抬起 鼠标左键 10 40 # 鼠标键抬起
    /// 帧 3 按键 字母A
    /// </code>
    ///
    /// **帧号怎么算**:`打开窗口` 之后是第 0 帧;每 `刷新()` 一次提交一帧、进入下一帧。
    /// 所以「第 k 帧开始」= `打开窗口` 之后(k = 0)或第 k 次 `刷新()` 之后 —— 这一条是确定性的根:
    /// 同一份源码 + 同一份脚本,每次跑出来的事件顺序与帧号**逐字节一样**。
    ///
    /// 脚本从哪儿来(两条都走同一个解析器):
    /// · **标准工程 / 自检**:工程目录里那一份 `事件脚本.txt`(`自检` 的 `运行带输出` 自动认);
    /// · **命令行**:`hbcc 主程序.hb --run --事件脚本 事件脚本.txt`。
    ///
    /// 桌面上真跑不用它:事件来自真窗口(见 `图形宿主\桌面壳C`)。
    /// </summary>
    public sealed class 事件脚本C
    {
        /// <summary> 脚本里的一条事件(帧号 + `取事件` 那四个字段)</summary>
        public readonly record struct 脚本事件(int 帧, int 类型, int 键码, int x, int y);

        /// <summary> 标准工程里那份脚本的文件名(与 `.hbp` 同一个目录)</summary>
        public const string 文件名 = "事件脚本.txt";

        /// <summary> 这份脚本要跑满多少帧(必写;也是"第几帧开始投关窗请求"的界线)</summary>
        public int 帧数 { get; }

        /// <summary>
        /// H39-3:这份脚本的**原文**(解析进来的那份文本)。
        ///
        /// 为什么留着:H39 把"跑程序"搬进子进程之后,脚本得由**真正跑程序的那一侧**挂上
        /// (`--事件脚本` + `--跨进程` 那条腿)—— 帧 0 投放 / 跑满帧数投关窗请求 / 帧数宽限那道护栏
        /// 全在 `图形宿主C` 里,而它在子进程;原文随装载帧过去、由那边用**同一个解析器**再解一遍,
        /// 于是"进程内 / 跨进程"只有一份语义实现(硬规矩:同一件事只留一处口径)。
        /// </summary>
        public string 原文 { get; }

        /// <summary> 脚本里那条"最多让程序多跑几帧"的宽限:第 `帧数` 帧起投关窗请求,再刷这么多帧还不理就报错 </summary>
        public const int 帧数宽限 = 4;

        private readonly 脚本事件[] _事件表;
        private readonly 脚本事件[][] _每帧;

        private 事件脚本C(int 帧数, 脚本事件[] 事件表, string 原文)
        {
            this.帧数 = 帧数;
            this.原文 = 原文 ?? "";
            _事件表 = 事件表;
            _每帧 = new 脚本事件[帧数][];
            for (int 帧 = 0; 帧 < 帧数; 帧++) _每帧[帧] = [.. 事件表.Where(x => x.帧 == 帧)];
        }

        /// <summary> 脚本里所有事件(按帧号排序;自检与诊断看它)</summary>
        public IReadOnlyList<脚本事件> 事件表 => _事件表;

        /// <summary> 第 `帧号` 帧开始时该投的事件(超出范围给空表)</summary>
        public 脚本事件[] 取第几帧(int 帧号) => 帧号 >= 0 && 帧号 < _每帧.Length ? _每帧[帧号] : [];

        /// <summary> 解析一份脚本;写错了一律抛 `脚本错误C`(带行号 + 说人话 + 给改法)</summary>
        public static 事件脚本C 解析(string 文本)
        {
            var 事件 = new List<脚本事件>();
            int 帧数 = 0;
            int 行号 = 0;
            foreach (string 原行 in 文本.Replace("\r\n", "\n").Split('\n'))
            {
                行号++;
                string 行 = 去注释(原行).Trim();
                if (行.Length == 0) continue;
                string[] 段 = 行.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                string 头 = 段[0];

                // ---- 帧数 N ----
                if (头 is "帧数" || string.Equals(头, "frames", StringComparison.OrdinalIgnoreCase))
                {
                    if (帧数 > 0)
                        throw new 脚本错误C(行号, "`帧数` 写了两遍。办法:整份脚本只留开头那一行 `帧数 N`");
                    if (段.Length != 2 || !int.TryParse(段[1], NumberStyles.None, CultureInfo.InvariantCulture, out 帧数) || 帧数 <= 0)
                        throw new 脚本错误C(行号, $"`帧数` 后面要跟一个大于 0 的整数(现在写的是 " +
                            $"`{行}`)。办法:写成 `帧数 4` 这样 —— 意思是这次跑满 4 帧");
                    continue;
                }

                // ---- 帧 K 类型 [参数…] ----
                if (头 is "帧" || string.Equals(头, "frame", StringComparison.OrdinalIgnoreCase))
                {
                    if (段.Length < 3)
                        throw new 脚本错误C(行号, $"这一行少东西(现在写的是 `{行}`)。" +
                            $"办法:`帧 0 按键 回车` 这样 —— 帧号 + 事件类型 + 参数");
                    if (!int.TryParse(段[1], NumberStyles.None, CultureInfo.InvariantCulture, out int 帧号) || 帧号 < 0)
                        throw new 脚本错误C(行号, $"帧号要写 0 或更大的整数(现在写的是 `{段[1]}`)。" +
                            $"办法:第 0 帧 = 打开窗口之后,每 `刷新()` 一次进下一帧");
                    if (!图形事件C.认事件名(段[2], out int 类型))
                        throw new 脚本错误C(行号, $"认不出这个事件类型:`{段[2]}`。" +
                            $"办法:写 关窗 / 按键 / 移动 / 按下 / 抬起(英文 close / key / move / down / up 也认)");
                    string[] 参数 = 段[3..];
                    事件.Add(解析一条(行号, 行, 帧号, 类型, 参数));
                    continue;
                }

                throw new 脚本错误C(行号, $"认不出这一行:`{行}`。" +
                    $"办法:每行要么是 `帧数 N`,要么是 `帧 帧号 事件类型 [参数…]`(`#` 或 `//` 起头是注释)");
            }

            if (帧数 <= 0)
                throw new 脚本错误C(0, "少一行 `帧数 N` —— 不写就不知道这次要跑满多少帧。" +
                    "办法:开头写一行 `帧数 4`(意思是跑满 4 帧)");
            foreach (var 条 in 事件)
                if (条.帧 >= 帧数)
                    throw new 脚本错误C(0, $"脚本里有一条事件排在第 {条.帧} 帧,但 `帧数` 只写到 {帧数} —— " +
                        $"那一帧永远不会到(事件永远不会投出来)。办法:把 `帧数` 改大,或者把这条挪到前面的帧");
            return new 事件脚本C(帧数, [.. 事件.OrderBy(x => x.帧)], 文本);
        }

        /// <summary> 第 `帧号` 帧、类型 `类型` 的一条事件,按类型的参数个数把剩下的词解析出来 </summary>
        private static 脚本事件 解析一条(int 行号, string 行, int 帧号, int 类型, string[] 参数)
        {
            switch (类型)
            {
                case 图形事件C.关窗:
                    if (参数.Length != 0)
                        throw new 脚本错误C(行号, $"`关窗` 后面不用跟参数(现在写的是 `{行}`)。办法:`帧 3 关窗`");
                    return new 脚本事件(帧号, 类型, 0, 0, 0);

                case 图形事件C.按键:
                    if (参数.Length != 1)
                        throw new 脚本错误C(行号, $"`按键` 要一个键名(现在写的是 `{行}`)。办法:`帧 0 按键 回车`");
                    return new 脚本事件(帧号, 类型, 键码(行号, 参数[0]), 0, 0);

                case 图形事件C.移动:
                    if (参数.Length != 2)
                        throw new 脚本错误C(行号, $"`移动` 要两个坐标 x y(现在写的是 `{行}`)。办法:`帧 1 移动 100 60`");
                    return new 脚本事件(帧号, 类型, 0, 坐标(行号, 参数[0], "x"), 坐标(行号, 参数[1], "y"));

                default:   // 按下 / 抬起
                    if (参数.Length != 3)
                        throw new 脚本错误C(行号, $"`{图形事件C.名字(类型)}` 要键码 + 两个坐标(现在写的是 `{行}`)。" +
                            $"办法:`帧 2 {(类型 == 图形事件C.按下 ? "按下" : "抬起")} 鼠标左键 10 40`");
                    return new 脚本事件(帧号, 类型, 键码(行号, 参数[0]),
                        坐标(行号, 参数[1], "x"), 坐标(行号, 参数[2], "y"));
            }
        }

        private static int 键码(int 行号, string 词)
        {
            if (!图形键C.认键名(词, out int 码))
                throw new 脚本错误C(行号, $"认不出这个键名:`{词}`。" +
                    $"办法:写 `回车` / `空格` / `左` / `字母A` / `数字5` / `F3` / `鼠标左键` 这类名字," +
                    $"也可以直接写键码的数字(键码表见帮助《上手》第25节)");
            return 码;
        }

        private static int 坐标(int 行号, string 词, string 名)
        {
            if (!int.TryParse(词, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int 值))
                throw new 脚本错误C(行号, $"{名} 要写整数(现在写的是 `{词}`)。办法:`帧 1 移动 100 60`");
            return 值;
        }

        /// <summary> 去掉行尾注释(`#` 或 `//` 起头) </summary>
        private static string 去注释(string 行)
        {
            int 井 = 行.IndexOf('#');
            int 斜 = 行.IndexOf("//", StringComparison.Ordinal);
            int 切 = 井 < 0 ? 斜 : 斜 < 0 ? 井 : Math.Min(井, 斜);
            return 切 < 0 ? 行 : 行[..切];
        }

        /// <summary> 从文件读一份脚本(找不到文件给说人话的错) </summary>
        public static 事件脚本C 读文件(string 路径)
        {
            if (!File.Exists(路径))
                throw new 脚本错误C(0, $"找不到事件脚本文件 {路径}。办法:检查路径,或者把 `帧数 N` 那一份脚本放过去");
            return 解析(File.ReadAllText(路径));
        }

        /// <summary> 工程目录里那一份标准脚本的路径(有就给;**没有给 null** —— 没有脚本不是错,只是没有输入) </summary>
        public static string? 找脚本(string? 工程目录)
        {
            if (string.IsNullOrEmpty(工程目录)) return null;
            string 路径 = Path.Combine(工程目录, 文件名);
            return File.Exists(路径) ? 路径 : null;
        }

        /// <summary> 一句话(诊断 / 自检用):`帧数 4;事件 5 条` </summary>
        public string 一句话() =>
            $"帧数 {帧数};事件 {_事件表.Length} 条(" + string.Join(" / ",
                _事件表.Select(x => $"帧{x.帧} {图形事件C.名字(x.类型)}" +
                    (x.类型 == 图形事件C.移动 || x.类型 == 图形事件C.按下 || x.类型 == 图形事件C.抬起
                        ? $" {图形键C.一句话(x.键码)} {x.x} {x.y}" : x.类型 == 图形事件C.按键 ? $" {图形键C.一句话(x.键码)}" : ""))) + ")";
    }
}
