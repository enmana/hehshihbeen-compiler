namespace hehshihbeen_compiler
{
    /// <summary>
    /// **一个断点的求值器**(P34,2026-10-07):把"这一刻的机器状态"包起来,回答两个问题 ——
    /// ① `这一趟算不算命中`(命中次数 + 条件;日志断点永远回答"不算命中")
    /// ② `这一趟该怎么记一笔`(条件断点停下来的那句话 / 日志断点要打印的那一行)。
    ///
    /// **为什么在引擎侧**:界面那一层只该做"把回调挂上去、把结果画出来" —— 读数、展开日志这两件事
    /// 都是纯逻辑(喂参数就能验),放这儿自检能真跑;放界面里就只能读源码文本猜(H38 的口径)。
    /// 界面侧每问一次就现造一个(它只活了这一次调用),所以不用考虑状态。
    ///
    /// 线程口径:它可能被调试会话在**后台线程**上调用(条件判断就发生在那里)—— 所以
    /// 它**只读**外部给的那几个东西(内存数组 / 标签表 / 名字表 / 日志处理),自己不留可变状态。
    /// </summary>
    public sealed class 断点求值器C
    {
        private readonly IReadOnlyList<文件映射C> _调试文件表;
        private readonly IReadOnlyDictionary<string, int>? _标签表;
        private readonly byte[]? _内存;
        private readonly uint _帧指针;

        /// <summary> 日志断点攒下来的行交给谁处理(界面传一个"塞进队列"的委托;null = 日志不落地)</summary>
        private readonly Action<string>? _记日志;

        public 断点求值器C(IReadOnlyList<文件映射C> 调试文件表, IReadOnlyDictionary<string, int>? 标签表,
            byte[]? 内存, uint 帧指针, Action<string>? 记日志 = null)
        {
            _调试文件表 = 调试文件表 ?? [];
            _标签表 = 标签表;
            _内存 = 内存;
            _帧指针 = 帧指针;
            _记日志 = 记日志;
        }

        /// <summary> "第 <paramref name="次数"/> 次经过时,这一刻的变量读数" —— 条件与日志用的是同一份 </summary>
        public List<变量读数C> 读数(int 行, 断点项C 断点)
        {
            var 段 = _调试文件表.FirstOrDefault(x => 文件判定C.同文件(x.文件, 断点.文件));
            return 变量读取C.取可见变量(段?.名字表, _标签表, _内存, _帧指针, 行);
        }

        /// <summary>
        /// 这一趟算不算命中。返回值三种:
        ///   · `true`  —— 停下来;
        ///   · `false` —— 不停(条件不成立,或者这是个日志断点:只打印);
        ///   · `null`  —— **判不了**(变量看不见 / 类型比不了 / 条件写错),`说明` 里写清为什么;
        ///     调用方按"停 + 提示一次"处理 —— 宁可多停一次让人看见问题,也绝不悄悄猜一个值把断点吞掉。
        /// </summary>
        public bool? 这一趟(断点项C 断点, 断点判定C 判定, long 次数, out string 说明)
        {
            说明 = "";
            // ① 命中次数
            if (!判定.命中次数.合法) { 说明 = "命中次数写错了:" + 判定.命中次数.说明; return null; }
            if (!判定.命中次数.满足(次数)) return false;

            // ② 条件
            string 原文 = 断点.条件?.Trim() ?? "";
            if (原文.Length > 0)
            {
                if (!判定.条件.合法) { 说明 = "断点条件写错了:" + 判定.条件.说明; return null; }
                var 判 = 断点条件求值C.判断(判定.条件, 段名字表(断点), _标签表, _内存, _帧指针, 断点.行, out string 为什么);
                if (判 == null) { 说明 = $"条件 `{原文}` 这次判不了:{为什么}"; return null; }
                if (!判.Value) return false;
            }

            // ③ 日志断点:打一行就走,不停下
            if (判定.是日志断点)
            {
                记一行(断点, 判定, 次数);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 命中之后"该怎么记一笔" —— 给界面那一侧用:
        /// 日志断点返回要打印的那一行(null = 不是日志断点);
        /// 普通/条件断点返回"停下来的说法"(带上第几次经过,`%5` 这种断点才看得出来是第几次)。
        /// </summary>
        public string? 记一笔(断点项C 断点, 断点判定C 判定, long 次数)
        {
            if (判定.是日志断点) { 记一行(断点, 判定, 次数); return null; }
            return $"{System.IO.Path.GetFileName(断点.文件)} 第 {断点.行} 行(第 {次数} 次经过)";
        }

        private 名字表C? 段名字表(断点项C 断点) =>
            _调试文件表.FirstOrDefault(x => 文件判定C.同文件(x.文件, 断点.文件))?.名字表;

        /// <summary> 把一条日志展开好交给外面(展开口径见 `日志断点C.展开`:看不见的占位原样留着并说明) </summary>
        private void 记一行(断点项C 断点, 断点判定C 判定, long 次数)
        {
            if (_记日志 == null) return;
            string 文件名 = System.IO.Path.GetFileName(断点.文件);
            string 文 = 日志断点C.展开(判定.日志, 读数(断点.行, 断点), 断点.行, 文件名);
            _记日志($"日志 {文件名} 第 {断点.行} 行(第 {次数} 次):{文}");
        }
    }
}
