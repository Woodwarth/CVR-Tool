// Web / Touch controls
// Purpose: render English local browser controls from the shared Application state.
// Units: dB/ms/metres/Hz/Q; Peak response preview explicitly uses 48 kHz.
// State: drafts remain local until Apply; polling pauses during editing.
// Scope: no direct amplifier transport, external scripts or factory-reset command.
"use strict";
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
  dialogApply = null;
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
async function api(path, body) {
  const r = await fetch(path, {
    method: body ? "POST" : "GET",
    headers: {
      Authorization: "Bearer " + key,
      ...(body ? { "Content-Type": "application/json" } : {}),
    },
    body: body ? JSON.stringify(body) : undefined,
    cache: "no-store",
  });
  if (!r.ok) {
    let e;
    try {
      e = await r.json();
    } catch {}
    throw Error(
      e?.error ||
        (r.status === 401
          ? "Enter the access key from the desktop app."
          : "Request failed: " + r.status),
    );
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
  if (busy) return;
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
      $("#editor").open ||
      document.activeElement?.matches("input,select"))
  )
    return;
  const devices = await (await api("/api/devices")).json();
  if (!devices.some((d) => d.id === selected)) selected = devices[0]?.id || "";
  $("#devices").innerHTML = devices
    .map(
      (d) =>
        `<option value="${esc(d.id)}" ${d.id === selected ? "selected" : ""}>${esc(d.name)} · ${esc(d.capabilities.model)}</option>`,
    )
    .join("");
  if (!selected) return;
  state = await (
    await api("/api/state?id=" + encodeURIComponent(selected))
  ).json();
  render();
}
function button(label, action, data = {}, enabled = true, cls = "") {
  return `<button data-action="${action}" data-args="${esc(JSON.stringify(data))}" class="${cls}"${dis(enabled)}>${label}</button>`;
}
function render() {
  const d = state.device,
    c = state.controls;
  $("#identity").innerHTML =
    `<div class="identity"><h2><i class="led ${offline() ? "bad" : "good"}"></i>${esc(d.name)}</h2><small>${esc(d.capabilities.model)} · ${d.capabilities.outputCount} channels</small>${button("Rename", "rename", {}, allow("canRename"))}${button(c.displayLocked ? "🔒" : "🔓", "lock", { enabled: !c.displayLocked }, allow("canSetDisplayLock"))}${button("⏻ Standby", "standby", { enabled: !c.standby }, allow("canSetStandby") && c.standbyKnown, c.standby ? "on" : "")}${button("✋ Wink", "wink", { enabled: !c.wink }, allow("canSetWink") && c.wink != null, c.wink ? "on" : "")}</div><small>Operating time: ${d.readings?.operatingMinutes == null ? "—" : Math.floor(d.readings.operatingMinutes / 60) + " h " + (d.readings.operatingMinutes % 60) + " min"}</small>`;
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
  if (matrix)
    matrix.style.gridTemplateColumns = `repeat(${state.routing.inputCount + 1}, 1fr)`;
}
function gain(side, ch, value) {
  const capability = side === "input" ? "canSetInputGain" : "canSetVerifiedDsp";
  return `<button class="value" data-action="gain" data-args="${esc(JSON.stringify({ side, channel: ch, value }))}"${dis(allow(capability) && value != null)}>${fmt(value, " dB")}</button><input aria-label="${side} ${ch} gain" type="range" min="-80" max="18" step="0.5" value="${value ?? 0}" data-gain="${side}" data-channel="${ch}"${dis(allow(capability) && value != null)}>`;
}
function mute(side, ch, muted) {
  return button(
    muted ? "🔇" : "🔊",
    "mute",
    { side, channel: ch, enabled: !muted },
    allow(side === "input" ? "canSetInputMute" : "canSetMutes") &&
      muted != null,
    muted ? "muted" : "",
  );
}
function telemetry(ch) {
  const t = state.telemetry.find((t) => t.channel === ch),
    s = t?.status;
  return `<div class="telemetry"><span>Voltage</span><span>${fmt(t?.voltage, " V")}</span><span>Current</span><span>${fmt(t?.current, " A")}</span><span>Load</span><span>${fmt(t?.loadOhms, " Ω")}</span><span>Temperature</span><span>${fmt(t?.temperatureC, " °C")}</span></div><div class="flags">${[
    ["fault", "Fault"],
    ["load", "Load"],
    ["open", "Open"],
    ["overTemperature", "Temp"],
    ["clip", "Clip"],
    ["standby", "Standby"],
    ["highImpedance", "Hi-Z"],
    ["bridged", "Bridge"],
  ]
    .map(
      ([key, label]) =>
        `<span><i class="led ${s?.[key] === true ? "bad" : s?.[key] === false ? "good" : ""}"></i>${label} ${s?.[key] == null ? "—" : s[key] ? "On" : "Off"}</span>`,
    )
    .join("")}</div>`;
}
function main() {
  return `<h2>Inputs</h2><div class="grid">${state.inputs.map((i) => `<article class="card"><h3>Input ${String.fromCharCode(64 + i.channel)}</h3>${gain("input", i.channel, i.gainDb)}${mute("input", i.channel, i.muted)}<small>Input level / clip: —</small></article>`).join("")}</div><h2>Outputs</h2><div class="grid">${state.device.outputs.map((o) => `<article class="card"><h3>Output ${esc(o.name)}</h3>${mute("output", o.number, o.muted)}${telemetry(o.number)}</article>`).join("")}</div>`;
}
function inputs() {
  const r = state.routing;
  return `<div class="grid">${state.inputs.map((i) => `<article class="card"><h3>Input ${i.channel}</h3>${button("Delay · " + fmt(i.delayMs, " ms") + " / " + fmt(i.delayMs * 0.343, " m"), "delay", { side: "input", channel: i.channel, value: i.delayMs }, allow())}${button("EQ · " + (i.eqEnabled ? "On" : "Off"), "eq", { side: "input", channel: i.channel })}</article>`).join("")}</div><h2>Routing</h2><div class="matrix"><span></span>${state.inputs.map((i) => `<span>In ${i.channel}</span>`).join("")}${state.device.outputs
    .map(
      (o) =>
        `<span>Out ${esc(o.name)}</span>${r.cells
          .filter((c) => c.output === o.number)
          .map((c) =>
            button(
              c.muted ? "🔇" : fmt(c.gainDb),
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
          .join("")}`,
    )
    .join(
      "",
    )}</div><h2>Output gain</h2><div class="grid">${state.device.outputs.map((o) => `<article class="card"><h3>Output ${esc(o.name)}</h3>${gain("output", o.number, o.gainDb)}</article>`).join("")}</div>`;
}
function outputs() {
  return `<div class="grid">${state.device.outputs
    .map((o) => {
      const d = state.dynamics.find((d) => d.channel === o.number),
        f = state.fir.find((f) => f.channel === o.number);
      return `<article class="card"><h3>Output ${esc(o.name)}</h3>${gain("output", o.number, o.gainDb)}${mute("output", o.number, o.muted)}${button("Delay · " + fmt(o.delayMs, " ms"), "delay", { side: "output", channel: o.number, value: o.delayMs }, allow())}${button("Polarity · " + (o.inverted ? "180°" : "0°"), "output", { channel: o.number, inverted: !o.inverted }, allow())}${button("EQ", "eq", { side: "output", channel: o.number })}<label>Mode<select data-mode="${o.number}"${dis(allow())}>${d.loadMode == null || (live() && ![0, 1].includes(d.loadMode)) ? `<option value="" selected disabled>${esc(d.loadMode == null ? "Unknown" : ["Low-Z", "70V", "100V", "BRDG Low-Z", "BRDG 70V", "BRDG 100V"][d.loadMode] || "Unknown")}</option>` : ""}${["Low-Z", "70V", ...(!live() ? ["100V"] : [])].map((s, n) => `<option value="${n}" ${d.loadMode === n ? "selected" : ""}>${s}</option>`).join("")}</select></label>${button("Noise gate · " + (d.noiseGateEnabled ? "On" : "Off"), "dynamics", { channel: o.number, dynamics: { noiseGateEnabled: !d.noiseGateEnabled } }, allow())}${button("Gate threshold · " + fmt(d.noiseGateDb, " dB"), "gate", { channel: o.number, value: d.noiseGateDb }, allow() && d.noiseGateDb != null)}${button("FIR · " + (f.enabled ? "On" : "Off"), "fir", { channel: o.number, enabled: !f.enabled }, allow())}<small>${f.coefficientsKnown ? f.coefficients.length + " FIR taps" : "FIR coefficients unknown"}</small><details><summary>Limiter settings</summary><p>RMS: ${fmt(d.rms.thresholdVolts, " V")} · ${d.rms.enabled ? "On" : "Off"}</p><p>Peak: ${fmt(d.peak.thresholdVolts, " V")} · ${d.peak.enabled ? "On" : "Off"}</p><small>Limiter and Stereo/Bridge editing are available in Desktop when supported.</small></details>${telemetry(o.number)}</article>`;
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
document.addEventListener("click", async (e) => {
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
    if (t.dataset.gain) {
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
  if (!dialogApply) return;
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
  selected = $("#devices").value;
  try {
    await refresh(true);
  } catch (e) {
    status(e.message, true);
  }
};
$("#connect").onclick = async () => {
  const entered = prompt("Access key from the desktop app", key);
  if (entered == null) return;
  key = entered.trim();
  sessionStorage.setItem("cvr-key", key);
  try {
    await refresh(true);
    status("Connected");
  } catch (e) {
    status(e.message, true);
  }
};
refresh(true).catch((e) => status(e.message, true));
setInterval(() => refresh().catch((e) => status(e.message, true)), 2000);
