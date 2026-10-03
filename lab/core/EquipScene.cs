using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Carriers.Assembly;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
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
    /// 换装场景装置（手感设计/06 第 3.6 节、08 第 1 节）：手感解析器、换装链（<c>EquipmentFeelChain</c>）、武器优先的普攻映射
    /// （<c>WeaponPreferredActionBinding</c>）、挂进动作时间线的协作者全部取自<b>生产装配</b>（宿主以
    /// <c>HeadlessWorldOptions.FeelOptions</c> 开启，装置只读 <c>world.Gameplay.Feel</c>，不再自装一套）；
    /// 装置自己只装生产装配里属于表现层的几件（姿势选择器 + 换装姿势桥，与 <c>PresentationAssembly</c> 同一组构造调用；
    /// 外观/武器表现档案来源、装备面板视图模型），并在每个脚本换装步骤之后采集运行期事实。
    /// <para>
    /// 判断记录（装置不装受击顿帧与命中解析的结论）：换装场景度量的是"换装之后解析出的值与动作节奏"，不打靶子；顿帧只记录
    /// 解析出的 tick 值，不跑受击裁决（那是 <c>HitFeelAssembly</c> 与顿帧基线的事）。生产装配在这个世界里同样装了受击裁决，
    /// 只是脚本里没有命中事件，它不产生行为。
    /// </para>
    /// <para>
    /// 判断记录（不实例化 <c>PresentationAssembly</c>）：整份表现装配还会装镜头、反馈绑定、界面、存档 UI 等订阅与事件，
    /// 会改变换装场景指纹里的事件总数等度量，而它们与"换装链"无关；表现层的真实装配与渲染由引擎侧宿主验证。姿势选择器与桥
    /// 的构造调用与 <c>PresentationAssembly</c> 逐行一致，不是另一套实现。
    /// </para>
    /// <para>
    /// 判断记录（动作步长）：动作时间线用 <c>SkillOptions.ActionStepSeconds</c> 换算毫秒到 tick，生产装配把它回填成宿主模拟步长，
    /// 所以换装脚本不再要求 tickRate 为 60（<c>equip_cycle_tick30</c> 用 30 证明）。
    /// </para>
    /// </summary>
    internal sealed class EquipRig : IDisposable
    {
        private readonly HeadlessWorld _world;
        private readonly Id _player;
        private readonly EquipRecording _record;
        private readonly FeelSystem _feel;
        private readonly IFeelEquipmentProvider _provider;
        private readonly EquipmentFeelChain _chain;
        private readonly IActionSkillBinding _binding;
        private readonly Id? _unarmed;
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

            var registry = world.Registry;
            var equipment = world.Gameplay.Carriers.Equipment;
            var production = world.Gameplay.Feel
                ?? throw new LabFormatException($"换装场景 {meta.ScriptId} 需要生产装配的手感系统（宿主应以 HeadlessWorldOptions.FeelOptions 开启）");
            // 读主手/副手武器引用用与生产装配同一实现、同一缺省选项的提供者（生产实例不对外暴露，实现是无状态读取）。
            _provider = new EquippedWeaponFeelProvider(equipment, registry);
            _feel = production.Feel;
            _chain = production.WeaponChain;
            _binding = production.ActionBinding;

            _unarmed = meta.UnarmedAttackSkill.Length > 0 ? new Id(meta.UnarmedAttackSkill) : (Id?)null;

            var catalog = new FeelWeaponCatalog(registry);

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

            // 玩家学会数据里每把武器声明的普攻时间线技能（auto_attack_timeline_ref，去重，数据行序）与空手普攻。
            var learned = new List<Id>();
            foreach (var row in registry.GetAll("feel.weapon"))
            {
                if (row.TryGetId("auto_attack_timeline_ref", out var attackRef) && !learned.Contains(attackRef))
                {
                    learned.Add(attackRef);
                }
            }

            if (_unarmed.HasValue)
            {
                learned.Add(_unarmed.Value);
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

        /// <summary>
        /// 普攻输入对应的技能：经生产装配的武器优先映射（主手武器的 <c>auto_attack_timeline_ref</c>）；武器没有声明（空手）且映射的槽位回落
        /// 也没有绑定时，取脚本声明的空手普攻技能。宿主仍按脚本的按下沿直接提交施放意图（不经输入缓冲，保持换装场景的响应度量口径）。
        /// <para>
        /// 判断记录（空手普攻不绑槽位）：生产装配的空手回落是"普攻动作的 <c>skill_slot</c> 槽位绑定"，绑定会发 <c>unit.skill_binding_changed</c>，
        /// 让换装指纹的事件总数多一条，而它与换装链无关；因此空手普攻由脚本声明、在映射给不出技能时取用（实验室装置里的最后一级回落）。
        /// </para>
        /// </summary>
        public bool TryResolveAttackSkill(string actionId, int tick, out Id skillId)
        {
            var intent = new BufferedIntent(new Id(actionId), ActionClass.Attack, tick, tick, 0, null, BufferHoldState.Tap, 0, false);
            if (_binding.TryResolveSkill(_player, intent, out skillId))
            {
                return true;
            }

            if (_unarmed.HasValue)
            {
                skillId = _unarmed.Value;
                return true;
            }

            skillId = default;
            return false;
        }

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
                    RebaseVersionAfterAction();
                    break;
                case ActionCancelledEvent cancelled when cancelled.ActorId.Equals(_player):
                    _action = null;
                    RebaseVersionAfterAction();
                    break;
            }
        }

        /// <summary>
        /// 生产装配的手感解析器在动作开始与结束时各使缓存失效一次（动作层，<c>InvalidatingActionFeelResolver</c>），版本号随之递增；
        /// 这不是换装链的重算，所以动作结束后把版本基线重新取到当前值，换装步骤上的"版本增量"只剩装备变化引起的那部分。
        /// </summary>
        private void RebaseVersionAfterAction() => _lastVersion = _feel.Resolver.GetVersion(_player);

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
        }
    }
}
