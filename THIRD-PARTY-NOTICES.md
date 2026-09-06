# Attribution

Satr is an independently maintained derivative, not a terminal engine written from scratch.

- The terminal parser and rendering lineage originate from [RtlTerminal](https://github.com/mirbehnam/RtlTerminal), commit `f42308b5b080de858ada60cb21183aff0fdaed4e` (MIT). The renderer has been ported to Avalonia; the original Windows session bridge is replaced with Porta.Pty. Its exact copyright notice is preserved in `licenses/RtlTerminal-MIT.txt`. The upstream author is behnam tajadini / behnamapps; the license names PersianTerminal contributors.
- Unicode.Bidi 0.3.18 implements the Unicode bidirectional algorithm. Its MIT license and port notices are included under `licenses/Unicode.Bidi-*`. Upstream: https://github.com/erikbra/unicode-bidi-net . This incorporates Rust unicode-bidi and Unicode data.
- .NET runtime licenses accompany standalone distributions under `licenses/dotnet-*`.
- Avalonia 12.1.2 (MIT): https://github.com/AvaloniaUI/Avalonia . This uses the free desktop framework, not Avalonia XPF.
- Porta.Pty 2.2.2 (MIT): https://github.com/tomlm/Porta.Pty . Ships native Linux PTY support and the Microsoft Windows Console ConPTY runtime (MIT).
- SkiaSharp 3.119.4, HarfBuzzSharp 8.3.1.3, Avalonia ANGLE and MicroCom.Runtime include native and transitive notices under `licenses/`.
- Satr's identity, project organization, product UI, Arabic composer, persistence/recovery, Windows modifier handling, bidirectional rendering integration, documentation and independent packaging were developed for this derivative. Some extend upstream code; the original attribution still applies.

No upstream updater, Explorer integration, branding assets, website, publishing workflow or release endpoint is used by Satr. The program does not claim affiliation with OpenAI, Anthropic or the upstream project.
