using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;
using Crest.UI;

/// <summary>Real in-flight services with test-only HTTP handlers; no external network or user destinations.</summary>
internal static class OptionalActivityWiringChecks
{
    public static async Task Run(Crest.App app, IslandWindow island, AppSettings settings, Action<bool, string> check,
        Action<IslandWindow, string>? renderActivity = null)
    {
        var originalDownloads = app.Downloads;
        var originalAi = app.Ai;
        bool originalDownloadEnabled = settings.Downloads.Enabled, originalAiEnabled = settings.Ai.Enabled;
        string originalModel = settings.Ai.Model;
        string destination = Path.Combine(app.Store.DirectoryPath, "Owned in-flight fixture.bin");
        using var knownStream = new PrefixHoldingStream();
        using var downloadHandler = new HoldingHandler(knownStream);
        using var downloadClient = new HttpClient(downloadHandler);
        using var downloads = new DownloadService(() => settings.Downloads, downloadClient);
        using var aiHandler = new HoldingHandler();
        using var aiClient = new HttpClient(aiHandler);
        using var ai = new AiAssistantService(() => settings.Ai, aiClient);
        Task<string?>? request = null;
        try
        {
            Set(app, "Downloads", downloads); Set(app, "Ai", ai);
            settings.Downloads.Enabled = true; settings.Ai.Enabled = true; settings.Ai.Model = "fixture-model";
            if (downloads.Start("https://example.test/owned-transfer", destination) is string error)
                throw new InvalidOperationException(error);
            await downloadHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            const string prompt = "Private fixture prompt never belongs in the floating summary.";
            request = ai.AskAsync(prompt);
            await aiHandler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var activities = IslandActivityCatalog.Read(app);
            var download = activities.Single(item => item.Kind == IslandActivityKind.Download);
            var assistant = activities.Single(item => item.Kind == IslandActivityKind.Ai);
            check(download.EntityId == downloads.Items.Single().Id && download.Progress == null && download.Value == "0 B"
                && assistant.Progress == null && ai.Busy && !(assistant.Title + assistant.Subtitle + assistant.Value).Contains(prompt, StringComparison.Ordinal),
                "The floating catalog reads real opted-in in-flight downloads and AI state without fabricated percentages or prompt disclosure");

            Select(island, download.Id); island.Expand("Overview"); await Idle();
            renderActivity?.Invoke(island, "fixture-download-unknown-context.png");
            var deck = IslandProbe.Field<ActivityDeck>(island, "_expanded");
            IslandProbe.Field<Button>(deck, "_moreControls").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            check(IslandProbe.Presentation(island) == "Detail" && Descendants<DownloadPanel>(island).Any(panel => panel.IsVisible)
                && !Descendants<AiAssistantPanel>(island).Any(panel => panel.IsVisible),
                "An ongoing download opens its actual Downloads integration controls rather than an unrelated default tab");

            Select(island, assistant.Id); island.Expand("Overview"); await Idle();
            renderActivity?.Invoke(island, "fixture-ai-busy-context.png");
            IslandProbe.Field<Button>(deck, "_moreControls").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            check(IslandProbe.Presentation(island) == "Detail" && Descendants<AiAssistantPanel>(island).Any(panel => panel.IsVisible)
                && !Descendants<DownloadPanel>(island).Any(panel => panel.IsVisible),
                "A busy AI activity opens its actual AI integration controls and preserves the in-flight request");

            if (renderActivity != null)
            {
                string knownDestination = Path.Combine(app.Store.DirectoryPath, "Known-size in-flight fixture.bin");
                if (downloads.Start("https://example.test/known-size-transfer", knownDestination) is string knownError)
                    throw new InvalidOperationException(knownError);
                await knownStream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(3));
                var knownItem = downloads.Items.Single(item => item.Destination == knownDestination);
                var knownActivity = IslandActivityCatalog.Read(app).Single(item => item.EntityId == knownItem.Id);
                if (knownItem.Phase != DownloadPhase.Downloading || knownItem.ReceivedBytes != PrefixHoldingStream.PrefixBytes
                    || knownItem.TotalBytes != PrefixHoldingStream.TotalBytes || knownActivity.Progress != .25
                    || !ai.Busy || downloads.Items.Single(item => item.Id == download.EntityId).Phase != DownloadPhase.Downloading)
                    throw new InvalidOperationException("The known-size preview requires real written bytes while the original unknown download and AI request remain in flight.");
                Select(island, knownActivity.Id); island.Expand("Overview"); await Idle();
                renderActivity(island, "fixture-download-known-context.png");
                downloads.Cancel(knownItem.Id);
                for (int attempt = 0; attempt < 100 && downloads.Items.Single(item => item.Id == knownItem.Id).Phase == DownloadPhase.Downloading; attempt++) await Task.Delay(10);
                for (int attempt = 0; attempt < 100 && downloads.Items.Any(item => item.Id == knownItem.Id); attempt++)
                { downloads.Remove(knownItem.Id); await Task.Delay(10); }
                if (downloads.Items.Any(item => item.Id == knownItem.Id) || File.Exists(knownDestination)
                    || Directory.GetFiles(app.Store.DirectoryPath, Path.GetFileName(knownDestination) + ".crest-*.part").Length != 0)
                    throw new InvalidOperationException("The known-size preview must remove its cancelled transfer and partial bytes before the existing consent checks.");
            }

            settings.Downloads.Enabled = false; settings.Ai.Enabled = false;
            check(downloads.Items.Single().Phase == DownloadPhase.Downloading && ai.Busy
                && IslandActivityCatalog.Read(app).All(item => item.Kind is not IslandActivityKind.Download and not IslandActivityKind.Ai),
                "Removing provider consent excludes late live integration state before asynchronous cleanup completes");
            island.Suppress();
            downloads.Cancel(download.EntityId); ai.Cancel();
            await request.WaitAsync(TimeSpan.FromSeconds(3));
            for (int attempt = 0; attempt < 100 && downloads.Items.Any(item => item.Phase == DownloadPhase.Downloading); attempt++) await Task.Delay(10);
            check(!island.IsVisible && !island.IsHitTestVisible && !ai.Busy && downloads.Items.All(item => item.Phase != DownloadPhase.Downloading)
                && !File.Exists(destination),
                "Cancelling actual optional operations cleans their owned scratch state without revealing a suppressed island");
        }
        finally
        {
            island.Suppress();
            foreach (var item in downloads.Items) downloads.Cancel(item.Id);
            ai.Cancel();
            if (request != null) await request.WaitAsync(TimeSpan.FromSeconds(3));
            await downloads.WaitForIdleAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Set(app, "Downloads", originalDownloads); Set(app, "Ai", originalAi);
            settings.Downloads.Enabled = originalDownloadEnabled; settings.Ai.Enabled = originalAiEnabled; settings.Ai.Model = originalModel;
        }
    }
    private static void Set(Crest.App app, string property, object value) => typeof(Crest.App).GetProperty(property)!.SetValue(app, value);
    private static void Select(IslandWindow island, string id)
        => typeof(IslandWindow).GetField("_selectedActivityId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(island, id);
    private static async Task Idle() { await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); await Task.Delay(25); }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
    private sealed class HoldingHandler : HttpMessageHandler
    {
        private readonly PrefixHoldingStream? _knownStream;
        public HoldingHandler(PrefixHoldingStream? knownStream = null) => _knownStream = knownStream;
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_knownStream != null && request.RequestUri?.AbsolutePath == "/known-size-transfer")
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(_knownStream), RequestMessage = request };
                response.Content.Headers.ContentLength = PrefixHoldingStream.TotalBytes;
                return response;
            }
            Entered.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The cancelled fixture handler must not return a response.");
        }
    }
    private sealed class PrefixHoldingStream : Stream
    {
        public const int PrefixBytes = 65536;
        public const long TotalBytes = PrefixBytes * 4L;
        private int _sent;
        public TaskCompletionSource<bool> Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_sent < PrefixBytes)
            {
                int count = Math.Min(buffer.Length, PrefixBytes - _sent);
                buffer.Span[..count].Fill(0x5A); _sent += count;
                return count;
            }
            Waiting.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("The cancelled preview stream must not finish a transfer.");
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
