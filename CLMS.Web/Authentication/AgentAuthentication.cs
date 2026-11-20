using System.Globalization;
using System.Text;
using CLMS.Application.UnitRegistry;
using CLMS.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace CLMS.Web.Authentication;

public static class AgentAuthentication
{
    /// <summary>Configures the dedicated agent bearer scheme with signing, audience, lifetime, and live credential validation.</summary>
    public static void Configure(JwtBearerOptions options, IConfiguration configuration)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = configuration["Jwt:Issuer"],
            ValidateAudience = true, ValidAudience = configuration["AgentAuth:Audience"] ?? "CLMS.Agent",
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(15),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!)),
            ValidAlgorithms = [AgentTokenService.NormalizeAlgorithm(configuration["Jwt:SigningAlgorithm"] ?? SecurityAlgorithms.HmacSha256)]
        };
        options.Events = new JwtBearerEvents { OnTokenValidated = ValidateAsync };
    }
    /// <summary>Rejects agent principals when validating tokens for the existing human bearer scheme.</summary>
    public static Task RequireHuman(TokenValidatedContext context)
    {
        if (context.Principal?.FindFirst("principal_type")?.Value == "agent") context.Fail("Human principal required.");
        return Task.CompletedTask;
    }
    /// <summary>Registers separate agent-only and administrator-cookie authorization policies.</summary>
    public static void AddPolicies(AuthorizationOptions options)
    {
        options.AddPolicy("AgentOnly", p => p.AddAuthenticationSchemes(AuthenticationSchemes.AgentBearer)
            .RequireAuthenticatedUser().RequireClaim("principal_type", "agent"));
        options.AddPolicy("UnitRegistryAdmin", p => p.AddAuthenticationSchemes(AuthenticationSchemes.Cookie)
            .RequireAuthenticatedUser().RequireRole("Admin"));
    }
    /// <summary>Validates agent identity claims and checks the current unit generation and live persistent credential before authorizing a request.</summary>
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var p = context.Principal!;
        if (p.FindFirst("principal_type")?.Value != "agent" ||
            !Guid.TryParse(p.FindFirst("computer_unit_id")?.Value, out var unit) ||
            !int.TryParse(p.FindFirst("credential_generation")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var generation) ||
            !Guid.TryParse(p.FindFirst("device_credential_id")?.Value, out var credential))
        {
            context.Fail("Invalid agent principal.");
            return;
        }
        if (!await context.HttpContext.RequestServices.GetRequiredService<IUnitRegistryRepository>()
                .IsAuthorizedAsync(unit, generation, credential, context.HttpContext.RequestAborted))
            context.Fail("Device credentials are revoked.");
    }
}
