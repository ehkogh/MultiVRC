import { html, icon, logo, page } from "./html.js";

export function renderLogin({ error = "", name = "" } = {}) {
  const body = html`
<main class="login">
  <form class="card login-card" method="post" action="/admin/login">
    <div class="brand big">${logo(40)}<span>MultiVRC</span></div>
    <h1>Management</h1>
    <p class="muted small">Sign in to manage the verified list.</p>
    <label for="l-name">Your name</label><input id="l-name" name="name" required maxlength="40" value="${name}" autocomplete="username" placeholder="Shown in the activity log">
    <label for="l-pw">Password</label><input id="l-pw" name="password" type="password" required autocomplete="current-password" autofocus>
    ${error ? html`<div class="form-error">${error}</div>` : ""}
    <button type="submit" class="wide">${icon("lock", 15)} Sign in</button>
    <a class="muted small back" href="/">Back to the homepage</a>
  </form>
</main>`;
  return page({ title: "Sign in · MultiVRC", body });
}

export function renderPanel({ name }) {
  const body = html`
<div class="panel">
  <nav class="rail">
    <a class="rail-logo" href="/" title="Homepage">${logo(28)}</a>
    <div class="rail-sep"></div>
    <div class="rail-nav">
      <button class="rail-icon on" data-tab="servers" data-tip="Verified servers">${icon("badge-check", 18)}</button>
      <button class="rail-icon" data-tab="requests" data-tip="Requests">${icon("send", 18)}<span class="rail-count" id="request-count" hidden></span></button>
      <button class="rail-icon" data-tab="activity" data-tip="Activity">${icon("layers", 18)}</button>
    </div>
    <div class="rail-bottom">
      <form method="post" action="/admin/logout"><button class="rail-icon" data-tip="Sign out (${name})">${icon("lock", 18)}</button></form>
    </div>
  </nav>
  <main class="content">
    <section data-view="servers">
      <div class="page-head">
        <div><div class="eyebrow">Management</div><h1>Verified servers</h1><p class="muted">What launchers read from <a href="/api/verified-servers" target="_blank"><code>/api/verified-servers</code></a>. A template is verified only when installed from exactly its listed URL.</p></div>
        <button id="add-server">${icon("sparkles", 15)} Add server</button>
      </div>
      <div id="server-list" class="server-list"><div class="empty"><span class="spinner"></span></div></div>
    </section>
    <section data-view="requests" hidden>
      <div class="page-head"><div><div class="eyebrow">Management</div><h1>Verification requests</h1><p class="muted">Sent from the homepage. Each is checked automatically when it arrives; re-check before approving.</p></div></div>
      <div id="request-list" class="server-list"></div>
    </section>
    <section data-view="activity" hidden>
      <div class="page-head"><div><div class="eyebrow">Management</div><h1>Activity</h1><p class="muted">Every change to the list, newest first.</p></div></div>
      <div class="card"><table><thead><tr><th>When</th><th>What</th><th>Details</th></tr></thead><tbody id="activity-list"></tbody></table></div>
    </section>
  </main>
</div>

<div class="modal-backdrop" id="modal-backdrop" hidden></div>
<form class="modal" id="server-modal" hidden>
  <div class="modal-head"><h2 id="modal-title">Add server</h2><span class="spacer"></span><button type="button" class="close" data-close aria-label="Close">${icon("x", 16)}</button></div>
  <div class="modal-body">
    <input type="hidden" name="id">
    <input type="hidden" name="requestId">
    <label for="m-name">Name</label><input id="m-name" name="name" required maxlength="80">
    <label for="m-url">Template URL</label>
    <div class="input-group"><input id="m-url" name="templateUrl" type="url" required placeholder="https://…/multivrc.json"><button type="button" class="secondary" id="m-check">Check</button></div>
    <ul class="check-results" id="m-checks"></ul>
    <label for="m-by">Verified by</label><input id="m-by" name="verifiedBy" placeholder="Comma separated" value="${name}">
    <label for="m-note">Note</label><textarea id="m-note" name="note" maxlength="600" placeholder="Shown in the launcher beside the server"></textarea>
  </div>
  <div class="modal-foot"><span class="spacer"></span><button type="button" class="secondary" data-close>Cancel</button><button type="submit" id="m-save">Save</button></div>
</form>
<div class="toasts" id="toasts"></div>`;
  return page({ title: "Management · MultiVRC", body, scripts: ["/admin.js"] });
}
