/* Player screen: lobby + live game. All truth comes from the server; this script only renders. */
$(function () {
  var code = $('#app').data('code'), S = null, conn = null, claiming = {}, hl = 0;
  var STATUS = { Lobby: ['Lobby', 'ok'], Running: ['Live', 'ok'], Paused: ['Paused', 'warn'], Completed: ['Completed', ''], Cancelled: ['Cancelled', 'bad'] };

  function noHl() { return S.cutMode === 'ManualNoHighlight'; }   // called numbers are not highlighted; no "to go"/ready hints either
  function isCut(t, n) { return S.cutMode === 'Auto' ? S.called.indexOf(n) > -1 : t.marks.indexOf(n) > -1; }
  function hasCalled(v) { return S.called.indexOf(v) > -1; }
  function rowOf(t, r) { return t.grid.slice(r * 9, r * 9 + 9).filter(Boolean); }
  function miss(a) { return a.filter(function (v) { return !hasCalled(v); }).length; }

  /** How many numbers this ticket still needs for a pattern (0 = complete). UX only: the server re-validates every claim. */
  function missing(t, pattern) {
    switch (pattern) {
      case 'FourCorners': var a = rowOf(t, 0), b = rowOf(t, 2); return miss([a[0], a[a.length - 1], b[0], b[b.length - 1]]);
      case 'FirstRow': return miss(rowOf(t, 0));
      case 'SecondRow': return miss(rowOf(t, 1));
      case 'ThirdRow': return miss(rowOf(t, 2));
      case 'FullHouse': return miss(t.grid.filter(Boolean));
      case 'EarlyFive': return Math.max(0, 5 - t.grid.filter(function (v) { return v && hasCalled(v); }).length);
    }
    var required = T.patternCells(pattern).map(function (cell) { return t.grid[cell[0] * 9 + cell[1]]; }).filter(Boolean);
    return required.length ? miss(required) : 99;
  }
  /** ids of this player's tickets that already won the prize (a prize can be won once per ticket) */
  function wonBy(pattern) { return (S.wonTickets && S.wonTickets[pattern]) || []; }
  function openPrize(p) { return !p.closed && p.winners.length < p.winnerCount; }

  /** closest open prize across all tickets, e.g. "4 to go · Four Corners" (shown once above the tickets, not in each ticket header) */
  function goalHint() {
    var best = null;
    S.tickets.forEach(function (t) {
      S.prizes.forEach(function (p) {
        if (!openPrize(p) || wonBy(p.pattern).indexOf(t.id) > -1) return;
        var n = missing(t, p.pattern); if (!best || n < best.n) best = { n: n, name: p.name };
      });
    });
    if (!best || best.n >= 99) return '';
    return best.n === 0 ? '<span class="rdy">▦ ✔ ' + T.esc(best.name) + ' ready!</span>' : '<span class="togo">▦ ' + best.n + ' to go • ' + T.esc(best.name) + '</span>';
  }

  function renderTickets() {
    var h = '', last = S.called[S.called.length - 1];
    S.tickets.forEach(function (t) {
      var won = S.prizes.filter(function (p) { return wonBy(p.pattern).indexOf(t.id) > -1; });
      h += '<div class="t3' + (won.length ? ' won' : '') + '"><div class="thead"><b>🎫 Ticket #' + t.serial + '</b></div>' +
        (won.length ? '<div class="wons">' + won.map(function (p) { return '<span class="tb">🏆 ' + T.esc(p.name) + '</span>'; }).join(' ') + '</div>' : '') + '<div class="tgrid">';
      t.grid.forEach(function (v) {
        if (!v) { h += '<div class="blank"></div>'; return; }
        var cut = isCut(t, v), isCalled = hasCalled(v);
        h += '<div class="num ' + (cut ? 'cut' : isCalled && !noHl() ? 'called' : '') + (S.cutMode !== 'Auto' ? ' manual' : '') + (v === hl ? ' hl' : '') + (v === last ? ' new' : '') + '" data-t="' + t.id + '" data-n="' + v + '">' + v + '</div>';
      });
      h += '</div></div>';
    });
    if (!S.tickets.length) h = '<p class="sub">You have no tickets yet. Press “Get a ticket”.</p>';
    $('#tix').html(h);
    $('#tcount').text(S.tickets.length + '/' + S.maxTickets);
    var g = S.tickets.length && !noHl() ? goalHint() : ''; $('#goal').html(g).toggle(!!g);
    var canBuy = S.tickets.length < S.maxTickets && (S.status === 'Lobby' || (S.status === 'Running' && S.called.length <= S.lateJoinMaxCalls));
    $('#buy').toggle(canBuy).text('➕ Get a ticket (' + S.tickets.length + '/' + S.maxTickets + ')');
  }

  function renderPrizes() {
    var h = '', running = S.status === 'Running', ready = 0;
    S.prizes.forEach(function (p) {
      var m = S.mine[p.pattern], why = '', locked = false, won = wonBy(p.pattern);
      if (m === 'won') { locked = true; why = 'All tickets won'; }
      else if (m === 'barred') { locked = true; why = 'Locked'; }
      else if (p.closed) { locked = true; why = 'Closed'; }
      else if (!running) { locked = true; why = S.status === 'Lobby' ? 'Not started' : 'Not running'; }
      else if (!S.tickets.length) { locked = true; why = 'No ticket'; }
      var isReady = !locked && !noHl() && S.tickets.some(function (t) { return won.indexOf(t.id) < 0 && missing(t, p.pattern) === 0; });
      if (isReady) ready++;
      h += '<div class="pr' + (isReady ? ' ready' : '') + (locked ? ' off' : '') + '">' + T.patternPreview(p.pattern) +
        '<div class="nm">' + T.esc(p.name) + '<small>' + (p.reward ? T.esc(p.reward) + ' · ' : '') + p.winners.length + '/' + p.winnerCount + ' winners' +
        (S.tickets.length > 1 || won.length ? ' · <span class="mine">you ' + won.length + '/' + S.tickets.length + '</span>' : '') +
        (p.winners.length ? '<br>' + T.esc(p.winners.join(', ')) : '') + '</small></div>' +
        '<button class="btn sm ' + (locked ? 'locked' : 'primary') + '" data-p="' + p.pattern + '" ' + (locked ? 'disabled title="' + why + '"' : '') + '>' + (locked ? '🔒 ' + why : isReady ? '🎯 Claim' : 'Claim') + '</button></div>';
    });
    $('#prizes').html(h);
    $('.readycount').text(ready).toggle(ready > 0);
  }

  function renderHeader() {
    var st = STATUS[S.status] || [S.status, '']; $('#statusBadge').text(st[0]).attr('class', 'badge ' + st[1]);
    $('#hostName').text(S.host); $('#callLbl').text(S.callMode === 'Auto' ? 'Auto (every ' + S.interval + 's)' : 'Manual');
    $('#cutLbl').text(S.cutMode === 'Auto' ? 'Auto (cut for you)' : noHl() ? 'Manual (no highlight)' : 'Manual (tap to cut)');
    $('#legendCalled,#legendFind').toggle(!noHl());
    $('#cnt').text(S.called.length);
    var last = S.called[S.called.length - 1];
    T.board(document.getElementById('board'), S.called, last);
    if (hl) $('#board div').filter(function () { return +this.textContent === hl; }).addClass('sel');
    $('#recent').html(S.called.slice(-8, -1).reverse().map(function (n) { return '<span data-n="' + n + '">' + n + '</span>'; }).join(''));
    if (last) { $('#ball').text(last); $('#said').text('Number ' + last + ' called'); }
    var msg = S.status === 'Lobby' ? 'Waiting for the host to start the game. Take your tickets now.' :
      S.status === 'Paused' ? 'The host has paused the game.' : S.status === 'Cancelled' ? 'The host cancelled this game.' : '';
    $('#banner').text(msg).toggle(!!msg);
    if (S.status === 'Completed' || S.status === 'Cancelled') setTimeout(function () { location.href = '/Rooms/Results/' + code; }, 2500);
  }

  function renderPlayers() {
    $('#pcount').text(S.players.length);
    $('#players').html(S.players.slice(0, 30).map(function (p) { return '<li><span class="dot"></span>' + T.esc(p.name) + (p.id === S.myId ? ' (you)' : '') + '</li>'; }).join('') +
      (S.players.length > 30 ? '<li class="hint">+ ' + (S.players.length - 30) + ' more…</li>' : ''));
  }

  function renderAll() { renderHeader(); renderTickets(); renderPrizes(); renderPlayers(); }

  $('#tix').on('click', '.num.manual', function () {
    var t = +$(this).data('t'), n = +$(this).data('n');
    if (S.called.indexOf(n) < 0) { T.toast(n + ' has not been called yet'); return; }
    conn.invoke('Mark', code, t, n).then(function (marks) {
      S.tickets.filter(function (x) { return x.id === t; })[0].marks = marks; renderTickets();
    }).catch(function (e) { T.toast(T.err(e)); });
  });

  $('#buy').on('click', function () {
    var b = $(this).prop('disabled', true);
    conn.invoke('BuyTicket', code).then(function (t) { S.tickets.push(t); renderTickets(); renderPrizes(); })
      .catch(function (e) { T.toast(T.err(e)); });
    setTimeout(function () { b.prop('disabled', false); }, 600);
  });

  $('#prizes').on('click', 'button[data-p]:not(:disabled)', function () {
    var btn = $(this), pattern = btn.data('p');
    if (claiming[pattern]) return; claiming[pattern] = true;
    btn.prop('disabled', true).text('Checking…');                       // immediate lock, no double-click
    // claim with a ticket that has not won this prize yet and looks complete; the server re-validates everything
    var ticket = bestTicket(pattern);
    conn.invoke('Claim', code, ticket.id, pattern).then(function (r) {
      if (r.status === 'won') {
        T.confetti(); T.sfx('win');
        $('#winTxt').html('You won <b>' + T.esc(r.prize) + '</b> with <b>ticket #' + ticket.serial + '</b> — winner <b>' + r.slot + ' of ' + r.of + '</b>!' + (r.reward ? '<br>🎁 ' + T.esc(r.reward) : '') +
          (S.tickets.length > 1 ? '<br>You can win it again with another complete ticket.' : ''));
        T.modal('win', true);
      } else if (r.status === 'late') {
        $('#lateTxt').html('Your claim for <b>' + T.esc(r.prize) + '</b> was correct, but other players got there first.<br><b>Be quicker next time — claim the moment you complete a line!</b>');
        T.sfx('late'); T.modal('late', true);
      } else if (r.status === 'invalid') {
        if (r.locked) S.mine[pattern] = 'barred';          // otherwise the player may try again (limit set by the host)
        $('#badTxt').text(r.message); T.sfx('bad'); T.modal('bad', true);
      } else { T.toast(r.message); if (/locked/i.test(r.message)) S.mine[pattern] = 'barred'; }
    }).catch(function (e) { T.toast(T.err(e)); })
      .then(function () { claiming[pattern] = false; return conn.invoke('GetState', code).then(function (s) { S = s; renderAll(); }); });
  });

  // UX helper only: a ticket that has not won this prize yet, preferring one that looks complete (the server re-validates)
  function bestTicket(pattern) {
    var won = wonBy(pattern), free = S.tickets.filter(function (t) { return won.indexOf(t.id) < 0; });
    return free.filter(function (t) { return missing(t, pattern) === 0; })[0] || free[0] || S.tickets[0];
  }

  // tap a number (board, recent chips, or a ticket when tapping does not cut) to highlight it on every ticket
  $(document).on('click', '#board div, #recent span, .tgrid .num:not(.manual)', function () {
    if (!S || noHl()) return;                       // "find it yourself" rooms: no tap-to-locate helper
    var v = +($(this).data('n') || this.textContent); hl = hl === v ? 0 : v; renderHeader(); renderTickets();
  });
  T.initTabs('prizes');

  T.connect(code, {
    NumberCalled: function (m) {
      S.called.push(m.number); $('#ball').text(m.number).removeClass('pop'); void $('#ball')[0].offsetWidth; $('#ball').addClass('pop');
      $('#said').text('Number ' + m.number + ' called'); T.speak(m.number); T.sfx('call');
      T.feed('Number <b>' + m.number + '</b> called'); renderHeader(); renderTickets(); renderPrizes();
    },
    PrizesChanged: function (p) { S.prizes = p; renderPrizes(); renderTickets(); },
    PlayersChanged: function (p) { S.players = p; renderPlayers(); },
    // a single join is sent as a delta (a full list per join does not scale to 1500 players)
    PlayerJoined: function (p) { if (!S.players.some(function (x) { return x.id === p.id; })) { S.players.push(p); renderPlayers(); } },
    Feed: function (t) { T.feed(T.esc(t)); },
    StatusChanged: function () { conn.invoke('GetState', code).then(function (s) { S = s; renderAll(); }); },
    PlayerRemoved: function (m) { if (m.id === S.myId) { alert('You were removed from this room by the host.'); location.href = '/'; } }
  }, function (s) { S = s; renderAll(); }).then(function (c) { conn = c; });
});
