using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Crest.Core;
using Crest.Features;
using Microsoft.Win32;

namespace Crest.UI;

public sealed partial class IslandWindow
{
    private void BuildCaptures()
    {
        _body.Children.Add(Design.Text("Use Windows to take a screenshot. Select saved images to keep their references here.", 12, "Muted"));
        _body.Children.Add(Design.Button("Take a screenshot", () => _app.OpenWindowsTool("ms-screenclip:"), true));
        _body.Children.Add(Design.Button("Select recent captures", SelectCaptures));
        var captures = _app.Workflows.Captures;
        _body.Children.Add(Design.Text(captures.Count + " / 12 selected references", 11, "Muted"));
        foreach (var capture in captures)
        {
            var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var remove = Design.IconButton("\uE711", "Remove capture reference " + capture.Name, () => { _app.Workflows.RemoveCapture(capture.Id); BuildBody(); });
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            var open = Design.Button(capture.Name + (capture.Available ? "" : " · missing"), () => _ = _app.OpenTarget(() => _app.Local.Launch(new LauncherEntry { Target = capture.Path })));
            Design.RegisterTooltip(open, capture.Path); row.Children.Add(open); _body.Children.Add(row);
            _body.Children.Add(Design.Text("Selected " + capture.SelectedUtc.ToLocalTime().ToString("g"), 10, "Muted"));
        }
        if (captures.Count > 0) _body.Children.Add(Design.Button("Clear capture references", () => { _app.Workflows.ClearCaptures(); BuildBody(); }));
    }
    private void SelectCaptures()
    {
        _app.Controller.Dismiss(); Suppress();
        var dialog = new OpenFileDialog
        {
            Title = "Select saved screenshot references", Multiselect = true, CheckFileExists = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
            InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots")
        };
        if (dialog.ShowDialog() == true && _app.Workflows.SelectCaptures(dialog.FileNames) is string error) ShowMessage(error);
        _app.Policy.Recheck();
    }
    private void BuildWorkspaces()
    {
        _body.Children.Add(Design.Text("Launch all the targets in a preset. Manage names and targets in Settings → Workspaces.", 12, "Muted"));
        foreach (var workspace in _app.Preferences.WorkspacePresets)
        {
            var button = Design.Button("Launch " + workspace.Name, () => _app.OpenWorkspace(workspace), true);
            button.Margin = new Thickness(0, 8, 0, 0); _body.Children.Add(button);
            _body.Children.Add(Design.Text(workspace.Targets.Count + " targets · " + string.Join(", ", workspace.Targets.Select(x => x.Name)), 11, "Muted"));
        }
        _body.Children.Add(Design.Button("Manage workspaces in Settings", _app.OpenSettings));
    }
    private void RelinkShelf(ShelfItem item)
    {
        _app.Controller.Dismiss(); Suppress();
        string? replacement = null;
        if (item.IsDirectory)
        {
            var dialog = new OpenFolderDialog { Title = "Locate " + item.Name, InitialDirectory = _app.Local.ShelfRecoveryFolder(item.Id) ?? "" };
            if (dialog.ShowDialog() == true) replacement = dialog.FolderName;
        }
        else
        {
            var dialog = new OpenFileDialog { Title = "Locate " + item.Name, CheckFileExists = true, InitialDirectory = _app.Local.ShelfRecoveryFolder(item.Id) ?? "" };
            if (dialog.ShowDialog() == true) replacement = dialog.FileName;
        }
        if (replacement != null && _app.Local.RelinkShelf(item.Id, replacement) is string error) ShowMessage(error);
        _app.Policy.Recheck();
    }
}
