using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RetroNotes95
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Native.SetupDpi();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show("操作を完了できませんでした。\n" + e.Exception.Message, "Retro Notes 95", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            string name = (Native.IsWindows ? @"Local\" : "") + "RetroNotes95";
            bool owned = false;
            using (var mutex = new Mutex(false, name + ".Lock"))
            {
                try { owned = mutex.WaitOne(0, false); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned)
                {
                    try { using (var ev = EventWaitHandle.OpenExisting(name + ".Show")) ev.Set(); }
                    catch { MessageBox.Show("すでに起動しています。時計の近くの付箋アイコンから開いてください。", "Retro Notes 95"); }
                    return;
                }
                try
                {
                    using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show"))
                    using (var assets = new ThemeAssets(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets")))
                    {
                        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroNotes95");
                        using (var app = new NoteApplication(new NoteStore(dir), assets, signal, false)) Application.Run(app);
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show("起動できませんでした。ZIPをすべて展開してから起動してください。\n\n" + e.Message,
                        "Retro Notes 95", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }
    }

    public sealed class NoteApplication : ApplicationContext
    {
        public readonly AppState State;
        public readonly NoteStore Store;
        public readonly ThemeAssets Assets;
        public readonly List<NoteWindow> Windows = new List<NoteWindow>();
        public bool Closing { get; private set; }
        public bool Dirty { get; private set; }
        public bool SaveFailed { get; private set; }
        public string LastSaveError { get; private set; }
        public DateTime LastSaved { get; private set; }
        private readonly System.Windows.Forms.Timer saveTimer;
        private readonly System.Windows.Forms.Timer signalTimer;
        private readonly NotifyIcon tray;
        private bool saveWarningShown;

        public NoteApplication(NoteStore store, ThemeAssets assets, EventWaitHandle signal, bool testing)
        {
            Store = store; Assets = assets; State = Store.Load();
            saveTimer = new System.Windows.Forms.Timer { Interval = 600 };
            saveTimer.Tick += delegate { saveTimer.Stop(); Flush(false); };
            signalTimer = new System.Windows.Forms.Timer { Interval = 300 };
            if (signal != null) signalTimer.Tick += delegate { if (signal.WaitOne(0)) ShowAllNotes(); };
            signalTimer.Start();
            if (!testing)
            {
                tray = new NotifyIcon { Icon = assets.Icon, Text = "Retro Notes 95 — ダブルクリックで付箋を表示", Visible = true };
                tray.DoubleClick += delegate { ShowAllNotes(); };
                var menu = Classic.Menu();
                menu.Opening += delegate { FillTrayMenu(menu); };
                tray.ContextMenuStrip = menu;
            }
            foreach (var data in State.Notes.ToArray()) Open(data);
            if (State.Notes.Count == 0) NewNote();
            if (!testing && !String.IsNullOrEmpty(Store.RecoveryMessage)) MessageBox.Show(Store.RecoveryMessage, "付箋の復元");
        }

        private void FillTrayMenu(ContextMenuStrip menu)
        {
            menu.Items.Clear();
            menu.Items.Add("新しい付箋", null, delegate { NewNote(); });
            menu.Items.Add("付箋をすべて表示", null, delegate { ShowAllNotes(); });
            menu.Items.Add("付箋をすべて隠す", null, delegate { foreach (var w in Windows.ToArray()) HideNote(w); });
            menu.Items.Add(new ToolStripSeparator());
            foreach (var window in Windows.ToArray())
            {
                var local = window;
                var item = new ToolStripMenuItem((window.Data.Hidden ? "○ " : "● ") + window.Data.Title);
                item.Click += delegate { ShowNote(local); }; menu.Items.Add(item);
            }
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("バックアップを書き出す…", null, delegate { ExportBackup(null); });
            menu.Items.Add("終了", null, delegate { ExitApplication(); });
        }

        private NoteWindow Open(NoteData data)
        {
            var window = new NoteWindow(this, data); Windows.Add(window);
            if (!data.Hidden) window.Show();
            return window;
        }
        public NoteWindow NewNote()
        {
            int offset = State.Notes.Count % 9 * 26;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            var data = new NoteData { Title = "MEMO " + (State.Notes.Count + 1).ToString("00"), X = area.Left + 100 + offset, Y = area.Top + 80 + offset };
            State.Notes.Add(data); var window = Open(data); window.Activate(); ScheduleSave(); return window;
        }
        public void ShowNote(NoteWindow window)
        {
            window.Data.Hidden = false;
            if (window.WindowState == FormWindowState.Minimized) window.WindowState = FormWindowState.Normal;
            window.EnsureOnScreen(); window.Show(); window.Activate(); ScheduleSave();
        }
        public void ShowAllNotes()
        {
            if (Windows.Count == 0) { NewNote(); return; }
            foreach (var window in Windows.ToArray()) ShowNote(window);
        }
        public void HideNote(NoteWindow window)
        {
            window.RememberBounds(); window.Data.Hidden = true; window.Hide(); ScheduleSave();
        }
        public void DeleteNote(NoteWindow window)
        {
            if (MessageBox.Show(window, "「" + window.Data.Title + "」を削除しますか？\n隠すだけなら右上の×を使えます。", "付箋を削除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            // Save the current state once so .bak retains the just-deleted note.
            if (!Flush(true)) return;
            State.Notes.Remove(window.Data); Windows.Remove(window); window.Dispose();
            ScheduleSave(); Flush(true);
        }
        public void ScheduleSave()
        {
            Dirty = true; saveTimer.Stop(); saveTimer.Start();
            foreach (var w in Windows) w.InvalidateStatus();
        }
        public bool Flush(bool alert)
        {
            saveTimer.Stop();
            try
            {
                Store.Save(State); Dirty = false; SaveFailed = false; LastSaved = DateTime.Now; LastSaveError = null; saveWarningShown = false;
                foreach (var w in Windows) w.InvalidateStatus();
                return true;
            }
            catch (Exception e)
            {
                SaveFailed = true; LastSaveError = e.Message;
                foreach (var w in Windows) w.InvalidateStatus();
                if (alert || !saveWarningShown)
                {
                    saveWarningShown = true;
                    MessageBox.Show("付箋を保存できませんでした。メモは画面に残っています。\nFile → テキストを書き出す、で別の場所に保存できます。\n\n" + e.Message,
                        "保存できませんでした", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }
        }
        public void ExitApplication()
        {
            if (Closing) return;
            foreach (var window in Windows) window.RememberBounds();
            if (!Flush(true)) return;
            Closing = true;
            foreach (var w in Windows.ToArray()) w.Close();
            if (tray != null) tray.Visible = false;
            ExitThread();
        }

        public void ExportBackup(IWin32Window owner)
        {
            using (var dialog = new SaveFileDialog { Title = "付箋のバックアップ", Filter = "付箋バックアップ (*.json)|*.json", FileName = "RetroNotes95-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json" })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                try { NoteStore.WriteAtomic(State, dialog.FileName, null); }
                catch (Exception e) { MessageBox.Show(owner, e.Message, "書き出せませんでした"); }
            }
        }
        public void ImportBackup(IWin32Window owner)
        {
            using (var dialog = new OpenFileDialog { Title = "バックアップを付箋として追加", Filter = "付箋バックアップ (*.json)|*.json" })
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    var imported = NoteStore.Read(dialog.FileName);
                    if (State.Notes.Count + imported.Notes.Count > 1000) throw new InvalidDataException("付箋が多すぎます。");
                    if (MessageBox.Show(owner, imported.Notes.Count + "枚の付箋を追加します。現在の付箋は残ります。", "バックアップを取り込む", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                    foreach (var source in imported.Notes)
                    {
                        var note = source.Copy(); note.Id = Guid.NewGuid().ToString("N"); note.Hidden = false;
                        State.Notes.Add(note); Open(note);
                    }
                    ScheduleSave(); Flush(true);
                }
                catch (Exception e) { MessageBox.Show(owner, e.Message, "取り込めませんでした"); }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Closing = true;
                saveTimer.Dispose(); signalTimer.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                foreach (var w in Windows.ToArray()) w.Dispose(); Windows.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
