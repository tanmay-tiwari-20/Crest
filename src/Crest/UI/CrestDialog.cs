using System.Windows;
using System.Windows.Controls;

namespace Crest.UI;

/// <summary>A local, owner-modal confirmation with a safe keyboard default.</summary>
internal sealed class CrestDialog : Window
{
    private CrestDialog(Window owner, string title, string detail, string confirm)
    {
        Owner = owner;
        Title = title;
        Width = 480;
        MaxWidth = SystemParameters.WorkArea.Width - 32;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Surface");
        var layout = new StackPanel { Margin = new Thickness(28) };
        var mark = CrestBrand.Mark(32, "Muted");
        mark.HorizontalAlignment = HorizontalAlignment.Left;
        mark.Margin = new Thickness(0, 0, 0, 20);
        layout.Children.Add(mark);
        var heading = Design.Text(title, 24, "Text", true);
        heading.Margin = new Thickness(0, 0, 0, 12);
        layout.Children.Add(heading);
        var description = Design.Text(detail, 14, "Muted");
        description.Margin = new Thickness(0, 0, 0, 24);
        layout.Children.Add(description);
        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = Design.Button("Cancel", () => DialogResult = false);
        cancel.IsCancel = true;
        cancel.IsDefault = true;
        cancel.Margin = new Thickness(0, 0, 8, 0);
        actions.Children.Add(cancel);
        var accept = Design.Button(confirm, () => DialogResult = true);
        accept.SetResourceReference(Control.ForegroundProperty, "Danger");
        Grid.SetColumn(accept, 1);
        actions.Children.Add(accept);
        layout.Children.Add(actions);
        Content = layout;
        Loaded += (_, _) => cancel.Focus();
    }

    public static bool Confirm(Window owner, string title, string detail, string action)
        => new CrestDialog(owner, title, detail, action).ShowDialog() == true;
}
