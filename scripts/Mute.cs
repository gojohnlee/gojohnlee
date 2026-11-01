using System;
using System.Runtime.InteropServices;
// Windows 볼륨 믹서와 같은 앱별 음소거(Core Audio). 게임 코드와 무관하게 그 프로세스 소리만 끈다.
public static class AppMute
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator { int NotImpl1(); [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice dev); }
    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice { [PreserveSig] int Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o); }
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2 { int N1(); int N2(); [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e); }
    [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator { [PreserveSig] int GetCount(out int n); [PreserveSig] int GetSession(int i, out IAudioSessionControl2 s); }
    [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2 { int a(); int b(); int c(); int d(); int e(); int f(); int g(); int h(); int i(); int j(); int k(); [PreserveSig] int GetProcessId(out uint pid); }
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume { int a(); int b(); [PreserveSig] int SetMute(bool m, ref Guid ctx); }

    // 그 pid의 세션을 찾아 음소거(on)·해제하면 true. Windows가 exe별로 기억하므로 시험 끝에 꼭 해제
    public static bool Mute(int pid, bool on)
    {
        bool hit = false;
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice dev; if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0) return false;
            var iid = typeof(IAudioSessionManager2).GUID; object o;
            if (dev.Activate(ref iid, 1, IntPtr.Zero, out o) != 0) return false;
            IAudioSessionEnumerator se; ((IAudioSessionManager2)o).GetSessionEnumerator(out se);
            int n; se.GetCount(out n);
            for (int i = 0; i < n; i++)
            {
                IAudioSessionControl2 s; se.GetSession(i, out s); uint p; s.GetProcessId(out p);
                if (p == pid) { var g = Guid.Empty; ((ISimpleAudioVolume)s).SetMute(on, ref g); hit = true; }
            }
        }
        catch { }
        return hit;
    }
}
