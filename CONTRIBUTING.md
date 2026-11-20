# Contributing

English and Spanish contributions are welcome. Start with a focused change or a reproducible report.

## Report a bug or suggest an improvement

Use the [issue forms](https://github.com/JesusCenMon/ETS2-Discord-Presence/issues/new/choose). Include the app version, Windows version, ETS2 version, steps to reproduce and the expected result. For visual issues, include the window size, display scaling, theme and a screenshot if possible.

For questions or ideas that need discussion, use [Discussions](https://github.com/JesusCenMon/ETS2-Discord-Presence/discussions). Remove personal paths and private information from diagnostic files and recordings.

Report security-sensitive issues through the [security policy](SECURITY.md).

## Develop on Windows

Install the .NET 10 SDK, then run the following from the repository root.

```powershell
dotnet restore tests/Ets2DiscordPresence.Tests/Ets2DiscordPresence.Tests.csproj
dotnet test tests/Ets2DiscordPresence.Tests/Ets2DiscordPresence.Tests.csproj -c Release
dotnet run --project src/Ets2DiscordPresence/Ets2DiscordPresence.csproj
```

The app can prepare its telemetry readers when it finds ETS2. Close the game before reader maintenance. Use the existing local dashboard demonstration for UI checks.

Keep changes focused. Reuse existing telemetry, journal, replay, Discord and update code. Keep English and Spanish user-facing text together using the existing localization pattern. Include a regression test when a behavioral fix needs one.

## Submit a pull request

Explain the problem and the resulting behavior. Include the checks you ran, screenshots for visible UI changes, and any known limitations. Match the current code style and preserve unrelated behavior.

For UI changes, check normal and narrow windows, keyboard focus, English and Spanish, light/dark themes, large text and reduced motion. Use the PR template to record the checks relevant to your change.

## Package a release

```powershell
./scripts/build-release.ps1 -Tag v0.8.6
```

The tag, project version, artwork identity and README release heading must agree. Packaging runs the automated suite and checks the self-contained executable.

Before publishing, check the bundled reader setup, delivery and free driving presence, privacy controls, country artwork, update behavior and experimental map/photo recognition in a real ETS2 session where possible. Record any remaining gaps in the release notes. The player download is `ETS2DiscordPresence.exe`.

## Contribuir en español

Puedes abrir incidencias y pull requests en español. Describe qué ocurre, cómo repetirlo y qué esperabas. Incluye versiones y capturas cuando ayuden.

Para desarrollar necesitas Windows y .NET 10 SDK. Ejecuta los comandos de arriba desde la raíz del repositorio. Mantén los textos de interfaz en español e inglés, y explica cómo verificaste el cambio. Las mejoras visuales deben comprobarse también con teclado, ventana estrecha y texto ampliado.

## Community

Follow the [Code of Conduct](CODE_OF_CONDUCT.md). Contributions are shared under the repository's MIT license. Keep third-party license notices with the assets or code you contribute.
