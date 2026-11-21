namespace CLMS.Shared.Dtos;

public sealed record FacultyStreamOffer(Guid StreamId, Guid PeerId, string Presenter, string Sdp);
public sealed record FacultyStreamAnswer(Guid StreamId, Guid PeerId, string Sdp);
public sealed record FacultyStreamPeer(Guid Id, string UnitName, string? AnswerSdp);
public sealed record FacultyStreamState(Guid Id, string Laboratory, IReadOnlyList<FacultyStreamPeer> Peers);

public static class FacultyStreamProtocol
{
    public const string Offer = "FacultyStreamOffer";
    public const string Answer = "AnswerFacultyStream";
    public const string Stop = "StopFacultyStream";
    public const string Closed = "CloseFacultyStream";
    public const int MaximumSdpLength = 64 * 1024;
}
