using System.Collections.Generic;

namespace PwlEditor.Models;

public class WaveProject
{
    public double PeriodDuration { get; set; } = 1.0;
    public double TotalDuration { get; set; } = 10.0;
    public double YMin { get; set; } = -1.0;
    public double YMax { get; set; } = 1.0;
    public double VoltsPerDiv { get; set; } = 1.0;
    public double YOffset { get; set; } = 0.0;
    public double SnapX { get; set; } = 0.05;
    public double SnapY { get; set; } = 0.05;
    public bool SnapEnabled { get; set; } = true;
    public List<WavePoint> Points { get; set; } = new();
    public FormulaSettings Formula { get; set; } = new();
}
