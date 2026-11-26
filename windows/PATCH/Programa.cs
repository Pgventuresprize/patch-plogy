using System;
using System.Threading;
using System.Windows;

namespace Patch;

public static class Programa
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Pruebas sin ventana (no chocan con la copia abierta): escriben el resultado en el registro y salen.
        //   PATCH.exe --probar-cuentas
        //   PATCH.exe --probar-cerebro chatgpt|claude "orden"
        if (Array.IndexOf(args, "--probar-cuentas") >= 0 || Array.IndexOf(args, "--probar-cerebro") >= 0)
        {
            Config.Iniciar(Array.Empty<string>());
            Pruebas(args).GetAwaiter().GetResult();
            return;
        }
        //   PATCH.exe --probar-marca   → dibuja la ventanita con la marca en %LOCALAPPDATA%\PATCH\prueba-marca.png
        if (Array.IndexOf(args, "--probar-marca") >= 0)
        {
            Config.Iniciar(Array.Empty<string>());
            ProbarMarca();
            return;
        }
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

    static void ProbarMarca()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Startup += (_, _) =>
        {
            try
            {
                using (var ico = Marca.Icono(32)) Registro.Log($"Prueba de marca · ícono {ico.Width}x{ico.Height}");
                Registro.Log($"Prueba de marca · fuente: {string.Join(", ", Marca.Display.FamilyNames.Values)} · caras {Marca.Display.GetTypefaces().Count}");
                var p = new Pildora();
                p.Mostrar(Fase.Permiso, "Necesito tu permiso", "Quiero crear la carpeta Paquetes revisados dentro de Plogy.", true);
                p.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
                {
                    var v = (System.Windows.FrameworkElement)p.Content;
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(v.ActualWidth * 2), (int)(v.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                    bmp.Render(v);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using (var f = System.IO.File.Create(System.IO.Path.Combine(Config.Datos, "prueba-marca.png"))) enc.Save(f);
                    Registro.Log("Prueba de marca · ventanita dibujada");
                    app.Shutdown();
                });
            }
            catch (Exception ex) { Registro.Log("Prueba de marca · ERROR: " + ex); app.Shutdown(); }
        };
        app.Run();
    }

    static async System.Threading.Tasks.Task Pruebas(string[] args)
    {
        foreach (var p in new[] { "claude", "chatgpt" })
        {
            var e = await Cuentas.Consultar(p);
            Registro.Log($"Prueba de cuentas · {p}: instalado={e.Instalado} conectado={e.Conectado} ({e.Detalle})");
        }
        var i = Array.IndexOf(args, "--probar-cerebro");
        if (i < 0 || i + 2 >= args.Length) return;
        ICerebro cerebro = args[i + 1] == "chatgpt" ? new CerebroCodex() : new Cerebro();
        var r = await cerebro.Preguntar(args[i + 2], null, s => Registro.Log("Prueba · estado: " + s));
        Registro.Log($"Prueba de cerebro · {args[i + 1]} (error={r.Error}, permisos={r.Denegados.Count}): {r.Texto}");
        var j = i + 3 < args.Length ? args[i + 3] : null;
        if (j != null)
        {
            r = await cerebro.Preguntar(j, null, s => Registro.Log("Prueba · estado: " + s));
            Registro.Log($"Prueba de cerebro · seguimiento (error={r.Error}): {r.Texto}");
        }
        cerebro.Cerrar();
    }
}
