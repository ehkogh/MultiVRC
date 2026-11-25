import { renderHome } from "./home.js";
import { renderLogin, renderPanel } from "./admin.js";
import { checkTemplate, fetchTemplate, summarize } from "./checks.js";
import { clearCookie, passwordMatches, sameOrigin, session, sessionCookie } from "./auth.js";
import {
  activity, cleanServer, isHttps, newId, publicEntry, record, requests, sameUrl, saveRequests, saveServers, servers,
} from "./store.js";

const MAX_PENDING = 50;
const LOGIN_ATTEMPTS = 10;
const LOGIN_WINDOW = 900;

const CSP = [
  "default-src 'self'",
  "img-src 'self' https: data:",
  "script-src 'self' https://challenges.cloudflare.com",
  "frame-src https://challenges.cloudflare.com",
  "style-src 'self' 'unsafe-inline'",
  "connect-src 'self'",
  "base-uri 'none'",
  "form-action 'self'",
  "frame-ancestors 'none'",
].join("; ");

const htmlResponse = (body, init = {}) =>
  new Response(body, {
    ...init,
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      "Content-Security-Policy": CSP,
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "strict-origin-when-cross-origin",
      ...init.headers,
    },
  });

const json = (data, status = 200, headers = {}) =>
  new Response(JSON.stringify(data), { status, headers: { "Content-Type": "application/json; charset=utf-8", ...headers } });

const fail = (message, status = 400) => json({ error: message }, status);
const redirect = (to, headers = {}) => new Response(null, { status: 303, headers: { Location: to, ...headers } });

export default {
  async fetch(request, env, ctx) {
    try {
      return await route(request, env, ctx);
    } catch (e) {
      if (e instanceof HttpError) return fail(e.message, e.status);
      console.error(e);
      return fail("Something went wrong.", 500);
    }
  },
};

class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

async function route(request, env, ctx) {
  const url = new URL(request.url);
  const path = url.pathname.replace(/\/+$/, "") || "/";
  const method = request.method;

  if (method === "GET" && path === "/") return home(env);
  if (path === "/api/verified-servers" || path === "/verified-servers.json") {
    if (method === "OPTIONS") return new Response(null, { status: 204, headers: cors() });
    if (method === "GET") return verifiedList(env);
  }
  if (method === "POST" && path === "/api/requests") return submitRequest(request, env);

  if (path === "/admin") {
    if (method !== "GET") throw new HttpError(405, "Method not allowed.");
    const user = await session(request, env);
    return htmlResponse(user ? renderPanel(user) : renderLogin(), { headers: { "Cache-Control": "no-store" } });
  }
  if (method === "POST" && path === "/admin/login") return login(request, env);
  if (method === "POST" && path === "/admin/logout") return redirect("/", { "Set-Cookie": clearCookie() });
  if (path.startsWith("/admin/api/")) return adminApi(request, env, path.slice("/admin/api/".length), method);

  return env.ASSETS.fetch(request);
}

const cors = () => ({ "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Methods": "GET, OPTIONS" });

async function verifiedList(env) {
  const list = (await servers(env)).map(publicEntry);
  return json(list, 200, { ...cors(), "Cache-Control": "public, max-age=300" });
}

async function home(env) {
  const list = await servers(env);
  const [looks, release, stars] = await Promise.all([
    Promise.all(list.map(async (s) => summarize(await fetchTemplate(s.templateUrl)))),
    latestRelease(env.GITHUB_REPO),
    repoStars(env.GITHUB_REPO),
  ]);
  const shown = list.map((s, i) => ({ ...s, look: looks[i] }));
  return htmlResponse(renderHome({ servers: shown, release, stars, repo: env.GITHUB_REPO, turnstileSiteKey: env.TURNSTILE_SITE_KEY }), {
    headers: { "Cache-Control": "public, max-age=60" },
  });
}

async function latestRelease(repo) {
  try {
    const response = await fetch(`https://api.github.com/repos/${repo}/releases/latest`, {
      headers: { "User-Agent": "MultiVRC-Site", Accept: "application/vnd.github+json" },
      cf: { cacheTtl: 600, cacheEverything: true },
      signal: AbortSignal.timeout(5000),
    });
    if (!response.ok) return null;
    const release = await response.json();
    const setup = release.assets?.find((a) => /^MultiVRC-Setup-.*\.exe$/i.test(a.name));
    return {
      version: String(release.tag_name ?? "").replace(/^v/, ""),
      url: release.html_url,
      setup: setup ? { url: setup.browser_download_url, size: setup.size } : null,
    };
  } catch {
    return null;
  }
}

async function repoStars(repo) {
  try {
    const response = await fetch(`https://api.github.com/repos/${repo}`, {
      headers: { "User-Agent": "MultiVRC-Site", Accept: "application/vnd.github+json" },
      cf: { cacheTtl: 600, cacheEverything: true },
      signal: AbortSignal.timeout(5000),
    });
    if (!response.ok) return null;
    const count = (await response.json()).stargazers_count;
    return Number.isInteger(count) ? count : null;
  } catch {
    return null;
  }
}

async function body(request) {
  const type = request.headers.get("Content-Type") ?? "";
  if (type.includes("application/json")) {
    try {
      return await request.json();
    } catch {
      throw new HttpError(400, "The request body is not valid JSON.");
    }
  }
  return Object.fromEntries(await request.formData());
}

async function submitRequest(request, env) {
  if (!sameOrigin(request)) throw new HttpError(403, "Send requests from the homepage.");
  const input = await body(request);
  if (input.website) return json({ ok: true, checks: [] });

  if (env.TURNSTILE_SECRET) {
    const verdict = await fetch("https://challenges.cloudflare.com/turnstile/v0/siteverify", {
      method: "POST",
      body: new URLSearchParams({
        secret: env.TURNSTILE_SECRET,
        response: String(input["cf-turnstile-response"] ?? ""),
        remoteip: request.headers.get("CF-Connecting-IP") ?? "",
      }),
    }).then((r) => r.json()).catch(() => ({ success: false }));
    if (!verdict.success) throw new HttpError(400, "The challenge failed. Reload the page and try again.");
  }

  const name = String(input.name ?? "").trim().slice(0, 80);
  const templateUrl = String(input.templateUrl ?? "").trim();
  const contact = String(input.contact ?? "").trim().slice(0, 120);
  const note = String(input.note ?? "").trim().slice(0, 1000);
  if (!name || !contact) throw new HttpError(400, "A server name and a way to reach you are needed.");
  if (!isHttps(templateUrl)) throw new HttpError(400, "The template URL must be an https:// address.");

  if ((await servers(env)).some((s) => sameUrl(s.templateUrl, templateUrl))) throw new HttpError(409, "That template is already verified.");
  const pending = await requests(env);
  if (pending.some((r) => sameUrl(r.templateUrl, templateUrl))) throw new HttpError(409, "That template is already waiting for review.");
  if (pending.length >= MAX_PENDING) throw new HttpError(503, "Too many requests are waiting for review. Try again later.");

  const { checks } = await checkTemplate(templateUrl);
  pending.push({ id: newId(), name, templateUrl, contact, note, checks, submittedAt: new Date().toISOString() });
  await saveRequests(env, pending);
  await record(env, "request", `${name} asked to be verified (${templateUrl})`);
  return json({ ok: true, checks }, 201);
}

async function login(request, env) {
  if (!sameOrigin(request)) throw new HttpError(403, "Sign in from the management page.");
  const ip = request.headers.get("CF-Connecting-IP") ?? "unknown";
  const throttleKey = `login:${ip}`;
  const attempts = Number((await env.DATA.get(throttleKey)) ?? 0);
  const form = await request.formData();
  const name = String(form.get("name") ?? "").trim().slice(0, 40);
  const show = (error, status) => htmlResponse(renderLogin({ error, name }), { status, headers: { "Cache-Control": "no-store" } });

  if (!env.ADMIN_PASSWORD) return show("The panel has no password yet: set ADMIN_PASSWORD with wrangler secret put.", 503);
  if (attempts >= LOGIN_ATTEMPTS) return show("Too many attempts. Wait a few minutes and try again.", 429);
  if (!name || !(await passwordMatches(env, String(form.get("password") ?? "")))) {
    await env.DATA.put(throttleKey, String(attempts + 1), { expirationTtl: LOGIN_WINDOW });
    return show("That password is not right.", 401);
  }
  await env.DATA.delete(throttleKey);
  await record(env, "sign-in", `${name} signed in`);
  return redirect("/admin", { "Set-Cookie": await sessionCookie(env, name) });
}

async function adminApi(request, env, path, method) {
  const user = await session(request, env);
  if (!user) throw new HttpError(401, "Sign in again.");
  if (method !== "GET" && !sameOrigin(request)) throw new HttpError(403, "Cross-site request refused.");
  const parts = path.split("/");

  if (method === "GET" && path === "state") {
    const [list, pending, log] = await Promise.all([servers(env), requests(env), activity(env)]);
    return json({ user, servers: list, requests: pending, activity: log }, 200, { "Cache-Control": "no-store" });
  }

  if (method === "POST" && path === "check") {
    const { url } = await body(request);
    const { checks, template } = await checkTemplate(String(url ?? ""));
    return json({ checks, look: summarize(template) });
  }

  if (parts[0] === "servers") {
    const list = await servers(env);

    if (method === "POST" && parts.length === 1) {
      const input = await body(request);
      const server = clean(input);
      if (list.some((s) => sameUrl(s.templateUrl, server.templateUrl))) throw new HttpError(409, "That template URL is already listed.");
      list.push(server);
      await saveServers(env, list);
      if (input.requestId) await saveRequests(env, (await requests(env)).filter((r) => r.id !== input.requestId));
      await record(env, "add", `${user.name} verified ${server.name} (${server.templateUrl})`);
      return json(server, 201);
    }

    if (method === "POST" && parts[1] === "order") {
      const { ids } = await body(request);
      if (!Array.isArray(ids)) throw new HttpError(400, "ids must be a list.");
      const rank = new Map(ids.map((id, i) => [id, i]));
      list.sort((a, b) => (rank.get(a.id) ?? 1e9) - (rank.get(b.id) ?? 1e9));
      await saveServers(env, list);
      await record(env, "reorder", `${user.name} reordered the list`);
      return json(list);
    }

    const index = list.findIndex((s) => s.id === parts[1]);
    if (index === -1) throw new HttpError(404, "No such server.");
    const existing = list[index];

    if (method === "PUT") {
      const server = clean(await body(request), existing);
      if (list.some((s, i) => i !== index && sameUrl(s.templateUrl, server.templateUrl))) throw new HttpError(409, "That template URL is already listed.");
      list[index] = server;
      await saveServers(env, list);
      await record(env, "edit", `${user.name} edited ${server.name}${sameUrl(existing.templateUrl, server.templateUrl) ? "" : ` (URL now ${server.templateUrl})`}`);
      return json(server);
    }

    if (method === "DELETE") {
      list.splice(index, 1);
      await saveServers(env, list);
      await record(env, "remove", `${user.name} removed ${existing.name} (${existing.templateUrl})`);
      return json({ ok: true });
    }
  }

  if (parts[0] === "requests" && parts.length === 3 && method === "POST") {
    const pending = await requests(env);
    const item = pending.find((r) => r.id === parts[1]);
    if (!item) throw new HttpError(404, "No such request.");

    if (parts[2] === "reject") {
      await saveRequests(env, pending.filter((r) => r.id !== item.id));
      await record(env, "reject", `${user.name} turned down ${item.name} (${item.templateUrl})`);
      return json({ ok: true });
    }
    if (parts[2] === "recheck") {
      item.checks = (await checkTemplate(item.templateUrl)).checks;
      item.checkedAt = new Date().toISOString();
      await saveRequests(env, pending);
      return json(item);
    }
  }

  throw new HttpError(404, "Not found.");
}

function clean(input, existing) {
  try {
    return cleanServer(input, existing);
  } catch (e) {
    throw new HttpError(400, e.message);
  }
}
