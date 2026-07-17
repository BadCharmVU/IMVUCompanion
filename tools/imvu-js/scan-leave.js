const fs = require('fs');
const c = fs.readFileSync(__dirname + '/welcome.min.js', 'utf8');
const pats = [
  'leave_room', 'Leave Room', 'leave-room', 'exit-room', 'exit_chat',
  'close_room', 'room_exit', 'btn-leave', 'leaveRoom', 'secondsInRoom',
  'leaveHangout', 'exitHangout', 'disconnectRoom', 'doLeaveRoom',
  'leaveChatRoom', 'exitFromRoom', 'navigateOutOfRoom'
];
for (const p of pats) {
  let i = 0, n = 0;
  while ((i = c.indexOf(p, i)) >= 0 && n < 3) {
    console.log('###', p, i);
    console.log(c.substring(Math.max(0, i - 80), Math.min(c.length, i + 220)));
    console.log('');
    i += p.length;
    n++;
  }
}
