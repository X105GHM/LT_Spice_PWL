# LTspice PWL Editor (WPF / C#)

Dieses Projekt ist ein lauffähiges MVP für einen grafischen PWL-Editor für LTspice.

## Enthalten

- WPF-GUI mit Zeichenfläche
- Punkte per Maus setzen und ziehen
- Rechtsklick auf einen Punkt für exakte XY-Eingabe
- X-Position darf nicht an linken/rechten Nachbarn vorbeigezogen werden
- Snapping für X und Y
- Formelmodus mit `t`, `pi`, `e`, `i`
- trigonometrische und komplexe Funktionen
- Umwandlung der Formel in Stützpunkte
- JSON-Projekt speichern/laden
- PWL-Datei importieren/exportieren
- LaTeX-Preview als Text exportieren

## Unterstützte Funktionen im Formelmodus

- `sin`, `cos`, `tan`
- `asin`, `acos`, `atan`
- `sinh`, `cosh`, `tanh`
- `exp`, `log`, `log10`, `sqrt`
- `abs`, `mag`, `phase`
- `real`, `imag`, `re`, `im`, `conj`
- `pow(a,b)`, `min(a,b)`, `max(a,b)`, `cis(x)`
- Operatoren: `+`, `-`, `*`, `/`, `^`

## Beispiel-Formeln

```txt
sin(2*pi*t)
0.7*sin(2*pi*t) + 0.2*sin(2*pi*3*t)
real(exp(i*2*pi*t))
mag(1 + 0.5*exp(i*2*pi*t))
phase(exp(i*2*pi*t))
```

## Build

In Visual Studio:

1. Projekt öffnen
2. NuGet ist nicht nötig
3. Starten

Oder per CLI:

```bash
dotnet build
```

## Single-File EXE

Für eine Ein-Datei-EXE kannst du z. B. veröffentlichen mit:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Hinweise

- Das Projekt verwendet `net8.0-windows`, damit es breit kompatibel ist.
- Wenn du auf `net10.0-windows` umstellen willst, musst du nur die `.csproj` anpassen.
- Die LaTeX-Vorschau ist absichtlich einfach gehalten. Für echtes mathematisches Rendering würde ich später eine passende Math-Rendering-Library ergänzen.
- Importierte PWL-Dateien werden zunächst als eine Punktliste übernommen; eine automatische Periodenerkennung ist im MVP noch nicht enthalten.
