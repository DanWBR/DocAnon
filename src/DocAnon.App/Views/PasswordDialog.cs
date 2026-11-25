using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DocAnon.App.Views;

/// <summary>Asks for a password, and optionally for the same password again to confirm it.</summary>
public sealed class PasswordDialog : Window
{
    private readonly TextBox _first = new() { PasswordChar = '●', PlaceholderText = "senha" };
    private readonly TextBox _second = new() { PasswordChar = '●', PlaceholderText = "repita a senha" };
    private readonly TextBlock _error = new() { Classes = { "leak" }, IsVisible = false };
    private readonly bool _confirm;
    private readonly int _minLength;

    public PasswordDialog(string title, string message, bool confirm, int minLength = 0)
    {
        _confirm = confirm;
        _minLength = minLength;
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = null;

        var ok = new Button { Content = "OK", Classes = { "primary" }, IsDefault = true, MinWidth = 90 };
        var cancel = new Button { Content = "Cancelar", Classes = { "action" }, IsCancel = true, MinWidth = 90 };
        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => Close(null);
        _second.IsVisible = confirm;

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _first,
                _second,
                _error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok },
                },
            },
        };
        Opened += (_, _) => _first.Focus();
    }

    private void Accept()
    {
        string p = _first.Text ?? "";
        if (p.Length < Math.Max(1, _minLength))
        {
            ShowError(_minLength > 1 ? $"A senha precisa ter pelo menos {_minLength} caracteres." : "Digite a senha.");
            return;
        }
        if (_confirm && p != (_second.Text ?? ""))
        {
            ShowError("As duas senhas não são iguais.");
            return;
        }
        Close(p);
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }
}
