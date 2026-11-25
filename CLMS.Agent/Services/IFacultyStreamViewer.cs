using CLMS.Shared.Dtos;

namespace CLMS.Agent.Services;

public interface IFacultyStreamViewer
{
    Task<string?> ShowAsync(FacultyStreamOffer offer, Func<Task> onClosed, CancellationToken cancellationToken);
    Task StopAsync(Guid? streamId = null);
    Task ShowErrorAsync(Guid streamId, string code) => StopAsync(streamId);
}
