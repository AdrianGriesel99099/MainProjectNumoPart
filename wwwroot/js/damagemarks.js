// wwwroot/js/damagemarks.js
//
// Drives the damage-marking popup on Pages/Vehicles/Details.cshtml. Triggered by picking a part
// from #damage-mark-part-select and clicking #damage-mark-open, it shows a Bootstrap modal where
// clicking a tagged photo of that part drops a pin — a "layer over it", never burned into the
// image itself. A part with no tagged photo falls back to a note-only record at a fixed position,
// since there is nothing to click a pin onto.
(function () {
    const partSelect = document.getElementById('damage-mark-part-select');
    const openBtn = document.getElementById('damage-mark-open');
    const modalEl = document.getElementById('damage-mark-modal');
    if (!partSelect || !openBtn || !modalEl || typeof bootstrap === 'undefined') return;

    const modal = new bootstrap.Modal(modalEl);
    const titleEl = document.getElementById('damage-mark-title');
    const photoSelect = document.getElementById('damage-mark-photo-select');
    const canvasWrap = document.getElementById('damage-mark-canvas-wrap');
    const noteForm = document.getElementById('damage-mark-note-form');
    const noteInput = document.getElementById('damage-mark-note');
    const errorEl = document.getElementById('damage-mark-error');

    let currentPart = null;
    let currentPhotoId = null;
    let pendingClick = null; // { xPercent, yPercent } awaiting Save
    let hasPhoto = false; // whether the pending mark, if saved, should carry currentPhotoId or null

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
    // Re-attached each time the photo changes since canvasWrap's contents are replaced wholesale,
    // not patched in place.
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
        // Nothing tagged to this part yet — there is nothing sensible to click a pin onto. The
        // record itself (part, note, author, timestamp) is still the point, so marking still
        // works here; it just saves at a fixed centred position instead of a real pin.
        canvasWrap.innerHTML = '<div class="damage-mark-empty">No photo tagged to this part yet — you can still record a note.</div>';
        hasPhoto = false;
        if (!window.__canMarkDamage) return;
        pendingClick = { xPercent: 50, yPercent: 50 };
        noteForm.classList.remove('d-none');
        noteInput.value = '';
        errorEl.textContent = '';
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
        hasPhoto = true;

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

    photoSelect.addEventListener('change', () => {
        currentPhotoId = parseInt(photoSelect.value, 10);
        renderPhotoTab();
    });

    openBtn.addEventListener('click', () => {
        currentPart = partSelect.value;
        titleEl.textContent = 'Mark damage — ' + partSelect.options[partSelect.selectedIndex].textContent;
        modal.show();
    });

    // Content is populated only once Bootstrap confirms the modal has finished its show
    // transition, not immediately on click — image layout needs real, visible layout to measure,
    // and the modal is display:none right up until this event fires.
    modalEl.addEventListener('shown.bs.modal', () => {
        if (!currentPart) return;
        clearPending();
        photoSelect.classList.add('d-none');
        renderPhotoTab();
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
                photoId: hasPhoto ? currentPhotoId : null,
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
