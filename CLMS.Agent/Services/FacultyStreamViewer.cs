using CLMS.Shared.Dtos;
using Microsoft.Extensions.Logging;

namespace CLMS.Agent.Services;

/// <summary>Receive-only WebRTC viewer. No login, device token, camera, or microphone is exposed to the WebView.</summary>
public sealed class FacultyStreamViewer(IDesktopAgentService desktop, ILogger<FacultyStreamViewer> logger) : IFacultyStreamViewer
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private CancellationTokenSource? receiving;
    private Guid? receivingStream;
    private Guid? activeStream;
    private NavigationPage? modal;
    private ContentView? viewerBody;
    private Button? fullScreenButton;
    private WebView? web;
    private Func<Task>? notifyClosed;
    private bool fullScreenManaged, previousFullScreen;

    public async Task<string?> ShowAsync(FacultyStreamOffer offer, Func<Task> onClosed, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = attempt.Token;
        try
        {
            await CloseCoreAsync();
            lock (sync) { receiving = attempt; receivingStream = offer.StreamId; }
            activeStream = offer.StreamId;
            notifyClosed = onClosed;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                desktop.Show();
                web = new WebView();
                web.Navigating += (_, args) =>
                {
                    // The viewer is local, self-contained HTML. Never navigate to untrusted remote content.
                    if (args.Url is not ("about:blank" or "about:srcdoc") && !args.Url.StartsWith("data:", StringComparison.Ordinal)
                        && !args.Url.StartsWith("ms-appx-web:", StringComparison.Ordinal)) args.Cancel = true;
                };
                var page = new ViewerPage(() => StopAsync()) { Title = $"Live stream · {offer.Presenter}", BackgroundColor = Colors.Black };
                viewerBody = new ContentView { Content = web };
                fullScreenButton = new Button { Text = "Exit full screen", FontSize = 12, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#243044"), Padding = new Thickness(12, 6) };
                fullScreenButton.Clicked += async (_, _) => await SetViewerFullScreenAsync(!desktop.IsFullScreen);
                var closeButton = new Button { Text = "Close viewer", FontSize = 12, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#8b1538"), Padding = new Thickness(12, 6) };
                closeButton.Clicked += async (_, _) => await StopAsync();
                var header = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto) },
                    ColumnSpacing = 10, Padding = new Thickness(16, 8), BackgroundColor = Color.FromArgb("#111827")
                };
                header.Add(new Label { Text = $"Faculty live stream · {offer.Presenter}", TextColor = Colors.White, FontSize = 14, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation });
                header.Add(fullScreenButton, 1);
                header.Add(closeButton, 2);
                var content = new Grid { RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Star) } };
                content.Add(header);
                content.Add(viewerBody, 0, 1);
                page.Content = content;
                NavigationPage.SetHasNavigationBar(page, false);
                modal = new NavigationPage(page);
                var root = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page
                    ?? throw new InvalidOperationException("The agent window is unavailable.");
                await root.Navigation.PushModalAsync(modal);
                previousFullScreen = desktop.IsFullScreen;
                fullScreenManaged = true;
                await SetViewerFullScreenAsync(true);
                using var source = await FileSystem.OpenAppPackageFileAsync("faculty-stream.html");
                using var reader = new StreamReader(source);
                web.Source = new HtmlWebViewSource { Html = await reader.ReadToEndAsync(cancellationToken) };
            });
            return await FacultyViewerHandshake.NegotiateAsync(offer.Sdp,
                script => MainThread.InvokeOnMainThreadAsync(() => web!.EvaluateJavaScriptAsync(script)), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CloseCoreAsync();
            throw;
        }
        catch (Exception ex)
        {
            var failure = ex as FacultyViewerException ?? new FacultyViewerException("viewer_start_failed");
            logger.LogWarning("Faculty viewer setup failed ({Code}, {ExceptionType}).", failure.Code, ex.GetType().Name);
            if (!await ShowFailureAsync(failure)) { await CloseCoreAsync(); throw; }
            return null;
        }
        finally
        {
            lock (sync) { receiving = null; receivingStream = null; }
            gate.Release();
        }
    }

    private Task<bool> ShowFailureAsync(FacultyViewerException failure) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (viewerBody is null || modal is null) return false;
        if (web is not null)
        {
            try { await web.EvaluateJavaScriptAsync("window.stopStream && window.stopStream(); null;"); } catch { }
        }
        var close = new Button { Text = "Close viewer", BackgroundColor = Color.FromArgb("#8b1538"), TextColor = Colors.White };
        close.Clicked += async (_, _) => await StopAsync();
        viewerBody.Content = new VerticalStackLayout
        {
            Padding = new Thickness(28), Spacing = 16, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = "Live stream couldn't start", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                new Label { Text = failure.UserMessage, TextColor = Colors.LightGray },
                new Label { Text = $"Diagnostic: {failure.Code}", FontSize = 12, TextColor = Colors.Gray },
                close
            }
        };
        return true;
    });

    private async Task SetViewerFullScreenAsync(bool enabled)
    {
        try { await desktop.SetFullScreenAsync(enabled); }
        catch (Exception ex) { logger.LogWarning("Viewer full-screen change failed ({ExceptionType}).", ex.GetType().Name); }
        if (fullScreenButton is not null) fullScreenButton.Text = desktop.IsFullScreen ? "Exit full screen" : "Full screen";
    }

    private sealed class ViewerPage(Func<Task> close) : ContentPage
    {
        protected override bool OnBackButtonPressed()
        {
            MainThread.BeginInvokeOnMainThread(async () => await close());
            return true;
        }
    }

    public async Task ShowErrorAsync(Guid streamId, string code)
    {
        await gate.WaitAsync();
        try
        {
            if (activeStream == streamId) await ShowFailureAsync(new FacultyViewerException(code));
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(Guid? streamId = null)
    {
        // Stop can interrupt loading/ICE gathering before waiting for the UI operation's gate.
        lock (sync)
            if (!streamId.HasValue || streamId == receivingStream) receiving?.Cancel();
        await gate.WaitAsync();
        try
        {
            if (streamId.HasValue && streamId != activeStream) return;
            var closed = streamId.HasValue ? null : notifyClosed;
            await CloseCoreAsync();
            if (closed is not null)
            {
                try { await closed(); }
                catch { /* The server may already be gone; closing locally must still succeed. */ }
            }
        }
        finally { gate.Release(); }
    }

    private async Task CloseCoreAsync()
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (web is not null)
                {
                    try { await web.EvaluateJavaScriptAsync("window.stopStream && window.stopStream()"); }
                    catch { }
                }
                if (modal is not null)
                {
                    if (fullScreenManaged) await SetViewerFullScreenAsync(previousFullScreen);
                    var navigation = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
                    if (navigation?.ModalStack.LastOrDefault() == modal) await navigation.PopModalAsync();
                }
                web?.Handler?.DisconnectHandler();
                web = null;
                modal = null;
                viewerBody = null;
                fullScreenButton = null;
                fullScreenManaged = false;
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning("Could not dismiss the faculty stream viewer ({ExceptionType}).", ex.GetType().Name);
        }
        finally { activeStream = null; notifyClosed = null; }
    }
}
