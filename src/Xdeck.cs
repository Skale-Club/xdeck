// Xdeck: audio output, microphone and game controllers from the tray.
//  - Native host (this file): tray icon, hotkeys, controller input and combo, audio endpoints
//    (default device, volume, mute, level meters), controller battery, the banner, and the
//    WebView2 window that shows ui/ (panel + controller tester).
//  - Ctrl+Alt+F11 / Ctrl+Alt+F10, or hold D-pad right + L1 and flick the right stick, switch the
//    playback device forward / back; Data\devices.txt optionally sets the order.
//  - L1 + D-pad right + Triangle opens / hides the panel, which the controller then drives.
//  - Every shortcut above can be changed in shortcuts.ini (read again on save, no new build).
// Build with ..\build.ps1 (C# 5 / .NET Framework 4.8: no string interpolation, no ?. operator).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;

// Portable layout: everything Xdeck writes lives in Data\ next to the exe.
// Data: portable mode when a Data folder sits next to the exe (the zip ships one); otherwise —
// e.g. installed for all users in Program Files — each Windows user gets %LOCALAPPDATA%\Xdeck.
static class AppPaths
{
    public static string Exe { get { return Path.GetDirectoryName(Application.ExecutablePath); } }
    public static bool Portable { get { return Directory.Exists(Path.Combine(Exe, "Data")); } }
    public static string Data
    {
        get
        {
            string d = Portable ? Path.Combine(Exe, "Data")
                                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Xdeck");
            Directory.CreateDirectory(d);
            return d;
        }
    }
    public static string DeviceOrder { get { return Path.Combine(Data, "devices.txt"); } }
    public static string ShortcutFile { get { return Path.Combine(Data, "shortcuts.ini"); } }
}

// shortcuts.ini in the data folder: written with the defaults on first run, read at start and again
// whenever it is saved (XdeckApp watches it), so a shortcut changes with no new build or restart.
static class ShortcutsFile
{
    public static string NextKey = "Ctrl+Alt+F11", PrevKey = "Ctrl+Alt+F10";

    const string Template =
@"# Xdeck shortcuts. Save this file and Xdeck uses the new shortcuts right away: no restart, no new build.
# Atalhos do Xdeck. Salve este arquivo e o Xdeck já passa a usar os novos atalhos.
#
# Controller: button names joined by + (hold together) or , (one after another, at most 1.5 s apart),
#   e.g.  L1 + Right + Triangle   or   L1, Right, L1, Triangle
#   Buttons: L1 R1 L3 R3 . Up Down Left Right . Cross Circle Square Triangle . Options Share Home Touchpad
#   Xbox names work too (LB RB A B X Y Menu View Guide); buttons go by position, so Triangle = Y.
#   Keep in each controller shortcut a button the panel does not use (L1, R1, L3, R3, Share, Home),
#   so pressing the shortcut does not also move the focus in the panel.
# Keyboard: Ctrl / Alt / Shift / Win + one key (F1..F24, A..Z, 0..9, Home, PageUp, ...).
# Leave a value empty, or write none, to turn that shortcut off.

# open / hide the panel
panel = L1 + Right + Triangle

# hold these, then flick the right stick: right / up = next audio output, left / down = previous
audio = L1 + Right

# next / previous audio output from the keyboard
audio.next = Ctrl+Alt+F11
audio.previous = Ctrl+Alt+F10
" + MouseSection;

    // Added to a file written by an older Xdeck that has no mouse lines yet.
    const string MouseSection =
@"
# turn the controller into a mouse, and back: left stick = pointer (hold LT / L2 for fine moves),
# right stick = scroll, A / Cross = left click (hold to drag), B / Circle = right click,
# X / Square = middle click, D-pad = arrow keys, Menu / Options = Enter, View / Share = Esc
mouse = L1 + Right + Square

# pointer speed with the stick all the way, in pixels per second (200 to 6000)
mouse.speed = 1400
";

    public static void EnsureExists()
    {
        try
        {
            string path = AppPaths.ShortcutFile;
            if (!File.Exists(path)) { File.WriteAllText(path, Template, new UTF8Encoding(false)); return; }
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (!Regex.IsMatch(text, @"^\s*mouse\s*=", RegexOptions.Multiline))
                File.AppendAllText(path, (text.EndsWith("\n") ? "" : "\r\n") + MouseSection, new UTF8Encoding(false));
        }
        catch { }
    }

    // Applies the file and returns its problems, one per bad line; a bad line keeps what was in use.
    // Throws IOException while the editor still holds the file (the caller retries).
    public static List<string> Load()
    {
        List<string> errors = new List<string>();
        if (!File.Exists(AppPaths.ShortcutFile)) return errors;
        string[] lines = File.ReadAllLines(AppPaths.ShortcutFile, Encoding.UTF8);
        int[] panel = Shortcuts.Panel;
        string panelText = Shortcuts.PanelText;
        int audio = Shortcuts.Audio;
        int[] mouse = Shortcuts.Mouse;
        string mouseText = Shortcuts.MouseText;
        int mouseSpeed = Shortcuts.MouseSpeed;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            string where = "line " + (i + 1) + ": ";
            int eq = line.IndexOf('=');
            if (eq < 0) { errors.Add(where + "no \"=\""); continue; }
            string key = line.Substring(0, eq).Trim().ToLowerInvariant(), value = line.Substring(eq + 1).Trim();
            string err;
            uint mods, vk;
            if (key == "mouse.speed")
            {
                int v;
                if (!int.TryParse(value, out v) || v < 200 || v > 6000) { errors.Add(where + "mouse.speed takes a number from 200 to 6000"); continue; }
                mouseSpeed = v;
            }
            else if (key == "panel" || key == "audio" || key == "mouse")
            {
                int[] steps = Shortcuts.ParsePad(value, out err);
                if (steps == null) { errors.Add(where + err); continue; }
                if (key == "panel") { panel = steps; panelText = value; }
                else if (key == "mouse") { mouse = steps; mouseText = value; }
                else if (steps.Length > 1) errors.Add(where + "audio takes buttons held together (+), not a sequence (,)");
                else audio = steps.Length == 0 ? 0 : steps[0];
            }
            else if (key == "audio.next" || key == "audio.previous")
            {
                if (!Shortcuts.ParseKey(value, out mods, out vk, out err)) { errors.Add(where + err); continue; }
                if (key == "audio.next") NextKey = value; else PrevKey = value;
            }
            else errors.Add(where + "unknown setting \"" + key + "\"");
        }
        Shortcuts.SetPad(panel, panelText, audio, mouse, mouseText, mouseSpeed);
        return errors;
    }
}

// Windows 11 hides new tray icons under the ^ arrow. Promote Xdeck's icon to the taskbar once per
// user (Explorer creates the NotifyIconSettings entry a moment after the icon first appears); a
// flag in Data remembers it, so if the user hides the icon later that choice is kept.
static class TrayPromote
{
    public static void Once()
    {
        string flag = Path.Combine(AppPaths.Data, "tray-promoted");
        if (File.Exists(flag)) return;
        try
        {
            using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings", true))
            {
                if (root == null) return;
                string exe = Path.GetFileName(Application.ExecutablePath).ToLowerInvariant();
                foreach (string sub in root.GetSubKeyNames())
                    using (RegistryKey k = root.OpenSubKey(sub, true))
                    {
                        string path = k == null ? null : k.GetValue("ExecutablePath") as string;
                        if (path == null || !path.ToLowerInvariant().EndsWith("\\" + exe)) continue;
                        k.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                        File.WriteAllText(flag, DateTime.Now.ToString("o"));
                    }
            }
        }
        catch { }
    }
}

// ---------------------------------------------------------------- audio endpoints
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints();
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    int OpenPropertyStore();
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
}

[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation
{
    [PreserveSig] int GetPeakValue(out float peak);
}

[Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    int RegisterControlChangeNotify(IntPtr notify);
    int UnregisterControlChangeNotify(IntPtr notify);
    int GetChannelCount(out uint count);
    int SetMasterVolumeLevel(float db, ref Guid ctx);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
    int GetMasterVolumeLevel(out float db);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
    int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
    int GetChannelVolumeLevel(uint ch, out float db);
    int GetChannelVolumeLevelScalar(uint ch, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}

[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, int streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long def, out long min);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
}

// Live peak levels for audio endpoints. A microphone only reports a level while some
// stream is capturing from it, so for microphones a silent shared-mode capture stream is
// opened (and closed again when the panel closes).
static class Meters
{
    class Handle { public IAudioMeterInformation Meter; public IAudioClient Client; public float Level; }
    static readonly Dictionary<string, Handle> handles = new Dictionary<string, Handle>();
    static readonly Guid IidMeter = new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    static readonly Guid IidClient = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    // 0..1 on a 60 dB scale, smoothed: fast attack, slow release.
    public static float Level(string id)
    {
        Handle h;
        if (!handles.TryGetValue(id, out h)) h = handles[id] = Open(id);
        if (h == null || h.Meter == null) return 0;
        float peak;
        if (h.Meter.GetPeakValue(out peak) != 0) return 0;
        float db = peak <= 0.000001f ? -60f : (float)(20 * Math.Log10(peak));
        float target = Math.Max(0f, Math.Min(1f, (db + 60f) / 60f));
        h.Level = target > h.Level ? target : h.Level * 0.82f + target * 0.18f;
        return h.Level;
    }

    static Handle Open(string id)
    {
        try
        {
            IMMDeviceEnumerator e = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            IMMDevice d;
            if (e.GetDevice(id, out d) != 0 || d == null) return null;
            Handle h = new Handle();
            object o;
            Guid iid = IidMeter;
            if (d.Activate(ref iid, 23, IntPtr.Zero, out o) == 0) h.Meter = (IAudioMeterInformation)o;
            if (id.StartsWith("{0.0.1.", StringComparison.Ordinal))
            {
                iid = IidClient;
                if (d.Activate(ref iid, 23, IntPtr.Zero, out o) == 0)
                {
                    IAudioClient c = (IAudioClient)o;
                    IntPtr fmt;
                    if (c.GetMixFormat(out fmt) == 0)
                    {
                        if (c.Initialize(0, 0, 2000000, 0, fmt, IntPtr.Zero) == 0 && c.Start() == 0) h.Client = c;
                        Marshal.FreeCoTaskMem(fmt);
                    }
                }
            }
            return h;
        }
        catch { return null; }
    }

    public static void CloseAll()
    {
        foreach (Handle h in handles.Values)
        {
            if (h == null) continue;
            try { if (h.Client != null) { h.Client.Stop(); Marshal.ReleaseComObject(h.Client); } } catch { }
            try { if (h.Meter != null) Marshal.ReleaseComObject(h.Meter); } catch { }
        }
        handles.Clear();
    }
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorCom { }

[Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    int GetMixFormat(); int GetDeviceFormat(); int ResetDeviceFormat(); int SetDeviceFormat();
    int GetProcessingPeriod(); int SetProcessingPeriod(); int GetShareMode(); int SetShareMode();
    int GetPropertyValue(); int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
class CPolicyConfigClient { }

static class Audio
{
    public class Endpoint { public string Id; public string Name; }

    const string Root = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\";
    const string NameProp = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    const string RenderPrefix = "{0.0.0.00000000}.", CapturePrefix = "{0.0.1.00000000}.";

    static bool IsCapture(string id) { return id.StartsWith(CapturePrefix, StringComparison.OrdinalIgnoreCase); }

    static string Key(string id)
    {
        int i = id.IndexOf("}.");
        return Root + (IsCapture(id) ? "Capture\\" : "Render\\") + (i < 0 ? id : id.Substring(i + 2));
    }

    public static bool IsActive(string id)
    {
        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Key(id)))
        {
            if (k == null) return false;
            object v = k.GetValue("DeviceState");
            return v is int && (int)v == 1;
        }
    }

    public static string Name(string id)
    {
        if (id == null) return "—";
        using (RegistryKey k = Registry.LocalMachine.OpenSubKey(Key(id) + @"\Properties"))
        {
            object v = k == null ? null : k.GetValue(NameProp);
            return v as string ?? id;
        }
    }

    // Enabled, connected endpoints. Outputs follow the order of devices.txt; anything
    // active that is not listed there comes after, by name.
    public static List<Endpoint> Active(bool capture)
    {
        List<Endpoint> all = new List<Endpoint>();
        string prefix = capture ? CapturePrefix : RenderPrefix;
        using (RegistryKey root = Registry.LocalMachine.OpenSubKey(Root + (capture ? "Capture" : "Render")))
        {
            if (root == null) return all;
            foreach (string guid in root.GetSubKeyNames())
            {
                string id = prefix + guid;
                if (IsActive(id)) all.Add(new Endpoint { Id = id, Name = Name(id) });
            }
        }
        List<string> order = new List<string>();
        if (!capture)
        {
            string list = AppPaths.DeviceOrder;
            if (File.Exists(list))
                foreach (string raw in File.ReadAllLines(list))
                {
                    string line = raw.Trim();
                    int hash = line.IndexOf('#');
                    if (hash >= 0) line = line.Substring(0, hash).Trim();
                    if (line.Length > 0) order.Add(line.ToLowerInvariant());
                }
        }
        all.Sort(delegate (Endpoint a, Endpoint b)
        {
            int ia = order.IndexOf(a.Id.ToLowerInvariant()), ib = order.IndexOf(b.Id.ToLowerInvariant());
            if (ia < 0) ia = int.MaxValue;
            if (ib < 0) ib = int.MaxValue;
            return ia != ib ? ia.CompareTo(ib) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return all;
    }

    public static string DefaultId() { return DefaultId(false); }

    public static string DefaultId(bool capture)
    {
        IMMDeviceEnumerator e = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDevice d;
        if (e.GetDefaultAudioEndpoint(capture ? 1 : 0, 1, out d) != 0 || d == null) return null;
        string id;
        d.GetId(out id);
        return id;
    }

    // ---- endpoint volume / mute (the same controls as the Windows volume mixer)
    static IAudioEndpointVolume Volume(string id)
    {
        IMMDeviceEnumerator e = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
        IMMDevice d;
        if (e.GetDevice(id, out d) != 0 || d == null) return null;
        Guid iid = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        object o;
        return d.Activate(ref iid, 23, IntPtr.Zero, out o) == 0 ? (IAudioEndpointVolume)o : null;
    }

    public static bool GetVolume(string id, out float level, out bool mute)
    {
        level = 0; mute = false;
        IAudioEndpointVolume v = Volume(id);
        if (v == null) return false;
        try { return v.GetMasterVolumeLevelScalar(out level) == 0 && v.GetMute(out mute) == 0; }
        finally { Marshal.ReleaseComObject(v); }
    }

    public static void SetVolume(string id, float level)
    {
        IAudioEndpointVolume v = Volume(id);
        if (v == null) return;
        Guid ctx = Guid.Empty;
        try
        {
            v.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, level)), ref ctx);
            if (level > 0) v.SetMute(false, ref ctx);       // moving the slider up un-mutes, like Windows
        }
        finally { Marshal.ReleaseComObject(v); }
    }

    public static void SetMute(string id, bool mute)
    {
        IAudioEndpointVolume v = Volume(id);
        if (v == null) return;
        Guid ctx = Guid.Empty;
        try { v.SetMute(mute, ref ctx); } finally { Marshal.ReleaseComObject(v); }
    }

    // Outputs: console + multimedia. Microphones: also communications, so calls use it too.
    public static void SetDefault(string id)
    {
        IPolicyConfig p = (IPolicyConfig)new CPolicyConfigClient();
        p.SetDefaultEndpoint(id, 0);
        p.SetDefaultEndpoint(id, 1);
        if (IsCapture(id)) p.SetDefaultEndpoint(id, 2);
    }

    public static string Glyph(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("airpods") || n.Contains("air pods") || n.Contains("pulse") || n.Contains("head")) return "headphones";
        if (Regex.IsMatch(n, @"\btv\b")) return "tv";
        if (n.Contains("monitor") || n.Contains("display")) return "monitor";
        return "speaker";
    }
}

// ---------------------------------------------------------------- controllers
class Pad
{
    public string Mac;          // 12 hex digits, the stable key
    public string Kind;         // xbox | dualsense | switchpro | joycon | g29 | handbrake
    public string Name;
    public bool Connected;
    public int Battery = -1;    // percent, -1 unknown
    public bool Charging;
    public int Index;           // position among connected controllers of the same kind
    public string Conn = "Bluetooth";
}

static class Pads
{
    [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr reserved; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int req);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceProperty(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type, byte[] buf, int size, out int req, int flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string n, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr t);

    static readonly DEVPROPKEY KeyFriendlyName = new DEVPROPKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
    static readonly DEVPROPKEY KeyConnected = new DEVPROPKEY { fmtid = new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), pid = 15 };
    static readonly DEVPROPKEY KeyBattery = new DEVPROPKEY { fmtid = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), pid = 2 };
    static readonly Regex PadName = new Regex("Xbox Wireless Controller|Xbox Controller|DualSense|DualShock|Wireless Controller|Pro Controller|Joy-Con", RegexOptions.IgnoreCase);

    // Wired game devices recognised by USB VID/PID (HID path), with the name to show.
    static readonly string[][] Wired = {
        new string[] { "vid_046d&pid_c24f", "g29", "G29 Racing Wheel" },
        new string[] { "vid_7864&pid_5757", "handbrake", "Handbrake" },
    };

    // HID readers (DualSense, Pro Controller), by device path: battery, the combos and panel navigation.
    static readonly Dictionary<string, HidReader> readers = new Dictionary<string, HidReader>();

    static byte[] Prop(IntPtr set, ref SP_DEVINFO_DATA d, DEVPROPKEY key)
    {
        uint type; int req;
        byte[] buf = new byte[512];
        return SetupDiGetDeviceProperty(set, ref d, ref key, out type, buf, buf.Length, out req, 0) ? Sub(buf, req) : null;
    }

    static byte[] Sub(byte[] b, int n) { byte[] o = new byte[n]; Array.Copy(b, o, n); return o; }

    static string KindOf(string name, string instanceId)
    {
        string n = name.ToLowerInvariant();
        string i = instanceId.ToUpperInvariant();
        if (n.Contains("xbox") || i.Contains("045E")) return "xbox";
        if (n.Contains("joy-con")) return "joycon";
        if (n.Contains("pro controller") || i.Contains("057E")) return "switchpro";
        return "dualsense";
    }

    public static List<Pad> List()
    {
        List<Pad> pads = new List<Pad>();
        foreach (string enumerator in new string[] { "BTHLE", "BTHENUM" })
        {
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, 0x4 | 0x2); // ALLCLASSES | PRESENT
            SP_DEVINFO_DATA d = new SP_DEVINFO_DATA();
            d.cbSize = Marshal.SizeOf(d);
            for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref d); i++)
            {
                StringBuilder sb = new StringBuilder(512); int req;
                if (!SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out req)) continue;
                string id = sb.ToString();
                Match m = Regex.Match(id, @"^BTH(LE|ENUM)\\DEV_([0-9A-F]{12})", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                byte[] fn = Prop(set, ref d, KeyFriendlyName);
                string name = fn == null ? "" : Encoding.Unicode.GetString(fn).TrimEnd('\0');
                if (!PadName.IsMatch(name)) continue;
                byte[] con = Prop(set, ref d, KeyConnected);
                byte[] bat = Prop(set, ref d, KeyBattery);
                Pad p = new Pad();
                p.Mac = m.Groups[2].Value.ToUpperInvariant();
                p.Name = name;
                p.Kind = KindOf(name, id);
                p.Connected = con != null && con.Length > 0 && con[0] != 0;
                if (bat != null && bat.Length > 0) p.Battery = bat[0];
                pads.Add(p);
            }
            SetupDiDestroyDeviceInfoList(set);
        }

        // Fill battery from HID input reports where Windows has none (DualSense, Pro Controller).
        RefreshReaders();
        foreach (Pad p in pads)
        {
            if (!p.Connected || p.Battery >= 0) continue;
            lock (readers)
                foreach (HidReader r in readers.Values)
                    if (r.Alive && r.Kind == p.Kind && r.Battery >= 0) { p.Battery = r.Battery; p.Charging = r.Charging; break; }
        }

        // Only connected controllers are listed.
        pads = pads.FindAll(delegate (Pad p) { return p.Connected; });

        // Friendlier names, numbered when there are several of the same kind.
        Dictionary<string, int> count = new Dictionary<string, int>();
        foreach (Pad p in pads) { int c; count.TryGetValue(p.Kind, out c); count[p.Kind] = c + 1; }
        Dictionary<string, int> seen = new Dictionary<string, int>();
        pads.Sort(delegate (Pad a, Pad b)
        {
            int c = b.Connected.CompareTo(a.Connected);
            if (c != 0) return c;
            c = string.Compare(a.Kind, b.Kind, StringComparison.Ordinal);
            return c != 0 ? c : string.Compare(a.Mac, b.Mac, StringComparison.Ordinal);
        });
        foreach (Pad p in pads)
        {
            int n; seen.TryGetValue(p.Kind, out n); seen[p.Kind] = ++n;
            p.Index = n - 1;
            if (p.Kind == "joycon") continue;                     // keeps "Joy-Con (L)" / "Joy-Con (R)"
            string label = p.Kind == "xbox" ? "Xbox" : p.Kind == "switchpro" ? "Pro Controller" : "DualSense";
            p.Name = count[p.Kind] > 1 ? label + " " + n : label;
        }

        // Wired devices (wheel, handbrake): present when their HID interface exists.
        List<string> paths = HidPaths();
        foreach (string[] w in Wired)
        {
            if (!paths.Exists(delegate (string x) { return x.ToLowerInvariant().Contains(w[0]); })) continue;
            pads.Add(new Pad { Mac = "usb-" + w[1], Kind = w[1], Name = w[2], Connected = true, Conn = "USB" });
        }
        return pads;
    }

    static List<string> HidPaths()
    {
        List<string> o = new List<string>();
        Guid g; HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
        SP_DEVICE_INTERFACE_DATA d = new SP_DEVICE_INTERFACE_DATA();
        d.cbSize = Marshal.SizeOf(d);
        for (int i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d); i++)
        {
            int req;
            SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
            IntPtr buf = Marshal.AllocHGlobal(req);
            Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
            if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                o.Add(Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)));
            Marshal.FreeHGlobal(buf);
        }
        SetupDiDestroyDeviceInfoList(set);
        return o;
    }

    public static void RefreshReaders()
    {
        foreach (string path in HidPaths())
        {
            string lp = path.ToLowerInvariant();
            string kind = null;
            if (lp.Contains("054c") && (lp.Contains("0ce6") || lp.Contains("0df2"))) kind = "dualsense";
            else if (lp.Contains("057e") && lp.Contains("2009")) kind = "switchpro";
            else if (lp.Contains("057e") && (lp.Contains("2006") || lp.Contains("2007"))) kind = "joycon";
            if (kind == null) continue;
            lock (readers)
            {
                HidReader r;
                if (readers.TryGetValue(lp, out r) && r.Alive) continue;
                r = new HidReader(path, kind);
                readers[lp] = r;
                r.Start();
            }
        }
    }

    class HidReader
    {
        readonly string path, kind;
        readonly PadHooks hooks = new PadHooks();
        readonly PadState state;
        public volatile int Battery = -1;
        public volatile bool Charging;
        public volatile bool Alive = true;
        public string Kind { get { return kind; } }

        public HidReader(string path, string kind)
        {
            this.path = path; this.kind = kind;
            state = new PadState(kind, path.ToLowerInvariant());
        }

        public void Start()
        {
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.Start();
        }

        void Run()
        {
            try
            {
                SafeFileHandle h = CreateFile(path, 0x80000000 | 0x40000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h.IsInvalid) h = CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h.IsInvalid) { Alive = false; return; }
                using (FileStream fs = new FileStream(h, FileAccess.Read, 1, false))
                {
                    byte[] b = new byte[600];
                    bool askedFull = false;
                    Inputs.Add(state);
                    while (true)
                    {
                        int n = fs.Read(b, 0, b.Length);
                        if (n <= 0) break;
                        bool ok = false;
                        if (kind == "dualsense")
                        {
                            // o = offset of the left stick X byte (report layout after the header)
                            int o = -1;
                            if (b[0] == 0x31 && n > 56) o = 2;                            // Bluetooth, full report
                            else if (b[0] == 0x01 && n >= 64) o = 1;                      // USB
                            if (o > 0)
                            {
                                Sony(b[o + 52]);
                                SonyButtons(b[o + 7], b[o + 8], b[o + 9]);
                                state.LX = Axis8(b[o]); state.LY = -Axis8(b[o + 1]);
                                state.RX = Axis8(b[o + 2]); state.RY = -Axis8(b[o + 3]);
                                state.LT = b[o + 4] / 255f; state.RT = b[o + 5] / 255f;
                                ok = true;
                            }
                            else if (b[0] == 0x01 && n >= 10)                             // Bluetooth, basic report
                            {
                                SonyButtons(b[5], b[6], b[7]);
                                state.LX = Axis8(b[1]); state.LY = -Axis8(b[2]);
                                state.RX = Axis8(b[3]); state.RY = -Axis8(b[4]);
                                state.LT = b[8] / 255f; state.RT = b[9] / 255f;
                                ok = true;
                                if (!askedFull)
                                {
                                    askedFull = true;                                     // reading feature 0x05 switches
                                    byte[] f = new byte[41]; f[0] = 0x05;                 // the pad to full reports
                                    HidD_GetFeature(h, f, f.Length);
                                }
                            }
                        }
                        else if ((kind == "switchpro" || kind == "joycon") && (b[0] == 0x30 || b[0] == 0x21) && n > 11)
                        {
                            int level = b[2] >> 5;                                      // 0..4
                            Battery = Math.Min(100, level * 25);
                            Charging = (b[2] & 0x10) != 0;
                            byte r = b[3], s = b[4], l = b[5];
                            state.Set(PadState.West, (r & 0x01) != 0);                  // Y
                            state.Set(PadState.North, (r & 0x02) != 0);                 // X
                            state.Set(PadState.South, (r & 0x04) != 0);                 // B
                            state.Set(PadState.East, (r & 0x08) != 0);                  // A
                            state.Set(PadState.RB, (r & 0x40) != 0);
                            state.RT = (r & 0x80) != 0 ? 1 : 0;
                            state.Set(PadState.Back, (s & 0x01) != 0);
                            state.Set(PadState.Start, (s & 0x02) != 0);
                            state.Set(PadState.R3, (s & 0x04) != 0);
                            state.Set(PadState.L3, (s & 0x08) != 0);
                            state.Set(PadState.Home, (s & 0x10) != 0);
                            state.Set(PadState.Extra, (s & 0x20) != 0);                 // Capture
                            state.Set(PadState.Down, (l & 0x01) != 0);
                            state.Set(PadState.Up, (l & 0x02) != 0);
                            state.Set(PadState.Right, (l & 0x04) != 0);
                            state.Set(PadState.Left, (l & 0x08) != 0);
                            state.Set(PadState.LB, (l & 0x40) != 0);
                            state.LT = (l & 0x80) != 0 ? 1 : 0;
                            int lx = b[6] | ((b[7] & 0x0F) << 8), ly = (b[7] >> 4) | (b[8] << 4);  // 12-bit, centre ~2048
                            int rx = b[9] | ((b[10] & 0x0F) << 8), ry = (b[10] >> 4) | (b[11] << 4);
                            state.LX = Stick12(lx); state.LY = Stick12(ly);
                            state.RX = Stick12(rx); state.RY = Stick12(ry);
                            ok = true;
                        }
                        else if (kind == "switchpro" && b[0] == 0x3F && n >= 12)        // simple HID mode
                        {
                            byte b1 = b[1], b2 = b[2];
                            state.Set(PadState.South, (b1 & 0x01) != 0);
                            state.Set(PadState.East, (b1 & 0x02) != 0);
                            state.Set(PadState.West, (b1 & 0x04) != 0);
                            state.Set(PadState.North, (b1 & 0x08) != 0);
                            state.Set(PadState.LB, (b1 & 0x10) != 0);
                            state.Set(PadState.RB, (b1 & 0x20) != 0);
                            state.LT = (b1 & 0x40) != 0 ? 1 : 0;
                            state.RT = (b1 & 0x80) != 0 ? 1 : 0;
                            state.Set(PadState.Back, (b2 & 0x01) != 0);
                            state.Set(PadState.Start, (b2 & 0x02) != 0);
                            state.Set(PadState.L3, (b2 & 0x04) != 0);
                            state.Set(PadState.R3, (b2 & 0x08) != 0);
                            state.Set(PadState.Home, (b2 & 0x10) != 0);
                            state.Set(PadState.Extra, (b2 & 0x20) != 0);
                            Hat(b[3]);
                            state.LX = ((b[4] | (b[5] << 8)) - 32768) / 32768f;
                            state.LY = -((b[6] | (b[7] << 8)) - 32768) / 32768f;
                            state.RX = ((b[8] | (b[9] << 8)) - 32768) / 32768f;
                            state.RY = -((b[10] | (b[11] << 8)) - 32768) / 32768f;
                            ok = true;
                        }
                        if (ok) { state.Touch(); hooks.Update(state); }
                    }
                }
            }
            catch { }
            Inputs.Remove(state);
            Alive = false;
        }

        static float Axis8(byte v) { return (v - 128) / 127f; }
        static float Stick12(int v) { return Math.Max(-1f, Math.Min(1f, (v - 2048) / 1600f)); }

        // hat: 0 N, 1 NE, 2 E, 3 SE, 4 S, 5 SW, 6 W, 7 NW, 8+ none
        void Hat(int hat)
        {
            state.Set(PadState.Up, hat == 7 || hat == 0 || hat == 1);
            state.Set(PadState.Right, hat >= 1 && hat <= 3);
            state.Set(PadState.Down, hat >= 3 && hat <= 5);
            state.Set(PadState.Left, hat >= 5 && hat <= 7);
        }

        void SonyButtons(byte b0, byte b1, byte b2)
        {
            Hat(b0 & 0x0F);
            state.Set(PadState.West, (b0 & 0x10) != 0);     // square
            state.Set(PadState.South, (b0 & 0x20) != 0);    // cross
            state.Set(PadState.East, (b0 & 0x40) != 0);     // circle
            state.Set(PadState.North, (b0 & 0x80) != 0);    // triangle
            state.Set(PadState.LB, (b1 & 0x01) != 0);
            state.Set(PadState.RB, (b1 & 0x02) != 0);
            state.Set(PadState.Back, (b1 & 0x10) != 0);     // create
            state.Set(PadState.Start, (b1 & 0x20) != 0);    // options
            state.Set(PadState.L3, (b1 & 0x40) != 0);
            state.Set(PadState.R3, (b1 & 0x80) != 0);
            state.Set(PadState.Home, (b2 & 0x01) != 0);     // PS
            state.Set(PadState.Extra, (b2 & 0x02) != 0);    // touchpad click
        }

        void Sony(byte status)
        {
            int level = status & 0x0F;                                                  // 0..10
            int state = status >> 4;                                                    // 0 discharging, 1 charging, 2 full
            Battery = state == 2 ? 100 : Math.Min(100, level * 10 + 5);
            Charging = state == 1;
        }
    }
}

// ---------------------------------------------------------------- live input state
// One controller's input, normalised. Buttons are positional (South = A / Cross / B on a
// Pro Controller). Sticks are -1..1 with +y up; triggers 0..1.
class PadState
{
    public const int Up = 0, Down = 1, Left = 2, Right = 3, South = 4, East = 5, West = 6, North = 7,
                     LB = 8, RB = 9, L3 = 10, R3 = 11, Back = 12, Start = 13, Home = 14, Extra = 15;
    public readonly string Kind, Key;
    volatile int bits;
    public volatile float LX, LY, RX, RY, LT, RT;
    public DateTime Updated;

    public PadState(string kind, string key) { Kind = kind; Key = key; }
    public bool Is(int b) { return (bits & (1 << b)) != 0; }
    public int Bits { get { return bits; } }
    public void Set(int b, bool on) { if (on) bits |= 1 << b; else bits &= ~(1 << b); }
    public void SetBits(int v) { bits = v; }
    public void Touch() { Updated = DateTime.Now; }
}

// Registry of the controllers currently delivering input.
static class Inputs
{
    static readonly List<PadState> all = new List<PadState>();
    public static void Add(PadState s) { lock (all) if (!all.Contains(s)) all.Add(s); }
    public static void Remove(PadState s) { lock (all) all.Remove(s); }

    // The n-th (0-based) live controller of a kind, in a stable order.
    public static PadState Find(string kind, int n)
    {
        List<PadState> k = new List<PadState>();
        lock (all) foreach (PadState s in all) if (s.Kind == kind) k.Add(s);
        k.Sort(delegate (PadState a, PadState b) { return string.Compare(a.Key, b.Key, StringComparison.Ordinal); });
        return n >= 0 && n < k.Count ? k[n] : null;
    }
}

// ---------------------------------------------------------------- shortcuts
// What each controller and keyboard shortcut is. These are the defaults; ShortcutsFile reads
// shortcuts.ini over them, and again whenever that file is saved, so changing a shortcut needs no
// new build and no restart.
//   controller: button names joined by "+" (hold together) and "," (one after another, each step
//               within StepMs), e.g. "L1 + Right + Triangle" or "L1, Right, L1, Triangle";
//   keyboard:   Ctrl / Alt / Shift / Win + one key, e.g. "Ctrl+Alt+F11".
static class Shortcuts
{
    public const int StepMs = 1500;
    const int NavButtons = (1 << PadState.Up) | (1 << PadState.Down) | (1 << PadState.Left) | (1 << PadState.Right) |
                           (1 << PadState.South) | (1 << PadState.East) | (1 << PadState.West) | (1 << PadState.Start);

    public static volatile int[] Panel = { (1 << PadState.LB) | (1 << PadState.Right) | (1 << PadState.North) };
    public static volatile string PanelText = "L1 + Right + Triangle";
    public static volatile int Audio = (1 << PadState.LB) | (1 << PadState.Right);
    public static volatile int[] Mouse = { (1 << PadState.LB) | (1 << PadState.Right) | (1 << PadState.West) };
    public static volatile string MouseText = "L1 + Right + Square";
    public static volatile int MouseSpeed = 1400;   // pointer pixels per second with the stick all the way
    // The buttons of these shortcuts that the panel does not use (L1 by default). While one is held
    // the controller is doing a shortcut, so the panel and the mouse mode ignore its D-pad and face
    // buttons.
    public static volatile int Guard = 1 << PadState.LB;

    public static void SetPad(int[] panel, string panelText, int audio, int[] mouse, string mouseText, int mouseSpeed)
    {
        int all = audio;
        foreach (int m in panel) all |= m;
        foreach (int m in mouse) all |= m;
        Panel = panel;
        PanelText = panelText;
        Audio = audio;
        Mouse = mouse;
        MouseText = mouseText;
        MouseSpeed = mouseSpeed;
        Guard = all & ~NavButtons;
    }

    // "" or "none" = turned off (no steps). null + error when a name is not a button.
    public static int[] ParsePad(string text, out string error)
    {
        error = null;
        string t = (text ?? "").Trim();
        if (t.Length == 0 || t.Equals("none", StringComparison.OrdinalIgnoreCase)) return new int[0];
        List<int> steps = new List<int>();
        foreach (string step in t.Split(','))
        {
            int mask = 0;
            foreach (string name in step.Split('+'))
            {
                int b = Button(name);
                if (b < 0)
                {
                    error = name.Trim().Length == 0 ? "a button is missing in \"" + t + "\"" : "\"" + name.Trim() + "\" is not a controller button";
                    return null;
                }
                mask |= 1 << b;
            }
            steps.Add(mask);
        }
        return steps.ToArray();
    }

    // Buttons are positional: PlayStation, Xbox and Nintendo-style names of the same spot all work.
    // A B X Y are the Xbox positions (A bottom, Y top).
    public static int Button(string name)
    {
        string n = (name ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
        if (n.StartsWith("dpad")) n = n.Substring(4);
        switch (n)
        {
            case "up": return PadState.Up;
            case "down": return PadState.Down;
            case "left": return PadState.Left;
            case "right": return PadState.Right;
            case "cross": case "a": case "✕": case "×": return PadState.South;
            case "circle": case "b": case "○": case "◯": return PadState.East;
            case "square": case "x": case "□": case "▢": return PadState.West;
            case "triangle": case "y": case "△": return PadState.North;
            case "l1": case "lb": case "l": return PadState.LB;
            case "r1": case "rb": case "r": return PadState.RB;
            case "l3": case "ls": return PadState.L3;
            case "r3": case "rs": return PadState.R3;
            case "share": case "create": case "back": case "view": case "select": case "minus": return PadState.Back;
            case "options": case "start": case "menu": case "plus": return PadState.Start;
            case "home": case "ps": case "guide": case "xbox": return PadState.Home;
            case "touchpad": case "capture": return PadState.Extra;
        }
        return -1;
    }

    // "" or "none" = turned off (vk 0). false + error when the text is not a hotkey.
    public static bool ParseKey(string text, out uint mods, out uint vk, out string error)
    {
        mods = 0; vk = 0; error = null;
        string t = (text ?? "").Trim();
        if (t.Length == 0 || t.Equals("none", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (string raw in t.Split('+'))
        {
            string p = raw.Trim(), l = p.ToLowerInvariant();
            if (l == "ctrl" || l == "control") mods |= 0x2;
            else if (l == "alt") mods |= 0x1;
            else if (l == "shift") mods |= 0x4;
            else if (l == "win" || l == "windows") mods |= 0x8;
            else if (vk != 0) { error = "\"" + t + "\" has more than one key"; return false; }
            else
            {
                Keys k;
                if (p.Length == 1 && char.IsLetterOrDigit(p[0])) vk = (uint)char.ToUpperInvariant(p[0]);
                else if (p.Length > 0 && !char.IsDigit(p[0]) && Enum.TryParse(p, true, out k) &&
                         ((int)k & ~0xFFFF) == 0 && ((int)k & 0xFFFF) > 7) vk = (uint)k;
                else { error = "\"" + p + "\" is not a key"; return false; }
            }
        }
        if (vk == 0) { error = "\"" + t + "\" has no key, only Ctrl / Alt / Shift / Win"; return false; }
        return true;
    }
}

// ---------------------------------------------------------------- the audio combo
// Hold the audio buttons (D-pad right + L1 by default), then flick the right stick:
// right or up = next output, left or down = previous output.
class Combo
{
    public static Action<int> Fire;   // set by the app; called from input threads
    bool armed = true;

    // true when it fired on this reading
    public bool Update(PadState s)
    {
        float x = s.RX, y = s.RY;
        float mag = Math.Max(Math.Abs(x), Math.Abs(y));
        if (mag < 0.30f) { armed = true; return false; }
        int need = Shortcuts.Audio;
        if (need == 0 || (s.Bits & need) != need || !armed || mag < 0.65f) return false;
        armed = false;
        int dir = Math.Abs(x) >= Math.Abs(y) ? (x > 0 ? 1 : -1) : (y > 0 ? 1 : -1);
        Action<int> f = Fire;
        if (f != null) f(dir);
        return true;
    }
}

// Follows one controller shortcut ("a + b" held together, or "a, b, c" one after another) through
// the readings of one controller. A step is done on the reading where the last of its buttons goes
// down; in a sequence, a press outside the current step breaks it (and may start it again).
class ShortcutTracker
{
    int[] steps;
    public int Step;                       // steps done so far (only a sequence gets past 0 without firing)
    DateTime stepAt;

    // true when the shortcut was completed on this reading
    public bool Update(int[] seq, int bits, int before, int pressed, DateTime now)
    {
        if (seq != steps) { steps = seq; Step = 0; }
        if (Step > 0 && (now - stepAt).TotalMilliseconds > Shortcuts.StepMs) Step = 0;
        if (pressed == 0 || seq.Length == 0) return false;
        int cur = seq[Step];
        if (Step > 0 && (pressed & ~cur) != 0) { Step = 0; cur = seq[0]; }
        if ((bits & cur) != cur || (before & cur) == cur) return false;
        Step++;
        stepAt = now;
        if (Step < seq.Length) return false;
        Step = 0;
        return true;
    }
}

// ---------------------------------------------------------------- controller as mouse
// Turned on and off with the mouse shortcut (L1 + D-pad right + Square by default) or the tray
// menu. While it is on and the panel is closed, every controller drives the pointer.
static class MouseMode
{
    public static volatile bool On;
    public static Action<bool> Changed;   // set by the app; called from whichever thread changed it

    public static void Set(bool on)
    {
        if (On == on) return;
        On = on;
        Action<bool> c = Changed;
        if (c != null) c(on);
    }

    public static void Toggle() { Set(!On); }
}

// One controller as a mouse:
//   left stick  pointer (speed grows with the tilt; hold LT / L2 for fine moves)
//   right stick scroll (up / down, left / right)
//   A / Cross   left button (held = drag)      B / Circle  right button      X / Square  middle button
//   D-pad       arrow keys (held = repeat)     Menu / Options  Enter         View / Share  Esc
class PadMouse
{
    public static Action<int, int> Move;   // dx, dy in pixels
    public static Action<int, int> Scroll; // vertical, horizontal wheel units (120 = one notch; + = up / right)
    public static Action<int, bool> Click; // 0 left, 1 right, 2 middle; down / up
    public static Action<int, bool> Key;   // virtual key; down / up (a repeat is another down)

    static readonly int[] ClickPad = { PadState.South, PadState.East, PadState.West };
    static readonly int[] KeyPad = { PadState.Up, PadState.Down, PadState.Left, PadState.Right, PadState.Start, PadState.Back };
    static readonly int[] KeyVk = { 0x26, 0x28, 0x25, 0x27, 0x0D, 0x1B };
    const int RepeatDelayMs = 400, RepeatMs = 45;
    const double Dead = 0.18, NotchesPerSec = 14;

    int down;                              // pad buttons whose mouse button / key is down
    readonly DateTime[] keySince = new DateTime[6], keyLast = new DateTime[6];
    DateTime last;
    double fx, fy, fv, fh;                 // fractions of a pixel / notch still to send

    public void Update(PadState s, int bits, int pressed, bool run, int guard, DateTime now)
    {
        if (!run) { ReleaseAll(); last = DateTime.MinValue; fx = fy = fv = fh = 0; return; }
        double dt = last == DateTime.MinValue ? 0 : Math.Min((now - last).TotalSeconds, 0.05);
        last = now;
        bool guarded = (bits & guard) != 0;  // L1 held: the buttons belong to a shortcut

        // pointer
        double speed = Shortcuts.MouseSpeed * (s.LT > 0.5f ? 0.25 : 1.0);
        double vx, vy;
        Curve(s.LX, -s.LY, out vx, out vy);
        fx += vx * speed * dt;
        fy += vy * speed * dt;
        int ix = (int)fx, iy = (int)fy;
        fx -= ix; fy -= iy;
        if (ix != 0 || iy != 0) { Action<int, int> m = Move; if (m != null) m(ix, iy); }

        // scroll, in whole notches (some apps take any wheel message as a full notch)
        double sx, sy;
        Curve(s.RX, s.RY, out sx, out sy);
        if (guarded) { sx = sy = 0; fv = fh = 0; }   // the audio combo flicks the right stick
        fv += sy * NotchesPerSec * dt;
        fh += sx * NotchesPerSec * dt;
        int nv = (int)fv, nh = (int)fh;
        fv -= nv; fh -= nh;
        if (nv != 0 || nh != 0) { Action<int, int> w = Scroll; if (w != null) w(nv * 120, nh * 120); }

        // buttons: a press starts only on its own rising edge and outside a shortcut; a release
        // always lets go of what was pressed
        for (int i = 0; i < ClickPad.Length; i++)
        {
            int b = 1 << ClickPad[i];
            if ((down & b) == 0 && (pressed & b) != 0 && !guarded) { down |= b; SendClick(i, true); }
            else if ((down & b) != 0 && (bits & b) == 0) { down &= ~b; SendClick(i, false); }
        }
        for (int i = 0; i < KeyPad.Length; i++)
        {
            int b = 1 << KeyPad[i];
            if ((down & b) == 0 && (pressed & b) != 0 && !guarded)
            { down |= b; keySince[i] = keyLast[i] = now; SendKey(KeyVk[i], true); }
            else if ((down & b) != 0 && (bits & b) == 0) { down &= ~b; SendKey(KeyVk[i], false); }
            else if ((down & b) != 0 && i < 4 && (now - keySince[i]).TotalMilliseconds >= RepeatDelayMs &&
                     (now - keyLast[i]).TotalMilliseconds >= RepeatMs)
            { keyLast[i] = now; SendKey(KeyVk[i], true); }
        }
    }

    // Round dead zone, then a squared response: small tilts are precise, a full tilt is fast.
    static void Curve(float x, float y, out double ox, out double oy)
    {
        double mag = Math.Sqrt(x * x + y * y);
        if (mag <= Dead) { ox = oy = 0; return; }
        double t = Math.Min(1, (mag - Dead) / (1 - Dead));
        double k = t * t / mag;
        ox = x * k; oy = y * k;
    }

    void ReleaseAll()
    {
        if (down == 0) return;
        for (int i = 0; i < ClickPad.Length; i++) if ((down & (1 << ClickPad[i])) != 0) SendClick(i, false);
        for (int i = 0; i < KeyPad.Length; i++) if ((down & (1 << KeyPad[i])) != 0) SendKey(KeyVk[i], false);
        down = 0;
    }

    static void SendClick(int which, bool isDown) { Action<int, bool> c = Click; if (c != null) c(which, isDown); }
    static void SendKey(int vk, bool isDown) { Action<int, bool> k = Key; if (k != null) k(vk, isDown); }
}

// Everything one controller drives, fed from its input thread after each reading:
//  - the audio combo (above);
//  - the panel shortcut (L1 + D-pad right + Triangle by default) to open or hide the panel;
//  - the mouse shortcut (L1 + D-pad right + Square by default) and the mouse mode itself;
//  - panel navigation while the panel is open: D-pad / left stick, A, B, X, Menu, and holding B.
class PadHooks
{
    public static Action Toggle;          // the panel shortcut was pressed
    public static Action<string> Nav;     // "up" "down" "left" "right" "a" "b" "x" "start" "bhold"
    public static volatile bool NavActive; // the panel is open: send navigation

    const int RepeatDelayMs = 380, RepeatMs = 110, BHoldMs = 700;
    const int Wait = -2;                   // dirHeld: a direction is still held from a shortcut; wait for release

    readonly Combo combo = new Combo();
    readonly ShortcutTracker panelKey = new ShortcutTracker(), mouseKey = new ShortcutTracker();
    readonly PadMouse mouse = new PadMouse();
    int prev;
    // Navigation that happens mid-sequence is held back, so a D-pad step of the sequence does not move
    // the focus or the volume; it is dropped if the sequence completes, sent if it breaks.
    readonly List<string> held = new List<string>();
    int dirHeld = -1;                      // PadState.Up/Down/Left/Right being held (D-pad or stick), -1 none
    DateTime dirSince, dirLast;
    DateTime bSince;
    bool bFired;

    bool InSequence { get { return panelKey.Step > 0 || mouseKey.Step > 0; } }

    public void Update(PadState s)
    {
        DateTime now = DateTime.Now;
        int bits = s.Bits, before = prev;
        int pressed = bits & ~before;      // rising edges
        prev = bits;

        if (combo.Update(s)) { panelKey.Step = mouseKey.Step = 0; held.Clear(); }

        // -------- shortcuts
        if (panelKey.Update(Shortcuts.Panel, bits, before, pressed, now))
        {
            held.Clear();
            Action t = Toggle;
            if (t != null) t();
        }
        if (mouseKey.Update(Shortcuts.Mouse, bits, before, pressed, now)) { held.Clear(); MouseMode.Toggle(); }
        if (!InSequence) Flush();          // a sequence broke or timed out: what it held back goes out

        mouse.Update(s, bits, pressed, MouseMode.On && !NavActive, Shortcuts.Guard, now);

        if (!NavActive) { held.Clear(); dirHeld = Wait; bFired = true; return; }

        // While a shortcut button the panel does not use (L1) is held, the D-pad and face buttons
        // belong to the shortcut. A direction still held when it is let go waits for its release.
        if ((bits & Shortcuts.Guard) != 0) { dirHeld = Wait; bFired = true; return; }

        // -------- navigation
        if ((pressed & (1 << PadState.South)) != 0) Emit("a");
        if ((pressed & (1 << PadState.East)) != 0) Emit("b");
        if ((pressed & (1 << PadState.West)) != 0) Emit("x");
        if ((pressed & (1 << PadState.Start)) != 0) Emit("start");

        if (s.Is(PadState.East))
        {
            if ((pressed & (1 << PadState.East)) != 0) { bSince = now; bFired = false; }
            if (!bFired && (now - bSince).TotalMilliseconds >= BHoldMs) { bFired = true; Emit("bhold"); }
        }

        int dir = Direction(s);
        if (dir < 0) dirHeld = -1;
        else if (dirHeld == Wait) { }
        else if (dir != dirHeld) { dirHeld = dir; dirSince = dirLast = now; Emit(DirName(dir)); }
        else if ((now - dirSince).TotalMilliseconds >= RepeatDelayMs && (now - dirLast).TotalMilliseconds >= RepeatMs)
        { dirLast = now; Emit(DirName(dir)); }
    }

    // D-pad first, then the left stick (with hysteresis so a resting stick never drifts the focus).
    int Direction(PadState s)
    {
        if (s.Is(PadState.Up)) return PadState.Up;
        if (s.Is(PadState.Down)) return PadState.Down;
        if (s.Is(PadState.Left)) return PadState.Left;
        if (s.Is(PadState.Right)) return PadState.Right;
        float x = s.LX, y = s.LY, ax = Math.Abs(x), ay = Math.Abs(y);
        float need = dirHeld >= 0 ? 0.35f : 0.6f;
        if (Math.Max(ax, ay) < need) return -1;
        if (ax >= ay) return x > 0 ? PadState.Right : PadState.Left;
        return y > 0 ? PadState.Up : PadState.Down;
    }

    static string DirName(int d)
    {
        return d == PadState.Up ? "up" : d == PadState.Down ? "down" : d == PadState.Left ? "left" : "right";
    }

    void Emit(string what)
    {
        if (InSequence) { held.Add(what); return; }
        Action<string> n = Nav;
        if (n != null) n(what);
    }

    void Flush()
    {
        if (held.Count == 0) return;
        string[] q = held.ToArray();
        held.Clear();
        Action<string> n = Nav;
        if (n != null && NavActive) foreach (string w in q) n(w);
    }
}

// Xbox controllers through XInput (the hidden "Ex" entry point also reports the Guide button).
static class XInputPoller
{
    [StructLayout(LayoutKind.Sequential)]
    struct XState { public uint packet; public ushort buttons; public byte lt, rt; public short lx, ly, rx, ry; }
    [DllImport("xinput1_4.dll", EntryPoint = "#100")] static extern int XInputGetStateEx(int i, out XState s);

    public static void Start()
    {
        Thread t = new Thread(Run);
        t.IsBackground = true;
        t.Start();
    }

    static float Stick(short v) { return Math.Max(-1f, v / 32767f); }

    static void Run()
    {
        PadHooks[] hooks = { new PadHooks(), new PadHooks(), new PadHooks(), new PadHooks() };
        PadState[] states = new PadState[4];
        for (int i = 0; i < 4; i++) states[i] = new PadState("xbox", "xinput" + i);
        int[] misses = new int[4];
        while (true)
        {
            bool any = false;
            for (int i = 0; i < 4; i++)
            {
                if (misses[i] > 0) { misses[i]--; continue; }    // don't poll empty slots every tick
                XState x;
                if (XInputGetStateEx(i, out x) != 0) { Inputs.Remove(states[i]); misses[i] = 100; continue; }
                any = true;
                PadState s = states[i];
                ushort w = x.buttons;
                int bits = 0;
                if ((w & 0x0001) != 0) bits |= 1 << PadState.Up;
                if ((w & 0x0002) != 0) bits |= 1 << PadState.Down;
                if ((w & 0x0004) != 0) bits |= 1 << PadState.Left;
                if ((w & 0x0008) != 0) bits |= 1 << PadState.Right;
                if ((w & 0x0010) != 0) bits |= 1 << PadState.Start;
                if ((w & 0x0020) != 0) bits |= 1 << PadState.Back;
                if ((w & 0x0040) != 0) bits |= 1 << PadState.L3;
                if ((w & 0x0080) != 0) bits |= 1 << PadState.R3;
                if ((w & 0x0100) != 0) bits |= 1 << PadState.LB;
                if ((w & 0x0200) != 0) bits |= 1 << PadState.RB;
                if ((w & 0x0400) != 0) bits |= 1 << PadState.Home;
                if ((w & 0x1000) != 0) bits |= 1 << PadState.South;
                if ((w & 0x2000) != 0) bits |= 1 << PadState.East;
                if ((w & 0x4000) != 0) bits |= 1 << PadState.West;
                if ((w & 0x8000) != 0) bits |= 1 << PadState.North;
                s.SetBits(bits);
                s.LX = Stick(x.lx); s.LY = Stick(x.ly); s.RX = Stick(x.rx); s.RY = Stick(x.ry);
                s.LT = x.lt / 255f; s.RT = x.rt / 255f;
                s.Touch();
                Inputs.Add(s);
                hooks[i].Update(s);
            }
            Thread.Sleep(any ? 8 : 30);
        }
    }
}

// Mouse and keyboard events for the controller mouse (PadMouse). Windows does not let a normal app
// send input to a window that runs as administrator, so those ignore it.
static class Inject
{
    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput { public int dx, dy; public int data; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)]
    struct KeyInput { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)]
    struct Input
    {
        [FieldOffset(0)] public uint type;      // 0 mouse, 1 keyboard
        [FieldOffset(8)] public MouseInput mi;  // x64: the union starts after 4 bytes of padding
        [FieldOffset(8)] public KeyInput ki;
    }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);

    const uint MOVE = 0x1, WHEEL = 0x800, HWHEEL = 0x1000, KEYUP = 0x2, EXTENDED = 0x1;
    static readonly uint[] ClickDown = { 0x2, 0x8, 0x20 }, ClickUp = { 0x4, 0x10, 0x40 };   // left, right, middle

    static void Send(Input i) { SendInput(1, new Input[] { i }, Marshal.SizeOf(typeof(Input))); }

    static void Mouse(int dx, int dy, int data, uint flags)
    {
        Input i = new Input();
        i.type = 0;
        i.mi.dx = dx; i.mi.dy = dy; i.mi.data = data; i.mi.flags = flags;
        Send(i);
    }

    public static void Move(int dx, int dy) { Mouse(dx, dy, 0, MOVE); }

    public static void Scroll(int vertical, int horizontal)
    {
        if (vertical != 0) Mouse(0, 0, vertical, WHEEL);
        if (horizontal != 0) Mouse(0, 0, horizontal, HWHEEL);
    }

    public static void Click(int which, bool down) { Mouse(0, 0, 0, down ? ClickDown[which] : ClickUp[which]); }

    public static void Key(int vk, bool down)
    {
        Input i = new Input();
        i.type = 1;
        i.ki.vk = (ushort)vk;
        i.ki.scan = (ushort)MapVirtualKey((uint)vk, 0);
        bool extended = vk >= 0x21 && vk <= 0x2E;   // arrows, Home / End, Page Up / Down, Insert, Delete
        i.ki.flags = (down ? 0 : KEYUP) | (extended ? EXTENDED : 0);
        Send(i);
    }
}

// ---------------------------------------------------------------- shared drawing
static class Ui
{
    public const string GlyphTv = "\uE7F4", GlyphHeadphone = "\uE7F6", GlyphSpeaker = "\uE767",
                        GlyphPad = "\uE7FC", GlyphBolt = "\uE945";

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

    static string iconFont;
    public static string IconFont
    {
        get
        {
            if (iconFont != null) return iconFont;
            foreach (string f in new string[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                using (Font t = new Font(f, 10f)) if (t.Name == f) { iconFont = f; return f; }
            return iconFont = "Segoe UI Symbol";
        }
    }

    public static readonly Color Back = Color.FromArgb(32, 32, 34);
    public static readonly Color Card = Color.FromArgb(44, 44, 47);
    public static readonly Color Text = Color.FromArgb(245, 245, 247);
    public static readonly Color Dim = Color.FromArgb(160, 160, 168);
    public static readonly Color Faint = Color.FromArgb(90, 90, 96);

    public static Color Accent
    {
        get
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (v is int) { int c = (int)v; return Color.FromArgb(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF); }
            }
            catch { }
            return Color.FromArgb(0, 120, 212);
        }
    }

    public static Color BatteryColor(int pct)
    {
        if (pct < 0) return Faint;
        if (pct <= 20) return Color.FromArgb(232, 72, 85);
        if (pct <= 45) return Color.FromArgb(240, 180, 41);
        return Color.FromArgb(46, 196, 120);
    }

    // Rounded corners, dark frame and a thin border on Windows 11.
    public static void Frame(IntPtr hwnd)
    {
        int round = 2, dark = 1, border = 0x00423F3C; // COLORREF 0x00BBGGRR
        DwmSetWindowAttribute(hwnd, 33, ref round, 4);
        DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        DwmSetWindowAttribute(hwnd, 34, ref border, 4);
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        GraphicsPath p = new GraphicsPath();
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    // ------------------------------------------------------------ drawn icons
    // Solid icons drawn with paths (cut-outs are cleared to transparent), rendered at 2x,
    // then re-centred on their real ink bounds so every icon sits dead centre in its box.
    // Kinds: pad, monitor, tv, headphones, speaker.
    static readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>();

    public static void Icon(Graphics g, string kind, RectangleF box, Color c)
    {
        int px = (int)Math.Ceiling(Math.Max(box.Width, box.Height) * 2);
        InterpolationMode oldI = g.InterpolationMode;
        PixelOffsetMode oldP = g.PixelOffsetMode;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(IconBitmap(kind, px, c), box);
        g.InterpolationMode = oldI;
        g.PixelOffsetMode = oldP;
    }

    public static Bitmap IconBitmap(string kind, int s, Color c)
    {
        string key = kind + ":" + s + ":" + c.ToArgb();
        Bitmap cached;
        if (iconCache.TryGetValue(key, out cached)) return cached;
        Bitmap raw = new Bitmap(s, s);
        using (Graphics g = Graphics.FromImage(raw))
        using (Brush fill = new SolidBrush(c))
        using (Brush clear = new SolidBrush(Color.Transparent))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            switch (kind)
            {
                case "monitor": DrawScreen(g, fill, clear, s, false); break;
                case "tv": DrawScreen(g, fill, clear, s, true); break;
                case "headphones": DrawHeadphones(g, c, fill, s); break;
                case "speaker": DrawSpeaker(g, c, fill, s); break;
                case "mic": DrawMic(g, c, fill, s); break;
                default: DrawPad(g, fill, clear, s); break;
            }
        }
        Bitmap bmp = CenterInk(raw);
        raw.Dispose();
        iconCache[key] = bmp;
        return bmp;
    }

    static void DrawPad(Graphics g, Brush fill, Brush clear, int s)
    {
        float w = s * 0.94f, h = w * 0.66f, x = (s - w) / 2, y = (s - h) / 2;
        using (GraphicsPath body = new GraphicsPath(FillMode.Winding))
        {
            body.AddPath(Rounded(new RectangleF(x + w * 0.06f, y, w * 0.88f, h * 0.64f), h * 0.30f), false);
            body.AddEllipse(x, y + h * 0.20f, w * 0.38f, h * 0.80f);
            body.AddEllipse(x + w * 0.62f, y + h * 0.20f, w * 0.38f, h * 0.80f);
            g.FillPath(fill, body);
        }
        g.CompositingMode = CompositingMode.SourceCopy;
        float t = w * 0.075f, L = w * 0.21f, cx = x + w * 0.27f, cy = y + h * 0.40f;
        using (GraphicsPath dpad = new GraphicsPath(FillMode.Winding))
        {
            dpad.AddPath(Rounded(new RectangleF(cx - L / 2, cy - t / 2, L, t), t / 2.2f), false);
            dpad.AddPath(Rounded(new RectangleF(cx - t / 2, cy - L / 2, t, L), t / 2.2f), false);
            g.FillPath(clear, dpad);
        }
        float r = w * 0.075f, d = w * 0.075f, bx = x + w * 0.73f, by = y + h * 0.40f;
        g.FillEllipse(clear, bx - d / 2, by - r - d / 2, d, d);
        g.FillEllipse(clear, bx + r - d / 2, by - d / 2, d, d);
        g.FillEllipse(clear, bx - d / 2, by + r - d / 2, d, d);
        g.FillEllipse(clear, bx - r - d / 2, by - d / 2, d, d);
        g.CompositingMode = CompositingMode.SourceOver;
    }

    // Monitor: bezel + neck + base. TV: wider bezel + two feet.
    static void DrawScreen(Graphics g, Brush fill, Brush clear, int s, bool tv)
    {
        float w = s * (tv ? 0.96f : 0.86f), h = w * (tv ? 0.58f : 0.64f);
        float stand = tv ? s * 0.08f : s * 0.17f;
        float x = (s - w) / 2, y = (s - (h + stand)) / 2;
        float r = s * 0.07f, t = s * 0.075f;
        using (GraphicsPath frame = Rounded(new RectangleF(x, y, w, h), r)) g.FillPath(fill, frame);
        g.CompositingMode = CompositingMode.SourceCopy;
        using (GraphicsPath inner = Rounded(new RectangleF(x + t, y + t, w - 2 * t, h - 2 * t), r * 0.45f)) g.FillPath(clear, inner);
        g.CompositingMode = CompositingMode.SourceOver;
        if (tv)
        {
            float fw = s * 0.07f;
            using (GraphicsPath a = Rounded(new RectangleF(x + w * 0.16f, y + h - 1, fw, stand + 1), fw * 0.4f)) g.FillPath(fill, a);
            using (GraphicsPath b = Rounded(new RectangleF(x + w * 0.84f - fw, y + h - 1, fw, stand + 1), fw * 0.4f)) g.FillPath(fill, b);
        }
        else
        {
            float nw = s * 0.09f, bh = s * 0.07f;
            g.FillRectangle(fill, s / 2f - nw / 2, y + h - 1, nw, stand - bh + 2);
            using (GraphicsPath b = Rounded(new RectangleF(s / 2f - w * 0.26f, y + h + stand - bh, w * 0.52f, bh), bh / 2)) g.FillPath(fill, b);
        }
    }

    static void DrawHeadphones(Graphics g, Color c, Brush fill, int s)
    {
        float pw = s * 0.095f;
        float bandX = s * 0.15f, bandW = s * 0.70f, bandY = s * 0.12f;
        using (Pen band = new Pen(c, pw))
        {
            band.StartCap = LineCap.Round;
            band.EndCap = LineCap.Round;
            g.DrawArc(band, bandX, bandY, bandW, bandW, 180, 180);
            float cy = bandY + bandW / 2;
            g.DrawLine(band, bandX, cy, bandX, cy + s * 0.12f);
            g.DrawLine(band, bandX + bandW, cy, bandX + bandW, cy + s * 0.12f);
        }
        float cw = s * 0.22f, ch = s * 0.36f, top = bandY + bandW / 2 + s * 0.04f;
        using (GraphicsPath l = Rounded(new RectangleF(bandX - cw * 0.35f, top, cw, ch), cw * 0.42f)) g.FillPath(fill, l);
        using (GraphicsPath r = Rounded(new RectangleF(bandX + bandW - cw * 0.65f, top, cw, ch), cw * 0.42f)) g.FillPath(fill, r);
    }

    static void DrawSpeaker(Graphics g, Color c, Brush fill, int s)
    {
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddPolygon(new PointF[] {
                new PointF(s * 0.10f, s * 0.38f), new PointF(s * 0.28f, s * 0.38f), new PointF(s * 0.50f, s * 0.18f),
                new PointF(s * 0.50f, s * 0.82f), new PointF(s * 0.28f, s * 0.62f), new PointF(s * 0.10f, s * 0.62f) });
            using (Pen round = new Pen(c, s * 0.05f)) { round.LineJoin = LineJoin.Round; g.DrawPath(round, p); }
            g.FillPath(fill, p);
        }
        using (Pen wave = new Pen(c, s * 0.075f))
        {
            wave.StartCap = LineCap.Round;
            wave.EndCap = LineCap.Round;
            g.DrawArc(wave, s * 0.42f, s * 0.34f, s * 0.32f, s * 0.32f, -50, 100);
            g.DrawArc(wave, s * 0.36f, s * 0.20f, s * 0.58f, s * 0.60f, -50, 100);
        }
    }

    // Microphone: capsule, U-shaped holder, stem and base.
    static void DrawMic(Graphics g, Color c, Brush fill, int s)
    {
        float cw = s * 0.34f, ch = s * 0.50f;
        using (GraphicsPath cap = Rounded(new RectangleF((s - cw) / 2, s * 0.06f, cw, ch), cw / 2)) g.FillPath(fill, cap);
        using (Pen pen = new Pen(c, s * 0.08f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            float hw = s * 0.58f;
            g.DrawArc(pen, (s - hw) / 2, s * 0.22f, hw, s * 0.50f, 0, 180);
            g.DrawLine(pen, s / 2f, s * 0.72f, s / 2f, s * 0.86f);
            g.DrawLine(pen, s * 0.34f, s * 0.90f, s * 0.66f, s * 0.90f);
        }
    }

    // Copy the drawn ink so that its bounding box is centred in the bitmap.
    static Bitmap CenterInk(Bitmap src)
    {
        int s = src.Width, minX = s, minY = s, maxX = -1, maxY = -1;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                if (src.GetPixel(x, y).A > 24)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        Bitmap dst = new Bitmap(s, s);
        if (maxX < 0) return dst;
        float dx = (s - 1 - maxX - minX) / 2f, dy = (s - 1 - maxY - minY) / 2f;
        using (Graphics g = Graphics.FromImage(dst))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(src, new RectangleF(dx, dy, s, s));
        }
        return dst;
    }

    // Icon-font glyph centred on its real ink bounds (fonts pad glyphs unevenly, so
    // centring the text box leaves the icon visibly off-centre).
    public static void Glyph(Graphics g, string glyph, float size, Color color, RectangleF box)
    {
        using (FontFamily ff = new FontFamily(IconFont))
        using (GraphicsPath p = new GraphicsPath())
        {
            p.AddString(glyph, ff, (int)FontStyle.Regular, size, PointF.Empty, StringFormat.GenericTypographic);
            RectangleF b = p.GetBounds();
            if (b.Width <= 0 || b.Height <= 0) return;
            using (Matrix m = new Matrix())
            {
                m.Translate(box.X + (box.Width - b.Width) / 2 - b.X, box.Y + (box.Height - b.Height) / 2 - b.Y);
                p.Transform(m);
            }
            using (Brush br = new SolidBrush(color)) g.FillPath(br, p);
        }
    }

    // Live audio level: 12 thin vertical pills, green, then amber, then red at the top.
    public const int LevelCount = 12;
    public const float LevelW = 3, LevelH = 16, LevelGap = 2;
    public static float LevelWidth { get { return LevelCount * LevelW + (LevelCount - 1) * LevelGap; } }

    public static void LevelPills(Graphics g, float x, float y, float level)
    {
        int lit = (int)Math.Round(Math.Max(0, Math.Min(1, level)) * LevelCount);
        for (int i = 0; i < LevelCount; i++)
        {
            Color on = i >= LevelCount - 1 ? Color.FromArgb(232, 72, 85) : i >= LevelCount - 3 ? Color.FromArgb(240, 180, 41) : Color.FromArgb(46, 196, 120);
            RectangleF r = new RectangleF(x + i * (LevelW + LevelGap), y, LevelW, LevelH);
            using (GraphicsPath p = Rounded(r, LevelW / 2))
            using (Brush b = new SolidBrush(i < lit ? on : Color.FromArgb(58, 58, 62)))
                g.FillPath(b, p);
        }
    }

    // Vertical pills, lit from the left by battery level.
    public const int PillCount = 10;
    public const float PillW = 4, PillH = 20, PillGap = 3;
    public static float PillsWidth { get { return PillCount * PillW + (PillCount - 1) * PillGap; } }

    public static void BatteryPills(Graphics g, float x, float y, int pct)
    {
        int lit = pct < 0 ? 0 : Math.Max(1, (int)Math.Ceiling(pct / (100.0 / PillCount)));
        Color on = BatteryColor(pct), off = Color.FromArgb(62, 62, 66);
        for (int i = 0; i < PillCount; i++)
        {
            RectangleF r = new RectangleF(x + i * (PillW + PillGap), y, PillW, PillH);
            using (GraphicsPath p = Rounded(r, PillW / 2))
            using (Brush b = new SolidBrush(i < lit ? on : off))
                g.FillPath(b, p);
        }
    }
}

// ---------------------------------------------------------------- audio banner
class Banner : Form
{
    readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
    string title = "", caption = "Audio output", glyph = "speaker";
    int index, count;
    DateTime shownAt;
    const int HoldMs = 2200;

    public Banner()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Ui.Back;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        anim.Interval = 15;
        anim.Tick += delegate { Step(); };
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; // NOACTIVATE | TOOLWINDOW | TOPMOST
            cp.ClassStyle |= 0x20000;                            // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.Frame(Handle); }

    public void ShowDevice(string name, int index, int count)
    {
        Show("Audio output", name, Audio.Glyph(name), index, count);
    }

    public void Show(string caption, string name, string kind, int index, int count)
    {
        this.caption = caption; title = name; glyph = kind; this.index = index; this.count = count;
        using (Graphics g = CreateGraphics())
        using (Font tf = new Font("Segoe UI Semibold", 20f, GraphicsUnit.Pixel))
        {
            int textW = (int)Math.Ceiling(g.MeasureString(title, tf).Width);
            using (Font cf = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
                textW = Math.Max(textW, (int)Math.Ceiling(g.MeasureString(caption.ToUpperInvariant(), cf).Width));
            Size = new Size(Math.Max(240, 88 + textW + 28), 84);
        }
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Left + 50, wa.Top + 60);
        Invalidate();
        if (!Visible) { Opacity = 0; Show(); }
        shownAt = DateTime.Now;
        anim.Start();
    }

    void Step()
    {
        double t = (DateTime.Now - shownAt).TotalMilliseconds;
        if (t < 140) Opacity = t / 140.0;
        else if (t < 140 + HoldMs) Opacity = 1;
        else if (t < 140 + HoldMs + 260) Opacity = 1 - (t - 140 - HoldMs) / 260.0;
        else { anim.Stop(); Hide(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Smooth(g);
        g.Clear(Ui.Back);
        Color accent = Ui.Accent;

        // accent strip + icon disc
        using (Brush a = new SolidBrush(accent)) g.FillRectangle(a, 0, 0, 4, Height);
        RectangleF disc = new RectangleF(20, 18, 48, 48);
        using (Brush a = new SolidBrush(Color.FromArgb(48, accent))) g.FillEllipse(a, disc);
        Ui.Icon(g, glyph, new RectangleF(disc.X + 12, disc.Y + 12, disc.Width - 24, disc.Height - 24),
                accent.GetBrightness() < 0.35f ? Color.White : accent);

        using (Font cf = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
        using (Brush cb = new SolidBrush(Ui.Dim))
            g.DrawString(caption.ToUpperInvariant(), cf, cb, 86, 16);
        using (Font tf = new Font("Segoe UI Semibold", 20f, GraphicsUnit.Pixel))
        using (Brush tb = new SolidBrush(Ui.Text))
            g.DrawString(title, tf, tb, 84, 32);

        // position dots
        if (count > 1)
        {
            float x = 88, y = 64;
            for (int i = 0; i < count; i++)
            {
                bool on = i == index;
                float w = on ? 14 : 5;
                using (GraphicsPath p = Ui.Rounded(new RectangleF(x, y, w, 5), 2.5f))
                using (Brush b = new SolidBrush(on ? accent : Ui.Faint))
                    g.FillPath(b, p);
                x += w + 5;
            }
        }
    }
}

// ---------------------------------------------------------------- controllers panel
class WebPanel : Form
{
    readonly Microsoft.Web.WebView2.WinForms.WebView2 web = new Microsoft.Web.WebView2.WinForms.WebView2();
    readonly System.Windows.Forms.Timer levelsTick = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    bool ready, wantShow, prewarming;
    List<Audio.Endpoint> outs = new List<Audio.Endpoint>(), mics = new List<Audio.Endpoint>();

    public WebPanel()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Ui.Back;
        Size = new Size(360, 320);
        web.Dock = DockStyle.Fill;
        web.DefaultBackgroundColor = Ui.Back;
        Controls.Add(web);
        levelsTick.Interval = 50;
        levelsTick.Tick += delegate { SendLevels(); };
        refresh.Interval = 2000;
        refresh.Tick += delegate { SendState(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00000080;   // TOOLWINDOW
            cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return prewarming; } }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Ui.Frame(Handle); }

    // Load the page off-screen at start-up so the first click on the tray opens instantly.
    public void Prewarm()
    {
        prewarming = true;
        Location = new Point(-5000, -5000);
        Show();
        Init();
    }

    async void Init()
    {
        try
        {
            string data = Path.Combine(AppPaths.Data, "WebView2");
            CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, data, null);
            await web.EnsureCoreWebView2Async(env);
            CoreWebView2 cw = web.CoreWebView2;
            cw.Settings.AreDefaultContextMenusEnabled = false;
            cw.Settings.AreDevToolsEnabled = false;
            cw.Settings.IsZoomControlEnabled = false;
            cw.Settings.IsStatusBarEnabled = false;
            cw.Settings.AreBrowserAcceleratorKeysEnabled = false;
            string ui = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ui");
            cw.SetVirtualHostNameToFolderMapping("xdeck.ui", ui, CoreWebView2HostResourceAccessKind.Allow);
            cw.WebMessageReceived += OnMessage;
            cw.Navigate("https://xdeck.ui/index.html");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Xdeck could not start its panel (WebView2): " + ex.Message, "Xdeck");
        }
    }

    // ------------------------------------------------ open / close
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

    bool padMode;                 // opened from a controller: the page shows a focus ring from the start
    IntPtr returnTo = IntPtr.Zero; // the window (usually the game) that had the focus before the panel

    public bool Open { get { return Visible && !prewarming; } }

    public void Toggle()
    {
        if (Open) HidePanel();
        else ShowPanel();
    }

    // The controller sequence: open or hide, and remember that a controller drives it.
    public void TogglePad()
    {
        if (Open) HidePanel();
        else ShowPanel(true);
    }

    public void ShowPanel() { ShowPanel(false); }

    public void ShowPanel(bool fromPad)
    {
        bool wasOpen = Open;
        padMode = fromPad;
        wantShow = true;
        prewarming = false;
        if (!wasOpen)
        {
            // from the tray the foreground is the taskbar itself; only a controller opens it over a game
            IntPtr fg = GetForegroundWindow();
            returnTo = fromPad && fg != Handle ? fg : IntPtr.Zero;
        }
        if (ready) { Post(ShowMessage()); SendState(); }
        Place();
        if (!Visible) Show();
        if (fromPad) ForceForeground(); else Activate();
        web.Focus();
        levelsTick.Start();
        refresh.Start();
        PadHooks.NavActive = true;
    }

    public void HidePanel()
    {
        bool hadFocus = GetForegroundWindow() == Handle;
        PadHooks.NavActive = false;
        wantShow = false;
        levelsTick.Stop();
        refresh.Stop();
        Meters.CloseAll();
        Hide();
        // give the focus back to the game (or whatever was in front), so it gets its input again
        if (hadFocus && returnTo != IntPtr.Zero && IsWindow(returnTo)) SetForegroundWindow(returnTo);
        returnTo = IntPtr.Zero;
    }

    Dictionary<string, object> ShowMessage()
    {
        return new Dictionary<string, object> { { "type", "show" }, { "pad", padMode }, { "combo", Shortcuts.PanelText } };
    }

    // shortcuts.ini changed: refresh the hint that names the panel shortcut
    public void ShortcutsChanged()
    {
        if (ready) Post(new Dictionary<string, object> { { "type", "shortcuts" }, { "combo", Shortcuts.PanelText } });
        SendState();   // the mouse switch names its shortcut
    }

    // A controller press is not user input Windows lets a background app take the focus with, so
    // attach to the foreground thread for a moment (the tray click does not need this).
    void ForceForeground()
    {
        IntPtr fg = GetForegroundWindow();
        uint me = GetCurrentThreadId();
        uint other = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, IntPtr.Zero);
        bool attached = other != 0 && other != me && AttachThreadInput(me, other, true);
        try { BringWindowToTop(Handle); SetForegroundWindow(Handle); Activate(); }
        finally { if (attached) AttachThreadInput(me, other, false); }
    }

    // Controller navigation from PadHooks, on the UI thread.
    public void Nav(string what)
    {
        if (!Open || !ready) return;
        Post(new Dictionary<string, object> { { "type", "pad" }, { "btn", what } });
    }

    public void Reload() { if (Visible && !prewarming) SendState(); }

    void Place()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
    }

    float Scale_()
    {
        using (Graphics g = CreateGraphics()) return g.DpiX / 96f;
    }

    // ------------------------------------------------ bridge
    void Post(object msg)
    {
        if (web.CoreWebView2 == null) return;
        try { web.CoreWebView2.PostWebMessageAsJson(json.Serialize(msg)); } catch { }
    }

    void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        Dictionary<string, object> m;
        try { m = json.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson); } catch { return; }
        object t;
        if (m == null || !m.TryGetValue("type", out t)) return;
        switch ((string)t)
        {
            case "ready":
                ready = true;
                SendState();
                if (prewarming) { prewarming = false; Hide(); }
                else if (wantShow) Post(ShowMessage());
                break;
            case "size":
                float s = Scale_();
                int w = (int)Math.Ceiling(Convert.ToDouble(m["w"]) * s), h = (int)Math.Ceiling(Convert.ToDouble(m["h"]) * s);
                Size = new Size(w, h);
                Place();
                break;
            case "close":
                HidePanel();
                break;
            case "select":
                try { Audio.SetDefault((string)m["id"]); } catch { }
                SendState();
                break;
            case "volume":                                      // live while the slider moves; no state echo
                try { Audio.SetVolume((string)m["id"], (float)Convert.ToDouble(m["v"])); } catch { }
                break;
            case "mute":
                try { Audio.SetMute((string)m["id"], Convert.ToBoolean(m["m"])); } catch { }
                SendState();
                break;
            case "mouse":
                try { MouseMode.Set(Convert.ToBoolean(m["on"])); } catch { }
                break;
        }
    }

    static object EndpointJson(Audio.Endpoint ep)
    {
        float vol; bool mute;
        bool ok = false;
        try { ok = Audio.GetVolume(ep.Id, out vol, out mute); } catch { vol = 0; mute = false; }
        Dictionary<string, object> d = new Dictionary<string, object> { { "id", ep.Id }, { "name", ep.Name } };
        if (ok) { d["vol"] = Math.Round(vol, 3); d["mute"] = mute; }
        return d;
    }

    public void SendState()
    {
        if (!ready) return;
        string defOut = null, defMic = null;
        try { outs = Audio.Active(false); defOut = Audio.DefaultId(false); } catch { outs = new List<Audio.Endpoint>(); }
        try { mics = Audio.Active(true); defMic = Audio.DefaultId(true); } catch { mics = new List<Audio.Endpoint>(); }
        List<Pad> pads;
        try { pads = Pads.List(); } catch { pads = new List<Pad>(); }

        Color a = Ui.Accent;
        Post(new Dictionary<string, object> {
            { "type", "state" },
            { "accent", string.Format("#{0:x2}{1:x2}{2:x2}", a.R, a.G, a.B) },
            { "outs", outs.ConvertAll(EndpointJson) },
            { "mics", mics.ConvertAll(EndpointJson) },
            { "defOut", defOut }, { "defMic", defMic },
            { "pads", pads.ConvertAll(delegate (Pad p) { return (object)new Dictionary<string, object> {
                { "key", p.Mac }, { "kind", p.Kind }, { "name", p.Name }, { "index", p.Index },
                { "battery", p.Battery }, { "charging", p.Charging }, { "conn", p.Conn } }; }) },
            { "mouse", MouseMode.On }, { "mouseKey", Shortcuts.MouseText },
        });
    }

    // the controller mouse was turned on or off (from any place): refresh its switch
    public void MouseChanged() { SendState(); }

    void SendLevels()
    {
        if (!ready || !Visible) return;
        Dictionary<string, object> lv = new Dictionary<string, object>();
        foreach (Audio.Endpoint ep in outs) { try { lv[ep.Id] = Math.Round(Meters.Level(ep.Id), 3); } catch { } }
        foreach (Audio.Endpoint ep in mics) { try { lv[ep.Id] = Math.Round(Meters.Level(ep.Id), 3); } catch { } }
        Post(new Dictionary<string, object> { { "type", "levels" }, { "levels", lv } });
    }
}

// ---------------------------------------------------------------- app
class XdeckApp : Form
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);

    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    const int WM_HOTKEY = 0x0312;
    readonly Banner banner = new Banner();
    readonly WebPanel panel = new WebPanel();
    readonly NotifyIcon tray = new NotifyIcon();
    ToolStripMenuItem mouseItem;

    public XdeckApp()
    {
        // shortcuts.ini first, so the hotkeys registered with the window handle are the ones it names
        ShortcutsFile.EnsureExists();
        try { startupErrors = ShortcutsFile.Load(); } catch { }
        WatchShortcuts();

        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;

        tray.Icon = MakeTrayIcon();
        tray.Text = "Xdeck";
        tray.Visible = true;
        tray.MouseUp += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) panel.Toggle(); };
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("Open panel", null, delegate { panel.ShowPanel(); });
        menu.Items.Add("Next audio output", null, delegate { Cycle(1); });
        menu.Items.Add("Previous audio output", null, delegate { Cycle(-1); });
        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem autostart = new ToolStripMenuItem("Start with Windows");
        autostart.Checked = Autostart.IsOn();
        if (Autostart.ForAllUsers()) { autostart.Text = "Start with Windows (all users)"; autostart.Enabled = false; }
        else autostart.Click += delegate { Autostart.Set(!Autostart.IsOn()); autostart.Checked = Autostart.IsOn(); };
        menu.Items.Add(autostart);
        mouseItem = new ToolStripMenuItem("Controller as mouse");
        mouseItem.Click += delegate { MouseMode.Toggle(); };
        menu.Items.Add(mouseItem);
        menu.Items.Add("Edit shortcuts…", null, delegate { EditShortcuts(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Xdeck", null, delegate { tray.Visible = false; Application.Exit(); });
        tray.ContextMenuStrip = menu;
        panel.Prewarm();

        // keep the gear on the taskbar instead of under the ^ arrow (first run per user)
        System.Windows.Forms.Timer promote = new System.Windows.Forms.Timer();
        int promoteTries = 0;
        promote.Interval = 2500;
        promote.Tick += delegate { TrayPromote.Once(); if (++promoteTries >= 6) promote.Stop(); };
        promote.Start();

        // Controller combo -> audio switch, on the UI thread.
        Combo.Fire = delegate (int dir)
        {
            if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { Cycle(dir); });
        };
        // Controller panel shortcut (L1 + right + Triangle) -> open / hide the panel; navigation while open.
        PadHooks.Toggle = delegate
        {
            if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { panel.TogglePad(); });
        };
        PadHooks.Nav = delegate (string what)
        {
            if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { panel.Nav(what); });
        };
        // Controller as mouse: input goes straight out from the controller threads; the banner, the
        // tray check and the panel switch follow on the UI thread.
        PadMouse.Move = Inject.Move;
        PadMouse.Scroll = Inject.Scroll;
        PadMouse.Click = Inject.Click;
        PadMouse.Key = Inject.Key;
        MouseMode.Changed = delegate (bool on)
        {
            if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate
            {
                mouseItem.Checked = on;
                banner.Show("Controller mouse", on ? "On" : "Off", "pad", 0, 0);
                panel.MouseChanged();
            });
        };
        XInputPoller.Start();
        System.Windows.Forms.Timer scan = new System.Windows.Forms.Timer();
        scan.Interval = 3000;
        scan.Tick += delegate { try { Pads.RefreshReaders(); UpdateTrayText(); } catch { } };
        scan.Start();
        try { Pads.RefreshReaders(); } catch { }

        // Banner when an audio output or a microphone is plugged in / turned on.
        knownOut = Ids(false);
        knownMic = Ids(true);
        System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer();
        watch.Interval = 2000;
        watch.Tick += delegate { WatchAudio(); };
        watch.Start();

        // "Xdeck.exe --panel" from another process opens the panel here.
        Thread w = new Thread(delegate ()
        {
            using (EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, "Xdeck_show_panel"))
                while (true)
                {
                    ev.WaitOne();
                    if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { panel.Toggle(); });
                }
        });
        w.IsBackground = true;
        w.Start();
    }

    // Tray glyph: an 8-tooth gear with a round hole.
    static void Gear(Graphics g, float cx, float cy, float r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        const int teeth = 8;
        float rOut = r, rIn = r * 0.74f;
        List<PointF> pts = new List<PointF>();
        for (int i = 0; i < teeth; i++)
        {
            double a = i * 2 * Math.PI / teeth;
            double half = Math.PI / teeth;                  // one tooth + one gap per step
            double[] angles = { a - half * 0.55, a - half * 0.38, a + half * 0.38, a + half * 0.55 };
            float[] radii = { rIn, rOut, rOut, rIn };
            for (int k = 0; k < 4; k++)
                pts.Add(new PointF(cx + (float)(Math.Cos(angles[k]) * radii[k]), cy + (float)(Math.Sin(angles[k]) * radii[k])));
            double gapA = a + half;                         // round the gap floor
            pts.Add(new PointF(cx + (float)(Math.Cos(gapA) * rIn * 0.98), cy + (float)(Math.Sin(gapA) * rIn * 0.98)));
        }
        using (GraphicsPath p = new GraphicsPath(FillMode.Alternate))
        {
            p.AddPolygon(pts.ToArray());
            float h = r * 0.34f;
            p.AddEllipse(cx - h, cy - h, 2 * h, 2 * h);     // alternate fill leaves the hole empty
            using (Brush b = new SolidBrush(c)) g.FillPath(b, p);
        }
    }

    static Icon MakeTrayIcon()
    {
        using (Bitmap bmp = new Bitmap(32, 32))
        {
            using (Graphics g = Graphics.FromImage(bmp))
                Gear(g, 16, 16, 15f, Color.White);
            IntPtr h = bmp.GetHicon();
            Icon icon = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            return icon;
        }
    }

    protected override void SetVisibleCore(bool value)
    {
        if (!IsHandleCreated) CreateHandle();
        base.SetVisibleCore(false);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        List<string> problems = startupErrors ?? new List<string>();
        problems.AddRange(RegisterKeys());
        if (problems.Count > 0) ShowShortcutProblems(problems);
    }

    List<string> startupErrors;

    // The keyboard shortcuts from shortcuts.ini; returns the ones another app already holds.
    List<string> RegisterKeys()
    {
        UnregisterHotKey(Handle, 1);
        UnregisterHotKey(Handle, 2);
        List<string> failed = new List<string>();
        string[] texts = { ShortcutsFile.NextKey, ShortcutsFile.PrevKey };
        for (int i = 0; i < 2; i++)
        {
            uint mods, vk;
            string err;
            if (!Shortcuts.ParseKey(texts[i], out mods, out vk, out err) || vk == 0) continue;
            if (!RegisterHotKey(Handle, i + 1, mods | MOD_NOREPEAT, vk))
                failed.Add("another app is already using " + texts[i]);
        }
        return failed;
    }

    void ShowShortcutProblems(List<string> problems)
    {
        string text = string.Join("\n", problems.ToArray());
        if (text.Length > 240) text = text.Substring(0, 239) + "…";
        tray.ShowBalloonTip(8000, "Xdeck shortcuts", text, ToolTipIcon.Warning);
    }

    void EditShortcuts()
    {
        ShortcutsFile.EnsureExists();
        try { System.Diagnostics.Process.Start("notepad.exe", "\"" + AppPaths.ShortcutFile + "\""); } catch { }
    }

    // Editors save in bursts (truncate, write, rename), so wait for a quiet moment, then read; while
    // the editor still holds the file, try again shortly.
    FileSystemWatcher shortcutWatch;
    System.Windows.Forms.Timer shortcutDelay;

    void WatchShortcuts()
    {
        shortcutDelay = new System.Windows.Forms.Timer();
        shortcutDelay.Interval = 400;
        shortcutDelay.Tick += delegate { ReloadShortcuts(); };
        try
        {
            shortcutWatch = new FileSystemWatcher(AppPaths.Data, "shortcuts.ini");
            shortcutWatch.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
            FileSystemEventHandler changed = delegate
            {
                if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { shortcutDelay.Stop(); shortcutDelay.Start(); });
            };
            shortcutWatch.Changed += changed;
            shortcutWatch.Created += changed;
            shortcutWatch.Renamed += delegate (object s, RenamedEventArgs e) { changed(s, e); };
            shortcutWatch.EnableRaisingEvents = true;
        }
        catch { }
    }

    void ReloadShortcuts()
    {
        shortcutDelay.Stop();
        List<string> problems;
        try { problems = ShortcutsFile.Load(); }
        catch (IOException) { shortcutDelay.Start(); return; }
        catch (UnauthorizedAccessException) { shortcutDelay.Start(); return; }
        problems.AddRange(RegisterKeys());
        panel.ShortcutsChanged();
        if (problems.Count > 0) ShowShortcutProblems(problems);
        else
        {
            string p = Shortcuts.PanelText.Length > 0 ? Shortcuts.PanelText : "off";
            tray.ShowBalloonTip(3000, "Xdeck shortcuts updated", "Panel: " + p, ToolTipIcon.Info);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, 1);
        UnregisterHotKey(Handle, 2);
        tray.Visible = false;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            Cycle(m.WParam.ToInt32() == 1 ? 1 : -1);
            return;
        }
        base.WndProc(ref m);
    }

    // Tray tooltip: battery of each wireless controller, with a bolt while charging (max 63 chars).
    void UpdateTrayText()
    {
        List<string> parts = new List<string>();
        foreach (Pad p in Pads.List())
            if (p.Conn != "USB" && p.Battery >= 0)
                parts.Add(p.Name + " " + p.Battery + "%" + (p.Charging ? " ⚡" : ""));
        string text = parts.Count == 0 ? "Xdeck" : string.Join(" · ", parts.ToArray());
        tray.Text = text.Length > 63 ? text.Substring(0, 62) + "…" : text;
    }

    HashSet<string> knownOut = new HashSet<string>(), knownMic = new HashSet<string>();

    static HashSet<string> Ids(bool capture)
    {
        HashSet<string> s = new HashSet<string>();
        try { foreach (Audio.Endpoint ep in Audio.Active(capture)) s.Add(ep.Id); } catch { }
        return s;
    }

    void WatchAudio()
    {
        HashSet<string> outs = Ids(false), mics = Ids(true);
        bool changed = !outs.SetEquals(knownOut) || !mics.SetEquals(knownMic);
        foreach (string id in mics)
            if (!knownMic.Contains(id)) { banner.Show("Microphone connected", Audio.Name(id), "mic", 0, 0); break; }
        foreach (string id in outs)
            if (!knownOut.Contains(id)) { string n = Audio.Name(id); banner.Show("Audio output connected", n, Audio.Glyph(n), 0, 0); break; }
        knownOut = outs;
        knownMic = mics;
        if (changed && panel.Visible) panel.Reload();
    }

    // Every enabled, connected output — devices.txt only sets the order, so this works on any PC.
    List<string> ActiveDevices()
    {
        return Audio.Active(false).ConvertAll(delegate (Audio.Endpoint ep) { return ep.Id; });
    }

    void Cycle(int dir)
    {
        try
        {
            List<string> active = ActiveDevices();
            if (active.Count == 0) { banner.ShowDevice("No output connected", 0, 0); return; }
            int idx = active.IndexOf(Audio.DefaultId() ?? "");
            int next = idx < 0 ? (dir > 0 ? 0 : active.Count - 1)
                               : ((idx + dir) % active.Count + active.Count) % active.Count;
            Audio.SetDefault(active[next]);
            banner.ShowDevice(Audio.Name(active[next]), next, active.Count);
            if (panel.Visible) panel.Reload();
        }
        catch (Exception ex) { banner.ShowDevice("Error: " + ex.Message, 0, 0); }
    }
}

// "Start with Windows": a shortcut to this exe in the user's Startup folder (the user turns it on from
// the tray). An install for all users puts one in the common Startup folder instead; that one is
// managed by install.ps1, so the tray item just shows it as on.
static class Autostart
{
    static string Name { get { return Path.GetFileNameWithoutExtension(Application.ExecutablePath) + ".lnk"; } }
    static string Link { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), Name); } }
    static string CommonLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Name); } }

    public static bool ForAllUsers() { return File.Exists(CommonLink); }
    public static bool IsOn() { return File.Exists(Link) || ForAllUsers(); }

    public static void Set(bool on)
    {
        try
        {
            if (!on) { if (File.Exists(Link)) File.Delete(Link); return; }
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { Link });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { Application.ExecutablePath });
            lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(Application.ExecutablePath) });
            lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
        }
        catch (Exception ex) { MessageBox.Show("Could not change Start with Windows: " + ex.Message, "Xdeck"); }
    }
}

static class Program
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    // "Xdeck.exe --icons <file.png>" renders every icon (banner size and 4x) for a visual check.
    static void IconSheet(string file)
    {
        string[] kinds = { "pad", "monitor", "tv", "headphones", "speaker", "mic" };
        Color accent = Ui.Accent;
        Color ink = accent.GetBrightness() < 0.35f ? Color.White : accent;
        using (Bitmap bmp = new Bitmap(kinds.Length * 120 + 20, 220))
        using (Graphics g = Graphics.FromImage(bmp))
        {
            Ui.Smooth(g);
            g.Clear(Ui.Back);
            for (int i = 0; i < kinds.Length; i++)
            {
                float x = 20 + i * 120;
                RectangleF disc = new RectangleF(x + 26, 16, 48, 48);
                using (Brush a = new SolidBrush(Color.FromArgb(48, accent))) g.FillEllipse(a, disc);
                Ui.Icon(g, kinds[i], new RectangleF(disc.X + 12, disc.Y + 12, 24, 24), ink);
                RectangleF big = new RectangleF(x, 84, 100, 100);
                using (Pen guide = new Pen(Color.FromArgb(70, 70, 76))) { g.DrawRectangle(guide, big.X, big.Y, big.Width, big.Height); g.DrawLine(guide, big.X + 50, big.Y, big.X + 50, big.Bottom); g.DrawLine(guide, big.X, big.Y + 50, big.Right, big.Y + 50); }
                Ui.Icon(g, kinds[i], new RectangleF(big.X + 14, big.Y + 14, 72, 72), ink);
                using (Font f = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
                using (Brush b = new SolidBrush(Ui.Dim))
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center })
                    g.DrawString(kinds[i], f, b, new RectangleF(x, 192, 100, 20), sf);
            }
            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    [STAThread]   // must sit directly on Main: WebView2 and the audio COM objects need an STA thread
    static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--icons") { SetProcessDPIAware(); IconSheet(args[1]); return; }
        bool created;
        using (Mutex mutex = new Mutex(true, "Xdeck_single_instance", out created))
        {
            if (!created)
            {
                if (Array.IndexOf(args, "--panel") >= 0)
                {
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting("Xdeck_show_panel", out ev)) using (ev) ev.Set();
                }
                return;
            }
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.Run(new XdeckApp());
        }
    }
}
