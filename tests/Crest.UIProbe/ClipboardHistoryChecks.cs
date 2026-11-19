using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Crest.Core;
using Crest.Features;
using Crest.UI;

internal static class ClipboardHistoryChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        ClipboardSnapshotChecks.Run(check);
        string tempParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string root = Path.GetFullPath(Path.Combine(tempParent, "CrestClipboardUI-" + Guid.NewGuid().ToString("N")));
        IDataObject? original = await RetryClipboard(Clipboard.GetDataObject, "read original clipboard");
        var snapshot = await RetryClipboard(() => ClipboardSnapshot.Capture(original), "detach and verify original clipboard values");
        var options = new ClipboardHistoryOptions();
        using var hub = new ClipboardHistoryHub(new SettingsStore(root), () => options, automaticRetention: false);
        using var listener = new ClipboardHistoryListener(hub);
        listener.Start();
        try
        {
            await RetryClipboard(() => Clipboard.SetText("Crest synthetic text before opt-in"), "write pre-opt-in text");
            await Pump();
            check(hub.Search().Count == 0 && !Directory.Exists(root), "Native clipboard listener remains unregistered and stores no text before opt-in");
            options = new(true);
            hub.ApplyPreferences();
            await Pump();
            check(hub.Search().Count == 0, "Enabling clipboard history does not import existing Windows clipboard content");
            await RetryClipboard(() => Clipboard.SetText("Crest synthetic Alpha\nBeta"), "write capture text");
            await WaitFor(() => hub.Search("ALPHA").Any(item => item.Text == "Crest synthetic Alpha\nBeta"), "Unicode text capture");
            check(hub.Search("ALPHA").Single().Text == "Crest synthetic Alpha\nBeta", "Native clipboard-update events capture Unicode text and expose searchable local history");
            hub.SetPaused(true);
            await RetryClipboard(() => Clipboard.SetText("Crest synthetic copy during pause"), "write paused text");
            await Pump();
            hub.SetPaused(false);
            await Pump();
            check(hub.Search().Count == 1 && hub.Search("during pause").Count == 0, "Pause unregisters clipboard capture and resume excludes text copied during pause");
            hub.Clear();
            await Pump();
            check(hub.Search().Count == 0, "Clearing clipboard history does not reimport the current clipboard value");
            var privateData = new DataObject();
            privateData.SetText("Crest synthetic protected text");
            privateData.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[] { 1, 0, 0, 0 }));
            await RetryClipboard(() => Clipboard.SetDataObject(privateData, true), "write protected text");
            await Pump();
            check(hub.Search().Count == 0, "Native capture honors the Windows exclusion marker for protected clipboard content");
            await RetryClipboard(() => Clipboard.SetText("Crest synthetic searchable text"), "write searchable text");
            await WaitFor(() => hub.Search("searchable").Any(item => item.Text == "Crest synthetic searchable text"), "searchable text capture");
            var messages = new List<string>();
            var panel = new ClipboardHistoryPanel(hub, listener.CopyToClipboard, messages.Add, () => listener.Status);
            panel.Measure(new Size(388, 1000));
            panel.Arrange(new Rect(0, 0, 388, panel.DesiredSize.Height));
            var search = Descendants<TextBox>(panel).Single();
            search.Text = "SEARCHABLE";
            panel.Refresh();
            var pin = Descendants<Button>(panel).Single(button => AutomationProperties.GetName(button).StartsWith("Pin saved text:", StringComparison.Ordinal));
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => hub.Search().Any(item => item.Text == "Crest synthetic searchable text" && item.Pinned)
                && Descendants<Button>(panel).Any(button => AutomationProperties.GetName(button) == "Clear history" && button.IsEnabled), "pin update and control recovery");
            check(hub.Search().Single().Pinned && AutomationProperties.GetName(search) == "Search clipboard history", "Clipboard panel search and pin controls operate on saved text with accessible names");
            var copy = Descendants<Button>(panel).Single(button => AutomationProperties.GetName(button).StartsWith("Copy saved text:", StringComparison.Ordinal));
            await RetryClipboard(() =>
            {
                int previousMessages = messages.Count;
                copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (messages.Count > previousMessages && messages[^1] == "Windows clipboard is busy. Try Copy again.")
                    throw new ExternalException("The native copy action encountered a busy clipboard.");
                if (messages.Count <= previousMessages || !messages[^1].StartsWith("Copied saved text", StringComparison.Ordinal))
                    throw new InvalidOperationException("The native clipboard Copy action did not acknowledge success.");
            }, "explicit Copy action");
            await WaitFor(() => messages.Any(message => message.StartsWith("Copied saved text", StringComparison.Ordinal))
                && Clipboard.GetText() == "Crest synthetic searchable text", "explicit clipboard copy");
            string copiedText = await RetryClipboard(Clipboard.GetText, "read copied text");
            check(copiedText == "Crest synthetic searchable text" && messages.Last().StartsWith("Copied saved text", StringComparison.Ordinal), "Explicit clipboard history Copy restores the selected text through Windows");
            var clear = Descendants<Button>(panel).Single(button => AutomationProperties.GetName(button) == "Clear history");
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => hub.Search().Count == 0 && clear.IsEnabled, "clear update and control recovery");
            await Pump();
            check(hub.Search().Count == 0, "Clipboard panel clear removes pinned history as well as ordinary entries");
            options = new(false);
            hub.ApplyPreferences();
            await Pump();
            check(!File.Exists(Path.Combine(root, "clipboard.json")) && !hub.Capturing, "Turning clipboard history off unregisters native capture and erases its saved text");
        }
        finally
        {
            listener.Dispose();
            hub.Reset();
            try
            {
                bool fullyRestored = false, unicodeFallbackRestored = false;
                try
                {
                    await RetryClipboard(() => { if (snapshot.Empty) Clipboard.Clear(); else Clipboard.SetDataObject(snapshot.CreateDataObject(), true); }, "restore detached original clipboard");
                    fullyRestored = await RetryClipboard(() => snapshot.Matches(Clipboard.GetDataObject()), "verify restored clipboard values");
                }
                catch (Exception) { /* Preserve captured Unicode even when a complete multi-format restore fails. */ }
                if (!fullyRestored && snapshot.UnicodeText is string originalText)
                {
                    // Keep any successfully restored richer formats when Unicode is already exact.
                    try { unicodeFallbackRestored = await RetryClipboard(() => Clipboard.GetText(TextDataFormat.UnicodeText) == originalText, "check original Unicode before fallback"); }
                    catch (Exception) { /* Still attempt the independent captured-text write after a failed read. */ }
                    if (!unicodeFallbackRestored)
                        await RetryClipboard(() => Clipboard.SetText(originalText, TextDataFormat.UnicodeText), "restore captured original Unicode fallback");
                    unicodeFallbackRestored = await RetryClipboard(() => Clipboard.GetText(TextDataFormat.UnicodeText) == originalText, "verify original Unicode fallback");
                }
                check(fullyRestored,
                    "Native clipboard regression restores detached user formats and exact Unicode text after disposing capture"
                    + (fullyRestored ? "" : unicodeFallbackRestored ? " (full-format restore failed; exact original Unicode fallback restored)" : " (restoration failed)"));
            }
            finally
            {
                if (root.StartsWith(tempParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestClipboardUI-", StringComparison.Ordinal)
                    && Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }

    private static async Task Pump()
    {
        await Task.Delay(180);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }
    private static async Task WaitFor(Func<bool> completed, string operation)
    {
        long started = Environment.TickCount64;
        while (true)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            try { if (completed()) return; }
            catch (ExternalException) { /* A transient clipboard lock may clear before the bound. */ }
            if (Environment.TickCount64 - started >= 5000)
                throw new TimeoutException("Clipboard regression did not settle: " + operation);
            await Task.Delay(40);
        }
    }
    private static Task RetryClipboard(Action operation, string name)
        => RetryClipboard(() => { operation(); return true; }, name);
    private static async Task<T> RetryClipboard<T>(Func<T> operation, string name)
    {
        long started = Environment.TickCount64;
        while (true)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            try { return operation(); }
            catch (ExternalException)
            {
                if (Environment.TickCount64 - started >= 5000)
                    throw new TimeoutException("Windows clipboard operation remained busy for five seconds: " + name);
            }
            await Task.Delay(40);
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject element) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
        {
            var child = VisualTreeHelper.GetChild(element, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
