namespace Palace.Helpers;

/// <summary>
/// Pure reentrancy gate for presenter Tick / render. Nested Tick (e.g. Diligent
/// MessageBox pumping the DispatcherQueue timer) must return without native work.
/// </summary>
public sealed class SmoothTickReentrancyGate
{
    private int _depth;

    public bool IsInside => _depth > 0;

    /// <summary>Enter the gate. Returns false if already on the stack.</summary>
    public bool TryEnter()
    {
        if (_depth > 0)
        {
            return false;
        }

        _depth++;
        return true;
    }

    public void Exit()
    {
        if (_depth > 0)
        {
            _depth--;
        }
    }
}
