using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DocAnon.Core.Model;

namespace DocAnon.App.Controls;

/// <summary>
/// Shows a visual page with what will be covered: words that will get a marker (red), automatic
/// covers such as logos (blue, click to toggle) and manual boxes (black). Dragging draws a new
/// manual box; right-clicking a manual box removes it.
/// </summary>
public sealed class PageView : Control
{
    private Bitmap? _bitmap;
    private VisualPage? _page;
    private Point? _dragStart;
    private Point _dragNow;

    public IReadOnlyList<(int Start, int Length)> ActiveRanges { get; set; } = [];

    public event Action? BoxesChanged;

    public VisualPage? Page
    {
        get => _page;
        set
        {
            _page = value;
            _bitmap?.Dispose();
            _bitmap = value is null ? null : new Bitmap(new MemoryStream(value.RenderOriginalPng()));
            InvalidateVisual();
        }
    }

    public void Refresh() => InvalidateVisual();

    private (double Scale, double Ox, double Oy) Fit()
    {
        if (_bitmap is null || _page is null) return (1, 0, 0);
        double s = Math.Min(Bounds.Width / _page.WidthPx, Bounds.Height / _page.HeightPx);
        double ox = (Bounds.Width - _page.WidthPx * s) / 2, oy = (Bounds.Height - _page.HeightPx * s) / 2;
        return (s, ox, oy);
    }

    private Rect ToScreen(BoundingBox b)
    {
        var (s, ox, oy) = Fit();
        return new Rect(ox + b.X * s, oy + b.Y * s, Math.Max(1, b.Width * s), Math.Max(1, b.Height * s));
    }

    private BoundingBox ToPage(Rect r)
    {
        var (s, ox, oy) = Fit();
        return new BoundingBox((float)((r.X - ox) / s), (float)((r.Y - oy) / s), (float)(r.Width / s), (float)(r.Height / s));
    }

    private static readonly IBrush WordFill = new SolidColorBrush(Color.FromArgb(90, 230, 60, 60));
    private static readonly IBrush CoverOn = new SolidColorBrush(Color.FromArgb(120, 40, 90, 230));
    private static readonly IBrush CoverOff = new SolidColorBrush(Color.FromArgb(30, 40, 90, 230));
    private static readonly IPen CoverPen = new Pen(Brushes.RoyalBlue, 1.5);
    private static readonly IBrush ManualFill = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        if (_bitmap is null || _page is null) return;
        var (s, ox, oy) = Fit();
        ctx.DrawImage(_bitmap, new Rect(ox, oy, _page.WidthPx * s, _page.HeightPx * s));
        foreach (var w in _page.Words)
        {
            bool hit = ActiveRanges.Any(r => w.TextStart < r.Start + r.Length && r.Start < w.TextStart + w.TextLength);
            if (hit) ctx.FillRectangle(WordFill, ToScreen(w.Box));
        }
        foreach (var c in _page.AutoCovers)
        {
            var r = ToScreen(c.Box);
            ctx.FillRectangle(c.Include ? CoverOn : CoverOff, r);
            ctx.DrawRectangle(CoverPen, r);
        }
        foreach (var m in _page.ManualBoxes) ctx.FillRectangle(ManualFill, ToScreen(m));
        if (_dragStart is { } d) ctx.DrawRectangle(new Pen(Brushes.Black, 1, DashStyle.Dash), new Rect(d, _dragNow).Normalize());
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_page is null) return;
        var p = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            var hit = _page.ManualBoxes.FindLastIndex(m => ToScreen(m).Contains(p));
            if (hit >= 0)
            {
                _page.ManualBoxes.RemoveAt(hit);
                BoxesChanged?.Invoke();
                InvalidateVisual();
            }
            return;
        }
        var cover = _page.AutoCovers.LastOrDefault(c => ToScreen(c.Box).Contains(p));
        if (cover is not null && e.KeyModifiers == KeyModifiers.None)
        {
            cover.Include = !cover.Include;
            BoxesChanged?.Invoke();
            InvalidateVisual();
            return;
        }
        _dragStart = p;
        _dragNow = p;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStart is null) return;
        _dragNow = e.GetPosition(this);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragStart is not { } d || _page is null) return;
        var r = new Rect(d, e.GetPosition(this)).Normalize();
        _dragStart = null;
        e.Pointer.Capture(null);
        if (r.Width > 4 && r.Height > 4)
        {
            _page.ManualBoxes.Add(ToPage(r));
            BoxesChanged?.Invoke();
        }
        InvalidateVisual();
    }
}
