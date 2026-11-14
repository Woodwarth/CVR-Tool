// Web / Touch controls
// Module: Serves the local browser interface and routes authenticated requests to the shared session.
// Purpose: render English local browser controls from the shared Application state.
// Units: dB/ms/metres/Hz/Q; Peak response preview explicitly uses 48 kHz.
// State: drafts remain local; an independent heartbeat pauses writes on connection loss.
// Scope: no direct amplifier transport, external scripts.
"use strict";
// A page owns one logical session; HTTP connections remain free to run in parallel.
const browserClientId = Array.from(
  crypto.getRandomValues(new Uint8Array(16)),
  (b) => b.toString(16).padStart(2, "0"),
).join("");
const gainQueue = new Map(),
  gainActive = new Set();
let gainSending = false,
  gainTimer = null,
  gainCurrent = null;
const $ = (s) => document.querySelector(s),
  esc = (v) =>
    String(v ?? "").replace(
      /[&<>"']/g,
      (c) =>
        ({
          "&": "&amp;",
          "<": "&lt;",
          ">": "&gt;",
          '"': "&quot;",
          "'": "&#39;",
        })[c],
    );
let key = location.hash.slice(1) || sessionStorage.getItem("cvr-key") || "",
  selected = "",
  tab = "Main",
  state = null,
  busy = false,
  dialogApply = null,
  appConnected = false,
  stateFresh = false,
  lastHeartbeat = 0,
  heartbeatBusy = false,
  connectionProblem = "connecting",
  accessEpoch = 0,
  stateGeneration = 0,
  refreshInFlight = null;
const heartbeatIntervalMs = 2000,
  heartbeatTimeoutMs = 3000,
  connectionStaleMs = 5000;
if (location.hash) {
  sessionStorage.setItem("cvr-key", key);
  history.replaceState(null, "", location.pathname);
}
const fmt = (v, u = "") =>
  v == null
    ? "—"
    : Number(v).toLocaleString("en-US", { maximumFractionDigits: 2 }) + u;
function status(text, error = false) {
  $("#status").textContent = text;
  $("#status").className = error ? "error" : "";
}
function renderConnection() {
  const ready = appConnected && stateFresh;
  const kind = !key
    ? "access"
    : ready
      ? "connected"
      : appConnected
        ? "connecting"
        : connectionProblem;
  const messages = {
    connected: ["Connected", "Browser controls are up to date."],
    connecting: [
      "Connecting to desktop app…",
      "Controls are paused until the current session is loaded.",
    ],
    disconnected: [
      "App connection lost",
      "Controls paused. Reconnecting automatically…",
    ],
    busy: [
      "Browser control in use",
      "Another browser is connected. Close it before connecting here.",
    ],
    unauthorized: [
      "Connection link expired",
      "Scan the QR code in Desktop again or enter its current access key.",
    ],
    access: [
      "Scan to connect",
      "Use the QR code or connection link shown in the desktop app.",
    ],
  };
  const [title, detail] = messages[kind];
  $("#app-connection").dataset.state = kind;
  $("#connection-title").textContent = title;
  $("#app-connection .led").className = "led " + (ready ? "good" : "bad");
  $("#connection-detail").textContent =
    detail +
    (kind === "disconnected" && lastHeartbeat
      ? " Last response: " + new Date(lastHeartbeat).toLocaleTimeString() + "."
      : "");
  $("#controls").disabled = !ready;
  $("#editor-controls").disabled = !ready;
  $("#editor-commit").disabled = !ready;
  $("#editor-connection").textContent = title + ". " + detail;
  $("#editor-connection").classList.toggle("hidden", ready);
}
function loseConnection(reason = "disconnected") {
  cancelQueuedGains();
  if (appConnected || stateFresh) stateGeneration++;
  appConnected = false;
  stateFresh = false;
  connectionProblem = reason;
  renderConnection();
  if (reason === "disconnected")
    status("Offline · Showing the last received settings", true);
}
function connectionReady() {
  if (appConnected && Date.now() - lastHeartbeat > connectionStaleMs)
    loseConnection();
  return appConnected && stateFresh;
}
async function api(path, body, timeoutMs = body ? 15000 : 5000) {
  if (body && !connectionReady())
    throw Error(
      "Desktop app disconnected. Wait for the connection before applying changes.",
    );
  const epoch = accessEpoch;
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  let r;
  try {
    r = await fetch(path, {
      method: body ? "POST" : "GET",
      headers: {
        Authorization: "Bearer " + key,
        "X-CVR-Client": browserClientId,
        ...(body ? { "Content-Type": "application/json" } : {}),
      },
      body: body ? JSON.stringify(body) : undefined,
      cache: "no-store",
      signal: controller.signal,
    });
  } catch (e) {
    if (epoch === accessEpoch) loseConnection();
    throw Error(
      body
        ? "Connection lost. Check the current device state after reconnecting before trying again."
        : "Desktop app is not responding.",
    );
  } finally {
    clearTimeout(timeout);
  }
  if (epoch !== accessEpoch)
    throw Error("The connection link changed. Waiting for the new session.");
  if (!r.ok) {
    if (r.status === 401) loseConnection("unauthorized");
    if (r.status === 423) loseConnection("busy");
    let e;
    try {
      e = await r.json();
    } catch {}
    const error = Error(
      e?.error ||
        (r.status === 401
          ? "Enter the access key from the desktop app."
          : "Request failed: " + r.status),
    );
    error.httpStatus = r.status;
    throw error;
  }
  return r;
}
function live() {
  return !state.device.isDemo;
}
function offline() {
  const r = state.device.readings;
  return (
    live() &&
    (!r ||
      r.error ||
      !r.telemetryObservedAt ||
      Date.now() - Date.parse(r.telemetryObservedAt) > 3000)
  );
}
function allow(cap = "canSetVerifiedDsp") {
  return !offline() && (!live() || state.device.readings?.[cap]);
}
const dis = (b) => (b ? "" : " disabled");
async function change(operation, data = {}) {
  if (busy || gainSending || gainQueue.size)
    throw Error("Wait for the current change to be confirmed.");
  if (!connectionReady())
    throw Error(
      "Desktop app disconnected. Wait for a fresh connection before editing.",
    );
  busy = true;
  status("Applying and checking readback…");
  try {
    await api("/api/change", { deviceId: selected, operation, ...data });
    if (document.activeElement instanceof HTMLElement)
      document.activeElement.blur();
    await refresh(true);
    status("Change confirmed");
    return true;
  } catch (e) {
    status(e.message, true);
    throw e;
  } finally {
    busy = false;
  }
}
const pressedPointers = new Set();
document.addEventListener("pointerdown", e => pressedPointers.add(e.pointerId), true);
for (const event of ["pointerup","pointercancel"]) window.addEventListener(event,e=>pressedPointers.delete(e.pointerId),true);
window.addEventListener("blur",()=>pressedPointers.clear());
async function refresh(force = false) {
  if (!key) return;
  if (
    !force &&
    (busy || pressedPointers.size ||
      gainSending ||
      gainQueue.size ||
      gainActive.size ||
      $("#editor").open ||
      document.activeElement?.matches("input,select"))
  )
    return;
  if (refreshInFlight) return refreshInFlight;
  const epoch = accessEpoch;
  const generation = stateGeneration;
  const request = (async () => {
    const devices = await (await api("/api/devices")).json();
    if (epoch !== accessEpoch || generation !== stateGeneration) return;
    if (!devices.some((d) => d.id === selected))
      selected = devices[0]?.id || "";
    $("#devices").innerHTML = devices
      .map(
        (d) =>
          `<option value="${esc(d.id)}" ${d.id === selected ? "selected" : ""}>${esc(d.name)} · ${esc(d.capabilities.model)}</option>`,
      )
      .join("");
    if (!force && pressedPointers.size) return;
    renderDeviceList(devices);
    if (!selected) {
      state = null;
      $("#identity").textContent = "No devices available.";
      $("#page").replaceChildren();
      stateFresh = true;
      renderConnection();
      return;
    }
    const received = await (
      await api("/api/state?id=" + encodeURIComponent(selected))
    ).json();
    if (epoch !== accessEpoch || generation !== stateGeneration) return;
    if (!force && (gainSending || gainQueue.size || gainActive.size || pressedPointers.size || $("#editor").open || document.activeElement?.matches("input,select"))) return;
    state = received;
    stateFresh = true;
    render();
    renderConnection();
  })();
  refreshInFlight = request;
  try {
    return await request;
  } finally {
    if (refreshInFlight === request) refreshInFlight = null;
  }
}
async function heartbeat() {
  if (heartbeatBusy || !key) return;
  const epoch = accessEpoch;
  heartbeatBusy = true;
  try {
    await api("/api/health", undefined, heartbeatTimeoutMs);
    if (epoch !== accessEpoch) return;
    const reconnecting = !appConnected || !stateFresh;
    const previouslyConnected = lastHeartbeat > 0;
    lastHeartbeat = Date.now();
    appConnected = true;
    renderConnection();
    if (reconnecting) {
      await refresh(true);
      if (connectionReady() && (!state || !offline()))
        status(
          previouslyConnected
            ? "Connection restored · Current settings loaded"
            : "Connected to desktop app",
        );
    }
  } catch (e) {
    if (epoch !== accessEpoch) return;
    if (e.httpStatus === 401) loseConnection("unauthorized");
    else if (e.httpStatus === 423) loseConnection("busy");
    else if (!appConnected) loseConnection();
    status(e.message, true);
  } finally {
    heartbeatBusy = false;
  }
}
function useAccessKey(value) {
  key = value.trim();
  sessionStorage.setItem("cvr-key", key);
  accessEpoch++;
  lastHeartbeat = 0;
  loseConnection("connecting");
  heartbeat();
}
// Reuse the desktop symbol shapes instead of depending on platform emoji fonts.
function symbol(kind, active = false) {
  const speaker = "M 1,7 L 5,7 L 10,3 L 10,15 L 5,11 L 1,11 Z";
  const paths = {
    speaker: speaker + " M 13,5 Q 18,9 13,13",
    mute: speaker + " M 13,6 L 18,12 M 18,6 L 13,12",
    standby: "M 5,4 C -2,10 2,18 10,18 C 18,18 22,10 15,4 M 10,0 L 10,10",
    lock:
      "M 3,8 H 17 V 18 H 3 Z M 10,11 V 15 " +
      (active
        ? "M 6,8 L 6,5 C 6,0 14,0 14,5 L 14,8"
        : "M 6,8 L 6,5 C 6,0 14,0 14,5"),
    wink: "M 7,18 C 5,17 4,15 3,12 L 1.7,9.5 Q 1,8 2.2,7.6 Q 3,7.2 3.8,8.4 L 5.5,10.5 L 3.7,4.6 Q 3.3,3.3 4.4,3 Q 5.5,2.7 6,4 L 7.3,8 L 6.3,2.5 Q 6.1,1.3 7.2,1.1 Q 8.3,1 8.5,2.3 L 9.5,7.8 L 9.6,2.5 Q 9.6,1.3 10.7,1.3 Q 11.8,1.3 11.8,2.6 L 11.8,8 L 12.9,4 Q 13.2,2.9 14.3,3.2 Q 15.4,3.5 15.1,4.8 L 13.7,10.8 C 14.7,14.8 13.5,17.8 10,18.5 Z M 16.5,2 Q 20,5 18.5,9 M 17,4 Q 18.5,6 17.5,8",
  };
  return `<svg class="symbol" viewBox="-1 -1 22 22" aria-hidden="true"><path d="${paths[kind]}"/></svg>`;
}
function button(label, action, data = {}, enabled = true, cls = "") {
  const title =
    action === "mute"
      ? `${data.enabled ? "Mute" : "Unmute"} ${data.side} ${data.channel}`
      : action === "route"
        ? `Input ${data.input} to output ${data.output}: ${data.enabled ? "muted" : fmt(data.value, " dB")}`
        : ["standby", "lock", "wink"].includes(action)
          ? action + " " + (data.enabled ? "on" : "off")
          : null;
  return `<button data-action="${action}" data-args="${esc(JSON.stringify(data))}" class="${cls}"${title ? ` title="${esc(title)}" aria-label="${esc(title)}"` : ""}${dis(enabled)}>${label}</button>`;
}
function renderDeviceList(devices) {
  $("#device-list").innerHTML = devices
    .map(
      (d) =>
        `<button class="device-button ${d.id === selected ? "active" : ""}" data-device="${esc(d.id)}"><i class="led ${d.isDemo || (!d.readings?.error && d.readings?.telemetryObservedAt && Date.now() - Date.parse(d.readings.telemetryObservedAt) < 3000) ? "good" : "bad"}"></i><span>${esc(d.name)}<small>${esc(d.capabilities.model)}</small></span></button>`,
    )
    .join("");
}
function render() {
  const d = state.device,
    c = state.controls,
    r = d.readings;
  const hours =
    r?.operatingMinutes == null
      ? "—"
      : Math.floor(r.operatingMinutes / 60) +
        " h " +
        (r.operatingMinutes % 60) +
        " min";
  $("#identity").innerHTML =
    `<div class="identity-info"><div><small>Name</small>${button(esc(d.name), "rename", {}, allow("canRename"), "device-name")}</div><div><small>Model</small><span>${esc(d.capabilities.model)}</span></div><div><small>Channels</small><span>${d.capabilities.inputCount} in / ${d.capabilities.outputCount} out</span></div><div><small>Preset</small><span>${esc(r?.currentPreset || (d.isDemo ? "Local settings" : "—"))}</span></div><div><small>Connection</small><span><i class="led ${offline() ? "bad" : "good"}"></i>${offline() ? "Offline · Cached" : d.isDemo ? "Local session" : "Live"}</span></div><div><small>Operating time</small><span>${hours}</span></div></div><div class="device-actions">${button(symbol("lock", c.displayLocked), "lock", { enabled: !c.displayLocked }, allow("canSetDisplayLock"))}${button(symbol("standby"), "standby", { enabled: !c.standby }, allow("canSetStandby") && c.standbyKnown, c.standby ? "on" : "")}${button(symbol("wink"), "wink", { enabled: !c.wink }, allow("canSetWink") && c.wink != null, c.wink ? "on" : "")}</div>`;
  if (offline()) status("Offline · Cached settings", true);
  $("#page").innerHTML =
    tab === "Main"
      ? main()
      : tab === "Inputs"
        ? inputs()
        : tab === "Outputs"
          ? outputs()
          : presets();
  if ($("#preset-name")) {
    const slot = state.presets.slots.find(s => s.number === Number($("#preset-slot").value));
    $("#preset-name").value = presetDrafts.get(presetKey(slot?.number)) ?? (slot?.occupied ? slot.name : "Preset " + (slot?.number ?? 1));
    $("#preset-name").oninput = e => presetDrafts.set(presetKey(slot?.number), e.target.value);
    $("#preset-slot").onchange = e => { presetSlots.set(selected, Number(e.target.value)); render(); };
  }
  const matrix = document.querySelector(".matrix");
  if (matrix) {
    matrix.style.gridTemplateColumns = `repeat(${state.routing.inputCount}, minmax(50px, 1fr))`;
    matrix.style.gridTemplateRows = `32px repeat(${state.routing.outputCount}, 110px)`;
  }
  for (const meter of document.querySelectorAll(".meter")) {
    const db = meter.dataset.db === "" ? null : Number(meter.dataset.db);
    meter.querySelector(".meter-fill").style.height =
      db == null
        ? "0%"
        : Math.max(0, Math.min(100, ((db - Number(meter.dataset.floor)) / -Number(meter.dataset.floor)) * 100)) + "%";
  }
}
function meter(db = null) {
  return `<div class="meter" data-floor="${live() ? -40 : -60}" data-db="${db ?? ""}" aria-label="${db == null ? "Level unknown" : fmt(db, live() ? " dB" : " dBFS")}"><div class="meter-fill"></div>${db == null ? "<span>—</span>" : ""}</div>`;
}
function clip(value) {
  return `<span class="clip"><i class="led ${value === true ? "bad" : value === false ? "" : "unknown"}"></i>Clip</span>`;
}
function gain(side, ch, value, vertical = false) {
  const enabled =
    allow(side === "input" ? "canSetInputGain" : "canSetVerifiedDsp") &&
    value != null;
  const reading = state.telemetry.find(t => t.channel === ch);
  const readout = button(
    value == null ? "—" : Number(value).toFixed(1) + " dB",
    "gain",
    { side, channel: ch, value },
    enabled,
    "value",
  );
  return `<div class="gain-control ${vertical ? "vertical-gain" : ""}" data-gain-side="${side}" data-gain-channel="${ch}"><div class="fader-row"><input aria-label="${side} ${ch} gain" type="range" min="-80" max="18" step="0.5" value="${value ?? 0}" data-gain="${side}" data-channel="${ch}"${dis(enabled)}>${vertical ? meter(reading?.inputMeterDb) : ""}</div>${vertical ? clip(reading?.inputClip) : ""}${readout}</div>`;
}
function mute(side, ch, muted) {
  return button(
    symbol(muted ? "mute" : "speaker"),
    "mute",
    { side, channel: ch, enabled: !muted },
    allow(side === "input" ? "canSetInputMute" : "canSetMutes") &&
      muted != null,
    muted ? "muted" : "",
  );
}
function measurements(ch) {
  const t = state.telemetry.find((t) => t.channel === ch);
  return `<div class="telemetry"><span>Voltage (V)</span><span>${fmt(t?.voltage)}</span><span>Current (A)</span><span>${fmt(t?.current)}</span><span>Load (Ω)</span><span>${fmt(t?.loadOhms)}</span><span>Temp (°C)</span><span>${fmt(t?.temperatureC)}</span></div>`;
}
function flags(ch) {
  const s = state.telemetry.find((t) => t.channel === ch)?.status;
  return `<div class="flags">${[
    ["fault", "Fault"],
    ["load", "Load"],
    ["open", "Open"],
    ["overTemperature", "Temp"],
    ["clip", "Clip"],
    ["standby", "Stby"],
    ["highImpedance", "Hi-Z"],
    ["bridged", "Bridge"],
    ["limiting", "Limit"],
  ]
    .map(
      ([k, label]) =>
        `<span><i class="led ${s?.[k] === true ? "active-" + k : s?.[k] === false ? "" : "unknown"}"></i>${label}</span>`,
    )
    .join("")}</div>`;
}
function main() {
  return `<div class="main-panels"><section class="section-panel"><h2>Inputs</h2><div class="channel-grid">${state.inputs.map((i) => `<article class="card channel-card"><h3>${String.fromCharCode(64 + i.channel)}</h3>${gain("input", i.channel, i.gainDb, true)}${mute("input", i.channel, i.muted)}</article>`).join("")}</div></section><section class="section-panel"><h2>Outputs</h2><div class="channel-grid">${state.device.outputs.map((o) => `<article class="card channel-card"><h3>${esc(o.name)}</h3><div class="output-meter-stage">${meter(state.telemetry.find((t) => t.channel === o.number)?.[live() ? "outputMeterDb" : "levelDbFs"])}${flags(o.number)}</div>${mute("output", o.number, o.muted)}${measurements(o.number)}</article>`).join("")}</div></section></div>`;
}
function routeFace(cell) {
  const angle = (135 + 270 * Math.max(0,Math.min(1,(cell.gainDb+80)/98))) * Math.PI / 180;
  const x = 40+32*Math.cos(angle), y=40+32*Math.sin(angle);
  const arc = cell.muted ? "" : `<path d="M17.37 62.63 A32 32 0 ${cell.gainDb > -14.67 ? 1 : 0} 1 ${x} ${y}" fill="none" stroke="#00cebd" stroke-width="6"/>`;
  return `<svg viewBox="0 0 80 80" aria-hidden="true"><circle cx="40" cy="40" r="32" fill="#202830" stroke="#46525d" stroke-width="7"/>${arc}<circle cx="40" cy="40" r="26" fill="#202830" stroke="${cell.muted?"#ed5d6f":"#edf2f5"}" stroke-width="2"/><circle cx="${x}" cy="${y}" r="4" fill="#111c22" stroke="white"/>${cell.muted?'<path d="M32 38h3l5-4v12l-5-4h-3z M44 37l6 6m0-6l-6 6" fill="none" stroke="#ed5d6f" stroke-width="2"/>':`<text x="40" y="44" text-anchor="middle" fill="white" font-size="12">${fmt(cell.gainDb)}</text>`}</svg>`;
}
function inputs() {
  const r = state.routing;
  return `<div class="routing-layout"><section class="processing-column">${state.inputs.map((i) => `<div class="processing-pair">${button(`<strong>Delay</strong><span>${fmt(i.delayMs * 0.343, " m")}</span><small>${fmt(i.delayMs, " ms")}</small>`, "delay", { side: "input", channel: i.channel, value: i.delayMs }, allow(), "function-tile")}${button(`<strong>EQ</strong><span>${i.eqEnabled ? "On" : "Off"}</span>`, "eq", { side: "input", channel: i.channel }, true, "function-tile")}</div>`).join("")}</section><section class="section-panel matrix-panel"><div class="matrix">${state.inputs.map((i) => `<span>Source ${i.channel}</span>`).join("")}${state.device.outputs
    .map((o) =>
      r.cells
        .filter((c) => c.output === o.number)
        .map((c) =>
          button(
            routeFace(c),
            "route",
            {
              input: c.input,
              output: c.output,
              value: c.gainDb,
              enabled: c.muted,
            },
            allow("canSetMutes"),
            c.muted ? "muted" : "",
          ),
        )
        .join(""),
    )
    .join(
      "",
    )}</div></section><section class="processing-column output-gain-column">${state.device.outputs.map((o) => `<div class="volume-tile"><small>Volume ${esc(o.name)}</small>${button(Number(o.gainDb).toFixed(1) + " dB", "gain", { side: "output", channel: o.number, value: o.gainDb }, allow(), "value")}</div>`).join("")}</section></div>`;
}
function outputRow(o) {
  const ch = o.number, d = state.dynamics.find(item => item.channel === ch);
  const fir = state.fir.find(item => item.channel === ch);
  const eq = state.outputProcessing.find(item => item.channel === ch);
  const follower = d.bridged && ch % 2 === 0, editable = allow() && !follower;
  function tile(title, text, action, args = {}, enabled = editable) {
    return button(`<strong>${esc(title)}</strong><span>${esc(text)}</span>`,action,{channel:ch,...args},enabled,"function-tile");
  }
  const supported100V = /^DSP-(1502|1504|2002|2004|3002|3004|3302|4302)$/.test(state.device.capabilities.model);
  const modes = ["Low-Z","70V",...(!live() && supported100V ? ["100V"] : [])];
  const options = modes.map((label,i) => `<option value="${i}" ${i===d.loadMode?"selected":""}>${label}</option>`).join("");
  const unknown = !modes[d.loadMode] ? '<option selected disabled>—</option>' : "";
  return `<article class="card output-strip"><div class="signal-row">
    ${tile("Source","Routing","navigate",{tab:"Inputs"},!follower)}
    ${tile("FIR",fir.enabled?"On":"Off","fir-editor",{},!follower)}
    ${tile("EQ",eq.eqEnabled?"On":"Off","eq",{side:"output"},!follower)}
    ${tile("Trim",fmt(o.gainDb," dB"),"gain",{side:"output",value:o.gainDb})}
    ${tile("Delay",fmt(o.delayMs," ms"),"delay",{side:"output",value:o.delayMs})}
    ${tile("Polarity",o.inverted?"180°":"0°","output",{inverted:!o.inverted})}
    ${tile("Limiters","Peak / RMS","limiters",{},!follower)}
    <label class="function-tile mode-tile"><strong>Mode</strong><span>${esc(modes[d.loadMode] ?? "—")}</span><select data-mode="${ch}" aria-label="Output ${ch} mode"${dis(editable)}>${unknown}${options}</select></label>
    ${tile("Noise Gate",(d.noiseGateEnabled?"On":"Off")+" · "+fmt(d.noiseGateDb," dB"),"gate-editor")}
    <fieldset class="mute-cell"${dis(!follower)}>${mute("output",ch,o.muted)}</fieldset>
    ${tile("Output "+o.name,"Speaker","navigate",{tab:"Main"},!follower)}
    ${tile("",d.bridged?"Bridge":"Stereo","bridge",{enabled:!d.bridged},allow())}
  </div></article>`;
}
function outputs() {
  return `<div class="output-strips">${state.device.outputs.map(outputRow).join("")}</div>`;
}
const presetDrafts = new Map(), presetSlots = new Map();
function presetKey(slot) { return JSON.stringify([selected, slot]); }
function presets() {
  const slots = state.presets.slots;
  const slot = slots.find(s => s.number === presetSlots.get(selected)) ?? slots[0];
  return `<div class="presets"><section class="section-panel"><h3>On Device</h3><div class="preset-fields"><label>Preset slot<select id="preset-slot">${slots.map(s => `<option value="${s.number}" ${s === slot ? "selected" : ""}>${s.number} · ${esc(s.name)}</option>`).join("")}</select></label><label>Preset name<input id="preset-name" maxlength="32"></label><label>Current preset<span>${esc(state.presets.currentName || "—")}</span></label></div><div class="preset-actions">${button("Load from Device","recall-preset",{},!!slot && allow())}${button("Save to Device","store-preset",{},!!slot && allow())}${button("Factory Reset","factory-reset",{},allow())}${live()?button("Inspect protocol reply","inspect",{},!offline()):""}${button("Clear All Presets","clear-presets",{},slots.length > 0 && allow())}</div></section><section class="section-panel"><h3>On PC</h3>${button("Load from PC","load",{},allow())}${button("Save to PC","save")}<input id="preset-file" class="hidden" type="file" accept=".sd"></section></div>`;
}
function dialog(title, html, apply) {
  $("#editor-body").oninput = null;
  $("#editor-title").textContent = title;
  $("#editor-body").innerHTML = html;
  $("#editor-error").textContent = "";
  dialogApply = apply;
  $("#apply").disabled = !apply;
  $("#editor").showModal();
}
function number(id, label, value, min, max, step = 0.1, enabled = true) {
  return `<label>${label}<input id="${id}" type="number" inputmode="decimal" min="${min}" max="${max}" step="${step}" value="${value}"${dis(enabled)}></label>`;
}
function value(id) {
  const e = $("#" + id),
    v = Number(e.value);
  if (
    e.value.trim() === "" ||
    !Number.isFinite(v) ||
    v < Number(e.min) ||
    v > Number(e.max)
  )
    throw Error("Enter a valid value for " + id);
  return v;
}
function eq(side, ch) {
  const p = (side === "input" ? state.inputs : state.outputProcessing).find(
      (i) => i.channel === ch,
    ),
    editable = allow() && channelAvailable(side,ch);
  dialog(
    `${side === "input" ? "Input" : "Output"} ${ch} EQ`,
    `<canvas id="curve" class="plot" width="1100" height="320"></canvas><label><input id="eq-enabled" type="checkbox" role="switch" ${p.eqEnabled ? "checked" : ""}${dis(editable)}> EQ On</label><div class="bands">${[
      "highPass",
      "lowPass",
    ]
      .map((key, n) => {
        const f = p[key],
          known =
            editable &&
            f &&
            (!live() || side === "output" || state.liveCrossoverTypes.includes(f.filterType)),
          opts =
            live() && side === "input"
              ? state.crossoverOptions.filter(
                  (o) =>
                    state.liveCrossoverTypes.includes(o.code) ||
                    o.code === f?.filterType,
                )
              : state.crossoverOptions;
        return `<section class="card"><h3>${n ? "LP" : "HP"}</h3><select id="${key}-type"${dis(known)}>${opts.map((o) => `<option value="${o.code}" ${f?.filterType === o.code ? "selected" : ""}>${esc(o.label)}</option>`).join("")}</select>${number(key + "-hz", "Hz", f?.frequencyHz ?? 20, 20, 20000, 1, known)}<label><input id="${key}-on" type="checkbox" role="switch" ${f?.enabled ? "checked" : ""}${dis(known)}> On</label></section>`;
      })
      .join("")}${p.bands
      .map((b) => {
        const edit = editable && (!live() || b.filterType === 0);
        return `<section class="card"><h3>EQ ${b.number}</h3>${number("hz-" + b.number, "Hz", b.frequencyHz, 20, 20000, 1, edit)}${number("db-" + b.number, "dB", b.gainDb, -18, 18, 0.5, edit)}${number("q-" + b.number, "Q", b.q, 0.1, 20, 0.01, edit)}<label><input id="on-${b.number}" type="checkbox" role="switch" ${b.enabled ? "checked" : ""}${dis(edit)}> On</label></section>`;
      })
      .join(
        "",
      )}</div><p class="subtle">Gain (dB) · Phase (°) · 48 kHz</p><button type="button" id="reset-eq"${dis(editable)}>Reset EQ</button><button type="button" id="generate-fir">Generate FIR</button><span id="plot-status"></span>`,
    editable
      ? async () => {
          const processing = {
            eqEnabled: $("#eq-enabled").checked,
            bands: p.bands.map((b) => ({
              ...b,
              frequencyHz: value("hz-" + b.number),
              gainDb: value("db-" + b.number),
              q: value("q-" + b.number),
              enabled: $("#on-" + b.number).checked,
            })),
          };
          for (const key of ["highPass", "lowPass"]) {
            const f = p[key];
            if (f && !$("#" + key + "-type").disabled) {
              const next = {
                frequencyHz: value(key + "-hz"),
                filterType: Number($("#" + key + "-type").value),
                enabled: $("#" + key + "-on").checked,
              };
              if (JSON.stringify(next) !== JSON.stringify(f))
                processing[key] = next;
            }
          }
          await change(side === "input" ? "input" : "output-eq", {
            channel: ch,
            processing,
          });
        }
      : null,
  );
  const canvas = $("#curve");
  function draft() {
    function crossover(key) {
      return p[key] && { frequencyHz: value(key+"-hz"), filterType: Number($("#"+key+"-type").value), enabled: $("#"+key+"-on").checked };
    }
    return { enabled: $("#eq-enabled").checked, highPass: crossover("highPass"), lowPass: crossover("lowPass"),
      bands: p.bands.map(b => ({ ...b, frequencyHz: value("hz-"+b.number), gainDb: value("db-"+b.number), q: value("q-"+b.number), enabled: $("#on-"+b.number).checked })) };
  }
  let version = 0, timer;
  function redraw() {
    const v = ++version;
    clearTimeout(timer);
    timer = setTimeout(async () => {
      if (!canvas.isConnected) return;
      try {
        const points = await (await api("/api/eq-preview", draft())).json();
        if (v !== version || !canvas.isConnected) return;
        const ctx = canvas.getContext("2d"), w = canvas.width, h = canvas.height;
        ctx.clearRect(0,0,w,h);
        const left=44,right=w-48,top=18,bottom=h-29;
        const X=hz=>left+(right-left)*Math.log10(hz/20)/3;
        const Y=db=>top+(bottom-top)*(24-db)/48;
        ctx.font="12px system-ui"; ctx.setLineDash([]); ctx.lineWidth=1;
        const grid=(x1,y1,x2,y2)=>{ctx.strokeStyle="#34454f";ctx.beginPath();ctx.moveTo(x1,y1);ctx.lineTo(x2,y2);ctx.stroke();};
        for(const hz of [20,50,100,200,500,1000,2000,5000,10000,20000]){
          const x=X(hz);grid(x,top,x,bottom);ctx.textAlign="center";ctx.fillStyle="#aebbc4";ctx.fillText(hz>=1000?(hz/1000)+"k":hz,x,bottom+18);
        }
        for(let db=-24;db<=24;db+=6){
          const y=Y(db);grid(left,y,right,y);ctx.textAlign="right";ctx.fillStyle="#aebbc4";ctx.fillText(db,left-7,y+4);
          ctx.textAlign="left";ctx.fillStyle="#f0bd71";ctx.fillText((db*7.5)+"°",right+6,y+4);
        }
        for (const [field,limit,color] of [["db",24,"#00cebd"],["phase",180,"#f0bd71"]]) {
          ctx.strokeStyle=color;ctx.lineWidth=2; ctx.setLineDash(field==="db"?[]:[5,3]); ctx.beginPath();
          points.forEach((point,i) => {
            const x=left+i*(right-left)/(points.length-1), y=Y(point[field]*24/limit);
            if (!i || field==="phase" && Math.abs(point.phase-points[i-1].phase)>180) ctx.moveTo(x,y); else ctx.lineTo(x,y);
          });
          ctx.stroke();
        }
        ctx.setLineDash([]);
        for(const b of draft().bands) {
          ctx.beginPath();ctx.arc(X(b.frequencyHz),Y(b.gainDb),12,0,Math.PI*2);
          ctx.fillStyle="#182129";ctx.fill();ctx.strokeStyle=b.enabled?"#edf2f5":"#71838e";ctx.stroke();
          ctx.fillStyle="#edf2f5";ctx.textAlign="center";ctx.fillText(b.number,X(b.frequencyHz),Y(b.gainDb)+4);
        }
        $("#plot-status").textContent="";
      } catch(e) { if(v===version && canvas.isConnected) {canvas.getContext("2d").clearRect(0,0,canvas.width,canvas.height); $("#plot-status").textContent=e.message;} }
    },80);
  }
  let dragged=null;
  function graphPoint(e) {
    const box=canvas.getBoundingClientRect();
    return [(e.clientX-box.left)*canvas.width/box.width,(e.clientY-box.top)*canvas.height/box.height];
  }
  canvas.onpointerdown=e=>{
    if(!editable || e.button!==0) return;
    const [x,y]=graphPoint(e);
    dragged=draft().bands.find(b=>!$("#hz-"+b.number).disabled && Math.hypot(
      x-(44+(canvas.width-92)*Math.log10(b.frequencyHz/20)/3),
      y-(18+(canvas.height-47)*(24-b.gainDb)/48))<=17)?.number;
    if(dragged!=null){canvas.setPointerCapture(e.pointerId);e.preventDefault();}
  };
  canvas.onpointermove=e=>{
    if(dragged==null)return;
    const [x,y]=graphPoint(e);
    $("#hz-"+dragged).value=Math.round(Math.min(20000,Math.max(20,20*1000**((x-44)/(canvas.width-92)))));
    $("#db-"+dragged).value=Math.max(-18,Math.min(18,24-(y-18)*48/(canvas.height-47))).toFixed(1);
    redraw();
  };
  canvas.onlostpointercapture=()=>{dragged=null;};
  $("#editor-body").oninput=redraw;
  $("#reset-eq").onclick=()=>{
    const defaults=state.eqDefaults;
    for (const b of defaults.bands) {
      if ($("#hz-"+b.number).disabled) continue;
      $("#hz-"+b.number).value=b.frequencyHz;
      $("#db-"+b.number).value=b.gainDb;
      $("#q-"+b.number).value=b.q;
      $("#on-"+b.number).checked=p.bands.find(x=>x.number===b.number).filterType===0;
    }
    for (const key of ["highPass","lowPass"]) {
      if ($("#"+key+"-type").disabled) continue;
      $("#"+key+"-type").value=0;
      $("#"+key+"-hz").value=defaults[key].frequencyHz;
      $("#"+key+"-on").checked=false;
    }
    $("#eq-enabled").checked=true;
    redraw();
  };
  editorChannels(side,ch,n=>eq(side,n),async n=>{
    const {enabled,...filters}=draft();
    await change(side==="input"?"input":"output-eq",{channel:n,processing:{eqEnabled:enabled,...filters}});
  });
  $("#generate-fir").onclick=async () => {
    try {
      const blob=await (await api("/api/eq-fir",draft())).blob(), url=URL.createObjectURL(blob), link=document.createElement("a");
      link.href=url; link.download=side+"-"+ch+"-EQ.txt"; link.click(); setTimeout(()=>URL.revokeObjectURL(url),30000);
    } catch(e) {status(e.message);}
  };
  redraw();
}
function channelAvailable(side,channel) {
  return side==="input" || channel%2===1 || !state.dynamics.find(item=>item.channel===channel)?.bridged;
}

function editorChannels(side,current,open,copy) {
  const channels=(side==="input"?state.inputs:state.outputProcessing)
    .map(item=>item.channel).filter(n=>channelAvailable(side,n));
  const options=channels.map(n=>`<option value="${n}" ${n===current?"selected":""}>${side==="input"?n:String.fromCharCode(64+n)}</option>`).join("");
  const destinations=channels.filter(n=>n!==current).map(n=>`<option value="${n}">${side==="input"?n:String.fromCharCode(64+n)}</option>`).join("");
  $("#editor-body").insertAdjacentHTML("afterbegin",`<div class="channel-tools"><label>Channel<select id="editor-channel">${options}</select></label>
    <label>Destination<select id="copy-channel">${destinations}</select></label><button type="button" id="copy-settings"${dis(allow() && !!destinations)}>Copy settings</button></div>`);
  // Compare visible draft controls so switching also works while a value is incomplete.
  const fields=[...$("#editor-body").querySelectorAll("input, select")].filter(el=>!el.closest(".channel-tools"));
  const signature=()=>JSON.stringify(fields.map(el=>el.type==="checkbox"?el.checked:el.value));
  const initial=signature();
  $("#editor-channel").onchange=e=>{
    const next=Number(e.target.value);
    if (signature()!==initial && !confirm("Discard edits and switch channel?")) {e.target.value=current;return;}
    open(next);
  };
  $("#copy-settings").onclick=async ()=>{
    const target=Number($("#copy-channel").value);
    if (!confirm("Copy these settings to "+side+" "+target+"?")) return;
    try {await copy(target);}
    catch(e){$("#editor-error").textContent=e.message;}
  };
}

function firEditor(channel) {
  let filter=state.fir.find(item=>item.channel===channel), replaced=false;
  const editable=allow() && channelAvailable("output",channel);
  dialog("Output FIR · Channel "+channel,
    `<p id="fir-info"></p><label><input id="fir-enabled" type="checkbox" role="switch" ${filter.enabled?"checked":""}${dis(editable)}> FIR On</label>
    <div class="channel-tools"><label>Import .txt<input type="file" accept=".txt" id="fir-file"${dis(editable)}></label>
    <button type="button" id="fir-export">Export</button><button type="button" id="fir-remove"${dis(editable)}>Remove</button></div>`,
    editable?()=>replaced?change("fir-replace",{channel,filter:{...filter,enabled:$("#fir-enabled").checked}}):
      change("fir",{channel,enabled:$("#fir-enabled").checked}):null);
  function display() {
    $("#fir-info").textContent=(filter.name || "Unnamed filter")+" · "+(filter.coefficientsKnown?filter.coefficients.length+" taps":"Coefficients unavailable");
    $("#fir-export").disabled=!filter.coefficientsKnown || !filter.coefficients.length;
  }
  display();
  $("#fir-file").onchange=async e=>{
    const input=e.target,file=input.files[0];
    if (!file) return;
    try {
      if(file.size>1048576) throw Error("FIR file exceeds 1 MiB.");
      const response=await api("/api/fir-import",{name:file.name.replace(/\.txt$/i,"").slice(0,32),text:await file.text()});
      const imported=await response.json();
      if(!input.isConnected || !$("#editor").open) return;
      filter=imported;replaced=true;display();
    } catch(error){$("#editor-error").textContent=error.message;}
  };
  $("#fir-export").onclick=()=>{
    const url=URL.createObjectURL(new Blob([filter.coefficients.join("\n")+"\n"],{type:"text/plain"}));
    const link=document.createElement("a");link.href=url;link.download=(filter.name||"FIR")+".txt";link.click();
    setTimeout(()=>URL.revokeObjectURL(url),30000);
  };
  $("#fir-remove").onclick=()=>{
    filter={...filter,name:"",coefficients:[1],coefficientsKnown:true};
    replaced=true;$("#fir-enabled").checked=false;display();
  };
}

function inspectReply() {
  const functions=[[43,"FIR"],[50,"Stereo / bridge"],[51,"Channel copy"],[52,"Output EQ"],[53,"Dynamic EQ"],[54,"Peak limiter"],[55,"RMS limiter"],[57,"Speaker profile"],[59,"Preset names"],[62,"Source alignment"],[82,"RTA"],[88,"Input delay switch"],[91,"Limiter settings and load"],[93,"Input group gain"]];
  dialog("Protocol inspection",`<label>Function<select id="inspect-function">${functions.map(([n,label])=>`<option value="${n}">${n} · ${label}</option>`).join("")}</select></label>
    <label>Channel<select id="inspect-channel">${state.device.outputs.map(o=>`<option>${o.number}</option>`).join("")}</select></label>
    <label>Source<select id="inspect-source" disabled><option>Analog</option><option>Digital</option><option>AES</option></select></label>
    <button type="button" id="inspect-read">Read from device</button><p id="inspect-result"></p>`,null);
  $("#inspect-function").onchange=e=>$("#inspect-source").disabled=e.target.value!=="62";
  $("#inspect-read").onclick=async e=>{
    const read=e.target,out=$("#inspect-result"),source=$("#inspect-source");
    const params=new URLSearchParams({id:selected,function:$("#inspect-function").value,channel:$("#inspect-channel").value,segment:source.disabled?0:source.selectedIndex});
    read.disabled=true;
    try {out.textContent=await (await api("/api/inspection?"+params)).json();}
    catch(error){out.textContent=error.message;}
    finally {read.disabled=false;}
  };
}
async function action(a, d) {
  if (a === "factory-reset") {
    if (confirm("Restore factory settings on " + state.device.name + "? Current processing and stored presets may be cleared.")) {
      await change("factory-reset",{confirmed:true});
    }
    return;
  }
  if (a === "inspect") return inspectReply();
  if (a === "navigate") return document.querySelector(`[data-tab="${d.tab}"]`).click();
  if (a === "gate-editor") {
    const gate = state.dynamics.find(x => x.channel === d.channel);
    dialog("Noise gate · Output "+d.channel,
      `<label><input id="gate-enabled" type="checkbox" role="switch" ${gate.noiseGateEnabled?"checked":""}>On</label>` +
      number("gate-db","Threshold · dB",gate.noiseGateDb ?? 0,-100,0,1,gate.noiseGateDb != null),
      () => change("dynamics",{channel:d.channel,dynamics:{noiseGateEnabled:$("#gate-enabled").checked,
        ...(gate.noiseGateDb != null ? {noiseGateDb:value("gate-db")} : {})}}));
    return;
  }
  if (a === "fir-editor") {
    firEditor(d.channel);
    return;
  }
  if (a === "limiters") {
    const {rms:r,peak:p} = state.dynamics.find(x=>x.channel===d.channel);
    const toggle=(id,label,on)=>`<label><input type="checkbox" role="switch" id="${id}" ${on?"checked":""}>${label}</label>`;
    const settings=(target)=>{
      const dest=state.dynamics.find(x=>x.channel===target);
      return {
        rms:{...dest.rms,enabled:$("#r-on").checked,thresholdVolts:value("r-v"),automatic:$("#r-auto").checked,attackMs:value("r-at"),releaseMultiplier:value("r-rel")},
        peak:{...dest.peak,enabled:$("#p-on").checked,thresholdVolts:value("p-v"),holdMs:value("p-h"),releaseMs:value("p-rel")}
      };
    };
    const commit=target=>change("dynamics",{channel:target,dynamics:settings(target)});
    dialog("Limiters · Output "+d.channel,`<div class="limiter-columns"><section>${toggle("r-on","RMS On",r.enabled)}
      ${number("r-v","Threshold · Vrms",r.thresholdVolts,0,r.maximumVolts,.01)}
      ${toggle("r-auto","Automatic timing",r.automatic)}
      ${number("r-at","Attack · ms",r.attackMs,1,2000,1)}${number("r-rel","Release × attack",r.releaseMultiplier,2,32,1)}</section>
      <section>${toggle("p-on","Peak On",p.enabled)}${number("p-v","Threshold · Vpeak",p.thresholdVolts,0,p.maximumVolts,.01)}
      ${number("p-h","Hold · ms",p.holdMs,0,2000,1)}${number("p-rel","Release · ms",p.releaseMs,20,1000,1)}</section></div>`,
      allow() && channelAvailable("output",d.channel)?()=>commit(d.channel):null);
    editorChannels("output",d.channel,n=>action("limiters",{channel:n}),commit);
    const timing=()=>{ $("#r-at").disabled=$("#r-rel").disabled=$("#r-auto").checked; };
    $("#r-auto").onchange=timing; timing(); return;
  }
  if (a === "bridge") {
    const first = Math.floor((d.channel - 1) / 2) * 2 + 1;
    if (!confirm("Switch outputs " + String.fromCharCode(64+first) + "/" + String.fromCharCode(65+first) + " to " + (d.enabled ? "Bridge" : "Stereo") + "?")) return;
    return change("dynamics", { channel:first, dynamics:{bridged:d.enabled} });
  }
  if (a === "mute")
    return change(
      d.side === "input" ? "input" : "output",
      d.side === "input"
        ? { channel: d.channel, processing: { muted: d.enabled } }
        : { channel: d.channel, enabled: d.enabled },
    );
  if (a === "gain") {
    const input = d.side === "input";
    dialog(input ? "Input gain" : "Output trim",
      number("gain-db", "Gain · dB", d.value, -80, 18, 0.5),
      () => change(input ? "input" : "output", input
        ? { channel: d.channel, processing: { gainDb: value("gain-db") } }
        : { channel: d.channel, value: value("gain-db") }));
    $("#gain-db").focus();
    $("#gain-db").select();
    return;
  }
  if (a === "eq") return eq(d.side, d.channel);
  if (a === "delay") {
    dialog(
      "Delay",
      number("delay-ms", "Time · ms", d.value, 0, 100, 0.01) +
        number("delay-m", "Distance · m", d.value * 0.343, 0, 34.3, 0.001),
      () =>
        change(
          d.side === "input" ? "input" : "output",
          d.side === "input"
            ? { channel: d.channel, processing: { delayMs: value("delay-ms") } }
            : { channel: d.channel, delayMs: value("delay-ms") },
        ),
    );
    $("#delay-ms").oninput = () =>
      ($("#delay-m").value = Number($("#delay-ms").value) * 0.343);
    $("#delay-m").oninput = () =>
      ($("#delay-ms").value = Number($("#delay-m").value) / 0.343);
    return;
  }
  if (a === "route") {
    dialog(
      `Input ${d.input} → Output ${d.output}`,
      `<input id="route-gain" type="range" min="-80" max="18" step="0.5" value="${d.value}"><output id="route-value">${d.value} dB</output><label><input id="route-muted" type="checkbox" role="switch" ${d.enabled ? "checked" : ""}> Mute</label>`,
      () =>
        change("route", {
          input: d.input,
          output: d.output,
          value: Number($("#route-gain").value),
          enabled: $("#route-muted").checked,
        }),
    );
    $("#route-gain").oninput = () =>
      ($("#route-value").textContent = $("#route-gain").value + " dB");
    return;
  }
  if (a === "gate") {
    dialog("Noise gate", number("gate-threshold", "Threshold · dB", d.value, -100, 0, 1),
      () => change("dynamics", {channel: d.channel, dynamics: {noiseGateDb: value("gate-threshold")}}));
    return;
  }
  if (a === "rename") {
    dialog("Device name", '<label>Name<input id="device-name-input" type="text"></label>',
      () => change("rename", { name: $("#device-name-input").value }));
    const field = $("#device-name-input");
    field.value = state.device.name;
    field.focus();
    field.select();
    return;
  }
  if (a === "standby" && d.enabled && !confirm("Put the amplifier in standby?"))
    return;
  if (a === "save") {
    const blob = await (
        await api("/api/preset?id=" + encodeURIComponent(selected))
      ).blob(),
      url = URL.createObjectURL(blob),
      link = document.createElement("a");
    link.href = url;
    link.download = ($("#preset-name")?.value.trim() || "CVR-preset") + ".sd";
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 30000);
    return;
  }
  if (a === "load") return $("#preset-file").click();
  if (a.endsWith("-preset") || a === "clear-presets") {
    if (!confirm("Confirm " + a.replaceAll("-", " ") + "?")) return;
    return change(a, {
      slot: Number($("#preset-slot")?.value || 1),
      name:
        a === "store-preset"
          ? $("#preset-name").value.trim()
          : null,
      confirmed: true,
    });
  }
  return change(a, d);
}
// Gain gestures preview immediately and send the latest position without replay after failure.
function cancelQueuedGains() {
  faderPointers.clear();
  gainQueue.clear();
  gainActive.clear();
  if (gainTimer !== null) clearTimeout(gainTimer);
  gainTimer = null;
}
function gainKey(device, channel) {
  return device + ":" + channel;
}
function paintGain(slider, value) {
  const control = slider.closest(".gain-control"),
    readout = control?.querySelector(".value");
  if (readout) {
    readout.textContent = Number(value).toFixed(1) + " dB";
    const args = JSON.parse(readout.dataset.args);
    args.value = value;
    readout.dataset.args = JSON.stringify(args);
  }
}
function scheduleGain(delay = 100) {
  if (gainTimer !== null || gainSending || !gainQueue.size) return;
  gainTimer = setTimeout(drainGain, delay);
}
function queueInputGain(slider) {
  if (!connectionReady() || !allow("canSetInputGain")) return;
  const channel = Number(slider.dataset.channel),
    value = Number(slider.value),
    id = gainKey(selected, channel);
  gainActive.add(id);
  paintGain(slider, value);
  if (gainCurrent?.id === id && gainCurrent.value === value) {
    gainQueue.delete(id);
    return;
  }
  if (
    gainCurrent?.id !== id &&
    state.inputs.find((i) => i.channel === channel)?.gainDb === value
  ) {
    gainQueue.delete(id);
    return;
  }
  gainQueue.set(id, {
    id,
    deviceId: selected,
    channel,
    value,
    epoch: accessEpoch,
    generation: stateGeneration,
  });
  scheduleGain();
}
async function drainGain() {
  gainTimer = null;
  if (!connectionReady()) {
    cancelQueuedGains();
    return;
  }
  if (busy) {
    scheduleGain();
    return;
  }
  const entry = gainQueue.values().next().value;
  if (!entry) return;
  gainQueue.delete(entry.id);
  if (
    entry.epoch !== accessEpoch ||
    entry.generation !== stateGeneration ||
    entry.deviceId !== selected
  ) {
    scheduleGain();
    return;
  }
  gainSending = true;
  gainCurrent = entry;
  try {
    const response = await api("/api/change", {
      deviceId: entry.deviceId,
      operation: "input",
      channel: entry.channel,
      processing: { gainDb: entry.value },
    });
    const confirmed = await response.json();
    if (
      entry.epoch === accessEpoch &&
      entry.generation === stateGeneration &&
      entry.deviceId === selected
    ) {
      const input = state.inputs.find((i) => i.channel === entry.channel);
      if (input) input.gainDb = confirmed.gainDb;
      if (!gainQueue.size) status("Gain confirmed");
    }
  } catch (e) {
    cancelQueuedGains();
    status(e.message, true);
  } finally {
    gainSending = false;
    gainCurrent = null;
    if (gainQueue.size) scheduleGain();
    else if (!gainActive.size && connectionReady()) {
      try {
        await refresh(true);
      } catch (e) {
        status(e.message, true);
      }
    }
  }
}
function finishGain(slider) {
  if (slider?.dataset.gain !== "input") return;
  gainActive.delete(gainKey(selected, Number(slider.dataset.channel)));
  if (gainTimer !== null) {
    clearTimeout(gainTimer);
    gainTimer = null;
  }
  if (gainQueue.size) scheduleGain(0);
}
async function selectDevice(id) {
  cancelQueuedGains();
  stateGeneration++;
  selected = id;
  stateFresh = false;
  renderConnection();
  if (refreshInFlight) await refreshInFlight.catch(() => {});
  await refresh(true);
}
// Map touch/mouse Y directly to gain. Keep pointer capture during a drag outside the track.
const faderPointers = new Map();
function dragGain(event, slider) {
  const rect = slider.getBoundingClientRect(), inset = Math.min(13, rect.height / 4);
  const fraction = Math.max(0, Math.min(1, (rect.bottom - inset - event.clientY) / (rect.height - 2 * inset)));
  slider.value = String(Math.round((-80 + 98 * fraction) * 2) / 2);
  queueInputGain(slider);
}
document.addEventListener("pointerdown", e => {
  const slider=e.target;
  if (slider.dataset.gain !== "input" || slider.disabled || !connectionReady() || e.button !== 0) return;
  e.preventDefault(); slider.focus({preventScroll:true}); slider.setPointerCapture(e.pointerId);
  faderPointers.set(e.pointerId,slider); dragGain(e,slider);
});
document.addEventListener("pointermove", e => {
  const slider=faderPointers.get(e.pointerId);
  if(slider && slider.isConnected) dragGain(e,slider);
});
for(const type of ["pointerup","pointercancel","lostpointercapture"])
  document.addEventListener(type,e=>{
    const slider=faderPointers.get(e.pointerId);
    if(slider){faderPointers.delete(e.pointerId); finishGain(slider);}
  });
document.addEventListener("input", (e) => {
  if (!e.target.dataset.gain) return;
  paintGain(e.target, Number(e.target.value));
  if (e.target.dataset.gain === "input") queueInputGain(e.target);
});
for (const event of ["pointerup", "pointercancel", "keyup", "focusout"])
  document.addEventListener(event, (e) => finishGain(e.target));
window.addEventListener("pagehide", () => {
  cancelQueuedGains();
  if (key)
    fetch("/api/disconnect", {
      method: "POST",
      headers: {
        Authorization: "Bearer " + key,
        "X-CVR-Client": browserClientId,
      },
      keepalive: true,
    }).catch(() => {});
});
document.addEventListener("click", async (e) => {
  const device = e.target.closest("[data-device]");
  if (device) {
    try {
      await selectDevice(device.dataset.device);
    } catch (error) {
      status(error.message, true);
    }
    return;
  }
  const b = e.target.closest("[data-action]");
  if (!b) return;
  try {
    await action(b.dataset.action, JSON.parse(b.dataset.args));
  } catch (e) {
    status(e.message, true);
  }
});
document.addEventListener("change", async (e) => {
  try {
    const t = e.target;
    if (t.dataset.gain === "input") {
      finishGain(t);
    }
    if (t.dataset.gain && t.dataset.gain !== "input") {
      const ch = Number(t.dataset.channel),
        v = Number(t.value);
      await change(
        t.dataset.gain === "input" ? "input" : "output",
        t.dataset.gain === "input"
          ? { channel: ch, processing: { gainDb: v } }
          : { channel: ch, value: v },
      );
    }
    if (t.dataset.mode)
      await change("dynamics", {
        channel: Number(t.dataset.mode),
        dynamics: { loadMode: Number(t.value) },
      });
    if (t.id === "preset-file" && t.files[0]) {
      const f = t.files[0];
      if (!f.name.toLowerCase().endsWith(".sd") || f.size > 131072)
        throw Error("Choose a native .sd preset.");
      if (confirm("Load this .sd preset?")) {
        const bytes = new Uint8Array(await f.arrayBuffer());
        let data = "";
        for (const b of bytes) data += String.fromCharCode(b);
        await change("load-preset", { data: btoa(data), confirmed: true });
      }
    }
  } catch (e) {
    status(e.message, true);
  }
});

$("#apply").onclick = async () => {
  if (!dialogApply || !connectionReady()) return;
  $("#apply").disabled = true;
  try {
    await dialogApply();
    $("#editor").close();
    await refresh(true);
  } catch (e) {
    $("#editor-error").textContent = e.message;
  } finally {
    $("#apply").disabled = false;
  }
};
document.querySelectorAll("[data-tab]").forEach(
  (b) =>
    (b.onclick = () => {
      tab = b.dataset.tab;
      document
        .querySelectorAll("[data-tab]")
        .forEach((x) => x.classList.toggle("active", x === b));
      if (state) render();
    }),
);
$("#devices").onchange = async () => {
  try {
    await selectDevice($("#devices").value);
  } catch (e) {
    status(e.message, true);
  }
};
$("#disconnect").onclick = () => {
  const oldKey = key;
  useAccessKey("");
  sessionStorage.removeItem("cvr-key");
  fetch("/api/disconnect", {
    method: "POST",
    headers: {
      Authorization: "Bearer " + oldKey,
      "X-CVR-Client": browserClientId,
    },
    keepalive: true,
  }).catch(() => {});
};
$("#connect").onclick = async () => {
  const entered = prompt("Access key from the desktop app", key);
  if (entered == null) return;
  useAccessKey(entered);
};
window.addEventListener("offline", () => loseConnection());
window.addEventListener("online", () => heartbeat());
window.addEventListener("hashchange", () => {
  if (!location.hash) return;
  const nextKey = location.hash.slice(1);
  history.replaceState(null, "", location.pathname);
  useAccessKey(nextKey);
});
document.addEventListener("visibilitychange", () => {
  if (document.visibilityState !== "visible") return;
  connectionReady();
  heartbeat();
});
renderConnection();
heartbeat();
setInterval(heartbeat, heartbeatIntervalMs);
setInterval(() => connectionReady(), 500);
setInterval(() => refresh().catch((e) => status(e.message, true)), 2000);

const appearance = $("#appearance");
const systemLight = matchMedia("(prefers-color-scheme: light)");
function applyAppearance() {
  const light = appearance.value === "Light" || (appearance.value === "System" && systemLight.matches);
  document.documentElement.dataset.theme = light ? "light" : "dark";
}
try { appearance.value = localStorage.getItem("cvr-appearance") || "System"; } catch {}
appearance.onchange = () => {
  applyAppearance();
  try { localStorage.setItem("cvr-appearance", appearance.value); } catch {}
};
systemLight.addEventListener("change", applyAppearance);
applyAppearance();
