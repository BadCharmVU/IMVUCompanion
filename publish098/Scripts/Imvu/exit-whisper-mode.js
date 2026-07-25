const doc = __imvuFindChatRoot().doc;
const sel = '{{CHAT_INPUT_SEL}}';
function clickEl(el) {
    if (!el) return false;
    try { el.scrollIntoView({ block: 'nearest' }); } catch(e) {}
    const evt = new MouseEvent('click', { bubbles: true, cancelable: true, view: window });
    el.dispatchEvent(evt);
    if (typeof el.click === 'function') el.click();
    return true;
}
const whisperClose = doc.querySelectorAll('.whisper-close, span.whisper-close, [class*="whisper-close"]');
for (const el of whisperClose) { if (clickEl(el)) return 'closed'; }
const panels = doc.querySelectorAll('[class*="whisper-compose"], [class*="whisper-target"], [class*="whisper-mode"], [class*="whisper-bar"], [class*="whisper-panel"]');
for (const bar of panels) {
    const btn = bar.querySelector('.whisper-close, [class*="whisper-close"], [class*="close"], [class*="icon-close"], button');
    if (clickEl(btn)) return 'closed';
}
const closers = doc.querySelectorAll('[class*="close-whisper"], [class*="cancel-whisper"], [class*="whisper-cancel"]');
for (const c of closers) { if (clickEl(c)) return 'closed'; }
const inpRef = doc.querySelector(sel);
if (inpRef) {
    inpRef.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, bubbles: true }));
    inpRef.blur();
}
const still = doc.querySelector('.whisper-close, span.whisper-close, [class*="whisper-compose"], [class*="whisper-target"]');
return still ? 'still-open' : 'closed';
