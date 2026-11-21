using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace RgbBrightnessOSD
{
    internal static class NativeMethods
    {
        public const int WH_KEYBOARD_LL = 13;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_SYSKEYDOWN = 0x0104;
        public const int VK_F20 = 0x83;
        public const int VK_F21 = 0x84;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }

    internal enum OsdPosition
    {
        BottomLeft,
        BottomCenter,
        BottomRight
    }

    internal sealed class AppSettings
    {
        private static readonly string SettingsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RgbBrightnessOSD");

        private static readonly string SettingsPath = System.IO.Path.Combine(SettingsDir, "settings.ini");

        public int Brightness { get; set; }
        public int Step { get; set; }
        public OsdPosition Position { get; set; }
        public int UpKey { get; set; }
        public int DownKey { get; set; }

        public AppSettings()
        {
            Brightness = 85;
            Step = 5;
            Position = OsdPosition.BottomLeft;
            UpKey = NativeMethods.VK_F21;
            DownKey = NativeMethods.VK_F20;
        }

        public static string FormatKey(int vkCode)
        {
            return ((WinForms.Keys)vkCode).ToString();
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(SettingsPath))
                    return settings;

                foreach (string rawLine in File.ReadAllLines(SettingsPath))
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    int parsedInt;
                    OsdPosition parsedPos;
                    if (key == "Brightness" && int.TryParse(val, out parsedInt))
                        settings.Brightness = Clamp(parsedInt, 0, 100);
                    else if (key == "Step" && int.TryParse(val, out parsedInt))
                        settings.Step = Clamp(parsedInt, 1, 50);
                    else if (key == "Position" && Enum.TryParse(val, out parsedPos))
                        settings.Position = parsedPos;
                    else if (key == "UpKey" && int.TryParse(val, out parsedInt) && parsedInt > 0 && parsedInt < 256)
                        settings.UpKey = parsedInt;
                    else if (key == "DownKey" && int.TryParse(val, out parsedInt) && parsedInt > 0 && parsedInt < 256)
                        settings.DownKey = parsedInt;
                }
            }
            catch
            {
                // Ignore corrupt settings and use defaults
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllLines(SettingsPath, new[]
                {
                    "Brightness=" + Brightness,
                    "Step=" + Step,
                    "Position=" + Position,
                    "UpKey=" + UpKey,
                    "DownKey=" + DownKey
                });
            }
            catch
            {
                // Ignore write failures
            }
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    internal sealed class ThemePalette
    {
        public System.Windows.Media.Color CardBackground { get; set; }
        public System.Windows.Media.Color CardBorder { get; set; }
        public System.Windows.Media.Color Foreground { get; set; }
        public System.Windows.Media.Color TrackBackground { get; set; }
        public System.Windows.Media.Color FillBar { get; set; }
        public System.Drawing.Color TrayIconColor { get; set; }

        public static ThemePalette FromWindowsTheme()
        {
            bool useLight = ReadRegDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) == 1;
            bool colorPrevalence = ReadRegDword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "ColorPrevalence", 0) == 1;
            System.Windows.Media.Color accent = GetWindowsAccentColor();

            if (colorPrevalence)
            {
                // Tint the card with the Windows accent color (similar to Windows taskbar/flyout)
                System.Windows.Media.Color bgBase = useLight
                    ? Blend(accent, System.Windows.Media.Colors.White, 0.15)
                    : Blend(accent, System.Windows.Media.Color.FromRgb(16, 12, 24), 0.25);

                bool bgIsLight = GetLuminance(bgBase) > 0.58;
                System.Windows.Media.Color fg = bgIsLight
                    ? System.Windows.Media.Color.FromRgb(24, 24, 28)
                    : System.Windows.Media.Colors.White;

                System.Windows.Media.Color track = bgIsLight
                    ? System.Windows.Media.Color.FromArgb(55, 0, 0, 0)
                    : System.Windows.Media.Color.FromArgb(75, 255, 255, 255);

                System.Windows.Media.Color fill = bgIsLight
                    ? Blend(accent, System.Windows.Media.Colors.Black, 0.35)
                    : Blend(accent, System.Windows.Media.Colors.White, 0.55);

                System.Windows.Media.Color border = bgIsLight
                    ? System.Windows.Media.Color.FromArgb(45, 0, 0, 0)
                    : System.Windows.Media.Color.FromArgb(65, 255, 255, 255);

                return new ThemePalette
                {
                    CardBackground = System.Windows.Media.Color.FromArgb(252, bgBase.R, bgBase.G, bgBase.B),
                    CardBorder = border,
                    Foreground = fg,
                    TrackBackground = track,
                    FillBar = fill,
                    TrayIconColor = System.Drawing.Color.FromArgb(fill.R, fill.G, fill.B)
                };
            }

            if (useLight)
            {
                return new ThemePalette
                {
                    CardBackground = System.Windows.Media.Color.FromArgb(252, 245, 245, 248),
                    CardBorder = System.Windows.Media.Color.FromArgb(40, 0, 0, 0),
                    Foreground = System.Windows.Media.Color.FromRgb(26, 26, 30),
                    TrackBackground = System.Windows.Media.Color.FromArgb(45, 0, 0, 0),
                    FillBar = accent,
                    TrayIconColor = System.Drawing.Color.FromArgb(accent.R, accent.G, accent.B)
                };
            }

            // Dark mode without ColorPrevalence
            System.Windows.Media.Color darkFill = GetLuminance(accent) < 0.25
                ? Blend(accent, System.Windows.Media.Colors.White, 0.40)
                : accent;

            return new ThemePalette
            {
                CardBackground = System.Windows.Media.Color.FromArgb(252, 32, 32, 36),
                CardBorder = System.Windows.Media.Color.FromArgb(55, 255, 255, 255),
                Foreground = System.Windows.Media.Colors.White,
                TrackBackground = System.Windows.Media.Color.FromArgb(70, 255, 255, 255),
                FillBar = darkFill,
                TrayIconColor = System.Drawing.Color.FromArgb(darkFill.R, darkFill.G, darkFill.B)
            };
        }

        private static System.Windows.Media.Color GetWindowsAccentColor()
        {
            try
            {
                System.Windows.Media.Color glass = SystemParameters.WindowGlassColor;
                if (glass.A > 0)
                    return System.Windows.Media.Color.FromRgb(glass.R, glass.G, glass.B);
            }
            catch
            {
            }
            return System.Windows.Media.Color.FromRgb(120, 60, 200);
        }

        private static int ReadRegDword(string subKey, string valueName, int defaultValue)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subKey, false))
                {
                    if (key == null) return defaultValue;
                    object val = key.GetValue(valueName);
                    if (val is int) return (int)val;
                }
            }
            catch
            {
            }
            return defaultValue;
        }

        private static System.Windows.Media.Color Blend(System.Windows.Media.Color a, System.Windows.Media.Color b, double t)
        {
            byte r = (byte)Math.Round(a.R + (b.R - a.R) * t);
            byte g = (byte)Math.Round(a.G + (b.G - a.G) * t);
            byte bl = (byte)Math.Round(a.B + (b.B - a.B) * t);
            return System.Windows.Media.Color.FromRgb(r, g, bl);
        }

        private static double GetLuminance(System.Windows.Media.Color c)
        {
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        }
    }

    internal sealed class OsdWindow : Window
    {
        private const double CardWidth = 310;
        private const double CardHeight = 58;
        private const double TrackMaxWidth = 176;

        private readonly Border _card;
        private readonly Border _trackBg;
        private readonly Border _fillBar;
        private readonly TextBlock _valueText;
        private readonly Ellipse _sunCenter;
        private readonly Line[] _sunRays;
        private readonly DispatcherTimer _hideTimer;
        private bool _isVisible;

        public OsdWindow()
        {
            Width = CardWidth;
            Height = CardHeight;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Opacity = 0;

            _card = new Border
            {
                Width = CardWidth,
                Height = CardHeight,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 0, 18, 0)
            };

            var grid = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            UIElement sunIcon = CreateSunIcon(out _sunCenter, out _sunRays);
            Grid.SetColumn(sunIcon, 0);
            grid.Children.Add(sunIcon);

            var trackContainer = new Grid
            {
                Width = TrackMaxWidth,
                Height = 6,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _trackBg = new Border
            {
                Width = TrackMaxWidth,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            _fillBar = new Border
            {
                Width = TrackMaxWidth * 0.85,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            trackContainer.Children.Add(_trackBg);
            trackContainer.Children.Add(_fillBar);
            Grid.SetColumn(trackContainer, 1);
            grid.Children.Add(trackContainer);

            _valueText = new TextBlock
            {
                Text = "85",
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_valueText, 2);
            grid.Children.Add(_valueText);

            _card.Child = grid;
            Content = _card;

            ApplyTheme(ThemePalette.FromWindowsTheme());

            _hideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1800)
            };
            _hideTimer.Tick += (s, e) =>
            {
                _hideTimer.Stop();
                FadeOut();
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(
                hwnd,
                NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
        }

        public void ApplyTheme(ThemePalette palette)
        {
            _card.Background = new SolidColorBrush(palette.CardBackground);
            _card.BorderBrush = new SolidColorBrush(palette.CardBorder);
            _trackBg.Background = new SolidColorBrush(palette.TrackBackground);
            _fillBar.Background = new SolidColorBrush(palette.FillBar);

            var fgBrush = new SolidColorBrush(palette.Foreground);
            _valueText.Foreground = fgBrush;
            _sunCenter.Stroke = fgBrush;
            for (int i = 0; i < _sunRays.Length; i++)
                _sunRays[i].Stroke = fgBrush;
        }

        private static UIElement CreateSunIcon(out Ellipse centerCircle, out Line[] rays)
        {
            var canvas = new Canvas
            {
                Width = 22,
                Height = 22,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            centerCircle = new Ellipse
            {
                Width = 9,
                Height = 9,
                StrokeThickness = 1.8
            };
            Canvas.SetLeft(centerCircle, 6.5);
            Canvas.SetTop(centerCircle, 6.5);
            canvas.Children.Add(centerCircle);

            double cx = 11.0;
            double cy = 11.0;
            double innerR = 7.2;
            double outerR = 9.8;
            rays = new Line[8];

            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4.0;
                var ray = new Line
                {
                    X1 = cx + innerR * Math.Cos(angle),
                    Y1 = cy + innerR * Math.Sin(angle),
                    X2 = cx + outerR * Math.Cos(angle),
                    Y2 = cy + outerR * Math.Sin(angle),
                    StrokeThickness = 1.8,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
                rays[i] = ray;
                canvas.Children.Add(ray);
            }

            return canvas;
        }

        public void ShowBrightness(int brightness, OsdPosition position)
        {
            ApplyTheme(ThemePalette.FromWindowsTheme());
            int clamped = AppSettings.Clamp(brightness, 0, 100);
            _valueText.Text = clamped.ToString();
            _fillBar.Width = TrackMaxWidth * (clamped / 100.0);

            PositionWindow(position);

            _hideTimer.Stop();
            _hideTimer.Start();

            if (!_isVisible)
            {
                _isVisible = true;
                Show();
                var fadeIn = new DoubleAnimation(Opacity, 1.0, TimeSpan.FromMilliseconds(110))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                BeginAnimation(OpacityProperty, fadeIn);
            }
            else if (Opacity < 1.0)
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 1.0;
            }
        }

        private void PositionWindow(OsdPosition position)
        {
            Rect workArea = SystemParameters.WorkArea;
            const double marginX = 24;
            const double marginBottom = 16;

            Top = workArea.Bottom - CardHeight - marginBottom;

            switch (position)
            {
                case OsdPosition.BottomCenter:
                    Left = workArea.Left + (workArea.Width - CardWidth) / 2.0;
                    break;
                case OsdPosition.BottomRight:
                    Left = workArea.Right - CardWidth - marginX;
                    break;
                case OsdPosition.BottomLeft:
                default:
                    Left = workArea.Left + marginX;
                    break;
            }
        }

        private void FadeOut()
        {
            var fadeOut = new DoubleAnimation(Opacity, 0.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                if (Opacity <= 0.01)
                {
                    _isVisible = false;
                    Hide();
                }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }

    internal sealed class KeyCaptureWindow : Window
    {
        public KeyCaptureWindow(string targetLabel)
        {
            Width = 340;
            Height = 110;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            ThemePalette palette = ThemePalette.FromWindowsTheme();

            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(palette.CardBackground),
                BorderBrush = new SolidColorBrush(palette.CardBorder),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(20, 16, 20, 16)
            };

            var stack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            stack.Children.Add(new TextBlock
            {
                Text = "Press a key or turn dial for " + targetLabel,
                Foreground = new SolidColorBrush(palette.Foreground),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Press Esc to cancel",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, palette.Foreground.R, palette.Foreground.G, palette.Foreground.B)),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            card.Child = stack;
            Content = card;
        }
    }

    internal static class Program
    {
        private const string StartupRegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "RgbBrightnessOSD";

        private static AppSettings _settings;
        private static OsdWindow _osdWindow;
        private static WinForms.NotifyIcon _trayIcon;
        private static IntPtr _hookId = IntPtr.Zero;
        private static NativeMethods.LowLevelKeyboardProc _hookCallback;
        private static Dispatcher _dispatcher;
        private static Action<int> _captureCallback;
        private static KeyCaptureWindow _captureWindow;
        private static WinForms.ToolStripMenuItem _upKeyMenuItem;
        private static WinForms.ToolStripMenuItem _downKeyMenuItem;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "RgbBrightnessOSD_SingleInstance", out createdNew))
            {
                if (!createdNew)
                    return;

                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };

                _dispatcher = Dispatcher.CurrentDispatcher;
                _settings = AppSettings.Load();
                _osdWindow = new OsdWindow();

                SetupTrayIcon();
                InstallKeyboardHook();
                SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

                app.Exit += (s, e) =>
                {
                    SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
                    UninstallKeyboardHook();
                    if (_trayIcon != null)
                    {
                        _trayIcon.Visible = false;
                        _trayIcon.Dispose();
                    }
                    _settings.Save();
                };

                app.Run();
            }
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (_dispatcher == null) return;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                var palette = ThemePalette.FromWindowsTheme();
                if (_osdWindow != null)
                    _osdWindow.ApplyTheme(palette);
                if (_trayIcon != null)
                    _trayIcon.Icon = CreateTraySunIcon(palette.TrayIconColor);
            }));
        }

        private static void InstallKeyboardHook()
        {
            _hookCallback = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                IntPtr hMod = NativeMethods.GetModuleHandle(curModule.ModuleName);
                _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _hookCallback, hMod, 0);
            }
        }

        private static void UninstallKeyboardHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
            {
                var kb = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                int vk = (int)kb.vkCode;

                if (_captureCallback != null)
                {
                    if (!IsModifierKey(vk))
                    {
                        Action<int> cb = _captureCallback;
                        _captureCallback = null;
                        _dispatcher.BeginInvoke(new Action(() => cb(vk)));
                    }
                    return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (vk == _settings.UpKey)
                {
                    _dispatcher.BeginInvoke(new Action(() => AdjustBrightness(_settings.Step)));
                }
                else if (vk == _settings.DownKey)
                {
                    _dispatcher.BeginInvoke(new Action(() => AdjustBrightness(-_settings.Step)));
                }
            }

            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static bool IsModifierKey(int vk)
        {
            return vk == 0x10 || vk == 0xA0 || vk == 0xA1 || // Shift
                   vk == 0x11 || vk == 0xA2 || vk == 0xA3 || // Ctrl
                   vk == 0x12 || vk == 0xA4 || vk == 0xA5 || // Alt
                   vk == 0x5B || vk == 0x5C;                 // Win
        }

        private static void BeginKeyCapture(string label, Action<int> onCaptured)
        {
            if (_captureWindow != null)
            {
                _captureWindow.Close();
                _captureWindow = null;
            }

            _captureWindow = new KeyCaptureWindow(label);
            _captureWindow.Closed += (s, e) =>
            {
                _captureCallback = null;
                _captureWindow = null;
            };

            _captureCallback = vk =>
            {
                if (_captureWindow != null)
                {
                    _captureWindow.Close();
                    _captureWindow = null;
                }
                if (vk != 0x1B) // Escape cancels
                {
                    onCaptured(vk);
                }
            };

            _captureWindow.Show();
            _captureWindow.Activate();
        }

        private static void UpdateKeybindLabels()
        {
            string upName = AppSettings.FormatKey(_settings.UpKey);
            string downName = AppSettings.FormatKey(_settings.DownKey);
            if (_upKeyMenuItem != null)
                _upKeyMenuItem.Text = "Brightness Up: " + upName + "...";
            if (_downKeyMenuItem != null)
                _downKeyMenuItem.Text = "Brightness Down: " + downName + "...";
            if (_trayIcon != null)
                _trayIcon.Text = "RGB Brightness OSD (" + downName + " / " + upName + ")";
        }

        private static void AdjustBrightness(int delta)
        {
            _settings.Brightness = AppSettings.Clamp(_settings.Brightness + delta, 0, 100);
            _settings.Save();
            _osdWindow.ShowBrightness(_settings.Brightness, _settings.Position);
        }

        private static void SetupTrayIcon()
        {
            var menu = new WinForms.ContextMenuStrip();

            var showItem = new WinForms.ToolStripMenuItem("Show Meter");
            showItem.Click += (s, e) => _osdWindow.ShowBrightness(_settings.Brightness, _settings.Position);
            menu.Items.Add(showItem);

            var levelMenu = new WinForms.ToolStripMenuItem("Set Level");
            foreach (int preset in new[] { 0, 25, 50, 75, 85, 100 })
            {
                int val = preset;
                var item = new WinForms.ToolStripMenuItem(val + "%");
                item.Click += (s, e) =>
                {
                    _settings.Brightness = val;
                    _settings.Save();
                    _osdWindow.ShowBrightness(_settings.Brightness, _settings.Position);
                };
                levelMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(levelMenu);

            var stepMenu = new WinForms.ToolStripMenuItem("Step Size");
            var stepItems = new List<WinForms.ToolStripMenuItem>();
            foreach (int stepVal in new[] { 1, 2, 5, 10 })
            {
                int val = stepVal;
                var item = new WinForms.ToolStripMenuItem(val + "%") { Checked = (_settings.Step == val) };
                item.Click += (s, e) =>
                {
                    _settings.Step = val;
                    _settings.Save();
                    foreach (var si in stepItems) si.Checked = false;
                    item.Checked = true;
                };
                stepItems.Add(item);
                stepMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(stepMenu);

            var posMenu = new WinForms.ToolStripMenuItem("Position");
            var posItems = new Dictionary<OsdPosition, WinForms.ToolStripMenuItem>();
            foreach (var kvp in new[]
            {
                new KeyValuePair<OsdPosition, string>(OsdPosition.BottomLeft, "Bottom Left"),
                new KeyValuePair<OsdPosition, string>(OsdPosition.BottomCenter, "Bottom Center"),
                new KeyValuePair<OsdPosition, string>(OsdPosition.BottomRight, "Bottom Right")
            })
            {
                OsdPosition pos = kvp.Key;
                var item = new WinForms.ToolStripMenuItem(kvp.Value) { Checked = (_settings.Position == pos) };
                item.Click += (s, e) =>
                {
                    _settings.Position = pos;
                    _settings.Save();
                    foreach (var pi in posItems.Values) pi.Checked = false;
                    item.Checked = true;
                    _osdWindow.ShowBrightness(_settings.Brightness, _settings.Position);
                };
                posItems[pos] = item;
                posMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(posMenu);

            var keybindsMenu = new WinForms.ToolStripMenuItem("Keybinds");
            _upKeyMenuItem = new WinForms.ToolStripMenuItem();
            _upKeyMenuItem.Click += (s, e) => BeginKeyCapture("Brightness Up", vk =>
            {
                _settings.UpKey = vk;
                _settings.Save();
                UpdateKeybindLabels();
            });
            _downKeyMenuItem = new WinForms.ToolStripMenuItem();
            _downKeyMenuItem.Click += (s, e) => BeginKeyCapture("Brightness Down", vk =>
            {
                _settings.DownKey = vk;
                _settings.Save();
                UpdateKeybindLabels();
            });
            var resetKeysItem = new WinForms.ToolStripMenuItem("Reset to F21 / F20");
            resetKeysItem.Click += (s, e) =>
            {
                _settings.UpKey = NativeMethods.VK_F21;
                _settings.DownKey = NativeMethods.VK_F20;
                _settings.Save();
                UpdateKeybindLabels();
            };
            keybindsMenu.DropDownItems.Add(_upKeyMenuItem);
            keybindsMenu.DropDownItems.Add(_downKeyMenuItem);
            keybindsMenu.DropDownItems.Add(new WinForms.ToolStripSeparator());
            keybindsMenu.DropDownItems.Add(resetKeysItem);
            menu.Items.Add(keybindsMenu);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            var startupItem = new WinForms.ToolStripMenuItem("Start with Windows")
            {
                Checked = IsStartupEnabled()
            };
            startupItem.Click += (s, e) =>
            {
                bool enable = !startupItem.Checked;
                SetStartupEnabled(enable);
                startupItem.Checked = IsStartupEnabled();
            };
            menu.Items.Add(startupItem);

            menu.Items.Add(new WinForms.ToolStripSeparator());

            var exitItem = new WinForms.ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => Application.Current.Shutdown();
            menu.Items.Add(exitItem);

            _trayIcon = new WinForms.NotifyIcon
            {
                Icon = CreateTraySunIcon(ThemePalette.FromWindowsTheme().TrayIconColor),
                ContextMenuStrip = menu,
                Visible = true
            };
            UpdateKeybindLabels();
            _trayIcon.DoubleClick += (s, e) => _osdWindow.ShowBrightness(_settings.Brightness, _settings.Position);
        }

        private static Icon CreateTraySunIcon(System.Drawing.Color color)
        {
            using (var bmp = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                using (var pen = new System.Drawing.Pen(color, 2.4f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;

                    g.DrawEllipse(pen, 10f, 10f, 12f, 12f);

                    float cx = 16f, cy = 16f, r1 = 10f, r2 = 13.5f;
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4.0;
                        g.DrawLine(
                            pen,
                            cx + r1 * (float)Math.Cos(angle),
                            cy + r1 * (float)Math.Sin(angle),
                            cx + r2 * (float)Math.Cos(angle),
                            cy + r2 * (float)Math.Sin(angle));
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegKey, false))
                {
                    return key != null && key.GetValue(AppName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void SetStartupEnabled(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegKey, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exePath = Process.GetCurrentProcess().MainModule.FileName;
                        key.SetValue(AppName, "\"" + exePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch
            {
                // Ignore registry errors
            }
        }
    }
}
