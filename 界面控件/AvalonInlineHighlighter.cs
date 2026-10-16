using System;
using System.Collections.Generic;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// 把引擎侧扫出来的"上色位置"涂到 AvalonEdit 的每一行上。
    ///
    /// **性能**:变量名/结构名要靠"整篇扫一遍声明"才知道,所以这里按**文档版本**缓存:
    /// 同一个版本(AvalonEdit 每改一次文本换一个版本对象)只扫一次,同一行反复重画不会重扫。
    /// 再叠一层 `源码名字扫描C` 自己的节流(250ms 内换了两版就用上一版的名字集),
    /// 连着敲字也不会每敲一下就把整篇重扫。
    ///
    /// **C16 花括号彩虹色**:`{}` 按嵌套层号上色(**C16-2 起第 1 层也上色**)。层号是**规则**,
    /// 在引擎 `花括号层号C.扫()`(注释与字面量里的括号不算,与 `折叠区C` 同一套扫描口径);
    /// 这里只把"看得见的那几十行"查出来涂色 —— 整篇只扫一次(同样按文档版本缓存)。
    ///
    /// **含进来的库**(用户 2026-10-07 点名"高亮没跟上"):这一页 `包含` 进来的库里,模块名与公开方法名
    /// 也上色 —— 数据取自库源码(`库索引C`,窄口径),**不依赖编译结果**。`取文件路径` 就是为它传的。
    /// </summary>
    public class AvalonInlineHighlighter(语法高亮C 高亮器, Func<string?>? 取文件路径 = null) : DocumentColorizingTransformer
    {
        private readonly 语法高亮C 高亮器 = 高亮器 ?? throw new ArgumentNullException(nameof(高亮器));

        private ITextSourceVersion? _上次版本;
        private 配色名字集C? _名字集;
        private List<(int 开始, int 结束)>? _注释区;

        /// <summary> C16:这一版文档的花括号层号表(注释 / 字面量里的括号不算) </summary>
        private 花括号层号C.层号表C? _花括号层号;

        /// <summary> C7:逐行的扫描结果缓存(命中就这一行不重扫)</summary>
        private readonly 增量高亮C _缓存 = new();

        protected override void ColorizeLine(DocumentLine line)
        {
            if (line == null) return;
            var doc = CurrentContext.Document;
            string text = doc.GetText(line);

            取这一版(doc, out var 集, out var 注释区, out var 花括号);

            片段上色(line, text, 集, 注释区, doc, 花括号);
            花括号上色(line, 花括号, doc);
        }

        /// <summary> 词类上色(变量 / 关键字 / 字面量 / 注释…):走 `增量高亮C` 的逐行缓存 </summary>
        private void 片段上色(DocumentLine line, string text, 配色名字集C 集,
            List<(int 开始, int 结束)> 注释区, TextDocument doc, 花括号层号C.层号表C? 花括号)
        {
            List<(int 起始, int 长度, Brush 颜色)> 片段;
            try
            {
                // C7:逐行结果走 `增量高亮C` 的缓存 —— 滚动回来、换主题重画、同一行反复重画都不重扫;
                // 缓存的版本号就用 AvalonEdit 的文档版本对象(引用变了整表失效,规则只有一处)。
                片段 = 取片段(text, 集, 本行注释(注释区, line.Offset, line.Length), doc.Version,
                    花括号层号C.取层号(花括号, line.LineNumber), 花括号层号C.取行括号(花括号, line.LineNumber));
            }
            catch
            {
                return;
            }

            int lineStart = line.Offset;
            foreach (var (起始, 长度, 颜色) in 片段)
            {
                int start = lineStart + 起始;
                int end = start + 长度;
                if (start < lineStart) start = lineStart;
                if (end > lineStart + line.Length) end = lineStart + line.Length;
                if (start >= end) continue;

                try
                {
                    ChangeLinePart(start, end, element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(颜色);
                    });
                }
                catch
                {
                    // 忽略单个片段错误
                }
            }
        }

        /// <summary>
        /// C16:给这一行里的 `{` / `}` 上色(**C16-2 起第 1 层也上色**,`取色档` 恒在 `0~6`)。
        /// 层号由引擎算好,这里只按层号取画刷;注释与字面量里的括号**引擎那边就没记**,所以这里不必再判一次。
        /// </summary>
        private void 花括号上色(DocumentLine line, 花括号层号C.层号表C? 花括号, TextDocument doc)
        {
            var 一位 = 花括号层号C.取行括号(花括号, line.LineNumber);
            if (一位.Count == 0) return;
            foreach (var (列, 层号) in 一位)
            {
                int 色档 = 花括号层号C.取色档(层号);
                if (列 < 0 || 列 >= line.Length) continue;    // 折叠/换行边界上对不上就跳过
                int start = line.Offset + 列;
                try
                {
                    var 刷 = 高亮器.取花括号刷(色档);
                    ChangeLinePart(start, start + 1, element => element.TextRunProperties.SetForegroundBrush(刷));
                }
                catch
                {
                    // 忽略单个字符错误
                }
            }
        }

        /// <summary>
        /// 这一版文档的"声明过的名字"与"整篇注释区" + C16 的花括号层号表。同一个版本只算一次:
        /// AvalonEdit 每改一次文本换一个版本对象,同一行反复重画不会重算。
        /// </summary>
        private void 取这一版(TextDocument doc, out 配色名字集C 名字集,
            out List<(int 开始, int 结束)> 注释区, out 花括号层号C.层号表C? 花括号)
        {
            var 版本 = doc.Version;
            if (_注释区 != null && ReferenceEquals(版本, _上次版本))
            {
                名字集 = _名字集 ??= new 配色名字集C();
                注释区 = _注释区;
                花括号 = _花括号层号;
                return;
            }
            _上次版本 = 版本;
            string 全文;
            try { 全文 = doc.Text; }
            catch { 全文 = ""; }
            try { _名字集 = 源码名字扫描C.取(全文, DateTime.UtcNow); }
            catch { _名字集 ??= new 配色名字集C(); }
            try { _名字集 = 并入库名字(_名字集, 全文); }
            catch { /* 库那一份只是帮手:查不动就照旧按本文件上色 */ }
            try { _注释区 = 代码解析器.取注释区(全文); }
            catch { _注释区 = []; }
            try { _花括号层号 = 花括号层号C.扫(全文); }
            catch { _花括号层号 = null; }
            名字集 = _名字集;
            注释区 = _注释区;
            花括号 = _花括号层号;
        }

        /// <summary>
        /// 把"这一页 `包含` 进来的库"里的名字并进名字集(窄口径,数据见 `库索引C`):
        /// 库的**模块名**按"结构 / 类型"那一档上色,库的**公开方法名**按"用户方法名"那一档上色 ——
        /// 不新开配色词类(选项页那 15 档一个不动)。
        /// **`源码名字扫描C.取` 返回的是它自己缓存的那一份**(可能正被别的调用点用着),
        /// 所以这里造一个新的并集、**绝不改那一份**。
        /// </summary>
        private 配色名字集C 并入库名字(配色名字集C 扫出来, string 全文)
        {
            var 库 = 库索引C.取(取文件路径?.Invoke(), 全文);
            if (库.空) return 扫出来;

            var 果 = new 配色名字集C();
            foreach (string 名 in 扫出来.结构) 果.结构.Add(名);
            foreach (string 名 in 扫出来.变量) 果.变量.Add(名);
            foreach (string 名 in 扫出来.方法名) 果.方法名.Add(名);
            foreach (string 名 in 扫出来.常量名) 果.常量名.Add(名);
            foreach (string 名 in 库.模块名) 果.结构.Add(名);
            foreach (string 名 in 库.方法名) 果.方法名.Add(名);
            return 果;
        }

        /// <summary>
        /// C7:逐行的扫描结果过一层缓存(`增量高亮C`)。命中就不扫 —— 滚动回来、换主题重画、
        /// 同一行反复重画都只花一次扫描的钱;行文本变了才重扫那一行。
        /// C16:这一行的花括号层号(本行开头层号 + 每个括号的列号与层号)也进缓存键 ——
        /// 改动上面几行、括号层号变了也要重算,不然颜色会留在旧层上。
        /// </summary>
        private List<(int 起始, int 长度, Brush 颜色)> 取片段(string 行, 配色名字集C 名字集,
            List<(int 开始, int 结束)> 本行注释, object? 版本, int 行首层号, List<花括号层号C.括号位C> 行括号)
        {
            var 结果 = new List<(int, int, Brush)>();
            foreach (var 位 in _缓存.取行片段(行, 名字集, 本行注释, 版本, 花括号签名(行首层号, 行括号)))
                结果.Add((位.起始, 位.长度, 高亮器.取刷(位.词类)));
            return 结果;
        }

        /// <summary> C16:花括号层号进缓存键的紧凑签名(`行首层号:列-层号;…`) </summary>
        private static string 花括号签名(int 行首层号, List<花括号层号C.括号位C> 行括号)
        {
            if (行括号.Count == 0) return 行首层号.ToString();
            var sb = new System.Text.StringBuilder();
            sb.Append(行首层号).Append(':');
            foreach (var (列, 层号) in 行括号) sb.Append(列).Append('-').Append(层号).Append(';');
            return sb.ToString();
        }

        /// <summary> 把整篇注释区换算成"这一行里的区间"(块注释能跨行,所以要按行裁) </summary>
        private static List<(int 开始, int 结束)> 本行注释(List<(int 开始, int 结束)> 全局, int 行起点, int 行长)
        {
            var 结果 = new List<(int, int)>();
            foreach (var (开始, 结束) in 全局)
            {
                int 交起 = Math.Max(开始, 行起点);
                int 交止 = Math.Min(结束, 行起点 + 行长);
                if (交止 > 交起) 结果.Add((交起 - 行起点, 交止 - 行起点));
            }
            return 结果;
        }
    }
}
