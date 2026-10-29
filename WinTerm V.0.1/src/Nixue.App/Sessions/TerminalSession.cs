using System;
using System.Text;
using Nixue.Core.Config;
using Nixue.Core.Diagnostics;
using Nixue.Core.Shells;
using Nixue.Core.Terminal;

namespace Nixue.App.Sessions
{
    /// <summary>
    /// One independent shell session: a ConPTY backend feeding a
    /// <see cref="TerminalEmulator"/>/<see cref="TerminalBuffer"/>. Output arrives on a
    /// background thread, is UTF-8 decoded with a stateful decoder (so multibyte
    /// characters split across reads are preserved), then marshalled to the UI thread
    /// where the emulator and buffer are touched by a single thread only.
    /// </summary>
    public sealed class TerminalSession : IDisposable
    {
        private readonly Action<Action> _postToUi;
        private readonly ITerminalBackend _backend;
        private readonly TerminalBuffer _buffer;
        private readonly TerminalEmulator _emulator;
        private readonly Decoder _decoder;
        private readonly object _pendingGate = new object();
        private readonly StringBuilder _pending = new StringBuilder();
        private bool _drainScheduled;

        private SessionState _state = SessionState.Created;
        private int _cols;
        private int _rows;

        public ShellInfo Shell { get; private set; }
        public string Title { get; set; }
        public int ExitCode { get; private set; }

        /// <summary>Raised on the UI thread after output has been applied to the buffer.</summary>
        public event Action Updated;
        /// <summary>Raised on the UI thread when the program sets the window title.</summary>
        public event Action<string> TitleChanged;
        /// <summary>Raised on the UI thread when the shell process exits.</summary>
        public event Action<int> Exited;
        /// <summary>Raised on the UI thread when the terminal bell fires.</summary>
        public event Action Bell;

        public TerminalSession(ShellInfo shell, int cols, int rows, int scrollbackLimit,
                               Action<Action> postToUi, ITerminalBackend backend)
        {
            if (shell == null) throw new ArgumentNullException("shell");
            if (postToUi == null) throw new ArgumentNullException("postToUi");
            Shell = shell;
            _postToUi = postToUi;
            _cols = Math.Max(1, cols);
            _rows = Math.Max(1, rows);
            _buffer = new TerminalBuffer(_rows, _cols, scrollbackLimit);
            _emulator = new TerminalEmulator(_buffer);
            _decoder = Encoding.UTF8.GetDecoder();
            _backend = backend != null ? backend : new ConPtyBackend();

            _emulator.TitleChanged += OnEmuTitle;
            _emulator.BellRang += OnEmuBell;
            _emulator.ResponseReady += OnEmuResponse;

            _backend.OutputReceived += OnBackendOutput;
            _backend.Exited += OnBackendExited;

            Title = shell.DisplayName;
        }

        public SessionState State { get { return _state; } }
        public TerminalBuffer Buffer { get { return _buffer; } }
        public TerminalEmulator Emulator { get { return _emulator; } }
        public int Cols { get { return _cols; } }
        public int Rows { get { return _rows; } }
        public bool IsRunning { get { return _state == SessionState.Running; } }

        public void Start(string commandLine, string workingDirectory)
        {
            _state = SessionState.Starting;
            try
            {
                _backend.Start(commandLine, workingDirectory, _cols, _rows);
                _state = SessionState.Running;
            }
            catch (Exception ex)
            {
                _state = SessionState.Failed;
                Log.Error("Failed to start shell session", ex);
                throw;
            }
        }

        public void SendInput(string text)
        {
            if (_state != SessionState.Running) return;
            _backend.Write(text);
        }

        public void Resize(int cols, int rows)
        {
            cols = Math.Max(1, cols);
            rows = Math.Max(1, rows);
            if (cols == _cols && rows == _rows) return;
            _cols = cols;
            _rows = rows;
            _buffer.Resize(rows, cols);
            _backend.Resize(cols, rows);
            RaiseUpdated();
        }

        public void SetScrollbackLimit(int limit)
        {
            _buffer.SetScrollbackLimit(limit);
        }

        // ---- Backend callbacks (background thread) -------------------------

        private void OnBackendOutput(byte[] data, int count)
        {
            // Decode on the read thread (single-threaded use of the decoder) then
            // queue the text for the UI thread. Coalesce to avoid flooding the
            // dispatcher under heavy output.
            int charCount = _decoder.GetCharCount(data, 0, count, false);
            if (charCount == 0) return;
            char[] chars = new char[charCount];
            int produced = _decoder.GetChars(data, 0, count, chars, 0, false);
            string text = new string(chars, 0, produced);

            bool schedule = false;
            lock (_pendingGate)
            {
                _pending.Append(text);
                if (!_drainScheduled) { _drainScheduled = true; schedule = true; }
            }
            if (schedule) _postToUi(Drain);
        }

        private void Drain()
        {
            string text;
            lock (_pendingGate)
            {
                text = _pending.ToString();
                _pending.Length = 0;
                _drainScheduled = false;
            }
            if (text.Length > 0)
            {
                _emulator.Feed(text);
                RaiseUpdated();
            }
        }

        private void OnBackendExited(int code)
        {
            ExitCode = code;
            _postToUi(delegate
            {
                // Apply any trailing output first.
                Drain();
                _state = SessionState.Exited;
                Action<int> handler = Exited;
                if (handler != null) handler(code);
                RaiseUpdated();
            });
        }

        // ---- Emulator callbacks (UI thread, during Feed) -------------------

        private void OnEmuResponse(string response)
        {
            _backend.Write(response);
        }

        private void OnEmuTitle(string title)
        {
            Title = title;
            Action<string> handler = TitleChanged;
            if (handler != null) handler(title);
        }

        private void OnEmuBell()
        {
            Action handler = Bell;
            if (handler != null) handler();
        }

        private void RaiseUpdated()
        {
            Action handler = Updated;
            if (handler != null) handler();
        }

        public void Dispose()
        {
            if (_state == SessionState.Disposed) return;
            _state = SessionState.Stopping;
            try
            {
                _backend.OutputReceived -= OnBackendOutput;
                _backend.Exited -= OnBackendExited;
                _emulator.TitleChanged -= OnEmuTitle;
                _emulator.BellRang -= OnEmuBell;
                _emulator.ResponseReady -= OnEmuResponse;
                _backend.Dispose();
            }
            catch (Exception ex)
            {
                Log.Debug("Error disposing session: " + ex.Message);
            }
            _state = SessionState.Disposed;
        }
    }
}
