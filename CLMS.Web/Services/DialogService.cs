namespace CLMS.Web.Services;

public class DialogService
{
    public event Action? OnChanged;
    public DialogRequest? CurrentRequest { get; private set; }
    
    public Task ShowMessageDialogAsync(string message,string title = "")
    {
        var dialogRequest = new DialogRequest(DialogType.Message, title, message);
        CurrentRequest = dialogRequest;
        OnChanged?.Invoke();

        return dialogRequest.CompletionSource.Task;
    }

    public Task<bool> ShowConfirmDialogAsync(string message, string title = "Confirm")
    {
        var dialogRequest = new DialogRequest(DialogType.Confirm, title, message);
        CurrentRequest = dialogRequest;
        OnChanged?.Invoke();
        
        return dialogRequest.CompletionSource.Task;
    }

    public void Cancel()
    {
        CurrentRequest?.CompletionSource.TrySetResult(false);
        Close();
    }

    public void Confirm()
    {
        CurrentRequest?.CompletionSource.TrySetResult(true);
        Close();
    }

    public void Close()
    {
        CurrentRequest = null;
        OnChanged?.Invoke();
    }

    public enum DialogType
    {
        Message,
        Confirm,
    }

    public sealed class DialogRequest
    {
        public DialogType Type;
        public string Title;
        public string Message;

        public TaskCompletionSource<bool> CompletionSource = new();

        public DialogRequest(DialogType type, string title, string message)
        {
            Type = type;
            Title = title;
            Message = message;
        }
    }
}