using System;
using System.Threading;
using System.Windows;

namespace Patch;

public static class Programa
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Una sola copia de PATCH. Si ya está abierta, se le pide que muestre la barra lateral.
        using var unica = new Mutex(true, "Plogy.PATCH.unica", out var primera);
        var abrir = new EventWaitHandle(false, EventResetMode.AutoReset, "Plogy.PATCH.abrir");
        var salir = new EventWaitHandle(false, EventResetMode.AutoReset, "Plogy.PATCH.salir");
        if (!primera)
        {
            // PATCH.exe --salir cierra la copia abierta de forma ordenada (la usa el instalador al actualizar).
            if (Array.IndexOf(args, "--salir") >= 0) salir.Set(); else abrir.Set();
            return;
        }
        if (Array.IndexOf(args, "--salir") >= 0) return;

        Config.Iniciar(args);
        // Dibujo por software si se pide (o si la tarjeta gráfica no está disponible para este proceso).
        if (Array.IndexOf(args, "--software") >= 0 || Environment.GetEnvironmentVariable("PATCH_SOFTWARE") == "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Registro.Log("Error no controlado: " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { Registro.Log("Error fatal: " + e.ExceptionObject); BarraLateral.LiberarTodas(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => BarraLateral.LiberarTodas();

        Controlador? c = null;
        app.Startup += (_, _) =>
        {
            Nativo.LimpiarReservasHuerfanas();
            c = new Controlador();
            c.Arrancar();
            // Otra copia pidió abrir la barra
            new Thread(() => { while (abrir.WaitOne()) app.Dispatcher.BeginInvoke(() => c.AbrirBarra()); }) { IsBackground = true }.Start();
            new Thread(() => { if (salir.WaitOne()) app.Dispatcher.BeginInvoke(() => c.Salir()); }) { IsBackground = true }.Start();
        };
        app.Run();
    }
}
