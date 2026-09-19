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
        /// 调用。等价于 <see cref="EnsureLoading(Id, ResourceKind, LoadCallback?)"/> 传
        /// <c>onComplete: null</c>（见该重载判断记录）。</summary>
        public void EnsureLoading(Id resourceId, ResourceKind kind) => EnsureLoading(resourceId, kind, onComplete: null);

        /// <summary>
        /// 排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md"教训三"新增重载：取代此前固定传给
        /// <see cref="IResourceLoader.LoadAsync"/> 的空操作回调——首次引用时若调用方传入
        /// <paramref name="onComplete"/>，会在 <see cref="IResourceLoader.LoadAsync"/> 的完成回调
        /// 到达时原样转发（<c>resourceId</c>/<c>success</c> 均透传，不做任何解释）。本类型的定位不变
        /// （类型注释"不关心加载成功/失败"）：<paramref name="onComplete"/> 只是把 <c>LoadAsync</c>
        /// 本就会触发的完成通知转发给调用方，是否要据此做什么（如重新应用图层、记诊断）完全是调用方
        /// （<see cref="Presentation.Render.SpriteCharacterRig"/> 等）的职责，本类型只是一个透传通道，
        /// 不持有、不解释回调结果。
        /// <para>
        /// ABI 只新增（AGENTS.md"代码规则"）：两参数重载签名不变、内部转发本方法且
        /// <c>onComplete</c> 传 <c>null</c>，语义与此前固定空回调完全一致，不影响任何既有调用方。
        /// </para>
        /// <para>
        /// 判断记录（同一 id 至多回调一次、去重不受 <paramref name="onComplete"/> 是否为 null 影响）：
        /// <c>_requested.Add(resourceId)</c> 已经过的 id 直接返回，不会重复调用
        /// <see cref="IResourceLoader.LoadAsync"/>，因此本次传入的 <paramref name="onComplete"/> 会被
        /// 丢弃、永远不会被调用——这是既有"同一 id 只触发一次加载"承诺的自然延伸：调用方若需要在
        /// "已经被别处请求过加载"的 id 上也挂一个完成通知，应改为自己持有并复用同一个回调委托（如
        /// <see cref="Presentation.Render.SpriteCharacterRig.HandleResourceLoadCompleted"/> 那样绑定
        /// 到实例而不是绑定到某一次具体调用），不能假设每次调用都会各自拿到一次通知。
        /// </para>
        /// </summary>
        public void EnsureLoading(Id resourceId, ResourceKind kind, LoadCallback? onComplete)
        {
            if (!_requested.Add(resourceId))
            {
                return;
            }

            _loader.LoadAsync(resourceId, kind, (id, success) => onComplete?.Invoke(id, success));
        }
    }
}
