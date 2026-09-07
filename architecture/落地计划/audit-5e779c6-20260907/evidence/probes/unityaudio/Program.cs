using System.Reflection;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;

var root = new GameObject("AudioRoot");
var audio = new UnityAudio(root.transform, new UnityResourceLoader());
var tick = typeof(UnityAudio).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic)!;
void Tick(double seconds) => tick.Invoke(audio, new object[] { seconds });

var musicA = (AudioSource)typeof(UnityAudio).GetField("_musicSourceA", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(audio)!;
var musicB = (AudioSource)typeof(UnityAudio).GetField("_musicSourceB", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(audio)!;

audio.PlayMusic(new Id("music.a"), 1.0, true);
Console.WriteLine($"MUSIC_AFTER_PLAY incoming=B? clipA={musicA.clip?.Name ?? "null"} clipB={musicB.clip?.Name ?? "null"} playingA={musicA.isPlaying} playingB={musicB.isPlaying} volA={musicA.volume:0.###} volB={musicB.volume:0.###}");
Tick(0.5);
Console.WriteLine($"MUSIC_AFTER_TICK_0.5 playingA={musicA.isPlaying} playingB={musicB.isPlaying} volA={musicA.volume:0.###} volB={musicB.volume:0.###}");
audio.StopMusic(0);
Console.WriteLine($"MUSIC_AFTER_STOP_0 playingA={musicA.isPlaying} playingB={musicB.isPlaying}");

var sfx = audio.PlaySfx(new Id("sfx.one"), 1, 1, null);
var pool = (System.Collections.IList)typeof(UnityAudio).GetField("_sfxPool", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(audio)!;
Console.WriteLine($"SFX_AFTER_PLAY handle={sfx.Value} pool={pool.Count} activeSource={(pool[0]!.GetType().GetField("Source")!.GetValue(pool[0]) as AudioSource)!.isPlaying}");
var sfxSource = (AudioSource)pool[0]!.GetType().GetField("Source")!.GetValue(pool[0])!;
sfxSource.SimulateNaturalEnd();
Tick(1.0);
Console.WriteLine($"SFX_AFTER_NATURAL_END pool={pool.Count} activeFlag={pool[0]!.GetType().GetField("Active")!.GetValue(pool[0])} sourcePlaying={sfxSource.isPlaying}");
var sfx2 = audio.PlaySfx(new Id("sfx.two"), 1, 1, null);
Console.WriteLine($"SFX_SECOND_PLAY handle={sfx2.Value} pool={pool.Count}");
