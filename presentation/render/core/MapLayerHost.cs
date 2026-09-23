using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SceneRouter;
using Presentation.VfxSfx.Contracts;

namespace Presentation.Render
{
    /// <summary>
    /// ADR-0080：地图分层图（<c>ground</c>/<c>overlay</c>/<c>decal</c>）接入运行期渲染的驱动方——
    /// 场景加载完成（<see cref="ISceneRouter.RegisterPostLoadHook"/>）时读取该地图 <c>world.map</c>
    /// 行的 <c>image_transform</c>，发起 <see cref="ResourceKind.MapLayers"/> 加载，加载完成后按三层
    /// 分别调用 <see cref="IRenderer2D.CreateMapLayerInstance"/>；场景卸载/切图前
    /// （<see cref="ISceneRouter.RegisterPreUnloadHook"/>）销毁本地图建出的全部层，避免切走再切回时
    /// 堆出两套分层图。
    /// <para>
    /// 表现层铁律（09 第 1 节）：本类型只读 <see cref="IDataRegistryView"/> 暴露的只读查询，不写回
    /// 任何状态；只订阅 <see cref="ISceneRouter"/> 既有的 <c>pre_unload</c>/<c>post_load</c> 挂载点
    /// （本就是表现层驱动 P4 绘制指令的既有惯例），不新增、不发布任何 <c>IEventBus</c> 事件；绘制/
    /// 卸载只经 <see cref="IRenderer2D"/>/<see cref="IResourceLoader"/> 两个 L-1 接口完成，自身不直接
    /// 调用任何具体引擎 API。
    /// </para>
    /// <para>
    /// 判断记录（世界矩形唯一由 <c>image_transform</c> 决定，ADR-0080 决策 2）：地图行未声明
    /// <c>image_transform</c>，或声明了但未声明 <c>image_size_px</c>（<see cref="MapImageTransform.WorldBounds"/>
    /// 因此为 <c>null</c>，无法界定世界坐标包围盒）——两种情形均归入同一种"缺关键入参，无法界定要摆
    /// 多大，索性不摆"处理口径：记一条诊断，不建任何层，不抛异常。不臆造第二套"只知道换算比例、不知道
    /// 图多大时"的降级方案（例如摆一个任意默认尺寸）。
    /// </para>
    /// <para>
    /// 判断记录（谁决定某一层是否真的建出来）：本类型对 <see cref="MapLayerKind.Ground"/>/
    /// <see cref="MapLayerKind.Overlay"/>/<see cref="MapLayerKind.Decal"/> 三层均无条件调用一次
    /// <see cref="IRenderer2D.CreateMapLayerInstance"/>，具体某一层的图片是否存在（尤其是可选层
    /// <c>decal</c>）由渲染实现自己解析资源后决定：存在则返回有效句柄，不存在则按 ADR-0080 决策 3
    /// 的约定返回无效句柄（必需层记诊断用占位，可选层记诊断不画占位）。本类型只按
    /// <see cref="MapLayerHandle.IsValid"/> 决定是否把该句柄纳入销毁清单，不重复判断"这一层该不该
    /// 存在"——避免表现层驱动方与渲染实现各自维护一份"哪些层文件存在"的判断，产生分歧。
    /// </para>
    /// </summary>
    public sealed class MapLayerHost : IDisposable
    {
        private readonly IDataRegistryView _registry;
        private readonly IRenderer2D _renderer2D;
        private readonly IResourceLoader? _resourceLoader;
        private readonly SubscriptionHandle _postLoadSubscription;
        private readonly SubscriptionHandle _preUnloadSubscription;
        private readonly Dictionary<Id, List<MapLayerHandle>> _activeLayers = new Dictionary<Id, List<MapLayerHandle>>();
        private bool _disposed;

        /// <summary>诊断出口（同 <c>VfxDiagnostics</c>/<c>SfxDiagnostics</c> 惯例，见
        /// <see cref="IPresentationDiagnostics"/> 类型注释）：地图未声明 <c>image_transform</c>、
        /// 分层图加载失败、必需层未能建出等降级路径记一条警告，不抛异常。</summary>
        public IPresentationDiagnostics Diagnostics { get; } = new PresentationDiagnosticsRecorder();

        public MapLayerHost(ISceneRouter sceneRouter, IDataRegistryView registry, IRenderer2D renderer2D, IResourceLoader? resourceLoader)
        {
            if (sceneRouter == null) throw new ArgumentNullException(nameof(sceneRouter));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _renderer2D = renderer2D ?? throw new ArgumentNullException(nameof(renderer2D));
            _resourceLoader = resourceLoader;

            _postLoadSubscription = sceneRouter.RegisterPostLoadHook(OnPostLoad);
            _preUnloadSubscription = sceneRouter.RegisterPreUnloadHook(OnPreUnload);
        }

        /// <summary>测试/诊断用：当前仍持有存活句柄的地图数（供"切图不泄漏"一类断言，不属于本类型
        /// 对外承诺的运行时能力）。</summary>
        public int ActiveMapCount => _activeLayers.Count;

        /// <summary>测试/诊断用：<paramref name="mapId"/> 当前存活的分层图实例数（0 表示未建过或已
        /// 全部销毁）。</summary>
        public int GetActiveLayerCount(Id mapId) =>
            _activeLayers.TryGetValue(mapId, out var handles) ? handles.Count : 0;

        private void OnPostLoad(Id mapId)
        {
            var record = _registry.Get(WorldMapSchema.Table.Name, mapId);
            if (record == null)
            {
                // 理论不应发生——SceneRouter.LoadScene 本身要求 world.map 行存在才能发起加载，post_load
                // 挂载点收到的 mapId 必然对应一条已加载记录；仍保留防御性分支，不假定调用方一定按
                // ISceneRouter 契约驱动本类型。
                Diagnostics.Warn($"map_layer_host: world.map 行 \"{mapId}\" 不存在，跳过地图分层图建层");
                return;
            }

            var transform = MapImageTransform.FromRecord(record);
            if (transform == null)
            {
                Diagnostics.Warn($"map_layer_host: 地图 \"{mapId}\" 未声明 image_transform，跳过地图分层图建层（ADR-0080 决策 2：世界矩形唯一由 image_transform 决定）");
                return;
            }

            var bounds = transform.WorldBounds;
            if (bounds == null)
            {
                Diagnostics.Warn($"map_layer_host: 地图 \"{mapId}\" 的 image_transform 未声明 image_size_px，无法界定世界矩形，跳过地图分层图建层");
                return;
            }

            if (_resourceLoader == null)
            {
                Diagnostics.Warn($"map_layer_host: 未注入 IResourceLoader，跳过地图 \"{mapId}\" 分层图建层");
                return;
            }

            var worldBounds = new Rect(bounds.Value.Min, bounds.Value.Max);

            // ADR-0080 决策 3："先 LoadAsync 再建层"的顺序由表现层驱动方负责——渲染实现只消费已加载
            // 完成的资源，本类型是本资源种类唯一的消费方，首次引用该地图分层图的责任在此。
            _resourceLoader.LoadAsync(mapId, ResourceKind.MapLayers, (loadedMapId, success) =>
            {
                if (!success)
                {
                    Diagnostics.Warn($"map_layer_host: 地图 \"{loadedMapId}\" 分层图加载失败，跳过建层");
                    return;
                }

                BuildLayers(loadedMapId, worldBounds);
            });
        }

        private void BuildLayers(Id mapId, Rect worldBounds)
        {
            var handles = new List<MapLayerHandle>(3);
            TryCreateLayer(mapId, MapLayerKind.Ground, worldBounds, RenderLayers.Ground, handles);
            TryCreateLayer(mapId, MapLayerKind.Overlay, worldBounds, RenderLayers.Foreground, handles);
            TryCreateLayer(mapId, MapLayerKind.Decal, worldBounds, RenderLayers.Decoration, handles);
            _activeLayers[mapId] = handles;
        }

        private void TryCreateLayer(Id mapId, MapLayerKind kind, Rect worldBounds, int renderLayer, List<MapLayerHandle> handles)
        {
            var handle = _renderer2D.CreateMapLayerInstance(mapId, kind, worldBounds, renderLayer);
            if (handle.IsValid)
            {
                handles.Add(handle);
                return;
            }

            if (kind != MapLayerKind.Decal)
            {
                // ground/overlay 是 ADR-0080 决策 1 声明的必需层：渲染实现返回无效句柄理论上只应在
                // decal 缺失这一优先场景发生（决策 6）；必需层也拿到无效句柄大概率是数据缺陷（声明了
                // image_transform 却没有对应图片），记一条诊断，但不阻断（渲染实现自己的占位/诊断策略
                // 已经处理了"资源缺失"本身，这里不重复报告为异常，也不重试）。
                Diagnostics.Warn($"map_layer_host: 地图 \"{mapId}\" 的必需层 {kind} 未能创建实例（渲染实现返回无效句柄）");
            }
            // decal 缺失是合法状态（ADR-0080 决策 6：可选层缺失时只建两层、记诊断、不报错），本类型
            // 不重复记诊断——是否存在该层文件、是否要记诊断，由渲染实现自己决定（见类型注释判断记录）。
        }

        private void OnPreUnload(Id mapId)
        {
            if (!_activeLayers.TryGetValue(mapId, out var handles))
            {
                return;
            }

            for (var i = 0; i < handles.Count; i++)
            {
                _renderer2D.DestroyMapLayerInstance(handles[i]);
            }

            _activeLayers.Remove(mapId);
        }

        /// <summary>判断记录（Dispose 时主动销毁仍存活的层，2026-09-23 补充）：<see cref="OnPreUnload"/>
        /// 只在场景真的经 <see cref="ISceneRouter"/> 走一次卸载/切图时才会触发；调用方（生产装配根
        /// <c>PresentationAssembly</c>）在地图仍处于加载状态时直接 Dispose（例如进程整体退出、测试
        /// 夹具收尾）不会经过那条路径。同 <c>ViewFactory.DestroyAllCreatedViews</c>/其余 Host 型别
        /// "自己负责清理自己建出的引擎侧对象"的既有惯例，这里补齐——避免调用方在同一进程内反复
        /// 构造/销毁 <c>PresentationAssembly</c>（例如测试夹具、或未来允许应用内"退出到桌面"之外的
        /// 场景）时，前一份实例建出的地图分层图 GameObject 一直挂在引擎适配层的常驻根节点下不被回收。</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            foreach (var handles in _activeLayers.Values)
            {
                for (var i = 0; i < handles.Count; i++)
                {
                    _renderer2D.DestroyMapLayerInstance(handles[i]);
                }
            }
            _activeLayers.Clear();

            _postLoadSubscription.Dispose();
            _preUnloadSubscription.Dispose();
        }
    }
}
