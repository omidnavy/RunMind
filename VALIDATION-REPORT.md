# RunMind 1.7.0 — Validation report

## Environment

- Date: 2026-11-04
- JavaScript runtime: Node.js 22.16.0
- Target platform: Windows 10/11, x64
- .NET SDK: unavailable in the validation environment
- WebView2 runtime: unavailable in the validation environment

## Checks performed

| Check | Result | Notes |
|---|---|---|
| Shared chat JavaScript syntax | Passed | Extracted the inline application script from `ChatWindow.html` and checked it with `node --check`. |
| JSON Schema helper tests | Passed | Valid/invalid documents, required/type/extra-property errors, local JSON Pointer `$ref`, and schema composition / conditional checks. |
| PDF text extraction helper tests | Passed | Plain text operators and a Flate-compressed content stream. This is not a representative corpus of real-world PDFs. |
| ZIP parsing and decompression helper tests | Passed | Standard ZIP directory parsing, deflate decompression, CRC-32 verification, and rejection of traversal paths, symlinks, and Windows-reserved paths. |
| CSV helper tests | Passed | Integer/string inference and a full join with unmatched rows. |
| Plugin manifest JSON | Passed | Parsed successfully. |
| Project XML | Passed | Both `.csproj` files parsed successfully. |
| WPF XAML XML | Passed | Both XAML files parsed successfully. |
| Version metadata | Passed | Standalone project, PowerToys project, plugin manifest, and MCP client metadata identify as `1.7.0`. |
| Source archive integrity | Passed | ZIP integrity checked after packaging. |

The helper tests invoke the current helper functions extracted from `ChatWindow.html` with lightweight filesystem/provider mocks. They do **not** simulate the complete WebView2 UI, native approval cards, browser File System Access API behavior, or real network requests.

## Not verified in this environment

- A complete Windows/.NET 10 Release build of `RunMind.sln`.
- Live startup and window behavior in both WPF/WebView2 hosts.
- PowerToys Run deployment and plugin lifecycle behavior.
- Real Ollama / LM Studio / OpenAI-compatible provider diagnostics.
- PDF compatibility across a broad real-world corpus, especially scanned, encrypted, object-stream, and custom-font documents.
- End-to-end ZIP extraction through the browser permission and approval UI.
- Browser worker timeout behavior in an actual WebView2 runtime.

## Known feature boundaries

- JSON Schema validation is deliberately bounded. It supports common validation keywords and local JSON Pointer references; unsupported advanced keywords are returned as warnings rather than silently treated as validated.
- PDF extraction reads selectable text from supported PDF streams. It does not render pages or perform OCR.
- Archive tools support standard ZIP, not ZIP64, multi-disk ZIP, TAR, or other archive formats.
- Connection diagnosis tests model discovery, not generation or tool-call capability. Some browser networking and CORS failures cannot be distinguished conclusively.
- CSV type inference is heuristic and bounded; dataset joins have a 100,000-row output limit.

## Release packaging note

The uploaded source tree was intentionally trimmed before this work. It does not contain the `lib\x64` PowerToys reference assemblies, `Assets\Brand` files, README, or screenshots removed from the upload. The **trimmed source ZIP** preserves that tree as supplied; the **overlay ZIP** contains only files changed or added for 1.7.0 and is intended to be extracted over the user's original, complete 1.6.13 source tree.
