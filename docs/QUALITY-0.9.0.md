# Calidad de interfaz · v0.9.0

Esta lista corresponde a las 50 mejoras solicitadas. Distingue cambios nuevos de mecanismos que ya existían y se conservaron o hicieron más accesibles. No implica que cada punto fuera una función inexistente ni garantiza latencias iguales en todos los equipos.

| # | Mejora aplicada o reforzada | Implementación / comprobación |
|---|---|---|
| 1 | Tiempos de navegación reales | `UiPerformance`, alcance de navegación y apartados de Ajustes. |
| 2 | Registro local de bloqueos | Pulso de la interfaz; conserva hasta 32 retrasos de cola de 150 ms o más. |
| 3 | Presupuestos de respuesta | 500 ms en primera visita, 100 ms en visitas preparadas; son objetivos, no garantías. |
| 4 | Precarga durante la inactividad | Hasta 32 cabeceras de grabaciones, sin crear controles y con cancelación al navegar. |
| 5 | Estructura antes de datos secundarios | Diario y Repeticiones aparecen antes de sus consultas; aviso de apertura en sección nueva. |
| 6 | Evitar escrituras/dibujos repetidos | Comparación de vistas y datos, celdas virtuales y gráficos retenidos. |
| 7 | No dibujar pestañas ocultas | Guardas de visibilidad en Inicio, Viaje, vehículo y vista previa. |
| 8 | Cancelación al abandonar | Diario, catálogo, reproducción, precarga y gráficos cancelan o descartan resultados antiguos. |
| 9 | Último clic tiene prioridad | Navegación diferida conserva solo el destino más reciente; generaciones de consultas. |
| 10 | Trabajo de archivos/red fuera de la interfaz | Consultas grandes, gráficos, mantenimiento automático, captura/OCR y descargas. La escritura del diario se conserva en su hilo propietario. |
| 11 | Agrupar distribución al redimensionar | `UiLayoutBatch`, retraso corto de resize y medidas de ancho reutilizadas. |
| 12 | Espacio estable al cargar | Espacio de portada reservado mientras hay clave de imagen y fila de avisos estable. |
| 13 | Caché de imágenes acotada | LRU de hasta 12 elementos / 8 MB; presupuesto reducido a 1,5 MB al ocultar. |
| 14 | Liberar recursos | Cancelación y disposición de bitmaps, fuentes y páginas; prueba de crecimiento de handles. |
| 15 | Menos trabajo al minimizar | Se omite dibujo y se reduce el pulso de diagnóstico; presencia y grabación permanecen activas. |
| 16 | Frecuencia según actividad | La consulta del juego se reduce con ahorro activado solo fuera de entregas y grabación. |
| 17 | Diario grande | Consulta de instantánea por lotes de 128 con cancelación, orden fuera de la interfaz y celdas bajo demanda. No fragmenta la búsqueda ni sus totales. |
| 18 | Metadatos reutilizados | Caché por tamaño/fecha y precarga de cabeceras. |
| 19 | Gráficos en segundo plano | Bitmaps preparados con datos, tamaño, fuente y métrica capturados; no se accede a controles desde el trabajador. |
| 20 | Pruebas repetibles de navegación | Proyecto `Ets2DiscordPresence.UiChecks`, ciclos configurables, conteo de páginas, handles y métricas acotadas. |
| 21 | Espaciado coherente | Normalización de márgenes/separadores a 4/8/16 px. |
| 22 | Jerarquía tipográfica | Títulos de sección de 20 pt, subtítulos y texto con tamaños compartidos; Segoe UI. |
| 23 | Menos bordes innecesarios | Se conserva la superficie plana y el énfasis en selección/foco; sin añadir paneles decorativos. |
| 24 | Iconos uniformes | Recursos Fluent existentes con tamaño/tinte comunes y escala DPI. |
| 25 | Selección clara | Estado de pestaña con texto, contraste e indicador; sincronizado con la navegación efectiva. |
| 26 | Transición del menú | Animación breve del estado seleccionado, sin desplazar páginas completas. |
| 27 | Respuesta de botones | Estados de pulsación/teclado/hover existentes, conservados en las nuevas acciones. |
| 28 | Valores suaves | Progreso y combustible interpolados; sin animar la distribución del contenido. |
| 29 | Evitar entradas repetitivas | Páginas retenidas y ausencia de animaciones completas al volver. |
| 30 | Reducir movimiento | Preferencia de usuario, animación de Windows, alto contraste y visibilidad controlan `CanAnimate`. |
| 31 | Contraste secundario | Paletas compartidas y estados deshabilitados; texto del sistema con alto contraste. |
| 32 | Controles coherentes | Estilo plano, fuentes compartidas y colores del tema para listas, entradas y nuevas superficies. Barras de desplazamiento nativas. |
| 33 | Texto/escala ampliados | Opciones de texto hasta 200 %, WinForms PerMonitorV2; prueba local a DPI 120 y texto 100/200 %. |
| 34 | Ventana estrecha | Barra lateral y navegación de Ajustes compactas, contenido desplazable y acciones que se ajustan. |
| 35 | Foco por teclado | Orden de tabulación, foco visible y recuperación diferida del control anterior. |
| 36 | Estados vacíos accionables | Quitar filtros/abrir ETS2 en Diario; abrir grabación/quitar búsqueda en Repeticiones. |
| 37 | Carga comprensible | Mensajes específicos de búsqueda del Diario, lectura de grabaciones y consulta de versiones. |
| 38 | Avisos sin desplazar contenido | Fila de estado de altura reservada y mensajes temporales. |
| 39 | Inicio centrado en el viaje | Ruta, actividad y conexión al frente; explicación de campos plegada. |
| 40 | Detalles en la misma ventana | Se conservan Diario/Repeticiones/Ajustes integrados y se incorpora Ayuda/revisión del informe. |
| 41 | Última presencia y hora | Vista del texto enviado, hora de envío y confirmación/hora separada en diagnóstico. |
| 42 | Estados de conexión distintos | Cerrado, esperando datos, conectado y reconectando; también en la barra lateral. |
| 43 | Reconectar Discord | Cliente RPC existente y generación de conexión para republicar tras reconectar; acción manual de republicación. |
| 44 | Estado del detector | Ayuda muestra resultado, hora, duración y motivos de desactivación/espera/fallo. |
| 45 | Casos del detector | Pruebas de etiquetas en español/inglés y retícula a 720p, 1080p, 1440p y 2160p. |
| 46 | Reducir falsos positivos | Dos coincidencias para confirmar, dos pérdidas para retirar, tolerancia al cambio de ventana y vencimiento. |
| 47 | Probar portadas | Selector de país y camión/autobús con recursos integrados, sin modificar la actividad real. |
| 48 | Portada de respaldo | Comprobación de disponibilidad con caché/reintento; recurso genérico por vehículo en caso de fallo. |
| 49 | Actualizar conservando ajustes | Actualizador existente con SHA-256, copia anterior y confirmación de arranque; acciones visibles en Juego/Ayuda. |
| 50 | Exportación revisable | Informe dentro de la app antes de guardar; sin rutas, nombres, coordenadas ni imágenes; sin envío automático. |

## Verificación reproducible

En PowerShell, desde la raíz del proyecto:

```powershell
dotnet test tests/Ets2DiscordPresence.Tests/Ets2DiscordPresence.Tests.csproj -c Release --artifacts-path dist/verification
dotnet run --project tests/Ets2DiscordPresence.UiChecks -c Release -- "$PWD/dist/ui-checks" night en 100 200
dotnet run --project tests/Ets2DiscordPresence.UiChecks -c Release -- "$PWD/dist/ui-checks" day es 200 40
./scripts/build-release.ps1 -Tag v0.9.0
```

El último argumento de UiChecks es el número de ciclos de tres secciones, entre 40 y 2000. Se usan 1800 viajes ficticios y una grabación sintética en el directorio de salida del proyecto de pruebas. No inicia el cliente Discord ni prepara el lector del juego. Genera capturas y un informe local. El test unitario de consulta usa 50 000 viajes.

Las mediciones del manejador reflejan trabajo de CPU/distribución de navegación. La medida adicional hasta `Application.Idle` está disponible cuando el bucle normal de mensajes de Windows alcanza la inactividad; no es FPS ni latencia del monitor. Los presupuestos no constituyen un test de velocidad universal. El bucle de comprobación con `DoEvents` puede dejar esa segunda medida sin completar.

## Límites que requieren una partida real

- La captura puede fallar con pantalla completa exclusiva o ventanas superpuestas. Ayuda explica el estado; probar ventana sin bordes sigue siendo una alternativa.
- La retícula solo reconoce su variante visual conocida. Con controles ocultos, otros idiomas o un diseño distinto, la detección puede no encontrar una coincidencia.
- Las capturas originales de la conversación ya no están disponibles en sus rutas temporales. Los casos nuevos no equivalen a volver a comprobar esas imágenes ni a una validación en directo.
- La reconexión visible desde otra cuenta de Discord, la actualización completa entre ejecutables y monitores con DPI diferentes requieren comprobación manual. Se mantienen las pruebas existentes del actualizador y de integridad de la publicación.
- Las pruebas de 600 cambios verifican una carga repetida acotada; no sustituyen una sesión continua de varias horas.
