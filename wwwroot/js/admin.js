(() => {
  const btn = document.getElementById('btnToggleSidebar');
  const sidebar = document.querySelector('.sidebar');
  if (btn && sidebar) {
    btn.addEventListener('click', () => sidebar.classList.toggle('open'));
  }

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
(function(){
  function text(el){ return (el && el.textContent || '').toLowerCase().trim(); }
  function unique(values){ return Array.from(new Set(values.filter(Boolean))).slice(0,80); }
  function label(en, ar){ return (document.documentElement.dir === 'rtl') ? ar : en; }
  function makeOption(v){ const o=document.createElement('option'); o.value=v; o.textContent=v; return o; }
  function ensureFilters(){
    const page = document.querySelector('.admin-page');
    if(!page || page.querySelector('.ghars-filter-panel')) return;
    const table = page.querySelector('table');
    if(!table) return;
    const rows = Array.from(table.querySelectorAll('tbody tr'));
    if(!rows.length) return;
    const pageTitle = document.querySelector('.admin-topbar .fw-semibold')?.textContent?.trim() || label('Filters','التصفية');
    const rowTexts = rows.map(r => text(r));
    const statuses = unique(rows.map(r => r.querySelector('.badge, [class*=status], td:nth-last-child(2), td:nth-last-child(1)')?.textContent?.trim()).filter(v => v && v.length < 40));
    const types = unique(rows.map(r => r.cells && r.cells.length > 2 ? r.cells[2].textContent.trim() : '').filter(v => v && v.length < 50));
    const firstCol = unique(rows.map(r => r.cells && r.cells.length > 0 ? r.cells[0].textContent.trim() : '').filter(v => v && v.length < 60));
    const panel = document.createElement('div');
    panel.className='ghars-filter-panel';
    panel.innerHTML = `
      <div class="filter-title"><i class="bi bi-funnel"></i><span>${label('Advanced Filters','خيارات التصفية المتقدمة')}</span><span class="badge text-bg-light ms-2">${pageTitle}</span></div>
      <div class="row g-2 align-items-end">
        <div class="col-12 col-md-3"><label class="form-label">${label('Keyword','كلمة البحث')}</label><input type="search" class="form-control form-control-sm ghars-filter-keyword" placeholder="${label('Search all columns','البحث في كل الأعمدة')}"></div>
        <div class="col-6 col-md-2"><label class="form-label">${label('Status','الحالة')}</label><select class="form-select form-select-sm ghars-filter-status"><option value="">${label('All','الكل')}</option></select></div>
        <div class="col-6 col-md-2"><label class="form-label">${label('Type / Category','النوع / الفئة')}</label><select class="form-select form-select-sm ghars-filter-type"><option value="">${label('All','الكل')}</option></select></div>
        <div class="col-6 col-md-2"><label class="form-label">${label('Main entity','الجهة الرئيسية')}</label><select class="form-select form-select-sm ghars-filter-entity"><option value="">${label('All','الكل')}</option></select></div>
        <div class="col-6 col-md-1"><label class="form-label">${label('From','من')}</label><input type="date" class="form-control form-control-sm ghars-filter-from"></div>
        <div class="col-6 col-md-1"><label class="form-label">${label('To','إلى')}</label><input type="date" class="form-control form-control-sm ghars-filter-to"></div>
        <div class="col-6 col-md-1 d-grid"><button type="button" class="btn btn-outline-secondary btn-sm ghars-filter-reset">${label('Reset','مسح')}</button></div>
      </div>
      <div class="small text-muted mt-2">${label('Filters work together on the visible table data.','تعمل خيارات التصفية معاً على البيانات الظاهرة في الجدول.')}</div>
    `;
    table.parentElement.insertBefore(panel, table);
    const statusSel=panel.querySelector('.ghars-filter-status'), typeSel=panel.querySelector('.ghars-filter-type'), entitySel=panel.querySelector('.ghars-filter-entity');
    statuses.forEach(v=>statusSel.appendChild(makeOption(v)));
    types.forEach(v=>typeSel.appendChild(makeOption(v)));
    firstCol.forEach(v=>entitySel.appendChild(makeOption(v)));
    function rowDate(r){ const m=(r.textContent||'').match(/\b(20\d{2})[-\/](\d{1,2})[-\/](\d{1,2})\b|\b(\d{1,2})[-\/](\d{1,2})[-\/](20\d{2})\b/); if(!m) return ''; if(m[1]) return `${m[1]}-${String(m[2]).padStart(2,'0')}-${String(m[3]).padStart(2,'0')}`; return `${m[6]}-${String(m[4]).padStart(2,'0')}-${String(m[5]).padStart(2,'0')}`; }
    function apply(){
      const kw=panel.querySelector('.ghars-filter-keyword').value.toLowerCase().trim();
      const st=statusSel.value.toLowerCase(), ty=typeSel.value.toLowerCase(), en=entitySel.value.toLowerCase();
      const from=panel.querySelector('.ghars-filter-from').value, to=panel.querySelector('.ghars-filter-to').value;
      let visible=0;
      rows.forEach((r,i)=>{
        const all=rowTexts[i]; const d=rowDate(r);
        let ok=true;
        if(kw && !all.includes(kw)) ok=false;
        if(st && !all.includes(st)) ok=false;
        if(ty && !all.includes(ty)) ok=false;
        if(en && !all.includes(en)) ok=false;
        if(from && d && d < from) ok=false;
        if(to && d && d > to) ok=false;
        r.style.display=ok?'':'none'; if(ok) visible++;
      });
      let empty=page.querySelector('.ghars-filter-empty');
      if(!visible){ if(!empty){ empty=document.createElement('div'); empty.className='ghars-filter-empty'; empty.textContent=label('No records match the selected filters.','لا توجد سجلات مطابقة لخيارات التصفية.'); table.after(empty); } }
      else if(empty) empty.remove();
    }
    panel.addEventListener('input', apply); panel.addEventListener('change', apply);
    panel.querySelector('.ghars-filter-reset').addEventListener('click', ()=>{ panel.querySelectorAll('input,select').forEach(x=>x.value=''); apply(); });
  }
  if(document.readyState==='loading') document.addEventListener('DOMContentLoaded', ensureFilters); else ensureFilters();
})();
