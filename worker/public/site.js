function toast(text, error = false) {
  const box = document.getElementById("toasts");
  if (!box) return;
  const item = document.createElement("div");
  item.className = error ? "toast error" : "toast";
  item.textContent = text;
  box.append(item);
  setTimeout(() => item.remove(), 4500);
}

function showChecks(list, checks) {
  list.replaceChildren(
    ...checks.map((c) => {
      const li = document.createElement("li");
      li.className = c.ok ? "" : "bad";
      li.append(c.label);
      if (c.detail) {
        const detail = document.createElement("span");
        detail.className = "detail";
        detail.textContent = c.detail;
        li.append(" ", detail);
      }
      return li;
    }),
  );
}

document.addEventListener("click", async (e) => {
  const button = e.target.closest("[data-copy]");
  if (!button) return;
  try {
    await navigator.clipboard.writeText(button.dataset.copy);
    toast("Template URL copied. Paste it into New instance in the launcher.");
  } catch {
    toast(button.dataset.copy);
  }
});

const form = document.getElementById("apply-form");
if (form) {
  const status = document.getElementById("apply-status");
  const checks = document.getElementById("apply-checks");
  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const submit = form.querySelector("button[type=submit]");
    submit.disabled = true;
    status.textContent = "Checking your template…";
    checks.replaceChildren();
    try {
      const response = await fetch("/api/requests", { method: "POST", body: new FormData(form) });
      const result = await response.json();
      if (!response.ok) throw new Error(result.error ?? "That did not work.");
      showChecks(checks, result.checks);
      const failed = result.checks.filter((c) => !c.ok).length;
      status.textContent = failed ? `Sent. ${failed} check${failed === 1 ? "" : "s"} did not pass: fix them before review.` : "Sent for review. Thanks!";
      form.reset();
    } catch (err) {
      status.textContent = "";
      toast(err.message, true);
    } finally {
      submit.disabled = false;
      window.turnstile?.reset();
    }
  });
}

const clips = document.querySelectorAll(".feature-clip video");
const canHover = matchMedia("(hover: hover) and (pointer: fine)").matches;
const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;

if (clips.length && "IntersectionObserver" in window) {
  const watch = new IntersectionObserver(
    (entries) => {
      for (const { target, isIntersecting } of entries) {
        if (!isIntersecting) {
          if (!target.paused) target.pause();
          continue;
        }
        target.preload = "auto";
        if (!canHover && !reduced) target.play().catch(() => {});
      }
    },
    { threshold: 0.35, rootMargin: "200px 0px" },
  );
  clips.forEach((video) => watch.observe(video));
}

if (canHover) {
  clips.forEach((video) => {
    const panel = video.closest(".feature-panel");
    panel.addEventListener("pointerenter", () => {
      video.currentTime = 0;
      video.play().catch(() => {});
    });
    panel.addEventListener("pointerleave", () => {
      video.pause();
      video.load();
    });
  });
}

