# Third-party notices

LiveCue for Windows (`LiveCueRemake.exe` and `LiveCueRelayHost.exe`) is published together with the
software below. LiveCue itself is under the MIT License; see `LICENSE` in the same folder.

Each component is used unchanged, as its authors publish it.

| Component | Used for | Author | Licence |
|---|---|---|---|
| .NET 10 runtime, ASP.NET Core and the Windows Desktop runtime (WPF) | The runtime both executables carry with them, the relay's HTTP server and the window | .NET Foundation and Contributors; Microsoft Corporation | MIT |
| `System.Security.Cryptography.ProtectedData` | Windows DPAPI, which protects the stored copy of the pairing token | Microsoft Corporation | MIT |
| `Net.Codecrete.QrCodeGenerator` | The module matrix of the pairing QR code | Manuel Bleichenbacher; based on the QR Code generator library by Project Nayuki | MIT |
| `CommunityToolkit.Mvvm` | View-model helpers of the window | .NET Foundation and Contributors | MIT |

The .NET runtime in turn contains third-party material. Its own notices are in the .NET source
repositories (`THIRD-PARTY-NOTICES.TXT` in `dotnet/runtime`, `dotnet/aspnetcore` and `dotnet/wpf`).

## The MIT License

The components above are each provided under the following terms, with their own copyright line:

- Copyright (c) .NET Foundation and Contributors
- Copyright (c) Microsoft Corporation
- Copyright (c) Manuel Bleichenbacher
- Copyright (c) Project Nayuki

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR
THE USE OR OTHER DEALINGS IN THE SOFTWARE.
