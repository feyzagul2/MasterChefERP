(function () {
    "use strict";

    const API_URL = "/api/mc-executive-finance";

    function money(value, currency) {
        if (value === null || value === undefined)
            return "—";

        try {
            return new Intl.NumberFormat("tr-TR", {
                style: "currency",
                currency: currency || "TRY",
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            }).format(value);
        }
        catch {
            return new Intl.NumberFormat("tr-TR", {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            }).format(value) + " " + (currency || "");
        }
    }

    function valueLines(rows, key) {
        const available = (rows || [])
            .filter(function (row) {
                return row[key] !== null &&
                       row[key] !== undefined;
            });

        if (!available.length)
            return '<span class="mc-finance-empty">—</span>';

        return available.map(function (row) {
            return '<span class="mc-finance-currency-line">' +
                money(row[key], row.currency) +
                '</span>';
        }).join("");
    }

    function ensurePanel() {
        let panel =
            document.getElementById("mc-final-finance-status");

        if (panel)
            return panel;

        const dashboard =
            document.getElementById("mc-executive-dashboard");

        if (!dashboard)
            return null;

        panel = document.createElement("section");
        panel.id = "mc-final-finance-status";
        panel.className = "mc-final-finance-status";

        panel.innerHTML = `
            <div class="mc-final-finance-head">
                <div>
                    <h3>Finansal Durum</h3>
                    <p>
                        Banka, kasa ve cari hareketlerinden
                        hesaplanan gerçek finans özeti
                    </p>
                </div>
                <span id="mcFinanceDataBadge"
                      class="mc-final-finance-badge">
                    KONTROL EDİLİYOR
                </span>
            </div>

            <div class="mc-final-finance-grid">
                <article>
                    <small>Banka</small>
                    <strong id="mcFinanceBank">—</strong>
                </article>

                <article>
                    <small>Kasa</small>
                    <strong id="mcFinanceCash">—</strong>
                </article>

                <article>
                    <small>Cari Alacak</small>
                    <strong id="mcFinanceReceivable">—</strong>
                </article>

                <article>
                    <small>Cari Borç</small>
                    <strong id="mcFinancePayable">—</strong>
                </article>

                <article>
                    <small>Net Pozisyon</small>
                    <strong id="mcFinanceNet">—</strong>
                </article>
            </div>

            <div id="mcFinanceDataMessage"
                 class="mc-final-finance-message">
                Finans hareketleri kontrol ediliyor.
            </div>

            <div class="mc-final-finance-links">
                <a href="/MCBankCash">Banka/Kasa Hesapları</a>
                <a href="/MCBankCashReport">Banka/Kasa Raporu</a>
                <a href="/MCCariLedger">Cari Hareketleri</a>
                <a href="/MCBankCashAudit">Finans Kontrol</a>
            </div>
        `;

        dashboard.appendChild(panel);

        return panel;
    }

    function setHtml(id, html) {
        const element = document.getElementById(id);

        if (element)
            element.innerHTML = html;
    }

    function setText(id, text) {
        const element = document.getElementById(id);

        if (element)
            element.textContent = text;
    }

    function hideLegacyFinance() {
        const live =
            document.getElementById("mc-finance-live");

        if (live)
            live.style.display = "none";

        document
            .querySelectorAll(".mc-exec-finance-grid")
            .forEach(function (element) {
                element.style.display = "none";
            });
    }

    async function loadFinance() {
        ensurePanel();
        hideLegacyFinance();

        try {
            const response = await fetch(API_URL, {
                method: "GET",
                credentials: "same-origin",
                headers: {
                    "Accept": "application/json"
                }
            });

            if (!response.ok)
                throw new Error("HTTP " + response.status);

            const data = await response.json();
            const rows = data.currencies || [];

            setHtml(
                "mcFinanceBank",
                valueLines(rows, "bank")
            );

            setHtml(
                "mcFinanceCash",
                valueLines(rows, "cash")
            );

            setHtml(
                "mcFinanceReceivable",
                valueLines(rows, "receivable")
            );

            setHtml(
                "mcFinancePayable",
                valueLines(rows, "payable")
            );

            setHtml(
                "mcFinanceNet",
                valueLines(rows, "netPosition")
            );

            if (!data.hasAnyFinanceMovement) {
                setText(
                    "mcFinanceDataBadge",
                    "HAREKET VERİSİ YOK"
                );

                setText(
                    "mcFinanceDataMessage",
                    "Finans hareket tablolarında henüz gerçek kayıt bulunmuyor. " +
                    "Bu nedenle 0 TL gösterilmiyor."
                );

                return;
            }

            setText(
                "mcFinanceDataBadge",
                "CANLI VERİ"
            );

            const notes = [];

            if (!data.hasBankCashMovements)
                notes.push("Banka/kasa hareketi henüz yok.");

            if (!data.hasCariMovements)
                notes.push("Cari hareket henüz yok.");

            if (data.unknownAccountTypes &&
                data.unknownAccountTypes.length) {

                notes.push(
                    "Tanımsız hesap türleri: " +
                    data.unknownAccountTypes.join(", ")
                );
            }

            if (notes.length) {
                setText(
                    "mcFinanceDataMessage",
                    notes.join(" ")
                );
            }
            else {
                setText(
                    "mcFinanceDataMessage",
                    "Tüm göstergeler gerçek banka/kasa ve cari hareketlerinden hesaplanıyor."
                );
            }
        }
        catch (error) {
            setText(
                "mcFinanceDataBadge",
                "VERİ ALINAMADI"
            );

            setText(
                "mcFinanceDataMessage",
                "Finans özeti okunamadı. Mevcut kayıtlar değiştirilmedi."
            );

            console.error(
                "MC finance dashboard error:",
                error
            );
        }
    }

    function init() {
        ensurePanel();
        hideLegacyFinance();
        loadFinance();
    }

    if (document.readyState === "loading")
        document.addEventListener("DOMContentLoaded", init);
    else
        init();
})();