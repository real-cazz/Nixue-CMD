using Nixue.Core.Config;

namespace Nixue.Core.Shells
{
    /// <summary>A discovered (or not discovered) shell the app can launch.</summary>
    public sealed class ShellInfo
    {
        public ShellKind Kind;
        public string DisplayName;
        public string ExecutablePath;   // null/empty when not available
        public bool Available;
        public string Version;          // best-effort, may be empty
        public string UnavailableReason;

        public ShellInfo(ShellKind kind, string displayName)
        {
            Kind = kind;
            DisplayName = displayName;
            ExecutablePath = "";
            Version = "";
            UnavailableReason = "";
        }
    }
}
