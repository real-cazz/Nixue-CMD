using System;

namespace Nixue.App.Sessions
{
    /// <summary>
    /// Abstraction over a pseudoconsole/process host. The ConPTY implementation is the
    /// only backend today; the interface exists so an alternative (or a test fake)
    /// could be substituted without touching the session or UI layers.
    /// </summary>
    public interface ITerminalBackend : IDisposable
    {
        /// <summary>Raised on a background thread when bytes arrive from the shell.</summary>
        event Action<byte[], int> OutputReceived;

        /// <summary>Raised when the hosted process exits. Argument is the exit code.</summary>
        event Action<int> Exited;

        /// <summary>Starts the shell. Throws on failure with an actionable message.</summary>
        void Start(string commandLine, string workingDirectory, int cols, int rows);

        /// <summary>Writes UTF-8 encoded input to the shell.</summary>
        void Write(string text);

        /// <summary>Resizes the pseudoconsole window.</summary>
        void Resize(int cols, int rows);

        bool IsRunning { get; }
        int ExitCode { get; }
    }
}
