'use strict';
const $ = id => document.getElementById(id);
const STATUS = { Scheduled: 'Zaplanowana', Active: 'Aktywna w C4', Expired: 'Wygasła', Revoked: 'Cofnięta' };
let profiles = [], visits = [];

async function api(path, opts = {}) {
  const r = await fetch('/api' + path, { ...opts,
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'fetch', ...(opts.headers || {}) } });
  if (r.status === 401) { location.href = '/login.html'; throw new Error('unauthorized'); }
  const body = r.headers.get('content-type')?.includes('json') ? await r.json() : null;
  if (!r.ok) throw new Error(body?.error || body?.detail || ('HTTP ' + r.status));
  return body;
}

const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const fmt = d => new Date(d).toLocaleString('pl-PL', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
const localInput = d => { const x = new Date(d - d.getTimezoneOffset() * 60000); return x.toISOString().slice(0, 16); };

function showMsg(text, ok) { const m = $('msg'); m.textContent = text; m.className = 'msg show ' + (ok ? 'ok' : 'bad'); }

function defaultTimes() {
  const now = new Date(); now.setSeconds(0, 0); now.setMinutes(Math.ceil(now.getMinutes() / 15) * 15);
  $('from').value = localInput(now);
  $('to').value = localInput(new Date(now.getTime() + 4 * 3600e3));
}

function renderRows() {
  const q = $('filter').value.trim().toLowerCase();
  const list = visits.filter(v => !q || [v.firstName, v.lastName, v.company, v.email].join(' ').toLowerCase().includes(q));
  const pname = id => profiles.find(p => p.id === id)?.name ?? id;
  $('rows').innerHTML = list.length ? list.map(v => `
    <tr>
      <td><b>${esc(v.lastName)} ${esc(v.firstName)}</b><div class="sub">${esc(v.company || '')} ${v.company ? '· ' : ''}${esc(v.email)}</div>
          ${v.hostName ? `<div class="sub">do: ${esc(v.hostName)}</div>` : ''}</td>
      <td>${fmt(v.validFrom)}<div class="sub">do ${fmt(v.validTo)}</div></td>
      <td>${esc(pname(v.accessProfileId))}</td>
      <td><span class="badge ${v.status}">${STATUS[v.status] || v.status}</span>
          ${v.emailSent ? '' : '<div class="err">mail niewysłany</div>'}
          ${v.lastError ? `<div class="err" title="${esc(v.lastError)}">${esc(v.lastError.slice(0, 60))}</div>` : ''}</td>
      <td><code>${esc(v.accessCodeMasked)}</code></td>
      <td><div class="actions">
        <button class="small" data-qr="${v.id}">QR</button>
        ${['Scheduled', 'Active'].includes(v.status) ? `
          <button class="small" data-resend="${v.id}">Wyślij ponownie</button>
          <button class="small" data-revoke="${v.id}">Cofnij</button>` : ''}
      </div></td>
    </tr>`).join('') : '<tr><td colspan="6" class="empty">Brak wizyt</td></tr>';
}

async function loadVisits() { visits = await api('/visits'); renderRows(); }

async function loadHealth() {
  try { const h = await api('/health'); $('health').textContent = `C4 (${h.mode}): ${h.ok ? 'OK' : 'BŁĄD'}`;
        $('health').title = h.message; $('health').className = 'health ' + (h.ok ? 'ok' : 'bad'); }
  catch { $('health').textContent = 'C4: brak odpowiedzi'; $('health').className = 'health bad'; }
}

$('form').addEventListener('submit', async e => {
  e.preventDefault();
  const from = new Date($('from').value), to = new Date($('to').value);
  $('submit').disabled = true;
  try {
    const v = await api('/visits', { method: 'POST', body: JSON.stringify({
      firstName: $('firstName').value, lastName: $('lastName').value, email: $('email').value,
      phone: $('phone').value || null, company: $('company').value || null, hostName: $('hostName').value || null,
      accessProfileId: $('profile').value, validFrom: from.toISOString(), validTo: to.toISOString() }) });
    showMsg(`Zarejestrowano: ${v.firstName} ${v.lastName}. ` +
      (v.emailSent ? 'Kod QR wysłany na ' + v.email + '.' : 'UWAGA: mail nie został wysłany – użyj „Wyślij ponownie”.'), v.emailSent);
    ['firstName', 'lastName', 'email', 'phone', 'company'].forEach(id => $(id).value = '');
    await loadVisits();
  } catch (err) { showMsg(err.message, false); }
  finally { $('submit').disabled = false; }
});

$('rows').addEventListener('click', async e => {
  const b = e.target.closest('button'); if (!b) return;
  const v = visits.find(x => x.id === (b.dataset.qr || b.dataset.resend || b.dataset.revoke));
  try {
    if (b.dataset.qr) {
      $('qrTitle').textContent = `${v.firstName} ${v.lastName}`;
      $('qrImg').src = `/api/visits/${v.id}/qr.png`; $('qrDialog').showModal();
    } else if (b.dataset.resend) {
      await api(`/visits/${v.id}/resend`, { method: 'POST' }); await loadVisits();
    } else if (b.dataset.revoke && confirm(`Cofnąć dostęp dla ${v.firstName} ${v.lastName}? Kod przestanie działać.`)) {
      await api(`/visits/${v.id}/revoke`, { method: 'POST' }); await loadVisits();
    }
  } catch (err) { alert(err.message); }
});

$('profile').addEventListener('change', () => $('profileDesc').textContent = profiles.find(p => p.id === $('profile').value)?.description || '');
$('filter').addEventListener('input', renderRows);
$('refresh').addEventListener('click', () => { loadVisits(); loadHealth(); });
$('logout').addEventListener('click', async () => { await api('/logout', { method: 'POST' }); location.href = '/login.html'; });

(async () => {
  const me = await api('/me');
  $('who').textContent = me.displayName; $('site').textContent = 'C4 GuestPass · ' + me.siteName; document.title = $('site').textContent;
  profiles = await api('/profiles');
  $('profile').innerHTML = profiles.map(p => `<option value="${esc(p.id)}">${esc(p.name)}</option>`).join('');
  $('profile').dispatchEvent(new Event('change'));
  defaultTimes();
  await Promise.all([loadVisits(), loadHealth()]);
  setInterval(loadVisits, 30000);
})().catch(e => { if (e.message !== "unauthorized") console.error(e); });
