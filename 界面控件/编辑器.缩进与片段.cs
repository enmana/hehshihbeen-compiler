using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Indentation;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 编辑器的**回车自动缩进**与**代码片段**(第122批:C5)。
    ///
    /// 缩进规则一个字都不在这儿写死 —— 全部转发给引擎 `代码排版C`:
    /// 命令行 `hbcc --排版`、菜单『格式化当前文件』、回车自动缩进**共用同一份规则**,
    /// 不会出现"格式化排成这样、回车又缩成那样"。选项里可以整关(那就退回 AvalonEdit 自带的"照抄上一行")。
    /// </summary>
    public partial class 编辑器
    {
        /// <summary> 当前的排版设置(选项页改了就跟着变;策略每次现取,不用重挂) </summary>
        public static 排版设置C 当前排版设置()
        {
            try
            {
                var 选项 = 配置.选项.当前;
                return new 排版设置C { 缩进宽度 = Math.Clamp(选项.缩进宽度, 1, 16) };
            }
            catch { return new 排版设置C(); }
        }

        /// <summary> 关掉自动缩进时退回 AvalonEdit 自带那一套(照抄上一行的前导空白)</summary>
        private void 装上缩进策略()
        {
            if (代码编辑器 == null) return;
            try { 代码编辑器.TextArea.IndentationStrategy = new 排版缩进策略(); }
            catch { /* 装不上就照旧用默认缩进:绝不影响编辑/编译 */ }
        }

        /// <summary>
        /// 把一段代码骨架插到光标处(按当前行的层级缩进好),插完光标停在 `◆` 那个位置。
        /// 换行跟着文档走(CRLF 的文档里插 CRLF),免得插一段就多出一片混行尾。
        ///
        /// C28 起"算"这件事整个搬进引擎 `片段插入C.算插入`:菜单插入与**智能补全里选中片段候选**共用同一份算
        /// (两条路插出来的文本逐字节相同,自检直接比字符串);这里只负责"照算好的结果改文档 + 落光标"。
        /// </summary>
        public void 插入片段(片段项C 项) => 插入片段(项, 缩写长度: 0);

        /// <summary>
        /// 同上,外加"光标前那一段缩写要先删掉"(`缩写长度` = 打进去的那几个字母有几个)。
        /// 智能补全那条路走的是这个重载:用户打的是 `rg`,选中 `如果{…}` 之后 `rg` 要被骨架顶掉。
        /// </summary>
        public void 插入片段(片段项C 项, int 缩写长度)
        {
            try
            {
                if (代码编辑器 == null || 项 == null) return;
                var 文 = 代码编辑器.Document;
                int 位置 = Math.Clamp(代码编辑器.CaretOffset, 0, 文.TextLength);
                var 动作 = 片段插入C.算插入(文.Text, 位置, 缩写长度, 项, 当前排版设置());
                using (文.RunUpdate()) 文.Replace(动作.起点, 动作.删除长度, 动作.文本);
                代码编辑器.CaretOffset = Math.Clamp(动作.光标, 0, 文.TextLength);
                代码编辑器.Focus();
            }
            catch (Exception 异常) { 异常收集C.记($"插入代码片段失败: {异常.Message}"); }
        }
    }

    /// <summary>
    /// 回车/缩进用的策略(挂在 `TextArea.IndentationStrategy` 上,AvalonEdit 在按回车建新行时调它)。
    /// 规则来自 `代码排版C.回车后的缩进` —— 块里进一档、`}` 收尾的行退回来、续行进一档、标签体进一档。
    /// </summary>
    public sealed class 排版缩进策略 : IIndentationStrategy
    {
        /// <summary>
        /// 超过这么多字符的文档,回车**不扫全篇**(算缩进要看这一行之前的括号,大文件里每次都扫一遍会卡):
        /// 退回 AvalonEdit 自带的"照抄上一行"。这是**有意保留的降级**(C7 大文件流畅度早已做完,
        /// 这一档不是"待办",是那条性能口径本身 —— 200_000 字以上的文档按回车想精确对齐就手打)。
        /// 精确缩进随时可以按 `Ctrl+Shift+F`(格式化当前文件)—— 那是一次明确的操作,慢一点没关系。
        /// </summary>
        private const int 大文件上限 = 200_000;

        public void IndentLine(TextDocument document, DocumentLine line)
        {
            if (document == null || line == null) return;
            try
            {
                var 选项 = 配置.选项.当前;
                if (!选项.回车自动缩进 || document.TextLength > 大文件上限)
                {
                    new DefaultIndentationStrategy().IndentLine(document, line);
                    return;
                }
                // C16-4:新行是空行 → 按"上一行那个块里进一档"算;新行**带着原文的尾巴**(行中间回车)→
                // 按这一行自己的写法算(`}` 退一档 —— 不然拆出来的 `}` 会停在 0 列、或者比它的 `{` 还深一档)。
                string? 目标 = 代码排版C.回车后前导(document.Text, line.LineNumber, 编辑器.当前排版设置());
                if (目标 != null) 设前导(document, line, 目标);
            }
            catch
            {
                try { new DefaultIndentationStrategy().IndentLine(document, line); } catch { }
            }
        }

        /// <summary> 多行的缩进(选中一片按 Tab 时):一行一行按同一套规则算 </summary>
        public void IndentLines(TextDocument document, int beginLine, int endLine)
        {
            if (document == null) return;
            try
            {
                var 选项 = 配置.选项.当前;
                if (!选项.回车自动缩进)
                {
                    new DefaultIndentationStrategy().IndentLines(document, beginLine, endLine);
                    return;
                }
                var 设置 = 编辑器.当前排版设置();
                for (int 行号 = beginLine; 行号 <= endLine && 行号 <= document.LineCount; 行号++)
                {
                    var 行 = document.GetLineByNumber(行号);
                    string? 目标 = 代码排版C.行目标缩进(document.Text, 行号, 设置);
                    if (目标 != null) 设前导(document, 行, 目标);
                }
            }
            catch { }
        }

        /// <summary>
        /// 把这一行的前导空白换成 目标(已经一样就什么都不做)。
        /// **行首不是空白的也要换**(C16-4):回车把 `}` / 语句尾巴拆到新行时,那一行一开始就顶在 0 列 ——
        /// 老写法在这里 `return`,于是 `}` 永远停在 0 列、与它的 `{` 对不上。
        /// 安全由**目标本身**保证:整行在注释/字符串里、顶格分节线、断开的标签体,
        /// `代码排版C` 一律给 `null`(调用方直接不动它)。
        /// </summary>
        private static void 设前导(TextDocument document, DocumentLine line, string 目标)
        {
            string 现行 = document.GetText(line);
            string 内容 = 现行.TrimStart(' ', '\t');
            int 前导长 = 现行.Length - 内容.Length;
            if (前导长 == 目标.Length && 现行.StartsWith(目标, StringComparison.Ordinal)) return;
            document.Replace(line.Offset, 前导长, 目标);
        }
    }
}
