using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// VFX anchor/socket 持续跟随的可选引擎能力（第十四轮审核"VFX anchor/socket 未持续跟随"根治，
    /// 见 09_表现层.md 第 5.3 节"跟随"勘误）：<c>core/foundation/engine_adapter</c> 的
    /// <see cref="IRenderer2D"/> 契约（02 号文档定义的 L-1 最小原语）只有
    /// <see cref="IRenderer2D.EmitParticle"/>/<see cref="IRenderer2D.StopParticle"/>，没有"移动一个
    /// 已经在播的粒子实例"这一原语——<see cref="VfxPlayer"/> 因此此前只能在 <c>Spawn</c> 那一刻算出
    /// 一次世界坐标，此后即便挂接目标（<c>attach_mode: anchor</c> 的锚点、<c>socket</c> 降级为 world
    /// 时的实体本身）持续移动，已发射的粒子也不会跟着挪动。
    /// <para>
    /// 本接口不改 <see cref="IRenderer2D"/> 契约本身（那是 <c>core/</c> 范围，改契约意味着全部引擎
    /// 适配层实现都要跟着改，超出本次任务边界），而是表现层（L5）按需探测的可选扩展能力——同
    /// <c>Presentation.Common.IModelHandleProvider</c>/<c>IAnchorQuery</c> 一贯的"能力接口"惯例：
    /// 具体引擎适配层的 <see cref="IRenderer2D"/> 实现若能支持重新定位已发射的粒子实例（如 Unity 侧
    /// 直接改已创建 GameObject 的 <c>Transform.position</c>，见
    /// <c>Adapter.Unity.EngineAdapter.UnityRenderer2D</c>），可以让该实现类同时实现本接口；
    /// <see cref="VfxPlayer"/> 构造期以 <c>(_renderer2D as IParticleRepositioner)</c> 探测，未实现
    /// 本接口的引擎适配层（含全部既有测试用 <c>StubRenderer2D</c>）保持"生成后静止在初始位置"的
    /// 改动前行为，不抛异常、不产生任何副作用（<see cref="VfxPlayer"/> 判断记录"未装配的能力静默
    /// 跳过"一贯惯例）。
    /// </para>
    /// </summary>
    public interface IParticleRepositioner
    {
        /// <summary>把 <paramref name="handle"/>（<see cref="IRenderer2D.EmitParticle"/> 返回的粒子
        /// 实例句柄）移动到 <paramref name="position"/>。<paramref name="handle"/> 对应的实例已经
        /// 因为 <see cref="IRenderer2D.StopParticle"/>/自然播完而不存在时静默忽略，不抛异常——同
        /// <see cref="IRenderer2D.StopParticle"/> 对已停止句柄的宽容惯例一致（<see cref="VfxPlayer"/>
        /// 只在句柄仍被自己跟踪期间调用本方法，正常不会出现这种情形，这里是给引擎适配层实现的
        /// 防御性契约要求，不是本接口预期的常见路径）。</summary>
        void SetParticlePosition(ParticleHandle handle, Vec2 position);
    }
}
