// Desktop / Appearance — glass surfaces, system colors and the saved theme choice.
// Shared brushes update existing windows and preserve open editor drafts.
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace CvrTool.Desktop;

internal static class Appearance
{
    private static readonly string PreferenceFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CvrTool", "appearance.txt");
    public static readonly string[] Choices = ["System", "Light", "Dark"];
    public static string Choice { get; private set; } = "System";
    public static readonly SolidColorBrush Ink = new(), Muted = new(), Accent = new(), Line = new(), Surface = new();
    public static readonly LinearGradientBrush Canvas = Gradient(), Glass = Gradient(), Strip = Gradient(), Selected = Gradient();

    private static LinearGradientBrush Gradient() => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(.6, 1, RelativeUnit.Relative),
        GradientStops = [new(Colors.Transparent, 0), new(Colors.Transparent, .35), new(Colors.Transparent, 1)]
    };

    public static void Initialize(App app)
    {
        string? saved = null;
        try { if (File.Exists(PreferenceFile)) saved = File.ReadAllText(PreferenceFile).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        app.ActualThemeVariantChanged += (_, _) => Update(app.ActualThemeVariant);
        Select(app, saved);
        Update(app.ActualThemeVariant);
    }

    public static void Select(App app, string? choice)
    {
        Choice = choice is "Light" or "Dark" ? choice : "System";
        app.RequestedThemeVariant = Choice switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferenceFile)!);
        File.WriteAllText(PreferenceFile, Choice);
    }

    private static void Update(ThemeVariant variant)
    {
        bool light = variant == ThemeVariant.Light;
        Color Tone(string dark, string bright) => Color.Parse(light ? bright : dark);
        Ink.Color = Tone("#EDF2F5", "#142D37");
        Muted.Color = Tone("#A6B7BE", "#435D68");
        Accent.Color = Tone("#24DECE", "#006D67");
        Line.Color = Tone("#46525D", "#9AAEB6");
        Surface.Color = Tone("#182129", "#E7EFF2");
        void Paint(LinearGradientBrush brush, Color top, Color middle, Color bottom)
        {
            brush.GradientStops[0].Color = top;
            brush.GradientStops[1].Color = middle;
            brush.GradientStops[2].Color = bottom;
        }
        Paint(Canvas, Tone("#1A4045","#CCDDE0"), Tone("#142329","#E3ECEF"), Tone("#10181F","#F6F9FA"));
        Paint(Glass, Tone("#DF345056","#EBFFFFFF"), Tone("#D825333A","#D1EAF1F3"), Tone("#EE1B252C","#E8DCE6EA"));
        Paint(Strip, Tone("#EA2F3D44","#FCFFFFFF"), Tone("#E81D292F","#F0F4F8F9"), Tone("#F0141D23","#E6E2EBEF"));
        Paint(Selected, Tone("#397877","#F3FFFF"), Tone("#205557","#C3EEEA"), Tone("#153739","#A9D8D5"));
    }
}
