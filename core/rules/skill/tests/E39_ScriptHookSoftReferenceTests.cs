using System.Linq;
using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// 消费方反馈第 39 条验收（2026-09-13，见
    /// architecture/落地计划/消费方反馈-2026-09-13-编辑器-第38-39条.md）：全仓排查确认
    /// <c>found.hook</c> 早已在 <c>Core.Foundation.HookRegistry.FoundHookSchema.Table</c> 登记
    /// <c>TableSchema</c>（收边 I1 落地，<c>DialogHost</c>/<c>AreaTriggerHost</c> 均为真实运行时
    /// 消费方），此前 <c>skill.def</c> 的 <c>script</c> 效果 <c>hook_id</c> 字段判断记录仍写"found.hook
    /// 当前无实现级 schema 登记"已经过时——本测试锁死该字段补齐 <see cref="FieldSchema.SoftReferenceTable"/>
    /// 元数据（做法同消费方反馈第 37 条 <c>skill.proc_def.trigger_skill</c>）。
    /// </summary>
    public sealed class E39_ScriptHookSoftReferenceTests
    {
        [Fact]
        public void ScriptEffect_HookId_DeclaresSoftReferenceToFoundHook()
        {
            var scriptCase = Core.Rules.Skill.SkillSchemas.EffectsItemSchema.Variants!.Cases["script"];
            var paramsField = scriptCase.Single(f => f.Name == "params");
            var hookIdField = paramsField.Fields!.Single(f => f.Name == "hook_id");

            Assert.Equal(FieldKind.Id, hookIdField.Kind);
            Assert.Equal("found.hook", hookIdField.SoftReferenceTable);
            Assert.Null(hookIdField.SoftReferenceDomain);
        }
    }
}
