// PROTOTYPE ONLY — three success-mark variants of the coloured-bar 64 × 28 DIP Overlay, switchable via ?variant=A|B|C.
const variants = {
  A: {
    name: "Finale Geometrie",
    specs: { "Sichtbar": "64 × 28 DIP", "Hit target": "80 × 40 DIP", "Balken": "4 aktive · +1 / +6 / 0 / −6", "Farbcode": "Balken", "Erfolg": "1.6 s" }
  },
  B: {
    name: "Voller Haken",
    specs: { "Sichtbar": "64 × 28 DIP", "Hit target": "80 × 40 DIP", "Balken": "5 · Haken aus 5", "Farbcode": "Balken", "Erfolg": "1.6 s" }
  },
  C: {
    name: "Sechs Balken",
    specs: { "Sichtbar": "64 × 28 DIP", "Hit target": "80 × 40 DIP", "Balken": "6 · Haken aus 6", "Farbcode": "Balken", "Erfolg": "1.6 s" }
  }
};

const states = {
  idle: ["Bereit", "Local Dictation. Bereit."],
  recording: ["Aufnahme", "Local Dictation. Diktat wird aufgenommen. Escape bricht ab."],
  transcribing: ["Transkription", "Local Dictation. Diktat wird transkribiert."],
  loading: ["Modell laden", "Local Dictation. Transkriptionsmodell wird geladen. Verarbeitung wartet."],
  success: ["Erfolg", "Local Dictation. Text wurde eingefügt."],
  error: ["Fehler", "Local Dictation. Ein Fehler ist aufgetreten. Text wurde im Transkriptionsverlauf erhalten. Klicken für Aktionen."]
};

const overlay = document.querySelector("#overlay");
const stage = document.querySelector("#monitor-stage");
const menu = document.querySelector("#app-menu");
const gestureReadout = document.querySelector("#gesture-readout");
let variant = new URLSearchParams(location.search).get("variant")?.toUpperCase() || "A";
if (!variants[variant]) variant = "A";
let state = "idle";
let drag = null;
let successTimer = null;
let micHeights = [4, 4, 4, 4, 4, 4];
let checkOffsets = [1, 6, 0, -6];
let editorDrag = null;
let successPreviewPinned = false;
const editorScale = 4;

function formatCheckOffset(value, withUnit = true) {
  const signed = value > 0 ? `+${value}` : value < 0 ? `−${Math.abs(value)}` : "0";
  return withUnit ? `${signed} DIP` : signed;
}

function applyCheckGeometry() {
  const liveBars = overlay.querySelectorAll("i");
  checkOffsets.forEach((offset, index) => {
    liveBars[index + 1].style.setProperty("--success-y", `${offset}px`);
    const editorBar = document.querySelector(`[data-check-bar="${index}"]`);
    editorBar.style.setProperty("--editor-y", `${offset * editorScale}px`);
    editorBar.setAttribute("aria-valuenow", String(offset));
    editorBar.setAttribute("aria-valuetext", `${formatCheckOffset(offset)}; negative Werte oben, positive unten`);
    document.querySelector(`[data-check-range="${index}"]`).value = String(offset);
    document.querySelector(`[data-check-output="${index}"]`).textContent = formatCheckOffset(offset);
  });
  document.querySelector("#check-offset-summary").textContent = checkOffsets.map((offset, index) => `B${index + 1} ${formatCheckOffset(offset, false)}`).join(" · ");
}

function previewEditedCheck() {
  successPreviewPinned = true;
  if (variant !== "A") setVariant("A");
  setState("success");
}

function setCheckOffset(index, nextValue) {
  checkOffsets[index] = Math.max(-8, Math.min(8, Math.round(nextValue)));
  applyCheckGeometry();
  previewEditedCheck();
}

function setVariant(next) {
  variant = next;
  const info = variants[variant];
  document.body.classList.toggle("variant-b", variant === "B");
  document.body.classList.toggle("variant-c", variant === "C");
  document.querySelector("#variant-readout").textContent = `${variant} · ${info.name}`;
  document.querySelector("#switcher-label").textContent = `${variant} · ${info.name}`;
  document.querySelector("#spec-list").innerHTML = Object.entries(info.specs).map(([key, value]) => `<dt>${key}</dt><dd>${value}</dd>`).join("");
  const params = new URLSearchParams(location.search);
  params.set("variant", variant);
  history.replaceState(null, "", `${location.pathname}?${params}`);
  applyCheckGeometry();
  setHomePosition();
}

function setState(next) {
  window.clearTimeout(successTimer);
  state = next;
  overlay.dataset.state = state;
  overlay.querySelectorAll("i").forEach(bar => bar.style.removeProperty("height"));
  document.querySelectorAll("#state-controls [data-state]").forEach(button => button.setAttribute("aria-pressed", String(button.dataset.state === state)));
  document.querySelector("#state-readout").textContent = states[state][0];
  document.querySelector("#sr-output").textContent = states[state][1];
  overlay.setAttribute("aria-label", `${states[state][1]} Klicken für Anwendungsmenü; ziehen zum temporären Verschieben.`);
  if (state === "success" && !successPreviewPinned) {
    const delay = 1600;
    successTimer = window.setTimeout(() => setState("idle"), delay);
  }
}

function updateLevelCopy(value) {
  document.querySelector("#mic-level-value").textContent = `${value}%`;
  const hint = value <= 12
    ? "Grundrauschen · Noise gate geschlossen"
    : value <= 30
      ? "Sehr leise · Verständlichkeit wahrscheinlich eingeschränkt"
      : value <= 75
        ? "Normale Sprechlautstärke · geglättet"
        : "Laut · deutlich sichtbarer Ausschlag";
  document.querySelector("#mic-level-hint").textContent = hint;
}

function animateMicrophoneLevel() {
  if (state !== "recording") return;
  const value = Number(document.querySelector("#mic-level").value);
  const gated = value <= 12 ? 0 : (value - 12) / 88;
  const envelope = 4 + Math.pow(gated, .72) * 16;
  const profile = variant === "C" ? [.5, .7, .92, .92, .7, .5] : [.58, .82, 1, .76, .52, 0];
  const reducedMotion = document.body.classList.contains("reduced-motion");
  overlay.querySelectorAll("i").forEach((bar, index) => {
    const speechMovement = gated && !reducedMotion ? (Math.random() - .5) * 3.2 : 0;
    const target = Math.max(4, Math.min(20, 4 + (envelope - 4) * profile[index] + speechMovement));
    micHeights[index] += (target - micHeights[index]) * (reducedMotion ? 1 : .38);
    bar.style.height = `${micHeights[index].toFixed(1)}px`;
  });
}

function selectedMonitor() {
  const selection = document.querySelector("#monitor-select").value;
  return document.querySelector(selection === "secondary" ? ".monitor-secondary" : ".monitor-primary");
}

function setHomePosition() {
  const monitor = selectedMonitor();
  const stageRect = stage.getBoundingClientRect();
  const rect = monitor.getBoundingClientRect();
  const anchor = document.querySelector("#anchor-select").value;
  const margin = 14;
  const xMap = {
    left: rect.left - stageRect.left + margin,
    center: rect.left - stageRect.left + rect.width / 2 - overlay.offsetWidth / 2,
    right: rect.right - stageRect.left - margin - overlay.offsetWidth
  };
  const yMap = {
    top: rect.top - stageRect.top + margin,
    bottom: rect.bottom - stageRect.top - 29 - margin - overlay.offsetHeight
  };
  const [vertical, horizontal] = anchor.split("-");
  overlay.style.left = `${xMap[horizontal]}px`;
  overlay.style.top = `${yMap[vertical]}px`;
  menu.hidden = true;
  const monitorSelection = document.querySelector("#monitor-select").value;
  const fallback = monitorSelection === "missing" ? "Gespeicherter Monitor fehlt → Primär (nur diese Session)" : monitor.querySelector(".monitor-label").textContent;
  gestureReadout.textContent = `Home: ${fallback} · ${document.querySelector("#anchor-select").selectedOptions[0].textContent}`;
}

function openMenu() {
  const left = Math.min(stage.clientWidth - 205, overlay.offsetLeft + overlay.offsetWidth + 4);
  const top = Math.min(stage.clientHeight - 190, overlay.offsetTop);
  menu.style.left = `${left}px`;
  menu.style.top = `${top}px`;
  menu.hidden = !menu.hidden;
  gestureReadout.textContent = menu.hidden ? "Menü geschlossen" : "Klick erkannt · Anwendungsmenü geöffnet";
}

overlay.addEventListener("pointerdown", event => {
  drag = { id: event.pointerId, startX: event.clientX, startY: event.clientY, left: overlay.offsetLeft, top: overlay.offsetTop, moved: false };
  overlay.setPointerCapture(event.pointerId);
});
overlay.addEventListener("pointermove", event => {
  if (!drag || drag.id !== event.pointerId) return;
  const dx = event.clientX - drag.startX;
  const dy = event.clientY - drag.startY;
  const threshold = 6;
  if (!drag.moved && Math.hypot(dx, dy) >= threshold) {
    drag.moved = true;
    overlay.classList.add("dragging");
    menu.hidden = true;
  }
  if (!drag.moved) {
    gestureReadout.textContent = `Klick-Kandidat · ${Math.hypot(dx, dy).toFixed(1)} / ${threshold} DIP`;
    return;
  }
  const maxLeft = stage.clientWidth - overlay.offsetWidth;
  const maxTop = stage.clientHeight - overlay.offsetHeight;
  overlay.style.left = `${Math.max(0, Math.min(maxLeft, drag.left + dx))}px`;
  overlay.style.top = `${Math.max(0, Math.min(maxTop, drag.top + dy))}px`;
  gestureReadout.textContent = `Temporär verschoben · Δ ${Math.round(dx)}, ${Math.round(dy)} DIP`;
});
overlay.addEventListener("pointerup", event => {
  if (!drag || drag.id !== event.pointerId) return;
  const wasDrag = drag.moved;
  drag = null;
  overlay.classList.remove("dragging");
  if (!wasDrag) openMenu();
});
overlay.addEventListener("contextmenu", event => { event.preventDefault(); openMenu(); });

document.querySelector("#state-controls").addEventListener("click", event => {
  const button = event.target.closest("[data-state]");
  if (button) {
    successPreviewPinned = false;
    setState(button.dataset.state);
  }
});
document.querySelector("#monitor-select").addEventListener("change", setHomePosition);
document.querySelector("#anchor-select").addEventListener("change", setHomePosition);
document.querySelector("#return-home").addEventListener("click", setHomePosition);
document.querySelector("#reduced-motion").addEventListener("change", event => document.body.classList.toggle("reduced-motion", event.target.checked));
document.querySelector("#high-contrast").addEventListener("change", event => document.body.classList.toggle("high-contrast", event.target.checked));
document.querySelector("#mic-level").addEventListener("input", event => updateLevelCopy(Number(event.target.value)));

document.querySelectorAll("[data-check-range]").forEach(input => {
  input.addEventListener("input", event => setCheckOffset(Number(event.target.dataset.checkRange), Number(event.target.value)));
});

document.querySelectorAll("[data-check-bar]").forEach(bar => {
  bar.addEventListener("pointerdown", event => {
    const index = Number(event.currentTarget.dataset.checkBar);
    editorDrag = { id: event.pointerId, index, startY: event.clientY, startOffset: checkOffsets[index] };
    event.currentTarget.setPointerCapture(event.pointerId);
    event.preventDefault();
  });
  bar.addEventListener("pointermove", event => {
    if (!editorDrag || editorDrag.id !== event.pointerId) return;
    const delta = Math.round((event.clientY - editorDrag.startY) / editorScale);
    setCheckOffset(editorDrag.index, editorDrag.startOffset + delta);
  });
  bar.addEventListener("pointerup", event => {
    if (editorDrag?.id === event.pointerId) editorDrag = null;
  });
  bar.addEventListener("pointercancel", () => { editorDrag = null; });
  bar.addEventListener("keydown", event => {
    if (!["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"].includes(event.key)) return;
    event.preventDefault();
    event.stopPropagation();
    const index = Number(event.currentTarget.dataset.checkBar);
    const decrease = event.key === "ArrowUp" || event.key === "ArrowLeft";
    setCheckOffset(index, checkOffsets[index] + (decrease ? -1 : 1));
  });
});

document.querySelector("#reset-check-s3").addEventListener("click", () => {
  checkOffsets = [1, 6, 0, -6];
  applyCheckGeometry();
  previewEditedCheck();
});
document.querySelector("#center-check-bars").addEventListener("click", () => {
  checkOffsets = [0, 0, 0, 0];
  applyCheckGeometry();
  previewEditedCheck();
});

function cycle(direction) {
  successPreviewPinned = false;
  const keys = Object.keys(variants);
  const index = (keys.indexOf(variant) + direction + keys.length) % keys.length;
  setVariant(keys[index]);
  setState(state);
}
document.querySelector("#previous-variant").addEventListener("click", () => cycle(-1));
document.querySelector("#next-variant").addEventListener("click", () => cycle(1));
document.addEventListener("keydown", event => {
  if (["INPUT", "SELECT", "TEXTAREA"].includes(document.activeElement.tagName) || document.activeElement.isContentEditable) return;
  if (event.key === "ArrowLeft") cycle(-1);
  if (event.key === "ArrowRight") cycle(1);
  if (event.key === "Escape") { menu.hidden = true; setState("idle"); }
});
window.addEventListener("resize", setHomePosition);

setVariant(variant);
setState("idle");
updateLevelCopy(55);
window.setInterval(animateMicrophoneLevel, 100);
