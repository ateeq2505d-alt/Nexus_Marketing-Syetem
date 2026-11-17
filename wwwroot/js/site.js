// Nexus UI behaviour: theme, sidebar, toasts, confirm dialog, table search/sort, quick search, loading buttons.
(function () {
  'use strict';
  var doc = document.documentElement;

  // ---- theme (light / dark), remembered per browser ----
  function applyTheme(t) {
    doc.setAttribute('data-bs-theme', t);
    var i = document.querySelector('#themeBtn i'); if (i) i.className = t === 'dark' ? 'bi bi-sun' : 'bi bi-moon-stars';
  }
  var saved = null; try { saved = localStorage.getItem('nx-theme'); } catch (e) { }
  applyTheme(saved || (window.matchMedia && matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'));
  window.toggleTheme = function () {
    var n = doc.getAttribute('data-bs-theme') === 'dark' ? 'light' : 'dark';
    applyTheme(n); try { localStorage.setItem('nx-theme', n); } catch (e) { }
  };

  // ---- sidebar on small screens ----
  window.toggleSidebar = function () {
    document.getElementById('sidebar').classList.toggle('open');
    document.getElementById('backdrop').classList.toggle('show');
  };

  document.addEventListener('DOMContentLoaded', function () {
    // ---- toasts: auto-dismiss ----
    document.querySelectorAll('.nx-toast').forEach(function (t) {
      var x = t.querySelector('.x'); if (x) x.onclick = function () { t.remove(); };
      setTimeout(function () { t.style.transition = 'opacity .4s'; t.style.opacity = '0'; setTimeout(function () { t.remove(); }, 400); }, t.classList.contains('err') ? 9000 : 6000);
    });

    // ---- confirmation modal for forms with data-confirm ----
    var pending = null, modalEl = document.getElementById('confirmModal');
    if (modalEl) {
      var modal = new bootstrap.Modal(modalEl);
      document.addEventListener('submit', function (e) {
        var f = e.target, msg = f.getAttribute && f.getAttribute('data-confirm');
        if (msg && !f.dataset.confirmed) {
          e.preventDefault(); pending = f;
          document.getElementById('confirmText').textContent = msg; modal.show();
        }
      }, true);
      document.getElementById('confirmYes').onclick = function () {
        if (pending) { pending.dataset.confirmed = '1'; modal.hide(); pending.requestSubmit ? pending.requestSubmit() : pending.submit(); }
      };
    }

    // ---- loading state on submit buttons ----
    document.addEventListener('submit', function (e) {
      var f = e.target; if (e.defaultPrevented) return;
      var b = f.querySelector('button.btn-primary:not([type=button]), button.btn-success, button.btn-danger');
      if (b && !b.classList.contains('loading')) setTimeout(function () { b.classList.add('loading'); }, 0);
    });

    // ---- every table scrolls inside its card instead of widening the page (important on phones) ----
    document.querySelectorAll('.content table.table').forEach(function (t) {
      var wrap = t.closest('.table-responsive');
      if (!wrap) { wrap = document.createElement('div'); wrap.className = 'table-responsive'; t.parentNode.insertBefore(wrap, t); wrap.appendChild(t); }
      var hs = t.tHead && t.tHead.rows[0]; if (hs && !hs.cells[hs.cells.length - 1].textContent.trim()) wrap.classList.add('has-actions');
    });

    // ---- tables: live search + click-to-sort (tables with more than 5 rows) ----
    document.querySelectorAll('.content .card table.table').forEach(function (tbl) {
      var body = tbl.tBodies[0]; if (!body || !tbl.tHead) return;
      var rows = Array.prototype.slice.call(body.rows).filter(function (r) { return r.cells.length > 1; });
      var head = tbl.tHead && tbl.tHead.rows[0];
      if (head) Array.prototype.forEach.call(head.cells, function (th, idx) {
        if (!th.textContent.trim()) return;
        th.classList.add('sortable');
        th.addEventListener('click', function () {
          var asc = !th.classList.contains('asc');
          Array.prototype.forEach.call(head.cells, function (c) { c.classList.remove('asc', 'desc'); });
          th.classList.add(asc ? 'asc' : 'desc');
          var val = function (r) { var t = (r.cells[idx] ? r.cells[idx].textContent : '').trim(); var n = parseFloat(t.replace(/[$,]/g, '')); return isNaN(n) || !/^[-$\d.,\s]+$/.test(t) ? t.toLowerCase() : n; };
          rows.sort(function (a, b) { var x = val(a), y = val(b); return (x > y ? 1 : x < y ? -1 : 0) * (asc ? 1 : -1); });
          rows.forEach(function (r) { body.appendChild(r); });
        });
      });
      if (rows.length > 8) {
        var bar = document.createElement('div'); bar.className = 'table-filter';
        bar.innerHTML = '<div class="wrap"><i class="bi bi-search"></i><input class="form-control form-control-sm" placeholder="Filter this table..."></div><span class="count"></span>';
        var host = tbl.closest('.table-responsive') || tbl; host.parentNode.insertBefore(bar, host);
        var inp = bar.querySelector('input'), cnt = bar.querySelector('.count');
        var update = function () {
          var q = inp.value.toLowerCase(), shown = 0;
          rows.forEach(function (r) { var ok = r.textContent.toLowerCase().indexOf(q) > -1; r.style.display = ok ? '' : 'none'; if (ok) shown++; });
          cnt.textContent = shown + ' of ' + rows.length + ' rows';
        };
        inp.addEventListener('input', update); update();
      }
    });

    // ---- topbar quick search: order id (11 chars) / account id (16 chars) / name ----
    var qs = document.getElementById('quickSearch');
    if (qs) qs.addEventListener('submit', function (e) {
      e.preventDefault(); var v = qs.querySelector('input').value.trim(); if (!v) return;
      var u = v.length === 11 ? '/Home/Search?orderId=' + encodeURIComponent(v) : v.length === 16 ? '/Home/Search?accountId=' + encodeURIComponent(v)
        : (qs.dataset.auth === '1' ? '/Home/Advanced?kind=order&name=' + encodeURIComponent(v) : '/Home/Search?orderId=' + encodeURIComponent(v));
      location.href = u;
    });
    document.addEventListener('keydown', function (e) { if (e.key === '/' && !/INPUT|TEXTAREA|SELECT/.test(document.activeElement.tagName) && qs) { e.preventDefault(); qs.querySelector('input').focus(); } });

    // ---- login: demo account chips ----
    document.querySelectorAll('[data-demo]').forEach(function (c) {
      c.addEventListener('click', function () {
        var p = c.dataset.demo.split('|'); document.querySelector('input[name=username]').value = p[0]; document.querySelector('input[name=password]').value = p[1];
      });
    });

    // readable status badges: NotFeasible -> Not feasible
    document.querySelectorAll('.badge').forEach(function (b) { if (b.children.length === 0 && /^[A-Z][a-z]+([A-Z][a-z]+)+$/.test(b.textContent.trim())) b.textContent = b.textContent.trim().replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^(\w+) (\w)/, function (m, a, c) { return a + ' ' + c.toLowerCase(); }); });
    // bring server-side form errors into view
    var fe = document.querySelector('.form-errors'); if (fe) fe.scrollIntoView({ behavior: 'smooth', block: 'center' });
    // animate bars
    document.querySelectorAll('.bar-row .fill').forEach(function (f) { var w = f.dataset.w; f.style.width = '0'; setTimeout(function () { f.style.width = w; }, 60); });
    document.querySelectorAll('.content > *').forEach(function (el, i) { el.classList.add('fade-in'); el.style.animationDelay = Math.min(i * 40, 240) + 'ms'; });
  });
})();
