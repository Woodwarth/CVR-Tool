// Web / Touch controls
// Module: Serves the local browser interface and routes authenticated requests to the shared session.
// Purpose: render English local browser controls from the shared Application state.
// Units: dB/ms/metres/Hz/Q; Peak response preview explicitly uses 48 kHz.
// State: drafts remain local; an independent heartbeat pauses writes on connection loss.
// Scope: no direct amplifier transport, external scripts or factory-reset command.
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
    connected: ["Connected to desktop app", "Browser controls are up to date."],
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
async function refresh(force = false) {
  if (!key) return;
  if (
    !force &&
    (busy ||
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
    renderDeviceList(devices);
    if (!selected) {
      state = null;
      $("#identity").textContent = "No devices available.";
      $("#page").replaceChildren();
      stateFresh = true;
      renderConnection();
      return;
    }
    state = await (
      await api("/api/state?id=" + encodeURIComponent(selected))
    ).json();
    if (epoch !== accessEpoch || generation !== stateGeneration) return;
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
        : Math.max(0, Math.min(100, ((db + 60) / 60) * 100)) + "%";
  }
}
function meter(db = null) {
  return `<div class="meter" data-db="${db ?? ""}" aria-label="${db == null ? "Level unknown" : fmt(db, " dBFS")}"><div class="meter-fill"></div>${db == null ? "<span>—</span>" : ""}</div>`;
}
function clip(value) {
  return `<span class="clip"><i class="led ${value === true ? "bad" : value === false ? "" : "unknown"}"></i>Clip</span>`;
}
function gain(side, ch, value, vertical = false) {
  const enabled =
    allow(side === "input" ? "canSetInputGain" : "canSetVerifiedDsp") &&
    value != null;
  const readout = button(
    value == null ? "—" : Number(value).toFixed(1) + " dB",
    "gain",
    { side, channel: ch, value },
    enabled,
    "value",
  );
  return `<div class="gain-control ${vertical ? "vertical-gain" : ""}" data-gain-side="${side}" data-gain-channel="${ch}"><div class="fader-row"><input aria-label="${side} ${ch} gain" type="range" min="-80" max="18" step="0.5" value="${value ?? 0}" data-gain="${side}" data-channel="${ch}"${dis(enabled)}>${vertical ? meter() : ""}</div>${vertical ? clip(null) : ""}${readout}</div>`;
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
  ]
    .map(
      ([k, label]) =>
        `<span><i class="led ${s?.[k] === true ? "bad" : s?.[k] === false ? "" : "unknown"}"></i>${label}</span>`,
    )
    .join("")}</div>`;
}
function main() {
  return `<div class="main-panels"><section class="section-panel"><h2>Inputs</h2><div class="channel-grid">${state.inputs.map((i) => `<article class="card channel-card"><h3>${String.fromCharCode(64 + i.channel)}</h3>${gain("input", i.channel, i.gainDb, true)}${mute("input", i.channel, i.muted)}</article>`).join("")}</div></section><section class="section-panel"><h2>Outputs</h2><div class="channel-grid">${state.device.outputs.map((o) => `<article class="card channel-card"><h3>${esc(o.name)}</h3><div class="output-meter-stage">${meter(state.telemetry.find((t) => t.channel === o.number)?.levelDbFs)}${flags(o.number)}</div>${mute("output", o.number, o.muted)}${measurements(o.number)}</article>`).join("")}</div></section></div>`;
}
function inputs() {
  const r = state.routing;
  return `<div class="routing-layout"><section class="processing-column"><h3>Input processing</h3>${state.inputs.map((i) => `<div class="processing-pair">${button(`<strong>Delay</strong><span>${fmt(i.delayMs * 0.343, " m")}</span><small>${fmt(i.delayMs, " ms")}</small>`, "delay", { side: "input", channel: i.channel, value: i.delayMs }, allow(), "function-tile")}${button(`<strong>EQ</strong><span>${i.eqEnabled ? "On" : "Off"}</span>`, "eq", { side: "input", channel: i.channel }, true, "function-tile")}</div>`).join("")}</section><section class="section-panel matrix-panel"><div class="matrix">${state.inputs.map((i) => `<span>Source ${i.channel}</span>`).join("")}${state.device.outputs
    .map((o) =>
      r.cells
        .filter((c) => c.output === o.number)
        .map((c) =>
          button(
            c.muted ? symbol("mute") : fmt(c.gainDb),
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
    )}</div></section><section class="processing-column output-gain-column"><h3>Output gain</h3>${state.device.outputs.map((o) => `<div class="volume-tile"><small>Volume ${esc(o.name)}</small>${button(Number(o.gainDb).toFixed(1) + " dB", "gain", { side: "output", channel: o.number, value: o.gainDb }, allow(), "value")}</div>`).join("")}</section></div>`;
}
function outputs() {
  return `<div class="output-strips">${state.device.outputs
    .map((o) => {
      const d = state.dynamics.find((d) => d.channel === o.number),
        f = state.fir.find((f) => f.channel === o.number);
      return `<article class="card output-strip"><div class="output-heading"><h3>Output ${esc(o.name)}</h3>${mute("output", o.number, o.muted)}</div><div class="function-grid">${button(`<strong>Trim</strong><span>${Number(o.gainDb).toFixed(1)} dB</span>`, "gain", { side: "output", channel: o.number, value: o.gainDb }, allow(), "function-tile")}${button(`<strong>Delay</strong><span>${fmt(o.delayMs, " ms")}</span><small>${fmt(o.delayMs * 0.343, " m")}</small>`, "delay", { side: "output", channel: o.number, value: o.delayMs }, allow(), "function-tile")}${button(`<strong>EQ</strong><span>Output EQ</span>`, "eq", { side: "output", channel: o.number }, true, "function-tile")}${button(`<strong>Polarity</strong><span>${o.inverted ? "180°" : "0°"}</span>`, "output", { channel: o.number, inverted: !o.inverted }, allow(), "function-tile")}<label class="function-tile"><strong>Mode</strong><select data-mode="${o.number}"${dis(allow())}>${d.loadMode == null || (live() && ![0, 1].includes(d.loadMode)) ? `<option value="" selected disabled>${esc(d.loadMode == null ? "Unknown" : ["Low-Z", "70V", "100V", "BRDG Low-Z", "BRDG 70V", "BRDG 100V"][d.loadMode] || "Unknown")}</option>` : ""}${["Low-Z", "70V", ...(!live() ? ["100V"] : [])].map((s, n) => `<option value="${n}" ${d.loadMode === n ? "selected" : ""}>${s}</option>`).join("")}</select></label>${button(`<strong>Noise gate</strong><span>${d.noiseGateEnabled ? "On" : "Off"}</span>`, "dynamics", { channel: o.number, dynamics: { noiseGateEnabled: !d.noiseGateEnabled } }, allow(), "function-tile")}${button(`<strong>Gate threshold</strong><span>${fmt(d.noiseGateDb, " dB")}</span>`, "gate", { channel: o.number, value: d.noiseGateDb }, allow() && d.noiseGateDb != null, "function-tile")}${button(`<strong>FIR</strong><span>${f.enabled ? "On" : "Off"}</span>`, "fir", { channel: o.number, enabled: !f.enabled }, allow(), "function-tile")}</div><details><summary>Status and limiter settings</summary>${measurements(o.number)}${flags(o.number)}<p>RMS ${fmt(d.rms.thresholdVolts, " V")} · ${d.rms.enabled ? "On" : "Off"} / Peak ${fmt(d.peak.thresholdVolts, " V")} · ${d.peak.enabled ? "On" : "Off"}</p><small>${f.coefficientsKnown ? f.coefficients.length + " FIR taps" : "FIR coefficients unknown"} · Limiter/Bridge editing is available in Desktop when supported.</small></details></article>`;
    })
    .join("")}</div>`;
}
function presets() {
  return `<div class="presets">${button("Save to PC / iPad (.sd)", "save")}${button("Load from PC / iPad (.sd)", "load", {}, !live())}<input id="preset-file" class="hidden" type="file" accept=".sd"><h3>Device presets</h3>${state.presets.slots.length ? `<select id="preset-slot">${state.presets.slots.map((s) => `<option value="${s.number}">${s.number} · ${esc(s.name || "Empty")}</option>`).join("")}</select>${button("Load from device", "recall-preset", {}, !live())}${button("Save to device", "store-preset", {}, !live())}${button("Clear all presets", "clear-presets", {}, !live())}` : "<p>Device preset slots have not been read.</p>"}<p class="subtle">Factory reset is unavailable in browser control.</p></div>`;
}
function dialog(title, html, apply) {
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
    editable = allow() && (side === "input" || !live());
  dialog(
    `${side === "input" ? "Input" : "Output"} ${ch} EQ`,
    `<canvas id="curve" class="plot" width="850" height="180"></canvas><label><input id="eq-enabled" type="checkbox" ${p.eqEnabled ? "checked" : ""}${dis(editable)}> EQ On</label><div class="bands">${[
      "highPass",
      "lowPass",
    ]
      .map((key, n) => {
        const f = p[key],
          known =
            editable &&
            f &&
            (!live() || state.liveCrossoverTypes.includes(f.filterType)),
          opts =
            live() && side === "input"
              ? state.crossoverOptions.filter(
                  (o) =>
                    state.liveCrossoverTypes.includes(o.code) ||
                    o.code === f?.filterType,
                )
              : state.crossoverOptions;
        return `<section class="card"><h3>${n ? "LP" : "HP"}</h3><select id="${key}-type"${dis(known)}>${opts.map((o) => `<option value="${o.code}" ${f?.filterType === o.code ? "selected" : ""}>${esc(o.label)}</option>`).join("")}</select>${number(key + "-hz", "Hz", f?.frequencyHz ?? 20, 20, 20000, 1, known)}<label><input id="${key}-on" type="checkbox" ${f?.enabled ? "checked" : ""}${dis(known)}> On</label></section>`;
      })
      .join("")}${p.bands
      .map((b) => {
        const edit = editable && (!live() || b.filterType === 0);
        return `<section class="card"><h3>EQ ${b.number}</h3>${number("hz-" + b.number, "Hz", b.frequencyHz, 20, 20000, 1, edit)}${number("db-" + b.number, "dB", b.gainDb, -18, 18, 0.5, edit)}${number("q-" + b.number, "Q", b.q, 0.1, 20, 0.01, edit)}<label><input id="on-${b.number}" type="checkbox" ${b.enabled ? "checked" : ""}${dis(edit)}> On</label></section>`;
      })
      .join(
        "",
      )}</div><p class="subtle">48 kHz Peak-EQ preview. HP/LP curves are not included here. Unknown device filter types are not plotted.</p>`,
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
  $("#editor-body").addEventListener("input", plot, { once: true });
  plot();
}
function plot() {
  const canvas = $("#curve");
  if (!canvas) return;
  const ctx = canvas.getContext("2d");
  ctx.clearRect(0, 0, 850, 180);
  ctx.strokeStyle = "#34454f";
  for (let y = 0; y <= 180; y += 30) {
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(850, y);
    ctx.stroke();
  }
  ctx.strokeStyle = "#00cebd";
  ctx.beginPath();
  const p = (tab === "Outputs" ? state.outputProcessing : state.inputs).find(
    (i) =>
      i.channel === Number($("#editor-title").textContent.match(/\d+/)?.[0]),
  );
  if (!p) return;
  for (let x = 0; x <= 850; x++) {
    const f = 20 * Math.pow(1000, x / 850),
      w = (2 * Math.PI * f) / 48000;
    let db = 0;
    if ($("#eq-enabled").checked)
      for (const b of p.bands) {
        if (b.filterType !== 0 || !$("#on-" + b.number).checked) continue;
        const hz = Number($("#hz-" + b.number).value),
          gain = Number($("#db-" + b.number).value),
          q = Number($("#q-" + b.number).value),
          A = Math.pow(10, gain / 40),
          wc = (2 * Math.PI * hz) / 48000,
          alpha = Math.sin(wc) / (2 * q),
          co = Math.cos(wc);
        const mag = (a, b, c) =>
          Math.hypot(
            a + b * Math.cos(w) + c * Math.cos(2 * w),
            -b * Math.sin(w) - c * Math.sin(2 * w),
          );
        db +=
          20 *
          Math.log10(
            mag(1 + alpha * A, -2 * co, 1 - alpha * A) /
              mag(1 + alpha / A, -2 * co, 1 - alpha / A),
          );
      }
    const y = 90 - Math.max(-24, Math.min(24, db)) * 3.75;
    if (x === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  }
  ctx.stroke();
}
async function action(a, d) {
  if (a === "mute")
    return change(
      d.side === "input" ? "input" : "output",
      d.side === "input"
        ? { channel: d.channel, processing: { muted: d.enabled } }
        : { channel: d.channel, enabled: d.enabled },
    );
  if (a === "gain") {
    const s = prompt("Gain in dB · −80 to +18", d.value);
    if (s == null) return;
    const v = Number(s.replace(",", "."));
    if (s.trim() === "" || !Number.isFinite(v))
      throw Error("Enter a gain in dB.");
    return change(
      d.side === "input" ? "input" : "output",
      d.side === "input"
        ? { channel: d.channel, processing: { gainDb: v } }
        : { channel: d.channel, value: v },
    );
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
      `<input id="route-gain" type="range" min="-80" max="18" step=".1" value="${d.value}"><output id="route-value">${d.value} dB</output><label><input id="route-muted" type="checkbox" ${d.enabled ? "checked" : ""}> Mute</label>`,
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
    const v = prompt("Gate threshold · −100 to 0 dB", d.value);
    if (v == null) return;
    return change("dynamics", {
      channel: d.channel,
      dynamics: { noiseGateDb: Number(v.replace(",", ".")) },
    });
  }
  if (a === "rename") {
    const name = prompt("Device name", state.device.name);
    if (name != null) return change("rename", { name });
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
    link.download = "CVR-preset.sd";
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
          ? prompt("Preset name", "Preset") || "Preset"
          : null,
      confirmed: true,
    });
  }
  return change(a, d);
}
// Gain gestures preview immediately and send the latest position without replay after failure.
function cancelQueuedGains() {
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
document.addEventListener("pointerdown", (e) => {
  if (e.target.dataset.gain === "input" && connectionReady())
    gainActive.add(gainKey(selected, Number(e.target.dataset.channel)));
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
$("#editor-body").addEventListener("input", () => plot());
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
