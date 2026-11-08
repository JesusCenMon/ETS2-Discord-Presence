# ETS2 Discord Presence

Muestra en Discord qué estás haciendo en **Euro Truck Simulator 2**: entrega, ciudades de origen y destino, distancia restante y, cuando se puede estimar, una ciudad cercana. Si no tienes una entrega, muestra «Explorando libremente».

## Instalar en Windows

1. Descarga `ETS2-Discord-Presence-win-x64.zip` desde [Releases](https://github.com/JesusCenMon/ETS2-Discord-Presence/releases). Descomprímelo y abre `ETS2DiscordPresence.exe`.
2. La aplicación busca ETS2 en Steam. Pulsa **Instalar componente**. Si el juego está abierto, ciérralo primero.
3. Abre Discord de escritorio y ETS2. La aplicación intentará conectarse sola. Puedes minimizar la ventana: queda junto al reloj.

Después de instalar el componente, la aplicación recuerda dónde está ETS2 y se conecta automáticamente cuando vuelves a abrirla. Si ETS2 está cerrado, la actividad se borra.

**Aviso:** esta aplicación se distribuye por GitHub. Los mods de ETS2 en Steam Workshop no pueden instalar y ejecutar por sí solos el componente que lee el juego. No es un mod de suscripción automática.

## Qué muestra

- En una entrega: carga, origen, destino y kilómetros restantes.
- Sin entrega: exploración libre.
- Ciudad cercana: una **estimación**, calculada con una lista de ciudades del mapa base. Puede faltar en expansiones y mapas modificados; entonces la aplicación omite la ciudad.
- En pausa: indica que el juego está en pausa.

La actividad aparece como **ETS2 Presence** en Discord. El título oficial detectado automáticamente por Discord puede coexistir con esta actividad según la configuración del cliente.

## Problemas frecuentes

- **No aparece ETS2:** pulsa «Buscar...» y selecciona la carpeta `Euro Truck Simulator 2` dentro de tu biblioteca de Steam.
- **No aparece la actividad:** abre Discord de escritorio y revisa que «Compartir mi actividad» esté activado.
- **La aplicación espera datos:** reinicia ETS2 después de instalar el componente. Confirma que aceptaste el aviso de SDK avanzado del juego.
- **Ciudad incorrecta:** la ubicación es aproximada y la lista no cubre todos los DLC ni mapas externos.

## Desarrollo

Requiere .NET 10 SDK en Windows.

El ID público de la aplicación registrada en Discord está en `DiscordIdentity.cs` y viene incluido en la descarga.

```powershell
dotnet test tests/Ets2DiscordPresence.Tests/Ets2DiscordPresence.Tests.csproj -c Release
./scripts/build-release.ps1
```

El archivo listo para compartir queda en `dist/ETS2-Discord-Presence-win-x64.zip`.

## Alcance y créditos

La aplicación lee datos locales del juego y se comunica con el cliente local de Discord. No envía telemetría a un servidor propio. La descarga del componente de ETS2 proviene de la [versión V.1.12.1 de RenCloud](https://github.com/RenCloud/scs-sdk-plugin/releases/tag/V.1.12.1), bajo licencia MIT; la aplicación comprueba su huella antes de instalarlo. Los datos de ciudades proceden de [ETS2-City-Coordinate-Retriever](https://github.com/Koenvh1/ETS2-City-Coordinate-Retriever), también bajo licencia MIT; su aviso de licencia está en `data/CITY-DATA-LICENSE.txt`.

Probado mediante compilación y pruebas automáticas; la lectura en una partida real de ETS2 y la visualización en una cuenta de Discord requieren validación en una máquina con el juego y Discord abiertos.
