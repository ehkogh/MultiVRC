import { isHttps } from "./store.js";

const SHA256 = /^[0-9a-f]{64}$/i;

export async function fetchTemplate(url, ttl = 900) {
  if (!isHttps(url)) return null;
  try {
    const response = await fetch(url, {
      headers: { "User-Agent": "MultiVRC-Site", Accept: "application/json" },
      cf: { cacheTtl: ttl, cacheEverything: true },
      signal: AbortSignal.timeout(6000),
    });
    if (!response.ok) return null;
    return await response.json();
  } catch {
    return null;
  }
}

export async function checkTemplate(url) {
  const checks = [];
  const add = (label, ok, detail = "") => checks.push({ label, ok, detail });

  add("Served over HTTPS", isHttps(url), isHttps(url) ? "" : "The URL must start with https://");
  if (!isHttps(url)) return { checks, template: null };

  let template = null;
  try {
    const response = await fetch(url, {
      headers: { "User-Agent": "MultiVRC-Site", Accept: "application/json" },
      signal: AbortSignal.timeout(8000),
    });
    add("Reachable", response.ok, response.ok ? "" : `HTTP ${response.status}`);
    if (response.ok) {
      const text = await response.text();
      try {
        template = JSON.parse(text);
        add("Valid JSON", true);
      } catch (e) {
        add("Valid JSON", false, e.message);
      }
    }
  } catch (e) {
    add("Reachable", false, e.message);
  }
  if (!template || typeof template !== "object") return { checks, template: null };

  add("Has an id and a name", Boolean(template.id && template.name), template.id ? "" : "id or name is missing");
  add("Has a version", Boolean(template.version), template.version ? `v${template.version}` : "Bump a version so instances update");
  add("Is a modded template", String(template.kind ?? "").toLowerCase() === "modded", `kind: ${template.kind ?? "(none)"}`);

  const downloads = Array.isArray(template.downloads) ? template.downloads : [];
  const unpinned = downloads.filter((d) => !SHA256.test(String(d?.sha256 ?? "")));
  add(
    "Every download is pinned by sha256",
    unpinned.length === 0,
    downloads.length === 0 ? "No downloads" : unpinned.length ? `${unpinned.length} of ${downloads.length} without a sha256` : `${downloads.length} pinned`,
  );
  const insecure = downloads.filter((d) => !isHttps(String(d?.url ?? "")));
  add("Every download is HTTPS", insecure.length === 0, insecure.length ? `${insecure.length} not over https` : "");

  const build = template.build ?? {};
  add("Build is pinned to a manifest", Boolean(build.manifestId), build.manifestId ? build.version ?? build.manifestId : build.branch ? `follows branch ${build.branch}` : "no build");

  return { checks, template };
}

export function summarize(template) {
  if (!template || typeof template !== "object") return null;
  const branding = template.branding ?? {};
  return {
    id: str(template.id, 64),
    name: str(template.name, 80),
    version: str(template.version, 32),
    tagline: str(branding.tagline, 140),
    description: str(template.description, 400),
    iconUrl: httpsOrNull(branding.iconUrl),
    iconBackground: color(branding.iconBackground),
    backgroundUrl: httpsOrNull(branding.backgroundUrl),
    accent: color(template.accent),
    links: (Array.isArray(branding.links) ? branding.links : [])
      .filter((l) => l && isHttps(String(l.url ?? "")))
      .slice(0, 6)
      .map((l) => ({ label: str(l.label, 30) || "Link", url: l.url, kind: str(l.kind, 20) })),
  };
}

const str = (v, max) => (typeof v === "string" ? v.slice(0, max) : "");
const httpsOrNull = (v) => (typeof v === "string" && isHttps(v) ? v : null);
const color = (v) => (typeof v === "string" && /^#[0-9a-f]{3,8}$/i.test(v) ? v : null);
