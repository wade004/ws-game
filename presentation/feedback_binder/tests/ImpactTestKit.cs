using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Core.Foundation.Common;
using Core.Foundation.Feel;
using Core.Rules.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// 打击反馈包测试夹具：真实 <see cref="FeelResolver"/>（框架 <see cref="FeelFields.Default"/> + <c>data/_feel</c> 的
    /// <c>rpg_classic</c> 中性预设作基底），按单位挂角色/武器覆盖层，由测试用"呈现型字段写入"造出非缺省档案。
    /// 期望值在用例里由写入的字段值与标定算出，不写死裸数。
    /// </summary>
    internal sealed class ImpactTestKit : IFeelBodyProvider, IFeelEquipmentProvider
    {
        public static readonly Id Player = new Id("unit.kit_player");
        public static readonly Id Hero = new Id("unit.kit_hero");

        /// <summary>标定：参考身高 2、基础移速 4、参考镜头高度 10（同手感模块测试标定 A 的取值）。</summary>
        public static FeelCalibration Calibration() =>
            new FeelCalibration("feel.calibration.kit", "feel.preset.rpg_classic", 2.0, 4.0, 30.0, 10.0, 1.0, 32.0, 50.0);

        public const double StepSeconds = 1.0 / 60.0;

        private readonly Dictionary<Id, string> _characters = new Dictionary<Id, string>();
        private readonly Dictionary<Id, string> _weapons = new Dictionary<Id, string>();
        private readonly List<FeelRow> _rows = new List<FeelRow>();

        public ImpactTestKit()
        {
            _rows.Add(FeelRow.Preset("feel.preset.rpg_classic", null, NeutralPresetValues()));
        }

        public string? GetArchetypeRef(Id unitId) => null;

        public string? GetCharacterRef(Id unitId) => _characters.TryGetValue(unitId, out var r) ? r : null;

        public string? GetMainWeaponRef(Id unitId) => _weapons.TryGetValue(unitId, out var r) ? r : null;

        public string? GetOffhandWeaponRef(Id unitId) => null;

        public static FeelWrite Set(string field, double v) => new FeelWrite(field, FeelOp.Set, FeelValue.Of(v));

        public static FeelWrite Set(string field, string v) => new FeelWrite(field, FeelOp.Set, FeelValue.Of(v));

        /// <summary>给单位挂一把武器（武器为主的呈现字段：冲击增益、音效档、材质、反馈包引用……）。</summary>
        public ImpactTestKit WithWeapon(Id unit, string weaponId, params FeelWrite[] writes)
        {
            _rows.Add(FeelRow.Weapon(weaponId, writes));
            _weapons[unit] = weaponId;
            return this;
        }

        /// <summary>给单位挂角色覆盖层（角色为主的呈现字段：镜头组、同时发声上限……）。</summary>
        public ImpactTestKit WithCharacter(Id unit, string characterId, params FeelWrite[] writes)
        {
            _rows.Add(FeelRow.Overlay(FeelTables.Character, characterId, writes));
            _characters[unit] = characterId;
            return this;
        }

        public FeelResolver Build() =>
            new FeelResolver(
                new FeelProfileSet(FeelFields.Default, _rows), Calibration(), StepSeconds,
                new FeelProviders { Body = this, Equipment = this });

        /// <summary>读 <c>data/_feel/feel/feel.preset.json</c> 里 <c>rpg_classic</c> 的全字段取值作为基底（框架中性档案）。</summary>
        private static IEnumerable<FeelWrite> NeutralPresetValues()
        {
            var path = Path.Combine(RepoRoot(), "data", "_feel", "feel", "feel.preset.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
            {
                if (row.GetProperty("id").GetString() != "feel.preset.rpg_classic")
                {
                    continue;
                }

                foreach (var prop in row.GetProperty("values").EnumerateObject())
                {
                    switch (prop.Value.ValueKind)
                    {
                        case JsonValueKind.Number:
                            yield return Set(prop.Name, prop.Value.GetDouble());
                            break;
                        case JsonValueKind.True:
                        case JsonValueKind.False:
                            yield return new FeelWrite(prop.Name, FeelOp.Set, FeelValue.Of(prop.Value.GetBoolean()));
                            break;
                        default:
                            yield return Set(prop.Name, prop.Value.GetString()!);
                            break;
                    }
                }
            }
        }

        public static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFilePath = "")
        {
            // 本文件位于 <root>/presentation/feedback_binder/tests/ImpactTestKit.cs，向上 3 级即仓库根。
            var dir = new DirectoryInfo(Path.GetDirectoryName(sourceFilePath)!);
            for (var i = 0; i < 3; i++) dir = dir.Parent!;
            return dir.FullName;
        }

        // ------------------------------------------------------------------ 反馈包与 sfx 索引

        public static SfxDef FeelSfx(string id, SfxFeelLayer layer, int tier, string? material = null) =>
            new SfxDef(new Id(id), "combat", null, null, new Id("sfx." + id.Substring(id.IndexOf('.') + 1)), false, layer, tier, material);

        public static ImpactVariant Variant(
            string impactClass, ImpactOutcome outcome, ImpactCameraSpec? camera = null, ImpactIntensity? intensity = null,
            ImpactFlashSpec? flash = null, ImpactVfxSpec? vfx = null, Id? text = null, ImpactFreezeLayers? freeze = null,
            params ImpactSfxSpec[] sfx) =>
            new ImpactVariant(impactClass, outcome, flash, vfx, sfx, camera, text, null, freeze ?? ImpactFreezeLayers.Default, intensity);

        public static ImpactHit Hit(
            Id source, Id target, string impactClass = "medium", HitResult result = HitResult.Hit, double ratio = 0.0,
            bool crit = false, bool kill = false, double amount = 10.0, Vec2? contact = null, Vec2? direction = null,
            Id? attackInstance = null) =>
            new ImpactHit(
                source, target, null, attackInstance, result, impactClass, amount, ratio, crit, kill,
                contact, Vec2.Zero, direction ?? new Vec2(1, 0));
    }

    /// <summary>记录全部新接口调用的 sink（在 <see cref="RecordingFeedbackSink"/> 之上加手感新增的几个口）。</summary>
    internal sealed class ImpactRecordingSink : IFeedbackSink
    {
        public readonly List<(Id VfxId, FeedbackAttachSpec Attach, IReadOnlyDictionary<string, double>? Parameters)> Vfx =
            new List<(Id, FeedbackAttachSpec, IReadOnlyDictionary<string, double>?)>();
        public readonly List<(Id SfxId, Vec2? At)> Sfx = new List<(Id, Vec2?)>();
        public readonly List<(Id Entity, Id Profile)> Flashes = new List<(Id, Id)>();
        public readonly List<(Id Entity, Id Style, string Text)> Texts = new List<(Id, Id, string)>();
        public readonly List<ImpactCameraCue> Cues = new List<ImpactCameraCue>();
        public readonly List<Id> Shakes = new List<Id>();
        public readonly List<(IReadOnlyList<Id> Units, int Ticks, ImpactFreezeLayers Layers)> Freezes =
            new List<(IReadOnlyList<Id>, int, ImpactFreezeLayers)>();
        public readonly List<IReadOnlyList<Id>> Releases = new List<IReadOnlyList<Id>>();

        public void FloatingText(Id entityId, Id styleId, string text) => Texts.Add((entityId, styleId, text));

        public void PlayVfx(Id vfxId, FeedbackAttachSpec attach) => Vfx.Add((vfxId, attach, null));

        public void PlayVfx(Id vfxId, FeedbackAttachSpec attach, IReadOnlyDictionary<string, double>? parameters) =>
            Vfx.Add((vfxId, attach, parameters));

        public void PlaySfx(Id sfxId, Vec2? at) => Sfx.Add((sfxId, at));

        public void Freeze(double durationMs) { }

        public void ShakeCamera(Id profileId) => Shakes.Add(profileId);

        public void Flash(Id entityId, Id profileId) => Flashes.Add((entityId, profileId));

        public void ImpactCamera(ImpactCameraCue cue)
        {
            Cues.Add(cue);
            if (cue.ShakeProfileId.HasValue) ShakeCamera(cue.ShakeProfileId.Value);
        }

        public void FreezePresentation(IReadOnlyList<Id> unitIds, int ticks, ImpactFreezeLayers layers) =>
            Freezes.Add((unitIds, ticks, layers));

        public void ReleasePresentation(IReadOnlyList<Id> unitIds) => Releases.Add(unitIds);

        public bool HasPendingPlayback => false;

        public event Action? PendingPlaybackChanged
        {
            add { }
            remove { }
        }
    }
}
