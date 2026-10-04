using System;
using Core.Foundation.Common;
using Core.Foundation.Feel;

namespace Presentation.Render
{
    /// <summary>起步/急停混合时长来源（ADR-0147）：状态切换 -> 混合时长（秒）；<see cref="LocomotionBlends"/> 是框架实现。</summary>
    public interface ILocomotionBlendSource
    {
        bool TryGetBlendSeconds(Id entityId, AnimState from, AnimState to, out double seconds);
    }

    /// <summary>
    /// 起步/急停的剪辑混合时长（手感设计/02 第 7 节、04 第 3 节，ADR-0147）：从待机切到移动取呈现型字段 <c>start_blend_ms</c>，
    /// 从移动切回待机取 <c>stop_blend_ms</c>（毫秒 → 秒，0 = 硬切）。其它状态切换不返回（走姿势集声明与默认）。
    /// 纯呈现、只读呈现型视图。只对 model 型骨骼剪辑有意义（序列帧播放器不做交叉淡入，装配层对 sprite 型不调用）。
    /// <para>
    /// 判断记录：优先级是"作者对该对键的显式声明 &gt; 本类型给出的档案混合 &gt; 目标剪辑逐键声明 &gt; 默认"（见 <see cref="ModelCharacterRig.SetNextBlendSeconds"/>）——
    /// 档案字段是起停过渡的专用调参入口，姿势集里给每个键写的 <c>blend_ms</c> 是通用的"切入本剪辑"默认，二者同时存在时前者更具体；
    /// 作者若对某一对键（如 <c>idle → move.run</c>）显式写了 <c>blends</c>，那是比档案更具体的指定，仍然优先。
    /// 姿势集存在专门的过渡剪辑（<c>move.start</c>/<c>move.stop</c>）时由姿势解析选择，本类型不介入。
    /// </para>
    /// </summary>
    public sealed class LocomotionBlends : ILocomotionBlendSource
    {
        private readonly IFeelPresentingSource _feel;

        public LocomotionBlends(IFeelPresentingSource feel)
        {
            _feel = feel ?? throw new ArgumentNullException(nameof(feel));
        }

        /// <summary>
        /// <paramref name="from"/> → <paramref name="to"/> 是起步（Idle→Move）或急停（Move→Idle）且档案声明了对应字段时返回 true，
        /// <paramref name="seconds"/> 为混合时长（秒）；否则返回 false。
        /// </summary>
        public bool TryGetBlendSeconds(Id entityId, AnimState from, AnimState to, out double seconds)
        {
            seconds = 0.0;
            string field;
            if (from == AnimState.Idle && to == AnimState.Move) field = FeelFieldNames.StartBlendMs;
            else if (from == AnimState.Move && to == AnimState.Idle) field = FeelFieldNames.StopBlendMs;
            else return false;

            var view = _feel.ResolvePresenting(entityId);
            if (view == null || !view.Contains(field)) return false;
            var value = view.GetRaw(field);
            if (value.Kind != FeelValueKind.Number) return false;
            seconds = Math.Max(0.0, value.AsNumber()) / 1000.0;
            return true;
        }
    }
}
