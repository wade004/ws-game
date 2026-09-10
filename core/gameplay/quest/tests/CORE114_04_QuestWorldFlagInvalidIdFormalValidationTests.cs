using System.Linq;
using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// CORE114-04 根治验收（外部审计 audit-76d16a5-20260910，见 <c>ExprValueJson.Parse</c> <c>"$id"</c>
    /// 分支、<c>QuestContentValidationRule</c> <c>reward_world_flag_value_shape</c> 判断记录）：正式
    /// <see cref="ContentValidationAssembly.Run"/> 默认参数装配下，<c>quest.def</c>
    /// <c>rewards.world_flags[].value = {"$id":"BAD"}</c>（Id 格式非法）必须产出阻断级字段错误的
    /// <c>report</c>，而不是让 <see cref="Core.Gameplay.Common.ExprValueJson.IsValid"/> 内部的 Id
    /// 构造异常原样冒泡、让正式校验入口本身崩溃——此前的异常安全缺口正是外部审计
    /// <c>QuestWorldFlagValueBoundaryProbe</c> 复现的 <c>invalid_id_formal=throws;
    /// type=ArgumentException</c>。改造自该探针。
    /// </summary>
    public sealed class CORE114_04_QuestWorldFlagInvalidIdFormalValidationTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":[" + rowsJson + "]}";

        private static InMemoryDataSource Source(string value) => new InMemoryDataSource()
            .Add("l10n.locale", Envelope("l10n.locale", "{\"id\":\"l10n.locale.zh_cn\",\"is_default\":true}"))
            .Add("l10n.text", Envelope("l10n.text", "{\"key\":\"l10n.quest.core114_04.title\",\"locale\":\"l10n.locale.zh_cn\",\"text\":\"Audit\"}"))
            .Add("quest.def", Envelope("quest.def",
                "{\"id\":\"quest.core114_04\",\"title_key\":\"l10n.quest.core114_04.title\"," +
                "\"objectives\":[{\"type\":\"event\",\"target_ref\":\"event.core114_04\",\"count\":1}]," +
                "\"start_method\":\"auto\",\"turn_in_method\":\"auto\",\"repeatable\":\"none\"," +
                "\"rewards\":{\"world_flags\":[{\"flagKey\":\"world.flag.core114_04\",\"value\":" + value + "}]}}"));

        [Fact]
        public void Run_MalformedIdWorldFlagValue_ReportsBlockingErrorInsteadOfThrowing()
        {
            var run = ContentValidationAssembly.Run(new IDataSource[] { Source("{\"$id\":\"BAD\"}") });

            Assert.True(run.Report.IsBlocking);
            Assert.Contains(run.Report.Issues, i =>
                i.Check == "reward_world_flag_value_shape" &&
                i.Field == "rewards.world_flags[0].value");
        }

        [Fact]
        public void Run_ValidBoolWorldFlagValue_PassesAndParsesOneWorldFlag()
        {
            var run = ContentValidationAssembly.Run(new IDataSource[] { Source("true") });
            Assert.False(run.Report.IsBlocking, string.Join("; ", run.Report.Issues));

            var registry = (DataRegistry)ContentValidationAssembly.CreateRegistry(
                Source("true"), new ContentValidationOptions(), out _);
            registry.LoadAll();
            var parsed = Core.Gameplay.Quest.QuestDefinition.FromRecord(
                registry.Get("quest.def", "quest.core114_04")!,
                Core.Gameplay.Assembly.GameplaySchemaCatalog.FullExprSchema);
            Assert.Single(parsed.Rewards.WorldFlags);
        }

        [Theory]
        [InlineData("1")]
        [InlineData("1.5")]
        [InlineData("\"some_text\"")]
        [InlineData("{\"$id\": \"world.core114_04_other\"}")]
        public void Run_OtherLegalShapes_PassAndParse(string value)
        {
            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(value) });
            Assert.False(run.Report.IsBlocking, value + " => " + string.Join("; ", run.Report.Issues));
        }
    }
}
