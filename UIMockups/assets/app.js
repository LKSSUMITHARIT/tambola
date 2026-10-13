/* Shared mock helpers (vanilla JS; real app will use jQuery + SignalR) */
(function () {
  var role = (document.body.dataset.role || 'player');
  var page = document.body.dataset.page || '';
  var links = {
    player: [['dashboard.html', 'Home'], ['lobby.html', 'Lobby'], ['player-game.html', 'Play'], ['results.html', 'Results'], ['history.html', 'My Games']],
    host: [['dashboard.html', 'Home'], ['host-create.html', 'Create Room'], ['host-console.html', 'Host Console'], ['results.html', 'Results'], ['history.html', 'My Games']],
    admin: [['admin.html', 'Hosts & Employees'], ['reports.html', 'Reports'], ['dashboard.html', 'Home']]
  }[role] || [];
  var nav = links.map(function (l) { return '<a href="' + l[0] + '"' + (l[0] === page ? ' class="on"' : '') + '>' + l[1] + '</a>'; }).join('');
  var who = { player: ['AK', 'CORP\\anil.kumar', 'Player'], host: ['SH', 'CORP\\sumit.harit', 'Host'], admin: ['AD', 'CORP\\admin.user', 'Admin'] }[role];
  if (who) {
    var h = document.createElement('div');
    h.innerHTML = '<div class="mockbar">UI MOCKUP — dummy data only · <a href="index.html">all screens</a></div>' +
      '<header class="top"><a class="logo" href="index.html">🎯 Tambola</a><nav>' + nav + '</nav>' +
      '<div class="who"><span class="badge brand">' + who[2] + '</span>' + who[1] + '<div class="avatar">' + who[0] + '</div>' +
      '<button class="btn sm" id="themeBtn" title="Toggle theme">🌓</button></div></header>';
    document.body.prepend(h);
    document.getElementById('themeBtn').onclick = function () {
      var d = document.documentElement, dark = d.dataset.theme === 'dark' || (!d.dataset.theme && matchMedia('(prefers-color-scheme:dark)').matches);
      d.dataset.theme = dark ? 'light' : 'dark';
    };
  }
})();

var T = {
  board: function (el, called, last) {
    var h = '';
    for (var i = 1; i <= 90; i++) h += '<div class="' + (called.indexOf(i) > -1 ? 'on' : '') + (i === last ? ' last' : '') + '">' + i + '</div>';
    el.innerHTML = h;
  },
  toast: function (msg) {
    var t = document.getElementById('toast');
    if (!t) { t = document.createElement('div'); t.id = 'toast'; t.className = 'toast'; document.body.appendChild(t); }
    t.textContent = msg; t.classList.add('show'); clearTimeout(t._h); t._h = setTimeout(function () { t.classList.remove('show'); }, 2600);
  },
  confetti: function () {
    var e = ['🎉', '🎊', '⭐', '✨', '🏆'];
    for (var i = 0; i < 40; i++) {
      var s = document.createElement('span'); s.className = 'confetti'; s.textContent = e[i % e.length];
      s.style.left = Math.random() * 100 + 'vw'; s.style.animationDuration = (2 + Math.random() * 2.5) + 's';
      document.body.appendChild(s); setTimeout(function (x) { x.remove(); }.bind(null, s), 5000);
    }
  },
  speak: function (n) {
    if (!window.speechSynthesis) return;
    var u = new SpeechSynthesisUtterance(n < 10 ? 'Number ' + n : String(n).split('').join(' ') + ', ' + n);
    speechSynthesis.cancel(); speechSynthesis.speak(u);
  },
  shuffle: function (a) { for (var i = a.length - 1; i > 0; i--) { var j = Math.floor(Math.random() * (i + 1)); var t = a[i]; a[i] = a[j]; a[j] = t; } return a; },
  modal: function (id, show) { document.getElementById(id).classList.toggle('show', show); }
};
