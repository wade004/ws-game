using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Numbers.StatBlock;
using Core.Rules.Common;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="IEquipmentHost"/> 默认实现（见 07 第 1.3/1.4 节）。
    /// <para>
    /// 判断记录 1——换装（目标槽已占用）的操作顺序：先把要装备的新实例从背包摘走（腾出一个格子），
    /// 再把旧实例的属性/技能/光环/套装加成撤销并放回背包。07 原文只说"槽已占用 → 先卸下……回背包"，
    /// 没有规定与"从背包移出新实例"的相对先后；本实现选择"先摘新、后还旧"而非按字面"先还旧、后摘
    /// 新"，是因为后者在 <see cref="InventoryOptions.MaxSlots"/> 紧张时会产生一个纯属实现顺序造成的
    /// 伪"背包已满"失败（旧实例还回去的那一刻背包个数暂时 +1，而新实例其实马上就要被摘走），
    /// 与最终状态（两次操作结束后格子数不变）不符；调整顺序后新旧交换在容量层面必然成功，不产生
    /// 该实现细节导致的意外失败分支。<see cref="EquipFailureReason.SlotOccupied"/> 因此不会被本实现
    /// 使用——07 对应的 <c>EquipResult</c> 契约注释本就允许"失败"或"按策略自动置换"两种实现选择
    /// （见 <c>Core.Carriers.Common.EquipFailureReason.SlotOccupied</c> 顶部注释），本模块选择后者。
    /// </para>
    /// <para>
    /// 判断记录 2——<see cref="Unequip"/> 在背包已满时的处理：任务书"背包满按 FullPolicy，拒绝时
    /// Unequip 返回 null 并记诊断"。本实现在改动任何状态（属性/技能/光环/套装加成）之前先探测背包
    /// 是否至少还有一个空位（<see cref="InventoryHost.HasRoomForOne"/>），没有空位则整个操作直接中止
    /// 返回 null（物品继续保持装备状态），不会出现"联动已撤销但物品从世界中消失"的数据丢失分支。
    /// <see cref="InventoryOptions.FullPolicy"/> 的 Reject/Partial 两个取值在这里退化为同一种行为——
    /// 归还的是恰好一个不可拆分的实例（07 校验要求装备类 <c>stack_size == 1</c>），不存在"部分归还"
    /// 的中间状态，Partial 策略在这种场景下没有比 Reject 更宽松的空间可言，见
    /// <see cref="InventoryHost.HasRoomForOne"/> 顶部注释。
    /// </para>
    /// <para>
    /// 判断记录 3——套装加成的来源标记：<see cref="IEffectSink.ApplyAura"/> 的 <c>sourceId</c> 参数
    /// 传套装本身的 id（<c>item.set.&lt;name&gt;</c>），不是任何一件具体物品实例 id——套装加成不属于
    /// 单件物品的联动效果，其生效/失效由"当前凑齐几件"这一集合状态决定，用套装 id 作为来源标记更
    /// 准确地表达"这条光环来自套装门槛，不来自某一件具体装备"，并且天然避免"卸下凑数的其中一件却
    /// 错误撤销了套装光环的 sourceId 归属"这类混淆（套装光环由 <see cref="RecomputeSetBonuses"/> 按
    /// 阈值显式 ApplyAura/RemoveAura 管理，不经 <see cref="IStatHost.RemoveModifiersBySource"/> 一类
    /// "按来源整体撤销"机制，因为光环没有等价的按来源整体撤销接口，见 <see cref="IEffectSink"/>）。
    /// </para>
    /// <para>
    /// 判断记录 4——<c>ISkillHost</c> 契约缺口：06 <c>ISkillHost</c> 没有"学习/遗忘技能"方法（技能书
    /// 相关能力目前只存在于 <c>core/rules/skill</c> 内部实现，未提升到共享契约），本模块改动范围
    /// 不允许修改 <c>core/rules/*</c>；构造参数改用 <see cref="SkillGranter"/> 具名委托绕过，由更
    /// 上层组装代码把真实的 <c>SkillHost.LearnSkill</c>/<c>ForgetSkill</c>（阶段 3 整理补齐）适配
    /// 成这个签名后注入（见该
    /// 委托类型顶部注释、任务书"契约缺口用模块内委托绕过并汇报"）。
    /// </para>
    /// </summary>
    public sealed class EquipmentHost : IEquipmentHost, IWeaponDamageQuery
    {
        private readonly IEventBus _bus;
        private readonly InventoryHost _inventory;
        private readonly IStatHost _statHost;
        private readonly IEffectSink _effectSink;
        private readonly SkillGranter _skillGranter;
        private readonly IUnitAccess _unitAccess;
        private readonly ItemOptions _options;
        private readonly IItemDiagnostics _diagnostics;

        /// <summary>CR140-02 根治（architecture/落地计划/audit-c86bfa9-20260908，P2）：C08 收口时
        /// 已经把这个可选依赖注入进来（此前只用于订阅 <see cref="IAuraQuery.InstanceReplaced"/>），
        /// 现在额外保留一份引用供 <see cref="ReapplyGrants"/> 用 <see cref="IAuraQuery.HasAura"/>
        /// 判断某个 <c>aura_def</c> 是否仍然生效——跨图 <c>World.ClearAll</c> 后重放装备/套装授予的
        /// Aura 时，靠这个查询避免对"确实还活着"的光环重复叠加（见该方法判断记录）。未注入时
        /// （<c>null</c>，多数测试用的最小假实现）<see cref="ReapplyGrants"/> 退化为"总是全部重新
        /// 施加"，调用方需自行保证不会在 Aura 仍然存活时重复调用。</summary>
        private readonly IAuraQuery? _auraQuery;

        private readonly IDataRegistryView _registry;
        private readonly Dictionary<Id, DataRecord> _slotDefinitions = new Dictionary<Id, DataRecord>();
        private readonly Dictionary<Id, DataRecord> _sets = new Dictionary<Id, DataRecord>();

        private readonly Dictionary<Id, Dictionary<Id, ItemInstance>> _equipped =
            new Dictionary<Id, Dictionary<Id, ItemInstance>>();

        /// <summary>RC-05 收边补齐：value 从 <c>List&lt;AuraInstanceRef&gt;</c> 改为
        /// <c>List&lt;(Id AuraDefId, AuraInstanceRef Ref)&gt;</c>——需要保留每条授予记录对应的
        /// <c>aura_def</c> id，供诊断/调试与 <see cref="RevertGrants"/> 逐条核对。</summary>
        private readonly Dictionary<(Id UnitId, Id InstanceId), List<(Id AuraDefId, AuraInstanceRef Ref)>> _grantedAuras =
            new Dictionary<(Id, Id), List<(Id, AuraInstanceRef)>>();

        /// <summary>
        /// N09 收边补齐（外部审计 68c9bed，P2）：装备 <c>grants.auras</c> 与套装门槛加成两处共用的
        /// 跨来源引用计数账本，按 <b>(unitId, 实际光环实例句柄 AuraInstanceId)</b> 计数（不是按
        /// <c>auraDefId</c>——原实现按 <c>aura_def</c> id 聚合计数踩过的坑，见
        /// <see cref="AuraHandleLedger"/> 类型注释历史小节）。
        /// <para>
        /// CORE-170-01 根治（architecture/落地计划/audit-8160178-20260908，P2）：这份计数此前是
        /// 本类私有字段，只有装备 <c>grants.auras</c>/套装门槛加成两处参与；种族/职业被动光环
        /// （<c>RulesAssembly.ReapplyRacePassiveAuras</c>）完全不参与，导致装备与种族共享同一
        /// <c>aura_def</c> 时卸装会把种族仍依赖的共享实例一并删除（见该缺陷判断记录）。改为
        /// <see cref="RulesAssembly"/> 持有的单一 <see cref="AuraHandleLedger"/> 实例，经构造函数
        /// 注入，使装备、套装门槛加成、种族三类来源共享同一份计数——本字段随之移除，全部原本读写
        /// <c>_auraHandleRefCount</c> 的地方改为调用 <see cref="_auraHandleLedger"/> 的
        /// <c>Register</c>/<c>Release</c>/<c>Forget</c>；<c>StackOverflowPolicy.Replace</c> 换句柄的
        /// 计数迁移也从本类 <see cref="OnAuraInstanceReplaced"/> 里移出，由
        /// <see cref="AuraHandleLedger"/> 自己订阅 <c>InstanceReplaced</c> 独立完成（见该类型判断
        /// 记录），本类 <see cref="OnAuraInstanceReplaced"/> 只保留 <see cref="_grantedAuras"/>/
        /// <see cref="_appliedSetBonuses"/> 这两份"我自己记着哪个句柄"的簿记迁移。
        /// </para>
        /// </summary>
        private readonly AuraHandleLedger _auraHandleLedger;

        private readonly Dictionary<(Id UnitId, Id SetId), Dictionary<int, List<AuraInstanceRef>>> _appliedSetBonuses =
            new Dictionary<(Id, Id), Dictionary<int, List<AuraInstanceRef>>>();

        /// <summary>T-N2-5（ADR-0032 决策 7/8）：词缀反解值——同 <see cref="IBudgetSolver"/>/<see
        /// cref="BudgetSolver"/> 判断记录"不持有可变状态、每次调用是纯函数"，未显式注入时本类自建
        /// 一份私有实例即可，不需要跨宿主共享同一份（同 <see cref="_auraHandleLedger"/> 未注入时
        /// 自建的既有惯例，但 <c>BudgetSolver</c> 连"跨来源共享账本"这一需求都没有，纯粹是避免每次
        /// 调用 <c>new BudgetSolver()</c> 的开销）。</summary>
        private readonly IBudgetSolver _budgetSolver;

        /// <summary>T-N2-5 判断记录（新增构造重载而不是给既有构造函数追加可选参数）：
        /// <c>toolchain/abi_probe.ps1</c> 把"给既有 <c>.ctor</c> 追加带默认值的新参数"仍判定为
        /// BREAKING——C# 源码层面重编译调用方无感（可选参数），但物理 IL 签名（参数个数）变了，
        /// 已编译的旧调用方（未重新编译、只换新 DLL）按原签名调用会失败，属于真正的二进制不兼容。
        /// 硬性规则 5"ABI 只允许新增"因此要求的是"新增一个重载"，不是"改写既有重载再加参数"——本
        /// 类型原 11 参构造函数签名原样保留（不追加任何参数），新增一个 12 参重载（末尾追加 <see
        /// cref="IBudgetSolver"/>），旧重载转发新重载并传 <c>budgetSolver: null</c>（等价于未提供，
        /// 走 <see cref="_budgetSolver"/> 判断记录"未显式注入时自建一份"）。</summary>
        public EquipmentHost(
            IDataRegistryView registry,
            IEventBus bus,
            InventoryHost inventory,
            IStatHost statHost,
            IEffectSink effectSink,
            SkillGranter skillGranter,
            IUnitAccess unitAccess,
            ItemOptions? options = null,
            IItemDiagnostics? diagnostics = null,
            IAuraQuery? auraQuery = null,
            AuraHandleLedger? auraHandleLedger = null)
            : this(registry, bus, inventory, statHost, effectSink, skillGranter, unitAccess,
                  options, diagnostics, auraQuery, auraHandleLedger, budgetSolver: null)
        {
        }

        /// <summary>T-N2-5（ADR-0032 决策 7/8）新增重载：见上一重载判断记录"新增构造重载而不是给
        /// 既有构造函数追加可选参数"。</summary>
        public EquipmentHost(
            IDataRegistryView registry,
            IEventBus bus,
            InventoryHost inventory,
            IStatHost statHost,
            IEffectSink effectSink,
            SkillGranter skillGranter,
            IUnitAccess unitAccess,
            ItemOptions? options,
            IItemDiagnostics? diagnostics,
            IAuraQuery? auraQuery,
            AuraHandleLedger? auraHandleLedger,
            IBudgetSolver? budgetSolver)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _statHost = statHost ?? throw new ArgumentNullException(nameof(statHost));
            _effectSink = effectSink ?? throw new ArgumentNullException(nameof(effectSink));
            _skillGranter = skillGranter ?? throw new ArgumentNullException(nameof(skillGranter));
            _unitAccess = unitAccess ?? throw new ArgumentNullException(nameof(unitAccess));
            _options = options ?? new ItemOptions();
            _diagnostics = diagnostics ?? new InMemoryItemDiagnostics();
            // T-N2-5：新增可选依赖，惯例同本类型其它可选依赖（未提供时自建一份，见 _budgetSolver
            // 判断记录）。
            _budgetSolver = budgetSolver ?? new BudgetSolver();
            // CORE-170-01 根治：真实装配（CarriersAssembly）传入 RulesAssembly.AuraHandles（与种族
            // 被动共享的同一账本）。未提供时（null，惯例同本类型其它可选依赖）本类自建一份私有账本
            // ——退化为修复前"只在装备/套装两处之间共享计数、不与种族来源共享"的行为，不影响不涉及
            // 种族共享 aura_def 的既有测试断言；多数测试用的最小假实现本就不构造真正的种族光环。
            _auraHandleLedger = auraHandleLedger ?? new AuraHandleLedger(effectSink, auraQuery);

            ReloadSlotDefinitionsAndSets();

            // P2-05 关联根治（外部审计 audit-c9ff301-20260909，见 InventoryHost 同一类判断记录）：
            // _slotDefinitions/_sets 此前只在构造期从 registry 读取一次、永久常驻。本类型本就持有
            // registry 引用，直接内部订阅、自行重新查询。
            _bus.Subscribe<DataLoadCompletedEvent>(DataRegistryEventKeys.LoadCompleted, _ => ReloadSlotDefinitionsAndSets());

            // C08 收口（外部审计 7e63d66 第四轮）：可选注入，未提供时（null，惯例同本类型其它可选
            // 依赖）行为与本次改动之前完全一致——只是重新暴露了 C08 描述的那个缺口，不会抛异常或
            // 改变既有测试断言。真实生产装配（见 CarriersAssembly）传入 Rules.Skill.AuraQuery（真实
            // AuraHost），使 StackOverflowPolicy.Replace 换句柄后本类记录的授予句柄能同步随之更新，
            // 见 OnAuraInstanceReplaced 判断记录。
            _auraQuery = auraQuery;
            if (auraQuery != null)
            {
                auraQuery.InstanceReplaced += OnAuraInstanceReplaced;
            }
        }

        private void ReloadSlotDefinitionsAndSets()
        {
            _slotDefinitions.Clear();
            foreach (var record in _registry.GetAll("item.slot_definition"))
            {
                _slotDefinitions[record.GetId("id")] = record;
            }

            _sets.Clear();
            foreach (var record in _registry.GetAll("item.set"))
            {
                _sets[record.GetId("id")] = record;
            }
        }

        /// <summary>
        /// C08 收口：<see cref="IAuraQuery.InstanceReplaced"/> 的订阅回调——<paramref name="oldInstanceId"/>
        /// 已经在光环系统内部失效，<paramref name="newInstanceId"/> 是接替它的新句柄。把
        /// <see cref="_grantedAuras"/> 里全部仍引用 <paramref name="oldInstanceId"/> 的授予记录（可能
        /// 来自任意一件装备，不只是刚发起本次 Replace 调用的那一件）原子迁移到新句柄。
        /// <para>
        /// CORE-170-01 根治：跨来源共享的引用计数迁移（原先在本方法内直接搬 <c>_auraHandleRefCount</c>）
        /// 已经随该字段一并上移到 <see cref="_auraHandleLedger"/>（<see cref="AuraHandleLedger"/>），
        /// 由它自己订阅同一个 <c>InstanceReplaced</c> 独立完成计数迁移，不需要本方法代劳——本方法此后
        /// 只保留 <see cref="_grantedAuras"/>/<see cref="_appliedSetBonuses"/> 这两份"我自己记着哪个
        /// 句柄"的簿记迁移，理由同 <see cref="AuraHandleLedger"/> 类型注释"三类来源各自仍然维护各自
        /// 私有簿记，本类只负责跨来源共享的计数"。
        /// </para>
        /// </summary>
        private void OnAuraInstanceReplaced(Id targetId, Id defId, Id oldInstanceId, Id newInstanceId)
        {
            foreach (var kv in _grantedAuras)
            {
                if (!kv.Key.UnitId.Equals(targetId))
                {
                    continue;
                }

                var list = kv.Value;
                for (var i = 0; i < list.Count; i++)
                {
                    if (list[i].Ref.AuraInstanceId.Equals(oldInstanceId))
                    {
                        list[i] = (list[i].AuraDefId, new AuraInstanceRef(newInstanceId));
                    }
                }
            }

            // R03 收边补齐：套装门槛加成（RecomputeSetBonuses/_appliedSetBonuses）持有的句柄引用
            // 同样要跟着迁移——理由与上面 _grantedAuras 完全对称：AllowMultiSourceTiming=false 时
            // 装备 grants.auras 与套装门槛加成对同一个 aura_def 施加会合并成同一份实例，任意一侧先
            // 达到 max_stacks 触发 Replace 都可能换掉另一侧已经记着的旧句柄；不迁移的话另一侧后续
            // 释放引用时会拿着一个已经不存在的旧句柄调用 ReleaseAuraHandle，既找不到对应计数，也
            // 无法让真正持有新句柄的那份计数归零——共享的光环实例最终会永久残留（不会被任何一侧
            // 正确移除）。
            foreach (var kv in _appliedSetBonuses)
            {
                if (!kv.Key.UnitId.Equals(targetId))
                {
                    continue;
                }

                foreach (var thresholdEntry in kv.Value)
                {
                    var list = thresholdEntry.Value;
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (list[i].AuraInstanceId.Equals(oldInstanceId))
                        {
                            list[i] = new AuraInstanceRef(newInstanceId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// T-N2-7 更新（见五参重载判断记录、<c>core/carriers/item/README.md</c> 判断记录 20）：
        /// <see cref="ItemInstance"/> 现已携带 <see cref="ItemInstance.Quality"/>/<see
        /// cref="ItemInstance.Affixes"/> 身份字段，三参重载不再无条件转发 <c>null</c>/<c>null</c>——
        /// 先查一次背包里这件物品的当前实例，转发它自带的品质/词缀（找不到实例时仍转发
        /// <c>null</c>/<c>null</c>，五参重载内部会再查一次 <see cref="InventoryHost.FindInstance"/>
        /// 并统一返回 <see cref="EquipFailureReason.NotInInventory"/>，行为与改动前一致，不额外抛
        /// 异常）。多查一次背包属于同一单位背包内的字典/列表查找，代价可忽略，换来的是三参重载不
        /// 再需要任何单独的"品质解析"逻辑——五参重载既有的 <c>qualityId ?? template.GetId("quality")</c>
        /// 兜底分支只在"确实没查到实例"这一种情况下才会生效。
        /// </summary>
        public EquipResult Equip(Id unitId, Id instanceId, Id slot)
        {
            var maybeInstance = _inventory.FindInstance(unitId, instanceId);
            return Equip(unitId, instanceId, slot, maybeInstance?.Quality, maybeInstance?.Affixes);
        }

        /// <summary>
        /// T-N2-5（ADR-0032 决策 7/8；07 第 1.6 节修订段"物品实例只存身份……读档按数据重算"）新增
        /// 公开重载：显式指定穿戴时用于词缀反解的品质/词缀身份。
        /// <para>
        /// **T-N2-7 更新**：<see cref="Core.Carriers.Common.ItemInstance"/> 已携带 <c>Quality</c>/
        /// <c>Affixes</c> 字段（见该类型顶部判断记录），三参 <see cref="Equip(Id, Id, Id)"/> 已按
        /// 本条判断记录原计划改为转发 <c>instance.Quality</c>/<c>instance.Affixes</c>（查不到实例
        /// 时仍转发 <c>null</c>/<c>null</c>，见该重载判断记录）——本方法自身逻辑不变，以下历史记录
        /// 原样保留供追溯。
        /// </para>
        /// <para>
        /// 历史判断记录（T-N2-5 落地时）：<see cref="Core.Carriers.Common.ItemInstance"/> 当时不携带
        /// <c>Quality</c>/<c>Affixes</c> 字段（要到分阶段落地计划 T-N2-7 才落地），但护甲/词缀反解值
        /// 写入 <see cref="IStatHost"/> 这条装备联动需要提前接上"按品质与词缀重算"的路径——任务书
        /// 原文"把'词缀值写入'实现为接受 (qualityId, IReadOnlyList&lt;Id&gt; affixIds) 的内部/公开
        /// 路径，并在既有装备路径里用'模板品质 + 空词缀'调用，T-N2-7 接上实例字段"。<paramref
        /// name="qualityId"/> 为 <c>null</c> 时按模板自身 <c>quality</c> 字段解析，<paramref
        /// name="affixIds"/> 为 <c>null</c> 时按空列表处理（模板自身 <c>stats</c>/护甲不受影响，只
        /// 影响"词缀反解值"这一段，见 <see cref="ApplyGrants"/>）。
        /// </para>
        /// </summary>
        public EquipResult Equip(Id unitId, Id instanceId, Id slot, Id? qualityId, IReadOnlyList<Id>? affixIds)
        {
            var maybeInstance = _inventory.FindInstance(unitId, instanceId);
            if (maybeInstance == null)
            {
                return EquipResult.Fail(EquipFailureReason.NotInInventory);
            }

            var template = RequireTemplate(maybeInstance.Value.TemplateId);
            var templateSlot = template.GetId("slot");
            if (!SlotMatches(templateSlot, slot))
            {
                return EquipResult.Fail(EquipFailureReason.SlotMismatch);
            }

            // 阶段 3 整理"事项二"：is_equipment=false 的槽位是分类桶（消耗品/材料），即便 slot 匹配
            // 也不可经 Equip 装备（见 ItemSchemas.SlotDefinition.is_equipment 判断记录）。
            if (!IsEquipmentSlot(slot))
            {
                return EquipResult.Fail(EquipFailureReason.SlotMismatch);
            }

            if (_options.EnforceRequirements && TryGetRequiredLevel(template, out var requiredLevel) &&
                _unitAccess.GetLevel(unitId) < requiredLevel)
            {
                return EquipResult.Fail(EquipFailureReason.RequirementNotMet);
            }

            if (!_inventory.TryTakeWhole(unitId, instanceId, out var taken))
            {
                // 竞态兜底：Equip 内两次访问 InventoryHost 之间理论上不会被并发改动（本层不引入
                // 多线程，见 00 架构总则"单线程确定性模拟"），此分支只做防御。
                return EquipResult.Fail(EquipFailureReason.NotInInventory);
            }

            // T-N2-5：见本方法判断记录——缺省按"模板自身品质 + 空词缀"解析。
            var resolvedQuality = qualityId ?? template.GetId("quality");
            var resolvedAffixes = affixIds ?? Array.Empty<Id>();

            // T-N2-7 判断记录——把解析结果写回 taken 的 Quality/Affixes（ItemInstance 新增字段前，
            // 本行不存在也不需要，因为没有字段可写）：ApplyGrants 用 resolvedQuality/resolvedAffixes
            // 把词缀反解值写进 StatHost，若存入 unitSlots[slot]/背包的 taken 仍是原样（可能来自
            // AddItemCore 缺省创建、Quality/Affixes 与 resolvedQuality/resolvedAffixes 不一致——如
            // 调用方显式传入了不同的 qualityId/affixIds），会出现"StatHost 上生效的品质/词缀"与
            // "GetAllEquippedInstances/EquipmentPersistable.Save 序列化出的身份字段"两者不一致——
            // 存档/读档（EquipmentPersistable.Load 走三参 Equip，转发的是存档里 instance.Quality/
            // Affixes）会用序列化出的（旧、不一致的）身份重新反解，读档后 StatHost 与存档前不再
            // 相等，直接违反 ADR-0032 决策 8"读档按数据重算……与存档前一致"这一不变量。因此这里必须
            // 用 resolvedQuality/resolvedAffixes 重建 taken 后再存入 unitSlots，保证"StatHost 上生效
            // 的身份"与"这件装备实例自己携带、会被存档序列化的身份"恒一致——不存在第二份影子状态。
            taken = new ItemInstance(taken.InstanceId, taken.TemplateId, taken.Count, resolvedQuality, resolvedAffixes, taken.Extra);

            var unitSlots = GetOrCreateUnitSlots(unitId);
            ItemInstanceRef? replaced = null;
            if (unitSlots.TryGetValue(slot, out var occupying))
            {
                var occupyingTemplate = RequireTemplate(occupying.TemplateId);
                RevertGrants(unitId, occupying, occupyingTemplate);
                unitSlots.Remove(slot);
                if (TryGetSetId(occupyingTemplate, out var occupyingSetId))
                {
                    RecomputeSetBonuses(unitId, occupyingSetId);
                }

                if (!_inventory.TryPutBack(unitId, occupying))
                {
                    _diagnostics.Warn(
                        $"Equip 换装：旧物品 \"{occupying.InstanceId}\" 未能放回单位 \"{unitId}\" 背包" +
                        "（不应发生，见 EquipmentHost 判断记录 1：换装前已让出一个格子）");
                }

                replaced = occupying.ToRef();
            }

            ApplyGrants(unitId, taken, template, resolvedQuality, resolvedAffixes);
            unitSlots[slot] = taken;
            if (TryGetSetId(template, out var setId))
            {
                RecomputeSetBonuses(unitId, setId);
            }

            _bus.Enqueue(new ItemEquippedEvent(unitId, instanceId, slot));
            return EquipResult.Ok(replaced);
        }

        public ItemInstanceRef? Unequip(Id unitId, Id slot)
        {
            var unitSlots = GetOrCreateUnitSlots(unitId);
            if (!unitSlots.TryGetValue(slot, out var instance))
            {
                return null;
            }

            if (!_inventory.HasRoomForOne(unitId))
            {
                _diagnostics.Warn(
                    $"Unequip 中止：单位 \"{unitId}\" 背包已满，槽位 \"{slot}\" 保持装备状态" +
                    "（见 EquipmentHost 判断记录 2）");
                return null;
            }

            var template = RequireTemplate(instance.TemplateId);
            RevertGrants(unitId, instance, template);
            unitSlots.Remove(slot);
            if (TryGetSetId(template, out var setId))
            {
                RecomputeSetBonuses(unitId, setId);
            }

            var putBack = _inventory.TryPutBack(unitId, instance);
            if (!putBack)
            {
                _diagnostics.Warn(
                    $"Unequip：物品 \"{instance.InstanceId}\" 未能放回单位 \"{unitId}\" 背包" +
                    "（不应发生，见 EquipmentHost 判断记录 2：已由 HasRoomForOne 预先确认有空位）");
            }

            _bus.Enqueue(new ItemUnequippedEvent(unitId, slot, instance.InstanceId));
            return instance.ToRef();
        }

        /// <summary>
        /// FND-10 收边补齐：卸下该单位当前全部已装备物品，撤销全部联动（属性修正/技能授予/光环/
        /// 套装加成），但物品本身不放回背包——供 <see cref="EquipmentPersistable.Load"/> 在应用
        /// 快照前，把单位重置到"无装备"的干净状态使用（见该类型判断记录"按快照替换完整装备状态"）。
        /// <para>
        /// 判断记录：读档是"完整替换"语义，不是"合并"——快照里不存在的已装备物品不应该凭空出现在
        /// 读档后的背包里，它们要么会被 <c>InventoryPersistable.Load</c> 从快照自身的背包数据里
        /// 放回（若确实还在那份历史快照的背包段中），要么本来就不该在这次读档后的世界里存在。
        /// 因此本方法只做联动撤销与 <see cref="_equipped"/> 内部字典清理，不调用
        /// <see cref="InventoryHost"/> 的任何写入方法，与调用方是否已经/将要处理背包段的相对顺序
        /// 无关（不像公开的 <see cref="Unequip"/> 那样把物品放回背包——那是"正常卸装备"的语义，
        /// 这里是"读档前先清空"的语义，两者刻意不同）。
        /// </para>
        /// </summary>
        internal void ClearAllEquippedForLoad(Id unitId)
        {
            if (!_equipped.TryGetValue(unitId, out var slots) || slots.Count == 0)
            {
                return;
            }

            var touchedSetIds = new HashSet<Id>();
            foreach (var kv in new List<KeyValuePair<Id, ItemInstance>>(slots))
            {
                var slot = kv.Key;
                var instance = kv.Value;
                var template = RequireTemplate(instance.TemplateId);
                RevertGrants(unitId, instance, template);
                slots.Remove(slot);
                if (TryGetSetId(template, out var setId))
                {
                    touchedSetIds.Add(setId);
                }

                _bus.Enqueue(new ItemUnequippedEvent(unitId, slot, instance.InstanceId));
            }

            foreach (var setId in touchedSetIds)
            {
                RecomputeSetBonuses(unitId, setId);
            }
        }

        public ItemInstanceRef? GetEquipped(Id unitId, Id slot) =>
            _equipped.TryGetValue(unitId, out var slots) && slots.TryGetValue(slot, out var instance)
                ? instance.ToRef()
                : (ItemInstanceRef?)null;

        public IReadOnlyDictionary<Id, ItemInstanceRef> GetAllEquipped(Id unitId)
        {
            var result = new Dictionary<Id, ItemInstanceRef>();
            if (_equipped.TryGetValue(unitId, out var slots))
            {
                foreach (var kv in slots)
                {
                    result[kv.Key] = kv.Value.ToRef();
                }
            }

            return result;
        }

        /// <summary>补充：该单位当前全部已装备槽位到完整 <see cref="ItemInstance"/>（含堆叠数/扩展
        /// 字段）的映射，供 <see cref="EquipmentPersistable.Save"/> 序列化使用——<see
        /// cref="IEquipmentHost.GetAllEquipped"/> 只返回不透明的 <see cref="ItemInstanceRef"/>，
        /// 存档需要完整数据，见该类型顶部判断记录。</summary>
        public IReadOnlyDictionary<Id, ItemInstance> GetAllEquippedInstances(Id unitId)
        {
            var result = new Dictionary<Id, ItemInstance>();
            if (_equipped.TryGetValue(unitId, out var slots))
            {
                foreach (var kv in slots)
                {
                    result[kv.Key] = kv.Value;
                }
            }

            return result;
        }

        /// <summary>消费方反馈第 52 条：把 <paramref name="slot"/> 当前已装备的 <see cref="ItemInstance"/>
        /// 身份字段（模板/品质/词缀）原样映射为 <see cref="EquippedWeaponSummary"/>，供消费方一次调用
        /// 取得与自建适配层手工搬运完全一致的三元组（见 <see cref="EquippedWeaponSummary"/> 类型顶部
        /// 判断记录）；该槽位当前无装备时返回 null。不要求 <paramref name="slot"/> 一定是
        /// <c>item.slot_definition.is_weapon</c> 的武器槽——见 <see cref="EquippedWeaponSummary"/>
        /// 判断记录"不限定只用于武器槽"。</summary>
        public EquippedWeaponSummary? GetEquippedWeaponSummary(Id unitId, Id slot) =>
            _equipped.TryGetValue(unitId, out var slots) && slots.TryGetValue(slot, out var instance)
                ? EquippedWeaponSummary.FromInstance(instance)
                : (EquippedWeaponSummary?)null;

        /// <summary>补充：该单位当前全部已装备槽位到 <see cref="EquippedWeaponSummary"/> 的映射，供
        /// <see cref="Core.Sim.StandardPlayerBuilder"/> 一次性取全部槽位的武器摘要（而不必按已知槽位
        /// 逐一调用 <see cref="GetEquippedWeaponSummary(Id, Id)"/>）——同 <see
        /// cref="GetAllEquippedInstances"/> 判断记录，槽位是否"是武器"由调用方按
        /// <c>item.slot_definition.is_weapon</c> 自行判断，本方法不做过滤（含非武器槽）。</summary>
        public IReadOnlyDictionary<Id, EquippedWeaponSummary> GetAllEquippedWeaponSummaries(Id unitId)
        {
            var result = new Dictionary<Id, EquippedWeaponSummary>();
            if (_equipped.TryGetValue(unitId, out var slots))
            {
                foreach (var kv in slots)
                {
                    result[kv.Key] = EquippedWeaponSummary.FromInstance(kv.Value);
                }
            }

            return result;
        }

        /// <summary>补充：当前装备在 <paramref name="slot"/> 的武器伤害/攻速定义（见 07 第 1.4 节第
        /// 3 点"普通攻击的数值输入……随之更新"、第 6 节"武器决定普通攻击动作……属于表现层职责"——
        /// 本方法只提供数值输入，不决定具体表现）。该槽位无装备，或物品没有 <c>weapon_profile</c>
        /// 字段时返回 null。</summary>
        public WeaponProfile? GetWeaponProfile(Id unitId, Id slot)
        {
            if (!_equipped.TryGetValue(unitId, out var slots) || !slots.TryGetValue(slot, out var instance))
            {
                return null;
            }

            var template = RequireTemplate(instance.TemplateId);
            if (!template.TryGetObject("weapon_profile", out var profile))
            {
                return null;
            }

            return new WeaponProfile(
                GetNumber(profile, "damage_min", 0),
                GetNumber(profile, "damage_max", 0),
                GetNumber(profile, "speed", 0),
                GetIdOpt(profile, "weapon_school"));
        }

        /// <summary>
        /// RC-11 收边补齐：<see cref="IWeaponDamageQuery"/> 实现——<c>weapon_damage_pct</c> 效果原语
        /// （<c>core/rules/skill.EffectDispatcher</c>，经 <c>Core.Rules.Assembly.
        /// DeferredWeaponDamageQuery</c> 依赖倒置注入）取"当前武器基础伤害"的唯一入口，见该接口
        /// 方法注释"判断记录"（<c>damage_min</c>/<c>damage_max</c> 均值；按槽位 id 序数最先命中的
        /// 武器槽为准；未装备任何武器槽返回 0）。不依赖调用方传入具体槽位——本方法自己按
        /// <see cref="_slotDefinitions"/> 的 <c>is_weapon</c> 标记（见 <see cref="IsEquipmentSlot"/>
        /// 同一份数据、ItemSchemas.SlotDefinition.is_weapon 判断记录）从该单位当前已装备的槽位里找
        /// "武器槽"，不需要框架层硬编码任何具体槽位 id（如 "main_hand"）——槽位命名完全由游戏内容
        /// 数据决定。
        /// <para>
        /// T-N2-6 判断记录（口径不变、硬性规则"禁止改既有签名"）：ADR-0032 决策 4 落地后武器改走
        /// "秒伤预算"，但本方法保留 <c>(damage_min+damage_max)/2</c> 语义——本方法只是把"找第一个
        /// 武器槽"这一步抽成 <see cref="TryGetFirstWeaponSlot"/> 供 <see cref="GetWeaponDps"/> 共用，
        /// 行为与返回值同改动前逐位一致。更新（T-N3-3 已落地）：<c>weapon_damage_pct</c> 原语改接
        /// 秒伤 × 一拍常数后不再调用本方法（见 <see cref="IWeaponDamageQuery.GetWeaponBaseDamage"/>
        /// 判断记录"更新"段），本方法保留供其它消费方，实现不受影响。
        /// </para>
        /// </summary>
        public double GetWeaponBaseDamage(Id unitId)
        {
            if (!TryGetFirstWeaponSlot(unitId, out var weaponSlot))
            {
                return 0.0;
            }

            var profile = GetWeaponProfile(unitId, weaponSlot);
            return profile.HasValue ? (profile.Value.DamageMin + profile.Value.DamageMax) / 2.0 : 0.0;
        }

        /// <summary>T-N2-6：<see cref="GetWeaponBaseDamage"/>/<see cref="GetWeaponDps"/> 共用的"找第一个
        /// 武器槽"步骤，从 <see cref="GetWeaponBaseDamage"/> 原实现原样抽出（多个武器槽/双持时按槽位 id
        /// 序数最先命中，见该方法既有判断记录），不改变既有选择逻辑。</summary>
        private bool TryGetFirstWeaponSlot(Id unitId, out Id weaponSlot)
        {
            weaponSlot = default;
            if (!_equipped.TryGetValue(unitId, out var slots) || slots.Count == 0)
            {
                return false;
            }

            var weaponSlotIds = new List<Id>();
            foreach (var slot in slots.Keys)
            {
                if (_slotDefinitions.TryGetValue(slot, out var slotDef) &&
                    slotDef.TryGetBool("is_weapon", out var isWeapon) && isWeapon)
                {
                    weaponSlotIds.Add(slot);
                }
            }

            if (weaponSlotIds.Count == 0)
            {
                return false;
            }

            weaponSlotIds.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            weaponSlot = weaponSlotIds[0];
            return true;
        }

        /// <summary>
        /// T-N2-6（ADR-0032 决策 4；07 第 1.2 节修订段）：<see cref="IWeaponDamageQuery.GetWeaponDps"/>
        /// 实现——武器秒伤 = <c>item.weapon_dps_curve</c>（id 取 <see cref="ItemOptions.WeaponDpsCurveId"/>）
        /// 在该武器模板 <c>item_level</c> 处求值 × 品质预算倍率（<c>item.quality_definition
        /// .budget_multiplier</c>，找不到对应品质记录时缺省 1，同 <see cref="ItemBudgetValidationRule"/>
        /// 既有口径）× 武器槽位系数（该武器所在槽位的 <c>item.slot_definition.budget_coefficient</c>，
        /// 缺省 1）。武器槽的选取规则同 <see cref="GetWeaponBaseDamage"/>（<see
        /// cref="TryGetFirstWeaponSlot"/>）；未装备任何武器槽、或曲线找不到对应记录时返回 0，不抛
        /// 异常（同 <see cref="ApplyArmorValue"/>"曲线缺失按不写处理"既有口径）。
        /// <para>
        /// 判断记录（品质取值来源，2026-09-16 深度复审 B-M1 修正）：品质预算倍率改为读取这件已装备
        /// 实例真实的 <see cref="ItemInstance.Quality"/>（掉落三次掷骰、<see cref="Equip(Id, Id, Id,
        /// Id?, System.Collections.Generic.IReadOnlyList{Id})"/> 穿戴时写入），不再取武器模板自身
        /// 登记的静态默认 <c>quality</c> 字段——同批 <see cref="ApplyAffixValues"/> 早已在 T-N2-7 落地
        /// 时改用 <c>resolvedQuality</c>，本方法此前遗漏未跟进，导致"装备品质 ≠ 模板默认品质"（掉落
        /// 系统的常规产出）场景下武器秒伤按错误的品质倍率计算，见复审报告 B-M1。
        /// </para>
        /// </summary>
        public double GetWeaponDps(Id unitId)
        {
            if (!TryGetFirstWeaponSlot(unitId, out var weaponSlot))
            {
                return 0.0;
            }

            if (!_equipped.TryGetValue(unitId, out var slots) || !slots.TryGetValue(weaponSlot, out var instance))
            {
                return 0.0;
            }

            var template = RequireTemplate(instance.TemplateId);

            var curveRecord = _registry.Get("item.weapon_dps_curve", _options.WeaponDpsCurveId);
            if (curveRecord == null)
            {
                return 0.0;
            }

            var curve = CurveSchema.ReadBreakpoints(curveRecord, "entries");
            var itemLevel = (int)template.GetInt("item_level");
            var baseDps = curve.Evaluate(itemLevel);

            var qualityRecord = _registry.Get("item.quality_definition", instance.Quality);
            var qualityMultiplier = qualityRecord != null && qualityRecord.TryGetNumber("budget_multiplier", out var qm)
                ? qm
                : 1.0;

            var slotCoefficient = _slotDefinitions.TryGetValue(weaponSlot, out var slotDef) &&
                slotDef.TryGetNumber("budget_coefficient", out var sc)
                ? sc
                : 1.0;

            return baseDps * qualityMultiplier * slotCoefficient;
        }

        // -----------------------------------------------------------------
        // 装备联动：属性 / 技能 / 光环（07 第 1.4 节步骤 1/2）
        // -----------------------------------------------------------------

        private void ApplyGrants(Id unitId, ItemInstance instance, DataRecord template, Id qualityId, IReadOnlyList<Id> affixIds)
        {
            if (template.TryGetArray("stats", out var stats))
            {
                foreach (var raw in stats)
                {
                    if (!(raw is JsonObject obj))
                    {
                        continue;
                    }

                    var statId = RequireId(obj, "stat");
                    var op = ParseOp(GetString(obj, "op", "flat"));
                    var value = GetNumber(obj, "value", 0);
                    _statHost.AddModifier(unitId, new StatModifier(statId, op, value, instance.InstanceId));
                }
            }

            // T-N2-5（ADR-0032 决策 4）：护甲值——非 stats 来源，同 sourceId（instance.InstanceId），
            // 卸下时随 RevertGrants 的 RemoveModifiersBySource 一并撤销，不需要单独的撤销路径。
            ApplyArmorValue(unitId, instance, template);

            // T-N2-5（ADR-0032 决策 7/8）：词缀反解值——同 sourceId，随穿戴按数据重算；本任务的
            // 调用点见 Equip(Id,Id,Id,Id?,IReadOnlyList<Id>?) 判断记录（模板品质 + 空词缀）。
            ApplyAffixValues(unitId, instance, template, qualityId, affixIds);

            if (!template.TryGetObject("grants", out var grants))
            {
                return;
            }

            if (grants.TryGetValue("skills", out var skillsRaw) && skillsRaw is JsonArray skillsArr)
            {
                foreach (var s in skillsArr)
                {
                    if (s is JsonString ss && Id.TryParse(ss.Value, out var skillId))
                    {
                        // RC-05 收边补齐：以本装备实例 id 作为来源传给 SkillGranter（见该委托类型
                        // 判断记录）——接收端（SkillHost）按来源做引用计数，卸下这件装备只撤销这一
                        // 个来源，不影响永久学习或其它装备来源仍在授予的同一技能。
                        _skillGranter(unitId, skillId, instance.InstanceId, true);
                    }
                }
            }

            if (grants.TryGetValue("auras", out var aurasRaw) && aurasRaw is JsonArray aurasArr && aurasArr.Count > 0)
            {
                // R03 收边补齐（外部审计 5e779c6，P2）：list 必须在遍历开始前就登记进
                // _grantedAuras（不是遍历结束后一次性赋值）——同一件装备的 grants.auras 里重复
                // 两次同一个 aura_def 时（如测试数据 item.repro.duplicate），第二次 ApplyAura 会因为
                // maxStacks 溢出触发 StackOverflowPolicy.Replace，同步整发 OnAuraInstanceReplaced。
                // 该回调按"_grantedAuras 里已登记的条目"做句柄迁移；如果这里的 list 还只是一个未登记
                // 的局部变量，回调找不到第一次施加留下的那条记录去迁移，list 里就会残留一个已经失效
                // 的旧句柄引用，且 _auraHandleRefCount 会被 ApplyGrants 自身的计数与回调迁移的计数
                // 重复累加——Unequip 时因为多算的计数无法归零，光环卸不干净（见外部审计复现日志
                // ReproEquipmentDuplicate 的 duplicate 分支）。提前登记后，回调能在同一次循环内原地
                // 更新已有条目，list 与 _auraHandleRefCount 全程保持一致。
                var key = (unitId, instance.InstanceId);
                var list = new List<(Id, AuraInstanceRef)>();
                _grantedAuras[key] = list;

                foreach (var a in aurasArr)
                {
                    if (a is JsonString asStr && Id.TryParse(asStr.Value, out var auraDefId))
                    {
                        // N09 收边补齐：先拿到本次施加实际落地的实例句柄，再按句柄（不是 auraDefId）
                        // 计数——见 _auraHandleRefCount 判断记录。
                        var granted = _effectSink.ApplyAura(unitId, auraDefId, instance.InstanceId);
                        RegisterAuraHandle(unitId, granted.AuraInstanceId);

                        list.Add((auraDefId, granted));
                    }
                }
            }
        }

        /// <summary>
        /// T-N2-5（ADR-0032 决策 4；07 第 1.2 节修订段"护甲值 = item.armor_curve(item_level) ×
        /// 槽位系数，仅护甲位，不占预算"）：把该件装备的护甲值经 <see cref="IStatHost.AddModifier"/>
        /// 写入 <see cref="ItemOptions.ArmorStatId"/>，来源同该件装备实例 id（<see
        /// cref="ItemInstance.InstanceId"/>）——与模板 <c>stats</c>/词缀反解值共用同一个 sourceId
        /// （任务书硬性规则"禁止用第二个 sourceId 写词缀值"，护甲值同理，一并遵守），卸下时随
        /// <see cref="RevertGrants"/> 的 <see cref="IStatHost.RemoveModifiersBySource"/> 一并撤销。
        /// <para>
        /// 判断记录（护甲位判定——T-N2-6 设计层裁定，取代 T-N2-5 的推断规则）：T-N2-5 曾按"非武器位
        /// （<c>is_weapon != true</c>）且是真正装备位（<c>is_equipment != false</c>）"推断护甲位，
        /// 代价是戒指/项链/饰品一类传统意义上不该有护甲值的槽位也会被写入护甲修正，与魔兽世界"护甲
        /// 仅头肩胸手腕手腰腿脚背盾"的更细分类不同（见该版本判断记录，`core/carriers/item/README.md`
        /// 判断记录 20）。设计层就此裁定：<c>item.slot_definition</c> 新增显式可选字段
        /// <c>has_armor</c>（见 <see cref="ItemSchemas.SlotDefinition"/>，缺省 <c>false</c>），
        /// <see cref="IsArmorSlot"/> 只看 <c>has_armor == true</c>，不再从 <c>is_weapon</c>/
        /// <c>is_equipment</c> 推断——游戏层需要显式给每个防具位（头/胸/腿/手/脚等）登记
        /// <c>has_armor: true</c>，戒指/饰品/武器位缺省 <c>false</c> 即不写护甲。
        /// </para>
        /// <para>
        /// 判断记录（护甲曲线缺失时的行为）：<see cref="ItemOptions.ArmorCurveId"/> 在已加载的
        /// <c>item.armor_curve</c> 里找不到对应记录（游戏层尚未登记，或使用了非默认曲线 id 却未同步
        /// 配置 <see cref="ItemOptions"/>）时，按"不写护甲"处理，直接返回，不抛异常——同 <see
        /// cref="TryGetRequiredLevel"/> 需求等级曲线缺失时的既有口径一致（任务书"实现要点……曲线缺失
        /// 时行为：护甲不写、需求等级视为 0"）。
        /// </para>
        /// </summary>
        private void ApplyArmorValue(Id unitId, ItemInstance instance, DataRecord template)
        {
            var slot = template.GetId("slot");
            if (!IsArmorSlot(slot))
            {
                return;
            }

            var curveRecord = _registry.Get("item.armor_curve", _options.ArmorCurveId);
            if (curveRecord == null)
            {
                return;
            }

            var curve = CurveSchema.ReadBreakpoints(curveRecord, "entries");
            var itemLevel = (int)template.GetInt("item_level");
            var slotCoefficient = _slotDefinitions.TryGetValue(slot, out var slotDef) &&
                slotDef.TryGetNumber("budget_coefficient", out var sc)
                ? sc
                : 1.0;

            var armorValue = curve.Evaluate(itemLevel) * slotCoefficient;
            _statHost.AddModifier(unitId, new StatModifier(_options.ArmorStatId, StatModifierOp.Flat, armorValue, instance.InstanceId));
        }

        /// <summary>见 <see cref="ApplyArmorValue"/> 判断记录"护甲位判定"（T-N2-6 设计层裁定）——只看
        /// 槽位显式字段 <c>item.slot_definition.has_armor</c>，缺省 <c>false</c>。</summary>
        private bool IsArmorSlot(Id slot) =>
            _slotDefinitions.TryGetValue(slot, out var slotDef) &&
            slotDef.TryGetBool("has_armor", out var hasArmor) && hasArmor;

        /// <summary>
        /// T-N2-5（ADR-0032 决策 7/8；07 第 1.6 节修订段）：按 <paramref name="affixIds"/> 逐条把
        /// <c>item.affix</c> 的词缀经 <see cref="IBudgetSolver.Solve"/> 反解出各属性点数，与模板
        /// <c>stats</c>/护甲值一起以同一 sourceId（<see cref="ItemInstance.InstanceId"/>）写入 <see
        /// cref="IStatHost"/>——"具体数值在掉落那一刻……算出"（07 原文）在本模块的落地口径是"穿戴时
        /// 按数据重算"，不在物品实例上缓存反解结果（10 第 2.5 节"属性快照默认不存"的延伸，同 ADR-0032
        /// 决策 8"物品实例只存身份……读档按数据重算"）。
        /// <para>
        /// 判断记录（<c>shareOfBudget = affix.budget_share × Σratio</c>，<c>statMix</c> 归一化）：
        /// <see cref="IBudgetSolver.Solve"/> 要求 <c>statMix</c> 的 <c>ratio</c> 之和严格为 1（见该
        /// 接口判断记录），但 <c>item.affix.stat_mix</c> 的存量数据只保证"之和不超过一"（<see
        /// cref="ItemAffixStatMixRatioSumRule"/>）。本方法按 <see cref="BudgetSolver"/> 类型判断记录
        /// 给出的归一化方案：记原始比例之和为 <c>Σratio</c>，传入 <c>Solve</c> 的 <c>statMix</c> 按
        /// <c>ratio_i / Σratio</c> 归一化（之和恰为 1，满足输入前提，且不改变各属性间的相对比例），
        /// 同时把 <c>shareOfBudget</c> 由 <c>budget_share</c> 改传 <c>budget_share × Σratio</c>——
        /// 这样"该条词缀内部未用满的份额"（<c>Σratio &lt; 1</c>）会按比例折算进实际反解出的目标预算，
        /// 不会把"内部分配不满一"错误放大成"仍按 <c>budget_share</c> 全额反解"。任务书原文给出的正是
        /// 这个乘积形式（"shareOfBudget = affix.budget_share × Σratio"），本方法按此实现。
        /// </para>
        /// <para>
        /// 判断记录（引用完整性/形状异常不抛，记诊断跳过该条词缀）：<paramref name="affixIds"/>
        /// 引用的 <c>item.affix</c> 记录理论上已经过 <c>reference_integrity</c> 校验，运行期查不到
        /// 视为数据未通过校验时的防御分支（同 <see cref="RequireTemplate"/> 惯例，但降级为跳过而非
        /// 抛异常——装备联动是"尽量把能算的算出来"的运行期路径，单条词缀数据异常不应该让整次穿戴
        /// 失败）：记录 <see cref="IItemDiagnostics.Warn"/> 后跳过该条词缀，继续处理其余词缀。
        /// <c>budget_share &lt;= 0</c> 或 <c>stat_mix</c> 为空/全部比例非正同样按"这条词缀不贡献任何
        /// 属性"处理，静默跳过（不是数据错误，只是这条词缀恰好不产生数值，同 07 原文"授予规则"里
        /// 某些池子允许零值词缀的表述口径一致）。
        /// </para>
        /// </summary>
        private void ApplyAffixValues(Id unitId, ItemInstance instance, DataRecord template, Id qualityId, IReadOnlyList<Id> affixIds)
        {
            if (affixIds == null || affixIds.Count == 0)
            {
                return;
            }

            var slot = template.GetId("slot");
            var itemLevel = (int)template.GetInt("item_level");

            // 2026-09-16 深度复审 B-S1：在词缀循环外一次性构建 StatBudgetInfo，循环内经 IBudgetSolver
            // 新增的 8 参重载复用同一份——此前一件 N 词缀的装备穿戴一次会触发 N 次全表扫描
            // （stat.definition/stat.weight/stat.rating_conversion）+ N 次新 Dictionary 分配，见该
            // 重载判断记录。
            var statBudgetInfo = ItemBudgetCurve.BuildStatBudgetInfo(_registry);

            foreach (var affixId in affixIds)
            {
                var affixRecord = _registry.Get("item.affix", affixId);
                if (affixRecord == null)
                {
                    _diagnostics.Warn(
                        $"装备 \"{instance.InstanceId}\" 引用的词缀 \"{affixId}\" 在 item.affix 中不存在" +
                        "（应已通过 item.template.affixes/item.affix 引用完整性校验），本次穿戴跳过该词缀");
                    continue;
                }

                var budgetShare = affixRecord.TryGetNumber("budget_share", out var bs) ? bs : 0.0;
                if (budgetShare <= 0.0)
                {
                    continue;
                }

                if (!affixRecord.TryGetArray("stat_mix", out var mixArray) || mixArray.Count == 0)
                {
                    continue;
                }

                var rawMix = new List<(Id Stat, double Ratio)>(mixArray.Count);
                double ratioSum = 0;
                foreach (var raw in mixArray)
                {
                    if (!(raw is JsonObject obj))
                    {
                        continue;
                    }

                    var statId = RequireId(obj, "stat");
                    var ratio = GetNumber(obj, "ratio", 0);
                    if (ratio <= 0.0)
                    {
                        continue;
                    }

                    rawMix.Add((statId, ratio));
                    ratioSum += ratio;
                }

                if (rawMix.Count == 0 || ratioSum <= 0.0)
                {
                    continue;
                }

                var normalizedMix = new List<(Id Stat, double Ratio)>(rawMix.Count);
                foreach (var entry in rawMix)
                {
                    normalizedMix.Add((entry.Stat, entry.Ratio / ratioSum));
                }

                // 防御：正常数据经 ItemAffixStatMixRatioSumRule（ratioSum <= 1）与 budget_share 的
                // [0,1] 字段范围校验后乘积必然 <= 1，这里的夹取只应对校验被绕过（如直接构造测试数据）
                // 的场景，避免 IBudgetSolver.Solve 的 shareOfBudget 越界校验抛出与本方法契约不符的
                // 异常——本方法对外的失败模式统一是"跳过该条词缀 + 记诊断"，不是抛异常。
                var shareOfBudget = budgetShare * ratioSum;
                if (shareOfBudget > 1.0)
                {
                    shareOfBudget = 1.0;
                }

                var result = _budgetSolver.Solve(
                    itemLevel, qualityId, slot, normalizedMix, _options.BudgetCurveId, shareOfBudget, _registry,
                    statBudgetInfo);

                foreach (var kv in result.Values)
                {
                    _statHost.AddModifier(unitId, new StatModifier(kv.Key, StatModifierOp.Flat, kv.Value, instance.InstanceId));
                }
            }
        }

        /// <summary>
        /// R03 收边补齐：登记"多了一个来源持有这个光环实例句柄"，供 <see cref="ReleaseAuraHandle"/>
        /// 配对释放。<see cref="ApplyGrants"/>（装备本身的 <c>grants.auras</c>）与
        /// <see cref="RecomputeSetBonuses"/>（套装门槛加成）此前各自维护互不相通的簿记——
        /// <c>AllowMultiSourceTiming=false</c>（默认）时两者对同一个 <c>aura_def</c> 施加会在
        /// <c>AuraHost</c> 内合并成<b>同一个</b>实例句柄（见 <c>AuraHost.ApplyAura</c> 按
        /// <c>(target, aura_def)</c> 合并槽位），只要其中一处不参与共享计数，另一处卸载/降级时就会
        /// 无条件调用 <see cref="IEffectSink.RemoveAura"/> 把仍被别处引用的共享实例整个移除——外部
        /// 审计复现场景正是"卸下普通装备后，仍满足件数门槛的套装光环被一并删除"（见外部审计
        /// ReproEquipmentDuplicate 的 set 分支）。统一改为两处都经这一对方法登记/释放，只有全部来源
        /// 都释放完毕（计数归零）才真正调用 <see cref="IEffectSink.RemoveAura"/>。CORE-170-01 根治：
        /// 实际计数已上移到 <see cref="_auraHandleLedger"/>（<see cref="AuraHandleLedger"/>），本方法
        /// 只是转发，保留这一对方法名是为了本类内部全部调用点不必逐一改名，且方法名本身仍然准确
        /// 描述了"这个来源新持有一份引用"的语义。
        /// </summary>
        private void RegisterAuraHandle(Id unitId, Id auraInstanceId) => _auraHandleLedger.Register(unitId, auraInstanceId);

        /// <summary>见 <see cref="RegisterAuraHandle"/> 判断记录——释放一个来源持有的引用；仍有其它
        /// 来源持有同一句柄时只递减计数，真正归零时才调用 <see cref="IEffectSink.RemoveAura"/>。</summary>
        private void ReleaseAuraHandle(Id unitId, AuraInstanceRef auraRef) => _auraHandleLedger.Release(unitId, auraRef);

        private void RevertGrants(Id unitId, ItemInstance instance, DataRecord template)
        {
            _statHost.RemoveModifiersBySource(unitId, instance.InstanceId);

            if (template.TryGetObject("grants", out var grants) &&
                grants.TryGetValue("skills", out var skillsRaw) && skillsRaw is JsonArray skillsArr)
            {
                foreach (var s in skillsArr)
                {
                    if (s is JsonString ss && Id.TryParse(ss.Value, out var skillId))
                    {
                        // 只撤销"这件装备"这一个来源（见 ApplyGrants 判断记录）。
                        _skillGranter(unitId, skillId, instance.InstanceId, false);
                    }
                }
            }

            var key = (unitId, instance.InstanceId);
            if (_grantedAuras.TryGetValue(key, out var list))
            {
                foreach (var (_, r) in list)
                {
                    // N09/R03 收边补齐：按实例句柄（不是 auraDefId）计数，见 _auraHandleRefCount /
                    // RegisterAuraHandle/ReleaseAuraHandle 判断记录——AllowMultiSourceTiming=true 下
                    // 每件装备的句柄互不相同，计数恒为 1，立即精确移除；=false 下多件装备（含套装门槛
                    // 加成，见 RecomputeSetBonuses）可能共享同一句柄，计数聚合，只在最后一个引用退出
                    // 时才真正移除。
                    ReleaseAuraHandle(unitId, r);
                }

                _grantedAuras.Remove(key);
            }
        }

        /// <summary>
        /// CR140-02 根治（architecture/落地计划/audit-c86bfa9-20260908，P2）：跨图 <c>World.ClearAll</c>
        /// 触发 <c>entity.destroyed</c>，<c>AuraHost</c>（<c>core/rules/skill</c>）响应该事件移除目标
        /// 名下全部运行期 Aura 实例（含装备 <c>grants.auras</c>/套装门槛加成——见 <see
        /// cref="ApplyGrants"/>/<see cref="RecomputeSetBonuses"/> 施加时用的 <c>IEffectSink.ApplyAura</c>，
        /// 其反向撤销走同一个"按 <c>entity.destroyed</c> 清空"路径）；但本类的 <see cref="_equipped"/>/
        /// <see cref="_grantedAuras"/>/<see cref="_appliedSetBonuses"/> 全部按 <c>unitId</c>（不是
        /// <c>entityId</c>）记账，与 <see cref="IWorldSim"/> 的实体生命周期无关，<c>ClearAll</c> 完全
        /// 不触碰——玩家实体重新登记回 <see cref="IWorldSim"/> 后，<see cref="_equipped"/> 仍然"记得"
        /// 装备着哪些物品，装备本身的属性加成（<see cref="IStatHost.AddModifier"/>）与技能授予（<see
        /// cref="SkillGranter"/>）也完好——<see cref="IStatHost"/>/<c>core/rules/skill</c> 的技能授予
        /// 台账同样按 <c>unitId</c> 记账，不监听 <c>entity.destroyed</c>——唯独 <see
        /// cref="_grantedAuras"/>/<see cref="_appliedSetBonuses"/> 里记录的 <see cref="AuraInstanceRef"/>
        /// 句柄全部失效（<c>AuraHost</c> 那边已经不认得），装备看起来"还穿着"、实际光环全部消失，
        /// 直到重新装/卸一次才会被动刷新，中间这段时间玩家的光环相关加成（伤害减免、免疫、控制抗性
        /// 等）凭空缺失。
        /// <para>
        /// 调用方（装配根 <c>GameplayAssembly.EnterMap</c>，即 <c>ISceneRouter</c> <c>post_load</c>
        /// 统一钩子，玩家实体重新登记进 <see cref="IWorldSim"/> 之后）应调用本方法：按
        /// <c>instance→definition→grants</c> 对 <paramref name="unitId"/> 当前 <see cref="_equipped"/>
        /// 里每件装备重放 <c>grants.auras</c>（不重放 <c>stats</c>/<c>skills</c>——那两类从未真正丢失，
        /// 重放会造成双重叠加，见上），并对涉及到的每个套装重新走一遍套装门槛判定。
        /// </para>
        /// <para>
        /// 幂等（<see cref="_auraQuery"/> 可用时，真实装配恒可用，见该字段判断记录）：逐条用
        /// <see cref="IAuraQuery.HasAura"/> 核实——已经生效的 <c>aura_def</c>（典型如本方法被意外
        /// 连续调用两次，或压根没有发生过 <c>ClearAll</c>）沿用 <see cref="_grantedAuras"/> 里已经
        /// 记录的句柄，不重新 <c>ApplyAura</c>（<c>AllowMultiSourceTiming=true</c> 时重复施加会产生
        /// 独立新叠层实例，不能靠"再施加一次反正会合并"蒙混过去）；只有确认已经不生效的才重新施加。
        /// </para>
        /// <para>
        /// CR150-01 根治（architecture/落地计划/audit-3224ca1-20260908，P2）：上面这条幂等判断必须用
        /// "调用本方法之前，这个 <c>aura_def</c> 是否已经生效"这个<b>在改动任何状态之前就固定下来的
        /// 快照</b>去驱动，不能在逐件重放的循环<b>过程中</b>反复实时调用 <see cref="IAuraQuery.HasAura"/>。
        /// 两件装备共享同一 <c>aura_def</c>（默认 <c>AllowMultiSourceTiming=false</c>）时，本方法重放
        /// 第一件会把该 <c>aura_def</c> 的 <c>HasAura</c> 从 false 变为 true——如果第二件用"循环期间
        /// 实时查询"的结果来判断，会把这次由第一件重放造成的"刚刚变活"误判成"从来没有失效过"，进而
        /// 走幂等分支去复用自己（第二件）名下那份早已随 <c>ClearAll</c> 失效的旧句柄：这份旧句柄既没有
        /// 被重新 <c>RegisterAuraHandle</c>，也不是 <c>AuraHost</c> 真正认得的活句柄，导致共享光环的
        /// 引用计数只算上了第一件——卸下第一件时第二件仍装备着，计数却已经归零，光环被误删（外部审计
        /// 复现：<c>afterFirstUnequip</c> 实际 False，预期仍应为 True）。改为下面这个逐 <c>aura_def</c>
        /// 惰性缓存、只在"这个 def 第一次被问到"时真正查询一次 <see cref="IAuraQuery.HasAura"/>、此后
        /// 同一次 <see cref="ReapplyGrants"/> 调用内全部复用同一个结果的写法后：第一件、第二件对同一个
        /// <c>aura_def</c> 拿到的都是"本方法开始执行前"那个真实值——两件都判定为"确实已失效"，各自都
        /// 会调用 <see cref="IEffectSink.ApplyAura"/>（与最初 <see cref="ApplyGrants"/> 完全同一路径，
        /// 本类不在这一层揣测/复用其它来源的句柄）并各自 <see cref="RegisterAuraHandle"/>；
        /// <c>AllowMultiSourceTiming=false</c> 下第二次 <c>ApplyAura</c> 会被 <c>AuraHost</c> 自己按
        /// <c>(target, aura_def)</c> 合并回第一件刚创建的同一个实例，两次登记自然聚合成计数 2，与两件
        /// 装备各自正常 <see cref="Equip"/> 时的稳态完全一致；<c>AllowMultiSourceTiming=true</c> 下
        /// 两次 <c>ApplyAura</c>（不同 sourceId）则各自独立开出两个实例，计数各自恒为 1，同样与该模式
        /// 下装备的稳态一致——两种模式都不需要本类额外分支处理，全部交给 <c>AuraHost</c> 的既有合并
        /// 策略决定，本方法只负责"是否要重新调用 ApplyAura"这一个判断。幂等场景（未发生 <c>ClearAll</c>，
        /// 或本方法被意外连续调用）下，"调用前快照"与"循环期间实时查询"结果相同（因为本来就没有任何
        /// 状态被改动过），因此这处收紧不影响原有幂等断言。
        /// </para>
        /// </summary>
        public void ReapplyGrants(Id unitId)
        {
            if (!_equipped.TryGetValue(unitId, out var slots) || slots.Count == 0)
            {
                return;
            }

            // CR150-01 根治：见上方判断记录——按 aura_def 惰性缓存"本次 ReapplyGrants 调用开始前是否
            // 已生效"，同一次调用内所有装备/套装门槛共享同一份快照，不随循环内的重放结果实时变化。
            var aliveBeforeReapplyCache = new Dictionary<Id, bool>();
            bool WasAliveBeforeReapply(Id auraDefId)
            {
                if (_auraQuery == null)
                {
                    return false;
                }

                if (!aliveBeforeReapplyCache.TryGetValue(auraDefId, out var alive))
                {
                    alive = _auraQuery.HasAura(unitId, auraDefId);
                    aliveBeforeReapplyCache[auraDefId] = alive;
                }

                return alive;
            }

            var touchedSetIds = new HashSet<Id>();
            foreach (var kv in slots)
            {
                var instance = kv.Value;
                var template = RequireTemplate(instance.TemplateId);
                ReapplyAuraGrants(unitId, instance, template, WasAliveBeforeReapply);
                if (TryGetSetId(template, out var setId))
                {
                    touchedSetIds.Add(setId);
                }
            }

            foreach (var setId in touchedSetIds)
            {
                ReapplySetBonuses(unitId, setId, WasAliveBeforeReapply);
            }
        }

        /// <summary>见 <see cref="ReapplyGrants"/> 判断记录：对单件装备重放 <c>grants.auras</c>，逐条
        /// 按 <paramref name="wasAliveBeforeReapply"/>（本次 <see cref="ReapplyGrants"/> 调用开始前的
        /// 惰性快照，不是循环期间的实时查询，见 CR150-01 判断记录）判断是否需要真的重新
        /// <c>ApplyAura</c>。</summary>
        private void ReapplyAuraGrants(
            Id unitId, ItemInstance instance, DataRecord template, Func<Id, bool> wasAliveBeforeReapply)
        {
            if (!template.TryGetObject("grants", out var grants) ||
                !grants.TryGetValue("auras", out var aurasRaw) || !(aurasRaw is JsonArray aurasArr) || aurasArr.Count == 0)
            {
                return;
            }

            var key = (unitId, instance.InstanceId);
            _grantedAuras.TryGetValue(key, out var previous);

            var list = new List<(Id, AuraInstanceRef)>();

            foreach (var a in aurasArr)
            {
                if (!(a is JsonString asStr) || !Id.TryParse(asStr.Value, out var auraDefId))
                {
                    continue;
                }

                if (wasAliveBeforeReapply(auraDefId))
                {
                    // 幂等：这个 aura_def 在本次 ReapplyGrants 调用开始前就已经生效——沿用此前记录里
                    // 对应的句柄，不重新 ApplyAura（见 ReapplyGrants 判断记录"不能靠再施加一次反正会
                    // 合并蒙混过去"；用调用前快照而非循环期间实时查询，见 CR150-01 判断记录）。
                    var existing = FindGrantedRef(previous, auraDefId);
                    if (existing.HasValue)
                    {
                        list.Add((auraDefId, existing.Value));
                        continue;
                    }

                    // previous 里没有这一条已知句柄，但 HasAura 已确认生效：大概率是套装门槛加成一类
                    // 别的来源已经把它施加到位（AllowMultiSourceTiming=false 时共享同一实例，见
                    // _auraHandleRefCount 判断记录），本方法不负责这种情况下的句柄归属，跳过，不
                    // 在这里凭空登记一份不属于本装备实例的引用计数。
                    continue;
                }

                var granted = _effectSink.ApplyAura(unitId, auraDefId, instance.InstanceId);
                RegisterAuraHandle(unitId, granted.AuraInstanceId);
                list.Add((auraDefId, granted));
            }

            if (previous != null)
            {
                foreach (var (_, oldRef) in previous)
                {
                    if (!ListContainsHandle(list, oldRef.AuraInstanceId))
                    {
                        // 这个旧句柄没有被本次重放保留：ClearAll 后 AuraHost 那边已经不存在这个实例，
                        // 只需要丢弃本类自己的引用计数簿记，不能（也不需要）调用
                        // ReleaseAuraHandle/IEffectSink.RemoveAura——目标不存在，调用没有意义。
                        _auraHandleLedger.Forget(unitId, oldRef.AuraInstanceId);
                    }
                }
            }

            _grantedAuras[key] = list;
        }

        private static AuraInstanceRef? FindGrantedRef(List<(Id AuraDefId, AuraInstanceRef Ref)>? list, Id auraDefId)
        {
            if (list == null)
            {
                return null;
            }

            foreach (var (defId, r) in list)
            {
                if (defId.Equals(auraDefId))
                {
                    return r;
                }
            }

            return null;
        }

        private static bool ListContainsHandle(List<(Id AuraDefId, AuraInstanceRef Ref)> list, Id auraInstanceId)
        {
            foreach (var (_, r) in list)
            {
                if (r.AuraInstanceId.Equals(auraInstanceId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>见 <see cref="ReapplyGrants"/> 判断记录：<see cref="_appliedSetBonuses"/> 记录的
        /// 是"曾经施加过"，不是"现在还活着"——ClearAll 后其中的 <see cref="AuraInstanceRef"/> 可能已经
        /// 失效但记录本身还在，<see cref="RecomputeSetBonuses"/> 只看这份记录判断 <c>isApplied</c>，
        /// 会误以为不需要重新施加。本方法先按 <see cref="_auraQuery"/> 清掉已经失效的档位记录（不
        /// 调用 <see cref="ReleaseAuraHandle"/>/<see cref="IEffectSink.RemoveAura"/>——那个句柄在光环
        /// 系统内早已不存在），再交由既有的 <see cref="RecomputeSetBonuses"/> 按当前件数重新判定、
        /// 按需重新施加——两者组合起来才是"跨图后重新核实一遍套装光环"的完整语义。
        /// <para>
        /// CR150-01 根治：判断"仍然生效"同样必须用 <paramref name="wasAliveBeforeReapply"/>（本次
        /// <see cref="ReapplyGrants"/> 调用开始前的惰性快照），不能用循环期间的实时 <c>HasAura</c>
        /// 查询——理由与 <see cref="ReapplyAuraGrants"/> 完全对称：套装门槛加成与某件装备的
        /// <c>grants.auras</c> 共享同一个 <c>aura_def</c> 时，装备那一侧的重放（本方法在
        /// <see cref="ReapplyGrants"/> 里恒晚于逐件装备重放执行）可能已经把它变回"生效"，实时查询会
        /// 误判本方法自己这份记录"从未失效"，导致对着一个早已随 <c>ClearAll</c> 死掉的旧句柄调用
        /// <see cref="ReleaseAuraHandle"/>/<c>RemoveAura</c> 或干脆整条记录都不被回收。
        /// </para>
        /// </summary>
        private void ReapplySetBonuses(Id unitId, Id setId, Func<Id, bool> wasAliveBeforeReapply)
        {
            if (_appliedSetBonuses.TryGetValue((unitId, setId), out var applied) &&
                _sets.TryGetValue(setId, out var setRecord))
            {
                var auraRefByThreshold = new Dictionary<int, Id>();
                foreach (var (threshold, auraRef) in ParseSetBonuses(setRecord))
                {
                    auraRefByThreshold[threshold] = auraRef;
                }

                foreach (var threshold in new List<int>(applied.Keys))
                {
                    if (auraRefByThreshold.TryGetValue(threshold, out var auraDefId) && wasAliveBeforeReapply(auraDefId))
                    {
                        // 仍然生效（幂等场景，见 ReapplyGrants 判断记录），保留记录不动。
                        continue;
                    }

                    foreach (var staleRef in applied[threshold])
                    {
                        _auraHandleLedger.Forget(unitId, staleRef.AuraInstanceId);
                    }

                    applied.Remove(threshold);
                }
            }

            RecomputeSetBonuses(unitId, setId);
        }

        // -----------------------------------------------------------------
        // 套装件数门槛（07 第 1.5 节 ItemSet）
        // -----------------------------------------------------------------

        /// <summary>
        /// CORE114 静态候选根治（外部审计 audit-76d16a5-20260910 core-findings.md"静态边界与未列
        /// P2"一节"Equipment set cache"）：判断记录——此前下方"按 <paramref name="bonuses"/>（当前
        /// <c>item.set</c> 定义）逐门槛比较 <c>isApplied</c>"的循环，只能发现"新定义里存在、但施加
        /// 状态需要变化"的门槛，天生看不到"<see cref="_appliedSetBonuses"/> 里已经 applied、但新定义
        /// 里已经不存在这个门槛 key"的情况——<c>item.set</c> reload 把某个门槛删除，或把
        /// <c>count</c> 改到当前装备件数永远达不到的值，都会让旧门槛从新 <paramref name="bonuses"/>
        /// 里消失，循环因此完全不会访问它，句柄永久孤儿（真实探针复现：合法 2 件套装门槛已施加，
        /// reload 删除该门槛后逐件卸下，光环句柄从未被释放，<c>HasAura</c> 持续为 true）。改为先扫一遍
        /// <see cref="_appliedSetBonuses"/> 里已记录、但新 <paramref name="bonuses"/> 里已不存在的
        /// "孤儿门槛"，无条件释放——这与 <see cref="ReapplySetBonuses"/>（跨图重放场景）判断记录里
        /// "先清失效记录、再交给本方法按当前件数重新判定"是同一条原则，只是触发时机不同：那里是
        /// "光环系统内已失效但记录还在"，这里是"记录仍然有效存活、但定义已经不再承认这个门槛"，两者
        /// 都必须在遍历新定义之前先行清理，否则新定义的遍历范围本就覆盖不到它们。
        /// <para>
        /// 已知局限（未在本次根治范围内）：孤儿门槛的清理只在本方法被调用时发生（装备/卸下变化，或
        /// <see cref="ReapplySetBonuses"/> 跨图重放）——<c>item.set</c> reload 那一刻本身不会主动为
        /// 全部已注册单位重算，如果 reload 后玩家迟迟不做任何装备操作，孤儿光环会一直生效到下一次
        /// 装备变化才被发现并释放。这与 <c>EconomyHost</c>/<c>StatHost</c> 等"reload 时立即对账"的
        /// 处理时机不同，是否需要 reload 时主动扫描全部单位属于另一个更大的设计决策（需要遍历全部
        /// 已知单位 × 已知套装，本类型当前不持有"全部已注册单位"的索引），不在本次候选验收范围内。
        /// </para>
        /// </summary>
        private void RecomputeSetBonuses(Id unitId, Id setId)
        {
            if (!_sets.TryGetValue(setId, out var setRecord))
            {
                // 应已被 item.template.set_id 的引用完整性校验拦截。
                return;
            }

            var currentCount = CountEquippedPiecesOfSet(unitId, setId);
            var bonuses = ParseSetBonuses(setRecord);
            var thresholdsInCurrentDefinition = new HashSet<int>();
            foreach (var (threshold, _) in bonuses)
            {
                thresholdsInCurrentDefinition.Add(threshold);
            }

            var key = (unitId, setId);
            if (!_appliedSetBonuses.TryGetValue(key, out var applied))
            {
                applied = new Dictionary<int, List<AuraInstanceRef>>();
                _appliedSetBonuses[key] = applied;
            }

            foreach (var staleThreshold in new List<int>(applied.Keys))
            {
                if (thresholdsInCurrentDefinition.Contains(staleThreshold))
                {
                    continue;
                }

                foreach (var staleRef in applied[staleThreshold])
                {
                    ReleaseAuraHandle(unitId, staleRef);
                }

                applied.Remove(staleThreshold);
            }

            foreach (var (threshold, auraRef) in bonuses)
            {
                var isApplied = applied.ContainsKey(threshold);
                if (currentCount >= threshold && !isApplied)
                {
                    var handle = _effectSink.ApplyAura(unitId, auraRef, setId);
                    // R03 收边补齐：套装门槛加成与装备 grants.auras（见 ApplyGrants/RevertGrants）
                    // 统一经 RegisterAuraHandle/ReleaseAuraHandle 记账——AllowMultiSourceTiming=false
                    // 时两者对同一个 aura_def 的施加会在 AuraHost 内合并成同一份实例句柄，这里不再
                    // 各自为政，才能保证任一侧卸载/降级时都不会误删另一侧仍需要的共享光环（见该方法
                    // 判断记录）。
                    RegisterAuraHandle(unitId, handle.AuraInstanceId);
                    applied[threshold] = new List<AuraInstanceRef> { handle };
                }
                else if (currentCount < threshold && isApplied)
                {
                    foreach (var r in applied[threshold])
                    {
                        ReleaseAuraHandle(unitId, r);
                    }

                    applied.Remove(threshold);
                }
            }
        }

        private int CountEquippedPiecesOfSet(Id unitId, Id setId)
        {
            if (!_equipped.TryGetValue(unitId, out var slots))
            {
                return 0;
            }

            var count = 0;
            foreach (var instance in slots.Values)
            {
                var template = RequireTemplate(instance.TemplateId);
                if (TryGetSetId(template, out var sid) && sid.Equals(setId))
                {
                    count++;
                }
            }

            return count;
        }

        private static List<(int Count, Id AuraRef)> ParseSetBonuses(DataRecord setRecord)
        {
            var result = new List<(int, Id)>();
            foreach (var raw in setRecord.GetArray("bonuses"))
            {
                if (!(raw is JsonObject obj))
                {
                    continue;
                }

                var count = (int)GetNumber(obj, "count", 0);
                var auraRef = RequireId(obj, "aura_ref");
                result.Add((count, auraRef));
            }

            return result;
        }

        // -----------------------------------------------------------------
        // 小工具
        // -----------------------------------------------------------------

        private Dictionary<Id, ItemInstance> GetOrCreateUnitSlots(Id unitId)
        {
            if (!_equipped.TryGetValue(unitId, out var slots))
            {
                slots = new Dictionary<Id, ItemInstance>();
                _equipped[unitId] = slots;
            }

            return slots;
        }

        private DataRecord RequireTemplate(Id templateId)
        {
            var template = _inventory.GetTemplate(templateId);
            if (template == null)
            {
                throw new InvalidOperationException(
                    $"物品模板 \"{templateId}\" 不存在（应已通过 item.template 的引用完整性校验）");
            }

            return template;
        }

        /// <summary>该槽位是否为真正的装备位（见 ItemSchemas.SlotDefinition.is_equipment 判断
        /// 记录，缺省 true）。未登记的槽位 id（理论上不会发生——Equip 前已经过 SlotMatches，requested
        /// slot 必然是某个已登记的 item.slot_definition）按 true 处理，与字段缺省语义一致。</summary>
        private bool IsEquipmentSlot(Id slot)
        {
            return !_slotDefinitions.TryGetValue(slot, out var slotDef) ||
                !slotDef.TryGetBool("is_equipment", out var value) || value;
        }

        private bool SlotMatches(Id templateSlot, Id requestedSlot)
        {
            if (templateSlot.Equals(requestedSlot))
            {
                return true;
            }

            if (_slotDefinitions.TryGetValue(requestedSlot, out var slotDef) &&
                slotDef.TryGetIdList("accepts", out var accepts))
            {
                foreach (var a in accepts)
                {
                    if (a.Equals(templateSlot))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// T-N2-5（ADR-0032 决策 5；07 第 1.1/1.2 节修订段"<c>requirements.level</c> 未填时按
        /// <c>item.req_level_curve</c> 由 <c>item_level</c> 反推，填了以手填为准"）：优先读模板显式
        /// 填写的 <c>requirements.level</c>；未填时按 <see cref="ItemOptions.ReqLevelCurveId"/> 指向
        /// 的曲线反推。
        /// <para>
        /// 判断记录（取整规则——设计层裁定（2026-09-15）：采纳）：ADR-0032 决策 5、07 第 1.1/1.2 节修订段均只给
        /// "由此反推"一句，未指明反推出的非整数需求等级如何取整为 <see cref="int"/>。本方法按
        /// <see cref="Math.Ceiling"/>（向上取整）——"需求等级"是穿戴门槛，宁可让门槛略严（差 0.x 级
        /// 也算未达标）也不放宽，同预算/护甲/需求等级三条曲线"越界夹取到端点"这一既有口径里"选择更
        /// 保守近似"的思路一致；若设计层确认应改为 <c>Math.Round</c>/向下取整，只需改本方法这一处。
        /// </para>
        /// <para>
        /// 判断记录（曲线缺失时的行为）：<see cref="ItemOptions.ReqLevelCurveId"/> 在已加载的
        /// <c>item.req_level_curve</c> 里找不到对应记录时，按"无等级限制"处理（<paramref
        /// name="level"/> 输出 0，返回 <c>false</c>）——与未登记 <c>requirements</c> 字段时的既有
        /// 行为一致，不抛异常（同 <see cref="ApplyArmorValue"/> 护甲曲线缺失的既有口径）。曲线求值
        /// 结果 &lt;= 0 同样按"无等级限制"处理（负的/零的需求等级没有实际门槛意义）。
        /// </para>
        /// </summary>
        private bool TryGetRequiredLevel(DataRecord template, out int level)
        {
            if (template.TryGetObject("requirements", out var req) &&
                req.TryGetValue("level", out var v) && v is JsonNumber n)
            {
                level = (int)n.Value;
                return true;
            }

            var curveRecord = _registry.Get("item.req_level_curve", _options.ReqLevelCurveId);
            if (curveRecord == null)
            {
                level = 0;
                return false;
            }

            var curve = CurveSchema.ReadBreakpoints(curveRecord, "entries");
            var itemLevel = (int)template.GetInt("item_level");
            var reqLevel = curve.Evaluate(itemLevel);
            if (reqLevel <= 0.0)
            {
                level = 0;
                return false;
            }

            level = (int)Math.Ceiling(reqLevel);
            return true;
        }

        private static bool TryGetSetId(DataRecord template, out Id setId)
        {
            if (template.TryGetString("set_id", out var s) && Id.TryParse(s, out setId))
            {
                return true;
            }

            setId = default;
            return false;
        }

        private static Id RequireId(JsonObject o, string key)
        {
            if (o.TryGetValue(key, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }

            throw new ArgumentException($"字段 \"{key}\" 缺失或不是合法 Id");
        }

        private static Id? GetIdOpt(JsonObject o, string key) =>
            o.TryGetValue(key, out var v) && v is JsonString s && Id.TryParse(s.Value, out var id)
                ? id
                : (Id?)null;

        private static string GetString(JsonObject o, string key, string fallback) =>
            o.TryGetValue(key, out var v) && v is JsonString s ? s.Value : fallback;

        private static double GetNumber(JsonObject o, string key, double fallback) =>
            o.TryGetValue(key, out var v) && v is JsonNumber n ? n.Value : fallback;

        private static StatModifierOp ParseOp(string text) => text switch
        {
            "flat" => StatModifierOp.Flat,
            "pct" => StatModifierOp.Pct,
            "mult" => StatModifierOp.Mult,
            _ => throw new ArgumentException($"未知的 stats.op \"{text}\"，应为 flat|pct|mult"),
        };
    }
}
