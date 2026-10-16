namespace hehshihbeen_compiler
{
    /// <summary>
    /// 一行源码里"一次调用"占的那一段字符(起含、止不含;列号 0 基) + 它在调用链上的层号。
    /// 界面上拿它按层叠色画灰条(C15),所以只算**位置**,不碰界面。
    /// </summary>
    public readonly record struct 调用段C(int 行, int 起, int 止, int 层号);

    /// <summary>
    /// **调用跨度**:把"调用链上的每一帧"落到它那一行的**字符区间**上(C15)。
    ///
    /// 为什么要落区间:`调用栈标记C` 记的是"调用处**地址**",快照把它翻成**行号** —— 同一行里
    /// 层层往外调(`测试输出(打招呼(叫号(2)));`)会被按行去重成一行,看着只有一层。要按层叠灰,
    /// 就得知道每一层在那行里占哪几个字。
    ///
    /// 怎么落:编译器只给行、不给列,所以这里**从那一行的正文里推** ——
    /// 扫出所有"名字 + 配对括号"的调用段(`名字(…)`,字符串与 `//` 注释里的括号不算),
    /// 再按**名字 + 从外到内**的顺序与帧对上;段与段的嵌套关系就是层与层的关系。
    /// 对不上的帧直接丢掉(宁可少画一层,也不画错一段)。
    ///
    /// 纯函数:正文由调用方按行号喂进来,所以自检能直接喂几行假正文逐条钉住。
    /// </summary>
    public static class 调用跨度C
    {
        /// <summary>
        /// 算一行的调用跨度表。
        /// </summary>
        /// <param name="帧表">调用链(从最外层到当前这一层;`调试步进结果C.调用帧` 就是这个顺序)</param>
        /// <param name="取行文本">行号(1 基)→ 那一行的正文;取不到返回 null 就跳过那一行</param>
        /// <param name="只看文件">只算这个文件里的帧(空 = 都算)。层号仍按整条链算,别的文件只是不画</param>
        public static List<调用段C> 算(IReadOnlyList<调用帧C>? 帧表, Func<int, string?>? 取行文本, string? 只看文件 = null)
        {
            var 结果 = new List<调用段C>();
            if (帧表 == null || 帧表.Count == 0 || 取行文本 == null) return 结果;

            var 按行 = new Dictionary<int, List<调用帧C>>();
            foreach (var 帧 in 帧表)
            {
                if (帧 == null || 帧.行 <= 0 || 帧.名.Length == 0) continue;
                if (!string.IsNullOrEmpty(只看文件) &&
                    !string.Equals(帧.文件, 只看文件, StringComparison.OrdinalIgnoreCase)) continue;
                if (!按行.TryGetValue(帧.行, out var 一行里的帧)) { 一行里的帧 = []; 按行[帧.行] = 一行里的帧; }
                一行里的帧.Add(帧);
            }

            foreach (var (行, 一行里的帧) in 按行)
            {
                string? 正文 = 取行文本(行);
                if (string.IsNullOrEmpty(正文)) continue;
                var 段表 = new List<(int 起, int 止, string 名, int 深度)>();
                扫一段(正文, 0, 正文.Length, 0, 段表);
                if (段表.Count == 0) continue;
                // 先浅后深(同深按从左到右):帧是从外到内的,配"最外面还没用过的同名段"就对了
                段表.Sort((a, b) => a.深度 != b.深度 ? a.深度.CompareTo(b.深度) : a.起.CompareTo(b.起));
                var 用过了 = new bool[段表.Count];
                foreach (var 帧 in 一行里的帧)
                {
                    int 选中 = 找同名段(段表, 用过了, 帧.名, 只要没用过的: true);
                    if (选中 >= 0) 用过了[选中] = true;
                    // 同一个调用点被递归重新进入时,一行的段只有一个 —— 这时**重复用同一段**:
                    // 按层号一层层叠着画,越内层越深(这正是叠灰要的效果)
                    else 选中 = 找同名段(段表, 用过了, 帧.名, 只要没用过的: false);
                    if (选中 < 0) continue;
                    结果.Add(new 调用段C(行, 段表[选中].起, 段表[选中].止, 帧.层号));
                }
            }

            结果.Sort((a, b) => a.层号 != b.层号 ? a.层号.CompareTo(b.层号) : a.起.CompareTo(b.起));
            return 结果;
        }

        /// <summary>
        /// 调用链层号(1 基)→ 颜色档(0 基),按档数循环。
        /// 拍板(2026-09-19):7 档 = 赤橙黄绿青蓝紫,第 8 层起回到赤色(所以画出来永远不会糊成一块黑)。
        /// </summary>
        public static int 档位(int 层号, int 档数 = 7)
        {
            if (档数 <= 0) return 0;
            int 档 = (层号 - 1) % 档数;
            return 档 < 0 ? 档 + 档数 : 档;
        }

        /// <summary>
        /// 在段表里找第一个同名段(段表已经"先浅后深、同深从左到右"排好)。
        /// `只要没用过的` = true 时跳过已经配过帧的那些;false 时允许重复用(递归重新进入同一个调用点)。
        /// </summary>
        private static int 找同名段(List<(int 起, int 止, string 名, int 深度)> 段表, bool[] 用过了, string 名, bool 只要没用过的)
        {
            for (int i = 0; i < 段表.Count; i++)
            {
                if (只要没用过的 && 用过了[i]) continue;
                if (string.Equals(段表[i].名, 名, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        /// <summary> 扫一段正文,把"名字 + 配对括号"都记下来(递归进实参里,所以嵌套关系的深度是准的) </summary>
        private static void 扫一段(string 正文, int 起, int 止, int 深度, List<(int 起, int 止, string 名, int 深度)> 结果)
        {
            int i = 起;
            while (i < 止)
            {
                char c = 正文[i];
                if (c == '"') { i = 跳过字符串(正文, i, 止); continue; }        // 字符串里的括号不算
                if (c == '/' && i + 1 < 止 && 正文[i + 1] == '/') return;      // `//` 后面是注释
                if (!是名字起(c)) { i++; continue; }
                int 名起 = i;
                while (i < 止 && 是名字里(正文[i])) i++;
                string 名 = 尾名(正文[名起..i]);
                int 括号 = i;
                while (括号 < 止 && char.IsWhiteSpace(正文[括号])) 括号++;
                if (括号 >= 止 || 正文[括号] != '(') continue;
                int 闭 = 找配对右括号(正文, 括号, 止);
                if (闭 < 0) continue;
                结果.Add((名起, 闭 + 1, 名, 深度));
                扫一段(正文, 括号 + 1, 闭, 深度 + 1, 结果);
                i = 闭 + 1;
            }
        }

        private static int 找配对右括号(string 正文, int 开, int 止)
        {
            int 深 = 0;
            int i = 开;
            while (i < 止)
            {
                char c = 正文[i];
                if (c == '"') { i = 跳过字符串(正文, i, 止); continue; }
                if (c == '/' && i + 1 < 止 && 正文[i + 1] == '/') return -1;
                if (c == '(') 深++;
                else if (c == ')')
                {
                    深--;
                    if (深 == 0) return i;
                }
                i++;
            }
            return -1;
        }

        /// <summary> 跳过一段字符串字面量,返回它后面那个位置(没配对的引号就跳到段尾) </summary>
        private static int 跳过字符串(string 正文, int 起, int 止)
        {
            int i = 起 + 1;
            while (i < 止)
            {
                if (正文[i] == '\\') { i += 2; continue; }
                if (正文[i] == '"') return i + 1;
                i++;
            }
            return 止;
        }

        private static bool 是名字起(char c) => char.IsLetter(c) || c == '_' || c > 0x7F;

        private static bool 是名字里(char c) => 是名字起(c) || char.IsDigit(c) || c == '.';

        private static string 尾名(string 名)
        {
            int 点 = 名.LastIndexOf('.');
            return 点 >= 0 && 点 + 1 < 名.Length ? 名[(点 + 1)..] : 名;
        }
    }
}
