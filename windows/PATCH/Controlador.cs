using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Patch;

/// <summary>Une oído, cerebro, boca, ventanita, barra lateral y bandeja. Todo corre en el hilo de la interfaz.</summary>
public sealed class Controlador
{
    readonly Dispatcher ui = Application.Current.Dispatcher;
    readonly Oido oido = new();
    readonly Boca boca = new();
    readonly Cerebro cerebro = new();
    readonly Transcriptor transcriptor = new();
    int turnoOido;              // sube con cada orden que pasa por Whisper (para descartar respuestas viejas)
    Pildora pildora = null!;
    BarraLateral barra = null!;
    Bandeja bandeja = null!;
    Nativo.Atajo? atajo;
    readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromMilliseconds(150) };
    FileSystemWatcher? vigia, vigiaComandos;

    Fase fase = Fase.Reposo;
    bool microfonoListo;
    string orden = "";
    bool nombreEnEstaFrase;
    bool desdeSeguimiento;      // empezó a hablar sin decir el nombre, en la ventana de seguimiento
    string loQueDice = "";      // lo que PATCH está diciendo en voz alta (para no interrumpirse con su propia voz)
    /// <summary>Por debajo de esta seguridad, una frase sin el nombre se descarta (suele ser conversación de fondo).</summary>
    const double SeguridadMinima = 0.62;
    DateTime inicioEscucha = DateTime.Now, ultimoCambio = DateTime.Now, ultimoNivelBarra = DateTime.MinValue;
    List<Denegado> pendientes = new();
    DateTime pendientesDesde = DateTime.MinValue;
    string? palabraPendiente;
    int turnoVoz;
    List<string> alias = new();
    readonly List<object> historial = new();
    List<string>? calibracion;

    static readonly string[] Voces =
    {
        "es-CO-GonzaloNeural|Gonzalo (Colombia)", "es-CO-SalomeNeural|Salomé (Colombia)", "es-MX-JorgeNeural|Jorge (México)",
        "es-MX-DaliaNeural|Dalia (México)", "es-ES-AlvaroNeural|Álvaro (España)", "es-ES-ElviraNeural|Elvira (España)",
        "es-AR-TomasNeural|Tomás (Argentina)", "es-AR-ElenaNeural|Elena (Argentina)",
    };

    // ------------------------------------------------------------------ Arranque

    public void Arrancar()
    {
        Registro.Log($"PATCH arrancó (carpeta {Config.Carpeta})");
        RecalcularAlias();
        pildora = new Pildora();
        pildora.Tocada += () => AlternarBarra();
        pildora.Boton += si => BotonSiNo(si);
        barra = new BarraLateral();
        barra.Mensaje += m => ui.BeginInvoke(() => DeLaBarra(m));
        bandeja = new Bandeja(this);
        atajo = new Nativo.Atajo(() => ui.BeginInvoke(AtajoPulsado));

        oido.Parcial += t => ui.BeginInvoke(() => AlParcial(t));
        oido.Final += f => ui.BeginInvoke(() => AlFinal(f.Texto, f));
        oido.Nivel += n => ui.BeginInvoke(() => AlNivel(n));
        oido.Problema += t => ui.BeginInvoke(() => Avisar(t));

        reloj.Tick += (_, _) => Tic();
        reloj.Start();
        VigilarConfig();
        boca.Preparar();
        cerebro.Precalentar();
        _ = PrepararOido();

        var marca = Path.Combine(Config.Datos, "bienvenida.txt");
        if (!File.Exists(marca))
        {
            File.WriteAllText(marca, DateTime.Now.ToString("s"));
            barra.Mostrar();
            barra.Enviar(new { tipo = "bienvenida" });
        }
    }

    async Task PrepararOido()
    {
        try
        {
            if (!File.Exists(Path.Combine(Oido.CarpetaModelo, "am", "final.mdl")))
            {
                pildora.Mostrar(Fase.Aviso, "Preparando el oído…", "Descargo el reconocimiento de voz en español (38 MB). Solo pasa una vez.");
                await Oido.AsegurarModelo(p => ui.BeginInvoke(() => pildora.PonerTexto($"Descargando el reconocimiento de voz… {p}%")));
            }
            var prueba = Array.IndexOf(Environment.GetCommandLineArgs(), "--probar-audio");
            if (prueba > 0)
            {
                // Modo de prueba: en vez del micrófono, "oye" los WAV indicados (separados por ';').
                oido.IniciarSinMicrofono();
                microfonoListo = true;
                EnviarConfig();
                var archivos = Environment.GetCommandLineArgs()[prueba + 1].Split(';', StringSplitOptions.RemoveEmptyEntries);
                var whisperListo = PrepararWhisper();
                _ = Task.Run(async () =>
                {
                    await whisperListo; // la prueba usa el mismo oído que la app (con Whisper, si está activo)
                    await Task.Delay(3000);
                    for (int k = 0; k < archivos.Length; k++)
                    {
                        var a0 = archivos[k];
                        var siguienteMientrasHabla = k + 1 < archivos.Length && archivos[k + 1].StartsWith("!");
                        // "!archivo.wav" se inyecta MIENTRAS habla (para probar que se le puede interrumpir).
                        var mientrasHabla = a0.StartsWith("!");
                        var a = a0.TrimStart('!');
                        if (mientrasHabla)
                        {
                            for (int i = 0; i < 200 && !await ui.InvokeAsync(() => fase == Fase.Hablando); i++) await Task.Delay(300);
                            await Task.Delay(1500);
                        }
                        Registro.Log("Prueba: inyecto " + Path.GetFileName(a) + (mientrasHabla ? " (mientras habla)" : ""));
                        await oido.Inyectar(a);
                        // Espera a que termine de pensar y hablar antes de la siguiente frase
                        // (salvo que la siguiente sea para decirla mientras habla).
                        await Task.Delay(1500);
                        if (siguienteMientrasHabla) continue;
                        while (await ui.InvokeAsync(() => fase is Fase.Pensando or Fase.Hablando)) await Task.Delay(300);
                        await Task.Delay(1000);
                    }
                    Registro.Log("Prueba: terminé");
                });
                return;
            }
            if (Config.Microfono) AbrirMicrofono();
            pildora.Mostrar(Fase.Aviso, $"{Config.Asistente} está listo",
                microfonoListo ? $"Di «{Config.Asistente}» y tu pedido, o usa Ctrl+Alt+P." : "El micrófono está bloqueado: toca aquí para escribirle.");
            pildora.OcultarEn(6);
            _ = PrepararWhisper();
        }
        catch (Exception ex)
        {
            Registro.Log("No pude preparar el oído: " + ex.Message);
            Avisar("No pude preparar el reconocimiento de voz. Revisa tu conexión y vuelve a abrir PATCH.");
        }
    }

    void AbrirMicrofono()
    {
        try { oido.Iniciar(); microfonoListo = true; }
        catch (Exception ex)
        {
            microfonoListo = false;
            Registro.Log("No pude abrir el micrófono: " + ex);
            Avisar("No pude abrir el micrófono. Revisa Configuración > Privacidad > Micrófono.");
        }
        EnviarConfig();
    }

    void RecalcularAlias() => alias = Texto.AliasDe(Config.Asistente, Config.Alias);

    void VigilarConfig()
    {
        vigia = new FileSystemWatcher(Config.Carpeta, "patch-config.txt") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size };
        var espera = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        espera.Tick += (_, _) => { espera.Stop(); RecalcularAlias(); EnviarConfig(); };
        vigia.Changed += (_, _) => ui.BeginInvoke(() => { espera.Stop(); espera.Start(); });
        vigia.EnableRaisingEvents = true;

        // Los comandos los puede crear Claude (por voz) o el usuario a mano: Ajustes se actualiza solo.
        Comandos.Preparar();
        try
        {
            vigiaComandos = new FileSystemWatcher(Comandos.Carpeta, "*.md") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            var esperaC = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            esperaC.Tick += (_, _) => { esperaC.Stop(); EnviarConfig(); };
            FileSystemEventHandler h = (_, _) => ui.BeginInvoke(() => { esperaC.Stop(); esperaC.Start(); });
            vigiaComandos.Changed += h; vigiaComandos.Created += h; vigiaComandos.Deleted += h;
            vigiaComandos.Renamed += (_, _) => ui.BeginInvoke(() => { esperaC.Stop(); esperaC.Start(); });
            vigiaComandos.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Registro.Log("No pude vigilar los comandos: " + ex.Message); }
    }

    // ------------------------------------------------------------------ Oído

    bool ConversacionAbierta => barra.Abierta && microfonoListo;

    void AlNivel(float n)
    {
        pildora.Nivel(n);
        if (barra.Abierta && (DateTime.Now - ultimoNivelBarra).TotalMilliseconds > 100)
        {
            ultimoNivelBarra = DateTime.Now;
            barra.Enviar(new { tipo = "nivel", v = Math.Min(1, n * 9) });
        }
    }

    void AlParcial(string t)
    {
        ultimoCambio = DateTime.Now;
        if (calibracion != null) { barra.Enviar(new { tipo = "escuchando", texto = t }); return; }
        switch (fase)
        {
            case Fase.Hablando:
                // Mientras habla solo atiende "silencio", "cállate", "descansa"… (o su nombre, si hay cancelación de eco).
                if (Texto.EsParadaMientrasHabla(t, loQueDice)) { Registro.Log($"Interrumpido: «{t}»"); Descansar(); }
                else if (oido.ConEco && Config.EscucharNombre && Texto.DespuesDelNombre(t, Config.Asistente, alias) is { } r)
                {
                    Registro.Log($"Me llamaron mientras hablaba: «{t}»");
                    boca.Callar(); turnoVoz++;
                    Activar(true, r);
                }
                break;
            case Fase.Reposo:
            case Fase.Pensando:
                if (!Config.EscucharNombre) return;
                var resto = Texto.DespuesDelNombre(t, Config.Asistente, alias);
                if (resto == null) return;
                Registro.Log($"Me llamaron: «{t}»");
                if (fase == Fase.Pensando) cerebro.Cancelar();
                Activar(true, resto);
                break;
            case Fase.Escuchando:
                orden = nombreEnEstaFrase ? (Texto.DespuesDelNombre(t, Config.Asistente, alias) ?? orden) : t;
                MostrarEscucha();
                break;
            case Fase.Seguimiento:
            case Fase.Permiso:
                fase = Fase.Escuchando;
                nombreEnEstaFrase = Texto.DespuesDelNombre(t, Config.Asistente, alias) != null;
                desdeSeguimiento = !nombreEnEstaFrase;
                orden = nombreEnEstaFrase ? Texto.DespuesDelNombre(t, Config.Asistente, alias)! : t;
                inicioEscucha = DateTime.Now;
                MostrarEscucha();
                break;
        }
    }

    void AlFinal(string t, Oido.Frase? frase = null)
    {
        ultimoCambio = DateTime.Now;
        if (calibracion != null) { PasoCalibracion(t); return; }
        switch (fase)
        {
            case Fase.Hablando:
                if (Texto.EsParadaMientrasHabla(t, loQueDice)) { Registro.Log($"Interrumpido: «{t}»"); Descansar(); }
                break;
            case Fase.Reposo:
            case Fase.Pensando:
                if (!Config.EscucharNombre) return;
                var resto = Texto.DespuesDelNombre(t, Config.Asistente, alias);
                if (resto == null) return;
                Registro.Log($"Me llamaron: «{t}»");
                if (fase == Fase.Pensando) { cerebro.Cancelar(); turnoOido++; }
                if (resto.Length > 0) { orden = resto; EnviarOido(frase, conNombre: true); }
                else { Activar(false, ""); VerificarSoloNombre(frase); }
                break;
            case Fase.Escuchando:
                var conElNombre = nombreEnEstaFrase;
                orden = nombreEnEstaFrase ? (Texto.DespuesDelNombre(t, Config.Asistente, alias) ?? "") : t;
                if (desdeSeguimiento && !nombreEnEstaFrase && !DirigidaAMi(frase, orden))
                {
                    // Habló alguien sin decir el nombre y no sonaba a una orden para PATCH: se ignora.
                    desdeSeguimiento = false;
                    orden = "";
                    fase = pendientes.Count > 0 ? Fase.Permiso : Fase.Seguimiento;
                    if (!barra.Abierta) pildora.PonerTexto("");
                    break;
                }
                nombreEnEstaFrase = false;
                desdeSeguimiento = false;
                if (orden.Trim().Length > 0) EnviarOido(frase, conElNombre);
                else
                {
                    // Dijo solo el nombre: espera la orden.
                    inicioEscucha = DateTime.Now;
                    MostrarEscucha();
                    if (conElNombre) VerificarSoloNombre(frase);
                }
                break;
            case Fase.Seguimiento:
            case Fase.Permiso:
                var conNombre = Texto.DespuesDelNombre(t, Config.Asistente, alias);
                orden = conNombre ?? t;
                if (conNombre == null && !DirigidaAMi(frase, orden)) { orden = ""; break; }
                if (orden.Trim().Length > 0) EnviarOido(frase, conNombre != null);
                break;
        }
    }

    /// <summary>
    /// ¿Una frase dicha SIN el nombre iba para PATCH? Las órdenes de parar y los sí/no siempre cuentan;
    /// lo demás solo si el reconocedor la oyó con seguridad (la conversación de fondo suele oírse mal).
    /// </summary>
    bool DirigidaAMi(Oido.Frase? f, string texto)
    {
        if (Texto.EsOrdenDeDescanso(texto)) return true;
        if (pendientes.Count > 0 && (Texto.EsAfirmativo(texto) || Texto.EsNegativo(texto))) return true;
        if (f == null) return true;
        // Con la barra abierta la conversación está activa: no se filtra por lo que entendió Vosk (que oye con poca
        // seguridad justo las palabras que no conoce). Decide el oído fino: si no oye habla, se ignora (ver EnviarOido).
        if (barra.Abierta && Transcriptor.Activo && transcriptor.Listo && f.Seguridad < SeguridadMinima)
        {
            Registro.Log($"Frase sin nombre con la barra abierta (seguridad {f.Seguridad:0.00}): la revisa el oído fino. Vosk: «{texto}»");
            return true;
        }
        var ok = f.Seguridad >= SeguridadMinima;
        Registro.Log($"Frase sin nombre {(ok ? "aceptada" : "IGNORADA")} (seguridad {f.Seguridad:0.00}, volumen {f.Volumen:0.000}): «{texto}»");
        return ok;
    }

    void Activar(bool conNombre, string resto)
    {
        if (!microfonoListo) { barra.Mostrar(); return; }
        boca.Callar();
        oido.Pausar(false);
        turnoOido++; // una orden que Whisper todavía estaba transcribiendo ya no vale
        fase = Fase.Escuchando;
        nombreEnEstaFrase = conNombre;
        orden = resto;
        inicioEscucha = ultimoCambio = DateTime.Now;
        if (!conNombre) oido.Reiniciar();
        MostrarEscucha();
    }

    void MostrarEscucha()
    {
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "escuchando", titulo = "Te escucho…", texto = orden });
        else pildora.Mostrar(Fase.Escuchando, "Te escucho…", orden);
    }

    void Tic()
    {
        var ahora = DateTime.Now;
        switch (fase)
        {
            case Fase.Escuchando:
                // Si dejó de cambiar el texto un buen rato, se envía (por si el reconocedor no cerró la frase).
                // Sin el nombre (seguimiento) se espera a la frase completa, que trae la seguridad.
                if (orden.Trim().Length > 0 && !desdeSeguimiento && (ahora - ultimoCambio).TotalSeconds > 2.5) EnviarOido(null, nombreEnEstaFrase);
                else if (desdeSeguimiento && (ahora - ultimoCambio).TotalSeconds > 6) { desdeSeguimiento = false; VolverAReposo(); }
                else if (orden.Trim().Length == 0 && (ahora - inicioEscucha).TotalSeconds > 8) VolverAReposo();
                break;
            case Fase.Seguimiento:
            case Fase.Permiso:
                // Ventana para seguir hablando sin repetir el nombre. Corta a propósito: si es larga,
                // cualquier conversación cercana se le mandaría como orden.
                var espera = pendientes.Count > 0 ? 20 : barra.Abierta ? 10 : 7;
                if (palabraPendiente == null && (ahora - inicioEscucha).TotalSeconds > espera) VolverAReposo();
                break;
        }
    }

    void VolverAReposo(double ocultarEn = 0.3)
    {
        fase = Fase.Reposo;
        orden = "";
        nombreEnEstaFrase = false;
        desdeSeguimiento = false;
        // Los permisos pendientes NO se olvidan aquí: se puede responder después escribiendo "sí" en la barra.
        oido.Pausar(false);
        oido.Reiniciar();
        pildora.OcultarEn(ocultarEn);
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "reposo", titulo = microfonoListo ? $"Di «{Config.Asistente}» para hablarme" : "Micrófono bloqueado: escríbeme", texto = "" });
    }

    // ------------------------------------------------------------------ Oído fino (Whisper)

    /// <param name="rehacer">Cambió el modo o la clave de Groq: se vuelve a elegir el motor aunque ya haya uno listo.</param>
    async Task PrepararWhisper(bool rehacer = false)
    {
        if (!Transcriptor.Activo || (transcriptor.Listo && !rehacer)) return;
        try
        {
            var descargo = false;
            await Task.Run(() => transcriptor.Preparar(aviso => ui.BeginInvoke(() =>
            {
                descargo = true;
                Registro.Log(aviso);
                if (fase == Fase.Reposo && !barra.Abierta) { pildora.Mostrar(Fase.Aviso, "Mejorando el oído…", aviso); pildora.OcultarEn(8); }
            })));
            Registro.Log("Oído fino listo: " + transcriptor.Descripcion);
            if (descargo && fase == Fase.Reposo && !barra.Abierta)
            {
                pildora.Mostrar(Fase.Aviso, "Oído mejorado", $"Ahora entiendo mucho mejor lo que me pides. Di «{Config.Asistente}» y tu pedido.");
                pildora.OcultarEn(6);
            }
            EnviarConfig();
        }
        catch (Exception ex) { Registro.Log("Oído fino no disponible (sigo con Vosk): " + ex.Message); EnviarConfig(); }
    }

    /// <summary>
    /// Manda una orden dicha por voz. Si el oído fino está listo, primero la vuelve a transcribir con Whisper
    /// (Vosk la oyó para detectar el nombre, pero confunde palabras). Con <paramref name="conNombre"/>, la frase
    /// tenía que empezar por el nombre: si Whisper no lo oye, era una falsa alarma (por ejemplo, alguien dijo "pues").
    /// </summary>
    async void EnviarOido(Oido.Frase? f, bool conNombre)
    {
        var vosk = orden;
        var audio = f?.Audio;
        if (audio == null) { audio = oido.AudioEnCurso(); oido.Reiniciar(); } // se envía antes de que Vosk cierre la frase
        if (!Transcriptor.Activo || !transcriptor.Listo || audio.Length < 8000) { Enviar(); return; }

        var mio = ++turnoOido;
        orden = "";
        fase = Fase.Pensando;
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "pensando", titulo = "Te escuché…", texto = vosk });
        else pildora.Mostrar(Fase.Pensando, "Te escuché…", vosk);
        var reloj = Stopwatch.StartNew();
        var texto = await transcriptor.Transcribir(audio);
        if (mio != turnoOido || fase != Fase.Pensando) return; // lo cancelaron o empezó otra orden
        var ms = reloj.ElapsedMilliseconds;

        if (texto == null) { orden = vosk; Registro.Log($"Whisper falló, uso lo de Vosk: «{vosk}»"); }
        else if (texto.Length == 0)
        {
            // Whisper manda: si no oyó habla, era ruido o una conversación lejana.
            Registro.Log($"Whisper no oyó habla ({ms} ms). Vosk había oído «{vosk}»: lo ignoro");
            if (conNombre) { VolverAReposo(); return; }
            SeguirEscuchando();
            return;
        }
        else
        {
            var resto = Texto.DespuesDelNombreEstricto(texto, Config.Asistente);
            if (conNombre && resto == null)
            {
                Registro.Log($"Falsa alarma: Whisper oyó «{texto}» sin el nombre ({ms} ms). Vosk había oído «{vosk}»");
                VolverAReposo();
                return;
            }
            orden = resto ?? texto;
            Registro.Log($"Whisper ({ms} ms): «{texto}» · Vosk: «{vosk}»");
        }
        if (orden.Trim().Trim(',', '.', ' ').Length == 0)
        {
            // Solo dijo el nombre: queda escuchando la orden.
            if (conNombre) { Activar(false, ""); return; }
            VolverAReposo();
            return;
        }
        Enviar();
    }

    /// <summary>Se descartó una frase sin el nombre: sigue escuchando como antes (seguimiento o permiso), sin enviarla.</summary>
    void SeguirEscuchando()
    {
        var permiso = pendientes.Count > 0 || palabraPendiente != null;
        fase = permiso ? Fase.Permiso : Fase.Seguimiento;
        inicioEscucha = DateTime.Now;
        orden = "";
        var titulo = permiso ? "¿Lo autorizo? Di sí o no" : "Te sigo escuchando…";
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "seguimiento", titulo, texto = "" });
        else { pildora.Mostrar(Fase.Seguimiento, titulo, "", siNo: permiso); pildora.OcultarEn(7); }
    }

    /// <summary>
    /// Dijeron solo el nombre y quedó escuchando la orden. Si Whisper no oye el nombre en ese audio, era una falsa
    /// alarma (una palabra parecida en una conversación) y vuelve a reposo, antes de tomar lo siguiente como orden.
    /// </summary>
    async void VerificarSoloNombre(Oido.Frase? f)
    {
        if (f?.Audio is not { Length: >= 8000 } audio || !Transcriptor.Activo || !transcriptor.Listo) return;
        var mio = turnoOido;
        var texto = await transcriptor.Transcribir(audio);
        if (texto == null || mio != turnoOido || fase != Fase.Escuchando || orden.Trim().Length > 0) return;
        if (Texto.DespuesDelNombreEstricto(texto, Config.Asistente) != null) return;
        Registro.Log($"Falsa alarma: dijeron «{texto}», no mi nombre (Vosk: «{f.Texto}»)");
        VolverAReposo();
    }

    // ------------------------------------------------------------------ Hablar con Claude

    void Enviar(string? escrito = null)
    {
        var texto = (escrito ?? orden).Trim().Trim(',', '.', ' ');
        orden = "";
        if (texto.Length == 0) return;
        // "Silencio", "descansa", "deja de escuchar"… se resuelve aquí mismo, sin Claude.
        if (palabraPendiente == null && Texto.EsOrdenDeDescanso(texto)) { Registro.Log($"Orden de descanso: «{texto}»"); Descansar(); return; }
        // Un permiso sin responder sigue vigente 5 minutos (aunque se haya cerrado la ventanita): un "sí" lo aprueba.
        if (pendientes.Count > 0 && palabraPendiente == null && (DateTime.Now - pendientesDesde).TotalMinutes > 5) pendientes = new();
        if (pendientes.Count > 0 && palabraPendiente == null)
        {
            var p = pendientes;
            if (Texto.EsNegativo(texto)) { pendientes = new(); Consultar("No te autorizo eso. No lo hagas.", null, texto); return; }
            if (Texto.EsAfirmativo(texto)) { pendientes = new(); cerebro.Recordar(p); Consultar("Autorizado. Hazlo ahora.", p.Select(d => d.Regla).ToList(), texto); return; }
            pendientes = new();
        }
        if (palabraPendiente != null)
        {
            // Hay una confirmación escrita pendiente: la voz no la reemplaza.
            if (Texto.EsNegativo(texto)) { CancelarConfirmacion(); Consultar("No lo autorizo. No lo hagas.", null, texto); return; }
            Decir($"Para eso necesito que escribas {palabraPendiente} en la barra lateral.");
            return;
        }
        // Un comando guardado ("buenos días"): la app lo reconoce al instante y le pasa los pasos a Claude.
        if (Comandos.Buscar(texto) is { } c)
        {
            Registro.Log($"Comando «{c.Nombre}» ({c.Archivo}) por «{texto}»");
            Consultar(Comandos.Orden(c, texto), null, texto, $"Comando «{c.Nombre}»…");
            return;
        }
        Consultar(texto, null, texto);
    }

    async void Consultar(string texto, List<string>? permitir, string mostrando, string titulo = "Pensando…")
    {
        fase = Fase.Pensando;
        boca.Callar();
        oido.Pausar(false);
        AgregarMensaje("usuario", mostrando);
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "pensando", titulo, texto = "" });
        else pildora.Mostrar(Fase.Pensando, titulo, mostrando);

        var r = await cerebro.Preguntar(texto, permitir, e => ui.BeginInvoke(() =>
        {
            if (fase != Fase.Pensando) return;
            if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "pensando", titulo = e, texto = "" });
            else pildora.PonerTitulo(e);
        }));
        if (fase != Fase.Pensando) return; // la cancelaron
        Respuesta(r);
    }

    void Respuesta(Resultado r)
    {
        if (r.Denegados.Count > 0)
        {
            pendientes = r.Denegados;
            pendientesDesde = DateTime.Now;
            var que = string.Join(", ", r.Denegados.Select(d => d.Descripcion).Distinct());
            var tipo = r.Denegados.Select(d => d.Tipo).Max();
            Registro.Log($"Pide permiso ({tipo}): {string.Join(" | ", r.Denegados.Select(d => d.Regla))}");
            if (!string.IsNullOrWhiteSpace(r.Texto)) AgregarMensaje("asistente", r.Texto);
            if (tipo != TipoPermiso.Normal)
            {
                palabraPendiente = tipo switch { TipoPermiso.Borrar => "BORRAR", TipoPermiso.Enviar => "ENVIAR", _ => "COMPARTIR" };
                barra.Mostrar();
                barra.Enviar(new
                {
                    tipo = "confirmar", palabra = palabraPendiente,
                    accion = tipo switch { TipoPermiso.Borrar => "borrar (va a la papelera)", TipoPermiso.Enviar => "enviar", _ => "compartir" },
                    descripcion = Mayuscula(que),
                    detalle = string.Join("\n", r.Denegados.Select(d => d.Detalle)),
                });
                fase = Fase.Permiso;
                inicioEscucha = DateTime.Now;
                Decir($"Para eso necesito tu confirmación por escrito. Escribe {palabraPendiente} en la barra lateral.", entrarSeguimiento: false);
                return;
            }
            fase = Fase.Permiso;
            inicioEscucha = DateTime.Now; // la ventana para decir "sí" empieza ahora, no cuando se dio la orden
            if (barra.Abierta) barra.Enviar(new { tipo = "permiso", descripcion = Mayuscula(que), detalle = string.Join("\n", r.Denegados.Select(d => d.Detalle)) });
            else pildora.Mostrar(Fase.Permiso, "Necesito tu permiso", Mayuscula(que), siNo: true);
            Decir($"Para eso necesito tu permiso: {que}. ¿Lo autorizo?");
            return;
        }
        var t = string.IsNullOrWhiteSpace(r.Texto) ? "Listo." : r.Texto;
        AgregarMensaje("asistente", t);
        if (!barra.Abierta) pildora.Mostrar(Fase.Hablando, Config.Asistente, ParaPantalla(t), siNo: Texto.EsPreguntaSiNo(t));
        else barra.Enviar(new { tipo = "estado", fase = "hablando", titulo = "", texto = "" });
        Decir(t);
    }

    void Decir(string t, bool entrarSeguimiento = true)
    {
        var mio = ++turnoVoz;
        if (fase != Fase.Permiso) fase = Fase.Hablando;
        // Sigue oyendo mientras habla, pero en Hablando solo atiende órdenes de parar (ver AlParcial/AlFinal).
        loQueDice = t;
        oido.Reiniciar();
        void despues()
        {
            if (mio != turnoVoz) return;
            // Medio segundo de cola y se olvida lo oído: así no toma el final de su propia voz como orden.
            var d = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            d.Tick += (_, _) =>
            {
                d.Stop();
                if (mio != turnoVoz) return;
                loQueDice = "";
                oido.Reiniciar();
                if (entrarSeguimiento) EntrarSeguimiento(t);
            };
            d.Start();
        }
        if (Config.VozActiva) boca.Decir(t, _ => ui.BeginInvoke(despues));
        else
        {
            var lectura = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Min(20, 3 + t.Length / 25.0)) };
            lectura.Tick += (_, _) => { lectura.Stop(); despues(); };
            lectura.Start();
        }
    }

    void EntrarSeguimiento(string dijo = "")
    {
        if (fase is not (Fase.Hablando or Fase.Permiso)) return;
        // Escuchar sin el nombre solo tiene sentido si preguntó algo, si espera un permiso o si la barra está abierta.
        // Si no, vuelve a pedir el nombre: así una conversación cercana no se le manda como orden.
        var hizoPregunta = dijo.TrimEnd().EndsWith("?");
        if (fase == Fase.Hablando && !hizoPregunta && !barra.Abierta)
        {
            VolverAReposo(ocultarEn: Math.Min(12, 4 + dijo.Length / 40.0)); // la respuesta queda un rato a la vista
            return;
        }
        if (fase == Fase.Hablando) fase = Fase.Seguimiento;
        inicioEscucha = DateTime.Now;
        orden = "";
        oido.Reiniciar();
        if (!microfonoListo) { VolverAReposo(); return; }
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "seguimiento", titulo = pendientes.Count > 0 ? "¿Lo autorizo? Di sí o no" : "Te sigo escuchando…", texto = "" });
        else { pildora.PonerTitulo(pendientes.Count > 0 ? "¿Lo autorizo? Di sí o no" : "¿Algo más?"); pildora.OcultarEn(pendientes.Count > 0 ? 20 : 7); }
    }

    /// <summary>"Silencio", "descansa"…: se calla al instante, deja lo que estaba haciendo y cierra el micrófono.</summary>
    void Descansar()
    {
        cerebro.Cancelar();
        boca.Callar();
        turnoVoz++;
        loQueDice = "";
        if (palabraPendiente != null) CancelarConfirmacion();
        pendientes = new();
        orden = "";
        fase = Fase.Reposo;
        if (microfonoListo) { oido.Detener(); microfonoListo = false; }
        Config.Poner("microfono", "off");
        Registro.Log("Descansando: voz cortada y micrófono cerrado");
        pildora.Mostrar(Fase.Aviso, "Descansando", "No escucho nada. Para despertarme: Ctrl+Alt+P o el icono junto al reloj.");
        pildora.OcultarEn(5);
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "reposo", titulo = "Descansando: no escucho", texto = "" });
        EnviarConfig();
        bandeja.Actualizar();
    }

    /// <summary>Vuelve a escuchar. Con escuchar=true queda atento a la orden de una vez (sin decir el nombre).</summary>
    public void Despertar(bool escuchar)
    {
        PonerMicrofono(true);
        if (!microfonoListo) return;
        Registro.Log("Despierto");
        if (escuchar) Activar(false, "");
        else { pildora.Mostrar(Fase.Aviso, "Te escucho de nuevo", $"Di «{Config.Asistente}» y tu pedido."); pildora.OcultarEn(3); }
    }

    void BotonSiNo(bool si)
    {
        boca.Callar();
        if (pendientes.Count > 0) Enviar(si ? "sí" : "no");
        else Consultar(si ? "Sí." : "No.", null, si ? "Sí" : "No");
    }

    void CancelarConfirmacion()
    {
        palabraPendiente = null;
        pendientes = new();
        barra.Enviar(new { tipo = "confirmar", palabra = (string?)null });
    }

    // ------------------------------------------------------------------ Gestos

    void AtajoPulsado()
    {
        if (!microfonoListo) { Despertar(escuchar: true); return; } // descansando: el atajo lo despierta y escucha
        switch (fase)
        {
            case Fase.Reposo:
            case Fase.Seguimiento:
            case Fase.Permiso: Activar(false, ""); break;
            case Fase.Escuchando: if (orden.Trim().Length == 0) VolverAReposo(); else EnviarOido(null, nombreEnEstaFrase); break;
            case Fase.Hablando: boca.Callar(); turnoVoz++; Activar(false, ""); break;
            case Fase.Pensando: cerebro.Cancelar(); Activar(false, ""); break;
        }
    }

    public void AlternarBarra()
    {
        if (barra.Abierta) barra.Cerrar();
        else { pildora.Esconder(); barra.Mostrar(); }
    }

    public void AbrirBarra(string? seccion = null)
    {
        pildora.Esconder();
        barra.Mostrar();
        if (seccion != null) barra.Enviar(new { tipo = "seccion", nombre = seccion });
    }

    public bool MicrofonoActivo => microfonoListo;

    public void PonerMicrofono(bool on)
    {
        Config.Poner("microfono", on ? "on" : "off");
        if (on && !microfonoListo) AbrirMicrofono();
        if (!on && microfonoListo) { oido.Detener(); microfonoListo = false; if (fase is Fase.Escuchando or Fase.Seguimiento) VolverAReposo(); EnviarConfig(); }
        bandeja.Actualizar();
    }

    public void Salir()
    {
        Registro.Log("PATCH se cerró");
        barra.Cerrar();
        cerebro.Cerrar();
        boca.Dispose();
        oido.Dispose();
        try { transcriptor.Dispose(); } catch { }
        atajo?.Dispose();
        bandeja.Dispose();
        Application.Current.Shutdown();
    }

    // ------------------------------------------------------------------ Barra lateral

    void DeLaBarra(JsonElement m)
    {
        string S(string c) => m.TryGetProperty(c, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        bool B(string c) => m.TryGetProperty(c, out var v) && v.ValueKind == JsonValueKind.True;
        switch (S("tipo"))
        {
            case "listo":
                EnviarConfig();
                barra.Enviar(new { tipo = "historial", mensajes = historial });
                break;
            case "enviar":
                boca.Callar();
                turnoVoz++;
                if (fase == Fase.Pensando) cerebro.Cancelar();
                fase = Fase.Reposo;
                Enviar(S("texto"));
                break;
            case "microfono": PonerMicrofono(B("on")); break;
            case "voz":
                Config.Poner("voz", B("on") ? "on" : "off");
                if (!B("on")) boca.Callar();
                break;
            case "config":
                var clave = S("clave");
                if (new[] { "asistente", "usuario", "modelo", "voz_edge", "escuchar_nombre", "max_caracteres", "cancelar_eco", "volumen_automatico", "whisper", "oido" }.Contains(clave))
                {
                    Config.Poner(clave, S("valor"));
                    if (clave is "whisper" or "oido" && Transcriptor.Activo) _ = PrepararWhisper(rehacer: true);
                    if (clave is "cancelar_eco" or "volumen_automatico" && microfonoListo)
                    {
                        // Se reabre el micrófono con la nueva forma de oír.
                        oido.Detener();
                        microfonoListo = false;
                        AbrirMicrofono();
                        if (fase is Fase.Escuchando or Fase.Seguimiento) VolverAReposo();
                    }
                    if (clave == "asistente") { Config.Poner("alias", ""); cerebro.NuevaConversacion(); }
                    if (clave == "modelo") cerebro.Reconfigurar();
                    RecalcularAlias();
                    EnviarConfig();
                }
                break;
            case "confirmar":
                var escrito = S("escrito").Trim().ToUpperInvariant();
                if (palabraPendiente == null) break;
                if (B("ok") && escrito == palabraPendiente)
                {
                    var reglas = pendientes.Select(d => d.Regla).ToList();
                    Registro.Log($"Confirmado por escrito ({palabraPendiente}): {string.Join(" | ", reglas)}");
                    palabraPendiente = null;
                    pendientes = new();
                    barra.Enviar(new { tipo = "confirmar", palabra = (string?)null });
                    Consultar($"El usuario lo confirmó por escrito. Hazlo ahora, exactamente lo que propusiste.", reglas, "Confirmado por escrito");
                }
                else if (!B("ok")) { CancelarConfirmacion(); Consultar("No lo autorizo. No lo hagas.", null, "Cancelado"); }
                break;
            case "permiso":
                Enviar(B("ok") ? "sí" : "no");
                break;
            case "groqClave":
                // La clave no pasa por patch-config.txt ni por el registro: se guarda cifrada y se prueba.
                try { ClaveGroq.Guardar(S("clave")); Registro.Log("Clave de Groq guardada"); }
                catch (Exception ex) { Registro.Log("No pude guardar la clave de Groq: " + ex.Message); }
                _ = PrepararWhisper(rehacer: true);
                break;
            case "comandoGuardar":
                try
                {
                    var archivo = Comandos.Guardar(S("archivo"), S("frases"), S("pasos"));
                    Registro.Log($"Comando guardado desde Ajustes: {archivo}");
                    barra.Enviar(new { tipo = "comandoGuardado", archivo, error = (string?)null });
                }
                catch (Exception ex) { barra.Enviar(new { tipo = "comandoGuardado", archivo = S("archivo"), error = ex.Message }); }
                EnviarConfig();
                break;
            case "comandoBorrar":
                try { Comandos.Borrar(S("archivo")); Registro.Log($"Comando a la papelera: {S("archivo")}"); }
                catch (Exception ex) { Registro.Log("No pude borrar el comando: " + ex.Message); }
                EnviarConfig();
                break;
            case "comandoProbar":
                if (Comandos.Todos().FirstOrDefault(x => x.Archivo == S("archivo")) is { } cp)
                {
                    boca.Callar();
                    turnoVoz++;
                    if (fase == Fase.Pensando) cerebro.Cancelar();
                    fase = Fase.Reposo;
                    Consultar(Comandos.Orden(cp, cp.Nombre), null, cp.Nombre, $"Comando «{cp.Nombre}»…");
                }
                break;
            case "calibrar": EmpezarCalibracion(); break;
            case "probarVoz": Decir($"Hola, soy {Config.Asistente}. Así sueno.", false); break;
            case "nuevaConversacion":
                cerebro.NuevaConversacion();
                historial.Clear();
                barra.Enviar(new { tipo = "historial", mensajes = historial });
                break;
            case "detener":
                cerebro.Cancelar();
                boca.Callar();
                turnoVoz++;
                VolverAReposo();
                break;
            case "cerrar": barra.Cerrar(); break;
            case "inicioWindows": PonerInicio(B("on")); EnviarConfig(); break;
            case "abrir":
                var que = S("que");
                var ruta = que switch { "notas" => Path.Combine(Config.Carpeta, "notas"), "registro" => Registro.Ruta, "carpeta" => Config.Carpeta, "comandos" => Comandos.Carpeta, "config" => Config.Ruta, "groq" => "https://console.groq.com/keys", _ => null };
                if (ruta != null) try { Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true }); } catch { }
                break;
        }
    }

    void EnviarConfig()
    {
        barra.Enviar(new
        {
            tipo = "config",
            asistente = Config.Asistente, usuario = Config.Usuario, modelo = Config.Modelo, voz = Config.VozActiva,
            microfono = microfonoListo, microfonoConfig = Config.Microfono, escucharNombre = Config.EscucharNombre,
            vozEdge = Config.VozEdge, alias = Config.Alias, inicioWindows = InicioActivo, atajo = "Ctrl+Alt+P",
            cancelarEco = Config.Valor("cancelar_eco", "on") != "off", volumenAutomatico = Config.Valor("volumen_automatico", "on") != "off",
            conEco = oido.ConEco,
            comandos = Comandos.Todos().Select(c => new { archivo = c.Archivo, frases = string.Join(", ", c.Frases), pasos = c.Pasos }),
            whisper = Transcriptor.Activo, oido = Transcriptor.Modo, groqClave = ClaveGroq.Hay, oidoEstado = transcriptor.Estado,
            voces = Voces.Select(v => new { id = v.Split('|')[0], nombre = v.Split('|')[1] }),
            modelos = new[] { new { id = "haiku", nombre = "Haiku (el más rápido)" }, new { id = "sonnet", nombre = "Sonnet (equilibrado)" }, new { id = "opus", nombre = "Opus (el más capaz)" } },
        });
        bandeja?.Actualizar();
    }

    void AgregarMensaje(string rol, string texto)
    {
        var m = new { tipo = "mensaje", rol, texto, hora = DateTime.Now.ToString("HH:mm") };
        historial.Add(m);
        if (historial.Count > 200) historial.RemoveAt(0);
        barra.Enviar(m);
    }

    // ------------------------------------------------------------------ Calibración del nombre

    void EmpezarCalibracion()
    {
        if (!microfonoListo) { barra.Enviar(new { tipo = "calibracion", terminado = true, aviso = "Activa el micrófono para calibrar." }); return; }
        boca.Callar();
        cerebro.Cancelar();
        fase = Fase.Reposo;
        calibracion = new List<string>();
        oido.Pausar(false);
        oido.Reiniciar();
        barra.Enviar(new { tipo = "calibracion", paso = 1, total = 3, nombre = Config.Asistente });
    }

    void PasoCalibracion(string oyo)
    {
        if (calibracion == null) return;
        calibracion.Add(oyo);
        Registro.Log($"Calibración {calibracion.Count}/3: oí «{oyo}»");
        if (calibracion.Count < 3)
        {
            barra.Enviar(new { tipo = "calibracion", paso = calibracion.Count + 1, total = 3, nombre = Config.Asistente, oido = oyo });
            return;
        }
        var (nuevos, aviso) = Texto.Calibrar(Config.Asistente, calibracion);
        calibracion = null;
        var todos = Config.Alias.Concat(nuevos).Distinct().ToList();
        Config.Poner("alias", string.Join(",", todos));
        RecalcularAlias();
        barra.Enviar(new { tipo = "calibracion", terminado = true, alias = todos, aviso, oido = oyo });
        EnviarConfig();
    }

    // ------------------------------------------------------------------ Varios

    void Avisar(string t)
    {
        if (barra.Abierta) barra.Enviar(new { tipo = "mensaje", rol = "sistema", texto = t, hora = DateTime.Now.ToString("HH:mm") });
        else { pildora.Mostrar(Fase.Aviso, "PATCH", t); pildora.OcultarEn(8); }
    }

    static string Mayuscula(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    static string ParaPantalla(string t) =>
        System.Text.RegularExpressions.Regex.Replace(System.Text.RegularExpressions.Regex.Replace(t, @"\*\*([^*]+)\*\*", "$1"), @"(?m)^\s*#{1,6}\s*", "").Trim();

    const string ClaveInicio = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool InicioActivo => Registry.CurrentUser.OpenSubKey(ClaveInicio)?.GetValue("PATCH") != null;

    public static void PonerInicio(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(ClaveInicio, true);
        if (k == null) return;
        if (on) k.SetValue("PATCH", $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue("PATCH", false);
    }
}
