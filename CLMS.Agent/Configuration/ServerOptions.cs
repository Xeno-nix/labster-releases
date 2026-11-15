namespace CLMS.Agent.Configuration;

public static class ServerOptions
{
    // Matches CLMS.Web's HTTPS launch profile. Other PCs need the server's reachable HTTPS address.
    public const string DefaultAddress = "https://localhost:7190";
}
