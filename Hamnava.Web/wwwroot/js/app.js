/**
 * HAMNAVA CORE VANILLA JS (Zero frameworks, Zero window.alert/confirm)
 * Handles: Mobile drawer, Toast notifications, Accessible Confirm Modal,
 * Figma "دنیای هم‌نوا" tabs, Teacher circular carousel, Referral copy,
 * and Searchable DB Referrer Picker.
 */
(function () {
  "use strict";

  // 1. Toast Notification Host
  function ensureToastHost() {
    let host = document.getElementById("hn-toast-host");
    if (!host) {
      host = document.createElement("div");
      host.id = "hn-toast-host";
      host.className = "hn-toast-host";
      host.setAttribute("role", "status");
      host.setAttribute("aria-live", "polite");
      document.body.appendChild(host);
    }
    return host;
  }

  window.HamnavaToast = function (message, type = "info") {
    const host = ensureToastHost();
    const toast = document.createElement("div");
    toast.className = "hn-toast";
    toast.dataset.type = type;

    const text = document.createElement("span");
    text.textContent = message;
    toast.appendChild(text);

    host.appendChild(toast);
    window.setTimeout(() => {
      toast.style.opacity = "0";
      toast.style.transition = "opacity 0.25s ease";
      window.setTimeout(() => toast.remove(), 260);
    }, 4200);
  };

  // 2. Anti-Forgery Token Reader for Fetch AJAX
  window.HamnavaCsrfToken = function () {
    const input = document.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : "";
  };

  // 3. Accessible Confirmation Modal (replaces native window.confirm)
  window.HamnavaConfirm = function (title, message) {
    return new Promise((resolve) => {
      let backdrop = document.getElementById("hn-confirm-backdrop");
      if (!backdrop) {
        backdrop = document.createElement("div");
        backdrop.id = "hn-confirm-backdrop";
        backdrop.className = "hn-modal-backdrop";
        backdrop.innerHTML = `
          <div class="hn-modal" role="dialog" aria-modal="true" aria-labelledby="hn-confirm-title">
            <h3 id="hn-confirm-title" style="margin:0 0 0.5rem;"></h3>
            <p id="hn-confirm-msg" style="margin:0;color:var(--hn-text-muted);"></p>
            <div class="hn-modal-actions">
              <button type="button" class="hn-btn hn-btn-outline hn-btn-sm" id="hn-confirm-cancel">انصراف</button>
              <button type="button" class="hn-btn hn-btn-espresso hn-btn-sm" id="hn-confirm-ok">تایید</button>
            </div>
          </div>`;
        document.body.appendChild(backdrop);
      }

      backdrop.querySelector("#hn-confirm-title").textContent = title;
      backdrop.querySelector("#hn-confirm-msg").textContent = message;
      backdrop.classList.add("is-open");

      const okBtn = backdrop.querySelector("#hn-confirm-ok");
      const cancelBtn = backdrop.querySelector("#hn-confirm-cancel");

      const cleanup = (result) => {
        backdrop.classList.remove("is-open");
        okBtn.onclick = null;
        cancelBtn.onclick = null;
        resolve(result);
      };

      okBtn.onclick = () => cleanup(true);
      cancelBtn.onclick = () => cleanup(false);
    });
  };

  // 4. Mobile Navigation Drawer
  function initMobileDrawer() {
    const openBtn = document.querySelector("[data-hn-drawer-open]");
    const drawer = document.getElementById("hn-mobile-drawer");
    if (!openBtn || !drawer) return;

    openBtn.addEventListener("click", () => drawer.classList.add("is-open"));
    drawer.addEventListener("click", (e) => {
      if (e.target === drawer || e.target.closest("[data-hn-drawer-close]")) {
        drawer.classList.remove("is-open");
      }
    });
  }

  // 5. Figma "دنیای هم‌نوا" Interactive 4-Tab Showcase
  function initWorldTabs() {
    const tabs = document.querySelectorAll("[data-world-tab]");
    const img = document.getElementById("hn-world-image");
    const caption = document.getElementById("hn-world-caption");
    const link = document.getElementById("hn-world-link");
    if (!tabs.length || !img) return;

    tabs.forEach((tab) => {
      tab.addEventListener("click", () => {
        tabs.forEach((t) => {
          t.classList.remove("is-active");
          t.setAttribute("aria-selected", "false");
        });
        tab.classList.add("is-active");
        tab.setAttribute("aria-selected", "true");

        img.style.opacity = "0.25";
        window.setTimeout(() => {
          img.src = tab.dataset.image || img.src;
          if (caption) caption.textContent = tab.dataset.caption || "";
          if (link && tab.dataset.url) link.setAttribute("href", tab.dataset.url);
          img.style.opacity = "0.92";
        }, 120);
      });
    });
  }

  // 6. Teacher Circular Carousel Controls
  function initCarousels() {
    document.querySelectorAll("[data-hn-carousel]").forEach((shell) => {
      const track = shell.querySelector(".hn-carousel-track");
      const prev = shell.querySelector("[data-carousel-prev]");
      const next = shell.querySelector("[data-carousel-next]");
      if (!track) return;

      const step = () => Math.max(240, track.clientWidth * 0.65);
      if (prev) prev.addEventListener("click", () => track.scrollBy({ left: step(), behavior: "smooth" }));
      if (next) next.addEventListener("click", () => track.scrollBy({ left: -step(), behavior: "smooth" }));
    });
  }

  // 7. Copy Buttons (Referral Link / Code)
  function initCopyButtons() {
    document.querySelectorAll("[data-copy-text]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        const text = btn.dataset.copyText || "";
        try {
          await navigator.clipboard.writeText(text);
          window.HamnavaToast("در حافظه کلیپ‌بورد کپی شد.", "success");
        } catch {
          window.HamnavaToast("امکان کپی خودکار نبود؛ متن را دستی انتخاب کنید.", "info");
        }
      });
    });
  }

  // 8. Searchable Database-Driven Referrer Picker
  function initReferrerPickers() {
    document.querySelectorAll("[data-referrer-picker]").forEach((wrap) => {
      const searchInput = wrap.querySelector("[data-picker-search]");
      const hiddenInput = wrap.querySelector("[data-picker-value]");
      const resultsBox = wrap.querySelector("[data-picker-results]");
      const endpoint = wrap.dataset.referrerPicker;
      if (!searchInput || !hiddenInput || !resultsBox || !endpoint) return;

      let timer = null;
      searchInput.addEventListener("input", () => {
        window.clearTimeout(timer);
        const q = searchInput.value.trim();
        timer = window.setTimeout(async () => {
          try {
            const resp = await fetch(`${endpoint}?q=${encodeURIComponent(q)}`);
            if (!resp.ok) return;
            const items = await resp.json();
            resultsBox.innerHTML = "";
            if (!items.length) {
              resultsBox.innerHTML = '<div style="padding:0.65rem;color:var(--hn-text-muted);font-size:0.85rem;">معرفی یافت نشد.</div>';
              return;
            }
            items.forEach((item) => {
              const btn = document.createElement("button");
              btn.type = "button";
              btn.className = "hn-picker-item";
              btn.textContent = `${item.fullName} — ${item.mobile}${item.referralCode ? " (" + item.referralCode + ")" : ""}`;
              btn.addEventListener("click", () => {
                hiddenInput.value = item.id;
                searchInput.value = item.fullName;
                resultsBox.innerHTML = "";
              });
              resultsBox.appendChild(btn);
            });
          } catch {
            window.HamnavaToast("خطا در جستجوی معرف‌ها.", "error");
          }
        }, 250);
      });
    });
  }

  // 9. Confirm-guarded forms (e.g. quota reset / withdrawals)
  function initConfirmForms() {
    document.querySelectorAll("form[data-confirm-message]").forEach((form) => {
      form.addEventListener("submit", async (e) => {
        if (form.dataset.confirmed === "true") return;
        e.preventDefault();
        const ok = await window.HamnavaConfirm(
          form.dataset.confirmTitle || "تایید عملیات",
          form.dataset.confirmMessage
        );
        if (ok) {
          form.dataset.confirmed = "true";
          form.submit();
        }
      });
    });
  }

  document.addEventListener("DOMContentLoaded", () => {
    initMobileDrawer();
    initWorldTabs();
    initCarousels();
    initCopyButtons();
    initReferrerPickers();
    initConfirmForms();
  });
})();
