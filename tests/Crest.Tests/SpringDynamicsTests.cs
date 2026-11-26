using Crest.Core;

internal static class SpringDynamicsTests
{
    public static int Run()
    {
        int passed = 0;
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        void Near(double actual, double expected, double tolerance = 1e-7)
        {
            if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException($"Spring sample {actual} differs from {expected} by more than {tolerance}.");
        }
        void Check(bool value) { if (!value) throw new InvalidOperationException("Spring physics outcome failed."); }

        Test("Spring sampling retains position and velocity at a retarget boundary", () =>
        {
            foreach (double damping in new[] { .86, 1d })
            {
                var moving = SpringDynamics.Sample(260, 0, 820, .083, .32, damping);
                var reversed = SpringDynamics.Sample(moving.Position, moving.Velocity, 260, 0, .32, damping);
                Near(reversed.Position, moving.Position);
                Near(reversed.Velocity, moving.Velocity);
                double step = 1e-6;
                var after = SpringDynamics.Sample(moving.Position, moving.Velocity, 260, step, .32, damping);
                var next = SpringDynamics.Sample(moving.Position, moving.Velocity, 260, 2 * step, .32, damping);
                // Second-order derivative avoids mistaking expected spring acceleration for a velocity discontinuity.
                Near((-3 * reversed.Position + 4 * after.Position - next.Position) / (2 * step), reversed.Velocity, .01);
            }
        });

        Test("Spring motion is independent of 30, 60, 120 and 144 Hz sampling", () =>
        {
            foreach (double damping in new[] { .86, 1d })
            {
                var direct = SpringDynamics.Sample(52, -15, 150, 1, .32, damping);
                foreach (int rate in new[] { 30, 60, 120, 144 })
                {
                    (double Position, double Velocity) sampled = (52, -15);
                    for (int frame = 0; frame < rate; frame++)
                        sampled = SpringDynamics.Sample(sampled.Position, sampled.Velocity, 150, 1d / rate, .32, damping);
                    Near(sampled.Position, direct.Position);
                    Near(sampled.Velocity, direct.Velocity);
                }
            }
        });

        Test("Geometry springs settle within a subpixel displacement and bounded residual velocity", () =>
        {
            foreach (double damping in new[] { .86, 1d })
            {
                double settling = SpringDynamics.SettlingTime(.32, damping);
                foreach (double velocity in new[] { -3000d, 0d, 3000d })
                {
                    var settled = SpringDynamics.Sample(260, velocity, 820, settling, .32, damping);
                    Near(settled.Position, 820, .25);
                    Near(settled.Velocity, 0, 5);
                }
            }
        });

        Test("One hundred rapid spring reversals remain finite and converge to the final target", () =>
        {
            (double Position, double Velocity) current = (260, 0);
            for (int index = 0; index < 100; index++)
            {
                double target = index % 2 == 0 ? 820 : 260;
                current = SpringDynamics.Sample(current.Position, current.Velocity, target, .013, .32, .86);
                Check(double.IsFinite(current.Position) && double.IsFinite(current.Velocity)
                    && current.Position is > 100 and < 1000 && Math.Abs(current.Velocity) < 10000);
            }
            current = SpringDynamics.Sample(current.Position, current.Velocity, 820, 2, .32, .86);
            Near(current.Position, 820, 1e-6);
            Near(current.Velocity, 0, 1e-5);
        });

        Test("Critical opacity spring stays monotonic while a stalled frame still reaches its target", () =>
        {
            double prior = 0;
            for (int index = 0; index < 100; index++)
            {
                double position = SpringDynamics.Sample(0, 0, 1, index / 120d, .22, 1).Position;
                Check(position >= prior && position is >= 0 and <= 1);
                prior = position;
            }
            var stalled = SpringDynamics.Sample(0, 0, 1, 10, .22, 1);
            Near(stalled.Position, 1);
            Near(stalled.Velocity, 0);
        });
        return passed;
    }
}
