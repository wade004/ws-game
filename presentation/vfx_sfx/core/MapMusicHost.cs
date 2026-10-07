using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.SceneRouter;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// 按地图切背景音乐的可选宿主（P4 备忘 2，样板游戏 A 反馈）：场景加载完成（<see cref="ISceneRouter.RegisterPostLoadHook"/>）时读该地图
    /// <c>world.map</c> 行的 <c>music_ref</c>，经 <see cref="IMusicPlayer"/> 播放（资源未加载时由播放器先加载）。此前 <c>music_ref</c> 只被校验、
    /// 没有消费者，游戏只能自己读字段再调 <c>IAudio.PlayMusic</c>。
    /// <para>
    /// 判断记录：① 地图行没有 <c>music_ref</c>（或为空）时<b>不动</b>当前音乐——不停止也不换（游戏可能在用代码接管曲目，例如首领战另切一首；
    /// 想让某张图静音请显式停止）。② 同一首曲目在相邻两张地图上相同，由 <see cref="IMusicPlayer.Play"/> 的幂等保证不重播。③ <c>music_ref</c> 不是合法
    /// 资源 id 时记诊断、不抛异常。④ <see cref="PresentationAssemblyOptions.MapMusicEnabled"/> 为 false 时装配根不构造本宿主，游戏自己管音乐。
    /// 表现层铁律：只读 <see cref="IDataRegistryView"/>，不发布事件，音频只经 <see cref="IMusicPlayer"/>（→ <c>IAudio</c>）。
    /// </para>
    /// </summary>
    public sealed class MapMusicHost : IDisposable
    {
        private readonly IDataRegistryView _registry;
        private readonly IMusicPlayer _music;
        private readonly IPresentationDiagnostics _diagnostics;
        private readonly SubscriptionHandle _postLoadSubscription;
        private bool _disposed;

        public MapMusicHost(ISceneRouter sceneRouter, IDataRegistryView registry, IMusicPlayer music, IPresentationDiagnostics? diagnostics = null)
        {
            if (sceneRouter == null) throw new ArgumentNullException(nameof(sceneRouter));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _music = music ?? throw new ArgumentNullException(nameof(music));
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
            _postLoadSubscription = sceneRouter.RegisterPostLoadHook(OnPostLoad);
        }

        public IPresentationDiagnostics Diagnostics => _diagnostics;

        private void OnPostLoad(Id mapId)
        {
            var record = _registry.Get(WorldMapSchema.Table.Name, mapId);
            if (record == null || !record.TryGetString("music_ref", out var musicRef) || string.IsNullOrEmpty(musicRef))
            {
                return;
            }

            if (!Id.TryParse(musicRef, out var track))
            {
                _diagnostics.Warn($"map_music_host: 地图 \"{mapId}\" 的 music_ref \"{musicRef}\" 不是合法的资源 id，跳过");
                return;
            }

            _music.Play(track);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _postLoadSubscription.Dispose();
        }
    }
}
