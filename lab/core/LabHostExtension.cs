using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Sim;
using Presentation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.VfxSfx.Contracts;
using Presentation.ViewBinding;

namespace Lab
{
    /// <summary>
    /// 宿主扩展点的上下文（06 第 4 节"引擎宿主"）：扩展在装配完成后一次性拿到本次运行的世界、格子、出场标签与记录，
    /// 不必再向宿主要内部状态。上下文里的集合只读；世界是同一个 <see cref="HeadlessWorld"/>（扩展只许读，覆盖层等
    /// 调试入口除外，见 <see cref="OverrideExtension"/>）。
    /// </summary>
    public sealed class LabHostContext
    {
        public HeadlessWorld World { get; }

        public LabScenario Cell { get; }

        public InputScript Script { get; }

        public LabRecording Recording { get; }

        /// <summary>固定步长（秒）。</summary>
        public double StepSeconds { get; }

        /// <summary>帧长（秒），即 <c>1 / frameRateCap</c>。</summary>
        public double FrameSeconds { get; }

        public Id PlayerId { get; }

        /// <summary>实体 id → 出场标签（玩家 <c>player</c>，靶子取靶子集条目名）。</summary>
        public IReadOnlyDictionary<Id, string> Labels { get; }

        /// <summary>出场靶子（标签 → 实体 id），按出场顺序。</summary>
        public IReadOnlyList<KeyValuePair<string, Id>> Dummies { get; }

        /// <summary>宿主用的外形登记（与 <see cref="ViewBinder"/> 读的是同一个）。</summary>
        public IDisplayInfoRegistry DisplayInfo { get; }

        /// <summary>手感装配是否开着（脚本带 <c>feel</c> 且变体没关）；关着时打击反馈流水线不存在，<see cref="LabHostExtension.FeedbackTee"/> 不会被取用。</summary>
        public bool FeelOn { get; }

        internal LabHostContext(
            HeadlessWorld world, LabScenario cell, InputScript script, LabRecording recording, double step, double frameSeconds,
            Id playerId, IReadOnlyDictionary<Id, string> labels, IReadOnlyList<KeyValuePair<string, Id>> dummies,
            IDisplayInfoRegistry displayInfo, bool feelOn)
        {
            World = world;
            Cell = cell;
            Script = script;
            Recording = recording;
            StepSeconds = step;
            FrameSeconds = frameSeconds;
            PlayerId = playerId;
            Labels = labels;
            Dummies = dummies;
            DisplayInfo = displayInfo;
            FeelOn = feelOn;
        }

        /// <summary>出场标签（玩家 <c>player</c>）对应的实体 id；没有该标签返回 null。</summary>
        public Id? FindByLabel(string label)
        {
            foreach (var pair in Labels)
            {
                if (string.Equals(pair.Value, label, StringComparison.Ordinal))
                {
                    return pair.Key;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 实验室宿主扩展点：引擎宿主、覆盖层应用等在不复制宿主逐步顺序的前提下接入同一次运行。
    /// <para>
    /// 判断记录（为什么是扩展点而不是再写一份宿主）：06 第 4 节要求引擎宿主复用内核的脚本、格子、度量与指纹定义、逻辑组与无头宿主
    /// 逐字节一致。若引擎侧另写一份逐步顺序，两份宿主的任何细微漂移都会变成"逻辑组不一致"的假阳性或假阴性；因此引擎宿主就是
    /// 同一个 <see cref="LabHost.Run(LabHostOptions, LabScenario, InputScript, LabCatalog?, LabRunVariant?, LabHostExtension?)"/>，
    /// 只在固定的几处接入点上叠加引擎侧的表现驱动。扩展点只能读世界、不能写世界的逻辑状态（唯一例外是调试覆盖层，它本来就是第 8 层
    /// 的写入口）；"引擎侧表现驱动不回流逻辑"由跨宿主逻辑组逐字节一致来证明。缺省（不传扩展）时行为与引入前逐位一致。
    /// </para>
    /// <para>调用顺序：<see cref="OnAttach"/> → <see cref="WrapViewFactory"/> → 取 <see cref="FeedbackTee"/> → <see cref="OnReady"/>（循环之前）→
    /// 每固定步 <see cref="OnMoveAxis"/>（脚本轴事件）/<see cref="OnFixedStepEnd"/> → 每帧 <see cref="OnFrame"/> → <see cref="OnFinished"/>。</para>
    /// </summary>
    public abstract class LabHostExtension
    {
        /// <summary>世界、外形登记与出场标签都就绪后调用一次（视图工厂创建之前）。</summary>
        public virtual void OnAttach(LabHostContext context)
        {
        }

        /// <summary>包装视图工厂：返回的工厂替代宿主的记录型假工厂交给 <see cref="ViewBinder"/>；扩展应转发给 <paramref name="inner"/>，
        /// 这样表现时间线（<see cref="LabRecording.Frames"/>）仍来自记录型假视图，与无头宿主逐位一致。</summary>
        public virtual IViewFactory WrapViewFactory(IViewFactory inner, LabHostContext context) => inner;

        /// <summary>手感装配开着时，打击反馈流水线出批的每条指令除记入表现时间线外，还转发给它（引擎侧镜头冲量、顿帧冻结、特效/音效的落地）。</summary>
        public virtual IFeedbackSink? FeedbackTee => null;

        /// <summary>全部装配完成、第一个固定步之前调用（应用调试覆盖等）。</summary>
        public virtual void OnReady(LabHostContext context)
        {
        }

        /// <summary>
        /// 相机朝向查询（框架原生相机相对控制空间的来源，<c>InputMapOptions.CameraOrientation</c>）：非 null 且控制空间为 <c>camera_relative</c> 时，
        /// 宿主把移动动作声明成 <c>camera_relative</c>，由输入映射按相机偏航换算轴值——宿主与扩展都不再自己换算。缺省 null：
        /// 本宿主不证明相机相对输入，格子按 <c>world</c> 跑（无头宿主的既有行为）。在 <see cref="OnAttach"/> 之后读取。
        /// </summary>
        public virtual Core.Foundation.EngineAdapter.ICameraOrientation? CameraOrientation => null;

        /// <summary>覆盖格子声明的控制空间（<c>world</c> 或 <c>camera_relative</c>）；缺省 null 取格子自己声明的值。在 <see cref="OnAttach"/> 之后读取。</summary>
        public virtual string? ControlSpaceOverride => null;

        /// <summary>
        /// 脚本轴事件所在的固定步里、输入映射更新之后：<paramref name="stick"/> 是注入桩摇杆的设备轴（横版深度锁丢掉竖直分量之后），
        /// <paramref name="mapped"/> 是输入映射给出的移动轴（<c>camera_relative</c> 时已按相机偏航换算成世界方向）。只读观测点，
        /// 不得改世界；相机相对输入的核对据此做。
        /// </summary>
        public virtual void OnMoveAxis(int tick, Vec2 stick, Vec2 mapped)
        {
        }

        /// <summary>该固定步全部逻辑与记录完成之后（<c>tick</c> 是刚完成的步序号）。</summary>
        public virtual void OnFixedStepEnd(int tick)
        {
        }

        /// <summary>每帧表现同步（<see cref="ViewBinder"/> 同步完位姿、帧记录已追加）之后。</summary>
        public virtual void OnFrame(int frame, double alpha, double frameSeconds)
        {
        }

        /// <summary>循环结束、记录定稿之后（资源释放之前）。</summary>
        public virtual void OnFinished(LabRecording recording)
        {
        }
    }

    /// <summary>把多个扩展按顺序串起来：视图工厂依次包装，反馈转发给全部非空转发目标，其余回调按顺序逐个调用；轴转换依次作用。</summary>
    public sealed class CompositeLabHostExtension : LabHostExtension
    {
        private readonly List<LabHostExtension> _items = new List<LabHostExtension>();
        private IFeedbackSink? _tee;

        public CompositeLabHostExtension(params LabHostExtension[] items)
        {
            foreach (var item in items)
            {
                if (item != null)
                {
                    _items.Add(item);
                }
            }
        }

        public override void OnAttach(LabHostContext context)
        {
            foreach (var item in _items) item.OnAttach(context);
        }

        public override IViewFactory WrapViewFactory(IViewFactory inner, LabHostContext context)
        {
            var current = inner;
            foreach (var item in _items) current = item.WrapViewFactory(current, context);
            return current;
        }

        public override IFeedbackSink? FeedbackTee
        {
            get
            {
                if (_tee != null)
                {
                    return _tee;
                }

                var sinks = new List<IFeedbackSink>();
                foreach (var item in _items)
                {
                    var sink = item.FeedbackTee;
                    if (sink != null)
                    {
                        sinks.Add(sink);
                    }
                }

                _tee = sinks.Count == 0 ? null : sinks.Count == 1 ? sinks[0] : new FanOutFeedbackSink(sinks);
                return _tee;
            }
        }

        public override void OnReady(LabHostContext context)
        {
            foreach (var item in _items) item.OnReady(context);
        }

        /// <summary>第一个提供相机朝向查询的成员（没有则 null）。</summary>
        public override Core.Foundation.EngineAdapter.ICameraOrientation? CameraOrientation
        {
            get
            {
                foreach (var item in _items)
                {
                    var found = item.CameraOrientation;
                    if (found != null)
                    {
                        return found;
                    }
                }

                return null;
            }
        }

        /// <summary>第一个给出控制空间覆盖的成员（没有则 null）。</summary>
        public override string? ControlSpaceOverride
        {
            get
            {
                foreach (var item in _items)
                {
                    var found = item.ControlSpaceOverride;
                    if (found != null)
                    {
                        return found;
                    }
                }

                return null;
            }
        }

        public override void OnMoveAxis(int tick, Vec2 stick, Vec2 mapped)
        {
            foreach (var item in _items) item.OnMoveAxis(tick, stick, mapped);
        }

        public override void OnFixedStepEnd(int tick)
        {
            foreach (var item in _items) item.OnFixedStepEnd(tick);
        }

        public override void OnFrame(int frame, double alpha, double frameSeconds)
        {
            foreach (var item in _items) item.OnFrame(frame, alpha, frameSeconds);
        }

        public override void OnFinished(LabRecording recording)
        {
            foreach (var item in _items) item.OnFinished(recording);
        }

        private sealed class FanOutFeedbackSink : IFeedbackSink
        {
            private readonly List<IFeedbackSink> _sinks;

            public FanOutFeedbackSink(List<IFeedbackSink> sinks)
            {
                _sinks = sinks;
            }

            public void FloatingText(Id entityId, Id styleId, string text) { foreach (var s in _sinks) s.FloatingText(entityId, styleId, text); }

            public void PlayVfx(Id vfxId, FeedbackAttachSpec attach) { foreach (var s in _sinks) s.PlayVfx(vfxId, attach); }

            public void PlaySfx(Id sfxId, Vec2? at) { foreach (var s in _sinks) s.PlaySfx(sfxId, at); }

            public void Freeze(double durationMs) { foreach (var s in _sinks) s.Freeze(durationMs); }

            public void ShakeCamera(Id profileId) { foreach (var s in _sinks) s.ShakeCamera(profileId); }

            public void Flash(Id entityId, Id profileId) { foreach (var s in _sinks) s.Flash(entityId, profileId); }

            public void ImpactCamera(ImpactCameraCue cue) { foreach (var s in _sinks) s.ImpactCamera(cue); }

            public void FreezePresentation(IReadOnlyList<Id> unitIds, int ticks, ImpactFreezeLayers layers) { foreach (var s in _sinks) s.FreezePresentation(unitIds, ticks, layers); }

            public void ReleasePresentation(IReadOnlyList<Id> unitIds) { foreach (var s in _sinks) s.ReleasePresentation(unitIds); }

            public bool HasPendingPlayback => false;

            public event Action? PendingPlaybackChanged
            {
                add { }
                remove { }
            }
        }
    }
}
