// GoCar — etkileşimler ve animasyonlar
(() => {
  const root = document.documentElement;
  const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const hasGsap = !!window.gsap && !!window.ScrollTrigger;
  root.classList.add("js");
  if (reduced || !hasGsap) root.classList.add("reduced");

  const $ = (s, el = document) => el.querySelector(s);
  const $$ = (s, el = document) => [...el.querySelectorAll(s)];

  // ---------- Yumuşak kaydırma ----------
  let lenis = null;
  if (!reduced && window.Lenis && hasGsap) {
    lenis = new Lenis({ duration: 1.15, smoothWheel: true });
    window.__lenis = lenis;
    lenis.on("scroll", ScrollTrigger.update);
    gsap.ticker.add(t => lenis.raf(t * 1000));
    gsap.ticker.lagSmoothing(0);
    $$('a[href^="#"], a[href^="/#"]').forEach(a => a.addEventListener("click", e => {
      const hash = a.getAttribute("href").replace(/^\//, "");
      if (location.pathname !== "/" && a.getAttribute("href").startsWith("/#")) return;
      const target = hash.length > 1 && document.querySelector(hash);
      if (!target) return;
      e.preventDefault();
      lenis.scrollTo(target, { offset: -90 });
      closeMenu();
    }));
  }

  // ---------- Üst menü ----------
  const header = $("[data-header]");
  const progress = $(".scroll-progress");
  let lastY = 0;
  const onScroll = () => {
    const y = window.scrollY;
    header?.classList.toggle("is-scrolled", y > 30);
    header?.classList.toggle("is-hidden", y > 400 && y > lastY && !document.body.classList.contains("menu-open"));
    lastY = y;
    const max = document.documentElement.scrollHeight - innerHeight;
    if (progress) progress.style.transform = `scaleX(${max > 0 ? y / max : 0})`;
  };
  addEventListener("scroll", onScroll, { passive: true });
  onScroll();

  const menu = $("[data-mobile-menu]");
  const toggle = $("[data-menu-toggle]");
  function closeMenu() {
    if (!menu || menu.hidden) return;
    menu.hidden = true;
    document.body.classList.remove("menu-open");
    toggle?.setAttribute("aria-expanded", "false");
    toggle.innerHTML = '<svg class="i"><use href="#i-menu"/></svg>';
    lenis?.start();
  }
  toggle?.addEventListener("click", () => {
    if (!menu.hidden) return closeMenu();
    menu.hidden = false;
    document.body.classList.add("menu-open");
    toggle.setAttribute("aria-expanded", "true");
    toggle.innerHTML = '<svg class="i"><use href="#i-x"/></svg>';
    lenis?.stop();
    if (hasGsap && !reduced) gsap.from($$("nav a, .btn", menu), { y: 40, opacity: 0, stagger: .05, duration: .6, ease: "power3.out" });
  });
  $$("a", menu || document.createElement("div")).forEach(a => a.addEventListener("click", closeMenu));
  addEventListener("keydown", e => e.key === "Escape" && closeMenu());

  // ---------- Filtre seçimleri (select → sayfa) ----------
  $$("select[data-nav]").forEach(s => s.addEventListener("change", () => location.href = s.value));

  // ---------- Tarih seçiciler ----------
  const iso = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
  const parse = v => { const [y, m, d] = v.split("-").map(Number); return new Date(y, m - 1, d); };
  const dayDiff = (a, b) => Math.round((parse(b) - parse(a)) / 864e5);

  $$("[data-search]").forEach(form => {
    const pickup = $('[data-date="pickup"]', form);
    const ret = $('[data-date="return"]', form);
    const pTime = $('[data-time="pickup"]', form);
    const rTime = $('[data-time="return"]', form);
    const pBranch = $("[data-pickup-branch]", form);
    const rBranch = $("[data-return-branch]", form);
    const different = $("[data-different-return]", form);
    const returnField = $("[data-return-field]", form);
    const daysLabel = $("[data-days-label]", form);
    if (!pickup || !ret) return;

    const today = iso(new Date());
    pickup.min = today;
    ret.min = pickup.value || today;

    const updateDays = () => {
      if (!daysLabel || !pickup.value || !ret.value) return;
      const ph = +pTime.value.split(":")[0] + pTime.value.split(":")[1] / 60;
      const rh = +rTime.value.split(":")[0] + rTime.value.split(":")[1] / 60;
      const hours = dayDiff(pickup.value, ret.value) * 24 + rh - ph;
      const days = Math.max(1, Math.ceil((hours - 1) / 24));
      daysLabel.textContent = `${days} gün`;
    };

    let fpReturn = null;
    const syncReturn = () => {
      if (!pickup.value) return;
      if (!ret.value || ret.value <= pickup.value) {
        const d = parse(pickup.value); d.setDate(d.getDate() + 3);
        ret.value = iso(d);
        fpReturn?.setDate(ret.value, false);
      }
      ret.min = pickup.value;
      fpReturn?.set("minDate", pickup.value);
      updateDays();
      form.dispatchEvent(new Event("trip-change"));
    };

    if (window.flatpickr) {
      const opts = { locale: "tr", altInput: true, altFormat: "j M D", dateFormat: "Y-m-d", disableMobile: true, minDate: "today" };
      const fpPickup = flatpickr(pickup, { ...opts, onChange: () => { syncReturn(); setTimeout(() => fpReturn?.open(), 80); } });
      fpReturn = flatpickr(ret, { ...opts, minDate: pickup.value || "today", onChange: () => { updateDays(); form.dispatchEvent(new Event("trip-change")); } });
      fpPickup.altInput.setAttribute("aria-label", "Alış tarihi");
      fpReturn.altInput.setAttribute("aria-label", "İade tarihi");
    } else {
      pickup.addEventListener("change", syncReturn);
      ret.addEventListener("change", () => { updateDays(); form.dispatchEvent(new Event("trip-change")); });
    }
    [pTime, rTime, pBranch, rBranch].forEach(el => el?.addEventListener("change", () => { updateDays(); form.dispatchEvent(new Event("trip-change")); }));

    if (different && rBranch) {
      const apply = () => {
        returnField.hidden = !different.checked;
        if (!different.checked) rBranch.value = pBranch.value;
      };
      different.addEventListener("change", apply);
      pBranch.addEventListener("change", () => { if (!different.checked) rBranch.value = pBranch.value; });
      apply();
    }
    updateDays();
  });

  // ---------- Canlı fiyat ----------
  $$("[data-quote-form]").forEach(box => {
    const car = box.dataset.car;
    const form = box.tagName === "FORM" ? box : $("form", box);
    const q = name => $(`[data-q="${name}"]`, box);
    const row = name => $(`[data-q-row="${name}"]`, box);
    const breakdowns = $$(".breakdown", box);
    let timer, controller;

    const run = async () => {
      const data = new FormData(form);
      const params = new URLSearchParams({ arac: car });
      const pick = (...names) => { for (const n of names) { const v = data.get(n); if (v !== null) return v; } return ""; };
      params.set("sube", pick("sube", "Sube"));
      params.set("donusSube", pick("donusSube", "DonusSube"));
      params.set("alis", pick("alis", "Alis"));
      params.set("alisSaat", pick("alisSaat", "AlisSaat"));
      params.set("donus", pick("donus", "Donus"));
      params.set("donusSaat", pick("donusSaat", "DonusSaat"));
      data.getAll("Ek").forEach(e => params.append("ek", e));

      controller?.abort();
      controller = new AbortController();
      breakdowns.forEach(b => b.classList.add("is-loading"));
      try {
        const res = await fetch(`/api/teklif?${params}`, { signal: controller.signal, headers: { Accept: "application/json" } });
        if (!res.ok) throw new Error(res.status);
        const r = await res.json();
        const err = q("error");
        if (!r.ok) {
          if (err) { err.hidden = false; err.textContent = r.error; }
          q("submit")?.setAttribute("disabled", "");
          return;
        }
        if (err) err.hidden = true;
        const set = (k, v) => { const el = q(k); if (el && v != null) el.textContent = v; };
        set("days", r.days); set("baseTotal", r.baseTotal); set("total", r.total); set("deposit", r.deposit);
        set("pickup", r.pickup); set("return", r.return);
        const dr = row("discount"); if (dr) { dr.hidden = !r.discount; set("discount", r.discount); }
        const ow = row("oneWay"); if (ow) { ow.hidden = !r.oneWay; set("oneWay", r.oneWay); }
        const list = $('[data-q-list="extras"]', box);
        if (list) list.innerHTML = r.extras.map(e => `<div><dt>${e.label}</dt><dd>${e.amount}</dd></div>`).join("");
        const av = q("availability");
        if (av) { av.textContent = r.available ? "Müsait" : "Dolu"; av.className = `avail ${r.available ? "is-ok" : "is-full"}`; }
        const un = q("unavailable");
        if (un) {
          un.hidden = r.available;
          const nf = q("nextFree");
          if (nf) nf.innerHTML = r.nextFree ? `En yakın uygun başlangıç: <a href="/arac/${car}?${r.nextFree.query}#musaitlik">${r.nextFree.label}</a>` : "";
        }
        const btn = q("submit");
        if (btn) btn.disabled = !r.available;
        pulse(q("total"));
      } catch (e) {
        if (e.name !== "AbortError") console.warn("Fiyat alınamadı", e);
      } finally {
        breakdowns.forEach(b => b.classList.remove("is-loading"));
      }
    };
    const schedule = () => { clearTimeout(timer); timer = setTimeout(run, 180); };
    $$("[data-search]", box.tagName === "FORM" ? box.parentElement : box).forEach(s => s.addEventListener("trip-change", schedule));
    if (box.matches("[data-search]") || $("[data-search]", box) === null) box.addEventListener("trip-change", schedule);
    $$('input[name="Ek"]', box).forEach(i => i.addEventListener("change", schedule));
  });

  function pulse(el) {
    if (!el || !hasGsap || reduced) return;
    gsap.fromTo(el, { scale: 1.08, color: "#F2A900" }, { scale: 1, color: "", duration: .6, ease: "power3.out" });
  }

  // ---------- Diyaloglar ----------
  $$("[data-open-dialog]").forEach(b => b.addEventListener("click", () => document.getElementById(b.dataset.openDialog)?.showModal()));
  $$("[data-close-dialog]").forEach(b => b.addEventListener("click", () => b.closest("dialog")?.close()));

  // ---------- SSS akordeonu (yumuşak açılış) ----------
  $$("details.acc").forEach(d => {
    const body = $(".acc-body", d);
    $("summary", d).addEventListener("click", e => {
      if (!hasGsap || reduced) return;
      e.preventDefault();
      if (d.open) {
        gsap.to(body, { height: 0, duration: .4, ease: "power3.inOut", onComplete: () => { d.open = false; body.style.height = ""; } });
      } else {
        d.open = true;
        gsap.fromTo(body, { height: 0 }, { height: body.scrollHeight, duration: .5, ease: "power3.out", onComplete: () => body.style.height = "" });
      }
    });
  });

  if (reduced || !hasGsap) return;

  // ================= GSAP animasyonları =================
  gsap.registerPlugin(ScrollTrigger);
  gsap.defaults({ ease: "power3.out" });

  // Başlıkları kelime kelime böl
  const splitWords = el => {
    const walk = node => {
      [...node.childNodes].forEach(n => {
        if (n.nodeType === 3) {
          const frag = document.createDocumentFragment();
          n.textContent.split(/(\s+)/).forEach(part => {
            if (!part) return;
            if (/^\s+$/.test(part)) { frag.append(" "); return; }
            const line = document.createElement("span"); line.className = "split-line"; line.style.display = "inline-block";
            const w = document.createElement("span"); w.className = "split-word"; w.textContent = part;
            line.append(w); frag.append(line);
          });
          n.replaceWith(frag);
        } else if (n.nodeType === 1 && !n.classList.contains("split-line")) {
          const wrap = document.createElement("span"); wrap.className = "split-line"; wrap.style.display = "inline-block";
          n.classList.add("split-word"); n.replaceWith(wrap); wrap.append(n);
        }
      });
    };
    walk(el);
    return $$(".split-word", el);
  };

  $$("[data-split]").forEach(el => {
    const words = splitWords(el);
    const inHero = el.closest("[data-hero], .corp-hero");
    gsap.from(words, {
      yPercent: 110, rotate: 4, duration: 1.1, stagger: .06, delay: inHero ? .15 : .05, ease: "power4.out",
      scrollTrigger: inHero ? null : { trigger: el, start: "top 90%" },
    });
  });

  // Hero
  const heroIn = $$("[data-hero-in]");
  if (heroIn.length) gsap.fromTo(heroIn, { y: 40, opacity: 0 }, { y: 0, opacity: 1, duration: 1.1, stagger: .12, delay: .45 });
  $$("[data-hero-img]").forEach(img => {
    gsap.fromTo(img, { scale: 1.18 }, { scale: 1, duration: 2.2, ease: "power2.out" });
    gsap.to(img, { yPercent: 14, ease: "none", scrollTrigger: { trigger: img.closest("section"), start: "top top", end: "bottom top", scrub: true } });
  });

  // Genel giriş animasyonları
  ScrollTrigger.batch("[data-anim='fade-up']", {
    start: "top 90%",
    onEnter: els => gsap.fromTo(els, { y: 50, opacity: 0 }, { y: 0, opacity: 1, duration: 1, stagger: .08, overwrite: true }),
  });
  ScrollTrigger.batch("[data-anim='card']", {
    start: "top 92%",
    onEnter: els => gsap.fromTo(els, { y: 70, opacity: 0, scale: .96 }, { y: 0, opacity: 1, scale: 1, duration: 1.1, stagger: .09, overwrite: true }),
  });
  // Yatay şeritteki kartlar görünür başlar (pin animasyonu kendisi hareket ettirir)
  $$("[data-rail-track] [data-anim]").forEach(el => el.style.opacity = 1);

  // Sayaçlar
  $$("[data-count]").forEach(el => {
    const end = parseFloat(el.dataset.count);
    const dec = +(el.dataset.decimals || 0);
    const obj = { v: 0 };
    gsap.to(obj, {
      v: end, duration: 2, ease: "power2.out",
      scrollTrigger: { trigger: el, start: "top 92%" },
      onUpdate: () => el.textContent = obj.v.toLocaleString("tr-TR", { minimumFractionDigits: dec, maximumFractionDigits: dec }),
    });
  });

  // Kelime kelime aydınlanan paragraf
  $$("[data-reveal-words]").forEach(p => {
    p.innerHTML = p.textContent.trim().split(/\s+/).map(w => `<span class="w">${w}</span>`).join(" ");
    const words = $$(".w", p);
    ScrollTrigger.create({
      trigger: p, start: "top 80%", end: "bottom 45%", scrub: true,
      onUpdate: self => {
        const n = Math.round(self.progress * words.length);
        words.forEach((w, i) => w.classList.toggle("is-on", i < n));
      },
    });
  });

  // Yatay filo şeridi: masaüstünde sabitlenip yana kayar
  const mm = gsap.matchMedia();
  mm.add("(min-width: 721px)", () => {
    const rail = $("[data-rail]");
    const track = $("[data-rail-track]");
    if (!rail || !track) return;
    const distance = () => Math.max(0, track.scrollWidth - innerWidth + 48);
    const tween = gsap.to(track, {
      x: () => -distance(), ease: "none",
      scrollTrigger: { trigger: rail, start: "top top", end: () => "+=" + distance(), pin: true, scrub: 1, invalidateOnRefresh: true, anticipatePin: 1 },
    });
    $$(".car-card img", track).forEach(img => gsap.fromTo(img, { xPercent: -6, scale: 1.12 }, {
      xPercent: 6, ease: "none", scrollTrigger: { trigger: img, containerAnimation: tween, start: "left right", end: "right left", scrub: true },
    }));
  });

  // Adımlar: ilerleme çizgisi + aktif adım
  const stepsLine = $("[data-steps-line]");
  if (stepsLine) {
    gsap.to(stepsLine, { scaleY: 1, ease: "none", scrollTrigger: { trigger: "[data-steps]", start: "top 60%", end: "bottom 60%", scrub: true } });
    $$(".step").forEach(step => ScrollTrigger.create({ trigger: step, start: "top 62%", onToggle: s => step.classList.toggle("is-on", s.isActive || s.progress > 0), end: "max" }));
    gsap.from(".step", { x: 40, opacity: 0, duration: .9, stagger: .12, scrollTrigger: { trigger: "[data-steps]", start: "top 80%" } });
  }

  // Görsel açılışı (perde) + paralaks
  $$("[data-clip-reveal]").forEach(el => gsap.fromTo(el, { clipPath: "inset(18% 12% 18% 12% round 28px)" }, {
    clipPath: "inset(0% 0% 0% 0% round 28px)", duration: 1.5, ease: "power3.inOut", scrollTrigger: { trigger: el, start: "top 85%" },
  }));
  $$("[data-parallax]").forEach(img => gsap.fromTo(img, { yPercent: -8 }, {
    yPercent: 0, ease: "none", scrollTrigger: { trigger: img.parentElement, start: "top bottom", end: "bottom top", scrub: true },
  }));

  // Kategori kartları hafif paralaks
  $$(".cat-tile img").forEach(img => gsap.fromTo(img, { yPercent: -6, scale: 1.12 }, {
    yPercent: 6, ease: "none", scrollTrigger: { trigger: img.parentElement, start: "top bottom", end: "bottom top", scrub: true },
  }));

  // Footer başlığı
  const fcta = $(".footer-cta h2");
  if (fcta) gsap.from(splitWords(fcta), { yPercent: 110, duration: 1, stagger: .05, ease: "power4.out", scrollTrigger: { trigger: fcta, start: "top 90%" } });

  // Mıknatıs butonlar
  if (matchMedia("(pointer: fine)").matches) {
    $$(".magnetic").forEach(btn => {
      const xTo = gsap.quickTo(btn, "x", { duration: .5, ease: "power3" });
      const yTo = gsap.quickTo(btn, "y", { duration: .5, ease: "power3" });
      btn.addEventListener("mousemove", e => {
        const r = btn.getBoundingClientRect();
        xTo((e.clientX - r.left - r.width / 2) * .25);
        yTo((e.clientY - r.top - r.height / 2) * .35);
      });
      btn.addEventListener("mouseleave", () => { xTo(0); yTo(0); });
    });
  }

  // Görseller yüklenince ölçüleri yenile
  addEventListener("load", () => ScrollTrigger.refresh());
})();
