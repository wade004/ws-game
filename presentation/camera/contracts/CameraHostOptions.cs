using System.Collections.Generic;

namespace Presentation.Camera
{
    /// <summary>
    /// <see cref="CameraHost"/> 的策略配置项（见 09 第 8 节"镜头切换……由玩法层通过事件/钩子触发"、
    /// 01 L5 模块表 <c>camera</c> 行"策略配置项：跟随策略、边界规则"）。
    /// </summary>
    public sealed class CameraHostOptions
    {
        /// <summary>是否在收到 <c>scene.load_finished</c> 时重置跟随目标（见 09 第 8 节、
        /// 03 第 6 节"镜头……据此各自完成初始化"）。默认 true。</summary>
        public bool ResetFollowOnSceneLoadFinished { get; }

        /// <summary>
        /// <c>encounter.phase_changed</c> 的 <c>newPhase</c>（阶段下标）到应切换的 profileId 的
        /// 映射（"可选切档，配置"，见任务书拍板）；未提供或某个阶段没有对应表项时不切档。
        /// </summary>
        public IReadOnlyDictionary<int, Core.Foundation.Common.Id>? PhaseProfileSwitch { get; }

        public CameraHostOptions(
            bool resetFollowOnSceneLoadFinished = true,
            IReadOnlyDictionary<int, Core.Foundation.Common.Id>? phaseProfileSwitch = null)
        {
            ResetFollowOnSceneLoadFinished = resetFollowOnSceneLoadFinished;
            PhaseProfileSwitch = phaseProfileSwitch;
        }
    }
}
