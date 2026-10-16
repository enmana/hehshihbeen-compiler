namespace hehshihbeen_compiler
{
    /// <summary>
    /// 汉字 → 不带声调的全拼(`长` → CHANG),给**模糊拼音补全**用。
    ///
    /// 来历:这张表由 `NPinyin` 生成,**只覆盖补全词表里出现过的字**(107 个);
    /// 生成办法(换词表后重跑一次即可):
    ///   ① 建一个临时控制台工程,引用 `引擎库.csproj` 与 NuGet 包 `NPinyin`;
    ///   ② 把 `补全词表C.固定候选` 里的中英名收集起来,取所有汉字,`NPinyin.Pinyin.GetPinyin(字)` 逐个转大写;
    ///   ③ 把结果按每行 8 条贴回本文件。
    /// 为什么不用现成的运行时库:NPinyin 只有 .NET Framework 资产,直接引用会给每次构建添 4 条兼容性警告;
    /// 而这张表是**生成出来的死数据**,一个依赖都不加。
    /// 表里查不到的字(例如用户自己起的名字)只按**首字母**匹配,不影响别的候选。
    /// </summary>
    public static class 字音表C
    {
        /// <summary> 生成出来的表:每 8 条一组,组内用空格分,组末用 `;` </summary>
        private const string 表 =
            "不=BU 与=YU 串=CHUAN 为=WEI 乘=CHENG 于=YU 位=WEI 余=YU;" +
            "假=JIA 停=TING 储=CHU 入=RU 况=KUANG 减=JIAN 出=CHU 函=HAN;" +
            "则=ZE 到=DAO 制=ZHI 加=JIA 包=BAO 即=JI 历=LI 反=FAN;" +
            "取=QU 变=BIAN 右=YOU 号=HAO 否=FOU 含=HAN 回=HUI 地=DI;" +
            "址=ZHI 复=FU 大=DA 如=RU 始=SHI 字=ZI 存=CUN 寻=XUN;" +
            "小=XIAO 左=ZUO 帧=ZHENG 度=DU 异=YI 当=DANG 循=XUN 态=TAI;" +
            "情=QING 或=HUO 择=ZE 拼=PIN 按=AN 换=HUAN 接=JIE 数=SHU;" +
            "整=ZHENG 文=WEN 方=FANG 无=WU 构=GOU 果=GUO 栈=ZHAN 止=ZHI;" +
            "比=BI 法=FA 测=CE 添=TIAN 清=QING 环=HUAN 用=YONG 相=XIANG;" +
            "真=ZHEN 短=DUAN 移=YI 空=KONG 立=LI 符=FU 等=DENG 结=JIE;" +
            "继=JI 续=XU 节=JIE 行=XING 认=REN 试=SHI 读=DU 调=DIAO;" +
            "起=QI 跳=TIAO 转=ZHUAN 载=ZAI 较=JIAO 辑=JI 输=SHU 返=FAN;" +
            "选=XUAN 逻=LUO 遍=BIAN 量=LIANG 长=CHANG 间=JIAN 除=CHU 零=LING;" +
            "静=JING 高=GAO 默=MO;";

        private static readonly Dictionary<char, string> _表 = 解析(表);

        private static Dictionary<char, string> 解析(string 文本)
        {
            var 结果 = new Dictionary<char, string>();
            foreach (string 条 in 文本.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] 对 = 条.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (string 一项 in 对)
                {
                    int 等号 = 一项.IndexOf('=');
                    if (等号 != 1) continue;
                    结果[一项[0]] = 一项[(等号 + 1)..];
                }
            }
            return 结果;
        }

        /// <summary> 取一个字的全拼(大写);表里没有返回空串 </summary>
        public static string 取(char 字) => _表.TryGetValue(字, out string? 音) ? 音 : "";

        /// <summary> 一共收了多少个字(自检用) </summary>
        public static int 字数 => _表.Count;
    }
}
