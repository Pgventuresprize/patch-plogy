using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Patch;

/// <summary>Lo que el controlador necesita de un cerebro: Claude Code (Cerebro) o Codex con ChatGPT (CerebroCodex).</summary>
public interface ICerebro
{
    event Action<Resultado>? Suelta;
    bool Ocupado { get; }
    void Recordar(IEnumerable<Denegado> ds);
    void Precalentar();
    Task<Resultado> Preguntar(string texto, IEnumerable<string>? permitir, Action<string> estado);
    bool Agregar(string texto);
    void Cancelar();
    void NuevaConversacion();
    void Reconfigurar();
    void Cerrar();
}

/// <summary>
/// Cerebro con la cuenta de ChatGPT, a través de Codex (codex exec), sin ventana.
/// Cada orden es un proceso: la primera abre una conversación y las siguientes la retoman (codex exec resume).
/// Corre en modo seguro (sandbox workspace-write): lee todo el computador, busca en internet y solo escribe dentro de
/// la carpeta de PATCH. Codex no puede pedir permisos a mitad desde aquí, así que mover, ordenar o borrar archivos del
/// usuario y enviar correos quedan para Claude, que sí pasa por las confirmaciones de la app.
/// </summary>
public sealed class CerebroCodex : ICerebro
{
    readonly object candado = new();
    Process? actual;
    TaskCompletionSource<Resultado>? fin;
    string? hilo;
    DateTime ultimaVez = DateTime.MinValue;
    int generacion;

    // Codex no contesta aparte: nada llega sin turno esperando.
    public event Action<Resultado>? Suelta { add { } remove { } }
    public bool Ocupado { get { lock (candado) return actual != null; } }
    public void Recordar(IEnumerable<Denegado> ds) { }
    public void Precalentar() { }

    /// <summary>codex.exe en el PATH, el de npm, o el que trae la app de escritorio de Codex.</summary>
    public static string? RutaCodex()
    {
        if (Cuentas.EnPath("codex.exe") is { } exe) return exe;
        if (Cuentas.EnPath("codex.cmd") is { } cmd) return cmd;
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd");
        if (File.Exists(npm)) return npm;
        try
        {
            var app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
            if (Directory.Exists(app))
                return Directory.GetFiles(app, "codex.exe", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        catch { }
        return null;
    }

    static string Indicacion() =>
        "[Instrucciones de la app PATCH, no las repitas] " +
        $"Respondes desde la app de PATCH en Windows. Te llamas {Config.Asistente}. Sigue AGENTS.md de esta carpeta. " +
        "El usuario te habla por voz o te escribe en la barra lateral y no ve ninguna terminal: tu respuesta se lee en voz alta " +
        "y se muestra en una ventanita, así que responde en 1 a 4 frases, sin tablas ni bloques de código. " +
        "Trabajas en segundo plano: nunca uses el mouse, el teclado ni el navegador del usuario. " +
        "Estás conectado con la cuenta de ChatGPT del usuario y corres en modo seguro: puedes buscar en internet, buscar y leer " +
        "cualquier archivo del computador, y escribir solo dentro de la carpeta de PATCH (por ejemplo, notas en notas\\). " +
        "No puedes mover, ordenar, renombrar ni borrar archivos del usuario, ni enviar correos: si te lo piden, dilo en una frase, " +
        "ofrece dejar el plan en una nota y explica que esas acciones necesitan conectar Claude en los ajustes de PATCH. " +
        "Para buscar archivos usa .\\scripts\\buscar.ps1 y para leer Word, Excel o PowerPoint, .\\scripts\\leer.ps1. " +
        $"Rutas de este equipo: carpeta del usuario {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}, " +
        $"Escritorio {Environment.GetFolderPath(Environment.SpecialFolder.Desktop)}, Documentos {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}. " +
        "[Fin de las instrucciones]";

    public async Task<Resultado> Preguntar(string texto, IEnumerable<string>? permitir, Action<string> estado)
    {
        Cancelar();
        var codex = RutaCodex();
        if (codex == null) return new Resultado { Texto = "No encuentro Codex en este computador. Ábreme los ajustes y conecta tu cuenta de ChatGPT.", Error = true };
        if ((DateTime.Now - ultimaVez).TotalMinutes > 15) hilo = null; // tras 15 minutos sin hablar, conversación nueva
        Registro.Log($"Orden (ChatGPT): «{texto}»");
        estado("Pensando…");

        var psi = new ProcessStartInfo(codex)
        {
            WorkingDirectory = Config.Carpeta,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        var args = new List<string> { "exec", "--json", "--skip-git-repo-check", "-s", "workspace-write", "-C", Config.Carpeta, "-c", "web_search=\"live\"" };
        var modelo = Config.Valor("modelo_chatgpt");
        if (modelo.Length > 0) args.AddRange(new[] { "-m", modelo });
        if (hilo != null) args.AddRange(new[] { "resume", hilo });
        args.Add("-");
        foreach (var a in args) psi.ArgumentList.Add(a);
        Cerebro.LimpiarEntorno(psi.Environment);
        psi.Environment["PATCH_APP"] = "1";

        Process p;
        var mio = new TaskCompletionSource<Resultado>(TaskCreationOptions.RunContinuationsAsynchronously);
        int gen;
        try
        {
            lock (candado)
            {
                p = Process.Start(psi)!;
                actual = p; fin = mio; gen = ++generacion;
            }
        }
        catch (Exception ex)
        {
            Registro.Log("No pude arrancar Codex: " + ex.Message);
            return new Resultado { Texto = "No pude arrancar Codex.", Error = true };
        }
        p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data) && !e.Data.Contains("warning", StringComparison.OrdinalIgnoreCase)) Registro.Log("codex: " + e.Data); };
        p.BeginErrorReadLine();
        var primera = hilo == null ? Indicacion() + "\n\n" + texto : texto;
        try { using (var w = new StreamWriter(p.StandardInput.BaseStream, new UTF8Encoding(false))) await w.WriteAsync(primera); }
        catch (Exception ex) { Registro.Log("No pude mandarle la orden a Codex: " + ex.Message); }

        _ = Task.Run(async () =>
        {
            string? ultimo = null, error = null;
            try
            {
                string? linea;
                while ((linea = await p.StandardOutput.ReadLineAsync()) != null)
                {
                    if (gen != generacion) return;
                    JsonElement o;
                    try { o = JsonDocument.Parse(linea).RootElement; } catch { continue; }
                    switch (o.TryGetProperty("type", out var t) ? t.GetString() : null)
                    {
                        case "thread.started":
                            if (o.TryGetProperty("thread_id", out var id)) hilo = id.GetString();
                            break;
                        case "item.started":
                            if (o.TryGetProperty("item", out var ini) && EstadoPara(ini) is { } e) estado(e);
                            break;
                        case "item.completed":
                            if (o.TryGetProperty("item", out var it) && it.TryGetProperty("type", out var tt) && tt.GetString() == "agent_message"
                                && it.TryGetProperty("text", out var tx)) ultimo = tx.GetString();
                            break;
                        case "turn.failed":
                        case "error":
                            error = o.TryGetProperty("message", out var mm) ? mm.GetString()
                                : o.TryGetProperty("error", out var er) && er.TryGetProperty("message", out var em) ? em.GetString() : "error";
                            break;
                    }
                }
                await p.WaitForExitAsync();
            }
            catch (Exception ex) { error = ex.Message; }
            lock (candado)
            {
                if (gen != generacion) return;
                actual = null; fin = null;
            }
            ultimaVez = DateTime.Now;
            if (!string.IsNullOrWhiteSpace(ultimo))
            {
                Registro.Log($"Respuesta (ChatGPT): {ultimo[..Math.Min(200, ultimo.Length)]}");
                mio.TrySetResult(new Resultado { Texto = ultimo });
            }
            else
            {
                Registro.Log($"Codex terminó sin respuesta (código {(p.HasExited ? p.ExitCode : -1)}): {error}");
                var sinSesion = (error ?? "").Contains("login", StringComparison.OrdinalIgnoreCase) || (error ?? "").Contains("auth", StringComparison.OrdinalIgnoreCase);
                mio.TrySetResult(new Resultado
                {
                    Texto = sinSesion ? "Tu sesión de ChatGPT no está abierta. Ábreme los ajustes y vuelve a iniciar sesión." : "Tuve un problema hablando con ChatGPT. Inténtalo otra vez.",
                    Error = true,
                });
            }
        });
        return await mio.Task;
    }

    /// <summary>Codex no recibe mensajes a mitad de una orden: lo que se diga se manda con la siguiente.</summary>
    public bool Agregar(string texto) => false;

    public void Cancelar()
    {
        Process? p; TaskCompletionSource<Resultado>? f;
        lock (candado)
        {
            p = actual; f = fin;
            if (p == null) return;
            actual = null; fin = null; generacion++;
        }
        try { if (!p.HasExited) p.Kill(true); } catch { }
        f?.TrySetResult(new Resultado { Texto = "", Error = true });
        Registro.Log("Consulta cancelada (ChatGPT)");
    }

    public void NuevaConversacion()
    {
        Cancelar();
        hilo = null;
        Registro.Log("Conversación nueva (ChatGPT)");
    }

    public void Reconfigurar() { if (!Ocupado) hilo = null; }

    public void Cerrar() => Cancelar();

    static string? EstadoPara(JsonElement item)
    {
        var tipo = item.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (tipo)
        {
            case "web_search": return "Buscando en internet…";
            case "file_change": return "Escribiendo…";
            case "reasoning": return "Pensando…";
            case "command_execution":
                var c = item.TryGetProperty("command", out var cmd) ? cmd.GetString() ?? "" : "";
                if (c.Contains("buscar.")) return "Buscando en tu computador…";
                if (c.Contains("leer.")) return "Leyendo el documento…";
                if (c.Contains("Get-ChildItem") || c.Contains("Get-Content")) return "Revisando archivos…";
                if (c.Contains("Start-Process")) return "Abriendo…";
                return "Trabajando…";
            default: return null;
        }
    }
}
