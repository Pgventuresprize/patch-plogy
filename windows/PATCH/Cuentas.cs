using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Patch;

/// <summary>
/// Las dos formas de darle cerebro a PATCH sin claves de API: iniciar sesión con la cuenta de Claude (Claude Code)
/// o con la de ChatGPT (Codex). Todo pasa en este computador: PATCH solo pregunta a cada programa si hay sesión
/// y abre su propio inicio de sesión, que se completa en el navegador. Nunca ve ni guarda contraseñas.
/// </summary>
public static class Cuentas
{
    public sealed record Estado(bool Instalado, bool Conectado, string Detalle);

    /// <summary>"claude" o "chatgpt".</summary>
    public static bool Valido(string p) => p is "claude" or "chatgpt";

    public static async Task<Estado> Consultar(string proveedor) => proveedor == "chatgpt" ? await ChatGPT() : await Claude();

    static async Task<Estado> Claude()
    {
        var ruta = Cerebro.RutaClaude();
        if (ruta == null) return new(false, false, "No está instalado");
        var (codigo, salida) = await Correr(ruta, "auth", "status", "--json");
        try
        {
            var o = JsonDocument.Parse(salida[salida.IndexOf('{')..]).RootElement;
            var dentro = o.TryGetProperty("loggedIn", out var l) && l.ValueKind == JsonValueKind.True;
            if (!dentro) return new(true, false, "Sin sesión");
            var plan = o.TryGetProperty("subscriptionType", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;
            var metodo = o.TryGetProperty("authMethod", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            var detalle = !string.IsNullOrEmpty(plan) ? "Plan " + char.ToUpper(plan![0]) + plan[1..]
                : metodo == "claude.ai" ? "Con tu cuenta de Claude" : "Con Anthropic Console";
            return new(true, true, detalle);
        }
        catch { return new(true, codigo == 0 && salida.Contains("\"loggedIn\": true"), codigo == 0 ? "Sin sesión" : "No pude revisar la sesión"); }
    }

    static async Task<Estado> ChatGPT()
    {
        var ruta = CerebroCodex.RutaCodex();
        if (ruta == null) return new(false, false, "No está instalado");
        var (codigo, salida) = await Correr(ruta, "login", "status");
        // Codex escribe el estado en la salida de errores: "Logged in using ChatGPT" / "Not logged in".
        var dentro = codigo == 0 && salida.Contains("Logged in", StringComparison.OrdinalIgnoreCase)
            && !salida.Contains("Not logged in", StringComparison.OrdinalIgnoreCase);
        if (!dentro) return new(true, false, "Sin sesión");
        return new(true, true, salida.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) ? "Con tu cuenta de ChatGPT" : "Con clave de API");
    }

    /// <summary>
    /// Abre el inicio de sesión del programa en una ventanita de consola: el programa abre el navegador,
    /// la persona entra con su cuenta y la ventanita se cierra sola al terminar.
    /// </summary>
    public static void IniciarSesion(string proveedor)
    {
        var ruta = proveedor == "chatgpt" ? CerebroCodex.RutaCodex() : Cerebro.RutaClaude();
        if (ruta == null) { Instalar(proveedor); return; }
        var args = proveedor == "chatgpt" ? new[] { "login" } : new[] { "auth", "login", "--claudeai" };
        Registro.Log($"Inicio de sesión abierto ({proveedor})");
        Consola(ruta, args);
    }

    /// <summary>Instala el programa con su instalador oficial (en una consola visible) o abre su página.</summary>
    public static void Instalar(string proveedor)
    {
        Registro.Log($"Instalación pedida ({proveedor})");
        if (proveedor == "claude")
        {
            // Instalador oficial de Claude Code para Windows.
            Consola("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-NoExit", "-Command",
                "Write-Host 'Instalando Claude Code (instalador oficial de Anthropic)...'; irm https://claude.ai/install.ps1 | iex; Write-Host ''; Write-Host 'Listo. Vuelve a PATCH y toca Iniciar sesion.'");
            return;
        }
        if (EnPath("npm.cmd") is { } npm)
        {
            Consola("powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-NoExit", "-Command",
                $"Write-Host 'Instalando Codex (paquete oficial de OpenAI)...'; & '{npm}' install -g @openai/codex; Write-Host ''; Write-Host 'Listo. Vuelve a PATCH y toca Iniciar sesion.'");
            return;
        }
        try { Process.Start(new ProcessStartInfo("https://github.com/openai/codex") { UseShellExecute = true }); } catch { }
    }

    static void Consola(string exe, params string[] args)
    {
        try
        {
            // PATCH no tiene consola propia: con CreateNoWindow=false Windows le abre una ventanita al programa.
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = Config.Carpeta };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Cerebro.LimpiarEntorno(psi.Environment);
            Process.Start(psi);
        }
        catch (Exception ex) { Registro.Log($"No pude abrir {Path.GetFileName(exe)}: {ex.Message}"); }
    }

    /// <summary>Corre un comando sin ventana y devuelve el código y toda la salida (normal y de errores).</summary>
    static async Task<(int, string)> Correr(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Config.Carpeta,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Cerebro.LimpiarEntorno(psi.Environment);
            using var p = Process.Start(psi)!;
            var salida = p.StandardOutput.ReadToEndAsync();
            var errores = p.StandardError.ReadToEndAsync();
            await Task.WhenAny(p.WaitForExitAsync(), Task.Delay(25000));
            if (!p.HasExited) { try { p.Kill(true); } catch { } return (-1, ""); }
            return (p.ExitCode, await salida + "\n" + await errores);
        }
        catch (Exception ex) { Registro.Log($"No pude correr {Path.GetFileName(exe)}: {ex.Message}"); return (-1, ""); }
    }

    public static string? EnPath(string archivo)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { var c = Path.Combine(dir.Trim(), archivo); if (File.Exists(c)) return c; } catch { }
        }
        return null;
    }
}
