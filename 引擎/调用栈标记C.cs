namespace hehshihbeen_compiler
{
    /// <summary>
    /// 单步调试时的"调用处"记帐:执行被 调用 跳到别处时,记下**调用处**的地址;
    /// 等执行回到"调用处的下一条指令"(调用返回了)再把这条撤掉。
    ///
    /// 编辑器拿这份地址表把那些行标成**淡灰** —— 跳走以后还知道"我是从哪跳过来的",找得回来。
    /// 只认"调用"这一种跳转:如果/循环 那种跳来跳去不算(不然满屏都是灰的)。
    /// </summary>
    public sealed class 调用栈标记C
    {
        private readonly List<int> _调用处 = [];
        private readonly List<int> _被调 = [];

        /// <summary> 当前还欠着的调用处地址(按调用先后排,最后一个是最近的) </summary>
        public IReadOnlyList<int> 调用处地址 => _调用处;

        /// <summary>
        /// 与 `调用处地址` 一一对应的"被调方入口地址"(C15 叠灰要靠它认出被调的是哪个函数)。
        /// 两份表同进同出,顺序永远一致;调用方按同一个下标读就行。
        /// </summary>
        public IReadOnlyList<int> 被调地址 => _被调;

        /// <summary>
        /// 走一步之后记一笔。旧指令是"调用"而且真的跳走了 → 记下旧PC(调用处)与被调方入口;
        /// 新PC 正好落在某个调用处的下一条 → 说明那层调用回来了,撤掉它。
        /// </summary>
        public void 记一步(int 旧PC, int 新PC, bool 旧指令是调用)
        {
            if (旧指令是调用 && 新PC != 旧PC + 4)
            {
                _调用处.Add(旧PC);
                _被调.Add(新PC);
            }
            while (_调用处.Count > 0 && 新PC == _调用处[^1] + 4)
            {
                _调用处.RemoveAt(_调用处.Count - 1);
                _被调.RemoveAt(_被调.Count - 1);
            }
        }

        public void 清空()
        {
            _调用处.Clear();
            _被调.Clear();
        }

        /// <summary>
        /// H39-2:**整份复原**(跨进程那条腿用):把"跑程序那一侧"记下来的两张表原样搬进来。
        /// 两张表一一对应,长度不一致就按短的那一份收(宁可少几层灰条,也不许下标错位 ——
        /// 错位的灰条比没有灰条更误导人)。
        /// </summary>
        public void 复原(IReadOnlyList<int> 调用处们, IReadOnlyList<int> 被调们)
        {
            _调用处.Clear();
            _被调.Clear();
            if (调用处们 == null || 被调们 == null) return;
            int 条数 = Math.Min(调用处们.Count, 被调们.Count);
            for (int i = 0; i < 条数; i++)
            {
                _调用处.Add(调用处们[i]);
                _被调.Add(被调们[i]);
            }
        }
    }
}
