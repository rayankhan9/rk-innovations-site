(function () {
  const $ = (id) => document.getElementById(id);
  const latEl = $('lat'), lngEl = $('lng'), polyEl = $('poly'), radiusEl = $('radius');
  const startLat = parseFloat(latEl.value) || 20.5937, startLng = parseFloat(lngEl.value) || 78.9629;
  const hasPoint = !!(parseFloat(latEl.value) && parseFloat(lngEl.value) && !(Math.abs(startLat - 20.5937) < 1e-6 && Math.abs(startLng - 78.9629) < 1e-6));

  const map = L.map('map').setView([startLat, startLng], hasPoint ? 17 : 5);
  L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '&copy; OpenStreetMap contributors' }).addTo(map);

  let marker = null, circle = null, polygon = null, points = [];
  try { points = JSON.parse(polyEl.value || '[]'); } catch { points = []; }

  const shape = () => document.querySelector('input[name=Shape]:checked').value;

  function drawCircle() {
    const ll = [parseFloat(latEl.value), parseFloat(lngEl.value)];
    if (isNaN(ll[0]) || isNaN(ll[1])) return;
    if (!marker) { marker = L.marker(ll, { draggable: true }).addTo(map); marker.on('dragend', () => setCenter(marker.getLatLng())); }
    marker.setLatLng(ll);
    if (!circle) circle = L.circle(ll, { radius: +radiusEl.value, color: '#0f766e', fillOpacity: .15 }).addTo(map);
    circle.setLatLng(ll).setRadius(+radiusEl.value);
  }
  function drawPolygon() {
    if (polygon) { map.removeLayer(polygon); polygon = null; }
    if (points.length) polygon = (points.length >= 3 ? L.polygon : L.polyline)(points, { color: '#0f766e', fillOpacity: .15 }).addTo(map);
    polyEl.value = JSON.stringify(points);
    if (points.length >= 3) {
      latEl.value = (points.reduce((a, p) => a + p[0], 0) / points.length).toFixed(7);
      lngEl.value = (points.reduce((a, p) => a + p[1], 0) / points.length).toFixed(7);
    }
  }
  function setCenter(ll) { latEl.value = ll.lat.toFixed(7); lngEl.value = ll.lng.toFixed(7); drawCircle(); }

  function applyMode() {
    const c = shape() === 'Circle';
    $('circleOpts').classList.toggle('d-none', !c);
    $('polyOpts').classList.toggle('d-none', c);
    if (circle) c ? circle.addTo(map) : map.removeLayer(circle);
    if (marker) c ? marker.addTo(map) : map.removeLayer(marker);
    if (polygon) c ? map.removeLayer(polygon) : polygon.addTo(map);
    c ? drawCircle() : drawPolygon();
  }

  map.on('click', (e) => {
    if (shape() === 'Circle') setCenter(e.latlng);
    else { points.push([+e.latlng.lat.toFixed(7), +e.latlng.lng.toFixed(7)]); drawPolygon(); }
  });
  radiusEl.addEventListener('input', () => { $('rv').textContent = radiusEl.value; drawCircle(); });
  document.querySelectorAll('input[name=Shape]').forEach((r) => r.addEventListener('change', applyMode));
  $('undo').onclick = () => { points.pop(); drawPolygon(); };
  $('clear').onclick = () => { points = []; drawPolygon(); };

  $('here').onclick = () => navigator.geolocation && navigator.geolocation.getCurrentPosition((p) => {
    const ll = L.latLng(p.coords.latitude, p.coords.longitude);
    map.setView(ll, 18);
    if (shape() === 'Circle') setCenter(ll);
  }, () => alert('Could not get your location. Allow location access in the browser.'));

  async function search() {
    const q = $('q').value.trim(); if (!q) return;
    try {
      const r = await fetch('https://nominatim.openstreetmap.org/search?format=json&limit=1&q=' + encodeURIComponent(q));
      const j = await r.json();
      if (!j.length) return alert('No result found.');
      const ll = L.latLng(+j[0].lat, +j[0].lon);
      map.setView(ll, 17);
      if (shape() === 'Circle') setCenter(ll);
    } catch { alert('Search is unavailable right now. Click on the map instead.'); }
  }
  $('go').onclick = search;
  $('q').addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); search(); } });

  if (hasPoint || shape() === 'Polygon') applyMode(); else { $('circleOpts').classList.remove('d-none'); }
  if (shape() === 'Polygon' && points.length) map.fitBounds(L.latLngBounds(points));
  setTimeout(() => map.invalidateSize(), 200);
})();
