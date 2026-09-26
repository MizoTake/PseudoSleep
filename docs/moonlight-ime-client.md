# Moonlightの英数・半角全角補助

JIS配列のWindows操作端末でCaps Lock／英数または半角／全角を押して離すと、US配列ホストの日本語IMEを1回切り替える補助です。配列全体やHHKBの設定を変更しません。クライアントでRaw Inputの物理的な押下と解放を確認し、Moonlight経由でF13の短い押下・解放を送ります。ホストではそれを日本語入力のオン・オフに変換します。英数による変換モード変更の全動作を再現するものではなく、入力済み文字のF9/F10変換も行いません。

**操作端末での実機検証は未完了です。** 自動テストとWindows上の補助EXEの起動確認とは別に、後述の接続試験が必要です。

## v2：押下だけ増えて解放が0になる場合

v1では「英数／半角全角の押下は増える、解放は0、送信も0」という実機報告がありました。日本語106配列はこれらをNLS特殊キーとして扱うため、通常の押下・解放を前提にした判定だけでは送信できない場合があります。v2はMoonlightの有効なキー割り当てを確認し、通常のCaps Lock／バッククォートに対応しない配列なら、追加済みの英語（米国）US入力をMoonlightに要求します。配列変更が実際に反映されるまで送信しません。PCの機種名や言語名だけで判定しません。

初回はZIPを展開し、旧補助を通知領域の「終了」で閉じてから **`Start-MoonlightImeClient.cmd`** を実行してください。既存の日本語入力と言語順を残してUS入力を追加し、入力をアプリごとに保持する設定にした後、v2を起動します。設定前のバックアップは `%LOCALAPPDATA%\PseudoSleepClient\KeyboardBackups` に保存されます。管理者権限は不要です。2回目以降はEXEから起動できます。

Moonlightで英語入力が選ばれても、リモート先の日本語IMEは別です。補助の半角／全角で切り替える対象はメインPCです。Moonlightで選択された配列は補助終了後も保持されます。自動選択を止める場合は補助のチェックを外し、MoonlightでWin＋Spaceから入力言語を選び直してください。ほかのアプリで使う日本語入力は保持します。

## 導入

1. メインPCで最新のPseudoSleepをビルド・導入し、Moonlightを切断して物理画面を復帰します。
2. メインPCの管理者PowerShellで `scripts/Set-RemoteImeBridge.ps1 -Mode Enabled` を実行します。同じログオンユーザーで実行してください。設定をバックアップし、Sunshineを再起動します。既存のリピート設定は維持します。
3. `scripts/Build-MoonlightImeClient.ps1` を実行して生成した `artifacts/moonlight-ime-client` フォルダーを、Moonlightを使う操作端末にコピーします。`Build.ps1 -Publish` でも生成されます。
4. 操作端末で初回は `Start-MoonlightImeClient.cmd` を起動します。既に旧補助が動いていれば先に終了します。Moonlightと補助は通常ユーザーで実行します。Windowsの.NET Framework 4を使用するため、別途.NET 8やAutoHotkeyは必要ありません。管理者権限・追加ドライバ・新しい通信ポートは不要です。
5. MoonlightでDesktopを新規起動します。補助は配信画面が前面にある間だけ送信します。Caps Lock／英数を押して離し、メモ帳などで日本語入力のオン・オフを確認します。

「通知領域にしまう」またはウィンドウを閉じると常駐します。終了は通知領域または画面の「終了」です。自動起動は設定しません。必要なら操作端末の `shell:startup` にEXEへのショートカットを置けます。

## 動作と制限

- 切り替えはキーを**離した時**です。長押し中に繰り返し切り替えません。解放が確認できなければ送信しません。時限解除やキー押下の自動再送は行いません。
- Shift／Ctrl／Alt／Winとの組み合わせは切り替え対象外です。長押し中の画面変更、設定変更、機器取り外しでも予約した切り替えを破棄します。
- 配列変更時も予約した切り替えを破棄します。配列変更前から押していたキーは一度離してから押し直してください。配列変更の失敗を成功扱いせず、解放をタイマーで補完しません。
- ホスト側では元のCapsと半角全角をF14／F15へ置換し、配信中だけ消費します。IMEへ直接置換しないため、元の解放通知が欠けてもIMEの連続切り替えやバッククォート入力を防ぐ設計です。
- 有効化中は**すべてのMoonlightクライアントでCaps、半角全角、F13～F15を補助用に予約**します。補助を終了したクライアント、対象キーをオフにした場合、修飾キー付きのCaps／半角全角はホストへ通常入力として通りません。通常の動作へ戻すにはメインPC側も解除してください。
- 物理HHKBのキーは注入イベントではないため変換しません。文字・矢印・Backspaceなど他のキーの処理は変更しません。
- ホスト側の管理者アプリやUAC画面へのIME注入は対象外です。別ユーザーのデスクトップにも対応しません。
- 検出対象はプロセス名 `Moonlight`、配信ウィンドウクラス `SDL_app` です。独自ビルドで異なる場合は補助を終了し、設定ファイルの `ProcessName` / `WindowClass` を実際の値へ変更します。通常は変更不要です。
- クライアントの設定は `%LOCALAPPDATA%\PseudoSleep\MoonlightImeClient.xml` に保存します。キーの文章・機器識別子はファイルへ記録しません。診断は英数／半角全角の押下・解放・送信回数、直近の対象キーのコード、最後に検出したMoonlightのウィンドウクラスと配列状態を示します。診断画面に戻った後も配信中の配列状態を確認できます。送信回数はホストの受信・IME反映を保証するものではありません。

## 接続試験

1. Caps／英数を1秒間隔で10回押し、1回ずつ切り替わることを確認します。半角／全角も試します。
2. 両方をそれぞれ3秒以上長押しし、離した時だけ1回切り替わることを確認します。
3. 押したまま別ウィンドウへ移動して離し、意図しない切り替えが起きないことを確認します。
4. Moonlightを切断・再接続して再試行します。物理HHKBでの操作、文字の確定前・変換候補表示中も確認します。
5. 以前 `key_repeat_delay = 0` にしていた場合は、上記の確認後にメインPCで `scripts/Set-RemoteKeyRepeat.ps1 -Mode Enabled` を管理者実行し、文字・矢印・Backspaceの長押しを確認します。再発時は同じスクリプトの `-Mode Disabled` へ戻します。

反応しない場合は、補助の「診断情報をコピー」で英数の押下と解放が増えるか確認します。v2の `Last stream layout` が `MissingUsInput` なら初回起動ファイルで準備し、`Rejected` ならMoonlightで手動で英語（US）を選びます。`Ready` でも解放が0なら診断情報を保存してください。ホストの `PseudoSleep.exe status` には `remoteImeBridgeEnabled`、配信中の `remoteImeBridgeActive`、`remoteImeToggleCount` が出ます。クライアントだけ送信回数が増える場合と、ホストで切り替え回数が増えてもIMEが変わらない場合を区別できます。

## 解除

Moonlightを切断して画面を復帰後、メインPCの管理者PowerShellで `scripts/Set-RemoteImeBridge.ps1 -Mode Disabled` を実行し、操作端末の補助を終了します。作成したStartupショートカットがあれば削除します。補助が設定したマッピングだけを戻し、他のカスタムマッピングとリピート設定は維持します。

## 設計の根拠

Windowsの[Raw InputのMake/Break情報](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawkeyboard)を使います。ただし、[Microsoftの106配列実装](https://github.com/microsoft/Windows-driver-samples/blob/main/input/layout/fe_kbds/jpn/106/kbd106.c)では半角全角と英数はNLS特殊キーです。Raw Inputなら必ず物理解放が届くとは仮定せず、`MapVirtualKeyEx` で有効な割り当てを調べ、必要に応じて [WM_INPUTLANGCHANGEREQUEST](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-inputlangchangerequest) を前面のMoonlightだけに送ります。要求の送信成功と配列の反映は分けて確認します。Raw Inputのスキャンコードは公式サンプルに従ってブレーク側の上位ビットを正規化し、解放の有無はFlagsで判断します。

[SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)で中継キーの押下と解放を1回の配列送信にまとめます。クライアントでは元のイベントを抑止するキーフックを置かず、Raw Inputまで抑止される経路を避けます。ネットワークは既存のMoonlight入力経路だけを使用します。

ホスト側はF13のDOWNだけでは切り替えず、その後のUPで1回動作し、自己注入イベントを除外します。配信終了時に状態をリセットします。物理キーの欠落・接続断・Win32 APIの失敗を含む実機上の挙動は、上記の試験で確認する必要があります。
