namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// <see cref="IEncounterHost.TryStart"/> 的结果码（ADR-0068《遭遇开始幂等化：同一地图上同一
    /// 遭遇定义已有进行中实例时 Start 不再新建》，消费方反馈——游戏接入方第十一批）。区分"这次调用
    /// 真的创建了一个新实例"与"这次调用命中了已有的进行中实例、原样把它返回"，供调用方按需区分
    /// 处理（如只在真正创建时才播放"遭遇开始"演出，命中已有实例时静默）；<see cref="IEncounterHost.Start"/>
    /// 本身签名不变，两种结果都返回同一个实例 id，只是不通过返回值本身暴露是否新建。
    /// </summary>
    public enum EncounterStartResult
    {
        /// <summary>本次调用创建了一个新实例（按 <c>encounter.def.units</c> 生成参战单位、发布
        /// <see cref="EncounterStartedEvent"/>）。</summary>
        Started,

        /// <summary>同一地图上同一遭遇定义已经有一个进行中（<see cref="EncounterState.IsActive"/>
        /// 为 true）的实例——本次调用不新建、不重新生成参战单位、不重新发布
        /// <see cref="EncounterStartedEvent"/>，原样返回那个已在进行中的实例 id（幂等）。</summary>
        AlreadyActive,
    }
}
