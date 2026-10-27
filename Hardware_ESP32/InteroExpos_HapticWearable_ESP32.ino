/**
 * InteroExpos Lab — Firmware para Pulsera/Dispositivo Háptico Experimental (ESP32)
 * Compatible con: ESP32 DevKit, Seeed Studio XIAO ESP32S3 / ESP32C3, M5StickC Plus2
 *
 * Funciones:
 * 1. Se conecta directamente por Bluetooth Low Energy (BLE Client) a la banda pectoral
 *    (Polar H10 / H9 / Garmin / Coospo) buscando el servicio estándar Heart Rate (0x180D).
 * 2. Calcula en tiempo real la FC (BPM), intervalos R-R (ms) y la primera derivada (dFC/dt en 10s).
 * 3. Controla un motor vibratorio (vía PWM / Transistor MOSFET o driver háptico DRV2605L)
 *    aplicando las condiciones experimentales (Continuo 1:1, Pico, Alivio, Ganancia de Pendiente, Fading).
 * 4. Envía toda la telemetría por puerto Serial (USB) o Bluetooth hacia la consola Microsoft (.NET / Excel).
 */

#include <Arduino.h>
#include <BLEDevice.h>

// ============================================================================
// CONFIGURACIÓN DE PINES DEL HARDWARE (ESP32)
// ============================================================================
constexpr int PIN_MOTOR_PWM = 4;      // Pin GPIO conectado al módulo vibrador MOSFET o entrada PWM
constexpr int PIN_LED_STATUS = 2;     // LED indicador de estado BLE
constexpr int PWM_CHANNEL = 0;
constexpr int PWM_FREQ = 250;         // Frecuencia base PWM para actuador háptico
constexpr int PWM_RESOLUTION = 8;     // 0 - 255 (permite modular intensidad exacta 0% a 100%)

// UUIDs estándar Bluetooth SIG para Heart Rate
static BLEUUID serviceUUID("0000180d-0000-1000-8000-00805f9b34fb");
static BLEUUID charUUID("00002a37-0000-1000-8000-00805f9b34fb");

// ============================================================================
// VARIABLES DE ESTADO PSICOFISIOLÓGICO Y EXPERIMENTAL
// ============================================================================
enum ExperimentalMode {
  MODE_NONE = 0,            // Control sin vibración
  MODE_CONTINUOUS_1TO1 = 1, // Pulso 1:1 en cada latido
  MODE_AROUSAL_PEAK = 2,    // Solo vibra sobre umbral (+% Línea Base)
  MODE_RELIEF_SLOPE = 3,    // Solo vibra en pendiente negativa (dFC/dt <= -2.5 BPM/10s)
  MODE_SLOPE_GAIN = 4,      // Exp 2: Perturbación de pendiente gamma tras pico real
  MODE_ADAPTIVE_FADING = 5, // Exp 3: Atenuación de intensidad PWM (100% -> 15%)
  MODE_SHAM = 6             // Control activo desacoplado
};

ExperimentalMode currentMode = MODE_CONTINUOUS_1TO1;
int baselineHR = 72;
float thresholdPct = 15.0f;
float reliefSlopeThreshold = -2.5f;
float slopeGainGamma = 1.0f;
uint8_t fadingIntensityPwm = 255; // 255 = 100%, 204 = 80%, 153 = 60%, 89 = 35%, 38 = 15%

volatile int currentHR = 0;
volatile int lastRRms = 0;
int phasePeakHR = 0;
bool peakReachedInBlock = false;

// Buffer circular de 10 segundos para calcular la derivada dFC/dt
constexpr int HISTORY_SIZE = 10;
int hrRingBuffer[HISTORY_SIZE] = {0};
int hrBufferIndex = 0;
bool hrBufferFull = false;
float currentSlope10s = 0.0f;

// Planificador de pulsos hápticos no bloqueante
int targetHapticBPM = 0;
bool useDoublePulseRelief = false;
unsigned long lastBeatTimestampMs = 0;

// Variables de conexión BLE
static boolean doConnect = false;
static boolean isConnected = false;
static boolean doScan = false;
static BLERemoteCharacteristic* pRemoteCharacteristic;
static BLEAdvertisedDevice* myDevice;

// ============================================================================
// ACTUALIZACIÓN DE LA META HÁPTICA SEGÚN CONDICIÓN EXPERIMENTAL
// ============================================================================
void updateHapticTarget() {
  int hr = currentHR;
  if (hr <= 0) {
    targetHapticBPM = 0;
    return;
  }

  float thresholdHR = baselineHR * (1.0f + thresholdPct / 100.0f);
  if (hr >= thresholdHR) peakReachedInBlock = true;
  if (hr > phasePeakHR) phasePeakHR = hr;

  bool isAboveThreshold = (hr >= thresholdHR);
  bool isDecelerating = (currentSlope10s <= reliefSlopeThreshold) && (peakReachedInBlock || hr > baselineHR + 4);
  useDoublePulseRelief = false;

  switch (currentMode) {
    case MODE_NONE:
      targetHapticBPM = 0;
      break;

    case MODE_CONTINUOUS_1TO1:
      targetHapticBPM = hr;
      break;

    case MODE_AROUSAL_PEAK:
      targetHapticBPM = isAboveThreshold ? hr : 0;
      break;

    case MODE_RELIEF_SLOPE:
      if (isDecelerating) {
        targetHapticBPM = max(55, (int)(hr * 0.85f));
        useDoublePulseRelief = true;
      } else {
        targetHapticBPM = 0;
      }
      break;

    case MODE_SLOPE_GAIN: {
      int peak = max(hr, phasePeakHR);
      int drop = peak - hr;
      int modulated = (int)round(peak - slopeGainGamma * drop);
      targetHapticBPM = constrain(modulated, 45, 195);
      break;
    }

    case MODE_ADAPTIVE_FADING:
      targetHapticBPM = hr;
      break;

    case MODE_SHAM: {
      float wave = sin(millis() / 6000.0f) * 2.5f;
      targetHapticBPM = (int)round(baselineHR + wave);
      break;
    }
  }
}

// ============================================================================
// CALLBACK DE NOTIFICACIÓN BLE GATT 0x2A37 (BANDA PECTORAL)
// ============================================================================
static void notifyCallback(
  BLERemoteCharacteristic* pBLERemoteCharacteristic,
  uint8_t* pData,
  size_t length,
  bool isNotify) {

  if (length < 2) return;

  uint8_t flags = pData[0];
  bool is16Bit = (flags & 0x01) != 0;
  bool hasEnergy = (flags & 0x08) != 0;
  bool hasRR = (flags & 0x10) != 0;

  int offset = 1;
  if (is16Bit) {
    currentHR = pData[offset] | (pData[offset + 1] << 8);
    offset += 2;
  } else {
    currentHR = pData[offset];
    offset += 1;
  }

  if (hasEnergy) offset += 2;

  if (hasRR && offset + 1 < (int)length) {
    uint16_t rawRR = pData[offset] | (pData[offset + 1] << 8);
    lastRRms = (int)((rawRR / 1024.0f) * 1000.0f);
  } else if (currentHR > 0) {
    lastRRms = 60000 / currentHR;
  }

  // Actualizar ventana de 10 segundos y calcular primera derivada (dFC/dt)
  int oldestHR = hrRingBuffer[hrBufferIndex];
  hrRingBuffer[hrBufferIndex] = currentHR;
  hrBufferIndex = (hrBufferIndex + 1) % HISTORY_SIZE;
  if (hrBufferIndex == 0) hrBufferFull = true;

  if (hrBufferFull) {
    currentSlope10s = (float)(currentHR - oldestHR);
  }

  updateHapticTarget();

  // Imprimir fila CSV por Serial para lectura directa en Excel Data Streamer o Visual Studio
  Serial.printf("DATA,%lu,%d,%d,%d,%.2f,%d,%d\n",
                millis(), currentHR, baselineHR, lastRRms, currentSlope10s, targetHapticBPM, (int)currentMode);
}

// ============================================================================
// DISPARO DEL MOTOR HÁPTICO (SIMPLE O DOBLE PULSO DE ALIVIO)
// ============================================================================
void triggerHapticBeat() {
  uint8_t intensity = (currentMode == MODE_ADAPTIVE_FADING) ? fadingIntensityPwm : 255;

  if (useDoublePulseRelief) {
    // Doble pulso suave para señalizar alivio autonómico
    analogWrite(PIN_MOTOR_PWM, intensity * 0.75);
    delay(35);
    analogWrite(PIN_MOTOR_PWM, 0);
    delay(65);
    analogWrite(PIN_MOTOR_PWM, intensity);
    delay(50);
    analogWrite(PIN_MOTOR_PWM, 0);
  } else {
    // Pulso único sincronizado con sístole/latido
    analogWrite(PIN_MOTOR_PWM, intensity);
    delay(50);
    analogWrite(PIN_MOTOR_PWM, 0);
  }
}

// ============================================================================
// GESTIÓN DE CONEXIÓN BLE CLIENTE
// ============================================================================
class MyClientCallback : public BLEClientCallbacks {
  void onConnect(BLEClient* pclient) {}
  void onDisconnect(BLEClient* pclient) {
    isConnected = false;
    Serial.println("STATUS,BLE_DISCONNECTED");
  }
};

bool connectToServer() {
  BLEClient* pClient = BLEDevice::createClient();
  pClient->setClientCallbacks(new MyClientCallback());
  pClient->connect(myDevice);

  BLERemoteService* pRemoteService = pClient->getService(serviceUUID);
  if (pRemoteService == nullptr) {
    pClient->disconnect();
    return false;
  }

  pRemoteCharacteristic = pRemoteService->getCharacteristic(charUUID);
  if (pRemoteCharacteristic == nullptr) {
    pClient->disconnect();
    return false;
  }

  if (pRemoteCharacteristic->canNotify()) {
    pRemoteCharacteristic->registerForNotify(notifyCallback);
  }

  isConnected = true;
  Serial.println("STATUS,BLE_CONNECTED_TO_BAND");
  return true;
}

class MyAdvertisedDeviceCallbacks : public BLEAdvertisedDeviceCallbacks {
  void onResult(BLEAdvertisedDevice advertisedDevice) {
    if (advertisedDevice.haveServiceUUID() && advertisedDevice.isAdvertisingService(serviceUUID)) {
      BLEDevice::getScan()->stop();
      myDevice = new BLEAdvertisedDevice(advertisedDevice);
      doConnect = true;
      doScan = true;
    }
  }
};

// ============================================================================
// COMANDOS DESDE PC / VISUAL STUDIO / EXCEL DATA STREAMER POR PUERTO SERIAL
// ============================================================================
void checkSerialCommands() {
  if (!Serial.available()) return;
  String cmd = Serial.readStringUntil('\n');
  cmd.trim();

  if (cmd.startsWith("MODE:")) {
    currentMode = (ExperimentalMode)cmd.substring(5).toInt();
    phasePeakHR = currentHR;
    peakReachedInBlock = false;
  } else if (cmd.startsWith("LB:")) {
    baselineHR = cmd.substring(3).toInt();
  } else if (cmd.startsWith("GAMMA:")) {
    slopeGainGamma = cmd.substring(6).toFloat();
  } else if (cmd.startsWith("FADE:")) {
    // 0 a 255 (para Experimento 3 de Desvanecimiento Adaptativo)
    fadingIntensityPwm = (uint8_t)constrain(cmd.substring(5).toInt(), 0, 255);
  }
}

void setup() {
  Serial.begin(115200);
  pinMode(PIN_MOTOR_PWM, OUTPUT);
  analogWrite(PIN_MOTOR_PWM, 0);

  Serial.println("STATUS,INTEROEXPOS_ESP32_READY");
  BLEDevice::init("InteroExpos_Wearable");

  BLEScan* pBLEScan = BLEDevice::getScan();
  pBLEScan->setAdvertisedDeviceCallbacks(new MyAdvertisedDeviceCallbacks());
  pBLEScan->setInterval(1349);
  pBLEScan->setWindow(449);
  pBLEScan->setActiveScan(true);
  pBLEScan->start(5, false);
}

void loop() {
  checkSerialCommands();

  if (doConnect) {
    connectToServer();
    doConnect = false;
  }

  if (!isConnected && doScan) {
    BLEDevice::getScan()->start(0);
  }

  // Ejecutar latido háptico según el intervalo objetivo actual
  if (targetHapticBPM >= 40) {
    unsigned long intervalMs = 60000UL / (unsigned long)targetHapticBPM;
    unsigned long now = millis();
    if (now - lastBeatTimestampMs >= intervalMs) {
      lastBeatTimestampMs = now;
      triggerHapticBeat();
    }
  }
}
