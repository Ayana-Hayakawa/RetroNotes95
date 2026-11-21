using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace RetroNotes95
{
    public sealed class NoteWindow : Form
    {
        public readonly NoteData Data;
        public readonly TextBox Editor;
        private readonly NoteApplication app;
        private float scale = 1;
        private Font titleFont, menuFont, statusFont, bodyFont;
        private bool active, ready, maximized;
        private Rectangle restoreBounds;
        private Rectangle dragBounds;
        private Point dragPoint;
        private string pressed;
        private int dragMode;
        private int characterCount;
        private readonly ToolTip tips = new ToolTip();

        public NoteWindow(NoteApplication owner, NoteData data)
        {
            app = owner; Data = data;
            Text = data.Title + " — Retro Notes 95";
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            BackColor = Classic.Face; ForeColor = Color.Black; Icon = owner.Assets.Icon;
            DoubleBuffered = true; KeyPreview = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            using (Graphics graphics = CreateGraphics()) scale = Math.Max(1, graphics.DpiX / 96f);
            MinimumSize = new Size(S(280), S(170));
            Editor = new TextBox { Multiline = true, BorderStyle = BorderStyle.None, AcceptsReturn = true,
                AcceptsTab = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
                ShortcutsEnabled = true, HideSelection = false, MaxLength = 1000000,
                ImeMode = ImeMode.NoControl, ForeColor = Color.Black, Text = Data.Body };
            Controls.Add(Editor);
            UpdateFonts();
            Editor.BackColor = Classic.Paper(Data.Paper);
            characterCount = new StringInfo(Data.Body).LengthInTextElements;
            Editor.TextChanged += delegate
            {
                Data.Body = Editor.Text; characterCount = new StringInfo(Data.Body).LengthInTextElements;
                app.ScheduleSave();
            };
            Editor.HandleCreated += delegate { Native.ClassicControl(Editor); };
            Editor.ContextMenuStrip = BuildEditMenu();
            float ratio = (96f * scale) / Math.Max(96, Data.SavedDpi);
            Bounds = Placement.KeepVisible(new Rectangle(Data.X, Data.Y, Math.Max(S(280), (int)(Data.Width * ratio)),
                Math.Max(S(170), (int)(Data.Height * ratio))), Screen.AllScreens.Select(s => s.WorkingArea).ToArray());
            TopMost = data.Pinned;
            ready = true; LayoutEditor(); RememberBounds();
        }

        private int S(float value) { return (int)Math.Round(value * scale); }
        private Rectangle TitleRect { get { return new Rectangle(S(4), S(4), Width - S(8), S(20)); } }
        private Rectangle CloseRect { get { return new Rectangle(Width - S(22), S(7), S(16), S(14)); } }
        private Rectangle MaxRect { get { return new Rectangle(Width - S(41), S(7), S(16), S(14)); } }
        private Rectangle MinRect { get { return new Rectangle(Width - S(57), S(7), S(16), S(14)); } }
        private Rectangle PinRect { get { return new Rectangle(Width - S(94), Height - S(21), S(74), S(16)); } }
        private Rectangle GripRect { get { return new Rectangle(Width - S(18), Height - S(19), S(14), S(14)); } }
        private Rectangle WellRect { get { return new Rectangle(S(4), S(45), Width - S(8), Height - S(72)); } }
        private Rectangle MenuRect(int i)
        {
            int[] x = { 9, 49, 90, 135 };
            int[] width = { 31, 31, 36, 35 };
            return new Rectangle(S(x[i]), S(26), S(width[i]), S(16));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e); Native.SquareWindow(this);
        }
        private void UpdateFonts()
        {
            var oldTitle = titleFont; var oldMenu = menuFont; var oldStatus = statusFont; var oldBody = bodyFont;
            titleFont = app.Assets.Title(scale, Data.Title.Any(c => c > 255));
            menuFont = app.Assets.Menu(scale);
            statusFont = new Font("MS UI Gothic", S(11), FontStyle.Regular, GraphicsUnit.Pixel);
            bodyFont = app.Assets.Body(Data, scale); Editor.Font = bodyFont;
            foreach (var f in new[] { oldTitle, oldMenu, oldStatus, oldBody }) if (f != null) f.Dispose();
        }
        private void LayoutEditor()
        {
            if (Editor == null) return;
            Editor.SetBounds(S(7), S(48), Math.Max(10, Width - S(14)), Math.Max(10, Height - S(78)));
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e); LayoutEditor();
            if (ready) { RememberBounds(); app.ScheduleSave(); }
        }
        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            if (ready) { RememberBounds(); app.ScheduleSave(); }
        }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); active = true; Invalidate(TitleRect); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); active = false; Invalidate(TitleRect); }
        public void InvalidateStatus() { Invalidate(new Rectangle(0, Height - S(26), Width, S(26))); }
        public void RememberBounds()
        {
            if (!ready || maximized || WindowState != FormWindowState.Normal) return;
            Data.X = Left; Data.Y = Top; Data.Width = Width; Data.Height = Height; Data.SavedDpi = (int)Math.Round(96 * scale);
        }
        public void EnsureOnScreen()
        {
            if (!maximized) Bounds = Placement.KeepVisible(Bounds, Screen.AllScreens.Select(s => s.WorkingArea).ToArray());
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None; g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            int line = Math.Max(1, S(1));
            Classic.Bevel(g, ClientRectangle, false, line);
            using (var brush = new SolidBrush(active ? Classic.Navy : Classic.Shadow)) g.FillRectangle(brush, TitleRect);
            Classic.PixelImage(g, app.Assets.AppImage, new Rectangle(S(6), S(6), S(16), S(16)));
            using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, LineAlignment = StringAlignment.Center })
                g.DrawString(Data.Title + " - Sticky Notes", titleFont, Brushes.White, new RectangleF(S(25), S(4), Width - S(88), S(20)), format);
            DrawTitleButton(g, MinRect, "min"); DrawTitleButton(g, MaxRect, "max"); DrawTitleButton(g, CloseRect, "close");
            string[] names = { "File", "Edit", "View", "Help" };
            for (int i = 0; i < 4; i++)
            {
                Rectangle r = MenuRect(i);
                if (pressed == "menu" + i) Classic.Bevel(g, r, true, line);
                g.DrawString(names[i], menuFont, Brushes.Black, r.X + S(2), r.Y + S(1));
                using (var pen = new Pen(Color.Black, line)) g.DrawLine(pen, r.X + S(3), r.Y + S(12), r.X + S(8), r.Y + S(12));
            }
            using (var dark = new Pen(Classic.Shadow, line)) g.DrawLine(dark, S(4), S(42), Width - S(5), S(42));
            using (var white = new Pen(Color.White, line)) g.DrawLine(white, S(4), S(43), Width - S(5), S(43));
            using (var paper = new SolidBrush(Editor.BackColor)) g.FillRectangle(paper, WellRect);
            Classic.Bevel(g, WellRect, true, line);
            Rectangle saved = new Rectangle(S(4), Height - S(21), Width - S(174), S(16));
            Rectangle count = new Rectangle(Width - S(165), Height - S(21), S(66), S(16));
            Classic.Bevel(g, saved, true, line); Classic.Bevel(g, count, true, line);
            string status = app.SaveFailed ? "保存できません" : app.Dirty ? "編集中…" : "保存済み";
            using (var format = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
            using (var brush = new SolidBrush(app.SaveFailed ? Color.DarkRed : Color.Black))
            {
                g.DrawString(status, statusFont, brush, new RectangleF(saved.X + S(4), saved.Y, saved.Width - S(8), saved.Height), format);
                g.DrawString(characterCount + "文字", statusFont, Brushes.Black,
                    new RectangleF(count.X + S(3), count.Y, count.Width - S(6), count.Height), format);
            }
            Classic.Bevel(g, PinRect, Data.Pinned || pressed == "pin", line);
            g.DrawString(Data.Pinned ? "PIN ON" : "PIN OFF", menuFont, Brushes.Black, PinRect.X + S(13), PinRect.Y + S(2));
            Classic.PixelImage(g, app.Assets.GripImage, GripRect);
        }

        private void DrawTitleButton(Graphics g, Rectangle rect, string name)
        {
            using (var face = new SolidBrush(Classic.Face)) g.FillRectangle(face, rect);
            Classic.Bevel(g, rect, pressed == name, Math.Max(1, S(1)));
            int shift = pressed == name ? S(1) : 0;
            if (name == "min") g.FillRectangle(Brushes.Black, rect.X + S(4) + shift, rect.Y + S(9) + shift, S(6), S(2));
            else Classic.PixelImage(g, name == "close" ? app.Assets.CloseImage : app.Assets.MaxImage,
                new Rectangle(rect.X + S(1) + shift, rect.Y + S(1) + shift, S(13), S(11)));
        }
        private string Target(Point p)
        {
            if (CloseRect.Contains(p)) return "close";
            if (MaxRect.Contains(p)) return "max";
            if (MinRect.Contains(p)) return "min";
            if (PinRect.Contains(p)) return "pin";
            for (int i = 0; i < 4; i++) if (MenuRect(i).Contains(p)) return "menu" + i;
            return null;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            string target = Target(e.Location);
            if (target != null)
            {
                pressed = target; Capture = true; Invalidate(); return;
            }
            if (GripRect.Contains(e.Location) && !maximized) dragMode = 2;
            else if (TitleRect.Contains(e.Location) && !maximized) dragMode = 1;
            if (dragMode != 0) { dragPoint = MousePosition; dragBounds = Bounds; Capture = true; }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragMode != 0)
            {
                Point p = MousePosition; int dx = p.X - dragPoint.X, dy = p.Y - dragPoint.Y;
                if (dragMode == 1) Location = new Point(dragBounds.X + dx, dragBounds.Y + dy);
                else Size = new Size(Math.Max(MinimumSize.Width, dragBounds.Width + dx), Math.Max(MinimumSize.Height, dragBounds.Height + dy));
            }
            Cursor = GripRect.Contains(e.Location) && !maximized ? Cursors.SizeNWSE : Cursors.Default;
            string tip = PinRect.Contains(e.Location) ? "常に手前に表示する / 解除する" : GripRect.Contains(e.Location) ? "ドラッグで大きさを変更" : "";
            if (tips.GetToolTip(this) != tip) tips.SetToolTip(this, tip);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            string action = pressed;
            bool same = action != null && action == Target(e.Location);
            bool dragged = dragMode != 0;
            pressed = null; dragMode = 0; Capture = false; Invalidate();
            if (dragged) { EnsureOnScreen(); RememberBounds(); app.ScheduleSave(); }
            if (!same) return;
            if (action == "close" || action == "min") app.HideNote(this);
            else if (action == "max") ToggleMaximize();
            else if (action == "pin") TogglePin();
            else if (action.StartsWith("menu")) ShowMenu(Int32.Parse(action.Substring(4)));
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (TitleRect.Contains(e.Location) && Target(e.Location) == null) RenameNote();
        }
        public void TogglePin() { Data.Pinned = !Data.Pinned; TopMost = Data.Pinned; app.ScheduleSave(); InvalidateStatus(); }
        private void ToggleMaximize()
        {
            if (!maximized)
            {
                RememberBounds(); restoreBounds = Bounds; maximized = true; Bounds = Screen.FromControl(this).WorkingArea;
            }
            else { maximized = false; Bounds = restoreBounds; EnsureOnScreen(); RememberBounds(); }
            app.ScheduleSave();
        }

        private static ToolStripMenuItem Item(string text, Action action, Keys shortcut)
        {
            var item = new ToolStripMenuItem(text); item.Click += delegate { action(); };
            if (shortcut != Keys.None) item.ShortcutKeys = shortcut;
            return item;
        }
        private ContextMenuStrip BuildEditMenu()
        {
            var menu = Classic.Menu();
            menu.Items.Add(Item("元に戻す", delegate { Editor.Undo(); }, Keys.Control | Keys.Z));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Item("切り取り", delegate { Editor.Cut(); }, Keys.Control | Keys.X));
            menu.Items.Add(Item("コピー", delegate { Editor.Copy(); }, Keys.Control | Keys.C));
            menu.Items.Add(Item("貼り付け", delegate { try { Editor.Paste(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "貼り付けできませんでした"); } }, Keys.Control | Keys.V));
            menu.Items.Add(Item("すべて選択", delegate { Editor.SelectAll(); Editor.Focus(); }, Keys.Control | Keys.A));
            return menu;
        }
        private void ShowMenu(int index)
        {
            ContextMenuStrip menu;
            if (index == 1) menu = BuildEditMenu();
            else
            {
                menu = Classic.Menu();
                if (index == 0)
                {
                    menu.Items.Add(Item("新しい付箋", delegate { app.NewNote(); }, Keys.Control | Keys.N));
                    menu.Items.Add(Item("名前を変更…", RenameNote, Keys.F2));
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(Item("今すぐ保存", delegate { app.Flush(true); }, Keys.Control | Keys.S));
                    menu.Items.Add(Item("テキストを書き出す…", ExportText, Keys.Control | Keys.Shift | Keys.E));
                    menu.Items.Add(Item("すべての付箋をバックアップ…", delegate { app.ExportBackup(this); }, Keys.Control | Keys.Shift | Keys.S));
                    menu.Items.Add(Item("バックアップを取り込む…", delegate { app.ImportBackup(this); }, Keys.None));
                    menu.Items.Add(Item("保存フォルダーを開く", OpenDataFolder, Keys.None));
                    menu.Items.Add(new ToolStripSeparator());
                    menu.Items.Add(Item("この付箋を隠す", delegate { app.HideNote(this); }, Keys.Control | Keys.W));
                    menu.Items.Add(Item("この付箋を削除…", delegate { app.DeleteNote(this); }, Keys.None));
                    menu.Items.Add(Item("アプリを終了", delegate { app.ExitApplication(); }, Keys.None));
                }
                else if (index == 2)
                {
                    var pin = Item("常に手前に表示", TogglePin, Keys.Control | Keys.Shift | Keys.P); pin.Checked = Data.Pinned; menu.Items.Add(pin);
                    menu.Items.Add(Item("付箋をすべて表示", delegate { app.ShowAllNotes(); }, Keys.None));
                    var colors = new ToolStripMenuItem("メモの色");
                    string[] names = { "white", "pink", "blue", "mint", "cream" }, labels = { "ホワイト", "パステルピンク", "アイスブルー", "ミント", "クリーム" };
                    for (int i = 0; i < names.Length; i++)
                    {
                        string color = names[i]; var choice = Item(labels[i], delegate { SetPaper(color); }, Keys.None);
                        choice.Checked = Data.Paper == color; colors.DropDownItems.Add(choice);
                    }
                    menu.Items.Add(colors);
                    var sizes = new ToolStripMenuItem("文字サイズ");
                    foreach (int value in new[] { 12, 16, 20, 24, 28 })
                    {
                        int pixels = value;
                        var choice = Item(pixels + " px", delegate { Data.FontPixels = pixels; UpdateFonts(); app.ScheduleSave(); }, Keys.None);
                        choice.Checked = Data.FontPixels == pixels; sizes.DropDownItems.Add(choice);
                    }
                    menu.Items.Add(sizes);
                    var fonts = new ToolStripMenuItem("文字の種類");
                    foreach (string value in new[] { "pixel", "standard" })
                    {
                        string font = value;
                        var choice = Item(font == "pixel" ? "ドット文字 (DotGothic16)" : "標準 (MS Gothic)", delegate { Data.FontName = font; UpdateFonts(); app.ScheduleSave(); }, Keys.None);
                        choice.Checked = Data.FontName == font; fonts.DropDownItems.Add(choice);
                    }
                    menu.Items.Add(fonts);
                    menu.Items.Add(new ToolStripSeparator());
                    var startup = Item("Windowsと一緒に起動", delegate
                    {
                        try { Native.SetStartup(!Native.StartupEnabled); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, "自動起動を変更できませんでした"); }
                    }, Keys.None);
                    startup.Enabled = Native.IsWindows; startup.Checked = Native.StartupEnabled; menu.Items.Add(startup);
                }
                else
                {
                    menu.Items.Add(Item("使い方", ShowHelp, Keys.F1));
                    menu.Items.Add(Item("Retro Notes 95 について", delegate
                    {
                        MessageBox.Show(this, "Retro Notes 95 — Version 1.0\n\nWindows 95風のデスクトップ付箋。\n付箋はこのPCに自動保存します。\n\nFonts: W95FA / DotGothic16\nフォントのライセンスは licenses フォルダーに同梱。", "Retro Notes 95");
                    }, Keys.None));
                }
            }
            menu.Closed += delegate { menu.Dispose(); if (!IsDisposed && Visible) Editor.Focus(); };
            menu.Show(this, new Point(MenuRect(index).X, S(43)));
        }
        public void SetPaper(string color) { Data.Paper = color; Editor.BackColor = Classic.Paper(color); Invalidate(); app.ScheduleSave(); }

        private void RenameNote()
        {
            using (var dialog = new Form { Text = "付箋の名前", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, BackColor = Classic.Face, ClientSize = new Size(340, 110), Font = new Font("MS UI Gothic", 10) })
            {
                var field = new TextBox { Text = Data.Title, MaxLength = 120, Left = 12, Top = 28, Width = 316 };
                dialog.Controls.Add(new Label { Text = "タイトル", Left = 12, Top = 8, Width = 200 }); dialog.Controls.Add(field);
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 164, Top = 70, Width = 78 };
                var cancel = new Button { Text = "キャンセル", DialogResult = DialogResult.Cancel, Left = 250, Top = 70, Width = 78 };
                dialog.Controls.Add(ok); dialog.Controls.Add(cancel); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                dialog.Shown += delegate { field.SelectAll(); field.Focus(); };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Data.Title = String.IsNullOrWhiteSpace(field.Text) ? "MEMO" : field.Text.Trim();
                Text = Data.Title + " — Retro Notes 95"; UpdateFonts(); app.ScheduleSave(); Invalidate();
            }
        }
        private void ExportText()
        {
            string filename = String.Join("_", Data.Title.Split(Path.GetInvalidFileNameChars())) + ".txt";
            using (var dialog = new SaveFileDialog { Title = "メモを書き出す", Filter = "テキストファイル (*.txt)|*.txt", FileName = filename })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { File.WriteAllText(dialog.FileName, Data.Body, new UTF8Encoding(true)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "書き出せませんでした"); }
            }
        }
        private void OpenDataFolder()
        {
            try { Process.Start(new ProcessStartInfo(app.Store.DirectoryPath) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "フォルダーを開けませんでした"); }
        }
        private void ShowHelp()
        {
            MessageBox.Show(this,
                "・白いところに直接入力。少し待つと自動保存。\n・青いバーをドラッグすると移動。\n・右下をドラッグすると大きさを変更。\n・タイトルをダブルクリック / F2 で名前を変更。\n・PIN ON は常に手前に表示。\n・×と最小化ボタンは付箋を隠します。内容は残ります。\n・時計の近くのアイコンをダブルクリックすると再表示。\n・Win+Dで隠れたときも、アイコンから再表示できます。\n\nCtrl+N: 新しい付箋\nCtrl+S: 今すぐ保存\nCtrl+W: 隠す\nCtrl+Shift+P: 常に手前\n\n自動起動は View → Windowsと一緒に起動、で設定できます。",
                "Retro Notes 95 の使い方", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.N)) { app.NewNote(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { app.Flush(true); return true; }
            if (keyData == (Keys.Control | Keys.W)) { app.HideNote(this); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.P)) { TogglePin(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.E)) { ExportText(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.S)) { app.ExportBackup(this); return true; }
            if (keyData == Keys.F2) { RenameNote(); return true; }
            if (keyData == Keys.F1) { ShowHelp(); return true; }
            if (keyData == (Keys.Control | Keys.A)) { Editor.SelectAll(); return true; }
            if (keyData == (Keys.Alt | Keys.F)) { ShowMenu(0); return true; }
            if (keyData == (Keys.Alt | Keys.E)) { ShowMenu(1); return true; }
            if (keyData == (Keys.Alt | Keys.V)) { ShowMenu(2); return true; }
            if (keyData == (Keys.Alt | Keys.H)) { ShowMenu(3); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!app.Closing && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; app.HideNote(this); }
            else if (!app.Closing) app.Flush(false);
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                tips.Dispose();
                foreach (var f in new[] { titleFont, menuFont, statusFont, bodyFont }) if (f != null) f.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
