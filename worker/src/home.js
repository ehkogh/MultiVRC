import { html, raw, icon, logo, page } from "./html.js";

const FEATURES = [
  ["builds", "#6ff0bf", "box", "Builds", "Any version, straight from Steam.", "Pick a branch or an exact manifest. Updates only fetch what changed."],
  ["instances", "#6aa8ff", "layers", "Instances", "One build, many setups.", "Instances hardlink their build, so ten of them take the space of one."],
  ["isolation", "#ffc26b", "swap", "Isolation", "Every setup keeps its own login.", "Data, settings and saved login swap in at launch and back when the game closes."],
  ["templates", "#ff6fb5", "sparkles", "Templates", "Servers bring their own look.", "One JSON file sets up the build, mods, art, news and links."],
];

const GUARANTEES = [
  ["shield", "Official stays official", "Nothing is added or loaded. It starts through Easy Anti-Cheat, exactly as Steam starts it."],
  ["swap", "Outside the game", "Swaps happen on disk and in the registry, before launch and after exit."],
  ["lock", "One at a time", "A modded client never runs beside an official one, and never sees its login."],
];

const TRUST = [
  ["official", "#6ff0bf", "shield", "Official", "Official VRChat · protected by Easy Anti-Cheat", "VRChat as Steam ships it.", "Protected by Easy Anti-Cheat and never modified."],
  ["verified", "#6aa8ff", "badge-check", "Verified", "Verified community server", "Checked by people we trust.", "Reviewed by the project. Still a modded client, not anti-cheat protected."],
  ["unverified", "#a0a8b8", "shield-alert", "Unverified", "Community server · not verified, take care", "Nobody has checked it yet.", "Its template can install files that run with the game. Take care."],
];

const STEPS = [
  ["#6ff0bf", "link", "Send the URL", "Fill in the form. Nothing to install, no account needed."],
  ["#6aa8ff", "shield", "Checked automatically", "The template is fetched and checked straight away. You see the results here."],
  ["#ffc26b", "badge-check", "Reviewed by people", "Someone the project trusts reads it. Approved servers get the blue badge."],
];

const NEEDS = [
  ["lock", "A stable HTTPS URL"],
  ["shield", "A <code>sha256</code> on every download"],
  ["github", "The plugin's source, or what it does"],
];

const compact = (n) => (n < 1000 ? String(n) : `${(n / 1000).toFixed(n < 10000 ? 1 : 0).replace(/\.0$/, "")}k`);
const monogram = (name) => name.split(/\s+/).map((w) => w[0]).join("").slice(0, 2).toUpperCase();
const linkIcon = (kind) => (["discord", "github"].includes(kind) ? kind : kind === "website" ? "globe" : "external");

function serverCard(server) {
  const look = server.look ?? {};
  const accent = look.accent ?? "#6aa8ff";
  const cover = look.backgroundUrl
    ? html`<div class="cover card-art" style="background-image:url('${raw(encodeURI(look.backgroundUrl).replace(/'/g, "%27"))}')"></div>`
    : html`<div class="cover card-art pattern-dots" style="background:linear-gradient(135deg, ${accent}, #0b0d12)"><span class="monogram">${monogram(server.name)}</span></div>`;
  const avatar = look.iconUrl
    ? html`<img class="server-icon" src="${look.iconUrl}" alt="" loading="lazy" style="background:${look.iconBackground ?? "transparent"}">`
    : html`<span class="server-icon mono" style="background:${accent}">${monogram(server.name)}</span>`;
  return html`<article class="game-card" style="--accent-card:${accent}">
  <div class="game-cover">${cover}<span class="kind-flag modded">${icon("badge-check", 12)} Verified</span></div>
  <div class="game-info">${avatar}<div class="game-text"><div class="game-name">${server.name}</div><div class="game-sub">${look.tagline || "Community server"}</div></div></div>
  <p class="game-note">${server.note}</p>
  <div class="game-foot">
    <span title="Checked by">${icon("shield", 13)} ${server.verifiedBy.length ? server.verifiedBy.join(", ") : "The MultiVRC team"}</span>
    <span class="row tight">${(look.links ?? []).map((l) => html`<a class="icon-link" href="${l.url}" rel="noopener nofollow" target="_blank" title="${l.label}">${icon(linkIcon(l.kind), 15)}</a>`)}
      <button class="icon-link" data-copy="${server.templateUrl}" title="Copy template URL">${icon("copy", 15)}</button></span>
  </div>
</article>`;
}

function marqueeRow(list, reverse) {
  const reps = Math.max(1, Math.ceil(8 / list.length));
  const set = Array.from({ length: reps }, () => list).flat();
  return html`<div class="marquee-row${reverse ? " reverse" : ""}" style="--duration:${set.length * 8}s"${reverse ? raw(' aria-hidden="true" inert') : ""}>
    <div class="marquee-track">
      <div class="marquee-set">${set.map((s, i) => (i < list.length ? serverCard(s) : raw(String(serverCard(s)).replace("<article ", '<article aria-hidden="true" inert '))))}</div>
      <div class="marquee-set" aria-hidden="true" inert>${set.map(serverCard)}</div>
    </div>
  </div>`;
}

function marquee(servers) {
  const half = Math.ceil(servers.length / 2);
  const second = [...servers.slice(half), ...servers.slice(0, half)];
  return html`<div class="marquee" aria-label="Verified servers">${marqueeRow(servers, false)}${servers.length >= 5 ? marqueeRow(second, true) : ""}</div>`;
}

export function renderHome({ servers, release, stars, repo, turnstileSiteKey }) {
  const backdrop = servers.find((s) => s.look?.backgroundUrl)?.look.backgroundUrl ?? null;
  const download = release?.setup?.url ?? `https://github.com/${repo}/releases/latest`;
  const body = html`
<header class="topbar">
  <a class="brand" href="/" aria-label="MultiVRC home">${logo(30)}<span>MultiVRC</span></a>
  <nav class="toplinks"><a href="#features">Features</a><a href="#safety">Safety</a><a href="#servers">Servers</a><a href="#apply">Get verified</a></nav>
  <span class="spacer"></span>
  <a class="star-link" href="https://github.com/${repo}" target="_blank" rel="noopener" title="Star MultiVRC on GitHub">${icon("github", 17)}<span class="star-count">${icon("star", 13)} ${stars == null ? "Star" : compact(stars)}</span></a>
  <a class="button small" href="${download}">${icon("download", 15)} Download</a>
</header>

<div class="hero-band">
  <section class="hero">
    <div class="hero-text">
      <h1>Simplify your<br><img class="vrc-logo" src="/img/vrchat.webp" width="516" height="225" alt="VRChat"> <span class="grad-text">setup.</span></h1>
      <p class="lede">Official, betas, old builds and private servers. Installed together, never in each other's way.</p>
      <div class="actions">
        <a class="button big" href="${download}">${icon("download", 18)} Download</a>
        <a class="button secondary big" href="https://github.com/${repo}" target="_blank" rel="noopener">${icon("github", 18)} Source</a>
      </div>
      ${release?.linux ? html`<p class="alt-download">Also on Linux: <a href="${release.linux.url}">download ${release.linux.version}</a>${release.linux.prerelease ? html` <span class="pre-tag">pre-release</span>` : ""} · <a href="https://github.com/${repo}#linux" target="_blank" rel="noopener">what you need</a></p>` : ""}
    </div>
    <figure class="hero-shot">
      <img src="/img/launcher.webp" width="1360" height="800" alt="The MultiVRC launcher open on a community server's page, with its instances down the left rail, a Play button, play time and the server's news.">
    </figure>
  </section>
</div>

<main class="site">

  <section id="features" class="features-head">
    <h2 class="sr-only">What it does</h2>
  </section>
</main>

<div class="feature-band">
  <div class="feature-strip">
    ${FEATURES.map(([clip, tone, i, label, title, text]) => html`<article class="feature-panel" style="--tone:${tone}">
      <div class="feature-clip"><video muted loop playsinline preload="none" poster="/clips/${clip}.jpg" width="960" height="720" aria-label="${label} in the launcher"><source src="/clips/${clip}.mp4" type="video/mp4"></video></div>
      <div class="feature-text">
        <div class="feature-label">${icon(i, 15)} ${label}</div>
        <h3>${title}</h3>
        <p>${text}</p>
      </div>
    </article>`)}
  </div>
</div>

<main class="site">
</main>

<div class="safety-band">
  <section id="safety" class="safety">
    <h2 class="big-title">Never the reason<br><span class="grad-text">you get banned.</span></h2>
    <p class="lede">Official instances stay exactly as Steam ships them. Everything else happens outside the game.</p>
    <ul class="guarantees">
      ${GUARANTEES.map(([i, title, text]) => html`<li><span class="guarantee-icon">${icon(i, 18)}</span><b>${title}</b><p>${text}</p></li>`)}
    </ul>
  </section>
  <div class="feature-strip trio">
    ${TRUST.map(([cls, tone, i, label, pill, title, text]) => html`<article class="feature-panel trust-panel" style="--tone:${tone}">
      <div class="trust-visual">
        <span class="trust-disc">${icon(i, 40)}</span>
        <span class="trust ${cls} open">${icon(i, 15)}<span class="trust-text">${pill}</span></span>
      </div>
      <div class="feature-text">
        <div class="feature-label">${icon(i, 15)} ${label}</div>
        <h3>${title}</h3>
        <p>${text}</p>
      </div>
    </article>`)}
  </div>
  <p class="safety-note">Verification lives with MultiVRC, never with a server: a template can't vouch for itself.</p>
</div>

<main class="site">
</main>

<div class="servers-band">
  <section id="servers" class="servers-head">
    <h2 class="big-title">Verified servers.</h2>
    <p class="lede">Checked by people the project trusts. Add one from the launcher under <b>New instance → Verified community</b>.</p>
  </section>
  ${servers.length
    ? marquee(servers)
    : html`<div class="site"><div class="library-empty">${icon("badge-check", 28)}<h3>No verified servers yet</h3><p class="muted">Run a server? Apply below.</p></div></div>`}
</div>

<div class="apply-band">
  <section id="apply" class="apply">
    <div class="apply-intro">
      <h2 class="big-title">Run a server?<br><span class="grad-text">Get it verified.</span></h2>
      <p class="lede">Send your template's URL. Once approved, launchers show the blue badge for templates installed from exactly that URL.</p>
      <ol class="steps">
        ${STEPS.map(([tone, i, title, text]) => html`<li style="--tone:${tone}"><span class="step-icon">${icon(i, 18)}</span><div><b>${title}</b><p>${text}</p></div></li>`)}
      </ol>
      <div class="needs">
        <span class="needs-label">Before you send</span>
        <ul>${NEEDS.map(([i, text]) => html`<li>${icon(i, 14)} ${raw(text)}</li>`)}</ul>
        <p class="muted small">New to templates? Read <a href="https://github.com/${repo}/blob/main/docs/making-templates.md" target="_blank" rel="noopener">Making a template</a>.</p>
      </div>
    </div>
    <form class="apply-form" id="apply-form">
      <div class="feature-label">${icon("send", 15)} Apply</div>
      <h3>Send for review</h3>
      <div class="field-pair">
        <div><label for="a-name">Server name</label><input id="a-name" name="name" required maxlength="80" placeholder="My Server"></div>
        <div><label for="a-contact">How to reach you</label><input id="a-contact" name="contact" required maxlength="120" placeholder="Discord or email"></div>
      </div>
      <label for="a-url">Template URL</label><input id="a-url" name="templateUrl" type="url" required placeholder="https://my-server.example/multivrc.json">
      <label for="a-note">What it is, and where its source lives</label><textarea id="a-note" name="note" maxlength="1000" placeholder="2022-era worlds; BepInEx with the MyServer plugin (github.com/…)"></textarea>
      <input class="trap" name="website" tabindex="-1" autocomplete="off" aria-hidden="true">
      ${turnstileSiteKey ? html`<div class="cf-turnstile" data-sitekey="${turnstileSiteKey}" data-theme="dark"></div>` : ""}
      <div class="actions"><button type="submit" class="big">${icon("send", 16)} Send for review</button><span class="form-status muted small" id="apply-status"></span></div>
      <ul class="check-results" id="apply-checks"></ul>
    </form>
  </section>
</div>

<footer class="footer">
  <span class="brand small">${logo(20)}<span>MultiVRC</span></span>
  <span class="muted small">MIT licence. VRChat is a trademark of VRChat Inc.; this project is not affiliated with it.</span>
  <span class="spacer"></span>
  <a class="signature" href="https://ehko.dev" target="_blank" rel="noopener me" title="Made by ehko"><img src="/img/signature.svg" width="80" height="36" alt="ehko"></a>
  <span class="footer-sep" aria-hidden="true"></span>
  <a class="icon-link" href="https://x.com/callmeehko" target="_blank" rel="noopener me" title="Twitter">${icon("twitter", 16)}</a>
  <a class="icon-link" href="https://github.com/ehkogh" target="_blank" rel="noopener me" title="GitHub">${icon("github", 16)}</a>
</footer>
<div class="toasts" id="toasts"></div>`;

  const scripts = ["/site.js"];
  if (turnstileSiteKey) scripts.push("https://challenges.cloudflare.com/turnstile/v0/api.js");
  return page({
    title: "MultiVRC · Simplify your VRChat setup",
    description: "Official VRChat, betas, old builds and private servers. Installed together, never in each other's way.",
    body,
    backdrop,
    scripts,
  });
}
