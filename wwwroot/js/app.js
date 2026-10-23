(function () {
  "use strict";

  const fmt = new Intl.NumberFormat("tr-TR");
  const today = new Date();
  today.setMinutes(today.getMinutes() - today.getTimezoneOffset());
  const todayIso = today.toISOString().slice(0, 10);

  // Geçmiş tarihleri seçtirme
  document.querySelectorAll("[data-min-today]").forEach(el => el.min = todayIso);

  // Dönüş tarihi en az alıştan bir gün sonra olsun
  function pairDates(pickup, ret) {
    if (!pickup || !ret) return;
    const sync = () => {
      if (!pickup.value) return;
      const next = new Date(pickup.value);
      next.setDate(next.getDate() + 1);
      const min = next.toISOString().slice(0, 10);
      ret.min = min;
      if (!ret.value || ret.value < min) ret.value = min;
    };
    pickup.addEventListener("change", sync);
    sync();
  }
  pairDates(document.getElementById("alis"), document.getElementById("donus"));
  pairDates(document.getElementById("r-pickup"), document.getElementById("r-return"));

  // Rezervasyon penceresi: seçilen aracı ve toplam tutarı göster
  const modal = document.getElementById("reserveModal");
  if (modal) {
    let price = 0;
    const pickup = document.getElementById("r-pickup");
    const ret = document.getElementById("r-return");
    const update = () => {
      const days = Math.round((new Date(ret.value) - new Date(pickup.value)) / 86400000);
      const ok = days > 0 && Number.isFinite(days);
      document.getElementById("r-days").textContent = ok ? `${days} gün × ${fmt.format(price)} ₺` : "Tarihleri kontrol edin";
      document.getElementById("r-total").textContent = ok ? `${fmt.format(days * price)} ₺` : "—";
    };
    modal.addEventListener("show.bs.modal", e => {
      const btn = e.relatedTarget;
      price = Number(btn.dataset.carPrice);
      document.getElementById("r-car-id").value = btn.dataset.carId;
      document.getElementById("reserveTitle").textContent = btn.dataset.carName;
      document.getElementById("r-car-image").src = btn.dataset.carImage;
      // Arama formundaki tarih ve noktayı taşı
      const a = document.getElementById("alis"), d = document.getElementById("donus"), k = document.getElementById("konum");
      if (a && a.value) pickup.value = a.value;
      if (d && d.value) ret.value = d.value;
      if (k) document.getElementById("r-location").value = k.value;
      pickup.dispatchEvent(new Event("change"));
      update();
    });
    pickup.addEventListener("change", update);
    ret.addEventListener("change", update);
  }

  // Uzun dönem paketleri: aylık / yıllık
  const sw = document.getElementById("flexSwitchCheckChecked");
  if (sw) {
    const apply = () => {
      document.querySelectorAll(".pricing-content .content").forEach(el =>
        el.style.display = el.classList.contains(sw.checked ? "yearly" : "monthly") ? "flex" : "none");
      document.getElementById("yearly-label").classList.toggle("label-color", sw.checked);
      document.getElementById("monthly-label").classList.toggle("label-color", !sw.checked);
    };
    sw.addEventListener("change", apply);
    apply();
  }

  // Kaydırınca üst menüye gölge
  const nav = document.getElementById("header-nav");
  const onScroll = () => nav && nav.classList.toggle("scrolled", window.scrollY > 10);
  window.addEventListener("scroll", onScroll, { passive: true });
  onScroll();

  if (window.Swiper) {
    new Swiper(".testimonial-swiper", { loop: true, autoplay: { delay: 6000 }, pagination: { el: ".swiper-pagination", clickable: true } });
  }
  if (window.AOS) AOS.init({ duration: 700, once: true, offset: 40 });
})();
