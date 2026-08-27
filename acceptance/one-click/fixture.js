'use strict';

const http = require('http');
const querystring = require('querystring');

const port = Number.parseInt(process.env.ONE_CLICK_FIXTURE_PORT || '18080', 10);
const placements = new Map();
const requests = new Map();

function escapeHtml(value) {
  return String(value || '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function record(pathname) {
  requests.set(pathname, (requests.get(pathname) || 0) + 1);
}

function form(scripted) {
  const markup = `<form id="commentform" method="post" action="/wp-comments-post.php">
    <input name="author" required><input name="email" required><input name="url">
    <textarea name="comment" required></textarea>
    <input type="hidden" name="comment_post_ID" value="77">
    <input type="hidden" name="comment_parent" value="0">
    <button type="submit">Post comment</button></form>`;
  if (!scripted) return markup;
  return `<div id="comment-root"></div><script>
    document.getElementById('comment-root').innerHTML = ${JSON.stringify(markup)};
  </script>`;
}

function page(kind) {
  const placed = placements.get(kind);
  const anchor = placed ? `<a class="accepted-backlink" href="${escapeHtml(placed)}">Accepted target</a>` : '';
  if (kind === 'closed') return '<meta name="generator" content="WordPress"><main class="comments-closed">Comments are closed.</main>';
  if (kind === 'fallback') return `<meta name="generator" content="WordPress"><main class="postid-77"><div class="wp-content">Fallback post</div>${anchor}</main>`;
  if (kind === 'oversized-js') {
    const boundedLargeMarkup = placed ? '' : `<div>${'x'.repeat(2200000)}</div>`;
    return `<meta name="generator" content="WordPress"><link rel="stylesheet" href="/wp-content/theme.css"><main>${anchor}${form(true)}${boundedLargeMarkup}</main>`;
  }
  return `<meta name="generator" content="WordPress"><link rel="stylesheet" href="/wp-content/theme.css"><main>${anchor}${form(false)}</main>`;
}

function kindFromReferer(value) {
  try {
    return new URL(value).pathname.split('/').filter(Boolean)[0] || 'standard';
  } catch {
    return 'standard';
  }
}

const server = http.createServer((request, response) => {
  const url = new URL(request.url, `http://${request.headers.host || 'one-click.fixture'}`);
  record(url.pathname);
  if (request.method === 'GET' && url.pathname === '/__health') {
    response.writeHead(200, { 'Content-Type': 'text/plain' });
    response.end('ready');
    return;
  }
  if (request.method === 'GET' && url.pathname === '/__stats') {
    response.writeHead(200, { 'Content-Type': 'application/json' });
    response.end(JSON.stringify({ placements: Object.fromEntries(placements), requests: Object.fromEntries(requests) }));
    return;
  }
  if (request.method === 'GET' && url.pathname === '/auth') {
    response.writeHead(401, { 'Content-Type': 'text/plain' });
    response.end('Authentication required.');
    return;
  }
  if (request.method === 'GET' && url.pathname === '/forbidden') {
    response.writeHead(403, { 'Content-Type': 'text/plain' });
    response.end('Rejected.');
    return;
  }
  if (request.method === 'GET') {
    const kind = url.pathname.split('/').filter(Boolean)[0] || 'standard';
    const body = page(kind);
    response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Content-Length': Buffer.byteLength(body) });
    response.end(body);
    return;
  }
  if (request.method !== 'POST' || url.pathname !== '/wp-comments-post.php') {
    response.writeHead(404, { 'Content-Type': 'text/plain' });
    response.end('Not found.');
    return;
  }

  let body = '';
  request.setEncoding('utf8');
  request.on('data', chunk => {
    body += chunk;
    if (body.length > 65536) request.destroy();
  });
  request.on('end', () => {
    const values = querystring.parse(body);
    const kind = kindFromReferer(request.headers.referer);
    if (values.comment_post_ID !== '77') {
      response.writeHead(400, { 'Content-Type': 'text/plain' });
      response.end('Invalid post identifier.');
      return;
    }
    if (kind === 'moderated') {
      response.writeHead(302, { Location: '/moderated?unapproved=77#comment-77' });
      response.end('Awaiting moderation.');
      return;
    }
    placements.set(kind, values.url || '');
    response.writeHead(302, { Location: `/${kind}?published=1#comment-77` });
    response.end('Published.');
  });
});

server.listen(port, '0.0.0.0', () => process.stderr.write(`one-click fixture ready on ${port}\n`));

function stop() {
  server.close(() => process.exit(0));
  setTimeout(() => process.exit(1), 5000).unref();
}
process.on('SIGTERM', stop);
process.on('SIGINT', stop);
