// T-M15（ADR-0125）配套：非"公共构造器"形态的空参守卫——公共方法、静态工厂、受保护构造器（抽象基类）、
// internal 类型构造器。公共构造器的守卫由 NullGuardTests.cs 的反射用例批量覆盖；这里逐处手写，
// 与 `grep ArgumentNullException presentation/` 的清单一一对应。
using System;
using System.Collections.Generic;
using System.Reflection;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Presentation.Assembly;
using Presentation.Camera;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using Presentation.Shell;
using Presentation.Ui;
using Tests.PresentationRender;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using Xunit;

namespace Tests.PresentationCommon
{
    public class MethodNullGuardTests
    {
        private static T Make<T>() where T : class => (T)NullGuardSynthesizer.Synthesize(typeof(T))!;

        private static void AssertNullGuard(string expectedParamName, Action call)
        {
            var ex = Assert.Throws<ArgumentNullException>(call);
            Assert.Equal(expectedParamName, ex.ParamName);
        }

        // ---- assembly ----

        [Fact]
        public void ContentValidationAssembly_Run_NullSources_Throws() =>
            AssertNullGuard("sources", () => ContentValidationAssembly.Run(null!));

        [Fact]
        public void ContentValidationAssembly_CreateRegistry_NullArguments_Throw()
        {
            AssertNullGuard("primary", () => ContentValidationAssembly.CreateRegistry(null!, new ContentValidationOptions(), out _));
            AssertNullGuard("options", () => ContentValidationAssembly.CreateRegistry(Make<IDataSource>(), null!, out _));
        }

        [Fact]
        public void SchemaAudit_Run_NullArguments_Throw()
        {
            var allowlist = SchemaAuditAllowlist.Parse("{\"entries\":[]}");
            var schemas = new List<TableSchema>();
            var declarations = new List<ReferenceDeclaration>();

            AssertNullGuard("schemas", () => SchemaAudit.Run(null!, allowlist, declarations));
            AssertNullGuard("allowlist", () => SchemaAudit.Run(schemas, null!, declarations));
            AssertNullGuard("referenceDeclarations", () => SchemaAudit.Run(schemas, allowlist, null!));
        }

        [Fact]
        public void SchemaAuditAllowlist_Parse_NullJson_Throws() =>
            AssertNullGuard("json", () => SchemaAuditAllowlist.Parse(null!));

        [Fact]
        public void SchemaFieldExports_Collect_NullSchema_Throw()
        {
            AssertNullGuard("schema", () => SchemaFieldDeprecationExport.Collect(null!));
            AssertNullGuard("schema", () => SchemaFieldItemCountExport.Collect(null!));
            AssertNullGuard("schema", () => SchemaFieldRangeExport.Collect(null!));
        }

        // ---- camera ----

        [Fact]
        public void CameraHost_RegisterProfile_NullProfile_Throws() =>
            AssertNullGuard("profile", () => Make<CameraHost>().RegisterProfile(null!));

        // ---- feedback_binder ----

        [Fact]
        public void TextSource_Field_NullFieldName_Throws() =>
            AssertNullGuard("fieldName", () => TextSource.Field(null!));

        [Fact]
        public void CharacterRigHitFrameSource_RegisterRig_NullRig_Throws() =>
            AssertNullGuard("rig", () => Make<CharacterRigHitFrameSource>().RegisterRig(new Id("unit.a"), null!));

        [Fact]
        public void HitFrameSyncPolicy_WaitForHitFrame_NullReleaseOrToken_Throws()
        {
            var policy = Make<HitFrameSyncPolicy>();
            var attacker = new Id("unit.a");

            AssertNullGuard("release", () => policy.WaitForHitFrame(attacker, new object(), null!));
            AssertNullGuard("batchToken", () => policy.WaitForHitFrame(attacker, null!, () => { }));
            // 无 token 重载转调带 token 重载：release 为 null 同样被拒。
            AssertNullGuard("release", () => policy.WaitForHitFrame(attacker, null!));
        }

        [Fact]
        public void PlaybackQueue_Enqueue_NullStep_Throws() =>
            AssertNullGuard("step", () => Make<PlaybackQueue>().Enqueue(null!));

        // ---- render ----

        [Fact]
        public void FrameAnimPlayer_CallbackRegistrations_NullCallback_Throw()
        {
            var player = Make<FrameAnimPlayer>();

            AssertNullGuard("callback", () => player.OnComplete(null!));
            AssertNullGuard("callback", () => player.OnAnimEvent(null!));
            AssertNullGuard("callback", () => player.OnFrameChanged(null!));
        }

        [Fact]
        public void ModelCharacterRig_ApplyEquipVisual_NullDef_Throws() =>
            AssertNullGuard("def", () => Make<ModelCharacterRig>().ApplyEquipVisual(null!));

        [Fact]
        public void RenderConventionHost_NullArguments_Throw()
        {
            var host = Make<RenderConventionHost>();

            AssertNullGuard("spriteInfo", () => host.ResolveDirectionSlot(default, null!));
            AssertNullGuard("layerNamesInOrder", () => host.ComposeSpriteLayers(null!, Make<SpriteInfo>(), default));
        }

        [Fact]
        public void SpriteCharacterRig_NullArguments_Throw()
        {
            var rig = Make<SpriteCharacterRig>();

            AssertNullGuard("player", () => rig.AttachFrameAnimPlayer(null!));
            AssertNullGuard("layerNamesInOrder", () => rig.ComposeAndApplyLayers(null!, default, _ => new Id("res.a")));
            AssertNullGuard("resolveResourceId", () => rig.ComposeAndApplyLayers(new List<string>(), default, null!));
        }

        /// <summary><c>SpriteViewBase</c> 是抽象基类、构造器受保护：经测试里现成的派生类 <c>TestSpriteView</c> 触达。</summary>
        [Fact]
        public void SpriteViewBase_ProtectedConstructor_NullArguments_Throw()
        {
            var renderer = Make<IRenderer2D>();
            var conventions = Make<IRenderConventionHost>();
            var displayInfo = Make<DisplayInfo>();

            AssertNullGuard("renderer", () => new TestSpriteView(null!, conventions, displayInfo));
            AssertNullGuard("conventions", () => new TestSpriteView(renderer, null!, displayInfo));
            AssertNullGuard("displayInfo", () => new TestSpriteView(renderer, conventions, null!));
        }

        // ---- shell / ui / vfx_sfx ----

        [Fact]
        public void ShellHost_SaveSettings_NullAdditionalFields_Throws() =>
            AssertNullGuard("additionalFields", () => Make<ShellHost>().SaveSettings(null!));

        [Fact]
        public void FromRecord_NullRecord_Throws_ForShellMenuAndUiLayout()
        {
            AssertNullGuard("record", () => ShellMenuDefinition.FromRecord(null!));
            AssertNullGuard("record", () => UiLayoutDefinition.FromRecord(null!));
        }

        [Fact]
        public void PresentationDiagnosticsRecorder_Warn_NullMessage_Throws() =>
            AssertNullGuard("message", () => new PresentationDiagnosticsRecorder().Warn(null!));

        [Fact]
        public void SfxPlayer_SetLayerVolume_NullLayer_Throws() =>
            AssertNullGuard("layer", () => Make<SfxPlayer>().SetLayerVolume(null!, 0.5));

        /// <summary><c>VfxPool</c> 是 internal 类型（只经 <c>VfxPlayer</c> 内部使用）：反射触达其公共构造器。</summary>
        [Fact]
        public void VfxPool_InternalConstructor_NullArguments_Throw()
        {
            var poolType = typeof(VfxPlayer).Assembly.GetType("Presentation.VfxSfx.Core.VfxPool", throwOnError: true)!;
            var ctor = poolType.GetConstructor(new[] { typeof(VfxOptions), typeof(Action<ParticleHandle>) })!;
            Action<ParticleHandle> stop = _ => { };

            void AssertThrows(string expected, object?[] args)
            {
                var wrapped = Assert.Throws<TargetInvocationException>(() => ctor.Invoke(args));
                var ex = Assert.IsType<ArgumentNullException>(wrapped.InnerException);
                Assert.Equal(expected, ex.ParamName);
            }

            AssertThrows("options", new object?[] { null, stop });
            AssertThrows("stop", new object?[] { new VfxOptions(), null });
        }
    }
}
