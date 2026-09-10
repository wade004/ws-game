using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Gameplay.WorldState;
using Xunit;

namespace Tests.Gameplay.WorldState
{
    public class WorldStateTests
    {
        private static readonly Id BridgeFlag = new Id("world.bridge.repaired");
        private static readonly Id ChestFlag = new Id("world.gobj.chest_01.open_state");
        private static readonly Id WriterQuest = new Id("quest.deliver_letter");

        // -------------------------------------------------------------
        // 基本读写
        // -------------------------------------------------------------

        [Fact]
        public void Set_Then_Get_Has_ReturnsWrittenValue()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());

            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);

            Assert.True(state.Has(BridgeFlag));
            Assert.Equal(ExprValue.OfBool(true), state.Get(BridgeFlag));
        }

        [Fact]
        public void Get_MissingFlag_ReturnsFalse_AndHasIsFalse()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());

            Assert.False(state.Has(BridgeFlag));
            Assert.Equal(ExprValue.OfBool(false), state.Get(BridgeFlag));
        }

        [Fact]
        public void Remove_ExistingFlag_ReturnsTrue_AndClearsIt()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            state.Set(BridgeFlag, ExprValue.OfInt(3), WriterQuest);

            var removed = state.Remove(BridgeFlag, WriterQuest);

            Assert.True(removed);
            Assert.False(state.Has(BridgeFlag));
        }

        [Fact]
        public void Remove_MissingFlag_ReturnsFalse()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());

            Assert.False(state.Remove(BridgeFlag, WriterQuest));
        }

        // -------------------------------------------------------------
        // 事件：flagKey/oldValue/newValue/writerId + 同值不发事件
        // -------------------------------------------------------------

        [Fact]
        public void Set_ChangedValue_PublishesFlagChangedEvent_WithCorrectFields()
        {
            var eventBus = TestSupport.NewEventBus();
            var state = new Core.Gameplay.WorldState.WorldState(eventBus);
            WorldFlagChangedEvent? captured = null;
            eventBus.Subscribe<WorldFlagChangedEvent>(WorldStateEventKeys.FlagChanged, evt => captured = evt);

            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);

            Assert.NotNull(captured);
            Assert.Equal(BridgeFlag, captured!.FlagKey);
            Assert.Equal(ExprValue.OfBool(false), captured.OldValue);
            Assert.Equal(ExprValue.OfBool(true), captured.NewValue);
            Assert.Equal(WriterQuest, captured.WriterId);
        }

        [Fact]
        public void Set_SameValueTwice_PublishesEventOnlyOnce()
        {
            var eventBus = TestSupport.NewEventBus();
            var state = new Core.Gameplay.WorldState.WorldState(eventBus);
            var dispatchCount = 0;
            eventBus.Subscribe<WorldFlagChangedEvent>(WorldStateEventKeys.FlagChanged, _ => dispatchCount++);

            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);
            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);

            Assert.Equal(1, dispatchCount);
        }

        [Fact]
        public void Remove_AlwaysPublishes_EvenIfValueEqualsMissingDefault()
        {
            var eventBus = TestSupport.NewEventBus();
            var state = new Core.Gameplay.WorldState.WorldState(eventBus);
            state.Set(BridgeFlag, ExprValue.OfBool(false), WriterQuest);
            var dispatchCount = 0;
            eventBus.Subscribe<WorldFlagChangedEvent>(WorldStateEventKeys.FlagChanged, _ => dispatchCount++);

            var removed = state.Remove(BridgeFlag, WriterQuest);

            Assert.True(removed);
            Assert.Equal(1, dispatchCount);
        }

        // -------------------------------------------------------------
        // OnChanged：按 key 过滤、取消订阅、回调异常隔离
        // -------------------------------------------------------------

        [Fact]
        public void OnChanged_OnlyFiresForMatchingFlagKey()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            var bridgeFired = false;
            var chestFired = false;
            state.OnChanged(BridgeFlag, (o, n) => bridgeFired = true);
            state.OnChanged(ChestFlag, (o, n) => chestFired = true);

            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);

            Assert.True(bridgeFired);
            Assert.False(chestFired);
        }

        [Fact]
        public void OnChanged_ReceivesOldAndNewValue()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            state.Set(BridgeFlag, ExprValue.OfInt(1), WriterQuest);
            ExprValue capturedOld = default;
            ExprValue capturedNew = default;
            state.OnChanged(BridgeFlag, (o, n) => { capturedOld = o; capturedNew = n; });

            state.Set(BridgeFlag, ExprValue.OfInt(2), WriterQuest);

            Assert.Equal(ExprValue.OfInt(1), capturedOld);
            Assert.Equal(ExprValue.OfInt(2), capturedNew);
        }

        [Fact]
        public void OnChanged_Unsubscribe_StopsReceivingCallbacks()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            var fireCount = 0;
            var handle = state.OnChanged(BridgeFlag, (o, n) => fireCount++);

            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);
            handle.Dispose();
            state.Set(BridgeFlag, ExprValue.OfBool(false), WriterQuest);

            Assert.Equal(1, fireCount);
        }

        [Fact]
        public void OnChanged_CallbackThrows_IsolatedFromOtherSubscribers_AndRecordsDiagnostic()
        {
            var diagnostics = new InMemoryWorldStateDiagnostics();
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus(), diagnostics: diagnostics);
            var secondFired = false;
            state.OnChanged(BridgeFlag, (o, n) => throw new InvalidOperationException("boom"));
            state.OnChanged(BridgeFlag, (o, n) => secondFired = true);

            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);

            Assert.True(secondFired);
            Assert.Single(diagnostics.Warnings);
        }

        // -------------------------------------------------------------
        // 参数校验
        // -------------------------------------------------------------

        [Fact]
        public void Set_FlagKeyNotUnderWorldDomain_Throws()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());

            Assert.Throws<ArgumentException>(() =>
                state.Set(new Id("quest.deliver_letter"), ExprValue.OfBool(true), WriterQuest));
        }

        [Fact]
        public void Set_DefaultWriterId_Throws()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());

            Assert.Throws<ArgumentException>(() =>
                state.Set(BridgeFlag, ExprValue.OfBool(true), default));
        }

        // -------------------------------------------------------------
        // Keys / KeysUnder：序数排序
        // -------------------------------------------------------------

        [Fact]
        public void Keys_AreOrdinallySorted()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            state.Set(new Id("world.c.flag"), ExprValue.OfBool(true), WriterQuest);
            state.Set(new Id("world.a.flag"), ExprValue.OfBool(true), WriterQuest);
            state.Set(new Id("world.b.flag"), ExprValue.OfBool(true), WriterQuest);

            var keys = state.Keys;

            Assert.Equal(3, keys.Count);
            Assert.Equal("world.a.flag", keys[0].Value);
            Assert.Equal("world.b.flag", keys[1].Value);
            Assert.Equal("world.c.flag", keys[2].Value);
        }

        [Fact]
        public void KeysUnder_ReturnsOnlyMatchingPrefix_Sorted()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            state.Set(new Id("world.gobj.chest_02.open_state"), ExprValue.OfBool(true), WriterQuest);
            state.Set(new Id("world.gobj.chest_01.open_state"), ExprValue.OfBool(true), WriterQuest);
            state.Set(new Id("world.quest_flag"), ExprValue.OfBool(true), WriterQuest);
            state.Set(new Id("world.gobj"), ExprValue.OfBool(true), WriterQuest); // 前缀本身也是一条合法标志

            var underGobj = state.KeysUnder(new Id("world.gobj"));

            Assert.Equal(3, underGobj.Count);
            Assert.Equal("world.gobj", underGobj[0].Value);
            Assert.Equal("world.gobj.chest_01.open_state", underGobj[1].Value);
            Assert.Equal("world.gobj.chest_02.open_state", underGobj[2].Value);
        }

        [Fact]
        public void Count_ReflectsNumberOfSetFlags()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            Assert.Equal(0, state.Count);

            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);
            Assert.Equal(1, state.Count);

            state.Remove(BridgeFlag, WriterQuest);
            Assert.Equal(0, state.Count);
        }

        // -------------------------------------------------------------
        // IWorldFlags（L3 依赖倒置最小子集）
        // -------------------------------------------------------------

        [Fact]
        public void IWorldFlags_Get_ReturnsNull_WhenUnset()
        {
            Core.Carriers.Common.IWorldFlags flags = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());

            Assert.Null(flags.Get(BridgeFlag));
        }

        [Fact]
        public void IWorldFlags_Get_ReturnsValue_WhenSet()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            state.Set(BridgeFlag, ExprValue.OfInt(7), WriterQuest);
            Core.Carriers.Common.IWorldFlags flags = state;

            Assert.Equal(ExprValue.OfInt(7), flags.Get(BridgeFlag));
        }

        // -------------------------------------------------------------
        // IPersistable：五种值类型往返，Id 与 String 区分，Load 不发事件
        // -------------------------------------------------------------

        [Fact]
        public void Save_Load_RoundTrips_AllFiveValueKinds()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            state.Set(new Id("world.flag.bool"), ExprValue.OfBool(true), WriterQuest);
            state.Set(new Id("world.flag.int"), ExprValue.OfInt(42), WriterQuest);
            state.Set(new Id("world.flag.number"), ExprValue.OfNumber(3.5), WriterQuest);
            state.Set(new Id("world.flag.number_whole"), ExprValue.OfNumber(5.0), WriterQuest);
            state.Set(new Id("world.flag.string"), ExprValue.OfString("hello"), WriterQuest);
            state.Set(new Id("world.flag.id"), ExprValue.OfId(new Id("item.iron_sword")), WriterQuest);

            var saved = state.Save();

            var loadedInto = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            loadedInto.Load(saved);

            Assert.Equal(ExprValue.OfBool(true), loadedInto.Get(new Id("world.flag.bool")));
            Assert.Equal(ExprValue.OfInt(42), loadedInto.Get(new Id("world.flag.int")));
            Assert.Equal(ExprValue.OfNumber(3.5), loadedInto.Get(new Id("world.flag.number")));
            Assert.Equal(ExprValueKind.Number, loadedInto.Get(new Id("world.flag.number_whole")).Kind);
            Assert.Equal(5.0, loadedInto.Get(new Id("world.flag.number_whole")).AsNumber);
            Assert.Equal(ExprValue.OfString("hello"), loadedInto.Get(new Id("world.flag.string")));
            Assert.Equal(ExprValue.OfId(new Id("item.iron_sword")), loadedInto.Get(new Id("world.flag.id")));
        }

        /// <summary>V-02 根治验收（第十七方深度审核）：<c>WorldState.ToJson</c> 的 Int 分支改用
        /// <c>JsonNumber.FromInt64</c> 之后，超过 2^53 的整数世界标志必须精确往返。</summary>
        [Fact]
        public void Save_Load_RoundTripsIntFlagExactlyAboveDoublePrecisionBoundary()
        {
            const long hugeInt = 9007199254740993L; // 2^53 + 1
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            state.Set(new Id("world.flag.huge_int"), ExprValue.OfInt(hugeInt), WriterQuest);

            var saved = state.Save();
            var text = JsonWriter.Write(saved);
            Assert.Contains(hugeInt.ToString(System.Globalization.CultureInfo.InvariantCulture), text);

            var loadedInto = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            loadedInto.Load(JsonReader.Parse(text));

            Assert.Equal(ExprValue.OfInt(hugeInt), loadedInto.Get(new Id("world.flag.huge_int")));
        }

        [Fact]
        public void Save_DistinguishesIdFromString_ViaDollarIdWrapper()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            state.Set(new Id("world.flag.id"), ExprValue.OfId(new Id("item.iron_sword")), WriterQuest);

            var saved = (JsonObject)state.Save();
            var idField = (JsonObject)saved[0].Value;

            Assert.True(idField.ContainsKey("$id"));
            Assert.Equal("item.iron_sword", ((JsonString)idField["$id"]).Value);
        }

        [Fact]
        public void Load_DoesNotPublishEvent_OrFireOnChanged()
        {
            var eventBus = TestSupport.NewEventBus();
            var state = new Core.Gameplay.WorldState.WorldState(eventBus);
            var dispatchCount = 0;
            eventBus.Subscribe<WorldFlagChangedEvent>(WorldStateEventKeys.FlagChanged, _ => dispatchCount++);
            var onChangedFired = false;
            state.OnChanged(BridgeFlag, (o, n) => onChangedFired = true);

            var data = new JsonObjectBuilder().Add(BridgeFlag.Value, JsonBool.True).Build();
            state.Load(data);

            Assert.True(state.Has(BridgeFlag));
            Assert.Equal(0, dispatchCount);
            Assert.False(onChangedFired);
        }

        /// <summary>
        /// CORE-170-03 根治（architecture/落地计划/audit-8160178-20260908，P2）：修复前 <see
        /// cref="Core.Gameplay.WorldState.WorldState.Load"/> 在校验 <c>data</c> 形状之前就已经
        /// 无条件 <c>_flags.Clear()</c>——坏 shape（既不是 JsonNull 也不是 JsonObject）会在清空之后
        /// 才抛 <see cref="FormatException"/>，读档前的全部 flag 因此丢失且不可恢复（与
        /// <c>EquipmentPersistable.Load</c> 曾经的同一类缺陷成因相同）。根治后先在临时字典里完整
        /// 解析校验，只有整份数据校验通过才清空并替换，坏 shape 时应保留读档前的全部 flag。</summary>
        [Fact]
        public void Load_BadShape_ThrowsFormatException_AndLeavesExistingFlagsUntouched()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());
            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);
            state.Set(ChestFlag, ExprValue.OfInt(3), WriterQuest);

            var badShape = new JsonString("wrong-shape");
            var ex = Record.Exception(() => state.Load(badShape));

            Assert.IsType<FormatException>(ex);
            Assert.True(state.Has(BridgeFlag), "CORE-170-03 核心断言：坏 shape 读档失败后，既有 flag 不应消失。");
            Assert.Equal(ExprValue.OfBool(true), state.Get(BridgeFlag));
            Assert.True(state.Has(ChestFlag));
            Assert.Equal(ExprValue.OfInt(3), state.Get(ChestFlag));
        }

        [Fact]
        public void SectionKey_MatchesSaveSections_WorldStateFlags()
        {
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus());

            Assert.Equal(Core.Foundation.SaveSystem.SaveSections.WorldStateFlags, state.SectionKey);
        }

        // -------------------------------------------------------------
        // EnforceSchema
        // -------------------------------------------------------------

        [Fact]
        public void EnforceSchema_MatchingPrefixAndKind_Allows()
        {
            var options = new WorldStateOptions
            {
                EnforceSchema = true,
                SchemaEntries = new[] { new WorldFlagSchemaEntry(new Id("world.bridge"), ExprValueKind.Bool) },
            };
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus(), options);

            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);

            Assert.True(state.Has(BridgeFlag));
        }

        [Fact]
        public void EnforceSchema_NoMatchingPrefix_Throws()
        {
            var options = new WorldStateOptions { EnforceSchema = true };
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus(), options);

            Assert.Throws<ArgumentException>(() => state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest));
        }

        [Fact]
        public void EnforceSchema_KindMismatch_Throws()
        {
            var options = new WorldStateOptions
            {
                EnforceSchema = true,
                SchemaEntries = new[] { new WorldFlagSchemaEntry(new Id("world.bridge"), ExprValueKind.Int) },
            };
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus(), options);

            Assert.Throws<ArgumentException>(() => state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest));
        }

        [Fact]
        public void EnforceSchema_PicksMostSpecificPrefix()
        {
            var options = new WorldStateOptions
            {
                EnforceSchema = true,
                SchemaEntries = new[]
                {
                    new WorldFlagSchemaEntry(new Id("world.bridge"), ExprValueKind.Int),
                    new WorldFlagSchemaEntry(new Id("world.bridge.repaired"), ExprValueKind.Bool),
                },
            };
            var state = new Core.Gameplay.WorldState.WorldState(TestSupport.NewEventBus(), options);

            // 命中更具体的 world.bridge.repaired（Bool），而不是更宽泛的 world.bridge（Int）。
            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);

            Assert.True(state.Has(BridgeFlag));
        }

        // -------------------------------------------------------------
        // DispatchMode.Enqueue：延后到 DispatchPending
        // -------------------------------------------------------------

        [Fact]
        public void DispatchMode_Enqueue_DefersEventAndOnChanged_UntilDispatchPending()
        {
            var eventBus = TestSupport.NewEventBus();
            var options = new WorldStateOptions { Mode = WorldStateOptions.DispatchMode.Enqueue };
            var state = new Core.Gameplay.WorldState.WorldState(eventBus, options);
            var dispatchCount = 0;
            eventBus.Subscribe<WorldFlagChangedEvent>(WorldStateEventKeys.FlagChanged, _ => dispatchCount++);
            var onChangedFired = false;
            state.OnChanged(BridgeFlag, (o, n) => onChangedFired = true);

            state.Set(BridgeFlag, ExprValue.OfBool(true), WriterQuest);

            Assert.Equal(0, dispatchCount);
            Assert.False(onChangedFired);

            eventBus.DispatchPending();

            Assert.Equal(1, dispatchCount);
            Assert.True(onChangedFired);
        }
    }
}
