/**
 * InteroExpos Lab — Motor Experimental de Biofeedback Háptico Cardíaco (BLE 0x180D)
 * Diseñado para investigación traslacional en exposición y aprendizaje inhibitorio.
 */

const state = {
  // BLE & Simulator
  bleDevice: null,
  bleCharacteristic: null,
  isConnectedBLE: false,
  isSimulating: false,
  simTimer: null,
  simProfile: "REST",
  simCurrentHR: 72,

  // Real-time physiological metrics
  currentHR: 0,
  currentRR: 0,
  currentRMSSD: 0,
  currentSlope10s: 0,
  baselineHR: 72,
  rrWindow: [],        // Last 30 RR intervals (ms)
  hrHistory10s: [],    // [{t, hr}] for 10s slope calculation
  chartHistory: [],    // Last 90 points for canvas [{t, hr, hapticBPM, pattern}]
  peakReachedInPhase: false,
  phasePeakHR: 0,

  // Haptic Engine
  condition: "CONTINUOUS",
  thresholdPct: 15,
  reliefSlope: -2.5,
  multiplierK: 1.0,
  pulseMs: 55,
  effectivePulseMs: 55,
  targetHapticBPM: 0,
  activeHapticPatternLabel: "Inactivo",
  hapticTimer: null,
  lastHapticFireTime: 0,

  // Session & Protocol Phases
  isRecording: false,
  sessionStartTime: null,
  recordingClockTimer: null,
  activePhase: "BASELINE",
  pendingEventMarker: "",
  phaseSamples: [], // HR & RR samples inside current phase

  // Schandry Interoceptive Accuracy Task
  schandryRunning: false,
  schandryRealBeats: 0,
  schandryIacc: null,

  // Data Logs for CSV Export
  timeseriesLog: [],
  trialsSummaryLog: [],

  // WakeLock
  wakeLock: null
};

// Condition descriptions for researcher guidance
const CONDITION_DESCRIPTIONS = {
  CONTINUOUS:
    "<strong>Exp 1 — Condición Bifásica/Continua (1:1):</strong> El teléfono vibra en cada latido real durante los bloques de exposición. Permite percibir tanto la cardioaceleración (violación de expectativa) como la desaceleración progresiva.",
  THRESHOLD_BOTH:
    "<strong>Exp 1 — Transición Dinámica (Umbral + Alivio):</strong> Vibra cuando la FC supera el umbral sobre la línea base y cambia a un <em>doble pulso suave de alivio</em> cuando detecta una primera derivada negativa (desaceleración autonómica).",
  RELIEF_ONLY:
    "<strong>Exp 1 — Bloqueada al Alivio (Relief-Derivative-Locked):</strong> Silencio durante el pico inicial; solo emite el doble pulso suave cuando la derivada es negativa (dFC/dt ≤ umbral), aislando el error de predicción de alivio.",
  THRESHOLD_ALERT_ONLY:
    "<strong>Exp 1 — Bloqueada al Pico (Arousal-Locked):</strong> Vibra únicamente mientras la FC supera el umbral sobre la línea base y se silencia en cuanto empieza a bajar, aislando la violación de expectativa interoceptiva.",
  SLOPE_GAIN:
    "<strong>Exp 2 — Perturbación de Ganancia de Pendiente (γ):</strong> Respeta el pico cardíaco 100% real al inicio del bloque (máxima credibilidad interoceptiva) y modula la pendiente de bajada según γ: FC_háptica = FC_pico - γ·(FC_pico - FC_real).",
  ADAPTIVE_FADING:
    "<strong>Exp 3 — Desvanecimiento Háptico Adaptativo:</strong> Atenúa progresivamente la duración del pulso vibratorio del Bloque 1 al Bloque 5 (100% → 80% → 60% → 35% → 15%) para evitar que el dispositivo opere como señal de seguridad y prevenir la renovación contextual.",
  SHAM:
    "<strong>Control Activo Sham / Yoked:</strong> El teléfono vibra a una cadencia tónica cercana a la línea base (±2.5 BPM de deriva sinusoidal), desacoplada de la reactividad real del participante.",
  NONE:
    "<strong>Control Sin Feedback:</strong> La banda pectoral registra FC e intervalos R-R de forma continua, pero el motor vibratorio permanece apagado."
};

// DOM Elements
const el = {
  bleStatusPill: document.getElementById("bleStatusPill"),
  bleDot: document.getElementById("bleDot"),
  bleStatusText: document.getElementById("bleStatusText"),
  hapticStatusText: document.getElementById("hapticStatusText"),
  recStatusPill: document.getElementById("recStatusPill"),
  recStatusText: document.getElementById("recStatusText"),

  valHR: document.getElementById("valHR"),
  valDeltaLB: document.getElementById("valDeltaLB"),
  valRR: document.getElementById("valRR"),
  valRMSSD: document.getElementById("valRMSSD"),
  valSlope: document.getElementById("valSlope"),
  valDynamicState: document.getElementById("valDynamicState"),
  valHapticBPM: document.getElementById("valHapticBPM"),
  valHapticPattern: document.getElementById("valHapticPattern"),

  hrChart: document.getElementById("hrChart"),
  btnConnectBLE: document.getElementById("btnConnectBLE"),
  btnToggleSim: document.getElementById("btnToggleSim"),
  btnTestVibrate: document.getElementById("btnTestVibrate"),
  simControls: document.getElementById("simControls"),
  simStateSelect: document.getElementById("simStateSelect"),

  inputSubjectId: document.getElementById("inputSubjectId"),
  selectExperiment: document.getElementById("selectExperiment"),
  selectPhobia: document.getElementById("selectPhobia"),
  inputASI3: document.getElementById("inputASI3"),
  selectCondition: document.getElementById("selectCondition"),
  inputThresholdPct: document.getElementById("inputThresholdPct"),
  inputReliefSlope: document.getElementById("inputReliefSlope"),
  inputMultiplierK: document.getElementById("inputMultiplierK"),
  inputPulseMs: document.getElementById("inputPulseMs"),
  conditionHelperText: document.getElementById("conditionHelperText"),

  inputBaselineHR: document.getElementById("inputBaselineHR"),
  btnCaptureBaseline: document.getElementById("btnCaptureBaseline"),
  selectSchandrySec: document.getElementById("selectSchandrySec"),
  inputCountedBeats: document.getElementById("inputCountedBeats"),
  btnStartSchandry: document.getElementById("btnStartSchandry"),
  btnCalcSchandry: document.getElementById("btnCalcSchandry"),
  lblRealBeats: document.getElementById("lblRealBeats"),
  lblIaccScore: document.getElementById("lblIaccScore"),

  btnStartRec: document.getElementById("btnStartRec"),
  btnMarkStimulusOn: document.getElementById("btnMarkStimulusOn"),
  btnMarkStimulusOff: document.getElementById("btnMarkStimulusOff"),
  phaseButtonsContainer: document.getElementById("phaseButtonsContainer"),

  inputExpectancy: document.getElementById("inputExpectancy"),
  inputSUDS: document.getElementById("inputSUDS"),
  inputRelief: document.getElementById("inputRelief"),
  inputPerceivedHR: document.getElementById("inputPerceivedHR"),
  btnSaveBlockRating: document.getElementById("btnSaveBlockRating"),

  trialsTableBody: document.getElementById("trialsTableBody"),
  btnExportTimeseries: document.getElementById("btnExportTimeseries"),
  btnExportTrials: document.getElementById("btnExportTrials"),
  btnClearData: document.getElementById("btnClearData"),

  btnBlindMode: document.getElementById("btnBlindMode"),
  blindOverlay: document.getElementById("blindOverlay"),
  blindPulseCircle: document.getElementById("blindPulseCircle"),
  btnExitBlindMode: document.getElementById("btnExitBlindMode")
};

// ============================================================================
// 1. WEB BLUETOOTH API (Standard Heart Rate Profile 0x180D / 0x2A37)
// ============================================================================

async function connectBluetoothBand() {
  if (!navigator.bluetooth) {
    alert(
      "Tu navegador no soporta Web Bluetooth API.\n\nEn Android usa Google Chrome. En escritorio usa Chrome o Edge sobre localhost o HTTPS."
    );
    return;
  }

  try {
    if (state.isSimulating) {
      stopSimulator();
    }

    el.bleStatusText.textContent = "Buscando banda...";
    const device = await navigator.bluetooth.requestDevice({
      filters: [{ services: ["heart_rate"] }]
    });

    state.bleDevice = device;
    device.addEventListener("gattserverdisconnected", onBLEDisconnected);

    const server = await device.gatt.connect();
    const service = await server.getPrimaryService("heart_rate");
    const characteristic = await service.getCharacteristic("heart_rate_measurement");

    await characteristic.startNotifications();
    characteristic.addEventListener("characteristicvaluechanged", handleHeartRateNotification);

    state.bleCharacteristic = characteristic;
    state.isConnectedBLE = true;

    el.bleStatusPill.className = "pill connected";
    el.bleDot.className = "dot pulse";
    el.bleStatusText.textContent = device.name || "Banda BLE Conectada";
    el.btnConnectBLE.textContent = "Desconectar Banda BLE";

    requestWakeLock();
    startHapticScheduler();
  } catch (err) {
    console.warn("Conexión BLE cancelada o fallida:", err);
    el.bleStatusText.textContent = "Banda Desconectada";
  }
}

function disconnectBluetoothBand() {
  if (state.bleDevice && state.bleDevice.gatt.connected) {
    state.bleDevice.gatt.disconnect();
  }
  onBLEDisconnected();
}

function onBLEDisconnected() {
  state.isConnectedBLE = false;
  el.bleStatusPill.className = "pill";
  el.bleDot.className = "dot";
  el.bleStatusText.textContent = "Banda Desconectada";
  el.btnConnectBLE.textContent = "Conectar Banda Bluetooth (0x180D)";
}

/**
 * Decodes standard Bluetooth GATT Heart Rate Measurement (0x2A37) packet
 */
function handleHeartRateNotification(event) {
  const value = event.target.value;
  const flags = value.getUint8(0);
  const is16Bit = (flags & 0x01) !== 0;
  const hasEnergyExpended = (flags & 0x08) !== 0;
  const hasRRIntervals = (flags & 0x10) !== 0;

  let offset = 1;
  let hr = 0;
  if (is16Bit) {
    hr = value.getUint16(offset, true);
    offset += 2;
  } else {
    hr = value.getUint8(offset);
    offset += 1;
  }

  if (hasEnergyExpended) {
    offset += 2;
  }

  const rrList = [];
  if (hasRRIntervals) {
    while (offset + 1 < value.byteLength) {
      const rawRR = value.getUint16(offset, true);
      // Standard resolution is 1/1024 seconds -> convert to ms
      const rrMs = Math.round((rawRR / 1024.0) * 1000);
      if (rrMs > 250 && rrMs < 2000) {
        rrList.push(rrMs);
      }
      offset += 2;
    }
  } else if (hr > 0) {
    // Fallback synthetic RR if band only sends BPM
    rrList.push(Math.round(60000 / hr));
  }

  processIncomingPhysiology(hr, rrList);
}

// ============================================================================
// 2. PHYSIOLOGICAL SIMULATOR (For dry testing & calibration)
// ============================================================================

function toggleSimulator() {
  if (state.isSimulating) {
    stopSimulator();
  } else {
    if (state.isConnectedBLE) {
      disconnectBluetoothBand();
    }
    startSimulator();
  }
}

function startSimulator() {
  state.isSimulating = true;
  state.simCurrentHR = state.baselineHR || 72;
  el.bleStatusPill.className = "pill simulating";
  el.bleDot.className = "dot pulse";
  el.bleStatusText.textContent = "Simulador Activo";
  el.btnToggleSim.textContent = "Detener Simulador";
  el.simControls.style.display = "block";

  startHapticScheduler();

  state.simTimer = setInterval(() => {
    const profile = el.simStateSelect.value;
    let target = state.baselineHR;
    let jitter = (Math.random() - 0.5) * 2.2;

    if (profile === "REST") {
      target = state.baselineHR;
      state.simCurrentHR += (target - state.simCurrentHR) * 0.25 + jitter;
    } else if (profile === "ANTICIPATION") {
      target = state.baselineHR + 16;
      state.simCurrentHR += (target - state.simCurrentHR) * 0.25 + jitter;
    } else if (profile === "EXPOSURE_PEAK") {
      target = state.baselineHR + 42;
      state.simCurrentHR += (target - state.simCurrentHR) * 0.3 + jitter * 1.3;
    } else if (profile === "RELIEF_DECEL") {
      // Smooth steady deceleration (-0.85 BPM per second -> ~ -8.5 BPM / 10s)
      const floorHR = state.baselineHR + 3;
      state.simCurrentHR = Math.max(floorHR, state.simCurrentHR - 0.85 + jitter * 0.4);
    }

    const hr = Math.round(Math.max(48, Math.min(185, state.simCurrentHR)));
    // Add natural respiratory sinus arrhythmia to RR
    const baseRR = 60000 / hr;
    const rsaNoise = profile === "EXPOSURE_PEAK" ? (Math.random() - 0.5) * 14 : (Math.random() - 0.5) * 42;
    const rrMs = Math.round(baseRR + rsaNoise);

    processIncomingPhysiology(hr, [rrMs]);
  }, 1000);
}

function stopSimulator() {
  state.isSimulating = false;
  if (state.simTimer) {
    clearInterval(state.simTimer);
    state.simTimer = null;
  }
  el.bleStatusPill.className = "pill";
  el.bleDot.className = "dot";
  el.bleStatusText.textContent = "Banda Desconectada";
  el.btnToggleSim.textContent = "Iniciar Simulador Fisiológico";
  el.simControls.style.display = "none";
}

// ============================================================================
// 3. SIGNAL PROCESSING (RMSSD, 10s Slope, Dynamic State & Haptic Target)
// ============================================================================

function processIncomingPhysiology(hr, rrList) {
  const now = Date.now();
  state.currentHR = hr;

  if (rrList && rrList.length > 0) {
    state.currentRR = rrList[rrList.length - 1];
    rrList.forEach((rr) => {
      state.rrWindow.push(rr);
      if (state.rrWindow.length > 30) {
        state.rrWindow.shift();
      }
    });
    state.currentRMSSD = computeRMSSD(state.rrWindow);
  }

  // If Schandry task is running, accumulate beats proportional to packet
  if (state.schandryRunning) {
    state.schandryRealBeats += hr / 60.0;
  }

  // Update 10-second sliding window for slope calculation
  state.hrHistory10s.push({ t: now, hr });
  state.hrHistory10s = state.hrHistory10s.filter((item) => now - item.t <= 10500);
  state.currentSlope10s = computeSlope10s(state.hrHistory10s);

  // Check if threshold was exceeded in this phase (to enable relief detection after arousal)
  const thresholdHR = state.baselineHR * (1 + state.thresholdPct / 100.0);
  if (hr >= thresholdHR) {
    state.peakReachedInPhase = true;
  }
  if (hr > state.phasePeakHR) {
    state.phasePeakHR = hr;
  }

  // Compute target haptic output according to experimental condition and active phase
  updateHapticTarget(hr, thresholdHR);

  // Store sample in current phase statistics
  state.phaseSamples.push({
    t: now,
    hr,
    rr: state.currentRR,
    rmssd: state.currentRMSSD,
    slope10s: state.currentSlope10s
  });

  // Update Chart History (keep last 90 seconds)
  state.chartHistory.push({
    t: now,
    hr,
    hapticBPM: state.targetHapticBPM,
    pattern: state.activeHapticPatternLabel
  });
  if (state.chartHistory.length > 90) {
    state.chartHistory.shift();
  }

  // Log to timeseries if recording is active
  if (state.isRecording) {
    const elapsedSec = ((now - state.sessionStartTime) / 1000).toFixed(2);
    state.timeseriesLog.push({
      timestamp_iso: new Date(now).toISOString(),
      elapsed_sec: elapsedSec,
      subject_id: el.inputSubjectId.value.trim() || "SUB_001",
      experiment: el.selectExperiment.value,
      phobia: el.selectPhobia.value,
      condition: state.condition,
      phase: state.activePhase,
      hr_bpm: hr,
      baseline_hr: state.baselineHR,
      delta_hr_bpm: (hr - state.baselineHR).toFixed(1),
      rr_ms: state.currentRR,
      rmssd_ms: state.currentRMSSD.toFixed(1),
      slope_10s: state.currentSlope10s.toFixed(2),
      haptic_bpm_delivered: state.targetHapticBPM,
      haptic_pattern: state.activeHapticPatternLabel,
      event_marker: state.pendingEventMarker
    });
    state.pendingEventMarker = "";
  }

  renderTelemetryUI(thresholdHR);
  drawTelemetryChart(thresholdHR);
}

function computeRMSSD(rrArray) {
  if (rrArray.length < 2) return 0;
  let sumSqDiff = 0;
  for (let i = 1; i < rrArray.length; i++) {
    const diff = rrArray[i] - rrArray[i - 1];
    sumSqDiff += diff * diff;
  }
  return Math.sqrt(sumSqDiff / (rrArray.length - 1));
}

function computeSlope10s(history) {
  if (history.length < 3) return 0;
  const first = history[0];
  const last = history[history.length - 1];
  const dtSec = (last.t - first.t) / 1000.0;
  if (dtSec < 2) return 0;
  // Express slope normalized per 10 seconds
  return ((last.hr - first.hr) / dtSec) * 10.0;
}

// ============================================================================
// 4. HAPTIC BIOFEEDBACK ENGINE (Vibration API)
// ============================================================================

function getAdaptiveFadingFactor(phase) {
  const fadingMap = {
    EXP_B1: 1.0,
    EXP_B2: 0.8,
    EXP_B3: 0.6,
    EXP_B4: 0.35,
    EXP_B5: 0.15
  };
  return fadingMap[phase] !== undefined ? fadingMap[phase] : 1.0;
}

function updateHapticTarget(hr, thresholdHR) {
  state.effectivePulseMs = state.pulseMs;

  // Always silence haptics during Schandry task, Baseline, or Retention Test (No-Feedback test)
  if (
    state.schandryRunning ||
    state.activePhase === "BASELINE" ||
    state.activePhase === "RETENTION_NO_FB"
  ) {
    state.targetHapticBPM = 0;
    state.activeHapticPatternLabel =
      state.activePhase === "RETENTION_NO_FB" ? "Silenciado (Test Retención)" : "Silenciado (Fase Basal)";
    return;
  }

  const cond = state.condition;
  const isAboveThreshold = hr >= thresholdHR;
  const isDecelerating =
    state.currentSlope10s <= state.reliefSlope &&
    (state.peakReachedInPhase || hr > state.baselineHR + 4);

  if (cond === "NONE") {
    state.targetHapticBPM = 0;
    state.activeHapticPatternLabel = "Sin vibración (Control)";
  } else if (cond === "CONTINUOUS") {
    state.targetHapticBPM = hr;
    state.activeHapticPatternLabel = "Pulso 1:1 Sincronizado";
  } else if (cond === "THRESHOLD_BOTH") {
    if (isDecelerating) {
      state.targetHapticBPM = Math.max(55, Math.round(hr * 0.85));
      state.activeHapticPatternLabel = "Doble Pulso (Alivio ↓)";
    } else if (isAboveThreshold) {
      state.targetHapticBPM = hr;
      state.activeHapticPatternLabel = "Alerta Umbral ↑";
    } else {
      state.targetHapticBPM = 0;
      state.activeHapticPatternLabel = "Bajo umbral (En espera)";
    }
  } else if (cond === "RELIEF_ONLY") {
    if (isDecelerating) {
      state.targetHapticBPM = Math.max(55, Math.round(hr * 0.85));
      state.activeHapticPatternLabel = "Doble Pulso (Alivio ↓)";
    } else {
      state.targetHapticBPM = 0;
      state.activeHapticPatternLabel = "Esperando desaceleración";
    }
  } else if (cond === "THRESHOLD_ALERT_ONLY") {
    if (isAboveThreshold) {
      state.targetHapticBPM = hr;
      state.activeHapticPatternLabel = "Alerta Umbral ↑";
    } else {
      state.targetHapticBPM = 0;
      state.activeHapticPatternLabel = "Bajo umbral (Silencio)";
    }
  } else if (cond === "SLOPE_GAIN") {
    // Exp 2: Respects true peak HR, modulates deceleration trajectory by gamma (multiplierK)
    const peak = Math.max(hr, state.phasePeakHR || hr);
    const dropFromPeak = peak - hr;
    const modulatedHR = Math.round(peak - state.multiplierK * dropFromPeak);
    state.targetHapticBPM = Math.max(45, Math.min(195, modulatedHR));
    state.activeHapticPatternLabel = `Pendiente γ=${state.multiplierK.toFixed(2)} (Pico ${peak})`;
  } else if (cond === "ADAPTIVE_FADING") {
    // Exp 3: Progressive attenuation of pulse duration across blocks B1 -> B5
    const fadeFactor = getAdaptiveFadingFactor(state.activePhase);
    state.effectivePulseMs = Math.max(8, Math.round(state.pulseMs * fadeFactor));
    state.targetHapticBPM = hr;
    state.activeHapticPatternLabel = `Fading ${Math.round(fadeFactor * 100)}% (${state.effectivePulseMs}ms)`;
  } else if (cond === "SHAM") {
    // Uncoupled tonic rhythm around baseline with slow sine wave
    const slowWave = Math.sin(Date.now() / 6000.0) * 2.5;
    state.targetHapticBPM = Math.round(state.baselineHR + slowWave);
    state.activeHapticPatternLabel = "Sham Desacoplado";
  }
}

function startHapticScheduler() {
  if (state.hapticTimer) {
    clearTimeout(state.hapticTimer);
  }
  const loop = () => {
    if (!state.isConnectedBLE && !state.isSimulating) {
      state.targetHapticBPM = 0;
      return;
    }

    if (state.targetHapticBPM >= 40) {
      const intervalMs = Math.max(280, Math.round(60000 / state.targetHapticBPM));
      fireHapticPulse(state.activeHapticPatternLabel);
      state.hapticTimer = setTimeout(loop, intervalMs);
    } else {
      state.hapticTimer = setTimeout(loop, 250);
    }
  };
  loop();
}

function fireHapticPulse(patternLabel) {
  state.lastHapticFireTime = Date.now();

  // Visual indicator on Blind Overlay if active
  if (el.blindOverlay.classList.contains("active")) {
    el.blindPulseCircle.classList.add("haptic-fire");
    setTimeout(() => el.blindPulseCircle.classList.remove("haptic-fire"), 110);
  }

  if (!("vibrate" in navigator)) return;

  if (patternLabel.includes("Doble Pulso")) {
    // Distinct bifasic soft pattern for autonomic relief / deceleration
    navigator.vibrate([35, 65, 55]);
  } else {
    // Crisp single heartbeat pulse (uses effectivePulseMs for Adaptive Fading support)
    navigator.vibrate(state.effectivePulseMs || state.pulseMs);
  }
}

// ============================================================================
// 5. UI RENDERING & LIVE OSCILLOSCOPE CANVAS
// ============================================================================

function renderTelemetryUI(thresholdHR) {
  el.valHR.textContent = state.currentHR || "--";
  const delta = state.currentHR - state.baselineHR;
  const sign = delta >= 0 ? "+" : "";
  el.valDeltaLB.textContent = `Δ LB: ${sign}${delta} BPM (Umbral: ${Math.round(thresholdHR)})`;

  el.valRR.textContent = state.currentRR || "--";
  el.valRMSSD.textContent = `RMSSD: ${state.currentRMSSD.toFixed(1)} ms`;

  const slopeSign = state.currentSlope10s >= 0 ? "+" : "";
  el.valSlope.textContent = `${slopeSign}${state.currentSlope10s.toFixed(1)}`;

  if (state.currentSlope10s <= state.reliefSlope) {
    el.valDynamicState.textContent = "Desaceleración / Alivio ↓";
    el.valDynamicState.style.color = "#34d399";
  } else if (state.currentHR >= thresholdHR) {
    el.valDynamicState.textContent = "Sobre Umbral Activación ↑";
    el.valDynamicState.style.color = "#f87171";
  } else {
    el.valDynamicState.textContent = "Rango Basal / Estable";
    el.valDynamicState.style.color = "#94a3b8";
  }

  el.valHapticBPM.textContent = state.targetHapticBPM;
  el.valHapticPattern.textContent = state.activeHapticPatternLabel;
  el.hapticStatusText.textContent =
    state.targetHapticBPM > 0 ? `${state.targetHapticBPM} BPM` : "Silencio";
}

function drawTelemetryChart(thresholdHR) {
  const canvas = el.hrChart;
  const ctx = canvas.getContext("2d");
  const w = canvas.width;
  const h = canvas.height;

  ctx.clearRect(0, 0, w, h);

  const minBPM = 45;
  const maxBPM = 165;
  const mapY = (bpm) => {
    const clamped = Math.max(minBPM, Math.min(maxBPM, bpm));
    return h - 18 - ((clamped - minBPM) / (maxBPM - minBPM)) * (h - 32);
  };

  // Horizontal grid & Baseline line
  const yBase = mapY(state.baselineHR);
  ctx.strokeStyle = "rgba(16, 185, 129, 0.45)";
  ctx.setLineDash([5, 5]);
  ctx.beginPath();
  ctx.moveTo(0, yBase);
  ctx.lineTo(w, yBase);
  ctx.stroke();

  ctx.fillStyle = "#10b981";
  ctx.font = "11px sans-serif";
  ctx.fillText(`LB: ${state.baselineHR} BPM`, 8, yBase - 4);

  // Threshold line
  const yThresh = mapY(thresholdHR);
  ctx.strokeStyle = "rgba(245, 158, 11, 0.45)";
  ctx.beginPath();
  ctx.moveTo(0, yThresh);
  ctx.lineTo(w, yThresh);
  ctx.stroke();
  ctx.setLineDash([]);

  ctx.fillStyle = "#f59e0b";
  ctx.fillText(`Umbral: ${Math.round(thresholdHR)} BPM`, w - 115, yThresh - 4);

  if (state.chartHistory.length < 2) return;

  // Plot Real HR Curve
  ctx.strokeStyle = "#f87171";
  ctx.lineWidth = 2.2;
  ctx.beginPath();
  const maxPoints = 90;
  const stepX = w / (maxPoints - 1);

  state.chartHistory.forEach((pt, idx) => {
    const x = idx * stepX;
    const y = mapY(pt.hr);
    if (idx === 0) ctx.moveTo(x, y);
    else ctx.lineTo(x, y);
  });
  ctx.stroke();

  // Plot Haptic Delivery Dots at the bottom
  state.chartHistory.forEach((pt, idx) => {
    if (pt.hapticBPM > 0) {
      const x = idx * stepX;
      ctx.fillStyle = pt.pattern.includes("Alivio") ? "#34d399" : "#a855f7";
      ctx.beginPath();
      ctx.arc(x, h - 7, 3, 0, Math.PI * 2);
      ctx.fill();
    }
  });
}

// ============================================================================
// 6. SCHANDRY HEARTBEAT COUNTING TASK (Interoceptive Accuracy)
// ============================================================================

function startSchandryTask() {
  if (!state.isConnectedBLE && !state.isSimulating) {
    alert("Conecta primero la banda Bluetooth o activa el Simulador para contar los latidos reales.");
    return;
  }
  const durationSec = parseInt(el.selectSchandrySec.value, 10) || 25;
  state.schandryRunning = true;
  state.schandryRealBeats = 0;
  el.btnStartSchandry.disabled = true;
  el.btnStartSchandry.textContent = `Contando (${durationSec}s)...`;
  el.lblRealBeats.textContent = "Contando...";

  // Short cue vibration at start
  if ("vibrate" in navigator) navigator.vibrate(150);

  setTimeout(() => {
    state.schandryRunning = false;
    state.schandryRealBeats = Math.round(state.schandryRealBeats);
    el.btnStartSchandry.disabled = false;
    el.btnStartSchandry.textContent = "Iniciar Ventana de Conteo (Silenciosa)";
    el.lblRealBeats.textContent = `${state.schandryRealBeats} latidos`;
    if ("vibrate" in navigator) navigator.vibrate([120, 80, 120]);
  }, durationSec * 1000);
}

function calculateSchandryScore() {
  const real = state.schandryRealBeats;
  const counted = parseFloat(el.inputCountedBeats.value);
  if (!real || isNaN(counted)) {
    alert("Ejecuta primero la ventana de conteo e ingresa los latidos reportados por el participante.");
    return;
  }
  const iacc = Math.max(0, 1 - Math.abs(real - counted) / real);
  state.schandryIacc = iacc.toFixed(3);
  el.lblIaccScore.textContent = `${state.schandryIacc} (${Math.round(iacc * 100)}%)`;
}

// ============================================================================
// 7. SESSION RECORDING, PHASE MANAGEMENT & BLOCK RATINGS
// ============================================================================

function toggleRecording() {
  if (!state.isRecording) {
    state.isRecording = true;
    state.sessionStartTime = Date.now();
    el.recStatusPill.className = "pill recording";
    el.btnStartRec.textContent = "Pausar Registro";
    el.btnStartRec.className = "btn btn-danger";
    requestWakeLock();

    state.recordingClockTimer = setInterval(() => {
      const sec = Math.floor((Date.now() - state.sessionStartTime) / 1000);
      const mm = String(Math.floor(sec / 60)).padStart(2, "0");
      const ss = String(sec % 60).padStart(2, "0");
      el.recStatusText.textContent = `REC ${mm}:${ss}`;
    }, 1000);
  } else {
    state.isRecording = false;
    clearInterval(state.recordingClockTimer);
    el.recStatusPill.className = "pill";
    el.recStatusText.textContent = "Pausado";
    el.btnStartRec.textContent = "Reanudar Registro";
    el.btnStartRec.className = "btn btn-success";
  }
}

function setActivePhase(newPhase) {
  state.activePhase = newPhase;
  state.phaseSamples = [];
  state.peakReachedInPhase = false;
  state.phasePeakHR = state.currentHR || 0;
  state.pendingEventMarker = `PHASE_START_${newPhase}`;

  document.querySelectorAll(".phase-btn").forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.phase === newPhase);
  });

  if (state.currentHR > 0) {
    const thresholdHR = state.baselineHR * (1 + state.thresholdPct / 100.0);
    updateHapticTarget(state.currentHR, thresholdHR);
    renderTelemetryUI(thresholdHR);
  }
}

function saveCurrentBlockRating() {
  const samples = state.phaseSamples;
  const meanHR =
    samples.length > 0
      ? (samples.reduce((acc, s) => acc + s.hr, 0) / samples.length).toFixed(1)
      : state.currentHR || 0;
  const peakHR =
    samples.length > 0
      ? Math.max(...samples.map((s) => s.hr))
      : state.currentHR || 0;
  const meanRMSSD =
    samples.length > 0
      ? (samples.reduce((acc, s) => acc + s.rmssd, 0) / samples.length).toFixed(1)
      : state.currentRMSSD.toFixed(1);

  const perceivedHR = parseFloat(el.inputPerceivedHR.value) || 0;
  const actualHRNow = state.currentHR || parseFloat(meanHR) || 0;
  const interoceptiveError = (perceivedHR - actualHRNow).toFixed(1);

  const row = {
    time_iso: new Date().toLocaleTimeString(),
    subject_id: el.inputSubjectId.value.trim() || "SUB_001",
    experiment: el.selectExperiment.value,
    phobia: el.selectPhobia.value,
    asi3_maia: el.inputASI3.value.trim() || "NA",
    schandry_iacc: state.schandryIacc || "NA",
    condition: state.condition,
    phase: state.activePhase,
    baseline_hr: state.baselineHR,
    mean_hr: meanHR,
    peak_hr: peakHR,
    rmssd_ms: meanRMSSD,
    perceived_hr: perceivedHR,
    interoceptive_error_bpm: interoceptiveError,
    expectancy_0_100: parseFloat(el.inputExpectancy.value) || 0,
    suds_0_100: parseFloat(el.inputSUDS.value) || 0,
    relief_0_100: parseFloat(el.inputRelief.value) || 0,
    slope_10s_end: state.currentSlope10s.toFixed(2)
  };

  state.trialsSummaryLog.push(row);
  state.pendingEventMarker = `BLOCK_RATING_SAVED_${state.activePhase}`;
  renderTrialsTable();
}

function renderTrialsTable() {
  if (state.trialsSummaryLog.length === 0) {
    el.trialsTableBody.innerHTML = `<tr><td colspan="11" style="text-align:center;color:var(--text-muted);">Aún no se han guardado bloques en esta sesión.</td></tr>`;
    return;
  }

  el.trialsTableBody.innerHTML = state.trialsSummaryLog
    .map((r) => {
      const errSign = parseFloat(r.interoceptive_error_bpm) >= 0 ? "+" : "";
      return `
        <tr>
          <td>${r.time_iso}</td>
          <td><strong>${r.phase}</strong></td>
          <td>${r.condition}</td>
          <td>${r.mean_hr}</td>
          <td>${r.peak_hr}</td>
          <td>${r.rmssd_ms}</td>
          <td>${r.perceived_hr} (${errSign}${r.interoceptive_error_bpm})</td>
          <td>${r.expectancy_0_100}%</td>
          <td>${r.suds_0_100}</td>
          <td>${r.relief_0_100}</td>
          <td>${r.slope_10s_end}</td>
        </tr>
      `;
    })
    .join("");
}

// ============================================================================
// 8. CSV EXPORT UTILITIES (UTF-8 with BOM for Excel / R / SPSS / JASP)
// ============================================================================

function downloadCSV(filename, rows) {
  if (!rows || rows.length === 0) {
    alert("No hay datos registrados todavía para exportar.");
    return;
  }
  const headers = Object.keys(rows[0]);
  const csvLines = [
    headers.join(","),
    ...rows.map((row) =>
      headers
        .map((h) => {
          const val = row[h] === null || row[h] === undefined ? "" : String(row[h]);
          return val.includes(",") || val.includes('"') ? `"${val.replace(/"/g, '""')}"` : val;
        })
        .join(",")
    )
  ];

  const blob = new Blob(["\uFEFF" + csvLines.join("\n")], {
    type: "text/csv;charset=utf-8;"
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

// ============================================================================
// 9. WAKELOCK & EVENT LISTENERS
// ============================================================================

async function requestWakeLock() {
  if ("wakeLock" in navigator) {
    try {
      state.wakeLock = await navigator.wakeLock.request("screen");
    } catch (e) {
      // Ignore if browser blocks WakeLock
    }
  }
}

function initEventListeners() {
  el.btnConnectBLE.addEventListener("click", () => {
    if (state.isConnectedBLE) disconnectBluetoothBand();
    else connectBluetoothBand();
  });

  el.btnToggleSim.addEventListener("click", toggleSimulator);

  el.btnTestVibrate.addEventListener("click", () => {
    if ("vibrate" in navigator) {
      navigator.vibrate([60, 80, 60]);
    } else {
      alert("Este navegador/dispositivo no soporta navigator.vibrate (usa Chrome en Android).");
    }
  });

  el.selectCondition.addEventListener("change", (e) => {
    state.condition = e.target.value;
    el.conditionHelperText.innerHTML = CONDITION_DESCRIPTIONS[state.condition] || "";
  });

  el.inputThresholdPct.addEventListener("input", (e) => {
    state.thresholdPct = parseFloat(e.target.value) || 15;
  });

  el.inputReliefSlope.addEventListener("input", (e) => {
    state.reliefSlope = parseFloat(e.target.value) || -2.5;
  });

  el.inputMultiplierK.addEventListener("input", (e) => {
    state.multiplierK = parseFloat(e.target.value) || 1.0;
  });

  el.inputPulseMs.addEventListener("input", (e) => {
    state.pulseMs = parseInt(e.target.value, 10) || 50;
  });

  el.inputBaselineHR.addEventListener("input", (e) => {
    state.baselineHR = parseInt(e.target.value, 10) || 72;
  });

  el.btnCaptureBaseline.addEventListener("click", () => {
    if (state.currentHR > 0) {
      state.baselineHR = state.currentHR;
      el.inputBaselineHR.value = state.currentHR;
    }
  });

  el.btnStartSchandry.addEventListener("click", startSchandryTask);
  el.btnCalcSchandry.addEventListener("click", calculateSchandryScore);

  el.btnStartRec.addEventListener("click", toggleRecording);

  el.btnMarkStimulusOn.addEventListener("click", () => {
    state.pendingEventMarker = "STIMULUS_ONSET";
  });

  el.btnMarkStimulusOff.addEventListener("click", () => {
    state.pendingEventMarker = "STIMULUS_OFFSET";
  });

  el.phaseButtonsContainer.addEventListener("click", (e) => {
    const btn = e.target.closest(".phase-btn");
    if (!btn) return;
    setActivePhase(btn.dataset.phase);
  });

  el.btnSaveBlockRating.addEventListener("click", saveCurrentBlockRating);

  el.btnExportTimeseries.addEventListener("click", () => {
    const subj = el.inputSubjectId.value.trim() || "SUB_001";
    downloadCSV(`serie_temporal_${subj}.csv`, state.timeseriesLog);
  });

  el.btnExportTrials.addEventListener("click", () => {
    const subj = el.inputSubjectId.value.trim() || "SUB_001";
    downloadCSV(`resumen_bloques_${subj}.csv`, state.trialsSummaryLog);
  });

  el.btnClearData.addEventListener("click", () => {
    if (confirm("¿Seguro que deseas borrar los registros de la sesión actual?")) {
      state.timeseriesLog = [];
      state.trialsSummaryLog = [];
      state.phaseSamples = [];
      renderTrialsTable();
    }
  });

  el.btnBlindMode.addEventListener("click", () => {
    el.blindOverlay.classList.add("active");
    requestWakeLock();
  });

  el.btnExitBlindMode.addEventListener("click", () => {
    el.blindOverlay.classList.remove("active");
  });

  // Initial canvas render
  drawTelemetryChart(state.baselineHR * 1.15);
}

document.addEventListener("DOMContentLoaded", initEventListeners);
