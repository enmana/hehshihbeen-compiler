using System.Collections.ObjectModel;
using System.ComponentModel;

namespace hehshihbeen_compiler.选卡控件
{
    /// <summary>
    /// 标签页集合的视图模型。
    ///
    /// **H30(2026-09-24,清理"有壳没接线"的残留)**:这里原先挂着四处**全仓没人用**的壳,本批一并撤掉 ——
    /// ① `被操作页`(集合变化时写来写去,可**没有一处读它**)+ 只为它存在的 `CollectionChanged` 订阅;
    /// ② `AddTabCommand`(注释写"绑定到 + 按钮",可界面上**没有那个按钮**)与它调的无参 `添加()`;
    /// ③ `CloseTabCommand` / `选卡页视图模型.CloseCommand`(关页走的是页头那个 ✕ 的 `Click` 事件
    ///    = `MainWindow.窗口与文件.cs` 的 `标签关闭按钮_Click` → `尝试关闭标签页`,命令从来没绑过)
    ///    以及只为它存在的 `删除(object)`;
    /// ④ `修改 / 修改标题 / 修改内容`(没人调,报错串里还留着一个错别字"此处标题不能**位**null")。
    /// 另:那个"带标题 / 内容 / 全称"的构造重载也**没人调**(它一造出来就补一张页),一并撤掉。
    /// 放这两个死命令的 `选卡控件\中继命令.cs` 也跟着删了(它只服务它们,别处一处没引)。
    /// **留下的是真在用的那一套**:集合本身 + `选中页` + `添加(标题 / 内容 / 全称 / 种类)` +
    /// `确保选项页` + 选项页那几条判据。
    /// </summary>
    public class 主视图模型 : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        // 存放所有标签页的集合（UI会自动同步增删）
        public ObservableCollection<选卡页视图模型> 选卡页视图模型表 { get; set; }
        private 选卡页视图模型? _选中页 = null;
        public 选卡页视图模型? 选中页
        {
            get => _选中页;
            set
            {
                _选中页 = value;
                // 记得触发 PropertyChanged 通知
                OnPropertyChanged(nameof(选中页));
            }
        }

        /// <summary> 常驻“选项”页(若存在) </summary>
        public 选卡页视图模型? 选项页 => 选卡页视图模型表.FirstOrDefault(t => t.是否选项页);

        /// <summary> 常规(文件)页(排除选项页这种特殊页) </summary>
        public IEnumerable<选卡页视图模型> 常规页 =>
            选卡页视图模型表.Where(t => !t.是特殊页);

        /// <summary> H61-3:当下那个**预览(临时)页**(没有就是 null;同一时刻最多一个) </summary>
        public 选卡页视图模型? 临时页 => 常规页.FirstOrDefault(页 => 页.是临时页);

        /// <summary>
        /// H61-3:**预览打开** —— 用一份**新页**把当前那个预览页顶掉(没有预览页就按常规页追加在末尾)。
        /// 为什么是"整页换"而不是"就地改内容"三条:
        ///   ① 就地改内容会走 `内容` 的 setter,而那条 setter 带着"一改就固定"的口径(H61-3),新文本一推上去
        ///      预览页当场就变成普通页了;
        ///   ② 换的是**新页对象**,编辑器那套"换了页 = 载入、不进撤销栈"(`撤销边界C.算载入`)照旧生效,
        ///      从一个文件的撤销栈里撤出另一个文件的正文这种老 bug(C29)不会回来;
        ///   ③ 页对象换了,`是临时页` 这一格自然跟着新的来,不必给旧页做"复位"。
        /// **位置不变**:新页顶在旧页那一格上(标签页不会跳到最后去)。
        /// </summary>
        public 选卡页视图模型 换临时页(string 标题, string 内容, string 全称)
        {
            var 新页 = new 选卡页视图模型(标题, 内容, 全称) { 是临时页 = true };
            var 旧页 = 临时页;
            int 位置 = 旧页 == null ? -1 : 选卡页视图模型表.IndexOf(旧页);
            if (位置 < 0) { 加入常规页(新页); return 新页; }
            选卡页视图模型表[位置] = 新页;
            选中页 = 新页;
            return 新页;
        }

        public 主视图模型()
        {
            选卡页视图模型表 = [];
            // 注意:这里**故意不加**空白页 —— 没打开项目/文件时,编辑器区就是空的,不摆一个空标签页
        }

        /// <summary> 把新页插到"选项页"之前(没选项页则追加),保证选项页恒在最右 </summary>
        private void 加入常规页(选卡页视图模型 新页)
        {
            var 选项 = 选项页;
            int 插入位置 = 选项 != null ? 选卡页视图模型表.IndexOf(选项) : 选卡页视图模型表.Count;
            if (插入位置 < 0) 插入位置 = 选卡页视图模型表.Count;
            选卡页视图模型表.Insert(插入位置, 新页);
            选中页 = 新页;
        }

        public void 添加(string 标题, string 内容, string 全称)
        {
            var newTab = new 选卡页视图模型(标题, 内容, 全称);
            加入常规页(newTab);
        }

        /// <summary> C20:开一页汇编源码页(落点 = 项目目录里的 `.hba`;种类决定后缀与筛选器) </summary>
        public void 添加(string 标题, string 内容, string 全称, 页面种类C 页种类)
        {
            var newTab = new 选卡页视图模型(标题, 内容, 全称) { 页种类 = 页种类 };
            加入常规页(newTab);
        }

        /// <summary> 创建(或复用)常驻选项页 </summary>
        public void 确保选项页()
        {
            if (选项页 != null) return;
            var 选项 = new 选卡页视图模型("选项", "", null, 是否选项页: true);
            选卡页视图模型表.Add(选项);
            // 选回第一个常规页
            var 常规 = 常规页.FirstOrDefault();
            if (常规 != null) 选中页 = 常规;
        }
    }
}
