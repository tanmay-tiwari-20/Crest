using Crest.Core;

internal static class ControlEditTests
{
    public static int Run()
    {
        int passed = 0;
        void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        void Check(bool result) { if (!result) throw new Exception("Pending control edit outcome failed"); }
        Test("Hardware refresh cannot overwrite a value awaiting debounce", () =>
        {
            var edit = new PendingControlEdit();
            edit.Record(80);
            Check(edit.DisplayValue(20, 0) == 80);
            Check(edit.TryBeginWrite(out var write) && write.Value == 80);
            Check(edit.DisplayValue(20, 100) == 80);
        });
        Test("An older write cannot erase a newer requested adjustment", () =>
        {
            var edit = new PendingControlEdit();
            edit.Record(80); edit.TryBeginWrite(out var first);
            edit.Record(35);
            Check(!edit.TryBeginWrite(out _));
            Check(edit.CompleteWrite(first, 100) && edit.DisplayValue(80, 101) == 35);
            Check(edit.TryBeginWrite(out var latest) && latest.Value == 35);
            edit.CompleteWrite(latest, 200);
            Check(!edit.HasPendingEdit && edit.DisplayValue(35, 201) == 35);
        });
        Test("Canceled capability state rejects a stale completion after recovery", () =>
        {
            var edit = new PendingControlEdit();
            edit.Record(80); edit.TryBeginWrite(out var obsolete);
            edit.Cancel();
            Check(!edit.HasPendingEdit && edit.DisplayValue(20, 0) == 20);
            edit.Record(45); edit.TryBeginWrite(out var current);
            edit.CompleteWrite(obsolete, 100);
            Check(edit.IsWriting && edit.DisplayValue(80, 100) == 45);
            edit.CompleteWrite(current, 200);
            Check(!edit.HasPendingEdit && !edit.IsWriting);
        });
        Test("Seek waits for a lagging player timeline to acknowledge the target", () =>
        {
            var edit = new PendingControlEdit();
            edit.Record(200); edit.TryBeginWrite(out var seek);
            edit.CompleteWrite(seek, 100, 1500);
            Check(edit.DisplayValue(10, 200, 2) == 200);
            Check(edit.DisplayValue(201, 300, 2) == 201 && !edit.HasPendingEdit);
        });
        Test("Unacknowledged seek returns to actual player data after bounded grace", () =>
        {
            var edit = new PendingControlEdit();
            edit.Record(200); edit.TryBeginWrite(out var seek);
            edit.CompleteWrite(seek, 100, 1500);
            Check(edit.DisplayValue(10, 1599, 2) == 200);
            Check(edit.DisplayValue(10, 1600, 2) == 10 && !edit.HasPendingEdit);
        });
        return passed;
    }
}
