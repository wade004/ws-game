using System;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Xunit;

namespace Tests.Carriers.Common
{
    public class ResultTypesTests
    {
        [Fact]
        public void EquipResult_Ok_HasNoneReason()
        {
            var result = EquipResult.Ok();

            Assert.True(result.Success);
            Assert.Equal(EquipFailureReason.None, result.Reason);
            Assert.Null(result.Replaced);
        }

        [Fact]
        public void EquipResult_Ok_CanCarryReplacedItem()
        {
            var replaced = new ItemInstanceRef(new Id("item.inst_old"));

            var result = EquipResult.Ok(replaced);

            Assert.True(result.Success);
            Assert.Equal(replaced, result.Replaced);
        }

        [Fact]
        public void EquipResult_Fail_RequiresNonNoneReason()
        {
            Assert.Throws<ArgumentException>(() => EquipResult.Fail(EquipFailureReason.None));
        }

        [Fact]
        public void EquipResult_Fail_CarriesReasonAndNoReplacement()
        {
            var result = EquipResult.Fail(EquipFailureReason.SlotOccupied);

            Assert.False(result.Success);
            Assert.Equal(EquipFailureReason.SlotOccupied, result.Reason);
            Assert.Null(result.Replaced);
        }

        [Fact]
        public void InteractResult_CarriesOutcomeAndDispatchedRef()
        {
            var skillId = new Id("skill.open_lock");

            var result = new InteractResult(true, InteractOutcome.Skill, skillId);

            Assert.True(result.Success);
            Assert.Equal(InteractOutcome.Skill, result.Outcome);
            Assert.Equal(skillId, result.DispatchedRef);
        }

        [Fact]
        public void InteractResult_LockedOutcome_HasNoDispatchedRefByDefault()
        {
            var result = new InteractResult(false, InteractOutcome.Locked);

            Assert.False(result.Success);
            Assert.Null(result.DispatchedRef);
        }
    }
}
