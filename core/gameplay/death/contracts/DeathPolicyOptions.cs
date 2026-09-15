using System;
using Core.Foundation.Common;
using Core.Foundation.SaveSystem;

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
    /// 外部审核阻塞项 2 收口新增（见 <c>core/gameplay/assembly/GameplayAssembly.RestoreFromSlot</c>
    /// 类型注释）：<c>reload_save</c> 策略读档的窄契约——签名与 <see cref="ISaveSystem.Load"/> 完全
    /// 一致（入参存档槽 id，返回 <see cref="LoadResult"/>）。<see cref="DeathPolicyHost"/>（L4）不
    /// 直接持有 <c>ISceneRouter</c>（本模块构造函数依赖清单本就没有它，见该类型），"读档后若目标
    /// 地图与当前地图不同则切场景"这一步交给注入方实现——惯例同 <see cref="ReviveUnitDelegate"/>/
    /// <see cref="ResolveDefaultSpawnDelegate"/> 两个既有 L4↔L3/L4↔L0 边界委托，<c>GameplayAssembly</c>
    /// （组装根，同时持有 <see cref="ISaveSystem"/> 与 <c>ISceneRouter</c>）把
    /// <c>GameplayAssembly.RestoreFromSlot</c> 方法组接给本委托。
    /// </summary>
    public delegate LoadResult ReloadSaveDelegate(Id slotId);

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

        /// <summary>
        /// 外部审核阻塞项 2 收口新增：<c>reload_save</c> 策略读档的窄契约，见
        /// <see cref="ReloadSaveDelegate"/>。为 <c>null</c>（默认，未装配 <c>GameplayAssembly</c> 的
        /// 单元测试场景）时，<see cref="DeathPolicyHost"/> 退化为直接调用注入的
        /// <c>ISaveSystem.Load</c>（本模块构造函数既有依赖，不切场景、不额外处理），与本次收口之前
        /// 完全一致的既有行为——只是生产装配（<c>GameplayAssembly</c>）总会接上一份真正"读档 + 必要
        /// 时切场景"的实现，见 <c>GameplayAssembly.RestoreFromSlot</c>。
        /// </summary>
        public ReloadSaveDelegate? ReloadSave { get; set; }

        /// <summary>
        /// T-N4-9（[ADR-0034](../../../architecture/adr/0034-单一货币与价格挂物品等级.md) 决策 6；
        /// 06 第 4.6 节 2026-09-14 修订段"复活费"）：<c>respawn_point</c> 策略复活费的计算方式，
        /// 二选一，原文措辞——<c>pct_of_balance</c>（按当前余额百分比）或
        /// <c>fixed_by_level</c>（引用曲线，按等级取固定值）。默认 <see cref="None"/>（本任务前的
        /// 既有行为——不收复活费），本模块不为默认策略/曲线取值做任何主张（同 <see cref="Policy"/>
        /// 顶部判断记录"每款游戏应显式声明口味"）。
        /// </summary>
        public enum RespawnFeePolicy
        {
            /// <summary>不收复活费——本任务之前的既有行为，逐位不变。</summary>
            None,

            /// <summary>按 <see cref="DeathPolicyOptions.RespawnFeePercentage"/> 乘以当前余额计算。</summary>
            PctOfBalance,

            /// <summary>按 <see cref="DeathPolicyOptions.RespawnFeeFixedAmount"/> 委托（引用等级
            /// 曲线，曲线本身的来源/表结构不属于本模块关心的事，见该委托判断记录）取固定值。</summary>
            FixedByLevel,
        }

        /// <summary>复活费计算方式，默认 <see cref="RespawnFeePolicy.None"/>（不收费，逐位兼容
        /// 本任务之前的行为）。</summary>
        public RespawnFeePolicy RespawnFee { get; set; } = RespawnFeePolicy.None;

        /// <summary><see cref="RespawnFeePolicy.PctOfBalance"/> 时的百分比（<c>[0,1]</c>，如
        /// <c>0.1</c> 表示扣当前余额的 10%）。默认 <c>0</c>——未显式配置时即便
        /// <see cref="RespawnFee"/> 被设为 <see cref="RespawnFeePolicy.PctOfBalance"/>，计算出的
        /// 费用也恒为 0（同本类"字段有默认值但不代表任何游戏口味决策"的一贯注释惯例）。</summary>
        public double RespawnFeePercentage { get; set; } = 0.0;

        /// <summary>
        /// 复活费扣的是哪种货币。为 <c>null</c>（默认）时，即便 <see cref="RespawnFee"/> 不是
        /// <see cref="RespawnFeePolicy.None"/>，也不收费——本模块不为"该扣哪种货币"这件事挑一个
        /// 隐含默认值（不同于 <c>core/gameplay/economy</c> 那种"只有一种货币"的框架级前提，死亡
        /// 复活策略本身对货币种类没有任何天然倾向）。
        /// </summary>
        public Id? RespawnFeeCurrencyId { get; set; }

        /// <summary><see cref="RespawnFeePolicy.FixedByLevel"/> 时，取该单位应付的原始（未与余额
        /// 取 min 之前）复活费委托——签名只接 <c>unitId</c>，具体"按等级查哪条曲线"完全交给注入方
        /// （<c>core/gameplay/assembly.GameplayAssembly</c> 或游戏层），本模块不关心曲线表结构/
        /// 来源（同 <see cref="ReviveUnitDelegate"/> 等既有窄委托"只关心签名，不关心实现"的一贯
        /// 判断记录）。为 <c>null</c>（未接线）时该策略下费用恒为 0——不阻断复活。</summary>
        public delegate long RespawnFeeFixedAmountDelegate(Id unitId);

        /// <summary>见 <see cref="RespawnFeeFixedAmountDelegate"/>。</summary>
        public RespawnFeeFixedAmountDelegate? RespawnFeeFixedAmount { get; set; }

        /// <summary>读取 <paramref name="unitId"/> 在 <paramref name="currencyId"/> 的当前余额的
        /// 窄委托——<see cref="DeathPolicyHost"/>（L4）不直接依赖 <c>IEconomyHost</c>（硬性规则），
        /// 经这个签名与 <c>IEconomyHost.GetBalance</c> 完全一致的委托拿到"读余额"这一窄能力，
        /// 用于（a）<see cref="RespawnFeePolicy.PctOfBalance"/> 计算原始费用本身，（b）ADR-0034
        /// 决策 6"实际费用 = min(计算值, 当前余额)"这条对两种策略都成立的夹取规则。为 <c>null</c>
        /// （未接线）时按余额恒为 0 处理——费用夹到 0，不阻断复活。</summary>
        public delegate long RespawnFeeBalanceDelegate(Id unitId, Id currencyId);

        /// <summary>见 <see cref="RespawnFeeBalanceDelegate"/>。</summary>
        public RespawnFeeBalanceDelegate? RespawnFeeBalance { get; set; }

        /// <summary>
        /// 实际执行扣费的窄委托——签名与 <c>IEconomyHost.TryPay(unitId, currencyId, amount,
        /// reason)</c> 一致（返回值不影响复活流程本身，见 <see cref="DeathPolicyHost"/> 判断
        /// 记录"复活永远不被阻断"）。<c>GameplayAssembly</c> 把
        /// <c>(unitId, currencyId, amount) =&gt; Economy.TryPay(unitId, currencyId, amount,
        /// "respawn_fee")</c> 这个闭包接给本委托。为 <c>null</c>（未接线，如未经 GameplayAssembly
        /// 的单元测试场景）时不产生任何扣费副作用，同样不阻断复活。
        /// </summary>
        public delegate bool RespawnFeeChargeDelegate(Id unitId, Id currencyId, long amount);

        /// <summary>见 <see cref="RespawnFeeChargeDelegate"/>。</summary>
        public RespawnFeeChargeDelegate? RespawnFeeCharge { get; set; }
    }
}
