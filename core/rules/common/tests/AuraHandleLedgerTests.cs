using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    /// <summary>
    /// T-M18（测试覆盖梳理 2026-10-01）：<see cref="AuraHandleLedger"/> 三个此前只经装备/种族光环用例
    /// 间接覆盖的分支——<c>Release</c> 对未登记键、<c>Forget</c>、<c>InstanceReplaced</c> 迁移合并到已存在键。
    /// 账本不公开计数，断言一律经 <see cref="IEffectSink.RemoveAura"/> 的调用记录观测：引用计数归零
    /// （或键不存在）时才会真正调用一次。
    /// </summary>
    public class AuraHandleLedgerTests
    {
        private static readonly Id Unit = new Id("unit.ledger_a");
        private static readonly Id OtherUnit = new Id("unit.ledger_b");
        private static readonly Id DefId = new Id("aura.ledger_def");
        private static readonly Id Handle1 = new Id("aura.instance.ledger_1");
        private static readonly Id Handle2 = new Id("aura.instance.ledger_2");

        private sealed class RecordingEffectSink : IEffectSink
        {
            public readonly List<(Id Target, Id Instance)> Removed = new List<(Id, Id)>();

            public ResolveResult ApplyEffect(EffectContext context) =>
                throw new NotSupportedException("本用例不应施加效果");

            public AuraInstanceRef ApplyAura(Id targetId, Id auraDefId, Id sourceId, double? durationOverride = null) =>
                throw new NotSupportedException("本用例不应施加光环");

            public void RemoveAura(Id targetId, AuraInstanceRef auraInstanceRef) =>
                Removed.Add((targetId, auraInstanceRef.AuraInstanceId));
        }

        private sealed class FakeAuraQuery : IAuraQuery
        {
            public event Action<Id, Id, Id, Id>? InstanceReplaced;

            public int SubscriberCount => InstanceReplaced?.GetInvocationList().Length ?? 0;

            public void Replace(Id target, Id def, Id oldInstance, Id newInstance) =>
                InstanceReplaced?.Invoke(target, def, oldInstance, newInstance);

            public bool HasAura(Id unitId, Id auraDefId) => false;
            public int GetStacks(Id unitId, Id auraDefId) => 0;
            public ControlFlags GetControlFlags(Id unitId) => default;
            public bool IsImmune(Id unitId, Id school, EffectKind kind) => false;
            public double ConsumeAbsorb(Id unitId, Id school, double amount) => 0.0;
            public IReadOnlyList<Id> GetActiveAuraDefs(Id unitId) => Array.Empty<Id>();
        }

        private static (AuraHandleLedger Ledger, RecordingEffectSink Sink, FakeAuraQuery Query) Build()
        {
            var sink = new RecordingEffectSink();
            var query = new FakeAuraQuery();
            return (new AuraHandleLedger(sink, query), sink, query);
        }

        // ---- 构造 -------------------------------------------------------------------------------

        [Fact]
        public void Constructor_NullEffectSink_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AuraHandleLedger(null!));
        }

        [Fact]
        public void Constructor_SubscribesToInstanceReplacedOnlyWhenQueryGiven()
        {
            var query = new FakeAuraQuery();

            _ = new AuraHandleLedger(new RecordingEffectSink(), query);
            Assert.Equal(1, query.SubscriberCount);

            // auraQuery 省略：不订阅、不抛异常，未触发换句柄的计数行为不受影响。
            var sink = new RecordingEffectSink();
            var ledger = new AuraHandleLedger(sink);
            ledger.Register(Unit, Handle1);
            ledger.Release(Unit, new AuraInstanceRef(Handle1));
            Assert.Equal((Unit, Handle1), Assert.Single(sink.Removed));
        }

        // ---- 基线：共享引用计数 ----------------------------------------------------------------

        [Fact]
        public void Release_SharedHandle_RemovesOnlyWhenLastReferenceReleased()
        {
            var (ledger, sink, _) = Build();
            const int holders = 3;
            for (var i = 0; i < holders; i++) ledger.Register(Unit, Handle1);

            for (var i = 0; i < holders - 1; i++)
            {
                ledger.Release(Unit, new AuraInstanceRef(Handle1));
                Assert.Empty(sink.Removed);
            }

            ledger.Release(Unit, new AuraInstanceRef(Handle1));
            Assert.Equal((Unit, Handle1), Assert.Single(sink.Removed));
        }

        // ---- Release 对未登记键 ----------------------------------------------------------------

        [Fact]
        public void Release_UnregisteredKey_RemovesAuraImmediately_AndDoesNotGoNegative()
        {
            var (ledger, sink, _) = Build();

            ledger.Release(Unit, new AuraInstanceRef(Handle1));

            Assert.Equal((Unit, Handle1), Assert.Single(sink.Removed));

            // 未登记键不留下负计数：随后正常 Register 一次再 Release 一次，仍是"一个引用、一次摘除"。
            ledger.Register(Unit, Handle1);
            ledger.Release(Unit, new AuraInstanceRef(Handle1));
            Assert.Equal(2, sink.Removed.Count);
        }

        [Fact]
        public void Release_KeyIsPerUnit_OtherUnitsReferenceIsUnaffected()
        {
            var (ledger, sink, _) = Build();
            ledger.Register(Unit, Handle1);
            ledger.Register(OtherUnit, Handle1);

            ledger.Release(Unit, new AuraInstanceRef(Handle1));

            Assert.Equal((Unit, Handle1), Assert.Single(sink.Removed));
            // OtherUnit 的引用仍登记着：再释放一次才摘除它自己的实例，且只摘这一次。
            ledger.Release(OtherUnit, new AuraInstanceRef(Handle1));
            Assert.Equal(2, sink.Removed.Count);
            Assert.Equal((OtherUnit, Handle1), sink.Removed[1]);
        }

        // ---- Forget ---------------------------------------------------------------------------

        [Fact]
        public void Forget_DropsCountWithoutCallingRemoveAura()
        {
            var (ledger, sink, _) = Build();
            ledger.Register(Unit, Handle1);
            ledger.Register(Unit, Handle1);

            ledger.Forget(Unit, Handle1);

            Assert.Empty(sink.Removed);

            // 计数确实被丢弃：之后 Release 走"未登记键"分支，立刻摘除一次；
            // 若计数还在（2），第一次 Release 只会递减而不摘除。
            ledger.Release(Unit, new AuraInstanceRef(Handle1));
            Assert.Equal((Unit, Handle1), Assert.Single(sink.Removed));
        }

        [Fact]
        public void Forget_UnknownKey_IsNoOp_AndOnlyAffectsTheNamedKey()
        {
            var (ledger, sink, _) = Build();
            ledger.Register(Unit, Handle1);
            ledger.Register(Unit, Handle1);

            ledger.Forget(Unit, Handle2);        // 不存在的句柄
            ledger.Forget(OtherUnit, Handle1);   // 同句柄、别的单位

            Assert.Empty(sink.Removed);
            // Unit/Handle1 的两份引用仍在：第一次 Release 只递减。
            ledger.Release(Unit, new AuraInstanceRef(Handle1));
            Assert.Empty(sink.Removed);
            ledger.Release(Unit, new AuraInstanceRef(Handle1));
            Assert.Equal((Unit, Handle1), Assert.Single(sink.Removed));
        }

        // ---- InstanceReplaced 迁移 -------------------------------------------------------------

        [Fact]
        public void InstanceReplaced_MigratesCountToNewHandle_OldHandleNoLongerCounted()
        {
            var (ledger, sink, query) = Build();
            ledger.Register(Unit, Handle1);
            ledger.Register(Unit, Handle1);

            query.Replace(Unit, DefId, Handle1, Handle2);

            // 新句柄名下带着 2 份引用：第一次 Release 只递减，第二次才摘除。
            ledger.Release(Unit, new AuraInstanceRef(Handle2));
            Assert.Empty(sink.Removed);
            ledger.Release(Unit, new AuraInstanceRef(Handle2));
            Assert.Equal((Unit, Handle2), Assert.Single(sink.Removed));

            // 旧句柄名下已无计数：Release 走未登记键分支立即摘除。
            ledger.Release(Unit, new AuraInstanceRef(Handle1));
            Assert.Equal(2, sink.Removed.Count);
            Assert.Equal((Unit, Handle1), sink.Removed[1]);
        }

        [Fact]
        public void InstanceReplaced_NewHandleAlreadyRegistered_MergesCounts()
        {
            var (ledger, sink, query) = Build();
            const int oldRefs = 2;
            const int existingNewRefs = 1;
            for (var i = 0; i < oldRefs; i++) ledger.Register(Unit, Handle1);
            for (var i = 0; i < existingNewRefs; i++) ledger.Register(Unit, Handle2);

            query.Replace(Unit, DefId, Handle1, Handle2);

            // 合并后新句柄共 oldRefs + existingNewRefs 份引用，只有释放满这个数才摘除，且只摘一次。
            var merged = oldRefs + existingNewRefs;
            for (var i = 0; i < merged - 1; i++)
            {
                ledger.Release(Unit, new AuraInstanceRef(Handle2));
                Assert.Empty(sink.Removed);
            }

            ledger.Release(Unit, new AuraInstanceRef(Handle2));
            Assert.Equal((Unit, Handle2), Assert.Single(sink.Removed));
        }

        [Fact]
        public void InstanceReplaced_OldHandleNotTracked_DoesNotCreateNewKey_AndOtherUnitsUntouched()
        {
            var (ledger, sink, query) = Build();
            ledger.Register(OtherUnit, Handle1); // 同句柄 id、不同单位：不应被迁移

            query.Replace(Unit, DefId, Handle1, Handle2);

            // Unit 名下没有 Handle1 的登记：未迁移出任何新键，Handle2 仍是"未登记键"（立即摘除）。
            ledger.Release(Unit, new AuraInstanceRef(Handle2));
            Assert.Equal((Unit, Handle2), Assert.Single(sink.Removed));

            // OtherUnit 的 Handle1 引用原样保留。
            ledger.Release(OtherUnit, new AuraInstanceRef(Handle1));
            Assert.Equal(2, sink.Removed.Count);
            Assert.Equal((OtherUnit, Handle1), sink.Removed[1]);
        }
    }
}
