# Guía Práctica de Hardware: Cómo Comprar o Construir el Dispositivo Vibrador Cardíaco

Para tus experimentos tienes **3 rutas posibles** (desde comprar un módulo ya ensamblado con correa que no requiere soldar un solo cable, hasta armar tu propio prototipo con **ESP32** por ~\$20 USD).

> [!IMPORTANT]
> **¿Por qué usar un ESP32 en lugar de un Arduino Uno clásico?**
> El Arduino Uno tradicional es grande y **no tiene Bluetooth Low Energy (BLE)**. En cambio, una placa **ESP32** (como el *Seeed Studio XIAO ESP32S3* o *M5StickC Plus2*) se programa exactamente igual en el **Arduino IDE** o en **Visual Studio**, cuesta menos de \$10 USD, es del tamaño de una moneda y ya trae **Bluetooth Low Energy integrado** para conectarse directo a la banda del pecho (Polar H10/H9/Coospo).

---

## Ruta 1: Comprar el hardware ya encapsulado tipo reloj (Cero soldadura, listo en 5 minutos)

Si no quieres soldar cables ni imprimir cajas en 3D, puedes comprar un microcontrolador comercial tipo reloj que ya trae pantalla, batería recargable, correa de muñeca, Bluetooth Low Energy y motor vibrador:

1. **M5StickC PLUS2 (~$20 USD) + Sombrero Vibrador "M5Stack Vibration Hat" (~$4 USD):**
   * Se enchufan como un bloque de Lego (sin cables ni soldadura), incluye correa de velcro para la muñeca y se le carga directamente nuestro archivo [`InteroExpos_HapticWearable_ESP32.ino`](InteroExpos_HapticWearable_ESP32.ino).
2. **LilyGO T-Watch S3 (~$38 USD):**
   * Es un reloj completo programable con chip ESP32-S3, Bluetooth 5.0 y **controlador háptico profesional DRV2605L** ya soldado adentro.
3. **Bangle.js 2 (~$90 USD):**
   * Reloj de investigación abierto con Bluetooth y motor de vibración en la muñeca.

---

## Ruta 2: Construir tu propio Dispositivo Háptico de Laboratorio (~$22 USD)

Si quieres construir una pulsera o parche torácico ultraligero (ideal para que el participante tenga las manos 100% libres durante la exposición), esta es la lista exacta de materiales (disponibles en Amazon, MercadoLibre, Mouser o DigiKey):

### Lista de Materiales (BOM)

| Componente | Modelo Recomendado | Precio Aprox. | Función en el Experimento |
| :--- | :--- | :--- | :--- |
| **1. Sensor Cardíaco (Pecho)** | **Polar H10**, **Polar H9** o **Coospo H6** | \$30 – \$85 USD | Mide el ECG en el pecho y transmite FC + intervalos R-R por Bluetooth (`0x180D`) sin errores por movimiento. *(Evita los sensores ópticos verdes de dedo/oreja de Arduino porque fallan cuando el sujeto mueve la mano en la exposición).* |
| **2. Microcontrolador BLE** | **Seeed Studio XIAO ESP32C3** o **ESP32 DevKit V1** | \$5 – \$9 USD | Recibe la frecuencia cardíaca por Bluetooth, calcula la derivada de alivio ($\frac{d\text{FC}}{dt}$) y controla el motor. |
| **3. Módulo Motor Vibrador** | **Módulo Motor Vibración MOSFET PWM para Arduino** (moneda plana 1027) *o* **Adafruit DRV2605L + Motor LRA** | \$3 – \$9 USD | Es el disco plano (igual al de los celulares) que se pega con una banda elástica en la muñeca interna o sobre el esternón del participante. |
| **4. Batería (Opcional)** | **Batería LiPo 3.7V 400mAh** (o cable USB largo conectado al PC) | \$5 USD | El chip *XIAO ESP32C3* ya tiene cargador de batería integrado. |

---

## Diagrama de Conexión (Solo 3 hilos)

El módulo de vibración para Arduino tiene 3 pines (`VCC`, `GND`, `IN/PWM`) y se conecta así al ESP32:

```text
[ ESP32 / XIAO ESP32 ]                 [ Módulo Motor Vibrador MOSFET ]
      Pin 3.3V / 5V  ---------------->  VCC (+)
      Pin GND        ---------------->  GND (-)
      Pin GPIO 4     ---------------->  IN / SIG (Señal PWM de vibración)
```

---

## Integración Directa con Microsoft Excel ("Data Streamer")

Una ventaja enorme de usar esta placa con el ecosistema **Microsoft** de la universidad es que **Microsoft Excel** incluye un complemento oficial llamado **Microsoft Data Streamer** pensado para leer placas Arduino/ESP32 en tiempo real:

1. En **Microsoft Excel**, ve a **Archivo $\rightarrow$ Opciones $\rightarrow$ Complementos $\rightarrow$ Complementos COM** y marca la casilla **Microsoft Data Streamer for Excel**.
2. Conecta el ESP32 por USB al computador y abre la pestaña **Data Streamer $\rightarrow$ Conectar un dispositivo** en Excel.
3. Nuestro firmware [`InteroExpos_HapticWearable_ESP32.ino`](InteroExpos_HapticWearable_ESP32.ino) envía cada segundo una fila limpia que llena automáticamente las columnas de Excel en vivo (`Tiempo_ms`, `FC_Real`, `Linea_Base`, `Intervalo_RR`, `Derivada_10s`, `BPM_Haptico`, `Modo_Experimental`), y desde una celda de Excel puedes cambiar el modo experimental (`MODE:1`, `MODE:3`, `GAMMA:0.35`, `FADE:80`) sin tocar código.
