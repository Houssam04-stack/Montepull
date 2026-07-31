// MVP-0 report print + APS Gantt / planning exports
window.axioplan = window.axioplan || {};

window.axioplan.printElementById = function (elementId) {
  var el = document.getElementById(elementId);
  if (!el) {
    window.print();
    return;
  }

  if (el.tagName === "IFRAME") {
    try {
      var win = el.contentWindow;
      if (win) {
        win.focus();
        win.print();
        return;
      }
    } catch (e) {
      /* fall through */
    }
  }

  var html = el.tagName === "IFRAME"
    ? (el.srcdoc || "")
    : el.outerHTML;
  var w = window.open("", "_blank", "noopener,noreferrer,width=900,height=700");
  if (!w) {
    alert("Autorisez les pop-ups pour Imprimer / PDF.");
    return;
  }
  w.document.open();
  w.document.write("<!DOCTYPE html><html><head><title>Rapport</title>");
  w.document.write("<style>body{font-family:Segoe UI,sans-serif;padding:1rem} @media print{body{padding:0}}</style>");
  w.document.write("</head><body>");
  w.document.write(html);
  w.document.write("</body></html>");
  w.document.close();
  w.focus();
  setTimeout(function () { w.print(); }, 250);
};

window.axioplan.downloadText = function (filename, content, mime) {
  var blob = new Blob([content], { type: mime || "text/plain;charset=utf-8" });
  var url = URL.createObjectURL(blob);
  var a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
};

window.axioplan.downloadSvgMarkup = function (svgMarkup, filename) {
  if (!svgMarkup) return false;
  var xml = svgMarkup;
  if (xml.indexOf("xmlns=") < 0) {
    xml = xml.replace("<svg", '<svg xmlns="http://www.w3.org/2000/svg"');
  }
  window.axioplan.downloadText(filename || "gantt.svg", xml, "image/svg+xml;charset=utf-8");
  return true;
};

window.axioplan.downloadSvgById = function (elementId, filename) {
  var svg = document.getElementById(elementId);
  if (!svg) return false;
  var clone = svg.cloneNode(true);
  if (!clone.getAttribute("xmlns")) {
    clone.setAttribute("xmlns", "http://www.w3.org/2000/svg");
  }
  var xml = new XMLSerializer().serializeToString(clone);
  window.axioplan.downloadText(filename || "gantt.svg", xml, "image/svg+xml;charset=utf-8");
  return true;
};

window.axioplan.downloadPngFromSvgMarkup = function (svgMarkup, filename, scale) {
  return new Promise(function (resolve) {
    if (!svgMarkup) {
      resolve(false);
      return;
    }
    var xml = svgMarkup;
    if (xml.indexOf("xmlns=") < 0) {
      xml = xml.replace("<svg", '<svg xmlns="http://www.w3.org/2000/svg"');
    }
    var parser = new DOMParser();
    var doc = parser.parseFromString(xml, "image/svg+xml");
    var svg = doc.documentElement;
    var width = parseFloat(svg.getAttribute("width") || "1200");
    var height = parseFloat(svg.getAttribute("height") || "480");
    var s = Math.max(1, scale || 2);
    var img = new Image();
    var url = URL.createObjectURL(new Blob([xml], { type: "image/svg+xml;charset=utf-8" }));
    img.onload = function () {
      var canvas = document.createElement("canvas");
      canvas.width = Math.ceil(width * s);
      canvas.height = Math.ceil(height * s);
      var ctx = canvas.getContext("2d");
      ctx.fillStyle = "#ffffff";
      ctx.fillRect(0, 0, canvas.width, canvas.height);
      ctx.setTransform(s, 0, 0, s, 0, 0);
      ctx.drawImage(img, 0, 0);
      URL.revokeObjectURL(url);
      canvas.toBlob(function (blob) {
        if (!blob) {
          resolve(false);
          return;
        }
        var a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = filename || "gantt.png";
        document.body.appendChild(a);
        a.click();
        a.remove();
        resolve(true);
      }, "image/png");
    };
    img.onerror = function () {
      URL.revokeObjectURL(url);
      resolve(false);
    };
    img.src = url;
  });
};

window.axioplan.downloadPngFromSvgById = function (elementId, filename) {
  var svg = document.getElementById(elementId);
  if (!svg) return Promise.resolve(false);
  var clone = svg.cloneNode(true);
  if (!clone.getAttribute("xmlns")) {
    clone.setAttribute("xmlns", "http://www.w3.org/2000/svg");
  }
  var xml = new XMLSerializer().serializeToString(clone);
  return window.axioplan.downloadPngFromSvgMarkup(xml, filename, 2);
};

window.axioplan.escapeHtml = function (value) {
  return String(value == null ? "" : value)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
};

window.axioplan.printGanttPdfDocument = function (title, generatedAt, summaryHtml, svgMarkup, suggestedName) {
  var safeTitle = window.axioplan.escapeHtml(title || "Planning APS — Gantt");
  var safeMeta = window.axioplan.escapeHtml(generatedAt || "");
  var safeName = suggestedName ? window.axioplan.escapeHtml(suggestedName) : "";
  var svg = svgMarkup || "";
  if (svg && svg.indexOf("xmlns=") < 0) {
    svg = svg.replace("<svg", '<svg xmlns="http://www.w3.org/2000/svg"');
  }
  // CDATA n'est pas valide dans du SVG embarqué en HTML → page blanche.
  svg = svg.replace(/<!\[CDATA\[/g, "").replace(/\]\]>/g, "");
  // Évite collision d'ids marker/clipPath si plusieurs SVG (préfixe unique).
  var prefix = "p" + Date.now().toString(36);
  svg = svg.replace(/\bid=\"arrow\"/g, "id=\"" + prefix + "-arrow\"")
           .replace(/url\(#arrow\)/g, "url(#" + prefix + "-arrow)");

  var styles = [
    "@page { size: A4 landscape; margin: 10mm; }",
    "html,body{background:#fff;color:#1f2933;}",
    "body{font-family:'Segoe UI',Tahoma,sans-serif;padding:12px;margin:0;}",
    "h1{font-size:1.2rem;margin:0 0 0.25rem}",
    ".meta{color:#64748b;font-size:0.85rem;margin-bottom:12px}",
    ".kpis{display:flex;flex-wrap:wrap;gap:8px;margin:10px 0 14px}",
    ".kpi{border:1px solid #d9e2ec;border-radius:8px;padding:8px 12px;min-width:140px;background:#f8fafc}",
    ".kpi span{display:block;font-size:0.75rem;color:#64748b}",
    ".kpi strong{display:block;font-size:1rem;margin-top:2px}",
    ".gantt-critical-path{margin:10px 0;padding:8px 10px;border-left:3px solid #9a3412;background:#fff7ed}",
    ".aps-gantt-legend{display:flex;flex-wrap:wrap;gap:12px;margin:12px 0;color:#475569;font-size:0.85rem}",
    ".gantt-print-table{width:100%;border-collapse:collapse;margin:12px 0;font-size:11px}",
    ".gantt-print-table th,.gantt-print-table td{border:1px solid #d9e2ec;padding:4px 6px;text-align:left}",
    ".gantt-print-table th{background:#f8fafc}",
    ".gantt-print-chart{overflow:auto;margin-top:8px;border:1px solid #e2e8f0;padding:8px;background:#fff}",
    ".gantt-print-chart svg{display:block;max-width:none;height:auto}",
    ".no-print{margin-top:1rem;color:#52606d}",
    "@media print{",
    "  body{padding:0}",
    "  .no-print{display:none !important}",
    "  .gantt-print-chart{border:none;padding:0;overflow:visible}",
    "  .gantt-print-chart,.gantt-print-table,.kpi,.gantt-critical-path{break-inside:avoid}",
    "}"
  ].join("\n");

  var html = [
    "<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"utf-8\"/>",
    "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>",
    "<title>", safeTitle, "</title>",
    "<style>", styles, "</style></head><body class=\"gantt-print-container\">",
    "<header class=\"gantt-header\">",
    "<h1>", safeTitle, "</h1>",
    "<div class=\"meta\">Généré le ", safeMeta,
    safeName ? (" · Fichier suggéré : " + safeName) : "",
    "</div></header>",
    summaryHtml || "<p>Résumé indisponible.</p>",
    "<div class=\"gantt-print-chart gantt-grid\">",
    svg || "<p style=\"color:#b91c1c\">Diagramme SVG indisponible.</p>",
    "</div>",
    "<p class=\"no-print\">Utilisez Imprimer → Enregistrer au format PDF (orientation paysage).</p>",
    "<script>(function(){function go(){try{window.focus();window.print();}catch(e){}}",
    "if(document.readyState==='complete'){setTimeout(go,250);}else{",
    "window.addEventListener('load',function(){setTimeout(go,250);});",
    "setTimeout(go,800);}})();<\/script>",
    "</body></html>"
  ].join("");

  // Blob URL : évite about:blank + noopener (page blanche sans document accessible).
  var blob = new Blob([html], { type: "text/html;charset=utf-8" });
  var url = URL.createObjectURL(blob);
  var w = window.open(url, "_blank");
  if (!w) {
    URL.revokeObjectURL(url);
    // Fallback même onglet si pop-up bloquée
    var iframe = document.createElement("iframe");
    iframe.style.position = "fixed";
    iframe.style.right = "0";
    iframe.style.bottom = "0";
    iframe.style.width = "0";
    iframe.style.height = "0";
    iframe.style.border = "0";
    iframe.src = url;
    document.body.appendChild(iframe);
    iframe.onload = function () {
      try {
        iframe.contentWindow.focus();
        iframe.contentWindow.print();
      } catch (e) {
        alert("Autorisez les pop-ups pour l'export PDF, ou utilisez l'export SVG/PNG.");
      }
      setTimeout(function () {
        iframe.remove();
        URL.revokeObjectURL(url);
      }, 2000);
    };
    return;
  }

  setTimeout(function () { URL.revokeObjectURL(url); }, 60000);
};

// Back-compat
window.axioplan.printGanttPdf = function (elementId, title, summaryHtml) {
  var svg = document.getElementById(elementId);
  var svgHtml = svg ? new XMLSerializer().serializeToString(svg.cloneNode(true)) : "";
  window.axioplan.printGanttPdfDocument(title, new Date().toLocaleString(), summaryHtml, svgHtml, null);
};
