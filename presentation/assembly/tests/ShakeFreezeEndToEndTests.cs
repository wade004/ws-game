using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Combat;
using Core.Rules.Common;
using Presentation.Assembly;
using Presentation.Camera;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// ADR-0121 第 4 条（D4）：震屏 <c>profile_id</c> 语义 = 当前相机档的 shake preset id；事件 →
    /// 规则 → <c>ShakeCamera</c> → <c>CameraHost</c> → <c>StubCamera.LastShake*</c> 与事件 →
    /// <c>Freeze</c> → <c>OnFreeze</c> 两条经真实 <see cref="PresentationAssembly"/> 的端到端用例；
    /// 当前档没有该 preset 时不抛、记一条诊断并跳过本次震屏，后续派发照常。期望值一律由测试内
    /// 构造的 preset / 数据常量算出，不写裸数。
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        private static CombatDamageDealtEvent NewDamageEvent() =>
            new CombatDamageDealtEvent(
                new Id("unit.smoke_player"), new Id("unit.smoke_target"), new Id("skill.school.physical"),
                17.0, isCrit: false, HitResult.Hit);

        private static string SingleActionBindingTable(string ruleId, string actionsJson) =>
            "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
            "{\"id\": \"" + ruleId + "\", \"event\": \"combat.damage_dealt\", \"actions\": [" + actionsJson + "]}" +
            "]}";

        private static CameraProfile ProfileWithShakePresets(string profileId, params ShakePreset[] presets) =>
            new CameraProfile(new Id(profileId), 45, 0, 5, 15, 10, 0.2, shakePresets: presets);

        [Fact]
        public void ShakeCameraRule_Event_To_StubCameraShake_EndToEnd_ADR0121_D4()
        {
            var preset = new ShakePreset(new Id("feedback.shake.d4_e2e"), amplitude: 0.35, duration: 0.2, frequency: 25);
            var presentation = Build(out _, out _, out var engine, out var bus, extraTables: source =>
                source.Add("feedback.binding", SingleActionBindingTable(
                    "feedback.d4_shake_e2e",
                    "{\"kind\": \"shake_camera\", \"params\": {\"profile_id\": \"" + preset.Id + "\"}}")));
            presentation.Camera.Configure(ProfileWithShakePresets("camera_profile.d4_e2e", preset));

            bus.PublishImmediate(NewDamageEvent());

            Assert.Equal(preset.Amplitude, engine.Camera.LastShakeIntensity);
            Assert.Equal(preset.Duration, engine.Camera.LastShakeDurationSeconds);
            Assert.Equal(preset.Frequency, engine.Camera.LastShakeFrequency);
        }

        [Fact]
        public void FreezeRule_Event_To_OnFreeze_EndToEnd_ADR0121_D4()
        {
            const double durationMs = 55;
            var freezes = new List<double>();
            var options = new PresentationAssemblyOptions { OnFreeze = ms => freezes.Add(ms) };
            var presentation = Build(out _, out _, out _, out var bus, options, extraTables: source =>
                source.Add("feedback.binding", SingleActionBindingTable(
                    "feedback.d4_freeze_e2e",
                    "{\"kind\": \"freeze\", \"params\": {\"duration_ms\": " + durationMs + "}}")));

            bus.PublishImmediate(NewDamageEvent());

            Assert.Equal(new[] { durationMs }, freezes);
        }

        [Fact]
        public void ShakeCameraRule_PresetMissingInCurrentProfile_RecordsOneDiagnostic_SkipsShake_LaterDispatchUnaffected_ADR0121_D4()
        {
            var existing = new ShakePreset(new Id("feedback.shake.d4_existing"), amplitude: 0.5, duration: 0.3, frequency: 40);
            var missingId = new Id("feedback.shake.d4_not_in_current_profile");
            var floatingTexts = new List<string>();
            var options = new PresentationAssemblyOptions
            {
                OnFloatingText = (_, __, text) => floatingTexts.Add(text),
            };
            // 同一事件上先挂一条引用缺失 preset 的震屏规则，再挂飘字：缺失不得影响同事件后续动作与后续事件。
            var presentation = Build(out _, out _, out var engine, out var bus, options, extraTables: source =>
            {
                source.Add("feedback.binding", SingleActionBindingTable(
                    "feedback.d4_shake_missing",
                    "{\"kind\": \"shake_camera\", \"params\": {\"profile_id\": \"" + missingId + "\"}}"));
            });
            presentation.Camera.Configure(ProfileWithShakePresets("camera_profile.d4_missing", existing));
            var sinkRecorder = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.FeedbackSinkDiagnostics);
            var binderRecorder = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.Feedback.Diagnostics);
            var sinkBefore = sinkRecorder.Warnings.Count;
            var binderBefore = binderRecorder.Warnings.Count;

            bus.PublishImmediate(NewDamageEvent());

            // 恰一条诊断，且点明缺失的 preset id；相机没被震；不是靠 FeedbackBinder 的 SafeDispatch 兜异常。
            Assert.Equal(sinkBefore + 1, sinkRecorder.Warnings.Count);
            Assert.Contains(missingId.ToString(), sinkRecorder.Warnings[sinkRecorder.Warnings.Count - 1]);
            Assert.Equal(0.0, engine.Camera.LastShakeIntensity);
            Assert.Equal(binderBefore, binderRecorder.Warnings.Count);
            // 同一事件上样例规则的飘字照常派发（后续派发不受影响）。
            Assert.Single(floatingTexts);

            // 后续派发照常：同一事件再来一次，飘字照常派发，且只再多一条诊断。
            bus.PublishImmediate(NewDamageEvent());
            Assert.Equal(sinkBefore + 2, sinkRecorder.Warnings.Count);
            Assert.Equal(2, floatingTexts.Count);
        }
    }
}
