# InteroExpos Lab — Ecosistema Microsoft para Investigación Universitaria

Este repositorio contiene la plataforma completa (software y documentación) adaptada al **ecosistema Microsoft** de la universidad para ejecutar la serie de **3 experimentos inéditos de psicología traslacional** sobre modulación interoceptiva háptica durante la exposición.

---

## 1. Entregables de Microsoft Office (Comité de Ética y Base de Datos)

* **[Protocolo_Investigacion_InteroExpos_Universidad.docx](Protocolo_Investigacion_InteroExpos_Universidad.docx)** — Documento en **Microsoft Word** con la fundamentación teórica, la justificación de originalidad estricta frente a estudios previos, la metodología de los 3 experimentos y la operacionalización de variables.
* **[Matriz_Datos_Experimentos_InteroExpos.xlsx](Matriz_Datos_Experimentos_InteroExpos.xlsx)** — Libro maestro en **Microsoft Excel** con 4 hojas preconfiguradas y **fórmulas automáticas**:
  1. `1_Participantes_y_Schandry`: Cálculo automático del índice de precisión interoceptiva ($I_{\text{acc}}$) en las ventanas de 25 s, 35 s y 45 s.
  2. `2_Bloques_y_Extincion`: Cálculo automático del Error de Calibración Interoceptiva ($\text{FC}_{\text{percibida}} - \text{FC}_{\text{real}}$), Magnitud de Violación de Expectativas, Alivio y Retorno del Miedo a las 48 h sin feedback.
  3. `3_Diccionario_Variables`: Diccionario metodológico listo para importar en Power BI, SPSS, JASP o R.
  4. `4_Diseno_y_Potencia`: Planificación de tamaño muestral ($1-\beta = 0.85, \alpha = 0.05$).

---

## 2. Solución Nativa en Visual Studio 2022 (.NET 8 MAUI / C#)

Para compilar como aplicación nativa en **Windows 10/11** y **Android** desde **Visual Studio 2022**:
1. Abre la solución **[InteroExposLab.sln](InteroExposLab.sln)** en Visual Studio 2022 (con la carga de trabajo *Desarrollo de la interfaz de usuario de aplicaciones multiplataforma de .NET MAUI* instalada).
2. Estructura de código en C#:
   * [`ExperimentalModels.cs`](InteroExposLab/Models/ExperimentalModels.cs): Modelos de datos y enumeración `FeedbackCondition` con las condiciones inéditas de los 3 experimentos.
   * [`BleHeartRateService.cs`](InteroExposLab/Services/BleHeartRateService.cs): Conexión GATT Bluetooth Low Energy (`0x180D` / `0x2A37`), decodificador binario de intervalos R-R ($1/1024\text{ s} \rightarrow \text{ms}$) y simulador fisiológico en C#.
   * [`HeartRateBiofeedbackEngine.cs`](InteroExposLab/Services/HeartRateBiofeedbackEngine.cs): Cálculo móvil de RMSSD, primera derivada cardíaca ($\frac{d\text{FC}}{dt}$ en ventana de 10 s), algoritmo de perturbación de pendiente ($\gamma$), desvanecimiento háptico adaptativo (*Adaptive Fading*) y control del motor vibratorio nativo (`Microsoft.Maui.Devices.Vibration`).
   * [`ExcelCsvExportService.cs`](InteroExposLab/Services/ExcelCsvExportService.cs): Exportador CSV con BOM UTF-8 que invoca el diálogo nativo de compartir hacia **Microsoft OneDrive / Teams / Excel**.

---

## 3. Ejecución Directa en Microsoft Edge y Azure Static Web Apps

Si deseas usar la versión Web/PWA sin compilar APK:
* **En Microsoft Edge (Windows o Android):** Levanta un servidor local o despliégalo en el tenant institucional de **Microsoft Azure Static Web Apps** (ya configurado mediante [staticwebapp.config.json](staticwebapp.config.json) con cabeceras `Permissions-Policy` para Bluetooth, Vibración y WakeLock).
* Los archivos CSV exportados desde la aplicación incluyen codificación `UTF-8 con BOM` para que **Microsoft Excel** reconozca automáticamente las columnas y caracteres especiales al abrirlos con doble clic o al subirlos a una carpeta compartida de **OneDrive / SharePoint** de la universidad.
