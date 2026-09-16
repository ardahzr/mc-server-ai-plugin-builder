// MC Panel — landing: background video loop, nav glass on scroll, scroll reveals.
(function () {
  'use strict';
  var reduce = window.matchMedia && matchMedia('(prefers-reduced-motion: reduce)').matches;

  // ---------- nav glass once the hero starts to scroll ----------
  var nav = document.querySelector('.lnav');
  if (nav) {
    var onScroll = function () { nav.classList.toggle('scrolled', window.scrollY > 24); };
    onScroll();
    window.addEventListener('scroll', onScroll, { passive: true });
  }

  // ---------- scroll reveal ----------
  var revealables = document.querySelectorAll('[data-reveal]');
  if (!reduce && 'IntersectionObserver' in window && revealables.length) {
    document.documentElement.classList.add('js-reveal');
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (e) {
        if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
      });
    }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });
    revealables.forEach(function (el) { io.observe(el); });
  }

  // ---------- background video: two copies cross-fade at the loop point ----------
  var A = document.getElementById('bgVideoA');
  var B = document.getElementById('bgVideoB');
  if (!A || !B) return;

  if (reduce) {
    A.removeAttribute('autoplay');
    A.pause(); B.pause();
    try { A.currentTime = 0; } catch (e) {}
    return;
  }

  var FADE = 0.9;
  var cur = A, nxt = B, swapping = false;

  function play(v) {
    var p = v.play();
    if (p && typeof p.catch === 'function') p.catch(function () {});
  }
  play(A);

  function tick() {
    if (swapping || !cur.duration) return;
    if (cur.duration - cur.currentTime > FADE) return;
    swapping = true;
    var out = cur;
    nxt.currentTime = 0;
    play(nxt);
    nxt.classList.add('is-active');
    out.classList.remove('is-active');
    cur = nxt; nxt = out;
    setTimeout(function () {
      out.pause();
      out.currentTime = 0;
      swapping = false;
    }, FADE * 1000 + 100);
  }

  A.addEventListener('timeupdate', tick);
  B.addEventListener('timeupdate', tick);

  // don't decode video nobody can see
  document.addEventListener('visibilitychange', function () {
    if (document.hidden) cur.pause(); else play(cur);
  });
})();
