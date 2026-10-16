using System.IO;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// P23(第120批):把一次**编译失败**摊成"问题页 / 波浪线要的一条条诊断"。
    ///
    /// 为什么要它:引擎那边的多错误报告(K5)是"合成一条异常,文本里把几处错列全" ——
    /// 界面以前只认那一条的**首错位置**,于是"一共 3 处错"只画得出 1 条红线,问题页也只列 1 条。
    /// 现在 `编译异常C.子错` 把一条条挂上了,这里按**每一条自己的文件与行号**出诊断:
    ///   · 文件对得上这一页的 → 照原行号出诊断(调用方拿去画红线 + 进问题页);
    ///   · 都不在这一页、但这一页是**当前页** → 在**夹进这一页范围**的行上补一条兜底
    ///     (理由与老代码一样:宁可多标一条,也不能让用户看到"什么都没有");
    ///   · 别的页(不是当前页)一条都不出 —— 那些错由"问题页兜底那轮"统一列,免得重复。
    ///
    /// 纯函数:不写盘、不碰编辑器、不改编译器状态;界面只负责把结果画出来 / 双击跳转。
    /// </summary>
    public static class 编译错误C
    {
        /// <summary> 这一页该加的诊断 + 有没有"确实落在这一页"的错(调用方据此决定还要不要兜底) </summary>
        public readonly record struct 单页诊断(List<诊断项C> 表, bool 落在本页);

        /// <summary> 算一页的诊断(见类注释的三条规则);`错` 为 null(编译通过)就什么都不给 </summary>
        public static 单页诊断 取诊断(编译异常C? 错, string? 这一页全称, int 这一页行数, bool 是当前页)
        {
            var 表 = new List<诊断项C>();
            if (错 == null) return new 单页诊断(表, false);

            bool 落在本页 = false;
            foreach (var 一条 in 错.逐条())
            {
                if (一条.行号 is not { } 行 || 行 <= 0) continue;
                string 该条文件 = 一条.文件 ?? 错.文件 ?? "";
                if (!文件判定C.同文件(该条文件, 这一页全称)) continue;
                表.Add(new 诊断项C { 行 = 行, 级别 = 诊断级别.错误, 规则 = "编译错误", 文本 = 一条.中文 });
                落在本页 = true;
            }

            if (!落在本页 && 是当前页)
            {
                var 各条 = 错.逐条();
                var 首 = 各条.FirstOrDefault(x => x.行号 is > 0) ?? 错;
                int 行 = Math.Max(1, Math.Min(首.行号 ?? 1, Math.Max(1, 这一页行数)));
                string 哪儿 = 首.文件 is { Length: > 0 } ? "(" + Path.GetFileName(首.文件) + ")" : "";
                表.Add(new 诊断项C
                {
                    行 = 行,
                    级别 = 诊断级别.错误,
                    规则 = "编译错误",
                    文本 = 各条.Count > 1
                        ? $"编译未通过:共 {各条.Count} 处错误,第一条在第 {首.行号} 行{哪儿} —— 看底部『问题』页,双击能一条条跳"
                        : $"编译未通过:{首.中文}(编译错误在第 {首.行号} 行{哪儿})",
                });
                落在本页 = true;      // 已经标出来了,别再让调用方兜第二遍
            }
            return new 单页诊断(表, 落在本页);
        }

        /// <summary> "这一页之外"的那些错(问题页兜底用):每条带**自己的文件与行**,按 文件 → 行 排好。
        /// 没有行号的错跳过(没有位置就跳不过去,报出来只会添乱)。 </summary>
        public static List<(string 全称, int 行, string 文本)> 取各条(编译异常C? 错)
        {
            var 结果 = new List<(string 全称, int 行, string 文本)>();
            if (错 == null) return 结果;
            foreach (var 一条 in 错.逐条())
            {
                if (一条.行号 is not { } 行 || 行 <= 0) continue;
                string 文件 = 一条.文件 ?? 错.文件 ?? "";
                结果.Add((文件, 行, 一条.中文));
            }
            结果.Sort((a, b) =>
            {
                int 比 = string.CompareOrdinal(a.全称, b.全称);
                return 比 != 0 ? 比 : a.行.CompareTo(b.行);
            });
            return 结果;
        }
    }
}
