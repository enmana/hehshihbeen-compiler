// ============================================================
// 位图仓C.cs —— R4-12:**离屏位图**那一族(功能号 70~79)的仓库与四个动作
//
//   为什么单开一个件:位图与"窗口 / 绘制命令"是**两回事** ——
//   它是**离屏**的(建一张图、往上画点、再问它的像素),**不需要呈现口**,也不入每帧那条命令表。
//   口径照流句柄 / 套接字句柄那一套:**句柄表 + 旧号当场作废**(用废号给状态码,不停机)。
//
//   像素格式与 `绘制命令C.颜色` 同一个:**0xAARRGGBB**(与 HB 源码里写的颜色字面量一致)。
//   内存用量:一张 480×520 的图 = 249,600 格 × 4 字节 ≈ 1 MB —— 与"运行内存默认 1MB"同量级,
//   所以**这张表是宿主的,不占 HB 的运行内存**(那正是把它放宿主侧的理由之一)。
//
//   ⚠ 这一批只做"建 / 释放 / 问尺寸 / 画点 / 读点"五条(70~74)。
//   施工图(`基础控件施工计划.md` §2)里 75~79 还有"画线到位图 / 画矩形到位图 / 贴位图(上图窗)"
//   那一档 —— **贴位图那条才是"一次提交一整块"的关键**,下一批做。
// ============================================================
using System;
using System.Collections.Generic;

namespace hehshihbeen_compiler
{
    /// <summary>一张离屏位图:宽 / 高 / 像素(0xAARRGGBB,行优先、紧排)</summary>
    public sealed class 位图C
    {
        public int 宽;
        public int 高;
        public uint[] 像素 = [];
        /// <summary> 取一个像素(越界给 0 —— 与"取图像素"那条能力的口径一致)</summary>
        public uint 取(int x, int y) =>
            x < 0 || y < 0 || x >= 宽 || y >= 高 ? 0u : 像素[y * 宽 + x];
        /// <summary> 画一个像素(越界**什么都不做** —— 让绘制代码不必自己判边界)</summary>
        public void 画(int x, int y, uint 颜色)
        {
            if (x < 0 || y < 0 || x >= 宽 || y >= 高) return;
            像素[y * 宽 + x] = 颜色;
        }

        // ================= R4-12:两条"批量"画法(线 / 矩形)=================
        //
        //   为什么要放宿主侧:它们是"位图那一族"里唯一**一条命令干很多事**的活
        //   (画一条 200 像素的线 = 一次调用,而不是 200 次),这正是位图这条路省往返的地方。
        //   算法用最朴素的 Bresenham(整数、无浮点、无除法)—— 那点精度对这个用途够了。

        /// <summary> 画一条线(Bresenham,整数)。两个端点**都画**(闭区间),越界的格子自动跳过。</summary>
        public void 画线(int x1, int y1, int x2, int y2, uint 颜色)
        {
            int 差x = Math.Abs(x2 - x1), 差y = Math.Abs(y2 - y1);
            int 步x = x1 < x2 ? 1 : -1, 步y = y1 < y2 ? 1 : -1;
            int 偏 = 差x - 差y;
            int x = x1, y = y1;
            while (true)
            {
                画(x, y, 颜色);
                if (x == x2 && y == y2) break;
                int 两倍 = 偏 * 2;
                if (两倍 > -差y) { 偏 -= 差y; x += 步x; }
                if (两倍 < 差x) { 偏 += 差x; y += 步y; }
            }
        }

        /// <summary> 画一个实心矩形(闭区间 [x, x+宽-1] × [y, y+高-1]);宽高 ≤ 0 什么都不做。</summary>
        public void 画矩形(int x, int y, int 矩形宽, int 矩形高, uint 颜色)
        {
            if (矩形宽 <= 0 || 矩形高 <= 0) return;
            int 末x = x + 矩形宽 - 1, 末y = y + 矩形高 - 1;
            if (末x < 0 || 末y < 0 || x >= 宽 || y >= 高) return;      // 整块在外面:一步都不用走
            int 起x = Math.Max(x, 0), 起y = Math.Max(y, 0);
            int 止x = Math.Min(末x, 宽 - 1), 止y = Math.Min(末y, 高 - 1);
            for (int 行 = 起y; 行 <= 止y; 行++)
            {
                int 基 = 行 * 宽;
                for (int 列 = 起x; 列 <= 止x; 列++) 像素[基 + 列] = 颜色;
            }
        }
    }

    /// <summary>
    /// 位图仓库(进程级静态;与 `宿主能力C` 那些静态表同一个格局)。
    /// 句柄从 1 起、只增不减 —— **旧号永不复用**:释放之后再拿那个号来用一律"句柄不认",
    /// 这样"用完没释放"与"释放了还在用"这两种错都能当场暴露,而不会悄悄画到别人的图上。
    ///
    /// ⚠ **"从 1 起"与"只增不减"管的是同一个程序的这一次运行**:一个程序跑完 `清光()` 连号一起
    /// 压回 1(见那个方法)—— 原生那条腿**每个程序都是新进程**,句柄绝对值两边必须一样。
    /// </summary>
    public static class 位图仓C
    {
        private static readonly Dictionary<int, 位图C> _表 = [];
        private static readonly Lock _锁 = new();
        private static int _下一个号 = 1;

        /// <summary> 建一张(宽高非法给 null)。**上限**:宽高各 ≤ 8192、总格数 ≤ 1600 万(约 64MB),
        /// 免得一句 `建位图(100000, 100000)` 就把进程吃掉。句柄由 `建并取号` 那条路给。</summary>
        public static 位图C? 建(int 宽, int 高)
        {
            if (宽 <= 0 || 高 <= 0 || 宽 > 8192 || 高 > 8192) return null;
            if ((long)宽 * 高 > 16_000_000L) return null;
            return new 位图C { 宽 = 宽, 高 = 高, 像素 = new uint[(long)宽 * 高] };
        }

        /// <summary> 建一张并**先定号再入表**(号是入表那一刻定的 —— 不能"入表之后再回头读数",
        /// 那会拿到别的线程刚建的那张的号)。宽高非法给 -1。</summary>
        public static int 建并取号(int 宽, int 高, out 位图C? 图)
        {
            图 = 建(宽, 高);
            if (图 == null) return -1;
            lock (_锁)
            {
                int 号 = _下一个号++;
                _表[号] = 图;
                return 号;
            }
        }

        public static 位图C? 取(int 号)
        {
            lock (_锁) return _表.TryGetValue(号, out var 图) ? 图 : null;
        }

        /// <summary> 释放:还给宿主。**没这个号 / 已经释放过**都给 false(调用方据此报"句柄不认")</summary>
        public static bool 释放(int 号)
        {
            lock (_锁) return _表.Remove(号);
        }

        /// <summary> 现在有几张(自检用;也用于"跑完一个程序清干净"那道护栏)</summary>
        public static int 张数 { get { lock (_锁) return _表.Count; } }

        /// <summary> 全清(宿主跑完一个程序调一次,免得上一份的图漏到下一份)。
        /// ⚠ **号也压回 1**:`清光` 就是"一个程序从头开始"那一刀(与 `C后端\运行时.c` 的 `hb_boot` 同档)。
        /// 不回号的话,同一个进程里连跑两个程序,后一个拿到的句柄就与**原生那条腿**(每个程序一个新进程,
        /// `hb_bmp_下一号` 从 1 起)错开 —— 句柄会落进 HB 的运行内存(`整数 图 = 位图.建(…)`),
        /// 全量对拍连内存一起逐字节比,当场红。</summary>
        public static void 清光()
        {
            lock (_锁)
            {
                _表.Clear();
                _下一个号 = 1;      // "旧号永不复用"管的是**一个程序之内**;跨程序 = 新宿主、号从头起
            }
        }
    }
}
