# HeartRate OSC Bridge for Windows 11

Windows標準のBluetooth LE GATT APIでBLE対応の心拍センサから心拍数を受信してBPMを表示するWPFアプリです。ANT+ドングルは使用しません。

## 起動

```powershell
dotnet run --project .\HeartRateOscBridge\HeartRateOscBridge.csproj
```

設定でシミュレーションモードをオフにし、心拍センサを装着して電源を入れてから「接続」を押します。最初に8秒間、周囲のBLEデバイスをスキャンして一覧表示するので、心拍センサを選んで「選択して接続」を押します。アプリは選択したデバイスのHeart Rate Service (0x180D)を確認し、Heart Rate Measurement (0x2A37)の通知を購読します。

手動接続で選択した心拍センサは設定ファイルに記憶されます。次回起動時は保存済みのセンサを自動スキャンして接続します。未接続中は常時バックグラウンドスキャンを行い、センサが見つかるまで再試行します。

接続後に心拍センサが圏外になったり電源オフになったりした場合も、切断を検知して自動的に再スキャン・再接続します。

設定は実行ファイルと同じディレクトリの`settings.json`に保存されます。

## GitHub Actionsでのビルド

任意のブランチへpushすると、GitHub ActionsがWindows x64向けの単一ファイル版を発行し、ZIPをActions artifactとして30日間保存します。ZIP名はプロジェクトファイルの`Version`から生成されます。たとえば`1.2.3`なら`HeartRateOscBridge-Release-v1-2-3.zip`です。手動実行はActionsタブの「Build Windows Release」から開始できます。

WindowsのBluetoothをオンにしてください。通常、Windowsの設定画面で事前にペアリングする必要はありません。別のアプリが心拍センサを占有している場合は切断してください。

接続後はメイン画面の「心拍グラフ」ボタンから、接続中に受信した心拍数の時間変化を別ウィンドウで確認できます。横軸は実時間（HH:mm）で、データ量に応じて1分・5分・15分などの目盛り間隔を自動選択します。グラフは受信中に自動更新されます。
