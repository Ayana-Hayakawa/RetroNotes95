using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace RetroNotes95
{
    [DataContract]
    public sealed class NoteData
    {
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Title = "MEMO";
        [DataMember] public string Body = "";
        [DataMember] public int X = 120;
        [DataMember] public int Y = 100;
        [DataMember] public int Width = 440;
        [DataMember] public int Height = 330;
        [DataMember] public int SavedDpi = 96;
        [DataMember] public bool Pinned;
        [DataMember] public bool Hidden;
        [DataMember] public string Paper = "white";
        [DataMember] public string FontName = "pixel";
        [DataMember] public int FontPixels = 16;

        [OnDeserializing]
        private void Defaults(StreamingContext context)
        {
            Id = Guid.NewGuid().ToString("N"); Title = "MEMO"; Body = "";
            X = 120; Y = 100; Width = 440; Height = 330; SavedDpi = 96;
            Paper = "white"; FontName = "pixel"; FontPixels = 16;
        }

        public NoteData Copy()
        {
            return (NoteData)MemberwiseClone();
        }

        public void Normalize()
        {
            if (String.IsNullOrWhiteSpace(Id)) Id = Guid.NewGuid().ToString("N");
            Title = String.IsNullOrWhiteSpace(Title) ? "MEMO" : Title.Trim();
            if (Title.Length > 120) Title = Title.Substring(0, 120);
            Body = Body ?? "";
            if (Body.Length > 1000000) throw new InvalidDataException("付箋の文字数が多すぎます。上限は100万文字です。");
            Width = Math.Max(280, Math.Min(Width, 5000));
            Height = Math.Max(170, Math.Min(Height, 5000));
            SavedDpi = Math.Max(96, Math.Min(SavedDpi, 480));
            FontPixels = new[] { 12, 16, 20, 24, 28 }.Contains(FontPixels) ? FontPixels : 16;
            FontName = FontName == "standard" ? "standard" : "pixel";
            if (!new[] { "white", "pink", "blue", "mint", "cream" }.Contains(Paper)) Paper = "white";
        }
    }

    [DataContract]
    public sealed class AppState
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<NoteData> Notes = new List<NoteData>();

        public void Validate()
        {
            if (Version != 1) throw new InvalidDataException("このバックアップの形式には対応していません。");
            if (Notes == null) Notes = new List<NoteData>();
            if (Notes.Count > 1000) throw new InvalidDataException("付箋は1000枚まで読み込めます。");
            var ids = new HashSet<string>();
            foreach (var note in Notes)
            {
                if (note == null) throw new InvalidDataException("付箋のデータが正しくありません。");
                note.Normalize();
                if (!ids.Add(note.Id)) { note.Id = Guid.NewGuid().ToString("N"); ids.Add(note.Id); }
            }
        }
    }

    public sealed class NoteStore
    {
        public readonly string DirectoryPath;
        public string FilePath { get { return Path.Combine(DirectoryPath, "notes.json"); } }
        public string BackupPath { get { return FilePath + ".bak"; } }
        public string RecoveryMessage { get; private set; }
        private bool primaryHealthy = true;

        public NoteStore(string directory) { DirectoryPath = directory; }

        public AppState Load()
        {
            Directory.CreateDirectory(DirectoryPath);
            if (!File.Exists(FilePath))
            {
                if (File.Exists(BackupPath))
                {
                    try { RecoveryMessage = "バックアップから付箋を復元しました。"; return Read(BackupPath); }
                    catch { RecoveryMessage = "バックアップを読み込めませんでした。元のファイルはそのまま残しています。"; }
                }
                return new AppState();
            }
            try { return Read(FilePath); }
            catch (Exception)
            {
                primaryHealthy = false;
                // Retain the unreadable original before any future write.
                string retained = FilePath + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff") + ".json";
                File.Copy(FilePath, retained, false);
                if (File.Exists(BackupPath))
                {
                    try
                    {
                        var recovered = Read(BackupPath);
                        RecoveryMessage = "保存データを読み込めなかったため、直前のバックアップから復元しました。元のファイルも保存フォルダーに残しています。";
                        return recovered;
                    }
                    catch { }
                }
                RecoveryMessage = "保存データを読み込めませんでした。元のファイルは保存フォルダーに残しています。File → バックアップを取り込む、で復元できます。";
                return new AppState();
            }
        }

        public static AppState Read(string path)
        {
            if (new FileInfo(path).Length > 32L * 1024L * 1024L) throw new InvalidDataException("バックアップは32MBまで読み込めます。");
            using (var stream = File.OpenRead(path))
            {
                var state = (AppState)new DataContractJsonSerializer(typeof(AppState)).ReadObject(stream);
                if (state == null) throw new InvalidDataException("付箋データがありません。");
                state.Validate();
                return state;
            }
        }

        public void Save(AppState state)
        {
            state.Validate();
            Directory.CreateDirectory(DirectoryPath);
            WriteAtomic(state, FilePath, primaryHealthy ? BackupPath : null);
            primaryHealthy = true;
        }

        public static void WriteAtomic(AppState state, string path, string backup)
        {
            state.Validate();
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                {
                    new DataContractJsonSerializer(typeof(AppState)).WriteObject(stream, state);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, backup, true);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    public static class Placement
    {
        public static Rectangle KeepVisible(Rectangle desired, Rectangle[] areas)
        {
            if (areas == null || areas.Length == 0) return desired;
            Rectangle area = areas.OrderByDescending(a => IntersectionArea(a, desired)).First();
            if (!areas.Any(a => a.Contains(new Point(desired.X + 40, desired.Y + 12))))
            { desired.X = area.Left + 40; desired.Y = area.Top + 40; }
            int w = Math.Min(desired.Width, area.Width);
            int h = Math.Min(desired.Height, area.Height);
            return new Rectangle(Math.Max(area.Left, Math.Min(desired.X, area.Right - w)),
                Math.Max(area.Top, Math.Min(desired.Y, area.Bottom - h)), w, h);
        }

        private static long IntersectionArea(Rectangle a, Rectangle b)
        {
            Rectangle r = Rectangle.Intersect(a, b);
            return (long)Math.Max(0, r.Width) * Math.Max(0, r.Height);
        }
    }
}
