// Refined road bike, viewBox 0 0 100 100, centred on (50, 50): 700c wheels, ~73° angles, drop bar.
module.exports.bike = (c = '#fff', motion = false) => {
  const R = 15.5, ry = 59, rx = 25.5, fx = 74.5;
  const bb = [46.5, 60.5], seat = [40.8, 39.2], headTop = [66.8, 37.4], headBot = [68.4, 43.6];
  const p = a => a.join(' ');
  return `
<g fill="none" stroke="${c}" stroke-linecap="round" stroke-linejoin="round">
  <circle cx="${rx}" cy="${ry}" r="${R}" stroke-width="3.3"/>
  <circle cx="${fx}" cy="${ry}" r="${R}" stroke-width="3.3"/>
  <path stroke-width="3.7" d="M${rx} ${ry} L${p(bb)} L${p(seat)} Z M${p(seat)} L${p(headTop)} L${p(headBot)} L${p(bb)}"/>
  <path stroke-width="3.5" d="M${p(headBot)} Q${headBot[0] + 1.2} ${headBot[1] + 9} ${fx} ${ry}"/>
  <path stroke-width="3.2" d="M${p(seat)} L39.8 34.6"/>
  <path stroke-width="3.6" d="M34.6 34.4 L45 34.4"/>
  <path stroke-width="3.2" d="M${p(headTop)} L71.8 34.8 C76.8 34.2 77.4 39.6 73 39.6"/>
</g>
<circle cx="${bb[0]}" cy="${bb[1]}" r="3.4" fill="${c}"/>
${motion ? `<g stroke="${c}" stroke-linecap="round"><path d="M3 45 L11 45" stroke-width="3" opacity="0.5"/><path d="M1 53 L7.5 53" stroke-width="3" opacity="0.32"/></g>` : ''}`;
};
