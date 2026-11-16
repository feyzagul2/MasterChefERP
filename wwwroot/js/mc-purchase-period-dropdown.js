(function () {
    "use strict";

    function cleanText(value) {
        return (value || "")
            .replace(/\s+/g, " ")
            .trim()
            .toLocaleLowerCase("tr-TR");
    }

    function formatDate(date) {
        const y = date.getFullYear();
        const m = String(date.getMonth() + 1).padStart(2, "0");
        const d = String(date.getDate()).padStart(2, "0");

        return y + "-" + m + "-" + d;
    }

    function setDate(input, date) {
        if (!input) return;

        input.value = formatDate(date);

        input.dispatchEvent(
            new Event("input", { bubbles: true })
        );

        input.dispatchEvent(
            new Event("change", { bubbles: true })
        );
    }

    function init() {
        if (document.querySelector(".mc-period-dropdown")) {
            return;
        }

        const elements = Array.from(
            document.querySelectorAll("button, a")
        );

        function findButton(label) {
            const target = cleanText(label);

            return elements.find(function (el) {
                return cleanText(el.textContent) === target;
            });
        }

        const original = {
            thisMonth:  findButton("Bu Ay"),
            lastMonth:  findButton("Geçen Ay"),
            all:        findButton("Tüm Kayıtlar"),
            last7:      findButton("Son 7 Gün"),
            last30:     findButton("Son 30 Gün"),
            last90:     findButton("Son 90 Gün")
        };

        if (!original.thisMonth) {
            return;
        }

        const dateInputs = Array.from(
            document.querySelectorAll('input[type="date"]')
        );

        if (dateInputs.length < 2) {
            return;
        }

        const startInput = dateInputs[0];
        const endInput   = dateInputs[1];

        const details = document.createElement("details");
        details.className = "mc-period-dropdown";

        const summary = document.createElement("summary");
        summary.textContent = "Dönem Seç";

        const menu = document.createElement("div");
        menu.className = "mc-period-dropdown-menu";

        function addOption(label, callback) {
            const button = document.createElement("button");

            button.type = "button";
            button.className = "mc-period-dropdown-item";
            button.textContent = label;

            button.addEventListener("click", function (event) {
                event.preventDefault();
                event.stopPropagation();

                callback();

                summary.textContent = label;
                details.open = false;
            });

            menu.appendChild(button);
        }

        addOption("Bu Ay", function () {
            if (original.thisMonth) {
                original.thisMonth.click();
            }
        });

        addOption("Geçen Ay", function () {
            if (original.lastMonth) {
                original.lastMonth.click();
            }
        });

        addOption("Son 2 Ay", function () {
            const today = new Date();

            const start = new Date(
                today.getFullYear(),
                today.getMonth() - 1,
                1
            );

            setDate(startInput, start);
            setDate(endInput, today);
        });

        addOption("Tüm Kayıtlar", function () {
            if (original.all) {
                original.all.click();
            }
            else {
                startInput.value = "";
                endInput.value = "";

                startInput.dispatchEvent(
                    new Event("change", { bubbles: true })
                );

                endInput.dispatchEvent(
                    new Event("change", { bubbles: true })
                );
            }
        });

        addOption("Son 7 Gün", function () {
            if (original.last7) {
                original.last7.click();
            }
        });

        addOption("Son 30 Gün", function () {
            if (original.last30) {
                original.last30.click();
            }
        });

        addOption("Son 90 Gün", function () {
            if (original.last90) {
                original.last90.click();
            }
        });

        details.appendChild(summary);
        details.appendChild(menu);

        original.thisMonth.parentNode.insertBefore(
            details,
            original.thisMonth
        );

        [
            original.thisMonth,
            original.lastMonth,
            original.all,
            original.last7,
            original.last30,
            original.last90
        ].forEach(function (el) {
            if (el) {
                el.style.setProperty(
                    "display",
                    "none",
                    "important"
                );
            }
        });

        

        document.addEventListener("click", function (event) {
            if (
                details.open &&
                !details.contains(event.target)
            ) {
                details.open = false;
            }
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            init
        );
    }
    else {
        init();
    }

    setTimeout(init, 200);
    setTimeout(init, 500);
    setTimeout(init, 1000);
})();



