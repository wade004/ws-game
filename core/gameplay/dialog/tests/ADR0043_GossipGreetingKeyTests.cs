using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;
using Core.Gameplay.Dialog;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Dialog
{
    /// <summary>
    /// ADR-0043 验收（消费方反馈 3a/3b 合并改动单：<c>dialog.gossip_menu</c> 缺开场白/正文字段，
    /// <c>DialogPanel</c> 因此硬编码占位文案）：本测试锁死 schema 登记与
    /// <see cref="GossipMenuDefinition.FromRecord"/> 对新增可选字段 <c>greeting_key</c> 的解析行为——
    /// 有该字段时正确解析为 <see cref="GossipMenuDefinition.GreetingKey"/>（供 <c>DialogHost</c> 透传给
    /// <see cref="GossipView"/>、UI 消费方据此渲染正文区）；无该字段时为 null 且不抛异常（ADR-0043 决策
    /// "缺省不渲染正文区、不回落占位文案"的数据层前提）。<see cref="DialogHostTests"/> 另覆盖
    /// <c>DialogHost.OpenGossip</c>/<c>GetGossipView</c> 把 <see cref="GossipMenuDefinition.GreetingKey"/>
    /// 透传到 <see cref="GossipView.GreetingKey"/> 这一段（构造 <see cref="GossipMenuDefinition"/> 直接
    /// 用内存态构造函数）；本文件专门覆盖再往前一段——从 JSON 记录解析出内存态的
    /// <see cref="GossipMenuDefinition.FromRecord"/> 本身。
    /// </summary>
    public sealed class ADR0043_GossipGreetingKeyTests
    {
        private static readonly IExprSchema Schema = QuestExprSchemaEntries.BuildParsingSchema();

        private static DataRecord Record(string json)
        {
            var raw = (JsonObject)JsonReader.Parse(json);
            var id = new Id(((JsonString)raw["id"]).Value);
            return new DataRecord(DialogSchemas.GossipMenu, id.Value, id, raw);
            // 判断记录：DataRecord 构造函数第 2 参 key 是原始字符串（内容表主键即 id 原文），
            // 第 3 参 id（CommonId? = Id?）是解析后的 Id——两者取值相同、类型不同，同
            // DataRecord.GetId 的既有用法。
        }

        [Fact]
        public void GossipMenu_Field_GreetingKey_IsRegisteredAsOptionalTextKey()
        {
            var field = System.Linq.Enumerable.Single(DialogSchemas.GossipMenu.Fields, f => f.Name == "greeting_key");

            Assert.Equal(FieldKind.TextKey, field.Kind);
            Assert.False(field.Required);
        }

        [Fact]
        public void FromRecord_ParsesGreetingKey_WhenPresent()
        {
            var record = Record(
                "{\"id\":\"dialog.adr0043_menu\",\"greeting_key\":\"l10n.dialog.adr0043_menu.greeting\"," +
                "\"options\":[{\"text_key\":\"l10n.opt_a\"}]}");

            var menu = GossipMenuDefinition.FromRecord(record, Schema);

            Assert.Equal(new Id("l10n.dialog.adr0043_menu.greeting"), menu.GreetingKey);
            Assert.Single(menu.Options);
        }

        [Fact]
        public void FromRecord_GreetingKeyIsNull_WhenFieldAbsent_AndDoesNotThrow()
        {
            var record = Record(
                "{\"id\":\"dialog.adr0043_menu_no_greeting\",\"options\":[{\"text_key\":\"l10n.opt_a\"}]}");

            var menu = GossipMenuDefinition.FromRecord(record, Schema);

            Assert.Null(menu.GreetingKey);
            Assert.Single(menu.Options);
        }
    }
}
