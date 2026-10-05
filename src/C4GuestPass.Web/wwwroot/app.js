'use strict';
/* GuestPass – aplikacja recepcji i najemców (vanilla JS, bez zależności) */

const $ = (sel, root = document) => root.querySelector(sel);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const ROLE = { BuildingAdmin: 'Administrator budynku', CompanyAdmin: 'Administrator firmy', Host: 'Pracownik' };

const I = {
  visits: '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="2" y="2.5" width="12" height="11" rx="1.5"/><path d="M2 6h12M5.5 1v3M10.5 1v3"/></svg>',
  users: '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="6" cy="5.5" r="2.5"/><path d="M1.5 14c.4-2.6 2.2-4 4.5-4s4.1 1.4 4.5 4M11 3.2a2.4 2.4 0 0 1 0 4.6M12.5 10.3c1.2.6 1.9 1.9 2 3.7"/></svg>',
  companies: '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M2.5 14.5v-12h7v12M9.5 6.5h4v8M1 14.5h14M5 5h2M5 8h2M5 11h2M11.5 9h0M11.5 11.5h0"/></svg>',
  plus: '<svg width="14" height="14" viewBox="0 0 14 14" fill="none" stroke="currentColor" stroke-width="1.7"><path d="M7 2v10M2 7h10"/></svg>',
  more: '<svg width="16" height="16" viewBox="0 0 16 16" fill="currentColor"><circle cx="3.5" cy="8" r="1.3"/><circle cx="8" cy="8" r="1.3"/><circle cx="12.5" cy="8" r="1.3"/></svg>',
  x: '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M3.5 3.5l9 9M12.5 3.5l-9 9"/></svg>',
  search: '<svg width="14" height="14" viewBox="0 0 14 14" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="6" cy="6" r="4.3"/><path d="M9.3 9.3L13 13"/></svg>',
  qr: '<svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4"><rect x="2" y="2" width="4.5" height="4.5"/><rect x="9.5" y="2" width="4.5" height="4.5"/><rect x="2" y="9.5" width="4.5" height="4.5"/><path d="M9.5 9.5h2v2M14 9.5v4.5h-4.5"/></svg>',
  mail: '<svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4"><rect x="1.5" y="3" width="13" height="10" rx="1.2"/><path d="M2 4l6 5 6-5"/></svg>',
  ban: '<svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4"><circle cx="8" cy="8" r="6"/><path d="M3.8 12.2l8.4-8.4"/></svg>',
  edit: '<svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4"><path d="M10.5 2.5l3 3L6 13H3v-3z"/></svg>',
  key: '<svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4"><circle cx="5" cy="11" r="3"/><path d="M7.2 8.8L14 2M11.5 4.5l2 2"/></svg>',
  out: '<svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4"><path d="M6 2.5H3v11h3M10 5l3 3-3 3M13 8H6"/></svg>',
  gear: '<svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="8" cy="8" r="2.2"/><path d="M8 1.5v2M8 12.5v2M1.5 8h2M12.5 8h2M3.4 3.4l1.4 1.4M11.2 11.2l1.4 1.4M3.4 12.6l1.4-1.4M11.2 4.8l1.4-1.4"/></svg>',
  trash: '<svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4"><path d="M2.5 4h11M6 4V2.5h4V4M4 4l.7 9.5h6.6L12 4"/></svg>',
  copy: '<svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4"><rect x="5" y="5" width="9" height="9" rx="1"/><path d="M11 5V2H2v9h3"/></svg>',
};

const S = { me: null, zones: [], companies: [], visits: [], users: [], filter: 'today', q: '', catalog: null, catalogError: null, c4: null };

/* ---------- API ---------- */
async function api(path, opts = {}) {
  const r = await fetch('/api' + path, {
    ...opts, body: opts.body && JSON.stringify(opts.body),
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch' },
  });
  if (r.status === 401) { location.href = '/login.html'; throw new Error('Sesja wygasła.'); }
  const isJson = r.headers.get('content-type')?.includes('json');
  const body = isJson ? await r.json() : null;
  if (r.status === 403 && body?.code === 'must_change_password') { S.me.mustChangePassword = true; route(); throw new Error(body.error); }
  if (r.status === 403) throw new Error('Brak uprawnień do tej operacji.');
  if (!r.ok) throw new Error(body?.error || body?.detail || body?.title || `Błąd serwera (${r.status})`);
  return body;
}

/* ---------- czas ---------- */
const DAY = 864e5;
const startOfDay = d => { const x = new Date(d); x.setHours(0, 0, 0, 0); return x; };
const hm = d => new Date(d).toLocaleTimeString('pl-PL', { hour: '2-digit', minute: '2-digit' });
const dayLabel = d => {
  const diff = Math.round((startOfDay(d) - startOfDay(new Date())) / DAY);
  const base = new Date(d).toLocaleDateString('pl-PL', { weekday: 'long', day: 'numeric', month: 'long' });
  return diff === 0 ? 'Dziś · ' + base : diff === 1 ? 'Jutro · ' + base : diff === -1 ? 'Wczoraj · ' + base : base;
};
const dateInput = d => { const x = new Date(d); return `${x.getFullYear()}-${String(x.getMonth() + 1).padStart(2, '0')}-${String(x.getDate()).padStart(2, '0')}`; };
const timeInput = d => hm(d);
const plural = (n, one, few, many) => n === 1 ? one : (n % 10 >= 2 && n % 10 <= 4 && (n % 100 < 10 || n % 100 >= 20)) ? few : many;
const initials = (a, b) => ((a?.[0] || '') + (b?.[0] || '')).toUpperCase() || '·';

/* ---------- UI helpers ---------- */
function toast(msg, bad = false) {
  const t = document.createElement('div');
  t.className = 'toast' + (bad ? ' bad' : '');
  t.textContent = msg;
  $('#toasts').append(t);
  setTimeout(() => t.remove(), bad ? 6000 : 3500);
}

let menuEl = null;
function closeMenu() { menuEl?.remove(); menuEl = null; }
function openMenu(anchor, items) {
  closeMenu();
  menuEl = document.createElement('div');
  menuEl.className = 'menu'; menuEl.setAttribute('role', 'menu');
  menuEl.innerHTML = items.map((it, i) => it === '-' ? '<hr>' :
    `<button role="menuitem" data-i="${i}" class="${it.danger ? 'danger' : ''}">${it.icon || ''}<span>${esc(it.label)}</span></button>`).join('');
  document.body.append(menuEl);
  const r = anchor.getBoundingClientRect(), mw = menuEl.offsetWidth, mh = menuEl.offsetHeight;
  menuEl.style.left = Math.max(8, Math.min(r.right - mw, innerWidth - mw - 8)) + 'px';
  menuEl.style.top = (r.bottom + mh + 8 > innerHeight ? r.top - mh - 4 : r.bottom + 4) + 'px';
  menuEl.addEventListener('click', e => { const b = e.target.closest('button'); if (!b) return; const it = items[+b.dataset.i]; closeMenu(); it.run(); });
  menuEl.querySelector('button')?.focus();
}
document.addEventListener('click', e => { if (menuEl && !menuEl.contains(e.target) && !e.target.closest('[data-menu]')) closeMenu(); });

function openDrawer({ title, subtitle, body, submitLabel, note = '', onSubmit, onReady }) {
  const scrim = document.createElement('div'); scrim.className = 'scrim';
  const d = document.createElement('form'); d.className = 'drawer'; d.noValidate = true;
  d.setAttribute('role', 'dialog'); d.setAttribute('aria-label', title);
  d.innerHTML = `
    <header><div><h2>${esc(title)}</h2>${subtitle ? `<p>${esc(subtitle)}</p>` : ''}</div>
      <button type="button" class="icon-btn" data-close aria-label="Zamknij">${I.x}</button></header>
    <div class="body"><div class="form-error" hidden></div>${body}</div>
    <footer><span class="note">${note}</span><button type="button" class="btn ghost" data-close>Anuluj</button>
      <button type="submit" class="btn primary">${esc(submitLabel)}</button></footer>`;
  document.body.append(scrim, d);
  requestAnimationFrame(() => { scrim.classList.add('open'); d.classList.add('open'); });
  const close = () => { d.classList.remove('open'); scrim.classList.remove('open'); setTimeout(() => { d.remove(); scrim.remove(); }, 220); document.removeEventListener('keydown', onKey); };
  const onKey = e => { if (e.key === 'Escape' && !document.querySelector('dialog[open]')) close(); };
  document.addEventListener('keydown', onKey);
  scrim.onclick = close;
  d.querySelectorAll('[data-close]').forEach(b => b.onclick = close);
  const err = d.querySelector('.form-error'), submit = d.querySelector('[type=submit]');
  d.addEventListener('submit', async e => {
    e.preventDefault(); err.hidden = true; submit.disabled = true;
    try { await onSubmit(d); close(); }
    catch (ex) { err.textContent = ex.message; err.hidden = false; d.querySelector('.body').scrollTop = 0; }
    finally { submit.disabled = false; }
  });
  onReady?.(d);
  setTimeout(() => d.querySelector('input:not([type=hidden]),select')?.focus(), 230);
  return d;
}

function openDialog(html, onReady) {
  const dlg = document.createElement('dialog');
  dlg.innerHTML = html;
  document.body.append(dlg);
  dlg.addEventListener('close', () => dlg.remove());
  dlg.querySelectorAll('[data-close]').forEach(b => b.onclick = () => dlg.close());
  onReady?.(dlg);
  dlg.showModal();
  return dlg;
}

const field = (id, label, input, hint = '') =>
  `<div class="field"><label for="${id}">${label}</label>${input}${hint ? `<span class="hint">${hint}</span>` : ''}</div>`;
const val = (form, id) => form.querySelector('#' + id)?.value.trim() ?? '';

/* ---------- dane ---------- */
const zoneName = id => S.zones.find(z => z.id === id)?.name ?? id;
const companyName = id => S.companies.find(c => c.id === id)?.name ?? '—';

async function loadVisits() { S.visits = await api('/visits'); }
async function loadCompanies() { S.companies = await api('/companies'); }
async function loadUsers() { S.users = S.me.canManageUsers ? await api('/users') : []; }

async function checkC4() {
  const el = $('#c4state');
  try {
    const h = await api('/health');
    el.innerHTML = `<span class="dot ${h.ok ? 'ok' : 'bad'}"></span><span>C4${h.mode === 'Mock' ? ' (symulacja)' : ''}: ${h.ok ? 'połączono' : 'błąd'}</span>`;
    el.title = h.message;
  } catch { el.innerHTML = '<span class="dot bad"></span><span>C4: brak odpowiedzi</span>'; }
}

/* ---------- wizyty: logika statusu ---------- */
const finished = v => v.status === 'Expired' || v.status === 'Revoked' || !!v.checkedOutAt;
const inside = v => !!v.checkedInAt && !v.checkedOutAt;
const issue = v => !finished(v) && (!!v.lastError || !v.emailSent);

function statusOf(v) {
  if (v.checkedOutAt) return { cls: '', label: 'Wyszedł', sub: hm(v.checkedOutAt) };
  if (v.status === 'Revoked') return { cls: '', label: 'Dostęp cofnięty', sub: '' };
  if (v.status === 'Expired') return { cls: '', label: 'Wygasło', sub: v.checkedInAt ? 'bez wyjścia' : 'nie przyszedł' };
  if (v.lastError) return { cls: 'issue', label: v.lastError.startsWith('Mail') ? 'Mail nie wysłany' : 'Błąd C4', sub: 'szczegóły w menu' };
  if (inside(v)) return { cls: 'in', label: 'W budynku', sub: 'od ' + hm(v.checkedInAt) };
  if (v.status === 'Active') return { cls: 'active', label: 'Kod aktywny', sub: 'czeka na gościa' };
  return { cls: 'scheduled', label: 'Zaplanowana', sub: 'kod wysłany' };
}

function filtered() {
  const today = startOfDay(new Date()), tomorrow = new Date(+today + DAY), q = S.q.toLowerCase();
  const match = v => !q || [v.firstName, v.lastName, v.company, v.email, v.hostName, companyName(v.companyId)].join(' ').toLowerCase().includes(q);
  const sets = {
    today: v => new Date(v.validFrom) < tomorrow && new Date(v.validTo) >= today && v.status !== 'Revoked',
    upcoming: v => new Date(v.validFrom) >= tomorrow && !finished(v),
    inside: inside,
    history: finished,
  };
  return S.visits.filter(v => sets[S.filter](v) && match(v))
    .sort((a, b) => S.filter === 'history' ? new Date(b.validFrom) - new Date(a.validFrom) : new Date(a.validFrom) - new Date(b.validFrom));
}

/* ---------- widok: wizyty ---------- */
function renderVisits() {
  const today = startOfDay(new Date()), tomorrow = new Date(+today + DAY);
  const todays = S.visits.filter(v => new Date(v.validFrom) < tomorrow && new Date(v.validTo) >= today && v.status !== 'Revoked');
  const counts = {
    today: todays.length,
    upcoming: S.visits.filter(v => new Date(v.validFrom) >= tomorrow && !finished(v)).length,
    inside: S.visits.filter(inside).length,
    history: S.visits.filter(finished).length,
  };
  const expected = todays.filter(v => !v.checkedInAt && !finished(v)).length;
  const problems = S.visits.filter(issue).length;
  const scope = S.me.isBuildingAdmin ? 'Wszystkie firmy w budynku' : S.me.companyName;

  $('#view').innerHTML = `
    <div class="head">
      <div><h1>Goście</h1><p>${esc(scope)} · ${esc(new Date().toLocaleDateString('pl-PL', { weekday: 'long', day: 'numeric', month: 'long' }))}</p></div>
      <div class="spacer"></div>
      <button class="btn primary" id="invite">${I.plus} Zaproś gościa <kbd>N</kbd></button>
    </div>
    <div class="stats">
      <div class="stat"><b>${counts.today}</b><span>zaproszeń na dziś</span></div>
      <div class="stat"><b>${expected}</b><span>oczekiwanych jeszcze dziś</span></div>
      <div class="stat"><b>${counts.inside}</b><span>gości w budynku</span></div>
      <div class="stat ${problems ? 'alert' : ''}"><b>${problems}</b><span>wymaga uwagi</span></div>
    </div>
    <div class="toolbar">
      <div class="seg" role="group" aria-label="Filtr">
        ${[['today', 'Dziś'], ['upcoming', 'Nadchodzące'], ['inside', 'W budynku'], ['history', 'Historia']].map(([k, l]) =>
          `<button data-f="${k}" aria-pressed="${S.filter === k}">${l}<span class="n">${counts[k]}</span></button>`).join('')}
      </div>
      <label class="search">${I.search}<input class="input" id="q" placeholder="Szukaj gościa, firmy, osoby…" value="${esc(S.q)}" aria-label="Szukaj"></label>
    </div>
    <div id="vlist"></div>`;

  $('#invite').onclick = openInvite;
  $('#view').querySelectorAll('[data-f]').forEach(b => b.onclick = () => { S.filter = b.dataset.f; renderVisits(); });
  $('#q').oninput = e => { S.q = e.target.value; renderVisitList(); };
  renderVisitList();
}

function renderVisitList() {
  const list = filtered(), box = $('#vlist');
  if (!list.length) {
    const msg = {
      today: ['Na dziś nie ma zaproszeń', 'Zaproś gościa – dostanie kod QR e-mailem.'],
      upcoming: ['Brak zaplanowanych wizyt', 'Zaproszenia na kolejne dni pojawią się tutaj.'],
      inside: ['Nikogo nie ma w budynku', 'Oznacz przybycie gościa przyciskiem „Przyszedł”.'],
      history: ['Historia jest pusta', 'Zakończone wizyty z ostatnich 14 dni.'],
    }[S.filter];
    box.innerHTML = `<div class="empty"><b>${S.q ? 'Brak wyników dla „' + esc(S.q) + '”' : msg[0]}</b>${S.q ? '' : msg[1]}</div>`;
    return;
  }
  const groups = new Map();
  for (const v of list) { const k = startOfDay(v.validFrom).toISOString(); if (!groups.has(k)) groups.set(k, []); groups.get(k).push(v); }
  box.innerHTML = [...groups].map(([k, vs]) => `
    <section class="day"><h3>${esc(dayLabel(k))}<span class="n">${vs.length}</span></h3>
      <div class="list">${vs.map(visitRow).join('')}</div></section>`).join('');
  box.querySelectorAll('[data-act]').forEach(b => b.onclick = () => visitAction(b.dataset.act, b.dataset.id, b));
}

function visitRow(v) {
  const st = statusOf(v), done = finished(v);
  const multiDay = startOfDay(v.validFrom).getTime() !== startOfDay(v.validTo).getTime();
  const primary = done ? '' : !v.checkedInAt
    ? `<button class="btn sm" data-act="checkin" data-id="${v.id}">Przyszedł</button>`
    : `<button class="btn sm" data-act="checkout" data-id="${v.id}">Wyszedł</button>`;
  return `<div class="vrow ${done ? 'done' : ''}">
    <div class="time">${hm(v.validFrom)}–${hm(v.validTo)}<span>${multiDay ? 'do ' + new Date(v.validTo).toLocaleDateString('pl-PL', { day: 'numeric', month: 'short' }) : esc(v.accessCodeMasked.slice(-4) ? 'kod …' + v.accessCodeMasked.slice(-4) : '')}</span></div>
    <div class="guest"><span class="mono-av">${esc(initials(v.firstName, v.lastName))}</span>
      <div class="t"><b>${esc(v.firstName)} ${esc(v.lastName)}</b><span>${esc(v.company || v.email)}</span></div></div>
    <div class="where">${esc(zoneName(v.accessProfileId))}<span>${S.me.isBuildingAdmin ? esc(companyName(v.companyId)) + ' · ' : ''}${esc(v.hostName || '')}</span></div>
    <div class="status ${st.cls}"><span class="dot"></span><div>${esc(st.label)}${st.sub ? `<small>${esc(st.sub)}</small>` : ''}</div></div>
    <div class="acts">${primary}<button class="icon-btn" data-menu data-act="menu" data-id="${v.id}" aria-label="Więcej">${I.more}</button></div>
  </div>`;
}

async function visitAction(act, id, anchor) {
  const v = S.visits.find(x => x.id === id);
  if (act === 'menu') {
    const live = !finished(v);
    openMenu(anchor, [
      { label: 'Pokaż kod QR', icon: I.qr, run: () => showQr(v) },
      ...(live ? [{ label: 'Wyślij e-mail ponownie', icon: I.mail, run: () => visitAction('resend', id) }] : []),
      ...(v.lastError ? [{ label: 'Szczegóły problemu', icon: I.ban, run: () => openDialog(`<div class="d-body"><h2>Problem z wizytą</h2><p class="muted">${esc(v.lastError)}</p><p class="muted">System ponawia operacje w C4 automatycznie co 30 s.</p></div><div class="d-foot"><button class="btn" data-close>Zamknij</button></div>`) }] : []),
      ...(live ? ['-', { label: 'Cofnij dostęp', icon: I.ban, danger: true, run: () => confirmRevoke(v) }] : []),
    ]);
    return;
  }
  try {
    const updated = await api(`/visits/${id}/${act}`, { method: 'POST' });
    Object.assign(v, updated);
    toast({ checkin: `${v.firstName} ${v.lastName} – w budynku`, checkout: `${v.firstName} ${v.lastName} wyszedł. Kod usunięty z C4.`,
            resend: `Wysłano ponownie na ${v.email}`, revoke: `Dostęp cofnięty – kod nie otworzy już drzwi` }[act]);
    renderVisits();
  } catch (e) { toast(e.message, true); }
}

function confirmRevoke(v) {
  openDialog(`<div class="d-body"><h2>Cofnąć dostęp?</h2>
    <p class="muted">Kod gościa <b>${esc(v.firstName)} ${esc(v.lastName)}</b> zostanie natychmiast usunięty z systemu C4 i przestanie otwierać drzwi. Tej operacji nie można odwrócić – w razie potrzeby wyślij nowe zaproszenie.</p></div>
    <div class="d-foot"><button class="btn ghost" data-close>Anuluj</button><button class="btn accent" id="ok">Cofnij dostęp</button></div>`,
    dlg => $('#ok', dlg).onclick = () => { dlg.close(); visitAction('revoke', v.id); });
}

function showQr(v) {
  openDialog(`<div class="d-body"><h2>${esc(v.firstName)} ${esc(v.lastName)}</h2>
    <p class="muted" style="margin:0">${esc(zoneName(v.accessProfileId))} · ${hm(v.validFrom)}–${hm(v.validTo)}</p>
    <div class="qrbox"><img src="/api/visits/${v.id}/qr.png" alt="Kod QR gościa"></div>
    <p class="muted" style="font-size:12px;margin:0">Gość otrzymał ten kod e-mailem. Możesz go też pokazać do zeskanowania telefonem.</p></div>
    <div class="d-foot"><button class="btn" data-close>Zamknij</button></div>`);
}

/* ---------- zaproszenie ---------- */
function companyZones(companyId) {
  const co = S.companies.find(c => c.id === companyId);
  return co ? S.zones.filter(z => co.zones.some(cz => cz.profileId === z.id)) : [];
}

function openInvite() {
  const now = new Date(); now.setMinutes(Math.ceil(now.getMinutes() / 15) * 15, 0, 0);
  const end = new Date(+now + 2 * 36e5);
  const admin = S.me.isBuildingAdmin;
  const companies = S.companies.filter(c => c.active);
  openDrawer({
    title: 'Zaproś gościa',
    subtitle: admin ? 'W imieniu wybranej firmy' : `W imieniu ${S.me.companyName}`,
    submitLabel: 'Wyślij zaproszenie',
    body: `
      ${admin ? `<div class="section">Firma zapraszająca</div>${field('company', 'Firma', `<select class="input" id="company">${companies.map(c => `<option value="${c.id}">${esc(c.name)}</option>`).join('')}</select>`)}` : ''}
      <div class="section">Gość</div>
      <div class="grid2">${field('fn', 'Imię', '<input class="input" id="fn" required maxlength="80" autocomplete="off">')}
        ${field('ln', 'Nazwisko', '<input class="input" id="ln" required maxlength="80" autocomplete="off">')}</div>
      ${field('em', 'E-mail', '<input class="input" id="em" type="email" required maxlength="200" autocomplete="off" placeholder="na ten adres trafi kod QR">')}
      <div class="grid2">${field('co', 'Firma gościa', '<input class="input" id="co" maxlength="120" placeholder="opcjonalnie">')}
        ${field('ph', 'Telefon', '<input class="input" id="ph" type="tel" maxlength="40" placeholder="opcjonalnie">')}</div>
      <div class="section">Wizyta</div>
      ${field('host', 'Osoba przyjmująca', `<input class="input" id="host" maxlength="120" value="${esc(S.me.displayName)}">`)}
      ${field('day', 'Dzień', `<input class="input" id="day" type="date" value="${dateInput(now)}" min="${dateInput(new Date())}">`)}
      <div class="grid2">${field('from', 'Od', `<input class="input" id="from" type="time" step="900" value="${timeInput(now)}">`)}
        ${field('to', 'Do', `<input class="input" id="to" type="time" step="900" value="${timeInput(end)}">`)}</div>
      <div class="chips" style="margin:-4px 0 16px">
        <button type="button" class="chip" data-h="1">1 godz.</button><button type="button" class="chip" data-h="2">2 godz.</button>
        <button type="button" class="chip" data-h="4">4 godz.</button><button type="button" class="chip" data-h="day">Cały dzień 8–18</button></div>
      <div class="section">Dostęp</div>
      <div id="zones"></div>
      <div class="summary" id="sum"></div>`,
    onReady: d => {
      const renderZones = () => {
        const cid = admin ? val(d, 'company') : S.me.companyId;
        const zs = companyZones(cid);
        $('#zones', d).innerHTML = zs.length ? zs.map((z, i) => `<label class="check"><input type="radio" name="zone" value="${esc(z.id)}" ${i === 0 ? 'checked' : ''}>
          <div><b>${esc(z.name)}</b><span>${esc(z.description || '')}</span></div></label>`).join('')
          : '<p class="muted">Ta firma nie ma przydzielonych stref – skontaktuj się z administratorem budynku.</p>';
        summary();
      };
      const times = () => {
        const day = val(d, 'day'), f = val(d, 'from'), t = val(d, 'to');
        let from = new Date(`${day}T${f}`), to = new Date(`${day}T${t}`);
        if (to <= from) to = new Date(+to + DAY);    // wizyta przez północ
        return { from, to };
      };
      const summary = () => {
        const { from, to } = times(), em = val(d, 'em');
        if (isNaN(from) || isNaN(to)) { $('#sum', d).textContent = ''; return; }
        const act = new Date(+from - 30 * 6e4), deact = new Date(+to + 30 * 6e4);
        $('#sum', d).innerHTML = `Kod QR trafi ${em ? `na <b>${esc(em)}</b>` : 'na podany e-mail'} od razu po wysłaniu.
          W systemie C4 będzie aktywny <b>${act.toLocaleDateString('pl-PL', { day: 'numeric', month: 'short' })} ${hm(act)}–${hm(deact)}</b> (30 min zapasu), potem zostanie usunięty automatycznie.`;
      };
      d.querySelectorAll('[data-h]').forEach(c => c.onclick = () => {
        if (c.dataset.h === 'day') { $('#from', d).value = '08:00'; $('#to', d).value = '18:00'; }
        else { const [h, m] = val(d, 'from').split(':').map(Number); const t = new Date(2000, 0, 1, h + +c.dataset.h, m); $('#to', d).value = timeInput(t); }
        summary();
      });
      d.addEventListener('input', summary);
      if (admin) $('#company', d).onchange = renderZones;
      renderZones();
      d._times = times;
    },
    onSubmit: async d => {
      const { from, to } = d._times();
      const zone = d.querySelector('input[name=zone]:checked')?.value;
      if (!val(d, 'fn') || !val(d, 'ln')) throw new Error('Podaj imię i nazwisko gościa.');
      if (!/^\S+@\S+\.\S+$/.test(val(d, 'em'))) throw new Error('Podaj poprawny adres e-mail – na niego trafi kod QR.');
      if (!zone) throw new Error('Wybierz strefę dostępu.');
      const v = await api('/visits', { method: 'POST', body: {
        firstName: val(d, 'fn'), lastName: val(d, 'ln'), email: val(d, 'em'), company: val(d, 'co') || null, phone: val(d, 'ph') || null,
        hostName: val(d, 'host') || null, accessProfileId: zone, validFrom: from.toISOString(), validTo: to.toISOString(),
        companyId: S.me.isBuildingAdmin ? val(d, 'company') : null } });
      S.visits.push(v);
      if (startOfDay(v.validFrom) > startOfDay(new Date())) S.filter = 'upcoming'; else S.filter = 'today';
      renderVisits();
      toast(v.emailSent ? `Zaproszenie wysłane do ${v.firstName} ${v.lastName}` : 'Wizyta zapisana, ale e-mail nie wyszedł – wyślij ponownie z menu', !v.emailSent);
    },
  });
}

/* ---------- widok: użytkownicy ---------- */
function renderUsers() {
  const admin = S.me.isBuildingAdmin;
  const rows = S.users.map(u => `
    <tr class="${u.active ? '' : 'off'}">
      <td><div class="cell-main"><span class="mono-av">${esc(initials(...u.displayName.split(' ')))}</span><div><b>${esc(u.displayName)}</b><span class="mono">${esc(u.login)}</span></div></div></td>
      ${admin ? `<td>${u.companyId ? esc(companyName(u.companyId)) : '<span class="muted">—</span>'}</td>` : ''}
      <td><span class="tag role-${u.role}">${ROLE[u.role]}</span></td>
      <td>${u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString('pl-PL', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }) : '<span class="muted">nigdy</span>'}</td>
      <td>${!u.active ? '<span class="status"><span class="dot"></span>Zablokowane</span>' : u.mustChangePassword ? '<span class="status scheduled"><span class="dot"></span>Hasło tymczasowe</span>' : '<span class="status active"><span class="dot"></span>Aktywne</span>'}</td>
      <td class="r"><button class="icon-btn" data-menu data-u="${u.id}" aria-label="Akcje">${I.more}</button></td>
    </tr>`).join('');
  $('#view').innerHTML = `
    <div class="head"><div><h1>Użytkownicy</h1><p>${admin ? 'Konta wszystkich firm i administratorów budynku' : `Konta firmy ${esc(S.me.companyName)} – mogą zapraszać gości`}</p></div>
      <div class="spacer"></div><button class="btn primary" id="add">${I.plus} Dodaj użytkownika</button></div>
    <table class="table"><thead><tr><th>Osoba</th>${admin ? '<th>Firma</th>' : ''}<th>Rola</th><th>Ostatnie logowanie</th><th>Status</th><th></th></tr></thead>
      <tbody>${rows || `<tr><td colspan="6" class="muted">Brak kont</td></tr>`}</tbody></table>`;
  $('#add').onclick = () => openUser();
  $('#view').querySelectorAll('[data-u]').forEach(b => b.onclick = () => {
    const u = S.users.find(x => x.id === b.dataset.u);
    openMenu(b, [
      { label: 'Edytuj', icon: I.edit, run: () => openUser(u) },
      { label: 'Resetuj hasło', icon: I.key, run: () => resetPassword(u) },
    ]);
  });
}

function roleOptions(selected) {
  const roles = S.me.isBuildingAdmin ? ['Host', 'CompanyAdmin', 'BuildingAdmin'] : ['Host', 'CompanyAdmin'];
  return roles.map(r => `<option value="${r}" ${r === selected ? 'selected' : ''}>${ROLE[r]}</option>`).join('');
}

function openUser(u) {
  const admin = S.me.isBuildingAdmin, isNew = !u;
  openDrawer({
    title: isNew ? 'Nowy użytkownik' : 'Edytuj użytkownika',
    subtitle: isNew ? 'Hasło tymczasowe pokażemy po zapisaniu' : u.login,
    submitLabel: isNew ? 'Utwórz konto' : 'Zapisz',
    body: `
      ${field('dn', 'Imię i nazwisko', `<input class="input" id="dn" required value="${esc(u?.displayName)}">`)}
      ${isNew ? field('lg', 'Login', '<input class="input mono" id="lg" required autocomplete="off" placeholder="np. anna.kowalska">', 'Bez spacji, min. 3 znaki') : ''}
      ${field('em', 'E-mail', `<input class="input" id="em" type="email" value="${esc(u?.email)}" placeholder="opcjonalnie">`)}
      ${field('role', 'Rola', `<select class="input" id="role">${roleOptions(u?.role ?? 'Host')}</select>`,
        'Pracownik zaprasza gości. Administrator firmy dodatkowo zarządza kontami swojej firmy.')}
      ${admin && isNew ? field('cmp', 'Firma', `<select class="input" id="cmp">${S.companies.map(c => `<option value="${c.id}">${esc(c.name)}</option>`).join('')}</select>`) : ''}
      ${!isNew ? `<label class="check"><input type="checkbox" id="act" ${u.active ? 'checked' : ''}><div><b>Konto aktywne</b><span>Zablokowanie natychmiast wylogowuje użytkownika.</span></div></label>` : ''}`,
    onReady: d => {
      const sync = () => { const c = $('#cmp', d); if (c) c.closest('.field').hidden = val(d, 'role') === 'BuildingAdmin'; };
      $('#role', d).onchange = sync; sync();
    },
    onSubmit: async d => {
      if (isNew) {
        const res = await api('/users', { method: 'POST', body: {
          login: val(d, 'lg'), displayName: val(d, 'dn'), email: val(d, 'em') || null, role: val(d, 'role'),
          companyId: admin ? (val(d, 'role') === 'BuildingAdmin' ? null : val(d, 'cmp')) : S.me.companyId } });
        S.users.push(res.user); renderUsers();
        showSecret(`Konto ${res.user.login} utworzone`, res.tempPassword);
      } else {
        const res = await api(`/users/${u.id}`, { method: 'PUT', body: {
          displayName: val(d, 'dn'), email: val(d, 'em') || null, role: val(d, 'role'), active: $('#act', d).checked } });
        Object.assign(u, res); renderUsers(); toast('Zapisano zmiany');
      }
    },
  });
}

function resetPassword(u) {
  openDialog(`<div class="d-body"><h2>Zresetować hasło?</h2><p class="muted">${esc(u.displayName)} zostanie wylogowany i przy następnym logowaniu ustawi nowe hasło.</p></div>
    <div class="d-foot"><button class="btn ghost" data-close>Anuluj</button><button class="btn primary" id="ok">Resetuj</button></div>`,
    dlg => $('#ok', dlg).onclick = async () => {
      dlg.close();
      try { const r = await api(`/users/${u.id}/reset-password`, { method: 'POST' }); u.mustChangePassword = true; renderUsers(); showSecret(`Nowe hasło dla ${u.login}`, r.tempPassword); }
      catch (e) { toast(e.message, true); }
    });
}

function showSecret(title, secret) {
  openDialog(`<div class="d-body"><h2>${esc(title)}</h2>
    <p class="muted" style="margin:0">Przekaż hasło tymczasowe użytkownikowi bezpiecznym kanałem. Zostanie pokazane tylko raz – przy pierwszym logowaniu trzeba je zmienić.</p>
    <div class="secret"><span id="sec">${esc(secret)}</span><button class="icon-btn" id="cp" aria-label="Kopiuj">${I.copy}</button></div></div>
    <div class="d-foot"><button class="btn primary" data-close>Gotowe</button></div>`,
    dlg => $('#cp', dlg).onclick = async () => { try { await navigator.clipboard.writeText(secret); toast('Skopiowano'); } catch { getSelection().selectAllChildren($('#sec', dlg)); } });
}

/* ---------- widok: firmy ---------- */
function renderCompanies() {
  const userCount = id => S.users.filter(u => u.companyId === id).length;
  const rows = S.companies.map(c => `
    <tr class="${c.active ? '' : 'off'}">
      <td><div class="cell-main"><span class="mono-av">${esc(initials(...c.name.split(' ')))}</span><div><b>${esc(c.name)}</b><span>${userCount(c.id)} ${plural(userCount(c.id), 'konto', 'konta', 'kont')}</span></div></div></td>
      <td><div class="chips">${c.zones.map(z => `<span class="tag">${esc(zoneName(z.profileId))}</span>`).join('')}</div></td>
      <td class="mono">${c.maxConcurrentGuests || '∞'}</td>
      <td>${c.active ? '<span class="status active"><span class="dot"></span>Aktywna</span>' : '<span class="status"><span class="dot"></span>Zablokowana</span>'}</td>
      <td class="r"><button class="btn sm ghost" data-c="${c.id}">Edytuj</button></td>
    </tr>`).join('');
  $('#view').innerHTML = `
    <div class="head"><div><h1>Firmy</h1><p>Najemcy, którzy mogą zapraszać gości – i strefy, do których mogą ich wpuszczać</p></div>
      <div class="spacer"></div><button class="btn primary" id="add">${I.plus} Dodaj firmę</button></div>
    <table class="table"><thead><tr><th>Firma</th><th>Strefy dla gości</th><th>Limit</th><th>Status</th><th></th></tr></thead>
      <tbody>${rows || '<tr><td colspan="5" class="muted">Dodaj pierwszą firmę, a potem jej administratora w zakładce Użytkownicy.</td></tr>'}</tbody></table>`;
  $('#add').onclick = () => openCompany().catch(e => toast(e.message, true));
  $('#view').querySelectorAll('[data-c]').forEach(b => b.onclick = () => openCompany(S.companies.find(c => c.id === b.dataset.c)).catch(e => toast(e.message, true)));
}

async function openCompany(c) {
  const isNew = !c;
  const has = id => c?.zones.find(z => z.profileId === id);
  if (!S.catalog) await loadCatalog();
  openDrawer({
    title: isNew ? 'Nowa firma' : c.name, subtitle: 'Uprawnienia, które firma może nadawać gościom',
    submitLabel: isNew ? 'Dodaj firmę' : 'Zapisz',
    body: `
      ${field('nm', 'Nazwa firmy', `<input class="input" id="nm" required value="${esc(c?.name)}">`)}
      ${field('mx', 'Limit jednocześnie ważnych zaproszeń', `<input class="input mono" id="mx" type="number" min="0" value="${c?.maxConcurrentGuests ?? 20}">`, '0 = bez limitu')}
      <div class="section">Strefy dla gości</div>
      ${S.zones.map(z => `<label class="check"><input type="checkbox" name="z" value="${esc(z.id)}" ${has(z.id) ? 'checked' : ''}>
        <div style="flex:1;min-width:0"><b>${esc(z.name)}</b><span>${esc(z.description || '')}</span>
        ${folderSelect(`data-folder="${esc(z.id)}"`, has(z.id)?.c4PersonFolderId, `Folder strefy: ${folderText(z.c4PersonFolderId)}`)}</div></label>`).join('')
        || '<p class="muted">Nie ma jeszcze stref. Dodaj je w zakładce Konfiguracja C4.</p>'}
      ${!isNew ? `<div class="section">Status</div><label class="check"><input type="checkbox" id="act" ${c.active ? 'checked' : ''}><div><b>Firma aktywna</b><span>Zablokowanie wylogowuje wszystkich jej użytkowników.</span></div></label>` : ''}`,
    onSubmit: async d => {
      const zones = [...d.querySelectorAll('input[name=z]:checked')].map(i => {
        const f = d.querySelector(`[data-folder="${CSS.escape(i.value)}"]`).value.trim();
        return { profileId: i.value, c4PersonFolderId: f || null };
      });
      const body = { name: val(d, 'nm'), active: isNew ? true : $('#act', d).checked, maxConcurrentGuests: +val(d, 'mx') || 0, zones };
      const res = await api(isNew ? '/companies' : `/companies/${c.id}`, { method: isNew ? 'POST' : 'PUT', body });
      if (isNew) S.companies.push(res); else Object.assign(c, res);
      S.companies.sort((a, b) => a.name.localeCompare(b.name, 'pl'));
      renderCompanies(); toast(isNew ? `Dodano firmę ${res.name}` : 'Zapisano zmiany');
    },
  });
}

/* ---------- widok: konfiguracja C4 (administrator budynku) ---------- */
const short = id => '…' + String(id).slice(-4);
const catItem = (list, id) => S.catalog?.[list].find(x => x.id === id);
const folderText = id => catItem('folders', id)?.name ?? (S.catalog ? `nie ma w C4 (${short(id)})` : `folder ${short(id)}`);
const catLabel = (list, id) => catItem(list, id) ? esc(catItem(list, id).name)
  : `<span class="warn-txt" title="${esc(id)}">${S.catalog ? 'nie ma w C4' : 'brak połączenia z C4'} (${short(id)})</span>`;

async function loadCatalog(force = false) {
  if (S.catalog && !force) return S.catalog;
  try { S.catalog = await api('/c4/catalog'); S.catalogError = null; }
  catch (e) { S.catalog = null; S.catalogError = e.message; }
  return S.catalog;
}

/** Lista wyboru folderu osób w C4. `empty` = etykieta pustej opcji (np. folder domyślny strefy); bez niej wybór jest wymagany. */
function folderSelect(attrs, current, empty) {
  const known = !current || catItem('folders', current);
  return `<select class="input" ${attrs}>
    ${empty ? `<option value="">${esc(empty)}</option>` : `<option value="" disabled ${current ? '' : 'selected'}>— wybierz folder z C4 —</option>`}
    ${(S.catalog?.folders || []).map(f => `<option value="${f.id}" ${f.id === current ? 'selected' : ''}>${esc(f.name)}</option>`).join('')}
    ${known ? '' : `<option value="${esc(current)}" selected>${esc(folderText(current))}</option>`}
  </select>`;
}

/** Pola wyboru poziomów dostępu z C4; zaznaczone, których nie ma już w C4, zostają widoczne do odznaczenia. */
function levelChecks(name, selected, exclude = []) {
  const levels = (S.catalog?.accessLevels || []).filter(l => !exclude.includes(l.id));
  const missing = selected.filter(id => !exclude.includes(id) && !levels.some(l => l.id === id));
  if (!levels.length && !missing.length && exclude.length)
    return '<p class="muted">Wszystkie poziomy dostępu z C4 każdy gość dostaje już jako wspólne.</p>';
  if (!levels.length && !missing.length)
    return `<p class="muted">${S.catalog ? 'W C4 nie ma jeszcze poziomów dostępu – utwórz je w kliencie C4 i kliknij „Odśwież dane z C4”.' : 'Lista dostępna po połączeniu z C4.'}</p>`;
  return `<div class="checks">${levels.map(l => `<label class="check"><input type="checkbox" name="${name}" value="${l.id}" ${selected.includes(l.id) ? 'checked' : ''}><div><b>${esc(l.name)}</b></div></label>`).join('')}
    ${missing.map(id => `<label class="check"><input type="checkbox" name="${name}" value="${id}" checked><div><b>${catLabel('accessLevels', id)}</b><span>odznacz, aby usunąć</span></div></label>`).join('')}</div>`;
}
const checkedValues = (root, name) => [...root.querySelectorAll(`input[name="${name}"]:checked`)].map(i => i.value);

function renderConfig() {
  $('#view').innerHTML = `<div class="head"><div><h1>Konfiguracja C4</h1><p>Pobieranie danych z C4…</p></div></div>`;
  loadConfig(false);
}

async function loadConfig(force) {
  try {
    const [c4, zones, health] = await Promise.all([api('/settings/c4'), api('/zones'), api('/health'), loadCatalog(force)]);
    S.c4 = c4; S.zones = zones; S.health = health;
  } catch (e) { toast(e.message, true); return; }
  if ((location.hash.slice(2) || 'visits') === 'config') drawConfig();
  if (force) { checkC4(); toast(S.catalog ? 'Pobrano aktualne dane z C4' : 'Nie udało się połączyć z C4', !S.catalog); }
}

function drawConfig() {
  const c4 = S.c4, isCard = c4.credentialType !== 'PIN';
  const usedBy = id => S.companies.filter(c => c.zones.some(z => z.profileId === id)).map(c => c.name);
  const zoneRows = S.zones.map(z => `
    <tr>
      <td><b>${esc(z.name)}</b>${z.description ? `<span class="muted" style="display:block;font-size:12px">${esc(z.description)}</span>` : ''}</td>
      <td>${catLabel('folders', z.c4PersonFolderId)}</td>
      <td><div class="chips">${z.accessLevelIds.map(id => `<span class="tag">${catLabel('accessLevels', id)}</span>`).join('') || '<span class="muted">tylko wspólne</span>'}</div></td>
      <td>${usedBy(z.id).map(esc).join(', ') || '<span class="muted">żadna</span>'}</td>
      <td class="r"><button class="icon-btn" data-menu data-z="${esc(z.id)}" aria-label="Akcje">${I.more}</button></td>
    </tr>`).join('');

  $('#view').innerHTML = `
    <div class="head"><div><h1>Konfiguracja C4</h1><p>Jak goście są zakładani w systemie kontroli dostępu – wybierasz z list pobranych z C4</p></div>
      <div class="spacer"></div><button class="btn" id="refresh">Odśwież dane z C4</button></div>
    ${S.catalogError ? `<div class="notice bad">${esc(S.catalogError)} Ustawienia możesz przeglądać, ale wybór z list będzie możliwy po przywróceniu połączenia.</div>`
      : S.health && !S.health.ok ? `<div class="notice bad"><b>Do poprawy:</b> ${esc(S.health.message)}. Wybierz właściwy folder lub uprawnienie z listy albo usuń nieużywaną strefę.</div>` : ''}

    <form class="panel" id="c4form" novalidate>
      <h2>Karta gościa</h2>
      <p class="lead">W jakiej postaci kod z zaproszenia trafia do C4. Czytnik QR przy drzwiach odczytuje kod i przekazuje go jak numer karty (najczęściej) albo jak PIN.</p>
      <div class="grid2">
        <label class="check"><input type="radio" name="ctype" value="Card" ${isCard ? 'checked' : ''}><div><b>Karta</b><span>Kod z QR zapisywany jako numer karty</span></div></label>
        <label class="check"><input type="radio" name="ctype" value="PIN" ${isCard ? '' : 'checked'}><div><b>PIN</b><span>Kod z QR zapisywany jako PIN</span></div></label>
      </div>
      <div id="cardTypeBox" ${isCard ? '' : 'hidden'}>
        ${field('cardType', 'Typ karty w C4', `<select class="input" id="cardType">
          <option value="">Automatycznie – pierwszy włączony typ karty</option>
          ${(S.catalog?.cardTypes || []).map(t => `<option value="${t.id}" ${t.id === c4.cardTypeId ? 'selected' : ''}>${esc(t.name)}</option>`).join('')}
          ${c4.cardTypeId && !catItem('cardTypes', c4.cardTypeId) ? `<option value="${c4.cardTypeId}" selected>${S.catalog ? 'Typ niewłączony w C4' : 'Zapisany typ'} (${short(c4.cardTypeId)})</option>` : ''}
        </select>`, 'Lista zawiera tylko typy włączone w C4. Typ musi pomieścić kod z QR (12 cyfr wymaga karty co najmniej 40-bitowej).')}
      </div>

      <h2 style="margin-top:22px">Uprawnienia każdego gościa</h2>
      <p class="lead">Poziomy dostępu z C4, które dostaje każdy gość niezależnie od strefy – np. <b>visitor</b>. Dodatkowe uprawnienia możesz dodać w strefach poniżej.</p>
      ${levelChecks('lvl', c4.accessLevelIds)}
      <div class="panel-foot"><button type="submit" class="btn primary">Zapisz ustawienia</button></div>
    </form>

    <section class="panel">
      <div style="display:flex;align-items:flex-start;gap:12px;flex-wrap:wrap">
        <div style="flex:1;min-width:240px"><h2>Strefy dla gości</h2>
          <p class="lead">Strefa to miejsce, do którego firma może zaprosić gościa. Każda ma folder w C4, w którym zakładany jest gość, i opcjonalnie dodatkowe uprawnienia. Strefy przydzielasz firmom w zakładce Firmy.</p></div>
        <button class="btn primary" id="addZone">${I.plus} Dodaj strefę</button>
      </div>
      <table class="table"><thead><tr><th>Strefa</th><th>Folder w C4</th><th>Dodatkowe uprawnienia</th><th>Firmy</th><th></th></tr></thead>
        <tbody>${zoneRows || '<tr><td colspan="5" class="muted">Nie ma jeszcze stref – dodaj pierwszą.</td></tr>'}</tbody></table>
    </section>`;

  const form = $('#c4form');
  form.querySelectorAll('input[name=ctype]').forEach(r => r.onchange = () => { $('#cardTypeBox').hidden = form.querySelector('input[name=ctype]:checked').value !== 'Card'; });
  form.onsubmit = async e => {
    e.preventDefault();
    const btn = form.querySelector('[type=submit]'); btn.disabled = true;
    try {
      S.c4 = await api('/settings/c4', { method: 'PUT', body: {
        credentialType: form.querySelector('input[name=ctype]:checked').value,
        cardTypeId: $('#cardType').value || null, accessLevelIds: checkedValues(form, 'lvl') } });
      toast('Zapisano ustawienia C4'); await refreshHealth();
    } catch (ex) { toast(ex.message, true); }
    finally { btn.disabled = false; }
  };
  $('#refresh').onclick = () => { $('#refresh').disabled = true; loadConfig(true); };
  $('#addZone').onclick = () => openZone();
  $('#view').querySelectorAll('[data-z]').forEach(b => b.onclick = () => {
    const z = S.zones.find(x => x.id === b.dataset.z);
    openMenu(b, [
      { label: 'Edytuj', icon: I.edit, run: () => openZone(z) },
      '-',
      { label: 'Usuń strefę', icon: I.trash, danger: true, run: () => confirmDeleteZone(z) },
    ]);
  });
}

async function refreshHealth() {
  try { S.health = await api('/health'); } catch { S.health = null; }
  drawConfig(); checkC4();
}

function openZone(z) {
  const isNew = !z;
  if (!S.catalog) { toast('Brak połączenia z C4 – nie można wybrać folderu. Kliknij „Odśwież dane z C4”.', true); return; }
  const common = S.c4?.accessLevelIds.map(id => catItem('accessLevels', id)?.name ?? short(id)).join(', ') || 'brak';
  openDrawer({
    title: isNew ? 'Nowa strefa' : z.name, subtitle: 'Gdzie i z jakimi uprawnieniami gość jest zakładany w C4',
    submitLabel: isNew ? 'Dodaj strefę' : 'Zapisz',
    body: `
      ${field('zn', 'Nazwa strefy', `<input class="input" id="zn" required maxlength="80" value="${esc(z?.name)}" placeholder="np. Piętro 2 – biura">`, 'Tę nazwę widzą firmy przy zapraszaniu gościa.')}
      ${field('zd', 'Opis', `<input class="input" id="zd" maxlength="200" value="${esc(z?.description)}" placeholder="opcjonalnie, np. hol, windy, drzwi biura 2.07">`)}
      ${field('zf', 'Folder osób w C4', folderSelect('id="zf"', z?.c4PersonFolderId, null), 'W tym folderze aplikacja zakłada gościa na czas wizyty i usuwa go po jej zakończeniu.')}
      <div class="section">Dodatkowe uprawnienia w tej strefie</div>
      <p class="muted" style="margin:-4px 0 10px;font-size:12.5px">Oprócz wspólnych dla każdego gościa: ${esc(common)}.</p>
      ${levelChecks('zl', z?.accessLevelIds || [], S.c4?.accessLevelIds || [])}`,
    onSubmit: async d => {
      if (!val(d, 'zn')) throw new Error('Podaj nazwę strefy.');
      if (!val(d, 'zf')) throw new Error('Wybierz folder osób w C4.');
      const body = { name: val(d, 'zn'), description: val(d, 'zd') || null, c4PersonFolderId: val(d, 'zf'), accessLevelIds: checkedValues(d, 'zl') };
      const res = await api(isNew ? '/zones' : `/zones/${encodeURIComponent(z.id)}`, { method: isNew ? 'POST' : 'PUT', body });
      if (isNew) S.zones.push(res); else Object.assign(z, res);
      toast(isNew ? `Dodano strefę ${res.name}` : 'Zapisano strefę'); await refreshHealth();
    },
  });
}

function confirmDeleteZone(z) {
  openDialog(`<div class="d-body"><h2>Usunąć strefę?</h2>
    <p class="muted">Strefa <b>${esc(z.name)}</b> zniknie z listy przy zapraszaniu gości. Folderu i uprawnień w C4 to nie zmienia.</p></div>
    <div class="d-foot"><button class="btn ghost" data-close>Anuluj</button><button class="btn accent" id="ok">Usuń</button></div>`,
    dlg => $('#ok', dlg).onclick = async () => {
      dlg.close();
      try { await api(`/zones/${encodeURIComponent(z.id)}`, { method: 'DELETE' }); S.zones = S.zones.filter(x => x.id !== z.id); toast('Strefa usunięta'); await refreshHealth(); }
      catch (e) { toast(e.message, true); }
    });
}

/* ---------- konto ---------- */
function changePasswordForm(forced) {
  return `<form id="pw" class="${forced ? '' : ''}" novalidate>
    <div class="form-error" hidden></div>
    ${field('cur', forced ? 'Hasło tymczasowe' : 'Obecne hasło', '<input class="input" id="cur" type="password" autocomplete="current-password" required>')}
    ${field('nw', 'Nowe hasło', '<input class="input" id="nw" type="password" autocomplete="new-password" required minlength="10">', 'Co najmniej 10 znaków')}
    ${field('nw2', 'Powtórz nowe hasło', '<input class="input" id="nw2" type="password" autocomplete="new-password" required>')}
  </form>`;
}

async function submitPassword(root) {
  const err = $('.form-error', root);
  err.hidden = true;
  if (val(root, 'nw') !== val(root, 'nw2')) { err.textContent = 'Nowe hasła nie są takie same.'; err.hidden = false; return false; }
  try { await api('/me/password', { method: 'POST', body: { currentPassword: $('#cur', root).value, newPassword: $('#nw', root).value } }); return true; }
  catch (e) { err.textContent = e.message; err.hidden = false; return false; }
}

function openAccountMenu(anchor) {
  openMenu(anchor, [
    { label: 'Zmień hasło', icon: I.key, run: () => openDialog(`<div class="d-body"><h2>Zmiana hasła</h2>${changePasswordForm(false)}</div>
        <div class="d-foot"><button class="btn ghost" data-close>Anuluj</button><button class="btn primary" id="ok">Zmień hasło</button></div>`,
        dlg => $('#ok', dlg).onclick = async () => { if (await submitPassword(dlg)) { dlg.close(); toast('Hasło zmienione'); } }) },
    '-',
    { label: 'Wyloguj', icon: I.out, run: async () => { await api('/logout', { method: 'POST' }); location.href = '/login.html'; } },
  ]);
}

function renderForcedPassword() {
  $('#shell').hidden = true;
  let host = $('#forced');
  if (!host) { host = document.createElement('div'); host.id = 'forced'; document.body.prepend(host); }
  host.innerHTML = `<div class="auth" style="grid-template-columns:1fr"><section class="form"><div>
    <h2>Ustaw własne hasło</h2><p class="lead">Logujesz się hasłem tymczasowym. Zanim zaczniesz, ustaw nowe – znane tylko Tobie.</p>
    ${changePasswordForm(true)}<button class="btn primary" id="ok">Zapisz i przejdź dalej</button>
    <p style="text-align:center;margin-top:18px"><a href="#" id="lo" class="muted">Wyloguj</a></p></div></section></div>`;
  $('#ok', host).onclick = async () => { if (await submitPassword(host)) { host.remove(); S.me.mustChangePassword = false; await boot(); } };
  $('#lo', host).onclick = async e => { e.preventDefault(); await api('/logout', { method: 'POST' }); location.href = '/login.html'; };
  $('#nw2', host).addEventListener('keydown', e => { if (e.key === 'Enter') $('#ok', host).click(); });
}

/* ---------- routing / start ---------- */
const PAGES = () => [
  { id: 'visits', label: 'Goście', icon: I.visits, render: renderVisits, count: () => S.visits.filter(inside).length || '' },
  ...(S.me.canManageUsers ? [{ id: 'users', label: 'Użytkownicy', icon: I.users, render: renderUsers, count: () => S.users.length }] : []),
  ...(S.me.isBuildingAdmin ? [{ id: 'companies', label: 'Firmy', icon: I.companies, render: renderCompanies, count: () => S.companies.length },
                              { id: 'config', label: 'Konfiguracja C4', icon: I.gear, render: renderConfig, count: () => '' }] : []),
];

function route() {
  if (S.me.mustChangePassword) return renderForcedPassword();
  $('#shell').hidden = false;
  const pages = PAGES(), id = location.hash.slice(2) || 'visits';
  const page = pages.find(p => p.id === id) || pages[0];
  $('#nav').innerHTML = pages.map(p => `<a href="#/${p.id}" ${p === page ? 'aria-current="page"' : ''}>${p.icon}<span>${p.label}</span><span class="count">${p.count()}</span></a>`).join('');
  closeMenu();
  page.render();
}

async function boot() {
  S.me = await api('/me');
  if (S.me.mustChangePassword) return route();
  $('#siteName').textContent = S.me.siteName;
  $('#meBtn').innerHTML = `<span class="mono-av">${esc(initials(...S.me.displayName.split(' ')))}</span>
    <span class="who"><b>${esc(S.me.displayName)}</b><span>${esc(S.me.companyName || ROLE[S.me.role])}</span></span>`;
  $('#meBtn').onclick = e => openAccountMenu(e.currentTarget);
  $('#meBtn').dataset.menu = '';
  [S.zones] = await Promise.all([api('/zones'), loadCompanies(), loadVisits(), loadUsers()]);
  route();
  checkC4();
  setInterval(async () => { try { await loadVisits(); if ((location.hash.slice(2) || 'visits') === 'visits' && !document.querySelector('.drawer, dialog[open]')) renderVisitList(); } catch { } }, 30000);
  setInterval(checkC4, 60000);
}

addEventListener('hashchange', () => S.me && route());
document.addEventListener('keydown', e => {
  if (e.key.toLowerCase() === 'n' && !e.ctrlKey && !e.metaKey && !e.altKey && !e.target.closest('input,select,textarea')
      && !document.querySelector('.drawer, dialog[open]') && S.me && !S.me.mustChangePassword) { e.preventDefault(); location.hash = '#/visits'; openInvite(); }
});

boot().catch(e => { if (!/Sesja/.test(e.message)) { console.error(e); toast(e.message, true); } });
