using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BetterSS;

// Presentation only. Finite WPF clocks, never a permanent rendering/timer loop.
internal static class Motion
{
    internal const int Fast = 120, Standard = 180, Panel = 240, Exit = 110;
    internal static bool? ReducedOverride { get; set; }
    internal static bool Reduced => ReducedOverride ?? (!SystemParameters.ClientAreaAnimation || !SystemParameters.UIEffects || UI.HighContrast);
    private sealed record Running(Stopwatch Time, double Start, double End, double Velocity, double Omega);
    private static readonly ConditionalWeakTable<DependencyObject, Dictionary<DependencyProperty, Running>> active = new();

    internal static (double Position, double Velocity) Spring(double from, double to, double velocity, double omega, double seconds)
    {
        double offset = from - to, b = velocity + omega * offset, decay = Math.Exp(-omega * seconds);
        return (to + (offset + b * seconds) * decay, (b - omega * (offset + b * seconds)) * decay);
    }
    private static void Begin(DependencyObject target, DependencyProperty property, AnimationTimeline? animation)
    {
        if (target is UIElement visual) visual.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        else if (target is Animatable value) value.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        else throw new ArgumentException("Motion target must support WPF animation.");
    }
    internal static void Snap(DependencyObject target, DependencyProperty property, double value)
    {
        if (active.TryGetValue(target, out var states)) states.Remove(property);
        Begin(target, property, null); target.SetValue(property, value);
    }
    internal static void To(DependencyObject target, DependencyProperty property, double value, int milliseconds = Standard, bool spring = false, Action? completed = null)
    {
        double from = (double)target.GetValue(property);
        var states = active.GetOrCreateValue(target);
        double velocity = spring && states.TryGetValue(property, out var previous) && previous.Omega > 0
            ? Spring(previous.Start, previous.End, previous.Velocity, previous.Omega, previous.Time.Elapsed.TotalSeconds).Velocity : 0;
        if (Reduced || milliseconds <= 0 || (Math.Abs(from - value) < .00001 && Math.Abs(velocity) < .00001))
        { Snap(target, property, value); completed?.Invoke(); return; }
        double omega = 8.0 / (milliseconds / 1000.0);
        var state = new Running(Stopwatch.StartNew(), from, value, velocity, spring ? omega : 0); states[property] = state;
        AnimationTimeline animation = spring
            ? new SpringAnimation(from, value, velocity, omega, milliseconds)
            : new DoubleAnimation(from, value, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) =>
        {
            if (!states.TryGetValue(property, out var current) || !ReferenceEquals(current, state)) return;
            Snap(target, property, value); completed?.Invoke();
        };
        Begin(target, property, animation);
    }
    internal static void Enter(FrameworkElement visual, int duration = Panel, Point? origin = null)
    {
        // The render transform never affects layout or the outer hit target.
        if (Reduced)
        {
            if (visual.RenderTransform is ScaleTransform previous && HasClock(previous, ScaleTransform.ScaleXProperty))
            { Snap(previous, ScaleTransform.ScaleXProperty, 1); Snap(previous, ScaleTransform.ScaleYProperty, 1); }
            Snap(visual, UIElement.OpacityProperty, 1); return;
        }
        visual.RenderTransformOrigin = origin ?? new Point(.5, .5);
        bool continuing = visual.RenderTransform is ScaleTransform current && HasClock(current, ScaleTransform.ScaleXProperty);
        var scale = continuing ? (ScaleTransform)visual.RenderTransform : new ScaleTransform(.985, .985);
        visual.RenderTransform = scale;
        To(scale, ScaleTransform.ScaleXProperty, 1, duration, true);
        To(scale, ScaleTransform.ScaleYProperty, 1, duration, true);
        if (!continuing) Snap(visual, UIElement.OpacityProperty, .55);
        To(visual, UIElement.OpacityProperty, 1, Standard);
    }
    internal static bool HasClock(DependencyObject target, DependencyProperty property)
        => active.TryGetValue(target, out var states) && states.ContainsKey(property);

    private sealed class SpringAnimation : DoubleAnimationBase
    {
        private static readonly DependencyProperty FromProperty = DependencyProperty.Register("From", typeof(double), typeof(SpringAnimation));
        private static readonly DependencyProperty ToProperty = DependencyProperty.Register("To", typeof(double), typeof(SpringAnimation));
        private static readonly DependencyProperty VelocityProperty = DependencyProperty.Register("Velocity", typeof(double), typeof(SpringAnimation));
        private static readonly DependencyProperty OmegaProperty = DependencyProperty.Register("Omega", typeof(double), typeof(SpringAnimation));
        private double from { get => (double)GetValue(FromProperty); set => SetValue(FromProperty, value); }
        private double to { get => (double)GetValue(ToProperty); set => SetValue(ToProperty, value); }
        private double velocity { get => (double)GetValue(VelocityProperty); set => SetValue(VelocityProperty, value); }
        private double omega { get => (double)GetValue(OmegaProperty); set => SetValue(OmegaProperty, value); }
        public SpringAnimation() { }
        internal SpringAnimation(double start, double end, double speed, double frequency, int milliseconds)
        { from = start; to = end; velocity = speed; omega = frequency; Duration = TimeSpan.FromMilliseconds(milliseconds); }
        protected override Freezable CreateInstanceCore() => new SpringAnimation();
        protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock clock)
            => clock.CurrentProgress >= 1 ? to : Spring(from, to, velocity, omega, clock.CurrentTime?.TotalSeconds ?? 0).Position;
    }
}
