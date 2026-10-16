using System;
using System.Collections.Generic;
using System.Linq;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// **H38-3:一份真的"假前端"实现** —— `界面服务C` 门面 + 四份能力接口**全替**,
    /// **一行引擎的编译 / 执行都不调**,也**不来任何窗口**。
    ///
    /// 它只有一件用处:**证明前端的全部耦合点都在契约上**。换成它之后,
    /// "编译 → 装载 → 单步 → 取快照 → 收图形命令流"这一整条路照样走得通 ——
    /// 哪一天谁把某一步挪回界面里、绕开契约去摸引擎,这一条当场红
    /// (`不该走到` 那几格就是为这件事准备的:前端走到契约上**没列**的东西,它直接抛)。
    ///
    /// **它怎么做到"不碰引擎"**:
    /// · **编译那一格**不发汇编器:按源码行数发一段**手写的假机器码**(一行一个字节,值 = 行号);
    /// · **调试那一格**自己拿一个下标当 PC、自己攒"假步进结果"(`调试步进结果C` 就是个纯数据记录);
    /// · **宿主那一格**用一本内存字典当文件系统;
    /// · **图形那一格**自己攒一条命令队列,每"走一步"往挂上的呈现口推一帧。
    ///
    /// **诚实记一条(契约上还剩的口子)**:`调试服务C.机器` / `会话机器` / `取机器` 这三格
    /// **返回的具体类型是 `虚拟机C`** —— 那是 H39-2"把跑程序挪进子进程"要**就地换机器**留下的形状
    /// (换成 `跨进程虚拟机C`),假前端绕不过去,只能给一只**占位虚拟机**;
    /// 但假前端**一次都不碰它**,自检拿"那只占位机器自始至终是空的(机器码 0 字节、内存全 0)"当证据。
    /// 要把这三格也从契约上拿掉,得先给"跑程序住哪一侧"另立一层 —— 那是另一件事,不在 H38 这三步里。
    /// </summary>
    internal sealed class 假前端C : 界面服务C, 编译服务C, 调试服务C, 宿主与文件服务C, 图形服务C
    {
        // ================= 账本(自检拿它当"路真的走完了"的证据) =================

        public int 编译次数;
        public int 装载次数;
        /// <summary> 走 `按内存装载` 那一格来了几次(执行那条腿按工程 `内存` 装载 —— 与 `装载次数` 分开数)</summary>
        public int 按内存装载次数;
        public int 快照次数;
        public int 已走步数;
        public int 投事件次数;
        public int 会话清空次数;
        public readonly List<string> 图形帧 = [];
        public readonly Dictionary<string, string> 文件 = new(StringComparer.OrdinalIgnoreCase);
        public string? 基准目录;
        public string? 挂过的文件实现;

        /// <summary> 契约上那三格绕不过去的占位虚拟机(假前端一次都不碰它)</summary>
        public readonly 虚拟机C 占位机 = new();

        private byte[] _假机器码 = [];
        private int 下标;

        /// <summary> `这段机器码用图形` 认不认这一份(自检拿它演"这一趟有窗口")</summary>
        public bool 这段用图形;

        // ================= 门面:四格就是它自己(与 `引擎实现C` 同一个形状) =================

        public 编译服务C 编译 => this;
        public 调试服务C 调试 => this;
        public 宿主与文件服务C 宿主 => this;
        public 图形服务C 图形 => this;

        /// <summary> 前端走到契约上**没列**的那一格时当场炸 —— 那正是"契约漏了"的证据</summary>
        private static T 不该走到<T>(string 格) =>
            throw new NotSupportedException($"假前端:{格} 这一格前端不该走到 —— 走到了说明契约上漏了这一格的定义");

        private static void 不该走到(string 格) =>
            throw new NotSupportedException($"假前端:{格} 这一格前端不该走到 —— 走到了说明契约上漏了这一格的定义");

        // ================= 一、编译那一族 =================

        public 汇编调试结果C? 最近结果 { get; private set; }
        public 汇编调试结果C? 当前结果 { get; private set; }
        public IReadOnlyList<(string 名称, string 内容)>? 最近源码表 { get; private set; }

        /// <summary>
        /// 假汇编:发一段**手写的**机器码 —— 一行源码一个字节,值 = 那一行的行号(第 1 行 = 1,第 2 行 = 2…)。
        /// 初值表也手写一条(`计数 = 7`),这样"装载连初值一起落进数据段"那件事在假前端上也验得到。
        /// **一个字节都不经过汇编器。**
        /// </summary>
        public 汇编调试结果C 汇编多文件带调试(
            IEnumerable<(string 名称, string 内容)> 源文件表, bool 优化 = true, int 堆大小 = 0,
            IReadOnlyList<string>? 工程归属 = null, int 池大小 = 0, int 内存大小 = 0,
            int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
        {
            编译次数++;
            var 表 = 源文件表.ToList();
            int 行数 = Math.Max(表.Sum(一 => (一.内容 ?? "").Split('\n').Length), 1);
            byte[] 码 = new byte[行数];
            for (int i = 0; i < 码.Length; i++) 码[i] = (byte)(i + 1);
            var 结果 = new 汇编调试结果C { 机器码 = 码, 代码字节数 = 码.Length };
            结果.初值表.Add(new 初值项C { 地址 = 0x400, 值 = 7, 宽度 = 4 });
            最近结果 = 结果;
            最近源码表 = 表;
            return 结果;
        }

        public byte[] 汇编单文件(string 源代码, string? 基目录 = null) =>
            [.. Enumerable.Range(1, Math.Max((源代码 ?? "").Split('\n').Length, 1)).Select(i => (byte)i)];

        public Dictionary<string, int> 取标签表() => new() { ["假标签"] = 0x400 };
        /// <summary> H100 第二步:假前端没有真账本槽(所以 `内存用量` 那一格如实给全 0) </summary>
        public Dictionary<string, int> 取内部槽地址() => [];
        public void 设最近源码表(IReadOnlyList<(string 名称, string 内容)>? 源码表) => 最近源码表 = 源码表;
        public void 清最近结果() { 最近结果 = null; 最近源码表 = null; }
        public void 记当前结果(汇编调试结果C? 结果) => 当前结果 = 结果;
        public int 寄存结果(汇编调试结果C 结果) => 1;
        public IReadOnlyList<初值项C> 初值表() => 最近结果?.初值表 ?? [];
        public IReadOnlyList<文件映射C> 调试文件表() => [];
        public (List<string> 消息表, int 错误槽地址) 打包材料() => ([], 0);
        public C后端结果C 转译成C(int 内存大小) => 不该走到<C后端结果C>("编译.转译成C");
        public C后端结果C 按号转译成C(int 结果号, int 内存大小) => 不该走到<C后端结果C>("编译.按号转译成C");

        public int 默认堆大小 => 64 * 1024;
        public int 默认池大小 => 4096;
        public int 默认帧大小 => 64;
        public int 最小帧大小 => 32;
        public int 最大帧大小 => 4096;
        public int 默认内存大小 => 1024 * 1024;
        public int 最大内存大小 => 虚拟机C.最大内存大小;
        public int 默认软栈大小(int 内存大小) => 内存大小 / 8;
        public int 栈顶保留(int 内存大小) => 内存大小 / 4;
        public string 切换中英(bool 使用中文, string 代码) => 代码;
        public 编译错误C.单页诊断 取诊断(编译异常C? 错, string? 这一页全称, int 这一页行数, bool 是当前页)
            => 不该走到<编译错误C.单页诊断>("编译.取诊断");
        public List<(string 全称, int 行, string 文本)> 取各条(编译异常C? 错) => 不该走到<List<(string 全称, int 行, string 文本)>>("编译.取各条");
        public List<诊断项C> 检查(string 源码, bool 编译通过 = true) => 不该走到<List<诊断项C>>("编译.检查");
        public List<折叠区C.区段> 算折叠区(string 正文) => 不该走到<List<折叠区C.区段>>("编译.算折叠区");
        public string 对齐语义列(string 代码, out string 主换行) { 主换行 = "\n"; return 不该走到<string>("编译.对齐语义列"); }
        public List<补全候选C> 取补全(补全来源C? 来源, 补全请求C 请求, IReadOnlyCollection<string>? 模糊规则 = null)
            => 不该走到<List<补全候选C>>("编译.取补全");

        // ================= 二、调试那一族 =================

        // ---- 两台机器:契约上只剩这三格绕不过具体类型(见类注释) ----

        public 虚拟机C 机器 { get => 占位机; set { } }
        public 虚拟机C 会话机器 { get => 占位机; set { } }
        public 虚拟机C 取机器(bool 会话机) => 占位机;
        public void 请求中断(bool 会话机 = false) { }
        public bool 要重新装载(虚拟机C 机, byte[] 机器码) => !ReferenceEquals(_假机器码, 机器码);
        public bool 机器要重新装载(byte[] 机器码) => !ReferenceEquals(_假机器码, 机器码);

        // ---- 机器读数:假前端**不读真机器**,读自己那本账 ----

        public int 寄存器个数(虚拟机C 机) => 寄存器C.数量;
        public uint 寄存器值(虚拟机C 机, int 号) => (uint)(号 == 0 ? 7 : 0);
        public int 内存长度(虚拟机C 机) => 0x800;
        public byte 内存字节(虚拟机C 机, int 地址) => 地址 == 0x400 ? (byte)7 : (byte)0;
        public byte[] 内存数组(虚拟机C 机) => new byte[0x800];
        public bool 是中断停下(虚拟机C.停下原因C 原因) => false;

        /// <summary>
        /// H100 第二步:**假前端如实给全 0** —— 它不真跑内存,硬编一个"用量"就是把假数据喂进监视条。
        /// 真口径在 `引擎\内存用量C.cs`(那条路由 `前后端边界` 那组里的**真跑**断言钉着)。
        /// </summary>
        public (int 池用, int 池顶, int 堆用, int 堆共, int 软栈用, int 软栈共) 内存用量(虚拟机C 机) => (0, 0, 0, 0, 0, 0);

        // ---- 装载 / 逐步 / 快照 ----

        public void 装载(虚拟机C 机, byte[] 机器码) { 装载次数++; _假机器码 = 机器码; 下标 = 0; }
        public void 按内存装载(虚拟机C 机, byte[] 机器码, int 内存大小) { 按内存装载次数++; 装载(机, 机器码); }
        public void 装载会话(byte[] 机器码) { 装载次数++; _假机器码 = 机器码; 下标 = 0; }
        /// <summary> H13:假前端不真跑程序,热重载只如实记一笔(契约上这一格必须有人应答,否则界面那条路编不过)</summary>
        public int 热重载次数;
        public bool 热重载(虚拟机C 机, byte[] 机器码) { 热重载次数++; _假机器码 = 机器码; 下标 = 0; return true; }

        public bool 会话有机器码 => _假机器码.Length > 0;
        public byte[] 会话机器码 => _假机器码;
        public bool 会话机器码就是这一份(byte[] 机器码) => ReferenceEquals(_假机器码, 机器码);
        public bool 会话已停机 => 下标 >= _假机器码.Length;
        public long 会话已执行步数 => 已走步数;
        public string? 会话当前文件 { get; set; } = "假.hb";
        public bool 会话汇编模式 { get; set; }
        public int 会话内存大小 { get; set; } = 0x800;
        public IReadOnlyList<文件映射C> 会话调试文件表 { get; set; } = [];
        public Dictionary<string, int> 会话标签表 { get; set; } = [];
        /// <summary> H100 第二步:假前端不真跑内存,账本槽地址如实收着但算不出用量(见 `内存用量` 那一格)</summary>
        public Dictionary<string, int> 会话内部槽地址 { get; set; } = [];
        public HashSet<int> 会话断点地址 { get; set; } = [];
        public Func<int, bool>? 会话断点命中判断 { get; set; }
        public 调用栈标记C 会话调用栈 { get; } = new();
        public void 会话清空() { 会话清空次数++; _假机器码 = []; 下标 = 0; }
        public void 记会话装载() { }
        public bool 会话装载机换过() => false;

        public 调试步进结果C 逐步一次()
        {
            if (下标 >= _假机器码.Length) return 停机快照(推进: false);
            return 停机快照(推进: true);
        }

        public 调试步进结果C 运行到断点(long 上限 = 5_000_000)
        {
            var 末 = 逐步一次();
            while (!末.已停机 && 已走步数 < 上限) 末 = 逐步一次();
            return 末;
        }

        public 调试步进结果C 继续(long 上限 = 5_000_000) => 运行到断点(上限);
        public 调试步进结果C 会话快照(bool 这一步跳出去 = false) { 快照次数++; return 停机快照(推进: false); }
        public (string 文件, int 行)? 会话取位置(int 地址)
            => 地址 >= 0 && 地址 < _假机器码.Length ? ("假.hb", _假机器码[地址]) : null;

        public 会话状态C 读状态(byte[]? 机器码 = null)
            => new(会话有机器码, 会话机器码就是这一份(机器码 ?? []), 会话已停机, false, 已走步数);
        public 会话状态C 读状态(调试会话C 会话, byte[]? 机器码 = null) => 读状态(机器码);

        /// <summary> 攒一份"假步进结果":一步 = 假机器码里的一个字节(值就是那一行的行号)</summary>
        private 调试步进结果C 停机快照(bool 推进)
        {
            if (推进)
            {
                下标++;
                已走步数++;
                // 图形那一族:每"走一步"往挂上的呈现口推一帧(证明"收图形命令流"也走契约这一条路)
                if (这段用图形 && _呈现口 is { } 口)
                {
                    int 行 = 下标 > 0 && 下标 <= _假机器码.Length ? _假机器码[下标 - 1] : 0;
                    口.呈现([new 绘制命令C { 动作 = "画矩形", x = 行, y = 1, 宽 = 2, 高 = 3, 颜色 = 0xFF00FF00u }]);
                    图形帧.Add($"帧@{行}");
                }
            }
            int 当前行 = 下标 > 0 && 下标 <= _假机器码.Length ? _假机器码[下标 - 1] : 0;
            var 一步 = new 调试步进结果C
            {
                程序计数器 = 下标,
                已停机 = 下标 >= _假机器码.Length,
                已执行步数 = 已走步数,
                文件 = "假.hb",
                源码行 = 当前行,
                显示行 = 当前行,
                指令文本 = $"假指令 行={当前行}",
            };
            一步.变量.Add(new 变量读数C { 名字 = "假计数", 类型名 = "整数", 可读 = true, 值文本 = "7", 位置文本 = "R0" });
            return 一步;
        }

        // ---- 断点表:一本内存账本(界面那条路走得到这几格) ----

        private readonly HashSet<(string 文件, int 行)> _断点 = [];
        private readonly Dictionary<(string 文件, int 行), string> _断点条件 = [];
        private readonly Dictionary<(string 文件, int 行), (string 条件, string 次数, string 日志)> _断点全项 = [];

        public void 加载断点(string? 工程目录) { _断点.Clear(); _断点条件.Clear(); _断点全项.Clear(); }
        public int[] 断点行号(string 全称) => [.. _断点.Where(x => x.文件 == 全称).Select(x => x.行).OrderBy(x => x)];
        public int[] 断点附加行号(string 全称) =>
            [.. _断点全项.Where(x => x.Key.文件 == 全称 && (x.Value.条件.Length > 0 || x.Value.次数.Length > 0 || x.Value.日志.Length > 0))
                .Select(x => x.Key.行).OrderBy(x => x)];
        public HashSet<int> 断点地址(IReadOnlyList<文件映射C> 调试文件表) => [];
        public Dictionary<int, 断点项C> 断点地址带条件(IReadOnlyList<文件映射C> 调试文件表) => [];
        public bool 有断点(string 全称, int 行) => _断点.Contains((全称, 行));
        public string 取断点条件(string 全称, int 行) => _断点条件.GetValueOrDefault((全称, 行), "");

        /// <summary> P34:整项那一格(假前端也按"真有一本账"来,界面的设置弹窗走这条路)</summary>
        public 断点项C? 取断点(string 全称, int 行) =>
            _断点全项.TryGetValue((全称, 行), out var 项)
                ? new 断点项C { 文件 = 全称, 行 = 行, 条件 = 项.条件, 命中次数 = 项.次数, 日志 = 项.日志 }
                : null;

        public bool 设断点(string 全称, int 行, string 条件, string 命中次数, string 日志)
        {
            _断点全项[(全称, 行)] = (条件, 命中次数, 日志);
            _断点条件[(全称, 行)] = 条件;
            return true;
        }

        public void 设断点条件(string 全称, int 行, string 条件) => _断点条件[(全称, 行)] = 条件;
        public void 保存断点(string? 工程目录) { }
        public bool 切换断点(string 全称, int 行) => _断点.Add((全称, 行)) || !_断点.Remove((全称, 行));
        public int 断点个数 => _断点.Count;
        public void 清除断点() { _断点.Clear(); _断点条件.Clear(); _断点全项.Clear(); }
        public int 清失效断点(IReadOnlyList<文件映射C> 调试文件表) => 0;
        public string 断点摘要() => _断点.Count == 0 ? "" : $"假断点 {_断点.Count} 个";
        public string 断点条件摘要() => "";
        public bool 断点表有文件(IReadOnlyList<文件映射C> 调试文件表, string 全称) => 调试文件表.Count == 0 || 调试文件表.Any(x => x.文件 == 全称);
        public int? 断点算某行地址(IReadOnlyList<文件映射C> 调试文件表, string 全称, int 行) => 行;

        // ================= 三、宿主与文件那一族 =================

        public bool 是同一个文件(string? 左, string? 右) =>
            string.Equals((左 ?? "").Replace('\\', '/'), (右 ?? "").Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        public string 读源文件(string 路径) => 文件.TryGetValue(路径, out var 文) ? 文 : "";
        public string 解源文件(byte[] 字节, string 路径 = "") => System.Text.Encoding.UTF8.GetString(字节);
        public void 写回(string 路径, string 文本) => 文件[路径] = 文本;
        public bool 文件存在(string 路径) => 文件.ContainsKey(路径);
        public string 读文本文件(string 路径) => 读源文件(路径);
        public void 写文本文件(string 路径, string 文本) => 文件[路径] = 文本;
        public void 建目录(string 目录) { }
        public void 挂上文件实现(文件接口C? 实现) => 挂过的文件实现 = 实现?.GetType().Name ?? "(摘掉)";
        public void 摘掉文件实现() => 挂过的文件实现 = "(摘掉)";
        public 文件接口C? 当前文件实现 => null;
        public void 设基准目录(string? 目录) => 基准目录 = 目录;
        public void 设可执行白名单(IReadOnlyDictionary<string, string>? 表) { }
        public void 设网址白名单(网址白名单C? 表) { }
        public int 处理(byte[] 内存, uint 参数区, int 功能号) => 0;
        public 宿主能力种C 问能力(string 可执行路径, int 该到几号 = 0, int 该到包格式 = 0) => new();
        public string? 陈旧原因(string 可执行路径, int 该到几号 = 0, int 该到包格式 = 0) => "假前端:没有真宿主";
        public string 能力上限文本(宿主能力种C 能力) => "假前端";
        public string 包格式上限文本(宿主能力种C 能力) => "假前端";
        public int 这段机器码要的功能号上限(byte[] 机器码) => 0;
        public byte[] 打包(byte[] 宿主字节, byte[] 机器码, IReadOnlyList<string> 消息表, int 错误槽地址,
            IReadOnlyList<初值项C>? 初值表 = null, 源行表C? 源行表 = null)
            => 不该走到<byte[]>("宿主.打包");
        public int 段初值包格式 => 1;
        public List<文件映射C> 建全工程表(
            IReadOnlyList<(string 名称, string 内容)> 文件表, bool 优化 = true, int 堆大小 = 0,
            IReadOnlyList<string>? 工程归属 = null, int 池大小 = 0, int 内存大小 = 0,
            int 软栈大小 = -1, bool 边界检查 = true, int 帧大小 = -1)
            => 不该走到<List<文件映射C>>("宿主.建全工程表");
        public 源行表C 压源行表(IReadOnlyList<文件映射C>? 段表) => 不该走到<源行表C>("宿主.压源行表");

        // ================= 四、图形那一族 =================

        private 图形呈现口C? _呈现口;

        public 图形呈现口C? 呈现口 => _呈现口;
        public void 挂上(图形呈现口C? 口) => _呈现口 = 口;
        public void 摘掉() => _呈现口 = null;
        public void 清空() => 图形帧.Clear();
        public Action<int, int, int, int>? 事件出口 { get; set; }
        public void 投事件(int 类型, int 键码, int x, int y) => 投事件次数++;
        public bool 这段机器码用图形(byte[] 机器码) => 这段用图形;
    }
}
