using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CSharp.RuntimeBinder;

namespace Crest.Features;

internal sealed class OutlookCalendarReader
{
    private const int DefaultCalendarFolder = 9;
    private const int AppointmentClass = 26;
    private const int CancelledMeeting = 5;
    private const int ReceivedAndCancelledMeeting = 7;
    private const int MaxScannedItems = 500;
    private const int MaxLocationLength = 8192;
    private static readonly Guid OutlookApplicationClassId = new("0006F03A-0000-0000-C000-000000000046");
    private int _reading;

    public Task<CalendarParseResult> ReadAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // COM calls cannot always be interrupted. Keep at most one worker while Outlook is unresponsive.
        if (Interlocked.Exchange(ref _reading, 1) != 0) throw new IOException("An Outlook read is still in progress.");
        var completion = new TaskCompletionSource<CalendarParseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(Read(now, cancellationToken)); }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception error) { completion.TrySetException(error); }
            finally { Volatile.Write(ref _reading, 0); }
        }) { IsBackground = true, Name = "Crest Outlook calendar" };
        try
        {
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        catch
        {
            Volatile.Write(ref _reading, 0);
            throw;
        }
        return completion.Task;
    }

    private static CalendarParseResult Read(DateTimeOffset now, CancellationToken cancellationToken)
    {
        dynamic? outlook = null, session = null, folder = null, items = null, upcoming = null;
        var events = new List<CalendarEvent>();
        int invalid = 0;
        var through = now.AddDays(CalendarService.PreviewDays);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid applicationId = OutlookApplicationClassId;
            Marshal.ThrowExceptionForHR(GetActiveObject(ref applicationId, IntPtr.Zero, out object application));
            outlook = application;
            cancellationToken.ThrowIfCancellationRequested();
            session = outlook.Session;
            folder = session.GetDefaultFolder(DefaultCalendarFolder);
            items = folder.Items;
            cancellationToken.ThrowIfCancellationRequested();
            items.Sort("[Start]", false);
            items.IncludeRecurrences = true;

            // Outlook's Jet filter uses current local date and short time formats, with minute precision.
            string lowerBound = FilterDate(now.AddMinutes(-1));
            string upperBound = FilterDate(through.AddMinutes(1));
            cancellationToken.ThrowIfCancellationRequested();
            upcoming = items.Restrict($"[End] >= '{lowerBound}' AND [Start] <= '{upperBound}'");
            for (int index = 0; index < MaxScannedItems && events.Count < CalendarService.MaxEvents; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dynamic? item = index == 0 ? upcoming.GetFirst() : upcoming.GetNext();
                if (item is null) break;
                try
                {
                    if ((int)item.Class != AppointmentClass || (int)item.MeetingStatus is CancelledMeeting or ReceivedAndCancelledMeeting) continue;
                    var start = AsUtc((DateTime)item.StartUTC);
                    var end = AsUtc((DateTime)item.EndUTC);
                    if (end < start) { invalid++; continue; }
                    if (start > through || end <= now) continue;

                    bool allDay = item.AllDayEvent;
                    DateOnly? date = allDay ? DateOnly.FromDateTime((DateTime)item.StartInStartTimeZone) : null;
                    string title = Bound((string?)item.Subject, 300);
                    string location = Bound((string?)item.Location, MaxLocationLength);
                    var meeting = MeetingLinks.Find(location);
                    string id = Bound((string?)item.EntryID, 1024);
                    if (id.Length == 0) id = index.ToString(CultureInfo.InvariantCulture);
                    events.Add(new(id + ":" + start.UtcTicks, title.Length == 0 ? "Untitled event" : title,
                        start, end, allDay, Bound(location, 500), "", meeting?.Url, meeting?.Provider, date));
                }
                catch (Exception error) when (error is COMException or RuntimeBinderException or ArgumentException) { invalid++; }
                finally { Release((object)item); }
            }
            return new(events.OrderBy(item => item.StartUtc).ThenBy(item => item.Title).ToArray(), 0, invalid);
        }
        catch (Exception error) when (error is COMException or RuntimeBinderException or InvalidCastException)
        {
            throw new IOException("Outlook's calendar could not be read.");
        }
        finally
        {
            // Balance each COM acquisition even if two returned interface pointers share a wrapper.
            Release((object?)upcoming);
            Release((object?)items);
            Release((object?)folder);
            Release((object?)session);
            Release((object?)outlook);
        }
    }

    private static string FilterDate(DateTimeOffset value) => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture).Replace("'", "''");
    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    private static string Bound(string? value, int maxLength) => value == null ? "" : value.Length > maxLength ? value[..maxLength] : value;
    private static void Release(object? value)
    {
        if (value == null || !Marshal.IsComObject(value)) return;
        try { Marshal.ReleaseComObject(value); }
        catch (InvalidComObjectException) { }
    }

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int GetActiveObject(ref Guid classId, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object value);
}
