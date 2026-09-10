namespace SquirrelBox.AspNetCore.Dashboard;

internal static class SquirrelBoxDashboardHtml
{
    public const string Login = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>SquirrelBox</title>
  <style>
    :root{--orange:#D95A1A;--orange2:#F47A22;--teal:#008C94;--teal2:#005C63;--ink:#062B33;--cream:#FFF7E8;--line:#4A1B12;--white:#FFFCF4}
    *{box-sizing:border-box} body{margin:0;background:var(--white);color:var(--ink);font:14px/1.45 ui-sans-serif,system-ui,-apple-system,Segoe UI,sans-serif;display:grid;min-height:100vh;place-items:center}
    main{width:min(420px,calc(100vw - 32px));background:var(--cream);border:1px solid rgba(74,27,18,.22);border-radius:8px;padding:24px;box-shadow:0 24px 80px rgba(6,43,51,.12)}
    h1{font-size:28px;margin:0 0 4px;font-weight:800;letter-spacing:0;color:var(--ink)} h1 span{color:var(--orange)}
    p{margin:0 0 20px;color:rgba(6,43,51,.72)} label{display:block;margin:14px 0 6px;font-weight:700}
    input{width:100%;height:42px;border:1px solid rgba(74,27,18,.28);border-radius:6px;background:#fff;color:var(--ink);padding:0 12px;font:inherit}
    button{width:100%;height:42px;border:0;border-radius:6px;background:var(--teal);color:#fff;font-weight:800;margin-top:18px;cursor:pointer}
    button:hover{background:var(--teal2)} .error{display:none;color:var(--orange);font-weight:700;margin-top:12px}
  </style>
</head>
<body>
  <main>
    <h1>Squirrel<span>Box</span></h1>
    <p>Dashboard access</p>
    <form id="login">
      <label>Username</label>
      <input name="username" autocomplete="username" required />
      <label>Password</label>
      <input name="password" type="password" autocomplete="current-password" required />
      <button>Sign in</button>
      <div class="error" id="error">Invalid credentials.</div>
    </form>
  </main>
  <script>
    const dashboardPath = location.pathname.replace(/\/$/, '');
    document.getElementById('login').addEventListener('submit', async event => {
      event.preventDefault();
      const body = Object.fromEntries(new FormData(event.currentTarget).entries());
      const response = await fetch(`${dashboardPath}/auth/login`, { method:'POST', headers:{'content-type':'application/json'}, body:JSON.stringify(body) });
      if (response.ok) location.reload();
      else document.getElementById('error').style.display = 'block';
    });
  </script>
</body>
</html>
""";

    public const string Dashboard = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>SquirrelBox Dashboard</title>
  <style>
    :root{--orange:#D95A1A;--orange2:#F47A22;--teal:#008C94;--teal2:#005C63;--ink:#062B33;--cream:#FFF7E8;--line:#4A1B12;--white:#FFFCF4;--muted:rgba(6,43,51,.64)}
    *{box-sizing:border-box} body{margin:0;background:var(--white);color:var(--ink);font:13px/1.45 ui-sans-serif,system-ui,-apple-system,Segoe UI,sans-serif}
    .shell{display:grid;grid-template-columns:232px 1fr;min-height:100vh}.side{background:var(--ink);color:var(--white);padding:18px 14px;display:flex;flex-direction:column;gap:16px}
    .brand{font-size:24px;font-weight:900;letter-spacing:0}.brand span{color:var(--orange2)}.nav button{width:100%;height:36px;text-align:left;border:0;border-radius:6px;background:transparent;color:var(--white);padding:0 10px;font-weight:700;cursor:pointer}.nav button.active,.nav button:hover{background:rgba(255,252,244,.12)}
    .main{padding:18px 22px}.top{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:16px}.top h1{font-size:22px;margin:0}.live{display:inline-flex;align-items:center;gap:8px;color:var(--teal2);font-weight:800}.dot{width:9px;height:9px;background:var(--teal);border-radius:50%;box-shadow:0 0 0 4px rgba(0,140,148,.12)}
    .grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;margin-bottom:14px}.stat{background:var(--cream);border:1px solid rgba(74,27,18,.18);border-radius:8px;padding:12px}.stat b{display:block;font-size:22px}.stat span{color:var(--muted);font-weight:700}
    .toolbar{display:flex;gap:8px;margin:10px 0 12px}.toolbar input,.toolbar select{height:36px;border:1px solid rgba(74,27,18,.22);border-radius:6px;background:#fff;color:var(--ink);padding:0 10px;font:inherit}
    .panel{display:none}.panel.active{display:block}.tablewrap{border:1px solid rgba(74,27,18,.18);border-radius:8px;overflow:auto;background:#fff}table{width:100%;border-collapse:collapse;min-width:840px}th,td{padding:10px;border-bottom:1px solid rgba(74,27,18,.12);text-align:left;vertical-align:top}th{font-size:12px;text-transform:uppercase;color:var(--muted);background:var(--cream)}
    tr:hover{background:#fffaf0}.pill{display:inline-flex;align-items:center;border-radius:999px;padding:3px 8px;font-weight:800;font-size:12px;background:rgba(0,140,148,.10);color:var(--teal2)}.Failed{background:rgba(217,90,26,.12);color:var(--orange)}.Published,.Completed{background:rgba(0,140,148,.12);color:var(--teal2)}.Pending,.Started,.Publishing{background:rgba(244,122,34,.14);color:var(--orange)}
    .events{display:grid;gap:8px}.event{background:#fff;border:1px solid rgba(74,27,18,.16);border-radius:8px;padding:10px}.event strong{display:inline-block;margin-right:8px}.event small{color:var(--muted)}
    @media (max-width: 840px){.shell{grid-template-columns:1fr}.side{position:sticky;top:0;z-index:2}.grid{grid-template-columns:repeat(2,minmax(0,1fr))}}
  </style>
</head>
<body>
  <div class="shell">
    <aside class="side">
      <div class="brand">Squirrel<span>Box</span></div>
      <nav class="nav">
        <button class="active" data-tab="inbox">Inbox</button>
        <button data-tab="outbox">Outbox</button>
        <button data-tab="deferred">Deferred</button>
        <button data-tab="events">Events</button>
      </nav>
    </aside>
    <main class="main">
      <div class="top"><h1>Work Dashboard</h1><div class="live"><span class="dot"></span><span id="liveText">Live</span></div></div>
      <section class="grid">
        <div class="stat"><b id="inboxCount">0</b><span>Inbox</span></div>
        <div class="stat"><b id="outboxCount">0</b><span>Outbox</span></div>
        <div class="stat"><b id="failedCount">0</b><span>Failed</span></div>
        <div class="stat"><b id="eventCount">0</b><span>Events</span></div>
      </section>
      <div class="toolbar"><input id="search" placeholder="Search id, operation, status, correlation" /><select id="status"><option value="">All statuses</option></select></div>
      <section id="inbox" class="panel active"><div class="tablewrap"><table><thead><tr><th>Status</th><th>Source</th><th>Operation</th><th>Key</th><th>Mode</th><th>Correlation</th><th>Created</th></tr></thead><tbody id="inboxRows"></tbody></table></div></section>
      <section id="outbox" class="panel"><div class="tablewrap"><table><thead><tr><th>Status</th><th>Transport</th><th>Operation</th><th>Destination</th><th>Attempts</th><th>Correlation</th><th>Updated</th></tr></thead><tbody id="outboxRows"></tbody></table></div></section>
      <section id="deferred" class="panel"><div class="events" id="deferredRows"></div></section>
      <section id="events" class="panel"><div class="events" id="eventRows"></div></section>
    </main>
  </div>
  <script>
    const state = { inbox: [], outbox: [], events: [], tab: 'inbox' };
    const $ = id => document.getElementById(id);
    const fmt = value => value ? new Date(value).toLocaleString() : '';
    const pill = value => `<span class="pill ${value}">${value}</span>`;
    const text = value => (value ?? '').toString().replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const matches = item => {
      const q = $('search').value.toLowerCase();
      const s = $('status').value;
      const hay = JSON.stringify(item).toLowerCase();
      return (!q || hay.includes(q)) && (!s || item.status === s || item.name === s);
    };
    function render() {
      $('inboxCount').textContent = state.inbox.length;
      $('outboxCount').textContent = state.outbox.length;
      $('failedCount').textContent = state.inbox.filter(x=>x.status==='Failed').length + state.outbox.filter(x=>x.status==='Failed').length;
      $('eventCount').textContent = state.events.length;
      $('inboxRows').innerHTML = state.inbox.filter(matches).map(x => `<tr><td>${pill(x.status)}</td><td>${text(x.source)}</td><td>${text(x.operation)}</td><td>${text(x.idempotencyKey)}</td><td>${text(x.executionMode)}</td><td>${text(x.correlationId)}</td><td>${fmt(x.createdOnUtc)}</td></tr>`).join('');
      $('outboxRows').innerHTML = state.outbox.filter(matches).map(x => `<tr><td>${pill(x.status)}</td><td>${text(x.transport)}</td><td>${text(x.operation)}</td><td>${text(x.destination)}</td><td>${x.attempts}</td><td>${text(x.correlationId)}</td><td>${fmt(x.updatedOnUtc)}</td></tr>`).join('');
      const deferred = state.events.filter(x => x.category === 'Deferred').filter(matches);
      $('deferredRows').innerHTML = deferred.map(eventCard).join('');
      $('eventRows').innerHTML = state.events.filter(matches).slice().reverse().map(eventCard).join('');
      const statuses = new Set([...state.inbox.map(x=>x.status), ...state.outbox.map(x=>x.status), ...state.events.map(x=>x.name)]);
      const current = $('status').value;
      $('status').innerHTML = '<option value="">All statuses</option>' + [...statuses].sort().map(x=>`<option ${x===current?'selected':''}>${text(x)}</option>`).join('');
    }
    function eventCard(x){return `<article class="event"><strong>${text(x.name)}</strong>${pill(x.status || x.category)}<br><small>${text(x.category)} ${text(x.subjectId)} ${fmt(x.occurredOnUtc)}</small></article>`}
    document.querySelectorAll('.nav button').forEach(btn => btn.addEventListener('click', () => {
      document.querySelectorAll('.nav button,.panel').forEach(x => x.classList.remove('active'));
      btn.classList.add('active'); $(btn.dataset.tab).classList.add('active'); state.tab = btn.dataset.tab;
    }));
    $('search').addEventListener('input', render); $('status').addEventListener('change', render);
    const dashboardPath = location.pathname.replace(/\/$/, '');
    fetch(`${dashboardPath}/api/state`).then(r => r.json()).then(data => { state.inbox=data.inbox||[]; state.outbox=data.outbox||[]; state.events=data.events||[]; render(); });
    const stream = new EventSource(`${dashboardPath}/events/stream`);
    stream.addEventListener('squirrelbox', e => { state.events.push(JSON.parse(e.data)); state.events = state.events.slice(-300); render(); });
    stream.onerror = () => $('liveText').textContent = 'Reconnecting';
    stream.onopen = () => $('liveText').textContent = 'Live';
  </script>
</body>
</html>
""";
}
