using Core.Foundation.Common;

namespace Core.Gameplay.Loot
{
    /// <summary>
    /// ADR-0062：<c>interact</c> 意图原生分流（<see cref="LootHost.Interact"/>）尝试拾取地面掉落物前
    /// 的权限判定回调——是否放行由具体游戏决定，本模块不解释 <paramref name="ownerHint"/> 的含义（见
    /// <see cref="LootOptions.PickupPermissionChecker"/> 判断记录）。返回 <c>true</c> 放行、
    /// <c>false</c> 拒绝（<see cref="LootHost.Interact"/> 返回
    /// <see cref="LootPickupFailureReason.PermissionDenied"/>）。
    /// </summary>
    public delegate bool LootPickupPermissionDelegate(Id unitId, Id lootInstanceId, Id? ownerHint);

    /// <summary>拾取时背包已满的处理策略（见 08 第 1.2 节"拾取动作直接调用 07 的
    /// InventoryHost.addItem，背包已满时的处理策略……为策略配置项"）。与
    /// <c>Core.Carriers.Item.InventoryFullPolicy</c>（单次 <c>AddItem</c> 调用内部"这一批数量放不下
    /// 怎么办"）是两个不同层面的策略：本枚举描述的是"一次 <see cref="LootHost.PickUp"/> 调用要处理
    /// 多个物品堆叠时，其中某一堆放不下该怎么对待整次拾取"，由 <see cref="LootHost"/> 在调用
    /// <c>IInventoryHost.AddItem</c> 之上再加一层判断（见 <see cref="LootHost"/> 判断记录）。</summary>
    public enum LootPickupPolicy
    {
        /// <summary>只要有任意一个物品堆叠放不下（部分或全部），整次拾取不生效：已经放入背包的
        /// 堆叠会被撤回，地面掉落物保持不变，返回失败。</summary>
        Reject,

        /// <summary>能拿多少拿多少：放不下的部分（或整堆）留在地面掉落物上，只有全部拾完才销毁地面
        /// 掉落物实体。</summary>
        Partial,
    }

    /// <summary>
    /// <see cref="LootHost"/> 的构造期策略配置（见 08 第 9 节 Loot 行"策略配置项：伪随机启用与曲线、
    /// 拾取满背包策略"）。
    /// </summary>
    public sealed class LootOptions
    {
        /// <summary>掉落判定唯一使用的 <c>IRngHost</c> 流（见任务书"随机只经
        /// IRngHost.Next(LootOptions.RngStream="loot.roll")"）。</summary>
        public Id RngStream { get; set; } = new Id("loot.roll");

        /// <summary>是否启用伪随机分布（见 08 第 1.1 节"（建议）对稀有条目引入……伪随机分布机制"）：
        /// 关闭时（默认）每次判定的实际概率恒等于 <c>LootEntry.WeightOrChance × context.Multiplier</c>；
        /// 开启时叠加 <see cref="PseudoRandomStep"/> 描述的"未掉落次数累积"加成，见
        /// <see cref="LootHost"/> 判断记录（本模块自行拍板的具体曲线，架构原文只登记
        /// <c>guaranteed_min</c> 作为保底挂载点，不规定伪随机曲线本身）。仅对
        /// <c>roll_mode=chance_each</c> 的条目生效——<c>weighted_pick_one</c> 组内条目的相对权重
        /// 已经是"抽中即出"的语义，不存在"连续多次判定不中"的概念，伪随机加成不适用。</summary>
        public bool PseudoRandom { get; set; }

        /// <summary>伪随机启用时，每次判定未中后叠加到下次有效概率的比例（线性递增：第 N 次连续未中后，
        /// 有效概率 = <c>基础概率 × (1 + N × PseudoRandomStep)</c>，封顶 1）。默认 0.1（未中 10 次后
        /// 概率翻倍）。</summary>
        public double PseudoRandomStep { get; set; } = 0.1;

        /// <summary>嵌套 <c>loot.table</c> 引用的递归深度上限（见 08 第 1.1 节"支持嵌套引用"、任务书
        /// "递归（深度上限 8）"）；超过时静默停止该分支的展开（不掉落、不抛异常）——正常内容应已被
        /// <see cref="LootContentValidationRule"/> 的成环检测拦截，这里只是运行期兜底防御。</summary>
        public int MaxNestedDepth { get; set; } = 8;

        /// <summary>拾取距离上限（见 08 第 1.2 节"DroppedLoot 与拾取"、05 第 1.6 节），默认 3。</summary>
        public double PickupRange { get; set; } = 3.0;

        /// <summary>拾取时背包已满的整体处理策略，见 <see cref="LootPickupPolicy"/>，默认
        /// <see cref="LootPickupPolicy.Partial"/>（"能拿多少拿多少"更贴近多数 ARPG 的默认体验，且
        /// 不会因为背包只差一个格子就让整批掉落物连同已经能装下的部分一起作废）。</summary>
        public LootPickupPolicy FullPolicy { get; set; } = LootPickupPolicy.Partial;

        /// <summary><see cref="DroppedLootEntity"/> 的默认存活时长（游戏内秒数）；0 表示不过期
        /// （见 05 第 1.6 节 <c>expireAt</c> 为 <c>Optional&lt;Number&gt;</c>）。默认 0（不过期）——
        /// 08 第 1.2 节把"背包已满的处理策略"列为策略配置项，但未同等强调掉落物必须过期，保守取默认
        /// 不过期，避免内容作者忘记配置导致地面掉落物意外消失。</summary>
        public double DefaultLifetime { get; set; }

        /// <summary>掉落地面实体是否随存档持久化（见 05 第 1.6 节"（建议）随当前地图状态一并存档"）。
        /// 为 true 时由组装层注册 <see cref="DroppedLootPersistable"/>（段 <c>world.dropped_loot</c>，
        /// 本模块不自动注册——同 <c>core/gameplay/world_state</c> 的既有惯例，见该模块 README"不负责
        /// 什么"一节：具体挂接存档段是组装期的事）。默认 true。</summary>
        public bool PersistDropped { get; set; } = true;

        /// <summary>
        /// ADR-0062（消费方反馈第五批第 1 条）：<see cref="LootHost.Interact"/>（<c>interact</c> 意图
        /// 原生分流，与 <c>core/carriers/gobj</c>/<c>core/carriers/creature</c> 两条既有分流同构）
        /// 在判距、判存在之后、真正调用 <see cref="LootHost.PickUp"/> 之前的权限接缝。
        /// <para>
        /// 判断记录（默认放行，不新造归属系统）：<see cref="DroppedLootEntity.OwnerHint"/> 类型注释
        /// 明确写着"本架构单机场景无实际分配意义，仅为兼容掉落生成管线数据结构而保留"——本模块从未
        /// 实现过任何"谁能拾取"的判定，<see cref="LootHost.PickUp"/>（既有签名，不改）本身也不检查
        /// 它。核对 <c>core/carriers/gobj.GameObjectHost.Interact</c> 同一"判归属"接缝发现同样不存在
        /// （该方法唯一的准入门槛是数据驱动的 <c>LockDef</c>/<c>CheckRequirement</c>，语义是"锁"不是
        /// "归属"）：本仓库目前没有一处 interact 分流真正实现过身份/队伍归属判定。据此没有旧概念可
        /// 复用，也不擅自发明一套（掉落物没有"锁"这种数据驱动准入门槛可比照）；改用本仓库贯穿
        /// <c>GobjOptions</c>/<c>CreatureInteractOptions</c> 的既有惯例——L4 回调注入点，未注入
        /// （默认 <c>null</c>）时默认放行，把"谁能拾取"这个判断完全留给具体游戏在组装期注入。回调
        /// 携带 <c>ownerHint</c>（即 <see cref="DroppedLootEntity.OwnerHint"/>）作为上下文供游戏自行
        /// 解释（队伍/个人归属等），本模块自身不赋予它任何强制语义，与该字段类型注释一贯口径一致。
        /// </para>
        /// </summary>
        public LootPickupPermissionDelegate? PickupPermissionChecker { get; set; }
    }
}
