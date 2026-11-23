using Avalonia;
using Avalonia.Controls;

namespace StudyHelper.Views;

/// <summary>
/// Attached property: keeps a chat ScrollViewer scrolled to the newest message.
/// Usage: &lt;ScrollViewer views:AutoScroll.IsEnabled="True"&gt;
/// </summary>
public sealed class AutoScroll
{
    private AutoScroll()
    {
    }

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<AutoScroll, ScrollViewer, bool>("IsEnabled");

    static AutoScroll()
    {
        IsEnabledProperty.Changed.AddClassHandler<ScrollViewer>((scrollViewer, e) =>
        {
            scrollViewer.ScrollChanged -= OnScrollChanged;
            if (e.NewValue is true)
                scrollViewer.ScrollChanged += OnScrollChanged;
        });
    }

    public static bool GetIsEnabled(ScrollViewer element) => element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ScrollViewer element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Content got taller (new message / answer arrived) -> jump to the bottom.
        if (sender is ScrollViewer scrollViewer && e.ExtentDelta.Y > 0)
            scrollViewer.ScrollToEnd();
    }
}
