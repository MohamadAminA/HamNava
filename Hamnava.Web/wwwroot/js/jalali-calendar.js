/**
 * HAMNAVA INTERACTIVE JALALI CALENDAR (Vanilla JS + Fetch AJAX)
 * Supports Week/Agenda browsing, Drag & Drop rescheduling with server-side conflict rollback,
 * and quick Attendance marking (Present / AuthorizedAbsence / UnauthorizedAbsence).
 */
(function () {
  "use strict";

  const root = document.getElementById("hn-jalali-calendar");
  if (!root) return;

  const eventsUrl = root.dataset.eventsUrl;
  const moveUrl = root.dataset.moveUrl;
  const attendanceUrl = root.dataset.attendanceUrl;
  const gridEl = document.getElementById("hn-cal-grid");
  const rangeLabelEl = document.getElementById("hn-cal-range-label");
  const roomFilterEl = document.getElementById("hn-cal-room-filter");

  let anchorDate = new Date();
  let currentEvents = [];

  const faDateFormatter = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
    weekday: "long",
    month: "long",
    day: "numeric"
  });

  function getWeekDays(baseDate) {
    const start = new Date(baseDate);
    // Align to Saturday (Shanbeh = 6 in JS Date.getDay())
    const day = start.getDay();
    const diffToSat = (day + 1) % 7;
    start.setDate(start.getDate() - diffToSat);
    start.setHours(0, 0, 0, 0);

    const days = [];
    for (let i = 0; i < 7; i++) {
      const d = new Date(start);
      d.setDate(start.getDate() + i);
      days.push(d);
    }
    return days;
  }

  async function loadEvents() {
    const days = getWeekDays(anchorDate);
    const startUtc = days[0].toISOString();
    const endDay = new Date(days[6]);
    endDay.setDate(endDay.getDate() + 1);
    const endUtc = endDay.toISOString();

    const params = new URLSearchParams({ startUtc, endUtc });
    if (roomFilterEl && roomFilterEl.value) {
      params.set("roomId", roomFilterEl.value);
    }

    if (rangeLabelEl) {
      rangeLabelEl.textContent = `${faDateFormatter.format(days[0])} تا ${faDateFormatter.format(days[6])}`;
    }

    try {
      const resp = await fetch(`${eventsUrl}?${params.toString()}`);
      if (!resp.ok) throw new Error("خطا در بارگذاری تقویم");
      currentEvents = await resp.json();
      renderWeek(days, currentEvents);
    } catch {
      window.HamnavaToast("بارگذاری جلسات تقویم با خطا مواجه شد.", "error");
    }
  }

  function renderWeek(days, events) {
    gridEl.innerHTML = "";
    days.forEach((dayDate) => {
      const isoDay = dayDate.toISOString().slice(0, 10);
      const col = document.createElement("div");
      col.className = "hn-cal-day-col";
      col.dataset.isoDay = isoDay;

      const header = document.createElement("div");
      header.className = "hn-cal-day-header";
      header.textContent = faDateFormatter.format(dayDate);
      col.appendChild(header);

      col.addEventListener("dragover", (e) => {
        e.preventDefault();
        col.classList.add("is-drop-target");
      });

      col.addEventListener("dragleave", () => col.classList.remove("is-drop-target"));

      col.addEventListener("drop", async (e) => {
        e.preventDefault();
        col.classList.remove("is-drop-target");
        const sessionId = e.dataTransfer.getData("text/plain");
        const ev = currentEvents.find((x) => x.sessionId === sessionId);
        if (!ev) return;

        // Preserve original Tehran time-of-day on the newly dropped date
        const origDate = new Date(ev.startAtUtc);
        const target = new Date(`${isoDay}T00:00:00Z`);
        target.setUTCHours(origDate.getUTCHours(), origDate.getUTCMinutes(), 0, 0);

        await moveSessionAjax(sessionId, target.toISOString());
      });

      const dayEvents = events.filter((ev) => ev.startAtUtc.slice(0, 10) === isoDay);
      if (!dayEvents.length) {
        const empty = document.createElement("div");
        empty.style.cssText = "font-size:0.8rem;color:var(--hn-text-muted);text-align:center;padding:1.25rem 0;";
        empty.textContent = "بدون جلسه";
        col.appendChild(empty);
      } else {
        dayEvents.forEach((ev) => col.appendChild(buildEventCard(ev)));
      }

      gridEl.appendChild(col);
    });
  }

  function buildEventCard(ev) {
    const card = document.createElement("div");
    card.className = "hn-cal-event";
    card.draggable = true;
    card.dataset.sessionId = ev.sessionId;
    card.dataset.status = ev.status;

    card.innerHTML = `
      <div style="display:flex;justify-content:space-between;align-items:center;gap:0.35rem;margin-bottom:0.25rem;">
        <strong>${escapeHtml(ev.studentFullName)}</strong>
        <span class="hn-badge hn-badge-info">${escapeHtml(ev.startTimeTehran)}</span>
      </div>
      <div style="color:var(--hn-text-muted);font-size:0.78rem;">
        ${escapeHtml(ev.instrumentName)} • اتاق ${escapeHtml(ev.roomName)}
      </div>
      <div style="display:flex;gap:0.35rem;margin-top:0.5rem;flex-wrap:wrap;">
        <button type="button" class="hn-btn hn-btn-teal hn-btn-sm" data-att="Present" style="padding:0.2rem 0.55rem;font-size:0.75rem;">حاضر</button>
        <button type="button" class="hn-btn hn-btn-outline hn-btn-sm" data-att="AuthorizedAbsence" style="padding:0.2rem 0.55rem;font-size:0.75rem;">غیبت موجه</button>
        <button type="button" class="hn-btn hn-btn-outline hn-btn-sm" data-att="UnauthorizedAbsence" style="padding:0.2rem 0.55rem;font-size:0.75rem;">غیبت غیرموجه</button>
      </div>`;

    card.addEventListener("dragstart", (e) => {
      card.classList.add("is-dragging");
      e.dataTransfer.setData("text/plain", ev.sessionId);
    });

    card.addEventListener("dragend", () => card.classList.remove("is-dragging"));

    card.querySelectorAll("[data-att]").forEach((btn) => {
      btn.addEventListener("click", async () => {
        await markAttendanceAjax(ev.sessionId, btn.dataset.att);
      });
    });

    return card;
  }

  async function moveSessionAjax(sessionId, newStartAtUtc) {
    try {
      const resp = await fetch(moveUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "RequestVerificationToken": window.HamnavaCsrfToken()
        },
        body: JSON.stringify({
          sessionId,
          newStartAtUtc,
          newRoomId: roomFilterEl && roomFilterEl.value ? roomFilterEl.value : null,
          reason: "جابجایی از طریق کشیدن و رها کردن در تقویم"
        })
      });

      const data = await resp.json();
      if (!resp.ok || !data.succeeded) {
        window.HamnavaToast(data.message || "تداخل زمانی یا ظرفیت اتاق اجازه جابجایی نداد.", "error");
        await loadEvents(); // Instant visual rollback
        return;
      }

      window.HamnavaToast(data.message || "زمان جلسه با موفقیت تغییر کرد.", "success");
      await loadEvents();
    } catch {
      window.HamnavaToast("خطا در ارتباط با سرور برای تغییر زمان جلسه.", "error");
      await loadEvents();
    }
  }

  async function markAttendanceAjax(sessionId, statusName) {
    const statusMap = { Present: 1, AuthorizedAbsence: 2, UnauthorizedAbsence: 3 };
    try {
      const resp = await fetch(attendanceUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "RequestVerificationToken": window.HamnavaCsrfToken()
        },
        body: JSON.stringify({ sessionId, status: statusMap[statusName], notes: null })
      });
      const data = await resp.json();
      if (!resp.ok || !data.succeeded) {
        window.HamnavaToast(data.message || "ثبت حضور و غیاب ناموفق بود.", "error");
        return;
      }
      window.HamnavaToast(data.message || "وضعیت حضور و غیاب ثبت شد.", "success");
      await loadEvents();
    } catch {
      window.HamnavaToast("خطا در ثبت حضور و غیاب.", "error");
    }
  }

  function escapeHtml(str) {
    const div = document.createElement("div");
    div.textContent = str ?? "";
    return div.innerHTML;
  }

  document.getElementById("hn-cal-prev")?.addEventListener("click", () => {
    anchorDate.setDate(anchorDate.getDate() - 7);
    loadEvents();
  });

  document.getElementById("hn-cal-today")?.addEventListener("click", () => {
    anchorDate = new Date();
    loadEvents();
  });

  document.getElementById("hn-cal-next")?.addEventListener("click", () => {
    anchorDate.setDate(anchorDate.getDate() + 7);
    loadEvents();
  });

  roomFilterEl?.addEventListener("change", loadEvents);

  loadEvents();
})();
