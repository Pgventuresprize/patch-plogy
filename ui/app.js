// Interfaz de la barra lateral de PATCH (compartida por Windows y Mac).
(function () {
  const $ = (id) => document.getElementById(id);
  const chat = $("chat"), entrada = $("entrada"), btnEnviar = $("btnEnviar");
  let cfg = { asistente: "Patch", usuario: "", microfono: true, voz: true };
  let enVivo = null;           // burbuja con lo que se está oyendo
  let palabra = null;          // confirmación escrita pendiente
  let editando = null;         // comando que se está editando en Ajustes ("" si es uno nuevo)

  // ---------------------------------------------------------------- Utilidades
  const escapar = (s) => s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

  // Markdown mínimo: negritas, código, enlaces, listas y párrafos.
  function formatear(t) {
    const lineas = escapar(t.trim()).split(/\r?\n/);
    let html = "", lista = false, parrafo = [];
    const cerrarParrafo = () => { if (parrafo.length) { html += "<p>" + parrafo.join("<br>") + "</p>"; parrafo = []; } };
    for (const l of lineas) {
      const item = l.match(/^\s*(?:[-*•]|\d+\.)\s+(.*)$/);
      if (item) { cerrarParrafo(); if (!lista) { html += "<ul>"; lista = true; } html += "<li>" + item[1] + "</li>"; continue; }
      if (lista) { html += "</ul>"; lista = false; }
      if (!l.trim()) { cerrarParrafo(); continue; }
      parrafo.push(l.replace(/^#{1,6}\s*/, ""));
    }
    cerrarParrafo();
    if (lista) html += "</ul>";
    return html
      .replace(/\*\*([^*]+)\*\*/g, "<b>$1</b>")
      .replace(/`([^`]+)`/g, "<code>$1</code>")
      .replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g, '<a href="$2" target="_blank">$1</a>')
      .replace(/(^|[\s(])(https?:\/\/[^\s<)]+)/g, '$1<a href="$2" target="_blank">$2</a>');
  }

  function bajar() { chat.scrollTop = chat.scrollHeight; }

  function agregar(rol, texto, hora) {
    $("vacio").hidden = true;
    quitarEnVivo();
    const d = document.createElement("div");
    d.className = "msg " + rol;
    if (rol === "asistente") d.innerHTML = formatear(texto);
    else d.textContent = texto;
    if (hora && rol !== "sistema") { const h = document.createElement("span"); h.className = "hora"; h.textContent = hora; d.appendChild(h); }
    chat.appendChild(d);
    bajar();
  }

  function mostrarEnVivo(texto) {
    if (!texto) { quitarEnVivo(); return; }
    $("vacio").hidden = true;
    if (!enVivo) { enVivo = document.createElement("div"); enVivo.className = "msg usuario en-vivo"; chat.appendChild(enVivo); }
    enVivo.textContent = texto;
    bajar();
  }
  function quitarEnVivo() { if (enVivo) { enVivo.remove(); enVivo = null; } }

  function pista() {
    const p = $("pista");
    p.classList.remove("escuchando");
    p.textContent = cfg.microfono
      ? (cfg.escucharNombre === false ? `Micrófono activo · usa ${cfg.atajo || "Ctrl+Alt+P"} para hablarle` : `Micrófono activo · di «${cfg.asistente}» para hablarle`)
      : "Micrófono bloqueado · escríbeme";
  }

  function aplicarConfig(c) {
    cfg = Object.assign(cfg, c);
    $("nombre").textContent = cfg.asistente;
    $("avatar").textContent = (cfg.asistente || "P").trim().charAt(0).toUpperCase();
    document.querySelectorAll(".nombreAsistente").forEach((e) => (e.textContent = cfg.asistente));
    $("saludoUsuario").textContent = cfg.usuario ? ", " + cfg.usuario : "";
    entrada.placeholder = `Escríbele a ${cfg.asistente}…`;
    document.title = `${cfg.asistente} · PATCH`;
    $("btnMic").classList.toggle("apagado", !cfg.microfono);
    $("btnMic").title = cfg.microfono ? "Bloquear el micrófono (solo chat)" : "Activar el micrófono";
    $("btnVoz").classList.toggle("apagado", !cfg.voz);
    $("btnVoz").title = cfg.voz ? "Silenciar la voz" : "Leer en voz alta";
    pista();

    // Ajustes (sin pisar lo que el usuario está escribiendo)
    if (document.activeElement !== $("cfgAsistente")) $("cfgAsistente").value = cfg.asistente || "";
    if (document.activeElement !== $("cfgUsuario")) $("cfgUsuario").value = cfg.usuario || "";
    if (cfg.modelos) $("cfgModelo").innerHTML = cfg.modelos.map((m) => `<option value="${m.id}">${escapar(m.nombre)}</option>`).join("");
    $("cfgModelo").value = cfg.modelo;
    if (cfg.voces) $("cfgVoz").innerHTML = cfg.voces.map((v) => `<option value="${v.id}">${escapar(v.nombre)}</option>`).join("");
    $("cfgVoz").value = cfg.vozEdge;
    // Sin voces para elegir (Mac usa la del sistema): solo queda el botón de probar.
    $("cfgVoz").hidden = !(cfg.voces && cfg.voces.length);
    $("cfgLeer").checked = !!cfg.voz;
    $("cfgEscuchar").checked = cfg.escucharNombre !== false;
    $("cfgEco").checked = cfg.cancelarEco !== false;
    $("cfgAgc").checked = cfg.volumenAutomatico !== false;
    // Opciones que la app no maneja (por ejemplo, en Mac el volumen automático lo hace el sistema)
    $("cfgEco").parentElement.hidden = cfg.cancelarEco === undefined;
    $("cfgAgc").parentElement.hidden = cfg.volumenAutomatico === undefined;
    $("cfgWhisper").checked = cfg.whisper !== false;
    $("cfgWhisper").parentElement.hidden = cfg.whisper === undefined;
    $("oidoFino").hidden = cfg.whisper === undefined || cfg.whisper === false;
    $("cfgOido").value = cfg.oido || "auto";
    if (document.activeElement !== $("cfgGroq")) {
      $("cfgGroq").value = "";
      $("cfgGroq").placeholder = cfg.groqClave ? "Clave guardada ✓ (pega otra para cambiarla)" : "Pega aquí tu clave gsk_…";
    }
    $("oidoEstado").textContent = cfg.oidoEstado || "";
    document.querySelectorAll(".atajo2").forEach((e) => (e.textContent = cfg.atajo || "Ctrl+Alt+P"));
    $("cfgInicio").checked = !!cfg.inicioWindows;
    $("atajo").textContent = cfg.atajo || "Ctrl+Alt+P";
    $("alias").innerHTML = (cfg.alias || []).map((a) => `<span>${escapar(a)}</span>`).join("");
    // La app que no maneja comandos (todavía la de Mac) no manda la lista: la sección no se muestra.
    $("seccionComandos").hidden = cfg.comandos === undefined;
    $("lnkComandos").hidden = cfg.comandos === undefined;
    if (c.comandos !== undefined && editando === null) pintarComandos();
  }

  // ---------------------------------------------------------------- Comandos

  function pintarComandos() {
    const lista = $("listaComandos");
    lista.innerHTML = "";
    const todos = cfg.comandos || [];
    if (!todos.length && editando === null) lista.innerHTML = '<p class="nota" style="margin-bottom:8px">Todavía no tienes comandos.</p>';
    for (const c of todos) lista.appendChild(editando === c.archivo ? formulario(c) : tarjeta(c));
    if (editando === "") lista.appendChild(formulario({ archivo: "", frases: "", pasos: "" }));
    $("btnNuevoComando").hidden = editando !== null;
  }
  function tarjeta(c) {
    const d = document.createElement("div");
    d.className = "comando";
    const [nombre, ...otras] = c.frases.split(",").map((x) => x.trim());
    d.innerHTML = `<div class="comando-cabecera"><b>«${escapar(nombre)}»</b><span><button class="enlace" data-a="probar">Probar</button><button class="enlace" data-a="editar">Editar</button></span></div>` +
      (otras.length ? `<div class="comando-frases">También: ${otras.map(escapar).join(", ")}</div>` : "") +
      `<div class="comando-pasos">${escapar(c.pasos)}</div>`;
    d.querySelector('[data-a="probar"]').onclick = () => { Puente.enviar({ tipo: "comandoProbar", archivo: c.archivo }); $("ajustes").hidden = true; };
    d.querySelector('[data-a="editar"]').onclick = () => { editando = c.archivo; pintarComandos(); };
    return d;
  }
  function formulario(c) {
    const d = document.createElement("div");
    d.className = "comando";
    d.innerHTML = `<label class="campo"><span>Cuando le digas</span><input class="frases" placeholder="buenos días, buen día"><small>Puedes poner varias formas, separadas por comas.</small></label>` +
      `<label class="campo"><span>Lo que hace</span><textarea class="pasos" placeholder="1. Dime la hora.&#10;2. Dime el clima de hoy en Bogotá.&#10;3. Lee mi agenda de hoy."></textarea><small>Escríbelo como se lo dirías a una persona, un paso por línea.</small></label>` +
      `<div class="comando-error" hidden></div>` +
      `<div class="comando-botones"><button class="boton principal" data-a="guardar">Guardar</button><button class="boton" data-a="cancelar">Cancelar</button>` +
      (c.archivo ? `<button class="enlace borrar" data-a="borrar">Borrar</button>` : "") + `</div>`;
    d.querySelector(".frases").value = c.frases;
    d.querySelector(".pasos").value = c.pasos;
    d.querySelector('[data-a="guardar"]').onclick = () => {
      const frases = d.querySelector(".frases").value.trim(), pasos = d.querySelector(".pasos").value.trim();
      const err = d.querySelector(".comando-error");
      if (!frases || !pasos) { err.textContent = !frases ? "Falta la frase que lo activa." : "Faltan los pasos."; err.hidden = false; return; }
      Puente.enviar({ tipo: "comandoGuardar", archivo: c.archivo, frases, pasos });
    };
    d.querySelector('[data-a="cancelar"]').onclick = () => { editando = null; pintarComandos(); };
    const borrar = d.querySelector('[data-a="borrar"]');
    if (borrar) borrar.onclick = () => {
      if (!borrar.dataset.seguro) { borrar.dataset.seguro = "1"; borrar.textContent = "¿Seguro? Toca otra vez"; return; }
      Puente.enviar({ tipo: "comandoBorrar", archivo: c.archivo });
      editando = null;
    };
    setTimeout(() => d.querySelector(c.archivo ? ".pasos" : ".frases").focus(), 30);
    return d;
  }
  $("btnNuevoComando").onclick = () => { editando = ""; pintarComandos(); };
  function comandoGuardado(m) {
    if (m.error) {
      const err = document.querySelector("#listaComandos .comando-error");
      if (err) { err.textContent = m.error; err.hidden = false; }
      return;
    }
    editando = null;
    pintarComandos();
  }

  function estado(e) {
    const trabajando = e.fase === "pensando";
    $("trabajando").hidden = !trabajando;
    if (trabajando) $("trabajandoTexto").textContent = e.titulo || "Pensando…";
    $("estado").textContent = e.titulo || (e.fase === "hablando" ? "Hablando…" : "Listo");
    $("medidor").classList.toggle("activo", e.fase === "escuchando" || e.fase === "seguimiento");
    const p = $("pista");
    if (e.fase === "escuchando") { p.textContent = "Te escucho…"; p.classList.add("escuchando"); mostrarEnVivo(e.texto || ""); }
    else if (e.fase === "seguimiento") { p.textContent = e.titulo || "Te sigo escuchando…"; p.classList.add("escuchando"); quitarEnVivo(); }
    else { pista(); if (e.fase !== "escuchando") quitarEnVivo(); }
    $("avatar").style.boxShadow = e.fase === "hablando" ? "0 0 0 4px rgba(255,91,0,.25)" : "none";
  }

  // ---------------------------------------------------------------- Confirmaciones
  function confirmar(m) {
    palabra = m.palabra || null;
    $("confirmar").hidden = !palabra;
    if (!palabra) return;
    $("permiso").hidden = true;
    $("trabajando").hidden = true;
    $("estado").textContent = "Esperando tu confirmación escrita";
    $("confAccion").textContent = m.accion || "hacer esto";
    $("confDescripcion").textContent = m.descripcion || "";
    $("confDetalle").textContent = m.detalle || "";
    $("confPalabra").textContent = palabra;
    $("confEscrito").value = "";
    $("confEscrito").placeholder = "Escribe " + palabra;
    $("confSi").disabled = true;
    setTimeout(() => $("confEscrito").focus(), 50);
  }
  $("confEscrito").addEventListener("input", () => { $("confSi").disabled = $("confEscrito").value.trim().toUpperCase() !== palabra; });
  $("confEscrito").addEventListener("keydown", (e) => { if (e.key === "Enter" && !$("confSi").disabled) $("confSi").click(); });
  $("confSi").onclick = () => { Puente.enviar({ tipo: "confirmar", ok: true, escrito: $("confEscrito").value }); $("confirmar").hidden = true; };
  $("confNo").onclick = () => { Puente.enviar({ tipo: "confirmar", ok: false }); $("confirmar").hidden = true; palabra = null; };

  function permiso(m) {
    $("permDescripcion").textContent = m.descripcion || "";
    $("permDetalle").textContent = m.detalle || "";
    $("permDetalle").hidden = !m.detalle;
    $("permiso").hidden = false;
    $("trabajando").hidden = true;
    $("estado").textContent = "Esperando tu permiso";
  }
  $("permSi").onclick = () => { $("permiso").hidden = true; Puente.enviar({ tipo: "permiso", ok: true }); };
  $("permNo").onclick = () => { $("permiso").hidden = true; Puente.enviar({ tipo: "permiso", ok: false }); };

  // ---------------------------------------------------------------- Calibración
  function calibracion(m) {
    const e = $("calEstado");
    if (m.terminado) {
      $("btnCalibrar").disabled = false;
      e.innerHTML = m.aviso ? escapar(m.aviso) : (m.alias && m.alias.length ? "Listo: ya sé cómo te oigo." : "Listo.");
      return;
    }
    $("btnCalibrar").disabled = true;
    e.innerHTML = `Di <b>«${escapar(m.nombre)}»</b> (${m.paso} de ${m.total})` + (m.oido ? `<br>Antes oí: «${escapar(m.oido)}»` : "");
  }
  $("btnCalibrar").onclick = () => Puente.enviar({ tipo: "calibrar" });

  // ---------------------------------------------------------------- Mensajes de la app
  Puente.alRecibir((m) => {
    switch (m.tipo) {
      case "config": aplicarConfig(m); break;
      case "historial":
        chat.querySelectorAll(".msg").forEach((x) => x.remove());
        enVivo = null;
        $("vacio").hidden = m.mensajes.length > 0;
        for (const x of m.mensajes) agregar(x.rol, x.texto, x.hora);
        break;
      case "mensaje": agregar(m.rol, m.texto, m.hora); if (m.rol === "asistente") { $("trabajando").hidden = true; $("permiso").hidden = true; } break;
      case "estado": estado(m); break;
      case "nivel": $("nivel").style.width = Math.round(m.v * 100) + "%"; break;
      case "confirmar": confirmar(m); break;
      case "permiso": permiso(m); break;
      case "calibracion": calibracion(m); break;
      case "comandoGuardado": comandoGuardado(m); break;
      case "escuchando": {
        // Durante la calibración se muestra lo que va oyendo
        if (!$("btnCalibrar").disabled) break;
        let vivo = $("calEstado").querySelector(".vivo");
        if (!vivo) { vivo = document.createElement("div"); vivo.className = "vivo"; $("calEstado").appendChild(vivo); }
        vivo.textContent = "… " + m.texto;
        break;
      }
      case "bienvenida": abrirAjustes(true); break;
      case "seccion": if (m.nombre === "ajustes") abrirAjustes(false); break;
      case "abierta": case "enfocar": if ($("ajustes").hidden) setTimeout(() => entrada.focus(), 30); break;
    }
  });

  // ---------------------------------------------------------------- Redactor
  function ajustarAlto() { entrada.style.height = "auto"; entrada.style.height = Math.min(140, entrada.scrollHeight) + "px"; btnEnviar.disabled = !entrada.value.trim(); }
  function enviar(texto) {
    texto = (texto ?? entrada.value).trim();
    if (!texto) return;
    Puente.enviar({ tipo: "enviar", texto });
    entrada.value = "";
    ajustarAlto();
  }
  entrada.addEventListener("input", ajustarAlto);
  entrada.addEventListener("keydown", (e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); enviar(); } });
  btnEnviar.onclick = () => enviar();
  document.querySelectorAll(".sugerencias button").forEach((b) => (b.onclick = () => enviar(b.dataset.texto)));
  $("btnDetener").onclick = () => Puente.enviar({ tipo: "detener" });

  // ---------------------------------------------------------------- Cabecera
  $("btnMic").onclick = () => Puente.enviar({ tipo: "microfono", on: !cfg.microfono });
  $("btnVoz").onclick = () => { cfg.voz = !cfg.voz; aplicarConfig({}); Puente.enviar({ tipo: "voz", on: cfg.voz }); };
  $("btnCerrar").onclick = () => Puente.enviar({ tipo: "cerrar" });
  $("btnAjustes").onclick = () => abrirAjustes(false);
  $("btnVolver").onclick = () => { $("ajustes").hidden = true; entrada.focus(); };

  function abrirAjustes(bienvenida) {
    $("bienvenida").hidden = !bienvenida;
    $("ajustes").hidden = false;
    if (bienvenida) setTimeout(() => $("cfgAsistente").select(), 50);
  }

  // ---------------------------------------------------------------- Ajustes
  const guardar = (clave, valor) => Puente.enviar({ tipo: "config", clave, valor: String(valor) });
  $("cfgAsistente").addEventListener("change", (e) => { const v = e.target.value.trim() || "Patch"; e.target.value = v; guardar("asistente", v); });
  $("cfgUsuario").addEventListener("change", (e) => guardar("usuario", e.target.value.trim()));
  $("cfgModelo").addEventListener("change", (e) => guardar("modelo", e.target.value));
  $("cfgVoz").addEventListener("change", (e) => guardar("voz_edge", e.target.value));
  $("btnProbarVoz").onclick = () => Puente.enviar({ tipo: "probarVoz" });
  $("cfgLeer").addEventListener("change", (e) => { cfg.voz = e.target.checked; aplicarConfig({}); Puente.enviar({ tipo: "voz", on: e.target.checked }); });
  $("cfgEscuchar").addEventListener("change", (e) => guardar("escuchar_nombre", e.target.checked ? "on" : "off"));
  $("cfgEco").addEventListener("change", (e) => guardar("cancelar_eco", e.target.checked ? "on" : "off"));
  $("cfgAgc").addEventListener("change", (e) => guardar("volumen_automatico", e.target.checked ? "on" : "off"));
  $("cfgWhisper").addEventListener("change", (e) => guardar("whisper", e.target.checked ? "on" : "off"));
  $("cfgOido").addEventListener("change", (e) => guardar("oido", e.target.value));
  // La clave no pasa por patch-config.txt (Claude lee ese archivo): va directo a la app, que la guarda cifrada.
  $("cfgGroq").addEventListener("change", (e) => {
    const clave = e.target.value.trim();
    e.target.value = "";
    if (clave) { $("oidoEstado").textContent = "Probando la clave…"; Puente.enviar({ tipo: "groqClave", clave }); }
  });
  $("lnkGroq").onclick = (e) => { e.preventDefault(); Puente.enviar({ tipo: "abrir", que: "groq" }); };
  $("cfgInicio").addEventListener("change", (e) => Puente.enviar({ tipo: "inicioWindows", on: e.target.checked }));
  document.querySelectorAll("[data-abrir]").forEach((b) => (b.onclick = () => Puente.enviar({ tipo: "abrir", que: b.dataset.abrir })));
  $("btnNueva").onclick = () => { Puente.enviar({ tipo: "nuevaConversacion" }); $("ajustes").hidden = true; };

  // En Mac la opción de arranque dice "al iniciar sesión"
  if (navigator.platform && navigator.platform.toLowerCase().includes("mac")) {
    document.querySelector("#cfgInicio + span").textContent = "Abrir PATCH al iniciar sesión";
    document.querySelectorAll(".ajustes h3").forEach((h) => { if (h.textContent === "Windows") h.textContent = "Mac"; });
  }

  ajustarAlto();
  Puente.enviar({ tipo: "listo" });
})();
