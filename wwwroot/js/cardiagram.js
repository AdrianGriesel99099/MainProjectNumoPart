// wwwroot/js/cardiagram.js
//
// Drives _CarDiagram.cshtml. Two modes:
//   picker   — clicking a region selects that part; fires 'carpartselected'
//   coverage — regions with photos are shaded; clicking one fires 'carpartfiltered'
//
// A part drawn in several views (the roof appears in four) shares one data-part value, so
// selecting it updates every instance at once. That redundancy is also the bug detector: a
// mistyped data-part simply fails to light up alongside its siblings.
(function () {
    // front -> left -> rear -> right, so holding one arrow walks all the way round the car.
    const ORBIT = ['front', 'left', 'rear', 'right'];
    const LABELS = { front: 'Front', left: 'Left side', rear: 'Rear', right: 'Right side', top: 'Top' };

    function initDiagram(root) {
        const stage = root.querySelector('.car-stage');
        const nameEl = root.querySelector('.car-view-name');
        const views = root.querySelectorAll('.car-view');
        const regions = root.querySelectorAll('.cp, .cp-btn');
        const mode = root.dataset.mode || 'picker';

        let orbitIndex = ORBIT.indexOf('left');
        let current = 'left';
        let lastSide = 'left';
        let selected = null;

        function showView() {
            views.forEach(v => v.classList.toggle('active', v.dataset.view === current));
            if (nameEl) nameEl.textContent = LABELS[current];
            const onTop = current === 'top';
            // "Left of above" means nothing, so the horizontal arrows go away on the top view.
            root.querySelector('.car-nav-left').style.visibility = onTop ? 'hidden' : 'visible';
            root.querySelector('.car-nav-right').style.visibility = onTop ? 'hidden' : 'visible';
            root.querySelector('.car-nav-up').style.visibility = onTop ? 'hidden' : 'visible';
            root.querySelector('.car-nav-down').style.visibility = onTop ? 'visible' : 'hidden';
        }

        function paint() {
            regions.forEach(r => {
                r.classList.toggle('selected', selected !== null && r.dataset.part === selected);
            });
        }

        function select(part) {
            selected = part;
            paint();
            root.dispatchEvent(new CustomEvent(
                mode === 'coverage' ? 'carpartfiltered' : 'carpartselected',
                { bubbles: true, detail: { part: part } }));
        }

        regions.forEach(r => {
            r.addEventListener('click', () => select(r.dataset.part));
            if (r.classList.contains('cp')) {
                // SVG shapes are not focusable by default; without this the diagram is
                // mouse-only and the dropdown becomes the sole keyboard path.
                r.setAttribute('tabindex', '0');
                r.setAttribute('role', 'button');
                r.addEventListener('keydown', e => {
                    if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); select(r.dataset.part); }
                });
            }
        });

        stage.querySelectorAll('.car-nav').forEach(btn => {
            btn.addEventListener('click', () => {
                const dir = btn.dataset.nav;
                if (dir === 'up') { lastSide = current; current = 'top'; }
                else if (dir === 'down') { current = lastSide; }
                else {
                    orbitIndex = (orbitIndex + (dir === 'right' ? 1 : ORBIT.length - 1)) % ORBIT.length;
                    current = ORBIT[orbitIndex];
                    lastSide = current;
                }
                showView();
            });
        });

        // Coverage shading is applied from outside via this hook, so the partial stays a dumb
        // template and the page owns the photo counts.
        root.applyCoverage = function (partsWithPhotos) {
            const set = new Set(partsWithPhotos);
            regions.forEach(r => r.classList.toggle('covered', set.has(r.dataset.part)));
        };
        root.setSelected = function (part) { selected = part; paint(); };
        root.clearSelection = function () { selected = null; paint(); };

        showView();
    }

    document.querySelectorAll('.car-diagram').forEach(initDiagram);
})();
