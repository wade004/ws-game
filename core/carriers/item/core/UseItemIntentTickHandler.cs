using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;
using Core.Rules.Skill;

namespace Core.Carriers.Item
{
    /// <summary>
    /// 消费 <c>use_item</c> 意图（<c>Args</c> 形状：<c>{instance_id: Id}</c>，由表现层
    /// <c>UiIntents.UseItem</c> 提交，<see cref="Intent.ActorId"/> 即使用者）：按物品模板
    /// <c>item.template.on_use</c>（<c>{skill_ref: Reference(skill.def), consume: Bool?}</c>）以使用者为施法者施放该技能，
    /// 施放成功且 <c>consume</c> 不为 false（缺省 true）时从使用者背包扣掉 1 个。挂在
    /// <see cref="TickPhase.TriggerEvaluation"/>（与 <c>interact</c> 意图同阶段，见 03 第 4.2 节步骤 6）。
    /// <para>
    /// 判断记录（消费方反馈 2026-10-07 样板游戏 A P3 缺口 G1）：此前 <c>UiIntents.UseItem</c> 提交的
    /// <c>use_item</c> 意图没有任何消费者，<c>item.template</c> 也没有登记"使用时做什么"的字段，消耗品只能躺在背包里。
    /// 本处理器补上这条通路，且完全复用既有机制而不引入第二套效果系统：效果由技能管线执行（治疗、加光环、
    /// 授予物品……任何 <c>skill.def.effects</c> 能表达的），冷却/资源/死亡/沉默等前置条件由施法管线裁决，
    /// 技能被拒时物品不消耗；药水冷却就是该技能的 <c>cooldown</c>。
    /// </para>
    /// <para>
    /// 判断记录（拒绝路径一律只记诊断、不抛异常、不改状态）：实例不在使用者背包（含别人的实例 id）、
    /// 模板没有 <c>on_use</c>、<c>skill_ref</c> 缺失或指向未登记技能、施法失败，五种情形都不扣物品也不产生副作用。
    /// 目标交给技能自己的目标链（<c>target_shape_ref</c>）解析，本处理器不传显式目标。
    /// </para>
    /// <para>
    /// 判断记录（扣减时机）：先施法、成功后再扣——施法成功后扣减失败（理论上不会，实例刚刚取到且同一线程）
    /// 只记诊断；反过来"先扣后施法"在施法被拒时需要回滚，更脆弱。
    /// </para>
    /// </summary>
    public sealed class UseItemIntentTickHandler : ITickPhaseHandler
    {
        private readonly InventoryHost _inventory;
        private readonly SkillHost _skills;
        private readonly IItemDiagnostics _diagnostics;

        public UseItemIntentTickHandler(InventoryHost inventory, SkillHost skills, IItemDiagnostics? diagnostics = null)
        {
            _inventory = inventory ?? throw new System.ArgumentNullException(nameof(inventory));
            _skills = skills ?? throw new System.ArgumentNullException(nameof(skills));
            _diagnostics = diagnostics ?? new InMemoryItemDiagnostics();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            foreach (var intent in world.CurrentIntents)
            {
                if (intent.Kind != "use_item")
                {
                    continue;
                }

                if (!intent.Args.TryGetValue("instance_id", out var idVal) || !(idVal is JsonString idStr))
                {
                    _diagnostics.Warn($"use_item 意图缺少 instance_id 参数（actorId=\"{intent.ActorId}\"），已忽略");
                    continue;
                }

                var instanceId = new Id(idStr.Value);
                var found = _inventory.FindInstance(intent.ActorId, instanceId);
                if (found == null)
                {
                    _diagnostics.Warn($"use_item：实例 \"{instanceId}\" 不在单位 \"{intent.ActorId}\" 的背包里，已忽略");
                    continue;
                }

                var instance = found.Value;
                var template = _inventory.GetTemplate(instance.TemplateId);
                if (template == null || !template.TryGetObject("on_use", out var onUse))
                {
                    _diagnostics.Warn($"use_item：模板 \"{instance.TemplateId}\" 没有 on_use，不可使用");
                    continue;
                }

                if (!onUse.TryGetValue("skill_ref", out var skillVal) || !(skillVal is JsonString skillStr)
                    || !Id.TryParse(skillStr.Value, out var skillId))
                {
                    _diagnostics.Warn($"use_item：模板 \"{instance.TemplateId}\" 的 on_use 缺少合法的 skill_ref");
                    continue;
                }

                var result = _skills.CastSkill(intent.ActorId, skillId, System.Array.Empty<Id>());
                if (!result.Success)
                {
                    _diagnostics.Warn(
                        $"use_item：技能 \"{skillId}\" 施放被拒（{result.Reason}），物品 \"{instance.TemplateId}\" 未消耗");
                    continue;
                }

                var consume = !(onUse.TryGetValue("consume", out var consumeVal) && consumeVal is JsonBool b && !b.Value);
                if (consume && !_inventory.RemoveItem(intent.ActorId, instanceId, 1))
                {
                    _diagnostics.Warn($"use_item：技能已施放但扣减物品 \"{instanceId}\" 失败");
                }
            }
        }
    }
}
