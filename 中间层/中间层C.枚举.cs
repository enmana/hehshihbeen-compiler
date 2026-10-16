using System;
using System.Collections.Generic;
using System.Globalization;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 中间层展开器的一部分:**枚举(H68-2)**。
    ///
    /// 语法只有一条(**一行写完**):`枚举 颜色 { 赤, 绿, 蓝 }`;成员是**编译期常量**,
    /// 从 0 起依次 +1,也能自己定值(`枚举 颜色 { 赤 = 3, 绿, 蓝 }` ⇒ 3 / 4 / 5;认十进制与 `0x`,认负号)。
    ///
    /// 三条口径(见 `未做任务清单.md` §9 H68「四」):
    ///   · **底层就是有符号 整数(4 字节,与 C# enum 同口径)** —— 枚举不占存储、机器码里就是一个立即数,
    ///     所以这一层**一条指令都不发、ISA / 机器码零改动**:它只影响"名字怎么解析""类型怎么写"。
    ///   · **成员当编译期常量折叠** —— 表达式里出现 `绿`(或写全 `颜色.绿`)就当字面量用;
    ///     常量初值那条路(`颜色 画 = 绿;` / 数组字面量 / 结构字段默认值)走 `折成员常量` 折成十进制原文,
    ///     于是范围检查、装载期初值那一整套**一个字都不用改**。
    ///   · **不许隐式跨枚举**(用户拍板的那条):`颜色 画 = 别的枚举成员;` 当场报错给改法;
    ///     但枚举与 `整数` 之间照旧随便换(底层就是同一个东西)。
    ///
    /// **明确不做**(与施工图一致):`[]` 里的数组长度、`循环` 头里的枚举声明、区间类型 ——
    /// 撞上一律给一句"怎么写"的指路,不默默收下。
    /// </summary>
    internal partial class 中间层展开器
    {
        /// <summary> 枚举名(模块里的带前缀:`数学.颜色`)→ 枚举类型 </summary>
        private readonly Dictionary<string, 枚举类型C> _枚举表 = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 成员裸名(`绿`)与全名(`颜色.绿`)→ "它在哪个枚举里 + 值是多少"。
        /// 两种写法都能用:裸名省事,全名在**两个枚举重名**时指得清(重名在声明处当场拦住,见 处理枚举声明)。
        /// </summary>
        private readonly Dictionary<string, (string 枚举名, long 值)> _枚举成员表 = new(StringComparer.OrdinalIgnoreCase);

        /// <summary> 已预登记的枚举:登记全名 → 声明所在伪行下标(与 `_已预登记结构` 同一套"已预登记"协议)</summary>
        private readonly Dictionary<string, int> _已预登记枚举 = new(StringComparer.OrdinalIgnoreCase);

        #region 枚举声明

        /// <summary>
        /// 预扫一遍**枚举声明**(在结构之前 —— 结构字段可以是枚举类型,`结构 卡 { 颜色 花; }`)。
        /// 与结构同一套道理:库文件里的枚举,主程序要用就得先把名字登记上。
        /// </summary>
        private void 预扫枚举(List<int> 顶层)
        {
            foreach (int i in 顶层)
            {
                string 行 = 去可见性前缀(_伪行表[i]);
                if (中间层C.到英文关键字(取首词(行)) == "enum") 尝试预登记枚举(i);
            }
        }

        /// <summary> 试登记一条枚举声明;登记成功返回 true(失败一律不吭声,交给逐句处理那趟)</summary>
        private bool 尝试预登记枚举(int 下标)
        {
            string 原始行 = _伪行表[下标];
            string 行 = 去可见性前缀(原始行);
            if (中间层C.到英文关键字(取首词(行)) != "enum") return false;
            try
            {
                设置预扫现场(下标, 原始行);
                int 位置 = 下标;
                处理枚举声明(ref 位置, 预扫: true);
                foreach (var (_, 预行) in _已预登记枚举)
                    if (预行 == 下标) return true;
                return false;
            }
            catch (Exception)
            {
                // 预扫一条错都不报:这一条原样留给逐句处理那一趟(报错文案/行号/两处位置都在那儿)
                return false;
            }
        }

        /// <summary>
        /// 一条枚举声明:`枚举 颜色 { 赤, 绿, 蓝 }`(**一行写完**)。
        /// 登记 `_枚举表` + `_枚举成员表`(裸名与全名两把钥匙)+ 调试名字表;**不发任何代码**。
        /// </summary>
        private void 处理枚举声明(ref int 位置, bool 预扫 = false)
        {
            int 行号 = _伪行号表[位置];
            int 声明下标 = 位置;
            string 原始行 = _伪行表[位置].Trim();
            string 首词 = 取首词(原始行);                       // 枚举 / enum
            string 余下 = 原始行[首词.Length..].Trim();
            int 左花 = 余下.IndexOf('{');
            int 右花 = 余下.LastIndexOf('}');
            string 枚举名 = (左花 >= 0 ? 余下[..左花] : 余下).Trim();
            if (枚举名.Length == 0)
                throw 报错(行号, $"枚举声明缺少名字:写成 `枚举 颜色 {{ 赤, 绿, 蓝 }}`(成员之间用逗号隔开)",
                    "enum declaration needs a name");
            if (!标识符全词正则.IsMatch(枚举名))
                throw 报错(行号, $"枚举名非法: '{枚举名}'(名字要以文字或 `_` 开头,后面只能跟文字/数字/`_`)",
                    $"invalid enum name: '{枚举名}'");
            if (类型C.是类型名(枚举名) || 中间层指令C.到英文关键字(枚举名) != null)
                throw 报错(行号, $"枚举名冲突: '{枚举名}'(它已经是类型名或语言关键字,换个名字)",
                    $"enum name conflicts: '{枚举名}'");
            枚举名 = 限定名(枚举名);

            // 形状:一行写完。`{` 另起一行 / 少一个 `}` 都在这儿拦住 —— 说清"该怎么写",别掉到后面的解析里
            if (左花 < 0 || 右花 < 左花)
                throw 报错(行号, $"枚举要**一行写完**:`枚举 {枚举名} {{ 赤, 绿, 蓝 }}`" +
                    $"(每个成员一行那种写法还没做 —— 成员之间用逗号隔开就行)",
                    $"an enum must be written on one line: '枚举 {枚举名} {{ 赤, 绿, 蓝 }}'");
            string 尾 = 余下[(右花 + 1)..].Trim();
            if (尾.Length > 0 && 尾 != ";")
                throw 报错(行号, $"`}}` 后面只能有一个分号(也可以不写):'{尾}' 是多余的",
                    $"unexpected text after '}}': '{尾}'");

            // "已预登记"协议(与结构同一套):预扫登记过的这一条,逐句处理时**只再校验一遍**,
            // 不重报重名、不重写名字表/出处/编译提示(写第二遍会当场报"枚举名冲突")
            bool 已预登记 = !预扫 && 已预登记枚举到本行(枚举名, 声明下标);
            if (预扫)
            {
                // 预扫撞重名不报错(该报给谁、带哪两处位置,得看逐句处理那趟);已经登记过的也不重登
                if (_枚举表.ContainsKey(枚举名) || _已预登记枚举.ContainsKey(枚举名)) return;
            }
            else if (!已预登记 && _枚举表.ContainsKey(枚举名))
            {
                throw 报错(行号, $"枚举名冲突: '{枚举名}' —— {上一处声明(枚举名)}{同名怎么办("枚举")}",
                    $"enum name conflicts: '{枚举名}'");
            }
            if (!已预登记)
            {
                记声明出处(枚举名, 行号);
                记私有(枚举名);
            }
            if (_结构表.ContainsKey(枚举名))
                throw 报错(行号, $"枚举名与结构类型名冲突: '{枚举名}'(类型名在同一片名字空间里,换个名字)",
                    $"enum name conflicts with a struct/generic type: '{枚举名}'");

            string 成员文本 = 余下[(左花 + 1)..右花].Trim();
            if (成员文本.Length == 0)
                throw 报错(行号, $"枚举 '{枚举名}' 一个成员都没有:写成 `枚举 {枚举名} {{ 赤, 绿, 蓝 }}`",
                    $"enum '{枚举名}' has no members");

            var 成员表 = new List<(string 名, long 值)>();
            long 下一个 = 0;
            foreach (string 段 in 成员文本.Split(','))
            {
                string s = 段.Trim();
                if (s.Length == 0)
                    throw 报错(行号, $"枚举 '{枚举名}' 里有个空的成员(多半是多写了一个逗号):'{成员文本}'",
                        $"empty enum member in '{枚举名}'");
                string 名 = s;
                long 值 = 下一个;
                int 等号 = s.IndexOf('=');
                if (等号 >= 0)
                {
                    名 = s[..等号].Trim();
                    值 = 解析成员值(s[(等号 + 1)..].Trim(), 行号, 枚举名, 名);
                }
                if (!标识符全词正则.IsMatch(名))
                    throw 报错(行号, $"枚举成员名非法: '{名}'", $"invalid enum member name: '{名}'");
                if (成员表.Any(m => string.Equals(m.名, 名, StringComparison.OrdinalIgnoreCase)))
                    throw 报错(行号, $"枚举成员重复: '{枚举名}.{名}'", $"duplicate enum member: '{枚举名}.{名}'");
                if (类型C.是类型名(名) || 中间层指令C.到英文关键字(名) != null)
                    throw 报错(行号, $"枚举成员名不能用语言关键字 / 类型名: '{枚举名}.{名}'(换个名字)",
                        $"enum member cannot be a keyword or type name: '{名}'");
                // 裸名是全工程共用的:两个枚举里都有 `绿` ⇒ 表达式里写 `绿` 指谁说不清,当场拦住给两条明路
                if (_枚举成员表.TryGetValue(名, out var 已占) && !string.Equals(已占.枚举名, 枚举名, StringComparison.OrdinalIgnoreCase))
                {
                    if (预扫) return;      // 预扫不报错,留给逐句处理那趟
                    throw 报错(行号, $"枚举成员名冲突: '{名}' 已经在 `{已占.枚举名}` 里了" +
                        $"(两个枚举的成员不许重名 —— 表达式里写裸名 `{名}` 就说不清指谁)。改法:把其中一个枚举里的这个成员改个名" +
                        $"(例如 `{枚举名}.{名}一` 或 `{已占.枚举名}.{名}二`),两边各用各的",
                        $"enum member name conflicts: '{名}' (already in '{已占.枚举名}')");
                }
                成员表.Add((名, 值));
                下一个 = 值 + 1;
            }
            var 枚举 = new 枚举类型C(枚举名, 成员表);

            if (已预登记) return;      // 预扫已经登记过(含名字表与编译提示):这里只当"再校验一遍成员"

            _枚举表[枚举名] = 枚举;
            foreach (var (名, 值) in 成员表)
            {
                _枚举成员表.TryAdd(名, (枚举名, 值));
                _枚举成员表.TryAdd(枚举名 + "." + 名, (枚举名, 值));
            }
            _名字表.枚举[枚举名] = 枚举.成员名们;
            foreach (var (名, 值) in 成员表) _名字表.枚举成员[枚举名 + "." + 名] = 值;
            _声明工程[枚举名] = _当前工程;
            记可见性(枚举名);        // H60-15 那套:补全 / 导图按它筛枚举名的可见性
            _编译提示.Add($"枚举 {枚举名}:{成员表.Count} 个成员(底层 = 有符号 整数 4 字节,不占存储)→ {枚举.成员一览()}");
            if (预扫) _已预登记枚举[枚举名] = 声明下标;
            位置++;
        }

        /// <summary> 这条枚举声明是不是**这一行**预扫登记的那一条(全名 + 声明下标都要对上,与结构同一套协议)</summary>
        private bool 已预登记枚举到本行(string 全名, int 声明下标)
            => 声明下标 >= 0 && _已预登记枚举.TryGetValue(全名, out int 预行) && 预行 == 声明下标;

        /// <summary> 成员自己写的那个值(`= 3` / `= -1` / `= 0x10`):按**底层类型**(有符号 整数)做范围检查 </summary>
        private long 解析成员值(string 文本, int 行号, string 枚举名, string 成员名)
        {
            if (文本.Length == 0)
                throw 报错(行号, $"枚举成员 '{枚举名}.{成员名}' 的等号右边是空的:要么写个值(`= 3`),要么把等号去掉(自动接着上一个 +1)",
                    $"enum member '{成员名}' has nothing after '='");
            bool 负 = 文本.StartsWith('-');
            string 数 = 负 ? 文本[1..].Trim() : 文本;
            long 值;
            if (数.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!ulong.TryParse(数[2..], NumberStyles.HexNumber, null, out ulong 十六))
                    throw 报错(行号, $"枚举成员 '{枚举名}.{成员名}' 的值认不出来: '{文本}'(要十进制或 0x 十六进制)",
                        $"unrecognized enum member value: '{文本}'");
                值 = (long)十六;
            }
            else if (!long.TryParse(数, out 值))
            {
                throw 报错(行号, $"枚举成员 '{枚举名}.{成员名}' 的值认不出来: '{文本}'。" +
                    $"只能写常量(十进制或 0x 十六进制、可以带负号),不能写算式",
                    $"unrecognized enum member value: '{文本}'");
            }
            if (负) 值 = -值;
            long 下 = 类型C.有符号最小值(类型C.字宽度), 上 = 类型C.有符号最大值(类型C.字宽度);
            if (值 < 下 || 值 > 上)
                throw 报错(行号, $"枚举成员 '{枚举名}.{成员名}' 的值 {值} 超出底层类型 整数 的范围({下} ~ {上})" +
                    $"(枚举底层就是 4 字节有符号整数)",
                    $"enum member value {值} out of range for int");
            return 值;
        }

        #endregion

        #region 查枚举(类型词 / 成员常量)

        /// <summary>
        /// 枚举名 → 枚举类型:模块里写裸名字(`颜色`)先看本模块的 `模块.颜色`,再看全局;
        /// 外面写全名(`数学.颜色`)直接命中。与 `查结构类型` 同一套口径。
        /// </summary>
        private 枚举类型C? 查枚举类型(string 名词)
        {
            if (_枚举表.TryGetValue(名词, out var 枚举)) { 校验可取用(名词, _当前源行号); return 枚举; }
            if (_当前模块.Length > 0 && !名词.Contains('.')
                && _枚举表.TryGetValue(_当前模块 + "." + 名词, out var 本模块枚举)) return 本模块枚举;   // 模块自己人
            return null;
        }

        /// <summary> 这个名字(裸名或全名)是不是一个已声明的枚举 </summary>
        private bool 是枚举名(string 名词) => 查枚举类型(名词) != null;

        /// <summary>
        /// 一个声明里的"类型词"到底算什么类型:**基础类型 → 它自己;枚举名 → 它的底层(有符号 整数 4 字节)**;
        /// 都不是 → null(交给结构那边去认)。
        /// </summary>
        private 类型C.类型定义? 取类型定义(string 词)
            => 类型C.解析(词) ?? (查枚举类型(词) != null ? 类型C.枚举底层 : null);

        /// <summary> 这个词是不是"能当标量用的类型名"(基础类型 / 枚举;结构不算) </summary>
        private bool 是标量类型词(string 词) => 取类型定义(词) != null;

        /// <summary> 枚举成员当常量:认裸名(`绿`)与全名(`颜色.绿`);不是成员给 null </summary>
        private (string 枚举名, long 值)? 查枚举成员常量(string 词)
        {
            string t = 词.Trim();
            if (t.Length == 0) return null;
            if (_枚举成员表.TryGetValue(t, out var 成员)) return 成员;
            if (_当前模块.Length > 0 && !t.Contains('.') && _枚举成员表.TryGetValue(_当前模块 + "." + t, out var 本模块)) return 本模块;
            return null;
        }

        /// <summary>
        /// **常量初值那条路的折法**:整段文本正好是一个枚举成员名 ⇒ 换成十进制原文(负值带负号),
        /// 好让下游那套"字面量 + 范围检查 + 装载期初值"一个字都不用改。
        /// `目标枚举名` 非空(声明处知道要的是哪个枚举)时顺手做"不许跨枚举"的检查。
        /// </summary>
        private string 折成员常量(string 文本, int 行号, string 目标枚举名 = "")
        {
            if (查枚举成员常量(文本) is not { } 成员) return 文本;
            if (目标枚举名.Length > 0 && !string.Equals(成员.枚举名, 目标枚举名, StringComparison.OrdinalIgnoreCase))
                throw 报错(行号, 跨枚举文案(目标枚举名, 成员.枚举名, 成员.值), "enum member of another enum");
            return 成员.值.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// **一个枚举的值不许悄悄变成另一个枚举的值**(用户拍板的那条约束)。
        /// 右边不是"整段就是一个枚举成员"就放行(算式 / 整数常量 / 别的变量都不管 —— 这一层没有类型推断,
        /// 不装那个样子)。
        /// </summary>
        private void 检查跨枚举(string 目标类型词, string 右侧, int 行号)
        {
            if (查枚举类型(目标类型词) is not { } 目标) return;
            if (查枚举成员常量(右侧) is not { } 成员) return;
            if (string.Equals(成员.枚举名, 目标.名称, StringComparison.OrdinalIgnoreCase)) return;
            throw 报错(行号, 跨枚举文案(目标.名称, 成员.枚举名, 成员.值), "enum member of another enum");
        }

        /// <summary> 跨枚举那一句错(声明 / 赋值 / 返回共用一句,别各写半句)</summary>
        private static string 跨枚举文案(string 目标枚举, string 来源枚举, long 值) =>
            $"`{来源枚举}` 的成员不能直接当 `{目标枚举}` 的值用(两个枚举的成员不许隐式互赋)。" +
            $"改法:①改用 `{目标枚举}` 自己的成员;②真要传这个数,先存进一个 `整数` 变量再赋" +
            $"(`整数 中转 = {来源枚举} 的成员;` 然后 `{目标枚举} 变量 = 中转;`)—— 枚举底层就是有符号 整数";

        /// <summary>
        /// H68-2 施工图里那条"明确不做"的指路:**枚举不进 `[]` 当数组长度**(不做区间类型)。
        /// 撞上给一句人话,别掉到"数组长度非法"那种看不出所以然的报错里。
        /// </summary>
        private void 不许当数组长度(string 长度文本, int 行号)
        {
            if (查枚举成员常量(长度文本) is not { } 成员) return;
            throw 报错(行号, $"枚举成员 `{长度文本}` 不能当数组长度(HB 没有「区间类型」这种用法;" +
                $"它是个值,不是个数)。改法:写个常量数字(`整数 表[8];`),或者用列表 `整数 表[]`(个数自己长)、" +
                $"按枚举个数定长就自己把那个数写上",
                "an enum member cannot be used as an array length");
        }

        #endregion
    }
}
