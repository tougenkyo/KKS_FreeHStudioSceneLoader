# KKS_FreeHStudioSceneLoader (experimental)

Koikatsu Sunshine port of **FreeHStudioSceneLoader**: use CharaStudio scenes as maps for Free H in the main game.

> **Status: experimental.** Expect issues, and keep backups of your scenes and configs.
> This is an unofficial port. Please do not ask the original author or the KK_Plugins maintainers for support.

[日本語の説明は下にあります。](#日本語)

## Origin and license
- Based on `FreeHStudioSceneLoader.KK` from [IllusionMods/KK_Plugins](https://github.com/IllusionMods/KK_Plugins) by DeathWeasel1337.
  The original source is kept unmodified in [`upstream/`](upstream/) for reference.
- Modified for Koikatsu Sunshine (2026, see git history and [docs/PORTING.md](docs/PORTING.md)).
- Licensed under the **GNU General Public License v3.0**, same as the original. See [LICENSE](LICENSE).
- Parts ported from other plugins are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Requirements
- Koikatsu Sunshine with BepInEx 5
- Sideloader and ExtensibleSaveFormat (part of BepisPlugins)
- Optional: MoreOutfits (KK_Plugins) for naming the extra outfit slots

## Installation
Copy `KKS_FreeHStudioSceneLoader.dll` into `BepInEx/plugins/` (a subfolder such as `BepInEx/plugins/KKS_FreeHStudioSceneLoader/` is fine).

## Usage
1. Open Free H. Click the "Choose a studio scene" button or press **F7** (both configurable).
2. Wait for the studio lists to load (first time only). Scenes from `UserData/studio/scene` are listed as thumbnails or file names. Another folder can be typed in or picked from the subfolders above the list (remembered in the config).
3. Click a scene. This sets it for H, lists any mods it needs that are missing, and shows its settings. "Unset", or choosing a map in the game's own map selection, goes back to the normal map.
4. Start H as usual. The scene's items, folders, lights and characters are placed on the map.

The scene set last is set again when Free H opens, also after the game is restarted (until it is unset; see Scenes / Set the last scene again).

### Filters and the scene data list
The list can be narrowed down to scenes with or without Timeline animation, with females only / males only / both, and with or without mod errors (missing mods, Sideloader errors while resolving the scene's mods, or files that can't be read).
To know this the whole scene has to be read, which takes as long as choosing it, so the results are kept in a data list (`BepInEx/config/KKS_FreeHStudioSceneLoader.scenelist.txt`):

- "Make data list" reads the scenes that are not in the list yet or changed since (a few per frame; it goes on while the window is closed and can be stopped). Chosen scenes are added too.
- "Remake all" reads every scene again, e.g. after installing mods.
- While a filter is on, scenes not in the data list are left out (their number is shown). Scenes in the list show what they contain on the thumbnail (F = females, M = males, Anim, Missing mods, Mod error).

The windows are in English by default. The 日本語 / English button at the top right of each window (or General / Language in the config) switches the language.

### Scene settings (selection window)
Saved per scene file in `BepInEx/config/KKS_FreeHStudioSceneLoader.scenes.xml`.
- **Base map**: when several Free H maps use the same studio map (e.g. mod maps with different H positions), choose which one.
- **Time of day**: optionally use the scene's time of day for the Free H map.
- **Characters**: placed posed as in Studio (decoration only, they do not take part in H). Each one, or all females / all males, can be switched off.
  - "Replace" swaps a character for another card (pose and clothes state stay as in the scene); "Original" brings the original back.
    The card list can be switched to the other sex: a male can be replaced with a female card (and the other way round). Such a character changes sex the next time H starts, and gets a "Penis" switch (off for a female, on for a male).
  - "Clothes ▼" chooses the outfit (including MoreOutfits slots), loads a coordinate card, and sets all clothes on / half / off.
  - Cards are listed from `UserData/chara/female`, `UserData/chara/male` and `UserData/coordinate` by default. Another folder can be typed in or picked from the subfolders in the card window (remembered in the config).
- **Sounds**: the scene's sound/voice items and the characters' voice lists.

### During H
F7 or the on-screen button opens a window for the scene:
- Show or hide each character, change its outfit and clothes state, and replace it.
- Move or turn a character alone, together with the items NodesConstraints link to it, or the whole scene.
- Save the layout (positions, rotations, visibility) in 3 slots; optionally re-apply the last saved slot whenever the scene is loaded.
- Switch the sounds.

The game's point move (場所移動, the pin icon) also offers the places where the scene's characters stand, under every kind of H point the map has.

### What is reproduced
- Items, folders (including hidden ones), lights, item scales, colors and patterns, the background image.
- The scene's shadow, bloom, ambient occlusion and ambient light settings, and Studio's lighting for scenes without a map.
- Studio-only plugins whose data is kept in the scene: Timeline, KKPE (bone and blend shape edits), NodesConstraints, and "FK and IK". Timeline camera, color, clothes and some other tracks are not.
- Character silhouettes (Studio's 「シルエット」).

### Scenes without a studio map
Scenes whose background is built from items: the map chosen in Free H provides the H positions and is hidden, and the scene is placed so that its female character stands at the H start position. Choose the Free H map before picking the scene (choosing a map in the normal map selection afterwards unsets the scene).

### Known limitations
- Scenes whose studio map has no Free H version are not supported.
- Studio cameras, routes and text items are not placed.

## Configuration
Settings are in `BepInEx/config/local.kks.freehstudiosceneloader.cfg` (or the ConfigurationManager, F1).

| Section | Setting | Default | Description |
|---|---|---|---|
| General | Language | English | English or Japanese |
| General | H points at studio characters | on | Point move also offers the studio characters' places |
| General | Load studio female characters / male characters | on | Default for scenes not set up yet |
| General | Load studio lights | on | |
| General | Play studio sound items | on | Default for scenes not set up yet |
| General | Play character voice lists | off | Default for scenes not set up yet |
| General | Match studio time of day | off | Default for scenes not set up yet |
| General | Scene selection window | F7 | Key that opens the windows |
| General | Hide base map for scenes without a map | on | |
| General | Studio lighting for scenes without a map | on | |
| General | Use the shadows and bloom of the scene | on | Instead of the game's graphic settings while a scene is placed |
| General | Background color for scenes without a map | black | |
| General | Show studio background image | on | |
| Studio plugins | Play Timeline animation / Apply KKPE pose edits / Apply FK and IK | on | |
| Button | Show button / Button position / Show button in H / Button in H position | | On-screen buttons |
| Button | Window background opacity | 0.85 | |
| Button | Show scene thumbnails | on | Also switched in the selection window |
| Cards | Female card folder / Male card folder / Coordinate card folder | UserData/... | Folders of the card lists |
| Cards | Include subfolders | on | |
| Scenes | Scene folder | UserData/studio/scene | Folder of the scene list |
| Scenes | Include subfolders | on | |
| Scenes | Set the last scene again | on | Set the last scene when Free H opens after a restart |
| Scenes | Filter: Timeline animation / characters / missing mods | All | The filters of the scene list (also set in the window) |
| Diagnostics | Log character poses / Log scene lighting | off | For comparing with Studio |

## Building
Requires the .NET SDK. Game and BepInEx reference assemblies come from the IllusionMods NuGet feed (see `nuget.config`); no game files are included in this repository.

```
dotnet build -c Release
```

Output: `bin/Release/KKS_FreeHStudioSceneLoader.dll`.

---

## 日本語

コイカツ！サンシャイン用の非公式移植版です。CharaStudio のシーンを、フリーHのマップとして使えます（元は KK_Plugins の FreeHStudioSceneLoader。GPL-3.0）。**試験的なものです。** 元の作者や KK_Plugins にはこの版について問い合わせないでください。

### 必要なもの
- BepInEx 5、Sideloader、ExtensibleSaveFormat（BepisPlugins）
- 任意：MoreOutfits（KK_Plugins）。追加コーデの名前を表示します

### 導入
`KKS_FreeHStudioSceneLoader.dll` を `BepInEx/plugins/` に入れます。

### 使い方
1. フリーHの画面で「Choose a studio scene」（日本語表示では「スタジオシーンを選ぶ」）ボタンを押すか、F7 キーを押します。
2. `UserData/studio/scene` のシーンが一覧になります（サムネイルとファイル名を切り替えられます）。一覧の上で別のフォルダやサブフォルダを指定できます。
3. シーンをクリックするとHにセットされ、足りないMODの確認と、シーンの設定が表示されます。「解除」するか、ゲームの通常のマップ選択でマップを選ぶと、元のマップに戻ります。
4. いつもどおりHを始めると、シーンのアイテム・フォルダ・ライト・キャラクターがマップに置かれます。

最後にセットしたシーンは、ゲームを起動し直したあとも、フリーHを開くと自動でセットされます（解除するまで）。

#### 絞り込みとデータリスト
一覧を「Timeline アニメのあり・なし」「女性のみ・男性のみ・両方」「MODエラー（足りないMOD、Sideloader がシーンのMODの解決中に出したエラー、読めないファイル）のあり・なし」で絞り込めます。
これを調べるにはシーンを丸ごと読む必要があり、選んだときと同じだけ時間がかかるので、結果をデータリスト（`BepInEx/config/KKS_FreeHStudioSceneLoader.scenelist.txt`）に保存します。

- 「データリスト作成」：まだリストにないシーンと、変更されたシーンを読みます（1フレームに少しずつ。窓を閉じても続き、中止できます）。選んだシーンもリストに入ります。
- 「全部作り直す」：すべてのシーンを読み直します（MODを入れたあとなど）。
- 絞り込み中は、データリストにないシーンは表示しません（件数を表示します）。リストにあるシーンは、サムネイルに中身を表示します（女・男の人数、アニメ、MOD不足の種類数、MODエラー）。

表示は英語が既定です。窓の右上の「日本語」ボタン（または設定の General / Language）で日本語に切り替えられます。

### 主な機能
- 土台のマップ、時間帯、キャラクター（女性・男性・1人ずつの表示）、音をシーンごとに設定。
- キャラクターを別のカードに差し替え（男性を女性のカードに、女性を男性のカードにもできます。性別は次にHを始めたときに変わり、「ちんちん」の表示を切り替えられます）。服の種類（MoreOutfits の追加コーデを含む）、コーデカードの読み込み、全着・半脱・全脱。カードを探すフォルダは指定できます。
- H中の窓：キャラクターの表示・服・差し替え、移動と回転（1人、NodesConstraints でつながったアイテムごと、シーン全体）、配置の保存（3スロット。最後に保存したスロットを自動で再現できます）。
- ゲームの「場所移動」（ピンのアイコン）に、シーンのキャラクターが立っている場所を追加します。
- シーンの影・ブルーム・AO・環境光、マップのないシーンの Studio と同じ照明、シルエット表示。
- Timeline、KKPE、NodesConstraints、FK and IK のデータを再現（Timeline のカメラ・色・服などのトラックは対象外）。

### 制限
- フリーH用のマップがないスタジオマップのシーンには、まだ対応していません。
- スタジオのカメラ、ルート、テキストは置きません。
