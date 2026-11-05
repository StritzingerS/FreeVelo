using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FreeVelo.Core.Workouts
{
    /// <summary>
    /// The user's own workouts, stored as FreeVelo-ZWO files in one folder (one file per workout, file name = id).
    /// </summary>
    public sealed class WorkoutLibrary
    {
        private readonly List<Workout> _workouts = new List<Workout>();
        private readonly List<string> _errors = new List<string>();

        public WorkoutLibrary(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }

        public IReadOnlyList<Workout> Workouts => _workouts;

        /// <summary>Problems from the last <see cref="Reload"/> (unreadable files).</summary>
        public IReadOnlyList<string> Errors => _errors;

        /// <summary>Reads all .zwo files of the folder; broken files are skipped and reported in <see cref="Errors"/>.</summary>
        public void Reload()
        {
            _workouts.Clear();
            _errors.Clear();
            if (!Directory.Exists(Folder)) return;
            foreach (var path in Directory.GetFiles(Folder, "*.zwo").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    _workouts.Add(WorkoutFiles.FromZwo(File.ReadAllText(path, Encoding.UTF8), Path.GetFileNameWithoutExtension(path)));
                }
                catch (Exception e) when (e is FormatException || e is IOException)
                {
                    _errors.Add($"{Path.GetFileName(path)}: {e.Message}");
                }
            }

            _workouts.Sort((a, b) => string.Compare(a.Name.Get("en"), b.Name.Get("en"), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Saves (creates or replaces) a workout; returns the file path.</summary>
        public string Save(Workout workout)
        {
            Directory.CreateDirectory(Folder);
            if (string.IsNullOrWhiteSpace(workout.Id)) workout.Id = UniqueId(workout.Name.Get("en"));
            string path = PathOf(workout.Id);
            File.WriteAllText(path, WorkoutFiles.ToZwo(workout), new UTF8Encoding(false));
            int index = _workouts.FindIndex(w => w.Id == workout.Id);
            if (index >= 0) _workouts[index] = workout;
            else _workouts.Add(workout);
            return path;
        }

        /// <summary>Imports a .zwo / .erg / .mrc file under a new id; throws <see cref="FormatException"/> if invalid.</summary>
        public Workout Import(string fileName, string content)
        {
            var workout = WorkoutFiles.Load(fileName, content);
            workout.Id = UniqueId(Path.GetFileNameWithoutExtension(fileName));
            Save(workout);
            return workout;
        }

        public bool Delete(string id)
        {
            string path = PathOf(id);
            _workouts.RemoveAll(w => w.Id == id);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        public Workout? Find(string id) => _workouts.FirstOrDefault(w => w.Id == id);

        /// <summary>File-system safe id that is not used yet ("my-workout", "my-workout-2", …).</summary>
        public string UniqueId(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }

            string baseId = sb.ToString().Trim('-');
            if (baseId.Length == 0) baseId = "workout";
            if (baseId.Length > 60) baseId = baseId.Substring(0, 60);
            string id = baseId;
            for (int n = 2; _workouts.Any(w => w.Id == id) || File.Exists(PathOf(id)) || BuiltInWorkouts.Find(id) != null; n++) id = $"{baseId}-{n}";
            return id;
        }

        private string PathOf(string id) => Path.Combine(Folder, id + ".zwo");
    }
}
