using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Patch;

/// <summary>Llamadas directas a Windows: ventanas que no roban el foco, barra lateral reservada y atajo global.</summary>
public static class Nativo
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int alto, bool repintar);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string s);
    [DllImport("shell32.dll")] static extern uint SHAppBarMessage(uint msg, ref APPBARDATA d);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    /// <summary>La ventana no se activa al mostrarse ni al tocarla: el usuario sigue escribiendo donde estaba.</summary>
    public static void SinFoco(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        SetWindowLong(h, GWL_EXSTYLE, GetWindowLong(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    public static double Escala(Window w)
    {
        var h = new WindowInteropHelper(w).Handle;
        var dpi = h == IntPtr.Zero ? 96 : GetDpiForWindow(h);
        return (dpi == 0 ? 96 : dpi) / 96.0;
    }

    // ---------------- Barra lateral (AppBar): Windows le reserva el borde derecho y reacomoda las demás ventanas.

    public sealed class BarraReservada : IDisposable
    {
        readonly Window ventana;
        readonly IntPtr h;
        readonly uint mensaje;
        bool registrada;
        public int AnchoDip { get; set; }

        public BarraReservada(Window w, int anchoDip)
        {
            ventana = w;
            AnchoDip = anchoDip;
            h = new WindowInteropHelper(w).EnsureHandle();
            mensaje = RegisterWindowMessage("PATCH_BARRA_LATERAL");
            HwndSource.FromHwnd(h).AddHook(Gancho);
        }

        public void Reservar()
        {
            if (!registrada)
            {
                var d = Datos();
                d.uCallbackMessage = mensaje;
                SHAppBarMessage(0 /* ABM_NEW */, ref d);
                registrada = true;
            }
            Ubicar();
        }

        public void Liberar()
        {
            if (!registrada) return;
            var d = Datos();
            SHAppBarMessage(1 /* ABM_REMOVE */, ref d);
            registrada = false;
        }

        APPBARDATA Datos() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = h, uEdge = 2 /* ABE_RIGHT */ };

        void Ubicar()
        {
            var pantalla = System.Windows.Forms.Screen.FromHandle(h).Bounds;
            var ancho = (int)Math.Round(AnchoDip * Escala(ventana));
            var d = Datos();
            d.rc = new RECT { left = pantalla.Right - ancho, top = pantalla.Top, right = pantalla.Right, bottom = pantalla.Bottom };
            SHAppBarMessage(2 /* ABM_QUERYPOS */, ref d);
            d.rc.left = d.rc.right - ancho;
            SHAppBarMessage(3 /* ABM_SETPOS */, ref d);
            MoveWindow(h, d.rc.left, d.rc.top, d.rc.right - d.rc.left, d.rc.bottom - d.rc.top, true);
        }

        IntPtr Gancho(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool manejado)
        {
            // ABN_POSCHANGED (1): cambió la barra de tareas u otra barra; se vuelve a ubicar.
            if (registrada && msg == mensaje && w.ToInt32() == 1) Ubicar();
            return IntPtr.Zero;
        }

        public void Dispose() => Liberar();
    }

    /// <summary>
    /// Si PATCH se cerró de golpe con la barra abierta, Windows puede quedarse con el borde reservado
    /// (el área de trabajo queda angosta). Registrar y quitar una barra temporal hace que Windows limpie las huérfanas.
    /// </summary>
    public static void LimpiarReservasHuerfanas()
    {
        try
        {
            using var temporal = new HwndSource(new HwndSourceParameters("PATCH-limpieza") { Width = 0, Height = 0, WindowStyle = 0 });
            var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = temporal.Handle, uEdge = 2 };
            var p = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            SHAppBarMessage(0, ref d);
            d.rc = new RECT { left = p.Right - 1, top = p.Top, right = p.Right, bottom = p.Bottom };
            SHAppBarMessage(2, ref d);
            SHAppBarMessage(3, ref d);
            SHAppBarMessage(1, ref d);
        }
        catch (Exception ex) { Registro.Log("Limpieza de barras: " + ex.Message); }
    }

    // ---------------- Atajo global (Ctrl+Alt+Espacio)

    public sealed class Atajo : IDisposable
    {
        readonly HwndSource fuente;
        readonly Action accion;
        public bool Registrado { get; }

        public Atajo(Action accion)
        {
            this.accion = accion;
            fuente = new HwndSource(new HwndSourceParameters("PATCH-atajo") { Width = 0, Height = 0, WindowStyle = 0 });
            fuente.AddHook(Gancho);
            Registrado = RegisterHotKey(fuente.Handle, 1, 0x0001 /*ALT*/ | 0x0002 /*CTRL*/ | 0x4000 /*NOREPEAT*/, 0x20 /*ESPACIO*/);
            if (!Registrado) Registro.Log("No pude registrar el atajo Ctrl+Alt+Espacio (¿lo usa otra app?)");
        }

        IntPtr Gancho(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool manejado)
        {
            if (msg == 0x0312 /* WM_HOTKEY */) { accion(); manejado = true; }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterHotKey(fuente.Handle, 1);
            fuente.Dispose();
        }
    }
}
