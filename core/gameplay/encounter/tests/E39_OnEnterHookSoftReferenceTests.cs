using System.Linq;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Gameplay.Encounter
{
    /// <summary>
    /// 消费方反馈第 39 条验收（2026-09-13，见
    /// architecture/落地计划/消费方反馈-2026-09-13-编辑器-第38-39条.md）：<c>encounter.def.phases[].
    /// on_enter_hook</c> 判断记录此前仍写"found.hook 当前无实现级 schema 登记"——全仓排查确认该表
    /// 早已登记 <c>TableSchema</c>（<c>Core.Foundation.HookRegistry.FoundHookSchema.Table</c>，收边
    /// I1 落地），本测试锁死本字段补齐 <see cref="FieldSchema.SoftReferenceTable"/> 元数据。
    /// </summary>
    public sealed class E39_OnEnterHookSoftReferenceTests
    {
        [Fact]
        public void PhaseItemSchema_OnEnterHook_DeclaresSoftReferenceToFoundHook()
        {
            var field = Core.Gameplay.Encounter.EncounterSchemas.PhaseItemSchema.Fields!
                .Single(f => f.Name == "on_enter_hook");

            Assert.Equal(FieldKind.Id, field.Kind);
            Assert.Equal("found.hook", field.SoftReferenceTable);
            Assert.Null(field.SoftReferenceDomain);
        }
    }
}
