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
2. Wait for the studio lists to load (first time only), pick a scene from `UserData/studio/scene`, click it (this sets it for H; 「解除」 goes back to the normal map).
3. The Free H map is switched to the scene's base map; start H as usual. Items, folders and lights from the scene are placed on the map.

In the selection window, after choosing a scene:
- **Characters**: the scene's characters are placed posed as in Studio (decoration only, they do not take part in H). Females, males and single characters can be switched off.
- **Base map**: when several Free H maps load the same studio map (e.g. mod maps with different H positions), the one named like the studio map is used first; another can be chosen.
- **Time of day**: optionally set the Free H time of day to the scene's (the map's lighting).
- **Sounds**: the scene's sound/voice items (on by default) and the characters' voice lists.

These choices are saved per scene file in `BepInEx/config/KKS_FreeHStudioSceneLoader.scenes.xml`.

During H, F7 or the on-screen button opens a window to show/hide the scene's characters, move or turn them (alone, together with the folders/items NodesConstraints link them to, or the whole scene), and switch the sounds.

Studio-only plugins whose data is kept in the scene are reproduced: Timeline, KKPE (bone and blend shape edits), NodesConstraints, and "FK and IK" (see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)). Timeline camera, color, clothes and some other tracks are not.

Scenes without a studio map (backgrounds built from items): the map currently chosen in Free H provides the H positions and is hidden; the scene is placed so that its female character stands at the H start position. Choose the Free H map before picking the scene, or change it afterwards in the normal map selection.

Scenes whose map has no Free H version are not supported yet.

## Requirements (runtime)
- Koikatsu Sunshine with BepInEx 5, KKAPI, Sideloader
