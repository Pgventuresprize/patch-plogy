using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
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

/// <summary>
/// Habla con Claude Code sin ventana. Mantiene UN proceso abierto (stream-json de entrada y salida) y le manda cada
/// orden por la misma conexión: así cada turno tarda ~1,5 s en arrancar en vez de ~8 s.
/// Para una acción ya confirmada por el usuario se usa un proceso aparte con ese permiso exacto, y luego se sigue.
/// </summary>
public sealed class Cerebro
{
    sealed class Turno
    {
        public readonly TaskCompletionSource<Resultado> Fin = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action<string> Estado = _ => { };
        public readonly List<Denegado> Denegados = new();
        public bool EnVivo;    // corre en el proceso abierto (se le pueden sumar mensajes a mitad)
    }

    /// <summary>
    /// Una respuesta que llegó sin turno esperando: pasa cuando se sumó un mensaje justo cuando Claude terminaba,
    /// y Claude lo contesta como un turno aparte.
    /// </summary>
    public event Action<Resultado>? Suelta;

    readonly object candado = new();
    Process? vivo;
    string firmaVivo = "";
    Turno? turno;
    string? sesion;
    DateTime ultimaVez = DateTime.MinValue;
    int generacion;

    public bool Ocupado => turno != null;

    // Permisos que el usuario ya aprobó con un "sí" mientras la app está abierta: no se vuelven a pedir.
    readonly List<string> aprobados = new();

    /// <summary>
    /// Recuerda un permiso aprobado para el resto de la sesión. Solo comandos exactos y herramientas concretas:
    /// nunca "Write"/"Edit" sin ruta (sería escribir en cualquier parte), ni borrar, enviar o compartir.
    /// </summary>
    public void Recordar(IEnumerable<Denegado> ds)
    {
        lock (aprobados)
            foreach (var d in ds)
            {
                if (d.Tipo != TipoPermiso.Normal) continue;
                if (d.Herramienta is "Write" or "Edit" or "MultiEdit" or "NotebookEdit") continue;
                if (!aprobados.Contains(d.Regla)) aprobados.Add(d.Regla);
            }
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

    /// <summary>La app ya habla y se calla sola: los hooks de voz del proyecto solo le sumarían ~2 s por turno.</summary>
    static string AjustesSinHooks()
    {
        var ruta = Path.Combine(Config.Datos, "claude-sin-hooks.json");
        if (!File.Exists(ruta)) File.WriteAllText(ruta, "{\"disableAllHooks\": true}");
        return ruta;
    }

    static string Indicacion() =>
        $"Estás respondiendo desde la app de PATCH en Windows. Te llamas {Config.Asistente}. " +
        "El usuario te habla por voz o te escribe en la barra lateral, y no ve ninguna terminal: tu respuesta se lee en voz alta " +
        "y se muestra en una ventanita pequeña, así que sé breve. Trabajas en segundo plano: nunca uses el mouse, el teclado ni el " +
        "navegador del usuario. Abre algo en pantalla solo si el resultado es para verlo. " +
        "Cada permiso que pidas interrumpe al usuario: usa solo comandos ya aprobados y simples. Para buscar archivos usa " +
        ".\\scripts\\buscar.ps1; para leer Word, Excel o PowerPoint, .\\scripts\\leer.ps1; para PDF, la herramienta Read; " +
        "para ordenar o mover varios archivos, .\\scripts\\organizar.ps1 (primero con -Vista). " +
        "En PowerShell escribe las rutas completas, nunca variables como $env:USERPROFILE o $HOME, y no uses bloques { }, " +
        "ForEach-Object, [Clase]::Metodo() ni paréntesis como (Get-ChildItem ...).Count: eso siempre pide permiso. " +
        "Filtra con la forma simple (Where-Object Length -gt 50MB), cuenta con | Measure-Object y busca un tipo por " +
        "comando con -Filter *.mp4 (una lista con coma como -Include *.mp4,*.mov pide permiso). " +
        $"Rutas de este equipo: carpeta del usuario {Ruta(Environment.SpecialFolder.UserProfile)}, Escritorio {Ruta(Environment.SpecialFolder.Desktop)}, " +
        $"Documentos {Ruta(Environment.SpecialFolder.MyDocuments)}, Descargas {Descargas()}, Imágenes {Ruta(Environment.SpecialFolder.MyPictures)}. " +
        "Nunca escribas tu propio código o script para abrir, convertir o mover archivos, ni para saltarte un permiso, " +
        "y nunca le pidas al usuario que corra comandos a mano. " +
        "No puedes mostrar diálogos de permiso: si una acción necesita permiso, inténtala igual y la app le pedirá la autorización " +
        "al usuario (por voz, o por escrito si es borrar, enviar o compartir). Si una acción quedó esperando ese permiso, " +
        "no digas que falló: di en una frase qué vas a hacer y que necesitas su confirmación. Tus reglas de confirmación siguen vigentes.";

    static string Ruta(Environment.SpecialFolder c) => Environment.GetFolderPath(c);

    /// <summary>La carpeta de Descargas real (puede estar movida); .NET no la trae como SpecialFolder.</summary>
    static string Descargas()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");
            if (k?.GetValue("{374DE290-123F-4565-9164-39C4925E467B}") is string s) return Environment.ExpandEnvironmentVariables(s);
        }
        catch { }
        return Path.Combine(Ruta(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>Lo que hace que el proceso abierto deba reiniciarse si cambia (modelo o nombre del asistente).</summary>
    static string Firma() => Config.Modelo + "|" + Config.Asistente;

    ProcessStartInfo Arranque(string claude, bool persistente, IEnumerable<string>? extra)
    {
        var psi = new ProcessStartInfo(claude)
        {
            WorkingDirectory = Config.Carpeta,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        var args = new List<string> { "-p", "--output-format", "stream-json", "--verbose", "--model", Config.Modelo,
            "--settings", AjustesSinHooks(), "--append-system-prompt", Indicacion(),
            "--add-dir", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        if (persistente) args.AddRange(new[] { "--input-format", "stream-json" });
        if (sesion != null) args.AddRange(new[] { "--resume", sesion });
        args.AddRange(new[] { "--disallowedTools", "mcp__claude-in-chrome" });
        var reglas = PermisosDelProyecto();
        lock (aprobados) reglas.AddRange(aprobados);
        if (extra != null) reglas.AddRange(extra);
        if (reglas.Count > 0) { args.Add("--allowedTools"); args.AddRange(reglas); }
        foreach (var a in args) psi.ArgumentList.Add(a);
        LimpiarEntorno(psi.Environment);
        psi.Environment["PATCH_APP"] = "1";
        return psi;
    }

    /// <summary>
    /// Si PATCH se abrió desde otra sesión de Claude Code (por ejemplo, un instalador corrido por un agente),
    /// hereda variables de esa sesión (su id, su canal de mensajes, "ignorar la confianza"...). No deben pasar a Claude.
    /// </summary>
    static readonly System.Text.RegularExpressions.Regex VariablesDeSesion = new(
        "^(CLAUDECODE|CLAUDE_PID|CLAUDE_AGENT_SDK_VERSION|AI_AGENT|MCP_CONNECTION_NONBLOCKING|" +
        "CLAUDE_CODE_(ENTRYPOINT|IGNORE_PERSISTED_TRUST|CHILD_.*|SESSION.*|MESSAGING.*|EMIT_STARTUP_TIMING|" +
        "ENABLE_SDK_FILE_CHECKPOINTING|ENABLE_TASKS|QUESTION_PREVIEW_FORMAT|STARTUP_FAILURE_RESULTS|SSE_PORT))$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static void LimpiarEntorno(IDictionary<string, string?> entorno)
    {
        foreach (var k in entorno.Keys.Where(k => VariablesDeSesion.IsMatch(k)).ToList()) entorno.Remove(k);
    }

    /// <summary>Abre el proceso de Claude por adelantado (al arrancar la app), para que la primera orden no espere.</summary>
    public void Precalentar() => Task.Run(() => { lock (candado) AsegurarVivo(); });

    /// <summary>Debe llamarse con el candado tomado.</summary>
    Process? AsegurarVivo()
    {
        if ((DateTime.Now - ultimaVez).TotalMinutes > 15 && sesion != null && turno == null)
        {
            // Tras 15 minutos sin hablar, se empieza una conversación nueva.
            sesion = null;
            Matar();
        }
        if (vivo is { HasExited: false } && firmaVivo == Firma()) return vivo;
        Matar();
        var claude = RutaClaude();
        if (claude == null) return null;
        try
        {
            var p = Process.Start(Arranque(claude, true, null))!;
            var gen = ++generacion;
            vivo = p;
            firmaVivo = Firma();
            p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Registro.Log("claude: " + e.Data); };
            p.BeginErrorReadLine();
            _ = Task.Run(() => Leer(p, gen));
            Registro.Log($"Claude abierto y esperando ({Config.Modelo}{(sesion != null ? ", continúa la conversación" : "")})");
            return p;
        }
        catch (Exception ex) { Registro.Log("No pude arrancar Claude Code: " + ex.Message); return null; }
    }

    void Matar()
    {
        var p = vivo;
        vivo = null;
        generacion++;
        try { if (p is { HasExited: false }) p.Kill(true); } catch { }
    }

    /// <summary>Lee la salida del proceso abierto y la reparte al turno en curso.</summary>
    async Task Leer(Process p, int gen)
    {
        try
        {
            string? linea;
            while ((linea = await p.StandardOutput.ReadLineAsync()) != null)
            {
                if (gen != generacion) return;
                Procesar(linea);
            }
        }
        catch (Exception ex) { Registro.Log("Lectura de Claude: " + ex.Message); }
        // El proceso terminó (o lo cerraron). Si había un turno esperando, se le avisa.
        lock (candado)
        {
            if (gen != generacion) return;
            vivo = null;
            var t = turno;
            turno = null;
            t?.Fin.TrySetResult(new Resultado { Texto = "Tuve un problema hablando con Claude. Revisa que tu sesión de Claude Code siga abierta.", Error = true });
        }
        Registro.Log($"El proceso de Claude terminó (código {(p.HasExited ? p.ExitCode : -1)})");
    }

    void Procesar(string linea)
    {
        JsonElement o;
        try { o = JsonDocument.Parse(linea).RootElement; } catch { return; }
        var t = turno;
        switch (o.TryGetProperty("type", out var tipo) ? tipo.GetString() : null)
        {
            case "system":
                if (o.TryGetProperty("session_id", out var s)) sesion = s.GetString();
                break;
            case "assistant":
                if (t == null) break;
                if (o.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Array)
                    foreach (var b in c.EnumerateArray())
                        if (b.TryGetProperty("type", out var bt) && bt.GetString() == "tool_use")
                        {
                            var e = EstadoPara(b.GetProperty("name").GetString() ?? "", b.TryGetProperty("input", out var inp) ? inp : default);
                            if (e != null) t.Estado(e);
                        }
                break;
            case "result":
                if (o.TryGetProperty("session_id", out var s2)) sesion = s2.GetString();
                ultimaVez = DateTime.Now;
                var denegados = new List<Denegado>();
                if (o.TryGetProperty("permission_denials", out var pd) && pd.ValueKind == JsonValueKind.Array)
                    foreach (var d in pd.EnumerateArray())
                        denegados.Add(new Denegado
                        {
                            Herramienta = d.TryGetProperty("tool_name", out var tn) ? tn.GetString() ?? "" : "",
                            Entrada = d.TryGetProperty("tool_input", out var ti) ? ti.Clone() : default,
                        });
                var texto = o.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
                var error = o.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
                if (t == null)
                {
                    Registro.Log($"Respuesta suelta (a algo sumado al final): {texto[..Math.Min(200, texto.Length)]}");
                    if (texto.Length > 0 || denegados.Count > 0) Suelta?.Invoke(new Resultado { Texto = texto, Denegados = denegados, Error = error });
                    break;
                }
                lock (candado) { if (turno == t) turno = null; }
                Registro.Log($"Respuesta ({denegados.Count} permisos pendientes): {texto[..Math.Min(200, texto.Length)]}");
                t.Fin.TrySetResult(new Resultado { Texto = texto, Denegados = denegados, Error = error });
                break;
        }
    }

    public async Task<Resultado> Preguntar(string texto, IEnumerable<string>? permitir, Action<string> estado)
    {
        Cancelar();
        if (RutaClaude() == null) return new Resultado { Texto = "No encuentro Claude Code instalado en este computador.", Error = true };
        Registro.Log($"Orden ({Config.Modelo}): «{texto}»" + (permitir != null ? $" con permiso para {string.Join(", ", permitir)}" : ""));
        estado("Pensando…");
        if (permitir != null) return await PreguntarConPermiso(texto, permitir.ToList(), estado);

        var t = new Turno { Estado = estado, EnVivo = true };
        Process? p;
        lock (candado)
        {
            p = AsegurarVivo();
            if (p == null) return new Resultado { Texto = "No pude arrancar Claude Code.", Error = true };
            turno = t;
        }
        var mensaje = JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = texto } });
        try
        {
            // Con el candado: así no se cruza con un mensaje que se le esté sumando (Agregar).
            var bytes = new UTF8Encoding(false).GetBytes(mensaje + "\n");
            lock (candado) { p.StandardInput.BaseStream.Write(bytes); p.StandardInput.BaseStream.Flush(); }
        }
        catch (Exception ex)
        {
            lock (candado) { if (turno == t) turno = null; Matar(); }
            Registro.Log("No pude mandarle la orden a Claude: " + ex.Message);
            return new Resultado { Texto = "Tuve un problema hablando con Claude. Inténtalo otra vez.", Error = true };
        }
        ultimaVez = DateTime.Now;
        return await t.Fin.Task;
    }

    /// <summary>Acción ya confirmada: proceso aparte con ese permiso exacto, que continúa la misma conversación.</summary>
    async Task<Resultado> PreguntarConPermiso(string texto, List<string> permitir, Action<string> estado)
    {
        var t = new Turno { Estado = estado };
        Process p;
        int gen;
        lock (candado)
        {
            Matar(); // el proceso abierto se rehace después, con la conversación al día
            p = Process.Start(Arranque(RutaClaude()!, false, permitir))!;
            gen = generacion;
            turno = t;
        }
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Registro.Log("claude: " + e.Data); };
        p.BeginErrorReadLine();
        using (var w = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false))) await w.WriteAsync(texto);
        _ = Task.Run(async () =>
        {
            string? linea;
            while ((linea = await p.StandardOutput.ReadLineAsync()) != null) { if (gen != generacion) return; Procesar(linea); }
            lock (candado)
            {
                if (turno == t) { turno = null; t.Fin.TrySetResult(new Resultado { Texto = "Tuve un problema hablando con Claude.", Error = true }); }
            }
        });
        var r = await t.Fin.Task;
        Precalentar();
        return r;
    }

    /// <summary>
    /// Suma un mensaje a la tarea en curso SIN cortarla: Claude Code lo lee en la siguiente pausa entre herramientas
    /// y lo tiene en cuenta en la misma respuesta (probado: una sola respuesta que atiende las dos cosas).
    /// Devuelve false si la tarea corre en un proceso aparte (acción autorizada): entonces se manda después.
    /// </summary>
    public bool Agregar(string texto)
    {
        Process? p;
        lock (candado)
        {
            if (turno is not { EnVivo: true } || vivo is not { HasExited: false }) return false;
            p = vivo;
        }
        try
        {
            var mensaje = JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = texto } });
            var bytes = new UTF8Encoding(false).GetBytes(mensaje + "\n");
            lock (candado) { p.StandardInput.BaseStream.Write(bytes); p.StandardInput.BaseStream.Flush(); }
            Registro.Log($"Sumado a la tarea: «{texto}»");
            return true;
        }
        catch (Exception ex) { Registro.Log("No pude sumar a la tarea: " + ex.Message); return false; }
    }

    public void Cancelar()
    {
        Turno? t;
        lock (candado)
        {
            t = turno;
            if (t == null) return;
            turno = null;
            Matar(); // la única forma segura de cortar un turno a mitad
        }
        t.Fin.TrySetResult(new Resultado { Texto = "", Error = true });
        Registro.Log("Consulta cancelada");
        Precalentar();
    }

    public void NuevaConversacion()
    {
        lock (candado) { sesion = null; turno?.Fin.TrySetResult(new Resultado { Error = true }); turno = null; Matar(); }
        Registro.Log("Conversación nueva");
        Precalentar();
    }

    /// <summary>Cambió el modelo o el nombre: se rehace el proceso abierto (conserva la conversación).</summary>
    public void Reconfigurar()
    {
        lock (candado) { if (turno == null) Matar(); }
        Precalentar();
    }

    public void Cerrar()
    {
        lock (candado) { turno = null; Matar(); }
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
