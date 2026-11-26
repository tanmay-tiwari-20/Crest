using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;
using Crest.UI;

/// <summary>Actual optional-tool controls with isolated HTTP fixtures and destination/open callbacks.</summary>
internal static class DownloadAiUiChecks
{
    public static async Task Run(Crest.App app, Action<bool, string> check, string output)
    {
        app.Dispatcher.VerifyAccess();
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string directory = Path.Combine(parent, "CrestDownloadAiUI-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); Directory.CreateDirectory(output);
        var settings = new AppSettings { Theme = "Dark" };
        var messages = new List<string>();
        int downloadRequests = 0, destinationChoices = 0, saved = 0;
        var opened = new List<string>();
        string? destination = Path.Combine(directory, "Chosen download.txt");
        using var downloadClient = new HttpClient(new FixtureHandler((_, _) =>
        {
            downloadRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("selected download bytes", Encoding.UTF8) });
        }));
        using var downloads = new DownloadService(() => settings.Downloads, downloadClient);
        int aiRequests = 0;
        using var aiClient = new HttpClient(new FixtureHandler((_, _) =>
        {
            aiRequests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"message\":{\"content\":\"This fixture answer performs no action.\"}}", Encoding.UTF8, "application/json") });
        }));
        using var ai = new AiAssistantService(() => settings.Ai, aiClient);
        void Save() { saved++; downloads.ApplyPreferences(); ai.ApplyPreferences(); }
        var downloadPanel = new DownloadPanel(downloads, settings.Downloads, Save, messages.Add,
            () => { destinationChoices++; return destination; }, opened.Add);
        var aiPanel = new AiAssistantPanel(ai, settings.Ai, Save, messages.Add);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var surface = new Border { Width = 388, Height = 236, Child = scroll };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        var window = new Window { Title = "Crest download and AI controls probe", Width = 420, Height = 300, Content = surface, ShowInTaskbar = false };
        try
        {
            scroll.Content = downloadPanel; Design.Apply(settings); window.Show(); await Settle();
            check(!settings.Downloads.Enabled && !Input(downloadPanel, "Crest download HTTP or HTTPS URL").IsEnabled
                && !Button(downloadPanel, "Choose destination and download").IsEnabled && downloadRequests == 0,
                "Download controls remain disabled with no HTTP request before explicit opt-in");
            var url = Input(downloadPanel, "Crest download HTTP or HTTPS URL");
            check(url.MaxLength == 4096 && CheckBox(downloadPanel, "Enable Crest-managed downloads").IsChecked == false,
                "Download URL input is bounded and enablement exposes an accessible checkbox");
            CheckBox(downloadPanel, "Enable Crest-managed downloads").IsChecked = true; await Settle();
            check(settings.Downloads.Enabled && url.IsEnabled && saved > 0,
                "Download opt-in saves the setting and enables the owned-transfer controls");
            url.Text = "file:///unsupported"; await Click(downloadPanel, "Choose destination and download");
            check(destinationChoices == 0 && downloadRequests == 0 && messages.Count == 1,
                "Invalid download URL is rejected before choosing a destination or sending a request");
            url.Text = "https://example.test/selected-file"; await Click(downloadPanel, "Choose destination and download");
            await downloads.WaitForIdleAsync(); downloadPanel.Refresh(); await Settle();
            check(destinationChoices == 1 && downloadRequests == 1 && downloads.Items.Single().Phase == DownloadPhase.Completed
                && File.ReadAllText(destination!) == "selected download bytes" && opened.Count == 0,
                "Download button uses the selected filename and completed transfers never open files automatically");
            await Click(downloadPanel, "Open downloaded file");
            check(opened.SequenceEqual(new[] { destination! }),
                "Completed-download open control invokes only its explicit callback with the chosen path");
            destination = null; await Click(downloadPanel, "Choose destination and download");
            check(destinationChoices == 2 && downloadRequests == 1 && downloads.Items.Count == 1,
                "Cancelling destination selection creates no transfer or network request");
            foreach (string theme in new[] { "Dark", "Light" })
            {
                settings.Theme = theme; Design.Apply(settings); scroll.ScrollToTop(); await Settle();
                Render(surface, Path.Combine(output, "p3-downloads-" + theme.ToLowerInvariant() + ".png"));
                scroll.ScrollToBottom(); await Settle();
                Render(surface, Path.Combine(output, "p3-downloads-actions-" + theme.ToLowerInvariant() + ".png"));
            }
            await Click(downloadPanel, "Cancel transfers and clear list"); await downloads.WaitForIdleAsync();
            check(downloads.Items.Count == 0 && File.Exists(Path.Combine(directory, "Chosen download.txt")),
                "Clear download list removes retained references and preserves the downloaded file");
            CheckBox(downloadPanel, "Enable Crest-managed downloads").IsChecked = false; await Settle();
            check(!settings.Downloads.Enabled && !url.IsEnabled, "Download disconnect restores disabled controls");

            scroll.Content = aiPanel; scroll.ScrollToTop(); await Settle();
            var prompt = Input(aiPanel, "Read-only AI question");
            var endpoint = Input(aiPanel, "Local Ollama endpoint, loopback only");
            var model = Input(aiPanel, "Installed Ollama model name");
            check(!settings.Ai.Enabled && !prompt.IsEnabled && !Button(aiPanel, "Ask local model").IsEnabled && aiRequests == 0
                && prompt.MaxLength == AiAssistantService.MaxPromptCharacters && endpoint.MaxLength == 300 && model.MaxLength == 100,
                "AI remains off before opt-in and bounds question, endpoint, and model inputs");
            int beforeErrors = messages.Count;
            endpoint.Text = "https://remote.example/"; await Click(aiPanel, "Save local model selection");
            check(messages.Count == beforeErrors + 1 && !settings.Ai.Enabled && settings.Ai.Endpoint == AiAssistantService.DefaultEndpoint && aiRequests == 0,
                "AI endpoint control rejects remote services without enabling access or sending the prompt");
            endpoint.Text = AiAssistantService.DefaultEndpoint; model.Text = "fixture-model";
            CheckBox(aiPanel, "Enable read-only local AI assistance").IsChecked = true; await Settle();
            check(settings.Ai.Enabled && settings.Ai.Model == "fixture-model" && prompt.IsEnabled && aiRequests == 0,
                "Local AI opt-in validates and saves the installed-model selection before requesting an answer");
            prompt.Text = "Describe a timer without taking action."; await Click(aiPanel, "Ask local model");
            await WaitFor(() => !ai.Busy && ai.Messages.Count == 2); aiPanel.Refresh(); await Settle();
            check(aiRequests == 1 && ai.Messages.Count == 2 && prompt.Text.Length == 0
                && Input(aiPanel, "Local model answer, read-only text").IsReadOnly
                && Input(aiPanel, "Local model answer, read-only text").Text == "This fixture answer performs no action.",
                "Ask control renders the fixture response as read-only text and clears the submitted question");
            Save(); aiPanel.Refresh(); await Settle();
            check(ai.Messages.Count == 2 && Descendants<TextBox>(aiPanel).Any(x => AutomationProperties.GetName(x) == "Local model answer, read-only text"),
                "Unrelated settings application preserves the visible AI conversation");
            foreach (string theme in new[] { "Dark", "Light" })
            {
                settings.Theme = theme; Design.Apply(settings); scroll.ScrollToTop(); await Settle();
                Render(surface, Path.Combine(output, "p3-ai-settings-" + theme.ToLowerInvariant() + ".png"));
                scroll.ScrollToBottom(); await Settle();
                Render(surface, Path.Combine(output, "p3-ai-answer-" + theme.ToLowerInvariant() + ".png"));
            }
            check(Descendants<Button>(aiPanel).All(x => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(x)))
                && Descendants<TextBox>(aiPanel).All(x => !string.IsNullOrWhiteSpace(AutomationProperties.GetName(x))),
                "Visible AI controls and read-only conversation fields expose accessible names");
            await Click(aiPanel, "Clear conversation");
            check(ai.Messages.Count == 0 && !Descendants<TextBox>(aiPanel).Any(x => AutomationProperties.GetName(x) == "Local model answer, read-only text"),
                "AI clear control removes retained conversation and its response fields");
            CheckBox(aiPanel, "Enable read-only local AI assistance").IsChecked = false; await Settle();
            check(!settings.Ai.Enabled && !prompt.IsEnabled && ai.Messages.Count == 0,
                "AI disconnect clears memory-only conversation and disables the question control");
            check(scroll.ScrollableHeight > 0 && surface.ActualWidth == 388 && surface.ActualHeight == 236,
                "Optional panels fit the 388 by 236 detail viewport with reachable scroll overflow");
        }
        finally
        {
            window.Close(); downloads.Dispose(); ai.Dispose();
            Design.Apply(app.EffectivePreferences);
            if (Path.GetFullPath(directory).StartsWith(parent, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(directory).StartsWith("CrestDownloadAiUI-", StringComparison.Ordinal) && Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    private static TextBox Input(DependencyObject root, string name) => Descendants<TextBox>(root).Single(x => AutomationProperties.GetName(x) == name);
    private static CheckBox CheckBox(DependencyObject root, string name) => Descendants<CheckBox>(root).Single(x => AutomationProperties.GetName(x) == name);
    private static Button Button(DependencyObject root, string name) => Descendants<Button>(root).Single(x => AutomationProperties.GetName(x) == name);
    private static async Task Click(DependencyObject root, string name)
    {
        var button = Button(root, name); if (!button.IsEnabled) throw new InvalidOperationException("Optional UI control is disabled: " + name);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); await Settle();
    }
    private static async Task Settle() { await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); await Task.Delay(20); }
    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        if (!condition()) throw new TimeoutException("Fixture-backed optional UI action did not settle.");
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static void Render(FrameworkElement element, string file)
    {
        element.UpdateLayout();
        var target = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen()) context.DrawRectangle(IslandProbe.VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        target.Render(drawing); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = File.Create(file); encoder.Save(stream);
    }
    private sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
