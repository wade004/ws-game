using System.Linq;
using Core.Foundation.Common;
using Core.Gameplay.Quest;
using Xunit;

namespace Tests.Gameplay.Quest
{
    /// <summary>
    /// 消费方反馈第 54 条（2026-09-18，见
    /// architecture/落地计划/消费方反馈-2026-09-18-编辑器-第54-58条.md）验收：
    /// <see cref="QuestReferenceExtractor.ExtractReferencedQuestIds"/> 新增的公开静态入口，覆盖多引用、
    /// 去重与顺序约定、无引用、非法表达式四类行为。
    /// </summary>
    public sealed class QuestReferenceExtractorTests
    {
        [Fact]
        public void MultipleDistinctReferences_ReturnsAllInSourceOrder()
        {
            var ids = QuestReferenceExtractor.ExtractReferencedQuestIds(
                "quest.is_completed(quest.e54_b) and quest.is_active(quest.e54_c)");

            Assert.Equal(new[] { Id.Parse("quest.e54_b"), Id.Parse("quest.e54_c") }, ids);
        }

        [Fact]
        public void DuplicateReference_DedupedKeepingFirstOccurrenceOrder()
        {
            var ids = QuestReferenceExtractor.ExtractReferencedQuestIds(
                "quest.is_completed(quest.e54_a) and (quest.is_active(quest.e54_b) or " +
                "quest.is_completed(quest.e54_a))");

            Assert.Equal(new[] { Id.Parse("quest.e54_a"), Id.Parse("quest.e54_b") }, ids);
        }

        [Fact]
        public void NoQuestReference_ReturnsEmpty()
        {
            var ids = QuestReferenceExtractor.ExtractReferencedQuestIds("player.level() >= 5");

            Assert.Empty(ids);
        }

        [Fact]
        public void NullOrEmptyText_ReturnsEmpty()
        {
            Assert.Empty(QuestReferenceExtractor.ExtractReferencedQuestIds(""));
            Assert.Empty(QuestReferenceExtractor.ExtractReferencedQuestIds(null!));
        }

        [Fact]
        public void UnparsableExpr_ReturnsEmptyNotThrow()
        {
            // 判断记录：语法错误不向调用方抛出新的未处理异常路径，返回空列表——与
            // QuestContentValidationRule.ValidatePrerequisiteGraph 内部"语法错误已由 expr_parsable
            // 报告，这里跳过"的既有语义保持一致，见 QuestReferenceExtractor 类型顶部判断记录。
            var ids = QuestReferenceExtractor.ExtractReferencedQuestIds("(( bad");

            Assert.Empty(ids);
        }

        [Fact]
        public void NestedInsideAndOrNot_StillCollected()
        {
            var ids = QuestReferenceExtractor.ExtractReferencedQuestIds(
                "not quest.is_completed(quest.e54_a)");

            Assert.Equal(new[] { Id.Parse("quest.e54_a") }, ids);
        }

        [Fact]
        public void CustomSchema_SameResultAsDefault()
        {
            var schema = QuestExprSchemaEntries.BuildParsingSchema();

            var ids = QuestReferenceExtractor.ExtractReferencedQuestIds(
                "quest.is_completed(quest.e54_a)", schema);

            Assert.Equal(new[] { Id.Parse("quest.e54_a") }, ids);
        }
    }
}
