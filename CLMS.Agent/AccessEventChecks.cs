namespace CLMS.Agent;

internal static class AccessEventChecks
{
    /// <summary>Runs deterministic checks for event classification, account filtering, timestamps, and console formatting.</summary>
    public static int Run()
    {
        // Fails the console self-test when a named assertion is not satisfied.
        static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"Check failed: {name}");
        }
        // Builds simulated Windows event XML for the console self-tests.
        static string Xml(int type, string user = "Student", string sid = "S-1-5-21-123-1001") => $"""
            <Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
              <System><EventID>4624</EventID><TimeCreated SystemTime="2026-10-25T17:00:12Z" /></System>
              <EventData><Data Name="LogonType">{type}</Data><Data Name="TargetUserName">{user}</Data><Data Name="TargetUserSid">{sid}</Data></EventData>
            </Event>
            """;
        foreach (var type in new[] { 2, 10, 11, 12 }) Check(UnitAccessEvent.FromSecurityXml(Xml(type))?.Event == "Sign-in", $"sign-in type {type}");
        foreach (var type in new[] { 7, 13 }) Check(UnitAccessEvent.FromSecurityXml(Xml(type))?.Event == "Unlock", $"unlock type {type}");
        foreach (var type in new[] { 3, 4, 5, 8, 9 }) Check(UnitAccessEvent.FromSecurityXml(Xml(type)) is null, $"ignore noninteractive type {type}");
        foreach (var user in new[] { "DWM-1", "UMFD-2", "PC01$" }) Check(UnitAccessEvent.FromSecurityXml(Xml(2, user)) is null, $"ignore system user {user}");
        Check(UnitAccessEvent.FromSecurityXml(Xml(2, "SYSTEM", "S-1-5-18")) is null, "ignore SYSTEM SID");
        var access = UnitAccessEvent.FromSecurityXml(Xml(2))!;
        Check(access.OccurredUtc == DateTimeOffset.Parse("2026-10-25T17:00:12Z"), "preserve recorded time");
        var output = access.Format("LAB1-PC-01", "Computer Lab 1");
        Check(output.Contains("2026-10-26 01:00:12 AM PH (UTC+8)"), "PH date boundary");
        Check(!output.Contains("Student"), "no user identities in output");
        Check(!access.Format("PC\n01", "Lab\r01").Contains('\n'), "one line per event");
        Console.WriteLine("Unit access checks passed (sign-in/unlock classification, system-account filtering, event time, PH formatting).");
        Console.WriteLine("Simulated sample events (not actual workstation activity):");
        Console.WriteLine(new UnitAccessEvent("Startup", access.OccurredUtc.AddMinutes(-1)).Format("LAB1-PC-01", "Computer Lab 1"));
        Console.WriteLine(output);
        Console.WriteLine(new UnitAccessEvent("Unlock", access.OccurredUtc.AddMinutes(15)).Format("LAB1-PC-01", "Computer Lab 1"));
        return 0;
    }
}
