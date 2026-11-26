using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Patch;

/// <summary>
/// Oído fino: Vosk sigue escuchando siempre y detecta el nombre; cuando hay una orden, su audio pasa por Whisper
/// (whisper-large-v3-turbo), que conoce palabras que Vosk no ("Patch", "MCP", nombres de clientes) y confirma que de
/// verdad dijeron el nombre. Hay dos motores con el mismo modelo, y la misma pista:
/// - Local: whisper.cpp vía Whisper.net, en la tarjeta gráfica (Vulkan) o en el procesador. Ningún audio sale del PC.
/// - Groq: en la nube, con la clave gratis de cada usuario. Para equipos lentos (sin tarjeta gráfica).
/// En modo automático se usa el local si responde rápido; si no, Groq (si hay clave). Si Groq falla, el local.
/// Medido en un Ryzen 7 260 + RTX 5060 con frases de 4 a 6 s: ~0,5 s en la NVIDIA, ~1,5 s en la Radeon integrada,
/// ~3,5 s solo con el procesador (modelo small).
/// </summary>
public sealed class Transcriptor : IDisposable
{
    /// <summary>Con tarjeta gráfica (Vulkan): el más preciso. 574 MB.</summary>
    const string ModeloGpu = "ggml-large-v3-turbo-q5_0.bin";
    /// <summary>Solo procesador: más liviano y rápido, algo menos preciso. 190 MB.</summary>
    const string ModeloCpu = "ggml-small-q5_1.bin";
    const string UrlModelos = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";
    const string UrlGroq = "https://api.groq.com/openai/v1/audio/transcriptions";
    /// <summary>En modo automático, si el PC tarda más que esto por frase, se prefiere Groq (si hay clave).</summary>
    const long MsLocalAceptable = 1000;

    public static string Carpeta => Path.Combine(Config.Datos, "modelos", "whisper");
    public static bool Activo => Config.Valor("whisper", "on") != "off";
    /// <summary>oido= en patch-config.txt: auto (por defecto), local o groq.</summary>
    public static string Modo => Config.Valor("oido", "auto") is "local" or "groq" ? Config.Valor("oido") : "auto";

    // Motor local
    WhisperFactory? fabrica;
    WhisperProcessor? proc;
    string promptUsado = "";
    readonly SemaphoreSlim turno = new(1, 1);
    /// <summary>Lo que tarda el local con órdenes reales (promedio). null hasta la primera orden.</summary>
    long? msLocal;
    bool localDedicada;
    string dondeLocal = "";

    // Motor en la nube (Groq)
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    string? clave;
    bool groqOk, usarGroq;
    long? msGroq;
    string? errorGroq;

    readonly SemaphoreSlim preparando = new(1, 1);

    public bool Listo => fabrica != null || (usarGroq && groqOk);

    /// <summary>Para el registro.</summary>
    public string Descripcion => usarGroq ? $"Groq en la nube ({msGroq} ms)" : fabrica != null ? $"{dondeLocal} ({(msLocal is long ml ? ml + " ms" : "sin medir")})" : "sin oído fino";

    /// <summary>Para los ajustes, en palabras del usuario.</summary>
    public string Estado
    {
        get
        {
            if (!Activo) return "";
            var error = errorGroq != null && Modo != "local" ? " " + errorGroq : "";
            if (usarGroq) return $"Ahora usa Groq en la nube{Segundos(msGroq)}.";
            if (fabrica != null) return $"Ahora usa {dondeLocal}{Segundos(msLocal)}.{error}";
            return "Preparándose…" + error;
        }
    }

    static string Segundos(long? ms) => ms == null ? "" : $": unos {ms.Value / 1000.0:0.0} s por frase".Replace('.', ',');

    /// <summary>
    /// Elige el motor según el modo, la tarjeta gráfica y la clave de Groq. Baja el modelo local si hace falta (una vez)
    /// y deja caliente lo elegido. Se puede volver a llamar cuando cambian el modo o la clave.
    /// </summary>
    public async Task Preparar(Action<string> aviso, CancellationToken ct = default)
    {
        await preparando.WaitAsync(ct);
        try
        {
            clave = ClaveGroq.Leer();
            var modo = Modo;
            var (gpu, nombreGpu, dedicada) = ElegirGpu();
            localDedicada = gpu >= 0 && dedicada;
            // El local se carga si se pidió, si no hay clave de Groq, o si hay tarjeta gráfica (rápido y sirve sin internet).
            // Sin tarjeta y con clave no se baja: Groq es mucho más rápido que el procesador.
            var cargarLocal = modo == "local" || clave == null || gpu >= 0;
            if (cargarLocal && fabrica == null) await PrepararLocal(gpu, nombreGpu, aviso, ct);
            groqOk = false;
            msGroq = null;
            errorGroq = null;
            if (clave != null && modo != "local")
            {
                // Una frase de silencio: comprueba la clave, mide el tiempo y deja abierta la conexión.
                groqOk = await TranscribirGroq(new byte[48000], ct) != null;
            }
            // Groq no sirve (clave mala, sin internet) y no había local: se carga el local de respaldo.
            if (!groqOk && fabrica == null) await PrepararLocal(gpu, nombreGpu, aviso, ct);
            Decidir();
            Registro.Log($"Oído fino: modo {modo}, local {(fabrica != null ? dondeLocal : "no")}, " +
                         $"Groq {(clave == null ? "sin clave" : groqOk ? $"{msGroq} ms" : errorGroq)} → usa {(usarGroq ? "Groq" : fabrica != null ? "local" : "Vosk")}");
        }
        finally { preparando.Release(); }
    }

    async Task PrepararLocal(int gpu, string nombreGpu, Action<string> aviso, CancellationToken ct)
    {
        var archivo = gpu >= 0 ? ModeloGpu : ModeloCpu;
        var ruta = Path.Combine(Carpeta, archivo);
        if (!File.Exists(ruta)) await Descargar(archivo, ruta, aviso, ct);
        RuntimeOptions.RuntimeLibraryOrder = gpu >= 0 ? [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu] : [RuntimeLibrary.Cpu];
        fabrica = WhisperFactory.FromPath(ruta, new WhisperFactoryOptions { UseGpu = gpu >= 0, GpuDevice = Math.Max(0, gpu) });
        var cargada = RuntimeOptions.LoadedLibrary?.ToString() ?? "?";
        dondeLocal = gpu >= 0 && cargada == "Vulkan" ? $"la tarjeta {nombreGpu}" : "el procesador";
        // Una pasada en vacío: reserva memoria y compila lo de la tarjeta gráfica, para que la primera orden no espere.
        // No sirve para medir la velocidad: con silencio, Whisper a veces se alarga (en la RTX 5060 dio 1,8 s contra
        // 0,65 s de una orden real). La velocidad se mide con las órdenes de verdad (ver Transcribir).
        await TranscribirLocal(new byte[32000], ct);
    }

    /// <summary>
    /// En automático, con los dos motores listos, Groq solo si el local es lento: medido con órdenes reales, o antes de
    /// la primera orden, si no corre en una tarjeta dedicada (la integrada tarda ~1,5 s y el procesador ~3,5 s).
    /// </summary>
    void Decidir()
    {
        var modo = Modo;
        var localLento = msLocal is long ms ? ms > MsLocalAceptable : !localDedicada;
        usarGroq = groqOk && (modo == "groq" || fabrica == null || (modo == "auto" && localLento));
    }

    /// <summary>Audio PCM de 16 kHz, 16 bits, mono. Devuelve el texto, "" si no oyó habla, o null si falló.</summary>
    public async Task<string?> Transcribir(byte[] pcm, CancellationToken ct = default)
    {
        GuardarParaRevisar(pcm);
        if (usarGroq)
        {
            var t = await TranscribirGroq(pcm, ct);
            if (t != null) return Limpiar(t);
            if (fabrica == null) return null; // sin local: queda lo que oyó Vosk
            Registro.Log("Groq no respondió: uso el oído local");
        }
        var reloj = Stopwatch.StartNew();
        var texto = await TranscribirLocal(pcm, ct);
        if (texto != null)
        {
            var ms = reloj.ElapsedMilliseconds;
            msLocal = msLocal is long antes ? (antes + ms) / 2 : ms;
            var eraGroq = usarGroq;
            Decidir();
            if (usarGroq && !eraGroq) Registro.Log($"El oído local tarda {msLocal} ms por frase: desde ahora uso Groq");
        }
        return texto;
    }

    async Task<string?> TranscribirLocal(byte[] pcm, CancellationToken ct)
    {
        if (fabrica == null) return null;
        await turno.WaitAsync(ct);
        try
        {
            var prompt = Prompt();
            if (prompt != promptUsado || proc == null) Construir(prompt);
            var n = pcm.Length / 2;
            var muestras = new float[n];
            for (int i = 0; i < n; i++) muestras[i] = BitConverter.ToInt16(pcm, 2 * i) / 32768f;
            var sb = new StringBuilder();
            await foreach (var s in proc!.ProcessAsync(muestras, ct)) sb.Append(s.Text);
            return Limpiar(sb.ToString());
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) { Registro.Log("Whisper falló: " + ex.Message); return null; }
        finally { turno.Release(); }
    }

    /// <summary>El mismo modelo (whisper-large-v3-turbo) en Groq. Nunca se anota la clave en el registro.</summary>
    async Task<string?> TranscribirGroq(byte[] pcm, CancellationToken ct)
    {
        if (clave == null) return null;
        try
        {
            using var form = new MultipartFormDataContent();
            var wav = new ByteArrayContent(Wav(pcm));
            wav.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            form.Add(wav, "file", "orden.wav");
            form.Add(new StringContent("whisper-large-v3-turbo"), "model");
            form.Add(new StringContent("es"), "language");
            form.Add(new StringContent(Prompt()), "prompt");
            form.Add(new StringContent("json"), "response_format");
            form.Add(new StringContent("0"), "temperature");
            using var req = new HttpRequestMessage(HttpMethod.Post, UrlGroq) { Content = form };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clave);
            var reloj = Stopwatch.StartNew();
            using var r = await Http.SendAsync(req, ct);
            var cuerpo = await r.Content.ReadAsStringAsync(ct);
            if (!r.IsSuccessStatusCode)
            {
                var codigo = (int)r.StatusCode;
                errorGroq = codigo switch
                {
                    401 or 403 => "La clave de Groq no es válida.",
                    429 => "Groq llegó a su límite gratis por ahora.",
                    _ => $"Groq respondió con un error ({codigo}).",
                };
                Registro.Log($"Groq respondió {codigo}: {cuerpo[..Math.Min(200, cuerpo.Length)]}");
                return null;
            }
            msGroq = reloj.ElapsedMilliseconds;
            errorGroq = null;
            return JsonDocument.Parse(cuerpo).RootElement.GetProperty("text").GetString() ?? "";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return null; }
        catch (Exception ex)
        {
            errorGroq = "No hay conexión con Groq.";
            Registro.Log("Groq falló: " + ex.Message);
            return null;
        }
    }

    static byte[] Wav(byte[] pcm)
    {
        using var m = new MemoryStream(44 + pcm.Length);
        using (var w = new BinaryWriter(m, Encoding.ASCII, true))
        {
            w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16); w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm);
        }
        return m.ToArray();
    }

    /// <summary>Para afinar el oído: con PATCH_GUARDAR_AUDIO=carpeta, guarda cada audio que transcribe como WAV.</summary>
    static void GuardarParaRevisar(byte[] pcm)
    {
        var carpeta = Environment.GetEnvironmentVariable("PATCH_GUARDAR_AUDIO");
        if (string.IsNullOrEmpty(carpeta)) return;
        try
        {
            Directory.CreateDirectory(carpeta);
            File.WriteAllBytes(Path.Combine(carpeta, $"oido-{DateTime.Now:HHmmss-fff}.wav"), Wav(pcm));
        }
        catch { }
    }

    void Construir(string prompt)
    {
        proc?.Dispose();
        // Ojo: no achicar la ventana de audio (WithAudioContextSize) para ir más rápido. Se probó con 768: escribe
        // "¿qué oráis?" por "¿qué hora es?" y repite la frase varias veces. Con la ventana completa sale bien.
        proc = fabrica!.CreateBuilder()
            .WithLanguage("es")
            .WithPrompt(prompt)
            .WithThreads(Math.Max(4, Environment.ProcessorCount / 2))
            .WithNoContext()
            .WithSingleSegment()
            .Build();
        promptUsado = prompt;
    }

    /// <summary>
    /// La pista que se le da a Whisper: el nombre del asistente y del usuario, y palabras que suelen aparecer en las
    /// órdenes. Así escribe "Patch" y no "touch" o "pues". Se puede ampliar con vocabulario= en patch-config.txt.
    /// </summary>
    static string Prompt()
    {
        var a = Config.Asistente;
        var u = Config.Usuario;
        var extra = Config.Valor("vocabulario");
        return $"{a}{(u.Length > 0 ? ", " + u : "")}. Oye {a}. Hola {a}. " +
               "Spotify, Google Drive, Gmail, Calendar, WhatsApp, YouTube, Excel, Word, PowerPoint, PDF" +
               (extra.Length > 0 ? ", " + extra : "") + ".";
    }

    // Lo que Whisper suele "oír" en el silencio o el ruido (aprendido de subtítulos de internet). "[Música]", "(risas)"…
    static readonly string[] Alucinaciones =
    {
        "subtitulos", "subtítulos", "amara.org", "suscribete", "suscríbete", "gracias por ver", "[", "(",
    };

    static string Limpiar(string t)
    {
        t = t.Trim();
        var minus = t.ToLowerInvariant();
        if (Alucinaciones.Any(minus.Contains) && minus.Length < 80) return "";
        if (minus is "gracias." or "gracias" or "." or "...") return "";
        return SinRepeticiones(t);
    }

    /// <summary>
    /// Whisper a veces se queda en un bucle y repite la misma frase ("¿Cuánto es 12 por 8? ¿Cuánto es 12 por 8?…").
    /// Se dejan solo las frases distintas, en orden.
    /// </summary>
    static string SinRepeticiones(string t)
    {
        var frases = System.Text.RegularExpressions.Regex.Split(t, @"(?<=[.?!])\s+").Where(f => f.Length > 0).ToList();
        if (frases.Count < 2) return t;
        var vistas = new System.Collections.Generic.HashSet<string>();
        var quedan = frases.Where(f => vistas.Add(string.Join(" ", Texto.Palabras(f)))).ToList();
        return quedan.Count == frases.Count ? t : string.Join(" ", quedan);
    }

    async Task Descargar(string archivo, string ruta, Action<string> aviso, CancellationToken ct)
    {
        Directory.CreateDirectory(Carpeta);
        var parcial = ruta + ".part";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        using var r = await http.GetAsync(UrlModelos + archivo, HttpCompletionOption.ResponseHeadersRead, ct);
        r.EnsureSuccessStatusCode();
        var total = r.Content.Headers.ContentLength ?? 0;
        aviso($"Mejoro el oído: descargo el reconocimiento fino ({(total > 0 ? total / 1_000_000 : 0)} MB). Solo pasa una vez.");
        await using (var origen = await r.Content.ReadAsStreamAsync(ct))
        await using (var destino = File.Create(parcial))
        {
            var buf = new byte[1 << 16];
            long leidos = 0;
            int n, ultimo = -1;
            while ((n = await origen.ReadAsync(buf, ct)) > 0)
            {
                await destino.WriteAsync(buf.AsMemory(0, n), ct);
                leidos += n;
                var pct = total > 0 ? (int)(leidos * 100 / total) : 0;
                if (pct / 10 != ultimo) { ultimo = pct / 10; Registro.Log($"Descargando {archivo}: {pct}%"); }
            }
        }
        File.Move(parcial, ruta, true);
    }

    // ------------------------------------------------------------------ Qué tarjeta gráfica usar

    /// <summary>
    /// La tarjeta dedicada si hay (NVIDIA, AMD), si no la integrada; -1 si no hay Vulkan (solo procesador).
    /// El índice es el orden de Vulkan, el mismo que usa whisper.cpp (comprobado: 0 = Radeon integrada, 1 = RTX 5060).
    /// </summary>
    public static (int indice, string nombre, bool dedicada) ElegirGpu()
    {
        if (Config.Valor("whisper_gpu") is { Length: > 0 } fijo && int.TryParse(fijo, out var f)) return (f, $"la tarjeta {f}", f >= 0);
        try
        {
            var info = new VkInstanceCreateInfo { sType = 1 };
            if (vkCreateInstance(ref info, IntPtr.Zero, out var instancia) != 0) return (-1, "", false);
            try
            {
                uint n = 0;
                vkEnumeratePhysicalDevices(instancia, ref n, null);
                if (n == 0) return (-1, "", false);
                var disp = new IntPtr[n];
                vkEnumeratePhysicalDevices(instancia, ref n, disp);
                int integrada = -1, dedicada = -1;
                string nombreI = "", nombreD = "";
                var props = Marshal.AllocHGlobal(4096);
                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        vkGetPhysicalDeviceProperties(disp[i], props);
                        var tipo = Marshal.ReadInt32(props, 16);           // VkPhysicalDeviceType
                        var nombre = Marshal.PtrToStringUTF8(props + 20) ?? "";
                        Registro.Log($"Tarjeta gráfica {i}: {nombre} (tipo {tipo})");
                        if (tipo == 2 && dedicada < 0) { dedicada = i; nombreD = nombre; }
                        if (tipo == 1 && integrada < 0) { integrada = i; nombreI = nombre; }
                    }
                }
                finally { Marshal.FreeHGlobal(props); }
                return dedicada >= 0 ? (dedicada, nombreD, true) : integrada >= 0 ? (integrada, nombreI, false) : (-1, "", false);
            }
            finally { vkDestroyInstance(instancia, IntPtr.Zero); }
        }
        catch (Exception ex) { Registro.Log("Sin Vulkan (Whisper usará el procesador): " + ex.Message); return (-1, "", false); }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct VkInstanceCreateInfo
    {
        public int sType; public IntPtr pNext; public uint flags; public IntPtr pApplicationInfo;
        public uint enabledLayerCount; public IntPtr ppEnabledLayerNames; public uint enabledExtensionCount; public IntPtr ppEnabledExtensionNames;
    }

    [DllImport("vulkan-1.dll")] static extern int vkCreateInstance(ref VkInstanceCreateInfo info, IntPtr asignador, out IntPtr instancia);
    [DllImport("vulkan-1.dll")] static extern int vkEnumeratePhysicalDevices(IntPtr instancia, ref uint cuantos, [In, Out] IntPtr[]? dispositivos);
    [DllImport("vulkan-1.dll")] static extern void vkGetPhysicalDeviceProperties(IntPtr dispositivo, IntPtr propiedades);
    [DllImport("vulkan-1.dll")] static extern void vkDestroyInstance(IntPtr instancia, IntPtr asignador);

    public void Dispose()
    {
        proc?.Dispose();
        fabrica?.Dispose();
    }
}

/// <summary>
/// La clave de Groq de cada usuario. No va en patch-config.txt porque Claude lee ese archivo: se guarda aparte, en
/// %LOCALAPPDATA%\PATCH (que Claude no puede leer), cifrada con la cuenta de Windows del usuario (DPAPI).
/// </summary>
public static class ClaveGroq
{
    static string Ruta => Path.Combine(Config.Datos, "groq.clave");
    static readonly byte[] Entropia = "PATCH-groq"u8.ToArray();

    public static string? Leer()
    {
        try
        {
            if (!File.Exists(Ruta)) return null;
            var texto = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(Ruta), Entropia, DataProtectionScope.CurrentUser)).Trim();
            return texto.Length > 0 ? texto : null;
        }
        catch { return null; }
    }

    public static bool Hay => Leer() != null;

    public static void Guardar(string clave)
    {
        clave = clave.Trim();
        if (clave.Length == 0) { if (File.Exists(Ruta)) File.Delete(Ruta); return; }
        File.WriteAllBytes(Ruta, ProtectedData.Protect(Encoding.UTF8.GetBytes(clave), Entropia, DataProtectionScope.CurrentUser));
    }
}
