// SweetAlert2 dialogs for the whole site, in the Ghars palette and the page's language/direction.
//
//  - Confirmations: put data-confirm="Question?" on a <form> or on a submit <button>. The submit is held,
//    a SweetAlert asks, and on Yes the same form is submitted with the same button (so formaction and
//    name/value still apply). Optional data-confirm-ok="Delete" sets the confirm button text.
//  - Messages: the TempData alerts from _Toasts.cshtml (data-toast) are shown as SweetAlert toasts.
//  - window.alert is routed through SweetAlert as well.
// Without JavaScript or if the library fails to load, forms submit and alerts render as before.
(function () {
    if (typeof Swal === 'undefined') return;

    var rtl = document.documentElement.dir === 'rtl';
    var t = function (en, ar) { return rtl ? ar : en; };
    var GREEN = '#2D9B6C', GREY = '#6c757d', DANGER = '#c0392b';
    var dangerWords = /delete|remove|reject|withdraw|حذف|إزالة|رفض|سحب/i;

    var base = Swal.mixin({
        confirmButtonColor: GREEN,
        cancelButtonColor: GREY,
        reverseButtons: rtl,
        customClass: { popup: 'ghars-swal' },
        didOpen: function (el) { el.setAttribute('dir', rtl ? 'rtl' : 'ltr'); }
    });

    document.addEventListener('submit', function (e) {
        var form = e.target;
        if (!(form instanceof HTMLFormElement)) return;
        var btn = e.submitter && e.submitter.hasAttribute('data-confirm') ? e.submitter : null;
        var msg = btn ? btn.getAttribute('data-confirm') : form.getAttribute('data-confirm');
        if (!msg) return;
        if (form.dataset.gharsConfirmed === '1') { delete form.dataset.gharsConfirmed; return; }

        e.preventDefault();
        e.stopImmediatePropagation();
        var okText = (btn || form).getAttribute('data-confirm-ok');
        var danger = dangerWords.test(msg);
        base.fire({
            text: msg,
            icon: danger ? 'warning' : 'question',
            showCancelButton: true,
            confirmButtonText: okText || t('Yes, continue', 'نعم، متابعة'),
            cancelButtonText: t('Cancel', 'إلغاء'),
            confirmButtonColor: danger ? DANGER : GREEN,
            focusCancel: danger
        }).then(function (r) {
            if (!r.isConfirmed) return;
            form.dataset.gharsConfirmed = '1';
            if (form.requestSubmit) form.requestSubmit(e.submitter || undefined);
            else form.submit();
        });
    }, true);

    window.alert = function (message) {
        base.fire({ text: String(message), icon: 'info', confirmButtonText: t('OK', 'حسناً') });
    };

    var toast = Swal.mixin({
        toast: true,
        position: rtl ? 'top-start' : 'top-end',
        showConfirmButton: false,
        showCloseButton: true,
        timer: 6000,
        timerProgressBar: true,
        didOpen: function (el) {
            el.setAttribute('dir', rtl ? 'rtl' : 'ltr');
            el.addEventListener('mouseenter', Swal.stopTimer);
            el.addEventListener('mouseleave', Swal.resumeTimer);
        }
    });

    function showToasts() {
        var items = Array.prototype.slice.call(document.querySelectorAll('[data-toast]'));
        var chain = Promise.resolve();
        items.forEach(function (el) {
            var icon = el.getAttribute('data-toast');
            var text = el.textContent.trim();
            el.remove();
            // Warnings can be long (e.g. a list of addresses not sent), so they stay until closed.
            chain = chain.then(function () {
                return toast.fire({ icon: icon, text: text, timer: icon === 'warning' ? undefined : 6000 });
            });
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', showToasts);
    else showToasts();
})();
