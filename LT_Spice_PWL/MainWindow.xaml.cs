using Microsoft.Win32;
using PwlEditor.Controls;
using PwlEditor.Dialogs;
using PwlEditor.Models;
using PwlEditor.Services;
using PwlEditor.Services.Formula;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PwlEditor;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<WavePoint> _points = new();
    private const int VerticalDivisions = 10;

    private static readonly double[] SnapYSteps = Build125Sequence(0.001, 1000.0);
    private static readonly double[] TimeSteps = Build125Sequence(1e-9, 1000.0);
    private static readonly double[] YOffsetSteps = BuildSignedSequence(Build125Sequence(0.1, 1000.0));

    public MainWindow()
    {
        InitializeComponent();

        ThemeManager.ApplyTheme(ThemeManager.Light);

        OutputModeComboBox.ItemsSource = Enum.GetValues(typeof(FormulaOutputMode));
        OutputModeComboBox.SelectedItem = FormulaOutputMode.Real;

        PointsDataGrid.ItemsSource = _points;

        WaveEditor.PointEditRequested += WaveEditor_PointEditRequested;
        WaveEditor.PointsChanged += WaveEditor_PointsChanged;

        LoadDefaultPoints();
        ApplySettingsToEditor();
        RefreshLatexPreview();
        UpdateStatus("MVP geladen.");
    }

    private void WaveEditor_PointsChanged(object? sender, EventArgs e)
    {
        SyncPointsFromEditor();
        UpdateStatus($"{_points.Count} Punkt(e) in der Periode.");
    }

    private void WaveEditor_PointEditRequested(object? sender, PointEditRequestedEventArgs e)
    {
        var leftLimit = e.Index > 0 ? _points[e.Index - 1].X : 0.0;
        var rightLimit = e.Index < _points.Count - 1
            ? _points[e.Index + 1].X
            : GetDouble(PeriodDurationTextBox, 1.0) * GetTimeUnitFactor(PeriodUnitComboBox);

        var (yMin, yMax) = GetYAxisFromScopeSettings();

        var dialog = new PointEditDialog(e.Point, leftLimit, rightLimit, yMin, yMax)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            _points[e.Index].X = dialog.Result.X;
            _points[e.Index].Y = dialog.Result.Y;
            ReloadEditorFromPoints();
            UpdateStatus("Punkt aktualisiert.");
        }
    }

    private void ApplySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplySettingsToEditor();
            UpdateStatus("Einstellungen übernommen.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void LoadDefaultPoints_Click(object sender, RoutedEventArgs e)
    {
        LoadDefaultPoints();
        UpdateStatus("Standardpunkte geladen.");
    }

    private void ApplyFormula_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            double periodDuration = GetDouble(PeriodDurationTextBox, 1.0) * GetTimeUnitFactor(PeriodUnitComboBox);
            int samples = Math.Max(500, GetInt(SamplesPerPeriodTextBox, 200));
            var outputMode = (FormulaOutputMode)(OutputModeComboBox.SelectedItem ?? FormulaOutputMode.Real);
            string expression = FormulaTextBox.Text.Trim();

            var points = FormulaEngine.GeneratePoints(expression, periodDuration, samples, outputMode)
                .Where(p => !double.IsNaN(p.X) && !double.IsInfinity(p.X) &&
                            !double.IsNaN(p.Y) && !double.IsInfinity(p.Y))
                .OrderBy(p => p.X)
                .ToList();

            if (points.Count == 0)
                throw new InvalidOperationException("Die Formel hat keine gültigen Punkte erzeugt.");

            _points.Clear();
            foreach (var point in points)
                _points.Add(point.Clone());

            ReloadEditorFromPoints();
            RefreshLatexPreview();
            UpdateStatus($"Formel erfolgreich in Punkte umgewandelt ({samples} Samples/Periode).");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        PeriodDurationTextBox.Text = "1.0";
        TotalDurationTextBox.Text = "10.0";
        VoltsPerDivTextBox.Text = "1.0";
        YOffsetTextBox.Text = "0.0";
        SnapXTextBox.Text = "0.05";
        SnapYTextBox.Text = "0.05";
        SnapEnabledCheckBox.IsChecked = true;
        FormulaTextBox.Text = "sin(2*pi*t)";
        SamplesPerPeriodTextBox.Text = "200";
        OutputModeComboBox.SelectedItem = FormulaOutputMode.Real;

        LoadDefaultPoints();
        ApplySettingsToEditor();
        RefreshLatexPreview();
        UpdateStatus("Neues Projekt angelegt.");
    }

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "PWL Editor Projekt (*.pwlproj.json)|*.pwlproj.json|JSON-Datei (*.json)|*.json",
                DefaultExt = ".pwlproj.json"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var project = BuildProjectFromUi();
            ProjectFileService.Save(dialog.FileName, project);
            UpdateStatus("Projekt gespeichert.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void OpenProject_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PWL Editor Projekt (*.pwlproj.json;*.json)|*.pwlproj.json;*.json|Alle Dateien (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var project = ProjectFileService.Load(dialog.FileName);
            ApplyProjectToUi(project);
            UpdateStatus("Projekt geladen.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ImportPwl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PWL/Text (*.txt;*.pwl;*.csv)|*.txt;*.pwl;*.csv|Alle Dateien (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var imported = PwlFileService.Import(dialog.FileName);
            if (imported.Count == 0)
                throw new InvalidOperationException("Es wurden keine gültigen Punkte in der Datei gefunden.");

            var (yMin, yMax) = GetYAxisFromScopeSettings();

            _points.Clear();
            foreach (var point in imported.OrderBy(p => p.X))
            {
                _points.Add(new WavePoint(point.X, Math.Clamp(point.Y, yMin, yMax)));
            }

            double maxX = imported.Max(p => p.X);
            SetTimeWithBestUnit(maxX, PeriodDurationTextBox, PeriodUnitComboBox);
            SetTimeWithBestUnit(maxX, TotalDurationTextBox, TotalDurationUnitComboBox);

            ReloadEditorFromPoints();
            UpdateStatus("PWL-Datei importiert.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ExportPwl_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_points.Count == 0)
                throw new InvalidOperationException("Keine Punkte vorhanden.");

            var dialog = new SaveFileDialog
            {
                Filter = "LTspice PWL (*.txt)|*.txt|PWL (*.pwl)|*.pwl|CSV (*.csv)|*.csv",
                DefaultExt = ".txt"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            var period = GetDouble(PeriodDurationTextBox, 1.0) * GetTimeUnitFactor(PeriodUnitComboBox);
            var total = GetDouble(TotalDurationTextBox, period) * GetTimeUnitFactor(TotalDurationUnitComboBox);
            var snapX = GetDouble(SnapXTextBox, 0.05) * GetTimeUnitFactor(SnapXUnitComboBox);

            var repeated = PwlFileService.BuildRepeatedWave(_points.ToList(), period, total);

            repeated = RemoveRedundantDuplicatePoints(repeated);

            if (TryFindDuplicateTimes(repeated, out int duplicateCount, out double firstDuplicateX))
            {
                double suggestedDelay = SuggestMinimalDelay(repeated, period, snapX);

                var result = MessageBox.Show(
                    this,
                    $"Es wurden {duplicateCount} doppelte Zeitstempel erkannt.\n\n" +
                    $"Erstes Vorkommen bei t = {firstDuplicateX.ToString("G6", CultureInfo.CurrentCulture)} s\n\n" +
                    $"LTspice kann damit Probleme haben.\n" +
                    $"Das entspricht meist einer ideal unendlich steilen Flanke.\n\n" +
                    $"Vorgeschlagener Minimal-Delay: {suggestedDelay.ToString("G6", CultureInfo.CurrentCulture)} s\n\n" +
                    $"Soll dieser Delay automatisch eingefügt werden?",
                    "Doppelte Zeitstempel erkannt",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    UpdateStatus("Export abgebrochen: doppelte Zeitstempel erkannt.");
                    return;
                }

                repeated = ApplyMinimalDelayToDuplicateTimes(repeated, suggestedDelay);

                if (TryFindDuplicateTimes(repeated, out _, out _))
                    throw new InvalidOperationException("Doppelte Zeitstempel konnten nicht automatisch aufgelöst werden.");
            }

            PwlFileService.Export(dialog.FileName, repeated);
            UpdateStatus($"PWL exportiert ({repeated.Count} Punkte).");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void ExportLatex_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Filter = "LaTeX/Text (*.tex)|*.tex|Text (*.txt)|*.txt",
                DefaultExt = ".tex"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            RefreshLatexPreview();
            File.WriteAllText(dialog.FileName, LatexPreviewTextBox.Text);
            UpdateStatus("LaTeX-Vorschau exportiert.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void LoadDefaultPoints()
    {
        double period = GetDouble(PeriodDurationTextBox, 1.0) * GetTimeUnitFactor(PeriodUnitComboBox);

        _points.Clear();
        _points.Add(new WavePoint(0.0, 0.0));
        _points.Add(new WavePoint(period * 0.25, 1.0));
        _points.Add(new WavePoint(period * 0.5, 0.0));
        _points.Add(new WavePoint(period * 0.75, -1.0));
        _points.Add(new WavePoint(period, 0.0));

        ReloadEditorFromPoints();
    }

    private void ApplySettingsToEditor()
    {
        var period = GetDouble(PeriodDurationTextBox, 1.0) * GetTimeUnitFactor(PeriodUnitComboBox);
        var (yMin, yMax) = GetYAxisFromScopeSettings();

        WaveEditor.PeriodDuration = period;
        WaveEditor.YMin = yMin;
        WaveEditor.YMax = yMax;
        WaveEditor.HorizontalDivisions = 10;
        WaveEditor.VerticalDivisions = VerticalDivisions;
        WaveEditor.SnapX = GetDouble(SnapXTextBox, 0.05) * GetTimeUnitFactor(SnapXUnitComboBox);
        WaveEditor.SnapY = GetDouble(SnapYTextBox, 0.05);
        WaveEditor.SnapEnabled = SnapEnabledCheckBox.IsChecked == true;
        WaveEditor.InvalidateVisual();
    }

    private WaveProject BuildProjectFromUi()
    {
        var (yMin, yMax) = GetYAxisFromScopeSettings();

        return new WaveProject
        {
            PeriodDuration = GetDouble(PeriodDurationTextBox, 1.0),
            TotalDuration = GetDouble(TotalDurationTextBox, 10.0),
            YMin = yMin,
            YMax = yMax,
            VoltsPerDiv = GetDouble(VoltsPerDivTextBox, 1.0),
            YOffset = GetDouble(YOffsetTextBox, 0.0),
            SnapX = GetDouble(SnapXTextBox, 0.05),
            SnapY = GetDouble(SnapYTextBox, 0.05),
            SnapEnabled = SnapEnabledCheckBox.IsChecked == true,
            Points = _points.Select(p => p.Clone()).ToList(),
            Formula = new FormulaSettings
            {
                Expression = FormulaTextBox.Text,
                OutputMode = (FormulaOutputMode)(OutputModeComboBox.SelectedItem ?? FormulaOutputMode.Real),
                SamplesPerPeriod = GetInt(SamplesPerPeriodTextBox, 200),
                LatexPreview = LatexPreviewTextBox.Text
            }
        };
    }

    private void ApplyProjectToUi(WaveProject project)
    {
        PeriodDurationTextBox.Text = project.PeriodDuration.ToString(CultureInfo.CurrentCulture);
        TotalDurationTextBox.Text = project.TotalDuration.ToString(CultureInfo.CurrentCulture);

        if (project.VoltsPerDiv > 0)
            VoltsPerDivTextBox.Text = project.VoltsPerDiv.ToString(CultureInfo.CurrentCulture);
        else
            VoltsPerDivTextBox.Text = "1.0";

        YOffsetTextBox.Text = project.YOffset.ToString(CultureInfo.CurrentCulture);
        SnapXTextBox.Text = project.SnapX.ToString(CultureInfo.CurrentCulture);
        SnapYTextBox.Text = project.SnapY.ToString(CultureInfo.CurrentCulture);
        SnapEnabledCheckBox.IsChecked = project.SnapEnabled;

        FormulaTextBox.Text = project.Formula.Expression;
        SamplesPerPeriodTextBox.Text = project.Formula.SamplesPerPeriod.ToString(CultureInfo.CurrentCulture);
        OutputModeComboBox.SelectedItem = project.Formula.OutputMode;
        LatexPreviewTextBox.Text = project.Formula.LatexPreview;

        _points.Clear();
        foreach (var point in project.Points.OrderBy(p => p.X))
            _points.Add(point.Clone());

        ReloadEditorFromPoints();
        ApplySettingsToEditor();
        RefreshLatexPreview();
    }

    private void ReloadEditorFromPoints()
    {
        ApplySettingsToEditor();
        WaveEditor.SetPoints(_points);
        PointsDataGrid.Items.Refresh();
    }

    private void SyncPointsFromEditor()
    {
        _points.Clear();
        foreach (var point in WaveEditor.Points.OrderBy(p => p.X))
            _points.Add(point.Clone());

        PointsDataGrid.Items.Refresh();
    }

    private void RefreshLatexPreview()
    {
        LatexPreviewTextBox.Text = FormulaEngine.ToLatexPreview(FormulaTextBox.Text);
    }

    private (double yMin, double yMax) GetYAxisFromScopeSettings()
    {
        double vDiv = GetDouble(VoltsPerDivTextBox, 1.0);
        double offset = GetDouble(YOffsetTextBox, 0.0);

        if (vDiv <= 0)
            throw new InvalidOperationException("V/div muss größer als 0 sein.");

        double halfRange = (VerticalDivisions / 2.0) * vDiv;
        double yMin = offset - halfRange;
        double yMax = offset + halfRange;

        return (yMin, yMax);
    }

    private static double GetDouble(TextBox textBox, double fallback)
    {
        if (TryParseDouble(textBox.Text, out var value))
            return value;

        return fallback;
    }

    private static int GetInt(TextBox textBox, int fallback)
    {
        if (int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value)
            || int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        return fallback;
    }

    private static bool TryParseDouble(string input, out double value)
    {
        return double.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
               || double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void ShowError(string message)
    {
        UpdateStatus("Fehler: " + message);
        MessageBox.Show(this, message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void UpdateStatus(string text)
    {
        StatusTextBlock.Text = text;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem item)
        {
            string theme = item.Content?.ToString() ?? ThemeManager.Dark;
            ThemeManager.ApplyTheme(theme);
            InvalidateEditor();
        }
    }

    private void InvalidateEditor()
    {
        WaveEditor?.InvalidateVisual();
    }

    private double GetTimeUnitFactor(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
        {
            string unit = item.Content?.ToString() ?? "s";
            return unit switch
            {
                "s" => 1.0,
                "ms" => 1e-3,
                "us" => 1e-6,
                "ns" => 1e-9,
                _ => 1.0
            };
        }

        return 1.0;
    }

    private void AutoFromPoints_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_points.Count == 0)
                throw new InvalidOperationException("Keine Punkte vorhanden.");

            double maxX = _points.Max(p => p.X);
            double minY = _points.Min(p => p.Y);
            double maxY = _points.Max(p => p.Y);

            if (maxX <= 0)
                maxX = 1e-6;

            double center = (minY + maxY) / 2.0;
            double span = maxY - minY;

            if (span < 1e-12)
                span = 2.0;

            double vDiv = span / VerticalDivisions;
            if (vDiv <= 0)
                vDiv = 1.0;

            SetTimeWithBestUnit(maxX, PeriodDurationTextBox, PeriodUnitComboBox);
            SetTimeWithBestUnit(maxX, TotalDurationTextBox, TotalDurationUnitComboBox);

            VoltsPerDivTextBox.Text = vDiv.ToString("G6", CultureInfo.CurrentCulture);
            YOffsetTextBox.Text = center.ToString("G6", CultureInfo.CurrentCulture);

            double snap = maxX / 100.0;
            SetTimeWithBestUnit(snap, SnapXTextBox, SnapXUnitComboBox);

            ApplySettingsToEditor();
            ReloadEditorFromPoints();

            UpdateStatus("Signal-/Achsenwerte automatisch aus Punkten gesetzt.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void AutoFromFormula_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string expression = FormulaTextBox.Text.Trim();
            var outputMode = (FormulaOutputMode)(OutputModeComboBox.SelectedItem ?? FormulaOutputMode.Real);
            int samplesPerPeriod = Math.Max(600, GetInt(SamplesPerPeriodTextBox, 200));

            double[] searchWindows =
            {
                0.05,
                0.1,
                0.2,
                0.5,
                1.0,
                2.0,
                5.0,
                10.0
            };

            FormulaAutoAnalyzer.AnalysisResult? best = null;

            foreach (double window in searchWindows)
            {
                try
                {
                    var result = FormulaAutoAnalyzer.Analyze(
                        expression,
                        outputMode,
                        samplesPerPeriod: samplesPerPeriod,
                        searchSamples: 12000,
                        searchMaxTime: window);

                    if (result.EstimatedPeriod > 0 && result.EstimatedPeriod < window * 0.9)
                    {
                        best = result;
                        break;
                    }
                }
                catch
                {
                }
            }

            if (best is null)
                throw new InvalidOperationException("Die Formel konnte nicht automatisch analysiert werden.");

            double oldPeriod = GetDouble(PeriodDurationTextBox, 1.0) * GetTimeUnitFactor(PeriodUnitComboBox);

            SetTimeWithBestUnit(best.EstimatedPeriod, PeriodDurationTextBox, PeriodUnitComboBox);
            SetTimeWithBestUnit(best.EstimatedPeriod, TotalDurationTextBox, TotalDurationUnitComboBox);

            double center = (best.YMin + best.YMax) / 2.0;
            double span = best.YMax - best.YMin;
            double vDiv = span / VerticalDivisions;

            if (vDiv <= 0)
                vDiv = 1.0;

            VoltsPerDivTextBox.Text = vDiv.ToString("G6", CultureInfo.CurrentCulture);
            YOffsetTextBox.Text = center.ToString("G6", CultureInfo.CurrentCulture);

            double snap = best.EstimatedPeriod / 100.0;
            SetTimeWithBestUnit(snap, SnapXTextBox, SnapXUnitComboBox);

            ApplySettingsToEditor();

            _points.Clear();
            foreach (var point in best.OnePeriodPoints)
                _points.Add(point.Clone());

            ReloadEditorFromPoints();
            RefreshLatexPreview();

            if (oldPeriod > 0)
            {
                double relErr = Math.Abs(oldPeriod - best.EstimatedPeriod) / best.EstimatedPeriod;
                if (relErr > 0.2)
                {
                    MessageBox.Show(
                        this,
                        $"Warnung: Die bisher eingestellte Periodendauer passte nicht zur Formel.\n\n" +
                        $"Erkannt: {best.EstimatedPeriod.ToString("G6", CultureInfo.CurrentCulture)} s",
                        "Warnung",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            UpdateStatus("Periodendauer und Y-Achse aus Formel bestimmt.");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void SetTimeWithBestUnit(double timeSeconds, TextBox textBox, ComboBox comboBox)
    {
        double abs = Math.Abs(timeSeconds);

        string unit;
        double factor;

        if (abs >= 1.0)
        {
            unit = "s";
            factor = 1.0;
        }
        else if (abs >= 1e-3)
        {
            unit = "ms";
            factor = 1e-3;
        }
        else if (abs >= 1e-6)
        {
            unit = "us";
            factor = 1e-6;
        }
        else
        {
            unit = "ns";
            factor = 1e-9;
        }

        double displayValue = timeSeconds / factor;
        textBox.Text = Math.Round(displayValue, 6).ToString(CultureInfo.CurrentCulture);

        foreach (ComboBoxItem item in comboBox.Items)
        {
            if ((item.Content?.ToString() ?? "") == unit)
            {
                comboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void VoltsPerDivTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        AdjustWithMouseWheel(() =>
        {
            double current = GetDouble(VoltsPerDivTextBox, 1.0);
            double next = GetNextVoltsPerDivValue(current, e.Delta > 0);
            VoltsPerDivTextBox.Text = next.ToString("G", CultureInfo.CurrentCulture);
        }, e);
    }

    private void YOffsetTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        AdjustWithMouseWheel(() =>
        {
            double current = GetDouble(YOffsetTextBox, 0.0);
            double next = GetNextFromSequence(current, e.Delta > 0, YOffsetSteps);
            YOffsetTextBox.Text = next.ToString("G", CultureInfo.CurrentCulture);
        }, e);
    }

    private void SnapYTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        AdjustWithMouseWheel(() =>
        {
            double current = GetDouble(SnapYTextBox, 0.05);
            double next = GetNextFromSequence(current, e.Delta > 0, SnapYSteps);
            SnapYTextBox.Text = next.ToString("G", CultureInfo.CurrentCulture);
        }, e);
    }

    private void PeriodDurationTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        AdjustWithMouseWheel(() =>
        {
            double currentSeconds = GetDouble(PeriodDurationTextBox, 1.0) * GetTimeUnitFactor(PeriodUnitComboBox);
            double nextSeconds = GetNextTimeScrollValue(currentSeconds, e.Delta > 0);
            SetTimeWithBestUnit(nextSeconds, PeriodDurationTextBox, PeriodUnitComboBox);
        }, e);
    }

    private void SnapXTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        AdjustWithMouseWheel(() =>
        {
            double currentSeconds = GetDouble(SnapXTextBox, 0.05) * GetTimeUnitFactor(SnapXUnitComboBox);
            double nextSeconds = GetNextTimeScrollValue(currentSeconds, e.Delta > 0);
            SetTimeWithBestUnit(nextSeconds, SnapXTextBox, SnapXUnitComboBox);
        }, e);
    }

    private void AdjustWithMouseWheel(Action adjustAction, MouseWheelEventArgs e)
    {
        try
        {
            adjustAction();
            ReloadEditorFromPoints();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
            e.Handled = true;
        }
    }

    private static double GetNextVoltsPerDivValue(double current, bool increase)
    {
        current = Math.Max(0.1, current);

        double[] fixedSteps =
        {
            0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0,
            2.0, 3.0, 4.0, 5.0,
            10.0, 20.0, 30.0, 40.0, 50.0
        };

        const double eps = 1e-12;

        if (increase)
        {
            foreach (double step in fixedSteps)
            {
                if (step > current + eps)
                    return step;
            }

            return current <= 50.0 + eps
                ? 100.0
                : (Math.Floor(current / 50.0) + 1.0) * 50.0;
        }
        else
        {
            if (current > 50.0 + eps)
            {
                double prev = Math.Ceiling(current / 50.0 - eps) * 50.0 - 50.0;
                return Math.Max(50.0, prev);
            }

            for (int i = fixedSteps.Length - 1; i >= 0; i--)
            {
                if (fixedSteps[i] < current - eps)
                    return fixedSteps[i];
            }

            return 0.1;
        }
    }

    private static double GetNextFromSequence(double current, bool increase, IReadOnlyList<double> sequence)
    {
        if (sequence.Count == 0)
            return current;

        const double eps = 1e-12;

        if (increase)
        {
            foreach (double step in sequence)
            {
                if (step > current + eps)
                    return step;
            }

            return sequence[^1];
        }
        else
        {
            for (int i = sequence.Count - 1; i >= 0; i--)
            {
                if (sequence[i] < current - eps)
                    return sequence[i];
            }

            return sequence[0];
        }
    }

    private static double[] Build125Sequence(double minValue, double maxValue)
    {
        if (minValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(minValue), "minValue muss > 0 sein.");

        if (maxValue < minValue)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue muss >= minValue sein.");

        var values = new List<double>();
        double[] bases = { 1.0, 2.0, 5.0 };

        int minExponent = (int)Math.Floor(Math.Log10(minValue)) - 1;
        int maxExponent = (int)Math.Ceiling(Math.Log10(maxValue)) + 1;

        for (int exp = minExponent; exp <= maxExponent; exp++)
        {
            double factor = Math.Pow(10, exp);

            foreach (double b in bases)
            {
                double value = b * factor;
                if (value >= minValue && value <= maxValue)
                    values.Add(value);
            }
        }

        return values
            .Distinct()
            .OrderBy(v => v)
            .ToArray();
    }

    private static double[] BuildSignedSequence(IEnumerable<double> positiveSteps)
    {
        var positives = positiveSteps
            .Where(v => v > 0)
            .Distinct()
            .OrderBy(v => v)
            .ToArray();

        var negatives = positives
            .Reverse()
            .Select(v => -v);

        return negatives
            .Concat(new[] { 0.0 })
            .Concat(positives)
            .ToArray();
    }

    private static double GetNextTimeScrollValue(double current, bool increase)
    {
        current = Math.Max(1e-12, current);

        double exponent = Math.Floor(Math.Log10(current));
        double baseValue = Math.Pow(10.0, exponent);
        double normalized = current / baseValue;

        int step = (int)Math.Round(normalized, MidpointRounding.AwayFromZero);
        step = Math.Clamp(step, 1, 9);

        const double eps = 1e-12;

        if (increase)
        {
            if (current > step * baseValue + eps)
                step++;

            if (step < 9)
            {
                step++;
                return step * baseValue;
            }

            return 1.0 * baseValue * 10.0;
        }
        else
        {
            if (current < step * baseValue - eps)
                step--;

            if (step > 1)
            {
                step--;
                return step * baseValue;
            }

            return 9.0 * baseValue / 10.0;
        }
    }

    private void FormulaTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Insert)
        {
            e.Handled = true;
        }
    }

    private static List<WavePoint> RemoveRedundantDuplicatePoints(IEnumerable<WavePoint> points)
    {
        var ordered = points.OrderBy(p => p.X).ToList();
        var result = new List<WavePoint>();

        foreach (var point in ordered)
        {
            if (result.Count == 0)
            {
                result.Add(point.Clone());
                continue;
            }

            var prev = result[^1];

            if (TimesEqual(prev.X, point.X) && ValuesEqual(prev.Y, point.Y))
                continue;

            result.Add(point.Clone());
        }

        return result;
    }

    private static bool TryFindDuplicateTimes(IReadOnlyList<WavePoint> points, out int duplicateCount, out double firstDuplicateX)
    {
        duplicateCount = 0;
        firstDuplicateX = 0.0;

        for (int i = 1; i < points.Count; i++)
        {
            if (TimesEqual(points[i - 1].X, points[i].X))
            {
                duplicateCount++;

                if (duplicateCount == 1)
                    firstDuplicateX = points[i].X;
            }
        }

        return duplicateCount > 0;
    }

    private static List<WavePoint> ApplyMinimalDelayToDuplicateTimes(IReadOnlyList<WavePoint> points, double baseDelay)
    {
        var result = points
            .Select(p => p.Clone())
            .OrderBy(p => p.X)
            .ToList();

        int i = 0;

        while (i < result.Count)
        {
            int groupStart = i;
            double baseX = result[groupStart].X;
            int groupEnd = groupStart;

            while (groupEnd + 1 < result.Count && TimesEqual(result[groupEnd + 1].X, baseX))
                groupEnd++;

            int groupSize = groupEnd - groupStart + 1;

            if (groupSize > 1)
            {
                double localDelay = baseDelay;

                if (groupEnd + 1 < result.Count)
                {
                    double nextDistinctX = result[groupEnd + 1].X;
                    double availableWindow = nextDistinctX - baseX;

                    if (availableWindow <= 0)
                        throw new InvalidOperationException("Ungültige Punktreihenfolge beim Anwenden des Delays.");

                    localDelay = Math.Min(localDelay, availableWindow / groupSize);
                }

                if (localDelay <= 0)
                    throw new InvalidOperationException("Kein gültiger Minimal-Delay bestimmbar.");

                for (int j = 1; j < groupSize; j++)
                    result[groupStart + j].X = baseX + localDelay * j;
            }

            i = groupEnd + 1;
        }

        return result;
    }

    private static double SuggestMinimalDelay(IReadOnlyList<WavePoint> points, double period, double snapX)
    {
        var candidates = new List<double>();

        if (snapX > 0)
            candidates.Add(snapX / 10.0);

        double minPositiveDx = double.PositiveInfinity;

        for (int i = 1; i < points.Count; i++)
        {
            double dx = points[i].X - points[i - 1].X;
            if (dx > 0 && dx < minPositiveDx)
                minPositiveDx = dx;
        }

        if (!double.IsInfinity(minPositiveDx))
            candidates.Add(minPositiveDx / 10.0);

        if (period > 0)
            candidates.Add(period / 1_000_000.0);

        var delay = candidates
            .Where(v => v > 0)
            .DefaultIfEmpty(1e-12)
            .Min();

        return delay;
    }

    private static bool TimesEqual(double a, double b)
    {
        double scale = Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
        return Math.Abs(a - b) <= scale * 1e-12;
    }

    private static bool ValuesEqual(double a, double b)
    {
        double scale = Math.Max(1.0, Math.Max(Math.Abs(a), Math.Abs(b)));
        return Math.Abs(a - b) <= scale * 1e-12;
    }
}