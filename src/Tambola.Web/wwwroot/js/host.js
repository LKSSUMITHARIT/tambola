/* Host console. The server verifies the caller is this room's host on every action. */
$(function () {
  var code = $('#app').data('code'), S = null, conn = null;
  var STATUS = { Lobby: ['Lobby', 'ok'], Running: ['Running', 'ok'], Paused: ['Paused', 'warn'], Completed: ['Completed', ''], Cancelled: ['Cancelled', 'bad'] };

  function run(method) {
    var args = [method, code].concat([].slice.call(arguments, 1));
    return conn.invoke.apply(conn, args).catch(function (e) { T.toast(T.err(e)); });
  }
  function refresh() { return conn.invoke('GetState', code).then(function (s) { S = s; render(); }); }

  function render() {
    var st = STATUS[S.status] || [S.status, '']; $('#statusBadge').text(st[0]).attr('class', 'badge ' + st[1]);
    var running = S.status === 'Running', paused = S.status === 'Paused', live = running || paused, auto = S.callMode === 'Auto';
    $('#callLbl').text(auto ? 'Auto (every ' + S.interval + 's)' : 'Manual'); $('#cutLbl').text(S.cutMode === 'Auto' ? 'Auto' : 'Manual');
    $('#cm button').each(function () { $(this).toggleClass('on', $(this).data('v') === S.callMode); });
    $('#interval').val(S.interval);
    $('#start').prop('disabled', S.status !== 'Lobby');
    $('#next').prop('disabled', !running || auto || S.called.length >= 90);
    $('#pause').prop('disabled', !live).text(paused ? '▶ Resume' : '⏸ Pause');
    $('#end').prop('disabled', !(S.status === 'Lobby' || live)).text(S.status === 'Lobby' ? '✖ Cancel room' : '■ End game');
    $('#lock').text(S.locked ? '🔓 Unlock room' : '🔒 Lock room');
    $('#cnt').text(S.called.length);
    var last = S.called[S.called.length - 1];
    T.board(document.getElementById('board'), S.called, last);
    $('#recent').html(S.called.slice(-8, -1).reverse().map(function (n) { return '<span>' + n + '</span>'; }).join(''));
    if (last) $('#ball').text(last);
    var msg = S.status === 'Lobby' ? 'Waiting for players to join and take tickets. Start when ready.' :
      paused ? 'Game paused.' : S.called.length >= 90 ? 'All 90 numbers called. End the game when ready.' : '';
    $('#banner').text(msg).toggle(!!msg);
    renderPrizes(); renderPlayers();
    if (S.status === 'Completed' || S.status === 'Cancelled') setTimeout(function () { location.href = '/Rooms/Results/' + code; }, 2000);
  }

  function renderPrizes() {
    $('#prizes').html(S.prizes.map(function (p) {
      var pct = Math.min(100, 100 * p.winners.length / p.winnerCount);
      return '<div class="pr' + (p.closed ? ' off' : '') + '">' + T.patternPreview(p.pattern) + '<div class="nm">' + T.esc(p.name) + ' ' + (p.closed ? '<span class="badge ok">Closed</span>' : '<span class="badge warn">Open</span>') +
        '<small>' + (p.reward ? T.esc(p.reward) + ' · ' : '') + (T.esc(p.winners.join(', ')) || 'No winners yet') + '</small><div class="bar"><i style="width:' + pct + '%"></i></div></div><b>' + p.winners.length + '/' + p.winnerCount + '</b></div>';
    }).join(''));
  }

  function renderPlayers() {
    $('#pcount,#pcount2').text(S.players.length);
    $('#players').html(S.players.map(function (p) {
      return '<li><span class="dot"></span>' + T.esc(p.name) + '<button class="btn sm danger" style="margin-left:auto" data-rm="' + p.id + '" type="button">Remove</button></li>';
    }).join('') || '<li class="hint">Nobody has joined yet.</li>');
  }

  $('#start').on('click', function () { run('Start').then(refresh); });
  $('#next').on('click', function () { var b = $(this).prop('disabled', true); run('CallNext').then(function () { setTimeout(function () { b.prop('disabled', false); render(); }, 400); }); });
  $('#pause').on('click', function () { run('SetPaused', S.status === 'Running').then(refresh); });
  $('#end').on('click', function () {
    var msg = S.status === 'Lobby' ? 'Cancel this room?' : 'End the game now? Remaining prizes will not be awarded.';
    if (confirm(msg)) run('End', S.status === 'Lobby');
  });
  $('#cm').on('click', 'button', function () { run('SetCalling', $(this).data('v'), +$('#interval').val()).then(refresh); });
  $('#interval').on('change', function () { run('SetCalling', S.callMode, +$(this).val()).then(refresh); });
  $('#lock').on('click', function () { run('SetLocked', !S.locked).then(refresh); });
  $('#players').on('click', '[data-rm]', function () { if (confirm('Remove this player from the room?')) run('RemovePlayer', +$(this).data('rm')); });

  T.initTabs('prizes');

  T.connect(code, {
    NumberCalled: function (m) {
      S.called.push(m.number); $('#ball').text(m.number).removeClass('pop'); void $('#ball')[0].offsetWidth; $('#ball').addClass('pop');
      T.speak(m.number); T.sfx('call');
      T.feed('Number <b>' + m.number + '</b> called'); render();
    },
    PrizesChanged: function (p) { S.prizes = p; renderPrizes(); },
    PlayersChanged: function (p) { S.players = p; renderPlayers(); $('#pcount2').text(p.length); },
    // joins arrive one by one; re-render the (possibly 1500-long) list at most every 300 ms
    PlayerJoined: function (p) {
      if (S.players.some(function (x) { return x.id === p.id; })) return;
      S.players.push(p); $('#pcount,#pcount2').text(S.players.length);
      if (!S._pt) S._pt = setTimeout(function () { S._pt = null; renderPlayers(); }, 300);
    },
    Feed: function (t) { T.feed(T.esc(t)); },
    StatusChanged: function () { refresh(); },
    PlayerRemoved: function () { }
  }, function (s) { S = s; render(); }).then(function (c) { conn = c; });
});
