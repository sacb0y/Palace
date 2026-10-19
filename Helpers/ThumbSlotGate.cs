namespace Palace.Helpers;

/// <summary>
/// Fixed number of decode slots. Cancelling the token that entered a slot
/// frees it immediately. A later <see cref="ThumbSlot.Dispose"/> is a no-op
/// for that ticket, so a slow decode cannot keep the cap after it is abandoned.
/// </summary>
public sealed class ThumbSlotGate
{
    private readonly SemaphoreSlim _slots;
    private readonly object _sync = new();
    private readonly HashSet<int> _held = [];
    private int _next;

    public ThumbSlotGate(int cap)
    {
        if (cap < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(cap));
        }

        Cap = cap;
        _slots = new SemaphoreSlim(cap, cap);
    }

    public int Cap { get; }

    /// <summary>Slots nobody is holding. Same meaning as <see cref="SemaphoreSlim.CurrentCount"/>.</summary>
    public int Available => _slots.CurrentCount;

    public bool IsHeld(int ticket)
    {
        lock (_sync)
        {
            return _held.Contains(ticket);
        }
    }

    public async Task<ThumbSlot> EnterAsync(CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        var ticket = Interlocked.Increment(ref _next);
        lock (_sync)
        {
            _held.Add(ticket);
        }

        CancellationTokenRegistration registration;
        try
        {
            registration = cancellationToken.UnsafeRegister(
                static state =>
                {
                    var (gate, id) = ((ThumbSlotGate Gate, int Id))state!;
                    gate.Release(id);
                },
                (this, ticket));
        }
        catch
        {
            Release(ticket);
            throw;
        }

        if (!IsHeld(ticket))
        {
            registration.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(cancellationToken);
        }

        return new ThumbSlot(this, ticket, registration);
    }

    internal bool Release(int ticket)
    {
        lock (_sync)
        {
            if (!_held.Remove(ticket))
            {
                return false;
            }
        }

        _slots.Release();
        return true;
    }
}

/// <summary>One held slot. Dispose when the decode returns; cancel already freed it.</summary>
public sealed class ThumbSlot : IDisposable
{
    private ThumbSlotGate? _gate;
    private readonly int _ticket;
    private CancellationTokenRegistration _registration;

    internal ThumbSlot(ThumbSlotGate gate, int ticket, CancellationTokenRegistration registration)
    {
        _gate = gate;
        _ticket = ticket;
        _registration = registration;
    }

    public int Ticket => _ticket;

    public bool IsCurrent => _gate?.IsHeld(_ticket) == true;

    public void Dispose()
    {
        _registration.Dispose();
        var gate = Interlocked.Exchange(ref _gate, null);
        gate?.Release(_ticket);
    }
}
