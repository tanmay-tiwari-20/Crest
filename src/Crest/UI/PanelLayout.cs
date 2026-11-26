using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Crest.UI;

/// <summary>Shared hierarchy and spacing for tools used in both the island and Settings.</summary>
internal static class PanelLayout
{
    public static TextBlock Heading(string title, string? description = null)
    {
        var heading = Design.Text(title, 18, "Text", true);
        heading.Margin = new Thickness(0, 0, 0, description == null ? 12 : 4);
        return heading;
    }

    public static Border Group(string title, UIElement content, string? description = null)
    {
        var stack = new StackPanel();
        stack.Children.Add(Heading(title, description));
        if (description != null)
        {
            var detail = Design.Text(description, 12, "Muted");
            detail.Margin = new Thickness(0, 0, 0, 12);
            stack.Children.Add(detail);
        }
        stack.Children.Add(content);
        var card = Design.Card(stack, new Thickness(16));
        card.Margin = new Thickness(0, 0, 0, 12);
        return card;
    }

    public static void Select(Button button, bool selected, string label)
    {
        button.SetResourceReference(Control.BackgroundProperty, selected ? "Raised" : "Inset");
        button.SetResourceReference(Control.ForegroundProperty, selected ? "Text" : "Muted");
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        AutomationProperties.SetName(button, label);
        AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
    }

    public static void SpaceActions(Panel parent)
    {
        foreach (UIElement element in parent.Children)
            if (element is FrameworkElement control)
                control.Margin = new Thickness(0, 0, 8, 8);
    }

    // Apply once to a newly composed tool. Dynamic rows use the same shared factories.
    // Existing field-specific spacing and all controls/handlers stay intact.
    public static void Refine(Panel parent)
    {
        foreach (UIElement element in parent.Children)
        {
            if (element is TextBlock text)
            {
                if (text.FontSize < 12)
                {
                    text.FontSize = 12;
                    text.LineHeight = 12 * 1.45;
                }
                if (text.Margin == new Thickness(0)) text.Margin = new Thickness(0, 0, 0, 6);
            }
            else if (element is Button button && parent is StackPanel stack && stack.Orientation == Orientation.Vertical)
            {
                if (button.Margin == new Thickness(0)) button.Margin = new Thickness(0, 0, 0, 8);
            }
            if (element is Panel panel) Refine(panel);
            else if (element is Border border && border.Child is Panel content) Refine(content);
        }
    }
}
