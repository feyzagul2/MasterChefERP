(function () {
    "use strict";

    function normalize(value) {
        return (value || "")
            .replace(/\s+/g, " ")
            .trim()
            .toLocaleLowerCase("tr-TR");
    }

    function ownLabel(element) {
        if (!element) return "";

        var copy = element.cloneNode(true);

        copy.querySelectorAll("ul,ol,.submenu,.sub-menu,.dropdown-menu")
            .forEach(function (node) {
                node.remove();
            });

        return normalize(copy.textContent);
    }

    function findLink(scope, names) {
        var target = names.map(normalize);

        return Array.from(
            scope.querySelectorAll("a,button")
        ).find(function (element) {
            return target.indexOf(ownLabel(element)) !== -1;
        }) || null;
    }

    function directListItem(element) {
        if (!element) return null;

        var item = element.closest("li");

        return item && item.contains(element)
            ? item
            : null;
    }

    function findSubmenu(controlItem) {
        if (!controlItem) return null;

        var lists = Array.from(
            controlItem.querySelectorAll("ul")
        );

        return lists.find(function (list) {
            return list.querySelectorAll("a[href]").length >= 2;
        }) || null;
    }

    function initialize() {
        if (document.documentElement.dataset.mcTradeMenuV2 === "1") {
            return;
        }

        var control = findLink(document, [
            "Satın Alma Kontrol Merkezi"
        ]);

        if (!control) return;

        var controlItem = directListItem(control);
        var submenu = findSubmenu(controlItem);

        if (!controlItem || !submenu) return;

        var reconciliation = findLink(document, [
            "Cari Mutabakat"
        ]);

        var invoice = findLink(document, [
            "Fatura Kontrol Merkezi"
        ]);

        var reconciliationItem = directListItem(reconciliation);
        var invoiceItem = directListItem(invoice);

        var rootList = controlItem.parentElement;

        if (!rootList || !rootList.matches("ul")) {
            return;
        }

        /*
         * Yalnizca gercek kardes menu ogeleri tasinir.
         * Herhangi bir alt menu ya da farkli yetki alanindan
         * oge cekilmez.
         */
        var movable = [reconciliationItem, invoiceItem].filter(
            function (item) {
                return item &&
                    item !== controlItem &&
                    item.parentElement === rootList &&
                    item.querySelector("a[href]") &&
                    !item.querySelector("ul,ol");
            }
        );

        /*
         * Iki oge de ayni seviyede bulunmazsa
         * hiyerarsi degistirilmez. Mevcut menu korunur.
         */
        var canMove =
            reconciliationItem &&
            invoiceItem &&
            movable.length === 2;

        controlItem.classList.add(
            "mc-trade-item",
            "mc-trade-control"
        );

        submenu.classList.add("mc-trade-submenu");

        if (canMove) {
            var divider = document.createElement("li");
            divider.className = "mc-trade-divider";
            divider.setAttribute("aria-hidden", "true");

            submenu.appendChild(divider);

            submenu.appendChild(reconciliationItem);
            submenu.appendChild(invoiceItem);

            console.info(
                "MASTER CHEF: Ticari yonetim alt menu duzeni uygulandi."
            );
        } else {
            console.info(
                "MASTER CHEF: Menu yapisi farkli; " +
                "baglantilar tasinmadi, gorunum duzenlendi."
            );
        }

        document.documentElement.dataset.mcTradeMenuV2 = "1";
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize);
    } else {
        initialize();
    }
})();