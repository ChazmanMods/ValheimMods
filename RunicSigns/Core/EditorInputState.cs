namespace RunicSigns.Core;

// Full gameplay blocking belongs only to a successfully opened editor. After closing,
// consume only actions that were held at close, with a finite stale-input deadline.
internal sealed class EditorInputState
{
    internal static readonly string[] ClosingActions =
        { "Attack", "JoyAttack", "SecondaryAttack", "JoySecondaryAttack", "Use", "JoyUse", "JoyButtonB" };
    internal bool IsOpen { get; private set; }
    private uint _heldAtClose;
    private float _releaseDeadline;
    internal void Open() { IsOpen = true; _heldAtClose = 0; }
    internal void Close(float now, uint heldAtClose)
    {
        IsOpen = false;
        _heldAtClose = heldAtClose;
        _releaseDeadline = now + 1f;
    }
    internal bool SuppressClosingAction(string name, bool held, float now)
    {
        if (IsOpen || now >= _releaseDeadline) { _heldAtClose = 0; return false; }
        int index = System.Array.IndexOf(ClosingActions, name);
        if (index < 0) return false;
        uint bit = 1u << index;
        if (!held) _heldAtClose &= ~bit;
        return (_heldAtClose & bit) != 0;
    }
    internal void Reset() { IsOpen = false; _heldAtClose = 0; _releaseDeadline = 0; }
}
