// TypeBeep-Setup — תוכנית התקנה/הסרה של "שומר עברית"
// קובץ TypeBeep.exe מוטמע בתוך ההתקנה כמשאב; ההתקנה לתיקיית המשתמש — ללא צורך בהרשאות מנהל.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TypeBeepSetup
{
    static class Installer
    {
        public const string AppName = "שומר עברית";
        public const string Version = "1.0";

        public static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeBeep"); }
        }
        public static string ExePath { get { return Path.Combine(InstallDir, "TypeBeep.exe"); } }
        static string UninstPath { get { return Path.Combine(InstallDir, "Uninstall.exe"); } }
        static string StartMenuLnk
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); }
        }
        static string DesktopLnk
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); }
        }
        static string SettingsDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TypeBeep"); }
        }
        const string UninstKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TypeBeep";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static void KillApp()
        {
            foreach (var p in Process.GetProcessesByName("TypeBeep"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
        }

        public static void Install(bool desktopShortcut)
        {
            KillApp();
            Directory.CreateDirectory(InstallDir);

            // חילוץ התוכנה מהמשאב המוטמע (עם נסיונות חוזרים אם הקובץ עדיין נעול)
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("TypeBeep.exe"))
                    using (FileStream dst = File.Create(ExePath))
                    {
                        byte[] buf = new byte[65536];
                        int n;
                        while ((n = src.Read(buf, 0, buf.Length)) > 0) dst.Write(buf, 0, n);
                    }
                    break;
                }
                catch (IOException)
                {
                    if (attempt >= 5) throw;
                    Thread.Sleep(500);
                }
            }

            // העתקת ההתקנה עצמה כתוכנית הסרה
            try { File.Copy(Application.ExecutablePath, UninstPath, true); } catch { }

            // קיצורי דרך
            CreateShortcut(StartMenuLnk, ExePath);
            if (desktopShortcut) CreateShortcut(DesktopLnk, ExePath);

            // רישום ב"אפליקציות מותקנות" של Windows
            using (var rk = Registry.CurrentUser.CreateSubKey(UninstKey))
            {
                rk.SetValue("DisplayName", AppName + " (TypeBeep)");
                rk.SetValue("DisplayVersion", Version);
                rk.SetValue("Publisher", "TypeBeep");
                rk.SetValue("InstallLocation", InstallDir);
                rk.SetValue("DisplayIcon", ExePath);
                rk.SetValue("UninstallString", "\"" + UninstPath + "\" -uninstall");
                rk.SetValue("NoModify", 1, RegistryValueKind.DWord);
                rk.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                rk.SetValue("EstimatedSize", 120, RegistryValueKind.DWord);
            }
        }

        public static void Launch()
        {
            try { Process.Start(ExePath); } catch { }
        }

        public static void Uninstall(bool silent)
        {
            if (!silent)
            {
                var r = MessageBox.Show("להסיר את \"" + AppName + "\" מהמחשב?", "הסרת " + AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                if (r != DialogResult.Yes) return;
            }
            KillApp();
            try { using (var rk = Registry.CurrentUser.OpenSubKey(RunKey, true)) if (rk != null) rk.DeleteValue("TypeBeep", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstKey, false); } catch { }
            try { File.Delete(StartMenuLnk); } catch { }
            try { File.Delete(DesktopLnk); } catch { }
            try { if (Directory.Exists(SettingsDir)) Directory.Delete(SettingsDir, true); } catch { }
            try { File.Delete(ExePath); } catch { }

            // מחיקה עצמית של תיקיית ההתקנה (אחרי שהתהליך הזה ייסגר)
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ping 127.0.0.1 -n 3 >nul & rd /s /q \"" + InstallDir + "\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                });
            }
            catch { }

            if (!silent)
                MessageBox.Show("\"" + AppName + "\" הוסר מהמחשב.", "הסרה הושלמה",
                    MessageBoxButtons.OK, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        }

        static void CreateShortcut(string lnkPath, string target)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { AppName + " — התראת CapsLock ושפה" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            catch { }
        }
    }

    class SetupForm : Form
    {
        public SetupForm()
        {
            Text = "התקנת " + Installer.AppName;
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(430, 230);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F);

            var lblTitle = new Label
            {
                Text = Installer.AppName + " — התראה עדינה על CapsLock ומקלדת לא בעברית",
                Location = new Point(15, 15),
                Size = new Size(400, 24),
                Font = new Font("Segoe UI Semibold", 10F)
            };
            var lblDesc = new Label
            {
                Text = "ההתקנה תעתיק את התוכנה אל תיקיית המשתמש:\n" + Installer.InstallDir +
                       "\n\nהתוכנה תופעל בסיום ותרוץ אוטומטית בכל עליית מחשב (ניתן לשינוי בהגדרות). להסרה: הגדרות Windows ← אפליקציות.",
                Location = new Point(15, 48),
                Size = new Size(400, 95)
            };
            var chkDesktop = new CheckBox
            {
                Text = "צור קיצור דרך על שולחן העבודה",
                Location = new Point(15, 148),
                Size = new Size(400, 22),
                Checked = true
            };
            var btnInstall = new Button { Text = "התקן", Location = new Point(15, 185), Size = new Size(120, 32) };
            var btnCancel = new Button { Text = "ביטול", Location = new Point(145, 185), Size = new Size(120, 32) };
            Controls.AddRange(new Control[] { lblTitle, lblDesc, chkDesktop, btnInstall, btnCancel });
            AcceptButton = btnInstall;
            CancelButton = btnCancel;

            btnCancel.Click += delegate { Close(); };
            btnInstall.Click += delegate
            {
                btnInstall.Enabled = false;
                try
                {
                    Installer.Install(chkDesktop.Checked);
                    Installer.Launch();
                    MessageBox.Show("ההתקנה הושלמה!\n\"" + Installer.AppName + "\" פועל עכשיו — חפש את העיגול הצבעוני ליד השעון.",
                        "ההתקנה הושלמה", MessageBoxButtons.OK, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    Close();
                }
                catch (Exception ex)
                {
                    btnInstall.Enabled = true;
                    MessageBox.Show("ההתקנה נכשלה: " + ex.Message, "שגיאה",
                        MessageBoxButtons.OK, MessageBoxIcon.Error,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                }
            };
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = false, silent = false;
            foreach (string a in args)
            {
                if (a == "-uninstall") uninstall = true;
                if (a == "-silent") silent = true;
            }
            if (uninstall) { Installer.Uninstall(silent); return; }
            if (silent) { Installer.Install(true); Installer.Launch(); return; }
            Application.Run(new SetupForm());
        }
    }
}
