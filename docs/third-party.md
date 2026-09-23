# 外部ソフトウェア

このリポジトリのコードと文書は[MITライセンス](../LICENSE)です。外部製品に本リポジトリのライセンスは適用されません。インストーラー、ドライバ、外部製品のソース、診断ログはGitへ含めません。

| 製品 | 用途 | 入手先・ライセンス確認先 |
| --- | --- | --- |
| .NET | ビルド・Windows Desktop Runtime | [dotnet](https://dotnet.microsoft.com/download/dotnet/8.0)、[runtimeのライセンス](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) |
| Sunshine | 配信とクライアント認証 | [公式リポジトリ](https://github.com/LizardByte/Sunshine)、[ライセンス](https://github.com/LizardByte/Sunshine/blob/master/LICENSE) |
| Moonlight | 接続する端末のクライアント | [公式サイト](https://moonlight-stream.org/)、[Qtクライアント](https://github.com/moonlight-stream/moonlight-qt) |
| Virtual Display Driver | Windowsの仮想画面 | [公式リポジトリ](https://github.com/VirtualDrivers/Virtual-Display-Driver) |
| Steam Streaming Speakersなど | 任意の仮想音声出力 | 各ドライバの提供元。音声ドライバは自動ダウンロード・再配布しません |

`Download-Host.ps1`は検証対象の固定バージョンを公式GitHub Releasesから取得し、SHA256を検証します。取得物と展開物は無視対象の`artifacts/downloads/`へ保存します。最新版を自動追従する仕組みではありません。バージョン更新時はURL・ハッシュ・署名・設定形式・実機動作を確認してください。

対象バージョンはSunshine `2026.914.233613`とVirtual Display Driver `25.7.23`です。再配布する場合は、各配布物のライセンスと同梱条件を別途確認してください。
