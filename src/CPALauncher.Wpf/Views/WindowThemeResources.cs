using System.Windows;
using System.Windows.Media;
using CPALauncher.ViewModels;

namespace CPALauncher.Views;

internal static class WindowThemeResources
{
    public static bool IsDarkModeEnabled()
    {
        return Application.Current?.MainWindow?.DataContext is MainViewModel { IsDarkMode: true };
    }

    public static void ApplyWizardResources(ResourceDictionary resources, bool isDark)
    {
        ApplyCommonResources(resources, "Wizard", isDark);
        SetSolidResource(resources, "WizardSurfaceBrush", isDark ? "#182638" : "#F7FBFE");
        SetSolidResource(resources, "WizardInputBrush", isDark ? "#142131" : "#F8FBFE");
    }

    public static void ApplyDialogResources(ResourceDictionary resources, bool isDark)
    {
        ApplyCommonResources(resources, "Dialog", isDark);
        SetSolidResource(resources, "DialogSurfaceBrush", isDark ? "#182638" : "#F7FBFE");
    }

    private static void ApplyCommonResources(ResourceDictionary resources, string prefix, bool isDark)
    {
        SetSolidResource(resources, $"{prefix}InkBrush", isDark ? "#EAF1F8" : "#142233");
        SetSolidResource(resources, $"{prefix}MutedBrush", isDark ? "#B3C0CF" : "#536376");
        SetSolidResource(resources, $"{prefix}LineBrush", isDark ? "#304155" : "#D9E2EC");
        SetSolidResource(resources, $"{prefix}WindowEdgeBrush", isDark ? "#203148" : "#EAF1F8");
        SetSolidResource(resources, $"{prefix}ActionBrush", isDark ? "#1B2B3F" : "#F8FBFE");
        SetSolidResource(resources, $"{prefix}ActionHoverBrush", isDark ? "#24364D" : "#FFFFFF");
        SetSolidResource(resources, $"{prefix}ActionPressedBrush", isDark ? "#152236" : "#EDF4FA");
        SetSolidResource(resources, $"{prefix}PrimaryHoverBrush", isDark ? "#4BA977" : "#3FA36C");
        SetSolidResource(resources, $"{prefix}PrimaryPressedBrush", isDark ? "#2F8C5A" : "#2F8C5A");

        SetGradientResource(
            resources,
            $"{prefix}BackgroundBrush",
            isDark
                ? new[] { "#101B29", "#152336" }
                : new[] { "#F7FAFD", "#EAF1F8" });

        SetGradientResource(
            resources,
            $"{prefix}PrimaryBrush",
            isDark
                ? new[] { "#61C487", "#349866" }
                : new[] { "#57B77C", "#349866" });
    }

    private static void SetSolidResource(ResourceDictionary resources, string key, string color)
    {
        var nextColor = ParseColor(color);
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = nextColor;
            return;
        }

        resources[key] = new SolidColorBrush(nextColor);
    }

    private static void SetGradientResource(ResourceDictionary resources, string key, IReadOnlyList<string> colors)
    {
        if (resources[key] is LinearGradientBrush brush && !brush.IsFrozen && brush.GradientStops.Count >= colors.Count)
        {
            for (var i = 0; i < colors.Count; i++)
            {
                brush.GradientStops[i].Color = ParseColor(colors[i]);
            }

            return;
        }

        resources[key] = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            [
                new GradientStop(ParseColor(colors[0]), 0),
                new GradientStop(ParseColor(colors[^1]), 1),
            ],
        };
    }

    private static Color ParseColor(string color)
        => (Color)ColorConverter.ConvertFromString(color);
}
