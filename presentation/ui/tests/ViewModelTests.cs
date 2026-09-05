using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Gameplay.Dialog;
using Core.Gameplay.Quest;
using Presentation.Ui;
using Xunit;
using FoundationSaveSystem = Core.Foundation.SaveSystem;

namespace Tests.PresentationUi
{
    /// <summary>十个视图模型各至少一条 <c>Refresh</c> 断言（见任务书验收要求）。每条测试都验证
    /// "构造/手动 Refresh 之后，公开属性反映底层宿主当前状态"，覆盖铁律 P1（只读查询）与 P2
    /// （订阅刷新）在视图模型层面的落实。</summary>
    public class ViewModelTests
    {
        private static readonly Id Health = new Id("arch.power.health");
        private static readonly Id Strength = new Id("stat.strength");

        [Fact]
        public void HudViewModel_refreshes_level_and_power_bars_from_hosts()
        {
            var world = new UiWorldFixture();
            world.PowerHost.RegisterUnit(world.PlayerId, new[] { Health });
            world.PowerHost.SetForTest(world.PlayerId, Health, 55, 100);
            world.Progression.SetForTest(world.PlayerId, 3, 10, 200);

            using var vm = new HudViewModel(world.DataSource, world.PlayerId, new[] { Health });

            Assert.Equal(3, vm.Level);
            Assert.Equal(55, vm.PowerBars[Health].Current);
            Assert.Equal(100, vm.PowerBars[Health].Max);
            Assert.False(vm.HasTarget);

            world.CurrentTarget = world.TargetId;
            world.PowerHost.RegisterUnit(world.TargetId, new[] { Health });
            world.PowerHost.SetForTest(world.TargetId, Health, 20, 40);
            vm.Refresh();

            Assert.True(vm.HasTarget);
            Assert.Equal(20, vm.TargetPowerBars[Health].Current);
        }

        [Fact]
        public void ActionBarViewModel_reads_slot_bindings_and_cooldowns()
        {
            var world = new UiWorldFixture();
            var fireball = new Id("skill.fireball");
            world.SkillBook.SetCooldownForTest(world.PlayerId, fireball, 2.0);
            world.SkillBindings.Bind(world.PlayerId, "slot_0", fireball);

            using var vm = new ActionBarViewModel(world.DataSource, world.PlayerId, 2, world.SkillBindings);

            Assert.Equal(2, vm.SlotCount);
            Assert.Equal(fireball, vm.Slots[0].SkillId);
            Assert.Equal(2.0, vm.Slots[0].Cooldown);
            Assert.False(vm.Slots[0].Available);
            Assert.Null(vm.Slots[1].SkillId);
            Assert.False(vm.Slots[1].Available);
        }

        [Fact]
        public void InventoryViewModel_reads_slots_and_equipped_items()
        {
            var world = new UiWorldFixture();
            var templateId = new Id("item.iron_sword");
            var instanceId = world.Inventory.AddItemForTest(world.PlayerId, templateId, 1);
            var slot = new Id("equip.main_hand");
            world.Equipment.Equip(world.PlayerId, instanceId, slot);

            using var vm = new InventoryViewModel(world.DataSource, new[] { slot });

            Assert.Single(vm.Slots);
            Assert.Equal(templateId, vm.Slots[0].TemplateId);
            Assert.Equal(instanceId, vm.EquippedSlots[slot]);
        }

        [Fact]
        public void QuestLogViewModel_reads_log_from_quest_host()
        {
            var world = new UiWorldFixture();
            var questId = new Id("quest.find_the_missing_child");
            world.Quest.SeedQuestForTest(questId, QuestState.Active, new[] { 1 });

            using var vm = new QuestLogViewModel(world.DataSource, world.Quest, world.PlayerId);

            Assert.Contains(vm.Log, p => p.QuestId.Equals(questId) && p.State == QuestState.Active);
        }

        [Fact]
        public void DialogViewModel_reads_story_view()
        {
            var world = new UiWorldFixture();
            var dialog = new FakeDialogHost
            {
                StoryViewToReturn = new StoryView(
                    new Id("dialog.tree.intro"), new Id("dialog.node.start"), new Id("l10n.dialog.intro"), null,
                    new List<(int, Id)>())
            };

            using var vm = new DialogViewModel(world.DataSource, dialog, world.PlayerId);

            Assert.True(vm.IsOpen);
            Assert.Equal(new Id("dialog.tree.intro"), vm.Story!.TreeId);
        }

        [Fact]
        public void SkillBookViewModel_reads_known_skills_and_cooldowns()
        {
            var world = new UiWorldFixture();
            var fireball = new Id("skill.fireball");
            world.SkillBook.LearnForTest(world.PlayerId, fireball);
            world.SkillBook.SetCooldownForTest(world.PlayerId, fireball, 0);

            using var vm = new SkillBookViewModel(world.DataSource, world.SkillBook, world.PlayerId);

            var entry = Assert.Single(vm.Entries);
            Assert.Equal(fireball, entry.SkillId);
            Assert.True(entry.Ready);
        }

        [Fact]
        public void CharacterStatsViewModel_reads_stat_values_and_localized_names()
        {
            var world = new UiWorldFixture();
            world.StatHost.SetBase(world.PlayerId, Strength, 15);
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") });
            var nameKey = new Id("l10n.stat.strength.name");
            l10n.SetTextForTest(nameKey, "Strength");

            using var vm = new CharacterStatsViewModel(world.DataSource, l10n, world.PlayerId, new[] { (Strength, nameKey) });

            var entry = Assert.Single(vm.Entries);
            Assert.Equal(15, entry.Value);
            Assert.Equal("Strength", entry.DisplayName);
        }

        [Fact]
        public void SettingsViewModel_reads_locale_bindings_and_conflicts()
        {
            var world = new UiWorldFixture();
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us"), new Id("l10n.zh_cn") });
            var inputMap = new FakeInputMapHost();
            inputMap.SetBindingsForTest("input.action.move", "key:w");
            inputMap.SetBindingsForTest("input.action.jump", "key:w");

            var audioVolume = new FakeAudioLayerVolumeHost(new[] { "sfx" });
            audioVolume.SetVolume("sfx", 0.7);
            using var vm = new SettingsViewModel(
                world.DataSource, l10n, inputMap, new[] { "input.action.move", "input.action.jump" }, audioVolume);

            Assert.Equal(new Id("l10n.en_us"), vm.Locale);
            Assert.Equal(0.7, vm.LayerVolumes["sfx"]);
            var moveRow = vm.Bindings[0];
            Assert.Equal("input.action.move", moveRow.ActionName);
            Assert.Contains("input.action.jump", moveRow.Conflicts[0]);
        }

        [Fact]
        public void SaveSlotsViewModel_reads_slots_from_save_system()
        {
            var fileSystem = new StubFileSystem();
            var saveSystem = new FoundationSaveSystem.SaveSystem(
                fileSystem, new FoundationSaveSystem.SaveSystemOptions(new Id("game.demo")));
            saveSystem.Save(new FoundationSaveSystem.SaveRequest(new Id("save.slot_1"), "2026-09-05T00:00:00Z"));

            var world = new UiWorldFixture();
            using var vm = new SaveSlotsViewModel(world.DataSource, saveSystem);

            Assert.Contains(vm.Slots, s => s.SlotId.Equals(new Id("save.slot_1")));
        }

        [Fact]
        public void PauseMenuViewModel_reflects_app_state()
        {
            var world = new UiWorldFixture();
            var appState = new AppStateHost(world.EventBus);
            appState.RequestTransition(AppState.MainMenu);
            appState.RequestTransition(AppState.Loading);
            appState.RequestTransition(AppState.InWorld);

            var options = new[] { new PauseMenuOption(new Id("shell.action.resume"), new Id("l10n.pause.resume")) };
            using var vm = new PauseMenuViewModel(world.DataSource, appState, options);

            Assert.Equal(AppState.InWorld, vm.CurrentState);
            Assert.False(vm.IsPaused);
            Assert.Single(vm.Options);

            appState.RequestTransition(AppState.Pause);
            vm.Refresh();
            Assert.True(vm.IsPaused);
        }
    }
}
