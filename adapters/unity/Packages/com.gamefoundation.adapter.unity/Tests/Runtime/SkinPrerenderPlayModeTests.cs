#nullable enable
// SkinPrerenderPlayModeTests：标准骨骼蒙皮预渲染（手感设计/04 第 6.2 节、ADR-0140）引擎侧执行器的验收。
//
// 用标准假人预制体当蒙皮，经执行器（编辑器程序集里的 SkinPrerenderRunner，反射调用其 Run(作业路径)）把一个键子集
// （待机、走路、cast、cast.quick、空中攻击、击飞）渲染到两个方向档 × （整身 / 身体层 / 一个装备层），再逐项断言：
//   1 帧数与序列帧版假人姿势集规格（assets/_placeholder/std_dummy_poses.json，独立构造）逐键一致；
//   2 每一帧非空，待机首帧脚点落在枢轴（144×144 画布、枢轴 (72,140)）±3 像素内；
//   3 装备层与身体层逐像素对齐：轮廓并集 == 整身轮廓，未被装备层遮住的身体层像素与整身逐像素相同；
//   4 同一台机器同一作业渲染两次逐字节一致；
//   5 产物喂给现有序列帧播放路径（UnityResourceLoader -> 生产的关键帧换算 -> UnityFrameAnimPlayer）：
//     cast / cast.quick 的 release、空中攻击的 hit 与 hit_frame 触发时刻，与假人姿势集（同一播放路径）逐位相同，
//     且 release 触发时刻与逻辑释放点（相位 1 的毫秒数）之差为 0（帧量化内）；
//   6 拒绝路径：没有标准骨骼的预制体、找不到对象的装备层选择器、缺剪辑资产，都返回"拒绝"并列出清单，不写帧文件。
//
// 判断记录：
// - 执行器在编辑器程序集（只编进编辑器），本测试程序集是全平台程序集，所以不引用它的类型，只经反射取 Run 方法；作业/结果
//   用本类里字段同名的镜像类型（JsonUtility 按字段名读写）。方法或字段改了，测试在第一次调用就失败并指出，不会静默通过。
// - 命令行驱动器（toolchain/prerender_skin）的组装、自检、数据行由 pytest 用桩后端覆盖；真实"驱动器 + 引擎批处理"的端到端
//   不进门禁（门禁里引擎工程被 Unity 步骤占着工程锁），在工具 README 里记了实跑的数值。
// - 序列帧资产在测试里就地组装（单行图集 + frames.json，格式同假人姿势集），数据行事件取自假人姿势集规格——预渲染的事件由
//   同一套函数产出，所以本测试验的是"渲染出来的帧数与帧时长经生产的换算与播放路径后，事件触发时刻对得上"，不是重新验事件表。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using NUnit.Framework;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:render")]
    public sealed class SkinPrerenderPlayModeTests : PlayModeTestBase
    {
        // ---- 作业/结果镜像（字段名与 SkinPrerenderRunner.Job / Result 一一对应）----
        [Serializable] private class SlotM { public string name = ""; public float yaw_deg; }
        [Serializable] private class LayerM { public string name = ""; public string[] selectors = Array.Empty<string>(); }
        [Serializable] private class ClipM { public string stem = ""; public float[] sample_ms = Array.Empty<float>(); }
        [Serializable]
        private class JobM
        {
            public string skin = "";
            public string out_dir = "";
            public string result_path = "";
            public string clips_resources_dir = "GameFoundation/anim_clips";
            public string[] required_bones = Array.Empty<string>();
            public int canvas_w = 144;
            public int canvas_h = 144;
            public float pixels_per_unit = 32f;
            public float pivot_x = 72f;
            public float pivot_y = 140f;
            public float camera_pitch_deg;
            public string lighting = "unlit";
            public float[] light_euler_deg = { 50f, -30f, 0f };
            public float[] light_color = { 1f, 1f, 1f };
            public float[] ambient_color = { 1f, 1f, 1f };
            public float position_scale = 1f;
            public int unity_layer = 30;
            public SlotM[] slots = Array.Empty<SlotM>();
            public LayerM[] layers = Array.Empty<LayerM>();
            public ClipM[] clips = Array.Empty<ClipM>();
        }

        [Serializable] private class FileM { public string file = ""; public string stem = ""; public string slot = ""; public string layer = ""; public int frames; }
        [Serializable]
        private class ResultM
        {
            public string status = "";
            public string message = "";
            public string[] missing_bones = Array.Empty<string>();
            public string[] duplicate_bones = Array.Empty<string>();
            public string[] bad_selectors = Array.Empty<string>();
            public string[] overlapping_renderers = Array.Empty<string>();
            public string[] missing_clips = Array.Empty<string>();
            public FileM[] files = Array.Empty<FileM>();
        }

        private const string SkinPath = "Assets/Resources/GameFoundation/models/std_dummy_biped.prefab";
        private const string NoBonesSkinPath = "Assets/Resources/GameFoundation/models/placeholder_biped.prefab";
        private const string HandSelector = "hips/spine/upper_arm_r/forearm_r/hand_r";
        private const int W = 144;
        private const int H = 144;
        private const int PivotX = 72;
        private const int PivotY = 140;
        private const int FrameBytes = W * H * 4;

        private static readonly string[] Keys = { "idle", "move.walk", "cast", "cast.quick", "attack.air.unarmed", "hit.launch" };
        private static readonly string[] Bones =
        {
            "hips", "spine", "head", "upper_arm_r", "forearm_r", "hand_r", "socket.main_hand", "upper_arm_l", "forearm_l", "hand_l",
            "socket.off_hand", "thigh_r", "shin_r", "foot_r", "thigh_l", "shin_l", "foot_l",
        };

        private string _tmp = "";
        private bool _overridden;

        [SetUp]
        public void SetUp()
        {
            _tmp = Path.Combine(Path.GetTempPath(), "skinpr_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(_tmp);
        }

        [TearDown]
        public void TearDown()
        {
            if (_overridden)
            {
                UnityResourceLoader.RootDirOverrideForTests = null;
                _overridden = false;
            }
            try { Directory.Delete(_tmp, true); } catch (IOException) { /* 临时目录清理失败不影响结论 */ }
        }

        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        private static JsonObject ReadJson(string relative) => (JsonObject)JsonReader.Parse(File.ReadAllText(Path.Combine(RepoRoot, relative)));

        private static string StemOf(string key) => "std_dummy_" + key.Replace('.', '_');

        private static MethodInfoHolder Runner => MethodInfoHolder.Instance;

        private sealed class MethodInfoHolder
        {
            public static readonly MethodInfoHolder Instance = new MethodInfoHolder();
            public readonly System.Reflection.MethodInfo Run;

            private MethodInfoHolder()
            {
                var type = Type.GetType("Adapter.Unity.SkinPrerender.Editor.SkinPrerenderRunner, Adapter.Unity.SkinPrerender.Editor");
                Assert.IsNotNull(type, "SkinPrerenderRunner 应存在（包的编辑器程序集 Adapter.Unity.SkinPrerender.Editor）");
                Run = type!.GetMethod("Run", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!;
                Assert.IsNotNull(Run, "SkinPrerenderRunner.Run(string) 应存在（public static）");
            }
        }

        // 序列帧版假人姿势集规格里一个键的相位与帧数 -> 每帧取样时刻（循环取帧起点、单次取帧中点；与工具链同一规则）。
        private sealed class SpecClip
        {
            public int FrameCount;
            public bool Loop;
            public double TotalMs;
            public double FirstPhaseMs;
            public float[] SampleMs = Array.Empty<float>();
            public double[] DurationsS = Array.Empty<double>();
            public List<(string Name, double TimePct)> Events = new List<(string, double)>();
        }

        private static SpecClip ReadSpecClip(JsonObject spec, string key)
        {
            var c = (JsonObject)((JsonObject)spec["clips"])[key];
            var phases = (JsonArray)c["phases"];
            var perPhase = (JsonArray)c["frames_per_phase"];
            var loop = ((JsonBool)c["loop"]).Value;
            var sample = new List<float>();
            var durs = new List<double>();
            double t = 0.0;
            for (var p = 0; p < phases.Count; p++)
            {
                var ms = ((JsonNumber)((JsonObject)phases[p])["ms"]).Value;
                var n = (int)((JsonNumber)perPhase[p]).Value;
                var dur = ms / n;
                for (var i = 0; i < n; i++)
                {
                    sample.Add((float)(loop ? t : t + dur / 2.0));
                    durs.Add(Math.Round(dur / 1000.0, 6));
                    t += dur;
                }
            }
            var result = new SpecClip
            {
                FrameCount = (int)((JsonNumber)c["frame_count"]).Value,
                Loop = loop,
                TotalMs = ((JsonNumber)c["total_ms"]).Value,
                FirstPhaseMs = ((JsonNumber)((JsonObject)phases[0])["ms"]).Value,
                SampleMs = sample.ToArray(),
                DurationsS = durs.ToArray(),
            };
            foreach (var e in (JsonArray)c["events"])
            {
                var o = (JsonObject)e;
                result.Events.Add((((JsonString)o["name"]).Value, ((JsonNumber)o["time_pct"]).Value));
            }
            return result;
        }

        private JobM MakeJob(string outDir, string skin, IReadOnlyList<string> keys, JsonObject spec, bool layers = true)
        {
            var job = new JobM
            {
                skin = skin,
                out_dir = outDir,
                result_path = Path.Combine(outDir, "result.json"),
                required_bones = Bones,
                slots = new[] { new SlotM { name = "front", yaw_deg = 0f }, new SlotM { name = "side_r", yaw_deg = 90f } },
                layers = layers ? new[] { new LayerM { name = "hand_main", selectors = new[] { HandSelector } } } : Array.Empty<LayerM>(),
            };
            var clips = new List<ClipM>();
            foreach (var key in keys)
            {
                clips.Add(new ClipM { stem = StemOf(key), sample_ms = ReadSpecClip(spec, key).SampleMs });
            }
            job.clips = clips.ToArray();
            return job;
        }

        private (int Code, ResultM Result) Run(JobM job)
        {
            Directory.CreateDirectory(job.out_dir);
            var jobPath = Path.Combine(job.out_dir, "job.json");
            File.WriteAllText(jobPath, JsonUtility.ToJson(job), new UTF8Encoding(false));
            var code = (int)Runner.Run.Invoke(null, new object[] { jobPath });
            Assert.IsTrue(File.Exists(job.result_path), "执行器应总是写出结果文件");
            return (code, JsonUtility.FromJson<ResultM>(File.ReadAllText(job.result_path)));
        }

        private static string RawPath(string dir, string key, string slot, string variant) =>
            Path.Combine(dir, StemOf(key) + "__" + slot + "__" + variant + ".rgba");

        private static int Alpha(byte[] data, int frame, int x, int y) => data[frame * FrameBytes + (y * W + x) * 4 + 3];

        private static int OpaqueCount(byte[] data, int frame)
        {
            var n = 0;
            for (var i = 0; i < W * H; i++)
            {
                if (data[frame * FrameBytes + i * 4 + 3] > 0) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------------------------------------ 渲染与逐项断言

        [Test]
        public void Render_Subset_FrameCountsNonEmptyPivotAndLayerAlignment_AndTwoRendersAreByteIdentical()
        {
            var spec = ReadJson("assets/_placeholder/std_dummy_poses.json");
            var dirA = Path.Combine(_tmp, "a");
            var (code, result) = Run(MakeJob(dirA, SkinPath, Keys, spec));
            Assert.AreEqual(0, code, "渲染应成功：" + result.message);
            Assert.AreEqual("ok", result.status, result.message);
            Assert.AreEqual(Keys.Length * 2 * 3, result.files.Length, "每个键 × 2 方向 × （整身、身体层、1 个装备层）一个文件");

            foreach (var key in Keys)
            {
                var want = ReadSpecClip(spec, key);
                foreach (var slot in new[] { "front", "side_r" })
                {
                    var all = File.ReadAllBytes(RawPath(dirA, key, slot, "all"));
                    var body = File.ReadAllBytes(RawPath(dirA, key, slot, "body"));
                    var hand = File.ReadAllBytes(RawPath(dirA, key, slot, "hand_main"));
                    Assert.AreEqual(want.FrameCount * FrameBytes, all.Length, $"{key}/{slot} 整身帧数应等于假人姿势集规格的 {want.FrameCount}");
                    Assert.AreEqual(all.Length, body.Length);
                    Assert.AreEqual(all.Length, hand.Length);
                    for (var f = 0; f < want.FrameCount; f++)
                    {
                        Assert.GreaterOrEqual(OpaqueCount(all, f), 16, $"{key}/{slot} 第 {f} 帧整身不应为空");
                        Assert.GreaterOrEqual(OpaqueCount(body, f), 16, $"{key}/{slot} 第 {f} 帧身体层不应为空");
                        Assert.GreaterOrEqual(OpaqueCount(hand, f), 1, $"{key}/{slot} 第 {f} 帧装备层不应为空");
                        AssertLayersAligned(all, body, hand, f, $"{key}/{slot}/{f}");
                    }
                }
            }

            // 待机首帧：脚点落在枢轴上（轮廓底边、水平中心）
            foreach (var slot in new[] { "front", "side_r" })
            {
                var idle = File.ReadAllBytes(RawPath(dirA, "idle", slot, "all"));
                int minX = W, maxX = -1, maxY = -1;
                for (var y = 0; y < H; y++)
                {
                    for (var x = 0; x < W; x++)
                    {
                        if (Alpha(idle, 0, x, y) == 0) continue;
                        minX = Math.Min(minX, x);
                        maxX = Math.Max(maxX, x);
                        maxY = Math.Max(maxY, y);
                    }
                }
                Assert.AreEqual(PivotY, maxY + 1, 3, $"待机 {slot} 首帧轮廓底边应在枢轴纵坐标 {PivotY} ±3 内");
                Assert.AreEqual(PivotX, (minX + maxX + 1) / 2.0, 3.0, $"待机 {slot} 首帧轮廓水平中心应在枢轴横坐标 {PivotX} ±3 内");
            }

            // 同机同作业再渲染一次：逐字节一致
            var dirB = Path.Combine(_tmp, "b");
            var again = Run(MakeJob(dirB, SkinPath, Keys, spec));
            Assert.AreEqual(0, again.Code, again.Result.message);
            foreach (var f in result.files)
            {
                CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(dirA, f.file)), File.ReadAllBytes(Path.Combine(dirB, f.file)),
                    f.file + "：同一台机器、同一作业渲染两次应逐字节一致");
            }
        }

        private static void AssertLayersAligned(byte[] all, byte[] body, byte[] hand, int frame, string where)
        {
            var o = frame * FrameBytes;
            for (var i = 0; i < W * H; i++)
            {
                var p = o + i * 4;
                var a = all[p + 3] > 0;
                var b = body[p + 3] > 0;
                var l = hand[p + 3] > 0;
                if (a != (b || l))
                {
                    Assert.Fail($"{where}：像素 {i} 轮廓不一致（整身 {a}，身体层 {b}，装备层 {l}）");
                }
                if (b && !l && (all[p] != body[p] || all[p + 1] != body[p + 1] || all[p + 2] != body[p + 2]))
                {
                    Assert.Fail($"{where}：像素 {i} 未被装备层遮住，身体层颜色应与整身相同");
                }
                if (l && !b && (all[p] != hand[p] || all[p + 1] != hand[p + 1] || all[p + 2] != hand[p + 2]))
                {
                    Assert.Fail($"{where}：像素 {i} 只有装备层，颜色应与整身相同");
                }
            }
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------------------------------------------ 材质与光照

        [Test]
        public void Render_UnlitKeepsTheSkinsBaseColor_AndLitModeRendersTheSameSilhouette()
        {
            // 用一份临时的"着色蒙皮"（假人预制体 + 一种 URP Lit 基础色材质）验证 unlit 保色（sRGB 往返无偏）、lit 能渲染且轮廓一致。
            const string folder = "Assets/_SkinPrerenderTmp";
            var spec = ReadJson("assets/_placeholder/std_dummy_poses.json");
            if (!UnityEditor.AssetDatabase.IsValidFolder(folder)) UnityEditor.AssetDatabase.CreateFolder("Assets", "_SkinPrerenderTmp");
            try
            {
                var source = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(SkinPath);
                var inst = UnityEngine.Object.Instantiate(source);
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                Assert.IsNotNull(shader, "URP Lit 着色器应存在");
                var mat = new Material(shader);
                mat.SetColor("_BaseColor", new Color(0.8f, 0.2f, 0.1f, 1f));
                UnityEditor.AssetDatabase.CreateAsset(mat, folder + "/skin.mat");
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
                UnityEditor.PrefabUtility.SaveAsPrefabAsset(inst, folder + "/colored.prefab");
                UnityEngine.Object.DestroyImmediate(inst);

                var unlit = MakeJob(Path.Combine(_tmp, "unlit"), folder + "/colored.prefab", new[] { "idle" }, spec, layers: false);
                var (code, res) = Run(unlit);
                Assert.AreEqual(0, code, res.message);
                var flat = File.ReadAllBytes(RawPath(unlit.out_dir, "idle", "front", "all"));
                var distinctUnlit = new HashSet<int>();
                for (var i = 0; i < W * H; i++)
                {
                    var p = i * 4;
                    if (flat[p + 3] == 0) continue;
                    distinctUnlit.Add(flat[p] << 16 | flat[p + 1] << 8 | flat[p + 2]);
                    Assert.AreEqual(204, flat[p], 3, "unlit：红通道应等于基础色 0.8 x 255");
                    Assert.AreEqual(51, flat[p + 1], 3, "unlit：绿通道应等于基础色 0.2 x 255");
                    Assert.AreEqual(25.5, flat[p + 2], 3, "unlit：蓝通道应等于基础色 0.1 x 255");
                }
                Assert.AreEqual(1, distinctUnlit.Count, "unlit 下整个轮廓是同一种平涂色");

                // lit 模式：本工程用 URP 2D 渲染器（Assets/Settings/URP-2D-Pipeline.asset），2D 渲染器不处理三维受光，
                // 材质按基础色平涂——所以这里只能断言 lit 渲染成功、轮廓与 unlit 逐像素一致，不能断言"受光变色"
                // （受光行为只在三维渲染器的工程里有意义，见工具 README 已知限制；没有自动化用例，如实记录）。
                var lit = MakeJob(Path.Combine(_tmp, "lit"), folder + "/colored.prefab", new[] { "idle" }, spec, layers: false);
                lit.lighting = "lit";
                var (litCode, litRes) = Run(lit);
                Assert.AreEqual(0, litCode, litRes.message);
                var shaded = File.ReadAllBytes(RawPath(lit.out_dir, "idle", "front", "all"));
                Assert.AreEqual(flat.Length, shaded.Length);
                for (var i = 0; i < W * H; i++)
                {
                    Assert.AreEqual(flat[i * 4 + 3] > 0, shaded[i * 4 + 3] > 0, $"lit 与 unlit 的轮廓应一致（像素 {i}）");
                }
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(folder);
            }
        }
#endif

        // ------------------------------------------------------------------------------------------------ 拒绝路径

        [Test]
        public void Render_SkinWithoutStandardBones_IsRefusedWithTheFullMissingList_AndWritesNoFrames()
        {
            var spec = ReadJson("assets/_placeholder/std_dummy_poses.json");
            var dir = Path.Combine(_tmp, "nobones");
            var (code, result) = Run(MakeJob(dir, NoBonesSkinPath, new[] { "idle" }, spec, layers: false));
            Assert.AreEqual(3, code, "缺骨骼应以退出码 3 拒绝");
            Assert.AreEqual("refused", result.status);
            // 占位胶囊体自带一个主手挂点，其余 16 根标准骨骼都缺；清单应一次列全，而不是遇到第一根缺的就停。
            var expectedMissing = new List<string>(Bones);
            expectedMissing.Remove("socket.main_hand");
            CollectionAssert.AreEquivalent(expectedMissing, result.missing_bones, "应一次列出全部缺失的骨骼");
            Assert.AreEqual(0, Directory.GetFiles(dir, "*.rgba").Length, "拒绝时不应写任何帧文件");
        }

        [Test]
        public void Render_UnknownLayerSelector_IsRefused()
        {
            var spec = ReadJson("assets/_placeholder/std_dummy_poses.json");
            var job = MakeJob(Path.Combine(_tmp, "sel"), SkinPath, new[] { "idle" }, spec);
            job.layers = new[] { new LayerM { name = "ghost", selectors = new[] { "no/such/path" } } };
            var (code, result) = Run(job);
            Assert.AreEqual(3, code);
            Assert.AreEqual("refused", result.status);
            Assert.AreEqual(1, result.bad_selectors.Length);
            StringAssert.Contains("ghost:no/such/path", result.bad_selectors[0]);
        }

        [Test]
        public void Render_MissingStandardClipAsset_IsRefused_AndMissingSkinAsset_IsRefused()
        {
            var spec = ReadJson("assets/_placeholder/std_dummy_poses.json");
            var job = MakeJob(Path.Combine(_tmp, "noclip"), SkinPath, new[] { "idle" }, spec, layers: false);
            job.clips = new[] { new ClipM { stem = "std_dummy_no_such_clip", sample_ms = new[] { 0f } } };
            var (code, result) = Run(job);
            Assert.AreEqual(3, code);
            CollectionAssert.AreEqual(new[] { "std_dummy_no_such_clip" }, result.missing_clips);

            var job2 = MakeJob(Path.Combine(_tmp, "noskin"), "Assets/Skins/does_not_exist.prefab", new[] { "idle" }, spec, layers: false);
            var (code2, result2) = Run(job2);
            Assert.AreEqual(3, code2);
            Assert.AreEqual("refused", result2.status);
            StringAssert.Contains("does_not_exist.prefab", result2.message);
        }

        // ------------------------------------------------------------------------------------------------ 现有序列帧播放路径

        private static IEnumerator LoadEffect(UnityResourceLoader loader, Id id, Action<UnityResourceLoader.EffectAsset> done)
        {
            var loaded = false;
            UnityResourceLoader.EffectAsset? asset = null;
            loader.LoadAsync(id, ResourceKind.Effect, (_, ok) => { loaded = ok; });
            var deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                loader.Tick();
                if (loaded && loader.TryGetEffect(id, out var e)) { asset = e; break; }
                yield return null;
            }
            Assert.IsNotNull(asset, id.Value + " 应能加载");
            done(asset!);
        }

        private static IReadOnlyDictionary<string, int>? Keyframes(List<AnimClipEventSpec> events, int frameCount)
        {
            var method = typeof(UnityViewFactory).GetMethod("ComputeKeyframes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(method, "UnityViewFactory.ComputeKeyframes 应存在（私有静态：数据行 events -> 帧下标关键帧表）");
            return (IReadOnlyDictionary<string, int>?)method!.Invoke(null, new object[] { events, frameCount });
        }

        /// <summary>按生产路径播放该效果并返回各标记首次触发时的累计播放秒数（1 毫秒步长）。</summary>
        private static Dictionary<string, double> FireTimes(UnityResourceLoader.EffectAsset effect, SpecClip clip)
        {
            var events = new List<AnimClipEventSpec>();
            foreach (var e in clip.Events) events.Add(new AnimClipEventSpec(e.Name, e.TimePct));
            var keyframes = Keyframes(events, effect.Frames.Length);
            var go = new GameObject("SkinPrerenderFirePlayer");
            try
            {
                go.AddComponent<SpriteRenderer>();
                var player = go.AddComponent<UnityFrameAnimPlayer>();
                var time = new ManualFrameTimeSource();
                player.TimeSource = time;
                var id = new Id("anim.skin_prerender_probe");
                player.RegisterClipFromEffect(id, effect, keyframes);
                var fired = new Dictionary<string, double>(StringComparer.Ordinal);
                player.OnAnimEvent(marker => { if (!fired.ContainsKey(marker)) fired[marker] = player.AdvancedSeconds; });
                player.Play(id, loop: false, speed: 1.0);
                time.SetDelta(0.001);
                for (var i = 0; i < 2000; i++) player.Step();
                return fired;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static void WriteSpriteClip(string root, string stem, byte[] all, SpecClip clip)
        {
            var frames = clip.FrameCount;
            var dir = Path.Combine(root, "sprite_anim", stem);
            Directory.CreateDirectory(dir);
            var width = W * frames;
            var pixels = new Color32[width * H];
            for (var f = 0; f < frames; f++)
            {
                for (var y = 0; y < H; y++)
                {
                    for (var x = 0; x < W; x++)
                    {
                        var p = f * FrameBytes + (y * W + x) * 4;
                        pixels[(H - 1 - y) * width + f * W + x] = new Color32(all[p], all[p + 1], all[p + 2], all[p + 3]);
                    }
                }
            }
            var tex = new Texture2D(width, H, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(pixels);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, "atlas.png"), tex.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.Append("{\n  \"frame_w\": ").Append(W).Append(",\n  \"frame_h\": ").Append(H).Append(",\n  \"fps\": 20,\n  \"frame_duration\": 0.05,\n  \"loop\": ")
              .Append(clip.Loop ? "true" : "false").Append(",\n  \"frames\": [\n");
            for (var f = 0; f < frames; f++)
            {
                sb.Append("    { \"index\": ").Append(f).Append(", \"x\": ").Append(f * W).Append(", \"y\": 0, \"w\": ").Append(W).Append(", \"h\": ").Append(H)
                  .Append(", \"duration\": ").Append(clip.DurationsS[f].ToString("R", inv)).Append(" }").Append(f < frames - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n}\n");
            File.WriteAllText(Path.Combine(dir, "frames.json"), sb.ToString(), new UTF8Encoding(false));
        }

        [UnityTest]
        public IEnumerator PrerenderedClips_ThroughTheExistingSpritePlayPath_FireReleaseAndHitAtTheSameTimesAsTheProceduralDummy()
        {
            var spec = ReadJson("assets/_placeholder/std_dummy_poses.json");
            var dir = Path.Combine(_tmp, "play");
            var keys = new[] { "cast", "cast.quick", "attack.air.unarmed" };
            var (code, result) = Run(MakeJob(dir, SkinPath, keys, spec, layers: false));
            Assert.AreEqual(0, code, result.message);

            var root = Path.Combine(_tmp, "assets");
            var clips = new Dictionary<string, SpecClip>();
            foreach (var key in keys)
            {
                clips[key] = ReadSpecClip(spec, key);
                WriteSpriteClip(root, "pr_" + key.Replace('.', '_'), File.ReadAllBytes(RawPath(dir, key, "front", "all")), clips[key]);
            }

            // 预渲染产物：现有序列帧播放路径
            UnityResourceLoader.RootDirOverrideForTests = root;
            _overridden = true;
            var pre = new Dictionary<string, UnityResourceLoader.EffectAsset>();
            var preLoader = new UnityResourceLoader();
            foreach (var key in keys)
            {
                yield return LoadEffect(preLoader, new Id("sprite_anim.pr_" + key.Replace('.', '_')), e => pre[key] = e);
            }

            // 假人姿势集：同一播放路径（占位资产根）
            UnityResourceLoader.RootDirOverrideForTests = Path.Combine(RepoRoot, "assets", "_placeholder");
            var proc = new Dictionary<string, UnityResourceLoader.EffectAsset>();
            var procLoader = new UnityResourceLoader();
            foreach (var key in keys)
            {
                yield return LoadEffect(procLoader, new Id("sprite_anim." + StemOf(key)), e => proc[key] = e);
            }

            foreach (var key in keys)
            {
                Assert.AreEqual(proc[key].Frames.Length, pre[key].Frames.Length, $"{key}：预渲染与假人姿势集的帧数一致");
                var fp = FireTimes(pre[key], clips[key]);
                var fq = FireTimes(proc[key], clips[key]);
                CollectionAssert.AreEquivalent(fq.Keys, fp.Keys, $"{key}：触发的标记集合一致");
                foreach (var kv in fq)
                {
                    Assert.AreEqual(kv.Value, fp[kv.Key], 1e-9, $"{key}：标记 {kv.Key} 的触发时刻与假人姿势集逐位相同");
                }
            }

            // release 触发时刻与逻辑释放点（相位 1 的毫秒数）之差为 0（帧量化 + 1 毫秒步长内）；预渲染与假人姿势集的差同为 0
            foreach (var key in new[] { "cast", "cast.quick" })
            {
                var t = FireTimes(pre[key], clips[key])["release"];
                var tp = FireTimes(proc[key], clips[key])["release"];
                Assert.AreEqual(clips[key].FirstPhaseMs / 1000.0, t, 0.0015, $"{key}：release 触发时刻与逻辑释放点之差应为 0");
                Assert.AreEqual(0.0, t - tp, 1e-9, $"{key}：预渲染与假人姿势集的 release 时刻之差应为 0");
            }
            var hit = FireTimes(pre["attack.air.unarmed"], clips["attack.air.unarmed"]);
            Assert.AreEqual(hit["hit"], hit["hit_frame"], 1e-9, "空中攻击：hit 与 hit_frame 同刻触发（W5/W6 命中对齐保持）");
        }
    }
}
