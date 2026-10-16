using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace hehshihbeen_compiler
{
    /// <summary> 中间层展开器的一部分:K7 模块(名字前缀)与 `私有`。(第88批从 `中间层C.cs` 按 region 原样搬出,逻辑一字未改) </summary>
    internal partial class 中间层展开器
    {
        #region K7 模块(名字前缀)与 私有
        /// <summary>
        /// 把裸名字补成模块里的全名(`求和` → `数学.求和`)。**带点的名字一律原样** ——
        /// `别的模块.函数` / `容器.成员` 是"已经限定好了"的,再补前缀就找不着了。
        /// </summary>
        private string 限定名(string 裸名) =>
            _当前模块.Length > 0 && !裸名.Contains('.') ? _当前模块 + "." + 裸名 : 裸名;

        /// <summary> 全名属于哪个模块(第一个点前面那一段);没有模块前缀返回 "" </summary>
        private static string 名字的模块(string 全名)
        {
            int 点 = 全名.IndexOf('.');
            return 点 > 0 ? 全名[..点] : "";
        }

        /// <summary>
        /// 顶层声明(不在函数里)在模块里要带前缀;函数里的参数/局部**不带** ——
        /// 它们本来就只在那个函数里可见,带上前缀反而会让调试名字表看起来奇怪。
        /// </summary>
        private string 声明注册名(string 裸名) => _当前函数 == null ? 限定名(裸名) : 裸名;

        /// <summary> 这个名字标了 `私有` 吗?(容器/模块前缀标了私有,它里面的成员跟着私有) </summary>
        private bool 是私有(string 全名)
        {
            if (_私有名.Count == 0) return false;
            if (_私有名.Contains(全名)) return true;
            foreach (string p in _私有名)
                if (全名.Length > p.Length && 全名.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary> 这个名字标了 `程序集` 吗?(容器/模块前缀标了程序集,它里面的成员跟着算) </summary>
        private bool 是程序集(string 全名)
        {
            if (_程序集名.Count == 0) return false;
            if (_程序集名.Contains(全名)) return true;
            foreach (string p in _程序集名)
                if (全名.Length > p.Length && 全名.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// 标了 `程序集` 的这个名字是**哪个工程**声明的。成员跟着前置容器算时(容器标了程序集,
        /// `模块.成员` 本身没标),取那个容器的声明工程 —— 不然成员会成了"谁的工程都不是"躲过检查。
        /// </summary>
        private string 程序集声明工程(string 全名)
        {
            if (_声明工程.TryGetValue(全名, out string? 处)) return 处 ?? "";
            foreach (string p in _程序集名)
                if (全名.Length > p.Length && 全名.StartsWith(p + ".", StringComparison.OrdinalIgnoreCase)
                    && _声明工程.TryGetValue(p, out string? 前缀处)) return 前缀处 ?? "";
            return "";
        }

        /// <summary>
        /// 引用**别的模块**的私有成员 → 当场报错(用户点名要的"库的内部数据不暴露")。
        /// 同一个模块里用自己人当然放行 —— 不然私有就没法用了。
        /// 紧接着还有一档 `程序集`:引用**别的 `.hbp` 工程**的程序集成员 → 同样当场报错。
        /// </summary>
        private void 校验可取用(string 全名, int 行号)
        {
            if (是私有(全名))
            {
                string 属 = 名字的模块(全名);
                if (string.Equals(属, _当前模块, StringComparison.OrdinalIgnoreCase)) return;
                throw 报错(行号, $"'{全名}' 是模块 '{属}' 的私有成员(声明时写了 `私有`),只能在模块内部使用。" +
                    $"办法:①确实要公开,就把声明前面的 `私有` 去掉;②在本模块里加一个公开的函数把它取出来",
                    $"'{全名}' is private to module '{属}'");
            }
            if (!是程序集(全名)) return;
            string 声明工程 = 程序集声明工程(全名);
            if (声明工程.Length == 0 || string.Equals(声明工程, _当前工程, StringComparison.OrdinalIgnoreCase)) return;
            throw 报错(行号, $"'{全名}' 是工程 '{声明工程}' 的 `程序集` 成员(声明时写了 `程序集`),只有在同一个工程里能用。" +
                $"办法:①确实要给别人用,就把声明前面的 `程序集` 去掉;②在本工程里加一个公开的函数把它取出来",
                $"'{全名}' is internal to project '{声明工程}' (declared with `internal`)");
        }

        /// <summary>
        /// 记下"这一句的声明写了可见性前缀":`私有` → `_私有名`,`程序集` → `_程序集名` + 声明工程。
        /// 标记由这个函数读走之后清掉(声明处理函数统一调它,不必自己判前缀)。
        /// </summary>
        private void 记私有(string 全名)
        {
            if (!_本句私有 && !_本句程序集) return;
            if (_本句私有) _私有名.Add(全名);
            if (_本句程序集)
            {
                _程序集名.Add(全名);
                _声明工程[全名] = _当前工程;      // `程序集` 的边界靠"声明工程 ≠ 当前工程"判
            }
            _本句私有 = false;
            _本句程序集 = false;
        }

        /// <summary> 第 `序` 条伪行属于哪个工程(归属表空/越界 → 空串 = 匿名工程,与 `公共` 等价;见 `_工程归属`) </summary>
        private string 文件工程(int 序)
        {
            int 号 = 序 >= 0 && 序 < _伪行文件表.Count ? _伪行文件表[序] : -1;
            return _工程归属 is { } 归属 && 号 >= 0 && 号 < 归属.Count ? 归属[号] : "";
        }

        /// <summary> 记下"这个名字是工程 `工程` 的 `程序集` 成员"(预扫收函数档位用;声明那条路走 `记私有`) </summary>
        private void 记程序集档(string 全名, string 工程)
        {
            _程序集名.Add(全名);
            _声明工程[全名] = 工程;
        }

        /// <summary>
        /// C19:把一个**顶层名字**的可见性记进调试名字表(函数 / 结构 / 方法容器 / 全局变量各声明处顺手调一次)。
        /// 判据直接复用本文件上面那三个函数(`是私有` / `是程序集` / `程序集声明工程`)——
        /// **与 `校验可取用` 同一份判据**,不另写一套:补全那边照这张表筛,就不会出现"补出来能写、编译报错"。
        /// 只是旁路信息,不参与汇编、不影响机器码。
        /// </summary>
        private void 记可见性(string 全名)
        {
            if (全名.Length == 0) return;
            _名字表.可见性[全名] = new 可见性项C
            {
                私有 = 是私有(全名),
                程序集 = 是程序集(全名),
                声明模块 = 名字的模块(全名),
                声明工程 = 程序集声明工程(全名) is { Length: > 0 } 处 ? 处 : _当前工程,
                声明文件 = _当前文件,
            };
            记文件归属();
        }

        /// <summary>
        /// C19:记下"正在处理的那条语句属于哪个文件、哪个模块、哪个工程"(补全要判"当前文件在哪个模块/工程里")。
        /// 每条语句都记一遍,同一个文件的值不变 —— 覆盖写,不占内存,也不影响别的表。
        /// </summary>
        private void 记文件归属()
        {
            // 单文件编译没有文件名(全是 ""):照样记一条 —— 那种程序只有一个模块,用得上
            _名字表.文件模块[_当前文件] = _当前模块;
            _名字表.文件工程[_当前文件] = _当前工程;
            if (_当前模块.Length > 0 && !_名字表.模块名.Contains(_当前模块, StringComparer.OrdinalIgnoreCase))
                _名字表.模块名.Add(_当前模块);
        }

        /// <summary>
        /// 结构名 → 结构类型:模块里写裸名字(`点`)先看本模块的 `模块.点`,再看全局;外面写全名(`数学.点`)直接命中。
        /// </summary>
        private 结构类型C? 查结构类型(string 名词)
        {
            // H68-3 元组:类型位置上的一对方括号(`[整数, 整数]`)现造一个结构类型 ——
            // 元组不进 `_结构表`(它不是 `结构` 声明),所以这一条要放在最前面。
            if (是元组类型词(名词)) return 试取元组类型(名词);
            if (_结构表.TryGetValue(名词, out var 结构)) { 校验可取用(名词, _当前源行号); return 结构; }
            if (_当前模块.Length > 0 && !名词.Contains('.')
                && _结构表.TryGetValue(_当前模块 + "." + 名词, out var 本模块结构)) return 本模块结构;   // 模块自己人
            return null;
        }

        /// <summary> 这个名字(裸名或全名)是不是一个已声明的结构类型 </summary>
        private bool 是结构名(string 名词) => 查结构类型(名词) != null;

        /// <summary>
        /// 去掉声明前面的可见性前缀(`私有`/`private`、`程序集`/`internal`、`公共`/`public`;
        /// 预扫与正式处理都要用同一套)。`公共` 是 H2 起的**显式写法** —— 去掉之后与不写走同一条路。
        /// </summary>
        private string 去可见性前缀(string 行)
        {
            while (true)
            {
                string 首词 = 取首词(行);
                string? 词 = 中间层C.到英文关键字(首词);
                if (词 is not ("private" or "internal" or "public")) return 行;
                行 = 行[首词.Length..].Trim();
            }
        }

        /// <summary>
        /// A4(第103批):**结构字段**的可见性检查 —— 与顶层声明同一套口径:
        /// `私有` 字段只有声明它的**模块**能用;`程序集` 字段只有声明它的**工程**能用。
        /// 站得住就返回,站不住当场报错并给改法(口径与 `校验可取用` 一致)。
        /// </summary>
        private void 校验字段可取用(结构类型C 结构, 结构字段C 字段, int 行号)
        {
            if (!字段.有可见性) return;
            string 字段全名 = $"{结构.名称}.{字段.名称}";
            if (字段.私有)
            {
                string 属 = 名字的模块(结构.名称);
                if (string.Equals(属, _当前模块, StringComparison.OrdinalIgnoreCase)) return;
                if (属.Length == 0)
                    throw 报错(行号,
                        $"'{字段全名}' 是结构 '{结构.名称}' 的**私有字段**(声明时写了 `私有`),只有**跟它同在一个模块**的地方能用;" +
                        $"这个结构所在的文件没写 `模块`,所以只有同样没写 `模块` 的文件能碰。" +
                        $"办法:①确实要给外面用,就把字段前面的 `私有` 去掉;②在声明它的文件里加一个公开的函数把值取出来;" +
                        $"③想让这个私有字段连「没有模块的文件」也挡住,就给那个文件第一条加一行 `模块 名字`",
                        $"field '{字段全名}' is private (its file has no `module`)");
                throw 报错(行号,
                    $"'{字段全名}' 是结构 '{结构.名称}' 的**私有字段**(声明时写了 `私有`),只有声明它的模块 '{属}' 里能用。" +
                    $"办法:①确实要给外面用,就把字段前面的 `私有` 去掉;②在模块 '{属}' 里加一个公开的函数/方法把值取出来(或存进去)",
                    $"field '{字段全名}' is private to module '{属}'");
            }
            string 声明工程 = 程序集声明工程(结构.名称);
            if (声明工程.Length == 0 || string.Equals(声明工程, _当前工程, StringComparison.OrdinalIgnoreCase)) return;
            throw 报错(行号,
                $"'{字段全名}' 是结构 '{结构.名称}' 的 `程序集` 字段(声明时写了 `程序集`),只有在同一个工程里能用。" +
                $"办法:①确实要给别的工程用,就把字段前面的 `程序集` 去掉;②在工程 '{声明工程}' 里加一个公开的函数把它取出来",
                $"field '{字段全名}' is internal to project '{声明工程}'");
        }

        /// <summary>
        /// 把这个结构里**所有**带可见性前缀的字段都验一遍(第一个站不住的报错)。
        /// 用在一处"不点名字、却按字段顺序整块读写"的地方:结构的字面量初值 `点 位置 = [1, 2];`
        /// —— 那种写法等于隔着字段名碰了每一格,不能绕过可见性。
        /// </summary>
        private void 校验全字段可取用(结构类型C 结构, int 行号)
        {
            foreach (var 字段 in 结构.字段) 校验字段可取用(结构, 字段, 行号);
        }

        /// <summary>
        /// 去掉声明前面的可见性前缀,并告诉调用方取走了哪几个(结构字段声明用 —— 顶层那条路走 `去可见性前缀`)。
        /// `私有`/`private` → 私有;`程序集`/`internal` → 程序集;`公共`/`public`(H2)只是**显式写出"公开"**,
        /// 去掉就完了、不记档位(与不写完全同义);两个都写也收(与顶层同一套词)。
        /// </summary>
        private static string 去可见性前缀带标记(string 行, out bool 私有, out bool 程序集)
        {
            私有 = false; 程序集 = false;
            while (true)
            {
                string 首词 = 取首词(行);
                string? 词 = 中间层C.到英文关键字(首词);
                if (词 is not ("private" or "internal" or "public")) return 行;
                if (词 == "private") 私有 = true;
                else if (词 == "internal") 程序集 = true;
                行 = 行[首词.Length..].Trim();
            }
        }

        /// <summary> 结构字段名字表那一份(调试/编辑页用):带上可见性标记 </summary>
        private static 字段项C 字段项(结构字段C f) => new()
        {
            名字 = f.名称, 类型名 = f.类型名, 宽度 = f.宽度, 偏移 = f.偏移,
            是结构 = f.是结构, 是数组 = f.是数组, 元素宽 = f.是列表 ? f.列表元素宽 : f.元素宽, 元素个数 = f.元素个数,
            是字符串 = f.是字符串, 元素是字符串 = f.元素是字符串, 是列表 = f.是列表,
            私有 = f.私有, 程序集 = f.程序集,
        };

        /// <summary> 记下"这个顶层名字是在哪声明的"(工程级编译带文件名;单文件只说行号) </summary>
        private void 记声明出处(string 全名, int 行号)
        {
            if (全名.Length == 0) return;
            _声明出处[全名] = (_当前文件, 行号);
        }

        /// <summary> "上一处声明在哪" —— 重复声明的报错要把这一句带上(报出两处位置才找得到) </summary>
        private string 上一处声明(string 全名)
        {
            if (!_声明出处.TryGetValue(全名, out var 处)) return "";
            string 文件 = _工程文件表.Count > 0 && 处.文件.Length > 0 ? $"{Path.GetFileName(处.文件)} 第 {处.行} 行" : $"第 {处.行} 行";
            return $"{文件}已经有一个同名的;";
        }

        /// <summary> 重复声明的统一改法(D2:同一个工程里名字必须唯一;要同名就用 `模块` 把名字圈起来) </summary>
        private static string 同名怎么办(string 种类) =>
            $"同一个工程里{种类}名要唯一:两个文件各想要一个同名的,就给其中一个文件的**第一条**写一行 " +
            $"`模块 名字`(模块里的名字自动带 `名字.` 前缀,就不会撞),或者改个名;";
        #endregion
    }
}
