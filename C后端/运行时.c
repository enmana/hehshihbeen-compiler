/* ═══════════════════════════════════════════════════════════════════════════
   HB 运行期(R3-6-4 批 1;R3-6-3 起还带**图形能力 17~26 + 无头呈现**的那一档)——
   把 HB 虚拟机那套语义搬到 C 里,好让 hbcc 把机器码编成**不带 .NET** 的原生 exe。

   这份是 `hbcc` 的 C 后端拼在生成代码**前面**的固定一段:**手写、进版本库、单一事实源**。
   语义口径与 `引擎\虚拟机C.cs` 一字不差(内存模型 / 全部指令 / 停机 / 运行期错误),
   改这边就必须回头看那边,别让两份实现漂开。

   **两条 C 路共用它**:R3-6-4 的转译产物(每条指令一个 C 标签 + `goto`)与 R3-6-2 的
   解释器核(`switch(操作码)`)—— 差别只在分派方式,别的全在这一份里。
   图形那一族(R3-6-3)默认**不带**(`HB_有图形` = 0,独立 exe 没窗口,与 C# 那边"没挂呈现口"
   同口径给状态 1);解释器核自己 `#define HB_有图形 1`,于是它是唯一的"带图形的 C 宿主"
   (真窗口那一档是 R3-6-5 的事)。

   几条口径(有意为之,别当疏漏):
     · **标识符一律 ASCII** —— 这段代码要过 clang / gcc / MSVC 三套编译器,
       中文标识符在 MSVC 上要靠 `/utf-8` 兜,不如干脆不用;中文只在**注释与报错文案**里。
     · **内存是一个零初始化的字节块**(`calloc`),机器码抄在 [0, 代码字节数),
       顶层常量初值随后写进去(与 `装载` 同一个顺序:初值不会被机器码盖掉)。
     · **SP 从内存末尾往下长**,`R[16]`、PC、PSW(0=等于 1=大于 2=小于)与虚拟机同口径。
     · **停机之后查错误槽**:槽里非 0 就按号找说明,打到 stderr 并以退出码 1 收场
       (与 hbcc `--run` 那条"D2 口径"一致:这次运行算没通过)。
     · **stdout 只在重定向时切二进制**:终端上留文本模式(换行才能正确回车),
       重定向出去时逐字节原样 —— 对拍要的是后者。
     · **路径那一族一律走 `hb_宽开` / 命令行参数一律过 `hb_取参数`** —— Windows 上那族 ANSI 接口
       认不了中文路径,而 CRT 给的 `argv` 是按本机代码页转出来的(实测踩过三次,见那两处的注释)。
     · **Linux 那一档要 `_POSIX_C_SOURCE`**(下一行就是):工具链给的是 `cc -std=c11` —— **严格标准模式**
       (`__STRICT_ANSI__` 生效)⇒ `nanosleep` / `dlopen` 这些 POSIX 名字**一个都不给声明**,
       报 `implicit declaration of function 'nanosleep'`。实测:R3-6-5 批 2 一加 X11 那一档,
       Linux 上 7 条跨平台断言当场全红(在**任何头文件之前**声明一句就好了)。
   ═══════════════════════════════════════════════════════════════════════════ */

/* 见上面最后那条口径;**必须在任何 include 之前**(别处已经定义过也不冲突) */
#ifndef _POSIX_C_SOURCE
#define _POSIX_C_SOURCE 200809L
#endif

#include <stdarg.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>       /* sqrtf:通用化的硬件浮点开方(见 hb_fsqrt) */
#include <sys/stat.h>
#include <time.h>
#include <wchar.h>

#ifdef _WIN32
#include <direct.h>
#include <fcntl.h>
#include <io.h>
#include <windows.h>
/* `CommandLineToArgvW` 在 shell32 里(见下面 `hb_取参数`):显式点名,免得漏链接 */
#pragma comment(lib, "shell32.lib")
/* 系统文件对话框(`GetOpenFileNameW` / `GetSaveFileNameW`)在 comdlg32 里:
   与 `user32.lib` 一样是系统自带(见下面 `hb_win_*` 那一段的 `#pragma comment`)——
   G4 批 23 的 `选文件` / `另存文件` 要用它,"一条命令行直接编出 exe"的口径不变 */
#pragma comment(lib, "comdlg32.lib")
#else
#include <dirent.h>
#include <errno.h>
#include <pthread.h>
#include <unistd.h>
#endif

/* 有 computed goto 的编译器(GCC / Clang)走标签表:间接跳转与 `返回` 少一次分派。
   **用 #ifndef 让路**:自检要验那条"没有 computed goto 时退回 switch(PC)"的兜底支路,
   就靠给编译器加一句 `-DHB_COMPUTED_GOTO=0`(见 `C工具链C` 的环境变量 `HB_C编译器选项`);
   无条件 #define 会把命令行那份覆盖掉,兜底支路就永远没被真跑过。 */
#ifndef HB_COMPUTED_GOTO
#if defined(__GNUC__) || defined(__clang__)
#define HB_COMPUTED_GOTO 1
#else
#define HB_COMPUTED_GOTO 0
#endif
#endif

typedef uint8_t hb_u8;
typedef uint32_t hb_u32;

/* 按 UTF-8 路径开文件(`hb_dump_state` 与图形那一族都在它前面,先声明一句;实现在下面宿主那一段) */
static FILE *hb_宽开(const char *路径, const char *模式, int *是否宽);

/* 错误槽的一条:错误号 → 说人话的说明(编译期生成,按号线性找;冷路) */
typedef struct hb_err_msg
{
    hb_u32 no;
    const char *msg;
} hb_err_msg;

/* 源码行表的一段:地址 >= addr 的指令都算 line 行(升序;0 = 查不到) */
typedef struct hb_span
{
    hb_u32 addr;
    hb_u32 line;
} hb_span;

static hb_u8 *hb_mem;
static hb_u32 hb_mem_size;
static hb_u32 hb_r[16];
static hb_u32 hb_pc;
static hb_u32 hb_sp;
static hb_u8 hb_psw;

/* R3-6-4 批 2(全量对拍):**执行过的指令条数** 与 **最近若干条指令地址**。
   生成代码在每条指令的标签后面更新它们(见 `hb_到达()`)。
   为什么要有它:输出相同、内存相同,但"跑过的条数 / 路径"不同 = 两份实现走的不是同一条路
   (实测一个示例差出三百多条,只比输出与最终内存根本看不出来);
   转储里带上最近几十条地址,分岔点就能直接看出来。 */
static hb_u32 hb_steps;
static hb_u32 hb_at;
#define HB_TRACE_N 64
static hb_u32 hb_trace[HB_TRACE_N];
static hb_u32 hb_trace_n;

/* 走到一条新指令上:记地址、记最近路径、条数 +1 */
static void hb_到达(hb_u32 地址)
{
    hb_at = 地址;
    hb_trace[hb_trace_n++ & (HB_TRACE_N - 1u)] = 地址;
    hb_steps++;
}

static const hb_span *hb_lines;
static hb_u32 hb_lines_n;

static hb_u32 hb_slot;
static const hb_err_msg *hb_errs;
static hb_u32 hb_errs_n;

/* ---- 输出缓冲:一个字节一次 fwrite 太碎,攒一攒再写 ---- */
#define HB_OUT_CAP 8192
static hb_u8 hb_out_buf[HB_OUT_CAP];
static size_t hb_out_len;

static void hb_flush(void)
{
    if (hb_out_len > 0)
    {
        fwrite(hb_out_buf, 1, hb_out_len, stdout);
        hb_out_len = 0;
    }
}

static void hb_put(hb_u8 b)
{
    if (hb_out_len >= HB_OUT_CAP) hb_flush();
    hb_out_buf[hb_out_len++] = b;
}

/* ---- 行号:拿当前这条指令的地址查源码行(冷路才走,线性扫够用) ---- */
static hb_u32 hb_line_of(hb_u32 addr)
{
    hb_u32 line = 0;
    hb_u32 i;
    for (i = 0; i < hb_lines_n; i++)
    {
        if (hb_lines[i].addr <= addr) line = hb_lines[i].line;
        else break;
    }
    return line;
}

static void hb_dump_state(void);

static void hb_fail_at(hb_u32 addr, const char *msg)
{
    hb_u32 line;
    hb_flush();
    hb_dump_state();
    line = hb_line_of(addr);
    if (line > 0) fprintf(stderr, "运行期错误(第 %u 行): %s\n", line, msg);
    else fprintf(stderr, "运行期错误: %s\n", msg);
    fflush(stderr);
    exit(1);
}

static void hb_fail(const char *msg) { hb_fail_at(hb_pc, msg); }

static void hb_failf(hb_u32 addr, const char *fmt, ...)
{
    char buf[512];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(buf, sizeof buf, fmt, ap);
    va_end(ap);
    hb_fail_at(addr, buf);
}

/* ---- 内存:与 `虚拟机C.CheckAddr / ReadMemory / WriteMemory / ReadWord / WriteWord` 同口径 ---- */
static hb_u32 hb_word_at(hb_u32 a)
{
    return (hb_u32)hb_mem[a]
        | ((hb_u32)hb_mem[a + 1] << 8)
        | ((hb_u32)hb_mem[a + 2] << 16)
        | ((hb_u32)hb_mem[a + 3] << 24);
}

static void hb_word_put(hb_u32 a, hb_u32 v)
{
    hb_mem[a] = (hb_u8)(v & 0xFF);
    hb_mem[a + 1] = (hb_u8)((v >> 8) & 0xFF);
    hb_mem[a + 2] = (hb_u8)((v >> 16) & 0xFF);
    hb_mem[a + 3] = (hb_u8)((v >> 24) & 0xFF);
}

static void hb_ck8(hb_u32 a)
{
    if (a >= hb_mem_size)
        hb_failf(hb_pc, "内存越界: 0x%X 超出内存(共 %u 字节)—— 检查下标 / 地址是不是算错了", a, hb_mem_size);
}

static hb_u8 hb_ld8(hb_u32 a) { hb_ck8(a); return hb_mem[a]; }

static void hb_st8(hb_u32 a, hb_u32 v) { hb_ck8(a); hb_mem[a] = (hb_u8)v; }

static void hb_ck32(hb_u32 a)
{
    if (a > hb_mem_size - 4)
        hb_failf(hb_pc, "字访问越界: 0x%X..0x%X 超出内存(共 %u 字节)—— 检查下标 / 地址是不是算错了",
            a, a + 3, hb_mem_size);
}

static hb_u32 hb_ld32(hb_u32 a) { hb_ck32(a); return hb_word_at(a); }

static void hb_st32(hb_u32 a, hb_u32 v) { hb_ck32(a); hb_word_put(a, v); }

/* ---- 硬件栈:压返回地址 / 入栈出栈(SP 往下长,溢出与空栈都报说人话的错) ---- */
/* T11(2026-09-27 评测整改):下界与池的硬顶同一个数(内存末尾 − 给硬件栈留的余量,见 hb_host_reserve)。
   以前只挡 `hb_sp < 4` —— 那要压满整个内存才拦得住,路上早把代码与数据写掉了(与 `虚拟机C.PushWord` 同一处缺陷)。*/
static hb_u32 hb_host_reserve(void);
static void hb_push(hb_u32 v)
{
    if (hb_sp < hb_host_reserve() + 4u)
        hb_fail("栈溢出(压栈次数太多,或者压栈/弹栈没配对 —— 定长栈只占运行内存末尾那一块,"
            "再往下写就压坏代码与数据了;调用套得太深就把递归改成循环,大数组挪到顶层)");
    hb_sp -= 4;
    hb_word_put(hb_sp, v);
}

static hb_u32 hb_pop(void)
{
    hb_u32 v;
    if (hb_sp + 4 > hb_mem_size)
        hb_fail("栈为空(压栈/弹栈没配对 —— 检查是不是多写了一次 弹出,少写了一次 压栈)");
    v = hb_word_at(hb_sp);
    hb_sp += 4;
    return v;
}

/* ---- 比较 / 输入输出 ---- */
static void hb_cmp(hb_u32 a, hb_u32 b) { hb_psw = (a > b) ? 1 : ((a < b) ? 2 : 0); }

/*
  比较取值(P13 `比较取值` 0x2F):低 3 位是关系(0 等于 / 1 不等于 / 2 大于 / 3 小于 / 4 大于等于 / 5 小于等于),
  第 3 位(8)= 按**有符号**比 —— 与 `虚拟机C` 那个 case 一字不差。
*/
static hb_u32 hb_cmp_set(hb_u32 left, hb_u32 right, hb_u32 code)
{
    int ok;
    if ((code & 8u) != 0)
    {
        int32_t a = (int32_t)left;
        int32_t b = (int32_t)right;
        switch (code & 7u)
        {
            case 0u: ok = (a == b); break;
            case 1u: ok = (a != b); break;
            case 2u: ok = (a > b); break;
            case 3u: ok = (a < b); break;
            case 4u: ok = (a >= b); break;
            case 5u: ok = (a <= b); break;
            default: ok = 0; break;
        }
    }
    else
    {
        switch (code & 7u)
        {
            case 0u: ok = (left == right); break;
            case 1u: ok = (left != right); break;
            case 2u: ok = (left > right); break;
            case 3u: ok = (left < right); break;
            case 4u: ok = (left >= right); break;
            case 5u: ok = (left <= right); break;
            default: ok = 0; break;
        }
    }
    return ok ? 1u : 0u;
}

static hb_u32 hb_in(void)
{
    int c = fgetc(stdin);
    return (c == EOF) ? 0xFFFFFFFFu : (hb_u32)(hb_u8)c;
}

static void hb_out(hb_u8 b) { hb_put(b); }

static void hb_newline(void) { hb_put(0x0A); }

/* ---- 间接控制转移:目标在寄存器里,对齐只能在运行期查(与 `调用寄存器` / `跳转寄存器` 同口径) ---- */
static void hb_align(hb_u32 target, const char *name)
{
    if ((target & 0x3u) != 0)
        hb_failf(hb_pc, "`%s` 的目标地址没有 4 字节对齐:0x%X"
            "(指令定长 4 字节,从两条指令中间跳过去会解释出乱七八糟的字节;"
            "表里装的地址请用 `地址(...)` 取,别在它上面加减零头)", name, target);
}

static void hb_call_reg(hb_u32 target, hb_u32 ret)
{
    hb_align(target, "调用寄存器");
    hb_push(ret);
    hb_pc = target;
}

static void hb_jmp_reg(hb_u32 target)
{
    hb_align(target, "跳转寄存器");
    hb_pc = target;
}

/* ---- 通用化(2026-10):硬件浮点(单精度)--------------------------------------
   口径与 C# 侧 `引擎\虚拟机C.cs` 一字不差:寄存器里那 32 位就是 **IEEE754 单精度位模式**
   (与 `Sample\库\float32.hb` 同一个约定),运算用 C 的 `float` 做。
   为什么两边能逐位一致:C 的 float 就是 IEEE754 单精度,`+ - * / sqrtf` 都要求**正确舍入**;
   ⚠ 唯一要守的:**不许让编译器做 FMA 融合 / 重排**(浮点不满足结合律,换个算序位模式就变了)——
   C# 侧那条表达式就是"一次一个运算",C 侧也照"一次一个运算"编成独立函数调用,不给融合留机会。
   除零**不报错**:IEEE754 给 ±Inf / NaN(整数除零那条闸在 0x25/0x26,与这里无关)。 */
static float hb_f(uint32_t 位) { float f; memcpy(&f, &位, 4); return f; }
static uint32_t hb_fb(float f) { uint32_t 位; memcpy(&位, &f, 4); return 位; }

static uint32_t hb_fadd(uint32_t a, uint32_t b) { volatile float x = hb_f(a), y = hb_f(b); return hb_fb(x + y); }
static uint32_t hb_fsub(uint32_t a, uint32_t b) { volatile float x = hb_f(a), y = hb_f(b); return hb_fb(x - y); }
static uint32_t hb_fmul(uint32_t a, uint32_t b) { volatile float x = hb_f(a), y = hb_f(b); return hb_fb(x * y); }
static uint32_t hb_fdiv(uint32_t a, uint32_t b) { volatile float x = hb_f(a), y = hb_f(b); return hb_fb(x / y); }
static uint32_t hb_fsqrt(uint32_t a) { volatile float x = hb_f(a); return hb_fb(sqrtf(x)); }

/* 浮点比较:低 3 位是关系(0 == / 1 != / 2 > / 3 < / 4 >= / 5 <=)。
   **无序(NaN)口径照 IEEE754**:有序比较(`==` `<` `>` `<=` `>=`)一律给假,而 `!=` 给**真**
   —— 与 C# 侧 `float` 的比较结果一致(两边都靠 C 语义,所以天然同口径)。 */
static hb_u32 hb_fcmp(uint32_t a, uint32_t b, hb_u32 码)
{
    volatile float x = hb_f(a), y = hb_f(b);
    int 成立 = 0;
    switch (码 & 7u)
    {
        case 0: 成立 = (x == y); break;
        case 1: 成立 = (x != y); break;
        case 2: 成立 = (x >  y); break;
        case 3: 成立 = (x <  y); break;
        case 4: 成立 = (x >= y); break;
        case 5: 成立 = (x <= y); break;
        default: 成立 = 0; break;
    }
    return 成立 ? 1u : 0u;
}

/* 浮点转整数:按零截断;**越界 / NaN 给 0x80000000**(与 C# 侧那条判断逐字对齐 —— 不用 C 的
   `(int)f` 直接转,那个在越界时是未定义行为,两边会对不上)。 */
static uint32_t hb_f2i(uint32_t a)
{
    volatile float x = hb_f(a);
    if (!(x >= -2147483648.0f && x < 2147483648.0f)) return 0x80000000u;
    return (uint32_t)(int32_t)x;
}

/* 整数转浮点:按**有符号**读那 32 位(与 C# 侧 `(float)unchecked((int)…)` 同口径) */
static uint32_t hb_i2f(uint32_t a) { volatile int32_t v = (int32_t)a; return hb_fb((float)v); }

/* ---- 通用化第二批(2026-10):整数扩展 9 条(0x40~0x47)-------------------------
   口径与 C# 侧 `引擎\虚拟机C.cs` 那 9 个 case **一字不差**(两边靠"对拍逐字节一致"顶着):
     · 除零照旧当场报运行期错误(与老 除法/取余 同一档,不是浮点那种 ±Inf);
     · `-2^31 / -1` 照 C 的**回绕**给 `-2^31`(C# 那边 int 除法会抛,所以它在 VM 里单独判断过)——
       C 这边 (int32_t)INT32_MIN / -1 是**未定义行为**,所以也显式写出来,不许靠编译器;
     · 取余的符号**跟被除数**(C99 截断除法口径,两边天然一致)。
   位运算那三条都是**对 32 位整体**算;`0` 的前导零/尾随零 = 32、数一位 = 0。 */

static hb_u32 hb_sdiv(uint32_t a, uint32_t b)
{
    int32_t x = (int32_t)a, y = (int32_t)b;
    if (y == 0) hb_fail("除零(除数是 0 —— 先判断除数再除)");
    if (x == INT32_MIN && y == -1) return 0x80000000u;      /* 回绕,不是溢出陷阱 */
    return (uint32_t)(x / y);
}

static hb_u32 hb_smod(uint32_t a, uint32_t b)
{
    int32_t x = (int32_t)a, y = (int32_t)b;
    if (y == 0) hb_fail("除零(取余的除数是 0 —— 先判断除数再取余)");
    if (x == INT32_MIN && y == -1) return 0u;               /* 与 C# 那边 `x % -1 == 0` 对齐 */
    return (uint32_t)(x % y);
}

/* 无符号 64 位积的**高 32 位**(C 的 `unsigned long long` 保证至少 64 位) */
static hb_u32 hb_mulh(uint32_t a, uint32_t b) { return (hb_u32)(((unsigned long long)a * (unsigned long long)b) >> 32); }

/* 前导零 / 尾随零:0 给 32(与 C# 侧显式判断同口径 —— 不依赖 __builtin 的未定义行为) */
static hb_u32 hb_clz(uint32_t v)
{
    hb_u32 n = 0;
    if (v == 0u) return 32u;
    while ((v & 0x80000000u) == 0u) { v <<= 1; n++; }
    return n;
}

static hb_u32 hb_ctz(uint32_t v)
{
    hb_u32 n = 0;
    if (v == 0u) return 32u;
    while ((v & 1u) == 0u) { v >>= 1; n++; }
    return n;
}

static hb_u32 hb_popcnt(uint32_t v)
{
    hb_u32 n = 0;
    while (v != 0u) { n += (v & 1u); v >>= 1; }
    return n;
}

/* ---- ISA 扩容 批 1:浮点补齐(0x48~0x4B)-----------------------------------------
   口径与 C# 侧 `引擎\虚拟机C.cs` 那四个 case **一字不差**(两边靠"对拍逐字节一致"顶着):
   · 最大 / 最小:有一边是**非数**就把那一边**原样**交出去(两边都非数给第一个),否则按浮点比大小 ——
     ⚠ 为这个原因**不许**用 C 的 `fmaxf` / `fminf`(它们遇非数给另一边,而且非数位模式由实现定);
   · 向下取整:朝 −∞,`-0` 保号,`±∞` 与非数原样返回(算法见下面那段注释)—— 不许用 `floorf` / `(int)` 截断;
   · 符号复制:位级 `(|甲| 的位) | (乙 的符号位)`,连 `-0` / 非数的符号位也照搬。
   三条都是**只用整数/位运算**:浮点只参与"比大小",不参与位模式生成。 */

/* 这条位模式是不是非数:指数全 1 且尾数非 0 */
static int hb_is_nan(uint32_t v) { return (v & 0x7F800000u) == 0x7F800000u && (v & 0x007FFFFFu) != 0u; }

static hb_u32 hb_fmax(uint32_t 甲, uint32_t 乙)
{
    volatile float x, y;
    if (hb_is_nan(甲)) return 甲;
    if (hb_is_nan(乙)) return 乙;
    x = hb_f(甲); y = hb_f(乙);
    return x >= y ? 甲 : 乙;
}

static hb_u32 hb_fmin(uint32_t 甲, uint32_t 乙)
{
    volatile float x, y;
    if (hb_is_nan(甲)) return 甲;
    if (hb_is_nan(乙)) return 乙;
    x = hb_f(甲); y = hb_f(乙);
    return x <= y ? 甲 : 乙;
}

/* 朝 −∞ 取整:`|值| < 1` 显式分流(负数一律 -1.0,`-0` 保号);`|值| ≥ 1` 清掉小数位得到向零截断值,
   只有"负 + 原本还有小数位"才减 1.0(减完不会下溢成零,舍入值 ≥ 1.0)⇒ 全程不碰指数。 */
static hb_u32 hb_floor(uint32_t 位)
{
    hb_u32 指数 = (位 >> 23) & 0xFFu;
    int 负数 = (位 & 0x80000000u) != 0u;
    int 移;
    hb_u32 尾数, 分数掩码, 截断;
    volatile float x;
    if (指数 == 0xFFu) return 位;                       /* ±∞ / 非数:原样 */
    if (指数 == 0u) return 负数 ? 位 : 0u;               /* 非规格化 / ±0 */
    移 = (int)指数 - 127;
    if (移 < 0) return 负数 ? 0xBF800000u : 0u;          /* |值| < 1 */
    if (移 >= 23) return 位;                             /* 已经是整数 */
    尾数 = 位 & 0x007FFFFFu;
    分数掩码 = (1u << (23 - 移)) - 1u;
    if ((尾数 & 分数掩码) == 0u) return 位;               /* 已经是整数(小数位全 0) */
    截断 = 位 & ~分数掩码;                                /* 向零截断 */
    x = hb_f(截断);
    return 负数 ? hb_fb(x - 1.0f) : 截断;
}

/* ---- ISA 扩容 批 3 + 批 4(0x54~0x62)-----------------------------------------
   口径与 C# 侧 `引擎\虚拟机C.cs` 一字不差:三寄存器档字段 [23:20] 基 [19:16] 下标 [15:12] 值/目标 [11:8] 小立即数;
   内存读写走 hb_ld32 / hb_st32(自带越界查),栈走 hb_push / hb_pop。 */

/* 4 位关系码(与 `比较取值` 那张表同一份):低 3 位是关系,第 3 位(8)= 按有符号比 */
static int hb_rel(uint32_t 码, uint32_t 甲, uint32_t 乙)
{
    int 比;
    if ((码 & 8u) != 0u) { int32_t x = (int32_t)甲, y = (int32_t)乙; 比 = (x > y) - (x < y); }
    else { 比 = (甲 > 乙) - (甲 < 乙); }
    switch (码 & 7u)
    {
        case 0u: return 比 == 0;
        case 1u: return 比 != 0;
        case 2u: return 比 > 0;
        case 3u: return 比 < 0;
        case 4u: return 比 >= 0;
        case 5u: return 比 <= 0;
        default: return 0;
    }
}

/* 有符号饱和加:越界夹到 ±极值(不许靠编译器,显式写) */
static hb_u32 hb_sadd(uint32_t a, uint32_t b)
{
    int64_t 和 = (int64_t)(int32_t)a + (int64_t)(int32_t)b;
    if (和 > 2147483647LL) return 2147483647u;
    if (和 < -2147483648LL) return 0x80000000u;
    return (hb_u32)(int32_t)和;
}
/* ---- ISA 扩容 批 2:位与字节(0x4C~0x53)---------------------------------------
   八条全是纯整数 / 位运算(不碰浮点、不报运行期错误)⇒ 与老整数那批同一个写法:
   两边各自写死,不依赖编译器内建(C 的 `__builtin_bswap32` 之类**不许用**,保证与 C# 逐位一致)。 */

static hb_u32 hb_umulh(uint32_t a, uint32_t b) { return (hb_u32)(((unsigned long long)a * (unsigned long long)b) >> 32); }

static hb_u32 hb_umadd(uint32_t a, uint32_t b)
{
    return (hb_u32)((((unsigned long long)a * (unsigned long long)b) + (unsigned long long)b) >> 32);
}

static hb_u32 hb_rol(uint32_t v, uint32_t 量)
{
    hb_u32 n = 量 & 31u;
    return n == 0u ? v : (hb_u32)((v << n) | (v >> (32u - n)));
}

static hb_u32 hb_ror(uint32_t v, uint32_t 量)
{
    hb_u32 n = 量 & 31u;
    return n == 0u ? v : (hb_u32)((v >> n) | (v << (32u - n)));
}

static hb_u32 hb_bswap(uint32_t v)
{
    return ((v & 0x000000FFu) << 24) | ((v & 0x0000FF00u) << 8) | ((v & 0x00FF0000u) >> 8) | ((v & 0xFF000000u) >> 24);
}

static hb_u32 hb_getbyte(uint32_t v, hb_u32 n) { return (v >> (8u * n)) & 0xFFu; }

/* 整数的绝对值:`-2^31` 照 C 的补码回绕**仍是 -2^31**(不许写 `-v`,那是 UB) */
static hb_u32 hb_iabs(uint32_t v)
{
    int32_t x = (int32_t)v;
    return x < 0 ? (hb_u32)(0u - v) : v;
}
/* 大小取甲、符号位取乙(`-0` / 非数的符号位也照搬) */
static hb_u32 hb_copysign(uint32_t 甲, uint32_t 乙) { return (甲 & 0x7FFFFFFFu) | (乙 & 0x80000000u); }

/* 低 N 位按**有符号**扩成 32 位;N = 0 或 >= 32 视作"整 32 位",什么都不做 */
static hb_u32 hb_sign_ext(uint32_t v, hb_u32 n)
{
    if (n >= 1u && n <= 31u)
    {
        uint32_t m = (1u << n) - 1u;
        uint32_t 低 = v & m;
        return (低 & (1u << (n - 1u))) != 0u ? (低 | ~m) : 低;
    }
    return v;
}

/* 低 N 位零扩展;N = 0 或 >= 32 视作"整 32 位",什么都不做 */
static hb_u32 hb_zero_ext(uint32_t v, hb_u32 n)
{
    return (n >= 1u && n <= 31u) ? (v & ((1u << n) - 1u)) : v;
}

/* 分派表没接住(目标不在代码段里):说清是什么事,别让程序默默跑飞 */
static void hb_bad_pc(hb_u32 target, hb_u32 code_len)
{
    hb_failf(target, "`返回` / 间接跳转 的目标 0x%X 不是代码段里的一条指令(代码段只有 %u 字节)"
        " —— 检查返回地址 / 表里存的地址算错了没有", target, code_len);
}

/* ---- 出口:停机 + 查错误槽 ---- */
static void hb_dump_state(void);

static void hb_stop(void)
{
    hb_flush();
    hb_dump_state();
    if (hb_slot != 0 && hb_slot + 4 <= hb_mem_size)
    {
        hb_u32 no = hb_word_at(hb_slot);
        if (no != 0)
        {
            const char *msg = "运行期错误";
            hb_u32 i;
            for (i = 0; i < hb_errs_n; i++)
                if (hb_errs[i].no == no) { msg = hb_errs[i].msg; break; }
            fprintf(stderr, "运行停下: %s\n", msg);
            fflush(stderr);
            exit(1);
        }
    }
    exit(0);
}

/* ═══════════════════════════════════════════════════════════════════════════
   R3-6-4 批 2:整机状态导出(全量对拍用)

   自检要拿"C# 虚拟机跑一遍"与"C 后端编出的原生 exe 跑一遍"**逐字节**对账(寄存器 / 内存 /
   程序输出),所以两边都得能把"跑完这一刻机器里到底是什么"写下来。
   **只有显式要**才落盘:命令行给了 `--导出状态 <路径>`(见 `hb_取导出状态参数`)才写,
   用户那条路上一个字节都不写(那条参数不出现时 `hb_dump_slot` 为空)。

   为什么走**命令行参数**而不是环境变量:实测这台开发机上,从 .NET 起子进程时**自定义环境变量
   根本送不到原生 exe**(`getenv` 一律 NULL,`HB_C_DUMP` 也一样;只有 PATH / INCLUDE / LIB 这种
   被宿主放行的才过得去)—— 而参数是进程启动的实际内容,不受这套限制。

   格式与 `引擎\虚拟机C.cs` 的 `导出状态` **一字不差**(那边逐字符还原):
     `PC` / `MEM` / `CODE_BYTES` / `DATA_BYTES` 四行,随后 `R0 ~ R15`(每行 `R<十进制编号> <8 位十六进制>`),
     再往后是**整块内存的十六进制**(每行 64 个十六进制字符 = 32 字节,不带走首地址 —— 两边同长才算数)。
   `SP` 两边**不比**:C 侧的硬件栈在 C 自己的调用栈上(`压栈`/`弹栈` 编译成了 C 的表达式),
   HB 内存末尾那 4 个字节一个字都没动过,拿它当"栈指针"只会制造假差异;
   内存里真被用过的那一段照旧逐字节比。
   ═══════════════════════════════════════════════════════════════════════════ */
static const char *hb_dump_slot;
static hb_u32 hb_code_bytes;
static hb_u32 hb_data_bytes;

static char hb_dump_digit(hb_u32 v) { v &= 0x0Fu; return (char)(v < 10u ? ('0' + v) : ('A' + (v - 10u))); }

/* 往缓冲里写 8 位十六进制(大写,与 C# 的 `X8` 同口径),返回写了几格 */
static int hb_dump_hex8(char *p, hb_u32 v)
{
    int i;
    for (i = 0; i < 8; i++) p[i] = hb_dump_digit(v >> ((7 - i) * 4));
    return 8;
}

/* 一行 `<标签> <8 位十六进制>\n`,返回这一行几格 */
static int hb_dump_line(char *p, const char *标签, hb_u32 值)
{
    int n = 0;
    while (*标签 != '\0') p[n++] = *标签++;
    p[n++] = ' ';
    n += hb_dump_hex8(p + n, 值);
    p[n++] = '\n';
    return n;
}

static void hb_dump_state(void)
{
    FILE *f;
    int 是宽 = 0;
    char 行[80];
    int n, i;
    hb_u32 基;
    if (hb_dump_slot == NULL || hb_dump_slot[0] == '\0') return;
    /* 走 `hb_宽开`:暂存目录名里有中文,Windows 那族 ANSI 接口认不了
       (实测踩到:C 解释器跑得好好的,状态转储却一个字节都没落盘 ⇒ 38 个工程全报"没落下状态转储") */
    f = hb_宽开(hb_dump_slot, "wb", &是宽);
    if (f == NULL) return;                 /* 对拍是自检的事:落不下盘就别打扰用户程序 */

    n  = hb_dump_line(行, "PC", hb_pc);                                 fwrite(行, 1, (size_t)n, f);
    n  = hb_dump_line(行, "LAST_ADDR", hb_at);                          fwrite(行, 1, (size_t)n, f);
    n  = hb_dump_line(行, "STEPS", hb_steps);                           fwrite(行, 1, (size_t)n, f);
    n  = hb_dump_line(行, "MEM", hb_mem_size);                          fwrite(行, 1, (size_t)n, f);
    n  = hb_dump_line(行, "CODE_BYTES", hb_code_bytes);                 fwrite(行, 1, (size_t)n, f);
    n  = hb_dump_line(行, "DATA_BYTES", hb_data_bytes);                 fwrite(行, 1, (size_t)n, f);

    for (i = 0; i < 16; i++)
    {
        n = 0;
        行[n++] = 'R';
        if (i >= 10) 行[n++] = (char)('0' + (i / 10));
        行[n++] = (char)('0' + (i % 10));
        行[n++] = ' ';
        n += hb_dump_hex8(行 + n, hb_r[i]);
        行[n++] = '\n';
        fwrite(行, 1, (size_t)n, f);
    }

    for (基 = 0; 基 < hb_mem_size; 基 += 32u)
    {
        n = 0;
        for (i = 0; i < 32; i++)
        {
            hb_u32 a = 基 + (hb_u32)i;
            hb_u8 b = (a < hb_mem_size) ? hb_mem[a] : (hb_u8)0;
            行[n++] = hb_dump_digit((hb_u32)(b >> 4));
            行[n++] = hb_dump_digit((hb_u32)b);
        }
        行[n++] = '\n';
        fwrite(行, 1, (size_t)n, f);
    }
    /* 最近跑过的指令地址(从旧到新):分岔点在哪儿,拿两份转储一对照就看得见 */
    {
        hb_u32 起 = (hb_trace_n > HB_TRACE_N) ? (hb_trace_n - HB_TRACE_N) : 0u;
        for (基 = 起; 基 < hb_trace_n; 基++)
        {
            n = hb_dump_line(行, "T", hb_trace[基 & (HB_TRACE_N - 1u)]);
            fwrite(行, 1, (size_t)n, f);
        }
    }
    fclose(f);
}

/* ═══════════════════════════════════════════════════════════════════════════
   R3-6-4 批 3:宿主能力(16 条)的 C 版

   语义与 `引擎\宿主能力C.cs` **逐条同口径**:参数区布局 / 状态码 / "装不下就按 ×2 从池里换一块" /
   "池会自己长大"。改这边就必须回头看那边 —— 这也是批 2 的全量对拍能**连整块内存一起比**的原因:
   `读文件` / `列目录` / `环境变量` 往字符串变量里写的东西,连"从池里换了哪一块、池指针挪到哪"都得一样。

   **这台宿主能做什么(口径有意为之,别当疏漏)**:C 后端编出来的是一份**独立 exe** ——
   没有工程文件,也就带不了白名单(`未做任务清单.md` D6 那条尾巴要治的事),于是与老的独立 exe 完全一样:
     · 本地能力(读文件 / 写文件 / 追加写文件 / 建目录 / 删文件 / 列目录 / 环境变量 / 删目录 /
       移动到 / 复制到 / 读文件块 / 当前时间)照做;
     · `运行进程` / `取网址`(要白名单的那两条)一律 **状态 1「这个宿主没挂这项能力」**;
     · `取退出码` / `取网址状态码` 照 C# 那一份给 0xFFFFFFFF(等价于"C# 宿主没挂白名单"时那一路)。
   所以"16 条的 C 版"= 16 个功能号的**分派与状态码全都在**,其中 4 条按独立 exe 的口径如实给"没挂能力"。

   两条落地上的小心:
     · **越界一律变成状态码,不让 C 程序崩**:C# 那边越界是抛异常 → 落到"读写失败 / 参数不合法";
       这边每次取字 / 存字都先查(`hb_host_rd` / `hb_host_wr`),越界读数给毒值 0xFFFFFFFF,
       后面每一项长度检查自然落到"参数不合法"上。
     · **Windows 上路径走宽字符**:HB 的路径是 UTF-8 字节,而 `fopen` / `mkdir` 那一族在本机走 ANSI
       码页(中文路径会静默找不着)—— 所以 Windows 一侧统一转 UTF-16 再调 `_wfopen` / `CreateDirectoryW` /
       `FindFirstFileW`;POSIX 一侧直接用 UTF-8 字节。
   ═══════════════════════════════════════════════════════════════════════════ */

/* 功能号(与 `宿主能力C` 的常量一一对应;那边改一个号,这边跟着改) */
#define HB_F_READFILE   1u     /* 读文件 */
#define HB_F_WRITEFILE  2u     /* 写文件 */
#define HB_F_NOW        3u     /* 当前时间 */
#define HB_F_APPEND     4u     /* 追加写文件 */
#define HB_F_MKDIR      5u     /* 建目录 */
#define HB_F_DELFILE    6u     /* 删文件 */
#define HB_F_LISTDIR    7u     /* 列目录 */
#define HB_F_GETENV     8u     /* 环境变量 */
#define HB_F_RUNPROC    9u     /* 运行进程(要白名单 ⇒ 本宿主不挂,状态 1) */
#define HB_F_EXITCODE   10u    /* 取退出码 */
#define HB_F_RMDIR      11u    /* 删目录(只删空目录) */
#define HB_F_MOVETO     12u    /* 移动到(不覆盖) */
#define HB_F_COPYTO     13u    /* 复制到(不覆盖) */
#define HB_F_READCHUNK  14u    /* 读文件块 */
#define HB_F_FETCHURL   15u    /* 取网址(要白名单 ⇒ 本宿主不挂,状态 1) */
#define HB_F_URLCODE    16u    /* 取网址状态码 */

/* ── G3 域 5:异步输入输出(40 发起异步 / 41 任务就绪 / 42 取消任务)──
   与 C# 侧 `宿主能力C.功能_发起异步` … 一一对应。**文件那两条真开线程**(零依赖:
   Windows `CreateThread` / POSIX `pthread_create` + 一把互斥,实现见下面 `hb_host_发起异步` 那一段);
   **连接(种类 3)本宿主不挂** —— 与网络 20~26 同一条 D6 口径(独立 exe 带不了 `.hbp` 的 `网址` 白名单):
   状态 1、任务号写 -1,不发线程(与 C# 宿主"没给工程文件"那一档逐字节一致)。 */
#define HB_F_STARTASYNC 40u    /* 发起异步(立刻返回任务号;排不上给 -1) */
#define HB_F_TASKREADY  41u    /* 任务就绪(0 还没好 / 1 好了 / 2 这次失败 / 3 作废 / 22 号不成形) */
#define HB_F_CANCELTASK 42u    /* 取消任务(尽力;旧号当场作废) */

/* ---- G3 域 3:网络(裸 socket,功能号 20~26,与 `宿主能力C` 的 `功能_连接` … 一一对应)----
   **本宿主(独立 exe / 解释器核)不挂这一族**:与 `取网址` / `运行进程` 同一条 D6 口径 ——
   裸 socket 同样吃 `.hbp` 的 `网址` 白名单,而独立 exe **带不了白名单**(它是一份能随便拷走的产物),
   于是整族一律状态 1(没挂能力),写回槽写 -1(与 C# 宿主"没给工程文件"那一档逐字节一致)。
   真要那两个能力写在帮助里(见《三十一、网络》)。 */
#define HB_F_CONNECT    20u    /* 连接(要白名单 ⇒ 本宿主不挂,状态 1) */
#define HB_F_SEND       21u    /* 发送(同上) */
#define HB_F_RECV       22u    /* 接收(同上) */
#define HB_F_CLOSE      23u    /* 关闭(同上) */
#define HB_F_LISTEN     24u    /* 监听(同上) */
#define HB_F_ACCEPT     25u    /* 接受(同上) */
#define HB_F_RESOLVE    26u    /* 主机解析(同上) */
#define HB_F_SENDBLOCK  27u    /* 发送块(同上;2026-10-06 加的:从块地址发,显式字节数) */
#define HB_F_RECVBLOCK  28u    /* 接收块(同上;2026-10-06 加的:收进块地址,逐字节读得回来) */

/* ── G3 域 6:运行时(43 睡眠 / 44 冲刷 / 46 改名 / 47 临时目录)──
   **45 是空的**(判定不做:`45 时钟分辨率/单调时钟` 由 `HB_F_NOWMS` 55 覆盖,
   理由与影响面记在 `未做任务清单.md`《◆ 已声明不做》)⇒ 这四个号**不连续**,别按区间判。
   四条**在所有宿主上都挂**(不要白名单、不碰别的表):独立 exe / 解释器核与 C# 那一份同口径。 */
#define HB_F_SLEEPMS   43u    /* 睡眠(毫秒,阻塞;GUI 的"让出一帧"是 68 `HB_F_SLEEP`,两条不是一回事) */
#define HB_F_FLUSH     44u    /* 冲刷(写流句柄;流还开着。读流 → 参数不合法) */
#define HB_F_RENAME    46u    /* 改名(目标存在就整体替换;`移动到` 是不覆盖那一档) */
#define HB_F_TEMPDIR   47u    /* 临时目录(写进字符串变量;尾部不带分隔符) */

/* ---- G3 域 1:流式读写(功能号 48~54,与 `宿主能力C` 的 `功能_打开读流` … 一一对应)----
   逐字节对拍的那一套(句柄 = `(表下标 << 16) | 版本`、表 64 格、先给最小空位)与 C# 侧
   `宿主能力C` 的流句柄表**同一份口径** —— 两边各跑一遍,句柄值都要一样。 */
#define HB_F_OPENREAD   48u    /* 打开读流 */
#define HB_F_READBLOCK  49u    /* 读一块 */
#define HB_F_SEEK       50u    /* 定位 */
#define HB_F_CLOSEREAD  51u    /* 关读流 */
#define HB_F_OPENWRITE  52u    /* 打开写流 */
#define HB_F_WRITEBLOCK 53u    /* 写一块 */
#define HB_F_CLOSEWRITE 54u    /* 关写流(先冲刷) */

/* ---- G3 域 2:界面那一域的节拍(与 `宿主能力C` 的一一对应)----
   `现在毫秒` 是界面 / 定时器唯一还缺的宿主能力:`Sample\库\定时.hb` 靠它算"到点了没有"。
   单调钟(Win32 `GetTickCount64` / POSIX `CLOCK_MONOTONIC`),给低 32 位、约 49.7 天回绕 ——
   与 C# 侧 `宿主能力C.取当前毫秒` 同一份口径(对拍时两边冻成同一个刻度)。 */
#define HB_F_NOWMS      55u    /* 现在毫秒 */

/* ---- R1-6 批 1:图形界面(功能号 **60~69**;G3 域 2 起号段从 17~26 挪到框架表给界面留的那一段,
   表在 `图形宿主C`;挪号只动号,十条原语的语义一个字没改)----
   本宿主(独立 exe)**没有窗口**:与 C# 那边"没挂呈现口"同口径 → 状态 1(没挂能力),
   而且**一个字节都不写**(取事件的四个出参也不写)—— 两边才逐字节对得上。
   例外是 `取文字宽度`:它是**纯计算**(排版用),没窗口也照样给数。 */
#define HB_F_OPENWIN    60u    /* 打开窗口 */
#define HB_F_CLOSEWIN   61u    /* 关闭窗口 */
#define HB_F_CLEAR      62u    /* 清屏 */
#define HB_F_DRAWTEXT   63u    /* 画文字 */
#define HB_F_DRAWRECT   64u    /* 画矩形 */
#define HB_F_DRAWLINE   65u    /* 画线 */
#define HB_F_PRESENT    66u    /* 刷新 */
#define HB_F_POLLEVENT  67u    /* 取事件 */
#define HB_F_SLEEP      68u    /* 等待(让出一帧) */
#define HB_F_TEXTWIDTH  69u    /* 取文字宽度(纯计算) */

/* ── R4-12:位图那一族(功能号 **70~77**)────────────────────────────────────────
   与 `引擎\图形宿主C.cs` 的号段 / 请求块布局**逐字段同形**(改这边必须回头看那边):

     70 `建位图`     12 字节:句柄**槽址** / 宽 / 高   ⚠ 第 1 格是地址,不是句柄(图还不存在)
     71 `释放位图`    4 字节:句柄
     72 `位图尺寸`   12 字节:句柄 / 宽址 / 高址
     73 `画点到图`   16 字节:句柄 / x / y / 颜色
     74 `取图像素`   16 字节:句柄 / x / y / 颜色(写回)
     75 `贴位图`     16 字节:句柄 / x / y / (第 4 格不用)
     76 `画线到图`   24 字节:句柄 / x1 / y1 / x2 / y2 / 颜色
     77 `画矩形到图` 24 字节:句柄 / x / y / 宽 / 高 / 颜色

   **这一族与 60~69 那一档最大的不同**:位图是**离屏**的(建一张图、往上画点、再问它的像素),
   与"窗口开没开"无关 —— C# 那边也是这么分的(`图形宿主C.处理` 里那一段**早退 switch**),
   所以 70~74 / 76 / 77 在**没开窗**时照旧给成功;只有 `贴位图`(75)要窗口(它是把一整块贴到屏上)。
   像素格式与 C# 同一份:**0xAARRGGBB**、行优先、紧排。 */
#define HB_F_MAKEBMP    70u    /* 建位图 */
#define HB_F_FREEBMP    71u    /* 释放位图 */
#define HB_F_BMPSIZE    72u    /* 位图尺寸 */
#define HB_F_PUTPIX     73u    /* 画点到图 */
#define HB_F_GETPIX     74u    /* 取图像素 */
#define HB_F_BLITBMP    75u    /* 贴位图 */
#define HB_F_LINEBMP    76u    /* 画线到图 */
#define HB_F_RECTBMP    77u    /* 画矩形到图 */
/* 号 **78** `贴文字到位图`(控 1-①,第四百一十七批):**C 这一侧还没实现** —— 字形光栅化要么借平台
   (Win32 的 `DrawTextW` / X11 那套字体,两套都得另写),要么自带点阵字模(汉字那 3 MB 塞不进 1MB)。
   ⇒ 这里**如实给"没挂能力"**(与 C# 侧没挂呈现口同一个状态码),**不假装能画**。
   C# 那条腿(WPF / Avalonia 两个呈现口)已经能用;跨腿判据因此只做到**形状级** —— 见
   `未做任务清单.md` 与 `任务清单.md` 第四百一十七批。 */
#define HB_F_TEXTBMP    78u    /* 贴文字到位图(本侧如实"没这一手") */

/* 图形专有状态码(与 `图形宿主C` 同值;0 / 1 / 9 用的是上面那三个通用码) */
#define HB_ST_NOWIN     22u    /* 还没 `打开窗口` 就想画 / 就刷新 */
#define HB_ST_WINON     23u    /* 窗口已经开着(只支持一个窗口) */
#define HB_ST_NOEVENT   24u    /* 这一次 `取事件` 没有事件 */
#define HB_ST_NOWINDOW  25u    /* 呈现口抛了异常(本宿主没有呈现口,留码给对拍) */

/* ---- G4 批 23:剪贴板 / 提示框 / 选文件(功能号 **80~85**)----
   与 C# 侧 `宿主能力C` 那六条**逐字段同形**(请求块形状见那边每条常量头上的说明):
     80 `读剪贴板`   28 字节:头址 / 池指针槽 / 池末槽 / 状态
     81 `写剪贴板`   28 字节:内容址 / 内容长 / 空 / 空
     82 `提示框`     28 字节:标题址/长 · 正文址/长 · 按钮 · 答案(写回)
     83 `选文件`     36 字节:标题址/长 · 后缀址/长 · 头址 · 池两槽 · 预填名址/长
     84 `另存文件`   36 字节:与 `选文件` 逐字段同形(只差"要保存")
     85 `清空剪贴板` 16 字节:状态(写回)
   ⚠ **本宿主的闸**:与 C# 那边同一条口径 —— 独立 exe 与命令行**不许弹窗**(硬规矩 6),
   所以 82 / 83 / 84 一律给"不支持";**剪贴板照样是好的**(它不弹窗、不用人点)。 */
#define HB_F_READCLIP   80u    /* 读剪贴板 */
#define HB_F_WRITECLIP  81u    /* 写剪贴板 */
#define HB_F_DIALOG     82u    /* 提示框 */
#define HB_F_PICKFILE   83u    /* 选文件 */
#define HB_F_SAVEFILE   84u    /* 另存文件 */
#define HB_F_CLEARCLIP  85u    /* 清空剪贴板 */

/* `这个平台没有这一手`(与 `宿主能力C.状态_不支持` 同值:25)。
   ⚠ 与图形那一族的 `HB_ST_NOWINDOW`(也是 25)**不同族**,各自在自己的号段里解释 —— 22 / 23 / 24
   早就这么复用了(见 `HB_ST_TASKMANY` 那一行)。 */
#define HB_ST_UNSUP     25u    /* 这个平台 / 这个宿主没有这一手 */

/* 提示框的按钮与答案(与 `宿主能力C.按钮_*` / `答_*` 同数) */
#define HB_BTN_OK       0u
#define HB_BTN_OKCANCEL 1u
#define HB_BTN_YESNO    2u
#define HB_BTN_YESNOCAN 3u


/* 状态码(与 `宿主能力C` 同数;0 = 成功) */
#define HB_ST_OK        0u
#define HB_ST_NOCAP     1u
#define HB_ST_OPEN      2u     /* 打不开:不存在 / 没权限 */
#define HB_ST_BIG       3u     /* 放不下(池长到头了) */
#define HB_ST_IO        4u     /* 读写出错 */
#define HB_ST_ARG       9u     /* 参数不合法 */
#define HB_ST_NOTEMPTY  13u    /* 目录不空 */
#define HB_ST_EXISTS    14u    /* 目标已经存在 */
#define HB_ST_NOTDIR    15u    /* 这个路径不是目录 */
#define HB_ST_EOF       16u    /* 读到头了 */
#define HB_ST_BADHANDLE 22u    /* 句柄不认(G3 流:没开过 / 已经关了) */

/* ---- R4-11:读模型(Wavefront `.obj`,功能号 56;与 `宿主能力C.功能_读取模型` 一一对应)----
   口径与 C# 那一份**逐字相同**(硬规矩:两端语义同口径,不然全量对拍当场红):
     参数:路径址/长 · 顶点址/容量 · 边址/容量 · **条数块址**;
     出口:参数区 **+28** 顶点数 / **+32** 边数 / **+36** 状态码(条数块那三格也一起写)。
   ⚠ 参数区偏移别和输入撞车:输入占 0~23,24 是条数块地址,出口从 28 起(C# 那一份踩过)。
   只认 `v` 与 `f`;面按多边形扇形三角化;边去重;下标 1 起数、负数从末尾倒数;
   读进来归一化(包围盒中心到原点、最长边缩到 2 个单位)。
   ⚠ C 里不能像 C# 那样用动态表 ⇒ 这里定死两个上限(**超了就报"装不下",不静默截断**)。 */
#define HB_F_READMODEL  56u
#define HB_OBJ_MAXV     40000u   /* 顶点解析上限 */
#define HB_OBJ_MAXE     60000u   /* 边解析上限(原始条数,去重前) */

/* 边表(文件级静态;这一族只在装载模型时用一次,不参与每帧) */
static hb_u32 hb_obj_e[HB_OBJ_MAXE][2];

#define HB_ST_TOOMANY   23u    /* 流句柄表满了(G3 流) */
#define HB_ST_TASKMANY  24u    /* 任务格满了(G3 域 5:32 格都占着)。与图形那两格同值不同族 —— 22/23 早就是这么复用的 */

static const char *hb_now_slot;   /* `--hb-now <秒>`(生成的 main 填);NULL / 空 = 用本机真时间 */
static const char *hb_now_ms_slot; /* `--hb-now-ms <毫秒>`(G3 域 2):把 `现在毫秒()` 的钟冻住;NULL / 空 = 用本机单调钟 */

/* ---- 越界安全的字访问 ---- */
static int hb_host_fits(hb_u32 a, uint64_t n)
{
    return (uint64_t)a + n <= (uint64_t)hb_mem_size;
}

static hb_u32 hb_host_rd(hb_u32 a)
{
    return hb_host_fits(a, 4u) ? hb_word_at(a) : 0xFFFFFFFFu;
}

static void hb_host_wr(hb_u32 a, hb_u32 v)
{
    if (hb_host_fits(a, 4u)) hb_word_put(a, v);
}

/* 池能长到"内存末尾 − 给硬件栈留的余量"(与 `虚拟机C.栈顶保留` 一字不差) */
static hb_u32 hb_host_reserve(void)
{
    hb_u32 四分之一 = hb_mem_size / 4u;
    return 四分之一 < (16u * 1024u) ? 四分之一 : (16u * 1024u);
}

static hb_u32 hb_host_now(void)
{
    if (hb_now_slot != NULL && hb_now_slot[0] != '\0') return (hb_u32)strtoul(hb_now_slot, NULL, 10);
    return (hb_u32)time(NULL);
}

/*
    G3 域 2:`现在毫秒` 的 C 版 —— **单调钟**(与 C# 的 `Environment.TickCount64` 同口径),
    给低 32 位(约 49.7 天回绕,差值照旧对;见 `宿主能力C.功能_现在毫秒` 那段口径)。
    为什么不用 `time(NULL)`:`当前时间()` 是墙上时间,用户一改系统时间它就会往回跳 ——
    定时器最怕这个(实测过:两帧之间永远是 0,定时器就没法定时)。

    **对拍怎么冻**:不冻的话这个刻度写进运行内存、整机逐字节比必然差 4 个字节 ——
    与 `当前时间()` 一样,命令行给了 `--hb-now-ms <毫秒>` 就用那个写死的刻度,
    自检两边喂同一个值(见 `综合自检C.对拍一个工程` 的"冻两把钟");不给就是本机单调钟。
*/
static hb_u32 hb_host_now_ms(void)
{
#if defined(_WIN32)
    if (hb_now_ms_slot != NULL && hb_now_ms_slot[0] != '\0')
        return (hb_u32)strtoul(hb_now_ms_slot, NULL, 10);
    return (hb_u32)GetTickCount64();
#elif defined(__linux__)
    {
        struct timespec 时;
        if (hb_now_ms_slot != NULL && hb_now_ms_slot[0] != '\0')
            return (hb_u32)strtoul(hb_now_ms_slot, NULL, 10);
        if (clock_gettime(CLOCK_MONOTONIC, &时) != 0) return 0u;
        return (hb_u32)((uint64_t)时.tv_sec * 1000u + (uint64_t)(时.tv_nsec / 1000000));
    }
#else
    /* 别的平台还没接:`--hb-now-ms` 给了就照给,没给如实给 0(与"没挂这个能力"同值) */
    return (hb_now_ms_slot != NULL && hb_now_ms_slot[0] != '\0')
        ? (hb_u32)strtoul(hb_now_ms_slot, NULL, 10) : 0u;
#endif
}

/*
   这个码点算不算"两格宽"(与 C# 的 `图形宿主C.是宽字` 逐条同区间:CJK / 全角标点 / 韩文 /
   中日韩兼容)。`取文字宽度` 与无头呈现那边共用这一份 —— 有两条 C 路(转译产物 / 解释器核),
   但这条口径只该有一处。
*/
static int hb_gfx_宽字(hb_u32 码点)
{
    return (码点 >= 0x1100u && 码点 <= 0x115Fu)
        || (码点 >= 0x2E80u && 码点 <= 0x303Eu)
        || (码点 >= 0x3041u && 码点 <= 0x33FFu)
        || (码点 >= 0x3400u && 码点 <= 0x4DBFu)
        || (码点 >= 0x4E00u && 码点 <= 0x9FFFu)
        || (码点 >= 0xA000u && 码点 <= 0xA4CFu)
        || (码点 >= 0xAC00u && 码点 <= 0xD7A3u)
        || (码点 >= 0xF900u && 码点 <= 0xFAFFu)
        || (码点 >= 0xFE30u && 码点 <= 0xFE4Fu)
        || (码点 >= 0xFF00u && 码点 <= 0xFF60u)
        || (码点 >= 0xFFE0u && 码点 <= 0xFFE6u);
}

/* ---- 一串字节(读文件 / 列目录拼串用;动态长) ---- */
typedef struct hb_buf
{
    hb_u8 *p;
    size_t n;
    size_t cap;
} hb_buf;

static void hb_buf_free(hb_buf *b) { free(b->p); b->p = NULL; b->n = 0; b->cap = 0; }

static int hb_buf_room(hb_buf *b, size_t 要)
{
    hb_u8 *p;
    size_t 新;
    if (要 <= b->cap) return 1;
    新 = b->cap == 0 ? 4096u : b->cap;
    while (新 < 要) 新 *= 2u;
    p = (hb_u8 *)realloc(b->p, 新);
    if (p == NULL) return 0;
    b->p = p;
    b->cap = 新;
    return 1;
}

static char *hb_host_dup(const char *s)
{
    size_t n = strlen(s);
    char *t = (char *)malloc(n + 1u);
    if (t != NULL) memcpy(t, s, n + 1u);
    return t;
}

static char *hb_host_join(const char *目录, const char *名)
{
    size_t a = strlen(目录), b = strlen(名);
    char *s = (char *)malloc(a + b + 2u);
    if (s == NULL) return NULL;
    memcpy(s, 目录, a);
    s[a] = '/';
    memcpy(s + a + 1u, 名, b);
    s[a + b + 1u] = '\0';
    return s;
}

/* 路径的父目录(调用方 free);没有分隔符就给 NULL */
static char *hb_host_parent(const char *路径)
{
    size_t n = strlen(路径), i;
    char *s;
    while (n > 0 && (路径[n - 1u] == '/' || 路径[n - 1u] == '\\')) n--;
    for (i = n; i > 0; i--)
        if (路径[i - 1u] == '/' || 路径[i - 1u] == '\\')
        {
            if (i <= 1u) return NULL;
            s = (char *)malloc(i);
            if (s == NULL) return NULL;
            memcpy(s, 路径, i - 1u);
            s[i - 1u] = '\0';
            return s;
        }
    return NULL;
}

#ifdef _WIN32
/* UTF-8 → UTF-16(`/` 归一成 `\`;调用方 free);失败给 NULL */
static wchar_t *hb_host_w(const char *s)
{
    int n, i;
    wchar_t *w;
    if (s == NULL) return NULL;
    n = MultiByteToWideChar(CP_UTF8, 0, s, -1, NULL, 0);
    if (n <= 0) return NULL;
    w = (wchar_t *)malloc((size_t)n * sizeof(wchar_t));
    if (w == NULL) return NULL;
    if (MultiByteToWideChar(CP_UTF8, 0, s, -1, w, n) <= 0) { free(w); return NULL; }
    for (i = 0; w[i] != L'\0'; i++)
        if (w[i] == L'/') w[i] = L'\\';
    return w;
}

/* UTF-16 → UTF-8(调用方 free);失败给 NULL */
static char *hb_host_u8(const wchar_t *w)
{
    int n;
    char *s;
    if (w == NULL) return NULL;
    n = WideCharToMultiByte(CP_UTF8, 0, w, -1, NULL, 0, NULL, NULL);
    if (n <= 0) return NULL;
    s = (char *)malloc((size_t)n);
    if (s == NULL) return NULL;
    WideCharToMultiByte(CP_UTF8, 0, w, -1, s, n, NULL, NULL);
    return s;
}
#endif

/* ---- 按 UTF-8 路径开文件(与 `hb_host_wfopen` 同一套口径) ----
   为什么单开一个件:**这台机器上的 ANSI 码页认不了中文路径**(实测踩过三次:
   镜像文件、图形 dump、状态转储各一次),凡是"路径从命令行 / 暂存目录来"的 `fopen`
   都得换成它。`是否宽` 非空时告诉调用方"走的是宽路径"(两边都用 `fclose` 收尾,不用分)。 */
static char *hb_宽转u8(const wchar_t *宽)
{
#ifdef _WIN32
    return hb_host_u8(宽);
#else
    (void)宽;
    return NULL;
#endif
}

static FILE *hb_宽开(const char *路径, const char *模式, int *是否宽)
{
#ifdef _WIN32
    wchar_t *宽路径 = hb_host_w(路径);
    wchar_t *宽模式 = hb_host_w(模式);
    FILE *f = NULL;
    if (宽路径 != NULL && 宽模式 != NULL) f = _wfopen(宽路径, 宽模式);
    free(宽路径);
    free(宽模式);
    if (是否宽 != NULL) *是否宽 = 1;
    return f;
#else
    if (是否宽 != NULL) *是否宽 = 0;
    return fopen(路径, 模式);
#endif
}

/* ═══════════════════════════════════════════════════════════════════════════
   命令行参数**自己转 UTF-8**(R3-6-3)

   为什么非转不可(这一条实测踩了两遍,写下来免得再绕):
   Windows 上 `main(int, char **argv)` 那些字节是**运行库从 UTF-16 按"当前代码页"转出来的** ——
   本机代码页是 936,**非 ASCII 的参数一进去就成了乱码**:`--hb-图形` 与中文路径(`…\工具\临时\…`)
   都是这样,表现出来是"参数明明递过去了、程序却打用法退出"或者"文件悄悄开不出来"。
   ⇒ 参数按**宽字符**拿(`GetCommandLineW` + `CommandLineToArgvW`),自己转成 UTF-8;
   POSIX 一侧的 `argv` 本来就是 UTF-8,照用(返回 NULL = 用原来的 `argv`)。
   ═══════════════════════════════════════════════════════════════════════════ */

#ifdef _WIN32

/* 返回 UTF-8 的参数表(活到进程结束);拿不到就返回 NULL,调用方照旧用 `argv` */
static char **hb_取参数(int argc, char **argv, int *出个数)
{
    int 个数 = 0, i;
    LPWSTR *宽表 = CommandLineToArgvW(GetCommandLineW(), &个数);
    char **表;
    (void)argc; (void)argv;
    if (宽表 == NULL || 个数 <= 0) return NULL;
    表 = (char **)calloc((size_t)个数 + 1u, sizeof(char *));
    if (表 == NULL) { LocalFree(宽表); return NULL; }
    for (i = 0; i < 个数; i++)
    {
        表[i] = hb_宽转u8(宽表[i]);
        if (表[i] == NULL) { LocalFree(宽表); return NULL; }
    }
    表[个数] = NULL;
    LocalFree(宽表);
    *出个数 = 个数;
    return 表;
}

#else

static char **hb_取参数(int argc, char **argv, int *出个数)
{
    (void)argc; (void)argv; (void)出个数;
    return NULL;
}

#endif

/* ═══════════════════════════════════════════════════════════════════════════
   R3-6-5 批 2:命令行开关**只有一份**(解释器核与转译产物两条 C 路的 `main` 共用)。

   为什么要抽这一处:这一批起转译产物也带图形了,于是"认哪些开关"变成两条路都要有的东西 ——
   各写一套必然漂开(老的 `--hb-图形` / `--hb-事件脚本` 只在解释器核里认过一遍)。
   口径一字不改:`--hb-dump` / `--hb-now` 老样子;图形那三件只有 `HB_有图形` 为 1 时才认,
   不带图形的那一份给了它们照旧报"这一份没带图形"(与老的 `#else` 分支同一个说法)。

   返回 0 = 认下来了;2 = 用法不对(调用方打印用法并退出)。
   `带图形` 由调用方传(`HB_有图形`),这样这个函数本身与图形开关解耦。
   ═══════════════════════════════════════════════════════════════════════════ */

typedef struct hb_开关
{
    const char *状态文件;      /* `--hb-dump <文件>`:跑完把整机状态导到这儿(空 = 不导) */
    const char *图形文件;      /* `--hb-图形 <文件>`:绘制命令逐行写进这儿(空 = 写标准输出) */
    const char *脚本文本;      /* `--hb-事件脚本 <文件>`:按帧喂的事件脚本(空 = 没有输入) */
    int 要窗口;                /* `--hb-窗口`:开真窗口那一档 */
} hb_开关;

static void hb_用法(const char *程序名, int 带图形)
{
    const char *谁 = (程序名 != NULL && 程序名[0] != '\0') ? 程序名 : "hb";
    fprintf(stderr, "用法: %s <镜像文件|(生成物自带机器码)> [--hb-dump <状态文件>] [--hb-now <秒>] [--hb-now-ms <毫秒>]\n", 谁);
    if (带图形)
        fprintf(stderr,
            "                [--hb-图形 <文件>] [--hb-事件脚本 <文件>] [--hb-窗口]\n"
            "  --hb-图形   挂上无头呈现:绘制命令逐行写进那个文件(不写就走标准输出)。\n"
            "  --hb-事件脚本  按这份脚本按帧喂事件(格式见 `引擎\\图形事件C.cs`)。\n"
            "  --hb-窗口   开一个真窗口(Windows / GDI,Linux / X11):绘制命令真画上去,\n"
            "              键盘鼠标真收进来;没给 `--hb-图形` 时绘制命令的文本 dump 丢进黑洞\n"
            "              (不混进程序输出)。\n");
    else
        fprintf(stderr, "                (这一份编的时候没带图形,图形那三个开关认不出来)\n");
    fflush(stderr);
}

static int hb_取开关(int argc, char **argv, int 带图形, hb_开关 *出)
{
    int i;
    出->状态文件 = NULL;
    出->图形文件 = NULL;
    出->脚本文本 = NULL;
    出->要窗口 = 0;

    for (i = 1; i < argc; i++)
    {
        if (strcmp(argv[i], "--hb-dump") == 0 && i + 1 < argc) 出->状态文件 = argv[++i];
        else if (strcmp(argv[i], "--hb-now") == 0 && i + 1 < argc) hb_now_slot = argv[++i];
        else if (strcmp(argv[i], "--hb-now-ms") == 0 && i + 1 < argc) hb_now_ms_slot = argv[++i];
        else if (带图形 && strcmp(argv[i], "--hb-图形") == 0 && i + 1 < argc) 出->图形文件 = argv[++i];
        else if (带图形 && strcmp(argv[i], "--hb-事件脚本") == 0 && i + 1 < argc) 出->脚本文本 = argv[++i];
        else if (带图形 && strcmp(argv[i], "--hb-窗口") == 0) 出->要窗口 = 1;
        else if (argv[i][0] == '-' && argv[i][1] != '\0') return 2;      /* 认不出的开关 / 少一个参数 */
    }
    return 0;
}

/* ---- 文件 / 目录:这一层只认 UTF-8 路径,平台差异关在这几个小件里 ---- */
static int hb_host_is_file(const char *路径)
{
#ifdef _WIN32
    wchar_t *w = hb_host_w(路径);
    DWORD 属性;
    int 成;
    if (w == NULL) return 0;
    属性 = GetFileAttributesW(w);
    free(w);
    成 = (属性 != INVALID_FILE_ATTRIBUTES && (属性 & FILE_ATTRIBUTE_DIRECTORY) == 0);
    return 成;
#else
    struct stat 状态;
    return stat(路径, &状态) == 0 && S_ISREG(状态.st_mode);
#endif
}

static int hb_host_is_dir(const char *路径)
{
#ifdef _WIN32
    wchar_t *w = hb_host_w(路径);
    DWORD 属性;
    int 成;
    if (w == NULL) return 0;
    属性 = GetFileAttributesW(w);
    free(w);
    成 = (属性 != INVALID_FILE_ATTRIBUTES && (属性 & FILE_ATTRIBUTE_DIRECTORY) != 0);
    return 成;
#else
    struct stat 状态;
    return stat(路径, &状态) == 0 && S_ISDIR(状态.st_mode);
#endif
}

/* 逐级建目录(与 C# 的 `Directory.CreateDirectory` 同口径:中间的也建;**已存在不算错**) */
static void hb_host_makedirs(const char *路径)
{
#ifdef _WIN32
    wchar_t *w = hb_host_w(路径);
    size_t i;
    if (w == NULL) return;
    for (i = 1u; w[i] != L'\0'; i++)
        if (w[i] == L'\\' || w[i] == L'/')
        {
            wchar_t c = w[i];
            w[i] = L'\0';
            CreateDirectoryW(w, NULL);
            w[i] = c;
        }
    CreateDirectoryW(w, NULL);
    free(w);
#else
    char *t = hb_host_dup(路径);
    size_t i;
    if (t == NULL) return;
    for (i = 1u; t[i] != '\0'; i++)
        if (t[i] == '/' || t[i] == '\\')
        {
            char c = t[i];
            t[i] = '\0';
            mkdir(t, 0755);
            t[i] = c;
        }
    mkdir(t, 0755);
    free(t);
#endif
}

static int hb_host_write_file(const char *路径, const hb_u8 *内容, size_t 长, int 追加)
{
    FILE *f;
#ifdef _WIN32
    wchar_t *w = hb_host_w(路径);
    if (w == NULL) return HB_ST_IO;
    f = _wfopen(w, 追加 ? L"ab" : L"wb");
    free(w);
#else
    f = fopen(路径, 追加 ? "ab" : "wb");
#endif
    if (f == NULL) return HB_ST_IO;
    if (长 > 0 && fwrite(内容, 1, 长, f) != 长) { fclose(f); return HB_ST_IO; }
    fclose(f);
    return HB_ST_OK;
}

static int hb_host_read_all(const char *路径, hb_buf *出)
{
    FILE *f;
    size_t 读;
    出->n = 0;
#ifdef _WIN32
    wchar_t *w = hb_host_w(路径);
    if (w == NULL) return HB_ST_IO;
    f = _wfopen(w, L"rb");
    free(w);
#else
    f = fopen(路径, "rb");
#endif
    if (f == NULL) return HB_ST_IO;
    for (;;)
    {
        if (!hb_buf_room(出, 出->n + 65536u)) { fclose(f); return HB_ST_IO; }
        读 = fread(出->p + 出->n, 1, 出->cap - 出->n, f);
        出->n += 读;
        if (读 == 0) break;
    }
    fclose(f);
    return HB_ST_OK;
}

/* 从 `偏移` 起读**最多** `上限` 字节;到头了(或文件空)给 HB_ST_EOF */
static int hb_host_read_chunk(const char *路径, hb_u32 偏移, hb_u32 上限, hb_buf *出)
{
    FILE *f;
    long 长度;
    size_t 要读, 读;
    出->n = 0;
#ifdef _WIN32
    wchar_t *w = hb_host_w(路径);
    if (w == NULL) return HB_ST_IO;
    f = _wfopen(w, L"rb");
    free(w);
#else
    f = fopen(路径, "rb");
#endif
    if (f == NULL) return HB_ST_IO;
    if (fseek(f, 0, SEEK_END) != 0) { fclose(f); return HB_ST_IO; }
    长度 = ftell(f);
    if (长度 < 0) { fclose(f); return HB_ST_IO; }
    if ((long)偏移 >= 长度) { fclose(f); return HB_ST_EOF; }
    要读 = (size_t)(长度 - (long)偏移);
    if (要读 > (size_t)上限) 要读 = (size_t)上限;
    if (!hb_buf_room(出, 要读)) { fclose(f); return HB_ST_IO; }
    if (fseek(f, (long)偏移, SEEK_SET) != 0) { fclose(f); return HB_ST_IO; }
    读 = fread(出->p, 1, 要读, f);
    出->n = 读;
    fclose(f);
    return HB_ST_OK;
}

/* ================= G3 流句柄表(与 C# 侧 `宿主能力C` 同一份口径)=================
   句柄 = `(表下标 << 16) | 版本`;版本从 1 起、**这一格被复用过了那一下 +1**(老句柄从此认不出)。
   开流**先给最小空位** —— 两边跑同一段程序,拿到的句柄值必须逐个一样(对拍比的就是它)。
   表是进程级静态:这个 exe 跑一个程序,退出就没了。

   ⚠ **"开的时候不抬版本、关的时候才抬"**(第三百四十三批修):原先这一份在 `开流` 里也 +1,
   于是一格开开关关 k 次,C 侧末次句柄是 `2k-1`、C# 侧是 `k` —— 两边只在"每个程序只开一次流"
   的工程上看不出差别,`示例32_数据表`(一份表要开几十次读流)当场露馅:末次读流句柄
   虚拟机 `0x1D`(=29)、原生 `0x39`(=57),逐字节对拍报"内存第一个不同"。
   那个被比到的字节是 `打开读流` 请求块 `+8`(句柄写回那一格)。 */
#define HB_流表格数 64
static FILE *hb_流表[HB_流表格数];
static hb_u32 hb_流版本[HB_流表格数];
static int hb_流是写[HB_流表格数];

/* 按句柄找那一格(认不出给 -1);找到时把"这条是写流吗"填进 *是写 */
static int hb_host_流格(hb_u32 句柄, int *是写)
{
    hb_u32 下标 = 句柄 >> 16;
    hb_u32 版本 = 句柄 & 0xFFFFu;
    if (下标 >= (hb_u32)HB_流表格数 || 版本 == 0u) return -1;
    if (hb_流表[下标] == NULL || hb_流版本[下标] != 版本) return -1;
    if (是写 != NULL) *是写 = hb_流是写[下标];
    return (int)下标;
}

/* 开一条流:`写 = 0` 走 rb(文件必须在)、`写 = 1` 走 wb(不存在就新建、存在就从头写起);
   成了把句柄写进 *句柄(状态码照样返回) */
static hb_u32 hb_host_开流(const char *路径, int 写, hb_u32 *句柄)
{
    int i;
    FILE *f;
    for (i = 0; i < HB_流表格数; i++) if (hb_流表[i] == NULL) break;
    if (i >= HB_流表格数) return HB_ST_TOOMANY;
#ifdef _WIN32
    {
        wchar_t *w = hb_host_w(路径);
        if (w == NULL) return HB_ST_IO;
        f = _wfopen(w, 写 ? L"wb" : L"rb");
        free(w);
    }
#else
    f = fopen(路径, 写 ? "wb" : "rb");
#endif
    if (f == NULL) return HB_ST_OPEN;
    hb_流表[i] = f;
    /* 版本**开的时候不抬**(与 C# 侧 `宿主能力C.开流` 逐字对齐):抬的那一下在 `关流` 里 ——
       "版本 +1" 表达的是"这一格从此算复用过了"。静态零初值 ⇒ 头一次用这一格先把它摆到 1,
       于是第 k 次开给的句柄就是 `(下标 << 16) | k`,与 C# 侧一模一样 */
    if (hb_流版本[i] == 0u) hb_流版本[i] = 1u;
    hb_流是写[i] = 写;
    *句柄 = ((hb_u32)i << 16) | (hb_流版本[i] & 0xFFFFu);
    return HB_ST_OK;
}

/* 关一条流(`冲刷 = 1` 时先 fflush —— `关写流` 的口径) */
static int hb_host_关流(hb_u32 句柄, int 冲刷)
{
    int 格 = hb_host_流格(句柄, NULL);
    if (格 < 0) return 0;
    if (冲刷) fflush(hb_流表[格]);
    fclose(hb_流表[格]);
    hb_流表[格] = NULL;
    hb_流版本[格] = hb_流版本[格] + 1u;
    return 1;
}

/* 按句柄写一段(返回真写下去的字节数;-1 = 句柄不认 / 不是写流 / 写出错) */
static long hb_host_往流里写(hb_u32 句柄, const hb_u8 *数据, hb_u32 长)
{
    int 是写 = 0;
    int 格 = hb_host_流格(句柄, &是写);
    size_t 写;
    if (格 < 0 || !是写) return -1;
    if (长 == 0u) return 0;
    写 = fwrite(数据, 1, (size_t)长, hb_流表[格]);
    fflush(hb_流表[格]);        /* 与 C# 侧 `FileStream.Flush()` 同一档:先到系统缓冲,落盘在关流 */
    if (写 != (size_t)长) return -1;
    return (long)写;
}

/* 目录里的名字(只本层、不带路径,按字节序排序),拼成 `\n` 分隔的 UTF-8 串(调用方 free)。
   不是目录 / 打不开给 NULL。
   排序口径:C# 那边是 `StringComparer.Ordinal`(UTF-16 码元序);这边按 UTF-8 字节序 ——
   基本多文种平面里两者的先后**一致**(UTF-8 保序),示例里的名字又都是 ASCII,所以对得上。 */
static char *hb_host_listdir(const char *路径)
{
    char **名 = NULL;
    size_t 数 = 0, 容 = 0, i, 全长 = 1u;
    char *结果;
    hb_buf 串;
    if (!hb_host_is_dir(路径)) return NULL;
#ifdef _WIN32
    {
        wchar_t *宽目录 = hb_host_w(路径);
        wchar_t *模式;
        WIN32_FIND_DATAW 找;
        HANDLE 句柄;
        size_t 长;
        if (宽目录 == NULL) return NULL;
        长 = wcslen(宽目录);
        while (长 > 0 && 宽目录[长 - 1u] == L'\\') 长--;
        模式 = (wchar_t *)malloc((长 + 3u) * sizeof(wchar_t));
        if (模式 == NULL) { free(宽目录); return NULL; }
        memcpy(模式, 宽目录, 长 * sizeof(wchar_t));
        模式[长] = L'\\';
        模式[长 + 1u] = L'*';
        模式[长 + 2u] = L'\0';
        free(宽目录);
        句柄 = FindFirstFileW(模式, &找);
        free(模式);
        if (句柄 == INVALID_HANDLE_VALUE) return NULL;
        do
        {
            char *一个;
            if (wcscmp(找.cFileName, L".") == 0 || wcscmp(找.cFileName, L"..") == 0) continue;
            一个 = hb_host_u8(找.cFileName);
            if (一个 == NULL) continue;
            if (数 + 1u > 容)
            {
                char **更大 = (char **)realloc(名, (容 == 0 ? 16u : 容 * 2u) * sizeof(char *));
                if (更大 == NULL) { free(一个); continue; }
                名 = 更大;
                容 = 容 == 0 ? 16u : 容 * 2u;
            }
            名[数++] = 一个;
        } while (FindNextFileW(句柄, &找));
        FindClose(句柄);
    }
#else
    {
        DIR *目录 = opendir(路径);
        struct dirent *项;
        if (目录 == NULL) return NULL;
        while ((项 = readdir(目录)) != NULL)
        {
            char *一个;
            if (strcmp(项->d_name, ".") == 0 || strcmp(项->d_name, "..") == 0) continue;
            一个 = hb_host_dup(项->d_name);
            if (一个 == NULL) continue;
            if (数 + 1u > 容)
            {
                char **更大 = (char **)realloc(名, (容 == 0 ? 16u : 容 * 2u) * sizeof(char *));
                if (更大 == NULL) { free(一个); continue; }
                名 = 更大;
                容 = 容 == 0 ? 16u : 容 * 2u;
            }
            名[数++] = 一个;
        }
        closedir(目录);
    }
#endif
    if (名 == NULL) return hb_host_dup("");
    for (i = 1u; i < 数; i++)            /* 插入排序:目录条目通常就几十个,不去引 qsort 的比较函数 */
    {
        char *当前 = 名[i];
        size_t j = i;
        while (j > 0 && strcmp(名[j - 1u], 当前) > 0) { 名[j] = 名[j - 1u]; j--; }
        名[j] = 当前;
    }
    for (i = 0; i < 数; i++) 全长 += strlen(名[i]) + 1u;
    串.p = (hb_u8 *)malloc(全长);
    if (串.p == NULL)
    {
        for (i = 0; i < 数; i++) free(名[i]);
        free(名);
        return NULL;
    }
    结果 = (char *)串.p;
    结果[0] = '\0';
    for (i = 0; i < 数; i++)
    {
        if (i > 0) strcat(结果, "\n");
        strcat(结果, 名[i]);
        free(名[i]);
    }
    free(名);
    return 结果;
}

static int hb_host_delfile(const char *路径)
{
#ifdef _WIN32
    wchar_t *w = hb_host_w(路径);
    int 成;
    if (w == NULL) return HB_ST_IO;
    成 = _wunlink(w);
    free(w);
    return 成 == 0 ? HB_ST_OK : HB_ST_IO;
#else
    return unlink(路径) == 0 ? HB_ST_OK : HB_ST_IO;
#endif
}

static int hb_host_rmdir(const char *路径)
{
#ifdef _WIN32
    wchar_t *w = hb_host_w(路径);
    int 成;
    if (w == NULL) return HB_ST_IO;
    成 = _wrmdir(w);
    free(w);
    return 成 == 0 ? HB_ST_OK : HB_ST_IO;
#else
    return rmdir(路径) == 0 ? HB_ST_OK : HB_ST_IO;
#endif
}

static int hb_host_rename(const char *旧路径, const char *新路径)
{
#ifdef _WIN32
    wchar_t *旧 = hb_host_w(旧路径);
    wchar_t *新 = hb_host_w(新路径);
    int 成;
    if (旧 == NULL || 新 == NULL) { free(旧); free(新); return HB_ST_IO; }
    成 = _wrename(旧, 新);
    free(旧);
    free(新);
    return 成 == 0 ? HB_ST_OK : HB_ST_IO;
#else
    return rename(旧路径, 新路径) == 0 ? HB_ST_OK : HB_ST_IO;
#endif
}

static int hb_host_copy_file(const char *源, const char *目标)
{
    hb_buf 内容;
    int 状态;
    FILE *出;
    memset(&内容, 0, sizeof 内容);
    状态 = hb_host_read_all(源, &内容);
    if (状态 != HB_ST_OK) { hb_buf_free(&内容); return 状态; }
#ifdef _WIN32
    {
        wchar_t *w = hb_host_w(目标);
        if (w == NULL) { hb_buf_free(&内容); return HB_ST_IO; }
        出 = _wfopen(w, L"wb");
        free(w);
    }
#else
    出 = fopen(目标, "wb");
#endif
    if (出 == NULL) { hb_buf_free(&内容); return HB_ST_IO; }
    if (内容.n > 0 && fwrite(内容.p, 1, 内容.n, 出) != 内容.n) { fclose(出); hb_buf_free(&内容); return HB_ST_IO; }
    fclose(出);
    hb_buf_free(&内容);
    return HB_ST_OK;
}

/* 整棵目录复制(目标已存在 → 14;不合并不覆盖,与 C# 那一份同口径) */
static int hb_host_copy_tree(const char *源, const char *目标)
{
    char *名字表 = hb_host_listdir(源);
    char *游;
    int 状态 = HB_ST_OK;
    if (名字表 == NULL) return HB_ST_IO;
    hb_host_makedirs(目标);
    if (名字表[0] == '\0') { free(名字表); return HB_ST_OK; }
    游 = 名字表;
    while (*游 != '\0')
    {
        char *换行 = strchr(游, '\n');
        char *子源;
        char *子目标;
        if (换行 != NULL) *换行 = '\0';
        子源 = hb_host_join(源, 游);
        子目标 = hb_host_join(目标, 游);
        if (子源 != NULL && 子目标 != NULL)
        {
            if (hb_host_is_dir(子源)) 状态 = hb_host_copy_tree(子源, 子目标);
            else if (hb_host_is_file(子源))
            {
                if (hb_host_is_file(子目标) || hb_host_is_dir(子目标)) 状态 = HB_ST_EXISTS;
                else 状态 = hb_host_copy_file(子源, 子目标);
            }
        }
        free(子源);
        free(子目标);
        if (状态 != HB_ST_OK) break;
        if (换行 == NULL) break;
        游 = 换行 + 1;
    }
    free(名字表);
    return 状态;
}

static char *hb_host_getenv(const char *名)
{
#ifdef _WIN32
    wchar_t *宽名 = hb_host_w(名);
    wchar_t *值;
    char *出;
    if (宽名 == NULL) return NULL;
    值 = _wgetenv(宽名);
    free(宽名);
    if (值 == NULL) return NULL;
    出 = hb_host_u8(值);
    return 出;
#else
    const char *值 = getenv(名);
    return 值 == NULL ? NULL : hb_host_dup(值);
#endif
}

/* ---- 参数区取串 / 把一段字节写进"参数 3 那个字符串头"指向的字符串变量 ---- */

static char *hb_host_argstr(hb_u32 参数区, hb_u32 参数下标)
{
    hb_u32 址 = hb_host_rd(参数区 + 参数下标 * 4u);
    hb_u32 长 = hb_host_rd(参数区 + 参数下标 * 4u + 4u);
    char *s;
    if (!hb_host_fits(址, (uint64_t)长 + 1u)) return NULL;
    s = (char *)malloc((size_t)长 + 1u);
    if (s == NULL) return NULL;
    if (长 > 0) memcpy(s, hb_mem + 址, 长);
    s[长] = '\0';
    return s;
}

/*
  把 `内容` 写进"头址"那个字符串变量 —— 与 `宿主能力C.写进字符串变量` 一字不差:
  装不下就按 ×2 从池里换一块(老内容搬过去、头里的"数据地址/容量"与池指针槽一起更新),
  池不够先把"池末"按 ×2 往上抬(最多抬到"内存末尾 − 硬件栈余量"),抬到顶才给"放不下"。
  `上限` > 0 时先把内容截到上限(读文件 / 列目录 / 环境变量用;读文件块传 0)。
*/
static hb_u32 hb_host_put_string_池(hb_u32 头址, hb_u32 池指针槽, hb_u32 池末槽,
                                    const hb_u8 *内容, size_t 内容长, hb_u32 上限);

static hb_u32 hb_host_put_string(hb_u32 参数区, hb_u32 头址, const hb_u8 *内容, size_t 内容长, hb_u32 上限)
{
    return hb_host_put_string_池(头址, hb_host_rd(参数区 + 12u), hb_host_rd(参数区 + 16u), 内容, 内容长, 上限);
}

/*
   同上,但**池那两格直接给**(G3 域 5 的异步读要用):`hb_host_任务就绪` 手上只有任务格里抄下来的
   头地址与两个槽地址,没有"请求块"(内容是从**上一次** `发起异步` 的请求块里抄下来的)。
*/
static hb_u32 hb_host_put_string_池(hb_u32 头址, hb_u32 池指针槽, hb_u32 池末槽,
                                    const hb_u8 *内容, size_t 内容长, hb_u32 上限)
{
    hb_u32 数据址, 容量, 新容量, 池指针, 池末, 新块, 旧长, 硬顶, 抬到;
    if (上限 > 0 && 内容长 > (size_t)上限) 内容长 = (size_t)上限;
    数据址 = hb_host_rd(头址);
    容量 = hb_host_rd(头址 + 8u);
    if (内容长 + 1u > (size_t)容量)
    {
        if (!hb_host_fits(池指针槽, 8u) || !hb_host_fits(池末槽, 4u)) return HB_ST_ARG;
        新容量 = (hb_u32)内容长 + 1u;
        if (容量 * 2u > 新容量) 新容量 = 容量 * 2u;
        if (新容量 < (hb_u32)内容长 + 1u) 新容量 = (hb_u32)内容长 + 1u;   /* 容量 < 内容长 时按 ×2 会不够,兜一头 */
        池指针 = hb_host_rd(池指针槽);
        池末 = hb_host_rd(池末槽);
        if ((uint64_t)池指针 + 新容量 > (uint64_t)池末)
        {
            int 余量 = (int)hb_host_reserve();
            硬顶 = hb_mem_size > (hb_u32)余量 ? hb_mem_size - (hb_u32)余量 : 0u;
            while (池末 < 硬顶 && (uint64_t)池指针 + 新容量 > (uint64_t)池末)
            {
                抬到 = 池末 * 2u;
                if (抬到 > 硬顶) 抬到 = 硬顶;
                if (抬到 <= 池末) break;
                池末 = 抬到;
                hb_host_wr(池末槽, 池末);
            }
        }
        if ((uint64_t)池指针 + 新容量 > (uint64_t)池末) return HB_ST_BIG;
        新块 = 池指针;
        if (!hb_host_fits(新块, (uint64_t)新容量 + 1u)) return HB_ST_ARG;
        旧长 = hb_host_rd(头址 + 4u);
        if (旧长 > 0 && hb_host_fits(数据址, 旧长)) memmove(hb_mem + 新块, hb_mem + 数据址, (size_t)旧长);
        hb_host_wr(头址, 新块);
        hb_host_wr(头址 + 8u, 新容量);
        hb_host_wr(池指针槽, 池指针 + 新容量);
        数据址 = 新块;
        容量 = 新容量;
    }
    if (!hb_host_fits(数据址, (uint64_t)内容长 + 1u)) return HB_ST_ARG;
    if (内容长 > 0) memcpy(hb_mem + 数据址, 内容, 内容长);
    hb_mem[数据址 + 内容长] = 0;
    hb_host_wr(头址 + 4u, (hb_u32)内容长);
    return HB_ST_OK;
}

/* ---- 16 个功能号各自那一段(与 `宿主能力C` 的同名方法逐条对着看) ---- */

static hb_u32 hb_host_读文件(hb_u32 参数区)
{
    char *路径 = hb_host_argstr(参数区, 0u);
    hb_u32 头址 = hb_host_rd(参数区 + 8u);
    hb_u32 上限 = hb_host_rd(参数区 + 20u);
    hb_buf 内容;
    hb_u32 状态;
    if (路径 == NULL) return HB_ST_ARG;
    if (!hb_host_fits(头址, 12u)) { free(路径); return HB_ST_ARG; }
    if (!hb_host_is_file(路径)) { free(路径); return HB_ST_OPEN; }
    memset(&内容, 0, sizeof 内容);
    if (hb_host_read_all(路径, &内容) != HB_ST_OK) { free(路径); hb_buf_free(&内容); return HB_ST_IO; }
    free(路径);
    状态 = hb_host_put_string(参数区, 头址, 内容.p, 内容.n, 上限);
    hb_buf_free(&内容);
    return 状态;
}

/* ---- R4-11:把 `.obj` 读成"顶点 + 边"(与 C# 的 `处理读取模型` 逐字同口径) ----
   三件事按顺序做:① 一趟扫完(顶点进表、面里抽边、边去重);② 归一化;③ 写两块 + 三个出口。
   ⚠ 出口**每一条返回路径都要写**(C# 那一份第一版只在成功那路写,调用方读到的是堆里的垃圾)。 */
static void hb_obj_写出口(hb_u32 参数区, hb_u32 条数块址, hb_u32 顶点数, hb_u32 边数, hb_u32 面数, hb_u32 状态)
{
    hb_host_wr(参数区 + 40u, 顶点数);
    hb_host_wr(参数区 + 44u, 边数);
    hb_host_wr(参数区 + 48u, 面数);
    hb_host_wr(参数区 + 52u, 状态);
    if (条数块址 != 0u && hb_host_fits(条数块址, 16u))
    {
        hb_host_wr(条数块址, 顶点数);
        hb_host_wr(条数块址 + 4u, 边数);
        hb_host_wr(条数块址 + 8u, 面数);
        hb_host_wr(条数块址 + 12u, 状态);
    }
}

static hb_u32 hb_host_读取模型(hb_u32 参数区)
{
    char *路径 = hb_host_argstr(参数区, 0u);
    hb_u32 顶点址   = hb_host_rd(参数区 + 8u);
    hb_u32 顶点容量 = hb_host_rd(参数区 + 12u);
    hb_u32 边址     = hb_host_rd(参数区 + 16u);
    hb_u32 边容量   = hb_host_rd(参数区 + 20u);
    /* R4-14:`面块` 在 24/28(与 `宿主能力C.处理读取模型` 同布局);`面容量 = 0` = 调用方不要面 */
    hb_u32 面址     = hb_host_rd(参数区 + 24u);
    hb_u32 面容量   = hb_host_rd(参数区 + 28u);
    hb_u32 条数块址 = hb_host_rd(参数区 + 32u);
    hb_buf 内容;
    /* 顶点表:每颗三个单精度(与调用方那块内存一个排法,归一化就地改) */
    static float 点x[HB_OBJ_MAXV], 点y[HB_OBJ_MAXV], 点z[HB_OBJ_MAXV];
    /* 三角面(C# 那边是动态表;C 里定死一张,与 `HB_OBJ_MAXE` 同量级) */
    static hb_u32 面表[HB_OBJ_MAXE][3];
    hb_u32 点数 = 0u, 原始边数 = 0u, 去重后 = 0u, 面数 = 0u;
    float 小x, 大x, 小y, 大y, 小z, 大z, 最长, 倍, 中x, 中y, 中z;
    char *p, *行尾;

    if (路径 == NULL) return HB_ST_ARG;
    /* ⚠ **参数区 56 字节 = 14 格** —— 与 `中间层C.宿主能力与取值.cs` 那个 `新槽(56)`、
       以及 `宿主能力C.处理读取模型` 的 44 字节出口布局**必须一模一样**:
         0/4 路径址/长 · 8/12 顶点址/容量 · 16/20 边址/容量 · 24/28 面址/容量
         · 32 条数块址 · 出口 40/44/48/52(顶点/边/面/状态)。
       ⚠⚠ 这里踩过一次真的(2026-10-11 实测):C 侧还停在**老布局**(条数块读 +24、出口写 28/32/36),
       于是出口把 `条数块地址` 那一格覆盖掉、拿 `面址` 当条数块 ⇒ **原生这条腿读模型恒失败**
       (虚拟机那条腿走 C# 那一份,早就对齐了 ⇒ 只有原生红,查起来像"模型文件的问题")。
       改布局时**三处一起改**:这里 / `hb_obj_写出口` / `中间层C.宿主能力与取值.cs`。 */
    if (!hb_host_fits(参数区, 56u)) { free(路径); return HB_ST_ARG; }
    if (!hb_host_is_file(路径)) { hb_obj_写出口(参数区, 条数块址, 0u, 0u, 0u, 1u); free(路径); return HB_ST_OPEN; }
    memset(&内容, 0, sizeof 内容);
    if (hb_host_read_all(路径, &内容) != HB_ST_OK)
    {
        hb_obj_写出口(参数区, 条数块址, 0u, 0u, 0u, 1u);
        free(路径); hb_buf_free(&内容); return HB_ST_IO;
    }
    free(路径);

    /* ---- ① 一趟扫完 ---- */
    for (p = (char *)内容.p; p != NULL && *p != '\0'; p = 行尾)
    {
        char *行 = p;
        char *t;
        行尾 = strchr(p, '\n');
        if (行尾 != NULL) { *行尾 = '\0'; 行尾++; } 
        while (*行 == ' ' || *行 == '\t' || *行 == '\r') 行++;
        /* 只认 `v ` 与 `f `(注意看第二个字符:`vn` / `vt` 是法线与纹理坐标,要跳过) */
        if (!((*行 == 'v' || *行 == 'f') && (行[1] == ' ' || 行[1] == '\t'))) continue;
        if (*行 == 'v')
        {
            float 三[3];
            int 第 = 0;
            for (t = strtok(行 + 1, " \t\r,"); t != NULL && 第 < 3; t = strtok(NULL, " \t\r,"))
                三[第++] = (float)atof(t);
            if (第 == 3 && 点数 < HB_OBJ_MAXV)
            {
                点x[点数] = 三[0]; 点y[点数] = 三[1]; 点z[点数] = 三[2];
                点数++;
            }
            continue;
        }
        if (*行 == 'f')
        {
            hb_u32 号[64];
            int 几个 = 0;
            for (t = strtok(行 + 1, " \t\r"); t != NULL && 几个 < 64; t = strtok(NULL, " \t\r"))
            {
                char *斜 = strchr(t, '/');
                long 原号;
                if (斜 != NULL) *斜 = '\0';
                if (*t == '\0') continue;
                原号 = atol(t);
                if (原号 > 0)      号[几个] = (hb_u32)(原号 - 1);
                else if (原号 < 0) 号[几个] = (hb_u32)((long)点数 + 原号);
                else continue;
                if (号[几个] >= 点数) continue;      /* 越界的面整个丢掉(与 C# 那边一致) */
                几个++;
            }
            if (几个 >= 3)
            {
                int k;
                for (k = 0; k < 几个; k++)
                {
                    hb_u32 左 = 号[k], 右 = 号[(k + 1) % 几个];
                    hb_u32 小, 大;
                    if (左 == 右) continue;
                    小 = 左 < 右 ? 左 : 右;
                    大 = 左 < 右 ? 右 : 左;
                    if (原始边数 < HB_OBJ_MAXE)
                    {
                        hb_obj_e[原始边数][0] = 小;
                        hb_obj_e[原始边数][1] = 大;
                        原始边数++;
                    }
                }
                /* R4-14:同一趟把**三角面**也存下来(扇形三角化 `(0,k,k+1)`),
                   绕向**照 OBJ 原样保留** —— 背面剔除要用它(与 C# 那边逐条同口径) */
                for (k = 1; k + 1 < 几个; k++)
                {
                    if (号[0] == 号[k] || 号[k] == 号[k + 1] || 号[0] == 号[k + 1]) continue;
                    if (面数 < HB_OBJ_MAXE)
                    {
                        面表[面数][0] = 号[0];
                        面表[面数][1] = 号[k];
                        面表[面数][2] = 号[k + 1];
                        面数++;
                    }
                }
            }
            continue;
        }
    }
    hb_buf_free(&内容);

    if (点数 == 0u) { hb_obj_写出口(参数区, 条数块址, 0u, 0u, 0u, 2u); return HB_ST_OK; }

    /* ---- ② 归一化(先算包围盒) ---- */
    小x = 大x = 点x[0]; 小y = 大y = 点y[0]; 小z = 大z = 点z[0];
    {
        hb_u32 i;
        for (i = 1u; i < 点数; i++)
        {
            if (点x[i] < 小x) 小x = 点x[i]; else if (点x[i] > 大x) 大x = 点x[i];
            if (点y[i] < 小y) 小y = 点y[i]; else if (点y[i] > 大y) 大y = 点y[i];
            if (点z[i] < 小z) 小z = 点z[i]; else if (点z[i] > 大z) 大z = 点z[i];
        }
    }
    最长 = 大x - 小x;
    if (大y - 小y > 最长) 最长 = 大y - 小y;
    if (大z - 小z > 最长) 最长 = 大z - 小z;
    倍 = 最长 > 1e-9f ? 2.0f / 最长 : 1.0f;
    中x = (小x + 大x) * 0.5f; 中y = (小y + 大y) * 0.5f; 中z = (小z + 大z) * 0.5f;

    /* ---- ③ 边去重(C 里用插入排序式的有序表 + 二分:朴素两重扫 60000² 太慢) ---- */
    {
        hb_u32 i, j;
        for (i = 0u; i < 原始边数; i++)
        {
            hb_u32 首下标 = hb_obj_e[i][0], 次下标 = hb_obj_e[i][1];
            hb_u32 左 = 0u, 右 = 去重后;
            int 已有 = 0;
            while (左 < 右)
            {
                hb_u32 中 = 左 + (右 - 左) / 2u;
                if (hb_obj_e[中][0] < 首下标 || (hb_obj_e[中][0] == 首下标 && hb_obj_e[中][1] < 次下标)) 左 = 中 + 1u;
                else if (hb_obj_e[中][0] == 首下标 && hb_obj_e[中][1] == 次下标) { 已有 = 1; break; }
                else 右 = 中;
            }
            if (已有) continue;
            for (j = 去重后; j > 左; j--)
            {
                hb_obj_e[j][0] = hb_obj_e[j - 1u][0];
                hb_obj_e[j][1] = hb_obj_e[j - 1u][1];
            }
            hb_obj_e[左][0] = 首下标;
            hb_obj_e[左][1] = 次下标;
            去重后++;
        }
    }

    if (点数 > 顶点容量 || 去重后 > 边容量 || (面容量 > 0u && 面数 > 面容量))
    {
        /* 装不下:照实报三个条数(好让调用方把上限调大 / 换一档) */
        hb_obj_写出口(参数区, 条数块址, 点数, 去重后, 面数, 3u);
        return HB_ST_OK;
    }

    /* ---- ④ 写三块(顶点紧排 12 字节 / 边紧排 8 字节 / 面紧排 12 字节) ---- */
    {
        hb_u32 i;
        if (!hb_host_fits(顶点址, 点数 * 12u) || !hb_host_fits(边址, 去重后 * 8u)) return HB_ST_ARG;
        if (面容量 > 0u && !hb_host_fits(面址, 面数 * 12u)) return HB_ST_ARG;
        for (i = 0u; i < 点数; i++)
        {
            float 值[3];
            hb_u32 k;
            值[0] = (点x[i] - 中x) * 倍;
            值[1] = (点y[i] - 中y) * 倍;
            值[2] = (点z[i] - 中z) * 倍;
            for (k = 0u; k < 3u; k++)
            {
                hb_u32 位;
                memcpy(&位, &值[k], 4u);
                hb_host_wr(顶点址 + i * 12u + k * 4u, 位);
            }
        }
        for (i = 0u; i < 去重后; i++)
        {
            hb_host_wr(边址 + i * 8u, hb_obj_e[i][0]);
            hb_host_wr(边址 + i * 8u + 4u, hb_obj_e[i][1]);
        }
        if (面容量 > 0u)
        {
            for (i = 0u; i < 面数; i++)
            {
                hb_host_wr(面址 + i * 12u, 面表[i][0]);
                hb_host_wr(面址 + i * 12u + 4u, 面表[i][1]);
                hb_host_wr(面址 + i * 12u + 8u, 面表[i][2]);
            }
        }
    }

    hb_obj_写出口(参数区, 条数块址, 点数, 去重后, 面数, 0u);   /* 成功 */
    return HB_ST_OK;
}

static hb_u32 hb_host_写文件(hb_u32 参数区, int 追加)
{
    char *路径 = hb_host_argstr(参数区, 0u);
    hb_u32 内容址 = hb_host_rd(参数区 + 8u);    hb_u32 内容长 = hb_host_rd(参数区 + 12u);
    char *目录;
    hb_u32 状态;
    if (路径 == NULL) return HB_ST_ARG;
    if (!hb_host_fits(内容址, 内容长)) { free(路径); return HB_ST_ARG; }
    /* 缺的目录先建出来(与 C# 那一份同口径:相对路径是相对"程序运行时的工作目录"的) */
    目录 = hb_host_parent(路径);
    if (目录 != NULL) { hb_host_makedirs(目录); free(目录); }
    状态 = hb_host_write_file(路径, hb_mem + 内容址, (size_t)内容长, 追加);
    free(路径);
    return 状态;
}

static hb_u32 hb_host_建目录(hb_u32 参数区)
{
    char *路径 = hb_host_argstr(参数区, 0u);
    if (路径 == NULL) return HB_ST_ARG;
    hb_host_makedirs(路径);
    if (!hb_host_is_dir(路径)) { free(路径); return HB_ST_OPEN; }
    free(路径);
    return HB_ST_OK;
}

static hb_u32 hb_host_删文件(hb_u32 参数区)
{
    char *路径 = hb_host_argstr(参数区, 0u);
    hb_u32 状态;
    if (路径 == NULL) return HB_ST_ARG;
    if (!hb_host_is_file(路径)) { free(路径); return HB_ST_OPEN; }
    状态 = hb_host_delfile(路径);
    free(路径);
    return 状态;
}

static hb_u32 hb_host_列目录(hb_u32 参数区)
{
    char *路径 = hb_host_argstr(参数区, 0u);
    char *清单;
    hb_u32 头址 = hb_host_rd(参数区 + 8u);
    hb_u32 上限 = hb_host_rd(参数区 + 20u);
    hb_u32 状态;
    if (路径 == NULL) return HB_ST_ARG;
    if (!hb_host_fits(头址, 12u)) { free(路径); return HB_ST_ARG; }
    if (!hb_host_is_dir(路径)) { free(路径); return HB_ST_OPEN; }
    清单 = hb_host_listdir(路径);
    free(路径);
    if (清单 == NULL) return HB_ST_IO;
    状态 = hb_host_put_string(参数区, 头址, (const hb_u8 *)清单, strlen(清单), 上限);
    free(清单);
    return 状态;
}

static hb_u32 hb_host_环境变量(hb_u32 参数区)
{
    char *名 = hb_host_argstr(参数区, 0u);
    char *值;
    hb_u32 头址 = hb_host_rd(参数区 + 8u);
    hb_u32 上限 = hb_host_rd(参数区 + 20u);
    if (名 == NULL) return HB_ST_ARG;
    if (!hb_host_fits(头址, 12u)) { free(名); return HB_ST_ARG; }
    值 = hb_host_getenv(名);
    free(名);
    if (值 == NULL) return HB_ST_OPEN;
    {
        hb_u32 状态 = hb_host_put_string(参数区, 头址, (const hb_u8 *)值, strlen(值), 上限);
        free(值);
        return 状态;
    }
}

static hb_u32 hb_host_删目录(hb_u32 参数区)
{
    char *路径 = hb_host_argstr(参数区, 0u);
    char *清单;
    hb_u32 状态;
    if (路径 == NULL) return HB_ST_ARG;
    if (hb_host_is_file(路径)) { free(路径); return HB_ST_NOTDIR; }
    if (!hb_host_is_dir(路径)) { free(路径); return HB_ST_OPEN; }
    清单 = hb_host_listdir(路径);
    if (清单 == NULL) { free(路径); return HB_ST_IO; }
    if (清单[0] != '\0') { free(清单); free(路径); return HB_ST_NOTEMPTY; }
    free(清单);
    状态 = hb_host_rmdir(路径);
    free(路径);
    return 状态;
}

static hb_u32 hb_host_移动到(hb_u32 参数区)
{
    char *旧路径 = hb_host_argstr(参数区, 0u);
    char *新路径;
    char *目录;
    hb_u32 状态 = HB_ST_OPEN;
    if (旧路径 == NULL) return HB_ST_ARG;
    新路径 = hb_host_argstr(参数区, 2u);
    if (新路径 == NULL) { free(旧路径); return HB_ST_ARG; }
    if (hb_host_is_file(新路径) || hb_host_is_dir(新路径)) 状态 = HB_ST_EXISTS;
    else if (hb_host_is_file(旧路径) || hb_host_is_dir(旧路径))
    {
        目录 = hb_host_parent(新路径);
        if (目录 != NULL) { hb_host_makedirs(目录); free(目录); }
        状态 = hb_host_rename(旧路径, 新路径);
    }
    free(旧路径);
    free(新路径);
    return 状态;
}

static hb_u32 hb_host_复制到(hb_u32 参数区)
{
    char *源 = hb_host_argstr(参数区, 0u);
    char *目标;
    char *目录;
    hb_u32 状态 = HB_ST_OPEN;
    if (源 == NULL) return HB_ST_ARG;
    目标 = hb_host_argstr(参数区, 2u);
    if (目标 == NULL) { free(源); return HB_ST_ARG; }
    if (hb_host_is_file(目标) || hb_host_is_dir(目标)) 状态 = HB_ST_EXISTS;
    else if (hb_host_is_file(源))
    {
        目录 = hb_host_parent(目标);
        if (目录 != NULL) { hb_host_makedirs(目录); free(目录); }
        状态 = hb_host_copy_file(源, 目标);
    }
    else if (hb_host_is_dir(源)) 状态 = hb_host_copy_tree(源, 目标);
    free(源);
    free(目标);
    return 状态;
}

static hb_u32 hb_host_读文件块(hb_u32 参数区)
{
    char *路径;
    hb_u32 头址, 偏移, 上限;
    hb_buf 块;
    hb_u32 状态;
    if (!hb_host_fits(参数区, 28u)) return HB_ST_ARG;
    路径 = hb_host_argstr(参数区, 0u);
    if (路径 == NULL) return HB_ST_ARG;
    头址 = hb_host_rd(参数区 + 8u);
    if (!hb_host_fits(头址, 12u)) { free(路径); return HB_ST_ARG; }
    偏移 = hb_host_rd(参数区 + 20u);
    上限 = hb_host_rd(参数区 + 24u);
    if (上限 == 0u) 上限 = 4096u;                 /* 兜底:不给上限就一块 4KB */
    if (!hb_host_is_file(路径)) { free(路径); return HB_ST_OPEN; }
    memset(&块, 0, sizeof 块);
    状态 = hb_host_read_chunk(路径, 偏移, 上限, &块);
    free(路径);
    if (状态 == HB_ST_EOF) { hb_host_put_string(参数区, 头址, NULL, 0, 0u); return HB_ST_EOF; }
    if (状态 != HB_ST_OK) { hb_buf_free(&块); return 状态; }
    状态 = hb_host_put_string(参数区, 头址, 块.p, 块.n, 0u);
    hb_buf_free(&块);
    return 状态;
}

/* ---- G3 流式读写(48~54):参数区与 `宿主能力C.功能_*` 那七条逐条对齐 ---- */

static hb_u32 hb_host_打开流(hb_u32 参数区, int 写)
{
    char *路径;
    hb_u32 状态, 句柄 = 0u;
    if (!hb_host_fits(参数区, 16u)) return HB_ST_ARG;
    路径 = hb_host_argstr(参数区, 0u);
    if (路径 == NULL) return HB_ST_ARG;
    状态 = hb_host_开流(路径, 写, &句柄);
    free(路径);
    hb_host_wr(参数区 + 8u, 状态 == HB_ST_OK ? 句柄 : 0xFFFFFFFFu);
    return 状态;
}

/* 读一块:请求块与 `读文件块` 同形(+8 头址 / +12 池指针槽 / +16 池末槽 / +20 上限),
   只把"+0 路径址 +4 路径长"换成"+0 句柄",写回改成"+24 读到的字节数"(失败 -1)。 */
static hb_u32 hb_host_读一块(hb_u32 参数区)
{
    hb_u32 句柄, 头址, 上限, 读到 = 0u;
    hb_buf 块;
    int 格;
    if (!hb_host_fits(参数区, 28u)) return HB_ST_ARG;
    句柄 = hb_host_rd(参数区);
    头址 = hb_host_rd(参数区 + 8u);
    if (!hb_host_fits(头址, 12u)) { hb_host_wr(参数区 + 24u, 0xFFFFFFFFu); return HB_ST_ARG; }
    格 = hb_host_流格(句柄, NULL);
    if (格 < 0) { hb_host_wr(参数区 + 24u, 0xFFFFFFFFu); return HB_ST_BADHANDLE; }

    memset(&块, 0, sizeof 块);
    上限 = hb_host_rd(参数区 + 20u);
    if (上限 == 0u) 上限 = 4096u;
    if (hb_buf_room(&块, 上限))
    {
        /* 与 C# 侧 `FileStream.Read` **同口径**:一次调用只 fread 一次,它给几个字节就用几个 ——
           不主动把这一块填满。两边都这么干,"上一次 Read 给了几个字节"才可能逐个对上
           (实测:一边循环填满、一边单次返回,短读切法必然不同,读回来的计数也会跟着差)。 */
        size_t 读 = fread(块.p, 1, (size_t)上限, hb_流表[格]);
        块.n = 读;
    }
    /* fread 短读(读到一半出错)与"正好到末尾"在这条口径下都是"这一轮到此为止" ——
       统一给 0(与 C# 侧那个 catch 给 0 同一档),免得调用方的分块循环退化成死循环 */
    读到 = (hb_u32)块.n;
    /* ⚠ **字节数先写回**:`hb_host_put_string` 会在池里换块 / 搬数据,而池块完全可能盖住请求块自己
       (C# 侧实测踩过:先写内容再写字节数,`块.长度()` 读到 0 —— 那 4 字节被字符串的数据盖掉了) */
    hb_host_wr(参数区 + 24u, 读到);
    if (读到 > 0u)
    {
        hb_u32 写状态 = hb_host_put_string(参数区, 头址, 块.p, 块.n, 0u);
        hb_buf_free(&块);
        if (写状态 != HB_ST_OK) { hb_host_wr(参数区 + 24u, 0xFFFFFFFFu); return 写状态; }
    }
    else
    {
        hb_buf_free(&块);
        hb_host_put_string(参数区, 头址, NULL, 0, 0u);      /* 到末尾:把变量清空(与 `读文件块` 同口径) */
    }
    return HB_ST_OK;
}

/* 写一块:参数1 句柄 / 参数2 数据址 / 参数3 字节数;参数4 写回 = 真写下去几个字节(失败 -1) */
static hb_u32 hb_host_写一块(hb_u32 参数区)
{
    hb_u32 句柄, 数据址, 字节数, 写回;
    long 写;
    int 格, 是写 = 0;
    if (!hb_host_fits(参数区, 16u)) return HB_ST_ARG;
    写回 = 参数区 + 12u;
    句柄 = hb_host_rd(参数区);
    数据址 = hb_host_rd(参数区 + 4u);
    字节数 = hb_host_rd(参数区 + 8u);
    格 = hb_host_流格(句柄, &是写);
    if (格 < 0) { hb_host_wr(写回, 0xFFFFFFFFu); return HB_ST_BADHANDLE; }
    /* 数据地址越界 / 拿读流来写:与 C# 侧同一个状态码(那边也是"参数不合法") */
    if (!hb_host_fits(数据址, 字节数) || !是写) { hb_host_wr(写回, 0xFFFFFFFFu); return HB_ST_ARG; }
    写 = hb_host_往流里写(句柄, hb_mem + 数据址, 字节数);
    hb_host_wr(写回, 写 < 0 ? 0xFFFFFFFFu : (hb_u32)写);
    return 写 < 0 ? HB_ST_IO : HB_ST_OK;
}

/* 定位:参数1 句柄 / 参数2 绝对偏移(从文件开头算) */
static hb_u32 hb_host_定位(hb_u32 参数区)
{
    hb_u32 句柄, 偏移;
    int 格;
    if (!hb_host_fits(参数区, 12u)) return HB_ST_ARG;
    句柄 = hb_host_rd(参数区);
    偏移 = hb_host_rd(参数区 + 4u);
    格 = hb_host_流格(句柄, NULL);
    if (格 < 0) return HB_ST_BADHANDLE;
    if (fseek(hb_流表[格], (long)偏移, SEEK_SET) != 0) return HB_ST_IO;
    return HB_ST_OK;
}

/*
   ═══ G3 域 6:运行时那四条(睡眠 / 冲刷 / 改名 / 临时目录)═══
   与 `引擎\宿主能力C.cs` 的同名处理函数**逐条同口径** —— 改这边必须回头看那边:
     · 睡眠:负数(> 0x7FFFFFFF)给"参数不合法",**不当 0 睡**;阻塞到底(被信号打断就少睡一点,不重试);
     · 冲刷:只认写流(读流给 9);`fflush` + `_commit` / `fsync` = C# 的 `Flush(flushToDisk: true)`;
     · 改名:文件走**覆盖式改名**(Windows `MoveFileEx(REPLACE_EXISTING)` / POSIX `rename` 本来就替换),
       目录碰上已存在的目标给 14(目录不替换);
     · 临时目录:Windows `GetTempPathW` / POSIX `TMPDIR` 或 `/tmp`,**尾部隔符去掉**(留一个字符:
       `/` 这种根目录本身不能被削成空串)—— 与 C# `Path.GetTempPath()` + 去尾同一个口径。
*/

/* 睡 毫秒(阻塞)。毫秒 > 0x7FFFFFFF(当无符号读回来就是"负数")→ 参数不合法 */
static hb_u32 hb_host_睡眠(hb_u32 参数区)
{
    hb_u32 毫秒 = hb_host_rd(参数区);
    if (毫秒 > 0x7FFFFFFFu) return HB_ST_ARG;
#ifdef _WIN32
    Sleep((DWORD)毫秒);
#else
    {
        struct timespec 要, 剩;
        要.tv_sec = (time_t)(毫秒 / 1000u);
        要.tv_nsec = (long)((毫秒 % 1000u) * 1000000u);
        /* 被信号打断就返回来(EINTR):与 C# `Thread.Sleep` 同档 —— 不替调用方重试 */
        while (nanosleep(&要, &剩) != 0 && errno == EINTR) 要 = 剩;
    }
#endif
    return HB_ST_OK;
}

/* 把一条**写流**的缓冲推下去并落盘(流还开着)。读流给 9(它没有要落盘的写缓冲) */
static hb_u32 hb_host_冲刷(hb_u32 参数区)
{
    hb_u32 句柄 = hb_host_rd(参数区);
    int 是写 = 0;
    int 格 = hb_host_流格(句柄, &是写);
    if (格 < 0) return HB_ST_BADHANDLE;
    if (!是写) return HB_ST_ARG;
    if (fflush(hb_流表[格]) != 0) return HB_ST_IO;
#ifdef _WIN32
    _commit(_fileno(hb_流表[格]));
#else
    fsync(fileno(hb_流表[格]));
#endif
    return HB_ST_OK;
}

/* 覆盖式改名:Windows 上要 `MoveFileExW(REPLACE_EXISTING)`(`_wrename` 目标在就失败 —— 那是 `移动到` 的口径) */
static int hb_host_替换改名(const char *旧路径, const char *新路径)
{
#ifdef _WIN32
    wchar_t *旧 = hb_host_w(旧路径);
    wchar_t *新 = hb_host_w(新路径);
    int 成;
    if (旧 == NULL || 新 == NULL) { free(旧); free(新); return HB_ST_IO; }
    成 = MoveFileExW(旧, 新, MOVEFILE_REPLACE_EXISTING) ? 0 : 1;
    free(旧);
    free(新);
    return 成 == 0 ? HB_ST_OK : HB_ST_IO;
#else
    return rename(旧路径, 新路径) == 0 ? HB_ST_OK : HB_ST_IO;   /* POSIX 的 rename 本来就替换 */
#endif
}

/* 改名(覆盖式替换)。旧路径不存在 → 2;目录碰上已存在的目标 → 14 */
static hb_u32 hb_host_改名(hb_u32 参数区)
{
    char *旧路径 = hb_host_argstr(参数区, 0u);
    char *新路径;
    hb_u32 状态;
    if (旧路径 == NULL) return HB_ST_ARG;
    新路径 = hb_host_argstr(参数区, 2u);
    if (新路径 == NULL) { free(旧路径); return HB_ST_ARG; }
    if (!hb_host_is_file(旧路径) && !hb_host_is_dir(旧路径)) 状态 = HB_ST_OPEN;
    else if ((hb_host_is_file(新路径) || hb_host_is_dir(新路径))
             && (hb_host_is_dir(旧路径) || hb_host_is_dir(新路径))) 状态 = HB_ST_EXISTS;
    else
    {
        char *目录 = hb_host_parent(新路径);
        if (目录 != NULL) { hb_host_makedirs(目录); free(目录); }
        状态 = hb_host_替换改名(旧路径, 新路径);
    }
    free(旧路径);
    free(新路径);
    return 状态;
}

/* 本机临时目录写进那个字符串变量(尾分隔符去掉;取不到给空串) */
static hb_u32 hb_host_临时目录(hb_u32 参数区)
{
    hb_u32 头址, 上限, 状态;
    char *原始 = NULL;
    char *净;
    size_t 长;
    if (!hb_host_fits(参数区, 28u)) return HB_ST_ARG;
    头址 = hb_host_rd(参数区 + 8u);
    if (!hb_host_fits(头址, 12u)) return HB_ST_ARG;
    上限 = hb_host_rd(参数区 + 20u);
#ifdef _WIN32
    {
        wchar_t 宽[1024];
        DWORD 长宽 = GetTempPathW(1024u, 宽);
        if (长宽 > 0u && 长宽 < 1024u) 原始 = hb_host_u8(宽);
    }
#else
    {
        const char *取值 = getenv("TMPDIR");
        原始 = hb_host_dup(取值 != NULL && 取值[0] != '\0' ? 取值 : "/tmp");
    }
#endif
    if (原始 == NULL) 原始 = hb_host_dup("");
    净 = 原始;
    长 = strlen(净);
    while (长 > 1u && (净[长 - 1u] == '\\' || 净[长 - 1u] == '/')) 净[--长] = '\0';
    状态 = hb_host_put_string(参数区, 头址, (const hb_u8 *)净, 长, 上限);
    free(原始);
    return 状态;
}

/* ================= G4 批 23:剪贴板 / 提示框 / 选文件(80~85)=================
   与 C# 侧 `宿主能力C` 那六条**逐字段同形**;平台那一份实现只有 Windows:
   剪贴板走 `OpenClipboard` 族、提示框走 `MessageBoxW`、挑文件走 `GetOpenFileNameW` / `GetSaveFileNameW`
   (user32 + comdlg32,都是系统自带 ⇒ "一条命令行直接编出 exe"的口径不变)。
   **别的平台如实给 `HB_ST_UNSUP`** —— 与"没有 X 就不开窗"同一个口径:不去猜、不去拼一个假对话框。

   ⚠ 与 C# 那边**同一条闸**:独立 exe / 命令行**不许弹窗**(硬规矩 6:一个没人点的模态框会把整趟挂住),
   所以 82 / 83 / 84 一律给"不支持";剪贴板那三条**照旧是好的**(它不弹窗、不用人点)。 */

/* UTF-8 → 宽串(用完 `free`);失败给 NULL。照 `hb_win_宽串` 那一套(同一个 API:`MultiByteToWideChar`) */
#ifdef _WIN32
static wchar_t *hb_pop_宽串(const hb_u8 *文本, size_t 字节长)
{
    int 宽数;
    wchar_t *宽;
    if (文本 == NULL) return NULL;
    宽数 = MultiByteToWideChar(CP_UTF8, 0, (const char *)文本, (int)字节长, NULL, 0);
    if (宽数 < 0) return NULL;
    宽 = (wchar_t *)malloc(((size_t)宽数 + 1u) * sizeof(wchar_t));
    if (宽 == NULL) return NULL;
    if (宽数 > 0) MultiByteToWideChar(CP_UTF8, 0, (const char *)文本, (int)字节长, 宽, 宽数);
    宽[宽数] = L'\0';
    return 宽;
}

/* 开一次剪贴板:别的进程正开着它是常态 ⇒ 照规矩重试几次再认输 */
static int hb_pop_开剪贴板(void)
{
    int 试;
    for (试 = 0; 试 < 10; 试++)
    {
        if (OpenClipboard(NULL)) return 1;
        Sleep(10);
    }
    return 0;
}

/* 读剪贴板里的文本(UTF-8;`*出长` 给出字节数)。没有文本 ⇒ 空串 + 返回 1(那不是错) */
static int hb_pop_读剪贴板(hb_u8 **出文, size_t *出长)
{
    HGLOBAL 块;
    const wchar_t *指针;
    int 宽数, 字节数;
    *出文 = NULL;
    *出长 = 0;
    if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return 1;      /* 没有文本:成功 + 空串 */
    if (!hb_pop_开剪贴板()) return 0;
    块 = GetClipboardData(CF_UNICODETEXT);
    if (块 == NULL) { CloseClipboard(); return 1; }
    指针 = (const wchar_t *)GlobalLock(块);
    if (指针 == NULL) { CloseClipboard(); return 0; }
    宽数 = (int)wcslen(指针);
    字节数 = WideCharToMultiByte(CP_UTF8, 0, 指针, 宽数, NULL, 0, NULL, NULL);
    if (字节数 < 0) { GlobalUnlock(块); CloseClipboard(); return 0; }
    *出文 = (hb_u8 *)malloc((size_t)字节数 + 1u);
    if (*出文 == NULL) { GlobalUnlock(块); CloseClipboard(); return 0; }
    if (字节数 > 0) WideCharToMultiByte(CP_UTF8, 0, 指针, 宽数, (char *)*出文, 字节数, NULL, NULL);
    (*出文)[字节数] = 0;
    *出长 = (size_t)字节数;
    GlobalUnlock(块);
    CloseClipboard();
    return 1;
}

/* 写剪贴板(覆盖)。成功 1 / 失败 0 */
static int hb_pop_写剪贴板(const hb_u8 *文本, size_t 字节长)
{
    wchar_t *宽 = hb_pop_宽串(文本, 字节长);
    HGLOBAL 块;
    void *指针;
    size_t 字节;
    int 交出去了 = 0;
    if (宽 == NULL) return 0;
    字节 = (wcslen(宽) + 1u) * sizeof(wchar_t);
    块 = GlobalAlloc(GMEM_MOVEABLE, 字节);
    if (块 == NULL) { free(宽); return 0; }
    指针 = GlobalLock(块);
    if (指针 == NULL) { GlobalFree(块); free(宽); return 0; }
    memcpy(指针, 宽, 字节);
    GlobalUnlock(块);
    free(宽);
    if (!hb_pop_开剪贴板()) { GlobalFree(块); return 0; }
    if (EmptyClipboard() && SetClipboardData(CF_UNICODETEXT, 块) != NULL) 交出去了 = 1;
    CloseClipboard();
    if (!交出去了) GlobalFree(块);          /* 交出去了那块内存归系统,不许 free */
    return 交出去了;
}

/* 清空剪贴板 */
static int hb_pop_清剪贴板(void)
{
    int 成;
    if (!hb_pop_开剪贴板()) return 0;
    成 = EmptyClipboard() ? 1 : 0;
    CloseClipboard();
    return 成;
}
#endif /* _WIN32 */

/* 读剪贴板(80):头址 / 池两槽 / 状态 */
static hb_u32 hb_host_读剪贴板(hb_u32 参数区)
{
    hb_u32 头址, 状态;
#ifndef _WIN32
    (void)参数区;
    return HB_ST_UNSUP;
#else
    hb_u8 *文 = NULL;
    size_t 长 = 0;
    if (!hb_host_fits(参数区, 28u)) return HB_ST_ARG;
    头址 = hb_host_rd(参数区);
    if (!hb_host_fits(头址, 12u)) return HB_ST_ARG;
    if (!hb_pop_读剪贴板(&文, &长)) return HB_ST_IO;
    状态 = hb_host_put_string_池(头址, hb_host_rd(参数区 + 4u), hb_host_rd(参数区 + 8u),
                                文 == NULL ? (const hb_u8 *)"" : 文, 长, 0u);
    free(文);
    return 状态;
#endif
}

/* 写剪贴板(81):内容址 / 内容长 */
static hb_u32 hb_host_写剪贴板(hb_u32 参数区)
{
#ifndef _WIN32
    (void)参数区;
    return HB_ST_UNSUP;
#else
    hb_u32 址, 长;
    if (!hb_host_fits(参数区, 28u)) return HB_ST_ARG;
    址 = hb_host_rd(参数区);
    长 = hb_host_rd(参数区 + 4u);
    if (!hb_host_fits(址, 长)) return HB_ST_ARG;
    return hb_pop_写剪贴板((const hb_u8 *)hb_mem + 址, (size_t)长) ? HB_ST_OK : HB_ST_IO;
#endif
}

/* 清空剪贴板(85) */
static hb_u32 hb_host_清空剪贴板(hb_u32 参数区)
{
#ifndef _WIN32
    (void)参数区;
    return HB_ST_UNSUP;
#else
    if (!hb_host_fits(参数区, 16u)) return HB_ST_ARG;
    return hb_pop_清剪贴板() ? HB_ST_OK : HB_ST_IO;
#endif
}

/* 提示框(82):标题 / 正文 / 按钮 → 答案。
   ⚠ **本宿主不弹**(独立 exe / 命令行:硬规矩 6)⇒ 一律 `HB_ST_UNSUP`,
   而答案那一格**照 C# 那边先写哨兵** 0xFFFFFFFF(两边读到的字节才一样)。 */
static hb_u32 hb_host_提示框(hb_u32 参数区)
{
    if (!hb_host_fits(参数区, 28u)) return HB_ST_ARG;
    hb_host_wr(参数区 + 20u, 0xFFFFFFFFu);
    return HB_ST_UNSUP;
}

/* 选文件 / 另存文件(83 / 84):本宿主同样不弹(见上)。
   ⚠ 这里**只把路径那一格清空**(写空串得走池那一套 ⇒ 得先验长度),
   不给"半截结果";两个出口与 C# 那边同形:状态码说"没有这一手"。 */
static hb_u32 hb_host_选文件(hb_u32 参数区)
{
    hb_u32 头址;
    if (!hb_host_fits(参数区, 36u)) return HB_ST_ARG;
    头址 = hb_host_rd(参数区 + 16u);
    if (!hb_host_fits(头址, 12u)) return HB_ST_ARG;
    return HB_ST_UNSUP;
}

/* ================= G3 域 5:异步输入输出(40 / 41 / 42)=================
   与 C# 侧 `宿主能力C` 的任务表**同一份口径**(对拍比的就是"谁在第几格、任务号是多少、结果是什么"):
     · 任务号 = `(下标 << 16) | 版本`,32 格,版本从 1 起;收走 / 取消 ⇒ 版本 +1(旧号当场作废);
     · 一格从 `发起` 占到最后一次 `任务就绪`(或者 `取消任务`)—— **跑完不自己放格子**,
       所以"32 格全占着"是**确定的**:发起 32 个不查就绪,第 33 个必然给 -1;
     · 后台线程**一个字节都不碰 `hb_mem`**:它只把结果放进自己那一格,
       VM 内存的搬运全在 `hb_host_任务就绪`(主线程)里 —— 与 C# 那边"线程只写托管对象"同一条;
     · **连接(种类 3)本宿主不挂**(见上面 `HB_F_STARTASYNC` 那段):状态 1、任务号写 -1,不发线程。
   线程:**零依赖** —— Windows `CreateThread`(kernel32,不必引库)/ POSIX `pthread_create`
   (glibc ≥ 2.34 起就在 libc 里;本仓库的 Linux 验证镜像是 Ubuntu,够新)+ 一把互斥。 */

#define HB_任务表格数 32
#define HB_任务_空    0
#define HB_任务_在跑  1
#define HB_任务_好了  2

#define HB_种类_读文件 1
#define HB_种类_写文件 2
#define HB_种类_连接   3

typedef struct hb_任务格
{
    int 状态;                 /* HB_任务_空 / 在跑 / 好了 */
    hb_u32 版本;              /* 从 1 起;收走 / 取消 各 +1 */
    int 要取消;
    int 种类;
    hb_u32 结果状态;
    hb_u32 结果数字;          /* 读 / 写:字节数(本宿主连接不挂) */
    hb_u8 *结果数据;          /* 读文件那一趟的字节(malloc;交出去或丢掉时 free) */
    size_t 结果长;
    hb_u32 头址, 池指针槽, 池末槽, 上限;
    hb_u32 端口;
    char *路径;               /* 后台线程要用的路径(malloc;线程收尾时 free) */
    hb_u8 *写内容;            /* 写文件那一趟的字节(malloc;线程收尾时 free) */
    size_t 写长;
} hb_任务格;

static hb_任务格 hb_任务表[HB_任务表格数];

#ifdef _WIN32
static CRITICAL_SECTION hb_任务锁;
static volatile LONG hb_任务锁备好 = 0;
static void hb_任务上锁(void)
{
    if (hb_任务锁备好 == 0) { InitializeCriticalSection(&hb_任务锁); hb_任务锁备好 = 1; }
    EnterCriticalSection(&hb_任务锁);
}
static void hb_任务解锁(void) { LeaveCriticalSection(&hb_任务锁); }
#else
static pthread_mutex_t hb_任务锁 = PTHREAD_MUTEX_INITIALIZER;
static void hb_任务上锁(void) { pthread_mutex_lock(&hb_任务锁); }
static void hb_任务解锁(void) { pthread_mutex_unlock(&hb_任务锁); }
#endif

typedef struct hb_任务活 { int 格; } hb_任务活;

/* 后台线程收尾:把结果放进格子(取消过就丢掉、把格子还掉);`数据` 的所有权一起交过来 */
static void hb_任务收尾(int 格号, hb_u32 状态, hb_u32 数字, hb_u8 *数据, size_t 数据长)
{
    hb_任务格 *g = &hb_任务表[格号];
    hb_任务上锁();
    if (g->要取消 || g->状态 != HB_任务_在跑)
    {
        g->状态 = HB_任务_空;
        hb_任务解锁();
        if (数据 != NULL) free(数据);
        return;
    }
    g->结果状态 = 状态;
    g->结果数字 = 数字;
    g->结果数据 = 数据;
    g->结果长 = 数据长;
    g->状态 = HB_任务_好了;
    hb_任务解锁();
}

/* 后台那一趟(真阻塞的那几条) */
static void hb_任务干活(void *参数)
{
    hb_任务活 *活 = (hb_任务活 *)参数;
    int 格号 = 活->格;
    hb_任务格 *g;
    free(活);
    g = &hb_任务表[格号];

    if (g->种类 == HB_种类_读文件)
    {
        hb_buf 内容;
        hb_u32 状态;
        memset(&内容, 0, sizeof 内容);
        if (!hb_host_is_file(g->路径)) 状态 = HB_ST_OPEN;
        else
        {
            状态 = hb_host_read_chunk(g->路径, 0u, g->上限, &内容);
            if (状态 == HB_ST_EOF) 状态 = HB_ST_OK;   /* 空文件 = 成功 + 0 字节(与 C# 侧 `读字节块` 同) */
        }
        free(g->路径); g->路径 = NULL;
        if (状态 == HB_ST_OK) hb_任务收尾(格号, 状态, (hb_u32)内容.n, 内容.p, 内容.n);
        else
        {
            hb_buf_free(&内容);
            hb_任务收尾(格号, 状态, 0u, NULL, 0u);
        }
        return;
    }

    if (g->种类 == HB_种类_写文件)
    {
        char *目录 = hb_host_parent(g->路径);
        hb_u32 状态;
        size_t 写长 = g->写长;
        if (目录 != NULL) { hb_host_makedirs(目录); free(目录); }
        /* 与 C# 的 `虚拟文件C.写字节` 同口径:覆盖写、缺的目录先建出来;写完把字节数当结果交出去 */
        状态 = hb_host_write_file(g->路径, g->写内容, 写长, 0);
        free(g->路径); g->路径 = NULL;
        free(g->写内容); g->写内容 = NULL;
        hb_任务收尾(格号, 状态, 状态 == HB_ST_OK ? (hb_u32)写长 : 0u, NULL, 0u);
        return;
    }

    /* 种类 3 连接:本宿主不挂(发起那一头就返回了,走不到这儿)—— 兜底按"没挂能力"收场 */
    free(g->路径); g->路径 = NULL;
    hb_任务收尾(格号, HB_ST_NOCAP, 0u, NULL, 0u);
}

/* 起一个后台线程(跑完自生自灭,不等它);起不来给 0 */
static int hb_任务起线程(int 格号)
{
    hb_任务活 *活 = (hb_任务活 *)malloc(sizeof(hb_任务活));
    if (活 == NULL) return 0;
    活->格 = 格号;
#ifdef _WIN32
    {
        HANDLE 线 = CreateThread(NULL, 0, (LPTHREAD_START_ROUTINE)hb_任务干活, 活, 0u, NULL);
        if (线 == NULL) { free(活); return 0; }
        CloseHandle(线);
        return 1;
    }
#else
    {
        pthread_t 线;
        if (pthread_create(&线, NULL, (void *(*)(void *))hb_任务干活, 活) != 0) { free(活); return 0; }
        pthread_detach(线);
        return 1;
    }
#endif
}

/* 把任务表压回"刚启动"(没在 C# 那边暴露成能力,但留着与"一个工程一个进程"的口径对齐) */
static void hb_host_清任务表(void)
{
    int i;
    hb_任务上锁();
    for (i = 0; i < HB_任务表格数; i++)
    {
        hb_任务格 *g = &hb_任务表[i];
        if (g->路径 != NULL) { free(g->路径); g->路径 = NULL; }
        if (g->写内容 != NULL) { free(g->写内容); g->写内容 = NULL; }
        if (g->结果数据 != NULL) { free(g->结果数据); g->结果数据 = NULL; }
        g->状态 = HB_任务_空;
        g->版本 = 1u;                 /* 版本从 1 起(与 C# 侧"整格丢掉、新格版本 1"逐字对齐;0 = 无效号) */
        g->要取消 = 0;
    }
    hb_任务解锁();
}

/* 发起异步(请求块 40 字节:+0 种类 / +4 参数一址 / +8 参数一长 / +12 参数二址 / +16 参数二长 / +20 上限
   / +24 任务号(写回)/ +28 头址 / +32 池指针槽 / +36 池末槽) */
static hb_u32 hb_host_发起异步(hb_u32 参数区)
{
    hb_u32 种类, 上限, 任务槽 = 参数区 + 24u;
    char *参数一, *参数二;
    int 空, i;
    hb_任务格 *g;
    if (!hb_host_fits(参数区, 40u)) return HB_ST_ARG;
    hb_host_wr(任务槽, 0xFFFFFFFFu);                      /* 排不上就留 -1(与网络那一族"先写 -1"同) */

    种类 = hb_host_rd(参数区);
    参数一 = hb_host_argstr(参数区, 1u);
    参数二 = hb_host_argstr(参数区, 3u);
    上限 = hb_host_rd(参数区 + 20u);
    if (参数一 == NULL || 参数二 == NULL) { free(参数一); free(参数二); return HB_ST_ARG; }

    if (种类 == HB_种类_连接)
    {
        /* 要白名单的两族同一条 D6 口径:独立 exe 带不了 `.hbp` 的 `网址` 表 ⇒ 本宿主不挂 */
        free(参数一); free(参数二);
        return HB_ST_NOCAP;
    }
    if (种类 != HB_种类_读文件 && 种类 != HB_种类_写文件) { free(参数一); free(参数二); return HB_ST_ARG; }
    if (上限 > 1024u * 1024u) { free(参数一); free(参数二); return HB_ST_ARG; }
    if (种类 == HB_种类_读文件)
    {
        if (上限 == 0u) 上限 = 4096u;                          /* 兜底:不给上限就一块 4KB(与 `读一块` 同) */
        if (!hb_host_fits(hb_host_rd(参数区 + 28u), 12u)) { free(参数一); free(参数二); return HB_ST_ARG; }
    }
    else if (上限 == 0u)
    {
        上限 = (hb_u32)strlen(参数二);                           /* 0 = 整段都写(与 `写一块` 同;路径与内容都是 UTF-8 字节) */
    }

    hb_任务上锁();                                          /* 顺手把锁备好(第一个线程起来之前) */
    空 = -1;
    for (i = 0; i < HB_任务表格数; i++)
        if (hb_任务表[i].状态 == HB_任务_空) { 空 = i; break; }
    if (空 < 0) { hb_任务解锁(); free(参数一); free(参数二); return HB_ST_TASKMANY; }
    g = &hb_任务表[空];
    g->状态 = HB_任务_在跑;
    g->要取消 = 0;
    g->种类 = (int)种类;
    g->结果状态 = HB_ST_OK;
    g->结果数字 = 0u;
    g->结果数据 = NULL;
    g->结果长 = 0u;
    g->头址 = hb_host_rd(参数区 + 28u);
    g->池指针槽 = hb_host_rd(参数区 + 32u);
    g->池末槽 = hb_host_rd(参数区 + 36u);
    g->上限 = 上限;
    g->端口 = 0u;
    g->路径 = hb_host_dup(参数一);
    g->写内容 = NULL;
    g->写长 = 0u;
    if (种类 == HB_种类_写文件 && 上限 > 0u)
    {
        g->写内容 = (hb_u8 *)malloc((size_t)上限);
        if (g->写内容 != NULL && 参数二[0] != '\0')
        {
            size_t 有 = strlen(参数二);
            if (有 > (size_t)上限) 有 = (size_t)上限;
            memcpy(g->写内容, 参数二, 有);
            g->写长 = 有;
        }
    }
    hb_host_wr(任务槽, ((hb_u32)空 << 16) | (g->版本 & 0xFFFFu));
    hb_任务解锁();
    free(参数一); free(参数二);

    if (!hb_任务起线程(空))
    {
        /* 线程起不来:这个任务当场算失败(格子还回去,任务号作废) */
        hb_任务上锁();
        g->状态 = HB_任务_空;
        g->版本 = g->版本 + 1u;
        if (g->路径 != NULL) { free(g->路径); g->路径 = NULL; }
        if (g->写内容 != NULL) { free(g->写内容); g->写内容 = NULL; }
        hb_任务解锁();
        hb_host_wr(任务槽, 0xFFFFFFFFu);
        return HB_ST_IO;
    }
    return HB_ST_OK;
}

/* 任务就绪(请求块 16 字节:+0 任务号 / +4 状态(写回)/ +8 字节数(写回)/ +12 空) */
static hb_u32 hb_host_任务就绪(hb_u32 参数区)
{
    hb_u32 号, 下标, 版本, 状态, 数字, 结果;
    hb_u32 头址, 池指针槽, 池末槽, 上限;
    hb_任务格 *g;
    hb_u8 *数据;
    size_t 数据长;
    数据 = NULL; 数据长 = 0u;
    if (!hb_host_fits(参数区, 16u)) return HB_ST_ARG;
    hb_host_wr(参数区 + 4u, 0xFFFFFFFFu);                  /* 作废 / 号非法时两格都是 -1(确定值,便于对拍) */
    hb_host_wr(参数区 + 8u, 0xFFFFFFFFu);

    号 = hb_host_rd(参数区);
    下标 = 号 >> 16;
    版本 = 号 & 0xFFFFu;
    if (下标 >= (hb_u32)HB_任务表格数 || 版本 == 0u) return HB_ST_BADHANDLE;   /* 22:号根本不成形 */

    hb_任务上锁();
    g = &hb_任务表[下标];
    if (g->状态 == HB_任务_空 || (g->版本 & 0xFFFFu) != 版本) { hb_任务解锁(); return 3u; }
    if (g->状态 == HB_任务_在跑) { hb_任务解锁(); hb_host_wr(参数区 + 4u, 0u); hb_host_wr(参数区 + 8u, 0u); return HB_ST_OK; }
    if (g->要取消) { hb_任务解锁(); return 3u; }

    状态 = g->结果状态;
    数字 = g->结果数字;
    数据 = g->结果数据;
    数据长 = g->结果长;
    头址 = g->头址;
    池指针槽 = g->池指针槽;
    池末槽 = g->池末槽;
    上限 = g->上限;
    g->结果数据 = NULL;
    g->结果长 = 0u;
    g->版本 = g->版本 + 1u;                                /* 收走:这个号当场作废 */
    g->状态 = HB_任务_空;
    hb_任务解锁();

    /* ⚠ 内容**先**写、两个标量**后**写:`hb_host_put_string_池` 会在池里换块搬数据,
       两个标量放最后写才不会被它盖掉(与 `读一块` 那个"绝不能写 +24"是同一个坑) */
    if (状态 == HB_ST_OK && 数据 != NULL)
    {
        hb_u32 写状态 = hb_host_put_string_池(头址, 池指针槽, 池末槽, 数据, 数据长, 上限);
        if (写状态 != HB_ST_OK) 状态 = 写状态;
    }
    if (数据 != NULL) free(数据);

    结果 = 状态 == HB_ST_OK ? 1u : 2u;
    hb_host_wr(参数区 + 4u, 状态);
    hb_host_wr(参数区 + 8u, 数字);
    return 结果;
}

/* 取消任务(请求块 8 字节:+0 任务号 / +4 空) */
static hb_u32 hb_host_取消任务(hb_u32 参数区)
{
    hb_u32 号, 下标, 版本;
    hb_任务格 *g;
    if (!hb_host_fits(参数区, 8u)) return HB_ST_ARG;
    号 = hb_host_rd(参数区);
    下标 = 号 >> 16;
    版本 = 号 & 0xFFFFu;
    if (下标 >= (hb_u32)HB_任务表格数 || 版本 == 0u) return HB_ST_BADHANDLE;
    hb_任务上锁();
    g = &hb_任务表[下标];
    if (g->状态 == HB_任务_空 || (g->版本 & 0xFFFFu) != 版本) { hb_任务解锁(); return 3u; }
    g->要取消 = 1;
    g->版本 = g->版本 + 1u;                                /* 旧号当场作废(与"收走"同一条) */
    if (g->状态 == HB_任务_好了)
    {
        if (g->结果数据 != NULL) { free(g->结果数据); g->结果数据 = NULL; }
        g->结果长 = 0u;
        g->状态 = HB_任务_空;
    }
    hb_任务解锁();
    return HB_ST_OK;
}

/*
  宿主调用那条指令的落点(生成的 C 就是 `hb_r[N] = hb_host_call(hb_r[N], 功能号);`):
  `参数区` = 寄存器进来那个地址,返回值 = 状态码(0 = 成功)。
  每条能力都有分支;`运行进程` / `取网址` / 网络那一族按独立 exe 的口径给"没挂能力"(见文件头那段说明)。
*/
/*
   `取文字宽度` 的 C 版(与 C# 侧 `图形宿主C.量文字宽度` **逐条同口径**):
   ASCII 1 格、汉字/全角 2 格;UTF-8 解码后按码点区间判宽(区间表见公共段那个 `hb_gfx_宽字`),
   4 字节序列按**两个 UTF-16 单元**算(C# 那边 `foreach (char …)` 数的是 UTF-16 单元,
   补充平面一个字符占两格 —— 两边才一致)。本宿主没窗口也照样给数(它是纯计算)。
*/
static hb_u32 hb_host_取文字宽度(hb_u32 参数区)
{
    hb_u32 址, 长, i = 0u, 宽 = 0u;
    if (!hb_host_fits(参数区, 12u)) return HB_ST_ARG;
    址 = hb_host_rd(参数区);
    长 = hb_host_rd(参数区 + 4u);
    if (!hb_host_fits(址, (uint64_t)长 + 1u)) return HB_ST_ARG;
    while (i < 长)
    {
        hb_u32 c = hb_mem[址 + i];
        if (c < 0x80u) { 宽 += 1u; i += 1u; }
        else if ((c & 0xE0u) == 0xC0u && i + 1u < 长)
        {
            hb_u32 码点 = ((c & 0x1Fu) << 6) | (hb_mem[址 + i + 1u] & 0x3Fu);
            宽 += hb_gfx_宽字(码点) ? 2u : 1u; i += 2u;
        }
        else if ((c & 0xF0u) == 0xE0u && i + 2u < 长)
        {
            hb_u32 码点 = ((c & 0x0Fu) << 12) | ((hb_mem[址 + i + 1u] & 0x3Fu) << 6)
                        | (hb_mem[址 + i + 2u] & 0x3Fu);
            宽 += hb_gfx_宽字(码点) ? 2u : 1u; i += 3u;
        }
        else if ((c & 0xF8u) == 0xF0u && i + 3u < 长) { 宽 += 2u; i += 4u; }   /* 补充平面 = 两个 UTF-16 单元 */
        else { 宽 += 1u; i += 1u; }
    }
    hb_host_wr(参数区 + 8u, 宽);
    return HB_ST_OK;
}

/* ═══════════════════════════════════════════════════════════════════════════
   R3-6-3:图形能力 17~26 的 C 版 + **无头呈现** + 事件脚本(确定性输入)

   与上面那 16 条同一套口径:参数区布局 / 状态码 / 越界一律落成状态码,
   语义与 `引擎\图形宿主C.cs` **逐条同口径** —— 改这边就必须回头看那边。

   **本宿主 = 无头**(与 C# 那边 `hbcc --run` / 自检挂 `无头呈现C` 同一个意思):
   `打开窗口` / `关闭窗口` / 清屏 / 画文字 / 画矩形 / 画线 / 刷新,每一条都按
   `绘制命令C.一句话()` 与 `无头呈现C` 的**同一份文本格式**逐行打出来(打到"图形口");
   `取事件` 从**事件脚本**按帧取;`等待` 不真睡(要的是确定与快);`取文字宽度` 是纯计算。

   为什么不真开窗:`图形宿主C` 那一侧真画在 `图形呈现口C` 后面(桌面壳 / 手机端),
   无头呈现只是它的一个实现;**C 版呈现层(Skia / SDL3)是 R3-6-5 的事**,本批只做无头这一档。

   **两条铁律**(与 C# 那边一字不差,少一条输出就对不上):
     · `打开窗口` = 第 0 帧开始(投脚本里帧 0 的事件);每 `刷新` 一次提交一帧、进下一帧;
       帧数跑满之后改投**一个关窗请求**(类型 1,只投一次);再往后还刷就报带行号的运行期错误;
     · 画 / 刷新之前没开窗 = 状态 22(`没有窗口`),**不是**静默丢掉。
   ═══════════════════════════════════════════════════════════════════════════ */

#ifndef HB_有图形
#define HB_有图形 0
#endif

/* R3-6-5:图形**呈现层** C 版(Win32 / GDI **与** Linux / X11,都是**零依赖**)。
   缺省 0 = 老口径(无头 dump / 独立 exe 没窗口);解释器核与转译产物在 Windows / Linux 上把它开成 1,
   而且**运行期**还要给 `--hb-窗口` 才真开窗 —— 不给开关时下面那一段一次都不执行,行为与老版一字不差。

   **非 Windows 那一档为什么走 X11 + 运行时 `dlopen`**(R3-6-5 批 2,用户 2026-10-01 拍板取 a):
   与 Win32 那一档同一条"零依赖"口径 —— 不引第三方库、不联网拉包、不动工具链。
   本层**不 include 任何 X11 头、也不链接任何 X 库**:类型与函数原型自己声明一份,
   `libX11.so.6` 在**运行期**用 `dlopen` 挂(dlopen 在 glibc 里,不用额外链接)。
   于是:编的时候零依赖、"这台机器没有 X"时 `dlopen` 失败 ⇒ 开窗给 0 ⇒ 能力层照老口径给状态 25,
   与"没有窗口"那条路完全同形。**macOS 那一档如实登记不做**(见 `未做任务清单.md` §二十)。 */
#ifndef HB_有窗口
#define HB_有窗口 0
#endif

#if HB_有图形

/* 参数区各功能号要读 / 写几个字节(与 `图形宿主C.参数区字节数` 同表) */
static hb_u32 hb_gfx_区大小(hb_u32 功能号)
{
    switch (功能号)
    {
        case HB_F_OPENWIN:  return 16u;   /* 标题址 / 标题长 / 宽 / 高 */
        case HB_F_CLEAR:    return 4u;    /* 颜色 */
        case HB_F_DRAWTEXT: return 20u;   /* x / y / 文本址 / 文本长 / 颜色 */
        case HB_F_DRAWRECT: return 20u;   /* x / y / 宽 / 高 / 颜色 */
        case HB_F_DRAWLINE: return 20u;   /* x1 / y1 / x2 / y2 / 颜色 */
        case HB_F_POLLEVENT:return 16u;   /* 类型 / 键码 / x / y(四个都写回) */
        case HB_F_SLEEP:    return 4u;    /* 毫秒 */
        case HB_F_TEXTWIDTH:return 12u;   /* 文本址 / 文本长 / 结果(写回) */
        /* R4-12:位图那一族(70~77)—— 与 `图形宿主C.参数区字节数` 同表。
           ⚠ **没登记的号给 0** ⇒ 那一族整张参数表被当成空的(C# 侧第一版漏登记过,
             症状是"句柄恒为 0":宿主读到的宽高都是 0,图建不出来)。 */
        case HB_F_MAKEBMP:  return 12u;   /* 句柄**槽址** / 宽 / 高 */
        case HB_F_FREEBMP:  return 4u;    /* 句柄 */
        case HB_F_BMPSIZE:  return 12u;   /* 句柄 / 宽址 / 高址 */
        case HB_F_PUTPIX:   return 16u;   /* 句柄 / x / y / 颜色 */
        case HB_F_GETPIX:   return 16u;   /* 句柄 / x / y / 颜色(写回) */
        case HB_F_BLITBMP:  return 16u;   /* 句柄 / x / y / (第 4 格不用) */
        case HB_F_LINEBMP:  return 24u;   /* 句柄 / x1 / y1 / x2 / y2 / 颜色 */
        case HB_F_RECTBMP:  return 24u;   /* 句柄 / x / y / 宽 / 高 / 颜色 */
        case HB_F_TEXTBMP:  return 24u;   /* 句柄 / x / y / 文本址 / 文本长 / 颜色(号 78;本侧还没实现) */
        default:            return 0u;    /* 关闭窗口 / 刷新:没有参数 */
    }
}

static int hb_gfx_是图形功能号(hb_u32 功能号)
{
    /* 上界 = 位图那一族的末号(**78**,控 1-① 加了 `贴文字到位图`):图形号 60~78 连着一段,
       "这一号是不是图形号"只有这一处判定(C# 那边 `图形宿主C` 与位图号段是分开两段判的,
       这边合成一段 —— 两边**认的号集合相同**才是判据) */
    return 功能号 >= HB_F_OPENWIN && 功能号 <= HB_F_TEXTBMP;
}

/* ══════════ R4-12:位图仓(离屏,句柄表 + **旧号永不复用**)══════════

   与 C# 侧 `引擎\位图仓C.cs` **同一份口径**,连边界都一样:

     · 句柄从 **1** 起、只增不减;释放之后再拿那个号来用一律给 22(`句柄不认`)——
       这样"用完没释放"与"释放了还在用"两种错都能当场暴露,而不会悄悄画到别人的图上;
     · 单张图:宽 / 高各 ≤ 8192、**总格数 ≤ 1600 万**(约 64MB)⇒ 超了返回"建不出来",不静默截断;
     · 像素 **0xAARRGGBB**、行优先、紧排;越界读给 0、越界画什么都不做。

   ⚠ **它不进 HB 的运行内存**(`hb_mem` 那一块):一张 480×520 的图就 1MB,与"默认运行内存 1MB"
     同量级 —— 这正是把像素放宿主侧的理由(施工图 §2.2 第 ① 条),自检里那条"建位图前后
     虚拟机那 1MB 的占用条不变"量的就是它。

   ⚠ **表是进程级静态**:C# 那边靠 `宿主能力C.清运行痕迹 → 位图仓C.清光()` 收干净,
     这边靠 `hb_boot`(装载 = 一个程序从头开始)里那一句 `hb_bmp_清光()` —— 同一件事的两头。 */
#define HB_BMP_宽上限    8192u
#define HB_BMP_格数上限  16000000u    /* 宽 × 高 的上限(16,000,000 格 ≈ 64MB) */

typedef struct
{
    hb_u32 号;        /* 0 = 这个格子空着(可以再给下一张用;**号**永远是新的) */
    hb_u32 宽;
    hb_u32 高;
    hb_u32 *像素;
} hb_bmp_格;

static hb_bmp_格 *hb_bmp_表 = NULL;
static hb_u32 hb_bmp_容量 = 0;
static hb_u32 hb_bmp_下一号 = 1;

/* 按号找那一格(找不着 = 没建过 / 已经释放过 ⇒ 调用方给 22) */
static hb_bmp_格 *hb_bmp_找(hb_u32 号)
{
    hb_u32 i;
    if (号 == 0u) return NULL;
    for (i = 0; i < hb_bmp_容量; i++)
        if (hb_bmp_表[i].号 == 号) return &hb_bmp_表[i];
    return NULL;
}

/* 建一张并定号。宽高不合法 / 要不到内存给 0(调用方转成状态 9)。 */
static hb_u32 hb_bmp_建(hb_u32 宽, hb_u32 高)
{
    hb_bmp_格 *格 = NULL;
    hb_u32 i;
    hb_u32 *像素;
    if (宽 == 0u || 高 == 0u || 宽 > HB_BMP_宽上限 || 高 > HB_BMP_宽上限) return 0u;
    if ((uint64_t)宽 * (uint64_t)高 > (uint64_t)HB_BMP_格数上限) return 0u;
    像素 = (hb_u32 *)calloc((size_t)((uint64_t)宽 * (uint64_t)高), sizeof(hb_u32));
    if (像素 == NULL) return 0u;                 /* 内存要不到:**如实失败**,别给个半张图 */
    for (i = 0; i < hb_bmp_容量 && 格 == NULL; i++)
        if (hb_bmp_表[i].号 == 0u) 格 = &hb_bmp_表[i];
    if (格 == NULL)                              /* 表满了:扩一倍(与 C# 那份"能装多少装多少"同口径) */
    {
        hb_u32 新容量 = hb_bmp_容量 == 0u ? 8u : hb_bmp_容量 * 2u;
        hb_bmp_格 *新表 = (hb_bmp_格 *)realloc(hb_bmp_表, (size_t)新容量 * sizeof(hb_bmp_格));
        if (新表 == NULL) { free(像素); return 0u; }
        memset(新表 + hb_bmp_容量, 0, (size_t)(新容量 - hb_bmp_容量) * sizeof(hb_bmp_格));
        hb_bmp_表 = 新表;
        hb_bmp_容量 = 新容量;
        for (i = 0; i < hb_bmp_容量 && 格 == NULL; i++)
            if (hb_bmp_表[i].号 == 0u) 格 = &hb_bmp_表[i];
        if (格 == NULL) { free(像素); return 0u; }
    }
    格->号 = hb_bmp_下一号++;
    格->宽 = 宽;
    格->高 = 高;
    格->像素 = 像素;
    return 格->号;
}

/* 释放:还回宿主。没这个号 / 已经释放过都给 0(调用方据此报"句柄不认") */
static int hb_bmp_放(hb_u32 号)
{
    hb_bmp_格 *格 = hb_bmp_找(号);
    if (格 == NULL) return 0;
    free(格->像素);
    格->像素 = NULL;
    格->号 = 0u;
    格->宽 = 0u;
    格->高 = 0u;
    return 1;
}

/* 全清(装载一个程序时调一次;与 C# 侧 `位图仓C.清光()` 同一件事) */
static void hb_bmp_清光(void)
{
    hb_u32 i;
    for (i = 0; i < hb_bmp_容量; i++)
    {
        if (hb_bmp_表[i].像素 != NULL) free(hb_bmp_表[i].像素);
        hb_bmp_表[i].像素 = NULL;
        hb_bmp_表[i].号 = 0u;
        hb_bmp_表[i].宽 = 0u;
        hb_bmp_表[i].高 = 0u;
    }
    /* ⚠ `hb_bmp_下一号` **不回 1**:号只增不减是这一族的硬口径(旧号永不复用) */
}

/* 画一个像素(越界**什么都不做** —— 让绘制代码不必自己判边界;与 `位图C.画` 同口径) */
static void hb_bmp_画(hb_bmp_格 *格, int x, int y, hb_u32 颜色)
{
    if (x < 0 || y < 0 || (hb_u32)x >= 格->宽 || (hb_u32)y >= 格->高) return;
    格->像素[(hb_u32)y * 格->宽 + (hb_u32)x] = 颜色;
}

/* 取一个像素(越界给 0 —— 与"取图像素"那条能力的口径一致) */
static hb_u32 hb_bmp_取(hb_bmp_格 *格, int x, int y)
{
    if (x < 0 || y < 0 || (hb_u32)x >= 格->宽 || (hb_u32)y >= 格->高) return 0u;
    return 格->像素[(hb_u32)y * 格->宽 + (hb_u32)x];
}

/* 画一条线(Bresenham,整数;**与 `位图C.画线` 逐句同算法** —— 两边像素必须一模一样)。
   两个端点都画(闭区间),越界的格子自动跳过。 */
static void hb_bmp_画线(hb_bmp_格 *格, int x1, int y1, int x2, int y2, hb_u32 颜色)
{
    int 差x = abs(x2 - x1), 差y = abs(y2 - y1);
    int 步x = x1 < x2 ? 1 : -1, 步y = y1 < y2 ? 1 : -1;
    int 偏 = 差x - 差y;
    int x = x1, y = y1;
    for (;;)
    {
        int 两倍;
        hb_bmp_画(格, x, y, 颜色);
        if (x == x2 && y == y2) break;
        两倍 = 偏 * 2;
        if (两倍 > -差y) { 偏 -= 差y; x += 步x; }
        if (两倍 < 差x) { 偏 += 差x; y += 步y; }
    }
}

/* 画一个实心矩形(闭区间 [x, x+宽-1] × [y, y+高-1]);宽高 ≤ 0 什么都不做。
   与 `位图C.画矩形` 逐句同算法。 */
static void hb_bmp_画矩形(hb_bmp_格 *格, int x, int y, int 矩形宽, int 矩形高, hb_u32 颜色)
{
    int 末x, 末y, 起x, 起y, 止x, 止y, 行, 列;
    if (矩形宽 <= 0 || 矩形高 <= 0) return;
    末x = x + 矩形宽 - 1;
    末y = y + 矩形高 - 1;
    if (末x < 0 || 末y < 0 || x >= (int)格->宽 || y >= (int)格->高) return;   /* 整块在外面:一步都不用走 */
    起x = x > 0 ? x : 0;
    起y = y > 0 ? y : 0;
    止x = 末x < (int)格->宽 - 1 ? 末x : (int)格->宽 - 1;
    止y = 末y < (int)格->高 - 1 ? 末y : (int)格->高 - 1;
    for (行 = 起y; 行 <= 止y; 行++)
    {
        hb_u32 基 = (hb_u32)行 * 格->宽;
        for (列 = 起x; 列 <= 止x; 列++) 格->像素[基 + (hb_u32)列] = 颜色;
    }
}

/* ---- 脚本里的键名 / 事件名(UTF-8 逐字节比;脚本是 UTF-8 写的,两边同一份字面量) ---- */

static const struct { const char *名; int 码; } hb_gfx_键表[] = {
    { "退格", 8 }, { "backspace", 8 },
    { "制表", 9 }, { "tab", 9 },
    { "回车", 13 }, { "enter", 13 },
    { "上档", 16 }, { "shift", 16 },
    { "控制", 17 }, { "ctrl", 17 },
    { "替换", 18 }, { "alt", 18 },
    { "退出", 27 }, { "esc", 27 },
    { "空格", 32 }, { "space", 32 },
    { "上一页", 33 }, { "pageup", 33 },
    { "下一页", 34 }, { "pagedown", 34 },
    { "首页", 36 }, { "home", 36 },
    { "末页", 35 }, { "end", 35 },
    { "插入", 45 }, { "insert", 45 },
    { "删除", 46 }, { "delete", 46 },
    { "左", 37 }, { "left", 37 },
    { "上", 38 }, { "up", 38 },
    { "右", 39 }, { "right", 39 },
    { "下", 40 }, { "down", 40 },
    { "鼠标左键", 1 }, { "mouseleft", 1 },
    { "鼠标中键", 2 }, { "mousemiddle", 2 },
    { "鼠标右键", 3 }, { "mouseright", 3 },
    { NULL, 0 }
};

static const struct { const char *名; const char *别名; int 码; } hb_gfx_事件表[] = {
    { "关窗", "close", 1 },
    { "按键", "key", 2 },
    { "移动", "move", 3 },
    { "按下", "down", 4 },
    { "抬起", "up", 5 },
    { NULL, NULL, 0 }
};

static int hb_gfx_同(hb_u8 a, char b)
{
    if (b >= 'A' && b <= 'Z') b = (char)(b - 'A' + 'a');
    if (a >= 'A' && a <= 'Z') a = (hb_u8)(a - 'A' + 'a');
    return a == (hb_u8)b;
}

static int hb_gfx_同词(const hb_u8 *词, hb_u32 长, const char *名)
{
    hb_u32 i = 0u;
    if (名 == NULL) return 0;
    while (i < 长 && 名[i] != '\0') { if (!hb_gfx_同(词[i], 名[i])) return 0; i++; }
    return i == 长 && 名[i] == '\0';
}

static hb_u32 hb_gfx_词长(const hb_u8 *词, hb_u32 剩)
{
    hb_u32 i;
    for (i = 0u; i < 剩; i++) if (词[i] == ' ' || 词[i] == '\t' || 词[i] == '\r' || 词[i] == '\n') break;
    return i;
}

/* 整数:认十进制(可带前导 +/-)与 `0x` 十六进制;`带号` = 允许前导符号(坐标那几个词要) */
static int hb_gfx_是整数(const hb_u8 *词, hb_u32 长, int 带号, int *出)
{
    hb_u32 i = 0u;
    long 值 = 0;
    int 负 = 0;
    if (长 == 0u) return 0;
    if (带号 && (词[0] == '-' || 词[0] == '+'))
    {
        负 = 词[0] == '-';
        i = 1u;
        if (i >= 长) return 0;
    }
    if (i == 0u && 长 > 2u && 词[0] == '0' && (词[1] == 'x' || 词[1] == 'X'))
    {
        for (i = 2u; i < 长; i++)
        {
            hb_u8 c = 词[i];
            int d;
            if (c >= '0' && c <= '9') d = c - '0';
            else if (c >= 'a' && c <= 'f') d = c - 'a' + 10;
            else if (c >= 'A' && c <= 'F') d = c - 'A' + 10;
            else return 0;
            值 = 值 * 16 + d;
            if (值 > 0x7FFFFFFFL) return 0;
        }
        *出 = (int)值;
        return 1;
    }
    for (; i < 长; i++)
    {
        if (词[i] < '0' || 词[i] > '9') return 0;
        值 = 值 * 10 + (词[i] - '0');
        if (值 > 2147483647L) return 0;
    }
    *出 = 负 ? (int)(-值) : (int)值;
    return 1;
}

/* 认一个键名(与 `图形键C.认键名` 同一套;认不出给 0) */
static int hb_gfx_认键名(const hb_u8 *词, hb_u32 长, int *码)
{
    int i;
    if (长 == 0u) return 0;
    for (i = 0; hb_gfx_键表[i].名 != NULL; i++)
        if (hb_gfx_同词(词, 长, hb_gfx_键表[i].名)) { *码 = hb_gfx_键表[i].码; return 1; }
    /* 单个字母:`a` / `A` 都算 A 键(ASCII 大写口径) */
    if (长 == 1u && ((词[0] >= 'a' && 词[0] <= 'z') || (词[0] >= 'A' && 词[0] <= 'Z')))
    {
        *码 = (词[0] >= 'a' && 词[0] <= 'z') ? (词[0] - 'a' + 'A') : 词[0];
        return 1;
    }
    /* `字母X`(6 字节前缀 + 一个字母) */
    if (长 == 7u && hb_gfx_同词(词, 6u, "字母")
        && ((词[6] >= 'a' && 词[6] <= 'z') || (词[6] >= 'A' && 词[6] <= 'Z')))
    {
        *码 = (词[6] >= 'a' && 词[6] <= 'z') ? (词[6] - 'a' + 'A') : 词[6];
        return 1;
    }
    /* `数字5`(6 字节前缀 + 一个数字) */
    if (长 == 7u && hb_gfx_同词(词, 6u, "数字") && 词[6] >= '0' && 词[6] <= '9')
    {
        *码 = 词[6];
        return 1;
    }
    /* `F1`~`F12`(大小写都认) */
    if ((词[0] == 'F' || 词[0] == 'f') && 长 >= 2u && 长 <= 3u)
    {
        int 号 = 0;
        hb_u32 k;
        int 都数字 = 1;
        for (k = 1u; k < 长; k++)
        {
            if (词[k] < '0' || 词[k] > '9') { 都数字 = 0; break; }
            号 = 号 * 10 + (词[k] - '0');
        }
        if (都数字 && 号 >= 1 && 号 <= 12) { *码 = 111 + 号; return 1; }
    }
    /* 裸数字:十进制,或 `0x` 起头的十六进制(键码本身就是对外接口,允许直接写它的数) */
    return hb_gfx_是整数(词, 长, 0, 码);
}

/* ---- 事件脚本(帧号 → 事件;唯一事实源 = `引擎\图形事件C.cs` 顶上那段格式说明) ---- */
#define HB_GFX_MAX_EVENTS 256

static struct
{
    int 帧数;                          /* 脚本说跑满多少帧(必写) */
    int 已读;                          /* 数组里装了几条 */
    int 投过关窗;                      /* 关窗请求只投一次 */
    int 事件类型[HB_GFX_MAX_EVENTS];
    int 事件帧[HB_GFX_MAX_EVENTS];
    int 键码[HB_GFX_MAX_EVENTS];
    int 事件x[HB_GFX_MAX_EVENTS];
    int 事件y[HB_GFX_MAX_EVENTS];
} hb_gfx_脚本;

static int hb_gfx_有脚本 = 0;

/* 数这一行第几列(1 起);列之间按空白切开。返回 0 = 没这一列 */
static int hb_gfx_第几列(const char *行, int 列号, const char **起, hb_u32 *长)
{
    const char *p = 行;
    int n = 0;
    for (;;)
    {
        while (*p == ' ' || *p == '\t') p++;
        if (*p == '\0' || *p == '\r' || *p == '\n') return 0;
        n++;
        if (n == 列号)
        {
            const char *s = p;
            while (*p != '\0' && *p != ' ' && *p != '\t' && *p != '\r' && *p != '\n') p++;
            *起 = s;
            *长 = (hb_u32)(p - s);
            return 1;
        }
        while (*p != '\0' && *p != ' ' && *p != '\t' && *p != '\r' && *p != '\n') p++;
    }
}

static int hb_gfx_列数(const char *行)
{
    int n = 0;
    const char *起;
    hb_u32 长;
    while (hb_gfx_第几列(行, n + 1, &起, &长)) n++;
    return n;
}

/*
   读一份事件脚本(与 `事件脚本C.解析` **同一个解析器口径**:`#` / `//` 起头是注释(行尾的也切掉)、
   空行不算、`帧数 N` 必写且大于 0、`帧 K 类型 [参数…]` 的各类型参数个数固定、每个事件的帧号都要 < 帧数)。
   脚本写错一律**带行号**报出来 —— 与 C# 那边同一句规矩:别让它变成"程序静悄悄地没有事件"。
*/
static void hb_gfx_读脚本(const char *路径)
{
    int 是宽路径 = 0;
    FILE *f = hb_宽开(路径, "rb", &是宽路径);
    char 行[1024];
    int 行号 = 0;
    int 有帧数 = 0;
    int i;
    if (f == NULL)
        hb_failf(hb_pc, "读不动事件脚本 %s(文件在不在?路径对不对?)", 路径);
    hb_gfx_脚本.已读 = 0;
    hb_gfx_脚本.投过关窗 = 0;
    hb_gfx_脚本.帧数 = 0;
    while (fgets(行, (int)sizeof 行, f) != NULL)
    {
        const char *起;
        hb_u32 长;
        char *p;
        int 格数;
        int 值;
        行号++;
        if (行号 == 1 && (unsigned char)行[0] == 0xEFu && (unsigned char)行[1] == 0xBBu
            && (unsigned char)行[2] == 0xBFu)
            memmove(行, 行 + 3, strlen(行 + 3) + 1u);     /* C# 的 `File.ReadAllText` 会吃掉 BOM,这边照做 */
        /* 去注释(与 `事件脚本C.去注释` 同口径:第一个 `#` 或 `//` 起头的那一截切掉) */
        {
            char *井 = strchr(行, '#');
            char *斜 = strstr(行, "//");
            char *切 = 井 == NULL ? 斜 : (斜 == NULL ? 井 : (井 < 斜 ? 井 : 斜));
            if (切 != NULL) *切 = '\0';
        }
        p = 行;
        while (*p == ' ' || *p == '\t') p++;
        if (*p == '\0' || *p == '\r' || *p == '\n') continue;
        格数 = hb_gfx_列数(p);
        if (!hb_gfx_第几列(p, 1, &起, &长)) continue;
        if (hb_gfx_同词((const hb_u8 *)起, 长, "帧数") || hb_gfx_同词((const hb_u8 *)起, 长, "frames"))
        {
            if (有帧数)
            {
                fclose(f);
                hb_failf(hb_pc, "事件脚本 第 %d 行:`帧数` 写了两遍。办法:整份脚本只留开头那一行 `帧数 N`", 行号);
            }
            if (格数 != 2 || !hb_gfx_第几列(p, 2, &起, &长)
                || !hb_gfx_是整数((const hb_u8 *)起, 长, 0, &值) || 值 <= 0)
            {
                fclose(f);
                hb_failf(hb_pc, "事件脚本 第 %d 行:`帧数` 后面要跟一个大于 0 的整数"
                    "(办法:写成 `帧数 4` 这样 —— 意思是这次跑满 4 帧)", 行号);
            }
            hb_gfx_脚本.帧数 = 值;
            有帧数 = 1;
            continue;
        }
        if (!hb_gfx_同词((const hb_u8 *)起, 长, "帧") && !hb_gfx_同词((const hb_u8 *)起, 长, "frame"))
        {
            fclose(f);
            hb_failf(hb_pc, "事件脚本 第 %d 行:认不出这一行。办法:每行要么是 `帧数 N`,"
                "要么是 `帧 帧号 事件类型 [参数…]`(`#` 或 `//` 起头是注释)", 行号);
        }
        if (格数 < 3)
        {
            fclose(f);
            hb_failf(hb_pc, "事件脚本 第 %d 行:这一行少东西。办法:`帧 0 按键 回车` 这样 —— 帧号 + 事件类型 + 参数", 行号);
        }
        if (!hb_gfx_第几列(p, 2, &起, &长) || !hb_gfx_是整数((const hb_u8 *)起, 长, 0, &值) || 值 < 0)
        {
            fclose(f);
            hb_failf(hb_pc, "事件脚本 第 %d 行:帧号要写 0 或更大的整数。办法:第 0 帧 = 打开窗口之后,"
                "每 `刷新()` 一次进下一帧", 行号);
        }
        {
            int 帧号 = 值;
            int 类型 = 0;
            int 键 = 0, 横 = 0, 纵 = 0;
            int 参数个数;
            if (!hb_gfx_第几列(p, 3, &起, &长))
            {
                fclose(f);
                hb_failf(hb_pc, "事件脚本 第 %d 行:`帧 %d` 后面还要写事件类型", 行号, 帧号);
            }
            for (i = 0; hb_gfx_事件表[i].名 != NULL; i++)
                if (hb_gfx_同词((const hb_u8 *)起, 长, hb_gfx_事件表[i].名)
                    || hb_gfx_同词((const hb_u8 *)起, 长, hb_gfx_事件表[i].别名)) { 类型 = hb_gfx_事件表[i].码; break; }
            if (类型 == 0)
            {
                fclose(f);
                hb_failf(hb_pc, "事件脚本 第 %d 行:认不出这个事件类型。办法:写 关窗 / 按键 / 移动 / 按下 / 抬起"
                    "(英文 close / key / move / down / up 也认)", 行号);
            }
            参数个数 = 格数 - 3;
            if (类型 == 1)
            {
                if (参数个数 != 0)
                {
                    fclose(f);
                    hb_failf(hb_pc, "事件脚本 第 %d 行:`关窗` 后面不用跟参数。办法:`帧 3 关窗`", 行号);
                }
            }
            else if (类型 == 2)
            {
                if (参数个数 != 1)
                {
                    fclose(f);
                    hb_failf(hb_pc, "事件脚本 第 %d 行:`按键` 要一个键名。办法:`帧 0 按键 回车`", 行号);
                }
                if (!hb_gfx_第几列(p, 4, &起, &长) || !hb_gfx_认键名((const hb_u8 *)起, 长, &键))
                {
                    fclose(f);
                    hb_failf(hb_pc, "事件脚本 第 %d 行:认不出这个键名。办法:写 `回车` / `空格` / `左` / `字母A` /"
                        " `数字5` / `F3` / `鼠标左键` 这类名字,也可以直接写键码的数字", 行号);
                }
            }
            else if (类型 == 3)
            {
                if (参数个数 != 2)
                {
                    fclose(f);
                    hb_failf(hb_pc, "事件脚本 第 %d 行:`移动` 要两个坐标 x y。办法:`帧 1 移动 100 60`", 行号);
                }
                if (!hb_gfx_第几列(p, 4, &起, &长) || !hb_gfx_是整数((const hb_u8 *)起, 长, 1, &横)
                    || !hb_gfx_第几列(p, 5, &起, &长) || !hb_gfx_是整数((const hb_u8 *)起, 长, 1, &纵))
                {
                    fclose(f);
                    hb_failf(hb_pc, "事件脚本 第 %d 行:`移动` 的坐标要写整数。办法:`帧 1 移动 100 60`", 行号);
                }
            }
            else
            {
                if (参数个数 != 3)
                {
                    fclose(f);
                    hb_failf(hb_pc, "事件脚本 第 %d 行:`%s` 要键码 + 两个坐标。办法:`帧 2 %s 鼠标左键 10 40`",
                        行号, 类型 == 4 ? "按下" : "抬起", 类型 == 4 ? "按下" : "抬起");
                }
                if (!hb_gfx_第几列(p, 4, &起, &长) || !hb_gfx_认键名((const hb_u8 *)起, 长, &键)
                    || !hb_gfx_第几列(p, 5, &起, &长) || !hb_gfx_是整数((const hb_u8 *)起, 长, 1, &横)
                    || !hb_gfx_第几列(p, 6, &起, &长) || !hb_gfx_是整数((const hb_u8 *)起, 长, 1, &纵))
                {
                    fclose(f);
                    hb_failf(hb_pc, "事件脚本 第 %d 行:`%s` 的键名 / 坐标认不出。办法:`帧 2 %s 鼠标左键 10 40`",
                        行号, 类型 == 4 ? "按下" : "抬起", 类型 == 4 ? "按下" : "抬起");
                }
            }
            if (hb_gfx_脚本.已读 >= HB_GFX_MAX_EVENTS)
            {
                fclose(f);
                hb_failf(hb_pc, "事件脚本的事件太多了(上限 %d 条)", HB_GFX_MAX_EVENTS);
            }
            hb_gfx_脚本.事件帧[hb_gfx_脚本.已读] = 帧号;
            hb_gfx_脚本.事件类型[hb_gfx_脚本.已读] = 类型;
            hb_gfx_脚本.键码[hb_gfx_脚本.已读] = 键;
            hb_gfx_脚本.事件x[hb_gfx_脚本.已读] = 横;
            hb_gfx_脚本.事件y[hb_gfx_脚本.已读] = 纵;
            hb_gfx_脚本.已读++;
        }
    }
    fclose(f);
    if (!有帧数)
        hb_failf(hb_pc, "事件脚本 %s 里没有 `帧数 N` —— 不写就不知道这次要跑满多少帧(它是必写的)", 路径);
    for (i = 0; i < hb_gfx_脚本.已读; i++)
        if (hb_gfx_脚本.事件帧[i] >= hb_gfx_脚本.帧数)
            hb_failf(hb_pc, "事件脚本里有一条事件排在第 %d 帧,但 `帧数` 只写到 %d —— 那一帧永远不会到"
                "(办法:把 `帧数` 改大,或者把这条挪到前面的帧)", hb_gfx_脚本.事件帧[i], hb_gfx_脚本.帧数);
    (void)是宽路径;              /* 关文件只用 `fclose`(宽窄两路一样) */
    hb_gfx_有脚本 = 1;
}

/* 图中途进来的事件(投脚本时往这里放;`取事件` 从头取) */
#define HB_GFX_MAX_QUEUE 256
static int hb_gfx_队列类型[HB_GFX_MAX_QUEUE];
static int hb_gfx_队列键[HB_GFX_MAX_QUEUE];
static int hb_gfx_队列x[HB_GFX_MAX_QUEUE];
static int hb_gfx_队列y[HB_GFX_MAX_QUEUE];
static int hb_gfx_队列头 = 0;
static int hb_gfx_队列尾 = 0;

static void hb_gfx_入队(int 类型, int 键, int 横, int 纵)
{
    if (hb_gfx_队列尾 >= HB_GFX_MAX_QUEUE) return;
    hb_gfx_队列类型[hb_gfx_队列尾] = 类型;
    hb_gfx_队列键[hb_gfx_队列尾] = 键;
    hb_gfx_队列x[hb_gfx_队列尾] = 横;
    hb_gfx_队列y[hb_gfx_队列尾] = 纵;
    hb_gfx_队列尾++;
}

/* 把脚本里排在第 `帧号` 帧的事件投进来;帧数用尽之后改投关窗请求(只投一次) */
static void hb_gfx_投放第几帧(int 帧号)
{
    int i;
    if (!hb_gfx_有脚本) return;
    if (帧号 < hb_gfx_脚本.帧数)
    {
        for (i = 0; i < hb_gfx_脚本.已读; i++)
            if (hb_gfx_脚本.事件帧[i] == 帧号)
                hb_gfx_入队(hb_gfx_脚本.事件类型[i], hb_gfx_脚本.键码[i],
                            hb_gfx_脚本.事件x[i], hb_gfx_脚本.事件y[i]);
        return;
    }
    if (hb_gfx_脚本.投过关窗) return;
    hb_gfx_脚本.投过关窗 = 1;
    hb_gfx_入队(1, 0, 0, 0);
}

/* ---- 无头呈现(与 `无头呈现C` 的文本格式一字不差;只有这一处是格式的事实源) ---- */

static FILE *hb_gfx_出 = NULL;      /* 图形口:`--hb-图形 <文件>`,缺省 stdout */
static int hb_gfx_窗口开 = 0;
static int hb_gfx_窗口宽 = 0;
static int hb_gfx_窗口高 = 0;
static int hb_gfx_已呈现帧数 = 0;
static int hb_gfx_攒着 = 0;         /* 这一帧攒了几条命令 */

static FILE *hb_gfx_口(void) { return hb_gfx_出 != NULL ? hb_gfx_出 : stdout; }

/* 把一段 UTF-8 直接写出去(生成 C 里面写中文注释与中文字面量都用这一招) */
static void hb_gfx_原样(const char *s) { fputs(s, hb_gfx_口()); }

/* 文字一律加引号(反斜杠与引号转义)—— 与 `绘制命令C.带引号` 同口径 */
static void hb_gfx_带引号(const hb_u8 *文本, hb_u32 长)
{
    hb_u32 i;
    FILE *f = hb_gfx_口();
    fputc('"', f);
    for (i = 0u; i < 长; i++)
    {
        if (文本[i] == '\\') { fputc('\\', f); fputc('\\', f); }
        else if (文本[i] == '"') { fputc('\\', f); fputc('"', f); }
        else fputc((int)文本[i], f);
    }
    fputc('"', f);
}

/* ═══════════════════════════════════════════════════════════════════════════
   R3-6-5:图形呈现层 C 版 —— **三平台都是零依赖**

   ① **Windows**:Win32 / GDI(user32 + gdi32 系统自带,`#pragma comment` 让"一条命令行直接编出 exe"照旧)。
   ② **Linux**:X11 —— 运行时 `dlopen("libX11.so.6")`,**不 include X11 头、不链接 X 库**(见下面那一段)。
   ③ **macOS**:如实**不做**(本机没有真机可验,登记在 `未做任务清单.md` §二十;`hb_win_打开` 给 0)。

   为什么不用 SDL3 / Skia:拉一份第三方库进来就是新的装机与离线还原风险;而 Win32 与 X11 都是
   系统自带的**即时模式** API,与"HB 每帧重画"的模型天然同形(用户 2026-10-01 当班两批都取"零依赖")。

   **只加不减,而且是两道闸**:整段由 `HB_有窗口` 包住(编的时候不要就一行都不编),
   运行期还要 `--hb-窗口` 才把 `hb_win_活` 置 1 —— 没给开关时下面每一个函数都**空转**
   (一个系统调用都不发),所以无头 dump、38 个标准工程的对拍**一个字节都不变**。

   与 `图形宿主\桌面壳C`(C# / Avalonia 那一份)对齐的几条口径(两个平台实现共用):
     · **颜色 = 0xAARRGGBB**;GDI 要 0x00BBGGRR、X11 要 0x00RRGGBB,换算各在 `hb_win_颜色` 一处;
     · **每帧从黑底开始** + "上一次提交出去的那一帧"留着重贴 ⇒ 不会贴出正在攒的半截;
     · **空帧不许擦屏**(H40);
     · **文字左上角定位**,字体 = 等宽的位图/点阵字体(中文 2 格、ASCII 1 格,与 `取文字宽度` 同口径);
     · **键码就是 Windows 虚拟键那套老编号**(`图形键C` 的口径)⇒ Windows 同号,X11 那一侧由 keysym 译过来;
     · **用户点叉** = 投一个关窗请求(类型 1)并真销毁窗口;`关闭窗口()` 对已关的照旧给成功。
   ═══════════════════════════════════════════════════════════════════════════ */
/* 窗口模式下没给 `--hb-图形` 时,绘制命令的文本 dump 丢进这里 —— 别混进"程序输出"那一股(R3-6-3 坑四)。
   **R3-6-5 批 2 起这一件无条件编出来**:转译产物生成的 `main` 会问它一句(那一句在 `hb_gfx_装配` 里),
   而"这一份没带窗口层"(`HB_有窗口 0`)是合法情形 —— 那时它是这一族里唯一还需要存在的一个。 */
static FILE *hb_win_黑洞(void)
{
    int 是宽 = 0;
#ifdef _WIN32
    return hb_宽开("NUL", "wb", &是宽);
#else
    return hb_宽开("/dev/null", "wb", &是宽);
#endif
}

/* "这一趟跑的是真窗口呈现层"这把闸 —— **无条件定义**(没编窗口层时恒为 0)。
   为什么不能跟着 `HB_有窗口` 一起消失:上面那几处能力层要问它(`if (hb_win_活) hb_win_…`),
   那些分支在"这份宿主没挂窗口"时必须是**编得过的空转**,而不是整段被 `#if` 掉
   (第一版把它挪进 `#if HB_有窗口` 里,于是 `HB_有窗口 0` 的合法组合编不过 —— 自检 8 条 Linux 工程当场红)。 */
static int hb_win_活 = 0;

#if HB_有窗口

static int hb_win_宽 = 0;
static int hb_win_高 = 0;
static int hb_win_用户关过 = 0;
/* 用户点叉之后叫回来:能力层那边也得知道"窗口没了"(否则下一次画还往一块空画布上画) */
static void hb_gfx_窗口被用户关掉(void)
{
    hb_gfx_窗口开 = 0;
    hb_gfx_攒着 = 0;
}

#if defined(_WIN32)
/* ══════════════ ① Windows:Win32 / GDI ══════════════ */

#if defined(_MSC_VER)
/* cl / clang-cl:默认链接里没有 user32 / gdi32 —— 这两行让"一条命令行直接编出 exe"照旧成立 */
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#endif

static HWND hb_win_句柄 = NULL;
static HDC hb_win_画dc = NULL;       /* 正在攒的这一帧 */
static HBITMAP hb_win_画位图 = NULL;
static HBITMAP hb_win_画旧位图 = NULL;
static HDC hb_win_显dc = NULL;       /* 上一次 `刷新` 提交出去的那一帧(WM_PAINT 重贴它) */
static HBITMAP hb_win_显位图 = NULL;
static HBITMAP hb_win_显旧位图 = NULL;
static HFONT hb_win_字体 = NULL;
static int hb_win_类注册 = 0;

static const wchar_t *hb_win_类名 = L"HB图形窗口C";
static const wchar_t *hb_win_字体名 = L"新宋体";

/* HB 颜色(0xAARRGGBB)→ GDI 的 COLORREF(0x00BBGGRR) */
static COLORREF hb_win_颜色(hb_u32 值)
{
    return RGB((BYTE)((值 >> 16) & 0xFFu), (BYTE)((值 >> 8) & 0xFFu), (BYTE)(值 & 0xFFu));
}

/* UTF-8 字节段 → UTF-16(调用方 free;失败给 NULL = 这一段画不出来,不算错) */
static wchar_t *hb_win_宽串(const hb_u8 *文本, hb_u32 长)
{
    int 需;
    wchar_t *w;
    if (文本 == NULL || 长 == 0u || 长 > 0x7FFFFFFFu) return NULL;
    需 = MultiByteToWideChar(CP_UTF8, 0, (LPCCH)文本, (int)长, NULL, 0);
    if (需 <= 0) return NULL;
    w = (wchar_t *)malloc(((size_t)需 + 1u) * sizeof(wchar_t));
    if (w == NULL) return NULL;
    if (MultiByteToWideChar(CP_UTF8, 0, (LPCCH)文本, (int)长, w, 需) <= 0) { free(w); return NULL; }
    w[需] = L'\0';
    return w;
}

/* 把一块画布填成一个颜色(`清屏` 与"每帧的黑底"都走它) */
static void hb_win_底色(HDC dc, hb_u32 颜色)
{
    HBRUSH 刷;
    RECT 区;
    if (dc == NULL) return;
    刷 = CreateSolidBrush(hb_win_颜色(颜色));
    if (刷 == NULL) return;
    区.left = 0; 区.top = 0; 区.right = hb_win_宽; 区.bottom = hb_win_高;
    FillRect(dc, &区, 刷);
    DeleteObject(刷);
}

/* Windows 虚拟键 → HB 键码(`图形键C`):有名字的那一族**同号**,认不出的给 0(键盘上总有怪键) */
static int hb_win_键码(unsigned 键)
{
    if (键 >= (unsigned)'A' && 键 <= (unsigned)'Z') return (int)键;
    if (键 >= (unsigned)'0' && 键 <= (unsigned)'9') return (int)键;
    if (键 >= (unsigned)VK_F1 && 键 <= (unsigned)VK_F12) return (int)键;
    switch ((int)键)
    {
        case VK_BACK:    return 8;
        case VK_TAB:     return 9;
        case VK_RETURN:  return 13;
        case VK_SHIFT: case VK_LSHIFT: case VK_RSHIFT:       return 16;
        case VK_CONTROL: case VK_LCONTROL: case VK_RCONTROL: return 17;
        case VK_MENU: case VK_LMENU: case VK_RMENU:          return 18;
        case VK_ESCAPE:  return 27;
        case VK_SPACE:   return 32;
        case VK_PRIOR:   return 33;
        case VK_NEXT:    return 34;
        case VK_END:     return 35;
        case VK_HOME:    return 36;
        case VK_LEFT:    return 37;
        case VK_UP:      return 38;
        case VK_RIGHT:   return 39;
        case VK_DOWN:    return 40;
        case VK_INSERT:  return 45;
        case VK_DELETE:  return 46;
        default:         return 0;
    }
}

/* 鼠标消息 → HB 鼠标键码(1 左 / 2 中 / 3 右;VK_MBUTTON 是 4,别直接拿虚拟键当键码) */
static int hb_win_鼠标码(unsigned 消息)
{
    switch (消息)
    {
        case WM_LBUTTONDOWN: case WM_LBUTTONUP: return 1;
        case WM_MBUTTONDOWN: case WM_MBUTTONUP: return 2;
        case WM_RBUTTONDOWN: case WM_RBUTTONUP: return 3;
        default: return 0;
    }
}

static void hb_win_拆窗口(void);

static LRESULT CALLBACK hb_win_窗口过程(HWND 窗, UINT 消息, WPARAM 键, LPARAM 位)
{
    int 横 = (int)(short)LOWORD(位);
    int 纵 = (int)(short)HIWORD(位);
    switch (消息)
    {
        case WM_CLOSE:
            hb_gfx_入队(1, 0, 0, 0);              /* 关窗请求(与桌面壳 `Closing` → `Close` 同形) */
            hb_win_用户关过 = 1;
            hb_gfx_窗口被用户关掉();
            hb_win_拆窗口();
            return 0;
        case WM_DESTROY:
            return 0;
        case WM_ERASEBKGND:
            return 1;                             /* 我们整块重画,不让系统先刷一遍白底(免得闪) */
        case WM_PAINT:
        {
            PAINTSTRUCT 画;
            HDC dc = BeginPaint(窗, &画);
            if (hb_win_显dc != NULL) BitBlt(dc, 0, 0, hb_win_宽, hb_win_高, hb_win_显dc, 0, 0, SRCCOPY);
            EndPaint(窗, &画);
            return 0;
        }
        case WM_KEYDOWN:
        case WM_SYSKEYDOWN:
            /* Alt+F4 / Alt+空格 交回系统(窗口还得关得掉),其余键盘按 HB 口径入队 */
            if (消息 == WM_SYSKEYDOWN && (键 == (WPARAM)VK_F4 || 键 == (WPARAM)VK_SPACE)
                && (GetKeyState(VK_MENU) & 0x8000) != 0)
                return DefWindowProcW(窗, 消息, 键, 位);
            hb_gfx_入队(2, hb_win_键码((unsigned)键), 0, 0);
            return 0;
        case WM_MOUSEMOVE:
            hb_gfx_入队(3, 0, 横, 纵);
            return 0;
        case WM_LBUTTONDOWN: case WM_MBUTTONDOWN: case WM_RBUTTONDOWN:
            hb_gfx_入队(4, hb_win_鼠标码((unsigned)消息), 横, 纵);
            return 0;
        case WM_LBUTTONUP: case WM_MBUTTONUP: case WM_RBUTTONUP:
            hb_gfx_入队(5, hb_win_鼠标码((unsigned)消息), 横, 纵);
            return 0;
        default:
            return DefWindowProcW(窗, 消息, 键, 位);
    }
}

static void hb_win_收内存(void)
{
    if (hb_win_画dc != NULL)
    {
        if (hb_win_画旧位图 != NULL) SelectObject(hb_win_画dc, hb_win_画旧位图);
        if (hb_win_字体 != NULL) SelectObject(hb_win_画dc, GetStockObject(SYSTEM_FONT));
        DeleteDC(hb_win_画dc);
        hb_win_画dc = NULL;
    }
    if (hb_win_显dc != NULL)
    {
        if (hb_win_显旧位图 != NULL) SelectObject(hb_win_显dc, hb_win_显旧位图);
        DeleteDC(hb_win_显dc);
        hb_win_显dc = NULL;
    }
    if (hb_win_画位图 != NULL) { DeleteObject(hb_win_画位图); hb_win_画位图 = NULL; }
    if (hb_win_显位图 != NULL) { DeleteObject(hb_win_显位图); hb_win_显位图 = NULL; }
    if (hb_win_字体 != NULL) { DeleteObject(hb_win_字体); hb_win_字体 = NULL; }
    hb_win_宽 = 0;
    hb_win_高 = 0;
}

/* 开一个真窗口(只支持一个)。开不出来给 0 —— 能力层把它转成状态 25,不让程序崩 */
static int hb_win_打开(const hb_u8 *标题, hb_u32 标题长, int 宽, int 高)
{
    WNDCLASSEXW 类;
    RECT 框;
    wchar_t *宽标题;
    HDC 屏;
    int 画宽 = 宽 <= 0 ? 320 : 宽;          /* 与桌面壳 `宽 <= 0 ? 320 : 宽` 同口径 */
    int 画高 = 高 <= 0 ? 200 : 高;

    if (!hb_win_活 || hb_win_句柄 != NULL) return 0;

    if (!hb_win_类注册)
    {
        memset(&类, 0, sizeof(类));
        类.cbSize = sizeof(类);
        类.style = CS_HREDRAW | CS_VREDRAW;
        类.lpfnWndProc = hb_win_窗口过程;
        类.hInstance = GetModuleHandleW(NULL);
        类.hCursor = LoadCursorW(NULL, IDC_ARROW);
        类.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
        类.lpszClassName = hb_win_类名;
        if (RegisterClassExW(&类) == 0) return 0;
        hb_win_类注册 = 1;
    }

    宽标题 = hb_win_宽串(标题, 标题长);
    框.left = 0; 框.top = 0; 框.right = 画宽; 框.bottom = 画高;
    AdjustWindowRect(&框, WS_OVERLAPPEDWINDOW, FALSE);   /* 让**客户区**正好是 画宽 × 画高:坐标 1:1 */
    hb_win_句柄 = CreateWindowExW(0, hb_win_类名, 宽标题 != NULL ? 宽标题 : L"HB 图形窗口",
        WS_OVERLAPPEDWINDOW, CW_USEDEFAULT, CW_USEDEFAULT,
        框.right - 框.left, 框.bottom - 框.top, NULL, NULL, GetModuleHandleW(NULL), NULL);
    if (宽标题 != NULL) free(宽标题);
    if (hb_win_句柄 == NULL) return 0;

    屏 = GetDC(hb_win_句柄);
    hb_win_画dc = CreateCompatibleDC(屏);
    hb_win_画位图 = CreateCompatibleBitmap(屏, 画宽, 画高);
    hb_win_显dc = CreateCompatibleDC(屏);
    hb_win_显位图 = CreateCompatibleBitmap(屏, 画宽, 画高);
    ReleaseDC(hb_win_句柄, 屏);
    if (hb_win_画dc == NULL || hb_win_画位图 == NULL || hb_win_显dc == NULL || hb_win_显位图 == NULL)
    {
        hb_win_拆窗口();
        return 0;
    }

    hb_win_画旧位图 = (HBITMAP)SelectObject(hb_win_画dc, hb_win_画位图);
    hb_win_显旧位图 = (HBITMAP)SelectObject(hb_win_显dc, hb_win_显位图);
    hb_win_宽 = 画宽;
    hb_win_高 = 画高;
    /* 新宋体:等宽、ASCII 半角 ⇒ 与 `取文字宽度` 的"ASCII 1 格 / 中文 2 格"同一口径 */
    hb_win_字体 = CreateFontW(-14, 0, 0, 0, FW_NORMAL, 0, 0, 0, DEFAULT_CHARSET,
        OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        FIXED_PITCH | FF_MODERN, hb_win_字体名);
    if (hb_win_字体 != NULL) SelectObject(hb_win_画dc, hb_win_字体);
    SetBkMode(hb_win_画dc, TRANSPARENT);
    SetTextAlign(hb_win_画dc, TA_LEFT | TA_TOP);
    hb_win_底色(hb_win_画dc, 0xFF000000u);
    hb_win_底色(hb_win_显dc, 0xFF000000u);
    ShowWindow(hb_win_句柄, SW_SHOW);
    UpdateWindow(hb_win_句柄);
    return 1;
}

static void hb_win_拆窗口(void)
{
    if (hb_win_句柄 != NULL)
    {
        HWND 旧 = hb_win_句柄;
        hb_win_句柄 = NULL;                   /* 先摘记号:`DestroyWindow` 会回调进来(WM_DESTROY) */
        DestroyWindow(旧);
    }
    hb_win_收内存();
}

/* 画布在不在就是这一档的闸:`hb_win_画dc` 只有"真窗口开着"时才有,
   所以不给 `--hb-窗口` 时下面四个都是空转;**自检也不必开窗** —— 它自己摆一块
   `GetDC(NULL)` 的兼容位图当画布,画完用 `GetPixel` 读回来(见「C后端图形窗口」那一组)。 */
static void hb_win_清屏(hb_u32 颜色)
{
    if (hb_win_画dc == NULL) return;
    hb_win_底色(hb_win_画dc, 颜色);
}

static void hb_win_画矩形(int x, int y, int 宽, int 高, hb_u32 颜色)
{
    HBRUSH 刷;
    RECT 区;
    if (hb_win_画dc == NULL) return;
    if (宽 <= 0 || 高 <= 0) return;           /* 与桌面壳 `Math.Max(0, …)` 同口径:空的就不画 */
    刷 = CreateSolidBrush(hb_win_颜色(颜色));
    if (刷 == NULL) return;
    区.left = x; 区.top = y; 区.right = x + 宽; 区.bottom = y + 高;
    FillRect(hb_win_画dc, &区, 刷);
    DeleteObject(刷);
}

static void hb_win_画线(int x1, int y1, int x2, int y2, hb_u32 颜色)
{
    HPEN 笔;
    HGDIOBJ 旧;
    if (hb_win_画dc == NULL) return;
    笔 = CreatePen(PS_SOLID, 1, hb_win_颜色(颜色));
    if (笔 == NULL) return;
    旧 = SelectObject(hb_win_画dc, 笔);
    MoveToEx(hb_win_画dc, x1, y1, NULL);
    LineTo(hb_win_画dc, x2, y2);
    SelectObject(hb_win_画dc, 旧);
    DeleteObject(笔);
}

static void hb_win_画文字(int x, int y, const hb_u8 *文本, hb_u32 长, hb_u32 颜色)
{
    wchar_t *宽;
    if (hb_win_画dc == NULL) return;
    宽 = hb_win_宽串(文本, 长);
    if (宽 == NULL) return;                   /* 空串 / 不是 UTF-8:画不出来不算错(与桌面壳那一档一致) */
    SetTextColor(hb_win_画dc, hb_win_颜色(颜色));
    TextOutW(hb_win_画dc, x, y, 宽, (int)wcslen(宽));
    free(宽);
}

/* **贴位图**(R4-12):把 HB 位图那一块像素(0xAARRGGBB、行优先、紧排)一次画到**这一帧的画布**上。
   与 `画矩形` / `画线` 同一档(都往 `hb_win_画dc` 上画,`刷新` 才提交出去):
   `SetDIBitsToDevice` 吃的就是"32 位 BI_RGB、自上而下"的 DIB,与 HB 位图的排法**同一份**,
   所以这里不用逐像素搬 —— 一次调用贴一整块,那正是这一族省往返的地方。 */
static void hb_win_贴位图(int x, int y, const hb_u32 *像素, int 宽, int 高)
{
    BITMAPINFOHEADER 头;
    if (hb_win_画dc == NULL || 像素 == NULL || 宽 <= 0 || 高 <= 0) return;
    memset(&头, 0, sizeof(头));
    头.biSize = sizeof(头);
    头.biWidth = 宽;
    头.biHeight = -高;                 /* **负** = 自上而下,与 HB 位图的行优先同向(写正数会上下颠倒) */
    头.biPlanes = 1;
    头.biBitCount = 32;
    头.biCompression = BI_RGB;         /* BI_RGB 的 32 位 DIB:内存里就是 B,G,R,A(与 0xAARRGGBB 一致) */
    SetDIBitsToDevice(hb_win_画dc, x, y, (DWORD)宽, (DWORD)高, 0, 0, 0, (UINT)高,
                      (const void *)像素, (const BITMAPINFO *)&头, DIB_RGB_COLORS);
}
/* `刷新` 这一档:提交出去 + 留一份给 WM_PAINT + 画布刷回黑底(下一帧与"新画布"等价) */
static void hb_win_刷新(int 命令数)
{
    HDC dc;
    if (!hb_win_活 || hb_win_句柄 == NULL || hb_win_画dc == NULL) return;
    if (命令数 <= 0) return;                  /* H40:空帧不许擦屏 */
    BitBlt(hb_win_显dc, 0, 0, hb_win_宽, hb_win_高, hb_win_画dc, 0, 0, SRCCOPY);
    dc = GetDC(hb_win_句柄);
    if (dc != NULL)
    {
        BitBlt(dc, 0, 0, hb_win_宽, hb_win_高, hb_win_画dc, 0, 0, SRCCOPY);
        ReleaseDC(hb_win_句柄, dc);
    }
    hb_win_底色(hb_win_画dc, 0xFF000000u);
}

/* 收一遍系统消息(不阻塞):键盘 / 鼠标 / 关窗请求都在这儿变成 HB 事件 */
static void hb_win_泵(void)
{
    MSG 消;
    if (!hb_win_活) return;
    while (PeekMessageW(&消, NULL, 0, 0, PM_REMOVE))
    {
        TranslateMessage(&消);
        DispatchMessageW(&消);
    }
}

static void hb_win_睡(int 毫秒)
{
    if (!hb_win_活) return;
    if (毫秒 > 0) Sleep((DWORD)毫秒);
    hb_win_泵();
}

#elif defined(__linux__)
/* ══════════════ ② Linux:X11(运行时 dlopen,零依赖) ══════════════

   这一档**不含任何 X11 头、不链接任何 X 库**:Xlib 的类型与函数原型在这里自己声明一份
   (只声明要用到的那些),`libX11.so.6` 在运行期 `dlopen` 进来。三个直接好处:
     · 编的时候零依赖(与 Win32 那一档同一条口径),`cc -std=c11 x.c -o x` 照旧一条命令;
     · 这台机器 / 容器里没有 X 也编得过、跑得起来 —— 开窗那一步给 0,能力层转成状态 25;
     · 以后要加别的平台,照这一段的形状再写一段即可(X11 是 `#elif defined(__linux__)`,
     macOS 那一档**如实登记不做**)。
   标识符一律 ASCII(与全文件同一口径):Xlib 那套名字照抄,自己的东西用 hb_ 前缀。 */

#include <dlfcn.h>
#include <locale.h>

typedef struct hb_x_display hb_x_display;
typedef unsigned long hb_x_window;
typedef unsigned long hb_x_pixmap;
typedef unsigned long hb_x_drawable;
typedef unsigned long hb_x_colormap;
typedef unsigned long hb_x_atom;
typedef unsigned long hb_x_visualid;
typedef unsigned long hb_x_keycode;
typedef unsigned long hb_x_keysym;
typedef unsigned long hb_x_font;
typedef unsigned long hb_x_gc;
typedef unsigned long hb_x_id;

typedef struct hb_x_visual
{
    void *ext_data;
    hb_x_visualid visualid;
    int class_;
    unsigned long red_mask, green_mask, blue_mask;
    int bits_per_rgb;
    int map_entries;
} hb_x_visual;

typedef struct hb_x_setwindowattributes
{
    int x, y;
    int width, height;
    int border_width;
    int depth;
    /* ⚠ 这几格**必须按 Xlib 的宽度声明**:`visual` 是 `Visual *`、`root` 是 `Window`(unsigned long)、
       `colormap` 是 `Colormap`(unsigned long)。第一版把它们写成 `int`,而 64 位下指针 / long 是 8 字节
       ⇒ `depth` 之后的字段**整体错位**,`map_state` / `override_redirect` 读回来的是别的字段的碎片
       (实测:窗口明明是 `IsViewable`,`map_state` 读出来是 0、`override_redirect` 读出来是 163917)。
       这种错不报任何错、状态码全 0,只有拿 `xwininfo` 去问 X 服务器才看得见。 */
    void *visual;
    hb_x_window root;
    int class_;
    int bit_gravity, win_gravity;
    int backing_store;
    unsigned long backing_planes, backing_pixel;
    int save_under;
    hb_x_colormap colormap;
    int map_installed;
    int map_state;
    long all_event_masks;
    long your_event_mask;
    long do_not_propagate_mask;
    int override_redirect;
    void *screen;
} hb_x_setwindowattributes;

typedef struct hb_x_windowattributes
{
    int x, y;
    int width, height;
    int border_width;
    int depth;
    void *visual;                                  /* `Visual *` —— 8 字节,别写成 int(见上面那条) */
    hb_x_window root;                              /* `Window` —— 8 字节 */
    int class_;
    int bit_gravity, win_gravity;
    int backing_store;
    unsigned long backing_planes, backing_pixel;
    int save_under;
    hb_x_colormap colormap;                        /* `Colormap` —— 8 字节 */
    int map_installed;
    int map_state;
    long all_event_masks;
    long your_event_mask;
    long do_not_propagate_mask;
    int override_redirect;
    void *screen;
} hb_x_windowattributes;

typedef struct hb_x_keyevent
{
    int type;
    unsigned long serial;
    int send_event;
    hb_x_display *display;
    hb_x_window window;
    hb_x_window root;
    hb_x_window subwindow;
    unsigned long time;
    int x, y;
    int x_root, y_root;
    unsigned int state;
    unsigned int keycode;
    int same_screen;
} hb_x_keyevent;

typedef struct hb_x_button_event
{
    int type;
    unsigned long serial;
    int send_event;
    hb_x_display *display;
    hb_x_window window;
    hb_x_window root;
    hb_x_window subwindow;
    unsigned long time;
    int x, y;
    int x_root, y_root;
    unsigned int state;
    unsigned int button;
    int same_screen;
} hb_x_button_event;

typedef struct hb_x_motion_event
{
    int type;
    unsigned long serial;
    int send_event;
    hb_x_display *display;
    hb_x_window window;
    hb_x_window root;
    hb_x_window subwindow;
    unsigned long time;
    int x, y;
    int x_root, y_root;
    unsigned int state;
    char is_hint;
    int same_screen;
} hb_x_motion_event;

typedef struct hb_x_client_message_event
{
    int type;
    unsigned long serial;
    int send_event;
    hb_x_display *display;
    hb_x_window window;
    hb_x_atom message_type;
    int format;
    union { char b[20]; short s[10]; long l[5]; } data;
} hb_x_client_message_event;

typedef union hb_x_event
{
    int type;
    hb_x_keyevent xkey;
    hb_x_button_event xbutton;
    hb_x_motion_event xmotion;
    hb_x_client_message_event xclient;
    long pad[24];
} hb_x_event;

/* Xlib 里用得着的那一族(名字与原形照抄;只在 `hb_x_` 这个名字空间里挂一次) */
typedef struct hb_xlib
{
    void *模块;
    hb_x_display *(*XOpenDisplay)(const char *);
    int (*XCloseDisplay)(hb_x_display *);
    int (*XDefaultScreen)(hb_x_display *);
    hb_x_window (*XRootWindow)(hb_x_display *, int);
    unsigned long (*XBlackPixel)(hb_x_display *, int);
    unsigned long (*XWhitePixel)(hb_x_display *, int);
    hb_x_window (*XCreateSimpleWindow)(hb_x_display *, hb_x_window, int, int, unsigned int, unsigned int,
                                       unsigned int, unsigned long, unsigned long);
    int (*XDestroyWindow)(hb_x_display *, hb_x_window);
    int (*XMapWindow)(hb_x_display *, hb_x_window);
    int (*XUnmapWindow)(hb_x_display *, hb_x_window);
    /* 离屏画布那一族:**与 Win32 那一档的 `CreateCompatibleDC` / `BitBlt` 同形** ——
       没有它就只能直接往窗口上画,而 X11 的"提交后把画布刷回黑底"会把刚画的那一帧一起擦掉
       (实测:窗口 IsViewable、状态码全 0,抓屏是全黑)。 */
    int (*XDefaultDepth)(hb_x_display *, int);
    hb_x_pixmap (*XCreatePixmap)(hb_x_display *, hb_x_drawable, unsigned int, unsigned int, unsigned int);
    int (*XFreePixmap)(hb_x_display *, hb_x_pixmap);
    int (*XCopyArea)(hb_x_display *, hb_x_drawable, hb_x_drawable, hb_x_gc,
                     int, int, unsigned int, unsigned int, int, int);
    int (*XStoreName)(hb_x_display *, hb_x_window, const char *);
    int (*XSelectInput)(hb_x_display *, hb_x_window, long);
    hb_x_atom (*XInternAtom)(hb_x_display *, const char *, int);
    int (*XSetWMProtocols)(hb_x_display *, hb_x_window, hb_x_atom *, int);
    int (*XFlush)(hb_x_display *);
    int (*XSync)(hb_x_display *, int);
    int (*XPending)(hb_x_display *);
    int (*XNextEvent)(hb_x_display *, hb_x_event *);
    int (*XLookupString)(hb_x_keyevent *, char *, int, hb_x_keysym *, void *);
    hb_x_keysym (*XLookupKeysym)(hb_x_keyevent *, int);
    hb_x_gc (*XCreateGC)(hb_x_display *, hb_x_drawable, unsigned long, void *);
    int (*XFreeGC)(hb_x_display *, hb_x_gc);
    int (*XSetForeground)(hb_x_display *, hb_x_gc, unsigned long);
    int (*XSetBackground)(hb_x_display *, hb_x_gc, unsigned long);
    int (*XSetLineAttributes)(hb_x_display *, hb_x_gc, unsigned int, int, int, int);
    int (*XSetGraphicsExposures)(hb_x_display *, hb_x_gc, int);
    int (*XFillRectangle)(hb_x_display *, hb_x_drawable, hb_x_gc, int, int, unsigned int, unsigned int);
    int (*XDrawLine)(hb_x_display *, hb_x_drawable, hb_x_gc, int, int, int, int);
    int (*XDrawString)(hb_x_display *, hb_x_drawable, hb_x_gc, int, int, const char *, int);
    void *(*XCreateFontSet)(hb_x_display *, const char *, char ***, int *, char **);
    void (*XFreeFontSet)(hb_x_display *, void *);
    void (*XFreeStringList)(char **);
    int (*Xutf8DrawString)(hb_x_display *, hb_x_drawable, void *, hb_x_gc, int, int, const char *, int);
    int (*Xutf8TextExtents)(void *, const char *, int, void *, void *);
    int (*XGetWindowAttributes)(hb_x_display *, hb_x_window, void *);
    void *(*XGetImage)(hb_x_display *, hb_x_drawable, int, int, unsigned int, unsigned int, unsigned long, int);
    unsigned long (*XGetPixel)(void *, int, int);
    int (*XDestroyImage)(void *);
    /* R4-12:`贴位图` 要那一手(把一整块像素一次贴到画布上)—— 三个都是 Xlib 核心符号,必挂 */
    hb_x_visual *(*XDefaultVisual)(hb_x_display *, int);
    void *(*XCreateImage)(hb_x_display *, hb_x_visual *, unsigned int, int, int,
                          char *, unsigned int, unsigned int, int, int);
    int (*XPutImage)(hb_x_display *, hb_x_drawable, hb_x_gc, void *,
                     int, int, int, int, unsigned int, unsigned int);
} hb_xlib;

static hb_xlib hb_x;                 /* 挂上来的那一份(模块 == NULL = 没挂上) */

static hb_x_display *hb_win_显;
static hb_x_window hb_win_句柄 = 0;
static hb_x_gc hb_win_gc = 0;
static void *hb_win_字体集 = NULL;
static int hb_win_屏号 = 0;
static hb_x_atom hb_win_关窗原子 = 0;
static unsigned long hb_win_黑 = 0;
static hb_x_pixmap hb_win_画布 = 0;             /* 正在攒的这一帧(与 Win32 那一档的 `hb_win_画dc` 同形) */
static hb_x_pixmap hb_win_已提交 = 0;           /* 上一次提交出去的那一帧(Expose 重贴它;与 `hb_win_显dc` 同形) */
static unsigned long hb_win_画布色 = 0;         /* 画布当前底色(提交后回到黑) */

/* X11 事件/掩码那几个常量(Xlib 头里的值,照抄;别改) */
#define HB_X_KEYPRESS        2
#define HB_X_BUTTONPRESS     4
#define HB_X_BUTTONRELEASE   5
#define HB_X_MOTIONNOTIFY    6
#define HB_X_EXPOSE          12
#define HB_X_CLIENTMESSAGE   33
#define HB_X_KEYPRESSMASK    (1L << 0)
#define HB_X_KEYRELEASEMASK  (1L << 1)
#define HB_X_BUTTONPRESSMASK (1L << 2)
#define HB_X_BUTTONRELEASEMASK (1L << 3)
#define HB_X_POINTERMOTIONMASK (1L << 6)
#define HB_X_EXPOSUREMASK    (1L << 15)
#define HB_X_STRUCTUREMASK   (1L << 17)
#define HB_X_BUTTON1         1
#define HB_X_BUTTON2         2
#define HB_X_BUTTON3         3

/* 只声明用得到的那几个符号名(字符串在 dlopen 里按名字找,不引头文件)。
   `能不能缺` = 1 的那几个找不着不算错(缺了只是少一项便利,别把整档拖垮);别的缺一个就整档不成 —— */
typedef struct { const char *名; void **落点; int 能不能缺; } hb_x_符号;

static int hb_x_挂上(void)
{
    static const hb_x_符号 表[] = {
        { "XOpenDisplay", (void **)&hb_x.XOpenDisplay, 0 },
        { "XCloseDisplay", (void **)&hb_x.XCloseDisplay, 0 },
        { "XDefaultScreen", (void **)&hb_x.XDefaultScreen, 0 },
        { "XRootWindow", (void **)&hb_x.XRootWindow, 0 },
        { "XBlackPixel", (void **)&hb_x.XBlackPixel, 0 },
        { "XWhitePixel", (void **)&hb_x.XWhitePixel, 0 },
        { "XCreateSimpleWindow", (void **)&hb_x.XCreateSimpleWindow, 0 },
        { "XDestroyWindow", (void **)&hb_x.XDestroyWindow, 0 },
        { "XMapWindow", (void **)&hb_x.XMapWindow, 0 },
        { "XUnmapWindow", (void **)&hb_x.XUnmapWindow, 0 },
        { "XDefaultDepth", (void **)&hb_x.XDefaultDepth, 0 },
        { "XCreatePixmap", (void **)&hb_x.XCreatePixmap, 0 },
        { "XFreePixmap", (void **)&hb_x.XFreePixmap, 0 },
        { "XCopyArea", (void **)&hb_x.XCopyArea, 0 },
        { "XStoreName", (void **)&hb_x.XStoreName, 0 },
        { "XSelectInput", (void **)&hb_x.XSelectInput, 0 },
        { "XInternAtom", (void **)&hb_x.XInternAtom, 0 },
        { "XSetWMProtocols", (void **)&hb_x.XSetWMProtocols, 0 },
        { "XFlush", (void **)&hb_x.XFlush, 0 },
        { "XSync", (void **)&hb_x.XSync, 0 },
        { "XPending", (void **)&hb_x.XPending, 0 },
        { "XNextEvent", (void **)&hb_x.XNextEvent, 0 },
        { "XLookupString", (void **)&hb_x.XLookupString, 0 },
        { "XLookupKeysym", (void **)&hb_x.XLookupKeysym, 0 },
        { "XCreateGC", (void **)&hb_x.XCreateGC, 0 },
        { "XFreeGC", (void **)&hb_x.XFreeGC, 0 },
        { "XSetForeground", (void **)&hb_x.XSetForeground, 0 },
        { "XSetBackground", (void **)&hb_x.XSetBackground, 0 },
        { "XSetLineAttributes", (void **)&hb_x.XSetLineAttributes, 0 },
        { "XSetGraphicsExposures", (void **)&hb_x.XSetGraphicsExposures, 0 },
        { "XFillRectangle", (void **)&hb_x.XFillRectangle, 0 },
        { "XDrawLine", (void **)&hb_x.XDrawLine, 0 },
        { "XDrawString", (void **)&hb_x.XDrawString, 0 },
        { "XCreateFontSet", (void **)&hb_x.XCreateFontSet, 0 },
        { "XFreeFontSet", (void **)&hb_x.XFreeFontSet, 0 },
        { "XFreeStringList", (void **)&hb_x.XFreeStringList, 1 },
        { "Xutf8DrawString", (void **)&hb_x.Xutf8DrawString, 0 },
        { "Xutf8TextExtents", (void **)&hb_x.Xutf8TextExtents, 0 },
        /* 这三件只有"从窗口读回像素"那一档要用(自检 / 人工留证);缺了不算这一档不成 */
        { "XGetWindowAttributes", (void **)&hb_x.XGetWindowAttributes, 1 },
        { "XGetImage", (void **)&hb_x.XGetImage, 1 },
        { "XGetPixel", (void **)&hb_x.XGetPixel, 1 },
        { "XDestroyImage", (void **)&hb_x.XDestroyImage, 1 },
        { "XDefaultVisual", (void **)&hb_x.XDefaultVisual, 0 },
        { "XCreateImage", (void **)&hb_x.XCreateImage, 0 },
        { "XPutImage", (void **)&hb_x.XPutImage, 0 },
        { NULL, NULL, 0 }
    };
    static const char *候选[] = { "libX11.so.6", "libX11.so", NULL };
    int i, k;

    if (hb_x.模块 != NULL) return 1;
    for (k = 0; 候选[k] != NULL && hb_x.模块 == NULL; k++)
        hb_x.模块 = dlopen(候选[k], RTLD_LAZY | RTLD_LOCAL);
    if (hb_x.模块 == NULL) return 0;
    for (i = 0; 表[i].名 != NULL; i++)
    {
        *表[i].落点 = dlsym(hb_x.模块, 表[i].名);
        if (*表[i].落点 == NULL && !表[i].能不能缺)   /* 少一个就整档不成:别半挂着用 */
        {
            dlclose(hb_x.模块);
            hb_x.模块 = NULL;
            return 0;
        }
    }
    return 1;
}

/* HB 颜色(0xAARRGGBB)→ X11 的像素值(0x00RRGGBB,TrueColor 真彩屏) */
static unsigned long hb_win_颜色(hb_u32 值)
{
    return (unsigned long)(值 & 0x00FFFFFFu);
}

/* 字体基名表(逗号分隔;`XCreateFontSet` 按这张表给**每个字符集**各找一份字体)。
   要点:`LANG=C.UTF-8` 时 Xlib 并不按"UTF-8 只要 ISO10646-1"办,而是要一长串遗留字符集
   (`ISO8859-*` / `KOI8-R` / `JISX0208` / `KSC5601` / **`GB2312.1980-0`** …),少哪个哪个字符就画不出来。
   容器里实测各档:
     ① `-*-fixed-*`                 → 15 份,缺 `KSC5601` + **`GB2312`**(中文一个像素都不出);
     ② 加 `-wenquanyi-*`(文泉驿)  → 只补上 iso10646-1,**补不上 GB2312**(26.04 的 `xfonts-wqy` 只有这一种编码);
     ③ 再加 `-*-song-*`(`xfonts-intl-chinese` 的国标宋)→ 16 份,**只差韩文** ⇒ 中文出得来。
   所以镜像里那三个字体包**一个都不能少**;一张都挂不上才退回 `XDrawString`(只出 ASCII,UTF-8 会被
   按 Latin-1 逐字节画成乱码 —— 比"什么都不画"更容易看出不对)。 */
static const char *hb_win_字体名 =
    "-*-fixed-medium-r-normal--14-*-*-*-c-*-*-*,"
    "-*-fixed-*-*-*-*-14-*-*-*-*-*-*,"
    "-wenquanyi-*-medium-r-normal--14-*-*-*-*-*-iso10646-1,"
    "-*-song-medium-r-normal--16-*-*-*-*-*-*";

static int hb_win_键码(hb_x_keysym 符)
{
    if (符 >= 0x20u && 符 < 0x7Fu) return (int)符;        /* 可见 ASCII 就是它自己(与 Windows 同口径) */
    if (符 >= 0xFFBEu && 符 <= 0xFFC9u) return 112 + (int)(符 - 0xFFBEu);   /* F1~F12 */
    switch ((unsigned long)符)
    {
        case 0xFF08u: return 8;                            /* BackSpace */
        case 0xFF09u: return 9;                            /* Tab */
        case 0xFF0Du: return 13;                           /* Return */
        case 0xFFE1u: case 0xFFE2u: return 16;             /* Shift_L / Shift_R */
        case 0xFFE3u: case 0xFFE4u: return 17;             /* Control_L / Control_R */
        case 0xFFE9u: case 0xFFEAu: return 18;             /* Alt_L / Alt_R */
        case 0xFF1Bu: return 27;                           /* Escape */
        case 0x0020u: return 32;                           /* space(XLookupKeysym 通常给 0x20) */
        case 0xFF55u: return 33;                           /* Prior(= 上一页)*/
        case 0xFF56u: return 34;                           /* Next */
        case 0xFF57u: return 35;                           /* End */
        case 0xFF50u: return 36;                           /* Home */
        case 0xFF51u: return 37;                           /* Left */
        case 0xFF52u: return 38;                           /* Up */
        case 0xFF53u: return 39;                           /* Right */
        case 0xFF54u: return 40;                           /* Down */
        case 0xFF63u: return 45;                           /* Insert */
        case 0xFFFFu: return 46;                           /* Delete */
        default: return 0;                                 /* 键盘上总有怪键,与 Windows 那一档同口径 */
    }
}

/* 鼠标键位:X11 的 1/2/3 就是左/中/右,与 HB 的 1/2/3 同号(Windows 那边要翻 VK_MBUTTON=4) */
static int hb_win_鼠标码(unsigned 键) { return (键 >= 1u && 键 <= 3u) ? (int)键 : 0; }

static void hb_win_拆窗口(void);

/* UTF-8 字节段按"整段"用 Xutf8DrawString 画(有了字体集);没有字体集就退回 XDrawString(ASCII)。
   说明:字体集一定要拿 locale 挂(`XCreateFontSet` 的基名列表里带 fixed),中文才出得来。 */
static void hb_win_画文字(int x, int y, const hb_u8 *文本, hb_u32 长, hb_u32 颜色)
{
    char 缓冲[1024];
    hb_u32 用 = 长;
    if (hb_win_画布 == 0 || hb_win_gc == 0) return;
    if (文本 == NULL || 长 == 0u) return;
    if (用 > sizeof(缓冲) - 1u) 用 = (hb_u32)(sizeof(缓冲) - 1u);
    memcpy(缓冲, 文本, 用);
    缓冲[用] = '\0';
    hb_x.XSetForeground(hb_win_显, hb_win_gc, hb_win_颜色(颜色));
    if (hb_win_字体集 != NULL)
        hb_x.Xutf8DrawString(hb_win_显, hb_win_画布, hb_win_字体集, hb_win_gc, x, y + 12, 缓冲, (int)用);
    else
        hb_x.XDrawString(hb_win_显, hb_win_画布, hb_win_gc, x, y + 12, 缓冲, (int)用);
}

/* **贴位图**(R4-12,X11 那一半):把 HB 位图那块像素一次贴到**这一帧的画布**(离屏 Pixmap)上,
   与 `清屏` / `画矩形` / `画线` 同一档,`hb_win_刷新` 才把它提交到窗口与屏幕。

   ⚠⚠ **两个坑写在这儿,别踩第二遍**:
     ① `XPutImage` 要一个 `XImage`;**深度取本屏的默认深度**(Xvfb / 常见真机都是 24 ⇒ 每像素 32 位,
        低 24 位就是 0xRRGGBB,与 `hb_win_颜色` 那份口径一致;顶上那 8 位是 alpha,TrueColor 不看它);
     ② `XDestroyImage` **会把 `image->data` 一起 free 掉**(Xlib 的既定行为)——
        直接把 HB 位图那块缓冲区交进去,贴完就被释放 ⇒ 下一次再用就是野指针。
        所以这里**先拷一份**交给 XImage,让它去释放自己那一份(与 C# 侧 `贴位图` 命令带
        "像素快照"是同一个道理:交出去的东西不该与还在用的那份共用一块内存)。 */
static void hb_win_贴位图(int x, int y, const hb_u32 *像素, int 宽, int 高)
{
    void *图;
    hb_u32 *拷贝;
    size_t 字节 = (size_t)宽 * (size_t)高 * sizeof(hb_u32);
    if (hb_win_画布 == 0 || hb_win_gc == 0 || 像素 == NULL || 宽 <= 0 || 高 <= 0) return;
    拷贝 = (hb_u32 *)malloc(字节);
    if (拷贝 == NULL) return;
    memcpy(拷贝, 像素, 字节);
    图 = hb_x.XCreateImage(hb_win_显, hb_x.XDefaultVisual(hb_win_显, hb_win_屏号),
        (unsigned)hb_x.XDefaultDepth(hb_win_显, hb_win_屏号), 2 /* ZPixmap */, 0,
        (char *)拷贝, (unsigned)宽, (unsigned)高, 32, 0);
    if (图 == NULL) { free(拷贝); return; }
    hb_x.XPutImage(hb_win_显, hb_win_画布, hb_win_gc, 图, 0, 0, x, y, (unsigned)宽, (unsigned)高);
    hb_x.XDestroyImage(图);              /* 它自己会释放 `拷贝`(见上面坑 ②) */
}

/* 画布 = 一块**离屏 Pixmap**(与 Win32 那一档的 `hb_win_画dc` 同形):四个画图函数都往它上面画,
   `hb_win_刷新` 才把它贴到窗口(`hb_win_已提交`)与屏幕(`hb_win_句柄`)上,贴完把画布刷回黑底。
   第一版没有这一层 —— 直接往窗口上画,而"提交完把画布刷回黑底"那一句刷的就是屏幕本身
   ⇒ 每提交一帧就把刚画的擦掉,窗口永远全黑(状态码还全是 0)。X11 不像 Win32 那样自带
   "窗口 DC / 内存 DC"两层,这两层得自己造。 */
static void hb_win_清屏(hb_u32 颜色)
{
    if (hb_win_画布 == 0 || hb_win_gc == 0) return;
    hb_x.XSetForeground(hb_win_显, hb_win_gc, hb_win_颜色(颜色));
    hb_x.XFillRectangle(hb_win_显, hb_win_画布, hb_win_gc, 0, 0, (unsigned)hb_win_宽, (unsigned)hb_win_高);
    hb_win_画布色 = hb_win_颜色(颜色);
}

static void hb_win_画矩形(int x, int y, int 宽, int 高, hb_u32 颜色)
{
    if (hb_win_画布 == 0 || hb_win_gc == 0) return;
    if (宽 <= 0 || 高 <= 0) return;                   /* 与桌面壳 `Math.Max(0, …)` 同口径 */
    hb_x.XSetForeground(hb_win_显, hb_win_gc, hb_win_颜色(颜色));
    hb_x.XFillRectangle(hb_win_显, hb_win_画布, hb_win_gc, x, y, (unsigned)宽, (unsigned)高);
}

static void hb_win_画线(int x1, int y1, int x2, int y2, hb_u32 颜色)
{
    if (hb_win_画布 == 0 || hb_win_gc == 0) return;
    hb_x.XSetForeground(hb_win_显, hb_win_gc, hb_win_颜色(颜色));
    hb_x.XDrawLine(hb_win_显, hb_win_画布, hb_win_gc, x1, y1, x2, y2);
}

static void hb_win_刷新(int 命令数)
{
    if (!hb_win_活 || hb_win_句柄 == 0 || hb_win_画布 == 0 || hb_win_已提交 == 0) return;
    if (命令数 <= 0) return;                          /* H40:空帧不许擦屏 */
    /* 与 GDI 那一档同形:先留一份"已提交那一帧"(Expose 重贴它),再贴到屏幕上 */
    hb_x.XCopyArea(hb_win_显, hb_win_画布, hb_win_已提交, hb_win_gc, 0, 0,
                   (unsigned)hb_win_宽, (unsigned)hb_win_高, 0, 0);
    hb_x.XCopyArea(hb_win_显, hb_win_画布, hb_win_句柄, hb_win_gc, 0, 0,
                   (unsigned)hb_win_宽, (unsigned)hb_win_高, 0, 0);
    hb_x.XFlush(hb_win_显);
    hb_x.XSync(hb_win_显, 0);
    /* 与 GDI 那一档同形:提交完之后**画布**回到黑底(下一帧与"新画布"等价)。
       擦的必须是上面那块离屏 Pixmap —— 擦窗口就是把刚提交的那一帧擦掉。 */
    hb_x.XSetForeground(hb_win_显, hb_win_gc, hb_win_黑);
    hb_x.XFillRectangle(hb_win_显, hb_win_画布, hb_win_gc, 0, 0, (unsigned)hb_win_宽, (unsigned)hb_win_高);
    hb_win_画布色 = hb_win_黑;
}

static void hb_win_泵(void)
{
    hb_x_event 事;
    if (!hb_win_活 || hb_win_显 == NULL) return;
    while (hb_x.XPending(hb_win_显) > 0)
    {
        memset(&事, 0, sizeof(事));
        hb_x.XNextEvent(hb_win_显, &事);
        switch (事.type)
        {
            case HB_X_CLIENTMESSAGE:
                if ((hb_x_atom)事.xclient.data.l[0] == hb_win_关窗原子)
                {
                    hb_gfx_入队(1, 0, 0, 0);              /* 关窗请求(与桌面壳 `Closing` → `Close` 同形) */
                    hb_win_用户关过 = 1;
                    hb_gfx_窗口被用户关掉();
                    hb_win_拆窗口();
                    return;
                }
                break;
            case HB_X_KEYPRESS:
                hb_gfx_入队(2, hb_win_键码(hb_x.XLookupKeysym(&事.xkey, 0)), 0, 0);
                break;
            case HB_X_MOTIONNOTIFY:
                hb_gfx_入队(3, 0, 事.xmotion.x, 事.xmotion.y);
                break;
            case HB_X_BUTTONPRESS:
                hb_gfx_入队(4, hb_win_鼠标码(事.xbutton.button), 事.xbutton.x, 事.xbutton.y);
                break;
            case HB_X_BUTTONRELEASE:
                hb_gfx_入队(5, hb_win_鼠标码(事.xbutton.button), 事.xbutton.x, 事.xbutton.y);
                break;
            case HB_X_EXPOSE:
                /* 露出来就重贴"已提交那一帧"。X11 默认**没有**后备存储:收到 Expose 不自己重画,
                   露出来的地方就是一片垃圾(第一版把 Expose 扔进 `default` "交给窗口系统自己重画",
                   而窗口系统并不会重画)。 */
                if (hb_win_已提交 != 0 && hb_win_gc != 0)
                    hb_x.XCopyArea(hb_win_显, hb_win_已提交, hb_win_句柄, hb_win_gc, 0, 0,
                                   (unsigned)hb_win_宽, (unsigned)hb_win_高, 0, 0);
                break;
            default:
                break;                                    /* 别的事件这一档用不上 */
        }
    }
}

static void hb_win_睡(int 毫秒)
{
    if (!hb_win_活) return;
    if (毫秒 > 0)
    {
        struct timespec 歇;
        歇.tv_sec = 毫秒 / 1000;
        歇.tv_nsec = (long)(毫秒 % 1000) * 1000000L;
        nanosleep(&歇, NULL);
    }
    hb_win_泵();
}

/* 开一个真窗口(只支持一个)。开不出来给 0 —— 能力层把它转成状态 25,不让程序崩。
   这一档开不出来有三种正常情形(都不算错,如实给 0):没挂上 libX11 / 没有 DISPLAY / X 连不上。 */
static int hb_win_打开(const hb_u8 *标题, hb_u32 标题长, int 宽, int 高)
{
    int 画宽 = 宽 <= 0 ? 320 : 宽;                    /* 与桌面壳 `宽 <= 0 ? 320 : 宽` 同口径 */
    int 画高 = 高 <= 0 ? 200 : 高;
    char 标题缓冲[256];
    hb_u32 用 = 标题长;
    char **缺字 = NULL;
    int 缺字个数 = 0;

    if (!hb_win_活 || hb_win_句柄 != 0) return 0;
    if (!hb_x_挂上()) return 0;
    hb_win_显 = hb_x.XOpenDisplay(NULL);
    if (hb_win_显 == NULL) return 0;

    hb_win_屏号 = hb_x.XDefaultScreen(hb_win_显);
    hb_win_黑 = hb_x.XBlackPixel(hb_win_显, hb_win_屏号);
    /* **一格都不许省、顺序也别记错**(容器里实测踩过,而且是"编得过、跑起来全错"的那种):
       Xlib 的签名是 `XCreateSimpleWindow(显, 父窗, x, y, 宽, 高, border_width, border, background)` ——
       **一共 9 格**。所以是 `XRootWindow(显, 屏号)` 去当"父窗",后面跟
       `0, 0, 画宽, 画高, 0(边框), 边框色, 底色`。
       第一版写成 `(显, XRootWindow(…), 屏号, 0, 0, 画宽, 画高, 0)` —— **8 格**:`屏号` 顶掉了"父窗"那一格、
       整排往后错一位,而第 9 格 `background` 只能从寄存器里捡个垃圾值(实测属性里 `override_redirect=163917`)
       ⇒ 窗口以"绕过窗口管理器"的方式建出来、**永远不映射**(实测 `map_state=0`、抓屏全黑),
       而每一路状态码都是 0、什么都不报。**这种错只有真开一屏才看得见。 */
    hb_win_句柄 = hb_x.XCreateSimpleWindow(hb_win_显, hb_x.XRootWindow(hb_win_显, hb_win_屏号),
        0, 0, (unsigned)画宽, (unsigned)画高, 0, hb_win_黑, 0);
    if (hb_win_句柄 == 0) { hb_x.XCloseDisplay(hb_win_显); hb_win_显 = NULL; return 0; }

    if (标题 != NULL && 标题长 > 0u)
    {
        if (用 > sizeof(标题缓冲) - 1u) 用 = (hb_u32)(sizeof(标题缓冲) - 1u);
        memcpy(标题缓冲, 标题, 用);
        标题缓冲[用] = '\0';
        hb_x.XStoreName(hb_win_显, hb_win_句柄, 标题缓冲);
    }

    /* 点叉(WM_DELETE_WINDOW)要走协议表,不然点了叉 X 只会把连接断掉、我们看不到那件事 */
    hb_win_关窗原子 = hb_x.XInternAtom(hb_win_显, "WM_DELETE_WINDOW", 0);
    if (hb_win_关窗原子 != 0) hb_x.XSetWMProtocols(hb_win_显, hb_win_句柄, &hb_win_关窗原子, 1);

    hb_x.XSelectInput(hb_win_显, hb_win_句柄,
        HB_X_KEYPRESSMASK | HB_X_BUTTONPRESSMASK | HB_X_BUTTONRELEASEMASK
        | HB_X_POINTERMOTIONMASK | HB_X_EXPOSUREMASK | HB_X_STRUCTUREMASK);
    hb_x.XMapWindow(hb_win_显, hb_win_句柄);
    hb_win_gc = hb_x.XCreateGC(hb_win_显, hb_win_句柄, 0, NULL);
    if (hb_win_gc == 0)
    {
        hb_x.XDestroyWindow(hb_win_显, hb_win_句柄);
        hb_win_句柄 = 0;
        hb_x.XCloseDisplay(hb_win_显);
        hb_win_显 = NULL;
        return 0;
    }
    /* 两块离屏画布(与 Win32 那一档的 `CreateCompatibleDC` 同形):一块攒当前帧、一块存"已提交那一帧"。
       深度取屏幕默认深度 —— GC 是按深度建的,窗口与 Pixmap 同深度才贴得动。 */
    hb_win_画布 = hb_x.XCreatePixmap(hb_win_显, hb_win_句柄, (unsigned)画宽, (unsigned)画高,
                                     (unsigned)hb_x.XDefaultDepth(hb_win_显, hb_win_屏号));
    hb_win_已提交 = hb_x.XCreatePixmap(hb_win_显, hb_win_句柄, (unsigned)画宽, (unsigned)画高,
                                       (unsigned)hb_x.XDefaultDepth(hb_win_显, hb_win_屏号));
    if (hb_win_画布 == 0 || hb_win_已提交 == 0)
    {
        if (hb_win_画布 != 0) { hb_x.XFreePixmap(hb_win_显, hb_win_画布); hb_win_画布 = 0; }
        if (hb_win_已提交 != 0) { hb_x.XFreePixmap(hb_win_显, hb_win_已提交); hb_win_已提交 = 0; }
        hb_x.XFreeGC(hb_win_显, hb_win_gc);
        hb_win_gc = 0;
        hb_x.XDestroyWindow(hb_win_显, hb_win_句柄);
        hb_win_句柄 = 0;
        hb_x.XCloseDisplay(hb_win_显);
        hb_win_显 = NULL;
        return 0;
    }
    hb_x.XSetBackground(hb_win_显, hb_win_gc, hb_win_黑);
    hb_x.XSetGraphicsExposures(hb_win_显, hb_win_gc, 0);
    hb_x.XSetLineAttributes(hb_win_显, hb_win_gc, 1, 0 /* LineSolid */, 0 /* CapButt */, 0 /* JoinMiter */);

    /* ⚠ 这一句不能省:C 程序**不会**自己认 `LANG` / `LC_ALL`,不调 `setlocale` 就一直是 "C" locale,
       而 `XCreateFontSet` / `Xutf8DrawString` 都看当前 locale ⇒ 建出来的字体集是 C locale 那一套、
       没有 UTF-8 转换器,**非 ASCII 一个像素都不画、也不报错**(实测:同一张字体基名表,调了出中文、不调整行消失)。
       只动 `LC_CTYPE`(**别用 `LC_ALL`**):`LC_NUMERIC` 一动,`printf("%f")` 的小数点在某些 locale 下会变成逗号,
       那会把 HB 的浮点输出改掉。 */
    setlocale(LC_CTYPE, "");

    /* 字体集:等宽位图字体 + 中文那一族;拿不到就退回只出 ASCII 的 XDrawString(不算错) */
    hb_win_字体集 = hb_x.XCreateFontSet(hb_win_显, hb_win_字体名, &缺字, &缺字个数, NULL);
    if (缺字 != NULL && hb_x.XFreeStringList != NULL) hb_x.XFreeStringList(缺字);

    hb_win_宽 = 画宽;
    hb_win_高 = 画高;
    hb_win_画布色 = hb_win_黑;
    hb_win_清屏(0xFF000000u);
    hb_x.XFlush(hb_win_显);
    return 1;
}

static void hb_win_拆窗口(void)
{
    if (hb_win_画布 != 0 && hb_win_显 != NULL) { hb_x.XFreePixmap(hb_win_显, hb_win_画布); hb_win_画布 = 0; }
    if (hb_win_已提交 != 0 && hb_win_显 != NULL) { hb_x.XFreePixmap(hb_win_显, hb_win_已提交); hb_win_已提交 = 0; }
    if (hb_win_gc != 0 && hb_win_显 != NULL) { hb_x.XFreeGC(hb_win_显, hb_win_gc); hb_win_gc = 0; }
    if (hb_win_字体集 != NULL && hb_win_显 != NULL) { hb_x.XFreeFontSet(hb_win_显, hb_win_字体集); hb_win_字体集 = NULL; }
    if (hb_win_句柄 != 0 && hb_win_显 != NULL)
    {
        hb_x_window 旧 = hb_win_句柄;
        hb_win_句柄 = 0;                              /* 先摘记号(与 Win32 那一档同一个理由) */
        hb_x.XDestroyWindow(hb_win_显, 旧);
    }
    if (hb_win_显 != NULL) { hb_x.XCloseDisplay(hb_win_显); hb_win_显 = NULL; }
    hb_win_宽 = 0;
    hb_win_高 = 0;
}

#else
/* ══════════════ ③ 别的平台(含 macOS):如实不做 ══════════════
   窗口那一档在这些平台上**编得过、跑得起来**,只是开窗一律给 0 ⇒ 能力层给状态 25
   ("这个宿主没挂呈现口"那条老口径)。macOS 那一档登记在 `未做任务清单.md` §二十 R3-6-5 批 2。 */

static void hb_win_拆窗口(void) { }
static int hb_win_打开(const hb_u8 *标题, hb_u32 标题长, int 宽, int 高)
{
    (void)标题; (void)标题长; (void)宽; (void)高;
    return 0;
}
static void hb_win_清屏(hb_u32 颜色) { (void)颜色; }
static void hb_win_画矩形(int x, int y, int 宽, int 高, hb_u32 颜色) { (void)x; (void)y; (void)宽; (void)高; (void)颜色; }
static void hb_win_画线(int x1, int y1, int x2, int y2, hb_u32 颜色) { (void)x1; (void)y1; (void)x2; (void)y2; (void)颜色; }
static void hb_win_画文字(int x, int y, const hb_u8 *文本, hb_u32 长, hb_u32 颜色)
{
    (void)x; (void)y; (void)文本; (void)长; (void)颜色;
}
static void hb_win_刷新(int 命令数) { (void)命令数; }
static void hb_win_泵(void) { }
static void hb_win_睡(int 毫秒) { (void)毫秒; }
/* R4-12:这一档没有窗口层 ⇒ `贴位图` 空转(与"没开窗"同一效果;离屏那七条照旧全好 ——
   它们不碰窗口,只碰 `hb_bmp_` 那一张表) */
static void hb_win_贴位图(int x, int y, const hb_u32 *像素, int 宽, int 高)
{
    (void)x; (void)y; (void)像素; (void)宽; (void)高;
}

#endif /* 平台分支 */

static void hb_win_挂上(int 要窗口)
{
    if (hb_win_活 && !要窗口) hb_win_拆窗口();
    hb_win_活 = 要窗口 ? 1 : 0;
    if (!hb_win_活) hb_win_用户关过 = 0;
}

#endif /* HB_有窗口 */

/* ── 图形那一档的**装配入口**(两条 C 路的 `main` 共用这一句) ──────────────────
   口径(R3-6-5 批 2):`--hb-图形 <文件>` 挂无头呈现 dump;给 `--hb-窗口` 时换成真窗口
   (Windows / GDI,Linux / X11),没给 `--hb-图形` 就把文本 dump 丢进黑洞(不混进程序输出)。

   **为什么这一段必须在这儿、而不是抄进两个 `main`**:`hb_win_挂上`(还有真开窗那一族)只在
   `HB_有窗口` 为 1 时才有定义(缺省 0 = 老口径"没有窗口层"),转译产物生成的代码又是**每个程序
   现生成一份**的 —— 抄两遍就等于把那几句钉死在"窗口层一定编进来"上。收成这一个函数之后:
   没编窗口层时把 `要窗口` 压成 0(`hb_win_活` 恒 0,那一族谁都不碰),行为与老版一字不差。
   返回 0 = 挂好了;2 = 用法 / 环境不对(调用方打印用法并退出)。 */
#if HB_有图形

/* 先声明一句:装配这一步在它前面(`hb_gfx_挂上` 的实现在呈现层那一段之后)——
   少了这一句,C89/C99 会按"隐式声明返回 int"用,到真定义那儿就报 `C2371 重定义;不同的基类型`(实测踩到)。 */
static void hb_gfx_挂上(FILE *出, const char *脚本路径, int 要窗口);

static int hb_gfx_装配(const hb_开关 *开关)
{
    FILE *gfx = NULL;
    int 要窗口 = 开关->要窗口;
#if !HB_有窗口
    要窗口 = 0;                     /* 这一份没编窗口层:开窗那一档不认(与老版同形) */
#endif
    if (开关->图形文件 != NULL)
    {
        /* 走 `hb_宽开`:Windows 上那条 ANSI 路径认不了中文目录(实测:参数通了、文件开不出来) */
        int 是宽 = 0;
        gfx = hb_宽开(开关->图形文件, "wb", &是宽);
        if (gfx == NULL)
        {
            fprintf(stderr, "运行期错误: 打不开图形输出文件 %s\n", 开关->图形文件);
            fflush(stderr);
            return 2;
        }
    }
    /* 窗口那一档:没显式给 `--hb-图形` 就把文本 dump 丢进黑洞 —— 别混进"程序输出"那一股(R3-6-3 坑四) */
    else if (要窗口) gfx = hb_win_黑洞();
    hb_gfx_挂上(gfx, 开关->脚本文本, 要窗口);
    return 0;
}

#endif /* HB_有图形 */

/* 攒一条绘制命令(没开窗口 = 状态 22 —— 这条边界不能静默) */
static hb_u32 hb_gfx_攒一条(void)
{
    if (!hb_gfx_窗口开) return HB_ST_NOWIN;
    hb_gfx_攒着++;
    return HB_ST_OK;
}

/* `打开窗口`:呈现口收到整批时那一行(不是命令队列里的一条) */
static hb_u32 hb_gfx_打开窗口(hb_u32 参数区)
{
    hb_u32 标题址 = hb_host_rd(参数区);
    hb_u32 标题长 = hb_host_rd(参数区 + 4u);
    hb_u32 宽 = hb_host_rd(参数区 + 8u);
    hb_u32 高 = hb_host_rd(参数区 + 12u);
    FILE *f = hb_gfx_口();
    if (!hb_host_fits(标题址, (uint64_t)标题长 + 1u)) return HB_ST_ARG;
    if (hb_gfx_窗口开) return HB_ST_WINON;
#if HB_有窗口
#if HB_有窗口
    /* 真窗口这一档:开不出来就如实说"开不了窗口"(状态 25),别假装开了 */
    if (hb_win_活 && !hb_win_打开(hb_mem + 标题址, 标题长, (int)宽, (int)高)) return HB_ST_NOWINDOW;
#endif
#endif
    hb_gfx_攒着 = 0;                    /* 开窗是新的开始:上一轮没刷的命令不带过来 */
    hb_gfx_窗口开 = 1;
    hb_gfx_窗口宽 = (int)宽;
    hb_gfx_窗口高 = (int)高;
    hb_gfx_已呈现帧数 = 0;              /* 打开窗口 = 第 0 帧开始 */
    hb_gfx_脚本.投过关窗 = 0;
    fprintf(f, "打开窗口 标题=");
    hb_gfx_带引号(hb_mem + 标题址, 标题长);
    fprintf(f, " 宽=%d 高=%d\n", (int)宽, (int)高);
    hb_gfx_投放第几帧(0);
    return HB_ST_OK;
}

static hb_u32 hb_gfx_关闭窗口(void)
{
#if HB_有窗口
    /* 用户已经点叉把窗口关掉了:`关闭窗口` 照旧给成功(与桌面壳对已关窗口 `Close()` 是无操作同口径) */
    if (hb_win_活 && !hb_gfx_窗口开 && hb_win_用户关过)
    {
        hb_win_用户关过 = 0;
        hb_gfx_原样("关闭窗口\n");
        return HB_ST_OK;
    }
#endif
    if (!hb_gfx_窗口开) return HB_ST_NOWIN;
#if HB_有窗口
    if (hb_win_活) hb_win_拆窗口();
#endif
    hb_gfx_攒着 = 0;                    /* 没刷的命令随窗口一起去 */
    hb_gfx_窗口开 = 0;
    hb_gfx_窗口宽 = 0;
    hb_gfx_窗口高 = 0;
    hb_gfx_原样("关闭窗口\n");
    return HB_ST_OK;
}

/* `刷新` = 一帧的边界:先打这一帧攒下的命令,再打 `刷新 命令=N`,然后进下一帧 */
static hb_u32 hb_gfx_刷新(void)
{
    int 帧号;
    int 超了;
    FILE *f = hb_gfx_口();
    if (!hb_gfx_窗口开) return HB_ST_NOWIN;
    hb_gfx_已呈现帧数++;
    帧号 = hb_gfx_已呈现帧数;
    超了 = hb_gfx_有脚本 && 帧号 > hb_gfx_脚本.帧数 + 4;      /* 4 = `事件脚本C.帧数宽限` */
#if HB_有窗口
    if (hb_win_活) hb_win_刷新(hb_gfx_攒着);                  /* 真画出去(空帧不擦屏那一条在它里面) */
#endif
    fprintf(f, "刷新 命令=%d\n", hb_gfx_攒着);
    hb_gfx_攒着 = 0;
    if (!超了) hb_gfx_投放第几帧(帧号);
    else
    {
        hb_failf(hb_pc, "事件脚本说跑满 %d 帧,程序到第 %d 帧还在刷新 —— 多半是没理那个「关窗请求」(类型 1)。"
            "办法:主循环里 `如果 (取事件(类型, 键码, x, y)) { 如果 (类型 == 1) { 跳出; } }`,"
            "或者在 `帧数` 那一行写大一点", hb_gfx_脚本.帧数, 帧号);
    }
    return HB_ST_OK;
}

static hb_u32 hb_gfx_取事件(hb_u32 参数区)
{
#if HB_有窗口
    if (hb_win_活) hb_win_泵();          /* 真窗口:先把系统消息收进来(键盘 / 鼠标 / 关窗) */
#endif
    if (hb_gfx_队列头 >= hb_gfx_队列尾) return HB_ST_NOEVENT;
    hb_host_wr(参数区,       (hb_u32)hb_gfx_队列类型[hb_gfx_队列头]);
    hb_host_wr(参数区 + 4u,  (hb_u32)hb_gfx_队列键[hb_gfx_队列头]);
    hb_host_wr(参数区 + 8u,  (hb_u32)hb_gfx_队列x[hb_gfx_队列头]);
    hb_host_wr(参数区 + 12u, (hb_u32)hb_gfx_队列y[hb_gfx_队列头]);
    hb_gfx_队列头++;
    return HB_ST_OK;
}

/* 画文字 / 画矩形 / 画线 / 清屏:各打一行(顺序 = 下单顺序) */
static hb_u32 hb_gfx_画文字(hb_u32 参数区)
{
    hb_u32 文本址 = hb_host_rd(参数区 + 8u);
    hb_u32 文本长 = hb_host_rd(参数区 + 12u);
    hb_u32 状态;
    if (!hb_host_fits(文本址, (uint64_t)文本长 + 1u)) return HB_ST_ARG;
    状态 = hb_gfx_攒一条();
    if (状态 != HB_ST_OK) return 状态;
#if HB_有窗口
    if (hb_win_活)
        hb_win_画文字((int)hb_host_rd(参数区), (int)hb_host_rd(参数区 + 4u),
                      hb_mem + 文本址, 文本长, hb_host_rd(参数区 + 16u));
#endif
    fprintf(hb_gfx_口(), "画文字 x=%d y=%d 文本=", (int)hb_host_rd(参数区), (int)hb_host_rd(参数区 + 4u));
    hb_gfx_带引号(hb_mem + 文本址, 文本长);
    fprintf(hb_gfx_口(), " 颜色=0x%08X\n", (unsigned)hb_host_rd(参数区 + 16u));
    return HB_ST_OK;
}

static hb_u32 hb_gfx_画矩形(hb_u32 参数区)
{
    hb_u32 状态 = hb_gfx_攒一条();
    if (状态 != HB_ST_OK) return 状态;
#if HB_有窗口
    if (hb_win_活)
        hb_win_画矩形((int)hb_host_rd(参数区), (int)hb_host_rd(参数区 + 4u),
                      (int)hb_host_rd(参数区 + 8u), (int)hb_host_rd(参数区 + 12u),
                      hb_host_rd(参数区 + 16u));
#endif
    fprintf(hb_gfx_口(), "画矩形 x=%d y=%d 宽=%d 高=%d 颜色=0x%08X\n",
        (int)hb_host_rd(参数区), (int)hb_host_rd(参数区 + 4u),
        (int)hb_host_rd(参数区 + 8u), (int)hb_host_rd(参数区 + 12u),
        (unsigned)hb_host_rd(参数区 + 16u));
    return HB_ST_OK;
}

static hb_u32 hb_gfx_画线(hb_u32 参数区)
{
    hb_u32 状态 = hb_gfx_攒一条();
    if (状态 != HB_ST_OK) return 状态;
#if HB_有窗口
    if (hb_win_活)
        hb_win_画线((int)hb_host_rd(参数区), (int)hb_host_rd(参数区 + 4u),
                    (int)hb_host_rd(参数区 + 8u), (int)hb_host_rd(参数区 + 12u),
                    hb_host_rd(参数区 + 16u));
#endif
    fprintf(hb_gfx_口(), "画线 x1=%d y1=%d x2=%d y2=%d 颜色=0x%08X\n",
        (int)hb_host_rd(参数区), (int)hb_host_rd(参数区 + 4u),
        (int)hb_host_rd(参数区 + 8u), (int)hb_host_rd(参数区 + 12u),
        (unsigned)hb_host_rd(参数区 + 16u));
    return HB_ST_OK;
}

/* 清屏:颜色那一格 */
static hb_u32 hb_gfx_清屏(hb_u32 参数区)
{
    hb_u32 状态 = hb_gfx_攒一条();
    if (状态 != HB_ST_OK) return 状态;
#if HB_有窗口
    if (hb_win_活) hb_win_清屏(hb_host_rd(参数区));
#endif
    fprintf(hb_gfx_口(), "清屏 颜色=0x%08X\n", (unsigned)hb_host_rd(参数区));
    return HB_ST_OK;
}

/* ══════════════════ R4-12:位图那一族(70~77)的八个处理 ══════════════════
   逐条对着 `引擎\图形宿主C.cs` 那八个 `处理…` 写 —— 状态码、写回槽位置、越界行为
   必须**一字不差**,否则 C# 虚拟机与 C 解释器核的 `取图像素` / 返回值就对不上(对拍会红)。 */

/* **建位图**:`参数1` **出句柄的槽地址**(调用方那个整数变量在哪)、`参数2/3` 宽高。
   宽高不合法 / 内存要不到 ⇒ 状态 9,句柄槽写 **-1**。 */
static hb_u32 hb_gfx_建位图(hb_u32 参数区)
{
    hb_u32 句柄址 = hb_host_rd(参数区);
    hb_u32 号 = hb_bmp_建(hb_host_rd(参数区 + 4u), hb_host_rd(参数区 + 8u));
    if (号 == 0u)
    {
        if (hb_host_fits(句柄址, 4u)) hb_host_wr(句柄址, 0xFFFFFFFFu);
        return HB_ST_ARG;
    }
    if (!hb_host_fits(句柄址, 4u)) return HB_ST_ARG;   /* ⚠ 与 C# 同序:先按"建不出来"写 -1,再报槽不合法 */
    hb_host_wr(句柄址, 号);
    return HB_ST_OK;
}

/* **位图尺寸**:`参数1` 句柄、`参数2/3` 写回宽高;句柄不认给 22(两个写回槽不动) */
static hb_u32 hb_gfx_位图尺寸(hb_u32 参数区)
{
    hb_bmp_格 *格 = hb_bmp_找(hb_host_rd(参数区));
    hb_u32 宽址, 高址;
    if (格 == NULL) return HB_ST_BADHANDLE;
    宽址 = hb_host_rd(参数区 + 4u);
    高址 = hb_host_rd(参数区 + 8u);
    if (!hb_host_fits(宽址, 4u) || !hb_host_fits(高址, 4u)) return HB_ST_ARG;
    hb_host_wr(宽址, 格->宽);
    hb_host_wr(高址, 格->高);
    return HB_ST_OK;
}

/* **画点到图**:`参数1` 句柄、`参数2/3` x/y、`参数4` 颜色。越界不报错(什么都不做)。 */
static hb_u32 hb_gfx_画点到图(hb_u32 参数区)
{
    hb_bmp_格 *格 = hb_bmp_找(hb_host_rd(参数区));
    if (格 == NULL) return HB_ST_BADHANDLE;
    hb_bmp_画(格, (int)hb_host_rd(参数区 + 4u), (int)hb_host_rd(参数区 + 8u), hb_host_rd(参数区 + 12u));
    return HB_ST_OK;
}

/* **取图像素**:`参数1` 句柄、`参数2/3` x/y、`参数4`(写回)= 颜色。越界给 0。
   ⚠ 结果写在 **+12** 那一格(C# 那边也是 `写字(内存, 参数区 + 12, …)`)—— 位置差一格就对不上。 */
static hb_u32 hb_gfx_取图像素(hb_u32 参数区)
{
    hb_bmp_格 *格 = hb_bmp_找(hb_host_rd(参数区));
    if (格 == NULL) return HB_ST_BADHANDLE;
    hb_host_wr(参数区 + 12u, hb_bmp_取(格, (int)hb_host_rd(参数区 + 4u), (int)hb_host_rd(参数区 + 8u)));
    return HB_ST_OK;
}

/* **贴位图**:`参数1` 句柄、`参数2/3` 贴到窗口的哪个位置。
   它是位图那条路的**出口**,也是"帧数不再随画了多少东西涨"的那一条 ⇒ **要窗口**(进窗口命令队列)。
   ⚠ 与 C# 同序:**先查句柄**(不认给 22),再`攒一条`(没窗口给 22`没有窗口`)。 */
static hb_u32 hb_gfx_贴位图(hb_u32 参数区)
{
    hb_bmp_格 *格 = hb_bmp_找(hb_host_rd(参数区));
    hb_u32 状态;
    if (格 == NULL) return HB_ST_BADHANDLE;
    状态 = hb_gfx_攒一条();
    if (状态 != HB_ST_OK) return 状态;
#if HB_有窗口
    if (hb_win_活)
        hb_win_贴位图((int)hb_host_rd(参数区 + 4u), (int)hb_host_rd(参数区 + 8u),
                      格->像素, (int)格->宽, (int)格->高);
#endif
    /* 行格式 = `绘制命令C.一句话()` 里那一句:`贴位图 x=… y=… 宽=… 高=…`(**不倒像素**) */
    fprintf(hb_gfx_口(), "贴位图 x=%d y=%d 宽=%d 高=%d\n",
        (int)hb_host_rd(参数区 + 4u), (int)hb_host_rd(参数区 + 8u), (int)格->宽, (int)格->高);
    return HB_ST_OK;
}

/* **画线到图**:`参数1` 句柄、`参数2..5` 两端点、`参数6` 颜色(离屏、Bresenham)。 */
static hb_u32 hb_gfx_画线到图(hb_u32 参数区)
{
    hb_bmp_格 *格 = hb_bmp_找(hb_host_rd(参数区));
    if (格 == NULL) return HB_ST_BADHANDLE;
    hb_bmp_画线(格, (int)hb_host_rd(参数区 + 4u), (int)hb_host_rd(参数区 + 8u),
                (int)hb_host_rd(参数区 + 12u), (int)hb_host_rd(参数区 + 16u), hb_host_rd(参数区 + 20u));
    return HB_ST_OK;
}

/* **画矩形到位图**:`参数1` 句柄、`参数2/3` 左上角、`参数4/5` 宽高、`参数6` 颜色(实心、闭区间)。 */
static hb_u32 hb_gfx_画矩形到图(hb_u32 参数区)
{
    hb_bmp_格 *格 = hb_bmp_找(hb_host_rd(参数区));
    if (格 == NULL) return HB_ST_BADHANDLE;
    hb_bmp_画矩形(格, (int)hb_host_rd(参数区 + 4u), (int)hb_host_rd(参数区 + 8u),
                  (int)hb_host_rd(参数区 + 12u), (int)hb_host_rd(参数区 + 16u), hb_host_rd(参数区 + 20u));
    return HB_ST_OK;
}

/* 挂上无头呈现(`-` = 不挂,那 10 条按"这个宿主没挂这项能力"给状态 1;见文件头那段说明)。
   `要窗口`(R3-6-5)= 这一趟换成真窗口那个呈现层(Windows + `--hb-窗口` 才给 1)。 */
static void hb_gfx_挂上(FILE *出, const char *脚本路径, int 要窗口)
{
#if HB_有窗口
    hb_win_挂上(要窗口);
#else
    (void)要窗口;
#endif
    hb_gfx_出 = 出;
    hb_gfx_窗口开 = 0;
    hb_gfx_窗口宽 = 0;
    hb_gfx_窗口高 = 0;
    hb_gfx_攒着 = 0;
    hb_gfx_已呈现帧数 = 0;
    hb_gfx_队列头 = 0;
    hb_gfx_队列尾 = 0;
    hb_gfx_有脚本 = 0;
    hb_gfx_脚本.已读 = 0;
    hb_gfx_脚本.帧数 = 0;
    hb_gfx_脚本.投过关窗 = 0;
    if (脚本路径 != NULL && 脚本路径[0] != '\0') hb_gfx_读脚本(脚本路径);
}

static hb_u32 hb_gfx_调用(hb_u32 参数区, hb_u32 功能号)
{
    if (!hb_host_fits(参数区, hb_gfx_区大小(功能号))) return HB_ST_ARG;
    switch (功能号)
    {
        case HB_F_OPENWIN:  return hb_gfx_打开窗口(参数区);
        case HB_F_CLOSEWIN: return hb_gfx_关闭窗口();
        case HB_F_CLEAR:    return hb_gfx_清屏(参数区);
        case HB_F_DRAWTEXT: return hb_gfx_画文字(参数区);
        case HB_F_DRAWRECT: return hb_gfx_画矩形(参数区);
        case HB_F_DRAWLINE: return hb_gfx_画线(参数区);
        case HB_F_PRESENT:  return hb_gfx_刷新();
        case HB_F_POLLEVENT:return hb_gfx_取事件(参数区);
        case HB_F_SLEEP:
#if HB_有窗口
            if (hb_win_活) { hb_win_睡((int)hb_host_rd(参数区)); return HB_ST_OK; }
#endif
            return HB_ST_OK;          /* 无头不睡:自检要的是确定与快,不是真等 */
        case HB_F_TEXTWIDTH:return hb_host_取文字宽度(参数区);
        /* ---- R4-12:位图那一族(70~77)----
           ⚠ 70~74 / 76 / 77 **不需要窗口**(离屏),`贴位图`(75)要 —— 这条边界由各处理自己把,
             与 C# 那边"位图那一族走早退 switch、`贴位图` 留在下面"是同一件事的两种写法。 */
        case HB_F_MAKEBMP:  return hb_gfx_建位图(参数区);
        case HB_F_FREEBMP:  return hb_bmp_放(hb_host_rd(参数区)) ? HB_ST_OK : HB_ST_BADHANDLE;
        case HB_F_BMPSIZE:  return hb_gfx_位图尺寸(参数区);
        case HB_F_PUTPIX:   return hb_gfx_画点到图(参数区);
        case HB_F_GETPIX:   return hb_gfx_取图像素(参数区);
        case HB_F_BLITBMP:  return hb_gfx_贴位图(参数区);
        case HB_F_LINEBMP:  return hb_gfx_画线到图(参数区);
        case HB_F_RECTBMP:  return hb_gfx_画矩形到图(参数区);
        /* 号 78 `贴文字到位图`:**本侧如实"没这一手"** —— 字形光栅化要么借平台(Win32 / X11 两套都得另写),
           要么自带点阵字模(汉字那 3MB 塞不进 1MB)。给 HB_ST_NOCAP(与 C# 侧"没挂呈现口"同码),
           **不假装能画、也不动那张图一个格子**。判据要拉这一条时按"两腿不一致是**已知**的"记。 */
        case HB_F_TEXTBMP:  return HB_ST_NOCAP;
        default:            return HB_ST_ARG;
    }
}

#endif /* HB_有图形 */

/* 弹窗那一族(80~85)的分派:`hb_host_call` 在各功能号自己的长度检查**之前**调它 */
static hb_u32 hb_host_调用弹窗(hb_u32 参数区, hb_u32 功能号)
{
    switch (功能号)
    {
        case HB_F_READCLIP:  return hb_host_读剪贴板(参数区);
        case HB_F_WRITECLIP: return hb_host_写剪贴板(参数区);
        case HB_F_DIALOG:    return hb_host_提示框(参数区);
        case HB_F_PICKFILE:  return hb_host_选文件(参数区);
        case HB_F_SAVEFILE:  return hb_host_选文件(参数区);
        case HB_F_CLEARCLIP: return hb_host_清空剪贴板(参数区);
        default:             return HB_ST_ARG;
    }
}

/* 弹窗那一族(80~85)的分派:`hb_host_call` 在各功能号自己的长度检查**之前**调它
   (⚠ 它得先声明一句:定义在 `hb_host_call` **后面** —— 那几个处理函数要用到这一节之上的东西) */
static hb_u32 hb_host_调用弹窗(hb_u32 参数区, hb_u32 功能号);

static hb_u32 hb_host_call(hb_u32 参数区, hb_u32 功能号)
{
#if HB_有图形
    /* 图形那 10 条的参数区有的大于 16 字节(`打开窗口` 16 / `画文字` 20 / `取事件` 16):
       按各功能号要几个字节查,够不着就判"参数不合法"(与 `图形宿主C.参数区字节数` 同表,不越界读) */
    if (hb_gfx_是图形功能号(功能号))
        return hb_gfx_调用(参数区, 功能号);
#endif
    /* ⚠ 读模型那一族的请求块是 **56 字节**(14 格,见 `hb_host_读取模型`),
       它自己有一道 56 的检查;这里先按 16 拦一道的话,请求块靠近内存末尾时会被冤枉成
       "参数不合法"(改布局那次踩过:三处只改了两处)。 */
    if (功能号 == HB_F_READMODEL)
        return hb_host_读取模型(参数区);
    /* G4 批 23:弹窗那一族的请求块也不止 16 字节(读剪贴板 / 写剪贴板 / 提示框 28,选文件 / 另存文件 36)
       —— 与读模型同一条规矩:先按各自的表拦一道,别让靠近内存末尾的请求块被冤枉成"参数不合法"。 */
    if (功能号 >= HB_F_READCLIP && 功能号 <= HB_F_CLEARCLIP)
        return hb_host_调用弹窗(参数区, 功能号);
    if (!hb_host_fits(参数区, 16u)) return HB_ST_ARG;
    switch (功能号)
    {
        /* G3 域 5:异步那一族的请求块不止 16 字节(`发起异步` 40),先按它自己那张表拦一道 */
        case HB_F_STARTASYNC: return hb_host_发起异步(参数区);
        case HB_F_TASKREADY:  return hb_host_任务就绪(参数区);
        case HB_F_CANCELTASK: return hb_host_取消任务(参数区);
        case HB_F_READFILE:  return hb_host_读文件(参数区);
        case HB_F_WRITEFILE: return hb_host_写文件(参数区, 0);
        case HB_F_APPEND:    return hb_host_写文件(参数区, 1);
        case HB_F_MKDIR:     return hb_host_建目录(参数区);
        case HB_F_DELFILE:   return hb_host_删文件(参数区);
        case HB_F_LISTDIR:   return hb_host_列目录(参数区);
        case HB_F_GETENV:    return hb_host_环境变量(参数区);
        case HB_F_RMDIR:     return hb_host_删目录(参数区);
        case HB_F_MOVETO:    return hb_host_移动到(参数区);
        case HB_F_COPYTO:    return hb_host_复制到(参数区);
        case HB_F_READCHUNK: return hb_host_读文件块(参数区);
        case HB_F_READMODEL: return hb_host_读取模型(参数区);   /* R4-11:读 `.obj`(顶点 + 边) */
        case HB_F_OPENREAD:  return hb_host_打开流(参数区, 0);
        case HB_F_OPENWRITE: return hb_host_打开流(参数区, 1);
        case HB_F_READBLOCK: return hb_host_读一块(参数区);
        case HB_F_WRITEBLOCK:return hb_host_写一块(参数区);
        case HB_F_SEEK:      return hb_host_定位(参数区);
        case HB_F_CLOSEREAD: return hb_host_关流(hb_host_rd(参数区), 0) ? HB_ST_OK : HB_ST_BADHANDLE;
        case HB_F_CLOSEWRITE:return hb_host_关流(hb_host_rd(参数区), 1) ? HB_ST_OK : HB_ST_BADHANDLE;
        /* G3 域 6:运行时那四条(睡眠 / 冲刷 / 改名 / 临时目录)—— 四条到处都挂,不走"没挂能力"那一档 */
        case HB_F_SLEEPMS:   return hb_host_睡眠(参数区);
        case HB_F_FLUSH:     return hb_host_冲刷(参数区);
        case HB_F_RENAME:    return hb_host_改名(参数区);
        case HB_F_TEMPDIR:   return hb_host_临时目录(参数区);
        case HB_F_NOW:
            hb_host_wr(参数区, hb_host_now());
            return HB_ST_OK;
        case HB_F_NOWMS:                          /* G3 域 2:单调毫秒(界面 / 定时器的节拍) */
            hb_host_wr(参数区, hb_host_now_ms());
            return HB_ST_OK;
        case HB_F_EXITCODE:                       /* 没挂"运行进程"⇒ 与 C# 那一份同值 */
            hb_host_wr(参数区, 0xFFFFFFFFu);
            return HB_ST_OK;
        case HB_F_URLCODE:                        /* 没挂"取网址"⇒ 同上 */
            hb_host_wr(参数区, 0xFFFFFFFFu);
            return HB_ST_OK;
        case HB_F_RUNPROC:                        /* 要白名单:独立 exe 带不了(D6) */
            /* 与 `宿主能力C.处理运行进程` 同口径:**先**把"退出码 = 没有(0xFFFFFFFF)"写进写回槽(+32),
               再报"没挂能力" —— 少写这一笔,两边那 4 个字节就对不上(实测批 3 第一遍就是这么红的) */
            if (!hb_host_fits(参数区, 40u)) return HB_ST_ARG;
            hb_host_wr(参数区 + 32u, 0xFFFFFFFFu);
            return HB_ST_NOCAP;
        case HB_F_FETCHURL:                       /* 同上:HTTP 状态码的写回槽在 +28 */
            if (!hb_host_fits(参数区, 36u)) return HB_ST_ARG;
            hb_host_wr(参数区 + 28u, 0xFFFFFFFFu);
            return HB_ST_NOCAP;
        /* G3 域 3:网络(裸 socket)—— 同一条 D6 口径:独立 exe 带不了白名单 ⇒ 整族状态 1。
           写回槽**先写 -1 再返回**,与 C# 宿主"没给工程文件"那一档逐字节一致(那一边也是先写 -1)。 */
        case HB_F_CONNECT:                        /* 连接:句柄写回槽在 +12 */
            if (!hb_host_fits(参数区, 16u)) return HB_ST_ARG;
            hb_host_wr(参数区 + 12u, 0xFFFFFFFFu);
            return HB_ST_NOCAP;
        case HB_F_LISTEN:                         /* 监听:句柄写回槽在 +12(与连接同形) */
            if (!hb_host_fits(参数区, 16u)) return HB_ST_ARG;
            hb_host_wr(参数区 + 12u, 0xFFFFFFFFu);
            return HB_ST_NOCAP;
        case HB_F_ACCEPT:                         /* 接受:句柄写回槽在 +8 */
            if (!hb_host_fits(参数区, 12u)) return HB_ST_ARG;
            hb_host_wr(参数区 + 8u, 0xFFFFFFFFu);
            return HB_ST_NOCAP;
        case HB_F_SEND:                           /* 发送:字节数写回槽在 +12 */
            if (!hb_host_fits(参数区, 16u)) return HB_ST_ARG;
            hb_host_wr(参数区 + 12u, 0xFFFFFFFFu);
            return HB_ST_NOCAP;
        case HB_F_RECV:                           /* 接收:字节数写回槽在 +24(与 `读一块` 同形) */
            if (!hb_host_fits(参数区, 28u)) return HB_ST_ARG;
            hb_host_wr(参数区 + 24u, 0xFFFFFFFFu);
            return HB_ST_NOCAP;
        case HB_F_CLOSE:                          /* 关闭:没有写回槽 */
            if (!hb_host_fits(参数区, 8u)) return HB_ST_ARG;
            return HB_ST_NOCAP;
        case HB_F_RESOLVE:                        /* 主机解析:没有写回槽(返回值就是状态) */
            if (!hb_host_fits(参数区, 24u)) return HB_ST_ARG;
            return HB_ST_NOCAP;
        case HB_F_SENDBLOCK:                      /* 发送块:字节数写回槽在 +12(与 `发送` 同形) */
            if (!hb_host_fits(参数区, 16u)) return HB_ST_ARG;
            hb_host_wr(参数区 + 12u, 0xFFFFFFFFu);
            return HB_ST_NOCAP;
        case HB_F_RECVBLOCK:                      /* 接收块:字节数写回槽在 +20 */
            if (!hb_host_fits(参数区, 24u)) return HB_ST_ARG;
            hb_host_wr(参数区 + 20u, 0xFFFFFFFFu);
            return HB_ST_NOCAP;
        case HB_F_TEXTWIDTH:                      /* 纯计算,没窗口也给数(与 `图形宿主C` 同口径) */
#if HB_有图形
            return hb_gfx_调用(参数区, 功能号);
#else
            return hb_host_取文字宽度(参数区);
#endif
#if HB_有图形
        /* R3-6-3:图形能力 17~25 —— 本宿主是**无头**(命令逐行 dump / 事件按脚本投) */
        case HB_F_OPENWIN: case HB_F_CLOSEWIN: case HB_F_CLEAR:
        case HB_F_DRAWTEXT: case HB_F_DRAWRECT: case HB_F_DRAWLINE:
        case HB_F_PRESENT: case HB_F_POLLEVENT: case HB_F_SLEEP:
            return hb_gfx_调用(参数区, 功能号);
#else
        /* 图形能力 17~25:本宿主没有窗口 —— 与 C# 那边"没挂呈现口"一样给状态 1,一个字节都不写 */
        case HB_F_OPENWIN: case HB_F_CLOSEWIN: case HB_F_CLEAR:
        case HB_F_DRAWTEXT: case HB_F_DRAWRECT: case HB_F_DRAWLINE:
        case HB_F_PRESENT: case HB_F_POLLEVENT: case HB_F_SLEEP:
            return HB_ST_NOCAP;
#endif
        default:             return HB_ST_ARG;
    }
}

/* ---- 装载:复位全部状态、把机器码与初值搬进内存(与 `装载` 同一顺序) ----
   `img` 是"代码段之后那些非零字节"(零件一组:地址 / 值;实际几乎总是空 ——
   数据段整块是零初始化的,由 calloc 兜住),`init` 是顶层常量初值(三件一组:地址 / 宽度 / 值);
   `dump_path` 非空时停机 / 报错那一刻把整机状态写到那个路径(R3-6-4 批 2 的全量对拍用)。 */
static void hb_boot(const hb_u8 *code, hb_u32 code_len,
                    const hb_u32 *img, hb_u32 img_n,
                    const hb_u32 *init, hb_u32 init_n,
                    hb_u32 slot, const hb_err_msg *errs, hb_u32 errs_n,
                    const hb_span *lines, hb_u32 lines_n,
                    hb_u32 data_len,
                    hb_u32 mem_size,
                    const char *dump_path)
{
    hb_u32 i;
#ifdef _WIN32
    /* 终端上留文本模式(换行正确回车);重定向出去按字节原样 —— 对拍要的是后者 */
    if (!_isatty(_fileno(stdout))) _setmode(_fileno(stdout), _O_BINARY);
    if (!_isatty(_fileno(stdin))) _setmode(_fileno(stdin), _O_BINARY);
#endif
    if (mem_size < 4) mem_size = 4;
    /* G3 域 5:装载 = "一个程序从头开始" ⇒ 异步任务表也压回刚启动(与 C# 侧 `清句柄表` 同一条口径) */
    hb_host_清任务表();
#if HB_有图形
    /* R4-12:位图仓同理 —— 上一份程序留下的图不带进下一份(与 C# 侧
       `宿主能力C.清运行痕迹 → 位图仓C.清光()` 是同一件事的两头)。
       ⚠ 号表**不回 1**(旧号永不复用那条口径不因装载而破)。 */
    hb_bmp_清光();
#endif
    hb_mem_size = mem_size;
    hb_code_bytes = code_len;
    hb_data_bytes = data_len;
    hb_dump_slot = dump_path;
#ifndef HB_不报分派
    /* R3-6-2:解释器核也 `#include` 这一份,但它**没有分派表**这回事 ——
       它自己定义 `HB_不报分派` 把这一句关掉(不然它会跟着报一句"我走的是 switch(PC)",
       那是转译产物的口径,解释器照说不成立)。 */
    if (hb_dump_slot != NULL && hb_dump_slot[0] != '\0')
    {
        /* 这一次到底走哪条分派路,只有编出来的这个 exe 自己知道(标签表 / switch(PC) 是
           `#if` 决定的);要状态导出就是要对拍,顺手报一句给自检看。 */
        fprintf(stderr, "C后端分派: %s\n", HB_COMPUTED_GOTO ? "计算跳转(标签表)" : "switch(PC) 兜底");
        fflush(stderr);
    }
#endif
    hb_mem = (hb_u8 *)calloc(mem_size, 1);
    if (hb_mem == NULL)
    {
        fprintf(stderr, "运行期错误: 开不出 %u 字节的运行内存\n", mem_size);
        exit(1);
    }
    if (code_len > mem_size)
    {
        fprintf(stderr, "运行期错误: 机器码 %u 字节装不进 %u 字节的运行内存\n", code_len, mem_size);
        exit(1);
    }
    memcpy(hb_mem, code, code_len);
    if (img != NULL)
    {
        for (i = 0; i < img_n; i++)
        {
            hb_u32 addr = img[i * 2 + 0];
            if (addr < mem_size) hb_mem[addr] = (hb_u8)img[i * 2 + 1];
        }
    }
    if (init != NULL)
    {
        for (i = 0; i < init_n; i++)
        {
            hb_u32 addr = init[i * 3 + 0];
            hb_u32 width = init[i * 3 + 1];
            hb_u32 value = init[i * 3 + 2];
            hb_u32 k;
            if (width != 1 && width != 2 && width != 4) width = 4;
            if (addr + width > mem_size) continue;
            for (k = 0; k < width; k++) hb_mem[addr + k] = (hb_u8)(value >> (k * 8));
        }
    }
    hb_slot = slot;
    hb_errs = errs;
    hb_errs_n = errs_n;
    hb_lines = lines;
    hb_lines_n = lines_n;
    memset(hb_r, 0, sizeof hb_r);
    hb_pc = 0;
    hb_sp = mem_size;
    hb_psw = 0;
    hb_steps = 0;
    hb_at = 0;
    hb_trace_n = 0;
}
