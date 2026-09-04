using Core.Foundation.Common;
using Core.Foundation.SceneRouter;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>见 <see cref="AreaTriggerOptions.MapTransitionRequested"/>：<paramref name="spawnPoint"/>
    /// 是 <c>params.spawn_point</c>，可空。</summary>
    public delegate void MapTransitionRequestedDelegate(Id unitId, Id targetMap, Id? spawnPoint);

    /// <summary>见 <see cref="AreaTriggerOptions.EncounterStartRequested"/>。</summary>
    public delegate void EncounterStartRequestedDelegate(Id unitId, Id encounterRef);

    /// <summary>见 <see cref="AreaTriggerOptions.TrapTrigger"/>（组装层接
    /// <c>Core.Carriers.Gobj.GameObjectHost.TriggerTrap</c>）。</summary>
    public delegate void TrapTriggerDelegate(Id gobjInstanceId, Id unitId);

    /// <summary>
    /// <see cref="AreaTriggerHost"/> 的策略配置项 + L4/组装层回调注入点（惯例同
    /// <c>Core.Carriers.Gobj.GobjOptions</c>：本模块不直接依赖 <c>encounter</c>/<c>quest</c> 等其它
    /// L4 模块或具体游戏组装细节，改用委托回调）。
    /// </summary>
    public sealed class AreaTriggerOptions
    {
        /// <summary><see cref="Core.Gameplay.WorldState.IWorldState.Set"/> 的 <c>writerId</c>（见 05
        /// 第 8.2 节"每次写入必须带 writerId"），默认 <c>"area_trigger.host"</c>，用于写入
        /// <c>one_shot</c> 已触发标志。</summary>
        public Id WriterId { get; set; } = new Id("area_trigger.host");

        /// <summary>
        /// 见 05 第 7 节 <c>map_transition</c> 行；未注入时该类型触发只记一条诊断、不产生任何跨地图
        /// 效果（判断记录：<c>spawn_point</c> 的落地——具体传送到哪个出生点——不属于 05 第 6.1 节
        /// <c>ISceneRouter</c> 现有签名覆盖范围，本模块只把 <c>spawn_point</c> 原样透传给本委托，由
        /// 组装层结合 <c>post_load</c> 钩子完成实际落点）。
        /// </summary>
        public MapTransitionRequestedDelegate? MapTransitionRequested { get; set; }

        /// <summary>
        /// 除 <see cref="MapTransitionRequested"/> 外，若同时注入本项，<c>map_transition</c> 触发
        /// 还会直接调用 <see cref="ISceneRouter.LoadScene"/>（见 05 第 7 节"经场景路由"、03 第 6
        /// 节）。两者可任选其一或同时注入：只注入本项时不经委托；只注入委托时不发起场景路由加载
        /// （供组装层自行决定加载时机，如先弹出确认 UI 再加载）。<see cref="ISceneRouter.LoadScene"/>
        /// 抛出的异常（如加载正在进行中）被捕获并记一条诊断，不中断 <see cref="AreaTriggerHost.Evaluate"/>
        /// 本次循环的其余触发体处理。
        /// </summary>
        public ISceneRouter? SceneRouter { get; set; }

        /// <summary>见 05 第 7 节 <c>encounter_start</c> 行；未注入时该类型触发只记一条诊断。</summary>
        public EncounterStartRequestedDelegate? EncounterStartRequested { get; set; }

        /// <summary>见 07 第 3.1 节 <c>trap</c> 行、<see cref="IAreaTriggerHost.RegisterTrap"/>；未注入
        /// 时陷阱触发只记一条诊断，不释放任何技能。</summary>
        public TrapTriggerDelegate? TrapTrigger { get; set; }
    }
}
