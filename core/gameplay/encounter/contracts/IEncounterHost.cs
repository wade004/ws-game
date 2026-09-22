using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// 遭遇模块对外契约（见 08 第 9 节契约汇总表 <c>Encounter/Level</c> 行
    /// <c>EncounterHost.start/evaluate(...)</c>）。由 <c>core/gameplay/encounter</c> 实现。
    /// </summary>
    public interface IEncounterHost
    {
        /// <summary>
        /// 开始一次遭遇：按 <c>encounter.def.units</c> 生成参战单位（<c>template_ref</c> 经
        /// <see cref="Core.Carriers.Common.ICreatureFactory.Spawn"/>，<c>spawn_ref</c> 经
        /// <see cref="SpawnRequester"/>），发出 <see cref="EncounterStartedEvent"/>，记录参与单位。
        /// 返回本次运行的实例 id（<c>encounter.inst_&lt;n&gt;</c>，全局递增，见
        /// <see cref="EncounterEventKeys"/> 判断记录"事件字段 encounterId 落地为实例 id"）。
        /// <para>
        /// ADR-0068 幂等化（消费方反馈——游戏接入方第十一批）：<paramref name="mapId"/> 上
        /// <paramref name="encounterId"/> 已经有一个进行中（<see cref="EncounterState.IsActive"/>
        /// 为 true）的实例时，本方法不再新建——不重新生成参战单位、不重新发布
        /// <see cref="EncounterStartedEvent"/>，原样返回那个已在进行中的实例 id（幂等；"进行中"
        /// 的判定口径同 <see cref="GetState"/> 的 <see cref="EncounterState.IsActive"/>，已胜利/
        /// 失败/被 <see cref="Abort"/> 的实例不算，会被当作"可以新建"处理）。不同地图上的同一
        /// <paramref name="encounterId"/> 互不影响——各自按自己的 <paramref name="mapId"/> 独立
        /// 判定。背景：<c>encounter_start</c> 类型区域触发设为 <c>one_shot: false</c>（为了死亡后
        /// 可重打）时，玩家在战斗中来回穿越触发圈，此前每次进入都会再 <see cref="Start"/> 一次，
        /// 同一遭遇因此出现多个并存的进行中实例、订阅开始事件的逻辑被反复重置，表现层出现极难
        /// 定位的间歇性问题——这正是本次幂等化要消除的场景。
        /// </para>
        /// <para>
        /// 本方法签名不变：调用方拿到的仍然只是一个 <see cref="Id"/>，无法从返回值本身区分"这次
        /// 真的新建了"还是"命中了已有实例"；需要区分时改用 <see cref="TryStart"/>。
        /// </para>
        /// </summary>
        Id Start(Id encounterId, Id mapId, Id playerUnitId);

        /// <summary>
        /// ADR-0068 补充：与 <see cref="Start"/> 语义完全相同（内部走同一份判定，不是另一套独立
        /// 实现），额外把"这次是新建还是命中了已有的进行中实例"用返回值显式暴露给调用方——
        /// <paramref name="instanceId"/> 与 <see cref="Start"/> 返回值同一枚约定（新建时是新分配的
        /// 实例 id，命中已有实例时是那个已在进行中的实例 id）。
        /// <para>
        /// C# 8 默认接口成员：本默认实现只能委托给 <see cref="Start"/>（接口的既有必须实现成员，
        /// 任何 <see cref="IEncounterHost"/> 实现方都具备）并恒返回
        /// <see cref="EncounterStartResult.Started"/>——与 <c>ISkillHost</c> 那类"写路径默认实现
        /// 不能悄悄制造假成功"的判断记录（见 <c>Core.Rules.Common.ISkillHost.LearnSkill</c>）不是
        /// 同一种处境：本方法不是凭空新增的能力，它做的事（开始一次遭遇）<see cref="Start"/> 本来
        /// 就必须支持，默认实现只是老老实实调用那个既有的必须实现成员，不产生"看似成功、实际什么
        /// 都没发生"的假象；它欠缺的只是"识别出命中了已有实例"这一层锦上添花的判定能力（需要访问
        /// 具体实现方的内部实例账本，接口层面拿不到），因而降级为"总是报告 Started"——旧的
        /// <see cref="IEncounterHost"/> 实现方（未升级的自定义实现、旧版本编译产物）由此保持与
        /// 升级前完全一致的行为（每次调用都真的新建），不会退化出错误结果。生产实现
        /// <see cref="EncounterHost"/> 用显式接口实现覆盖，转发到与 <see cref="Start"/> 共用的同一份
        /// 判定逻辑，真正区分两种结果（避免隐式实现把 <see cref="Start"/> 现有的普通公开方法物理
        /// 签名悄悄改写成 <c>virtual sealed</c>，被 <c>toolchain/abi_surface</c> 静态签名比对误判为
        /// 破坏——同 <c>ISkillHost</c> 系列默认接口成员判断记录里反复用到的手法）。任何组合/包装
        /// <see cref="IEncounterHost"/>（若存在）都应显式转发到内层实现，不应悄悄吃掉这个降级
        /// 默认值——同 <c>ISkillHost.GetSkillReadiness</c> 判断记录"框架内
        /// InterfaceDefaultMemberForwardingTests 门禁"。
        /// </para>
        /// </summary>
        EncounterStartResult TryStart(Id encounterId, Id mapId, Id playerUnitId, out Id instanceId)
        {
            instanceId = Start(encounterId, mapId, playerUnitId);
            return EncounterStartResult.Started;
        }

        /// <summary>
        /// 推进一次实例的判定（08 第 4.3 节；由 <see cref="EncounterTickHandler"/> 在
        /// <c>TickPhase.TriggerEvaluation</c> 阶段逐实例调用，也可在测试中直接调用回放）：
        /// 依次检查未触发波次的 <c>trigger_condition</c>、按声明顺序单调检查下一阶段的
        /// <c>enter_condition</c>（一次 <see cref="Evaluate"/> 至多推进一个阶段）、
        /// <c>arena_rules.reset_if_leave</c> 场地边界、<c>victory_condition</c>/
        /// <c>defeat_condition</c>。<paramref name="instanceId"/> 不存在或该实例已结束
        /// （<see cref="GetState"/> 的 <see cref="EncounterState.IsActive"/> 为 false）时空操作。
        /// </summary>
        void Evaluate(Id instanceId);

        /// <summary>强制中止一个实例（如场景卸载/玩家退出）：标记为不再活跃，此后
        /// <see cref="Evaluate"/> 空操作；不销毁参战单位，不发任何事件（任务书未规定 Abort 的
        /// 具体收尾行为，见 README 判断记录，具体清理交给调用方按需处理）。</summary>
        void Abort(Id instanceId);

        /// <summary>
        /// 按地图批量终止（GP-04 判断记录，architecture/落地计划/audit-b3b91ee-20260907/
        /// code-review.md）：把 <paramref name="mapId"/> 下全部仍活跃的实例标记为不再活跃（语义
        /// 同对每一个逐个调用 <see cref="Abort"/>），用于地图切换——离开一张地图时，绑定在该地图上
        /// 的遭遇不应继续在新地图的上下文里被 <see cref="Evaluate"/>（波次/胜负条件引用的单位位置、
        /// 场地边界等都已不再对应新地图）。不销毁参战单位、不发 <see cref="EncounterWonEvent"/>/
        /// <see cref="EncounterLostEvent"/>（离开地图不是"打赢/打输"）。返回本次实际终止的实例 id
        /// 列表（未处于活跃状态或不属于该地图的实例不计入），供调用方（<see cref="ILevelHost"/>）
        /// 据此清理自己关联的运行记录。
        /// </summary>
        IReadOnlyList<Id> AbortForMap(Id mapId);

        /// <summary>查询一个实例当前的阶段/波次/活跃状态快照。<paramref name="instanceId"/> 不存在时抛
        /// <see cref="System.ArgumentException"/>。</summary>
        EncounterState GetState(Id instanceId);

        /// <summary>任务书未列出、供 <see cref="EncounterTickHandler"/> 枚举需要驱动的实例（拍板补充，
        /// 见 README"契约缺口"一节）：全部仍活跃（<see cref="EncounterState.IsActive"/> 为 true）的
        /// 实例 id，按 <see cref="Start"/> 调用顺序排列。</summary>
        IReadOnlyList<Id> ActiveInstanceIds { get; }
    }
}
