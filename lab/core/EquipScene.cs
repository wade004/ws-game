using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Rules.Common;
using Core.Rules.Skill;
using Core.Sim;
using Presentation.Render;
using Presentation.VfxSfx.Core;
using Presentation.Ui;

namespace Lab
{
    /// <summary>换装场景里一次 <c>equip</c>/<c>unequip</c> 脚本事件执行后（该宿主固定步末尾）的运行期事实快照。</summary>
    public sealed class EquipStepRecord
    {
        public int Tick { get; set; }

        /// <summary><c>equip</c> 或 <c>unequip</c>。</summary>
        public string Op { get; set; } = string.Empty;

        /// <summary>穿上的物品模板 id，或卸下的槽位 id。</summary>
        public string Arg { get; set; } = string.Empty;

        public bool Ok { get; set; }

        /// <summary>物品占用的装备槽位 id（卸下时同 <see cref="Arg"/>）。</summary>
        public string Slot { get; set; } = string.Empty;

        /// <summary>该槽位是否武器槽（<c>item.slot_definition.is_weapon</c>）。</summary>
        public bool IsWeapon { get; set; }

        public string MainRef { get; set; } = string.Empty;

        public string OffhandRef { get; set; } = string.Empty;

        /// <summary>换装链对账出的武器族（空手为空串）。</summary>
        public string Family { get; set; } = string.Empty;

        /// <summary>相对上一次快照，手感解析版本号的增量。</summary>
        public int FeelVersionDelta { get; set; }

        /// <summary>主手武器手感引用相对上一次快照是否变了。</summary>
        public bool WeaponChanged { get; set; }

        /// <summary>本步内发出的 <c>feel.weapon_changed</c> 个数。</summary>
        public int WeaponChangedEvents { get; set; }

        public string ImpactClass { get; set; } = string.Empty;

        public int AttackerHitstopTicks { get; set; }

        public int TargetHitstopTicks { get; set; }

        public string SfxMaterial { get; set; } = string.Empty;

        /// <summary>按 <c>sfx.&lt;层&gt;.&lt;材质&gt;</c> 约定解出的 swing 层 sfx 行 id（缺行回落 generic，仍缺为空串）。</summary>
        public string SwingSfx { get; set; } = string.Empty;

        public string ImpactSfx { get; set; } = string.Empty;

        /// <summary>材质对应的层行缺失（回落到 generic）的层数。</summary>
        public int SfxFallbacks { get; set; }

        /// <summary>姿势选择器当前的武器族。</summary>
        public string PoseFamily { get; set; } = string.Empty;

        /// <summary>该武器族下 <c>idle</c> 解析出的姿势集键。</summary>
        public string IdleKey { get; set; } = string.Empty;

        public string AttackKey { get; set; } = string.Empty;

        /// <summary>武器表现档案引用（<c>display.weapon_style.*</c>，非武器为空）。</summary>
        public string WeaponStyle { get; set; } = string.Empty;

        /// <summary>该物品的图标 id（<c>display.map.icon_id</c>），缺失为空串。</summary>
        public string Icon { get; set; } = string.Empty;

        /// <summary>外观映射的模式/槽位/资源集（<c>sprite|slot|mesh</c> 风格的文本，无外观映射为空串）。</summary>
        public string Visual { get; set; } = string.Empty;

        /// <summary>界面装备面板视图模型里的槽位 → 模板（排序后拼接）。</summary>
        public string UiSlots { get; set; } = string.Empty;

        /// <summary>装备宿主里的槽位 → 模板（排序后拼接），与 <see cref="UiSlots"/> 应一致。</summary>
        public string HostSlots { get; set; } = string.Empty;
    }

    /// <summary>换装场景里玩家的一次普通攻击时间线动作（从 <c>action.started</c> 到 <c>action.finished</c>）。</summary>
    public sealed class EquipActionRecord
    {
        public int StartTick { get; set; }

        public string Skill { get; set; } = string.Empty;

        public string MainRef { get; set; } = string.Empty;

        public int DurationTicks { get; set; }

        /// <summary>相对开始 tick 的偏移；未到达为 -1。</summary>
        public int ActiveAt { get; set; } = -1;

        public int RecoveryAt { get; set; } = -1;

        public int FinishedAt { get; set; } = -1;

        public int ExpectedStartup { get; set; }

        public int ExpectedActive { get; set; }

        public int ExpectedRecovery { get; set; }

        /// <summary>武器 <c>timeline_reference</c> 与普攻技能时间线毫秒不一致的相位数（无参考数值为 0）。</summary>
        public int ReferenceMismatches { get; set; }
    }

    /// <summary>换装场景的运行期记录（只在脚本 meta 的 <c>scene</c> 为 <c>equip</c> 时存在）。</summary>
    public sealed class EquipRecording
    {
        public List<EquipStepRecord> Steps { get; } = new List<EquipStepRecord>();

        public List<EquipActionRecord> Actions { get; } = new List<EquipActionRecord>();

        public double StepSeconds { get; set; }
    }

    /// <summary>
    /// 换装场景装置（手感设计/06 第 3.6 节、08 第 1 节）：在无头世界上装出换装链的全部生产部件——手感解析器 + 装备提供者、
    /// 换装链、武器普攻映射（挂进动作时间线）、姿势选择器 + 换装姿势桥、外观/武器表现档案来源、装备面板视图模型——
    /// 并在每个脚本换装步骤之后采集运行期事实。
    /// <para>
    /// 判断记录（装置不装受击顿帧与命中解析）：换装场景度量的是"换装之后解析出的值与动作节奏"，不打靶子；顿帧只记录
    /// 解析出的 tick 值，不跑受击裁决（那是 <c>HitFeelAssembly</c> 与顿帧基线的事）。
    /// </para>
    /// <para>
    /// 判断记录（动作步长）：动作时间线用 <c>SkillOptions.ActionStepSeconds</c>（缺省 1/60）换算毫秒到 tick，而
    /// <c>GameplayAssembly</c> 目前不回填它（见 <c>core/rules/skill</c> README 判断记录），所以换装脚本的 tick 率必须是 60；
    /// 不一致时装置显式抛错，不静默按错的步长算。
    /// </para>
    /// </summary>
    internal sealed class EquipRig : IDisposable
    {
        private const double ActionStepSeconds = 1.0 / 60.0;

        private readonly HeadlessWorld _world;
        private readonly Id _player;
        private readonly EquipRecording _record;
        private readonly FeelSystem _feel;
        private readonly EquipmentFeelProvider _provider;
        private readonly EquipmentFeelChain _chain;
        private readonly WeaponActionBinding _binding;
        private readonly PoseSelector _pose;
        private readonly EquipmentPoseBridge _bridge;
        private readonly EquipmentVisualSource _visual;
        private readonly EquipmentWeaponStyleSource _weaponStyle;
        private readonly IDisplayInfoRegistry _displayInfo;
        private readonly InventoryViewModel _ui;
        private readonly HashSet<string> _poseKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<EquipStepRecord> _pending = new List<EquipStepRecord>();
        private readonly Dictionary<EquipStepRecord, Id> _pendingInstances = new Dictionary<EquipStepRecord, Id>();
        private EquipActionRecord? _action;
        private int _weaponChangedThisTick;
        private int _lastVersion;
        private string _lastMain = string.Empty;

        public IReadOnlyCollection<Id> LearnedSkills { get; }

        private EquipRig(HeadlessWorld world, ScriptMeta meta, double stepSeconds, EquipRecording record, IDisplayInfoRegistry displayInfo)
        {
            _world = world;
            _player = world.Player.EntityId;
            _record = record;
            _displayInfo = displayInfo;
            record.StepSeconds = stepSeconds;
            if (Math.Abs(stepSeconds - ActionStepSeconds) > 1e-9)
            {
                throw new LabFormatException(
                    $"换装场景脚本 {meta.ScriptId} 的 tickRate 必须是 60（动作时间线按 1/60 秒换算毫秒，当前 {meta.TickRate}）");
            }

            var registry = world.Registry;
            var equipment = world.Gameplay.Carriers.Equipment;
            _provider = new EquipmentFeelProvider(equipment, registry);
            var assembled = FeelAssembly.Assemble(registry, new FeelAssemblyOptions
            {
                StepSeconds = stepSeconds,
                CalibrationId = meta.FeelCalibrationId.Length > 0 ? meta.FeelCalibrationId : null,
                Providers = new FeelProviders { Equipment = _provider },
            });
            if (!assembled.IsAssembled)
            {
                throw new LabFormatException($"换装场景 {meta.ScriptId} 没有装出手感系统：{assembled.Reason}");
            }

            _feel = assembled.System!;
            var catalog = new FeelWeaponCatalog(registry);
            _chain = new EquipmentFeelChain(world.Bus, _provider, catalog, _feel.Resolver, () => new[] { _player });
            Id? unarmed = meta.UnarmedAttackSkill.Length > 0 ? new Id(meta.UnarmedAttackSkill) : (Id?)null;
            _binding = new WeaponActionBinding(_provider, catalog, unarmed);
            world.Gameplay.Carriers.Rules.Skill.AttachTimelineServices(new TimelineServices { Feel = _feel.Resolver, Binding = _binding });

            _pose = new PoseSelector();
            _bridge = new EquipmentPoseBridge(world.Bus, _pose);

            var visuals = new Dictionary<Id, EquipVisualDef>();
            foreach (var row in registry.GetAll("display.equip_visual"))
            {
                var def = EquipVisualDef.FromRecord(row);
                visuals[def.ItemId] = def;
            }

            _visual = new EquipmentVisualSource(world.Bus, visuals);
            _weaponStyle = new EquipmentWeaponStyleSource(world.Bus, MainHandTemplate, displayInfo);

            var skillBook = new SkillHostSkillBookQuery(world.Gameplay.Carriers.Rules.Skill);
            var providers = new IUiPathProvider[]
            {
                new PlayerPathProvider(
                    _player, world.Gameplay.Carriers.Rules.Stats, world.Gameplay.Carriers.Rules.Powers,
                    world.Gameplay.Carriers.Rules.Progression, world.Gameplay.Carriers.Inventory, equipment,
                    world.Gameplay.Quest, world.Gameplay.Economy, skillBook, world.Gameplay.Carriers.Rules.Skill.AuraQuery,
                    world.Gameplay.Carriers.Units, world.Gameplay.Carriers.Rules.AutoAttack, world.Gameplay.AreaTrigger, registry),
            };
            var slotIds = new List<Id>();
            foreach (var slot in registry.GetAll("item.slot_definition"))
            {
                if (slot.TryGetBool("is_equipment", out var isEquipment) && isEquipment)
                {
                    slotIds.Add(new Id(slot.Key));
                }
            }

            _ui = new InventoryViewModel(new UiDataSource(world.Bus, providers), slotIds);

            if (meta.PoseSet.Length > 0 && !string.Equals(meta.PoseSet, "none", StringComparison.Ordinal))
            {
                var set = registry.Get("display.anim_set", meta.PoseSet)
                    ?? throw new LabFormatException($"换装场景 {meta.ScriptId} 的 poseSet {meta.PoseSet} 在数据里不存在");
                if (set.TryGetObject("clips", out var clips))
                {
                    for (var i = 0; i < clips.Count; i++)
                    {
                        _poseKeys.Add(clips[i].Key);
                    }
                }
            }

            var learned = new List<Id>();
            foreach (var pair in meta.WeaponAttackSkills)
            {
                learned.Add(new Id(pair.Value));
            }

            if (unarmed.HasValue)
            {
                learned.Add(unarmed.Value);
            }

            foreach (var skill in learned)
            {
                world.Gameplay.Carriers.Rules.Skill.LearnSkill(_player, skill);
            }

            LearnedSkills = learned;
            world.Bus.Subscribe<FeelWeaponChangedEvent>(RulesEventKeys.FeelWeaponChanged, e =>
            {
                if (e.UnitId.Equals(_player))
                {
                    _weaponChangedThisTick++;
                }
            });
            _chain.Sync(_player);
            world.Bus.DispatchPending();
            _lastVersion = _feel.Resolver.GetVersion(_player);
            _lastMain = _provider.GetMainWeaponRef(_player) ?? string.Empty;
            _weaponChangedThisTick = 0;
        }

        public static EquipRig Create(
            HeadlessWorld world, ScriptMeta meta, double stepSeconds, LabRecording recording, IDisplayInfoRegistry displayInfo)
        {
            var record = new EquipRecording();
            recording.Equip = record;
            return new EquipRig(world, meta, stepSeconds, record, displayInfo);
        }

        private Id? MainHandTemplate(Id unit)
        {
            var identities = ((IEquipmentHost)_world.Gameplay.Carriers.Equipment).GetAllEquippedIdentities(unit);
            var weaponSlots = new List<Id>();
            foreach (var pair in identities)
            {
                var def = _world.Registry.Get("item.slot_definition", pair.Key);
                if (def != null && def.TryGetBool("is_weapon", out var isWeapon) && isWeapon)
                {
                    weaponSlots.Add(pair.Key);
                }
            }

            if (weaponSlots.Count == 0)
            {
                return null;
            }

            weaponSlots.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            return identities[weaponSlots[0]].TemplateId;
        }

        /// <summary>主手武器对应的普攻技能（挂在 <see cref="WeaponActionBinding"/> 上，数据契约字段 → 空手技能）。</summary>
        public bool TryResolveAttackSkill(out Id skillId) => _binding.TryResolveAttackSkill(_player, out skillId);

        /// <summary>穿上物品：背包加一件再穿到物品模板声明的槽位，登记一步待快照记录。</summary>
        public void Equip(int tick, string itemTemplateId)
        {
            var step = new EquipStepRecord { Tick = tick, Op = "equip", Arg = itemTemplateId };
            var template = _world.Registry.Get("item.template", itemTemplateId)
                ?? throw new LabFormatException($"换装脚本引用的物品 {itemTemplateId} 在数据里不存在");
            var slot = template.GetId("slot");
            step.Slot = slot.Value;
            var inventory = _world.Gameplay.Carriers.Inventory;
            if (!inventory.AddItem(_player, new Id(itemTemplateId), 1))
            {
                throw new LabFormatException($"物品 {itemTemplateId} 放不进背包");
            }

            Id? instance = null;
            foreach (var item in inventory.ListItems(_player))
            {
                if (string.Equals(item.TemplateId.Value, itemTemplateId, StringComparison.Ordinal))
                {
                    instance = item.InstanceId;
                }
            }

            if (!instance.HasValue)
            {
                throw new LabFormatException($"物品 {itemTemplateId} 加入背包后找不到实例");
            }

            var result = _world.Gameplay.Carriers.Equipment.Equip(_player, instance.Value, slot);
            step.Ok = result.Success;
            _pendingInstances[step] = instance.Value;
            _pending.Add(step);
        }

        public void Unequip(int tick, string slotId)
        {
            var step = new EquipStepRecord { Tick = tick, Op = "unequip", Arg = slotId, Slot = slotId };
            step.Ok = _world.Gameplay.Carriers.Equipment.Unequip(_player, new Id(slotId)) != null;
            _pending.Add(step);
        }

        /// <summary>该宿主固定步里被总线派发的一个事件（动作开始/相位/结束）。</summary>
        public void OnEvent(IEvent evt, int tick)
        {
            switch (evt)
            {
                case ActionStartedEvent started when started.ActorId.Equals(_player):
                    BeginActionRecord(started, tick);
                    break;
                case ActionPhaseChangedEvent phase when phase.ActorId.Equals(_player) && _action != null:
                    if (phase.Phase == ActionPhase.Active) _action.ActiveAt = tick - _action.StartTick;
                    else if (phase.Phase == ActionPhase.Recovery) _action.RecoveryAt = tick - _action.StartTick;
                    break;
                case ActionFinishedEvent finished when finished.ActorId.Equals(_player) && _action != null:
                    _action.FinishedAt = tick - _action.StartTick;
                    _action = null;
                    break;
            }
        }

        private void BeginActionRecord(ActionStartedEvent started, int tick)
        {
            var record = new EquipActionRecord
            {
                StartTick = tick,
                Skill = started.SkillId.Value,
                DurationTicks = started.DurationTicks,
                MainRef = _provider.GetMainWeaponRef(_player) ?? string.Empty,
            };

            var def = _world.Registry.Get("skill.def", started.SkillId);
            if (def != null && def.TryGetObject("timeline", out var timeline))
            {
                var judging = _feel.Resolver.Resolve(_player).Judging;
                double Ms(string name) => Number(timeline, name);
                int Ticks(double ms, string scaleField) =>
                    FeelCalibration.MillisecondsToTicks(ms * judging.GetNumber(scaleField), _record.StepSeconds);
                record.ExpectedStartup = Ticks(Ms("startup_ms"), FeelFieldNames.PhaseScaleStartup);
                record.ExpectedActive = Ticks(Ms("active_ms"), FeelFieldNames.PhaseScaleActive);
                record.ExpectedRecovery = Ticks(Ms("recovery_ms"), FeelFieldNames.PhaseScaleRecovery);

                var catalog = new FeelWeaponCatalog(_world.Registry);
                if (record.MainRef.Length > 0 && catalog.TryGet(record.MainRef, out var info) && info.TimelineReference != null)
                {
                    var reference = info.TimelineReference;
                    record.ReferenceMismatches =
                        (reference.StartupMs.HasValue && Math.Abs(reference.StartupMs.Value - Ms("startup_ms")) > 1e-9 ? 1 : 0)
                        + (reference.ActiveMs.HasValue && Math.Abs(reference.ActiveMs.Value - Ms("active_ms")) > 1e-9 ? 1 : 0)
                        + (reference.RecoveryMs.HasValue && Math.Abs(reference.RecoveryMs.Value - Ms("recovery_ms")) > 1e-9 ? 1 : 0);
                }
            }

            _record.Actions.Add(record);
            _action = record;
        }

        private static double Number(Core.Foundation.Common.Json.JsonObject obj, string name)
        {
            for (var i = 0; i < obj.Count; i++)
            {
                if (string.Equals(obj[i].Key, name, StringComparison.Ordinal) && obj[i].Value is Core.Foundation.Common.Json.JsonNumber n)
                {
                    return n.Value;
                }
            }

            return 0;
        }

        /// <summary>宿主固定步末尾（该步事件已派发完）：给本步登记的每个换装步骤采集运行期事实。</summary>
        public void EndTick()
        {
            foreach (var step in _pending)
            {
                Snapshot(step);
                _record.Steps.Add(step);
            }

            _pending.Clear();
            _pendingInstances.Clear();
            _weaponChangedThisTick = 0;
        }

        private void Snapshot(EquipStepRecord step)
        {
            var registry = _world.Registry;
            var slotDef = registry.Get("item.slot_definition", step.Slot);
            step.IsWeapon = slotDef != null && slotDef.TryGetBool("is_weapon", out var isWeapon) && isWeapon;
            step.MainRef = _provider.GetMainWeaponRef(_player) ?? string.Empty;
            step.OffhandRef = _provider.GetOffhandWeaponRef(_player) ?? string.Empty;
            step.Family = _chain.GetFamily(_player) ?? string.Empty;
            step.WeaponChanged = !string.Equals(step.MainRef, _lastMain, StringComparison.Ordinal);
            _lastMain = step.MainRef;
            step.WeaponChangedEvents = _weaponChangedThisTick;

            var resolver = _feel.Resolver;
            var version = resolver.GetVersion(_player);
            step.FeelVersionDelta = version - _lastVersion;
            _lastVersion = version;
            var feel = resolver.Resolve(_player);
            step.ImpactClass = feel.Judging.GetText(FeelFieldNames.ImpactClass);
            step.AttackerHitstopTicks = feel.Judging.GetTicks(FeelFieldNames.AttackerHitstopMs);
            step.TargetHitstopTicks = feel.Judging.GetTicks(FeelFieldNames.TargetHitstopMs);
            var material = feel.Presenting.GetText(FeelFieldNames.SfxMaterial);
            step.SfxMaterial = material.Length > 0 ? material : "generic";
            step.SwingSfx = ResolveSfx("swing", step.SfxMaterial, step);
            step.ImpactSfx = ResolveSfx("impact", step.SfxMaterial, step);

            var context = _pose.GetContext(_player);
            step.PoseFamily = context.Family ?? string.Empty;
            step.IdleKey = PoseKey("idle", context.Family);
            step.AttackKey = PoseKey("attack", context.Family);

            step.WeaponStyle = _weaponStyle.GetWeaponStyleRef(_player)?.Value ?? string.Empty;
            if (string.Equals(step.Op, "equip", StringComparison.Ordinal))
            {
                step.Icon = _displayInfo.Lookup(new Id(step.Arg))?.IconId ?? string.Empty;
                if (_pendingInstances.TryGetValue(step, out var instance)
                    && _visual.VisualByItemInstanceId.TryGetValue(instance, out var visual))
                {
                    step.Visual = (visual.Mode == EquipVisualMode.SlotMesh ? "slot_mesh" : "socket_attach") + "|"
                        + (visual.SlotId?.Value ?? visual.SocketId?.Value ?? string.Empty) + "|"
                        + (visual.MeshRef?.Value ?? visual.ModelRef?.Value ?? string.Empty);
                }
            }

            step.UiSlots = Describe(_ui.EquippedSlotIdentities);
            step.HostSlots = Describe(((IEquipmentHost)_world.Gameplay.Carriers.Equipment).GetAllEquippedIdentities(_player));
        }

        private string ResolveSfx(string layer, string material, EquipStepRecord step)
        {
            var id = $"sfx.{layer}.{material}";
            if (_world.Registry.Get("sfx.def", id) != null)
            {
                return id;
            }

            step.SfxFallbacks++;
            var generic = $"sfx.{layer}.generic";
            return _world.Registry.Get("sfx.def", generic) != null ? generic : string.Empty;
        }

        private string PoseKey(string state, string? family)
        {
            if (_poseKeys.Count == 0)
            {
                return string.Empty;
            }

            var resolution = Core.Foundation.DisplayInfo.PoseResolver.Resolve(
                new Core.Foundation.DisplayInfo.PoseRequest(state, family: family), key => _poseKeys.Contains(key));
            return resolution.Found ? resolution.TableKey : string.Empty;
        }

        private static string Describe(IReadOnlyDictionary<Id, EquippedItemIdentity> slots)
        {
            var parts = new List<string>();
            foreach (var pair in slots)
            {
                parts.Add(pair.Key.Value + "=" + pair.Value.TemplateId.Value);
            }

            parts.Sort(StringComparer.Ordinal);
            return string.Join(",", parts);
        }

        public void Dispose()
        {
            _ui.Dispose();
            _weaponStyle.Dispose();
            _visual.Dispose();
            _bridge.Dispose();
            _chain.Dispose();
        }
    }
}
