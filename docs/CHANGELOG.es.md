# Historial de versiones

[English changelog](../CHANGELOG.md) · [Volver a la guía](README.es.md)

## Última versión · v0.9.1

- Inicio más claro, aviso explícito de ejemplo/repetición y barra inferior sin duplicación en Inicio/Viaje.
- Protección de cambios sin guardar al navegar o salir: seguir editando, descartar o guardar y continuar al destino solicitado.
- Deshacer por apartado, contador de cambios, indicación de reinicio y errores de guardado dentro de Ajustes.
- Opciones avanzadas plegadas que la búsqueda puede abrir automáticamente.
- Detalle del Diario junto a la lista, selección más clara y textos completos disponibles.
- Controles de repetición fijos y estados de reproducción, pausa y finalización diferenciados.
- Dibujo agrupado, superficies con doble búfer, conservación del desplazamiento y actualización visual aplazada durante la interacción.
- Ajustes adaptables a ventana estrecha y texto al 200 %, datos antiguos señalados y restauración de ventana maximizada.
- Incluye la base de rendimiento y diagnóstico de v0.9.0 que todavía no se había publicado.

[Detalle de los 50 puntos](QUALITY-0.9.1.md). El reconocimiento en una partida real y los monitores con DPI distintos requieren validación manual.

## v0.9.0

- Medición local de navegación y bloqueos, con informe que se revisa antes de guardar. No se envía automáticamente y no incluye jugadores, rutas, coordenadas ni capturas.
- Precarga limitada de grabaciones durante la inactividad, consultas del Diario y gráficos en segundo plano con cancelación.
- Filtros, orden, selección y posición de Diario/Repeticiones conservados entre aperturas.
- Menos dibujo de secciones ocultas, caché de imágenes limitada por memoria y mantenimiento de archivos fuera de la interfaz.
- Ayuda integrada: conexión, último intento del detector, mediciones, portadas por país/vehículo y actualizaciones.
- Confirmación de lecturas del detector, vencimiento de resultados antiguos y reconocimiento visual del modo foto sin depender siempre del texto.
- Portada genérica de respaldo si falla la del país, con comprobaciones y reintentos limitados.
- Texto hasta el 200 %, espacios y títulos coherentes, y acciones útiles en pantallas vacías.
- Actualización verificada y recuperación de la versión anterior accesibles desde Ajustes y Ayuda.

Consulta la [lista de implementación y verificación](QUALITY-0.9.0.md). Sigue pendiente validar el detector en una partida real y los cambios entre monitores con distintas escalas.

## v0.8.8

- Corrige las pausas restantes al abrir Ajustes y cambiar de pestaña.
- Agrupa la distribución y el dibujo de los controles, y prepara los estilos antes de crear sus ventanas nativas.
- Evita repetir mediciones cuando el ancho no cambia y abre directamente el apartado solicitado.
- Presenta cada página en una actualización conjunta y conserva las animaciones de la barra lateral y los botones.
- Corrige cálculos repetidos al ajustar Viaje y restaura el foco al terminar la actualización.

La comprobación con ventana visible midió unos 0,4–0,6 segundos al abrir Ajustes, frente a 3,3 segundos antes. Las pestañas preparadas se dibujaron en unos 16–30 ms. Son resultados de esta máquina y este contenido.

### v0.8.7

- Diario y Repeticiones conservan su pantalla, selección, filtros y desplazamiento.
- Ajustes se abre en la ventana principal y prepara los controles de cada apartado al visitarlo.
- Transiciones más ligeras, tabla de viajes con celdas bajo demanda y catálogo de grabaciones reutilizable.
- Lectura de repeticiones en segundo plano con progreso y cancelación.
- Mejoras de menús, estados vacíos, foco, botones, miniaturas y tamaños de texto.
- Menos dibujo en mapas y gráficos y mejor ajuste de ventanas estrechas.

Se comprobaron las funciones principales con 1.800 viajes de demostración, los dos idiomas, temas claro/oscuro, texto al 150% y 120 cambios consecutivos por sesión de prueba. Las vueltas al Diario midieron 24–27 ms en la prueba local final, frente a 342–373 ms antes. Quedan pruebas durante una partida real y entre monitores con escalas distintas.

### v0.8.6

- Inicio prioriza la ruta y las métricas disponibles. La barra de viaje permite abrir el detalle desde cualquier sección.
- La interfaz usa tonos grafito, un acento configurable y controles unificados para navegación, ajustes y grabaciones.

### v0.8.5

- Cambios de sección y pestaña con una transición corta. La biblioteca de grabaciones se carga en segundo plano.
- El modo foto reconoce el marco del visor del mando, además del texto. Al confirmar un cambio, la app actualiza la actividad de Discord de inmediato.

### v0.8.4

- Se añadieron transiciones en la navegación, los cambios de portada y actividad, las cifras de viaje, el mapa, el Diario y el reproductor. Respetan «Reducir movimiento».
- La detección experimental de mapa y modo foto puede leer ETS2 aunque el juego no notifique una pausa. El diagnóstico distingue una imagen no disponible de un texto que no coincide.

### v0.8.3

- Diario, Repeticiones, detalles y comparaciones se abren dentro de la ventana principal. Volver a la lista conserva la selección y los filtros.

### v0.8.2

- Inicio y Viaje tienen una composición más limpia, con conexión fuera de la tarjeta, ayuda plegable, indicadores de ruta más claros y un mapa más alto.
- Ajustes usa navegación lateral con iconos, vista previa compacta de Discord y muestras de tema claro y oscuro.
- Diario, Repeticiones y ventanas secundarias comparten el estilo del panel. Se renovaron las portadas para mantener visibles los detalles de las banderas.

### v0.8.1

- Se pueden compartir país y bandera sin mostrar la ciudad. Las interrupciones breves de lectura conservan la actividad unos segundos.
- El Diario guarda sesiones de pasajeros y puede guardar conducción libre. El pago de carga se registra cuando ETS2 lo informa. Los trayectos de pasajeros observados aparecen como sesiones cerradas.
- Mapa y modo foto tienen interruptores independientes. El reconocimiento de pantallas sigue siendo experimental.
- Se añadió recuperación de ajustes dañados y de restauraciones interrumpidas.

### v0.8.0

- Inicio muestra el estado de la publicación, el motivo de una portada genérica y una acción según el estado.
- Las banderas, los nombres y los vehículos tienen un encuadre común. Ajustes permite comparar las portadas en español e inglés.
- El Diario incorpora filtros individuales y comparación visible. Las repeticiones muestran eventos agrupados, intervalos sin datos y duración del tramo seleccionado.

### v0.7.9

- Completa el país de 26 ciudades para que Discord muestre la portada correspondiente al pasar por ellas.

### v0.7.8

- Portadas de Discord revisadas en miniatura. La vista previa permite comparar camión y autobús y probar cinco estados.
- El mapa muestra el rumbo del vehículo, distingue paradas y destaca los eventos seleccionados durante una repetición.
- El diario facilita comparar viajes y agrupa visualmente las fechas. Ajustes añade búsqueda con borrado rápido, muestras de tema y escala de texto del 150 %.

### v0.7.7

- Ajustes de diseño en Inicio, Viaje, Diario, Repeticiones y Ajustes.
- Detección experimental del mapa y modo foto mediante los textos visibles de ETS2 en primer plano. Compatible con etiquetas en español e inglés. El análisis usa el reconocimiento de texto de Windows y se puede desactivar en Ajustes de Discord.

### v0.7.6

- Discord muestra una sola portada. El autobús y el camión tienen portadas propias, con el mismo estilo por país.
- En el menú de la bandeja, **Actividad de ETS2** permite mostrar «Tomando fotos», «Mirando el mapa» o «Personalizando el camión/autobús». El modo automático muestra «Tomando fotos» brevemente cuando el juego guarda una captura PNG en su carpeta de capturas durante una pausa. El SDK solo informa de la pausa general, por lo que mapa y personalización se seleccionan desde el menú.

### v0.7.5

- Discord y el panel distinguen entre una ruta de pasajeros y la conducción libre en autobús en el DLC [Coaches](https://blog.scssoft.com/2026/11/coaches-release.html). La ruta y la distancia aparecen cuando ETS2 las comparte. Se conserva la portada del país según la configuración de privacidad.
- El ejecutable prepara automáticamente el complemento de Coaches. Si ETS2 estaba abierto durante la preparación, reinícialo para activar la lectura de rutas de pasajeros.

### v0.7.4

- El catálogo incluye las 14 ciudades del DLC Islandia para ETS2 1.62. Los identificadores se comprobaron con los datos del DLC y las posiciones se tomaron del [mapa de Trucker Mudgeon](https://github.com/truckermudgeon/truckermudgeon.github.io/blob/95962983980f8230a22182f3d6c7d0f0620d38cf/ets2-search.geojson).
- La imagen de actividad de Discord cambia según el país detectado y el idioma de Discord elegido en la app. Las portadas usan banderas de [Flagpedia](https://flagpedia.net/download) y un camión generado para el proyecto.

### v0.7.3

- El estado del viaje separa el tiempo al volante del tiempo detenido y distingue las mediciones observadas de la distancia informada por ETS2.
- El Diario incluye estadísticas por período, objetivos mensuales y un calendario anual. Las repeticiones tienen gráficos, marcadores con notas y un bucle A–B.
- Los datos locales muestran el almacenamiento utilizado y permiten inspeccionar copias verificadas antes de restaurar categorías concretas.
- Discord ofrece perfiles desde la bandeja y una vista previa de plantillas con contador de bytes.

### v0.7.2

Mejoras de fiabilidad en telemetría, actualizaciones y OBS. El canal **Stable** muestra versiones estables. **Preview** también muestra versiones de prueba.

## Novedades anteriores

- **Panel.** Cambia entre Completo, Conducción y Transmisión desde Más opciones. También hay una ventana flotante, búsqueda en Ajustes, botones fijables, formato de hora, millas y avisos configurables.
- **Mapa.** El menú del mapa incluye rumbo, seguimiento, búsqueda de ciudades, capas y recorrido completo. La escala usa unidades del mapa del juego. El aviso de ruta indica cambios grandes en la distancia o llegada estimada.
- **Discord.** Puedes personalizar el texto y el temporizador, ocultar toda la actividad temporalmente, aplicar reglas de ubicación y usar un atajo de pausa total. La actividad usa la imagen general del juego.
- **OBS.** En Ajustes → OBS puedes copiar una dirección local y pegarla en una fuente de navegador. Puedes elegir qué datos mostrar, tamaño, posición y texto de espera. La fuente funciona mientras la app esté abierta.
- **Grabaciones y diario.** Activa la grabación automática por entrega o pausa una grabación manualmente. Usa Ctrl + Alt + F12 para marcar un momento mientras grabas, y abre la repetición para nombrarlo. También puedes recortar una copia, comparar dos repeticiones, consultar estadísticas mensuales por camión y mover viajes entre conductores. La papelera conserva los elementos durante 30 días.
- **Mantenimiento.** Ajustes incluye límite de espacio y copias programadas mientras la app está abierta. La guía de conexión permite comprobar la actividad y exportar un informe sin rutas personales. Más opciones incluye la prueba local de conducción, entrega, pausa y desconexión.
