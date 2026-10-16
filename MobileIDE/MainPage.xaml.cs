using hehshihbeen_compiler;
using System.Text;

namespace 手机IDE;

public partial class MainPage : ContentPage
{
    private byte[]? _机器码;
    /// <summary>
    /// H38-2:这里**不再存**编译结果(那个类型就是引擎对象)。机器码之外要的两样
    /// (顶层常量初值 / 名字表)一律问契约要:`前端契约C.服务.编译.*`。
    /// </summary>
    private bool _程序修改;                 // 程序内部改文本,避免重入触发候选/成对

    // ---- D4:从系统选择器打开的"当前文件"与"工程目录"(编译时用文件全称当文件名,`包含` 才能按目录解析) ----
    private string? _当前文件全称;
    private string? _工程目录;

    /// <summary>
    /// 现存的 `_机器码` 是**哪一份源码**编出来的(第329批)。
    /// 「执行」拿它判"要不要先自己编一遍" —— 源码 / 附加文件动过就重编,
    /// 不然会出现"改了源码再点执行、跑的还是上一版机器码"那种静默跑旧代码。
    /// </summary>
    private string? _汇编签名;

    // ---- H9:「打开文件…」一次挑了多个时,编辑框只装**第一个**(它是启动文件),
    //      其余几个在这儿排着,**汇编时跟它一起编**(与桌面「添加文件」一个口径:顺序 = 用户挑的顺序) ----
    private readonly List<string> _附加文件 = [];

    // ---- H29:控制台输入(照桌面「控制台」页口径) ----
    /// <summary> 送给程序的字节队列(一行 = 那一行的 UTF-8 字节 + 一个换行) </summary>
    private readonly Queue<byte> _输入队列 = new();
    /// <summary> 送进去的每一行的回显(与桌面同一个口径:`&gt; 行`),显示在结果框开头 </summary>
    private readonly StringBuilder _输入回显 = new();
    /// <summary> 这台机器:同一份机器码连着跑 —— 卡在"等输入"时按『发送』接着跑,PC / 内存 / 已产出的输出都得留着。
    /// **H38-2:机器本身放在契约里**(`前端契约C.服务.调试.机器`),这里只记"有没有开过机"。</summary>
    private bool _有机器;
    /// <summary> 本次运行的程序输出(接着跑时继续往后攒) </summary>
    private readonly StringBuilder _程序输出 = new();

    // 自动成对:开符 → 补右符
    private static readonly Dictionary<char, char> 成对表 = new()
    {
        ['('] = ')', ['['] = ']', ['{'] = '}', ['"'] = '"', ['\''] = '\'',
    };

    public MainPage()
    {
        InitializeComponent();
        手机选择器.挂上();                  // D4:把系统的"选文件/选目录"接到引擎的 选择器C 上
        源码编辑框.Text = "加载立即数 R0, 7\n加载立即数 R1, 5\n加法 R0, R1\n停止";
        结果框.Text = "汇编/执行结果将显示在这里。";
    }

    // ==================== D4:打开文件 / 打开工程目录(走系统选择器) ====================

    /// <summary> 问一次"选文件"或"选目录";没挂选择器 / 用户取消 都返回 null(调用方给一句提示) </summary>
    private async Task<选择结果C?> 挑一次(bool 要目录, string 标题, string[]? 后缀 = null, bool 可多选 = false)
    {
        var 请求 = new 选择请求C { 要目录 = 要目录, 标题 = 标题, 后缀 = 后缀 ?? [], 可多选 = 可多选 };
        var 口子 = 要目录 ? 选择器C.选目录 : 选择器C.选文件;
        if (口子 == null)
        {
            结果框.Text = "这个平台没有系统选择器(选择器没挂上)—— 可以先用『装入示例』或直接手写代码。";
            return null;
        }
        var 结果 = await 口子(请求);
        if (结果 == null)
        {
            结果框.Text = "选择器没有回应(可能被系统挡了)。";
            return null;
        }
        if (!结果.有路径)
        {
            结果框.Text = 结果.说明.Length > 0 ? 结果.说明 : "没有选到东西。";
            return null;
        }
        return 结果;
    }

    /// <summary>
    /// **H9:「打开文件…」可以一次挑多个** —— 第一个进编辑框(它就是启动文件,`包含` 按它所在目录解析),
    /// 其余的排进 `_附加文件`,**汇编时跟它一起编**(与桌面「添加文件」同一个口径:磁盘上那份原样编、
    /// 编辑框里看不到;`文件表` 顺序 = 用户挑的顺序)。只挑一个时与老口径一模一样。
    /// </summary>
    private async void 打开文件按钮_Click(object? sender, EventArgs e)
    {
        try
        {
            var 结果 = await 挑一次(要目录: false, "选一个 HB 源文件(可多选)", [".hb"], 可多选: true);
            if (结果 == null) return;
            手机选择器.摘系统目录();            // H64:挑的是单个文件(真路径那一档),上一次挂的系统目录作废
            string 第一个 = 结果.路径表[0];
            string 文本 = 前端契约C.服务.宿主.读文本文件(第一个);
            _当前文件全称 = 第一个;
            _工程目录 = Path.GetDirectoryName(第一个);
            源码编辑框.Text = 文本;
            _附加文件.Clear();
            _附加文件.AddRange(结果.路径表.Skip(1));
            结果框.Text = $"已打开:{Path.GetFileName(第一个)}({文本.Length} 字符)\n"
                + (_附加文件.Count > 0
                    ? $"另有 {_附加文件.Count} 个文件跟着一起汇编(它们不在编辑框里,磁盘上那份原样编):\n"
                      + string.Join("\n", _附加文件.Select(f => $"  · {Path.GetFileName(f)}")) + "\n"
                    : "")
                + "汇编时会按这个文件所在目录解析 `包含`。";
        }
        catch (Exception ex) { 结果框.Text = $"打开文件失败:{ex.Message}"; }
    }

    /// <summary>
    /// 「打开工程目录…」(H10 + H64)。两档:
    ///   · **系统目录**(H64,Android):弹 SAF 的目录选择器,挑中的那一棵树由 `手机选择器.挂系统目录`
    ///     挂到引擎的 `虚拟文件C` 上;拿回来的是虚拟路径 —— 工程文件 / 源码 / 程序里的文件能力都认它;
    ///   · **应用自己的工程目录**(老那一档,或用户取消了系统选择器):真路径,不用问权限。
    /// 挑完之后立刻 `选择器C.看目录` 判一下能不能开工(没有 .hbp 也没有 .hb 就如实说一句)。
    /// </summary>
    private async void 打开目录按钮_Click(object? sender, EventArgs e)
    {
        try
        {
            var 结果 = await 挑一次(要目录: true, "选一个工程目录");
            if (结果 == null) return;
            var 看 = 选择器C.看目录(结果.路径);
            if (!看.可用) { 结果框.Text = 看.说明; return; }

            _附加文件.Clear();          // H9:换成这个工程目录了,上一个文件带来的"附加文件"作废
            _工程目录 = 结果.路径;
            if (看.工程文件.Length > 0)
            {
                var 工程 = 工程文件C.加载(看.工程文件);
                string 启动 = 工程.绝对路径(工程.启动文件);
                if (前端契约C.服务.宿主.文件存在(启动))
                {
                    _当前文件全称 = 启动;
                    源码编辑框.Text = 前端契约C.服务.宿主.读文本文件(启动);
                    结果框.Text = $"已打开工程:{工程.摘要()}\n启动文件:{虚拟路径C.文件名(启动)}(已读进编辑框)";
                }
                else
                {
                    _当前文件全称 = null;
                    结果框.Text = $"工程读到了({工程.摘要()}),但启动文件不在:{工程.启动文件}";
                }
            }
            else
            {
                _当前文件全称 = null;
                结果框.Text = 看.说明 + "\n用『打开文件…』挑一个 .hb 就能开工。";
            }
        }
        catch (Exception ex) { 结果框.Text = $"打开工程目录失败:{ex.Message}"; }
    }

    // ==================== H10:保存(写回磁盘;兼"新建工程") ====================

    /// <summary>
    /// **把编辑框里那份写回磁盘。** 打开过文件就写回那一份;没打开过(手写 / 装入示例)就写到
    /// **应用自己的沙箱工程目录**里的 `主程序.hb` —— 手机端的"新建工程"就是这一条(不另加一格按钮)。
    /// 落盘之后把"当前文件"指过去:下一次汇编的启动文件、`包含` 的解析基准、相对路径的基准目录都跟着它走。
    /// **H64**:打开的是**系统目录**里的工程时,这一份就写回系统目录那一份(SAF 实现负责落盘);
    /// 写不进去(授权被收回之类)如实报一句,不静默。
    /// </summary>
    private void 保存按钮_Click(object? sender, EventArgs e)
    {
        try
        {
            string 目标 = _当前文件全称 ?? Path.Combine(手机选择器.工程根目录, "主程序.hb");
            string 目录 = 虚拟路径C.所在目录(目标);
            if (目录.Length > 0) 前端契约C.服务.宿主.建目录(目录);
            前端契约C.服务.宿主.写文本文件(目标, 源码编辑框.Text ?? "");
            _当前文件全称 = 目标;
            _工程目录 = 目录.Length > 0 ? 目录 : _工程目录;
            结果框.Text = $"已保存:{(手机选择器.是系统目录路径(目标) ? 虚拟路径C.显示(目标) : 目标)}\n"
                + "程序里写相对路径(例如 读文件(\"数据.txt\"))就是相对这个目录。";
        }
        catch (Exception ex) { 结果框.Text = $"保存失败:{ex.Message}"; }
    }

    // ==================== 智能输入 ====================

    private void 源码编辑框_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_程序修改) return;
        try
        {
            if (e.OldTextValue != null && e.NewTextValue != null)
                自动成对(e.OldTextValue, e.NewTextValue);
            刷新候选();
        }
        catch { /* 输入辅助失败不影响编辑 */ }
    }

    /// <summary> 用户刚输入单个开符 → 若后面没有对应闭符,自动补上并让光标停在中间 </summary>
    private void 自动成对(string 旧, string 新)
    {
        if (新.Length != 旧.Length + 1) return;      // 只处理“多了一个字符”
        // 找新增字符(最左差异即插入点;IME 多字符提交时不做成对)
        int 差异 = 0;
        while (差异 < 旧.Length && 差异 < 新.Length && 旧[差异] == 新[差异]) 差异++;
        if (新.Length - 旧.Length != 1) return;
        char 插 = 新[差异];
        if (!成对表.TryGetValue(插, out char 闭)) return;

        int 光标 = 源码编辑框.CursorPosition;
        if (光标 < 0 || 光标 > 新.Length) 光标 = 差异 + 1;
        // 仅当插入字符确实在光标前(即刚敲的),且其后不是对应闭符时补
        if (光标 - 1 != 差异) return;
        if (光标 < 新.Length && 新[光标] == 闭) return;

        _程序修改 = true;
        try
        {
            源码编辑框.Text = 新[..光标] + 闭 + 新[光标..];
            源码编辑框.CursorPosition = 光标;   // 停在开符与闭符之间
        }
        finally { _程序修改 = false; }
    }

    /// <summary> 取光标前“当前词”,据此算候选并显示/隐藏候选栏 </summary>
    private void 刷新候选()
    {
        string 文本 = 源码编辑框.Text ?? "";
        int 光标 = 源码编辑框.CursorPosition;
        if (光标 < 0 || 光标 > 文本.Length) 光标 = 文本.Length;
        int 起 = 光标;
        while (起 > 0 && 是词字符(文本[起 - 1])) 起--;
        string 词 = 文本[起..光标];
        // 刚敲了一个 `.` → 弹“方法”(长度/清空/添加);写在点后面的词也只给方法
        bool 在点后 = 起 > 0 && 文本[起 - 1] == '.';
        string 点名 = 在点后 ? 取点名(文本, 起) : "";

        var 候选 = 收集候选(词, 在点后, 点名);
        候选栏.ItemsSource = 候选.Count > 0 ? 候选 : null;
        候选栏.IsVisible = 候选.Count > 0;
    }

    private static bool 是词字符(char c) =>
        char.IsLetterOrDigit(c) || c == '_';

    /// <summary> `名字.` 里点号**左边**那个名字(`分数.` → `分数`;没有返回空串) —— C19 按它算"." 后面的成员 </summary>
    private static string 取点名(string 文本, int 词起)
    {
        if (词起 < 2 || 文本[词起 - 1] != '.' || !是词字符(文本[词起 - 2])) return "";
        int 止 = 词起 - 1;
        int 起 = 止 - 1;
        while (起 >= 0 && 是词字符(文本[起])) 起--;
        return 文本.Substring(起 + 1, 止 - 起 - 1);
    }

    /// <summary>
    /// 候选:固定词表 + 工程里的名字(C19 起走引擎同一个 `编译服务C.取补全` —— 桌面与手机两端行为一致:
    /// 变量按作用域、函数/结构来自整个工程、按公私有过滤、`名字.` 后面给那个类型的成员)。
    /// 手机端编的是编辑器里这一份源码(没有工程文件),所以"本文件表 = 工程表 = 最近一次汇编的结果";
    /// 没汇编过就是 null,引擎自己退回现场源码扫兜底。
    /// </summary>
    private List<补全候选C> 收集候选(string 词, bool 在点后 = false, string 点名 = "")
    {
        string 文本 = 源码编辑框.Text ?? "";
        int 光标 = 源码编辑框.CursorPosition;
        if (光标 < 0 || 光标 > 文本.Length) 光标 = 文本.Length;
        int 行 = 1;
        for (int i = 0; i < 光标 && i < 文本.Length; i++)
            if (文本[i] == '\n') 行++;

        var 来源 = new 补全来源C
        {
            本文件表 = 前端契约C.服务.编译.最近结果?.调试文件表.FirstOrDefault()?.名字表,
            工程表 = 前端契约C.服务.编译.最近结果?.名字表,
        };
        var 请求 = new 补全请求C
        {
            当前文件 = _当前文件全称 ?? "",
            当前行 = 行,
            词 = 词,
            在点后 = 在点后,
            点名 = 点名,
            现场正文 = 文本,
            上限 = 40,
        };
        var 候选 = 前端契约C.服务.编译.取补全(来源, 请求);
        if (在点后) return 候选;      // `名字.` 后面只给那个类型的成员(点完还没打字就把成员全列出来)

        // 寄存器:R/R0..R15(数量以 寄存器C 为准)
        string 上 = 词.ToUpperInvariant();
        if (词.Length is >= 1 and <= 3 && 上[0] == 'R')
            for (int i = 0; i < 寄存器C.数量; i++)
            {
                string r = 寄存器C.名称(i);
                if (r.StartsWith(上, StringComparison.Ordinal) && !候选.Any(x => x.词 == r))
                    候选.Add(new 补全候选C(r, 候选来历.名字, r));
            }
        return [.. 候选.Take(40)];
    }

    private void 候选栏_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not 补全候选C 选中) return;
        插入候选(选中);
        // 复位选择,允许再次点同一项
        候选栏.SelectedItem = null;
    }

    private void 插入候选(补全候选C 项)
    {
        string 文本 = 源码编辑框.Text ?? "";
        int 光标 = 源码编辑框.CursorPosition;
        if (光标 < 0 || 光标 > 文本.Length) 光标 = 文本.Length;
        int 起 = 光标;
        while (起 > 0 && 是词字符(文本[起 - 1])) 起--;

        _程序修改 = true;
        try
        {
            // C28:代码片段那条 —— 先把打进去的缩写删掉,再按当前层级插整段骨架。
            // 算的活全在引擎 `片段插入C.算插入`(桌面端同一条路),手机这边只负责换文本 + 落光标。
            if (项.片段 != null)
            {
                var 动作 = 片段插入C.算插入(文本, 光标, 光标 - 起, 项.片段);
                源码编辑框.Text = 文本[..动作.起点] + 动作.文本 + 文本[(动作.起点 + 动作.删除长度)..];
                源码编辑框.CursorPosition = Math.Clamp(动作.光标, 0, 源码编辑框.Text.Length);
                候选栏.IsVisible = false;
                候选栏.ItemsSource = null;
                return;
            }

            // 手机上"点一条"= 桌面按空格那一档(C16-1):名字 + 一个空格;带括号的(方法/内建函数)插 `xxx()`,光标停在括号里
            string 插入 = 补全词表C.确认文本(项, 补空格: true);
            源码编辑框.Text = 文本[..起] + 插入 + 文本[光标..];
            源码编辑框.CursorPosition = 起 + 插入.Length - (项.括号内落光标 ? 1 : 0);
            候选栏.IsVisible = false;
            候选栏.ItemsSource = null;
        }
        finally { _程序修改 = false; }
    }

    // ==================== 汇编/执行 ====================

    private void 汇编按钮_Click(object? sender, EventArgs e) => 编一下();

    /// <summary>
    /// **编一遍**(第329批起从「汇编」按钮里抽出来,给「执行」共用)。
    ///
    /// 用户 2026-10-01 真机核对时点名:手机端原来要"先点汇编、执行才可点",而设计口径是
    /// 「**默认能跑**(能自动就自动:自动编译、自动开窗、自动清屏,少让人记仪式)」——
    /// 桌面端 F5 就是自己先编。现在「执行」点下去时若**没编过 / 源码或附加文件动过**就先自己编一遍。
    /// 返回**编成了没有**(没编成就不跑)。
    /// </summary>
    private bool 编一下()
    {
        try
        {
            // D4:有了"当前文件全称"就用它当文件名 —— `包含` 是按**文件所在目录**解析的;
            // 没打开过文件(手写/装入示例)就退回 主程序.hb(相对当前工作目录)。
            string 文件名 = _当前文件全称 ?? (_工程目录 is { Length: > 0 } ? Path.Combine(_工程目录, "主程序.hb") : "主程序.hb");
            // H9:「打开文件…」多选进来的那几个排在后面一起编(启动文件排第一;磁盘上那份原样读,编辑框里那份用编辑框的)
            var 文件表 = new List<(string 名称, string 内容)> { (文件名, 源码编辑框.Text ?? "") };
            foreach (string 附加 in _附加文件)
                if (前端契约C.服务.宿主.文件存在(附加)) 文件表.Add((附加, 前端契约C.服务.宿主.读文本文件(附加)));
            var 编 = 前端契约C.服务.编译.汇编多文件带调试(文件表);
            _机器码 = 编.机器码;
            前端契约C.服务.编译.记当前结果(编);     // H38-2:这一份就是"当前机器码那一份"(装载时连顶层常量初值一起装)
            结果框.Text = $"汇编成功: {_机器码.Length} 字节"
                + (文件表.Count > 1 ? $"({文件表.Count} 个文件一起编)" : "") + "\n";
            foreach (var kv in 前端契约C.服务.编译.取标签表().OrderBy(x => x.Value))
                结果框.Text += $"  {kv.Key} = 0x{kv.Value:X}\n";
            _汇编签名 = 当前汇编签名();             // 这一版就是"编过的这一版"
            _有机器 = false;                       // H29:换了机器码,上一台(可能还卡在"等输入"上)作废
            return true;
        }
        catch (Exception ex)
        {
            结果框.Text = $"汇编错误: {ex.Message}";
            _机器码 = null;                        // 编不过就把上一版机器码丢干净:绝不让「执行」跑旧代码
            _汇编签名 = null;
            _有机器 = false;
            return false;
        }
    }

    /// <summary> 这一版的"算不算同一份"签名:源码 + 附加文件表(动过就该重编) </summary>
    private string 当前汇编签名() => (源码编辑框.Text ?? "") + "\u0000" + string.Join("\u0000", _附加文件);

    /// <summary> 执行前保证机器码是**当前这一份源码**编出来的(没编过 / 动过就现编;编不过就不跑) </summary>
    private bool 确保汇编过() =>
        _机器码 is { Length: > 0 } && 前端契约C.服务.编译.最近结果 != null && _汇编签名 == 当前汇编签名()
        || 编一下();

    // ==================== H29:控制台输入(手机端) ====================

    /// <summary> 程序要一个输入字节:队列里有就给;没有 → 返回 null
    /// (开了 `等输入` 时虚拟机会停在 `IN` 那条指令上等下一行,而不是当作"没有输入了") </summary>
    private uint? 取一个输入字节() => _输入队列.Count > 0 ? _输入队列.Dequeue() : null;

    /// <summary> 程序吐一个字节(与桌面"输出页"同一份口径:换行照原样,其余按低字节收) </summary>
    private void 收一个输出字节(byte b) => _程序输出.Append(b == (byte)'\n' ? '\n' : (char)b);

    /// <summary> 把一行文字(UTF-8 + 换行)送进输入队列;程序正停在输入上就顺势接着跑 </summary>
    private void 送一行输入(string 行)
    {
        _输入回显.Append("> ").Append(行).Append('\n');            // 回显:让你看得见自己送了什么(桌面同款)
        foreach (byte b in Encoding.UTF8.GetBytes(行)) _输入队列.Enqueue(b);
        _输入队列.Enqueue((byte)'\n');
        if (_有机器 && 前端契约C.服务.调试.机器.正在等输入) 接着跑();
        else 画结果("已送入一行(程序这一趟没有在等输入,下一次『执行』会从空输入开始)。\n");
    }

    private void 控制台输入框_Completed(object? sender, EventArgs e) => 送出输入框里那一行();

    private void 发送按钮_Click(object? sender, EventArgs e) => 送出输入框里那一行();

    /// <summary> 取输入框里那一行送进去并清空(按回车 / 点『发送』两条路同一个口径) </summary>
    private void 送出输入框里那一行()
    {
        string 行 = 控制台输入框.Text ?? "";
        控制台输入框.Text = "";
        送一行输入(行);
    }

    /// <summary> 把"送回显 + 程序输出 + 寄存器 + 状态行"重画进结果框(手机端就这一个框,当控制台使) </summary>
    private void 画结果(string 状态)
    {
        var sb = new StringBuilder();
        if (_输入回显.Length > 0) sb.Append(_输入回显);
        sb.Append("程序输出:\n").Append(_程序输出.Length == 0 ? "(无)\n" : _程序输出.ToString());
        if (_有机器)
        {
            var 机 = 前端契约C.服务.调试.机器;
            sb.Append("寄存器:\n");
            for (int i = 0; i < 前端契约C.服务.调试.寄存器个数(机); i++)
            {
                uint 值 = 前端契约C.服务.调试.寄存器值(机, i);
                sb.Append($"  R{i} = 0x{值:X8} ({值})\n");
            }
            sb.Append($"PC=0x{机.程序计数器:X} SP=0x{机.栈指针:X} PSW={机.状态标志}\n");
        }
        sb.Append(状态);
        结果框.Text = sb.ToString();
    }

    /// <summary> 送进一行之后接着跑:同一台机器、同一份机器码,PC 与内存都留着
    /// (`正在等输入` 这个记号必须先清掉,不然循环条件一进来就是假的、一步都不跑 —— R2-6) </summary>
    private void 接着跑()
    {
        前端契约C.服务.调试.机器.清除等待输入();
        跑到停();
    }

    /// <summary> 跑到停机、撞上"正在等输入"、或者出错为止(与桌面那条循环同一个退出条件) </summary>
    private void 跑到停()
    {
        try
        {
            var 机 = 前端契约C.服务.调试.机器;
            if (前端契约C.服务.调试.机器要重新装载(_机器码!)) 前端契约C.服务.调试.装载(机, _机器码!);     // 首次 / 上一趟已停机 → 从头装
            while (机.单步() && !机.正在等输入) { }
            画结果(机.正在等输入
                ? "程序在等输入 —— 在上面输入一行,按回车或点『发送』。\n"
                : "程序结束。\n");
        }
        catch (Exception ex)
        {
            画结果($"执行错误: {ex.Message}\n");
        }
    }

    private void 执行按钮_Click(object? sender, EventArgs e)
    {
        // 第329批:没编过 / 源码或附加文件动过就**自己先编一遍**(编不过就不跑)。
        // 用户真机核对时点名"要先点汇编、执行才可点",与"默认能跑"那条设计口径不符;
        // 顺带治掉"改了源码再点执行、跑的还是上一版机器码"这个静默跑旧代码的隐患。
        if (!确保汇编过()) return;
        if (_机器码 == null || _机器码.Length == 0 || 前端契约C.服务.编译.最近结果 == null) return;
        // H29:从头跑 —— 上一趟的输入队列、回显、输出一起清掉(与桌面「执行」同一个口径)
        _输入队列.Clear();
        _输入回显.Clear();
        _程序输出.Clear();
        // H38-2:机器交给契约管(界面不再存一个引擎对象字段)
        前端契约C.服务.调试.机器 = new 虚拟机C
        {
            输入源 = 取一个输入字节,
            输出目标 = 收一个输出字节,
            宿主调用 = 前端契约C.服务.宿主.处理,   // P28:读文件 / 写文件 / 当前时间(手机端能碰哪些目录由系统说了算)
            等输入 = true,               // H29:读输入而输入还没来时**停下来等**,不当作 EOF
            初值表 = 前端契约C.服务.编译.初值表(),   // 顶层常量初值跟着机器码一起落进数据段
            // D5:手机端**不给网络**(用户 2026-09-22 拍板)—— 手机 IDE 编的是编辑器里那一份源码、没有 .hbp 工程文件,
            // 白名单没有自然归宿,于是「取网址」在手机上就是"没挂能力"(给 假);帮助文本《第24节·手机端》写死了这一条。
        };
        _有机器 = true;
        // H10:相对路径的基准目录 = **程序源文件所在的那个目录**(全仓唯一的解析口子 `宿主与文件服务C.设基准目录`);
        // 没打开过文件(手写 / 装入示例)就落回应用自己的沙箱工程目录。
        手机选择器.定基准目录(_工程目录);
        跑到停();
    }

    private void 装入示例_Click(object? sender, EventArgs e)
    {
        _附加文件.Clear();              // H9:示例是自成一体的,上次挑进来的"附加文件"不跟着编
        _程序修改 = true;
        try
        {
            源码编辑框.Text =
                "// 中间层示例:当/while 累加 0..4 → R0 = 10\n" +
                "R0 = 0\n" +
                "R2 = 0\n" +
                "当 (R2 < 5) {\n" +
                "    R0 = R0 + R2\n" +
                "    R2 = R2 + 1\n" +
                "}\n" +
                "停止";
        }
        finally { _程序修改 = false; }
        候选栏.IsVisible = false;
    }
}
