using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Patch;

/// <summary>Icono junto al reloj: abrir la barra, hablar, bloquear el micrófono, ajustes y salir.</summary>
public sealed class Bandeja : IDisposable
{
    readonly NotifyIcon icono;
    readonly Controlador c;
    readonly ToolStripMenuItem microfono;

    public Bandeja(Controlador c)
    {
        this.c = c;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir PATCH", null, (_, _) => c.AbrirBarra());
        microfono = new ToolStripMenuItem("Micrófono activo", null, (_, _) => c.PonerMicrofono(!c.MicrofonoActivo));
        menu.Items.Add(microfono);
        menu.Items.Add("Ajustes", null, (_, _) => c.AbrirBarra("ajustes"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir de PATCH", null, (_, _) => c.Salir());
        icono = new NotifyIcon
        {
            Icon = Marca.Icono(System.Windows.Forms.SystemInformation.SmallIconSize.Width),
            Text = "PATCH — di el nombre de tu asistente o Ctrl+Alt+P",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icono.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) c.AlternarBarra(); };
        Actualizar();
    }

    public void Actualizar()
    {
        microfono.Checked = c.MicrofonoActivo;
        icono.Text = c.MicrofonoActivo ? $"PATCH — di «{Config.Asistente}» o Ctrl+Alt+P" : "PATCH — descansando (Ctrl+Alt+P para despertarlo)";
    }

    public void Dispose()
    {
        icono.Visible = false;
        icono.Dispose();
    }
}
