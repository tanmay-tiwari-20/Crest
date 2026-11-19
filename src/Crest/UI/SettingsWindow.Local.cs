using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Crest.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Crest.UI;

public sealed partial class SettingsWindow
{
    private string _preferenceDisplay = "";
    private AppSettings Scoped => _app.Preferences.ForDisplay(_preferenceDisplay);
    private void PreferenceScope()
    {
        const string defaults = "All displays (default)";
        var displays = Forms.Screen.AllScreens.Select(x => x.DeviceName).Concat(_app.Preferences.Displays.Keys).Distinct().ToArray();
        Section("Preference display", "Appearance and trigger settings can be saved separately for each display. Uncustomized displays use the defaults.",
            Choice("Preference display", new[] { defaults }.Concat(displays).ToArray(), _preferenceDisplay == "" ? defaults : _preferenceDisplay,
                value => { _preferenceDisplay = value == defaults ? "" : value; BuildPage(); }));
        if (_preferenceDisplay != "" && _app.Preferences.Displays.ContainsKey(_preferenceDisplay))
            _page.Children.Add(Design.Button("Use defaults for this display", () => { _app.Preferences.Displays.Remove(_preferenceDisplay); Save(); BuildPage(); }));
    }
    private void ChangeScoped(Action<AppSettings> change)
    {
        if (_preferenceDisplay == "") { change(_app.Preferences); return; }
        var display = _app.Preferences.CustomizeDisplay(_preferenceDisplay);
        var settings = Scoped;
        change(settings);
        display.Theme = settings.Theme; display.Motion = settings.Motion;
        display.SizeScale = settings.SizeScale; display.MotionSpeed = settings.MotionSpeed;
        display.FullWidthTrigger = settings.FullWidthTrigger; display.TriggerWidth = settings.TriggerWidth;
        display.TriggerDepth = settings.TriggerDepth; display.DwellMs = settings.DwellMs; display.GraceMs = settings.GraceMs;
    }
    private void ModuleOrdering()
    {
        var stack = new StackPanel();
        foreach (string module in _app.Preferences.ModuleOrder)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var down = Design.Button("↓", () => MoveModule(module, 1));
            AutomationProperties.SetName(down, "Move " + module + " later");
            down.IsEnabled = _app.Preferences.ModuleOrder.IndexOf(module) < _app.Preferences.ModuleOrder.Count - 1;
            DockPanel.SetDock(down, Dock.Right); row.Children.Add(down);
            var up = Design.Button("↑", () => MoveModule(module, -1));
            AutomationProperties.SetName(up, "Move " + module + " earlier");
            up.IsEnabled = _app.Preferences.ModuleOrder.IndexOf(module) > 0;
            DockPanel.SetDock(up, Dock.Right); row.Children.Add(up);
            row.Children.Add(Design.Text(module, 13)); stack.Children.Add(row);
        }
        Section("Module order", "This order controls the icon dock and pointer navigation.", stack);
    }
    private void MoveModule(string module, int direction)
    {
        var list = _app.Preferences.ModuleOrder;
        int index = list.IndexOf(module), target = index + direction;
        if (index < 0 || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]); Save(); BuildPage();
    }
    private void Privacy()
    {
        Heading("Keep only what you choose.", "Clipboard capture is off by default. All retained data stays in your Windows user profile.");
        Section("Text clipboard history", "Capture new text copies while Crest runs. Turning this off clears retained text. History is limited to 100 entries and 8,192 characters per entry; pinned entries remain until removed or cleared.",
            Toggle("Enable text clipboard history", _app.Preferences.ClipboardHistoryEnabled, value => _app.Preferences.ClipboardHistoryEnabled = value));
        Range("Clipboard retention", "Unpinned entries older than this are removed, including while capture is paused.", 1, 30, _app.Preferences.ClipboardRetentionDays, value => _app.Preferences.ClipboardRetentionDays = (int)value, " days");
        Section("Manage history", "Search, pin, pause capture, copy text, remove an entry, or clear everything in the Clipboard module. Clearing also removes pins. Existing clipboard text is not imported when capture is enabled or resumed.");
        Section("Reference storage", "Only pinned shelf paths and explicitly selected recent capture paths survive a restart. Crest never copies, moves, or deletes your source files.");
    }
    private void Workspaces()
    {
        Heading("Open a workspace together.", "Save groups of up to 20 apps, folders, files, and websites. A preset launches only when you select it.");
        var name = new TextBox { MaxLength = 80, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(name, "New workspace name");
        var creation = new StackPanel(); creation.Children.Add(name);
        creation.Children.Add(Design.Button("Create workspace", () =>
        {
            if (_app.Preferences.WorkspacePresets.Count >= 20) { _message.Text = "You can save up to 20 workspaces."; return; }
            if (string.IsNullOrWhiteSpace(name.Text)) { _message.Text = "Enter a workspace name."; return; }
            _app.Preferences.WorkspacePresets.Add(new WorkspacePreset { Name = name.Text.Trim() }); Save(); BuildPage();
        }, true));
        Section("New workspace", "Give this group a recognizable name.", creation);
        foreach (var workspace in _app.Preferences.WorkspacePresets.ToArray())
        {
            var stack = new StackPanel();
            var rename = new TextBox { Text = workspace.Name, MaxLength = 80, Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetName(rename, "Workspace name " + workspace.Name);
            stack.Children.Add(rename);
            stack.Children.Add(Design.Button("Save workspace name", () => { if (string.IsNullOrWhiteSpace(rename.Text)) { _message.Text = "Enter a workspace name."; return; } workspace.Name = rename.Text.Trim(); Save(); BuildPage(); }));
            foreach (var target in workspace.Targets.ToArray())
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                var remove = Design.Button("Remove", () => { workspace.Targets.Remove(target); Save(); BuildPage(); });
                AutomationProperties.SetName(remove, "Remove " + target.Name + " from " + workspace.Name);
                DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                row.Children.Add(Design.Text(target.Name + "\n" + target.Target, 11, "Muted")); stack.Children.Add(row);
            }
            var add = new WrapPanel();
            add.Children.Add(Design.Button("Add app or file", () => { var dialog = new OpenFileDialog { Title = "Add workspace app or file", CheckFileExists = true }; if (dialog.ShowDialog(this) == true) AddWorkspaceTarget(workspace, dialog.FileName, "App"); }));
            add.Children.Add(Design.Button("Add folder", () => { var dialog = new OpenFolderDialog { Title = "Add workspace folder" }; if (dialog.ShowDialog(this) == true) AddWorkspaceTarget(workspace, dialog.FolderName, "Folder"); }));
            stack.Children.Add(add);
            var url = new TextBox { MaxLength = 4096, Margin = new Thickness(0, 8, 0, 8) };
            AutomationProperties.SetName(url, "Website address for " + workspace.Name); stack.Children.Add(url);
            stack.Children.Add(Design.Button("Add website", () =>
            {
                if (!Uri.TryCreate(url.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) { _message.Text = "Enter an HTTP or HTTPS website address."; return; }
                AddWorkspaceTarget(workspace, uri.AbsoluteUri, "Website");
            }));
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            actions.Children.Add(Design.Button("Launch " + workspace.Name, () => _app.OpenWorkspace(workspace)));
            actions.Children.Add(Design.Button("Remove workspace", () => { _app.Preferences.WorkspacePresets.Remove(workspace); Save(); BuildPage(); }));
            stack.Children.Add(actions); Section(workspace.Name, workspace.Targets.Count + " / 20 targets", stack);
        }
    }
    private void AddWorkspaceTarget(WorkspacePreset workspace, string path, string kind)
    {
        if (workspace.Targets.Count >= 20) { _message.Text = "This workspace holds up to 20 targets."; return; }
        if (workspace.Targets.Any(x => string.Equals(x.Target, path, StringComparison.OrdinalIgnoreCase))) { _message.Text = "This target is already in the workspace."; return; }
        string label = kind == "Website" ? new Uri(path).Host : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        workspace.Targets.Add(new LauncherEntry { Name = label.Length == 0 ? path : label, Target = path, Kind = kind }); Save(); BuildPage();
    }
}
