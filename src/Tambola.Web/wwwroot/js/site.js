/* Shared helpers: theme, toast, confetti, speech, board. Requires jQuery. */
var T = (function () {
  var shapeCells = {
    Star: [[0,4],[1,1],[1,3],[1,4],[1,5],[1,7],[2,0],[2,2],[2,4],[2,6],[2,8]],
    Pyramid: [[0,4],[1,2],[1,3],[1,4],[1,5],[1,6],[2,0],[2,1],[2,2],[2,3],[2,4],[2,5],[2,6],[2,7],[2,8]],
    Diamond: [[0,4],[1,3],[1,4],[1,5],[2,4]],
    LetterH: [[0,0],[0,8],[1,0],[1,1],[1,2],[1,3],[1,4],[1,5],[1,6],[1,7],[1,8],[2,0],[2,8]],
    LetterX: [[0,0],[0,8],[1,3],[1,4],[1,5],[2,0],[2,8]]
  };
  function esc(s) { return $('<div>').text(s == null ? '' : s).html(); }
  return {
    esc: esc,
    patternCells: function (pattern) { return shapeCells[pattern] || []; },
    patternPreview: function (pattern) {
      var cells = shapeCells[pattern];
      if (!cells) return '';
      var html = '<span class="pattern-preview" role="img" aria-label="' + pattern + ' shape preview">';
      for (var r = 0; r < 3; r++) for (var c = 0; c < 9; c++)
        html += '<i' + (cells.some(function (cell) { return cell[0] === r && cell[1] === c; }) ? ' class="on"' : '') + '></i>';
      return html + '</span>';
    },
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
    /** Sound options are set by the Admin (data-* attributes on <html>, kept live by the SettingsChanged event). A user may only mute locally, and only if the Admin allows it. */
    soundOn: function () {
      var d = document.documentElement.dataset;
      if (d.allowMute !== 'no') { try { var v = localStorage.getItem('sound'); if (v) return v === 'on'; } catch (e) { } }
      return d.defaultSound !== 'off';
    },
    volume: function () { var v = parseInt(document.documentElement.dataset.volume, 10); return isNaN(v) ? 0.8 : Math.max(0, Math.min(1, v / 100)); },
    applySettings: function (s) {
      var d = document.documentElement.dataset;
      d.defaultSound = s.enabled ? 'on' : 'off'; d.caller = s.caller; d.fallback = s.fallback; d.volume = s.volume; d.allowMute = s.allowMute ? 'yes' : 'no';
      $('#soundBtn').toggle(!!s.allowMute); T.setSound(T.soundOn());
      if (s.caller === 'hindi' && $('#ball').length) T.preloadVoice();
    },
    setSound: function (on) {
      try { localStorage.setItem('sound', on ? 'on' : 'off'); } catch (e) { }
      $('#soundBtn').text(on ? '🔊' : '🔇'); if (!on) T.stopVoice();
    },
    callerStyle: function () { return document.documentElement.dataset.caller === 'classic' ? 'classic' : 'hindi'; },
    fallbackHindi: function () { return document.documentElement.dataset.fallback !== 'classic'; },
    sfx: function (kind) {   // short WebAudio effects (no audio files needed)
      if (!T.soundOn()) return;
      try {
        var A = window.AudioContext || window.webkitAudioContext; if (!A) return;
        var ctx = T._ac = T._ac || new A(); if (ctx.state === 'suspended') ctx.resume();
        var notes = { call: [[880, 0, .08]], win: [[523, 0, .15], [659, .15, .15], [784, .3, .15], [1047, .45, .4]],
                      late: [[440, 0, .15], [330, .15, .3]], bad: [[200, 0, .3]] }[kind] || [];
        notes.forEach(function (n) {
          var o = ctx.createOscillator(), g = ctx.createGain(); o.type = kind === 'bad' ? 'sawtooth' : 'sine'; o.frequency.value = n[0];
          g.gain.setValueAtTime(.15 * T.volume() + .0001, ctx.currentTime + n[1]); g.gain.exponentialRampToValueAtTime(.001, ctx.currentTime + n[1] + n[2]);
          o.connect(g); g.connect(ctx.destination); o.start(ctx.currentTime + n[1]); o.stop(ctx.currentTime + n[1] + n[2] + .02);
        });
      } catch (e) { }
    },
    voice: { buf: {}, text: {}, loading: null },
    /** Downloads and decodes every funny-Hindi clip into memory so a call plays instantly (the service worker keeps them cached across visits). */
    preloadVoice: function () {
      var V = T.voice; if (V.loading) return V.loading;
      var A = window.AudioContext || window.webkitAudioContext; if (!A || !window.fetch) return;
      var ctx = T._ac = T._ac || new A();
      $('#soundBtn').attr('title', 'Sound on/off (loading caller voice...)');
      V.loading = fetch('/audio/hi/calls.json').then(function (r) { return r.json(); }).then(function (calls) {
        V.text = calls; var ids = Object.keys(calls), i = 0;
        function next() {
          if (i >= ids.length) return Promise.resolve();
          var id = ids[i++];
          return fetch('/audio/hi/' + id + '.mp3').then(function (r) { return r.ok ? r.arrayBuffer() : Promise.reject(); })
            .then(function (b) { return new Promise(function (res, rej) { ctx.decodeAudioData(b, res, rej); }); })
            .then(function (d) { V.buf[id] = d; }).catch(function () { }).then(next);
        }
        return Promise.all([next(), next(), next(), next()]);
      }).then(function () { $('#soundBtn').attr('title', 'Sound on/off (caller voice ready)'); })
        .catch(function () { V.loading = null; $('#soundBtn').attr('title', 'Sound on/off'); });
      return V.loading;
    },
    stopVoice: function () {
      try { if (T._src) { T._src.stop(); T._src = null; } } catch (e) { }
      if (window.speechSynthesis) speechSynthesis.cancel();
    },
    speak: function (n) {
      if (!T.soundOn()) return;
      var hindi = T.callerStyle() === 'hindi';
      if (hindi) {
        T.stopVoice();
        var d = T.voice.buf[n], ctx = T._ac;
        if (d && ctx) {   // clip already in memory: instant playback
          try {
            if (ctx.state === 'suspended') ctx.resume();
            var src = T._src = ctx.createBufferSource(), gain = ctx.createGain(); gain.gain.value = T.volume(); src.buffer = d; src.connect(gain); gain.connect(ctx.destination); src.start(); return;
          } catch (e) { }
        }
        T.preloadVoice();
        var el = new Audio('/audio/hi/' + n + '.mp3');   // not decoded yet (or no WebAudio): stream the clip, then fall back to TTS
        el.volume = T.volume(); el.play().catch(function () { T.speakTts(n, T.fallbackHindi()); });
        return;
      }
      T.speakTts(n, false);
    },
    /** Browser text-to-speech: the last-resort voice. */
    speakTts: function (n, hindi) {
      if (!window.speechSynthesis) return;
      var u;
      if (hindi) {
        var voices = speechSynthesis.getVoices(), hindiVoice = voices.filter(function (v) { return /^hi(?:-|$)/i.test(v.lang); })[0];
        if (hindiVoice) {
          u = new SpeechSynthesisUtterance(T.voice.text[n] || 'अरे वाह, नंबर ' + n + ' आया!');
          u.lang = hindiVoice.lang; u.voice = hindiVoice;
        }
      }
      if (!u) u = new SpeechSynthesisUtterance(n < 10 ? 'Number ' + n : String(n).split('').join(' ') + ', ' + n);
      u.volume = T.volume(); speechSynthesis.cancel(); speechSynthesis.speak(u);
    },
    modal: function (id, show) { document.getElementById(id).classList.toggle('show', show); },
    /** Connects to the game hub with automatic reconnect; onReady runs after (re)join with the full room state. */
    connect: function (code, handlers, onState) {
      var conn = new signalR.HubConnectionBuilder().withUrl('/hubs/game').withAutomaticReconnect([0, 1000, 2000, 5000, 10000]).build();
      Object.keys(handlers).forEach(function (k) { conn.on(k, handlers[k]); });
      conn.on('SettingsChanged', T.applySettings);
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
    feed: function (html) { $('#feed').prepend('<div>' + html + '</div>'); if (T.currentTab && T.currentTab() !== 'feed') $('.newfeed').show(); },
    /** Tabbed side panel (tabs on wide screens; bottom dock + slide-up sheet on phones). 'home' = the main area (closes the sheet). */
    initTabs: function (first) {
      var cur = first;
      function open(t) {
        var mobile = innerWidth <= 900, close = t === 'home' || (mobile && t === cur && $('#panel').hasClass('open'));
        if (!close) cur = t;
        $('#panel').toggleClass('open', !close && mobile);
        $('.tabpane').each(function () { $(this).toggleClass('on', $(this).data('pane') === cur); });
        $('#tabs button').each(function () { $(this).toggleClass('on', $(this).data('tab') === cur); });
        $('#dock button').each(function () { $(this).toggleClass('on', $(this).data('tab') === (close || !mobile ? 'home' : cur)); });
        if (cur === 'feed' && !close) $('.newfeed').hide();
      }
      T.currentTab = function () { return cur; };
      $(document).on('click', '#tabs button,#dock button', function () { open($(this).data('tab')); });
      open('home');
    }
  };
})();

$(function () {
  $('#themeBtn').on('click', function () {
    var d = document.documentElement, dark = d.dataset.theme === 'dark' || (!d.dataset.theme && matchMedia('(prefers-color-scheme:dark)').matches);
    d.dataset.theme = dark ? 'light' : 'dark';
    try { localStorage.setItem('theme', d.dataset.theme); } catch (e) { }
  });
  T.setSound(T.soundOn());
  if (T.callerStyle() === 'hindi' && $('#ball').length) T.preloadVoice();   // room pages only
  if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(function () { });
  $('[data-pattern-preview]').each(function () {
    $(this).html(T.patternPreview($(this).attr('data-pattern-preview')));
  });
  $('#soundBtn').on('click', function () { T.setSound(!T.soundOn()); if (T.soundOn()) T.sfx('call'); });
  $(document).on('click', '[data-close]', function () { T.modal($(this).data('close'), false); });
});
