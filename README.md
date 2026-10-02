# UniversalGrasp

UniversalGrasp lets you hold any item from your Valheim inventory visually in either hand.

- Hold the configured modifier (default: **Left Alt**) and left-click an item to put it in the left hand.
- Hold the configured modifier and right-click an item to put it in the right hand.
- Repeat the same action to remove that visual item from the hand.

The interaction only applies to the player's own inventory. Ordinary clicks and container inventories keep their normal behavior.

The modifier is a local client setting and can be changed in the BepInEx configuration or Configuration Manager. It is not synchronized with a server.

## Requirements

- Valheim
- BepInExPack for Valheim

## Installation

### Mod manager

Install UniversalGrasp and its declared dependencies through Gale, Thunderstore Mod Manager, or r2modman. The same Thunderstore package works with all three managers.

### Manual

Copy `UniversalGrasp.dll` into `BepInEx/plugins`.

## Building

1. Copy `Config.Build.user.props.example` to `Config.Build.user.props`.
2. Set `VALHEIM_INSTALL` to the local Valheim installation.
3. Ensure `publicized_assemblies/assembly_valheim_publicized.dll` exists below Valheim's managed assembly directory.
4. Open `UniversalGrasp.sln` in Visual Studio and build.

A Release build creates a distributable archive below `artifacts`.

Valheim, BepInEx, Unity assemblies, and publicized game assemblies are local build prerequisites and are not part of this repository.

## License

Copyright © 2021-2026 bid. Licensed under GPL-3.0-or-later. See `LICENSE`.
