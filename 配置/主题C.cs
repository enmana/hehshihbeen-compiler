using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace hehshihbeen_compiler.配置
{
    /// <summary> 一套主题(可直接 JSON 序列化保存到 主题/*.json) </summary>
    public class 主题设置
    {
        public string 名称 { get; set; } = "亮";
        public string 背景 { get; set; } = "#FFF0F0F0";       // 主内容背景
        public string 前景 { get; set; } = "#FF1E1E1E";       // 主前景
        public string 面板背景 { get; set; } = "#FFFFFFFF";   // 面板/卡片背景
        public string 编辑器背景 { get; set; } = "#FFFFFFFF";
        public string 编辑器前景 { get; set; } = "#FF1E1E1E";
        public string 行号颜色 { get; set; } = "#FF8A8A8A";
        // 下面这几个是"第86批以前就有的老颜色" —— 保留它们只为**读老主题文件**(见 取词类色 的搬迁),
        // 新的配色一律走 词类颜色 + 配色词类C(选项页里按词类一项一项调)。
        public string 关键字颜色 { get; set; } = "#FF0000FF";
        public string 寄存器颜色 { get; set; } = "#FFFF0000";
        public string 数字颜色 { get; set; } = "#FF800080";
        public string 注释颜色 { get; set; } = "#FF008000";
        public string 默认文本颜色 { get; set; } = "#FF000000";
        public string 强调色 { get; set; } = "#FF0078D7";
        public string 输出框背景 { get; set; } = "#FFF5F5F5";
        public string 输出框前景 { get; set; } = "#FF1E1E1E";

        /// <summary>
        /// **词类颜色**(键 = `配色词类C` 里的词类键,值 = `#AARRGGBB`)。
        /// 第87批起,"按词类上色"的项(`指令/关键字/类型/结构/内建函数/方法/变量/字符串/字符/数字/寄存器/注释/默认`)
        /// 都走这张表,选项页里一项一项都能单独调。
        ///
        /// **老主题文件兼容**:老的 `选项.json` / 自定义主题里没有这一项 → 反序列化后是空字典,
        /// `取词类色` 会先把老属性(`关键字颜色`/`寄存器颜色`/`数字颜色`/`注释颜色`/`默认文本颜色`)搬过来,
        /// 再不够才用 `配色词类C` 按亮/暗背景给的默认值 —— 已经存过的配色一点不浪费,
        /// 也不会因为多了几项就失效(自检有一条专门盯这个)。
        /// </summary>
        public Dictionary<string, string> 词类颜色 { get; set; } = new(StringComparer.Ordinal);

        /// <summary> 当前主题算不算暗色(按 `编辑器背景` 亮度判;用来挑亮/暗两套默认词类色) </summary>
        public bool 是暗主题 => 配色词类C.是暗背景(编辑器背景);

        /// <summary>
        /// 取一个词类在这个主题下的实际颜色。顺序:
        /// ① 本主题 `词类颜色` 里显式存了的 → 用它(选项页改的就是它);
        /// ② 没有 → 看老属性有没有(老主题文件里的 `关键字颜色` 等);
        /// ③ 还没有 → `配色词类C` 按"亮/暗背景"给的默认色。
        /// </summary>
        public string 取词类色(string 词类键) =>
            配色词类C.解析色(词类键, 词类颜色,
                // 老主题(第86批以前)里只有这几个色:按 老属性映射 把值递进去
                老属性 => GetType().GetProperty(老属性)?.GetValue(this) as string,
                是暗主题);

        /// <summary> 把全部词类键填成"这个主题该有的默认色"(内置主题生成时用;显式存过的不动) </summary>
        public void 补全词类默认色()
        {
            词类颜色 ??= new(StringComparer.Ordinal);
            foreach (var 类 in 配色词类C.全部项)
                if (!词类颜色.ContainsKey(类.键)) 词类颜色[类.键] = 是暗主题 ? 类.暗色 : 类.亮色;
        }

        /// <summary> 把 #AARRGGBB 转成画刷 </summary>
        public static SolidColorBrush 转刷(string 十六进制)
        {
            try
            {
                if (十六进制.StartsWith('#'))
                {
                    string hex = 十六进制[1..];
                    if (hex.Length == 6) hex = "FF" + hex;
                    if (hex.Length == 8 &&
                        uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint 值))
                    {
                        return new SolidColorBrush(Color.FromArgb(
                            (byte)(值 >> 24), (byte)(值 >> 16), (byte)(值 >> 8), (byte)值));
                    }
                }
            }
            catch { }
            return Brushes.Transparent;
        }
    }

    /// <summary> 主题管理:内置 暗/亮,以 JSON 保存于 主题/*.json;当前选择保存于 主题/当前主题.json </summary>
    public static class 主题C
    {
        public static string 主题目录 =>
            Path.Combine(程序目录C.数据目录, "主题");

        /// <summary> 当前生效的主题(可能为空) </summary>
        public static 主题设置? 当前主题 { get; private set; }

        /// <summary> 主题切换广播(界面订阅后应用) </summary>
        public static event Action<主题设置>? 主题变化;

        private static readonly Dictionary<string, 主题设置> 内置主题 = new()
        {
            ["暗"] = new 主题设置
            {
                名称 = "暗",
                背景 = "#FF2B2B2B",
                前景 = "#FFE8E8E8",
                面板背景 = "#FF333337",
                编辑器背景 = "#FF1E1E1E",
                编辑器前景 = "#FFDCDCDC",
                行号颜色 = "#FF6E6E6E",
                关键字颜色 = "#FF569CD6",
                寄存器颜色 = "#FF4EC9B0",
                数字颜色 = "#FFB5CEA8",
                注释颜色 = "#FF608B4E",
                默认文本颜色 = "#FFDCDCDC",
                强调色 = "#FF007ACC",
                输出框背景 = "#FF1E1E1E",
                输出框前景 = "#FFDCDCDC",
            },
            ["亮"] = new 主题设置
            {
                名称 = "亮",
                背景 = "#FFF0F0F0",
                前景 = "#FF1E1E1E",
                面板背景 = "#FFFFFFFF",
                编辑器背景 = "#FFFFFFFF",
                编辑器前景 = "#FF1E1E1E",
                行号颜色 = "#FF8A8A8A",
                关键字颜色 = "#FF0000FF",
                寄存器颜色 = "#FFFF0000",
                数字颜色 = "#FF800080",
                注释颜色 = "#FF008000",
                默认文本颜色 = "#FF000000",
                强调色 = "#FF0078D7",
                输出框背景 = "#FFF5F5F5",
                输出框前景 = "#FF1E1E1E",
            },
            // ================= 两套"风格主题"(2026-10-07 加) =================
            // 前两套(暗 / 亮)是**中性**配色,这两套是**带风格**的:整屏一个调子,词类颜色自己重排一份。
            // 它们与其它主题走**同一条路**(`词类颜色` 显式给值 + 界面那几支刷子),所以:
            //   · 选项页里逐项改色照旧生效(改的是同一张表);
            //   · 改完想留住就"另存为新主题"(`取全部主题` 只把这两套按代码刷回文件)。
            // 口径照 `配色词类C` 那张表:指令 / 关键字 / 类型 / 变量 四类**一眼分得开**(H60 那条自称的老毛病)。
            ["黑客帝国"] = new 主题设置
            {
                名称 = "黑客帝国",
                背景 = "#FF030803",                 // 几乎全黑,带一点绿
                前景 = "#FF7CFF9A",
                面板背景 = "#FF07140A",
                编辑器背景 = "#FF020602",
                编辑器前景 = "#FF54FF7E",
                行号颜色 = "#FF1F6B33",
                强调色 = "#FF00FF66",
                输出框背景 = "#FF010401",
                输出框前景 = "#FF48E86E",
                词类颜色 = new(StringComparer.Ordinal)
                {
                    ["默认"] = "#FF54FF7E",
                    ["指令"] = "#FF00FF66",         // 最亮的那管绿:指令 = 机器在说话
                    ["关键字"] = "#FFB6FF6A",       // 偏黄的绿:控制流
                    ["类型"] = "#FF3FE0C8",         // 青绿
                    ["结构"] = "#FF6FE8C0",
                    ["用户方法名"] = "#FFCDFFB0",
                    ["内建函数"] = "#FF5CFFC8",
                    ["方法"] = "#FFE2FF8A",
                    ["变量"] = "#FFC6FFD8",
                    ["常量"] = "#FF8CFF3C",
                    ["寄存器"] = "#FF22DDA8",
                    ["数字"] = "#FF9CFF4A",
                    ["字符串"] = "#FF7BFFB0",
                    ["字符"] = "#FF6CFFD8",
                    ["注释"] = "#FF1F6B33",         // 暗绿:掉进背景里,像终端里那行注释
                },
            },
            ["赛博朋克"] = new 主题设置
            {
                名称 = "赛博朋克",
                背景 = "#FF0B0718",                 // 深紫夜色
                前景 = "#FFE6E1FF",
                面板背景 = "#FF140C2E",
                编辑器背景 = "#FF0A0618",
                编辑器前景 = "#FFEDE7FF",
                行号颜色 = "#FF4A3B7A",
                强调色 = "#FFFF2E88",               // 霓虹品红
                输出框背景 = "#FF080414",
                输出框前景 = "#FF9FFFEF",
                词类颜色 = new(StringComparer.Ordinal)
                {
                    ["默认"] = "#FFEDE7FF",
                    ["指令"] = "#FF2DE2E6",         // 电光青
                    ["关键字"] = "#FFFF2E88",       // 霓虹品红
                    ["类型"] = "#FF22D3EE",
                    ["结构"] = "#FF57F7E0",
                    ["用户方法名"] = "#FFC77DFF",
                    ["内建函数"] = "#FF3B9EFF",     // 电光蓝(原来那管霓虹橙压在暖色上糊了,换掉)
                    ["方法"] = "#FFFFE066",         // 霓虹黄
                    ["变量"] = "#FF7DE2FF",
                    ["常量"] = "#FFB6FF3C",         // 酸绿
                    ["寄存器"] = "#FFFF6BD6",
                    ["数字"] = "#FFFFC857",
                    ["字符串"] = "#FFFF7BAC",
                    ["字符"] = "#FFF08CFF",
                    ["注释"] = "#FF6C5CE7",         // 暗紫:不抢眼但看得见
                },
            },
        };

        /// <summary>
        /// 内置主题建好后,把**全部词类**按它自己的"亮/暗背景"填上默认色 ——
        /// 这样生成的 `暗.json` / `亮.json` 里每一项词类都看得见、能直接改
        /// (改内置会在下次启动被代码刷回,界面会提醒"另存为新主题")。
        /// </summary>
        static 主题C()
        {
            foreach (var 主题 in 内置主题.Values) 主题.补全词类默认色();
        }

        /// <summary> 名称(或别名 dark/light)取主题;找不到返回 null </summary>
        public static 主题设置? 按名称取(string 名称)
        {
            string 名 = 名称?.Trim() ?? "";
            string? 别名映射 = 名.ToLowerInvariant() switch
            {
                "dark" => "暗",
                "light" => "亮",
                _ => null,
            };
            名 = 别名映射 ?? 名;

            foreach (var 主题 in 取全部主题())
                if (主题.名称 == 名) return 主题;
            return null;
        }

        /// <summary> 读取 主题 目录下全部主题(内置每次按代码刷新,保证新字段同步;自定义主题文件请用别的名字) </summary>
        public static List<主题设置> 取全部主题()
        {
            Directory.CreateDirectory(主题目录);
            foreach (var (名称, 内置) in 内置主题)
            {
                string 文件 = Path.Combine(主题目录, $"{名称}.json");
                File.WriteAllText(文件, JsonSerializer.Serialize(内置, new JsonSerializerOptions { WriteIndented = true }));
            }

            var 结果 = new List<主题设置>();
            foreach (string 文件 in Directory.GetFiles(主题目录, "*.json"))
            {
                if (Path.GetFileName(文件) == "当前主题.json") continue;
                try
                {
                    var 主题 = JsonSerializer.Deserialize<主题设置>(File.ReadAllText(文件));
                    if (主题 != null && !string.IsNullOrWhiteSpace(主题.名称)) 结果.Add(主题);
                }
                catch { }
            }
            // 保证 暗/亮 一定存在(文件损坏时回退内置)
            foreach (var 内置 in 内置主题.Values)
                if (!结果.Any(x => x.名称 == 内置.名称)) 结果.Add(内置);
            return 结果;
        }

        /// <summary> 切换到指定主题并广播;找不到返回 false </summary>
        public static bool 应用(string 名称)
        {
            var 主题 = 按名称取(名称);
            if (主题 == null) return false;
            return 应用对象(主题, 持久化: true);
        }

        /// <summary> 直接用主题对象应用并广播(预览/自定义主题);持久化=false 时不写“当前主题.json” </summary>
        public static bool 应用对象(主题设置 主题, bool 持久化)
        {
            if (主题 == null) return false;
            当前主题 = 主题;
            if (持久化) 保存当前主题(主题.名称);
            主题变化?.Invoke(主题);
            return true;
        }

        public static void 保存当前主题(string 名称)
        {
            try
            {
                Directory.CreateDirectory(主题目录);
                File.WriteAllText(Path.Combine(主题目录, "当前主题.json"),
                    JsonSerializer.Serialize(new 当前主题记录 { 名称 = 名称 }));
            }
            catch { }
        }

        public static string 加载当前主题名()
        {
            try
            {
                string 文件 = Path.Combine(主题目录, "当前主题.json");
                if (File.Exists(文件))
                {
                    var 记录 = JsonSerializer.Deserialize<当前主题记录>(File.ReadAllText(文件));
                    if (记录 != null && !string.IsNullOrWhiteSpace(记录.名称)) return 记录.名称;
                }
            }
            catch { }
            return "亮";
        }

        private class 当前主题记录
        {
            public string 名称 { get; set; } = "亮";
        }
    }
}
