using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Patch;

/// <summary>Un comando del usuario: una frase que lo activa ("buenos días") y los pasos que PATCH hace.</summary>
public sealed record Comando(string Archivo, string[] Frases, string Pasos)
{
    public string Nombre => Frases.Length > 0 ? Frases[0] : Path.GetFileNameWithoutExtension(Archivo);
}

/// <summary>
/// Los comandos viven en la carpeta comandos/ de PATCH, uno por archivo, para que el usuario y Claude los puedan leer y
/// escribir igual:
/// <code>
/// ---
/// frases: buenos días, buen día
/// ---
/// 1. Dime la hora.
/// 2. ...
/// </code>
/// La app reconoce la frase al instante (sin esperar a Claude) y le pasa los pasos para que los haga.
/// </summary>
public static class Comandos
{
    public static string Carpeta => Path.Combine(Config.Carpeta, "comandos");
    static string Ejemplos => Path.Combine(Config.Carpeta, "comandos-ejemplo");

    /// <summary>La primera vez crea la carpeta con los comandos de ejemplo (la carpeta es del usuario: git no la toca).</summary>
    public static void Preparar()
    {
        try
        {
            if (Directory.Exists(Carpeta)) return;
            Directory.CreateDirectory(Carpeta);
            if (Directory.Exists(Ejemplos))
                foreach (var f in Directory.GetFiles(Ejemplos, "*.md"))
                    File.Copy(f, Path.Combine(Carpeta, Path.GetFileName(f)));
            Registro.Log("Carpeta de comandos creada con los ejemplos");
        }
        catch (Exception ex) { Registro.Log("No pude preparar los comandos: " + ex.Message); }
    }

    public static List<Comando> Todos()
    {
        var r = new List<Comando>();
        if (!Directory.Exists(Carpeta)) return r;
        foreach (var f in Directory.GetFiles(Carpeta, "*.md").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            try { if (Leer(f) is { } c) r.Add(c); }
            catch (Exception ex) { Registro.Log($"No pude leer el comando {Path.GetFileName(f)}: {ex.Message}"); }
        }
        return r;
    }

    static Comando? Leer(string ruta)
    {
        var lineas = File.ReadAllLines(ruta, Encoding.UTF8).ToList();
        var frases = Array.Empty<string>();
        var inicio = 0;
        if (lineas.Count > 0 && lineas[0].Trim() == "---")
        {
            var fin = lineas.FindIndex(1, l => l.Trim() == "---");
            if (fin < 0) return null;
            foreach (var l in lineas.Skip(1).Take(fin - 1))
            {
                var i = l.IndexOf(':');
                if (i > 0 && l[..i].Trim().Equals("frases", StringComparison.OrdinalIgnoreCase))
                    frases = l[(i + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(x => x.Trim('"', '\'', '«', '»', ' ')).Where(x => x.Length > 0).ToArray();
            }
            inicio = fin + 1;
        }
        var pasos = string.Join("\n", lineas.Skip(inicio)).Trim();
        if (frases.Length == 0 || pasos.Length == 0) return null;
        return new Comando(Path.GetFileName(ruta), frases, pasos);
    }

    /// <summary>Palabras de cortesía que se pueden decir alrededor de la frase sin cambiar el pedido.</summary>
    static readonly HashSet<string> Relleno = new()
    {
        "oye", "oiga", "hey", "ey", "hola", "ok", "okey", "ya", "bueno", "pues", "porfa", "por", "favor", "porfavor", "jefe", "amigo",
    };

    /// <summary>
    /// El comando que pide esta orden: la frase tal cual (con alguna letra mal oída), sola o con palabras de cortesía
    /// ("buenos días, Patch", "buenos días, por favor"). Cualquier otra palabra de más la vuelve otra orden:
    /// "me voy a dormir" no es el comando «me voy», y "buenos días, ¿qué tengo en la tarde?" va a Claude normal.
    /// </summary>
    public static Comando? Buscar(string orden)
    {
        var nombres = Texto.Palabras(Config.Asistente + " " + Config.Usuario).ToHashSet();
        var dicho = Texto.Palabras(orden);
        if (dicho.Length == 0) return null;
        Comando? mejor = null;
        var mejorPuntos = int.MaxValue;
        foreach (var c in Todos())
            foreach (var frase in c.Frases)
            {
                var f = Texto.Palabras(frase);
                if (f.Length == 0) continue;
                // El relleno se quita solo si no es parte de la frase (un comando puede llamarse «hola»).
                var resto = dicho.Where(p => f.Contains(p) || !(Relleno.Contains(p) || nombres.Contains(p))).ToArray();
                var ff = string.Join(" ", f);
                var d = Texto.Distancia(string.Join(" ", resto), ff);
                if (d > ff.Length / 6) continue; // "buenos dias" tolera una letra mal oída; las frases cortas, ninguna
                var puntos = d * 10 + (dicho.Length - resto.Length);
                if (puntos < mejorPuntos) { mejor = c; mejorPuntos = puntos; }
            }
        return mejor;
    }

    /// <summary>Lo que se le manda a Claude para que haga el comando.</summary>
    public static string Orden(Comando c, string dicho) =>
        $"El usuario dijo «{dicho}», que activa su comando guardado «{c.Nombre}» (comandos/{c.Archivo}). " +
        "Hazlo ahora, sin pedir más detalles. Haz a la vez las consultas que no dependen unas de otras. " +
        "Para la hora usa Get-Date. Para el clima usa (ya está aprobado, con la URL entre comillas dobles y la latitud y longitud " +
        "de la ciudad que pide el paso; este ejemplo es Bogotá): Invoke-RestMethod \"https://api.open-meteo.com/v1/forecast?latitude=4.71&longitude=-74.07" +
        "&current=temperature_2m&daily=temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=auto&forecast_days=1\". " +
        "Al final da UNA sola respuesta hablada, corta y natural, que junte todo en el orden de los pasos. Los pasos:\n\n" + c.Pasos;

    /// <summary>Guarda un comando (nuevo si archivo es null) y devuelve el nombre del archivo.</summary>
    public static string Guardar(string? archivo, string frases, string pasos)
    {
        var lista = frases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 0).ToArray();
        if (lista.Length == 0) throw new ArgumentException("El comando necesita al menos una frase.");
        if (string.IsNullOrWhiteSpace(pasos)) throw new ArgumentException("El comando necesita pasos.");
        Directory.CreateDirectory(Carpeta);
        if (string.IsNullOrEmpty(archivo) || !Valido(archivo))
        {
            var baseNombre = string.Join("-", Texto.Palabras(lista[0])).Trim('-');
            if (baseNombre.Length == 0) baseNombre = "comando";
            if (baseNombre.Length > 40) baseNombre = baseNombre[..40];
            archivo = baseNombre + ".md";
            for (int n = 2; File.Exists(Path.Combine(Carpeta, archivo)); n++) archivo = $"{baseNombre}-{n}.md";
        }
        var texto = $"---\nfrases: {string.Join(", ", lista)}\n---\n{pasos.Trim().Replace("\r\n", "\n")}\n";
        File.WriteAllText(Path.Combine(Carpeta, archivo), texto, new UTF8Encoding(false));
        return archivo;
    }

    /// <summary>Lo manda a la papelera de reciclaje (se puede recuperar).</summary>
    public static void Borrar(string archivo)
    {
        if (!Valido(archivo)) return;
        var ruta = Path.Combine(Carpeta, archivo);
        if (!File.Exists(ruta)) return;
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(ruta,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
    }

    /// <summary>Solo un nombre de archivo .md dentro de comandos/, nunca una ruta.</summary>
    static bool Valido(string archivo) =>
        archivo.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && archivo == Path.GetFileName(archivo) &&
        archivo.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !archivo.StartsWith(".");
}
