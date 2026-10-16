namespace hehshihbeen_compiler
{
    /// <summary>
    /// **签名提示表**(P37,2026-10-07):名字 → 那个函数的签名。
    ///
    /// 与智能补全**同一份事实源**(这一条别打折扣):函数表来自最近一次编译的 `名字表C.函数签名`
    /// (参数名 / 类型 / `出` / `[]` 都在里面),内建函数与语言关键字来自 `中间层指令C.说明表`,
    /// 方法来自 `中间层指令C.方法名表` —— 表里加一条,这里就多一条,**不另抄一份参数表**。
    ///
    /// 为什么单独放一个类(而不是塞进补全):补全的说明是**一句话**(给人扫一眼的),
    /// 签名提示要的是**结构化**的那几样(参数一段一段、当前第几个要加粗)——
    /// 把一句话再解析回来才是真的抄两份。
    /// </summary>
    public static class 签名表C
    {
        /// <summary> 一句说明最多留多少字(提示框那一行不换行,太长会顶出编辑区)</summary>
        public const int 说明上限 = 120;

        /// <summary>
        /// 建一份签名表。`函数表们` = 最近一次编译里那几张 `名字表C`(工程表 + 每个文件一份):
        /// 同一个名字以**先来的**为准(工程表排在最前面,它有模块全名)。
        /// </summary>
        public static List<签名提示C> 建(IEnumerable<名字表C?> 函数表们)
        {
            var 结果 = new List<签名提示C>();
            var 见过 = new HashSet<string>(StringComparer.Ordinal);

            void 加(签名提示C 签)
            {
                if (签.名字.Length == 0) return;
                if (!见过.Add(签.名字)) return;
                结果.Add(签);
            }

            // ① 源码里的函数(参数名 / 类型 / `出` / `[]` 逐项都在)
            foreach (var 表 in 函数表们)
            {
                if (表 == null) continue;
                foreach (var (全名, 签) in 表.函数签名)
                {
                    if (全名.Length == 0 || 签 == null) continue;
                    var 段 = 签.参数.Select(参数文本).ToList();
                    加(new 签名提示C
                    {
                        名字 = 全名,
                        参数原文 = string.Join(", ", 段),
                        参数段 = 段,
                        返回类型 = 签.返回类型,
                        说明 = $"方法 {全名}({string.Join(", ", 段)})"
                            + (签.返回类型.Length > 0 ? $" 返回 {签.返回类型}" : ""),
                    });
                }
            }

            // ② 内建函数(中英两侧都登记):说明取 `说明表` 里那句 —— 它就是"内建函数 = 干什么"的唯一事实源。
            //    注意:**说明表的键是英文别名**(`readline`),中文名要拿 `中英对照` 反查一次,
            //    两头都试(表里也有直接以中文为键的条目)。
            foreach (var (中文, 英文) in 中间层指令C.内建函数名表)
            {
                加内建(中文, 英文);
                加内建(英文, 中文);
            }

            // ③ 字符串 / 列表的带点方法(`长度()` 这种,没有参数)
            foreach (var (中文, 英文) in 中间层指令C.方法名表)
            {
                加方法(中文, 英文);
                加方法(英文, 中文);
            }

            return 结果;

            // 说明表的键可能是中文名,也可能是英文别名 —— 两个都试,取先命中的那个
            string 找说明(string 名, string 别名)
            {
                if (中间层指令C.说明表.TryGetValue(名, out string? 有) && 有.Length > 0) return 有;
                if (别名.Length > 0 && 中间层指令C.说明表.TryGetValue(别名, out string? 又) && 又.Length > 0) return 又;
                return "";
            }

            void 加内建(string 名, string 别名)
            {
                if (名.Length == 0) return;
                加(new 签名提示C
                {
                    名字 = 名,
                    参数原文 = "…",
                    说明 = 截断(找说明(名, 别名)),
                });
            }

            void 加方法(string 名, string 别名)
            {
                if (名.Length == 0) return;
                加(new 签名提示C
                {
                    名字 = 名,
                    参数原文 = "",
                    是方法 = true,
                    说明 = 截断(找说明(名, 别名)),
                });
            }
        }

        /// <summary> 参数一项写成给人看的文本(与 `补全名字C` 那句小字同一套写法) </summary>
        public static string 参数文本(参数项C 参数) =>
            $"{参数.类型名} {参数.名字}" + (参数.是出 ? " 出" : "") + (参数.是数组 ? "[]" : "");

        /// <summary> 说明截到上限(超了加一个省略号);提示框那一行不换行</summary>
        public static string 截断(string 说明)
        {
            string 文 = (说明 ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return 文.Length <= 说明上限 ? 文 : 文[..说明上限] + "…";
        }
    }
}
