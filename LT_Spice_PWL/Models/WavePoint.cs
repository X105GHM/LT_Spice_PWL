namespace PwlEditor.Models;

public class WavePoint
{
    public double X { get; set; }
    public double Y { get; set; }

    public WavePoint()
    {
    }

    public WavePoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    public WavePoint Clone() => new(X, Y);
}
