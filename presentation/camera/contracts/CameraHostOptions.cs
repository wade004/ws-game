using System;
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

        /// <summary>
        /// PRES-118-CAMERA 根治（第十八轮审核）新增：<see cref="ResetFollowOnSceneLoadFinished"/>
        /// 为真时，<c>scene.load_finished</c> 把跟随目标清空之后，紧接着用本委托重新决定"接下来跟随
        /// 谁"（返回 <c>null</c> 表示维持清空、不重新指定，与改动前行为一致）。本字段本身就是
        /// "是否跟随谁"这项策略在 load_finished 时刻的可替换落点——不同游戏/不同场景可以传不同的
        /// 解析逻辑（例如固定跟随主控角色、或按场景类型切换跟随目标），也可以整体不传（<c>null</c>，
        /// 默认值）以保留"重置后由调用方自行选择何时再 <see cref="ICameraHost.Follow"/>"的原始行为。
        /// <para>
        /// 存在本字段的原因：<c>SceneRouter</c> 先执行 PostLoad 钩子、后派发 <c>scene.load_finished</c>
        /// （见 <c>core/foundation/scene_router/core/SceneRouter.cs</c>），任何"在 PostLoad 钩子里
        /// 提前 <see cref="ICameraHost.Follow"/>"的接线都会被随后到达的 load_finished 重置覆盖掉；
        /// 只有把"重新指定目标"这一步放在 <see cref="CameraHost"/> 自己响应 load_finished 的同一次
        /// 处理内、且晚于重置本身执行，才能确定性地避免这个先后顺序问题，不依赖调用方猜测钩子和事件
        /// 谁先谁后。
        /// </para>
        /// </summary>
        public Func<Core.Foundation.Common.Id?>? FollowTargetResolverOnReset { get; }

        public CameraHostOptions(
            bool resetFollowOnSceneLoadFinished = true,
            IReadOnlyDictionary<int, Core.Foundation.Common.Id>? phaseProfileSwitch = null)
            : this(resetFollowOnSceneLoadFinished, phaseProfileSwitch, followTargetResolverOnReset: null)
        {
        }

        /// <summary>PRES-118-CAMERA 根治新增的重载：额外接受 <see cref="FollowTargetResolverOnReset"/>。
        /// 独立新增一个重载而不是直接在原构造函数上加第三个可选参数——原构造函数的两参数签名已经是
        /// 已发布 ABI 表面的一部分（见 <c>docs-project/abi-strict-1.12/surface-baseline.txt</c>），
        /// 只新增不修改，保证仅链接旧签名的既有调用方二进制不受影响。</summary>
        public CameraHostOptions(
            bool resetFollowOnSceneLoadFinished,
            IReadOnlyDictionary<int, Core.Foundation.Common.Id>? phaseProfileSwitch,
            Func<Core.Foundation.Common.Id?>? followTargetResolverOnReset)
        {
            ResetFollowOnSceneLoadFinished = resetFollowOnSceneLoadFinished;
            PhaseProfileSwitch = phaseProfileSwitch;
            FollowTargetResolverOnReset = followTargetResolverOnReset;
        }
    }
}
