using System;

namespace Crest.Core;

public readonly record struct ControlWrite(long Version, double Value);

/// <summary>Retains the latest user value through debounce, asynchronous writes and delayed provider acknowledgement.</summary>
public sealed class PendingControlEdit
{
    private long _version, _activeWrite, _acknowledgeUntil;
    private double? _desired;
    public bool HasPendingEdit => _desired.HasValue;
    public bool NeedsWrite { get; private set; }
    public bool IsWriting { get; private set; }

    public void Record(double value)
    {
        _version++;
        _desired = value;
        _acknowledgeUntil = 0;
        NeedsWrite = true;
    }

    public bool TryBeginWrite(out ControlWrite write)
    {
        write = default;
        if (!_desired.HasValue || !NeedsWrite || IsWriting) return false;
        IsWriting = true;
        NeedsWrite = false;
        _activeWrite = _version;
        write = new ControlWrite(_version, _desired.Value);
        return true;
    }

    public bool CompleteWrite(ControlWrite write, long nowMilliseconds, int acknowledgementMs = 0)
    {
        if (!IsWriting || write.Version != _activeWrite) return false;
        IsWriting = false;
        _activeWrite = 0;
        if (write.Version != _version) return NeedsWrite;
        if (acknowledgementMs > 0) _acknowledgeUntil = nowMilliseconds + acknowledgementMs;
        else _desired = null;
        return false;
    }

    public double DisplayValue(double reportedValue, long nowMilliseconds, double tolerance = 0.01)
    {
        if (_desired.HasValue && !NeedsWrite && !IsWriting &&
            (_acknowledgeUntil == 0 || nowMilliseconds >= _acknowledgeUntil || Math.Abs(reportedValue - _desired.Value) <= tolerance))
        {
            _desired = null;
            _acknowledgeUntil = 0;
        }
        return _desired ?? reportedValue;
    }

    public void Cancel()
    {
        _version++;
        _desired = null;
        _activeWrite = _acknowledgeUntil = 0;
        NeedsWrite = IsWriting = false;
    }
}
