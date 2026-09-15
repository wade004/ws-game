using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Core.Rules.ExprHost;

namespace Core.Rules.Ai
{
    /// <summary>
    /// <see cref="IRotationEvaluator"/> 默认实现（见该接口判断记录、ADR-0031 决策 12、ADR-0035 决策
    /// 2）。T-N3-10：从 <c>AiHost.Evaluate</c>/<c>AiHost.LoadRotations</c>/
    /// <c>AiHost.ClassifyIsHostileSingleTarget</c> 原样抽出的优先级表求值逻辑，独立成不依赖
    /// <c>ai.behavior_profile</c>、不要求 <see cref="BehaviorState.Combat"/> 的组件——<see
    /// cref="AiHost"/> 现在只在 <see cref="BehaviorState.Combat"/> 态委托本组件（见 <see
    /// cref="AiHost.Evaluate(Id)"/>），行为档/战斗态门控仍留在 <see cref="AiHost"/> 里，本组件自身
    /// 不检查、也不知道调用方是否处于战斗；玩家单位"一键智能释放"可以直接 <c>new
    /// RotationEvaluator(...)</c> 复用，不需要经过 <see cref="AiHost"/>/<c>RegisterUnit</c>。
    /// <para>
    /// 无状态保证：构造期一次性从 <see cref="IDataRegistryView"/> 读取并编译全部 <c>ai.rotation</c>
    /// 记录到 <see cref="_rotations"/>（<c>CompiledRotationEntry</c>：按 <c>priority</c> 降序排序、
    /// <c>condition</c> Expr 文本解析一次、"敌对单体"分类一次，判据与判断记录逐字保留自原
    /// <c>AiHost.ClassifyIsHostileSingleTarget</c>），此后只读——这是"内容编译缓存"，不是"单位状态"：
    /// 不随传入哪个 <c>unitId</c> 而变化，多个单位共享同一份只读编译结果，与迁移前 <c>AiHost</c> 原
    /// <c>_rotations</c> 字段同一性质。<see cref="Evaluate"/> 本身不写任何实例字段，每次调用只读入参
    /// （<c>unitId</c>/<c>rotationId</c>/<c>targetId</c>）与上述构造期只读缓存，满足本任务硬性规则
    /// "禁止求值组件持有单位状态"。
    /// </para>
    /// <para>
    /// 已知限制（与迁移前 <c>AiHost.LoadRotations</c> 同一限制，非本任务新引入）：构造完成后若
    /// <see cref="IDataRegistry.Reload"/>（开发期热重载）发生，<see cref="_rotations"/> 不会自动失效/
    /// 重新编译——调用方需要整体重建一个新的 <see cref="RotationEvaluator"/> 实例。集成任务如需支持
    /// 热重载下的即时生效，需要订阅 reload 事件并重新编译，本任务不新增该能力。
    /// </para>
    /// <para>
    /// 就绪判定（<see cref="ISkillHost.GetSkillReadiness"/>）先于 <see cref="ISkillHost.CastSkill"/>：
    /// 候选条件为真后，先查一次只读的 <see cref="SkillReadiness.IsReady"/>（覆盖冷却/充能/公共冷却，
    /// T-N3-4 起还覆盖 <see cref="SkillReadinessBlockers.ConditionNotMet"/>/<see
    /// cref="SkillReadinessBlockers.ActionLocked"/>，见该方法契约文档），不就绪直接跳过、不产生一次
    /// 注定失败的 <c>CastSkill</c> 调用（及其 <c>skill.cast_failed</c> 事件）——不自行用
    /// <c>IsCasting</c>/<c>GetCooldown</c> 等零散查询推断就绪与否，统一走这一个只读快照。就绪不等于
    /// 一定能成功（<see cref="ISkillHost.GetSkillReadiness"/> 不覆盖存活/控制、学派锁定、资源、目标
    /// 合法性、距离与视线），因此真正的最终裁决仍是随后的 <see cref="ISkillHost.CastSkill"/>——其
    /// 失败一样会被本方法当作"跳过、继续看下一条"处理，与迁移前完全一致（见既有
    /// <c>AiRotationTests.Evaluate_OnCooldownEntry_FallsBackToNextPriority</c> 等用例，测试假实现
    /// <c>FakeSkillHost</c> 未编程 <c>GetSkillReadiness</c> 时按 <see cref="ISkillHost"/> 默认接口
    /// 实现恒就绪，因此该既有用例仍然会真的调用一次 <c>CastSkill</c> 才发现失败，行为逐位不变）。
    /// </para>
    /// </summary>
    public sealed class RotationEvaluator : IRotationEvaluator
    {
        private readonly ISkillHost _skillHost;
        private readonly IExprHostFactory _exprHostFactory;
        private readonly IExprSchema _exprSchema;
        private readonly IExprDiagnostics _exprDiagnostics;

        /// <summary>构造期一次性编译的只读内容缓存，见类型判断记录"无状态保证"——不是单位状态。</summary>
        private readonly Dictionary<string, IReadOnlyList<CompiledRotationEntry>> _rotations =
            new Dictionary<string, IReadOnlyList<CompiledRotationEntry>>(StringComparer.Ordinal);

        public RotationEvaluator(
            IDataRegistryView registry,
            ISkillHost skillHost,
            IExprHostFactory exprHostFactory,
            IExprSchema? exprSchema = null,
            IExprDiagnostics? exprDiagnostics = null)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            _skillHost = skillHost ?? throw new ArgumentNullException(nameof(skillHost));
            _exprHostFactory = exprHostFactory ?? throw new ArgumentNullException(nameof(exprHostFactory));
            _exprSchema = exprSchema ?? RulesExprSchema.Base;
            _exprDiagnostics = exprDiagnostics ?? new ExprDiagnosticsRecorder();

            LoadRotations(registry);
        }

        public bool HasRotation(Id rotationId) => _rotations.ContainsKey(rotationId.Value);

        public SkillCastRequest? Evaluate(Id unitId, Id rotationId, Id? targetId)
        {
            if (!_rotations.TryGetValue(rotationId.Value, out var entries))
            {
                return null;
            }

            foreach (var entry in entries)
            {
                var host = _exprHostFactory.CreateFor(unitId, targetId, null);
                if (!ExprEvaluator.EvaluateBool(entry.Condition, host, _exprDiagnostics))
                {
                    continue;
                }

                // 就绪判定复用 ISkillHost.GetSkillReadiness，不自行推断，见类型判断记录"就绪判定"。
                if (!_skillHost.GetSkillReadiness(unitId, entry.SkillId).IsReady)
                {
                    continue;
                }

                // RC-10 收边同一判据（原 AiHost.Evaluate 判断记录）：只有"敌对单体"类技能才把调用方
                // 追踪的目标强塞给 CastSkill；自疗/友疗/AOE/未知一律传空目标数组，交技能自己的
                // target_shape_ref 目标链解析。
                var targets = entry.IsHostileSingleTarget && targetId.HasValue
                    ? new[] { targetId.Value }
                    : Array.Empty<Id>();

                var result = _skillHost.CastSkill(unitId, entry.SkillId, targets);
                if (result.Success)
                {
                    return new SkillCastRequest(unitId, entry.SkillId, targets);
                }
            }

            return null;
        }

        // -----------------------------------------------------------------
        // 数据加载（原样搬迁自 AiHost.LoadRotations/AiHost.ClassifyIsHostileSingleTarget）
        // -----------------------------------------------------------------

        private void LoadRotations(IDataRegistryView registry)
        {
            foreach (var record in registry.GetAll(AiSchemas.Rotation.Name))
            {
                var id = record.GetId("id");
                var entriesArray = record.GetArray("entries");
                var compiled = new List<CompiledRotationEntry>(entriesArray.Count);

                foreach (var item in entriesArray)
                {
                    var obj = (JsonObject)item;
                    var priority = (int)((JsonNumber)obj["priority"]).Value;
                    var conditionText = ((JsonString)obj["condition"]).Value;
                    var skillId = new Id(((JsonString)obj["skill_id"]).Value);
                    var node = ExprParser.Parse(conditionText, _exprSchema);
                    var isHostileSingleTarget = ClassifyIsHostileSingleTarget(registry, skillId);
                    compiled.Add(new CompiledRotationEntry(priority, node, skillId, isHostileSingleTarget));
                }

                compiled.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                _rotations[id.Value] = compiled;
            }
        }

        /// <summary>
        /// 原样搬迁自 <c>AiHost.ClassifyIsHostileSingleTarget</c>（RC-10 收边补齐，判据逐字保留）：
        /// 判断 <paramref name="skillId"/> 的目标类型是否为"敌对单体"——直接读 <c>skill.def.target_shape_ref</c>
        /// 指向的 <c>target.chain_def</c> 记录本身做只读探测两个字段，不经过 <c>core/rules/skill</c>/
        /// <c>core/rules/targeting</c> 任何具体类型（本模块只持有 <see cref="ISkillHost"/> 这个共享
        /// L2 契约）。判定标准：<c>filters</c> 含 <c>"relation:hostile"</c>、不含
        /// <c>"relation:friendly"</c>，且 <c>max_targets</c>（缺省 1）恰为 1。技能未登记、
        /// <c>target_shape_ref</c> 缺失、链未登记、字段解析异常等任何"读不出"的情况一律保守返回
        /// false（交目标链自行解析，不强塞）。
        /// </summary>
        private static bool ClassifyIsHostileSingleTarget(IDataRegistryView registry, Id skillId)
        {
            var skillRecord = registry.Get("skill.def", skillId);
            if (skillRecord == null || !skillRecord.TryGetId("target_shape_ref", out var chainId))
            {
                return false;
            }

            var chainRecord = registry.Get("target.chain_def", chainId);
            if (chainRecord == null)
            {
                return false;
            }

            var maxTargets = 1L;
            if (chainRecord.TryGetInt("max_targets", out var explicitMaxTargets))
            {
                maxTargets = explicitMaxTargets;
            }

            if (maxTargets != 1)
            {
                return false;
            }

            if (!chainRecord.TryGetArray("filters", out var filters))
            {
                return false;
            }

            var hasHostile = false;
            for (var i = 0; i < filters.Count; i++)
            {
                if (!(filters[i] is JsonString filterStr))
                {
                    continue;
                }

                if (filterStr.Value == "relation:friendly")
                {
                    return false;
                }

                if (filterStr.Value == "relation:hostile")
                {
                    hasHostile = true;
                }
            }

            return hasHostile;
        }
    }
}
