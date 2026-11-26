using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Crest.UI;

internal static class ThemePalettes
{
    private static readonly IReadOnlyDictionary<string, Brush> Dark = Create(
        canvas: "#101114", surface: "#17191D", raised: "#202328", inset: "#111317",
        text: "#F3F5F6", muted: "#B8BEC5", subtle: "#A5ADB7", line: "#7D8892",
        divider: "#363D44", hover: "#30363D", selected: "#253A36",
        accent: "#A3E4CB", accentFill: "#AAE8D2", accentText: "#132922", islandAccent: "#AAE8D2");

    private static readonly IReadOnlyDictionary<string, Brush> Light = Create(
        canvas: "#EDEFEA", surface: "#F7F8F4", raised: "#FFFFFF", inset: "#F0F2ED",
        text: "#1C2421", muted: "#4F5D56", subtle: "#58665E", line: "#718177",
        divider: "#D7DFD8", hover: "#E7EDE7", selected: "#DCEDE3",
        accent: "#176347", accentFill: "#20684E", accentText: "#FFFFFF", islandAccent: "#C4EAD1",
        success: "#246947", warning: "#7C5117", danger: "#A23343");

    private static readonly IReadOnlyDictionary<string, Brush> Ocean = Create(
        canvas: "#0D1720", surface: "#132230", raised: "#1C3040", inset: "#101C28",
        text: "#EEF7FC", muted: "#B6CBD9", subtle: "#9CB5C6", line: "#7797AA",
        divider: "#345165", hover: "#29455B", selected: "#25465A",
        accent: "#9DDCF5", accentFill: "#A9E1FA", accentText: "#102936", islandAccent: "#A9E1FA");

    private static readonly IReadOnlyDictionary<string, Brush> Forest = Create(
        canvas: "#101910", surface: "#18271B", raised: "#223429", inset: "#121F16",
        text: "#F2F7EB", muted: "#C1D0B9", subtle: "#A8BD9E", line: "#829D78",
        divider: "#3F5641", hover: "#344A37", selected: "#304B34",
        accent: "#C0E5A1", accentFill: "#C8E9AD", accentText: "#23331B", islandAccent: "#C8E9AD");

    private static readonly IReadOnlyDictionary<string, Brush> Rose = Create(
        canvas: "#1B1219", surface: "#261B24", raised: "#352533", inset: "#211720",
        text: "#FFF3FB", muted: "#DCC4D3", subtle: "#C6A7BA", line: "#A7889A",
        divider: "#523C4C", hover: "#493344", selected: "#503248",
        accent: "#F2BAD6", accentFill: "#F4C4DA", accentText: "#3C2535", islandAccent: "#F4C4DA");

    private static readonly IReadOnlyDictionary<string, Brush> Island = new Dictionary<string, Brush>
    {
        ["IslandShell"] = Brushes.Black,
        ["IslandCard"] = Solid("#111113"),
        ["IslandInput"] = Solid("#19191C"),
        ["IslandHover"] = Solid("#242427"),
        ["IslandLine"] = Solid("#777780"),
        ["IslandText"] = Solid("#F8F8FA"),
        ["IslandMuted"] = Solid("#B3B3BC"),
        ["IslandActionText"] = Solid("#19191C"),
        ["IslandOutline"] = Solid("#29292D"),
        ["IslandAccentMusic"] = Solid("#F2A1AF"),
        ["IslandAccentTimer"] = Solid("#F0C879"),
        ["IslandAccentNotice"] = Solid("#FFA68F")
    };

    private static readonly string[] SystemBackgrounds =
    {
        "Canvas", "Surface", "Raised", "Inset", "Hover", "Selected",
        "IslandShell", "IslandCard", "IslandInput", "IslandHover"
    };
    private static readonly string[] SystemForegrounds =
    {
        "Text", "Muted", "Subtle", "Line", "Divider", "Success", "Warning", "Danger",
        "IslandText", "IslandMuted", "IslandLine", "IslandOutline"
    };
    private static readonly string[] SystemHighlights =
    {
        "Accent", "AccentFill", "IslandAccent", "IslandAccentMusic", "IslandAccentTimer", "IslandAccentNotice"
    };
    private static readonly string[] SystemHighlightText = { "AccentText", "IslandActionText" };

    internal static string WindowsTheme =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is 1
            ? "Light"
            : "Dark";

    internal static string Resolve(string theme, string windowsTheme) => theme == "System" ? windowsTheme : theme;

    internal static IReadOnlyDictionary<string, Brush> Get(string theme) => theme switch
    {
        "Light" => Light,
        "Ocean" => Ocean,
        "Forest" => Forest,
        "Rose" => Rose,
        _ => Dark
    };

    internal static void Apply(ResourceDictionary resources, string theme, bool highContrast)
    {
        if (highContrast)
        {
            SetBrushes(SystemBackgrounds, SystemColors.WindowBrush);
            SetBrushes(SystemForegrounds, SystemColors.WindowTextBrush);
            SetBrushes(SystemHighlights, SystemColors.HighlightBrush);
            SetBrushes(SystemHighlightText, SystemColors.HighlightTextBrush);
            return;
        }

        foreach (var (role, brush) in Get(theme))
            SetBrush(resources, role, brush);
        foreach (var (role, brush) in Island)
            SetBrush(resources, role, brush);

        void SetBrushes(string[] roles, Brush brush)
        {
            foreach (string role in roles)
                SetBrush(resources, role, brush);
        }
    }

    private static void SetBrush(ResourceDictionary resources, string role, Brush brush)
    {
        if (!ReferenceEquals(resources[role], brush))
            resources[role] = brush;
    }

    private static IReadOnlyDictionary<string, Brush> Create(
        string canvas, string surface, string raised, string inset,
        string text, string muted, string subtle, string line,
        string divider, string hover, string selected,
        string accent, string accentFill, string accentText, string islandAccent,
        string success = "#A2DDB7", string warning = "#F1CF90", string danger = "#F4AEA9") =>
        new Dictionary<string, Brush>
        {
            ["Canvas"] = Solid(canvas),
            ["Surface"] = Solid(surface),
            ["Raised"] = Solid(raised),
            ["Inset"] = Solid(inset),
            ["Text"] = Solid(text),
            ["Muted"] = Solid(muted),
            ["Subtle"] = Solid(subtle),
            ["Line"] = Solid(line),
            ["Divider"] = Solid(divider),
            ["Hover"] = Solid(hover),
            ["Selected"] = Solid(selected),
            ["Accent"] = Solid(accent),
            ["AccentFill"] = Solid(accentFill),
            ["AccentText"] = Solid(accentText),
            ["Success"] = Solid(success),
            ["Warning"] = Solid(warning),
            ["Danger"] = Solid(danger),
            ["IslandAccent"] = Solid(islandAccent)
        };

    private static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
