/* Shared shell for the Tambola mockups: navbar, theme toggle, decor, tabs, boards. */
(function () {
  var $ = function (s, r) { return (r || document).querySelector(s); };
  var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };
  var root = document.documentElement;

  // ----- theme (remembered per browser; works without storage) -----
  var KEY = 'tambola-mock-theme';
  function getSaved() { try { return localStorage.getItem(KEY); } catch (e) { return null; } }
  function save(v) { try { localStorage.setItem(KEY, v); } catch (e) {} }
  if (getSaved() === 'light') root.setAttribute('data-theme', 'light');

  // ----- navbar -----
  var page = document.body.getAttribute('data-page') || '';
  var bare = document.body.hasAttribute('data-bare');
  var links = [
    ['index', 'index.html', '🏠', 'Home'],
    ['history', 'my-games.html', '🎮', 'My Games'],
    ['help', 'help.html', '❓', 'Help'],
    ['create', 'create-room.html', '➕', 'Create Room'],
    ['admin', 'admin-hosts.html', '🛡️', 'Admin']
  ];
  var nav = document.createElement('div');
  nav.className = 'nav';
  var h = '<a class="logo" href="index.html">Tambola <small><i class="chip" style="background:#d9304f">7</i><i class="chip" style="background:#2563eb">5</i><i class="chip" style="background:#f59e0b">9</i></small></a>';
  if (!bare) {
    links.forEach(function (l) {
      var on = page === l[0] || (l[0] === 'admin' && page.indexOf('admin') === 0) || (l[0] === 'index' && (page === 'play' || page === 'host' || page === 'results'));
      h += '<a class="lnk' + (on ? ' on' : '') + '" href="' + l[1] + '">' + l[2] + ' <span class="lbl">' + l[3] + '</span></a>';
    });
    h += '<div class="sp"></div>' +
      '<button class="pill" type="button">👤 <span class="lbl">KEYWORDSINTL\\demoUser123 ▾</span></button>' +
      '<a class="pill lnk" href="change-password.html">🔒 <span class="lbl">Password</span></a>' +
      '<a class="pill lnk" href="login.html">↪ <span class="lbl">Sign out</span></a>';
  } else { h += '<div class="sp"></div>'; }
  h += '<button class="pill round" id="theme" type="button" title="Toggle theme"></button>';
  nav.innerHTML = h;
  document.body.insertBefore(nav, document.body.firstChild);

  var tb = $('#theme');
  function paintTheme() { tb.textContent = root.getAttribute('data-theme') === 'light' ? '🌙' : '☀'; }
  paintTheme();
  tb.onclick = function () {
    var light = root.getAttribute('data-theme') === 'light';
    if (light) root.removeAttribute('data-theme'); else root.setAttribute('data-theme', 'light');
    save(light ? 'dark' : 'light'); paintTheme();
  };

  // ----- decor (balls + confetti) -----
  var d = '';
  [['23', 120, '-14px', null, '-14px', null, '#d946ef', '#6b1d85'],
   ['7', 120, '210px', null, '-34px', null, '#2f8bff', '#0a3fa8'],
   ['46', 96, null, '130px', '-10px', null, '#22c55e', '#0d6b2e'],
   ['17', 100, null, '-22px', '-30px', null, '#f5b301', '#9a6200']].forEach(function (b) {
    d += '<div class="deco ball" style="width:' + b[1] + 'px;height:' + b[1] + 'px;font-size:' + (b[1] / 2.7) + 'px;' +
      (b[2] ? 'left:' + b[2] + ';' : '') + (b[3] ? 'right:' + b[3] + ';' : '') + 'bottom:' + b[4] + ';--c:' + b[6] + ';--d:' + b[7] + '">' + b[0] + '</div>';
  });
  d += '<span class="conf" style="left:160px;bottom:150px;background:#f5b301;transform:rotate(25deg)"></span>' +
       '<span class="conf" style="left:170px;bottom:100px;background:#8b5cf6;transform:rotate(-30deg)"></span>' +
       '<span class="conf" style="right:300px;bottom:50px;background:#ef4444;transform:rotate(40deg)"></span>' +
       '<span class="conf" style="right:80px;bottom:200px;background:#f5b301;transform:rotate(-20deg)"></span>';
  document.body.insertAdjacentHTML('beforeend', d);

  // ----- tabs: <div class="tabroot"> with .tabs button[data-tab] and .pane[data-pane] -----
  function show(rootEl, tab) {
    $$('.tabs button', rootEl).forEach(function (x) { x.classList.toggle('on', x.getAttribute('data-tab') === tab); });
    $$('.pane', rootEl).forEach(function (p) { p.classList.toggle('on', p.getAttribute('data-pane') === tab); });
  }
  $$('.tabroot').forEach(function (r) {
    $$('.tabs button', r).forEach(function (b) { b.onclick = function () { show(r, b.getAttribute('data-tab')); syncDock(b.getAttribute('data-tab')); }; });
  });
  var dock = $('#dock');
  function syncDock(tab) { if (dock) $$('button', dock).forEach(function (x) { x.classList.toggle('on', x.getAttribute('data-tab') === tab); }); }
  if (dock) {
    $$('button', dock).forEach(function (b) {
      b.onclick = function () {
        var t = b.getAttribute('data-tab'), r = $('.tabroot');
        syncDock(t);
        if (t === 'home') { window.scrollTo({ top: 0, behavior: 'smooth' }); return; }
        show(r, t); r.scrollIntoView({ behavior: 'smooth' });
      };
    });
  }

  // ----- pill toggles (Live/Past, Manual/Auto) -----
  $$('.pill-toggle').forEach(function (g) {
    $$('button', g).forEach(function (b) {
      b.onclick = function () {
        $$('button', g).forEach(function (x) { x.classList.toggle('on', x === b); });
        var t = b.getAttribute('data-show');
        if (t) $$('[data-view]').forEach(function (v) { v.style.display = v.getAttribute('data-view') === t ? '' : 'none'; });
      };
    });
  });

  // ----- 1..90 boards: <div class="board" data-called="7,23,46"> -----
  $$('.board[data-called]').forEach(function (el) {
    var called = el.getAttribute('data-called').split(',').filter(Boolean).map(Number), s = '';
    for (var n = 1; n <= 90; n++) s += '<div data-n="' + n + '"' + (called.indexOf(n) > -1 ? ' class="on"' : '') + '>' + n + '</div>';
    el.innerHTML = s;
  });
  // tapping a board number highlights it on the tickets (Play page)
  $$('.board[data-find]').forEach(function (b) {
    b.addEventListener('click', function (e) {
      var n = e.target.getAttribute('data-n'); if (!n) return;
      $$('.c[data-n]').forEach(function (c) { c.style.outline = c.getAttribute('data-n') === n ? '3px solid #fff' : ''; });
    });
  });
})();
