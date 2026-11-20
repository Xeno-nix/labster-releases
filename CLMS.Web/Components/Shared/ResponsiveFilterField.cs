namespace CLMS.Web.Components.Shared;

public sealed record ResponsiveFilterField(string Label, string DefaultLabel, string DefaultValue, string? Value,
    IEnumerable<(string Value, string Label)> Options)
{
    public string Selection => Value ?? DefaultValue;
}
