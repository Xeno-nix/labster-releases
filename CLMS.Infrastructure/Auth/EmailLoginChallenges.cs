using System.Security.Cryptography;
using System.Text;
using CLMS.Application.Identity.Interfaces;
using CLMS.Domain.Entities.Identity;
using CLMS.Shared.Dtos;

namespace CLMS.Infrastructure.Auth;

/// <summary>Bounded process-local proof storage. No plaintext code or pending credential is retained.</summary>
public sealed class EmailLoginChallenges(IAccountSecurityCrypto crypto, TimeProvider clock) : IEmailLoginChallenges
{
    private readonly object gate = new();
    private readonly Dictionary<string, Entry> entries = [];
    private readonly Dictionary<Guid, Queue<DateTime>> sends = [];
    private readonly byte[] codeKey = RandomNumberGenerator.GetBytes(32);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private sealed record Entry(UserClaimsDto User, EmailLoginChallenge Challenge)
    {
        // Null identifies a login proof. A Boolean binds a proof to exactly one settings change.
        public bool? TargetEnabled { get; init; }
    }

    public LoginCodeDelivery Create(UserClaimsDto user)
    {
        lock (gate)
        {
            Cleanup();
            if (entries.Count >= 5000) throw new InvalidOperationException("Sign-in is busy. Try again shortly.");
            CheckSendLimit(user.UserId);
            var credential = crypto.GenerateToken();
            var code = crypto.GenerateCode();
            var entry = new Entry(user with { TwoFactorVerified = false }, new(Now, CodeHash(credential, code)));
            entries.Add(crypto.Hash(credential), entry);
            sends[user.UserId].Enqueue(Now);
            return new(Receipt(credential, entry), entry.User, code);
        }
    }

    public LoginCodeDelivery Resend(string credential)
    {
        lock (gate)
        {
            var entry = Require(credential);
            if (entry.TargetEnabled is not null) throw Invalid();
            CheckSendLimit(entry.User.UserId);
            var code = crypto.GenerateCode();
            entry.Challenge.Resend(CodeHash(credential, code), Now);
            sends[entry.User.UserId].Enqueue(Now);
            return new(Receipt(credential, entry), entry.User, code);
        }
    }

    public EmailLoginReceipt Status(string credential)
    {
        lock (gate)
        {
            var entry = Require(credential);
            if (entry.TargetEnabled is not null) throw Invalid();
            return Receipt(credential, entry);
        }
    }
    public UserClaimsDto Consume(string credential, string code)
    {
        lock (gate)
        {
            var entry = Require(credential);
            if (entry.TargetEnabled is not null) throw Invalid();
            var correct = code is { Length: 6 } && code.All(char.IsAsciiDigit) &&
                CryptographicOperations.FixedTimeEquals(Convert.FromHexString(entry.Challenge.CodeHash), Convert.FromHexString(CodeHash(credential, code)));
            if (!entry.Challenge.TryConsume(correct, Now)) throw Invalid();
            entries.Remove(crypto.Hash(credential));
            return entry.User with { TwoFactorVerified = true };
        }
    }
    public void Cancel(string credential) { lock (gate) if (ValidCredential(credential)) entries.Remove(crypto.Hash(credential)); }

    public SettingCodeDelivery CreateSetting(UserClaimsDto user, bool enabled)
    {
        lock (gate)
        {
            // The monitor is reentrant; share login entropy, capacity, and the per-account send budget atomically.
            var delivery = Create(user);
            var key = crypto.Hash(delivery.Receipt.ChallengeCredential);
            entries[key] = entries[key] with { TargetEnabled = enabled };
            return new(delivery, enabled);
        }
    }
    public SettingCodeDelivery ResendSetting(Guid userId, string credential)
    {
        lock (gate)
        {
            var entry = Require(credential);
            if (entry.User.UserId != userId || entry.TargetEnabled is null) throw Invalid();
            CheckSendLimit(userId);
            var code = crypto.GenerateCode();
            entry.Challenge.Resend(CodeHash(credential, code), Now);
            sends[userId].Enqueue(Now);
            return new(new(Receipt(credential, entry), entry.User, code), entry.TargetEnabled.Value);
        }
    }
    public TwoFactorSettingReceipt SettingStatus(Guid userId, string credential)
    {
        lock (gate)
        {
            var entry = Require(credential);
            if (entry.User.UserId != userId || entry.TargetEnabled is null) throw Invalid();
            return new(Receipt(credential, entry), entry.TargetEnabled.Value);
        }
    }
    public VerifiedSettingProof ConsumeSetting(Guid userId, string credential, string code, bool passwordValid)
    {
        lock (gate)
        {
            var entry = Require(credential);
            if (entry.User.UserId != userId || entry.TargetEnabled is null) throw Invalid();
            var correct = code is { Length: 6 } && code.All(char.IsAsciiDigit) &&
                CryptographicOperations.FixedTimeEquals(Convert.FromHexString(entry.Challenge.CodeHash), Convert.FromHexString(CodeHash(credential, code)));
            if (!entry.Challenge.TryConsume(correct && passwordValid, Now)) throw Invalid();
            entries.Remove(crypto.Hash(credential));
            return new(entry.User, entry.TargetEnabled.Value);
        }
    }

    private Entry Require(string credential)
    {
        Cleanup();
        if (!ValidCredential(credential) || !entries.TryGetValue(crypto.Hash(credential), out var entry) || !entry.Challenge.IsActive(Now)) throw Invalid();
        return entry;
    }
    private void CheckSendLimit(Guid id)
    {
        if (!sends.TryGetValue(id, out var history))
        {
            if (sends.Count >= 5000) throw new InvalidOperationException("Sign-in is busy. Try again shortly.");
            sends[id] = history = new();
        }
        if (history.Count >= 5) throw new InvalidOperationException("Too many codes requested. Try again in 15 minutes.");
    }
    private void Cleanup()
    {
        foreach (var pair in entries.Where(x => !x.Value.Challenge.IsActive(Now)).ToArray()) entries.Remove(pair.Key);
        foreach (var pair in sends.ToArray())
        {
            while (pair.Value.TryPeek(out var date) && date <= Now.AddMinutes(-15)) pair.Value.Dequeue();
            if (pair.Value.Count == 0) sends.Remove(pair.Key);
        }
    }
    private string CodeHash(string credential, string code) => Convert.ToHexStringLower(HMACSHA256.HashData(codeKey, Encoding.UTF8.GetBytes(credential + ":" + code)));
    private static EmailLoginReceipt Receipt(string credential, Entry entry) => new(credential, entry.Challenge.ExpiresAt, entry.Challenge.LastSentAt.AddMinutes(1));
    private static bool ValidCredential(string? value) => value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static InvalidOperationException Invalid() => new("The code or sign-in request is invalid, expired, or has too many failed attempts. Sign in again if necessary.");
}
