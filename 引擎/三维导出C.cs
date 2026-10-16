// ============================================================
// 三维导出C.cs —— R4-10:**把跑完的网格导成文本**(顶点 / 边),给"看得见"用
//
//   为什么要它:`hbcc` 把绘制命令打成**文字**要过控制台管道,编码极容易转坏
//   (实测:抓下来 1.1 MB 的文件里一个中文字都解不出 ⇒ 解析不了);
//   而"从虚拟机内存里按地址读浮点"这条路,要 `--labels` + 整块内存 dump —— 又慢又费。
//   这一份直接**在进程内**把数组读出来写成 ASCII 文本:一个数字一行,不经过任何编码转换。
//
// 文件格式(全 ASCII,好解析):
//   第一行:`顶点数 边数`
//   接着顶点:`顶点数` 行,每行三个浮点(十进制,点号),**空格分隔**
//   接着边:  `边数` 行,每行两个整数(顶点下标)
//
// 用法(在引擎里调用):
//   `三维导出C.写文件(hbcc 编译并跑完的那台机.内存, 路径, out 说明)`
//   —— 数组地址靠 `三维导出C.找数组槽(编译结果的名字表, 标签表)` 现查。
// ============================================================
using System.Globalization;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>一次"导出网格"的结果(路径 + 顶点/边条数,给人看的话)</summary>
    public sealed class 三维导出结果C
    {
        public bool 成功;
        public int 顶点数;
        public int 边数;
        public string 说明 = "";
    }

    /// <summary>
    /// 把 `三维网格.hb` 里那五个数组(顶点x/y/z、边起点/边终点)从**跑完的内存**里读出来写成文本。
    ///
    /// 数组布局(`三维网格.hb` 定死):
    ///   `浮点 顶点x[8000]` —— 每个元素 4 字节、IEEE754 单精度位模式、紧排;
    ///   `整数 边起点[20000]` —— 每个元素 4 字节小端。
    /// 变量槽的地址要调用方给(它来自 `名字表C.变量` 的 `槽` → 标签表)。
    /// </summary>
    public static class 三维导出C
    {
        /// <summary>
        /// 按名字查一个**全局数组变量**的槽地址。
        /// 名字按"模块限定名"与"裸名"都试一遍(`三维网格.顶点x` 与 `顶点x`)——
        /// 库里的模块级变量在名字表里是哪种写法,由 `模块` 那一层决定,两种都认最稳。
        /// </summary>
        public static int 找数组槽(名字表C 名字表, IReadOnlyDictionary<string, int> 标签表, params string[] 候选名)
        {
            foreach (var 变量 in 名字表.变量)
            {
                if (!变量.是全局) continue;
                foreach (string 名 in 候选名)
                {
                    bool 命中 = string.Equals(变量.名字, 名, StringComparison.Ordinal)
                        || 变量.名字.EndsWith("." + 名, StringComparison.Ordinal);
                    if (!命中) continue;
                    if (变量.槽.Length > 0 && 标签表.TryGetValue(变量.槽, out int 址)) return 址;
                }
            }
            return -1;
        }

        /// <summary> 读一个 4 字节小端字(地址越界给 0) </summary>
        private static uint 读字(byte[] 内存, int 址)
        {
            if (址 < 0 || 址 + 4 > 内存.Length) return 0;
            return (uint)(内存[址] | (内存[址 + 1] << 8) | (内存[址 + 2] << 16) | (内存[址 + 3] << 24));
        }

        /// <summary> 位模式 → 单精度浮点(`BitConverter` 的口径与 ISA 一致) </summary>
        private static float 位转浮(uint 位) => BitConverter.Int32BitsToSingle((int)位);

        /// <summary>
        /// 写文件。`顶点数` / `边数` 由调用方给(它们是 `三维网格` 里的两个全局整数,
        /// 也可以现读 —— 这里读它们更省事:`顶点数址` / `边数址` 传进来)。
        /// </summary>
        public static 三维导出结果C 写文件(byte[] 内存, string 路径,
            int 顶点x址, int 顶点y址, int 顶点z址, int 边起点址, int 边终点址,
            int 顶点数址, int 边数址, int 顶点上限 = 8000, int 边上限 = 20000)
        {
            var 果 = new 三维导出结果C();
            if (内存 == null || 顶点x址 < 0 || 边起点址 < 0)
            {
                果.说明 = "网格数组的地址没找齐(名字表里没有那几个全局变量)—— 这个工程可能没有 `包含 \"三维网格.hb\"`。";
                return 果;
            }
            int 顶点数 = (int)读字(内存, 顶点数址);
            int 边数 = (int)读字(内存, 边数址);
            if (顶点数 < 0 || 顶点数 > 顶点上限) 顶点数 = 0;
            if (边数 < 0 || 边数 > 边上限) 边数 = 0;

            var 文 = new StringBuilder(顶点数 * 40 + 边数 * 16 + 64);
            var 不变 = CultureInfo.InvariantCulture;
            文.Append(顶点数.ToString(不变)).Append(' ').Append(边数.ToString(不变)).Append('\n');
            for (int i = 0; i < 顶点数; i++)
            {
                float x = 位转浮(读字(内存, 顶点x址 + i * 4));
                float y = 位转浮(读字(内存, 顶点y址 + i * 4));
                float z = 位转浮(读字(内存, 顶点z址 + i * 4));
                文.Append(x.ToString("R", 不变)).Append(' ')
                   .Append(y.ToString("R", 不变)).Append(' ')
                   .Append(z.ToString("R", 不变)).Append('\n');
            }
            for (int i = 0; i < 边数; i++)
            {
                文.Append(((int)读字(内存, 边起点址 + i * 4)).ToString(不变)).Append(' ')
                   .Append(((int)读字(内存, 边终点址 + i * 4)).ToString(不变)).Append('\n');
            }

            try
            {
                文本落盘C.写回(路径, 文.ToString());
            }
            catch (Exception 错)
            {
                果.说明 = $"写不出去({路径}):{错.Message}";
                return 果;
            }
            果.成功 = true;
            果.顶点数 = 顶点数;
            果.边数 = 边数;
            果.说明 = $"顶点 {顶点数} / 边 {边数} → {路径}";
            return 果;
        }
    }
}
