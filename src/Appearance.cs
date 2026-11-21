using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RetroNotes95
{
    public static class Native
    {
        public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool RemoveFontResourceEx(string file, uint flags, IntPtr reserved);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr window, string app, string ids);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        public static void SetupDpi() { if (IsWindows) try { SetProcessDPIAware(); } catch { } }
        public static void ClassicControl(Control c) { if (IsWindows) try { SetWindowTheme(c.Handle, "", ""); } catch { } }
        public static void SquareWindow(Form f)
        {
            if (!IsWindows) return;
            try { int value = 1; DwmSetWindowAttribute(f.Handle, 33, ref value, 4); } catch { }
        }
        public static bool StartupEnabled
        {
            get
            {
                if (!IsWindows) return false;
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return key != null && key.GetValue("RetroNotes95") != null;
            }
        }
        public static void SetStartup(bool enabled)
        {
            if (!IsWindows) return;
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled) key.SetValue("RetroNotes95", "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                else key.DeleteValue("RetroNotes95", false);
            }
        }
    }

    public sealed class ThemeAssets : IDisposable
    {
        private readonly PrivateFontCollection collection = new PrivateFontCollection();
        private readonly List<string> loadedFiles = new List<string>();
        public FontFamily PixelFamily { get; private set; }
        public FontFamily TitleFamily { get; private set; }
        public readonly Bitmap AppImage;
        public readonly Bitmap CloseImage;
        public readonly Bitmap MaxImage;
        public readonly Bitmap GripImage;
        public readonly Icon Icon;

        public ThemeAssets(string directory)
        {
            foreach (string file in new[] { "W95FA.otf", "DotGothic16-Regular.ttf" })
            {
                string path = Path.Combine(directory, file);
                if (!File.Exists(path)) continue;
                try
                {
                    if (Native.IsWindows) Native.AddFontResourceEx(path, 0x10, IntPtr.Zero);
                    collection.AddFontFile(path); loadedFiles.Add(path);
                }
                catch { }
            }
            foreach (FontFamily family in collection.Families)
            {
                if (family.Name.IndexOf("W95FA", StringComparison.OrdinalIgnoreCase) >= 0) TitleFamily = family;
                if (family.Name.IndexOf("DotGothic", StringComparison.OrdinalIgnoreCase) >= 0) PixelFamily = family;
            }
            AppImage = Load(directory, "app-icon.png");
            CloseImage = Load(directory, "close.png");
            MaxImage = Load(directory, "maximize.png");
            GripImage = Load(directory, "size-grip.png");
            string iconPath = Path.Combine(directory, "app.ico");
            Icon = File.Exists(iconPath) ? new Icon(iconPath) : (Icon)SystemIcons.Application.Clone();
        }

        private static Bitmap Load(string dir, string name)
        {
            string path = Path.Combine(dir, name);
            return File.Exists(path) ? new Bitmap(path) : null;
        }
        public Font Title(float scale, bool japanese)
        {
            if (!japanese && TitleFamily != null) return SafeFont(TitleFamily, "MS UI Gothic", 12 * scale, FontStyle.Regular);
            return SafeFont(null, "MS UI Gothic", 12 * scale, FontStyle.Bold);
        }
        public Font Menu(float scale)
        {
            return SafeFont(TitleFamily, "MS UI Gothic", 11 * scale, FontStyle.Regular);
        }
        public Font Body(NoteData note, float scale)
        {
            return SafeFont(note.FontName == "pixel" ? PixelFamily : null, "MS Gothic", note.FontPixels * scale, FontStyle.Regular);
        }
        private static Font SafeFont(FontFamily family, string fallback, float size, FontStyle style)
        {
            Font font = null;
            if (family != null)
            {
                try
                {
                    // Mono/fontconfig needs a named family for native controls;
                    // Windows GDI+ supports the private collection directly.
                    font = Native.IsWindows ? new Font(family, size, style, GraphicsUnit.Pixel)
                        : new Font(family.Name, size, style, GraphicsUnit.Pixel);
                    if (font.GetHeight(96) > 0) return font;
                }
                catch { }
                if (font != null) font.Dispose();
            }
            return new Font(fallback, size, style, GraphicsUnit.Pixel);
        }
        public void Dispose()
        {
            foreach (Bitmap image in new[] { AppImage, CloseImage, MaxImage, GripImage }) if (image != null) image.Dispose();
            Icon.Dispose(); collection.Dispose();
            if (Native.IsWindows) foreach (string file in loadedFiles) try { Native.RemoveFontResourceEx(file, 0x10, IntPtr.Zero); } catch { }
        }
    }

    public static class Classic
    {
        public static readonly Color Face = Color.FromArgb(192, 192, 192);
        public static readonly Color Shadow = Color.FromArgb(128, 128, 128);
        public static readonly Color Navy = Color.FromArgb(0, 0, 128);
        public static Color Paper(string name)
        {
            switch (name)
            {
                case "pink": return Color.FromArgb(255, 241, 250);
                case "blue": return Color.FromArgb(237, 247, 255);
                case "mint": return Color.FromArgb(235, 255, 247);
                case "cream": return Color.FromArgb(255, 253, 216);
                default: return Color.White;
            }
        }
        public static void Bevel(Graphics g, Rectangle r, bool pressed, int scale)
        {
            if (r.Width < 3 || r.Height < 3) return;
            using (var white = new Pen(pressed ? Shadow : Color.White, scale))
            using (var black = new Pen(pressed ? Color.White : Color.Black, scale))
            using (var mid = new Pen(pressed ? Color.Black : Shadow, scale))
            {
                int x = r.Left, y = r.Top, right = r.Right - scale, bottom = r.Bottom - scale;
                g.DrawLines(white, new[] { new Point(x, bottom), new Point(x, y), new Point(right, y) });
                g.DrawLines(black, new[] { new Point(x, bottom), new Point(right, bottom), new Point(right, y) });
                g.DrawLines(mid, new[] { new Point(x + scale, bottom - scale), new Point(right - scale, bottom - scale), new Point(right - scale, y + scale) });
            }
        }
        public static void PixelImage(Graphics g, Image image, Rectangle rect)
        {
            if (image == null) return;
            var old = g.InterpolationMode; var offset = g.PixelOffsetMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor; g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, rect); g.InterpolationMode = old; g.PixelOffsetMode = offset;
        }
        public static ContextMenuStrip Menu()
        {
            return new ContextMenuStrip { Renderer = new ClassicMenuRenderer(), BackColor = Face, ForeColor = Color.Black,
                Font = new Font("MS UI Gothic", 9), ShowImageMargin = false, ShowCheckMargin = true };
        }
    }

    internal sealed class ClassicMenuRenderer : ToolStripProfessionalRenderer
    {
        public ClassicMenuRenderer() : base(new ClassicColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Classic.Shadow : e.Item.Selected ? Color.White : Color.Black;
            base.OnRenderItemText(e);
        }
    }
    internal sealed class ClassicColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Classic.Face; } }
        public override Color ImageMarginGradientBegin { get { return Classic.Face; } }
        public override Color ImageMarginGradientMiddle { get { return Classic.Face; } }
        public override Color ImageMarginGradientEnd { get { return Classic.Face; } }
        public override Color MenuItemSelected { get { return Classic.Navy; } }
        public override Color MenuItemSelectedGradientBegin { get { return Classic.Navy; } }
        public override Color MenuItemSelectedGradientEnd { get { return Classic.Navy; } }
        public override Color MenuItemBorder { get { return Classic.Navy; } }
        public override Color MenuBorder { get { return Color.Black; } }
        public override Color SeparatorDark { get { return Classic.Shadow; } }
        public override Color SeparatorLight { get { return Color.White; } }
    }
}
