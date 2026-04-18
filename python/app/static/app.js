const API = '';
let currentScreenshot = null;
let currentImage = null;
let templates = [];
let overlays = [];

// Selection state
let isSelecting = false;
let selStart = { x: 0, y: 0 };
let selEnd = { x: 0, y: 0 };

const canvas = document.getElementById('canvas');
const ctx = canvas.getContext('2d');

// ========== Init ==========
window.addEventListener('load', () => {
    refreshAll();
});

async function refreshAll() {
    await Promise.all([loadScreenshots(), loadTemplates()]);
    setStatus('就绪');
}

function setStatus(msg) {
    document.getElementById('status-bar').textContent = msg;
}

// ========== Screenshots ==========
async function loadScreenshots() {
    const res = await fetch(API + '/api/screenshots');
    const data = await res.json();
    const list = document.getElementById('screenshot-list');
    list.innerHTML = '';
    for (const ss of data.screenshots) {
        const img = document.createElement('img');
        img.className = 'ss-thumb';
        img.src = API + ss.url;
        img.title = ss.filename;
        img.dataset.filename = ss.filename;
        img.onclick = () => selectScreenshot(ss.filename, img);
        list.appendChild(img);
    }
}

function selectScreenshot(filename, thumbEl) {
    document.querySelectorAll('.ss-thumb').forEach(el => el.classList.remove('active'));
    if (thumbEl) thumbEl.classList.add('active');
    currentScreenshot = filename;
    overlays = [];

    const img = new Image();
    img.onload = () => {
        currentImage = img;
        canvas.width = img.width;
        canvas.height = img.height;
        canvas.style.display = 'block';
        document.getElementById('viewer-placeholder').style.display = 'none';
        drawCanvas();
    };
    img.src = API + '/api/screenshots/' + filename;
}

// ========== Templates ==========
async function loadTemplates() {
    const res = await fetch(API + '/api/templates');
    const data = await res.json();
    templates = data.templates || [];
    document.getElementById('template-count').textContent = `模板库 (${data.count})`;
    renderTemplateList();
}

function renderTemplateList() {
    const query = (document.getElementById('search-input').value || '').toLowerCase();
    const list = document.getElementById('template-list');
    list.innerHTML = '';
    for (const t of templates) {
        if (query && !t.name.toLowerCase().includes(query) && !t.category.toLowerCase().includes(query)) continue;
        const card = document.createElement('div');
        card.className = 'tpl-card';
        card.innerHTML = `
            <img src="${API}${t.image_url}" alt="${t.name}">
            <div class="tpl-card-info">
                <div class="name">${t.name}</div>
                <div class="meta">${t.category} | ${t.threshold}</div>
            </div>
            <span class="tpl-delete" title="删除" onclick="event.stopPropagation(); deleteTemplate('${t.name}')">&times;</span>
        `;
        list.appendChild(card);
    }
}

function filterTemplates() { renderTemplateList(); }

async function deleteTemplate(name) {
    if (!confirm(`确定删除模板 "${name}"？`)) return;
    await fetch(API + `/api/templates/${name}`, { method: 'DELETE' });
    await loadTemplates();
    setStatus(`已删除模板: ${name}`);
}

// ========== Canvas Drawing ==========
function drawCanvas() {
    if (!currentImage) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(currentImage, 0, 0);

    for (const o of overlays) {
        ctx.strokeStyle = o.color;
        ctx.lineWidth = 2;
        ctx.strokeRect(o.x, o.y, o.w, o.h);

        if (o.label) {
            ctx.font = '12px monospace';
            const m = ctx.measureText(o.label);
            const lx = o.x;
            const ly = o.y - 4;
            ctx.fillStyle = 'rgba(0,0,0,0.7)';
            ctx.fillRect(lx, ly - 12, m.width + 6, 15);
            ctx.fillStyle = o.color;
            ctx.fillText(o.label, lx + 3, ly);
        }
    }

    if (isSelecting) {
        const rx = Math.min(selStart.x, selEnd.x);
        const ry = Math.min(selStart.y, selEnd.y);
        const rw = Math.abs(selEnd.x - selStart.x);
        const rh = Math.abs(selEnd.y - selStart.y);
        ctx.strokeStyle = '#e94560';
        ctx.lineWidth = 2;
        ctx.setLineDash([6, 3]);
        ctx.strokeRect(rx, ry, rw, rh);
        ctx.setLineDash([]);
    }
}

// ========== Canvas Mouse Events ==========
canvas.addEventListener('mousedown', (e) => {
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / rect.width;
    const scaleY = canvas.height / rect.height;
    selStart.x = Math.round((e.clientX - rect.left) * scaleX);
    selStart.y = Math.round((e.clientY - rect.top) * scaleY);
    selEnd.x = selStart.x;
    selEnd.y = selStart.y;
    isSelecting = true;
});

canvas.addEventListener('mousemove', (e) => {
    if (!isSelecting) return;
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / rect.width;
    const scaleY = canvas.height / rect.height;
    selEnd.x = Math.round((e.clientX - rect.left) * scaleX);
    selEnd.y = Math.round((e.clientY - rect.top) * scaleY);
    drawCanvas();
});

canvas.addEventListener('mouseup', (e) => {
    if (!isSelecting) return;
    isSelecting = false;
    const rect = canvas.getBoundingClientRect();
    const scaleX = canvas.width / rect.width;
    const scaleY = canvas.height / rect.height;
    selEnd.x = Math.round((e.clientX - rect.left) * scaleX);
    selEnd.y = Math.round((e.clientY - rect.top) * scaleY);

    const rx = Math.min(selStart.x, selEnd.x);
    const ry = Math.min(selStart.y, selEnd.y);
    const rw = Math.abs(selEnd.x - selStart.x);
    const rh = Math.abs(selEnd.y - selStart.y);

    if (rw > 5 && rh > 5) {
        showCreateModal(rx, ry, rw, rh);
    }
    drawCanvas();
});

// ========== Create Modal ==========
function showCreateModal(x, y, w, h) {
    document.getElementById('tpl-name').value = '';
    document.getElementById('tpl-desc').value = '';
    document.getElementById('tpl-threshold').value = '0.80';
    document.getElementById('tpl-category').value = 'button';

    const preview = document.getElementById('crop-preview');
    const pctx = preview.getContext('2d');
    const scale = Math.min(128 / w, 128 / h, 2);
    const dw = Math.round(w * scale);
    const dh = Math.round(h * scale);
    preview.width = dw;
    preview.height = dh;
    pctx.drawImage(currentImage, x, y, w, h, 0, 0, dw, dh);

    window._pendingCrop = { x, y, width: w, height: h };
    document.getElementById('modal-overlay').classList.remove('hidden');
}

function closeModal() {
    document.getElementById('modal-overlay').classList.add('hidden');
}

async function saveTemplate() {
    const name = document.getElementById('tpl-name').value.trim();
    if (!name) { alert('请输入模板名称'); return; }
    if (!currentScreenshot) { alert('请先选择截图'); return; }

    const body = {
        name: name,
        category: document.getElementById('tpl-category').value,
        threshold: parseFloat(document.getElementById('tpl-threshold').value),
        description: document.getElementById('tpl-desc').value.trim(),
        screenshot: currentScreenshot,
        region: window._pendingCrop,
    };

    try {
        const res = await fetch(API + '/api/templates', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        const data = await res.json();
        if (res.ok && data.success) {
            closeModal();
            await loadTemplates();
            setStatus(`模板已创建: ${name}`);
        } else {
            alert('创建失败: ' + (data.detail || data.error || JSON.stringify(data)));
        }
    } catch (e) {
        alert('请求失败: ' + e.message);
    }
}

// ========== OCR / Match ==========
async function runOcr() {
    if (!currentScreenshot) { alert('请先选择截图'); return; }
    setStatus('正在运行 OCR...');

    const imgRes = await fetch(API + '/api/screenshots/' + currentScreenshot);
    const blob = await imgRes.blob();
    const form = new FormData();
    form.append('image', blob, currentScreenshot);

    const res = await fetch(API + '/api/ocr', { method: 'POST', body: form });
    const data = await res.json();
    if (data.success) {
        overlays = overlays.filter(o => o.type !== 'ocr');
        for (const t of data.texts) {
            overlays.push({
                type: 'ocr',
                x: t.bbox.x, y: t.bbox.y, w: t.bbox.width, h: t.bbox.height,
                color: '#4fc3f7',
                label: `${t.text} (${(t.confidence * 100).toFixed(0)}%)`,
            });
        }
        drawCanvas();
        setStatus(`OCR 完成: ${data.texts.length} 个文本, ${data.elapsed_ms}ms`);
    } else {
        setStatus('OCR 失败: ' + data.error);
    }
}

async function runMatchAll() {
    if (!currentScreenshot) { alert('请先选择截图'); return; }
    setStatus('正在匹配模板...');

    const imgRes = await fetch(API + '/api/screenshots/' + currentScreenshot);
    const blob = await imgRes.blob();
    const form = new FormData();
    form.append('image', blob, currentScreenshot);

    const res = await fetch(API + '/api/match', { method: 'POST', body: form });
    const data = await res.json();
    if (data.success) {
        overlays = overlays.filter(o => o.type !== 'match');
        for (const m of data.matches) {
            overlays.push({
                type: 'match',
                x: m.bbox.x, y: m.bbox.y, w: m.bbox.width, h: m.bbox.height,
                color: '#66bb6a',
                label: `[${m.template}] ${(m.confidence * 100).toFixed(0)}%`,
            });
        }
        drawCanvas();
        setStatus(`匹配完成: ${data.matches.length} 个匹配, ${data.elapsed_ms}ms`);
    } else {
        setStatus('匹配失败: ' + data.error);
    }
}

function clearOverlays() {
    overlays = [];
    drawCanvas();
    setStatus('已清除标注');
}
