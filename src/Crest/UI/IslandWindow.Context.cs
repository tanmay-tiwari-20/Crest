using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Crest.Core;

namespace Crest.UI;

public sealed partial class IslandWindow
{
    private bool _detailSizeQueued;
    private void ApplyIslandResources()
    {
        var aliases = new[] { ("Canvas", "IslandShell"), ("Surface", "IslandShell"), ("Raised", "IslandCard"), ("Inset", "IslandInput"),
            ("Text", "IslandText"), ("Muted", "IslandMuted"), ("Subtle", "IslandMuted"), ("Line", "IslandLine"), ("Divider", "IslandOutline"),
            ("Hover", "IslandHover"), ("Selected", "IslandCard"), ("Accent", "IslandAccent"), ("AccentFill", "IslandAccent"),
            ("AccentText", "IslandActionText"), ("Warning", "IslandAccentNotice"), ("Danger", "IslandAccentNotice") };
        foreach (var (local, shared) in aliases)
        {
            var brush = Design.Brush(shared);
            if (!ReferenceEquals(_shell.Resources[local], brush)) _shell.Resources[local] = brush;
        }
    }

    private void ReadActivities()
    {
        var previous = _selectedActivity;
        _activities = IslandActivityCatalog.Read(_app, _shownBatteryNotice);
        _selectedActivity = _selectedActivityId == null ? _activities.FirstOrDefault() : _activities.FirstOrDefault(item => item.Id == _selectedActivityId);
        bool ended = _selectedActivityId != null && _selectedActivity == null && previous != null;
        if (_selectedActivity == null) { _selectedActivityId = null; _selectedActivity = _activities.FirstOrDefault(); }
        _compactRect = _selectedActivity == null ? IdleRect : CompactRect;
        if (ended && _presentation == IslandPresentation.Activity)
        {
            _module = "Overview";
            TransitionTo(IslandPresentation.Compact, _compactRect);
        }
        UpdateCompactActivity();
        if (_presentation == IslandPresentation.Compact && _targetRect != _compactRect)
        {
            AnimateShape(_compactRect);
            MoveHeader(_compactRect);
        }
        UpdateSecondaryActivities();
    }

    private void UpdateCompactActivity()
    {
        var activity = _selectedActivity;
        string title = activity?.Title ?? "crest";
        string hint = activity == null ? "Idle"
            : activity.Kind == IslandActivityKind.Music ? activity.Subtitle
            : _presentation == IslandPresentation.Activity ? activity.Kind switch
            {
                IslandActivityKind.Reminder => "Due reminder",
                IslandActivityKind.Download => "Managed download",
                IslandActivityKind.Ai => "Local model",
                _ => activity.Subtitle
            }
            : activity.Phase == "Completed" || activity.Kind == IslandActivityKind.Reminder ? activity.Subtitle
            : activity.Kind == IslandActivityKind.BatteryWarning ? "Windows reported " + activity.Value
            : activity.Phase + " · " + activity.Value;
        Set("compactTitle", title);
        Set("compactDetail", hint);
        string name = activity == null ? "Open tools" : "Expand " + activity.Kind.ToString().ToLowerInvariant() + ", " + title + ", " + activity.Subtitle + ", " + activity.Value;
        AutomationProperties.SetName(_compactDetails, name);
        AutomationProperties.SetName(_compact, title + ", " + hint);
        Design.RegisterTooltip(_compactDetails, activity == null ? "Open your tools" : title + " · " + activity.Value);
        _compactGlyph.Text = activity?.Glyph ?? "\uE80F";
        _compactGlyph.SetResourceReference(TextBlock.ForegroundProperty, activity?.AccentKey ?? "IslandAccent");
        _compactBrand.Visibility = activity == null ? Visibility.Visible : Visibility.Collapsed;
        _compactGlyph.Visibility = activity == null ? Visibility.Collapsed : Visibility.Visible;
        _compactArt.Source = activity?.Kind == IslandActivityKind.Music ? _app.Hardware.Music.Artwork : null;
        _compactArt.Visibility = _compactArt.Source == null ? Visibility.Collapsed : Visibility.Visible;
        bool action = activity?.CanInvoke == true && _presentation == IslandPresentation.Compact;
        _compactPlay.Visibility = action ? Visibility.Visible : Visibility.Collapsed;
        _compactPlay.IsEnabled = action && !_compactActionBusy && !IslandActivityCatalog.IsActionBusy(_app);
        _compact.ColumnDefinitions[2].Width = new GridLength(action ? 40 : 0);
        string actionName = activity == null ? "No activity action available"
            : activity.Kind == IslandActivityKind.BatteryWarning ? "Open laptop controls"
            : activity.Kind == IslandActivityKind.Reminder ? "Open due reminders"
            : activity.Kind == IslandActivityKind.Timer && activity.Phase == "Completed" ? "Open completed timers"
            : activity.Kind == IslandActivityKind.Focus && activity.Phase == "Completed" ? "Open completed focus session"
            : activity.PrimaryActionName;
        AutomationProperties.SetName(_compactPlay, actionName);
        Design.RegisterTooltip(_compactPlay, actionName);
        if (_compactPlay.Content is TextBlock glyph) glyph.Text = activity?.PrimaryActionGlyph ?? "\uE768";
        _compactPlay.SetResourceReference(Control.ForegroundProperty, activity?.AccentKey ?? "IslandText");
        string openName = _presentation == IslandPresentation.Compact ? activity == null ? "Open tools" : "Expand current activity" : "Collapse island";
        AutomationProperties.SetName(_compactOpen, openName);
        Design.RegisterTooltip(_compactOpen, openName);
        if (_compactOpen.Content is TextBlock arrow) arrow.Text = _presentation == IslandPresentation.Compact ? "\uE70D" : "\uE70E";
    }

    private void BuildSecondaryActivities()
    {
        for (int i = 0; i < _secondary.Length; i++)
        {
            int slot = i;
            var button = Design.Button("", () =>
            {
                if (_secondary[slot].Tag is not IslandActivity activity || !_activities.Any(item => item.Id == activity.Id)) return;
                _selectedActivityId = activity.Id;
                OpenContext();
            });
            button.Style = (Style)FindResource("CircleButton");
            button.Width = button.Height = button.MinWidth = button.MinHeight = 44;
            button.Padding = new Thickness(3);
            button.BorderThickness = new Thickness(1);
            button.SetResourceReference(Control.BackgroundProperty, "IslandShell");
            button.SetResourceReference(Control.BorderBrushProperty, "IslandOutline");
            button.HorizontalAlignment = HorizontalAlignment.Left;
            button.VerticalAlignment = VerticalAlignment.Top;
            button.Margin = new Thickness(_secondaryRects[i].X, _secondaryRects[i].Y, 0, 0);
            button.Visibility = Visibility.Collapsed;
            _secondary[i] = button;
            _surface.Children.Add(button);
        }
    }

    private void UpdateSecondaryActivities()
    {
        var secondary = _activities.Where(item => item.Id != _selectedActivity?.Id && item.Kind != _selectedActivity?.Kind)
            .GroupBy(item => item.Kind).Select(group => group.First()).Take(_secondary.Length).ToArray();
        for (int i = 0; i < _secondary.Length; i++)
        {
            var button = _secondary[i];
            bool visible = _presentation == IslandPresentation.Compact && i < secondary.Length;
            button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            button.IsHitTestVisible = visible;
            button.Opacity = 1;
            if (!visible) { button.Tag = null; continue; }
            var activity = secondary[i];
            button.Tag = activity;
            bool numeric = activity.Kind is IslandActivityKind.Timer or IslandActivityKind.Focus || activity.Kind == IslandActivityKind.Download && activity.Progress.HasValue;
            string value = activity.Kind == IslandActivityKind.Download && activity.Progress is double progress
                ? Math.Round(Math.Clamp(progress, 0, 1) * 100).ToString(System.Globalization.CultureInfo.CurrentCulture) + "%"
                : BubbleValue(activity.Value);
            var content = Design.Text(numeric ? value : activity.Glyph, 11, activity.AccentKey, true);
            if (!numeric) { content.FontFamily = new FontFamily("Segoe Fluent Icons"); content.FontSize = 17; }
            content.TextAlignment = TextAlignment.Center;
            content.VerticalAlignment = VerticalAlignment.Center;
            content.TextWrapping = TextWrapping.NoWrap;
            button.Content = content;
            AutomationProperties.SetName(button, "Expand " + activity.Kind.ToString().ToLowerInvariant() + ", " + activity.Title + ", " + activity.Value);
            Design.RegisterTooltip(button, activity.Title + " · " + activity.Value);
        }
    }

    private static string BubbleValue(string value)
    {
        var parts = value.Split(':');
        return parts.Length > 2 ? parts[0] + "h" : value.Length <= 5 ? value : value[..4];
    }

    private void OpenContext()
    {
        if (!IsVisible && !_rendering) return;
        bool keyboard = IsVisible && IsKeyboardFocusWithin;
        ReadActivities();
        if (_selectedActivity == null) { ShowTools(); return; }
        _selectedActivityId = _selectedActivity.Id;
        _module = _selectedActivity.Module;
        _expanded.ShowActivity(_selectedActivity);
        TransitionTo(IslandPresentation.Activity, ContextRect());
        if (keyboard) FocusForKeyboard();
    }

    private void OpenActivityDetails(IslandActivity activity)
    {
        Expand(activity.Module);
        SelectActivityDetails(activity);
        UpdateDetailSize();
    }

    private void SelectActivityDetails(IslandActivity? activity)
    {
        if (activity == null || activity.Module != _module) return;
        if (activity.Kind == IslandActivityKind.Reminder) _timerTools?.ShowReminders();
        else if (activity.Kind == IslandActivityKind.Focus) _timerTools?.ShowFocusSessions();
        else if (activity.Kind == IslandActivityKind.Timer && activity.EntityId.Length > 0) _app.Local.SelectTimer(activity.EntityId);
        else if (activity.Kind == IslandActivityKind.Download) _integrationTools?.ShowSection("Downloads");
        else if (activity.Kind == IslandActivityKind.Ai) _integrationTools?.ShowSection("AI");
    }

    private void ShowTools()
    {
        if (!IsVisible && !_rendering) return;
        bool keyboard = IsVisible && IsKeyboardFocusWithin;
        _module = "Tools";
        ClosePlayerPopup();
        BuildTabs();
        _tabs.Measure(new Size(400, double.PositiveInfinity));
        double height = Math.Clamp(Math.Ceiling(_tabs.DesiredSize.Height) + 36, 166, 528);
        _dock.Height = height;
        TransitionTo(IslandPresentation.Tools, new Rect(20, 0, 440, height));
        if (keyboard) FocusForKeyboard();
    }

    private Rect ContextRect() => new(52, 0, 376, Math.Clamp(Math.Ceiling(68 + _expanded.TargetHeight), 128, 500));
    private void UpdateContextSize()
    {
        if (_presentation != IslandPresentation.Activity) return;
        var target = ContextRect();
        if (_targetRect != target) AnimateShape(target);
    }
    private Rect GetDetailRect()
    {
        _body.Measure(new Size(396, double.PositiveInfinity));
        double height = Math.Clamp(Math.Ceiling(_body.DesiredSize.Height) + 98, 206, 528);
        _detail.Height = height;
        return new Rect(20, 0, 440, height);
    }
    private void UpdateDetailSize()
    {
        if (_presentation != IslandPresentation.Detail) return;
        var target = GetDetailRect();
        if (_targetRect != target) AnimateShape(target);
    }
    private void QueueDetailSizeUpdate()
    {
        if (_presentation != IslandPresentation.Detail || _detailSizeQueued || Dispatcher.HasShutdownStarted) return;
        _detailSizeQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            _detailSizeQueued = false;
            UpdateDetailSize();
        }));
    }

    private FrameworkElement View(IslandPresentation presentation) => presentation switch
    {
        IslandPresentation.Activity => _expanded,
        IslandPresentation.Tools => _dock,
        IslandPresentation.Detail => _detail,
        _ => _compact
    };

    private void TransitionTo(IslandPresentation presentation, Rect target, bool animated = true)
    {
        int generation = ++_viewGeneration;
        var previous = _presentation;
        _presentation = presentation;
        _wide = presentation != IslandPresentation.Compact;
        bool immediate = !animated || _rendering || Design.ReducedMotion(_app.EffectivePreferences);
        bool header = presentation is IslandPresentation.Compact or IslandPresentation.Activity;
        foreach (var state in new[] { IslandPresentation.Activity, IslandPresentation.Tools, IslandPresentation.Detail })
        {
            var view = View(state);
            bool show = state == presentation;
            view.IsHitTestVisible = show;
            if (show && view.Visibility != Visibility.Visible) { view.Opacity = 0; view.Visibility = Visibility.Visible; }
            if (immediate)
            {
                _motion.Stop(view, OpacityProperty);
                view.Opacity = show ? 1 : 0;
                view.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show && animated && !_rendering)
                {
                    var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(60));
                    fade.Completed += (_, _) => { if (generation == _viewGeneration) { ((IAnimatable)view).BeginAnimation(OpacityProperty, null); AcknowledgeShownBatteryNotice(); } };
                    ((IAnimatable)view).BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
                }
            }
            else if (view.Visibility == Visibility.Visible)
            {
                _motion.Double(view, OpacityProperty, show ? 1 : 0, _app.EffectivePreferences.MotionSpeed, show ? .22 : .10, completed: () =>
                {
                    if (generation != _viewGeneration) return;
                    if (!show) view.Visibility = Visibility.Collapsed;
                    if (show) AcknowledgeShownBatteryNotice();
                });
            }
        }
        _compact.IsHitTestVisible = header;
        if (header && _compact.Visibility != Visibility.Visible) { _compact.Opacity = 0; _compact.Visibility = Visibility.Visible; }
        if (immediate)
        {
            _motion.Stop(_compact, OpacityProperty);
            _compact.Opacity = header ? 1 : 0;
            _compact.Visibility = header ? Visibility.Visible : Visibility.Collapsed;
        }
        else _motion.Double(_compact, OpacityProperty, header ? 1 : 0, _app.EffectivePreferences.MotionSpeed, header ? .16 : .10, completed: () => { if (generation == _viewGeneration && !header) _compact.Visibility = Visibility.Collapsed; });
        UpdateCompactActivity();
        MoveHeader(target, immediate);
        var slide = presentation switch { IslandPresentation.Activity => _deckSlide, IslandPresentation.Tools => _dockSlide, _ => _detailSlide };
        if (immediate) { _motion.Stop(slide, TranslateTransform.YProperty); slide.Y = 0; }
        else
        {
            if (previous != presentation && View(presentation) != _compact && View(presentation).Opacity <= .01) slide.Y = 5;
            _motion.Double(slide, TranslateTransform.YProperty, 0, _app.EffectivePreferences.MotionSpeed, .28);
        }
        if (immediate)
        {
            _motion.Stop(_clip, RectangleGeometry.RectProperty);
            _motion.Stop(_clip, RectangleGeometry.RadiusXProperty);
            _motion.Stop(_clip, RectangleGeometry.RadiusYProperty);
            _targetRect = target;
            _clip.Rect = target;
            _clip.RadiusX = _clip.RadiusY = presentation == IslandPresentation.Compact ? 24 : 30;
        }
        else AnimateShape(target);
        UpdateSecondaryActivities();
    }

    private void MoveHeader(Rect target, bool immediate = false)
    {
        if (_presentation is not IslandPresentation.Compact and not IslandPresentation.Activity) return;
        bool expanded = _presentation == IslandPresentation.Activity;
        _compact.Width = expanded ? target.Width - 40 : target.Width;
        double x = expanded ? target.X + 20 : target.X, y = expanded ? 12 : 0;
        if (immediate || _rendering || Design.ReducedMotion(_app.EffectivePreferences))
        {
            _motion.Stop(_compactSlide, TranslateTransform.XProperty);
            _motion.Stop(_compactSlide, TranslateTransform.YProperty);
            _compactSlide.X = x; _compactSlide.Y = y;
        }
        else
        {
            _motion.Double(_compactSlide, TranslateTransform.XProperty, x, _app.EffectivePreferences.MotionSpeed, .32, .86);
            _motion.Double(_compactSlide, TranslateTransform.YProperty, y, _app.EffectivePreferences.MotionSpeed, .32, .86);
        }
    }

    private void FinishMotionForReducedPreference()
    {
        foreach (var transform in new[] { _slide, _detailSlide, _deckSlide, _dockSlide }) { _motion.Stop(transform, TranslateTransform.YProperty); transform.Y = 0; }
        _motion.Stop(_clip, RectangleGeometry.RectProperty);
        _motion.Stop(_clip, RectangleGeometry.RadiusXProperty);
        _motion.Stop(_clip, RectangleGeometry.RadiusYProperty);
        _clip.Rect = _targetRect;
        _clip.RadiusX = _clip.RadiusY = _presentation == IslandPresentation.Compact ? 24 : 30;
        foreach (var state in new[] { IslandPresentation.Activity, IslandPresentation.Tools, IslandPresentation.Detail })
        {
            var view = View(state); _motion.Stop(view, OpacityProperty);
            view.Visibility = state == _presentation ? Visibility.Visible : Visibility.Collapsed;
            view.Opacity = state == _presentation ? 1 : 0;
        }
        _motion.Stop(_compact, OpacityProperty);
        bool header = _presentation is IslandPresentation.Compact or IslandPresentation.Activity;
        _compact.Visibility = header ? Visibility.Visible : Visibility.Collapsed;
        _compact.Opacity = header ? 1 : 0;
        MoveHeader(_targetRect, true);
    }
}
