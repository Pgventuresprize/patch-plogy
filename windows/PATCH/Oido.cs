using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Vosk;

namespace Patch;

/// <summary>
/// Micrófono siempre abierto y reconocimiento de voz en español DENTRO del PC (Vosk). Ningún audio sale del equipo.
/// Los eventos llegan en un hilo de audio: quien los use debe pasarlos al hilo de la interfaz.
/// </summary>
public sealed class Oido : IDisposable
{
    public const string NombreModelo = "vosk-model-small-es-0.42";
    const string UrlModelo = "https://alphacephei.com/vosk/models/" + NombreModelo + ".zip";

    public event Action<string>? Parcial;
    public event Action<Frase>? Final;
    public event Action<float>? Nivel;
    public event Action<string>? Problema;

    Model? modelo;
    VoskRecognizer? reconocedor;
    WaveInEvent? entrada;
    readonly object candado = new();
    volatile bool pausado;
    bool detenido = true;
    string ultimoParcial = "";
    DateTime ultimoNivel = DateTime.MinValue;

    public bool Activo => !detenido;
    public static string CarpetaModelo => Path.Combine(Config.Datos, "modelos", NombreModelo);

    /// <summary>Baja el modelo (38 MB) la primera vez. Informa el avance de 0 a 100.</summary>
    public static async Task AsegurarModelo(Action<int> avance, CancellationToken ct = default)
    {
        if (File.Exists(Path.Combine(CarpetaModelo, "am", "final.mdl"))) return;
        var destino = Path.Combine(Config.Datos, "modelos");
        Directory.CreateDirectory(destino);
        var zip = Path.Combine(destino, NombreModelo + ".zip");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        using (var r = await http.GetAsync(UrlModelo, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            r.EnsureSuccessStatusCode();
            var total = r.Content.Headers.ContentLength ?? 40_000_000;
            await using var origen = await r.Content.ReadAsStreamAsync(ct);
            await using var archivo = File.Create(zip);
            var buf = new byte[81920];
            long leidos = 0;
            int n, ultimo = -1;
            while ((n = await origen.ReadAsync(buf, ct)) > 0)
            {
                await archivo.WriteAsync(buf.AsMemory(0, n), ct);
                leidos += n;
                var pct = (int)(leidos * 100 / total);
                if (pct != ultimo) { ultimo = pct; avance(Math.Min(pct, 99)); }
            }
        }
        ZipFile.ExtractToDirectory(zip, destino, true);
        File.Delete(zip);
        avance(100);
    }

    /// <summary>true si el micrófono está pasando por la cancelación de eco de Windows.</summary>
    public bool ConEco => eco != null;
    CapturaConEco? eco;

    public void Iniciar()
    {
        lock (candado)
        {
            if (!detenido) return;
            Vosk.Vosk.SetLogLevel(-1);
            modelo ??= new Model(CarpetaModelo);
            reconocedor = new VoskRecognizer(modelo, 16000f);
            reconocedor.SetMaxAlternatives(0);
            reconocedor.SetWords(true); // trae la seguridad de cada palabra
            detenido = false;
        }
        string nombre = "";
        try { nombre = WaveInEvent.GetCapabilities(0).ProductName; } catch { }

        // 1) Con cancelación de eco (resta lo que suena por los parlantes). 2) Si Windows no puede, micrófono normal.
        reintentosEco = 0;
        if (Config.Valor("cancelar_eco", "on") != "off")
        {
            try
            {
                eco = AbrirEco();
                Registro.Log($"Micrófono abierto con cancelación de eco: {nombre}");
                return;
            }
            catch (Exception ex) { Registro.Log("Sin cancelación de eco (uso el micrófono normal): " + ex.Message); }
        }
        AbrirNormal();
        Registro.Log($"Micrófono abierto: {nombre}");
    }

    CapturaConEco AbrirEco()
    {
        var c = new CapturaConEco();
        c.Datos += (b, n) => Procesar(b, n);
        c.Fallo += m => { Registro.Log("La cancelación de eco se detuvo: " + m); CambiarANormal(); };
        c.Iniciar();
        return c;
    }

    // Si la cancelación de eco se cae (por ejemplo, al suspender el PC o cambiar los parlantes), se sigue con el
    // micrófono normal y se reintenta a los 30 s. Sin esto quedaría oyéndose a sí mismo hasta reiniciar la app.
    int reintentosEco;

    void VolverAEco()
    {
        if (detenido || eco != null || Config.Valor("cancelar_eco", "on") == "off") return;
        try
        {
            var c = AbrirEco();
            lock (candado)
            {
                if (detenido) { c.Detener(); return; } // cerraron el micrófono mientras tanto
                if (entrada != null)
                {
                    entrada.RecordingStopped -= AlDetenerse; // es un cambio a propósito, no una desconexión
                    try { entrada.StopRecording(); } catch { }
                    entrada.Dispose();
                    entrada = null;
                }
            }
            eco = c;
            Registro.Log("Cancelación de eco recuperada");
        }
        catch (Exception ex) { Registro.Log("La cancelación de eco sigue sin funcionar (sigo con el micrófono normal): " + ex.Message); }
    }

    void AbrirNormal()
    {
        lock (candado)
        {
            entrada = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100, DeviceNumber = 0 };
            entrada.DataAvailable += (_, a) => Procesar(a.Buffer, a.BytesRecorded);
            entrada.RecordingStopped += AlDetenerse;
            entrada.StartRecording();
        }
    }

    void CambiarANormal()
    {
        var e = eco;
        eco = null;
        Task.Run(async () =>
        {
            try { e?.Detener(); } catch { }
            if (detenido) return;
            try { AbrirNormal(); Registro.Log("Sigo con el micrófono normal"); }
            catch (Exception ex) { Problema?.Invoke("No pude abrir el micrófono: " + ex.Message); return; }
            if (++reintentosEco > 5) return; // si se cae una y otra vez, se queda con el normal
            await Task.Delay(30_000);
            VolverAEco();
        });
    }

    /// <summary>Modo de prueba: reconocimiento listo pero sin abrir el micrófono; el audio llega por Inyectar.</summary>
    public void IniciarSinMicrofono()
    {
        lock (candado)
        {
            Vosk.Vosk.SetLogLevel(-1);
            modelo ??= new Model(CarpetaModelo);
            reconocedor ??= new VoskRecognizer(modelo, 16000f);
            detenido = false;
        }
        Registro.Log("Oído en modo de prueba (sin micrófono)");
    }

    /// <summary>Modo de prueba: pasa un WAV de 16 kHz mono por el oído a velocidad real, más un segundo de silencio.</summary>
    public async Task Inyectar(string wav)
    {
        var datos = File.ReadAllBytes(wav);
        var pcm = new byte[datos.Length - 44 + 32000];
        Array.Copy(datos, 44, pcm, 0, datos.Length - 44);
        for (int i = 0; i < pcm.Length; i += 3200)
        {
            var n = Math.Min(3200, pcm.Length - i);
            var trozo = new byte[n];
            Array.Copy(pcm, i, trozo, 0, n);
            Procesar(trozo, n);
            await Task.Delay(100);
        }
    }

    public void Detener()
    {
        lock (candado)
        {
            if (detenido) return;
            detenido = true;
            try { entrada?.StopRecording(); } catch { }
            entrada?.Dispose();
            entrada = null;
        }
        var e = eco;
        eco = null;
        try { e?.Detener(); } catch { }
        lock (candado)
        {
            reconocedor?.Dispose();
            reconocedor = null;
        }
        Registro.Log("Micrófono cerrado");
    }

    /// <summary>Mientras habla PATCH, el oído descarta el audio (para no escucharse a sí mismo).</summary>
    public void Pausar(bool si)
    {
        pausado = si;
        if (si) lock (candado) { reconocedor?.Reset(); ultimoParcial = ""; OlvidarAudio(); }
    }

    /// <summary>Olvida lo que llevaba oído (para empezar una frase nueva).</summary>
    public void Reiniciar()
    {
        lock (candado) { reconocedor?.Reset(); ultimoParcial = ""; OlvidarAudio(); }
    }

    // Volumen de la frase en curso: el pico del nivel (suavizado) desde que empezó a hablar.
    double picoFrase;

    // Diagnóstico: a los 5 s de abrir el micrófono se anota si llega audio (y cuánto).
    DateTime? inicioDiagnostico;
    long bytesDiagnostico;
    double sumaNivelDiagnostico;
    int bloquesDiagnostico;

    void Procesar(byte[] buffer, int bytes)
    {
        double suma = 0;
        int muestras = bytes / 2;
        for (int i = 0; i < bytes - 1; i += 2) { var v = BitConverter.ToInt16(buffer, i) / 32768.0; suma += v * v; }
        var rms = Math.Sqrt(suma / Math.Max(1, muestras));
        if (inicioDiagnostico == null) inicioDiagnostico = DateTime.Now;
        if (bloquesDiagnostico >= 0)
        {
            bytesDiagnostico += bytes; sumaNivelDiagnostico += rms; bloquesDiagnostico++;
            if ((DateTime.Now - inicioDiagnostico.Value).TotalSeconds > 5)
            {
                Registro.Log($"Audio llegando{(eco != null ? " (con cancelación de eco)" : "")}: {bytesDiagnostico / 32000.0:0.0} s en 5 s, nivel medio {sumaNivelDiagnostico / bloquesDiagnostico:0.0000}");
                bloquesDiagnostico = -1;
            }
        }
        // Nivel para la animación (aunque esté en pausa)
        if ((DateTime.Now - ultimoNivel).TotalMilliseconds > 60)
        {
            ultimoNivel = DateTime.Now;
            Nivel?.Invoke((float)rms);
        }
        if (pausado) return;
        Frase? final = null;
        string? parcial = null;
        lock (candado)
        {
            if (reconocedor == null) return;
            picoFrase = Math.Max(picoFrase * 0.995, rms);
            GuardarAudio(buffer, bytes);
            if (reconocedor.AcceptWaveform(buffer, bytes))
            {
                final = LeerFrase(reconocedor.Result(), picoFrase) with { Audio = AudioDeLaFrase() };
                OlvidarAudio();
            }
            else
            {
                var p = Leer(reconocedor.PartialResult(), "partial");
                if (p != ultimoParcial)
                {
                    ultimoParcial = p;
                    parcial = p;
                    // Vosk muestra la primera palabra un poco después de que empezó a sonar: se guarda un margen antes.
                    if (p.Length > 0 && inicioVoz < 0) inicioVoz = Math.Max(0, fraseAudio.Length - bytes - MargenAntes);
                }
            }
        }
        if (final != null)
        {
            ultimoParcial = "";
            picoFrase = 0;
            if (final.Texto.Length > 0) { UltimaFrase = final; Final?.Invoke(final); }
        }
        else if (!string.IsNullOrEmpty(parcial)) Parcial?.Invoke(parcial);
    }

    // ------------------------------------------------------------------ Audio de la frase (para Whisper)

    // El audio desde la última frase completa, hasta 30 s. Se marca dónde empezó a hablar para no mandar el silencio previo.
    readonly MemoryStream fraseAudio = new();
    long inicioVoz = -1;
    const int BytesPorSegundo = 32000;                 // 16 kHz, 16 bits, mono
    const int MaxAudio = 30 * BytesPorSegundo;
    const int MargenAntes = 1200 * BytesPorSegundo / 1000; // 1,2 s

    void GuardarAudio(byte[] buffer, int bytes)
    {
        fraseAudio.Write(buffer, 0, bytes);
        if (fraseAudio.Length <= MaxAudio) return;
        // Solo se conservan los últimos 20 s.
        var todo = fraseAudio.ToArray();
        var quitar = todo.Length - 20 * BytesPorSegundo;
        fraseAudio.SetLength(0);
        fraseAudio.Write(todo, quitar, todo.Length - quitar);
        inicioVoz = inicioVoz < 0 ? -1 : Math.Max(0, inicioVoz - quitar);
    }

    byte[] AudioDeLaFrase()
    {
        var todo = fraseAudio.ToArray();
        // Sin parcial (una frase muy corta), se toman los últimos 6 s.
        var desde = inicioVoz >= 0 ? inicioVoz : Math.Max(0, todo.Length - 6 * BytesPorSegundo);
        desde -= desde % 2;
        return todo[(int)desde..];
    }

    void OlvidarAudio() { fraseAudio.SetLength(0); inicioVoz = -1; }

    /// <summary>El audio de lo que se lleva oído de la frase en curso (cuando se envía sin esperar el final de Vosk).</summary>
    public byte[] AudioEnCurso() { lock (candado) return AudioDeLaFrase(); }

    /// <summary>Lo que se oyó, qué tan seguro está el reconocedor (0 a 1), qué tan fuerte sonó y su audio.</summary>
    public sealed record Frase(string Texto, double Seguridad, double Volumen, byte[]? Audio = null);

    /// <summary>La última frase completa (con su seguridad y volumen), para decidir si iba dirigida a PATCH.</summary>
    public Frase? UltimaFrase { get; private set; }

    static Frase LeerFrase(string json, double volumen)
    {
        try
        {
            var r = JsonDocument.Parse(json).RootElement;
            var texto = r.GetProperty("text").GetString() ?? "";
            double seguridad = 1;
            if (r.TryGetProperty("result", out var palabras) && palabras.ValueKind == JsonValueKind.Array && palabras.GetArrayLength() > 0)
            {
                double s = 0; int n = 0;
                foreach (var w in palabras.EnumerateArray()) if (w.TryGetProperty("conf", out var c)) { s += c.GetDouble(); n++; }
                if (n > 0) seguridad = s / n;
            }
            return new Frase(texto, seguridad, volumen);
        }
        catch { return new Frase("", 0, volumen); }
    }

    void AlDetenerse(object? s, StoppedEventArgs e)
    {
        if (detenido) return;
        // Se desconectó el micrófono o cambió el dispositivo: se reintenta.
        Registro.Log($"El micrófono se detuvo: {e.Exception?.Message}");
        Problema?.Invoke("Se perdió el micrófono; lo reintento.");
        detenido = true;
        try { entrada?.Dispose(); } catch { }
        entrada = null;
        Task.Delay(2000).ContinueWith(_ => { try { Iniciar(); } catch (Exception ex) { Problema?.Invoke("No pude abrir el micrófono: " + ex.Message); } });
    }

    static string Leer(string json, string campo)
    {
        try { return JsonDocument.Parse(json).RootElement.GetProperty(campo).GetString() ?? ""; } catch { return ""; }
    }

    public void Dispose()
    {
        Detener();
        modelo?.Dispose();
    }
}
