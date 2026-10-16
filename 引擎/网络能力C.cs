using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// D5:**网络能力**的引擎侧框架(与四个宿主共用,零 UI 依赖)。
    ///
    /// 分工(与 P28 宿主能力同一套):
    /// **宿主**决定"能不能连"(工程文件 `.hbp` 的 `网址` 表 → `宿主能力C.设网址白名单`),
    /// **这一层**决定"怎么连"(同步一次性 GET + 超时 + 上限,失败一律给应答对象,不上抛)。
    ///
    /// 为什么叫"框架"而不是"一个函数":
    /// ① 白名单(`网址白名单C`)与传输(`网络能力C.传输`)是**两块可换的零件** ——
    ///    以后加 POST / 请求头 / 流式,只要在 `网络请求C` 上加字段,白名单那一半一个字不用动;
    ///    别的宿主想换网络栈(或者自检要替身)把 `传输` 一挂就行;
    /// ② 编解码口径、字节上限、超时口径都在这一层收口,以后写 `Sample\库\*.hb` 的网络库不用各写一遍。
    /// </summary>
    public sealed class 网址白名单项C
    {
        /// <summary> 主机名或 IP(不含协议、不含端口;IPv6 请用 `[::1]:8080` 这种带方括号的写法) </summary>
        public string 主机 { get; init; } = "";

        /// <summary> 端口;**0 = 没写**(按协议的默认端口算:http 80 / https 443) </summary>
        public int 端口 { get; init; }

        public string 文本 => 端口 == 0 ? 主机 : $"{主机}:{端口}";

        /// <summary> 这条声明的端口对某个协议来说"等于没写"吗(http/https 的默认端口) </summary>
        private bool 吃默认端口(string 协议) => 端口 == 0 || 端口 == 协议默认端口(协议);

        /// <summary> 协议 → 默认端口;不认识的协议给 -1(调用方据此判"不支持") </summary>
        public static int 协议默认端口(string 协议) => 协议.ToLowerInvariant() switch
        {
            "http" => 80,
            "https" => 443,
            _ => -1,
        };

        /// <summary> 这个网址的主机 + 端口命中本行声明吗(**只比主机与端口,不管路径与查询串**) </summary>
        public bool 命中(Uri 网址)
        {
            if (协议默认端口(网址.Scheme) < 0) return false;                  // 非 http/https:白名单不认
            if (!string.Equals(网址.Host, 主机, StringComparison.OrdinalIgnoreCase)) return false;
            return 吃默认端口(网址.Scheme) || 网址.Port == 端口;
        }

        /// <summary>
        /// 解析白名单里的一行。认三种写法:`主机`、`主机:端口`、以及完整的 `http://主机[:端口]`。
        /// 端口留空 = 按协议默认端口(所以 `例子.测试` 同时允许它 80 端口上的 http 与 443 端口上的 https)。
        /// </summary>
        public static bool 试解析(string 行, out 网址白名单项C? 项, out string 错)
        {
            项 = null;
            错 = "";
            string 文本 = (行 ?? "").Trim().Trim('　');
            if (文本.Length == 0) { 错 = "这一行是空的"; return false; }

            // 写法③:完整网址(带 ://)—— 只取它的主机与端口
            if (文本.Contains("://", StringComparison.Ordinal))
            {
                if (!Uri.TryCreate(文本, UriKind.Absolute, out Uri? 网址) || 协议默认端口(网址.Scheme) < 0)
                { 错 = $"`{文本}` 不是有效的 http/https 网址"; return false; }
                if (网址.Host.Length == 0) { 错 = $"`{文本}` 里没写主机"; return false; }
                项 = new 网址白名单项C { 主机 = 网址.Host, 端口 = 网址.IsDefaultPort ? 0 : 网址.Port };
                return true;
            }

            // 写法①/②:主机 / 主机:端口
            string 主机 = 文本;
            int 端口 = 0;
            int 冒号 = 文本.LastIndexOf(':');
            if (冒号 >= 0)
            {
                string 尾巴 = 文本[(冒号 + 1)..];
                if (尾巴.Length > 0 && 尾巴.All(char.IsAsciiDigit))
                {
                    if (!int.TryParse(尾巴, out 端口) || 端口 < 1 || 端口 > 65535)
                    { 错 = $"`{文本}` 的端口要在 1 ~ 65535 之间"; return false; }
                    主机 = 文本[..冒号];
                }
                else if (冒号 == 文本.IndexOf(':'))
                {
                    // 只有一个冒号、右边又不是数字:多半是把端口写错了(比如写成了全角冒号或字母)
                    错 = $"`{文本}` 里的端口不是数字(要写 `主机:端口`,或者只写 `主机`)";
                    return false;
                }
            }
            主机 = 主机.Trim();
            if (主机.Length == 0) { 错 = $"`{文本}` 里没写主机"; return false; }
            if (主机.Any(char.IsWhiteSpace)) { 错 = $"`{文本}` 的主机里有空格/换行"; return false; }
            if (主机.Contains('/') || 主机.Contains('\\'))
            { 错 = $"`{文本}` 里只能写主机(不要带路径):要限制路径的话先写 `主机:端口`,路径由程序自己拼"; return false; }
            if (主机.Contains(':'))
            { 错 = $"`{文本}` 里冒号太多:IPv6 请写成 `[::1]:端口` 这样的完整网址(带 `http://`)"; return false; }
            项 = new 网址白名单项C { 主机 = 主机, 端口 = 端口 };
            return true;
        }

        public override string ToString() => 文本;
    }

    /// <summary>
    /// **网址白名单**:工程文件 `.hbp` 的 `网址` 表(一行一条)解析出来的结果。
    /// HB 程序**永远给不了"没声明过的主机"** —— 不在表里连试都不试(与 `可执行文件` 白名单同一套安全边界)。
    /// 解析不了的行不会让工程打不开:进 `问题` 表,由 `工程文件C.检查()` 报给用户。
    /// </summary>
    public sealed class 网址白名单C
    {
        private readonly List<网址白名单项C> _项 = [];
        private readonly List<string> _问题 = [];

        /// <summary> 空表(= 这个工程没声明任何网址 ⇒ 那个宿主没有网络能力) </summary>
        public static 网址白名单C 空 { get; } = new();

        public IReadOnlyList<网址白名单项C> 项表 => _项;
        public IReadOnlyList<string> 问题 => _问题;
        public bool 是空的 => _项.Count == 0;

        /// <summary> 按行解析(逗号/分号也当分隔符:一行里写两条也能认) </summary>
        public static 网址白名单C 由文本(IEnumerable<string>? 行)
        {
            var 表 = new 网址白名单C();
            foreach (string 原始 in 行 ?? [])
            {
                if (原始 == null) continue;
                foreach (string 段 in 原始.Split(['\n', '\r', ',', ';', '、'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!网址白名单项C.试解析(段, out 网址白名单项C? 项, out string 错))
                    {
                        表._问题.Add($"`网址` 表里的 `{段.Trim()}` 写的不对:{错}");
                        continue;
                    }
                    if (项 == null) continue;
                    if (表._项.Any(x => string.Equals(x.文本, 项.文本, StringComparison.OrdinalIgnoreCase))) continue;   // 重复的悄悄去掉
                    表._项.Add(项);
                }
            }
            return 表;
        }

        /// <summary> 这个网址在不在表里(命中时把那一行给出去,便于报错时说清"是哪条声明放行的") </summary>
        public bool 允许(Uri 网址, out 网址白名单项C? 命中)
        {
            命中 = null;
            foreach (var 项 in _项)
                if (项.命中(网址)) { 命中 = 项; return true; }
            return false;
        }

        /// <summary>
        /// **裸 socket(G3 域 3:网络 20~26)那一档的白名单判据** —— 与 `允许(Uri, …)` 的分工:
        /// 那条走的是"http / https 的**默认端口**"口径(写一行 `例子.测试` 就同时放行它 80 上的 http
        /// 与 443 上的 https);裸 socket 没有"协议"这回事,**一条不带端口的声明等于放行这台主机的所有端口**
        /// —— 那比白名单本身的用意宽太多。⇒ 裸 socket 只认**带端口**的声明(`主机:端口`,
        /// 或者带协议的完整网址,但那个网址必须写到端口)。
        ///
        /// 判定与取网址同一条逻辑:**不在表里连 DNS 都不做**(调用方先在 `宿主能力C` 里过这一关)。
        /// </summary>
        public bool 允许主机端口(string 主机, int 端口, out 网址白名单项C? 命中)
        {
            命中 = null;
            if (主机.Length == 0 || 端口 < 1 || 端口 > 65535) return false;
            foreach (var 项 in _项)
            {
                if (项.端口 == 0) continue;                                    // 不带端口的声明不管裸 socket
                if (项.端口 != 端口) continue;
                if (string.Equals(项.主机, 主机, StringComparison.OrdinalIgnoreCase)) { 命中 = 项; return true; }
            }
            return false;
        }

        /// <summary>
        /// `主机解析` 那一关:这台主机**有没有**一条带端口的声明。
        /// 为什么不像连接那样连端口一起比:解析这个名字本身不带端口,而"能不能解析"是白名单管的第一道
        /// (先声明、再解析、最后才连接)—— 只要这台主机在表里出现过(带端口的那一式),就让它解析。
        /// </summary>
        public bool 有主机(string 主机)
        {
            if (主机.Length == 0) return false;
            foreach (var 项 in _项)
            {
                if (项.端口 == 0) continue;
                if (string.Equals(项.主机, 主机, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public string 文本 => string.Join("、", _项.Select(x => x.文本));

        public override string ToString() => 是空的 ? "(空)" : 文本;
    }

    /// <summary> 一次网络请求(现在只有 GET;以后加 POST / 请求头就往这儿加字段,别的层不用动) </summary>
    public sealed class 网络请求C
    {
        public Uri 网址 { get; init; } = new("http://127.0.0.1/");
        public int 超时毫秒 { get; init; } = 网络能力C.默认超时毫秒;

        /// <summary> 响应体最多收多少字节(超了给"放不下",**不截断**:截断会让人以为拿到了全文) </summary>
        public int 上限字节 { get; init; } = 网络能力C.默认上限字节;
    }

    /// <summary> 一次网络请求的结果(成功失败都是一个对象:`状态` 是宿主能力状态码,0 = 成功) </summary>
    public sealed class 网络应答C
    {
        public int 状态 { get; init; } = 宿主能力C.状态_成功;
        public int HTTP状态码 { get; init; }
        public byte[] 内容 { get; init; } = [];
        public string 说明 { get; init; } = "";
        public bool 成功 => 状态 == 宿主能力C.状态_成功;

        public static 网络应答C 错(int 状态, string 说明, int HTTP状态码 = 0) =>
            new() { 状态 = 状态, 说明 = 说明, HTTP状态码 = HTTP状态码 };
    }

    /// <summary>
    /// 网络取数的**本机实现**(四个宿主共用一份)+ 可替换的传输钩子。
    /// 一次性同步请求:**不跟跳转**(3xx 算失败 —— 跳转可能把你带到白名单外的机器上)、**响应有上限**、
    /// **超时硬性**(超时就把这次请求掐掉;`宿主调用` 本身是同步的,所以超时是唯一能保证"程序还能往下走"的东西)。
    /// </summary>
    public static class 网络能力C
    {
        public const int 默认超时毫秒 = 10000;
        public const int 最大超时毫秒 = 3600_000;          // 与 `运行进程` 同一口径:超时上限 1 小时
        public const int 默认上限字节 = 256 * 1024;
        public const int 最大上限字节 = 4 * 1024 * 1024;

        /// <summary>
        /// 传输钩子:null = 用下面这份本机实现。自检的**替身**、或者以后某个宿主想换网络栈,往这儿挂一个就行 ——
        /// 白名单那一段在前面(`宿主能力C.处理取网址`)已经拦过了,替身只负责"真的去取一次"。
        /// </summary>
        public static Func<网络请求C, 网络应答C>? 传输 { get; set; }

        public static void 清替身() => 传输 = null;

        /// <summary> 取一次(GET)。**任何异常都变成应答对象**,不上抛 —— 宿主能力那一层从不抛异常给虚拟机 </summary>
        public static 网络应答C 取(网络请求C 请求)
        {
            try { return (传输 ?? 本机取)(请求); }
            catch (Exception ex) { return 网络应答C.错(宿主能力C.状态_读写失败, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        private static readonly Lazy<HttpClient> _客户端 = new(造客户端);

        private static HttpClient 造客户端() => new(new HttpClientHandler
        {
            AllowAutoRedirect = false,     // 不跟跳转:跳到别的主机 = 绕过白名单(3xx 会被报成"响应出错")
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        })
        {
            Timeout = Timeout.InfiniteTimeSpan,     // 超时按**每次请求**用 CancellationTokenSource 管,不用这个全局值
        };

        /// <summary> 本机实现:同步一次性 GET(http/https) </summary>
        public static 网络应答C 本机取(网络请求C 请求)
        {
            int 超时 = 请求.超时毫秒 <= 0 ? 默认超时毫秒 : Math.Min(请求.超时毫秒, 最大超时毫秒);
            int 上限 = 请求.上限字节 <= 0 ? 默认上限字节 : Math.Min(请求.上限字节, 最大上限字节);
            using var 掐 = new CancellationTokenSource(TimeSpan.FromMilliseconds(超时));
            try
            {
                using var 消息 = new HttpRequestMessage(HttpMethod.Get, 请求.网址);
                using HttpResponseMessage 应答 = _客户端.Value.Send(消息, HttpCompletionOption.ResponseHeadersRead, 掐.Token);
                int 码 = (int)应答.StatusCode;
                if (码 < 200 || 码 >= 300)
                    return 网络应答C.错(宿主能力C.状态_响应出错, $"服务器回了 HTTP {码}", 码);

                using Stream 流 = 应答.Content.ReadAsStream(掐.Token);
                byte[] 原始 = 读一块(流, 上限, out bool 超了, 掐.Token);
                if (超了)
                    return 网络应答C.错(宿主能力C.状态_放不下, $"响应超过 {上限} 字节(上限是编译期那个数字)", 码);

                byte[] 内容 = 规范成UTF8(原始,
                    应答.Content.Headers.ContentType?.MediaType,
                    应答.Content.Headers.ContentType?.CharSet);
                return new 网络应答C { 状态 = 宿主能力C.状态_成功, HTTP状态码 = 码, 内容 = 内容 };
            }
            catch (OperationCanceledException) when (掐.IsCancellationRequested)
            {
                return 网络应答C.错(宿主能力C.状态_网络超时, $"超过 {超时} 毫秒还没等到响应");
            }
            catch (HttpRequestException ex)
            {
                return 网络应答C.错(宿主能力C.状态_连不上, $"连不上:{ex.Message}");
            }
            catch (Exception ex)
            {
                return 网络应答C.错(宿主能力C.状态_读写失败, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary> 读响应体,最多 `上限` 字节;再多一个字节也读得到 ⇒ 置 `超了`(不截断,让调用方去报"放不下") </summary>
        private static byte[] 读一块(Stream 流, int 上限, out bool 超了, CancellationToken 掐)
        {
            超了 = false;
            using var 收 = new MemoryStream();
            byte[] 缓冲 = new byte[Math.Min(上限, 8192) + 1];
            while (true)
            {
                int 读 = 流.Read(缓冲, 0, 缓冲.Length);
                if (读 <= 0) break;
                int 还要 = 上限 - (int)收.Length;
                if (读 > 还要) { 超了 = true; 收.Write(缓冲, 0, Math.Max(还要, 0)); break; }
                收.Write(缓冲, 0, 读);
                掐.ThrowIfCancellationRequested();
            }
            return 收.ToArray();
        }

        /// <summary>
        /// 响应体统一成"HB 字符串里存的那份字节"。
        /// 文本类(text/json/xml/html/js):按 `Content-Type` 的 charset 解,没写就按
        /// "先严 UTF-8、再系统代码页"猜(`宿主能力C.解码输出`,与子进程输出同一套),再统一按 UTF-8 存 ——
        /// 于是中文站点的 GBK 页面进 HB 字符串也是对的。
        /// **二进制类(图片/压缩包/字体…)一个字节都不动**:转码会把它们毁掉。
        /// </summary>
        public static byte[] 规范成UTF8(byte[] 原始, string? 媒体类型, string? 字符集)
        {
            if (原始.Length == 0) return 原始;
            if (!是文本类(媒体类型)) return 原始;
            string 文本;
            Encoding? 指定 = null;
            if (!string.IsNullOrWhiteSpace(字符集))
            {
                try { 指定 = Encoding.GetEncoding(字符集.Trim().Trim('"', '\'')); } catch { 指定 = null; }
            }
            if (指定 != null)
            {
                try { 文本 = 指定.GetString(原始); }
                catch { 文本 = 宿主能力C.解码输出(原始); }
            }
            else 文本 = 宿主能力C.解码输出(原始);
            return Encoding.UTF8.GetBytes(文本);
        }

        /// <summary> 这个媒体类型算不算"文本"(没写 Content-Type 时按文本处理:多数接口都留空) </summary>
        private static bool 是文本类(string? 媒体类型)
        {
            if (string.IsNullOrWhiteSpace(媒体类型)) return true;
            string 类型 = 媒体类型.ToLowerInvariant();
            return 类型.StartsWith("text/", StringComparison.Ordinal)
                || 类型.Contains("json") || 类型.Contains("xml") || 类型.Contains("javascript")
                || 类型.Contains("x-www-form-urlencoded") || 类型.Contains("html");
        }
    }
}
