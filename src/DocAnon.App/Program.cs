using Avalonia;

namespace DocAnon.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        // As in the DWSIM Avalonia UI, the platform UI font (Segoe UI on Windows) is used instead
        // of the bundled Inter, because the system rasterises it with native hinting.
        .LogToTrace();
}
