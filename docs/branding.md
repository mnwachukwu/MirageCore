# Icons and shipping your own client

Everything a fork changes to make the engine look like its own game — without editing a
project file, and in one case without a compiler at all.

## Icons

One mark — a lit tile on a dark grid — with a badge in the corner saying which application it is:

| | Badge | Executable **and** installer |
|---|---|---|
| Client | *(none — the plain mark is the game)* | `assets/icons/client.ico` |
| Editor | a pencil | `assets/icons/editor.ico` |
| Server | an open folder | `assets/icons/server.ico` |

Each app's installer wears that app's own icon. There is no separate installer mark on purpose: one
shared badge made all three `Setup.exe` files look identical in a downloads folder, which is the one
place the distinction actually earns its keep.

All three are **drawn** rather than rasterized from an SVG — the mark is nine rounded rectangles and
the badges are a few polygons, so the geometry is a page of numbers in a generator. That generator is
published, under `ArtGenerators/` in the [tools repository](https://github.com/mnwachukwu/MirageSourceRemastered.Tools.Public);
the icons it produces are committed here, in every format and size, so nothing about building or
rebranding this repository depends on it.

**Rebranding does not need that script.** Every route below replaces a committed file, and three of
the four take a repack rather than a compiler. Draw or export your own at the listed sizes and drop
them in.

#### Four icons, not three

There are more places an icon appears than there are applications, and each is reached differently:

| What | Where it comes from | Format | Changing it needs |
|---|---|---|---|
| Executable icon — Explorer, taskbar, Alt-Tab | `<ApplicationIcon>` | `.ico` | a rebuild, or `rcedit` |
| Installer and its shortcuts | `vpk --icon` in `*.Publish.csproj` | `.ico` | a repack — no compiler |
| macOS app bundle | `Contents/Resources/AppIcon.icns` | `.icns` | a repack |
| **The client's game window and taskbar button** | an embedded `Icon.bmp` | **`.bmp`** | a rebuild |

That last row is the one that surprises people. MonoGame does **not** read the executable's icon for
the window it opens. `SdlGameWindow` looks for an embedded resource in the entry assembly and, if it
finds none, falls back to `MonoGame.bmp` — a copy of MonoGame's own logo embedded in the framework.
Ship nothing and every window and taskbar button in your game carries MonoGame's branding, no matter
how correct the `.exe` looks in Explorer.

The resource is embedded with `LogicalName="Icon.bmp"`, which is load-bearing. MonoGame tries an
assembly-derived name first and a bare `Icon.bmp` second, and *the derived one has already changed*:
3.8.1 used `<EntryNamespace>.Icon.bmp`, 3.8.4 uses `<AssemblyName>.Icon.bmp`. Since `AssemblyName` is
`$(GameNameSlug)`, anything keyed on it would also break the moment a fork renamed the game. The bare
fallback is the one name stable across both the version change and every rename.

It has to be a real BMP, and specifically a **BITMAPV4HEADER** one: the framework loads it through
`SDL_LoadBMP`, and a plain 32-bit `BITMAPINFOHEADER` leaves the alpha channel formally undefined, so
SDL is entitled to read it as padding — which turns the mark's rounded corners black. The generator
writes the V4 header by hand for exactly this reason.

#### Rebranding a fork

**Every icon path lives in [`Directory.Build.props`](../Directory.Build.props), beside `GameName`**, so
branding is one file rather than four csproj files:

```xml
<ClientIcon>$(RepoRoot)assets\icons\client.ico</ClientIcon>
<EditorIcon>$(RepoRoot)assets\icons\editor.ico</EditorIcon>
<ServerIcon>$(RepoRoot)assets\icons\server.ico</ServerIcon>
<ClientWindowIcon>$(RepoRoot)client\src\Mirage.Client.Shell\Icon.bmp</ClientWindowIcon>
```

Three ways to use that, in increasing order of effort:

1. **Replace the files in place.** Keep the paths, overwrite the art. Nothing else to change.
2. **Repoint the properties** at wherever you keep your own artwork — outside the tree is fine.
3. **Override per build**, without editing anything:
   `dotnet build Mirage.slnx -p:ClientIcon=C:\art\mygame.ico`

Each is declared `Condition="'$(X)' == ''"`, which is what makes the command-line form win.

#### What still needs a rebuild, and why

Two icons are compiled in, so a fork repackaging a **released** client rather than building from
source cannot reach them with `tools/pack-client.ps1` alone:

- the executable icon — covered by passing `-RcEdit`, which rewrites the PE resource in place;
- the window icon — **not currently covered.** There is no released-artifact equivalent.

Closing that would mean the client looking for an icon file beside its executable at startup and
calling `SDL_SetWindowIcon` itself, rather than relying on the embedded resource. That is a real
option and not much code, but it needs `DllImport` against SDL2 with a custom resolver — MonoGame
ships the Linux native as `libSDL2-2.0.so.0`, which the default probing does not find — and it would
be shipped tested on Windows only. It has been left undone deliberately rather than overlooked; if
distributing pre-built rebranded clients becomes a real workflow, that is the thing to build.

The site's favicon and its Open Graph card carry the same mark and the same accent, both by hand — but
deliberately not the same ground. These icons sit on the desktop applications' violet neutrals so that
an icon and the window it opens agree; the site's mark sits on the site's own background. The accent is
the shared part, and changing *that* means changing all of them.

## Shipping your own client

Running a world and want players to install *your* game rather than this one? You do not need the
source. Velopack packages a folder of files and does not care where the folder came from, so a
published client — a portable zip from a release, with your own graphics, music and data swapped in —
is enough input.

```sh
powershell -File tools/pack-client.ps1 -Source ./my-client -Name "Aethermoor" -Version 1.0.0 -Icon ./aethermoor.ico
```

[`tools/pack-client.ps1`](../tools/pack-client.ps1) needs only the .NET SDK — it installs `vpk` itself
on first run, and compiles nothing. It runs under the Windows PowerShell that ships with Windows, and
under PowerShell 7 on Linux and macOS; pass `-Runtime linux-x64` for an AppImage.

> The script keeps a UTF-8 BOM, and needs it. Windows PowerShell 5.1 reads a BOM-less file as ANSI,
> which turns the em-dashes in its comments into a character it treats as a string delimiter — the
> parser then swallows the rest of the file and the script exits 0 having done nothing at all. If
> your editor strips the BOM, put it back.

It sets your name on the installer, the Start Menu and desktop shortcuts, Add/Remove Programs, and
the install folder under `%LocalAppData%`, and your icon on `Setup.exe` and the shortcuts.

> **Two things it cannot reach**, both because they are compiled in rather than packaged:
>
> - The icon *inside* the client executable — what Explorer and the taskbar read. Pass
>   `-RcEdit <path to rcedit.exe>` and the script rewrites the PE resource in place;
>   [rcedit](https://github.com/electron/rcedit/releases) is a single small executable.
> - The icon on the **game window itself**, which MonoGame reads from an embedded resource. There is
>   no repackaging equivalent — see "What still needs a rebuild, and why" above. Building from source
>   with your own `ClientWindowIcon` is currently the only route.

**macOS cannot be packaged from another OS.** The `.app` bundle is assembled by hand on the packaging
machine (see the `PublishAll_Osx` targets), and forks wanting a macOS build should also change
`CFBundleIdentifier` in each `installer/Info.plist` — it is a `com.example.*` placeholder and the one
identity string not derived from `GameName`.
