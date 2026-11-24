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
    Pildora pildora = null!;
    BarraLateral barra = null!;
    Bandeja bandeja = null!;
    Nativo.Atajo? atajo;
    readonly DispatcherTimer reloj = new() { Interval = TimeSpan.FromMilliseconds(150) };
    FileSystemWatcher? vigia;

    Fase fase = Fase.Reposo;
    bool microfonoListo;
    string orden = "";
    bool nombreEnEstaFrase;
    DateTime inicioEscucha = DateTime.Now, ultimoCambio = DateTime.Now, ultimoNivelBarra = DateTime.MinValue;
    List<Denegado> pendientes = new();
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
        oido.Final += t => ui.BeginInvoke(() => AlFinal(t));
        oido.Nivel += n => ui.BeginInvoke(() => AlNivel(n));
        oido.Problema += t => ui.BeginInvoke(() => Avisar(t));

        reloj.Tick += (_, _) => Tic();
        reloj.Start();
        VigilarConfig();
        boca.Preparar();
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
                _ = Task.Run(async () =>
                {
                    await Task.Delay(3000);
                    foreach (var a in archivos)
                    {
                        Registro.Log("Prueba: inyecto " + Path.GetFileName(a));
                        await oido.Inyectar(a);
                        // Espera a que termine de pensar y hablar antes de la siguiente frase
                        await Task.Delay(1500);
                        while (await ui.InvokeAsync(() => fase is Fase.Pensando or Fase.Hablando)) await Task.Delay(300);
                        await Task.Delay(1000);
                    }
                    Registro.Log("Prueba: terminé");
                });
                return;
            }
            if (Config.Microfono) AbrirMicrofono();
            pildora.Mostrar(Fase.Aviso, $"{Config.Asistente} está listo",
                microfonoListo ? $"Di «{Config.Asistente}» y tu pedido, o usa Ctrl+Alt+Espacio." : "El micrófono está bloqueado: toca aquí para escribirle.");
            pildora.OcultarEn(6);
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
                orden = nombreEnEstaFrase ? Texto.DespuesDelNombre(t, Config.Asistente, alias)! : t;
                inicioEscucha = DateTime.Now;
                MostrarEscucha();
                break;
        }
    }

    void AlFinal(string t)
    {
        ultimoCambio = DateTime.Now;
        if (calibracion != null) { PasoCalibracion(t); return; }
        switch (fase)
        {
            case Fase.Reposo:
            case Fase.Pensando:
                if (!Config.EscucharNombre) return;
                var resto = Texto.DespuesDelNombre(t, Config.Asistente, alias);
                if (resto == null) return;
                Registro.Log($"Me llamaron: «{t}»");
                if (fase == Fase.Pensando) cerebro.Cancelar();
                if (resto.Length > 0) { orden = resto; Enviar(); }
                else { Activar(false, ""); }
                break;
            case Fase.Escuchando:
                orden = nombreEnEstaFrase ? (Texto.DespuesDelNombre(t, Config.Asistente, alias) ?? "") : t;
                nombreEnEstaFrase = false;
                if (orden.Trim().Length > 0) Enviar();
                else { inicioEscucha = DateTime.Now; MostrarEscucha(); } // dijo solo el nombre: espera la orden
                break;
            case Fase.Seguimiento:
            case Fase.Permiso:
                orden = Texto.DespuesDelNombre(t, Config.Asistente, alias) ?? t;
                if (orden.Trim().Length > 0) Enviar();
                break;
        }
    }

    void Activar(bool conNombre, string resto)
    {
        if (!microfonoListo) { barra.Mostrar(); return; }
        boca.Callar();
        oido.Pausar(false);
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
                if (orden.Trim().Length > 0 && (ahora - ultimoCambio).TotalSeconds > 2.5) Enviar();
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

    void VolverAReposo()
    {
        fase = Fase.Reposo;
        orden = "";
        nombreEnEstaFrase = false;
        if (palabraPendiente == null) pendientes = new();
        oido.Pausar(false);
        oido.Reiniciar();
        pildora.OcultarEn(0.3);
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "reposo", titulo = microfonoListo ? $"Di «{Config.Asistente}» para hablarme" : "Micrófono bloqueado: escríbeme", texto = "" });
    }

    // ------------------------------------------------------------------ Hablar con Claude

    void Enviar(string? escrito = null)
    {
        var texto = (escrito ?? orden).Trim().Trim(',', '.', ' ');
        orden = "";
        if (texto.Length == 0) return;
        if (pendientes.Count > 0 && palabraPendiente == null)
        {
            var p = pendientes;
            if (Texto.EsNegativo(texto)) { pendientes = new(); Consultar("No te autorizo eso. No lo hagas.", null, texto); return; }
            if (Texto.EsAfirmativo(texto)) { pendientes = new(); Consultar("Autorizado. Hazlo ahora.", p.Select(d => d.Regla).ToList(), texto); return; }
            pendientes = new();
        }
        if (palabraPendiente != null)
        {
            // Hay una confirmación escrita pendiente: la voz no la reemplaza.
            if (Texto.EsNegativo(texto)) { CancelarConfirmacion(); Consultar("No lo autorizo. No lo hagas.", null, texto); return; }
            Decir($"Para eso necesito que escribas {palabraPendiente} en la barra lateral.");
            return;
        }
        Consultar(texto, null, texto);
    }

    async void Consultar(string texto, List<string>? permitir, string mostrando)
    {
        fase = Fase.Pensando;
        boca.Callar();
        oido.Pausar(false);
        AgregarMensaje("usuario", mostrando);
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "pensando", titulo = "Pensando…", texto = "" });
        else pildora.Mostrar(Fase.Pensando, "Pensando…", mostrando);

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
            if (barra.Abierta) barra.Enviar(new { tipo = "permiso", descripcion = Mayuscula(que), detalle = string.Join("\n", r.Denegados.Select(d => d.Detalle)) });
            else pildora.Mostrar(Fase.Permiso, "Necesito tu permiso", Mayuscula(que), siNo: true);
            Decir($"Para eso necesito tu permiso: {que}. ¿Lo autorizo?");
            return;
        }
        var t = string.IsNullOrWhiteSpace(r.Texto) ? "Listo." : r.Texto;
        AgregarMensaje("asistente", t);
        if (!barra.Abierta) pildora.Mostrar(Fase.Hablando, Config.Asistente, ParaPantalla(t), siNo: t.TrimEnd().EndsWith("?"));
        else barra.Enviar(new { tipo = "estado", fase = "hablando", titulo = "", texto = "" });
        Decir(t);
    }

    void Decir(string t, bool entrarSeguimiento = true)
    {
        var mio = ++turnoVoz;
        if (fase != Fase.Permiso) fase = Fase.Hablando;
        oido.Pausar(true); // que no se escuche a sí mismo
        void despues()
        {
            if (mio != turnoVoz) return;
            // Medio segundo de cola para que no se oiga el final de su propia voz.
            var d = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            d.Tick += (_, _) => { d.Stop(); if (mio != turnoVoz) return; oido.Pausar(false); if (entrarSeguimiento) EntrarSeguimiento(); };
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

    void EntrarSeguimiento()
    {
        if (fase is not (Fase.Hablando or Fase.Permiso)) return;
        if (fase == Fase.Hablando) fase = Fase.Seguimiento;
        inicioEscucha = DateTime.Now;
        orden = "";
        oido.Reiniciar();
        if (!microfonoListo) { VolverAReposo(); return; }
        if (barra.Abierta) barra.Enviar(new { tipo = "estado", fase = "seguimiento", titulo = pendientes.Count > 0 ? "¿Lo autorizo? Di sí o no" : "Te sigo escuchando…", texto = "" });
        else { pildora.PonerTitulo(pendientes.Count > 0 ? "¿Lo autorizo? Di sí o no" : "¿Algo más?"); pildora.OcultarEn(pendientes.Count > 0 ? 20 : 7); }
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
        if (!microfonoListo) { barra.Mostrar(); barra.Enviar(new { tipo = "enfocar" }); return; }
        switch (fase)
        {
            case Fase.Reposo:
            case Fase.Seguimiento:
            case Fase.Permiso: Activar(false, ""); break;
            case Fase.Escuchando: if (orden.Trim().Length == 0) VolverAReposo(); else Enviar(); break;
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
        cerebro.Cancelar();
        boca.Dispose();
        oido.Dispose();
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
                if (new[] { "asistente", "usuario", "modelo", "voz_edge", "escuchar_nombre", "max_caracteres" }.Contains(clave))
                {
                    Config.Poner(clave, S("valor"));
                    if (clave == "asistente") { Config.Poner("alias", ""); cerebro.NuevaConversacion(); }
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
                var ruta = que switch { "notas" => Path.Combine(Config.Carpeta, "notas"), "registro" => Registro.Ruta, "carpeta" => Config.Carpeta, "config" => Config.Ruta, _ => null };
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
            vozEdge = Config.VozEdge, alias = Config.Alias, inicioWindows = InicioActivo, atajo = "Ctrl+Alt+Espacio",
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
