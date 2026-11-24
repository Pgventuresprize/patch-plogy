using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Patch;

public enum TipoPermiso { Normal, Borrar, Enviar, Compartir }

/// <summary>Un permiso que Claude Code no pudo usar sin preguntar.</summary>
public sealed class Denegado
{
    public string Herramienta { get; init; } = "";
    public JsonElement Entrada { get; init; }

    string? Campo(string c) => Entrada.ValueKind == JsonValueKind.Object && Entrada.TryGetProperty(c, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public string Regla => Herramienta is "Bash" or "PowerShell" && Campo("command") is { } c ? $"{Herramienta}({c})" : Herramienta;

    public TipoPermiso Tipo
    {
        get
        {
            var h = Herramienta.ToLowerInvariant();
            var c = (Campo("command") ?? "").ToLowerInvariant();
            if (h.Contains("trash") || h.Contains("delete") || c.Contains("papelera.ps1") || c.Contains("papelera.sh") || c.Contains("remove-item")) return TipoPermiso.Borrar;
            if (h.EndsWith("send_message") || h.EndsWith("__forward") || h.EndsWith("__reply") || h.Contains("send_now")) return TipoPermiso.Enviar;
            if (h.Contains("share_file") || h.Contains("permission")) return TipoPermiso.Compartir;
            return TipoPermiso.Normal;
        }
    }

    public string Descripcion => Herramienta switch
    {
        "Bash" or "PowerShell" => (Campo("description") is { } d ? char.ToLower(d[0]) + d[1..] : "ejecutar un comando"),
        "Write" or "Edit" or "MultiEdit" => "escribir en " + Path.GetFileName(Campo("file_path") ?? "un archivo"),
        _ => "usar " + Cerebro.NombreAmigable(Herramienta),
    };

    public string Detalle => Campo("command") ?? Campo("file_path") ?? (Entrada.ValueKind == JsonValueKind.Object ? Entrada.GetRawText() : Herramienta);
}

public sealed class Resultado
{
    public string Texto { get; init; } = "";
    public List<Denegado> Denegados { get; init; } = new();
    public bool Error { get; init; }
}

/// <summary>Corre PATCH con Claude Code sin ventana (claude -p, stream-json) y conserva la conversación.</summary>
public sealed class Cerebro
{
    string? sesion;
    DateTime ultimaVez = DateTime.MinValue;
    Process? proceso;
    int corrida;

    public bool Ocupado => proceso != null;
    public void NuevaConversacion() { sesion = null; Registro.Log("Conversación nueva"); }

    public void Cancelar()
    {
        var p = proceso;
        proceso = null;
        corrida++;
        try { if (p is { HasExited: false }) p.Kill(true); } catch { }
        if (p != null) Registro.Log("Consulta cancelada");
    }

    public static string? RutaClaude()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe");
        if (File.Exists(local)) return local;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { var c = Path.Combine(dir.Trim(), "claude.exe"); if (File.Exists(c)) return c; } catch { }
        }
        return null;
    }

    /// <summary>
    /// En modo sin pantalla, Claude Code ignora los permisos del proyecto si la carpeta no fue marcada como confiable.
    /// Por eso la app se los pasa explícitamente (los mismos de .claude/settings.json y settings.local.json).
    /// </summary>
    static List<string> PermisosDelProyecto()
    {
        var r = new List<string>();
        foreach (var f in new[] { "settings.json", "settings.local.json" })
        {
            var ruta = Path.Combine(Config.Carpeta, ".claude", f);
            if (!File.Exists(ruta)) continue;
            try
            {
                var raiz = JsonDocument.Parse(File.ReadAllText(ruta)).RootElement;
                if (raiz.TryGetProperty("permissions", out var p) && p.TryGetProperty("allow", out var a))
                    r.AddRange(a.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
            }
            catch (Exception ex) { Registro.Log($"No pude leer {f}: {ex.Message}"); }
        }
        return r.Distinct().ToList();
    }

    static string Indicacion() =>
        $"Estás respondiendo desde la app de PATCH en Windows. Te llamas {Config.Asistente}. " +
        "El usuario te habla por voz o te escribe en la barra lateral, y no ve ninguna terminal: tu respuesta se lee en voz alta " +
        "y se muestra en una ventanita pequeña, así que sé breve. Trabajas en segundo plano: nunca uses el mouse, el teclado ni el " +
        "navegador del usuario. Abre algo en pantalla solo si el resultado es para verlo. " +
        "Cada permiso que pidas interrumpe al usuario: usa solo comandos ya aprobados y simples. Para buscar archivos usa " +
        ".\\scripts\\buscar.ps1; para leer Word, Excel o PowerPoint, .\\scripts\\leer.ps1; para PDF, la herramienta Read. " +
        "Nunca escribas tu propio código para abrir o convertir archivos. " +
        "No puedes mostrar diálogos de permiso: si una acción necesita permiso, inténtala igual y la app le pedirá la autorización " +
        "al usuario (por voz, o por escrito si es borrar, enviar o compartir). Tus reglas de confirmación siguen vigentes.";

    public async Task<Resultado> Preguntar(string texto, IEnumerable<string>? permitir, Action<string> estado)
    {
        Cancelar();
        var mia = ++corrida;
        var claude = RutaClaude();
        if (claude == null) return new Resultado { Texto = "No encuentro Claude Code instalado en este computador.", Error = true };
        if ((DateTime.Now - ultimaVez).TotalMinutes > 15) sesion = null;

        var psi = new ProcessStartInfo(claude)
        {
            WorkingDirectory = Config.Carpeta,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in new[] { "-p", "--output-format", "stream-json", "--verbose", "--model", Config.Modelo,
                                  "--append-system-prompt", Indicacion(), "--add-dir", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
            psi.ArgumentList.Add(a);
        if (sesion != null) { psi.ArgumentList.Add("--resume"); psi.ArgumentList.Add(sesion); }
        psi.ArgumentList.Add("--disallowedTools");
        psi.ArgumentList.Add("mcp__claude-in-chrome");
        var reglas = PermisosDelProyecto();
        if (permitir != null) reglas.AddRange(permitir);
        if (reglas.Count > 0) { psi.ArgumentList.Add("--allowedTools"); foreach (var r in reglas) psi.ArgumentList.Add(r); }
        psi.Environment["PATCH_APP"] = "1";

        Process p;
        try { p = Process.Start(psi)!; }
        catch (Exception ex) { return new Resultado { Texto = "No pude arrancar Claude Code: " + ex.Message, Error = true }; }
        proceso = p;
        ultimaVez = DateTime.Now;
        Registro.Log($"Orden ({Config.Modelo}): «{texto}»" + (permitir != null ? $" con permiso para {string.Join(", ", permitir)}" : ""));

        using (var w = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false))) await w.WriteAsync(texto);
        var errores = p.StandardError.ReadToEndAsync();
        string? resultado = null, sesionNueva = null;
        bool esError = false;
        var denegados = new List<Denegado>();
        estado("Pensando…");

        string? linea;
        while ((linea = await p.StandardOutput.ReadLineAsync()) != null)
        {
            if (mia != corrida) break;
            JsonElement o;
            try { o = JsonDocument.Parse(linea).RootElement; } catch { continue; }
            switch (o.TryGetProperty("type", out var t) ? t.GetString() : null)
            {
                case "system":
                    if (o.TryGetProperty("session_id", out var s)) sesionNueva = s.GetString();
                    break;
                case "assistant":
                    if (o.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array)
                        foreach (var b in c.EnumerateArray())
                            if (b.TryGetProperty("type", out var bt) && bt.GetString() == "tool_use")
                            {
                                var e = EstadoPara(b.GetProperty("name").GetString() ?? "", b.TryGetProperty("input", out var inp) ? inp : default);
                                if (e != null) estado(e);
                            }
                    break;
                case "result":
                    resultado = o.TryGetProperty("result", out var r) ? r.GetString() : null;
                    esError = o.TryGetProperty("is_error", out var ie) && ie.GetBoolean();
                    if (o.TryGetProperty("session_id", out var s2)) sesionNueva = s2.GetString();
                    if (o.TryGetProperty("permission_denials", out var pd) && pd.ValueKind == JsonValueKind.Array)
                        foreach (var d in pd.EnumerateArray())
                            denegados.Add(new Denegado
                            {
                                Herramienta = d.TryGetProperty("tool_name", out var tn) ? tn.GetString() ?? "" : "",
                                Entrada = d.TryGetProperty("tool_input", out var ti) ? ti.Clone() : default,
                            });
                    break;
            }
        }
        if (mia != corrida) return new Resultado { Texto = "", Error = true };
        await p.WaitForExitAsync();
        proceso = null;
        if (sesionNueva != null) sesion = sesionNueva;
        ultimaVez = DateTime.Now;
        if (resultado == null)
        {
            var err = await errores;
            Registro.Log($"Claude Code terminó sin respuesta (código {p.ExitCode}): {err[..Math.Min(500, err.Length)]}");
            return new Resultado { Texto = "Tuve un problema hablando con Claude. Revisa que tu sesión de Claude Code siga abierta.", Error = true };
        }
        Registro.Log($"Respuesta ({denegados.Count} permisos pendientes): {resultado[..Math.Min(200, resultado.Length)]}");
        return new Resultado { Texto = resultado, Denegados = denegados, Error = esError };
    }

    public static string NombreAmigable(string h)
    {
        if (h.Contains("Calendar")) return "tu calendario";
        if (h.Contains("Gmail")) return "tu correo";
        if (h.Contains("Drive")) return "tu Drive";
        if (h.Contains("Spotify")) return "Spotify";
        return h switch { "WebSearch" => "la búsqueda web", "WebFetch" => "una página web", _ => h };
    }

    static string? EstadoPara(string h, JsonElement e)
    {
        string? Campo(string c) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(c, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        if (h.Contains("Calendar")) return "Revisando tu agenda…";
        if (h.Contains("Gmail")) return "Revisando tu correo…";
        if (h.Contains("Drive")) return "Buscando en tu Drive…";
        switch (h)
        {
            case "WebSearch": return Campo("query") is { } q ? $"Buscando: {q}" : "Buscando en internet…";
            case "WebFetch": return "Leyendo una página…";
            case "Read": case "Glob": case "Grep": case "LS": return "Revisando archivos…";
            case "Write": case "Edit": case "MultiEdit": return "Escribiendo…";
            case "Task": case "Agent": return "Investigando a fondo…";
            case "TodoWrite": case "ToolSearch": return null;
            case "Skill":
                return Campo("skill") switch
                {
                    "briefing" => "Preparando tu briefing…",
                    "ordenar-escritorio" or "organizar" => "Organizando…",
                    "buscar-documentos" => "Buscando tus documentos…",
                    "investigar" => "Investigando…",
                    _ => "Trabajando…",
                };
            case "Bash":
            case "PowerShell":
                var c = (Campo("command") ?? "").Trim();
                if (c.Contains("buscar.")) return "Buscando en tu computador…";
                if (c.Contains("leer.")) return "Leyendo el documento…";
                if (c.StartsWith("Start-Process") || c.StartsWith("open")) return "Abriendo…";
                if (c.StartsWith("Move-Item") || c.StartsWith("mv")) return "Moviendo archivos…";
                return Campo("description") is { } d ? d + "…" : "Trabajando…";
            default: return "Trabajando…";
        }
    }
}
