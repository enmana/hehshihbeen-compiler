using System;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 一次补全能用上的**两片名字表**(C19):
    ///   · `本文件表` —— 当前文件那份调试名字表(变量按作用域:`可见变量(行, 所在函数)` 的行区间只在同一个文件里有意义);
    ///   · `工程表` —— 工程级名字表(函数 / 结构 / 字段 / 可见性 / 签名都在这里,别的文件声明的也能补)。
    /// 名字表是**编译期旁路信息**:没有编译结果时两片都可以是 null,那时按现场源码扫兜底。
    /// </summary>
    public sealed class 补全来源C
    {
        /// <summary> 当前文件那份(变量按作用域;查不到给 null = 退回工程表 / 现场扫) </summary>
        public 名字表C? 本文件表;
        /// <summary> 工程级那份(函数 / 结构 / 可见性 / 参数表看它) </summary>
        public 名字表C? 工程表;
    }

    /// <summary> 一次补全"问的是什么"(界面两端都只填这一份,规则全在 `补全名字C.取`) </summary>
    public sealed class 补全请求C
    {
        /// <summary> 正在编辑哪个文件(与名字表里的"声明文件"同一套写法;拿不到给空串 —— 那时只能现场兜底) </summary>
        public string 当前文件 = "";
        /// <summary> 光标在第几行(1 基;变量作用域与"这一行在哪个函数里"都按它算) </summary>
        public int 当前行 = 1;
        /// <summary> 光标前那个词(可以为空:刚敲了一个 `.`) </summary>
        public string 词 = "";
        /// <summary> 光标前是不是一个 `.`(按成员补) </summary>
        public bool 在点后;
        /// <summary> `在点后` 时点号**左边**那个名字(`分数.` 的 `分数`;取不到给空串) </summary>
        public string 点名 = "";
        /// <summary> 现场正文(没有编译结果时按它扫名字;只有这一档的兜底是"整篇声明过的名字") </summary>
        public string 现场正文 = "";
        /// <summary> 允许现场扫兜底(用户拍板 a:没编译过 / 刚改过也补) </summary>
        public bool 现场兜底 = true;
        /// <summary> 最多给几条 </summary>
        public int 上限 = 30;
    }

    /// <summary>
    /// **智能补全的名字计算器**(C19,纯函数:自检直接喂名字表与请求,不开界面)。
    ///
    /// 与老口径(`补全词表C.从文件收集名字` 的"整篇扫一遍")的差别:
    ///   ① **变量按作用域** —— 走 `名字表C.可见变量(行, 所在函数)`:别的函数的局部、已经出了块的变量都不弹;
    ///   ② **函数 / 结构来自整个工程** —— 别的 `.hb`(与本文件没 `包含` 关系)声明的也能补,不再只有本文件 + 包含两层;
    ///   ③ **`名字.` 后面给那个类型的成员** —— 结构字段、方法容器成员、`字符串`/列表的内建方法(用户点名的"结构的被调用时的补全");
    ///   ④ **按可见性过滤** —— `私有` 只在声明它的模块里给、`程序集` 只在同一个 `.hbp` 工程里给,
    ///      判据与编译器 `中间层C.模块.cs` 的 `校验可取用` / `校验字段可取用` **同一份数据**(名字表里那两张旁路表);
    ///      **绝不弹一个写下去必然报错的名字**(用户拍板 b);
    ///   ⑤ **参数提示** —— 函数 / 方法的候选带签名(`整数 左, 整数 右`),插入时写成 `名字()` 并把光标停在括号里(用户拍板 e);
    ///   ⑥ **现场兜底** —— 没有这个文件的编译结果时(没编译过 / 新文件),退回按源码扫(用户拍板 a);
    ///      这一档不细分作用域(源码里没有块区间信息),编译一次就按 ① 走准的;
    ///   ⑦ **代码片段骨架也进候选**(C28)—— 打 `rg` / `如` 时 `如果`(关键字)与 `如果{…}`(骨架)**分两条**列,
    ///      骨架那条插入时**按当前层级缩进**(走 `片段插入C.算插入`,与菜单『插入代码片段』同一份算,
    ///      所以两边的插入结果逐字节相同)。
    ///
    /// 匹配与排序仍然只在 `补全词表C.排序匹配` 那一份里(拼音首字母 / 全拼 / 模糊音全照旧)。
    /// **ISA / 语言面 / 机器码一个字都不动** —— 这一份只读名字表。
    /// </summary>
    public static class 补全名字C
    {
        /// <summary>
        /// 取候选(固定词表 + 工程里的名字 / 点号后面的成员)。界面两端与自检都走这一个入口。
        /// </summary>
        public static List<补全候选C> 取(补全来源C? 来源, 补全请求C 请求, IReadOnlyCollection<string>? 模糊规则 = null)
        {
            请求 ??= new 补全请求C();
            int 上限 = 请求.上限 > 0 ? 请求.上限 : 30;
            名字表C? 工程表 = 来源?.工程表;
            名字表C? 本文件表 = 来源?.本文件表 ?? 工程表;

            if (请求.在点后) return 成员候选(工程表, 本文件表, 请求, 上限, 模糊规则);

            var 池 = new List<补全候选C>(补全词表C.固定候选);
            池.AddRange(片段候选());              // C28:代码片段骨架也进候选(与菜单『插入代码片段』同一份数据)
            if (工程表 != null) 池.AddRange(名字候选(工程表, 本文件表 ?? 工程表, 请求));

            // 现场兜底:这个名字表里没有这个文件(没编译过 / 新文件 / 单文件编译)时的老路
            if (要现场兜底(请求, 工程表, 本文件表))
            {
                string 当前模块 = 工程表 == null ? "" : 取归属(工程表.文件模块, 请求.当前文件);
                string 当前工程 = 工程表 == null ? "" : 取归属(工程表.文件工程, 请求.当前文件);
                foreach (string 名 in 补全词表C.从文件收集名字(请求.现场正文, 请求.当前文件))
                    if (!被隐藏(工程表, 名, 当前模块, 当前工程))
                        池.Add(new 补全候选C(名, 候选来历.名字, 名));
            }
            return 补全词表C.排序匹配(请求.词, 池, 上限, 模糊规则);
        }

        // ---------------- 代码片段(C28) ----------------

        /// <summary>
        /// C28:代码片段骨架那一档候选 —— 数据**直接读 `片段表C.全部`**(不抄第二份:
        /// 片段表里加一条,补全候选自动多一条,与帮助站点"表 → 现场生成"同一套钉子)。
        ///
        /// 显示成 `触发{…}`(用户原话:`如果` 与 `如果{…}` **分两条** —— 前一条是关键字、只补名字,
        /// 后一条是骨架、插整段);匹配按触发词算(`补全候选C.匹配名`),所以打 `rg` / `如` 两条都能命中。
        /// 插入文本这里先给一份"不缩进"的(真插的时候按当前层级现算,见 `片段插入C.算插入`),
        /// 万一有哪条路直接走 `确认文本` 也不会插出个空字符串。
        /// 只在"不在点后"那条路上加 —— `名字.` 后面要的是那个类型的成员,不是骨架。
        /// </summary>
        public static List<补全候选C> 片段候选()
        {
            var 结果 = new List<补全候选C>();
            foreach (var 项 in 片段表C.全部)
            {
                if (项 == null || 项.触发.Length == 0) continue;
                结果.Add(new 补全候选C($"{项.触发}{{…}}", 候选来历.片段, 片段表C.展开(项, ""), null, false,
                    $"片段:{项.说明}", 项));
            }
            return 结果;
        }

        // ---------------- 不在点后:变量 / 函数 / 结构 ----------------

        private static List<补全候选C> 名字候选(名字表C 工程表, 名字表C 本文件表, 补全请求C 请求)
        {
            var 结果 = new List<补全候选C>();
            string 当前模块 = 取归属(工程表.文件模块, 请求.当前文件);
            string 当前工程 = 取归属(工程表.文件工程, 请求.当前文件);

            // ① 变量:按作用域(全局 + 本函数里作用域盖住这一行的 + 别的文件的全局)
            foreach (var 项 in 可见变量(工程表, 本文件表, 请求))
            {
                if (项.名字.Length == 0) continue;
                if (项.是全局 && !看得见(工程表, 项.名字, 当前模块, 当前工程)) continue;
                string 写得出 = 写得出的名字(项.名字, 当前模块);
                结果.Add(new 补全候选C(写得出, 候选来历.名字, 写得出, null, false, 变量说明(项)));
            }

            // ② 函数 / 方法:整个工程(方法容器的成员留给 `点名.` 那条路)
            var 函数尾名数 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var 函数全名 = new List<string>();
            foreach (var (函数, _, _, _) in 工程表.函数范围)
            {
                if (函数.Length == 0 || 是容器成员(工程表, 函数)) continue;
                if (函数全名.Contains(函数, StringComparer.OrdinalIgnoreCase)) continue;
                if (!看得见(工程表, 函数, 当前模块, 当前工程)) continue;
                函数全名.Add(函数);
                string 尾 = 尾段(函数);
                函数尾名数[尾] = 函数尾名数.GetValueOrDefault(尾) + 1;
            }
            foreach (string 全名 in 函数全名)
            {
                string 尾 = 尾段(全名);
                string 写得出 = 写得出的名字(全名, 当前模块);
                // 同名函数不止一个(不同模块各写一个)时用"写得出来的那个全名",不然两条候选会挤成一条
                string 词 = 函数尾名数.GetValueOrDefault(尾) > 1 ? 写得出 : 尾;
                结果.Add(new 补全候选C(词, 候选来历.名字, 写得出 + "()", null, true,
                    签名说明(全名, 工程表.函数签名.GetValueOrDefault(全名))));
            }

            // ③ 结构 / 方法容器名(H60 步①起,容器名也是能写的类型名 —— `计数器 计数;` 声明第二份;
            //    `_方法_…` 是内部结构名,不给人写)
            foreach (var (结构名, 字段表) in 工程表.结构)
            {
                if (结构名.StartsWith("_方法_", StringComparison.Ordinal)) continue;
                // H68-3:元组类型(`[整数, 整数]`)是"现造的结构类型",名字不是能写的标识符 ⇒ 不当候选
                if (结构名.StartsWith("[", StringComparison.Ordinal)) continue;
                if (!看得见(工程表, 结构名, 当前模块, 当前工程)) continue;
                string 写得出 = 写得出的名字(结构名, 当前模块);
                bool 是容器类型名 = 工程表.容器类型.Contains(结构名);
                结果.Add(new 补全候选C(写得出, 候选来历.类型, 写得出, null, false,
                    是容器类型名 ? 容器类型说明(结构名, 字段表) : 结构说明(结构名, 字段表)));
            }
            // ④ 枚举名(H68-2):与结构一样能当类型名写(`颜色 画 = 绿;`);说明里把成员与值一起报出来
            foreach (var (枚举名, 成员们) in 工程表.枚举)
            {
                if (!看得见(工程表, 枚举名, 当前模块, 当前工程)) continue;
                string 写得出 = 写得出的名字(枚举名, 当前模块);
                结果.Add(new 补全候选C(写得出, 候选来历.类型, 写得出, null, false, 枚举说明(工程表, 枚举名, 成员们)));
            }
            return 结果;
        }

        /// <summary>
        /// 全部"这一行看得见"的变量:本文件的走作用域(`可见变量`),别的文件的**只收全局**
        /// (`本文件表` 是按文件拆过的,别的文件的局部不能混进来 —— 行号是各文件自己的)。
        /// </summary>
        private static List<名字项C> 可见变量(名字表C 工程表, 名字表C 本文件表, 补全请求C 请求)
        {
            int 行 = Math.Max(1, 请求.当前行);
            string? 所在函数 = 本文件表.行所属函数(行);
            var 结果 = new List<名字项C>();
            var 见过 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var 项 in 本文件表.可见变量(行, 所在函数))
                if (见过.Add(项.名字)) 结果.Add(项);
            foreach (var 项 in 工程表.变量)
                if (项.是全局 && 见过.Add(项.名字)) 结果.Add(项);
            return 结果;
        }

        // ---------------- 在点后:成员 ----------------

        private static List<补全候选C> 成员候选(名字表C? 工程表, 名字表C? 本文件表, 补全请求C 请求, int 上限,
            IReadOnlyCollection<string>? 模糊规则)
        {
            var 内建 = new List<补全候选C>(补全词表C.固定方法候选);
            if (请求.点名.Length == 0)
                return 补全词表C.排序匹配(请求.词, 内建, 上限, 模糊规则);

            // 含进来的库(窄口径,数据见 `库索引C`):`控制台.` 这类**不依赖编译结果**也能给成员 ——
            // 只在"工程表不认识这个模块名"(没编译过 / 那个库不在工程里)时查;查到了就用库那一份
            bool 工程表认得这个模块 = 工程表 != null
                && 工程表.模块名.Contains(请求.点名, StringComparer.OrdinalIgnoreCase);
            if (!工程表认得这个模块)
            {
                var 库里 = 库成员候选(请求, 上限, 模糊规则);
                if (库里.Count > 0) return 库里;
            }
            if (工程表 == null)
                return 补全词表C.排序匹配(请求.词, 内建, 上限, 模糊规则);

            string 当前模块 = 取归属(工程表.文件模块, 请求.当前文件);
            string 当前工程 = 取归属(工程表.文件工程, 请求.当前文件);
            名字表C 本表 = 本文件表 ?? 工程表;

            // 点号左边那个名字是什么?(① 变量 ② 结构 / 方法容器自己的名字 ③ 模块名)
            string 类型名 = "";
            bool 是列表 = false, 是数组 = false, 是字符串 = false;
            var 变量 = 可见变量(工程表, 本表, 请求).FirstOrDefault(x =>
                string.Equals(x.名字, 请求.点名, StringComparison.OrdinalIgnoreCase)
                || string.Equals(尾段(x.名字), 请求.点名, StringComparison.OrdinalIgnoreCase));
            if (变量 != null)
            {
                类型名 = 变量.类型名;
                是列表 = 变量.是列表;
                是数组 = 变量.是数组;
                是字符串 = 变量.是字符串参数 || string.Equals(类型名, "字符串", StringComparison.OrdinalIgnoreCase);
            }
            else if (工程表.结构.ContainsKey(请求.点名)) 类型名 = 请求.点名;
            else if (工程表.结构.ContainsKey("_方法_" + 请求.点名)) 类型名 = "_方法_" + 请求.点名;
            else if (工程表.枚举.ContainsKey(请求.点名)) 类型名 = 请求.点名;      // H68-2:`颜色.` 后面给成员

            var 结果 = new List<补全候选C>();
            if (类型名.Length == 0)
                return 模块成员候选(工程表, 请求, 当前模块, 当前工程, 内建, 上限, 模糊规则);

            if (是列表 || 是字符串)
                return 补全词表C.排序匹配(请求.词, 内建, 上限, 模糊规则);
            if (是数组) return [];                      // 数组没有 `.方法()` 这种写法:宁可不给,也不给错的

            string 容器名 = 类型名.StartsWith("_方法_", StringComparison.Ordinal) ? 类型名["_方法_".Length..] : "";
            // H68-2:枚举 —— `颜色.` 后面给的是**成员常量**(不是字段,也不插括号)
            if (工程表.枚举.TryGetValue(类型名, out var 枚举们))
            {
                if (看得见(工程表, 类型名, 当前模块, 当前工程))
                    foreach (string 成员 in 枚举们)
                    {
                        string 全名 = 类型名 + "." + 成员;
                        long 值 = 工程表.枚举成员.GetValueOrDefault(全名);
                        结果.Add(new 补全候选C(成员, 候选来历.字段, 成员, null, false, $"枚举成员 {全名} = {值}"));
                    }
                return 补全词表C.排序匹配(请求.词, 结果, 上限, 模糊规则);
            }
            if (工程表.结构.TryGetValue(类型名, out var 字段表))
            {
                string 结构名 = 容器名.Length > 0 ? 容器名 : 类型名;
                if (看得见(工程表, 结构名, 当前模块, 当前工程))
                    foreach (var 字段 in 字段表)
                    {
                        if (!字段看得见(工程表, 字段, 结构名, 当前模块, 当前工程)) continue;
                        结果.Add(new 补全候选C(字段.名字, 候选来历.字段, 字段.名字, null, false, 字段说明(字段)));
                    }
            }
            // 方法容器的成员方法(`棋盘.面积(…)`):函数范围里"前缀尾段 == 容器名"的那些
            if (容器名.Length > 0)
                foreach (var (函数, _, _, _) in 工程表.函数范围)
                {
                    if (!是它的成员(函数, 容器名, out string 成员)) continue;
                    if (!看得见(工程表, 函数, 当前模块, 当前工程)) continue;
                    if (结果.Any(x => x.词 == 成员)) continue;
                    结果.Add(new 补全候选C(成员, 候选来历.方法, 成员 + "()", null, true,
                        签名说明(函数, 工程表.函数签名.GetValueOrDefault(函数))));
                }
            // H60 步④:**结构自己的方法**(`点.到原点(…)`)—— 名字表里另有一格(结构名 → 方法定义名)
            if (工程表.结构方法.TryGetValue(类型名, out var 结构方法们))
                foreach (string 定义名 in 结构方法们)
                {
                    string 成员 = 尾段(定义名);
                    if (!看得见(工程表, 定义名, 当前模块, 当前工程)) continue;
                    if (结果.Any(x => x.词 == 成员)) continue;
                    结果.Add(new 补全候选C(成员, 候选来历.方法, 成员 + "()", null, true,
                        签名说明(定义名, 工程表.函数签名.GetValueOrDefault(定义名))));
                }
            return 补全词表C.排序匹配(请求.词, 结果, 上限, 模糊规则);
        }

        /// <summary> `模块.` 这种写法:给这个模块里的函数与结构(插入一律写全名,别的模块里就得这么写) </summary>
        private static List<补全候选C> 模块成员候选(名字表C 工程表, 补全请求C 请求, string 当前模块, string 当前工程,
            List<补全候选C> 内建, int 上限, IReadOnlyCollection<string>? 模糊规则)
        {
            if (!工程表.模块名.Contains(请求.点名, StringComparer.OrdinalIgnoreCase))
                return 补全词表C.排序匹配(请求.词, 内建, 上限, 模糊规则);

            var 结果 = new List<补全候选C>();
            foreach (var (函数, _, _, _) in 工程表.函数范围)
            {
                if (是容器成员(工程表, 函数)) continue;
                if (!string.Equals(名字的模块(函数), 请求.点名, StringComparison.OrdinalIgnoreCase)) continue;
                if (!看得见(工程表, 函数, 当前模块, 当前工程)) continue;
                string 写得出 = 写得出的名字(函数, 当前模块);
                if (结果.Any(x => x.词 == 尾段(函数))) continue;
                结果.Add(new 补全候选C(尾段(函数), 候选来历.名字, 写得出 + "()", null, true,
                    签名说明(函数, 工程表.函数签名.GetValueOrDefault(函数))));
            }
            foreach (var (结构名, 字段表) in 工程表.结构)
            {
                if (结构名.StartsWith("_方法_", StringComparison.Ordinal)) continue;
                // H68-3:元组类型不当候选(理由同上)
                if (结构名.StartsWith("[", StringComparison.Ordinal)) continue;
                if (!string.Equals(名字的模块(结构名), 请求.点名, StringComparison.OrdinalIgnoreCase)) continue;
                if (!看得见(工程表, 结构名, 当前模块, 当前工程)) continue;
                string 写得出 = 写得出的名字(结构名, 当前模块);
                if (结果.Any(x => x.词 == 尾段(结构名))) continue;
                结果.Add(new 补全候选C(尾段(结构名), 候选来历.类型, 写得出, null, false,
                    工程表.容器类型.Contains(结构名) ? 容器类型说明(结构名, 字段表) : 结构说明(结构名, 字段表)));
            }
            if (结果.Count == 0) 结果.AddRange(内建);
            return 补全词表C.排序匹配(请求.词, 结果, 上限, 模糊规则);
        }

        /// <summary>
        /// `模块.` 的成员 —— 查**本文件 `包含` 进来的库**(窄口径;数据取自库源码,见 `库索引C`)。
        /// 与 `模块成员候选` 的分工:那一条走"最近一次编译结果"的名字表(可见性 / 私有 / 程序集都按它判),
        /// 这一条**不依赖编译结果**(没编译过也补得出来),给的是那个库的**公开方法**(带签名与作用当说明)。
        /// 库里声明的**结构**这一档没收(要用结构就 `包含` 之后照名字写,补全先不做 —— 宁可少给)。
        /// </summary>
        private static List<补全候选C> 库成员候选(补全请求C 请求, int 上限, IReadOnlyCollection<string>? 模糊规则)
        {
            var 库 = 库索引C.取(请求.当前文件, 请求.现场正文);
            if (库.空 || !库.模块方法.TryGetValue(请求.点名, out var 方法们)) return [];

            var 结果 = new List<补全候选C>();
            foreach (var (名, 签名, 作用) in 方法们)
            {
                if (结果.Any(x => x.词 == 名)) continue;
                结果.Add(new 补全候选C(名, 候选来历.方法, 名 + "()", null, true,
                    作用.Length > 0 ? $"{签名} —— {作用}" : 签名));
            }
            return 补全词表C.排序匹配(请求.词, 结果, 上限, 模糊规则);
        }

        // ---------------- 可见性(与编译器同一份数据、同一个判据) ----------------

        /// <summary> 这个名字在当前文件里看得见吗(`私有`/`程序集` 的口径与 `中间层C.模块.cs` 的 `校验可取用` 一致) </summary>
        private static bool 看得见(名字表C 工程表, string 全名, string 当前模块, string 当前工程)
        {
            var 项 = 工程表.查可见性(全名);
            return 项 == null || 看得见(项, 当前模块, 当前工程);
        }

        private static bool 看得见(可见性项C 项, string 当前模块, string 当前工程)
        {
            // `私有`:只有声明它的模块里能用(容器前缀标了私有,成员跟着算 —— 声明模块记的就是那个前缀)
            if (项.私有 && !string.Equals(项.声明模块, 当前模块, StringComparison.OrdinalIgnoreCase)) return false;
            // `程序集`:只有同一个 `.hbp` 工程里能用(声明工程为空 = 匿名工程,与 `公共` 等价)
            if (项.程序集 && 项.声明工程.Length > 0
                && !string.Equals(项.声明工程, 当前工程, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary> 字段的可见性(A4 那套:字段自己标了才拦;私有看结构所在模块,程序集看结构声明工程)</summary>
        private static bool 字段看得见(名字表C 工程表, 字段项C 字段, string 结构名, string 当前模块, string 当前工程)
        {
            if (!字段.私有 && !字段.程序集) return true;
            if (字段.私有 && !string.Equals(名字的模块(结构名), 当前模块, StringComparison.OrdinalIgnoreCase)) return false;
            if (字段.程序集)
            {
                string 处 = 工程表.查可见性(结构名)?.声明工程 ?? "";
                if (处.Length > 0 && !string.Equals(处, 当前工程, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary> 现场扫出来的**裸名**该不该拦:工程里同名(或同尾名)的声明全是看不见的私有/程序集成员 </summary>
        private static bool 被隐藏(名字表C? 工程表, string 名, string 当前模块, string 当前工程)
        {
            if (工程表 == null || 名.Length == 0) return false;
            bool 有隐藏 = false;
            foreach (var (全名, 项) in 工程表.可见性)
            {
                if (!string.Equals(尾段(全名), 名, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(全名, 名, StringComparison.OrdinalIgnoreCase)) continue;
                if (看得见(项, 当前模块, 当前工程)) return false;      // 有一个看得见的同名就照给
                有隐藏 = true;
            }
            return 有隐藏;
        }

        // ---------------- 小工具 ----------------

        /// <summary> 没有这个文件的编译结果时才退回现场扫(编译过就以编译结果为准:那才有作用域与可见性)</summary>
        private static bool 要现场兜底(补全请求C 请求, 名字表C? 工程表, 名字表C? 本文件表)
        {
            if (!请求.现场兜底 || 请求.现场正文.Length == 0) return false;
            if (工程表 == null || 本文件表 == null) return true;
            return 本文件表.变量.Count == 0 && 本文件表.函数范围.Count == 0;
        }

        /// <summary> 全名里"点前面那一段"是不是一个**模块**(不是模块就是方法容器)</summary>
        private static bool 是容器成员(名字表C 工程表, string 全名)
        {
            int 点 = 全名.IndexOf('.');
            if (点 <= 0) return false;
            string 余 = 工程表.模块名.Contains(全名[..点], StringComparer.OrdinalIgnoreCase) ? 全名[(点 + 1)..] : 全名;
            return 余.Contains('.');
        }

        /// <summary> 全名是不是 `点名` 那个容器的成员(容器名按尾段比:`数学.棋盘.面积` 也算 `棋盘.` 的成员)</summary>
        private static bool 是它的成员(string 全名, string 点名, out string 成员)
        {
            成员 = "";
            int 点 = 全名.LastIndexOf('.');
            if (点 <= 0) return false;
            string 前 = 全名[..点];
            if (!string.Equals(前, 点名, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(尾段(前), 尾段(点名), StringComparison.OrdinalIgnoreCase)) return false;
            成员 = 全名[(点 + 1)..];
            return 成员.Length > 0;
        }

        /// <summary> 在当前文件里"写得出来"的那个名字:同一个模块(或都没有模块)用尾名,别的模块要写全名 </summary>
        private static string 写得出的名字(string 全名, string 当前模块)
        {
            int 点 = 全名.IndexOf('.');
            if (点 <= 0) return 全名;
            return string.Equals(全名[..点], 当前模块, StringComparison.OrdinalIgnoreCase) ? 全名[(点 + 1)..] : 全名;
        }

        private static string 尾段(string 名)
        {
            int 点 = 名.LastIndexOf('.');
            return 点 < 0 ? 名 : 名[(点 + 1)..];
        }

        private static string 名字的模块(string 全名)
        {
            int 点 = 全名.IndexOf('.');
            return 点 > 0 ? 全名[..点] : "";
        }

        private static string 取归属(Dictionary<string, string> 表, string 文件)
        {
            if (文件.Length == 0) return "";
            if (表.TryGetValue(文件, out string? 命中)) return 命中 ?? "";
            foreach (var (路径, 值) in 表)
                if (文件判定C.同文件(路径, 文件)) return 值 ?? "";
            // 单文件编译 / 拆表按文件名对不上:整篇只有一个模块时那条记在空文件名上
            return 表.TryGetValue("", out string? 匿名) ? 匿名 ?? "" : "";
        }

        /// <summary> 参数提示:函数 / 方法候选右侧那行小字(参数表 + 返回类型)</summary>
        private static string 签名说明(string 全名, 函数签名C? 签名)
        {
            if (签名 == null) return $"方法 {全名}";
            string 参 = string.Join(", ", 签名.参数.Select(参数文本));
            string 返 = 签名.返回类型.Length > 0 ? $" 返回 {签名.返回类型}" : "";
            return $"方法 {全名}({参}){返}";
        }

        private static string 参数文本(参数项C 参数) =>
            $"{参数.类型名} {参数.名字}" + (参数.是出 ? " 出" : "") + (参数.是数组 ? "[]" : "");

        private static string 变量说明(名字项C 项)
        {
            string 类型 = 名字表C.显示类型名(项.类型名);
            if (项.是列表) 类型 = $"列表[{项.元素个数}]({类型})";
            else if (项.是数组) 类型 = $"数组[{项.元素个数}]({类型})";
            else if (项.是引用) 类型 = $"引用({类型})";
            // H60 步①:方法容器的那一份"单例变量"与容器类型**同名**(两个身份)——
            // 同名时补全只留得下一条(按词去重),留下的多半是这条变量;那句小字必须把另一重身份说出来,
            // 否则用户永远看不出"这个名字还能当类型名写 `计数器 计数;`"。
            string 双身份 = 项.类型名.StartsWith("_方法_", StringComparison.Ordinal)
                ? $" · 也是类型名({类型} 实例;)" : "";
            return $"变量 {项.名字}:{类型}{(项.是全局 ? " · 全局" : "")}{双身份}";
        }

        private static string 字段说明(字段项C 字段)
        {
            string 类型 = 字段.是列表 ? $"列表({名字表C.显示类型名(字段.类型名)})"
                : 字段.是数组 ? $"数组[{字段.元素个数}]({名字表C.显示类型名(字段.类型名)})"
                : 字段.是字符串 ? "字符串" : 名字表C.显示类型名(字段.类型名);
            string 额外 = (字段.私有 ? " · 私有" : "") + (字段.程序集 ? " · 程序集" : "");
            return $"字段 {字段.名字}:{类型}{额外}";
        }

        private static string 结构说明(string 结构名, List<字段项C> 字段表)
        {
            var 名字们 = 字段表.Take(6).Select(f => $"{f.名字}({名字表C.显示类型名(f.类型名)})");
            string 尾 = 字段表.Count > 6 ? " …" : "";
            return $"结构 {结构名}:{string.Join(", ", 名字们)}{尾}";
        }

        /// <summary> **枚举**那一档的候选说明(H68-2):写清成员与值(枚举不占存储,成员是编译期常量)</summary>
        private static string 枚举说明(名字表C 工程表, string 枚举名, List<string> 成员们)
        {
            var 名字们 = 成员们.Take(6).Select(m => $"{m} = {工程表.枚举成员.GetValueOrDefault(枚举名 + "." + m)}");
            string 尾 = 成员们.Count > 6 ? " …" : "";
            return $"枚举 {枚举名}:{string.Join(", ", 名字们)}{尾}";
        }

        /// <summary>
        /// **方法容器名**那一档的候选说明(H60 步①):它与结构一样能当类型名用,但写出来的是
        /// `静态 方法 计数器(...)` —— 说明里就照实说"方法容器",不冒充结构(H46:一个词管一件事)。
        /// </summary>
        private static string 容器类型说明(string 容器名, List<字段项C> 字段表)
        {
            if (字段表.Count == 0) return $"方法容器 {容器名}:没有常驻字段(纯名字空间)";
            var 名字们 = 字段表.Take(6).Select(f => $"{f.名字}({名字表C.显示类型名(f.类型名)})");
            string 尾 = 字段表.Count > 6 ? " …" : "";
            return $"方法容器 {容器名}:{string.Join(", ", 名字们)}{尾}";
        }
    }
}
