// MC_RAW_TYPOGRAPHY_2026
// MC_RAW_BACK_BUTTON_FINAL_2026
(function () {
    "use strict";

    const names = {
        "/MaterialCatalog": "Malzeme Katalo?u",
        "/RawMaterials": "Hammadde Stok Kartlar?",
        "/MaterialStockMapping": "Stok Kart? E?le?tirme",
        "/MCRawWarehouses": "Hammadde Depolar?"
    };

    function fixTabs() {
        const nav = document.querySelector(".mc-final-nav");
        if (!nav) return;

        nav.querySelectorAll("a[href]").forEach(function (link) {
            let url;
            try {
                url = new URL(link.href, location.origin);
            } catch {
                return;
            }

            const correct = names[url.pathname];

            if (correct && link.textContent.trim() !== correct) {
                link.textContent = correct;
            }

            link.style.fontFamily = '"Segoe UI", Arial, sans-serif';
            link.style.fontWeight = "600";
            link.style.letterSpacing = "normal";
        });
    }

    function fixInsideFrame() {
        const frame = document.getElementById(
            "mc-hammadde-content-frame"
        );

        if (!frame) return;

        let doc;

        try {
            doc = frame.contentDocument;
        } catch {
            return;
        }

        if (!doc || !doc.body) return;

        function hideExtraBack() {
            doc.querySelectorAll("a, button").forEach(function (item) {
                const text = (item.textContent || "")
                    .replace(/\s+/g, " ")
                    .trim()
                    .toLocaleLowerCase("tr-TR");

                if (
                    text.includes("?nceki sayfaya d?n") ||
                    text.includes("genel bak??'a d?n") ||
                    text.includes("genel bak???a d?n")
                ) {
                    item.style.setProperty(
                        "display", "none", "important"
                    );
                }
            });
        }

        hideExtraBack();

        if (frame._mcBackDocument === doc) return;

        if (frame._mcBackObserver) {
            frame._mcBackObserver.disconnect();
        }

        frame._mcBackDocument = doc;

        const observer = new MutationObserver(hideExtraBack);

        observer.observe(doc.body, {
            childList: true,
            subtree: true,
            characterData: true
        });

        frame._mcBackObserver = observer;
    }

    function init() {
        fixTabs();
        fixInsideFrame();

        const frame = document.getElementById(
            "mc-hammadde-content-frame"
        );

        if (frame && !frame.dataset.mcFinalBackListener) {
            frame.dataset.mcFinalBackListener = "1";

            frame.addEventListener("load", function () {
                fixInsideFrame();
            });
        }

        const nav = document.querySelector(".mc-final-nav");

        if (nav && !nav.dataset.mcFinalTextObserver) {
            nav.dataset.mcFinalTextObserver = "1";

            const observer = new MutationObserver(fixTabs);

            observer.observe(nav, {
                childList: true,
                subtree: true,
                characterData: true
            });
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
