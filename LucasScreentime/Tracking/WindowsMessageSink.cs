using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LucasScreentime.Tracking;

public sealed class WindowsMessageSink : IDisposable
{
    public event Action<int>? MonitorStateChanged; // 0=off, 1=on, 2=dimmed
    public event Action? SystemSleeping;
    public event Action? SystemResumed;
    public event Action? SessionLocked;
    public event Action? SessionUnlocked;

    private static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private const int WM_POWERBROADCAST = 0x0218;
    private const int PBT_POWERSETTINGCHANGE = 0x8013;
    private const int PBT_APMSUSPEND = 0x0004;
    private const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    private const int WM_WTSSESSION_CHANGE = 0x02B1;
    private const int WTS_SESSION_LOCK = 0x7;
    private const int WTS_SESSION_UNLOCK = 0x8;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
    private const int NOTIFY_FOR_THIS_SESSION = 0x0;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr RegisterPowerSettingNotification(
        IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterPowerSettingNotification(IntPtr Handle);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, int dwFlags);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);

    private HwndSource? _hwndSource;
    private IntPtr _powerHandle;
    private bool _disposed;

    public void Initialize()
    {
        var parameters = new HwndSourceParameters("LucasScreentime_Sink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ParentWindow = new IntPtr(-3),             // HWND_MESSAGE
        };

        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        var guid = GUID_CONSOLE_DISPLAY_STATE;
        _powerHandle = RegisterPowerSettingNotification(
            _hwndSource.Handle, ref guid, DEVICE_NOTIFY_WINDOW_HANDLE);

        WTSRegisterSessionNotification(_hwndSource.Handle, NOTIFY_FOR_THIS_SESSION);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_POWERBROADCAST)
        {
            int wp = wParam.ToInt32();
            if (wp == PBT_APMSUSPEND)
                SystemSleeping?.Invoke();
            else if (wp == PBT_APMRESUMEAUTOMATIC)
                SystemResumed?.Invoke();
            else if (wp == PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero)
            {
                var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
                if (setting.PowerSetting == GUID_CONSOLE_DISPLAY_STATE)
                    MonitorStateChanged?.Invoke((int)setting.Data);
            }
        }
        else if (msg == WM_WTSSESSION_CHANGE)
        {
            int wp = wParam.ToInt32();
            if (wp == WTS_SESSION_LOCK)
                SessionLocked?.Invoke();
            else if (wp == WTS_SESSION_UNLOCK)
                SessionUnlocked?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_powerHandle != IntPtr.Zero)
            UnregisterPowerSettingNotification(_powerHandle);
        if (_hwndSource != null)
        {
            WTSUnRegisterSessionNotification(_hwndSource.Handle);
            _hwndSource.Dispose();
        }
    }
}
