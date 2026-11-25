using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using DocAnon.App.Controls;
using DocAnon.App.ViewModels;
using DocAnon.Core.Model;

namespace DocAnon.App.Views;

public partial class MainWindow : Window
{
    private readonly SpanColorizer _colorizer = new();
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);

        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        DropZone.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        OpenFilesButton.Click += OnOpenFiles;
        CopyTextButton.Click += async (_, _) => await CopyText(_vm?.ExportText);
        CopyRestoredButton.Click += async (_, _) => await CopyText(_vm?.RestoredText);
        SavePackageButton.Click += OnSavePackage;
        AddLogoButton.Click += OnAddLogo;
        PageCombo.SelectionChanged += (_, _) => ShowPage();
        PageExport.IsCheckedChanged += (_, _) =>
        {
            if (PageCombo.SelectedItem is PageItem p) p.Export = PageExport.IsChecked == true;
        };
        PageView.BoxesChanged += () => PageView.Refresh();
        BuildContextMenu();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is null) return;
        _vm.PropertyChanged += OnVmChanged;
        _vm.SpansChanged += RefreshHighlights;
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedDocument))
        {
            Editor.Text = _vm?.SelectedDocument?.Doc.Text ?? "";
            RefreshHighlights();
            PageCombo.SelectedIndex = _vm?.SelectedDocument?.Pages.Count > 0 ? 0 : -1;
            ShowPage();
        }
    }

    private void RefreshHighlights()
    {
        var d = _vm?.SelectedDocument;
        _colorizer.Spans = d?.Spans.Select(s => (s.Start, s.Length, s.Type, s.Include && s.Action != SpanAction.Keep, s.Action == SpanAction.Scale)).ToList()
                           ?? [];
        Editor.TextArea.TextView.Redraw();
        PageView.ActiveRanges = d?.Spans.Where(s => s.Include && s.Action == SpanAction.Pseudonymize).Select(s => (s.Start, s.Length)).ToList() ?? [];
        PageView.Refresh();
    }

    private void ShowPage()
    {
        var p = PageCombo.SelectedItem as PageItem;
        PageView.Page = p?.Page;
        PageExport.IsChecked = p?.Export ?? false;
        RefreshHighlights();
    }

    private void BuildContextMenu()
    {
        var types = MainViewModel.EntityTypes;
        foreach (var t in types)
        {
            var mark = new MenuItem { Header = EntityTypeInfo.Label(t) };
            mark.Click += (_, _) => _vm?.MarkSelection(Editor.SelectionStart, Editor.SelectionLength, t);
            MarkMenu.Items.Add(mark);
            var add = new MenuItem { Header = EntityTypeInfo.Label(t) };
            add.Click += (_, _) =>
            {
                if (Editor.SelectionLength > 0) _vm?.AddSelectionToProfile(Editor.SelectedText, t);
            };
            ProfileMenu.Items.Add(add);
        }
        var valor = new MenuItem { Header = "Valor (marcador em vez de escala)" };
        valor.Click += (_, _) => _vm?.MarkSelection(Editor.SelectionStart, Editor.SelectionLength, EntityType.VALOR);
        MarkMenu.Items.Add(valor);
        NeverMenu.Click += (_, _) =>
        {
            if (Editor.SelectionLength > 0) _vm?.NeverAnonymize(Editor.SelectedText);
        };
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null || _vm is null) return;
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0 && _vm.AddFilesCommand.CanExecute(paths)) await _vm.AddFilesCommand.ExecuteAsync(paths);
    }

    private async void OnOpenFiles(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Escolha os documentos",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Documentos suportados") { Patterns = Extraction.DocumentExtractor.SupportedExtensions.Select(x => "*" + x).ToList() },
            ],
        });
        var paths = picked.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0 && _vm.AddFilesCommand.CanExecute(paths)) await _vm.AddFilesCommand.ExecuteAsync(paths);
    }

    private async void OnSavePackage(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pasta para o pacote anonimizado" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } folder) await _vm.SavePackageAsync(folder);
    }

    private async void OnAddLogo(object? sender, RoutedEventArgs e)
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Imagem do logotipo (recortada)",
            FileTypeFilter = [new FilePickerFileType("Imagens") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"] }],
        });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is { } p) _vm?.AddLogoFromFile(p);
    }

    private async Task CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text) || Clipboard is null) return;
        await Clipboard.SetTextAsync(text);
        if (_vm is not null) _vm.Status = "Texto copiado para a área de transferência.";
    }

    private async void CopyImage_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: RedactedImage img } || Clipboard is null) return;
        using var bmp = new Bitmap(new MemoryStream(img.Png));
        await Clipboard.SetBitmapAsync(bmp);
        if (_vm is not null) _vm.Status = $"Imagem \"{img.Source}\" copiada.";
    }
}
