# Refinamiento visual y de uso · v0.9.1

Seguimiento de los últimos 50 puntos solicitados. Algunos refuerzan mecanismos existentes; otros añaden comportamientos nuevos. Se publica también la base de v0.9.0, documentada por separado. La lista describe medidas concretas, no una garantía de ausencia de parpadeos o latencia en todos los equipos.

| # | Resultado | Código o comprobación |
|---|---|---|
| 1 | Reducir destellos al cambiar de sección | Superficies con doble búfer y fondo del tema. |
| 2 | Evitar mostrar construcción parcial | `UiRedrawScope`, preparación de Ajustes y actualización agrupada. |
| 3 | Mantener los píxeles anteriores durante el cambio | Se suspende el dibujo del contenedor hasta finalizar la sustitución; sin captura bitmap de toda la ventana. |
| 4 | Reducir cambios tardíos de distribución | Distribución agrupada del marco y comprobación de tamaño sin cambios en Inicio. |
| 5 | Respuesta al menú | Selección inmediata, apertura diferida y prioridad del último destino. |
| 6 | No interferir al escribir | Filtro de interacción aplaza actualizaciones visuales brevemente; la lectura/publicación continúa. |
| 7 | Conservar desplazamiento | Captura/restauración de la posición al refrescar Inicio y Viaje. |
| 8 | No quitar el foco | Los refrescos no llaman a Focus; la restauración de foco pertenece a la navegación. |
| 9 | No sobrescribir una selección reciente | Generación de selección frente al resultado asíncrono del Diario. |
| 10 | Detectar recortes | Auditoría de alturas de etiquetas y anchura de acciones; casos de ventana estrecha y texto al 200 %. |
| 11 | Títulos proporcionados | Títulos principales y de repetición ajustados; actividad de Inicio a 18 pt. |
| 12 | Controles coherentes | Fábricas y alturas adaptadas a fuente/DPI; acciones de Ajustes limitadas al ancho disponible. |
| 13 | Espaciado consistente | Uso de márgenes de 4/8/16 y eliminación de separaciones irregulares del viaje/vehículo. |
| 14 | Alineación compartida | Contenedores TableLayout/FlowLayout y márgenes comunes. |
| 15 | Evitar espacio sin utilidad | Se retira la barra duplicada en Inicio/Viaje y el estado de Ajustes cuando no hay cambios. |
| 16 | Menos competencia visual en Inicio | Título de actividad más contenido, explicación de campos plegada y barra duplicada oculta. |
| 17 | Ruta y actividad al frente | Se conserva el recorrido antes de la presencia y sus acciones. |
| 18 | Vehículo plegado | Detalles secundarios ocultos inicialmente y apertura explícita. |
| 19 | Conexión compacta | Estados breves; la antigüedad se añade solo tras recibir datos, sin repetir la espera inicial. |
| 20 | Ejemplos inequívocos | Aviso persistente de demo/repetición y título de vista de ejemplo. |
| 21 | Portada adaptable | Tamaño existente según espacio y altura; no se amplía hasta desplazar las acciones. |
| 22 | Proporción de imágenes | PictureBox Zoom y miniaturas proporcionadas. |
| 23 | Espacio durante carga | Tamaño reservado y recurso de respaldo mientras se prepara una imagen remota. |
| 24 | Cambio de país discreto | Transición breve de la portada, respetando reducción de movimiento. |
| 25 | Respaldo explicado | Nota de portada genérica por ubicación oculta/desconocida o imagen no disponible. |
| 26 | Texto junto a portada | Menor título, texto envolvente y distribución apilada cuando falta ancho. |
| 27 | Contenido largo | Límites de ancho y ajuste de texto de ruta, presencia y detalles. |
| 28 | Barra del viaje contenida | Alturas según fuente; metadatos secundarios omitidos si falta altura. |
| 29 | Sin repetición del viaje | Barra retirada en Inicio/Viaje y en Ajustes con poca altura. |
| 30 | Ruta completa disponible | Ayudas emergentes sobre rutas/celdas y acción del viaje. |
| 31 | Ajustes habituales frente a avanzados | Acciones menos frecuentes plegadas en Juego/Datos locales. |
| 32 | Explicaciones útiles | Se conservan indicaciones de privacidad, lector, actualización y formatos; se explican las decisiones de guardado. |
| 33 | Reinicio señalado | Cambiar el tema nativo añade la indicación al estado de cambios. |
| 34 | Cambios pendientes visibles | Cuenta de campos distintos a sus valores al abrir Ajustes. |
| 35 | Protección contra pérdida | Navegación/salida ofrecen seguir editando, guardar o descartar dentro de la misma ventana. |
| 36 | Guardar/Cancelar accesibles | Pie fuera del área desplazable, con ancho y ajuste adaptables. |
| 37 | Búsqueda precisa | Abre el apartado/grupo avanzado, desplaza al control, enfoca y resalta el resultado. |
| 38 | Errores accionables | Fallos de inicio/guardado explican cómo reintentar; avisos breves no desplazan la página. |
| 39 | Error cerca de la acción | Error de guardado dentro del pie; error de inicio enfoca la opción de inicio automático. |
| 40 | Menos interrupciones informativas | Confirmación de guardado y fallo de atajo en el aviso; las decisiones de datos se mantienen explícitas. |
| 41 | Filas legibles | Espaciado de celdas y separación de fechas fina. |
| 42 | Selección legible | Fondo de selección con acento moderado y texto del tema; alto contraste nativo. |
| 43 | Menos acciones sin selección | Comparación oculta sin selección y acciones dependientes deshabilitadas. |
| 44 | Detalle sin perder la lista | Panel de detalle del viaje, lateral o inferior según ancho. |
| 45 | Transporte fijo | Deslizador, tiempo, reproducción y velocidad fuera del contenido desplazable. |
| 46 | Estados de repetición explícitos | Preparando, reproduciendo, en pausa o terminada; no se confunden pausa y final. |
| 47 | Motivo del detector | Distingue desactivación, privacidad, ejemplo, selección manual, captura fallida, falta de reconocimiento y confirmación. |
| 48 | Publicación diferenciada | Preparada sin enviar, enviada con hora y confirmada por Discord. |
| 49 | Antigüedad de datos | Aviso tras 4,5 segundos sin nuevas lecturas; los ejemplos no se etiquetan como datos reales. |
| 50 | Ciclo de vida | Cancelación al dejar páginas, limpieza al cerrar y restauración del estado maximizado. |

## Verificación

```powershell
dotnet test tests/Ets2DiscordPresence.Tests -c Release --artifacts-path dist/verification
dotnet run --project tests/Ets2DiscordPresence.UiChecks -c Release -- "$PWD/dist/ui-checks" night en 100 40
dotnet run --project tests/Ets2DiscordPresence.UiChecks -c Release -- "$PWD/dist/ui-checks" day es 200 40
./scripts/build-release.ps1 -Tag v0.9.1
```

UiChecks usa datos ficticios aislados, una grabación sintética y 1800 viajes. Comprueba detalle junto a lista, transporte dentro de la ventana, protección de borradores, guardar y continuar, deshacer apartado, foco/búsqueda, navegación repetida y recursos acotados. No instala el lector ni inicia Discord.

La auditoría de distribución distingue recortes de altura/acciones de los casos con scroll y puntos suspensivos intencionales. No sustituye una inspección visual ni detecta todos los problemas de accesibilidad.

Siguen pendientes las comprobaciones en una partida real, la observación desde otra cuenta de Discord y monitores con escalas distintas. La detección de mapa/foto depende de la ventana capturable y sus textos/controles visibles. Las cifras locales de rendimiento no son una promesa para otros equipos.
