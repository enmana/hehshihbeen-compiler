using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 一条"编辑项"能回答的最小问题(文件 / 位置 / 原文 / 换成什么)。
    /// `重命名C`(改名)与 `查找替换C`(文本替换)各有一种具体的编辑项,套用逻辑(`编辑清单C.套用`)只认这个接口。
    /// </summary>
    public interface 编辑项I
    {
        /// <summary> 文件表里那个名字(工程文件表口径,不做路径归一) </summary>
        string 文件 { get; }

        /// <summary> 行号(1 基) </summary>
        int 行号 { get; }

        /// <summary> 列(0 基) </summary>
        int 列 { get; }

        /// <summary> 命中的那一段有多长(字符数) </summary>
        int 原词长度 { get; }

        /// <summary> 命中的原文那一段(套用时用它核对位置) </summary>
        string 原词 { get; }

        /// <summary> 要换成什么 </summary>
        string 新词 { get; }
    }

    /// <summary>
    /// 一条编辑项(改名 / 文本替换都用它):`旧行` = 这一行的原文,`新行` = **只把这一处**换掉之后的这一行
    /// (同一行有多处就会有多个条目,每个条目的 `新行` 都只含它自己那一处改动 —— 这样按"倒着改"(列大的先改)套上去才对得上)。
    /// </summary>
    public sealed class 编辑项C : 编辑项I
    {
        public string 文件 { get; set; } = "";
        public int 行号 { get; set; }
        public int 列 { get; set; }
        public int 原词长度 { get; set; }
        public string 原词 { get; set; } = "";
        public string 新词 { get; set; } = "";

        /// <summary> 改之前这一行的原文(不含行尾换行符) </summary>
        public string 旧行 { get; set; } = "";

        /// <summary> 改之后这一行(只含这一处改动) </summary>
        public string 新行 { get; set; } = "";

        /// <summary> 给自检/面板用的一行文本 </summary>
        public override string ToString() => $"{System.IO.Path.GetFileName(文件)}:{行号}:{列} `{原词}`→`{新词}`(长 {原词长度})";
    }

    /// <summary>
    /// 改名 / 文本替换**办不到 或 要提醒**的一件事(带文件与行号,说人话 + 给改法)。
    /// 只要清单的 `问题` 非空,清单就是**空的**(不硬改),见各自清单的 `可改`。
    /// </summary>
    public sealed class 编辑问题C
    {
        /// <summary> 说人话的一句话(含判据与改法) </summary>
        public string 说明 = "";

        /// <summary> 涉及哪个文件(可能是空串:跟具体位置无关的问题) </summary>
        public string 文件 = "";

        /// <summary> 涉及哪一行(1 基;0 = 不知道/不涉及) </summary>
        public int 行号;

        /// <summary> 给自检/面板用的一行文本 </summary>
        public override string ToString() =>
            行号 > 0 ? $"{System.IO.Path.GetFileName(文件)}:{行号}:{说明}" : 说明;
    }

    /// <summary>
    /// 编辑清单的**公共工具**:位置换算、造编辑项、注释/字符串保护区判定,以及把清单**套用**到文件表上。
    /// `重命名清单C` 与 `查找替换清单C` 都用这里的实现(一套逻辑,两处用 —— 免得两边的幂等口径慢慢走偏)。
    /// </summary>
    public static class 编辑清单C
    {
        /// <summary>
        /// 把一份编辑清单整体套到文件表上(纯函数:返回新表,不改入参、不写盘)。
        /// 套的时候会核对每处的 `原词`:**对不上就跳过** —— 所以同一份清单套两遍等于套一遍(幂等),
        /// 已经改过的文本再套也不会被改坏。
        /// </summary>
        public static List<(string 名称, string 内容)> 套用<T>(IReadOnlyList<T> 编辑,
            IReadOnlyList<(string 名称, string 内容)> 文件表) where T : 编辑项I
        {
            var 结果 = new List<(string 名称, string 内容)>();
            foreach (var (名称, 内容) in 文件表)
            {
                string 文本 = 内容 ?? "";
                // 这个文件有几处要改(可能为 0:清单里没有它的份)
                var 该文件的 = new List<T>();
                foreach (var 项 in 编辑)
                    if (string.Equals(项.文件, 名称, StringComparison.OrdinalIgnoreCase)) 该文件的.Add(项);
                if (该文件的.Count == 0) { 结果.Add((名称, 文本)); continue; }

                // (行号,列) → 绝对下标;倒着套(下标大的先改),前面的下标就不会被顶偏
                var 行首表 = 行首下标表(文本);
                var 待改 = new List<(int 起, int 长, string 新词, string 原词)>();
                foreach (var 项 in 该文件的)
                {
                    if (项.行号 < 1 || 项.行号 > 行首表.Count) continue;
                    int 起 = 行首表[项.行号 - 1] + 项.列;
                    if (起 < 0 || 起 + 项.原词长度 > 文本.Length) continue;
                    if (!string.Equals(文本.Substring(起, 项.原词长度), 项.原词, StringComparison.Ordinal)) continue;
                    待改.Add((起, 项.原词长度, 项.新词, 项.原词));
                }
                待改.Sort((a, b) => b.起.CompareTo(a.起));
                var sb = new StringBuilder(文本);
                foreach (var (起, 长, 新词, _) in 待改) sb.Remove(起, 长).Insert(起, 新词);
                结果.Add((名称, sb.ToString()));
            }
            return 结果;
        }

        /// <summary> 这个位置在不在"注释 ∪ 字符串"里(在两段区间里就跳过) </summary>
        public static bool 在保护区内(List<(int 开始, int 结束)> 保护区, int 下标)
        {
            foreach (var (开始, 结束) in 保护区)
            {
                if (下标 < 开始) return false;      // 区间按开始排好序,后面的更靠后
                if (下标 < 结束) return true;
            }
            return false;
        }

        /// <summary> 绝对下标 → (行号 1 基, 列 0 基) </summary>
        public static (int 行号, int 列) 定位(List<int> 行首表, int 下标)
        {
            int 低 = 0, 高 = 行首表.Count - 1;
            while (低 < 高)
            {
                int 中 = (低 + 高 + 1) / 2;
                if (行首表[中] <= 下标) 低 = 中; else 高 = 中 - 1;
            }
            return (低 + 1, 下标 - 行首表[低]);
        }

        /// <summary> 一篇文章里每一行开头在整篇里的下标(0 基行号 → 下标) </summary>
        public static List<int> 行首下标表(string 文本)
        {
            var 表 = new List<int> { 0 };
            for (int i = 0; i < 文本.Length; i++)
                if (文本[i] == '\n') 表.Add(i + 1);
            return 表;
        }

        /// <summary> 造一条编辑项(`旧行` 去掉行尾 `\r`,`新行` 只含这一处改动) </summary>
        public static 编辑项C 造编辑项(string 文件, string 文本, List<int> 行首表, int 下标, int 长, string 新词)
        {
            var (行号, 列) = 定位(行首表, 下标);
            int 行起 = 行首表[行号 - 1];
            int 行止 = 行号 < 行首表.Count ? 行首表[行号] : 文本.Length;
            string 旧行 = 文本[行起..行止].TrimEnd('\r', '\n');
            string 原词 = 文本.Substring(下标, 长);
            return new 编辑项C
            {
                文件 = 文件,
                行号 = 行号,
                列 = 列,
                原词长度 = 长,
                原词 = 原词,
                新词 = 新词,
                旧行 = 旧行,
                新行 = 旧行[..列] + 新词 + 旧行[(列 + 长)..],
            };
        }

        /// <summary> 清单按 文件 → 行号 → 列 排序(同一份输入必给同一个顺序) </summary>
        public static void 排好(List<编辑项C> 编辑) => 编辑.Sort((a, b) =>
        {
            int 比 = string.CompareOrdinal(a.文件, b.文件);
            if (比 != 0) return 比;
            比 = a.行号.CompareTo(b.行号);
            return 比 != 0 ? 比 : a.列.CompareTo(b.列);
        });
    }
}
