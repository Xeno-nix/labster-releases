using System.Globalization;
using System.Xml.Linq;

namespace CLMS.Agent;

public sealed record UnitAccessEvent(string Event, DateTimeOffset OccurredUtc)
{
    public string Format(string workstation, string laboratory)
    {
        static string Label(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
        var time = OccurredUtc.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd hh:mm:ss tt", CultureInfo.InvariantCulture);
        return $"[{time} PH (UTC+8)] Workstation: {Label(workstation)} | Laboratory: {Label(laboratory)} | Event: {Event}";
    }

    public static UnitAccessEvent? FromSecurityXml(string xml)
    {
        XNamespace ns = "http://schemas.microsoft.com/win/2004/08/events/event";
        var root = XElement.Parse(xml);
        var system = root.Element(ns + "System");
        if (system?.Element(ns + "EventID")?.Value != "4624") return null;
        var data = root.Element(ns + "EventData")?.Elements(ns + "Data")
            .ToDictionary(x => (string)x.Attribute("Name")!, x => x.Value);
        if (data is null) return null;
        var user = data.GetValueOrDefault("TargetUserName", "");
        var sid = data.GetValueOrDefault("TargetUserSid", "");
        if (string.IsNullOrWhiteSpace(user) || user.EndsWith('$') ||
            user.StartsWith("DWM-", StringComparison.OrdinalIgnoreCase) ||
            user.StartsWith("UMFD-", StringComparison.OrdinalIgnoreCase) ||
            sid is "S-1-5-18" or "S-1-5-19" or "S-1-5-20" or "S-1-5-7") return null;
        var kind = data.GetValueOrDefault("LogonType") switch
        {
            "2" or "10" or "11" or "12" => "Sign-in",
            "7" or "13" => "Unlock",
            _ => null
        };
        var timestamp = (string?)system.Element(ns + "TimeCreated")?.Attribute("SystemTime");
        return kind is not null && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var time) ? new(kind, time.ToUniversalTime()) : null;
    }
}
