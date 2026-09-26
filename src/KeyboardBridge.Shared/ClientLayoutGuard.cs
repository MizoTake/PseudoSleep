namespace PseudoSleep.KeyboardBridge
{
    internal interface IClientKeyboardLayout
    {
        long GetLayout(long window);
        long GetUsLayout();
        bool HasReleaseKeys(long layout);
        bool Request(long window, long layout);
    }

    internal enum ClientLayoutState { Inactive, Ready, NeedsUsInput, MissingUsInput, Waiting, Rejected, Unavailable }

    internal sealed class ClientLayoutGuard
    {
        private readonly IClientKeyboardLayout backend;
        private long window;
        private long requestedAt;
        private bool attempted;
        internal long CurrentLayout { get; private set; }
        internal ClientLayoutState State { get; private set; }
        internal ClientLayoutGuard(IClientKeyboardLayout backend) { this.backend = backend; }
        internal void Reset() { window = 0; attempted = false; CurrentLayout = 0; State = ClientLayoutState.Inactive; }
        internal bool Update(long target, bool enabled, bool automatic, long now)
        {
            if (!enabled || target == 0) { Reset(); return false; }
            if (target != window) { Reset(); window = target; }
            CurrentLayout = backend.GetLayout(window);
            if (CurrentLayout == 0) { State = ClientLayoutState.Unavailable; return false; }
            if (backend.HasReleaseKeys(CurrentLayout)) { State = ClientLayoutState.Ready; return true; }
            if (!automatic) { State = ClientLayoutState.NeedsUsInput; return false; }
            if (attempted)
            {
                if (State != ClientLayoutState.Waiting || now - requestedAt >= 2000) State = ClientLayoutState.Rejected;
                return false;
            }
            var us = backend.GetUsLayout();
            if (us == 0) { State = ClientLayoutState.MissingUsInput; return false; }
            attempted = true; requestedAt = now;
            State = backend.Request(window, us) ? ClientLayoutState.Waiting : ClientLayoutState.Rejected;
            // Posting a request does not prove the target accepted it. Verify its layout on a later poll.
            return false;
        }
    }
}
