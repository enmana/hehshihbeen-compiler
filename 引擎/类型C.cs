namespace hehshihbeen_compiler
{
    /// <summary>
    /// 基本类型:**整数默认有符号**(2026-09 定),无符号要写出来。
    ///   字节/逻辑/字符      1 字节,无符号(字符就是字节,按编码用)
    ///   短整数              2 字节,**有符号** -32768 ~ 32767
    ///   整数                4 字节,**有符号**
    ///   无符号短整数         2 字节 0 ~ 65535(以前的 短整数 语义)
    ///   无符号整数           4 字节 0 ~ 4294967295
    ///   **文字**            4 字节,无符号:一个 **Unicode 码点**(从字符串下标取出来的就是它)
    ///   不写类型 / 变量      4 字节,**有符号**(= 整数)
    /// 字符串/64 位等以后用"结构 + 数组 + 方法"在本语言里向上搭。
    /// </summary>
    public enum 基本类型 { 无, 字节, 逻辑, 字符, 短整数, 整数, 无符号短整数, 无符号整数, 文字, 地址, 浮点 }

    /// <summary> 变量数据槽定义:汇编器按 宽度 紧致分配地址(不再一律 4 字节) </summary>
    public readonly record struct 变量槽定义(string 名称, int 宽度);

    /// <summary>
    /// 编译器自己开的**隐藏区**(堆 / 池 / 软栈 / 软栈溢出的垫子)的登记项(R1-3,第207批)。
    /// 中间层只知道"区名 + 槽名 + 多大 + 干什么用";**绝对地址要等汇编器把代码段长度定下来**,
    /// 所以这一份是"半成品",由 `汇编器C` 翻成 <see cref="内存分区C"/>,再交 `引擎\内存布局C.cs` 算整张表。
    /// </summary>
    public readonly record struct 内存分区定义C(string 区名, string 槽名, int 大小, string 说明);

    /// <summary>
    /// 隐藏区在运行内存里的**实际位置**(字节地址;由汇编器把 <see cref="内存分区定义C"/> 的槽名翻过来)。
    /// 给人看的那份表见 `引擎\内存布局C.cs`(它会把代码段 / 变量槽 / 空闲 / 硬件栈也补上)。
    /// </summary>
    public readonly record struct 内存分区C(string 区名, int 起始, int 大小, string 说明)
    {
        /// <summary> 这块区之后的第一个字节 </summary>
        public int 末尾 => 起始 + 大小;
    }

    /// <summary>
    /// 结构的一个字段:名称、类型名、宽度、相对结构起点的字节偏移(按声明序紧排)。
    /// 字段还能是**结构**(嵌套)或**数组**(含结构数组):这时 结构类型/元素宽/元素个数 有值,
    /// 访问就是"常量偏移累加",编译期全算得出来 —— 运行期一个额外指令都不多。
    /// </summary>
    public readonly record struct 结构字段C(string 名称, string 类型名, int 宽度, int 偏移,
        结构类型C? 结构类型 = null, int 元素宽 = 0, int 元素个数 = 0,
        bool 是字符串 = false, bool 元素是字符串 = false,
        bool 私有 = false, bool 程序集 = false,
        bool 是列表 = false, int 列表元素宽 = 0, 结构类型C? 列表元素结构 = null)
    {
        /// <summary> 是结构字段(单个结构 —— 结构**数组**字段不算,那个走 是数组)</summary>
        public bool 是结构 => 结构类型 != null && 元素个数 == 0;
        public bool 是数组 => 元素个数 > 0;
        /// <summary> 数组字段里元素是什么结构(基本类型数组时为 null)</summary>
        public 结构类型C? 元素结构 => 元素个数 > 0 ? 结构类型 : null;
        /// <summary> 这个字段里装着 `字符串`(8 字节:数据地址 + 容量,数据在堆里)。
        /// 数组字段(`字符串 名单[10]`)看 元素是字符串。 </summary>
        public bool 是串字段 => 是字符串 || 元素是字符串;
        /// <summary>
        /// 这个字段是**动态列表**(`整数 数[]`,H1 第四件):字段自己 12 字节 = 头
        /// (数据地址 @+0 / 长度 @+4 / 容量 @+8),真正的元素在**堆**里、按容量翻倍(与 `字符串` 字段同一个口径)。
        /// 是列表 时 是数组 为假(列表的个数是运行期的,不折进常量偏移)。
        /// </summary>
        public bool 是列表字段 => 是列表;
        /// <summary> A4:带可见性前缀的字段(声明时写了 `私有` / `程序集`)。没标的 = 公开</summary>
        public bool 有可见性 => 私有 || 程序集;
        /// <summary> 这个字段里有**堆块**(字符串块 / 列表的数据块):整体赋值要深拷、清 0 要归还 </summary>
        public bool 是堆字段 => 是串字段 || 是列表;
    }

    /// <summary> 结构类型(纯数据):字段按声明顺序紧致排布,总宽度 = 各字段宽度之和 </summary>
    public sealed class 结构类型C
    {
        public string 名称 { get; }
        public IReadOnlyList<结构字段C> 字段 { get; }
        public int 总宽度 { get; }

        /// <summary>
        /// 这个结构里有没有 `字符串` 字段(单个、数组、或者嵌在里面的结构里)。
        /// 有的话,"整体赋值/返回/放进列表"都得**逐字段深拷贝**那个字符串 ——
        /// 按字节搬只会把"数据地址"也搬过去,两个结构从此共用堆里同一块,改一边另一边跟着变。
        /// </summary>
        public bool 有字符串字段 { get; }

        /// <summary>
        /// 这个结构里有没有**动态列表**字段(`整数 数[];`,单个 / 数组元素 / 嵌在里面的结构里)。
        /// 有的话,整体赋值 / 返回 / 进列表要**深拷贝**那些列表(新要一块 + 把元素搬过去),
        /// 清 0 / 换页时要把数据块 `释放` 还回堆 —— 与 `字符串` 字段同一套口径。
        /// </summary>
        public bool 有列表字段 { get; }

        /// <summary> 结构里有没有任何"堆块"(字符串块 / 列表数据块):决定走不走那套特殊拷贝与归还 </summary>
        public bool 有堆字段 => 有字符串字段 || 有列表字段;

        /// <summary>
        /// **H3**:字段级的标量默认值(`结构 点 { 整数 x = 5; }` 里的那个 `5`)—— 字段名 → 初值**原文**。
        /// 只认**标量**字段(数组 / 字符串 / 结构字段的初值照旧不收,理由见 `解析字段行`);
        /// 原文在中间层展开时按字段类型做范围检查并折成 32 位值(见 `展开字段初值`)。
        /// 没有字段带默认值时是**空表**(老结构一个新字段都没有,行为与 H3 之前逐字节相同)。
        /// </summary>
        public IReadOnlyDictionary<string, string> 字段初值 { get; }

        /// <summary>
        /// H3:这个结构的默认值摊平成"一段内存里的绝对偏移 → 值"(按声明顺序,嵌套结构递归展开)。
        /// 变量声明处直接拿它写进"该格":要么并进装载期初值(顶层),要么按帧偏移现场写(函数里)。
        /// 这份表是"结构自己说了算"的默认值,与"某一份变量额外写的字面量初值"是两回事(见声明处)。
        /// </summary>
        public IReadOnlyList<(int 偏移, int 宽度, uint 值)> 默认初值们 { get; }

        public 结构类型C(string 名称, IReadOnlyList<结构字段C> 字段,
            IReadOnlyDictionary<string, string>? 字段初值 = null, IReadOnlyDictionary<string, uint>? 已折初值 = null)
        {
            this.名称 = 名称;
            this.字段 = 字段;
            this.字段初值 = 字段初值 ?? new Dictionary<string, string>(StringComparer.Ordinal);
            int 偏移 = 0;
            foreach (var f in 字段)
            {
                偏移 += f.宽度;
                if (f.是串字段 || (f.结构类型?.有字符串字段 ?? false)) 有字符串字段 = true;
                if (f.是列表 || (f.列表元素结构?.有堆字段 ?? false) || (f.结构类型?.有列表字段 ?? false)) 有列表字段 = true;
            }
            总宽度 = 偏移;
            默认初值们 = 摊开默认初值(已折初值 ?? new Dictionary<string, uint>(StringComparer.Ordinal));
        }

        /// <summary> 递归摊平默认值:自己这一层的字段放在 `基偏移` 上,嵌套结构整个往后挪 </summary>
        private List<(int 偏移, int 宽度, uint 值)> 摊开默认初值(IReadOnlyDictionary<string, uint> 已折)
        {
            var 表 = new List<(int, int, uint)>();
            foreach (var f in 字段)
            {
                if (f.是结构 && f.结构类型 is { } 内层)
                {
                    foreach (var (内偏移, 内宽, 内值) in 内层.默认初值们)
                        表.Add((f.偏移 + 内偏移, 内宽, 内值));
                    continue;
                }
                if (已折.TryGetValue(f.名称, out uint 值)) 表.Add((f.偏移, f.宽度, 值));
            }
            return 表;
        }

        /// <summary> 按名找字段;找不到返回 null </summary>
        public 结构字段C? 找字段(string 名)
        {
            foreach (var f in 字段)
                if (f.名称 == 名) return f;
            return null;
        }
    }

    /// <summary>
    /// **H68-2 枚举**:`枚举 颜色 { 赤, 绿, 蓝 }` —— 成员是**编译期常量**(0, 1, 2 …;也能写 `= 3`),
    /// 底层就是**有符号 整数**(4 字节,与 C# enum 同口径)。
    /// <para>
    /// 它**不占存储、机器码里就是一个立即数** —— 一份枚举声明发不出任何一条指令,所以这一份只活在编译期:
    /// 给类型名表 / 补全 / 高亮 / 报错文案用。成员之间**不许隐式互赋**(`颜色 画 = 别的枚举成员;` 编译期报错);
    /// 与结构**互不相容**(结构是可多份的纯值类型,枚举是常量表)。
    /// </para>
    /// </summary>
    public sealed class 枚举类型C
    {
        public string 名称 { get; }

        /// <summary> 成员(名 + 值),按声明顺序 —— 顺序就是帮助/补全/报错里的展示顺序。
        /// 值用 `long` 存(底层是有符号 4 字节:负值照原样留负号,报错与折常量时才对得上)。 </summary>
        public IReadOnlyList<(string 名, long 值)> 成员 { get; }

        public 枚举类型C(string 名称, IReadOnlyList<(string 名, long 值)> 成员)
        {
            this.名称 = 名称;
            this.成员 = 成员;
        }

        /// <summary> 这个成员在不在;在就给出它的值 </summary>
        public bool 取成员值(string 成员名, out long 值)
        {
            foreach (var (名, 值项) in 成员)
                if (string.Equals(名, 成员名, StringComparison.Ordinal)) { 值 = 值项; return true; }
            值 = 0;
            return false;
        }

        /// <summary> 成员名的裸名清单(名字表 / 补全用)</summary>
        public List<string> 成员名们
        {
            get
            {
                var 表 = new List<string>(成员.Count);
                foreach (var (名, _) in 成员) 表.Add(名);
                return 表;
            }
        }

        /// <summary> 给人看的一行(`赤 = 0, 绿 = 1, 蓝 = 2`)—— 编译提示 / 帮助 / 补全说明都用它 </summary>
        public string 成员一览(int 最多 = 0)
        {
            var 段 = new List<string>();
            foreach (var (名, 值) in 成员)
            {
                if (最多 > 0 && 段.Count >= 最多) { 段.Add("…"); break; }
                段.Add($"{名} = {值}");
            }
            return string.Join(", ", 段);
        }
    }

    /// <summary>
    /// 列表类型:元素宽 + 容量。**声明写法和数组一样,只是 `[]` 里不写数字**:
    ///   `短整数 表[8]` = 数组(个数写死,不长大;要用 添加 也行,满了就报错)
    ///   `短整数 分数[]` = 列表(个数不写:编译器能算就先算,算不出就自己长大)
    /// 存取和数组完全一样(`分数[i]`),多出来的只有 `.添加()/.长度()/.清空()`。
    /// </summary>
    public sealed class 列表类型C
    {
        public string 元素类型名 { get; }
        public int 元素宽 { get; }
        /// <summary> 元素是结构时非 null(列表里放结构:`点 路径[]`)</summary>
        public 结构类型C? 元素结构 { get; }
        /// <summary> 容量(元素个数)。0 = 还没定(由中间层按"编译器能否算出来"决定)</summary>
        public int 容量 { get; set; }
        /// <summary> true = 声明里写了数字(`[8]`):不长大,满了报错;false = 空 `[]`:装不下就自己长</summary>
        public bool 容量写死 { get; }
        /// <summary> 由编译器算出来的容量(编译页会告诉用户"算出来最多几个")</summary>
        public bool 容量是算出来的 { get; set; }

        /// <summary>
        /// **H3 ②**:声明时写了字面量初值的**列表**(`整数 名[] = [1, 2, 3];`)—— 初值个数(0 = 没写初值)。
        /// 容量就按它定(至少 `初始列表容量` 个),声明那一句再把这几个元素逐个 `添加` 进去 ⇒ 长度 = 这个数,
        /// 而且**照样会自己长**(与"定长数组"是两回事)。
        /// </summary>
        public int 初值个数 { get; set; }

        /// <summary> H3 ②:声明列表时写的那串字面量初值**原文**(`[1, 2, 3]`;空串 = 没写初值)</summary>
        public string 初值原文 { get; set; } = "";

        /// <summary>
        /// 元素是 `字符串`(`字符串 名单[]`,H1 起;每个元素 8 字节:数据地址 + 容量;真正的字节在堆里)。
        /// 与 `数组类型C.元素是字符串` 同一个口径 —— 区别只是"个数写不写"。
        /// </summary>
        public bool 元素是字符串 { get; set; }

        public 列表类型C(string 元素类型名, int 元素宽, int 容量, bool 容量写死, 结构类型C? 元素结构 = null)
        {
            this.元素类型名 = 元素类型名;
            this.元素宽 = 元素宽;
            this.容量 = 容量;
            this.容量写死 = 容量写死;
            this.元素结构 = 元素结构;
        }
    }

    /// <summary>
    /// 数组类型:元素类型/元素宽度/长度;元素紧致排列(元素 i 的偏移 = i × 元素宽)。
    /// 元素还可以是**结构**(结构数组:元素宽 = 结构总宽度),字段访问靠 元素结构 往下走。
    /// <para>
    /// **H68-1 多维数组**:上面那一套一个字没改,只是多了"这一层还分几维"。
    /// 单维数组 `各维长度 is null` ⇒ `长度` / `总宽度` / `总元素数` 与老写法逐字节同值;
    /// 多维数组(2~4 维,行优先)`各维长度 = [3, 4]` ⇒ **最后一维最快**(`袋[r][c]` 挨着放)。
    /// `元素宽` 一律是**最内层元素**的宽度;某一维一个下标要乘多少字节,问 `维步长(第几维)`。
    /// </summary>
    public sealed class 数组类型C
    {
        public string 元素类型名 { get; }
        public int 元素宽 { get; }
        /// <summary> 第一维的长度(单维 = 元素个数);`.长度()` 与第一个下标的上界都看它 </summary>
        public int 长度 { get; }
        public 结构类型C? 元素结构 { get; }

        /// <summary> 每一维的长度(行优先);**单维是 null** —— 老写法没有这一格时行为一字不变 </summary>
        public int[]? 各维长度 { get; }

        /// <summary> 元素是 `字符串`(每个元素 8 字节:数据地址 + 容量;真正的字节在堆里) </summary>
        public bool 元素是字符串 { get; set; }

        /// <summary> 不止一维(H68-1):地址要按"各维下标 × 维步长"线性累加 </summary>
        public bool 是多维 => 各维长度 is { Length: > 1 };

        /// <summary> 一共几个元素(各维相乘;单维 = 长度)。初值个数与"整块多大"都看它 </summary>
        public int 总元素数
        {
            get
            {
                if (各维长度 is not { Length: > 0 } 维) return 长度;
                int 总 = 1;
                foreach (int 一维 in 维) 总 *= 一维;
                return 总;
            }
        }

        public int 总宽度 => 元素宽 * 总元素数;

        /// <summary> 第 `维` 维(0 起)的一个下标要乘多少字节 = 它后面各维长度相乘 × 元素宽(行优先) </summary>
        public int 维步长(int 维)
        {
            if (各维长度 is not { Length: > 0 } 各) return 元素宽;
            int 步 = 元素宽;
            for (int i = 各.Length - 1; i > 维; i--) 步 *= 各[i];
            return 步;
        }

        /// <summary> 各维长度的中文写法(`3×4`),报错与帮助都用它 </summary>
        public string 各维文本 => 各维长度 is { Length: > 1 } 各 ? string.Join("×", 各) : 长度.ToString();

        public 数组类型C(string 元素类型名, int 元素宽, int 长度, 结构类型C? 元素结构 = null, int[]? 各维长度 = null)
        {
            this.元素类型名 = 元素类型名;
            this.元素宽 = 元素宽;
            this.长度 = 长度;
            this.元素结构 = 元素结构;
            this.各维长度 = 各维长度;
        }
    }

    /// <summary>
    /// 引用类型:`引用 点 p` = "带类型的 32 位地址"。
    /// 它只是编译期的事(知道指向什么、字段落在哪几个字节),访存走间接寻址 ——
    /// **ISA 一个字不改、运行期不多花指令**,只是"直接寻址"变成"间接寻址"。
    /// </summary>
    public sealed class 引用类型C
    {
        /// <summary> 指向的东西的类型名(点/短整数/32位字…) </summary>
        public string 指向类型名 { get; }
        public 结构类型C? 结构类型 { get; }
        /// <summary> 指向数组时:元素宽与个数(0 = 不是数组)</summary>
        public int 元素宽 { get; }
        public int 元素个数 { get; }
        public 结构类型C? 元素结构 { get; }
        /// <summary> 指向那一整块的宽度(结构 = 总宽,数组 = 总宽,标量 = 宽度)</summary>
        public int 整块宽 { get; }

        public 引用类型C(string 指向类型名, 结构类型C? 结构类型, int 整块宽, int 元素宽 = 0, int 元素个数 = 0, 结构类型C? 元素结构 = null)
        {
            this.指向类型名 = 指向类型名;
            this.结构类型 = 结构类型;
            this.整块宽 = 整块宽;
            this.元素宽 = 元素宽;
            this.元素个数 = 元素个数;
            this.元素结构 = 元素结构;
        }

        public bool 是数组 => 元素宽 > 0;

        /// <summary>
        /// **数组参数专用**:`函数 f(整数 表[])` 里那个 `表` —— 除了数据地址,还额外带一个"现在有几个元素"的
        /// 运行期长度(调用方把静态数组的元素个数或列表的当前长度一起递进来)。有它才能在函数里查越界、用 `表.长度()`。
        /// 用户手写的 `引用 整数 表 = 某数组` 没有这个(只有地址)。
        /// </summary>
        public bool 带长度 { get; set; }

        /// <summary>
        /// 这个"带长度的引用"是**字符串参数**(`函数 f(字符串 名字)`)。
        /// 形状和数组参数一样(数据地址 + 长度两个字),区别只在语义:元素是 1 字节的字符、
        /// 长度是"字节数",而且**不许在函数里重新赋值**(字符串赋值会换数据地址,调用方那边就对不上了)。
        /// </summary>
        public bool 是字符串 { get; set; }

        /// <summary>
        /// 这个"字符串参数"是**可写**的(`函数 f(字符串 出 名字)`,英文 `out`)。
        /// 可写字符串参数**只传调用方字符串头的地址**(4 字节:数据地址 / 长度 / 容量 三个字连续排)——
        /// 函数里赋值、拼接、清空都当场落在调用方那份上,变长时连调用方的容量一起长大。
        /// 只读的字符串参数仍是"数据地址 + 长度"两个字(8 字节)。
        /// </summary>
        public bool 出参数 { get; set; }
    }

    /// <summary> 变量绑定:数据槽 + (可选)结构类型 + (可选)数组类型 + (可选)列表类型 + (可选)引用类型 </summary>
    public readonly record struct 变量绑定C(变量槽定义 槽, 结构类型C? 结构类型, 数组类型C? 数组类型 = null,
        列表类型C? 列表类型 = null, 引用类型C? 引用类型 = null, int 声明宽 = 0, bool 是逻辑 = false, bool 有符号 = true,
        变量槽定义? 长度槽 = null)
    {
        public string 名称 => 槽.名称;
        public int 宽度 => 槽.宽度;
        /// <summary>
        /// 声明宽度(0 = 和槽一样宽)。只有一种情况两者不同:`短整数` 变量按 4 字节槽放(C 档优化)——
        /// 槽宽 4、声明宽 2,写进去的时候要按声明宽处理(见 地址位置.要截断):
        /// 无符号补 0(掩码),有符号补符号位(符号扩展)。
        /// </summary>
        public int 实际声明宽 => 声明宽 > 0 ? 声明宽 : 槽.宽度;
        /// <summary> 槽比声明的宽 → 存值时要按声明宽处理(守住"高半永远是对的"这条约定)</summary>
        public bool 要截断 => 声明宽 > 0 && 声明宽 < 槽.宽度;
        public bool 是结构 => 结构类型 != null;
        public bool 是数组 => 数组类型 != null;
        public bool 是列表 => 列表类型 != null;
        /// <summary> 槽里装的是"地址"(引用):访存要先把它读出来当基址 </summary>
        public bool 是引用 => 引用类型 != null;
        /// <summary> 数组参数(切片)的长度放在哪个槽里(运行期读它查越界/取 .长度());不是数组参数就是 null</summary>
        public bool 是数组切片 => 长度槽 != null;
        /// <summary> 数组或列表的元素宽(既不是数组也不是列表返回 0)</summary>
        public int 元素宽 => 数组类型?.元素宽 ?? 列表类型?.元素宽 ?? 0;
    }

    /// <summary> 类型唯一事实源:中英名、宽度、常量范围。中间层/汇编器/编辑器补全都查这里。 </summary>
    public static class 类型C
    {
        public readonly record struct 类型定义(基本类型 类型, string 中文, string 英文, int 宽度, bool 有符号 = false)
        {
            /// <summary> 这个类型能表示的最小常量(有符号才有负的下限) </summary>
            public long 最小值 => 有符号 ? -(1L << (宽度 * 8 - 1)) : 0;
            /// <summary> 最大常量 </summary>
            public long 最大值 => 有符号 ? (1L << (宽度 * 8 - 1)) - 1 : (宽度 >= 8 ? long.MaxValue : (1L << (宽度 * 8)) - 1);
            /// <summary> 范围的文字描述(帮助/报错都用它) </summary>
            public string 范围文本 => $"{最小值} ~ {最大值}";
        }

        public static readonly 类型定义[] 表 =
        [
            new(基本类型.字节,       "字节",       "byte",   1),
            new(基本类型.逻辑,       "逻辑",       "logic",  1),
            new(基本类型.字符,       "字符",       "char",   1),
            new(基本类型.短整数,     "短整数",     "short",  2, 有符号: true),
            new(基本类型.整数,       "整数",       "int",    4, 有符号: true),
            new(基本类型.无符号短整数, "无符号短整数", "ushort", 2),
            new(基本类型.无符号整数,  "无符号整数",  "uint",   4),
            // 文字 = 一个 Unicode 码点(从字符串下标取出来的就是它);无符号,比 `字符`(1 字节)大
            new(基本类型.文字,       "文字",       "rune",   4),
            // 地址 = 堆块地址(只能由 申请 产生,只能配 释放 与 取字/存字 用);无符号 4 字节
            new(基本类型.地址,       "地址",       "addr",   4),
            // 通用化(2026-10):**单精度浮点**。与 ISA 那 8 条硬件浮点指令(0x38~0x3F)同一口径 ——
            // 变量里装的就是 **IEEE754 单精度位模式**(4 字节),算术走 `浮点加法` 那一族;
            // 这与 `Sample\库\float32.hb` 的老约定**完全一致**(它一直把位模式放在 `无符号整数` 里算)
            // ⇒ 老库照旧能用,两边可以互换、可以混用。
            // 注意:它**不是整数** —— 凡是"按数值大小判范围 / 折半 / 当数组下标"的地方都不该收它。
            new(基本类型.浮点,       "浮点",       "float",  4),
        ];

        /// <summary> 不写类型时的宽度(32 位字) </summary>
        public const int 字宽度 = 4;

        /// <summary> 不写类型时按哪个类型办:**整数(有符号 4 字节)** </summary>
        public static 类型定义 默认类型 => new(基本类型.整数, "整数", "int", 4, 有符号: true);

        /// <summary>
        /// **H68-2**:枚举的**底层类型** —— 有符号 4 字节的 `整数`(与 C# enum 同口径)。
        /// 枚举声明登记之后,凡是要"宽度 / 有没有符号 / 范围"的地方都拿这一份顶上,
        /// 于是 `颜色 画 = 绿;` 与 `整数 画 = 1;` 在机器码上**逐字节同构**。
        /// </summary>
        public static 类型定义 枚举底层 => 表.First(x => x.类型 == 基本类型.整数);

        /// <summary> 解析类型名(中/英,英文大小写不敏感);不是类型名返回 null </summary>
        public static 类型定义? 解析(string 词)
        {
            foreach (var t in 表)
                if (词 == t.中文 || string.Equals(词, t.英文, StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

        public static bool 是类型名(string 词) => 解析(词) != null;

        /// <summary> 类型名是不是**有符号**的(按宽度给默认有符号性:窄的无符号类型走这里查表) </summary>
        public static bool 有符号(string 类型名) => 解析(类型名)?.有符号 ?? true;

        /// <summary> 该宽度能表示的常量上限(无符号;老签名保留)</summary>
        public static uint 最大值(int 宽度) => 宽度 >= 4 ? 0xFFFFFFFFu : (uint)((1 << (8 * 宽度)) - 1);

        /// <summary> 该宽度的有符号上限(32767 / 2147483647) </summary>
        public static long 有符号最大值(int 宽度) => (1L << (宽度 * 8 - 1)) - 1;

        /// <summary> 该宽度的有符号下限(-32768 / -2147483648) </summary>
        public static long 有符号最小值(int 宽度) => -(1L << (宽度 * 8 - 1));

        /// <summary> 把一个常量看成"宽度 字节的 有符号数"时的值(用于范围检查) </summary>
        public static long 看成有符号(uint 常量, int 宽度)
        {
            int 位 = 宽度 * 8;
            if (位 >= 32) return (int)常量;
            uint 掩 = (1u << 位) - 1;
            uint 截 = 常量 & 掩;
            uint 符号位 = 1u << (位 - 1);
            return (截 & 符号位) != 0 ? (long)截 - (1L << 位) : 截;
        }

        /// <summary> 类型关键字(中英)对:供 中英切换/语法高亮/补全(与 变量/var 一起用) </summary>
        public static (string 中文, string 英文)[] 关键字对 =>
            [.. 表.Select(x => (x.中文, x.英文))];
    }
}
