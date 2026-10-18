using System;
using RvtMcp.Plugin.Views.Toast;
using Xunit;

namespace RvtMcp.Tests
{
    public sealed class ToastFaultGuardTests
    {
        [Fact]
        public void Faults_outside_toast_work_are_left_to_their_owner()
        {
            var guard = new ToastFaultGuard();

            Assert.False(guard.TryClaim());
            Assert.False(guard.IsBroken);
        }

        [Fact]
        public void A_fault_inside_toast_work_is_claimed_and_disables_the_toast()
        {
            var guard = new ToastFaultGuard();
            var claimed = false;

            guard.Run(() => claimed = guard.TryClaim());

            Assert.True(claimed);
            Assert.True(guard.IsBroken);
            Assert.False(guard.InToastWork);
            // Once the work has unwound, a later fault belongs to someone else again.
            Assert.False(guard.TryClaim());
        }

        [Fact]
        public void Nested_work_stays_claimable_until_the_outer_work_ends()
        {
            var guard = new ToastFaultGuard();
            var claimedAfterInner = false;

            guard.Run(() =>
            {
                guard.Run(() => { });
                claimedAfterInner = guard.TryClaim();
            });

            Assert.True(claimedAfterInner);
            Assert.False(guard.InToastWork);
        }

        [Fact]
        public void Depth_is_restored_when_toast_work_throws()
        {
            var guard = new ToastFaultGuard();

            Assert.Throws<InvalidOperationException>(() => guard.Run(() => throw new InvalidOperationException()));

            Assert.False(guard.InToastWork);
            Assert.False(guard.TryClaim());
            Assert.False(guard.IsBroken);
        }
    }
}
