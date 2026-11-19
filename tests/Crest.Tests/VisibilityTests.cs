using Crest.Core;

internal static class VisibilityTests
{
    public static int Run()
    {
        int passed = 0;
        void Test(string name, Action outcome) { outcome(); passed++; Console.WriteLine("PASS " + name); }
        void Check(bool value) { if (!value) throw new InvalidOperationException("Visibility access outcome failed."); }
        var app = new VisibilityContext(true, false, false, true, true);
        var desktop = new VisibilityContext(true, true, false, true, false);
        var input = new VisibilityContext(true, false, true, false, false);
        var ownSettings = new VisibilityContext(true, false, false, false, false);

        Test("Desktop-only defaults reject foreground apps and covered desktop", () =>
        {
            var settings = new AppSettings();
            Check(!settings.AllowOverApps && !VisibilityRules.Allows(settings, app));
            Check(VisibilityRules.Allows(settings, desktop));
            Check(!VisibilityRules.Allows(settings, desktop with { CoveringApp = true }));
            settings.StrictDesktop = false;
            Check(VisibilityRules.Allows(settings, desktop with { CoveringApp = true }));
            Check(!VisibilityRules.Allows(settings, app));
        });
        Test("Opt-in app access permits other apps without overriding Settings or infrastructure checks", () =>
        {
            var settings = new AppSettings { AllowOverApps = true };
            Check(VisibilityRules.Allows(settings, app));
            Check(VisibilityRules.Allows(settings, desktop with { CoveringApp = true }));
            Check(VisibilityRules.Allows(settings, input with { CoveringApp = true }));
            Check(!VisibilityRules.Allows(settings, ownSettings));
            foreach (var context in new[] { app, desktop, input })
                Check(!VisibilityRules.Allows(settings, context with { EnvironmentReady = false }));
            settings.AllowOverApps = false;
            Check(!VisibilityRules.Allows(settings, app));
        });
        Test("App access persists explicitly and stays off for legacy, corrupt and reset preferences", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(), "CrestVisibilityTests-" + Guid.NewGuid().ToString("N"));
            var store = new SettingsStore(directory);
            store.Save(new AppSettings { AllowOverApps = true });
            Check(store.Load().AllowOverApps);
            store.Save(new AppSettings { AllowOverApps = false });
            Check(!store.Load().AllowOverApps);
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"SchemaVersion\":1,\"StrictDesktop\":false}");
            Check(!store.Load().AllowOverApps);
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{invalid}");
            Check(!store.Load().AllowOverApps);
            store.Clear();
            Check(!store.Load().AllowOverApps);
            // SettingsStore.Clear removes only these test-owned records; no user configuration is used.
        });
        Test("Changing access modes cancels stale reveals and never auto-opens over an app", () =>
        {
            var machine = new InteractionStateMachine();
            var settings = new AppSettings { AllowOverApps = true };
            int reveals = 0;
            machine.RevealRequested += (_, _) => reveals++;
            machine.SetEligibility(VisibilityRules.Allows(settings, app), true);
            machine.Tick(0, true, true); machine.Tick(1000, true, true);
            Check(reveals == 0);
            machine.Tick(1001, false, false); machine.Tick(1010, true, true); machine.Tick(1130, true, true);
            long obsolete = machine.TransitionVersion;
            Check(reveals == 1);
            // Native policy resets eligibility whenever the saved visibility mode changes.
            machine.SetEligibility(false, true);
            settings.AllowOverApps = false;
            machine.SetEligibility(VisibilityRules.Allows(settings, app), true);
            machine.CompleteReveal(obsolete);
            Check(machine.State == IslandState.Ineligible);
            settings.AllowOverApps = true;
            machine.SetEligibility(VisibilityRules.Allows(settings, app), true);
            machine.Tick(2000, true, true);
            Check(machine.State == IslandState.Armed && reveals == 1);
            machine.SetPaused(true, true); machine.RevealAccessibility();
            Check(machine.State == IslandState.Paused && reveals == 1);
        });
        return passed;
    }
}
