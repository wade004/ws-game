using Adapters.Stub;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Presentation.Camera;
using Xunit;

namespace Tests.PresentationCamera
{
    /// <summary>
    /// 玩家手感强度宿主（ADR-0148）：四个 0..1 系数（震屏/镜头冲击/闪白/手柄震动）读写设置文件的 <c>feel.intensity.*</c> 键；
    /// 缺省全 1、越界夹到 [0,1]、跨实例持久化、命名强度设置（<c>camera_user_intensity_setting</c>）按名读取。
    /// </summary>
    public sealed class FeelIntensityHostTests
    {
        private static (FeelIntensityHost Host, StubFileSystem Fs) Make(StubFileSystem? fs = null)
        {
            fs ??= new StubFileSystem();
            return (new FeelIntensityHost(new SettingsStore(fs)), fs);
        }

        [Theory]
        [InlineData(FeelIntensityKind.Shake)]
        [InlineData(FeelIntensityKind.Impulse)]
        [InlineData(FeelIntensityKind.Flash)]
        [InlineData(FeelIntensityKind.Rumble)]
        public void Defaults_AreOne_ForEveryKind(FeelIntensityKind kind) => Assert.Equal(1.0, Make().Host.Get(kind));

        [Fact]
        public void Set_PersistsToTheSettingsFile_AndANewHostOverTheSameFileReadsItBack()
        {
            var (host, fs) = Make();
            host.Set(FeelIntensityKind.Flash, 0.25);
            host.Set(FeelIntensityKind.Rumble, 0.0);

            var again = Make(fs).Host;
            Assert.Equal(0.25, again.Get(FeelIntensityKind.Flash));
            Assert.Equal(0.0, again.Get(FeelIntensityKind.Rumble));
            Assert.Equal(1.0, again.Get(FeelIntensityKind.Shake)); // 没设过的保持缺省
        }

        [Theory]
        [InlineData(-0.5, 0.0)]
        [InlineData(7.0, 1.0)]
        [InlineData(0.4, 0.4)]
        public void Set_ClampsToTheUnitInterval(double input, double expected)
        {
            var host = Make().Host;
            host.Set(FeelIntensityKind.Shake, input);
            Assert.Equal(expected, host.Get(FeelIntensityKind.Shake));
        }

        [Fact]
        public void Set_NonFinite_Throws() =>
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Make().Host.Set(FeelIntensityKind.Shake, double.NaN));

        [Fact]
        public void Set_KeepsOtherSettingsInTheFile_AndNamedSettingsAreReadByName()
        {
            var fs = new StubFileSystem();
            var store = new SettingsStore(fs);
            var builder = new JsonObjectBuilder();
            builder.Add("settings.camera_intensity", new JsonNumber(0.6));
            builder.Add("audio.master", new JsonNumber(0.9));
            Assert.True(store.Save(builder.Build()));

            var host = new FeelIntensityHost(store);
            host.Set(FeelIntensityKind.Impulse, 0.5);

            var data = new SettingsStore(fs).Load();
            Assert.True(data.TryGetValue("audio.master", out var master));
            Assert.Equal(0.9, ((JsonNumber)master).Value);
            Assert.Equal(0.6, host.GetSetting("settings.camera_intensity"));
            Assert.Equal(1.0, host.GetSetting("settings.never_written"));
        }

        [Fact]
        public void ReservedKeys_AreTheFourFeelIntensityKeys()
        {
            Assert.True(FeelIntensityKeys.IsReserved(FeelIntensityKeys.Of(FeelIntensityKind.Rumble)));
            Assert.False(FeelIntensityKeys.IsReserved("settings.camera_intensity"));
            Assert.Equal("feel.intensity.flash", FeelIntensityKeys.Of(FeelIntensityKind.Flash));
        }
    }
}
