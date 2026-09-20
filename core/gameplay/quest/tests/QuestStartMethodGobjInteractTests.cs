using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// 消费方反馈第 9 条（2026-09-20，ADR-0048）：<see cref="QuestStartMethod"/> 新增
    /// <see cref="QuestStartMethod.GobjInteract"/>/线上文本 <c>"gobj_interact"</c>。本文件只覆盖
    /// <see cref="QuestEnumWireNames.TryParseStartMethod"/> 的往返解析——新取值能被正确解析、
    /// 全部既有四个取值行为不受影响（ABI/schema 均为纯加法，见类型判断记录）。触发链路是否真的
    /// 能由 gobj 交互驱动任务接取，见
    /// <c>Tests.Gameplay.Assembly.GameplayAssemblyGobjQuestStartTests</c>（装配级集成测试）。
    /// </summary>
    public sealed class QuestStartMethodGobjInteractTests
    {
        [Fact]
        public void TryParseStartMethod_GobjInteract_ParsesToNewEnumValue()
        {
            Assert.True(QuestEnumWireNames.TryParseStartMethod("gobj_interact", out var value));
            Assert.Equal(QuestStartMethod.GobjInteract, value);
        }

        [Theory]
        [InlineData("npc_gossip", QuestStartMethod.NpcGossip)]
        [InlineData("item_use", QuestStartMethod.ItemUse)]
        [InlineData("area_trigger", QuestStartMethod.AreaTrigger)]
        [InlineData("auto", QuestStartMethod.Auto)]
        public void TryParseStartMethod_ExistingValues_StillParseTheSameAsBefore(string text, QuestStartMethod expected)
        {
            Assert.True(QuestEnumWireNames.TryParseStartMethod(text, out var value));
            Assert.Equal(expected, value);
        }

        [Fact]
        public void StartMethodValues_ContainsGobjInteract_AppendedAfterExistingFour()
        {
            Assert.Equal(
                new[] { "npc_gossip", "item_use", "area_trigger", "auto", "gobj_interact" },
                QuestEnumWireNames.StartMethodValues);
        }

        [Fact]
        public void TryParseStartMethod_UnknownText_ReturnsFalse()
        {
            Assert.False(QuestEnumWireNames.TryParseStartMethod("not_a_real_start_method", out _));
        }
    }
}
