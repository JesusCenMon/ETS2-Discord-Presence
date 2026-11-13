# ETS2 Discord Presence

Muestra en Discord qué estás haciendo en **Euro Truck Simulator 2**: entrega, ciudades de origen y destino, distancia restante y, cuando se puede estimar, una ciudad cercana. Si no tienes una entrega, muestra «Explorando libremente» y, cuando la ubicación se puede estimar, «Conduciendo por España», «Conduciendo por Francia» o el país correspondiente.

## Instalar en Windows

1. Descarga `ETS2DiscordPresence.exe` desde [Releases](https://github.com/JesusCenMon/ETS2-Discord-Presence/releases) y ábrelo directamente.
2. Abre Discord de escritorio y ETS2. La aplicación encuentra el juego en Steam y prepara la conexión automáticamente.

Puedes minimizar la aplicación y recuperarla desde el icono junto al reloj. Si ETS2 ya estaba abierto durante la primera preparación, reinícialo para que empiece a enviar los datos. Acepta el aviso de SDK si ETS2 lo muestra. Si Windows protege la carpeta del juego, la app te indica cómo continuar.

**Aviso:** esta aplicación se distribuye por GitHub. Los mods de ETS2 en Steam Workshop no pueden instalar y ejecutar por sí solos el componente que lee el juego. No es un mod de suscripción automática.

## Qué muestra

- En una entrega: carga, origen, destino y kilómetros restantes.
- Sin entrega: exploración libre.
- Ciudad cercana: una **estimación**, calculada con el catálogo incluido de ciudades del mapa base y de expansiones. En zonas sin cobertura la aplicación omite la ciudad.
- En pausa: indica que el juego está en pausa.
- El panel muestra la llegada estimada según el reloj de ETS2 y el progreso cuando el juego facilita una ruta y una distancia planificada. Un cambio de destino en el navegador del juego puede cambiar la estimación.

Desde **Ajustes** puedes abrir los ajustes para ordenar los datos de Discord y previsualizarlos, guardar un perfil de publicación, elegir el idioma del panel y de la actividad por separado y cambiar el tema. También puedes ordenar los paneles, elegir cuáles mostrar y activar la vista compacta. **Más opciones → Segundo monitor** mueve el panel a otra pantalla si hay una disponible. Usa Escape para volver a la ventana. Puedes ajustar el tamaño del texto por separado. Cambia el tema del panel desde los botones al pie de la barra lateral. Las ventanas nativas aplican la selección al volver a abrir la app.

El mapa muestra posiciones aproximadas, el destino y el recorrido observado durante la sesión. Arrastra o usa las flechas para moverlo. Usa la rueda o los botones para acercar y alejar, y pulsa «Centrar» para volver a la vista inicial. Puedes importar un catálogo de mapas modificados en **Ajustes → Juego**. El JSON debe contener una lista `citiesList` con los campos `realName`, `x` y `z`. El campo opcional `gameName` permite localizar destinos por su identificador de ETS2. Añade `country` a cada ciudad para que el catálogo personalizado pueda seleccionar las banderas. La línea hacia el destino indica la dirección aproximada.

**Diario** reúne las entregas y cancelaciones detectadas. Elige o crea un conductor para guardar sus viajes por separado. Busca por ciudad, carga, etiqueta o nota, marca favoritos y exporta el resultado a CSV. Usa Ctrl para seleccionar dos filas y compararlas. El calendario resume kilómetros, entregas y tiempo al volante por semana. Las horas al volante se calculan a partir de las muestras observadas.

**Repeticiones** permite grabar y borrar viajes locales o abrir un archivo JSONL anterior. Usa la línea de tiempo o el gráfico de velocidad para avanzar o retroceder. Puedes cambiar la velocidad y saltar a los eventos de recogida, ferry o entrega que se hayan detectado. Durante la reproducción se pausa la publicación en Discord.

En **Ajustes → Juego** puedes comprobar la versión y compatibilidad del lector por separado, repararlo o retirarlo con ETS2 cerrado, y activar avisos de combustible bajo. Los datos del camión aparecen cuando el juego los facilita.

En **Ajustes → Datos locales** puedes crear o restaurar una copia de los diarios, ajustes y catálogos. Puedes incluir las grabaciones cerradas en una copia manual y elegir cuánto tiempo conservarlas. El modo portátil usa la carpeta `ETS2PresenceData` junto al ejecutable a partir del siguiente inicio. Al cambiar de modo puedes copiar los ajustes, diarios y catálogos antes de volver a abrir la app.

La pausa se guarda y también se controla desde la bandeja de Windows. «Ver viaje rápido» muestra el destino, la distancia y el estado de Discord. En **Ajustes → Privacidad** puedes elegir un atajo que oculta temporalmente la ruta y la ciudad en Discord. En **Más opciones → Tarjeta OBS** se abre una ventana independiente con la ruta y distancia. Puedes capturar esa ventana en OBS y aplicar un filtro de clave de color magenta si el método de captura muestra el borde transparente como un color sólido. El inicio minimizado con Windows es opcional. Si mueves el ejecutable, desactiva y vuelve a activar esa opción desde su nueva ubicación.

Al abrir la app se comprueba si hay una versión más reciente en GitHub. Las versiones de prueba aparecen con el canal Preview. Cuando hay una actualización, aparece en **Más opciones**. Si la versión trae una suma de verificación, puedes descargarla e instalarla desde la app. La versión anterior queda disponible en el mismo menú si el ejecutable está en una carpeta donde puedes escribir.

La actividad aparece como **Euro Truck Simulator 2** en Discord. La actividad usa una sola portada, con la bandera del país cuando está disponible y se permite compartirla. El temporizador cuenta desde que se detecta la actividad. Ambos se pueden desactivar desde **Ajustes → Discord**. El título oficial detectado automáticamente por Discord puede coexistir con esta actividad según la configuración del cliente.

## Última versión · v0.8.1

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

## Problemas frecuentes

- **No aparece ETS2:** pulsa «Buscar ETS2» y selecciona la carpeta `Euro Truck Simulator 2` dentro de tu biblioteca de Steam.
- **No aparece la actividad:** abre Discord de escritorio y revisa que «Compartir mi actividad» esté activado.
- **La aplicación espera datos:** entra a una partida. Si acabas de abrir la app por primera vez con ETS2 ya iniciado, reinicia el juego y acepta su aviso de SDK si aparece.
- **Ciudad incorrecta:** la ubicación es aproximada y la lista no cubre todos los DLC ni mapas externos.
- **Lector incompatible:** sigue la indicación del panel y abre **Ajustes → Juego**. Desde ahí puedes reparar el lector propio o copiar un diagnóstico.

## Desarrollo

Requiere .NET 10 SDK en Windows.

El ID público de la aplicación registrada en Discord está en `DiscordIdentity.cs` y viene incluido en la descarga.

```powershell
dotnet test tests/Ets2DiscordPresence.Tests/Ets2DiscordPresence.Tests.csproj -c Release
./scripts/build-release.ps1
```

El script verifica la versión, el tag de los recursos de Discord y el encabezado del README. Ejecuta la suite antes de generar el ejecutable y su archivo SHA-256 en `dist/standalone`.

## Alcance y créditos

La aplicación lee los datos en el equipo y se comunica con Discord de escritorio. El ejecutable incluye el lector de la [versión V.1.12.1 de RenCloud](https://github.com/RenCloud/scs-sdk-plugin/releases/tag/V.1.12.1) y un catálogo de 414 puntos urbanos. Conserva nombres e identificadores de [ETS2-City-Coordinate-Retriever](https://github.com/Koenvh1/ETS2-City-Coordinate-Retriever) y añade posiciones y países del [mapa de Trucker Mudgeon](https://github.com/truckermudgeon/truckermudgeon.github.io/blob/559c5fa5a84189e48f637e84d79832841966a073/ets2-search.geojson). La posición del país se estima a partir de las ciudades, por lo que se omite en lugares sin cobertura o con señales ambiguas cerca de fronteras. Los catálogos personalizados pueden añadir el campo `country` con códigos como `ES` o `PT`. Sus avisos de licencia están disponibles desde el menú del icono junto al reloj.

Probado mediante compilación y pruebas automáticas; la lectura en una partida real de ETS2 y la visualización en una cuenta de Discord requieren validación en una máquina con el juego y Discord abiertos.

### Prueba con ETS2 y Discord antes de publicar

1. En Windows sin el SDK de .NET, abre el ejecutable junto con Discord de escritorio. Localiza ETS2 también desde una biblioteca de Steam secundaria.
2. Con una entrega activa, confirma desde una segunda cuenta el título, el texto, la portada y el cambio de país al cruzar una frontera.
3. Inicia una ruta de pasajeros y revisa el destino, la portada y el registro de la sesión. Comprueba que una ruta manual y una automática no se dupliquen.
4. Abre y cierra mapa y modo foto en español e inglés. Repite con juego en ventana, pantalla completa y superposición. Verifica que al volver a conducir se recupere el viaje.
5. Reinicia Discord, pausa la publicación y cierra ETS2. La actividad debe volver al reconectar y retirarse cuando dejes de compartir.
6. Actualiza desde una copia de la versión anterior y revisa que ajustes y diarios sigan intactos. Prueba la vuelta a la versión anterior.
7. Mueve la interfaz entre pantallas con escalas distintas y recorre Ajustes, Diario y Repeticiones con el teclado. Registra el resultado y los casos pendientes.
