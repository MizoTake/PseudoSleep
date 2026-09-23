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

仮想音声出力をSunshineの`virtual_sink`へ設定し、Moonlightの「ホストPCで音声を再生」をオフにします。オンに戻すとホストでも音声を再生します。切り替えは再接続時に反映されます。固定出力を使うアプリでは、Windowsの既定出力を利用する設定も必要です。

既にSteam Streaming Speakersがインストールされている場合、次の補助スクリプトで出力を有効にしてIDを取得できます。新しい音声ドライバは導入しません。該当する既存デバイスがない場合は停止します。

```powershell
.\scripts\Enable-VirtualAudio.ps1
# 表示されたIDを使い、同じアカウントの管理者PowerShellで実行
.\scripts\Configure-Sunshine.ps1 -AllowServiceRestart -VirtualAudioSink '表示された出力ID'
```

この補助処理はWindowsの非公開PolicyConfigインターフェースを使用するため、Windowsの変更による影響を受ける可能性があります。Windowsのサウンド設定とSunshine管理画面から有効な仮想出力を手動設定する方法もあります。音声ID・診断ファイルは公開しません。

## US配列ホストとJIS配列クライアントのIME切り替え

ホストをUS配列で使い、MoonlightのJISキーボードから「半角／全角」を押すとバッククォートが入力される場合、Sunshineで受信キーをIME切り替えへ割り当てます。通常状態へ戻してから、同じアカウントの管理者PowerShellで実行してください。設定のバックアップを作成し、Sunshineを再起動します。

```powershell
.\scripts\Set-RemoteKeyboard.ps1 -Mode JisIme
```

Sunshineの`keybindings`で`0xC0`を`0xF4`（`VK_DBE_DBCSCHAR`）へ置き換えます。Windowsのハードウェア配列と物理HHKBの入力は変更しません。他のキー割り当て・画面・音声設定を保持します。対象はSunshineへ接続する全クライアント共通のため、US配列のリモート端末を使う場合も同じ位置のキーがIME切り替えになります。クライアントごとの自動判定は行いません。

再接続後、メモ帳などで「半角／全角」を押し、英数字→日本語→英数字と両方向に切り替わることを確認してください。IMEやMoonlightのバージョンによって扱いが異なるため、実際のクライアントで確認します。元のバッククォート入力へ戻す場合は次を実行します。`Standard`はこのキーを`0xC0`自身へ割り当て、他の設定を維持します。以前の独自割り当てまで戻す場合はバックアップの該当箇所を確認してください。

```powershell
.\scripts\Set-RemoteKeyboard.ps1 -Mode Standard
```

Caps LockでもIMEを切り替えたい場合は、次を実行します。「半角／全角」の設定を保持して追加します。この補正は、クライアントからキーの押下・解放が毎回届くことが前提です。

```powershell
.\scripts\Set-RemoteKeyboard.ps1 -Mode JisIme -Key CapsLock
# Caps Lockだけを元の動作へ戻す場合
.\scripts\Set-RemoteKeyboard.ps1 -Mode Standard -Key CapsLock
```

`-Key`の既定値は`HalfWidthFullWidth`で、指定したキーだけを変更します。Caps Lockを補正中は、リモート側のそのキーをIME切り替えに使います。

### JISのCaps Lock／英数が単押しで反応しない場合

JISの同じ物理キーでも、Windowsの「英数」と通常のCaps Lockは入力イベントの扱いが異なります。[Mozcの入力処理](https://github.com/google/mozc/blob/master/src/gui/config_dialog/keybinding_editor.cc)にも、英数のキー解放が通常のキーと同じタイミングで通知されない場合への対応があります。Moonlight側で押しっぱなしと認識されると、2回目以降の押下がホストへ届かず、Sunshineのキー置換だけでは補えません。

まず配信を終了し、操作端末のMoonlight接続一覧画面で入力方式を`Win + Space`から「英語（米国）／US」へ切り替え、再接続してCaps Lockの単押しが毎回届くか確認します。配信中のショートカットはホストへ転送される場合があります。ホスト側は日本語IMEを使い続けます。この方法の効果はクライアントでの実機確認が必要です。JIS独自の変換・無変換・かなキーなども確認してください。元の入力方式へ戻す操作も`Win + Space`です。

Windowsの「アプリ ウィンドウごとに異なる入力方式を設定する」を有効にすると、Moonlightで使う入力方式とほかのアプリで使う入力方式を分けられます。英語／USが一覧にない場合は、操作端末で[Microsoftのキーボード追加手順](https://support.microsoft.com/ja-jp/windows/hardware/input-devices/manage-the-language-and-keyboard-input-layout-settings-in-windows)を使って追加します。ホストのキーボードドライバやハードウェア配列の変更は行いません。

補助スクリプトを使う場合は、`scripts/Set-MoonlightClientKeyboard.ps1`を**Moonlightを使う操作端末**へコピーし、Windows PowerShell 5.1から実行します。ホストでは実行しません。管理者権限や追加ソフトは不要です。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Set-MoonlightClientKeyboard.ps1
```

既存の言語の順序、日本語IME、ほかの入力方式を保持して英語／USを追加し、入力方式をアプリウィンドウごとに選ぶ設定へ変更します。変更前の一覧と入力切り替え設定は`%LOCALAPPDATA%\PseudoSleepClient\KeyboardBackups`へ保存します。スクリプトの完了後、Moonlightの接続一覧画面で`Win + Space`から英語／USを選択して再接続してください。ほかのアプリでは日本語を選びます。

元へ戻す場合は、日本語入力を選び直し、Windowsの言語オプションで今回追加したUS入力を削除します。アプリごとの入力設定はバックアップの`LanguageBar.IsLegacySwitchingMode`を確認して元へ戻してください。以前から存在した英語やほかの配列は削除しません。

この設定は[Sunshineのキー置換機能](https://github.com/LizardByte/Sunshine/blob/v2026.914.233613/docs/configuration.md#keybindings)を利用します。対応版の[入力変換テーブル](https://github.com/LizardByte/libvirtualhid/blob/53e1a949fc0784af716b782ddfa6c647cafd1f05/src/platform/windows/keylayout.hpp)では`0xF3`に別のスキャンコードが割り当てられるため、`0xF4`を使用します。

## 任意の常設仮想画面

通常時にも仮想画面を残したい場合だけ、設定JSONの`keepVirtualDisplayInNormalMode`を`true`にします。仮想画面が有効な状態で`Initialize-Config.ps1`によりSunshineの画面GUIDを取得し、`Configure-Sunshine.ps1`を再実行してください。この場合は固定GUIDを配信先にします。

通常時に仮想画面を無効にする既定の構成は、接続時の物理画面消灯と復帰時の切断を必須とします。古いセッションの再開によって物理画面を配信しないためです。物理画面を点灯したまま配信したい場合は、常設仮想画面へ切り替えてから「Moonlight接続時に物理画面を消灯する」を変更します。配置モードの変更は配信を終了し通常状態で行ってください。

## 復旧と連携解除

```powershell
.\scripts\Restore-Displays.ps1
```

または`Ctrl + Alt + Shift + F12`で復帰します。復元失敗時は`state.json`とバックアップを保全します。保存対象モニターを外している場合は再接続してください。OSやGPUが停止している場合や、両プロセスを終了した場合は即時の自動復旧ができません。

PseudoSleepを停止して通常のSunshine運用へ戻す場合は、先に配信を終了し、管理者PowerShellで開始フックを解除します。

```powershell
.\scripts\Remove-SunshineIntegration.ps1
```

この処理はPseudoSleepのフックだけを除去し、他のコマンド・音声設定・既存設定を維持してSunshineを再起動します。常設仮想画面の構成だった場合は、Sunshine管理画面で配信先を物理画面または自動選択へ変更してください。

通常ユーザーのPowerShellで停止・自動起動解除を実行します。

```powershell
.\scripts\Uninstall-App.ps1
```

フックが残っている場合は解除を促して停止します。設定・ログ・バックアップ・EXE・Sunshine・VDDは削除しません。付与したサービス開始・停止権限も自動削除しません。権限まで元に戻す場合は、保存したSDDLとその後の権限変更を確認したうえで管理者が復元してください。
