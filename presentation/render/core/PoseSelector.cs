using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Feel;

namespace Presentation.Render
{
    /// <summary>
    /// 按实体维护姿势解析的呈现侧上下文（步态、武器族、变体；手感设计/04 第 2 节），是 <see cref="IPoseContextSource"/>
    /// 的框架实现。调用方每帧（或每个速度采样）喂速度，武器/变体变化时设对应维度；上下文变化时触发
    /// <see cref="ContextChanged"/>，订阅方重新解析运动态姿势。
    /// <para>
    /// 铁律遵守：纯呈现——只读速度与呈现型手感视图（<see cref="PresentingFeelView"/>），不回写任何判定状态；
    /// 不订阅事件、不持有逻辑层写入能力。速度来源由调用方决定（视图位移差分，或运动状态的只读速度查询）。
    /// </para>
    /// <para>
    /// 判断记录：阈值随手感视图的版本号刷新（手感重算后版本号递增，下一次 <see cref="Observe"/> 读到新阈值）；
    /// 读不到视图（实体没有手感数据）时用 <see cref="GaitThresholds.Default"/>。首次观测某实体用
    /// <see cref="GaitDeriver.Seed"/> 无滞回定档（没有历史，不应带"上一档"偏置），之后才带滞回。
    /// </para>
    /// </summary>
    public sealed class PoseSelector : IPoseContextSource
    {
        private sealed class Entry
        {
            public readonly GaitDeriver Gait = new GaitDeriver();
            public bool Seeded;
            public int FeelVersion = -1;
            public string? Family;
            public string? Variant;
            public PoseContext Published;
        }

        private readonly Dictionary<Id, Entry> _entries = new Dictionary<Id, Entry>();

        public event Action<Id>? ContextChanged;

        public PoseContext GetContext(Id entityId) =>
            _entries.TryGetValue(entityId, out var e) ? e.Published : PoseContext.Empty;

        /// <summary>
        /// 观测一次速度：<paramref name="speedRatio"/> = <c>|velocity| / 基础移速</c>（基础移速取该实体自己的移速属性，不是标定值）。
        /// <paramref name="feel"/> 可空，用于读步态阈值。上下文因此变化时触发 <see cref="ContextChanged"/>。
        /// </summary>
        public void Observe(Id entityId, double speedRatio, PresentingFeelView? feel = null)
        {
            var e = GetOrAdd(entityId);

            var version = feel?.Version ?? -1;
            if (version != e.FeelVersion)
            {
                e.FeelVersion = version;
                e.Gait.SetThresholds(GaitThresholds.FromView(feel));
            }

            if (!e.Seeded)
            {
                e.Gait.Seed(speedRatio);
                e.Seeded = true;
            }
            else
            {
                e.Gait.Update(speedRatio);
            }

            Publish(entityId, e);
        }

        /// <summary>设置武器族（换主手武器后；null 清除）。</summary>
        public void SetFamily(Id entityId, string? family)
        {
            var e = GetOrAdd(entityId);
            e.Family = string.IsNullOrEmpty(family) ? null : family;
            Publish(entityId, e);
        }

        /// <summary>设置游戏层变体（null 清除）。</summary>
        public void SetVariant(Id entityId, string? variant)
        {
            var e = GetOrAdd(entityId);
            e.Variant = string.IsNullOrEmpty(variant) ? null : variant;
            Publish(entityId, e);
        }

        /// <summary>实体销毁/重生时清理其记账。</summary>
        public void Forget(Id entityId) => _entries.Remove(entityId);

        private Entry GetOrAdd(Id entityId)
        {
            if (!_entries.TryGetValue(entityId, out var e))
            {
                e = new Entry();
                _entries[entityId] = e;
            }
            return e;
        }

        private void Publish(Id entityId, Entry e)
        {
            var now = new PoseContext(e.Gait.Current, e.Family, e.Variant);
            if (now.Equals(e.Published))
            {
                return;
            }
            e.Published = now;
            ContextChanged?.Invoke(entityId);
        }
    }
}
