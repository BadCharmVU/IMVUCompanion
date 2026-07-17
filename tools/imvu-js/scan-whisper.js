const fs = require('fs');
const c = fs.readFileSync(__dirname + '/welcome.min.js', 'utf8');

function dump(p, n = 3, before = 80, after = 250) {
  let i = 0, k = 0;
  while ((i = c.indexOf(p, i)) >= 0 && k < n) {
    console.log('###', p, '@', i);
    console.log(c.substring(Math.max(0, i - before), Math.min(c.length, i + after)));
    console.log('');
    i += p.length;
    k++;
  }
}

const pats = [
  'onSendInput',
  '__onSendInput',
  'extend("ChatBar"',
  'uiContextName:"chat_bar',
  'isWhisper',
  'outgoingWhisper',
  'inWhisperMode',
  'resetMessageTarget',
  'handleWhisper',
  'sendMessage:function',
  '.sendMessage(',
  'whisper_to',
  'message_target',
  'recipient_cid',
  'private_message',
];
for (const p of pats) {
  let count = 0, i = 0;
  while ((i = c.indexOf(p, i)) >= 0) { count++; i += p.length; }
  if (count) console.log('COUNT', p, count);
}

dump('__send:function(e){if(!this.hasInput())', 1, 600, 100);
dump('inWhisperMode', 5, 40, 200);
dump('isWhisper', 8, 60, 180);
dump('sendMessage(', 15, 80, 150);
