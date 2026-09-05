#nullable enable
// UnityViewFactory：Presentation.Common.IViewFactory 的 Unity 引擎实现，presentation/view_binding
// 的 ViewBinder 是唯一调用方（见该接口注释）。
//
// 判断记录（View 生命周期跟踪，弥补 ViewBinder/CameraHost 不支持退订的已知缺口）：
// presentation/assembly/README.md"判断记录 4"如实记录了 ViewBinder 构造期直接 bus.Subscribe，
// 没有对外暴露退订句柄，PresentationAssembly.Dispose() 因此无法代为退订/销毁 ViewBinder 已创建的
// 全部 View。若不处理，场景重进（新建一整套 World/GameplayAssembly/PresentationAssembly）会让
// 上一次的 View（其 Sprite 实例挂在 DontDestroyOnLoad 的 UnityEngineHost.Renderer2D 根下，不会
// 随场景卸载自动销毁）持续累积。本类型不改 presentation/（该模块补退订属于契约变更，需要走 12
// 第 5 节流程），改为在"引擎侧"补一个不属于 IViewFactory 契约本身的协作方法
// DestroyAllCreatedViews——本工厂记住自己创建过的每一个 View，供
// GameFoundationBootstrap.OnDestroy 主动逐一调用 IView.Destroy() 清理，与
// UnityResourceLoader.TryGetSprite 之类"引擎实现之间的内部协作方法，不算契约违反"同一惯例。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Presentation
{
    /// <summary>没有可用 sprite 型 DisplayInfo 时的退化 View（见 <see cref="UnityViewFactory.CreateView"/>
    /// 判断记录）：不渲染任何东西，但仍满足 <see cref="IView"/> 契约，不阻断 <c>ViewBinder</c> 的绑定表
    /// 维护与后续事件转发查找。</summary>
    internal sealed class NullView : IView
    {
        public Id EntityId { get; private set; }
        public bool IsAlive { get; private set; }

        public void Bind(Id entityId)
        {
            EntityId = entityId;
            IsAlive = true;
        }

        public void OnEvent(IEvent evt)
        {
        }

        public void SyncPose(Vec2 pos, Direction facing, double height)
        {
        }

        public void Destroy() => IsAlive = false;
    }

    public sealed class UnityViewFactory : IViewFactory
    {
        private readonly IRenderer2D _renderer2D;
        private readonly IRenderConventionHost _conventions;
        private readonly IDisplayInfoRegistry _displayInfo;
        private readonly IResourceLoader _resourceLoader;
        private readonly List<IView> _created = new List<IView>();
        private readonly HashSet<string> _warnedMissingDisplay = new HashSet<string>();

        public UnityViewFactory(
            IRenderer2D renderer2D,
            IRenderConventionHost conventions,
            IDisplayInfoRegistry displayInfo,
            IResourceLoader resourceLoader)
        {
            _renderer2D = renderer2D ?? throw new ArgumentNullException(nameof(renderer2D));
            _conventions = conventions ?? throw new ArgumentNullException(nameof(conventions));
            _displayInfo = displayInfo ?? throw new ArgumentNullException(nameof(displayInfo));
            _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        }

        /// <summary>本工厂迄今创建过的全部 View，只读快照（诊断/测试用）。</summary>
        public IReadOnlyList<IView> CreatedViews => _created;

        public IView CreateView(ViewKind kind, Id displayId, Id entityId)
        {
            var info = _displayInfo.Lookup(displayId);
            IView view;
            if (info == null || info.Kind != DisplayKind.Sprite)
            {
                if (_warnedMissingDisplay.Add(displayId.Value))
                {
                    Debug.LogWarning($"[UnityViewFactory] displayId \"{displayId}\" 没有 kind=sprite 的 DisplayInfo，退化为空视图（不渲染）：kind={kind}");
                }
                view = new NullView();
            }
            else
            {
                view = new UnitySpriteView(_renderer2D, _conventions, info, _resourceLoader);
            }

            _created.Add(view);
            return view;
        }

        /// <summary>销毁本工厂创建过的全部仍存活的 View（见类型注释判断记录），并清空跟踪列表。
        /// 供 <c>GameFoundationBootstrap.OnDestroy</c>/场景重进清理调用，不属于 <see cref="IViewFactory"/>
        /// 契约本身。</summary>
        public void DestroyAllCreatedViews()
        {
            for (var i = 0; i < _created.Count; i++)
            {
                if (_created[i].IsAlive)
                {
                    _created[i].Destroy();
                }
            }
            _created.Clear();
        }
    }
}
