using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace GlobalTranslator
{
    // Small motion helpers. Sharkey stays quiet: short fades only, and nothing
    // animates when the user asked Windows to reduce motion or uses high contrast.
    internal static class UiMotion
    {
        internal static bool Enabled
        {
            get { return SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast; }
        }

        internal static void FadeIn(UIElement element, double from = 0, int milliseconds = 160)
        {
            if (element == null) return;
            if (!Enabled) { element.Opacity = 1; return; }
            element.Opacity = from;
            var animation = new DoubleAnimation(1, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += delegate { element.BeginAnimation(UIElement.OpacityProperty, null); element.Opacity = 1; };
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        internal static void FadeTo(UIElement element, double target, int milliseconds = 120)
        {
            if (element == null) return;
            if (!Enabled) { element.Opacity = target; return; }
            var animation = new DoubleAnimation(target, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += delegate
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = target;
            };
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }
    }
}
