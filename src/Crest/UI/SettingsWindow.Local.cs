using System;
using System.Collections.Generic;
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
    private Button? _resetDisplay;
    private AppSettings Scoped => _app.Preferences.ForDisplay(_preferenceDisplay);
    private StackPanel PreferenceScope(bool compact = false)
    {
        const string defaults = "All displays (default)";
        var displays = Forms.Screen.AllScreens.Select(x => x.DeviceName).Concat(_app.Preferences.Displays.Keys).Distinct().ToArray();
        var group = new StackPanel();
        var scope = Choice("Preference display", new[] { defaults }.Concat(displays).ToArray(), _preferenceDisplay.Length == 0 ? defaults : _preferenceDisplay,
            value =>
            {
                _preferenceDisplay = value == defaults ? "" : value;
                BuildPage();
            }, save: false);
        scope.SetResourceReference(ItemsControl.ItemTemplateProperty, "SettingsDisplayName");
        string? detail = compact ? null : _preferenceDisplay.Length == 0 ? "Defaults apply to displays without custom settings." : "Changes apply only to this display.";
        AddSetting(group, "Preference display", detail, scope);
        if (_preferenceDisplay.Length > 0)
        {
            var reset = Design.Button("Use defaults for this display", () =>
            {
                _app.Preferences.Displays.Remove(_preferenceDisplay);
                Save();
                BuildPage();
            });
            reset.IsEnabled = _app.Preferences.Displays.ContainsKey(_preferenceDisplay);
            reset.HorizontalAlignment = HorizontalAlignment.Left;
            reset.Margin = new Thickness(0, 8, 0, 0);
            group.Children.Add(reset);
            _resetDisplay = reset;
        }
        return group;
    }

    private static string DisplayLabel(Forms.Screen screen)
        => DisplayNameConverter.Label(screen.DeviceName) + (screen.Primary ? " (primary)" : "");

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
        if (_resetDisplay != null) _resetDisplay.IsEnabled = true;
    }
    private void ModuleList()
    {
        var details = new Dictionary<string, string>
        {
            ["Music"] = "Playback and track details from Windows media sessions.",
            ["Controls"] = "Audio, brightness, battery, network, and system activity.",
            ["Timer"] = "Timers, stopwatch, focus sessions, and reminders.",
            ["Launch"] = "Apps, folders, websites, and shortcuts you choose.",
            ["Shelf"] = "Keep and pin up to 20 file and folder references.",
            ["Capture"] = "Open Windows capture and revisit selected images.",
            ["Workspaces"] = "Launch a saved group of apps, folders, files, and websites.",
            ["Clipboard"] = "Find copied text after enabling capture in Privacy.",
            ["Integrations"] = "Weather, calendars, downloads, local AI, and Windows services."
        };
        var modules = _app.Preferences.ModuleOrder;
        var list = new StackPanel();
        for (int i = 0; i < modules.Count; i++)
        {
            string module = modules[i];
            if (i > 0)
            {
                var divider = new Border { Height = 1, Margin = new Thickness(0, 7, 0, 0) };
                divider.SetResourceReference(Border.BackgroundProperty, "Divider");
                list.Children.Add(divider);
            }
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var toggle = Toggle("Show " + module, _app.Preferences.EnabledModules.Contains(module), enabled =>
            {
                if (enabled && !_app.Preferences.EnabledModules.Contains(module)) _app.Preferences.EnabledModules.Add(module);
                if (!enabled) _app.Preferences.EnabledModules.Remove(module);
            });
            toggle.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(toggle);
            var up = Design.IconButton("\uE74A", "Move " + module + " earlier", () => MoveModule(module, -1));
            up.IsEnabled = i > 0;
            up.Margin = new Thickness(0, 0, 4, 0);
            actions.Children.Add(up);
            var down = Design.IconButton("\uE74B", "Move " + module + " later", () => MoveModule(module, 1));
            down.IsEnabled = i < modules.Count - 1;
            actions.Children.Add(down);
            AddSetting(list, module, details.GetValueOrDefault(module), actions);
        }
        Section("Tools on the island", "Use the switches to show tools and the arrows to set their order.", list);
        Section("Integration sources", "Choose which optional services Crest can use.", LinkTo("Integrations", "Configure integrations"));
    }
    private void MoveModule(string module, int direction)
    {
        var list = _app.Preferences.ModuleOrder;
        int index = list.IndexOf(module), target = index + direction;
        if (index < 0 || target < 0 || target >= list.Count) return;
        (list[index], list[target]) = (list[target], list[index]);
        Save();
        BuildPage();
    }
    private void Privacy()
    {
        Heading("Privacy", "Manage clipboard capture and see what Crest retains in your Windows profile.");
        var clipboard = new StackPanel();
        AddSetting(clipboard, "Capture new text copies", "Turning capture off clears the saved history, including pins.",
            Toggle("Enable text clipboard history", _app.Preferences.ClipboardHistoryEnabled, value => _app.Preferences.ClipboardHistoryEnabled = value));
        AddSetting(clipboard, "Clipboard retention", "Older unpinned entries expire even when capture is paused.",
            Range("Clipboard retention", 1, 30, _app.Preferences.ClipboardRetentionDays, value => _app.Preferences.ClipboardRetentionDays = (int)value, " days"));
        Section("Text clipboard history", "Crest can retain up to 100 new text copies, with up to 8,192 characters per entry. Use the Clipboard module to search, pin, or clear entries.", clipboard);
        Section("Saved file references", "Crest saves pinned shelf paths and capture paths you select. The original files stay where they are.");
    }
    private void Workspaces()
    {
        Heading("Workspaces", "Save groups of up to 20 apps, folders, files, and websites and open them together.");
        var name = new TextBox { MaxLength = 80, Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(name, "New workspace name");
        var creation = new StackPanel();
        var nameLabel = Design.Text("Workspace name", 12, "Muted");
        nameLabel.Margin = new Thickness(0, 0, 0, 4);
        creation.Children.Add(nameLabel);
        creation.Children.Add(name);
        var create = Design.Button("Create workspace", () =>
        {
            if (_app.Preferences.WorkspacePresets.Count >= 20) { SetStatus("You can save up to 20 workspaces."); return; }
            if (string.IsNullOrWhiteSpace(name.Text)) { SetStatus("Enter a workspace name."); return; }
            _app.Preferences.WorkspacePresets.Add(new WorkspacePreset { Name = name.Text.Trim() }); Save(); BuildPage();
        }, true);
        create.HorizontalAlignment = HorizontalAlignment.Left;
        creation.Children.Add(create);
        Section("New workspace", control: creation);
        if (_app.Preferences.WorkspacePresets.Count == 0)
            Section("No saved workspaces", "Create a workspace and add the files, apps, and sites you open together.");
        foreach (var workspace in _app.Preferences.WorkspacePresets.ToArray())
        {
            var stack = new StackPanel();
            var rename = new TextBox { Text = workspace.Name, MaxLength = 80, Margin = new Thickness(0, 0, 0, 8) };
            AutomationProperties.SetName(rename, "Workspace name " + workspace.Name);
            stack.Children.Add(rename);
            var saveName = Design.Button("Save workspace name", () =>
            {
                if (string.IsNullOrWhiteSpace(rename.Text)) { SetStatus("Enter a workspace name."); return; }
                workspace.Name = rename.Text.Trim();
                Save();
                BuildPage();
            });
            saveName.HorizontalAlignment = HorizontalAlignment.Left;
            saveName.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(saveName);
            foreach (var target in workspace.Targets.ToArray())
            {
                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
                var remove = Design.Button("Remove", () => { workspace.Targets.Remove(target); Save(); BuildPage(); });
                AutomationProperties.SetName(remove, "Remove " + target.Name + " from " + workspace.Name);
                DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
                var targetLabel = new StackPanel { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                targetLabel.Children.Add(Design.Text(target.Name, 13, "Text", true));
                var path = Design.Text(target.Target, 12, "Muted");
                path.TextTrimming = TextTrimming.CharacterEllipsis;
                path.TextWrapping = TextWrapping.NoWrap;
                path.ToolTip = target.Target;
                targetLabel.Children.Add(path);
                row.Children.Add(targetLabel);
                stack.Children.Add(row);
            }
            var add = new WrapPanel();
            add.Children.Add(Design.Button("Add app or file", () => { var dialog = new OpenFileDialog { Title = "Add workspace app or file", CheckFileExists = true }; if (dialog.ShowDialog(this) == true) AddWorkspaceTarget(workspace, dialog.FileName, "App"); }));
            add.Children.Add(Design.Button("Add folder", () => { var dialog = new OpenFolderDialog { Title = "Add workspace folder" }; if (dialog.ShowDialog(this) == true) AddWorkspaceTarget(workspace, dialog.FolderName, "Folder"); }));
            stack.Children.Add(add);
            PanelLayout.SpaceActions(add);
            var url = new TextBox { MaxLength = 4096, Margin = new Thickness(0, 4, 0, 8) };
            AutomationProperties.SetName(url, "Website address for " + workspace.Name);
            stack.Children.Add(Design.Text("Website address", 12, "Muted"));
            stack.Children.Add(url);
            var addWebsite = Design.Button("Add website", () =>
            {
                if (!Uri.TryCreate(url.Text.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) { SetStatus("Enter an HTTP or HTTPS website address."); return; }
                AddWorkspaceTarget(workspace, uri.AbsoluteUri, "Website");
            });
            addWebsite.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(addWebsite);
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            var launch = Design.Button("Launch workspace", () => _app.OpenWorkspace(workspace));
            AutomationProperties.SetName(launch, "Launch " + workspace.Name);
            launch.IsEnabled = workspace.Targets.Count > 0;
            actions.Children.Add(launch);
            actions.Children.Add(Design.Button("Remove workspace", () => { _app.Preferences.WorkspacePresets.Remove(workspace); Save(); BuildPage(); }));
            PanelLayout.SpaceActions(actions);
            stack.Children.Add(actions); Section(workspace.Name, workspace.Targets.Count + " / 20 targets", stack);
        }
    }
    private void AddWorkspaceTarget(WorkspacePreset workspace, string path, string kind)
    {
        if (workspace.Targets.Count >= 20) { SetStatus("This workspace holds up to 20 targets."); return; }
        if (workspace.Targets.Any(x => string.Equals(x.Target, path, StringComparison.OrdinalIgnoreCase))) { SetStatus("This target is already in the workspace."); return; }
        string label = kind == "Website" ? new Uri(path).Host : Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        workspace.Targets.Add(new LauncherEntry { Name = label.Length == 0 ? path : label, Target = path, Kind = kind }); Save(); BuildPage();
    }
}
