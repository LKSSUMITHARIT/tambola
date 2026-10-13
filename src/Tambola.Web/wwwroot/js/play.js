/* Player screen: lobby + live game. All truth comes from the server; this script only renders. */
$(function () {
  var code = $('#app').data('code'), S = null, conn = null, claiming = {};
  var STATUS = { Lobby: ['Lobby', 'ok'], Running: ['Live', 'ok'], Paused: ['Paused', 'warn'], Completed: ['Completed', ''], Cancelled: ['Cancelled', 'bad'] };

  function called() { return S.called; }
  function isCut(t, n) { return S.cutMode === 'Auto' ? S.called.indexOf(n) > -1 : t.marks.indexOf(n) > -1; }

  function renderTickets() {
    var h = '';
    S.tickets.forEach(function (t) {
      h += '<div class="ticket"><div class="thead"><b>Ticket #' + t.serial + '</b><span>T-' + t.id + '</span></div><div class="tgrid">';
      t.grid.forEach(function (v) {
        if (!v) { h += '<div class="blank"></div>'; return; }
        var cut = isCut(t, v), isCalled = S.called.indexOf(v) > -1;
        h += '<div class="num ' + (cut ? 'cut' : isCalled ? 'called' : '') + (S.cutMode === 'Manual' ? ' manual' : '') + '" data-t="' + t.id + '" data-n="' + v + '">' + v + '</div>';
      });
      h += '</div></div>';
    });
    if (!S.tickets.length) h = '<p class="sub">You have no tickets yet. Press “Get a ticket”.</p>';
    $('#tix').html(h);
    var canBuy = S.tickets.length < S.maxTickets && (S.status === 'Lobby' || (S.status === 'Running' && S.called.length <= S.lateJoinMaxCalls));
    $('#buy').toggle(canBuy).text('➕ Get a ticket (' + S.tickets.length + '/' + S.maxTickets + ')');
  }

  function renderPrizes() {
    var h = '', running = S.status === 'Running';
    S.prizes.forEach(function (p) {
      var m = S.mine[p.pattern], why = '', locked = false;
      if (m === 'won') { locked = true; why = 'You won this ✔'; }
      else if (m === 'barred') { locked = true; why = 'Invalid claim — locked'; }
      else if (p.closed) { locked = true; why = 'Prize closed'; }
      else if (!running) { locked = true; why = S.status === 'Lobby' ? 'Game not started' : 'Not running'; }
      else if (!S.tickets.length) { locked = true; why = 'No ticket'; }
      var pct = Math.min(100, 100 * p.winners.length / p.winnerCount);
      h += '<div class="prize"><div class="info"><b>' + T.esc(p.name) + '</b><small>' + (p.reward ? T.esc(p.reward) + ' · ' : '') + p.winners.length + '/' + p.winnerCount + ' winners' +
        (p.winners.length ? ' — ' + T.esc(p.winners.join(', ')) : '') + '</small><div class="bar"><i style="width:' + pct + '%"></i></div></div>' +
        '<button class="btn sm ' + (locked ? 'locked' : 'primary') + '" data-p="' + p.pattern + '" ' + (locked ? 'disabled title="' + why + '"' : '') + '>' + (locked ? '🔒 ' + why : 'Claim') + '</button></div>';
    });
    $('#prizes').html(h);
  }

  function renderHeader() {
    var st = STATUS[S.status] || [S.status, '']; $('#statusBadge').text(st[0]).attr('class', 'badge ' + st[1]);
    $('#hostName').text(S.host); $('#callLbl').text(S.callMode === 'Auto' ? 'Auto (every ' + S.interval + 's)' : 'Manual');
    $('#cutLbl').text(S.cutMode === 'Auto' ? 'Auto (cut for you)' : 'Manual (tap to cut)');
    $('#cnt').text(S.called.length);
    var last = S.called[S.called.length - 1];
    T.board(document.getElementById('board'), S.called, last);
    $('#recent').html(S.called.slice(-6, -1).reverse().map(function (n) { return '<span>' + n + '</span>'; }).join(''));
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
    // claim with the first ticket that satisfies the pattern locally; the server re-validates everything
    var ticket = bestTicket(pattern);
    conn.invoke('Claim', code, ticket.id, pattern).then(function (r) {
      if (r.status === 'won') {
        S.mine[pattern] = 'won'; T.confetti(); T.sfx('win');
        $('#winTxt').html('You won <b>' + T.esc(r.prize) + '</b> — winner <b>' + r.slot + ' of ' + r.of + '</b>!' + (r.reward ? '<br>🎁 ' + T.esc(r.reward) : ''));
        T.modal('win', true);
      } else if (r.status === 'late') {
        $('#lateTxt').html('Your claim for <b>' + T.esc(r.prize) + '</b> was correct, but other players got there first.<br><b>Be quicker next time — claim the moment you complete a line!</b>');
        T.sfx('late'); T.modal('late', true);
      } else if (r.status === 'invalid') {
        if (r.locked) S.mine[pattern] = 'barred';          // otherwise the player may try again (limit set by the host)
        $('#badTxt').text(r.message); T.sfx('bad'); T.modal('bad', true);
      } else { T.toast(r.message); if (/already|locked/i.test(r.message)) S.mine[pattern] = 'barred'; }
    }).catch(function (e) { T.toast(T.err(e)); })
      .then(function () { claiming[pattern] = false; return conn.invoke('GetState', code).then(function (s) { S = s; renderAll(); }); });
  });

  // UX helper only: choose the ticket that appears to satisfy the pattern (server re-validates everything)
  function rowOf(t, r) { return t.grid.slice(r * 9, r * 9 + 9).filter(Boolean); }
  function looksComplete(t, pattern) {
    var ok = function (a) { return a.every(function (v) { return S.called.indexOf(v) > -1; }); };
    switch (pattern) {
      case 'FourCorners': var a = rowOf(t, 0), b = rowOf(t, 2); return ok([a[0], a[a.length - 1], b[0], b[b.length - 1]]);
      case 'FirstRow': return ok(rowOf(t, 0));
      case 'SecondRow': return ok(rowOf(t, 1));
      case 'ThirdRow': return ok(rowOf(t, 2));
      case 'FullHouse': return ok(t.grid.filter(Boolean));
      case 'EarlyFive': return t.grid.filter(function (v) { return v && S.called.indexOf(v) > -1; }).length >= 5;
    }
    return false;
  }
  function bestTicket(pattern) {
    var hit = S.tickets.filter(function (t) { return looksComplete(t, pattern); })[0];
    return hit || S.tickets[0];
  }

  T.connect(code, {
    NumberCalled: function (m) {
      S.called.push(m.number); $('#ball').text(m.number).removeClass('pop'); void $('#ball')[0].offsetWidth; $('#ball').addClass('pop');
      $('#said').text('Number ' + m.number + ' called'); T.speak(m.number); T.sfx('call');
      T.feed('Number <b>' + m.number + '</b> called'); renderHeader(); renderTickets();
    },
    PrizesChanged: function (p) { S.prizes = p; renderPrizes(); },
    PlayersChanged: function (p) { S.players = p; renderPlayers(); },
    // a single join is sent as a delta (a full list per join does not scale to 1500 players)
    PlayerJoined: function (p) { if (!S.players.some(function (x) { return x.id === p.id; })) { S.players.push(p); renderPlayers(); } },
    Feed: function (t) { T.feed(T.esc(t)); },
    StatusChanged: function () { conn.invoke('GetState', code).then(function (s) { S = s; renderAll(); }); },
    PlayerRemoved: function (m) { if (m.id === S.myId) { alert('You were removed from this room by the host.'); location.href = '/'; } }
  }, function (s) { S = s; renderAll(); }).then(function (c) { conn = c; });
});
