using System;

namespace Crest.Core;

public enum IslandState
{
    Ineligible, Armed, HoverPending, Revealing, Open, Closing, Paused
}

/// <summary>Deterministic transitions. OS adapters supply eligibility, pointer membership and monotonic time.</summary>
public sealed class InteractionStateMachine
{
    private bool _eligible, _paused, _insideTrigger, _freshEntryReady, _explicitDismiss;
    private long _dwellStarted, _exitStarted = -1;
    public IslandState State { get; private set; } = IslandState.Ineligible;
    public long TransitionVersion
    {
        get; private set;
    }
    public int DwellMs { get; set; } = 120;
    public int GraceMs { get; set; } = 250;
    public event EventHandler? RevealRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? SuppressRequested;
    public event EventHandler? StateChanged;

    public void SetEligibility(bool eligible, bool pointerInTrigger)
    {
        bool previous = _eligible;
        _eligible = eligible;
        if (_paused)
            return;
        if (!eligible)
        {
            if (previous || State != IslandState.Ineligible)
                Reset(IslandState.Ineligible);
            return;
        }
        if (!previous || State == IslandState.Ineligible || State == IslandState.Paused)
        {
            _insideTrigger = pointerInTrigger;
            _freshEntryReady = !pointerInTrigger;
            _dwellStarted = 0;
            _exitStarted = -1;
            SetState(IslandState.Armed);
        }
    }

    public void SetPaused(bool paused, bool pointerInTrigger)
    {
        if (_paused == paused)
            return;
        _paused = paused;
        Reset(paused ? IslandState.Paused : IslandState.Ineligible);
        if (!paused)
        {
            bool eligible = _eligible;
            _eligible = false;
            SetEligibility(eligible, pointerInTrigger);
        }
    }

    public void Tick(long now, bool inTrigger, bool interacting)
    {
        if (!_eligible || _paused)
            return;
        if (!inTrigger)
            _freshEntryReady = true;
        bool freshEntry = inTrigger && !_insideTrigger && _freshEntryReady;
        _insideTrigger = inTrigger;
        if (State == IslandState.Armed && freshEntry)
        {
            _freshEntryReady = false;
            _dwellStarted = now;
            SetState(IslandState.HoverPending);
        }
        if (State == IslandState.HoverPending)
        {
            if (!inTrigger)
            {
                _dwellStarted = 0;
                SetState(IslandState.Armed);
            }
            else if (now - _dwellStarted >= Math.Clamp(DwellMs, 80, 500))
                RequestReveal();
            return;
        }
        if (State != IslandState.Open && State != IslandState.Revealing && State != IslandState.Closing)
            return;
        if (inTrigger || interacting)
        {
            _exitStarted = -1;
            if (State == IslandState.Closing && !_explicitDismiss)
                RequestReveal();
        }
        else
        {
            if (_exitStarted < 0)
                _exitStarted = now;
            if (State != IslandState.Closing && now - _exitStarted >= Math.Clamp(GraceMs, 100, 750))
            {
                SetState(IslandState.Closing);
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dismiss()
    {
        if (!_eligible || _paused)
            return;
        _dwellStarted = 0;
        _exitStarted = -1;
        _freshEntryReady = false;
        _explicitDismiss = true;
        if (State == IslandState.Open || State == IslandState.Revealing)
        {
            SetState(IslandState.Closing);
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (State == IslandState.HoverPending)
            SetState(IslandState.Armed);
    }

    public void RevealAccessibility()
    {
        if (_eligible && !_paused)
            RequestReveal();
    }
    public void CompleteReveal(long version)
    {
        if (_eligible && !_paused && State == IslandState.Revealing && version == TransitionVersion)
            SetState(IslandState.Open);
    }
    public void CompleteClose(long version)
    {
        if (!_eligible || _paused || State != IslandState.Closing || version != TransitionVersion)
            return;
        _dwellStarted = 0;
        _exitStarted = -1;
        _explicitDismiss = false;
        SetState(IslandState.Armed);
    }
    private void RequestReveal()
    {
        // A repeated keyboard shortcut should focus existing controls without restarting motion.
        if (State == IslandState.Open || State == IslandState.Revealing)
            return;
        _dwellStarted = 0;
        _exitStarted = -1;
        _explicitDismiss = false;
        SetState(IslandState.Revealing);
        RevealRequested?.Invoke(this, EventArgs.Empty);
    }
    private void Reset(IslandState state)
    {
        _dwellStarted = 0;
        _exitStarted = -1;
        _insideTrigger = _freshEntryReady = _explicitDismiss = false;
        TransitionVersion++;
        SetState(state);
        SuppressRequested?.Invoke(this, EventArgs.Empty);
    }
    private void SetState(IslandState state)
    {
        if (State == state)
            return;
        State = state;
        TransitionVersion++;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
