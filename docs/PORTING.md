# KKS移植メモ

元のKK版 `upstream/KK.FreeHStudioSceneLoader.cs`（689行）を、KKSの `Assembly-CSharp.dll` と照合した結果です（2026-10-04）。
照合に使ったのは型名・メンバー名・シグネチャなどのメタデータだけで、メソッドの中身は読んでいません。ツールは `../tools/MetaList` にあります。

## 前提の確認
- Studio系のクラス（`Studio.Studio`, `Info`, `MapCtrl`, `Map`, `GuideObjectManager`, `TreeNodeCtrl`, `CameraLightCtrl`, `PatternSelectListCtrl` など）は、**KKSのメインゲーム側のDLLにも存在します**。KK版と同じく、メインゲームの中でスタジオ機能を起動するという方針は成り立ちます。
- 元のコードのパッチ対象は約55か所あります。そのうち約48か所は、型名・メンバー名・注入しているフィールド名がKKSでも一致しています。
- 元のコードをKKSの参照でそのままコンパイルすると、エラーは1件だけです（`Config.Start`）。ただし、下の「実行時に失敗するもの」はコンパイルでは検出できません。

## KKSで仕組みが違う箇所（作業リスト）

| # | KK版のパッチ | KKSの状況 | 対応方針 |
|---|---|---|---|
| 1 | `Manager.Config.Start`（Postfix）で Studio を初期化 | `Start` がない。`Manager.Config` は static クラスで、`Initialize()` / `InitializeBeforeSceneLoad()` がある | 初期化のタイミングを変更する。候補はプラグインの `Start`、またはタイトル・Free-H画面に入ったとき |
| 2 | `ActionMap.Change(ref int no)`（Prefix、元の処理を中止） | `BaseMap.Change(int, FadeCanvas.Fade, bool)` と `BaseMap.ChangeAsync(int, …)`（UniTask）になった。文字列を引数に取る版もあり、名前だけではどちらか決まらない | 引数の型まで指定して `BaseMap.ChangeAsync(int, Fade, bool)` にパッチする。`no` は `BaseMap.no`（setter は private）|
| 3 | `MapSelectMenuScene.Start`：`___nodeFrame` と `____mapInfo` にボタンを追加 | **画面の作りが別物**。`nodeFrame` がなく、`_thumbnailButton`（MapThumbnailButton）を使う方式。`_mapInfo` は自動実装プロパティ（`<_mapInfo>k__BackingField`）になった。`Start` は UniTask を返す | マップ選択画面には手を入れず、**Free-H画面に独自の「スタジオシーン選択」ボタンとリスト（IMGUI）を追加する**方式にする |
| 4 | `HSceneProc.Start`：`dataH.mapNoFreeH = -1` | `HSceneProc.dataH` の型は `ActionGame.H.OpenHData.Data`（`mapNoFreeH` はある）。`Start` は IEnumerator | おおむね流用できる |
| 5 | `Studio.LoadScene` / `InitScene` / `Init` / `Awake` などを、**KK版の処理を書き写したもの**で置き換え | KKSの処理内容と一致する保証がない（null確認を足したKK版の複製） | KKSでの動作に合わせて作り直す必要がある。最大の作業 |
| 6 | `GuideObject.LateUpdate`、`TreeNodeCtrl.*`、`TreeNodeObject.*` なども、処理を書き写したものに置き換え | 同上 | 同上 |
| 7 | `FreeHScene.SetMapSprite` | KKSにもある（引数 `MapInfo.Param`）。Free-H画面は通常・オナニー・レズ・3P の4つ | 3P用の `mapImage3P` を追加して流用する |

## 前作者がサポートしない理由（参考）
- [KK_Plugins#74](https://github.com/IllusionMods/KK_Plugins/issues/74)（2021-03）：本人の説明は「未完成で、かろうじて動く程度。作業に飽きた」。
- [KK_Plugins#312](https://github.com/IllusionMods/KK_Plugins/issues/312)（2025-04）：「もうKoikatsuのモッディングには関わっていない。元は、他の人がゲームのコードを直接書き換えて作ったものを、パッチ方式に移植する実験だった。パッチで正しく動かすには、直接書き換える場合と比べて作業量が非常に多く、やる気が出ない。限定的には動くので、意欲のあるモッダーが引き継ぐのは歓迎する」。
- KK_Plugins 側でも「obsolete/broken plugin, do not create releases」として、リリースの対象から外されています（2025-12）。

## v0.1 の設計（2026-10-04）
KKSのゲーム側の処理（`[MODDING] Tools` の dnSpy.Console で逆コンパイル。結果はリポジトリには含めない）を確認したうえで、元のKK版から設計を次のように変えました。

- **マップはゲームのFree-H用マップを使う**：KKSのHシーンは `BaseMap.Change(番号)` でマップを読み込み、Hポイントを `h/common/HPoint_{番号}` から読み込む。シーンの土台マップに対応するFree-H用マップを `FreeHScene.member.resultMapInfo` に入れ、ゲーム本来の流れでマップとHポイントを読み込ませる。スタジオ側でマップは読み込まない（元のKK版の `ActionMap.Change` の置き換えや、`Map` / `MapCtrl` のパッチは不要になった）。
  - 対応づけは、シーンファイル（バンドル名とシーン名）の完全一致で行う。一致しなければ、`_studio` / `_s` を除いた名前で照合する（KKSの島マップはスタジオ専用のシーンを持つため）。ゲーム標準のスタジオ用マップでは、73個中、完全一致26個、名前照合で追加29個。
- **スタジオの起動は最小限、かつ必要になってから**：`Studio.Studio`、`Info`、`GuideObjectManager`、`TreeNodeCtrl` を、シーン選択ウィンドウを初めて開いたときに作る。`Studio.Awake` はシングルトンの設定だけを行い、スタジオの設定ファイル（option.xml / workspace）は読み書きしない。
- **シーンの読み込み**：Sideloader は `SceneInfo.Load` の途中（ExtendedSave のイベント）で、`Studio.Instance.sceneInfo` を参照してMODアイテムやマップのIDを変換する。そのため、読み込む前に `Studio.Instance.sceneInfo` に読み込み先を設定する。
- **配置するもの**：アイテム（kind 1）、ライト（kind 2、設定で切り替え）、フォルダ（kind 3）。キャラクター、ルート、カメラ、テキストは配置しない。スタジオでマップを移動していた場合（`caMap`）は、その逆の変換をアイテム側にかける。
- **ゲームの処理を書き写して置き換えるパッチは最小限に**：元のパッチのうち、ハンドル（GuideObject）とツリー（TreeNode）の画面まわりを無効化するものだけを残した。KKS版の処理と比較し、`GuideObject.Awake` / `LateUpdate`、`GuideObjectManager.DeleteAll`、`TreeNodeCtrl.AddNode` は KKS 版の処理に合わせて書いている。`Studio.LoadScene` / `InitScene` / `Init` は呼ばないので、置き換えもしない。
- **マップ選択画面には手を入れない**：Free-H画面で F7 キーを押すと、独自のシーン選択ウィンドウ（IMGUI）が開く。

## 未対応（今後）
- 土台マップを持たないシーン（アイテムだけで背景を作ったもの）
- キャラクターの読み込み
- MaterialEditor などで変更したアイテムのマテリアル（KKAPI のスタジオ用の読み込み処理が、メインゲームでは動かないため）

## 動作確認の記録
- 2026-10-05 v0.1.8：KKS形式のシーン（体育館ライブのマップ）で、フォルダ2個とアイテム3個の計5個を、エラーなく配置できた。
  ログ：`Studio scene objects placed: 5`

### 途中で見つかった問題と対処
| 版 | 問題 | 対処 |
|---|---|---|
| 0.1.1 | `TreeNodeCtrl.SelectMultiple` が KKS では同名のメソッドが2つあり、パッチ全体が中断した | このパッチをやめた。パッチを1件ずつ適用する方式に変えた |
| 0.1.2 | 実行時に、非公開フィールドへのアクセスで `FieldAccessException` | `AllowUnsafeBlocks`（コンパイラが SkipVerification / UnverifiableCode を出力する）を有効にした |
| 0.1.3 | `SceneInfo` のコンストラクタが `MapCtrl.Instance.Reflect` を参照し、`Init` が `cameraCtrl` を参照していた | `MapCtrl`（Awake / Reflect / UpdateUI を無効化）と、無効状態のオブジェクト上の `CameraControl` を用意した |
| 0.1.4 | シーンフォルダに、ただの画像（シーンデータのないPNG）が混在していた | PNGの後ろにあるバージョン文字列で判定し、一覧から外した |
| 0.1.5 | IMGUIのレイアウトエラー（描画中に一覧が変化した） | ファイルの確認処理を Update に移した |
| 0.1.6 | KKS_AssetImport が `SceneInfo.Load(string)` に処理を差し込んでおり、スタジオ外では例外を出す | `Load(string, out Version)` を直接呼ぶようにした（ExtendedSave / Sideloader はこちらに差し込んでいる） |
| 0.1.7 | KKSUS の OptimizeNEO が `TreeNodeObject.treeState` などに後処理を差し込んでおり、例外を出す | Finalizer で例外を受け止める（スタジオのホストが動いている間だけ） |
| 0.1.8 | `SetVisibleChild` などのパッチを移植し忘れていた。スタジオの音声アイテムが読み込み時に鳴った | パッチを追加した。`SEComponent.OnEnable` を止めた（設定で再生できるようにした） |

### 未確認・既知の制限
- 無印コイカツ（KK）形式のシーン（バージョン 1.0.x）は、キャラクターデータを読んだ後に失敗するものがある。原因は未調査（スタジオで開けるかどうかの確認待ち）
- 目に見えるアイテム（家具など）の位置・色・マテリアルの再現は、まだ確認していない
