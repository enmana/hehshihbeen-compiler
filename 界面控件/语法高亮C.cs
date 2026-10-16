using hehshihbeen_compiler.配置;
using System.Windows.Media;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// 编辑器的着色**外观层**:把"哪个词类什么颜色"变成画刷,按引擎侧扫出来的位置涂色。
    ///
    /// **词类与亮/暗默认色的唯一事实源是 `配色词类C`**(引擎侧,命令行自检也读得到);
    /// **"哪一段属于哪一类"的判定在 `高亮扫描C`**(同一份规则,编辑器与自检共用)。
    /// 这个类只干两件事:①把 `#AARRGGBB` 转成画刷(按当前主题);②把扫出来的位置交给 AvalonEdit。
    ///
    /// **C16 花括号彩虹色**:7 档色值由引擎 `花括号层号C` 给(`#RRGGBB`,饱和度压到 2/3),
    /// 这里只转画刷 —— 层号与配色规则同样留引擎(命令行自检才钉得住)。
    /// **H8 起这 7 档能在选项页里逐档改**(存在 `选项.json` 的 `花括号色`):引擎那份是**默认**,
    /// 用户调过的走 `花括号层号C.规范色表`(写坏 / 缺档一律落回默认),见 `刷新花括号色`。
    /// </summary>
    public partial class 语法高亮C
    {
        /// <summary> 词类键 → 画刷(按当前主题填;没上过主题就按"亮"那套默认值现算) </summary>
        private readonly Dictionary<string, Brush> _刷子 = new(StringComparer.Ordinal);

        /// <summary> C16:花括号 7 档画刷(色值来自 `花括号层号C.取色` / 选项里调过的那份,与主题无关) </summary>
        private readonly Dictionary<int, Brush> _花括号刷 = new();

        /// <summary> C16-3:缩进导引线的 7 档画刷(同一套色相 + `花括号层号C.导引线透明度`) </summary>
        private readonly Dictionary<int, Brush> _花括号线刷 = new();

        /// <summary>
        /// H8:当前生效的 7 档彩虹色(`#RRGGBB`,下标 = 色档)。取 `配置.选项.当前.花括号色` 规范过的那份 ——
        /// 用户没调过就逐档等于 `花括号层号C.色表`。**与主题无关**(亮/暗两套共用这 7 档,与 C16 当初一样)。
        /// </summary>
        private string[] _花括号色 = 花括号层号C.规范色表(配置.选项.当前.花括号色);

        /// <summary> 当前主题(选项页预览时会换成草稿主题) </summary>
        private 主题设置? _主题;

        /// <summary> 按主题刷新全部词类画刷 </summary>
        public void 应用主题(主题设置 主题)
        {
            _主题 = 主题;
            _刷子.Clear();
            foreach (var 类 in 配色词类C.全部项)
                _刷子[类.键] = 主题设置.转刷(主题.取词类色(类.键));
        }

        /// <summary>
        /// H8:选项页里改过 7 档彩虹色之后重取一遍(选项里那份 → 规范 → 清两个画刷缓存)。
        /// 缓存必须一起清 —— 不清的话刷子还是按老色值冻住的那几个,屏上一点变化都看不见。
        /// </summary>
        public void 刷新花括号色()
        {
            _花括号色 = 花括号层号C.规范色表(配置.选项.当前.花括号色);
            _花括号刷.Clear();
            _花括号线刷.Clear();
        }

        /// <summary> 取某一词类的画刷 </summary>
        public Brush 取刷(string 词类键)
        {
            if (_刷子.TryGetValue(词类键, out var 刷)) return 刷;
            bool 暗 = _主题?.是暗主题 ?? false;
            return 主题设置.转刷(配色词类C.取默认色(词类键, 暗));
        }

        /// <summary> C16:花括号色档(0 基,自动 7 循环)→ 画刷(H8 起色值可能是选项页里调过的) </summary>
        public Brush 取花括号刷(int 色档)
        {
            int 归一 = ((色档 % 7) + 7) % 7;
            if (_花括号刷.TryGetValue(归一, out var 刷)) return 刷;
            var 新刷 = 主题设置.转刷(_花括号色[归一]);
            新刷.Freeze();
            _花括号刷[归一] = 新刷;
            return 新刷;
        }

        /// <summary> 普通文本的颜色 </summary>
        public Brush 默认颜色 => 取刷(配色词类C.默认);

        /// <summary>
        /// C16-3:缩进导引线的画刷 —— **跟着括号那一层的颜色**(同一套 7 档色相),
        /// 只把透明度压到 `花括号层号C.导引线透明度`(线在文字底下,太实会盖住代码、太淡又看不见)。
        /// 色值来自 `_花括号色`(H8 起选项页里能逐档改,规范过的那份)。
        /// </summary>
        public Brush 取花括号线刷(int 色档)
        {
            int 归一 = ((色档 % 7) + 7) % 7;
            if (_花括号线刷.TryGetValue(归一, out var 刷)) return 刷;
            string 色 = _花括号色[归一];                                        // `#RRGGBB`
            var 新刷 = 主题设置.转刷($"#{花括号层号C.导引线透明度:X2}{色[1..]}");
            新刷.Freeze();
            _花括号线刷[归一] = 新刷;
            return 新刷;
        }
    }
}
