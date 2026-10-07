using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    /// <summary>
    /// P4 备忘 2（样板游戏 A 反馈）：<see cref="MusicPlayer"/> 自带加载（红：此前 <c>IAudio.PlayMusic</c> 没人替调用方加载，资源未加载时静音且无诊断）、
    /// <see cref="MapMusicHost"/> 按地图 <c>music_ref</c> 切曲。期望值取自测试自己写进数据的资源 id，不写死裸数。
    /// </summary>
    public sealed class MusicPlayerTests
    {
        private static readonly Id TrackA = new Id("music.test_a");
        private static readonly Id TrackB = new Id("music.test_b");

        private static (MusicPlayer Player, StubAudio Audio, StubResourceLoader Loader) NewPlayer(bool deferLoads)
        {
            var audio = new StubAudio();
            var loader = new StubResourceLoader { DeferCallbacks = deferLoads };
            return (new MusicPlayer(audio, loader), audio, loader);
        }

        [Fact]
        public void Play_ResourceNotLoaded_LoadsFirst_ThenStartsPlayback()
        {
            var (player, audio, loader) = NewPlayer(deferLoads: true);

            player.Play(TrackA, fadeInSeconds: 0.5, loop: true);

            Assert.Null(audio.CurrentMusic);
            Assert.Equal(TrackA, player.Requested);
            Assert.Null(player.Playing);
            Assert.Contains((TrackA, ResourceKind.Audio), loader.LoadRequests);

            loader.CompletePending(TrackA);

            Assert.Equal(TrackA, audio.CurrentMusic!.Value.TrackId);
            Assert.Equal(0.5, audio.CurrentMusic!.Value.FadeInSeconds);
            Assert.True(audio.CurrentMusic!.Value.Loop);
            Assert.Equal(TrackA, player.Playing);
        }

        [Fact]
        public void Play_AlreadyLoaded_StartsImmediately_WithoutSecondLoad()
        {
            var (player, audio, loader) = NewPlayer(deferLoads: false);
            loader.Register(TrackA);
            loader.LoadAsync(TrackA, ResourceKind.Audio, (_, __) => { });
            var requestsBefore = loader.LoadRequests.Count;

            player.Play(TrackA);

            Assert.Equal(TrackA, audio.CurrentMusic!.Value.TrackId);
            Assert.Equal(requestsBefore, loader.LoadRequests.Count);
        }

        [Fact]
        public void Play_SameTrackTwice_IsIdempotent_NoReplay()
        {
            var (player, audio, loader) = NewPlayer(deferLoads: false);
            loader.Register(TrackA);
            player.Play(TrackA);
            audio.StopMusic(0);                 // 外部把音频停了（探针）：同曲再次请求仍不重播——幂等以播放器自己的请求状态为准
            player.Play(TrackA);

            Assert.Null(audio.CurrentMusic);
            Assert.Single(loader.LoadRequests);
        }

        [Fact]
        public void Play_ThenSwitchBeforeLoadFinishes_LatestRequestWins()
        {
            var (player, audio, loader) = NewPlayer(deferLoads: true);

            player.Play(TrackA);
            player.Play(TrackB);
            loader.CompletePending(TrackB);
            loader.CompletePending(TrackA);

            Assert.Equal(TrackB, audio.CurrentMusic!.Value.TrackId);
            Assert.Equal(TrackB, player.Playing);
        }

        [Fact]
        public void Play_SwitchAwayAndBackWhileLoading_StillPlaysWhenLoadArrives()
        {
            var (player, audio, loader) = NewPlayer(deferLoads: true);

            player.Play(TrackA);
            player.Play(TrackB);
            player.Play(TrackA);
            loader.CompletePending(TrackA);

            Assert.Equal(TrackA, audio.CurrentMusic!.Value.TrackId);
            Assert.Single(loader.LoadRequests, r => r.ResourceId.Equals(TrackA));
        }

        [Fact]
        public void Play_LoadFails_RecordsDiagnostic_AndDoesNotPlay_NorRetry()
        {
            var (player, audio, loader) = NewPlayer(deferLoads: true);

            player.Play(TrackA);
            loader.FailPending(TrackA);

            Assert.Null(audio.CurrentMusic);
            Assert.NotEmpty(((global::Presentation.VfxSfx.Contracts.PresentationDiagnosticsRecorder)player.Diagnostics).Warnings);
            Assert.Null(player.Requested);

            player.Play(TrackA);
            Assert.Single(loader.LoadRequests);
            Assert.Null(audio.CurrentMusic);
        }

        [Fact]
        public void Stop_CancelsPendingRequest_AndStopsAudio()
        {
            var (player, audio, loader) = NewPlayer(deferLoads: true);

            player.Play(TrackA);
            player.Stop(1.5);
            loader.CompletePending(TrackA);

            Assert.Null(audio.CurrentMusic);
            Assert.Equal(1.5, audio.LastStopMusicFadeOutSeconds);
            Assert.Null(player.Requested);
            Assert.Null(player.Playing);
        }

        // -------------------------------------------------------------------- MapMusicHost

        private static string MapRow(string name, string? musicRef) =>
            "{\"id\": \"world." + name + "\", \"scene_ref\": \"scene." + name + "\", \"nav_ref\": \"nav." + name + "\", " +
            "\"spawn_points\": [{\"position\": {\"x\": 0, \"y\": 0}}]" +
            (musicRef != null ? ", \"music_ref\": \"" + musicRef + "\"" : "") + "}";

        private sealed class MapHarness
        {
            public StubAudio Audio { get; } = new StubAudio();
            public StubResourceLoader Loader { get; } = new StubResourceLoader();
            public Core.Foundation.SceneRouter.SceneRouter Router { get; }
            public MusicPlayer Player { get; }
            public MapMusicHost Host { get; }

            public MapHarness(params string[] rows)
            {
                var bus = new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
                var source = new InMemoryDataSource().Add("world.map",
                    "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" + string.Join(",", rows) + "]}");
                var registry = new Core.Foundation.DataRegistry.DataRegistry(source, bus);
                registry.RegisterSchema(WorldMapSchema.Table);
                Assert.False(registry.LoadAll().IsBlocking);
                var app = new AppStateHost(bus, AppStateMachineConfig.Default().AllowTransition(AppState.Loading, AppState.MainMenu));
                app.RequestTransition(AppState.MainMenu);
                Router = new Core.Foundation.SceneRouter.SceneRouter(registry, Loader, app, new WorldSim(bus), new HookRegistry(bus), bus);
                Player = new MusicPlayer(Audio, Loader);
                Host = new MapMusicHost(Router, registry, Player);
            }

            public void Enter(string name)
            {
                Loader.Register(new Id("scene." + name));
                Loader.Register(new Id("nav." + name));
                Loader.Register(new Id("world." + name));
                Router.LoadScene(new Id("world." + name));
                Router.Update();
            }
        }

        [Fact]
        public void MapMusicHost_SwitchingMaps_PlaysEachMapsMusicRef_AndSameTrackIsNotRestarted()
        {
            var h = new MapHarness(MapRow("town", TrackA.Value), MapRow("dungeon", TrackB.Value), MapRow("annex", TrackB.Value));
            h.Loader.Register(TrackA);
            h.Loader.Register(TrackB);

            h.Enter("town");
            Assert.Equal(TrackA, h.Audio.CurrentMusic!.Value.TrackId);

            h.Enter("dungeon");
            Assert.Equal(TrackB, h.Audio.CurrentMusic!.Value.TrackId);

            h.Audio.StopMusic(0);               // 探针：之后进入同曲目的地图不应重新调用 PlayMusic
            h.Enter("annex");
            Assert.Null(h.Audio.CurrentMusic);
        }

        [Fact]
        public void MapMusicHost_MapWithoutMusicRef_LeavesCurrentMusicAlone()
        {
            var h = new MapHarness(MapRow("town", TrackA.Value), MapRow("quiet", null));
            h.Loader.Register(TrackA);

            h.Enter("town");
            h.Enter("quiet");

            Assert.Equal(TrackA, h.Audio.CurrentMusic!.Value.TrackId);
            Assert.Equal(TrackA, h.Player.Requested);
        }

        [Fact]
        public void MapMusicHost_Dispose_UnhooksSceneRouter()
        {
            var h = new MapHarness(MapRow("town", TrackA.Value));
            h.Loader.Register(TrackA);
            h.Host.Dispose();

            h.Enter("town");

            Assert.Null(h.Audio.CurrentMusic);
        }
    }
}
