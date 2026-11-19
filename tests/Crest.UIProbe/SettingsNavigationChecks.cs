using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Crest.UI;

internal static class SettingsNavigationChecks
{
    public static async Task Run(Crest.App app, Action<bool, string> check)
    {
        var window = new SettingsWindow(app);
        try
        {
            window.Show();
            window.Activate();
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            var navigation = (StackPanel)typeof(SettingsWindow).GetField("_nav", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            var appearance = (Button)navigation.Children[1];
            appearance.Focus();
            Keyboard.Focus(appearance);
            appearance.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            check(ReferenceEquals(navigation.Children[1], appearance) && ReferenceEquals(Keyboard.FocusedElement, appearance),
                "Settings page changes preserve the activated navigation button and keyboard focus");
            appearance.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            var behavior = (Button)navigation.Children[2];
            bool next = ReferenceEquals(Keyboard.FocusedElement, behavior);
            behavior.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            behavior.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous));
            check(next && ReferenceEquals(Keyboard.FocusedElement, appearance),
                "Settings navigation retains forward and reverse tab order across page changes");
        }
        finally
        {
            window.Close();
        }
    }
}
