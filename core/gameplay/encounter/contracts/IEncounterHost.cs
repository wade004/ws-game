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
        /// </summary>
        Id Start(Id encounterId, Id mapId, Id playerUnitId);

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
