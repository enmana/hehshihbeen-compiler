namespace hehshihbeen_compiler
{
    /// <summary>
    /// **悬浮提示"该不该弹"的判定**(与界面无关,所以能上自检)。
    ///
    /// 要解决的那个 bug:"鼠标停在某个词上弹了提示,点它的 ✕ 关掉了,过一秒又自己弹回来" ——
    /// 关掉之后鼠标还在那个词上,编辑器那句"鼠标一动就重新数悬停"会把计时器重新起起来,
    /// 于是同一处的提示又被弹出来,用户看着就是"点关闭也关不掉"。
    ///
    /// 规则(用户要求:关掉之后不许被重新弹出来,但**同一处再悬停还得能弹**):
    ///   · **明确关掉**(✕ / Esc / 点气泡外 / 点编辑区):记住"这个位置键被关过",
    ///     在 `冷却毫秒` 之内、或者鼠标没离开这个位置键之前,**不许再弹**
    ///     (两条都要满足才放行 —— 鼠标还没走开就是"用户刚把它关掉",这时放行等于没关);
    ///   · 鼠标挪到**别的位置键**上 → 那条记忆清掉(再挪回来就能弹了,不会"关了就再也不弹");
    ///   · **自动关掉**(鼠标离开 / 滚动 / 改文本 / 切文件):不记 —— 那些是"用户没表态",不该拦住下次悬停。
    /// </summary>
    public sealed class 提示开关C
    {
        /// <summary> 明确关掉之后,同一个位置键至少安静这么久(毫秒);鼠标离开这个键就提前解除 </summary>
        public int 冷却毫秒 { get; set; } = 800;

        private string? _关过的键;
        private DateTime _关掉时刻 = DateTime.MinValue;
        private string? _上次悬停键;

        /// <summary>
        /// 现在这一处能不能弹提示。两条都要满足:**鼠标已经离开过这一处**、且冷却时间已过。
        /// 只看时间是不够的 —— 关掉之后鼠标还赖在同一个词上(真人握鼠标必然有细微移动),
        /// 鼠标事件会重新数悬停,冷却一过提示就又冒出来,用户看到的还是"点关闭没用"。
        /// </summary>
        public bool 可以弹(string? 位置键, DateTime 现在)
        {
            if (string.IsNullOrEmpty(位置键)) return false;
            if (_关过的键 == null || _关过的键 != 位置键) return true;
            if (_上次悬停键 == 位置键) return false;   // 鼠标一直没离开关掉的那一处:冷却过了也不弹
            return (现在 - _关掉时刻).TotalMilliseconds >= 冷却毫秒;
        }

        /// <summary> 记一笔"弹过"(目前只用来记位置,方便调试/自检) </summary>
        public void 记弹出(string? 位置键, DateTime 现在)
        {
            _上次悬停键 = 位置键;
            _ = 现在;
        }

        /// <summary> **明确关掉**:这个位置键在冷却期内不许再弹(鼠标离开它才解除) </summary>
        public void 记关掉(string? 位置键, DateTime 现在)
        {
            if (string.IsNullOrEmpty(位置键)) { _关过的键 = null; return; }
            _关过的键 = 位置键;
            _关掉时刻 = 现在;
        }

        /// <summary>
        /// 鼠标现在悬停在 `位置键` 上。挪到**别的键**上就把"关过"的记忆清掉
        /// (所以"关掉 → 挪开 → 再挪回来"是能重新弹的;一直停在原处则一直不弹)。
        /// </summary>
        public void 记鼠标停在(string? 位置键)
        {
            if (string.IsNullOrEmpty(位置键)) return;
            if (_上次悬停键 != null && _上次悬停键 != 位置键) _关过的键 = null;
            _上次悬停键 = 位置键;
        }

        /// <summary> 鼠标离开编辑器/气泡了(不记"关过",只清悬停键) </summary>
        public void 记鼠标离开() => _上次悬停键 = null;

        /// <summary> 清空全部记忆(切文件/换主题这类"整页换掉"的场合用) </summary>
        public void 清空()
        {
            _关过的键 = null;
            _上次悬停键 = null;
            _关掉时刻 = DateTime.MinValue;
        }

        /// <summary> 调试/自检用:现在记着"关过"哪个键(没有返回 null) </summary>
        public string? 关过的键 => _关过的键;
    }
}
