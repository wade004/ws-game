using System.Linq;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Gameplay.AreaTrigger
{
    /// <summary>
    /// 消费方反馈第 39 条验收（2026-09-13，见
    /// architecture/落地计划/消费方反馈-2026-09-13-编辑器-第38-39条.md）：全仓排查 <c>found.hook</c>
    /// 消费方时额外发现第五处同根因症状——<c>area.trigger_def.params.hook_id</c>（<c>trigger_type=
    /// script</c>）判断记录同样仍写"found.hook 当前无实现级 schema 登记"，与报告点名的三处（dialog 两处
    /// + encounter 一处）同批修复。本测试锁死本字段补齐 <see cref="FieldSchema.SoftReferenceTable"/>
    /// 元数据。
    /// </summary>
    public sealed class E39_HookIdSoftReferenceTests
    {
        [Fact]
        public void ParamsSchema_HookId_DeclaresSoftReferenceToFoundHook()
        {
            var field = Core.Gameplay.AreaTrigger.AreaTriggerSchemas.ParamsSchema.Fields!
                .Single(f => f.Name == "hook_id");

            Assert.Equal(FieldKind.Id, field.Kind);
            Assert.Equal("found.hook", field.SoftReferenceTable);
            Assert.Null(field.SoftReferenceDomain);
        }
    }
}
