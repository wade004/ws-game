#nullable enable
// GeneratePlaceholderModelAssets：一次性（可重复运行）生成 model 型外形的占位资产
// （W6-B，见 architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md、落地计划 W6-B 小节）。
//
// 产出（均落在 Assets/Resources/GameFoundation/ 下，与 UnityResourceLoader.ResolveModelResourcesPath/
// ResolveAnimClipResourcesPath 两条约定路径逐字对应，见该类型判断记录）：
//   models/placeholder_biped.prefab —— 胶囊体本体（Animator 直接挂在该 GameObject 上，见
//     UnityRenderer3D.CreateModelInstance 判断记录"AnimationEvent 中继必须与 Animator 同一
//     GameObject"）+ 两个子对象 "socket.main_hand"（空挂点，供 AttachToSocket 测试）/
//     "slot.head"（球体占位头部，带 MeshFilter/MeshRenderer，供 SetSlotMesh 测试）。
//   models/placeholder_biped.controller —— AnimatorController，三个状态 idle（loop）/attack/cast，
//     默认状态 idle。
//   anim_clips/idle.anim / attack.anim / cast.anim —— 与上述三个状态一一对应的 AnimationClip；
//     attack.anim 在 50% 时间点内嵌一个 AnimationEvent（functionName="OnAnimEvent"，
//     stringParameter="hit_frame"，见 UnityRenderer3D.AnimEventFunctionName/AnimEventDomainPrefix）
//     ——与 AnimClipResolver 的数据驱动事件注册（见该类型判断记录）双重覆盖同一份命中帧描述，
//     即便某个具体游戏后续替换掉这条数据驱动注册路径，占位内容本身仍然自带可用的命中帧事件。
//
// 判断记录（为什么胶囊体本体也是 Animator 所在的 GameObject，而不是另建一个空根节点）：
// UnityRenderer3D.CreateModelInstance 用 GetComponentInChildren<Animator>() 定位 Animator（不要求
// 是根节点自身），本脚本选择"胶囊体本身即根节点"是为了让预制体层级尽量简单（两个占位挂点子物体 +
// 一个本体，不需要额外一层"Root"空节点），与任务书"胶囊体+两个子对象"的字面描述一致。
//
// 判断记录（AnimationClip 内容：只做"确实会动"的最小占位，不追求美术效果）：本框架不产出正式
// 美术资源（同 assets/_placeholder 各占位资产的一贯定位），三条剪辑各自给 Transform.localPosition.y
// 或 localEulerAngles 加一条简单曲线，唯一目的是让"剪辑确实播放中"在编辑器/测试里可被观察到
// （Animator.GetCurrentAnimatorStateInfo 的 normalizedTime 会随时间推进），不追求任何具体动作观感。
//
// 判断记录（可重复运行）：全部资产按固定路径 CreateAsset/SaveAsPrefabAsset，重复运行本脚本会
// 直接覆盖已存在的同名资产（DeleteAsset 后重新创建，而不是尝试原地增量修改——AnimatorController
// 状态机的原地增量修改容易在重复运行后残留孤儿状态，删了重建更简单可靠、结果确定），可安全多次
// 执行（如占位内容规格调整后重新生成）。
using System.IO;
using Core.Foundation.Common;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Adapter.Unity.EditorTools
{
    public static class GeneratePlaceholderModelAssets
    {
        private const string ModelsDir = "Assets/Resources/GameFoundation/models";
        private const string AnimClipsDir = "Assets/Resources/GameFoundation/anim_clips";

        private const string PrefabPath = ModelsDir + "/placeholder_biped.prefab";
        private const string ControllerPath = ModelsDir + "/placeholder_biped.controller";
        private const string IdleClipPath = AnimClipsDir + "/idle.anim";
        private const string AttackClipPath = AnimClipsDir + "/attack.anim";
        private const string CastClipPath = AnimClipsDir + "/cast.anim";

        /// <summary>命中帧事件名（裸名，见 UnityRenderer3D.AnimEventDomainPrefix 判断记录，换算后
        /// 等于 Presentation.Render.ModelCharacterRig.HitFrameEventId 的 "anim_event." 域前缀 +
        /// 本字符串）。</summary>
        private const string HitFrameEventName = "hit_frame";

        [MenuItem("GameFoundation/Generate Placeholder Model Assets")]
        public static void Generate()
        {
            Directory.CreateDirectory(ModelsDir);
            Directory.CreateDirectory(AnimClipsDir);

            var idleClip = CreateOrReplaceClip(IdleClipPath, "idle", length: 1.0f, loop: true, hitFrameAtPct: null);
            var attackClip = CreateOrReplaceClip(AttackClipPath, "attack", length: 0.5f, loop: false, hitFrameAtPct: 0.5f);
            var castClip = CreateOrReplaceClip(CastClipPath, "cast", length: 0.6f, loop: false, hitFrameAtPct: null);

            var controller = CreateOrReplaceController(ControllerPath, idleClip, attackClip, castClip);

            CreateOrReplacePrefab(PrefabPath, controller);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[GeneratePlaceholderModelAssets] 生成完成：{PrefabPath} / {ControllerPath} / " +
                $"{IdleClipPath} / {AttackClipPath} / {CastClipPath}");
        }

        /// <summary>供 -executeMethod 批处理调用（同 Il2CppPlayerBuilder 一类入口惯例）：生成后立即
        /// 退出编辑器进程，退出码固定 0（<see cref="Generate"/> 内部任何异常会让 Unity 批处理以非零
        /// 退出码结束，不需要本方法额外捕获）。</summary>
        public static void GenerateAndExit()
        {
            Generate();
            EditorApplication.Exit(0);
        }

        private static AnimationClip CreateOrReplaceClip(string path, string clipName, float length, bool loop, float? hitFrameAtPct)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            var clip = new AnimationClip { name = clipName };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var curve = new AnimationCurve();
            curve.AddKey(0f, 0f);
            curve.AddKey(length, loop ? 0f : 0.15f);
            clip.SetCurve(string.Empty, typeof(Transform), "localPosition.y", curve);
            clip.frameRate = 30f;

            if (hitFrameAtPct.HasValue)
            {
                var evt = new AnimationEvent
                {
                    time = length * hitFrameAtPct.Value,
                    functionName = Adapter.Unity.EngineAdapter.UnityRenderer3D.AnimEventFunctionName,
                    stringParameter = HitFrameEventName,
                };
                AnimationUtility.SetAnimationEvents(clip, new[] { evt });
            }

            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        private static AnimatorController CreateOrReplaceController(
            string path, AnimationClip idleClip, AnimationClip attackClip, AnimationClip castClip)
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var stateMachine = controller.layers[0].stateMachine;

            var idleState = stateMachine.AddState("idle");
            idleState.motion = idleClip;
            stateMachine.defaultState = idleState;

            var attackState = stateMachine.AddState("attack");
            attackState.motion = attackClip;

            var castState = stateMachine.AddState("cast");
            castState.motion = castClip;

            return controller;
        }

        private static void CreateOrReplacePrefab(string path, AnimatorController controller)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "placeholder_biped";
            Object.DestroyImmediate(body.GetComponent<Collider>());

            var animator = body.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            var socket = new GameObject(new Id("socket.main_hand").Value);
            socket.transform.SetParent(body.transform, worldPositionStays: false);
            socket.transform.localPosition = new Vector3(0.5f, 0.6f, 0f);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = new Id("slot.head").Value;
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(body.transform, worldPositionStays: false);
            head.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

            PrefabUtility.SaveAsPrefabAsset(body, path);
            Object.DestroyImmediate(body);
        }
    }
}
