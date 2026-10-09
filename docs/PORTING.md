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
- **マップ選択画面には手を入れない**：Free-H画面のボタン（IMGUI、位置は設定で変更可）か F7 キーで、独自のシーン選択ウィンドウ（IMGUI）が開く。

## v0.2 土台マップを持たないシーン（2026-10-05）
アイテムだけで背景を作ったシーン（`SceneInfo.map == -1`）。元のKK版も対応していない。
- **H位置はFree-H画面で選んでいるマップから借りる**：シーンを選んだ時点の `resultMapInfo` をそのまま使う。選んだ後に通常のマップ選択で別のマップを選ぶと、シーンは解除せず、H位置に使うマップだけを切り替える。
- **シーンの置き場所**：シーン内の最初の女性キャラ（いなければ最初のキャラ、キャラがいなければ原点）の位置と向き（Y軸回転のみ）を、Hの開始位置 `HSceneProc.initPos` / `initRot` に合わせる。キャラの位置は、親のフォルダ・アイテムの `changeAmount` をたどって求める。`initPos` は `CreateCharacter` の前に確定するので、`lstFemale` に要素が入るのを待ってから読む。
- **土台マップは表示しない**（設定で切り替え）：`mapRoot` 以下の Renderer を無効にする。コライダーとHポイントはそのまま。H終了時に元に戻す。
- 制限：Hポイントを移動すると、キャラだけが借りたマップの別の位置に移動し、背景はその場に残る。

## 未対応（今後）
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
| 0.1.9 | Hシーンから選択画面に戻ると `SceneInfo.Init` でNRE。`DeleteAllNode` がホストの子（CameraControl の入れ物）まで消していた | ツリーノード専用の入れ物 `TreeNodes` を作り、その子だけを消すようにした |
| 0.1.10 | KK形式のシーンで、キャラクターの後のデータを読むところで `EndOfStreamException`。メインゲームの `ChaFile.LoadFile(BinaryReader)` は、カードの目印が `【KoiKatuCharaSun】` でないと途中で読むのをやめる。スタジオ版は目印を確認しない（2つの Assembly-CSharp を比べた違いはこの1か所と、衣装の数の扱いだけ） | Transpiler で目印の比較を差し替えた。シーンを読んでいる間だけ、KKの目印（`ChaFileDefine.KoikatsuCharaFileMarkCheck`）も通す |
| 0.2.1 | フォルダの中のキャラクターまで読み込もうとして `AddObjectFemale.Add` でNRE。フォルダごと失敗し、中のアイテムも置かれなかった | 配置する前に、フォルダ・アイテムの子から、配置しない種類（キャラ・ルート・カメラ・テキスト）を再帰的に取り除く |
| 0.2.2 | （機能追加）スタジオの背景画像（`SceneInfo.background`、StudioImageEmbed の `BGData`）がHで出ない | Hのカメラに ScreenSpaceCamera のキャンバスを作り、一番奥に画像を表示する（スタジオの `BackgroundCtrl` と同じく画面いっぱい） |
| 0.2.3 | マップの無いシーンで、隠したマップの空（スカイボックス）が見える。スタジオではマップが無いと黒 | 土台マップを隠すときに、カメラを単色（既定は黒、設定で変更可）で塗る。H終了時に戻す |
| 0.2.4 | 0.2.3 でもHで空が見えた（スイートルームを土台にした場合）。Hのカメラ1台だけを塗っていた | `RenderSettings.skybox` を外し、画面を消去するカメラ（Skybox / SolidColor、RenderTexture 以外）をすべて単色にする。H終了時に戻す。カメラの一覧をデバッグログに出す |
| 0.2.5 | アイテムの拡大縮小が反映されず、すべて等倍で置かれていた（シーン A の緑の壁 0.1×50×100 が小さな箱になる）。スタジオでは GuideObject.LateUpdate が毎フレーム大きさを設定するが、ここで作るガイドは非アクティブで LateUpdate が動かない（元のKK版も同じ） | 配置後に LateUpdate と同じ式で、親から順に1回だけ大きさを適用する。enablePos/Rot/Scale のセッターはフラグだけ保存する |
| 0.2.5 | 柄（パターン）付きアイテムで `OCIItem.SetPatternTex` が NRE（`Studio.patternSelectListCtrl` が無い）。アイテムの読み込みが途中で止まる | 非アクティブなオブジェクトに PatternSelectListCtrl を置き、StudioScene.CreatePatternList と同じ項目（mt_pattern と UserData/pattern_thumb）を UI 無しで登録する |
| 0.2.6 | マップ付きシーンのアイテムが部屋から離れた場所に置かれた（シーン B のビーチチェアがスイートルームに出ない）。スタジオの `MapCtrl.Reflect` はマップのルート位置を caMap で上書きする（スイートルームの本来のルート位置は (-50, 0, -115)）。Hではルートが本来の位置のままなのに、原点にある前提で変換していた | caMap の逆変換に、Hのマップのルートのワールド位置・回転を掛ける。ゲーム版とスタジオ版のスイートルームは、共通の部品486個がすべて同じ位置にあることをアセットで確認した |
| 0.2.7 | シーンのキャラクターを読み込んでいなかった。ゲームの体の骨格（chara/oo_base.unity3d の p_cf_body_bone）には、スタジオのポーズに必要な Preparation / CharAnimeCtrl / IKCtrl / PVCopy が無い | スタジオ版 ChaControl と同じく、シーンのキャラを作る間だけ体と頭の骨格を studio/base/00.unity3d から読む（LoadNoAsync のトランスパイラ）。キャラはHのキャラが作られた後に読む。後から有効になるIK/FKのガイドで Awake が calcScale を戻し骨の大きさを壊すため、Awake は何もしない。H設定の男の表示切り替えはシーンのキャラには適用しない |
| 0.2.8 | キャラクターを選んで読み込めない。Hでキャラクターの位置を直せない | 選択窓でシーンのキャラクターを一覧し、女性・男性・個別にオンオフ。H中は同じキーでキャラクター窓を開き、表示の切り替え、カメラ基準の移動・回転、カメラ注視点への移動、元に戻す（全員まとめても可）。移動は changeAmount を書き換える |
| 0.3.0 | Timeline / KKPE / NodesConstraints はスタジオ専用プラグインのため、Hではシーンが止まったまま（例: シーン C は速度0+IKで、腰などの動きはTimelineとKKPEの骨とNodesConstraintsの連結で作られている）。ユーザーのシーン768件中、Timeline 197件、KKPE 555件、連結166件 | HSPlugins（MIT）のソースに合わせて SceneAnimation.cs で再生する。オブジェクト番号は Studio.dicObjectCtrl を dicKey 順に並べた位置（ここで置かないカメラ等も数え、読み込めなかったアイテムは数えない）。Update で Timeline→ガイド付き連結、PostLateUpdate の先頭（全 LateUpdate の後、スキンメッシュ更新の前）に PlayerLoop の処理を足して KKPE の骨とブレンドシェイプ→Timeline after→全連結（最初は onPreCull で行い、スキンメッシュ更新後だったため顔が頭から外れて見えた）。カメラ・色・服などのトラック、KKPE の揺れ物/IK 等は未対応 |
| 0.3.2 | 髪が硬く動き体を貫通する（スタジオでは柔らかい）。KKPE と連結を全 LateUpdate の後に当てていたため、髪の DynamicBone が連結前の頭の姿勢で計算されていた。アンセンサー/BP の骨（cf_J_Vagina_root、cm_J_dan_top）はキャラ作成直後には無く、その連結2件が読めなかった | ゲームとスタジオの MonoScript 実行順（IK 9999 → 首 10100 → FK 10120 → 目線 10800 → DynamicBone_Ver02 12200 → DynamicBone 12250）を確認し、最初の DynamicBone の LateUpdate の前に KKPE→Timeline after→連結を当てる（PostLateUpdate の処理は保険）。見つからない骨の連結は15秒間 0.5秒ごとに再試行 |
| 0.3.3 | 手首がねじれる（スタジオでは正常）。KKPE は IKExecutionOrder（9996、KKPE が空のものを追加）の後、つまり IK（9999）の前に骨を当て、IK は編集後の背骨から腕を解く。Timeline after と NodesConstraints は Expression（実行順0）の最後の LateUpdate の後。こちらは IK の後に当てていたため、肩・背骨の編集で腕が動いた | 遅い段階を Timeline after → 全連結 → KKPE の順にし、IK（SolverManager）・首・FK・揺れ物のうち、そのフレームで最初に来た LateUpdate の前に実行する |
| 0.3.4 | 0.3.3 でも手首のねじれが残る（髪・顔は改善を確認） | KKPE は全キャラに CharaPoseController を付け、optimizeIK（既定オン）で FullBodyBipedIK の spineStiffness と pullBodyVertical を 0 にする。これを再現（効果は未確認） |
| 0.3.5 | KKPE をオフにすると手首が正常（その時も KKPE の Timeline トラックは動いていた）。原因は KKPE の固定データの男性 spine01/spine03 回転。女性の手は男性 spine03 に位置・回転で連結されており、編集前の背骨から計算していたため約17°回っていた | 遅い段階を KKPE → Timeline after → 全連結の順にし、連結が KKPE 編集後の骨に従うようにする（IK の前のまま）。KKPE をオフにすると KKPE の Timeline トラックもオフ |
| 0.3.6 | 0.3.5 でも KKPE オンで手首がねじれる。Timeline/NodesConstraints が使う Expression.LateUpdate は、キャラでは ExpressionBone（実行順 12150）で、IK・FK・目線の後。0.3.3〜0.3.5 は連結を IK の前に当てており、男性の IK 後の背骨と合っていなかった | 2段階に分ける。KKPE は最初の IK/首/FK の LateUpdate の前（スタジオの 9996 相当）。Timeline after と連結は最初の DynamicBone の LateUpdate の前（12150 と 12200 の間）。PostLateUpdate は保険 |
| 0.3.7 | 0.3.6 でも KKPE オンで手首がねじれる（髪・顔は正常）。KKPE オフでは正常 | 原因を一度の試行で切り分けられるよう、H のキャラ窓に診断欄を追加。連結の全体オンオフ、キャラごとの KKPE 固定ポーズ／Timeline／optimizeIK、骨ごとのオンオフを、その場で切り替えられる（オフにした骨は KKPE 適用前の値に戻す） |
| 0.3.8 | 診断で全部オフにしても手首のねじれが残る（連結をオフにすると IK ターゲットがその場で止まるため、その場の切り替えでは戻らない）。KKPE オフで正常だったのは連結を IK の前に当てていた 0.3.4〜0.3.5 | 診断欄に「連結と Timeline の後処理を IK の前に当てる」切り替えを追加（未確定のため既定は IK の後のまま） |
| 0.3.9 | 設定で KKPE をオフにして読み込むと手首は正常だが、診断でその場で全部オフにしてもねじれたまま（読み込み時に起きる何かが原因）。KKPE の itemInfo は順番で割り当てられるため、アイテムが欠けると次のアイテムに渡る | 読み込み時に KKPE の各部（固定の骨、Timeline の骨、ブレンドシェイプ、optimizeIK、アイテム）を個別に切れる診断用の設定を追加。itemInfo は index（dicKey）で割り当てる |
| 0.3.10 | 読み込み時に「固定の骨」だけ切ると手首は正常だが脚の開き方が変わる。固定のみの骨は女性の胸（cf_d_bust01）と男性の cf_j_spine01/03 回転だけ（他は Timeline と重複） | キャラ名・骨のパスで固定の骨を読み込み時に除外する診断用設定を追加し、読み込んだ固定の骨をログに出す |
| 0.3.11〜0.3.12 | 原因は Stephanie の固定の cf_j_spine03 回転だけ（胸・肩・眉では再現せず、spine03 だけ除くと正常）。同じ骨は Timeline でも毎フレーム上書きされるはずなので、オブジェクト番号のずれで Timeline の KKPE トラックが別キャラに当たっている疑い | 除外の文字の最後に $ で骨のパスの末尾一致。オブジェクト番号と中身（dicKey、種類、名前）をログに出す |
| 0.3.13〜0.3.16 | 数値で比較するため、スタジオ専用の計測プラグイン（_dev/tools/StudioPoseProbe、配布しない）と H 側で同じ値を BepInEx/FreeHStudioProbe.log に出す。スタジオでは手首の曲がりも約65°で H と同程度。違いは前腕（cf_j_forearm01）で、スタジオは肘の1軸だけ（0, 80, 0）、H は余分なひねり。スタジオではスタジオ専用プラグイン FK and IK（KK_Plugins FKIK、GPL-3.0）が、FK と IK を両方オンで保存したキャラを両方オンに戻す | FK and IK の読み込み時の処理を再現（設定 Apply FK and IK）。計測に FK/IK の状態、腕のひねり用の骨、体の補正コンポーネントを追加 |
| 0.3.17 | 前腕のひねりの原因を絞り込んだ。H では cf_j_hand の localPosition がずれている（前腕から手までが 0.222→0.250、手首の補助骨（前腕の子）と手の方向が約13°ずれる。スタジオは既定どおり）。FinalIK は前腕の向きを（肩・肘・手）の位置が作る面から決めるため、手の位置がずれると前腕がひねられる。手の骨の位置を書き換えるのは、スタジオの体の骨格にある PVCopy（cf_pv_hand/leg を世界座標で cf_j_hand/leg03 に写す。実行順0で IK より前）だけ。ゲームとスタジオの FinalIK（firstpass）は同一 | 計測に、IK の直前（IKSolverFullBody.OnUpdate の前）の手の localPosition・cf_pv_hand の位置・手との距離・背骨、アニメーターの状態、PVCopy のスイッチを追加（スタジオ側の計測プラグインも同じ）。キャラの読み込み時の PVCopy のスイッチと手の localPosition をログに出す。H のキャラ窓の診断欄に「PVCopy を切り、手・足首の骨を元の位置に戻す」を追加（プラグインのアニメーションが無いシーンでも診断欄と計測が出るようにした） |
| 0.3.18 | 0.3.17 の診断で PVCopy を切ると前腕が直った。計測では、スタジオでも H でも PVCopy は読み込み直後だけオン（1111）で、すぐオフ（0000）になる。オフにするのは ABMX（KKSABMX、LGPL-3.0。公開ソースで確認）で、基準値を取る処理で PVCopy を切り、戻さない。スタジオでは、オンの間の写しがアニメの姿勢と一致し、手の localPosition は既定値（-0.25, 0, 0）のまま。H では、その間にもう KKPE の肩・背骨の編集が当たっているため写した手が前腕から外れ（(-0.274, -0.049, 0.040)）、PVCopy が切れた後もそのまま残る。IK はその手に向けて前腕をひねる | シーンのキャラの PVCopy のスイッチが毎フレーム、IK の前にオンからオフに変わったら、その手・足首の骨を読み込み時の localPosition に戻す（StudioHost.CheckPvCopy。ログに出す）。0.3.17 の診断用の切り替えは削除 |
| 0.3.19 | 0.3.18 で手首は直ったが、腕や脚が内側に閉じる。計測では、肘・膝・足首（と男性の手）の IK ターゲットがキャラの原点 (0, 0, 0) にある（Timeline や連結で動くターゲットは正常）。0.3.18 の変更とは無関係で、シーンを2回選んでから H に入ると起きる（ログに同じシーンの選択が2回）。シーンを読むと ObjectInfo.Load が各オブジェクトの ChangeAmount を dicKey で Studio.dicChangeAmount に登録するが、Studio.AddChangeAmount は同じキーがあると例外を握りつぶして古い方を残す。ガイドオブジェクトは古い ChangeAmount に結び付き、新しいシーン情報の保存値（OnChange）が当たらない | シーンのデータを捨てるとき（読み直しの前）に、何も置いていなければ Studio.dicChangeAmount も消す |
| 0.3.20 | （機能追加）F7 の窓でキャラを動かすと手足だけ動き、体が残る。シーン D（シーン C と同系統）は NodesConstraints で両キャラの腰・骨盤・肩の IK ターゲットをフォルダに、女性の両手を男性の spine03 に連結しており、キャラだけを動かすとスタジオでも同じになる。連結先のフォルダ（6, 9, 16, 19, 22 など）は Timeline の guideObjectPos で毎フレーム親の空間の値を上書きされる | 窓に「動かす対象」を追加（試し）。「キャラと連結先」: 選んだキャラ（全員）を子とする連結の親のうちフォルダ・アイテムを、キャラと一緒に動かす。それらの値（Timeline が書く値）はそのままにして、親との間に入れた移動用の Transform を動かす。他の連結先やキャラの中にあるものは二重に動かさない。「シーン全体」: スタジオから置いたもの全部の親（FreeHStudioSceneLoader_Objects）を動かす。どちらも「元の位置に戻す」に対応 |
| 0.3.21 | （機能追加）「キャラと連結先」が望みどおりの動きだった。H 中に F7 と同じ操作をするボタンが欲しい。シーンの声を、読み込み時と読み込み後にそれぞれオンオフしたい。オンオフはシーンごとに保存したい（これまでは音・声アイテムが設定ファイル全体で1つ、キャラ個別のオンオフは保存なし） | 移動の既定を「キャラと連結先」にする（他の2つも残す）。H 中、シーンのキャラがいるときにキャラの窓を開閉するボタンを表示（設定 Button / Show button in H、Button in H position）。シーンごとの設定（音・声アイテム、キャラのボイスリストの再生、キャラ個別のオンオフ）を BepInEx/config/KKS_FreeHStudioSceneLoader.scenes.xml に、UserData/studio/scene からのパスで保存（SceneSettings.cs）。未設定のシーンは設定ファイルの既定値（Play studio sound items / Play character voice lists）。選択窓と H のキャラ窓で切り替えられ、H 中は音・声アイテムを止める／鳴らす（SEComponent を有効にし直す）、ボイスリストを先頭から再生／停止する。女性・男性の一括スイッチは従来どおり全体の設定 |
| 0.3.22 | （機能追加）女性・男性の一括スイッチもシーンごとに保存したい。シーンの音・声アイテムは既定で鳴らしたい | 一括スイッチを SceneSettings（females / males）に加え、選択窓の切り替えで保存。未設定のシーンと、古い版で保存した項目のないシーンは設定ファイルの値を使う。Play studio sound items の既定値を true に（既存の設定ファイルは値を書き換えた） |
| 0.3.23 | シーン D（スタジオのマップはラブホテル）で、土台がピンクルームになる。AnotherH の MOD の「ラブホテル」（No 960）と「ピンクルーム」（No 999）は、どちらも studio/map/lovehotel.unity3d の LoveHotel を読み、違いは H 位置（h/common）と時間帯ごとのサムネイルだけ。プラグインは同じシーンのマップのうち最初に見つかった 999 を選んでいた（2回の H とも 999 の hpoint_999 を読んでいることをログで確認）。明るさの違いは、マップ番号ではなく時間帯（SunLightInfo、フリーH の朝・昼・夕・夜）による可能性が高い | 同じスタジオマップを使うフリーH のマップを全部候補にし、シーンで前に選んだもの → スタジオのマップ名と同じ名前のもの → 番号順に並べる。選択窓に候補を切り替えるボタンを出し、選んだマップをシーンごとに保存（SceneSettings.FreeHMap）。H 開始時に、マップ番号・フリーH の時間帯・スタジオのシーンの時間帯（SceneInfo.sunLightType）をログに出す |
| 0.4.0 | （整理・機能追加）診断用の機能が残っていた。0.3.23 のログで、明るさの違いはフリーH の時間帯（1回目は夜、2回目は朝。スタジオのシーンは朝）と確認 | 診断を整理: 設定「Studio plugins (diagnostics)」の KKPE の部分ごとのオンオフ・除外する骨・トレースと、H のキャラ窓の診断欄（連結のオンオフ、IK の前に当てる切り替え、KKPE の骨ごとのオンオフ）を削除。計測（PoseProbe、スタジオ側の計測ツールと共通）は設定「Diagnostics / Log character poses」（既定オフ）で残す。オブジェクト一覧と KKPE の固定の骨のログはデバッグレベルに。整理前のコードは f258b49。時間帯をスタジオのシーン（SceneInfo.sunLightType）に合わせるオプションを追加（選択窓、シーンごとに保存、既定オフ。設定 Match studio time of day）。選んだ時点でフリーH の時間帯（全モードのトグル）を設定し、後からフリーH 画面で変えられる。秘密のビーチ（9）は対象外。README の使い方を更新 |
| 0.4.0 | （続き）選択窓は、シーンの情報が多いと縦に長くなる。選択窓などの上をクリックすると、後ろの本編の UI も反応する（IMGUI の窓は uGUI のクリックを止めない） | 選択窓を2列（左: シーン一覧・プレビュー・使う/解除、右: 選んだシーンの設定。右は高さを固定してスクロール）にし、幅 1040（画面に収まるよう縮める）。窓は画面外に出ないようにする。描いた窓とボタンの範囲に、最前面のオーバーレイ Canvas（sortingOrder 32767）の透明な Image を置き、EventSystem のクリックを受け止める（GuiBlocker.cs。H 中のキャラ窓・ボタンも同じ） |
| 0.4.1 | （改善）窓の背景が薄く文字が読みにくい。シーンの詳細は、一覧でクリックした時点で見たい（「このシーンを使う」でセット） | 窓の背景を濃い半透明の単色に（設定 Window background opacity、既定 0.85）。選択を「詳細の表示」（Selection.Preview: シーンを読み、土台マップの候補を求めて右の欄に表示。設定もここで変えられ保存される）と「セット」（Selection.Use: フリーHのマップを切り替え、サムネイル・時間帯を当てる）に分けた。読み込めるシーンは1つだけなので、別のシーンをクリックするとセット済みのシーンは解除される（その旨を表示）。「解除」は詳細を残してセットだけ外す |
| 0.4.2 | （機能追加）シーンに足りない MOD を選択窓で調べ、クリップボードにコピーしたい（例: シーン E は MOD 20 種類のうち 19 種類が無く、丸いオブジェクトしか出ない） | Sideloader の UniversalAutoResolver.ShowGUIDError（足りない MOD ごとに呼ばれ、「Missing mod detected!」を出す）に後処理を付け、シーンを読み込んでいる間（StudioHost.IsLoadingScene）だけ GUID・名前・作者・配布先と回数を記録する。アイテム・キャラのカード・マップ・フィルターなど、読み込み時に分かるものが対象（シェーダーなど H で置いたときに分かるものは含まない）。選択窓の右の欄に「足りないMODを調べる」（一覧を表示）と「クリップボードにコピー」（英語の一覧）を追加。Sideloader は別のプラグインなので名前で探してパッチし、見つからなくても他の機能は動く |
| 0.4.3 | H からフリーH 画面に戻ると、スタジオシーンの選択が解除されている（HSceneProc.OnDestroy と FreeHScene.Start の後処理で Selection.Clear）。戻ってから窓を開くと「このシーンを使う」がグレー（窓のプレビューは前のシーンのまま、読み込んだデータは捨てられていた） | H の終了時にセットしていたシーンを覚え（Selection.ReturnScenePath）、フリーH 画面の Start（コルーチン）が H のマップを選び終え、スタジオの一覧が使えるようになってから 0.5 秒後に、読み込み直してセットする。フリーH 画面の開始時と窓を開いたときに、読み込んでいないシーンのプレビューを消す |
| 0.4.4 | キャラクターのいないシーン（例: シーン F。アイテム 6 個）だと、H 中に F7 窓とボタンが出ず、シーンを動かせない（窓は StudioHost.HasPlacedCharacters のときだけ出していた） | シーンを H に置いていれば（StudioHost.HasPlacedScene）窓とボタンを出す。キャラクターがいないときは「シーン全体」だけを動かせるようにし、回転の中心はシーンの原点（StudioHost.SceneOrigin）にする |
| 0.4.5 | （機能追加）H で動かした位置などを簡単に保存したい。スロット 3 個で保存・ロードし、シーンを読み込んだら最後に保存したスロットを再現する | シーンの設定（KKS_FreeHStudioSceneLoader.scenes.xml）にスロット 3 個の配置（SceneLayout）を追加。保存するのは、シーンのルートの最初に置いた位置からのずれ（最初の向きを基準にするので、H のマップや開始位置が変わっても同じ関係になる）、キャラクターの changeAmount（親のフォルダ・アイテムからの相対値）と表示のオンオフ、「キャラと連結先」で動かしたフォルダ・アイテムのオフセット（dicKey ごと）。H でキャラクターを置き、プラグインのアニメーションを始めたあとに、最後に保存したスロットを再現する（シーンごとにオフにできる）。F7 窓に「保存」「ロード」「消す」を追加 |
| 0.4.6 | ①「足りないMODを調べる」ボタンをなくし、シーンをクリックしたときに一緒に表示したい ② 足りない MOD は薄い赤、揃っているときは薄い黄緑の文字にしたい ③ キャラのボイスを再生していると、フリーH 画面に戻っても鳴り続ける | ① シーンをクリックするとシーンを読み込み、そのとき Sideloader が報告した足りない MOD を記録しているので、ボタンなしでそのまま表示する（コピーのボタンは残す） ② 文字色を GUIStyle で指定 ③ Studio の VoiceCtrl は声が終わると次の声を再生し（NextVoicePlay）、音声はキャラクターの外（Manager.Voice）にあるので、キャラクターを消しても止まらなかった。Studio では OCIChar.OnDelete が voiceCtrl.Stop を呼ぶので、H の終了時（UnloadOverlay）にキャラクターを消す前に同じく止める |
| 0.4.7 | マップのないシーン（シーン G。KK 形式 1.0.4.2、map -1、アイテムで作った和室）が、Studio では暗い部屋に行灯の明かりなのに、H では全体が明るく照らされる | Studio のシーン（CharaStudio_Data/level2）のライトを UnityPy で調べると、有効なのはキャラライト（cullingMask = Chara だけ）のみで、マップがなければアイテムの層（Map, MapNoShadow）を照らすのはシーン内のライトだけ。H では隠したフリーH マップのライト（太陽・部屋の照明）が有効なままで、スタジオのアイテムを照らしていた。マップを隠すとき、スタジオのシーンとキャラクターのもの以外のライトの cullingMask から Map と MapNoShadow を外す（H のキャラクターの照明は変わらない。H の終了時に戻す）。設定 General / Studio lighting for scenes without a map（既定オン）。置いたライトの数もログに出す |
| 0.4.8 | 0.4.7 では見た目が変わらなかった（ログ: 隠したマップのライトは SunLightstaffroom の 1 個で、Map の層だけを照らしていた）。シーンを読むと、行灯（アイテム 14/11/43）の子に点光源（強さ 1.01、範囲 6、影あり）が 1 個ある | 推測で直さず、比べるための記録を追加: 環境光（モード・色・アンビエントプローブ）、品質設定（ピクセルライト数・影）、キャラ用のシェーダーのグローバル値、カメラの描画方式と画面効果、すべてのライト（層・強さ・影）、シーンのレンダラーの層とシェーダー。H ではシーンを置いた 3 秒後に、Studio では StudioPoseProbe（このファイルを共有）がシーンのオブジェクトが変わらなくなった 3 秒後に、同じ形式で出す |
| 0.4.9 | 0.4.8 の記録を比べた結果: 行灯の点光源は両方で同じ（強さ 1.01、範囲 6、Map 層）。アイテムのシェーダーは Shader Forge/main_StandardMDK_studio（Standard 系で環境光を受ける）。環境光（アンビエントプローブ）が Studio では 0、H では隠したマップのもの（L0 = 0.18, 0.23, 0.30）で、部屋全体が均一に明るくなっていた。反射の強さも Studio 0.7、H 1。ほかに、影（H は品質「Quality No Shadows」）とブルーム（H はオフ）が違うが、これはゲームの画質設定 | マップを隠すとき、アンビエントプローブを 0、反射の強さを 0.7 にする（Studio の level2 と同じ。H の終了時に戻す）。影とブルームはゲームの画質設定（セルフシャドウ、ブルーム）に任せる |
| 0.4.10 | 0.4.9 でも白っぽいまま（環境光・反射は Studio と同じになった） | 比べる記録にカメラの画面効果の設定値と色調補正のボリュームを追加 |
| 0.4.11 | 記録の比較: 色調補正（AmplifyColor）は同じ。違うのは、影（H は品質「Quality No Shadows」）、ブルーム（H はオフ。シーンは強さ 0.79・しきい値 0.5・ぼかし 5.1）、表示中のレンダラーの数（Studio 41、H 44）。影がないと行灯の点光源（範囲 6）が壁や障子を通り抜けて部屋全体を照らす。レンダラーの差は、非表示のフォルダー（key 0）の中のアイテム 3 個（障子）で、Studio では TreeNodeCtrl.RefreshVisibleLoop が隠すが、ここでは木の UI ごと止めていた | ① シーンの影とブルームの設定を使う（Studio の SystemButtonCtrl と同じ。影はゲームの品質レベルの組（偶数が影あり）を切り替え、ブルームはカメラの BloomAndFlares に値を入れ、CameraEffectorConfig が毎フレーム設定で戻さないように useBloom を止める。H の終了時に戻す）。設定 General / Use the shadows and bloom of the scene（既定オン）。マップのあるシーンにも効く ② 置いたあとに木をたどり、非表示のフォルダー・アイテムの中のものを onVisible(false) で隠す（子の表示スイッチ自体は変えない） |
| 0.4.12 | 0.4.11 でも、同じ角度で見て Studio より白っぽく色が薄い（Studio は暗く色が濃い） | アイテムのシェーダー（abdata/studio/01.unity3d の Shader Forge/main_StandardMDK_studio）を UnityPy で調べると、_lightmodel_ambient（RenderSettings の環境光の色）と _ambientshadowG（全体の陰影の色）を使う。環境光の色は Studio (0.32, 0.34, 0.39) ×1.5、H は (1, 1, 1) ×1 で、これが全体を白くしていた。マップのないシーンでは環境光の色（空・地平線・地面と強さ）も Studio の level2 の値にする。全体の陰影の色はシーンの値（ambientShadow、a = 1 - ambientShadowG。SystemButtonCtrl.SetAmbientShadow と同じ）を使う（Use the shadows and bloom of the scene の設定で。H の終了時に戻す） |
| 0.4.13 | 0.4.12 でほぼ Studio どおりになったが、オブジェクトの周りの影が濃い | アンビエントオクルージョン（AmplifyOcclusionEffect）が違う: Studio はシーンの値（色 0.78 の灰、半径 0.153）とカメラの既定値（サンプル High、強さ 1、指数 1、バイアス 0.1、フェードなし）、H はゲームの値（色 黒、半径 0、指数 0、サンプル Low）。シーンの値と Studio のカメラの値を入れ、CameraEffectorConfig が毎フレーム設定で切り替えないように _useAmplifyOcclus を止める（H の終了時に戻す） |
| 0.4.14 | 「このシーンを使う」ボタンもなくし、一覧でシーンを選ぶだけでセットしたい | 一覧のクリックで読み込み（Selection.Preview）に続けてセット（Selection.Use）する。解除したあと同じシーンをもう一度クリックするとセットし直す。H から戻ったときの自動セットも同じ処理を使う |
| 0.4.15 | 「解除」ボタンを左下に、幅を文字に合わせたい。解除したら、セットしたときに変わったマップのサムネイルと名前も元に戻したい | ボタンを左の欄の一番下（状態の文の下）に置き、幅を文字に合わせる（ExpandWidth(false)）。シーンをセットする前のフリーH のマップを覚え（Selection._mapBefore。スタジオマップのあるシーンは土台のマップに切り替えるため）、解除したときや、クリックしたシーンをセットできなかったときに戻す（resultMapInfo を戻すとゲームがサムネイルと名前を出し直す。同じマップなら FreeHScene.SetMapSprite を呼ぶ）。通常のマップ選択で別のマップを選んだら、それをプレイヤーの選択として覚えたマップを捨てる |
| 0.4.16 | シーン A: Studio では男キャラ 2 人が明るい黄緑のシルエット（Studio の「シルエット」、visibleSimple と simpleColor）なのに、H では普通に表示される | Studio の ChaControl.UpdateVisible（CharaStudio_Data の Assembly-CSharp）はシルエットの判定に fileStatus.visibleSimple をそのまま使うが、ゲームのものは H シーンの分岐の中でだけ使う。この分岐はシーンのキャラでは通らないようにしていた（H の男の表示設定を受けないため）ので、シルエットも消えていた。トランスパイラーで、分岐の前のシルエットの初期値（false）を、シーンのキャラなら visibleSimple にする（H のキャラは今までどおり false から始まる） |
| 0.4.17 | （機能追加）シーンの一覧を、ファイル名ではなくシーンの画像のサムネイルで出したい。3 列に収まる大きさで、「解除」の右のチェックボックスで切り替える | 一覧の幅に 3 列で並べ（16:9、ファイル名を下に）、スクロールで見えている行だけを描く。画像は見えている分を Update で 1 フレーム 12ms まで読み込み、最大 150 枚を古い順に捨てて保持する（再読込で捨てる）。切り替えは設定 Button / Show scene thumbnails（既定オン）にも保存する。セット中のシーンには「セット中」と出す |
| 0.4.18 | サムネイル表示のときは下のプレビュー画像を出さず、その分一覧を下に伸ばしたい（ファイル名の一覧では残す）。選んでいるシーンは薄い赤の枠で示したい | サムネイル表示ではプレビューを描かず、一覧の高さを 300 + 180 + 4 にする（見えている行の計算も同じ高さで）。選んでいるシーンのセルに薄い赤（1, 0.55, 0.55）の太さ 3 の枠を描く |
| 0.5.0 | （機能追加）シーンのキャラクターを別のキャラカードに差し替えたい（シーン選択と H 中の両方）。「シーンのキャラクター」で、女性は「女性を表示」の下、男性は「男性を表示」の下に並べたい。女性・男性を最初からオフにする設定を窓から変えたい | 差し替えは Studio の「変更」（OCIChar.ChangeChara: カードの見た目を読み、シーンのポーズと状態は残す）と同じ。シーンの設定に dicKey ごとのカード（UserData/chara からの相対パスと名前）を保存し、H でキャラクターを作る直前にカードを OICharInfo.charFile に読む（LoadCharaFile、状態は読まない）。H 中は ChangeChara をその場で呼び（体は Studio の骨で作る）、FK と IK の両方を使うキャラは EnableFKIK をやり直す。元に戻せるように、最初に差し替える前のシーンのカード（拡張データ込み）を BepInEx/cache に保存する。カード一覧は UserData/chara/female か male の KKS カード（【KoiKatuCharaSun】）を 4 列のサムネイルで出す（性別の違うカードは読み込み時に断る）。キャラの一覧は性別ごとに表示スイッチの下に並べ、「差し替え」「戻す」を付ける。新しいシーンの初期値（Load studio female/male characters）を窓のチェックボックスで変えられるようにする |
| 0.5.1 | シーン選択の窓で、キャラクターの「差し替え」ボタンが右端で切れ、右の欄に横スクロールバーが出る | 窓の幅を 1040 から 1180 にする（右の欄が 140 広がる。画面より広いときは画面に合わせる） |
| 0.5.2 | H のキャラクターの窓と、差し替えのカード一覧の右上に × を付けて閉じたい。シーンのキャラクターのコーディネートを変えたい | 窓の右上に × ボタン。服は Studio と同じく、服の種類（OCIChar.SetCoordinateInfo: 普段着・水着・パジャマ・バスタオル）と、UserData/coordinate のコーデカードの読み込み（OCIChar.LoadClothesFile: 今の種類に読み込む）。シーンの設定に dicKey ごとに保存し、H でキャラクターを置いた直後に適用する（H 中はその場で）。「服を戻す」はキャラのカード（差し替え先か、保存しておいたシーンのカード）を読み直し、シーンの服の種類に戻す。シーン選択ではキャラの行の「服 ▼」で開き、H ではキャラクターの窓で選んだキャラの服を変える。カード一覧の窓はコーデカード一覧にも使う |
| 0.5.3 | キャラクターには 5 個以上のコーデがあるので、それも選びたい | 5 個目以降は MoreOutfits（KK_Plugins）が足した枠。服の種類を ChaFile.coordinate の数だけ 4 列で並べ、名前は 4 個目までゲームのもの、それ以降は MoreOutfits の Plugin.GetCoodinateName（置いたキャラ）か、カードの拡張データの CoordinateNames（置く前）から取る（MoreOutfits はリフレクションで探し、無くても動く） |
| 0.5.4 | シーン選択で別のカードに差し替えたキャラの服の種類にも、そのカードの追加コーデ（MoreOutfits）を出したい。カードの一覧の窓が開いたまま後ろにあるとき、「差し替え」で前に出したい | 置く前のキャラの服の数と名前は、差し替えカードがあればそのカードを一度読んで（拡張データ込み、パスごとに保持）取る。一覧を開くボタンで、その窓を GUI.BringWindowToFront と FocusWindow で前に出す |
| 0.5.5 | キャラごとに全着・半脱・全脱を選びたい | 服の操作に「服の状態」（シーンのまま／全着／半脱／全脱）を追加し、シーンの設定に保存する。全部の服をまとめて ChaControl.SetClothesStateAll で変える（Studio の OCICharMale は SetClothesStateAll を持たないので、男性にも効くよう ChaControl を直接使う）。「シーンのまま」と「服を戻す」は、置いたときに覚えた部位ごとの状態（fileStatus.clothesState）に戻す |
| 0.5.6 | シーン選択のキャラクターの一覧で、キャラごとに枠を付けて分かりやすくしたい | キャラの行と服の操作を、縁だけの枠（灰色。服を開いているキャラは薄い赤）で囲む。枠は 8×8 の縁取りテクスチャを GUIStyle の border で伸ばして描く |
| 0.5.7 | H の窓をつかんで動かすと、ゲームの画面（カメラ）も回る。窓が縦に長いと画面からはみ出す | H のカメラ（BaseCameraControl_Ver2）は Input.GetMouseButton を直接読み、UI の上かどうかを見ない（uGUI を止める GuiBlocker も効かない）。マウスがプラグインの窓・ボタンの上にあるとき、またはそこで押したボタンを押している間は、InputMouseProc と InputMouseWheelZoomProc を false にする（窓の範囲は OnGUI の Repaint で集め、押したかどうかは Update で見る）。H の窓の中身を、画面の高さ - 60 を上限にしたスクロールビューに入れる |
| 0.5.8 | 0.5.7 のスクロールで H の窓が小さくなり、横スクロールバーも出て右が切れる | スクロールビューの高さを、前の Repaint で測った中身の高さ（画面の高さ - 60 まで）にする。中身の幅を元の窓の幅に固定し、窓をスクロールバーの分（18）だけ広げる。横スクロールバーは出さない |
| 0.5.9 | H の窓で、キャラの行の「戻す」と「シーン全体」が右端で少し切れる | H の窓の中身の幅を 440 から 490 にする |

### 未確認・既知の制限
- 目に見えるアイテム（家具など）の位置・色・マテリアルの再現は、まだ確認していない
