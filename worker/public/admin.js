const state = { servers: [], requests: [], activity: [], user: null };
const $ = (id) => document.getElementById(id);

function el(tag, props = {}, ...children) {
  const node = document.createElement(tag);
  for (const [key, value] of Object.entries(props)) {
    if (key === "class") node.className = value;
    else if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
    else if (value !== undefined && value !== null && value !== false) node.setAttribute(key, value);
  }
  node.append(...children.flat().filter((c) => c !== null && c !== undefined && c !== false));
  return node;
}

function toast(text, error = false) {
  const item = el("div", { class: error ? "toast error" : "toast" }, text);
  $("toasts").append(item);
  setTimeout(() => item.remove(), 4500);
}

async function api(path, options = {}) {
  const response = await fetch(`/admin/api/${path}`, {
    ...options,
    headers: { "Content-Type": "application/json", ...options.headers },
    body: options.body ? JSON.stringify(options.body) : undefined,
  });
  if (response.status === 401) {
    location.reload();
    throw new Error("Signed out.");
  }
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.error ?? `HTTP ${response.status}`);
  return data;
}

function confirmButton(label, onConfirm) {
  let armed = false;
  let timer;
  const button = el("button", { class: "small danger", type: "button" }, label);
  button.addEventListener("click", async () => {
    if (!armed) {
      armed = true;
      button.classList.add("armed");
      button.textContent = "Sure?";
      timer = setTimeout(() => {
        armed = false;
        button.classList.remove("armed");
        button.textContent = label;
      }, 3000);
      return;
    }
    clearTimeout(timer);
    button.disabled = true;
    await onConfirm();
  });
  return button;
}

function checkList(checks = []) {
  return el(
    "ul",
    { class: "check-results" },
    checks.map((c) => el("li", { class: c.ok ? "" : "bad" }, c.label, c.detail ? el("span", { class: "detail" }, ` ${c.detail}`) : null)),
  );
}

const monogram = (name) => name.split(/\s+/).map((w) => w[0]).join("").slice(0, 2).toUpperCase();
const when = (iso) => new Date(iso).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });

function renderServers() {
  const list = $("server-list");
  if (!state.servers.length) {
    list.replaceChildren(el("div", { class: "empty card" }, "No verified servers. Add one, or approve a request."));
    return;
  }
  list.replaceChildren(
    ...state.servers.map((server, index) =>
      el(
        "div",
        { class: "server-row" },
        el("span", { class: "server-icon mono", style: "background:#6aa8ff" }, monogram(server.name)),
        el(
          "div",
          { class: "grow" },
          el("b", {}, server.name),
          el("div", { class: "url", title: server.templateUrl }, server.templateUrl),
          server.note ? el("div", { class: "note" }, server.note) : null,
          el("div", { class: "by" }, `Verified by ${server.verifiedBy.join(", ") || "nobody named"} · updated ${when(server.updatedAt)}`),
        ),
        el(
          "div",
          { class: "row-actions" },
          el("button", { class: "small ghost", type: "button", title: "Move up", disabled: index === 0 ? "" : null, onclick: () => move(index, -1) }, "↑"),
          el("button", { class: "small ghost", type: "button", title: "Move down", disabled: index === state.servers.length - 1 ? "" : null, onclick: () => move(index, 1) }, "↓"),
          el("button", { class: "small secondary", type: "button", onclick: () => openModal(server) }, "Edit"),
          confirmButton("Remove", () => removeServer(server)),
        ),
      ),
    ),
  );
}

function renderRequests() {
  const count = $("request-count");
  count.hidden = state.requests.length === 0;
  count.textContent = state.requests.length;
  const list = $("request-list");
  if (!state.requests.length) {
    list.replaceChildren(el("div", { class: "empty card" }, "Nothing waiting for review."));
    return;
  }
  list.replaceChildren(
    ...state.requests.map((request) => {
      const failed = (request.checks ?? []).filter((c) => !c.ok).length;
      return el(
        "div",
        { class: "server-row request-row" },
        el("span", { class: "server-icon mono", style: "background:#2a3141" }, monogram(request.name)),
        el(
          "div",
          { class: "grow" },
          el("b", {}, request.name),
          el("span", { class: failed ? "tag warn" : "tag ok" }, failed ? `${failed} failed` : "checks pass"),
          el("div", { class: "url", title: request.templateUrl }, request.templateUrl),
          request.note ? el("div", { class: "note" }, request.note) : null,
          el("div", { class: "by" }, `From ${request.contact} · ${when(request.submittedAt)}${request.checkedAt ? ` · checked ${when(request.checkedAt)}` : ""}`),
          checkList(request.checks),
        ),
        el(
          "div",
          { class: "row-actions" },
          el("button", { class: "small secondary", type: "button", onclick: (e) => recheck(request, e.currentTarget) }, "Re-check"),
          confirmButton("Decline", () => reject(request)),
          el("button", { class: "small", type: "button", onclick: () => openModal({ name: request.name, templateUrl: request.templateUrl, note: "" }, request) }, "Approve…"),
        ),
      );
    }),
  );
}

function renderActivity() {
  $("activity-list").replaceChildren(
    ...(state.activity.length
      ? state.activity.map((a) => el("tr", {}, el("td", { class: "when" }, when(a.at)), el("td", {}, a.action), el("td", {}, a.detail)))
      : [el("tr", {}, el("td", { colspan: "3", class: "empty" }, "Nothing yet."))]),
  );
}

function renderAll() {
  renderServers();
  renderRequests();
  renderActivity();
}

async function load() {
  try {
    Object.assign(state, await api("state"));
    renderAll();
  } catch (e) {
    toast(e.message, true);
  }
}

async function move(index, by) {
  const ids = state.servers.map((s) => s.id);
  [ids[index], ids[index + by]] = [ids[index + by], ids[index]];
  try {
    state.servers = await api("servers/order", { method: "POST", body: { ids } });
    renderServers();
  } catch (e) {
    toast(e.message, true);
  }
}

async function removeServer(server) {
  try {
    await api(`servers/${server.id}`, { method: "DELETE" });
    toast(`${server.name} is no longer verified.`);
    await load();
  } catch (e) {
    toast(e.message, true);
  }
}

async function reject(request) {
  try {
    await api(`requests/${request.id}/reject`, { method: "POST" });
    toast(`Declined ${request.name}.`);
    await load();
  } catch (e) {
    toast(e.message, true);
  }
}

async function recheck(request, button) {
  button.disabled = true;
  try {
    const updated = await api(`requests/${request.id}/recheck`, { method: "POST" });
    Object.assign(request, updated);
    renderRequests();
  } catch (e) {
    toast(e.message, true);
    button.disabled = false;
  }
}

const modal = $("server-modal");
const backdrop = $("modal-backdrop");

function openModal(server = null, request = null) {
  modal.reset();
  $("m-checks").replaceChildren();
  $("modal-title").textContent = request ? `Approve ${request.name}` : server?.id ? `Edit ${server.name}` : "Add server";
  $("m-save").textContent = request ? "Verify server" : "Save";
  modal.elements.id.value = server?.id ?? "";
  modal.elements.requestId.value = request?.id ?? "";
  modal.elements.name.value = server?.name ?? "";
  modal.elements.templateUrl.value = server?.templateUrl ?? "";
  modal.elements.verifiedBy.value = server?.verifiedBy?.join(", ") ?? state.user?.name ?? "";
  modal.elements.note.value = server?.note ?? "";
  modal.hidden = backdrop.hidden = false;
  modal.elements.name.focus();
  if (request) $("m-check").click();
}

function closeModal() {
  modal.hidden = backdrop.hidden = true;
}

modal.addEventListener("click", (e) => e.target.closest("[data-close]") && closeModal());
backdrop.addEventListener("click", closeModal);
document.addEventListener("keydown", (e) => e.key === "Escape" && !modal.hidden && closeModal());

$("m-check").addEventListener("click", async (e) => {
  const button = e.currentTarget;
  const url = modal.elements.templateUrl.value.trim();
  if (!url) return;
  button.disabled = true;
  try {
    const { checks, look } = await api("check", { method: "POST", body: { url } });
    $("m-checks").replaceWith(Object.assign(checkList(checks), { id: "m-checks" }));
    if (look?.name && !modal.elements.name.value) modal.elements.name.value = look.name;
    if (look?.tagline && !modal.elements.note.value) modal.elements.note.value = look.tagline;
  } catch (err) {
    toast(err.message, true);
  } finally {
    button.disabled = false;
  }
});

modal.addEventListener("submit", async (e) => {
  e.preventDefault();
  const form = modal.elements;
  const payload = {
    name: form.name.value,
    templateUrl: form.templateUrl.value,
    verifiedBy: form.verifiedBy.value.split(",").map((v) => v.trim()).filter(Boolean),
    note: form.note.value,
    requestId: form.requestId.value || undefined,
  };
  $("m-save").disabled = true;
  try {
    if (form.id.value) await api(`servers/${form.id.value}`, { method: "PUT", body: payload });
    else await api("servers", { method: "POST", body: payload });
    toast(`${payload.name} saved. Launchers pick it up within a few minutes.`);
    closeModal();
    await load();
  } catch (err) {
    toast(err.message, true);
  } finally {
    $("m-save").disabled = false;
  }
});

$("add-server").addEventListener("click", () => openModal());

document.querySelectorAll("[data-tab]").forEach((tab) =>
  tab.addEventListener("click", () => {
    document.querySelectorAll("[data-tab]").forEach((t) => t.classList.toggle("on", t === tab));
    document.querySelectorAll("[data-view]").forEach((v) => (v.hidden = v.dataset.view !== tab.dataset.tab));
    history.replaceState(null, "", `#${tab.dataset.tab}`);
  }),
);
document.querySelector(`[data-tab="${location.hash.slice(1)}"]`)?.click();

load();
