# PseudoSleep

Windows 11でMoonlight接続時に物理画面を消灯し、仮想画面だけを配信するトレイアプリです。登録した物理マウス・キーボードで元の画面構成へ戻し、Moonlightを切断します。Windows本体とアプリは動作を続けます。

解像度への追従、復帰入力、ガード時間、音声の再生先を設定できます。物理画面を点灯したまま配信する任意構成も用意しています。.NET 8 / WinForms / Win32 APIで実装しており、追加のNuGetパッケージはありません。

## 主な動作

- MoonlightでDesktopを新規起動すると、仮想画面を確認した後に物理画面を切り離します。トレイの太陽／月アイコンでも切り替えられます。
- Moonlightの入力では復帰せず、登録した物理入力で復帰します。開始直後の誤復帰を防ぐ待ち時間は既定で3秒です。
- 元の位置、解像度、回転、リフレッシュレートを検証して復元します。独立した監視プロセスが異常終了時の復旧を試みます。
- 復帰後、Sunshineサービスを再起動して既存のMoonlightセッションを終了します。再接続はSunshineの起動完了後に可能です。
- 通常時は物理画面だけを有効にします。配信開始時に仮想画面を有効にし、最後のクライアント切断後に物理画面を復元して仮想画面を無効にします。
- PseudoSleepはユーザーのログオン時に自動起動します。SunshineはOS起動時にサービスとして起動します。ログオン前のデスクトップ操作は対象外です。

画面消灯はWindowsのロックではありません。MoonlightとメインPCは同じWindowsデスクトップと入力を共有します。

## 導入

必要なものはWindows 11 x64、.NET 8 Desktop Runtime、Sunshine、対応する仮想ディスプレイドライバ、復帰用の物理入力機器、別端末のMoonlightです。ソースからのビルドには.NET SDK 8以降を使用します。

```powershell
.\scripts\Build.ps1 -Publish
```

続いて[導入手順](docs/setup.md)に沿って、ホストソフトウェアの準備、画面・復帰デバイスの選択、アプリ導入、Sunshine連携を行ってください。仮想画面が1つの場合は初回起動時に自動選択し、既定の構成ではSunshine画面GUIDの手入力は不要です。ドライバ導入と画面切り替えがあるため、最初はホストPCを直接操作できる状態で行います。

検証済みの構成と範囲は[検証記録](docs/deployment-report.md)に記載しています。別GPU・別ドライバ・別Windows環境での動作は個別確認が必要です。

## 解像度と音声

Moonlightの解像度を「Native」にすると、新規起動時の要求サイズへ追従します。切断・復帰時に古い配信セッションを終了するため、次回もDesktopを新規起動してください。リフレッシュレートはPseudoSleep側の設定値を使用します。

仮想ドライバが要求解像度に対応している必要があります。未対応のサイズは[モード追加手順](docs/setup.md#解像度の追加)で登録してください。開始に失敗した場合、今回の接続で消灯した物理画面は復元を試みます。既に疑似スリープ中だった場合は既存状態を維持します。

音声はSunshineの仮想出力を設定したうえで、Moonlightの「ホストPCで音声を再生（Play audio on PC）」をオフにすると操作端末だけで再生できます。オンにするとホストでも再生します。変更後は再接続してください。仮想音声ドライバは本アプリに含みません。詳細は[音声設定](docs/setup.md#音声設定)を参照してください。

## 復帰とCLI

強制復帰は`Ctrl + Alt + Shift + F12`、またはスタートメニューの`PseudoSleep → Restore displays`です。

```powershell
$app = "$env:LOCALAPPDATA\Programs\PseudoSleep\PseudoSleep.exe"
& $app status | Out-String
& $app sleep --test-seconds=15 | Out-String
& $app wake --force | Out-String
& $app settings | Out-String
```

GUI形式のEXEなので、PowerShellでは`| Out-String`を付けて出力と終了を待ちます。常駐アプリへの命令は同じWindowsユーザーのセッションに限定します。常駐アプリが停止していても`wake --force`は保存済み構成から復旧を試みます。

| データ | 場所 |
| --- | --- |
| 設定 | `%APPDATA%\PseudoSleep\config.json` |
| 復旧情報・画面バックアップ | `%LOCALAPPDATA%\PseudoSleep\state.json`、`display-backup.json` |
| 日付別ログ | `%LOCALAPPDATA%\PseudoSleep\Logs` |
| ビルド成果物・導入時の保存物 | リポジトリ内の`artifacts/`（Git対象外） |

復元が完了しない場合、`state.json`は消さず[復旧・解除手順](docs/setup.md#復旧と連携解除)を確認してください。既定の構成ではMoonlight切断後も自動で復帰します。手動の疑似スリープは明示的な復帰まで維持します。アプリの常用をやめる前にSunshineの開始フックを解除してください。

## フォルダ構成

| 場所 | 内容 |
| --- | --- |
| `src/PseudoSleep.Core/` | 状態遷移、設定検証、入力ポリシー |
| `src/PseudoSleep/` | Windows UI、画面・入力・電源制御、IPC、復旧監視 |
| `tests/` | ホストの画面を変更しない回帰テスト |
| `scripts/` | ビルド、導入、設定、実機検証 |
| `docs/` | 導入手順、検証記録、脅威モデル、外部ソフトウェア |
| `.github/workflows/` | Windowsでのビルド・テスト |
| `PseudoSleep.sln` | 開発用ソリューション |

[実装仕様](PseudoSleep_Sunshine_Moonlight_Spec.md) / [開発・検証](CONTRIBUTING.md) / [セキュリティ](SECURITY.md) / [外部ソフトウェア](docs/third-party.md)

本リポジトリのコードと文書は[MITライセンス](LICENSE)です。Sunshine、Moonlight、ドライバなどの外部製品は各提供元のライセンスに従います。
