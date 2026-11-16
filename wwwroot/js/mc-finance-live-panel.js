/* MC_FINANCE_LIVE_PANEL_JS_V1 */
(function () {
    "use strict";

    function create(tag, cls, text) {
        var el = document.createElement(tag);

        if (cls) {
            el.className = cls;
        }

        if (text !== undefined && text !== null) {
            el.textContent = text;
        }

        return el;
    }

    function cleanFrame(frame) {
        try {
            var doc = frame.contentDocument ||
                      frame.contentWindow.document;

            if (!doc) return;

            var style = doc.createElement("style");

            style.textContent = [
                "body{background:#fff!important;}",
                "body>aside,.sidebar,#sidebar{display:none!important;}",
                "header,.topbar,.navbar,.app-header{display:none!important;}",
                "main{margin-left:0!important;padding:18px!important;width:100%!important;max-width:none!important;}",
                ".main-content,.content-wrapper{margin-left:0!important;width:100%!important;max-width:none!important;}",
                "body{padding-left:0!important;margin-left:0!important;}",
                "a,button,[role=button]{opacity:1!important;}"
            ].join("");

            doc.head.appendChild(style);

            var main = doc.querySelector("main");

            if (main) {
                main.style.setProperty(
                    "margin-left",
                    "0",
                    "important"
                );

                main.style.setProperty(
                    "max-width",
                    "none",
                    "important"
                );

                main.style.setProperty(
                    "width",
                    "100%",
                    "important"
                );
            }

            /*
             Iframe yuksekligini ic sayfanin gercek yuksekligine gore
             ayarla. Alt tablolar kesilmesin.
            */
            setTimeout(function () {
                try {
                    var h = Math.max(
                        doc.body.scrollHeight,
                        doc.documentElement.scrollHeight,
                        650
                    );

                    frame.style.height =
                        Math.min(h + 30, 1800) + "px";
                }
                catch (_) {}
            }, 250);
        }
        catch (_) {
            /*
             Ayni origin oldugu icin normalde buraya dusmez.
             Finans verisine müdahale edilmez.
            */
        }
    }

    function buildPanel() {
        if (document.getElementById("mc-finance-live")) {
            return;
        }

        var dashboard =
            document.getElementById("mc-executive-dashboard");

        if (!dashboard) {
            dashboard = document.querySelector("main");
        }

        if (!dashboard) return;

        var panel = create(
            "section",
            "mc-fin-live"
        );

        panel.id = "mc-finance-live";

        var head = create(
            "div",
            "mc-fin-live-head"
        );

        var title = create(
            "div",
            "mc-fin-live-title"
        );

        title.innerHTML =
            "<strong>Finansal Yönetim</strong>" +
            "<span>Banka, kasa, cari ve finans hareketleri — mevcut gerçek kayıtlar</span>";

        var badge = create(
            "div",
            "mc-fin-live-badge",
            "GERÇEK VERİ"
        );

        head.appendChild(title);
        head.appendChild(badge);

        var tabs = create(
            "div",
            "mc-fin-tabs"
        );

        var frameWrap = create(
            "div",
            "mc-fin-frame-wrap"
        );

        var loading = create(
            "div",
            "mc-fin-loading",
            "Finans verileri yükleniyor..."
        );

        frameWrap.appendChild(loading);

        var pages = [
            {
                id: "summary",
                label: "Finans Özeti",
                url: "/MCFinanceOverview"
            },
            {
                id: "bankcash",
                label: "Banka / Kasa",
                url: "/MCBankCashReport"
            },
            {
                id: "cari",
                label: "Cari Hareketleri",
                url: "/MCCariLedger"
            },
            {
                id: "audit",
                label: "Finans Kontrol",
                url: "/MCBankCashAudit"
            }
        ];

        var frames = {};

        function activate(id) {

            tabs.querySelectorAll(".mc-fin-tab")
                .forEach(function (button) {
                    button.classList.toggle(
                        "is-active",
                        button.dataset.id === id
                    );
                });

            Object.keys(frames).forEach(function (key) {
                frames[key].classList.toggle(
                    "is-active",
                    key === id
                );
            });

            loading.classList.remove("is-hidden");

            var frame = frames[id];

            if (!frame.dataset.loaded) {
                frame.src = frame.dataset.src;
                frame.dataset.loaded = "1";
            }
            else {
                loading.classList.add("is-hidden");
            }
        }

        pages.forEach(function (page, index) {

            var button = create(
                "button",
                "mc-fin-tab",
                page.label
            );

            button.type = "button";
            button.dataset.id = page.id;

            button.addEventListener(
                "click",
                function () {
                    activate(page.id);
                }
            );

            tabs.appendChild(button);

            var frame = create(
                "iframe",
                "mc-fin-frame"
            );

            frame.dataset.src = page.url;
            frame.setAttribute(
                "title",
                page.label
            );

            frame.addEventListener(
                "load",
                function () {

                    cleanFrame(frame);

                    if (
                        frame.classList.contains(
                            "is-active"
                        )
                    ) {
                        loading.classList.add(
                            "is-hidden"
                        );
                    }
                }
            );

            frames[page.id] = frame;
            frameWrap.appendChild(frame);

            if (index === 0) {
                button.classList.add("is-active");
                frame.classList.add("is-active");
            }
        });

        var shortcuts = create(
            "div",
            "mc-fin-shortcuts"
        );

        var links = [
            {
                url: "/MCFinanceOverview",
                title: "Finans Özeti",
                desc: "Genel finans görünümü"
            },
            {
                url: "/MCBankCashReport",
                title: "Banka / Kasa",
                desc: "Giriş, çıkış ve bakiye"
            },
            {
                url: "/MCCariLedger",
                title: "Cari Hesaplar",
                desc: "Borç / alacak hareketleri"
            },
            {
                url: "/MCBankCashAudit",
                title: "Kontrol Merkezi",
                desc: "Eşleşmeyen ve hatalı kayıtlar"
            }
        ];

        links.forEach(function (item) {

            var a = create(
                "a",
                "mc-fin-shortcut"
            );

            a.href = item.url;

            a.innerHTML =
                "<strong>" +
                item.title +
                "</strong>" +
                "<span>" +
                item.desc +
                "</span>";

            shortcuts.appendChild(a);
        });

        panel.appendChild(head);
        panel.appendChild(tabs);
        panel.appendChild(frameWrap);
        panel.appendChild(shortcuts);

        /*
         Finans basligi varsa hemen onun altina;
         yoksa dashboard'un sonuna.
        */
        var financeHead =
            dashboard.querySelector(
                ".mc-exec-finance-head"
            );

        if (financeHead) {

            var oldFinanceGrid =
                dashboard.querySelector(
                    ".mc-exec-finance-grid"
                );

            if (oldFinanceGrid) {
                oldFinanceGrid.style.display = "none";
            }

            financeHead.insertAdjacentElement(
                "afterend",
                panel
            );
        }
        else {
            dashboard.appendChild(panel);
        }

        /*
         Ilk finans sayfasini yukle.
        */
        activate("summary");
    }

    function init() {
        buildPanel();
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            function () {
                setTimeout(init, 250);
            }
        );
    }
    else {
        setTimeout(init, 250);
    }

})();
 /* MC_FINANCE_LIVE_PANEL_JS_V1_END */