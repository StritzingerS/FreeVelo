#!/usr/bin/env python3
"""
Turns a GPX track of a climb into FreeVelo's climb data (src/FreeVelo.Core/World/Gen/Climbs/<id>.csv):
one point every 5 m along the road, x/z in metres relative to the summit (x = east, z = north), elevation in metres
smoothed over 200 m (GPS elevation has spikes of 30 % and more over a few metres; the published per-km gradients
are kept). Only the way up is used (the track is cut at its highest point), optionally only its last <climb_km>.

  python3 tools/climbs/process_gpx.py stelvio "Passo dello Stelvio" Stelvio.gpx
  python3 tools/climbs/process_gpx.py alpe-dhuez "Alpe d'Huez" Alpe.gpx --climb-km 13.8
"""
import argparse, math, os, re

SPACING = 5.0
ELEVATION_WINDOW = 200.0


def load(path):
    text = open(path, encoding="utf-8").read()
    points = []
    for m in re.finditer(r"<trkpt\b([^>]*)>(.*?)</trkpt>", text, re.S):
        lat = float(re.search(r'lat="([-\d.]+)"', m.group(1)).group(1))
        lon = float(re.search(r'lon="([-\d.]+)"', m.group(1)).group(1))
        ele = re.search(r"<ele>([-\d.]+)</ele>", m.group(2))
        points.append((lat, lon, float(ele.group(1))))
    return points


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("id")
    ap.add_argument("name")
    ap.add_argument("gpx")
    ap.add_argument("--climb-km", type=float, default=None, help="use only the last N km before the summit")
    args = ap.parse_args()

    pts = load(args.gpx)
    top = max(range(len(pts)), key=lambda i: pts[i][2])
    pts = pts[: top + 1]
    lat0, lon0 = pts[-1][0], pts[-1][1]
    k = math.cos(math.radians(lat0))
    xs = [(p[1] - lon0) * 111320.0 * k for p in pts]
    zs = [(p[0] - lat0) * 110540.0 for p in pts]
    es = [p[2] for p in pts]
    d = [0.0]
    for i in range(1, len(pts)):
        d.append(d[-1] + math.hypot(xs[i] - xs[i - 1], zs[i] - zs[i - 1]))

    # Elevation: first resampled evenly every metre along the road (GPX points can be hundreds of metres apart,
    # e.g. in tunnels), then a moving average over 200 m of road.
    def raw_at(dist):
        j = max(0, min(len(d) - 2, next((i for i in range(1, len(d)) if d[i] >= dist), len(d) - 1) - 1))
        f = 0.0 if d[j + 1] == d[j] else max(0.0, min(1.0, (dist - d[j]) / (d[j + 1] - d[j])))
        return es[j] + (es[j + 1] - es[j]) * f

    metres = int(d[-1]) + 1
    even = [raw_at(min(i, d[-1])) for i in range(metres + 1)]
    half = int(ELEVATION_WINDOW / 2)
    prefix = [0.0]
    for v in even:
        prefix.append(prefix[-1] + v)
    averaged = []
    for i in range(len(even)):
        a, b = max(0, i - half), min(len(even) - 1, i + half)
        averaged.append((prefix[b + 1] - prefix[a]) / (b - a + 1))
    smooth = [averaged[min(len(averaged) - 1, int(round(x)))] for x in d]

    # Keep the true foot and summit heights: the averaging window is cut short at both ends, so the difference there
    # is blended out over the first / last kilometre (no step at the summit).
    blend = 1000.0
    start_error, end_error = es[0] - smooth[0], es[-1] - smooth[-1]
    for i in range(len(d)):
        smooth[i] += start_error * max(0.0, 1 - d[i] / blend) + end_error * max(0.0, 1 - (d[-1] - d[i]) / blend)

    def at(dist):
        j = max(0, min(len(d) - 2, next((i for i in range(1, len(d)) if d[i] >= dist), len(d) - 1) - 1))
        f = 0.0 if d[j + 1] == d[j] else (dist - d[j]) / (d[j + 1] - d[j])
        f = max(0.0, min(1.0, f))
        return (xs[j] + (xs[j + 1] - xs[j]) * f, zs[j] + (zs[j + 1] - zs[j]) * f, smooth[j] + (smooth[j + 1] - smooth[j]) * f)

    start = 0.0 if args.climb_km is None else max(0.0, d[-1] - args.climb_km * 1000)
    out = []
    s = start
    while s < d[-1]:
        out.append(at(s))
        s += SPACING
    out.append((xs[-1], zs[-1], smooth[-1]))

    # No smoothing of the line: route-planner tracks follow the mapped road (no GPS jitter), and every metre of the
    # length and every hairpin is kept.
    line = out

    target = os.path.join(os.path.dirname(__file__), "..", "..", "src", "FreeVelo.Core", "World", "Gen", "Climbs", args.id + ".csv")
    with open(target, "w", encoding="utf-8", newline="\n") as f:
        f.write(f"# {args.name}\n# x (m east of the summit), z (m north), elevation (m); one point every {SPACING:g} m\n")
        for x, z, e in line:
            f.write(f"{x:.1f},{z:.1f},{e:.1f}\n")
    length = sum(math.hypot(line[i][0] - line[i - 1][0], line[i][1] - line[i - 1][1]) for i in range(1, len(line)))
    print(f"{args.id}: {len(line)} points, {length/1000:.2f} km, {line[0][2]:.0f} → {line[-1][2]:.0f} m -> {os.path.normpath(target)}")


if __name__ == "__main__":
    main()
