namespace CLMS.Agent.Core;

/// <summary>A sanitized local connection or identity-verification failure safe to show in the UI.</summary>
public sealed class AgentIpcException(string code, string message, int? nativeError = null) : IOException(message)
{
    public string Code { get; } = code;
    public int? NativeError { get; } = nativeError;
}
