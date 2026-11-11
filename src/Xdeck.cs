// Xdeck: audio output, microphone and game controllers from the tray.
//  - Native host (this file): tray icon, hotkeys, controller input and combo, audio endpoints
//    (default device, volume, mute, level meters), controller battery, the banner, and the
//    WebView2 window that shows ui/ (panel + controller tester).
//  - Ctrl+Alt+F11 / Ctrl+Alt+F10, or hold D-pad right + L1 and flick the right stick, switch the
//    playback device forward / back; Data\devices.txt optionally sets the order.
//  - L1 + D-pad right + Triangle opens / hides the panel, which the controller then drives.
//  - A banner shows each device that connects (controller, Bluetooth, USB) with its battery, and
//    warns when one runs low.
//  - obs.* controller shortcuts drive OBS Studio (record, stream, scenes, ...) over its WebSocket.
//  - Every shortcut above can be changed in shortcuts.ini (read again on save, no new build).
// Build with ..\build.ps1 (C# 5 / .NET Framework 4.8: no string interpolation, no ?. operator).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
" + MouseSection + ObsSection;

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

    // Added to a file written by an older Xdeck that has no OBS lines yet.
    const string ObsSection =
@"
# OBS Studio from the controller / OBS Studio pelo controle.
# In OBS: Tools > WebSocket Server Settings > Enable WebSocket server. If it shows a password there
# (Show Connect Info), copy it to obs.password. Xdeck connects when you first press an OBS shortcut,
# and a banner says what OBS did.
#   obs.record       start / stop recording            obs.pause       pause / resume the recording
#   obs.stream       start / stop streaming            obs.replay      save the replay buffer
#   obs.virtualcam   start / stop the virtual camera
#   obs.scene.next / obs.scene.previous                the next / previous scene of OBS's list
#   obs.scene.<scene name>                             go to that scene,           e.g. obs.scene.Gameplay = R1 + Up
#   obs.mute.<audio source name>                       mute / unmute that source,  e.g. obs.mute.Mic/Aux = R1 + Down
#   obs.hotkey.<OBS hotkey name>                       press one of OBS's own hotkeys, e.g. obs.hotkey.OBSBasic.Screenshot = R1 + Share
obs.address = 127.0.0.1:4455
obs.password =

obs.record = R1 + Right + Cross
obs.pause = R1 + Right + Square
obs.stream = R1 + Right + Triangle
obs.replay = R1 + Right + Circle
obs.scene.next = R1 + R3
obs.scene.previous = R1 + L3
";

    public static void EnsureExists()
    {
        try
        {
            string path = AppPaths.ShortcutFile;
            if (!File.Exists(path)) { File.WriteAllText(path, Template, new UTF8Encoding(false)); return; }
            string text = File.ReadAllText(path, Encoding.UTF8);
            string add = "";
            if (!Regex.IsMatch(text, @"^\s*mouse\s*=", RegexOptions.Multiline)) add += MouseSection;
            if (!Regex.IsMatch(text, @"^\s*obs\.", RegexOptions.Multiline | RegexOptions.IgnoreCase)) add += ObsSection;
            if (add.Length > 0) File.AppendAllText(path, (text.EndsWith("\n") ? "" : "\r\n") + add, new UTF8Encoding(false));
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
        List<ObsBinding> obs = new List<ObsBinding>();
        string obsAddress = Obs.Address, obsPassword = Obs.Password;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
            string where = "line " + (i + 1) + ": ";
            int eq = line.IndexOf('=');
            if (eq < 0) { errors.Add(where + "no \"=\""); continue; }
            string rawKey = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
            string key = rawKey.ToLowerInvariant();
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
            else if (key == "obs.address")
            {
                if (value.Length == 0 || value.Contains(" ")) { errors.Add(where + "obs.address takes host:port, e.g. 127.0.0.1:4455"); continue; }
                obsAddress = value;
            }
            else if (key == "obs.password") obsPassword = value;
            else if (key.StartsWith("obs."))
            {
                ObsBinding b = ObsBinding.FromKey(rawKey, out err);
                if (b != null) b.Steps = Shortcuts.ParsePad(value, out err);
                if (b == null || b.Steps == null)
                {
                    errors.Add(where + err);
                    foreach (ObsBinding old in Shortcuts.Obs) if (old.Key == key) { obs.Add(old); break; }   // keeps the one in use
                    continue;
                }
                obs.RemoveAll(delegate (ObsBinding o) { return o.Key == key; });
                if (b.Steps.Length > 0) obs.Add(b);
            }
            else errors.Add(where + "unknown setting \"" + key + "\"");
        }
        Shortcuts.SetPad(panel, panelText, audio, mouse, mouseText, mouseSpeed, obs.ToArray());
        Obs.Address = obsAddress;
        Obs.Password = obsPassword;
        return errors;
    }
}

// ---------------------------------------------------------------- self-update
// Like the other Skale apps, Xdeck updates itself from its GitHub releases. A minute after start and
// every six hours it reads latest.json from the latest release; when that version is newer, it
// downloads the release zip in the background and keeps it only if it matches its signature
// (RSA SHA-256, made by build.ps1 -Release with the Xdeck update key; the public half is below).
// When it is ready, the tray and the panel offer a restart, and quitting Xdeck installs it too
// when its folder is writable.
// Installing: the zip is checked again and unpacked to %TEMP%, and the new Xdeck.exe runs from
// there with --apply-update: it waits for this copy to exit, copies the new files over this
// folder (never Data\, so settings stay) and starts Xdeck again through Explorer, as the user.
// In Program Files only an administrator can write, so that helper asks for approval once.
static class Updater
{
    public const string DefaultFeed = "https://github.com/Skale-Club/xdeck/releases/latest/download/latest.json";
    const string PublicKey = "<RSAKeyValue><Modulus>sY6SX0bMNUynsEozcmBJZ66U4HPwcbeSo7RN0yapJ6bk83bceczM7x4L1e5fRP2cDajE9JLs0DmRBWpwVLcgQS6MgwLfk4X0/0fIvxrANcUlVJWQ3IGSRAbB/D1kaD53U6OKfdi7Fjw7KPop6k/DOUAZRSmGQNN62RTzZ3tI3NHedinea8VnudV2HATU9v4XDENhbSNlCrjSC9ed8Geeas2HxfVVUYskCqDNYUjDsNxIcgMetbr0ZE0YYNW2xqwjQB2Y4veNkuru5/SLl5TBJKTaKR6Yo3N0yVps10yYe3W/L279CnIiFNy0WGlxl1VwqN3cb13pdn00qEKphLKA59pL1JEqogX7icd4CMfNU1rjjE68A7qn0VnEb3N0q9c1704wVnVSPRxO5fLAsZlxbbxmCh2ozlGtvSeonFS470Jvacl5ckc4sAjSrzUg9zD24zun7F0PA9NrzlkJDyvEXrRRW/GB0SgaIeZjL02lJ74ygPn0zYnnbDz1PHppwykN</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";
    const long MaxDownload = 100L * 1024 * 1024;
    const string Agent = "skale-x-updater";
    static readonly TimeSpan Every = TimeSpan.FromHours(6);

    public enum State { Idle, Checking, UpToDate, Downloading, Ready, Failed }
    public static volatile State Now = State.Idle;
    public static volatile string Version;  // the newer release being downloaded or ready
    public static volatile string Error;    // why the last check failed
    public static Action Changed;           // set by the app; called from the updater thread
    static int busy;

    // This build, from the assembly version build.ps1 stamps from VERSION.
    public static string Current
    {
        get { System.Version v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
    }

    // XDECK_UPDATE_FEED points a test copy at another feed (plain http only on this PC).
    static string Feed
    {
        get
        {
            string f = Environment.GetEnvironmentVariable("XDECK_UPDATE_FEED");
            return string.IsNullOrEmpty(f) ? DefaultFeed : f;
        }
    }

    static string Root { get { return Path.Combine(AppPaths.Data, "update"); } }
    static string ReadyFile { get { return Path.Combine(Root, "ready.json"); } }
    static string ZipOf(string version) { return Path.Combine(Root, "Xdeck-" + version + ".zip"); }

    public static void Start()
    {
        Cleanup();
        Dictionary<string, object> ready = ReadReady();
        if (ready != null) { Version = (string)ready["version"]; Now = State.Ready; }
        Thread t = new Thread(delegate ()
        {
            Thread.Sleep(Environment.GetEnvironmentVariable("XDECK_UPDATE_FEED") != null ? 2000 : 60000);
            while (true) { Check(); Thread.Sleep(Every); }
        });
        t.IsBackground = true;
        t.Start();
    }

    public static void CheckNow()
    {
        Thread t = new Thread(Check);
        t.IsBackground = true;
        t.Start();
    }

    static void Set(State s) { Now = s; Log("state " + s + (Version != null ? " " + Version : "")); Action c = Changed; if (c != null) c(); }

    // Data\update.log: what the updater did and why it stopped (kept under 200 KB).
    public static void Log(string line)
    {
        try
        {
            string f = Path.Combine(AppPaths.Data, "update.log");
            if (File.Exists(f) && new FileInfo(f).Length > 200 * 1024) File.Delete(f);
            File.AppendAllText(f, DateTime.Now.ToString("s") + " [" + Current + " pid " + Process.GetCurrentProcess().Id + "] " + line + "\r\n");
        }
        catch { }
    }

    static void Check()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;
        try
        {
            bool wasReady = Now == State.Ready;
            if (!wasReady) Set(State.Checking);
            try
            {
                Dictionary<string, object> m = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                    Encoding.UTF8.GetString(Get(Feed, 1024 * 1024)));
                string version = ((string)m["version"]).TrimStart('v');
                string url = (string)m["url"], signature = (string)m["signature"];
                if (Parse(version) == null) throw new Exception("the update feed has no valid version");
                if (Newer(version, Current) <= 0) { Error = null; if (!wasReady) Set(State.UpToDate); return; }
                if (wasReady && version == Version) { Error = null; Set(State.Ready); return; }
                Version = version;
                Set(State.Downloading);
                byte[] zip = Get(url, MaxDownload);
                if (!Verify(zip, signature)) throw new Exception("the download does not match its signature");
                Directory.CreateDirectory(Root);
                foreach (string old in Directory.GetFiles(Root, "Xdeck-*.zip")) TryDelete(old);
                File.WriteAllBytes(ZipOf(version) + ".download", zip);
                File.Move(ZipOf(version) + ".download", ZipOf(version));
                string notes = m.ContainsKey("notes") ? (string)m["notes"] : "";
                File.WriteAllText(ReadyFile, new JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                    { "version", version }, { "signature", signature }, { "notes", notes } }));
                Error = null;
                Set(State.Ready);
            }
            catch (Exception ex)
            {
                Error = ex is WebException && ((WebException)ex).Response is HttpWebResponse &&
                        ((HttpWebResponse)((WebException)ex).Response).StatusCode == HttpStatusCode.NotFound
                        ? "no update feed yet" : ex.Message;
                Log("check failed: " + ex);
                Dictionary<string, object> ready = ReadReady();   // a download made earlier is still good
                if (ready != null) { Version = (string)ready["version"]; Set(State.Ready); }
                else Set(State.Failed);
            }
        }
        finally { Interlocked.Exchange(ref busy, 0); }
    }

    static byte[] Get(string url, long max)
    {
        Uri u = new Uri(url);
        // a test feed (XDECK_UPDATE_FEED) may also be a file or a server on this PC
        bool local = (u.IsFile || u.IsLoopback) && Environment.GetEnvironmentVariable("XDECK_UPDATE_FEED") != null;
        if (u.Scheme != Uri.UriSchemeHttps && !local) throw new Exception("updates come only over HTTPS");
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;   // + TLS 1.3
        WebRequest r = WebRequest.Create(u);
        r.Timeout = 20000;
        HttpWebRequest h = r as HttpWebRequest;
        if (h != null) { h.UserAgent = Agent; h.ReadWriteTimeout = 120000; }
        using (WebResponse resp = r.GetResponse())
        using (Stream s = resp.GetResponseStream())
        using (MemoryStream m = new MemoryStream())
        {
            byte[] buf = new byte[81920];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                m.Write(buf, 0, n);
                if (m.Length > max) throw new Exception("the download is too large");
            }
            return m.ToArray();
        }
    }

    public static bool Verify(byte[] data, string signature)
    {
        try
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(PublicKey);
                return rsa.VerifyData(data, "SHA256", Convert.FromBase64String(signature));
            }
        }
        catch { return false; }
    }

    // "v1.2.3" / "1.2.3" -> {1, 2, 3}; null for anything else (no pre-releases).
    public static int[] Parse(string v)
    {
        string[] p = (v ?? "").Trim().TrimStart('v').Split('.');
        if (p.Length != 3) return null;
        int[] n = new int[3];
        for (int i = 0; i < 3; i++) if (p[i].Length == 0 || !int.TryParse(p[i], out n[i]) || n[i] < 0 || p[i].TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length > 0) return null;
        return n;
    }

    // > 0 when a is newer than b
    public static int Newer(string a, string b)
    {
        int[] x = Parse(a), y = Parse(b);
        if (x == null || y == null) return 0;
        for (int i = 0; i < 3; i++) if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        return 0;
    }

    // The update waiting to be installed, checked again against its signature; null if none.
    static Dictionary<string, object> ReadReady()
    {
        try
        {
            Dictionary<string, object> r = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(ReadyFile));
            string v = (string)r["version"];
            if (Newer(v, Current) <= 0) return null;   // already installed
            if (!File.Exists(ZipOf(v))) { Log("ready " + v + ": its zip is missing"); return null; }
            if (new FileInfo(ZipOf(v)).Length > MaxDownload || !Verify(File.ReadAllBytes(ZipOf(v)), (string)r["signature"]))
            { Log("ready " + v + ": the zip does not match its signature"); return null; }
            return r;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception ex) { Log("ready: " + ex); return null; }
    }

    // After an update: the downloads and the files an update moved aside are no longer needed.
    static void Cleanup()
    {
        try { if (ReadReady() == null && Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
        try { foreach (string f in Directory.GetFiles(AppPaths.Exe, "*.old-xdeck", SearchOption.AllDirectories)) TryDelete(f); } catch { }
        try
        {
            foreach (string d in Directory.GetDirectories(Path.GetTempPath(), "xdeck-update-*"))
                if (Directory.GetCreationTime(d) < DateTime.Now.AddMinutes(-10)) Directory.Delete(d, true);
        }
        catch { }
    }

    static void TryDelete(string f) { try { File.Delete(f); } catch { } }

    public static bool FolderWritable(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".xdeck-write-test-" + Process.GetCurrentProcess().Id);
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    // Starts installing the ready update; the caller must exit right after a true. False with an
    // error when there is none, or the administrator prompt was turned down.
    public static bool Install(bool relaunch, out string error)
    {
        error = null;
        try
        {
            Dictionary<string, object> ready = ReadReady();
            Log("install requested (relaunch " + relaunch + ")");
            if (ready == null) { error = "no update is ready"; Log("install: " + error); return false; }
            string version = (string)ready["version"];
            string tmp = Path.Combine(Path.GetTempPath(), "xdeck-update-" + version + "-" + DateTime.Now.Ticks);
            using (ZipArchive zip = ZipFile.OpenRead(ZipOf(version)))
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    // the zip holds Xdeck\...; skip its portable Data folder and folder entries
                    string name = e.FullName.Replace('/', '\\');
                    int cut = name.IndexOf('\\');
                    if (cut < 0 || e.Name.Length == 0) continue;
                    name = name.Substring(cut + 1);
                    if (name.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase) || name.Contains("..")) continue;
                    string dest = Path.Combine(tmp, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            string helper = Path.Combine(tmp, "Xdeck.exe");
            if (!File.Exists(helper)) { error = "the update has no Xdeck.exe"; return false; }
            ProcessStartInfo psi = new ProcessStartInfo(helper,
                "--apply-update \"" + AppPaths.Exe + "\" " + Process.GetCurrentProcess().Id + (relaunch ? " --relaunch" : ""));
            psi.UseShellExecute = true;
            psi.WorkingDirectory = tmp;
            if (!FolderWritable(AppPaths.Exe)) psi.Verb = "runas";   // Program Files: one administrator prompt
            Log("install: starting " + helper + (psi.Verb == "runas" ? " as administrator" : ""));
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex) { error = "the administrator prompt was turned down"; Log("install: " + ex); return false; }
        catch (Exception ex) { error = ex.Message; Log("install failed: " + ex); return false; }
    }

    // --apply-update <target> <pid> [--relaunch], run from the unpacked update: wait for the old copy
    // to exit, copy this folder over the target (Xdeck.exe last), then start it again as the user.
    public static int ApplyFromHere(string target, int pid, bool relaunch)
    {
        string log = Path.Combine(Path.GetTempPath(), "xdeck-update.log");
        Action<string> say = delegate (string line) { try { File.AppendAllText(log, DateTime.Now.ToString("s") + " " + line + "\r\n"); } catch { } };
        try
        {
            try { using (Process p = Process.GetProcessById(pid)) p.WaitForExit(20000); } catch (ArgumentException) { }
            string src = Path.GetDirectoryName(Application.ExecutablePath);
            say("applying " + src + " -> " + target);
            List<string> files = new List<string>(Directory.GetFiles(src, "*", SearchOption.AllDirectories));
            string self = Path.Combine(src, "Xdeck.exe");
            files.Remove(self);
            files.Add(self);
            int failed = 0;
            foreach (string f in files)
            {
                string dest = Path.Combine(target, f.Substring(src.Length).TrimStart('\\'));
                try { CopyOver(f, dest); }
                catch (Exception ex) { failed++; say("failed " + dest + ": " + ex.Message); }
            }
            say(failed == 0 ? "done" : failed + " files failed");
            // Settings › Apps shows the new version (XdeckSetup.exe made that entry)
            foreach (RegistryHive hive in new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                    using (RegistryKey k = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Xdeck", true))
                        if (k != null && string.Equals(((k.GetValue("InstallLocation") as string) ?? "").TrimEnd('\\'),
                                                       target.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                            k.SetValue("DisplayVersion", Current);
                }
                catch { }
            if (relaunch) Process.Start("explorer.exe", "\"" + Path.Combine(target, "Xdeck.exe") + "\"");
            return failed == 0 ? 0 : 2;
        }
        catch (Exception ex) { say("error: " + ex.Message); return 1; }
    }

    // A file another copy still has open (another Windows user's Xdeck) can be renamed but not
    // overwritten: move it aside as .old-xdeck, which the next start deletes.
    static void CopyOver(string src, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest));
        try { File.Copy(src, dest, true); return; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        string aside = dest + ".old-xdeck";
        if (File.Exists(aside)) { try { File.Delete(aside); } catch { aside = dest + "." + DateTime.Now.Ticks + ".old-xdeck"; } }
        File.Move(dest, aside);
        File.Copy(src, dest, true);
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

// Anything Xdeck announces when it connects: a controller, a Bluetooth device or a USB device.
class Device
{
    public string Key;          // stable: the controller's / Bluetooth address, the USB instance ID
    public string Name;
    public string Glyph = "usb";    // banner icon
    public string Conn = "USB";     // USB | Bluetooth
    public int Battery = -1;    // percent, -1 unknown or none
    public bool Charging, Connected, Pad;

    public static Device Of(Pad p)
    {
        return new Device { Key = p.Mac, Name = p.Name, Glyph = "pad", Conn = p.Conn, Battery = p.Battery, Charging = p.Charging, Connected = true, Pad = true };
    }
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

    // ------------------------------------------------------------ other devices
    // Everything else Xdeck announces when it connects: Bluetooth devices that are not controllers
    // (headphones, mice, keyboards, phones), and USB devices plugged into the PC.
    static readonly DEVPROPKEY KeyDesc = new DEVPROPKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 2 };
    static readonly DEVPROPKEY KeyBusName = new DEVPROPKEY { fmtid = new Guid("540b947e-8b40-45bc-a8a2-6a0b894cbda2"), pid = 4 };
    static readonly DEVPROPKEY KeyInternal = new DEVPROPKEY { fmtid = new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), pid = 4 }; // InLocalMachineContainer
    static readonly Guid UsbDeviceInterface = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    static readonly IntPtr NoSet = new IntPtr(-1);

    static string Text(byte[] b) { return b == null ? "" : Encoding.Unicode.GetString(b).TrimEnd('\0').Trim(); }

    // Connected Bluetooth devices that are not controllers. The battery sits on the device node
    // (Bluetooth LE) or on one of its service nodes (a headset's hands-free profile), whose
    // instance IDs carry the device's address.
    public static List<Device> Bluetooth()
    {
        List<Device> devs = new List<Device>();
        List<KeyValuePair<string, int>> batteries = new List<KeyValuePair<string, int>>();
        foreach (string enumerator in new string[] { "BTHLE", "BTHENUM", "BTHLEDEVICE" })
        {
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, 0x4 | 0x2); // ALLCLASSES | PRESENT
            if (set == NoSet) continue;
            SP_DEVINFO_DATA d = new SP_DEVINFO_DATA();
            d.cbSize = Marshal.SizeOf(d);
            for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref d); i++)
            {
                StringBuilder sb = new StringBuilder(512); int req;
                if (!SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out req)) continue;
                string id = sb.ToString().ToUpperInvariant();
                byte[] bat = Prop(set, ref d, KeyBattery);
                int battery = bat != null && bat.Length > 0 && bat[0] <= 100 ? bat[0] : -1;
                Match m = Regex.Match(id, @"^BTH(LE|ENUM)\\DEV_([0-9A-F]{12})");
                if (!m.Success) { if (battery >= 0) batteries.Add(new KeyValuePair<string, int>(id, battery)); continue; }
                string name = Text(Prop(set, ref d, KeyFriendlyName));
                if (name.Length == 0 || PadName.IsMatch(name)) continue;
                byte[] con = Prop(set, ref d, KeyConnected);
                bool connected = con != null && con.Length > 0 && con[0] != 0;
                string key = "bt-" + m.Groups[2].Value;
                Device dv = devs.Find(delegate (Device x) { return x.Key == key; });   // a dual-mode device is in both lists
                if (dv == null) devs.Add(dv = new Device { Key = key, Name = name, Glyph = GlyphFor(name, "bt"), Conn = "Bluetooth" });
                dv.Connected |= connected;
                dv.Battery = Math.Max(dv.Battery, battery);
            }
            SetupDiDestroyDeviceInfoList(set);
        }
        devs = devs.FindAll(delegate (Device x) { return x.Connected; });
        foreach (Device dv in devs)
        {
            string mac = dv.Key.Substring(3);
            foreach (KeyValuePair<string, int> b in batteries)
                if (b.Key.Contains(mac)) dv.Battery = Math.Max(dv.Battery, b.Value);
        }
        return devs;
    }

    // USB devices plugged in from outside: not hubs, not what is built into the PC (webcam, card
    // reader, Bluetooth radio), not the wheel and handbrake (they have their own banner). The name is
    // the one the device gives itself ("SanDisk Ultra", "G435 Wireless Gaming Headset").
    public static List<Device> Usb()
    {
        List<Device> o = new List<Device>();
        Guid g = UsbDeviceInterface;
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);   // PRESENT | DEVICEINTERFACE
        if (set == NoSet) return o;
        SP_DEVINFO_DATA d = new SP_DEVINFO_DATA();
        d.cbSize = Marshal.SizeOf(d);
        for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref d); i++)
        {
            StringBuilder sb = new StringBuilder(512); int req;
            if (!SetupDiGetDeviceInstanceId(set, ref d, sb, sb.Capacity, out req)) continue;
            string id = sb.ToString().ToLowerInvariant();
            if (id.Contains("root_hub")) continue;
            bool wheel = false;
            foreach (string[] w in Wired) if (id.Contains(w[0])) wheel = true;
            if (wheel) continue;
            byte[] inside = Prop(set, ref d, KeyInternal);
            if (inside != null && inside.Length > 0 && inside[0] != 0) continue;
            string name = Text(Prop(set, ref d, KeyBusName));
            if (name.Length == 0) name = Text(Prop(set, ref d, KeyFriendlyName));
            if (name.Length == 0) name = Text(Prop(set, ref d, KeyDesc));
            if (name.Length == 0 || name.ToLowerInvariant().Contains("hub")) continue;
            o.Add(new Device { Key = "usb-" + id, Name = name, Glyph = GlyphFor(name, "usb"), Conn = "USB", Connected = true });
        }
        SetupDiDestroyDeviceInfoList(set);
        return o;
    }

    // The banner icon from the device's name.
    static string GlyphFor(string name, string fallback)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("controller") || n.Contains("gamepad") || n.Contains("joystick")) return "pad";
        if (n.Contains("mouse") || n.Contains("trackball") || n.Contains("mx master") || n.Contains("mx anywhere")) return "mouse";
        if (n.Contains("keyboard") || n.Contains("teclado") || n.Contains("mx keys")) return "keyboard";
        if (n.Contains("airpods") || n.Contains("buds") || n.Contains("head") || n.Contains("earphone") ||
            n.Contains("fone") || Regex.IsMatch(n, @"\b(wh|wf)-")) return "headphones";
        if (n.Contains("speaker") || n.Contains("soundbar") || n.Contains("caixa") || n.Contains("jbl")) return "speaker";
        if (Regex.IsMatch(n, @"\bmic\b") || n.Contains("microphone") || n.Contains("hollyland") || n.Contains("rode")) return "mic";
        return fallback;
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
    public static void Remove(PadState s)
    {
        lock (all) all.Remove(s);
        MouseMode.Set(s, false);           // a controller that goes away stops being a mouse
    }

    // The n-th (0-based) live controller of a kind, in a stable order.
    public static PadState Find(string kind, int n)
    {
        List<PadState> k = OfKind(kind);
        return n >= 0 && n < k.Count ? k[n] : null;
    }

    // Its position among the live controllers of its kind (the n Find takes), -1 if not live.
    public static int IndexOf(PadState s) { return s == null ? -1 : OfKind(s.Kind).IndexOf(s); }

    static List<PadState> OfKind(string kind)
    {
        List<PadState> k = new List<PadState>();
        lock (all) foreach (PadState s in all) if (s.Kind == kind) k.Add(s);
        k.Sort(delegate (PadState a, PadState b) { return string.Compare(a.Key, b.Key, StringComparison.Ordinal); });
        return k;
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
    public static volatile ObsBinding[] Obs = new ObsBinding[0];   // the obs.* lines (OBS Studio)
    // The buttons of these shortcuts that the panel does not use (L1 by default). While one is held
    // the controller is doing a shortcut, so the panel and the mouse mode ignore its D-pad and face
    // buttons.
    public static volatile int Guard = 1 << PadState.LB;

    public static void SetPad(int[] panel, string panelText, int audio, int[] mouse, string mouseText, int mouseSpeed, ObsBinding[] obs)
    {
        int all = audio;
        foreach (int m in panel) all |= m;
        foreach (int m in mouse) all |= m;
        foreach (ObsBinding b in obs) foreach (int m in b.Steps) all |= m;
        Obs = obs;
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
// Each controller is a mouse on its own: the mouse shortcut (L1 + D-pad right + Square by default)
// turns it on and off for the controller that pressed it, and the switch in a controller's page
// for that controller. While it is on and the panel is closed, that controller drives the pointer.
static class MouseMode
{
    static readonly HashSet<PadState> on = new HashSet<PadState>();
    public static Action<PadState, bool> Changed;   // set by the app; called from whichever thread changed it

    public static bool IsOn(PadState s)
    {
        if (s == null) return false;
        lock (on) return on.Contains(s);
    }

    public static void Set(PadState s, bool value)
    {
        if (s == null) return;
        bool changed;
        lock (on) changed = value ? on.Add(s) : on.Remove(s);
        if (!changed) return;
        Action<PadState, bool> c = Changed;
        if (c != null) c(s, value);
    }

    public static void Toggle(PadState s) { Set(s, !IsOn(s)); }
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
//  - the OBS shortcuts (obs.* in shortcuts.ini), handed to the OBS worker;
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
    ObsBinding[] obsFor = new ObsBinding[0];          // the bindings obsKeys follow (one tracker each)
    ShortcutTracker[] obsKeys = new ShortcutTracker[0];
    int prev;
    // Navigation that happens mid-sequence is held back, so a D-pad step of the sequence does not move
    // the focus or the volume; it is dropped if the sequence completes, sent if it breaks.
    readonly List<string> held = new List<string>();
    int dirHeld = -1;                      // PadState.Up/Down/Left/Right being held (D-pad or stick), -1 none
    DateTime dirSince, dirLast;
    DateTime bSince;
    bool bFired;

    bool InSequence
    {
        get
        {
            if (panelKey.Step > 0 || mouseKey.Step > 0) return true;
            foreach (ShortcutTracker t in obsKeys) if (t.Step > 0) return true;
            return false;
        }
    }

    public void Update(PadState s)
    {
        DateTime now = DateTime.Now;
        int bits = s.Bits, before = prev;
        int pressed = bits & ~before;      // rising edges
        prev = bits;

        if (combo.Update(s)) { panelKey.Step = mouseKey.Step = 0; foreach (ShortcutTracker t in obsKeys) t.Step = 0; held.Clear(); }

        // -------- shortcuts
        if (panelKey.Update(Shortcuts.Panel, bits, before, pressed, now))
        {
            held.Clear();
            Action t = Toggle;
            if (t != null) t();
        }
        if (mouseKey.Update(Shortcuts.Mouse, bits, before, pressed, now)) { held.Clear(); MouseMode.Toggle(s); }
        ObsBinding[] obs = Shortcuts.Obs;
        if (obs != obsFor)
        {
            obsFor = obs;
            obsKeys = new ShortcutTracker[obs.Length];
            for (int i = 0; i < obs.Length; i++) obsKeys[i] = new ShortcutTracker();
        }
        for (int i = 0; i < obs.Length; i++)
            if (obsKeys[i].Update(obs[i].Steps, bits, before, pressed, now)) { held.Clear(); Obs.Run(obs[i]); }
        if (!InSequence) Flush();          // a sequence broke or timed out: what it held back goes out

        mouse.Update(s, bits, pressed, !NavActive && MouseMode.IsOn(s), Shortcuts.Guard, now);

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

// ---------------------------------------------------------------- OBS Studio
// The obs.* controller shortcuts drive OBS Studio through its WebSocket server (obs-websocket 5,
// built into OBS 28 and later: Tools > WebSocket Server Settings). Nothing connects until an OBS
// shortcut is pressed; then one worker thread opens the connection, keeps it for the next press and
// sends the requests in turn. The banner says what OBS did, or why it did not.
class ObsBinding
{
    public string Key;      // the shortcuts.ini key in lower case: obs.record, obs.scene.gameplay, ...
    public string Action;   // record pause stream replay virtualcam scene.next scene.previous scene mute hotkey
    public string Target;   // the scene, audio source or OBS hotkey name, as written
    public int[] Steps;     // the controller shortcut

    static readonly string[] Simple = { "record", "pause", "stream", "replay", "virtualcam", "scene.next", "scene.previous" };
    static readonly string[] Named = { "scene", "mute", "hotkey" };

    // null + error when the key is no OBS action
    public static ObsBinding FromKey(string rawKey, out string error)
    {
        error = null;
        string key = rawKey.ToLowerInvariant(), rest = rawKey.Substring(4).Trim();   // after "obs."
        foreach (string a in Simple)
            if (key == "obs." + a) return new ObsBinding { Key = key, Action = a };
        foreach (string a in Named)
            if (key.StartsWith("obs." + a + "."))
            {
                string name = rest.Substring(a.Length + 1).Trim();
                if (name.Length > 0) return new ObsBinding { Key = key, Action = a, Target = name };
                error = "the " + a + " name is missing after \"obs." + a + ".\"";
                return null;
            }
        error = "unknown OBS setting \"" + key + "\"";
        return null;
    }
}

class ObsError : Exception
{
    public readonly int Code;   // obs-websocket request status code; 0 when OBS did not get that far
    public ObsError(string message, int code) : base(message) { Code = code; }
}

static class Obs
{
    public static volatile string Address = "127.0.0.1:4455", Password = "";
    public static Action<string, string, string, int, int> Said;   // caption, text, icon, index, count (worker thread)

    const int NotFound = 600;     // obs-websocket: ResourceNotFound
    static readonly Queue<ObsBinding> queue = new Queue<ObsBinding>();
    static readonly JavaScriptSerializer json = new JavaScriptSerializer();
    static Thread worker;
    static ClientWebSocket ws;
    static string wsFor;          // address + password the open connection was made with
    static int lastId;

    // From a controller thread: queued for the worker (a few at most, so a held combo cannot pile up).
    public static void Run(ObsBinding b)
    {
        lock (queue)
        {
            if (queue.Count >= 4) return;
            queue.Enqueue(b);
            Monitor.Pulse(queue);
            if (worker != null) return;
            worker = new Thread(Work);
            worker.IsBackground = true;
            worker.Start();
        }
    }

    static void Work()
    {
        while (true)
        {
            ObsBinding b;
            lock (queue)
            {
                while (queue.Count == 0) Monitor.Wait(queue);
                b = queue.Dequeue();
            }
            try { Do(b); }
            catch (ObsError e) { Say("OBS", e.Message, "rec"); }
            catch (Exception e) { Close(); Say("OBS", "Error: " + e.Message, "rec"); }
        }
    }

    static void Say(string caption, string text, string icon) { Say(caption, text, icon, 0, 0); }
    static void Say(string caption, string text, string icon, int index, int count)
    {
        Action<string, string, string, int, int> s = Said;
        if (s != null) s(caption, text, icon, index, count);
    }

    static void Do(ObsBinding b)
    {
        Dictionary<string, object> r;
        switch (b.Action)
        {
            case "record":
                if (Bool(Call("GetRecordStatus", null), "outputActive")) { Call("StopRecord", null); Say("OBS", "Recording stopped", "rec"); }
                else { Call("StartRecord", null); Say("OBS", "Recording started", "rec"); }
                break;
            case "pause":
                r = Call("GetRecordStatus", null);
                if (!Bool(r, "outputActive")) Say("OBS", "Not recording", "rec");
                else if (Bool(r, "outputPaused")) { Call("ResumeRecord", null); Say("OBS", "Recording resumed", "rec"); }
                else { Call("PauseRecord", null); Say("OBS", "Recording paused", "rec"); }
                break;
            case "stream":
                if (Bool(Call("GetStreamStatus", null), "outputActive")) { Call("StopStream", null); Say("OBS", "Stream stopped", "live"); }
                else { Call("StartStream", null); Say("OBS", "Stream started", "live"); }
                break;
            case "replay":
                if (!Active("GetReplayBufferStatus")) { Say("OBS", "Replay buffer is off", "rec"); break; }
                Call("SaveReplayBuffer", null);
                Say("OBS", "Replay saved", "rec");
                break;
            case "virtualcam":
                if (Active("GetVirtualCamStatus")) { Call("StopVirtualCam", null); Say("OBS", "Virtual camera off", "monitor"); }
                else { Call("StartVirtualCam", null); Say("OBS", "Virtual camera on", "monitor"); }
                break;
            case "scene.next":
            case "scene.previous":
            {
                r = Call("GetSceneList", null);
                List<string> names = Scenes(r);
                if (names.Count == 0) { Say("OBS", "No scenes", "monitor"); break; }
                int at = names.IndexOf(Str(r, "currentProgramSceneName"));
                int dir = b.Action == "scene.next" ? 1 : -1;
                int n = at < 0 ? 0 : ((at + dir) % names.Count + names.Count) % names.Count;
                Call("SetCurrentProgramScene", Args("sceneName", names[n]));
                Say("OBS scene", names[n], "monitor", n, names.Count);
                break;
            }
            case "scene":
                try { Call("SetCurrentProgramScene", Args("sceneName", b.Target)); }
                catch (ObsError e) { if (e.Code == NotFound) throw new ObsError("No scene named " + b.Target, e.Code); throw; }
                Say("OBS scene", b.Target, "monitor");
                break;
            case "mute":
                try { r = Call("ToggleInputMute", Args("inputName", b.Target)); }
                catch (ObsError e) { if (e.Code == NotFound) throw new ObsError("No source named " + b.Target, e.Code); throw; }
                Say("OBS", b.Target + (Bool(r, "inputMuted") ? ": muted" : ": on"), "mic");
                break;
            case "hotkey":
                try { Call("TriggerHotkeyByName", Args("hotkeyName", b.Target)); }
                catch (ObsError e) { if (e.Code == NotFound) throw new ObsError("No OBS hotkey named " + b.Target, e.Code); throw; }
                Say("OBS hotkey", b.Target, "rec");
                break;
        }
    }

    // An output's status; an output OBS does not have set up (replay buffer, virtual camera) counts as off.
    static bool Active(string statusRequest)
    {
        try { return Bool(Call(statusRequest, null), "outputActive"); }
        catch (ObsError e) { if (e.Code == 0) throw; return false; }
    }

    // Scene names in the order of OBS's Scenes list (top first). obs-websocket sends them bottom
    // first, each with its sceneIndex (0 = bottom).
    static List<string> Scenes(Dictionary<string, object> r)
    {
        List<KeyValuePair<int, string>> s = new List<KeyValuePair<int, string>>();
        object[] list = r != null && r.ContainsKey("scenes") ? r["scenes"] as object[] : null;
        if (list != null)
            for (int i = 0; i < list.Length; i++)
            {
                Dictionary<string, object> d = list[i] as Dictionary<string, object>;
                if (d == null) continue;
                s.Add(new KeyValuePair<int, string>(d.ContainsKey("sceneIndex") ? Int(d, "sceneIndex") : i, Str(d, "sceneName")));
            }
        s.Sort(delegate (KeyValuePair<int, string> a, KeyValuePair<int, string> b) { return b.Key.CompareTo(a.Key); });
        return s.ConvertAll(delegate (KeyValuePair<int, string> p) { return p.Value; });
    }

    static Dictionary<string, object> Args(string k, object v) { return new Dictionary<string, object> { { k, v } }; }

    // ------------------------------------------------ the connection
    // One request; its responseData (null when it has none). Throws ObsError when OBS refuses it or
    // cannot be reached. A kept connection that went stale (OBS was closed and opened again) is
    // opened again once, as long as the request had not reached OBS.
    static Dictionary<string, object> Call(string type, Dictionary<string, object> data)
    {
        for (int attempt = 0; ; attempt++)
        {
            string address = Address, password = Password, key = address + "\n" + password;
            bool fresh = false;
            if (ws == null || ws.State != WebSocketState.Open || wsFor != key) { Connect(address, password, key); fresh = true; }
            string id = (++lastId).ToString();
            Dictionary<string, object> d = null;
            try
            {
                Send(new Dictionary<string, object> { { "op", 6 }, { "d", new Dictionary<string, object> {
                    { "requestType", type }, { "requestId", id }, { "requestData", data ?? new Dictionary<string, object>() } } } });
                while (d == null)
                {
                    Dictionary<string, object> m = Receive(6000);
                    if (m == null) throw new IOException("OBS closed the connection");
                    Dictionary<string, object> md = m.ContainsKey("d") ? m["d"] as Dictionary<string, object> : null;
                    if (Int(m, "op") == 7 && md != null && Str(md, "requestId") == id) d = md;
                }
            }
            catch (TimeoutException) { Close(); throw new ObsError("OBS did not answer", 0); }
            catch (Exception)
            {
                Close();
                if (fresh || attempt > 0) throw new ObsError("Lost the connection to OBS", 0);
                continue;
            }
            Dictionary<string, object> st = d.ContainsKey("requestStatus") ? d["requestStatus"] as Dictionary<string, object> : null;
            if (st != null && !Bool(st, "result"))
            {
                string c = Str(st, "comment");
                throw new ObsError(c.Length > 0 ? c : "OBS refused " + type + " (" + Int(st, "code") + ")", Int(st, "code"));
            }
            return d.ContainsKey("responseData") ? d["responseData"] as Dictionary<string, object> : null;
        }
    }

    // Hello (op 0) -> Identify (op 1, with the password's answer to the challenge) -> Identified (op 2).
    static void Connect(string address, string password, string key)
    {
        Close();
        string a = address.Trim();
        Uri uri;
        if (!Uri.TryCreate(a.Contains("://") ? a : "ws://" + a, UriKind.Absolute, out uri))
            throw new ObsError("obs.address is not an address: " + a, 0);
        ClientWebSocket s = new ClientWebSocket();
        s.Options.Proxy = null;   // OBS is on this PC or the local network
        try
        {
            using (CancellationTokenSource t = new CancellationTokenSource(3000)) Await(s.ConnectAsync(uri, t.Token));
        }
        catch (Exception)
        {
            s.Dispose();
            throw new ObsError("OBS is not open, or its WebSocket server is off", 0);
        }
        ws = s;
        try
        {
            Dictionary<string, object> hello = Receive(3000);
            Dictionary<string, object> hd = hello != null && hello.ContainsKey("d") ? hello["d"] as Dictionary<string, object> : null;
            if (hd == null || Int(hello, "op") != 0) throw new ObsError("No OBS WebSocket server at " + a, 0);
            Dictionary<string, object> id = new Dictionary<string, object> { { "rpcVersion", 1 }, { "eventSubscriptions", 0 } };
            Dictionary<string, object> auth = hd.ContainsKey("authentication") ? hd["authentication"] as Dictionary<string, object> : null;
            if (auth != null)
            {
                if (password.Length == 0) throw new ObsError("OBS asks for a password: set obs.password in shortcuts.ini", 0);
                id["authentication"] = Answer(password, Str(auth, "salt"), Str(auth, "challenge"));
            }
            Send(new Dictionary<string, object> { { "op", 1 }, { "d", id } });
            Dictionary<string, object> ok = Receive(3000);
            if (ok == null)
            {
                bool wrong = s.CloseStatus.HasValue && (int)s.CloseStatus.Value == 4009;   // AuthenticationFailed
                throw new ObsError(wrong ? "Wrong OBS password (obs.password in shortcuts.ini)" : "OBS closed the connection", 0);
            }
            if (Int(ok, "op") != 2) throw new ObsError("OBS did not accept Xdeck", 0);
            wsFor = key;
        }
        catch (ObsError) { Close(); throw; }
        catch (Exception) { Close(); throw new ObsError("No OBS WebSocket server at " + a, 0); }
    }

    // obs-websocket 5: base64(sha256(base64(sha256(password + salt)) + challenge))
    public static string Answer(string password, string salt, string challenge) { return Sha(Sha(password + salt) + challenge); }

    static string Sha(string text)
    {
        using (SHA256 h = SHA256.Create()) return Convert.ToBase64String(h.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }

    static void Close()
    {
        ClientWebSocket s = ws;
        ws = null;
        wsFor = null;
        if (s == null) return;
        try { s.Abort(); } catch { }
        try { s.Dispose(); } catch { }
    }

    static void Send(object msg)
    {
        byte[] b = Encoding.UTF8.GetBytes(json.Serialize(msg));
        using (CancellationTokenSource t = new CancellationTokenSource(3000))
            Await(ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, t.Token));
    }

    // The next message, or null when OBS closed the connection. TimeoutException after ms.
    static Dictionary<string, object> Receive(int ms)
    {
        byte[] buf = new byte[16384];
        using (MemoryStream all = new MemoryStream())
        using (CancellationTokenSource t = new CancellationTokenSource(ms))
            while (true)
            {
                WebSocketReceiveResult r;
                try { r = Await(ws.ReceiveAsync(new ArraySegment<byte>(buf), t.Token)); }
                catch { if (t.IsCancellationRequested) throw new TimeoutException(); throw; }
                if (r.MessageType == WebSocketMessageType.Close) return null;
                all.Write(buf, 0, r.Count);
                if (!r.EndOfMessage) continue;
                Dictionary<string, object> m = json.DeserializeObject(Encoding.UTF8.GetString(all.ToArray())) as Dictionary<string, object>;
                return m ?? new Dictionary<string, object>();
            }
    }

    static void Await(Task t)
    {
        try { t.Wait(); }
        catch (AggregateException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw(); }
    }

    static T Await<T>(Task<T> t) { Await((Task)t); return t.Result; }

    static bool Bool(Dictionary<string, object> d, string k) { return d != null && d.ContainsKey(k) && d[k] is bool && (bool)d[k]; }
    static string Str(Dictionary<string, object> d, string k) { return d != null && d.ContainsKey(k) && d[k] != null ? d[k].ToString() : ""; }
    static int Int(Dictionary<string, object> d, string k)
    {
        try { return d != null && d.ContainsKey(k) && d[k] != null ? Convert.ToInt32(d[k]) : 0; } catch { return 0; }
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
    // Kinds: pad, monitor, tv, headphones, speaker, mic, rec (OBS recording), live (OBS stream),
    // bt (Bluetooth), usb, mouse, keyboard.
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
                case "rec": DrawRec(g, c, fill, s); break;
                case "live": DrawLive(g, c, fill, s); break;
                case "bt": DrawBluetooth(g, c, s); break;
                case "usb": DrawUsb(g, fill, clear, s); break;
                case "mouse": DrawMouse(g, fill, clear, s); break;
                case "keyboard": DrawKeyboard(g, fill, clear, s); break;
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

    // Record: a ring around a dot.
    static void DrawRec(Graphics g, Color c, Brush fill, int s)
    {
        using (Pen ring = new Pen(c, s * 0.09f)) g.DrawEllipse(ring, s * 0.10f, s * 0.10f, s * 0.80f, s * 0.80f);
        g.FillEllipse(fill, s * 0.30f, s * 0.30f, s * 0.40f, s * 0.40f);
    }

    // Live: a dot with two waves on each side.
    static void DrawLive(Graphics g, Color c, Brush fill, int s)
    {
        g.FillEllipse(fill, s * 0.39f, s * 0.39f, s * 0.22f, s * 0.22f);
        using (Pen wave = new Pen(c, s * 0.075f))
        {
            wave.StartCap = LineCap.Round;
            wave.EndCap = LineCap.Round;
            foreach (float r in new float[] { 0.40f, 0.72f })
            {
                float d = s * r, o = (s - d) / 2;
                g.DrawArc(wave, o, o, d, d, -45, 90);
                g.DrawArc(wave, o, o, d, d, 135, 90);
            }
        }
    }

    // Bluetooth: the rune.
    static void DrawBluetooth(Graphics g, Color c, int s)
    {
        using (Pen pen = new Pen(c, s * 0.085f))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            g.DrawLines(pen, new PointF[] {
                new PointF(s * 0.28f, s * 0.30f), new PointF(s * 0.70f, s * 0.68f), new PointF(s * 0.50f, s * 0.88f),
                new PointF(s * 0.50f, s * 0.12f), new PointF(s * 0.70f, s * 0.32f), new PointF(s * 0.28f, s * 0.70f) });
        }
    }

    // USB: a plug, head up, with its two contacts.
    static void DrawUsb(Graphics g, Brush fill, Brush clear, int s)
    {
        g.FillRectangle(fill, s * 0.35f, s * 0.08f, s * 0.30f, s * 0.30f);
        using (GraphicsPath body = Rounded(new RectangleF(s * 0.26f, s * 0.38f, s * 0.48f, s * 0.54f), s * 0.08f)) g.FillPath(fill, body);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.FillRectangle(clear, s * 0.41f, s * 0.17f, s * 0.06f, s * 0.08f);
        g.FillRectangle(clear, s * 0.53f, s * 0.17f, s * 0.06f, s * 0.08f);
        g.CompositingMode = CompositingMode.SourceOver;
    }

    // Mouse: the body, split into two buttons.
    static void DrawMouse(Graphics g, Brush fill, Brush clear, int s)
    {
        using (GraphicsPath body = Rounded(new RectangleF(s * 0.26f, s * 0.06f, s * 0.48f, s * 0.88f), s * 0.24f)) g.FillPath(fill, body);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.FillRectangle(clear, s * 0.48f, s * 0.04f, s * 0.04f, s * 0.36f);
        g.FillRectangle(clear, s * 0.24f, s * 0.38f, s * 0.52f, s * 0.04f);
        g.CompositingMode = CompositingMode.SourceOver;
    }

    // Keyboard: two rows of keys and a space bar.
    static void DrawKeyboard(Graphics g, Brush fill, Brush clear, int s)
    {
        using (GraphicsPath body = Rounded(new RectangleF(s * 0.04f, s * 0.22f, s * 0.92f, s * 0.56f), s * 0.08f)) g.FillPath(fill, body);
        g.CompositingMode = CompositingMode.SourceCopy;
        float k = s * 0.085f, gap = s * 0.05f, x0 = (s - (6 * k + 5 * gap)) / 2;
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < 6; i++)
                g.FillRectangle(clear, x0 + i * (k + gap), s * 0.31f + row * (k + gap), k, k);
        g.FillRectangle(clear, s * 0.30f, s * 0.31f + 2 * (k + gap), s * 0.40f, k);
        g.CompositingMode = CompositingMode.SourceOver;
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

// ---------------------------------------------------------------- banner
// Top left of the primary screen, over everything, without taking the focus: the audio output,
// a device that connects (with its battery when it has one), the controller mouse, OBS.
class Banner : Form
{
    readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer();
    readonly Queue<Action> after = new Queue<Action>();
    string title = "", caption = "Audio output", glyph = "speaker";
    int index, count;
    int battery = NoBattery;      // a controller's battery line: percent, -1 unknown, Wired, or NoBattery
    bool charging;
    DateTime shownAt;
    int holdMs = 2200;
    const int NoBattery = -2, Wired = -3;

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
        battery = NoBattery;
        holdMs = 2200;
        Open(caption, name, kind, index, count);
    }

    // A device that connected (or runs low): its name and, under it, its battery (pills, percent,
    // charging). A controller always has the line (USB, or battery unknown); another device only
    // when it reports a battery.
    public void ShowWithBattery(string caption, Device d)
    {
        battery = d.Pad ? (d.Conn == "USB" ? Wired : d.Battery) : (d.Battery >= 0 ? d.Battery : NoBattery);
        charging = d.Charging;
        holdMs = 3500;
        Open(caption, d.Name, d.Glyph, 0, 0);
    }

    // Shows it now, or once the banner on screen is gone (several controllers connected together).
    public void ShowAfter(Action show)
    {
        if (Visible && anim.Enabled) after.Enqueue(show);
        else show();
    }

    string BatteryText()
    {
        if (battery == Wired) return "USB";
        if (battery < 0) return charging ? "Charging" : "Battery unknown";
        return battery + "%" + (charging ? " · charging" : "");
    }

    const float BatX = 88, BatPillW = 3, BatPillH = 11, BatGap = 2;
    static float BatPillsW { get { return Ui.PillCount * BatPillW + (Ui.PillCount - 1) * BatGap; } }

    void Open(string caption, string name, string kind, int index, int count)
    {
        this.caption = caption; title = name; glyph = kind; this.index = index; this.count = count;
        using (Graphics g = CreateGraphics())
        using (Font tf = new Font("Segoe UI Semibold", 20f, GraphicsUnit.Pixel))
        {
            int textW = (int)Math.Ceiling(g.MeasureString(title, tf).Width);
            using (Font cf = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
            {
                textW = Math.Max(textW, (int)Math.Ceiling(g.MeasureString(caption.ToUpperInvariant(), cf).Width));
                if (battery != NoBattery)
                    textW = Math.Max(textW, (int)Math.Ceiling((battery >= 0 ? BatPillsW + 8 : 0) + g.MeasureString(BatteryText(), cf).Width));
            }
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
        else if (t < 140 + holdMs) Opacity = 1;
        else if (t < 140 + holdMs + 260) Opacity = 1 - (t - 140 - holdMs) / 260.0;
        else
        {
            anim.Stop();
            Hide();
            if (after.Count > 0) after.Dequeue()();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Ui.Smooth(g);
        g.Clear(Ui.Back);
        Color accent = glyph == "rec" || glyph == "live" ? Color.FromArgb(232, 72, 85) : Ui.Accent;

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

        // a controller's battery: pills lit by level, then the percent (or USB / unknown)
        if (battery != NoBattery)
        {
            float x = BatX;
            Color bc = battery >= 0 ? Ui.BatteryColor(battery) : Ui.Dim;
            if (battery >= 0)
            {
                int lit = Math.Max(1, (int)Math.Ceiling(battery / (100.0 / Ui.PillCount)));
                for (int i = 0; i < Ui.PillCount; i++)
                    using (GraphicsPath p = Ui.Rounded(new RectangleF(x + i * (BatPillW + BatGap), 63, BatPillW, BatPillH), BatPillW / 2))
                    using (Brush b = new SolidBrush(i < lit ? bc : Color.FromArgb(62, 62, 66)))
                        g.FillPath(b, p);
                x += BatPillsW + 8;
            }
            using (Font cf = new Font("Segoe UI", 12f, GraphicsUnit.Pixel))
            using (Brush tb = new SolidBrush(bc))
                g.DrawString(BatteryText(), cf, tb, x - 2, 60);
        }

        // position dots
        else if (count > 1)
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
        // Clicking the tray icon while the panel is open first takes the focus away (which closes
        // it, below), then arrives here: that click meant "close", so don't open it again.
        if (!Open && (DateTime.Now - closedByClickAway).TotalMilliseconds < 500) return;
        if (Open) HidePanel();
        else ShowPanel();
    }

    DateTime closedByClickAway;

    // A click anywhere outside the panel closes it, like the other tray panels.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!Open) return;
        closedByClickAway = DateTime.Now;
        HidePanel();
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
            case "update":                                      // "Restart to update"
                if (XdeckApp.PanelUpdate != null) XdeckApp.PanelUpdate();
                break;
            case "mouse":                                       // the switch in a controller's page
                try
                {
                    string key = (string)m["key"];
                    foreach (Pad p in Pads.List())
                        if (p.Mac == key) { MouseMode.Set(Inputs.Find(p.Kind, p.Index), Convert.ToBoolean(m["on"])); break; }
                }
                catch { }
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
                { "battery", p.Battery }, { "charging", p.Charging }, { "conn", p.Conn },
                { "canMouse", Gamepad(p.Kind) && Inputs.Find(p.Kind, p.Index) != null },
                { "mouse", MouseMode.IsOn(Inputs.Find(p.Kind, p.Index)) } }; }) },
            { "mouseKey", Shortcuts.MouseText },
            { "version", Updater.Current },
            { "update", Updater.Now == Updater.State.Ready ? Updater.Version : null },
        });
    }

    static bool Gamepad(string kind) { return kind == "xbox" || kind == "dualsense" || kind == "switchpro" || kind == "joycon"; }

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
        menu.Items.Add("Edit shortcuts…", null, delegate { EditShortcuts(); });
        menu.Items.Add(new ToolStripSeparator());
        updateItem = new ToolStripMenuItem("Restart to update");
        updateItem.Font = new Font(updateItem.Font, FontStyle.Bold);
        updateItem.Visible = false;
        updateItem.Click += delegate { InstallUpdate(); };
        menu.Items.Add(updateItem);
        checkItem = new ToolStripMenuItem("Check for updates");
        checkItem.Click += delegate { userCheck = true; Updater.CheckNow(); };
        menu.Items.Add(checkItem);
        menu.Items.Add("Quit Xdeck", null, delegate { Quit(); });
        tray.ContextMenuStrip = menu;
        tray.BalloonTipClicked += delegate { if (balloonIsUpdate) InstallUpdate(); };
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
        // Controller as mouse: input goes straight out from the controller threads; the banner and
        // the switch in the controller's page follow on the UI thread.
        PadMouse.Move = Inject.Move;
        PadMouse.Scroll = Inject.Scroll;
        PadMouse.Click = Inject.Click;
        PadMouse.Key = Inject.Key;
        MouseMode.Changed = delegate (PadState s, bool on)
        {
            int n = Inputs.IndexOf(s);
            if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate
            {
                string name = s.Kind;
                try { foreach (Pad p in Pads.List()) if (p.Kind == s.Kind && p.Index == n) { name = p.Name; break; } } catch { }
                banner.Show("Controller as mouse", name + (on ? ": on" : ": off"), "pad", 0, 0);
                panel.MouseChanged();
            });
        };
        // OBS shortcuts: the worker thread reports what OBS did; the banner shows it.
        Obs.Said = delegate (string caption, string text, string icon, int index, int count)
        {
            if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { banner.Show(caption, text, icon, index, count); });
        };
        XInputPoller.Start();
        // Devices: controller battery in the tray tooltip, a banner when a device connects or runs low.
        // The first scan (soon after start) only learns which are there already.
        scan = new System.Windows.Forms.Timer();
        scan.Interval = 1500;
        scan.Tick += delegate { WatchDevices(); };
        SystemEvents.PowerModeChanged += PowerChanged;
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

        // Self-update: checks in the background; "Xdeck.exe --update" installs a ready update.
        Updater.Changed = delegate { if (IsHandleCreated) BeginInvoke((MethodInvoker)UpdateChanged); };
        Updater.Start();
        PanelUpdate = delegate { InstallUpdate(); };
        Thread u = new Thread(delegate ()
        {
            using (EventWaitHandle ev = new EventWaitHandle(false, EventResetMode.AutoReset, "Xdeck_install_update"))
                while (true)
                {
                    ev.WaitOne();
                    Updater.Log("--update received (handle " + IsHandleCreated + ")");
                    if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { InstallUpdate(); });
                }
        });
        u.IsBackground = true;
        u.Start();
    }

    // ------------------------------------------------ updates
    public static Action PanelUpdate;     // the panel's "Restart to update"
    ToolStripMenuItem updateItem, checkItem;
    bool userCheck, balloonIsUpdate;
    string announced;                     // the version whose "ready" notification was shown

    void UpdateChanged()
    {
        Updater.State st = Updater.Now;
        bool ready = st == Updater.State.Ready;
        updateItem.Visible = ready;
        updateItem.Text = "Restart to update to " + Updater.Version;
        checkItem.Enabled = st != Updater.State.Checking && st != Updater.State.Downloading;
        checkItem.Text = st == Updater.State.Checking ? "Checking for updates…"
                       : st == Updater.State.Downloading ? "Downloading " + Updater.Version + "…" : "Check for updates";
        if (ready && (announced != Updater.Version || userCheck))
        {
            announced = Updater.Version;
            userCheck = false;
            Balloon("Xdeck " + Updater.Version + " is ready", "Click here to restart Xdeck and update. Quitting Xdeck installs it too.", true);
        }
        else if (userCheck && st == Updater.State.UpToDate) { userCheck = false; Balloon("Xdeck is up to date", "Version " + Updater.Current + ".", false); }
        else if (userCheck && st == Updater.State.Failed) { userCheck = false; Balloon("Xdeck could not check for updates", Updater.Error ?? "", false); }
        panel.SendState();
    }

    void Balloon(string title, string text, bool update)
    {
        balloonIsUpdate = update;
        tray.ShowBalloonTip(update ? 10000 : 4000, title, text.Length > 0 ? text : " ", ToolTipIcon.Info);
    }

    void InstallUpdate()
    {
        Updater.Log("restart to update (state " + Updater.Now + ")");
        string error;
        if (Updater.Install(true, out error)) { tray.Visible = false; Application.Exit(); }
        else Balloon("Xdeck could not update", error, false);
    }

    // Quitting installs a ready update when it needs no administrator prompt.
    void Quit()
    {
        string error;
        if (Updater.Now == Updater.State.Ready && Updater.FolderWritable(AppPaths.Exe)) Updater.Install(false, out error);
        tray.Visible = false;
        Application.Exit();
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
        balloonIsUpdate = false;
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
            balloonIsUpdate = false;
            tray.ShowBalloonTip(3000, "Xdeck shortcuts updated", "Panel: " + p, ToolTipIcon.Info);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        SystemEvents.PowerModeChanged -= PowerChanged;
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

    // ------------------------------------------------ devices
    // A banner for each device that connects: a controller, a Bluetooth device (headphones, mouse,
    // keyboard, phone) or a USB device, with its battery when it reports one; and one when a
    // wireless device's battery gets low (again only after it charged or came back up).
    const int LowBattery = 15, PadBatteryWaitMs = 5000, BatteryWaitMs = 3000, QuietReconnectS = 60, AfterWakeS = 30;
    System.Windows.Forms.Timer scan;
    HashSet<string> known;                                          // null until the first scan
    readonly Dictionary<string, DateTime> arrived = new Dictionary<string, DateTime>();   // connected, banner not shown yet
    readonly Dictionary<string, DateTime> goneAt = new Dictionary<string, DateTime>();
    readonly HashSet<string> lowWarned = new HashSet<string>();
    DateTime quietUntil = DateTime.MinValue;   // asleep, and a moment after waking: devices come back, that is no news
    DateTime deviceArrived = DateTime.MinValue;   // the last USB / Bluetooth device that connected

    void WatchDevices()
    {
        List<Device> all = new List<Device>();
        List<Pad> pads;
        try
        {
            Pads.RefreshReaders();
            pads = Pads.List();
            foreach (Pad p in pads) all.Add(Device.Of(p));
            all.AddRange(Pads.Bluetooth());
            all.AddRange(Pads.Usb());
        }
        catch { return; }
        UpdateTrayText(pads);
        DateTime now = DateTime.Now;
        bool quiet = now < quietUntil;
        HashSet<string> keys = new HashSet<string>();
        foreach (Device d in all)
        {
            if (!keys.Add(d.Key) || known == null || known.Contains(d.Key) || quiet) continue;
            // Back after a moment is no news: Bluetooth mice and keyboards drop out while they sleep,
            // USB devices start over. A controller turned off and on again is still announced.
            DateTime gone;
            if (!d.Pad && goneAt.TryGetValue(d.Key, out gone) && (now - gone).TotalSeconds < QuietReconnectS) continue;
            arrived[d.Key] = now;
            if (!d.Pad) deviceArrived = now;
        }
        if (known != null) foreach (string k in known) if (!keys.Contains(k)) goneAt[k] = now;
        known = keys;   // devices already there when Xdeck starts get no banner

        foreach (string key in new List<string>(arrived.Keys))
        {
            Device d = all.Find(delegate (Device x) { return x.Key == key; });
            if (d == null) { arrived.Remove(key); continue; }   // gone again before its banner
            // The battery can take a moment to come in (a DualSense or Pro Controller reports it in its
            // first input reports, a headset once its hands-free profile connects): wait a little for it.
            if (d.Conn != "USB" && d.Battery < 0 && (now - arrived[key]).TotalMilliseconds < (d.Pad ? PadBatteryWaitMs : BatteryWaitMs)) continue;
            arrived.Remove(key);
            if (d.Battery >= 0 && d.Battery <= LowBattery && !d.Charging) lowWarned.Add(key);   // the banner shows it already
            string caption = d.Pad ? "Controller connected" : d.Conn == "USB" ? "USB device connected" : "Bluetooth device connected";
            banner.ShowAfter(delegate { banner.ShowWithBattery(caption, d); });
        }

        foreach (Device d in all)
        {
            if (d.Conn == "USB" || d.Battery < 0 || arrived.ContainsKey(d.Key)) continue;
            if (d.Charging || d.Battery > LowBattery + 5) { lowWarned.Remove(d.Key); continue; }
            if (d.Battery <= LowBattery && lowWarned.Add(d.Key))
            {
                Device low = d;
                banner.ShowAfter(delegate { banner.ShowWithBattery(low.Pad ? "Controller battery low" : "Battery low", low); });
            }
        }
        lowWarned.RemoveWhere(delegate (string k) { return !keys.Contains(k); });

        // while a new device's battery is on its way, look again sooner
        scan.Interval = arrived.Count > 0 ? 1000 : 3000;
    }

    void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) quietUntil = DateTime.MaxValue;
        else if (e.Mode == PowerModes.Resume) quietUntil = DateTime.Now.AddSeconds(AfterWakeS);
    }

    // Tray tooltip: battery of each wireless controller, with a bolt while charging (max 63 chars).
    void UpdateTrayText(List<Pad> pads)
    {
        List<string> parts = new List<string>();
        foreach (Pad p in pads)
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
        // A headset or speaker that connects over USB or Bluetooth already has its own banner (with
        // its battery): look for it now, and say nothing more about its audio.
        if (!outs.IsSubsetOf(knownOut) || !mics.IsSubsetOf(knownMic)) WatchDevices();
        bool said = (DateTime.Now - deviceArrived).TotalSeconds < 15 || DateTime.Now < quietUntil;
        foreach (string id in mics)
            if (!said && !knownMic.Contains(id)) { banner.Show("Microphone connected", Audio.Name(id), "mic", 0, 0); break; }
        foreach (string id in outs)
            if (!said && !knownOut.Contains(id)) { string n = Audio.Name(id); banner.Show("Audio output connected", n, Audio.Glyph(n), 0, 0); break; }
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
        string[] kinds = { "pad", "monitor", "tv", "headphones", "speaker", "mic", "rec", "live", "bt", "usb", "mouse", "keyboard" };
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
        // the unpacked update copying itself over the installed copy (Updater.Install)
        int oldPid;
        if (args.Length >= 3 && args[0] == "--apply-update" && int.TryParse(args[2], out oldPid))
        {
            Environment.Exit(Updater.ApplyFromHere(args[1], oldPid, Array.IndexOf(args, "--relaunch") >= 0));
            return;
        }
        bool created;
        using (Mutex mutex = new Mutex(true, "Xdeck_single_instance", out created))
        {
            if (!created)
            {
                // "Xdeck.exe --panel" opens the running copy's panel; "--update" installs its ready update
                string signal = Array.IndexOf(args, "--panel") >= 0 ? "Xdeck_show_panel"
                              : Array.IndexOf(args, "--update") >= 0 ? "Xdeck_install_update" : null;
                EventWaitHandle ev;
                if (signal != null && EventWaitHandle.TryOpenExisting(signal, out ev)) using (ev) ev.Set();
                return;
            }
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.Run(new XdeckApp());
        }
    }
}
