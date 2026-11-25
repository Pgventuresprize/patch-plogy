using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Patch;

/// <summary>
/// Barra lateral derecha: Windows le reserva el borde (las demás ventanas se reacomodan).
/// Por dentro muestra la interfaz compartida con Mac (carpeta ui/) en el motor de Edge.
/// </summary>
public sealed class BarraLateral : Window
{
    public const int Ancho = 380;
    readonly WebView2 web = new();
    Nativo.BarraReservada? reserva;
    bool lista;
    readonly System.Collections.Generic.List<string> enCola = new();

    public event Action<JsonElement>? Mensaje;
    public bool Abierta => IsVisible;

    static BarraLateral? actual;
    /// <summary>Al salir (aunque sea por un error), se devuelve el borde reservado de la pantalla.</summary>
    public static void LiberarTodas() { try { actual?.reserva?.Liberar(); } catch { } }

    public BarraLateral()
    {
        Title = "PATCH";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = Ancho;
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x14));
        Content = web;
        actual = this;
        SourceInitialized += (_, _) => reserva = new Nativo.BarraReservada(this, Ancho);
        Closing += (_, e) => { e.Cancel = true; Cerrar(); };
        _ = Iniciar();
    }

    async Task Iniciar()
    {
        try
        {
            var datos = Path.Combine(Config.Datos, "webview");
            var entorno = await CoreWebView2Environment.CreateAsync(null, datos);
            await web.EnsureCoreWebView2Async(entorno);
            var ui = Path.Combine(Config.Carpeta, "ui");
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("patch.ui", ui, CoreWebView2HostResourceAccessKind.DenyCors);
            web.CoreWebView2.Settings.AreDevToolsEnabled = true;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    var o = JsonDocument.Parse(e.WebMessageAsJson).RootElement;
                    if (o.TryGetProperty("tipo", out var t) && t.GetString() == "listo")
                    {
                        lista = true;
                        foreach (var m in enCola) web.CoreWebView2.PostWebMessageAsJson(m);
                        enCola.Clear();
                    }
                    Mensaje?.Invoke(o.Clone());
                }
                catch (Exception ex) { Registro.Log("Mensaje de la barra inválido: " + ex.Message); }
            };
            // Los enlaces se abren en el navegador del usuario, no dentro de la barra.
            web.CoreWebView2.NewWindowRequested += (_, e) => { e.Handled = true; Abrir(e.Uri); };
            web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://patch.ui/")) { e.Cancel = true; Abrir(e.Uri); }
            };
            web.CoreWebView2.Navigate("https://patch.ui/index.html");
        }
        catch (Exception ex)
        {
            Registro.Log("No pude abrir la barra lateral (WebView2): " + ex.Message);
        }
    }

    static void Abrir(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
    }

    /// <summary>Manda un objeto a la interfaz (se serializa a JSON).</summary>
    public void Enviar(object mensaje)
    {
        var json = JsonSerializer.Serialize(mensaje);
        if (lista && web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(json);
        else enCola.Add(json);
    }

    public void Mostrar()
    {
        if (!IsVisible) Show();
        reserva?.Reservar();
        Activate();
        web.Focus();
        Enviar(new { tipo = "abierta" });
    }

    public void Cerrar()
    {
        reserva?.Liberar();
        Hide();
    }
}
