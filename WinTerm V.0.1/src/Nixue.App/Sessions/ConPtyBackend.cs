using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Nixue.App.Interop;
using Nixue.Core.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace Nixue.App.Sessions
{
    /// <summary>
    /// A terminal backend built on the Windows pseudoconsole (ConPTY). It owns the
    /// pipes, the HPCON and the child process, reads output on a dedicated thread and
    /// cleans every resource up exactly once. Handles are managed as raw IntPtr values,
    /// following the Microsoft "EchoCon" ordering: the pseudoconsole-side pipe ends are
    /// closed only after the child process is created.
    /// </summary>
    public sealed class ConPtyBackend : ITerminalBackend
    {
        private IntPtr _hPC = IntPtr.Zero;
        private IntPtr _attrList = IntPtr.Zero;
        private NativeMethods.PROCESS_INFORMATION _procInfo;

        private IntPtr _inPipeWrite = IntPtr.Zero;  // we write (child stdin source)
        private IntPtr _inPipeRead = IntPtr.Zero;   // pseudoconsole reads from here
        private IntPtr _outPipeRead = IntPtr.Zero;  // we read (child stdout)
        private IntPtr _outPipeWrite = IntPtr.Zero; // pseudoconsole writes here

        private FileStream _writeStream;
        private FileStream _readStream;

        private Thread _readThread;
        private Thread _waitThread;
        private volatile bool _running;
        private volatile bool _disposed;
        private int _exitCode;
        private readonly object _writeGate = new object();

        public event Action<byte[], int> OutputReceived;
        public event Action<int> Exited;

        public bool IsRunning { get { return _running; } }
        public int ExitCode { get { return _exitCode; } }
        public int ProcessId { get { return _procInfo.dwProcessId; } }

        public void Start(string commandLine, string workingDirectory, int cols, int rows)
        {
            if (string.IsNullOrEmpty(commandLine))
                throw new ArgumentException("commandLine is required");
            if (cols < 1) cols = 1;
            if (rows < 1) rows = 1;

            try
            {
                NativeMethods.SECURITY_ATTRIBUTES sa = new NativeMethods.SECURITY_ATTRIBUTES();
                sa.nLength = Marshal.SizeOf(typeof(NativeMethods.SECURITY_ATTRIBUTES));
                sa.bInheritHandle = 0;

                if (!NativeMethods.CreatePipe(out _inPipeRead, out _inPipeWrite, ref sa, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (input) failed");
                if (!NativeMethods.CreatePipe(out _outPipeRead, out _outPipeWrite, ref sa, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (output) failed");

                NativeMethods.COORD size = new NativeMethods.COORD((short)cols, (short)rows);
                int hr = NativeMethods.CreatePseudoConsole(size, _inPipeRead, _outPipeWrite, 0, out _hPC);
                if (hr != 0)
                    throw new Win32Exception(hr, "CreatePseudoConsole failed (HRESULT 0x" + hr.ToString("X8") + ")");

                StartProcess(commandLine, workingDirectory);

                // The child now has the pseudoconsole; close the console-side pipe ends
                // in our process so only the pseudoconsole holds them (EchoCon ordering).
                CloseHandleSafe(ref _inPipeRead);
                CloseHandleSafe(ref _outPipeWrite);

                _writeStream = new FileStream(new SafeFileHandle(_inPipeWrite, true), FileAccess.Write);
                _readStream = new FileStream(new SafeFileHandle(_outPipeRead, true), FileAccess.Read);
                // Ownership transferred to the FileStreams.
                _inPipeWrite = IntPtr.Zero;
                _outPipeRead = IntPtr.Zero;

                _running = true;
                _readThread = new Thread(ReadLoop);
                _readThread.IsBackground = true;
                _readThread.Name = "ConPTY-Read";
                _readThread.Start();

                _waitThread = new Thread(WaitLoop);
                _waitThread.IsBackground = true;
                _waitThread.Name = "ConPTY-Wait";
                _waitThread.Start();

                Log.Info("ConPTY session started (" + cols + "x" + rows + ").");
            }
            catch
            {
                CleanupNative();
                throw;
            }
        }

        private void StartProcess(string commandLine, string workingDirectory)
        {
            NativeMethods.STARTUPINFOEX si = new NativeMethods.STARTUPINFOEX();
            si.StartupInfo.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFOEX));

            IntPtr size = IntPtr.Zero;
            NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            _attrList = Marshal.AllocHGlobal(size);
            if (!NativeMethods.InitializeProcThreadAttributeList(_attrList, 1, 0, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed");

            if (!NativeMethods.UpdateProcThreadAttribute(
                    _attrList, 0,
                    (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    _hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed");

            si.lpAttributeList = _attrList;

            string wd = workingDirectory;
            if (string.IsNullOrEmpty(wd) || !Directory.Exists(wd)) wd = null;

            StringBuilder cmd = new StringBuilder(commandLine);
            bool ok = NativeMethods.CreateProcess(
                null,
                cmd,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                NativeMethods.EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                wd,
                ref si,
                out _procInfo);

            if (!ok)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess failed for shell");

            Log.Debug("CreateProcess ok: pid=" + _procInfo.dwProcessId);
        }

        private void ReadLoop()
        {
            byte[] buffer = new byte[16 * 1024];
            try
            {
                while (!_disposed)
                {
                    int n = _readStream.Read(buffer, 0, buffer.Length);
                    if (n <= 0) break; // pipe closed -> shell exited
                    Action<byte[], int> handler = OutputReceived;
                    if (handler != null)
                    {
                        byte[] slice = new byte[n];
                        Buffer.BlockCopy(buffer, 0, slice, 0, n);
                        handler(slice, n);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_disposed) Log.Debug("ConPTY read loop ended: " + ex.Message);
            }
        }

        private void WaitLoop()
        {
            try
            {
                if (_procInfo.hProcess != IntPtr.Zero)
                {
                    NativeMethods.WaitForSingleObject(_procInfo.hProcess, NativeMethods.INFINITE);
                    uint code;
                    if (NativeMethods.GetExitCodeProcess(_procInfo.hProcess, out code))
                        _exitCode = unchecked((int)code);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ConPTY wait loop error: " + ex.Message);
            }
            finally
            {
                _running = false;
                Action<int> handler = Exited;
                if (handler != null && !_disposed) handler(_exitCode);
            }
        }

        public void Write(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (_disposed || _writeStream == null) return;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                lock (_writeGate)
                {
                    _writeStream.Write(bytes, 0, bytes.Length);
                    _writeStream.Flush();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ConPTY write failed: " + ex.Message);
            }
        }

        public void Resize(int cols, int rows)
        {
            if (_disposed || _hPC == IntPtr.Zero) return;
            if (cols < 1) cols = 1;
            if (rows < 1) rows = 1;
            try
            {
                NativeMethods.COORD size = new NativeMethods.COORD((short)cols, (short)rows);
                NativeMethods.ResizePseudoConsole(_hPC, size);
            }
            catch (Exception ex)
            {
                Log.Debug("ResizePseudoConsole failed: " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

            // Closing the pseudoconsole signals the shell to exit and unblocks reads.
            try { if (_hPC != IntPtr.Zero) NativeMethods.ClosePseudoConsole(_hPC); }
            catch { }
            _hPC = IntPtr.Zero;

            try { if (_writeStream != null) _writeStream.Dispose(); } catch { }
            try { if (_readStream != null) _readStream.Dispose(); } catch { }

            try
            {
                if (_procInfo.hProcess != IntPtr.Zero)
                {
                    uint wait = NativeMethods.WaitForSingleObject(_procInfo.hProcess, 2000);
                    if (wait != NativeMethods.WAIT_OBJECT_0)
                        NativeMethods.TerminateProcess(_procInfo.hProcess, 0);
                }
            }
            catch { }

            CleanupNative();
            Log.Info("ConPTY session disposed.");
        }

        private void CleanupNative()
        {
            try { if (_attrList != IntPtr.Zero) { NativeMethods.DeleteProcThreadAttributeList(_attrList); Marshal.FreeHGlobal(_attrList); } }
            catch { }
            _attrList = IntPtr.Zero;

            CloseHandleSafe(ref _inPipeRead);
            CloseHandleSafe(ref _outPipeWrite);
            CloseHandleSafe(ref _inPipeWrite);
            CloseHandleSafe(ref _outPipeRead);

            try { if (_procInfo.hThread != IntPtr.Zero) NativeMethods.CloseHandle(_procInfo.hThread); } catch { }
            try { if (_procInfo.hProcess != IntPtr.Zero) NativeMethods.CloseHandle(_procInfo.hProcess); } catch { }
            _procInfo.hThread = IntPtr.Zero;
            _procInfo.hProcess = IntPtr.Zero;
        }

        private static void CloseHandleSafe(ref IntPtr h)
        {
            if (h != IntPtr.Zero)
            {
                try { NativeMethods.CloseHandle(h); } catch { }
                h = IntPtr.Zero;
            }
        }
    }
}
