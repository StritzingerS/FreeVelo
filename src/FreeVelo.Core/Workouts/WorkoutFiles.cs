using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FreeVelo.Core.Workouts
{
    /// <summary>
    /// Reads and writes workouts.
    /// <list type="bullet">
    /// <item><b>.zwo</b> (Zwift XML, the de-facto standard; also our own storage format). FreeVelo extensions:
    /// <c>&lt;Repeat Count="n"&gt;</c> for nested repeats and <c>PowerW</c>/<c>PowerLowW</c>/<c>PowerHighW</c> for absolute watts,
    /// <c>name_de</c>/<c>description_de</c> for German texts. <see cref="ToZwo"/> with <c>strict = true</c> writes plain ZWO.</item>
    /// <item><b>.erg</b> (watts) and <b>.mrc</b> (% FTP) text files: import only.</item>
    /// </list>
    /// </summary>
    public static class WorkoutFiles
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Parses a workout file by extension (.zwo, .erg, .mrc).</summary>
        public static Workout Load(string fileName, string content)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            string id = Path.GetFileNameWithoutExtension(fileName);
            switch (ext)
            {
                case ".zwo":
                case ".xml":
                    return FromZwo(content, id);
                case ".erg":
                    return FromErgMrc(content, id, percent: false);
                case ".mrc":
                    return FromErgMrc(content, id, percent: true);
                default:
                    throw new FormatException($"Unsupported workout file type '{ext}' (supported: .zwo, .erg, .mrc).");
            }
        }

        // ------------------------------------------------------------------ ZWO

        public static Workout FromZwo(string xml, string id)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (XmlException e)
            {
                throw new FormatException("Invalid workout XML: " + e.Message, e);
            }

            var root = doc.Root ?? throw new FormatException("Empty workout file.");
            string name = Text(root, "name") ?? id;
            var workout = new Workout(id, new LocalizedText(name, Text(root, "name_de")))
            {
                Author = Text(root, "author"),
                Category = WorkoutCategory.Custom,
            };
            string? description = Text(root, "description");
            if (description != null) workout.Description = new LocalizedText(description, Text(root, "description_de"));

            string? category = Text(root, "category");
            if (category != null && Enum.TryParse(category, true, out WorkoutCategory parsed)) workout.Category = parsed;

            var blocks = root.Element("workout") ?? throw new FormatException("Missing <workout> element.");
            workout.Blocks.AddRange(ReadBlocks(blocks));
            if (workout.Blocks.Count == 0) throw new FormatException("The workout has no blocks.");
            return workout;
        }

        private static IEnumerable<WorkoutBlock> ReadBlocks(XElement parent)
        {
            foreach (var e in parent.Elements())
            {
                switch (e.Name.LocalName.ToLowerInvariant())
                {
                    case "steadystate":
                        yield return WithExtras(new SteadyBlock(Duration(e), Target(e, "Power", "PowerW")), e);
                        break;

                    case "warmup":
                    case "cooldown":
                    case "ramp":
                        yield return WithExtras(new RampBlock(Duration(e), Target(e, "PowerLow", "PowerLowW"), Target(e, "PowerHigh", "PowerHighW")), e);
                        break;

                    case "freeride":
                        yield return WithExtras(new FreeRideBlock(Duration(e)), e);
                        break;

                    case "maxeffort":
                        yield return WithExtras(new FreeRideBlock(Duration(e), isMaxEffort: true), e);
                        break;

                    case "intervalst":
                    {
                        int repeat = Math.Max(1, (int)Num(e, "Repeat", 1));
                        var on = new SteadyBlock(Int(e, "OnDuration"), Target(e, "OnPower", "OnPowerW", "PowerOnHigh"));
                        var off = new SteadyBlock(Int(e, "OffDuration"), Target(e, "OffPower", "OffPowerW", "PowerOffLow"));
                        var cadence = Cadence(e, "Cadence");
                        if (cadence.HasValue) on.Cadence = cadence;
                        var resting = Cadence(e, "CadenceResting");
                        if (resting.HasValue) off.Cadence = resting;
                        foreach (var cue in Cues(e))
                        {
                            // Cue offsets in IntervalsT refer to the whole block: put them into the first on-interval.
                            if (cue.OffsetS < on.DurationS) on.Cues.Add(cue);
                        }

                        yield return new RepeatBlock(repeat, new WorkoutBlock[] { on, off });
                        break;
                    }

                    case "repeat":
                        yield return new RepeatBlock(Math.Max(1, (int)Num(e, "Count", 1)), ReadBlocks(e).ToList());
                        break;

                    case "textevent":
                        break; // only valid inside blocks

                    default:
                        throw new FormatException($"Unknown workout block <{e.Name.LocalName}>.");
                }
            }
        }

        private static TimedBlock WithExtras(TimedBlock block, XElement e)
        {
            var cadence = Cadence(e, "Cadence");
            if (cadence.HasValue) block.Cadence = cadence;
            block.Cues.AddRange(Cues(e));
            return block;
        }

        private static IEnumerable<TextCue> Cues(XElement e) =>
            e.Elements().Where(c => c.Name.LocalName.Equals("textevent", StringComparison.OrdinalIgnoreCase))
                .Select(c => new TextCue(
                    (int)Num(c, "timeoffset", 0),
                    new LocalizedText(Attr(c, "message") ?? string.Empty, Attr(c, "message_de"))));

        private static int Duration(XElement e) => Int(e, "Duration");

        private static int Int(XElement e, string attribute)
        {
            int value = (int)Math.Round(Num(e, attribute, double.NaN));
            if (value <= 0) throw new FormatException($"<{e.Name.LocalName}> needs a positive {attribute}.");
            return value;
        }

        /// <summary>ZWO power is a fraction of FTP (0.95); FreeVelo may store absolute watts instead.</summary>
        private static PowerTarget Target(XElement e, string fractionAttribute, string wattsAttribute, string? fallbackFraction = null)
        {
            double watts = Num(e, wattsAttribute, double.NaN);
            if (!double.IsNaN(watts)) return PowerTarget.Watts(watts);
            double fraction = Num(e, fractionAttribute, double.NaN);
            if (double.IsNaN(fraction) && fallbackFraction != null) fraction = Num(e, fallbackFraction, double.NaN);
            if (double.IsNaN(fraction)) throw new FormatException($"<{e.Name.LocalName}> needs {fractionAttribute}.");
            return PowerTarget.Percent(Math.Round(fraction * 100.0, 1));
        }

        private static CadenceRange? Cadence(XElement e, string attribute)
        {
            double rpm = Num(e, attribute, double.NaN);
            if (double.IsNaN(rpm) || rpm <= 0) return null;
            double low = Num(e, attribute + "Low", double.NaN);
            double high = Num(e, attribute + "High", double.NaN);
            return !double.IsNaN(low) && !double.IsNaN(high)
                ? new CadenceRange((int)low, (int)high)
                : new CadenceRange((int)rpm - 5, (int)rpm + 5);
        }

        private static string? Attr(XElement e, string name) =>
            e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

        private static double Num(XElement e, string name, double fallback)
        {
            string? text = Attr(e, name);
            return text != null && double.TryParse(text, NumberStyles.Float, Inv, out double v) ? v : fallback;
        }

        private static string? Text(XElement root, string element)
        {
            string? value = root.Element(element)?.Value?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Writes ZWO. <paramref name="strict"/> = true produces plain Zwift-compatible files:
        /// repeats are written as IntervalsT where possible (otherwise expanded) and watts are converted
        /// to % FTP using <paramref name="ftpW"/>.
        /// </summary>
        public static string ToZwo(Workout workout, bool strict = false, int ftpW = 250)
        {
            var root = new XElement("workout_file",
                new XElement("author", workout.Author ?? "FreeVelo"),
                new XElement("name", workout.Name.Get("en")),
                new XElement("description", workout.Description?.Get("en") ?? string.Empty),
                new XElement("sportType", "bike"));
            if (!strict)
            {
                if (workout.Name.Values.ContainsKey("de")) root.Add(new XElement("name_de", workout.Name.Get("de")));
                if (workout.Description != null && workout.Description.Values.ContainsKey("de")) root.Add(new XElement("description_de", workout.Description.Get("de")));
                root.Add(new XElement("category", workout.Category.ToString()));
            }

            var blocks = new XElement("workout");
            WriteBlocks(blocks, workout.Blocks, strict, ftpW);
            root.Add(blocks);

            var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true, Encoding = new UTF8Encoding(false) };
            var sb = new StringBuilder();
            using (var writer = XmlWriter.Create(sb, settings)) root.WriteTo(writer);
            return sb.ToString();
        }

        private static void WriteBlocks(XElement parent, IEnumerable<WorkoutBlock> blocks, bool strict, int ftpW)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case SteadyBlock s:
                        parent.Add(Extras(new XElement("SteadyState", Dur(s), Power("Power", s.Target, strict, ftpW)), s));
                        break;

                    case RampBlock r:
                        parent.Add(Extras(new XElement("Ramp", Dur(r), Power("PowerLow", r.From, strict, ftpW), Power("PowerHigh", r.To, strict, ftpW)), r));
                        break;

                    case FreeRideBlock f:
                        parent.Add(Extras(new XElement(f.IsMaxEffort ? "MaxEffort" : "FreeRide", Dur(f)), f));
                        break;

                    case RepeatBlock rep when strict && rep.Blocks.Count == 2 && rep.Blocks[0] is SteadyBlock on && rep.Blocks[1] is SteadyBlock off:
                    {
                        var e = new XElement("IntervalsT",
                            new XAttribute("Repeat", rep.Count),
                            new XAttribute("OnDuration", on.DurationS),
                            new XAttribute("OffDuration", off.DurationS),
                            new XAttribute("OnPower", Fraction(on.Target, ftpW)),
                            new XAttribute("OffPower", Fraction(off.Target, ftpW)));
                        if (on.Cadence.HasValue) e.Add(new XAttribute("Cadence", (on.Cadence.Value.Min + on.Cadence.Value.Max) / 2));
                        if (off.Cadence.HasValue) e.Add(new XAttribute("CadenceResting", (off.Cadence.Value.Min + off.Cadence.Value.Max) / 2));
                        foreach (var cue in on.Cues) e.Add(Cue(cue, strict));
                        parent.Add(e);
                        break;
                    }

                    case RepeatBlock rep when strict:
                        for (int i = 0; i < rep.Count; i++) WriteBlocks(parent, rep.Blocks, true, ftpW);
                        break;

                    case RepeatBlock rep:
                    {
                        var e = new XElement("Repeat", new XAttribute("Count", rep.Count));
                        WriteBlocks(e, rep.Blocks, false, ftpW);
                        parent.Add(e);
                        break;
                    }
                }
            }

            XElement Extras(XElement e, TimedBlock b)
            {
                if (b.Cadence.HasValue)
                {
                    e.Add(new XAttribute("Cadence", (b.Cadence.Value.Min + b.Cadence.Value.Max) / 2));
                    if (!strict)
                    {
                        e.Add(new XAttribute("CadenceLow", b.Cadence.Value.Min));
                        e.Add(new XAttribute("CadenceHigh", b.Cadence.Value.Max));
                    }
                }

                foreach (var cue in b.Cues) e.Add(Cue(cue, strict));
                return e;
            }
        }

        private static XAttribute Dur(TimedBlock b) => new XAttribute("Duration", b.DurationS);

        private static XAttribute Power(string name, PowerTarget target, bool strict, int ftpW) =>
            target.Unit == TargetUnit.Watts && !strict
                ? new XAttribute(name + "W", target.Value.ToString("0.#", Inv))
                : new XAttribute(name, Fraction(target, ftpW));

        private static string Fraction(PowerTarget target, int ftpW) => (target.ToPercent(ftpW) / 100.0).ToString("0.###", Inv);

        private static XElement Cue(TextCue cue, bool strict)
        {
            var e = new XElement("textevent", new XAttribute("timeoffset", cue.OffsetS), new XAttribute("message", cue.Text.Get("en")));
            if (!strict && cue.Text.Values.ContainsKey("de")) e.Add(new XAttribute("message_de", cue.Text.Get("de")));
            return e;
        }

        // ------------------------------------------------------------------ ERG / MRC

        /// <summary>
        /// ERG (watts) / MRC (% FTP): [COURSE DATA] contains "minutes value" points; equal values form steady
        /// blocks, different values ramps, two points at the same time a step. [COURSE TEXT] becomes cues.
        /// </summary>
        public static Workout FromErgMrc(string content, string id, bool percent)
        {
            var lines = content.Replace("\r", string.Empty).Split('\n').Select(l => l.Trim()).ToList();
            string? section = null;
            string name = id;
            string? description = null;
            var points = new List<(double Minutes, double Value)>();
            var texts = new List<(int Seconds, string Text)>();

            foreach (var line in lines)
            {
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal))
                {
                    section = line.ToUpperInvariant();
                    continue;
                }

                switch (section)
                {
                    case "[COURSE HEADER]":
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string key = line.Substring(0, eq).Trim().ToUpperInvariant();
                        string value = line.Substring(eq + 1).Trim();
                        if (key == "FILE NAME" && value.Length > 0) name = Path.GetFileNameWithoutExtension(value);
                        if (key == "DESCRIPTION" && value.Length > 0) description = value;
                        break;
                    }

                    case "[COURSE DATA]":
                    {
                        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2
                            && double.TryParse(parts[0], NumberStyles.Float, Inv, out double minutes)
                            && double.TryParse(parts[1], NumberStyles.Float, Inv, out double value))
                        {
                            points.Add((minutes, value));
                        }

                        break;
                    }

                    case "[COURSE TEXT]":
                    {
                        var parts = line.Split('\t');
                        if (parts.Length >= 2 && double.TryParse(parts[0].Trim(), NumberStyles.Float, Inv, out double seconds))
                        {
                            texts.Add(((int)seconds, parts[1].Trim()));
                        }

                        break;
                    }
                }
            }

            if (points.Count < 2) throw new FormatException("No [COURSE DATA] points found.");

            var workout = new Workout(id, new LocalizedText(name)) { Category = WorkoutCategory.Custom };
            if (description != null) workout.Description = new LocalizedText(description);

            int startS = 0;
            int blockStartS = 0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                var a = points[i];
                var b = points[i + 1];
                int endS = (int)Math.Round(b.Minutes * 60);
                int duration = endS - (int)Math.Round(a.Minutes * 60);
                if (duration <= 0) continue; // step change (same time twice)

                PowerTarget From(double v) => percent ? PowerTarget.Percent(v) : PowerTarget.Watts(v);
                TimedBlock block;
                if (Math.Abs(a.Value - b.Value) < 0.001 && workout.Blocks.LastOrDefault() is SteadyBlock last && last.Target.Equals(From(a.Value)))
                {
                    last.DurationS += duration; // merge consecutive steady parts with the same target
                    block = last;
                }
                else
                {
                    block = Math.Abs(a.Value - b.Value) < 0.001
                        ? (TimedBlock)new SteadyBlock(duration, From(a.Value))
                        : new RampBlock(duration, From(a.Value), From(b.Value));
                    workout.Blocks.Add(block);
                    blockStartS = startS;
                }

                foreach (var (seconds, text) in texts.Where(t => t.Seconds >= startS && t.Seconds < endS))
                {
                    block.Cues.Add(new TextCue(seconds - blockStartS, new LocalizedText(text)));
                }

                startS = endS;
            }

            return workout;
        }
    }
}
