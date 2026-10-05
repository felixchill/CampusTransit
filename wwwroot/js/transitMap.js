// Leaflet interop for the CampusTransit live tracking map.
// Loaded on demand from the Live Map page, so it must tolerate Leaflet arriving late.

let map = null;
let tileLayer = null;
let routeLayer = null;
let stopLayer = null;
let shuttleLayer = null;
const markers = new Map();

// OpenStreetMap's public tile servers need no API key or account.
const TILES = "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png";
const SUBDOMAINS = "abc";
const ATTRIBUTION =
    '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors';

async function waitForLeaflet(timeoutMs = 8000) {
    if (window.L) return window.L;

    const scriptUrl = new URL("lib/leaflet/leaflet.js", document.baseURI).href;
    await new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = scriptUrl;
        script.onload = resolve;
        script.onerror = () => reject(new Error(`Unable to load Leaflet from ${scriptUrl}`));
        document.head.appendChild(script);
    });

    const started = Date.now();
    while (Date.now() - started < timeoutMs) {
        await new Promise(r => setTimeout(r, 120));
        if (window.L) return window.L;
    }
    return null;
}

function shuttleIcon(update) {
    const idle = update.status !== "InService" ? " idle" : "";
    return window.L.divIcon({
        className: "",
        iconSize: [30, 30],
        iconAnchor: [15, 15],
        html: `<div class="shuttle-marker${idle}" style="--marker:${update.color}">${update.code.replace(/^CT-/, "")}</div>`
    });
}

/// Surfaces a visible note if the basemap cannot be reached, instead of a silent blank map.
function warnIfTilesFail(el) {
    let failures = 0;

    tileLayer.on("tileerror", function () {
        failures++;
        if (failures !== 4 || el.querySelector(".map-overlay-note")) {
            return;
        }

        const note = document.createElement("div");
        note.className = "map-overlay-note";
        note.innerHTML =
            '<div class="alert alert-warn">' +
            '<svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" ' +
            'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' +
            '<path d="M10.3 4.3 2.6 18a2 2 0 0 0 1.7 3h15.4a2 2 0 0 0 1.7-3L13.7 4.3a2 2 0 0 0-3.4 0z"/>' +
            '<path d="M12 9.5v4"/><path d="M12 17h.01"/></svg>' +
            '<span>Map tiles could not be downloaded. Vehicle positions are still updating in the fleet panel.</span>' +
            "</div>";

        el.appendChild(note);
    });
}

function stopIcon(color) {
    return window.L.divIcon({
        className: "",
        iconSize: [11, 11],
        iconAnchor: [5.5, 5.5],
        html: `<div class="stop-marker" style="--marker:${color}"></div>`
    });
}

export async function init(elementId) {
    const L = await waitForLeaflet();
    if (!L) return false;

    const el = document.getElementById(elementId);
    if (!el) return false;

    map = L.map(el, { zoomControl: false, attributionControl: true, preferCanvas: true });
    L.control.zoom({ position: "topright" }).addTo(map);
    L.control.scale({ position: "bottomright", imperial: false }).addTo(map);

    tileLayer = L.tileLayer(TILES, {
        attribution: ATTRIBUTION,
        maxZoom: 19,
        subdomains: SUBDOMAINS
    }).addTo(map);

    warnIfTilesFail(el);

    routeLayer = L.layerGroup().addTo(map);
    stopLayer = L.layerGroup().addTo(map);
    shuttleLayer = L.layerGroup().addTo(map);

    // University of Ghana, Legon - Balme Library area, until route bounds arrive.
    map.setView([5.65113, -0.18703], 16);
    return true;
}

export function renderRoutes(routes) {
    if (!map) return;
    routeLayer.clearLayers();
    stopLayer.clearLayers();

    const bounds = [];

    (routes || []).forEach(route => {
        const points = (route.stops || [])
            .slice()
            .sort((a, b) => a.sequence - b.sequence)
            .map(s => [s.latitude, s.longitude]);

        if (points.length > 1) {
            const loop = points.concat([points[0]]);
            window.L.polyline(loop, {
                color: route.colorHex,
                weight: 4,
                opacity: 0.65,
                lineCap: "round",
                lineJoin: "round"
            }).addTo(routeLayer).bindTooltip(route.name, { className: "transit-tip", sticky: true });
            bounds.push(...points);
        }

        (route.stops || []).forEach(stop => {
            window.L.marker([stop.latitude, stop.longitude], { icon: stopIcon(route.colorHex), keyboard: false })
                .addTo(stopLayer)
                .bindTooltip(`<strong>${stop.name}</strong><br/>${route.name}`, { className: "transit-tip", direction: "top" });
        });
    });

    if (bounds.length) {
        map.fitBounds(bounds, { padding: [48, 48], maxZoom: 17 });
    }
}

export function updateShuttles(updates) {
    if (!map) return;

    const seen = new Set();

    (updates || []).forEach(update => {
        seen.add(update.shuttleId);
        const point = [update.latitude, update.longitude];
        const label = `<strong>${update.code}</strong> &middot; ${update.routeName || "Unassigned"}<br/>` +
            `${update.occupancy}/${update.capacity} seats &middot; ${update.status}` +
            (update.nextStop ? `<br/>Next: ${update.nextStop} (${update.etaMinutes} min)` : "");

        let marker = markers.get(update.shuttleId);
        if (!marker) {
            marker = window.L.marker(point, { icon: shuttleIcon(update), riseOnHover: true, keyboard: false })
                .addTo(shuttleLayer)
                .bindTooltip(label, { className: "transit-tip", direction: "top", offset: [0, -12] });
            markers.set(update.shuttleId, marker);
        } else {
            marker.setLatLng(point);
            marker.setIcon(shuttleIcon(update));
            marker.setTooltipContent(label);
        }
    });

    markers.forEach((marker, id) => {
        if (!seen.has(id)) {
            shuttleLayer.removeLayer(marker);
            markers.delete(id);
        }
    });
}

export function focus(latitude, longitude, zoom) {
    if (map) map.flyTo([latitude, longitude], zoom || 18, { duration: 0.6 });
}

export function fit() {
    if (!map) return;
    const bounds = [];
    markers.forEach(m => bounds.push(m.getLatLng()));
    if (bounds.length) map.fitBounds(bounds, { padding: [64, 64], maxZoom: 17 });
}

export function destroy() {
    if (map) {
        map.remove();
    }
    map = null;
    markers.clear();
}
