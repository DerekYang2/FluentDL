using System.ComponentModel;
using System.Numerics;
using FluentDL.Helpers;
using FluentDL.Models;
using FluentDL.Core.Sources;
using FluentDL.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation.Metadata;
using Windows.UI.ViewManagement;

namespace FluentDL.Views;

public sealed partial class QueueArtworkTile : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(SongSearchObject), typeof(QueueArtworkTile), new PropertyMetadata(null, Item_Changed));
    public static readonly DependencyProperty UseUrlFallbackProperty = DependencyProperty.Register(
        nameof(UseUrlFallback), typeof(bool), typeof(QueueArtworkTile), new PropertyMetadata(false, Item_Changed));
    private static readonly UISettings uiSettings = new();
    private static readonly AccessibilitySettings accessibility = new();
    private static readonly bool hasAnimationEvent = ApiInformation.IsEventPresent("Windows.UI.ViewManagement.UISettings", "AnimationsEnabledChanged");
    private static readonly HashSet<QueueArtworkTile> motionObservers = new();
    private static DispatcherTimer? motionTimer;
    private static bool motionEnabled = true;
    private SongSearchObject? observedItem;
    private BitmapImage? urlBitmap;
    private string? urlKey;
    private bool urlReady;
    private bool urlFailed;
    private bool loaded;
    private bool inViewport = true;
    private Visual? skeletonVisual;
    private ScalarKeyFrameAnimation? pulse;

    public QueueArtworkTile() => InitializeComponent();
    public SongSearchObject? Item { get => (SongSearchObject?)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public bool UseUrlFallback { get => (bool)GetValue(UseUrlFallbackProperty); set => SetValue(UseUrlFallbackProperty, value); }
    internal bool IsPulsing => pulse != null;
    internal bool IsSkeletonVisible => Skeleton.Visibility == Visibility.Visible;
    internal bool IsInViewport => inViewport;
    internal ImageSource? DisplayedSource => CoverImage.Source;

    private static void Item_Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (QueueViewModel.IsClosing) return;
        var tile = (QueueArtworkTile)sender;
        tile.ObserveItem();
        tile.Update();
    }

    private void ObserveItem()
    {
        if (observedItem != null) observedItem.PropertyChanged -= Item_PropertyChanged;
        QueueViewModel.ArtworkChanged -= SharedArtwork_Changed;
        observedItem = loaded ? Item : null;
        if (observedItem != null) observedItem.PropertyChanged += Item_PropertyChanged;
        if (loaded && !UseUrlFallback && Item is not QueueObject && Item?.Source != SourceIds.Local)
            QueueViewModel.ArtworkChanged += SharedArtwork_Changed;
    }

    private void SharedArtwork_Changed(string url) { if (!QueueViewModel.IsClosing && Item?.ImageLocation == url) Update(); }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(QueueObject.LocalBitmapImage) or nameof(QueueObject.ArtworkState) or nameof(SongSearchObject.ImageLocation))) return;
        if (DispatcherQueue.HasThreadAccess) Update();
        else DispatcherQueue.TryEnqueue(() => { if (loaded) Update(); });
    }

    private void Tile_Loaded(object sender, RoutedEventArgs args)
    {
        if (loaded || QueueViewModel.IsClosing) return;
        loaded = true;
        inViewport = true;
        ObserveItem();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && hasAnimationEvent) uiSettings.AnimationsEnabledChanged += Animations_Changed;
        uiSettings.ColorValuesChanged += Colors_Changed;
        Update();
    }

    private void Tile_Unloaded(object sender, RoutedEventArgs args)
    {
        loaded = false;
        ObserveItem();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && hasAnimationEvent) uiSettings.AnimationsEnabledChanged -= Animations_Changed;
        uiSettings.ColorValuesChanged -= Colors_Changed;
        WatchMotion(false);
        StopPulse();
        ClearUrlImage();
        if (!QueueViewModel.IsClosing) CoverImage.Source = null;
    }

    private void Tile_EffectiveViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        if (QueueViewModel.IsClosing) return;
        var viewport = args.EffectiveViewport;
        inViewport = viewport.Right > 0 && viewport.Bottom > 0 && viewport.X < ActualWidth && viewport.Y < ActualHeight;
        UpdatePulse();
    }

    private void Animations_Changed(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args) => SettingsChanged();
    private void Colors_Changed(UISettings sender, object args) => SettingsChanged();
    private void SettingsChanged()
    {
        if (!QueueViewModel.IsClosing) DispatcherQueue.TryEnqueue(() => { if (loaded && !QueueViewModel.IsClosing) UpdatePulse(); });
    }

    private void Update()
    {
        if (CoverImage == null || QueueViewModel.IsClosing) return;
        var image = Item?.LocalBitmapImage as ImageSource;
        var hasImage = image != null && !ReferenceEquals(image, QueueViewModel.ArtworkPlaceholder);
        var state = (Item as QueueObject)?.ArtworkState ?? QueueArtworkState.Unavailable;
        if (!hasImage && !UseUrlFallback && Item != null && QueueViewModel.TryGetArtwork(Item.ImageLocation, out var shared, out var sharedState))
        {
            image = shared;
            hasImage = shared != null;
            state = sharedState;
        }
        var loading = !hasImage && state == QueueArtworkState.Loading;
        ImageSource? fallback = Item == null || (Item.Source == SourceIds.Local && Item is not QueueObject && !UseUrlFallback)
            ? null : QueueViewModel.ArtworkPlaceholder;
        if (hasImage)
        {
            ClearUrlImage();
            CoverImage.Source = image;
        }
        else if (UseUrlFallback && Item != null && Uri.TryCreate(Item.ImageLocation, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            if (urlKey != uri.AbsoluteUri)
            {
                ClearUrlImage();
                urlKey = uri.AbsoluteUri;
                urlBitmap = new BitmapImage();
                urlBitmap.ImageOpened += Url_ImageOpened;
                urlBitmap.ImageFailed += Url_ImageFailed;
                urlBitmap.UriSource = uri;
            }
            loading = !urlReady && !urlFailed;
            CoverImage.Source = urlFailed ? fallback : urlBitmap;
            hasImage = urlReady && !urlFailed;
        }
        else
        {
            ClearUrlImage();
            CoverImage.Source = loading ? null : fallback;
        }
        Skeleton.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        var status = loading ? "QueueCoverLoading".GetLocalized() : hasImage ? string.Empty : "QueueCoverUnavailable".GetLocalized();
        ToolTipService.SetToolTip(this, status.Length > 0 ? status : null);
        AutomationProperties.SetName(this, status);
        UpdatePulse();
    }

    private void Url_ImageOpened(object sender, RoutedEventArgs args)
    {
        if (QueueViewModel.IsClosing || !ReferenceEquals(sender, urlBitmap)) return;
        urlReady = true;
        Update();
    }

    private void Url_ImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        if (QueueViewModel.IsClosing || !ReferenceEquals(sender, urlBitmap)) return;
        urlFailed = true;
        Update();
    }

    private void ClearUrlImage()
    {
        if (urlBitmap != null && !QueueViewModel.IsClosing)
        {
            urlBitmap.ImageOpened -= Url_ImageOpened;
            urlBitmap.ImageFailed -= Url_ImageFailed;
        }
        urlBitmap = null;
        urlKey = null;
        urlReady = urlFailed = false;
    }

    private void UpdatePulse()
    {
        if (QueueViewModel.IsClosing) return;
        var visibleLoading = loaded && inViewport && IsSkeletonVisible;
        WatchMotion(visibleLoading);
        if (!visibleLoading)
        {
            StopPulse();
            return;
        }
        if (!motionEnabled || accessibility.HighContrast)
        {
            StopPulse();
            ElementCompositionPreview.GetElementVisual(Skeleton).Opacity = accessibility.HighContrast ? 1f : 0.4f;
            return;
        }
        if (pulse != null) return;
        skeletonVisual = ElementCompositionPreview.GetElementVisual(Skeleton);
        var compositor = skeletonVisual.Compositor;
        pulse = compositor.CreateScalarKeyFrameAnimation();
        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0), new Vector2(0.6f, 1));
        pulse.InsertKeyFrame(0, 0.28f);
        pulse.InsertKeyFrame(0.5f, 0.5f, easing);
        pulse.InsertKeyFrame(1, 0.28f, easing);
        pulse.Duration = TimeSpan.FromSeconds(1.6);
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        skeletonVisual.StartAnimation("Opacity", pulse);
        easing.Dispose();
    }

    private void WatchMotion(bool enabled)
    {
        if (!enabled)
        {
            motionObservers.Remove(this);
            if (motionObservers.Count == 0) motionTimer?.Stop();
            return;
        }
        if (!motionObservers.Add(this) || motionObservers.Count != 1) return;
        motionEnabled = new UISettings().AnimationsEnabled;
        if (motionTimer == null)
        {
            // One preference check for all visible loading tiles, not a timer per row or an animation frame loop.
            motionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            motionTimer.Tick += (_, _) =>
            {
                if (QueueViewModel.IsClosing) { motionTimer.Stop(); motionObservers.Clear(); return; }
                // A fresh settings instance also handles desktops that do not deliver the WinRT change event.
                var current = new UISettings().AnimationsEnabled;
                if (current == motionEnabled) return;
                motionEnabled = current;
                foreach (var tile in motionObservers.ToArray()) tile.UpdatePulse();
            };
        }
        motionTimer.Start();
    }

    private void StopPulse()
    {
        if (!QueueViewModel.IsClosing)
        {
            skeletonVisual?.StopAnimation("Opacity");
            pulse?.Dispose();
        }
        pulse = null;
        skeletonVisual = null;
    }
}
