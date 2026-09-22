# HeartRate OSC Bridge 詳細仕様書

## 1. 概要

HeartRate OSC Bridgeは、Windows 11の標準Bluetooth LE機能を使ってBLE心拍センサから心拍数を受信し、画面表示、統計表示、時系列グラフ表示、およびVRChat向けOSC送信を行うデスクトップアプリケーションである。

本アプリはANT+ドングルおよびANT+通信を使用しない。対象は、Bluetooth LE GATTの標準Heart Rate Serviceを提供する心拍センサである。CooSpo HW706など、標準仕様に対応するセンサを利用できるが、特定の製品名には依存しない。

## 2. 対象環境と発行形式

- OS: Windows 11
- UI: WPF
- フレームワーク: .NET 10 Windows
- ターゲット: `net10.0-windows10.0.19041.0`
- CPU/ランタイム識別子: `win-x64`
- 発行方式: 単一ファイル
- 配布方式: フレームワーク依存
- .NETランタイム: 利用者が別途インストールする
- ReadyToRun: 無効
- 現行バージョン: `1.0.0`
- アセンブリ名/実行ファイル名: `HeartRateOscBridge`

Release発行時の主要設定は次のとおりである。

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>false</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<PublishReadyToRun>false</PublishReadyToRun>
```

## 3. Bluetooth LE受信仕様

### 3.1 スキャン

Windows標準の`BluetoothLEAdvertisementWatcher`をActiveスキャンモードで使用する。

- スキャン時間: 1回5秒（手動選択画面では初回8秒）
- 同一Bluetoothアドレスの重複通知は除外
- デバイス名が広告に含まれない場合は「名称なし（BLEデバイス）」と表示
- 表示項目: デバイス名およびBluetoothアドレス（16進数12桁）
- ANT+ドングルは対象外

### 3.2 初回・手動接続

メイン画面の「接続」を押すと自動スキャンを停止し、デバイス選択画面を開く。自動スキャン停止中に手動接続処理を実行するため、自動接続と手動接続が競合しない。

デバイス選択画面では周囲のBLEデバイスを一覧表示し、次の操作を提供する。

- デバイスをクリックして選択
- 「再スキャン」で一覧を更新
- 「選択して接続」で選択したデバイスに接続
- 選択行のダブルクリックでも接続可能

接続時には次のGATT構成を検証する。

- Heart Rate Service: UUID `0000180d-0000-1000-8000-00805f9b34fb`（`0x180D`）
- Heart Rate Measurement: UUID `00002a37-0000-1000-8000-00805f9b34fb`（`0x2A37`）
- Heart Rate MeasurementのNotifyを有効化

対象サービスまたは特性が存在しないデバイスを選択した場合は、接続エラーを表示する。

### 3.3 心拍数の解析

Heart Rate MeasurementのFlagsを確認し、心拍数の値形式を判定する。

- Flagsのbit 0が0: 8-bit unsigned値として解釈
- Flagsのbit 0が1: 16-bit unsigned値として解釈
- 0より大きく250未満の値だけを有効な心拍数として採用

有効な値を受信すると、画面、統計、グラフ履歴、OSC送信を更新する。

### 3.4 バッテリー残量

接続時にBattery Service `0x180F`からBattery Level `0x2A19`を読み取る。値は1 byteの百分率（0〜100）として解釈し、メイン画面のセンサ名の横に小さく表示する。センサがBattery Service/Levelに対応しない場合、読み取りに失敗した場合、または値が規定範囲外の場合は`電池 --`と表示する。Battery LevelのNotifyに対応するセンサでは通知を購読して表示を更新する。バッテリーの読み取り失敗は心拍数の受信・接続を妨げない。

### 3.5 自動スキャン・自動再接続

設定ファイルに保存された心拍センサがある場合、アプリ起動後に自動スキャンを開始する。保存済みデバイスとの照合は次の優先順位で行う。

1. Bluetoothアドレスの一致
2. 保存名とデバイス名の大文字小文字を区別しない一致

未接続中は、スキャンと待機を繰り返す。保存済みデバイスが見つかった場合は自動接続する。

接続後にセンサが圏外になった場合、電源が切れた場合、またはBluetooth接続が切断された場合は、切断イベントを検知し、次の処理を行う。

1. 現在の接続を破棄
2. 接続状態を未接続へ更新
3. OSCへ切断状態を送信（OSC有効時）
4. 自動スキャンを再開
5. 保存済みセンサが見つかれば再接続

再接続時に、アプリを終了していなければ心拍履歴・統計値はリセットしない。

## 4. メイン画面仕様

メインウィンドウの表示順は次のとおりである。

1. アプリタイトル
2. 接続状態
3. 心拍センサ名
4. 「現在の心拍数」
5. 現在値（`XX BPM`）
6. 接続後の集計
7. 過去5分の集計
8. 「集計をリセット」
9. 「切断」「設定」「心拍グラフ」
10. OSC状態表示およびOSC ON/OFFボタン
11. ステータス

接続状態は「未接続」「接続中…」「接続中」などで表示する。心拍センサ名は製品名に限定せず、「心拍センサ: デバイス名」の形式で表示する。

### 4.1 統計値

受信した各心拍数に受信時刻を付与し、次の統計を表示する。統計計算は全受信値の配列を保持せず、接続後の件数・合計・最小値・最大値と、直近5分だけのキューを使用する。

- 接続後の最大値
- 接続後の最小値
- 接続後の平均値
- 現在時刻から過去5分以内の最大値
- 現在時刻から過去5分以内の最小値
- 現在時刻から過去5分以内の平均値

平均値は小数第1位まで表示する。値がない場合は`--`を表示する。

「集計をリセット」を押すと、接続後統計、過去5分統計、およびグラフ履歴を同時に消去する。センサの一時切断・再接続では消去しない。アプリ終了後に履歴を永続化する仕様はない。

### 4.2 長時間計測時のデータ量削減

長時間計測で受信値を無制限に記録すると、グラフ描画とメモリ使用量が増大するため、グラフ履歴は時間バケット方式で集約する。各バケットは先頭値、末尾値、最小値とその時刻、最大値とその時刻を保持する。

グラフへ渡す際はこれらの代表点を時刻順に展開するため、単純な平均化や間引きによって短時間の高ピーク・低ピークを失わせない。グラフ表示時に各データ点へ個別のEllipseを生成せず、ピークを含む折れ線のみを描画する。

計測開始からの経過時間に応じたバケット幅は次のとおりである。

| 経過時間 | バケット幅 |
|---|---:|
| 10分以内 | 1秒 |
| 1時間以内 | 2秒 |
| 3時間以内 | 5秒 |
| 12時間以内 | 15秒 |
| 24時間以内 | 30秒 |
| 24時間超 | 60秒 |

バケット幅が変わる境界では、既存バケットを最小値・最大値を維持したまま新しい幅へ再集約する。

## 5. グラフ仕様

メイン画面の「心拍グラフ」で別ウィンドウを開く。グラフは共有履歴を参照し、受信中は500ms間隔で再描画する。

- タイトル: 「心拍数の時間変化」
- 縦軸: BPM
- 横軸: 実時刻（`HH:mm`）
- データ点: 集約バケットの先頭・末尾・最小・最大の時刻と心拍数
- ピークを含む折れ線を表示（長時間計測時の個別マーカーは生成しない）
- 縦軸範囲: データ最小値・最大値に余白を加えて自動決定
- 縦軸目盛り: 4区間
- 縦軸タイトルは目盛りラベルと重ならない位置に配置

横軸目盛りはデータ開始時刻ではなく、時計の切りのよい時刻を基準に配置する。データの時間幅に応じた目盛り間隔は次のとおりである。

| データ時間幅 | 目盛り間隔 |
|---|---:|
| 10分以内 | 1分 |
| 45分以内 | 5分 |
| 3時間以内 | 15分 |
| 12時間以内 | 30分 |
| 12時間超 | 1時間 |

例えば5分間隔の場合は`09:50`、`09:55`、`10:00`、`10:05`のように、実時刻の分境界に揃えて表示する。

## 6. 設定画面仕様

設定画面はダークテーマで表示し、次の設定を変更できる。

### 6.1 一般設定

- シミュレーションモード（実機なし）
- Windowsのスタートアップに登録する
- 最小化・閉じるときにタスクトレイへ格納する

シミュレーションモードでは実機のBLE接続を行わず、テスト用の心拍数を生成する。

### 6.2 OSC基本設定

- OSC送信の有効/無効
- 送信先IPアドレスまたはホスト名
- 送信先UDPポート
- Normalisedの分母
- VRChat Chatbox送信の有効/無効

ポートは1〜65535でなければならない。Normalisedの分母は0より大きい数値でなければならない。既定値は次のとおりである。

- 送信先: `127.0.0.1`
- ポート: `9000`
- Normalisedの分母: `240`

### 6.3 パラメータ個別設定

VRCOSC互換パラメータと後方互換パラメータは設定画面上で別セクションに分ける。各パラメータは個別に送信可否を変更できる。画面上の名前はVRChatで使用するフルパスで表示する。

## 7. OSC送信仕様

OSCは設定された送信先へUDPで送信する。メイン画面のOSC ON/OFFボタンでも送信状態を切り替えられ、現在の状態を画面に表示する。

### 7.1 VRCOSC互換パラメータ

| OSCアドレス | 型 | 送信内容 |
|---|---|---|
| `/avatar/parameters/VRCOSC/Heartrate/Value` | Int32 | 現在のBPM |
| `/avatar/parameters/VRCOSC/Heartrate/Normalised` | Float | `BPM / 分母`を0〜1にクランプ |
| `/avatar/parameters/VRCOSC/Heartrate/Average` | Int32 | 接続後の平均BPM（四捨五入） |
| `/avatar/parameters/VRCOSC/Heartrate/Connected` | Bool | BLE接続状態 |
| `/avatar/parameters/VRCOSC/Heartrate/Enabled` | Bool | BLE接続状態 |
| `/avatar/parameters/VRCOSC/Heartrate/Beat` | Bool | 心拍受信ごとに交互反転するビート値 |
| `/avatar/parameters/VRCOSC/Heartrate/Hundreds` | Float | BPMの百の位を0〜0.9で送信 |
| `/avatar/parameters/VRCOSC/Heartrate/Tens` | Float | BPMの十の位を0〜0.9で送信 |
| `/avatar/parameters/VRCOSC/Heartrate/Units` | Float | BPMの一の位を0〜0.9で送信 |

`Normalised`の計算では、設定された分母が1未満にならないよう内部的に1へ補正し、結果を0〜1に制限する。既定分母は240であり、設定画面から変更できる。

### 7.2 後方互換パラメータ

| OSCアドレス | 型 | 送信内容 |
|---|---|---|
| `/avatar/parameters/HR` | Int32 | 現在のBPM |
| `/avatar/parameters/isHRConnected` | Bool | BLE接続状態 |
| `/avatar/parameters/isHRActive` | Bool | 接続中または心拍受信中の状態 |
| `/avatar/parameters/isHRBeat` | Bool | 接続状態送信時にfalseを送信 |
| `/avatar/parameters/HeartBeatToggle` | Bool | 心拍受信ごとに交互反転する値 |

### 7.3 送信タイミング

- 心拍数受信時: 有効な心拍関連パラメータを送信
- 接続成功時: 接続状態をtrueで送信
- 切断時: 接続状態をfalseで送信
- 設定保存時: 接続中であれば現在の接続状態と直近心拍値を再送
- メイン画面のOSC有効化時: 接続中であれば現在状態を再送

送信失敗時はアプリの通常動作を停止させず、UDP送信例外を内部で処理する。

### 7.4 VRChat Chatbox

Chatbox送信を有効にすると、心拍数受信時に次のOSCメッセージを送信する。

- アドレス: `/chatbox/input`
- 文字列: `♡ 123 bpm`（数値部分は現在のBPM）
- immediate: true
- notification: false

送信先IPアドレスとポートは通常のOSCパラメータと共通である。

## 8. 設定ファイル

設定は`AppContext.BaseDirectory`、つまり実行ファイルと同じディレクトリの`settings.json`に保存する。LocalAppDataは使用しない。JSONはインデント付きで保存する。

主な項目は次のとおりである。

| 項目 | 型 | 既定値・用途 |
|---|---|---|
| `Simulation` | bool | false。シミュレーションモード |
| `StartWithWindows` | bool | false。Windowsスタートアップ登録 |
| `MinimizeToTray` | bool | true。最小化・閉じる時の格納 |
| `LastDeviceAddress` | ulong/null | 最後に接続したBLEアドレス |
| `LastDeviceName` | string | 最後に接続した名前 |
| `OscEnabled` | bool | false。OSC送信全体の有効状態 |
| `OscHost` | string | `127.0.0.1` |
| `OscPort` | int | `9000` |
| `ChatboxEnabled` | bool | false |
| `NormalisedDenominator` | double | `240` |
| `Vrcosc*` | bool | VRCOSC互換パラメータの個別設定 |
| `Legacy*` | bool | 後方互換パラメータの個別設定 |

設定画面で保存した内容は即時にファイルへ反映し、次回起動時に読み込む。

## 9. タスクトレイ・スタートアップ

タスクトレイにはHeartRate OSC Bridgeのアイコンを表示する。アイコンのメニューには次の項目がある。

- ウィンドウを表示
- 終了

トレイアイコンのダブルクリックでもウィンドウを表示する。

「Windowsのスタートアップに登録する」を有効にすると、現在の実行ファイルをWindowsユーザーのRunキーへ登録する。スタートアップ起動時には`--startup`引数を付け、メインウィンドウを表示せずトレイ格納状態で開始する。

「最小化・閉じるときにタスクトレイへ格納する」が有効な場合、最小化または閉じる操作では終了せずトレイへ格納する。トレイメニューの「終了」から明示的に終了できる。

## 10. アプリケーションアイコン

- EXEアイコン: `Assets/HeartRateOscBridge.ico`
- ウィンドウおよびトレイ表示: `Assets/HeartRateOscBridge.png`
- デザイン: ハート、心電図、OSC連携を想起させる青い枠と青・橙の接続点
- ICOはWindows Explorerで認識しやすい複数サイズ構成

## 11. データ保持と制約

- 心拍履歴はアプリ実行中のみメモリ上に保持し、長時間計測では時間バケットへ集約する
- センサ切断・再接続では履歴を保持する
- 「集計をリセット」で統計とグラフ履歴を消去する
- アプリ終了時に心拍履歴を保存する機能はない
- センサは標準Heart Rate Service/Measurementに対応している必要がある
- OSCはUDPのため、到達確認や再送制御は行わない
- OSC送信失敗は画面表示やBLE受信を妨げない
- 複数センサの同時接続には対応しない
- ANT+には対応しない

## 12. ソース構成

| ファイル | 役割 |
|---|---|
| `MainWindow.xaml/.cs` | メイン画面、接続制御、統計、トレイ、OSC切替 |
| `DeviceSelectionWindow.xaml/.cs` | BLEデバイス一覧と手動選択 |
| `HeartRateSources.cs` | BLEスキャン、GATT接続、心拍・バッテリー通知、シミュレーション |
| `HeartRateHistory.cs` | ピーク保持型の時間バケット履歴と統計集計 |
| `HeartRateGraphWindow.xaml/.cs` | 時系列グラフと実時刻目盛り |
| `Settings.cs` | 設定モデル、JSON保存、設定画面、スタートアップ登録 |
| `OscSender.cs` | OSCパケット生成、各パラメータ、Chatbox送信 |
| `Assets/` | PNGおよびICOアイコン |
| `HeartRateOscBridge.csproj` | .NET/WPF設定、バージョン、発行設定 |

## 13. 開発・発行

通常の開発ソースは次のディレクトリに置く。

```text
C:\Users\nezum\Documents\GitHub\HeartRateOscBridge
```

Gitリポジトリの既定ブランチは`main`である。ローカル環境での発行例は次のとおりである。

```powershell
dotnet restore .\HeartRateOscBridge.csproj --runtime win-x64
dotnet publish .\HeartRateOscBridge.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=false
```

発行先に、実行ファイルと同じディレクトリへ`settings.json`が生成される。利用者は対象PCに対応する.NET Desktop Runtimeをインストールしてから実行する。
