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
            Icon = Dibujar(),
            Text = "PATCH — di el nombre de tu asistente o Ctrl+Alt+Espacio",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icono.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) c.AlternarBarra(); };
        Actualizar();
    }

    public void Actualizar()
    {
        microfono.Checked = c.MicrofonoActivo;
        icono.Text = c.MicrofonoActivo ? $"PATCH — di «{Config.Asistente}» o Ctrl+Alt+Espacio" : "PATCH — descansando (Ctrl+Alt+Espacio para despertarlo)";
    }

    /// <summary>Círculo naranja de Plogy con una P blanca.</summary>
    static Icon Dibujar()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var naranja = new SolidBrush(Color.FromArgb(0xFF, 0x5B, 0x00));
            g.FillEllipse(naranja, 1, 1, 30, 30);
            using var f = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            var tam = g.MeasureString("P", f);
            g.DrawString("P", f, Brushes.White, (32 - tam.Width) / 2 + 0.5f, (32 - tam.Height) / 2);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        icono.Visible = false;
        icono.Dispose();
    }
}
