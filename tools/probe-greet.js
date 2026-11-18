(async () => {
  const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
  const cards = document.querySelectorAll('.job-card');
  if (!cards.length) return { err: 'no cards', url: location.href };
  cards[0].scrollIntoView({ block: 'center' });
  cards[0].click();
  await sleep(2500);
  const b = document.querySelector('button.job-detail-summary__apply');
  if (!b) return { err: 'no apply btn', url: location.href };
  const btnText = b.textContent.trim();
  b.click();
  await sleep(3200);
  const d = document.querySelector('.deliver-greeting-modal');
  const out = {
    btnText,
    dlgPresent: !!d,
    dlgText: d ? d.innerText.replace(/\s+/g, ' ').slice(0, 400) : '',
    dlgHtml: d ? d.outerHTML.slice(0, 4000) : '',
    editables: [...document.querySelectorAll('textarea, [contenteditable="true"]')]
      .map((t) => ({ tag: t.tagName, cls: String(t.className).slice(0, 60), ph: t.getAttribute('placeholder'), val: String(t.value || t.innerText || '').slice(0, 150) }))
      .slice(0, 6),
    btns: d ? [...d.querySelectorAll('button, a, div[class*="btn"]')].map((x) => ({ t: (x.textContent || '').trim().slice(0, 16), cls: String(x.className).slice(0, 60) })).slice(0, 24) : [],
  };
  if (d) {
    const c = d.querySelector('.deliver-greeting-modal__close') || d.querySelector('.deliver-greeting-modal__btn--sec');
    if (c) c.click();
    out.cancelled = !!c;
  }
  return out;
})()
