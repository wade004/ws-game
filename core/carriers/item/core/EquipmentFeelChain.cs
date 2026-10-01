using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.SaveSystem;
using Core.Rules.Common;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 换装链的规则侧接线（手感设计/08 第 1 节）：装备变化事件 → 对账单位的主手/副手武器手感引用与武器族 →
    /// 变化则使手感解析器缓存失效并立即重算（版本号递增）→ 发布 <c>feel.weapon_changed</c>，姿势家族、反馈变体、界面
    /// 据此刷新（表现侧订阅方见 <c>Presentation.Render.EquipmentPoseBridge</c>）。
    /// <para>
    /// 判断记录（只在武器相关状态变化时失效）：换护甲、换饰品不影响手感解析（解析器只读两个武器引用），对账结果相同则
    /// 不失效、不发事件，避免无谓重算与表现抖动。对账比较的是 (主手引用, 副手引用, 主手武器族) 三元组。
    /// </para>
    /// <para>
    /// 判断记录（未跟踪单位的初值）：从未见过的单位视为"此前空手、无族"，因此首次穿上武器会发事件，首次只穿护甲不会。
    /// 单位在本链创建之前已有装备（读档或预置）时，宿主对该单位调用一次 <see cref="Sync"/>，或在构造时传
    /// <c>knownUnits</c> 让读档对账覆盖它们。
    /// </para>
    /// <para>
    /// 判断记录（读档冷路径与热路径等价）：<c>SaveSystem.Load</c> 在事件抑制作用域内重放装备，<c>item.equipped</c>/
    /// <c>item.unequipped</c> 因此到不了本类（同 <c>EquipmentWeaponStyleSource</c> 的 PRES-110-01 记录）；
    /// 本类额外订阅正常派发的 <c>save.loaded</c>：先整体使解析器缓存失效，再对账全部已跟踪单位与 <c>knownUnits</c>，
    /// 与热路径走同一个 <see cref="Sync"/>，因此读档前后武器不同会同样发出 <c>feel.weapon_changed</c>。
    /// </para>
    /// <para>
    /// 判断记录（事件经总线入队）：<c>feel.weapon_changed</c> 用 <see cref="IEventBus.Enqueue"/> 提交，与触发它的
    /// <c>item.equipped</c> 在同一次 <c>DispatchPending</c> 内派发完毕（总线保证订阅者新入队的事件在同一次派发里继续派发），
    /// 因此换装那一 tick 末尾，解析版本、武器族与姿势家族都已更新。
    /// </para>
    /// </summary>
    public sealed class EquipmentFeelChain : IDisposable
    {
        private readonly struct State : IEquatable<State>
        {
            public readonly string? Main;
            public readonly string? Offhand;
            public readonly string? Family;

            public State(string? main, string? offhand, string? family)
            {
                Main = main;
                Offhand = offhand;
                Family = family;
            }

            public bool Equals(State other) =>
                string.Equals(Main, other.Main, StringComparison.Ordinal)
                && string.Equals(Offhand, other.Offhand, StringComparison.Ordinal)
                && string.Equals(Family, other.Family, StringComparison.Ordinal);

            public override bool Equals(object? obj) => obj is State s && Equals(s);

            public override int GetHashCode() => (Main ?? string.Empty).GetHashCode() ^ (Offhand ?? string.Empty).GetHashCode();
        }

        private readonly IEventBus _bus;
        private readonly IFeelEquipmentProvider _equipment;
        private readonly FeelWeaponCatalog _catalog;
        private readonly IFeelResolver _resolver;
        private readonly Func<IEnumerable<Id>>? _knownUnits;
        private readonly Dictionary<Id, State> _states = new Dictionary<Id, State>();
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        public EquipmentFeelChain(
            IEventBus bus, IFeelEquipmentProvider equipment, FeelWeaponCatalog catalog, IFeelResolver resolver,
            Func<IEnumerable<Id>>? knownUnits = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _knownUnits = knownUnits;

            _subscriptions.Add(bus.Subscribe<ItemEquippedEvent>(CarriersEventKeys.ItemEquipped, e => Sync(e.UnitId)));
            _subscriptions.Add(bus.Subscribe<ItemUnequippedEvent>(CarriersEventKeys.ItemUnequipped, e => Sync(e.UnitId)));
            _subscriptions.Add(bus.Subscribe(SaveEventKeys.SaveLoaded, _ => OnSaveLoaded()));
        }

        /// <summary>已跟踪单位的当前武器族（未跟踪或空手为 null）。</summary>
        public string? GetFamily(Id unitId) => _states.TryGetValue(unitId, out var s) ? s.Family : null;

        /// <summary>
        /// 对账一个单位：读当前武器引用与武器族，与上次对账结果比较；有变化才使解析器缓存失效、重算并发布
        /// <c>feel.weapon_changed</c>。返回是否发生了变化。
        /// </summary>
        public bool Sync(Id unitId)
        {
            var main = _equipment.GetMainWeaponRef(unitId);
            var offhand = _equipment.GetOffhandWeaponRef(unitId);
            var family = main != null && _catalog.TryGet(main, out var info) && info.Family.Length > 0 ? info.Family : null;
            var current = new State(main, offhand, family);
            var previous = _states.TryGetValue(unitId, out var known) ? known : default;
            _states[unitId] = current;
            if (current.Equals(previous))
            {
                return false;
            }

            _resolver.Invalidate(unitId, "equipment_changed");
            var version = _resolver.GetVersion(unitId);
            _bus.Enqueue(new FeelWeaponChangedEvent(unitId, previous.Main, main, offhand, previous.Family, family, version));
            return true;
        }

        private void OnSaveLoaded()
        {
            _resolver.InvalidateAll("save_loaded");
            var units = new HashSet<Id>(_states.Keys);
            if (_knownUnits != null)
            {
                foreach (var id in _knownUnits())
                {
                    units.Add(id);
                }
            }

            var ordered = new List<Id>(units);
            ordered.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            foreach (var unit in ordered)
            {
                Sync(unit);
            }
        }

        public void Dispose()
        {
            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}
