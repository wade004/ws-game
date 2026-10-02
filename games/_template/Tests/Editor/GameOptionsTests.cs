#nullable enable
// GameOptionsTests：测试覆盖第四批 T-M48——口味配置项容器 GameOptions（13 §4 逐行对应）此前没有对着它自己的
// 直接断言。关注四类性质：
//  ① 默认口味 = 框架默认：凡是映射到框架某个 Options 类的字段，默认值构造出来的 Options 必须与框架自己的
//     默认实例等价（期望值取自 new XxxOptions()，不在用例里写裸数），否则“不改任何口味配置项”就悄悄改变了
//     框架行为；
//  ② 非默认值逐字段透传：每个映射字段设成与默认不同的值，对应 Build*Options 必须反映出来（防止漏接线）；
//  ③ 默认 id 串合法且带 template 占位（不得是具体游戏代号，复制为新游戏时由使用者替换）；
//  ④ Inspector 序列化保证：类是 [Serializable] 纯 C# 类，所有公开值字段经 JsonUtility 往返不丢（委托字段不序列化，排除）。
//
// 判断记录（用反射调用 internal 的 Build*Options，而不是给模板加 InternalsVisibleTo）：模板改名后程序集名会
// 变化，硬编码旧程序集名的 InternalsVisibleTo 会失效（见 DataHotReload.ProcessPendingChanges 判断记录）；
// 方法名是模板契约的一部分（GameBootstrap 逐个调用），缺失时用例明确失败。
using System;
using System.Linq;
using System.Reflection;
using Core.Foundation.Common;
using Core.Rules.Combat;
using Core.Rules.Common;
using Core.Rules.Skill;
using Core.Rules.Targeting;
using NUnit.Framework;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using UnityEngine;

namespace Game.Template.EditorTests
{
    public sealed class GameOptionsTests
    {
        private static T Build<T>(GameOptions options, string methodName)
        {
            var method = typeof(GameOptions).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(method, $"GameOptions.{methodName} 不存在——GameBootstrap 依赖它");
            return (T)method!.Invoke(options, null)!;
        }

        // ------------------------------------------------------------------
        // ① 默认口味 = 框架默认
        // ------------------------------------------------------------------

        [Test]
        public void Defaults_CombatOptions_EqualFrameworkDefaults()
        {
            var built = Build<CombatOptions>(new GameOptions(), "BuildCombatOptions");
            var framework = new CombatOptions();
            Assert.AreEqual(framework.HitTableConfigId, built.HitTableConfigId);
            Assert.AreEqual(framework.DeathPolicy, built.DeathPolicy);
        }

        [Test]
        public void Defaults_SkillOptions_EqualFrameworkDefaults()
        {
            var built = Build<SkillOptions>(new GameOptions(), "BuildSkillOptions");
            var framework = new SkillOptions();
            Assert.AreEqual(framework.GcdEnabled, built.GcdEnabled);
            Assert.AreEqual(framework.GcdDuration, built.GcdDuration);
            Assert.AreEqual(framework.AllowMultiSourceTiming, built.AllowMultiSourceTiming);
        }

        [Test]
        public void Defaults_TargetingMovementLootRenderFeedback_EqualFrameworkDefaults()
        {
            var options = new GameOptions();

            Assert.AreEqual(new TargetingOptions().DefaultRadius,
                Build<TargetingOptions>(options, "BuildTargetingOptions").DefaultRadius);

            var movement = Build<Core.Carriers.Unit.MovementOptions>(options, "BuildMovementOptions");
            var frameworkMovement = new Core.Carriers.Unit.MovementOptions();
            Assert.AreEqual(frameworkMovement.DiscreteTurnEquivalentSeconds, movement.DiscreteTurnEquivalentSeconds);
            Assert.AreEqual(frameworkMovement.UnitBlocking, movement.UnitBlocking);
            Assert.AreEqual(frameworkMovement.PathFailurePolicy, movement.PathFailurePolicy);
            Assert.AreEqual(frameworkMovement.BlockingChangePolicy, movement.BlockingChangePolicy);

            Assert.AreEqual(new Core.Gameplay.Loot.LootOptions().PersistDropped,
                Build<Core.Gameplay.Loot.LootOptions>(options, "BuildLootOptions").PersistDropped);

            var render = Build<RenderOptions>(options, "BuildRenderOptions");
            var frameworkRender = new RenderOptions();
            Assert.AreEqual(frameworkRender.DirectionCount, render.DirectionCount);
            Assert.AreEqual(frameworkRender.HitFrameSync, render.HitFrameSync);

            var feedback = Build<FeedbackOptions>(options, "BuildFeedbackOptions");
            var frameworkFeedback = new FeedbackOptions();
            Assert.AreEqual(frameworkFeedback.MergeWindow, feedback.MergeWindow);
            Assert.AreEqual(frameworkFeedback.HitFrameSync, feedback.HitFrameSync);
        }

        [Test]
        public void Defaults_PlaceholderFlavors_AreOffExceptTheDocumentedOnes()
        {
            var o = new GameOptions();

            // 13 §4 里“待游戏层使用 / 无框架 Options 字段”的立项决策记录位：默认全部关闭。
            Assert.IsFalse(o.ConsumablesEnabled);
            Assert.IsFalse(o.JumpEnabled);
            Assert.IsFalse(o.MultiplePowerPoolsEnabled);
            Assert.IsFalse(o.DiminishingReturnsEnabled);
            Assert.IsFalse(o.UseModelShape);
            Assert.IsFalse(o.Use3DScene);
            Assert.IsFalse(o.GlobalCooldownEnabled);
            Assert.IsFalse(o.AuraMultiSourceTimingEnabled);
            Assert.IsFalse(o.UnitBlockEnabled);
            Assert.IsFalse(o.PacingWaitForPlayback, "默认节奏策略 = immediate");
            Assert.IsFalse(o.HitFrameSyncEnabled, "默认命中帧同步 = LogicDriven");

            // 文档化为默认开启的口味项。
            Assert.IsTrue(o.EnableDataHotReload, "开发期热重载默认开（发布构建下组件本身是空壳，见 DataHotReload 编译条件）");
            Assert.IsTrue(o.PersistDroppedLoot);
            Assert.IsTrue(o.AutoSaveOnSavePoint);
            Assert.IsTrue(o.AutoSaveOnQuestComplete);
            Assert.IsFalse(o.AutoSaveOnMapSwitch);
            Assert.IsTrue(o.CameraResetFollowOnSceneLoadFinished);
        }

        [Test]
        public void Defaults_EquipmentSlotIds_EmptyAndNoCallbacksWired()
        {
            var o = new GameOptions();
            Assert.IsEmpty(o.EquipmentSlotIds);
            Assert.IsEmpty(Build<System.Collections.Generic.IReadOnlyList<Id>>(o, "BuildEquipmentSlotIds"));
            Assert.IsNull(o.QuestOwnerResolver);
            Assert.IsNull(o.QuestDayProvider);
            Assert.IsNull(o.VendorOpenRequested);
        }

        // ------------------------------------------------------------------
        // ② 非默认值逐字段透传
        // ------------------------------------------------------------------

        [Test]
        public void NonDefaultValues_FlowThroughEveryBuildMethod()
        {
            var framework = new CombatOptions();
            var o = new GameOptions
            {
                HitTableConfigId = "combat.hit_table.custom_probe",
                DeathPolicy = framework.DeathPolicy == RespawnPolicy.ReloadSave ? RespawnPolicy.RespawnPoint : RespawnPolicy.ReloadSave,
                GlobalCooldownEnabled = true,
                GlobalCooldownDuration = new SkillOptions().GcdDuration + 0.75,
                AuraMultiSourceTimingEnabled = true,
                TargetingDefaultRadius = new TargetingOptions().DefaultRadius + 3.5,
                DiscreteTurnEquivalentSeconds = new Core.Carriers.Unit.MovementOptions().DiscreteTurnEquivalentSeconds + 0.5,
                UnitBlockEnabled = true,
                PathFailurePolicy = Core.Carriers.Unit.PathFailurePolicy.Stop,
                BlockingChangePolicy = Core.Carriers.Unit.BlockingChangePolicy.Revalidate,
                PersistDroppedLoot = false,
                DirectionCount = new RenderOptions().DirectionCount / 2,
                HitFrameSyncEnabled = true,
                FeedbackMergeWindowSeconds = 0.35,
            };

            var combat = Build<CombatOptions>(o, "BuildCombatOptions");
            Assert.AreEqual(new Id("combat.hit_table.custom_probe"), combat.HitTableConfigId);
            Assert.AreEqual(o.DeathPolicy, combat.DeathPolicy);
            Assert.AreNotEqual(framework.DeathPolicy, combat.DeathPolicy);

            var skill = Build<SkillOptions>(o, "BuildSkillOptions");
            Assert.IsTrue(skill.GcdEnabled);
            Assert.AreEqual(o.GlobalCooldownDuration, skill.GcdDuration);
            Assert.IsTrue(skill.AllowMultiSourceTiming);

            Assert.AreEqual(o.TargetingDefaultRadius, Build<TargetingOptions>(o, "BuildTargetingOptions").DefaultRadius);

            var movement = Build<Core.Carriers.Unit.MovementOptions>(o, "BuildMovementOptions");
            Assert.AreEqual(o.DiscreteTurnEquivalentSeconds, movement.DiscreteTurnEquivalentSeconds);
            Assert.IsTrue(movement.UnitBlocking);
            Assert.AreEqual(o.PathFailurePolicy, movement.PathFailurePolicy);
            Assert.AreEqual(o.BlockingChangePolicy, movement.BlockingChangePolicy);
            Assert.AreNotEqual(new Core.Carriers.Unit.MovementOptions().PathFailurePolicy, movement.PathFailurePolicy);
            Assert.AreNotEqual(new Core.Carriers.Unit.MovementOptions().BlockingChangePolicy, movement.BlockingChangePolicy);

            Assert.IsFalse(Build<Core.Gameplay.Loot.LootOptions>(o, "BuildLootOptions").PersistDropped);

            var render = Build<RenderOptions>(o, "BuildRenderOptions");
            Assert.AreEqual(o.DirectionCount, render.DirectionCount);
            Assert.AreEqual(HitFrameSyncStrategy.AnimKeyframeDriven, render.HitFrameSync);

            var feedback = Build<FeedbackOptions>(o, "BuildFeedbackOptions");
            Assert.AreEqual(o.FeedbackMergeWindowSeconds, feedback.MergeWindow);
            Assert.AreEqual(HitFrameSyncStrategy.AnimKeyframeDriven, feedback.HitFrameSync,
                "命中帧同步是同一个口味项的两个落点（渲染侧/反馈绑定侧），必须同值");
        }

        [Test]
        public void HitFrameSync_RenderAndFeedbackSides_AlwaysAgree_ForBothSwitchPositions()
        {
            foreach (var enabled in new[] { false, true })
            {
                var o = new GameOptions { HitFrameSyncEnabled = enabled };
                Assert.AreEqual(
                    Build<RenderOptions>(o, "BuildRenderOptions").HitFrameSync,
                    Build<FeedbackOptions>(o, "BuildFeedbackOptions").HitFrameSync,
                    $"HitFrameSyncEnabled={enabled}");
            }
        }

        [Test]
        public void BuildEquipmentSlotIds_ConvertsEveryEntryToIdInOrder_AndRejectsMalformedId()
        {
            var o = new GameOptions { EquipmentSlotIds = new[] { "item.slot.main_hand", "item.slot.off_hand", "item.slot.head" } };
            var ids = Build<System.Collections.Generic.IReadOnlyList<Id>>(o, "BuildEquipmentSlotIds");
            Assert.AreEqual(o.EquipmentSlotIds.Select(s => new Id(s)).ToArray(), ids.ToArray());

            var bad = new GameOptions { EquipmentSlotIds = new[] { "item.slot.ok", "NOT A VALID ID" } };
            var ex = Assert.Throws<TargetInvocationException>(() => Build<System.Collections.Generic.IReadOnlyList<Id>>(bad, "BuildEquipmentSlotIds"));
            Assert.IsInstanceOf<ArgumentException>(ex!.InnerException, "格式非法的槽位 id 应在装配期明确失败，而不是静默带进运行期");
        }

        [Test]
        public void BuildMethods_ReturnFreshInstancePerCall_SoCallersCannotMutateEachOthersOptions()
        {
            var o = new GameOptions();
            var a = Build<SkillOptions>(o, "BuildSkillOptions");
            var b = Build<SkillOptions>(o, "BuildSkillOptions");
            Assert.AreNotSame(a, b);
            a.GcdEnabled = !a.GcdEnabled;
            Assert.AreNotEqual(a.GcdEnabled, b.GcdEnabled);
        }

        // ------------------------------------------------------------------
        // ③ 默认 id 串
        // ------------------------------------------------------------------

        [Test]
        public void DefaultIdStrings_AreWellFormedIds()
        {
            var o = new GameOptions();
            foreach (var value in new[]
            {
                o.StartMapId, o.PlayerUnitId, o.PlayerFactionId, o.PlayerClassId, o.PlayerTemplateId,
                o.DefaultDifficultyId, o.GameId, o.HitTableConfigId, o.MainHandSlotId,
            })
            {
                Assert.DoesNotThrow(() => new Id(value), $"默认 id 串 \"{value}\" 应符合 Id 格式");
            }
        }

        [Test]
        public void TemplateOwnedDefaultIds_CarryTheTemplatePlaceholder_NotAConcreteGameCodename()
        {
            var o = new GameOptions();
            // 模板自有的起始状态 id 一律带 template 占位，复制为新游戏时由使用者替换（见字段注释与 README）。
            foreach (var value in new[]
            {
                o.StartMapId, o.PlayerUnitId, o.PlayerFactionId, o.PlayerClassId, o.PlayerTemplateId,
                o.DefaultDifficultyId, o.GameId,
            })
            {
                StringAssert.Contains("template", value);
            }
        }

        [Test]
        public void Defaults_StartingState_IsInternallyConsistent()
        {
            var o = new GameOptions();
            Assert.GreaterOrEqual(o.PlayerLevel, 1);
            Assert.Greater(o.ActiveSkillSlotCount, 0);
            Assert.Greater(o.DirectionCount, 0);
            Assert.Greater(o.ReferenceResolutionWidth, 0);
            Assert.Greater(o.ReferenceResolutionHeight, 0);
            Assert.GreaterOrEqual(o.FeedbackMergeWindowSeconds, 0.0);
            Assert.GreaterOrEqual(o.TargetingDefaultRadius, 0.0);
            Assert.GreaterOrEqual(o.GlobalCooldownDuration, 0.0);
        }

        // ------------------------------------------------------------------
        // ④ Inspector 序列化保证
        // ------------------------------------------------------------------

        [Test]
        public void SerializableValueFields_RoundTripThroughJsonUtility_ForDefaultAndMutatedInstances()
        {
            var mutated = new GameOptions
            {
                StartMapId = "world.roundtrip",
                PlayerLevel = 7,
                Seed = 987654321UL,
                ActiveSkillSlotCount = 9,
                GlobalCooldownEnabled = true,
                GlobalCooldownDuration = 2.25,
                DeathPolicy = RespawnPolicy.ReloadSave,
                EquipmentSlotIds = new[] { "item.slot.a", "item.slot.b" },
                ExtraFrameworkDatasetRoots = new[] { "data/_extra_a", "data/_extra_b" },
                EnableDataHotReload = false,
                FeedbackMergeWindowSeconds = 0.5,
            };

            foreach (var original in new[] { new GameOptions(), mutated })
            {
                var json = JsonUtility.ToJson(original);
                var copy = JsonUtility.FromJson<GameOptions>(json);
                Assert.IsNotNull(copy);

                var valueFields = typeof(GameOptions)
                    .GetFields(BindingFlags.Instance | BindingFlags.Public)
                    .Where(f => !typeof(Delegate).IsAssignableFrom(f.FieldType))
                    .ToArray();
                Assert.Greater(valueFields.Length, 30, "应遍历到全部口味值字段");
                foreach (var field in valueFields)
                {
                    var expected = field.GetValue(original);
                    var actual = field.GetValue(copy);
                    if (field.FieldType.IsArray)
                    {
                        CollectionAssert.AreEqual((System.Collections.IEnumerable)expected!, (System.Collections.IEnumerable)actual!, field.Name);
                    }
                    else
                    {
                        Assert.AreEqual(expected, actual, field.Name + " 应经 JsonUtility 往返不变");
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // ⑤ 手感落地 M1 补缺：额外框架数据根（开启 FeelOptions 时自动含 data/_feel）
        // ------------------------------------------------------------------

        [Test]
        public void ExtraFrameworkDatasetRoots_DefaultIsEmpty_WhenFeelDisabled()
        {
            var options = new GameOptions();
            Assert.IsNull(options.FeelOptions);
            Assert.IsEmpty(Build<System.Collections.Generic.IReadOnlyList<string>>(options, "BuildExtraFrameworkDatasetRoots"),
                "不开手感、不配额外根时数据来源必须与此前一致（只有框架根与游戏根）");
        }

        [Test]
        public void ExtraFrameworkDatasetRoots_FeelEnabled_AutomaticallyIncludesFeelRoot_BeforeExtras_WithoutDuplicates()
        {
            var options = new GameOptions
            {
                FeelOptions = new Core.Carriers.Assembly.CarriersFeelOptions(),
                ExtraFrameworkDatasetRoots = new[] { "data/_extra_pack", GameOptions.FeelDatasetRoot, "", "data/_extra_pack" },
            };

            var roots = Build<System.Collections.Generic.IReadOnlyList<string>>(options, "BuildExtraFrameworkDatasetRoots");
            CollectionAssert.AreEqual(new[] { GameOptions.FeelDatasetRoot, "data/_extra_pack" }, roots.ToArray(),
                "顺序：手感根在前、额外根按声明顺序在后，空串与重复项被忽略");
        }

        [Test]
        public void ExtraFrameworkDatasetRoots_FeelDisabled_StillHonoursExplicitExtras()
        {
            var options = new GameOptions { ExtraFrameworkDatasetRoots = new[] { "data/_extra_pack" } };
            var roots = Build<System.Collections.Generic.IReadOnlyList<string>>(options, "BuildExtraFrameworkDatasetRoots");
            CollectionAssert.AreEqual(new[] { "data/_extra_pack" }, roots.ToArray());
        }
    }
}
