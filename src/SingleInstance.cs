using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace PadMouse
{
    /// <summary>
    /// Makes sure only one PadMouse runs, and lets a second launch talk to the running copy
    /// ("open your settings" / "please exit so I can update you").
    /// </summary>
    public static class SingleInstance
    {
        const string MutexName = @"Local\PadMouse-single-instance";
        const string ShowName = @"Local\PadMouse-show-settings";
        const string ExitName = @"Local\PadMouse-exit";

        static Mutex mutex;
        public static EventWaitHandle ShowSignal { get; private set; }
        public static EventWaitHandle ExitSignal { get; private set; }

        static SecurityIdentifier User { get { return WindowsIdentity.GetCurrent().User; } }

        /// <summary>Become the running instance. waitMs > 0 waits for a previous copy to exit (restart/update).</summary>
        public static bool TryAcquire(int waitMs)
        {
            bool created;
            try
            {
                var sec = new MutexSecurity();
                sec.AddAccessRule(new MutexAccessRule(User, MutexRights.FullControl, AccessControlType.Allow));
                mutex = new Mutex(true, MutexName, out created, sec);
            }
            catch (UnauthorizedAccessException)
            {
                // Another copy is running with administrator rights.
                if (waitMs <= 0) return false;
                if (!WaitUntilGone(waitMs)) return false;
                return TryAcquire(0);
            }
            if (!created && waitMs > 0)
            {
                try { created = mutex.WaitOne(waitMs); }
                catch (AbandonedMutexException) { created = true; }
            }
            if (!created) { mutex.Dispose(); mutex = null; return false; }
            CreateSignals();
            return true;
        }

        static void CreateSignals()
        {
            var sec = new EventWaitHandleSecurity();
            sec.AddAccessRule(new EventWaitHandleAccessRule(User, EventWaitHandleRights.FullControl, AccessControlType.Allow));
            bool c;
            try { ShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName, out c, sec); } catch { }
            try { ExitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitName, out c, sec); } catch { }
        }

        public static bool IsRunning()
        {
            try { using (Mutex.OpenExisting(MutexName, MutexRights.Synchronize)) return true; }
            catch (WaitHandleCannotBeOpenedException) { return false; }
            catch (UnauthorizedAccessException) { return true; }
            catch { return false; }
        }

        static bool WaitUntilGone(int ms)
        {
            for (int t = 0; t < ms; t += 100)
            {
                if (!IsRunning()) return true;
                Thread.Sleep(100);
            }
            return !IsRunning();
        }

        static bool Signal(string name)
        {
            try
            {
                using (var h = EventWaitHandle.OpenExisting(name, EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize))
                    return h.Set();
            }
            catch { return false; }
        }

        public static bool SignalShowSettings()
        {
            try { Win32.AllowSetForegroundWindow(-1); } catch { }
            return Signal(ShowName);
        }

        /// <summary>Asks a running copy to exit and waits for it. True if nothing is running any more.</summary>
        public static bool AskRunningCopyToExit()
        {
            if (!IsRunning()) return true;
            Signal(ExitName);
            return WaitUntilGone(6000);
        }

        public static void Release()
        {
            try { if (mutex != null) { mutex.ReleaseMutex(); mutex.Dispose(); mutex = null; } } catch { }
        }
    }
}
