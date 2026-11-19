using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class ClipboardSnapshotChecks
{
    public static void Run(Action<bool, string> check)
    {
        string[] paths = { @"C:\Crest-synthetic\first.txt", @"C:\Crest-synthetic\second.txt" };
        byte[] audioBytes = { 1, 2, 3, 4 }, dibBytes = { 5, 6, 7, 8 }, markerBytes = { 0, 0, 0, 0 };
        using var audio = new MemoryStream(audioBytes);
        using var dib = new MemoryStream(dibBytes);
        using var marker = new MemoryStream(markerBytes);
        audio.Position = 2; dib.Position = 1; marker.Position = 3;
        var original = new DataObject();
        foreach (string format in new[] { DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText, DataFormats.StringFormat, DataFormats.Html, DataFormats.Rtf, DataFormats.CommaSeparatedValue })
            original.SetData(format, "Crest synthetic detached value for " + format, false);
        original.SetData(DataFormats.FileDrop, paths, false);
        original.SetData(DataFormats.WaveAudio, audio, false);
        original.SetData(DataFormats.Dib, dib, false);
        original.SetData("CanIncludeInClipboardHistory", marker, false);
        original.SetData("ExcludeClipboardContentFromMonitorProcessing", 1, false);
        var bitmap = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { 10, 20, 30, 255 }, 4, 0);
        original.SetData(DataFormats.Bitmap, bitmap, false);
        var lazy = new InvalidatableDataObject(original);
        var snapshot = ClipboardSnapshot.Capture(lazy);
        var expected = snapshot.CreateDataObject();
        bool positionsPreserved = audio.Position == 2 && dib.Position == 1 && marker.Position == 3;
        lazy.Invalidate();
        paths[0] = "changed"; audioBytes[0] = 99; dibBytes[0] = 99; markerBytes[0] = 99;
        bitmap.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { 99, 99, 99, 255 }, 4, 0);
        audio.Dispose(); dib.Dispose(); marker.Dispose();
        bool sourceInvalidated = false;
        try { lazy.GetData(DataFormats.UnicodeText, false); } catch (InvalidOperationException) { sourceInvalidated = true; }
        var detached = snapshot.CreateDataObject();
        check(sourceInvalidated && positionsPreserved && snapshot.Matches(expected) && snapshot.Matches(detached)
            && ((string[])detached.GetData(DataFormats.FileDrop, false)!)[0] == @"C:\Crest-synthetic\first.txt"
            && ((MemoryStream)detached.GetData(DataFormats.WaveAudio, false)!).ToArray().SequenceEqual(new byte[] { 1, 2, 3, 4 })
            && ((MemoryStream)detached.GetData(DataFormats.Dib, false)!).ToArray().SequenceEqual(new byte[] { 5, 6, 7, 8 }),
            "Clipboard backup detaches lazy OLE values, arrays, bitmap pixels and complete streams before owner invalidation or disposal");
        var unsupported = new DataObject();
        unsupported.SetData("Crest synthetic unsupported format", new object(), false);
        bool rejected = false;
        try { ClipboardSnapshot.Capture(unsupported); } catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Clipboard backup rejects unsupported formats before any native clipboard write");
    }
    private sealed class InvalidatableDataObject(IDataObject source) : IDataObject
    {
        private bool _invalidated;
        public void Invalidate() => _invalidated = true;
        private IDataObject Source => !_invalidated ? source : throw new InvalidOperationException("Synthetic lazy OLE source was invalidated.");
        public object GetData(string format, bool autoConvert) => Source.GetData(format, autoConvert);
        public object GetData(string format) => Source.GetData(format);
        public object GetData(Type format) => Source.GetData(format);
        public bool GetDataPresent(string format, bool autoConvert) => Source.GetDataPresent(format, autoConvert);
        public bool GetDataPresent(string format) => Source.GetDataPresent(format);
        public bool GetDataPresent(Type format) => Source.GetDataPresent(format);
        public string[] GetFormats(bool autoConvert) => Source.GetFormats(autoConvert);
        public string[] GetFormats() => Source.GetFormats();
        public void SetData(string format, object data, bool autoConvert) => throw new NotSupportedException();
        public void SetData(string format, object data) => throw new NotSupportedException();
        public void SetData(Type format, object data) => throw new NotSupportedException();
        public void SetData(object data) => throw new NotSupportedException();
    }
}
