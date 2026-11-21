using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RetroNotes95
{
    public static class Tests
    {
        private static int checks;
        private static void Check(bool value, string name)
        {
            if (!value) throw new Exception("FAIL: " + name);
            checks++; Console.WriteLine("PASS: " + name);
        }
        [STAThread]
        public static int Main(string[] args)
        {
            string root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
            try
            {
                StorageChecks(Path.Combine(root, "storage"));
                if (args.Length > 1 && args[1] == "--ui") UiChecks(root);
                Console.WriteLine("ALL " + checks + " CHECKS PASSED"); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        private static void StorageChecks(string dir)
        {
            var store = new NoteStore(dir); var state = store.Load();
            Check(state.Notes.Count == 0, "first run empty state");
            state.Notes.Add(new NoteData { Title = "夢の続き", Body = "日本語の付箋\r\n海とイルカ 🐬\r\n<タグ>もそのまま", X = -1000, Y = 140, Width = 451, Height = 312, Pinned = true, Paper = "pink" });
            store.Save(state); var restored = new NoteStore(dir).Load();
            Check(restored.Notes[0].Body == state.Notes[0].Body, "Japanese, emoji and multiline round trip");
            Check(restored.Notes[0].X == -1000 && restored.Notes[0].Width == 451 && restored.Notes[0].Pinned && restored.Notes[0].Paper == "pink", "geometry and display settings persist");
            state.Notes[0].Body = "2回目の保存"; store.Save(state);
            Check(NoteStore.Read(store.BackupPath).Notes[0].Body.Contains("イルカ"), "previous content retained as backup");
            Check(!Directory.GetFiles(dir, "*.tmp-*").Any(), "atomic save leaves no temp file");
            string export = Path.Combine(dir, "export.json"); NoteStore.WriteAtomic(state, export, null);
            Check(NoteStore.Read(export).Notes[0].Body == "2回目の保存", "backup export round trip");
            File.WriteAllText(store.FilePath, "broken data");
            var recovering = new NoteStore(dir); var recovered = recovering.Load();
            Check(recovered.Notes[0].Body.Contains("イルカ"), "corrupt primary recovers previous backup");
            Check(Directory.GetFiles(dir, "notes.json.unreadable-*.json").Length == 1, "corrupt original retained");
            recovering.Save(recovered);
            Check(NoteStore.Read(store.BackupPath).Notes[0].Body.Contains("イルカ"), "recovery save preserves healthy backup");
            var untrusted = new AppState { Version = 999 };
            bool rejected = false; try { untrusted.Validate(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "unsupported backup version rejected");
            var second = state.Notes[0].Copy(); state.Notes.Add(second); state.Validate();
            Check(state.Notes[0].Id != state.Notes[1].Id, "duplicate note IDs safely separated");
            Rectangle r = Placement.KeepVisible(new Rectangle(5000, 4000, 440, 330), new[] { new Rectangle(0, 0, 1920, 1040) });
            Check(r.X >= 0 && r.Y >= 0 && r.Right <= 1920 && r.Bottom <= 1040, "removed monitor placement recovers onscreen");
            Rectangle negative = Placement.KeepVisible(new Rectangle(-1500, 200, 440, 330), new[] { new Rectangle(-1920, 0, 1920, 1080), new Rectangle(0, 0, 1920, 1040) });
            Check(negative.X == -1500 && negative.Y == 200, "negative monitor coordinates preserved");
        }

        private static void UiChecks(string root)
        {
            Application.SetCompatibleTextRenderingDefault(false);
            using (var assets = new ThemeAssets(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets")))
            using (var app = new NoteApplication(new NoteStore(Path.Combine(root, "ui")), assets, null, true))
            {
                Check(assets.PixelFamily != null && assets.TitleFamily != null, "bundled fonts loaded");
                var first = app.Windows[0]; first.Data.Title = "TODAY";
                Check(first.Editor.Font.Name.IndexOf("DotGothic", StringComparison.OrdinalIgnoreCase) >= 0,
                    "editor uses the bundled Japanese pixel font");
                first.Bounds = new Rectangle(40, 40, 440, 330);
                first.Editor.Text = "今日のメモ\r\n\r\n・部屋の音楽を選ぶ\r\n・アイデアを忘れないうちに\r\n・夢の続きを、保存する。\r\n\r\nDigital daydream, 1995.";
                var second = app.NewNote(); second.Data.Title = "DAYDREAM"; second.Bounds = new Rectangle(520, 100, 340, 265);
                second.SetPaper("pink"); second.Editor.Text = "好きなものリスト\r\n\r\nピンクの空\r\n透明なイルカ\r\n古いパソコン\r\n\r\nまた明日。"; second.TogglePin();
                int stage = 0; Exception failure = null;
                var timer = new Timer { Interval = 1100 };
                timer.Tick += delegate
                {
                    try
                    {
                        if (stage++ == 0)
                        {
                            var saved = NoteStore.Read(app.Store.FilePath);
                            Check(saved.Notes.Count == 2 && saved.Notes[0].Body == first.Editor.Text, "editor autosaves after idle without explicit save");
                            Check(saved.Notes[0].Width == 440 && saved.Notes[1].Pinned, "move resize and pin autosaved");
                            first.Close(); Check(first.Data.Hidden && !first.Visible && app.Windows.Count == 2, "close hides without deleting");
                            app.ShowNote(first); Check(first.Visible && !first.Data.Hidden, "hidden note restored");
                            first.Editor.Select(0, 0); second.Editor.Select(0, 0);
                            app.Flush(true);
                            // Xvfb has no window manager: exercise the active-window paint state explicitly.
                            typeof(NoteWindow).GetMethod("OnActivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                                .Invoke(first, new object[] { EventArgs.Empty });
                            using (var image = new Bitmap(942, 425))
                            using (var graphics = Graphics.FromImage(image))
                            {
                                graphics.Clear(Color.FromArgb(217, 213, 227));
                                using (var a = new Bitmap(first.Width, first.Height))
                                using (var b = new Bitmap(second.Width, second.Height))
                                {
                                    first.DrawToBitmap(a, first.ClientRectangle); second.DrawToBitmap(b, second.ClientRectangle);
                                    graphics.DrawImageUnscaled(a, 26, 30); graphics.DrawImageUnscaled(b, 550, 112);
                                }
                                image.Save(Path.Combine(root, "retro-notes95-preview.png"), ImageFormat.Png);
                            }
                        }
                        else
                        {
                            Check(app.Flush(true), "final GUI save succeeds");
                            var restored = new NoteStore(app.Store.DirectoryPath).Load();
                            Check(restored.Notes[1].Paper == "pink" && restored.Notes[1].Body.Contains("イルカ"), "GUI content survives reload");
                            timer.Stop(); app.ExitApplication();
                        }
                    }
                    catch (Exception e) { failure = e; timer.Stop(); Application.ExitThread(); }
                };
                timer.Start(); Application.Run(app); timer.Dispose();
                if (failure != null) throw failure;
            }
        }
    }
}
