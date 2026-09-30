using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Rules.Ai;
using Core.Rules.Common;
using Core.Rules.Skill;
using Core.Rules.Targeting;
using Xunit;

namespace Tests.Rules.Ai
{
    /// <summary>
    /// D11（测试覆盖梳理 2026-10-01；设计决定，见 ADR-0125）：<see cref="RotationEvaluator"/> 在构造期
    /// 一次性编译 <c>ai.rotation</c>，数据注册表热重载（<see cref="IDataRegistry.Reload"/>）后已有实例不会
    /// 自动失效或重新编译——需要调用方重建实例。本文件钉住这一现行为：热重载后旧实例仍按旧编译结果
    /// 选技能，重建实例后才读到新版本。
    /// </summary>
    public sealed class RotationEvaluatorHotReloadTests
    {
        private static readonly Id Unit = new Id("unit.hot_reload_caster");
        private static readonly Id RotationId = new Id("ai.rotation.hot_reload");
        private static readonly Id OldSkill = new Id("skill.hot_reload_old");
        private static readonly Id NewSkill = new Id("skill.hot_reload_new");

        private static string RotationRows(Id skill) =>
            "[{ \"id\": \"" + RotationId.Value + "\", \"entries\": [" +
            "{ \"priority\": 1, \"condition\": \"true\", \"skill_id\": \"" + skill.Value + "\" } ] }]";

        private static string SkillDefRows() =>
            "[" + SkillDefRow(OldSkill) + "," + SkillDefRow(NewSkill) + "]";

        private static string SkillDefRow(Id id) =>
            "{\"id\": \"" + id.Value + "\", \"school\": \"skill.school.ai_test\", \"kind\": \"active\"," +
            " \"range\": 0, \"cast_time\": 0, \"respects_gcd\": true," +
            " \"target_shape_ref\": \"target.ai_test\", \"effects\": []}";

        /// <summary><c>ai.rotation</c> 表文本可在加载后替换的数据源（模拟开发期改了数据文件）。</summary>
        private sealed class MutableRotationSource : IDataSource
        {
            private readonly List<DataTableSource> _tables = new List<DataTableSource>();

            public string RotationJson;

            public MutableRotationSource(string initialRotationJson)
            {
                RotationJson = initialRotationJson;
                _tables.Add(new DataTableSource(AiSchemas.BehaviorProfile.Name, "memory://profile",
                    () => AiTestSupport.Envelope(AiSchemas.BehaviorProfile.Name, "[]")));
                _tables.Add(new DataTableSource(AiSchemas.Rotation.Name, "memory://rotation",
                    () => AiTestSupport.Envelope(AiSchemas.Rotation.Name, RotationJson)));
                _tables.Add(new DataTableSource(AiSchemas.PatrolPath.Name, "memory://patrol",
                    () => AiTestSupport.Envelope(AiSchemas.PatrolPath.Name, "[]")));
                _tables.Add(new DataTableSource("skill.def", "memory://skill_def",
                    () => AiTestSupport.Envelope("skill.def", SkillDefRows())));
                _tables.Add(new DataTableSource("target.chain_def", "memory://chain_def",
                    () => AiTestSupport.Envelope("target.chain_def", "[]")));
            }

            public IReadOnlyList<DataTableSource> ListTables() => _tables;
        }

        [Fact]
        public void Reload_ExistingEvaluatorKeepsOldCompilation_RebuiltEvaluatorSeesNewVersion()
        {
            var bus = AiTestSupport.CreateBus();
            var source = new MutableRotationSource(RotationRows(OldSkill));
            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(AiSchemas.BehaviorProfile);
            registry.RegisterSchema(AiSchemas.Rotation);
            registry.RegisterSchema(AiSchemas.PatrolPath);
            registry.RegisterSchema(SkillSchemas.Def);
            registry.RegisterSchema(TargetSchemas.ChainDef);
            Assert.False(registry.LoadAll().IsBlocking);

            var skills = new FakeSkillHost();
            var exprFactory = new FakeExprHostFactory();
            var original = new RotationEvaluator(registry, skills, exprFactory);
            Assert.Equal(OldSkill, original.Evaluate(Unit, RotationId, targetId: null)!.Value.SkillId);

            // 热重载：数据换成指向新技能的版本，注册表里的记录确实变了。
            source.RotationJson = RotationRows(NewSkill);
            var report = registry.Reload(AiSchemas.Rotation.Name);
            Assert.Equal(0, report.ErrorCount);
            var reloaded = registry.Get(AiSchemas.Rotation.Name, RotationId.Value);
            Assert.NotNull(reloaded);
            var reloadedEntry = (JsonObject)reloaded!.GetArray("entries")[0];
            Assert.Equal(NewSkill.Value, ((JsonString)reloadedEntry["skill_id"]).Value);

            // 设计决定：已有实例不自动重编译，仍然按旧编译结果选技能。
            skills.Calls.Clear();
            var stale = original.Evaluate(Unit, RotationId, targetId: null);
            Assert.Equal(OldSkill, stale!.Value.SkillId);
            Assert.Equal(OldSkill, Assert.Single(skills.Calls).skillId);

            // 重建实例后才读到新版本。
            var rebuilt = new RotationEvaluator(registry, skills, exprFactory);
            skills.Calls.Clear();
            var fresh = rebuilt.Evaluate(Unit, RotationId, targetId: null);
            Assert.Equal(NewSkill, fresh!.Value.SkillId);
            Assert.Equal(NewSkill, Assert.Single(skills.Calls).skillId);
        }
    }
}
