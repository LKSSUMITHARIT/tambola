/* Shared helpers: theme, toast, confetti, speech, board. Requires jQuery. */
var T = (function () {
  function esc(s) { return $('<div>').text(s == null ? '' : s).html(); }
  return {
    esc: esc,
    board: function (el, called, last) {
      var h = '';
      for (var i = 1; i <= 90; i++) h += '<div class="' + (called.indexOf(i) > -1 ? 'on' : '') + (i === last ? ' last' : '') + '">' + i + '</div>';
      el.innerHTML = h;
    },
    toast: function (msg) {
      var t = document.getElementById('toast');
      if (!t) { t = document.createElement('div'); t.id = 'toast'; t.className = 'toast'; document.body.appendChild(t); }
      t.textContent = msg; t.classList.add('show'); clearTimeout(t._h);
      t._h = setTimeout(function () { t.classList.remove('show'); }, 3200);
    },
    confetti: function () {
      var e = ['🎉', '🎊', '⭐', '✨', '🏆'];
      for (var i = 0; i < 40; i++) {
        var s = document.createElement('span'); s.className = 'confetti'; s.textContent = e[i % e.length];
        s.style.left = Math.random() * 100 + 'vw'; s.style.animationDuration = (2 + Math.random() * 2.5) + 's';
        document.body.appendChild(s); setTimeout((function (x) { return function () { x.remove(); }; })(s), 5000);
      }
    },
    soundOn: function () {
      try { var v = localStorage.getItem('sound'); if (v) return v === 'on'; } catch (e) { }
      return document.documentElement.dataset.defaultSound !== 'off';
    },
    setSound: function (on) {
      try { localStorage.setItem('sound', on ? 'on' : 'off'); } catch (e) { }
      $('#soundBtn').text(on ? '🔊' : '🔇'); if (!on && window.speechSynthesis) speechSynthesis.cancel();
    },
    sfx: function (kind) {   // short WebAudio effects (no audio files needed)
      if (!T.soundOn()) return;
      try {
        var A = window.AudioContext || window.webkitAudioContext; if (!A) return;
        var ctx = T._ac = T._ac || new A(); if (ctx.state === 'suspended') ctx.resume();
        var notes = { call: [[880, 0, .08]], win: [[523, 0, .15], [659, .15, .15], [784, .3, .15], [1047, .45, .4]],
                      late: [[440, 0, .15], [330, .15, .3]], bad: [[200, 0, .3]] }[kind] || [];
        notes.forEach(function (n) {
          var o = ctx.createOscillator(), g = ctx.createGain(); o.type = kind === 'bad' ? 'sawtooth' : 'sine'; o.frequency.value = n[0];
          g.gain.setValueAtTime(.15, ctx.currentTime + n[1]); g.gain.exponentialRampToValueAtTime(.001, ctx.currentTime + n[1] + n[2]);
          o.connect(g); g.connect(ctx.destination); o.start(ctx.currentTime + n[1]); o.stop(ctx.currentTime + n[1] + n[2] + .02);
        });
      } catch (e) { }
    },
    speak: function (n) {
      if (!T.soundOn() || !window.speechSynthesis) return;
      var u = new SpeechSynthesisUtterance(n < 10 ? 'Number ' + n : String(n).split('').join(' ') + ', ' + n);
      speechSynthesis.cancel(); speechSynthesis.speak(u);
    },
    modal: function (id, show) { document.getElementById(id).classList.toggle('show', show); },
    /** Connects to the game hub with automatic reconnect; onReady runs after (re)join with the full room state. */
    connect: function (code, handlers, onState) {
      var conn = new signalR.HubConnectionBuilder().withUrl('/hubs/game').withAutomaticReconnect([0, 1000, 2000, 5000, 10000]).build();
      Object.keys(handlers).forEach(function (k) { conn.on(k, handlers[k]); });
      function join() {
        return conn.invoke('JoinRoom', code).then(function (s) { $('#conn').hide(); onState(s); })
          .catch(function (e) { $('#banner').text(T.err(e)).show(); });
      }
      conn.onreconnecting(function () { $('#conn').show(); });
      conn.onreconnected(join);
      conn.onclose(function () { $('#conn').text('Disconnected. Refresh the page.').show(); });
      return conn.start().then(join).then(function () { return conn; }).catch(function (e) { $('#banner').text('Cannot connect: ' + T.err(e)).show(); });
    },
    err: function (e) { return (e && e.message ? e.message : String(e)).replace(/^.*HubException: /, ''); },
    feed: function (html) { $('#feed').prepend('<div>' + html + '</div>'); }
  };
})();

$(function () {
  $('#themeBtn').on('click', function () {
    var d = document.documentElement, dark = d.dataset.theme === 'dark' || (!d.dataset.theme && matchMedia('(prefers-color-scheme:dark)').matches);
    d.dataset.theme = dark ? 'light' : 'dark';
    try { localStorage.setItem('theme', d.dataset.theme); } catch (e) { }
  });
  T.setSound(T.soundOn());
  $('#soundBtn').on('click', function () { T.setSound(!T.soundOn()); if (T.soundOn()) T.sfx('call'); });
  $(document).on('click', '[data-close]', function () { T.modal($(this).data('close'), false); });
});
