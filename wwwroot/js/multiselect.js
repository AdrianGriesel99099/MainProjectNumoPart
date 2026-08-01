// wwwroot/js/multiselect.js
(function () {
    const selected = new Set();
    const bar = document.getElementById('selection-bar');
    if (!bar) return; // Page has no photos to select (e.g. a brand-new vehicle).

    const countEl = document.getElementById('selection-count');

    // Every form marked [data-selection-ids] receives the hidden id inputs, instead of the one
    // hard-coded download form this used to know about. That is what lets a second bulk action
    // (tagging a part) share the same selection without the two fighting over it.
    function syncForms() {
        document.querySelectorAll('[data-selection-ids]').forEach(form => {
            form.querySelectorAll('input[name="ids"]').forEach(el => el.remove());
            selected.forEach(id => {
                const input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'ids';
                input.value = id;
                form.appendChild(input);
            });
        });
    }

    function refreshBar() {
        if (selected.size === 0) {
            bar.classList.add('d-none');
        } else {
            bar.classList.remove('d-none');
            countEl.textContent = `${selected.size} selected`;
        }
        syncForms();
        // Actions driven by fetch (rather than a form POST) listen for this instead of polling.
        document.dispatchEvent(new CustomEvent('photoselectionchange', { detail: { count: selected.size } }));
    }

    function clearSelection() {
        selected.clear();
        document.querySelectorAll('.photo-tile.selected').forEach(t => t.classList.remove('selected'));
        refreshBar();
    }

    document.querySelectorAll('.photo-tile').forEach(tile => {
        tile.addEventListener('click', (e) => {
            if (e.target.classList.contains('photo-delete')) return; // let delete handle its own click
            const id = tile.dataset.photoId;
            if (selected.has(id)) {
                selected.delete(id);
                tile.classList.remove('selected');
            } else {
                selected.add(id);
                tile.classList.add('selected');
            }
            refreshBar();
        });
    });

    document.getElementById('selection-clear')?.addEventListener('click', clearSelection);

    // Exposed so a fetch-based action can read the selection directly rather than scraping
    // hidden inputs back out of a form it does not own. Ids are numbers here because the tagging
    // endpoint binds int[]; the dataset values are strings.
    window.PhotoSelection = {
        ids: () => Array.from(selected, id => parseInt(id, 10)),
        count: () => selected.size,
        clear: clearSelection
    };
})();
