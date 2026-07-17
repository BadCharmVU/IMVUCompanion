const fs = require('fs');
const c = fs.readFileSync(__dirname + '/welcome.min.js', 'utf8');

// Find __onSendInput full context
let i = c.indexOf('__onSendInput');
console.log('__onSendInput', i);
console.log(c.substring(i - 200, i + 500));

// Find connect to chat bar with send
const needles = [
  'listenTo(this.__chatBar',
  'listenTo(r,"sendInput"',
  'listenTo(e,"sendInput"',
  ',"sendInput"',
  "on('sendInput'",
  'on("sendInput"',
  'ChatBar,',
  'chatBarFactory',
  'chat_bar',
  'ChatInput',
  'isWhisper:',
  'isWhisper=',
  'is_whisper',
  'whisperMode',
  'inWhisperMode()',
];
for (const p of needles) {
  let idx = 0, n = 0;
  while ((idx = c.indexOf(p, idx)) >= 0 && n < 4) {
    console.log('\n###', p, idx);
    console.log(c.substring(Math.max(0, idx - 100), Math.min(c.length, idx + 280)));
    idx += p.length;
    n++;
  }
}

// Search for payload building with whisper
const re = /isWhisper/g;
let m, count = 0;
while ((m = re.exec(c)) && count < 15) {
  console.log('\nISW', m.index, c.substring(m.index - 100, m.index + 200));
  count++;
}
