using System.Globalization;
using System.Text.RegularExpressions;
using PwlEditor.Models;
using System.IO;

namespace PwlEditor.Services;

public static class PwlFileService
{
    public static IReadOnlyList<WavePoint> BuildRepeatedWave(IReadOnlyList<WavePoint> basePoints, double periodDuration, double totalDuration)
    {
        if (basePoints.Count == 0)
            return Array.Empty<WavePoint>();

        if (periodDuration <= 0)
            throw new ArgumentException("Die Periodendauer muss größer als 0 sein.");

        if (totalDuration < 0)
            throw new ArgumentException("Die Gesamtdauer darf nicht negativ sein.");

        var result = new List<WavePoint>();
        var cycle = 0;
        const double eps = 1e-12;

        while (cycle * periodDuration <= totalDuration + eps)
        {
            var offset = cycle * periodDuration;
            foreach (var point in basePoints.OrderBy(p => p.X))
            {
                var t = offset + point.X;
                if (t > totalDuration + eps)
                    break;

                var candidate = new WavePoint(t, point.Y);
                if (result.Count > 0)
                {
                    var prev = result[^1];
                    if (Math.Abs(prev.X - candidate.X) < eps && Math.Abs(prev.Y - candidate.Y) < eps)
                        continue;
                }
                result.Add(candidate);
            }
            cycle++;
        }

        return result;
    }

    public static void Export(string filePath, IReadOnlyList<WavePoint> repeatedWave)
    {
        using var writer = new StreamWriter(filePath, false);
        foreach (var point in repeatedWave)
        {
            writer.WriteLine($"{point.X.ToString("G17", CultureInfo.InvariantCulture)} {point.Y.ToString("G17", CultureInfo.InvariantCulture)}");
        }
    }

    public static IReadOnlyList<WavePoint> Import(string filePath)
    {
        var result = new List<WavePoint>();
        foreach (var raw in File.ReadAllLines(filePath))
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            var matches = Regex.Matches(line, @"[-+]?\d+(?:[\.,]\d+)?(?:[eE][-+]?\d+)?");
            if (matches.Count < 2)
                continue;

            if (TryParseFlexible(matches[0].Value, out var x) && TryParseFlexible(matches[1].Value, out var y))
                result.Add(new WavePoint(x, y));
        }

        return result.OrderBy(p => p.X).ToList();
    }

    private static bool TryParseFlexible(string input, out double value)
    {
        return double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               || double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}
