#nullable enable
// GenerateStdDummyModelAssets：按规格文件 assets/_placeholder/std_dummy_model_clips.json 生成框架级 model 型假人姿势集的
// 引擎侧资产（architecture/手感设计/04 第 6.1 节、ADR-0119；规格由 toolchain/gen_std_dummy_model_clips.py 生成）。
//
// 产出（落在 Assets/Resources/GameFoundation/ 下，与 UnityResourceLoader 的两条约定路径逐字对应）：
//   models/std_dummy_biped.prefab —— 标准人形骨骼占位体：根（Animator 所在，原点在脚底）→ hips → spine → head/双臂；
//     hips → 双腿；主手/副手挂点 "socket.main_hand" / "socket.off_hand"；方块可视件挂在对应骨骼下（头块名 "slot.head"，
//     槽位换网格的约定名）。骨骼层级、休息位置、块体尺寸全部来自规格，不在本脚本里写数。
//   models/std_dummy_biped.controller —— 每份剪辑资产一个状态（状态名 = 剪辑资产名 = clipId 末段，见
//     UnityRenderer3D.PlayAnim），默认状态 std_dummy_idle；每个状态预置 AnimStateFinishRelay（非循环完成事件）。
//   anim_clips/std_dummy_<键点号换下划线>.anim —— 每个非别名剪辑键一份；旋转曲线 localRotation.xyzw（线性切线，
//     关键帧与 sprite 版帧边界对齐）、hips 位置曲线 localPosition.xyz；内嵌 04 §5 命名事件（functionName=OnAnimEvent，
//     stringParameter=裸事件名；``hit`` 旁带 ``hit_frame`` 别名，见规格 anim_events）。别名键（attack -> attack.unarmed）
//     只在数据行里复用同一份剪辑资源，不单独出资产。
//
// 判断记录（为什么是新预制体 std_dummy_biped 而不改 placeholder_biped）：placeholder_biped 是胶囊体，没有骨骼层级，
// 既有占位用例（ModelIntegration 等）绑定着它的结构；骨骼剪辑需要真实的骨骼路径才能被 Animator 绑定，所以另出一个
// 与之并列的资产，两者互不影响。
//
// 判断记录（可重复运行）：剪辑与预制体"原地覆盖"（已有同路径资产时 CopySerialized/SaveAsPrefabAsset），guid 不变，
// 重复运行不会让大头的 .meta 抖动；控制器删除后重建（状态是子资产，原地增量容易残留孤儿状态），其 .meta 的 guid 会变，
// 同一次运行里重写的预制体随之指向新 guid，二者一并提交即可。
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Adapter.Unity.EditorTools
{
    public static class GenerateStdDummyModelAssets
    {
        private const string SpecPath = "../../../assets/_placeholder/std_dummy_model_clips.json";
        private const string ModelsDir = "Assets/Resources/GameFoundation/models";
        private const string AnimClipsDir = "Assets/Resources/GameFoundation/anim_clips";
        private const string ModelName = "std_dummy_biped";
        private const string PrefabPath = ModelsDir + "/" + ModelName + ".prefab";
        private const string ControllerPath = ModelsDir + "/" + ModelName + ".controller";
        private const string DefaultStateName = "std_dummy_idle";

        // ---- 规格的 JSON 形状（JsonUtility 只认字段名，未声明的字段忽略）----
        [Serializable] private class Spec { public float frame_rate; public SkeletonSpec skeleton = new SkeletonSpec(); public ClipSpec[] clips = Array.Empty<ClipSpec>(); }
        [Serializable] private class SkeletonSpec { public string root = ""; public BoneSpec[] bones = Array.Empty<BoneSpec>(); public VisualSpec[] visuals = Array.Empty<VisualSpec>(); }
        [Serializable] private class BoneSpec { public string name = ""; public string path = ""; public string parent = ""; public float[] pos = Array.Empty<float>(); }
        [Serializable] private class VisualSpec { public string bone = ""; public string name = ""; public float[] center = Array.Empty<float>(); public float[] size = Array.Empty<float>(); }
        [Serializable] private class EventSpec { public string name = ""; public float time_pct; }
        [Serializable] private class TrackSpec { public string path = ""; public float[] rot = Array.Empty<float>(); public float[] pos = Array.Empty<float>(); }
        [Serializable]
        private class ClipSpec
        {
            public string key = "";
            public string state = "";
            public string alias_of = "";
            public bool loop;
            public float total_ms;
            public float[] times_ms = Array.Empty<float>();
            public EventSpec[] anim_events = Array.Empty<EventSpec>();
            public TrackSpec[] tracks = Array.Empty<TrackSpec>();
        }

        [MenuItem("GameFoundation/Generate Std Dummy Model Assets")]
        public static void Generate()
        {
            var specFile = Path.GetFullPath(Path.Combine(Application.dataPath, SpecPath));
            if (!File.Exists(specFile))
            {
                throw new FileNotFoundException("缺规格文件（先运行 toolchain/gen_std_dummy_model_clips.py）", specFile);
            }

            var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(specFile));
            Directory.CreateDirectory(ModelsDir);
            Directory.CreateDirectory(AnimClipsDir);

            var clips = new List<AnimationClip>();
            var states = new List<(string, AnimationClip)>();
            foreach (var c in spec.clips)
            {
                if (!string.IsNullOrEmpty(c.state) && string.IsNullOrEmpty(c.alias_of))
                {
                    var clip = CreateOrUpdateClip(c, spec.frame_rate);
                    clips.Add(clip);
                    states.Add((c.state, clip));
                }
            }

            var controller = CreateOrReplaceController(states);
            CreateOrReplacePrefab(spec.skeleton, controller);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[GenerateStdDummyModelAssets] 生成完成：{PrefabPath} / {ControllerPath} / {clips.Count} 份剪辑（{AnimClipsDir}）");
        }

        /// <summary>供 -executeMethod 批处理调用：生成后退出编辑器进程。</summary>
        public static void GenerateAndExit()
        {
            Generate();
            EditorApplication.Exit(0);
        }

        // --------------------------------------------------------------

        private static AnimationClip CreateOrUpdateClip(ClipSpec c, float frameRate)
        {
            var clip = new AnimationClip { name = c.state, frameRate = frameRate };
            var times = new float[c.times_ms.Length];
            for (var i = 0; i < times.Length; i++)
            {
                times[i] = c.times_ms[i] / 1000f;
            }

            foreach (var t in c.tracks)
            {
                if (t.rot != null && t.rot.Length > 0)
                {
                    var comps = new[] { "x", "y", "z", "w" };
                    for (var k = 0; k < 4; k++)
                    {
                        clip.SetCurve(t.path, typeof(Transform), "localRotation." + comps[k], LinearCurve(times, t.rot, 4, k));
                    }
                }

                if (t.pos != null && t.pos.Length > 0)
                {
                    var comps = new[] { "x", "y", "z" };
                    for (var k = 0; k < 3; k++)
                    {
                        clip.SetCurve(t.path, typeof(Transform), "localPosition." + comps[k], LinearCurve(times, t.pos, 3, k));
                    }
                }
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = c.loop;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var events = new List<AnimationEvent>();
            var length = c.total_ms / 1000f;
            foreach (var e in c.anim_events)
            {
                events.Add(new AnimationEvent
                {
                    time = e.time_pct * length,
                    functionName = Adapter.Unity.EngineAdapter.UnityRenderer3D.AnimEventFunctionName,
                    stringParameter = e.name,
                });
            }
            AnimationUtility.SetAnimationEvents(clip, events.ToArray());

            var path = AnimClipsDir + "/" + c.state + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                existing.ClearCurves();
                EditorUtility.CopySerialized(clip, existing);
                UnityEngine.Object.DestroyImmediate(clip);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        /// <summary>逐段线性的曲线：每个关键帧的入/出切线取相邻段斜率，插值即直线（与 sprite 版"帧间不插值"的逐帧姿势一一对应，
        /// 20 帧/秒的关键帧之间由引擎线性插值）。</summary>
        private static AnimationCurve LinearCurve(float[] times, float[] values, int stride, int component)
        {
            var n = times.Length;
            var keys = new Keyframe[n];
            for (var i = 0; i < n; i++)
            {
                var v = values[i * stride + component];
                var inSlope = i == 0 ? 0f : (v - values[(i - 1) * stride + component]) / (times[i] - times[i - 1]);
                var outSlope = i == n - 1 ? 0f : (values[(i + 1) * stride + component] - v) / (times[i + 1] - times[i]);
                if (i == 0)
                {
                    inSlope = outSlope;
                }
                if (i == n - 1)
                {
                    outSlope = inSlope;
                }
                keys[i] = new Keyframe(times[i], v, inSlope, outSlope);
            }
            return new AnimationCurve(keys);
        }

        private static AnimatorController CreateOrReplaceController(List<(string state, AnimationClip clip)> states)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            AnimatorState? defaultState = null;
            foreach (var (name, clip) in states)
            {
                var s = machine.AddState(name);
                s.motion = clip;
                s.AddStateMachineBehaviour<Adapter.Unity.EngineAdapter.AnimStateFinishRelay>();
                if (name == DefaultStateName)
                {
                    defaultState = s;
                }
            }
            if (defaultState != null)
            {
                machine.defaultState = defaultState;
            }
            return controller;
        }

        private static void CreateOrReplacePrefab(SkeletonSpec skeleton, AnimatorController controller)
        {
            var root = new GameObject(skeleton.root);
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            var byName = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var b in skeleton.bones)
            {
                var go = new GameObject(b.name);
                var parent = string.IsNullOrEmpty(b.parent) ? root.transform : byName[b.parent];
                go.transform.SetParent(parent, worldPositionStays: false);
                go.transform.localPosition = new Vector3(b.pos[0], b.pos[1], b.pos[2]);
                byName[b.name] = go.transform;
            }

            var bonesByPath = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var b in skeleton.bones)
            {
                bonesByPath[b.path] = byName[b.name];
            }

            foreach (var v in skeleton.visuals)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = v.name;
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.transform.SetParent(bonesByPath[v.bone], worldPositionStays: false);
                cube.transform.localPosition = new Vector3(v.center[0], v.center[1], v.center[2]);
                cube.transform.localScale = new Vector3(v.size[0], v.size[1], v.size[2]);
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
