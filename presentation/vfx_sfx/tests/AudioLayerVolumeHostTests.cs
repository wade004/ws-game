using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SaveSystem;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.Presentation.VfxSfx
{
    /// <summary>记录 <see cref="ISfxPlayer.SetLayerVolume"/> 调用的最小假实现（其余成员本测试不关心，
    /// 恒返回默认值/空操作）。</summary>
    internal sealed class RecordingSfxPlayer : ISfxPlayer
    {
        public readonly Dictionary<string, double> LayerVolumes = new Dictionary<string, double>(System.StringComparer.Ordinal);

        public SfxHandle? Play(Id sfxId, Vec2? at) => null;
        public void Stop(SfxHandle handle) { }
        public void SetLayerVolume(string layer, double volume) => LayerVolumes[layer] = volume;
        public void SetLayerMuted(string layer, bool muted) { }
        public int PendingPlayCount => 0;
    }

    /// <summary><see cref="AudioLayerVolumeHost"/>（缺口 12）用例：见任务书"AudioLayerVolumeHost
    /// （持久化/恢复/路由到 IAudio 与 ISfxPlayer）"，补齐审计发现的"真实实现零测试"缺口。</summary>
    public class AudioLayerVolumeHostTests
    {
        [Fact]
        public void Layers_IsSfxLayersDeduplicated_PlusMusic_InOrder()
        {
            var sfxPlayer = new RecordingSfxPlayer();
            var audio = new StubAudio();
            var store = new SettingsStore(new StubFileSystem());

            var host = new AudioLayerVolumeHost(new[] { "combat", "ui", "combat" }, sfxPlayer, audio, store);

            Assert.Equal(new[] { "combat", "ui", "music" }, host.Layers);
        }

        [Fact]
        public void Layers_SfxLayersAlreadyContainingMusic_DoesNotDuplicate()
        {
            var sfxPlayer = new RecordingSfxPlayer();
            var audio = new StubAudio();
            var store = new SettingsStore(new StubFileSystem());

            var host = new AudioLayerVolumeHost(new[] { "music", "combat" }, sfxPlayer, audio, store);

            Assert.Equal(new[] { "music", "combat" }, host.Layers);
        }

        [Fact]
        public void GetVolume_UnknownLayer_ReturnsDefaultFullVolume()
        {
            var sfxPlayer = new RecordingSfxPlayer();
            var audio = new StubAudio();
            var store = new SettingsStore(new StubFileSystem());

            var host = new AudioLayerVolumeHost(new[] { "combat" }, sfxPlayer, audio, store);

            Assert.Equal(1.0, host.GetVolume("unknown_layer"));
        }

        [Fact]
        public void Construct_NoPersistedSettings_AppliesDefaultFullVolume_ToAllLayers()
        {
            var sfxPlayer = new RecordingSfxPlayer();
            var audio = new StubAudio();
            var store = new SettingsStore(new StubFileSystem());

            var host = new AudioLayerVolumeHost(new[] { "combat" }, sfxPlayer, audio, store);

            Assert.Equal(1.0, host.GetVolume("combat"));
            Assert.Equal(1.0, host.GetVolume(AudioLayerVolumeHost.MusicLayer));
            Assert.Equal(1.0, sfxPlayer.LayerVolumes["combat"]);
            Assert.Equal(1.0, audio.BusVolumes[AudioBus.Music]);
        }

        [Fact]
        public void SetVolume_SfxLayer_RoutesToSfxPlayer_NotAudioBus()
        {
            var sfxPlayer = new RecordingSfxPlayer();
            var audio = new StubAudio();
            var store = new SettingsStore(new StubFileSystem());
            var host = new AudioLayerVolumeHost(new[] { "combat" }, sfxPlayer, audio, store);

            host.SetVolume("combat", 0.4);

            Assert.Equal(0.4, host.GetVolume("combat"));
            Assert.Equal(0.4, sfxPlayer.LayerVolumes["combat"]);
            // 音乐总线音量维持构造期应用的默认值，不受本次对 "combat" 层的调整影响。
            Assert.Equal(1.0, audio.BusVolumes[AudioBus.Music]);
        }

        [Fact]
        public void SetVolume_MusicLayer_RoutesToAudioBus_NotSfxPlayer()
        {
            var sfxPlayer = new RecordingSfxPlayer();
            var audio = new StubAudio();
            var store = new SettingsStore(new StubFileSystem());
            var host = new AudioLayerVolumeHost(new[] { "combat" }, sfxPlayer, audio, store);
            sfxPlayer.LayerVolumes.Clear(); // 清掉构造期的默认音量写入，只看本次 SetVolume 之后的增量。

            host.SetVolume(AudioLayerVolumeHost.MusicLayer, 0.6);

            Assert.Equal(0.6, host.GetVolume(AudioLayerVolumeHost.MusicLayer));
            Assert.Equal(0.6, audio.BusVolumes[AudioBus.Music]);
            Assert.False(sfxPlayer.LayerVolumes.ContainsKey(AudioLayerVolumeHost.MusicLayer));
        }

        [Fact]
        public void SetVolume_PersistsToSettingsStore_UnderAudioVolumeKey()
        {
            var sfxPlayer = new RecordingSfxPlayer();
            var audio = new StubAudio();
            var store = new SettingsStore(new StubFileSystem());
            var host = new AudioLayerVolumeHost(new[] { "combat" }, sfxPlayer, audio, store);

            host.SetVolume("combat", 0.25);

            var saved = store.Load();
            Assert.True(saved.TryGetValue("audio.volume.combat", out var value));
            Assert.Equal(0.25, Assert.IsType<Core.Foundation.Common.Json.JsonNumber>(value).Value);
        }

        [Fact]
        public void SetVolume_PreservesOtherExistingSettingsKeys()
        {
            var sfxPlayer = new RecordingSfxPlayer();
            var audio = new StubAudio();
            var fs = new StubFileSystem();
            var store = new SettingsStore(fs);

            // 模拟另一个设置写入方（如 ShellHost 的 input_bindings）已经先写过一份文档。
            var builder = new Core.Foundation.Common.Json.JsonObjectBuilder();
            builder.Add("input_bindings", new Core.Foundation.Common.Json.JsonString("some_binding"));
            store.Save(builder.Build());

            var host = new AudioLayerVolumeHost(new[] { "combat" }, sfxPlayer, audio, store);
            host.SetVolume("combat", 0.7);

            var saved = store.Load();
            Assert.True(saved.TryGetValue("input_bindings", out var preserved));
            Assert.Equal("some_binding", Assert.IsType<Core.Foundation.Common.Json.JsonString>(preserved).Value);
            Assert.True(saved.TryGetValue("audio.volume.combat", out var volumeValue));
            Assert.Equal(0.7, Assert.IsType<Core.Foundation.Common.Json.JsonNumber>(volumeValue).Value);
        }

        [Fact]
        public void Construct_RestoresPersistedVolume_FromPreviousHostInstance_OnSameStore()
        {
            var fs = new StubFileSystem();
            var store1 = new SettingsStore(fs);
            var host1 = new AudioLayerVolumeHost(new[] { "combat" }, new RecordingSfxPlayer(), new StubAudio(), store1);
            host1.SetVolume("combat", 0.33);
            host1.SetVolume(AudioLayerVolumeHost.MusicLayer, 0.55);

            // 重建宿主（同一份设置存储，模拟重启进程）：GetVolume 与真正应用到 IAudio/ISfxPlayer 的
            // 音量都应恢复到上次调整的值，不只是"读得到"。
            var store2 = new SettingsStore(fs);
            var sfxPlayer2 = new RecordingSfxPlayer();
            var audio2 = new StubAudio();
            var host2 = new AudioLayerVolumeHost(new[] { "combat" }, sfxPlayer2, audio2, store2);

            Assert.Equal(0.33, host2.GetVolume("combat"));
            Assert.Equal(0.55, host2.GetVolume(AudioLayerVolumeHost.MusicLayer));
            Assert.Equal(0.33, sfxPlayer2.LayerVolumes["combat"]);
            Assert.Equal(0.55, audio2.BusVolumes[AudioBus.Music]);
        }
    }
}
