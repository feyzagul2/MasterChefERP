(function () {
    "use strict";

    function addLink() {
        if (document.querySelector(
            'a[href="/MCOperationsSummary"],' +
            'a[href="/MCOperationsSummary/Index"]'
        )) return;

        var sidebars = Array.from(document.querySelectorAll(
            'aside, .sidebar, .side-menu, .sidebar-nav, [id*="sidebar"]'
        ));

        var sidebar = sidebars.find(function (element) {
            return element.querySelector("a[href]") &&
                element.querySelectorAll("a[href]").length >= 3;
        });

        if (!sidebar) return;

        var links = Array.from(
            sidebar.querySelectorAll("a[href]")
        );

        var anchor = links.find(function (a) {
            var label = (a.textContent || "")
                .toLocaleLowerCase("tr-TR");

            return label.includes("yönetim paneli") ||
                label.includes("yönetim özeti") ||
                label.includes("genel bakış");
        });

        if (!anchor) {
            anchor = links.find(function (a) {
                var href = a.getAttribute("href") || "";

                return href.includes("MCManagement") ||
                    href.includes("Management");
            });
        }

        if (!anchor) {
            console.info(
                "MASTER CHEF: Yonetim menusu bulunamadi; " +
                "mevcut menu korunuyor."
            );
            return;
        }

        var parent = anchor.closest("li");

        if (!parent || !parent.parentElement ||
            parent.querySelector("ul,ol")) {
            return;
        }

        var newItem = document.createElement("li");
        newItem.className = parent.className;

        var newLink = document.createElement("a");
        newLink.href = "/MCOperationsSummary";
        newLink.className = anchor.className;
        newLink.textContent = "Operasyon Özeti";
        newLink.title = "Stok, üretim, sipariş ve finans özeti";

        newItem.appendChild(newLink);
        parent.insertAdjacentElement("afterend", newItem);

        if (location.pathname.toLowerCase().startsWith(
            "/mcoperationssummary"
        )) {
            newLink.classList.add("active");
            newLink.setAttribute("aria-current", "page");
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", addLink);
    } else {
        addLink();
    }
})();