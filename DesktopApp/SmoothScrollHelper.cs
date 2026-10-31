using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace OpenDualHub
{
    public static class SmoothScrollHelper
    {
        public static readonly DependencyProperty SmoothScrollProperty =
            DependencyProperty.RegisterAttached(
                "SmoothScroll",
                typeof(bool),
                typeof(SmoothScrollHelper),
                new PropertyMetadata(false, OnSmoothScrollChanged));

        public static bool GetSmoothScroll(DependencyObject obj) => (bool)obj.GetValue(SmoothScrollProperty);
        public static void SetSmoothScroll(DependencyObject obj, bool value) => obj.SetValue(SmoothScrollProperty, value);

        private static void OnSmoothScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                if ((bool)e.NewValue)
                {
                    sv.CanContentScroll = false;
                    sv.PreviewMouseWheel += Sv_PreviewMouseWheel;
                }
                else
                {
                    sv.PreviewMouseWheel -= Sv_PreviewMouseWheel;
                }
            }
        }

        private static void Sv_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                e.Handled = true;
                ScrollSmoothly(sv, -e.Delta * 0.75);
            }
        }

        public static void ScrollSmoothly(ScrollViewer sv, double delta)
        {
            double current = sv.VerticalOffset;
            double target = Math.Clamp(current + delta, 0, Math.Max(0, sv.ScrollableHeight));
            if (Math.Abs(target - current) < 0.5) return;

            var timer = new DispatcherTimer(DispatcherPriority.Render);
            timer.Interval = TimeSpan.FromMilliseconds(14);
            int ticks = 0;
            const int maxTicks = 14;

            timer.Tick += (s, args) =>
            {
                ticks++;
                double remaining = target - sv.VerticalOffset;
                if (Math.Abs(remaining) < 1.0 || ticks >= maxTicks)
                {
                    sv.ScrollToVerticalOffset(target);
                    timer.Stop();
                }
                else
                {
                    sv.ScrollToVerticalOffset(sv.VerticalOffset + remaining * 0.38);
                }
            };
            timer.Start();
        }
    }
}
