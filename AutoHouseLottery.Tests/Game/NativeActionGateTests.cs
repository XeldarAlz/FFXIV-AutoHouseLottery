using AutoHouseLottery.Core.Game.Model;
using System.Threading;

namespace AutoHouseLottery.Tests.Game;

public sealed class NativeActionGateTests
{
    [Fact]
    public void NothingIsPermittedByDefault()
    {
        Assert.False(NativeActionGate.IsPermitted(NativeAction.ConfirmEntry));
        Assert.False(NativeActionGate.IsPermitted(NativeAction.AcceptResult));
        Assert.False(NativeActionGate.IsPermitted(NativeAction.None));
    }

    [Fact]
    public void APermitCoversOnlyItsActionAndOnlyInsideItsBlock()
    {
        using (NativeActionGate.Allow(NativeAction.CancelEntry))
        {
            Assert.True(NativeActionGate.IsPermitted(NativeAction.CancelEntry));
            Assert.False(NativeActionGate.IsPermitted(NativeAction.ConfirmEntry));
            Assert.False(NativeActionGate.IsPermitted(NativeAction.CancelEntry | NativeAction.ConfirmEntry));
        }

        Assert.False(NativeActionGate.IsPermitted(NativeAction.CancelEntry));
    }

    [Fact]
    public void NestedPermitsUnwindInOrder()
    {
        using (NativeActionGate.Allow(NativeAction.PressEnterLottery))
        {
            using (NativeActionGate.Allow(NativeAction.PressEnterLottery | NativeAction.CancelEntry))
            {
                Assert.True(NativeActionGate.IsPermitted(NativeAction.CancelEntry));
            }

            Assert.True(NativeActionGate.IsPermitted(NativeAction.PressEnterLottery));
            Assert.False(NativeActionGate.IsPermitted(NativeAction.CancelEntry));
        }

        Assert.False(NativeActionGate.IsPermitted(NativeAction.PressEnterLottery));
    }

    [Fact]
    public void APermitIsInvisibleToAnotherThread()
    {
        var seenElsewhere = true;
        using (NativeActionGate.Allow(NativeAction.DeclineResult))
        {
            var other = new Thread(() => seenElsewhere = NativeActionGate.IsPermitted(NativeAction.DeclineResult));
            other.Start();
            other.Join();
            Assert.True(NativeActionGate.IsPermitted(NativeAction.DeclineResult));
        }

        Assert.False(seenElsewhere);
    }
}
