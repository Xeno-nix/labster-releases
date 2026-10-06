namespace CLMS.Web.Services;

public class ToastService
{
    private readonly Queue<ToastMessage> _queue = new();
    public event Action? OnChanged;
    
    /// <summary>
    /// Used to show a toast which is handled by ToastContainer
    /// </summary>
    /// <param name="message"></param>
    /// <param name="type"></param>
    public void Show(string message, ToastType type = ToastType.Info)
    {
        var toast = new ToastMessage(Guid.NewGuid(), message, type);
        
        _queue.Enqueue(toast);
        OnChanged?.Invoke();
    }
    
    /// <summary>
    /// Used by (and should only be used by) ToastContainer to retrieve Toasts
    /// </summary>
    /// <param name="toast"></param>
    /// <returns></returns>
    public bool TryDequeue(out ToastMessage? toast)
    {
        if (_queue.TryDequeue(out var result))
        {
            toast = result;
            return true;
        }

        toast = null;
        return false;
    }
    
    public enum ToastType
    {
        Success,
        Warning,
        Error,
        Info,
    }
    public sealed record ToastMessage(
        Guid Id,
        string Message,
        ToastType Type);
}