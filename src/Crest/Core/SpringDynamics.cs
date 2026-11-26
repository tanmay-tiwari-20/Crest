using System;

namespace Crest.Core;

/// <summary>Closed-form unit-mass spring; sampling is stable at any display refresh rate.</summary>
public static class SpringDynamics
{
    public static (double Position, double Velocity) Sample(double from, double velocity, double target, double seconds, double response, double damping)
    {
        double omega = 2 * Math.PI / response;
        double t = Math.Max(0, seconds), displacement = from - target;
        if (damping >= 1)
        {
            double b = velocity + omega * displacement;
            double decay = Math.Exp(-omega * t);
            return (target + (displacement + b * t) * decay, (velocity - omega * b * t) * decay);
        }
        double attenuation = damping * omega, frequency = omega * Math.Sqrt(1 - damping * damping);
        double sine = Math.Sin(frequency * t), cosine = Math.Cos(frequency * t);
        double coefficient = (velocity + attenuation * displacement) / frequency;
        double envelope = Math.Exp(-attenuation * t);
        double offset = displacement * cosine + coefficient * sine;
        return (target + envelope * offset, envelope * ((coefficient * frequency - attenuation * displacement) * cosine - (displacement * frequency + attenuation * coefficient) * sine));
    }

    // Eleven time constants keep even a high-velocity interrupted trajectory subpixel at rest.
    public static double SettlingTime(double response, double damping) => 11 * response / (2 * Math.PI * Math.Min(1, damping));
}
