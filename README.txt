# White Knuckle Together

Online multiplayer for White Knuckle, for up to 4 players, over Steam lobbies. Multiplayer is in the play menu.

## Features

- **Co-op:** climb the same world together. Enemies, items, props and the world are synced.
- **Race:** race up the same world. An orb follows the leader and hurts anyone left outside it. The last player alive, or the first to finish, wins.
- **Voice and text chat**, with optional proximity voice.
- **Lobby modifiers:** Iron Knuckle, Hard Mode, instanced worlds, shared roaches, item stealing, grab assists and more.
- **Multiplayer specific mechanics:** steal items from players hands, help other climbers with grab assists, and respawn everyone using disks.
- **Nobody left behind:** safety features to prevent other players from getting trapped behind closed doors.
- **Public lobbies**, or friends-only / invite-only.

## Progression is safe

Multiplayer runs never unlock anything and never count toward achievements or progression. Unlocked content in a lobby is
limited to whichever player has progressed the least, so nobody gets spoiled.

Singleplayer is entirely unaffected.

## Notes

- This mod is WIP and there will be bugs.
- This was originally created for personal use with friends, but it is in a stable enough state that I think others could have fun with it.
- Only supports campaign for now.
- Every player in a lobby needs the same version of the mod.
- Public lobbies and balancing are not my focus. It is intended to be played with friends and to help each other (or race).

## AI disclosure

I will try to keep it concise. As an introduction, I am a Unity developer and programmer with many years of experience. I know what I am doing.

AI was used quite extensively in this mod to integrate hooks, analyze assets, and fix niche issues.

AI is an extremely polarizing topic, yes, but the reality is that reverse engineering every aspect of White Knuckle to synchronize it 
over the network while handling every edge case by hand would take at least hundreds if not thousands of hours as Dark Machine continues 
to update the game. It is simply is not feasible for me to do in my spare time.

AI is extremely good at decompiling, analyzing, and labeling packaged content and assemblies. This means it can figure out what triggers activate
what events, how interactions resolve in a massive codebase, etc. and can inject custom code into those locations. I do not want to offend Dark Machine
by creating something using these tools, however, I think it is the only viable way for me to implement multiplayer in a reasonable amount of time.

## SPOILERS

- The shortcut codes that you find in a multiplayer world can be entered in a singleplayer world. This is probably undesirable, so I may remove this in the future.
- Not all shortcuts have been tested.
- The Nest is quite untested as of now, but in theory the groundwork has been laid for it.