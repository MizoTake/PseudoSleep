# PseudoSleep + Sunshine / Moonlight 実装仕様

更新日: 2026-09-23。初期の構築案を、実装と実機確認を反映した仕様へ整理しました。導入コマンドは[導入手順](docs/setup.md)、確認済み・未確認の範囲は[検証記録](docs/deployment-report.md)を参照してください。

## 役割と対象

Windows 11 x64の同一ユーザー・対話セッションを対象とします。PseudoSleepは画面状態、物理入力、スリープ防止、復旧を担当します。Sunshineは認証と映像・音声・入力の配信、仮想ディスプレイドライバはキャプチャ可能な画面の提供を担当します。Moonlightは別端末で実行します。

Windows本体をスリープさせず、物理ディスプレイをWindows Display Topologyから外します。単純なモニター電源OFFのメッセージだけには依存しません。画面の管理主体はPseudoSleepで、Sunshineの`dd_configuration_option`は`disabled`とします。

## 通常利用と接続

既定では通常時に物理画面だけを有効にします。Sunshineは`output_name`を空にして画面を自動選択し、`global_prep_cmd`から`PseudoSleep.exe stream-start`を実行します。常駐プロセスは同一ユーザーのIPCで`SUNSHINE_CLIENT_WIDTH` / `SUNSHINE_CLIENT_HEIGHT`を受け取ります。

Sunshineは開始フックより先にエンコーダーを検査します。無効な仮想出力に固定するとここで失敗するため、通常時は物理画面で検査を通し、実際の配信準備完了時には仮想画面だけを有効にします。常設仮想画面を明示的に選んだ場合だけ、従来の固定GUIDを使用します。

接続時に物理画面を消灯し、要求された解像度へ変更します。解像度追従を無効にした場合は設定の幅・高さを使用します。Hzは常にPseudoSleepの設定値です。ドライバが公開していないモードは拒否します。端末の種類や画面番号を固定する判定はしません。

常設仮想画面を選び接続時消灯を無効にした場合、物理画面の位置・解像度・回転・Hzを保って仮想画面だけを変更します。通常時に仮想画面を無効にする構成では、接続時消灯を必須とします。

配信開始でSunshineログ末尾から接続監視を開始し、最後のクライアント切断を検出したら物理画面を復元します。接続が30秒以内に確認できない場合、ログが読めない場合、Sunshineが停止した場合も復元を優先します。切断後のサービス再起動で古いDesktopセッションを終了し、次の接続は新規起動にします。開始フックのundoには`stream-stop`を登録し、アプリ終了時も復元します。Sunshine 2026.914.233613のInfoログ形式に依存するため、バージョン更新時に確認が必要です。

## 状態と復旧順序

`Normal → EnteringPseudoSleep → PseudoSleep → Waking → Normal`を基本とし、失敗時は`Recovery`または`Error`になります。

1. 設定、登録済み復帰入力、Sunshineサービス、画面所有設定、必要なサービス操作権限を検証します。
2. 現在の画面構成を取得し、画面変更前に復旧ジャーナルを原子的に保存します。
3. 独立Guardianの起動確認後、必要に応じて`PowerRequestSystemRequired`を取得します。
4. 物理画面を残して仮想画面を有効化し、DXGI Desktop Duplicationでフレーム取得を確認します。
5. 仮想画面だけへ切り替え、サイズ・Hz・キャプチャを再確認します。疑似スリープ構成はWindowsの保存済み構成へ書き込みません。
6. 復帰時は保存済みの画面ID・位置・サイズ・回転・Hzを検証してから復旧情報を削除し、電源要求を解除します。
7. 自動切断が有効なら、別プロセスからSunshineサービスを停止・開始します。すべての既存配信が切断されます。

接続準備で新しく疑似スリープへ入った後に失敗した場合は画面を復元します。既に疑似スリープ中の解像度変更失敗では既存状態を維持します。

異常終了はGuardianが検出します。未完了のジャーナルがあれば、次回起動でも設定ファイルの読込前に復旧を試みます。保存対象の物理モニターが見つからない場合は、利用可能な物理画面1台の緊急有効化を試み、不完全な復元としてジャーナルを残します。OSやGPU自体の停止、常駐アプリとGuardianの同時終了、破損したバックアップの完全復旧は保証しません。

## 入力とIPC

Raw Inputのデバイスパスを許可リストと照合し、除外パターンを優先します。デバイスがない注入入力は復帰させません。既定の入力ガードは3000msで、0～10000msから設定できます。経過時間は単調時計で判定します。

IPCと二重起動防止の名前にはユーザーSIDとセッションIDを含めます。名前付きパイプは`CurrentUserOnly`で制限し、コマンド長と通信時間に上限を設けます。常駐アプリは管理者権限を必要としません。

## 設定と保存

主な項目は`virtualDisplayDevicePath`、`virtualDisplayDeviceId`、`wakeDevices`、`ignoredDevices`、`wakeGuardMs`、`width`、`height`、`refreshRate`、`preventSystemSleep`です。

`sleepOnMoonlightConnect`、`disconnectMoonlightOnWake`、`followClientResolution`は既定で有効、`keepVirtualDisplayInNormalMode`は既定で無効です。最後のクライアントサイズは`lastClientWidth` / `lastClientHeight`へ記憶します。`restoreOnStartup`フィールドの値にかかわらず未完了の復旧は実行します。

設定の記憶や補助的な状態取得に失敗しても、完了済みの配信準備を失敗応答へ変更しません。復旧ジャーナルの保存は必須のままです。診断ログのI/Oエラーは画面復元処理を妨げません。

設定は`%APPDATA%\PseudoSleep\config.json`、復旧情報とログは`%LOCALAPPDATA%\PseudoSleep`に保存します。Sunshine管理UIの認証情報は保存しません。物理入力のログにはデバイスパスが含まれるため、生ログを公開しません。

## 音声・起動・権限

音声はSunshineの`virtual_sink`とMoonlightのホスト音声再生設定へ委ねます。音量値を0にする機能ではありません。特定の物理出力へ固定したアプリは、仮想出力の切り替えに追従しないことがあります。

PseudoSleepはユーザーのStartupショートカットでログオン時に起動します。SunshineはOS起動時に自動起動するサービスです。ドライバ導入とSunshine設定は管理者で実行します。自動切断を使用するユーザーにはSunshineサービスの`QUERY_STATUS | START | STOP`だけを追加し、サービス設定変更権限は追加しません。

## 対象外と検証

Windowsのスリープメニュー置換、Wake-on-LAN、自動ログオン、HDR自動切り替え、VPN構築、複数クライアントによる同時解像度変更は対象外です。OSの電源プランは変更しません。

コアの回帰テスト、PowerShellの検証、コンソールなしCLIの試験を通常のビルドに含めます。実機の画面切り替え・復旧・Moonlight映像・音声・物理入力は別途確認します。自動テストの合格を実機試験の代用とは扱いません。
