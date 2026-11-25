using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Patch;

/// <summary>
/// patch-config.txt: compartido con el PATCH de Terminal, con los scripts de voz y con la app de Mac.
/// Formato clave=valor, una por línea, en UTF-8.
/// </summary>
public static class Config
{
    static readonly UTF8Encoding Utf8 = new(false);
    static readonly object Candado = new();

    public static string Carpeta { get; private set; } = "";
    public static string Ruta => Path.Combine(Carpeta, "patch-config.txt");
    public static string Datos => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PATCH");

    /// <summary>Busca la carpeta del proyecto: --carpeta, la que dejó el instalador, la del .exe hacia arriba, o ~/patch-plogy.</summary>
    public static void Iniciar(string[] args)
    {
        Directory.CreateDirectory(Datos);
        string? elegida = null;
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--carpeta") elegida = args[i + 1];
        var guardada = Path.Combine(Datos, "carpeta.txt");
        if (elegida == null && File.Exists(guardada)) elegida = File.ReadAllText(guardada).Trim();
        if (elegida == null || !EsProyecto(elegida))
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !EsProyecto(d.FullName)) d = d.Parent;
            elegida = d?.FullName ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "patch-plogy");
        }
        Carpeta = elegida;
        File.WriteAllText(guardada, Carpeta, Utf8);
        if (!File.Exists(Ruta))
        {
            var ejemplo = Path.Combine(Carpeta, "patch-config.ejemplo.txt");
            if (File.Exists(ejemplo)) File.Copy(ejemplo, Ruta);
        }
    }

    static bool EsProyecto(string c) => File.Exists(Path.Combine(c, "AGENTS.md")) && Directory.Exists(Path.Combine(c, "scripts"));

    public static Dictionary<string, string> Leer()
    {
        var r = new Dictionary<string, string>();
        lock (Candado)
        {
            if (!File.Exists(Ruta)) return r;
            foreach (var l in File.ReadAllLines(Ruta, Utf8))
            {
                var i = l.IndexOf('=');
                if (i > 0) r[l[..i].Trim()] = l[(i + 1)..].Trim();
            }
        }
        return r;
    }

    public static string Valor(string clave, string porDefecto = "")
    {
        var v = Leer().GetValueOrDefault(clave, "");
        return string.IsNullOrWhiteSpace(v) ? porDefecto : v;
    }

    public static void Poner(string clave, string valor)
    {
        valor = valor.Replace("\r", " ").Replace("\n", " ").Trim();
        lock (Candado)
        {
            var lineas = File.Exists(Ruta) ? File.ReadAllLines(Ruta, Utf8).ToList() : new List<string>();
            var i = lineas.FindIndex(l => l.StartsWith(clave + "="));
            if (i >= 0) lineas[i] = $"{clave}={valor}"; else lineas.Add($"{clave}={valor}");
            File.WriteAllLines(Ruta, lineas, Utf8);
        }
    }

    public static string Asistente => Valor("asistente", "Patch");
    public static string Usuario => Valor("usuario");
    public static string Modelo => Valor("modelo", "sonnet");
    public static bool VozActiva => Valor("voz", "on") != "off";
    public static bool Microfono => Valor("microfono", "on") != "off";
    public static bool EscucharNombre => Valor("escuchar_nombre", "on") != "off";
    public static string VozEdge => Valor("voz_edge", "es-CO-GonzaloNeural");
    public static int MaxCaracteres => int.TryParse(Valor("max_caracteres"), out var n) && n > 50 ? n : 600;
    public static string[] Alias => Valor("alias").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Registro en %LOCALAPPDATA%\PATCH\patch.log (para ajustar la palabra de activación y depurar).</summary>
public static class Registro
{
    static readonly object Candado = new();
    public static string Ruta => Path.Combine(Config.Datos, "patch.log");

    public static void Log(string s)
    {
        try
        {
            lock (Candado)
            {
                var info = new FileInfo(Ruta);
                if (info.Exists && info.Length > 2_000_000) File.Move(Ruta, Ruta + ".anterior", true);
                File.AppendAllText(Ruta, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {s}\r\n", Encoding.UTF8);
            }
        }
        catch { }
    }
}
