namespace CLMS.Shared.Dtos;

public enum WorkstationCommandKind { SendMessage, Lock, Restart, Shutdown }
public enum WorkstationCommandStatus { MessageReceived, Applied, Requested, Failed, Expired, Unsupported, NotificationsDisabled }

public sealed record WorkstationCommand(Guid Id, WorkstationCommandKind Kind, string? Message, DateTimeOffset ExpiresAt);
public sealed record WorkstationCommandResult(Guid Id, WorkstationCommandStatus Status);

public static class WorkstationCommandProtocol
{
    public const string Execute = "ExecuteWorkstationCommand";
    public const int MaximumMessageLength = 1000;

    public static void Validate(WorkstationCommandKind kind, string? message)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown workstation command.");
        if (kind == WorkstationCommandKind.SendMessage)
        {
            if (string.IsNullOrWhiteSpace(message) || message.Length > MaximumMessageLength)
                throw new ArgumentException("Enter a message of 1 to 1000 characters.");
        }
        else if (!string.IsNullOrEmpty(message))
            throw new ArgumentException("This command does not accept a message.");
    }
}
