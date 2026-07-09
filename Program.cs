// TypeBeep — "שומר עברית"
// התראת צליל/הבהוב עדינה כאשר CapsLock דלוק או כשהמקלדת אינה בעברית.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TypeBeep
{
    static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint idThread);
        [DllImport("user32.dll")] public static extern short GetKeyState(int nVirtKey);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadKeyboardLayout(string pwszKLID, uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        // מבנה INPUT של SendInput בפריסת x64 (החלק של המקלדת בלבד)
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        public struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public ushort wVk;
            [FieldOffset(10)] public ushort wScan;
            [FieldOffset(12)] public uint dwFlags;
            [FieldOffset(16)] public uint time;
            [FieldOffset(24)] public IntPtr dwExtraInfo;
        }
        public const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;

        // waveOut — זרם שמע רציף עצמאי (להשארת התקן השמע ער, בנפרד מהצלילים)
        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }
        [DllImport("winmm.dll")] public static extern int waveOutOpen(out IntPtr hwo, uint uDeviceID, ref WAVEFORMATEX pwfx, IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);
        [DllImport("winmm.dll")] public static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);
        [DllImport("winmm.dll")] public static extern int waveOutWrite(IntPtr hwo, IntPtr pwh, int cbwh);
        [DllImport("winmm.dll")] public static extern int waveOutReset(IntPtr hwo);
        [DllImport("winmm.dll")] public static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, int cbwh);
        [DllImport("winmm.dll")] public static extern int waveOutClose(IntPtr hwo);
        public const int WAVEHDR_SIZE = 48; // גודל WAVEHDR ב-x64
        public const int WHDR_BEGINLOOP = 0x4, WHDR_ENDLOOP = 0x8;
        public const int WM_INPUTLANGCHANGEREQUEST = 0x0050;
        public const uint GA_ROOT = 2;
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] public static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "PlaySoundW", SetLastError = true)]
        public static extern bool PlaySoundFile(string pszSound, IntPtr hmod, uint fdwSound);
        public const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_LOOP = 0x8, SND_FILENAME = 0x20000;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        public const int WH_KEYBOARD_LL = 13;
        public const int WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int WM_HOTKEY = 0x0312;
        public const int VK_CAPITAL = 0x14;
        public const uint MOD_ALT = 0x1;
        public const uint MOD_CONTROL = 0x2;
        public const uint MOD_SHIFT = 0x4;
        public const int HEBREW_LANGID = 0x040D;
    }

    class Settings
    {
        public bool CapsAlert = true;
        public bool LangAlert = true;
        public bool RunAtStartup = true;
        public bool CapsReminder = true;
        public bool KeepAwake = true;   // זרם שקט שמונע מהתקן השמע "להירדם" ולבלוע צלילים קצרים
        public int AlertMode = 0;      // 0 = צליל, 1 = צליל+הבהוב, 2 = הבהוב בלבד
        public int HotkeyIdx = 0;      // אינדקס קיצור ההמרה לעברית
        public int Volume = 60;        // 0-100
        public int CapsSound = 0;
        public int LangSound = 2;
        public List<string> Excluded = new List<string>();

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TypeBeep");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.txt");
            }
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("capsAlert=" + (CapsAlert ? 1 : 0));
                sb.AppendLine("langAlert=" + (LangAlert ? 1 : 0));
                sb.AppendLine("startup=" + (RunAtStartup ? 1 : 0));
                sb.AppendLine("reminder=" + (CapsReminder ? 1 : 0));
                sb.AppendLine("keepAwake=" + (KeepAwake ? 1 : 0));
                sb.AppendLine("mode=" + AlertMode);
                sb.AppendLine("hotkey=" + HotkeyIdx);
                sb.AppendLine("volume=" + Volume);
                sb.AppendLine("capsSound=" + CapsSound);
                sb.AppendLine("langSound=" + LangSound);
                sb.AppendLine("excluded=" + string.Join("|", Excluded.ToArray()));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                    switch (key)
                    {
                        case "capsAlert": s.CapsAlert = val == "1"; break;
                        case "langAlert": s.LangAlert = val == "1"; break;
                        case "startup": s.RunAtStartup = val == "1"; break;
                        case "reminder": s.CapsReminder = val == "1"; break;
                        case "keepAwake": s.KeepAwake = val == "1"; break;
                        case "mode": int.TryParse(val, out s.AlertMode); break;
                        case "hotkey": int.TryParse(val, out s.HotkeyIdx); break;
                        case "volume": int.TryParse(val, out s.Volume); break;
                        case "capsSound": int.TryParse(val, out s.CapsSound); break;
                        case "langSound": int.TryParse(val, out s.LangSound); break;
                        case "excluded":
                            if (val.Length > 0)
                                s.Excluded = new List<string>(val.Split('|'));
                            break;
                    }
                }
            }
            catch { }
            if (s.Volume < 0) s.Volume = 0; if (s.Volume > 100) s.Volume = 100;
            if (s.AlertMode < 0 || s.AlertMode > 2) s.AlertMode = 0;
            if (s.HotkeyIdx < 0 || s.HotkeyIdx > 3) s.HotkeyIdx = 0;
            if (s.CapsSound < 0 || s.CapsSound > 3) s.CapsSound = 0;
            if (s.LangSound < 0 || s.LangSound > 3) s.LangSound = 2;
            return s;
        }
    }

    // מייצר צלילים רכים (סינוס עם עטיפת דעיכה) בזיכרון — בלי קבצים חיצוניים
    static class Sounds
    {
        public static readonly string[] Names = { "פעמון עדין", "צליל כפול", "טיפה", "נצנוץ" };
        const int RATE = 44100;

        static double Attack(double t) { return Math.Min(1.0, t / 0.008); }

        // זרם שקט (2 שניות, ללולאה) — מחזיק את התקן השמע ער בלי שנשמע דבר
        public static byte[] MakeSilence()
        {
            int n = RATE * 2;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + n * 2);
                bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                bw.Write(16); bw.Write((short)1); bw.Write((short)1);
                bw.Write(RATE); bw.Write(RATE * 2); bw.Write((short)2); bw.Write((short)16);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(n * 2);
                for (int i = 0; i < n; i++)
                    bw.Write((short)(2 * Math.Sin(2 * Math.PI * 40 * i / RATE))); // ±2 מתוך 32767 — לא נשמע
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] MakeWav(int sound, int volume, int leadMs)
        {
            double vol = 0.50 * volume / 100.0;
            int leadSamples = leadMs * RATE / 1000;
            double dur;
            var samples = new List<double>();
            switch (sound)
            {
                case 1: // צליל כפול — שתי פעימות רכות עולות
                    dur = 0.55;
                    for (int i = 0; i < (int)(dur * RATE); i++)
                    {
                        double t = i / (double)RATE, v = 0;
                        if (t >= 0.00) { double u = t - 0.00; if (u < 0.18) v += Math.Sin(2 * Math.PI * 523.25 * u) * Attack(u) * Math.Exp(-u / 0.09); }
                        if (t >= 0.19) { double u = t - 0.19; v += Math.Sin(2 * Math.PI * 659.25 * u) * Attack(u) * Math.Exp(-u / 0.13); }
                        samples.Add(v);
                    }
                    break;
                case 2: // טיפה — גלישת תדר יורדת
                    dur = 0.42;
                    double phase = 0;
                    for (int i = 0; i < (int)(dur * RATE); i++)
                    {
                        double t = i / (double)RATE;
                        double f = 900 - 440 * Math.Min(1.0, t / 0.25);
                        phase += 2 * Math.PI * f / RATE;
                        samples.Add(Math.Sin(phase) * Attack(t) * Math.Exp(-t / 0.13));
                    }
                    break;
                case 3: // נצנוץ — טון גבוה בהיר עם גוף
                    dur = 0.55;
                    for (int i = 0; i < (int)(dur * RATE); i++)
                    {
                        double t = i / (double)RATE;
                        double v = Math.Sin(2 * Math.PI * 1046.5 * t) + 0.5 * Math.Sin(2 * Math.PI * 1568 * t) * Math.Exp(-t / 0.08);
                        samples.Add(v * Attack(t) * Math.Exp(-t / 0.15));
                    }
                    break;
                default: // 0: פעמון עדין
                    dur = 0.55;
                    for (int i = 0; i < (int)(dur * RATE); i++)
                    {
                        double t = i / (double)RATE;
                        double v = Math.Sin(2 * Math.PI * 880 * t) + 0.4 * Math.Sin(2 * Math.PI * 1760 * t) * Math.Exp(-t / 0.06);
                        samples.Add(v * Attack(t) * Math.Exp(-t / 0.14) * 0.9);
                    }
                    break;
            }
            // דעיכה לאפס בסוף כדי למנוע "קליק"; ריפוד שקט בהתחלה נותן להתקן השמע זמן להתעורר
            int n = samples.Count;
            int total = leadSamples + n;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(36 + total * 2);
                bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                bw.Write(16); bw.Write((short)1); bw.Write((short)1);
                bw.Write(RATE); bw.Write(RATE * 2); bw.Write((short)2); bw.Write((short)16);
                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(total * 2);
                for (int i = 0; i < leadSamples; i++) bw.Write((short)0);
                for (int i = 0; i < n; i++)
                {
                    double fade = Math.Min(1.0, (n - 1 - i) / (0.03 * RATE));
                    double v = samples[i] * vol * fade;
                    if (v > 1) v = 1; if (v < -1) v = -1;
                    bw.Write((short)(v * 32767));
                }
                bw.Flush();
                return ms.ToArray();
            }
        }
    }

    // הבהוב מסך עדין — שכבה שקופה למחצה שלא תופסת פוקוס ולא חוסמת עכבר
    class FlashForm : Form
    {
        public FlashForm(Color color, Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = color;
            Opacity = 0.22;
            TopMost = true;
            var t = new Timer { Interval = 170 };
            t.Tick += delegate { t.Stop(); t.Dispose(); Close(); };
            t.Start();
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000;
                return cp;
            }
        }
    }

    class MainForm : Form
    {
        Settings settings;
        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        ToolStripMenuItem miConvert, miPause;
        Timer monitor;
        IntPtr hookId = IntPtr.Zero;
        Native.LowLevelKeyboardProc hookProc; // שמירת רפרנס כדי שה-GC לא ינקה
        IntPtr lastIconHandle = IntPtr.Zero;
        Color lastIconColor = Color.Empty;
        uint ownPid;
        Dictionary<uint, string> procCache = new Dictionary<uint, string>();

        IntPtr waveOut, waveHdr, waveData; // הזרם השקט הרציף
        bool suppressTyping;           // מדוכא בזמן המרת טקסט כדי שההקשות הסינתטיות לא יופיעו במעקב/בהתראות
        IntPtr lastForeignHwnd;        // החלון האחרון שהיה בפוקוס (לא שלנו)

        // מעקב אחרי רצף ההקלדה האחרון: נשמר כבר מומר לעברית, ומתאפס בקליק/Enter/ניווט/החלפת חלון
        readonly StringBuilder typedBuf = new StringBuilder();
        IntPtr typedHwnd = IntPtr.Zero;
        IntPtr mouseHookId = IntPtr.Zero;
        Native.LowLevelKeyboardProc mouseProc;
        uint hotkeyMods; int hotkeyVk;  // הקיצור הפעיל — כדי שלחיצתו לא תאפס את המעקב
        DateTime pausedUntil = DateTime.MinValue;
        bool prevBadCaps, prevBadLang, armedCaps, armedLang, capsAlerted, langAlerted;
        DateTime lastCapsAlert = DateTime.MinValue;
        bool startHidden, shownOnce, reallyExit, hideBalloonShown, loadingUi;

        // פקדים
        Label lblStatus, lblVolPct, lblHotkeyHint;
        ComboBox cmbHotkey;
        CheckBox chkCaps, chkLang, chkReminder, chkStartup, chkKeepAwake;
        ComboBox cmbCapsSound, cmbLangSound;
        TrackBar trkVolume;
        RadioButton rbSound, rbBoth, rbFlash;
        ListBox lstExcluded;
        Button btnPause;

        public MainForm(bool startHidden)
        {
            this.startHidden = startHidden;
            settings = Settings.Load();
            ownPid = (uint)Process.GetCurrentProcess().Id;
            Log("=== app start v1.6 pid=" + ownPid + " exe=" + Application.ExecutablePath);
            try { if (Directory.Exists(SoundsDir)) Directory.Delete(SoundsDir, true); }
            catch (Exception ex) { Log("sounds cleanup failed: " + ex.Message); }

            Text = "שומר עברית — TypeBeep";
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ClientSize = new Size(474, 616);
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi();
            LoadUiFromSettings();
            BuildTray();
            SetTrayColor(Color.FromArgb(60, 170, 90));
            ApplyStartup();

            hookProc = HookCallback;
            hookId = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, hookProc, Native.GetModuleHandle(null), 0);
            mouseProc = MouseHookCallback;
            mouseHookId = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, Native.GetModuleHandle(null), 0);

            monitor = new Timer { Interval = 250 };
            monitor.Tick += MonitorTick;
            monitor.Start();

            // יצירת ידית חלון + רישום קיצור גלובלי גם כשמתחילים מוסתרים
            IntPtr h = this.Handle;
            RegisterConvertHotkey(true);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden && !shownOnce && value) { shownOnce = true; value = false; }
            base.SetVisibleCore(value);
        }

        void BuildUi()
        {
            lblStatus = new Label { Location = new Point(12, 10), Size = new Size(450, 22), Font = new Font("Segoe UI Semibold", 9.5F), Text = "..." };
            Controls.Add(lblStatus);

            var g1 = new GroupBox { Text = "התראות וצלילים", Location = new Point(12, 38), Size = new Size(450, 128) };
            chkCaps = new CheckBox { Text = "התראה כש-CapsLock דלוק", Location = new Point(12, 26), Size = new Size(225, 22) };
            cmbCapsSound = new ComboBox { Location = new Point(245, 24), Size = new Size(125, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            var btnPlayCaps = new Button { Text = "▶", Location = new Point(378, 23), Size = new Size(40, 25) };
            chkLang = new CheckBox { Text = "התראה כשהמקלדת לא בעברית", Location = new Point(12, 58), Size = new Size(225, 22) };
            cmbLangSound = new ComboBox { Location = new Point(245, 56), Size = new Size(125, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            var btnPlayLang = new Button { Text = "▶", Location = new Point(378, 55), Size = new Size(40, 25) };
            var lblVol = new Label { Text = "עוצמה:", Location = new Point(12, 96), Size = new Size(60, 20) };
            trkVolume = new TrackBar { Location = new Point(75, 88), Size = new Size(285, 45), Minimum = 0, Maximum = 100, TickFrequency = 10 };
            lblVolPct = new Label { Location = new Point(368, 96), Size = new Size(50, 20) };
            g1.Controls.AddRange(new Control[] { chkCaps, cmbCapsSound, btnPlayCaps, chkLang, cmbLangSound, btnPlayLang, lblVol, trkVolume, lblVolPct });
            Controls.Add(g1);

            var g2 = new GroupBox { Text = "אופן ההתראה", Location = new Point(12, 174), Size = new Size(450, 54) };
            rbSound = new RadioButton { Text = "צליל בלבד", Location = new Point(12, 22), Size = new Size(110, 22) };
            rbBoth = new RadioButton { Text = "צליל + הבהוב מסך", Location = new Point(135, 22), Size = new Size(155, 22) };
            rbFlash = new RadioButton { Text = "הבהוב מסך בלבד", Location = new Point(300, 22), Size = new Size(140, 22) };
            g2.Controls.AddRange(new Control[] { rbSound, rbBoth, rbFlash });
            Controls.Add(g2);

            var g3 = new GroupBox { Text = "אפשרויות", Location = new Point(12, 236), Size = new Size(450, 136) };
            chkReminder = new CheckBox { Text = "תזכורת חוזרת כל 30 שניות כש-CapsLock נשאר דלוק", Location = new Point(12, 22), Size = new Size(425, 22) };
            chkStartup = new CheckBox { Text = "הפעל את התוכנה בעליית המחשב", Location = new Point(12, 48), Size = new Size(425, 22) };
            chkKeepAwake = new CheckBox { Text = "השאר את התקן השמע ער — פתרון לצלילים ש\"נבלעים\" (מומלץ)", Location = new Point(12, 74), Size = new Size(425, 22) };
            var lblHk = new Label { Text = "קיצור ההמרה לעברית:", Location = new Point(12, 104), Size = new Size(125, 20) };
            cmbHotkey = new ComboBox { Location = new Point(145, 101), Size = new Size(125, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbHotkey.Items.AddRange(HotkeyNames);
            g3.Controls.AddRange(new Control[] { chkReminder, chkStartup, chkKeepAwake, lblHk, cmbHotkey });
            Controls.Add(g3);

            var g4 = new GroupBox { Text = "תוכנות מוחרגות — לא תישמע בהן התראה", Location = new Point(12, 380), Size = new Size(450, 138) };
            lstExcluded = new ListBox { Location = new Point(12, 24), Size = new Size(290, 100) };
            var btnAddExc = new Button { Text = "הוספת תוכנה...", Location = new Point(312, 24), Size = new Size(126, 30) };
            var btnDelExc = new Button { Text = "הסרה", Location = new Point(312, 62), Size = new Size(126, 30) };
            g4.Controls.AddRange(new Control[] { lstExcluded, btnAddExc, btnDelExc });
            Controls.Add(g4);

            btnPause = new Button { Text = "השהה לשעה", Location = new Point(12, 528), Size = new Size(150, 32) };
            lblHotkeyHint = new Label { Location = new Point(175, 526), Size = new Size(290, 36), ForeColor = Color.DimGray };
            var btnHide = new Button { Text = "הסתר לאזור השעון", Location = new Point(12, 570), Size = new Size(150, 30) };
            var lblTray = new Label { Text = "סגירת החלון רק מסתירה אותו. ליציאה מלאה: קליק ימני על האייקון שליד השעון", Location = new Point(175, 568), Size = new Size(290, 40), ForeColor = Color.DimGray };
            Controls.AddRange(new Control[] { btnPause, lblHotkeyHint, btnHide, lblTray });

            cmbCapsSound.Items.AddRange(Sounds.Names);
            cmbLangSound.Items.AddRange(Sounds.Names);

            // אירועים
            EventHandler save = delegate { if (!loadingUi) SaveFromUi(); };
            chkCaps.CheckedChanged += save;
            chkLang.CheckedChanged += save;
            chkReminder.CheckedChanged += save;
            chkKeepAwake.CheckedChanged += save;
            cmbHotkey.SelectedIndexChanged += delegate
            {
                if (loadingUi) return;
                SaveFromUi();
                RegisterConvertHotkey(true);
                UpdateHotkeyHint();
            };
            chkStartup.CheckedChanged += delegate { if (!loadingUi) { SaveFromUi(); ApplyStartup(); } };
            cmbCapsSound.SelectedIndexChanged += save;
            cmbLangSound.SelectedIndexChanged += save;
            rbSound.CheckedChanged += save;
            rbBoth.CheckedChanged += save;
            rbFlash.CheckedChanged += save;
            trkVolume.ValueChanged += delegate { lblVolPct.Text = trkVolume.Value + "%"; if (!loadingUi) SaveFromUi(); };
            btnPlayCaps.Click += delegate { Play(cmbCapsSound.SelectedIndex); };
            btnPlayLang.Click += delegate { Play(cmbLangSound.SelectedIndex); };
            btnAddExc.Click += delegate { AddExcluded(); };
            btnDelExc.Click += delegate
            {
                if (lstExcluded.SelectedIndex >= 0)
                {
                    settings.Excluded.Remove((string)lstExcluded.SelectedItem);
                    lstExcluded.Items.RemoveAt(lstExcluded.SelectedIndex);
                    settings.Save();
                }
            };
            btnPause.Click += delegate
            {
                if (DateTime.Now < pausedUntil) pausedUntil = DateTime.MinValue;
                else pausedUntil = DateTime.Now.AddHours(1);
            };
            btnHide.Click += delegate { HideToTray(); };
        }

        void LoadUiFromSettings()
        {
            loadingUi = true;
            chkCaps.Checked = settings.CapsAlert;
            chkLang.Checked = settings.LangAlert;
            chkReminder.Checked = settings.CapsReminder;
            chkKeepAwake.Checked = settings.KeepAwake;
            chkStartup.Checked = settings.RunAtStartup;
            cmbCapsSound.SelectedIndex = settings.CapsSound;
            cmbLangSound.SelectedIndex = settings.LangSound;
            trkVolume.Value = settings.Volume;
            lblVolPct.Text = settings.Volume + "%";
            rbSound.Checked = settings.AlertMode == 0;
            rbBoth.Checked = settings.AlertMode == 1;
            rbFlash.Checked = settings.AlertMode == 2;
            cmbHotkey.SelectedIndex = settings.HotkeyIdx;
            lstExcluded.Items.Clear();
            foreach (string x in settings.Excluded) lstExcluded.Items.Add(x);
            loadingUi = false;
            UpdateHotkeyHint();
        }

        void UpdateHotkeyHint()
        {
            lblHotkeyHint.Text = HotkeyNames[settings.HotkeyIdx] + " — המרת ההקלדה האחרונה (או טקסט מסומן) לעברית";
        }

        void SaveFromUi()
        {
            settings.CapsAlert = chkCaps.Checked;
            settings.LangAlert = chkLang.Checked;
            settings.CapsReminder = chkReminder.Checked;
            settings.KeepAwake = chkKeepAwake.Checked;
            settings.RunAtStartup = chkStartup.Checked;
            settings.CapsSound = Math.Max(0, cmbCapsSound.SelectedIndex);
            settings.LangSound = Math.Max(0, cmbLangSound.SelectedIndex);
            settings.Volume = trkVolume.Value;
            settings.AlertMode = rbBoth.Checked ? 1 : rbFlash.Checked ? 2 : 0;
            settings.HotkeyIdx = Math.Max(0, cmbHotkey.SelectedIndex);
            settings.Save();
        }

        void BuildTray()
        {
            trayMenu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
            trayMenu.Items.Add("הגדרות...", null, delegate { ShowSettings(); });
            miConvert = new ToolStripMenuItem("", null, delegate { ConvertNow(true); });
            trayMenu.Items.Add(miConvert);
            miPause = new ToolStripMenuItem("השהה לשעה", null, delegate { TogglePauseHour(); });
            trayMenu.Items.Add(miPause);
            trayMenu.Opening += delegate
            {
                miPause.Text = DateTime.Now < pausedUntil ? "בטל השהיה" : "השהה לשעה";
                miConvert.Text = "המר לעברית את מה שהוקלד (" + HotkeyNames[settings.HotkeyIdx] + ")";
            };
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("יציאה", null, delegate { reallyExit = true; Close(); });
            tray = new NotifyIcon { Visible = true, Text = "שומר עברית", ContextMenuStrip = trayMenu };
            tray.DoubleClick += delegate { ShowSettings(); };
        }

        void ShowSettings()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void HideToTray()
        {
            Hide();
            if (!hideBalloonShown)
            {
                hideBalloonShown = true;
                tray.ShowBalloonTip(2500, "שומר עברית", "התוכנה ממשיכה לרוץ כאן, ליד השעון.", ToolTipIcon.Info);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            monitor.Stop();
            StopKeepAliveStream();
            if (hookId != IntPtr.Zero) Native.UnhookWindowsHookEx(hookId);
            if (mouseHookId != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHookId);
            Native.UnregisterHotKey(Handle, 1);
            tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == 1)
                BeginInvoke(new MethodInvoker(delegate { ConvertNow(false); }));
            base.WndProc(ref m);
        }

        void TogglePauseHour()
        {
            if (DateTime.Now < pausedUntil)
            {
                pausedUntil = DateTime.MinValue;
                tray.ShowBalloonTip(1500, "שומר עברית", "ההתראות פועלות שוב.", ToolTipIcon.Info);
            }
            else
            {
                pausedUntil = DateTime.Now.AddHours(1);
                tray.ShowBalloonTip(1500, "שומר עברית", "ההתראות הושהו לשעה.", ToolTipIcon.Info);
            }
        }

        // ---- קיצור ההמרה ----

        public static readonly string[] HotkeyNames = { "Ctrl+Alt+H", "Ctrl+Alt+L", "Ctrl+Shift+H", "F9" };

        void RegisterConvertHotkey(bool notifyOnFail)
        {
            Native.UnregisterHotKey(Handle, 1);
            uint mods; int vk;
            switch (settings.HotkeyIdx)
            {
                case 1: mods = Native.MOD_CONTROL | Native.MOD_ALT; vk = 'L'; break;
                case 2: mods = Native.MOD_CONTROL | Native.MOD_SHIFT; vk = 'H'; break;
                case 3: mods = 0; vk = 0x78; break; // F9
                default: mods = Native.MOD_CONTROL | Native.MOD_ALT; vk = 'H'; break;
            }
            bool ok = Native.RegisterHotKey(Handle, 1, mods, (uint)vk);
            hotkeyMods = mods; hotkeyVk = vk;
            Log("hotkey " + HotkeyNames[settings.HotkeyIdx] + " registered=" + ok);
            if (!ok && notifyOnFail)
                tray.ShowBalloonTip(4000, "שומר עברית",
                    "הקיצור " + HotkeyNames[settings.HotkeyIdx] + " תפוס על ידי תוכנה אחרת — בחר קיצור אחר בהגדרות.",
                    ToolTipIcon.Warning);
        }

        // ---- ניטור ----

        static bool IsModifierVk(int vk)
        {
            return vk == 0x10 || vk == 0x11 || vk == 0x12 || (vk >= 0xA0 && vk <= 0xA5) || vk == 0x14 || vk == 0x5B || vk == 0x5C;
        }

        bool IsHotkeyPress(int vk)
        {
            if (vk != hotkeyVk) return false;
            bool ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
            bool alt = (Native.GetAsyncKeyState(0x12) & 0x8000) != 0;
            bool shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
            return ctrl == ((hotkeyMods & Native.MOD_CONTROL) != 0) &&
                   alt == ((hotkeyMods & Native.MOD_ALT) != 0) &&
                   shift == ((hotkeyMods & Native.MOD_SHIFT) != 0);
        }

        IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && !suppressTyping)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (wParam == (IntPtr)Native.WM_KEYDOWN)
                {
                    if (vk >= 0x41 && vk <= 0x5A) // מקשי אותיות — טריגר להתראה
                    {
                        if (armedCaps && prevBadCaps && !capsAlerted) DoAlert(true);
                        if (armedLang && prevBadLang && !langAlerted) DoAlert(false);
                    }
                    TrackKey(vk);
                }
                else if (wParam == (IntPtr)Native.WM_SYSKEYDOWN)
                {
                    // צירופי Alt מבצעים פעולות — מאפסים את המעקב (חוץ מלחיצת קיצור ההמרה עצמו)
                    if (!IsModifierVk(vk) && !IsHotkeyPress(vk)) typedBuf.Length = 0;
                }
            }
            return Native.CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && typedBuf.Length > 0)
            {
                int msg = wParam.ToInt32();
                if (msg == 0x201 || msg == 0x204 || msg == 0x207) // לחיצת עכבר כלשהי
                {
                    // קליק בתוך חלון ההקלדה מזיז את הסמן — המעקב כבר לא רציף. קליק במקום אחר (תפריט שלנו, שורת משימות) לא מפריע
                    var pt = new Native.POINT { X = Marshal.ReadInt32(lParam), Y = Marshal.ReadInt32(lParam, 4) };
                    IntPtr clicked = Native.GetAncestor(Native.WindowFromPoint(pt), Native.GA_ROOT);
                    if (clicked != IntPtr.Zero && clicked == Native.GetAncestor(typedHwnd, Native.GA_ROOT))
                        typedBuf.Length = 0;
                }
            }
            return Native.CallNextHookEx(mouseHookId, nCode, wParam, lParam);
        }

        void TrackKey(int vk)
        {
            if (IsModifierVk(vk)) return;
            if (IsHotkeyPress(vk)) return; // לחיצת קיצור ההמרה לא נוגעת במעקב

            IntPtr fg = Native.GetForegroundWindow();
            if (fg != typedHwnd) { typedBuf.Length = 0; typedHwnd = fg; }

            bool ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
            if (ctrl) { typedBuf.Length = 0; return; } // קיצורי Ctrl משנים מצב — מאפסים

            if (vk == 0x08) { if (typedBuf.Length > 0) typedBuf.Length--; return; } // Backspace
            if (vk == 0x0D || vk == 0x09 || vk == 0x1B || vk == 0x2E ||
                (vk >= 0x21 && vk <= 0x28) || (vk >= 0x70 && vk <= 0x87))          // Enter/Tab/Esc/Del/ניווט/F1-F24
            { typedBuf.Length = 0; return; }

            bool shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
            char c = MapVkToHebrewChar(vk, shift);
            if (c != '\0')
            {
                typedBuf.Append(c);
                if (typedBuf.Length > 200) typedBuf.Remove(0, typedBuf.Length - 200);
            }
        }

        // ממפה מקש פיזי לתו שהיה נוצר בפריסה העברית
        static char MapVkToHebrewChar(int vk, bool shift)
        {
            if (vk >= 0x41 && vk <= 0x5A)
            {
                int idx = EN_KEYS.IndexOf(char.ToLowerInvariant((char)vk));
                return idx >= 0 ? HE_KEYS[idx] : '\0';
            }
            if (vk >= 0x30 && vk <= 0x39)
                return shift ? "!@#$%^&*()"[vk == 0x30 ? 9 : vk - 0x31] : (char)vk;
            if (vk >= 0x60 && vk <= 0x69) return (char)('0' + vk - 0x60); // מקלדת נומרית
            switch (vk)
            {
                case 0x20: return ' ';
                case 0x6A: return '*'; case 0x6B: return '+'; case 0x6D: return '-';
                case 0x6E: return '.'; case 0x6F: return '/';
                case 0xBA: return shift ? ':' : 'ף';
                case 0xDE: return shift ? '"' : ',';
                case 0xBC: return shift ? '<' : 'ת';
                case 0xBE: return shift ? '>' : 'ץ';
                case 0xBF: return shift ? '?' : '.';
                case 0xC0: return shift ? '~' : ';';
                case 0xBD: return shift ? '_' : '-';
                case 0xBB: return shift ? '+' : '=';
                case 0xDB: return '['; case 0xDD: return ']'; case 0xDC: return '\\';
            }
            return '\0';
        }

        // ---- ביצוע ההמרה ----

        void ConvertNow(bool fromMenu)
        {
            if (typedBuf.Length > 0 && typedBuf.ToString().Trim().Length > 0) ConvertTyped(fromMenu);
            else ConvertSelection(fromMenu);
        }

        void ConvertTyped(bool fromMenu)
        {
            try
            {
                string hebrew = typedBuf.ToString();
                IntPtr target = typedHwnd;
                if (target == IntPtr.Zero) return;
                if (Native.GetForegroundWindow() != target)
                {
                    Native.SetForegroundWindow(target);
                    System.Threading.Thread.Sleep(300);
                }
                WaitModifiersReleased();
                suppressTyping = true;
                SendBackspaces(hebrew.Length);
                System.Threading.Thread.Sleep(80);
                SendUnicode(hebrew);
                typedBuf.Length = 0;
                SwitchWindowToHebrew(target);
                Log("convert typed: " + hebrew.Length + " chars");
            }
            catch (Exception ex) { Log("convert typed FAILED: " + ex.Message); }
            finally { suppressTyping = false; }
        }

        void WaitModifiersReleased()
        {
            for (int i = 0; i < 60; i++)
            {
                if ((Native.GetAsyncKeyState(0x11) & 0x8000) == 0 &&
                    (Native.GetAsyncKeyState(0x12) & 0x8000) == 0 &&
                    (Native.GetAsyncKeyState(0x10) & 0x8000) == 0) return;
                System.Threading.Thread.Sleep(25);
            }
        }

        static void SendBackspaces(int n)
        {
            var inputs = new Native.INPUT[n * 2];
            for (int i = 0; i < n; i++)
            {
                inputs[i * 2].type = Native.INPUT_KEYBOARD; inputs[i * 2].wVk = 0x08;
                inputs[i * 2 + 1].type = Native.INPUT_KEYBOARD; inputs[i * 2 + 1].wVk = 0x08; inputs[i * 2 + 1].dwFlags = Native.KEYEVENTF_KEYUP;
            }
            if (n > 0) Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        static void SendUnicode(string s)
        {
            var inputs = new Native.INPUT[s.Length * 2];
            for (int i = 0; i < s.Length; i++)
            {
                inputs[i * 2].type = Native.INPUT_KEYBOARD; inputs[i * 2].wScan = s[i]; inputs[i * 2].dwFlags = Native.KEYEVENTF_UNICODE;
                inputs[i * 2 + 1].type = Native.INPUT_KEYBOARD; inputs[i * 2 + 1].wScan = s[i]; inputs[i * 2 + 1].dwFlags = Native.KEYEVENTF_UNICODE | Native.KEYEVENTF_KEYUP;
            }
            if (s.Length > 0) Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        static void SwitchWindowToHebrew(IntPtr hwnd)
        {
            try
            {
                IntPtr hkl = Native.LoadKeyboardLayout("0000040D", 0);
                if (hkl != IntPtr.Zero)
                    Native.PostMessage(hwnd, (uint)Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
            }
            catch { }
        }

        void MonitorTick(object sender, EventArgs e)
        {
            bool paused = DateTime.Now < pausedUntil;
            IntPtr fg = Native.GetForegroundWindow();
            uint pid = 0;
            uint tid = fg != IntPtr.Zero ? Native.GetWindowThreadProcessId(fg, out pid) : 0;

            bool hebrew = true;
            if (fg != IntPtr.Zero)
            {
                long layout = (long)Native.GetKeyboardLayout(tid);
                int langId = (int)(layout & 0xFFFF);
                if (langId != 0) hebrew = langId == Native.HEBREW_LANGID;
            }
            bool caps = (Native.GetKeyState(Native.VK_CAPITAL) & 1) == 1;

            string pname = GetProcName(pid);
            bool excluded = pid == ownPid || settings.Excluded.Contains(pname);
            if (fg != IntPtr.Zero && pid != ownPid) lastForeignHwnd = fg;

            bool badCaps = settings.CapsAlert && caps && !paused && !excluded;
            bool badLang = settings.LangAlert && !hebrew && !paused && !excluded;

            if (badCaps && !prevBadCaps) armedCaps = true; // ההתראה עצמה תופעל רק כשמתחילים להקליד
            if (!badCaps) { armedCaps = false; capsAlerted = false; }
            if (badLang && !prevBadLang) armedLang = true;
            if (!badLang) { armedLang = false; langAlerted = false; }

            if (badCaps && capsAlerted && settings.CapsReminder &&
                (DateTime.Now - lastCapsAlert).TotalSeconds >= 30) DoAlert(true);

            prevBadCaps = badCaps;
            prevBadLang = badLang;

            EnsureKeepAlive();
            UpdateIndicators(caps, hebrew, paused, excluded);
        }

        // ---- המרת טקסט שהוקלד בפריסה לטינית לעברית (למשל "cuer yuc" -> "בוקר טוב") ----

        const string EN_KEYS = "qwertyuiopasdfghjkl;'zxcvbnm,./";
        const string HE_KEYS = "/'קראטוןםפשדגכעיחלךף,זסבהנמצתץ.";

        public static string ToHebrewLayout(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                char c = char.ToLowerInvariant(ch);
                int idx = EN_KEYS.IndexOf(c);
                sb.Append(idx >= 0 ? HE_KEYS[idx] : ch);
            }
            return sb.ToString();
        }

        void ConvertSelection(bool fromMenu)
        {
            try
            {
                if (fromMenu)
                {
                    // בלחיצה מהתפריט הפוקוס אצלנו — מחזירים אותו לחלון שהמשתמש עבד בו
                    if (lastForeignHwnd != IntPtr.Zero) Native.SetForegroundWindow(lastForeignHwnd);
                    System.Threading.Thread.Sleep(300);
                }
                // ממתינים שהמשתמש ישחרר את Ctrl/Alt/Shift כדי שההקשות הסינתטיות לא יתערבבו
                for (int i = 0; i < 40; i++)
                {
                    if ((Native.GetAsyncKeyState(0x11) & 0x8000) == 0 &&
                        (Native.GetAsyncKeyState(0x12) & 0x8000) == 0 &&
                        (Native.GetAsyncKeyState(0x10) & 0x8000) == 0) break;
                    System.Threading.Thread.Sleep(25);
                }
                suppressTyping = true;

                string oldClip = null;
                try { if (Clipboard.ContainsText()) oldClip = Clipboard.GetText(); } catch { }
                try { Clipboard.Clear(); } catch { }
                SendKeys.SendWait("^c");
                string text = null;
                for (int i = 0; i < 20; i++)
                {
                    System.Threading.Thread.Sleep(30);
                    try { if (Clipboard.ContainsText()) { text = Clipboard.GetText(); break; } } catch { }
                }
                if (string.IsNullOrEmpty(text))
                {
                    try { if (oldClip != null) Clipboard.SetText(oldClip); } catch { }
                    tray.ShowBalloonTip(2500, "שומר עברית", "לא נמצא מה להמיר — הקלד או סמן טקסט ואז לחץ " + HotkeyNames[settings.HotkeyIdx] + ".", ToolTipIcon.Warning);
                    return;
                }
                string converted = ToHebrewLayout(text);
                if (converted == text)
                {
                    try { if (oldClip != null) Clipboard.SetText(oldClip); } catch { }
                    tray.ShowBalloonTip(2500, "שומר עברית", "אין בטקסט המסומן תווים להמרה.", ToolTipIcon.Info);
                    return;
                }
                try { Clipboard.SetText(converted); } catch { return; }
                SendKeys.SendWait("^v");
                Log("convert: " + text.Length + " chars");
            }
            catch (Exception ex) { Log("convert FAILED: " + ex.Message); }
            finally { suppressTyping = false; }
        }

        // זרם שקט רציף דרך waveOut — ערוץ נפרד מהצלילים, לא נעצר ולא מתאתחל לעולם.
        // כך התקן השמע נשאר ער תמיד, וההתראות (PlaySound) מנוגנות מעליו בלי שום אינטראקציה איתו.
        void EnsureKeepAlive()
        {
            if (settings.KeepAwake) StartKeepAliveStream();
            else StopKeepAliveStream();
        }

        void StartKeepAliveStream()
        {
            if (waveOut != IntPtr.Zero) return;
            try
            {
                var fmt = new Native.WAVEFORMATEX
                {
                    wFormatTag = 1, nChannels = 1, nSamplesPerSec = 44100,
                    wBitsPerSample = 16, nBlockAlign = 2, nAvgBytesPerSec = 88200, cbSize = 0
                };
                IntPtr h;
                int r = Native.waveOutOpen(out h, 0xFFFFFFFF, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
                if (r != 0) { Log("keepalive waveOutOpen failed " + r); return; }

                int n = 44100; // חוצץ של שנייה — סינוס 40Hz באמפליטודה 8 מתוך 32767, לא שמיע
                waveData = Marshal.AllocHGlobal(n * 2);
                for (int i = 0; i < n; i++)
                    Marshal.WriteInt16(waveData, i * 2, (short)(8 * Math.Sin(2 * Math.PI * 40 * i / 44100.0)));

                waveHdr = Marshal.AllocHGlobal(Native.WAVEHDR_SIZE);
                for (int off = 0; off < Native.WAVEHDR_SIZE; off += 4) Marshal.WriteInt32(waveHdr, off, 0);
                Marshal.WriteIntPtr(waveHdr, 0, waveData);       // lpData
                Marshal.WriteInt32(waveHdr, 8, n * 2);            // dwBufferLength
                r = Native.waveOutPrepareHeader(h, waveHdr, Native.WAVEHDR_SIZE);
                if (r != 0) { Log("keepalive prepare failed " + r); Native.waveOutClose(h); FreeWaveMem(); return; }

                // לולאה אינסופית של החוצץ בחומרה — הזרם מתנגן לנצח
                Marshal.WriteInt32(waveHdr, 24, Marshal.ReadInt32(waveHdr, 24) | Native.WHDR_BEGINLOOP | Native.WHDR_ENDLOOP);
                Marshal.WriteInt32(waveHdr, 28, -1);              // dwLoops = אינסוף
                r = Native.waveOutWrite(h, waveHdr, Native.WAVEHDR_SIZE);
                if (r != 0) { Log("keepalive write failed " + r); Native.waveOutClose(h); FreeWaveMem(); return; }

                waveOut = h;
                Log("keepalive stream started (continuous)");
            }
            catch (Exception ex) { Log("keepalive stream FAILED: " + ex.Message); }
        }

        void StopKeepAliveStream()
        {
            if (waveOut == IntPtr.Zero) return;
            try
            {
                Native.waveOutReset(waveOut);
                Native.waveOutUnprepareHeader(waveOut, waveHdr, Native.WAVEHDR_SIZE);
                Native.waveOutClose(waveOut);
            }
            catch { }
            waveOut = IntPtr.Zero;
            FreeWaveMem();
            Log("keepalive stream stopped");
        }

        void FreeWaveMem()
        {
            if (waveHdr != IntPtr.Zero) { Marshal.FreeHGlobal(waveHdr); waveHdr = IntPtr.Zero; }
            if (waveData != IntPtr.Zero) { Marshal.FreeHGlobal(waveData); waveData = IntPtr.Zero; }
        }

        void DoAlert(bool isCaps)
        {
            Log("alert " + (isCaps ? "caps" : "lang") + " mode=" + settings.AlertMode);
            if (isCaps) { capsAlerted = true; lastCapsAlert = DateTime.Now; }
            else langAlerted = true;
            if (settings.AlertMode != 2) Play(isCaps ? settings.CapsSound : settings.LangSound);
            if (settings.AlertMode >= 1)
            {
                Color c = isCaps ? Color.FromArgb(220, 50, 50) : Color.FromArgb(255, 150, 0);
                try { new FlashForm(c, Screen.FromPoint(Cursor.Position).Bounds).Show(); } catch { }
            }
        }

        void Play(int soundIndex)
        {
            try
            {
                // נגינה מקובץ ולא מהזיכרון — עוקף בעיות חיי-זיכרון וסנכרון של winmm
                // כשהזרם השקט כבוי, מוסיפים ריפוד שקט שנותן להתקן השמע זמן להתעורר
                int lead = settings.KeepAwake ? 0 : 600;
                string dir = SoundsDir;
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "s" + soundIndex + "_v" + settings.Volume + "_l" + lead + ".wav");
                if (!File.Exists(file)) File.WriteAllBytes(file, Sounds.MakeWav(soundIndex, settings.Volume, lead));
                bool ok = Native.PlaySoundFile(file, IntPtr.Zero, Native.SND_FILENAME | Native.SND_ASYNC | Native.SND_NODEFAULT);
                int err = ok ? 0 : Marshal.GetLastWin32Error();
                Log("play idx=" + soundIndex + " vol=" + settings.Volume + " lead=" + lead + " ok=" + ok + (ok ? "" : " err=" + err));
            }
            catch (Exception ex) { Log("play FAILED: " + ex.Message); }
        }

        static string SoundsDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TypeBeep", "sounds"); }
        }

        static void Log(string msg)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TypeBeep", "play.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff  ") + msg + "\r\n");
            }
            catch { }
        }

        string GetProcName(uint pid)
        {
            if (pid == 0) return "";
            string name;
            if (procCache.TryGetValue(pid, out name)) return name;
            try { name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); }
            catch { name = ""; }
            if (procCache.Count > 300) procCache.Clear();
            procCache[pid] = name;
            return name;
        }

        void UpdateIndicators(bool caps, bool hebrew, bool paused, bool excluded)
        {
            Color c;
            string status;
            if (paused) { c = Color.Gray; status = "ההתראות מושהות"; }
            else if (excluded) { c = Color.Gray; status = "התוכנה הפעילה מוחרגת מהתראות"; }
            else if (caps) { c = Color.FromArgb(220, 50, 50); status = hebrew ? "CapsLock דלוק" : "CapsLock דלוק + המקלדת לא בעברית!"; }
            else if (!hebrew) { c = Color.FromArgb(255, 150, 0); status = "המקלדת לא בעברית"; }
            else { c = Color.FromArgb(60, 170, 90); status = "הכול תקין — עברית, CapsLock כבוי"; }

            if (Visible)
            {
                lblStatus.Text = "מצב נוכחי: " + status;
                lblStatus.ForeColor = c == Color.Gray ? Color.DimGray : c;
                btnPause.Text = DateTime.Now < pausedUntil ? "בטל השהיה" : "השהה לשעה";
            }
            SetTrayColor(c);
            string tip = "שומר עברית — " + status;
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
        }

        void SetTrayColor(Color c)
        {
            if (c == lastIconColor) return;
            lastIconColor = c;
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(c)) g.FillEllipse(b, 1, 1, 13, 13);
                using (var p = new Pen(Color.FromArgb(90, Color.Black))) g.DrawEllipse(p, 1, 1, 13, 13);
            }
            IntPtr h = bmp.GetHicon();
            var icon = Icon.FromHandle(h);
            tray.Icon = icon;
            this.Icon = icon;
            if (lastIconHandle != IntPtr.Zero) Native.DestroyIcon(lastIconHandle);
            lastIconHandle = h;
            bmp.Dispose();
        }

        void ApplyStartup()
        {
            try
            {
                var rk = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (settings.RunAtStartup)
                    rk.SetValue("TypeBeep", "\"" + Application.ExecutablePath + "\" -tray");
                else
                    rk.DeleteValue("TypeBeep", false);
                rk.Close();
            }
            catch { }
        }

        void AddExcluded()
        {
            using (var f = new Form
            {
                Text = "בחר תוכנה להחרגה",
                RightToLeft = RightToLeft.Yes,
                RightToLeftLayout = true,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                ClientSize = new Size(380, 360),
                StartPosition = FormStartPosition.CenterParent,
                Font = Font
            })
            {
                var lb = new ListBox { Location = new Point(10, 10), Size = new Size(360, 290) };
                var seen = new HashSet<string>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (p.MainWindowTitle.Length == 0) continue;
                        string nm = p.ProcessName.ToLowerInvariant();
                        if (nm == "typebeep" || !seen.Add(nm)) continue;
                        string title = p.MainWindowTitle;
                        if (title.Length > 40) title = title.Substring(0, 40) + "...";
                        lb.Items.Add(nm + "  —  " + title);
                    }
                    catch { }
                }
                var ok = new Button { Text = "הוסף", Location = new Point(10, 312), Size = new Size(110, 32), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "ביטול", Location = new Point(130, 312), Size = new Size(110, 32), DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lb, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                lb.DoubleClick += delegate { if (lb.SelectedItem != null) f.DialogResult = DialogResult.OK; };
                if (f.ShowDialog(this) == DialogResult.OK && lb.SelectedItem != null)
                {
                    string name = ((string)lb.SelectedItem).Split(new[] { "  —  " }, StringSplitOptions.None)[0];
                    if (!settings.Excluded.Contains(name))
                    {
                        settings.Excluded.Add(name);
                        lstExcluded.Items.Add(name);
                        settings.Save();
                    }
                }
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool tray = false;
            foreach (string a in args) if (a == "-tray") tray = true;

            bool created;
            var mutex = new System.Threading.Mutex(true, "TypeBeepSingleInstance", out created);
            if (!created)
            {
                MessageBox.Show("שומר עברית כבר פועל — חפש את האייקון העגול ליד השעון.", "שומר עברית",
                    MessageBoxButtons.OK, MessageBoxIcon.Information,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(tray));
            GC.KeepAlive(mutex);
        }
    }
}
