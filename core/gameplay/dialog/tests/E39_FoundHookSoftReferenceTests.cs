using System.Linq;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Gameplay.Dialog
{
    /// <summary>
    /// 消费方反馈第 39 条验收（2026-09-13，见
    /// architecture/落地计划/消费方反馈-2026-09-13-编辑器-第38-39条.md）：全仓排查确认 <c>found.hook</c>
    /// 早已登记 <c>TableSchema</c>（<c>Core.Foundation.HookRegistry.FoundHookSchema.Table</c>，收边 I1
    /// 落地），<c>DialogHost.ExecuteAction</c>/<c>DialogHost</c> 进入节点时都是真实运行时消费方——本表
    /// <c>options[].actions[]{kind=script}.ref</c>/<c>nodes[].performance_hook_ref</c> 两处字段判断
    /// 记录此前仍写"found.hook 当前无实现级 schema 登记"已经过时，本测试锁死两处补齐
    /// <see cref="FieldSchema.SoftReferenceTable"/> 元数据（做法同消费方反馈第 37 条）。
    /// </summary>
    public sealed class E39_FoundHookSoftReferenceTests
    {
        [Fact]
        public void GossipAction_Script_Ref_DeclaresSoftReferenceToFoundHook()
        {
            var scriptCase = Core.Gameplay.Dialog.DialogSchemas.GossipActionItemSchema.Variants!.Cases["script"];
            var refField = scriptCase.Single(f => f.Name == "ref");

            Assert.Equal(FieldKind.Id, refField.Kind);
            Assert.Equal("found.hook", refField.SoftReferenceTable);
            Assert.Null(refField.SoftReferenceDomain);
        }

        [Fact]
        public void StoryNode_PerformanceHookRef_DeclaresSoftReferenceToFoundHook()
        {
            var field = Core.Gameplay.Dialog.DialogSchemas.StoryNodeItemSchema.Fields!
                .Single(f => f.Name == "performance_hook_ref");

            Assert.Equal(FieldKind.Id, field.Kind);
            Assert.Equal("found.hook", field.SoftReferenceTable);
            Assert.Null(field.SoftReferenceDomain);
        }
    }
}
