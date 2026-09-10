using System.Linq;
using Core.Foundation.Expr;
using Presentation.Assembly;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 消费方反馈（编辑器）第 27 条复现/回归（2026-09-11，见
    /// architecture/落地计划/消费方反馈-2026-09-11-编辑器-第27条.md）：正式装配的
    /// <see cref="PresentationSchemaCatalog.FullExprSchema"/>（等于
    /// <see cref="Core.Gameplay.Assembly.GameplaySchemaCatalog.FullExprSchema"/>，即
    /// <c>CompositeExprSchema(QuestExprSchemaEntries.BuildParsingSchema(), RulesExprSchema.Base)</c>）
    /// 逐分组调用 <see cref="IExprSchema.KnownKeys"/>/<see cref="IExprSchema.KnownGroups"/>：
    /// 根治前，<c>CompositeExprSchema</c>/<c>EventGroupPermissiveSchema</c>（
    /// <c>core/gameplay/quest/contracts/QuestExprSchemaEntries.cs</c>）/<c>RulesExprSchema</c> 均未
    /// 重写这两个 1.17.0 新增的带默认实现接口成员，逐级落回 <see cref="IExprSchema"/> 默认实现
    /// （恒返回空集合）——九个分组全部返回空，即便 <see cref="Core.Rules.ExprHost.RulesExprSchema.Base"/>
    /// 自身 <c>KnownKeys</c> 非空。根治后：<c>self</c>/<c>target</c>/<c>world</c>/<c>quest</c>/
    /// <c>player</c>/<c>combat</c>/<c>enemies</c>/<c>time</c> 八个分组各自非空，<c>event</c> 分组
    /// 按设计恒为空（<c>EventGroupPermissiveSchema</c> 对 <c>event</c> 不登记任何具体 key，只做
    /// "未登记也放行"的匹配，不存在"已登记 key 集合"这个概念，见该类型 <c>KnownKeys</c> 判断记录）。
    /// </summary>
    public class FullExprSchemaKnownKeysTests
    {
        private static readonly IExprSchema Schema = PresentationSchemaCatalog.FullExprSchema;

        [Theory]
        [InlineData(ExprGroups.Self)]
        [InlineData(ExprGroups.Target)]
        [InlineData(ExprGroups.World)]
        [InlineData(ExprGroups.Quest)]
        [InlineData(ExprGroups.Player)]
        [InlineData(ExprGroups.Combat)]
        [InlineData(ExprGroups.Enemies)]
        [InlineData(ExprGroups.Time)]
        public void KnownKeys_NonEventGroup_IsNonEmpty(string group)
        {
            Assert.NotEmpty(Schema.KnownKeys(group));
        }

        [Fact]
        public void KnownKeys_EventGroup_IsEmpty_ByDesign()
        {
            // event 分组的具体字段名随触发求值的事件类型而变，QuestExprSchemaEntries 内部的
            // EventGroupPermissiveSchema 从不登记任何具体 event.* key，只对该分组恒放行匹配（见
            // 该类型判断记录）——KnownKeys("event") 恒为空不是遗漏，是"恒放行"语义本身决定的。
            Assert.Empty(Schema.KnownKeys(ExprGroups.Event));
        }

        [Fact]
        public void KnownGroups_CoversAllRegisteredGroups_ExcludingEvent()
        {
            var groups = Schema.KnownGroups.OrderBy(g => g, System.StringComparer.Ordinal).ToArray();

            Assert.Equal(
                new[]
                {
                    ExprGroups.Combat, ExprGroups.Enemies, ExprGroups.Player, ExprGroups.Quest,
                    ExprGroups.Self, ExprGroups.Target, ExprGroups.Time, ExprGroups.World,
                },
                groups);
            Assert.DoesNotContain(ExprGroups.Event, groups);
        }

        [Fact]
        public void KnownKeys_Self_ContainsRulesExprHostFactoryKeys()
        {
            // 抽样断言几个 RulesExprHostFactory 真实支持的 key，防止"非空但结果不对"这类更隐蔽的回归。
            var keys = Schema.KnownKeys(ExprGroups.Self);
            Assert.Contains("hp", keys);
            Assert.Contains("is_alive", keys);
            Assert.Contains("distance_to_target", keys);
        }

        [Fact]
        public void KnownKeys_Quest_ContainsQuestExprGroupProviderKeys()
        {
            var keys = Schema.KnownKeys(ExprGroups.Quest);
            Assert.Contains("is_active", keys);
            Assert.Contains("is_objectives_complete", keys);
        }

        [Fact]
        public void KnownKeys_World_ContainsWorldExprSchemaEntriesKeys()
        {
            var keys = Schema.KnownKeys(ExprGroups.World);
            Assert.Contains("get", keys);
            Assert.Contains("has", keys);
            Assert.Contains("get_int", keys);
        }

        [Fact]
        public void KnownKeys_Time_ContainsRulesExprHostFactoryKeys()
        {
            var keys = Schema.KnownKeys(ExprGroups.Time);
            Assert.Contains("sim_time", keys);
            Assert.Contains("turn_index", keys);
        }
    }
}
