// MC_RECEIPT_STOCK_SUGGESTION_2026
(() => {
    "use strict";

    function associated(form, selector) {
        return Array.from(document.querySelectorAll(selector))
            .find(el => el.form === form) || null;
    }

    async function start() {
        const forms = document.querySelectorAll(
            'form[id^="rawtransfer-"]'
        );

        for (const form of forms) {
            const lineInput = associated(
                form, 'input[name="receiptLineId"]'
            );

            const material = associated(
                form, 'select[name="rawMaterialId"]'
            );

            const warehouse = associated(
                form, 'select[name="warehouseId"]'
            );

            if (!lineInput || !material) continue;

            const buttons = Array.from(
                document.querySelectorAll(
                    'button[type="submit"],input[type="submit"]'
                )
            ).filter(el => el.form === form);

            const originalDisabled = new Map(
                buttons.map(button => [button, button.disabled])
            );

            const note = document.createElement("div");
            note.style.cssText =
                "font-size:12px;line-height:1.5;margin-top:6px";

            material.insertAdjacentElement("afterend", note);

            const show = (message, error) => {
                note.textContent = message;
                note.style.color = error ? "#b91c1c" : "#166534";
            };

            const setBlocked = (blocked) => {
                buttons.forEach(button => {
                    button.disabled =
                        blocked || originalDisabled.get(button);
                });
            };

            show("Malzeme eşleştirmesi kontrol ediliyor...", false);
            setBlocked(true);

            try {
                const response = await fetch(
                    "/ReceiptStockSuggestion/Line?receiptLineId=" +
                    encodeURIComponent(lineInput.value),
                    { credentials: "same-origin" }
                );

                if (!response.ok)
                    throw new Error("Kontrol servisine erişilemedi.");

                const data = await response.json();

                if (data.status === "unmapped") {
                    show(data.message, false);
                    setBlocked(false);
                    continue;
                }

                if (data.status !== "matched") {
                    show(data.message || "Aktarım engellendi.", true);
                    setBlocked(true);
                    continue;
                }

                const expected = String(data.stockId);

                const option = Array.from(material.options)
                    .find(el => el.value === expected);

                if (!option) {
                    show(
                        "Eşleşen stok kartı seçim listesinde bulunamadı.",
                        true
                    );
                    setBlocked(true);
                    continue;
                }

                material.value = expected;

                const validate = () => {
                    const valid =
                        material.value === expected &&
                        (!warehouse || warehouse.value !== "");

                    setBlocked(!valid);

                    return valid;
                };

                material.addEventListener("change", () => {
                    if (material.value !== expected) {
                        material.value = expected;
                        show(
                            "Bu malzemenin stok kartı değiştirilemez.",
                            true
                        );
                    } else {
                        show(data.message, false);
                    }

                    validate();
                });

                if (warehouse)
                    warehouse.addEventListener("change", validate);

                form.addEventListener("submit", event => {
                    if (!validate()) {
                        event.preventDefault();
                        show(
                            "Doğru stok kartı ve depo seçilmelidir.",
                            true
                        );
                    }
                });

                validate();
                show(data.message, false);

            } catch {
                show(
                    "Stok kartı doğrulanamadı. Aktarım kapatıldı.",
                    true
                );

                setBlocked(true);
            }
        }
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", start);
    } else {
        start();
    }
})();
