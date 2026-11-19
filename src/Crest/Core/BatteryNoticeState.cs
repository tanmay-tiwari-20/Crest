namespace Crest.Core;

public enum BatteryWarningLevel { None, Low, Critical }
public sealed record BatteryNoticeSnapshot(long Id, BatteryWarningLevel Severity, int Percent);

/// <summary>Retains one OS-reported battery warning until its exact snapshot has been displayed.</summary>
public sealed class BatteryNoticeState
{
    private readonly object _gate = new();
    private BatteryWarningLevel _episodeSeverity;
    private BatteryNoticeSnapshot? _pending;
    private long _nextId;

    public BatteryNoticeSnapshot? Pending
    {
        get { lock (_gate) return _pending; }
    }

    public void Observe(bool available, byte windowsFlags, int percent, bool onExternalPower)
    {
        lock (_gate)
        {
            // Unknown readings neither erase a retained warning nor manufacture a fresh episode.
            if (!available || windowsFlags == 255 || (windowsFlags & 128) != 0 || percent < 0 || percent > 100)
                return;
            var severity = onExternalPower || (windowsFlags & 8) != 0 ? BatteryWarningLevel.None
                : (windowsFlags & 4) != 0 ? BatteryWarningLevel.Critical
                : (windowsFlags & 2) != 0 ? BatteryWarningLevel.Low : BatteryWarningLevel.None;
            if (severity == BatteryWarningLevel.None)
            {
                _episodeSeverity = BatteryWarningLevel.None;
                return;
            }
            if (severity <= _episodeSeverity)
                return;
            _episodeSeverity = severity;
            // Coalesce hidden episodes without replacing a more severe unshown warning.
            if (_pending is null || severity > _pending.Severity)
                _pending = new BatteryNoticeSnapshot(++_nextId, severity, percent);
        }
    }

    public bool Acknowledge(long id)
    {
        lock (_gate)
        {
            if (_pending?.Id != id)
                return false;
            _pending = null;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _pending = null;
            _episodeSeverity = BatteryWarningLevel.None;
        }
    }
}
