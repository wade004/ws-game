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
//     stringParameter=裸事件名；``hit`` 旁带 ``hit_frame`` 别名，见规格 anim_events，数据行已自带同名事件时不重复）。
//     别名键（attack -> attack.unarmed）只在数据行里复用同一份剪辑资源，不单独出资产。
//     体量组（规格 mass_groups，light/heavy：std_dummy_<体量>_<键>.anim）与主集的剪辑同目录、同控制器。
//
// 判断记录（为什么是新预制体 std_dummy_biped 而不改 placeholder_biped）：placeholder_biped 是胶囊体，没有骨骼层级，
// 既有占位用例（ModelIntegration 等）绑定着它的结构；骨骼剪辑需要真实的骨骼路径才能被 Animator 绑定，所以另出一个
// 与之并列的资产，两者互不影响。
//
// 判断记录（可重复运行、控制器逐字节确定）：剪辑与预制体"原地覆盖"（已有同路径资产时 CopySerialized/SaveAsPrefabAsset），
// guid 不变。控制器不再交给 Unity 的 AnimatorController API 创建（它给状态子资产分配随机 fileID，且删除重建会换 .meta 的
// guid，两次运行的控制器与引用它的预制体都会抖动），而是由本脚本直接写 YAML 文本：状态/行为子资产的 fileID 取状态名的
// FNV-1a 64 公式（与工具链 std_dummy_model_clips.config.controller_state_file_id 同式，自检逐状态核对）、状态顺序取规格顺序、
// 位置按序号递推；.meta 已存在则原样保留（guid 不变），不存在才按路径哈希写一份确定的 .meta。状态内的剪辑引用取剪辑 .meta 的
// guid，剪辑本身原地覆盖，所以同一份规格重复生成，控制器逐字节相同。
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
        [Serializable] private class Spec { public float frame_rate; public SkeletonSpec skeleton = new SkeletonSpec(); public ClipSpec[] clips = Array.Empty<ClipSpec>(); public MassGroupSpec[] mass_groups = Array.Empty<MassGroupSpec>(); }
        [Serializable] private class MassGroupSpec { public string id = ""; public ClipSpec[] clips = Array.Empty<ClipSpec>(); }
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
            var all = new List<ClipSpec>(spec.clips);
            foreach (var g in spec.mass_groups)
            {
                all.AddRange(g.clips);
            }
            foreach (var c in all)
            {
                if (!string.IsNullOrEmpty(c.state) && string.IsNullOrEmpty(c.alias_of))
                {
                    var clip = CreateOrUpdateClip(c, spec.frame_rate);
                    clips.Add(clip);
                    states.Add((c.state, clip));
                }
            }

            // 剪辑要先落盘并刷新，控制器 YAML 里才能写出它们的 guid。
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var controller = WriteDeterministicController(states);
            CreateOrReplacePrefab(spec.skeleton, controller, clips);

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

        // ---- 确定性控制器 ----
        private const long ControllerObjectId = 9100000;                 // 主资产（AnimatorController）的约定 fileID
        private const long StateMachineId = 9071758245397227233;        // 状态机子资产（固定值，沿用既有控制器里的 fileID）
        private const string RelayScriptGuid = "5d5ec73f9200180448ea1067113eba4a"; // AnimStateFinishRelay.cs.meta 的 guid

        /// <summary>与工具链 std_dummy_model_clips.config._fnv1a_signed64 同式：FNV-1a 64 位，按有符号解释。</summary>
        private static long Fnv1aSigned64(string text)
        {
            unchecked
            {
                ulong h = 0xCBF29CE484222325UL;
                foreach (var b in System.Text.Encoding.UTF8.GetBytes(text))
                {
                    h = (h ^ b) * 0x100000001B3UL;
                }
                return (long)h;
            }
        }

        private static long StateFileId(string state) => Fnv1aSigned64(ModelName + ".controller/state/" + state);

        private static long RelayFileId(string state) => Fnv1aSigned64(ModelName + ".controller/relay/" + state);

        private static AnimatorController WriteDeterministicController(List<(string state, AnimationClip clip)> states)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n");
            foreach (var (name, _) in states)
            {
                var relayId = RelayFileId(name);
                sb.Append("--- !u!114 &").Append(relayId.ToString(inv)).Append('\n');
                sb.Append("MonoBehaviour:\n  m_ObjectHideFlags: 1\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_GameObject: {fileID: 0}\n  m_Enabled: 1\n  m_EditorHideFlags: 0\n");
                sb.Append("  m_Script: {fileID: 11500000, guid: ").Append(RelayScriptGuid).Append(", type: 3}\n  m_Name: \n");
                sb.Append("  m_EditorClassIdentifier: Adapter.Unity::Adapter.Unity.EngineAdapter.AnimStateFinishRelay\n");
                var clipGuid = AssetDatabase.AssetPathToGUID(AnimClipsDir + "/" + name + ".anim");
                if (string.IsNullOrEmpty(clipGuid))
                {
                    throw new InvalidOperationException("剪辑资产没有 guid（未导入？）：" + name);
                }
                sb.Append("--- !u!1102 &").Append(StateFileId(name).ToString(inv)).Append('\n');
                sb.Append("AnimatorState:\n  serializedVersion: 6\n  m_ObjectHideFlags: 1\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n");
                sb.Append("  m_Name: ").Append(name).Append('\n');
                sb.Append("  m_Speed: 1\n  m_CycleOffset: 0\n  m_Transitions: []\n  m_StateMachineBehaviours:\n  - {fileID: ").Append(relayId.ToString(inv)).Append("}\n");
                sb.Append("  m_Position: {x: 50, y: 50, z: 0}\n  m_IKOnFeet: 0\n  m_WriteDefaultValues: 1\n  m_Mirror: 0\n  m_SpeedParameterActive: 0\n  m_MirrorParameterActive: 0\n  m_CycleOffsetParameterActive: 0\n  m_TimeParameterActive: 0\n");
                sb.Append("  m_Motion: {fileID: 7400000, guid: ").Append(clipGuid).Append(", type: 2}\n");
                sb.Append("  m_Tag: \n  m_SpeedParameter: \n  m_MirrorParameter: \n  m_CycleOffsetParameter: \n  m_TimeParameter: \n");
            }

            sb.Append("--- !u!1107 &").Append(StateMachineId.ToString(inv)).Append('\n');
            sb.Append("AnimatorStateMachine:\n  serializedVersion: 6\n  m_ObjectHideFlags: 1\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n  m_Name: Base Layer\n  m_ChildStates:\n");
            long defaultId = 0;
            for (var i = 0; i < states.Count; i++)
            {
                var id = StateFileId(states[i].state);
                if (states[i].state == DefaultStateName)
                {
                    defaultId = id;
                }
                sb.Append("  - serializedVersion: 1\n    m_State: {fileID: ").Append(id.ToString(inv)).Append("}\n");
                sb.Append("    m_Position: {x: ").Append((200 + 35 * i).ToString(inv)).Append(", y: ").Append((65 * i).ToString(inv)).Append(", z: 0}\n");
            }
            if (defaultId == 0 && states.Count > 0)
            {
                defaultId = StateFileId(states[0].state);
            }
            sb.Append("  m_ChildStateMachines: []\n  m_AnyStateTransitions: []\n  m_EntryTransitions: []\n  m_StateMachineTransitions: {}\n  m_StateMachineBehaviours: []\n");
            sb.Append("  m_AnyStatePosition: {x: 50, y: 20, z: 0}\n  m_EntryPosition: {x: 50, y: 120, z: 0}\n  m_ExitPosition: {x: 800, y: 120, z: 0}\n  m_ParentStateMachinePosition: {x: 800, y: 20, z: 0}\n");
            sb.Append("  m_DefaultState: {fileID: ").Append(defaultId.ToString(inv)).Append("}\n");

            sb.Append("--- !u!91 &").Append(ControllerObjectId.ToString(inv)).Append('\n');
            sb.Append("AnimatorController:\n  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n");
            sb.Append("  m_Name: ").Append(ModelName).Append("\n  serializedVersion: 5\n  m_AnimatorParameters: []\n  m_AnimatorLayers:\n  - serializedVersion: 5\n    m_Name: Base Layer\n");
            sb.Append("    m_StateMachine: {fileID: ").Append(StateMachineId.ToString(inv)).Append("}\n");
            sb.Append("    m_Mask: {fileID: 0}\n    m_Motions: []\n    m_Behaviours: []\n    m_BlendingMode: 0\n    m_SyncedLayerIndex: -1\n    m_DefaultWeight: 0\n    m_IKPass: 0\n    m_SyncedLayerAffectsTiming: 0\n");
            sb.Append("    m_Controller: {fileID: ").Append(ControllerObjectId.ToString(inv)).Append("}\n");

            var fullPath = Path.GetFullPath(ControllerPath);
            File.WriteAllText(fullPath, sb.ToString(), new System.Text.UTF8Encoding(false));
            var metaPath = fullPath + ".meta";
            if (!File.Exists(metaPath))
            {
                // 首次生成：guid 取路径的 MD5（确定值）；已有 .meta 一律保留原样。
                using var md5 = System.Security.Cryptography.MD5.Create();
                var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(ControllerPath));
                var guid = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                File.WriteAllText(metaPath,
                    "fileFormatVersion: 2\nguid: " + guid + "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 9100000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
                    new System.Text.UTF8Encoding(false));
            }
            AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                throw new InvalidOperationException("确定性控制器写出后未能作为 AnimatorController 导入：" + ControllerPath);
            }
            return controller;
        }

        private static GameObject BuildRoot(SkeletonSpec skeleton, AnimatorController controller)
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

            return root;
        }

        private static void CreateOrReplacePrefab(SkeletonSpec skeleton, AnimatorController controller, List<AnimationClip> clips)
        {
            // 剪辑的"通用绑定"（m_ClipBindingConstant.genericBindings）由 Unity 在"带 Animator 的预制体引用控制器并落盘"时烘进剪辑资产，
            // 既有已入库的剪辑都带着它。预制体本身下面可能因结构等价而不重写，所以这里先存一份临时预制体触发烘焙、把剪辑标脏落盘，
            // 再删掉临时预制体——无论预制体是否重写，剪辑资产的字节都一致，既有剪辑不抖动。
            const string scratchPrefab = ModelsDir + "/_bake_scratch.prefab";
            var scratchRoot = BuildRoot(skeleton, controller);
            PrefabUtility.SaveAsPrefabAsset(scratchRoot, scratchPrefab);
            UnityEngine.Object.DestroyImmediate(scratchRoot);
            foreach (var clip in clips)
            {
                EditorUtility.SetDirty(clip);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.DeleteAsset(scratchPrefab);

            var root = BuildRoot(skeleton, controller);
            // 预制体里 GameObject/组件的 fileID 由 Unity 在创建时随机分配，每次重建都会让整份 YAML 抖动；
            // 已有预制体与本次规格构建出的结构等价（层级、局部位置/旋转/缩放、Animator 的控制器与根运动设置）时原样保留。
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && Equivalent(existing, root, controller))
            {
                UnityEngine.Object.DestroyImmediate(root);
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static bool Equivalent(GameObject existing, GameObject fresh, AnimatorController controller)
        {
            var animator = existing.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController != controller || animator.applyRootMotion)
            {
                return false;
            }
            return NodeEquivalent(existing.transform, fresh.transform);
        }

        private static bool NodeEquivalent(Transform a, Transform b)
        {
            if (a.name != b.name || a.childCount != b.childCount
                || (a.localPosition - b.localPosition).sqrMagnitude > 1e-10f
                || (a.localScale - b.localScale).sqrMagnitude > 1e-10f
                || Quaternion.Angle(a.localRotation, b.localRotation) > 1e-4f)
            {
                return false;
            }
            var ca = a.GetComponents<Component>();
            var cb = b.GetComponents<Component>();
            if (ca.Length != cb.Length)
            {
                return false;
            }
            for (var i = 0; i < ca.Length; i++)
            {
                if (ca[i] == null || cb[i] == null || ca[i].GetType() != cb[i].GetType())
                {
                    return false;
                }
            }
            for (var i = 0; i < a.childCount; i++)
            {
                if (!NodeEquivalent(a.GetChild(i), b.GetChild(i)))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
