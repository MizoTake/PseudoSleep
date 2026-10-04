# 導入・設定

Windows 11 x64のホストを直接操作できる状態で導入します。別のWindowsユーザーの資格情報を使った昇格は対応しません。管理者と書いた手順も、デスクトップへログオンしている同じアカウントを昇格して実行してください。

## ビルドとホストの準備

1. .NET SDK 8以降と.NET 8 Desktop Runtimeを用意します。SDKのメジャーバージョンが新しくても、アプリの実行には.NET 8 Desktop Runtimeが必要です。
2. リポジトリのルートで次を実行します。出力先の`artifacts/`はGit対象外です。

```powershell
.\scripts\Build.ps1 -Publish
.\scripts\Download-Host.ps1
.\artifacts\publish\PseudoSleep.exe backup artifacts\preinstall-display-backup.json | Out-String
```

3. SunshineとVirtual Display Driverを新しく導入する場合、同じアカウントの管理者PowerShellで実行します。既に別途導入している場合は、各製品の設定を確認して次へ進めます。

```powershell
.\scripts\Install-Host.ps1
```

インストーラーとアーカイブの固定SHA256、および実行ファイル・ドライバの署名を確認します。ドライバは検証したアーカイブから新しく展開します。既存ドライバ・他の仮想ディスプレイは削除しません。設定を変更する前にバックアップを残します。再起動要求が表示された場合は、Windows再起動後に続けてください。

GPUとエンコーダーは機種名で固定しません。新規ドライバのGPUを明示したい場合は`-GpuName 'GPUの正式な表示名'`、Sunshineのエンコーダーを変更したい場合は`-Encoder nvenc`などを指定できます。指定しない場合はドライバの既定設定／Sunshineの既存設定を維持します。新規GPUの動作確認は各環境で行ってください。

4. ホスト自身で[Sunshine管理画面](https://localhost:47990/)を開き、初期アカウント設定を行います。MoonlightからホストのLANアドレスへ接続し、表示されたPINをSunshineへ入力します。まず通常のDesktop配信を確認して終了します。LANアドレスや認証情報はリポジトリへ保存しません。

## OS起動時の仮想画面を停止する

ログイン前に仮想画面が有効になる場合は、アプリとSunshineの連携設定後、通常状態で次を同じアカウントの管理者PowerShellから実行します。

```powershell
.\scripts\Set-VirtualDisplayDriverPower.ps1 -Mode OnDemand
```

設定と画面構成を`artifacts/driver-power-backup-*`へ保存し、選択した画面の親がVirtual Display Driverであることを確認して、そのドライバーを無効化します。次に通常権限でPseudoSleepを起動してください。アプリの自動起動は`Install-App.ps1`が登録します。

通常時・アプリ起動時にはドライバーを停止したままにします。Moonlightの新規接続または疑似スリープ操作で起動し、画面の取得を確認してから物理画面を消灯します。通常復帰・アプリ終了・ログオフ・復旧処理では、先に物理画面を復元してからドライバーを停止します。`status`の`virtualDisplayDriverEnabled: false`が通常時の期待値です。停止中も設定画面は登録済み仮想画面を保持します。

操作用タスクはWindows標準のPowerShellとPnpDeviceモジュールを使い、検証済みデバイスIDへの`Enable-PnpDevice`／`Disable-PnpDevice`をSYSTEMで実行します。固定したコマンドをタスク内に保存し、実行時にもハードウェアIDとDisplayクラスを確認します。PowerShellのプロファイルを読み込まず、Windows配下のモジュールを絶対パスで指定します。自動起動トリガーはなく、セットアップしたユーザーには読み取り・実行のみを許可します。ユーザーが編集可能なアプリ・スクリプトを管理者として自動実行する仕組みではありません。新しい依存パッケージは不要です。

旧版のPnPUtilを呼ぶタスクで「デバイス未接続」（1167）となり、停止中のVDDを起動できない場合は、通常状態で上記の`Set-VirtualDisplayDriverPower.ps1 -Mode OnDemand`を再実行してタスクを更新してください。疑似スリープ開始時はドライバー有効化と仮想画面の出現を待ち、映像取得に成功してから物理画面を切り離します。失敗した場合は元の画面状態へ復元を試みます。

この設定はVirtual Display Driver（ハードウェアID `Root\MttVDD`）が1つの環境に対応します。別の仮想ディスプレイドライバーや物理GPUは操作しません。ドライバー再インストールでデバイスIDが変わった場合は、設定をやり直してください。解像度追加などドライバーを再起動する作業では、いったん`-Mode AlwaysAvailable`で解除してから作業し、完了後に`-Mode OnDemand`を再設定します。

解除する場合は、通常状態で管理者PowerShellから`-Mode AlwaysAvailable`を実行し、通常権限でアプリを再起動します。対象ドライバーを有効に戻し、補助タスクを削除します。仮想画面を常時使う設定とは別です。

実際のログイン画面は再起動後に確認してください。配信中の電源断などで復帰処理を実行できなかった場合は、ドライバーの有効状態が残る可能性があります。再起動前に通常状態へ戻す運用が前提です。

状態確認は`PseudoSleep.exe status`、アプリ停止中・再起動時の確認は`scripts/Test-DriverPowerStartup.ps1`で行えます。`scripts/Test-Host.ps1`と`-CrashRecovery`は実際に画面を切り替え、復帰後のドライバー停止も検証します。これらの検証ではWindows自体の再起動は行いません。

## アプリとSunshineの連携

通常権限のPowerShellへ戻り、仮想画面と復帰用入力を設定します。

```powershell
.\scripts\Initialize-Config.ps1
.\scripts\Install-App.ps1 -SkipBuild
```

仮想画面が複数ある場合は、`PseudoSleep.exe displays | Out-String`で一覧を確認し、`Initialize-Config.ps1 -VirtualDevicePath '選んだ画面のdevicePath'`で指定します。初回起動時も仮想画面が1つなら自動選択し、未登録の場合はUSB親デバイスを確認できた入力だけを復帰候補として保存します。登録済みの選択は上書きしません。Bluetooth等の入力は、トレイの設定から「操作して登録」を使用してください。

通常時は物理画面だけを有効にします。この構成ではSunshine画面GUIDの手入力は不要です。設定画面の仮想ディスプレイ選択と復帰デバイスを確認してください。Sunshineなどの仮想入力は復帰デバイスへ登録しません。

同じアカウントの管理者PowerShellで、開始・終了フックとサービス操作権限を設定します。この操作はSunshineを再起動します。

```powershell
.\scripts\Configure-Sunshine.ps1 -AllowServiceRestart
```

次の設定を適用します。

- `global_prep_cmd`へ`PseudoSleep.exe stream-start`と`stream-stop`を追加。他の開始コマンドは維持します。
- 通常時に仮想画面を無効にする構成では`output_name`を空にし、配信時の有効画面から選択します。配信開始コマンドが完了した時点では仮想画面だけが有効です。
- `dd_configuration_option`などのSunshine側画面変更を無効にし、PseudoSleepが復元を管理します。
- セッション切断の監視に必要なInfoログを有効にします（`min_log_level = 2`）。
- 旧版が使用したIME切り替えの直接割り当てを解除します。詳細は[IME切り替えの修正](#us配列ホストとjis配列クライアントのime切り替え)を参照してください。
- UPnPを無効にし、管理UIをホスト自身からのアクセスに制限します。
- `-AllowServiceRestart`を指定したユーザーへ、Sunshineサービスの状態確認・開始・停止権限だけを追加します。変更前のACLはSunshine設定フォルダへ保存します。

配信の新規開始で仮想画面を有効にし、接続を確認できない場合は30秒で復元します。最後のクライアント切断をSunshineログで検出すると、物理画面を復元して仮想画面を無効にし、サービス再起動で古いアプリセッションも終了します。切断の検出は1秒周期で、通信断の認定にはSunshine側のタイムアウトが加わる場合があります。ログが読めなくなった場合も画面の復元を優先します。

次の接続ではDesktopを新規起動してください。物理復帰直後はSunshineの起動完了まで待ちます。ログの形式を変更する別バージョンのSunshineでは、切断の検出を再確認してください。

## 自動起動と更新

インストール先は`%LOCALAPPDATA%\Programs\PseudoSleep`です。現在ユーザーのStartupフォルダーへショートカットを作成し、ログオン時に起動します。SunshineはOS起動時にサービスとして起動します。ログオン前の画面操作は対象外です。

更新時は`Build.ps1 -Publish`の後に`Install-App.ps1 -SkipBuild`を実行します。通常状態に復帰して旧プロセスの終了を待ち、ファイルを更新します。設定はEXEの隣ではなくAppDataに保存するため、ビルド先やフォルダ名の変更でも共有します。`status`には常駐EXEと設定ファイルの場所も表示されます。画面制御アプリなので、日常利用はインストール先のショートカットに統一してください。

## 解像度の追加

Moonlightを「Native」にし、PseudoSleepの「Moonlightから要求された解像度に合わせる」を有効にします。仮想ドライバに対象モードがない場合は、通常状態に戻した後、管理者PowerShellで登録します。

```powershell
.\scripts\Set-VirtualModes.ps1 -Width 2880 -Height 1920 -RefreshRate 120
```

画面構成とXMLをバックアップし、ドライバ再起動後に画面を復元します。完了後は通常ユーザーのスタートメニューからPseudoSleepを起動してください。任意の幅・高さを指定でき、端末の機種名による分岐はありません。

## 音声設定

疑似スリープ中にMoonlightが未接続なら、既定の再生出力の音量を0にします。接続時または疑似スリープ解除時に元の音量へ戻します。初期設定では有効で、設定画面の「疑似スリープ中、Moonlight未接続時は音量を0にする」、または`config.json`の`silenceAudioWhenDisconnected`で切り替えます。既存の設定ファイルに項目がない場合も有効です。

Windowsのコンソール／マルチメディア用の既定出力を対象に、[Core Audioのマスター音量API](https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmastervolumelevelscalar)を使用します。ミュートフラグ、アプリごとの音量、固定指定された別の出力先は変更しません。Sunshineの接続ログを1秒ごとに確認し、複数クライアントがある場合は最後の切断を基準にします。ログ欠落などで接続状態が不明な場合は、音量を復元します。

元の音量は出力先IDとともに`%LOCALAPPDATA%\PseudoSleep\audio-state.json`へ変更前に保存します。出力先の変更にも対応し、接続時・復帰時・アプリ終了時に各出力の音量を復元します。異常終了時は独立した復旧プロセス、次回起動時は常駐アプリが復元を試みます。一時的に外された機器など、復元できない出力の情報は消さず、常駐中に再試行します。`PseudoSleep.exe audio-status`で既定出力の現在の音量（0～1）、接続判定、復元待ちの有無を読み取れます。

仮想音声出力をSunshineの`virtual_sink`へ設定し、Moonlightの「ホストPCで音声を再生」をオフにします。オンに戻すとホストでも音声を再生します。切り替えは再接続時に反映されます。固定出力を使うアプリでは、Windowsの既定出力を利用する設定も必要です。

既にSteam Streaming Speakersがインストールされている場合、次の補助スクリプトで出力を有効にしてIDを取得できます。新しい音声ドライバは導入しません。該当する既存デバイスがない場合は停止します。

```powershell
.\scripts\Enable-VirtualAudio.ps1
# 表示されたIDを使い、同じアカウントの管理者PowerShellで実行
.\scripts\Configure-Sunshine.ps1 -AllowServiceRestart -VirtualAudioSink '表示された出力ID'
```

この補助処理はWindowsの非公開PolicyConfigインターフェースを使用するため、Windowsの変更による影響を受ける可能性があります。Windowsのサウンド設定とSunshine管理画面から有効な仮想出力を手動設定する方法もあります。音声ID・診断ファイルは公開しません。

## US配列ホストとJIS配列クライアントのIME切り替え

Caps Lock／英数単押しには、新しい[操作端末用IME補助の導入手順](moonlight-ime-client.md)を使用します。クライアントで物理的な押下・解放を扱い、ホストへ1回の信号として送る方式です。以下は旧版の解除と、接続試験までの暫定対処です。

以前の`Set-RemoteKeyboard.ps1 -Mode JisIme`による補正は撤回しました。「半角／全角」またはCaps LockをSunshineの`keybindings`で`0xF4`（IME切り替え）へ直接置き換えると、キー解放の通知が届かない場合にIMEが連続して切り替わります。`Set-RemoteKeyboard.ps1`と、この割り当てを有効化する処理は削除しています。

旧版で補正を有効にした場合は、配信を終了してホストを直接操作できる状態に戻し、同じアカウントの管理者PowerShellで次を実行します。EXEを更新するだけではSunshineの既存設定は直りません。

```powershell
.\scripts\Reset-RemoteKeyboard.ps1
```

`0xC0 → 0xF4`と`0x14 → 0xF4`に一致する割り当てだけを、元のキー自身への割り当てに戻します。設定を変更する場合はバックアップを作成します。コメント、他のキーへの独自割り当て、画面・音声設定、通常キーのリピート設定は保持します。変更の有無にかかわらずSunshineを再起動し、残っている押しっぱなし状態を解除します。再接続して連続切り替えが止まったことを確認してください。

`Configure-Sunshine.ps1`を再実行する場合も、同じ2つの割り当てを解除してから通常の連携設定を行います。どの割り当てを旧版が書いたかという履歴は保存していないため、手動で設定した同じ2組も解除対象になります。それ以外の独自割り当ては維持します。

### 補正解除後も記号が連続入力される場合

直接置換を解除しても、解放の通知が欠落する問題自体は残ります。その場合はIME切り替えの代わりに、バッククォートなど元のキーが繰り返し入力されます。配信を終了し、同じアカウントの管理者PowerShellで次を実行すると、Sunshineが生成するリピートを暫定的に止められます。

```powershell
.\scripts\Set-RemoteKeyRepeat.ps1 -Mode Disabled
```

設定のバックアップを作成し、`key_repeat_delay = 0`にしてSunshineを再起動します。これは**リモートの全キー**に影響し、文字・矢印・Backspaceを長押ししても連続入力されなくなります。物理HHKBのリピート設定は変わりません。届かなかった押下・解放を修復する処理ではないため、キーの再入力が届かない問題やCaps Lock単押しのIME切り替えは解決しません。変更後に再接続して確認します。

クライアント側の押下・解放を修正した後で、通常のリピートを戻す場合は次を実行します。標準の開始遅延は500msです。以前から独自の遅延を使っていた場合は、バックアップの`key_repeat_delay`を`-DelayMilliseconds`に指定してください。解放欠落が残る状態で有効化すると、連続入力が再発します。

```powershell
.\scripts\Set-RemoteKeyRepeat.ps1 -Mode Enabled -DelayMilliseconds 500
```

### 原因と現在の制限

[対象版Sunshineの入力処理](https://github.com/LizardByte/Sunshine/blob/v2026.914.233613/src/input.cpp)は、解放を受信していないキーの押下を繰り返し生成します。既定値は押下から500ms後に開始し、毎秒24.9回です。IME切り替えキーも同じ処理を通るため、キー置換だけでは連続切り替えを防げません。通常の導入・連携設定ではリピート設定を維持し、上記の専用スクリプトで明示的に変更します。

JISの「英数」では、通常のCaps Lockと押下・解放通知が異なる場合があります。[Mozcの処理](https://github.com/google/mozc/blob/master/src/gui/config_dialog/keybinding_editor.cc)にもこの扱いへの対応があります。実機診断では単押し3回に対してホストが受信した押下は1回で、解放も欠落していました。ホスト側だけでは、届かなかった次の押下を復元できません。

新しいIME補助はクライアントのRaw Inputを使用し、1回の押下・解放を1回の信号へ変換します。押しっぱなし、解放欠落、再接続、フォーカス移動の状態テストは成功しました。Moonlight越しの実機受け入れは未完了です。IMEキーへの直接置換は再導入せず、物理HHKBのキーマップやホストのUS配列も変更しません。

以前用意した`Set-MoonlightClientKeyboard.ps1`は、操作端末へUS入力を追加する任意の補助です。IME補正を実装するものではなく、この不具合の修正に実行する必要はありません。既に実行した場合の変更前設定は`%LOCALAPPDATA%\PseudoSleepClient\KeyboardBackups`にあります。戻す場合は日本語入力を選び直し、今回追加したUS入力だけを削除し、アプリごとの入力設定をバックアップの`LanguageBar.IsLegacySwitchingMode`に合わせて戻してください。

## 仮想ディスプレイの手動起動・再起動

トレイの「設定・復帰デバイス」の仮想ディスプレイ欄に、次の操作があります。保存済みのドライバー・画面・解像度を使用します。

| ボタン | 動作 |
| --- | --- |
| 起動 | 登録済みのVDDを有効にし、物理画面の配置を保持して仮想画面を追加します。 |
| 再起動 | 物理画面へ復元してVDDを停止した後、再度起動して仮想画面を確認します。 |
| 停止して通常に戻す | 保存した物理画面の配置へ復元し、VDDを停止します。 |

疑似スリープを解除し、Moonlightを切断してから操作してください。手動で起動した後は`VirtualDisplayReady`状態となり、通常時の監視による自動停止を抑止します。そのまま疑似スリープに入ることもできます。停止・復帰・アプリ終了時には元の物理画面構成へ戻します。手動起動だけでは音量を0にしません。OSやアプリの通常起動時に自動でVDDを有効にする設定は保存しません。

補助タスクが未登録または旧方式の場合は、EXEに含めた定義で修復します。同じWindowsアカウントで管理者確認を承認してください。確認中は物理画面を保持し、キャンセル時は画面を変更せず終了します。修復後のSYSTEMタスクには自動起動トリガーがなく、対象VDDだけを操作します。PowerShellのソースファイルをEXEと一緒に配布する必要はありません。

VDDドライバー自体が削除されている場合は、その旨を表示します。このボタンはドライバーのダウンロードや再インストールを行わないため、ドライバーを導入して対象を再登録してください。

## 任意の常設仮想画面

通常時にも仮想画面を残したい場合だけ、設定JSONの`keepVirtualDisplayInNormalMode`を`true`にします。仮想画面が有効な状態で`Initialize-Config.ps1`によりSunshineの画面GUIDを取得し、`Configure-Sunshine.ps1`を再実行してください。この場合は固定GUIDを配信先にします。

通常時に仮想画面を無効にする既定の構成は、接続時の物理画面消灯と復帰時の切断を必須とします。古いセッションの再開によって物理画面を配信しないためです。物理画面を点灯したまま配信したい場合は、常設仮想画面へ切り替えてから「Moonlight接続時に物理画面を消灯する」を変更します。配置モードの変更は配信を終了し通常状態で行ってください。

## 復旧と連携解除

```powershell
.\scripts\Restore-Displays.ps1
```

または`Ctrl + Alt + Shift + F12`で復帰します。復元失敗時は`state.json`とバックアップを保全します。保存対象モニターを外している場合は再接続してください。OSやGPUが停止している場合や、両プロセスを終了した場合は即時の自動復旧ができません。

PseudoSleepを停止して通常のSunshine運用へ戻す場合は、先に配信を終了し、管理者PowerShellで開始フックを解除します。

新しいIME補助を有効化している場合は、先に `scripts/Set-RemoteImeBridge.ps1 -Mode Disabled` を実行してキー割り当てを戻してください。補助が有効なままでは連携解除・アンインストールを停止します。

```powershell
.\scripts\Remove-SunshineIntegration.ps1
```

この処理はPseudoSleepのフックだけを除去し、他のコマンド・音声設定・既存設定を維持してSunshineを再起動します。常設仮想画面の構成だった場合は、Sunshine管理画面で配信先を物理画面または自動選択へ変更してください。

通常ユーザーのPowerShellで停止・自動起動解除を実行します。

```powershell
.\scripts\Uninstall-App.ps1
```

フックが残っている場合は解除を促して停止します。設定・ログ・バックアップ・EXE・Sunshine・VDDは削除しません。付与したサービス開始・停止権限も自動削除しません。権限まで元に戻す場合は、保存したSDDLとその後の権限変更を確認したうえで管理者が復元してください。
