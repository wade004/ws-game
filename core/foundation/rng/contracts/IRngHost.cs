using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.Rng
{
    /// <summary>
    /// 分流随机源契约（对应 03_运行时骨架.md 第 9 节 <c>RngHost</c> 签名）。
    /// 按 <paramref name="stream"/>（一个 <see cref="Id"/>）分流生成随机数以保证可复现：
    /// 不同用途（掉落、AI、技能……）各自持有独立流，互不干扰；<see cref="GetStreamState"/>/
    /// <see cref="SetStreamState"/> 供存档系统持久化与恢复某条流的内部状态。
    /// </summary>
    public interface IRngHost
    {
        /// <summary>取 <paramref name="stream"/> 流的下一个随机数，范围 [0,1)。</summary>
        double Next(Id stream);

        /// <summary>
        /// 取 <paramref name="stream"/> 流的下一个随机整数，闭区间 [<paramref name="min"/>,
        /// <paramref name="max"/>]。<paramref name="min"/> &gt; <paramref name="max"/> 时抛出
        /// <see cref="System.ArgumentException"/>。内部使用无偏方法（拒绝采样），不产生取模偏差。
        /// </summary>
        int NextInt(Id stream, int min, int max);

        /// <summary>取 <paramref name="stream"/> 流当前的内部状态快照，供存档系统持久化。</summary>
        RngStreamState GetStreamState(Id stream);

        /// <summary>把 <paramref name="stream"/> 流的内部状态恢复为 <paramref name="state"/>，供存档系统读档时调用。</summary>
        void SetStreamState(Id stream, RngStreamState state);

        /// <summary>
        /// 当前已存在（被访问/恢复过）的全部流，按 <see cref="Id"/> 的序数（ordinal）顺序排列，
        /// 供存档系统遍历写出全部 stream_states。
        /// </summary>
        IReadOnlyList<Id> Streams { get; }

        /// <summary>清空全部已存在的流并更换主种子；用于开新档或读档前重建。</summary>
        void Reset(ulong masterSeed);

        /// <summary>
        /// 当前主种子（P1-04 收口新增）：任何"录制/存档时刻尚未创建、之后才第一次被访问"的流，
        /// 其初始状态由本值经 <see cref="Reset"/> 构造时的 <c>SeedDerivation</c> 派生（见
        /// rng/README.md"懒创建与派生"一节）——存档/回放系统必须把它一并持久化/传递，否则这类
        /// "未来新流"在读档/重放后会派生自与录制/保存时不同的主种子，产生不同的随机序列
        /// （见 <see cref="RngStreamsPersistable"/>、<c>Core.Foundation.SaveSystem.ReplayPlayer</c>
        /// 判断记录）。
        /// </summary>
        ulong MasterSeed { get; }
    }
}
