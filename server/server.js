// Lightweight, dependency-free leaderboard API. Deploy this folder to any Node.js host.
const http = require('http');
const fs = require('fs');
const path = require('path');

const port = Number(process.env.PORT || 8787);
const scoreFile = path.join(__dirname, 'data', 'scores.json');
const recentPosts = new Map();

function readScores() {
  try { return JSON.parse(fs.readFileSync(scoreFile, 'utf8')); }
  catch { return []; }
}
function saveScores(scores) {
  fs.mkdirSync(path.dirname(scoreFile), { recursive: true });
  fs.writeFileSync(scoreFile, JSON.stringify(scores, null, 2));
}
function send(res, status, body) {
  res.writeHead(status, { 'Content-Type': 'application/json', 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Methods': 'GET, POST, OPTIONS' });
  res.end(JSON.stringify(body));
}
function topScores() {
  return readScores().sort((a, b) => b.score - a.score || a.createdAt.localeCompare(b.createdAt)).slice(0, 20);
}

http.createServer((req, res) => {
  if (req.method === 'OPTIONS') return send(res, 204, {});
  if (req.method === 'GET' && req.url === '/api/leaderboard') return send(res, 200, { entries: topScores() });
  if (req.method !== 'POST' || req.url !== '/api/scores') return send(res, 404, { error: 'Not found' });

  const ip = req.socket.remoteAddress || 'unknown';
  if (Date.now() - (recentPosts.get(ip) || 0) < 3000) return send(res, 429, { error: 'Please wait before submitting again.' });
  let raw = '';
  req.on('data', chunk => { raw += chunk; if (raw.length > 4096) req.destroy(); });
  req.on('end', () => {
    try {
      const value = JSON.parse(raw);
      const name = String(value.name || '').trim().replace(/\s+/g, ' ');
      const score = Number(value.score);
      if (!/^[A-Za-z0-9 _-]{2,16}$/.test(name)) return send(res, 400, { error: 'Name must be 2-16 letters, numbers, spaces, _ or -.' });
      if (!Number.isInteger(score) || score < 1 || score > 10000000) return send(res, 400, { error: 'Invalid score.' });
      const scores = readScores();
      scores.push({ name, score, createdAt: new Date().toISOString() });
      saveScores(scores); recentPosts.set(ip, Date.now());
      return send(res, 201, { entries: topScores() });
    } catch { return send(res, 400, { error: 'Invalid JSON.' }); }
  });
}).listen(port, () => console.log(`Tetra leaderboard listening on :${port}`));
