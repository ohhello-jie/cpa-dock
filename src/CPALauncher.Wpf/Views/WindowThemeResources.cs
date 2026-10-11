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
        SetSolidResource(resources, "WizardSurfaceBrush", isDark ? "#1B1F27" : "#FFFFFF");
        SetSolidResource(resources, "WizardInputBrush", isDark ? "#12151B" : "#FFFFFF");
    }

    public static void ApplyDialogResources(ResourceDictionary resources, bool isDark)
    {
        ApplyCommonResources(resources, "Dialog", isDark);
        SetSolidResource(resources, "DialogSurfaceBrush", isDark ? "#1B1F27" : "#FFFFFF");
    }

    private static void ApplyCommonResources(ResourceDictionary resources, string prefix, bool isDark)
    {
        SetSolidResource(resources, $"{prefix}InkBrush", isDark ? "#ECEEF3" : "#252A34");
        SetSolidResource(resources, $"{prefix}MutedBrush", isDark ? "#A6ADBB" : "#7A818F");
        SetSolidResource(resources, $"{prefix}LineBrush", isDark ? "#303540" : "#E4E7ED");
        SetSolidResource(resources, $"{prefix}WindowEdgeBrush", isDark ? "#303540" : "#ECEEF3");
        SetSolidResource(resources, $"{prefix}ActionBrush", isDark ? "#222731" : "#FFFFFF");
        SetSolidResource(resources, $"{prefix}ActionHoverBrush", isDark ? "#2D3441" : "#FFFFFF");
        SetSolidResource(resources, $"{prefix}ActionPressedBrush", isDark ? "#222731" : "#EDF4FA");
        SetSolidResource(resources, $"{prefix}PrimaryHoverBrush", isDark ? "#47556C" : "#344054");
        SetSolidResource(resources, $"{prefix}PrimaryPressedBrush", isDark ? "#202632" : "#202632");

        SetGradientResource(
            resources,
            $"{prefix}BackgroundBrush",
            isDark
                ? new[] { "#12151B", "#12151B" }
                : new[] { "#F5F6F8", "#F5F6F8" });

        SetGradientResource(
            resources,
            $"{prefix}PrimaryBrush",
            isDark
                ? new[] { "#202632", "#202632" }
                : new[] { "#202632", "#202632" });
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
