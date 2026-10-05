# KKS_FreeHStudioSceneLoader (experimental)

Koikatsu Sunshine port of **FreeHStudioSceneLoader**, which lets you use Studio scenes as H scene maps in the main game.

> **Status: experimental / work in progress.** Expect issues.
> This is an unofficial port. Please do not ask the original author or the KK_Plugins maintainers for support.

## Origin and license
- Based on `FreeHStudioSceneLoader.KK` from [IllusionMods/KK_Plugins](https://github.com/IllusionMods/KK_Plugins) by DeathWeasel1337.
  The original source is kept unmodified in [`upstream/`](upstream/) for reference.
- Modified for Koikatsu Sunshine (2026, see git history and [docs/PORTING.md](docs/PORTING.md)).
- Licensed under the **GNU General Public License v3.0**, same as the original. See [LICENSE](LICENSE).

## Building
Requires the .NET SDK. Game and BepInEx reference assemblies come from the IllusionMods NuGet feed (see `nuget.config`); no game files are included in this repository.

```
dotnet build -c Release
```

Output: `bin/Release/KKS_FreeHStudioSceneLoader.dll` → copy to `BepInEx/plugins/`.

## Usage
1. Open Free H, click the 「スタジオシーンを選ぶ」 button or press **F7** on the selection screen (both configurable).
2. Wait for the studio lists to load (first time only), pick a scene from `UserData/studio/scene`, click 「このシーンを使う」.
3. The Free H map is switched to the scene's base map; start H as usual. Items, folders and lights from the scene are placed on the map. Characters are not loaded.

Scenes without a studio map (backgrounds built from items): the map currently chosen in Free H provides the H positions and is hidden; the scene is placed so that its female character stands at the H start position. Choose the Free H map before picking the scene, or change it afterwards in the normal map selection.

Scenes whose map has no Free H version are not supported yet.

## Requirements (runtime)
- Koikatsu Sunshine with BepInEx 5, KKAPI, Sideloader
