using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TubaWinUi3.Controls;

/// <summary>Keep a usable glyph until an image has actually decoded, including failed SVGs.</summary>
public sealed class ToolIconPresenter : UserControl
{
    private readonly FontIcon _fallback = new() { FontFamily = new FontFamily("Segoe Fluent Icons") };
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private int _generation;
    private bool _loaded;
    private bool _refreshQueued;
    private string? _displayedPath;
    private ImageSource? _displayedSource;
    private bool _highContrast;

    public ToolIconPresenter()
    {
        var grid = new Grid();
        grid.Children.Add(_fallback);
        grid.Children.Add(_image);
        Content = grid;
        _image.ImageOpened += (_, _) => _fallback.Visibility = Visibility.Collapsed;
        _image.ImageFailed += (_, _) => _fallback.Visibility = Visibility.Visible;
        ActualThemeChanged += (_, _) => QueueRefresh();
        Loaded += (_, _) => { _loaded = true; QueueRefresh(); };
        Unloaded += (_, _) => _loaded = false;
    }

    public static readonly DependencyProperty IconPathProperty = DependencyProperty.Register(nameof(IconPath), typeof(string), typeof(ToolIconPresenter), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty IconSourceProperty = DependencyProperty.Register(nameof(IconSource), typeof(ImageSource), typeof(ToolIconPresenter), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(ToolIconPresenter), new PropertyMetadata("\uE8B7", Changed));
    public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(nameof(GlyphSize), typeof(double), typeof(ToolIconPresenter), new PropertyMetadata(28d, Changed));
    public string? IconPath { get => (string?)GetValue(IconPathProperty); set => SetValue(IconPathProperty, value); }
    public ImageSource? IconSource { get => (ImageSource?)GetValue(IconSourceProperty); set => SetValue(IconSourceProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public double GlyphSize { get => (double)GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs _) => ((ToolIconPresenter)sender).QueueRefresh();

    private void QueueRefresh()
    {
        // Bindings arrive separately. Decode only after the realized control has all its inputs.
        if (!_loaded || _refreshQueued) return;
        _refreshQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _refreshQueued = false;
            if (_loaded) Refresh();
        });
    }

    private void Refresh()
    {
        _fallback.Glyph = string.IsNullOrWhiteSpace(Glyph) ? "\uE8B7" : Glyph;
        _fallback.FontSize = GlyphSize;
        var highContrast = Services.ThemeColors.IsSystemHighContrast();
        var path = string.IsNullOrWhiteSpace(IconPath) ? null : IconPath;
        var source = path is null ? IconSource : null;
        // Glyph/size, Loaded and ordinary theme changes must not restart an image load.
        if (_highContrast == highContrast && _displayedPath == path && ReferenceEquals(_displayedSource, source)) return;
        _highContrast = highContrast;
        _displayedPath = path;
        _displayedSource = source;
        var generation = ++_generation;
        _fallback.Visibility = Visibility.Visible;
        _image.Source = null;
        if (highContrast) return;
        try
        {
            // Subscribe before starting a load; a shared SVG might already have fired Opened.
            if (!string.IsNullOrWhiteSpace(IconPath))
                _image.Source = new BitmapImage
                {
                    DecodePixelType = DecodePixelType.Logical,
                    DecodePixelWidth = 48,
                    DecodePixelHeight = 48,
                    UriSource = new Uri(IconPath)
                };
            else if (IconSource is SvgImageSource svg && svg.UriSource is not null)
            {
                var local = new SvgImageSource();
                local.Opened += (_, _) => { if (generation == _generation) _fallback.Visibility = Visibility.Collapsed; };
                local.OpenFailed += (_, _) => { if (generation == _generation) _fallback.Visibility = Visibility.Visible; };
                _image.Source = local;
                local.UriSource = svg.UriSource;
            }
            else if (IconSource is not null) _image.Source = IconSource;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[ToolIcon] {ex.Message}"); }
    }
}
