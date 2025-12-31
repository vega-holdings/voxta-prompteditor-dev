/**
 * Voxta Prompt Editor - Main Application
 * Direct API mode (no IndexedDB)
 * VERSION: 2024-12-30-v6 - Drag-and-drop reordering + Auto-inject
 */

const API = '/api';
console.log('Prompt Editor app.js loaded, API base:', API);

// State
const state = {
  source: 'live',
  collection: null,
  language: 'en',
  category: '',
  template: '',
  presetName: null,
  activePresetName: null
};

let currentPreset = null;
let promptOrder = [];

// Active preset state
let activePresetData = null;

// DOM elements cache
const els = {};

// ============ API Functions ============

async function apiGet(path) {
  const res = await fetch(API + path, { credentials: 'same-origin' });
  if (!res.ok) throw new Error(await res.text() || res.statusText);
  return res.json();
}

async function apiPost(path, body) {
  const res = await fetch(API + path, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!res.ok) throw new Error(await res.text() || res.statusText);
  return res.json();
}

async function apiPut(path, body) {
  const res = await fetch(API + path, {
    method: 'PUT',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!res.ok) throw new Error(await res.text() || res.statusText);
  return res.json();
}

async function apiDelete(path) {
  const res = await fetch(API + path, {
    method: 'DELETE',
    credentials: 'same-origin'
  });
  if (!res.ok) throw new Error(await res.text() || res.statusText);
  return res.json();
}

// ============ Utility Functions ============

function setStatus(msg, type = '') {
  els.templateStatus.textContent = msg;
  els.templateStatus.className = 'status-box' + (type ? ' ' + type : '');
}

function toast(msg, type = 'success') {
  const t = document.getElementById('toast');
  t.textContent = msg;
  t.className = 'toast show ' + (type === 'success' ? '' : type);
  setTimeout(() => t.classList.remove('show'), 3000);
}

function fillSelect(sel, items, value = null, placeholder = null) {
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
  if (value && items.includes(value)) {
    sel.value = value;
  }
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// ============ Templates Tab ============

async function loadLanguages() {
  try {
    const data = await apiGet('/languages');
    fillSelect(els.selLanguage, data.items, state.language);
  } catch (err) {
    console.error('Failed to load languages:', err);
  }
}

async function loadCollections() {
  try {
    const data = await apiGet('/collections');
    fillSelect(els.selCollection, data.items, state.collection, '(none)');
  } catch (err) {
    console.error('Failed to load collections:', err);
  }
}

async function loadCategories() {
  try {
    const params = new URLSearchParams({
      source: state.source,
      language: state.language
    });
    if (state.collection) params.set('collection', state.collection);

    const data = await apiGet('/categories?' + params);
    fillSelect(els.selCategory, data.items, state.category, '(select)');

    if (data.items.length > 0 && !state.category) {
      state.category = data.items[0];
      els.selCategory.value = state.category;
    }
    await loadTemplates();
  } catch (err) {
    console.error('Failed to load categories:', err);
    setStatus('Failed: ' + err.message, 'error');
  }
}

async function loadTemplates() {
  try {
    const params = new URLSearchParams({
      source: state.source,
      language: state.language,
      category: state.category || ''
    });
    if (state.collection) params.set('collection', state.collection);

    const data = await apiGet('/templates?' + params);
    fillSelect(els.selTemplate, data.items, state.template, '(select)');

    if (data.items.length > 0 && !state.template) {
      state.template = data.items[0];
      els.selTemplate.value = state.template;
    }
    await loadTemplate();
  } catch (err) {
    console.error('Failed to load templates:', err);
    setStatus('Failed: ' + err.message, 'error');
  }
}

async function loadTemplate() {
  if (!state.template) {
    els.templateEditor.value = '';
    els.templatePath.textContent = '(not loaded)';
    return;
  }

  try {
    const params = new URLSearchParams({
      source: state.source,
      language: state.language,
      category: state.category || '',
      path: state.template
    });
    if (state.collection) params.set('collection', state.collection);

    const data = await apiGet('/template?' + params);
    els.templateEditor.value = data.content || '';
    els.templatePath.textContent = `${state.language}/${state.category}/${state.template}`;
    setStatus('Loaded', 'ok');
  } catch (err) {
    console.error('Failed to load template:', err);
    setStatus('Failed: ' + err.message, 'error');
  }
}

async function saveTemplate() {
  if (!state.template) {
    setStatus('No template selected', 'error');
    return;
  }

  try {
    setStatus('Saving...');
    await apiPut('/template', {
      source: state.source,
      collection: state.collection,
      language: state.language,
      category: state.category,
      templatePath: state.template,
      content: els.templateEditor.value
    });
    setStatus('Saved!', 'ok');
    toast('Template saved');
  } catch (err) {
    console.error('Failed to save:', err);
    setStatus('Failed: ' + err.message, 'error');
  }
}

async function createCollection() {
  const name = els.txtNewCollection.value.trim();
  if (!name) {
    toast('Enter a collection name', 'error');
    return;
  }

  try {
    await apiPost('/collections/create', { name, language: state.language });
    toast('Collection created');
    els.txtNewCollection.value = '';
    await loadCollections();
    els.selCollection.value = name;
    state.collection = name;
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

async function applyCollection() {
  if (!state.collection) {
    toast('Select a collection first', 'error');
    return;
  }
  if (!confirm(`Apply "${state.collection}" to Live?`)) return;

  try {
    await apiPost('/collections/apply', { name: state.collection, language: state.language });
    toast('Applied to Live');
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

async function restoreOriginals() {
  if (!confirm('Restore original templates to Live?')) return;

  try {
    await apiPost('/originals/restore', { language: state.language });
    toast('Originals restored');
    await loadTemplate();
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

// ============ Insert Include Helper ============

async function loadConvertedPresets() {
  try {
    const data = await apiGet('/converted-presets');
    fillSelect(els.selConvertedPreset, data.items, null, '(select preset)');
  } catch (err) {
    console.error('Failed to load converted presets:', err);
    fillSelect(els.selConvertedPreset, [], null, '(none available)');
  }
}

function insertIncludeAtCursor() {
  const presetName = els.selConvertedPreset.value;
  if (!presetName) {
    toast('Select a converted preset first', 'error');
    return;
  }

  const includeText = `{{ include 'Presets/${presetName}/Main' }}`;
  const editor = els.templateEditor;
  const start = editor.selectionStart;
  const end = editor.selectionEnd;
  const text = editor.value;

  // Insert at cursor position
  editor.value = text.substring(0, start) + includeText + text.substring(end);

  // Place cursor after the inserted text
  const newPos = start + includeText.length;
  editor.selectionStart = newPos;
  editor.selectionEnd = newPos;
  editor.focus();

  toast(`Inserted include for ${presetName}`);
}

// ============ Active Presets Tab ============

async function loadActivePresetList() {
  try {
    const data = await apiGet('/converted-presets');
    fillSelect(els.selActivePreset, data.items || [], state.activePresetName, '(select preset)');

    // Auto-load first preset if none selected
    if (!state.activePresetName && data.items?.length > 0) {
      state.activePresetName = data.items[0];
      els.selActivePreset.value = state.activePresetName;
      await loadActivePreset(state.activePresetName);
    }
  } catch (err) {
    console.error('Failed to load active presets:', err);
    fillSelect(els.selActivePreset, [], null, '(none available)');
  }
}

async function loadActivePreset(name) {
  if (!name) {
    activePresetData = null;
    state.activePresetName = null;
    renderActivePrompts();
    updateActivePresetStatus();
    updateInjectionStatus();
    return;
  }

  try {
    activePresetData = await apiGet('/converted-presets/' + encodeURIComponent(name));
    state.activePresetName = name;
    renderActivePrompts();
    updateActivePresetStatus();
    updateInjectionStatus();

    // Update include statement
    els.activeIncludeStatement.value = `{{ include 'Presets/${name}/Main' }}`;
  } catch (err) {
    console.error('Failed to load active preset:', err);
    toast('Failed: ' + err.message, 'error');
    activePresetData = null;
    renderActivePrompts();
    updateInjectionStatus();
  }
}

function updateActivePresetStatus() {
  if (!activePresetData) {
    els.activePresetStatus.textContent = 'No preset loaded';
    els.activePresetStatus.className = 'pill';
    els.activePromptTotal.textContent = '0';
    els.activePromptEnabled.textContent = '0';
    return;
  }

  const prompts = activePresetData.prompts || [];
  const enabled = prompts.filter(p => p.enabled).length;

  els.activePresetStatus.textContent = state.activePresetName;
  els.activePresetStatus.className = 'pill success';
  els.activePromptTotal.textContent = prompts.length;
  els.activePromptEnabled.textContent = enabled;
}

function renderActivePrompts() {
  const list = els.activePromptList;

  if (!activePresetData?.prompts?.length) {
    list.innerHTML = '<div class="empty-state"><h3>No Active Preset Selected</h3><p>Select a converted preset from the dropdown above, or convert one from the Converter tab.</p></div>';
    return;
  }

  list.innerHTML = '';

  for (const prompt of activePresetData.prompts) {
    const item = document.createElement('div');
    item.className = 'prompt-item' + (prompt.enabled ? '' : ' disabled');
    item.dataset.name = prompt.name;
    item.draggable = true;

    item.innerHTML = `
      <div class="prompt-header">
        <span class="drag-handle" title="Drag to reorder">⋮⋮</span>
        <div class="toggle ${prompt.enabled ? 'enabled' : ''}" data-name="${escapeHtml(prompt.name)}"></div>
        <span class="prompt-name">${escapeHtml(prompt.name)}</span>
        <span class="prompt-expand">▼</span>
      </div>
      <div class="prompt-content">
        <textarea class="code prompt-editor" data-name="${escapeHtml(prompt.name)}" rows="12">${escapeHtml(prompt.content || '')}</textarea>
        <div class="btn-group" style="margin-top:10px">
          <button class="primary save-prompt-btn" data-name="${escapeHtml(prompt.name)}">Save Changes</button>
        </div>
      </div>
    `;

    // Toggle click
    item.querySelector('.toggle').addEventListener('click', (e) => {
      e.stopPropagation();
      toggleActivePrompt(prompt.name);
    });

    // Header click to expand/collapse
    item.querySelector('.prompt-header').addEventListener('click', (e) => {
      if (e.target.classList.contains('toggle') || e.target.classList.contains('drag-handle')) return;
      item.classList.toggle('expanded');
    });

    // Save button
    item.querySelector('.save-prompt-btn').addEventListener('click', (e) => {
      e.stopPropagation();
      saveActivePrompt(prompt.name);
    });

    // Drag and drop events
    item.addEventListener('dragstart', handleDragStart);
    item.addEventListener('dragend', handleDragEnd);
    item.addEventListener('dragover', handleDragOver);
    item.addEventListener('drop', handleDrop);
    item.addEventListener('dragenter', handleDragEnter);
    item.addEventListener('dragleave', handleDragLeave);

    list.appendChild(item);
  }
}

// Drag and drop state
let draggedItem = null;
let draggedName = null;

function handleDragStart(e) {
  draggedItem = this;
  draggedName = this.dataset.name;
  this.classList.add('dragging');
  e.dataTransfer.effectAllowed = 'move';
  e.dataTransfer.setData('text/plain', this.dataset.name);
}

function handleDragEnd(e) {
  this.classList.remove('dragging');
  document.querySelectorAll('.prompt-item').forEach(item => {
    item.classList.remove('drag-over', 'drag-over-top', 'drag-over-bottom');
  });
  draggedItem = null;
  draggedName = null;
}

function handleDragOver(e) {
  e.preventDefault();
  e.dataTransfer.dropEffect = 'move';

  if (this === draggedItem) return;

  const rect = this.getBoundingClientRect();
  const midpoint = rect.top + rect.height / 2;

  this.classList.remove('drag-over-top', 'drag-over-bottom');
  if (e.clientY < midpoint) {
    this.classList.add('drag-over-top');
  } else {
    this.classList.add('drag-over-bottom');
  }
}

function handleDragEnter(e) {
  e.preventDefault();
  if (this !== draggedItem) {
    this.classList.add('drag-over');
  }
}

function handleDragLeave(e) {
  this.classList.remove('drag-over', 'drag-over-top', 'drag-over-bottom');
}

async function handleDrop(e) {
  e.preventDefault();
  e.stopPropagation();

  if (this === draggedItem || !activePresetData) return;

  const targetName = this.dataset.name;
  const sourceName = draggedName;

  if (!targetName || !sourceName) return;

  // Determine if dropping above or below target
  const rect = this.getBoundingClientRect();
  const midpoint = rect.top + rect.height / 2;
  const dropAbove = e.clientY < midpoint;

  // Get current order
  const prompts = activePresetData.prompts;
  const sourceIndex = prompts.findIndex(p => p.name === sourceName);
  const targetIndex = prompts.findIndex(p => p.name === targetName);

  if (sourceIndex === -1 || targetIndex === -1) return;

  // Remove source from array
  const [movedPrompt] = prompts.splice(sourceIndex, 1);

  // Calculate new target index (accounting for the removal)
  let newIndex = targetIndex;
  if (sourceIndex < targetIndex) {
    newIndex--;
  }
  if (!dropAbove) {
    newIndex++;
  }

  // Insert at new position
  prompts.splice(newIndex, 0, movedPrompt);

  // Save new order to backend
  const enabledPrompts = prompts.filter(p => p.enabled).map(p => p.name);
  const promptOrder = prompts.map(p => p.name);

  try {
    await apiPut('/converted-presets/' + encodeURIComponent(state.activePresetName) + '/config', {
      enabledPrompts,
      promptOrder
    });

    // Re-render
    renderActivePrompts();
    updateActivePresetStatus();
    toast('Reordered prompts');
  } catch (err) {
    toast('Failed to save order: ' + err.message, 'error');
    // Reload to restore original order
    await loadActivePreset(state.activePresetName);
  }
}

async function toggleActivePrompt(promptName) {
  if (!activePresetData) return;

  const prompt = activePresetData.prompts.find(p => p.name === promptName);
  if (!prompt) return;

  // Toggle locally
  prompt.enabled = !prompt.enabled;

  // Build config
  const enabledPrompts = activePresetData.prompts.filter(p => p.enabled).map(p => p.name);
  const promptOrder = activePresetData.prompts.map(p => p.name);

  try {
    await apiPut('/converted-presets/' + encodeURIComponent(state.activePresetName) + '/config', {
      enabledPrompts,
      promptOrder
    });

    // Re-render
    renderActivePrompts();
    updateActivePresetStatus();
    toast(`${prompt.enabled ? 'Enabled' : 'Disabled'} "${promptName}"`);
  } catch (err) {
    // Revert on error
    prompt.enabled = !prompt.enabled;
    toast('Failed: ' + err.message, 'error');
  }
}

async function saveActivePrompt(promptName) {
  if (!activePresetData || !state.activePresetName) return;

  const textarea = document.querySelector(`.prompt-editor[data-name="${CSS.escape(promptName)}"]`);
  if (!textarea) return;

  const content = textarea.value;

  try {
    await apiPut('/converted-presets/' + encodeURIComponent(state.activePresetName) + '/prompt/' + encodeURIComponent(promptName), {
      content
    });

    // Update local data
    const prompt = activePresetData.prompts.find(p => p.name === promptName);
    if (prompt) prompt.content = content;

    toast(`Saved "${promptName}"`);
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

async function enableAllActivePrompts() {
  if (!activePresetData) return;

  const enabledPrompts = activePresetData.prompts.map(p => p.name);
  const promptOrder = activePresetData.prompts.map(p => p.name);

  try {
    await apiPut('/converted-presets/' + encodeURIComponent(state.activePresetName) + '/config', {
      enabledPrompts,
      promptOrder
    });

    // Update local state
    activePresetData.prompts.forEach(p => p.enabled = true);
    renderActivePrompts();
    updateActivePresetStatus();
    toast('All prompts enabled');
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

async function disableAllActivePrompts() {
  if (!activePresetData) return;

  const promptOrder = activePresetData.prompts.map(p => p.name);

  try {
    await apiPut('/converted-presets/' + encodeURIComponent(state.activePresetName) + '/config', {
      enabledPrompts: [],
      promptOrder
    });

    // Update local state
    activePresetData.prompts.forEach(p => p.enabled = false);
    renderActivePrompts();
    updateActivePresetStatus();
    toast('All prompts disabled');
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

function copyIncludeStatement() {
  const text = els.activeIncludeStatement.value;
  if (text === '(select a preset)') {
    toast('Select a preset first', 'error');
    return;
  }

  navigator.clipboard.writeText(text).then(() => {
    toast('Copied to clipboard!');
  }).catch(() => {
    // Fallback
    els.activeIncludeStatement.select();
    document.execCommand('copy');
    toast('Copied to clipboard!');
  });
}

async function autoInjectPreset() {
  if (!state.activePresetName) {
    toast('Select a preset first', 'error');
    return;
  }

  const template = els.selInjectionTemplate.value;
  const language = state.language || 'en';

  try {
    const result = await apiPost('/converted-presets/' + encodeURIComponent(state.activePresetName) + '/auto-insert', {
      language,
      template
    });

    if (result.alreadyExists) {
      toast('Include already exists in template', 'warning');
    } else {
      toast(`Injected "${state.activePresetName}" into ${template}`);
    }
    updateInjectionStatus();
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

async function removePresetInjection() {
  if (!state.activePresetName) {
    toast('Select a preset first', 'error');
    return;
  }

  const template = els.selInjectionTemplate.value;
  const language = state.language || 'en';

  try {
    const result = await apiPost('/converted-presets/' + encodeURIComponent(state.activePresetName) + '/remove-include', {
      language,
      template
    });

    if (result.notFound) {
      toast('Include not found in template', 'warning');
    } else {
      toast(`Removed "${state.activePresetName}" from ${template}`);
    }
    updateInjectionStatus();
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

async function updateInjectionStatus() {
  const statusEl = document.getElementById('injectionStatus');
  if (!state.activePresetName) {
    statusEl.textContent = 'No preset selected';
    statusEl.className = 'pill';
    return;
  }

  const template = els.selInjectionTemplate.value;
  const language = state.language || 'en';

  try {
    // Read the template and check if include exists
    const params = new URLSearchParams({
      source: 'live',
      language,
      category: 'TextGen',
      path: template.replace('TextGen/', '')
    });

    const data = await apiGet('/template?' + params);
    const safeName = state.activePresetName.replace(/[^a-zA-Z0-9\-_]/g, '-');
    const includePattern = `Presets/${safeName}/Main`;

    if (data.content && data.content.includes(includePattern)) {
      statusEl.textContent = 'Injected ✓';
      statusEl.className = 'pill success';
    } else {
      statusEl.textContent = 'Not injected';
      statusEl.className = 'pill';
    }
  } catch (err) {
    statusEl.textContent = 'Unknown';
    statusEl.className = 'pill warning';
  }
}

// ============ Presets Tab ============

async function loadPresetList() {
  try {
    const data = await apiGet('/presets');
    fillSelect(els.selPreset, data.items, state.presetName, '(select)');
  } catch (err) {
    console.error('Failed to load presets:', err);
  }
}

async function loadPreset(name) {
  if (!name) {
    currentPreset = null;
    promptOrder = [];
    renderPrompts();
    renderSampling();
    return;
  }

  try {
    currentPreset = await apiGet('/presets/' + encodeURIComponent(name));
    state.presetName = name;

    const globalOrder = currentPreset.prompt_order?.find(po => po.character_id === 100001);
    promptOrder = globalOrder?.order || [];

    renderPrompts();
    renderSampling();
  } catch (err) {
    console.error('Failed to load preset:', err);
    toast('Failed: ' + err.message, 'error');
  }
}

function renderPrompts() {
  const list = els.promptList;

  if (!currentPreset?.prompts?.length) {
    list.innerHTML = '<div class="empty-state"><h3>No Preset Loaded</h3><p>Import or select a preset.</p></div>';
    els.presetPromptCount.textContent = '0';
    els.presetEnabledCount.textContent = '0';
    return;
  }

  const orderMap = new Map(promptOrder.map((o, i) => [o.identifier, { ...o, index: i }]));
  const sortedPrompts = [...currentPreset.prompts].sort((a, b) => {
    const aIdx = orderMap.get(a.identifier)?.index ?? 999;
    const bIdx = orderMap.get(b.identifier)?.index ?? 999;
    return aIdx - bIdx;
  });

  let enabledCount = 0;
  list.innerHTML = '';

  for (const prompt of sortedPrompts) {
    if (prompt.marker) continue;

    const orderEntry = orderMap.get(prompt.identifier);
    const enabled = orderEntry?.enabled ?? false;
    if (enabled) enabledCount++;

    const item = document.createElement('div');
    item.className = 'prompt-item' + (enabled ? '' : ' disabled');

    item.innerHTML = `
      <span class="drag-handle">☰</span>
      <div class="toggle ${enabled ? 'enabled' : ''}" data-id="${prompt.identifier}"></div>
      <span class="prompt-name">${escapeHtml(prompt.name)}</span>
      <span class="role-badge ${prompt.role || 'system'}">${prompt.role || 'system'}</span>
      <button class="ghost prompt-delete" data-id="${prompt.identifier}">×</button>
    `;

    item.querySelector('.toggle').addEventListener('click', (e) => {
      e.stopPropagation();
      togglePrompt(prompt.identifier);
    });

    item.querySelector('.prompt-delete').addEventListener('click', (e) => {
      e.stopPropagation();
      deletePrompt(prompt.identifier);
    });

    item.addEventListener('click', () => editPrompt(prompt.identifier));

    list.appendChild(item);
  }

  els.presetPromptCount.textContent = sortedPrompts.filter(p => !p.marker).length;
  els.presetEnabledCount.textContent = enabledCount;
}

function togglePrompt(id) {
  const entry = promptOrder.find(o => o.identifier === id);
  if (entry) entry.enabled = !entry.enabled;
  updatePromptOrder();
  renderPrompts();
}

function deletePrompt(id) {
  if (!confirm('Delete this prompt?')) return;
  currentPreset.prompts = currentPreset.prompts.filter(p => p.identifier !== id);
  promptOrder = promptOrder.filter(o => o.identifier !== id);
  updatePromptOrder();
  renderPrompts();
  saveCurrentPreset();
}

function editPrompt(id) {
  const prompt = currentPreset.prompts.find(p => p.identifier === id);
  if (!prompt) return;

  els.promptEditorDialog.style.display = 'block';
  els.promptEditorDialog.dataset.id = id;
  document.getElementById('editPromptName').value = prompt.name;
  document.getElementById('editPromptRole').value = prompt.role || 'system';
  document.getElementById('editPromptContent').value = prompt.content || '';
}

function closePromptEditor() {
  els.promptEditorDialog.style.display = 'none';
}

function savePromptEdit() {
  const id = els.promptEditorDialog.dataset.id;
  const prompt = currentPreset.prompts.find(p => p.identifier === id);
  if (!prompt) return;

  prompt.name = document.getElementById('editPromptName').value;
  prompt.role = document.getElementById('editPromptRole').value;
  prompt.content = document.getElementById('editPromptContent').value;

  closePromptEditor();
  renderPrompts();
  saveCurrentPreset();
}

function updatePromptOrder() {
  const idx = currentPreset.prompt_order?.findIndex(po => po.character_id === 100001);
  if (idx >= 0) currentPreset.prompt_order[idx].order = promptOrder;
}

async function saveCurrentPreset() {
  if (!state.presetName || !currentPreset) return;

  try {
    await apiPut('/presets/' + encodeURIComponent(state.presetName), currentPreset);
    toast('Preset saved');
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

function renderSampling() {
  const grid = els.samplingGrid;
  grid.innerHTML = '';
  if (!currentPreset) return;

  const params = [
    { key: 'temperature', label: 'Temperature', step: 0.1 },
    { key: 'top_p', label: 'Top P', step: 0.01 },
    { key: 'top_k', label: 'Top K', step: 1 },
    { key: 'min_p', label: 'Min P', step: 0.01 },
    { key: 'frequency_penalty', label: 'Freq Penalty', step: 0.01 },
    { key: 'presence_penalty', label: 'Pres Penalty', step: 0.01 },
    { key: 'repetition_penalty', label: 'Rep Penalty', step: 0.01 },
    { key: 'openai_max_tokens', label: 'Max Tokens', step: 1 }
  ];

  for (const param of params) {
    const value = currentPreset[param.key] ?? '';
    const field = document.createElement('div');
    field.className = 'field';
    field.innerHTML = `<label>${param.label}</label><input type="number" step="${param.step}" data-key="${param.key}" value="${value}">`;
    field.querySelector('input').addEventListener('change', (e) => {
      currentPreset[param.key] = parseFloat(e.target.value) || 0;
    });
    grid.appendChild(field);
  }
}

function addPrompt() {
  const name = prompt('Prompt name:');
  if (!name) return;
  const role = prompt('Role (system/assistant/user):', 'system') || 'system';

  if (!currentPreset) {
    currentPreset = { prompts: [], prompt_order: [{ character_id: 100001, order: [] }], temperature: 1, top_p: 1 };
    promptOrder = [];
  }

  const id = 'p-' + Date.now();
  currentPreset.prompts.push({ identifier: id, name, role, content: '', marker: false });
  promptOrder.push({ identifier: id, enabled: true });
  updatePromptOrder();
  renderPrompts();
  saveCurrentPreset();
}

async function importPreset(file) {
  try {
    const text = await file.text();
    const preset = JSON.parse(text);
    if (!preset.prompts) throw new Error('Invalid format');

    const name = file.name.replace(/\.json$/i, '');
    await apiPost('/presets', { name, data: preset });

    // Refresh ALL preset dropdowns
    await Promise.all([loadPresetList(), loadConverterPresets()]);

    els.selPreset.value = name;
    await loadPreset(name);
    toast(`Imported ${preset.prompts.length} prompts`);
  } catch (err) {
    toast('Import failed: ' + err.message, 'error');
  }
}

function exportPreset() {
  if (!currentPreset || !state.presetName) {
    toast('No preset loaded', 'error');
    return;
  }
  const blob = new Blob([JSON.stringify(currentPreset, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = state.presetName + '.json';
  a.click();
  URL.revokeObjectURL(url);
}

async function deleteCurrentPreset() {
  if (!state.presetName) {
    toast('No preset selected', 'error');
    return;
  }
  if (!confirm(`Delete preset "${state.presetName}"? This cannot be undone.`)) {
    return;
  }
  try {
    await apiDelete('/presets/' + encodeURIComponent(state.presetName));
    toast('Preset deleted');
    state.presetName = null;
    currentPreset = null;
    promptOrder = [];
    await loadPresetList();
    await loadConverterPresets();
    renderPrompts();
    renderSampling();
  } catch (err) {
    toast('Delete failed: ' + err.message, 'error');
  }
}

// ============ Converter Tab ============

async function loadConverterPresets() {
  try {
    const data = await apiGet('/presets');
    fillSelect(els.converterPreset, data.items, null, '(select)');
  } catch (err) {
    console.error('Failed to load presets:', err);
  }
}

async function previewConversion() {
  const presetName = els.converterPreset.value;
  if (!presetName) {
    toast('Select a preset', 'error');
    return;
  }

  try {
    const preset = await apiGet('/presets/' + encodeURIComponent(presetName));
    const preview = [];
    preview.push(`# Conversion Preview: ${presetName}`);
    preview.push(`# Total prompts in preset: ${preset.prompts?.length || 0}`);
    preview.push('');

    // Get order info - fallback to using prompts directly if no order
    const globalOrder = preset.prompt_order?.find(po => po.character_id === 100001);
    const orderEntries = globalOrder?.order || [];
    const orderMap = new Map(orderEntries.map((o, i) => [o.identifier, { ...o, index: i }]));

    // Build list of prompts to convert - more flexible filtering
    let promptsToConvert = [];

    if (orderEntries.length > 0) {
      // Use order if available
      promptsToConvert = (preset.prompts || [])
        .filter(p => {
          if (p.marker) return false;
          const orderEntry = orderMap.get(p.identifier);
          // Include if enabled in order, OR if not in order at all but has content
          return orderEntry?.enabled || (!orderEntry && p.content);
        })
        .sort((a, b) => {
          const aIdx = orderMap.get(a.identifier)?.index ?? 999;
          const bIdx = orderMap.get(b.identifier)?.index ?? 999;
          return aIdx - bIdx;
        });
    } else {
      // No order info - just use all non-marker prompts with content
      promptsToConvert = (preset.prompts || [])
        .filter(p => !p.marker && (p.content || p.enabled));
    }

    preview.push(`# Prompts to convert: ${promptsToConvert.length}`);
    preview.push('');

    if (promptsToConvert.length === 0) {
      preview.push('## No prompts found to convert!');
      preview.push('');
      preview.push('This could mean:');
      preview.push('- All prompts are disabled in prompt_order');
      preview.push('- All prompts are markers (not actual content)');
      preview.push('- The preset has no prompts with content');
      preview.push('');
      preview.push('Available prompts in this preset:');
      for (const p of (preset.prompts || [])) {
        const orderEntry = orderMap.get(p.identifier);
        const status = p.marker ? 'MARKER' : (orderEntry?.enabled ? 'ENABLED' : 'DISABLED');
        const hasContent = p.content ? `(${p.content.length} chars)` : '(no content)';
        preview.push(`  - ${p.name}: ${status} ${hasContent}`);
      }
    } else {
      for (const p of promptsToConvert) {
        const safeName = sanitizeFileName(p.name);
        preview.push(`## ${p.name} [${p.role || 'system'}]`);
        preview.push(`File: Presets/${presetName}/${safeName}.scriban`);
        preview.push('```scriban');
        preview.push(convertContent(p.content, p.name, presetName));
        preview.push('```');
        preview.push('');
      }
    }

    els.conversionPreview.textContent = preview.join('\n');
    els.btnConvert.disabled = promptsToConvert.length === 0;
  } catch (err) {
    console.error('Preview failed:', err);
    toast('Preview failed: ' + err.message, 'error');
  }
}

function sanitizeFileName(name) {
  if (!name) return 'unnamed';
  let cleaned = name.trim();
  // Remove ALL non-ASCII characters first (handles all Unicode decorative chars)
  cleaned = cleaned.replace(/[^\x00-\x7F]/g, '');
  // Keep only ASCII alphanumeric, spaces, hyphens, underscores
  cleaned = cleaned.replace(/[^a-zA-Z0-9\s\-_]/g, '');
  // Collapse multiple spaces to single space
  cleaned = cleaned.replace(/\s+/g, ' ').trim();
  // Replace spaces with hyphens
  cleaned = cleaned.replace(/ /g, '-');
  // Remove consecutive hyphens
  cleaned = cleaned.replace(/-+/g, '-');
  // Remove leading/trailing hyphens
  cleaned = cleaned.replace(/^-+|-+$/g, '');
  return cleaned || 'unnamed';
}

function convertContent(content, promptName, presetName) {
  if (!content || typeof content !== 'string') return '(empty prompt - no content)';

  let c = content;

  // Remove SillyTavern comments {{// ... }} - non-greedy match for multi-line
  c = c.replace(/\{\{\/\/[\s\S]*?\}\}/g, '');

  // Remove {{trim}} markers
  c = c.replace(/\{\{trim\}\}/gi, '');

  // Extract content from {{setvar::name::value}} - keep ONLY the value part
  c = c.replace(/\{\{setvar::[^:]+::([\s\S]*?)\}\}/gi, '$1');

  // Replace {{getvar::name}} with Scriban variable placeholder
  c = c.replace(/\{\{getvar::([^}]+)\}\}/gi, '{{ $1 | default: "" }}');

  // Character/user variables - map to Voxta Scriban variables
  c = c.replace(/\{\{char\}\}/gi, '{{ char }}');
  c = c.replace(/\{\{charname\}\}/gi, '{{ char }}');
  c = c.replace(/\{\{user\}\}/gi, '{{ user }}');
  c = c.replace(/\{\{username\}\}/gi, '{{ user }}');
  c = c.replace(/\{\{group\}\}/gi, '{{ other_chars | array.join ", " }}');

  // Character details - map to Voxta variables
  c = c.replace(/\{\{scenario\}\}/gi, '{{ scenario }}');
  c = c.replace(/\{\{personality\}\}/gi, '{{ char_personality | join_newlines }}');
  c = c.replace(/\{\{description\}\}/gi, '{{ char_description | join_newlines }}');
  c = c.replace(/\{\{persona\}\}/gi, '{{ user_description }}');
  c = c.replace(/\{\{summary\}\}/gi, '{{ summary }}');
  c = c.replace(/\{\{mesExamples\}\}/gi, '{{ char_message_examples | join_newlines }}');
  c = c.replace(/\{\{message_examples\}\}/gi, '{{ char_message_examples | join_newlines }}');

  // SillyTavern special tokens - map to Voxta equivalents
  c = c.replace(/<BOT>/gi, '{{ char }}');
  c = c.replace(/<USER>/gi, '{{ user }}');

  // Time/date variables - map to Voxta's now variable
  c = c.replace(/\{\{time\}\}/gi, '{{ now }}');
  c = c.replace(/\{\{date\}\}/gi, '{{ now }}');
  c = c.replace(/\{\{weekday\}\}/gi, '{{ now }}');
  c = c.replace(/\{\{isotime\}\}/gi, '{{ now }}');
  c = c.replace(/\{\{isodate\}\}/gi, '{{ now }}');

  // Remove/comment unsupported macros
  c = c.replace(/\{\{roll:[^}]*\}\}/gi, '{{~ # dice roll macro not supported ~}}');
  c = c.replace(/\{\{random::[^}]*\}\}/gi, '{{~ # random selection macro not supported ~}}');
  c = c.replace(/\{\{newline\}\}/gi, '\n');
  c = c.replace(/\{\{addvar::[^}]*\}\}/gi, '');
  c = c.replace(/\{\{idle_duration\}\}/gi, '');

  // Clean up remaining unsupported ST macros
  c = c.replace(/\{\{[a-z_]+\}\}/gi, '');

  // Clean up empty braces and excessive newlines
  c = c.replace(/\{\{\s*\}\}/g, '');
  c = c.replace(/\n{3,}/g, '\n\n');

  // Add header comment
  const header = `{{~ # From: ${presetName} / ${promptName} ~}}`;
  const result = header + '\n' + c.trim();

  return result;
}

async function runConversion() {
  const presetName = els.converterPreset.value;
  if (!presetName) return;

  const targetLive = document.getElementById('converterTargetLive').checked;
  const language = els.converterLanguage.value || 'en';

  try {
    const result = await apiPost('/presets/' + encodeURIComponent(presetName) + '/convert', { targetLive, language });
    const target = targetLive ? 'Live templates' : 'Collection';
    toast(`Converted ${result.files?.length || 0} files to ${target}`);

    // Refresh all relevant lists including Active Presets
    await Promise.all([loadCollections(), loadCategories(), loadConvertedPresets(), loadActivePresetList()]);

    // Auto-select the newly converted preset in Active Presets tab
    if (targetLive) {
      state.activePresetName = presetName;
      els.selActivePreset.value = presetName;
      await loadActivePreset(presetName);
    }
  } catch (err) {
    toast('Failed: ' + err.message, 'error');
  }
}

// ============ Initialization ============

async function init() {
  // Cache elements - Active Presets tab
  els.selActivePreset = document.getElementById('selActivePreset');
  els.activePresetStatus = document.getElementById('activePresetStatus');
  els.activePromptTotal = document.getElementById('activePromptTotal');
  els.activePromptEnabled = document.getElementById('activePromptEnabled');
  els.activePromptList = document.getElementById('activePromptList');
  els.activeIncludeStatement = document.getElementById('activeIncludeStatement');
  els.selInjectionTemplate = document.getElementById('selInjectionTemplate');

  // Cache elements - Templates tab
  els.selSource = document.getElementById('selSource');
  els.selCollection = document.getElementById('selCollection');
  els.selLanguage = document.getElementById('selLanguage');
  els.selCategory = document.getElementById('selCategory');
  els.selTemplate = document.getElementById('selTemplate');
  els.templateEditor = document.getElementById('templateEditor');
  els.templatePath = document.getElementById('templatePath');
  els.templateStatus = document.getElementById('templateStatus');
  els.txtNewCollection = document.getElementById('txtNewCollection');
  els.selConvertedPreset = document.getElementById('selConvertedPreset');

  // Cache elements - Presets tab
  els.selPreset = document.getElementById('selPreset');
  els.promptList = document.getElementById('promptList');
  els.presetPromptCount = document.getElementById('presetPromptCount');
  els.presetEnabledCount = document.getElementById('presetEnabledCount');
  els.samplingGrid = document.getElementById('samplingGrid');
  els.promptEditorDialog = document.getElementById('promptEditorDialog');

  // Cache elements - Converter tab
  els.converterPreset = document.getElementById('converterPreset');
  els.converterLanguage = document.getElementById('converterLanguage');
  els.conversionPreview = document.getElementById('conversionPreview');
  els.btnConvert = document.getElementById('btnConvert');

  // Tab switching
  document.querySelectorAll('.tab').forEach(tab => {
    tab.addEventListener('click', () => {
      document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
      document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
      tab.classList.add('active');
      document.getElementById(tab.dataset.tab + '-panel').classList.add('active');
    });
  });

  // Active Presets events
  els.selActivePreset.addEventListener('change', async () => {
    await loadActivePreset(els.selActivePreset.value);
    updateInjectionStatus();
  });
  document.getElementById('btnRefreshActivePreset').addEventListener('click', () => {
    loadActivePresetList();
    if (state.activePresetName) loadActivePreset(state.activePresetName);
  });
  document.getElementById('btnEnableAll').addEventListener('click', enableAllActivePrompts);
  document.getElementById('btnDisableAll').addEventListener('click', disableAllActivePrompts);
  document.getElementById('btnCopyInclude').addEventListener('click', copyIncludeStatement);
  document.getElementById('btnAutoInject').addEventListener('click', autoInjectPreset);
  document.getElementById('btnRemoveInjection').addEventListener('click', removePresetInjection);
  els.selInjectionTemplate.addEventListener('change', updateInjectionStatus);

  document.getElementById('activePromptSearch').addEventListener('input', (e) => {
    const q = e.target.value.toLowerCase();
    document.querySelectorAll('#activePromptList .prompt-item').forEach(item => {
      const name = item.querySelector('.prompt-name')?.textContent.toLowerCase() || '';
      item.style.display = name.includes(q) ? '' : 'none';
    });
  });

  // Templates events
  els.selSource.addEventListener('change', () => {
    state.source = els.selSource.value;
    els.selCollection.disabled = state.source === 'live';
    if (state.source === 'live') {
      state.collection = null;
      els.selCollection.value = '';
    }
    loadCategories();
  });

  els.selCollection.addEventListener('change', () => {
    state.collection = els.selCollection.value || null;
    loadCategories();
  });

  els.selLanguage.addEventListener('change', () => {
    state.language = els.selLanguage.value;
    loadCategories();
  });

  els.selCategory.addEventListener('change', () => {
    state.category = els.selCategory.value;
    loadTemplates();
  });

  els.selTemplate.addEventListener('change', () => {
    state.template = els.selTemplate.value;
    loadTemplate();
  });

  document.getElementById('btnTemplateSave').addEventListener('click', saveTemplate);
  document.getElementById('btnTemplateReload').addEventListener('click', loadTemplate);
  document.getElementById('btnCreateCollection').addEventListener('click', createCollection);
  document.getElementById('btnApplyCollection').addEventListener('click', applyCollection);
  document.getElementById('btnRestoreOriginals').addEventListener('click', restoreOriginals);
  document.getElementById('btnInsertInclude').addEventListener('click', insertIncludeAtCursor);
  document.getElementById('btnRefreshConverted').addEventListener('click', loadConvertedPresets);

  els.templateEditor.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.key === 's') {
      e.preventDefault();
      saveTemplate();
    }
  });

  // Presets events
  els.selPreset.addEventListener('change', () => loadPreset(els.selPreset.value));
  document.getElementById('btnPresetSave').addEventListener('click', saveCurrentPreset);
  document.getElementById('btnPresetExport').addEventListener('click', exportPreset);
  document.getElementById('btnPresetDelete').addEventListener('click', deleteCurrentPreset);
  document.getElementById('btnAddPrompt').addEventListener('click', addPrompt);

  document.getElementById('btnPresetImport').addEventListener('click', () => {
    document.getElementById('filePresetImport').click();
  });
  document.getElementById('filePresetImport').addEventListener('change', (e) => {
    if (e.target.files[0]) {
      importPreset(e.target.files[0]);
      e.target.value = '';
    }
  });

  document.getElementById('btnClosePromptEditor').addEventListener('click', closePromptEditor);
  document.getElementById('btnSavePromptEdit').addEventListener('click', savePromptEdit);

  document.getElementById('promptSearch').addEventListener('input', (e) => {
    const q = e.target.value.toLowerCase();
    document.querySelectorAll('.prompt-item').forEach(item => {
      const name = item.querySelector('.prompt-name')?.textContent.toLowerCase() || '';
      item.style.display = name.includes(q) ? '' : 'none';
    });
  });

  document.getElementById('btnSaveSampling').addEventListener('click', saveCurrentPreset);

  // Converter events
  els.converterPreset.addEventListener('change', () => {
    els.conversionPreview.textContent = 'Select a preset and click Preview';
    els.btnConvert.disabled = true;
  });
  document.getElementById('btnPreviewConversion').addEventListener('click', previewConversion);
  els.btnConvert.addEventListener('click', runConversion);

  // Check connection
  try {
    await apiGet('/languages');
    document.getElementById('statusDot').classList.add('online');
    document.getElementById('statusText').textContent = 'Connected';
  } catch {
    document.getElementById('statusDot').classList.add('offline');
    document.getElementById('statusText').textContent = 'Offline';
  }

  // Load data
  await Promise.all([loadLanguages(), loadCollections(), loadPresetList(), loadConverterPresets(), loadConvertedPresets(), loadActivePresetList()]);
  await loadCategories();

  document.getElementById('loadingScreen').classList.add('hidden');
}

document.addEventListener('DOMContentLoaded', init);
