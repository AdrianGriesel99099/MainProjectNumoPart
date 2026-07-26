// wwwroot/js/multiselect.js
(function () {
    const selected = new Set();
    const bar = document.getElementById('selection-bar');
    if (!bar) return; // Page has no photos to select (e.g. a brand-new vehicle).

    const countEl = document.getElementById('selection-count');
    const form = document.getElementById('selection-download-form');

    function refreshBar() {
        if (selected.size === 0) {
            bar.classList.add('d-none');
            return;
        }
        bar.classList.remove('d-none');
        countEl.textContent = `${selected.size} selected`;

        form.querySelectorAll('input[name="ids"]').forEach(el => el.remove());
        selected.forEach(id => {
            const input = document.createElement('input');
            input.type = 'hidden';
            input.name = 'ids';
            input.value = id;
            form.appendChild(input);
        });
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

    document.getElementById('selection-clear')?.addEventListener('click', () => {
        selected.clear();
        document.querySelectorAll('.photo-tile.selected').forEach(t => t.classList.remove('selected'));
        refreshBar();
    });
})();
