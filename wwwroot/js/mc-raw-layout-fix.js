// MC_RAW_ALIGNMENT_FIX_2026
(function () {
    "use strict";

    function init() {
        const frame = document.getElementById(
            "mc-hammadde-content-frame"
        );

        const nav = document.querySelector(".mc-final-nav");

        if (!frame || !nav) return;

        if (window.mcRawAlignmentLoaded) return;
        window.mcRawAlignmentLoaded = true;

        const style = document.createElement("style");

        style.textContent = `
            .mc-final-nav {
                display:flex!important;
                flex-wrap:wrap!important;
                align-items:center!important;
                gap:8px!important;
                width:100%!important;
                max-width:100%!important;
                box-sizing:border-box!important;
                margin:12px auto!important;
            }

            .mc-final-nav a {
                display:inline-flex!important;
                align-items:center!important;
                justify-content:center!important;
                min-height:46px!important;
                padding:10px 16px!important;
                text-align:center!important;
                white-space:normal!important;
            }

            #mc-hammadde-content {
                display:block!important;
                width:100%!important;
                max-width:100%!important;
                min-width:0!important;
                margin:0 auto!important;
                padding:0!important;
                border:0!important;
                box-sizing:border-box!important;
                overflow:hidden!important;
            }

            #mc-hammadde-content-frame {
                display:block!important;
                width:100%!important;
                max-width:100%!important;
                min-width:0!important;
                margin:0!important;
                padding:0!important;
                border:0!important;
                box-sizing:border-box!important;
            }

            @media(max-width:700px) {
                .mc-final-nav {
                    display:grid!important;
                    grid-template-columns:
                        repeat(2,minmax(0,1fr))!important;
                    gap:7px!important;
                }

                .mc-final-nav a {
                    width:100%!important;
                    min-width:0!important;
                    font-size:12px!important;
                    padding:10px 6px!important;
                }
            }

            @media(max-width:380px) {
                .mc-final-nav {
                    grid-template-columns:1fr!important;
                }
            }
        `;

        document.head.appendChild(style);

        const labels = {
            "/MaterialCatalog": "Malzeme Katalo?u",
            "/RawMaterials": "Hammadde Stok Kartlar?",
            "/MaterialStockMapping": "Stok Kart? E?le?tirme",
            "/MCRawWarehouses": "Hammadde Depolar?"
        };

        nav.querySelectorAll("a[href]").forEach(function (a) {
            const url = new URL(a.href, location.origin);

            if (labels[url.pathname]) {
                a.textContent = labels[url.pathname];
            }
        });

        function fixFrame() {
            let doc;

            try {
                doc = frame.contentDocument;
            } catch {
                return;
            }

            if (!doc || !doc.body) return;

            const main =
                doc.querySelector("main") ||
                doc.querySelector('[role="main"]') ||
                doc.querySelector(".main-content") ||
                doc.querySelector(".content-wrapper");

            if (!main) return;

            // ?? sayfan?n ikinci sol men?s?n? ve
            // ?st ba?l???n? g?r?n?mden ??kar.
            let current = main;

            while (current && current !== doc.body) {
                const parent = current.parentElement;

                if (!parent) break;

                Array.from(parent.children).forEach(function (child) {
                    if (child === current) return;

                    if (["SCRIPT", "STYLE", "LINK"].includes(child.tagName))
                        return;

                    child.style.setProperty(
                        "display", "none", "important"
                    );
                });

                current = parent;
            }

            // Eski uygulama d?zeninden kalan
            // 250px sol bo?lu?u s?f?rla.
            current = main;

            while (current) {
                const css = current.style;

                css.setProperty("margin-left", "0", "important");
                css.setProperty("margin-right", "0", "important");
                css.setProperty("padding-left", "0", "important");
                css.setProperty("padding-right", "0", "important");
                css.setProperty("width", "100%", "important");
                css.setProperty("max-width", "100%", "important");
                css.setProperty("min-width", "0", "important");
                css.setProperty("left", "0", "important");
                css.setProperty("transform", "none", "important");
                css.setProperty(
                    "grid-template-columns", "minmax(0,1fr)", "important"
                );

                if (current !== main) {
                    css.setProperty("display", "block", "important");
                }

                if (current === doc.body) break;

                current = current.parentElement;
            }

            if (!doc.getElementById("mc-alignment-style")) {
                const inner = doc.createElement("style");
                inner.id = "mc-alignment-style";

                inner.textContent = `
                    html,body {
                        width:100%!important;
                        max-width:100%!important;
                        min-width:0!important;
                        overflow-x:hidden!important;
                        background:transparent!important;
                    }

                    main {
                        box-sizing:border-box!important;
                        width:100%!important;
                        max-width:100%!important;
                        min-width:0!important;
                        margin:0!important;
                        padding:8px 12px!important;
                    }

                    main .container,
                    main .container-fluid {
                        width:100%!important;
                        max-width:100%!important;
                        min-width:0!important;
                        box-sizing:border-box!important;
                    }

                    main .table-responsive,
                    main table {
                        max-width:100%!important;
                    }

                    main .table-responsive {
                        overflow-x:auto!important;
                    }

                    @media(max-width:700px) {
                        main {
                            padding:6px!important;
                        }

                        input,select,textarea {
                            max-width:100%!important;
                            box-sizing:border-box!important;
                            font-size:16px!important;
                        }
                    }
                `;

                doc.head.appendChild(inner);
            }

            // ??erideki tekrar eden geri d?n??
            // ve yard?m butonlar?n? gizle.
            doc.querySelectorAll("main a,main button").forEach(
                function (el) {
                    const label = (el.textContent || "")
                        .trim()
                        .toLocaleLowerCase("tr-TR");

                    if (
                        label.includes("?nceki sayfaya d?n") ||
                        label.includes("bu sayfa nas?l kullan?l?r")
                    ) {
                        el.style.setProperty(
                            "display", "none", "important"
                        );
                    }
                }
            );

            function resize() {
                const height = Math.max(
                    doc.body.scrollHeight,
                    doc.documentElement.scrollHeight,
                    500
                );

                frame.style.setProperty(
                    "height", (height + 20) + "px", "important"
                );
            }

            resize();

            if (frame.mcObserver) frame.mcObserver.disconnect();

            if (window.ResizeObserver) {
                frame.mcObserver = new ResizeObserver(resize);
                frame.mcObserver.observe(main);
            }

            setTimeout(resize, 300);
            setTimeout(resize, 900);
        }

        frame.addEventListener("load", fixFrame);

        if (frame.contentDocument?.readyState === "complete") {
            fixFrame();
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
