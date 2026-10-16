/* Shared mock game engine for the three player-screen designs (needs app.js T and v2.js V2).
   Each design supplies its own markup and calls G.init({...}) with the element ids it has. */
var G = {
  S: null, cfg: {}, sound: false, auto: null,
  $: function (id) { return document.getElementById(id); },
  reset: function () {
    G.S = { called: [], hl: 0, tickets: [1, 2, 3, 4, 5, 6].map(function () { return V2.ticket(); }),
      prizes: [['EarlyFive', 2], ['FourCorners', 1], ['FirstRow', 1], ['SecondRow', 1], ['ThirdRow', 1], ['FullHouse', 5]].map(function (p) { return { k: p[0], n: p[1], others: 0, mine: [] }; }) };
    if (G.$('feed')) G.$('feed').innerHTML = '<div>Game started. Good luck! 🍀</div>';
    G.render();
  },
  /** numbers still needed on ticket g for pattern k */
  need: function (g, k, called) {
    var miss = function (v) { return called.indexOf(v) < 0; }, row = function (r) { return g.slice(r * 9, r * 9 + 9).filter(Boolean); };
    if (k === 'EarlyFive') return Math.max(0, 5 - g.filter(function (v) { return v && !miss(v); }).length);
    if (k === 'FourCorners') { var a = row(0), b = row(2); return [a[0], a[a.length - 1], b[0], b[b.length - 1]].filter(miss).length; }
    if (k === 'FirstRow') return row(0).filter(miss).length;
    if (k === 'SecondRow') return row(1).filter(miss).length;
    if (k === 'ThirdRow') return row(2).filter(miss).length;
    return g.filter(Boolean).filter(miss).length;
  },
  info: function (i) {
    var S = G.S, p = S.prizes[i], taken = p.others + p.mine.length, ready = -1;
    S.tickets.forEach(function (g, ti) { if (ready < 0 && p.mine.indexOf(ti) < 0 && G.need(g, p.k, S.called) === 0) ready = ti; });
    var closed = taken >= p.n, all = p.mine.length >= S.tickets.length;
    return { p: p, i: i, name: V2.P[p.k].name, icon: V2.pattern(V2.P[p.k].cells), taken: taken, closed: closed, all: all, ready: closed || all ? -1 : ready, off: closed || all,
      tip: V2.P[p.k].name + ': ' + taken + ' of ' + p.n + ' winners · you won ' + p.mine.length + ' of ' + S.tickets.length + ' possible (one per ticket)' };
  },
  readyCount: function () { var c = 0; G.S.prizes.forEach(function (_, i) { if (G.info(i).ready > -1) c++; }); return c; },
  /** closest open prize for a ticket: text like "2 to go · Full House" */
  hint: function (ti) {
    var S = G.S, best = null;
    S.prizes.forEach(function (p, i) {
      var f = G.info(i); if (f.off || p.mine.indexOf(ti) > -1) return;
      var n = G.need(S.tickets[ti], p.k, S.called);
      if (!best || n < best.n) best = { n: n, name: f.name };
    });
    if (!best) return '';
    return best.n === 0 ? '<span class="rdy">✔ ' + best.name + ' ready!</span>' : '<span class="togo">' + best.n + ' to go · ' + best.name + '</span>';
  },
  ticketHTML: function (ti) {
    var S = G.S, g = S.tickets[ti], last = S.called[S.called.length - 1];
    var won = S.prizes.filter(function (p) { return p.mine.indexOf(ti) > -1; });
    return '<div class="t3 ' + (won.length ? 'won' : '') + '" data-ti="' + ti + '"><div class="thead"><b>Ticket #' + (ti + 1) + '</b><span class="tinfo">' + G.hint(ti) + '</span></div>' +
      (won.length ? '<div class="wons">' + won.map(function (p) { return '<span class="tb">🏆 ' + V2.P[p.k].name + '</span>'; }).join(' ') + '</div>' : '') +
      '<div class="tgrid">' + g.map(function (v) {
        if (!v) return '<div class="blank"></div>';
        return '<div class="num ' + (S.called.indexOf(v) > -1 ? 'cut' : '') + (v === S.hl ? ' hl' : '') + (v === last ? ' new' : '') + '" data-n="' + v + '">' + v + '</div>';
      }).join('') + '</div></div>';
  },
  boardHTML: function () {
    var S = G.S, last = S.called[S.called.length - 1], h = '';
    for (var i = 1; i <= 90; i++) h += '<div data-n="' + i + '" class="' + (S.called.indexOf(i) > -1 ? 'on' : '') + (i === last ? ' last' : '') + (i === S.hl ? ' sel' : '') + '">' + i + '</div>';
    return h;
  },
  render: function () {
    var S = G.S, c = G.cfg, $ = G.$, last = S.called[S.called.length - 1];
    if ($('cnt')) $('cnt').textContent = S.called.length;
    ['ball', 'ball2'].forEach(function (id) { if ($(id)) { $(id).textContent = last || '—'; $(id).classList.remove('pop'); void $(id).offsetWidth; if (last) $(id).classList.add('pop'); } });
    if ($('said')) $('said').textContent = last ? 'Number ' + last + ' called' : 'Waiting for the first call…';
    if ($('recent')) $('recent').innerHTML = S.called.slice(-8, -1).reverse().map(function (n) { return '<span data-n="' + n + '">' + n + '</span>'; }).join('');
    if ($('board')) $('board').innerHTML = G.boardHTML();
    if ($('tix')) $('tix').innerHTML = S.tickets.map(function (_, ti) { return G.ticketHTML(ti); }).join('');
    if ($('tcount')) $('tcount').textContent = S.tickets.length + '/6';
    if ($('prizes')) $('prizes').innerHTML = S.prizes.map(function (_, i) { return c.prizeFn(G.info(i)); }).join('');
    document.querySelectorAll('[data-readycount]').forEach(function (e) { var n = G.readyCount(); e.textContent = n; e.style.display = n ? '' : 'none'; });
    if (c.onRender) c.onRender(G);
  },
  feed: function (t) { var d = document.createElement('div'); d.innerHTML = t; if (G.$('feed')) G.$('feed').prepend(d); if (G.cfg.onFeed) G.cfg.onFeed(); },
  call: function (silent) {
    var S = G.S, x; if (S.called.length >= 90) return;
    do { x = 1 + Math.floor(Math.random() * 90); } while (S.called.indexOf(x) > -1);
    S.called.push(x); G.feed('Number <b>' + x + '</b> called');
    if (G.sound) { try { var a = new Audio('../src/Tambola.Web/wwwroot/audio/hi/' + x + '.mp3'); a.play().catch(function () { }); } catch (e) { } }
    if (!silent) G.render(); if (G.cfg.onCall) G.cfg.onCall(x);
  },
  claim: function (i) {
    var S = G.S, f = G.info(i), p = S.prizes[i];
    if (f.off) return;
    var idx = -1; S.tickets.forEach(function (g, ti) { if (idx < 0 && p.mine.indexOf(ti) < 0 && G.need(g, p.k, S.called) === 0) idx = ti; });
    if (idx < 0) { T.toast('Not valid yet: none of your remaining tickets has ' + f.name + ' complete'); return; }
    p.mine.push(idx); var slot = p.others + p.mine.length;
    G.feed('🏆 You won ' + f.name + ' with ticket #' + (idx + 1) + ' (#' + slot + ' of ' + p.n + ')');
    G.$('winTxt').innerHTML = 'You won <b>' + f.name + '</b> with <b>ticket #' + (idx + 1) + '</b> — winner <b>' + slot + ' of ' + p.n + '</b>!' +
      (p.mine.length < S.tickets.length && slot < p.n ? '<br>You can claim it again with another complete ticket.' : '');
    T.confetti(); T.modal('win', true); G.render();
  },
  demoHTML: function () {
    return '<div class="demo"><b>DEMO</b> · <button class="btn sm" id="dNext">Call next</button> <button class="btn sm" id="dTen">Call 10</button> ' +
      '<label class="row" style="display:inline-flex;margin:0;gap:4px"><input type="checkbox" id="dAuto"> auto-call (3s)</label> ' +
      '<label class="row" style="display:inline-flex;margin:0;gap:4px"><input type="checkbox" id="dSound"> 🔊 funny voice</label> ' +
      '<select id="dPick" style="width:auto;padding:3px 6px;font-size:12px"><option value="0">Ticket #1</option><option value="1">Ticket #2</option><option value="2">Ticket #3</option><option value="3">Ticket #4</option><option value="4">Ticket #5</option><option value="5">Ticket #6</option></select>' +
      '<button class="btn sm" id="dComplete">Complete it</button> <button class="btn sm" id="dRival">Rival takes a Full House</button> <button class="btn sm" id="dReset">Reset</button></div>' +
      '<div class="overlay" id="win"><div class="modal"><div class="big">🎉</div><h2>Congratulations!</h2><p id="winTxt"></p><button class="btn primary" type="button" onclick="T.modal(\'win\',false)">Awesome!</button></div></div>';
  },
  init: function (cfg) {
    G.cfg = cfg; var $ = G.$;
    $('demo').innerHTML = G.demoHTML();
    $('dNext').onclick = function () { G.call(); };
    $('dTen').onclick = function () { for (var i = 0; i < 10; i++) G.call(true); G.render(); };
    $('dAuto').onchange = function () { clearInterval(G.auto); if (this.checked) G.auto = setInterval(function () { G.call(); }, 3000); };
    $('dSound').onchange = function () { G.sound = this.checked; };
    $('dComplete').onclick = function () { var ti = +$('dPick').value; G.S.tickets[ti].filter(Boolean).forEach(function (v) { if (G.S.called.indexOf(v) < 0) G.S.called.push(v); }); G.render(); };
    $('dRival').onclick = function () { var p = G.S.prizes[5]; if (p.others + p.mine.length < p.n) { p.others++; G.feed('🏆 Priya S won Full House (#' + (p.others + p.mine.length) + ' of ' + p.n + ')'); G.render(); } };
    $('dReset').onclick = G.reset;
    document.addEventListener('click', function (e) {          // tap any number (board, recent, ticket) to highlight it on every ticket
      var n = e.target.closest('[data-n]'); if (!n || e.target.closest('.overlay')) return;
      var v = +n.dataset.n; G.S.hl = G.S.hl === v ? 0 : v; G.render();
    });
    V2.collapsibles(); G.reset();
  }
};
