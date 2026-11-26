using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Animation;
using Crest.Core;

namespace Crest.UI;

/// <summary>Native WPF clocks with analytic spring sampling and velocity-preserving retargets.</summary>
public sealed class SpringMotion
{
    private sealed record Track(double[] From, double[] Velocity, double[] Target, double Response, double Damping)
    {
        public AnimationClock? Clock { get; set; }
    }
    private readonly Dictionary<(DependencyObject, DependencyProperty), Track> _tracks = new();

    public void Double(DependencyObject owner, DependencyProperty property, double target, double speed = 1, double response = .22, double damping = 1, Action? completed = null)
    {
        response /= speed;
        var key = (owner, property);
        double from = (double)owner.GetValue(property);
        double velocity = Velocity(key, 1)[0];
        var track = new Track(new[] { from }, new[] { velocity }, new[] { target }, response, damping);
        _tracks[key] = track;
        owner.SetValue(property, from);
        var animation = new ScalarSpring(track.From[0], velocity, target, response, damping, property == UIElement.OpacityProperty);
        Finish(animation, owner, property, track, completed);
        ((IAnimatable)owner).ApplyAnimationClock(property, track.Clock, HandoffBehavior.SnapshotAndReplace);
    }

    public void Rect(DependencyObject owner, DependencyProperty property, Rect target, double speed = 1, Action? completed = null)
    {
        var key = (owner, property);
        Rect current = (Rect)owner.GetValue(property);
        double[] from = { current.X, current.Y, current.Width, current.Height };
        double[] to = { target.X, target.Y, target.Width, target.Height };
        var track = new Track(from, Velocity(key, 4), to, .32 / speed, .86);
        _tracks[key] = track;
        owner.SetValue(property, current);
        var animation = new RectangleSpring(track.From, track.Velocity, track.Target, track.Response, track.Damping);
        Finish(animation, owner, property, track, completed);
        ((IAnimatable)owner).ApplyAnimationClock(property, track.Clock, HandoffBehavior.SnapshotAndReplace);
    }

    private double[] Velocity((DependencyObject, DependencyProperty) key, int count)
    {
        var velocity = new double[count];
        if (!_tracks.TryGetValue(key, out var prior) || !((IAnimatable)key.Item1).HasAnimatedProperties) return velocity;
        double elapsed = prior.Clock?.CurrentTime?.TotalSeconds ?? 0;
        if (elapsed >= SpringDynamics.SettlingTime(prior.Response, prior.Damping)) return velocity;
        for (int i = 0; i < count; i++)
            velocity[i] = SpringDynamics.Sample(prior.From[i], prior.Velocity[i], prior.Target[i], elapsed, prior.Response, prior.Damping).Velocity;
        return velocity;
    }

    private void Finish(AnimationTimeline animation, DependencyObject owner, DependencyProperty property, Track track, Action? completed)
    {
        track.Clock = animation.CreateClock();
        track.Clock.Completed += (_, _) =>
        {
            var key = (owner, property);
            if (!_tracks.TryGetValue(key, out var current) || !ReferenceEquals(current, track)) return;
            _tracks.Remove(key);
            owner.SetValue(property, track.Target.Length == 1 ? (object)track.Target[0] : new Rect(track.Target[0], track.Target[1], track.Target[2], track.Target[3]));
            ((IAnimatable)owner).BeginAnimation(property, null);
            completed?.Invoke();
        };
    }

    public void Stop(DependencyObject owner, DependencyProperty property)
    {
        object value = owner.GetValue(property);
        _tracks.Remove((owner, property));
        ((IAnimatable)owner).BeginAnimation(property, null);
        owner.SetValue(property, value);
    }

    public void StopAll()
    {
        foreach (var key in new List<(DependencyObject, DependencyProperty)>(_tracks.Keys)) Stop(key.Item1, key.Item2);
    }

    private sealed class ScalarSpring : DoubleAnimationBase
    {
        private readonly double _from, _velocity, _target, _response, _damping;
        private readonly bool _opacity;
        public ScalarSpring(double from, double velocity, double target, double response, double damping, bool opacity)
        {
            (_from, _velocity, _target, _response, _damping) = (from, velocity, target, response, damping);
            _opacity = opacity;
            Duration = TimeSpan.FromSeconds(SpringDynamics.SettlingTime(response, damping));
        }
        protected override Freezable CreateInstanceCore() => new ScalarSpring(_from, _velocity, _target, _response, _damping, _opacity);
        protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock clock)
        {
            double value = clock.CurrentProgress >= 1 ? _target : SpringDynamics.Sample(_from, _velocity, _target, clock.CurrentTime?.TotalSeconds ?? 0, _response, _damping).Position;
            return _opacity ? Math.Clamp(value, 0, 1) : value;
        }
    }

    private sealed class RectangleSpring : RectAnimationBase
    {
        private readonly double[] _from, _velocity, _target;
        private readonly double _response, _damping;
        public RectangleSpring(double[] from, double[] velocity, double[] target, double response, double damping)
        {
            (_from, _velocity, _target, _response, _damping) = (from, velocity, target, response, damping);
            Duration = TimeSpan.FromSeconds(SpringDynamics.SettlingTime(response, damping));
        }
        protected override Freezable CreateInstanceCore() => new RectangleSpring(_from, _velocity, _target, _response, _damping);
        protected override Rect GetCurrentValueCore(Rect defaultOriginValue, Rect defaultDestinationValue, AnimationClock clock)
        {
            double t = clock.CurrentTime?.TotalSeconds ?? 0;
            double Value(int i) => clock.CurrentProgress >= 1 ? _target[i] : SpringDynamics.Sample(_from[i], _velocity[i], _target[i], t, _response, _damping).Position;
            return new Rect(Value(0), Value(1), Math.Max(1, Value(2)), Math.Max(1, Value(3)));
        }
    }
}
