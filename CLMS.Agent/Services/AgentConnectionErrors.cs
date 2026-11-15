using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace CLMS.Agent.Services;

public static class AgentConnectionErrors
{
    public static string Describe(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
                return "HTTPS certificate validation failed. Trust the server certificate and use its matching hostname.";
            if (current is SocketException)
                return "Cannot reach the server. Check that CLMS.Web is running at the entered HTTPS address and port.";
            if (current is HttpRequestException { StatusCode: { } status })
                return status switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        "Server rejected the device key (401/403). Enter the original device key, not its SHA-256 hash. Verify the server has the matching key hash and a valid unit ID.",
                    HttpStatusCode.NotFound =>
                        "Agent endpoint not found (404). Check the server address and run the updated CLMS.Web project.",
                    HttpStatusCode.BadRequest =>
                        "Server rejected the connection request (400). Check the server address and the CLMS.Web terminal for details.",
                    _ => $"Server returned HTTP {(int)status}. Check the CLMS.Web terminal for the server error."
                };
        }

        return "Connection failed after contacting the server. Check the CLMS.Web terminal for details.";
    }
}
