// wwwroot/js/damagemarks.js
//
// Drives the damage-marking popup on Pages/Vehicles/Details.cshtml. Listens for 'carpartmarked'
// from the always-rendered "Mark damage" diagram (#damage-diagram, mode="mark" in
// cardiagram.js), opens a Bootstrap modal, and lets the user click a photo or a zoomed diagram
// shape to drop a pin — a "layer over it", never burned into the image itself.
(function () {
    const root = document.getElementById('damage-diagram');
    const modalEl = document.getElementById('damage-mark-modal');
    if (!root || !modalEl || typeof bootstrap === 'undefined') return;

    const modal = new bootstrap.Modal(modalEl);
    const titleEl = document.getElementById('damage-mark-title');
    const tabsEl = document.getElementById('damage-mark-tabs');
    const photoSelect = document.getElementById('damage-mark-photo-select');
    const canvasWrap = document.getElementById('damage-mark-canvas-wrap');
    const noteForm = document.getElementById('damage-mark-note-form');
    const noteInput = document.getElementById('damage-mark-note');
    const errorEl = document.getElementById('damage-mark-error');

    let currentPart = null;
    let currentTab = 'diagram';
    let currentPhotoId = null;
    let pendingClick = null; // { xPercent, yPercent } awaiting Save

    function marksFor(part, photoId) {
        return window.__damageMarksData.filter((m) =>
            m.part === part && (photoId ? m.photoId === photoId : m.photoId === null));
    }

    function photosFor(part) {
        return window.__vehiclePhotosData.filter((p) => p.part === part);
    }

    function clearPending() {
        pendingClick = null;
        noteForm.classList.add('d-none');
        canvasWrap.querySelectorAll('.damage-mark-dot.pending').forEach((d) => d.remove());
    }

    async function deleteMark(id) {
        if (!confirm('Delete this mark? This cannot be undone.')) return;
        const response = await fetch(`/api/vehicles/damage-marks/${id}`, { method: 'DELETE' });
        if (response.status === 204) {
            window.location.reload();
        } else {
            alert('Delete failed.');
        }
    }

    function renderDots(target, photoId) {
        target.querySelectorAll('.damage-mark-dot').forEach((d) => d.remove());
        marksFor(currentPart, photoId).forEach((m) => {
            const dot = document.createElement('div');
            dot.className = 'damage-mark-dot';
            dot.style.left = m.xPercent + '%';
            dot.style.top = m.yPercent + '%';
            dot.title = m.note + ' — ' + m.authorEmail;
            if (window.__isAdmin) {
                dot.addEventListener('click', (e) => { e.stopPropagation(); deleteMark(m.id); });
            }
            target.appendChild(dot);
        });
    }

    // A click anywhere on the target that ISN'T an existing dot starts a new pending mark.
    // Re-attached each time the tab/photo changes since canvasWrap's contents are replaced
    // wholesale, not patched in place.
    function attachClickToPlace(target) {
        if (!window.__canMarkDamage) return; // Viewers can look, not mark
        target.addEventListener('click', (e) => {
            if (e.target.classList.contains('damage-mark-dot')) return;
            const rect = target.getBoundingClientRect();
            const xPercent = ((e.clientX - rect.left) / rect.width) * 100;
            const yPercent = ((e.clientY - rect.top) / rect.height) * 100;
            pendingClick = { xPercent, yPercent };
            target.querySelectorAll('.damage-mark-dot.pending').forEach((d) => d.remove());
            const dot = document.createElement('div');
            dot.className = 'damage-mark-dot pending';
            dot.style.left = xPercent + '%';
            dot.style.top = yPercent + '%';
            target.appendChild(dot);
            noteForm.classList.remove('d-none');
            noteInput.value = '';
            errorEl.textContent = '';
            noteInput.focus();
        });
    }

    function renderNoVisual() {
        // Button-only parts (Interior, fenderliners, etc.) have no SVG shape and, if nothing is
        // tagged yet, no photo either — there is nothing sensible to click a pin onto. The
        // record itself (part, note, author, timestamp) is still the point, so marking still
        // works here; it just saves at a fixed centred position instead of a real pin.
        canvasWrap.innerHTML = '<div class="damage-mark-empty">No photo or diagram shape for this part yet — you can still record a note.</div>';
        if (!window.__canMarkDamage) return;
        pendingClick = { xPercent: 50, yPercent: 50 };
        noteForm.classList.remove('d-none');
        noteInput.value = '';
        errorEl.textContent = '';
    }

    function renderDiagramTab() {
        canvasWrap.innerHTML = '';
        // Any view works — the clone is measured once it's in THIS visible container, not the
        // source view, so it doesn't matter whether the source's own view is currently active.
        const source = root.querySelector('[data-part="' + currentPart + '"]');
        if (!source || source.tagName === 'BUTTON') { renderNoVisual(); return; }

        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.style.background = '#eceae3';
        const clone = source.cloneNode(true);
        clone.removeAttribute('class');
        clone.setAttribute('fill', '#cfd6da');
        clone.setAttribute('stroke', '#5f5e5a');
        clone.setAttribute('stroke-width', '1.5');
        svg.appendChild(clone);
        canvasWrap.appendChild(svg);

        // getBBox() needs the element to actually be laid out (display:block, attached) — true
        // here because the modal is only populated after 'shown.bs.modal' fires (see below).
        // Measuring the CLONE (now inside this visible popup) rather than the ORIGINAL source
        // sidesteps the fact that the source's own view might not be the diagram's active one,
        // where display:none would make getBBox() return a zero-size rect instead.
        const bbox = clone.getBBox();
        const pad = Math.max(bbox.width, bbox.height, 10) * 0.15;
        svg.setAttribute('viewBox',
            (bbox.x - pad) + ' ' + (bbox.y - pad) + ' ' + (bbox.width + pad * 2) + ' ' + (bbox.height + pad * 2));

        attachClickToPlace(canvasWrap);
        renderDots(canvasWrap, null);
    }

    function renderPhotoTab() {
        const photos = photosFor(currentPart);
        if (photos.length === 0) { renderNoVisual(); return; }

        if (photos.length > 1) {
            photoSelect.classList.remove('d-none');
            photoSelect.innerHTML = photos.map((p) => `<option value="${p.id}">${p.label}</option>`).join('');
            photoSelect.value = currentPhotoId && photos.some((p) => p.id === currentPhotoId) ? currentPhotoId : photos[0].id;
        } else {
            photoSelect.classList.add('d-none');
        }
        currentPhotoId = parseInt(photoSelect.value || photos[0].id, 10);

        canvasWrap.innerHTML = '';
        const img = document.createElement('img');
        img.alt = '';
        img.addEventListener('load', () => {
            attachClickToPlace(canvasWrap);
            renderDots(canvasWrap, currentPhotoId);
        });
        img.src = `/api/photos/${currentPhotoId}/thumbnail`;
        canvasWrap.appendChild(img);
    }

    function setTab(tab) {
        currentTab = tab;
        clearPending();
        tabsEl.querySelectorAll('button').forEach((b) => b.classList.toggle('active', b.dataset.tab === tab));
        if (tab === 'diagram') renderDiagramTab(); else renderPhotoTab();
    }

    tabsEl.querySelectorAll('button').forEach((btn) => {
        btn.addEventListener('click', () => setTab(btn.dataset.tab));
    });
    photoSelect.addEventListener('change', () => {
        currentPhotoId = parseInt(photoSelect.value, 10);
        renderPhotoTab();
    });

    root.addEventListener('carpartmarked', (e) => {
        currentPart = e.detail.part;
        titleEl.textContent = 'Mark damage — ' + currentPart;
        modal.show();
    });

    // Content is populated only once Bootstrap confirms the modal has finished its show
    // transition, not immediately on carpartmarked — getBBox() and image layout both need real,
    // visible layout to measure, and the modal is display:none right up until this event fires.
    modalEl.addEventListener('shown.bs.modal', () => {
        if (!currentPart) return;
        const hasPhotos = photosFor(currentPart).length > 0;
        setTab(hasPhotos ? 'photo' : 'diagram');
    });

    document.getElementById('damage-mark-cancel').addEventListener('click', clearPending);

    noteForm.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (!pendingClick) return;
        const response = await fetch(`/api/vehicles/${window.__vehicleId}/damage-marks`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                part: currentPart,
                photoId: currentTab === 'photo' ? currentPhotoId : null,
                xPercent: pendingClick.xPercent,
                yPercent: pendingClick.yPercent,
                note: noteInput.value
            })
        });
        // Exact status, not response.ok — a role-denied request answers 302 to
        // /Account/AccessDenied, which fetch follows to a 200 and would look like success.
        if (response.status === 200) {
            window.location.reload();
        } else {
            errorEl.textContent = (await response.text()) || 'Could not save the mark.';
        }
    });

    document.querySelectorAll('.damage-mark-delete').forEach((btn) => {
        btn.addEventListener('click', () => deleteMark(btn.dataset.markId));
    });
})();
