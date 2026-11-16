(function () {
    "use strict";

    function applyCariFix() {
        try {
            var params = new URLSearchParams(window.location.search);

            if (params.get("section") !== "cari") {
                return;
            }

            var hub = document.getElementById("mc-module-hub");

            if (!hub) {
                return;
            }

            var cards = hub.querySelectorAll("a.mc-hub-card");

            if (!cards || cards.length < 5) {
                return;
            }

            var names = [
                "Cari Hesaplar",
                "Cari Hareketleri",
                "Satın Alma",
                "Alış İrsaliyeleri",
                "Mal Kabul"
            ];

            var urls = [
                "/CariAccounts",
                "/MCCariLedger",
                "/Purchasing",
                "/PurchaseDispatch",
                "/PurchaseGoodsReceipt"
            ];

            for (var i = 0; i < 5; i++) {
                var card = cards[i];

                card.href = urls[i];

                var title = card.querySelector(".mc-hub-card-title");

                if (title) {
                    title.textContent = names[i];
                }
            }

            for (var j = 5; j < cards.length; j++) {
                cards[j].style.display = "none";
            }

            hub.setAttribute("data-cari-fixed", "yes");
        }
        catch (e) {
            console.error("MC Cari Hub Fix:", e);
        }
    }

    function startCariFix() {
        applyCariFix();

        var counter = 0;

        var timer = setInterval(function () {
            applyCariFix();

            counter++;

            if (counter >= 50) {
                clearInterval(timer);
            }
        }, 200);

        var observer = new MutationObserver(function () {
            applyCariFix();
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", startCariFix);
    }
    else {
        startCariFix();
    }
})();