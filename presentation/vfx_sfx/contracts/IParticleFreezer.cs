using Core.Foundation.EngineAdapter;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 粒子实例暂停/恢复的可选引擎能力（手感落地 M3-C，手感设计/07 第 5 节"粒子与拖尾按反馈包 <c>freeze_layers</c> 决定"）：
    /// <see cref="IRenderer2D"/> 契约只有 <c>EmitParticle</c>/<c>StopParticle</c>，没有"暂停一个已经在播的粒子实例"这一原语。
    /// 同 <see cref="IParticleRepositioner"/> 的做法，不改 <see cref="IRenderer2D"/>，而是表现层按需探测的可选能力：引擎适配层的
    /// <see cref="IRenderer2D"/> 实现若能暂停（Unity 侧暂停 <c>ParticleSystem</c>/序列帧播放器），让该实现类同时实现本接口；
    /// <see cref="Presentation.VfxSfx.Core.VfxPlayer"/> 构造期以 <c>(_renderer2D as IParticleFreezer)</c> 探测，未实现的适配层（含既有 <c>StubRenderer2D</c>）
    /// 不抛异常；此时视觉上无法暂停已发射的 2D 粒子，但 <see cref="Presentation.VfxSfx.Core.VfxPlayer"/> 仍停住这些特效的存活倒计时（不依赖本接口），解冻后从冻结点继续。
    /// </summary>
    public interface IParticleFreezer
    {
        /// <summary>
        /// 暂停（<paramref name="paused"/> 为真）或恢复 <paramref name="handle"/> 对应粒子实例的时间轴，幂等（重复暂停/恢复无副作用，
        /// 恢复从暂停点继续）。<paramref name="handle"/> 已停止/自然播完/不存在时静默忽略，不抛异常（同
        /// <see cref="IParticleRepositioner.SetParticlePosition"/> 的防御性契约）。
        /// </summary>
        void SetParticlePaused(ParticleHandle handle, bool paused);
    }
}
