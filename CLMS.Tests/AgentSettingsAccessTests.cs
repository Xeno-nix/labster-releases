using System.Security.Claims;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Domain.ValueObjects;
using CLMS.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace CLMS.Tests;

public sealed class AgentSettingsAccessTests
{
    [Theory]
    [InlineData(Role.Admin, 200)]
    [InlineData(Role.SuperAdmin, 200)]
    [InlineData(Role.Faculty, 403)]
    [InlineData(Role.Student, 403)]
    [InlineData(Role.Unassigned, 403)]
    public async Task OnlyCurrentAdministratorsCanOpenSettings(Role role, int status)
    {
        var user = Account(role);
        var controller = Controller(user);
        var result = await controller.Access();
        Assert.Equal(status, result is OkObjectResult ? 200 : Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public async Task DemotionIsReadEvenWhenTokenStillClaimsAdmin()
    {
        var user = Account(Role.Admin);
        var controller = Controller(user);
        Assert.IsType<OkObjectResult>(await controller.Access());
        user.UserRole = Role.Faculty;
        Assert.Equal(403, Assert.IsType<StatusCodeResult>(await controller.Access()).StatusCode);
        user.PasswordChanged = false;
        Assert.IsType<UnauthorizedResult>(await controller.Access());
    }

    [Fact]
    public async Task DevicePrincipalsCannotUnlockHumanSettings()
    {
        var user = Account(Role.Admin);
        var controller = Controller(user);
        controller.HttpContext.User = new(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new("principal_type", "agent")], "test"));
        Assert.IsType<UnauthorizedResult>(await controller.Access());
    }

    private static User Account(Role role) => new(Guid.NewGuid(), new Name("Test"), new Name("Admin"),
        "admin@example.test", role, "test-hash") { PasswordChanged = true };
    private static AgentSettingsController Controller(User user) => new(new EmailLoginTests.TestUsers(user))
    {
        ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new(new ClaimsIdentity([
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Role, "Admin"),
            new("principal_type", "human")], "test")) } }
    };
}
