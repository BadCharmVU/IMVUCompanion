const fs = require('fs');
const c = fs.readFileSync(__dirname + '/welcome.min.js', 'utf8');
let i = 0, n = 0;
while ((i = c.indexOf('recordLeaveRoom', i)) >= 0 && n < 15) {
  console.log('###', i);
  console.log(c.substring(Math.max(0, i - 150), Math.min(c.length, i + 100)));
  console.log('');
  i += 10;
  n++;
}
// leaveRoom method call sites
const re = /leaveRoom|LeaveRoom|__leave|exitRoom/g;
let m;
const seen = new Set();
while ((m = re.exec(c)) && seen.size < 40) {
  if (seen.has(m[0] + m.index)) continue;
  seen.add(m[0] + m.index);
  console.log(m[0], m.index, c.substring(Math.max(0, m.index - 40), m.index + 80).replace(/\n/g, ' '));
}
