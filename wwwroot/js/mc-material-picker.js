// MC_MATERIAL_PICKER_2026
(() => {
    "use strict";

    function init() {
        const fields = document.querySelectorAll(
            'input[name$=".ProductDescription"]'
        );

        fields.forEach((input, index) => {
            if (input.dataset.mcMaterialPicker === "1") return;

            input.dataset.mcMaterialPicker = "1";
            input.setAttribute("autocomplete", "off");

            const list = document.createElement("datalist");
            list.id = "mc-material-options-" + index;
            input.setAttribute("list", list.id);
            input.insertAdjacentElement("afterend", list);

            let timer = null;
            let requestId = 0;

            input.addEventListener("input", () => {
                clearTimeout(timer);
                const value = input.value.trim();
                const current = ++requestId;

                if (value.length < 2) {
                    list.replaceChildren();
                    return;
                }

                timer = setTimeout(async () => {
                    try {
                        const url =
                            "/MaterialLookup/Search?q=" +
                            encodeURIComponent(value);

                        const response = await fetch(url, {
                            credentials: "same-origin"
                        });

                        if (!response.ok) return;

                        const records = await response.json();

                        if (current !== requestId) return;

                        list.replaceChildren();

                        for (const item of records) {
                            const option = document.createElement("option");

                            option.value =
                                item.code + " - " + item.name;

                            option.label = item.type;

                            list.appendChild(option);
                        }
                    } catch {
                        // Normal manual entry remains available.
                    }
                }, 250);
            });
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", init);
    } else {
        init();
    }
})();
