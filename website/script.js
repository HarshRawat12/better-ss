(() => {
  'use strict';
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  let paused = reduced.matches;
  const motionButton = document.querySelector('.motion-toggle');
  const setMotion = value => {
    paused = value;
    document.documentElement.classList.toggle('motion-paused', value);
    motionButton.setAttribute('aria-pressed', String(value));
    motionButton.setAttribute('aria-label', value ? 'Resume decorative animations' : 'Pause decorative animations');
    motionButton.title = value ? 'Resume animations' : 'Pause animations';
    motionButton.innerHTML = `${value ? '▷' : 'Ⅱ'} <span>Motion</span>`;
    if (value) document.querySelector('.workspace-window').style.transform = '';
  };
  motionButton.addEventListener('click', () => setMotion(!paused));
  reduced.addEventListener('change', e => setMotion(e.matches));
  setMotion(paused);
  if ('IntersectionObserver' in window) {
    document.documentElement.classList.add('js-motion');
    const observer = new IntersectionObserver(entries => entries.forEach(entry => {
      if (entry.isIntersecting) { entry.target.classList.add('visible'); observer.unobserve(entry.target); }
    }), { threshold: 0.08, rootMargin: '0px 0px 20px 0px' });
    document.querySelectorAll('.reveal').forEach(el => observer.observe(el));
  }
  const menu = document.querySelector('.menu-toggle');
  const nav = document.querySelector('.site-header nav');
  const closeMenu = () => { menu.setAttribute('aria-expanded', 'false'); menu.setAttribute('aria-label', 'Open menu'); nav.classList.remove('is-open'); };
  menu.addEventListener('click', () => {
    const open = menu.getAttribute('aria-expanded') !== 'true';
    menu.setAttribute('aria-expanded', String(open)); menu.setAttribute('aria-label', open ? 'Close menu' : 'Open menu'); nav.classList.toggle('is-open', open);
  });
  nav.querySelectorAll('a').forEach(a => a.addEventListener('click', closeMenu));
  document.addEventListener('keydown', e => { if (e.key === 'Escape' && menu.getAttribute('aria-expanded') === 'true') { closeMenu(); menu.focus(); } });
  const steps = {
    capture: { kicker: 'JUST THE PART YOU NEED', title: 'See it. Save it.', copy: 'A small detail, a whole window, or every screen. Choose what you need and your screenshot is ready to use.', details: ['Capture an area, window, or display', 'Copied and ready to paste', 'A floating preview within reach'], next: 'Next: make it clear' },
    annotate: { kicker: 'A LITTLE CONTEXT GOES A LONG WAY', title: 'Make your point.', copy: 'Circle the detail. Add an arrow. Highlight what matters. Make the screenshot say exactly what you mean.', details: ['Pen, highlighter, arrows, and shapes', 'Crop, blur, mosaic, and quick filters', 'Undo and redo as you go'], next: 'Next: get the words' },
    text: { kicker: 'YOUR TIME IS TOO GOOD FOR RETYPING', title: 'Picture it. Copy it.', copy: 'The words in your screenshot can become the words in your next document. Extract the text, make any edits, and copy it over.', details: ['Extract words from your screenshot', 'Review and edit before copying', 'Text recognition on your own computer'], next: 'Next: put it to work' },
    export: { kicker: 'ON TO THE NEXT GOOD THING', title: 'Ready when you are.', copy: 'Paste into your draft, drag to an app that accepts images, or save in the format your work needs. Your next step is right there.', details: ['Drag from the floating preview', 'Save images or a PDF', 'Choose your size, quality, and folder'], next: 'Back to the first capture' }
  };
  const tabs = [...document.querySelectorAll('[role="tab"]')];
  const panel = document.getElementById('tour-panel');
  const demo = document.querySelector('.demo-visual');
  const next = document.getElementById('next-step');
  let active = 0;
  function selectStep(index, focus = false) {
    active = index;
    const tab = tabs[index], key = tab.dataset.step, step = steps[key];
    tabs.forEach(t => { t.setAttribute('aria-selected', String(t === tab)); t.tabIndex = t === tab ? 0 : -1; });
    panel.setAttribute('aria-labelledby', tab.id);
    document.querySelector('.tour-kicker').textContent = step.kicker;
    document.getElementById('tour-title').textContent = step.title;
    document.getElementById('tour-copy').textContent = step.copy;
    document.getElementById('tour-details').replaceChildren(...step.details.map(text => { const li = document.createElement('li'); li.textContent = text; return li; }));
    next.innerHTML = `${step.next} <svg aria-hidden="true"><use href="#i-arrow"/></svg>`;
    demo.dataset.mode = key;
    document.querySelector('.extracted-card').inert = key !== 'text';
    document.querySelector('.export-card').inert = key !== 'export';
    document.querySelector('.extracted-card').setAttribute('aria-hidden', String(key !== 'text'));
    document.querySelector('.export-card').setAttribute('aria-hidden', String(key !== 'export'));
    if (focus) tab.focus();
  }
  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => selectStep(index));
    tab.addEventListener('keydown', e => {
      let target;
      if (e.key === 'ArrowRight') target = (index + 1) % tabs.length;
      if (e.key === 'ArrowLeft') target = (index + tabs.length - 1) % tabs.length;
      if (e.key === 'Home') target = 0;
      if (e.key === 'End') target = tabs.length - 1;
      if (target !== undefined) { e.preventDefault(); selectStep(target, true); }
    });
  });
  next.addEventListener('click', () => selectStep((active + 1) % tabs.length));
  selectStep(0);
  document.getElementById('copy-demo').addEventListener('click', async () => {
    const status = document.querySelector('.copy-status');
    const text = document.querySelector('.extracted-card > p').textContent;
    try {
      if (!navigator.clipboard) throw new Error('Clipboard not available');
      await navigator.clipboard.writeText(text);
      status.textContent = 'Copied. Ready to paste into your draft.';
    } catch {
      const selection = window.getSelection(); const range = document.createRange();
      range.selectNodeContents(document.querySelector('.extracted-card > p')); selection.removeAllRanges(); selection.addRange(range);
      status.textContent = 'Text selected. Use your device’s Copy command.';
    }
  });
  document.querySelectorAll('.format-options button').forEach(button => button.addEventListener('click', () => {
    document.querySelectorAll('.format-options button').forEach(b => b.setAttribute('aria-pressed', String(b === button)));
    document.querySelector('.export-filename').textContent = `Project-notes.${button.textContent.toLowerCase()}`;
  }));
  const stage = document.querySelector('.hero-stage');
  const product = document.querySelector('.workspace-window');
  if (window.matchMedia('(hover: hover) and (min-width: 1000px)').matches) {
    let frame;
    stage.addEventListener('pointermove', e => {
      if (paused) return;
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        const rect = stage.getBoundingClientRect();
        const x = (e.clientX - rect.left) / rect.width - .5;
        const y = (e.clientY - rect.top) / rect.height - .5;
        product.style.transform = `rotateX(${5 - y * 4}deg) rotateY(${x * 3}deg)`;
      });
    });
    stage.addEventListener('pointerleave', () => { cancelAnimationFrame(frame); product.style.transform = ''; });
  }
})();

