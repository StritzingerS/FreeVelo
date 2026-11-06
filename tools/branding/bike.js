// Aero road bike silhouette (dropped seat stays, sloping top tube, aero seat post, integrated bar, deep rims),
// traced in the pixel coordinates of a 2000 × 1294 side-view photo, no brand markings.
const P = {
  rear: [618, 800], front: [1390, 800], R: 254,
  bb: [928, 850], seat: [834, 484], post: [786, 340],
  headTop: [1262, 478], headBot: [1298, 560], stay: [832, 580],
};
const p = a => a.join(' ');
module.exports.P = P;
module.exports.bikePx = (c = '#fff') => `
<g fill="none" stroke="${c}" stroke-linecap="round" stroke-linejoin="round">
  <circle cx="${P.rear[0]}" cy="${P.rear[1]}" r="${P.R - 28}" stroke-width="56"/>
  <circle cx="${P.front[0]}" cy="${P.front[1]}" r="${P.R - 28}" stroke-width="56"/>
  <path stroke-width="34" d="M${p(P.rear)} L${p(P.bb)} M${p(P.rear)} L${p(P.stay)}"/>
  <path stroke-width="50" d="M${p(P.bb)} L${p(P.seat)}"/>
  <path stroke-width="56" d="M${p(P.bb)} L${p(P.headBot)}"/>
  <path stroke-width="46" d="M${p(P.seat)} L${p(P.headTop)}"/>
  <path stroke-width="56" d="M${p(P.headTop)} L${p(P.headBot)}"/>
  <path stroke-width="42" d="M${p(P.headBot)} L${p(P.front)}"/>
  <path stroke-width="40" d="M${p(P.seat)} L${p(P.post)}"/>
  <path stroke-width="34" d="M684 318 Q770 332 862 318"/>
  <path stroke-width="32" d="M${p(P.headTop)} L1292 410 L1400 404 C1452 406 1458 480 1412 506"/>
</g>
<circle cx="${P.bb[0]}" cy="${P.bb[1]}" r="60" fill="${c}"/>`;

// For the icons: mapped into a 100 × 100 box, 90 wide, centred on (50, 54) (render.js moves it up by 4).
module.exports.bike = (c = '#fff') => `<g transform="translate(50 54) scale(${90 / 1278}) translate(-1005 -688)">${module.exports.bikePx(c)}</g>`;
