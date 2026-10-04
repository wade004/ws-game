using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.SimLoop;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 装备/卸装音效（手感设计/08 第 4 节 <c>item.template.equip_sfx_ref</c>，ADR-0153）生产装配级复现与不变量：
    /// 真实 <c>GameplayAssembly</c>（背包 + 装备宿主）+ 真实事件总线 + 真实 <c>PresentationAssembly</c>（装配根按目录声明装配
    /// <c>EquipSfxDirector</c> → <c>CompositeFeedbackSink</c> → <c>SfxPlayer</c>）+ 桩音频；期望值由数据行（声明的
    /// <c>equip_sfx_ref</c> → <c>sfx.def.resource_ref</c>）算出，不写裸数。
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        private static readonly Id EquipSfxItemId = new Id("item.sample_chime_sword");
        private static readonly Id EquipSfxId = new Id("sfx.sample_equip");
        private static readonly Id EquipSfxResourceId = new Id("sfx.sample_equip_clip");

        /// <summary>在共用最小数据集之上追加一件声明了 <c>equip_sfx_ref</c> 的主手武器与对应 <c>sfx.def</c> 行
        /// （共用集里的 <c>item.sample_sword</c> 不声明，作为"未声明"对照）。</summary>
        private static void AddEquipSfxTables(InMemoryDataSource source)
        {
            source.Add("item.template",
                "{\"table\": \"item.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + EquipSfxItemId.Value + "\", \"slot\": \"item.slot.sample_main_hand\", " +
                "\"quality\": \"item.quality.sample_common\", \"item_level\": 1, " +
                "\"weapon_profile\": {\"damage_min\": 50, \"damage_max\": 50, \"speed\": 1.5, " +
                "\"weapon_school\": \"school.physical\"}, " +
                "\"display_ref\": \"display.item.sample_chime_sword\", \"stack_size\": 1, " +
                "\"name_key\": \"l10n.item.sample_chime_sword.name\", " +
                "\"equip_sfx_ref\": \"" + EquipSfxId.Value + "\"}" +
                "]}");
            source.Add("sfx.def",
                "{\"table\": \"sfx.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + EquipSfxId.Value + "\", \"layer\": \"ui\", \"priority\": 1, " +
                "\"resource_ref\": \"" + EquipSfxResourceId.Value + "\"}" +
                "]}");
        }

        private static Id AddAndEquip(Core.Gameplay.Assembly.GameplayAssembly gameplay, Id templateId)
        {
            var playerId = gameplay.PlayerUnitProvider();
            gameplay.Carriers.Inventory.RegisterUnit(playerId);
            Assert.True(gameplay.Carriers.Inventory.AddItem(playerId, templateId, 1, new Id("item.quality.sample_common"), null));
            var instanceId = gameplay.Carriers.Inventory.ListItems(playerId).Single(i => i.TemplateId.Equals(templateId)).InstanceId;
            var result = gameplay.Carriers.Equipment.Equip(playerId, instanceId, OptionsMainHandSlot);
            Assert.True(result.Success, result.Reason.ToString());
            return instanceId;
        }

        private static int PlaybacksOf(Adapters.Stub.StubEngine engine, Id resource) =>
            engine.Audio.ActiveSfxPlaybacks.Values.Count(p => p.SoundId.Equals(resource));

        [Fact]
        public void EquipSfx_DeclaredOnTemplate_PlaysOnEquipAndOnUnequip_ThroughRealSfxPath()
        {
            var presentation = Build(out var gameplay, out var world, out var engine, out _, extraTables: AddEquipSfxTables);
            Assert.NotNull(presentation.EquipSfx);
            var playerId = gameplay.PlayerUnitProvider();
            var before = PlaybacksOf(engine, EquipSfxResourceId);

            AddAndEquip(gameplay, EquipSfxItemId);
            world.Tick(SimStep.Continuous(0.1));
            Assert.Equal(before + 1, PlaybacksOf(engine, EquipSfxResourceId));
            Assert.Equal(1, presentation.EquipSfx!.PlayedCount);

            Assert.NotNull(gameplay.Carriers.Equipment.Unequip(playerId, OptionsMainHandSlot));
            world.Tick(SimStep.Continuous(0.1));
            Assert.Equal(before + 2, PlaybacksOf(engine, EquipSfxResourceId));
            Assert.Equal(2, presentation.EquipSfx.PlayedCount);
        }

        [Fact]
        public void EquipSfx_TemplateWithoutDeclaration_PlaysNothing_EvenWhenAnotherTemplateDeclares()
        {
            var presentation = Build(out var gameplay, out var world, out var engine, out _, extraTables: AddEquipSfxTables);
            var playerId = gameplay.PlayerUnitProvider();

            AddAndEquip(gameplay, new Id("item.sample_sword"));
            world.Tick(SimStep.Continuous(0.1));
            Assert.NotNull(gameplay.Carriers.Equipment.Unequip(playerId, OptionsMainHandSlot));
            world.Tick(SimStep.Continuous(0.1));

            Assert.Equal(0, presentation.EquipSfx!.PlayedCount);
            Assert.Equal(0, PlaybacksOf(engine, EquipSfxResourceId));
        }

        [Fact]
        public void EquipSfx_NoTemplateDeclares_DirectorNotAssembled_AndEquipFlowProducesNoPlayback()
        {
            var presentation = Build(out var gameplay, out var world, out var engine, out _);
            var playerId = gameplay.PlayerUnitProvider();
            var before = engine.Audio.ActiveSfxPlaybacks.Count;

            AddAndEquip(gameplay, new Id("item.sample_sword"));
            world.Tick(SimStep.Continuous(0.1));
            Assert.NotNull(gameplay.Carriers.Equipment.Unequip(playerId, OptionsMainHandSlot));
            world.Tick(SimStep.Continuous(0.1));

            Assert.Null(presentation.EquipSfx);
            Assert.Equal(before, engine.Audio.ActiveSfxPlaybacks.Count);
        }
    }
}
