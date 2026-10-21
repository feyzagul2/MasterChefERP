(() => {
    "use strict";
    const sidebar = document.getElementById("sidebar");
    const toggle = document.getElementById("menuToggle");
    const close = document.getElementById("menuClose");
    const backdrop = document.getElementById("menuBackdrop");
    const shell = document.getElementById("appShell");
    if (sidebar && toggle && close && backdrop && shell) {
        const mobile = window.matchMedia("(max-width: 991px)");
        let isOpen = false;
        const items = () => [...sidebar.querySelectorAll('a[href], button:not([disabled])')];
        function setMenu(open, restoreFocus = true) {
            isOpen = open && mobile.matches;
            document.body.classList.toggle("menu-open", isOpen);
            toggle.setAttribute("aria-expanded", String(isOpen));
            toggle.setAttribute("aria-label", isOpen ? "Ana menüyü kapat" : "Ana menüyü aç");
            backdrop.hidden = !isOpen;
            sidebar.inert = mobile.matches && !isOpen;
            shell.inert = isOpen;
            if (isOpen) items()[0]?.focus();
            else if (restoreFocus && mobile.matches) toggle.focus();
        }
        toggle.addEventListener("click", () => setMenu(!isOpen));
        close.addEventListener("click", () => setMenu(false));
        backdrop.addEventListener("click", () => setMenu(false));
        sidebar.addEventListener("click", event => {
            if (mobile.matches && event.target.closest("a[href]")) setMenu(false, false);
        });
        document.addEventListener("keydown", event => {
            if (!isOpen) return;
            if (event.key === "Escape") { event.preventDefault(); setMenu(false); }
            if (event.key === "Tab") {
                const focusable = items();
                const first = focusable[0], last = focusable[focusable.length - 1];
                if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
                else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
            }
        });
        mobile.addEventListener("change", () => setMenu(false, false));
        setMenu(false, false);
    }
    const formatter = new Intl.DateTimeFormat("tr-TR", { timeZone: "Europe/Istanbul", day: "2-digit", month: "short", year: "numeric" });
    document.querySelectorAll("time[data-utc]").forEach(element => {
        const date = new Date(element.dataset.utc);
        if (!Number.isNaN(date.getTime())) element.textContent = formatter.format(date);
    });
})();
