using System.Net;
using System.Net.Mail;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces;
using CLMS.Application.Identity.Exceptions;
using CLMS.Application.Persistence.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Domain.ValueObjects;
using CLMS.Shared.Contracts;
using CLMS.Shared.Dtos;

namespace CLMS.Application.Identity.Services;

public sealed class AuthenticationService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IRefreshTokenHasher refreshTokenHasher,
    IRefreshTokenRepository refreshTokenRepository,
    IExternalIdentityRepository externalIdentityRepository,
    ICurrentUser currentUser,
    IEmailService emailService,
    AccountOptions options,
    IAccountSecurityCrypto crypto,
    EmailLoginService emailLogin)
{
    private const int MaxSessions = 5;
        
    private const int MaxFailedLoginAttempts = 5;
    // start warning the user once this many attempts (or fewer) remain
    private const int LockoutWarningThreshold = 3;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    
    /// <summary>Creates the account immediately. Email failure is a separate outcome, not a registration rollback.</summary>
    public async Task<RegistrationResultDto> RegisterAsync(RegisterDto request)
    {
        // await currentUser.RequireAdminAsync();
        var email = NormalizeEmail(request.Email);
        if (await userRepository.FindByEmailAsync(email) is not null)
            throw new InvalidOperationException("A user with this email already exists.");
        if (!Enum.IsDefined(request.UserRole) || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
            throw new InvalidOperationException("Provide a valid role and a password of at least 8 characters.");
        if (!Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttps && (options.RequireHttps || url.Scheme != Uri.UriSchemeHttp)) ||
            !MailAddress.TryCreate(options.SupportEmail, out _))
            throw new InvalidOperationException(options.RequireHttps
                ? "Configure the public HTTPS URL and support email before registering users."
                : "Configure a public HTTP or HTTPS URL and support email before registering users.");

        User user;
        try
        {
            user = new User(
                Guid.NewGuid(), 
                new Name(request.FirstName.Trim()), 
                new Name(request.LastName.Trim()),
                string.IsNullOrWhiteSpace(request.MiddleName) ? null : new Name(request.MiddleName.Trim()),
                string.IsNullOrWhiteSpace(request.Suffix) ? null : new Name(request.Suffix.Trim()),
                email, 
                request.UserRole, 
                passwordHasher.Hash(request.Password)) { PasswordChanged = false };
        }
        catch (ArgumentException ex) { throw new InvalidOperationException(ex.Message); }
        if (!await userRepository.AddAsync(user)) throw new InvalidOperationException("The account could not be created.");

        // The plaintext password exists only in the submitted request and this email, never in database storage or logs.
        var body = $"<h1>Your Labster account is ready</h1><p>Hello {Encode(user.GetFullName())},</p>" +
            $"<p>Your administrator registered your account.</p><p>Email: <strong>{Encode(email)}</strong><br>" +
            $"Initial password: <strong>{Encode(request.Password)}</strong></p>" +
            "<p><strong>You must change this password when you first sign in.</strong> Do not share your credentials.</p>" +
            $"<p><a href=\"{Encode(options.PublicUrl.TrimEnd('/') + "/login")}\">Sign in to Labster</a></p>" +
            $"<p>Need help? Contact <a href=\"mailto:{Encode(options.SupportEmail)}\">{Encode(options.SupportEmail)}</a>.</p>";
        try
        {
            await emailService.SendEmailAsync(email, "Your Labster account credentials", body, CancellationToken.None);
            return new(user.Id, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            // The administrator must see that the account already exists. Retrying registration would create a duplicate.
            return new(user.Id, false);
        }
    }

    public async Task<UserClaimsDto> AuthenticateUserAsync(AuthenticationRequestDto request)
    {
        var user = await userRepository.FindByEmailAsync(NormalizeEmail(request.Email));
        
        // if user not found using email
        if (user is null || user.DeletedAt.HasValue) throw new InvalidCredentialsException();

        // refuse while locked, even if the password is correct
        if (user.IsLockedOut) throw new AccountLockedException(user.LockoutEnd!.Value);

        // checks if the password is correct for the user
        var passwordValid =
            passwordHasher.Verify(
                request.Password,
                user.PasswordHash);

        // invalid password
        if (!passwordValid)
        {
            // counted in the database (not from the copy read above) so concurrent attempts aren't lost
            var failedAttempts = await userRepository.IncrementFailedLoginAttemptsAsync(user.Id);

            if (failedAttempts >= MaxFailedLoginAttempts)
            {
                var lockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
                await userRepository.LockOutAsync(user.Id, lockoutEnd);
                throw new AccountLockedException(lockoutEnd);
            }

            // only tell the user how many attempts remain once they are close to the limit
            var attemptsRemaining = MaxFailedLoginAttempts - failedAttempts;
            throw new InvalidCredentialsException(
                attemptsRemaining <= LockoutWarningThreshold ? attemptsRemaining : null);
        }

        // success - clear failed attempts and any expired lockout
        if (user.FailedLoginAttempts != 0 || user.LockoutEnd is not null)
            await userRepository.ResetFailedLoginAttemptsAsync(user.Id);
        
        return Claims(user);
    }

    public async Task<AuthTokensDto> JwtLogin(AuthenticationRequestDto request) =>
        await IssueTokens(await AuthenticateUserAsync(request));

    public async Task<EmailLoginReceipt> BeginLoginAsync(AuthenticationRequestDto request, CancellationToken ct = default) =>
        await emailLogin.BeginAsync(await AuthenticateUserAsync(request), ct);

    /// <summary>The caller returns either a completed sign-in or the intermediate email-code response.</summary>
    public async Task<(AuthTokensDto? Tokens, EmailLoginReceipt? Challenge)> LoginAsync(AuthenticationRequestDto request, CancellationToken ct = default)
    {
        var firstFactor = await AuthenticateUserAsync(request);
        if (!firstFactor.TwoFactorEnabled) return (await IssueTokens(firstFactor), null);
        return (null, await emailLogin.BeginAsync(firstFactor, ct));
    }

    public async Task<AuthTokensDto> IssueTokens(UserClaimsDto claims)
    {
        var user = await RequireActiveUser(claims.UserId);
        if ((user.TwoFactorEnabled && !claims.TwoFactorVerified) || !crypto.StampMatches(user, claims.SecurityStamp))
            throw new UnauthorizedAccessException("Complete email verification before signing in.");
        if (!user.PasswordChanged) throw new PasswordChangeRequiredException();
        return await IssueVerifiedAsync(user, null);
    }

    public async Task<AuthTokensDto> Refresh(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 256 || refreshToken.IndexOf('.') != 64)
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");
        var stored = await refreshTokenRepository.FindByHashAsync(HumanRefreshHash(refreshToken));
        if (stored is null || !stored.IsActive) throw new UnauthorizedAccessException("Invalid or expired refresh token.");
        var user = await RequireActiveUser(stored.UserId);
        if (!crypto.StampMatches(user, refreshToken[..64])) throw new UnauthorizedAccessException("Sign in again.");
        if (!user.PasswordChanged) throw new PasswordChangeRequiredException();
        return await IssueVerifiedAsync(user, stored.TokenHash);
    }

    public async Task Logout(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken) || refreshToken.Length > 256) return;
        var stored = await refreshTokenRepository.FindByHashAsync(HumanRefreshHash(refreshToken));
        if (stored is not null && !stored.IsRevoked)
            await refreshTokenRepository.RevokeAsync(stored.Id, DateTime.UtcNow);
    }

    /// <summary>Google subjects take precedence. An email match only allows a password-confirmed link prompt.</summary>
    public async Task<ExternalLoginUserDto> AuthenticateExternalUserAsync(ExternalLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateGoogleIdentity(request);
        var identity = await externalIdentityRepository.FindAsync("google", request.ProviderSubject, cancellationToken);
        var user = identity is null
            ? await userRepository.FindByEmailAsync(NormalizeEmail(request.Email))
            : await userRepository.FindByIdAsync(identity.UserId);
        // A linked but disabled/missing user never falls back to a different account with the same email.
        if (user is null || user.DeletedAt.HasValue)
            throw new UnauthorizedAccessException("This Google account is not registered in Labster. Contact your administrator.");
        return new(user.Id, user.Email, user.UserRole, user.PasswordChanged, RequiresLink: identity is null);
    }

    /// <summary>The host supplies Google claims from its protected external cookie, not from form fields.</summary>
    public async Task<ExternalLoginUserDto> LinkGoogleAsync(ExternalLoginRequest request, string password,
        CancellationToken cancellationToken = default)
    {
        var candidate = await AuthenticateExternalUserAsync(request, cancellationToken);
        var user = await RequireActiveUser(candidate.UserId);
        if (string.IsNullOrEmpty(password) || !passwordHasher.Verify(password, user.PasswordHash))
            throw new UnauthorizedAccessException("The Labster password is incorrect.");
        await externalIdentityRepository.LinkGoogleAsync(user.Id, user.Email, request.ProviderSubject, user.PasswordHash, cancellationToken);
        return new(user.Id, user.Email, user.UserRole, user.PasswordChanged);
    }

    /// <summary>Checks current credentials and updates the hash/flag with a concurrency check.</summary>
    public async Task ChangePasswordAsync(Guid userId, ChangePasswordDto request)
    {
        PasswordRecoveryService.ValidatePassword(request.NewPassword, request.ConfirmPassword);
        var user = await RequireActiveUser(userId);
        if (string.IsNullOrEmpty(request.CurrentPassword) || !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new InvalidOperationException("The current password is incorrect.");
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            throw new InvalidOperationException("Choose a password of at least 8 characters.");
        if (request.NewPassword != request.ConfirmPassword)
            throw new InvalidOperationException("The password and confirmation password do not match.");
        if (passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            throw new InvalidOperationException("Choose a password different from your current password.");
        if (!await userRepository.ChangePasswordAsync(user.Id, user.PasswordHash, passwordHasher.Hash(request.NewPassword)))
            throw new InvalidOperationException("The account changed. Sign in again and retry.");
    }

    public async Task<UserClaimsDto> GetActiveUserAsync(Guid userId) => Claims(await RequireActiveUser(userId));

    private async Task<User> RequireActiveUser(Guid id)
    {
        var user = await userRepository.FindByIdAsync(id);
        return user is null || user.DeletedAt.HasValue || user.IsLockedOut ? throw new UnauthorizedAccessException("This account is unavailable.") : user;
    }

    private void ValidateGoogleIdentity(ExternalLoginRequest request)
    {
        var email = NormalizeEmail(request.Email);
        var domain = email[(email.LastIndexOf('@') + 1)..];
        Console.WriteLine(!string.Equals(domain, request.HostedDomain, StringComparison.OrdinalIgnoreCase));
        if (
            // Provider is not google
            !string.Equals(request.Provider, "google", StringComparison.OrdinalIgnoreCase) ||
            // Is Empty
            string.IsNullOrWhiteSpace(request.ProviderSubject) ||
            // email is not verified
            !request.EmailVerified ||
            // TODO : Hosted Domain in validator
            // !string.Equals(domain, request.HostedDomain, StringComparison.OrdinalIgnoreCase) ||
            // allowed workspaces : usep.edu.ph && gmail.com
            !options.AllowedWorkspaceDomains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Use a verified organization Google Workspace account to sign in.");
    }

    private UserClaimsDto Claims(User user) => new(user.Id, user.Email, user.UserRole, user.PasswordChanged)
        { SecurityStamp = crypto.Stamp(user), TwoFactorEnabled = user.TwoFactorEnabled };

    private string HumanRefreshHash(string token) => "human2fa:v1:" + refreshTokenHasher.Hash(token);

    private async Task<AuthTokensDto> IssueVerifiedAsync(User user, string? predecessorHash)
    {
        var refreshToken = crypto.Stamp(user) + "." + tokenService.GenerateRefreshToken();
        var replacement = RefreshToken.Create(user.Id, HumanRefreshHash(refreshToken), DateTime.UtcNow.Add(tokenService.RefreshTokenLifetime));
        if (!await refreshTokenRepository.TryIssueAsync(user, replacement, predecessorHash, MaxSessions))
            throw new UnauthorizedAccessException("The session or account changed. Sign in again.");
        return new(tokenService.GenerateAccessToken(Claims(user)), refreshToken);
    }
    private static string NormalizeEmail(string email)
    {
        var trimmed = email?.Trim() ?? "";
        if (!MailAddress.TryCreate(trimmed, out var parsed) || parsed.Address != trimmed || trimmed.Length > 254)
            throw new InvalidOperationException("Enter a valid email address.");
        return trimmed.ToLowerInvariant();
    }
    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
