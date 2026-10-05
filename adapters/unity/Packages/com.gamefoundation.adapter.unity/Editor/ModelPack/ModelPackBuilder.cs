#nullable enable
// ModelPackBuilder：把"第三方骨骼模型包"（FBX + 贴图 + 规格 JSON）装配成框架 3D 模型管线认得的资产（ADR-0158）。
//
// 框架的 3D 模型管线只认两条 Resources 约定路径（UnityResourceLoader）：
//   model.<x>  -> Resources/GameFoundation/models/<x>.prefab     （根下带 Animator 的预制体）
//   anim.<y>   -> Resources/GameFoundation/anim_clips/<y>.anim   （AnimatorController 状态名 = 剪辑资产名 = clipId 末段）
// 本构建器按规格（schema gf.model_pack.v1，见 assets/_showcase/_source/build_data_3d.py 生成的 showcase3d_models.json）：
//   1. 以 FBX 导入产物为源，为每个模型生成：材质（着色器取规格 shader，贴图取规格 materials）、预制体
//      （根 -> Model 子物体 = FBX 实例，挂 Animator；Model 上按规格写朝向偏移与归一化缩放）、控制器（每个剪辑一个状态，
//      预置 AnimStateFinishRelay）、独立 .anim 剪辑（取 FBX 内的剪辑，可反向播放、可改循环）。
//   2. 产物落在 Assets/Showcase3dArt/Editor/Resources/GameFoundation/ 下——"Editor/Resources"只在编辑器里被 Resources.Load 命中，
//      不进玩家构建；整棵 Assets/Showcase3dArt/ 是 build.ps1 同步出来的构建期产物（.gitignore），不入库。
//
// 判断记录（为什么不把生成的预制体/剪辑入库，与 GenerateStdDummyModelAssets 不同）：骨骼剪辑从 FBX 抽成独立 .anim 后每份数百 KB，
// 4 个角色约 50 份，入库会把仓库撑大一个数量级；FBX 本身才是源头，生成物逐机重新生成成本是秒级。所以入库的只有 FBX/贴图/规格/着色器，
// 本构建器在编辑器加载时按"规格 + FBX 内容"的哈希增量生成（哈希没变不重做）。
// 判断记录（Animator 必须挂在 FBX 实例根上）：FBX 里的剪辑曲线路径相对 FBX 根（"CharacterArmature/…"），Animator 所在物体必须就是
// 那个根，所以预制体根自己不放 Animator，朝向偏移/缩放写在 Animator 所在的 Model 子物体上；UnityRenderer3D 用
// GetComponentInChildren<Animator> 找到它，事件中继也挂在同一物体上。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Adapter.Unity.EngineAdapter;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Adapter.Unity.Editor.ModelPack
{
    public static class ModelPackBuilder
    {
        /// <summary>构建器自身版本：改动生成逻辑时递增，使既有产物被重新生成。</summary>
        private const string BuilderVersion = "1";

        public const string PackRoot = "Assets/Showcase3dArt";
        private const string SourceDir = PackRoot + "/Source";
        private const string OutRoot = PackRoot + "/Editor/Resources/GameFoundation";
        private const string ModelsDir = OutRoot + "/models";
        private const string ClipsDir = OutRoot + "/anim_clips";
        private const string MaterialsDir = PackRoot + "/Generated/Materials";
        private const string StampFile = PackRoot + "/.built_stamp";

        [Serializable] private class PackSpec { public string schema = ""; public string shader = ""; public ModelSpec[] models = Array.Empty<ModelSpec>(); }
        [Serializable] private class MaterialSpec { public string name = ""; public string texture = ""; }
        [Serializable] private class ClipSpec { public string id = ""; public string source = ""; public bool loop; public bool reverse; }
        [Serializable]
        private class ModelSpec
        {
            public string id = "";
            public string fbx = "";
            public float target_height;
            public float yaw_offset_degrees;
            public bool upright;
            public MaterialSpec[] materials = Array.Empty<MaterialSpec>();
            public string default_clip = "";
            public ClipSpec[] clips = Array.Empty<ClipSpec>();
        }

        [InitializeOnLoadMethod]
        private static void AutoBuildOnLoad()
        {
            // delayCall：等编辑器首轮导入/编译结束后再动资产库（InitializeOnLoad 期间直接建资产不稳）。
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                {
                    return;
                }
                try
                {
                    EnsureBuilt();
                }
                catch (Exception e)
                {
                    Debug.LogError("[ModelPackBuilder] 自动装配失败：" + e);
                }
            };
        }

        [MenuItem("GameFoundation/手感试玩/重新装配模型包资产")]
        public static void RebuildMenu() => EnsureBuilt(force: true);

        /// <summary>供 -executeMethod 批处理调用：装配后退出。</summary>
        public static void BuildAndExit()
        {
            EnsureBuilt(force: true);
            EditorApplication.Exit(0);
        }

        /// <summary>规格目录下没有模型包规格（没同步过）时什么都不做；有则哈希未变不重做。</summary>
        public static void EnsureBuilt(bool force = false)
        {
            var specPaths = FindSpecs();
            if (specPaths.Count == 0)
            {
                return;
            }

            var stamp = ComputeStamp(specPaths);
            var stampFull = Path.GetFullPath(StampFile);
            if (!force && File.Exists(stampFull) && File.ReadAllText(stampFull) == stamp && OutputsPresent(specPaths))
            {
                return;
            }

            AssetDatabase.StartAssetEditing();
            var built = 0;
            try
            {
                Directory.CreateDirectory(ModelsDir);
                Directory.CreateDirectory(ClipsDir);
                Directory.CreateDirectory(MaterialsDir);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.Refresh();

            foreach (var specPath in specPaths)
            {
                var spec = JsonUtility.FromJson<PackSpec>(File.ReadAllText(specPath, Encoding.UTF8));
                foreach (var model in spec.models)
                {
                    BuildModel(spec, model);
                    built++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            File.WriteAllText(stampFull, stamp);
            Debug.Log($"[ModelPackBuilder] 装配完成：{built} 个模型 -> {OutRoot}");
        }

        private static List<string> FindSpecs()
        {
            var result = new List<string>();
            var dir = Path.GetFullPath(SourceDir);
            if (!Directory.Exists(dir))
            {
                return result;
            }
            foreach (var path in Directory.GetFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                if (File.ReadAllText(path, Encoding.UTF8).Contains("\"gf.model_pack.v1\""))
                {
                    result.Add(path);
                }
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static string ComputeStamp(List<string> specPaths)
        {
            using var md5 = MD5.Create();
            var sb = new StringBuilder(BuilderVersion);
            foreach (var specPath in specPaths)
            {
                var bytes = File.ReadAllBytes(specPath);
                sb.Append('|').Append(BitConverter.ToString(md5.ComputeHash(bytes)));
                var spec = JsonUtility.FromJson<PackSpec>(Encoding.UTF8.GetString(bytes));
                foreach (var model in spec.models)
                {
                    var fbx = Path.Combine(Path.GetFullPath(SourceDir), model.fbx);
                    if (File.Exists(fbx))
                    {
                        sb.Append('|').Append(new FileInfo(fbx).Length);
                    }
                }
            }
            return sb.ToString();
        }

        private static bool OutputsPresent(List<string> specPaths)
        {
            foreach (var specPath in specPaths)
            {
                var spec = JsonUtility.FromJson<PackSpec>(File.ReadAllText(specPath, Encoding.UTF8));
                foreach (var model in spec.models)
                {
                    if (!File.Exists(Path.GetFullPath(ModelsDir + "/" + model.id + ".prefab")))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static void BuildModel(PackSpec pack, ModelSpec model)
        {
            var fbxPath = SourceDir + "/" + model.fbx;
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
            var fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (fbxRoot == null)
            {
                throw new FileNotFoundException("FBX 未能导入（先运行 build.ps1 同步 assets/_showcase/models）：" + fbxPath);
            }

            // 1) 材质：按规格逐贴图建一份，着色器取规格 shader（按文件名在 Source 里找）。
            var shader = LoadShader(pack.shader);
            var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var m in model.materials)
            {
                var texturePath = SourceDir + "/" + m.texture;
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null)
                {
                    throw new FileNotFoundException("贴图未能导入：" + texturePath);
                }
                var matPath = MaterialsDir + "/" + model.id + "_" + m.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (material == null)
                {
                    material = new Material(shader) { name = model.id + "_" + m.name };
                    AssetDatabase.CreateAsset(material, matPath);
                }
                material.shader = shader;
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", Color.white);
                EditorUtility.SetDirty(material);
                materials[m.name] = material;
            }

            // 2) 剪辑：FBX 内的剪辑 -> 独立 .anim。
            var fbxClips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToList();
            Debug.Log($"[ModelPackBuilder] {model.id}：FBX 剪辑 {fbxClips.Count} 个：{string.Join(", ", fbxClips.Select(c => c.name + "(" + c.length.ToString("0.00") + "s)"))}；" +
                      "渲染器：" + string.Join(", ", fbxRoot.GetComponentsInChildren<Renderer>(true).Select(r => r.name + "[" + string.Join("/", r.sharedMaterials.Select(m => m == null ? "null" : m.name)) + "]")));
            var outClips = new List<(string id, AnimationClip clip)>();
            foreach (var spec in model.clips)
            {
                var source = fbxClips.FirstOrDefault(c => c.name == spec.source)
                    ?? fbxClips.FirstOrDefault(c => c.name.EndsWith("|" + StripArmature(spec.source), StringComparison.Ordinal))
                    ?? fbxClips.FirstOrDefault(c => c.name == StripArmature(spec.source));
                if (source == null)
                {
                    throw new InvalidOperationException(
                        $"FBX {model.fbx} 里找不到剪辑 \"{spec.source}\"（现有：{string.Join(", ", fbxClips.Select(c => c.name))}）");
                }
                outClips.Add((spec.id, WriteClip(spec, source)));
            }
            AssetDatabase.SaveAssets();

            // 3) 控制器。
            var controllerPath = ModelsDir + "/" + model.id + ".controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            AnimatorState? defaultState = null;
            foreach (var (id, clip) in outClips)
            {
                var state = machine.AddState(id);
                state.motion = clip;
                state.writeDefaultValues = true;
                state.AddStateMachineBehaviour<AnimStateFinishRelay>();
                if (id == model.default_clip)
                {
                    defaultState = state;
                }
            }
            if (defaultState != null)
            {
                machine.defaultState = defaultState;
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            // 4) 预制体。
            var root = new GameObject(model.id);
            var visual = (GameObject)UnityEngine.Object.Instantiate(fbxRoot);
            visual.name = "Model";
            var visualParent = root.transform;
            if (model.upright)
            {
                // 站在地面上：枢轴由 ModelGroundUpright 每帧按锚点朝向摆正（见该组件判断记录）。
                var pivot = new GameObject("UprightPivot");
                pivot.transform.SetParent(root.transform, worldPositionStays: false);
                pivot.AddComponent<ModelGroundUpright>();
                visualParent = pivot.transform;
            }
            visual.transform.SetParent(visualParent, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0f, model.yaw_offset_degrees, 0f);
            visual.transform.localScale = Vector3.one;

            var animator = visual.GetComponent<Animator>();
            if (animator == null)
            {
                animator = visual.AddComponent<Animator>();
            }
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(old => PickMaterial(materials, old, renderer)).ToArray();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                }
            }

            // 缩放：以绑定姿势下的网格高度归一到规格 target_height（场景单位）。
            var height = MeasureHeight(visual);
            if (height > 1e-4f && model.target_height > 0f)
            {
                var scale = model.target_height / height;
                visual.transform.localScale = new Vector3(scale, scale, scale);
            }

            var prefabPath = ModelsDir + "/" + model.id + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static string StripArmature(string source)
        {
            var i = source.IndexOf('|');
            return i < 0 ? source : source.Substring(i + 1);
        }

        private static Shader LoadShader(string fileName)
        {
            var path = SourceDir + "/" + fileName;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                throw new FileNotFoundException("着色器未能导入：" + path);
            }
            return shader;
        }

        /// <summary>FBX 自带材质按名字对规格材质（本包的材质名与贴图名一致）；对不上直接报错，不静默落到别的材质。</summary>
        private static Material PickMaterial(Dictionary<string, Material> materials, Material? old, Renderer renderer)
        {
            if (old != null && materials.TryGetValue(old.name, out var exact))
            {
                return exact;
            }
            throw new InvalidOperationException(
                $"渲染器 {renderer.name} 的材质 \"{(old == null ? "null" : old.name)}\" 在规格 materials 里没有对应项（现有：{string.Join(", ", materials.Keys)}）");
        }

        private static float MeasureHeight(GameObject visual)
        {
            var any = false;
            var bounds = new Bounds();
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.IndexOf("Sword", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    renderer.name.IndexOf("Dagger", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    renderer.name.IndexOf("Staff", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue; // 武器不算身高。
                }
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return any ? bounds.size.y : 0f;
        }

        private static AnimationClip WriteClip(ClipSpec spec, AnimationClip source)
        {
            var clip = new AnimationClip { name = spec.id, frameRate = source.frameRate };
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var curve = AnimationUtility.GetEditorCurve(source, binding);
                if (spec.reverse)
                {
                    curve = Reverse(curve, source.length);
                }
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(source);
            settings.loopTime = spec.loop;
            settings.loopBlend = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var path = ClipsDir + "/" + spec.id + ".anim";
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

        private static AnimationCurve Reverse(AnimationCurve curve, float length)
        {
            var keys = curve.keys;
            var reversed = new Keyframe[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                var k = keys[keys.Length - 1 - i];
                reversed[i] = new Keyframe(length - k.time, k.value, -k.outTangent, -k.inTangent);
            }
            return new AnimationCurve(reversed);
        }
    }
}
