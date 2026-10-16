using System.Text;

namespace hehshihbeen_compiler
{
    /// <summary>
    /// 把"程序一个字节一个字节吐出来的输出"按 **UTF-8** 解成给人看的文字。
    ///
    /// 为什么需要它:VM 的 OUT 是**按字节**来的,而中文一个字是 3 个字节 ——
    /// 一个字节一个字节地 `UTF8.GetString` 会解出乱码(用户实测:打印"你好"变成 `������`)。
    /// 所以这里把半截的序列先缓冲着,够一个完整字符再吐出去;程序停的时候把没缓冲完的按 `?` 收尾。
    /// </summary>
    public sealed class 文字输出C
    {
        private readonly Action<string> _吐;
        private readonly List<byte> _待解 = [];

        public 文字输出C(Action<string> 吐) => _吐 = 吐;

        /// <summary> 收到一个字节(丢掉回车,换行原样给) </summary>
        public void 写(byte b)
        {
            if (b == (byte)'\r') return;
            if (b < 0x80)
            {
                先收尾();
                _吐(((char)b).ToString());
                return;
            }
            _待解.Add(b);
            if (_待解.Count == 1)
            {
                int 应有 = 该有几个字节(b);
                if (应有 < 0) { 乱码一个(); return; }                   // 孤零零的后续字节:算一个乱码
                if (应有 == 1) { 先收尾(); return; }
            }
            int 需要 = 该有几个字节(_待解[0]);
            if (需要 > 0 && _待解.Count >= 需要) 先收尾();
            else if (_待解.Count > 4) 乱码一个();                        // 缓冲太多了:肯定不是合法序列
        }

        /// <summary> 程序停下来了:把没缓冲完的字节收尾(免得最后半个字丢弃)</summary>
        public void 收尾()
        {
            if (_待解.Count == 0) return;
            乱码一个();
        }

        /// <summary> 这个字节开头的一个 UTF-8 序列有几个字节;-1 = 它不是开头(是后续字节)</summary>
        private static int 该有几个字节(byte b) => b switch
        {
            >= 0xC0 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF7 => 4,
            _ => -1
        };

        private void 先收尾()
        {
            if (_待解.Count == 0) return;
            _吐(Encoding.UTF8.GetString([.. _待解]));
            _待解.Clear();
        }

        private void 乱码一个()
        {
            _待解.Clear();
            _吐("?");
        }
    }
}
