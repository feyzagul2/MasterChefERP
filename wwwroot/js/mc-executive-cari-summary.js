(() => {
    "use strict";

    const root = document.getElementById("mc-cari-management-summary");
    if (!root) return;

    const status = document.getElementById("mc-cari-summary-status");
    const receivable = document.getElementById("mc-cari-summary-receivable");
    const payable = document.getElementById("mc-cari-summary-payable");
    const overdue = document.getElementById("mc-cari-summary-overdue");
    const due7 = document.getElementById("mc-cari-summary-due7");
    const due30 = document.getElementById("mc-cari-summary-due30");
    const cashOps = document.getElementById("mc-cari-summary-cashops");
    const note = document.getElementById("mc-cari-summary-note");
    const recent = document.getElementById("mc-cari-summary-recent");

    const money = (value, currency) => {
        if (value === null || value === undefined) return "—";
        try {
            return new Intl.NumberFormat("tr-TR", {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            }).format(value) + " " + currency;
        } catch {
            return value + " " + currency;
        }
    };

    const typeText = (value) => {
        switch ((value || "").toLowerCase()) {
            case "sale": return "Satış";
            case "purchase": return "Alış";
            case "collection": return "Tahsilat";
            case "payment": return "Ödeme";
            default: return value || "Hareket";
        }
    };

    const dateText = (value) => {
        if (!value) return "—";
        const d = new Date(value);
        if (!Number.isNaN(d.getTime())) return d.toLocaleDateString("tr-TR");
        return value;
    };

    const currencyLines = (rows, field) => {
        if (!rows || rows.length === 0) return '<span class="mc-cari-no-value">—</span>';
        return rows.map(x =>
            '<span class="mc-cari-money-line">' +
            money(x[field], x.currency) +
            '</span>'
        ).join("");
    };

    fetch("/api/mc-executive-cari-summary", {
        credentials: "same-origin",
        headers: { "Accept": "application/json" }
    })
    .then(r => {
        if (!r.ok) throw new Error("HTTP " + r.status);
        return r.json();
    })
    .then(data => {
        if (!data.hasData) {
            status.textContent = "HAREKET VERİSİ YOK";
            status.className = "mc-cari-status is-empty";
            receivable.innerHTML = "—";
            payable.innerHTML = "—";
            overdue.textContent = "—";
            due7.textContent = "—";
            due30.textContent = "—";
            cashOps.textContent = "—";
            note.textContent = "Gerçek cari hareket kaydı bulunmadığı için finansal değer gösterilmiyor.";
            recent.innerHTML = '<div class="mc-cari-empty">Henüz cari hareket kaydı bulunmuyor.</div>';
            return;
        }

        if (!data.isValid) {
            status.textContent = "KONTROL GEREKİYOR";
            status.className = "mc-cari-status is-alert";
            receivable.innerHTML = "—";
            payable.innerHTML = "—";
            note.textContent = data.warning || "Cari hareketlerde doğrulama sorunu var.";
        } else {
            status.textContent = "CANLI CARİ";
            status.className = "mc-cari-status is-live";
            receivable.innerHTML = currencyLines(data.currencies, "receivable");
            payable.innerHTML = currencyLines(data.currencies, "payable");
            note.textContent = "Alacak ve borç toplamları cari hesap bakiyelerinden, para birimleri birbirine karıştırılmadan hesaplanır.";
        }

        overdue.textContent = data.overdueDueMovementCount;
        due7.textContent = data.dueWithin7DaysCount;
        due30.textContent = data.dueWithin30DaysCount;
        cashOps.textContent = data.collectionCount + " / " + data.paymentCount;

        if (!data.recent || data.recent.length === 0) {
            recent.innerHTML = '<div class="mc-cari-empty">Son cari hareket bulunmuyor.</div>';
            return;
        }

        recent.innerHTML = data.recent.map(x => {
            const amount = x.debit > 0 ? x.debit : x.credit;
            return '<div class="mc-cari-recent-row">' +
                '<div><strong>' + (x.cariName || x.cariCode || "Cari") + '</strong>' +
                '<small>' + typeText(x.movementType) + (x.reference ? " · " + x.reference : "") + '</small></div>' +
                '<div class="mc-cari-recent-right"><b>' + money(amount, x.currency) + '</b>' +
                '<small>' + dateText(x.movementDate) + (x.dueDate ? " · Vade " + dateText(x.dueDate) : "") + '</small></div>' +
                '</div>';
        }).join("");
    })
    .catch(() => {
        status.textContent = "VERİ ALINAMADI";
        status.className = "mc-cari-status is-alert";
        receivable.innerHTML = "—";
        payable.innerHTML = "—";
        overdue.textContent = "—";
        due7.textContent = "—";
        due30.textContent = "—";
        cashOps.textContent = "—";
        note.textContent = "Cari yönetim özeti yüklenemedi.";
        recent.innerHTML = '<div class="mc-cari-empty">Cari hareketler yüklenemedi.</div>';
    });
})();
