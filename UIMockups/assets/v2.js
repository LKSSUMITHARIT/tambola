/* Shared helpers for the v2 mocks (relies on T from app.js) */
var V2 = {
  collapsibles: function () {
    document.querySelectorAll('.cx').forEach(function (c) {
      var key = 'cx:' + location.pathname.split('/').pop() + ':' + c.dataset.id;
      try { if (localStorage.getItem(key) === '1') c.classList.add('closed'); } catch (e) { }
      c.querySelector('.cx-head').onclick = function () {
        c.classList.toggle('closed'); try { localStorage.setItem(key, c.classList.contains('closed') ? '1' : '0'); } catch (e) { }
      };
    });
  },
  /** 3x9 ticket, 5 numbers per row, column c holds numbers from its decade. Returns 27 cells (0 = blank). */
  ticket: function () {
    for (;;) {
      var rows = [0, 1, 2].map(function () { return T.shuffle([0, 1, 2, 3, 4, 5, 6, 7, 8]).slice(0, 5); });
      var cnt = [0, 0, 0, 0, 0, 0, 0, 0, 0]; rows.forEach(function (r) { r.forEach(function (c) { cnt[c]++; }); });
      if (cnt.some(function (x) { return x === 0; })) continue;
      var g = []; for (var i = 0; i < 27; i++) g.push(0);
      for (var c = 0; c < 9; c++) {
        var lo = c === 0 ? 1 : c * 10, hi = c === 8 ? 90 : c * 10 + 9, pool = [];
        for (var n = lo; n <= hi; n++) pool.push(n);
        var picks = T.shuffle(pool).slice(0, cnt[c]).sort(function (a, b) { return a - b; }), k = 0;
        for (var r = 0; r < 3; r++) if (rows[r].indexOf(c) > -1) g[r * 9 + c] = picks[k++];
      }
      return g;
    }
  },
  pattern: function (cells) {
    var h = ''; for (var i = 0; i < 27; i++) h += '<i class="' + (cells.indexOf(i) > -1 ? 'on' : '') + '"></i>';
    return '<span class="pattern-preview">' + h + '</span>';
  },
  P: {
    EarlyFive: { name: 'Early Five', cells: [0, 1, 2, 3, 4] },
    FourCorners: { name: 'Four Corners', cells: [0, 8, 18, 26] },
    FirstRow: { name: 'First Row', cells: [0, 1, 2, 3, 4, 5, 6, 7, 8] },
    SecondRow: { name: 'Second Row', cells: [9, 10, 11, 12, 13, 14, 15, 16, 17] },
    ThirdRow: { name: 'Third Row', cells: [18, 19, 20, 21, 22, 23, 24, 25, 26] },
    FullHouse: { name: 'Full House', cells: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26] }
  },
  /** does a ticket (27 cells) satisfy the pattern, given the called numbers? */
  complete: function (g, pat, called) {
    var nums = g.filter(Boolean), hit = function (v) { return called.indexOf(v) > -1; };
    var rowNums = function (r) { return g.slice(r * 9, r * 9 + 9).filter(Boolean); };
    if (pat === 'EarlyFive') return nums.filter(hit).length >= 5;
    if (pat === 'FourCorners') { var a = rowNums(0), b = rowNums(2); return [a[0], a[a.length - 1], b[0], b[b.length - 1]].every(hit); }
    if (pat === 'FirstRow') return rowNums(0).every(hit);
    if (pat === 'SecondRow') return rowNums(1).every(hit);
    if (pat === 'ThirdRow') return rowNums(2).every(hit);
    return nums.every(hit);
  }
};
