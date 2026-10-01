// StubResourceLoader：IResourceLoader 的最小可用桩实现——同步"异步"，不做任何真实 I/O。
// 用途：测试驱动"资源加载完成/失败"这条流程，而不依赖真实的图片/音频/字体/数据表加载。
// 与真实实现的差异：LoadAsync 默认在调用当下就同步触发 callback（真实实现会异步触发，
// 调用方不能依赖同步语义，但桩实现的同步触发不违反契约——回调最终总会被调用）；
// 只有经测试方法 Register(Id) 登记过的资源 id 才会加载成功，未登记的一律加载失败，
// 由此可以确定性地测试"资源缺失"分支。
//
// DeferCallbacks 模式（T1-7b2 新增，供 scene_router 测试驱动"加载中途"的中间状态）：
// 默认 false，保持上述同步回调行为不变；置为 true 后，LoadAsync 只把请求排进"待完成"表
// （按资源 id 各一条），不立即调用 callback，也不改变 IsLoaded/GetLoadProgress——由测试显式调用
// CompletePending（判成功：标记已加载并以 success=true 触发当初的 callback）或 FailPending（判失败：
// 以 success=false 触发，不标记已加载）才会触发。语义细节（均有 StubResourceLoaderTests 钉住）：
//   - 延迟模式下"成功还是失败"由测试调用 CompletePending/FailPending 显式裁决，**不看** Register/Unregister——
//     未登记的资源也可以被 CompletePending 判成功，登记过的也可以被 FailPending 判失败；
//     Register/Unregister 只决定默认（同步）模式下的结果；
//   - 同一资源 id 已有待完成请求时再 LoadAsync 抛 InvalidOperationException（须先 Complete/FailPending）；
//     不同资源 id 的待完成请求互相独立；
//   - 对没有待完成请求的 id 调 CompletePending/FailPending 抛 InvalidOperationException（含同步模式）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    /// <summary><see cref="IResourceLoader"/> 的最小桩实现：同步回调的“异步”加载，只有测试登记过的资源 id 才加载成功。</summary>
    public sealed class StubResourceLoader : IResourceLoader
    {
        private readonly HashSet<Id> _registered = new HashSet<Id>();
        private readonly HashSet<Id> _loaded = new HashSet<Id>();
        private readonly Dictionary<Id, LoadCallback> _pending = new Dictionary<Id, LoadCallback>();

        /// <summary>测试用开关：true 时 LoadAsync 只把请求排进待完成表（每个资源 id 至多一条，重复 LoadAsync
        /// 抛异常）而不回调，由 CompletePending/FailPending 显式裁决成败（不看 Register/Unregister），详见类型
        /// 顶部注释。默认 false，保持既有同步回调行为不变。</summary>
        public bool DeferCallbacks { get; set; }

        /// <summary>测试用：每次 LoadAsync 调用的 (resourceId, kind)，按调用顺序追加（含重复调用，
        /// 供"资源首次加载责任归属"——ADR-0016 决策 6——的调用方断言"同一 id 只调用一次"）。</summary>
        public readonly List<(Id ResourceId, ResourceKind Kind)> LoadRequests = new List<(Id, ResourceKind)>();

        public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            LoadRequests.Add((resourceId, kind));

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
