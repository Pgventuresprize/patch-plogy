using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Patch;

/// <summary>
/// Brand kit 02 de Plogy para la parte nativa de Windows (la interfaz web lo toma de ui/estilo.css).
/// Tinta, naranja, esfera coral→violeta y Plogy Display.
/// </summary>
public static class Marca
{
    public static readonly Color Tinta = Color.FromRgb(0x17, 0x13, 0x10);
    public static readonly Color Superficie = Color.FromRgb(0xF4, 0xF1, 0xEF);
    public static readonly Color Naranja = Color.FromRgb(0xFF, 0x5B, 0x00);
    public static readonly Color Coral = Color.FromRgb(0xF7, 0x59, 0x46);
    public static readonly Color Violeta = Color.FromRgb(0x9C, 0x5A, 0xA7);
    public static readonly Color Borde = Color.FromRgb(0xA3, 0x94, 0x86);
    /// <summary>Panel oscuro cálido (derivado de la tinta) para botones secundarios sobre tinta.</summary>
    public static readonly Color Panel = Color.FromRgb(0x2E, 0x26, 0x20);

    /// <summary>Degradado vertical del símbolo: 0 % #F75946 · 33 % #EE5358 · 50 % #E1556A · 67 % #C85981 · 100 % #9C5AA7.</summary>
    public static Brush Esfera()
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        g.GradientStops.Add(new GradientStop(Coral, 0));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xEE, 0x53, 0x58), 0.33));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xE1, 0x55, 0x6A), 0.5));
        g.GradientStops.Add(new GradientStop(Color.FromRgb(0xC8, 0x59, 0x81), 0.67));
        g.GradientStops.Add(new GradientStop(Violeta, 1));
        g.Freeze();
        return g;
    }

    /// <summary>Plogy Display (incrustada en el .exe). Respaldo: Arial, como pide el kit.</summary>
    public static readonly FontFamily Display = new(new Uri("pack://application:,,,/"), "./Marca/#Plogy Display, Arial");
    /// <summary>Texto de apoyo: Helvetica Neue → Helvetica → Arial.</summary>
    public static readonly FontFamily Cuerpo = new("Helvetica Neue, Helvetica, Arial");

    /// <summary>La esfera de Plogy como ícono de Windows (bandeja y ventanas).</summary>
    public static System.Drawing.Icon Icono(int lado = 0)
    {
        using var s = typeof(Marca).Assembly.GetManifestResourceStream("Patch.plogy.ico")
            ?? throw new FileNotFoundException("Falta el ícono incrustado plogy.ico");
        return lado > 0 ? new System.Drawing.Icon(s, lado, lado) : new System.Drawing.Icon(s);
    }

    public static System.Windows.Media.ImageSource IconoVentana()
    {
        using var s = typeof(Marca).Assembly.GetManifestResourceStream("Patch.plogy.ico")!;
        var d = new System.Windows.Media.Imaging.IconBitmapDecoder(s, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
        return d.Frames[0];
    }
}
