using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// ADR-0051：生物（creature/unit）原生交互接口——消费方反馈第 2 条根治："interact" 意图此前只有
    /// <see cref="IGameObjectHost"/>（gobj 专属）一个消费者，生物要被交互必须先包装成
    /// <c>gobj.template</c>。本接口让生物可以作为交互目标被直接 <c>interact</c>，不必绕经 gobj。由
    /// <c>core/carriers/creature</c> 实现（见该模块 <c>CreatureInteractionHost</c>）。
    /// <para>
    /// 判断记录（复用 <see cref="InteractResult"/>/<see cref="InteractOutcome"/>，不新建平行类型）：
    /// 生物当前只有"分发到 <c>dialog.gossip_menu</c>"一种原生交互结果（见
    /// <c>Core.Carriers.Creature.CreatureTemplate.GossipMenuRef</c> 判断记录），<see
    /// cref="InteractOutcome.Dialog"/>/<see cref="InteractOutcome.NoAction"/>/
    /// <see cref="InteractOutcome.Unknown"/> 三个既有取值已经完整覆盖；<see
    /// cref="InteractOutcome.Skill"/>/<see cref="InteractOutcome.Locked"/> 是 gobj 专属语义（生物没有
    /// "锁定"概念，技能施放走既有的 <c>cast</c> 意图，不经本接口），生物侧的实现恒不产出这两个取值，
    /// 不需要为此新增一套平行的枚举。
    /// </para>
    /// </summary>
    public interface ICreatureInteractionHost
    {
        /// <summary>交互的统一入口：<paramref name="unitId"/> 尝试交互 <paramref
        /// name="creatureInstanceId"/> 指向的生物实例，按该生物模板的 <c>gossip_menu_ref</c> 分发到
        /// 对话系统（见 <c>Core.Carriers.Creature.CreatureInteractionHost</c> 判断记录），成功时经
        /// 注入的回调把交互者身份与被交互者（生物）实例身份一并透传给下游。</summary>
        InteractResult Interact(Id unitId, Id creatureInstanceId);
    }
}
