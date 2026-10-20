namespace ArcaneSniper
{
    // Dwell must be continuous; switching targets or opening UI discards progress.
    internal sealed class AimGate
    {
        private int target;
        private float acquired;
        private float nextShot;
        public void Clear() { target = 0; acquired = 0; }
        public bool Ready(int id, float now, float dwell, float cooldown, bool triggerPressed)
        {
            if (id == 0) { Clear(); return false; }
            if (target != id) { target = id; acquired = now; }
            if (!triggerPressed || now - acquired < dwell || now < nextShot) return false;
            nextShot = now + cooldown;
            return true;
        }
    }
}
