using Crest.Core;

internal static class BatteryNoticeTests
{
    public static int Run()
    {
        int passed = 0;
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        void Check(bool value) { if (!value) throw new Exception("Battery notice outcome failed."); }

        Test("OS low battery remains pending after charging before the next reveal", () =>
        {
            var state = new BatteryNoticeState();
            state.Observe(true, 2, 12, false);
            var low = state.Pending;
            state.Observe(true, 10, 18, true);
            Check(low is { Severity: BatteryWarningLevel.Low, Percent: 12 } && state.Pending == low);
            Check(state.Acknowledge(low!.Id) && state.Pending is null);
        });
        Test("Repeated low readings do not duplicate an already displayed episode", () =>
        {
            var state = new BatteryNoticeState();
            state.Observe(true, 2, 14, false);
            var low = state.Pending!;
            state.Observe(true, 2, 11, false);
            Check(state.Pending == low);
            Check(state.Acknowledge(low.Id));
            Parallel.For(0, 100, _ => state.Observe(true, 2, 9, false));
            Check(state.Pending is null);
        });
        Test("Critical escalation survives acknowledgement of an older low snapshot", () =>
        {
            var state = new BatteryNoticeState();
            state.Observe(true, 2, 14, false);
            var low = state.Pending!;
            state.Observe(true, 6, 4, false);
            var critical = state.Pending;
            Check(critical is { Severity: BatteryWarningLevel.Critical, Percent: 4 } && critical.Id != low.Id);
            Check(!state.Acknowledge(low.Id) && state.Pending == critical);
            Check(state.Acknowledge(critical!.Id));
            state.Observe(true, 2, 8, false);
            state.Observe(true, 4, 3, false);
            Check(state.Pending is null);
        });
        Test("Known recovery allows a fresh low episode with a new identity", () =>
        {
            var state = new BatteryNoticeState();
            state.Observe(true, 2, 12, false);
            long first = state.Pending!.Id;
            state.Acknowledge(first);
            state.Observe(true, 1, 70, false);
            state.Observe(true, 2, 13, false);
            Check(state.Pending is { Severity: BatteryWarningLevel.Low, Percent: 13 } && state.Pending.Id != first);
        });
        Test("Unavailable battery reads preserve pending state and cannot create a fresh episode", () =>
        {
            var state = new BatteryNoticeState();
            state.Observe(true, 2, 10, false);
            var low = state.Pending!;
            state.Observe(false, 0, 0, false);
            state.Observe(true, 255, 0, false);
            state.Observe(true, 130, 0, false);
            state.Observe(true, 0, 255, false);
            Check(state.Pending == low);
            state.Acknowledge(low.Id);
            state.Observe(false, 0, 0, false);
            state.Observe(true, 2, 9, false);
            Check(state.Pending is null);
        });
        Test("Battery warning severity comes from Windows flags rather than an invented percentage threshold", () =>
        {
            var state = new BatteryNoticeState();
            state.Observe(true, 0, 3, false);
            state.Observe(true, 2, 9, true);
            state.Observe(true, 10, 9, false);
            Check(state.Pending is null);
            state.Observe(true, 4, 20, false);
            Check(state.Pending is { Severity: BatteryWarningLevel.Critical, Percent: 20 });
        });
        Test("Hidden episodes stay bounded to one notice and retain the more severe unshown warning", () =>
        {
            var state = new BatteryNoticeState();
            state.Observe(true, 4, 4, false);
            var critical = state.Pending!;
            for (int i = 0; i < 100; i++)
            {
                state.Observe(true, 8, 30, true);
                state.Observe(true, 2, 15, false);
            }
            Check(state.Pending == critical);
            state.Acknowledge(critical.Id);
            state.Observe(true, 4, 3, false);
            Check(state.Pending is { Severity: BatteryWarningLevel.Critical, Percent: 3 });
        });
        Test("Reset clears retained warnings without allowing stale acknowledgement of a new warning", () =>
        {
            var state = new BatteryNoticeState();
            state.Observe(true, 2, 15, false);
            long beforeReset = state.Pending!.Id;
            state.Clear();
            Check(state.Pending is null);
            state.Observe(true, 2, 15, false);
            Check(state.Pending is not null && !state.Acknowledge(beforeReset));
        });
        Test("Concurrent warning reads produce a single consumable pending snapshot", () =>
        {
            var state = new BatteryNoticeState();
            Parallel.For(0, 100, _ => state.Observe(true, 2, 10, false));
            var notice = state.Pending!;
            int acknowledgements = 0;
            Parallel.For(0, 100, _ => { if (state.Acknowledge(notice.Id)) Interlocked.Increment(ref acknowledgements); });
            Check(acknowledgements == 1 && state.Pending is null);
        });
        return passed;
    }
}
