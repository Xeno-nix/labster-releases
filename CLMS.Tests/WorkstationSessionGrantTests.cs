using CLMS.Web.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace CLMS.Tests;

public sealed class WorkstationSessionGrantTests
{
    [Fact]
    public void GrantBindsVerifiedHumanToOneWorkstationAndSession()
    {
        var clock = new TestClock();
        var grants = new WorkstationSessionGrants(new EphemeralDataProtectionProvider(), clock);
        var workstation = Guid.NewGuid();
        var session = Guid.NewGuid();
        var user = Guid.NewGuid();
        var grant = grants.Issue(workstation, session, user, "current-stamp");

        Assert.Equal(new WorkstationSessionIdentity(user, "current-stamp"), grants.Read(grant, workstation, session));
        Assert.Null(grants.Read(grant, Guid.NewGuid(), session));
        Assert.Null(grants.Read(grant, workstation, Guid.NewGuid()));
        Assert.Null(grants.Read(grant[1..], workstation, session));

        clock.Now += TimeSpan.FromMinutes(2);
        Assert.Null(grants.Read(grant, workstation, session));
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
