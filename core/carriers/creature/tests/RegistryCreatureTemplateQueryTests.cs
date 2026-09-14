using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Carriers.Creature
{
    /// <summary>
    /// 消费方反馈第 44 条根治：<see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery"/>
    /// 单元测试——直接从已加载的 <see cref="IDataRegistryView"/> 现读现解析
    /// <c>creature.template</c> 记录，不像 <see cref="Core.Carriers.Creature.CreatureFactory"/> 那样
    /// 预先解析出完整索引。覆盖：命中/未命中两个分支、<see cref="Core.Carriers.Creature.NpcFlag"/>
    /// 判定、以及字段非法时"包成 <see cref="System.ArgumentException"/> 而不是让
    /// <see cref="DataFieldException"/> 原样冒泡"的异常收敛判断记录（见该类型类型级注释）。
    /// </summary>
    public class RegistryCreatureTemplateQueryTests
    {
        [Fact]
        public void Constructor_NullView_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new Core.Carriers.Creature.RegistryCreatureTemplateQuery(null!));
        }

        [Fact]
        public void Get_RegisteredId_ReturnsMatchingTemplate()
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);

            var query = new Core.Carriers.Creature.RegistryCreatureTemplateQuery(registry);
            var template = query.Get(new Id("creature.sample_elite"));

            Assert.Equal(new Id("creature.sample_elite"), template.Id);
            Assert.Equal(3, template.Level);
        }

        [Fact]
        public void Get_UnregisteredId_ThrowsArgumentException()
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);

            var query = new Core.Carriers.Creature.RegistryCreatureTemplateQuery(registry);

            Assert.Throws<System.ArgumentException>(
                () => query.Get(new Id("creature.sample_does_not_exist")));
        }

        [Fact]
        public void HasFlag_MatchesTemplateNpcFlags()
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);

            // creature.sample_elite 登记了 npc_flag.vendor/npc_flag.questgiver（见
            // CreatureTestSupport.TemplateRows），不含 npc_flag.summon_only。
            var query = new Core.Carriers.Creature.RegistryCreatureTemplateQuery(registry);

            Assert.True(query.HasFlag(new Id("creature.sample_elite"), Core.Carriers.Creature.NpcFlag.Vendor));
            Assert.True(query.HasFlag(new Id("creature.sample_elite"), Core.Carriers.Creature.NpcFlag.Questgiver));
            Assert.False(query.HasFlag(new Id("creature.sample_elite"), Core.Carriers.Creature.NpcFlag.SummonOnly));

            // creature.sample_basic 未登记 npc_flags（可选字段缺省）。
            Assert.False(query.HasFlag(new Id("creature.sample_basic"), Core.Carriers.Creature.NpcFlag.SummonOnly));
        }

        [Fact]
        public void HasFlag_UnregisteredId_ThrowsArgumentException()
        {
            var bus = CreatureTestSupport.CreateBus();
            var registry = CreatureTestSupport.MakeRegistry(bus);

            var query = new Core.Carriers.Creature.RegistryCreatureTemplateQuery(registry);

            Assert.Throws<System.ArgumentException>(
                () => query.HasFlag(new Id("creature.sample_does_not_exist"), Core.Carriers.Creature.NpcFlag.SummonOnly));
        }

        /// <summary>只登记一条 <c>npc_flags</c> 含未登记职能标志的原始记录的最小
        /// <see cref="IDataRegistryView"/> 假实现（惯例同
        /// <c>CreatureTestSupport.UnknownTierRegistryView</c>：绕过 <see cref="DataRegistry"/> 本身
        /// 的加载/阻断态语义，只用来构造"字段级校验本该拦但这里故意绕过"的非法记录，验证
        /// <see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery"/> 自身对
        /// <see cref="Core.Carriers.Creature.CreatureTemplate.FromRecord"/> 抛出的
        /// <see cref="DataFieldException"/> 的收敛处理，不掺杂 <see cref="DataRegistry"/> 阻断态下
        /// <c>Get</c>/<c>GetAll</c> 本身会抛 <see cref="InvalidOperationException"/> 这另一条独立
        /// 逻辑——后者只在"LoadAll 已经跑完、报告已阻断"之后的独立读取才会触发，
        /// <see cref="Core.Gameplay.Spawn.SpawnSummonOnlyCreatureRule.Validate"/> 是在 LoadAll
        /// 校验期间（<c>_blocked</c> 尚未定型）被调用，不会遇到那种情形，本用例只需要单独验证
        /// <see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery"/> 这一层的异常收敛。</summary>
        private sealed class IllegalNpcFlagRegistryView : IDataRegistryView
        {
            private readonly List<DataRecord> _templates = new List<DataRecord>();

            public IllegalNpcFlagRegistryView()
            {
                var json = "{\"id\": \"creature.sample_bad_flag\", \"name_key\": \"l10n.creature.sample_bad_flag.name\", " +
                    "\"level\": 1, \"tier\": \"creature.tier.normal\", " +
                    "\"base_stats\": {\"stat.power\": 1}, \"faction_id\": \"fac.test_monster\", " +
                    "\"npc_flags\": [\"npc_flag.unknown_flag\"], \"display_ref\": \"display.sample\"}";
                var obj = (JsonObject)JsonReader.Parse(json);
                var id = new Id("creature.sample_bad_flag");
                _templates.Add(new DataRecord(
                    Core.Carriers.Creature.CreatureSchemas.Template, id.Value, id, obj));
            }

            public DataRecord? Get(string table, string key) =>
                table == Core.Carriers.Creature.CreatureSchemas.Template.Name
                    ? _templates.Find(r => r.Key == key)
                    : null;

            public DataRecord? Get(string table, Id id) => Get(table, id.Value);

            public IReadOnlyList<DataRecord> GetAll(string table) =>
                table == Core.Carriers.Creature.CreatureSchemas.Template.Name
                    ? _templates
                    : Array.Empty<DataRecord>();

            public IReadOnlyList<DataRecord> Query(string table, Core.Foundation.Expr.ExprNode predicate) =>
                Array.Empty<DataRecord>();

            public IReadOnlyList<DataRecord> Query(string table, string predicateText) =>
                Array.Empty<DataRecord>();

            public IReadOnlyList<string> Tables => Array.Empty<string>();

            public TableSchema? GetSchema(string table) => null;
        }

        /// <summary>消费方反馈第 44 条根治验收要求"模板 npc_flags 非法值时不因本规则抛异常"的下层
        /// 依据：<c>npc_flags</c> 含未登记职能标志时，<see cref="Core.Carriers.Creature.CreatureTemplate.FromRecord"/>
        /// 抛 <see cref="DataFieldException"/>；本类型的
        /// <see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery.Get"/> 必须把它包成
        /// <see cref="System.ArgumentException"/>（<c>InnerException</c> 保留原始异常），不能让
        /// <see cref="DataFieldException"/> 原样冒泡——上层
        /// <c>Core.Gameplay.Spawn.SpawnSummonOnlyCreatureRule.Validate</c> 只捕获
        /// <see cref="System.ArgumentException"/>（见该规则源码判断记录），两者的异常类型契约必须
        /// 匹配，否则该规则会让非法数据把整次校验从"报告诊断"变成进程级未处理异常。</summary>
        [Fact]
        public void Get_TemplateWithIllegalNpcFlagValue_WrapsDataFieldExceptionAsArgumentException()
        {
            var query = new Core.Carriers.Creature.RegistryCreatureTemplateQuery(new IllegalNpcFlagRegistryView());

            var ex = Assert.Throws<System.ArgumentException>(
                () => query.Get(new Id("creature.sample_bad_flag")));
            Assert.IsType<DataFieldException>(ex.InnerException);
        }

        /// <summary>同上，验证 <see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery.HasFlag"/>
        /// （内部调用 <see cref="Core.Carriers.Creature.RegistryCreatureTemplateQuery.Get"/>）同样不
        /// 会把 <see cref="DataFieldException"/> 原样冒泡——这正是
        /// <c>Core.Gameplay.Spawn.SpawnSummonOnlyCreatureRule.Validate</c> 实际调用的成员。</summary>
        [Fact]
        public void HasFlag_TemplateWithIllegalNpcFlagValue_WrapsDataFieldExceptionAsArgumentException()
        {
            var query = new Core.Carriers.Creature.RegistryCreatureTemplateQuery(new IllegalNpcFlagRegistryView());

            var ex = Assert.Throws<System.ArgumentException>(
                () => query.HasFlag(new Id("creature.sample_bad_flag"), Core.Carriers.Creature.NpcFlag.SummonOnly));
            Assert.IsType<DataFieldException>(ex.InnerException);
        }
    }
}
