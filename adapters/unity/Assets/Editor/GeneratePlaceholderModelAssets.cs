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
//   models/placeholder_biped.controller —— AnimatorController，四个状态 idle（loop）/attack/cast/
//     hit（H5b 根治新增，见下），默认状态 idle。
//   anim_clips/idle.anim / attack.anim / cast.anim / hit.anim —— 与上述四个状态一一对应的
//     AnimationClip；attack.anim 在 50% 时间点内嵌一个 AnimationEvent（functionName="OnAnimEvent"，
//     stringParameter="hit_frame"，见 UnityRenderer3D.AnimEventFunctionName/AnimEventDomainPrefix）
//     ——与 AnimClipResolver 的数据驱动事件注册（见该类型判断记录）双重覆盖同一份命中帧描述，
//     即便某个具体游戏后续替换掉这条数据驱动注册路径，占位内容本身仍然自带可用的命中帧事件。
//   hit.anim（H5b 根治新增，游戏侧复核发现"PlayMode 用例缺'受击后继续攻击'覆盖"）：0.3 秒非循环
//     剪辑，不内嵌任何 AnimationEvent（受击本身不需要命中帧）——此前占位内容只有
//     idle/attack/cast 三个状态，data/_sample/display/display.anim_set.json 的
//     display.anim_set.placeholder_biped 因此从未声明 hit 剪辑，model 型实体受击后 Animator 不会
//     播放任何东西，也就永远不会触发本文件 UnityRenderer3D.Tick 侦测的"非循环剪辑自然播放完成"，
//     AnimStateMachine 会永久卡在 Hit（下一次攻击优先级不足以覆盖 Hit，见该类型判断记录"优先级
//     表"）——这是"完成回调"修复要能在生产数据集下端到端验证"受击后继续攻击"必须一并补上的资产
//     缺口，与 09/02 勘误"引擎适配层必须在非循环剪辑结束时发出完成事件"是同一件事的资产落地半。
//   test_autoexit.anim / "test_autoexit" 状态（PR140-04B 根治新增，见
//     architecture/落地计划/audit-c86bfa9-20260908/AUDIT_REPORT.md PR140-03）：0.2 秒非循环剪辑 +
//     一条 hasExitTime=true、duration=0（瞬时切换）的 test_autoexit -> idle 自动过渡——本状态与其余
//     四个状态（idle/attack/cast/hit）完全隔离，只供 UnityRenderer3D.Tick 的"Animator 自动过渡时完成
//     事件是否漏发"回归测试直接调用 PlayAnim(handle, "anim.test_autoexit", ...)（同 H5b 既有
//     PlayHitClip_NonLoop_ReachesFinished_RaisesFinishedEvent 一贯的"绕过游戏逻辑状态机、直接对
//     UnityRenderer3D 播放"手法），不经任何 display.anim_set 登记，也不影响 attack/cast/hit 三个既有
//     状态——刻意不在共享的 attack/hit 状态上加自动过渡，避免影响 AnimReplayAndFinishEndToEndTests
//     等既有用例对"未播完前仍停留在该状态"的既有断言。
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
        private const string HitClipPath = AnimClipsDir + "/hit.anim";
        private const string TestAutoExitClipPath = AnimClipsDir + "/test_autoexit.anim";

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
            var hitClip = CreateOrReplaceClip(HitClipPath, "hit", length: 0.3f, loop: false, hitFrameAtPct: null);
            var testAutoExitClip = CreateOrReplaceClip(TestAutoExitClipPath, "test_autoexit", length: 0.2f, loop: false, hitFrameAtPct: null);

            var controller = CreateOrReplaceController(ControllerPath, idleClip, attackClip, castClip, hitClip, testAutoExitClip);

            CreateOrReplacePrefab(PrefabPath, controller);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                $"[GeneratePlaceholderModelAssets] 生成完成：{PrefabPath} / {ControllerPath} / " +
                $"{IdleClipPath} / {AttackClipPath} / {CastClipPath} / {HitClipPath} / {TestAutoExitClipPath}");
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
            string path, AnimationClip idleClip, AnimationClip attackClip, AnimationClip castClip, AnimationClip hitClip,
            AnimationClip testAutoExitClip)
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

            // H5b 根治新增：见文件顶部判断记录——补齐 hit 状态，使 display.anim_set.placeholder_biped
            // 能声明一条真正会被 Animator 播放、进而能被 UnityRenderer3D.Tick 侦测到自然播放完成的
            // 受击剪辑。
            var hitState = stateMachine.AddState("hit");
            hitState.motion = hitClip;

            // PR140-04B 根治新增：见文件顶部判断记录——test_autoexit 状态自带一条 hasExitTime=true、
            // duration=0（瞬时切换）的自动过渡直接回 idle，专供 PR140-03 回归测试复现"Animator 自动
            // 过渡在检测帧之前已经发生"这一窗口；与其余四个状态相互独立，不影响既有用例。
            var testAutoExitState = stateMachine.AddState("test_autoexit");
            testAutoExitState.motion = testAutoExitClip;
            var autoExitTransition = testAutoExitState.AddTransition(idleState);
            autoExitTransition.hasExitTime = true;
            autoExitTransition.exitTime = 1.0f;
            autoExitTransition.hasFixedDuration = true;
            autoExitTransition.duration = 0f;

            // PR150-02 根治新增（architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md
            // PR150-02"动画在首次检测前已经自动退出时，仍漏发 finished"）：五个状态逐一预置
            // AnimStateFinishRelay（见该类型判断记录），使 UnityRenderer3D.Tick 的完成检测不再单纯
            // 依赖外部轮询采样——占位内容因此成为"具体游戏内容应如何接线本机制"的参照样例，同
            // AnimEventFunctionName/hit_frame 事件那条既有惯例。
            idleState.AddStateMachineBehaviour<Adapter.Unity.EngineAdapter.AnimStateFinishRelay>();
            attackState.AddStateMachineBehaviour<Adapter.Unity.EngineAdapter.AnimStateFinishRelay>();
            castState.AddStateMachineBehaviour<Adapter.Unity.EngineAdapter.AnimStateFinishRelay>();
            hitState.AddStateMachineBehaviour<Adapter.Unity.EngineAdapter.AnimStateFinishRelay>();
            testAutoExitState.AddStateMachineBehaviour<Adapter.Unity.EngineAdapter.AnimStateFinishRelay>();

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
