using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Spawn
{
    /// <summary>
    /// 刷新与重生契约（见 05_对象模型与世界.md 第 5.3 节 <c>SpawnHost</c>）。<see cref="Update"/>/
    /// <see cref="TriggerNever"/>/<see cref="UnloadMap"/> 是 05 原文接口之外任务书拍板补充的成员
    /// （惯例同 <c>Core.Gameplay.AreaTrigger.IAreaTriggerHost</c>）。
    /// </summary>
    public interface ISpawnHost
    {
        /// <summary>
        /// 按 05 第 5.2 节四种策略结合 <see cref="Core.Gameplay.WorldState.IWorldState"/> 计算
        /// <paramref name="mapId"/> 当前应存在的刷新点实例并生成，返回本次实际生成的实体 id 列表
        /// （见 05 第 5.3 节 <c>applyForMap</c>）。供场景加载完成后调用；同一地图可重复调用（如
        /// 玩家反复出入同一地图），<c>on_map_enter</c> 策略每次调用都会按当前状态重新判定。
        /// </summary>
        IReadOnlyList<Id> ApplyForMap(Id mapId);

        /// <summary>实例死亡或被清除时回调（见 05 第 5.3 节 <c>notifyDespawn</c>），
        /// <paramref name="reason"/> 是自由文本分类（惯例同
        /// <c>Core.Carriers.Common.ICreatureFactory.Despawn</c>）。<c>timer</c> 策略据此开始计时；
        /// 非本模块登记的 <paramref name="entityId"/>（如手工放置对象）视为空操作。</summary>
        void NotifyDespawn(Id entityId, string reason);

        /// <summary>查询某刷新点当前的记录状态（见 05 第 5.3 节 <c>getSpawnRecord</c>）；未登记的
        /// <paramref name="spawnId"/> 返回 null。</summary>
        SpawnRecord? GetSpawnRecord(Id spawnId);

        /// <summary><c>timer</c> 策略的计时推进（任务书拍板补充）：全部处于计时中的刷新点剩余时间
        /// 减去 <paramref name="dt"/>，归零且所属地图仍加载（<see cref="ApplyForMap"/> 之后、
        /// <see cref="UnloadMap"/> 之前）时立即重新生成。</summary>
        void Update(double dt);

        /// <summary>脚本/对话显式触发一次生成（常用于 <c>never</c> 策略行，见 05 第 5.2 节该策略
        /// "只能由脚本钩子或对话动作显式触发生成"）；当前已有存活实例或 <c>condition</c> 不满足时
        /// 只记一条诊断，不重复生成。</summary>
        void TriggerNever(Id spawnId);

        /// <summary>场景卸载时调用：清理本模块对 <paramref name="mapId"/> 下各刷新点持有的运行期
        /// 实例引用（不销毁实体本身，见 <see cref="SpawnHost"/> 判断记录），保留
        /// <see cref="SpawnRecord.RespawnRemaining"/>/<see cref="SpawnRecord.SpawnCount"/>（任务书
        /// 拍板）。</summary>
        void UnloadMap(Id mapId);
    }
}
