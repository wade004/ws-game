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

        /// <summary>
        /// ADR-0069（消费方反馈——游戏接入方第十四批）：只读、无副作用查询——<paramref
        /// name="creatureInstanceId"/> 指向的生物实例当前是否有可交互内容。不判距离、不判交互
        /// 发起者、不判目标是否存活（存活仍由调用方按 ADR-0065/0067 权威口径单独核对，见
        /// <see cref="Core.Carriers.Assembly.InteractionTargetRegistry"/> 判断记录）——本查询只回答
        /// "内容"这一层，与 <see cref="Interact"/> 共用同一份内容判定逻辑（见
        /// <c>Core.Carriers.Creature.CreatureInteractionHost</c> 判断记录"HasContent 内部共用方法"，
        /// 不能两处各写一遍，否则两者迟早不一致）。供
        /// <see cref="Core.Carriers.Common.IInteractionTargetRegistry"/> 在"最近可交互目标"候选判定中
        /// 排除"存在且存活但没有任何可交互内容"的生物（消费方反馈原文：护送/跟随/闲逛的生物离玩家
        /// 最近时被误选中，玩家按交互键什么也不会发生）。
        /// <para>
        /// C# 8 默认接口成员（ABI 只加不改）：本默认实现无法访问任何具体实现方的模板/配置状态，
        /// 降级口径恒返回 <c>true</c>（"有内容"）——保持未升级的既有 <see
        /// cref="ICreatureInteractionHost"/> 实现方（自定义测试替身、旧版本编译产物）在接入本查询的
        /// 调用方眼中的行为与升级前一致：它们此前从未参与"是否有内容"过滤，默认"有内容"等价于"不
        /// 参与过滤"，不会把它们此前能正常交互的生物意外排除出候选（同 <c>IEncounterHost.TryStart</c>
        /// 判断记录同一类"降级口径不产生看似成功、实际排除了本该保留的候选"的处境）。生产实现
        /// <see cref="Core.Carriers.Creature.CreatureInteractionHost"/> 用显式接口实现覆盖，转发到与
        /// <see cref="Interact"/> 共用的判定逻辑（避免隐式实现让 <c>toolchain/abi_surface</c> 把既有
        /// 的普通公开方法误判为 <c>virtual sealed</c> 签名变更，手法同 <c>IEncounterHost.TryStart</c>
        /// 判断记录）。任何组合/包装 <see cref="ICreatureInteractionHost"/>（若存在）都应显式转发到
        /// 内层实现，不应悄悄吃掉这个降级默认值——同 <c>ISkillHost</c> 系列默认接口成员判断记录里
        /// "框架内 InterfaceDefaultMemberForwardingTests 门禁"。
        /// </para>
        /// </summary>
        bool HasInteractableContent(Id creatureInstanceId) => true;
    }
}
