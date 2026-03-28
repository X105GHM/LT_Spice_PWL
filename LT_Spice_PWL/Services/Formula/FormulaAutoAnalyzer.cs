using PwlEditor.Models;

namespace PwlEditor.Services.Formula;

public static class FormulaAutoAnalyzer
{
    public sealed class AnalysisResult
    {
        public double EstimatedPeriod { get; init; }
        public double YMin { get; init; }
        public double YMax { get; init; }
        public List<WavePoint> OnePeriodPoints { get; init; } = new();
    }

    public static AnalysisResult Analyze(
        string expression,
        FormulaOutputMode outputMode,
        int samplesPerPeriod = 600,
        int searchSamples = 12000,
        double searchMaxTime = 2.0)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new InvalidOperationException("Die Formel ist leer.");

        samplesPerPeriod = Math.Max(samplesPerPeriod, 400);
        searchSamples = Math.Max(searchSamples, 4000);

        var raw = FormulaEngine.GeneratePoints(expression, searchMaxTime, searchSamples, outputMode)
            .Where(IsFinitePoint)
            .OrderBy(p => p.X)
            .ToList();

        if (raw.Count < 200)
            throw new InvalidOperationException("Zu wenige gültige Punkte für die Analyse.");

        double period = EstimatePeriodFromSignal(raw);

        if (!(period > 0.0) || double.IsNaN(period) || double.IsInfinity(period))
            throw new InvalidOperationException("Die Periodendauer konnte nicht bestimmt werden.");

        var onePeriod = FormulaEngine.GeneratePoints(expression, period, samplesPerPeriod, outputMode)
            .Where(IsFinitePoint)
            .OrderBy(p => p.X)
            .ToList();

        if (onePeriod.Count < 10)
            throw new InvalidOperationException("Die analysierte Periode erzeugt zu wenige gültige Punkte.");

        double yMin = onePeriod.Min(p => p.Y);
        double yMax = onePeriod.Max(p => p.Y);

        if (Math.Abs(yMax - yMin) < 1e-12)
        {
            yMin -= 1.0;
            yMax += 1.0;
        }
        else
        {
            double margin = (yMax - yMin) * 0.05;
            yMin -= margin;
            yMax += margin;
        }

        return new AnalysisResult
        {
            EstimatedPeriod = period,
            YMin = yMin,
            YMax = yMax,
            OnePeriodPoints = onePeriod
        };
    }

    private static bool IsFinitePoint(WavePoint p)
    {
        return !(double.IsNaN(p.X) || double.IsInfinity(p.X) ||
                 double.IsNaN(p.Y) || double.IsInfinity(p.Y));
    }

    private static double EstimatePeriodFromSignal(List<WavePoint> points)
    {
        // Erst lokale Maxima versuchen
        var maxima = FindLocalMaxima(points);

        if (maxima.Count >= 2)
        {
            double periodFromMaxima = MedianDelta(maxima);
            if (periodFromMaxima > 0)
                return periodFromMaxima;
        }

        // Fallback: steigende Nulldurchgänge
        var zeroUp = FindRisingZeroCrossings(points);

        if (zeroUp.Count >= 2)
        {
            double periodFromZeros = MedianDelta(zeroUp);
            if (periodFromZeros > 0)
                return periodFromZeros;
        }

        throw new InvalidOperationException("Keine stabile Periodenstruktur gefunden.");
    }

    private static List<double> FindLocalMaxima(List<WavePoint> points)
    {
        var result = new List<double>();
        if (points.Count < 3)
            return result;

        double yMin = points.Min(p => p.Y);
        double yMax = points.Max(p => p.Y);
        double amplitude = yMax - yMin;
        double threshold = yMin + amplitude * 0.7; // nur deutliche Peaks

        for (int i = 1; i < points.Count - 1; i++)
        {
            double y0 = points[i - 1].Y;
            double y1 = points[i].Y;
            double y2 = points[i + 1].Y;

            if (y1 >= y0 && y1 > y2 && y1 >= threshold)
            {
                result.Add(points[i].X);
            }
        }

        return FilterTooCloseEvents(result);
    }

    private static List<double> FindRisingZeroCrossings(List<WavePoint> points)
    {
        var result = new List<double>();
        if (points.Count < 2)
            return result;

        for (int i = 0; i < points.Count - 1; i++)
        {
            double x1 = points[i].X;
            double y1 = points[i].Y;
            double x2 = points[i + 1].X;
            double y2 = points[i + 1].Y;

            // steigender Nulldurchgang
            if (y1 <= 0.0 && y2 > 0.0)
            {
                double dy = y2 - y1;
                if (Math.Abs(dy) < 1e-20)
                    continue;

                double t = -y1 / dy;
                double xCross = x1 + t * (x2 - x1);
                result.Add(xCross);
            }
        }

        return FilterTooCloseEvents(result);
    }

    private static List<double> FilterTooCloseEvents(List<double> events)
    {
        var filtered = new List<double>();
        if (events.Count == 0)
            return filtered;

        filtered.Add(events[0]);

        for (int i = 1; i < events.Count; i++)
        {
            if (events[i] - filtered[^1] > 1e-9)
                filtered.Add(events[i]);
        }

        return filtered;
    }

    private static double MedianDelta(List<double> xs)
    {
        if (xs.Count < 2)
            return -1.0;

        var deltas = new List<double>();
        for (int i = 1; i < xs.Count; i++)
        {
            double d = xs[i] - xs[i - 1];
            if (d > 0)
                deltas.Add(d);
        }

        if (deltas.Count == 0)
            return -1.0;

        deltas.Sort();

        int mid = deltas.Count / 2;
        if (deltas.Count % 2 == 0)
            return 0.5 * (deltas[mid - 1] + deltas[mid]);
        else
            return deltas[mid];
    }
}