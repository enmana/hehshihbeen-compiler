using System;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 第169批(C28):把注册表里那条"用某个程序打开网页"的命令**套成一条能直接喂 `Process.Start` 的命令行**。
    /// <para>
    /// 为什么需要它:帮助菜单原先走的是 `Process.Start(网址, UseShellExecute = true)` —— 把 `.html` 交给
    /// shell 按**文件关联**去开。可用户这台机器上那条关联是坏的(实测 `AssocQueryString(".html")` 只回
    /// `OpenWith.exe`,`file` 协议干脆回 `0x80070483` = 没有关联)⇒ `Process.Start` 抛异常,而异常只写进
    /// 底部那张平时收着的『异常』页 ⇒ 用户看到的就是**点了没反应**。
    /// 现在的路子是:先按注册表里 `http` 的 UserChoice 问出**默认浏览器**是谁(`默认浏览器C`,Windows 专属,
    /// 只能待在界面层),再用这里的纯函数把 `"…\msedge.exe" --single-argument %1` 这类模板套成
    /// `"…\msedge.exe" --single-argument file:///…`(**那条开关下面不加引号**,理由见下面 `套命令行` 的实证)。
    /// </para>
    /// <para>
    /// **纯函数**:不碰注册表、不起进程、不读文件 —— 自检直接喂模板字符串验它(第169批新增 4 条)。
    /// 这条路是跨平台的(引擎库是 `net10.0`,Android 那端也编它),所以注册表那条腿不许搬进来。
    /// </para>
    /// </summary>
    public static class 浏览器命令C
    {
        /// <summary> 认得的占位符(`%1` 是 shell 的老规矩;`%L` / `%l` 也有程序这么写,大小写两种都认) </summary>
        public static readonly string[] 占位符 = ["%1", "%l", "%L"];

        /// <summary>
        /// Edge / Chrome 注册表命令里的那个开关:`--single-argument`。
        /// **它的语义是"后面那条尾巴的原文就是网址"** —— 浏览器按原文取,不按 `argv` 拆,
        /// 于是加进去的引号会**原样成为网址的一部分**(见 `套命令行` 里那条实证)。
        /// </summary>
        public const string 单参数开关 = "--single-argument";

        /// <summary>
        /// 一条 shell open 命令模板 + 网址 →(可执行文件, 参数)。
        /// 口径(逐条都对应一种真实写法,别按直觉改):
        /// <list type="number">
        /// <item>可执行文件**带引号**就取引号里那一段(路径里有空格时必须这样写:`"C:\Program Files (x86)\…\msedge.exe"`);
        ///   不带引号就取到第一个空格为止;</item>
        /// <item>网址**默认加引号**再填进去 —— 网址里有 `%` 转义与 `#`,不加引号可能被 shell 拆开
        ///   (**例外**见下面 `--single-argument` 那一条);</item>
        /// <item>模板里写成 `"%1"`(占位符自带引号)时按那个形式换掉,免得套出两个引号;</item>
        /// <item>模板里有 `--single-argument`(Edge / Chrome 就是这种)**网址一律裸放、不加引号** ——
        ///   那条开关的语义是"后面的原文就是网址",引号会被浏览器当成网址的一部分(第170批实测,见下);</item>
        /// <item>模板里**一个占位符都没有**(例如只写了程序名)时,把网址接在参数尾巴上 —— 总比什么都不做强。</item>
        /// </list>
        /// 拆不出可执行文件(空模板 / 只有半个引号)时给两个空串,调用方据此判断"这条路走不通"。
        /// <para>
        /// **第170批的实证(别再按直觉把引号加回去)**:用户实测"帮助还是打不开,浏览器报
        /// `DNS_PROBE_FINISHED_NXDOMAIN`、主机名看着是 `file`"。查法是去读 Edge 的会话文件
        /// (`…\Edge\User Data\Default\Sessions\*`,里面记着真正跳过去的网址),那条是
        /// `http://"file///E:/Codes/…/index.html%22` —— **网址两头各带一个字面引号**;
        /// 又用 `cmd /c echo %CMDCMDLINE%` 验过 `Process.Start` 传参是**原样**的(没把 `"` 转义成 `\"`),
        /// 所以引号只可能来自"我们写进了命令行、浏览器又把它们当成网址原文"。
        /// 改成裸放之后,那两头的引号就没了。
        /// </para>
        /// <para>
        /// 裸放安全的前提:`网址` 是 `帮助站点C.网址` 用 `Uri.AbsoluteUri` 转出来的,空格与中文都已转义,
        /// 里面不会有空白字符把尾巴拆开。
        /// </para>
        /// </summary>
        public static (string 可执行文件, string 参数) 套命令行(string? 命令模板, string 网址)
        {
            string 模板 = (命令模板 ?? string.Empty).Trim();
            if (模板.Length == 0 || 网址.Length == 0) return (string.Empty, string.Empty);

            string 可执行文件, 参数;
            if (模板[0] == '"')
            {
                int 闭引号 = 模板.IndexOf('"', 1);
                if (闭引号 < 0) return (string.Empty, string.Empty);
                可执行文件 = 模板[1..闭引号];
                参数 = 模板[(闭引号 + 1)..].Trim();
            }
            else
            {
                int 第一个空格 = 模板.IndexOf(' ');
                if (第一个空格 < 0) { 可执行文件 = 模板; 参数 = string.Empty; }
                else { 可执行文件 = 模板[..第一个空格]; 参数 = 模板[(第一个空格 + 1)..].Trim(); }
            }
            if (可执行文件.Length == 0) return (string.Empty, string.Empty);

            // 单参数开关下面**不加引号**(理由见上面的实证);别的模板照旧加引号
            string 填入 = 参数.Contains(单参数开关, StringComparison.OrdinalIgnoreCase)
                ? 网址
                : "\"" + 网址 + "\"";
            foreach (string 占位 in 占位符)
            {
                string 带引号的占位 = "\"" + 占位 + "\"";
                if (参数.Contains(带引号的占位, StringComparison.Ordinal))
                    return (可执行文件, 参数.Replace(带引号的占位, 填入, StringComparison.Ordinal));
            }
            foreach (string 占位 in 占位符)
            {
                if (参数.Contains(占位, StringComparison.Ordinal))
                    return (可执行文件, 参数.Replace(占位, 填入, StringComparison.Ordinal));
            }
            return (可执行文件, 参数.Length == 0 ? 填入 : 参数 + " " + 填入);
        }
    }
}
