// StubResourceLoader：IResourceLoader 的最小可用桩实现——同步"异步"，不做任何真实 I/O。
// 用途：测试驱动"资源加载完成/失败"这条流程，而不依赖真实的图片/音频/字体/数据表加载。
// 与真实实现的差异：LoadAsync 在调用当下就同步触发 callback（真实实现会异步触发，
// 调用方不能依赖同步语义，但桩实现的同步触发不违反契约——回调最终总会被调用）；
// 只有经测试方法 Register(Id) 登记过的资源 id 才会加载成功，未登记的一律加载失败，
// 由此可以确定性地测试"资源缺失"分支。
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

        public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));

            if (_registered.Contains(resourceId))
            {
                _loaded.Add(resourceId);
                callback(resourceId, true);
            }
            else
            {
                callback(resourceId, false);
            }
        }

        public bool IsLoaded(Id resourceId) => _loaded.Contains(resourceId);

        public double GetLoadProgress(Id resourceId) => _loaded.Contains(resourceId) ? 1.0 : 0.0;

        public void Unload(Id resourceId) => _loaded.Remove(resourceId);

        /// <summary>测试用：登记一个资源 id，使其后续 LoadAsync 调用回调成功。</summary>
        public void Register(Id resourceId) => _registered.Add(resourceId);

        /// <summary>测试用：取消登记，使其后续 LoadAsync 调用回调失败。</summary>
        public void Unregister(Id resourceId) => _registered.Remove(resourceId);
    }
}
