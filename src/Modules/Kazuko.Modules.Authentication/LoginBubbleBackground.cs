using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Kazuko.Modules.Authentication;

/// <summary>Decorative login backdrop; owns its animation clocks only while visible.</summary>
public sealed class LoginBubbleBackground : Canvas
{
    private readonly List<Storyboard> _animations = [];

    public LoginBubbleBackground()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => Refresh();
        SizeChanged += (_, _) => Refresh();
    }

    private void Refresh()
    {
        Stop();
        if (!IsLoaded || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        Clip = new RectangleGeometry(new Rect(RenderSize), 26, 26);
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)
            return;

        for (var index = 0; index < 16; index++)
        {
            var diameter = 18 + Random.Shared.NextDouble() * 66;
            var seconds = 16 + Random.Shared.NextDouble() * 14;
            var duration = TimeSpan.FromSeconds(seconds);
            var drift = 12 + Random.Shared.NextDouble() * 26;
            var bubble = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                StrokeThickness = 1.5,
                Opacity = 0,
                RenderTransform = new TranslateTransform()
            };
            bubble.SetResourceReference(Shape.StrokeProperty, "OnPrimaryBrush");
            // Keep the centres transparent so the backdrop stays light behind the branding.
            SetLeft(bubble, Random.Shared.NextDouble() * ActualWidth - diameter / 2);
            Children.Add(bubble);

            var rise = new DoubleAnimation(ActualHeight + diameter, -diameter * 2, duration);
            var sway = new DoubleAnimationUsingKeyFrames();
            sway.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            sway.KeyFrames.Add(new SplineDoubleKeyFrame(index % 2 == 0 ? drift : -drift,
                KeyTime.FromPercent(0.5), new KeySpline(0.4, 0, 0.6, 1)));
            sway.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromPercent(1),
                new KeySpline(0.4, 0, 0.6, 1)));
            sway.Duration = duration;
            var fade = new DoubleAnimationUsingKeyFrames { Duration = duration };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.12 + Random.Shared.NextDouble() * 0.16,
                KeyTime.FromPercent(0.12)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.2, KeyTime.FromPercent(0.8)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));

            var storyboard = new Storyboard { Duration = duration, RepeatBehavior = RepeatBehavior.Forever };
            AddAnimation(storyboard, rise, bubble, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            AddAnimation(storyboard, sway, bubble, "(UIElement.RenderTransform).(TranslateTransform.X)");
            AddAnimation(storyboard, fade, bubble, "Opacity");
            storyboard.Begin(this, true);
            // Start at different points, including the first frame, instead of a simultaneous wave.
            storyboard.Seek(this, TimeSpan.FromSeconds(seconds * index / 16), TimeSeekOrigin.BeginTime);
            _animations.Add(storyboard);
        }
    }

    private static void AddAnimation(Storyboard storyboard, Timeline animation, Ellipse bubble, string path)
    {
        Storyboard.SetTarget(animation, bubble);
        Storyboard.SetTargetProperty(animation, new PropertyPath(path));
        storyboard.Children.Add(animation);
    }

    private void Stop()
    {
        foreach (var animation in _animations)
            animation.Remove(this);
        _animations.Clear();
        Children.Clear();
    }
}
