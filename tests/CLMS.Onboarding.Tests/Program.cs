using CLMS.Application.Identity.Services;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.Enums;
using CLMS.Shared.Contracts;
using CLMS.Shared.Dtos;
using CLMS.Web.Authentication;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

// Focused executable regression suite. No real emails are sent and Google is represented by verified test claims.
var passed = 0;
if (args.Contains("--users-only"))
{
    Console.WriteLine($"PASS: {await UsersTableChecks.Run()} users table regression checks.");
    return;
}
var request = new RegisterDto("Test", "User", null, null, "user@usep.edu.ph", Role.Faculty, "initial-password");
ExternalLoginRequest Google(string email = "user@usep.edu.ph", string subject = "google-sub") => new("google", subject, email, true, "usep.edu.ph");

foreach (var requireHttps in new[] { true, false })
{
    foreach (var publicUrl in new[] { "https://labster.example", "http://localhost:5124", "ftp://localhost/file", "/relative", "invalid", "" })
    {
        await Test($"registration URL policy (RequireHttps={requireHttps}, URL='{publicUrl}')", async () =>
        {
            var f = new Fixture(); f.Options.RequireHttps = requireHttps; f.Options.PublicUrl = publicUrl;
            if (publicUrl.StartsWith("https://") || (!requireHttps && publicUrl.StartsWith("http://")))
            {
                var result = await f.Accounts.RegisterAsync(request);
                Check.That(result.EmailSent && f.Mail.Body!.Contains(publicUrl + "/login"));
            }
            else
            {
                await Check.Reject<InvalidOperationException>(() => f.Accounts.RegisterAsync(request));
                Check.That(f.Users.Items.Count == 0 && f.Mail.Calls == 0);
            }
        });
    }
}
await Test("HTTPS is required by default", () =>
{
    Check.That(new AccountOptions().RequireHttps);
    return Task.CompletedTask;
});
await Test("development still requires a valid support email", async () =>
{
    var f = new Fixture(); f.Options.RequireHttps = false; f.Options.PublicUrl = "http://localhost:5124"; f.Options.SupportEmail = "invalid";
    await Check.Reject<InvalidOperationException>(() => f.Accounts.RegisterAsync(request));
    Check.That(f.Users.Items.Count == 0 && f.Mail.Calls == 0);
});
if (args.Contains("--environment-only"))
{
    passed += await HttpChecks.Run();
    Console.WriteLine($"PASS: {passed} environment and HTTP regression checks.");
    return;
}

passed += await UsersTableChecks.Run();

await Test("registration requires Admin before creating user or sending email", async () =>
{
    var f = new Fixture(); f.Actor.IsAdmin = false;
    await Check.Reject<UnauthorizedAccessException>(() => f.Accounts.RegisterAsync(request));
    Check.That(f.Users.Items.Count == 0 && f.Mail.Calls == 0);
});
await Test("host authorization rechecks stored Admin role, active state and password flag", async () =>
{
    var f = new Fixture(); var admin = Fixture.User(changed: true); admin.UserRole = Role.Admin; f.Users.Items[admin.Id] = admin;
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()), new Claim(ClaimTypes.Role, "Admin")], "test"));
    var actor = new CurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } }, new FixedState(principal), f.Users);
    Check.That(await actor.RequireAdminAsync() == admin.Id);
    admin.UserRole = Role.Faculty; await Check.Reject<UnauthorizedAccessException>(() => actor.RequireAdminAsync());
    admin.UserRole = Role.Admin; admin.PasswordChanged = false; await Check.Reject<UnauthorizedAccessException>(() => actor.RequireAdminAsync());
    admin.PasswordChanged = true; admin.DeletedAt = DateTime.UtcNow; await Check.Reject<UnauthorizedAccessException>(() => actor.RequireAdminAsync());
});
await Test("registration creates usable credentials, marks unchanged, and emails contact/instructions", async () =>
{
    var f = new Fixture(); var result = await f.Accounts.RegisterAsync(request with { Email = " User@USEP.edu.ph " });
    var user = f.Users.Items[result.UserId];
    Check.That(!user.PasswordChanged && user.Email == "user@usep.edu.ph" && user.PasswordHash == "hash:initial-password");
    Check.That(result.EmailSent && f.Mail.Recipient == user.Email && f.Mail.Body!.Contains("initial-password"));
    Check.That(f.Mail.Body!.Contains("must change this password") && f.Mail.Body.Contains("help@example.com"));
    Check.That(f.Refresh.Items.Count == 0);
});
await Test("email failure preserves the created account and reports a separate outcome", async () =>
{
    var f = new Fixture(); f.Mail.Fail = true;
    var result = await f.Accounts.RegisterAsync(request);
    Check.That(!result.EmailSent && f.Users.Items.ContainsKey(result.UserId));
    await Check.Reject<InvalidOperationException>(() => f.Accounts.RegisterAsync(request));
    Check.That(f.Mail.Calls == 1 && f.Users.Items.Count == 1);
});
await Test("email HTML encodes submitted credentials", async () =>
{
    var f = new Fixture(); await f.Accounts.RegisterAsync(request with { Password = "<script>&password" });
    Check.That(f.Mail.Body!.Contains("&lt;script&gt;&amp;password") && !f.Mail.Body.Contains("<script>"));
});
await Test("invalid registration role, email and short password cannot persist", async () =>
{
    var f = new Fixture();
    await Check.Reject<InvalidOperationException>(() => f.Accounts.RegisterAsync(request with { UserRole = (Role)50 }));
    await Check.Reject<InvalidOperationException>(() => f.Accounts.RegisterAsync(request with { Email = "invalid" }));
    await Check.Reject<InvalidOperationException>(() => f.Accounts.RegisterAsync(request with { Password = "short" }));
    Check.That(f.Users.Items.Count == 0 && f.Mail.Calls == 0);
});
await Test("password login reads flag without marking password changed", async () =>
{
    var f = new Fixture(); var user = Fixture.User(); f.Users.Items[user.Id] = user;
    var result = await f.Accounts.AuthenticateUserAsync(new(user.Email, "initial-password"));
    Check.That(!result.PasswordChanged && !user.PasswordChanged);
});
await Test("JWT login and refresh withhold tokens until password change", async () =>
{
    var f = new Fixture(); var user = Fixture.User(); f.Users.Items[user.Id] = user;
    await Check.Reject<PasswordChangeRequiredException>(() => f.Accounts.JwtLogin(new(user.Email, "initial-password")));
    await Check.Reject<PasswordChangeRequiredException>(() => f.Accounts.IssueTokens(new(user.Id, user.Email, user.UserRole, true)));
    var refresh = RefreshToken.Create(user.Id, "hash:refresh", DateTime.UtcNow.AddHours(1)); f.Refresh.Items.Add(refresh);
    await Check.Reject<PasswordChangeRequiredException>(() => f.Accounts.Refresh("refresh"));
    Check.That(f.Refresh.Items.Count == 1 && refresh.IsActive);
});
await Test("password change validates old password, length, confirmation and different value", async () =>
{
    var f = new Fixture(); var user = Fixture.User(); f.Users.Items[user.Id] = user;
    foreach (var change in new[] { new ChangePasswordDto("wrong", "new-password", "new-password"),
        new("initial-password", "short", "short"), new("initial-password", "new-password", "different"),
        new("initial-password", "initial-password", "initial-password") })
        await Check.Reject<InvalidOperationException>(() => f.Accounts.ChangePasswordAsync(user.Id, change));
    Check.That(!user.PasswordChanged && user.PasswordHash == "hash:initial-password");
});
await Test("successful change updates hash/flag, revokes refresh and permits JWT login", async () =>
{
    var f = new Fixture(); var user = Fixture.User(); f.Users.Items[user.Id] = user;
    var oldToken = RefreshToken.Create(user.Id, "hash:old", DateTime.UtcNow.AddHours(1)); f.Refresh.Items.Add(oldToken);
    await f.Accounts.ChangePasswordAsync(user.Id, new("initial-password", "new-password", "new-password"));
    Check.That(user.PasswordChanged && user.PasswordHash == "hash:new-password" && oldToken.IsRevoked);
    await Check.Reject<UnauthorizedAccessException>(() => f.Accounts.AuthenticateUserAsync(new(user.Email, "initial-password")));
    Check.That((await f.Accounts.JwtLogin(new(user.Email, "new-password"))).AccessToken == "access-token");
});
await Test("failed concurrent password update does not report success", async () =>
{
    var f = new Fixture(); var user = Fixture.User(); f.Users.Items[user.Id] = user; f.Users.FailPasswordUpdate = true;
    await Check.Reject<InvalidOperationException>(() => f.Accounts.ChangePasswordAsync(user.Id, new("initial-password", "new-password", "new-password")));
    Check.That(!user.PasswordChanged);
});
await Test("unlinked verified Google email matches prompt candidate without creating a link", async () =>
{
    var f = new Fixture(); var user = Fixture.User(); f.Users.Items[user.Id] = user;
    var result = await f.Accounts.AuthenticateExternalUserAsync(Google(" USER@USEP.EDU.PH "));
    Check.That(result.RequiresLink && !result.PasswordChanged && result.UserId == user.Id && f.Identities.Items.Count == 0);
});
await Test("unknown Google email is not registered; no account is created", async () =>
{
    var f = new Fixture(); await Check.Reject<UnauthorizedAccessException>(() => f.Accounts.AuthenticateExternalUserAsync(Google()));
    Check.That(f.Users.Items.Count == 0 && f.Identities.Items.Count == 0);
});
await Test("unverified Google email and wrong/missing Workspace domain are rejected", async () =>
{
    var f = new Fixture(); var user = Fixture.User(); f.Users.Items[user.Id] = user;
    foreach (var identity in new[] { Google() with { EmailVerified = false }, Google() with { HostedDomain = null },
        Google() with { HostedDomain = "other.edu" }, Google("user@gmail.com") with { HostedDomain = "gmail.com" } })
        await Check.Reject<UnauthorizedAccessException>(() => f.Accounts.AuthenticateExternalUserAsync(identity));
});
await Test("linked Google subject takes precedence over email fallback", async () =>
{
    var f = new Fixture(); var linked = Fixture.User("original@usep.edu.ph", true); var emailMatch = Fixture.User();
    f.Users.Items[linked.Id] = linked; f.Users.Items[emailMatch.Id] = emailMatch;
    f.Identities.Items.Add(new(Guid.NewGuid(), linked.Id, "google", "google-sub"));
    var result = await f.Accounts.AuthenticateExternalUserAsync(Google());
    Check.That(result.UserId == linked.Id && !result.RequiresLink);
    linked.DeletedAt = DateTime.UtcNow;
    await Check.Reject<UnauthorizedAccessException>(() => f.Accounts.AuthenticateExternalUserAsync(Google()));
});
await Test("Google linking requires correct internal password and preserves initial-change requirement", async () =>
{
    var f = new Fixture(); var user = Fixture.User(); f.Users.Items[user.Id] = user;
    await Check.Reject<UnauthorizedAccessException>(() => f.Accounts.LinkGoogleAsync(Google(), "wrong"));
    Check.That(f.Identities.Items.Count == 0);
    var result = await f.Accounts.LinkGoogleAsync(Google(), "initial-password");
    Check.That(!result.RequiresLink && !result.PasswordChanged && f.Identities.Items.Single().UserId == user.Id);
});
await Test("disabled account cannot log in or change password", async () =>
{
    var f = new Fixture(); var user = Fixture.User(); user.DeletedAt = DateTime.UtcNow; f.Users.Items[user.Id] = user;
    await Check.Reject<UnauthorizedAccessException>(() => f.Accounts.AuthenticateUserAsync(new(user.Email, "initial-password")));
    await Check.Reject<UnauthorizedAccessException>(() => f.Accounts.ChangePasswordAsync(user.Id, new("initial-password", "new-password", "new-password")));
});
passed += await HttpChecks.Run();
if (args.Contains("--mysql")) passed += await DatabaseChecks.Run();
Console.WriteLine($"PASS: {passed} account regression checks.");
async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
