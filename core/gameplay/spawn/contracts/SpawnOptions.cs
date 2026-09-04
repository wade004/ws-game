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
    }
}
