// StubResourceLoader：IResourceLoader 的最小可用桩实现——同步"异步"，不做任何真实 I/O。
// 用途：测试驱动"资源加载完成/失败"这条流程，而不依赖真实的图片/音频/字体/数据表加载。
// 与真实实现的差异：LoadAsync 默认在调用当下就同步触发 callback（真实实现会异步触发，
// 调用方不能依赖同步语义，但桩实现的同步触发不违反契约——回调最终总会被调用）；
// 只有经测试方法 Register(Id) 登记过的资源 id 才会加载成功，未登记的一律加载失败，
// 由此可以确定性地测试"资源缺失"分支。
//
// DeferCallbacks 模式（T1-7b2 新增，供 scene_router 测试驱动"加载中途"的中间状态）：
// 默认 false，保持上述同步回调行为不变；置为 true 后，LoadAsync 只记下一条"待完成"请求，
// 不立即调用 callback，也不改变 IsLoaded/GetLoadProgress——由测试显式调用 CompletePending
// （模拟加载成功）或 FailPending（模拟加载失败）才会触发当初传入的 callback 并相应更新
// IsLoaded/GetLoadProgress。两种模式下 Register/Unregister 仍然决定"最终会成功还是失败"，
// DeferCallbacks 只影响"回调触发的时机"，不影响"是否登记过"的语义。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubResourceLoader : IResourceLoader
    {
        private readonly HashSet<Id> _registered = new HashSet<Id>();
        private readonly HashSet<Id> _loaded = new HashSet<Id>();
        private readonly Dictionary<Id, LoadCallback> _pending = new Dictionary<Id, LoadCallback>();

        /// <summary>测试用开关：true 时 LoadAsync 不立即回调，由 CompletePending/FailPending
        /// 驱动（见类型顶部注释）。默认 false，保持既有同步回调行为不变。</summary>
        public bool DeferCallbacks { get; set; }

        public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            if (!DeferCallbacks)
            {
                if (_registered.Contains(resourceId))
                {
                    _loaded.Add(resourceId);
                    callback(resourceId, true);
                }
                else
                {
                    callback(resourceId, false);
                }
                return;
            }

            if (_pending.ContainsKey(resourceId))
            {
                throw new InvalidOperationException(
                    $"资源 \"{resourceId}\" 已有一个待完成的 LoadAsync 请求，须先 CompletePending/FailPending 才能再次 LoadAsync");
            }

            _pending[resourceId] = callback;
        }

        public bool IsLoaded(Id resourceId) => _loaded.Contains(resourceId);

        public double GetLoadProgress(Id resourceId)
        {
            if (_loaded.Contains(resourceId)) return 1.0;
            // 判断记录：延迟模式下"仍在待完成队列里"视为进度 0（粗粒度估算，02 第 1.7 节
            // 允许——本桩实现不模拟渐进式百分比，只区分"未开始/完成"两个离散点）。
            return 0.0;
        }

        public void Unload(Id resourceId) => _loaded.Remove(resourceId);

        /// <summary>测试用：登记一个资源 id，使其后续 LoadAsync 调用回调成功。</summary>
        public void Register(Id resourceId) => _registered.Add(resourceId);

        /// <summary>测试用：取消登记，使其后续 LoadAsync 调用回调失败。</summary>
        public void Unregister(Id resourceId) => _registered.Remove(resourceId);

        /// <summary>DeferCallbacks 模式下：完成一个待完成请求，标记为已加载并以 success=true
        /// 触发当初传入的 callback。<paramref name="resourceId"/> 没有待完成请求时抛
        /// <see cref="InvalidOperationException"/>。DeferCallbacks=false 时调用本方法同样抛异常
        /// （没有请求会处于"待完成"状态）。</summary>
        public void CompletePending(Id resourceId)
        {
            var callback = TakePending(resourceId);
            _loaded.Add(resourceId);
            callback(resourceId, true);
        }

        /// <summary>DeferCallbacks 模式下：使一个待完成请求以失败告终，以 success=false 触发
        /// 当初传入的 callback，不标记为已加载。<paramref name="resourceId"/> 没有待完成请求时抛
        /// <see cref="InvalidOperationException"/>。</summary>
        public void FailPending(Id resourceId)
        {
            var callback = TakePending(resourceId);
            callback(resourceId, false);
        }

        /// <summary>是否存在一个尚未 CompletePending/FailPending 的待完成请求。</summary>
        public bool HasPending(Id resourceId) => _pending.ContainsKey(resourceId);

        private LoadCallback TakePending(Id resourceId)
        {
            if (!_pending.TryGetValue(resourceId, out var callback))
            {
                throw new InvalidOperationException(
                    $"资源 \"{resourceId}\" 没有待完成的 LoadAsync 请求（DeferCallbacks 未开启，或尚未调用 LoadAsync，或已经 Complete/FailPending 过）");
            }

            _pending.Remove(resourceId);
            return callback;
        }
    }
}
