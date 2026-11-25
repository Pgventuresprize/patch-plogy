using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Patch;

/// <summary>
/// Habla con la voz neuronal de Edge (servidor de Node que queda abierto: scripts/voz/servidor.mjs).
/// Si no hay Node o falla, usa la voz de Windows (SAPI).
/// </summary>
public sealed class Boca : IDisposable
{
    Process? servidor;
    StreamWriter? alServidor;
    readonly ConcurrentDictionary<int, TaskCompletionSource<string?>> pendientes = new();
    int siguienteId;
    CancellationTokenSource? turno;
    WaveOutEvent? salida;
    System.Speech.Synthesis.SpeechSynthesizer? sapi;
    readonly object candado = new();

    public bool Hablando { get; private set; }

    string? RutaNode()
    {
        var modulo = Path.Combine(Config.Carpeta, "scripts", "voz", "node_modules", "msedge-tts");
        if (!Directory.Exists(modulo)) return null;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            try { var n = Path.Combine(dir.Trim(), "node.exe"); if (File.Exists(n)) return n; } catch { }
        }
        return null;
    }

    bool AsegurarServidor()
    {
        if (servidor is { HasExited: false }) return true;
        var node = RutaNode();
        if (node == null) return false;
        try
        {
            var psi = new ProcessStartInfo(node)
            {
                WorkingDirectory = Path.Combine(Config.Carpeta, "scripts", "voz"),
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add("servidor.mjs");
            servidor = Process.Start(psi)!;
            alServidor = new StreamWriter(servidor.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
            servidor.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                try
                {
                    var r = JsonDocument.Parse(e.Data).RootElement;
                    var id = r.GetProperty("id").GetInt32();
                    var ok = r.GetProperty("ok").GetBoolean();
                    if (pendientes.TryRemove(id, out var tcs)) tcs.TrySetResult(ok ? r.GetProperty("archivo").GetString() : null);
                    if (!ok) Registro.Log("Voz Edge falló: " + r.GetProperty("error").GetString());
                }
                catch { }
            };
            servidor.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Registro.Log("servidor de voz: " + e.Data); };
            servidor.BeginOutputReadLine();
            servidor.BeginErrorReadLine();
            Registro.Log("Servidor de voz listo");
            return true;
        }
        catch (Exception ex) { Registro.Log("No pude abrir el servidor de voz: " + ex.Message); return false; }
    }

    Task<string?> PedirAudio(string texto, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref siguienteId);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendientes[id] = tcs;
        var carpeta = Path.Combine(Path.GetTempPath(), "patch-voz", id.ToString());
        var pedido = JsonSerializer.Serialize(new { id, texto, voz = Config.VozEdge, carpeta });
        lock (candado) alServidor!.WriteLine(pedido);
        ct.Register(() => { if (pendientes.TryRemove(id, out var t)) t.TrySetResult(null); });
        _ = Task.Delay(15000).ContinueWith(_ => { if (pendientes.TryRemove(id, out var t)) t.TrySetResult(null); });
        return tcs.Task;
    }

    /// <summary>Precalienta el servidor (Node + conexión) para que la primera respuesta no espere.</summary>
    public void Preparar() => Task.Run(() => AsegurarServidor());

    /// <summary>Dice el texto. Al terminar (o si se corta) llama a fin(true si terminó completo).</summary>
    public void Decir(string texto, Action<bool>? fin = null)
    {
        Callar();
        var limpio = Texto.ParaVoz(texto, Config.MaxCaracteres);
        if (string.IsNullOrWhiteSpace(limpio)) { fin?.Invoke(true); return; }
        var cts = new CancellationTokenSource();
        turno = cts;
        Hablando = true;
        Task.Run(async () =>
        {
            bool completo = false;
            try { completo = await HablarAsync(limpio, cts.Token); }
            catch (Exception ex) { Registro.Log("Error al hablar: " + ex.Message); }
            finally
            {
                if (turno == cts) Hablando = false;
                if (!cts.IsCancellationRequested) fin?.Invoke(completo);
            }
        });
    }

    async Task<bool> HablarAsync(string texto, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        if (AsegurarServidor())
        {
            var partes = Texto.DividirFrases(texto);
            // Se piden todas de una vez: la segunda se genera mientras suena la primera.
            var audios = partes.Select(p => PedirAudio(p, ct)).ToArray();
            var primero = await audios[0];
            if (primero != null)
            {
                Registro.Log($"Voz lista en {sw.ElapsedMilliseconds} ms");
                if (!await Reproducir(primero, ct)) return false;
                for (int i = 1; i < audios.Length; i++)
                {
                    var a = await audios[i];
                    if (a == null || !await Reproducir(a, ct)) return false;
                }
                return true;
            }
            if (ct.IsCancellationRequested) return false;
        }
        return await HablarSistema(texto, ct);
    }

    async Task<bool> Reproducir(string mp3, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        var listo = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        MediaFoundationReader lector;
        try { lector = new MediaFoundationReader(mp3); }
        catch (Exception ex) { Registro.Log("No pude abrir el audio: " + ex.Message); return false; }
        var w = new WaveOutEvent();
        lock (candado) salida = w;
        w.PlaybackStopped += (_, _) => listo.TrySetResult(!ct.IsCancellationRequested);
        w.Init(lector);
        w.Play();
        using (ct.Register(() => { try { w.Stop(); } catch { } }))
        {
            var ok = await listo.Task;
            w.Dispose();
            lector.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(mp3)!, true); } catch { }
            return ok;
        }
    }

    Task<bool> HablarSistema(string texto, CancellationToken ct)
    {
        var listo = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            sapi ??= CrearSapi();
            void alTerminar(object? s, System.Speech.Synthesis.SpeakCompletedEventArgs e)
            {
                sapi!.SpeakCompleted -= alTerminar;
                listo.TrySetResult(!e.Cancelled && !ct.IsCancellationRequested);
            }
            sapi.SpeakCompleted += alTerminar;
            sapi.SpeakAsync(texto);
            ct.Register(() => { try { sapi.SpeakAsyncCancelAll(); } catch { } });
        }
        catch (Exception ex) { Registro.Log("Voz de Windows falló: " + ex.Message); listo.TrySetResult(false); }
        return listo.Task;
    }

    static System.Speech.Synthesis.SpeechSynthesizer CrearSapi()
    {
        var s = new System.Speech.Synthesis.SpeechSynthesizer();
        s.SetOutputToDefaultAudioDevice();
        var es = s.GetInstalledVoices().Where(v => v.Enabled && v.VoiceInfo.Culture.Name.StartsWith("es"))
            .OrderBy(v => v.VoiceInfo.Culture.Name == "es-CO" ? 0 : v.VoiceInfo.Culture.Name == "es-MX" ? 1 : 2).FirstOrDefault();
        if (es != null) s.SelectVoice(es.VoiceInfo.Name);
        return s;
    }

    /// <summary>Corta la voz al instante.</summary>
    public void Callar()
    {
        var t = turno;
        turno = null;
        Hablando = false;
        try { t?.Cancel(); } catch { }
        lock (candado) { try { salida?.Stop(); } catch { } }
        try { sapi?.SpeakAsyncCancelAll(); } catch { }
    }

    public void Dispose()
    {
        Callar();
        try { alServidor?.Close(); } catch { }
        try { if (servidor is { HasExited: false }) servidor.Kill(); } catch { }
        sapi?.Dispose();
    }
}
