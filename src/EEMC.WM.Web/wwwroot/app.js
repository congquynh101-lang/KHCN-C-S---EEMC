// Tiện ích phía trình duyệt cho EEMC Work Manager
window.eemc = {
    download: async function (fileName, streamRef) {
        const buffer = await streamRef.arrayBuffer();
        const blob = new Blob([buffer]);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName ?? 'tai-ve';
        document.body.appendChild(a);
        a.click();
        a.remove();
        URL.revokeObjectURL(url);
    },
    notifyPermission: async function () {
        if (!('Notification' in window)) return 'unsupported';
        if (Notification.permission === 'default') {
            try { return await Notification.requestPermission(); } catch { return 'denied'; }
        }
        return Notification.permission;
    },
    notifyState: function () { return ('Notification' in window) ? Notification.permission : 'unsupported'; },
    notify: function (title, body) {
        if (!('Notification' in window) || Notification.permission !== 'granted') return;
        try { new Notification(title, { body: body, tag: title + body }); } catch { /* trình duyệt chặn */ }
    },
    setTheme: function (dark) {
        document.documentElement.dataset.theme = dark ? 'dark' : 'light';
        try { localStorage.setItem('eemc-theme', dark ? 'dark' : 'light'); } catch { }
    },
    getTheme: function () {
        try { return localStorage.getItem('eemc-theme'); } catch { return null; }
    },
    focus: function (id) {
        const el = document.getElementById(id);
        if (el) el.focus();
    },
    scrollTo: function (id) {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ block: 'start' });
    },
    copyText: async function (text) {
        try { await navigator.clipboard.writeText(text); return true; } catch { return false; }
    },
    print: function () { window.print(); },
    // Phím tắt toàn cục: N = thêm công việc, / = tìm kiếm
    registerShortcuts: function (dotnet) {
        if (window.__eemcShortcuts) return;
        window.__eemcShortcuts = true;
        document.addEventListener('keydown', function (e) {
            const tag = (e.target && e.target.tagName) || '';
            const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(tag) || (e.target && e.target.isContentEditable);
            if (typing || e.ctrlKey || e.altKey || e.metaKey) return;
            if (e.key === 'n' || e.key === 'N') { e.preventDefault(); dotnet.invokeMethodAsync('OnShortcut', 'new'); }
            if (e.key === '/') { e.preventDefault(); dotnet.invokeMethodAsync('OnShortcut', 'search'); }
        });
    }
};
// Áp dụng giao diện đã lưu sớm để không nhấp nháy
(function () {
    try {
        const t = localStorage.getItem('eemc-theme');
        if (t) document.documentElement.dataset.theme = t;
        else if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) document.documentElement.dataset.theme = 'dark';
    } catch { }
})();
