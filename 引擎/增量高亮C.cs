using System.Diagnostics;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **逐行高亮的缓存**(C7 = P27:大文件流畅度)。为什么要有它:
    /// 编辑器上色是"一行一行问引擎"的,滚动、换主题、重新画一遍都会把**看得见的那几十行**再问一次 ——
    /// 行没变就没必要重扫。这里按 `(行文本, 这一行的注释区间, 名字集版本)` 缓存扫描结果,
    /// 并把**扫描行数 / 命中行数**记下来 —— 这就是"不是感觉快了"的那个可断言指标
    /// (自检里钉着:"同一份文本扫两遍,第二遍扫描 0 行")。
    ///
    /// 失效规则(四条,少一条就会上错色):
    ///   · 行文本变了 → 这一行 cache miss(按行文本做键,别的行照样命中);
    ///   · **名字集换了版本**(声明过的变量/结构名变了)→ 整表清空(变量与结构的上色靠它);
    ///   · **这一行的注释区间变了**(比如上面新开了一个 `/* … */`)→ 这一行 miss(注释是最高档色,不能拿旧结果);
    ///   · **这一行的花括号层号签名变了**(C16:上面几行增删了 `{` / `}`)→ 这一行 miss(括号颜色跟层号走)。
    /// </summary>
    public sealed class 增量高亮C
    {
        private readonly Dictionary<(string 行, string 注释, object? 版本, string 花括号), List<高亮位C>> _缓存 = [];
        private readonly Queue<(string 行, string 注释, object? 版本, string 花括号)> _顺序 = new();
        private readonly int _上限;

        /// <summary>
        /// 上限:按行缓存,大文件也不至于把内存吃光。**满了按"最早进来的先走"腾位置(FIFO),不清空整表** ——
        /// 清空的话"两万行的文件整篇扫一遍"正好把表填满,下一次扫描第一行就把它全丢了(踩过:再扫一遍 0 命中的断言直接红)。
        /// </summary>
        public const int 默认上限 = 50000;

        public 增量高亮C(int 上限 = 默认上限) => _上限 = Math.Max(16, 上限);

        /// <summary> 真的去扫了行(冷扫)的行数 </summary>
        public int 扫描行数 { get; private set; }

        /// <summary> 直接用缓存、没扫的行数 </summary>
        public int 命中行数 { get; private set; }

        public int 缓存条数 => _缓存.Count;

        public void 重置统计() { 扫描行数 = 0; 命中行数 = 0; }

        public void 清空()
        {
            _缓存.Clear();
            _顺序.Clear();
            重置统计();
        }

        /// <summary>
        /// 取这一行的高亮位。`名字版本` 请传"这一版名字集/文档版本"的对象(引用变了就整表失效)——
        /// 编辑器传的是 AvalonEdit 的文档版本对象,自检里随便传一个每轮新建的对象即可。
        /// `花括号签名`(C16)是"这一行花括号层号"的紧凑签名:它变了说明**上面几行动了**,
        /// 这一行即使一个字没改也要重算(不然 `{}` 的颜色会留在旧层上)。
        /// </summary>
        public List<高亮位C> 取行片段(string 行, 配色名字集C? 名字集,
            IReadOnlyList<(int 开始, int 结束)>? 注释区 = null, object? 名字版本 = null, string 花括号签名 = "")
        {
            行 ??= "";
            string 注释键 = 注释签名(注释区);
            var 键 = (行, 注释键, 名字版本, 花括号签名 ?? "");
            if (_缓存.TryGetValue(键, out var 已有))
            {
                命中行数++;
                return 已有;
            }
            if (_缓存.Count >= _上限) 腾位置();
            扫描行数++;
            var 结果 = 高亮扫描C.扫一行(行, 名字集, 注释区);
            _缓存[键] = 结果;
            _顺序.Enqueue(键);
            return 结果;
        }

        /// <summary> 满了:把最早进来的那几行请出去(一次腾 1/8,不整表清空 —— 大文件里"扫一次就全丢"最亏)</summary>
        private void 腾位置()
        {
            int 要腾 = Math.Max(1, _上限 / 8);
            while (要腾-- > 0 && _顺序.Count > 0) _缓存.Remove(_顺序.Dequeue());
        }

        private static string 注释签名(IReadOnlyList<(int 开始, int 结束)>? 注释区)
        {
            if (注释区 == null || 注释区.Count == 0) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var (开始, 结束) in 注释区) sb.Append(开始).Append('-').Append(结束).Append(';');
            return sb.ToString();
        }

        /// <summary>
        /// 把整篇文本扫一遍(冷缓存),返回"扫了多少行 + 花了多久" ——
        /// 给自检/命令行当**可断言的性能数据**用(大文件到底多贵,拿数字说话)。
        /// </summary>
        public (int 行数, double 毫秒) 冷扫全篇(string 文本, 配色名字集C? 名字集 = null)
        {
            清空();
            string 归一 = (文本 ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            var 行表 = 归一.Split('\n');
            var 注释 = new 注释行查找(归一);
            var 秒表 = Stopwatch.StartNew();
            for (int i = 0; i < 行表.Length; i++)
                取行片段(行表[i], 名字集, 注释.取(i), 名字版本: "冷扫");
            秒表.Stop();
            return (行表.Length, 秒表.Elapsed.TotalMilliseconds);
        }

        /// <summary> 同一份文本再扫一遍(应该**一行都不扫**,全命中)</summary>
        public (int 扫描, int 命中, double 毫秒) 再扫全篇(string 文本, 配色名字集C? 名字集 = null)
        {
            string 归一 = (文本 ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            var 行表 = 归一.Split('\n');
            var 注释 = new 注释行查找(归一);
            重置统计();
            var 秒表 = Stopwatch.StartNew();
            for (int i = 0; i < 行表.Length; i++)
                取行片段(行表[i], 名字集, 注释.取(i), 名字版本: "冷扫");
            秒表.Stop();
            return (扫描行数, 命中行数, 秒表.Elapsed.TotalMilliseconds);
        }

        /// <summary>
        /// "第 i 行落在哪些注释区间里"的**按行推进**查找器:注释区间是按出现顺序排好的,
        /// 顺着一行行问就只要往后挪游标 —— 每行都从头扫一遍全部区间会让"整篇"变成 O(行数 × 区间数)(实测能差上千倍)。
        /// </summary>
        private sealed class 注释行查找
        {
            private readonly List<(int 开始, int 结束)> _区;
            private readonly List<int> _行起 = [];
            private int _游标;

            public 注释行查找(string 归一文本)
            {
                _区 = 代码解析器.取注释区(归一文本);
                for (int i = 0, p = 0; i <= 归一文本.Length; i++)
                {
                    if (i == 0) _行起.Add(0);
                    if (i < 归一文本.Length && 归一文本[i] == '\n') { p = i + 1; _行起.Add(p); }
                }
            }

            public List<(int 开始, int 结束)> 取(int 行号)
            {
                var 结果 = new List<(int, int)>();
                if (行号 < 0 || 行号 >= _行起.Count) return 结果;
                int 行起 = _行起[行号];
                int 行止 = 行号 + 1 < _行起.Count ? _行起[行号 + 1] - 1 : int.MaxValue;   // 去掉换行本身
                while (_游标 < _区.Count && _区[_游标].结束 <= 行起) _游标++;
                for (int i = _游标; i < _区.Count; i++)
                {
                    var (开始, 结束) = _区[i];
                    if (开始 >= 行止) break;
                    int 交起 = Math.Max(开始, 行起);
                    int 交止 = Math.Min(结束, 行止);
                    if (交止 > 交起) 结果.Add((交起 - 行起, 交止 - 行起));
                }
                return 结果;
            }
        }
    }
}
