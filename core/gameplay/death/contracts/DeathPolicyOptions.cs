using System;
using Core.Foundation.Common;

namespace Core.Gameplay.Death
{
    /// <summary>
    /// <c>Revive</c> 的窄契约委托（见 <c>Core.Carriers.Unit.WorldUnitAccess.Revive</c>）：签名
    /// 与该方法完全一致，供 <c>GameplayAssembly</c> 把 <c>WorldUnitAccess.Revive</c> 方法组直接
    /// 接给本模块——<see cref="DeathPolicyHost"/>（L4）不直接引用 <c>WorldUnitAccess</c>
    /// （L3 具体类型），而是经这个委托拿到"复活单位"这一窄能力，惯例同
    /// <c>Core.Carriers.Gobj.TeleportResolverDelegate</c>/<c>SaveRequesterDelegate</c> 等既有
    /// L4↔L3 边界委托。
    /// </summary>
    public delegate void ReviveUnitDelegate(Id unitId, Vec2 position, double healthFraction);

    /// <summary>
    /// 解析"某张地图的默认复活点"委托：入参地图 id，命中时返回 <c>(该地图 id, 复活点世界坐标)</c>，
    /// 解析失败（地图不存在/无 <c>spawn_points</c>）返回 <c>null</c>。签名与
    /// <c>Core.Gameplay.Assembly.TeleportTargetResolver.Resolve</c> 完全一致——
    /// <c>GameplayAssembly</c>（组装根）把同一个 <c>TeleportTargetResolver</c> 实例的 <c>Resolve</c>
    /// 方法组接给本委托，传入死亡单位所属地图 id（一个合法的两段式 <c>world.&lt;地图名&gt;</c>
    /// 引用）即可复用其"整串即地图 id → 该地图 <c>spawn_points[0]</c>"这条既有解析路径（见该类型
    /// 判断记录），本模块（<c>core/gameplay/death</c>）不重复实现地图/出生点查找逻辑，也不直接
    /// 依赖 <c>core/gameplay/assembly</c>（同层 L4 模块之间不应互相依赖，组装根是唯一知道两者都
    /// 存在的一方）。
    /// </summary>
    public delegate (Id MapId, Vec2 Position)? ResolveDefaultSpawnDelegate(Id mapId);

    /// <summary>
    /// <c>core/gameplay/death</c> 模块的构造期口味配置（见 06_规则层_属性技能战斗AI.md 第 4.6 节
    /// 死亡与复活三策略、DECISIONS 拍板 3）。全部字段均有默认值，但默认取值本身不代表任何具体
    /// 游戏的口味决策——每款游戏应在自己的口味配置清单中显式声明这些字段的取值（惯例同
    /// <c>core/rules/skill.SkillOptions</c> 顶部判断记录）。
    /// </summary>
    public sealed class DeathPolicyOptions
    {
        /// <summary>
        /// 死亡复活策略。为 <c>null</c>（默认）时，<see cref="DeathPolicyHost"/> 取
        /// <c>CombatOptions.DeathPolicy</c> 作为默认值——本模块不重复声明一份独立的默认策略，
        /// 避免与 06 第 4.6 节"策略来自 <c>CombatOptions.DeathPolicy</c>"这条既有配置来源脱节；
        /// 非 null 时本字段覆盖 <c>CombatOptions.DeathPolicy</c>（供游戏层按需要单独覆盖死亡复活
        /// 与战斗其余部分的默认策略来源，二者本来就是两个独立字段）。
        /// </summary>
        public Core.Rules.Common.RespawnPolicy? Policy { get; set; }

        /// <summary>
        /// <c>respawn_point</c> 策略复活后的生命值比例（<c>[0,1]</c>），见 06 第 4.6 节
        /// "满状态复活"。默认 <c>1.0</c>（满状态）——该默认值是 06 文档明确写死的规范行为，不是
        /// 待游戏层决策的口味项，但仍暴露为字段供测试与后续可能的规则扩展直接构造非默认场景。
        /// </summary>
        public double RespawnHealthFraction { get; set; } = 1.0;

        /// <summary>
        /// <c>respawn_point</c> 策略从死亡到实际复活之间延迟的 tick 数（见 DECISIONS 拍板 3
        /// "延迟按选项（默认下一 tick）"）：<c>1</c> 表示死亡当次 tick 的下一次 tick 即复活，
        /// <c>N</c> 表示再等 <c>N</c> 次 tick。默认 <c>1</c>。不接受非正数（见
        /// <see cref="DeathPolicyHost"/> 构造函数校验）——死亡结算发生在
        /// <c>unit.died</c> 的 <c>EventDispatch</c> 阶段，晚于本 tick 的 <c>TriggerEvaluation</c>
        /// 阶段（本模块的 tick 处理器挂载点），因此"当次 tick 立即复活"在时序上不可行，
        /// 最早只能是下一次 tick。
        /// </summary>
        public int RespawnDelayTicks { get; set; } = 1;

        /// <summary>
        /// <c>reload_save</c> 策略读取的存档槽 id（见 06 第 4.6 节"回退到最近一次自动/手动存档"）。
        /// 默认 <c>"slot.autosave"</c>，与 <c>GameplayAssembly.DefaultAutosaveSlotId</c> 同源——
        /// 多存档槽的游戏应显式覆盖为自己追踪的"最近一次存档"槽 id。
        /// </summary>
        public Id AutosaveSlotId { get; set; } = new Id("slot.autosave");

        /// <summary>
        /// <c>permadeath</c> 策略删除的"当前槽" id 来源（见 06 第 4.6 节"死亡即结束当前存档周期"）。
        /// 为 <c>null</c>（默认）时退化为删除 <see cref="AutosaveSlotId"/>——单存档槽的游戏这一默认
        /// 值就足够；多存档槽的游戏（有独立于自动存档槽的"当前正在进行的存档周期"概念）应显式提供
        /// 本委托，返回真正应该被删除的槽 id。
        /// </summary>
        public Func<Id>? CurrentSlotIdProvider { get; set; }

        /// <summary>复活单位的窄契约，见 <see cref="ReviveUnitDelegate"/>。为 <c>null</c> 时
        /// <c>respawn_point</c> 策略只记诊断、不实际复活（未装配时的保守退化，同本仓库一贯的
        /// "未接线时静默/记诊断，不抛异常"惯例）。</summary>
        public ReviveUnitDelegate? ReviveUnit { get; set; }

        /// <summary>解析默认复活点的窄契约，见 <see cref="ResolveDefaultSpawnDelegate"/>。为
        /// <c>null</c> 或解析失败时同上，只记诊断、不复活。</summary>
        public ResolveDefaultSpawnDelegate? ResolveDefaultSpawn { get; set; }
    }
}
