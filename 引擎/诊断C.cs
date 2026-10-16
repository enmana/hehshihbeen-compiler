namespace hehshihbeen_compiler
{
    /// <summary> 一条编辑器下划线的严重程度:红(错误)/ 黄(警告)/ 绿(提示) </summary>
    public enum 诊断级别
    {
        错误 = 0,
        警告 = 1,
        提示 = 2,
    }

    /// <summary>
    /// 编辑器里"某一行某一段"的一条诊断:画成波浪线,鼠标移上去弹提示。
    /// 纯数据(引擎侧),界面只负责画 —— 这样"哪条规则该报什么"能用自检钉住。
    /// </summary>
    public sealed class 诊断项C
    {
        /// <summary> 源码行号(1 基) </summary>
        public int 行 { get; set; }
        /// <summary> 起始列(0 基,含) </summary>
        public int 起列 { get; set; }
        /// <summary> 结束列(0 基,不含);小于等于 起列 时按"整行"处理 </summary>
        public int 止列 { get; set; }
        public 诊断级别 级别 { get; set; } = 诊断级别.错误;
        /// <summary> 给人看的一句话(悬停提示里显示这个) </summary>
        public string 文本 { get; set; } = "";
        /// <summary> 这条是干什么的(自检用,比如"未使用变量") </summary>
        public string 规则 { get; set; } = "";

        /// <summary>
        /// 这条诊断属于哪个**源文件**(工程文件表里的那个名字或全路径)。
        /// 只给"跨文件算出来的"诊断用(未使用函数检测 —— 一次扫描覆盖整个工程,不带上文件就分不清是哪一份);
        /// `检查器C` 那套只看一份源码、由调用方认文件,留空即可。
        /// </summary>
        public string 文件 { get; set; } = "";

        public override string ToString() => $"第{行}行 {级别}:{文本}";
    }
}
