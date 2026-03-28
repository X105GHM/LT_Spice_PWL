using System.Globalization;
using System.Windows;
using PwlEditor.Models;

namespace PwlEditor.Dialogs;

public partial class PointEditDialog : Window
{
    private readonly double _minX;
    private readonly double _maxX;
    private readonly double _yMin;
    private readonly double _yMax;

    public WavePoint Result { get; private set; }

    public PointEditDialog(WavePoint original, double minX, double maxX, double yMin, double yMax)
    {
        InitializeComponent();

        _minX = minX;
        _maxX = maxX;
        _yMin = Math.Min(yMin, yMax);
        _yMax = Math.Max(yMin, yMax);

        Result = original.Clone();
        XTextBox.Text = original.X.ToString(CultureInfo.CurrentCulture);
        YTextBox.Text = original.Y.ToString(CultureInfo.CurrentCulture);
        RuleTextBlock.Text = $"Erlaubt: X zwischen {minX} und {maxX}, Y zwischen {_yMin} und {_yMax}.";
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParse(XTextBox.Text, out var x) || !TryParse(YTextBox.Text, out var y))
        {
            MessageBox.Show(this, "Bitte gültige Zahlen eingeben.", "Ungültige Eingabe", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (x < _minX || x > _maxX)
        {
            MessageBox.Show(this, $"X muss zwischen {_minX} und {_maxX} liegen.", "Ungültige X-Position", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (y < _yMin || y > _yMax)
        {
            MessageBox.Show(this, $"Y muss zwischen {_yMin} und {_yMax} liegen.", "Ungültiger Y-Wert", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Result = new WavePoint(x, y);
        DialogResult = true;
    }

    private static bool TryParse(string input, out double value)
    {
        return double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
               || double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}