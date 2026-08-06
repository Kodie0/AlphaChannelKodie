namespace AlphaChannel.Server.Admin;

// A single self-contained static page rather than a separate frontend project - no build step,
// matches this project's existing minimal-tooling posture. The token is entered client-side and
// sent as X-Admin-Token on every fetch; the page itself carries no secrets and isn't gated by
// AdminTokenFilter (only the API calls it makes are).
internal static class AdminUiEndpoint
{
    public static void MapAdminUiEndpoint(this WebApplication app)
    {
        app.MapGet("/admin/ui", () => Results.Content(Html, "text/html"));
    }

    private const string Html = """
        <!doctype html>
        <html>
        <head>
        <meta charset="utf-8">
        <title>AlphaChannel Admin</title>
        <style>
          body { font-family: system-ui, sans-serif; background: #0b0a13; color: #e6e6f0; padding: 24px; max-width: 900px; margin: 0 auto; }
          h1 { color: #9466fa; }
          input[type=text] { background: #1a1826; color: #e6e6f0; border: 1px solid #3a3650; border-radius: 6px; padding: 6px 10px; }
          table { width: 100%; border-collapse: collapse; margin-top: 16px; }
          th, td { text-align: left; padding: 8px; border-bottom: 1px solid #2a2740; }
          button { background: #9466fa; color: white; border: none; border-radius: 6px; padding: 6px 12px; cursor: pointer; margin-right: 6px; }
          button.deny { background: #d75a5a; }
          .mismatch { color: #f0b64c; font-weight: bold; }
          .muted { color: #9a97ad; }
        </style>
        </head>
        <body>
        <h1>AlphaChannel Admin</h1>
        <p class="muted">Token is stored in this browser only.</p>
        <p>
          Admin token: <input type="text" id="token" placeholder="X-Admin-Token">
          <button onclick="saveToken()">Save</button>
        </p>

        <h2>Open reports</h2>
        <button onclick="loadReports()">Refresh</button>
        <table id="reportsTable">
          <thead><tr><th>Reporter</th><th>Target</th><th>Reason</th><th>Revealed</th><th>Verified</th><th></th></tr></thead>
          <tbody></tbody>
        </table>

        <h2>Ban / unban an account</h2>
        <p>
          Account ID: <input type="text" id="banAccountId" placeholder="account guid">
          Reason: <input type="text" id="banReason" placeholder="reason">
          <button onclick="banAccount()">Ban</button>
          <button onclick="unbanAccount()">Unban</button>
        </p>

        <h2>Pending Lalafell review</h2>
        <button onclick="loadPending()">Refresh</button>
        <table id="pendingTable">
          <thead><tr><th>Handle</th><th>Character</th><th>World</th><th>Self-reported races</th><th>Lodestone</th><th></th></tr></thead>
          <tbody></tbody>
        </table>

        <h2>Settings</h2>
        <p>
          <label><input type="checkbox" id="hideToggle"> Hide Lalafell accounts from non-Lalafell viewers (overrides individual preference for everyone)</label>
          <button onclick="saveSettings()">Save</button>
        </p>

        <script>
          function token() { return localStorage.getItem('adminToken') || ''; }
          function saveToken() { localStorage.setItem('adminToken', document.getElementById('token').value); loadPending(); loadSettings(); loadReports(); }
          document.getElementById('token').value = token();

          async function api(path, options) {
            options = options || {};
            options.headers = Object.assign({}, options.headers, { 'X-Admin-Token': token(), 'Content-Type': 'application/json' });
            const response = await fetch(path, options);
            if (!response.ok) throw new Error(response.status + ' ' + path);
            const text = await response.text();
            return text ? JSON.parse(text) : null;
          }

          async function loadPending() {
            const rows = await api('/admin/lalafell/pending');
            const body = document.querySelector('#pendingTable tbody');
            body.innerHTML = '';
            for (const row of rows) {
              const tr = document.createElement('tr');
              tr.innerHTML =
                '<td>@' + row.handle + '</td>' +
                '<td>' + row.characterName + '</td>' +
                '<td>' + row.world + '</td>' +
                '<td>' + (row.selfReportedRaces || '-') + '</td>' +
                '<td class="' + (row.lodestoneRaceMismatch ? 'mismatch' : 'muted') + '">' + (row.lodestoneRaceMismatch ? 'MISMATCH' : 'ok') + '</td>' +
                '<td><button onclick="approve(\'' + row.accountId + '\')">Approve</button><button class="deny" onclick="deny(\'' + row.accountId + '\')">Deny</button></td>';
              body.appendChild(tr);
            }
          }

          async function approve(id) { await api('/admin/lalafell/' + id + '/approve', { method: 'POST' }); loadPending(); }
          async function deny(id) { await api('/admin/lalafell/' + id + '/deny', { method: 'POST' }); loadPending(); }

          async function loadReports() {
            const rows = await api('/admin/reports');
            const body = document.querySelector('#reportsTable tbody');
            body.innerHTML = '';
            for (const row of rows) {
              const tr = document.createElement('tr');
              const verified = row.frankingVerified === null ? '-' : (row.frankingVerified ? 'yes' : 'NO (tampered?)');
              tr.innerHTML =
                '<td>@' + row.reporterHandle + '</td>' +
                '<td>@' + row.targetHandle + '</td>' +
                '<td>' + row.reason + (row.details ? ' - ' + row.details : '') + '</td>' +
                '<td>' + (row.revealedBody || '-') + '</td>' +
                '<td class="' + (row.frankingVerified === false ? 'mismatch' : 'muted') + '">' + verified + '</td>' +
                '<td>' +
                  '<button onclick="resolveReport(\'' + row.id + '\', 0)">Dismiss</button>' +
                  '<button onclick="resolveReport(\'' + row.id + '\', 2)">Suspend 7d</button>' +
                  '<button class="deny" onclick="resolveReport(\'' + row.id + '\', 3)">Ban</button>' +
                '</td>';
              body.appendChild(tr);
            }
          }

          async function resolveReport(id, action) {
            await api('/admin/reports/' + id + '/resolve', { method: 'POST', body: JSON.stringify({ action }) });
            loadReports();
          }

          async function banAccount() {
            const id = document.getElementById('banAccountId').value;
            const reason = document.getElementById('banReason').value;
            await api('/admin/accounts/' + id + '/ban', { method: 'POST', body: JSON.stringify({ reason }) });
          }

          async function unbanAccount() {
            const id = document.getElementById('banAccountId').value;
            await api('/admin/accounts/' + id + '/unban', { method: 'POST' });
          }

          async function loadSettings() {
            const settings = await api('/admin/settings');
            document.getElementById('hideToggle').checked = settings.hideLalafellFromNonLalafell;
          }

          async function saveSettings() {
            await api('/admin/settings', { method: 'POST', body: JSON.stringify({ hideLalafellFromNonLalafell: document.getElementById('hideToggle').checked }) });
          }

          if (token()) { loadPending(); loadSettings(); loadReports(); }
        </script>
        </body>
        </html>
        """;
}
