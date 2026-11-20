using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using CLMS.Application.Identity.Interfaces;
using CLMS.Application.Interfaces;
using CLMS.Infrastructure.Persistence.MySql;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CLMS.Web.Controllers;

[ApiController, Route("api/agent/kiosk"), Authorize(Policy = "AgentOnly"), IgnoreAntiforgeryToken]
[EnableRateLimiting("AgentAuthentication"), RequestSizeLimit(4096)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentKioskController(MySqlConnectionFactory connections, IPasswordHasher passwords,
    IEmailService email) : ControllerBase
{
    public sealed record Credentials([Required, EmailAddress, MaxLength(254)] string Email,
        [Required, MaxLength(128)] string Password, Guid SessionId);
    public sealed record ResetRequest([Required, EmailAddress, MaxLength(254)] string Email);
    public sealed record Change([Required, EmailAddress, MaxLength(254)] string Email,
        [Required, RegularExpression("[0-9a-fA-F]{32}")] string Code,
        [Required, StringLength(128, MinimumLength = 15)] string Password);
    public sealed record SessionRequest(Guid SessionId);

    private string Unit => User.FindFirst("computer_unit_id")!.Value;
    private const string Enrolled = """
        EXISTS (SELECT 1 FROM student_enrollment e
        JOIN class_schedule c ON c.class_schedule_id=e.class_schedule_id
        JOIN academic_terms t ON t.academic_term_id=c.term_id
        WHERE e.student_id=a.student_id AND c.deleted_at IS NULL
          AND DATE(UTC_TIMESTAMP() + INTERVAL 8 HOUR) BETWEEN t.start_date AND t.end_date)
        """;

    [HttpPost("login")]
    public async Task<IActionResult> Login(Credentials request)
    {
        if (request.SessionId == Guid.Empty) return BadRequest();
        await using var db = connections.CreateConnection();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        var address = Normalize(request.Email);
        var account = await db.QuerySingleOrDefaultAsync<Account>(
            "SELECT student_id StudentId,password_hash PasswordHash FROM student_kiosk_accounts a WHERE email=@address AND " + Enrolled + " FOR UPDATE",
            new { address }, tx);
        if (account is null) return Unauthorized();
        if (account.PasswordHash is null)
        {
            if (request.Password != account.StudentId) return Unauthorized();
            await tx.CommitAsync();
            await SendCode(address);
            return Ok(new { changeRequired = true });
        }
        if (!passwords.Verify(request.Password, account.PasswordHash)) return Unauthorized();

        await db.ExecuteScalarAsync<string>("SELECT CAST(computer_unit_id AS CHAR) FROM computer_units WHERE computer_unit_id=@Unit FOR UPDATE", new { Unit }, tx);
        var sessionId = request.SessionId.ToString();
        var active = await db.QuerySingleOrDefaultAsync<ActiveSession>("""
            SELECT CAST(session_id AS CHAR) SessionId,student_id StudentId
            FROM student_kiosk_sessions WHERE unit_id=@Unit AND expires_at>UTC_TIMESTAMP(6) FOR UPDATE
            """, new { Unit }, tx);
        if (active is not null && (active.SessionId != sessionId || active.StudentId != account.StudentId))
            return Conflict();
        if (active is null)
        {
            await db.ExecuteAsync("""
                UPDATE unit_utilization_logs l JOIN student_kiosk_sessions s ON s.log_id=l.unit_utilization_log_id
                SET l.time_out=s.expires_at WHERE s.unit_id=@Unit AND l.time_out IS NULL;
                DELETE FROM student_kiosk_sessions WHERE unit_id=@Unit;
                INSERT INTO unit_utilization_logs(computer_unit_id,student_id,log_date,time_in)
                VALUES(@Unit,@StudentId,DATE(UTC_TIMESTAMP() + INTERVAL 8 HOUR),UTC_TIMESTAMP());
                INSERT INTO student_kiosk_sessions VALUES(@Unit,@sessionId,@StudentId,LAST_INSERT_ID(),UTC_TIMESTAMP(6) + INTERVAL 2 MINUTE);
                """, new { Unit, sessionId, account.StudentId }, tx);
        }
        await tx.CommitAsync();
        return Ok(new { changeRequired = false });
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset(ResetRequest request)
    {
        await SendCode(Normalize(request.Email));
        return NoContent();
    }

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(Change request)
    {
        var address = Normalize(request.Email);
        var code = SHA256.HashData(Convert.FromHexString(request.Code));
        await using var db = connections.CreateConnection();
        var studentId = await db.ExecuteScalarAsync<string>(
            "SELECT student_id FROM student_kiosk_accounts a WHERE email=@address AND code_hash=@code AND code_unit=@Unit AND code_expires>UTC_TIMESTAMP(6) AND " + Enrolled,
            new { address, code, Unit });
        if (studentId is null || request.Password.Contains(studentId, StringComparison.OrdinalIgnoreCase)
            || request.Password.Equals(address, StringComparison.OrdinalIgnoreCase)) return BadRequest();
        var updated = await db.ExecuteAsync("""
            UPDATE student_kiosk_accounts a SET password_hash=@hash,code_hash=NULL,code_unit=NULL,code_expires=NULL
            WHERE email=@address AND code_hash=@code AND code_unit=@Unit AND code_expires>UTC_TIMESTAMP(6) AND
            """ + " " + Enrolled, new { address, code, Unit, hash = passwords.Hash(request.Password) });
        return updated == 1 ? NoContent() : BadRequest();
    }

    [HttpPost("renew")]
    public async Task<IActionResult> Renew(SessionRequest request)
    {
        await using var db = connections.CreateConnection();
        var updated = await db.ExecuteAsync("""
            UPDATE student_kiosk_sessions s JOIN student_kiosk_accounts a ON a.student_id=s.student_id
            SET s.expires_at=UTC_TIMESTAMP(6)+INTERVAL 2 MINUTE
            WHERE s.unit_id=@Unit AND s.session_id=@sessionId AND s.expires_at>UTC_TIMESTAMP(6) AND
            """ + " " + Enrolled, new { Unit, sessionId = request.SessionId.ToString() });
        return updated == 1 ? NoContent() : Unauthorized();
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(SessionRequest request)
    {
        await using var db = connections.CreateConnection();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        await db.ExecuteScalarAsync<string>("SELECT CAST(computer_unit_id AS CHAR) FROM computer_units WHERE computer_unit_id=@Unit FOR UPDATE", new { Unit }, tx);
        await db.ExecuteAsync("""
            UPDATE unit_utilization_logs l JOIN student_kiosk_sessions s ON s.log_id=l.unit_utilization_log_id
            SET l.time_out=LEAST(UTC_TIMESTAMP(6),s.expires_at)
            WHERE s.unit_id=@Unit AND s.session_id=@sessionId AND l.time_out IS NULL;
            DELETE FROM student_kiosk_sessions WHERE unit_id=@Unit AND session_id=@sessionId;
            """, new { Unit, sessionId = request.SessionId.ToString() }, tx);
        await tx.CommitAsync();
        return NoContent();
    }

    private async Task SendCode(string address)
    {
        var secret = RandomNumberGenerator.GetBytes(16);
        var hash = SHA256.HashData(secret);
        await using var db = connections.CreateConnection();
        var changed = await db.ExecuteAsync("""
            UPDATE student_kiosk_accounts a SET code_hash=@hash,code_unit=@Unit,code_expires=UTC_TIMESTAMP(6)+INTERVAL 10 MINUTE
            WHERE email=@address AND (code_expires IS NULL OR code_expires<=UTC_TIMESTAMP(6)+INTERVAL 9 MINUTE) AND
            """ + " " + Enrolled, new { hash, Unit, address });
        if (changed == 0) return;
        try
        {
            await email.SendEmailAsync(address, "CLMS verification code",
                $"Your CLMS verification code is {Convert.ToHexString(secret)}. It expires in 10 minutes.");
        }
        catch
        {
            await db.ExecuteAsync("UPDATE student_kiosk_accounts SET code_expires=NULL WHERE email=@address AND code_hash=@hash", new { address, hash });
            throw new InvalidOperationException("Verification email could not be delivered.");
        }
    }

    private static string Normalize(string address) => address.Trim().ToLowerInvariant();
    private sealed record Account(string StudentId, string? PasswordHash);
    private sealed record ActiveSession(string SessionId, string StudentId);
}
