const COOKIE = "mvrc_session";
const SESSION_SECONDS = 60 * 60 * 12;
const encoder = new TextEncoder();

async function key(env) {
  const secret = env.SESSION_SECRET || env.ADMIN_PASSWORD;
  if (!secret) throw new Error("ADMIN_PASSWORD is not set.");
  return crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign", "verify"]);
}

const hex = (buffer) => [...new Uint8Array(buffer)].map((b) => b.toString(16).padStart(2, "0")).join("");

async function sign(env, value) {
  return hex(await crypto.subtle.sign("HMAC", await key(env), encoder.encode(value)));
}

export async function passwordMatches(env, given) {
  if (!env.ADMIN_PASSWORD || typeof given !== "string") return false;
  const [a, b] = await Promise.all([sign(env, `pw:${given}`), sign(env, `pw:${env.ADMIN_PASSWORD}`)]);
  return safeEqual(a, b);
}

function safeEqual(a, b) {
  let diff = a.length ^ b.length;
  for (let i = 0; i < Math.min(a.length, b.length); i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

export async function sessionCookie(env, name) {
  const expires = Math.floor(Date.now() / 1000) + SESSION_SECONDS;
  const payload = `${expires}.${encodeURIComponent(name).replaceAll(".", "%2E")}`;
  const token = `${payload}.${await sign(env, payload)}`;
  return `${COOKIE}=${token}; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=${SESSION_SECONDS}`;
}

export const clearCookie = () => `${COOKIE}=; Path=/; HttpOnly; Secure; SameSite=Strict; Max-Age=0`;

export async function session(request, env) {
  const cookie = request.headers.get("Cookie") ?? "";
  const token = cookie.split(/;\s*/).find((c) => c.startsWith(`${COOKIE}=`))?.slice(COOKIE.length + 1);
  if (!token) return null;
  const parts = token.split(".");
  if (parts.length !== 3) return null;
  const [expires, name, signature] = parts;
  if (Number(expires) < Date.now() / 1000) return null;
  if (!safeEqual(await sign(env, `${expires}.${name}`), signature)) return null;
  return { name: decodeURIComponent(name) };
}

export function sameOrigin(request) {
  const origin = request.headers.get("Origin");
  return origin !== null && origin === new URL(request.url).origin;
}
