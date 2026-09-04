using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Carriers.Item
{
    /// <summary>
    /// <see cref="IEffectExtension"/> 的物品分支实现：只处理 <see cref="EffectKind.CreateItem"/>
    /// （见 06 第 3.2 节该原语"往目标背包创建物品"、07 拍板决策 1"开门开箱等都归约为……授予"，
    /// <c>create_item</c> 是这一归约在效果原语层的落地）。<c>params</c> 形状为
    /// <c>{item_template: Id, count: Int}</c>（06 未给出该原语的精确参数名，本模块实现期按最小
    /// 必要集合补录，与任务书"处理 EffectKind.CreateItem（params {item_template, count} →
    /// InventoryHost.AddItem(target)）"一致）。<c>count</c> 省略时默认 1。
    /// </summary>
    public sealed class ItemEffectExtension : IEffectExtension
    {
        private readonly InventoryHost _inventory;
        private readonly IItemDiagnostics _diagnostics;

        public ItemEffectExtension(InventoryHost inventory, IItemDiagnostics? diagnostics = null)
        {
            _inventory = inventory ?? throw new System.ArgumentNullException(nameof(inventory));
            _diagnostics = diagnostics ?? new InMemoryItemDiagnostics();
        }

        public bool TryHandle(EffectContext context, out ResolveResult result)
        {
            if (context.Kind != EffectKind.CreateItem)
            {
                result = NoOp(context);
                return false;
            }

            if (!TryGetItemTemplate(context.Params, out var templateId))
            {
                _diagnostics.Warn(
                    $"create_item 效果缺少合法的 params.item_template（skillId={context.SkillId}，" +
                    $"targetId={context.TargetId}）");
                result = NoOp(context);
                return false;
            }

            var count = GetCount(context.Params);
            if (count <= 0)
            {
                _diagnostics.Warn(
                    $"create_item 效果的 params.count（{count}）非正数，已忽略（skillId={context.SkillId}）");
                result = NoOp(context);
                return false;
            }

            var added = _inventory.AddItem(context.TargetId, templateId, count);
            if (!added)
            {
                _diagnostics.Warn(
                    $"create_item 效果未能加入背包（单位 \"{context.TargetId}\" 背包已满，" +
                    $"模板 \"{templateId}\" x{count}）");
            }

            result = new ResolveResult(
                HitResult.Hit,
                requestedAmount: count,
                finalAmount: added ? count : 0,
                absorbed: 0,
                immune: false,
                isHeal: false);
            return true;
        }

        private static bool TryGetItemTemplate(JsonObject @params, out Id templateId)
        {
            if (@params.TryGetValue("item_template", out var v) && v is JsonString s && Id.TryParse(s.Value, out templateId))
            {
                return true;
            }

            templateId = default;
            return false;
        }

        private static int GetCount(JsonObject @params) =>
            @params.TryGetValue("count", out var v) && v is JsonNumber n ? (int)n.Value : 1;

        private static ResolveResult NoOp(EffectContext context) =>
            new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: false);
    }
}
