using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Gobj;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.Expr;
using Xunit;

namespace Tests.Carriers.Gobj
{
    /// <summary><see cref="GameObjectHost"/> 的行为测试：三种锁 requirement 各正反、十种类型的内置
    /// 交互行为、<c>on_use</c> 两种分发、<c>Locked</c>/距离过远/<c>gobj.interacted</c> 事件（见 07 第
    /// 9 节契约汇总表 GameObject 行"测试方式：脱离引擎构造带锁物件，分别用三种 requirement 驱动开锁
    /// 尝试，断言结果与预期一致"）。</summary>
    public sealed class GameObjectHostTests
    {
        private static readonly Id MapId = new Id("map.sample");
        private static readonly Id DisplayRef = new Id("display.sample_gobj");
        private static readonly Id Unit = new Id("unit.sample_1");

        private static JsonObject Template(
            string id, string kind, JsonObject typeData, string? lockId = null, JsonObject? onUse = null)
        {
            var fields = new List<(string, JsonValue)>
            {
                ("id", J.S(id)),
                ("name_key", J.S("l10n." + id.Replace('.', '_') + ".name")),
                ("kind", J.S(kind)),
                ("type_data", typeData),
                ("display_ref", J.S(DisplayRef.Value)),
            };

            if (lockId != null)
            {
                fields.Add(("lock_id", J.S(lockId)));
            }

            if (onUse != null)
            {
                fields.Add(("on_use", onUse));
            }

            return J.O(fields.ToArray());
        }

        private static JsonObject LockRow(string id, JsonObject requirement, bool consumeKey = false) =>
            J.O(("id", J.S(id)), ("requirement", requirement), ("consume_key", J.B(consumeKey)));

        // -----------------------------------------------------------------
        // 锁：三种 requirement 各正反 + consume_key + 已解锁跳过
        // -----------------------------------------------------------------

        [Fact]
        public void TryUnlock_ItemKey_PassesWithKey_FailsWithout()
        {
            const string lockId = "gobj.lock.sample_item";
            var itemId = new Id("item.sample_key");
            var world = new GobjWorldBuilder()
                .Lock(LockRow(lockId, J.O(("kind", J.S("item_key")), ("item_id", J.S(itemId.Value)))))
                .Template(Template("gobj.sample_door_a", "door", J.O(), lockId))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_a"), MapId, new Vec2(0, 0));

            Assert.False(world.Host.TryUnlock(Unit, gobjId));

            world.Inventory.AddItem(Unit, itemId, 1);
            Assert.True(world.Host.TryUnlock(Unit, gobjId));
        }

        [Fact]
        public void TryUnlock_WorldFlag_PassesWhenExpected_FailsOtherwise()
        {
            const string lockId = "gobj.lock.sample_flag";
            var flagKey = new Id("world.sample.flag_a");
            var world = new GobjWorldBuilder()
                .Lock(LockRow(lockId, J.O(("kind", J.S("world_flag")), ("flag_key", J.S(flagKey.Value)), ("expected", J.B(true)))))
                .Template(Template("gobj.sample_door_b", "door", J.O(), lockId))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_b"), MapId, new Vec2(0, 0));

            Assert.False(world.Host.TryUnlock(Unit, gobjId));

            world.Flags.Set(flagKey, ExprValue.OfBool(true), new Id("test.writer"));
            Assert.True(world.Host.TryUnlock(Unit, gobjId));
        }

        [Fact]
        public void TryUnlock_SkillCheck_PassesAboveThreshold_FailsBelow()
        {
            const string lockId = "gobj.lock.sample_skill";
            var skillTag = new Id("stat.sample_lockpick");
            var world = new GobjWorldBuilder()
                .Stat(skillTag.Value)
                .Lock(LockRow(lockId, J.O(("kind", J.S("skill_check")), ("skill_tag", J.S(skillTag.Value)), ("min_value", J.N(5)))))
                .Template(Template("gobj.sample_door_c", "door", J.O(), lockId))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_c"), MapId, new Vec2(0, 0));

            Assert.False(world.Host.TryUnlock(Unit, gobjId));

            world.Stats.SetBase(Unit, skillTag, 10);
            Assert.True(world.Host.TryUnlock(Unit, gobjId));
        }

        [Fact]
        public void TryUnlock_ConsumeKey_RemovesKeyItemOnSuccess()
        {
            const string lockId = "gobj.lock.sample_consume";
            var itemId = new Id("item.sample_key2");
            var world = new GobjWorldBuilder()
                .Lock(LockRow(lockId, J.O(("kind", J.S("item_key")), ("item_id", J.S(itemId.Value))), consumeKey: true))
                .Template(Template("gobj.sample_door_d", "door", J.O(), lockId))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            world.Inventory.AddItem(Unit, itemId, 1);
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_d"), MapId, new Vec2(0, 0));

            Assert.True(world.Host.TryUnlock(Unit, gobjId));
            Assert.Equal(0, world.Inventory.CountOf(Unit, itemId));
        }

        [Fact]
        public void TryUnlock_AlreadyUnlocked_DoesNotReCheckRequirement()
        {
            const string lockId = "gobj.lock.sample_once";
            var itemId = new Id("item.sample_key3");
            var world = new GobjWorldBuilder()
                .Lock(LockRow(lockId, J.O(("kind", J.S("item_key")), ("item_id", J.S(itemId.Value)))))
                .Template(Template("gobj.sample_door_e", "door", J.O(), lockId))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            world.Inventory.AddItem(Unit, itemId, 1);
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_e"), MapId, new Vec2(0, 0));

            Assert.True(world.Host.TryUnlock(Unit, gobjId));

            // 拿走钥匙后再次尝试仍应成功：已解锁标志跳过 requirement 重新校验。
            var instanceId = world.Inventory.ListItems(Unit)[0].InstanceId;
            world.Inventory.RemoveItem(Unit, instanceId, 1);
            Assert.True(world.Host.TryUnlock(Unit, gobjId));
        }

        // -----------------------------------------------------------------
        // key 格式（见 07 第 3.4 节、任务书给出的具体例子）
        // -----------------------------------------------------------------

        [Fact]
        public void GobjStateKeys_For_ProducesDocumentedFormat()
        {
            var instanceId = new Id("gobj.inst_1");
            Assert.Equal(new Id("world.gobj.inst_1.open_state"), GobjStateKeys.For(instanceId, "open_state"));
        }

        // -----------------------------------------------------------------
        // 按 kind 的内置交互行为
        // -----------------------------------------------------------------

        [Fact]
        public void Interact_Door_TogglesOpenStateAndEmitsStateChanged()
        {
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_door_f", "door", J.O()))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_f"), MapId, new Vec2(0, 0));

            var result = world.Host.Interact(Unit, gobjId);
            Assert.True(result.Success);
            world.Flush();

            var evt = Assert.Single(world.Of<GobjStateChangedEvent>());
            Assert.Equal(gobjId, evt.GobjInstanceId);
            Assert.Equal("open_state", evt.StateKey);
            Assert.False(evt.OldValue.AsBool);
            Assert.True(evt.NewValue.AsBool);

            Assert.Equal((ExprValue?)ExprValue.OfBool(true), world.Host.GetState(gobjId, "open_state"));

            // 再交互一次应该切回关闭。
            world.Host.Interact(Unit, gobjId);
            Assert.Equal((ExprValue?)ExprValue.OfBool(false), world.Host.GetState(gobjId, "open_state"));
        }

        [Fact]
        public void Interact_Chest_FirstOpenRollsLoot_SecondOpenDoesNotRepeat()
        {
            var lootTableRef = new Id("loot.sample_chest");
            var itemId = new Id("item.sample_gold");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_chest_a", "chest", J.O(("loot_table_ref", J.S(lootTableRef.Value)))))
                .Build();
            world.Loot.Table(lootTableRef, new ItemStack(itemId, 3));

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_chest_a"), MapId, new Vec2(0, 0));

            world.Host.Interact(Unit, gobjId);
            Assert.Equal(3, world.Inventory.CountOf(Unit, itemId));
            Assert.Single(world.Loot.Calls);

            world.Host.Interact(Unit, gobjId);
            Assert.Equal(3, world.Inventory.CountOf(Unit, itemId));
            Assert.Single(world.Loot.Calls);
        }

        [Fact]
        public void Interact_GatherNode_RespawnsAfterConfiguredDuration()
        {
            var lootTableRef = new Id("loot.sample_node");
            var itemId = new Id("item.sample_herb");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_node_a", "gather_node",
                    J.O(("loot_table_ref", J.S(lootTableRef.Value)), ("respawn_after_use", J.N(10)))))
                .Build();
            world.Loot.Table(lootTableRef, new ItemStack(itemId, 1));

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_node_a"), MapId, new Vec2(0, 0));

            world.SimTimeBox = 0;
            world.Host.Interact(Unit, gobjId);
            Assert.Equal(1, world.Inventory.CountOf(Unit, itemId));

            world.SimTimeBox = 5;
            world.Host.Interact(Unit, gobjId);
            Assert.Equal(1, world.Inventory.CountOf(Unit, itemId));

            world.SimTimeBox = 11;
            world.Host.Interact(Unit, gobjId);
            Assert.Equal(2, world.Inventory.CountOf(Unit, itemId));
        }

        [Fact]
        public void Interact_Lever_TogglesBothLinkedDoors()
        {
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_door_g", "door", J.O()))
                .Template(Template("gobj.sample_door_h", "door", J.O()))
                .Template(Template("gobj.sample_lever_a", "lever",
                    J.O(("linked_object_ids", J.Ids("gobj.inst_1", "gobj.inst_2")))))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));

            var doorA = world.SpawnFromTemplate(new Id("gobj.sample_door_g"), MapId, new Vec2(0, 0));
            var doorB = world.SpawnFromTemplate(new Id("gobj.sample_door_h"), MapId, new Vec2(0, 0));
            var lever = world.SpawnFromTemplate(new Id("gobj.sample_lever_a"), MapId, new Vec2(0, 0));

            // 联动引用的实例 id 依赖 AllocateEntityId 的确定性分配顺序，先断言假设成立。
            Assert.Equal(new Id("gobj.inst_1"), doorA);
            Assert.Equal(new Id("gobj.inst_2"), doorB);
            Assert.Equal(new Id("gobj.inst_3"), lever);

            world.Host.Interact(Unit, lever);

            Assert.Equal((ExprValue?)ExprValue.OfBool(true), world.Host.GetState(doorA, "open_state"));
            Assert.Equal((ExprValue?)ExprValue.OfBool(true), world.Host.GetState(doorB, "open_state"));
        }

        [Fact]
        public void Interact_Teleporter_SameMap_MovesUnit()
        {
            var targetRef = new Id("map.sample_target");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_tele_a", "teleporter", J.O(("teleport_target_ref", J.S(targetRef.Value)))))
                .Build();
            world.Options.TeleportResolver = _ => (MapId, new Vec2(9, 9));

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_tele_a"), MapId, new Vec2(0, 0));

            var result = world.Host.Interact(Unit, gobjId);
            Assert.True(result.Success);
            Assert.Equal(new Vec2(9, 9), world.Units.GetPosition(Unit));
        }

        [Fact]
        public void Interact_Teleporter_CrossMap_ReturnsDispatchedRefWithoutMoving()
        {
            var targetRef = new Id("map.sample_target2");
            var otherMap = new Id("map.sample_other");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_tele_b", "teleporter", J.O(("teleport_target_ref", J.S(targetRef.Value)))))
                .Build();
            world.Options.TeleportResolver = _ => (otherMap, new Vec2(1, 1));

            var startPos = new Vec2(0, 0);
            world.AddUnit(Unit, startPos);
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_tele_b"), MapId, new Vec2(0, 0));

            var result = world.Host.Interact(Unit, gobjId);
            Assert.True(result.Success);
            Assert.Equal(InteractOutcome.NoAction, result.Outcome);
            Assert.Equal((Id?)targetRef, result.DispatchedRef);
            Assert.Equal(startPos, world.Units.GetPosition(Unit));
        }

        [Fact]
        public void Interact_SavePoint_InvokesSaveRequester()
        {
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_save_a", "save_point", J.O()))
                .Build();

            Id? requested = null;
            world.Options.SaveRequester = uid => requested = uid;

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_save_a"), MapId, new Vec2(0, 0));

            var result = world.Host.Interact(Unit, gobjId);
            Assert.True(result.Success);
            Assert.Equal((Id?)Unit, requested);
        }

        [Fact]
        public void Interact_Sign_ReturnsNoActionSuccessWithoutSideEffects()
        {
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_sign_a", "sign", J.O(("text_key", J.S("l10n.sample_sign.text")))))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_sign_a"), MapId, new Vec2(0, 0));

            var result = world.Host.Interact(Unit, gobjId);
            Assert.True(result.Success);
            Assert.Equal(InteractOutcome.NoAction, result.Outcome);
        }

        [Fact]
        public void Interact_QuestObject_InvokesQuestActionDispatcher()
        {
            var questRef = new Id("quest.sample_action");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_quest_a", "quest_object", J.O(("quest_action_ref", J.S(questRef.Value)))))
                .Build();

            (Id UnitId, Id Ref)? received = null;
            world.Options.QuestActionDispatcher = (uid, r) => received = (uid, r);

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_quest_a"), MapId, new Vec2(0, 0));

            world.Host.Interact(Unit, gobjId);

            Assert.NotNull(received);
            var receivedValue = received!.Value;
            Assert.Equal(Unit, receivedValue.UnitId);
            Assert.Equal(questRef, receivedValue.Ref);
        }

        [Fact]
        public void TriggerTrap_DefaultsCasterToTriggeringUnit()
        {
            var skillId = new Id("skill.sample_trap_effect");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_trap_a", "trap",
                    J.O(("skill_id", J.S(skillId.Value)), ("trigger_shape", J.O(("kind", J.S("circle")), ("radius", J.N(2)))))))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_trap_a"), MapId, new Vec2(0, 0));

            var result = world.Host.TriggerTrap(gobjId, Unit);
            Assert.True(result.Success);

            var call = Assert.Single(world.Skills.CastCalls);
            Assert.Equal(Unit, call.CasterId);
            Assert.Equal(skillId, call.SkillId);
            Assert.Equal(Unit, Assert.Single(call.Targets));
        }

        [Fact]
        public void TriggerTrap_UsesConfiguredTrapCasterId_WhenSet()
        {
            var skillId = new Id("skill.sample_trap_effect2");
            var casterId = new Id("gobj.sample_trap_setter");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_trap_b", "trap",
                    J.O(("skill_id", J.S(skillId.Value)), ("trigger_shape", J.O(("kind", J.S("circle")), ("radius", J.N(1)))))))
                .Build();
            world.Options.TrapCasterId = casterId;

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_trap_b"), MapId, new Vec2(0, 0));

            world.Host.TriggerTrap(gobjId, Unit);

            var call = Assert.Single(world.Skills.CastCalls);
            Assert.Equal(casterId, call.CasterId);
            Assert.Equal(Unit, Assert.Single(call.Targets));
        }

        // -----------------------------------------------------------------
        // on_use 二选一分发
        // -----------------------------------------------------------------

        [Fact]
        public void Interact_OnUseSkill_DispatchesCastSkillWithGobjAsTarget()
        {
            var skillId = new Id("skill.sample_onuse");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_sign_b", "sign", J.O(("text_key", J.S("l10n.sample_sign2.text"))),
                    onUse: J.O(("kind", J.S("skill")), ("ref", J.S(skillId.Value)))))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_sign_b"), MapId, new Vec2(0, 0));

            var result = world.Host.Interact(Unit, gobjId);
            Assert.True(result.Success);
            Assert.Equal(InteractOutcome.Skill, result.Outcome);
            Assert.Equal((Id?)skillId, result.DispatchedRef);

            var call = Assert.Single(world.Skills.CastCalls);
            Assert.Equal(Unit, call.CasterId);
            Assert.Equal(skillId, call.SkillId);
            Assert.Equal(gobjId, Assert.Single(call.Targets));
        }

        [Fact]
        public void Interact_OnUseDialog_InvokesDialogOpener()
        {
            var dialogRef = new Id("dialog.sample_menu");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_sign_c", "sign", J.O(("text_key", J.S("l10n.sample_sign3.text"))),
                    onUse: J.O(("kind", J.S("dialog")), ("ref", J.S(dialogRef.Value)))))
                .Build();

            (Id UnitId, Id Ref)? received = null;
            world.Options.DialogOpener = (uid, r) => received = (uid, r);

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_sign_c"), MapId, new Vec2(0, 0));

            var result = world.Host.Interact(Unit, gobjId);
            Assert.True(result.Success);
            Assert.Equal(InteractOutcome.Dialog, result.Outcome);
            Assert.Equal((Id?)dialogRef, result.DispatchedRef);
            Assert.Equal(Unit, received!.Value.UnitId);
        }

        // -----------------------------------------------------------------
        // Locked / 距离过远 / gobj.interacted 事件
        // -----------------------------------------------------------------

        [Fact]
        public void Interact_LockedGobj_ReturnsLockedOutcome()
        {
            const string lockId = "gobj.lock.sample_locked";
            var itemId = new Id("item.sample_key_locked");
            var world = new GobjWorldBuilder()
                .Lock(LockRow(lockId, J.O(("kind", J.S("item_key")), ("item_id", J.S(itemId.Value)))))
                .Template(Template("gobj.sample_door_i", "door", J.O(), lockId))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_i"), MapId, new Vec2(0, 0));

            var result = world.Host.Interact(Unit, gobjId);
            Assert.False(result.Success);
            Assert.Equal(InteractOutcome.Locked, result.Outcome);
        }

        [Fact]
        public void Interact_TooFar_ReturnsUnknownOutcome()
        {
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_door_j", "door", J.O()))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_j"), MapId, new Vec2(100, 100));

            var result = world.Host.Interact(Unit, gobjId);
            Assert.False(result.Success);
            Assert.Equal(InteractOutcome.Unknown, result.Outcome);
        }

        [Fact]
        public void Interact_Success_EmitsGobjInteractedEvent()
        {
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_door_k", "door", J.O()))
                .Build();

            world.AddUnit(Unit, new Vec2(0, 0));
            var gobjId = world.SpawnFromTemplate(new Id("gobj.sample_door_k"), MapId, new Vec2(0, 0));

            world.Host.Interact(Unit, gobjId);
            world.Flush();

            var evt = Assert.Single(world.Of<GobjInteractedEvent>());
            Assert.Equal(Unit, evt.UnitId);
            Assert.Equal(gobjId, evt.GobjInstanceId);
        }

        // -----------------------------------------------------------------
        // spell_focus 查询
        // -----------------------------------------------------------------

        [Fact]
        public void HasSpellFocus_FindsMatchingFocusWithinRadius()
        {
            var tag = new Id("stat.sample_focus_tag");
            var world = new GobjWorldBuilder()
                .Template(Template("gobj.sample_focus_a", "spell_focus", J.O(("required_skill_tag", J.S(tag.Value)))))
                .Build();

            world.SpawnFromTemplate(new Id("gobj.sample_focus_a"), MapId, new Vec2(5, 5));

            Assert.True(world.Host.HasSpellFocus(new Vec2(5, 6), tag, 2));
            Assert.False(world.Host.HasSpellFocus(new Vec2(5, 6), tag, 0.5));
            Assert.False(world.Host.HasSpellFocus(new Vec2(5, 6), new Id("stat.sample_other_tag"), 2));
        }
    }
}
