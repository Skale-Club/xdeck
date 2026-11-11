// XdeckSetup.exe: installs Xdeck like any Windows app, from one file. build.ps1 embeds the app
// (build\Xdeck, zipped) as the resource "Xdeck.payload.zip" and the version as assembly info.
//
//   XdeckSetup.exe                      the install window: for everyone on this PC (asks for
//                                       administrator approval) or only for me, Start with Windows
//   XdeckSetup.exe /S [/CurrentUser] [/NoAutostart]       silent install (everyone by default)
//   "Uninstall Xdeck.exe" /uninstall [/S]                 what Settings › Apps runs to remove it
//
// Installing puts the app in C:\Program Files\Xdeck (everyone) or %LOCALAPPDATA%\Programs\Xdeck
// (only me), a Start menu shortcut, a Startup shortcut (Start with Windows), an entry in
// Settings › Apps › Installed apps, and a copy of this file as "Uninstall Xdeck.exe". Each user's
// settings live in %LOCALAPPDATA%\Xdeck and are kept on reinstall and uninstall. Installed copies
// then update themselves (Updater in src\Xdeck.cs).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Setup
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Xdeck";
    const string UninstallerName = "Uninstall Xdeck.exe";

    public static string Version
    {
        get { System.Version v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor + "." + v.Build; }
    }

    static string Self { get { return Assembly.GetExecutingAssembly().Location; } }

    static bool IsAdmin
    {
        get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
    }

    public static string Target(bool allUsers)
    {
        return allUsers ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Xdeck")
                        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Xdeck");
    }

    static string MenuDir(bool allUsers)
    {
        return Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonPrograms : Environment.SpecialFolder.Programs);
    }

    static string StartupDir(bool allUsers)
    {
        return Environment.GetFolderPath(allUsers ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);
    }

    static bool Has(string[] args, string flag)
    {
        foreach (string a in args) if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    [STAThread]
    static int Main(string[] args)
    {
        bool quiet = Has(args, "/S") || Has(args, "/quiet");
        try
        {
            int parent;
            if (args.Length >= 4 && args[0] == "/uninstall-from" && int.TryParse(args[3], out parent))
                return UninstallFrom(args[1], args[2] == "1", parent, quiet);
            if (Has(args, "/uninstall")) return Uninstall(quiet);
            if (Has(args, "/install") || quiet)
            {
                bool allUsers = !Has(args, "/CurrentUser");
                if (allUsers && !IsAdmin) return Elevated("/install /S" + (Has(args, "/NoAutostart") ? " /NoAutostart" : ""));
                Install(allUsers, !Has(args, "/NoAutostart"));
                return 0;
            }
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.Run(new SetupForm());
            return 0;
        }
        catch (Exception ex)
        {
            if (!quiet) MessageBox.Show(ex.Message, "Xdeck Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // Runs this file again as administrator and waits; 1223 when the prompt was turned down.
    public static int Elevated(string arguments)
    {
        ProcessStartInfo psi = new ProcessStartInfo(Self, arguments);
        psi.UseShellExecute = true;
        psi.Verb = "runas";
        try
        {
            using (Process p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
        }
        catch (Win32Exception) { return 1223; }
    }

    // ------------------------------------------------------------ install
    public static void Install(bool allUsers, bool autostart)
    {
        string target = Target(allUsers);
        StopXdeck();

        // an install for everyone replaces this user's own copy, so Xdeck does not start twice
        if (allUsers)
        {
            string own = Target(false);
            if (!string.Equals(own, target, StringComparison.OrdinalIgnoreCase)) TryDeleteDir(own);
            TryDelete(Path.Combine(MenuDir(false), "Xdeck.lnk"));
            TryDelete(Path.Combine(StartupDir(false), "Xdeck.lnk"));
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        }

        Directory.CreateDirectory(target);
        foreach (string f in Directory.GetFiles(target, "*", SearchOption.AllDirectories)) DeleteOrMoveAside(f);
        foreach (string d in Directory.GetDirectories(target)) TryDeleteDir(d);

        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Xdeck.payload.zip"))
        {
            if (s == null) throw new Exception("This setup file has no Xdeck inside; download it again.");
            using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    // the payload holds Xdeck\...; never a portable Data folder
                    string name = e.FullName.Replace('/', '\\');
                    int cut = name.IndexOf('\\');
                    if (cut < 0 || e.Name.Length == 0) continue;
                    name = name.Substring(cut + 1);
                    if (name.StartsWith("Data\\", StringComparison.OrdinalIgnoreCase) || name.Contains("..")) continue;
                    string dest = Path.Combine(target, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
        }
        File.Copy(Self, Path.Combine(target, UninstallerName), true);

        string exe = Path.Combine(target, "Xdeck.exe");
        Shortcut(Path.Combine(MenuDir(allUsers), "Xdeck.lnk"), exe);
        if (autostart) Shortcut(Path.Combine(StartupDir(allUsers), "Xdeck.lnk"), exe);
        else TryDelete(Path.Combine(StartupDir(allUsers), "Xdeck.lnk"));
        Register(allUsers, target);
        StartAsUser(exe);
    }

    // Settings › Apps › Installed apps
    static void Register(bool allUsers, string target)
    {
        long bytes = 0;
        foreach (string f in Directory.GetFiles(target, "*", SearchOption.AllDirectories)) bytes += new FileInfo(f).Length;
        string un = Path.Combine(target, UninstallerName);
        using (RegistryKey hive = RegistryKey.OpenBaseKey(allUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64))
        using (RegistryKey k = hive.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", "Xdeck");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", "Skale Club");
            k.SetValue("DisplayIcon", Path.Combine(target, "Xdeck.exe"));
            k.SetValue("InstallLocation", target);
            k.SetValue("UninstallString", "\"" + un + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + un + "\" /uninstall /S");
            k.SetValue("URLInfoAbout", "https://github.com/Skale-Club/xdeck");
            k.SetValue("HelpLink", "https://skale.club");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        }
    }

    static void Shortcut(string path, string exe)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(t);
        object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
        Type lt = lnk.GetType();
        lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exe });
        lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(exe) });
        lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Xdeck - audio and controllers" });
        lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }

    // Through Explorer, so Xdeck runs as the signed-in user even when setup is elevated.
    static void StartAsUser(string exe)
    {
        try { Process.Start("explorer.exe", "\"" + exe + "\""); } catch { }
    }

    // every running copy (all users when elevated), so its files can be replaced
    static void StopXdeck()
    {
        foreach (Process p in Process.GetProcessesByName("Xdeck"))
            try { using (p) { p.Kill(); p.WaitForExit(5000); } } catch { }
        Thread.Sleep(300);
    }

    static void TryDelete(string f) { try { if (File.Exists(f)) File.Delete(f); } catch { } }

    static void TryDeleteDir(string d)
    {
        for (int i = 0; i < 5; i++)
        {
            try { if (Directory.Exists(d)) Directory.Delete(d, true); return; } catch { Thread.Sleep(400); }
        }
    }

    // a file still open (another user's Xdeck that could not be stopped) can only be renamed
    static void DeleteOrMoveAside(string f)
    {
        try { File.Delete(f); }
        catch { try { File.Move(f, f + "." + DateTime.Now.Ticks + ".old-xdeck"); } catch { } }
    }

    // ------------------------------------------------------------ uninstall
    static int Uninstall(bool quiet)
    {
        string target = Path.GetDirectoryName(Self);
        bool allUsers = target.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
        if (!quiet && MessageBox.Show("Remove Xdeck from this PC?\n\nEach user's settings stay in %LOCALAPPDATA%\\Xdeck.",
                "Uninstall Xdeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1602;
        // This file lives in the folder being removed, so a copy in %TEMP% does the work once this
        // one has exited (it asks for administrator approval itself when needed).
        string copy = Path.Combine(Path.GetTempPath(), "xdeck-uninstall-" + DateTime.Now.Ticks + ".exe");
        File.Copy(Self, copy, true);
        Process.Start(new ProcessStartInfo(copy, "/uninstall-from \"" + target + "\" " + (allUsers ? "1" : "0") + " " +
                                           Process.GetCurrentProcess().Id + (quiet ? " /S" : "")) { UseShellExecute = false }).Dispose();
        return 0;
    }

    // From the copy in %TEMP%: parent is the uninstaller that started it (0 for the elevated pass).
    static int UninstallFrom(string target, bool allUsers, int parent, bool quiet)
    {
        if (parent != 0) try { using (Process p = Process.GetProcessById(parent)) p.WaitForExit(15000); } catch (ArgumentException) { }
        int code;
        if (allUsers && !IsAdmin) code = Elevated("/uninstall-from \"" + target + "\" 1 0 /S");
        else code = Remove(target, allUsers);
        if (!quiet)
        {
            if (code == 0) MessageBox.Show("Xdeck was removed.", "Uninstall Xdeck");
            else if (code == 1223) MessageBox.Show("Xdeck was not removed: administrator approval was turned down.", "Uninstall Xdeck");
            else MessageBox.Show("Xdeck could not remove everything in " + target + ".", "Uninstall Xdeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        // the outer copy deletes itself once it has exited
        if (parent != 0)
            try
            {
                ProcessStartInfo del = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /f /q \"" + Self + "\"");
                del.CreateNoWindow = true;
                del.UseShellExecute = false;
                Process.Start(del).Dispose();
            }
            catch { }
        return code;
    }

    static int Remove(string target, bool allUsers)
    {
        StopXdeck();
        TryDelete(Path.Combine(MenuDir(allUsers), "Xdeck.lnk"));
        TryDelete(Path.Combine(StartupDir(allUsers), "Xdeck.lnk"));
        try
        {
            using (RegistryKey hive = RegistryKey.OpenBaseKey(allUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64))
                hive.DeleteSubKeyTree(UninstallKey, false);
        }
        catch { }
        TryDeleteDir(target);
        return Directory.Exists(target) ? 2 : 0;
    }
}

// The install window.
class SetupForm : Form
{
    readonly RadioButton everyone = new RadioButton(), justMe = new RadioButton();
    readonly CheckBox autostart = new CheckBox();
    readonly Button install = new Button(), cancel = new Button();
    readonly Label status = new Label();
    bool done;

    public SetupForm()
    {
        Text = "Xdeck Setup";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(470, 300);
        BackColor = Color.White;
        PictureBox logo = new PictureBox();
        logo.SizeMode = PictureBoxSizeMode.Zoom;
        logo.SetBounds(24, 22, 48, 48);
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Xdeck.ico"))
            {
                Icon = new Icon(s);
                s.Position = 0;
                using (Icon big = new Icon(s, 64, 64)) logo.Image = big.ToBitmap();
            }
        }
        catch { }
        Controls.Add(logo);

        Label title = new Label();
        title.Text = "Xdeck " + Setup.Version;
        title.Font = new Font("Segoe UI Semibold", 15f);
        title.AutoSize = true;
        title.Location = new Point(86, 20);
        Controls.Add(title);

        Label tag = new Label();
        tag.Text = "Your audio and your controllers, from the tray.";
        tag.ForeColor = Color.FromArgb(96, 96, 96);
        tag.AutoSize = true;
        tag.Location = new Point(88, 52);
        Controls.Add(tag);

        everyone.Text = "Install for everyone on this PC (asks for administrator approval)";
        everyone.AutoSize = true;
        everyone.Location = new Point(26, 98);
        everyone.Checked = true;
        Controls.Add(everyone);

        justMe.Text = "Install only for me";
        justMe.AutoSize = true;
        justMe.Location = new Point(26, 126);
        Controls.Add(justMe);

        autostart.Text = "Start with Windows";
        autostart.AutoSize = true;
        autostart.Checked = true;
        autostart.Location = new Point(26, 166);
        Controls.Add(autostart);

        Label updates = new Label();
        updates.Text = "Xdeck updates itself after this.";
        updates.ForeColor = Color.FromArgb(96, 96, 96);
        updates.AutoSize = true;
        updates.Location = new Point(26, 198);
        Controls.Add(updates);

        status.AutoSize = false;
        status.SetBounds(26, 222, 420, 22);
        status.ForeColor = Color.FromArgb(0, 95, 184);
        Controls.Add(status);

        install.Text = "Install";
        install.SetBounds(262, 254, 92, 30);
        install.Click += delegate { if (done) Close(); else Go(); };
        Controls.Add(install);
        AcceptButton = install;

        cancel.Text = "Cancel";
        cancel.SetBounds(362, 254, 86, 30);
        cancel.Click += delegate { Close(); };
        Controls.Add(cancel);
        CancelButton = cancel;
    }

    void Go()
    {
        bool all = everyone.Checked, auto = autostart.Checked;
        install.Enabled = cancel.Enabled = everyone.Enabled = justMe.Enabled = autostart.Enabled = false;
        status.ForeColor = Color.FromArgb(0, 95, 184);
        status.Text = all ? "Waiting for administrator approval…" : "Installing…";
        Thread t = new Thread(delegate ()
        {
            string error = null;
            try
            {
                if (all && !IsAdminNow())
                {
                    int code = Setup.Elevated("/install /S" + (auto ? "" : " /NoAutostart"));
                    if (code == 1223) error = "Administrator approval was turned down. Pick \"Install only for me\" to install without it.";
                    else if (code != 0) error = "Setup could not finish (code " + code + ").";
                }
                else Setup.Install(all, auto);
            }
            catch (Exception ex) { error = ex.Message; }
            BeginInvoke((MethodInvoker)delegate { Finished(error); });
        });
        t.IsBackground = true;
        t.Start();
    }

    static bool IsAdminNow()
    {
        return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    }

    void Finished(string error)
    {
        if (error != null)
        {
            status.ForeColor = Color.FromArgb(196, 43, 28);
            status.Text = error;
            install.Enabled = cancel.Enabled = everyone.Enabled = justMe.Enabled = autostart.Enabled = true;
            return;
        }
        done = true;
        status.ForeColor = Color.FromArgb(16, 124, 16);
        status.Text = "Xdeck is installed. Find the gear in the tray, next to the clock.";
        install.Text = "Close";
        install.Enabled = true;
        cancel.Visible = false;
    }
}
