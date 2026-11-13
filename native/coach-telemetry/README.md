# Telemetría de Coaches

Este complemento escucha la configuración `bus_job` del SDK de SCS y publica los datos del encargo en memoria compartida local. La app combina esa señal con la posición y el resto de los datos de RenCloud.

Para recompilar la DLL incluida, ejecuta desde la raíz del proyecto con Zig 0.15.2 para Windows:

```powershell
zig c++ -target x86_64-windows-gnu -O2 -shared -s -nostdlib++ native/coach-telemetry/CoachTelemetry.cpp native/coach-telemetry/exports.def -o assets/coach-telemetry.dll
```

Las cabeceras incluidas proceden del [SDK de telemetría 1.16 de SCS](https://modding.scssoft.com/wiki/Documentation/Engine/SDK/Telemetry). Su licencia está en [sdk/LICENSE.txt](sdk/LICENSE.txt).
