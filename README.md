# White Knuckle Together

Online multiplayer for [White Knuckle](https://store.steampowered.com/app/3195790/White_Knuckle/), for up to 4 players, over Steam lobbies.

> [!WARNING]
> This mod is a work in progress and there will be bugs. Every player in a lobby needs the same version of the mod.

## Features

- **Co-op:** climb the same world together. Enemies, items, props and the world are synced.
- **Race:** race up the same world. An orb follows the leader and hurts anyone left outside it. The last player alive, or the first to finish, wins.
- **Voice and text chat**, with optional proximity voice.
- **Lobby modifiers:** Iron Knuckle, Hard Mode, instanced worlds, shared roaches, item stealing, grab assists and more.
- **Multiplayer-specific mechanics:** steal items from players' hands, help other climbers with grab assists, and respawn everyone using disks.
- **Nobody left behind:** safety features that stop players from getting trapped behind closed doors.
- **Public lobbies**, or friends-only / invite-only.

## Progression is safe

Multiplayer runs never unlock anything and never count toward achievements or progression. Unlocked content in a lobby is limited to whichever player has progressed the least, so nobody gets spoiled.

Singleplayer is entirely unaffected.

## Installation

### Manual

1. Install [BepInEx 5](https://thunderstore.io/c/white-knuckle/p/BepInEx/BepInExPack/) (BepInExPack 5.4.2305) into your White Knuckle folder and launch the game once.
2. Download the latest zip from [Releases](https://github.com/Nephren-ctrl/WKT/releases).
3. Extract it into `White Knuckle/BepInEx/plugins/`. Inside the plugins folder, there should be a folder called `WhiteKnuckleTogether` containing `WhiteKnuckleTogether.dll`, `opus.dll` and `wkt.bundle`.
4. Launch the game. Multiplayer is in the play menu.

## Notes

- This was originally made for playing with friends, but it's stable enough that I think others can have fun with it.
- Only the campaign is supported for now.
- Public lobbies and balancing are not my focus, but some effort did go into that. The mod is meant for playing with friends and helping each other (or racing).

## Building from source

1. Copy the game and BepInEx assemblies listed in [`libs/README.txt`](libs/README.txt) into `libs/`.
2. Build in Release:
   ```
   dotnet build WhiteKnuckleTogether/WhiteKnuckleTogether.csproj -c Release
   ```
3. Copy the output from `WhiteKnuckleTogether/bin/Release/netstandard2.1/` into your `BepInEx/plugins/WhiteKnuckleTogether/` folder.

## Known issues

<details>
<summary>⚠️ Contains spoilers</summary>

- Shortcut codes found in a multiplayer world can be entered in a singleplayer world. This is probably undesirable, so I may remove it in the future.
- Not all shortcuts have been tested.
- The Nest is mostly untested so far, but in theory the groundwork has been laid for it.

</details>

## License

Released under the [MIT License](LICENSE). White Knuckle and its assets belong to Dark Machine; this repository contains none of the game's files.
