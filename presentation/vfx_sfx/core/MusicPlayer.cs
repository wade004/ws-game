using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// <see cref="IMusicPlayer"/> 的默认实现（P4 备忘 2）。
    /// <para>
    /// 判断记录（为什么"自带加载"放在表现层而不是 <c>IAudio.PlayMusic</c> 里）：ADR-0016 决策 6 规定 <c>IAudio</c>/<c>IRenderer</c> 的实现不得隐式加载资源，
    /// 首次引用方负责 <see cref="IResourceLoader.LoadAsync(Id, ResourceKind, LoadCallback)"/>；音效早已由 <see cref="SfxPlayer"/> 履行这一责任，
    /// 音乐此前没有对应的播放器，调用方必须自己先加载再回调里播，漏了就静音且无诊断。本类型补上这个播放器，<c>IAudio</c> 契约不动。
    /// 加载失败、没有注入资源加载器时记一条诊断（不再静音无声）。同一资源 id 只发起一次 <c>LoadAsync</c>（含失败，失败不重试，惯例同
    /// <see cref="SfxPlayer"/>）。"最新请求为准"：加载完成回调只在"最新请求恰好还是这一首"时才开播（加载途中改了主意，先前那一首到货后不会再开播）。
    /// </para>
    /// </summary>
    public sealed class MusicPlayer : IMusicPlayer
    {
        private readonly IAudio _audio;
        private readonly IResourceLoader? _resourceLoader;
        private readonly IPresentationDiagnostics _diagnostics;
        private readonly HashSet<Id> _loadRequested = new HashSet<Id>();
        private readonly HashSet<Id> _loadFailed = new HashSet<Id>();

        public MusicPlayer(IAudio audio, IResourceLoader? resourceLoader, IPresentationDiagnostics? diagnostics = null)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _resourceLoader = resourceLoader;
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
        }

        public IPresentationDiagnostics Diagnostics => _diagnostics;

        public Id? Requested { get; private set; }

        public Id? Playing { get; private set; }

        public void Play(Id trackRef, double fadeInSeconds = 0.0, bool loop = true)
        {
            if (Requested.HasValue && Requested.Value.Equals(trackRef))
            {
                // 已在播或仍在加载同一首：幂等（切回同一张地图不重播）。
                return;
            }

            Requested = trackRef;
            if (_resourceLoader == null)
            {
                _diagnostics.Warn($"music_player: 未注入 IResourceLoader，直接交给音频后端播放 \"{trackRef}\"（后端未预加载该资源时会静音）");
                StartPlayback(trackRef, fadeInSeconds, loop);
                return;
            }

            if (_resourceLoader.IsLoaded(trackRef))
            {
                StartPlayback(trackRef, fadeInSeconds, loop);
                return;
            }

            if (_loadFailed.Contains(trackRef))
            {
                _diagnostics.Warn($"music_player: 音乐资源 \"{trackRef}\" 先前加载失败，不再重试，本次请求不播放");
                return;
            }

            // 先登记再发起：同步加载器会在 LoadAsync 内就回调，回调要能看到"已登记"。
            if (_loadRequested.Add(trackRef))
            {
                _resourceLoader.LoadAsync(trackRef, ResourceKind.Audio, (id, ok) => OnLoaded(id, ok, fadeInSeconds, loop));
            }
            else
            {
                // 同一资源已有在途加载（先前请求过又切走了）：这一次请求靠 IsLoaded 轮询不到回调，登记一个等价的"到货即播"——
                // 在途加载完成时 OnLoaded 按当时的 Requested 判断，所以这里只需把参数记下。
                _pendingParams[trackRef] = (fadeInSeconds, loop);
            }
        }

        private readonly Dictionary<Id, (double Fade, bool Loop)> _pendingParams = new Dictionary<Id, (double, bool)>();

        private void OnLoaded(Id resourceId, bool success, double fadeInSeconds, bool loop)
        {
            if (!success)
            {
                _loadFailed.Add(resourceId);
                _diagnostics.Warn($"music_player: 音乐资源 \"{resourceId}\" 加载失败，未播放");
                if (Requested.HasValue && Requested.Value.Equals(resourceId))
                {
                    Requested = null;
                }

                return;
            }

            // 以"当前最新请求恰好就是这一首"为准（不另设序号：同一首先后被请求两次时，在途的那次加载应服务最新一次的参数）。
            if (!Requested.HasValue || !Requested.Value.Equals(resourceId))
            {
                return;
            }

            if (_pendingParams.TryGetValue(resourceId, out var latest))
            {
                _pendingParams.Remove(resourceId);
                fadeInSeconds = latest.Fade;
                loop = latest.Loop;
            }

            StartPlayback(resourceId, fadeInSeconds, loop);
        }

        private void StartPlayback(Id trackRef, double fadeInSeconds, bool loop)
        {
            _audio.PlayMusic(trackRef, fadeInSeconds, loop);
            Playing = trackRef;
        }

        public void Stop(double fadeOutSeconds = 0.0)
        {
            Requested = null;
            Playing = null;
            _pendingParams.Clear();
            _audio.StopMusic(fadeOutSeconds);
        }
    }
}
