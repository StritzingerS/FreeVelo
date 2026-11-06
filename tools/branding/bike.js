// Shared bike drawing (viewBox 0 0 100 100), white strokes.
module.exports.bike = (color = '#fff') => `
<g fill="none" stroke="${color}" stroke-linecap="round" stroke-linejoin="round">
  <circle cx="27" cy="63" r="15" stroke-width="5"/>
  <circle cx="73" cy="63" r="15" stroke-width="5"/>
  <path d="M27 63 L47 63 L39.5 37 M27 63 L39.5 37 L66 38 M47 63 L67.5 44 M66 38 L73 63" stroke-width="4.6"/>
  <path d="M33.5 33.5 L45.5 33.5" stroke-width="5"/>
  <path d="M39.5 37 L39 33.5" stroke-width="4"/>
  <path d="M66 38 L65 32 L72 31 Q77 31 76.5 36 Q76 39 73 39.5" stroke-width="4"/>
  <circle cx="47" cy="63" r="3.2" fill="${color}" stroke="none"/>
</g>
<g stroke="${color}" stroke-linecap="round" stroke-width="3.2" opacity="0.55">
  <path d="M4 44 L14 44"/><path d="M1 53 L10 53"/><path d="M6 35 L13 35"/>
</g>`;
