(function () {
    "use strict";

    const KEY = "mc-sidebar-scroll-position-v1";

    function candidates() {
        return Array.from(document.querySelectorAll(
            ".mc-ticari-sidebar, #sidebar, .sidebar, aside, nav"
        ));
    }

    function getSidebar() {
        const list = candidates();

        for (const el of list) {
            if (!el) continue;

            const style = window.getComputedStyle(el);

            if (
                el.scrollHeight > el.clientHeight + 5 &&
                (
                    style.overflowY === "auto" ||
                    style.overflowY === "scroll" ||
                    style.overflowY === "overlay"
                )
            ) {
                return el;
            }
        }

        return list.find(function (el) {
            return el &&
                el.querySelector &&
                el.querySelector("a.menu-item");
        }) || null;
    }

    function savePosition() {
        const sidebar = getSidebar();

        if (!sidebar) return;

        try {
            sessionStorage.setItem(
                KEY,
                String(sidebar.scrollTop || 0)
            );
        }
        catch {}
    }

    function restorePosition() {
        const sidebar = getSidebar();

        if (!sidebar) return;

        try {
            const raw = sessionStorage.getItem(KEY);

            if (raw === null) return;

            const y = Number(raw);

            if (!Number.isFinite(y)) return;

            sidebar.scrollTop = y;

            requestAnimationFrame(function () {
                sidebar.scrollTop = y;
            });

            setTimeout(function () {
                sidebar.scrollTop = y;
            }, 50);

            setTimeout(function () {
                sidebar.scrollTop = y;
            }, 200);
        }
        catch {}
    }

    document.addEventListener(
        "click",
        function (event) {
            const link = event.target.closest(
                "a.menu-item, aside a, .sidebar a, #sidebar a, .mc-ticari-sidebar a"
            );

            if (!link) return;

            const href = link.getAttribute("href");

            if (
                !href ||
                href === "#" ||
                href.startsWith("javascript:")
            ) {
                return;
            }

            savePosition();
        },
        true
    );

    window.addEventListener("beforeunload", savePosition);
    window.addEventListener("pagehide", savePosition);

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            restorePosition
        );
    }
    else {
        restorePosition();
    }

    window.addEventListener("pageshow", restorePosition);

    setTimeout(restorePosition, 100);
    setTimeout(restorePosition, 400);
})();