using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary> 中间层展开器的一部分:流程关键字展开:条件分支 / 循环 / 跳出与继续。(第88批从 `中间层C.cs` 按 region 原样搬出,逻辑一字未改) </summary>
    internal partial class 中间层展开器
    {
        #region 流程关键字展开:条件分支(如果/选择 if/switch)
        // ---------------- if ----------------
        /// <summary>
        /// `如果 (条件) { … }` / `否则 { … }` / **`否则 如果 (条件) { … }`(else-if 链)**。
        /// else-if 就是语法糖:所有分支共用**同一个结束标签**,一层层当普通 if 展开 ——
        /// `否则 如果 (条件一) … 否则 如果 (条件二) … 否则 …` 不会让标签数量翻倍。
        /// 两种排版都认:`否则 如果 (…)(同一行)` 和 `否则` 换行再写 `如果 (…)`。
        /// </summary>
        private void 处理如果(ref int 位置) => 处理如果(ref 位置, 外层结束标签: null);

        private void 处理如果(ref int 位置, string? 外层结束标签)
        {
            // T20(2026-09-27 评测整改):`否则 如果 (…)` 链走**循环**,不再尾递归。
            // 原来每一段 `否则 如果` 都递归一次 `处理如果` —— 链长到 5000 段时栈被吃穿
            // (实测:`否则 如果` 2000 段能编、5000 段 `0xC00000FD` 把 hbcc 自己打死,连一句报错都没有)。
            // 这条链是**平**的(所有分支共用同一个结束标签),循环与递归语义一字不差,只是不再占栈。
            string 结束标签 = 外层结束标签 ?? 新标签("IfEnd");
            while (true)
            {
                int 行号 = _伪行号表[位置];
                string 条件 = 取括号内容(_伪行表[位置], 位置);
                if (条件.Length == 0) throw 报错(行号, "if/如果 缺少条件", "if requires a condition");
                string 否则标签 = 新标签("IfElse");
                位置++;
                输出条件跳过(条件, 行号, 否则标签);            // 条件为假 → 跳到 否则/结束
                处理块(ref 位置);

                bool 有否则 = 位置 < _伪行表.Count && 中间层C.到英文关键字(取首词(_伪行表[位置])) == "else";
                if (!有否则)
                {
                    发出($"{否则标签}:");                        // 无 else：假分支即到达结束
                    发出($"{结束标签}:");
                    return;
                }

                发出($"JMP {结束标签}");                     // 真分支结束后跳过 否则块
                发出($"{否则标签}:");

                // ① 同一行:`否则 如果 (…)` —— 把这一行就地改写成 `如果 (…)`,位置不动,下一圈当普通 if 处理
                string 否则行 = _伪行表[位置];
                string 余下 = 否则行[取首词(否则行).Length..].Trim();
                if (余下.Length > 0)
                {
                    if (中间层C.到英文关键字(取首词(余下)) != "if")
                        throw 报错(否则行 == null ? 行号 : _伪行号表[位置],
                            $"否则 后面要么直接跟 '{{',要么跟一个 如果(…):'{否则行}'", $"'else' must be followed by '{{' or 'if (…)'");
                    _伪行表[位置] = 余下;
                    continue;                                   // 下一圈:再处理一段 `如果`(结束标签由最后那一圈发)
                }

                // ② 换行写:`否则` 一行,下一行才是 `如果 (…)`
                位置++;
                if (位置 < _伪行表.Count && 中间层C.到英文关键字(取首词(_伪行表[位置])) == "if")
                    continue;

                if (位置 >= _伪行表.Count || _伪行表[位置] != "{")
                    throw 报错(_伪行号表[Math.Min(位置, _伪行表.Count - 1)], "else/否则 后缺少 '{'", "expected '{' after else");
                处理块(ref 位置);
                发出($"{结束标签}:");
                return;
            }
        }

        // ---------------- switch ----------------
        private void 处理选择(ref int 位置)
        {
            int 行号 = _伪行号表[位置];
            string 值文本 = 取括号内容(_伪行表[位置], 位置).Trim();
            // 值:寄存器 / 变量 / 字段 / 表达式都行(走 求值表达式,顺带拿到"有没有符号",区间比较才按有符号算)
            // 注意"可写: false":用户写的裸寄存器(R0 这种)绝不能被后面的比较偏置改掉
            表达式值 选择值;
            if (解析寄存器(值文本, out string 值寄存器)) 选择值 = 表达式值.寄存器值(值寄存器, 可写: false);
            else
            {
                准备语句占用(值文本);
                选择值 = 落到寄存器(求值表达式(值文本, 行号, 值文本));
            }
            // 有符号的值:先把"异或 0x80000000"做一次;后面每个测试都拿偏置后的值比偏置后的界。
            // 这样 ①选择值不会被每个测试各改一次(踩过:多值/区间全都不中) ②每个测试不用再复制一份临时寄存器
            bool 值有符号 = 选择值.有符号;
            if (值有符号) 选择值 = 偏置比较值(选择值);
            表达式值 界值(long 界) => 表达式值.常量值(值有符号 ? (uint)界 ^ 0x80000000u : (uint)界);
            位置++;
            if (位置 >= _伪行表.Count || _伪行表[位置] != "{") throw 报错(行号, "switch/选择 后缺少 '{'", "expected '{' after switch");
            位置++;

            string 结束标签 = 新标签("SwEnd");
            int 选择编号 = ++_选择计数;   // 本 switch 的固定编号，用于分支标签命名
            _上下文栈.Add((结束标签, null));
            int 项序号 = 0;     // 每项(情况/默认)的序号，用于生成分支标签名
            bool 已见默认 = false;

            while (位置 < _伪行表.Count && _伪行表[位置] != "}")
            {
                string 头文本 = _伪行表[位置];
                string 首词 = 取首词(头文本);
                string? 关键字 = 中间层C.到英文关键字(首词);
                if (关键字 != "case" && 关键字 != "default")
                    throw 报错(_伪行号表[位置], "switch/选择 内只能写 情况/case、默认/default", "only case/default allowed inside switch");
                int 头行号 = _伪行号表[位置];

                if (关键字 == "default")
                {
                    if (已见默认) throw 报错(头行号, "默认/default 只能出现一次", "duplicate default");
                    已见默认 = true;
                    位置++;
                    发出($"_fCase{选择编号}_{项序号}:");
                    处理块(ref 位置);
                    if (位置 < _伪行表.Count && _伪行表[位置] != "}")
                        throw 报错(_伪行号表[位置], "默认/default 必须放在 选择/switch 的最后", "default must be the last branch");
                }
                else // case
                {
                    string 常量文本 = 头文本[首词.Length..].Trim();
                    if (常量文本.Length == 0) throw 报错(头行号, "case/情况 缺少常量", "case requires a constant");
                    var 段们 = 解析情况常量(常量文本, 头行号);
                    位置++;
                    if (位置 >= _伪行表.Count || _伪行表[位置] != "{") throw 报错(头行号, "case/情况 后缺少 '{'", "expected '{' after case");

                    // 先解析分支体到临时缓冲（需要确定“不匹配时跳到哪”）
                    _缓冲栈.Push([]);
                    处理块(ref 位置);
                    var 分支体 = _缓冲栈.Pop();

                    // “一项都没匹配上时跳去哪”：还有下一项 → 下一项的分支标签；否则 → switch 结束
                    bool 还有下一项 = 位置 < _伪行表.Count && _伪行表[位置] != "}";
                    string 跳过目标 = 还有下一项 ? $"_fCase{选择编号}_{项序号 + 1}" : 结束标签;
                    string 命中标签 = $"_fCase{选择编号}_{项序号}中";

                    if (项序号 > 0) 发出($"_fCase{选择编号}_{项序号}:");
                    // 一项可以是多个值/区间(`情况 1, 2, 5` / `情况 1..5`):逐个测,中一个就跳到"命中"
                    for (int 段号 = 0; 段号 < 段们.Count; 段号++)
                    {
                        var (低, 高) = 段们[段号];
                        if (低 == 高)
                        {
                            比较跳转(选择值, 界值(低), "==", 头行号, 命中标签, 跳则真: true);
                            continue;
                        }
                        string 本段不中 = $"_fCase{选择编号}_{项序号}段{段号}";
                        比较跳转(选择值, 界值(低), "<", 头行号, 本段不中, 跳则真: true);
                        比较跳转(选择值, 界值(高), ">", 头行号, 本段不中, 跳则真: true);
                        发出($"JMP {命中标签}");
                        发出($"{本段不中}:");
                    }
                    发出($"JMP {跳过目标}");
                    发出($"{命中标签}:");
                    foreach (string 行 in 分支体) 发出(行);
                    发出($"JMP {结束标签}");
                }
                项序号++;
            }

            if (位置 >= _伪行表.Count || _伪行表[位置] != "}") throw 报错(行号, "switch/选择 缺少 '}'", "missing '}' for switch");
            位置++;
            _上下文栈.RemoveAt(_上下文栈.Count - 1);
            发出($"{结束标签}:");
        }
        #endregion
        #region 流程关键字展开:循环(循环/当/遍历 for/while/foreach)
        // ---------------- for ----------------
        private void 处理循环(ref int 位置)
        {
            int 行号 = _伪行号表[位置];
            string 头内容 = 取括号内容(_伪行表[位置], 位置);
            string[] 三段 = 头内容.Split(';');
            if (三段.Length != 3) throw 报错(行号, "for/循环 需要 '初始; 条件; 步进' 三段", "for requires init;cond;step");
            string 初始化 = 三段[0].Trim();
            string 条件 = 三段[1].Trim();
            string 步进 = 三段[2].Trim();
            位置++;
            if (位置 >= _伪行表.Count || _伪行表[位置] != "{") throw 报错(行号, "for/循环 后缺少 '{'", "expected '{' after for");

            string 开始标签 = 新标签("ForStart");
            string 步进标签 = 新标签("ForStep");
            string 结束标签 = 新标签("ForEnd");

            // for 头允许声明(如 for (short i = 1; ...)):声明归本循环作用域
            bool 头声明 = 初始化.Length > 0 && 是声明行(初始化);
            循环保留C? 保留 = null;
            if (头声明) 压作用域(行号);
            try
            {
                if (初始化.Length > 0)
                {
                    if (头声明) 处理声明文本(初始化, 行号);
                    else 输出赋值(初始化, 行号);
                }
                // 优化:声明/初始化做完之后再把不变量提出来(顺序不能反:初始化可能写它们);
                // 条件和步进每轮都要跑,所以它们的读也一起算进候选,但"被写"要连步进一起看
                int 体止 = 找块结束(位置);
                var 体文本 = new List<string>();
                if (条件.Length > 0) 体文本.Add(条件);
                for (int i = 位置 + 1; i < 体止; i++) 体文本.Add(_伪行表[i]);
                if (步进.Length > 0) 体文本.Add(步进);
                // P1 数组地址增量:步进是"归纳变量每轮 +常量"、计数器不会回绕、体里也没再改它时,
                // 体里的 `数组[归纳变量]` 走常驻步进指针
                归纳信息C? 归纳 = null;
                if (解析归纳步进(步进) is { } 归)
                {
                    var 体行 = new List<string>();
                    for (int i = 位置 + 1; i < 体止; i++) 体行.Add(_伪行表[i]);
                    uint? 初值 = 解析初始化常量(初始化, 归.变量);
                    if (!体里改过归纳变量(体行, 归.变量) && 归纳指针安全(归.变量, 归.步长, 初值, 条件))
                        归纳 = new 归纳信息C(归.变量, 归.步长, 初值);
                }
                保留 = 提升循环不变量(体文本, 行号, 归纳, 条件.Length > 0 ? 1 : 0);
                发出($"{开始标签}:");
                if (条件.Length > 0) 输出条件跳过(条件, 行号, 结束标签);
                _上下文栈.Add((结束标签, 步进标签));
                try
                {
                    处理块(ref 位置);
                    发出($"{步进标签}:");
                    if (步进.Length > 0) 输出赋值(步进, 行号);
                    // 步进指针跟着归纳变量一起往前走(这一层的常驻寄存器此刻还没还,不会被步进自己的临时寄存器占掉)
                    if (保留 is { } 层 && 归纳 is { } 归2)
                        foreach (var 项 in 层.步进指针)
                            发出($"ADD_IMMEDIATE {项.寄存器} {项.元素宽 * 归2.步长}");
                }
                finally { 收起循环保留(保留); }
               发出($"JMP {开始标签}");
                _上下文栈.RemoveAt(_上下文栈.Count - 1);
                发出($"{结束标签}:");
            }
            finally
            {
                if (头声明) 弹作用域(上一伪行源行号(位置));
            }
        }

        /// <summary> 是否是变量声明行(类型前置 / 结构名 / **枚举名**(H68-2)/ **元组类型**(H68-3)/ 变量|var / 引用) </summary>
        private bool 是声明行(string 内容)
        {
            string 首词 = 取声明类型词(内容);
            if (引用声明拆解(内容, out _, out _, out _, out _)) return true;
            // H68-3:元组写法一律走声明那条路 —— 类型写坏了也得由元组那一侧报"哪儿写坏了",
            // 不能在这儿判成 false 掉进"未声明的变量或非法赋值左值"(那句话看不出是元组写错了)。
            // 判据看**整行**(不是 首词):`[整数, 整数 初值;` 这种少一个 `]` 的,首词是空串。
            if (是元组类型词(内容)) return true;
            if (首词.Length == 0) return false;
            if (首词 is "字符串" || string.Equals(首词, "string", StringComparison.OrdinalIgnoreCase)) return true;
            // G2:`回调 名字 = 地址(函数名);` —— 与 `字符串` 同一档的附加类型名,走声明那条路
            if (首词 is "回调" || string.Equals(首词, "callback", StringComparison.OrdinalIgnoreCase)) return true;
            return 类型C.是类型名(首词) || 是结构名(首词) || 是枚举名(首词) || 中间层C.到英文关键字(首词) == "var";
        }

        /// <summary> `引用 点 p = 位置` / `ref 点 p = 位置`:拆成 (指向类型词, 变量名, 初值文本);不是引用声明返回 false </summary>
        private bool 引用声明拆解(string 内容, out string 类型词, out string 名称, out string 初值文本, out string 指向原文)
        {
            类型词 = 名称 = 初值文本 = 指向原文 = "";
            string 首词 = 取首词(内容);
            if (!(首词 == "引用" || string.Equals(首词, "ref", StringComparison.OrdinalIgnoreCase))) return false;
            string 余下 = 内容[首词.Length..].Trim();
            if (余下.Length == 0) return false;
            类型词 = 取声明类型词(余下);      // H68-3 元组也走这里(`引用 [整数, 整数] p = 位置`)
            if (类型词.Length == 0) return false;
            余下 = 余下[类型词.Length..].Trim();
            int 等号 = 余下.IndexOf('=');
            名称 = (等号 >= 0 ? 余下[..等号] : 余下).Trim();
            初值文本 = 等号 >= 0 ? 余下[(等号 + 1)..].Trim() : "";
            指向原文 = 初值文本;
            return 名称.Length > 0;
        }

        // ---------------- while / 当 ----------------
        private void 处理当(ref int 位置)
        {
            int 行号 = _伪行号表[位置];
            string 条件 = 取括号内容(_伪行表[位置], 位置);
            if (条件.Length == 0) throw 报错(行号, "当/while 缺少条件", "while requires a condition");
            位置++;
            if (位置 >= _伪行表.Count || _伪行表[位置] != "{") throw 报错(行号, "当/while 后缺少 '{'", "expected '{' after while");

            string 开始标签 = 新标签("WhileStart");
            string 结束标签 = 新标签("WhileEnd");
            // 优化:条件和体里"每轮都重读、但不会变"的变量/常量,提到循环外面只做一次(必须在 开始标签 之前)
            int 体止 = 找块结束(位置);
            var 体文本 = new List<string> { 条件 };
            for (int i = 位置 + 1; i < 体止; i++) 体文本.Add(_伪行表[i]);
            var 保留 = 提升循环不变量(体文本, 行号);
            发出($"{开始标签}:");
            输出条件跳过(条件, 行号, 结束标签);   // 条件为假 → 跳到 结束
            // continue 语义:回到条件判断处(即开始标签)
            _上下文栈.Add((结束标签, 开始标签));
            try { 处理块(ref 位置); }
            finally { 收起循环保留(保留); }
            发出($"JMP {开始标签}");
            _上下文栈.RemoveAt(_上下文栈.Count - 1);
            发出($"{结束标签}:");
        }
        private void 处理遍历(ref int 位置)
        {
            int 行号 = _伪行号表[位置];
            string 头内容 = 取括号内容(_伪行表[位置], 位置);
            // 形如: (当前, 起始 地址, 数量 字节数) / (当前, 地址, 字节数)
            // **批 10 起三个操作数都是变量/结构字段/数组元素**(以前是三个寄存器):
            //   每轮"读地址变量 → 按字节取出来 → 写进元素变量";地址变量每次 +1、数量变量每次 -1
            //   (和以前寄存器那版语义一致:循环跑完地址变量顶到末尾、数量变量归 0)。
            string[] 参数段表 = 头内容.Split(',');
            if (参数段表.Length != 3)
                throw 报错(行号, "遍历 需要 '元素变量, 起始 地址变量, 数量 字节数变量'(英文 foreach)",
                    "foreach requires (element, start-address, count)");
            var 名 = new string[3];
            for (int i = 0; i < 3; i++)
            {
                string 段内容 = 参数段表[i].Trim();
                string? 段首词 = 取首词(段内容);
                if (段首词 != null && 中间层C.到英文关键字(段首词) is "start" or "count")
                    段内容 = 段内容[段首词.Length..].Trim();
                名[i] = 段内容;
                if (解析寄存器(段内容, out string 旧寄存器))
                    throw 报错(行号, $"遍历 不再写寄存器 {旧寄存器} 了:三个操作数都改成变量 —— " +
                        $"`遍历 (byte 当前, 起始 地址, 数量 剩余)`(寄存器只在汇编层出现;要按字节遍历内存就把" +
                        $"当前字节/地址/剩余字节数各声明成一个变量)",
                        $"foreach takes variables now, not registers ({旧寄存器})");
            }
            var 链表 = new 引用链C[3];
            for (int i = 0; i < 3; i++)
                链表[i] = 解析引用链(名[i], 行号)
                    ?? throw 报错(行号, $"遍历 的第 {i + 1} 个操作数要是变量(写法见 `遍历 (byte 当前, 起始 地址, 数量 剩余)`):'{名[i]}'",
                        $"foreach operand {i + 1} must be a variable: '{名[i]}'");
            if (链表[0].基名 == 链表[1].基名 || 链表[0].基名 == 链表[2].基名 || 链表[1].基名 == 链表[2].基名)
                throw 报错(行号, "遍历 的三个变量必须互不相同", "foreach variables must be distinct");

            位置++;
            if (位置 >= _伪行表.Count || _伪行表[位置] != "{") throw 报错(行号, "foreach/遍历 后缺少 '{'", "expected '{' after foreach");

            string 开始标签 = 新标签("FeStart");
            string 步进标签 = 新标签("FeStep");
            string 结束标签 = 新标签("FeEnd");
            // 外面循环(循环/当)常驻的那些寄存器要一路护住 —— 不然"自成一段"会把它们当空闲临时用掉
            string[] 活的 = [.. _循环保留栈.SelectMany(层 => 层.寄存器)];
            _上下文栈.Add((结束标签, 步进标签));
            _缓冲栈.Push([]);
            处理块(ref 位置);
            var 循环体 = _缓冲栈.Pop();

            发出($"{开始标签}:");
            // ① 剩余字节数 == 0 就出循环;否则按字节读出来写进元素变量
            自成一段(() =>
            {
                string 剩余 = 链读到寄存器(链表[2], 行号, 名[2]);
                发出($"JZ {剩余} {结束标签}");
                string 地址 = 链读到寄存器(链表[1], 行号, 名[1]);
                string 元素 = 新临时();
                发出($"LOAD_INDIRECT {元素} {地址}");
                链写(元素, 链表[0], 行号, 名[0]);
            }, 活的);
            foreach (string 行 in 循环体) 发出(行);
            发出($"{步进标签}:");
            自成一段(() =>
            {
                string 地址 = 链读到寄存器(链表[1], 行号, 名[1]);
                发出($"ADD_IMMEDIATE {地址} 1");
                链写(地址, 链表[1], 行号, 名[1]);
            }, 活的);
            自成一段(() =>
            {
                string 剩余 = 链读到寄存器(链表[2], 行号, 名[2]);
                发出($"SUB_IMMEDIATE {剩余} 1");
                链写(剩余, 链表[2], 行号, 名[2]);
            }, 活的);
            发出($"JMP {开始标签}");
            _上下文栈.RemoveAt(_上下文栈.Count - 1);
            发出($"{结束标签}:");
        }
        #endregion
        #region 流程关键字展开:跳出与继续(break/continue)
        // ---------------- break / continue ----------------
        /// <summary>
        /// `跳出 [层数]` / `break [层数]`:不带层数 = 跳出最内层;带层数(常量)= 一次跳出 N 层。
        /// 实现的依据是编译期的 `_上下文栈`(嵌套 循环/选择 的跳出标签都在里面),所以**一条跳转指令**就够,
        /// ISA 一条没加。层数按"跳出点"算:选择 也算一层(与 C 的 break 一致)。
        /// </summary>
        private void 处理跳出(ref int 位置)
        {
            if (_上下文栈.Count == 0) throw 报错(_伪行号表[位置], "跳出/break 只能用在 循环/选择 内", "break outside loop/switch");
            int 层 = 取跳出层数(_伪行内容(位置), 行号: _伪行号表[位置], "跳出");
            if (层 > _上下文栈.Count)
                throw 报错(_伪行号表[位置], $"{取首词(_伪行内容(位置))} {层} 越界了:一共只套了 {_上下文栈.Count} 层(循环/选择都算)",
                    "break level out of range");
            发出($"JMP {_上下文栈[^层].跳出}");
            位置++;
        }

        /// <summary>
        /// `继续 [层数]` / `continue [层数]`:不带层数 = 下一次最内层循环;带层数 = 下一次第 N 层循环。
        /// 中间夹着的"选择"会被跳过(选择没有"下一次"这一说):层数只数**循环**。
        /// </summary>
        private void 处理继续(ref int 位置)
        {            if (_上下文栈.Count == 0 || _上下文栈[^1].继续 is not { } 目标)
                throw 报错(_伪行号表[位置], "继续/continue 只能用在循环内", "continue outside loop");
            int 层 = 取跳出层数(_伪行内容(位置), 行号: _伪行号表[位置], "继续");
            if (层 == 1)
            {
                发出($"JMP {目标}");
                位置++;
                return;
            }
            // 往上找第 层 个"有继续目标"的上下文(选择 的 继续 是 null,不当一层循环)
            int 见过 = 1;                       // 最内层那一个已经算过
            for (int i = _上下文栈.Count - 2; i >= 0; i--)
            {
                if (_上下文栈[i].继续 is not { } 上) continue;
                if (++见过 == 层)
                {
                    发出($"JMP {上}");
                    位置++;
                    return;
                }
            }
            throw 报错(_伪行号表[位置], $"{取首词(_伪行内容(位置))} {层} 越界了:外面只有 {见过 - 1} 层循环",
                "break/continue level out of range");
        }

        /// <summary> 这一行的原文(保留 `跳出 2` 里的数字;用来取层数) </summary>
        private string _伪行内容(int 位置) => 位置 >= 0 && 位置 < _伪行表.Count ? _伪行表[位置] : "";

        /// <summary> 取 `跳出 [层数]` 里的层数(不写就是 1);不是正整数的写法给出明确错误 </summary>
        private int 取跳出层数(string 内容, int 行号, string 词)
        {
            string 首词 = 取首词(内容);
            string 余 = 内容[首词.Length..].Trim().TrimEnd(';').Trim();
            if (余.Length == 0) return 1;
            if (!int.TryParse(余, out int 层) || 层 < 1)
                throw 报错(行号, $"{词} 后面的层数要是正整数(不写就是 1 层):'{内容.Trim()}'",
                    $"'{词}' level must be a positive integer");
            return 层;
        }

        /// <summary>
        /// 解析 `情况` 后面的常量表:`情况 1` / `情况 1, 2, 5` / `情况 1..5`(闭区间),也能混着写
        /// (`情况 1..3, 7, 9..10`)。分隔符认英文逗号、中文逗号与顿号;区间用两个点。
        /// 返回若干 (低, 高) 段:单个值就是 (值, 值)。
        /// </summary>
        private List<(long 低, long 高)> 解析情况常量(string 文本, int 行号)
        {
            var 结果 = new List<(long, long)>();
            foreach (string 一段 in 文本.Split([',', '，', '、'], StringSplitOptions.RemoveEmptyEntries))
            {
                string 段 = 一段.Trim();
                if (段.Length == 0) continue;
                int 点点 = 段.IndexOf("..", StringComparison.Ordinal);
                if (点点 < 0)
                {
                    long 值 = 解析情况界(段, 行号);
                    结果.Add((值, 值));
                    continue;
                }
                string 低文本 = 段[..点点].Trim(), 高文本 = 段[(点点 + 2)..].Trim();
                if (低文本.Length == 0 || 高文本.Length == 0)
                    throw 报错(行号, $"区间写法要两边都有数(如 `情况 1..5`):'{段}'", "range needs both bounds");
                long 低 = 解析情况界(低文本, 行号), 高 = 解析情况界(高文本, 行号);
                if (低 > 高)
                    throw 报错(行号, $"区间写反了:低 {低} 比高 {高} 还大('{段}')", "range lower bound is greater than upper bound");
                结果.Add((低, 高));
            }
            if (结果.Count == 0) throw 报错(行号, "情况/case 后面没有可用的常量", "case has no constants");
            return 结果;
        }

        // ---------------- 字面量初值 `[1, 2, 3]`(数组/结构共用;2026-10 起 `{ }` 只留给语句块) ----------------

        /// <summary> 这一段是不是方括号初值(`[1, 2, 3]`)。`{ … }` 从 2026-10 起**只表示语句块** </summary>
        private static bool 是方括号初值(string 文本)
        {
            string t = 文本.Trim();
            return t.Length >= 2 && t[0] == '[' && t[^1] == ']';
        }

        /// <summary> 拆方括号里的常量(按逗号;中文逗号与顿号也认)。里面不许再套一层方括号(嵌套结构请分别赋值) </summary>
        private List<string> 拆方括号初值(string 文本, int 行号)
        {
            string 内 = 文本.Trim();
            内 = 内[1..^1].Trim();
            var 结果 = new List<string>();
            if (内.Length == 0) return 结果;
            if (内.Contains('[') || 内.Contains(']'))
                throw 报错(行号, "字面量初值里不能再套一层方括号(结构里放结构请分别赋值)", "nested brackets in initializer");
            foreach (string 一段 in 内.Split([',', '，', '、'], StringSplitOptions.RemoveEmptyEntries))
            {
                string 段 = 一段.Trim();
                if (段.Length == 0) continue;
                结果.Add(段);
            }
            return 结果;
        }

        /// <summary>
        /// 字面量初值 `[…]` 折成"偏移 / 宽度 / 值"三件(数组与结构**共用一套口径**)。
        /// 取格 = 第 i 格在哪儿、多宽、什么类型名;写多了由 过多文案 报错,元素是结构/数组/字符串由取格自己拦。
        /// </summary>
        private List<(int 偏移, int 宽度, uint 值)> 折字面量初值(
            List<string> 常量们, int 格数, Func<int, string> 过多文案, int 行号,
            Func<int, (int 偏移, int 宽度, string 类型名)> 取格)
        {
            if (常量们.Count > 格数) throw 报错(行号, 过多文案(常量们.Count), "too many initializers");
            var 项们 = new List<(int 偏移, int 宽度, uint 值)>();
            for (int i = 0; i < 常量们.Count; i++)
            {
                var 格 = 取格(i);
                项们.Add((格.偏移, 格.宽度, 元素初值常量(常量们[i], 格.类型名, 行号)));
            }
            return 项们;
        }

        /// <summary>
        /// "这一格能不能收普通数值常量"——结构字段是结构 / 数组 / 字符串 / 列表的都不收:
        /// 字面量初值只能一个格子一个数,别的东西(堆块 / 嵌套块)写进去只会塞进去一串假地址。
        /// </summary>
        private void 要求字段收数字(结构字段C 字段, string 变量名, int 行号)
        {
            string 什么样 = 字段.是结构 ? "结构" : 字段.是数组 ? "数组" : 字段.是串字段 ? "字符串" : 字段.是列表 ? "列表" : "";
            if (什么样.Length == 0) return;
            throw 报错(行号, $"字段 '{字段.名称}' 是{什么样},字面量初值只收普通数值:" +
                $"先整块清 0,再对 {变量名}.{字段.名称} 分别赋值",
                $"field '{字段.名称}' is a {什么样}; assign it separately");
        }

        /// <summary> 元素是结构的数组不能用字面量初值整块给(塞进去的会是假地址):逐个元素赋值 </summary>
        private void 要求元素收数字(string 数组名, 结构类型C? 元素结构, int 行号)
        {
            if (元素结构 is not { } 结构) return;
            throw 报错(行号, $"数组 '{数组名}' 的元素是结构 '{结构.名称}',字面量初值只收普通数值:" +
                $"先整块清 0,再对 {数组名}[i] 逐个元素赋值(元素里的字段请用方法收)",
                $"literal initializer cannot fill struct elements of '{数组名}'");
        }

        /// <summary> 一个初值常量:按**元素/字段的类型**做范围检查(和变量常量一个口径),返回要写的 32 位值 </summary>
        private uint 元素初值常量(string 文本, string 类型名, int 行号)
        {
            // H68-2:枚举成员当场折成常量(类型名是枚举名时顺手做"不许跨枚举"的检查)
            文本 = 折成员常量(文本, 行号, 查枚举类型(类型名)?.名称 ?? "");
            long 值 = 解析情况界(文本, 行号);          // 复用:允许负数、认 0x、按 32 位范围
            类型C.类型定义? 定 = 类型C.解析(类型名);
            if (定 is { } 类型)
            {
                bool 有符号 = 类型.有符号;
                long 上 = 有符号 ? 类型C.有符号最大值(类型.宽度) : (long)类型C.最大值(类型.宽度);
                long 下 = 有符号 ? 类型C.有符号最小值(类型.宽度) : 0;
                if (值 < 下 || 值 > 上)
                    throw 报错(行号, $"初值 {值} 超出 {类型名} 的范围({下} ~ {上}) —— 要更大就换个更宽的类型," +
                        $"要负数就用有符号类型", $"initializer {值} out of range for {类型名}");
            }
            return (uint)值;
        }

        /// <summary> 当前是不是在某个循环体里?循环每轮都要重新执行里面的声明 —— 初值/清零都不能省 </summary>
        private bool 在循环里() => _上下文栈.Any(层 => 层.继续 != null);

        /// <summary>
        /// 这个初值能不能搬到"装载期写一次"?只有**程序顶层的常量**可以:
        /// 数据段里的槽(帧是复用的)+ **不在循环里**(循环体每轮都要重新执行那条声明,
        /// 搬走的话第二轮就不会重置了 —— 内层循环变量踩过:第二个外层轮次内层直接跳过)。
        /// </summary>
        private bool 初值可搬(变量槽定义 定义) => !是帧槽(定义) && !在循环里();

        /// <summary>
        /// 把一块(数组/结构)清 0:按 4 字节一个字写,剩下的零头按 2/1 字节补。
        /// 用在"循环体里声明的数组/结构"上 —— 那种声明**每轮都要重新执行**,数据段"天生是 0"不作数;
        /// 帧更是复用的(函数入口那次清零只管一遍,循环里第二轮就不够了)。
        /// 偏移寻址交给 地址位置,数据段槽与帧槽同一套写法。
        /// </summary>
        private void 发出清零填块(地址位置 基, int 总宽)
        {
            if (总宽 <= 0) return;
            自成一段(() =>
            {
                string 零 = 新临时();
                发出($"LOAD_IMMEDIATE {零} 0");
                int 偏移 = 0;
                while (总宽 - 偏移 >= 4)
                {
                    发出写变量(零, 基 with { 偏移 = 基.偏移 + 偏移, 宽度 = 4 });
                    偏移 += 4;
                }
                if (总宽 - 偏移 >= 2)
                {
                    发出写变量(零, 基 with { 偏移 = 基.偏移 + 偏移, 宽度 = 2 });
                    偏移 += 2;
                }
                if (总宽 - 偏移 == 1)
                    发出写变量(零, 基 with { 偏移 = 基.偏移 + 偏移, 宽度 = 1 });
            }, 基.槽);
        }

        /// <summary>
        /// 把一串常量按偏移写进一块内存(字面量初值用)。**值为 0 的跳过** ——
        /// 数组/结构本来就整体清过 0(顶层数据段是零,函数里入口也清零),写 0 纯属白干。
        /// </summary>
        private void 发出常量填块(string 基址, IReadOnlyList<(int 偏移, int 宽度, uint 值)> 项们)
        {
            foreach (var (偏移, 宽度, 值) in 项们)
            {
                if (值 == 0) continue;
                自成一段(() =>
                {
                    string 址 = 新临时();
                    发出($"MOV {址} {基址}");
                    if (偏移 > 0) 发出($"ADD_IMMEDIATE {址} {偏移}");
                    string 数 = 新临时();
                    输出装载常量(数, 值);
                    发出间接写(数, 址, 宽度);
                }, 基址);
            }
        }

        /// <summary>
        /// 顶层常量的字面量初值(数组/结构):登记进初值表,装载时直接写进内存 —— **一个字都不发**。
        /// 只有数据段里的槽能这么干;函数帧是复用的,必须现场写(见 发出常量填块)。
        /// **项里的偏移是相对这块开头的**(与 `发出常量填块` 同一套)⇒ 这里要加上这块自己的起始偏移,
        /// 因为初值表认的是"这个槽名 + 绝对偏移"(H3 踩过:结构默认值按相对偏移登记,装载期写歪了一格,
        /// 读出来像是"默认值在循环里被重复加了一遍")。
        /// </summary>
        private void 记顶层填块(string 槽名, IReadOnlyList<(int 偏移, int 宽度, uint 值)> 项们, int 起始偏移 = 0)
        {
            foreach (var (偏移, 宽度, 值) in 项们)
            {
                if (值 == 0) continue;                          // 数据段本来就是零
                _顶层初值.Add((槽名, 起始偏移 + 偏移, 宽度, 值));
            }
        }

        /// <summary>
        /// `情况` 的界:与普通数值不一样的地方是**允许负数**(有符号的值写 `情况 -1, 0..5` 才说得通),        /// 所以不走"20 位立即数"那套检查,只按 32 位范围查。十进制 / 0x 十六进制都认。
        ///
        /// R3-2(第184批):上限改吃 `硬上限C.情况常量上限`(唯一事实源),两处报错都补上**怎么绕** ——
        /// 以前只有一句"超出 32 位范围",用户看不出"那我该怎么写"。
        /// </summary>
        private long 解析情况界(string 文本, int 行号)
        {
            // H68-2:枚举成员也是编译期常量 ⇒ `情况 绿:` / `情况 绿, 蓝:` 照样能用(先折成十进制)
            文本 = 折成员常量(文本, 行号);
            string t = 文本.Trim();
            bool 负 = t.StartsWith('-');
            string 数 = 负 ? t[1..].Trim() : t;
            long 值;
            if (数.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!ulong.TryParse(数[2..], System.Globalization.NumberStyles.HexNumber, null, out ulong 十六) || 十六 > 硬上限C.情况常量上限)
                    throw 报错(行号, $"情况/case 的常量超出 32 位范围:'{t}'(能写到 {硬上限C.情况常量上限})。" + 情况常量改法(),
                        "case constant out of 32-bit range");
                值 = (long)十六;
            }
            else if (!long.TryParse(数, out 值))
                throw 报错(行号, $"情况/case 的常量认不出来:'{t}'(要十进制或 0x 十六进制)", $"unrecognized case constant: '{t}'");
            if (值 > (long)硬上限C.情况常量上限)
                throw 报错(行号, $"情况/case 的常量超出 32 位范围:'{t}'(能写到 {硬上限C.情况常量上限})。" + 情况常量改法(),
                    "case constant out of 32-bit range");
            return 负 ? -值 : 值;
        }

        /// <summary> R3-2:`情况` 常量超出 32 位时那句"怎么绕"(两处报错共用一句,别再各写一半) </summary>
        private static string 情况常量改法() =>
            "HB 的值最宽就是 32 位(整数 / 无符号整数 都 4 字节),超出的常量**没有任何值能匹配上** —— " +
            "要么把常量收进 32 位范围,要么别用 选择,改成 如果 + 64 位库" +
            "(`包含 \"库/int64.hb\"` 的 u长整数比较 / u长整数相等)";
        #endregion
    }
}
