namespace PwlEditor.Models;

public class FormulaSettings
{
    public string Expression { get; set; } = "sin(2*pi*t)";
    public FormulaOutputMode OutputMode { get; set; } = FormulaOutputMode.Real;
    public int SamplesPerPeriod { get; set; } = 200;
    public string LatexPreview { get; set; } = string.Empty;
}
