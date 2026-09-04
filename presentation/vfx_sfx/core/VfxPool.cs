using System;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// 按 <c>vfx.def.category</c> 分池的对象池（见 09_表现层.md 第 5.4 节"池的容量、回收策略
    /// （按 lifetime 超时回收/按 category 分池）由引擎适配层实现，接口对上层透明"——本模块把这
    /// 一职责落到表现层自己（<see cref="VfxPlayer"/> 内部持有一个 <see cref="VfxPool"/>），不下放
    /// 给 L-1，因为 L-1 的 <see cref="Core.Foundation.EngineAdapter.IRenderer2D"/> 只提供
    /// "创建/销毁一个粒子实例"这两个原子操作，没有池化语义）。
    /// <para>
    /// 判断记录（"超容量按 lifetime 最旧回收"的具体判定标准，09 原文未给出量化定义）：本类型把
    /// "最旧"解释为"剩余存活时间最短"（即最快会自然到期的实例），而不是"最早插入"——直觉是：
    /// 与其提前打断一个还有很长寿命的特效，不如提前打断一个反正很快也会被 <see cref="Update"/>
    /// 自然回收的特效，观感损失更小。未声明 <c>lifetime</c>（永不自然到期）的实例视为剩余时间
    /// 无穷大，因此只在全部实例都未声明 lifetime 时才按插入顺序（最早插入者）淘汰，作为确定性
    /// 兜底（避免"全部无穷大、挑不出一个"的未定义行为）。
    /// </para>
    /// </summary>
    internal sealed class VfxPool
    {
        private sealed class Entry
        {
            public ParticleHandle Handle;
            public double RemainingLifetime;
            public long InsertionSeq;
        }

        private readonly Dictionary<string, List<Entry>> _byCategory = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
        private readonly VfxOptions _options;
        private readonly Action<ParticleHandle> _stop;
        private long _seq;

        public VfxPool(VfxOptions options, Action<ParticleHandle> stop)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _stop = stop ?? throw new ArgumentNullException(nameof(stop));
        }

        /// <summary>登记一个刚创建的播放实例；超出该分类容量时先淘汰一个（见类型判断记录）再登记。</summary>
        public void Track(string category, ParticleHandle handle, double? lifetime)
        {
            var capacity = _options.PoolCapacityPerCategory.TryGetValue(category, out var cap)
                ? cap
                : _options.DefaultPoolCapacity;

            var list = GetOrCreateList(category);

            if (capacity > 0 && list.Count >= capacity)
            {
                var victimIndex = PickVictimIndex(list);
                var victim = list[victimIndex];
                list.RemoveAt(victimIndex);
                _stop(victim.Handle);
            }

            list.Add(new Entry
            {
                Handle = handle,
                RemainingLifetime = lifetime ?? double.PositiveInfinity,
                InsertionSeq = _seq++,
            });
        }

        /// <summary>调用方（<see cref="VfxPlayer.Stop"/>）主动停止某个句柄时，把它从池的跟踪记录里摘除，
        /// 避免 <see cref="Update"/> 之后又对一个已经外部停止的句柄重复调用 <c>StopParticle</c>。</summary>
        public void Untrack(ParticleHandle handle)
        {
            foreach (var list in _byCategory.Values)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].Handle.Equals(handle))
                    {
                        list.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        /// <summary>按 <paramref name="dt"/> 推进全部实例的剩余存活时间；到期的调用 <see cref="_stop"/> 回收。</summary>
        public void Update(double dt)
        {
            foreach (var list in _byCategory.Values)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var entry = list[i];
                    if (double.IsPositiveInfinity(entry.RemainingLifetime))
                    {
                        continue;
                    }

                    entry.RemainingLifetime -= dt;
                    if (entry.RemainingLifetime <= 0)
                    {
                        list.RemoveAt(i);
                        _stop(entry.Handle);
                    }
                }
            }
        }

        /// <summary>某个分类当前池内的实例数，供测试断言。</summary>
        public int CountInCategory(string category) =>
            _byCategory.TryGetValue(category, out var list) ? list.Count : 0;

        private List<Entry> GetOrCreateList(string category)
        {
            if (!_byCategory.TryGetValue(category, out var list))
            {
                list = new List<Entry>();
                _byCategory[category] = list;
            }
            return list;
        }

        private static int PickVictimIndex(List<Entry> list)
        {
            var victimIndex = 0;
            var best = list[0];
            for (var i = 1; i < list.Count; i++)
            {
                var candidate = list[i];
                if (candidate.RemainingLifetime < best.RemainingLifetime ||
                    (candidate.RemainingLifetime.Equals(best.RemainingLifetime) && candidate.InsertionSeq < best.InsertionSeq))
                {
                    best = candidate;
                    victimIndex = i;
                }
            }
            return victimIndex;
        }
    }
}
