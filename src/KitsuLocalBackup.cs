using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("StoryEco | Kitsu Local Backup")]
[assembly: AssemblyProduct("StoryEco | Kitsu Local Backup")]
[assembly: AssemblyCompany("StoryEco")]
[assembly: AssemblyCopyright("Copyright © 2026 StoryEco")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]
[assembly: AssemblyInformationalVersion("1.1.1")]

namespace StoryEco.KitsuBackup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Any(arg => String.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                Environment.Exit(SelfTest.Run());
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal static class SelfTest
    {
        public static int Run()
        {
            string directory = Path.Combine(Path.GetTempPath(), "kitsu-backup-self-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string diagnosticPath = Path.Combine(Path.GetTempPath(),
                "StoryEco-KitsuLocalBackup-self-test.log");
            byte[] source = new byte[(512 * 1024) + 137];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(source);
            bool success = false;
            try
            {
                string samplePath = Path.Combine(directory, "sample.bin");
                File.WriteAllBytes(samplePath, source);
                byte[] restored = File.ReadAllBytes(samplePath);
                if (!source.SequenceEqual(restored)) return 11;

                byte[] firstHash;
                byte[] secondHash;
                using (SHA256 sha = SHA256.Create()) firstHash = sha.ComputeHash(source);
                using (SHA256 sha = SHA256.Create()) secondHash = sha.ComputeHash(restored);
                if (!firstHash.SequenceEqual(secondHash)) return 12;

                Assembly assembly = Assembly.GetExecutingAssembly();
                string[] resources = {
                    "StoryEco.Assets.Vazirmatn.ttf",
                    "StoryEco.Assets.Kitsu.png",
                    "StoryEco.Assets.StoryEco.png"
                };
                foreach (string resource in resources)
                {
                    using (Stream stream = assembly.GetManifestResourceStream(resource))
                        if (stream == null || stream.Length == 0) return 13;
                }

                var settings = new AppSettings();
                settings.Normalize();
                if (settings.S3CaCertificatePath != "") return 14;
                settings = new AppSettings { S3Endpoint = "https://tehran-2a.irans3.com",
                    S3CaCertificatePath = AppSettings.DefaultS3CaCertificatePath };
                settings.Normalize();
                if (settings.S3CaCertificatePath != "") return 15;
                settings.S3CaCertificatePath = AppSettings.DefaultS3CaCertificatePath;
                settings.Normalize();
                if (settings.S3CaCertificatePath == "") return 16;
                settings = new AppSettings { S3Endpoint = "https://example.org",
                    S3CaCertificatePath = AppSettings.DefaultS3CaCertificatePath };
                settings.Normalize();
                if (settings.S3CaCertificatePath == "") return 17;
                settings = new AppSettings { S3Endpoint = "https://tehran-2a.irans3.com",
                    S3CaCertificatePath = "custom.pem" };
                settings.Normalize();
                if (settings.S3CaCertificatePath != "custom.pem") return 18;

                success = true;
                return 0;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(diagnosticPath, ex.ToString()); } catch { }
                return 10;
            }
            finally
            {
                Array.Clear(source, 0, source.Length);
                try { Directory.Delete(directory, true); } catch { }
                if (success) try { File.Delete(diagnosticPath); } catch { }
            }
        }
    }

    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember] public string SshHost = "";
        [DataMember] public int SshPort = 22;
        [DataMember] public string SshUser = "";
        [DataMember] public string SshKeyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh", "id_ed25519");
        [DataMember] public string ProtectedSudoPassword = "";

        [DataMember] public string S3Endpoint = "";
        [DataMember] public string S3Region = "";
        // Retained for settings-file compatibility. Version 1.1+ always discovers
        // and backs up every bucket visible to the configured credentials.
        [DataMember] public string S3Bucket = "*";
        [DataMember] public string S3CaCertificatePath = "";
        [DataMember] public int TlsSettingsVersion;
        [DataMember] public string ProtectedS3AccessKey = "";
        [DataMember] public string ProtectedS3SecretKey = "";
        [DataMember] public bool S3ForcePathStyle = true;

        [DataMember] public string LocalRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Kitsu Backups");
        [DataMember] public string RclonePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "tools", "rclone.exe");

        public static string DefaultS3CaCertificatePath
        {
            get
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "certs", "certum-dv-tls-g2-r39-chain.pem");
            }
        }

        public void Normalize()
        {
            S3Bucket = "*";
            if (String.Equals((S3Endpoint ?? "").TrimEnd('/'),
                "https://tehran-2a.irans3.com", StringComparison.OrdinalIgnoreCase) &&
                (String.IsNullOrWhiteSpace(S3Region) ||
                 String.Equals(S3Region, "default", StringComparison.OrdinalIgnoreCase) ||
                 String.Equals(S3Region, "tehran-2a", StringComparison.OrdinalIgnoreCase)))
                S3Region = "tehran-2";
            // One-time migration of the legacy bundled workaround only.
            // Explicit custom certificates and subsequent user choices are preserved.
            if (TlsSettingsVersion < 1 &&
                String.Equals((S3Endpoint ?? "").TrimEnd('/'),
                    "https://tehran-2a.irans3.com", StringComparison.OrdinalIgnoreCase) &&
                String.Equals(Path.GetFileName(S3CaCertificatePath ?? ""),
                    "certum-dv-tls-g2-r39-chain.pem", StringComparison.OrdinalIgnoreCase))
                S3CaCertificatePath = "";
            TlsSettingsVersion = 1;
            if ((String.IsNullOrWhiteSpace(RclonePath) || !File.Exists(RclonePath)) &&
                File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "rclone.exe")))
                RclonePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "rclone.exe");
        }

        public string SudoPassword
        {
            get { return SecretProtector.Unprotect(ProtectedSudoPassword); }
            set { ProtectedSudoPassword = SecretProtector.Protect(value); }
        }

        public string S3AccessKey
        {
            get { return SecretProtector.Unprotect(ProtectedS3AccessKey); }
            set { ProtectedS3AccessKey = SecretProtector.Protect(value); }
        }

        public string S3SecretKey
        {
            get { return SecretProtector.Unprotect(ProtectedS3SecretKey); }
            set { ProtectedS3SecretKey = SecretProtector.Protect(value); }
        }

    }

    internal static class SecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(
            "StoryEco.KitsuLocalBackup.v1");

        public static string Protect(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            byte[] clear = Encoding.UTF8.GetBytes(value);
            byte[] protectedBytes = ProtectedData.Protect(
                clear, Entropy, DataProtectionScope.CurrentUser);
            Array.Clear(clear, 0, clear.Length);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string Unprotect(string value)
        {
            if (String.IsNullOrEmpty(value)) return "";
            try
            {
                byte[] protectedBytes = Convert.FromBase64String(value);
                byte[] clear = ProtectedData.Unprotect(
                    protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                string result = Encoding.UTF8.GetString(clear);
                Array.Clear(clear, 0, clear.Length);
                return result;
            }
            catch
            {
                return "";
            }
        }
    }

    internal static class SettingsStore
    {
        public static readonly string DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StoryEco", "KitsuLocalBackup");
        public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");

        public static AppSettings Load()
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            try
            {
                using (FileStream stream = File.OpenRead(FilePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                    AppSettings settings = (AppSettings)serializer.ReadObject(stream);
                    settings.Normalize();
                    return settings;
                }
            }
            catch
            {
                return new AppSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            Directory.CreateDirectory(DirectoryPath);
            string temporary = FilePath + ".tmp";
            using (FileStream stream = File.Create(temporary))
            {
                var serializer = new DataContractJsonSerializer(typeof(AppSettings));
                serializer.WriteObject(stream, settings);
            }
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temporary, FilePath);
        }
    }

    internal static class UiTheme
    {
        public static readonly Color Window = Color.FromArgb(13, 18, 27);
        public static readonly Color Surface = Color.FromArgb(20, 28, 40);
        public static readonly Color SurfaceRaised = Color.FromArgb(31, 43, 59);
        public static readonly Color Input = Color.FromArgb(11, 16, 24);
        public static readonly Color Border = Color.FromArgb(63, 82, 105);
        public static readonly Color Accent = Color.FromArgb(238, 105, 36);
        public static readonly Color AccentSoft = Color.FromArgb(151, 67, 28);
        public static readonly Color Text = Color.FromArgb(241, 245, 249);
        public static readonly Color Muted = Color.FromArgb(159, 174, 193);
        public static readonly Color Success = Color.FromArgb(117, 221, 145);

        private static readonly PrivateFontCollection PrivateFonts = new PrivateFontCollection();
        private static readonly List<IntPtr> FontMemory = new List<IntPtr>();
        private static readonly FontFamily VazirmatnFamily = LoadFont();

        public static readonly Image KitsuLogo = LoadImage("StoryEco.Assets.Kitsu.png");
        public static readonly Image StoryEcoLogo = LoadImage("StoryEco.Assets.StoryEco.png");
        public static readonly Icon AppIcon = CreateIcon(KitsuLogo);

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont,
            IntPtr pdv, [In] ref uint pcFonts);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
            ref int value, int valueSize);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static FontFamily LoadFont()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
                    "StoryEco.Assets.Vazirmatn.ttf"))
                {
                    if (stream == null) throw new InvalidOperationException("Embedded Vazirmatn font was not found.");
                    byte[] bytes = new byte[checked((int)stream.Length)];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0) break;
                        offset += read;
                    }
                    IntPtr memory = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, memory, bytes.Length);
                    FontMemory.Add(memory);
                    PrivateFonts.AddMemoryFont(memory, bytes.Length);
                    uint installed = 0;
                    AddFontMemResourceEx(memory, (uint)bytes.Length, IntPtr.Zero, ref installed);
                    Array.Clear(bytes, 0, bytes.Length);
                }
                return PrivateFonts.Families.Length > 0
                    ? PrivateFonts.Families[0]
                    : FontFamily.GenericSansSerif;
            }
            catch
            {
                return new FontFamily("Segoe UI");
            }
        }

        private static Image LoadImage(string resourceName)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null) return new Bitmap(1, 1);
                using (Image source = Image.FromStream(stream))
                    return new Bitmap(source);
            }
        }

        private static Icon CreateIcon(Image image)
        {
            try
            {
                using (var bitmap = new Bitmap(image, new Size(64, 64)))
                {
                    IntPtr handle = bitmap.GetHicon();
                    try { return (Icon)Icon.FromHandle(handle).Clone(); }
                    finally { DestroyIcon(handle); }
                }
            }
            catch { return SystemIcons.Application; }
        }

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        {
            try
            {
                FontStyle supported = VazirmatnFamily.IsStyleAvailable(style)
                    ? style
                    : FontStyle.Regular;
                return new Font(VazirmatnFamily, size, supported, GraphicsUnit.Point);
            }
            catch { return new Font("Segoe UI", size, style, GraphicsUnit.Point); }
        }

        public static void EnableDarkTitleBar(Form form)
        {
            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
            }
            catch { }
        }

        public static void Apply(Control root)
        {
            root.ForeColor = Text;
            root.Font = Font(root.Font.Size, root.Font.Style);

            if (root is Form || root is TabControl || root is TabPage)
                root.BackColor = Window;
            else if (root is TextBoxBase)
            {
                root.BackColor = Input;
                root.ForeColor = Text;
            }
            else if (root is NumericUpDown)
            {
                root.BackColor = Input;
                root.ForeColor = Text;
            }
            else if (root is Button)
            {
                var button = (Button)root;
                button.BackColor = SurfaceRaised;
                button.ForeColor = Text;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = AccentSoft;
                button.FlatAppearance.BorderSize = Object.Equals(root.Tag, "chrome") ? 0 : 1;
                button.UseVisualStyleBackColor = false;
                button.Cursor = Cursors.Hand;
            }
            else if (root is Label)
            {
                root.BackColor = Color.Transparent;
                root.ForeColor = Object.Equals(root.Tag, "muted") ? Muted
                    : Object.Equals(root.Tag, "accent") ? Accent
                    : Object.Equals(root.Tag, "success") ? Success
                    : Text;
            }
            else if (root is CheckBox)
            {
                root.BackColor = Color.Transparent;
                root.ForeColor = Text;
            }
            else if (root is PictureBox)
                root.BackColor = Color.Transparent;
            else if (Object.Equals(root.Tag, "card"))
                root.BackColor = SurfaceRaised;
            else
                root.BackColor = Surface;

            foreach (Control child in root.Controls) Apply(child);
        }
    }

    internal static class RtlMessageBox
    {
        public static DialogResult Show(IWin32Window owner, string message, string caption,
            MessageBoxIcon icon)
        {
            using (var dialog = new Form())
            {
                dialog.Text = caption;
                dialog.Icon = UiTheme.AppIcon;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MinimizeBox = false;
                dialog.MaximizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.RightToLeft = RightToLeft.Yes;
                dialog.RightToLeftLayout = true;
                dialog.Width = 580;
                int estimatedLines = Math.Max(1, message.Length / 64) + message.Count(c => c == '\n');
                dialog.Height = Math.Max(200, Math.Min(420, 165 + estimatedLines * 20));

                var layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.Padding = new Padding(18);
                layout.ColumnCount = 2;
                layout.RowCount = 2;
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

                Icon systemIcon = icon == MessageBoxIcon.Error ? SystemIcons.Error
                    : icon == MessageBoxIcon.Warning ? SystemIcons.Warning
                    : SystemIcons.Information;
                var picture = new PictureBox();
                picture.Image = systemIcon.ToBitmap();
                picture.SizeMode = PictureBoxSizeMode.CenterImage;
                picture.Dock = DockStyle.Fill;
                layout.Controls.Add(picture, 0, 0);

                var text = new TextBox();
                text.Text = message;
                text.Multiline = true;
                text.ReadOnly = true;
                text.BorderStyle = BorderStyle.None;
                text.ScrollBars = ScrollBars.Vertical;
                text.Dock = DockStyle.Fill;
                text.TextAlign = HorizontalAlignment.Left;
                text.RightToLeft = RightToLeft.Yes;
                layout.Controls.Add(text, 1, 0);

                var ok = new Button();
                ok.Text = "تأیید";
                ok.DialogResult = DialogResult.OK;
                ok.Width = 112;
                ok.Height = 36;
                ok.Anchor = AnchorStyles.None;
                layout.Controls.Add(ok, 0, 1);
                layout.SetColumnSpan(ok, 2);

                dialog.Controls.Add(layout);
                dialog.AcceptButton = ok;
                dialog.Shown += delegate { UiTheme.EnableDarkTitleBar(dialog); };
                UiTheme.Apply(dialog);
                return dialog.ShowDialog(owner);
            }
        }
    }

    internal sealed class PageNavigator : UserControl
    {
        private readonly FlowLayoutPanel _navigation = new FlowLayoutPanel();
        private readonly Panel _content = new Panel();
        private readonly List<Control> _pages = new List<Control>();
        private readonly List<Button> _buttons = new List<Button>();
        private int _selectedIndex = -1;

        public PageNavigator()
        {
            Dock = DockStyle.Fill;
            BackColor = UiTheme.Window;
            RightToLeft = RightToLeft.Yes;

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _navigation.Dock = DockStyle.Fill;
            _navigation.FlowDirection = FlowDirection.RightToLeft;
            _navigation.RightToLeft = RightToLeft.No;
            _navigation.WrapContents = false;
            _navigation.Padding = new Padding(14, 6, 14, 4);
            _navigation.BackColor = UiTheme.Window;

            _content.Dock = DockStyle.Fill;
            _content.BackColor = UiTheme.Window;
            _content.Padding = new Padding(0);

            layout.Controls.Add(_navigation, 0, 0);
            layout.Controls.Add(_content, 0, 1);
            Controls.Add(layout);
        }

        public int SelectedIndex
        {
            get { return _selectedIndex; }
            set
            {
                if (value < 0 || value >= _pages.Count) return;
                _selectedIndex = value;
                for (int i = 0; i < _pages.Count; i++)
                {
                    _pages[i].Visible = i == value;
                    if (i == value) _pages[i].BringToFront();
                }
                RefreshTheme();
            }
        }

        public void AddPage(string title, Control page)
        {
            int index = _pages.Count;
            page.Dock = DockStyle.Fill;
            page.Visible = false;
            page.Margin = new Padding(0);
            _pages.Add(page);
            _content.Controls.Add(page);

            var button = new Button();
            button.Text = title;
            button.Width = 148;
            button.Height = 39;
            button.Margin = new Padding(4, 1, 4, 1);
            button.RightToLeft = RightToLeft.Yes;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Cursor = Cursors.Hand;
            button.Click += delegate { SelectedIndex = index; };
            _buttons.Add(button);
            _navigation.Controls.Add(button);

            if (_selectedIndex < 0) SelectedIndex = 0;
            else RefreshTheme();
        }

        public void RefreshTheme()
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                bool selected = i == _selectedIndex;
                Button button = _buttons[i];
                button.BackColor = selected ? UiTheme.Accent : UiTheme.Window;
                button.ForeColor = selected ? Color.White : UiTheme.Muted;
                button.FlatAppearance.BorderSize = 0;
                button.Font = UiTheme.Font(10F, selected ? FontStyle.Bold : FontStyle.Regular);
                button.UseVisualStyleBackColor = false;
            }
        }
    }

    internal sealed class DarkProgressBar : Control
    {
        private readonly Timer _timer = new Timer();
        private int _offset;
        private int _speed;

        public DarkProgressBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint, true);
            BackColor = UiTheme.Input;
            _timer.Interval = 28;
            _timer.Tick += delegate
            {
                _offset = (_offset + 14) % Math.Max(1, Width + Math.Max(80, Width / 4));
                Invalidate();
            };
        }

        public int MarqueeAnimationSpeed
        {
            get { return _speed; }
            set
            {
                _speed = value;
                _timer.Enabled = value > 0;
                if (value <= 0) _offset = 0;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(UiTheme.Input);
            using (var border = new Pen(UiTheme.Border))
                e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            if (_speed <= 0 || Width < 4 || Height < 4) return;

            int segment = Math.Max(80, Width / 4);
            int x = _offset - segment;
            using (var brush = new SolidBrush(UiTheme.Accent))
                e.Graphics.FillRectangle(brush, x, 2, segment, Math.Max(1, Height - 4));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class MainForm : Form
    {
        private AppSettings _settings;
        private readonly PageNavigator _tabs = new PageNavigator();
        private readonly RichTextBox _log = new RichTextBox();
        private readonly Label _status = new Label();
        private readonly DarkProgressBar _progress = new DarkProgressBar();
        private readonly List<Button> _actionButtons = new List<Button>();
        private Button _maximizeWindowButton;

        private const int WmNcHitTest = 0x0084;
        private const int WmNcLeftButtonDown = 0x00A1;
        private const int HtCaption = 2;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, int message,
            IntPtr wParam, IntPtr lParam);

        private TextBox _sshHost;
        private NumericUpDown _sshPort;
        private TextBox _sshUser;
        private TextBox _sshKey;
        private TextBox _sudoPassword;
        private TextBox _s3Endpoint;
        private TextBox _s3Region;
        private TextBox _s3CaCertificate;
        private TextBox _s3Access;
        private TextBox _s3Secret;
        private CheckBox _s3PathStyle;
        private TextBox _localRoot;
        private TextBox _rclonePath;

        public MainForm()
        {
            _settings = SettingsStore.Load();
            Text = "StoryEco | Kitsu Local Backup";
            Icon = UiTheme.AppIcon;
            Width = 1040;
            Height = 780;
            MinimumSize = new Size(900, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Font = UiTheme.Font(10F);
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            BackColor = UiTheme.Window;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(1);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Margin = new Padding(0);
            root.Padding = new Padding(0);
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(BuildTitleBar(), 0, 0);
            root.Controls.Add(_tabs, 0, 1);
            Controls.Add(root);

            BuildHomeTab();
            BuildSettingsTab();
            BuildLogTab();
            BuildGuideTab();
            LoadSettingsIntoControls();
            UiTheme.Apply(this);
            BackColor = UiTheme.Window;
            _tabs.RefreshTheme();
            _status.ForeColor = UiTheme.Success;
            Resize += delegate { UpdateMaximizeButton(); };
        }

        private Control BuildTitleBar()
        {
            var bar = new Panel();
            bar.Dock = DockStyle.Fill;
            bar.Margin = new Padding(0);
            bar.BackColor = UiTheme.Surface;
            bar.RightToLeft = RightToLeft.No;

            var logo = new PictureBox();
            logo.Image = UiTheme.KitsuLogo;
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.Dock = DockStyle.Left;
            logo.Width = 48;
            logo.Padding = new Padding(10, 8, 6, 8);

            var title = new Label();
            title.Text = "StoryEco | Kitsu Local Backup";
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Padding = new Padding(6, 2, 0, 0);
            title.Font = UiTheme.Font(10.5F, FontStyle.Bold);

            var windowButtons = new Panel();
            windowButtons.Dock = DockStyle.Right;
            windowButtons.Width = 144;
            windowButtons.RightToLeft = RightToLeft.No;

            Button minimize = MakeWindowButton("−");
            minimize.Left = 0;
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };

            _maximizeWindowButton = MakeWindowButton("▢");
            _maximizeWindowButton.Left = 48;
            _maximizeWindowButton.Click += delegate { ToggleMaximize(); };

            Button close = MakeWindowButton("×");
            close.Left = 96;
            close.Click += delegate { Close(); };
            close.MouseEnter += delegate { close.BackColor = Color.FromArgb(190, 45, 55); };
            close.MouseLeave += delegate { close.BackColor = UiTheme.Surface; };

            windowButtons.Controls.Add(minimize);
            windowButtons.Controls.Add(_maximizeWindowButton);
            windowButtons.Controls.Add(close);
            bar.Controls.Add(title);
            bar.Controls.Add(logo);
            bar.Controls.Add(windowButtons);

            MouseEventHandler drag = delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                if (WindowState == FormWindowState.Maximized) return;
                ReleaseCapture();
                SendMessage(Handle, WmNcLeftButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
            };
            bar.MouseDown += drag;
            title.MouseDown += drag;
            logo.MouseDown += drag;
            bar.DoubleClick += delegate { ToggleMaximize(); };
            title.DoubleClick += delegate { ToggleMaximize(); };
            return bar;
        }

        private static Button MakeWindowButton(string text)
        {
            var button = new Button();
            button.Text = text;
            button.Top = 0;
            button.Width = 48;
            button.Height = 52;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = UiTheme.Surface;
            button.ForeColor = UiTheme.Text;
            button.Tag = "chrome";
            button.TabStop = false;
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            UpdateMaximizeButton();
        }

        private void UpdateMaximizeButton()
        {
            if (_maximizeWindowButton != null)
                _maximizeWindowButton.Text = WindowState == FormWindowState.Maximized ? "❐" : "▢";
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg != WmNcHitTest || WindowState != FormWindowState.Normal) return;
            if ((int)message.Result != 1) return;

            long value = message.LParam.ToInt64();
            Point screen = new Point(unchecked((short)(value & 0xffff)),
                unchecked((short)((value >> 16) & 0xffff)));
            Point point = PointToClient(screen);
            const int grip = 8;
            bool left = point.X <= grip;
            bool right = point.X >= ClientSize.Width - grip;
            bool top = point.Y <= grip;
            bool bottom = point.Y >= ClientSize.Height - grip;

            if (left && top) message.Result = (IntPtr)HtTopLeft;
            else if (right && top) message.Result = (IntPtr)HtTopRight;
            else if (left && bottom) message.Result = (IntPtr)HtBottomLeft;
            else if (right && bottom) message.Result = (IntPtr)HtBottomRight;
            else if (left) message.Result = (IntPtr)HtLeft;
            else if (right) message.Result = (IntPtr)HtRight;
            else if (top) message.Result = (IntPtr)HtTop;
            else if (bottom) message.Result = (IntPtr)HtBottom;
        }

        private void BuildHomeTab()
        {
            var page = new Panel();
            page.Padding = new Padding(30, 24, 30, 28);
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.RowCount = 7;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var heading = new TableLayoutPanel();
            heading.Dock = DockStyle.Fill;
            heading.RightToLeft = RightToLeft.Yes;
            heading.RowCount = 2;
            heading.ColumnCount = 1;
            heading.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
            heading.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
            var appName = new Label();
            appName.Text = "StoryEco | Kitsu Local Backup";
            appName.Font = UiTheme.Font(20F, FontStyle.Bold);
            appName.Dock = DockStyle.Fill;
            // WinForms mirrors ContentAlignment when RightToLeft is enabled.
            // The *Left values below therefore render against the physical right edge.
            appName.TextAlign = ContentAlignment.BottomLeft;
            var subtitle = new Label();
            subtitle.Text = "تهیه نسخه‌های محلی بک آپ و بازیابی اطلاعات";
            subtitle.Font = UiTheme.Font(11F);
            subtitle.ForeColor = UiTheme.Muted;
            subtitle.Tag = "muted";
            subtitle.Dock = DockStyle.Fill;
            subtitle.TextAlign = ContentAlignment.TopLeft;
            heading.Controls.Add(appName, 0, 0);
            heading.Controls.Add(subtitle, 0, 1);
            layout.Controls.Add(heading, 0, 0);

            Button snapshot = MakeActionButton(
                "Snapshot قابل‌حمل سرور",
                "دیتابیس، Kitsu، تنظیمات سیستم، Docker و فهرست کامل بازسازی",
                async delegate { await RunServerBackup("snapshot"); });
            Button backup = MakeActionButton(
                "Backup سرور Kitsu",
                "دیتابیس، تنظیمات Kitsu و Previewهای محلی؛ مناسب اجرای روزانه",
                async delegate { await RunServerBackup("backup"); });
            Button s3 = MakeActionButton(
                "Backup محلی S3",
                "Snapshot تاریخ‌دار و کم‌حجم با Hard-link برای فایل‌های بدون تغییر",
                async delegate { await RunS3Backup(); });

            layout.Controls.Add(snapshot, 0, 1);
            layout.Controls.Add(backup, 0, 2);
            layout.Controls.Add(s3, 0, 3);

            _progress.Dock = DockStyle.Fill;
            _progress.Margin = new Padding(5, 5, 5, 5);
            _progress.MarqueeAnimationSpeed = 0;
            layout.Controls.Add(_progress, 0, 4);

            _status.Text = "آماده";
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleCenter;
            layout.Controls.Add(_status, 0, 5);
            layout.Controls.Add(CreateStoryEcoFooter(), 0, 6);

            page.Controls.Add(layout);
            _tabs.AddPage("پشتیبان‌گیری", page);
        }

        private Button MakeActionButton(string title, string subtitle, Func<Task> action)
        {
            var button = new Button();
            button.Text = title + Environment.NewLine + subtitle;
            button.Dock = DockStyle.Fill;
            button.Font = UiTheme.Font(11F, FontStyle.Bold);
            button.Margin = new Padding(5, 7, 5, 7);
            button.Click += async delegate
            {
                try { await action(); }
                catch (Exception ex) { HandleFailure(ex); }
            };
            _actionButtons.Add(button);
            return button;
        }

        private void BuildSettingsTab()
        {
            var page = new Panel();
            page.AutoScroll = true;
            page.RightToLeft = RightToLeft.Yes;
            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Top;
            table.AutoSize = true;
            table.RightToLeft = RightToLeft.Yes;
            table.Padding = new Padding(24, 18, 24, 28);
            table.ColumnCount = 3;
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));

            int row = 0;
            AddSection(table, ref row, "سرور Kitsu (SSH Key + sudo)");
            _sshHost = AddTextRow(table, ref row, "آدرس/IP سرور", null, false);
            _sshPort = AddNumberRow(table, ref row, "پورت SSH", 1, 65535);
            _sshUser = AddTextRow(table, ref row, "نام کاربری SSH", null, false);
            _sshKey = AddTextRow(table, ref row, "کلید خصوصی SSH", "انتخاب", false);
            table.GetControlFromPosition(2, row - 1).Click += delegate { BrowseFile(_sshKey, "All files|*.*"); };
            _sudoPassword = AddTextRow(table, ref row, "رمز sudo", null, true);

            AddSection(table, ref row, "ذخیره‌ساز S3 — همه Bucketها");
            _s3Endpoint = AddTextRow(table, ref row, "Endpoint", null, false);
            _s3Region = AddTextRow(table, ref row, "Region", null, false);
            _s3CaCertificate = AddTextRow(table, ref row, "گواهی CA سفارشی", "انتخاب", false);
            table.GetControlFromPosition(2, row - 1).Click += delegate
            {
                BrowseFile(_s3CaCertificate, "PEM certificate|*.pem;*.crt;*.cer|All files|*.*");
            };
            _s3Access = AddTextRow(table, ref row, "Access Key", null, true);
            _s3Secret = AddTextRow(table, ref row, "Secret Key", null, true);
            _s3PathStyle = new CheckBox();
            _s3PathStyle.Text = "Force path-style";
            _s3PathStyle.Dock = DockStyle.Fill;
            _s3PathStyle.RightToLeft = RightToLeft.Yes;
            _s3PathStyle.CheckAlign = ContentAlignment.MiddleLeft;
            _s3PathStyle.TextAlign = ContentAlignment.MiddleLeft;
            AddControlRow(table, ref row, "سازگاری Endpoint", _s3PathStyle, null);

            AddSection(table, ref row, "ذخیره‌سازی محلی");
            _localRoot = AddTextRow(table, ref row, "مسیر اصلی بکاپ", "انتخاب", false);
            table.GetControlFromPosition(2, row - 1).Click += delegate { BrowseFolder(_localRoot); };
            _rclonePath = AddTextRow(table, ref row, "مسیر rclone.exe", "انتخاب", false);
            table.GetControlFromPosition(2, row - 1).Click += delegate { BrowseFile(_rclonePath, "rclone.exe|rclone.exe|Executable|*.exe"); };

            var buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.AutoSize = false;
            buttons.WrapContents = false;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.RightToLeft = RightToLeft.No;
            buttons.Padding = new Padding(0, 10, 0, 8);
            Button save = new Button { Text = "ذخیره تنظیمات", Width = 145, Height = 38 };
            Button installHelper = new Button { Text = "نصب/آپدیت Helper", Width = 155, Height = 38 };
            Button testSsh = new Button { Text = "تست سرور و Helper", Width = 155, Height = 38 };
            Button testS3 = new Button { Text = "تست S3", Width = 115, Height = 38 };
            save.RightToLeft = installHelper.RightToLeft = testSsh.RightToLeft = testS3.RightToLeft = RightToLeft.Yes;
            save.Click += delegate { SaveControlsToSettings(true); };
            installHelper.Click += async delegate
            {
                try
                {
                    SaveControlsToSettings(false);
                    await RunBusy("در حال نصب Helper روی سرور...", async delegate
                    {
                        string output = await BackupEngine.InstallServerHelperAsync(_settings, Log);
                        Log(output);
                    });
                    RtlMessageBox.Show(this, "Helper با موفقیت نصب و آزمایش شد.", "موفق", MessageBoxIcon.Information);
                }
                catch (Exception ex) { HandleFailure(ex); }
            };
            testSsh.Click += async delegate
            {
                try
                {
                    SaveControlsToSettings(false);
                    await RunBusy("در حال تست سرور...", async delegate
                    {
                        string output = await BackupEngine.TestServerAsync(_settings, Log);
                        Log(output);
                    });
                    RtlMessageBox.Show(this, "سرور و Helper آماده‌اند.", "موفق", MessageBoxIcon.Information);
                }
                catch (Exception ex) { HandleFailure(ex); }
            };
            testS3.Click += async delegate
            {
                try
                {
                    SaveControlsToSettings(false);
                    await RunBusy("در حال تست S3...", async delegate
                    {
                        string output = await BackupEngine.TestS3Async(_settings, Log);
                        Log(output);
                    });
                    RtlMessageBox.Show(this, "اتصال S3 موفق بود.", "موفق", MessageBoxIcon.Information);
                }
                catch (Exception ex) { HandleFailure(ex); }
            };
            buttons.Controls.Add(save);
            buttons.Controls.Add(installHelper);
            buttons.Controls.Add(testSsh);
            buttons.Controls.Add(testS3);
            AddWideRow(table, ref row, buttons, 68);

            var note = new Label();
            note.Dock = DockStyle.Fill;
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.Text = "رمز sudo و کلیدهای S3 با Windows DPAPI ذخیره می‌شوند. همه Bucketهای قابل‌دسترسی جداگانه بکاپ گرفته می‌شوند. فایل‌های بکاپ سرور رمزنگاری نمی‌شوند؛ مسیر محلی را با BitLocker محافظت کنید. اعتبارسنجی TLS در S3 هیچ‌وقت غیرفعال نمی‌شود.";
            note.ForeColor = UiTheme.Muted;
            note.Tag = "muted";
            note.RightToLeft = RightToLeft.Yes;
            AddWideRow(table, ref row, note, 64);

            page.Controls.Add(table);
            _tabs.AddPage("تنظیمات", page);
        }

        private void BuildLogTab()
        {
            var page = new Panel();
            page.Padding = new Padding(24, 18, 24, 24);
            var help = new Label();
            help.Text = "راهنمای گزارش: جزئیات اتصال، دانلود و خطاها در این صفحه ثبت می‌شود. هنگام درخواست پشتیبانی، متن خطا را از همین‌جا کپی کنید.";
            help.Dock = DockStyle.Top;
            help.Height = 52;
            help.TextAlign = ContentAlignment.MiddleLeft;
            help.ForeColor = UiTheme.Muted;
            help.Tag = "muted";
            _log.Dock = DockStyle.Fill;
            _log.ReadOnly = true;
            _log.WordWrap = false;
            _log.Font = new Font("Consolas", 9F);
            _log.RightToLeft = RightToLeft.No;
            page.Controls.Add(_log);
            page.Controls.Add(help);
            _tabs.AddPage("گزارش", page);
        }

        private void BuildGuideTab()
        {
            var page = new Panel();
            page.AutoScroll = true;
            page.RightToLeft = RightToLeft.Yes;
            var table = new TableLayoutPanel();
            table.Dock = DockStyle.Top;
            table.AutoSize = true;
            table.RightToLeft = RightToLeft.Yes;
            table.ColumnCount = 1;
            table.Padding = new Padding(28, 20, 28, 32);
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            var heading = new Label();
            heading.Text = "راهنمای استفاده";
            heading.Font = UiTheme.Font(20F, FontStyle.Bold);
            heading.Dock = DockStyle.Fill;
            heading.TextAlign = ContentAlignment.MiddleLeft;
            heading.RightToLeft = RightToLeft.Yes;
            AddGuideRow(table, ref row, heading, 64);

            AddGuideCard(table, ref row, "۱. شروع سریع",
                "ابتدا وارد «تنظیمات» شوید و اطلاعات SSH، مشخصات S3 و مسیر ذخیره‌سازی محلی را وارد کنید. سپس «ذخیره تنظیمات» را بزنید. بعد دکمه‌های «تست سرور و Helper» و «تست S3» را اجرا کنید. فقط وقتی پیام موفقیت هر دو تست را دیدید، بکاپ‌گیری را شروع کنید.", 132);

            AddGuideCard(table, ref row, "۲. Snapshot قابل‌حمل سرور",
                "این گزینه کامل‌ترین نسخه برای ساخت دوباره سرور روی یک پروایدر جدید است. دیتابیس، تنظیمات Kitsu، Docker، Nginx، فایروال و فهرست بسته‌های نصب‌شده را ذخیره می‌کند. ماهی یک‌بار و همچنین قبل از هر تغییر مهم در سرور آن را اجرا کنید.", 132);

            AddGuideCard(table, ref row, "۳. Backup سرور Kitsu",
                "این گزینه برای بکاپ‌های منظم و سریع‌تر است و اطلاعات اصلی Kitsu، دیتابیس و تنظیمات موردنیاز را دریافت می‌کند. پیشنهاد: هفته‌ای یک‌بار یا بعد از ورود اطلاعات مهم آن را اجرا کنید. تا پایان عملیات، برنامه و اینترنت را باز نگه دارید.", 132);

            AddGuideCard(table, ref row, "۴. Backup محلی S3",
                "این گزینه همه Bucketهای قابل‌دسترسی را شناسایی می‌کند و هرکدام را با نام اصلی در پوشه‌ای جدا روی کامپیوتر کپی می‌کند؛ بنابراین Bucketهای pictures، movies و files کیتسو همگی محفوظ می‌مانند. اجرای اول ممکن است طولانی باشد و اجراهای بعدی برای فایل‌های تکراری از Hard-link استفاده می‌کنند. اگر تست S3 خطای TLS داد، مسیر گواهی CA سفارشی را بررسی کنید.", 166);

            AddGuideCard(table, ref row, "۵. بازیابی پس از ازبین‌رفتن سرورها",
                "روی سرور جدید Ubuntu 24.04 نصب کنید، سپس فایل Snapshot را باز کرده و تنظیمات و دیتابیس را مطابق پوشه‌های داخل آرشیو برگردانید. هر پوشه داخل data بکاپ S3 را به Bucket هم‌نام خودش منتقل کنید و Endpoint، Region و کلیدهای جدید را در Kitsu ثبت کنید. فایل manifest.json و فایل SHA256 کنار هر بکاپ برای بررسی سالم‌بودن آن هستند.", 166);

            AddGuideCard(table, ref row, "۶. برنامه نگهداری پیشنهادی",
                "Backup سرور: هفتگی  |  Snapshot کامل: ماهانه و قبل از تغییرات مهم  |  Backup S3: هفتگی یا بعد از آپلودهای سنگین. حداقل دو کپی جدا نگه دارید: یکی روی کامپیوتر و یکی روی هارد اکسترنال. هرگز آخرین نسخه سالم را قبل از آزمایش نسخه جدید حذف نکنید.", 132);

            var warning = new Label();
            warning.Text = "نکته مهم: بکاپ زمانی قابل اعتماد است که بتوان آن را بازیابی کرد. هر چند ماه یک‌بار یک بازیابی آزمایشی روی سرور موقت انجام دهید.";
            warning.Dock = DockStyle.Fill;
            warning.TextAlign = ContentAlignment.MiddleLeft;
            warning.RightToLeft = RightToLeft.Yes;
            warning.ForeColor = UiTheme.Success;
            warning.Tag = "success";
            AddGuideRow(table, ref row, warning, 64);

            page.Controls.Add(table);
            _tabs.AddPage("راهنمای استفاده", page);
        }

        private static void AddGuideCard(TableLayoutPanel table, ref int row,
            string title, string body, int height)
        {
            var card = new TableLayoutPanel();
            card.Tag = "card";
            card.Dock = DockStyle.Fill;
            card.RightToLeft = RightToLeft.Yes;
            card.Margin = new Padding(4, 6, 4, 6);
            card.Padding = new Padding(18, 10, 18, 12);
            card.ColumnCount = 1;
            card.RowCount = 2;
            card.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.Font = UiTheme.Font(12F, FontStyle.Bold);
            titleLabel.ForeColor = UiTheme.Accent;
            titleLabel.Tag = "accent";
            titleLabel.Dock = DockStyle.Fill;
            titleLabel.TextAlign = ContentAlignment.MiddleLeft;
            titleLabel.RightToLeft = RightToLeft.Yes;

            var bodyLabel = new Label();
            bodyLabel.Text = body;
            bodyLabel.Dock = DockStyle.Fill;
            bodyLabel.TextAlign = ContentAlignment.TopLeft;
            bodyLabel.RightToLeft = RightToLeft.Yes;
            bodyLabel.ForeColor = UiTheme.Text;

            card.Controls.Add(titleLabel, 0, 0);
            card.Controls.Add(bodyLabel, 0, 1);
            AddGuideRow(table, ref row, card, height);
        }

        private static void AddGuideRow(TableLayoutPanel table, ref int row,
            Control control, int height)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            table.Controls.Add(control, 0, row);
            row++;
        }

        private static void AddSection(TableLayoutPanel table, ref int row, string title)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            var label = new Label();
            label.Text = title;
            label.Font = UiTheme.Font(12F, FontStyle.Bold);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.RightToLeft = RightToLeft.Yes;
            label.Padding = new Padding(0, 12, 4, 0);
            table.Controls.Add(label, 0, row);
            table.SetColumnSpan(label, 3);
            row++;
        }

        private static TextBox AddTextRow(TableLayoutPanel table, ref int row, string label, string buttonText, bool password)
        {
            var box = new TextBox();
            box.Dock = DockStyle.Fill;
            box.RightToLeft = RightToLeft.Yes;
            // HorizontalAlignment is mirrored by WinForms in RTL mode.
            box.TextAlign = HorizontalAlignment.Left;
            if (password) box.UseSystemPasswordChar = true;
            AddControlRow(table, ref row, label, box, buttonText);
            return box;
        }

        private static NumericUpDown AddNumberRow(TableLayoutPanel table, ref int row, string label, decimal min, decimal max)
        {
            var box = new NumericUpDown();
            box.Minimum = min;
            box.Maximum = max;
            box.Dock = DockStyle.Fill;
            box.RightToLeft = RightToLeft.Yes;
            box.TextAlign = HorizontalAlignment.Left;
            AddControlRow(table, ref row, label, box, null);
            return box;
        }

        private static void AddControlRow(TableLayoutPanel table, ref int row, string labelText, Control control, string buttonText)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            var label = new Label();
            label.Text = labelText;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.RightToLeft = RightToLeft.Yes;
            table.Controls.Add(label, 0, row);
            control.Margin = new Padding(4, 5, 4, 5);
            table.Controls.Add(control, 1, row);
            if (buttonText != null)
            {
                var button = new Button();
                button.Text = buttonText;
                button.Dock = DockStyle.Fill;
                button.RightToLeft = RightToLeft.Yes;
                table.Controls.Add(button, 2, row);
            }
            else
            {
                var spacer = new Label();
                table.Controls.Add(spacer, 2, row);
            }
            row++;
        }

        private static void AddWideRow(TableLayoutPanel table, ref int row, Control control, int height)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Margin = new Padding(4, 3, 4, 3);
            table.Controls.Add(control, 0, row);
            table.SetColumnSpan(control, 3);
            row++;
        }

        private static Control CreateStoryEcoFooter()
        {
            var footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.ColumnCount = 1;
            footer.RowCount = 3;
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            var storyEcoLogo = new PictureBox();
            storyEcoLogo.Image = UiTheme.StoryEcoLogo;
            storyEcoLogo.SizeMode = PictureBoxSizeMode.Zoom;
            storyEcoLogo.Dock = DockStyle.Fill;
            storyEcoLogo.Margin = new Padding(280, 2, 280, 0);

            var credit = new Label();
            credit.Text = "تهیه شده توسط StoryEco.com";
            credit.Dock = DockStyle.Fill;
            credit.TextAlign = ContentAlignment.TopCenter;
            credit.ForeColor = UiTheme.Muted;
            credit.Tag = "muted";
            credit.RightToLeft = RightToLeft.Yes;

            footer.Controls.Add(new Panel(), 0, 0);
            footer.Controls.Add(storyEcoLogo, 0, 1);
            footer.Controls.Add(credit, 0, 2);
            return footer;
        }

        private void BrowseFolder(TextBox target)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = Directory.Exists(target.Text) ? target.Text : "";
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
            }
        }

        private void BrowseFile(TextBox target, string filter)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = filter;
                if (File.Exists(target.Text)) dialog.FileName = target.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
            }
        }

        private void LoadSettingsIntoControls()
        {
            _sshHost.Text = _settings.SshHost;
            _sshPort.Value = Math.Max(_sshPort.Minimum, Math.Min(_sshPort.Maximum, _settings.SshPort));
            _sshUser.Text = _settings.SshUser;
            _sshKey.Text = _settings.SshKeyPath;
            _sudoPassword.Text = _settings.SudoPassword;
            _s3Endpoint.Text = _settings.S3Endpoint;
            _s3Region.Text = _settings.S3Region;
            _s3CaCertificate.Text = _settings.S3CaCertificatePath;
            _s3Access.Text = _settings.S3AccessKey;
            _s3Secret.Text = _settings.S3SecretKey;
            _s3PathStyle.Checked = _settings.S3ForcePathStyle;
            _localRoot.Text = _settings.LocalRoot;
            _rclonePath.Text = _settings.RclonePath;
        }

        private void SaveControlsToSettings(bool showMessage)
        {
            _settings.SshHost = _sshHost.Text.Trim();
            _settings.SshPort = Decimal.ToInt32(_sshPort.Value);
            _settings.SshUser = _sshUser.Text.Trim();
            _settings.SshKeyPath = _sshKey.Text.Trim();
            _settings.SudoPassword = _sudoPassword.Text;
            _settings.S3Endpoint = _s3Endpoint.Text.Trim().TrimEnd('/');
            _settings.S3Region = _s3Region.Text.Trim();
            _settings.S3Bucket = "*";
            _settings.S3CaCertificatePath = _s3CaCertificate.Text.Trim();
            _settings.TlsSettingsVersion = 1;
            _settings.S3AccessKey = _s3Access.Text.Trim();
            _settings.S3SecretKey = _s3Secret.Text;
            _settings.S3ForcePathStyle = _s3PathStyle.Checked;
            _settings.LocalRoot = _localRoot.Text.Trim();
            _settings.RclonePath = _rclonePath.Text.Trim();
            SettingsStore.Save(_settings);
            if (showMessage)
                RtlMessageBox.Show(this, "تنظیمات ذخیره شد.", "موفق", MessageBoxIcon.Information);
        }

        private async Task RunServerBackup(string mode)
        {
            SaveControlsToSettings(false);
            string title = mode == "snapshot" ? "Snapshot سرور" : "Backup سرور";
            await RunBusy("در حال تهیه " + title + "...", async delegate
            {
                string path = await BackupEngine.RunServerBackupAsync(_settings, mode, Log);
                Log("Completed: " + path);
                BeginInvoke((Action)delegate
                {
                    RtlMessageBox.Show(this, title + " با موفقیت ذخیره شد:\n" + path,
                        "موفق", MessageBoxIcon.Information);
                });
            });
        }

        private async Task RunS3Backup()
        {
            SaveControlsToSettings(false);
            await RunBusy("در حال تهیه Backup محلی S3...", async delegate
            {
                string path = await BackupEngine.RunS3BackupAsync(_settings, Log);
                Log("Completed: " + path);
                BeginInvoke((Action)delegate
                {
                    RtlMessageBox.Show(this, "Backup محلی S3 با موفقیت ذخیره شد:\n" + path,
                        "موفق", MessageBoxIcon.Information);
                });
            });
        }

        private async Task RunBusy(string status, Func<Task> action)
        {
            SetBusy(true, status);
            try { await action(); }
            finally { SetBusy(false, "آماده"); }
        }

        private void SetBusy(bool busy, string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetBusy(busy, status)));
                return;
            }
            foreach (Button button in _actionButtons) button.Enabled = !busy;
            _progress.MarqueeAnimationSpeed = busy ? 30 : 0;
            _status.Text = status;
        }

        private void Log(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => Log(message)));
                return;
            }
            _log.AppendText(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private void HandleFailure(Exception ex)
        {
            SetBusy(false, "خطا");
            Log("ERROR: " + ex);
            _tabs.SelectedIndex = 2;
            RtlMessageBox.Show(this, ex.Message, "عملیات ناموفق", MessageBoxIcon.Error);
        }

    }

    internal static class BackupEngine
    {
        private const string HelperPath = "/usr/local/sbin/storyeco-backup-export";

        public static async Task<string> TestServerAsync(AppSettings settings, Action<string> log)
        {
            ValidateServer(settings);
            return await Task.Run(() => RunSshText(settings,
                "sudo -S -p '' " + HelperPath + " self-test", log));
        }

        public static async Task<string> InstallServerHelperAsync(AppSettings settings, Action<string> log)
        {
            ValidateServer(settings);
            return await Task.Run(() =>
            {
                string localHelper = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "server", "storyeco-backup-export");
                if (!File.Exists(localHelper))
                    throw new FileNotFoundException("Bundled server helper was not found.", localHelper);
                string remoteTemporary = "/tmp/storyeco-backup-export-" + Guid.NewGuid().ToString("N");
                string scpArguments = "-i " + Quote(settings.SshKeyPath) +
                    " -P " + settings.SshPort +
                    " -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=yes" +
                    " -o ConnectTimeout=20 " + Quote(localHelper) + " " +
                    Quote(settings.SshUser + "@" + settings.SshHost + ":" + remoteTemporary);
                log("Uploading the constrained backup helper...");
                RunProcessText("scp.exe", scpArguments, log, null);
                string privileged = "install -o root -g root -m 0755 " +
                    remoteTemporary + " " + HelperPath + " && " + HelperPath + " self-test";
                string command = "sudo -S -p '' sh -c '" + privileged +
                    "' && rm -f " + remoteTemporary;
                return RunSshText(settings, command, log);
            });
        }

        public static async Task<string> RunServerBackupAsync(AppSettings settings, string mode, Action<string> log)
        {
            ValidateServer(settings);
            if (mode != "snapshot" && mode != "backup") throw new ArgumentException("Invalid backup mode.");
            return await Task.Run(() => RunServerBackup(settings, mode, log));
        }

        private static string RunServerBackup(AppSettings settings, string mode, Action<string> log)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string category = mode == "snapshot" ? "ServerSnapshot" : "ServerBackup";
            string runDirectory = Path.Combine(settings.LocalRoot, category, timestamp);
            Directory.CreateDirectory(runDirectory);
            string partialPath = Path.Combine(runDirectory, category + ".tar.gz.part");
            string finalPath = Path.Combine(runDirectory, category + ".tar.gz");
            string logPath = Path.Combine(runDirectory, "run.log");
            var error = new StringBuilder();

            log("Connecting to " + settings.SshUser + "@" + settings.SshHost + "...");
            var start = new ProcessStartInfo();
            start.FileName = "ssh.exe";
            start.Arguments = BuildSshArguments(settings,
                "sudo -S -p '' " + HelperPath + " " + mode);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = start;
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null)
                        {
                            lock (error) error.AppendLine(e.Data);
                            log(e.Data);
                        }
                    };
                    if (!process.Start()) throw new InvalidOperationException("Could not start ssh.exe.");
                    process.BeginErrorReadLine();
                    process.StandardInput.WriteLine(settings.SudoPassword);
                    process.StandardInput.Flush();
                    process.StandardInput.Close();

                    log("Streaming the server archive to local storage...");
                    using (FileStream output = File.Create(partialPath))
                    {
                        process.StandardOutput.BaseStream.CopyTo(output);
                        output.Flush(true);
                    }
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("Server backup failed.\n" + error.ToString().Trim());
                }

                File.Move(partialPath, finalPath);
                string sha256 = ComputeSha256(finalPath);
                File.WriteAllText(finalPath + ".sha256", sha256 + "  " + Path.GetFileName(finalPath) + Environment.NewLine);
                File.WriteAllText(logPath, error.ToString());
                WriteManifest(runDirectory, category, "success", settings.SshHost, finalPath, sha256, null);
                return runDirectory;
            }
            catch (Exception ex)
            {
                TryDelete(partialPath);
                File.WriteAllText(logPath, error.ToString() + Environment.NewLine + ex);
                WriteManifest(runDirectory, category, "failed", settings.SshHost, null, null, ex.Message);
                throw;
            }
        }

        public static async Task<string> TestS3Async(AppSettings settings, Action<string> log)
        {
            ValidateS3(settings);
            return await Task.Run(() =>
            {
                string config = CreateTemporaryRcloneConfig(settings);
                try
                {
                    List<string> buckets = DiscoverS3Buckets(settings, config, log);
                    foreach (string bucket in buckets)
                    {
                        RunProcessText(settings.RclonePath,
                            "lsd " + Quote("source:" + bucket) +
                            " --max-depth 1 --config " + Quote(config) +
                            GetRcloneTlsArguments(settings), log, null);
                    }
                    return "Secure S3 connection succeeded. Accessible buckets (" +
                        buckets.Count + "): " + String.Join(", ", buckets);
                }
                finally { TryDelete(config); }
            });
        }

        public static async Task<string> RunS3BackupAsync(AppSettings settings, Action<string> log)
        {
            ValidateS3(settings);
            if (String.IsNullOrWhiteSpace(settings.LocalRoot)) throw new InvalidOperationException("Local backup path is empty.");
            return await Task.Run(() => RunS3Backup(settings, log));
        }

        private static string RunS3Backup(AppSettings settings, Action<string> log)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string root = Path.Combine(settings.LocalRoot, "S3");
            string runDirectory = Path.Combine(root, timestamp);
            string dataDirectory = Path.Combine(runDirectory, "data");
            string logPath = Path.Combine(runDirectory, "rclone.log");
            Directory.CreateDirectory(dataDirectory);

            string previous = FindLatestSuccessfulS3(root, runDirectory);
            if (previous != null)
            {
                log("Creating an NTFS hard-link snapshot from: " + previous);
                CloneTreeWithHardLinks(Path.Combine(previous, "data"), dataDirectory, log);
            }

            string config = CreateTemporaryRcloneConfig(settings);
            try
            {
                List<string> buckets = DiscoverS3Buckets(settings, config, log);
                RemoveStaleBucketDirectories(dataDirectory, buckets, log);

                string inventoryDirectory = Path.Combine(runDirectory, "s3-inventory");
                Directory.CreateDirectory(inventoryDirectory);
                var inventory = new S3Inventory();
                inventory.FormatVersion = 2;
                inventory.CreatedAt = DateTimeOffset.Now.ToString("O");
                inventory.Endpoint = settings.S3Endpoint;
                inventory.Region = settings.S3Region;

                foreach (string bucket in buckets)
                {
                    log("Synchronizing S3 bucket: " + bucket);
                    string bucketDirectory = Path.Combine(dataDirectory, bucket);
                    Directory.CreateDirectory(bucketDirectory);
                    string arguments = "sync " + Quote("source:" + bucket) + " " + Quote(bucketDirectory) +
                        " --config " + Quote(config) + GetRcloneTlsArguments(settings) +
                        " --fast-list --transfers 4 --checkers 8 --stats 15s --stats-one-line" +
                        " --log-level INFO --log-file " + Quote(logPath);
                    RunProcessText(settings.RclonePath, arguments, log, logPath);

                    string bucketInventoryPath = Path.Combine(inventoryDirectory, bucket + ".json");
                    RunProcessToFile(settings.RclonePath,
                        "lsjson " + Quote("source:" + bucket) +
                        " --recursive --hash --config " + Quote(config) +
                        GetRcloneTlsArguments(settings), bucketInventoryPath, log);

                    long bucketBytes;
                    long bucketFiles;
                    GetDirectoryStats(bucketDirectory, out bucketFiles, out bucketBytes);
                    inventory.Buckets.Add(new S3BucketInventory
                    {
                        Name = bucket,
                        FileCount = bucketFiles,
                        TotalBytes = bucketBytes,
                        InventoryFile = "s3-inventory/" + bucket + ".json"
                    });
                }

                string inventoryPath = Path.Combine(runDirectory, "s3-inventory.json");
                WriteS3Inventory(inventoryPath, inventory);
                long fileCount = inventory.Buckets.Sum(item => item.FileCount);
                long totalBytes = inventory.Buckets.Sum(item => item.TotalBytes);
                string extra = "buckets=" + inventory.Buckets.Count + ";files=" + fileCount + ";bytes=" + totalBytes;
                WriteManifest(runDirectory, "S3", "success", settings.S3Endpoint,
                    dataDirectory, null, extra);
                File.WriteAllText(Path.Combine(runDirectory, "SUCCESS"), DateTimeOffset.Now.ToString("O"));
                return runDirectory;
            }
            catch (Exception ex)
            {
                WriteManifest(runDirectory, "S3", "failed", settings.S3Endpoint,
                    dataDirectory, null, ex.Message);
                throw;
            }
            finally { TryDelete(config); }
        }

        private static void ValidateServer(AppSettings settings)
        {
            if (String.IsNullOrWhiteSpace(settings.SshHost)) throw new InvalidOperationException("SSH host is empty.");
            if (String.IsNullOrWhiteSpace(settings.SshUser)) throw new InvalidOperationException("SSH username is empty.");
            if (!File.Exists(settings.SshKeyPath)) throw new FileNotFoundException("SSH private key was not found.", settings.SshKeyPath);
            if (String.IsNullOrEmpty(settings.SudoPassword)) throw new InvalidOperationException("sudo password is empty.");
            if (String.IsNullOrWhiteSpace(settings.LocalRoot)) throw new InvalidOperationException("Local backup path is empty.");
        }

        private static void ValidateS3(AppSettings settings)
        {
            if (!File.Exists(settings.RclonePath)) throw new FileNotFoundException("rclone.exe was not found.", settings.RclonePath);
            if (!Uri.IsWellFormedUriString(settings.S3Endpoint, UriKind.Absolute) || !settings.S3Endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("S3 endpoint must be a valid HTTPS URL.");
            if (String.IsNullOrWhiteSpace(settings.S3AccessKey) || String.IsNullOrWhiteSpace(settings.S3SecretKey))
                throw new InvalidOperationException("S3 credentials are incomplete.");
            if (!String.IsNullOrWhiteSpace(settings.S3CaCertificatePath) && !File.Exists(settings.S3CaCertificatePath))
                throw new FileNotFoundException("S3 CA certificate was not found.", settings.S3CaCertificatePath);
        }

        private static List<string> DiscoverS3Buckets(AppSettings settings, string config, Action<string> log)
        {
            string output = RunProcessText(settings.RclonePath,
                "lsf " + Quote("source:") +
                " --dirs-only --format p --max-depth 1 --config " + Quote(config) +
                GetRcloneTlsArguments(settings), log, null);
            List<string> buckets = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim().TrimEnd('/'))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (buckets.Count == 0)
                throw new InvalidOperationException("No accessible S3 buckets were found.");
            foreach (string bucket in buckets) ValidateBucketName(bucket);
            return buckets;
        }

        private static void ValidateBucketName(string bucket)
        {
            if (bucket == "." || bucket == ".." || bucket.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0)
                throw new InvalidOperationException("S3 returned an unsafe bucket name: " + bucket);
        }

        private static string GetRcloneTlsArguments(AppSettings settings)
        {
            if (String.IsNullOrWhiteSpace(settings.S3CaCertificatePath)) return "";
            return " --ca-cert " + Quote(settings.S3CaCertificatePath);
        }

        private static void RemoveStaleBucketDirectories(string dataDirectory,
            IEnumerable<string> visibleBuckets, Action<string> log)
        {
            var visible = new HashSet<string>(visibleBuckets, StringComparer.OrdinalIgnoreCase);
            foreach (string directory in Directory.GetDirectories(dataDirectory))
            {
                string name = Path.GetFileName(directory);
                if (!visible.Contains(name))
                {
                    log("Removing bucket missing from current S3 snapshot: " + name);
                    Directory.Delete(directory, true);
                }
            }
        }

        private static void WriteS3Inventory(string path, S3Inventory inventory)
        {
            using (FileStream stream = File.Create(path))
            {
                var serializer = new DataContractJsonSerializer(typeof(S3Inventory));
                serializer.WriteObject(stream, inventory);
            }
        }

        private static string RunSshText(AppSettings settings, string command, Action<string> log)
        {
            return RunProcessText("ssh.exe", BuildSshArguments(settings, command), log, null,
                settings.SudoPassword + Environment.NewLine);
        }

        private static string BuildSshArguments(AppSettings settings, string command)
        {
            return "-i " + Quote(settings.SshKeyPath) +
                " -p " + settings.SshPort +
                " -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=yes" +
                " -o ConnectTimeout=20 -o ServerAliveInterval=30" +
                " " + Quote(settings.SshUser + "@" + settings.SshHost) +
                " " + Quote(command);
        }

        private static string CreateTemporaryRcloneConfig(AppSettings settings)
        {
            RejectNewlines(settings.S3Endpoint);
            RejectNewlines(settings.S3Region);
            RejectNewlines(settings.S3AccessKey);
            RejectNewlines(settings.S3SecretKey);
            string path = Path.Combine(Path.GetTempPath(), "kitsu-rclone-" + Guid.NewGuid().ToString("N") + ".conf");
            var text = new StringBuilder();
            text.AppendLine("[source]");
            text.AppendLine("type = s3");
            text.AppendLine("provider = Other");
            text.AppendLine("env_auth = false");
            text.AppendLine("access_key_id = " + settings.S3AccessKey);
            text.AppendLine("secret_access_key = " + settings.S3SecretKey);
            text.AppendLine("endpoint = " + settings.S3Endpoint);
            text.AppendLine("region = " + settings.S3Region);
            text.AppendLine("force_path_style = " + (settings.S3ForcePathStyle ? "true" : "false"));
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static void RejectNewlines(string value)
        {
            if (value != null && (value.Contains("\r") || value.Contains("\n")))
                throw new InvalidOperationException("A configuration value contains an invalid newline.");
        }

        private static string RunProcessText(string fileName, string arguments, Action<string> log, string logPath, string standardInput = null)
        {
            var start = new ProcessStartInfo(fileName, arguments);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            start.RedirectStandardInput = standardInput != null;
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Could not start: " + fileName);
                if (standardInput != null)
                {
                    process.StandardInput.Write(standardInput);
                    process.StandardInput.Close();
                }
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!String.IsNullOrWhiteSpace(output)) log(output.Trim());
                if (!String.IsNullOrWhiteSpace(error)) log(error.Trim());
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(Path.GetFileName(fileName) + " failed (" + process.ExitCode + ").\n" + error.Trim());
                return output;
            }
        }

        private static void RunProcessToFile(string fileName, string arguments, string outputPath, Action<string> log)
        {
            var start = new ProcessStartInfo(fileName, arguments);
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
            using (Process process = Process.Start(start))
            using (FileStream output = File.Create(outputPath))
            {
                if (process == null) throw new InvalidOperationException("Could not start: " + fileName);
                process.StandardOutput.BaseStream.CopyTo(output);
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!String.IsNullOrWhiteSpace(error)) log(error.Trim());
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(Path.GetFileName(fileName) + " failed (" + process.ExitCode + ").\n" + error.Trim());
            }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static void WriteManifest(string directory, string type, string status,
            string source, string artifact, string sha256, string details)
        {
            var manifest = new BackupManifest();
            manifest.FormatVersion = 1;
            manifest.Type = type;
            manifest.Status = status;
            manifest.CreatedAt = DateTimeOffset.Now.ToString("O");
            manifest.Source = source;
            manifest.Artifact = artifact;
            manifest.Sha256 = sha256;
            manifest.Details = details;
            using (FileStream stream = File.Create(Path.Combine(directory, "manifest.json")))
            {
                var serializer = new DataContractJsonSerializer(typeof(BackupManifest));
                serializer.WriteObject(stream, manifest);
            }
        }

        private static string FindLatestSuccessfulS3(string root, string current)
        {
            if (!Directory.Exists(root)) return null;
            return Directory.GetDirectories(root)
                .Where(path => !String.Equals(path, current, StringComparison.OrdinalIgnoreCase))
                .Where(path => File.Exists(Path.Combine(path, "SUCCESS")) && Directory.Exists(Path.Combine(path, "data")))
                .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static void CloneTreeWithHardLinks(string source, string destination, Action<string> log)
        {
            if (!Directory.Exists(source)) return;
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string relative = directory.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }
            long linked = 0;
            long copied = 0;
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar);
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (CreateHardLink(target, file, IntPtr.Zero)) linked++;
                else
                {
                    File.Copy(file, target, false);
                    copied++;
                }
            }
            log("Reused files with hard-links: " + linked + "; copied fallback: " + copied);
        }

        private static void GetDirectoryStats(string path, out long fileCount, out long totalBytes)
        {
            fileCount = 0;
            totalBytes = 0;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                fileCount++;
                totalBytes += new FileInfo(file).Length;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);
    }

    [DataContract]
    internal sealed class BackupManifest
    {
        [DataMember] public int FormatVersion;
        [DataMember] public string Type;
        [DataMember] public string Status;
        [DataMember] public string CreatedAt;
        [DataMember] public string Source;
        [DataMember] public string Artifact;
        [DataMember] public string Sha256;
        [DataMember] public string Details;
    }

    [DataContract]
    internal sealed class S3Inventory
    {
        public S3Inventory()
        {
            Buckets = new List<S3BucketInventory>();
        }

        [DataMember] public int FormatVersion;
        [DataMember] public string CreatedAt;
        [DataMember] public string Endpoint;
        [DataMember] public string Region;
        [DataMember] public List<S3BucketInventory> Buckets;
    }

    [DataContract]
    internal sealed class S3BucketInventory
    {
        [DataMember] public string Name;
        [DataMember] public long FileCount;
        [DataMember] public long TotalBytes;
        [DataMember] public string InventoryFile;
    }

    internal static class PortableArchive
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("KLB1");
        private const int Iterations = 300000;
        private const int HeaderSize = 4 + 4 + 16 + 16;
        private const int TagSize = 32;

        public static void Encrypt(Stream input, string outputPath, string password)
        {
            byte[] salt = RandomBytes(16);
            byte[] iv = RandomBytes(16);
            byte[] keyMaterial;
            using (var derive = new Rfc2898DeriveBytes(password, salt, Iterations, HashAlgorithmName.SHA256))
                keyMaterial = derive.GetBytes(64);
            byte[] encryptionKey = keyMaterial.Take(32).ToArray();
            byte[] macKey = keyMaterial.Skip(32).Take(32).ToArray();
            byte[] header = BuildHeader(salt, iv);

            try
            {
                using (FileStream output = File.Create(outputPath))
                using (var hmac = new HMACSHA256(macKey))
                using (var hmacStream = new HmacWriteStream(output, hmac))
                using (var aes = Aes.Create())
                {
                    hmacStream.Write(header, 0, header.Length);
                    aes.KeySize = 256;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    aes.Key = encryptionKey;
                    aes.IV = iv;
                    using (var crypto = new CryptoStream(hmacStream, aes.CreateEncryptor(), CryptoStreamMode.Write))
                    {
                        input.CopyTo(crypto);
                        crypto.FlushFinalBlock();
                    }
                    hmacStream.FinalizeHash();
                    byte[] tag = hmac.Hash;
                    output.Write(tag, 0, tag.Length);
                    output.Flush(true);
                }
            }
            finally
            {
                Array.Clear(keyMaterial, 0, keyMaterial.Length);
                Array.Clear(encryptionKey, 0, encryptionKey.Length);
                Array.Clear(macKey, 0, macKey.Length);
            }
        }

        public static void Decrypt(string inputPath, string outputPath, string password)
        {
            string partial = outputPath + ".part";
            try
            {
                using (FileStream input = File.OpenRead(inputPath))
                {
                    if (input.Length <= HeaderSize + TagSize) throw new InvalidDataException("Backup file is incomplete.");
                    byte[] header = ReadExactly(input, HeaderSize);
                    ValidateMagic(header);
                    int iterations = BitConverter.ToInt32(header, 4);
                    if (iterations < 100000 || iterations > 2000000) throw new InvalidDataException("Invalid KLB iteration count.");
                    byte[] salt = header.Skip(8).Take(16).ToArray();
                    byte[] iv = header.Skip(24).Take(16).ToArray();
                    byte[] keyMaterial;
                    using (var derive = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                        keyMaterial = derive.GetBytes(64);
                    byte[] encryptionKey = keyMaterial.Take(32).ToArray();
                    byte[] macKey = keyMaterial.Skip(32).Take(32).ToArray();
                    long cipherLength = input.Length - HeaderSize - TagSize;

                    byte[] expectedTag;
                    input.Position = input.Length - TagSize;
                    expectedTag = ReadExactly(input, TagSize);
                    byte[] actualTag;
                    using (var hmac = new HMACSHA256(macKey))
                    {
                        input.Position = 0;
                        HashLimited(input, hmac, HeaderSize + cipherLength);
                        actualTag = hmac.Hash;
                    }
                    if (!ConstantTimeEquals(expectedTag, actualTag))
                        throw new CryptographicException("Wrong archive password or corrupted backup.");

                    input.Position = HeaderSize;
                    using (var limited = new LimitedReadStream(input, cipherLength))
                    using (var aes = Aes.Create())
                    {
                        aes.KeySize = 256;
                        aes.Mode = CipherMode.CBC;
                        aes.Padding = PaddingMode.PKCS7;
                        aes.Key = encryptionKey;
                        aes.IV = iv;
                        using (var crypto = new CryptoStream(limited, aes.CreateDecryptor(), CryptoStreamMode.Read))
                        using (FileStream output = File.Create(partial))
                            crypto.CopyTo(output);
                    }
                    Array.Clear(keyMaterial, 0, keyMaterial.Length);
                    Array.Clear(encryptionKey, 0, encryptionKey.Length);
                    Array.Clear(macKey, 0, macKey.Length);
                }
                if (File.Exists(outputPath)) File.Delete(outputPath);
                File.Move(partial, outputPath);
            }
            catch
            {
                try { if (File.Exists(partial)) File.Delete(partial); } catch { }
                throw;
            }
        }

        private static byte[] BuildHeader(byte[] salt, byte[] iv)
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(Magic);
                writer.Write(Iterations);
                writer.Write(salt);
                writer.Write(iv);
                writer.Flush();
                return memory.ToArray();
            }
        }

        private static void ValidateMagic(byte[] header)
        {
            for (int i = 0; i < Magic.Length; i++)
                if (header[i] != Magic[i]) throw new InvalidDataException("This is not a KLB1 backup file.");
        }

        private static byte[] RandomBytes(int count)
        {
            byte[] bytes = new byte[count];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return bytes;
        }

        private static byte[] ReadExactly(Stream input, int count)
        {
            byte[] buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = input.Read(buffer, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException();
                offset += read;
            }
            return buffer;
        }

        private static void HashLimited(Stream input, HMAC hmac, long count)
        {
            byte[] buffer = new byte[1024 * 1024];
            long remaining = count;
            while (remaining > 0)
            {
                int wanted = (int)Math.Min(buffer.Length, remaining);
                int read = input.Read(buffer, 0, wanted);
                if (read <= 0) throw new EndOfStreamException();
                hmac.TransformBlock(buffer, 0, read, null, 0);
                remaining -= read;
            }
            hmac.TransformFinalBlock(new byte[0], 0, 0);
        }

        private static bool ConstantTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }
    }

    internal sealed class HmacWriteStream : Stream
    {
        private readonly Stream _output;
        private readonly HMAC _hmac;
        private bool _finalized;

        public HmacWriteStream(Stream output, HMAC hmac) { _output = output; _hmac = hmac; }
        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return true; } }
        public override long Length { get { return _output.Length; } }
        public override long Position { get { return _output.Position; } set { throw new NotSupportedException(); } }
        public override void Flush() { _output.Flush(); }
        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_finalized) throw new InvalidOperationException("HMAC already finalized.");
            _hmac.TransformBlock(buffer, offset, count, null, 0);
            _output.Write(buffer, offset, count);
        }
        public void FinalizeHash()
        {
            if (_finalized) return;
            _hmac.TransformFinalBlock(new byte[0], 0, 0);
            _finalized = true;
        }
        protected override void Dispose(bool disposing) { if (disposing) Flush(); base.Dispose(disposing); }
    }

    internal sealed class LimitedReadStream : Stream
    {
        private readonly Stream _input;
        private long _remaining;
        public LimitedReadStream(Stream input, long length) { _input = input; _remaining = length; }
        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return _remaining; } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            int wanted = (int)Math.Min(count, _remaining);
            int read = _input.Read(buffer, offset, wanted);
            _remaining -= read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
    }

    internal sealed class PasswordDialog : Form
    {
        private readonly TextBox _password = new TextBox();
        private PasswordDialog(string prompt)
        {
            Text = "رمز آرشیو";
            Width = 440;
            Height = 160;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 10F);
            RightToLeft = RightToLeft.Yes;
            RightToLeftLayout = true;
            var label = new Label { Text = prompt, Dock = DockStyle.Top, Height = 34, TextAlign = ContentAlignment.MiddleLeft };
            _password.Dock = DockStyle.Top;
            _password.UseSystemPasswordChar = true;
            var panel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft };
            var ok = new Button { Text = "تأیید", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "انصراف", DialogResult = DialogResult.Cancel, AutoSize = true };
            panel.Controls.Add(ok);
            panel.Controls.Add(cancel);
            Controls.Add(panel);
            Controls.Add(_password);
            Controls.Add(label);
            AcceptButton = ok;
            CancelButton = cancel;
        }
        public static string Ask(IWin32Window owner, string prompt)
        {
            using (var dialog = new PasswordDialog(prompt))
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._password.Text : null;
        }
    }
}
