using Core.Foundation.DataRegistry;
using Presentation.Assembly;
using Xunit;

namespace Tests.Gameplay.Dialog
{
    /// <summary>
    /// CORE114-04 根治验收（外部审计 audit-76d16a5-20260910，见 <c>ExprValueJson.Parse</c> <c>"$id"</c>
    /// 分支、<c>DialogContentValidationRule</c> <c>gossip_action_set_flag_value_shape</c> 判断记录）：
    /// 正式 <see cref="ContentValidationAssembly.Run"/> 默认参数装配下，<c>dialog.gossip_menu</c>
    /// <c>options[].actions[].params.value</c>（<c>set_flag</c> 动作）为 <c>{"$id":"BAD"}</c>（Id
    /// 格式非法）时同样必须产出阻断级字段错误，而不是让 <c>ExprValueJson.IsValid</c> 内部异常冒泡——
    /// 与 Quest 侧共用同一份 <c>ExprValueJson</c> 实现，两条正式校验路径都要覆盖（08/09 判断记录，
    /// 见 <c>QuestContentValidationRule</c> 同款测试）。
    /// </summary>
    public sealed class CORE114_04_DialogSetFlagInvalidIdFormalValidationTests
    {
        private static string Envelope(string table, string rowsJson) =>
            "{\"table\":\"" + table + "\",\"schema_version\":1,\"rows\":[" + rowsJson + "]}";

        private static InMemoryDataSource Source(string value) => new InMemoryDataSource()
            .Add("l10n.locale", Envelope("l10n.locale", "{\"id\":\"l10n.locale.zh_cn\",\"is_default\":true}"))
            .Add("l10n.text", Envelope("l10n.text", "{\"key\":\"l10n.dialog.core114_04.opt\",\"locale\":\"l10n.locale.zh_cn\",\"text\":\"Audit\"}"))
            .Add("dialog.gossip_menu", Envelope("dialog.gossip_menu",
                "{\"id\":\"dialog.core114_04_menu\",\"options\":[{\"text_key\":\"l10n.dialog.core114_04.opt\"," +
                "\"actions\":[{\"kind\":\"set_flag\",\"ref\":\"world.flag.core114_04\",\"params\":{\"value\":" + value + "}}]}]}"));

        [Fact]
        public void Run_MalformedIdSetFlagValue_ReportsBlockingErrorInsteadOfThrowing()
        {
            var run = ContentValidationAssembly.Run(new IDataSource[] { Source("{\"$id\":\"BAD\"}") });

            Assert.True(run.Report.IsBlocking);
            Assert.Contains(run.Report.Issues, i =>
                i.Check == "gossip_action_set_flag_value_shape" &&
                i.Field == "options[0].actions[0].params.value");
        }

        [Theory]
        [InlineData("true")]
        [InlineData("1")]
        [InlineData("1.5")]
        [InlineData("\"some_text\"")]
        [InlineData("{\"$id\": \"world.core114_04_other\"}")]
        public void Run_LegalShapes_Pass(string value)
        {
            var run = ContentValidationAssembly.Run(new IDataSource[] { Source(value) });
            Assert.False(run.Report.IsBlocking, value + " => " + string.Join("; ", run.Report.Issues));
        }
    }
}
