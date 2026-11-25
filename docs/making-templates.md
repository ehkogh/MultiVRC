# Making a template

A template is one JSON file that tells MultiVRC how to set up VRChat for your server. You host it
at a URL. A player pastes that URL into MultiVRC, clicks **Create instance**, and gets a working
client with nothing done by hand. MultiVRC then:

1. gets the VRChat build your mods were made for, downloading it from Steam if needed;
2. puts your mod loader, plugins and config into a separate copy of the game;
3. starts it the way your loader needs;
4. shows your server's icon, art, news and links in the launcher.

When you change the file and bump its `version`, every player's instance updates itself.

This guide builds a template step by step. The [reference](#reference) at the end lists every
field.

---

## 1. The smallest template

Make a file called `multivrc.json`:

```json
{
  "id": "my-server",
  "name": "My Server",
  "server": "My Server",
  "kind": "Modded",
  "version": "1",
  "build": { "manifestId": "1234567890123456789" }
}
```

- `id`: a short name that never changes. Letters, digits, `-` and `_` only. If a player installs
  another template with the same `id`, it replaces this one.
- `kind`: always `"Modded"` for a private server. `"Official"` templates may not install anything.
  They exist for VRChat's own branches.
- `version`: any text. Change it whenever you change the template (see [step 6](#6-ship-an-update)).
- `build`: which VRChat it runs on (next step).

This is already a valid template. It makes an instance of that build with nothing added.

## 2. Pick the build

Your mods are made for one VRChat build, so the template names it. There are two ways.

**Pin one build (recommended).** Give the Steam depot manifest of the build you developed
against:

```json
"build": { "version": "2026.4.1p1-1937", "manifestId": "1234567890123456789" }
```

VRChat is Steam app `438100`, and its game files are depot `438101`. Look up the manifest id on
SteamDB's page for depot 438101 (*Manifests* tab), or use the one Steam shows for your own install.
`version` is only a label for players.

**Follow a branch.** The instance always gets the newest build of a Steam branch:

```json
"build": { "branch": "public" }
```

Use this only if your mods keep working when VRChat updates. Otherwise every VRChat update breaks
your players' instances until you catch up. Branch names are Steam's: `public` is the release, and
`open-beta` is the open beta.

> If you give only `version`, MultiVRC can't download anything. It can only reuse a build the player
> already has with exactly that version, so always include `manifestId` or `branch`.

## 3. Install the mod loader and your plugin

`downloads` puts files into the instance's game folder. Each download has:

- `url`: where to get the file. Must be a direct download, not a page with a button.
- `path`: where it goes, relative to the game folder. For a zip, this is the folder it unpacks
  into: `""` means the game folder itself.
- `extract`: `true` to unzip it, or `false` to save it as a single file at `path`.
- `sha256`: the file's SHA-256 hash in hex. If the download doesn't match, the template isn't
  installed. Always include it. Players see "no checksum!" next to downloads without one, and the
  verified list won't take you without them.

Get a hash in PowerShell:

```powershell
(Get-FileHash .\BepInEx.zip -Algorithm SHA256).Hash
```

### With BepInEx (6, IL2CPP)

```json
"downloads": [
  { "url": "https://cdn.example.com/BepInEx-Unity.IL2CPP-win-x64.zip", "path": "", "extract": true,
    "sha256": "5F2C…" },
  { "url": "https://cdn.example.com/MyServer.Plugin.dll", "path": "BepInEx/plugins/MyServer.Plugin.dll",
    "sha256": "A91B…" }
],
"executable": "VRChat.exe"
```

### With MelonLoader

VRChat won't load MelonLoader from its own exe. The template makes a renamed copy, links its data
folder beside it, and starts that:

```json
"downloads": [
  { "url": "https://cdn.example.com/MelonLoader.x64.zip", "path": "", "extract": true, "sha256": "…" },
  { "url": "https://cdn.example.com/MyServer.Mod.dll", "path": "Mods/MyServer.Mod.dll", "sha256": "…" }
],
"copies": [
  { "from": "VRChat.exe", "to": "VRChatML.exe" },
  { "from": "VRChat_Data", "to": "VRChatML_Data", "junction": true }
],
"executable": "VRChatML.exe"
```

`copies` runs after the downloads. `junction: true` links a folder instead of copying it, so the
game's data folder isn't duplicated.

> Host your own copies of the loader zips rather than linking to a GitHub *latest* release. A pinned
> file with a `sha256` never changes under you.

## 4. Point the client at your server

`files` writes text files into the game folder. Use it for your plugin's config, so players never
type a server address:

```json
"files": [
  { "path": "BepInEx/config/MyServer.cfg",
    "content": "[Server]\nApiUrl = https://api.my-server.example/api/1/\nPhotonHost = photon.my-server.example\n" }
]
```

JSON strings need `\n` for line breaks and `\"` for quotes.

`arguments` are added to the command line every time the game starts:

```json
"arguments": ["--enable-debug-gui"]
```

### Extra play buttons

`playModes` adds other ways to start the game. They show in the menu under the arrow next to
**Play**, and in the instance's right-click menu, the same way official instances offer desktop
mode:

```json
"playModes": [
  { "label": "Play in desktop mode", "icon": "monitor", "arguments": ["--no-vr"] },
  { "label": "Play with the debug console", "icon": "terminal", "arguments": ["--enable-debug-gui"] },
  { "label": "Play without mods", "icon": "shield-off", "executable": "VRChat.exe" }
]
```

- `label`: what the menu item says.
- `icon`: optional. One of the launcher's icons: `play`, `monitor`, `terminal`, `wrench`,
  `shield`, `shield-off`, `sparkles`, `server`, `globe`, `layers`, `box`. Anything else shows a play
  arrow.
- `arguments`: added to the template's `arguments` for this start only.
- `executable`: optional. Starts this instead of the template's `executable`, relative to the game
  folder. Use it for a mode that skips your loader, like the MelonLoader example's plain
  `VRChat.exe`. It can't point outside the game folder. Official templates can't use it, because
  official instances always start through Easy Anti-Cheat.

What MultiVRC handles for you:

- Each instance has its own VRChat data folder, settings and saved login. A player's login to your
  server never mixes with their official one, and the other way round.
- Each instance has its own download cache. Set `"shareCache": true` to let all instances of *your*
  server share one, so worlds and avatars aren't downloaded twice. It's never shared with official
  VRChat or other servers.

What a template **can't** do:

- write anywhere outside the game folder: paths like `../..` are refused;
- install certificates, edit the hosts file, or change system settings.

If your server needs one of those, tell players in your `description`.

## 5. Make it look like your server

```json
"description": "A cozy server for 2022-era worlds. Bring your own avatars.",
"accent": "#e0559b",
"branding": {
  "tagline": "Worlds from the before times",
  "iconUrl": "https://cdn.my-server.example/icon.png",
  "iconBackground": "#ffffff",
  "backgroundUrl": "https://cdn.my-server.example/key-art.jpg",
  "news": { "type": "json", "url": "https://api.my-server.example/api/1/news" },
  "links": [
    { "label": "Discord", "url": "https://discord.gg/…", "kind": "discord" },
    { "label": "Website", "url": "https://my-server.example", "kind": "website" }
  ]
}
```

- `description`: shown when a player picks your server in **New instance**.
- `accent`: your colour, as `#rrggbb`.
- `iconUrl`: a square picture, at least 128×128. It's shown on the side rail, in the library and on
  your page. If it's a logo on a transparent background, set `iconBackground` to a colour to put
  behind it.
- `backgroundUrl`: the full-screen art behind your page. Leave it out and MultiVRC uses the
  picture from your newest news post.
- `news`: one of two kinds.
  - `"type": "rss"`: any blog's RSS feed.
  - `"type": "json"`: a URL that returns a list of posts:
    ```json
    [ { "title": "Halloween event", "summary": "Spooky worlds all week.",
        "imageUrl": "https://…/halloween.jpg", "url": "https://…/posts/halloween",
        "publishedAt": "2026-10-25T18:00:00Z" } ]
    ```
- `links`: buttons at the bottom of your page. `kind` picks the icon: `discord`, `x`, `youtube`,
  `github` or `website`.

## 6. Test it locally

You don't need to host it to try it:

1. In MultiVRC, open **New instance**, then **+ Add a server by its template URL**.
2. Paste a `file:` URL to your file, such as `file:///C:/Users/you/my-server/multivrc.json`, and
   click **Add**.
3. Pick your server on the left and check what it says under the name. It shows which build it
   will use or download, and lists every file it will install.
4. Click **Create instance**, then **Play**.

To try a change, add the file again. It replaces the installed template because the `id` is the
same. Instances you already made keep their old files until you right-click one and choose **Options → Reinstall its files**
or bump `version` (next step).

> The first start of a new instance takes a few minutes with either loader: they generate their
> interop assemblies then. Later starts are normal.

## 7. Publish it

Host `multivrc.json` anywhere that serves the raw file over HTTPS: your website, your API, or a
GitHub raw URL. Give players that URL. The same file is the "add to MultiVRC" link on your
Discord.

### Ship an update

Change what you need (a new plugin `url` and `sha256`, a new `manifestId`, new config), then change
`version`:

```json
"version": "2"
```

MultiVRC re-reads your URL when it starts and every 30 minutes. When `version` is different from
what an instance was set up with, it updates that instance:

- it switches to the new build if `build` changed, downloading it if needed;
- it installs `downloads`, `files` and `copies` again.

This happens automatically, unless the player turned auto-update off; then they see an **Update**
button. Nothing updates while the game is running. Only what's listed in the template is changed:
players' settings, logins and cache stay.

If you don't change `version`, edits to the file are picked up but existing instances aren't
redone.

## 8. Get verified (optional)

A server's page shows a badge in the corner:

- **Grey: unverified.** Every server starts here. Nobody has checked what the template installs.
- **Blue: verified.** People the MultiVRC project trusts have looked at it.

Verification lives with the MultiVRC project, never in your template, so a template can't vouch
for itself. To apply, fill in the form under **Get your server verified** on MultiVRC's homepage
(https://multivrc.ehko.dev/#apply) with your server's name, its template URL, how to reach you, and
what it is. The site checks the template as soon as you send it and shows what passed.

Requirements:

- the template URL is stable and served over HTTPS;
- every download has a `sha256`;
- you link the plugin's source or explain what it does.

The badge is tied to that exact URL. A template installed from anywhere else stays grey.

---

## Reference

### Template

| Field | Type | Default | |
|---|---|---|---|
| `id` | text | required | Letters, digits, `-`, `_`. Never change it. |
| `name` | text | required | Shown in the launcher. |
| `server` | text | `""` | Your server's name, used in messages ("Thanks for playing on …"). |
| `kind` | `"Modded"` / `"Official"` | `"Modded"` | Official templates can't install anything. |
| `version` | text | `""` | Change it to push an update to every instance. |
| `description` | text | `""` | Shown in **New instance**. |
| `build` | object | | See below. |
| `downloads` | list | `[]` | See below. |
| `files` | list | `[]` | `{ "path", "content" }`: text files written into the game folder. |
| `copies` | list | `[]` | `{ "from", "to", "junction" }`: copies or folder links inside the game folder. |
| `executable` | text | `"VRChat.exe"` | What to start, relative to the game folder. |
| `arguments` | list of text | `[]` | Command-line arguments. |
| `playModes` | list | `[]` | Extra play buttons. See below. |
| `shareCache` | true/false | `false` | Share one download cache across your server's instances. |
| `accent` | `#rrggbb` | `"#5b9be0"` | Your colour. |
| `branding` | object | | See below. |

### `build`

| Field | | |
|---|---|---|
| `manifestId` | text | A depot 438101 manifest: this exact build. |
| `branch` | text | Instead of `manifestId`: always the newest build on this Steam branch. |
| `version` | text | A label for players. On its own it can only match a build the player already has. |

### `downloads[]`

| Field | | |
|---|---|---|
| `url` | text | Direct download URL. |
| `path` | text | Where it goes in the game folder (`""` for the folder itself, when extracting). |
| `extract` | true/false | Unzip into `path` instead of saving the file at `path`. |
| `sha256` | hex | Expected SHA-256. A mismatch stops the install. |

### `playModes[]`

| Field | | |
|---|---|---|
| `label` | text | Required. The menu item's text. |
| `icon` | text | One of the launcher's icon names (see [Extra play buttons](#extra-play-buttons)). |
| `arguments` | list of text | Added to the template's `arguments`. |
| `executable` | text | Starts this instead, relative to the game folder. Not for official templates. |

### `branding`

| Field | | |
|---|---|---|
| `tagline` | text | One line under your server's name. |
| `iconUrl` | URL | Square icon, at least 128×128. |
| `iconBackground` | CSS colour | Shown behind a transparent icon. |
| `backgroundUrl` | URL | Full-screen page art. |
| `news` | `{ "type": "rss" \| "json", "url" }` | Your news feed. |
| `links` | list of `{ "label", "url", "kind" }` | `kind`: `discord`, `x`, `youtube`, `github`, `website`. |

### A complete example

```json
{
  "id": "my-server",
  "name": "My Server",
  "server": "My Server",
  "kind": "Modded",
  "version": "3",
  "description": "A cozy server for 2022-era worlds.",
  "accent": "#e0559b",
  "build": { "version": "2026.4.1p1-1937", "manifestId": "1234567890123456789" },
  "downloads": [
    { "url": "https://cdn.my-server.example/BepInEx-Unity.IL2CPP-win-x64.zip", "path": "", "extract": true, "sha256": "…" },
    { "url": "https://cdn.my-server.example/MyServer.Plugin.dll", "path": "BepInEx/plugins/MyServer.Plugin.dll", "sha256": "…" }
  ],
  "files": [
    { "path": "BepInEx/config/MyServer.cfg", "content": "[Server]\nApiUrl = https://api.my-server.example/api/1/\n" }
  ],
  "executable": "VRChat.exe",
  "arguments": [],
  "playModes": [
    { "label": "Play in desktop mode", "icon": "monitor", "arguments": ["--no-vr"] }
  ],
  "shareCache": true,
  "branding": {
    "tagline": "Worlds from the before times",
    "iconUrl": "https://cdn.my-server.example/icon.png",
    "backgroundUrl": "https://cdn.my-server.example/key-art.jpg",
    "news": { "type": "json", "url": "https://api.my-server.example/api/1/news" },
    "links": [ { "label": "Discord", "url": "https://discord.gg/…", "kind": "discord" } ]
  }
}
```
