using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ParentalControl.Service;

internal static class WindowsSession
{
    private const uint InvalidSessionId = 0xFFFFFFFF;

    public static uint? GetActiveConsoleSession()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == InvalidSessionId)
        {
            return null;
        }

        if (!WTSQuerySessionInformation(
                IntPtr.Zero,
                sessionId,
                WtsInfoClass.WtsConnectState,
                out var buffer,
                out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            return Marshal.ReadInt32(buffer) == (int)WtsConnectState.WtsActive ? sessionId : null;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    public static bool Logoff(uint sessionId)
    {
        if (!WTSLogoffSession(IntPtr.Zero, sessionId, false))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"WTSLogoffSession failed for session {sessionId}.");
        }

        return true;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr server,
        uint sessionId,
        WtsInfoClass infoClass,
        out IntPtr buffer,
        out uint bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSLogoffSession(IntPtr server, uint sessionId, [MarshalAs(UnmanagedType.Bool)] bool wait);

    private enum WtsInfoClass
    {
        WtsConnectState = 8
    }

    private enum WtsConnectState
    {
        WtsActive = 0
    }
}
