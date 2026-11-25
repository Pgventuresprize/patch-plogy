using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Patch;

public enum Fase { Reposo, Escuchando, Pensando, Hablando, Seguimiento, Permiso, Aviso }

/// <summary>
/// La ventanita sobre la barra de tareas. No se activa nunca (no le quita el foco a lo que estés haciendo).
/// Tocarla abre la barra lateral.
/// </summary>
public sealed class Pildora : Window
{
    public static readonly Color Naranja = Color.FromRgb(0xFF, 0x5B, 0x00);
    static readonly Brush Fondo = new SolidColorBrush(Color.FromArgb(0xF2, 0x17, 0x17, 0x19));

    readonly TextBlock titulo = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly TextBlock texto = new() { FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xDC)), TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.WordEllipsis, MaxHeight = 52 };
    readonly Ellipse punto = new() { Width = 14, Height = 14, Fill = new SolidColorBrush(Naranja) };
    readonly Ellipse halo = new() { Width = 30, Height = 30, Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x5B, 0x00)) };
    readonly StackPanel botones = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly DispatcherTimer ocultar = new();
    readonly DispatcherTimer latido = new() { Interval = TimeSpan.FromMilliseconds(45) };
    Fase fase = Fase.Reposo;
    double nivel, fasePulso;

    public event Action? Tocada;
    public event Action<bool>? Boton;

    public Pildora()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = 340;
        Opacity = 0;
        Title = "PATCH";

        var indicador = new Grid { Width = 34, Height = 34, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        indicador.Children.Add(halo);
        indicador.Children.Add(punto);
        halo.HorizontalAlignment = punto.HorizontalAlignment = HorizontalAlignment.Center;
        halo.VerticalAlignment = punto.VerticalAlignment = VerticalAlignment.Center;
        halo.RenderTransformOrigin = new Point(0.5, 0.5);
        halo.RenderTransform = new ScaleTransform(1, 1);

        botones.Children.Add(CrearBoton("Sí", true));
        botones.Children.Add(CrearBoton("No", false));

        var textos = new StackPanel();
        textos.Children.Add(titulo);
        textos.Children.Add(texto);
        textos.Children.Add(botones);

        var fila = new DockPanel();
        DockPanel.SetDock(indicador, Dock.Left);
        fila.Children.Add(indicador);
        fila.Children.Add(textos);

        Content = new Border
        {
            Background = Fondo,
            CornerRadius = new CornerRadius(16),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 12, 16, 12),
            Margin = new Thickness(10),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45 },
            Child = fila,
            Cursor = Cursors.Hand,
            ToolTip = "Toca para abrir la barra lateral",
        };

        MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not Button) Tocada?.Invoke(); };
        SourceInitialized += (_, _) => Nativo.SinFoco(this);
        SizeChanged += (_, _) => Ubicar();
        ocultar.Tick += (_, _) => { ocultar.Stop(); Esconder(); };
        latido.Tick += (_, _) => Animar();
    }

    Button CrearBoton(string t, bool si)
    {
        var b = new Button
        {
            Content = t, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 3, 14, 3),
            Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
            Background = si ? new SolidColorBrush(Naranja) : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3F)),
            Focusable = false,
        };
        b.Click += (_, _) => Boton?.Invoke(si);
        return b;
    }

    void Ubicar()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 6;
        Top = area.Bottom - ActualHeight - 6;
    }

    public void Mostrar(Fase f, string t, string cuerpo = "", bool siNo = false)
    {
        fase = f;
        titulo.Text = t;
        texto.Text = cuerpo;
        texto.Visibility = string.IsNullOrWhiteSpace(cuerpo) ? Visibility.Collapsed : Visibility.Visible;
        botones.Visibility = siNo ? Visibility.Visible : Visibility.Collapsed;
        punto.Fill = new SolidColorBrush(f is Fase.Aviso ? Color.FromRgb(0xE8, 0xB3, 0x3A) : Naranja);
        ocultar.Stop();
        if (!IsVisible) { Show(); Ubicar(); }
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
        latido.Start();
    }

    public void PonerTexto(string cuerpo)
    {
        texto.Text = cuerpo;
        texto.Visibility = string.IsNullOrWhiteSpace(cuerpo) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void PonerTitulo(string t) => titulo.Text = t;

    public void OcultarEn(double segundos)
    {
        ocultar.Stop();
        ocultar.Interval = TimeSpan.FromSeconds(Math.Max(0.05, segundos));
        ocultar.Start();
    }

    public void Esconder()
    {
        if (!IsVisible) return;
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        a.Completed += (_, _) => { if (Opacity == 0) { Hide(); latido.Stop(); fase = Fase.Reposo; } };
        BeginAnimation(OpacityProperty, a);
    }

    public void Nivel(float rms) => nivel = Math.Min(1, rms * 9);

    void Animar()
    {
        fasePulso += 0.12;
        double escala = fase switch
        {
            Fase.Escuchando or Fase.Seguimiento => 0.75 + nivel * 0.9,
            Fase.Pensando => 0.85 + 0.25 * Math.Sin(fasePulso * 1.6),
            Fase.Hablando => 0.9 + 0.2 * Math.Abs(Math.Sin(fasePulso * 2.3)),
            _ => 0.8,
        };
        nivel *= 0.85;
        var s = (ScaleTransform)halo.RenderTransform;
        s.ScaleX = s.ScaleY = escala;
    }
}
