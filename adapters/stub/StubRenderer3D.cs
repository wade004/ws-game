// StubRenderer3D：IRenderer3D 的最小可用桩实现——只记录调用，不做任何真实三维渲染。
// 用途：测试断言"模型实例被创建、放置参数是什么、播放了哪条动画、槽位换装/挂点挂接是否
// 生效"，而不启动任何三维渲染管线或骨骼动画系统。
// 与真实实现的差异：句柄按创建顺序自增分配；销毁后的句柄再次使用会抛
// InvalidOperationException；OnAnimEvent 只登记回调，测试可用 FireAnimEventForTest
// 主动触发一次关键帧事件来驱动上层订阅逻辑，因为桩本身不播放任何真实动画剪辑、
// 不会自然产生关键帧事件。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubRenderer3D : IRenderer3D
    {
        public readonly struct PlacementRecord
        {
            public readonly Vec2 PlanePos;
            public readonly double Height;
            public readonly double Facing;
            public readonly double Scale;
            public readonly double SortY;

            public PlacementRecord(Vec2 planePos, double height, double facing, double scale, double sortY)
            {
                PlanePos = planePos;
                Height = height;
                Facing = facing;
                Scale = scale;
                SortY = sortY;
            }
        }

        public readonly struct AnimPlayback
        {
            public readonly Id ClipId;
            public readonly bool Loop;
            public readonly double Speed;
            public readonly double BlendSeconds;

            public AnimPlayback(Id clipId, bool loop, double speed, double blendSeconds)
            {
                ClipId = clipId;
                Loop = loop;
                Speed = speed;
                BlendSeconds = blendSeconds;
            }
        }

        private int _nextHandle = 1;
        private readonly HashSet<int> _alive = new HashSet<int>();
        private readonly Dictionary<int, List<AnimEventCallback>> _animEventSubscribers = new Dictionary<int, List<AnimEventCallback>>();

        public readonly Dictionary<int, Id> CreatedModels = new Dictionary<int, Id>();
        public readonly Dictionary<int, PlacementRecord> Placements = new Dictionary<int, PlacementRecord>();
        public readonly Dictionary<int, AnimPlayback> CurrentAnims = new Dictionary<int, AnimPlayback>();
        public readonly Dictionary<int, double> AnimSpeeds = new Dictionary<int, double>();
        public readonly Dictionary<int, Dictionary<Id, Id?>> SlotMeshes = new Dictionary<int, Dictionary<Id, Id?>>();
        public readonly Dictionary<int, (Id SocketId, int ChildHandle)> Attachments = new Dictionary<int, (Id, int)>();
        public readonly Dictionary<int, Dictionary<string, double>> MaterialParams = new Dictionary<int, Dictionary<string, double>>();
        public readonly Dictionary<int, ShadowMode> Shadows = new Dictionary<int, ShadowMode>();

        public ModelHandle CreateModelInstance(Id modelId)
        {
            var handle = new ModelHandle(_nextHandle++);
            _alive.Add(handle.Value);
            CreatedModels[handle.Value] = modelId;
            return handle;
        }

        public void DestroyModelInstance(ModelHandle handle)
        {
            EnsureAlive(handle);
            _alive.Remove(handle.Value);
        }

        public void SetPlacement(ModelHandle handle, Vec2 planePos, double height, double facing, double scale, double sortY)
        {
            EnsureAlive(handle);
            Placements[handle.Value] = new PlacementRecord(planePos, height, facing, scale, sortY);
        }

        public void PlayAnim(ModelHandle handle, Id clipId, bool loop, double speed, double blendSeconds)
        {
            EnsureAlive(handle);
            CurrentAnims[handle.Value] = new AnimPlayback(clipId, loop, speed, blendSeconds);
        }

        public void SetAnimSpeed(ModelHandle handle, double speed)
        {
            EnsureAlive(handle);
            AnimSpeeds[handle.Value] = speed;
        }

        public SubscriptionHandle OnAnimEvent(ModelHandle handle, AnimEventCallback callback)
        {
            EnsureAlive(handle);
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            if (!_animEventSubscribers.TryGetValue(handle.Value, out var subscribers))
            {
                subscribers = new List<AnimEventCallback>();
                _animEventSubscribers[handle.Value] = subscribers;
            }

            subscribers.Add(callback);

            return new SubscriptionHandle(() => subscribers.Remove(callback));
        }

        public void SetSlotMesh(ModelHandle handle, Id slotId, Id? meshId)
        {
            EnsureAlive(handle);
            if (!SlotMeshes.TryGetValue(handle.Value, out var slots))
            {
                slots = new Dictionary<Id, Id?>();
                SlotMeshes[handle.Value] = slots;
            }

            slots[slotId] = meshId;
        }

        public void AttachToSocket(ModelHandle handle, Id socketId, ModelHandle child)
        {
            EnsureAlive(handle);
            EnsureAlive(child);
            Attachments[child.Value] = (socketId, handle.Value);
        }

        public void Detach(ModelHandle child)
        {
            Attachments.Remove(child.Value);
        }

        public void SetMaterialParam(ModelHandle handle, string paramName, double value)
        {
            EnsureAlive(handle);
            if (!MaterialParams.TryGetValue(handle.Value, out var parameters))
            {
                parameters = new Dictionary<string, double>(StringComparer.Ordinal);
                MaterialParams[handle.Value] = parameters;
            }

            parameters[paramName] = value;
        }

        public void SetShadow(ModelHandle handle, ShadowMode mode)
        {
            EnsureAlive(handle);
            Shadows[handle.Value] = mode;
        }

        /// <summary>测试用：模拟一次动画关键帧事件，触发该模型实例的全部已订阅回调。</summary>
        public void FireAnimEventForTest(ModelHandle handle, Id eventId)
        {
            if (!_animEventSubscribers.TryGetValue(handle.Value, out var subscribers))
            {
                return;
            }

            foreach (var callback in subscribers.ToArray())
            {
                callback(handle, eventId);
            }
        }

        /// <summary>H5b 根治新增（游戏侧复核发现 1）：测试用——同步判定"当前这次 PlayAnim 播放的剪辑
        /// 已经自然播放完成"，只在最近一次 <see cref="PlayAnim"/> 是非循环（<c>loop: false</c>）时才
        /// 经 <see cref="FireAnimEventForTest"/> 发出一次 <c>"anim_event.finished"</c>（与
        /// <c>Presentation.Render.ModelCharacterRig.AnimFinishedEventId</c> 逐字相等）；循环剪辑或
        /// 该句柄尚未播放过任何剪辑时 no-op——桩本身没有真实的时间/播放进度概念，本方法把
        /// "自然播放完成"简化为"调用方显式声明这次播放已经结束"，供
        /// <c>adapters/conformance/Renderer3DScenarios</c> 的对应场景经
        /// <c>ConformanceContext.CompleteNonLoopAnim</c> 钩子统一驱动（Unity 侧改为真的等待若干真实
        /// 帧，见该字段判断记录）。</summary>
        public void CompleteAnimForTest(ModelHandle handle)
        {
            if (CurrentAnims.TryGetValue(handle.Value, out var anim) && !anim.Loop)
            {
                FireAnimEventForTest(handle, FinishedEventId);
            }
        }

        /// <summary>H5b 根治新增：与 <see cref="Presentation.Render.ModelCharacterRig.AnimFinishedEventId"/>
        /// 逐字相等的本地常量——本项目（<c>adapters/stub</c>）不引用 <c>presentation/</c>，不能直接
        /// 复用该类型的静态字段（避免给桩实现新增一个跨层依赖），改本地按同一约定构造一份。</summary>
        public static readonly Id FinishedEventId = new Id("anim_event.finished");

        private void EnsureAlive(ModelHandle handle)
        {
            if (!_alive.Contains(handle.Value))
            {
                throw new InvalidOperationException($"模型句柄 {handle.Value} 已销毁或不存在");
            }
        }
    }
}
