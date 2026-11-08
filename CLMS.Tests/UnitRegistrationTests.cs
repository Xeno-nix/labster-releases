using CLMS.Domain.Entities.UnitManagement;
using Xunit;

namespace CLMS.Tests;

public sealed class UnitRegistrationTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void DeadlineIsInclusiveAndTerminalDecisionsStayTerminal(int ticks, bool expires)
    {
        var deadline = new DateTime(2026, 11, 8, 0, 10, 0, DateTimeKind.Utc);
        var request = new UnitRegistration { ExpiresAtUtc = deadline };
        Assert.Equal(expires, request.Expire(deadline.AddTicks(ticks)));
        if (expires) Assert.Throws<InvalidOperationException>(() => request.Decide(true, Guid.NewGuid(), deadline));
        else
        {
            request.Decide(true, Guid.NewGuid(), deadline.AddTicks(-1));
            Assert.False(request.Expire(deadline.AddDays(1)));
            Assert.Equal(RegistrationStatus.Approved, request.Status);
            Assert.Throws<InvalidOperationException>(() => request.Decide(false, Guid.NewGuid(), deadline));
        }
    }
}
