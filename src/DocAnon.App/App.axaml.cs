using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DocAnon.App.ViewModels;
using DocAnon.App.Views;

namespace DocAnon.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            int rootIdx = Array.IndexOf(args, "--raiz");
            var vm = new MainViewModel(rootIdx >= 0 && rootIdx + 1 < args.Length ? args[rootIdx + 1] : null);
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => vm.Dispose();
            int cap = Array.IndexOf(args, "--captura");
            if (cap >= 0 && cap + 1 < args.Length) window.Opened += async (_, _) => await CaptureAsync(desktop, window, vm, args, args[cap + 1]);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Diagnostic mode: "--captura saida.png [--sessao rótulo] [--aba n] [arquivos...]" opens the files in
    /// a new session, renders the window into a PNG with Avalonia itself and quits. It never reads the screen.
    /// </summary>
    private static async Task CaptureAsync(IClassicDesktopStyleApplicationLifetime desktop, Window window, MainViewModel vm, string[] args, string output)
    {
        string Arg(string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }
        var files = args.Where(a => a != output && File.Exists(a)).ToList();
        if (files.Count > 0)
        {
            vm.NewSessionLabel = Arg("--sessao", "captura");
            vm.NewSessionCommand.Execute(null);
            await vm.AddFilesCommand.ExecuteAsync(files);
        }
        vm.SelectedTab = int.Parse(Arg("--aba", "0"));
        if (Array.IndexOf(args, "--exportar") >= 0 && vm.ExportCommand.CanExecute(null))
        {
            await vm.ExportCommand.ExecuteAsync(null);
            vm.SelectedTab = int.Parse(Arg("--aba", "2"));
        }
        await Task.Delay(1500);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
            using var rtb = new RenderTargetBitmap(size);
            rtb.Render(window);
#pragma warning disable CS0618 // The encoder-options overload is not needed for a diagnostic PNG.
            rtb.Save(output);
#pragma warning restore CS0618
        });
        desktop.Shutdown();
    }
}
