using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Voxta.Modules.PromptEditor.Controllers;

[Authorize(Roles = "ADMIN")]
[Route("manage/prompt-editor")]
public sealed class PromptEditorManageController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return Content(PageHtml, "text/html");
    }

    private const string PageHtml =
        // language=html
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Voxta Prompt Editor</title>
          <style>
            :root {
              --bg: #0b0f14;
              --panel: #0f1722;
              --panel2: #0c1320;
              --text: #e6edf3;
              --muted: #9aa7b2;
              --border: #223047;
              --accent: #4f8cff;
              --danger: #ff5f5f;
              --ok: #42d392;
              --mono: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
              --sans: ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, Arial, "Noto Sans", "Liberation Sans", sans-serif;
            }
            * { box-sizing: border-box; }
            body {
              margin: 0;
              font-family: var(--sans);
              color: var(--text);
              background: radial-gradient(1000px 600px at 20% 0%, #101a2b, transparent),
                          radial-gradient(900px 500px at 90% 20%, #131b2e, transparent),
                          var(--bg);
            }
            header {
              padding: 18px 22px;
              border-bottom: 1px solid var(--border);
              background: rgba(10, 16, 24, 0.75);
              backdrop-filter: blur(10px);
              position: sticky;
              top: 0;
              z-index: 10;
            }
            header h1 {
              margin: 0;
              font-size: 16px;
              letter-spacing: 0.2px;
            }
            header .sub {
              margin-top: 6px;
              color: var(--muted);
              font-size: 12px;
              line-height: 1.35;
            }
            .wrap {
              max-width: 1200px;
              margin: 0 auto;
              padding: 18px 18px 26px;
            }
            .grid {
              display: grid;
              grid-template-columns: 1fr;
              gap: 14px;
            }
            .card {
              border: 1px solid var(--border);
              border-radius: 10px;
              background: rgba(15, 23, 34, 0.72);
              box-shadow: 0 6px 24px rgba(0,0,0,0.25);
              overflow: hidden;
            }
            .card .title {
              padding: 12px 14px;
              border-bottom: 1px solid var(--border);
              display: flex;
              justify-content: space-between;
              align-items: center;
              gap: 10px;
            }
            .title .left {
              display: flex;
              gap: 10px;
              align-items: center;
              min-width: 0;
            }
            .pill {
              font-size: 11px;
              padding: 2px 8px;
              border-radius: 999px;
              background: rgba(79, 140, 255, 0.14);
              border: 1px solid rgba(79, 140, 255, 0.25);
              color: #b9d0ff;
              white-space: nowrap;
            }
            .title h2 {
              margin: 0;
              font-size: 13px;
              font-weight: 600;
              letter-spacing: 0.2px;
              white-space: nowrap;
              overflow: hidden;
              text-overflow: ellipsis;
            }
            .card .body {
              padding: 14px;
            }
            .row {
              display: grid;
              grid-template-columns: 1fr 1fr 1fr;
              gap: 12px;
              margin-bottom: 10px;
            }
            .row.row2 { grid-template-columns: 1fr 1fr; }
            .row.row4 { grid-template-columns: 1fr 1fr 1fr 1fr; }
            label {
              display: block;
              font-size: 12px;
              color: var(--muted);
              margin-bottom: 6px;
            }
            select, input[type="text"] {
              width: 100%;
              padding: 10px 10px;
              border-radius: 8px;
              border: 1px solid var(--border);
              background: rgba(11, 15, 20, 0.65);
              color: var(--text);
              outline: none;
            }
            select:focus, input[type="text"]:focus, textarea:focus {
              border-color: rgba(79, 140, 255, 0.55);
              box-shadow: 0 0 0 3px rgba(79, 140, 255, 0.12);
            }
            textarea {
              width: 100%;
              min-height: 520px;
              resize: vertical;
              padding: 12px;
              border-radius: 10px;
              border: 1px solid var(--border);
              background: rgba(10, 14, 20, 0.75);
              color: var(--text);
              font-family: var(--mono);
              font-size: 13px;
              line-height: 1.45;
              tab-size: 2;
            }
            .actions {
              display: flex;
              gap: 10px;
              flex-wrap: wrap;
              align-items: center;
            }
            button {
              appearance: none;
              border: 1px solid rgba(79, 140, 255, 0.4);
              background: rgba(79, 140, 255, 0.16);
              color: #d8e5ff;
              padding: 9px 12px;
              border-radius: 8px;
              cursor: pointer;
              font-weight: 600;
              font-size: 12px;
            }
            button:hover { background: rgba(79, 140, 255, 0.22); }
            button:disabled {
              opacity: 0.55;
              cursor: not-allowed;
            }
            button.danger {
              border-color: rgba(255, 95, 95, 0.45);
              background: rgba(255, 95, 95, 0.14);
              color: #ffd6d6;
            }
            button.danger:hover { background: rgba(255, 95, 95, 0.18); }
            button.ghost {
              border-color: rgba(154, 167, 178, 0.35);
              background: rgba(154, 167, 178, 0.06);
              color: var(--text);
            }
            .status {
              font-family: var(--mono);
              font-size: 12px;
              color: var(--muted);
              white-space: pre-wrap;
              background: rgba(8, 12, 18, 0.55);
              border: 1px solid rgba(34, 48, 71, 0.65);
              padding: 10px 12px;
              border-radius: 10px;
              min-height: 42px;
            }
            .ok { color: var(--ok); }
            .err { color: var(--danger); }
            .hint {
              margin-top: 10px;
              color: var(--muted);
              font-size: 12px;
              line-height: 1.4;
            }
            @media (max-width: 980px) {
              .row, .row.row2, .row.row4 { grid-template-columns: 1fr; }
              textarea { min-height: 420px; }
            }
          </style>
        </head>
        <body>
          <header>
            <h1>Prompt Editor</h1>
            <div class="sub">
              Live prompts are in <code>Resources/Prompts/Default/&lt;lang&gt;</code>.
              Collections and originals live in <code>Data/PromptEditor/</code>.
            </div>
          </header>
        
          <div class="wrap">
            <div class="grid">
              <div class="card">
                <div class="title">
                  <div class="left">
                    <span class="pill">Admin-only</span>
                    <h2 id="currentPath">(not loaded)</h2>
                  </div>
                  <div class="actions">
                    <button id="btnSave" title="Ctrl+S">Save</button>
                    <button id="btnReload" class="ghost" title="Reload template from disk">Reload</button>
                  </div>
                </div>
                <div class="body">
                  <div class="row row4">
                    <div>
                      <label for="selSource">Editing source</label>
                      <select id="selSource">
                        <option value="live">Live</option>
                        <option value="collection">Collection</option>
                      </select>
                    </div>
                    <div>
                      <label for="selCollection">Collection</label>
                      <select id="selCollection"></select>
                    </div>
                    <div>
                      <label for="txtNewCollection">New collection</label>
                      <input id="txtNewCollection" type="text" placeholder="MyPrompts-v1" />
                    </div>
                    <div style="display:flex; align-items:end;">
                      <button id="btnCreateCollection" class="ghost" style="width:100%;">Create from Live (language)</button>
                    </div>
                  </div>
        
                  <div class="row">
                    <div>
                      <label for="selLanguage">Language</label>
                      <select id="selLanguage"></select>
                    </div>
                    <div>
                      <label for="selCategory">Category folder</label>
                      <select id="selCategory"></select>
                    </div>
                    <div>
                      <label for="selTemplate">Template</label>
                      <select id="selTemplate"></select>
                    </div>
                  </div>
        
                  <div class="row row2">
                    <div class="actions">
                      <button id="btnApplyCollection" class="danger">Apply collection to Live (language)</button>
                      <button id="btnRestoreOriginals" class="ghost">Restore Originals to Live (language)</button>
                    </div>
                    <div>
                      <div class="status" id="status">Loading…</div>
                    </div>
                  </div>
        
                  <textarea id="txtEditor" spellcheck="false" wrap="off"></textarea>
                  <div class="hint">
                    Tip: This is a plain editor (no template validation). Apply collections carefully — it overwrites the Live language folder after restoring the Originals backup.
                  </div>
                </div>
              </div>
            </div>
          </div>
        
          <script>
            const apiBase = '/api/extensions/prompt-editor';
            const els = {
              source: document.getElementById('selSource'),
              collection: document.getElementById('selCollection'),
              newCollection: document.getElementById('txtNewCollection'),
              language: document.getElementById('selLanguage'),
              category: document.getElementById('selCategory'),
              template: document.getElementById('selTemplate'),
              editor: document.getElementById('txtEditor'),
              status: document.getElementById('status'),
              currentPath: document.getElementById('currentPath'),
              btnSave: document.getElementById('btnSave'),
              btnReload: document.getElementById('btnReload'),
              btnCreate: document.getElementById('btnCreateCollection'),
              btnApply: document.getElementById('btnApplyCollection'),
              btnRestore: document.getElementById('btnRestoreOriginals'),
            };
        
            const stateKey = 'voxta.promptEditor.state.v1';
            const state = loadState();
            let dirty = false;
        
            function loadState() {
              try {
                const raw = localStorage.getItem(stateKey);
                if (!raw) return { source: 'live', collection: '', language: 'en', category: '', template: '' };
                const s = JSON.parse(raw);
                return {
                  source: (s.source === 'collection') ? 'collection' : 'live',
                  collection: (s.collection || ''),
                  language: (s.language || 'en'),
                  category: (s.category || ''),
                  template: (s.template || ''),
                };
              } catch {
                return { source: 'live', collection: '', language: 'en', category: '', template: '' };
              }
            }
        
            function saveState() {
              try {
                localStorage.setItem(stateKey, JSON.stringify(state));
              } catch {}
            }
        
            function setStatus(msg, kind) {
              els.status.textContent = msg || '';
              els.status.classList.remove('ok', 'err');
              if (kind) els.status.classList.add(kind);
            }
        
            async function apiJson(path, opts) {
              const res = await fetch(apiBase + path, {
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json' },
                ...opts
              });
              if (!res.ok) {
                const txt = await res.text();
                throw new Error(txt || `${res.status} ${res.statusText}`);
              }
              const ct = res.headers.get('content-type') || '';
              if (ct.includes('application/json')) return await res.json();
              return await res.text();
            }
        
            function fillSelect(sel, items, value, placeholder) {
              sel.innerHTML = '';
              if (placeholder) {
                const opt = document.createElement('option');
                opt.value = '';
                opt.textContent = placeholder;
                sel.appendChild(opt);
              }
              for (const item of items) {
                const opt = document.createElement('option');
                opt.value = item;
                opt.textContent = item;
                sel.appendChild(opt);
              }
              if (value && items.includes(value)) sel.value = value;
              else sel.value = sel.options.length ? sel.options[0].value : '';
            }
        
            function currentSelection() {
              return {
                source: els.source.value,
                collection: els.collection.value,
                language: els.language.value,
                category: els.category.value,
                template: els.template.value,
              };
            }
        
            function updatePathLabel() {
              const s = currentSelection();
              const src = s.source === 'collection' ? `collection:${s.collection || '(none)'}` : 'live';
              const path = (s.language && s.category && s.template)
                ? `${src} / ${s.language} / ${s.category} / ${s.template}`
                : `${src}`;
              els.currentPath.textContent = (dirty ? '* ' : '') + path;
            }
        
            async function refreshAll() {
              setStatus('Loading…');
              updateControls();
        
              const langs = await apiJson('/languages');
              fillSelect(els.language, langs.items || [], state.language);
              state.language = els.language.value;
        
              const cols = await apiJson('/collections');
              fillSelect(els.collection, cols.items || [], state.collection, '(select)');
              state.collection = els.collection.value;
        
              els.source.value = state.source;
              updateControls();
        
              await refreshCategories();
              setStatus('Ready.', 'ok');
            }
        
            function updateControls() {
              const isCollection = els.source.value === 'collection';
              els.collection.disabled = !isCollection;
              els.btnApply.disabled = !isCollection || !els.collection.value;
              els.btnCreate.disabled = false;
              updatePathLabel();
            }
        
            async function refreshCategories() {
              const s = currentSelection();
              state.source = s.source;
              state.collection = s.collection;
              state.language = s.language;
              saveState();
        
              if (s.source === 'collection' && !s.collection) {
                fillSelect(els.category, [], '', '(select a collection)');
                fillSelect(els.template, [], '', '(select a category)');
                els.editor.value = '';
                dirty = false;
                updatePathLabel();
                return;
              }
        
              const qs = new URLSearchParams({
                source: s.source,
                collection: s.collection || '',
                language: s.language || 'en'
              });
              const cats = await apiJson('/categories?' + qs.toString());
              fillSelect(els.category, cats.items || [], state.category);
              state.category = els.category.value;
              saveState();
              await refreshTemplates();
            }
        
            async function refreshTemplates() {
              const s = currentSelection();
              state.category = s.category;
              saveState();
        
              if (!s.category) {
                fillSelect(els.template, [], '', '(select a category)');
                els.editor.value = '';
                dirty = false;
                updatePathLabel();
                return;
              }
        
              const qs = new URLSearchParams({
                source: s.source,
                collection: s.collection || '',
                language: s.language || 'en',
                category: s.category || ''
              });
              const temps = await apiJson('/templates?' + qs.toString());
              fillSelect(els.template, temps.items || [], state.template);
              state.template = els.template.value;
              saveState();
              await loadTemplate();
            }
        
            async function loadTemplate() {
              const s = currentSelection();
              state.template = s.template;
              saveState();
        
              if (!s.template) {
                els.editor.value = '';
                dirty = false;
                updatePathLabel();
                return;
              }
        
              const qs = new URLSearchParams({
                source: s.source,
                collection: s.collection || '',
                language: s.language || 'en',
                category: s.category || '',
                path: s.template || ''
              });
              const tpl = await apiJson('/template?' + qs.toString());
              els.editor.value = (tpl && typeof tpl.content === 'string') ? tpl.content : '';
              dirty = false;
              updatePathLabel();
            }
        
            async function saveTemplate() {
              const s = currentSelection();
              if (!s.language || !s.category || !s.template) {
                setStatus('Pick language, category, and template first.', 'err');
                return;
              }
              const payload = {
                source: s.source,
                collection: s.collection || null,
                language: s.language,
                category: s.category,
                templatePath: s.template,
                content: els.editor.value || ''
              };
              const res = await apiJson('/template', { method: 'PUT', body: JSON.stringify(payload) });
              dirty = false;
              updatePathLabel();
              setStatus(res && res.message ? res.message : 'Saved.', 'ok');
            }
        
            async function createCollection() {
              const name = (els.newCollection.value || '').trim();
              const language = els.language.value || 'en';
              if (!name) {
                setStatus('Enter a New collection name first.', 'err');
                return;
              }
              const res = await apiJson('/collections/create', {
                method: 'POST',
                body: JSON.stringify({ name, language })
              });
              setStatus(res && res.message ? res.message : 'Created.', 'ok');
              els.newCollection.value = '';
              state.source = 'collection';
              state.collection = (res && res.value) ? res.value : name;
              state.category = '';
              state.template = '';
              saveState();
              await refreshAll();
            }
        
            async function applyCollection() {
              const s = currentSelection();
              if (!s.collection) {
                setStatus('Select a collection first.', 'err');
                return;
              }
              if (!confirm(`Apply collection "${s.collection}" to Live for "${s.language}"? This overwrites Live prompts for that language.`)) {
                return;
              }
              const res = await apiJson('/collections/apply', {
                method: 'POST',
                body: JSON.stringify({ name: s.collection, language: s.language })
              });
              setStatus(res && res.message ? res.message : 'Applied.', 'ok');
            }
        
            async function restoreOriginals() {
              const language = els.language.value || 'en';
              if (!confirm(`Restore Originals to Live for "${language}"? This resets Live prompts for that language back to the backup.`)) {
                return;
              }
              const res = await apiJson('/originals/restore', {
                method: 'POST',
                body: JSON.stringify({ language })
              });
              setStatus(res && res.message ? res.message : 'Restored.', 'ok');
            }
        
            function guardUnsavedChanges() {
              if (!dirty) return true;
              return confirm('You have unsaved changes. Discard them?');
            }
        
            els.source.addEventListener('change', async () => {
              if (!guardUnsavedChanges()) {
                els.source.value = state.source;
                return;
              }
              state.source = els.source.value;
              saveState();
              dirty = false;
              await refreshCategories();
              updateControls();
            });
        
            els.collection.addEventListener('change', async () => {
              if (!guardUnsavedChanges()) {
                els.collection.value = state.collection;
                return;
              }
              state.collection = els.collection.value;
              saveState();
              dirty = false;
              await refreshCategories();
              updateControls();
            });
        
            els.language.addEventListener('change', async () => {
              if (!guardUnsavedChanges()) {
                els.language.value = state.language;
                return;
              }
              state.language = els.language.value;
              saveState();
              dirty = false;
              await refreshCategories();
            });
        
            els.category.addEventListener('change', async () => {
              if (!guardUnsavedChanges()) {
                els.category.value = state.category;
                return;
              }
              state.category = els.category.value;
              saveState();
              dirty = false;
              await refreshTemplates();
            });
        
            els.template.addEventListener('change', async () => {
              if (!guardUnsavedChanges()) {
                els.template.value = state.template;
                return;
              }
              state.template = els.template.value;
              saveState();
              dirty = false;
              await loadTemplate();
            });
        
            els.editor.addEventListener('input', () => {
              if (!dirty) {
                dirty = true;
                updatePathLabel();
              }
            });
        
            els.btnSave.addEventListener('click', () => saveTemplate().catch(e => setStatus(e.message || String(e), 'err')));
            els.btnReload.addEventListener('click', () => loadTemplate().catch(e => setStatus(e.message || String(e), 'err')));
            els.btnCreate.addEventListener('click', () => createCollection().catch(e => setStatus(e.message || String(e), 'err')));
            els.btnApply.addEventListener('click', () => applyCollection().catch(e => setStatus(e.message || String(e), 'err')));
            els.btnRestore.addEventListener('click', () => restoreOriginals().catch(e => setStatus(e.message || String(e), 'err')));
        
            document.addEventListener('keydown', (e) => {
              if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
                e.preventDefault();
                saveTemplate().catch(err => setStatus(err.message || String(err), 'err'));
              }
            });
        
            refreshAll().catch(err => setStatus(err.message || String(err), 'err'));
          </script>
        </body>
        </html>
        """;
}
