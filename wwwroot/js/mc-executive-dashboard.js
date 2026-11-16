/* MC_EXECUTIVE_DASHBOARD_JS_V1 */
(function(){
    "use strict";

    function clean(s){
        return (s || "")
            .replace(/\s+/g," ")
            .trim();
    }

    function numberFromText(s){
        var m = clean(s).replace(/\./g,"").match(/-?\d+(?:,\d+)?/);
        if(!m) return null;

        var n = Number(m[0].replace(",","."));
        return Number.isFinite(n) ? n : null;
    }

    function candidateCards(){
        var selectors = [
            ".metric-card",
            ".mc-metric",
            ".mc-stat-card",
            ".mc-ops-card",
            ".summary-card",
            ".card"
        ];

        var found = [];

        selectors.forEach(function(selector){
            document.querySelectorAll(selector).forEach(function(el){
                if(
                    !el.closest("#mc-executive-dashboard") &&
                    !found.includes(el)
                ){
                    found.push(el);
                }
            });
        });

        /*
         Siniflar farkliysa sayisal deger tasiyan
         OperationsOverview link/kartlarini da yakala.
        */
        if(found.length < 4){
            document.querySelectorAll("main a, main section, main article, main div")
                .forEach(function(el){
                    if(el.closest("#mc-executive-dashboard")) return;

                    var txt = clean(el.innerText);

                    if(
                        txt.length > 2 &&
                        txt.length < 120 &&
                        /\d/.test(txt) &&
                        !found.includes(el)
                    ){
                        var children = el.querySelectorAll("*").length;

                        if(children <= 8){
                            found.push(el);
                        }
                    }
                });
        }

        return found;
    }

    function readMetrics(){
        var cards = candidateCards();
        var rows = [];
        var seen = {};

        cards.forEach(function(card){

            var txt = clean(card.innerText);
            if(!txt) return;

            var value = numberFromText(txt);
            if(value === null) return;

            var label = txt
                .replace(/-?\d[\d.,]*/g," ")
                .replace(/\s+/g," ")
                .trim();

            if(!label || label.length > 70) return;

            var key = label.toLocaleLowerCase("tr-TR");

            if(seen[key]) return;
            seen[key] = true;

            rows.push({
                label: label,
                value: value,
                element: card
            });
        });

        return rows;
    }

    function findMetric(rows, words){
        var normalizedWords = words.map(function(x){
            return x.toLocaleLowerCase("tr-TR");
        });

        return rows.find(function(row){
            var label = row.label.toLocaleLowerCase("tr-TR");

            return normalizedWords.some(function(word){
                return label.includes(word);
            });
        });
    }

    function renderKpis(rows){
        var host = document.getElementById("mc-exec-kpis");
        if(!host) return;

        var wanted = [
            {
                title:"Ürün",
                words:["ürün","urun"]
            },
            {
                title:"Hammadde",
                words:["hammadde"]
            },
            {
                title:"Üretim Emri",
                words:["üretim emri","uretim emri","üretim","uretim"]
            },
            {
                title:"Sevkiyat",
                words:["sevkiyat"]
            }
        ];

        host.innerHTML = "";

        wanted.forEach(function(item){
            var row = findMetric(rows,item.words);

            var box = document.createElement("div");
            box.className = "mc-exec-kpi";

            var label = document.createElement("div");
            label.className = "mc-exec-kpi-label";
            label.textContent = item.title;

            var value = document.createElement("div");
            value.className = "mc-exec-kpi-value";
            value.textContent = row ? row.value.toLocaleString("tr-TR") : "—";

            box.appendChild(label);
            box.appendChild(value);
            host.appendChild(box);
        });
    }

    function chartRows(rows){
        return rows
            .filter(function(x){
                return x.value >= 0;
            })
            .slice(0,10);
    }

    function renderDonut(rows){
        var host = document.getElementById("mc-exec-donut");
        if(!host) return;

        var data = chartRows(rows).slice(0,6);

        var total = data.reduce(function(sum,x){
            return sum + x.value;
        },0);

        host.innerHTML = "";

        if(total <= 0){
            host.innerHTML =
                '<div style="color:#64748b;font-size:13px">' +
                'Grafik için sayısal operasyon verisi bulunamadı.' +
                '</div>';
            return;
        }

        var colors = [
            "#173b60",
            "#315d87",
            "#5e7fa0",
            "#89a1b8",
            "#aebfce",
            "#d0dbe5"
        ];

        var wrap = document.createElement("div");
        wrap.className = "mc-exec-donut-wrap";

        var ns = "http://www.w3.org/2000/svg";

        var svg = document.createElementNS(ns,"svg");
        svg.setAttribute("viewBox","0 0 200 200");
        svg.setAttribute("class","mc-exec-donut-svg");

        var bg = document.createElementNS(ns,"circle");
        bg.setAttribute("cx","100");
        bg.setAttribute("cy","100");
        bg.setAttribute("r","70");
        bg.setAttribute("class","mc-exec-donut-bg");
        svg.appendChild(bg);

        var circumference = 2 * Math.PI * 70;
        var offset = 0;

        data.forEach(function(row,index){

            var length = circumference * (row.value / total);

            var circle = document.createElementNS(ns,"circle");

            circle.setAttribute("cx","100");
            circle.setAttribute("cy","100");
            circle.setAttribute("r","70");
            circle.setAttribute("class","mc-exec-donut-segment");
            circle.setAttribute("stroke",colors[index % colors.length]);

            circle.setAttribute(
                "stroke-dasharray",
                length + " " + (circumference - length)
            );

            circle.setAttribute(
                "stroke-dashoffset",
                -offset
            );

            svg.appendChild(circle);

            offset += length;
        });

        var center = document.createElement("div");
        center.className = "mc-exec-donut-center";
        center.innerHTML =
            "<strong>" +
            total.toLocaleString("tr-TR") +
            "</strong><span>Toplam kayıt</span>";

        wrap.appendChild(svg);
        wrap.appendChild(center);

        var legend = document.createElement("div");
        legend.className = "mc-exec-legend";

        data.forEach(function(row,index){

            var item = document.createElement("div");
            item.className = "mc-exec-legend-row";

            item.innerHTML =
                '<span class="mc-exec-legend-dot" style="background:' +
                colors[index % colors.length] +
                '"></span>' +
                '<span class="mc-exec-legend-name">' +
                row.label +
                '</span>' +
                '<span class="mc-exec-legend-value">' +
                row.value.toLocaleString("tr-TR") +
                '</span>';

            legend.appendChild(item);
        });

        host.appendChild(wrap);
        host.appendChild(legend);
    }

    function renderBars(rows){
        var host = document.getElementById("mc-exec-bars");
        if(!host) return;

        var data = chartRows(rows);
        var max = Math.max.apply(
            null,
            data.map(function(x){ return x.value; }).concat([1])
        );

        host.innerHTML = "";

        data.forEach(function(row){

            var width = Math.max(
                1,
                Math.round((row.value / max) * 100)
            );

            var item = document.createElement("div");
            item.className = "mc-exec-bar-row";

            item.innerHTML =
                '<div class="mc-exec-bar-label" title="' +
                row.label.replace(/"/g,"&quot;") +
                '">' +
                row.label +
                '</div>' +
                '<div class="mc-exec-bar-track">' +
                    '<div class="mc-exec-bar-fill" style="width:' +
                    width +
                    '%"></div>' +
                '</div>' +
                '<div class="mc-exec-bar-value">' +
                row.value.toLocaleString("tr-TR") +
                '</div>';

            host.appendChild(item);
        });
    }

    function hideOldMetrics(rows){
        rows.forEach(function(row){

            var el = row.element;

            /*
             Dashboard'in kendi basliklarini veya buyuk layout
             containerlarini gizleme.
            */
            if(
                el.closest("#mc-executive-dashboard") ||
                el.tagName === "MAIN"
            ){
                return;
            }

            el.classList.add("mc-exec-original-hidden");
        });
    }

    function moveDashboardToTop(){
        var dashboard =
            document.getElementById("mc-executive-dashboard");

        var main =
            document.querySelector("main");

        if(!dashboard || !main) return;

        /*
         Sayfa basligi / geri linki varsa onlari koru.
         Dashboard'u ilk ana içerik bloklarindan sonra konumla.
        */
        var h1 = main.querySelector("h1");

        if(h1){
            var next = h1.nextElementSibling;

            if(next){
                next.insertAdjacentElement(
                    "afterend",
                    dashboard
                );
                return;
            }
        }

        main.prepend(dashboard);
    }

    function init(){
        var dashboard =
            document.getElementById("mc-executive-dashboard");

        if(!dashboard) return;

        var rows = readMetrics();

        renderKpis(rows);
        renderDonut(rows);
        renderBars(rows);

        hideOldMetrics(rows);
        moveDashboardToTop();
    }

    if(document.readyState === "loading"){
        document.addEventListener(
            "DOMContentLoaded",
            function(){
                setTimeout(init,100);
            }
        );
    } else {
        setTimeout(init,100);
    }
})();
/* MC_EXECUTIVE_DASHBOARD_JS_V1_END */