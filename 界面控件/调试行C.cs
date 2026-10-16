using System.ComponentModel;

namespace hehshihbeen_compiler.界面控件
{
    /// <summary>
    /// 主窗口底部"变量"页的一行(名字 / 值 / 类型 / 在哪 / 现在在哪个寄存器里)—— 就像 VS 的变量窗口。
    /// 属性改动会通知界面,所以刷新是**原地改值**:不重建列表(不闪、滚动位置不跳)。
    /// </summary>
    public sealed class 变量行C : INotifyPropertyChanged
    {
        private string _名字 = "", _值 = "", _类型 = "", _在哪 = "", _寄存器槽 = "";
        public string 名字 { get => _名字; set { _名字 = value; 变了(nameof(名字)); } }
        public string 值 { get => _值; set { _值 = value; 变了(nameof(值)); } }
        public string 类型 { get => _类型; set { _类型 = value; 变了(nameof(类型)); } }
        public string 在哪 { get => _在哪; set { _在哪 = value; 变了(nameof(在哪)); } }
        /// <summary> 这一格变量的值现在落在哪个寄存器槽里(名字叫「槽」是为了别与"机器那排寄存器"混成一个词)</summary>
        public string 寄存器槽 { get => _寄存器槽; set { _寄存器槽 = value; 变了(nameof(寄存器槽)); } }

        /// <summary> 原地改这一行的内容(名字没变时走这条路,列表不重建)</summary>
        public void 设为(变量行C 别处)
        {
            值 = 别处.值; 类型 = 别处.类型; 在哪 = 别处.在哪; 寄存器槽 = 别处.寄存器槽;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void 变了(string 名) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(名));
    }

    /// <summary>
    /// 主窗口底部"监视"页的一行(P35):把"我要盯的那条表达式"写下来,每走一步自动重算。
    /// 与变量页同一套做法:**原地改值**(不重建列表,不闪、滚动位置不跳)。
    /// </summary>
    public sealed class 监视行C : INotifyPropertyChanged
    {
        private string _表达式 = "", _值 = "", _类型 = "", _历史 = "", _说明 = "";
        public string 表达式 { get => _表达式; set { _表达式 = value; 变了(nameof(表达式)); } }
        public string 值 { get => _值; set { _值 = value; 变了(nameof(值)); } }
        public string 类型 { get => _类型; set { _类型 = value; 变了(nameof(类型)); } }
        /// <summary> 变过的样子(`1 → 2 → 3`);只在值真的变了的时候记一条 </summary>
        public string 历史 { get => _历史; set { _历史 = value; 变了(nameof(历史)); } }
        public string 说明 { get => _说明; set { _说明 = value; 变了(nameof(说明)); } }

        /// <summary> 原地改这一行的内容(表达式没变时走这条路,列表不重建)</summary>
        public void 设为(监视行C 别处)
        {
            值 = 别处.值; 类型 = 别处.类型; 历史 = 别处.历史; 说明 = 别处.说明;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void 变了(string 名) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(名));
    }

    /// <summary> 主窗口底部"调试"页里寄存器表的一行:这一格现在装着谁的值(认不出来就空着,不写没用的话)</summary>
    public sealed class 寄存器行C : INotifyPropertyChanged
    {
        private string _寄存器 = "", _值 = "", _谁的 = "";
        public string 寄存器 { get => _寄存器; set { _寄存器 = value; 变了(nameof(寄存器)); } }
        public string 值 { get => _值; set { _值 = value; 变了(nameof(值)); } }
        public string 谁的 { get => _谁的; set { _谁的 = value; 变了(nameof(谁的)); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void 变了(string 名) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(名));
    }
}
