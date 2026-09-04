using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 特效播放器（见 09_表现层.md 第 5.3 节 <c>VfxPlayer</c>）：按 <c>vfx.def</c> 查表并经
    /// <see cref="Core.Foundation.EngineAdapter.IRenderer2D"/>/<see cref="Core.Foundation.EngineAdapter.IRenderer3D"/>/
    /// <see cref="Core.Foundation.EngineAdapter.ICamera"/> 播放（表现层铁律 P4，只经 L-1 接口绘制）。
    /// </summary>
    public interface IVfxPlayer
    {
        /// <summary>播放一条特效；<paramref name="vfxId"/> 未在 <c>vfx.def</c> 登记、或
        /// <paramref name="at"/> 的挂接目标不可解析时返回 null（记一条诊断，不抛异常）。
        /// <paramref name="parameters"/> 供 <c>IRenderer2D.EmitParticle</c> 的着色器/发射参数
        /// 透传，可为 null（等价空字典）。</summary>
        ParticleHandle? Spawn(Id vfxId, VfxAttach at, IReadOnlyDictionary<string, double>? parameters);

        /// <summary>提前停止一次播放；<paramref name="handle"/> 不存在/已停止时安全忽略。</summary>
        void Stop(ParticleHandle handle);

        /// <summary>按 <paramref name="dt"/> 推进对象池：到达 <c>vfx.def.lifetime</c> 的播放实例
        /// 自动回收（见 09 第 5.4 节对象池"按 lifetime 超时回收"）。</summary>
        void Update(double dt);
    }
}
