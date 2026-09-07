using System;
using Core.Foundation.Common;

namespace Core.Gameplay.Spawn
{
    /// <summary>
    /// 按模板在 <paramref name="mapId"/> 的 <paramref name="position"/>/<paramref name="facing"/> 生成
    /// 一个游戏对象实例并返回运行期实体 id（组装层接
    /// <c>Core.Carriers.Gobj.GameObjectFactory.Spawn</c>，见 <see cref="SpawnOptions.GobjSpawner"/>
    /// 判断记录）。
    /// </summary>
    public delegate Id GobjSpawnerDelegate(Id templateId, Id mapId, Vec2 position, double facing);

    /// <summary>
    /// R14 根治（architecture/落地计划/audit-5e779c6-20260907）：移除一个 <c>gobj.*</c> 域实体（组装层
    /// 接 <c>Core.Carriers.Gobj.GameObjectFactory.Despawn</c>，见 <see cref="SpawnOptions.GobjDespawner"/>
    /// 判断记录），供 <c>SpawnHost.Load</c> 在同图读档需要清理"快照倒计时命中、但当前世界仍存活的
    /// 孤儿实体"时使用——与 <see cref="GobjSpawnerDelegate"/> 同款惯例（委托注入，不直接依赖
    /// <c>Core.Carriers.Gobj.GameObjectFactory</c>，该类型自身注释明确不对外暴露为 common 契约接口）。
    /// </summary>
    public delegate void GobjDespawnerDelegate(Id entityId);

    /// <summary>
    /// <see cref="SpawnHost"/> 的策略配置项 + L4/组装层回调注入点（惯例同
    /// <c>Core.Gameplay.AreaTrigger.AreaTriggerOptions</c>）。
    /// </summary>
    public sealed class SpawnOptions
    {
        /// <summary><see cref="Core.Gameplay.WorldState.IWorldState.Set"/> 的 <c>writerId</c>，默认
        /// <c>"spawn.host"</c>，用于写入 <c>once</c> 已生成标志。</summary>
        public Id WriterId { get; set; } = new Id("spawn.host");

        /// <summary>
        /// <c>condition</c> 求值时作为 <c>IExprHostFactory.CreateFor</c> 的 selfId 使用的当前玩家单位
        /// （见 05 第 5.1 节 <c>condition</c> 引用 <c>world</c> 分组、任务书拍板"玩家可空，
        /// SpawnOptions.PlayerUnitResolver"）。默认恒返回 null（未接入玩家概念的最小可用配置）。
        /// <para>
        /// 契约缺口：<c>IExprHostFactory.CreateFor(Id selfId, ...)</c> 的 <c>selfId</c> 是不可空的
        /// <see cref="Id"/>，与 05 第 5.1 节"玩家可空"的期望不完全兼容——<see cref="SpawnHost"/> 在
        /// 本委托返回 null 时改用占位 Id（见 <c>SpawnHost.NoPlayerSentinel</c> 判断记录），不是真正的
        /// "无 self"求值。这是 <c>core/rules/common.IExprHostFactory</c>（其他任务负责的目录）现有
        /// 签名的既有限制，本模块只能记录、不能修复。
        /// </para>
        /// </summary>
        public Func<Id?> PlayerUnitResolver { get; set; } = () => null;

        /// <summary>见 <see cref="GobjSpawnerDelegate"/>；<c>content_ref</c> 域名为 <c>gobj</c> 时使用。
        /// 未注入时该行生成被跳过，只记一条诊断。</summary>
        public GobjSpawnerDelegate? GobjSpawner { get; set; }

        /// <summary>见 <see cref="GobjDespawnerDelegate"/>；<c>content_ref</c> 域名为 <c>gobj</c> 时
        /// 供 <c>SpawnHost.Load</c> 移除孤儿实体使用。未注入时该次移除被跳过，只记一条诊断——与 <see
        /// cref="GobjSpawner"/> 未注入时"跳过并记诊断"同一惯例，不强制要求接入 gobj 生成能力的调用方
        /// 也一定要接入移除能力。</summary>
        public GobjDespawnerDelegate? GobjDespawner { get; set; }
    }
}
