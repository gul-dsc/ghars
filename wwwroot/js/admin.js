(() => {
  // The sidebar toggle is a Bootstrap offcanvas trigger declared in _AdminLayout; the handler that
  // used to live here targeted '.sidebar', a class the admin layout does not use, and never ran.

  // SignalR notifications
  try {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/notifications")
      .withAutomaticReconnect()
      .build();

    const host = document.getElementById('notifToastHost');
    const dot = document.getElementById('notifDot');

    connection.on("notification", (payload) => {
      if (dot) dot.classList.remove('d-none');

      if (!host) return;

      const div = document.createElement('div');
      div.className = "alert alert-info alert-dismissible fade show";
      div.setAttribute("role","alert");
      div.innerHTML = `
        <div class="fw-semibold">${payload.titleEn ?? "Notification"}</div>
        <div class="small">${payload.messageEn ?? ""}</div>
        ${payload.linkUrl ? `<div class="mt-2"><a class="small" href="${payload.linkUrl}">Open</a></div>` : ""}
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
      `;
      host.prepend(div);
      setTimeout(() => { try { div.remove(); } catch {} }, 9000);
    });

    connection.start().catch(() => {});
  } catch (e) {
    // SignalR not available
  }
})();

// Universal advanced filtering for Ghars admin list/report pages.
//
// This runs on every admin table and cannot know a page's business vocabulary, so each column
// filter is named after the table's own header cell rather than after a guessed business meaning.
// A column whose header is blank, or which yields no values, gets no control at all: an unnamed or
// empty filter is worse than a missing one. Every control carries a deterministic unique id and a
// matching <label for>, so each has an accessible name and no id is ever duplicated.
(function(){
  let panelSeq = 0;

  function text(el){ return (el && el.textContent || '').toLowerCase().trim(); }
  function unique(values){ return Array.from(new Set(values.filter(Boolean))).slice(0,80); }
  function label(en, ar){ return (document.documentElement.dir === 'rtl') ? ar : en; }
  function makeOption(v){ const o=document.createElement('option'); o.value=v; o.textContent=v; return o; }
  function cellText(r, i){ return (r.cells && r.cells.length > i && i >= 0) ? r.cells[i].textContent.trim() : ''; }

  // A column's accessible name comes from its header cell. Header cells that only hold an icon or
  // an "Actions" affordance are blank once trimmed, and those columns are skipped.
  function headerText(table, i){
    const ths = table.querySelectorAll('thead th');
    if(i < 0 || i >= ths.length) return '';
    return (ths[i].textContent || '').replace(/\s+/g,' ').trim();
  }

  // Status is rendered as a badge throughout the admin area; find which column holds it.
  function badgeColumnIndex(rows){
    for(const r of rows){
      if(!r.cells) continue;
      for(let i=0;i<r.cells.length;i++){ if(r.cells[i].querySelector('.badge')) return i; }
    }
    return -1;
  }

  function ensureFilters(){
    const page = document.querySelector('.admin-page');
    if(!page || page.querySelector('.ghars-filter-panel')) return;
    const table = page.querySelector('table');
    if(!table) return;
    const rows = Array.from(table.querySelectorAll('tbody tr'));
    if(!rows.length) return;

    const uid = `ghars-filter-${++panelSeq}`;
    const pageTitle = document.querySelector('.admin-topbar .fw-semibold')?.textContent?.trim() || label('Filters','التصفية');
    const rowTexts = rows.map(r => text(r));

    // Each column filter: the column it reads, the name it takes from that column's header, and
    // the values found there. Anything without both a header and values is dropped below.
    const columns = [
      { key:'status', idx: badgeColumnIndex(rows), fallback: label('Status','الحالة'), max: 40 },
      { key:'type',   idx: 2,                     fallback: '',                        max: 50 },
      { key:'entity', idx: 0,                     fallback: '',                        max: 60 },
    ].map(c => ({
      ...c,
      name: headerText(table, c.idx) || c.fallback,
      values: c.idx < 0 ? [] : unique(rows.map(r => cellText(r, c.idx)).filter(v => v && v.length < c.max)),
    })).filter(c =>
      c.name &&
      c.values.length > 1 &&                                  // one value filters nothing
      !c.values.every(v => /^[\d.,%\s]+$/.test(v))            // an id or count column is not a filter
    );

    const panel = document.createElement('div');
    panel.className='ghars-filter-panel';
    panel.innerHTML = `
      <div class="filter-title"><i class="bi bi-funnel" aria-hidden="true"></i><span>${label('Advanced Filters','خيارات التصفية المتقدمة')}</span><span class="badge text-bg-light ms-2">${pageTitle}</span></div>
      <div class="row g-2 align-items-end">
        <div class="col-12 col-md-3">
          <label class="form-label" for="${uid}-search">${label('Search','بحث')}</label>
          <input type="search" id="${uid}-search" class="form-control form-control-sm ghars-filter-keyword" placeholder="${label('Search all columns','البحث في كل الأعمدة')}">
        </div>
        ${columns.map(c => `
        <div class="col-6 col-md-2">
          <label class="form-label" for="${uid}-${c.key}">${c.name}</label>
          <select id="${uid}-${c.key}" class="form-select form-select-sm ghars-filter-${c.key}"><option value="">${label('All','الكل')}</option></select>
        </div>`).join('')}
        <div class="col-6 col-md-1">
          <label class="form-label" for="${uid}-from">${label('From','من')}</label>
          <input type="date" id="${uid}-from" class="form-control form-control-sm ghars-filter-from">
        </div>
        <div class="col-6 col-md-1">
          <label class="form-label" for="${uid}-to">${label('To','إلى')}</label>
          <input type="date" id="${uid}-to" class="form-control form-control-sm ghars-filter-to">
        </div>
        <div class="col-6 col-md-1 d-grid"><button type="button" class="btn btn-outline-secondary btn-sm ghars-filter-reset">${label('Reset','مسح')}</button></div>
      </div>
      <div class="small text-muted mt-2">${label('Filters work together on the visible table data.','تعمل خيارات التصفية معاً على البيانات الظاهرة في الجدول.')}</div>
    `;
    // Insert above the table but outside its horizontal scroll container, so the filter controls
    // keep full width and do not scroll away with a wide table.
    const anchor = table.closest('.table-responsive') || table;
    anchor.parentElement.insertBefore(panel, anchor);

    const selects = columns.map(c => {
      const el = panel.querySelector(`#${CSS.escape(uid + '-' + c.key)}`);
      c.values.forEach(v => el.appendChild(makeOption(v)));
      return el;
    });

    function rowDate(r){ const m=(r.textContent||'').match(/\b(20\d{2})[-\/](\d{1,2})[-\/](\d{1,2})\b|\b(\d{1,2})[-\/](\d{1,2})[-\/](20\d{2})\b/); if(!m) return ''; if(m[1]) return `${m[1]}-${String(m[2]).padStart(2,'0')}-${String(m[3]).padStart(2,'0')}`; return `${m[6]}-${String(m[4]).padStart(2,'0')}-${String(m[5]).padStart(2,'0')}`; }
    function apply(){
      const kw=panel.querySelector('.ghars-filter-keyword').value.toLowerCase().trim();
      const picked=selects.map(s=>s.value.toLowerCase()).filter(Boolean);
      const from=panel.querySelector('.ghars-filter-from').value, to=panel.querySelector('.ghars-filter-to').value;
      let visible=0;
      rows.forEach((r,i)=>{
        const all=rowTexts[i]; const d=rowDate(r);
        let ok=true;
        if(kw && !all.includes(kw)) ok=false;
        if(picked.some(v => !all.includes(v))) ok=false;
        if(from && d && d < from) ok=false;
        if(to && d && d > to) ok=false;
        r.style.display=ok?'':'none'; if(ok) visible++;
      });
      let empty=page.querySelector('.ghars-filter-empty');
      if(!visible){ if(!empty){ empty=document.createElement('div'); empty.className='ghars-filter-empty'; empty.setAttribute('role','status'); empty.textContent=label('No records match the selected filters.','لا توجد سجلات مطابقة لخيارات التصفية.'); anchor.after(empty); } }
      else if(empty) empty.remove();
    }
    panel.addEventListener('input', apply); panel.addEventListener('change', apply);
    panel.querySelector('.ghars-filter-reset').addEventListener('click', ()=>{ panel.querySelectorAll('input,select').forEach(x=>x.value=''); apply(); });
  }
  if(document.readyState==='loading') document.addEventListener('DOMContentLoaded', ensureFilters); else ensureFilters();
})();
