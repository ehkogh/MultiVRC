const SERVERS = "servers";
const REQUESTS = "requests";
const ACTIVITY = "activity";
const MAX_ACTIVITY = 200;

export const newId = () => crypto.randomUUID().replaceAll("-", "").slice(0, 12);

export async function servers(env) {
  return (await env.DATA.get(SERVERS, "json")) ?? [];
}

export async function saveServers(env, list) {
  await env.DATA.put(SERVERS, JSON.stringify(list));
}

export async function requests(env) {
  return (await env.DATA.get(REQUESTS, "json")) ?? [];
}

export async function saveRequests(env, list) {
  await env.DATA.put(REQUESTS, JSON.stringify(list));
}

export async function activity(env) {
  return (await env.DATA.get(ACTIVITY, "json")) ?? [];
}

export async function record(env, action, detail) {
  const list = await activity(env);
  list.unshift({ at: new Date().toISOString(), action, detail });
  await env.DATA.put(ACTIVITY, JSON.stringify(list.slice(0, MAX_ACTIVITY)));
}

export const sameUrl = (a, b) => a.replace(/\/+$/, "").toLowerCase() === b.replace(/\/+$/, "").toLowerCase();

export function publicEntry(server) {
  return { name: server.name, templateUrl: server.templateUrl, verifiedBy: server.verifiedBy, note: server.note };
}

export function cleanServer(input, existing) {
  const name = String(input.name ?? "").trim().slice(0, 80);
  const templateUrl = String(input.templateUrl ?? "").trim();
  const note = String(input.note ?? "").trim().slice(0, 600);
  const verifiedBy = (Array.isArray(input.verifiedBy) ? input.verifiedBy : String(input.verifiedBy ?? "").split(","))
    .map((v) => String(v).trim().slice(0, 40))
    .filter(Boolean)
    .slice(0, 20);
  if (!name) throw new Error("A name is needed.");
  if (!isHttps(templateUrl)) throw new Error("The template URL must be an https:// address.");
  const now = new Date().toISOString();
  return {
    id: existing?.id ?? newId(),
    name,
    templateUrl,
    verifiedBy,
    note,
    addedAt: existing?.addedAt ?? now,
    updatedAt: now,
  };
}

export function isHttps(value) {
  try {
    return new URL(value).protocol === "https:";
  } catch {
    return false;
  }
}
