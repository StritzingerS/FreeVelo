const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
// Renders the app icon / startup logo into the Unity project (needs Playwright with Chromium):
//   node tools/branding/render.js
const assets = path.resolve(__dirname, '../../unity/FreeVelo/Assets/FreeVelo');
const font = 'file://' + path.join(assets, 'Resources/Fonts/Inter-Bold.ttf');
const out = n => path.join(assets, 'Branding', n);
const { bike } = require(path.join(__dirname, 'bike.js'));
const grad = `<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#FF8A3D"/><stop offset="1" stop-color="#F04E1A"/></linearGradient></defs>`;
const svgs = {
  // Full icon (iOS / legacy Android / default): full-bleed gradient, bike centred.
  'icon.png': [1024, 1024, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="1024" height="1024">${grad}<rect width="100" height="100" fill="url(#g)"/><g transform="translate(50 50) scale(0.78) translate(-50 -50) translate(5 -4)">${bike()}</g></svg>`],
  // Android adaptive layers (432 px, only the inner ~66 % is always visible).
  'icon_adaptive_bg.png': [432, 432, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="432" height="432">${grad}<rect width="100" height="100" fill="url(#g)"/></svg>`],
  'icon_adaptive_fg.png': [432, 432, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="432" height="432"><g transform="translate(50 50) scale(0.56) translate(-50 -50) translate(5 -4)">${bike()}</g></svg>`],
  // Startup screen logo: badge + wordmark on transparent background.
  'splash_logo.png': [1200, 400, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 300 100" width="1200" height="400">${grad}
     <style>@font-face{font-family:Inter;src:url(${font});} text{font-family:Inter;font-weight:700}</style>
     <rect x="6" y="10" width="80" height="80" rx="20" fill="url(#g)"/>
     <g transform="translate(46 50) scale(0.66) translate(-50 -50) translate(5 -4)">${bike()}</g>
     <text x="102" y="65" font-size="44" fill="#FFFFFF" letter-spacing="-1">Free<tspan fill="#FF6A2B">Velo</tspan></text></svg>`],
};
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage();
  for (const [name, [w, h, svg]] of Object.entries(svgs)) {
    await p.setViewportSize({ width: w, height: h });
    await p.setContent(`<html><body style="margin:0;background:transparent">${svg}</body></html>`);
    await p.waitForTimeout(200);
    await p.screenshot({ path: out(name), omitBackground: true, clip: { x: 0, y: 0, width: w, height: h } });
  }
  // Preview sheet for chat
  const img = n => 'data:image/png;base64,' + fs.readFileSync(out(n)).toString('base64');
  await p.setViewportSize({ width: 1200, height: 560 });
  await p.setContent(`<html><body style="margin:0;background:#0C1015;display:flex;gap:40px;align-items:center;justify-content:center;height:560px;font:16px sans-serif;color:#9aa">
    <div style="text-align:center"><img src="${img('icon.png')}" width="180" style="border-radius:40px"><br>App icon</div>
    <div style="text-align:center"><div style="width:180px;height:180px;border-radius:50%;overflow:hidden;position:relative"><img src="${img('icon_adaptive_bg.png')}" width="270" style="position:absolute;left:-45px;top:-45px"><img src="${img('icon_adaptive_fg.png')}" width="270" style="position:absolute;left:-45px;top:-45px"></div><br>Android (round mask)</div>
    <div style="text-align:center"><div style="background:#0C1015;border:1px solid #333;border-radius:24px;width:300px;height:420px;display:flex;align-items:center;justify-content:center"><img src="${img('splash_logo.png')}" width="260"></div><br>Startup screen</div>
  </body></html>`);
  await p.waitForTimeout(200);
  await p.screenshot({ path: path.join(__dirname, 'preview.png') });
  await b.close();
})();
