/* ═══════════════════════════════════════════════════════════════════════════
   HB 纯 C 解释器核(R3-6-2)——“不生成任何 C 代码也能跑同一份机器码”的那一份。

   它是干什么的:`hbcc` 把一份编译结果写成**镜像文件**(机器码 + 初值表 + 错误表 +
   源码行表 + 内存大小),这个程序把镜像装进内存,用 `switch (操作码)` **取指-译码-执行**
   一条条跑完 —— 与 R3-6-4“逐条静态展开成 C 源码”那条路并列:两条路跑的是**同一份机器码**,
   验收判据也是同一条:**与 C# 版虚拟机对拍,寄存器 / 整块内存 / 输出逐字节一致**。

   为什么值得单开一条路(R3-6-2 的“为什么”):
     · **换核**:ISA 是冻结的 4 字节定长 99 条,机器码里没有任何 .NET 概念 ——
       换一个 C 版解释器就能跑同一份机器码,HB 源码 / 机器码 / 能力号一个字不改;
     · **不生成代码**:`工具\指令集真值源.txt`(R3-6-1)就是这份实现的**施工基准**,
       99 条一条不落地落在这个 `switch` 里(自检按真值源的操作码表逐条核对)。

   **语义只有一份**(这条是这一批的设计要点):本文件 `#include "运行时.c"`,
   内存模型 / 硬件栈 / 运行期错误与错误槽 / 宿主能力 16 条 / 状态转储**全部用那一份**。
   解释器与“转译出来的原生 exe”的差别**只在分派方式**:那边是每条指令一个 C 标签、
   跳转是 `goto`;这边是一个 `switch`。所以“两套实现漂开”的风险被压到最小。

   用法(镜像由 C# 侧 `C后端C.写镜像` 写出来;参数名与转译产物**故意一致**,对拍工具两头共用):
     解释器核 <镜像文件> [--hb-dump <状态文件>] [--hb-now <秒>] [--hb-now-ms <毫秒>]
                [--hb-图形 <文件>] [--hb-事件脚本 <文件>]
     · `--hb-dump`:停机 / 报错那一刻把整机状态转储到那个路径(只有自检对拍会给);
     · `--hb-now`:把 `当前时间()` 的钟冻在那一秒(两边吃同一个值才比得了整块内存);
     · `--hb-now-ms`(G3 域 2):把 `现在毫秒()` 的钟冻在那一毫秒(同一个道理,给界面 / 定时那一档对拍用);
     · `--hb-图形 <文件>`(R3-6-3):**挂上无头呈现**,绘制命令逐行写进那个文件
       (不写就走 stdout)。闸门那条对拍要的两样东西——程序输出与图形 dump——因此各走各的:
       程序输出照旧在 stdout,与 `C状态C.导出虚拟机状态` 收的那一份逐字节比;
       图形 dump 与 C# 版 `无头呈现C` 打的那几行**逐行比**。
     · `--hb-事件脚本 <文件>`(R3-6-3):按这份脚本按帧喂键盘 / 鼠标 / 关窗事件
       (格式与 `引擎\图形事件C.cs` 顶上那段说明同一份;不写 = 没有输入,`取事件` 一律给"没有事件")。

   几条口径(有意为之,别当疏漏):
     · **标识符一律 ASCII** —— 这段代码要过 clang / gcc / MSVC 三套编译器,中文只在注释与文案里;
     · **内存是一块零初始化的字节块**(`calloc`,在 `hb_boot` 里),机器码抄在 [0, 代码字节数),
       初值表随后写进去(与虚拟机 `装载` 同一个顺序);
     · **PC 跑到内存外面**照 `虚拟机C.单步` 开头那条护栏报错(说人话 + 查源码行);
     · **`停机` / `断点` 之后查错误槽**:槽里非 0 就按号找说明、打到 stderr 并以退出码 1 收场。
   ═══════════════════════════════════════════════════════════════════════════ */

/* 解释器没有“分派表”这回事(标签表 / switch(PC) 是转译产物的口径)——
   别跟着 `hb_boot` 打那句“C后端分派: …”,那是一句假话。 */
#define HB_不报分派 1

/* R3-6-3:这一份**带图形**(图形能力 60~69 + `现在毫秒` 55 + 无头呈现 + 事件脚本,都在 `运行时.c` 里)。
   转译产物那边**不带**(`#define` 缺省 = 0,还是老口径"独立 exe 没有窗口"给状态 1)——
   呈现层(真开窗)是 R3-6-5 的事,这一批只做无头这一档。 */
#define HB_有图形 1

/* R3-6-5:图形**呈现层**(Win32 / GDI,零依赖)也编进来 —— 只有 Windows 上编得出
   (Linux / macOS 的平台窗口排在后续批);而且要给 `--hb-窗口` 才真开窗,
   不给就还是无头那一档 ⇒ 自检与 38 个标准工程的对拍一个字节都不变。 */
#if defined(_WIN32)
#define HB_有窗口 1
#endif

/* 与转译产物**共用同一份**运行期:内存模型 / 栈 / 运行期错误 / 宿主能力 / 状态转储 */
#include "运行时.c"

/* ═══════════════════════════════════════════════════════════════════════════
   镜像文件格式(HBIMG2,小端;唯一事实源 = 本段与 `C后端\转译器C.cs` 的 `写镜像`,
   两处字段**逐条对齐**,改一处必须改另一处 —— 对拍第一遍就会把这个错抓出来)

     偏移   字节  内容
     0      8     魔数 `HBIMG2\0\0`
     8      4     版本号(本版 = 2)
     12     4     内存大小(字节)
     16     4     代码字节数
     20     4     数据字节数(代码段之后那块静态数据段)
     24     4     镜像非零项数 n_img
     28     4     初值表项数 n_init
     32     4     错误表项数 n_err
     36     4     源码行段项数 n_line
     40     4     错误槽地址(0 = 这份程序没有错误槽)
     44     …     代码段字节(代码字节数)
     随后依次(都按上面的条数):
       n_img  组:{ u32 地址; u8 值 }                       —— 代码段之后那些非零字节
       n_init 组:{ u32 地址; u32 宽度; u32 值 }             —— 顶层常量初值
       n_err  组:{ u32 错误号; u32 文本字节数; UTF-8 文本 } —— 错误号 → 说明(不带结尾 0)
       n_line 组:{ u32 地址; u32 源码行 }                   —— 地址 ≥ 它就算这一行
   ═══════════════════════════════════════════════════════════════════════════ */

#define HB_IMG_MAGIC0 'H'
#define HB_IMG_MAGIC1 'B'
#define HB_IMG_MAGIC2 'I'
#define HB_IMG_MAGIC3 'M'
#define HB_IMG_MAGIC4 'G'
#define HB_IMG_MAGIC5 '2'
#define HB_IMG_VERSION 2u

/* 读镜像时的游标(每一步都查边界:镜像坏了要报“镜像读不动”,别读进野内存) */
typedef struct hb_cursor
{
    const hb_u8 *p;
    hb_u32 n;
    hb_u32 at;
} hb_cursor;

static void hb_img_panic(const char *what)
{
    fprintf(stderr, "镜像读不动: %s\n", what);
    fflush(stderr);
    exit(2);
}

static void hb_need(hb_cursor *c, hb_u32 n, const char *what)
{
    if (c->at > c->n || n > c->n - c->at) hb_img_panic(what);
}

static hb_u32 hb_take_u32(hb_cursor *c, const char *what)
{
    hb_u32 v;
    hb_need(c, 4u, what);
    v = (hb_u32)c->p[c->at]
        | ((hb_u32)c->p[c->at + 1] << 8)
        | ((hb_u32)c->p[c->at + 2] << 16)
        | ((hb_u32)c->p[c->at + 3] << 24);
    c->at += 4u;
    return v;
}

static hb_u8 hb_take_u8(hb_cursor *c, const char *what)
{
    hb_u8 v;
    hb_need(c, 1u, what);
    v = c->p[c->at];
    c->at += 1u;
    return v;
}

static hb_u8 *hb_read_all(const char *path, hb_u32 *out_len)
{
    int 是宽 = 0;
    FILE *f = hb_宽开(path, "rb", &是宽);
    long size;
    hb_u8 *p;
    if (f == NULL)
    {
        fprintf(stderr, "镜像读不动: 打不开 %s\n", path);
        fflush(stderr);
        exit(2);
    }
    if (fseek(f, 0, SEEK_END) != 0 || (size = ftell(f)) < 0 || fseek(f, 0, SEEK_SET) != 0)
    {
        fclose(f);
        hb_img_panic("量不出文件大小(是不是个目录?)");
    }
    p = (hb_u8 *)malloc((size_t)size + 1u);
    if (p == NULL)
    {
        fclose(f);
        hb_img_panic("开不出读镜像用的缓冲区");
    }
    if (size > 0 && fread(p, 1, (size_t)size, f) != (size_t)size)
    {
        fclose(f);
        free(p);
        hb_img_panic("文件读到一半读不动了");
    }
    fclose(f);
    *out_len = (hb_u32)size;
    return p;
}

/* 装载一份镜像:解出各张表 → 交给 `hb_boot`(与转译产物同一处装载口径)。
   表都留在 malloc 出来的内存里、**活到程序结束**(hb_boot 只记指针,不复制)。 */
static void hb_load_image(const char *path, const char *dump_path)
{
    hb_u32 文件长 = 0;
    hb_u8 *文 = hb_read_all(path, &文件长);
    hb_cursor 游 = { 文, 文件长, 0u };
    hb_u32 版本, 内存大小, 代码字节数, 数据字节数, n_img, n_init, n_err, n_line, 错误槽;
    const hb_u8 *代码段;
    hb_u32 *img = NULL, *init = NULL;
    hb_err_msg *errs = NULL;
    hb_span *lines = NULL;
    hb_u32 i;

    if (文件长 < 44u) hb_img_panic("文件太短,连头都装不下(不是一份镜像?)");
    if (hb_take_u8(&游, "魔数") != (hb_u8)HB_IMG_MAGIC0
        || hb_take_u8(&游, "魔数") != (hb_u8)HB_IMG_MAGIC1
        || hb_take_u8(&游, "魔数") != (hb_u8)HB_IMG_MAGIC2
        || hb_take_u8(&游, "魔数") != (hb_u8)HB_IMG_MAGIC3
        || hb_take_u8(&游, "魔数") != (hb_u8)HB_IMG_MAGIC4
        || hb_take_u8(&游, "魔数") != (hb_u8)HB_IMG_MAGIC5
        || hb_take_u8(&游, "魔数") != 0u
        || hb_take_u8(&游, "魔数") != 0u)
        hb_img_panic("魔数不是 HBIMG2(这不是 C 后端写出来的镜像;旧版本请重新用 hbcc 写一次)");

    版本 = hb_take_u32(&游, "版本号");
    if (版本 != HB_IMG_VERSION)
        hb_img_panic("镜像版本号对不上(这份文件是另一个版本的 hbcc 写的:要么重新写一次镜像,要么换回那一版解释器)");

    内存大小 = hb_take_u32(&游, "内存大小");
    代码字节数 = hb_take_u32(&游, "代码字节数");
    数据字节数 = hb_take_u32(&游, "数据字节数");
    n_img = hb_take_u32(&游, "镜像项数");
    n_init = hb_take_u32(&游, "初值表项数");
    n_err = hb_take_u32(&游, "错误表项数");
    n_line = hb_take_u32(&游, "源码行段数");
    错误槽 = hb_take_u32(&游, "错误槽地址");

    if (内存大小 < 4u) hb_img_panic("内存大小小于 4 字节,装不下任何一条指令");
    if (代码字节数 % 4u != 0u) hb_img_panic("代码字节数不是 4 的倍数(指令定长 4 字节)");
    if (代码字节数 > 内存大小) hb_img_panic("机器码比内存还大(这份镜像自相矛盾)");

    代码段 = 游.p + 游.at;
    hb_need(&游, 代码字节数, "代码段字节");
    游.at += 代码字节数;

    if (n_img > 0u)
    {
        img = (hb_u32 *)malloc(sizeof(hb_u32) * 2u * (size_t)n_img);
        if (img == NULL) hb_img_panic("开不出镜像非零项表");
        for (i = 0; i < n_img; i++)
        {
            img[i * 2u + 0u] = hb_take_u32(&游, "镜像项地址");
            img[i * 2u + 1u] = (hb_u32)hb_take_u8(&游, "镜像项值");
        }
    }

    if (n_init > 0u)
    {
        init = (hb_u32 *)malloc(sizeof(hb_u32) * 3u * (size_t)n_init);
        if (init == NULL) hb_img_panic("开不出初值表");
        for (i = 0; i < n_init; i++)
        {
            init[i * 3u + 0u] = hb_take_u32(&游, "初值地址");
            init[i * 3u + 1u] = hb_take_u32(&游, "初值宽度");
            init[i * 3u + 2u] = hb_take_u32(&游, "初值");
        }
    }

    if (n_err > 0u)
    {
        /* 错误说明全是 UTF-8 字节、不带结尾 0;开一块**装得下剩下全部字节**的缓冲区,
           `hb_err_msg.msg` 指向块里各自那一段(`hb_boot` 只记指针,所以这块要活到程序结束)。 */
        hb_u32 剩 = 文件长 - 游.at;
        hb_u32 用 = 0;
        char *文本块 = (char *)malloc((size_t)剩 + (size_t)n_err + 1u);
        if (文本块 == NULL) hb_img_panic("开不出错误说明缓冲区");
        errs = (hb_err_msg *)malloc(sizeof(hb_err_msg) * (size_t)n_err);
        if (errs == NULL) hb_img_panic("开不出错误表");
        for (i = 0; i < n_err; i++)
        {
            hb_u32 长, k;
            const hb_u8 *文本;
            errs[i].no = hb_take_u32(&游, "错误号");
            长 = hb_take_u32(&游, "错误说明长度");
            文本 = 游.p + 游.at;
            hb_need(&游, 长, "错误说明文本");
            游.at += 长;
            for (k = 0; k < 长; k++) 文本块[用 + k] = (char)文本[k];
            文本块[用 + 长] = '\0';
            errs[i].msg = 文本块 + 用;
            用 += 长 + 1u;
        }
    }

    if (n_line > 0u)
    {
        lines = (hb_span *)malloc(sizeof(hb_span) * (size_t)n_line);
        if (lines == NULL) hb_img_panic("开不出源码行表");
        for (i = 0; i < n_line; i++)
        {
            lines[i].addr = hb_take_u32(&游, "行段地址");
            lines[i].line = hb_take_u32(&游, "行段行号");
        }
    }

    hb_boot(代码段, 代码字节数,
            img, n_img,
            init, n_init,
            错误槽, errs, n_err,
            lines, n_line,
            数据字节数,
            内存大小,
            dump_path);
}

/* ═══════════════════════════════════════════════════════════════════════════
   解释循环 —— 99 条一条不落(施工基准 `工具\指令集真值源.txt` 的操作码表)。
   每一条的语义与 `C后端\转译器C.cs` 的 `发一条` **逐句对应**(同一批 `hb_` 助手),
   所以“解释器”与“转译产物”这两条路读的是同一份语义,不是两份。

   一条铁律:**热循环里不许出现“顺手多做一点”的检查** —— 多一条就与虚拟机对不上,
   而对拍比的是“执行过的指令条数 / 最后执行的指令地址 / 整块内存”。
   ═══════════════════════════════════════════════════════════════════════════ */
static void hb_interpret(void)
{
    for (;;)
    {
        hb_u32 ins, op, rd, rs, rv, imm, simm, simm2, target;

        /* 与 `虚拟机C.单步` 开头那条护栏同一个口径:PC 已经装不下 4 字节就报错,
           别让程序“跑出内存”表现成正常跑完(R2-3 当初治的就是这个)。 */
        if (hb_pc > hb_mem_size - 4u)
            hb_failf(hb_pc, "程序跑到了内存外面(执行到地址 0x%X,内存只有 %u 字节)"
                " —— 检查跳转 / 返回的目标地址算错了没有", hb_pc, hb_mem_size);

        hb_到达(hb_pc);                              /* 记账:执行条数 + 最近路径(对拍要比它) */
        ins = hb_word_at(hb_pc);
        op = (ins >> 24) & 0xFFu;
        rd = (ins >> 20) & 0xFu;                     /* [23:20] 寄存器(各指令叫法不同) */
        rs = (ins >> 16) & 0xFu;                     /* [19:16] 源 / 下标寄存器 */
        rv = (ins >> 12) & 0xFu;                     /* [15:12] 值寄存器(三寄存器格式) */
        imm = ins & 0xFFFFFu;
        simm = (ins >> 12) & 0xFu;
        simm2 = (ins >> 8) & 0xFu;

        switch (op)
        {
            case 0x00:                                /* 停止 HALT */
                hb_stop();
                break;

            case 0x01:                                /* 加载 R = 内存[addr] */
                hb_r[rd] = hb_ld8(imm);
                break;
            case 0x02:                                /* 存储 内存[addr] = R */
                hb_st8(imm, hb_r[rd]);
                break;
            case 0x03:                                /* 加载立即数 R = imm(20 位) */
                hb_r[rd] = imm;
                break;
            case 0x04:                                /* 加法 */
                hb_r[rd] += hb_r[rs];
                break;
            case 0x05:                                /* 减法 */
                hb_r[rd] -= hb_r[rs];
                break;
            case 0x06:                                /* 跳转 PC = addr(24 位左移 2) */
                hb_pc = (ins & 0xFFFFFFu) << 2;
                continue;
            case 0x07:                                /* 为零跳转(低 20 位就是字节地址,不左移) */
                if (hb_r[rd] == 0u) { hb_pc = imm; continue; }
                break;
            case 0x08:                                /* 比较 PSW */
                hb_cmp(hb_r[rd], hb_r[rs]);
                break;
            case 0x09:                                /* 大于跳转 */
                if (hb_psw == 1u) { hb_pc = (ins & 0xFFFFFFu) << 2; continue; }
                break;
            case 0x0A:                                /* 小于跳转 */
                if (hb_psw == 2u) { hb_pc = (ins & 0xFFFFFFu) << 2; continue; }
                break;
            case 0x1B:                                /* 相等跳转 */
                if (hb_psw == 0u) { hb_pc = (ins & 0xFFFFFFu) << 2; continue; }
                break;
            case 0x1C:                                /* 不相等跳转 */
                if (hb_psw != 0u) { hb_pc = (ins & 0xFFFFFFu) << 2; continue; }
                break;
            case 0x0B:                                /* 入栈 */
                hb_push(hb_r[rd]);
                break;
            case 0x0C:                                /* 出栈 */
                hb_r[rd] = hb_pop();
                break;
            case 0x0D:                                /* 调用:压返回地址,PC = addr */
                hb_push(hb_pc + 4u);
                hb_pc = (ins & 0xFFFFFFu) << 2;
                continue;
            case 0x0E:                                /* 返回:PC = 弹出栈顶 */
                hb_pc = hb_pop();
                continue;
            case 0x0F:                                /* 按位异或 */
                hb_r[rd] ^= hb_r[rs];
                break;
            case 0x10:                                /* 与 */
                hb_r[rd] &= hb_r[rs];
                break;
            case 0x11:                                /* 或 */
                hb_r[rd] |= hb_r[rs];
                break;
            case 0x12:                                /* 左移(移位量取低 5 位) */
                hb_r[rd] = hb_r[rd] << (hb_r[rs] & 31u);
                break;
            case 0x13:                                /* 右移(逻辑) */
                hb_r[rd] = hb_r[rd] >> (hb_r[rs] & 31u);
                break;
            case 0x14:                                /* 加立即数 */
                hb_r[rd] += imm;
                break;
            case 0x15:                                /* 减立即数 */
                hb_r[rd] -= imm;
                break;
            case 0x16:                                /* 取地址 R = addr(20 位,常量) */
                hb_r[rd] = imm;
                break;
            case 0x17:                                /* 加载间接寻址 */
                hb_r[rd] = hb_ld8(hb_r[rs]);
                break;
            case 0x18:                                /* 存储间接寻址 */
                hb_st8(hb_r[rs], hb_r[rd]);
                break;
            case 0x19:                                /* 取反 */
                hb_r[rd] ^= 0xFFFFFFFFu;
                break;
            case 0x1A:                                /* 比较立即数 */
                hb_cmp(hb_r[rd], imm);
                break;
            case 0x1D:                                /* 复制 */
                hb_r[rd] = hb_r[rs];
                break;
            case 0x1E:                                /* 载入高位 R = imm20 << 12(低 12 位清零) */
                hb_r[rd] = imm << 12;
                break;
            case 0x1F:                                /* 或立即数(拼 32 位常量低位) */
                hb_r[rd] |= imm;
                break;
            case 0x20:                                /* 取字 R = 小端 内存[addr..addr+3] */
                hb_r[rd] = hb_ld32(imm);
                break;
            case 0x21:                                /* 存字 */
                hb_st32(imm, hb_r[rd]);
                break;
            case 0x22:                                /* 取字间接 */
                hb_r[rd] = hb_ld32(hb_r[rs]);
                break;
            case 0x23:                                /* 存字间接 */
                hb_st32(hb_r[rs], hb_r[rd]);
                break;
            case 0x24:                                /* 乘法(32 位无符号截断) */
                hb_r[rd] = hb_r[rd] * hb_r[rs];
                break;
            case 0x25:                                /* 除法(除零 = 运行期错误) */
                if (hb_r[rs] == 0u) hb_fail("除零(除数是 0 —— 先判断除数再除)");
                hb_r[rd] = hb_r[rd] / hb_r[rs];
                break;
            case 0x26:                                /* 取余(除零 = 运行期错误) */
                if (hb_r[rs] == 0u) hb_fail("除零(取余的除数是 0 —— 先判断除数再取余)");
                hb_r[rd] = hb_r[rd] % hb_r[rs];
                break;
            case 0x27:                                /* 读取字符(EOF / 没接输入 → 0xFFFFFFFF) */
                hb_r[rd] = hb_in();
                break;
            case 0x28:                                /* 输出字符(低字节) */
                hb_out((hb_u8)hb_r[rd]);
                break;
            case 0x29:                                /* 输出换行 */
                hb_newline();
                break;
            case 0x2A:                                /* 取字帧 R = 小端 内存[FP+off] */
                hb_r[rd] = hb_ld32(hb_r[15] + imm);
                break;
            case 0x2B:                                /* 存字帧 */
                hb_st32(hb_r[15] + imm, hb_r[rd]);
                break;
            case 0x2C:                                /* 取地址帧 R = FP + off */
                hb_r[rd] = hb_r[15] + imm;
                break;
            case 0x2D:                                /* 左移立即数(移固定位数) */
                hb_r[rd] = hb_r[rd] << (imm & 31u);
                break;
            case 0x2E:                                /* 与立即数(掩码) */
                hb_r[rd] &= imm;
                break;
            case 0x2F:                                /* 比较取值:第 4 个操作数是关系码 */
                hb_r[rd] = hb_cmp_set(hb_r[rd], hb_r[rs], simm);
                break;
            case 0x30:                                /* 断点:没有调试器,按停机走(与转译产物同口径) */
                hb_stop();
                break;
            case 0x31:                                /* 宿主调用:R 进 = 参数区地址,出 = 状态码 */
                hb_r[rd] = hb_host_call(hb_r[rd], imm);
                break;
            case 0x32:                                /* 取字索引:Rd = 内存[Rd + (Ri << 缩放)] */
                hb_r[rd] = hb_ld32(hb_r[rd] + (hb_r[rs] << simm));
                break;
            case 0x33:                                /* 存字索引:内存[Rb + (Ri << 缩放)] = Rs */
                hb_st32(hb_r[rd] + (hb_r[rs] << simm2), hb_r[rv]);
                break;
            case 0x34:                                /* 右移立即数(逻辑) */
                hb_r[rd] = hb_r[rd] >> (imm & 31u);
                break;
            case 0x35:                                /* 调用寄存器:压返回地址,PC = R */
                target = hb_r[rd];
                hb_call_reg(target, hb_pc + 4u);
                continue;
            case 0x36:                                /* 跳转寄存器:PC = R(不压返回地址) */
                target = hb_r[rd];
                hb_jmp_reg(target);
                continue;
            case 0x37:                                /* 为零跳转寄存器:Rd == 0 → PC = Rs(大地址时代的条件跳转) */
                if (hb_r[rd] == 0u)
                {
                    target = hb_r[rs];
                    hb_jmp_reg(target);
                    continue;
                }
                break;
            /* ---- 通用化(2026-10):硬件浮点(单精度)。语义在 `运行时.c` 的 hb_f*
                    那一族里,与 C# 侧 `虚拟机C.cs` 的 case 0x38~0x3F 逐条对齐。 ---- */
            case 0x38:                                /* 浮点加法 */
                hb_r[rd] = hb_fadd(hb_r[rd], hb_r[rs]);
                break;
            case 0x39:                                /* 浮点减法 */
                hb_r[rd] = hb_fsub(hb_r[rd], hb_r[rs]);
                break;
            case 0x3A:                                /* 浮点乘法 */
                hb_r[rd] = hb_fmul(hb_r[rd], hb_r[rs]);
                break;
            case 0x3B:                                /* 浮点除法(除零给 ±Inf / NaN,不报错) */
                hb_r[rd] = hb_fdiv(hb_r[rd], hb_r[rs]);
                break;
            case 0x3C:                                /* 浮点开方 */
                hb_r[rd] = hb_fsqrt(hb_r[rd]);
                break;
            case 0x3D:                                /* 浮点比较:Rd = (Rd 关系 Rs) ? 1 : 0(无序给 0) */
                hb_r[rd] = hb_fcmp(hb_r[rd], hb_r[rs], simm);
                break;
            case 0x3E:                                /* 浮点转整数(越界 / NaN 给 0x80000000) */
                hb_r[rd] = hb_f2i(hb_r[rd]);
                break;
            case 0x3F:                                /* 整数转浮点(按有符号读) */
                hb_r[rd] = hb_i2f(hb_r[rd]);
                break;
            /* ---- 通用化第二批(2026-10):整数扩展 9 条(0x40~0x47)。
                    语义在 `运行时.c` 的 hb_sdiv / hb_smod / hb_mulh / hb_clz … 那一族里,
                    与 C# 侧 `虚拟机C.cs` 的 case 0x40~0x47 逐条对齐。 ---- */
            case 0x40:                                /* 有符号除法(除零 = 运行期错误) */
                hb_r[rd] = hb_sdiv(hb_r[rd], hb_r[rs]);
                break;
            case 0x41:                                /* 有符号取余(除零 = 运行期错误) */
                hb_r[rd] = hb_smod(hb_r[rd], hb_r[rs]);
                break;
            case 0x42:                                /* 高位乘(无符号 64 位积的高 32 位) */
                hb_r[rd] = hb_mulh(hb_r[rd], hb_r[rs]);
                break;
            case 0x43:                                /* 前导零(0 给 32) */
                hb_r[rd] = hb_clz(hb_r[rd]);
                break;
            case 0x44:                                /* 尾随零(0 给 32) */
                hb_r[rd] = hb_ctz(hb_r[rd]);
                break;
            case 0x45:                                /* 数一位 */
                hb_r[rd] = hb_popcnt(hb_r[rd]);
                break;
            case 0x46:                                /* 符号扩展(低 imm 位) */
                hb_r[rd] = hb_sign_ext(hb_r[rd], imm);
                break;
            case 0x47:                                /* 零扩展(低 imm 位) */
                hb_r[rd] = hb_zero_ext(hb_r[rd], imm);
                break;

            /* ---- ISA 扩容 批 1:浮点补齐(0x48~0x4B;口径见 `ISA扩容落地台账.md` §6.5) ---- */
            case 0x48:                                /* 浮点最大值(非数传播) */
                hb_r[rd] = hb_fmax(hb_r[rd], hb_r[rs]);
                break;
            case 0x49:                                /* 浮点最小值(非数传播) */
                hb_r[rd] = hb_fmin(hb_r[rd], hb_r[rs]);
                break;
            case 0x4A:                                /* 浮点向下取整(朝 −∞;-0 保号) */
                hb_r[rd] = hb_floor(hb_r[rd]);
                break;
            case 0x4B:                                /* 浮点符号复制(大小取 rd、符号位取 rs) */
                hb_r[rd] = hb_copysign(hb_r[rd], hb_r[rs]);
                break;

            /* ---- ISA 扩容 批 2:位与字节(0x4C~0x53) ---- */
            case 0x4C:                                /* 双字乘高 */
                hb_r[rd] = hb_umulh(hb_r[rd], hb_r[rs]);
                break;
            case 0x4D:                                /* 双字乘加 */
                hb_r[rd] = hb_umadd(hb_r[rd], hb_r[rs]);
                break;
            case 0x4E:                                /* 循环左移 */
                hb_r[rd] = hb_rol(hb_r[rd], hb_r[rs]);
                break;
            case 0x4F:                                /* 循环右移 */
                hb_r[rd] = hb_ror(hb_r[rd], hb_r[rs]);
                break;
            case 0x50:                                /* 字节交换 */
                hb_r[rd] = hb_bswap(hb_r[rd]);
                break;
            case 0x51:                                /* 取字节(n = 0~3,闸在汇编器) */
                hb_r[rd] = hb_getbyte(hb_r[rs], simm);
                break;
            case 0x52:                                /* 取整数绝对值 */
                hb_r[rd] = hb_iabs(hb_r[rd]);
                break;
            case 0x53:                                /* 取地址索引(字段照 0x33:[23:20] 基 [19:16] 下标 [15:12] 目标) */
                hb_r[(ins >> 12) & 0xFu] = hb_r[(ins >> 20) & 0xFu] + (hb_r[(ins >> 16) & 0xFu] << simm2);
                break;
            /* ---- ISA 扩容 批 3 + 批 4(0x54~0x62) ---- */
            case 0x54:                                /* 取半字索引(读 2 字节零扩展) */
                hb_r[(ins >> 12) & 0xFu] = hb_ld32(hb_r[(ins >> 20) & 0xFu] + (hb_r[(ins >> 16) & 0xFu] << simm2)) & 0xFFFFu;
                break;
            case 0x55:                                /* 存半字索引(写 2 字节:读改写,别把高 2 字节写成 0) */
            {
                uint32_t 址 = hb_r[(ins >> 20) & 0xFu] + (hb_r[(ins >> 16) & 0xFu] << simm2);
                hb_st32(址, (hb_ld32(址) & 0xFFFF0000u) | (hb_r[(ins >> 12) & 0xFu] & 0xFFFFu));
            }
            break;
            case 0x56:                                /* 存字节索引(写 1 字节:读改写) */
                {
                    uint32_t 址 = hb_r[(ins >> 20) & 0xFu] + (hb_r[(ins >> 16) & 0xFu] << simm2);
                    hb_st32(址, (hb_ld32(址) & 0xFFFFFF00u) | (hb_r[(ins >> 12) & 0xFu] & 0xFFu));
                }
                break;
            case 0x57:                                /* 内存拷贝(Rb 起 Ri 个字节 → Rs 起) */
                {
                    uint32_t k, 长 = hb_r[(ins >> 16) & 0xFu], 起 = hb_r[(ins >> 20) & 0xFu], 到 = hb_r[(ins >> 12) & 0xFu];
                    for (k = 0; k < 长; k++) hb_mem[到 + k] = hb_mem[起 + k];
                }
                break;
            case 0x58:                                /* 立即数范围比较(下界 <= 值 <= 上界 ⇒ 0,否则 2) */
                hb_psw = (hb_r[(ins >> 12) & 0xFu] >= hb_r[(ins >> 20) & 0xFu] && hb_r[(ins >> 12) & 0xFu] <= hb_r[(ins >> 16) & 0xFu]) ? 0u : 2u;
                break;
            case 0x59:                                /* 条件移动(rd = 成立 ? ri : rs);关系码在 [11:8] = simm2 */
                hb_r[rd] = hb_rel(simm2, hb_r[(ins >> 16) & 0xFu], hb_r[(ins >> 12) & 0xFu]) ? hb_r[(ins >> 16) & 0xFu] : hb_r[(ins >> 12) & 0xFu];
                break;
            case 0x5A:                                /* 比较并条件移动(成立才搬);关系码同上 */
                if (hb_rel(simm2, hb_r[(ins >> 16) & 0xFu], hb_r[(ins >> 12) & 0xFu])) hb_r[rd] = hb_r[(ins >> 16) & 0xFu];
                break;
            case 0x5B:                                /* 条件置位(PSW = 2 给 0,其余给 1) */
                hb_r[rd] = (hb_psw == 2u) ? 0u : 1u;
                break;
            case 0x5C:                                /* 大于等于跳转(PSW 0 或 1) */
                if (hb_psw != 2u) { hb_pc = (ins & 0xFFFFFFu) << 2; continue; }
                break;
            case 0x5D:                                /* 小于等于跳转(PSW 0 或 2) */
                if (hb_psw == 0u || hb_psw == 2u) { hb_pc = (ins & 0xFFFFFFu) << 2; continue; }
                break;
            case 0x5E:                                /* 尾调用跳转(不压返回地址) */
                hb_pc = (ins & 0xFFFFFFu) << 2;
                continue;
            case 0x5F:                                /* 饱和加 */
                hb_r[rd] = hb_sadd(hb_r[rd], hb_r[rs]);
                break;
            case 0x60:                                /* 无操作 */
                break;
            case 0x61:                                /* 带弹栈返回(先放掉立即数那些字节,再返回) */
                {
                    uint32_t 丢;
                    for (丢 = 0; 丢 < imm; 丢 += 4u) (void)hb_pop();
                    hb_pc = hb_pop();
                    continue;
                }
            case 0x62:                                /* 取返回地址(读栈顶不弹) */
                {
                    uint32_t 顶 = hb_pop();
                    hb_r[rd] = 顶;
                    hb_push(顶);
                }
                break;
            default:
                hb_failf(hb_pc, "未知操作码: 0x%02X(PC=0x%X)—— 机器码坏了,或者不是这一版指令集"
                    "(ISA 76 条,0x00~0x4B)", op, hb_pc);
                break;
        }

        hb_pc += 4u;                              /* 没跳转就前进到下一条 */
    }
}

/* ═══════════════════════════════════════════════════════════════════════════
   镜像文件格式(HBIMG2,小端;唯一事实源 = 本段与 `C后端\转译器C.cs` 的 `写镜像`,
   —— 这一段在文件顶上,这里是它下面接着的"用法"那一段)
   ═══════════════════════════════════════════════════════════════════════════ */

int main(int argc, char **argv)
{
    const char *image_path = NULL;
    hb_开关 开关;

    /* 先把命令行参数拿成 UTF-8 字节(Windows 上这一句是**必须的**,见 `hb_取参数`) */
    {
        int 个数 = 0;
        char **参数 = hb_取参数(argc, argv, &个数);
        if (参数 != NULL) { argc = 个数; argv = 参数; }
    }

    /* R3-6-5 批 2:开关解析与转译产物**共用一份**(见 `运行时.c` 的 `hb_取开关`) */
    if (hb_取开关(argc, argv, HB_有图形, &开关) != 0) { hb_用法(argv[0], HB_有图形); return 2; }
    if (argc < 2 || argv[1][0] == '-') { hb_用法(argv[0], HB_有图形); return 2; }
    image_path = argv[1];

    /* R3-6-3:无头呈现 + 事件脚本(在装载之前挂 —— 事件脚本要在第 0 帧就能投得出来)。
       R3-6-5 批 2:这一整段收进 `运行时.c` 的 `hb_gfx_装配`(转译产物那条 `main` 共用同一句)。 */
#if HB_有图形
    if (hb_gfx_装配(&开关) != 0) return 2;
#else
    if (开关.图形文件 != NULL || 开关.脚本文本 != NULL || 开关.要窗口)
    {
        fprintf(stderr, "这一份解释器核编的时候没带图形(没定义 HB_有图形)\n");
        fflush(stderr);
        return 2;
    }
#endif

    hb_load_image(image_path, 开关.状态文件);
    hb_interpret();
    return 0;                                     /* 到不了:`hb_stop` / `hb_fail` 自己收场 */
}
