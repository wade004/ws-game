using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// <see cref="ISkillBindingHost"/> 的默认实现（缺口 4）。按 <see cref="Id"/>（单位）→
    /// <c>Dictionary&lt;string, Id&gt;</c>（槽位 → 技能 id）双层字典维护绑定关系，纯内存态——落盘
    /// 经 <see cref="SkillBindingPersistable"/>（本文件下方，惯例同
    /// <c>Core.Carriers.Unit.UnitPersistable</c> 的静态工厂写法：只有"当前玩家单位"这一份绑定关系
    /// 参与存档，见 10_存档与持久化.md 第 2.2 节 <c>player.skill_bindings</c> 只在 <c>player</c> 段下，
    /// 不是"全部单位"的存档段）。
    /// </summary>
    public sealed class SkillBindingHost : ISkillBindingHost
    {
        private static readonly IReadOnlyDictionary<string, Id> EmptyBindings =
            new Dictionary<string, Id>(0, StringComparer.Ordinal);

        private readonly IEventBus _bus;
        private readonly KnownSkillQuery _knownSkillQuery;
        private readonly Dictionary<Id, Dictionary<string, Id>> _bindings = new Dictionary<Id, Dictionary<string, Id>>();

        public SkillBindingHost(IEventBus bus, KnownSkillQuery knownSkillQuery)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _knownSkillQuery = knownSkillQuery ?? throw new ArgumentNullException(nameof(knownSkillQuery));
        }

        public IReadOnlyDictionary<string, Id> GetBindings(Id unitId) =>
            _bindings.TryGetValue(unitId, out var slots) ? slots : EmptyBindings;

        public bool Bind(Id unitId, string slot, Id skillId)
        {
            if (string.IsNullOrEmpty(slot))
            {
                throw new ArgumentException("slot 不能为空", nameof(slot));
            }

            if (!_knownSkillQuery(unitId, skillId))
            {
                return false;
            }

            if (!_bindings.TryGetValue(unitId, out var slots))
            {
                slots = new Dictionary<string, Id>(StringComparer.Ordinal);
                _bindings[unitId] = slots;
            }

            slots[slot] = skillId;
            _bus.Enqueue(new UnitSkillBindingChangedEvent(unitId, slot, skillId));
            return true;
        }

        public bool Unbind(Id unitId, string slot)
        {
            if (string.IsNullOrEmpty(slot))
            {
                throw new ArgumentException("slot 不能为空", nameof(slot));
            }

            if (_bindings.TryGetValue(unitId, out var slots) && slots.Remove(slot))
            {
                _bus.Enqueue(new UnitSkillBindingChangedEvent(unitId, slot, null));
            }

            // 判断记录：该槽位本就未绑定时视为幂等成功——调用方（如"清空全部槽位"一类批量操作）
            // 不需要先查一遍 GetBindings 才能安全调用 Unbind；没有实际变化就不发事件。
            return true;
        }
    }
}
