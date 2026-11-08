# ETS2 Discord Presence

Muestra en Discord qué estás haciendo en **Euro Truck Simulator 2**: entrega, ciudades de origen y destino, distancia restante y, cuando se puede estimar, una ciudad cercana. Si no tienes una entrega, muestra «Explorando libremente».

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

En **Ajustes** puedes ordenar los datos de Discord y previsualizarlos, guardar un perfil de publicación, elegir el idioma del panel y de la actividad por separado y cambiar el tema. También puedes ordenar los paneles, elegir cuáles mostrar y activar la vista compacta. **Segundo monitor** mueve el panel a otra pantalla si hay una disponible. Usa Escape para volver a la ventana.

El mapa muestra posiciones aproximadas, el destino y el recorrido observado durante la sesión. Puedes importar un catálogo de mapas modificados en **Ajustes → Juego**. El JSON debe contener una lista `citiesList` con los campos `realName`, `x` y `z`. El campo opcional `gameName` permite localizar destinos por su identificador de ETS2.

**Historial** reúne las entregas y cancelaciones detectadas. Guarda los datos en este equipo, permite filtrar por ciudad o fechas y exporta el resultado a CSV. Las horas al volante se calculan a partir de las muestras observadas y las etapas de recogida, llegada y descanso pueden ser aproximadas.

En **Ajustes → Juego** puedes grabar telemetría local y reproducir un archivo JSONL. La reproducción se muestra en el panel sin publicar actividad en Discord. El mismo menú permite reparar o retirar el lector con ETS2 cerrado.

La pausa se guarda y también se controla desde la bandeja de Windows. El inicio minimizado con Windows es opcional. Si mueves el ejecutable, desactiva y vuelve a activar esa opción desde su nueva ubicación.

Al abrir la app se comprueba si hay una versión más reciente en GitHub. El botón de actualización abre la descarga del ejecutable.

La actividad aparece como **ETS2 Presence** en Discord. El título oficial detectado automáticamente por Discord puede coexistir con esta actividad según la configuración del cliente.

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

La aplicación lee los datos en el equipo y se comunica con Discord de escritorio. El ejecutable incluye el lector de la [versión V.1.12.1 de RenCloud](https://github.com/RenCloud/scs-sdk-plugin/releases/tag/V.1.12.1) y 240 ciudades de [ETS2-City-Coordinate-Retriever](https://github.com/Koenvh1/ETS2-City-Coordinate-Retriever), con datos del mapa base y de varias expansiones europeas. Sus avisos de licencia están disponibles desde el menú del icono junto al reloj.

Probado mediante compilación y pruebas automáticas; la lectura en una partida real de ETS2 y la visualización en una cuenta de Discord requieren validación en una máquina con el juego y Discord abiertos.
