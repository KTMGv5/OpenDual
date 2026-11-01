using System;
using System.Collections.Generic;
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

        private sealed class ScrollState
        {
            public double TargetOffset;
            public DispatcherTimer? Timer;
        }

        private static readonly Dictionary<ScrollViewer, ScrollState> s_states = new();

        private static void OnSmoothScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                if ((bool)e.NewValue)
                {
                    sv.CanContentScroll = false;
                    sv.PreviewMouseWheel += Sv_PreviewMouseWheel;
                    sv.Unloaded += Sv_Unloaded;
                }
                else
                {
                    sv.PreviewMouseWheel -= Sv_PreviewMouseWheel;
                    sv.Unloaded -= Sv_Unloaded;
                    s_states.Remove(sv);
                }
            }
        }

        private static void Sv_Unloaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer sv && s_states.TryGetValue(sv, out var state))
            {
                state.Timer?.Stop();
                s_states.Remove(sv);
            }
        }

        private static void Sv_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                e.Handled = true;
                ScrollSmoothly(sv, -e.Delta * 0.72);
            }
        }

        public static void ScrollSmoothly(ScrollViewer sv, double delta)
        {
            if (!s_states.TryGetValue(sv, out var state))
            {
                state = new ScrollState { TargetOffset = sv.VerticalOffset };
                s_states[sv] = state;
            }

            if (state.Timer == null || !state.Timer.IsEnabled)
            {
                state.TargetOffset = sv.VerticalOffset;
            }

            state.TargetOffset = Math.Clamp(state.TargetOffset + delta, 0, Math.Max(0, sv.ScrollableHeight));

            if (state.Timer == null)
            {
                state.Timer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(12)
                };
                state.Timer.Tick += (s, args) =>
                {
                    double remaining = state.TargetOffset - sv.VerticalOffset;
                    if (Math.Abs(remaining) < 0.8)
                    {
                        sv.ScrollToVerticalOffset(state.TargetOffset);
                        state.Timer.Stop();
                    }
                    else
                    {
                        sv.ScrollToVerticalOffset(sv.VerticalOffset + remaining * 0.42);
                    }
                };
            }

            if (!state.Timer.IsEnabled)
            {
                state.Timer.Start();
            }
        }
    }
}
