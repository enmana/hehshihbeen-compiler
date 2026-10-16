using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace hehshihbeen_compiler.选卡控件
{
    public abstract class BindableBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        protected void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    public class 选卡页视图模型 : BindableBase
    {
        private string? _标题;
        public string? 标题
        {
            get => _标题;
            set
            {
                if (!SetProperty(ref _标题, value)) return;
                OnPropertyChanged(nameof(显示标题));     // C21:标题变了,页头那一格跟着变
            }
        }

        /// <summary>
        /// C21(用户 2026-09-19:"在编辑框被更改后[页头]那加一个 `*` 星号表示被更改,保存后消失")。
        /// 写法拍板 = **`主程序.hb *`**(星号在文件名后面,中间一个空格)。
        /// 它就是**页头显示的那串字**:窗口标题栏与"另存为"建议名仍用 `标题`,别的地方不受影响。
        /// </summary>
        public string 显示标题 => 有未保存更改 ? $"{标题} *" : 标题 ?? "";

        private string? _内容;
        public string? 内容
        {
            get => _内容;
            set
            {
                if (!SetProperty(ref _内容, value)) return;
                OnPropertyChanged(nameof(有未保存更改));   // 改了内容 → 未保存状态可能变了
                OnPropertyChanged(nameof(显示标题));       // …页头的星号跟着变
                // H61-3:预览(临时)页**一改就固定**成普通页 —— 用户动过手的东西不再是"看一眼"。
                // 这一句同时兜住了"临时页被替换时丢掉改动":改过之后它已经不是临时页了,
                // 下一次预览换的是**别人**,不会把这一页的改动吞掉。
                if (_是临时页 && 有未保存更改) 是临时页 = false;
            }
        }
        private string? _全称;
        public string? 全称 { get => _全称; set => SetProperty(ref _全称, value); }

        private bool _是临时页;

        /// <summary>
        /// H61-3(VS 式预览标签页):这一页是**预览(临时)页** —— 导图双击跳过来时这样打开。
        /// 口径三条:
        ///   ① 页头标题**斜体**(与普通页分开,看标题就知道是"看一眼"的那种);
        ///   ② **同一时刻只留一个** —— 下一次预览由 `主视图模型.换临时页` 把它**整页换掉**(位置不变);
        ///   ③ **一编辑就固定**(见上面 `内容` 的 setter)、**再双击同一格 / 双击页头空白处也固定**。
        /// 固定 = 这一格改回 `false`,之后它就是一张普通标签页(关页照旧问未保存)。
        /// </summary>
        public bool 是临时页
        {
            get => _是临时页;
            set => SetProperty(ref _是临时页, value);
        }

        /// <summary>
        /// C20:这一页是**普通源码页**还是**汇编源码页**(`.hba`)。开页时按落点后缀定一次
        /// (`汇编源码C.判种类`),之后只用来选保存对话框的后缀/筛选器与提示口径 ——
        /// **保存、标脏、关页提示都走页上现成那一套**,不为汇编源码页另写一套。
        /// </summary>
        public 页面种类C 页种类 { get; set; } = 页面种类C.普通;

        /// <summary> 这一页是不是汇编源码页(C20;界面按它决定后缀、筛选器与几句提示) </summary>
        public bool 是汇编源码页 => 页种类 == 页面种类C.汇编源码;
        /// <summary> 该页最近一次打开/保存时写入磁盘的内容快照（null 表示尚无快照）</summary>
        private string? _已保存内容;

        /// <summary> 是否“选项”页(特殊固定页:不可关闭、不参与文件会话) </summary>
        public bool 是否选项页 { get; }

        /// <summary> 特殊页:不算"常规文件页",也不参与命令可用性里的"有内容"判断(目前只有选项页) </summary>
        public bool 是特殊页 => 是否选项页;

        /// <summary> 当前内容与磁盘快照不一致 => 有未保存的修改 </summary>
        public bool 有未保存更改
        {
            get
            {
                string 已保存 = _已保存内容 ?? "";
                string 当前 = _内容 ?? "";
                return !string.Equals(已保存, 当前, StringComparison.Ordinal);
            }
        }

        /// <summary> 打开文件或保存成功后，把当时写入磁盘的内容记为新快照 </summary>
        public void 标记已保存(string? 写入磁盘的内容)
        {
            _已保存内容 = 写入磁盘的内容;
            OnPropertyChanged(nameof(有未保存更改));
            OnPropertyChanged(nameof(显示标题));         // C21:保存后星号消失
        }

        /// <summary>
        /// C29:这一份内容**是不是刚"载入"进来的**(打开文件 / 新建页 / 装载工程)。
        /// 载入那一次**不进撤销栈** —— 编辑器把这面旗子读走就复位(`取走载入标记(要推的文本)`),
        /// 之后用户自己的动作(替换 / 格式化 / 改名 / 中英切换)照旧留一个撤销单元(C10 的成果不破)。
        ///
        /// 为什么不放在编辑器里:编辑器是从 `DataTemplate` 现造的,新开一页时**绑定先把它推过来**、
        /// 而载入那一刻界面还没造出编辑器来 —— 旗子只能由**页**带着,等编辑器来读。
        /// 旗子上记的是**当初载入的那份正文**(不是光一个真假):编辑器以后拿别的正文来推,
        /// 就不认这面旗子 —— 免得"打开一个空文件"(内容为空,绑定当场不变、不推)留下的旗子
        /// 把用户后面第一次编辑误判成载入。
        /// </summary>
        private string? _待载入正文;

        /// <summary> C29:标一句"下一次推到编辑器里的**这一份正文**算载入"(载入路径专用) </summary>
        public void 标记载入(string? 载入的正文) => _待载入正文 = 载入的正文 ?? "";

        /// <summary> C29:取走载入标记 —— 只有"要推的正是当初载入那一份"才算载入;不管命中与否都复位(只算一次) </summary>
        public bool 取走载入标记(string? 要推的文本)
        {
            bool 命中 = _待载入正文 != null && string.Equals(_待载入正文, 要推的文本 ?? "", StringComparison.Ordinal);
            _待载入正文 = null;
            return 命中;
        }

        /// <summary>
        /// H30:构造函数原先还收一格 `关闭命令`(由 `主视图模型` 的 `CloseTabCommand` 塞进来,存成
        /// `CloseCommand`)—— **XAML 从来没绑过它**,关页走的是页头那个 ✕ 的 `Click` 事件
        /// (`MainWindow.窗口与文件.cs` 的 `标签关闭按钮_Click` → `尝试关闭标签页`),那一格连同命令链一起撤了。
        /// </summary>
        public 选卡页视图模型(string? 标题, string? 内容, string? 全称, bool 是否选项页 = false)
        {
            // 直接写字段(不走属性 setter):构造函数里没人订属性通知,免得白发一遍"标题/内容变了"
            _标题 = 标题;
            _内容 = 内容;
            _全称 = 全称;
            this.是否选项页 = 是否选项页;

        }
    }
}
