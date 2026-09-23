# 開発・検証

Windows 11 x64と.NET SDK 8以降を使用します。アプリの実行には.NET 8 Desktop Runtimeが必要です。追加のNuGet依存はありません。

```powershell
.\scripts\Build.ps1 -Publish
```

コアのテストはテストフレームワークを使用しないコンソールプログラムです。`dotnet test`だけでは実行されません。`Build.ps1`を共通の検証入口にしてください。GitHub Actionsも同じコマンドを使用し、ホストソフトウェアの導入や画面の切り替えは行いません。

画面の切り替えを含む実機試験は、[導入手順](docs/setup.md)を完了したローカルPCでのみ実行します。通常のビルドやCIからは呼びません。

```powershell
.\scripts\Test-Host.ps1
.\scripts\Test-Host.ps1 -CrashRecovery
```

これらの実機試験は物理画面を一時的に無効にし、後者は常駐プロセスを強制終了します。手元の復帰用入力機器、ホットキー、CLIとバックアップを準備してください。Moonlightの映像・音声・物理入力の区別、OS再起動、異なるGPUの動作は個別の受け入れ確認が必要です。

コードはUTF-8を使用し、既存のBOM・改行・インデントを維持します。動作を変える修正には、可能な範囲で失敗を再現する回帰テストを添えてください。設定・ログ・生の端末ID・認証情報・ダウンロード済みバイナリはコミットしません。ログをIssueに添付する場合は内容を確認して匿名化してください。

IMEクライアントはWindows付属.NET Framework 4のC#コンパイラーでビルドします。`src/KeyboardBridge.Shared` は.NET 8のホストと共用するためC# 5で記述します。`Build.ps1` は状態テストとEXEのコンパイルを実行します。ネイティブフックの検証は対話デスクトップ上で明示的に実行します。

```powershell
dotnet run --project tests/PseudoSleep.NativeTests -c Release
.\artifacts\moonlight-ime-client\MoonlightImeClient.exe --smoke "$PWD\artifacts\ime-client-smoke.txt"
```

ネイティブテストはF13～F15をWindowsへ注入するため、他のキーカスタマイズツールが動作していない状態で実行します。IME操作は記録用スタブで、文字やIME切り替えは送信しません。補助のsmokeはRaw Input／前面ウィンドウ通知の登録とUI描画を確認します。どちらもMoonlight越しの実機確認の代わりにはなりません。
