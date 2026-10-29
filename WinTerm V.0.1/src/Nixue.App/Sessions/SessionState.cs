namespace Nixue.App.Sessions
{
    /// <summary>
    /// Explicit lifecycle states for a terminal session, preferred over a handful of
    /// loosely-coordinated boolean flags.
    /// </summary>
    public enum SessionState
    {
        Created,
        Starting,
        Running,
        Stopping,
        Exited,
        Failed,
        Disposed
    }
}
