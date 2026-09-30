using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Rules.Combat;
using Core.Rules.Common;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using Presentation.Shell;
using Presentation.Ui;
using Presentation.ViewBinding;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 生产装配接线探针（测试覆盖梳理 T-M41）：<see cref="PresentationAssemblyOptions"/> 的每一个可注入选项，
    /// 经真实 <see cref="PresentationAssembly"/> 注入后，能在运行时观测到该选项生效的结果——不止"构造不抛"。
    /// 已有用例覆盖的选项（<c>TargetResolver</c>/<c>ActionBarSlotCountFallback</c>/<c>OnFloatingText</c>/
    /// <c>OnFreeze</c>/<c>OnFlash</c>/<c>LoadedMapIdResolver</c>/<c>CameraHostOptions</c>/<c>SfxOptions</c>/
    /// <c>FeedbackOptions.MergeWindow</c>/<c>FeedbackQueueMode</c>）不在此重复，对照表见任务汇报。期望值一律由规则
    /// （宿主查询结果、数据行、选项字段值）算出，不写裸数，不依赖墙钟。
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        private static readonly Id OptionsMainHandSlot = new Id("item.slot.sample_main_hand");

        // -----------------------------------------------------------------
        // HudPowerTypes
        // -----------------------------------------------------------------

        [Fact]
        public void Options_HudPowerTypes_Default_ShowsHealthBar_MatchingPowerHost()
        {
            var presentation = Build(out var gameplay, out _, out _, out _);
            var playerId = presentation.Hud.PlayerId;
            var powers = gameplay.Carriers.Rules.Powers;

            var bar = Assert.Contains(WellKnownPowers.Health, presentation.Hud.PowerBars);

            Assert.Equal(powers.GetPower(playerId, WellKnownPowers.Health), bar.Current);
            Assert.Equal(powers.GetPowerMax(playerId, WellKnownPowers.Health), bar.Max);
        }

        [Fact]
        public void Options_HudPowerTypes_EmptyList_ShowsNoPowerBars()
        {
            var options = new PresentationAssemblyOptions { HudPowerTypes = Array.Empty<Id>() };

            var presentation = Build(out _, out _, out _, out _, options);

            Assert.Empty(presentation.Hud.PowerBars);
        }

        // -----------------------------------------------------------------
        // CharacterStatConfig
        // -----------------------------------------------------------------

        [Fact]
        public void Options_CharacterStatConfig_Injected_ProducesEntriesFromStatHostAndL10n_DefaultIsEmpty()
        {
            var statId = new Id("stat.max_health");
            var nameKey = new Id("l10n.stat.sample_max_health.name");
            var options = new PresentationAssemblyOptions
            {
                CharacterStatConfig = new[] { (statId, nameKey) },
            };

            var presentation = Build(out var gameplay, out _, out _, out _, options);
            var entry = Assert.Single(presentation.CharacterStats.Entries);

            Assert.Equal(statId, entry.StatId);
            Assert.Equal(gameplay.Carriers.Rules.Stats.GetStat(presentation.CharacterStats.PlayerId, statId), entry.Value);
            Assert.Equal(presentation.L10n.Text(nameKey), entry.DisplayName);

            var defaults = Build(out _, out _, out _, out _);
            Assert.Empty(defaults.CharacterStats.Entries);
        }

        // -----------------------------------------------------------------
        // EquipmentSlotIds
        // -----------------------------------------------------------------

        private static Id EquipSampleSwordOnPlayer(Core.Gameplay.Assembly.GameplayAssembly gameplay)
        {
            var playerId = gameplay.PlayerUnitProvider();
            gameplay.Carriers.Inventory.RegisterUnit(playerId);
            Assert.True(gameplay.Carriers.Inventory.AddItem(
                playerId, new Id("item.sample_sword"), 1, new Id("item.quality.sample_common"), null));
            var instanceId = gameplay.Carriers.Inventory.ListItems(playerId)[0].InstanceId;
            var result = gameplay.Carriers.Equipment.Equip(playerId, instanceId, OptionsMainHandSlot);
            Assert.True(result.Success, result.Reason.ToString());
            return instanceId;
        }

        [Fact]
        public void Options_EquipmentSlotIds_Injected_InventoryReportsEquippedItemInThatSlot_DefaultReportsNone()
        {
            var options = new PresentationAssemblyOptions { EquipmentSlotIds = new[] { OptionsMainHandSlot } };
            var presentation = Build(out var gameplay, out _, out _, out _, options);

            var instanceId = EquipSampleSwordOnPlayer(gameplay);
            presentation.Inventory.Refresh();

            Assert.Equal(instanceId, presentation.Inventory.EquippedSlots[OptionsMainHandSlot]);
            Assert.Equal(new Id("item.sample_sword"), presentation.Inventory.EquippedSlotIdentities[OptionsMainHandSlot].TemplateId);

            var defaults = Build(out var defaultsGameplay, out _, out _, out _);
            EquipSampleSwordOnPlayer(defaultsGameplay);
            defaults.Inventory.Refresh();
            Assert.Empty(defaults.Inventory.EquippedSlots);
        }

        // -----------------------------------------------------------------
        // PauseMenuOptions
        // -----------------------------------------------------------------

        [Fact]
        public void Options_PauseMenuOptions_Injected_ExposedOnPauseMenuInOrder_DefaultIsEmpty()
        {
            var injected = new[]
            {
                new PauseMenuOption(new Id("pause_menu.resume"), new Id("l10n.pause.resume")),
                new PauseMenuOption(new Id("pause_menu.quit"), new Id("l10n.pause.quit")),
            };
            var options = new PresentationAssemblyOptions { PauseMenuOptions = injected };

            var presentation = Build(out _, out _, out _, out _, options);

            Assert.Equal(injected.Length, presentation.PauseMenu.Options.Count);
            for (var i = 0; i < injected.Length; i++)
            {
                Assert.Equal(injected[i].OptionId, presentation.PauseMenu.Options[i].OptionId);
                Assert.Equal(injected[i].TextKey, presentation.PauseMenu.Options[i].TextKey);
            }

            Assert.Empty(Build(out _, out _, out _, out _).PauseMenu.Options);
        }

        // -----------------------------------------------------------------
        // SettingsActionNames
        // -----------------------------------------------------------------

        private static ActionDefinition OptionsAction(string name, string binding) =>
            new ActionDefinition(new Id(name), ActionKind.Button, new[] { binding });

        /// <summary>默认（<c>SettingsActionNames = null</c>）= 当前已声明的全部动作：构造期还没有任何动作时无行，
        /// 构造后才声明的动作在 <c>Refresh()</c> 后按声明顺序全部出现（缺陷修复前默认恒为空清单）。</summary>
        [Fact]
        public void Options_SettingsActionNames_Default_ListsAllDeclaredActions_IncludingThoseDeclaredAfterConstruction()
        {
            var confirm = OptionsAction("input.action.options_wiring_confirm", "key:enter");
            var cancel = OptionsAction("input.action.options_wiring_cancel", "key:escape");

            var presentation = Build(out _, out _, out _, out _);
            Assert.Empty(presentation.Settings.Bindings);

            presentation.InputMap.DeclareActionSet(new Id("input.set.options_wiring_a"), new[] { confirm });
            presentation.InputMap.DeclareActionSet(new Id("input.set.options_wiring_b"), new[] { cancel });
            presentation.Settings.Refresh();

            Assert.Equal(
                new[] { confirm.ActionId.Value, cancel.ActionId.Value },
                presentation.Settings.Bindings.Select(r => r.ActionName).ToArray());
            foreach (var row in presentation.Settings.Bindings)
            {
                Assert.Equal(presentation.InputMap.GetBindings(row.ActionName), row.Bindings);
            }
        }

        /// <summary>
        /// 缺陷修复回归（测试覆盖第二批缺陷 1）：<see cref="PresentationAssemblyOptions.SettingsActionNames"/> 注入非空清单，
        /// 此前在 <see cref="PresentationAssembly"/> 构造期就抛 <see cref="InvalidOperationException"/>（"未声明的动作"）——
        /// <c>SettingsViewModel</c> 构造期立即 <c>Refresh()</c> 调 <c>IInputMapHost.GetBindings</c>，而 <c>InputMapHost</c>
        /// 由装配根自己在构造期新建，此时尚无任何动作被声明。修复后：清单里未声明的动作跳过，声明后刷新即出现。
        /// </summary>
        [Fact]
        public void Options_SettingsActionNames_Injected_SettingsRowsReflectInputMapBindings()
        {
            var actionId = new Id("input.action.options_wiring_confirm");
            var options = new PresentationAssemblyOptions { SettingsActionNames = new[] { actionId.Value } };

            var presentation = Build(out _, out _, out _, out _, options);
            presentation.InputMap.DeclareActionSet(
                new Id("input.set.options_wiring"),
                new[] { new ActionDefinition(actionId, ActionKind.Button, new[] { "key:enter" }) });
            presentation.Settings.Refresh();

            var row = Assert.Single(presentation.Settings.Bindings);
            Assert.Equal(actionId.Value, row.ActionName);
            Assert.Equal(presentation.InputMap.GetBindings(actionId.Value), row.Bindings);
        }

        /// <summary>注入清单：构造后才声明的动作在刷新后出现（构造期不抛、无行）；清单外的已声明动作不进面板；
        /// 行顺序按清单顺序，不按声明顺序。</summary>
        [Fact]
        public void Options_SettingsActionNames_Injected_UndeclaredSkipped_LateDeclaredAppearOnRefresh_ListOrderWins()
        {
            var first = OptionsAction("input.action.options_wiring_first", "key:a");
            var second = OptionsAction("input.action.options_wiring_second", "key:b");
            var notListed = OptionsAction("input.action.options_wiring_not_listed", "key:c");
            var options = new PresentationAssemblyOptions
            {
                SettingsActionNames = new[] { first.ActionId.Value, second.ActionId.Value },
            };

            var presentation = Build(out _, out _, out _, out _, options);
            Assert.Empty(presentation.Settings.Bindings);

            presentation.InputMap.DeclareActionSet(new Id("input.set.options_wiring_a"), new[] { second, notListed });
            presentation.Settings.Refresh();
            Assert.Equal(
                new[] { second.ActionId.Value },
                presentation.Settings.Bindings.Select(r => r.ActionName).ToArray());

            presentation.InputMap.DeclareActionSet(new Id("input.set.options_wiring_b"), new[] { first });
            presentation.Settings.Refresh();
            Assert.Equal(
                new[] { first.ActionId.Value, second.ActionId.Value },
                presentation.Settings.Bindings.Select(r => r.ActionName).ToArray());
        }

        // -----------------------------------------------------------------
        // FlashProfileResolver（OnFlash 未显式覆盖时，默认路径经 CharacterRig.ProceduralAnim）
        // -----------------------------------------------------------------

        [Fact]
        public void Options_FlashProfileResolver_Injected_DrivesFlashParamsPassedToRig_ReceivesRuleProfileId()
        {
            var resolvedProfiles = new List<Id>();
            var resolved = new FlashParams(FlashParams.Default.Intensity / 2, FlashParams.Default.DurationSeconds * 2);
            var options = new PresentationAssemblyOptions
            {
                FlashProfileResolver = profileId =>
                {
                    resolvedProfiles.Add(profileId);
                    return resolved;
                },
            };
            var rigFactory = new RigViewFactory();
            var presentation = Build(out var gameplay, out _, out _, out var bus, options, viewFactory: rigFactory);
            var playerId = gameplay.PlayerUnitProvider();
            presentation.ViewBinder.OnEntityCreated(playerId, "player", new Id("display.sample_player"));
            var rig = Assert.IsType<RecordingCharacterRig>(rigFactory.LastCreated!.Rig);

            bus.PublishImmediate(new CombatDamageDealtEvent(
                playerId, new Id("unit.smoke_target"), new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit));

            var flash = Assert.Single(rig.Anim.Flashes);
            Assert.Equal(resolved.Intensity, flash.Intensity);
            Assert.Equal(resolved.DurationSeconds, flash.DurationSeconds);
            // profile_id 取自 feedback.binding 数据行（AddMinimalPresentationTables 的 flash 规则）。
            Assert.Equal(new Id("feedback.flash.sample"), Assert.Single(resolvedProfiles));
        }

        [Fact]
        public void Options_FlashProfileResolver_IsIgnored_WhenOnFlashExplicitlyOverridden()
        {
            var resolverCalls = 0;
            var flashes = new List<(Id EntityId, Id ProfileId)>();
            var options = new PresentationAssemblyOptions
            {
                OnFlash = (entityId, profileId) => flashes.Add((entityId, profileId)),
                FlashProfileResolver = _ =>
                {
                    resolverCalls++;
                    return FlashParams.Default;
                },
            };
            var rigFactory = new RigViewFactory();
            var presentation = Build(out var gameplay, out _, out _, out var bus, options, viewFactory: rigFactory);
            var playerId = gameplay.PlayerUnitProvider();
            presentation.ViewBinder.OnEntityCreated(playerId, "player", new Id("display.sample_player"));
            var rig = Assert.IsType<RecordingCharacterRig>(rigFactory.LastCreated!.Rig);

            bus.PublishImmediate(new CombatDamageDealtEvent(
                playerId, new Id("unit.smoke_target"), new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit));

            Assert.NotEmpty(flashes);
            Assert.Equal(0, resolverCalls);
            Assert.Empty(rig.Anim.Flashes);
        }

        // -----------------------------------------------------------------
        // AutoConfigureCameraFromFirstProfile
        // -----------------------------------------------------------------

        [Fact]
        public void Options_AutoConfigureCameraFromFirstProfile_Default_ConfiguresFirstProfileAndFollowsPlayer()
        {
            var presentation = Build(out var gameplay, out _, out _, out _);

            Assert.NotNull(presentation.Camera.CurrentProfile);
            // 数据集只有一条 camera_profile 行（AddMinimalPresentationTables），第一条即它。
            Assert.Equal(new Id("camera_profile.sample_default"), presentation.Camera.CurrentProfile!.Id);
            Assert.Equal(gameplay.PlayerUnitProvider(), presentation.Camera.FollowEntityId);
        }

        [Fact]
        public void Options_AutoConfigureCameraFromFirstProfile_False_LeavesCameraUnconfiguredAndNotFollowing()
        {
            var options = new PresentationAssemblyOptions { AutoConfigureCameraFromFirstProfile = false };

            var presentation = Build(out _, out _, out _, out var bus, options);

            Assert.Null(presentation.Camera.CurrentProfile);
            Assert.Null(presentation.Camera.FollowEntityId);

            // 选项关闭时装配根也不替调用方兜底"切图后重新跟随玩家"（PRES-118-CAMERA 默认解析函数只在打开时提供）。
            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("world.options_wiring_next_map")));
            Assert.Null(presentation.Camera.FollowEntityId);
        }

        // -----------------------------------------------------------------
        // NewGameStarter / TimestampProvider（ShellHost 装配接线）
        // -----------------------------------------------------------------

        private static readonly Id OptionsDifficultyId = new Id("diff.options_wiring_normal");

        private static void AddOptionsDifficultyAndStartMap(InMemoryDataSource source, Id startMapId)
        {
            source.Add("diff.tier",
                "{\"table\": \"diff.tier\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + OptionsDifficultyId.Value + "\", \"name_key\": \"l10n.diff.options_wiring_normal.name\", " +
                "\"modifier_aura_refs\": [], \"loot_multiplier\": 1.0, \"sort_weight\": 0}" +
                "]}");
            source.Add("world.map",
                "{\"table\": \"world.map\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + startMapId.Value + "\", \"scene_ref\": \"scene." + startMapId.Value + "\", " +
                "\"nav_ref\": \"nav." + startMapId.Value + "\", " +
                "\"spawn_points\": [{\"id\": \"spawn." + startMapId.Value + ".default\", \"position\": {\"x\": 0, \"y\": 0}, \"facing\": 0}]}" +
                "]}");
        }

        [Fact]
        public void Options_NewGameStarter_Injected_ReceivesShellNewGameArguments_AndReturnedMapIsLoaded()
        {
            var startMapId = new Id("world.options_wiring_start");
            var slotId = new Id("save.options_wiring_new_game");
            var archetypeId = new Id("arch.options_wiring_archetype");
            var received = new List<(Id Slot, Id Difficulty, Id? Archetype)>();
            var options = new PresentationAssemblyOptions
            {
                NewGameStarter = (slot, difficulty, archetype) =>
                {
                    received.Add((slot, difficulty, archetype));
                    return startMapId;
                },
            };
            var presentation = Build(out var gameplay, out _, out _, out var bus, options,
                extraTables: source => AddOptionsDifficultyAndStartMap(source, startMapId));
            var loadStarted = new List<Id>();
            bus.Subscribe<SceneLoadStartedEvent>(SceneRouterEventKeys.LoadStarted, e => loadStarted.Add(e.SceneId));
            Assert.True(presentation.Shell.Start());

            var ok = presentation.Shell.NewGame(slotId, OptionsDifficultyId, archetypeId);

            Assert.True(ok);
            var call = Assert.Single(received);
            Assert.Equal(slotId, call.Slot);
            Assert.Equal(OptionsDifficultyId, call.Difficulty);
            Assert.Equal(archetypeId, call.Archetype);
            Assert.Equal(OptionsDifficultyId, gameplay.Difficulty.CurrentTier);
            Assert.Equal(new[] { startMapId }, loadStarted);
        }

        [Fact]
        public void Options_NewGameStarter_Default_ThrowsNotSupportedWhenNewGameInvoked_ButNotAtConstruction()
        {
            var startMapId = new Id("world.options_wiring_start");
            var presentation = Build(out _, out _, out _, out _,
                extraTables: source => AddOptionsDifficultyAndStartMap(source, startMapId));

            Assert.Throws<NotSupportedException>(() =>
                presentation.Shell.NewGame(new Id("save.options_wiring_default_starter"), OptionsDifficultyId, null));
        }

        [Fact]
        public void Options_TimestampProvider_Injected_StampsSavedSlotMeta_WithProvidedText()
        {
            var calls = 0;
            var options = new PresentationAssemblyOptions
            {
                TimestampProvider = () => "options-wiring-stamp-" + (++calls).ToString(CultureInfo.InvariantCulture),
            };
            var presentation = Build(out _, out _, out _, out _, options);
            var slotId = new Id("save.options_wiring_timestamp");

            var result = presentation.Shell.OverwriteSlot(slotId, null, null);

            Assert.True(result.Success);
            Assert.Equal(1, calls);
            var slot = Assert.Single(presentation.SaveSystem.ListSlots());
            Assert.Equal(slotId, slot.SlotId);
            Assert.Equal("options-wiring-stamp-1", slot.Meta.UpdatedAt);
        }

        [Fact]
        public void Options_TimestampProvider_Default_StampsIso8601RoundtripUtcText()
        {
            var presentation = Build(out _, out _, out _, out _);
            var slotId = new Id("save.options_wiring_default_timestamp");

            var result = presentation.Shell.OverwriteSlot(slotId, null, null);

            Assert.True(result.Success);
            var stamp = Assert.Single(presentation.SaveSystem.ListSlots()).Meta.UpdatedAt;
            // 默认来源是 DateTime.UtcNow.ToString("o")：断言格式契约（往返 ISO-8601、UTC），不比较墙钟。
            Assert.True(
                DateTime.TryParseExact(stamp, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed),
                "默认时间戳应为 ISO-8601 往返格式，实际：" + stamp);
            Assert.Equal(DateTimeKind.Utc, parsed.Kind);
            Assert.Equal(stamp, parsed.ToString("o", CultureInfo.InvariantCulture));
        }

        // -----------------------------------------------------------------
        // ViewBinderOptions
        // -----------------------------------------------------------------

        private static (PresentationAssembly Presentation, Tests.PresentationViewBinding.FakeView View) BuildWithBoundPlayerView(
            PresentationAssemblyOptions? options, out Core.Gameplay.Assembly.GameplayAssembly gameplay,
            out Core.Foundation.SimLoop.WorldSim world, out IEventBus bus, IEventBus? busOverride = null)
        {
            var factory = new Tests.PresentationViewBinding.FakeViewFactory();
            var presentation = Build(out gameplay, out world, out _, out bus, options, viewFactory: factory, busOverride: busOverride);
            var playerId = gameplay.PlayerUnitProvider();
            presentation.ViewBinder.OnEntityCreated(playerId, "player", new Id("display.sample_player"));
            return (presentation, factory.CreatedByEntityId[playerId]);
        }

        [Fact]
        public void Options_ViewBinderOptions_ForwardedEventKeys_RestrictsWhichEventsReachTheView()
        {
            var options = new PresentationAssemblyOptions
            {
                ViewBinderOptions = new ViewBinderOptions(new[] { RulesEventKeys.CombatDamageDealt }),
            };
            var (_, view) = BuildWithBoundPlayerView(options, out var gameplay, out _, out var bus);
            var playerId = gameplay.PlayerUnitProvider();
            var damage = new CombatDamageDealtEvent(
                playerId, new Id("unit.smoke_target"), new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit);
            var equipped = new Core.Carriers.Common.ItemEquippedEvent(
                playerId, new Id("item_instance.options_wiring"), OptionsMainHandSlot);

            bus.PublishImmediate(damage);
            bus.PublishImmediate(equipped);

            Assert.Contains(damage, view.ReceivedEvents);
            Assert.DoesNotContain(equipped, view.ReceivedEvents);

            // 对照：默认转发键集合也转发 item.equipped（证明上面的差异来自选项，而不是事件本身不可转发）。
            var (_, defaultView) = BuildWithBoundPlayerView(null, out var defaultGameplay, out _, out var defaultBus);
            var defaultEquipped = new Core.Carriers.Common.ItemEquippedEvent(
                defaultGameplay.PlayerUnitProvider(), new Id("item_instance.options_wiring"), OptionsMainHandSlot);
            defaultBus.PublishImmediate(defaultEquipped);
            Assert.Contains(defaultEquipped, defaultView.ReceivedEvents);
        }

        [Fact]
        public void Options_ViewBinderOptions_DefaultDirectionCount_QuantizesFacingWhenNoDisplayInfoRegistered()
        {
            const double facing = Math.PI / 2;
            var configuredCount = new ViewBinderOptions().DefaultDirectionCount / 2;
            var options = new PresentationAssemblyOptions
            {
                ViewBinderOptions = new ViewBinderOptions(defaultDirectionCount: configuredCount),
            };
            var (presentation, view) = BuildWithBoundPlayerView(options, out var gameplay, out var world, out _);
            world.GetEntity(gameplay.PlayerUnitProvider())!.Facing = facing;

            presentation.ViewBinder.SyncAll(1.0);

            var synced = view.SyncCalls[view.SyncCalls.Count - 1].Facing;
            Assert.Equal(Direction.FromQuantized(facing, configuredCount), synced);
            // 选项确实改变了结果：与默认档位数量化出的方向不同。
            Assert.NotEqual(Direction.FromQuantized(facing, new ViewBinderOptions().DefaultDirectionCount), synced);
        }

        // -----------------------------------------------------------------
        // RenderOptions（同一个 RenderConventionHost 实例被 Render 属性与 ViewBinder 共用）
        // -----------------------------------------------------------------

        [Fact]
        public void Options_RenderOptions_FacingConvention_AppliedToViewBinderSyncPose_AndRenderProperty()
        {
            const double facing = Math.PI / 4;
            var renderOptions = new RenderOptions { MirrorFacingY = true, FacingAngleOffsetRadians = Math.PI };
            var options = new PresentationAssemblyOptions { RenderOptions = renderOptions };
            var (presentation, view) = BuildWithBoundPlayerView(options, out var gameplay, out var world, out _);
            world.GetEntity(gameplay.PlayerUnitProvider())!.Facing = facing;

            presentation.ViewBinder.SyncAll(1.0);

            // 规则（RenderOptions 字段注释）：先镜像（取负）、再加偏移，然后按方向档位数量化。
            var converted = -facing + renderOptions.FacingAngleOffsetRadians;
            Assert.Equal(converted, presentation.Render.ApplyFacingConvention(facing));
            var synced = view.SyncCalls[view.SyncCalls.Count - 1].Facing;
            Assert.Equal(Direction.FromQuantized(converted, new ViewBinderOptions().DefaultDirectionCount), synced);
            Assert.NotEqual(Direction.FromQuantized(facing, new ViewBinderOptions().DefaultDirectionCount), synced);
        }

        [Fact]
        public void Options_RenderOptions_DirectionIndexRemap_AppliedByRenderPropertyDirectionSlotResolution()
        {
            const int count = 8;
            var remap = Enumerable.Range(0, count).Select(i => (i + count / 2) % count).ToArray();
            var options = new PresentationAssemblyOptions { RenderOptions = new RenderOptions { DirectionIndexRemap = remap } };
            var sprite = new Core.Foundation.DisplayInfo.SpriteInfo("sprite.options_wiring", count);
            var rawIndex = 0;
            var direction = Direction.FromQuantized(rawIndex * 2 * Math.PI / count, count);
            Assert.Equal(rawIndex, direction.Index);

            var remapped = Build(out _, out _, out _, out _, options).Render.ResolveDirectionSlot(direction, sprite);
            var identityHost = Build(out _, out _, out _, out _).Render;

            // 重映射表把 raw 索引换成 remap[raw]：装配出的 Render 解析结果等于恒等映射下直接解析 remap[raw] 号方向的结果，
            // 且与恒等映射下解析 raw 号方向的结果不同。
            var expected = identityHost.ResolveDirectionSlot(new Direction(direction.RawRadians, remap[rawIndex], count), sprite);
            Assert.Equal(expected, remapped);
            Assert.NotEqual(identityHost.ResolveDirectionSlot(direction, sprite), remapped);
        }

        // -----------------------------------------------------------------
        // VfxOptions
        // -----------------------------------------------------------------

        [Fact]
        public void Options_VfxOptions_FirstLoadTimeoutSeconds_GovernsHowLongColdSpawnStaysPending()
        {
            var timeout = new VfxOptions().FirstLoadTimeoutSeconds / 5;
            var vfxId = new Id("vfx.sample_hit");
            var options = new PresentationAssemblyOptions { VfxOptions = new VfxOptions { FirstLoadTimeoutSeconds = timeout } };

            var presentation = Build(out _, out _, out var engine, out _, options, withResourceLoader: true);
            engine.ResourceLoader.DeferCallbacks = true;
            // 冷加载：返回占位句柄（ADR-0121 D1），请求排队等待首次加载完成。
            Assert.NotNull(presentation.Vfx.Spawn(vfxId, VfxAttach.World(Vec2.Zero), null));
            Assert.Equal(1, presentation.Vfx.PendingSpawnCount);

            presentation.Vfx.Update(timeout / 2);
            Assert.Equal(1, presentation.Vfx.PendingSpawnCount);
            presentation.Vfx.Update(timeout);
            Assert.Equal(0, presentation.Vfx.PendingSpawnCount);

            // 对照：不注入选项时（默认更长的超时）同样的累计时间内仍在等待。
            var defaults = Build(out _, out _, out var defaultEngine, out _, withResourceLoader: true);
            defaultEngine.ResourceLoader.DeferCallbacks = true;
            Assert.NotNull(defaults.Vfx.Spawn(vfxId, VfxAttach.World(Vec2.Zero), null));
            defaults.Vfx.Update(timeout * 1.5);
            Assert.Equal(1, defaults.Vfx.PendingSpawnCount);
        }

        // -----------------------------------------------------------------
        // MenuOverlayPanelIds
        // -----------------------------------------------------------------

        private static void EnterInWorld(Core.Gameplay.Assembly.GameplayAssembly gameplay)
        {
            Assert.True(gameplay.AppState.RequestTransition(AppState.MainMenu));
            Assert.True(gameplay.AppState.RequestTransition(AppState.Loading));
            Assert.True(gameplay.AppState.RequestTransition(AppState.InWorld));
        }

        [Fact]
        public void Options_MenuOverlayPanelIds_Injected_OpeningPanelPushesMenuOverlaySubState_ClosingPops()
        {
            var panelId = new Id("ui_layout_definition.sample_action_bar");
            var options = new PresentationAssemblyOptions { MenuOverlayPanelIds = new[] { panelId } };
            var presentation = Build(out var gameplay, out _, out _, out _, options);
            EnterInWorld(gameplay);
            Assert.Equal(InWorldSubState.Explore, gameplay.AppState.CurrentSubState);

            presentation.Panels.Open(panelId);
            Assert.Equal(InWorldSubState.MenuOverlay, gameplay.AppState.CurrentSubState);

            presentation.Panels.Close(panelId);
            Assert.Equal(InWorldSubState.Explore, gameplay.AppState.CurrentSubState);
        }

        [Fact]
        public void Options_MenuOverlayPanelIds_Default_OpeningPanelDoesNotChangeSubState()
        {
            var panelId = new Id("ui_layout_definition.sample_action_bar");
            var presentation = Build(out var gameplay, out _, out _, out _);
            EnterInWorld(gameplay);

            presentation.Panels.Open(panelId);

            Assert.True(presentation.Panels.IsOpen(panelId));
            Assert.Equal(InWorldSubState.Explore, gameplay.AppState.CurrentSubState);
        }

        // -----------------------------------------------------------------
        // EquipmentVisualSource（经 ViewBinder 的 save.loaded 装备外观对账）
        // -----------------------------------------------------------------

        private sealed class ResettableRecordingView : IView, IEquipmentVisualResettable
        {
            public readonly List<IReadOnlyList<EquippedItemRef>> Resets = new List<IReadOnlyList<EquippedItemRef>>();

            public Id EntityId { get; private set; }
            public bool IsAlive { get; private set; }

            public void Bind(Id entityId)
            {
                EntityId = entityId;
                IsAlive = true;
            }

            public void OnEvent(IEvent evt) { }
            public void SyncPose(Vec2 pos, Direction facing, double height) { }
            public void Destroy() => IsAlive = false;
            public void ResetEquipmentVisuals(IReadOnlyList<EquippedItemRef> equipped) => Resets.Add(equipped);
        }

        private sealed class ResettableViewFactory : IViewFactory
        {
            public ResettableRecordingView? Last;

            public IView CreateView(ViewKind kind, Id displayId, Id entityId)
            {
                Last = new ResettableRecordingView();
                return Last;
            }
        }

        [Fact]
        public void Options_EquipmentVisualSource_Injected_SaveLoadedReconcilesViewAgainstSnapshotAndCatalog()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });
            var templateId = new Id("item.sample_sword");
            var instanceId = new Id("item_instance.options_wiring_visual");
            var visual = new EquipVisualDef(
                new Id("display.equip_visual.options_wiring"), templateId, EquipVisualMode.SlotMesh,
                slotId: null, meshRef: null, socketId: null, modelRef: null);
            var catalog = new Dictionary<Id, EquipVisualDef> { [templateId] = visual };
            var equippedRef = new EquippedItemRef(OptionsMainHandSlot, instanceId, templateId);
            var resolverCalls = new List<Id>();
            var source = new EquipmentVisualSource(bus, catalog, unitId =>
            {
                resolverCalls.Add(unitId);
                return new[] { equippedRef };
            });
            var options = new PresentationAssemblyOptions { EquipmentVisualSource = source };
            var factory = new ResettableViewFactory();
            var presentation = Build(out var gameplay, out _, out _, out _, options, viewFactory: factory, busOverride: bus);
            var playerId = gameplay.PlayerUnitProvider();
            presentation.ViewBinder.OnEntityCreated(playerId, "player", new Id("display.sample_player"));
            Assert.Empty(factory.Last!.Resets);

            bus.PublishImmediate(new SaveLoadedEvent(new Id("save.options_wiring_visual")));

            Assert.Equal(new[] { playerId }, resolverCalls);
            var reset = Assert.Single(factory.Last!.Resets);
            Assert.Equal(equippedRef.ItemInstanceId, Assert.Single(reset).ItemInstanceId);
            Assert.Same(visual, source.VisualByItemInstanceId[instanceId]);
            source.Dispose();
        }

        [Fact]
        public void Options_EquipmentVisualSource_Default_SaveLoadedDoesNotResetViewEquipmentVisuals()
        {
            var factory = new ResettableViewFactory();
            var presentation = Build(out var gameplay, out _, out _, out var bus, viewFactory: factory);
            presentation.ViewBinder.OnEntityCreated(gameplay.PlayerUnitProvider(), "player", new Id("display.sample_player"));

            bus.PublishImmediate(new SaveLoadedEvent(new Id("save.options_wiring_visual_default")));

            Assert.Empty(factory.Last!.Resets);
        }

        // -----------------------------------------------------------------
        // hitFrameSource 构造参数 + FeedbackOptions.HitFrameSync（命中帧同步接线）
        // -----------------------------------------------------------------

        private sealed class FakeHitFrameSource : IHitFrameSource
        {
            private Action<Id>? _handlers;

            /// <summary>当前订阅 <see cref="HitFrameReached"/> 的处理函数个数（Dispose 完整性用例观测退订）。</summary>
            public int SubscriberCount => _handlers == null ? 0 : _handlers.GetInvocationList().Length;

            public event Action<Id>? HitFrameReached
            {
                add => _handlers += value;
                remove => _handlers -= value;
            }

            public bool AllHaveRig { get; set; } = true;

            public void RegisterRig(Id entityId, ICharacterRig rig) { }
            public void UnregisterRig(Id entityId) { }
            public bool HasRig(Id entityId) => AllHaveRig;
            public void Raise(Id entityId) => _handlers?.Invoke(entityId);
        }

        [Fact]
        public void HitFrameSourceCtorArg_WithAnimKeyframeDrivenSync_DefersDamageFeedbackUntilHitFrameReached()
        {
            var attacker = new Id("unit.smoke_player");
            var source = new FakeHitFrameSource();
            var floatingTexts = new List<string>();
            var options = new PresentationAssemblyOptions
            {
                OnFloatingText = (_, __, text) => floatingTexts.Add(text),
                FeedbackOptions = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven },
            };
            var presentation = Build(out _, out _, out _, out var bus, options, hitFrameSource: source);

            bus.PublishImmediate(NewDamageEvent());

            // combat.damage_dealt 默认 sync=hit_frame：攻击方有 rig 时等命中帧，之前不派发飘字。
            Assert.Empty(floatingTexts);

            source.Raise(attacker);

            Assert.Single(floatingTexts);
            Assert.NotNull(presentation);
        }

        [Fact]
        public void HitFrameSourceCtorArg_Omitted_DamageFeedbackDispatchesImmediately_EvenWithAnimKeyframeDrivenSync()
        {
            var floatingTexts = new List<string>();
            var options = new PresentationAssemblyOptions
            {
                OnFloatingText = (_, __, text) => floatingTexts.Add(text),
                FeedbackOptions = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven },
            };
            Build(out _, out _, out _, out var bus, options);

            bus.PublishImmediate(NewDamageEvent());

            // 未注入命中帧来源：不构造等待队列，sync: hit_frame 声明被忽略，全部动作立即派发。
            Assert.Single(floatingTexts);
        }
    }
}
