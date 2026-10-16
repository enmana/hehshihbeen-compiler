namespace hehshihbeen_compiler
{
    /// <summary> 一个变量在源码里的"作用域区间"(行号,1 基;止 = int.MaxValue 表示"到文件尾") </summary>
    public sealed class 名字作用域区间C
    {
        public int 起 = 1;
        public int 止 = int.MaxValue;
        public bool 含(int 行) => 行 >= 起 && 行 <= 止;
    }

    /// <summary>
    /// 调试用的"一个名字":变量名 + 类型 + 在哪儿(全局槽 / 帧偏移)+ 在哪些行可见。
    /// 这是**编译期**就知道的静态信息(值要靠调试器运行时去读)。
    /// </summary>
    public sealed class 名字项C
    {
        public string 名字 = "";
        /// <summary> 类型名:字节/短整数/字符/逻辑/32位字,或者结构名 </summary>
        public string 类型名 = "";
        public int 宽度;
        /// <summary> true = 全局数据槽(位置看 槽);false = 函数帧里的局部/参数(位置看 帧偏移) </summary>
        public bool 是全局;
        /// <summary> 全局变量的槽标签(第94批起工程级编译不再加文件序号前缀 —— 内部标签本来就是顺序唯一的) </summary>
        public string 槽 = "";
        /// <summary> 局部变量所属的函数定义名(空 = 顶层) </summary>
        public string 所属函数 = "";
        /// <summary> 帧内偏移(局部/参数) </summary>
        public int 帧偏移;
        public int 声明行;
        /// <summary>
        /// 这个变量/参数声明在哪个源文件(工程级编译时一个展开器连吃多文件,调试段靠它把名字分回各自文件)。
        /// 单文件编译 = ""(那时只有一个文件,不需要分)。
        ///
        /// **口径说清(第254批)**:这里从头到尾都是"**这条语句编进了哪个调试段**" —— 被 `包含` 进来的
        /// 库里的变量/局部,这一项仍是**包含它的那一份**(调试变量面板就靠它把这些名字挂在那一段上)。
        /// 与 `函数范围` 第 4 项的口径**有意不同**:那一项要的是"这个函数**真正**声明在哪份文件里"
        /// (补全/转到定义/思维导图要按文件名筛它)。两件事不要互相借。
        /// </summary>
        public string 声明文件 = "";
        /// <summary> 它在哪一层块作用域里(null = 整个函数/整个文件都可见) </summary>
        public 名字作用域区间C? 区间;
        /// <summary> 是数组时:元素宽与个数(0 = 不是数组);列表时元素个数 = 容量 </summary>
        public int 元素宽;
        public int 元素个数;
        /// <summary> 是不是列表(名字[]):调试面板显示"现在几个 / 最多几个" </summary>
        public bool 是列表;
        /// <summary> 是不是引用(槽里装的是地址):调试面板要按"指向哪儿"来显示,不能把槽本身当值 </summary>
        public bool 是引用;
        /// <summary> 是引用时:指向的类型名(基础类型或结构名) </summary>
        public string 指向类型名 = "";
        /// <summary> 是引用时:指向的东西有多宽(读指向的值用;结构填总宽度) </summary>
        public int 指向宽;
        /// <summary> 是 `字符串` 参数(`函数 f(字符串 名字)`):调试面板按"内容"显示,而不是把槽当指针看 </summary>
        public bool 是字符串参数;
        /// <summary> 是**可写**的字符串参数(`字符串 出 名字`):槽里装的是"调用方字符串头的地址" </summary>
        public bool 出参数;

        public bool 是数组 => 元素个数 > 0 && !是列表;
        public bool 含(int 行) => 区间 == null || 区间.含(行);
    }

    /// <summary> 结构/记录的一个字段(调试器按它把结构变量展开显示;嵌套结构与数组字段也能继续往下展开) </summary>
    public sealed class 字段项C
    {
        public string 名字 = "";
        public string 类型名 = "";
        public int 宽度;
        public int 偏移;
        /// <summary> 是嵌套结构(类型名就是那个结构,调试面板可继续展开)</summary>
        public bool 是结构;
        /// <summary> 是数组字段:元素宽与个数(0 = 不是数组)</summary>
        public bool 是数组;
        public int 元素宽;
        public int 元素个数;
        /// <summary> 是 `字符串` 字段(8 字节 = 数据地址 + 容量,真正的字节在堆里):
        /// 调试面板据此把内容读出来显示,而不是显示一个堆地址 </summary>
        public bool 是字符串;
        /// <summary> 数组字段的元素是 `字符串`(每个元素 8 字节) </summary>
        public bool 元素是字符串;
        /// <summary> 是**动态列表**字段(`整数 数[]`,12 字节头:数据地址 / 长度 / 容量,数据在堆里)。
        /// 调试面板显示"现在几个",**不显示堆地址**(地址是分配器的家事,会随优化/分配顺序变)。 </summary>
        public bool 是列表;
        /// <summary> A4:字段声明时标了 `私有` / `程序集`(调试与编辑页显示成"私有字段",读法照旧)</summary>
        public bool 私有;
        public bool 程序集;
    }

    /// <summary>
    /// 一个顶层名字(函数 / 结构 / 方法容器 / 全局变量)的**可见性**(C19 补全用):
    /// 声明时写没写 `私有` / `程序集`,以及它属于哪个模块、哪个工程、哪个文件。
    ///
    /// 与编译器 `中间层C.模块.cs` 的 `校验可取用` / `校验字段可取用` **同一份数据** ——
    /// 补全要按"当前文件在哪个模块 / 哪个工程"先筛一遍,绝不弹一个写下去必然报错的名字。
    /// 同样只是旁路信息:不参与汇编、不影响机器码。
    /// </summary>
    public sealed class 可见性项C
    {
        public bool 私有;
        public bool 程序集;
        /// <summary> 声明它的模块(全名第一个点前面那一段;文件没写 `模块` = "") </summary>
        public string 声明模块 = "";
        /// <summary> 声明它的 `.hbp` 工程(`程序集` 的边界;没有工程归属 = 匿名工程,与 `公共` 等价) </summary>
        public string 声明工程 = "";
        public string 声明文件 = "";
    }

    /// <summary> 函数参数表里的一项(C19 补全的"参数提示"用) </summary>
    public sealed class 参数项C
    {
        public string 名字 = "";
        public string 类型名 = "";
        /// <summary> 写了 `出`(英文 result):这个参数把改动带回调用方 </summary>
        public bool 是出;
        /// <summary> 引用参数(数组 / 字符串 / `引用`):槽里装的是地址 </summary>
        public bool 是引用;
        /// <summary> 带长度的引用(`类型 名字[]` / `类型 名字` 的字符串参数) </summary>
        public bool 是数组;
    }

    /// <summary> 一个函数的签名(返回类型 + 参数表;C19 补全的"参数提示"用,与 `函数范围` 同一套名字口径) </summary>
    public sealed class 函数签名C
    {
        /// <summary> 返回类型词("" = 无返回值) </summary>
        public string 返回类型 = "";
        public List<参数项C> 参数 = [];
    }

    /// <summary>
    /// 调试用的"名字目录":编译器在展开时顺手导出的静态信息 ——
    /// 有哪些变量(全局/局部)、各自什么类型、在哪、在哪些行可见;有哪些结构、字段怎么排。
    /// 调试器拿它 + 运行时的内存,就能显示"变量 字 = 65(字符)"这种一眼能懂的东西。
    ///
    /// 注意:它**只是给调试用的旁路信息**,不参与汇编、不影响机器码。
    /// </summary>
    public sealed class 名字表C
    {
        /// <summary> 全部变量(全局 + 所有函数的局部/参数) </summary>
        public List<名字项C> 变量 { get; set; } = [];

        /// <summary> 结构名 → 字段表(偏移/宽度) </summary>
        public Dictionary<string, List<字段项C>> 结构 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// **结构自己的方法**(H60 步④):结构名 → 方法定义名列表(`点` → `点.到原点` …)。
        /// 补全按它给"结构变量."补出方法(来历 = 方法、插入 `名()`、光标落括号里);签名照旧在 `函数签名` 里
        /// (**不含**编译器补的 `_收方`)。与 `结构` 同一口径:只是给编辑器的旁路信息,不参与汇编、不影响机器码。
        /// </summary>
        public Dictionary<string, List<string>> 结构方法 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// **枚举名 → 成员名列表**(H68-2):`枚举 颜色 { 赤, 绿, 蓝 }` 登记一条,补全据此在 `颜色.` 后面
        /// 列出成员。与 `结构` 同一口径:只是给编辑器的旁路信息,**不参与汇编、不影响机器码**
        /// (枚举不占存储:成员是编译期常量,机器码里就是一个立即数)。
        /// </summary>
        public Dictionary<string, List<string>> 枚举 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 枚举成员全名(`颜色.绿`)→ 值(H68-2) —— 补全的说明文字里要写出"= 1"这种 </summary>
        public Dictionary<string, long> 枚举成员 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// **方法容器的名字**(H60 步①起:`_结构表` / `结构` 里除了那个内部结构名 `_方法_计数器`,
        /// 还多一条**别名**——容器名自己)。它只是"这个源名字同时是一个类型名"的标记,
        /// 补全据此把候选说明写成"方法容器"而不是"结构"(真名优先:用户写的是 `静态 方法 计数器(...)`)。
        /// 与 `结构` 同一口径:只是给编辑器的旁路信息,不参与汇编、不影响机器码。
        /// </summary>
        public HashSet<string> 容器类型 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 函数定义名 → 它在源码里占的行范围(调试器用它判断"现在停在哪个函数里")。
        /// 第 4 项 = **真正声明这个函数的那一份文件**(工程级编译时行号是**各文件自己的**,不带文件就分不清是哪个函数)——
        /// 被 `包含` 进来的库函数记的就是**库文件那一份**(行号本来就是库里的行号;见 `函数段文件`)。
        /// </summary>
        public List<(string 函数, int 起, int 止, string 文件)> 函数范围 { get; set; } = [];

        /// <summary>
        /// 函数定义名 → **这段码编进了哪个调试段**(只有"段 ≠ 声明文件"的才记:被 `包含` 进来的库函数)。
        ///
        /// 为什么要两张表:`包含` 是**文本原地展开** —— 库里那个函数声明在库文件里(`函数范围` 第 4 项,
        /// 行号是库里的),可它的码住在**包含它的那一份**的调试段里。调试段的名字表要按这张表分
        /// (不记它,库函数会从每一段里掉出去:`调用关系扫描C` 的"标签 ↔ 定义名"、`调试会话C` 调用栈的层名
        /// 都要从调试段的名字表里拿)。查不到就按 `函数范围` 第 4 项算。
        /// </summary>
        public Dictionary<string, string> 函数段文件 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 函数名 → 它的**花括号在哪两行**(C14 单步用):进函数那一步黄条停在 `{` 行、出函数停在 `}` 行。
        /// 第 2/3 项 = 左/右花括号的源行号,第 4 项 = 定义在哪个源文件(与 `函数范围` 同一套口径)。
        ///
        /// 为什么不拿 `函数范围.起` 凑:`起` 是**声明行**(`函数 名(…)` 那一行),而 `{` 可能另起一行 ——
        /// 单步要停的是 `{` 那一行,两者不是一回事(用户 2026-09-19 的 C14 要求里给的就是花括号另起一行的写法)。
        /// 它同样只是给调试用的旁路信息,不参与汇编、不影响机器码。
        /// </summary>
        public List<(string 函数, int 左行, int 右行, string 文件)> 函数边界 { get; set; } = [];

        /// <summary>
        /// 这张表是**给哪一份文件**建的(调试段那一份:`汇编器C.拆名字表` 摆的值;工程级合并表 = 空串)。
        /// 它只回答一件事:上面那两张函数表**可能同时装着两份文件的函数**(被 `包含` 进来的库函数也在里面),
        /// 而"第 N 行落在哪个函数里"这种问法**只能拿本文件的函数来答** —— 库里的第 57 行不是本文件的第 57 行。
        /// 空串 = 不按文件筛(单文件编译 / 工程级表:那时只有一份文件的行号口径)。
        /// </summary>
        public string 本段文件 { get; set; } = "";

        /// <summary>
        /// 顶层名字 → 可见性(C19):函数 / 结构 / 方法容器 / 全局变量声明时顺手记一份。
        /// 名字的写法与 `函数范围` / `结构` 一致(带模块前缀的写全名)。
        /// </summary>
        public Dictionary<string, 可见性项C> 可见性 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 函数定义名 → 签名(参数表 + 返回类型;C19 的"参数提示") </summary>
        public Dictionary<string, 函数签名C> 函数签名 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 源文件 → 它的模块(补全要判"这个文件在哪个模块里";没写 `模块` = 空串) </summary>
        public Dictionary<string, string> 文件模块 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 源文件 → 它的 `.hbp` 工程(判 `程序集` 用;没有工程归属 = 空串) </summary>
        public Dictionary<string, string> 文件工程 { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 工程里出现过的模块名(判"点前面的那一段是模块还是方法容器"用) </summary>
        public List<string> 模块名 { get; set; } = [];

        /// <summary> 这个名字的可见性(没登记返回 null = 按"公开、随便用"算) </summary>
        public 可见性项C? 查可见性(string 全名) =>
            !string.IsNullOrEmpty(全名) && 可见性.TryGetValue(全名, out var 项) ? 项 : null;

        /// <summary> 当前停在"第 行"、且在这个函数里时,能看见哪些变量(全局的 + 这个函数里作用域盖住这一行的) </summary>
        public List<名字项C> 可见变量(int 行, string? 当前函数)
        {
            var 结果 = new List<名字项C>();
            foreach (var 项 in 变量)
            {
                if (!项.含(行)) continue;
                if (项.是全局) { 结果.Add(项); continue; }
                if (当前函数 == null || !string.Equals(项.所属函数, 当前函数, StringComparison.OrdinalIgnoreCase)) continue;
                结果.Add(项);
            }
            return 结果;
        }

        /// <summary>
        /// 这一行落在哪个函数里(不在任何函数里返回 null)。
        /// **只有声明在本文件(`本段文件`)里的函数才算数**:表里同时装着被 `包含` 进来的库函数
        /// (标签 ↔ 定义名要用),可它们的行号是**库文件自己的** —— 拿库里的第 57 行去认本文件的第 57 行
        /// 就会"张冠李戴"(H33 第252批实测的那一类对不上)。`本段文件` 为空(单文件 / 工程级表)时照旧全看。
        /// </summary>
        public string? 行所属函数(int 行)
        {
            foreach (var (函数, 起, 止, 文件) in 函数范围)
            {
                if (本段文件.Length > 0 && 文件.Length > 0 && !文件判定C.同文件(文件, 本段文件)) continue;
                if (行 >= 起 && 行 <= 止) return 函数;
            }
            return null;
        }

        /// <summary>
        /// 这一行落在哪个函数里 —— **按指定的那一份文件**找(H47)。库行的行号属于**库文件**,
        /// 拿它去认本文件的第 N 行就是"张冠李戴";`文件` 为空时按 `本段文件` 的老口径(见上面那个重载)。
        /// </summary>
        public string? 行所属函数(int 行, string 文件)
        {
            foreach (var (函数, 起, 止, 所在文件) in 函数范围)
            {
                if (文件.Length > 0 && 所在文件.Length > 0 && !文件判定C.同文件(所在文件, 文件)) continue;
                if (行 >= 起 && 行 <= 止) return 函数;
            }
            return null;
        }

        /// <summary> 这个函数的花括号在哪两行(null = 表里没有它,别硬猜) </summary>
        public (int 左行, int 右行)? 花括号行(string? 函数)
        {
            if (string.IsNullOrEmpty(函数)) return null;
            foreach (var (名, 左行, 右行, _) in 函数边界)
                if (string.Equals(名, 函数, StringComparison.OrdinalIgnoreCase)) return (左行, 右行);
            return null;
        }

        /// <summary>
        /// 类型名给人看的样子:静态方法容器编译出来的内部结构叫 "_方法_棋盘",
        /// 面板上不该露出这个内部名字,显示成 "棋盘" 就好。
        /// </summary>
        public static string 显示类型名(string 类型名) =>
            类型名.StartsWith("_方法_", StringComparison.Ordinal) ? 类型名["_方法_".Length..] : 类型名;
    }
}
