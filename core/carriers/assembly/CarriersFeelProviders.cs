using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Carriers.Assembly
{
    // 手感解析器读取实体只读状态的生产提供者（手感落地 S10）。解析器在 L0 不依赖载体层，这些实现读单位模板、装备、光环与动作状态，
    // 由 CarriersFeelAssembly 在装配根注入。全部只读、无副作用；数据缺失一律返回"无"，不抛异常（手感引用本来就是可选能力）。

    /// <summary>
    /// 体型/角色引用（第 2、5 层）：生物单位读单位模板（<c>creature.template</c>）、玩家单位读职业行（<c>arch.class</c>）的
    /// <c>feel_archetype_ref</c>/<c>feel_ref</c>。
    /// <para>
    /// 判断记录（玩家入口，ADR-0146）：玩家是 <see cref="PlayerUnit"/>，没有单位模板，职业才是它的"模板"，所以玩家按
    /// <see cref="PlayerUnit.ArchetypeId"/> 对应的 <c>arch.class</c> 行取两个引用——两个字段与 <c>creature.template</c> 同名同义，
    /// 解析路径（第 2 层体型原型、第 5 层角色）完全一致；职业行没有声明、或不在注册表时返回无，与此前玩家恒无体型/角色引用逐位相同。
    /// 其它实体类型（物件、投射物、召唤物）仍没有这两层。
    /// </para>
    /// </summary>
    public sealed class CreatureTemplateFeelBodyProvider : IFeelBodyProvider
    {
        /// <summary>职业行（<c>arch.class</c>）表名。</summary>
        public const string ClassTable = "arch.class";

        private readonly IWorldSim _world;
        private readonly IDataRegistryView _registry;

        public CreatureTemplateFeelBodyProvider(IWorldSim world, IDataRegistryView registry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public string? GetArchetypeRef(Id unitId) => ReadRef(unitId, "feel_archetype_ref");

        public string? GetCharacterRef(Id unitId) => ReadRef(unitId, "feel_ref");

        /// <summary>注册表里是否有任何职业行声明了手感引用（用于读档后失效判断：没有声明就不必为玩家重算）。</summary>
        public bool AnyClassDeclaresFeel()
        {
            if (!_registry.TryGetAll(ClassTable, out var rows)) return false;
            for (var i = 0; i < rows.Count; i++)
            {
                var record = rows[i];
                if ((record.TryGetString("feel_archetype_ref", out var a) && a.Length > 0)
                    || (record.TryGetString("feel_ref", out var c) && c.Length > 0))
                {
                    return true;
                }
            }

            return false;
        }

        private string? ReadRef(Id unitId, string field)
        {
            var entity = _world.GetEntity(unitId);
            if (entity == null) return null;

            string table;
            Id row;
            if (entity is PlayerUnit player)
            {
                if (string.IsNullOrEmpty(player.ArchetypeId.Value)) return null;
                table = ClassTable;
                row = player.ArchetypeId;
            }
            else
            {
                var template = entity.TemplateId;
                if (!template.HasValue) return null;
                table = "creature.template";
                row = template.Value;
            }

            var record = _registry.Get(table, row);
            return record != null && record.TryGetString(field, out var value) && value.Length > 0 ? value : null;
        }
    }

    /// <summary>实体标签：读单位当前持有的标签（第 3 层），原样转字符串（形如 <c>gender:female</c>）。</summary>
    public sealed class UnitTagFeelProvider : IFeelTagProvider
    {
        private readonly WorldUnitAccess _units;

        public UnitTagFeelProvider(WorldUnitAccess units)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
        }

        public IReadOnlyList<string> GetTags(Id unitId)
        {
            if (!_units.Exists(unitId)) return Array.Empty<string>();
            var tags = _units.GetTags(unitId);
            if (tags.Count == 0) return Array.Empty<string>();
            var result = new string[tags.Count];
            for (var i = 0; i < result.Length; i++) result[i] = tags[i].Value;
            return result;
        }
    }

    /// <summary>
    /// 装备：读主手与副手武器模板（<c>item.template</c>）的 <c>feel_weapon_ref</c>（第 4 层）。
    /// <para>
    /// 判断记录（主手/副手怎么定）：数据里没有"主手/副手"这个概念，只有 <c>item.slot_definition.is_weapon</c>。缺省取武器槽按槽位 id 序数排序
    /// （与 <c>EquipmentHost</c> 找"第一个武器槽"的规则一致）的第 1 个为主手、第 2 个为副手；游戏的槽位命名不符合这条约定时，用
    /// <see cref="CarriersFeelOptions.MainHandSlot"/>/<see cref="CarriersFeelOptions.OffhandSlot"/> 显式指定。
    /// </para>
    /// </summary>
    public sealed class EquippedWeaponFeelProvider : IFeelEquipmentProvider
    {
        private readonly EquipmentHost _equipment;
        private readonly IDataRegistryView _registry;
        private readonly Id? _mainSlot;
        private readonly Id? _offhandSlot;

        public EquippedWeaponFeelProvider(EquipmentHost equipment, IDataRegistryView registry, Id? mainSlot = null, Id? offhandSlot = null)
        {
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _mainSlot = mainSlot;
            _offhandSlot = offhandSlot;
        }

        public string? GetMainWeaponRef(Id unitId) => WeaponRef(unitId, _mainSlot, 0);

        public string? GetOffhandWeaponRef(Id unitId) => WeaponRef(unitId, _offhandSlot, 1);

        private string? WeaponRef(Id unitId, Id? explicitSlot, int ordinal)
        {
            var equipped = _equipment.GetAllEquippedInstances(unitId);
            if (equipped.Count == 0) return null;

            ItemInstance instance;
            if (explicitSlot.HasValue)
            {
                if (!equipped.TryGetValue(explicitSlot.Value, out instance)) return null;
            }
            else
            {
                var weaponSlots = new List<Id>();
                foreach (var slot in equipped.Keys)
                {
                    var def = _registry.Get("item.slot_definition", slot);
                    if (def != null && def.TryGetBool("is_weapon", out var isWeapon) && isWeapon) weaponSlots.Add(slot);
                }

                if (weaponSlots.Count <= ordinal) return null;
                weaponSlots.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
                instance = equipped[weaponSlots[ordinal]];
            }

            var template = _registry.Get("item.template", instance.TemplateId);
            return template != null && template.TryGetString("feel_weapon_ref", out var value) && value.Length > 0 ? value : null;
        }
    }

    /// <summary>当前动作：时间线动作进行中即处于动作，动作层手感引用取该技能 <c>timeline.feel_ref</c>（第 6 层）。</summary>
    public sealed class ActionStateFeelProvider : IFeelActionProvider
    {
        private readonly IActionStateQuery _actions;
        private readonly SkillHost _skill;

        public ActionStateFeelProvider(SkillHost skill)
        {
            _skill = skill ?? throw new ArgumentNullException(nameof(skill));
            _actions = skill.ActionStateQuery;
        }

        public FeelActionState GetActionState(Id unitId)
        {
            var state = _actions.Current(unitId);
            return state.HasValue ? new FeelActionState(true, _skill.GetTimelineFeelRef(state.Value.SkillId)) : FeelActionState.Idle;
        }
    }

    /// <summary>
    /// 临时状态：读单位身上光环定义（<c>skill.aura_def</c>）的 <c>feel_modifiers</c>（第 7 层）。
    /// <para>
    /// 判断记录（条目键与层数，M2-B）：条目键取光环实例 id（<see cref="AuraSnapshot.InstanceId"/>，手感设计/05 第 6 节），每个实例一条；
    /// 层数取 <see cref="AuraSnapshot.Stacks"/> 带进 <see cref="FeelTemporaryEntry.Stacks"/>，解析器对数值 <c>multiply</c> 连乘、<c>add</c> 累加（<c>set</c> 幂等）。
    /// 查询实现不暴露实例 id 时（旧实现、测试假实现）回落到光环定义 id 为键、同一定义只算一条。层数变化经 <c>aura.stack_changed</c> 失效重算。
    /// </para>
    /// </summary>
    public sealed class AuraFeelTemporaryProvider : IFeelTemporaryProvider
    {
        private readonly IAuraQuery _auras;
        private readonly IDataRegistryView _registry;
        private readonly FeelFieldSet _fields;

        public AuraFeelTemporaryProvider(IAuraQuery auras, IDataRegistryView registry, FeelFieldSet fields)
        {
            _auras = auras ?? throw new ArgumentNullException(nameof(auras));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public IReadOnlyList<FeelTemporaryEntry> GetEntries(Id unitId)
        {
            var snapshots = _auras.GetActiveAuraSnapshots(unitId);
            if (snapshots.Count == 0) return Array.Empty<FeelTemporaryEntry>();

            List<FeelTemporaryEntry>? entries = null;
            var seen = new HashSet<Id>();
            for (var i = 0; i < snapshots.Count; i++)
            {
                var defId = snapshots[i].AuraDefId;
                // 条目键 = 光环实例 id（手感设计/05 第 6 节）；查询实现不暴露实例 id 时回落到定义 id（同一定义只算一条，层数仍按快照层数）。
                var key = snapshots[i].InstanceId ?? defId;
                if (!seen.Add(key)) continue;
                var record = _registry.Get("skill.aura_def", defId);
                if (record == null) continue;
                var writes = FeelWriteParser.ParseWrites(record.Raw, "feel_modifiers", _fields);
                if (writes.Count == 0) continue;
                (entries ??= new List<FeelTemporaryEntry>()).Add(new FeelTemporaryEntry(key.Value, writes, snapshots[i].Stacks));
            }

            return entries ?? (IReadOnlyList<FeelTemporaryEntry>)Array.Empty<FeelTemporaryEntry>();
        }
    }
}
