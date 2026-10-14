# GourdHoops

A mod for A Township Tale that lets you hang gourd canteens on the potion hoops on your bag, not
just glass potion bottles. A bottle holds one portion and a gourd holds two, so a full set of hoops
carries twice as much.

## Install

It goes on the server and in each player's game.

1. Server: put `GourdHoops.dll` in the server's `Mods` folder and restart it.
2. Players: put `GourdHoops.dll` in your game's `Mods` folder.

Needs MelonLoader, which TheModdingTavern launcher installs.

Players without the mod still see gourds on other people's hoops and can take a gourd off their
own bag. They just can't hang one. If the server doesn't have it, a gourd you hang pops back off.

To remove it, delete `GourdHoops.dll`. Gourds already on a hoop stay there until someone takes
them off.

## Settings

The first start writes `UserData\GourdHoops.txt`:

```
items = 31744
docks = Potion Hoops
offset = 0 0 0
log = on
```

- `items`: the item hashes the hoops should take. 31744 is the Gourd Canteen. Add more with commas.
- `docks`: which dock rules get them. `Potion Hoops` is the hoop attachment for bags.
- `offset`: nudges where the item sits on the hoop, in metres (x y z). It only changes how it
  looks in your own game.
- `log`: write a line to the log when the hoops are changed.

Change a value and save. The mod picks it up in a couple of seconds, no restart. An item already
on a hoop moves to its new spot the next time it's hung.

The server and the players should use the same `items` and `docks`.

## Good to know

- `items` wants the item hash, not the prefab hash. The gourd's prefab is 29032, its item is 31744.
- The change is made to the Potion Hoops rule itself, so every hoop that uses it takes gourds.
- Nothing in the game is patched. The mod adds the gourd to the list of things the hoops accept,
  the same list the bottle is on.

## Build

`build.ps1` compiles `GourdHoops.cs` against your game install (set `$game` at the top) with the
.NET Framework's `csc`, and writes `build\GourdHoops.dll`.

## Licence

MIT. See `LICENSE`.
