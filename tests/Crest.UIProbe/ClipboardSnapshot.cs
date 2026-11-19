using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

/// <summary>Materializes clipboard values while their original OLE owner is still alive.</summary>
internal sealed class ClipboardSnapshot
{
    private const int MaximumBytes = 128 * 1024 * 1024;
    private static readonly HashSet<string> TextFormats = new(StringComparer.Ordinal)
    {
        DataFormats.UnicodeText, DataFormats.Text, DataFormats.OemText, DataFormats.StringFormat,
        DataFormats.Html, DataFormats.Rtf, DataFormats.CommaSeparatedValue
    };
    private static readonly HashSet<string> BinaryFormats = new(StringComparer.Ordinal)
    {
        DataFormats.Dib, DataFormats.WaveAudio, "ExcludeClipboardContentFromMonitorProcessing",
        "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard", "Preferred DropEffect",
        "Performed DropEffect", "Paste Succeeded"
    };
    // These describe OLE ownership/conversion rather than user clipboard content.
    private static readonly HashSet<string> BookkeepingFormats = new(StringComparer.Ordinal)
    {
        DataFormats.Locale, "Ole Private Data", "DataObject", "OleClipboardPersistOnFlush"
    };
    private readonly List<(string Format, object Value)> _values = new();
    public string? UnicodeText { get; private set; }
    public bool Empty => _values.Count == 0;

    public static ClipboardSnapshot Capture(IDataObject? source)
    {
        var snapshot = new ClipboardSnapshot();
        if (source is null) return snapshot;
        string[] formats = source.GetFormats(false).Distinct(StringComparer.Ordinal).ToArray();
        if (formats.Any(format => !TextFormats.Contains(format) && !BinaryFormats.Contains(format)
            && format != DataFormats.FileDrop && format != DataFormats.Bitmap && !BookkeepingFormats.Contains(format)))
            throw new InvalidOperationException("The original clipboard has an unsupported format. No synthetic clipboard changes were made.");
        if (source.GetDataPresent(DataFormats.UnicodeText, true))
            snapshot.UnicodeText = source.GetData(DataFormats.UnicodeText, true) as string
                ?? throw UnsafePayload();
        foreach (string format in formats.Where(format => !BookkeepingFormats.Contains(format)))
            snapshot._values.Add((format, Clone(format, source.GetData(format, false))));
        if (snapshot.UnicodeText is not null && !snapshot._values.Any(value => value.Format == DataFormats.UnicodeText))
            snapshot._values.Add((DataFormats.UnicodeText, snapshot.UnicodeText));
        if (!snapshot.Matches(snapshot.CreateDataObject()))
            throw new InvalidOperationException("The detached clipboard backup could not be verified. No synthetic clipboard changes were made.");
        return snapshot;
    }

    public DataObject CreateDataObject()
    {
        var data = new DataObject();
        foreach (var entry in _values) data.SetData(entry.Format, Clone(entry.Format, entry.Value), false);
        return data;
    }
    public bool Matches(IDataObject? data)
    {
        if (Empty) return data is null || data.GetFormats(false).All(BookkeepingFormats.Contains);
        if (data is null) return false;
        foreach (var entry in _values)
        {
            if (!data.GetDataPresent(entry.Format, true)) return false;
            object value = Clone(entry.Format, data.GetData(entry.Format, true));
            if (!Equal(entry.Value, value)) return false;
        }
        return UnicodeText is null || data.GetData(DataFormats.UnicodeText, true) as string == UnicodeText;
    }

    private static object Clone(string format, object? value)
    {
        if (TextFormats.Contains(format)) return value is string text ? text : throw UnsafePayload();
        if (format == DataFormats.FileDrop)
        {
            if (value is string[] paths) return paths.ToArray();
            if (value is StringCollection collection) return collection.Cast<string>().ToArray();
            throw UnsafePayload();
        }
        if (format == DataFormats.Bitmap && value is BitmapSource bitmap)
        {
            var normalized = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int stride = checked(normalized.PixelWidth * 4);
            int length = checked(stride * normalized.PixelHeight);
            if (length > MaximumBytes) throw UnsafePayload();
            byte[] pixels = new byte[length];
            normalized.CopyPixels(pixels, stride, 0);
            var detached = BitmapSource.Create(normalized.PixelWidth, normalized.PixelHeight, normalized.DpiX, normalized.DpiY,
                PixelFormats.Bgra32, null, pixels, stride);
            detached.Freeze();
            return detached;
        }
        if (BinaryFormats.Contains(format))
        {
            if (format != DataFormats.Dib && format != DataFormats.WaveAudio)
            {
                if (value is int signed) return new MemoryStream(BitConverter.GetBytes(signed), false);
                if (value is uint unsigned) return new MemoryStream(BitConverter.GetBytes(unsigned), false);
            }
            return new MemoryStream(ReadBytes(value), false);
        }
        throw UnsafePayload();
    }
    private static byte[] ReadBytes(object? value)
    {
        if (value is byte[] bytes) return bytes.Length <= MaximumBytes ? bytes.ToArray() : throw UnsafePayload();
        if (value is not Stream { CanRead: true, CanSeek: true } stream || stream.Length > MaximumBytes) throw UnsafePayload();
        long position = stream.Position;
        try
        {
            stream.Position = 0;
            byte[] detached = new byte[checked((int)stream.Length)];
            stream.ReadExactly(detached);
            return detached;
        }
        finally { stream.Position = position; }
    }
    private static bool Equal(object first, object second)
    {
        if (first is string text && second is string otherText) return text == otherText;
        if (first is string[] paths && second is string[] otherPaths) return paths.SequenceEqual(otherPaths, StringComparer.Ordinal);
        if (first is BitmapSource bitmap && second is BitmapSource otherBitmap)
        {
            if (bitmap.PixelWidth != otherBitmap.PixelWidth || bitmap.PixelHeight != otherBitmap.PixelHeight
                || bitmap.DpiX != otherBitmap.DpiX || bitmap.DpiY != otherBitmap.DpiY) return false;
            int stride = checked(bitmap.PixelWidth * 4);
            byte[] pixels = new byte[checked(stride * bitmap.PixelHeight)], otherPixels = new byte[pixels.Length];
            bitmap.CopyPixels(pixels, stride, 0); otherBitmap.CopyPixels(otherPixels, stride, 0);
            return pixels.SequenceEqual(otherPixels);
        }
        if (first is Stream && second is Stream) return ReadBytes(first).SequenceEqual(ReadBytes(second));
        return first.Equals(second);
    }
    private static InvalidOperationException UnsafePayload()
        => new("An original clipboard value could not be safely detached. No synthetic clipboard changes were made.");
}
