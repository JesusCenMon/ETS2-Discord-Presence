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
- Ciudad cercana: una **estimación**, calculada con una lista de ciudades del mapa base. Puede faltar en expansiones y mapas modificados; entonces la aplicación omite la ciudad.
- En pausa: indica que el juego está en pausa.
- El panel muestra la llegada estimada según el reloj de ETS2 y el progreso cuando el juego facilita una ruta y una distancia planificada. Un cambio de destino en el navegador del juego puede cambiar la estimación.

En **Ajustes** puedes ordenar los datos de Discord y previsualizarlos, guardar un perfil de publicación, elegir el idioma del panel y de la actividad por separado y cambiar el tema. También puedes ordenar los paneles, elegir cuáles mostrar y activar la vista compacta. **Más opciones → Segundo monitor** mueve el panel a otra pantalla si hay una disponible. Usa Escape para volver a la ventana. Puedes ajustar el tamaño del texto por separado. Vuelve a abrir la app después de cambiar el tema.

El mapa muestra posiciones aproximadas, el destino y el recorrido observado durante la sesión. Arrastra o usa las flechas para moverlo. Usa la rueda o los botones para acercar y alejar, y pulsa «Centrar» para volver a la vista inicial. Puedes importar un catálogo de mapas modificados en **Ajustes → Juego**. El JSON debe contener una lista `citiesList` con los campos `realName`, `x` y `z`. El campo opcional `gameName` permite localizar destinos por su identificador de ETS2.

**Diario** reúne las entregas y cancelaciones detectadas. Elige o crea un conductor para guardar sus viajes por separado. Busca por ciudad, carga, etiqueta o nota, marca favoritos y exporta el resultado a CSV. Usa Ctrl para seleccionar dos filas y compararlas. El calendario resume kilómetros, entregas y tiempo al volante por semana. Las horas al volante se calculan a partir de las muestras observadas.

**Repeticiones** permite grabar y borrar viajes locales o abrir un archivo JSONL anterior. Usa la línea de tiempo o el gráfico de velocidad para avanzar o retroceder. Puedes cambiar la velocidad y saltar a los eventos de recogida, ferry o entrega que se hayan detectado. Durante la reproducción se pausa la publicación en Discord.

En **Ajustes → Juego** puedes comprobar la versión y compatibilidad del lector por separado, repararlo o retirarlo con ETS2 cerrado, y activar avisos de combustible bajo. Los datos del camión aparecen cuando el juego los facilita.

En **Ajustes → Datos locales** puedes crear o restaurar una copia de los diarios, ajustes y catálogos. Las grabaciones se gestionan por separado y puedes elegir cuánto tiempo conservarlas. El modo portátil usa la carpeta `ETS2PresenceData` junto al ejecutable a partir del siguiente inicio. Para llevar el diario a la otra carpeta, exporta una copia y restáurala después de volver a abrir la app.

La pausa se guarda y también se controla desde la bandeja de Windows. «Ver viaje rápido» muestra el destino, la distancia y el estado de Discord. En **Ajustes → Privacidad** puedes elegir un atajo que oculta temporalmente la ruta y la ciudad en Discord. En **Más opciones → Tarjeta OBS** se abre una ventana independiente con la ruta y distancia. Puedes capturar esa ventana en OBS y aplicar un filtro de clave de color magenta si el método de captura muestra el borde transparente como un color sólido. El inicio minimizado con Windows es opcional. Si mueves el ejecutable, desactiva y vuelve a activar esa opción desde su nueva ubicación.

Al abrir la app se comprueba si hay una versión más reciente en GitHub. Cuando hay una actualización, aparece en **Más opciones**. Si la versión trae una suma de verificación, puedes descargarla e instalarla desde la app. La versión anterior queda disponible en el mismo menú si el ejecutable está en una carpeta donde puedes escribir.

La actividad aparece como **Euro Truck Simulator 2** en Discord. La imagen cambia su indicador según estés haciendo una entrega, explorando o en pausa, y el temporizador cuenta desde que se detecta la actividad. Ambos se pueden desactivar desde **Ajustes → Discord**. El título oficial detectado automáticamente por Discord puede coexistir con esta actividad según la configuración del cliente.

## Novedades de la v0.6.0

- **Panel.** Cambia entre Completo, Conducción y Transmisión desde Más opciones. También hay una ventana flotante, búsqueda en Ajustes, botones fijables, formato de hora, millas y avisos configurables.
- **Mapa.** El menú del mapa incluye rumbo, seguimiento, búsqueda de ciudades, capas y recorrido completo. La escala usa unidades del mapa del juego. El aviso de ruta indica cambios grandes en la distancia o llegada estimada.
- **Discord.** Puedes personalizar el texto y el temporizador, ocultar toda la actividad temporalmente, aplicar reglas de ubicación y usar un atajo de pausa total. Los países usan el arte general hasta que se añadan imágenes en `data/country_art.json`.
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

El script ejecuta las pruebas de reproducción y del lector antes de crear `dist/standalone/ETS2DiscordPresence.exe`.

## Alcance y créditos

La aplicación lee los datos en el equipo y se comunica con Discord de escritorio. El ejecutable incluye el lector de la [versión V.1.12.1 de RenCloud](https://github.com/RenCloud/scs-sdk-plugin/releases/tag/V.1.12.1) y un catálogo de 400 puntos urbanos. Conserva nombres e identificadores de [ETS2-City-Coordinate-Retriever](https://github.com/Koenvh1/ETS2-City-Coordinate-Retriever) y añade posiciones y países del [mapa de Trucker Mudgeon](https://github.com/truckermudgeon/truckermudgeon.github.io/blob/559c5fa5a84189e48f637e84d79832841966a073/ets2-search.geojson). La posición del país se estima a partir de las ciudades, por lo que se omite en lugares sin cobertura o con señales ambiguas cerca de fronteras. Los catálogos personalizados pueden añadir el campo `country` con códigos como `ES` o `PT`. Sus avisos de licencia están disponibles desde el menú del icono junto al reloj.

Probado mediante compilación y pruebas automáticas; la lectura en una partida real de ETS2 y la visualización en una cuenta de Discord requieren validación en una máquina con el juego y Discord abiertos.
