using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;
using Crest.Features;
using Crest.Platform;
using Crest.UI;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

/// <summary>
/// Calls the native OLE target of a test-owned HWND with real CF_HDROP data.
/// These component checks do not exercise a physical desktop gesture or shell policy.
/// </summary>
internal static class NativeShelfDropChecks
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint(int x, int y)
    {
        public readonly int X = x;
        public readonly int Y = y;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnterOrDrop(IntPtr target, IntPtr data, uint keys, NativePoint point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Over(IntPtr target, uint keys, NativePoint point, ref uint effect);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int Leave(IntPtr target);

    [DllImport("user32.dll", EntryPoint = "GetPropW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    public static async Task Run(Crest.App app, LocalHub hub, AppSettings settings, Action<bool, string> check)
    {
        app.Dispatcher.VerifyAccess();
        string tempParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath()));
        string directory = Path.Combine(tempParent, "CrestNativeShelf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string originalMotion = settings.Motion;
        var originalModules = settings.EnabledModules;
        Window? originalMainWindow = app.MainWindow;
        string[] propertyNames = [nameof(Crest.App.Preferences), nameof(Crest.App.Store), nameof(Crest.App.Local), nameof(Crest.App.Hardware), nameof(Crest.App.Policy), nameof(Crest.App.Controller)];
        var originalProperties = propertyNames.ToDictionary(name => name, name => typeof(Crest.App).GetProperty(name)!.GetValue(app));
        var appIslandField = typeof(Crest.App).GetField("_island", BindingFlags.NonPublic | BindingFlags.Instance)!;
        object? originalIsland = appIslandField.GetValue(app);
        IntPtr foreground = GetForegroundWindow();
        var store = new SettingsStore(Path.Combine(directory, "state"));
        using var local = new LocalHub(store, () => settings, new WindowsTimerClock(), automaticTick: false);
        using var hardware = new HardwareHub();
        hardware.Dispose(); // No hardware/media activity is needed for file-drop checks.
        using var policy = new DesktopPolicy(() => settings); // Deliberately never Start.
        IslandWindow? island = null;
        InteractionController? controller = null;
        NativeTarget? target = null;
        try
        {
            settings.Motion = "Reduced";
            settings.EnabledModules = originalModules.Concat(["Shelf"]).Distinct().ToList();
            SetApp(app, nameof(Crest.App.Preferences), settings);
            SetApp(app, nameof(Crest.App.Store), store);
            SetApp(app, nameof(Crest.App.Local), local);
            SetApp(app, nameof(Crest.App.Hardware), hardware);
            SetApp(app, nameof(Crest.App.Policy), policy);
            island = new IslandWindow(app);
            controller = new InteractionController(policy, () => settings, () => island.PixelBounds, () => island.HoldsInteraction, island.ContainsVisiblePoint);
            SetApp(app, nameof(Crest.App.Controller), controller);
            appIslandField.SetValue(app, island);
            IntPtr window = new WindowInteropHelper(island).EnsureHandle();
            RequireOwnedWindow(window);
            SetEligibility(policy, true);
            var machine = Field<InteractionStateMachine>(controller, "_machine");
            machine.SetEligibility(true, false);
            machine.RevealAccessibility();
            island.Reveal(controller.TransitionVersion);
            await Task.Delay(160);
            island.Expand("Shelf");
            await Task.Delay(160);
            island.UpdateLayout();
            target = await OpenNativeTarget(island, window);
            SetEligibility(policy, true);

            var surface = Field<FrameworkElement>(island, "_surface");
            var clip = Field<RectangleGeometry>(island, "_clip").Rect;
            Point screen = surface.PointToScreen(new Point(clip.X + clip.Width / 2, clip.Y + clip.Height / 2));
            var point = new NativePoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
            if (!island.ContainsVisiblePoint(new System.Drawing.Point(point.X, point.Y)))
                throw new InvalidOperationException("Native OLE fixture point is outside its visible, test-owned island.");

            async Task<bool> Case(string name, DragDropEffects allowed, DragDropEffects expected, bool record = true)
            {
                string file = Path.Combine(directory, name + ".txt");
                await File.WriteAllTextAsync(file, "Crest native OLE scratch data: " + name);
                byte[] originalHash = SHA256.HashData(File.ReadAllBytes(file));
                int before = local.Shelf.Count;
                var data = new DataObject(DataFormats.FileDrop, new[] { file });
                IntPtr nativeData = Marshal.GetComInterfaceForObject(data, typeof(ComDataObject));
                try
                {
                    foreach (uint effect in target!.DropSequence(nativeData, point, allowed))
                        if (effect != (uint)expected || (effect & (uint)DragDropEffects.Move) != 0)
                            throw new InvalidOperationException($"Native OLE {name} returned effect {effect}; expected {(uint)expected} without Move.");
                    if (expected != DragDropEffects.None)
                    {
                        for (int attempt = 0; attempt < 100 && (local.Shelf.Count == before || Field<bool>(island!, "_shelfDropPending")); attempt++)
                            await Task.Delay(20);
                    }
                    else
                        await Task.Delay(80);
                    bool imported = expected == DragDropEffects.None
                        ? local.Shelf.Count == before
                        : local.Shelf.Count == before + 1 && local.Shelf.Any(item => item.Path == file && item.Available);
                    bool preserved = File.Exists(file) && SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(originalHash);
                    bool passed = imported && preserved;
                    if (record)
                        check(passed, expected == DragDropEffects.None
                            ? "Native CF_HDROP rejects Move-only source effects without importing or modifying the original"
                            : $"Native CF_HDROP negotiates {expected} synchronously and imports a reference without modifying the original");
                    return passed;
                }
                finally
                {
                    Marshal.Release(nativeData);
                    GC.KeepAlive(data);
                }
            }

            await Case("all-effects", DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link, DragDropEffects.Link);
            await Case("copy-only", DragDropEffects.Copy, DragDropEffects.Copy);
            await Case("move-only", DragDropEffects.Move, DragDropEffects.None);

            // Both guards operate on this fixture only, with no actual shell eligibility claim.
            settings.EnabledModules.Remove("Shelf");
            bool disabled = await Case("disabled", DragDropEffects.Copy | DragDropEffects.Link, DragDropEffects.None, record: false);
            settings.EnabledModules.Add("Shelf");
            SetEligibility(policy, false);
            bool ineligible = await Case("ineligible", DragDropEffects.Copy | DragDropEffects.Link, DragDropEffects.None, record: false);
            check(disabled && ineligible && GetForegroundWindow() == foreground, "Native CF_HDROP rejects disabled and ineligible targets while preserving originals and foreground focus");
            GC.KeepAlive(hub); // Caller productivity/shelf state is never used or disposed by this fixture.
        }
        finally
        {
            target?.Dispose();
            island?.Suppress();
            controller?.Dispose();
            island?.Close();
            settings.Motion = originalMotion;
            settings.EnabledModules = originalModules;
            foreach (var property in originalProperties)
                SetApp(app, property.Key, property.Value);
            appIslandField.SetValue(app, originalIsland);
            app.MainWindow = originalMainWindow;
            // A unique, verified test-owned path contains all files and state from these checks.
            local.Dispose();
            string resolved = Path.GetFullPath(directory);
            if (resolved.StartsWith(tempParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(resolved).StartsWith("CrestNativeShelf-", StringComparison.Ordinal))
                Directory.Delete(resolved, recursive: true);
        }
    }

    private static async Task<NativeTarget> OpenNativeTarget(IslandWindow island, IntPtr window)
    {
        int lastResult = 0;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            RequireOwnedWindow(window);
            IntPtr registered = GetProp(window, "OleDropTargetInterface");
            if (registered != IntPtr.Zero)
            {
                Guid iid = new("00000122-0000-0000-C000-000000000046");
                lastResult = Marshal.QueryInterface(registered, in iid, out IntPtr target);
                if (lastResult >= 0 && target != IntPtr.Zero)
                    return new NativeTarget(target);
            }
            if (attempt == 1)
            {
                island.AllowDrop = false;
                island.AllowDrop = true;
            }
            island.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        throw new NotSupportedException($"Test-owned HWND did not expose an OLE IDropTarget after three registration checks (HRESULT 0x{lastResult:X8}). Routed UI checks remain separate evidence.");
    }

    private sealed class NativeTarget : IDisposable
    {
        private IntPtr _pointer;
        private readonly EnterOrDrop _enter, _drop;
        private readonly Over _over;
        private readonly Leave _leave;

        public NativeTarget(IntPtr pointer)
        {
            _pointer = pointer;
            IntPtr table = Marshal.ReadIntPtr(pointer);
            _enter = Marshal.GetDelegateForFunctionPointer<EnterOrDrop>(Marshal.ReadIntPtr(table, 3 * IntPtr.Size));
            _over = Marshal.GetDelegateForFunctionPointer<Over>(Marshal.ReadIntPtr(table, 4 * IntPtr.Size));
            _leave = Marshal.GetDelegateForFunctionPointer<Leave>(Marshal.ReadIntPtr(table, 5 * IntPtr.Size));
            _drop = Marshal.GetDelegateForFunctionPointer<EnterOrDrop>(Marshal.ReadIntPtr(table, 6 * IntPtr.Size));
        }

        public uint[] DropSequence(IntPtr data, NativePoint point, DragDropEffects allowed)
        {
            var effects = new uint[3];
            bool entered = false;
            try
            {
                uint effect = (uint)allowed;
                Marshal.ThrowExceptionForHR(_enter(_pointer, data, 1, point, ref effect));
                entered = true;
                effects[0] = effect;
                effect = (uint)allowed;
                Marshal.ThrowExceptionForHR(_over(_pointer, 1, point, ref effect));
                effects[1] = effect;
                effect = (uint)allowed;
                Marshal.ThrowExceptionForHR(_drop(_pointer, data, 0, point, ref effect));
                entered = false;
                effects[2] = effect;
                return effects;
            }
            finally
            {
                if (entered) Marshal.ThrowExceptionForHR(_leave(_pointer));
            }
        }

        public void Dispose()
        {
            if (_pointer == IntPtr.Zero) return;
            Marshal.Release(_pointer);
            _pointer = IntPtr.Zero;
        }
    }

    private static void RequireOwnedWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out uint owner) == 0 || owner != (uint)Environment.ProcessId)
            throw new InvalidOperationException("Native OLE checks may target only their own fixture HWND.");
    }
    private static void SetEligibility(DesktopPolicy policy, bool eligible) => typeof(DesktopPolicy).GetProperty(nameof(DesktopPolicy.IsEligible))!.SetValue(policy, eligible);
    private static void SetApp(Crest.App app, string name, object? value) => typeof(Crest.App).GetProperty(name)!.SetValue(app, value);
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
}
