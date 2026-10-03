#nullable enable
// SkinPrerenderRunner：把"标准骨骼 + 蒙皮"预渲染成序列帧的引擎侧执行器（architecture/手感设计/04 第 6.2 节、ADR-0140）。
//
// 定位：离线工具链能力，不进运行期。由 toolchain/prerender_skin/ 的命令行驱动器写一份作业 JSON（要渲染的剪辑、每帧取样时刻、
// 方向偏航角、装备层、相机与光照参数），本类读作业、逐 剪辑 × 方向 × 层 渲染透明背景图像，写成裸 RGBA 文件
// （每个 剪辑×方向×层 一个文件，各帧按序拼接，自上而下、直通道，宽×高×4 字节）与一份结果 JSON；
// 图集打包、frames.json、数据行、自检都在驱动器（Python）里做。
//
// 入口：RunBatch（批处理，-executeMethod）与 Run(作业路径)（供引擎内测试直接调用，返回退出码）。
//
// 判断记录：
// 1. 动画来源是框架已有的标准 model 型剪辑（Resources 下 GameFoundation/anim_clips/std_dummy_*.anim），不另录动画。曲线按"骨骼名"
//    施加（取曲线路径末段当骨骼名，在蒙皮层级里按名字找 Transform），不依赖蒙皮的层级路径与假人预制体一致——蒙皮只需要骨骼名
//    符合标准骨骼命名。旋转曲线直接当局部四元数写入（逐分量取值后归一化），hips 位置曲线乘 position_scale 后写入局部位置。
//    取样时刻由驱动器按 sprite 版同一规则（循环剪辑取帧起点、非循环取帧中点）算好，本类只在给定时刻对曲线求值，不自己舍入。
// 2. 不用 AnimationClip.SampleAnimation：它要求曲线路径与实例层级逐段一致；按名施加才能让任意层级的蒙皮复用同一批标准剪辑。
// 3. 渲染：正交相机、透明清屏、RenderTexture（8 位 sRGB）→ ReadPixels。URP 下优先走 RenderPipeline.SubmitRenderRequest。
//    相机和光照对整套固定；整个蒙皮转向（绕世界 Y 轴取 -偏航角，使 front 面向镜头、side_r 面向画面右），相机不动，所以同一档位的所有层共用
//    同一个相机与同一帧姿势，层间逐像素对齐是构造出来的，不是事后配准。
// 4. unlit（默认）把蒙皮材质换成 URP Unlit 并只保留基础色与基础贴图（确定性、与光照无关）；lit 保留蒙皮自己的材质并放一盏平行光。
// 5. 蒙皮与相机放在隔离的引擎层（unity_layer）上，相机剔除遮罩只含该层，不受场景里其它对象影响；渲染完整个蒙皮实例与相机/光照全部销毁，
//    RenderSettings 环境光还原。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Adapter.Unity.SkinPrerender.Editor
{
    public static class SkinPrerenderRunner
    {
        // ---- 作业 JSON 形状（JsonUtility 只认字段名，未声明的字段忽略）----
        [Serializable] public class SlotSpec { public string name = ""; public float yaw_deg; }
        [Serializable] public class LayerSpec { public string name = ""; public string[] selectors = Array.Empty<string>(); }
        [Serializable] public class ClipJob { public string stem = ""; public float[] sample_ms = Array.Empty<float>(); }
        [Serializable]
        public class Job
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
            public float[] ambient_color = { 0.55f, 0.55f, 0.55f };
            public float position_scale = 1f;
            public int unity_layer = 30;
            public SlotSpec[] slots = Array.Empty<SlotSpec>();
            public LayerSpec[] layers = Array.Empty<LayerSpec>();
            public ClipJob[] clips = Array.Empty<ClipJob>();
        }

        [Serializable] public class RenderedFile { public string file = ""; public string stem = ""; public string slot = ""; public string layer = ""; public int frames; }
        [Serializable]
        public class Result
        {
            public string status = "error";   // ok | refused | error
            public string message = "";
            public string[] missing_bones = Array.Empty<string>();
            public string[] duplicate_bones = Array.Empty<string>();
            public string[] bad_selectors = Array.Empty<string>();
            public string[] overlapping_renderers = Array.Empty<string>();
            public string[] missing_clips = Array.Empty<string>();
            public string unity_version = "";
            public string graphics_device = "";
            public float rest_height_units;
            public float elapsed_s;
            public RenderedFile[] files = Array.Empty<RenderedFile>();
        }

        private sealed class Refusal : Exception
        {
            public readonly Result Partial;
            public Refusal(string message, Result partial) : base(message) { Partial = partial; }
        }

        private const string ArgJob = "-skinPrerenderJob";

        /// <summary>批处理入口：<c>-executeMethod Adapter.Unity.SkinPrerender.Editor.SkinPrerenderRunner.RunBatch -skinPrerenderJob 作业.json</c>。</summary>
        public static void RunBatch()
        {
            var args = Environment.GetCommandLineArgs();
            string? job = null;
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == ArgJob) job = args[i + 1];
            if (string.IsNullOrEmpty(job))
            {
                Debug.LogError("[SkinPrerender] 缺少参数 " + ArgJob + " <作业.json>");
                EditorApplication.Exit(2);
                return;
            }
            var code = Run(job!);
            EditorApplication.Exit(code);
        }

        /// <summary>读取作业、渲染、写结果文件。返回 0 = ok；3 = 拒绝（缺骨骼等，见结果文件）；1 = 其它错误。</summary>
        public static int Run(string jobPath)
        {
            var started = DateTime.UtcNow;
            Job job;
            var result = new Result();
            try
            {
                job = JsonUtility.FromJson<Job>(File.ReadAllText(jobPath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkinPrerender] 读取作业失败：" + ex);
                return 1;
            }
            result.unity_version = Application.unityVersion;
            result.graphics_device = SystemInfo.graphicsDeviceType.ToString();
            var code = 1;
            try
            {
                var files = Render(job, result);
                result.files = files.ToArray();
                result.status = "ok";
                code = 0;
            }
            catch (Refusal r)
            {
                var p = r.Partial;
                p.status = "refused";
                p.message = r.Message;
                p.unity_version = result.unity_version;
                p.graphics_device = result.graphics_device;
                result = p;
                code = 3;
                Debug.LogWarning("[SkinPrerender] 拒绝渲染：" + r.Message);
            }
            catch (Exception ex)
            {
                result.status = "error";
                result.message = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
                Debug.LogError("[SkinPrerender] 渲染失败：" + ex);
                code = 1;
            }
            result.elapsed_s = (float)(DateTime.UtcNow - started).TotalSeconds;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(job.result_path)) ?? ".");
                File.WriteAllText(job.result_path, JsonUtility.ToJson(result, true), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Debug.LogError("[SkinPrerender] 写结果文件失败：" + ex);
                return 1;
            }
            return code;
        }

        // ------------------------------------------------------------------ 渲染

        private sealed class BoneCurves
        {
            public Transform Bone = null!;
            public AnimationCurve?[] Rot = new AnimationCurve?[4];
            public AnimationCurve?[] Pos = new AnimationCurve?[3];
        }

        private static List<RenderedFile> Render(Job job, Result partial)
        {
            if (job.slots.Length == 0) throw new Exception("作业没有方向档");
            if (job.clips.Length == 0) throw new Exception("作业没有剪辑");
            Directory.CreateDirectory(job.out_dir);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(job.skin);
            if (prefab == null)
                throw new Refusal("找不到蒙皮资产（应为引擎工程内的预制体或模型文件）：" + job.skin, partial);

            var savedAmbientMode = RenderSettings.ambientMode;
            var savedAmbient = RenderSettings.ambientLight;
            GameObject? instance = null, camGo = null, lightGo = null;
            RenderTexture? rt = null;
            Texture2D? tex = null;
            var tempMaterials = new List<Material>();
            var outFiles = new List<RenderedFile>();
            var prevActive = RenderTexture.active;
            try
            {
                instance = UnityEngine.Object.Instantiate(prefab);
                instance.name = "SkinPrerender_Skin";
                instance.transform.position = Vector3.zero;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                // 1) 骨骼校验：缺的、重名的一并列出再拒绝
                var bones = new Dictionary<string, List<Transform>>();
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                {
                    if (!bones.TryGetValue(t.name, out var l)) bones[t.name] = l = new List<Transform>();
                    l.Add(t);
                }
                var missing = new List<string>();
                var duplicate = new List<string>();
                foreach (var b in job.required_bones)
                {
                    if (!bones.TryGetValue(b, out var l)) missing.Add(b);
                    else if (l.Count > 1) duplicate.Add(b);
                }
                if (missing.Count > 0 || duplicate.Count > 0)
                {
                    partial.missing_bones = missing.ToArray();
                    partial.duplicate_bones = duplicate.ToArray();
                    throw new Refusal("蒙皮 " + job.skin + " 不符合标准骨骼命名：缺骨骼 [" + string.Join(", ", missing) +
                                      "]，骨名重复 [" + string.Join(", ", duplicate) + "]", partial);
                }

                // 2) 装备层选择器 -> 渲染器分组
                var allRenderers = new List<Renderer>(instance.GetComponentsInChildren<Renderer>(true));
                var layerOf = new Dictionary<Renderer, string>();
                var badSelectors = new List<string>();
                var overlaps = new List<string>();
                foreach (var layer in job.layers)
                {
                    foreach (var sel in layer.selectors)
                    {
                        var node = instance.transform.Find(sel);
                        if (node == null) { badSelectors.Add(layer.name + ":" + sel); continue; }
                        var rs = node.GetComponentsInChildren<Renderer>(true);
                        if (rs.Length == 0) { badSelectors.Add(layer.name + ":" + sel + "（其下没有渲染器）"); continue; }
                        foreach (var r in rs)
                        {
                            if (layerOf.TryGetValue(r, out var other) && other != layer.name)
                                overlaps.Add(GetPath(instance.transform, r.transform) + " 同时属于 " + other + " 与 " + layer.name);
                            else layerOf[r] = layer.name;
                        }
                    }
                }
                if (badSelectors.Count > 0 || overlaps.Count > 0)
                {
                    partial.bad_selectors = badSelectors.ToArray();
                    partial.overlapping_renderers = overlaps.ToArray();
                    throw new Refusal("装备层选择器无效：找不到 [" + string.Join("; ", badSelectors) + "]；重叠 [" + string.Join("; ", overlaps) + "]", partial);
                }

                // 3) 剪辑资产（Resources 路径，别名已由驱动器折叠）
                var curvesByStem = new Dictionary<string, List<BoneCurves>>();
                var missingClips = new List<string>();
                foreach (var cj in job.clips)
                {
                    var clip = Resources.Load<AnimationClip>(job.clips_resources_dir.TrimEnd('/') + "/" + cj.stem);
                    if (clip == null) { missingClips.Add(cj.stem); continue; }
                    curvesByStem[cj.stem] = ReadCurves(clip, bones);
                }
                if (missingClips.Count > 0)
                {
                    partial.missing_clips = missingClips.ToArray();
                    throw new Refusal("缺少标准剪辑资产 " + missingClips.Count + " 份（Resources/" + job.clips_resources_dir + "/<名>.anim）：" +
                                      string.Join(", ", missingClips.GetRange(0, Math.Min(8, missingClips.Count))), partial);
                }

                // 4) 隔离层、材质、相机、光照
                foreach (var t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = job.unity_layer;
                var unlit = job.lighting != "lit";
                if (unlit) SwapToUnlit(allRenderers, tempMaterials);

                camGo = new GameObject("SkinPrerender_Camera");
                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = job.canvas_h / (2f * job.pixels_per_unit);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 60f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << job.unity_layer;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.useOcclusionCulling = false;
                // 相机朝 -Z 看（模型 +Z 为前方，偏航 0 即面向镜头），俯角绕相机右轴下压。
                var camRot = Quaternion.Euler(job.camera_pitch_deg, 180f, 0f);
                var camUp = camRot * Vector3.up;
                var camRight = camRot * Vector3.right;
                var center = camUp * ((job.pivot_y - job.canvas_h / 2f) / job.pixels_per_unit)
                           + camRight * ((job.canvas_w / 2f - job.pivot_x) / job.pixels_per_unit);
                cam.transform.rotation = camRot;
                cam.transform.position = center - camRot * Vector3.forward * 20f;

                if (!unlit)
                {
                    lightGo = new GameObject("SkinPrerender_Light");
                    var light = lightGo.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.shadows = LightShadows.None;
                    light.intensity = 1f;
                    light.color = ToColor(job.light_color);
                    light.cullingMask = 1 << job.unity_layer;
                    lightGo.transform.rotation = Quaternion.Euler(job.light_euler_deg[0], job.light_euler_deg[1], job.light_euler_deg[2]);
                }
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = ToColor(job.ambient_color);

                rt = new RenderTexture(new RenderTextureDescriptor(job.canvas_w, job.canvas_h, RenderTextureFormat.ARGB32, 24)
                { sRGB = true, msaaSamples = 1, useMipMap = false, autoGenerateMips = false });
                rt.filterMode = FilterMode.Point;
                rt.Create();
                tex = new Texture2D(job.canvas_w, job.canvas_h, TextureFormat.RGBA32, false, false);

                partial.rest_height_units = MeasureHeight(allRenderers);

                // 5) 层变体：all（整身合成）、有装备层时再加 body 与各装备层
                var variants = new List<string> { "all" };
                if (job.layers.Length > 0)
                {
                    variants.Add("body");
                    foreach (var l in job.layers) variants.Add(l.name);
                }
                var frameBytes = job.canvas_w * job.canvas_h * 4;
                var buffer = new byte[frameBytes];

                foreach (var cj in job.clips)
                {
                    var curves = curvesByStem[cj.stem];
                    foreach (var slot in job.slots)
                    {
                        instance.transform.rotation = Quaternion.Euler(0f, -slot.yaw_deg, 0f);
                        var streams = new Dictionary<string, FileStream>();
                        try
                        {
                            foreach (var v in variants)
                            {
                                var name = cj.stem + "__" + slot.name + "__" + v + ".rgba";
                                streams[v] = new FileStream(Path.Combine(job.out_dir, name), FileMode.Create, FileAccess.Write);
                                outFiles.Add(new RenderedFile { file = name, stem = cj.stem, slot = slot.name, layer = v, frames = cj.sample_ms.Length });
                            }
                            foreach (var ms in cj.sample_ms)
                            {
                                ApplyPose(curves, ms / 1000f, job.position_scale);
                                foreach (var v in variants)
                                {
                                    SetVisibility(allRenderers, layerOf, v);
                                    RenderFrame(cam, rt, tex, buffer, job.canvas_w, job.canvas_h);
                                    streams[v].Write(buffer, 0, buffer.Length);
                                }
                            }
                        }
                        finally
                        {
                            foreach (var s in streams.Values) s.Dispose();
                        }
                    }
                }
                return outFiles;
            }
            finally
            {
                RenderTexture.active = prevActive;
                RenderSettings.ambientMode = savedAmbientMode;
                RenderSettings.ambientLight = savedAmbient;
                if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); }
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (lightGo != null) UnityEngine.Object.DestroyImmediate(lightGo);
                foreach (var m in tempMaterials) UnityEngine.Object.DestroyImmediate(m);
            }
        }

        private static string GetPath(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var c = t; c != null && c != root; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static Color ToColor(float[] v) => new Color(v[0], v[1], v[2], 1f);

        private static List<BoneCurves> ReadCurves(AnimationClip clip, Dictionary<string, List<Transform>> bones)
        {
            var map = new Dictionary<string, BoneCurves>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Transform)) continue;
                var slash = binding.path.LastIndexOf('/');
                var boneName = slash >= 0 ? binding.path.Substring(slash + 1) : binding.path;
                if (!bones.TryGetValue(boneName, out var l)) continue;   // 蒙皮没有这根骨骼（已由必需骨骼校验兜住），忽略
                if (!map.TryGetValue(boneName, out var bc)) map[boneName] = bc = new BoneCurves { Bone = l[0] };
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                switch (binding.propertyName)
                {
                    case "m_LocalRotation.x": bc.Rot[0] = curve; break;
                    case "m_LocalRotation.y": bc.Rot[1] = curve; break;
                    case "m_LocalRotation.z": bc.Rot[2] = curve; break;
                    case "m_LocalRotation.w": bc.Rot[3] = curve; break;
                    case "m_LocalPosition.x": bc.Pos[0] = curve; break;
                    case "m_LocalPosition.y": bc.Pos[1] = curve; break;
                    case "m_LocalPosition.z": bc.Pos[2] = curve; break;
                }
            }
            var list = new List<BoneCurves>(map.Values);
            list.Sort((a, b) => string.CompareOrdinal(a.Bone.name, b.Bone.name));
            return list;
        }

        private static void ApplyPose(List<BoneCurves> curves, float t, float positionScale)
        {
            foreach (var bc in curves)
            {
                if (bc.Rot[0] != null && bc.Rot[1] != null && bc.Rot[2] != null && bc.Rot[3] != null)
                {
                    var q = new Quaternion(bc.Rot[0]!.Evaluate(t), bc.Rot[1]!.Evaluate(t), bc.Rot[2]!.Evaluate(t), bc.Rot[3]!.Evaluate(t));
                    var n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                    if (n > 1e-8f) bc.Bone.localRotation = new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n);
                }
                if (bc.Pos[0] != null && bc.Pos[1] != null && bc.Pos[2] != null)
                    bc.Bone.localPosition = new Vector3(bc.Pos[0]!.Evaluate(t), bc.Pos[1]!.Evaluate(t), bc.Pos[2]!.Evaluate(t)) * positionScale;
            }
        }

        private static void SetVisibility(List<Renderer> all, Dictionary<Renderer, string> layerOf, string variant)
        {
            foreach (var r in all)
            {
                bool on;
                if (variant == "all") on = true;
                else if (variant == "body") on = !layerOf.ContainsKey(r);
                else on = layerOf.TryGetValue(r, out var ln) && ln == variant;
                r.enabled = on;
            }
        }

        private static float MeasureHeight(List<Renderer> all)
        {
            var has = false;
            var b = new Bounds();
            foreach (var r in all)
            {
                if (!r.enabled) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            return has ? b.size.y : 0f;
        }

        private static void SwapToUnlit(List<Renderer> renderers, List<Material> temp)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) throw new Exception("找不到 Unlit 着色器，无法使用 unlit 光照模式（可改用 lighting=lit）");
            foreach (var r in renderers)
            {
                var src = r.sharedMaterials;
                var dst = new Material[src.Length];
                for (var i = 0; i < src.Length; i++)
                {
                    var s = src[i];
                    var m = new Material(shader) { name = (s != null ? s.name : "none") + "_unlit" };
                    var color = Color.white;
                    Texture? map = null;
                    var offset = Vector2.zero;
                    var scale = Vector2.one;
                    if (s != null)
                    {
                        if (s.HasProperty("_BaseColor")) color = s.GetColor("_BaseColor");
                        else if (s.HasProperty("_Color")) color = s.GetColor("_Color");
                        if (s.HasProperty("_BaseMap")) { map = s.GetTexture("_BaseMap"); offset = s.GetTextureOffset("_BaseMap"); scale = s.GetTextureScale("_BaseMap"); }
                        else if (s.HasProperty("_MainTex")) { map = s.GetTexture("_MainTex"); offset = s.GetTextureOffset("_MainTex"); scale = s.GetTextureScale("_MainTex"); }
                    }
                    color.a = 1f;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", color);
                    if (map != null)
                    {
                        if (m.HasProperty("_BaseMap")) { m.SetTexture("_BaseMap", map); m.SetTextureOffset("_BaseMap", offset); m.SetTextureScale("_BaseMap", scale); }
                        if (m.HasProperty("_MainTex")) { m.SetTexture("_MainTex", map); m.SetTextureOffset("_MainTex", offset); m.SetTextureScale("_MainTex", scale); }
                    }
                    temp.Add(m);
                    dst[i] = m;
                }
                r.sharedMaterials = dst;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        private static void RenderFrame(Camera cam, RenderTexture rt, Texture2D tex, byte[] buffer, int w, int h)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (GraphicsSettings.currentRenderPipeline != null && RenderPipeline.SupportsRenderRequest(cam, req))
            {
                RenderPipeline.SubmitRenderRequest(cam, req);
            }
            else
            {
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;
            }
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = null;
            var raw = tex.GetRawTextureData();
            var rowBytes = w * 4;
            for (var y = 0; y < h; y++)
            {
                // ReadPixels 的原点在左下；输出自上而下。
                Buffer.BlockCopy(raw, (h - 1 - y) * rowBytes, buffer, y * rowBytes, rowBytes);
            }
        }
    }
}
