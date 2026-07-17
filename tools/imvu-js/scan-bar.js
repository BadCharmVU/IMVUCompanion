const fs = require('fs');
const c = fs.readFileSync(__dirname + '/welcome.min.js', 'utf8');

for (const p of [
  'ExpandingChatBar',
  'expanding_chat_bar',
  'addFooter',
  'sendInput',
  'onChatInput',
  'handleChatInput',
  'chatInput',
  '__sendMessageTo',
  'sendToChat',
  'postMessageToRoom',
  'messagePayload',
  'buildMessage',
  'createMessage',
  'isWhisper',
  'whisperTarget',
  'messageTarget',
]) {
  let count = 0, i = 0;
  while ((i = c.indexOf(p, i)) >= 0) { count++; i += p.length; }
  console.log(p, count);
}

// ExpandingChatBar usages
let i = 0, n = 0;
while ((i = c.indexOf('ExpandingChatBar', i)) >= 0 && n < 12) {
  console.log('\n### ExpandingChatBar', i);
  console.log(c.substring(Math.max(0, i - 80), Math.min(c.length, i + 220)));
  i += 10; n++;
}

// Look for parent listening to sendInput near chat scene
i = 0; n = 0;
while ((i = c.indexOf('sendInput', i)) >= 0 && n < 20) {
  const slice = c.substring(Math.max(0, i - 150), Math.min(c.length, i + 200));
  console.log('\nSENDINPUT', i, slice.replace(/\n/g, ' '));
  i += 9; n++;
}

// search patterns like .on("send
const re = /\.on\([\"']send[^\"']*[\"']/g;
let m;
while ((m = re.exec(c)) && n < 30) {
  console.log('ON', m.index, c.substring(m.index, m.index + 80));
  n++;
}
