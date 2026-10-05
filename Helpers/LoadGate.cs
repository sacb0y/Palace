namespace Palace.Helpers;

/// <summary>
/// Suppresses property-change side effects while a ViewModel hydrates.
/// Always clear via <see cref="Scope.Dispose"/> so swallowed failures cannot stick the gate.
/// </summary>
public sealed class LoadGate
{
    public bool IsLoading { get; private set; }

    public Scope Begin()
    {
        IsLoading = true;
        return new Scope(this);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly LoadGate _gate;

        internal Scope(LoadGate gate) => _gate = gate;

        public void Dispose() => _gate.IsLoading = false;
    }
}
