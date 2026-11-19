using Crest.Core;

internal static class FoundationTests
{
    public static int Run()
    {
        int passed = 0;
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        void Check(bool value) { if (!value) throw new Exception("Desktop interaction outcome failed"); }
        InteractionStateMachine Open()
        {
            var machine = new InteractionStateMachine();
            machine.SetEligibility(true, false);
            machine.Tick(0, true, true);
            machine.Tick(120, true, true);
            machine.CompleteReveal(machine.TransitionVersion);
            return machine;
        }

        Test("Stationary top-edge pointer waits for a fresh entry after startup and recovery", () =>
        {
            var machine = new InteractionStateMachine();
            int reveals = 0;
            machine.RevealRequested += (_, _) => reveals++;
            machine.SetEligibility(true, true);
            machine.Tick(0, true, true); machine.Tick(1000, true, true);
            Check(reveals == 0 && machine.State == IslandState.Armed);
            machine.Tick(1001, false, false); machine.Tick(1010, true, true); machine.Tick(1130, true, true);
            Check(reveals == 1);
            machine.SetEligibility(false, true); machine.SetEligibility(true, true);
            machine.Tick(5000, true, true);
            Check(reveals == 1 && machine.State == IslandState.Armed);
        });
        Test("Leaving before dwell emits no reveal or visible close", () =>
        {
            var machine = new InteractionStateMachine();
            int events = 0;
            machine.RevealRequested += (_, _) => events++;
            machine.CloseRequested += (_, _) => events++;
            machine.SetEligibility(true, false);
            machine.Tick(0, true, true); machine.Tick(70, false, false); machine.Tick(1000, false, false);
            Check(events == 0 && machine.State == IslandState.Armed);
        });
        Test("Losing eligibility interrupts dwell, reveal, open and closing immediately", () =>
        {
            foreach (var startingState in new[] { IslandState.HoverPending, IslandState.Revealing, IslandState.Open, IslandState.Closing })
            {
                var machine = new InteractionStateMachine();
                int suppressed = 0;
                machine.SuppressRequested += (_, _) => suppressed++;
                machine.SetEligibility(true, false);
                machine.Tick(0, true, true);
                if (startingState != IslandState.HoverPending) machine.Tick(120, true, true);
                if (startingState == IslandState.Open || startingState == IslandState.Closing) machine.CompleteReveal(machine.TransitionVersion);
                if (startingState == IslandState.Closing) { machine.Tick(130, false, false); machine.Tick(380, false, false); }
                Check(machine.State == startingState);
                long obsolete = machine.TransitionVersion;
                machine.SetEligibility(false, false);
                machine.CompleteReveal(obsolete); machine.CompleteClose(obsolete); machine.Tick(9000, true, true);
                Check(machine.State == IslandState.Ineligible && suppressed == 1);
            }
        });
        Test("Old reveal callbacks cannot complete a newer reveal", () =>
        {
            var machine = new InteractionStateMachine();
            machine.SetEligibility(true, false); machine.Tick(0, true, true); machine.Tick(120, true, true);
            long oldVersion = machine.TransitionVersion;
            machine.SetEligibility(false, false); machine.SetEligibility(true, false);
            machine.Tick(130, true, true); machine.Tick(250, true, true);
            machine.CompleteReveal(oldVersion);
            Check(machine.State == IslandState.Revealing);
            machine.CompleteReveal(machine.TransitionVersion);
            Check(machine.State == IslandState.Open);
        });
        Test("Pointer grace closes once and re-entry reverses without a stale close", () =>
        {
            var machine = Open();
            int closes = 0, reveals = 0;
            machine.CloseRequested += (_, _) => closes++;
            machine.RevealRequested += (_, _) => reveals++;
            machine.Tick(200, false, false); machine.Tick(449, false, false);
            Check(machine.State == IslandState.Open);
            machine.Tick(450, false, false);
            long oldClose = machine.TransitionVersion;
            machine.Tick(460, false, true); machine.CompleteClose(oldClose);
            Check(machine.State == IslandState.Revealing && closes == 1 && reveals == 1);
        });
        Test("Registered focus and pointer corridor retain an open island", () =>
        {
            var machine = Open();
            machine.Tick(200, false, true); machine.Tick(20000, false, true);
            Check(machine.State == IslandState.Open);
        });
        Test("Escape closes even while the pointer remains over controls", () =>
        {
            var machine = Open();
            machine.Dismiss();
            long close = machine.TransitionVersion;
            machine.Tick(200, true, true);
            Check(machine.State == IslandState.Closing);
            machine.CompleteClose(close); machine.Tick(1000, true, true);
            Check(machine.State == IslandState.Armed);
        });
        Test("Pause deactivates all reveal paths and resume needs fresh entry", () =>
        {
            var machine = Open();
            machine.SetPaused(true, true);
            machine.Tick(1000, true, true); machine.RevealAccessibility();
            Check(machine.State == IslandState.Paused);
            machine.SetPaused(false, true); machine.Tick(2000, true, true);
            Check(machine.State == IslandState.Armed);
        });
        Test("100 interrupted hover cycles produce no stale reopen or stuck state", () =>
        {
            var machine = new InteractionStateMachine();
            int reveals = 0;
            machine.RevealRequested += (_, _) => reveals++;
            for (int i = 0; i < 100; i++)
            {
                long now = i * 1000;
                machine.SetEligibility(true, false);
                machine.Tick(now, true, true);
                machine.Tick(now + 120, true, true);
                long version = machine.TransitionVersion;
                machine.SetEligibility(false, true);
                machine.CompleteReveal(version);
                machine.SetEligibility(true, true);
                machine.Tick(now + 900, true, true);
                Check(machine.State == IslandState.Armed);
                machine.SetEligibility(false, false);
            }
            Check(reveals == 100);
        });
        Test("Repeated accessibility reveal keeps one transition and existing open state", () =>
        {
            var machine = new InteractionStateMachine();
            int reveals = 0;
            machine.RevealRequested += (_, _) => reveals++;
            machine.RevealAccessibility();
            Check(reveals == 0 && machine.State == IslandState.Ineligible);
            machine.SetEligibility(true, true);
            machine.RevealAccessibility();
            long version = machine.TransitionVersion;
            for (int i = 0; i < 100; i++) machine.RevealAccessibility();
            Check(reveals == 1 && machine.TransitionVersion == version && machine.State == IslandState.Revealing);
            machine.CompleteReveal(version);
            machine.RevealAccessibility();
            Check(reveals == 1 && machine.State == IslandState.Open);
            machine.Dismiss();
            machine.RevealAccessibility();
            Check(reveals == 2 && machine.State == IslandState.Revealing);
        });
        return passed;
    }
}
