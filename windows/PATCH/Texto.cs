using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Patch;

public static class Texto
{
    /// <summary>Minúsculas, sin tildes, solo letras y números, en palabras.</summary>
    public static string[] Palabras(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static int Distancia(string a, string b)
    {
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var cur = new int[b.Length + 1];
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = cur;
        }
        return prev[b.Length];
    }

    /// <summary>Palabras con las que se puede llamar al asistente antes del nombre.</summary>
    static readonly HashSet<string> Saludos = new() { "oye", "oiga", "hey", "ey", "hola", "ok", "okey", "bueno" };

    /// <summary>Palabras demasiado comunes para servir de alias (darían activaciones falsas).</summary>
    public static readonly HashSet<string> Comunes = new()
    {
        "a", "al", "de", "del", "el", "la", "las", "los", "lo", "le", "les", "un", "una", "y", "o", "que", "en", "es", "no", "si", "se",
        "me", "mi", "te", "tu", "su", "ya", "por", "para", "con", "como", "pero", "mas", "muy", "hay", "eso", "esto", "esa", "ese",
        "oye", "hola", "bien", "bueno", "va", "voy", "ver", "pues", "porque", "cuando", "donde", "aqui", "alla", "ahi",
    };

    /// <summary>Cómo suele oír el reconocedor el nombre por defecto (medido con Vosk y 6 voces).</summary>
    static readonly Dictionary<string, string[]> AliasConocidos = new()
    {
        ["patch"] = new[] { "patch", "pach", "pac", "pack", "paco", "pat", "patc" },
    };

    public static List<string> AliasDe(string nombre, IEnumerable<string> aprendidos)
    {
        var n = string.Join("", Palabras(nombre));
        var r = new List<string> { n };
        if (AliasConocidos.TryGetValue(n, out var c)) r.AddRange(c);
        r.AddRange(aprendidos.Select(a => string.Join(" ", Palabras(a))).Where(a => a.Length > 0));
        return r.Distinct().ToList();
    }

    /// <summary>
    /// Si el texto empieza por el nombre ("Patch, …" u "Oye Patch, …"), devuelve lo que viene después; si no, null.
    /// El nombre tiene que abrir la frase: así "le puse un parche…" no lo activa.
    /// </summary>
    public static string? DespuesDelNombre(string texto, string nombre, IReadOnlyList<string> alias) =>
        BuscarNombre(texto, nombre, alias, false, null) ?? BuscarNombre(texto, nombre, alias, true, null);

    /// <summary>
    /// Igual que <see cref="DespuesDelNombre"/>, pero para lo que transcribió Whisper: sin las confusiones de Vosk
    /// ("paco", "pack") y con una letra de diferencia como máximo ("Pach", "Path"). Así "Paco, ven a comer" no lo activa.
    /// </summary>
    public static string? DespuesDelNombreEstricto(string texto, string nombre)
    {
        var solo = new[] { string.Join("", Palabras(nombre)) };
        return BuscarNombre(texto, nombre, solo, false, 1) ?? BuscarNombre(texto, nombre, solo, true, 1);
    }

    /// <summary>
    /// Con <paramref name="separar"/>, prueba si el saludo y el nombre vienen juntos en la primera palabra, como a veces
    /// los escribe Whisper ("Oyepatch, abre Spotify"). Solo se intenta si la forma normal no encontró el nombre.
    /// </summary>
    static string? BuscarNombre(string texto, string nombre, IReadOnlyList<string> alias, bool separar, int? tolerancia)
    {
        var p = Palabras(texto);
        if (p.Length == 0) return null;
        int i = Saludos.Contains(p[0]) ? 1 : 0;
        int pegado = 0;
        if (separar)
        {
            var sal = i == 0 ? Saludos.FirstOrDefault(s => p[0].Length > s.Length + 2 && p[0].StartsWith(s)) : null;
            if (sal == null) return null;
            p = new[] { sal, p[0][sal.Length..] }.Concat(p.Skip(1)).ToArray();
            i = 1;
            pegado = 1; // en el texto original son una sola palabra
        }
        if (i >= p.Length) return null;
        var objetivo = string.Join("", Palabras(nombre));
        int tol = tolerancia ?? (objetivo.Length <= 4 ? 1 : 2);

        int usadas = 0;
        foreach (var a in alias)
        {
            var partes = a.Split(' ');
            if (i + partes.Length <= p.Length && p.Skip(i).Take(partes.Length).SequenceEqual(partes)) { usadas = partes.Length; break; }
        }
        if (usadas == 0)
        {
            var w = p[i];
            if (w.Length >= 3 && w[0] == objetivo[0] && Math.Abs(w.Length - objetivo.Length) <= 1 && Distancia(w, objetivo) <= tol) usadas = 1;
            else if (i + 1 < p.Length && Distancia(w + p[i + 1], objetivo) <= tol - 1) usadas = 2;
        }
        if (usadas == 0) return null;

        // Devuelve el resto del texto original (con tildes y mayúsculas), saltando las palabras ya usadas.
        var originales = Regex.Split(texto.Trim(), @"\s+");
        int saltar = i + usadas - pegado;
        var resto = string.Join(" ", originales.Skip(Math.Min(saltar, originales.Length)));
        return resto.Trim(' ', ',', '.', ':', ';', '!', '¡', '?', '¿', '-');
    }

    /// <summary>De lo que oyó en la calibración, saca alias útiles (la primera o las dos primeras palabras).</summary>
    public static (List<string> alias, string? aviso) Calibrar(string nombre, IEnumerable<string> intentos)
    {
        var alias = new List<string>();
        string? aviso = null;
        foreach (var t in intentos)
        {
            var p = Palabras(t).Where(w => !Saludos.Contains(w)).ToArray();
            if (p.Length == 0) continue;
            var candidato = p.Length >= 2 && p[0].Length <= 3 && !Comunes.Contains(p[1]) ? $"{p[0]} {p[1]}" : p[0];
            if (Comunes.Contains(candidato)) { aviso = $"Te oí «{candidato}», que es una palabra muy común. Prueba un nombre más distinto, de dos o tres sílabas."; continue; }
            alias.Add(candidato);
        }
        return (alias.Distinct().ToList(), aviso);
    }

    static readonly HashSet<string> PalabrasDeDescanso = new()
    {
        "silencio", "callate", "calla", "callese", "callado", "descansa", "descansar", "descanso", "duermete", "dormir",
        "duerme", "detente", "basta", "stop", "suficiente", "shh", "shhh",
    };
    static readonly string[] FrasesDeDescanso =
    {
        "deja de escuchar", "deja de hablar", "no escuches", "no me escuches", "dejame en paz", "apagate", "no hables",
    };

    /// <summary>
    /// "Silencio", "descansa", "cállate", "deja de escuchar"… La app lo resuelve sola (sin Claude): se calla y deja de escuchar.
    /// Solo cuenta en frases cortas, para no confundir "busca el informe de silencio administrativo" con una orden de parar.
    /// </summary>
    public static bool EsOrdenDeDescanso(string s)
    {
        var p = Palabras(s);
        if (p.Length == 0 || p.Length > 6) return false;
        var junto = " " + string.Join(" ", p) + " ";
        return p.Any(PalabrasDeDescanso.Contains) || FrasesDeDescanso.Any(f => junto.Contains(" " + f + " "));
    }

    /// <summary>
    /// Mientras PATCH habla, el micrófono también oye su voz. Una palabra de parada cuenta solo si no está en lo que él
    /// mismo está diciendo (así no se interrumpe solo).
    /// </summary>
    public static bool EsParadaMientrasHabla(string oido, string loQueDice)
    {
        if (!EsOrdenDeDescanso(oido)) return false;
        var propias = Palabras(loQueDice).ToHashSet();
        return Palabras(oido).Any(w => (PalabrasDeDescanso.Contains(w)) && !propias.Contains(w));
    }

    static readonly HashSet<string> PalabrasDeCancelar = new()
    {
        "cancela", "cancelalo", "cancelala", "cancelar", "olvidalo", "olvidala", "olvida", "aborta", "abortar",
    };

    /// <summary>"Cancela", "olvídalo", "para", "espera"…: deja la tarea en curso (sin cerrar el micrófono). Solo frases cortas.</summary>
    public static bool EsCancelacion(string s)
    {
        var p = Palabras(s);
        if (p.Length == 0 || p.Length > 4) return false;
        if (p.Any(PalabrasDeCancelar.Contains)) return true;
        return string.Join(" ", p) is "para" or "para ya" or "ya para" or "para para" or "espera" or "no espera" or "espera espera";
    }

    public static bool EsNegativo(string s)
    {
        var p = Palabras(s).ToHashSet();
        return p.Contains("no") || p.Contains("cancela") || p.Contains("cancelar") || p.Contains("espera") || p.Contains("detente");
    }

    public static bool EsAfirmativo(string s)
    {
        var si = new HashSet<string> { "si", "dale", "hazlo", "autorizo", "autorizado", "autoriza", "claro", "adelante", "ok", "okay",
            "okey", "vale", "listo", "correcto", "hagale", "procede", "permitelo", "confirmo", "perfecto" };
        return Palabras(s).Any(si.Contains);
    }

    static readonly HashSet<string> Interrogativas = new()
        { "que", "cual", "cuales", "como", "cuando", "donde", "adonde", "quien", "quienes", "cuanto", "cuanta", "cuantos", "cuantas" };
    static readonly HashSet<string> Preposiciones = new() { "por", "para", "a", "en", "de", "con", "desde", "hasta", "sobre" };

    /// <summary>
    /// Si la respuesta termina en una pregunta de sí o no ("¿Quieres que lo abra?"), para mostrar los botones Sí/No.
    /// Una pregunta abierta ("¿Qué necesitas?", "¿Para qué hora?") no lleva botones.
    /// </summary>
    public static bool EsPreguntaSiNo(string t)
    {
        t = t.TrimEnd();
        if (t.Length < 2 || !t.EndsWith("?")) return false;
        var i = t.LastIndexOf('¿');
        var pregunta = i >= 0 ? t[(i + 1)..] : t[(t.LastIndexOfAny(new[] { '.', '!', '\n' }, t.Length - 2) + 1)..];
        var p = Palabras(pregunta).SkipWhile(w => w is "y" or "pero" or "entonces" or "bueno" or "o").ToArray();
        if (p.Length == 0) return false;
        if (Interrogativas.Contains(p[0])) return false;
        if (p.Length > 1 && Preposiciones.Contains(p[0]) && Interrogativas.Contains(p[1])) return false;
        if (p.Skip(1).Contains("o")) return false; // "¿lo borro o lo muevo?" es elegir, no sí o no
        return true;
    }

    /// <summary>Quita markdown, enlaces y tablas para leerlo en voz alta. Corta en un final de frase.</summary>
    public static string ParaVoz(string t, int max)
    {
        t = Regex.Replace(t, "```[\\s\\S]*?```", " Te dejé el código en pantalla. ");
        t = Regex.Replace(t, "`([^`]*)`", "$1");
        t = Regex.Replace(t, @"!\[[^\]]*\]\([^)]*\)", "");
        t = Regex.Replace(t, @"\[([^\]]+)\]\([^)]*\)", "$1");
        t = Regex.Replace(t, @"https?://\S+", "");
        t = string.Join("\n", t.Split('\n').Where(l => !l.TrimStart().StartsWith("|")));
        t = Regex.Replace(t, @"(?m)^\s*(Sources|Fuentes):.*$[\s\S]*", "");
        t = Regex.Replace(t, @"(?m)^\s*#{1,6}\s*", "");
        t = Regex.Replace(t, @"(?m)^\s*([-*•]|\d+\.)\s+", "");
        t = Regex.Replace(t, @"[*_~>#]", "");
        t = Regex.Replace(t, @"\s*\n+\s*", ". ");
        t = Regex.Replace(t, @"([:;,.?!])\s*\.", "$1");
        t = Regex.Replace(t, @"\s{2,}", " ").Trim();
        if (t.Length > max)
        {
            var corte = t[..max];
            var fin = new[] { corte.LastIndexOf(". "), corte.LastIndexOf("? "), corte.LastIndexOf("! ") }.Max();
            t = (fin > max * 0.4 ? corte[..(fin + 1)] : corte) + " El resto está en pantalla.";
        }
        return t;
    }

    /// <summary>Primera frase aparte (si es razonable), para empezar a hablar mientras se genera el resto.</summary>
    public static string[] DividirFrases(string t)
    {
        var m = Regex.Match(t, @"^(.{15,220}?[.?!:])\s+(\S.*)$", RegexOptions.Singleline);
        return m.Success ? new[] { m.Groups[1].Value, m.Groups[2].Value } : new[] { t };
    }
}
