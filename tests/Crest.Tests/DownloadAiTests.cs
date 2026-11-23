using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Crest.Features;

internal static class DownloadAiTests
{
    public static int Run()
    {
        int passed = 0;
        string parent = Path.GetFullPath(Path.GetTempPath());
        string root = Path.Combine(parent, "CrestDownloadAiTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Test("Downloads are opt-in and reject unsafe URLs and existing destinations", () =>
            {
                var preferences = new DownloadPreferences();
                int requests = 0;
                using var client = Client((_, _) => { requests++; return Task.FromResult(Response("bytes")); });
                using var service = new DownloadService(() => preferences, client);
                string target = Path.Combine(root, "selected.bin");
                Check(service.Start("https://example.test/file", target) != null && requests == 0);
                preferences.Enabled = true;
                Check(service.Start("file:///secret", target) != null);
                Check(service.Start("https://user:secret@example.test/file", target) != null);
                Check(service.Start("https://example.test/file#fragment", target) != null);
                Check(service.Start("https://example.test/file", "relative.bin") != null);
                File.WriteAllText(target, "original");
                Check(service.Start("https://example.test/file", target) != null && File.ReadAllText(target) == "original" && requests == 0);
            });
            Test("Owned download streams bytes and leaves no partial file on success", () =>
            {
                var preferences = new DownloadPreferences { Enabled = true };
                byte[] expected = Enumerable.Range(0, 90000).Select(x => (byte)(x % 256)).ToArray();
                using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(expected) }));
                using var service = new DownloadService(() => preferences, client);
                string target = Path.Combine(root, "success.bin");
                Check(service.Start("https://example.test/file", target) == null);
                service.WaitForIdleAsync().GetAwaiter().GetResult();
                var item = service.Items.Single();
                Check(item.Phase == DownloadPhase.Completed && item.ReceivedBytes == expected.Length && item.Percentage == 100);
                Check(File.ReadAllBytes(target).SequenceEqual(expected) && !Directory.EnumerateFiles(root, "*.part").Any());
                service.Remove(item.Id); Check(service.Items.Count == 0 && File.Exists(target));
            });
            Test("Unknown-length download never fabricates a percentage", () =>
            {
                var preferences = new DownloadPreferences { Enabled = true };
                using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NonSeekableStream(Encoding.UTF8.GetBytes("unknown length"))) }));
                using var service = new DownloadService(() => preferences, client);
                Check(service.Start("https://example.test/file", Path.Combine(root, "unknown.bin")) == null);
                service.WaitForIdleAsync().GetAwaiter().GetResult();
                Check(service.Items.Single().Phase == DownloadPhase.Completed && service.Items.Single().TotalBytes == null && service.Items.Single().Percentage == null);
            });
            Test("Incomplete and oversized server payloads fail without retained partial bytes", () =>
            {
                var preferences = new DownloadPreferences { Enabled = true };
                using var client = Client((request, _) =>
                {
                    var response = Response("short");
                    response.Content.Headers.ContentLength = request.RequestUri!.AbsolutePath == "/large" ? DownloadService.MaxBytes + 1 : 100;
                    return Task.FromResult(response);
                });
                using var service = new DownloadService(() => preferences, client);
                Check(service.Start("https://example.test/short", Path.Combine(root, "short.bin")) == null);
                Check(service.Start("https://example.test/large", Path.Combine(root, "large.bin")) == null);
                service.WaitForIdleAsync().GetAwaiter().GetResult();
                Check(service.Items.All(x => x.Phase == DownloadPhase.Failed) && !File.Exists(Path.Combine(root, "short.bin")) && !File.Exists(Path.Combine(root, "large.bin")));
                Check(!Directory.EnumerateFiles(root, "*.part").Any());
            });
            Test("Concurrent downloads are bounded and cancellation leaves originals untouched", () =>
            {
                var preferences = new DownloadPreferences { Enabled = true };
                using var client = Client(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("never"); });
                using var service = new DownloadService(() => preferences, client);
                Check(service.Start("https://example.test/one", Path.Combine(root, "one.bin")) == null);
                Check(service.Start("https://example.test/two", Path.Combine(root, "two.bin")) == null);
                Check(service.Start("https://example.test/three", Path.Combine(root, "three.bin")) != null && service.Items.Count == 2);
                foreach (var item in service.Items) service.Cancel(item.Id);
                service.WaitForIdleAsync().GetAwaiter().GetResult();
                Check(service.Items.All(x => x.Phase == DownloadPhase.Cancelled) && !File.Exists(Path.Combine(root, "one.bin")));
            });
            Test("Disabling downloads cancels outstanding work and clears retained URLs", () =>
            {
                var preferences = new DownloadPreferences { Enabled = true };
                using var client = Client(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("never"); });
                using var service = new DownloadService(() => preferences, client);
                Check(service.Start("https://example.test/private?query=chosen", Path.Combine(root, "disabled.bin")) == null);
                preferences.Enabled = false; service.ApplyPreferences(); service.WaitForIdleAsync().GetAwaiter().GetResult();
                Check(service.Items.Count == 0 && !File.Exists(Path.Combine(root, "disabled.bin")));
            });
            Test("Failed download can retry while respecting destination creation races", () =>
            {
                var preferences = new DownloadPreferences { Enabled = true };
                int attempts = 0;
                using var client = Client((_, _) => Task.FromResult(++attempts == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Response("retried")));
                using var service = new DownloadService(() => preferences, client);
                string target = Path.Combine(root, "retry.bin");
                service.Start("https://example.test/retry", target); service.WaitForIdleAsync().GetAwaiter().GetResult();
                string id = service.Items.Single().Id;
                Check(service.Items.Single().Phase == DownloadPhase.Failed);
                File.WriteAllText(target, "original");
                Check(service.Retry(id) != null && File.ReadAllText(target) == "original");
                File.Delete(target);
                Check(service.Retry(id) == null); service.WaitForIdleAsync().GetAwaiter().GetResult();
                Check(service.Items.Single().Phase == DownloadPhase.Completed && File.ReadAllText(target) == "retried");
            });
            Test("Quit cancels a stream in progress and finishes owned partial-file cleanup", () =>
            {
                var preferences = new DownloadPreferences { Enabled = true };
                using var source = new BlockingAfterFirstReadStream();
                using var client = Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) }));
                var service = new DownloadService(() => preferences, client);
                string target = Path.Combine(root, "quit.bin");
                Check(service.Start("https://example.test/quit", target) == null);
                source.Waiting.Task.Wait(TimeSpan.FromSeconds(5));
                Check(source.Waiting.Task.IsCompleted && Directory.EnumerateFiles(root, "*.part").Any());
                service.Dispose(); service.WaitForIdleAsync().GetAwaiter().GetResult();
                Check(!File.Exists(target) && !Directory.EnumerateFiles(root, "*.part").Any());
            });
            Test("AI normalizes unsafe destinations to disabled local defaults", () =>
            {
                Check(AiAssistantService.TryGetEndpoint("http://localhost:11434/", out _));
                Check(AiAssistantService.TryGetEndpoint("http://[::1]:11434/", out _));
                foreach (string endpoint in new[] { "https://remote.test/", "http://localhost.evil.test/", "http://127.0.0.1:11434/path", "http://user:secret@localhost/", "http://localhost/?secret=1" })
                    Check(!AiAssistantService.TryGetEndpoint(endpoint, out _));
                var preferences = new AiPreferences { Enabled = true, Endpoint = "https://remote.test/", Model = "bad model" };
                AiAssistantService.Normalize(preferences);
                Check(!preferences.Enabled && preferences.Endpoint == AiAssistantService.DefaultEndpoint && preferences.Model == "llama3.2");
            });
            Test("Read-only AI sends only bounded conversation to the selected local model", () =>
            {
                var preferences = new AiPreferences();
                string? posted = null; int requests = 0;
                using var client = Client(async (request, token) =>
                {
                    requests++; posted = await request.Content!.ReadAsStringAsync(token);
                    Check(request.RequestUri == new Uri("http://127.0.0.1:11434/api/chat"));
                    return Response("{\"message\":{\"content\":\"A read-only answer.\"}}", "application/json");
                });
                using var service = new AiAssistantService(() => preferences, client);
                Check(service.AskAsync("question").GetAwaiter().GetResult() != null && requests == 0);
                preferences.Enabled = true;
                Check(service.AskAsync("question").GetAwaiter().GetResult() == null);
                using var json = JsonDocument.Parse(posted!);
                Check(json.RootElement.GetProperty("messages").GetArrayLength() == 2 && !json.RootElement.TryGetProperty("tools", out _));
                Check(json.RootElement.GetProperty("stream").GetBoolean() == false && json.RootElement.GetProperty("model").GetString() == preferences.Model);
                Check(service.Messages.Count == 2 && service.Messages[1].Text == "A read-only answer." && !service.Busy);
                service.Clear(); Check(service.Messages.Count == 0);
            });
            Test("Invalid and oversized AI responses remain recoverable without conversation retention", () =>
            {
                var preferences = new AiPreferences { Enabled = true };
                int requests = 0;
                using var client = Client((_, _) => Task.FromResult(Response(++requests == 1 ? "{\"wrong\":true}" : new string('x', 170000), "application/json")));
                using var service = new AiAssistantService(() => preferences, client);
                Check(service.AskAsync("first").GetAwaiter().GetResult() != null && service.Messages.Count == 0);
                Check(service.AskAsync("second").GetAwaiter().GetResult() != null && service.Messages.Count == 0);
                Check(service.AskAsync(new string('x', AiAssistantService.MaxPromptCharacters + 1)).GetAwaiter().GetResult() != null && requests == 2);
            });
            Test("Unrelated settings saves preserve AI conversation and source changes clear it", () =>
            {
                var preferences = new AiPreferences { Enabled = true };
                using var client = Client((_, _) => Task.FromResult(Response("{\"message\":{\"content\":\"Retained answer.\"}}", "application/json")));
                using var service = new AiAssistantService(() => preferences, client);
                Check(service.AskAsync("keep").GetAwaiter().GetResult() == null);
                service.ApplyPreferences(); service.ApplyPreferences();
                Check(service.Messages.Count == 2);
                preferences.Model = "new-model"; service.ApplyPreferences();
                Check(service.Messages.Count == 0);
            });
            Test("AI cancellation and disconnect reject late answers and clear the conversation", () =>
            {
                var preferences = new AiPreferences { Enabled = true };
                using var client = Client(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Response("{}"); });
                using var service = new AiAssistantService(() => preferences, client);
                var pending = service.AskAsync("pending");
                Check(service.Busy && service.AskAsync("second").GetAwaiter().GetResult() != null);
                preferences.Enabled = false; service.ApplyPreferences();
                Check(pending.GetAwaiter().GetResult() != null && !service.Busy && service.Messages.Count == 0 && service.Status.Contains("off"));
            });
            Test("AI answers and memory-only history obey bounded retention", () =>
            {
                var preferences = new AiPreferences { Enabled = true };
                string answer = new string('a', AiAssistantService.MaxAnswerCharacters + 10);
                using var client = Client((_, _) => Task.FromResult(Response(JsonSerializer.Serialize(new { message = new { content = answer } }), "application/json")));
                using var service = new AiAssistantService(() => preferences, client);
                for (int i = 0; i < 8; i++) Check(service.AskAsync("question " + i).GetAwaiter().GetResult() == null);
                Check(service.Messages.Count <= 12 && service.Messages.Sum(x => x.Text.Length) <= AiAssistantService.MaxConversationCharacters);
                Check(service.Messages.Last().Text.Contains("truncated") && service.Messages.Last().Text.Length < AiAssistantService.MaxAnswerCharacters + 100);
            });
            return passed;
        }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(root).StartsWith("CrestDownloadAiTests-", StringComparison.Ordinal)) Directory.Delete(root, true);
        }
        void Test(string label, Action action) { action(); passed++; Console.WriteLine("PASS " + label); }
        void Check(bool outcome) { if (!outcome) throw new Exception("Download or AI outcome failed."); }
    }
    private static HttpClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new FixtureHandler(send)) { Timeout = Timeout.InfiniteTimeSpan };
    private static HttpResponseMessage Response(string text, string type = "application/octet-stream")
        => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, type) };
    private sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
    private sealed class BlockingAfterFirstReadStream : MemoryStream
    {
        private bool _first = true;
        public readonly TaskCompletionSource Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_first) { _first = false; buffer.Span[0] = 42; return 1; }
            Waiting.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }
}
