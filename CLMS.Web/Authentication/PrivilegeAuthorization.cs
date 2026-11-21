using CLMS.Application.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace CLMS.Web.Authentication;

public sealed record PrivilegeRequirement(string Privilege) : IAuthorizationRequirement;

public sealed class PrivilegeAuthorizationHandler(CurrentUser actor, IAuthorizationRepository privileges)
    : AuthorizationHandler<PrivilegeRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PrivilegeRequirement requirement)
    {
        try
        {
            var current = await actor.ValidateAsync(context.User);
            if (await privileges.HasPrivilegeAsync(current.Id, requirement.Privilege)) context.Succeed(requirement);
            else if (Privileges.IsProtected(requirement.Privilege))
                await privileges.RecordDeniedAsync(current.Id, requirement.Privilege, "Policy");
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Data.Common.DbException) { } // Unavailable authorization data never grants access.
    }
}

public sealed class AuthorizationChanges(ILogger<AuthorizationChanges> logger) : IAuthorizationChanges
{
    public event Action? Changed;
    public void NotifyChanged()
    {
        foreach (var handler in Changed?.GetInvocationList() ?? [])
            try { ((Action)handler)(); } catch (Exception ex) { logger.LogDebug(ex, "An authorization viewer disconnected."); }
    }
}

public static class PrivilegePolicies
{
    public static void Add(AuthorizationOptions options)
    {
        foreach (var privilege in Privileges.All)
            options.AddPolicy(Privileges.Prefix + privilege.Id, policy => policy.RequireAuthenticatedUser().AddRequirements(new PrivilegeRequirement(privilege.Id)));
        options.AddPolicy("RegisterUsers", policy => policy.RequireAuthenticatedUser().AddRequirements(new PrivilegeRequirement(Privileges.UsersCreate), new PrivilegeRequirement(Privileges.UsersAssignRoles)));
    }
}
