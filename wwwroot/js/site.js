// MC Panel — shared behaviour: mobile menu, live server status, toasts.
(function () {
  'use strict';

  // ---------- mobile menu ----------
  var burger = document.getElementById('burger');
  var menu = document.getElementById('menu');
  if (burger && menu) {
    var isOpen = function () { return menu.classList.contains('open'); };
    var set = function (open) {
      menu.classList.toggle('open', open);
      burger.setAttribute('aria-expanded', open ? 'true' : 'false');
      burger.setAttribute('aria-label', open ? 'Menüyü kapat' : 'Menüyü aç');
    };
    burger.addEventListener('click', function (e) { e.stopPropagation(); set(!isOpen()); });
    menu.addEventListener('click', function (e) { if (e.target.closest && e.target.closest('a')) set(false); });
    document.addEventListener('click', function (e) {
      if (!isOpen() || menu.contains(e.target) || burger.contains(e.target)) return;
      set(false);
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && isOpen()) { set(false); burger.focus(); }
    });
  }

  // ---------- server status ----------
  // [data-status-pill]            gets data-state="on|off"
  // [data-status-on/-off]         gets the matching text
  // [data-status-pid]             gets the PID (or "-")
  function applyStatus(data) {
    var on = !!(data && data.isRunning);
    document.querySelectorAll('[data-status-pill]').forEach(function (el) {
      el.setAttribute('data-state', on ? 'on' : 'off');
    });
    document.querySelectorAll('[data-status-on]').forEach(function (el) {
      el.textContent = on ? el.getAttribute('data-status-on') : el.getAttribute('data-status-off');
    });
    document.querySelectorAll('[data-status-pid]').forEach(function (el) {
      el.textContent = on && data.pid ? data.pid : '-';
    });
    document.dispatchEvent(new CustomEvent('mc:status', { detail: { isRunning: on, pid: data && data.pid } }));
  }

  var polling = document.querySelector('[data-status-pill],[data-status-on]');
  function refreshStatus() {
    return fetch('/Server/Status', { headers: { 'Accept': 'application/json' } })
      .then(function (r) { return r.ok ? r.json() : null; })
      .then(function (d) { if (d) applyStatus(d); return d; })
      .catch(function () { return null; });
  }
  if (polling) {
    refreshStatus();
    setInterval(function () { if (!document.hidden) refreshStatus(); }, 10000);
  }

  // ---------- toast ----------
  function toast(message, kind) {
    var stack = document.getElementById('toastStack');
    if (!stack || !message) return;
    var el = document.createElement('div');
    el.className = 'mc-toast ' + (kind === 'err' ? 'err' : 'ok');
    var icon = document.createElement('i');
    icon.className = 'bi ' + (kind === 'err' ? 'bi-exclamation-circle-fill' : 'bi-check-circle-fill');
    var text = document.createElement('span');
    text.textContent = message;
    el.appendChild(icon);
    el.appendChild(text);
    stack.appendChild(el);
    setTimeout(function () {
      el.classList.add('out');
      setTimeout(function () { el.remove(); }, 220);
    }, 3200);
  }

  window.McPanel = { applyStatus: applyStatus, refreshStatus: refreshStatus, toast: toast };
})();
