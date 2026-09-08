using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Rules.Common
{
    /// <summary>
    /// CORE-170-01 根治（architecture/落地计划/audit-8160178-20260908，P2）：跨来源（装备
    /// <c>grants.auras</c>、套装门槛加成、种族/职业被动）共享同一光环实例句柄的引用计数账本。
    /// 只依赖 <see cref="IEffectSink"/>/<see cref="IAuraQuery"/> 两个契约（不依赖
    /// <c>core/rules/skill</c> 任何具体实现类型），因此可以安全放在 <c>core/rules/common/contracts</c>
    /// ——供 <c>core/rules</c>（<c>RulesAssembly</c>，种族/职业被动来源）与 <c>core/carriers/item</c>
    /// （<see cref="Core.Carriers.Item.EquipmentHost"/>，装备/套装来源，只经契约类型依赖 <c>rules</c>
    /// 层，不直接引用 <c>core/rules/skill</c> 具体实现，见该类型既有判断记录"契约缺口用具名委托/
    /// 契约接口绕过"同一惯例）共同持有同一实例，不需要为共享这一个工具类而让 carriers 反向依赖
    /// <c>core/rules/skill</c> 的实现命名空间。
    /// <para>
    /// 背景——<c>AllowMultiSourceTiming == false</c>（默认，见 <c>core/rules/skill</c>
    /// <c>SkillOptions</c>）时，<c>AuraHost.ApplyAura</c> 对同一 <c>(target, aura_def)</c> 的多次施加
    /// 会合并到<b>同一个</b>实例句柄（按 <c>(targetId, defId, null)</c> 槽位聚合，见
    /// <c>AuraHost.ApplyAura</c> 判断记录），且 <c>AuraHost.RemoveAura</c> 本身不做引用计数——谁调用
    /// <c>RemoveAura</c> 谁就把实例整个摘除，不管还有没有别的来源仍然"挂着"这份共享光环。此前只有
    /// <see cref="Core.Carriers.Item.EquipmentHost"/> 自己维护一份私有的按 <c>(unitId,
    /// AuraInstanceId)</c> 计数（装备 <c>grants.auras</c> 与套装门槛加成两处共用），种族/职业被动光环
    /// （<c>RulesAssembly.ReapplyRacePassiveAuras</c>/<c>ArchetypeRegistry.ApplyTo</c>）完全不参与这份
    /// 计数、只按"<c>HasAura</c> 已经为 true 就跳过"判断是否需要重新施加——装备与种族被动共享同一
    /// <c>aura_def</c> 时，若装备先重放（见 <c>GameplayAssembly.EnterMap</c> 调用顺序）创建了共享
    /// 实例并把计数记成 1，种族重放看到 <c>HasAura=true</c> 直接跳过、从未为自己登记一份引用；随后
    /// 卸下装备释放这唯一一份引用、计数归零，触发 <see cref="IEffectSink.RemoveAura"/> 把种族仍然
    /// 依赖的共享实例整个删除——种族属性修正（走独立的 <c>StatModifierWriter</c> 登记表，生命周期与
    /// 光环无关）没有丢，但种族被动光环本身（buff 状态、触发效果）随装备一起消失。
    /// </para>
    /// <para>
    /// 根治方式：把这份引用计数从 <see cref="Core.Carriers.Item.EquipmentHost"/> 私有字段上移到本类，
    /// 由 <c>RulesAssembly</c> 持有单一实例，装备（经 <c>CarriersAssembly</c> 注入）与种族（
    /// <c>RulesAssembly</c> 自己）共享同一份账本——任何一类来源"新持有一份引用"调用
    /// <see cref="Register"/>，"释放自己的引用"调用 <see cref="Release"/>（只有全部来源都释放完毕、
    /// 计数归零才真正调用 <see cref="IEffectSink.RemoveAura"/>）。三类来源各自仍然维护"我给这个
    /// <c>aura_def</c>/这件装备实例登记过哪个句柄"的私有簿记（<c>EquipmentHost._grantedAuras</c>/
    /// <c>_appliedSetBonuses</c>、<c>RulesAssembly._raceAuraHandles</c>），本类只负责跨来源共享的
    /// 计数与"计数归零时真正摘除"这一件事，不替代任何一方自己的簿记。
    /// </para>
    /// <para>
    /// <c>StackOverflowPolicy.Replace</c> 换句柄（见 <see cref="IAuraQuery.InstanceReplaced"/> 判断
    /// 记录）时，旧句柄名下的计数需要原样搬到新句柄名下——本类在构造时直接订阅 <paramref
    /// name="auraQuery"/> 的 <c>InstanceReplaced</c> 自行完成这次迁移（<see
    /// cref="OnInstanceReplaced"/>），不要求 <see cref="Register"/>/<see cref="Release"/> 的调用方
    /// 各自处理；调用方（各自的私有簿记）仍需要单独订阅同一事件以更新自己记录的句柄指向哪个实例，
    /// 这与本类的计数迁移是两件独立的事，互不代劳。
    /// </para>
    /// </summary>
    public sealed class AuraHandleLedger
    {
        private readonly IEffectSink _effectSink;
        private readonly Dictionary<(Id UnitId, Id AuraInstanceId), int> _refCount =
            new Dictionary<(Id, Id), int>();

        /// <param name="auraQuery">
        /// 可选（惯例同本模块其它可选依赖）：非 null 时订阅 <see cref="IAuraQuery.InstanceReplaced"/>
        /// 自动迁移换句柄后的计数（见类型注释）；为 null（多数测试用的最小假实现，不触发
        /// <c>StackOverflowPolicy.Replace</c>）时退化为不迁移，不影响未触发换句柄场景下的计数正确性。
        /// </param>
        public AuraHandleLedger(IEffectSink effectSink, IAuraQuery? auraQuery = null)
        {
            _effectSink = effectSink ?? throw new ArgumentNullException(nameof(effectSink));
            if (auraQuery != null)
            {
                auraQuery.InstanceReplaced += OnInstanceReplaced;
            }
        }

        /// <summary>登记"多了一个来源持有 <paramref name="auraInstanceId"/> 这份引用"。</summary>
        public void Register(Id unitId, Id auraInstanceId)
        {
            var key = (unitId, auraInstanceId);
            _refCount[key] = _refCount.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        /// <summary>释放一个来源持有的引用；仍有其它来源持有同一句柄时只递减计数，真正归零时才调用
        /// <see cref="IEffectSink.RemoveAura"/>。</summary>
        public void Release(Id unitId, AuraInstanceRef auraRef)
        {
            var key = (unitId, auraRef.AuraInstanceId);
            var remaining = _refCount.TryGetValue(key, out var count) ? count - 1 : 0;

            if (remaining > 0)
            {
                _refCount[key] = remaining;
                return;
            }

            _refCount.Remove(key);
            _effectSink.RemoveAura(unitId, auraRef);
        }

        /// <summary>丢弃一个已知不再有效的句柄的簿记（典型如跨图 <c>World.ClearAll</c> 之后，
        /// <c>AuraHost</c> 已经不认识这个实例）——不递减计数触发 <see cref="IEffectSink.RemoveAura"/>，
        /// 目标不存在，调用没有意义，只清掉本类自己的引用计数记录。</summary>
        public void Forget(Id unitId, Id auraInstanceId) => _refCount.Remove((unitId, auraInstanceId));

        private void OnInstanceReplaced(Id targetId, Id defId, Id oldInstanceId, Id newInstanceId)
        {
            var oldKey = (targetId, oldInstanceId);
            if (!_refCount.TryGetValue(oldKey, out var migratingCount))
            {
                return;
            }

            _refCount.Remove(oldKey);
            var newKey = (targetId, newInstanceId);
            _refCount[newKey] = _refCount.TryGetValue(newKey, out var existingCount)
                ? existingCount + migratingCount
                : migratingCount;
        }
    }
}
