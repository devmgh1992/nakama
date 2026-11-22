/* Gayvelopers console skin — logo, copy, favicon and route flags.
   Kept defensive: the console is a Vue SPA, so every element can disappear
   between renders; we re-apply on a debounced MutationObserver. */
(function () {
  "use strict";

  var ICON = "/branding/icon.svg?v=1";
  var LOGO = "/branding/logo.svg?v=2";
  var LOGO_DARK = "/branding/logo-dark.svg?v=2";

  function iconLink() {
    var links = document.querySelectorAll('link[rel~="icon"]');
    var found = null;
    links.forEach(function (l) {
      if (!found && l.getAttribute("href") === ICON) found = l;
      else l.parentNode.removeChild(l);
    });
    if (!found) {
      found = document.createElement("link");
      found.rel = "icon";
      found.type = "image/svg+xml";
      found.href = ICON;
      document.head.appendChild(found);
    }
  }

  function isLogin() {
    return !!document.querySelector('form input[name="username"]');
  }

  function brandLogo() {
    var img = document.querySelector("img.h-10");
    if (img && img.getAttribute("src") !== LOGO) {
      img.setAttribute("src", LOGO);
      img.setAttribute("alt", "Gayvelopers");
      img.style.height = "40px";
      img.style.width = "auto";
    }
  }

  // Sidebar / header wordmark inside the console: same data-URI SVG, swap the src.
  function brandSidebar() {
    document.querySelectorAll('a[href="#/"] img, img.h-10, img.h-7').forEach(function (img) {
      if (img.dataset.gvLogo === "1") return;
      var src = img.getAttribute("src") || "";
      if (src.indexOf("data:image/svg+xml") !== 0) return;
      img.dataset.gvLogo = "1";
      img.removeAttribute("srcset");
      img.setAttribute("src", LOGO);
      img.setAttribute("alt", "Gayvelopers");
      var inSidebar = img.classList.contains("h-7") || !!img.closest("a[href='#/']");
      var h = inSidebar ? "28px" : "40px";
      if (inSidebar) img.setAttribute("src", LOGO_DARK);
      img.style.height = h;
      img.style.width = "auto";
    });
  }

  function brandCopy() {
    var h1 = document.querySelector("h1");
    if (!h1 || h1.dataset.gvDone === "1") return;
    if (!/Nakama Dashboard/i.test(h1.textContent || "")) return;

    h1.textContent = "Sign in to your Gayvelopers backend";
    h1.dataset.gvDone = "1";

    var sub = h1.parentNode.querySelector(".gv-sub");
    if (!sub) {
      sub = document.createElement("p");
      sub.className = "gv-sub";
      sub.textContent = "Private Nakama console · accounts, leaderboards and storage for your games";
      h1.insertAdjacentElement("afterend", sub);
    }
  }

  function brandBadge() {
    if (document.querySelector(".gv-badge")) return;
    var form = document.querySelector('form input[name="username"]');
    if (!form) return;
    var card = form.closest("div.grid") || form.closest("div");
    if (!card) return;

    var badge = document.createElement("div");
    badge.className = "gv-badge";
    badge.innerHTML =
      '<img src="' + ICON + '" alt="">' +
      '<span><b>Gayvelopers</b> · private server</span>' +
      '<span style="margin-left:auto;display:inline-flex;align-items:center;gap:6px">' +
      '<span class="gv-dot"></span>online</span>';
    card.appendChild(badge);
  }

  function apply() {
    try {
      iconLink();
      brandLogo();
      brandSidebar();
      brandCopy();
      brandBadge();
      var login = isLogin();
      document.documentElement.dataset.gvRoute = login ? "login" : "app";
      document.title = login
        ? "Gayvelopers · Sign in"
        : "Gayvelopers · Nakama Console";
    } catch (e) {
      /* never break the console because of branding */
    }
  }

  var pending = null;
  function schedule() {
    if (pending) return;
    pending = setTimeout(function () {
      pending = null;
      apply();
    }, 150);
  }

  function boot() {
    apply();
    var app = document.getElementById("app") || document.body;
    new MutationObserver(schedule).observe(app, { childList: true, subtree: true });
    window.addEventListener("hashchange", schedule);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
