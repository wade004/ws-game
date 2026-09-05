#nullable enable
// AudioScenarios：IAudio 契约一致性场景（见 02_引擎适配层.md 第 1.4 节 / ADR-0016 决策 3）。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class AudioScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IAudio>> All = new[]
        {
            new ConformanceScenario<IAudio>("StopSfx_未知句柄不抛异常", StopSfx_UnknownHandle_DoesNotThrow),
            new ConformanceScenario<IAudio>("SetBusVolume_全部总线不抛异常", SetBusVolume_AllBuses_DoesNotThrow),
            new ConformanceScenario<IAudio>("PlayMusic_StopMusic_淡入淡出不抛异常", PlayThenStopMusic_DoesNotThrow),
        };

        private static readonly Id SoundId = new Id("sfx.conformance_placeholder");
        private static readonly Id TrackId = new Id("music.conformance_placeholder");

        private static IEnumerator StopSfx_UnknownHandle_DoesNotThrow(IAudio audio, IConformanceAssert assert, ConformanceContext ctx)
        {
            var unknown = new SfxHandle(int.MaxValue - 1);
            assert.DoesNotThrow(() => audio.StopSfx(unknown), "契约约定：停止一个已停止/不存在的句柄是正常场景，不应抛异常");

            var played = audio.PlaySfx(SoundId, volume: 1.0, pitch: 1.0, position: null);
            assert.DoesNotThrow(() => audio.StopSfx(played), "StopSfx 对刚播放的句柄不应抛异常");
            assert.DoesNotThrow(() => audio.StopSfx(played), "对同一句柄重复调用 StopSfx 不应抛异常（已停止的句柄同样是正常场景）");
            yield break;
        }

        private static IEnumerator SetBusVolume_AllBuses_DoesNotThrow(IAudio audio, IConformanceAssert assert, ConformanceContext ctx)
        {
            foreach (AudioBus bus in System.Enum.GetValues(typeof(AudioBus)))
            {
                var capturedBus = bus;
                assert.DoesNotThrow(() => audio.SetBusVolume(capturedBus, 0.5), $"SetBusVolume({capturedBus}) 不应抛异常");
            }
            yield break;
        }

        private static IEnumerator PlayThenStopMusic_DoesNotThrow(IAudio audio, IConformanceAssert assert, ConformanceContext ctx)
        {
            assert.DoesNotThrow(() => audio.PlayMusic(TrackId, fadeInSeconds: 0, loop: true), "PlayMusic（无淡入）不应抛异常");
            assert.DoesNotThrow(() => audio.StopMusic(fadeOutSeconds: 0), "StopMusic（无淡出）不应抛异常");
            assert.DoesNotThrow(() => audio.PlayMusic(TrackId, fadeInSeconds: 0.2, loop: false), "PlayMusic（带淡入）不应抛异常");
            assert.DoesNotThrow(() => audio.StopMusic(fadeOutSeconds: 0.2), "StopMusic（带淡出）不应抛异常");
            yield break;
        }
    }
}
