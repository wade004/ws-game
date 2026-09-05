using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Presentation.Common
{
    /// <summary>
    /// "资源首次加载的责任归属"（02_引擎适配层.md 第 1.7 节 / ADR-0016 决策 6：谁首次引用一个
    /// 资源 id，谁负责调用 <see cref="IResourceLoader.LoadAsync"/>；<c>IRenderer2D</c>/<c>IAudio</c>/
    /// <c>IUISurface</c> 的实现只消费已加载完成的资源，遇到未加载的资源 id 时用内建占位并记诊断，
    /// 不隐式触发加载）这条职责的最小共享实现：记录已经触发过加载请求的资源 id，同一 id 只调用一次
    /// <c>LoadAsync</c>，不重复触发；不关心加载成功/失败——那是消费方（<c>IRenderer2D</c> 等）
    /// 自己的职责，本类型只负责"该由谁触发、别重复触发"。
    /// <para>
    /// 供 <c>presentation</c> 下"首次引用某个资源 id 的地方"复用：<c>VfxPlayer</c>（<c>vfx.def</c>
    /// 的 <c>resource_ref</c>，见 09 第 5.1 节）、<c>SfxPlayer</c>（<c>sfx.def</c> 的
    /// <c>resource_ref</c>/variants，见 09 第 5.3 节）、<c>SpriteViewBase</c>（精灵集 id 与纸娃娃层
    /// 解析出的资源 id，见 09 第 3.3 节）。
    /// </para>
    /// </summary>
    public sealed class ResourceReferenceTracker
    {
        private readonly IResourceLoader _loader;
        private readonly HashSet<Id> _requested = new HashSet<Id>();

        public ResourceReferenceTracker(IResourceLoader loader)
        {
            _loader = loader;
        }

        /// <summary>首次引用 <paramref name="resourceId"/> 时以 <paramref name="kind"/> 触发一次
        /// <see cref="IResourceLoader.LoadAsync"/>；此前已经引用过（无论加载是否已完成）则不重复
        /// 调用。</summary>
        public void EnsureLoading(Id resourceId, ResourceKind kind)
        {
            if (!_requested.Add(resourceId))
            {
                return;
            }

            _loader.LoadAsync(resourceId, kind, (_, _) => { });
        }
    }
}
